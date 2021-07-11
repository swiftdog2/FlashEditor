using FlashEditor.cache;
using FlashEditor.utils;
using System;
using System.Collections.Generic;
using System.Text;

namespace FlashEditor {
    internal class ItemDefinition {
        public string name;
        public int id;
        public bool[] decoded = new bool[256];
        public string[] groundOptions;
        public string[] inventoryOptions;

        public int inventoryModelId;

        public int maleWearModel1;
        public int maleWearModel2;
        public int femaleWearModel1;
        public int femaleWearModel2;

        public int manWearXOffset;
        public int manWearYOffset;
        public int manWearZOffset;

        public int womanWearXOffset;
        public int womanWearYOffset;
        public int womanWearZOffset;

        public int modelOffset1;
        public int modelOffset2;

        public int modelRotation1;
        public int modelRotation2;

        public int modelZoom;

        public int colourEquip1;
        public int colourEquip2;

        public short[] originalModelColors;
        public short[] modifiedModelColors;

        public int ambient;
        public int contrast;
        public int lendId;
        public int lendTemplateId;
        public bool membersOnly;
        public int notedId;
        public int notedTemplateId;
        public int stackable;
        public int[] stackableAmounts;
        public int[] stackableIds;
        public int teamId;
        public short[] textureColour1;
        public short[] textureColour2;
        public bool unnoted;
        public int value;
        public int nameColor;
        public bool hasNameColor;
        public int equipSlotId;
        public int equipId;
        public int multiStackSize;

        public SortedDictionary<int, object> itemParams;

        /// <summary>
        /// Constructs a new item definition from the stream data
        /// </summary>
        /// <param name="stream">The stream containing the encoded item data</param>
        public ItemDefinition(JagStream stream) {
            groundOptions = new[] { null, null, "take", null, null };
            inventoryOptions = new[] { null, null, null, null, "drop" };

            Decode(stream);
        }

        public int GetId() {
            return id;
        }

        /// <summary>
        /// Overrides defaults from the stream
        /// </summary>
        /// <param name="stream"></param>
        public void Decode(JagStream stream) {
            int total = 0;

            StringBuilder sb = new StringBuilder();

            if(stream != null) {
                while(true) {
                    //Read unsigned byte
                    int opcode = stream.ReadUnsignedByte();

                    //Flag that the default value was overridden
                    decoded[opcode] = true;

                    if(opcode == 0)
                        break;

                    Decode(stream, opcode);

                    //Shouldn't be necessary, but there are no more than 256 opcodes anyway so no harm
                    if(total++ > 256)
                        break;
                }

                for(int k = 0; k < decoded.Length; k++) {
                    if(decoded[k])
                        sb.Append(k + " ");
                }
            }
            //DebugUtil.Debug(name + " OPCODEs: " + sb.ToString());
        }

        /// <summary>
        /// Overrides a specific property corresponding to opcode
        /// </summary>
        /// <param name="stream">The stream to read from</param>
        /// <param name="opcode">The opcode value signalling which type to read</param>
        private void Decode(JagStream stream, int opcode) {
            if(opcode == 1) {
                inventoryModelId = stream.ReadUnsignedShort();
            } else if(opcode == 2) {
                name = stream.ReadJagexString();
            } else if(opcode == 4) {
                modelZoom = stream.ReadUnsignedShort();
            } else if(opcode == 5) {
                modelRotation1 = stream.ReadUnsignedShort();
            } else if(opcode == 6) {
                modelRotation2 = stream.ReadUnsignedShort();
            } else if(opcode == 7) {
                modelOffset1 = stream.ReadUnsignedShort();
                if(modelOffset1 > 32767)
                    modelOffset1 -= 65536;
                modelOffset1 <<= 0;
            } else if(opcode == 8) {
                modelOffset2 = stream.ReadUnsignedShort();
                if(modelOffset2 > 32767)
                    modelOffset2 -= 65536;
                modelOffset2 <<= 0;
            } else if(opcode == 11) {
                stackable = 1;
            } else if(opcode == 12) {
                value = stream.ReadInt();
            } else if(opcode == 13) {
                equipSlotId = stream.ReadUnsignedByte();
            } else if(opcode == 14) {
                equipId = stream.ReadUnsignedByte();
            } else if(opcode == 16) {
                membersOnly = true;
            } else if(opcode == 18) {
                multiStackSize = stream.ReadUnsignedShort();
            } else if(opcode == 23) {
                maleWearModel1 = stream.ReadUnsignedShort();
            } else if(opcode == 24) {
                femaleWearModel1 = stream.ReadUnsignedShort();
            } else if(opcode == 25) {
                maleWearModel2 = stream.ReadUnsignedShort();
            } else if(opcode == 26) {
                femaleWearModel2 = stream.ReadUnsignedShort();
            } else if(opcode == 27) {
                stream.ReadUnsignedByte();
            } else if(opcode >= 30 && opcode < 35) {
                groundOptions[opcode - 30] = stream.ReadJagexString();
                if(groundOptions[opcode - 30].Equals("Hidden", StringComparison.InvariantCultureIgnoreCase))
                    groundOptions[opcode - 30] = null;
            } else if(opcode >= 35 && opcode < 40) {
                inventoryOptions[opcode - 35] = stream.ReadJagexString();
            } else if(opcode == 40) {
                int length = stream.ReadUnsignedByte();
                originalModelColors = new short[length];
                modifiedModelColors = new short[length];
                for(int index = 0; index < length; index++) {
                    originalModelColors[index] = unchecked((short) (stream.ReadUnsignedShort()));
                    modifiedModelColors[index] = unchecked((short) (stream.ReadUnsignedShort()));
                }
            } else if(opcode == 41) {
                int length = stream.ReadUnsignedByte();
                textureColour1 = new short[length];
                textureColour2 = new short[length];
                for(int index = 0; index < length; index++) {
                    textureColour1[index] = unchecked((short) (stream.ReadUnsignedShort()));
                    textureColour2[index] = unchecked((short) (stream.ReadUnsignedShort()));
                }
            } else if(opcode == 42) {
                int length = stream.ReadUnsignedByte();
                for(int index = 0; index < length; index++)
                    stream.ReadByte();
            } else if(opcode == 43) {
                nameColor = stream.ReadInt();
                hasNameColor = true;
            } else if(opcode == 44) {
                stream.ReadUnsignedShort();
                //There's more crap but for the moment it's unnecessary
            } else if(opcode == 45) { //idk
                stream.ReadUnsignedShort();
                //As above
            } else if(opcode == 65) {
                unnoted = true;
            } else if(opcode == 78) {
                colourEquip1 = stream.ReadUnsignedShort();
            } else if(opcode == 79) {
                colourEquip2 = stream.ReadUnsignedShort();
            } else if(opcode == 90) {
                stream.ReadUnsignedShort();
            } else if(opcode == 91) {
                stream.ReadUnsignedShort();
            } else if(opcode == 92) {
                stream.ReadUnsignedShort();
            } else if(opcode == 93) {
                stream.ReadUnsignedShort();
            } else if(opcode == 94) {
                stream.ReadUnsignedShort();
            } else if(opcode == 95) {
                stream.ReadUnsignedShort();
            } else if(opcode == 96) {
                stream.ReadUnsignedByte();
            } else if(opcode == 97) {
                notedId = stream.ReadUnsignedShort();
            } else if(opcode == 98) {
                notedTemplateId = stream.ReadUnsignedShort();
            } else if(opcode >= 100 && opcode < 110) {
                if(stackableIds == null) {
                    stackableIds = new int[10];
                    stackableAmounts = new int[10];
                }
                stackableIds[opcode - 100] = stream.ReadUnsignedShort();
                stackableAmounts[opcode - 100] = stream.ReadUnsignedShort();
            } else if(opcode == 110) {
                stream.ReadUnsignedShort();
            } else if(opcode == 111) {
                stream.ReadUnsignedShort();
            } else if(opcode == 112) {
                stream.ReadUnsignedShort();
            } else if(opcode == 113) {
                ambient = stream.ReadUnsignedByte();
            } else if(opcode == 114) {
                contrast = stream.ReadUnsignedByte();
            } else if(opcode == 115) {
                teamId = stream.ReadUnsignedByte();
            } else if(opcode == 121) {
                lendId = stream.ReadUnsignedShort();
            } else if(opcode == 122) {
                lendTemplateId = stream.ReadUnsignedShort();
            } else if(opcode == 125) {
                manWearXOffset = stream.ReadUnsignedByte() << 2;
                manWearYOffset = stream.ReadUnsignedByte() << 2;
                manWearZOffset = stream.ReadUnsignedByte() << 2;
            } else if(opcode == 126) {
                womanWearXOffset = stream.ReadUnsignedByte() << 0;
                womanWearYOffset = stream.ReadUnsignedByte() << 0;
                womanWearZOffset = stream.ReadUnsignedByte() << 0;
            } else if(opcode == 127) {
                int groupCursorOp = stream.ReadUnsignedByte();
                int groundCursor = stream.ReadUnsignedShort();
            } else if(opcode == 128) {
                int cursor2op = stream.ReadUnsignedByte();
                int cursor2 = stream.ReadUnsignedShort();
            } else if(opcode == 129) {
                int cursor2op = stream.ReadUnsignedByte();
                int cursor2 = stream.ReadUnsignedShort();
            } else if(opcode == 130) {
                int cursor2iop = stream.ReadUnsignedByte();
                int icursor2 = stream.ReadUnsignedShort();
            } else if(opcode == 132) {
                int length = stream.ReadUnsignedByte();
                int[] unknownArray2 = new int[length];
                for(int index = 0; index < length; index++)
                    unknownArray2[index] = stream.ReadUnsignedShort();
            } else if(opcode == 134) {
                stream.ReadUnsignedByte();
            } else if(opcode == 139) {
                //bindLink
                stream.ReadUnsignedShort();
            } else if(opcode == 140) {
                //bindTemplate
                stream.ReadUnsignedShort();
            } else if(opcode >= 142 && opcode < 147) {
                stream.ReadUnsignedShort();
            } else if(opcode >= 150 && opcode < 155) {
                stream.ReadUnsignedShort();
            } else if(opcode == 157) {
                //some bool
            } else if(opcode == 161) {
                //shardLink
                stream.ReadUnsignedShort();
            } else if(opcode == 162) {
                //shardTemplate
                stream.ReadUnsignedShort();
            } else if(opcode == 163) {
                //shardCombineAmount
                stream.ReadUnsignedShort();
            } else if(opcode == 164) {
                //shardName
                stream.ReadJagexString();
            } else if(opcode == 249) {
                int length = stream.ReadUnsignedByte();

                itemParams = new SortedDictionary<int, object>();

                for(int k = 0; k < length; k++) {
                    bool stringInstance = stream.ReadUnsignedByte() == 1;
                    int key = stream.ReadMedium();

                    if(stringInstance) {
                        string s = stream.ReadJagexString();
                        if(!itemParams.ContainsKey(key))
                            itemParams.Add(key, s);
                    } else {
                        int x = stream.ReadInt();
                        if(!itemParams.ContainsKey(key))
                            itemParams.Add(key, x);
                    }
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
            stream.WriteShort(inventoryModelId);

            if(name != null) {
                stream.WriteByte(2);
                stream.WriteString(name);
            }

            stream.WriteByte(4);
            stream.WriteShort(modelZoom);

            stream.WriteByte(5);
            stream.WriteShort(modelRotation1);

            stream.WriteByte(6);
            stream.WriteShort(modelRotation2);

            stream.WriteByte(7);
            stream.WriteShort(modelOffset1);

            stream.WriteByte(8);
            stream.WriteShort(modelOffset2);

            if(stackable == 1)
                stream.WriteByte(11);

            stream.WriteByte(12);
            stream.WriteInteger(value);

            stream.WriteByte(13);
            stream.WriteByte((byte) equipSlotId);

            stream.WriteByte(14);
            stream.WriteByte((byte) equipId);

            if(membersOnly)
                stream.WriteByte(16);

            stream.WriteByte(18);
            stream.WriteShort(multiStackSize);

            stream.WriteByte(23);
            stream.WriteShort(maleWearModel1);

            stream.WriteByte(24);
            stream.WriteShort(femaleWearModel1);

            stream.WriteByte(25);
            stream.WriteShort(maleWearModel2);

            stream.WriteByte(26);
            stream.WriteShort(femaleWearModel2);

            //dunno what 27 is for
            //stream.WriteByte(27);

            for(int opcode = 30; opcode < 35; opcode++) {
                if(groundOptions[opcode - 30] != null) {
                    stream.WriteByte((byte) opcode);
                    stream.WriteString(groundOptions[opcode - 30]);
                }
            }

            for(int opcode = 35; opcode < 40; opcode++) {
                if(inventoryOptions[opcode - 35] != null) {
                    stream.WriteByte((byte) opcode);
                    stream.WriteString(inventoryOptions[opcode - 35]);
                }
            }

            if(originalModelColors != null) {
                stream.WriteByte(40);
                stream.WriteByte((byte) originalModelColors.Length);
                for(int k = 0; k < originalModelColors.Length; k++) {
                    stream.WriteShort(originalModelColors[k]);
                    stream.WriteShort(modifiedModelColors[k]);
                }
            }

            if(textureColour1 != null && textureColour2 != null) {
                stream.WriteByte(41);
                stream.WriteByte((byte) textureColour1.Length);
                for(int k = 0; k < textureColour1.Length; k++) {
                    stream.WriteShort(textureColour1[k]);
                    stream.WriteShort(textureColour2[k]);
                }
            }

            /*
            stream.WriteByte(42);
            for(int k = 0; k < length; k++)
                stream.WriteByte();
                */

            if(hasNameColor) {
                stream.WriteByte(43);
                stream.WriteInteger(nameColor);
            }

            /*
            stream.WriteByte(44);
            stream.WriteShort(0); //???

            stream.WriteByte(45);
            stream.WriteShort(0); //???
            */

            if(unnoted)
                stream.WriteByte(65);

            stream.WriteByte(78);
            stream.WriteShort(colourEquip1);

            stream.WriteByte(79);
            stream.WriteShort(colourEquip2);

            /*
            stream.WriteByte(90);
            stream.WriteShort(0); //???

            stream.WriteByte(91);
            stream.WriteShort(0); //???

            stream.WriteByte(92);
            stream.WriteShort(0); //???

            stream.WriteByte(93);
            stream.WriteShort(0); //???

            stream.WriteByte(94);
            stream.WriteShort(0); //???

            stream.WriteByte(95);
            stream.WriteShort(0); //???

            stream.WriteByte(96);
            stream.WriteByte(0); //???
            */

            stream.WriteByte(97);
            stream.WriteShort(notedId);

            stream.WriteByte(98);
            stream.WriteShort(notedTemplateId);

            if(stackableIds != null) {
                for(int opcode = 100; opcode < 110; opcode++) {
                    stream.WriteShort(stackableIds[opcode - 100]);
                    stream.WriteShort(stackableAmounts[opcode - 100]);
                }
            }

            /*
            stream.WriteByte(110);
            stream.WriteShort(0);

            stream.WriteByte(111);
            stream.WriteShort(0);

            stream.WriteByte(112);
            stream.WriteShort(0);
            */

            stream.WriteByte(113);
            stream.WriteByte((byte) ambient);

            stream.WriteByte(114);
            stream.WriteByte((byte) contrast);

            stream.WriteByte(115);
            stream.WriteByte((byte) teamId);

            stream.WriteByte(121);
            stream.WriteShort(lendId);

            stream.WriteByte(122);
            stream.WriteShort(lendTemplateId);

            //Probably incorrect
            stream.WriteByte(125);
            stream.WriteByte((byte) (manWearXOffset >> 2));
            stream.WriteByte((byte) (manWearYOffset >> 2));
            stream.WriteByte((byte) (manWearZOffset >> 2));

            stream.WriteByte(126);
            stream.WriteByte((byte) womanWearXOffset);
            stream.WriteByte((byte) womanWearYOffset);
            stream.WriteByte((byte) womanWearZOffset);

            stream.WriteByte(127);
            stream.WriteByte(0); //groupCursorOp
            stream.WriteShort(0); //groundCursor

            stream.WriteByte(128);
            stream.WriteByte(0); //cursor2op
            stream.WriteShort(0); //cursor2

            stream.WriteByte(129);
            stream.WriteByte(0); //cursor2op
            stream.WriteShort(0); //cursor2

            stream.WriteByte(130);
            stream.WriteByte(0); //cursor2iop
            stream.WriteShort(0); //icursor2

            stream.WriteByte(132);
            stream.WriteByte(0);
            stream.WriteShort(0);

            stream.WriteByte(134);
            stream.WriteByte(0);

            stream.WriteByte(139);
            stream.WriteShort(0); //bindLink

            stream.WriteByte(140);
            stream.WriteShort(0); //bindTemplate

            for(int opcode = 142; opcode < 147; opcode++) {
                stream.WriteByte((byte) opcode);
                stream.WriteShort(0);
            }

            for(int opcode = 150; opcode < 155; opcode++) {
                stream.WriteByte((byte) opcode);
                stream.WriteShort(0);
            }

            //157 some bool

            stream.WriteByte(161);
            stream.WriteShort(0); //shardLink

            stream.WriteByte(162);
            stream.WriteShort(0); //shardTemplate

            stream.WriteByte(163);
            stream.WriteShort(0); //shardCombineAmount

            stream.WriteByte(164);
            stream.WriteString("shardName");

            stream.WriteByte(249);
            //some more shit but dw about it tbh

            //End of stream sir
            stream.WriteByte(0);

            return stream.Flip();
        }

        internal void SetId(int id) {
            this.id = id;
        }
    }
}