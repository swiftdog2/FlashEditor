using System.Collections.Generic;

namespace FlashEditor.Definitions.QuickChat {
    /// <summary>
    ///     How many 16-bit words a quick-chat substitution slot stores after its type id.
    /// </summary>
    /// <remarks>
    ///     <b>This table is not in the cache.</b> Opcode 3 of a message record is a list of
    ///     <c>{u16 typeId, N x u16}</c> entries and nothing in the file says what <c>N</c> is - it is
    ///     <c>Class348.anInt2915</c>, hardcoded in the client, so a wrong entry here desynchronises
    ///     every field after it and the record still appears to parse.
    ///     <para>
    ///     Ported from the 637 client. <c>Class93_Sub1.method906</c> (Class93_Sub1.java:236-240)
    ///     returns the fourteen instances; each is built as
    ///     <c>Class348(id, anInt2913, anInt2910, anInt2915)</c> (Class348.java:72-80), so the word
    ///     count is the <b>fourth</b> constructor argument, not the second.
    ///     <c>Node_Sub10_Sub7.method1025</c> (Node_Sub10_Sub7.java:51-60) looks an entry up by the
    ///     first argument, and <c>Node_Sub46_Sub11.java:146-149</c> is the loop that consumes the
    ///     words.
    ///     </para>
    ///     <para>
    ///     The other two fields of a <c>Class348</c> are deliberately not ported: neither is needed
    ///     to size the payload. <c>anInt2913</c> is a bit width used when the client sends a chosen
    ///     slot value (Node_Sub46_Sub11.java:195-198) and <c>anInt2910</c> selects how the value is
    ///     read back for display (:110-112).
    ///     </para>
    ///     <para>
    ///     Corroborated by the 639 data rather than trusted: index 24's message group uses twelve of
    ///     these ids and every one of its records consumes to the exact byte under this table.
    ///     Ids 3 and 5 exist in neither the client nor either cache.
    ///     </para>
    /// </remarks>
    public static class QuickChatSlotType {
        /// <summary>
        ///     Type id to trailing word count, exactly as the 637 client hardcodes it.
        /// </summary>
        /// <remarks>
        ///     Each row cites the field that holds it, so any one of them can be re-checked against
        ///     the client in seconds:
        ///     0 Class151_Sub9.java:18, 1 Class77_Sub1.java:16, 2 Class4.java:18,
        ///     4 GameConfig.java:15, 6 Class42_Sub3.java:14, 7 Node_Sub36.java:14,
        ///     8 Class186.java:14, 9 aa_Sub3.java:23, 10 Class359.java:3,
        ///     11 Class151_Sub7.java:15, 12 Class218.java:14, 13 JS5Archive.java:9,
        ///     14 Node_Sub5_Sub1.java:12, 15 Class238.java:11.
        /// </remarks>
        private static readonly Dictionary<int, int> WordCounts = new Dictionary<int, int> {
            { 0, 1 }, { 1, 0 }, { 2, 0 }, { 4, 1 }, { 6, 2 }, { 7, 1 }, { 8, 1 },
            { 9, 1 }, { 10, 0 }, { 11, 2 }, { 12, 0 }, { 13, 0 }, { 14, 1 }, { 15, 0 }
        };

        /// <summary>Every slot type id the 637 client knows, ascending.</summary>
        public static IReadOnlyCollection<int> KnownTypeIds { get; } = new List<int>(WordCounts.Keys).AsReadOnly();

        /// <summary>
        ///     How many words follow <paramref name="slotTypeId"/>, or zero when the client has no
        ///     entry for it.
        /// </summary>
        /// <remarks>
        ///     Zero for an unknown type is the client's own behaviour and not a fallback invented
        ///     here: <c>Node_Sub46_Sub11.java:144</c> guards the whole block on
        ///     <c>class348 != null</c>, so an unrecognised type consumes its two id bytes and no
        ///     words at all, and the rest of the record continues to parse from there. A decoder
        ///     that instead skipped a guessed number of bytes would disagree with the client on the
        ///     very record the client is already reading.
        /// </remarks>
        /// <param name="slotTypeId">The stored slot type id.</param>
        /// <returns>The trailing word count.</returns>
        public static int WordCount(int slotTypeId) {
            return WordCounts.TryGetValue(slotTypeId, out int words) ? words : 0;
        }

        /// <summary>Whether the 637 client has a definition for this slot type.</summary>
        /// <param name="slotTypeId">The stored slot type id.</param>
        /// <returns>True when the type is one of the fourteen.</returns>
        public static bool IsKnown(int slotTypeId) => WordCounts.ContainsKey(slotTypeId);
    }
}
