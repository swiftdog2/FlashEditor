using System;
using System.IO;
using System.Threading;
using static FlashEditor.Utils.DebugUtil;

namespace FlashEditor.cache {
    /// <summary>
    ///     Asks a running Hydra JS5 update server to let go of the cache files before the editor
    ///     writes over them, and tells it when the write is done.
    /// </summary>
    /// <remarks>
    ///     A file watcher on the server side cannot do this job. The server opens the dat2 and every
    ///     idx read-only and holds them for the life of the process, and Java does not open them
    ///     with <c>FILE_SHARE_DELETE</c>, so on Windows <c>RSFileStore.SaveTo</c> cannot promote its
    ///     staged files over the originals while the server runs. The write fails, the files never
    ///     change, and a watcher waiting for a change waits forever. The release has to happen
    ///     before the write, which means the editor has to ask first.
    ///     <para>
    ///     The protocol is three files in the cache directory and is documented on the server side
    ///     by <c>CacheWatcher.java</c>, which is the other half of it:
    ///     </para>
    ///     <list type="number">
    ///         <item>the editor creates <c>reload.request</c>;</item>
    ///         <item>the watcher closes every handle and creates <c>reload.released</c>;</item>
    ///         <item>the editor waits for <c>reload.released</c>, writes the cache, then deletes
    ///             <c>reload.request</c>;</item>
    ///         <item>the watcher reloads and deletes <c>reload.released</c>.</item>
    ///     </list>
    ///     <para>
    ///     Between the second and fourth steps the server can serve nothing, so this is a
    ///     maintenance window rather than a live-server feature. That is why the setting that
    ///     enables it is off by default: pointed at a cache no server is serving, every save would
    ///     stall for the whole timeout and then refuse to write.
    ///     </para>
    ///     <para>
    ///     What this cannot fix, and what to check first if it appears to do nothing: a handle the
    ///     server does not release, or does not know it holds. A <b>read-write</b> handle anywhere
    ///     in that process stops the editor opening the cache at all - <c>StagedDataChannel.Open</c>
    ///     asks for <c>FileShare.Read</c> - so the failure lands in <c>RSFileStore</c>'s constructor
    ///     before any edit exists to save, and no handshake can help. Measured against the Hydra
    ///     update server, where <c>JS5Decoder</c> held an unread <c>FileServer</c> that opened a
    ///     second copy of the whole cache in mode <c>rw</c> on the first client handshake and never
    ///     closed it; fixed there rather than here.
    ///     </para>
    /// </remarks>
    public static class JS5ReloadHandshake {
        /// <summary>The file the editor creates to ask for the handles to be released.</summary>
        public const string RequestFileName = "reload.request";

        /// <summary>The file the server creates once its handles are shut.</summary>
        public const string ReleasedFileName = "reload.released";

        /// <summary>How often the released marker is looked for.</summary>
        /// <remarks>
        ///     Below the server's own 500 ms poll, so the round trip is bounded by the server's
        ///     cycle rather than by this one.
        /// </remarks>
        private static readonly TimeSpan PollInterval = TimeSpan.FromMilliseconds(100);

        /// <summary>
        ///     Runs <paramref name="save"/> inside the handshake: request, wait, save, withdraw the
        ///     request.
        /// </summary>
        /// <remarks>
        ///     Every exit path removes <c>reload.request</c>, including the timeout and a failed
        ///     save. Leaving it behind would strand the server with its handles shut and no cache
        ///     open, which looks exactly like a crash and is not recoverable without deleting a file
        ///     by hand.
        ///     <para>
        ///     A <c>reload.released</c> already present when this starts is deleted rather than
        ///     believed. It can only be residue from an editor that died mid-handshake, and taking
        ///     it as the answer would start the write while the server still held the files - which
        ///     is the exact failure the handshake exists to prevent.
        ///     </para>
        ///     <para>
        ///     The save does not run when the wait times out. On Windows it would fail with a
        ///     sharing violation anyway, and when nothing is serving the directory a silent success
        ///     would teach the user that the handshake works when it has not run at all.
        ///     </para>
        /// </remarks>
        /// <param name="cacheDirectory">The cache directory being written, which the server watches.</param>
        /// <param name="timeout">How long to wait for the server to release its handles.</param>
        /// <param name="save">The write to perform once the handles are shut.</param>
        /// <exception cref="ArgumentNullException">No directory or no save action was given.</exception>
        /// <exception cref="TimeoutException">The server did not release within <paramref name="timeout"/>.</exception>
        public static void Run(string cacheDirectory, TimeSpan timeout, Action save) {
            if (string.IsNullOrWhiteSpace(cacheDirectory))
                throw new ArgumentNullException(nameof(cacheDirectory));
            if (save == null)
                throw new ArgumentNullException(nameof(save));

            string request = Path.Combine(cacheDirectory, RequestFileName);
            string released = Path.Combine(cacheDirectory, ReleasedFileName);

            DiscardStaleMarker(released);

            Debug("JS5 reload: asking the server to release " + cacheDirectory);
            File.WriteAllText(request, Signature());

            try {
                if (!WaitForRelease(released, timeout))
                    throw new TimeoutException(
                        "No JS5 update server released the cache within " + (int) timeout.TotalSeconds
                        + " seconds." + Environment.NewLine + Environment.NewLine
                        + "Nothing was written. The editor asked by creating " + RequestFileName
                        + " in:" + Environment.NewLine + cacheDirectory + Environment.NewLine
                        + "and waited for " + ReleasedFileName + ", which never appeared."
                        + Environment.NewLine + Environment.NewLine
                        + "Either no server is serving this cache - in which case turn the JS5 live"
                        + " reload handshake off and save again - or the one that is has stopped"
                        + " watching the directory.");

                Debug("JS5 reload: handles released, writing the cache");
                save();
            }
            finally {
                /* The server reloads on the request going away, so this both completes a successful
                   handshake and unwinds a failed one. Delete before the caller sees an exception:
                   the editor stays usable either way, but a server left with its handles shut does
                   not. */
                Withdraw(request);
            }
        }

        /// <summary>
        ///     Runs a save through the handshake when the user has enabled it, and directly when
        ///     they have not.
        /// </summary>
        /// <remarks>
        ///     The one place the persisted setting is read, so every save path gets the same answer.
        ///     Off by default: the handshake only makes sense pointed at a cache a live server is
        ///     serving, and everywhere else it would cost every save the full timeout and then
        ///     refuse to write.
        /// </remarks>
        /// <param name="cacheDirectory">The directory being written.</param>
        /// <param name="save">The write to perform.</param>
        /// <exception cref="TimeoutException">The handshake is on and no server released the cache.</exception>
        public static void AroundSave(string cacheDirectory, Action save) {
            if (!Properties.Settings.Default.js5LiveReload) {
                save();
                return;
            }

            //Clamped rather than trusted: the setting is user-editable and a zero or negative
            //timeout would turn the wait into a single existence check, which is the stale-marker
            //failure with extra steps.
            int seconds = Math.Max(1, Properties.Settings.Default.js5LiveReloadTimeoutSeconds);
            Run(cacheDirectory, TimeSpan.FromSeconds(seconds), save);
        }

        /// <summary>
        ///     Removes a released marker left over from an earlier handshake.
        /// </summary>
        /// <remarks>
        ///     Best effort. A marker that cannot be deleted is reported and the handshake goes on to
        ///     ask anyway: the wait below is what decides whether the server answered, and a stale
        ///     marker that survives makes the wait return immediately, which is a visible failure
        ///     rather than a silent one.
        /// </remarks>
        /// <param name="released">The full path of the released marker.</param>
        private static void DiscardStaleMarker(string released) {
            try {
                if (!File.Exists(released))
                    return;

                Debug("JS5 reload: discarding a stale " + ReleasedFileName);
                File.Delete(released);
            } catch (Exception ex) {
                Debug("JS5 reload: could not discard " + released + ": " + ex.Message);
            }
        }

        /// <summary>Waits for the server's released marker to appear.</summary>
        /// <param name="released">The full path of the released marker.</param>
        /// <param name="timeout">How long to wait before giving up.</param>
        /// <returns>Whether the marker appeared in time.</returns>
        private static bool WaitForRelease(string released, TimeSpan timeout) {
            DateTime deadline = DateTime.UtcNow + timeout;

            while (true) {
                if (File.Exists(released))
                    return true;

                if (DateTime.UtcNow >= deadline)
                    return false;

                Thread.Sleep(PollInterval);
            }
        }

        /// <summary>
        ///     Deletes the request file, which is what tells the server to reload.
        /// </summary>
        /// <remarks>
        ///     A failure here is worth saying out loud rather than throwing over: by the time this
        ///     runs the cache is already written, so replacing that outcome with an exception would
        ///     report a successful save as a failed one. The server keeps polling, so deleting the
        ///     file by hand still completes the reload.
        /// </remarks>
        /// <param name="request">The full path of the request file.</param>
        private static void Withdraw(string request) {
            try {
                if (File.Exists(request))
                    File.Delete(request);
                Debug("JS5 reload: request withdrawn, the server may reload");
            } catch (Exception ex) {
                Debug("JS5 reload: could not delete " + request + ": " + ex.Message
                    + ". Delete it by hand or the server will not reload.");
            }
        }

        /// <summary>What goes inside the request file, for a human looking at a stranded one.</summary>
        /// <remarks>
        ///     The server only tests whether the file exists, so the contents are free. A stranded
        ///     request is the one failure mode that needs a person to intervene, and it should say
        ///     who left it and when.
        /// </remarks>
        /// <returns>A one-line description of this editor session.</returns>
        private static string Signature() {
            return "FlashEditor " + Environment.MachineName + " pid "
                + Environment.ProcessId + " at " + DateTime.Now.ToString("s");
        }
    }
}
