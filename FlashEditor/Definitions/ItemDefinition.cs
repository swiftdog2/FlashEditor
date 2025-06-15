using static FlashEditor.utils.DebugUtil;
using System;
using System.Collections.Generic;
using System.Text;

namespace FlashEditor {
    internal class ItemDefinition : ICloneable {
        public string name;
        public int id;
        public bool[] decoded = new bool[256];
        public string[] groundOptions = new[] { null, null, "take", null, null };
        public string[] inventoryOptions = new[] { null, null, null, null, "drop" };

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

        private static readonly StringBuilder SharedBuilder = new StringBuilder();

        public SortedDictionary<int, object> itemParams;

        public ItemDefinition Clone() { return (ItemDefinition) MemberwiseClone(); }
        object ICloneable.Clone() { return Clone(); }

        public int GetId() {
            return id;
        }

        /// <summary>
        /// Overrides defaults from the stream
        /// </summary>
        /// <param name="stream"></param>
        public static ItemDefinition Decode(JagStream stream) {
            int total = 0;

            SharedBuilder.Clear();
            ItemDefinition def = new ItemDefinition();

            if(stream != null) {
                while(true) {
                    //Read unsigned byte
                    int opcode = stream.ReadUnsignedByte();

                    //Flag that the default value was overridden
                    def.decoded[opcode] = true;

                    if(opcode == 0)
                        break;

                    def.Decode(stream, opcode);

                    //Shouldn't be necessary, but there are no more than 256 opcodes anyway so no harm
                    if(total++ > 256)
                        break;
                }

                for(int k = 0; k < def.decoded.Length; k++) {
                    if(def.decoded[k])
                        SharedBuilder.Append(k + " ");
                }
            }

            Debug((def.name ?? "null") + " (stream len " + stream.Length + "), OPCODEs: " + SharedBuilder.ToString(), LOG_DETAIL.INSANE);
            return def;
        }

        /// <summary>
        /// Overrides a specific property corresponding to opcode
        /// </summary>
        /// <param name="stream">The stream to read from</param>
        /// <param name="opcode">The opcode value signalling which type to read</param>
        private void Decode(JagStream stream, int opcode)
        {
            switch (opcode)
            {
                case 1:
                    inventoryModelId = stream.ReadUnsignedShort();
                    break;

                case 2:
                    name = stream.ReadJagexString();
                    break;

                case 4:
                    modelZoom = stream.ReadUnsignedShort();
                    break;

                case 5:
                    modelRotation1 = stream.ReadUnsignedShort();
                    break;

                case 6:
                    modelRotation2 = stream.ReadUnsignedShort();
                    break;

                case 7:
                    modelOffset1 = stream.ReadUnsignedShort();
                    if (modelOffset1 > 32767) modelOffset1 -= 65536;
                    modelOffset1 <<= 0;
                    break;

                case 8:
                    modelOffset2 = stream.ReadUnsignedShort();
                    if (modelOffset2 > 32767) modelOffset2 -= 65536;
                    modelOffset2 <<= 0;
                    break;

                case 11:
                    stackable = 1;
                    break;

                case 12:
                    value = stream.ReadInt();
                    break;

                case 13:
                    equipSlotId = stream.ReadUnsignedByte();
                    break;

                case 14:
                    equipId = stream.ReadUnsignedByte();
                    break;

                case 16:
                    membersOnly = true;
                    break;

                case 18:
                    multiStackSize = stream.ReadUnsignedShort();
                    break;

                case 23:
                    maleWearModel1 = stream.ReadUnsignedShort();
                    break;

                case 24:
                    femaleWearModel1 = stream.ReadUnsignedShort();
                    break;

                case 25:
                    maleWearModel2 = stream.ReadUnsignedShort();
                    break;

                case 26:
                    femaleWearModel2 = stream.ReadUnsignedShort();
                    break;

                case 27:                       // unused / legacy
                    stream.ReadUnsignedByte();
                    break;

                // ────────────────────────────────
                // RANGES THAT WERE IN THE DEFAULT
                // ────────────────────────────────

                // 30–34 : ground-click options
                case int n when n >= 30 && n < 35:
                    groundOptions[n - 30] = stream.ReadJagexString();
                    if ("Hidden".Equals(groundOptions[n - 30], StringComparison.InvariantCultureIgnoreCase))
                        groundOptions[n - 30] = null;
                    break;

                // 35–39 : inventory-click options
                case int n when n >= 35 && n < 40:
                    inventoryOptions[n - 35] = stream.ReadJagexString();
                    break;

                case 40:
                    {
                        int len = stream.ReadUnsignedByte();
                        originalModelColors = new short[len];
                        modifiedModelColors = new short[len];
                        for (int i = 0; i < len; i++)
                        {
                            originalModelColors[i] = (short)stream.ReadUnsignedShort();
                            modifiedModelColors[i] = (short)stream.ReadUnsignedShort();
                        }
                        break;
                    }

                case 41:
                    {
                        int len = stream.ReadUnsignedByte();
                        textureColour1 = new short[len];
                        textureColour2 = new short[len];
                        for (int i = 0; i < len; i++)
                        {
                            textureColour1[i] = (short)stream.ReadUnsignedShort();
                            textureColour2[i] = (short)stream.ReadUnsignedShort();
                        }
                        break;
                    }

                case 42:
                    {
                        int len = stream.ReadUnsignedByte();
                        for (int i = 0; i < len; i++) stream.ReadByte();
                        break;
                    }

                case 43:
                    nameColor = stream.ReadInt();
                    hasNameColor = true;
                    break;

                case 44:
                case 45:
                    stream.ReadUnsignedShort();        // placeholder / “more crap”
                    break;

                case 65:
                    unnoted = true;
                    break;

                case 78:
                    colourEquip1 = stream.ReadUnsignedShort();
                    break;

                case 79:
                    colourEquip2 = stream.ReadUnsignedShort();
                    break;

                case 90:
                case 91:
                case 92:
                case 93:
                case 94:
                case 95:
                    stream.ReadUnsignedShort();        // unused NPC/obj links
                    break;

                case 96:
                    stream.ReadUnsignedByte();         // some flag
                    break;

                case 97:
                    notedId = stream.ReadUnsignedShort();
                    break;

                case 98:
                    notedTemplateId = stream.ReadUnsignedShort();
                    break;

                // 100–109 : stackable variants
                case int n when n >= 100 && n < 110:
                    if (stackableIds == null)
                    {
                        stackableIds = new int[10];
                        stackableAmounts = new int[10];
                    }
                    stackableIds[n - 100] = stream.ReadUnsignedShort();
                    stackableAmounts[n - 100] = stream.ReadUnsignedShort();
                    break;

                case 110:
                case 111:
                case 112:
                    stream.ReadUnsignedShort();        // model resize XYZ
                    break;

                case 113:
                    ambient = stream.ReadUnsignedByte();
                    break;

                case 114:
                    contrast = stream.ReadUnsignedByte();
                    break;

                case 115:
                    teamId = stream.ReadUnsignedByte();
                    break;

                case 121:
                    lendId = stream.ReadUnsignedShort();
                    break;

                case 122:
                    lendTemplateId = stream.ReadUnsignedShort();
                    break;

                case 125:
                    manWearXOffset = stream.ReadUnsignedByte() << 2;
                    manWearYOffset = stream.ReadUnsignedByte() << 2;
                    manWearZOffset = stream.ReadUnsignedByte() << 2;
                    break;

                case 126:
                    womanWearXOffset = stream.ReadUnsignedByte();
                    womanWearYOffset = stream.ReadUnsignedByte();
                    womanWearZOffset = stream.ReadUnsignedByte();
                    break;

                case 127:
                case 128:
                case 129:
                case 130:
                    stream.ReadUnsignedByte();  // cursor op
                    stream.ReadUnsignedShort(); // cursor id
                    break;

                case 132:
                    {
                        int len = stream.ReadUnsignedByte();
                        int[] tmp = new int[len];
                        for (int i = 0; i < len; i++) tmp[i] = stream.ReadUnsignedShort();
                        break;
                    }

                case 134:
                    stream.ReadUnsignedByte();  // unknown flag
                    break;

                case 139:
                case 140:
                    stream.ReadUnsignedShort(); // bindLink / bindTemplate
                    break;

                // 142–146 : quest-point/item-set links
                case int n when n >= 142 && n < 147:
                    stream.ReadUnsignedShort();
                    break;

                // 150–154 : random placeholders
                case int n when n >= 150 && n < 155:
                    stream.ReadUnsignedShort();
                    break;

                case 157:
                    /* some boolean flag */
                    break;

                case 161:
                case 162:
                case 163:
                    stream.ReadUnsignedShort(); // shard linking
                    break;

                case 164:
                    stream.ReadJagexString();   // shard name
                    break;

                case 249:                      // params map
                    {
                        int len = stream.ReadUnsignedByte();
                        itemParams = new SortedDictionary<int, object>();

                        for (int i = 0; i < len; i++)
                        {
                            bool isString = stream.ReadUnsignedByte() == 1;
                            int key = stream.ReadMedium();

                            if (isString)
                            {
                                string s = stream.ReadJagexString();
                                if (!itemParams.ContainsKey(key)) itemParams.Add(key, s);
                            }
                            else
                            {
                                int val = stream.ReadInt();
                                if (!itemParams.ContainsKey(key)) itemParams.Add(key, val);
                            }
                        }
                        break;
                    }

                default:
                    // Unknown opcode – swallow silently or log/debug as needed
                    break;
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
            //stream.WriteByte2(27);

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
            stream.WriteByte2(42);
            for(int k = 0; k < length; k++)
                stream.WriteByte2();
                */

            if(hasNameColor) {
                stream.WriteByte(43);
                stream.WriteInteger(nameColor);
            }

            /*
            stream.WriteByte2(44);
            stream.WriteShort(0); //???

            stream.WriteByte2(45);
            stream.WriteShort(0); //???
            */

            if(unnoted)
                stream.WriteByte(65);

            stream.WriteByte(78);
            stream.WriteShort(colourEquip1);

            stream.WriteByte(79);
            stream.WriteShort(colourEquip2);

            /*
            stream.WriteByte2(90);
            stream.WriteShort(0); //???

            stream.WriteByte2(91);
            stream.WriteShort(0); //???

            stream.WriteByte2(92);
            stream.WriteShort(0); //???

            stream.WriteByte2(93);
            stream.WriteShort(0); //???

            stream.WriteByte2(94);
            stream.WriteShort(0); //???

            stream.WriteByte2(95);
            stream.WriteShort(0); //???

            stream.WriteByte2(96);
            stream.WriteByte2(0); //???
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
            stream.WriteByte2(110);
            stream.WriteShort(0);

            stream.WriteByte2(111);
            stream.WriteShort(0);

            stream.WriteByte2(112);
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