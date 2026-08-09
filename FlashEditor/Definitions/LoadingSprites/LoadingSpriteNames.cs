using System;
using System.Collections.Generic;
using FlashEditor.Cache;
using FlashEditor.Cache.Util;

namespace FlashEditor.Definitions.LoadingSprites {
    /// <summary>
    ///     Recovers the name of an index-32 group from the identifier its reference table stores.
    /// </summary>
    /// <remarks>
    ///     Index 32 sets the identifiers flag, so every group carries <c>hash(name)</c> and never
    ///     the name. A name is therefore only ever <i>recovered</i>, by hashing a candidate and
    ///     requiring it to equal the stored identifier - a 32-bit match against a handful of
    ///     candidates, which is a self-proving join rather than a plausible one.
    ///     <para>
    ///     The first three come from the client outright: <c>Class84.java:20-31</c> resolves
    ///     <c>p11_full</c>, <c>p12_full</c> and <c>b12_full</c> by name against this archive, and
    ///     they land on the three glyph-sheet groups. The fourth was recovered from a candidate list
    ///     and matches the fourth glyph sheet. <b>The twenty-one JPEG groups are not named here.</b>
    ///     Their identifiers are non-zero and no wordlist tried has matched one; inventing a
    ///     plausible name for a loading screen is exactly the mistake this cache rewards.
    ///     </para>
    /// </remarks>
    public static class LoadingSpriteNames {
        /// <summary>
        ///     Every candidate name, of which only exact hash matches are ever reported.
        /// </summary>
        /// <remarks>
        ///     Deliberately a list of names and not a map from group id. The id a name lands on
        ///     falls out of the hash at load, so a cache that renumbered its groups is still named
        ///     correctly and a cache that renamed them reports nothing rather than the wrong thing.
        /// </remarks>
        private static readonly string[] Candidates = {
            "p11_full",
            "p12_full",
            "b12_full",
            "verdana_11pt_regular"
        };

        private static readonly Dictionary<int, string> ByHash = BuildIndex();

        /// <summary>The candidate set, so a test can pin what this claims to know.</summary>
        public static IReadOnlyCollection<string> KnownNames => Candidates;

        /// <summary>
        ///     The name whose hash is <paramref name="identifier"/>, when one is known.
        /// </summary>
        /// <param name="identifier">The identifier the reference table stores for the group.</param>
        /// <param name="name">The recovered name, or <c>null</c>.</param>
        /// <returns>Whether a candidate hashes to that identifier.</returns>
        public static bool TryGetName(int identifier, out string? name) {
            return ByHash.TryGetValue(identifier, out name);
        }

        /// <summary>
        ///     The name of a group in an open cache, or <c>null</c> when it has not been recovered.
        /// </summary>
        /// <remarks>
        ///     <c>null</c> covers a group with no entry and an identifier nothing hashes to alike,
        ///     because neither entitles a caller to show a name.
        /// </remarks>
        /// <param name="cache">The open cache.</param>
        /// <param name="groupId">The index-32 group id.</param>
        /// <returns>The recovered name, or <c>null</c>.</returns>
        public static string? NameOf(RSCache cache, int groupId) {
            if (cache == null)
                throw new ArgumentNullException(nameof(cache));

            RSArchiveEntry entry = cache.GetReferenceTable(RSConstants.LOADING_SPRITES).GetArchiveEntry(groupId);
            if (entry == null)
                return null;

            return TryGetName(entry.GetIdentifier(), out string? name) ? name : null;
        }

        private static Dictionary<int, string> BuildIndex() {
            var index = new Dictionary<int, string>(Candidates.Length);
            foreach (string candidate in Candidates)
                index[NameHasher.GetNameHash(candidate)] = candidate;
            return index;
        }
    }
}
