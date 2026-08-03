using System.Collections.Generic;
using FlashEditor.Cache.Util;

namespace FlashEditor.Definitions.Interfaces {
    /// <summary>
    ///     Turns index 3's stored name hashes back into names, where that can be done provably.
    /// </summary>
    /// <remarks>
    ///     Index 3 carries identifiers at both levels, and the sentinel is <b>-1</b> rather than 0 -
    ///     <c>VersionTable.java:145-147</c> pre-fills the array with -1 and overwrites it only for
    ///     declared entries, so a stored -1 is the format's own way of saying unnamed. Measured over
    ///     this cache: no identifier anywhere is zero, 11 of the 1,078 groups are -1 and 1,721 of the
    ///     42,256 components are.
    ///     <para>
    ///     <b>A hash is not a name, and this class never pretends otherwise.</b> Only two routes are
    ///     used, and both prove themselves per row:
    ///     </para>
    ///     <list type="bullet">
    ///     <item>A curated list of plain-English candidates, each hashed and matched. Every entry
    ///     below was checked against the shipped table; <c>loginscreen</c> and <c>lobbyscreen</c> are
    ///     corroborated independently as the only two names the client asks index 3 for by name, at
    ///     <c>InterfaceSettings.java:356-358</c>.</item>
    ///     <item>The <c>com_&lt;fileId&gt;</c> rule for components. 9,219 component hashes match
    ///     <c>com_N</c> for some N under 4,000, and in <b>9,219 of 9,219</b> that N is the
    ///     component's own file id. A join that is checkable on every single row is the standard
    ///     <c>CLAUDE.md</c> demands after the track-name failure, so this one is applied by
    ///     recomputing the hash for the row's own file id rather than by searching a table.</item>
    ///     </list>
    ///     <para>
    ///     <b>Exhaustive cracking is deliberately not done here.</b> A 32-bit hash over a
    ///     37-character alphabet collides roughly once per target at six characters, so a lone
    ///     cracked candidate is a guess wearing a name. The remaining 31,316 bespoke component names
    ///     and most group names stay as hashes until somebody adds a verified entry below.
    ///     </para>
    /// </remarks>
    public static class InterfaceNames {
        /// <summary>The identifier value that means "unnamed".</summary>
        public const int Unnamed = -1;

        /// <summary>
        ///     Group names verified against this cache's own reference table.
        /// </summary>
        /// <remarks>
        ///     Keyed by group id rather than by hash so a reader can see which interface each claim is
        ///     about. <see cref="GroupName"/> re-hashes the name and checks it against the stored
        ///     identifier before returning it, so a wrong entry here shows up as no name rather than
        ///     as a false one.
        /// </remarks>
        private static readonly Dictionary<int, string> CuratedGroupNames = new Dictionary<int, string> {
            { 34, "notes" },
            { 64, "chat1" },
            { 65, "chat2" },
            { 66, "chat3" },
            { 67, "chat4" },
            { 105, "stockmarket" },
            { 137, "chatdefault" },
            { 139, "gnomeball" },
            { 149, "inventory" },
            { 182, "logout" },
            { 192, "magic" },
            { 228, "multi2" },
            { 230, "multi3" },
            { 232, "multi4" },
            { 234, "multi5" },
            { 261, "options" },
            { 271, "prayer" },
            { 320, "stats" },
            { 387, "wornitems" },
            { 464, "emotes" },
            { 548, "toplevel" },
            { 590, "clansetup" },
            { 596, "login" },
            { 744, "loginscreen" },
            { 755, "worldmap" },
            { 906, "lobbyscreen" },
            { 952, "bind" }
        };

        /// <summary>
        ///     The name of a group, or null when nothing verifiable is known.
        /// </summary>
        /// <remarks>
        ///     The curated name is only returned when it hashes to the identifier the table actually
        ///     holds. That check is what stops the list from being a second, unfalsifiable source of
        ///     truth: repoint the editor at a different cache and a name that no longer fits simply
        ///     stops being shown.
        /// </remarks>
        /// <param name="groupId">The group id.</param>
        /// <param name="identifier">The identifier the reference table holds for it.</param>
        /// <returns>The name, or null.</returns>
        public static string? GroupName(int groupId, int identifier) {
            if (identifier == Unnamed)
                return null;

            if (CuratedGroupNames.TryGetValue(groupId, out string? candidate) &&
                NameHasher.GetNameHash(candidate) == identifier)
                return candidate;

            return null;
        }

        /// <summary>
        ///     The name of a component, or null when nothing verifiable is known.
        /// </summary>
        /// <remarks>
        ///     Only the self-proving <c>com_&lt;fileId&gt;</c> rule. The hash is recomputed from the
        ///     row's own file id and compared, so the name is never asserted - it is confirmed.
        /// </remarks>
        /// <param name="fileId">The component's file id within its group.</param>
        /// <param name="identifier">The identifier the reference table holds for it.</param>
        /// <returns>The name, or null.</returns>
        public static string? ComponentName(int fileId, int identifier) {
            if (identifier == Unnamed)
                return null;

            string candidate = "com_" + fileId;
            return NameHasher.GetNameHash(candidate) == identifier ? candidate : null;
        }
    }
}
