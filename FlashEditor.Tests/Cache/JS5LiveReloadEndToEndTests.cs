using FlashEditor.cache;
using FlashEditor.Utils;
using System;
using System.IO;
using System.Linq;
using Xunit;
using Xunit.Abstractions;

namespace FlashEditor.Tests.Cache
{
    /// <summary>
    ///     Skips unless a disposable cache copy has been named <b>and</b> an update server is
    ///     declared to be serving it.
    /// </summary>
    /// <remarks>
    ///     xUnit resolves <see cref="FactAttribute.Skip"/> at discovery, which is what lets the
    ///     decision depend on the machine. The same shape as <c>RealCacheFactAttribute</c>, and for
    ///     the same reason: a run with nothing to point at should say so rather than pass silently.
    ///     <para>
    ///     Both tests need the server, in opposite directions. One asserts that a save fails, which
    ///     is only true while something holds the files open; the other waits for a handshake only
    ///     a server can answer, and would spend the whole timeout before failing.
    ///     </para>
    /// </remarks>
    public sealed class LiveReloadFactAttribute : FactAttribute
    {
        /// <summary>Creates the attribute, skipping when no cache or no server is declared.</summary>
        public LiveReloadFactAttribute()
        {
            string reason = JS5LiveReloadEndToEndTests.SkipReason;

            if (reason == null && Environment.GetEnvironmentVariable(
                    JS5LiveReloadEndToEndTests.ServerVariable) != "1")
                reason = JS5LiveReloadEndToEndTests.ServerVariable
                    + " is not 1, so no update server is declared to be serving that cache";

            if (reason != null)
                Skip = reason;
        }
    }

    /// <summary>
    /// Drives a real edit and a real save against a cache a running Hydra update server is
    /// serving, which is the only way to exercise the JS5 reload handshake against the thing it
    /// was written for.
    ///
    /// Both tests are skipped unless <see cref="CacheVariable"/> names a directory holding
    /// <see cref="MarkerFile"/>. That marker is a safety interlock, not a convenience: the two
    /// real 639 caches on this machine are read-only and neither carries it, so pointing this at
    /// one by accident skips instead of writing to it. Copy a cache and drop the marker in beside
    /// it.
    ///
    /// What they prove between them: with the handshake off and the server up, the save is
    /// refused by Windows because the server holds the files; with it on, the same edit reaches
    /// the disk. The bytes the server then serves are checked outside this suite, over the JS5
    /// protocol, against the container dumped here.
    /// </summary>
    public class JS5LiveReloadEndToEndTests
    {
        /// <summary>Names the scratch cache copy to edit.</summary>
        public const string CacheVariable = "FLASHEDITOR_LIVE_RELOAD_CACHE";

        /// <summary>Set when an update server is actually serving that copy.</summary>
        /// <remarks>
        ///     The refusal test asserts that a save fails, which is only true while something
        ///     holds the files open. Without this it would fail against a cache nobody is serving
        ///     by reporting a successful save as a defect.
        /// </remarks>
        public const string ServerVariable = "FLASHEDITOR_LIVE_RELOAD_SERVER";

        /// <summary>Where the dumped container and payload are written for an external check.</summary>
        public const string DumpVariable = "FLASHEDITOR_LIVE_RELOAD_DUMP";

        /// <summary>The interlock that says a directory is a disposable copy.</summary>
        public const string MarkerFile = "live-reload-scratch.marker";

        /// <summary>How long to wait for the server to release. Its own poll is 500 ms.</summary>
        private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(30);

        private readonly ITestOutputHelper output;

        public JS5LiveReloadEndToEndTests(ITestOutputHelper output)
        {
            this.output = output;
            DebugUtil.LOG_LEVEL = DebugUtil.LOG_DETAIL.NONE;
        }

        /// <summary>Why these tests cannot run here, or null when they can.</summary>
        internal static string SkipReason
        {
            get
            {
                string directory = Environment.GetEnvironmentVariable(CacheVariable);

                if (string.IsNullOrWhiteSpace(directory))
                    return CacheVariable + " is not set, so there is no disposable cache to edit";

                if (!File.Exists(Path.Combine(directory, MarkerFile)))
                    return directory + " does not hold " + MarkerFile
                        + ", so it is not a copy this test may write to";

                return null;
            }
        }

        /// <summary>The scratch cache to edit, or null when this run is not set up for one.</summary>
        private static string ScratchCache
        {
            get
            {
                string directory = Environment.GetEnvironmentVariable(CacheVariable);

                if (string.IsNullOrWhiteSpace(directory))
                    return null;

                return File.Exists(Path.Combine(directory, MarkerFile)) ? directory : null;
            }
        }

        /// <summary>
        /// The failure the handshake exists to prevent: Java opens the cache files without
        /// FILE_SHARE_DELETE, so <c>RSFileStore.SaveTo</c> cannot promote its staged files over
        /// them while the server runs.
        /// </summary>
        /// <remarks>
        ///     This asserts that the save is refused rather than that it corrupts anything, which
        ///     is what makes the handshake worth having: without it the edit is simply lost, and
        ///     the editor's own error is a bare sharing violation naming a file the user never
        ///     touched.
        /// </remarks>
        [LiveReloadFact]
        public void WithoutTheHandshakeTheSaveIsRefusedWhileTheServerHoldsTheFiles()
        {
            string cacheDirectory = ScratchCache;

            using (var store = new RSFileStore(cacheDirectory))
            {
                var cache = new RSCache(store);
                Edit(cache, out int group, out int file, out byte[] _);
                output.WriteLine("Edited index " + RSConstants.HUFFMAN_INDEX + " group " + group + " file " + file);

                Exception failure = Record.Exception(() => cache.WriteCache(cacheDirectory));

                Assert.NotNull(failure);
                output.WriteLine("Save refused, as it must be: " + failure.GetType().Name + ": " + failure.Message);
            }
        }

        /// <summary>
        /// The whole loop from the editor's side: ask, wait, write, withdraw. Persistence is
        /// checked by reopening the store, because a read through the cache that wrote it returns
        /// the staged bytes whether or not they were ever committed.
        /// </summary>
        [LiveReloadFact]
        public void ThroughTheHandshakeTheEditReachesDisk()
        {
            string cacheDirectory = ScratchCache;

            int group;
            int file;
            byte[] written;

            using (var store = new RSFileStore(cacheDirectory))
            {
                var cache = new RSCache(store);
                Edit(cache, out group, out file, out written);

                JS5ReloadHandshake.Run(cacheDirectory, Timeout, () => cache.WriteCache(cacheDirectory));
            }

            //A second, independent open. The first store's overlay is gone with it, so anything
            //read here came off the disk.
            using (var reopened = new RSFileStore(cacheDirectory))
            {
                var cache = new RSCache(reopened);

                Assert.Equal(written, cache.ReadFileBytes(RSConstants.HUFFMAN_INDEX, group, file));

                byte[] container = cache.LoadContainer(RSConstants.HUFFMAN_INDEX, group).ToArray();
                Dump("js5-live-reload-container.bin", container);
                Dump("js5-live-reload-payload.bin", written);

                output.WriteLine("Index " + RSConstants.HUFFMAN_INDEX + " group " + group + " file " + file
                    + ": payload " + written.Length + " bytes, stored container " + container.Length + " bytes");
            }
        }

        /// <summary>
        /// Stages one visible edit: the first file of the first group of the Huffman index, with
        /// every byte of its payload inverted.
        /// </summary>
        /// <remarks>
        ///     Index 10 because it is the smallest thing in the cache that is still a real group -
        ///     one group, one file, a few hundred bytes - so the container the server serves back
        ///     fits in one JS5 block and can be compared by eye. The payload keeps its length, so
        ///     a difference in the served bytes can only be the edit and never a re-chunking.
        /// </remarks>
        /// <param name="cache">The cache to edit.</param>
        /// <param name="group">The group edited.</param>
        /// <param name="file">The file edited within it.</param>
        /// <param name="written">The payload staged.</param>
        private static void Edit(RSCache cache, out int group, out int file, out byte[] written)
        {
            group = cache.EnumerateGroups(RSConstants.HUFFMAN_INDEX).First();
            file = cache.GetFileIds(RSConstants.HUFFMAN_INDEX, group).First();

            byte[] original = cache.ReadFileBytes(RSConstants.HUFFMAN_INDEX, group, file);
            Assert.NotEmpty(original);

            written = original.Select(b => (byte) ~b).ToArray();
            cache.WriteFile(RSConstants.HUFFMAN_INDEX, group, file, new JagStream(written));
        }

        /// <summary>Writes a byte dump for the external JS5 comparison and says where it went.</summary>
        /// <param name="name">The file name to write under.</param>
        /// <param name="bytes">The bytes to write.</param>
        private void Dump(string name, byte[] bytes)
        {
            string directory = Environment.GetEnvironmentVariable(DumpVariable);

            if (string.IsNullOrWhiteSpace(directory))
                directory = Path.GetTempPath();

            Directory.CreateDirectory(directory);
            string path = Path.Combine(directory, name);
            File.WriteAllBytes(path, bytes);
            output.WriteLine("Wrote " + bytes.Length + " bytes to " + path);
        }
    }
}
