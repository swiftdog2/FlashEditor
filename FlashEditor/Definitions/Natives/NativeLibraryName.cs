using System;
using System.Collections.Generic;
using System.Linq;
using FlashEditor.Cache;

namespace FlashEditor.Definitions.Natives {
    /// <summary>
    ///     An index-30 group name split into the three things it states.
    /// </summary>
    /// <remarks>
    ///     The name is the whole structure of this index. The client builds
    ///     <c>"&lt;os&gt;/&lt;arch&gt;/"</c> from <c>os.name</c> and <c>os.arch</c>
    ///     (<c>Class365.java:44-72</c>), appends the platform filename for the library it wants
    ///     (<c>Class35.java:92-102</c>) and hashes the result - so os, architecture and library are
    ///     not metadata beside the group, they are the address.
    /// </remarks>
    public readonly struct NativeLibraryName {
        private NativeLibraryName(string path, string operatingSystem, string architecture,
            string fileName, string library, string extension) {
            Path = path;
            OperatingSystem = operatingSystem;
            Architecture = architecture;
            FileName = fileName;
            Library = library;
            Extension = extension;
        }

        /// <summary>The stored name in full, or the empty string when it was never recovered.</summary>
        public string Path { get; }

        /// <summary>The first segment - <c>windows</c>, <c>linux</c> or <c>macos</c>.</summary>
        public string OperatingSystem { get; }

        /// <summary>
        ///     The second segment, <b>as stored</b>.
        /// </summary>
        /// <remarks>
        ///     Not normalised. <c>x64</c> and <c>x86_64</c> both occur under <c>windows</c> and mean
        ///     the same architecture, and folding them together here would erase the one thing about
        ///     this index worth reporting - see <see cref="NativeLibraryCensus"/>.
        /// </remarks>
        public string Architecture { get; }

        /// <summary>The last segment, the platform filename the client writes to disk.</summary>
        public string FileName { get; }

        /// <summary>The library family - <c>jaggl</c>, <c>jagdx</c>, <c>jagmisc</c>, <c>jaclib</c>, <c>hw3d</c>, <c>sw3d</c>.</summary>
        public string Library { get; }

        /// <summary>The filename extension including the dot, or the empty string.</summary>
        public string Extension { get; }

        /// <summary>Whether the name split into the three segments the format states.</summary>
        public bool IsWellFormed => OperatingSystem.Length > 0 && Architecture.Length > 0 && FileName.Length > 0;

        /// <summary>The unrecovered name, for a group nothing hashed to.</summary>
        public static NativeLibraryName None { get; } =
            new NativeLibraryName(string.Empty, string.Empty, string.Empty, string.Empty, string.Empty, string.Empty);

        /// <summary>Splits a recovered group name.</summary>
        /// <param name="path">The name, as recovered from the stored hash.</param>
        /// <returns>The split name, or <see cref="None"/> when there is no name.</returns>
        public static NativeLibraryName Parse(string? path) {
            if (string.IsNullOrEmpty(path))
                return None;

            string[] segments = path.Split('/');
            if (segments.Length != 3)
                //Kept whole rather than guessed at. A name that does not split is still the name the
                //hash proved, and showing it beats showing three empty columns.
                return new NativeLibraryName(path, string.Empty, string.Empty, string.Empty, string.Empty, string.Empty);

            string fileName = segments[2];
            int dot = fileName.LastIndexOf('.');
            string extension = dot < 0 ? string.Empty : fileName.Substring(dot);
            string stem = dot < 0 ? fileName : fileName.Substring(0, dot);

            //The lib prefix is a platform convention, not part of the family: linux/libjaggl.so and
            //windows/jaggl.dll are the same library, and a column that reported them differently
            //would break the one grouping a user actually wants.
            if (stem.StartsWith("lib", StringComparison.Ordinal) && stem.Length > 3)
                stem = stem.Substring(3);

            return new NativeLibraryName(path, segments[0], segments[1], fileName, stem, extension);
        }
    }

    /// <summary>
    ///     What the whole of index 30 looks like, and which of its names disagree with each other.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///     <b>The anomaly this exists to surface.</b> One group is named
    ///     <c>windows/x64/jagmisc.dll</c> while every other 64-bit Windows library in the cache is
    ///     under <c>windows/x86_64/</c>. The 637 client only ever emits <c>x86_64/</c> for a 64-bit
    ///     host (<c>Class365.java:70-72</c>), so it asks for a name no group carries and a 64-bit
    ///     Windows client cannot load jagmisc from this cache at all. <b>The cache wins.</b> The
    ///     stored hash is the fact and the name is not corrected anywhere in this editor - it is
    ///     reported.
    ///     </para>
    ///     <para>
    ///     Derived from the loaded cache rather than hardcoded to a group id. The rule is "one
    ///     operating system spells one architecture two ways", which is a property of the name set
    ///     and is checkable; a hardcoded id would keep claiming the anomaly against a cache that no
    ///     longer had it, and would miss a second one.
    ///     </para>
    /// </remarks>
    public sealed class NativeLibraryCensus {
        /// <summary>
        ///     Architecture tokens that name the same machine, lower-cased.
        /// </summary>
        /// <remarks>
        ///     Only the aliases this cache and this client actually use. A wider table would start
        ///     inventing equivalences nothing has demonstrated, which is the failure mode this whole
        ///     index is a lesson in.
        /// </remarks>
        private static readonly Dictionary<string, string> ArchitectureAliases = new(StringComparer.OrdinalIgnoreCase) {
            { "x86_64", "x86_64" },
            { "x64", "x86_64" },
            { "amd64", "x86_64" },
            { "x86", "x86" },
            { "i386", "x86" },
            { "i586", "x86" },
            { "i686", "x86" }
        };

        private readonly Dictionary<int, string> anomalies;

        private NativeLibraryCensus(Dictionary<int, string> anomalies, int named, int declared) {
            this.anomalies = anomalies;
            NamedGroups = named;
            DeclaredGroups = declared;
        }

        /// <summary>How many groups a committed candidate name resolved.</summary>
        public int NamedGroups { get; }

        /// <summary>How many groups the reference table declares.</summary>
        public int DeclaredGroups { get; }

        /// <summary>The group ids whose architecture token disagrees with their siblings'.</summary>
        public IReadOnlyCollection<int> AnomalousGroups => anomalies.Keys;

        /// <summary>
        ///     Why this group's name is odd, or <c>null</c> when it is not.
        /// </summary>
        /// <param name="groupId">The group id.</param>
        /// <returns>A sentence for the grid, or <c>null</c>.</returns>
        public string? AnomalyFor(int groupId) {
            return anomalies.TryGetValue(groupId, out string? note) ? note : null;
        }

        /// <summary>Surveys index 30 in an open cache.</summary>
        /// <param name="cache">The open cache.</param>
        /// <returns>The census.</returns>
        public static NativeLibraryCensus Build(RSCache cache) {
            if (cache == null)
                throw new ArgumentNullException(nameof(cache));

            var names = new Dictionary<int, NativeLibraryName>();
            RSReferenceTable table = cache.GetReferenceTable(RSConstants.NATIVE_LIBRARIES);

            foreach (int groupId in cache.EnumerateGroups(RSConstants.NATIVE_LIBRARIES)) {
                RSArchiveEntry? entry = table.GetArchiveEntry(groupId);
                if (entry == null)
                    continue;

                names[groupId] = NativeLibraryNames.TryGetName(entry.GetIdentifier(), out string? name)
                    ? NativeLibraryName.Parse(name)
                    : NativeLibraryName.None;
            }

            return new NativeLibraryCensus(FindAnomalies(names),
                names.Count(pair => pair.Value.Path.Length > 0), table.GetArchiveCount());
        }

        /// <summary>
        ///     Finds every operating system that spells one architecture more than one way.
        /// </summary>
        /// <remarks>
        ///     The minority spelling is the one reported, because the majority is what the client's
        ///     own path rule emits and is therefore the reachable one. Where two spellings are used
        ///     equally often neither is reported: there would be no evidence for calling either the
        ///     odd one, and guessing is what this index punishes.
        /// </remarks>
        private static Dictionary<int, string> FindAnomalies(Dictionary<int, NativeLibraryName> names) {
            var found = new Dictionary<int, string>();

            IEnumerable<IGrouping<(string Os, string Canonical), KeyValuePair<int, NativeLibraryName>>> families =
                names.Where(pair => pair.Value.IsWellFormed &&
                                    ArchitectureAliases.ContainsKey(pair.Value.Architecture))
                    .GroupBy(pair => (Os: pair.Value.OperatingSystem.ToLowerInvariant(),
                        Canonical: ArchitectureAliases[pair.Value.Architecture]));

            foreach (var family in families) {
                Dictionary<string, int> spellings = family
                    .GroupBy(pair => pair.Value.Architecture, StringComparer.OrdinalIgnoreCase)
                    .ToDictionary(spelling => spelling.Key, spelling => spelling.Count(),
                        StringComparer.OrdinalIgnoreCase);

                if (spellings.Count < 2)
                    continue;

                int majority = spellings.Values.Max();
                if (spellings.Values.Count(count => count == majority) != 1)
                    continue;

                string dominant = spellings.First(spelling => spelling.Value == majority).Key;

                foreach (KeyValuePair<int, NativeLibraryName> pair in family) {
                    if (string.Equals(pair.Value.Architecture, dominant, StringComparison.OrdinalIgnoreCase))
                        continue;

                    found[pair.Key] = "Named \"" + pair.Value.Architecture + "\" where the other " + majority +
                                      " " + family.Key.Os + " " + family.Key.Canonical +
                                      " libraries use \"" + dominant + "\". The 637 client only ever builds \"" +
                                      dominant + "/\" for this architecture (Class365.java:70-72), so it asks for a" +
                                      " name no group carries and cannot load this library at all. The stored hash" +
                                      " is the fact and is left as it is.";
                }
            }

            return found;
        }
    }
}
