using FlashEditor.cache;
using System;
using System.Configuration;
using System.IO;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using Xunit;

namespace FlashEditor.Tests.Cache
{
    /// <summary>
    /// The editor half of the JS5 reload handshake, against a stand-in for the server.
    ///
    /// The ordering is the whole feature. The Hydra update server holds read handles on the dat2
    /// and every idx without FILE_SHARE_DELETE, so on Windows the editor's save fails while it
    /// runs; the release has to happen before the write. Every test here is therefore about when
    /// the save runs and what is left on disk afterwards, not about what was written.
    ///
    /// No cache is opened. The protocol is three files in a directory, so a directory is all a
    /// test needs, and the server is played by a task that creates the released marker - which is
    /// exactly what CacheWatcher.java does once its handles are shut.
    /// </summary>
    public class JS5ReloadHandshakeTests : IDisposable
    {
        /// <summary>Long enough that a responder gets its turn, short enough not to stall a run.</summary>
        private static readonly TimeSpan Generous = TimeSpan.FromSeconds(5);

        /// <summary>Longer than the handshake's own 100 ms poll, so a timeout is a real one.</summary>
        private static readonly TimeSpan Brief = TimeSpan.FromMilliseconds(400);

        private readonly string directory;

        public JS5ReloadHandshakeTests()
        {
            directory = Path.Combine(Path.GetTempPath(), "fe-js5-handshake-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(directory);
        }

        public void Dispose()
        {
            if (Directory.Exists(directory))
                Directory.Delete(directory, true);
        }

        private string Request => Path.Combine(directory, JS5ReloadHandshake.RequestFileName);
        private string Released => Path.Combine(directory, JS5ReloadHandshake.ReleasedFileName);

        /// <summary>
        /// Plays the server: waits for the request, then answers with the released marker.
        /// </summary>
        /// <param name="delay">How long to sit on the request before answering.</param>
        /// <returns>The responder, to be awaited so a failure in it is not swallowed.</returns>
        private Task Respond(TimeSpan delay)
        {
            return Task.Run(() =>
            {
                while (!File.Exists(Request))
                    Thread.Sleep(10);

                Thread.Sleep(delay);
                File.WriteAllText(Released, "stand-in server");
            });
        }

        /// <summary>
        /// The save must not start until the marker exists, because until then the server still
        /// holds the files and the write fails with a sharing violation.
        /// </summary>
        [Fact]
        public async Task TheSaveRunsOnlyAfterTheServerReleases()
        {
            Task responder = Respond(TimeSpan.FromMilliseconds(250));

            bool releasedWhenSaved = false;
            bool requestPresentWhenSaved = false;
            int saves = 0;

            JS5ReloadHandshake.Run(directory, Generous, () =>
            {
                saves++;
                releasedWhenSaved = File.Exists(Released);
                requestPresentWhenSaved = File.Exists(Request);
            });

            await responder;

            Assert.Equal(1, saves);
            Assert.True(releasedWhenSaved, "the save ran before the server released its handles");

            //The request has to outlive the write: deleting it is what tells the server to reload,
            //and a server that reloaded mid-write would reopen a half-promoted cache.
            Assert.True(requestPresentWhenSaved, "the request was withdrawn before the write finished");
        }

        /// <summary>
        /// Withdrawing the request is the fourth step of the protocol, so a completed handshake
        /// leaves the directory as it found it.
        /// </summary>
        [Fact]
        public async Task TheRequestIsWithdrawnAfterASuccessfulSave()
        {
            Task responder = Respond(TimeSpan.Zero);

            JS5ReloadHandshake.Run(directory, Generous, () => { });

            await responder;

            Assert.False(File.Exists(Request));
        }

        /// <summary>
        /// A save that throws still has to withdraw the request. Otherwise the server sits with
        /// its handles shut and no cache open, which is indistinguishable from a crash and needs
        /// a file deleted by hand to recover.
        /// </summary>
        [Fact]
        public async Task TheRequestIsWithdrawnWhenTheSaveThrows()
        {
            Task responder = Respond(TimeSpan.Zero);

            Assert.Throws<IOException>(() =>
                JS5ReloadHandshake.Run(directory, Generous, () => throw new IOException("promotion failed")));

            await responder;

            Assert.False(File.Exists(Request));
        }

        /// <summary>
        /// Nothing answering means nothing is written. On Windows the write would fail anyway
        /// while a server holds the files, and where no server is running at all a silent success
        /// would report a handshake that never happened.
        /// </summary>
        [Fact]
        public void NothingIsWrittenWhenNoServerReleases()
        {
            int saves = 0;

            Assert.Throws<TimeoutException>(() =>
                JS5ReloadHandshake.Run(directory, Brief, () => saves++));

            Assert.Equal(0, saves);
            Assert.False(File.Exists(Request));
        }

        /// <summary>
        /// The failure has to name the directory it asked in and both file names, because the
        /// recovery is either turning the setting off or looking at that directory, and neither
        /// is guessable from "timed out".
        /// </summary>
        [Fact]
        public void TheTimeoutSaysWhereItAskedAndWhatToDo()
        {
            TimeoutException failure = Assert.Throws<TimeoutException>(() =>
                JS5ReloadHandshake.Run(directory, Brief, () => { }));

            Assert.Contains(directory, failure.Message);
            Assert.Contains(JS5ReloadHandshake.RequestFileName, failure.Message);
            Assert.Contains(JS5ReloadHandshake.ReleasedFileName, failure.Message);
        }

        /// <summary>
        /// A released marker already present can only be residue from an editor that died
        /// mid-handshake. Believing it would start the write while the server still held the
        /// files, which is the exact failure the handshake exists to prevent, so it is deleted
        /// and the wait starts from nothing.
        /// </summary>
        [Fact]
        public void AStaleReleasedMarkerIsNotTakenAsTheAnswer()
        {
            File.WriteAllText(Released, "left behind by a previous run");

            int saves = 0;

            Assert.Throws<TimeoutException>(() =>
                JS5ReloadHandshake.Run(directory, Brief, () => saves++));

            Assert.Equal(0, saves);
        }

        /// <summary>
        /// With the setting off the save is a plain save: no files, no wait. This is the path
        /// every user who is not pointed at a live server takes.
        /// </summary>
        [Fact]
        public void WithTheSettingOffTheSaveIsUntouched()
        {
            bool saved = false;

            JS5ReloadHandshake.AroundSave(directory, () => saved = true);

            Assert.True(saved);
            Assert.False(File.Exists(Request));
            Assert.False(File.Exists(Released));
            Assert.Empty(Directory.GetFiles(directory));
        }

        /// <summary>
        /// Off is the shipped default, read from the setting's own declaration rather than from
        /// the running value, which a local user.config could have changed. On by default would
        /// make every save against an ordinary cache wait out the timeout and then refuse.
        /// </summary>
        [Fact]
        public void TheHandshakeIsOffByDefault()
        {
            PropertyInfo setting = typeof(FlashEditor.Properties.Settings)
                .GetProperty("js5LiveReload", BindingFlags.Public | BindingFlags.Instance);

            Assert.NotNull(setting);

            var declared = (DefaultSettingValueAttribute) Attribute.GetCustomAttribute(
                setting, typeof(DefaultSettingValueAttribute));

            Assert.NotNull(declared);
            Assert.Equal("False", declared.Value);
        }
    }
}
