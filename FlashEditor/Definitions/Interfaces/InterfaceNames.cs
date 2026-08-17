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
    ///     hash-keyed readings of it agree completely - and from string literals harvested out of
    ///     the 637 client and the server. Those routes are <i>attested</i>: a source names the
    ///     interface and the cache agrees. Everything after them is <i>generated</i>, by recombining
    ///     the vocabulary of the attested names, and ships only against the bar set out below.
    ///     Component names are generated, by expanding each verified group name with that same
    ///     vocabulary, so only the suffix is ever guessed.</item>
    ///     </list>
    ///     <para>
    ///     <b>Exhaustive cracking is deliberately not done here, and a hash match is not on its own
    ///     a reason to ship a name.</b> djb2 is 32 bits, so a candidate set large enough will produce
    ///     a match for any target and the match means nothing. Recombination produced 610,956
    ///     candidates, which puts it firmly in that territory: for a name whose <i>only</i> evidence
    ///     is its own hash match, that is a guess wearing a name.
    ///     </para>
    ///     <para>
    ///     <b>The bar for adding a generated name, which is the rule and not a note about one
    ///     batch.</b> A name proposed by any process that enumerates candidates ships only with
    ///     corroboration independent of its own hash. Two things qualify, and one sibling is not a
    ///     family:
    ///     </para>
    ///     <list type="number">
    ///     <item>Two or more generated siblings sharing a leading token land on group ids within four
    ///     of one another. Adjacency is structure the generator never optimised for, so a set of
    ///     collisions cannot produce it - <c>npcchat_np1_overlay</c> through <c>np4_overlay</c> on
    ///     ids 90 to 93 is the case that earns its place this way.</item>
    ///     <item>The leading token already heads a name attested by a source, so the prefix is known
    ///     to be a real interface-name prefix in this cache rather than one the generator invented.
    ///     <c>banner_easter09</c> beside the attested <c>banner_easter08</c>, and
    ///     <c>cws_warning_33</c> beside 28 attested <c>cws_warning_N</c> siblings, qualify here.</item>
    ///     </list>
    ///     <para>
    ///     Applying that bar dropped four otherwise-matching names -
    ///     <c>zaros_staff_spells</c>, <c>task_main</c>, <c>task_side</c> and <c>black_overlay</c> -
    ///     whose leading tokens head no attested name, leaving 14 of the 18 recombination found.
    ///     Anything the bar rejects belongs in this remark, not in the table.
    ///     </para>
    ///     <para>
    ///     <b>How the residual false-positive rate is measured.</b> Not with decoys: a decoy is
    ///     gibberish whose leading token heads nothing, so the bar rejects it for the wrong reason
    ///     and the measurement is circular. The null used instead is a <i>foreign identifier set</i>
    ///     - index 8's 4593 real sprite-name hashes, sampled down to index 3's group count. Those are
    ///     real names of the same shape from the same naming culture, so a hit is a false positive of
    ///     exactly the feared kind, and the bar can be applied to it unchanged. Over 60 trials the
    ///     recombination candidate set scored <b>0.533</b> false hits per run raw and <b>0.000</b>
    ///     clearing the bar. The component route scored 0.000 over 40 whole-cache trials.
    ///     (Shifting the real identifiers is <i>not</i> a fair null either: djb2 clusters over short
    ///     strings, so a random shift moves the identifier set out of the region the candidates
    ///     occupy and flatters every route.)
    ///     </para>
    ///     <para>
    ///     One further candidate was withheld on evidence rather than on the bar. Group 1069 - the
    ///     highest group id the vanilla capture holds - has the identifier
    ///     <c>hash("golden_joystick")</c>, and the string occurs in the corpus exactly once, as
    ///     <c>"golden_joystick.ws"</c> passed to the CS2 open-URL opcode alongside
    ///     <c>download.ws</c> and <c>kbase/view.ws</c>. That is evidence it is a web path, not an
    ///     interface name, so the hash match is the kind of coincidence this class exists to refuse.
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

        /// <summary>
        ///     The identifier a component should carry once it has been renumbered.
        /// </summary>
        /// <remarks>
        ///     <b>Almost always the one it already had.</b> A name hash is stored, not derived, so
        ///     moving a component moves its name with it and nothing about the new id enters into
        ///     it. There is exactly one exception, and it is the one case where the stored hash is
        ///     a statement about the id rather than about the component: the generated
        ///     <c>com_&lt;fileId&gt;</c> convention. Where a component's identifier is
        ///     <c>hash("com_" + oldFileId)</c>, carrying it unchanged leaves a component at id 3
        ///     called <c>com_5</c>, which is not a name anything can resolve.
        ///     <para>
        ///     The convention is safe to act on because it proves itself on every row rather than
        ///     in aggregate: extending <c>com_&lt;N&gt;</c> over N in 0..3,999 matches 9,219
        ///     component hashes, and in 9,219 of 9,219 the N equals that component's own file id,
        ///     with no exceptions. The test here is the same one <see cref="ComponentName"/> makes -
        ///     re-hash the candidate and require it to reproduce what is stored - so a component
        ///     whose bespoke name merely looks generated is left alone.
        ///     </para>
        /// </remarks>
        /// <param name="oldFileId">The file id it is moving from.</param>
        /// <param name="newFileId">The file id it is moving to.</param>
        /// <param name="identifier">The identifier the table holds for it now.</param>
        /// <returns>The identifier to store against the new id.</returns>
        public static int MovedIdentifier(int oldFileId, int newFileId, int identifier) {
            if (identifier == Unnamed || oldFileId == newFileId)
                return identifier;

            return NameHasher.GetNameHash("com_" + oldFileId) == identifier
                ? NameHasher.GetNameHash("com_" + newFileId)
                : identifier;
        }
    }
}
