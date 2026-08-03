using FlashEditor.Cache.Util.Crypto;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace FlashEditor.Tests.Cache.RealCache
{
    /// <summary>
    ///     Finds the real revision-639 cache the conformance suite runs against.
    /// </summary>
    /// <remarks>
    ///     A real cache is hundreds of megabytes of game data and cannot be committed, so the
    ///     tests that need one skip cleanly when it is absent rather than failing. Set
    ///     <see cref="PathVariable"/> to point at a cache directory, or drop one in a
    ///     <c>cache</c> folder at the repository root.
    ///     <para>
    ///     Two revision-639 caches live in this tree and they disagree on eleven indexes, so
    ///     which one is found is not an implementation detail. The vanilla OpenRS2 capture is
    ///     preferred because it is the clean standard: the repository-root <c>cache</c> is a
    ///     private-server repack that adds content on the indexes a server customises, and a
    ///     measurement taken there describes that repack rather than build 639.
    ///     </para>
    /// </remarks>
    internal static class RealCacheLocator
    {
        /// <summary>Environment variable naming the cache directory explicitly.</summary>
        public const string PathVariable = "FLASHEDITOR_TEST_CACHE";

        /// <summary>
        ///     Environment variable that, when set to <c>1</c>, sweeps every archive in the
        ///     cache instead of the per-index sample.
        /// </summary>
        public const string FullSweepVariable = "FLASHEDITOR_TEST_CACHE_FULL";

        /// <summary>
        ///     Directory the OpenRS2 archive downloads unpack into, relative to a tree root.
        /// </summary>
        /// <remarks>
        ///     A capture unpacks to <c>OpenRS2/&lt;capture name&gt;/cache</c>, which is two
        ///     levels below a tree root rather than beside it, so the upward walk on its own
        ///     could never reach it however far it climbed.
        /// </remarks>
        private const string ArchiveDirectory = "OpenRS2";

        private const string DataFile = "main_file_cache.dat2";
        private const string MetaIndexFile = "main_file_cache.idx255";

        private static readonly object Gate = new object();
        private static bool _resolved;
        private static string _directory;
        private static string _skipReason;

        /// <summary>The located cache directory, or <c>null</c> when there is none.</summary>
        public static string Directory
        {
            get { Resolve(); return _directory; }
        }

        /// <summary>Why the cache is unavailable, or <c>null</c> when it is available.</summary>
        public static string SkipReason
        {
            get { Resolve(); return _skipReason; }
        }

        /// <summary>
        ///     The XTEA key file the production probe resolves for the located cache, or
        ///     <c>null</c> when it finds none.
        /// </summary>
        /// <remarks>
        ///     Exposed so a test can report which file the keys actually came from. The two
        ///     caches keep it in different places - the repack under <c>xteas/xteas.json</c> a
        ///     level above the cache, the OpenRS2 capture as <c>keys.json</c> beside the cache
        ///     directory - and <see cref="XTEAKeyTable.FindKeyFile"/> already probes both roots,
        ///     so neither needs the user's file moved. It probes <c>xteas.json</c> ahead of
        ///     <c>keys.json</c> though, so anything dropped beside a capture under the earlier
        ///     name would silently win over the shipped dump; naming the resolved path is what
        ///     makes that visible.
        /// </remarks>
        public static string KeyFile
        {
            get
            {
                string dir = Directory;
                return dir == null ? null : XTEAKeyTable.FindKeyFile(dir);
            }
        }

        /// <summary>Whether every archive should be examined rather than a per-index sample.</summary>
        public static bool FullSweep =>
            Environment.GetEnvironmentVariable(FullSweepVariable) == "1";

        private static void Resolve()
        {
            lock (Gate)
            {
                if (_resolved)
                    return;
                _resolved = true;

                string configured = Environment.GetEnvironmentVariable(PathVariable);
                if (!string.IsNullOrWhiteSpace(configured))
                {
                    if (IsCache(configured))
                        _directory = Path.GetFullPath(configured);
                    else
                        _skipReason = PathVariable + " is set to '" + configured +
                            "' but that is not a cache directory (needs " + DataFile + " and " + MetaIndexFile + ")";
                    return;
                }

                //Walk up from the test binaries, taking the first cache any ancestor offers, so a
                //cache dropped in this tree is picked up with no configuration.
                for (DirectoryInfo dir = new DirectoryInfo(AppContext.BaseDirectory); dir != null; dir = dir.Parent)
                {
                    foreach (string candidate in CandidatesUnder(dir.FullName))
                    {
                        if (!IsCache(candidate))
                            continue;
                        _directory = candidate;
                        return;
                    }
                }

                _skipReason = "no revision-639 cache found - set " + PathVariable +
                    " or place one in a 'cache' directory at the repository root";
            }
        }

        /// <summary>
        ///     The cache directories one ancestor offers, most preferred first.
        /// </summary>
        /// <remarks>
        ///     The OpenRS2 captures come first because a vanilla capture is the source of truth
        ///     and the repository-root <c>cache</c> is a repack. Captures are taken in name order
        ///     so a tree holding more than one resolves the same way on every run rather than
        ///     following whatever order the filesystem hands back.
        /// </remarks>
        /// <param name="root">The ancestor directory to probe.</param>
        /// <returns>Candidate cache directories, in preference order.</returns>
        private static IEnumerable<string> CandidatesUnder(string root)
        {
            string archive = Path.Combine(root, ArchiveDirectory);
            if (System.IO.Directory.Exists(archive))
            {
                foreach (string capture in System.IO.Directory.GetDirectories(archive)
                             .OrderBy(path => path, StringComparer.Ordinal))
                    yield return Path.Combine(capture, "cache");
            }

            yield return Path.Combine(root, "cache");
        }

        private static bool IsCache(string dir)
        {
            return !string.IsNullOrWhiteSpace(dir)
                && File.Exists(Path.Combine(dir, DataFile))
                && File.Exists(Path.Combine(dir, MetaIndexFile));
        }
    }
}
