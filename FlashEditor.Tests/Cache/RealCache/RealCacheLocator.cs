using System;
using System.IO;

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

                //Walk up from the test binaries looking for a sibling 'cache' directory, so a
                //cache dropped at the repository root is picked up with no configuration.
                for (DirectoryInfo dir = new DirectoryInfo(AppContext.BaseDirectory); dir != null; dir = dir.Parent)
                {
                    string candidate = Path.Combine(dir.FullName, "cache");
                    if (IsCache(candidate))
                    {
                        _directory = candidate;
                        return;
                    }
                }

                _skipReason = "no revision-639 cache found - set " + PathVariable +
                    " or place one in a 'cache' directory at the repository root";
            }
        }

        private static bool IsCache(string dir)
        {
            return !string.IsNullOrWhiteSpace(dir)
                && File.Exists(Path.Combine(dir, DataFile))
                && File.Exists(Path.Combine(dir, MetaIndexFile));
        }
    }
}
