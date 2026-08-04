using System;
using System.Collections.Generic;
using System.IO;
using FlashEditor.Cache.Util.Crypto;
using static FlashEditor.Utils.DebugUtil;

namespace FlashEditor.cache {
    /// <summary>
    ///     Where the editor looks for a cache, for the two directories it writes to, and for the
    ///     XTEA key file that belongs to whichever cache is actually open.
    /// </summary>
    /// <remarks>
    ///     This exists because the three paths it replaces were compile-time literals naming one
    ///     developer's machine, and one of the three did not exist even there. That was survivable
    ///     for the two output directories and fatal for the key file: the editor opens whatever
    ///     directory the user settings name, and the only key lookup it did was beside that
    ///     directory, so a cache stored anywhere without a key dump next to it resolved no keys at
    ///     all and reported every encrypted map square as unkeyed.
    ///     <para>
    ///     The three paths are three different things and are kept apart deliberately. The input is
    ///     the cache being read, the output is where edits and exports are written, and the pristine
    ///     copy is what the revert button reloads. Compare-to-output is only meaningful while the
    ///     first two differ, so <see cref="Output"/> and <see cref="Pristine"/> refuse to resolve to
    ///     <see cref="Input"/>.
    ///     </para>
    ///     <para>
    ///     Nothing here is authoritative over the user's own choice. <c>Editor.GetCacheDir</c> reads
    ///     the persisted setting first and only seeds it from <see cref="Input"/> when there is
    ///     none, so this is the fallback rather than the source of truth.
    ///     </para>
    /// </remarks>
    public static class CachePaths {
        /// <summary>Overrides <see cref="Input"/> without a rebuild.</summary>
        public const string InputVariable = "FLASHEDITOR_CACHE";

        /// <summary>Overrides <see cref="Output"/> without a rebuild.</summary>
        public const string OutputVariable = "FLASHEDITOR_CACHE_OUTPUT";

        /// <summary>Overrides <see cref="Pristine"/> without a rebuild.</summary>
        public const string PristineVariable = "FLASHEDITOR_CACHE_ORIGINAL";

        /* The literals this type was built to demote. They are kept because they are still right on
           the machine the editor was written on and because they name three distinct roles, but
           they are consulted last and only when their parent directory exists - a fallback that
           names a directory whose parent is absent is a guess, not a default. */
        private const string FallbackInput = "C:/Users/CJ/Desktop/RSPS/Hydra/cache/";
        private const string FallbackOutput = "C:/Users/CJ/Desktop/RSPS/Hydra/cache2/";
        private const string FallbackPristine = "C:/Users/CJ/Desktop/RSPS/Hydra/cache0/";

        /// <summary>The two files every JS5 cache directory has, whatever else it holds.</summary>
        private const string DataFile = "main_file_cache.dat2";
        private const string MetaIndexFile = "main_file_cache.idx255";

        /// <summary>Where a checked-out working copy keeps its cache and its key dump.</summary>
        private const string RepositoryCacheDirectory = "cache";

        /// <summary>Where an OpenRS2 capture is unpacked, each capture in its own subdirectory.</summary>
        private const string CaptureDirectory = "OpenRS2";

        /// <summary>How far up the directory tree the search for a cache and a key file goes.</summary>
        /// <remarks>
        ///     The application runs out of <c>bin/&lt;config&gt;/&lt;tfm&gt;</c>, so the repository
        ///     root is five levels up at most. The bound stops the walk at a drive root rather than
        ///     saving any measurable time.
        /// </remarks>
        private const int SearchDepth = 8;

        /// <summary>
        ///     The cache to open when the user has not chosen one.
        /// </summary>
        /// <remarks>
        ///     Resolved on every read rather than memoised: a directory can appear or be replaced
        ///     while the editor is running, and this is consulted a handful of times per session.
        /// </remarks>
        public static string Input {
            get {
                string? overridden = FromVariable(InputVariable);
                if (overridden != null)
                    return overridden;

                string? found = FindCache();
                if (found != null)
                    return found;

                return FallbackInput;
            }
        }

        /// <summary>
        ///     Where edits and item exports are written.
        /// </summary>
        /// <remarks>
        ///     Never the same directory as <see cref="Input"/>. The compare-to-output feature reads
        ///     one against the other, so collapsing them would make it compare a cache with itself
        ///     and report no differences whatever either holds.
        /// </remarks>
        public static string Output => Sibling(OutputVariable, FallbackOutput, "cache-output");

        /// <summary>
        ///     The untouched copy the revert button reloads.
        /// </summary>
        /// <remarks>
        ///     This one is allowed not to exist - it is a copy the user takes, not one the editor
        ///     makes. The reload button reports a directory that holds no cache rather than opening
        ///     nothing and leaving the editor looking hung.
        /// </remarks>
        public static string Pristine => Sibling(PristineVariable, FallbackPristine, "cache-original");

        /// <summary>
        ///     Whether a directory holds a JS5 cache.
        /// </summary>
        /// <remarks>
        ///     Both files are checked because the dat2 alone is not enough: a directory holding a
        ///     partial copy, or a client's data directory, can carry one without the other, and
        ///     <c>RSFileStore</c> needs the meta index before it can read anything at all.
        /// </remarks>
        /// <param name="directory">The directory to test.</param>
        /// <returns>Whether it holds a cache.</returns>
        public static bool IsCacheDirectory(string? directory) {
            if (string.IsNullOrWhiteSpace(directory))
                return false;

            try {
                return File.Exists(Path.Combine(directory, DataFile))
                    && File.Exists(Path.Combine(directory, MetaIndexFile));
            } catch (Exception ex) {
                //A malformed path is "not a cache" rather than a crash: this runs on a value that
                //came out of user settings or an environment variable.
                Debug("Could not test " + directory + " for a cache: " + ex.Message, LOG_DETAIL.ADVANCED);
                return false;
            }
        }

        /// <summary>
        ///     The XTEA key file belonging to an open cache.
        /// </summary>
        /// <remarks>
        ///     Beside the cache first, through <see cref="XTEAKeyTable.FindKeyFile"/>, which probes
        ///     the directory and its parent crossed with <c>xteas/</c> and <c>keys/</c>. That is what
        ///     both supported caches satisfy and it is the answer that is certainly about the cache
        ///     in front of it.
        ///     <para>
        ///     Only when that finds nothing does the search widen to the application's own directory
        ///     tree. Widening it is safe for this cache family and no wider claim is being made: the
        ///     keys are per map square and build-wide, and the same dump opens every keyed archive of
        ///     both 639 caches on disk. A key that does not fit is already treated as "not
        ///     encrypted", so a dump from the wrong build costs nothing beyond the archives it
        ///     cannot open.
        ///     </para>
        /// </remarks>
        /// <param name="cacheDirectory">The directory the cache was opened from.</param>
        /// <param name="probed">Every root searched, for a log line that says where to put a key file.</param>
        /// <returns>The key file, or null when none was found.</returns>
        public static string? FindKeyFile(string? cacheDirectory, out IReadOnlyList<string> probed) {
            var roots = new List<string>();

            if (!string.IsNullOrWhiteSpace(cacheDirectory))
                roots.Add(cacheDirectory);

            foreach (string root in SearchRoots())
                if (!Contains(roots, root))
                    roots.Add(root);

            probed = roots;

            foreach (string root in roots) {
                string? found = XTEAKeyTable.FindKeyFile(root);
                if (found != null)
                    return found;
            }

            return null;
        }

        /// <summary>The XTEA key file belonging to an open cache.</summary>
        /// <param name="cacheDirectory">The directory the cache was opened from.</param>
        /// <returns>The key file, or null when none was found.</returns>
        public static string? FindKeyFile(string? cacheDirectory) {
            return FindKeyFile(cacheDirectory, out _);
        }

        /// <summary>
        ///     The first cache the application can see from where it is running.
        /// </summary>
        /// <remarks>
        ///     An unpacked OpenRS2 capture is preferred over a repository <c>cache/</c> because the
        ///     capture is the vanilla build and the working copy's cache is a private-server repack.
        ///     Both are supported; the vanilla one is the better thing to open by default.
        /// </remarks>
        /// <returns>The cache directory, or null when none is in reach.</returns>
        private static string? FindCache() {
            foreach (string root in SearchRoots()) {
                string? capture = FindCapture(root);
                if (capture != null)
                    return capture;

                string repository = Path.Combine(root, RepositoryCacheDirectory);
                if (IsCacheDirectory(repository))
                    return repository;

                if (IsCacheDirectory(root))
                    return root;
            }

            return null;
        }

        /// <summary>An unpacked OpenRS2 capture under <paramref name="root"/>, if there is one.</summary>
        /// <param name="root">The directory to look under.</param>
        /// <returns>The capture's cache directory, or null.</returns>
        private static string? FindCapture(string root) {
            string captures = Path.Combine(root, CaptureDirectory);

            try {
                if (!Directory.Exists(captures))
                    return null;

                foreach (string capture in Directory.EnumerateDirectories(captures)) {
                    string inner = Path.Combine(capture, RepositoryCacheDirectory);
                    if (IsCacheDirectory(inner))
                        return inner;
                    if (IsCacheDirectory(capture))
                        return capture;
                }
            } catch (Exception ex) {
                //An unreadable directory costs this candidate, not the search.
                Debug("Could not list captures under " + captures + ": " + ex.Message, LOG_DETAIL.ADVANCED);
            }

            return null;
        }

        /// <summary>
        ///     Where an edit or revert directory lands.
        /// </summary>
        /// <remarks>
        ///     The environment wins, then the fallback literal while the directory it would sit in
        ///     exists, and otherwise a sibling of the cache being read. The middle arm is what keeps
        ///     the author's own layout working without hardcoding it as the answer everywhere else.
        /// </remarks>
        /// <param name="variable">The environment variable that overrides this path.</param>
        /// <param name="fallback">The literal to use while its parent directory exists.</param>
        /// <param name="siblingName">The directory name to use beside the input cache otherwise.</param>
        /// <returns>The directory, which is never <see cref="Input"/>.</returns>
        private static string Sibling(string variable, string fallback, string siblingName) {
            string input = Input;

            string? overridden = FromVariable(variable);
            if (overridden != null && !SamePath(overridden, input))
                return overridden;

            if (overridden == null && ParentExists(fallback) && !SamePath(fallback, input))
                return fallback;

            try {
                DirectoryInfo? parent = Directory.GetParent(input.TrimEnd('/', '\\'));
                if (parent != null)
                    return Path.Combine(parent.FullName, siblingName);
            } catch (Exception ex) {
                Debug("Could not derive " + siblingName + " beside " + input + ": " + ex.Message, LOG_DETAIL.ADVANCED);
            }

            //Only reachable when the input cache is a drive root, which nothing supports anyway.
            return Path.Combine(input, siblingName);
        }

        /// <summary>The value of an environment variable, or null when it is unset or blank.</summary>
        /// <param name="name">The variable name.</param>
        /// <returns>The value, trimmed, or null.</returns>
        private static string? FromVariable(string name) {
            string? value = Environment.GetEnvironmentVariable(name);
            return string.IsNullOrWhiteSpace(value) ? null : value.Trim();
        }

        /// <summary>Whether the directory a path would sit in exists.</summary>
        /// <param name="path">The path to test.</param>
        /// <returns>Whether its parent exists.</returns>
        private static bool ParentExists(string path) {
            try {
                DirectoryInfo? parent = Directory.GetParent(path.TrimEnd('/', '\\'));
                return parent != null && parent.Exists;
            } catch (Exception ex) {
                Debug("Could not resolve the parent of " + path + ": " + ex.Message, LOG_DETAIL.ADVANCED);
                return false;
            }
        }

        /// <summary>
        ///     The directories a search walks: where the application is running from and where it was
        ///     started, plus every ancestor of each.
        /// </summary>
        /// <remarks>
        ///     Both starting points are needed. A build run from the IDE has its base directory deep
        ///     under <c>bin/</c>, which is what reaches the repository root; a build launched from a
        ///     copied output folder has nothing above it and only the working directory is useful.
        /// </remarks>
        /// <returns>The roots, nearest first, without duplicates.</returns>
        private static IEnumerable<string> SearchRoots() {
            var roots = new List<string>();

            foreach (string start in new[] { AppDomain.CurrentDomain.BaseDirectory, Environment.CurrentDirectory }) {
                if (string.IsNullOrWhiteSpace(start))
                    continue;

                DirectoryInfo? directory;
                try {
                    directory = new DirectoryInfo(start);
                } catch (Exception ex) {
                    Debug("Could not walk up from " + start + ": " + ex.Message, LOG_DETAIL.ADVANCED);
                    continue;
                }

                for (int level = 0; level < SearchDepth && directory != null; level++) {
                    if (!Contains(roots, directory.FullName))
                        roots.Add(directory.FullName);
                    directory = directory.Parent;
                }
            }

            return roots;
        }

        /// <summary>Whether a list already names a directory, comparing as Windows compares paths.</summary>
        /// <param name="roots">The list so far.</param>
        /// <param name="candidate">The directory to test.</param>
        /// <returns>Whether it is already present.</returns>
        private static bool Contains(List<string> roots, string candidate) {
            foreach (string root in roots)
                if (SamePath(root, candidate))
                    return true;
            return false;
        }

        /// <summary>
        ///     Whether two paths name the same directory.
        /// </summary>
        /// <remarks>
        ///     Case-insensitive and trailing-separator-insensitive, because both are how the same
        ///     directory reaches here by two routes - one from user settings and one from a walk.
        ///     Full canonicalisation is not attempted; the comparison only has to be good enough to
        ///     stop the output directory silently becoming the input one.
        /// </remarks>
        /// <param name="left">One path.</param>
        /// <param name="right">The other.</param>
        /// <returns>Whether they name the same directory.</returns>
        private static bool SamePath(string? left, string? right) {
            if (left == null || right == null)
                return false;

            try {
                return string.Equals(
                    Path.GetFullPath(left).TrimEnd('/', '\\'),
                    Path.GetFullPath(right).TrimEnd('/', '\\'),
                    StringComparison.OrdinalIgnoreCase);
            } catch (Exception ex) {
                Debug("Could not compare " + left + " with " + right + ": " + ex.Message, LOG_DETAIL.ADVANCED);
                return false;
            }
        }
    }
}
