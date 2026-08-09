using System;
using System.Collections.Generic;
using FlashEditor.Cache;
using FlashEditor.Cache.Util;

namespace FlashEditor.Definitions.Fonts {
    /// <summary>
    ///     Recovers the name of a font group from the identifier its reference table stores.
    /// </summary>
    /// <remarks>
    ///     Index 13 sets the identifiers flag, so every group carries <c>hash(name)</c> and nothing
    ///     else - the name itself is not in the cache. A name is therefore only ever <i>recovered</i>
    ///     here, by hashing a candidate and checking it against the stored identifier, and reported
    ///     only when the two agree. <b>Nothing is reported on a guess.</b> A plausible font name that
    ///     hashes to something else is not a lead, it is noise, and this cache confirms a plausible
    ///     mapping by accident more readily than almost anything else in it.
    ///     <para>
    ///     Three of the names below come straight from the client - <c>Class84.java:23-26</c> asks
    ///     index 8 for <c>p11_full</c>, <c>p12_full</c> and <c>b12_full</c> by name, and index 13
    ///     shares index 8's id space and name hashes exactly. The rest were recovered by hashing
    ///     candidates. Eleven of the twenty-five groups in both supported caches are named this way;
    ///     the other fourteen have not fallen to any wordlist tried, and are left unnamed rather than
    ///     filled in.
    ///     </para>
    /// </remarks>
    public static class FontNames {
        /// <summary>
        ///     Every font name whose hash matches a group both supported caches declare.
        /// </summary>
        /// <remarks>
        ///     A candidate list, not a mapping: the ids are deliberately absent, because the id a
        ///     name lands on is derived from the hash at load rather than written down. A cache that
        ///     renumbered its font groups would still be named correctly.
        /// </remarks>
        private static readonly string[] Candidates = {
            "p11_full",
            "p12_full",
            "b12_full",
            "q8_full",
            "lunar_alphabet",
            "lunar_alphabet_lrg",
            "barbassault_font",
            "surok_font",
            "verdana_11pt_regular",
            "verdana_13pt_regular",
            "verdana_15pt_regular"
        };

        private static readonly Dictionary<int, string> ByHash = BuildIndex();

        /// <summary>The names this recovers, for a test that pins the set rather than a count.</summary>
        public static IReadOnlyCollection<string> KnownNames => Candidates;

        /// <summary>
        ///     The name whose hash is <paramref name="identifier"/>, when one is known.
        /// </summary>
        /// <param name="identifier">The identifier the reference table stores for the group.</param>
        /// <param name="name">The recovered name, or null.</param>
        /// <returns>Whether a candidate hashes to that identifier.</returns>
        public static bool TryGetName(int identifier, out string? name) {
            if (ByHash.TryGetValue(identifier, out string? found)) {
                name = found;
                return true;
            }

            name = null;
            return false;
        }

        /// <summary>
        ///     The name of a font group in an open cache, or null when it has not been recovered.
        /// </summary>
        /// <remarks>
        ///     Null covers two different situations on purpose - a table with no identifiers at all,
        ///     and an identifier no candidate hashes to - because neither entitles a caller to show
        ///     a name and distinguishing them would only invite one to.
        /// </remarks>
        /// <param name="cache">The open cache.</param>
        /// <param name="fontId">The font id, which is the index-13 group id.</param>
        /// <returns>The recovered name, or null.</returns>
        public static string? NameOf(RSCache cache, int fontId) {
            if (cache == null)
                throw new ArgumentNullException(nameof(cache));

            RSArchiveEntry entry = cache.GetReferenceTable(RSConstants.FONTS_INDEX).GetArchiveEntry(fontId);
            if (entry == null)
                return null;

            int identifier = entry.GetIdentifier();
            return TryGetName(identifier, out string? name) ? name : null;
        }

        private static Dictionary<int, string> BuildIndex() {
            var index = new Dictionary<int, string>(Candidates.Length);
            foreach (string candidate in Candidates)
                index[NameHasher.GetNameHash(candidate)] = candidate;
            return index;
        }
    }
}
