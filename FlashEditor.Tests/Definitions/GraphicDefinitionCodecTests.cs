using System;
using System.Collections.Generic;
using System.Linq;
using FlashEditor.Definitions.SpotAnims;
using Xunit;

namespace FlashEditor.Tests.Definitions
{
    /// <summary>
    ///     The spot-animation codec against bytes lifted from a real revision-639 cache.
    /// </summary>
    /// <remarks>
    ///     Index 21 is byte-identical in both supported caches, so every fixture below addresses the
    ///     same bytes wherever the suite is pointed. Two thirds of this file is about the effect
    ///     opcodes instead, which occur nowhere at all: eight opcodes set the same two fields and
    ///     three of them set the same kind, so nothing in the data can say whether an encoder picked
    ///     the right one back out.
    /// </remarks>
    public sealed class GraphicDefinitionCodecTests
    {
        /// <summary>Graphic 0 (group 0 file 0): opcodes 2, 1 - animation before model.</summary>
        /// <remarks>
        ///     The very first record of the index is already out of ascending order, which is the
        ///     shortest possible demonstration that the order is a property of the file.
        /// </remarks>
        public static readonly byte[] AnimationBeforeModel =
        {
            0x02, 0x30, 0x46, 0x01, 0xC2, 0x68, 0x00
        };

        /// <summary>Graphic 111 (group 0 file 111): opcodes 1, 2, 8, 7 - contrast before ambient.</summary>
        public static readonly byte[] ContrastBeforeAmbient =
        {
            0x01, 0x94, 0xE2, 0x02, 0x02, 0x9B, 0x08, 0x28, 0x07, 0x28, 0x00
        };

        /// <summary>Graphic 9 (group 0 file 9): opcodes 1, 40 - two recolours and no animation.</summary>
        public static readonly byte[] WithRecolours =
        {
            0x01, 0x0C, 0x40, 0x28, 0x02, 0x00, 0x39, 0x00, 0x21, 0x00, 0x3D, 0x00,
            0x21, 0x00
        };

        /// <summary>Graphic 534 (group 2 file 22): opcodes 1, 2, 6, 10.</summary>
        /// <remarks>Carries the movement-interrupt flag, which 158 records in the index set.</remarks>
        public static readonly byte[] WithMovementFlag =
        {
            0x01, 0x71, 0xF7, 0x02, 0x1C, 0x6F, 0x06, 0x00, 0x5A, 0x0A, 0x00
        };

        /// <summary>Graphic 919 (group 3 file 151): opcodes 1, 2, 4, 5, 7, 8, 6.</summary>
        /// <remarks>
        ///     The widest shape in the index: both scales, both lighting fields and a rotation, with
        ///     the rotation written last.
        /// </remarks>
        public static readonly byte[] WithScalesAndRotation =
        {
            0x01, 0x4E, 0x06, 0x02, 0x14, 0x4E, 0x04, 0x00, 0x48, 0x05, 0x00, 0x48,
            0x07, 0x0F, 0x08, 0x0F, 0x06, 0x00, 0x5A, 0x00
        };

        /// <summary>Every captured record, with the graphic id it was read from.</summary>
        public static IEnumerable<object[]> EveryFixture()
        {
            yield return new object[] { 0, AnimationBeforeModel };
            yield return new object[] { 9, WithRecolours };
            yield return new object[] { 111, ContrastBeforeAmbient };
            yield return new object[] { 534, WithMovementFlag };
            yield return new object[] { 919, WithScalesAndRotation };
        }

        /// <summary>Every captured record consumes exactly and re-encodes to the bytes it came from.</summary>
        /// <param name="id">The graphic id, so a failure names it.</param>
        /// <param name="stored">The captured bytes.</param>
        [Theory]
        [MemberData(nameof(EveryFixture))]
        public void EveryCapturedRecordRoundTrips(int id, byte[] stored)
        {
            var stream = new JagStream(stored);
            var definition = new GraphicDefinition { Id = id }.Decode(stream);

            Assert.True(stored.Length == stream.Position,
                $"graphic {id} consumed {stream.Position} of its {stored.Length} bytes");
            Assert.True(stored.AsSpan().SequenceEqual(definition.Encode().ToArray()),
                $"graphic {id} did not re-encode to the bytes it was decoded from");
        }

        /// <summary>The widest captured record decodes to the fields the client reads out of it.</summary>
        /// <remarks>
        ///     Ambient and contrast are offsets rather than absolute values - the client builds the
        ///     model with 64 + ambient and 850 + contrast - which is the difference an editor would
        ///     otherwise show wrong and write back wrong.
        /// </remarks>
        [Fact]
        public void TheWidestRecordDecodesToItsFields()
        {
            var definition = new GraphicDefinition { Id = 919 }.Decode(new JagStream(WithScalesAndRotation));

            Assert.Equal(19974, definition.ModelId);
            Assert.Equal(5198, definition.AnimationId);
            Assert.Equal(72, definition.ScaleXZ);
            Assert.Equal(72, definition.ScaleY);
            Assert.Equal(90, definition.Rotation);
            Assert.True(definition.RotationIsApplied);
            Assert.Equal(15, definition.Ambient);
            Assert.Equal(15, definition.Contrast);
            Assert.Equal(GraphicDefinition.AmbientBase + 15, definition.EffectiveAmbient);
            Assert.Equal(GraphicDefinition.ContrastBase + 15, definition.EffectiveContrast);
            Assert.Equal(GraphicDefinition.NoEffectOpcode, definition.EffectOpcode);
        }

        /// <summary>Recolours decode as ordered (from, to) pairs.</summary>
        [Fact]
        public void RecoloursDecodeAsPairs()
        {
            var definition = new GraphicDefinition { Id = 9 }.Decode(new JagStream(WithRecolours));

            Assert.Equal(3136, definition.ModelId);
            Assert.Equal(-1, definition.AnimationId);
            Assert.Equal(new[] { 57, 61 }, definition.RecolourFrom);
            Assert.Equal(new[] { 33, 33 }, definition.RecolourTo);
            Assert.Empty(definition.RetextureFrom);
        }

        /// <summary>The recorded order is replayed rather than sorted.</summary>
        /// <param name="id">The graphic id.</param>
        /// <param name="stored">The captured bytes.</param>
        /// <param name="expected">The opcode sequence the file stores.</param>
        [Theory]
        [InlineData(0, new byte[] { 0x02, 0x30, 0x46, 0x01, 0xC2, 0x68, 0x00 }, new[] { 2, 1 })]
        [InlineData(111, new byte[] { 0x01, 0x94, 0xE2, 0x02, 0x02, 0x9B, 0x08, 0x28, 0x07, 0x28, 0x00 },
            new[] { 1, 2, 8, 7 })]
        public void TheStoredOpcodeOrderIsNotAscendingAndSurvives(int id, byte[] stored, int[] expected)
        {
            var definition = new GraphicDefinition { Id = id }.Decode(new JagStream(stored));

            Assert.Equal(expected, definition.Opcodes.Select(record => record.Opcode).ToArray());
            Assert.False(expected.SequenceEqual(expected.OrderBy(opcode => opcode)),
                $"graphic {id} carries its opcodes in ascending order, so it cannot show that the " +
                "recorded order is needed");
            Assert.Equal(stored, definition.Encode().ToArray());
        }

        /// <summary>An empty record keeps every default and stays a single terminator byte.</summary>
        [Fact]
        public void AnEmptyRecordKeepsItsDefaults()
        {
            var definition = new GraphicDefinition { Id = 0 }.Decode(new JagStream(new byte[] { 0 }));

            Assert.Equal(0, definition.ModelId);
            Assert.Equal(-1, definition.AnimationId);
            Assert.Equal(GraphicDefinition.DefaultScale, definition.ScaleXZ);
            Assert.Equal(GraphicDefinition.DefaultScale, definition.ScaleY);
            Assert.Equal(0, definition.Rotation);
            Assert.Equal(0, definition.Ambient);
            Assert.Equal(0, definition.Contrast);
            Assert.False(definition.RespectsMovementInterrupt);
            Assert.Equal(GraphicDefinition.NoEffectOpcode, definition.EffectOpcode);
            Assert.Equal(new byte[] { 0 }, definition.Encode().ToArray());
        }

        /// <summary>
        ///     A field stored at the value its own default would give is not dropped.
        /// </summary>
        /// <remarks>
        ///     SYNTHETIC in shape and real in the cache: one record stores scale 128, 17 store
        ///     ambient 0 and 31 store contrast 0, all of them writing exactly what an absent opcode
        ///     produces. Deciding what to write from the decoded value alone shortens every one of
        ///     them.
        /// </remarks>
        [Fact]
        public void FieldsStoredAtTheirDefaultAreNotDropped()
        {
            byte[] stored = { 0x04, 0x00, 0x80, 0x07, 0x00, 0x08, 0x00, 0x00 };

            var definition = new GraphicDefinition { Id = -1 }.Decode(new JagStream(stored));

            Assert.Equal(GraphicDefinition.DefaultScale, definition.ScaleXZ);
            Assert.Equal(0, definition.Ambient);
            Assert.Equal(0, definition.Contrast);
            Assert.Equal(stored, definition.Encode().ToArray());
        }

        /// <summary>
        ///     A rotation the client ignores is kept as it was stored.
        /// </summary>
        /// <remarks>
        ///     Only 90, 180 and 270 do anything (Class107.java:201-209). Normalising anything else to
        ///     0 would look like a tidy-up and would rewrite the record.
        /// </remarks>
        [Fact]
        public void ARotationTheClientIgnoresIsStillStored()
        {
            byte[] stored = { 0x06, 0x00, 0x2D, 0x00 };

            var definition = new GraphicDefinition { Id = -1 }.Decode(new JagStream(stored));

            Assert.Equal(45, definition.Rotation);
            Assert.False(definition.RotationIsApplied);
            Assert.Equal(stored, definition.Encode().ToArray());
        }

        /// <summary>Clearing the movement flag removes its opcode rather than leaving one behind.</summary>
        [Fact]
        public void ClearingTheMovementFlagRemovesItsOpcode()
        {
            var definition = new GraphicDefinition { Id = 534 }.Decode(new JagStream(WithMovementFlag));
            Assert.True(definition.RespectsMovementInterrupt);

            definition.RespectsMovementInterrupt = false;
            byte[] encoded = definition.Encode().ToArray();

            Assert.DoesNotContain((byte) 0x0A, encoded);
            Assert.False(new GraphicDefinition { Id = 534 }
                .Decode(new JagStream(encoded)).RespectsMovementInterrupt);

            definition.RespectsMovementInterrupt = true;
            Assert.True(new GraphicDefinition { Id = 534 }
                .Decode(new JagStream(definition.Encode().ToArray())).RespectsMovementInterrupt);
        }

        /// <summary>Every effect opcode decodes to the kind and parameter the client sets.</summary>
        /// <remarks>
        ///     SYNTHETIC, and unavoidably so: none of the eight occurs in either cache, so no sweep
        ///     covers a single one of them. They are implemented anyway for the reason
        ///     <c>CLAUDE.md</c> gives for the unreachable reference-table branches - the first record
        ///     that does carry one is mis-parsed from that byte onward, and nothing would catch it.
        /// </remarks>
        /// <param name="stored">A record carrying one effect opcode.</param>
        /// <param name="opcode">The opcode it carries.</param>
        /// <param name="kind">The effect kind that opcode selects.</param>
        /// <param name="parameter">The parameter it leaves behind.</param>
        [Theory]
        [InlineData(new byte[] { 0x09, 0x00 }, 9, 3, 8224)]
        [InlineData(new byte[] { 0x0B, 0x00 }, 11, 1, -1)]
        [InlineData(new byte[] { 0x0C, 0x00 }, 12, 4, -1)]
        [InlineData(new byte[] { 0x0D, 0x00 }, 13, 5, -1)]
        [InlineData(new byte[] { 0x0E, 0x07, 0x00 }, 14, 2, 1792)]
        [InlineData(new byte[] { 0x0F, 0x12, 0x34, 0x00 }, 15, 3, 0x1234)]
        [InlineData(new byte[] { 0x10, 0x00, 0x01, 0x02, 0x03, 0x00 }, 16, 3, 0x00010203)]
        public void EveryEffectOpcodeRoundTrips(byte[] stored, int opcode, int kind, int parameter)
        {
            var stream = new JagStream(stored);
            var definition = new GraphicDefinition { Id = -1 }.Decode(stream);

            Assert.Equal(stored.Length, stream.Position);
            Assert.Equal(opcode, definition.EffectOpcode);
            Assert.Equal(kind, definition.EffectKind);
            Assert.Equal(parameter, definition.EffectParameter);
            Assert.Equal(stored, definition.Encode().ToArray());
        }

        /// <summary>
        ///     Three opcodes produce the same effect kind, and which one a record carried survives.
        /// </summary>
        /// <remarks>
        ///     SYNTHETIC. Opcodes 9, 15 and 16 all set kind 3 and write the parameter in three
        ///     different widths, so an encoder that chose an opcode from the decoded pair would
        ///     rewrite two records in three - a one-byte record as three bytes, or a five-byte one as
        ///     three. The opcode is therefore stored rather than recomputed.
        /// </remarks>
        [Fact]
        public void TheEffectOpcodeIsStoredBecauseTheKindDoesNotIdentifyIt()
        {
            byte[] viaNine = { 0x09, 0x00 };
            byte[] viaSixteen = { 0x10, 0x00, 0x00, 0x20, 0x20, 0x00 };

            var fromNine = new GraphicDefinition { Id = -1 }.Decode(new JagStream(viaNine));
            var fromSixteen = new GraphicDefinition { Id = -1 }.Decode(new JagStream(viaSixteen));

            //Identical decoded state, different bytes. Only the recorded opcode tells them apart.
            Assert.Equal(fromNine.EffectKind, fromSixteen.EffectKind);
            Assert.Equal(fromNine.EffectParameter, fromSixteen.EffectParameter);
            Assert.NotEqual(fromNine.EffectOpcode, fromSixteen.EffectOpcode);

            Assert.Equal(viaNine, fromNine.Encode().ToArray());
            Assert.Equal(viaSixteen, fromSixteen.Encode().ToArray());
        }

        /// <summary>
        ///     A record carrying two effect opcodes keeps the superseded one exactly as it was read.
        /// </summary>
        /// <remarks>
        ///     SYNTHETIC. The fields remember only the last opcode's value, so the earlier one exists
        ///     nowhere but in its recorded payload bytes - the same shape as a repeated scalar opcode
        ///     on index 20, reached here through two different opcodes rather than one twice.
        /// </remarks>
        [Fact]
        public void ASupersededEffectOpcodeIsReplayedRatherThanRewritten()
        {
            byte[] stored = { 0x0F, 0x00, 0x64, 0x10, 0x00, 0x00, 0x01, 0xF4, 0x00 };

            var definition = new GraphicDefinition { Id = -1 }.Decode(new JagStream(stored));

            Assert.Equal(16, definition.EffectOpcode);
            Assert.Equal(500, definition.EffectParameter);
            Assert.Equal(stored, definition.Encode().ToArray());
        }

        /// <summary>Setting an effect drops whatever effect opcode the record already carried.</summary>
        /// <remarks>
        ///     SYNTHETIC. The eight opcodes are mutually exclusive statements of the same two fields,
        ///     so leaving an old one in the stream would have the replay write both and the client
        ///     take whichever came last.
        /// </remarks>
        [Fact]
        public void SettingAnEffectReplacesTheOpcodeThatStatedTheOldOne()
        {
            var definition = new GraphicDefinition { Id = -1 }
                .Decode(new JagStream(new byte[] { 0x09, 0x00 }));

            definition.SetEffect(15, 0x1234);
            byte[] encoded = definition.Encode().ToArray();

            Assert.Equal(new byte[] { 0x0F, 0x12, 0x34, 0x00 }, encoded);

            var reread = new GraphicDefinition { Id = -1 }.Decode(new JagStream(encoded));
            Assert.Equal(15, reread.EffectOpcode);
            Assert.Equal(0x1234, reread.EffectParameter);

            //And clearing it removes the opcode altogether rather than writing a zero parameter.
            reread.SetEffect(GraphicDefinition.NoEffectOpcode);
            Assert.Equal(new byte[] { 0x00 }, reread.Encode().ToArray());
        }

        /// <summary>An opcode the client does not handle is refused rather than desynchronising.</summary>
        /// <remarks>
        ///     3 is the only gap in 1..16 and occurs nowhere in either 639 cache, so there is no data
        ///     veto to weigh against the client having no handler for it.
        /// </remarks>
        [Fact]
        public void UnknownOpcodesAreRefused()
        {
            foreach (byte opcode in new byte[] { 3, 17, 39, 42, 200 })
            {
                Assert.Throws<InvalidOperationException>(() =>
                    new GraphicDefinition { Id = 0 }.Decode(new JagStream(new byte[] { opcode, 0, 0, 0, 0 })));
            }
        }

        /// <summary>Recolour arrays a single count byte cannot describe are refused on the way out.</summary>
        [Fact]
        public void MismatchedRecolourArraysAreRefused()
        {
            var definition = new GraphicDefinition { Id = 9 }.Decode(new JagStream(WithRecolours));
            definition.RecolourTo = new[] { 1 };

            Assert.Throws<InvalidOperationException>(() => definition.Encode());
        }
    }
}
