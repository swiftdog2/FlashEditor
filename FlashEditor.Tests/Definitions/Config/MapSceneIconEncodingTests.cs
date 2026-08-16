using System.Linq;
using FlashEditor.Definitions.Config;
using FlashEditor.IO;
using Xunit;

namespace FlashEditor.Tests.Definitions.Config {
    /// <summary>
    ///     The two encodings of "no icon" in config group 34, and that an edit cannot swap one for
    ///     the other by accident.
    /// </summary>
    /// <remarks>
    ///     <b>Opcode 4 and the opcode being absent both mean "draw nothing".</b> Opcode 4 sets the
    ///     client's <c>anInt114</c> to -1 explicitly and the constructor leaves it at the same -1
    ///     (Class9.java:247-250), so the decoded value cannot tell them apart - only the opcode list
    ///     can. Seven of the hundred records in both caches carry opcode 4 and 93 carry opcode 1, so
    ///     both forms are live in shipped data and re-encoding one as the other rewrites a file
    ///     nobody edited.
    ///     <para>
    ///     These are hand-built byte strings rather than cache records on purpose. A round trip of
    ///     this encoder against this decoder proves nothing - two real defects in this project
    ///     survived exactly that - so the inputs here are written out as the bytes the format
    ///     defines and the assertions are against those bytes, not against a re-encode of them.
    ///     </para>
    ///     <para>
    ///     The byte-identity sweep over group 34 is a different claim and does not cover any of
    ///     this: it proves an <b>unedited</b> record comes back unchanged. Four real defects in this
    ///     repository have lived in that gap.
    ///     </para>
    /// </remarks>
    public sealed class MapSceneIconEncodingTests {
        /// <summary>A record storing opcode 4 keeps opcode 4 when the sprite is set and set back.</summary>
        /// <remarks>
        ///     The case the whole design exists for. Assigning the property alone would leave opcode
        ///     4 in the stream, so the first half of this would re-encode identically and the edit
        ///     would vanish with no error anywhere.
        /// </remarks>
        [Fact]
        public void AnExplicitNoIconRecordSetAndSetBackLandsOnItsOriginalBytes() {
            //Opcode 3 first so the swap has to preserve a position rather than an empty list, and
            //because not one sibling group of this index stores its opcodes in ascending order.
            byte[] stored = { 3, 4, 0 };

            MapSceneIconDefinition record = Decode(stored);
            Assert.Equal("no icon, stored as opcode 4", record.DescribeAbsentIconEncoding());

            record.SetSpriteGroupId(55);
            Assert.Equal(new byte[] { 3, 1, 0, 55, 0 }, record.Encode().ToArray());

            record.SetSpriteGroupId(-1);
            Assert.Equal(stored, record.Encode().ToArray());
        }

        /// <summary>A record storing opcode 1 keeps opcode 1 when the sprite is set and set back.</summary>
        [Fact]
        public void AnIconRecordSetAndSetBackLandsOnItsOriginalBytes() {
            byte[] stored = { 1, 0, 93, 3, 0 };

            MapSceneIconDefinition record = Decode(stored);
            Assert.Equal(93, record.SpriteGroupId);

            record.SetSpriteGroupId(-1);
            Assert.Equal(new byte[] { 4, 3, 0 }, record.Encode().ToArray());

            record.SetSpriteGroupId(93);
            Assert.Equal(stored, record.Encode().ToArray());
        }

        /// <summary>
        ///     Clearing an icon writes opcode 4 rather than -1 through opcode 1's unsigned short.
        /// </summary>
        /// <remarks>
        ///     The failure this rules out is silent in a way the round trip above would not catch on
        ///     its own: opcode 1 is a <c>readUnsignedShort</c>, so a -1 written through it stores
        ///     <c>0xFFFF</c> and decodes back as 65535 - a record of the right length naming sprite
        ///     group 65535, which the reference table does not declare.
        /// </remarks>
        [Fact]
        public void ClearingAnIconDoesNotWriteMinusOneThroughOpcodeOne() {
            MapSceneIconDefinition record = Decode(new byte[] { 1, 0, 93, 0 });
            record.SetSpriteGroupId(-1);

            byte[] encoded = record.Encode().ToArray();

            Assert.DoesNotContain((byte) 0xFF, encoded);
            Assert.Equal(-1, Decode(encoded).SpriteGroupId);
        }

        /// <summary>
        ///     The opcode is swapped where it stood, never appended.
        /// </summary>
        /// <remarks>
        ///     Position is the whole difference between an edit and a rewrite on this index. An
        ///     encoder that dropped the old opcode and appended the new one produces a record of the
        ///     right length with a byte moved, which the commit path then stages as a real change -
        ///     and the archive CRC covers the stored bytes, so that drags in the reference-table
        ///     entry of every archive packed alongside it for an edit that netted nothing.
        /// </remarks>
        [Fact]
        public void TheSwappedOpcodeKeepsThePositionTheRecordStoredItAt() {
            MapSceneIconDefinition record = Decode(new byte[] { 4, 2, 0x11, 0x22, 0x33, 3, 0 });

            record.SetSpriteGroupId(7);

            Assert.Equal(new[] { 1, 2, 3 },
                record.DecodedOpcodes.Select(entry => entry.Opcode).ToArray());
        }

        /// <summary>
        ///     A record carrying neither opcode keeps carrying neither when nothing is set.
        /// </summary>
        /// <remarks>
        ///     The third encoding of "no icon", and the only case where this editor picks a form:
        ///     with neither opcode stored there is nothing to preserve, so the codec's own
        ///     <c>AddedOpcodes</c> decides, and it appends nothing while the field is untouched.
        ///     This form occurs in neither cache - all 100 records carry opcode 1 or opcode 4 - so
        ///     no sweep exercises it.
        /// </remarks>
        [Fact]
        public void ARecordCarryingNeitherOpcodeGainsNeither() {
            byte[] stored = { 3, 0 };

            MapSceneIconDefinition record = Decode(stored);
            Assert.Equal("no icon, stored as the opcode being absent",
                record.DescribeAbsentIconEncoding());

            Assert.Equal(stored, record.Encode().ToArray());
        }

        private static MapSceneIconDefinition Decode(byte[] stored) {
            return new MapSceneIconDefinition { Id = 0 }.Decode(new JagStream(stored));
        }
    }
}
