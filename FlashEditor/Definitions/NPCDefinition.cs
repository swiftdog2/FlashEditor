using System;
using System.Collections.Generic;

namespace FlashEditor
{
    internal class NPCDefinition : ICloneable, IDefinition
    {
        sbyte primaryShadowModifier = -33;
        byte respawnDirection = 7;
        sbyte secondaryShadowModifier = -113;
        sbyte walkMask;

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

        public int[] unknownOptions = { -1, -1, -1, -1, -1, -1 };


        private SortedDictionary<int, object> config;
        private sbyte someField112;
        private int someField1101;
        private int someField1090;
        private int anInt1101;
        private int anInt1090;
        private sbyte anInt1104;

        /// <summary>
        /// Constructs a new item definition from the stream data
        /// </summary>
        /// <param name="stream">The stream containing the encoded item data</param>
        public NPCDefinition(JagStream stream)
        {
            Decode(stream);
        }

        /// <summary>
        /// Overrides defaults from the stream
        /// </summary>
        /// <param name="stream"></param>
        public void Decode(JagStream stream)
        {
            if (stream != null)
            {
                while (true)
                {
                    int opcode = stream.ReadUnsignedByte();

                    if (opcode == 0 || opcode == 255)
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
        private void Decode(JagStream stream, int opcode)
        {
            switch (opcode)
            {
                case 1:
                    {
                        int length = stream.ReadByte();
                        modelIds = new int[length];
                        for (int k = 0; k < length; k++)
                        {
                            int id = stream.ReadShort();
                            modelIds[k] = (id == 65535 ? -1 : id);
                        }
                        break;
                    }

                case 2:
                    name = stream.ReadJagexString();
                    break;

                case 12:
                    size = stream.ReadByte();
                    break;

                // options 30–34
                case 30:
                case 31:
                case 32:
                case 33:
                case 34:
                    {
                        int idx = opcode - 30;
                        options[idx] = stream.ReadJagexString();
                        if (string.Equals(options[idx], "Hidden", StringComparison.Ordinal))
                            options[idx] = null;
                        break;
                    }

                case 40:
                    {
                        int length = stream.ReadByte();
                        recolorSrc = new int[length];
                        recolorDst = new int[length];
                        for (int i = 0; i < length; i++)
                        {
                            recolorSrc[i] = stream.ReadShort();
                            recolorDst[i] = stream.ReadShort();
                        }
                        break;
                    }

                case 41:
                    {
                        int length = stream.ReadByte();
                        retextureSrc = new int[length];
                        retextureDst = new int[length];
                        for (int i = 0; i < length; i++)
                        {
                            retextureSrc[i] = stream.ReadShort();
                            retextureDst[i] = stream.ReadShort();
                        }
                        break;
                    }

                case 42:
                    {
                        int length = stream.ReadByte();
                        recolorDstPalette = new byte[length];
                        for (int i = 0; i < length; i++)
                            recolorDstPalette[i] = (byte)stream.ReadByte();
                        break;
                    }

                case 44:
                    op44 = stream.ReadShort();
                    break;

                case 45:
                    op45 = stream.ReadShort();
                    break;

                case 60:
                    {
                        int length = stream.ReadByte();
                        dialogueModels = new int[length];
                        for (int i = 0; i < length; i++)
                            dialogueModels[i] = stream.ReadShort();
                        break;
                    }

                case 93:
                    drawMinimapDot = false;
                    break;

                case 95:
                    level = stream.ReadShort();
                    break;

                case 97:
                    scaleXY = stream.ReadShort();
                    break;

                case 98:
                    scaleZ = stream.ReadShort();
                    break;

                case 99:
                    hasRenderPriority = true;
                    break;

                case 100:
                    ambient = stream.ReadByte();
                    break;

                case 101:
                    contrast = stream.ReadByte();
                    break;

                case 102:
                    headIcon = stream.ReadShort();
                    break;

                case 103:
                    rotation = stream.ReadShort();
                    break;

                case 106:
                case 118:
                    {
                        varbit = stream.ReadShort();
                        if (varbit == 65535) varbit = -1;

                        varp = stream.ReadShort();
                        if (varp == 65535) varp = -1;

                        int last = -1;
                        if (opcode == 118)
                        {
                            last = stream.ReadShort();
                            if (last == 65535) last = -1;
                        }

                        int count = stream.ReadByte();
                        morphs = new int[count + 2];
                        for (int i = 0; i <= count; i++)
                        {
                            int m = stream.ReadShort();
                            morphs[i] = (m == 65535 ? -1 : m);
                        }
                        morphs[count + 1] = last;
                        break;
                    }

                case 107:
                    clickable = false;
                    break;

                case 109:
                    slowWalk = false;
                    break;

                case 111:
                    animateIdle = false;
                    break;

                case 112:
                    anInt1104 = (sbyte)stream.ReadByte();
                    break;

                case 113:
                    primaryShadowColour = (short)stream.ReadShort();
                    secondaryShadowColour = (short)stream.ReadShort();
                    break;

                case 114:
                    primaryShadowModifier = (sbyte)stream.ReadByte();
                    secondaryShadowModifier = (sbyte)stream.ReadByte();
                    break;

                case 119:
                    walkMask = (sbyte)stream.ReadByte();
                    break;

                // Translations (restored original logic)
                case 121:
                    {
                        translations = new int[modelIds == null ? 0 : modelIds.Length][];
                        int length = stream.ReadByte();
                        for (int i_62_ = 0; i_62_ < length; i_62_++)
                        {
                            int index = stream.ReadByte();
                            int[] translations = (this.translations[index] = new int[3]);
                            translations[0] = stream.ReadByte();
                            translations[1] = stream.ReadByte();
                            translations[2] = stream.ReadByte();
                        }
                        break;
                    }

                case 122:
                    hitbarSprite = stream.ReadShort();
                    break;

                case 123:
                    height = stream.ReadShort();
                    break;

                case 125:
                    respawnDirection = (byte)stream.ReadByte();
                    break;

                case 127:
                    renderTypeID = stream.ReadShort();
                    break;

                case 128:
                    movementType = stream.ReadByte();
                    break;

                case 134:
                    {
                        idleSound = stream.ReadShort(); if (idleSound == 65535) idleSound = -1;
                        crawlSound = stream.ReadShort(); if (crawlSound == 65535) crawlSound = -1;
                        walkSound = stream.ReadShort(); if (walkSound == 65535) walkSound = -1;
                        runSound = stream.ReadShort(); if (runSound == 65535) runSound = -1;
                        soundDistance = stream.ReadByte();
                        break;
                    }

                case 135:
                    primaryCursorOp = stream.ReadByte();
                    primaryCursor = stream.ReadShort();
                    break;

                case 136:
                    secondaryCursorOp = stream.ReadByte();
                    secondaryCursor = stream.ReadShort();
                    break;

                case 137:
                    attackOpCursor = stream.ReadShort();
                    break;

                case 138:
                    armyIcon = stream.ReadShort();
                    break;

                case 139:
                    spriteId = stream.ReadShort();
                    break;

                case 140:
                    ambientSoundVolume = stream.ReadByte();
                    break;

                case 141:
                    visiblePriority = true;
                    break;

                case 142:
                    mapIcon = stream.ReadShort();
                    break;

                case 143:
                    invisiblePriority = true;
                    break;

                case 150:
                case 151:
                case 152:
                case 153:
                case 154:
                    {
                        int idx = opcode - 150;
                        options[idx] = stream.ReadJagexString();
                        if (string.Equals(options[idx], "Hidden", StringComparison.Ordinal))
                            options[idx] = null;
                        break;
                    }

                case 155:
                    hue = stream.ReadByte();
                    saturation = stream.ReadByte();
                    lightness = stream.ReadByte();
                    opacity = stream.ReadByte();
                    break;

                case 158:
                    mainOptionIndex = 1;
                    break;

                case 159:
                    mainOptionIndex = 0;
                    break;

                case 160:
                    {
                        int length = stream.ReadByte();
                        campaigns = new int[length];
                        for (int i = 0; i < length; i++)
                            campaigns[i] = stream.ReadShort();
                        break;
                    }

                case 162:
                    anInt1101 = stream.ReadShort();
                    anInt1090 = stream.ReadShort();
                    break;

                case 163:
                    anInt864 = stream.ReadByte();
                    break;

                case 164:
                    anInt848 = stream.ReadShort();
                    anInt837 = stream.ReadShort();
                    break;

                case 165:
                    anInt847 = stream.ReadByte();
                    break;

                case 168:
                    anInt828 = stream.ReadByte();
                    break;

                case 170:
                case 171:
                case 172:
                case 173:
                case 174:
                case 175:
                    unknownOptions[opcode - 170] = stream.ReadShort();
                    break;

                case 179:
                    {
                        unknownByte1 = stream.ReadByte();
                        unknownByte2 = stream.ReadByte();
                        unknownByte3 = stream.ReadByte();
                        unknownByte4 = stream.ReadByte();
                        unknownByte5 = stream.ReadByte();
                        unknownByte6 = stream.ReadByte();
                        break;
                    }

                case 249:
                    {
                        int cfgLen = stream.ReadByte();
                        if (config == null)
                            config = new SortedDictionary<int, object>();
                        for (int k = 0; k < cfgLen; k++)
                        {
                            bool isString = stream.ReadByte() == 1;
                            int key = stream.ReadMedium();
                            object val = isString
                                ? (object)stream.ReadJagexString()
                                : stream.ReadInt();
                            config[key] = val;
                        }
                        break;
                    }

                default:
                    // ignore unknown
                    break;
            }
        }



        /// <summary>
        /// Overrides a specific property corresponding to opcode
        /// </summary>
        /// <param name="stream">The stream to read from</param>
        /// <param name="opcode">The opcode value signalling which type to read</param>
        // --- Encode method matching above Decode ---
        public JagStream Encode()
        {
            var stream = new JagStream();

            // 1: modelIds
            stream.WriteByte(1);
            stream.WriteByte((byte)modelIds.Length);
            foreach (var id in modelIds)
                stream.WriteShort(id == -1 ? 0xFFFF : id);

            // 2: name
            stream.WriteByte(2);
            stream.WriteJagexString(name);

            // 12: size
            stream.WriteByte(12);
            stream.WriteByte((byte)size);

            // 30–34: options
            for (int opc = 30; opc <= 34; opc++)
            {
                stream.WriteByte((byte)opc);
                stream.WriteJagexString(options[opc - 30] ?? "Hidden");
            }

            // 40: recolor
            stream.WriteByte(40);
            stream.WriteByte((byte)recolorSrc.Length);
            for (int i = 0; i < recolorSrc.Length; i++)
            {
                stream.WriteShort(recolorSrc[i]);
                stream.WriteShort(recolorDst[i]);
            }

            // 41: retexture
            stream.WriteByte(41);
            stream.WriteByte((byte)retextureSrc.Length);
            for (int i = 0; i < retextureSrc.Length; i++)
            {
                stream.WriteShort(retextureSrc[i]);
                stream.WriteShort(retextureDst[i]);
            }

            // 42: palette
            stream.WriteByte(42);
            stream.WriteByte((byte)recolorDstPalette.Length);
            foreach (var b in recolorDstPalette)
                stream.WriteByte(b);

            // 44,45: op44/op45
            stream.WriteByte(44); stream.WriteShort(op44);
            stream.WriteByte(45); stream.WriteShort(op45);

            // 60: dialogueModels
            stream.WriteByte(60);
            stream.WriteByte((byte)dialogueModels.Length);
            foreach (var m in dialogueModels)
                stream.WriteShort(m);

            // 93: drawMinimapDot=false
            if (!drawMinimapDot) stream.WriteByte(93);

            // 95,97,98
            stream.WriteByte(95); stream.WriteShort(level);
            stream.WriteByte(97); stream.WriteShort(scaleXY);
            stream.WriteByte(98); stream.WriteShort(scaleZ);

            // 99
            if (hasRenderPriority) stream.WriteByte(99);

            // 100,101
            stream.WriteByte(100); stream.WriteByte((byte)ambient);
            stream.WriteByte(101); stream.WriteByte((byte)contrast);

            // 102,103
            stream.WriteByte(102); stream.WriteShort(headIcon);
            stream.WriteByte(103); stream.WriteShort(rotation);

            // 106/118: morphs
            int count = morphs.Length - 2;
            bool hasLast = morphs[count + 1] != -1;
            if (!hasLast)
            {
                stream.WriteByte(106);
                stream.WriteShort(varbit == -1 ? 0xFFFF : varbit);
                stream.WriteShort(varp == -1 ? 0xFFFF : varp);
            }
            else
            {
                stream.WriteByte(118);
                stream.WriteShort(varbit == -1 ? 0xFFFF : varbit);
                stream.WriteShort(varp == -1 ? 0xFFFF : varp);
                stream.WriteShort(morphs[count + 1] == -1 ? 0xFFFF : morphs[count + 1]);
            }
            stream.WriteByte((byte)count);
            for (int i = 0; i <= count; i++)
                stream.WriteShort(morphs[i] == -1 ? 0xFFFF : morphs[i]);

            // 107,109,111
            if (!clickable) stream.WriteByte(107);
            if (!slowWalk) stream.WriteByte(109);
            if (!animateIdle) stream.WriteByte(111);

            // 112
            stream.WriteByte(112);
            stream.WriteSignedByte(anInt1104);

            // 113,114,119
            stream.WriteByte(113);
            stream.WriteShort(primaryShadowColour);
            stream.WriteShort(secondaryShadowColour);
            stream.WriteByte(114);
            stream.WriteSignedByte(primaryShadowModifier);
            stream.WriteSignedByte(secondaryShadowModifier);
            stream.WriteByte(119);
            stream.WriteSignedByte(walkMask);

            // 121: translations
            stream.WriteByte(121);
            int tlen = translations == null ? 0 : translations.Length;
            stream.WriteByte((byte)tlen);
            for (int idx = 0; idx < tlen; idx++)
            {
                var t = translations[idx];
                if (t == null) continue;
                stream.WriteByte((byte)idx);
                stream.WriteByte((byte)t[0]);
                stream.WriteByte((byte)t[1]);
                stream.WriteByte((byte)t[2]);
            }

            // 122–128
            stream.WriteByte(122); stream.WriteShort(hitbarSprite);
            stream.WriteByte(123); stream.WriteShort(height);
            stream.WriteByte(125); stream.WriteByte(respawnDirection);
            stream.WriteByte(127); stream.WriteShort(renderTypeID);
            stream.WriteByte(128); stream.WriteByte((byte)movementType);

            // 134: sounds
            stream.WriteByte(134);
            stream.WriteShort(idleSound == -1 ? 0xFFFF : idleSound);
            stream.WriteShort(crawlSound == -1 ? 0xFFFF : crawlSound);
            stream.WriteShort(walkSound == -1 ? 0xFFFF : walkSound);
            stream.WriteShort(runSound == -1 ? 0xFFFF : runSound);
            stream.WriteByte((byte)soundDistance);

            // 135–143
            stream.WriteByte(135); stream.WriteByte((byte)primaryCursorOp); stream.WriteShort(primaryCursor);
            stream.WriteByte(136); stream.WriteByte((byte)secondaryCursorOp); stream.WriteShort(secondaryCursor);
            stream.WriteByte(137); stream.WriteShort(attackOpCursor);
            stream.WriteByte(138); stream.WriteShort(armyIcon);
            stream.WriteByte(139); stream.WriteShort(spriteId);
            stream.WriteByte(140); stream.WriteByte((byte)ambientSoundVolume);
            if (visiblePriority) stream.WriteByte(141);
            stream.WriteByte(142); stream.WriteShort(mapIcon);
            if (invisiblePriority) stream.WriteByte(143);

            // 150–154: options
            for (int opc = 150; opc <= 154; opc++)
            {
                stream.WriteByte((byte)opc);
                stream.WriteJagexString(options[opc - 150] ?? "Hidden");
            }

            // 155
            stream.WriteByte(155);
            stream.WriteByte((byte)hue);
            stream.WriteByte((byte)saturation);
            stream.WriteByte((byte)lightness);
            stream.WriteByte((byte)opacity);

            // 158/159
            if (mainOptionIndex == 1) stream.WriteByte(158);
            if (mainOptionIndex == 0) stream.WriteByte(159);

            // 160: campaigns
            stream.WriteByte(160);
            stream.WriteByte((byte)campaigns.Length);
            foreach (var c in campaigns) stream.WriteShort(c);

            // 162: anInt1101/anInt1090
            stream.WriteByte(162);
            stream.WriteShort(anInt1101);
            stream.WriteShort(anInt1090);

            // 163–168
            stream.WriteByte(163); stream.WriteByte((byte)anInt864);
            stream.WriteByte(164); stream.WriteShort(anInt848); stream.WriteShort(anInt837);
            stream.WriteByte(165); stream.WriteByte((byte)anInt847);
            stream.WriteByte(168); stream.WriteByte((byte)anInt828);

            // 170–175: unknownOptions
            for (int opc = 170; opc <= 175; opc++)
            {
                int val = unknownOptions[opc - 170];
                if (val != -1)
                {
                    stream.WriteByte((byte)opc);
                    stream.WriteShort(val);
                }
            }

            // 179
            stream.WriteByte(179);
            stream.WriteByte((byte)unknownByte1);
            stream.WriteByte((byte)unknownByte2);
            stream.WriteByte((byte)unknownByte3);
            stream.WriteByte((byte)unknownByte4);
            stream.WriteByte((byte)unknownByte5);
            stream.WriteByte((byte)unknownByte6);

            // 249: config
            if (config != null && config.Count > 0)
            {
                stream.WriteByte(249);
                stream.WriteByte((byte)config.Count);
                foreach (var kv in config)
                {
                    bool isStr = kv.Value is string;
                    stream.WriteByte((byte)(isStr ? 1 : 0));
                    stream.WriteMedium(kv.Key);
                    if (isStr) stream.WriteJagexString((string)kv.Value);
                    else stream.WriteInteger((int)kv.Value);
                }
            }

            // terminator
            stream.WriteByte(0);
            return stream.Flip();
        }

        internal void SetId(int id)
        {
            this.id = id;
        }

        /// <summary>
        /// Creates a shallow copy of this <see cref="NPCDefinition"/>.
        /// </summary>
        public NPCDefinition Clone() => (NPCDefinition)MemberwiseClone();
        object ICloneable.Clone() => Clone();
    }
}