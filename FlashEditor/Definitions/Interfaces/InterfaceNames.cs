using System.Collections.Generic;
using FlashEditor.Cache.Util;

namespace FlashEditor.Definitions.Interfaces {
    /// <summary>
    ///     Turns index 3's stored name hashes back into names, where that can be done provably.
    /// </summary>
    /// <remarks>
    ///     Index 3 carries identifiers at both levels, and the sentinel is <b>-1</b> rather than 0 -
    ///     <c>VersionTable.java:145-147</c> pre-fills the array with -1 and overwrites it only for
    ///     declared entries, so a stored -1 is the format's own way of saying unnamed. No identifier
    ///     in either cache is zero. The vanilla b639 capture names every one of its 1067 groups and
    ///     all 40,883 of its components; the repack leaves 11 of 1078 groups and 1721 of 42,256
    ///     components at -1.
    ///     <para>
    ///     <b>A hash is not a name, and this class never pretends otherwise.</b> Every route below
    ///     proves itself per row: the candidate is re-hashed and compared against what the loaded
    ///     cache holds for that exact group, and for a component that exact file within it. A
    ///     candidate that is wrong, or right for a different build, therefore reads as <i>unnamed</i>
    ///     rather than as a false name. That property is the whole design, and it is pinned by
    ///     <c>RealCacheInterfaceNameTests</c>, which re-derives every displayed name's hash over the
    ///     whole of index 3 and also checks that corrupting an identifier by one bit suppresses the
    ///     name - a check that never fires is not a check.
    ///     </para>
    ///     <list type="bullet">
    ///     <item>The <c>com_&lt;fileId&gt;</c> rule for components, recomputed from the row's own
    ///     file id rather than looked up. It resolves every component that carries a generated name:
    ///     9249 in the vanilla capture, 9219 in the repack.</item>
    ///     <item><see cref="InterfaceNameTable"/>, a table of candidates. Group names came from a
    ///     467-row list keyed by group id in the sibling HydraScape repository - 416 of its rows
    ///     verify at the exact id it states and none names a different group, so the id-keyed and
    ///     hash-keyed readings of it agree completely - from string literals harvested out of the
    ///     637 client and the server, and from recombining the vocabulary of whatever was already
    ///     verified. Component names came from expanding each verified group name with that same
    ///     vocabulary.</item>
    ///     </list>
    ///     <para>
    ///     <b>Exhaustive cracking is deliberately not done here, and recombination is not cracking.</b>
    ///     djb2 is 32 bits, so a candidate set large enough will produce a match for any target and
    ///     the match means nothing. What separates the two is a measured null: generate decoys of the
    ///     same length and character classes as the candidates, hash them against the real
    ///     identifiers, and keep a route only while its real hit count towers over its decoy count.
    ///     Shifting the identifiers instead is <i>not</i> a fair null - djb2 clusters over short
    ///     strings and a random shift moves the identifier set out of the region the candidates
    ///     occupy, which flatters every route.
    ///     </para>
    ///     <para>
    ///     One candidate was withheld under that standard. Group 1069 - the highest group id the
    ///     vanilla capture holds - has the identifier <c>hash("golden_joystick")</c>, and the string
    ///     occurs in the corpus exactly once, as <c>"golden_joystick.ws"</c> passed to the CS2
    ///     open-URL opcode alongside <c>download.ws</c> and <c>kbase/view.ws</c>. That is evidence it
    ///     is a web path, not an interface name, so the hash match is the kind of coincidence this
    ///     class exists to refuse. It is recorded here rather than shipped.
    ///     </para>
    /// </remarks>
    public static class InterfaceNames {
        /// <summary>The identifier value that means "unnamed".</summary>
        public const int Unnamed = -1;

        /// <summary>
        ///     The name of a group, or null when nothing verifiable is known.
        /// </summary>
        /// <remarks>
        ///     The candidate is only returned when it hashes to the identifier the table actually
        ///     holds. That check is what stops the table from being a second, unfalsifiable source of
        ///     truth: repoint the editor at a different cache and a name that no longer fits simply
        ///     stops being shown.
        /// </remarks>
        /// <param name="groupId">The group id.</param>
        /// <param name="identifier">The identifier the reference table holds for it.</param>
        /// <returns>The name, or null.</returns>
        public static string? GroupName(int groupId, int identifier) {
            if (identifier == Unnamed)
                return null;

            return InterfaceNameTable.Groups.TryGetValue(groupId, out string? candidate) &&
                   NameHasher.GetNameHash(candidate) == identifier
                ? candidate
                : null;
        }

        /// <summary>
        ///     The name of a component, or null when nothing verifiable is known.
        /// </summary>
        /// <remarks>
        ///     The generated <c>com_&lt;fileId&gt;</c> name is tried first because it is recomputed
        ///     from the row itself and so needs no table at all. The table is consulted only for the
        ///     components that carry a bespoke name, and its entry is proved the same way before it
        ///     is returned.
        ///     <para>
        ///     <paramref name="groupId"/> is required rather than convenient. A component id is only
        ///     meaningful inside its group - file 3 of one interface has nothing to do with file 3 of
        ///     another - so a lookup keyed on the file id alone would hand one interface's name to
        ///     every other interface's third component. The hash check would catch it, but the call
        ///     would be wrong in principle before it was caught.
        ///     </para>
        /// </remarks>
        /// <param name="groupId">The interface the component belongs to.</param>
        /// <param name="fileId">The component's file id within that interface.</param>
        /// <param name="identifier">The identifier the reference table holds for it.</param>
        /// <returns>The name, or null.</returns>
        public static string? ComponentName(int groupId, int fileId, int identifier) {
            if (identifier == Unnamed)
                return null;

            string generated = "com_" + fileId;
            if (NameHasher.GetNameHash(generated) == identifier)
                return generated;

            return InterfaceNameTable.Components.TryGetValue(groupId, out Dictionary<int, string>? inGroup) &&
                   inGroup.TryGetValue(fileId, out string? candidate) &&
                   NameHasher.GetNameHash(candidate) == identifier
                ? candidate
                : null;
        }
    }
}
