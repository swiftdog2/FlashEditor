using FlashEditor;
using FlashEditor.Definitions;
using Xunit;

namespace FlashEditor.Tests.Cache
{
    public class ObjectDefinitionCodecTests
    {
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
            s.WriteByte(72); s.WriteByte(3);    // offsetZ (1 UByte, NOT Short!)

            s.WriteByte(73);                    // obstructsWheelchair (flag)
            s.WriteByte(74);                    // isSolid (flag)
            s.WriteByte(75);                    // flag only (0 bytes)

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

            /*  round-trip  */
            var def = ObjectDefinition.DecodeFromStream(s);
            var encoded = def.Encode();

            Assert.Equal(s.ToArray(), encoded.ToArray());
        }
    }
}
