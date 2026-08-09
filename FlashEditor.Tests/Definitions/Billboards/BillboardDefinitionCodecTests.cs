using System;
using System.Collections.Generic;
using System.Linq;
using FlashEditor.Definitions.Billboards;
using Xunit;
using FlashEditor.IO;

namespace FlashEditor.Tests.Definitions.Billboards
{
    /// <summary>
    ///     The billboard codec against bytes lifted from a real revision-639 cache.
    /// </summary>
    /// <remarks>
    ///     One fixture per opcode ordering that occurs in the cache, all eight of them, because the
    ///     ordering is what an encoder gets wrong here while still producing a file of the right
    ///     length. Every one of them ends with opcode 1 and not one of them ascends, so an encoder
    ///     that chose its own order would reproduce none of them - and the archive CRC covers those
    ///     bytes, so it would rewrite every record the user merely opened.
    ///     <para>
    ///     The file ids are the vanilla capture's, and the repack stores identical bytes at the same
    ///     ids. They are recorded so a fixture can be re-read from the cache rather than trusted.
    ///     </para>
    /// </remarks>
    public sealed class BillboardDefinitionCodecTests
    {
        /// <summary>File 0: order 2, 3, 4, 5, 7, 1 - the most common shape.</summary>
        public static readonly byte[] FullRecord =
        {
            0x02, 0x00, 0x32, 0x00, 0x32,
            0x03, 0x00,
            0x04, 0x01,
            0x05, 0x00,
            0x07,
            0x01, 0x02, 0xDF,
            0x00
        };

        /// <summary>File 11: order 2, 4, 5, 7, 1 - no discarded byte.</summary>
        public static readonly byte[] WithoutUnusedByte =
        {
            0x02, 0x00, 0x41, 0x00, 0x41, 0x04, 0x01, 0x05, 0x00, 0x07, 0x01, 0x03, 0x33, 0x00
        };

        /// <summary>File 14: order 2, 4, 1 - the shortest shape.</summary>
        public static readonly byte[] Minimal =
        {
            0x02, 0x00, 0x32, 0x00, 0x32, 0x04, 0x01, 0x01, 0x03, 0x3B, 0x00
        };

        /// <summary>File 19: order 2, 4, 5, 1.</summary>
        public static readonly byte[] WithoutFaceSuppression =
        {
            0x02, 0x00, 0xC8, 0x00, 0xC8, 0x04, 0x01, 0x05, 0x00, 0x01, 0x03, 0x3E, 0x00
        };

        /// <summary>File 21: order 2, 4, 7, 1.</summary>
        public static readonly byte[] WithoutCombineMode =
        {
            0x02, 0x00, 0x32, 0x00, 0x32, 0x04, 0x01, 0x07, 0x01, 0x03, 0x3C, 0x00
        };

        /// <summary>File 22: order 2, 3, 4, 1.</summary>
        public static readonly byte[] UnusedByteWithoutFlags =
        {
            0x02, 0x00, 0x80, 0x00, 0x80, 0x03, 0x20, 0x04, 0x01, 0x01, 0x03, 0x3D, 0x00
        };

        /// <summary>File 36: order 2, 3, 5, 4, 7, 1 - opcode 5 before opcode 4.</summary>
        public static readonly byte[] CombineBeforeRaster =
        {
            0x02, 0x00, 0x30, 0x00, 0x30, 0x03, 0x20, 0x05, 0x03, 0x04, 0x01, 0x07, 0x01, 0x02, 0xE8, 0x00
        };

        /// <summary>File 45: order 2, 4, 7, 5, 1 - opcode 7 before opcode 5.</summary>
        public static readonly byte[] FaceSuppressionBeforeCombine =
        {
            0x02, 0x00, 0xD2, 0x00, 0xD2, 0x04, 0x01, 0x07, 0x05, 0x00, 0x01, 0x02, 0xDF, 0x00
        };

        /// <summary>Every captured record, with the file id it was read from.</summary>
        public static IEnumerable<object[]> EveryFixture()
        {
            yield return new object[] { 0, FullRecord };
            yield return new object[] { 11, WithoutUnusedByte };
            yield return new object[] { 14, Minimal };
            yield return new object[] { 19, WithoutFaceSuppression };
            yield return new object[] { 21, WithoutCombineMode };
            yield return new object[] { 22, UnusedByteWithoutFlags };
            yield return new object[] { 36, CombineBeforeRaster };
            yield return new object[] { 45, FaceSuppressionBeforeCombine };
        }

        /// <summary>Every captured record consumes exactly and re-encodes to the bytes it came from.</summary>
        /// <param name="id">The billboard id, so a failure names it.</param>
        /// <param name="stored">The captured bytes.</param>
        [Theory]
        [MemberData(nameof(EveryFixture))]
        public void EveryCapturedRecordRoundTrips(int id, byte[] stored)
        {
            var stream = new JagStream(stored);
            var definition = new BillboardDefinition { Id = id }.Decode(stream);

            Assert.True(stored.Length == stream.Position,
                $"billboard {id} consumed {stream.Position} of its {stored.Length} bytes");
            Assert.True(stored.AsSpan().SequenceEqual(definition.Encode().ToArray()),
                $"billboard {id} did not re-encode to the bytes it was decoded from");
        }

        /// <summary>
        ///     Material is the last opcode of every captured record, and none of them ascends.
        /// </summary>
        /// <param name="id">The billboard id.</param>
        /// <param name="stored">The captured bytes.</param>
        [Theory]
        [MemberData(nameof(EveryFixture))]
        public void MaterialIsWrittenLast(int id, byte[] stored)
        {
            var definition = new BillboardDefinition { Id = id }.Decode(new JagStream(stored));
            int[] opcodes = definition.Opcodes.Select(record => record.Opcode).ToArray();

            Assert.Equal(1, opcodes[opcodes.Length - 1]);
            Assert.False(opcodes.SequenceEqual(opcodes.OrderBy(opcode => opcode)),
                $"billboard {id} carries its opcodes in ascending order, so it cannot show that the " +
                "recorded order is needed");
        }

        /// <summary>The full record decodes to the fields the client reads out of it.</summary>
        /// <remarks>
        ///     Width and height are stored minus one, so a stored 0x32 is 51 rather than 50 - the
        ///     off-by-one an editor would otherwise show and write back wrong.
        /// </remarks>
        [Fact]
        public void TheFullRecordDecodesToItsFields()
        {
            var definition = new BillboardDefinition { Id = 0 }.Decode(new JagStream(FullRecord));

            Assert.Equal(735, definition.MaterialId);
            Assert.Equal(51, definition.Width);
            Assert.Equal(51, definition.Height);
            Assert.Equal(0, definition.UnusedByte3);
            Assert.Equal(1, definition.RasterMode);
            Assert.Equal(0, definition.CombineMode);
            Assert.False(definition.HiddenOnShaderRenderer);
            Assert.True(definition.HidesSourceFace);
        }

        /// <summary>
        ///     A record that omits an opcode keeps the client's default for it.
        /// </summary>
        [Fact]
        public void OmittedOpcodesKeepTheClientsDefaults()
        {
            var definition = new BillboardDefinition { Id = 14 }.Decode(new JagStream(Minimal));

            Assert.False(definition.Opcodes.Has(3));
            Assert.False(definition.Opcodes.Has(5));
            Assert.Equal(0, definition.UnusedByte3);
            Assert.Equal(BillboardDefinition.DefaultCombineMode, definition.CombineMode);
            Assert.False(definition.HidesSourceFace);
        }

        /// <summary>
        ///     A field stored at its own default is not dropped on the way out.
        /// </summary>
        /// <remarks>
        ///     File 22 stores the discarded byte at 0x20 and file 0 stores it at 0, which is
        ///     identical to what an absent opcode 3 gives. Deciding what to write from the value
        ///     alone would shorten every record that does that.
        /// </remarks>
        [Fact]
        public void FieldsStoredAtTheirDefaultAreNotDropped()
        {
            var definition = new BillboardDefinition { Id = 0 }.Decode(new JagStream(FullRecord));

            Assert.True(definition.Opcodes.Has(3));
            Assert.Equal(0, definition.UnusedByte3);
            Assert.Equal(FullRecord, definition.Encode().ToArray());
        }

        /// <summary>Clearing a flag removes its opcode rather than leaving one to be replayed.</summary>
        /// <remarks>
        ///     A bare flag has no payload, so nothing but the recorded stream says whether it is
        ///     set. If clearing it only changed a field, the replay would put the opcode back: the
        ///     row would change, the save would report success, and the flag would still be set in
        ///     the cache.
        /// </remarks>
        [Fact]
        public void ClearingAFlagRemovesItsOpcode()
        {
            var definition = new BillboardDefinition { Id = 0 }.Decode(new JagStream(FullRecord));
            Assert.True(definition.HidesSourceFace);

            definition.HidesSourceFace = false;
            byte[] encoded = definition.Encode().ToArray();

            Assert.DoesNotContain((byte) 7, encoded);
            Assert.False(new BillboardDefinition { Id = 0 }.Decode(new JagStream(encoded)).HidesSourceFace);

            //And setting it again puts it back, so the property is not one way.
            definition.HidesSourceFace = true;
            Assert.True(new BillboardDefinition { Id = 0 }
                .Decode(new JagStream(definition.Encode().ToArray())).HidesSourceFace);
        }

        /// <summary>
        ///     A stored 0xFFFF material decodes to -1 and is written back as 0xFFFF.
        /// </summary>
        /// <remarks>
        ///     The sentinel and an absent opcode 1 are aliases of the same decoded value, and no
        ///     record in either supported cache stores it - so the byte-identity sweep cannot defend
        ///     this branch and only this test does.
        /// </remarks>
        [Fact]
        public void TheNoMaterialSentinelRoundTrips()
        {
            byte[] withSentinel = { 0x04, 0x01, 0x01, 0xFF, 0xFF, 0x00 };

            var definition = new BillboardDefinition { Id = 0 }.Decode(new JagStream(withSentinel));

            Assert.Equal(-1, definition.MaterialId);
            Assert.Equal(withSentinel, definition.Encode().ToArray());
        }

        /// <summary>An empty record keeps every default and stays a single terminator byte.</summary>
        [Fact]
        public void AnEmptyRecordKeepsItsDefaults()
        {
            var definition = new BillboardDefinition { Id = 0 }.Decode(new JagStream(new byte[] { 0 }));

            Assert.Equal(-1, definition.MaterialId);
            Assert.Equal(BillboardDefinition.DefaultExtent, definition.Width);
            Assert.Equal(BillboardDefinition.DefaultExtent, definition.Height);
            Assert.Equal(BillboardDefinition.DefaultRasterMode, definition.RasterMode);
            Assert.Equal(BillboardDefinition.DefaultCombineMode, definition.CombineMode);
            Assert.False(definition.HiddenOnShaderRenderer);
            Assert.False(definition.HidesSourceFace);
            Assert.Equal(new byte[] { 0 }, definition.Encode().ToArray());
        }

        /// <summary>
        ///     Opcode 6 encodes and decodes even though no shipped record carries it.
        /// </summary>
        /// <remarks>
        ///     It is a real opcode with a real consumer (Renderable_Sub2.java:4036) and zero
        ///     occurrences in either cache, so no sweep defends it. Same category as the
        ///     reference-table flags that are set nowhere on disk: implement it, and cover it here.
        /// </remarks>
        [Fact]
        public void TheShaderSuppressionFlagIsImplementedThoughNoRecordCarriesIt()
        {
            var definition = new BillboardDefinition { Id = 0 }.Decode(new JagStream(new byte[] { 0 }));
            definition.HiddenOnShaderRenderer = true;

            byte[] encoded = definition.Encode().ToArray();
            Assert.Equal(new byte[] { 0x06, 0x00 }, encoded);
            Assert.True(new BillboardDefinition { Id = 0 }.Decode(new JagStream(encoded)).HiddenOnShaderRenderer);
        }

        /// <summary>An opcode the client does not handle is refused rather than desynchronising.</summary>
        [Fact]
        public void UnknownOpcodesAreRefused()
        {
            foreach (byte opcode in new byte[] { 8, 9, 100 })
            {
                Assert.Throws<InvalidOperationException>(() =>
                    new BillboardDefinition { Id = 0 }.Decode(new JagStream(new byte[] { opcode, 0, 0 })));
            }
        }
    }
}
