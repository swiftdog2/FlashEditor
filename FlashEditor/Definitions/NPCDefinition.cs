using static FlashEditor.utils.DebugUtil;
using System.Collections.Generic;

namespace FlashEditor {
    internal class NPCDefinition {
        byte primaryShadowModifier = 223;
        byte respawnDirection = 7;
        byte secondaryShadowModifier = 143;
        byte walkMask;

        int hue;
        int lightness;
        int opacity;
        int saturation;

        int[] campaigns;
        int[] dialogueModels;
        int[] modelIds;
        int[] recolorDst;
        int[] recolorSrc;
        int[] retextureDst;
        int[] retextureSrc;

        public bool animateIdle = true;
        public bool clickable = true;
        public bool drawMinimapDot = true;
        public bool hasRenderPriority;
        public bool invisiblePriority;
        public bool slowWalk = true;
        public bool visiblePriority;
        public byte mainOptionIndex;
        public byte[] recolorDstPalette;
        public int ambient;
        public int ambientSoundVolume = 255;
        public int armyIcon = -1;
        public int attackOpCursor = -1;
        public int contrast;
        public int crawlSound = -1;
        public int headIcon = -1;
        public int height = -1;
        public int hitbarSprite = -1;
        public int id;
        public int idleSound = -1;
        public int last;
        public int level = -1;
        public int mapIcon = -1;
        public int movementType;
        public int primaryCursor = -1;
        public int primaryCursorOp = -1;
        public int renderTypeID = -1;
        public int rotation = 32;
        public int runSound = -1;
        public int scaleXY = 128;
        public int scaleZ = 128;
        public int secondaryCursor = -1;
        public int secondaryCursorOp = -1;
        public int size = 1;
        public int soundDistance;
        public int spriteId = -1;
        public int varbit = -1;
        public int varp = -1;
        public int walkSound = -1;
        public int[] cursorOps;
        public int[] morphs;
        public int[][] translations;
        public string name = "null";
        public string[] options = new[] { null, null, null, null, null, "Examine" };
        short primaryShadowColour;
        short secondaryShadowColour;

        int anInt828;
        int anInt837;
        int anInt847;
        int anInt848;
        int anInt864;

        int[][] anIntArrayArray882;

        public bool unknownBoolean7;
        public byte[] aByteArray8445, aByteArray8446;

        public int op44;
        public int op45;
        public int unknownByte1, unknownByte2, unknownByte3, unknownByte4, unknownByte5, unknownByte6;

        public int[] unknownOptions = {-1, -1, -1, -1, -1, -1 };


        private SortedDictionary<int, object> config;

        /// <summary>
        /// Constructs a new item definition from the stream data
        /// </summary>
        /// <param name="stream">The stream containing the encoded item data</param>
        public NPCDefinition(JagStream stream) {
            Decode(stream);
        }

        /// <summary>
        /// Overrides defaults from the stream
        /// </summary>
        /// <param name="stream"></param>
        public void Decode(JagStream stream) {
            if(stream != null) {
                while(true) {
                    int opcode = stream.ReadUnsignedByte();

                    if(opcode == 0 || opcode == 255)
                        break;

                    Decode(stream, opcode);
                }
            }
        }

        /// <summary>
        /// Overrides a specific property corresponding to opcode
        /// </summary>
        /// <param name="stream">The stream to read from</param>
        /// <param name="opcode">The opcode value signalling which type to read</param>
        private void Decode(JagStream stream, int opcode) {
            if(opcode == 1) {
                int length = stream.ReadByte();
                modelIds = new int[length];
                for(int k = 0; k < length; k++) {
                    modelIds[k] = stream.ReadShort();
                    if((modelIds[k] ^ 0xffffffff) == -65536)
                        modelIds[k] = -1;
                }
            } else if(opcode == 2) {
                name = stream.ReadJagexString();
            } else if(opcode == 12) {
                size = stream.ReadByte();
            } else if(opcode >= 30 && opcode < 34) { //was 36 before
                options[opcode - 30] = stream.ReadJagexString();
                if(options[opcode - 30] == "Hidden")
                    options[opcode - 30] = null;
            } else if(opcode == 40) {
                //Read colours
                int length = stream.ReadByte();
                recolorDst = new int[length];
                recolorSrc = new int[length];
                for(int count = 0; (length ^ 0xffffffff) < (count ^ 0xffffffff); count++) {
                    recolorSrc[count] = stream.ReadShort();
                    recolorDst[count] = stream.ReadShort();
                }
            } else if(opcode == 41) {
                //Read textures
                int i = stream.ReadByte();
                retextureSrc = new int[i];
                retextureDst = new int[i];
                for(int i_54_ = 0; (i_54_ ^ 0xffffffff) > (i ^ 0xffffffff); i_54_++) {
                    retextureSrc[i_54_] = stream.ReadShort();
                    retextureDst[i_54_] = stream.ReadShort();
                }
            } else if(opcode == 42) {
                int length = stream.ReadByte();
                recolorDstPalette = new byte[length];
                for(int i_55_ = 0; length > i_55_; i_55_++) {
                    recolorDstPalette[i_55_] = (byte) stream.ReadByte();
                }
            } else if(opcode == 44) {
                op44 = stream.ReadShort();
            } else if(opcode == 45) {
                op45 = stream.ReadShort();
            } else if(opcode == 60) {
                int length = stream.ReadByte();
                dialogueModels = new int[length];
                for(int count = 0; (count ^ 0xffffffff) > (length ^ 0xffffffff); count++)
                    dialogueModels[count] = stream.ReadShort();
            } else if(opcode == 93) {
                drawMinimapDot = false;
            } else if(opcode == 95) {
                level = stream.ReadShort();
            } else if(opcode == 97) {
                scaleXY = stream.ReadShort();
            } else if(opcode == 98) {
                scaleZ = stream.ReadShort();
            } else if(opcode == 99) {
                hasRenderPriority = true;
            } else if(opcode == 100) {
                ambient = stream.ReadByte();
            } else if(opcode == 101) {
                contrast = stream.ReadByte();
            } else if(opcode == 102) {
                headIcon = stream.ReadShort();
            } else if(opcode == 103) {
                rotation = stream.ReadShort();
            } else if(opcode == 106 || opcode == 118) {
                varbit = stream.ReadShort();
                if(varbit == 65535)
                    varbit = -1;

                varp = stream.ReadShort();
                if(varp == 65535)
                    varp = -1;

                int last = -1;
                if(opcode == 118) {
                    last = stream.ReadShort();
                    if(last == 65535)
                        last = -1;
                }

                int count = stream.ReadByte();
                morphs = new int[2 + count];
                for(int index = 0; count >= index; index++) {
                    morphs[index] = stream.ReadShort();
                    if(morphs[index] == 65535)
                        morphs[index] = -1;
                }
                morphs[count + 1] = last;
            } else if(opcode == 107)
                clickable = false;
            else if(opcode == 109)
                slowWalk = false;
            else if(opcode == 111)
                animateIdle = false;
            else if(opcode == 113) {
                primaryShadowColour = (short) (stream.ReadShort());
                secondaryShadowColour = (short) (stream.ReadShort());
            } else if(opcode == 114) {
                primaryShadowModifier = (byte) (stream.ReadByte());
                secondaryShadowModifier = (byte) (stream.ReadByte());
            } else if(opcode == 119)
                walkMask = (byte) (stream.ReadByte());
            else if(opcode == 121) {
                //Translations

                translations = new int[modelIds == null ? 0 : modelIds.Length][];
                int length = (stream.ReadByte());
                for(int i_62_ = 0; ((i_62_ ^ 0xffffffff) > (length ^ 0xffffffff)); i_62_++) {
                    int index = stream.ReadByte();
                    int[] nigga = (translations[index] = (new int[3]));
                    nigga[0] = stream.ReadByte();
                    nigga[1] = stream.ReadByte();
                    nigga[2] = stream.ReadByte();
                }
            } else if(opcode == 122)
                hitbarSprite = (stream.ReadShort());
            else if(opcode == 123)
                height = (stream.ReadShort());
            else if(opcode == 125)
                respawnDirection = (byte) (stream.ReadByte());
            else if(opcode == 127)
                renderTypeID = (stream.ReadShort());
            else if(opcode == 128)
                movementType = stream.ReadByte();
            else if(opcode == 134) {
                idleSound = stream.ReadShort();
                if(idleSound == 65535)
                    idleSound = -1;

                crawlSound = (stream.ReadShort());
                if(crawlSound == 65535)
                    crawlSound = -1;

                walkSound = stream.ReadShort();
                if((walkSound ^ 0xffffffff) == -65536)
                    walkSound = -1;

                runSound = (stream.ReadShort());
                if((runSound ^ 0xffffffff) == -65536)
                    runSound = -1;

                soundDistance = stream.ReadByte();
            } else if(opcode == 135) {
                primaryCursorOp = stream.ReadByte();
                primaryCursor = stream.ReadShort();
            } else if(opcode == 136) {
                secondaryCursorOp = stream.ReadByte();
                secondaryCursor = stream.ReadShort();
            } else if(opcode == 137)
                attackOpCursor = stream.ReadShort();
            else if(opcode == 138)
                armyIcon = stream.ReadShort();
            else if(opcode == 139)
                spriteId = stream.ReadShort();
            else if(opcode == 140)
                ambientSoundVolume = stream.ReadByte();
            else if(opcode == 141)
                visiblePriority = true;
            else if(opcode == 142)
                mapIcon = stream.ReadShort();
            else if(opcode == 143) {
                invisiblePriority = true;
            } else if(opcode >= 150 && opcode < 155) {
                options[opcode - 150] = stream.ReadJagexString();
                if(options[opcode - 150] == "Hidden")
                    options[opcode - 150] = null;
            } else if(opcode == 155) {
                hue = stream.ReadByte();
                saturation = stream.ReadByte();
                lightness = stream.ReadByte();
                opacity = stream.ReadByte();
            } else if(opcode == 158) {
                mainOptionIndex = (byte) 1;
            } else if(opcode == 159) {
                mainOptionIndex = (byte) 0;
            } else if(opcode == 160) {
                int length = stream.ReadByte();
                campaigns = new int[length];
                for(int count = 0; length > count; count++)
                    campaigns[count] = stream.ReadShort();
            } else if(opcode == 162)
                unknownBoolean7 = true;
            else if(opcode == 163)
                anInt864 = stream.ReadByte();
            else if(opcode == 164) {
                anInt848 = stream.ReadShort();
                anInt837 = stream.ReadShort();
            } else if(opcode == 165)
                anInt847 = stream.ReadByte();
            else if(opcode == 168)
                anInt828 = stream.ReadByte();
            else if(opcode >= 170 && opcode < 176) {
                unknownOptions[opcode - 170] = stream.ReadShort();
            } else if(opcode == 179) {
                unknownByte1 = stream.ReadByte();
                unknownByte2 = stream.ReadByte();
                unknownByte3 = stream.ReadByte();
                unknownByte4 = stream.ReadByte();
                unknownByte5 = stream.ReadByte();
                unknownByte6 = stream.ReadByte();
            } else if(opcode == 249) {
                int configLength = stream.ReadByte();

                if(config == null)
                    config = new SortedDictionary<int, object>();

                for(int k = 0; k < configLength; k++) {
                    bool stringInstance = stream.ReadByte() == 1;
                    int key = stream.ReadMedium();

                    object value;
                    if(stringInstance)
                        value = stream.ReadJagexString();
                    else
                        value = stream.ReadInt();

                    if(config.ContainsKey(key))
                        config[key] = value;
                    else
                        config.Add(key, value);
                }
            }
        }

        /// <summary>
        /// Overrides a specific property corresponding to opcode
        /// </summary>
        /// <param name="stream">The stream to read from</param>
        /// <param name="opcode">The opcode value signalling which type to read</param>
        public JagStream Encode() {
            JagStream stream = new JagStream();

            stream.WriteByte(1);
            stream.WriteByte((byte) modelIds.Length);
            for(int k = 0; k < modelIds.Length; k++) {
                if(modelIds[k] == -1)
                    stream.WriteShort(-65536);
                else
                    stream.WriteShort(modelIds[k]);
            }

            stream.WriteByte(2);
            stream.WriteString(name);

            stream.WriteByte(12);
            stream.WriteByte((byte) this.size);

            for(int k = 30; k < 34; k++) {
                stream.WriteByte((byte) k);
                if(options[k - 30] == null)
                    stream.WriteString("Hidden");
                else
                    stream.WriteString(options[k]);
            }

            stream.WriteByte(40);
            stream.WriteByte((byte) recolorSrc.Length);
            for(int k = 0; k < recolorSrc.Length; k++) {
                stream.WriteShort(recolorSrc[k]);
                stream.WriteShort(recolorDst[k]);
            }

            stream.WriteByte(41);
            stream.WriteByte((byte) retextureSrc.Length);
            for(int k = 0; k < retextureSrc.Length; k++) {
                stream.WriteShort(retextureSrc[k]);
                stream.WriteShort(retextureDst[k]);
            }

            stream.WriteByte(42);
            stream.WriteByte((byte) recolorDstPalette.Length);
            for(int k = 0; k < recolorDstPalette.Length; k++)
                stream.WriteByte((byte) recolorDstPalette[k]);

            stream.WriteByte(44);
            stream.WriteShort(op44);

            stream.WriteByte(45);
            stream.WriteShort(op45);

            stream.WriteByte(60);
            stream.WriteByte((byte) dialogueModels.Length);
            for(int k = 0; k < dialogueModels.Length; k++)
                stream.WriteShort(dialogueModels[k]);

            if(!drawMinimapDot)
                stream.WriteByte(93);

            stream.WriteByte(95);
            stream.WriteShort((byte) level);

            stream.WriteByte(97);
            stream.WriteShort((byte) scaleXY);

            stream.WriteByte(98);
            stream.WriteShort((byte) scaleZ);

            if(this.hasRenderPriority)
                stream.WriteByte(99);

            stream.WriteByte(100);
            stream.WriteByte((byte) ambient);

            stream.WriteByte(101);
            stream.WriteByte((byte) contrast);

            stream.WriteByte(102);
            stream.WriteShort(headIcon);

            stream.WriteByte(103);
            stream.WriteShort(rotation);

            //same method for opcode 118...
            stream.WriteByte(106);
            if(varbit == -1)
                varbit = 65535;
            stream.WriteShort(varbit);

            if(varp == -1)
                varbit = 65535;
            stream.WriteShort(varbit);

            if(last == -1)
                last = 65535;
            stream.WriteShort(last);

            stream.WriteByte((byte) (morphs.Length + 2));
            for(int k = 0; morphs.Length >= k; k++) {
                if(morphs[k] == -1)
                    morphs[k] = 65535;

                stream.WriteShort(morphs[k]);
            }

            morphs = new int[2 + morphs.Length];
            for(int k = 0; morphs.Length >= k; k++) {
                if(morphs[k] == -1)
                    morphs[k] = 65535;
                stream.WriteShort(morphs[k]);
            }

            if(!clickable)
                stream.WriteByte(107);

            if(!slowWalk)
                stream.WriteByte(109);

            if(!animateIdle)
                stream.WriteByte(111);

            stream.WriteByte(113);

            stream.WriteShort((short) primaryShadowColour);
            stream.WriteShort((short) secondaryShadowColour);

            stream.WriteByte(114);
            stream.WriteByte(primaryShadowModifier);
            stream.WriteByte(secondaryShadowModifier);


            stream.WriteByte(119);
            stream.WriteByte(walkMask);

            stream.WriteByte(121);
            //Translations


            /*
            translations = new int[modelIds == null ? 0 : modelIds.Length][];
            int length = (stream.WriteByte2());
            for(int i_62_ = 0; ((i_62_ ^ 0xffffffff) > (length ^ 0xffffffff)); i_62_++) {
                int index = stream.WriteByte2();
                int[] nigga = (translations[index] = (new int[3]));
                nigga[0] = stream.WriteByte2();
                nigga[1] = stream.WriteByte2();
                nigga[2] = stream.WriteByte2();
            }
        } else*/

            stream.WriteByte(122);
            stream.WriteShort(hitbarSprite);

            stream.WriteByte(123);
            stream.WriteShort(height);

            stream.WriteByte(125);
            stream.WriteByte(respawnDirection);

            stream.WriteByte(127);
            stream.WriteShort(renderTypeID);

            stream.WriteByte(128);
            stream.WriteByte((byte) movementType);

            stream.WriteByte(134);
            if(idleSound == -1)
                idleSound = 65535;
            stream.WriteShort(idleSound);

            if(crawlSound == -1)
                crawlSound = 65535;
            stream.WriteShort(crawlSound);

            if(walkSound == -1)
                walkSound = 65535;
            stream.WriteShort(walkSound);

            if(runSound == -1)
                runSound = 65536;
            stream.WriteShort(runSound);

            stream.WriteByte((byte) soundDistance);

            stream.WriteByte(135);
            stream.WriteByte((byte) primaryCursorOp);
            stream.WriteShort(primaryCursor);

            stream.WriteByte(136);
            stream.WriteByte((byte) secondaryCursorOp);
            stream.WriteShort(secondaryCursor);

            stream.WriteByte(137);
            stream.WriteShort(attackOpCursor);

            stream.WriteByte(138);
            stream.WriteShort(armyIcon);

            stream.WriteByte(139);
            stream.WriteShort(spriteId);

            stream.WriteByte(140);
            stream.WriteByte((byte) ambientSoundVolume);

            stream.WriteByte(141);
            visiblePriority = true;

            stream.WriteByte(142);
            stream.WriteShort(mapIcon);

            stream.WriteByte(143);
            invisiblePriority = true;

            for(int k = 150; k < 155; k++) {
                if(options[k - 150] == null)
                    stream.WriteString("Hidden");
                else
                    stream.WriteString(options[k - 150]);
            }

            stream.WriteByte(155);
            stream.WriteByte((byte) hue);
            stream.WriteByte((byte) saturation);
            stream.WriteByte((byte) lightness);
            stream.WriteByte((byte) opacity);

            stream.WriteByte(158);
            mainOptionIndex = (byte) 1;

            stream.WriteByte(159);
            mainOptionIndex = (byte) 0;

            stream.WriteByte(160);
            stream.WriteByte((byte) campaigns.Length);
            for(int k = 0; k < campaigns.Length; k++)
                stream.WriteShort(campaigns[k]);

            stream.WriteByte(162);
            unknownBoolean7 = true;

            stream.WriteByte(163);
            stream.WriteByte((byte) anInt864);

            stream.WriteByte(164);
            stream.WriteShort(anInt848);
            stream.WriteShort(anInt837);


            stream.WriteByte(165);
            stream.WriteByte((byte) anInt847);

            stream.WriteByte(168);
            stream.WriteByte((byte) anInt828);

            //May or may not exist?
            for(int k = 0; k < 6; k++)
                if(unknownOptions[k] != -1) {
                    stream.WriteShort(170 + k);
                    stream.WriteShort(unknownOptions[k]);
                }


            stream.WriteByte(179);
            stream.WriteByte((byte) unknownByte1);
            stream.WriteByte((byte) unknownByte2);
            stream.WriteByte((byte) unknownByte3);
            stream.WriteByte((byte) unknownByte4);
            stream.WriteByte((byte) unknownByte5);
            stream.WriteByte((byte) unknownByte6);

            if(config != null) {
                stream.WriteByte(249);
                stream.WriteByte((byte) config.Count);

                foreach(KeyValuePair<int, object> kvp in config) {
                    if(kvp.Key == 1) //instance of string
                        stream.WriteString((string) kvp.Value);
                    else
                        stream.WriteInteger((int) kvp.Value);
                }
            }

            return stream.Flip();
        }

        internal void SetId(int id) {
            this.id = id;
        }
    }
}