using FlashEditor;
using Xunit;

namespace FlashEditor.Tests.Definitions
{
    public class NPCDefinitionCodecTests
    {
        /// <summary>
        /// Opcode 121 stores per-model translations as a sparse array: the decoder sizes it to
        /// modelIds.Length but only fills the indices actually present in the file, so unpopulated
        /// slots stay null. The encoder must therefore declare the number of records it actually
        /// writes, not the array length.
        ///
        /// Regression test: a definition whose translations array has a null slot must survive
        /// Decode -> Encode -> Decode with both the translations and the opcodes that follow 121
        /// intact. Declaring the wrong count makes the decoder overrun into opcode 122's bytes.
        /// </summary>
        [Fact]
        public void NPCDefinition_SparseTranslations_RoundTrip_DoesNotCorruptFollowingOpcodes()
        {
            var def = DecodeSparseTranslationDefinition();

            // Sanity: the hand-crafted stream decoded into the sparse shape we intended
            AssertSparseTranslations(def);
            Assert.Equal(4242, def.hitbarSprite);
            Assert.Equal(777, def.height);

            // Round-trip. With the count bug, Encode declares 3 records but writes 2, so the
            // decoder consumes 4 bytes belonging to opcode 122 and everything after it shifts.
            var def2 = new NPCDefinition(new JagStream(def.Encode().ToArray()));

            AssertSparseTranslations(def2);
            Assert.Equal(4242, def2.hitbarSprite);
            Assert.Equal(777, def2.height);

            // A second encode must be byte-identical to the first: the encoding is now stable.
            Assert.Equal(def.Encode().ToArray(), def2.Encode().ToArray());
        }

        /// <summary>
        /// A fully populated translations array has no null slots, so it round-trips even with
        /// the count bug present. Guards the fix against regressing the dense case.
        /// </summary>
        [Fact]
        public void NPCDefinition_DenseTranslations_RoundTrips()
        {
            var s = NewStreamWithEncoderPrerequisites(modelCount: 2);

            // 121: two models, both populated
            s.WriteByte(121);
            s.WriteByte(2);
            s.WriteByte(0); s.WriteByte(1); s.WriteByte(2); s.WriteByte(3);
            s.WriteByte(1); s.WriteByte(4); s.WriteByte(5); s.WriteByte(6);

            s.WriteByte(122); s.WriteShort(4242);
            s.WriteByte(123); s.WriteShort(777);
            s.WriteByte(0);
            s.Flip();

            var def = new NPCDefinition(new JagStream(s.ToArray()));
            var def2 = new NPCDefinition(new JagStream(def.Encode().ToArray()));

            Assert.NotNull(def2.translations);
            Assert.Equal(2, def2.translations.Length);
            Assert.Equal(new[] { 1, 2, 3 }, def2.translations[0]);
            Assert.Equal(new[] { 4, 5, 6 }, def2.translations[1]);
            Assert.Equal(4242, def2.hitbarSprite);
            Assert.Equal(777, def2.height);
        }

        /// <summary>
        /// Builds a definition with three model ids but translations for only slots 0 and 2,
        /// leaving slot 1 null, followed by opcodes 122 and 123 so an overrun is observable.
        /// </summary>
        private static NPCDefinition DecodeSparseTranslationDefinition()
        {
            var s = NewStreamWithEncoderPrerequisites(modelCount: 3);

            // 121: three model slots, but only two carry a translation record
            s.WriteByte(121);
            s.WriteByte(2);                                            // record count
            s.WriteByte(0); s.WriteByte(1); s.WriteByte(2); s.WriteByte(3);   // slot 0
            s.WriteByte(2); s.WriteByte(4); s.WriteByte(5); s.WriteByte(6);   // slot 2 (slot 1 stays null)

            s.WriteByte(122); s.WriteShort(4242);
            s.WriteByte(123); s.WriteShort(777);
            s.WriteByte(0);
            s.Flip();

            return new NPCDefinition(new JagStream(s.ToArray()));
        }

        /// <summary>
        /// Emits the opcodes Encode() dereferences unconditionally, so the resulting definition
        /// can be re-encoded without tripping over a null array.
        /// </summary>
        private static JagStream NewStreamWithEncoderPrerequisites(int modelCount)
        {
            var s = new JagStream();

            // 1: model ids
            s.WriteByte(1);
            s.WriteByte((byte) modelCount);
            for (int i = 0; i < modelCount; i++)
                s.WriteShort((i + 1) * 10);

            s.WriteByte(40); s.WriteByte(0);   // recolour
            s.WriteByte(41); s.WriteByte(0);   // retexture
            s.WriteByte(42); s.WriteByte(0);   // palette
            s.WriteByte(60); s.WriteByte(0);   // dialogue models
            s.WriteByte(160); s.WriteByte(0);  // campaigns

            // 106: morph table (varbit/varp absent, one morph id)
            s.WriteByte(106);
            s.WriteShort(unchecked((short) 0xFFFF));
            s.WriteShort(unchecked((short) 0xFFFF));
            s.WriteByte(0);
            s.WriteShort(unchecked((short) 0xFFFF));

            return s;
        }

        private static void AssertSparseTranslations(NPCDefinition def)
        {
            Assert.NotNull(def.translations);
            Assert.Equal(3, def.translations.Length);
            Assert.Equal(new[] { 1, 2, 3 }, def.translations[0]);
            Assert.Null(def.translations[1]);
            Assert.Equal(new[] { 4, 5, 6 }, def.translations[2]);
        }
    }
}
