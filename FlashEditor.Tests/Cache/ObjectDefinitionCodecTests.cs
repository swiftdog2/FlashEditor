using FlashEditor;
using FlashEditor.Definitions;
using Xunit;

namespace FlashEditor.Tests.Cache
{
    public class ObjectDefinitionCodecTests
    {
        /// <summary>
        /// Builds a hand-crafted binary stream exercising every opcode the encoder supports,
        /// then verifies: decode → field values correct → encode → bytes match → decode again → fields still correct.
        /// </summary>
        [Fact]
        public void ObjectDefinition_EncodeDecode_RoundTrips_AllKnownOpcodes()
        {
            var s = new JagStream();

            /*  5  - model list (format-2) + mandatory extra model block  */
            s.WriteByte(5);
            s.WriteByte(1);             // 1 group
            s.WriteSignedByte(-1);      // type
            s.WriteByte(1);             // 1 model
            s.WriteShort(101);          // model id
            // Extra model group block for opcode 5 (skipReadModelIds)
            s.WriteByte(0);             // 0 extra groups

            /*  basic scalars  */
            s.WriteByte(2); s.WriteJagexString("AllOpcodes");
            s.WriteByte(14); s.WriteByte(2);        // sizeX
            s.WriteByte(15); s.WriteByte(3);        // sizeY
            s.WriteByte(17);                        // walkable=false, clipType=0
            s.WriteByte(19); s.WriteByte(4);        // category
            s.WriteByte(21);                        // contourGroundType=1 (flag only, 0 bytes)
            s.WriteByte(22);                        // isClipped flag
            s.WriteByte(23);                        // obstructsGround = 1
            s.WriteByte(24); s.WriteShort(10);      // animationId
            s.WriteByte(27);                        // clipType=1
            s.WriteByte(28); s.WriteByte(1);        // decorDisplacement (1 byte)
            s.WriteByte(29); s.WriteSignedByte(-5); // ambientLighting (1 signed byte)

            /*  action strings 30-34  */
            for (int i = 0; i < 5; i++)
            {
                s.WriteByte(30 + i);
                s.WriteJagexString($"Act{i}");
            }

            /*  recolour (40)  */
            s.WriteByte(40);
            s.WriteByte(1);
            s.WriteShort(1);
            s.WriteShort(2);

            /*  retexture (41)  */
            s.WriteByte(41);
            s.WriteByte(1);
            s.WriteShort(3);
            s.WriteShort(4);

            /*  texture priorities (42)  */
            s.WriteByte(42);
            s.WriteByte(1);
            s.WriteSignedByte(0);

            /*  render flags  */
            s.WriteByte(62);                 // flipped
            s.WriteByte(64);                 // castsShadow = false
            s.WriteByte(65); s.WriteShort(128); // scaleX
            s.WriteByte(66); s.WriteShort(129); // scaleY
            s.WriteByte(67); s.WriteShort(130); // scaleZ
            s.WriteByte(68); s.WriteShort(5);   // mapSceneId
            s.WriteByte(69); s.WriteByte(2);    // minimapForceClip

            /* offsets */
            s.WriteByte(70); s.WriteShort(1);   // offsetX (signed short)
            s.WriteByte(71); s.WriteShort(2);   // offsetY (signed short)
            s.WriteByte(72); s.WriteShort(3);   // offsetZ (signed short, like 70/71)

            s.WriteByte(73);                    // obstructsWheelchair (flag)
            s.WriteByte(74);                    // isSolid (flag)
            s.WriteByte(75); s.WriteByte(7);    // 1 UByte payload, not a flag

            /*  morph table (92)  */
            s.WriteByte(92);
            s.WriteShort(0xFFFF);  // varbit
            s.WriteShort(0xFFFF);  // varp
            s.WriteShort(0xFFFF);  // defaultId
            s.WriteByte(0);        // count
            s.WriteShort(0xFFFF);  // morphIds[0]

            /* ambient sound - opcode 79 */
            s.WriteByte(79);
            s.WriteShort(1002);   // ambientSoundId (anInt3900)
            s.WriteShort(500);    // ambientSoundExtra (anInt3905) -- was missing!
            s.WriteByte(1);       // ambientSoundLoops (anInt3904)
            s.WriteByte(1);       // extraSound count
            s.WriteShort(300);    // extraSound[0]

            /*  menuOps 150-154  */
            for (int i = 0; i < 5; i++)
            {
                s.WriteByte(150 + i);
                s.WriteJagexString($"Menu{i}");
            }

            /*  minimap icons (160)  */
            s.WriteByte(160);
            s.WriteByte(1);
            s.WriteShort(500);

            /*  params map (249)  */
            s.WriteByte(249);
            s.WriteByte(1);          // one param
            s.WriteByte(1);          // is-string
            s.WriteMedium(0x010203);
            s.WriteJagexString("val");

            /* terminator */
            s.WriteByte(0);
            s.Flip();

            byte[] originalBytes = s.ToArray();

            // ── Phase 1: Decode and verify every public field ──
            var def = ObjectDefinition.DecodeFromStream(new JagStream(originalBytes));
            AssertDecodedFields(def);

            // ── Phase 2: Encode and verify byte-level equality ──
            var encoded = def.Encode();
            Assert.Equal(originalBytes, encoded.ToArray());

            // ── Phase 3: Decode the re-encoded bytes and verify fields again ──
            var def2 = ObjectDefinition.DecodeFromStream(new JagStream(encoded.ToArray()));
            AssertDecodedFields(def2);

            // ── Phase 4: Second encode must also match ──
            var encoded2 = def2.Encode();
            Assert.Equal(originalBytes, encoded2.ToArray());
        }

        /// <summary>
        /// Asserts that a decoded ObjectDefinition has the exact field values
        /// expected from the hand-crafted byte stream above.
        /// </summary>
        private static void AssertDecodedFields(ObjectDefinition def)
        {
            // name
            Assert.Equal("AllOpcodes", def.name);

            // geometry
            Assert.Equal(2, def.sizeX);
            Assert.Equal(3, def.sizeY);
            Assert.False(def.walkable);
            Assert.True(def.isClipped);
            Assert.Equal(4, def.category);

            // model groups (opcode 5)
            Assert.True(def.usesOpcode5);
            Assert.Single(def.modelTypes);
            Assert.Equal(-1, def.modelTypes[0]);
            Assert.Single(def.modelIds);
            Assert.Single(def.modelIds[0]);
            Assert.Equal((ushort)101, def.modelIds[0][0]);

            // animation
            Assert.Equal(10, def.animationId);

            // lighting (opcode 28: byte 1 << 2 = 4, opcode 29: sbyte -5)
            Assert.Equal(4, def.modelBrightness);
            Assert.Equal(-5, def.modelContrast);

            // scale
            Assert.Equal(128, def.scaleX);
            Assert.Equal(129, def.scaleY);
            Assert.Equal(130, def.scaleZ);

            // actions 30-34
            for (int i = 0; i < 5; i++)
                Assert.Equal($"Act{i}", def.actions[i]);

            // recolour (opcode 40)
            Assert.Single(def.recolSrc);
            Assert.Equal((short)1, def.recolSrc[0]);
            Assert.Equal((short)2, def.recolDst[0]);

            // retexture (opcode 41)
            Assert.Single(def.retexSrc);
            Assert.Equal((short)3, def.retexSrc[0]);
            Assert.Equal((short)4, def.retexDst[0]);

            // morph (opcode 92: varbit=-1, varp=-1, count=0, morphIds[0]=-1, defaultId=-1)
            Assert.Equal(-1, def.morphVarbit);
            Assert.Equal(-1, def.morphVarp);
            Assert.NotNull(def.morphIds);
            Assert.Equal(2, def.morphIds.Length);
            Assert.Equal(-1, def.morphIds[0]);
            Assert.Equal(-1, def.morphIds[1]);

            // sound (opcode 79)
            Assert.Equal(1002, def.ambientSoundId);
            Assert.Equal(1, def.ambientSoundLoops);
            Assert.NotNull(def.extraSounds);
            Assert.Single(def.extraSounds);
            Assert.Equal(300, def.extraSounds[0]);

            // menuOps 150-154
            for (int i = 0; i < 5; i++)
                Assert.Equal($"Menu{i}", def.menuOps[i]);

            // minimap icons (opcode 160)
            Assert.NotNull(def.minimapIcons);
            Assert.Single(def.minimapIcons);
            Assert.Equal((ushort)500, def.minimapIcons[0]);

            // params (opcode 249)
            Assert.NotNull(def.parameters);
            Assert.Single(def.parameters);
            Assert.True(def.parameters.ContainsKey(0x010203));
            Assert.Equal("val", def.parameters[0x010203]);

            // opcode flags that should be set
            Assert.True(def.decoded[5]);
            Assert.True(def.decoded[17]);
            Assert.True(def.decoded[21]);
            Assert.True(def.decoded[23]);
            Assert.True(def.decoded[27]);
            Assert.True(def.decoded[28]);
            Assert.True(def.decoded[29]);
            Assert.True(def.decoded[62]);
            Assert.True(def.decoded[64]);
            Assert.True(def.decoded[68]);
            Assert.True(def.decoded[72]);
            Assert.True(def.decoded[73]);
            Assert.True(def.decoded[74]);
            Assert.True(def.decoded[75]);
            Assert.True(def.decoded[92]);
        }
    }
}
