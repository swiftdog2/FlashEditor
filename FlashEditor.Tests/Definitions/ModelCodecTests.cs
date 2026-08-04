using FlashEditor;
using FlashEditor.Definitions;
using System;
using System.Collections.Generic;
using Xunit;

namespace FlashEditor.Tests.Definitions
{
    /// <summary>
    ///     Pins the model codec against hand-built bytes rather than against itself.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///     The byte-identity sweep over index 7 covers every model in the cache and still cannot
    ///     defend most of what is here, because the inputs that would tell a faithful codec from a
    ///     normalising one <b>do not occur in either cache</b>. A widened smart, a gap before the
    ///     footer, a new-protocol smart skin block, a bond with a two-byte third field: zero
    ///     occurrences each. An encoder that recomputed any of them would sweep clean and corrupt
    ///     the first model an editor touched.
    ///     </para>
    ///     <para>
    ///     Everything below is therefore built by hand from the field layout in the 637 client, and
    ///     asserted both ways - the decoded value has to be right, and the bytes have to come back.
    ///     Two of these cases do occur in the data and are pinned here anyway because they are the
    ///     ones a shortcut is most tempting on: the format-type flag bit that does not follow from
    ///     the format type, and the declared block length that overstates what is read.
    ///     </para>
    /// </remarks>
    public class ModelCodecTests
    {
        /// <summary>A model id well below the new-protocol range, so the sentinel decides.</summary>
        private const int SentinelModelId = 100;

        // ===================================================================
        //  Smart widths
        // ===================================================================

        /// <summary>
        ///     A delta small enough for one byte but stored in two must come back in two.
        /// </summary>
        /// <remarks>
        ///     Not one of the 57 million signed smarts in either cache's index 7 is widened, so a
        ///     shortest-form encoder passes the whole sweep and shortens this file by six bytes -
        ///     which moves every block after it and changes what the footer's declared lengths point
        ///     at.
        /// </remarks>
        [Fact]
        public void AWidenedSmart_KeepsItsWidth()
        {
            var builder = new NewerModelBuilder
            {
                VertexFlags = new byte[] { 0x7 },
                FaceOpcodes = new byte[] { 1 },
                FaceIndexBlock = Concat(WideSmart(0), WideSmart(0), WideSmart(0)),
                XBlock = WideSmart(5),
                YBlock = WideSmart(-3),
                ZBlock = WideSmart(63)
            };

            byte[] stored = builder.Build();
            ModelFile file = ModelCodec.Decode(stored, SentinelModelId);

            Assert.Equal(5, file.VertexDeltasX[0].Value);
            Assert.Equal(JagStream.SmartWidth.TwoByte, file.VertexDeltasX[0].Width);
            Assert.Equal(-3, file.VertexDeltasY[0].Value);
            Assert.Equal(63, file.VertexDeltasZ[0].Value);
            Assert.Equal(JagStream.SmartWidth.TwoByte, file.VertexDeltasZ[0].Width);
            AssertRoundTrips(stored, SentinelModelId);
        }

        /// <summary>
        ///     The one-byte signed smart carries -64 to 63 biased by 64, and both widths decode to
        ///     the same numbers.
        /// </summary>
        [Fact]
        public void BothSmartWidths_DecodeToTheSameValues()
        {
            var narrow = new NewerModelBuilder
            {
                VertexFlags = new byte[] { 0x1 },
                FaceOpcodes = new byte[] { 1 },
                FaceIndexBlock = Concat(NarrowSmart(0), NarrowSmart(0), NarrowSmart(0)),
                XBlock = NarrowSmart(-64)
            };
            var wide = new NewerModelBuilder
            {
                VertexFlags = new byte[] { 0x1 },
                FaceOpcodes = new byte[] { 1 },
                FaceIndexBlock = Concat(NarrowSmart(0), NarrowSmart(0), NarrowSmart(0)),
                XBlock = WideSmart(-64)
            };

            ModelFile narrowFile = ModelCodec.Decode(narrow.Build(), SentinelModelId);
            ModelFile wideFile = ModelCodec.Decode(wide.Build(), SentinelModelId);

            Assert.Equal(-64, narrowFile.VertexDeltasX[0].Value);
            Assert.Equal(-64, wideFile.VertexDeltasX[0].Value);
            Assert.Equal(JagStream.SmartWidth.OneByte, narrowFile.VertexDeltasX[0].Width);
            Assert.Equal(JagStream.SmartWidth.TwoByte, wideFile.VertexDeltasX[0].Width);
            AssertRoundTrips(narrow.Build(), SentinelModelId);
            AssertRoundTrips(wide.Build(), SentinelModelId);
        }

        // ===================================================================
        //  The format-type flag bit
        // ===================================================================

        /// <summary>
        ///     Flags bit 3 with a stored format type of 12 must keep the bit and the byte.
        /// </summary>
        /// <remarks>
        ///     Bit 3 clear already means format type 12, so an encoder that set the bit from
        ///     <c>FormatType != 12</c> would drop a byte here. The repack holds exactly one model
        ///     like this and the vanilla capture holds none, so on the default cache the sweep alone
        ///     would never notice.
        /// </remarks>
        [Fact]
        public void TheFormatTypeBit_IsNotRecomputedFromTheFormatType()
        {
            var builder = new NewerModelBuilder
            {
                Flags = 0x8,
                EmbeddedFormatType = 12,
                VertexFlags = new byte[] { 0 },
                FaceOpcodes = new byte[] { 1 },
                FaceIndexBlock = Concat(NarrowSmart(0), NarrowSmart(0), NarrowSmart(0))
            };

            byte[] stored = builder.Build();
            ModelFile file = ModelCodec.Decode(stored, SentinelModelId);

            Assert.True(file.HasEmbeddedFormatType);
            Assert.Equal(12, file.FormatType);
            AssertRoundTrips(stored, SentinelModelId);
        }

        /// <summary>
        ///     The vertex shift follows the format type, and is recorded rather than baked into the
        ///     stored deltas.
        /// </summary>
        /// <remarks>
        ///     The client's decoders never shift; its callers do, through <c>method2592</c>, when the
        ///     format type is below 13. Baking it into the decode is what made the old model decoder
        ///     impossible to encode from, because the stored delta could no longer be recovered.
        /// </remarks>
        [Fact]
        public void TheVertexShift_IsRecordedRatherThanBakedIn()
        {
            var below13 = new NewerModelBuilder
            {
                VertexFlags = new byte[] { 0x1 },
                FaceOpcodes = new byte[] { 1 },
                FaceIndexBlock = Concat(NarrowSmart(0), NarrowSmart(0), NarrowSmart(0)),
                XBlock = NarrowSmart(9)
            };
            var atLeast13 = new NewerModelBuilder
            {
                Flags = 0x8,
                EmbeddedFormatType = 15,
                VertexFlags = new byte[] { 0x1 },
                FaceOpcodes = new byte[] { 1 },
                FaceIndexBlock = Concat(NarrowSmart(0), NarrowSmart(0), NarrowSmart(0)),
                XBlock = NarrowSmart(9)
            };

            ModelFile oldFormat = ModelCodec.Decode(below13.Build(), SentinelModelId);
            ModelFile newFormat = ModelCodec.Decode(atLeast13.Build(), SentinelModelId);

            Assert.Equal(2, oldFormat.VertexShift);
            Assert.Equal(0, newFormat.VertexShift);
            Assert.Equal(9, oldFormat.VertexDeltasX[0].Value);
            Assert.Equal(9, newFormat.VertexDeltasX[0].Value);

            var shifted = new ModelDefinition { ModelID = SentinelModelId };
            shifted.Decode(new JagStream(below13.Build()));
            Assert.Equal(36, shifted.VertX[0]);
            Assert.Equal(2, shifted.VertexShift);

            var unshifted = new ModelDefinition { ModelID = SentinelModelId };
            unshifted.Decode(new JagStream(atLeast13.Build()));
            Assert.Equal(9, unshifted.VertX[0]);
        }

        // ===================================================================
        //  Declared lengths
        // ===================================================================

        /// <summary>
        ///     A block whose declared length exceeds what the client reads keeps the remainder.
        /// </summary>
        /// <remarks>
        ///     This shape occurs for real: 13,787 models in the vanilla capture declare a
        ///     textured-face projection block longer than they fill, because a type-2 face at format
        ///     type 15 consumes 7 of the block's 9-byte stride. It is asserted on a vertex block here
        ///     because that is the smallest file that can express it.
        /// </remarks>
        [Fact]
        public void ADeclaredBlockLongerThanItsContent_KeepsTheRemainder()
        {
            var builder = new NewerModelBuilder
            {
                VertexFlags = new byte[] { 0x1 },
                FaceOpcodes = new byte[] { 1 },
                FaceIndexBlock = Concat(NarrowSmart(0), NarrowSmart(0), NarrowSmart(0), new byte[] { 0xDE, 0xAD }),
                XBlock = Concat(NarrowSmart(4), new byte[] { 0xBE, 0xEF })
            };

            byte[] stored = builder.Build();
            ModelFile file = ModelCodec.Decode(stored, SentinelModelId);

            Assert.Equal(new byte[] { 0xBE, 0xEF }, file.SlackVertexX);
            Assert.Equal(new byte[] { 0xDE, 0xAD }, file.SlackFaceIndex);
            AssertRoundTrips(stored, SentinelModelId);
        }

        /// <summary>
        ///     Bytes between the end of the data and the footer are carried rather than dropped.
        /// </summary>
        /// <remarks>
        ///     No model in either cache has any, which is exactly why this is asserted synthetically
        ///     - a decoder that assumed the gap away would shorten the first file that had one, and
        ///     nothing in the cache would say so.
        /// </remarks>
        [Fact]
        public void BytesBetweenTheDataAndTheFooter_AreCarried()
        {
            var builder = new NewerModelBuilder
            {
                VertexFlags = new byte[] { 0 },
                FaceOpcodes = new byte[] { 1 },
                FaceIndexBlock = Concat(NarrowSmart(0), NarrowSmart(0), NarrowSmart(0)),
                Gap = new byte[] { 1, 2, 3 }
            };

            byte[] stored = builder.Build();
            ModelFile file = ModelCodec.Decode(stored, SentinelModelId);

            Assert.Equal(new byte[] { 1, 2, 3 }, file.Gap);
            AssertRoundTrips(stored, SentinelModelId);
        }

        // ===================================================================
        //  Fields the old decoder truncated
        // ===================================================================

        /// <summary>
        ///     A face skin above 127 stays positive.
        /// </summary>
        /// <remarks>
        ///     The stored field is an unsigned byte (Model.java:596), and 8,639 models in the repack
        ///     carry a value above 127. Holding it in a signed byte turned every one of those
        ///     negative, which puts the face in a skin group that does not exist.
        /// </remarks>
        [Fact]
        public void AFaceSkinAbove127_IsNotTruncated()
        {
            var builder = new NewerModelBuilder
            {
                FaceSkinFlag = 1,
                FaceSkins = new byte[] { 200 },
                VertexSkinFlag = 1,
                VertexSkins = new byte[] { 255 },
                VertexFlags = new byte[] { 0 },
                FaceOpcodes = new byte[] { 1 },
                FaceIndexBlock = Concat(NarrowSmart(0), NarrowSmart(0), NarrowSmart(0))
            };

            byte[] stored = builder.Build();
            var model = new ModelDefinition { ModelID = SentinelModelId };
            model.Decode(new JagStream(stored));

            Assert.NotNull(model.FaceSkin);
            Assert.Equal(200, model.FaceSkin[0]);
            AssertRoundTrips(stored, SentinelModelId);
        }

        /// <summary>
        ///     A texture-coordinate index above 127 addresses the mapping it names.
        /// </summary>
        /// <remarks>
        ///     The newer footer allows 255 textured faces, so the index does not fit a signed byte.
        ///     Truncating it silently pointed the face at a different mapping.
        /// </remarks>
        [Fact]
        public void ATextureCoordinateAbove127_AddressesItsOwnMapping()
        {
            const int textured = 200;
            var types = new byte[textured];
            var texturedBlock = new byte[textured * 6];

            var builder = new NewerModelBuilder
            {
                TextureTypes = types,
                TextureFlag = 1,
                TextureIds = new byte[] { 0, 7 },
                TextureCoordBlock = new byte[] { 199 },
                TexturedBlock = texturedBlock,
                VertexFlags = new byte[] { 0 },
                FaceOpcodes = new byte[] { 1 },
                FaceIndexBlock = Concat(NarrowSmart(0), NarrowSmart(0), NarrowSmart(0))
            };

            byte[] stored = builder.Build();
            var model = new ModelDefinition { ModelID = SentinelModelId };
            model.Decode(new JagStream(stored));

            Assert.NotNull(model.TextureCoordinates);
            Assert.Equal(198, model.TextureCoordinates[0]);
            Assert.Equal(6, model.FaceTextures[0]);
            AssertRoundTrips(stored, SentinelModelId);
        }

        // ===================================================================
        //  Textured face types 1 to 3
        // ===================================================================

        /// <summary>
        ///     Types 1, 2 and 3 carry three projection scalars, three byte fields, and - for type 2
        ///     alone - two more bytes.
        /// </summary>
        /// <remarks>
        ///     The widths differ by both format type and face type, and type 2 disagrees with 1 and 3
        ///     above format 14. At format 15 the block's declared stride is 9 while a type-2 entry
        ///     reads 7, which is where the routine slack in the shipped models comes from.
        /// </remarks>
        [Fact]
        public void TexturedFaceTypes1To3_AreDecodedWithTheirOwnWidths()
        {
            //Types 1 and 3 read three mediums at format 15; type 2 reads short, medium, short.
            byte[] typeOne = Concat(Medium(0x111111), Medium(0x222222), Medium(0x333333));
            byte[] typeTwo = Concat(Short(0x4444), Medium(0x555555), Short(0x6666));
            byte[] typeThree = Concat(Medium(0x777777), Medium(0x888888), Medium(0x999999));

            //Declared stride is 9 per entry, so the type-2 entry leaves two bytes unread.
            byte[] scale = Concat(typeOne, typeTwo, typeThree, new byte[] { 0xAA, 0xBB });

            byte[] vertices = Concat(
                Short(1), Short(2), Short(3),
                Short(4), Short(5), Short(6),
                Short(7), Short(8), Short(9));

            byte[] fieldA = { 11, 12, 13 };
            byte[] fieldB = { 21, 22, 23 };
            byte[] fieldC = { 31, 32, 41, 42, 33 };

            var builder = new NewerModelBuilder
            {
                Flags = 0x8,
                EmbeddedFormatType = 15,
                TextureTypes = new byte[] { 1, 2, 3 },
                TexturedBlock = Concat(vertices, scale, fieldA, fieldB, fieldC),
                VertexFlags = new byte[] { 0 },
                FaceOpcodes = new byte[] { 1 },
                FaceIndexBlock = Concat(NarrowSmart(0), NarrowSmart(0), NarrowSmart(0))
            };

            byte[] stored = builder.Build();
            ModelFile file = ModelCodec.Decode(stored, SentinelModelId);

            Assert.Equal(0, file.Type0FaceCount);
            Assert.Equal(3, file.Type1To3FaceCount);
            Assert.Equal(1, file.Type2FaceCount);

            Assert.Equal(0x111111, file.TextureScaleP[0]);
            Assert.Equal(0x333333, file.TextureScaleR[0]);
            Assert.Equal(0x4444, file.TextureScaleP[1]);
            Assert.Equal(0x555555, file.TextureScaleQ[1]);
            Assert.Equal(0x6666, file.TextureScaleR[1]);
            Assert.Equal(0x999999, file.TextureScaleR[2]);

            Assert.Equal(new byte[] { 11, 12, 13 }, file.TextureFieldA);
            Assert.Equal(new byte[] { 21, 22, 23 }, file.TextureFieldB);
            Assert.Equal(new byte[] { 31, 32, 33 }, file.TextureFieldC);
            Assert.Equal(41, file.TextureType2FieldA[1]);
            Assert.Equal(42, file.TextureType2FieldB[1]);
            Assert.Equal(new byte[] { 0xAA, 0xBB }, file.SlackTextureScale);

            AssertRoundTrips(stored, SentinelModelId);
        }

        /// <summary>
        ///     Type-0 and type 1-3 reference vertices live in two separate blocks and both reach the
        ///     same per-face arrays.
        /// </summary>
        [Fact]
        public void TheTwoTexturedVertexBlocks_BothPopulateTheSameArrays()
        {
            byte[] typeOneVertices = Concat(Short(10), Short(11), Short(12));
            byte[] typeZeroVertices = Concat(Short(20), Short(21), Short(22));
            byte[] scale = Concat(Short(1), Short(2), Short(3));

            var builder = new NewerModelBuilder
            {
                TextureTypes = new byte[] { 1, 0 },
                TexturedBlock = Concat(typeZeroVertices, typeOneVertices, scale,
                    new byte[] { 5 }, new byte[] { 6 }, new byte[] { 7 }),
                VertexFlags = new byte[] { 0 },
                FaceOpcodes = new byte[] { 1 },
                FaceIndexBlock = Concat(NarrowSmart(0), NarrowSmart(0), NarrowSmart(0))
            };

            byte[] stored = builder.Build();
            ModelFile file = ModelCodec.Decode(stored, SentinelModelId);

            Assert.Equal(10, file.TextureVertexA[0]);
            Assert.Equal(20, file.TextureVertexA[1]);
            Assert.Equal(22, file.TextureVertexC[1]);
            AssertRoundTrips(stored, SentinelModelId);
        }

        // ===================================================================
        //  Particles and bonds
        // ===================================================================

        /// <summary>
        ///     Flags bits 1 and 2 add three count-prefixed lists after the data and before the footer.
        /// </summary>
        [Fact]
        public void ParticlesAndBonds_AreReadFromTheTail()
        {
            byte[] tail = Concat(
                new byte[] { 2 },
                Short(300), Short(0),
                Short(301), Short(0),
                new byte[] { 1 },
                Short(400), Short(0),
                new byte[] { 1 },
                Short(500), Short(0), new byte[] { 9 }, new byte[] { 0xFE });

            var builder = new NewerModelBuilder
            {
                Flags = 0x2 | 0x4,
                Tail = tail,
                VertexFlags = new byte[] { 0 },
                FaceOpcodes = new byte[] { 1 },
                FaceIndexBlock = Concat(NarrowSmart(0), NarrowSmart(0), NarrowSmart(0))
            };

            byte[] stored = builder.Build();
            ModelFile file = ModelCodec.Decode(stored, SentinelModelId);

            Assert.Equal(2, file.Emitters.Length);
            Assert.Equal(300, file.Emitters[0].EmitterId);
            Assert.Equal(301, file.Emitters[1].EmitterId);
            Assert.Single(file.Effectors);
            Assert.Equal(400, file.Effectors[0].EffectorId);
            Assert.Single(file.Bonds);
            Assert.Equal(500, file.Bonds[0].BillboardId);
            Assert.Equal(9, file.Bonds[0].Third.Value);
            Assert.Equal(-2, file.Bonds[0].Fourth);

            var model = new ModelDefinition { ModelID = SentinelModelId };
            model.Decode(new JagStream(stored));
            Assert.Equal(300, model.ParticleEffectId);
            Assert.Equal(2, model.Emitters.Length);
            Assert.Single(model.Bonds);

            AssertRoundTrips(stored, SentinelModelId);
        }

        /// <summary>
        ///     An empty emitter list still carries the effector count byte after it.
        /// </summary>
        /// <remarks>
        ///     The client reads the second count unconditionally (Model.java:774), outside the
        ///     <c>if</c> that guards the emitter loop. Skipping it when there are no emitters would
        ///     leave a byte behind and put the bond list one byte out.
        /// </remarks>
        [Fact]
        public void AnEmptyEmitterList_StillCarriesTheEffectorCount()
        {
            var builder = new NewerModelBuilder
            {
                Flags = 0x2,
                Tail = new byte[] { 0, 0 },
                VertexFlags = new byte[] { 0 },
                FaceOpcodes = new byte[] { 1 },
                FaceIndexBlock = Concat(NarrowSmart(0), NarrowSmart(0), NarrowSmart(0))
            };

            byte[] stored = builder.Build();
            ModelFile file = ModelCodec.Decode(stored, SentinelModelId);

            Assert.Empty(file.Emitters);
            Assert.Empty(file.Effectors);
            Assert.Empty(file.Gap);
            AssertRoundTrips(stored, SentinelModelId);
        }

        // ===================================================================
        //  Strip opcodes
        // ===================================================================

        /// <summary>
        ///     The four strip opcodes produce the client's triangles, and the opcode bytes survive.
        /// </summary>
        /// <remarks>
        ///     Any face can be written as opcode 1 with three fresh deltas instead of 2, 3 or 4, so
        ///     re-deriving the opcode stream from the triangles yields a different, equally valid
        ///     file. The encoder replays the stored bytes instead.
        /// </remarks>
        [Fact]
        public void TheStripOpcodes_ProduceTheClientsTrianglesAndSurviveTheReEncode()
        {
            byte[] deltas = Concat(
                NarrowSmart(0), NarrowSmart(1), NarrowSmart(1),
                NarrowSmart(1),
                NarrowSmart(1),
                NarrowSmart(1));

            var builder = new NewerModelBuilder
            {
                FaceCount = 4,
                FaceOpcodes = new byte[] { 1, 2, 3, 4 },
                FaceIndexBlock = deltas,
                ColourBlock = new byte[8],
                VertexFlags = new byte[] { 0 }
            };

            byte[] stored = builder.Build();
            var model = new ModelDefinition { ModelID = SentinelModelId };
            model.Decode(new JagStream(stored));

            Assert.Equal(new[] { 0, 1, 2, 3 }, new[] { model.faceIndices1[0], model.faceIndices2[0], model.faceIndices3[0], model.faceIndices3[1] });
            Assert.Equal(2, model.faceIndices2[1]);
            Assert.Equal(0, model.faceIndices1[1]);
            Assert.Equal(3, model.faceIndices1[2]);
            Assert.Equal(4, model.faceIndices3[2]);
            Assert.Equal(2, model.faceIndices1[3]);
            Assert.Equal(3, model.faceIndices2[3]);
            Assert.Equal(5, model.faceIndices3[3]);

            AssertRoundTrips(stored, SentinelModelId);
        }

        /// <summary>
        ///     A face written as opcode 1 where opcode 2 would do keeps its opcode.
        /// </summary>
        [Fact]
        public void ARestartedStrip_IsNotCollapsedIntoAContinuation()
        {
            byte[] deltas = Concat(
                NarrowSmart(0), NarrowSmart(1), NarrowSmart(1),
                NarrowSmart(-2), NarrowSmart(1), NarrowSmart(1));

            var builder = new NewerModelBuilder
            {
                FaceCount = 2,
                FaceOpcodes = new byte[] { 1, 1 },
                FaceIndexBlock = deltas,
                ColourBlock = new byte[4],
                VertexFlags = new byte[] { 0 }
            };

            byte[] stored = builder.Build();
            ModelFile file = ModelCodec.Decode(stored, SentinelModelId);

            Assert.Equal(new byte[] { 1, 1 }, file.FaceOpcodes);
            Assert.Equal(6, file.FaceIndexDeltas.Length);
            AssertRoundTrips(stored, SentinelModelId);
        }

        // ===================================================================
        //  New protocol
        // ===================================================================

        /// <summary>
        ///     A new-protocol texture-coordinate index is an unsigned smart, not the signed one.
        /// </summary>
        /// <remarks>
        ///     The client reads it with <c>readSmart(454)</c> (Model.java:1115), which biases by 0
        ///     and 32768; the signed smart biases by 64 and 0xC000. A one-byte value read with the
        ///     wrong one comes out exactly 64 too low, with nothing to indicate it. Four of the seven
        ///     new-protocol models in the repack reach this read.
        /// </remarks>
        [Fact]
        public void ANewProtocolTextureCoordinate_UsesTheUnsignedSmart()
        {
            var builder = new NewProtocolModelBuilder
            {
                FormatTypeByte = 16,
                TextureTypes = new byte[] { 0 },
                TexturedBlock = Concat(Short(0), Short(0), Short(0)),
                TextureFlag = 1,
                TextureIds = Short(5),
                TextureCoordBlock = new byte[] { 0x40 },
                VertexFlags = new byte[] { 0 },
                FaceOpcodes = new byte[] { 1 },
                FaceIndexBlock = Concat(NarrowSmart(0), NarrowSmart(0), NarrowSmart(0))
            };

            byte[] stored = builder.Build();
            ModelFile file = ModelCodec.Decode(stored, ModelCodec.FirstNewProtocolModelId);

            Assert.Equal(64, file.TextureCoords[0].Value);

            var model = new ModelDefinition { ModelID = ModelCodec.FirstNewProtocolModelId };
            model.Decode(new JagStream(stored));
            Assert.Equal(63, model.TextureCoordinates[0]);

            AssertRoundTrips(stored, ModelCodec.FirstNewProtocolModelId);
        }

        /// <summary>
        ///     A new-protocol skin block declared explicitly holds smarts, and its stored length is
        ///     kept even when the client would derive one.
        /// </summary>
        /// <remarks>
        ///     Flags bit 4 switches the vertex-skin block from bytes to <c>readSmart2</c> and makes
        ///     the footer's stored length authoritative. Six of the seven new-protocol models store a
        ///     length the client then throws away because the bit is clear, so writing back a derived
        ///     length would change those files. No model sets bit 5, which is the face-skin
        ///     equivalent, so that arm is only reachable here.
        /// </remarks>
        [Fact]
        public void ANewProtocolSmartSkinBlock_KeepsItsWidthsAndItsStoredLength()
        {
            var builder = new NewProtocolModelBuilder
            {
                Flags = 0x10 | 0x20,
                VertexSkinFlag = 1,
                FaceSkinFlag = 1,
                VertexSkins = Concat(new byte[] { 0x00 }, new byte[] { 0xAA, 0xBB }),
                FaceSkins = Concat(new byte[] { 0x81, 0x02 }, new byte[] { 0xCC }),
                VertexFlags = new byte[] { 0 },
                FaceOpcodes = new byte[] { 1 },
                FaceIndexBlock = Concat(NarrowSmart(0), NarrowSmart(0), NarrowSmart(0))
            };

            byte[] stored = builder.Build();
            ModelFile file = ModelCodec.Decode(stored, ModelCodec.FirstNewProtocolModelId);

            Assert.True(file.VertexSkinsAreSmart);
            Assert.Equal(-1, file.VertexSkins[0].Value);
            Assert.Equal(new byte[] { 0xAA, 0xBB }, file.SlackVertexSkin);
            Assert.Equal(0x8102 - 32769, file.FaceSkins[0].Value);
            Assert.Equal(JagStream.SmartWidth.TwoByte, file.FaceSkins[0].Width);
            Assert.Equal(3, file.StoredVertexSkinLength);
            AssertRoundTrips(stored, ModelCodec.FirstNewProtocolModelId);
        }

        /// <summary>
        ///     A new-protocol opcode byte is masked to three bits, so its upper bits survive.
        /// </summary>
        /// <remarks>
        ///     Model.java:1071 masks with 7 for this layout alone. An encoder that rebuilt the byte
        ///     from the strip opcode would clear whatever the upper bits carry.
        /// </remarks>
        [Fact]
        public void ANewProtocolOpcodeByte_KeepsItsUpperBits()
        {
            var builder = new NewProtocolModelBuilder
            {
                FaceOpcodes = new byte[] { 0x21 },
                FaceIndexBlock = Concat(NarrowSmart(3), NarrowSmart(1), NarrowSmart(1)),
                VertexFlags = new byte[] { 0 }
            };

            byte[] stored = builder.Build();
            ModelFile file = ModelCodec.Decode(stored, ModelCodec.FirstNewProtocolModelId);

            Assert.Equal(0x21, file.FaceOpcodes[0]);
            Assert.Equal(3, file.FaceIndexDeltas.Length);

            var model = new ModelDefinition { ModelID = ModelCodec.FirstNewProtocolModelId };
            model.Decode(new JagStream(stored));
            Assert.Equal(3, model.faceIndices1[0]);
            Assert.Equal(5, model.faceIndices3[0]);

            AssertRoundTrips(stored, ModelCodec.FirstNewProtocolModelId);
        }

        /// <summary>
        ///     A new-protocol model that sets one of the two unreachable flag bits is refused rather
        ///     than guessed at.
        /// </summary>
        /// <remarks>
        ///     Bit 3's backwards seek lands inside a 26-byte footer rather than before it, and bit 7
        ///     declares a block whose length is read from a byte located by another backwards seek.
        ///     Neither occurs in either cache, so nothing could check a guess.
        /// </remarks>
        [Fact]
        public void ANewProtocolModelWithAnUnreachableFlag_IsRefused()
        {
            var embeddedFormat = new NewProtocolModelBuilder
            {
                Flags = 0x8,
                VertexFlags = new byte[] { 0 },
                FaceOpcodes = new byte[] { 1 },
                FaceIndexBlock = Concat(NarrowSmart(0), NarrowSmart(0), NarrowSmart(0))
            };
            var trailingBlock = new NewProtocolModelBuilder
            {
                Flags = 0x80,
                VertexFlags = new byte[] { 0 },
                FaceOpcodes = new byte[] { 1 },
                FaceIndexBlock = Concat(NarrowSmart(0), NarrowSmart(0), NarrowSmart(0))
            };

            byte[] first = embeddedFormat.Build();
            byte[] second = trailingBlock.Build();
            Assert.ThrowsAny<Exception>(() => ModelCodec.Decode(first, ModelCodec.FirstNewProtocolModelId));
            Assert.ThrowsAny<Exception>(() => ModelCodec.Decode(second, ModelCodec.FirstNewProtocolModelId));
        }

        // ===================================================================
        //  Format selection
        // ===================================================================

        /// <summary>
        ///     The model id decides the new-protocol layout, and the sentinel decides the other two.
        /// </summary>
        /// <remarks>
        ///     The client tests the id first and unconditionally, and has no third sentinel - an
        ///     <c>FF FD</c> tail is legacy to it.
        /// </remarks>
        [Fact]
        public void TheEncodingIsChosenByIdThenSentinel()
        {
            byte[] sentinelled = { 0, 0, 0xFF, 0xFF };
            byte[] other = { 0, 0, 0xFF, 0xFD };

            Assert.Equal(ModelEncoding.Newer, ModelCodec.ClassifyEncoding(sentinelled, 100));
            Assert.Equal(ModelEncoding.Legacy, ModelCodec.ClassifyEncoding(other, 100));
            Assert.Equal(ModelEncoding.NewProtocol,
                ModelCodec.ClassifyEncoding(other, ModelCodec.FirstNewProtocolModelId));
            Assert.Equal(ModelEncoding.NewProtocol,
                ModelCodec.ClassifyEncoding(sentinelled, ModelCodec.LastNewProtocolModelId));
            Assert.Equal(ModelEncoding.Newer,
                ModelCodec.ClassifyEncoding(sentinelled, ModelCodec.LastNewProtocolModelId + 1));
        }

        /// <summary>
        ///     The projection scalar widths follow the format type and the face type together.
        /// </summary>
        [Fact]
        public void TheProjectionScalarWidths_FollowBothTypes()
        {
            AssertWidths(12, 1, 2, 2, 2);
            AssertWidths(14, 1, 2, 3, 2);
            AssertWidths(15, 1, 3, 3, 3);
            AssertWidths(15, 2, 2, 3, 2);
            AssertWidths(16, 2, 3, 3, 3);
            AssertWidths(15, 3, 3, 3, 3);

            Assert.Equal(6, ModelFile.ScaleStride(12));
            Assert.Equal(7, ModelFile.ScaleStride(14));
            Assert.Equal(9, ModelFile.ScaleStride(15));
            Assert.Equal(9, ModelFile.ScaleStride(16));
        }

        private static void AssertWidths(int formatType, int textureType, int first, int second, int third)
        {
            ModelFile.ScaleWidths(formatType, textureType, out int a, out int b, out int c);
            Assert.Equal(first, a);
            Assert.Equal(second, b);
            Assert.Equal(third, c);
        }

        // ===================================================================
        //  Helpers
        // ===================================================================

        private static void AssertRoundTrips(byte[] stored, int modelId)
        {
            ModelFile file = ModelCodec.Decode(stored, modelId);
            byte[] again = ModelCodec.Encode(file).ToArray();
            Assert.Equal(stored, again);
        }

        private static byte[] NarrowSmart(int value) => new[] { (byte)(value + 64) };

        private static byte[] WideSmart(int value)
        {
            int biased = value + 0xC000;
            return new[] { (byte)(biased >> 8), (byte)biased };
        }

        private static byte[] Short(int value) => new[] { (byte)(value >> 8), (byte)value };

        private static byte[] Medium(int value) =>
            new[] { (byte)(value >> 16), (byte)(value >> 8), (byte)value };

        private static byte[] Concat(params byte[][] parts)
        {
            var all = new List<byte>();
            foreach (byte[] part in parts)
                all.AddRange(part);
            return all.ToArray();
        }

        /// <summary>
        ///     Assembles a newer-format model from its blocks, deriving the footer's declared lengths
        ///     from what was actually written.
        /// </summary>
        /// <remarks>
        ///     Blocks are emitted in the offset order <c>decoder_newer_format</c> accumulates
        ///     (Model.java:435-495), which is what makes the file readable by the codec under test
        ///     without the test knowing anything about the codec.
        /// </remarks>
        private sealed class NewerModelBuilder
        {
            public int VertexCount = 1;
            public int FaceCount = 1;
            public byte Flags;
            public byte Priority;
            public byte Alpha;
            public byte FaceSkinFlag;
            public byte TextureFlag;
            public byte VertexSkinFlag;
            public byte EmbeddedFormatType = 12;
            public byte[] TextureTypes = Array.Empty<byte>();
            public byte[] VertexFlags = { 0 };
            public byte[] FaceTypes;
            public byte[] FaceOpcodes = { 1 };
            public byte[] Priorities;
            public byte[] FaceSkins;
            public byte[] VertexSkins;
            public byte[] Alphas;
            public byte[] FaceIndexBlock = Array.Empty<byte>();
            public byte[] TextureIds;
            public byte[] TextureCoordBlock = Array.Empty<byte>();
            public byte[] ColourBlock = { 0, 0 };
            public byte[] XBlock = Array.Empty<byte>();
            public byte[] YBlock = Array.Empty<byte>();
            public byte[] ZBlock = Array.Empty<byte>();
            public byte[] TexturedBlock = Array.Empty<byte>();
            public byte[] Tail = Array.Empty<byte>();
            public byte[] Gap = Array.Empty<byte>();

            public byte[] Build()
            {
                var stream = new JagStream();
                Put(stream, TextureTypes);
                Put(stream, VertexFlags);
                Put(stream, FaceTypes);
                Put(stream, FaceOpcodes);
                Put(stream, Priorities);
                Put(stream, FaceSkins);
                Put(stream, VertexSkins);
                Put(stream, Alphas);
                Put(stream, FaceIndexBlock);
                Put(stream, TextureIds);
                Put(stream, TextureCoordBlock);
                Put(stream, ColourBlock);
                Put(stream, XBlock);
                Put(stream, YBlock);
                Put(stream, ZBlock);
                Put(stream, TexturedBlock);
                Put(stream, Tail);
                Put(stream, Gap);

                if ((Flags & 0x8) == 8)
                    stream.WriteByte(EmbeddedFormatType);

                stream.WriteShort(VertexCount);
                stream.WriteShort(FaceCount);
                stream.WriteByte(TextureTypes.Length);
                stream.WriteByte(Flags);
                stream.WriteByte(Priority);
                stream.WriteByte(Alpha);
                stream.WriteByte(FaceSkinFlag);
                stream.WriteByte(TextureFlag);
                stream.WriteByte(VertexSkinFlag);
                stream.WriteShort(XBlock.Length);
                stream.WriteShort(YBlock.Length);
                stream.WriteShort(ZBlock.Length);
                stream.WriteShort(FaceIndexBlock.Length);
                stream.WriteShort(TextureCoordBlock.Length);
                stream.WriteByte(0xFF);
                stream.WriteByte(0xFF);
                return stream.Flip().ToArray();
            }
        }

        /// <summary>
        ///     Assembles a new-protocol model: a three-byte header, the block order
        ///     <c>decoder_newest_format</c> accumulates, and a 26-byte footer with no sentinel.
        /// </summary>
        private sealed class NewProtocolModelBuilder
        {
            public int VertexCount = 1;
            public int FaceCount = 1;
            public byte Flags;
            public byte Priority;
            public byte Alpha;
            public byte FaceSkinFlag;
            public byte TextureFlag;
            public byte VertexSkinFlag;
            public byte FormatTypeByte = 16;
            public byte[] TextureTypes = Array.Empty<byte>();
            public byte[] VertexFlags = { 0 };
            public byte[] FaceTypes;
            public byte[] FaceOpcodes = { 1 };
            public byte[] Priorities;
            public byte[] FaceSkins = Array.Empty<byte>();
            public byte[] VertexSkins = Array.Empty<byte>();
            public byte[] Alphas;
            public byte[] FaceIndexBlock = Array.Empty<byte>();
            public byte[] TextureIds;
            public byte[] TextureCoordBlock = Array.Empty<byte>();
            public byte[] ColourBlock = { 0, 0 };
            public byte[] XBlock = Array.Empty<byte>();
            public byte[] YBlock = Array.Empty<byte>();
            public byte[] ZBlock = Array.Empty<byte>();
            public byte[] TexturedBlock = Array.Empty<byte>();
            public byte[] Tail = Array.Empty<byte>();
            public byte[] Gap = Array.Empty<byte>();

            public byte[] Build()
            {
                var stream = new JagStream();
                stream.WriteByte(1);
                stream.WriteByte(0);
                stream.WriteByte(FormatTypeByte);
                Put(stream, TextureTypes);
                Put(stream, VertexFlags);
                Put(stream, FaceTypes);
                Put(stream, FaceOpcodes);
                Put(stream, Priorities);
                Put(stream, FaceSkins);
                Put(stream, VertexSkins);
                Put(stream, Alphas);
                Put(stream, FaceIndexBlock);
                Put(stream, TextureIds);
                Put(stream, TextureCoordBlock);
                Put(stream, ColourBlock);
                Put(stream, XBlock);
                Put(stream, YBlock);
                Put(stream, ZBlock);
                Put(stream, TexturedBlock);
                Put(stream, Tail);
                Put(stream, Gap);

                stream.WriteShort(VertexCount);
                stream.WriteShort(FaceCount);
                stream.WriteShort(TextureTypes.Length);
                stream.WriteByte(Flags);
                stream.WriteByte(Priority);
                stream.WriteByte(Alpha);
                stream.WriteByte(FaceSkinFlag);
                stream.WriteByte(TextureFlag);
                stream.WriteByte(VertexSkinFlag);
                stream.WriteShort(XBlock.Length);
                stream.WriteShort(YBlock.Length);
                stream.WriteShort(ZBlock.Length);
                stream.WriteShort(FaceIndexBlock.Length);
                stream.WriteShort(TextureCoordBlock.Length);
                stream.WriteShort(VertexSkins.Length);
                stream.WriteShort(FaceSkins.Length);
                return stream.Flip().ToArray();
            }
        }

        private static void Put(JagStream stream, byte[] data)
        {
            if (data != null && data.Length > 0)
                stream.Write(data, 0, data.Length);
        }
    }
}
