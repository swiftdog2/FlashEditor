using static FlashEditor.utils.DebugUtil;
using System;
using System.Collections.Generic;
using System.Text;

namespace FlashEditor.Definitions
{
    /// <summary>
    /// Represents a single “loc” / world-object definition.
    /// </summary>
    public class ObjectDefinition : ICloneable, IDefinition
    {
        /*───────────────────────────────────────────*
         *  ▌  Static / shared helpers              ▐
         *───────────────────────────────────────────*/
        private static readonly StringBuilder SharedBuilder = new StringBuilder();

        /*───────────────────────────────────────────*
         *  ▌  Public fields (RS cache values)      ▐
         *───────────────────────────────────────────*/
        public int id;

        // ─── Name & menu strings ─────────────────────────
        public string name;
        public string[] actions = new string[5];        // op-codes 30-34
        public string[] menuOps = new string[5];        // op-codes 150-154

        // ─── Geometry / render flags ─────────────────────
        public byte sizeX = 1;          // op-code 14
        public byte sizeY = 1;          // op-code 15
        public bool walkable = true;
        public bool isClipped = false;
        public int modelBrightness, modelContrast;

        // ───── misc metadata ──────────────────────────
        public byte category;          // opcode 19

        // ─── Model groups (op-codes 1 / 5) ───────────────
        public bool usesOpcode5;                        // true → encode with 5, else 1
        public sbyte[] modelTypes;                // per group
        public ushort[][] modelIds;                  // per group

        // ─── Animation (op-code 24) ──────────────────────
        public int animationId = -1;

        // ─── Light / ambience (65-67) ────────────────────
        public int ambience;         // 65
        public int contrast;         // 66
        public int scaleZ;         // 67

        // ─── Morph varbits / varps (77 / 92) ─────────────
        public int morphVarbit = -1;
        public int morphVarp = -1;
        public int[] morphIds;

        // ─── Sound / ambience (78 / 79) ──────────────────
        public int ambientSoundId = -1;
        public int ambientSoundLoops;
        public int[] extraSounds;

        // ─── Colour / texture swaps (40 / 41) ────────────
        public short[] recolSrc, recolDst;
        public short[] retexSrc, retexDst;

        // ─── Minimap icons (op-code 160) ────────────────
        public ushort[] minimapIcons;

        // ─── Params (op-code 249) ───────────────────────
        public SortedDictionary<int, object> parameters;
        private byte clipType;
        private int obstructsGround;
        private bool randomAnimStart;
        private sbyte[] texturePriorities;
        private bool flipped;
        private bool castsShadow;
        private int scaleX;
        private int scaleY;
        private int mapSceneId;
        private byte minimapForceClip;
        private int offsetX;
        private int offsetY;
        private int offsetZ;
        private bool obstructsWheelchair;
        private bool isSolid;
        private byte supportItems;
        private byte soundRadius;
        private byte soundVolume;
        private bool playsOnLoop;
        private bool isDoorway;
        private bool blocksRanged;
        private bool nonFlatShading;
        private bool occludes;
        private int opcode;
        private int aByteCamType;
        private int camOverride;
        private int camPitch;
        private int camYaw;
        private int camRoll;
        private int camZoom;
        private int lightXOffset;
        private int lightYOffset;
        private int lightZOffset;
        private int cursorSprite;
        private int mapFunction;
        private int mapAreaId;
        private int objClipStart;
        private int objClipEnd;
        private bool breakShield;
        private byte minimapRenderPri;
        private bool alwaysDraw;
        private int actionCount;
        private bool lowPriorityRender;
        private readonly Dictionary<int, byte[]> _rawUnknown = new();

        // ─── Misc bookkeeping ───────────────────────────
        public readonly bool[] decoded = new bool[256];  // opcode hit-map

        /*───────────────────────────────────────────*
         *  ▌  Clone support                        ▐
         *───────────────────────────────────────────*/
        public ObjectDefinition Clone() => (ObjectDefinition)MemberwiseClone();
        object ICloneable.Clone() => Clone();

        /*───────────────────────────────────────────*
         *  ▌  Public decode entry-point            ▐
         *───────────────────────────────────────────*/
        public void Decode(JagStream stream)
        {
            int safeGuard = 0;
            SharedBuilder.Clear();

            while (true)
            {
                int op = stream.ReadUnsignedByte();
                decoded[op] = true;

                if (op == 0) break;          // terminator
                Decode(stream, op);

                if (++safeGuard > 256)
                    throw new InvalidOperationException("Opcode overflow while decoding ObjectDefinition.");
            }

            for (int i = 0; i < decoded.Length; i++)
                if (decoded[i]) SharedBuilder.Append(i).Append(' ');

            Debug($"ObjectDef {id} ({name ?? "null"}) OPCODES: {SharedBuilder}", LOG_DETAIL.NONE);
        }

        public static ObjectDefinition DecodeFromStream(JagStream stream)
        {
            var def = new ObjectDefinition();
            def.Decode(stream);
            return def;
        }


        /*───────────────────────────────────────────*
         *  ▌  Private per-opcode handler           ▐
         *───────────────────────────────────────────*/
        private void Decode(JagStream buf, int op)
        {
            switch (op)
            {
                /*──────── 1 / 5 : model lists ────────*/
                case 1:
                case 5:
                    {
                        usesOpcode5 = (op == 5);
                        int groupCt = buf.ReadUnsignedByte();
                        modelTypes = new sbyte[groupCt];
                        modelIds = new ushort[groupCt][];

                        for (int g = 0; g < groupCt; g++)
                        {
                            modelTypes[g] = (sbyte)buf.ReadSignedByte();
                            int modelCt = buf.ReadUnsignedByte();
                            var ids = new ushort[modelCt];
                            for (int m = 0; m < modelCt; m++) ids[m] = (ushort)buf.ReadUnsignedShort();
                            modelIds[g] = ids;
                        }
                        break;
                    }

                /*──────── scalar flags ────────*/
                case 2: name = buf.ReadJagexString(); break;
                case 14: sizeX = buf.ReadUnsignedByte(); break;
                case 15: sizeY = buf.ReadUnsignedByte(); break;

                case 17:
                case 18: walkable = false; break;

                // category/id-grouping
                case 19:                           
                    category = buf.ReadUnsignedByte();
                    return;

                /* ─────────────── map-scene & clip flags ─────────────── */
                case 21: clipType = buf.ReadUnsignedByte(); return;          // 1 byte
                case 22: isClipped = true; return;             // flag only
                case 23: obstructsGround = 1; return;             // Solidity flag
                case 24: animationId = buf.ReadUnsignedShort(); return;      // 2 bytes
                case 27: randomAnimStart = true; return;             // flag only
                case 28: modelBrightness = buf.ReadUnsignedByte() << 2; break;
                case 29: modelContrast = buf.ReadSignedByte(); break;

                /*──────── action strings 30-34 ────────*/
                case int a when (a >= 30 && a < 35):
                    actions[a - 30] = buf.ReadJagexString();
                    break;

                /*──────── recolour 40 ────────*/
                case 40:
                    {
                        int n = buf.ReadUnsignedByte();
                        recolSrc = new short[n];
                        recolDst = new short[n];
                        for (int i = 0; i < n; i++)
                        {
                            recolSrc[i] = (short)buf.ReadUnsignedShort();
                            recolDst[i] = (short)buf.ReadUnsignedShort();
                        }
                        break;
                    }

                /*──────── retexture 41 ────────*/
                case 41:
                    {
                        int n = buf.ReadUnsignedByte();
                        retexSrc = new short[n];
                        retexDst = new short[n];
                        for (int i = 0; i < n; i++)
                        {
                            retexSrc[i] = (short)buf.ReadUnsignedShort();
                            retexDst[i] = (short)buf.ReadUnsignedShort();
                        }
                        break;
                    }

                /* ─────────────── texture-priority table (byte[]) ─────── */
                case 42:
                    {
                        int n = buf.ReadUnsignedByte();
                        texturePriorities = new sbyte[n];
                        for (int i = 0; i < n; i++)
                            texturePriorities[i] = (sbyte)buf.ReadSignedByte();
                        return;
                    }

                /* ─────────────── render-side flags ─────────────── */
                case 62: flipped = true; return;        // mirror on X
                case 64: castsShadow = false; return;        // disable shadow
                case 65: scaleX = buf.ReadUnsignedShort(); return;    // 2 bytes
                case 66: scaleY = buf.ReadUnsignedShort(); return;    // 2 bytes
                case 67: scaleZ = buf.ReadUnsignedShort(); return;    // 2 bytes
                case 68: mapSceneId = buf.ReadUnsignedShort(); return;    // 2 bytes
                case 69: minimapForceClip = buf.ReadUnsignedByte(); return;  // 1 byte

                /* signed short offsets (<< 2 already applied in Java) */
                case 70: offsetX = buf.ReadShort() << 2; return;
                case 71: offsetY = buf.ReadShort() << 2; return;
                case 72: offsetZ = buf.ReadShort() << 2; return;

                case 73: obstructsWheelchair = true; return;
                case 74: isSolid = true; return;
                case 75: supportItems = buf.ReadUnsignedByte(); return;

                /*──────── morph (77 / 92) ────────*/
                case 77:
                case 92:
                    {
                        morphVarbit = SmartOrMinus1(buf);
                        morphVarp = SmartOrMinus1(buf);
                        int defaultId = (op == 92) ? SmartOrMinus1(buf) : -1;

                        int ct = buf.ReadUnsignedByte();
                        morphIds = new int[ct + 2];
                        for (int i = 0; i <= ct; i++) morphIds[i] = SmartOrMinus1(buf);
                        morphIds[ct + 1] = defaultId;
                        break;
                    }

                /*──────── sounds 78 / 79 ────────*/
                case 78:
                    {
                        ambientSoundId = buf.ReadUnsignedShort();
                        ambientSoundLoops = buf.ReadUnsignedByte();
                        break;
                    }
                case 79:
                    {
                        ambientSoundId = buf.ReadUnsignedShort();
                        int count = buf.ReadUnsignedByte();
                        extraSounds = new int[count];
                        for (int i = 0; i < count; i++) extraSounds[i] = buf.ReadUnsignedShort();
                        break;
                    }

                /*──────── menuOps 150-154 ────────*/
                case int m when (m >= 150 && m < 155):
                    menuOps[m - 150] = buf.ReadJagexString();
                    break;

                /*──────── minimap icons (160) ─────*/
                case 160:
                    {
                        int n = buf.ReadUnsignedByte();
                        minimapIcons = new ushort[n];
                        for (int i = 0; i < n; i++) minimapIcons[i] = (ushort)buf.ReadUnsignedShort();
                        break;
                    }

                /*──────── arbitrary params (249) ───*/
                case 249:
                    {
                        int len = buf.ReadUnsignedByte();
                        parameters = new SortedDictionary<int, object>();

                        for (int i = 0; i < len; i++)
                        {
                            bool isString = buf.ReadUnsignedByte() == 1;
                            int key = buf.ReadMedium();
                            object value = isString ? (object)buf.ReadJagexString() : buf.ReadInt();
                            if (!parameters.ContainsKey(key)) parameters.Add(key, value);
                        }
                        break;
                    }

                /*──────── unhandled opcodes ────────*/
                default:
                    // Safely ignore for now.
                    break;
            }
        }

        /*───────────────────────────────────────────*
         *  ▌  Helpers                               ▐
         *───────────────────────────────────────────*/
        private static int SmartOrMinus1(JagStream buf)
        {
            int val = buf.ReadUnsignedShort();
            return val == 0xFFFF ? -1 : val;
        }

        /*───────────────────────────────────────────*
         *  ▌  Encode back to binary                 ▐
         *───────────────────────────────────────────*/
        public JagStream Encode()
        {
            var o = new JagStream();

            /* local helper */
            void Emit(int op, Action payload = null)
            {
                o.WriteByte((byte)op);
                payload?.Invoke();
            }

            /*─── 1 / 5  – model-group tables ───────────────────────────*/
            if (modelIds != null && modelTypes != null)
            {
                int op = usesOpcode5 ? 5 : 1;
                Emit(op, () =>
                {
                    o.WriteByte((byte)modelIds.Length);
                    for (int g = 0; g < modelIds.Length; g++)
                    {
                        o.WriteSignedByte(modelTypes[g]);
                        o.WriteByte((byte)modelIds[g].Length);
                        foreach (ushort id in modelIds[g])
                            o.WriteShort(id);
                    }
                });
            }

            /*─── 2 – name ──────────────────────────────────────────────*/
            if (!string.IsNullOrEmpty(name))
                Emit(2, () => o.WriteJagexString(name));

            /*─── size (14 / 15) ───────────────────────────────────────*/
            if (sizeX != 1) Emit(14, () => o.WriteByte(sizeX));
            if (sizeY != 1) Emit(15, () => o.WriteByte(sizeY));

            /*─── walk-blocking flags (17 / 18) ────────────────────────*/
            if (!walkable)
            {
                // reproduce whichever flag we originally saw
                if (decoded[17]) Emit(17); else if (decoded[18]) Emit(18); else Emit(17);
            }

            /*─── 19 – category id ─────────────────────────────────────*/
            if (category != 0)
                Emit(19, () => o.WriteByte(category));

            /*─── 21-24 – clipping & animation information ─────────────*/
            if (clipType != 0) Emit(21, () => o.WriteByte(clipType));
            if (isClipped) Emit(22);
            if (obstructsGround == 1) Emit(23);
            if (animationId != -1) Emit(24, () => o.WriteShort(animationId));

            /*─── 27 – random animation start ─────────────────────────*/
            if (randomAnimStart) Emit(27);

            /*─── brightness / contrast (28 / 29) ─────────────────────*/
            if (modelBrightness != 0)
                Emit(28, () => o.WriteByte((byte)(modelBrightness >> 2))); // undo « <<2 » from decode
            if (modelContrast != 0)
                Emit(29, () => o.WriteSignedByte((sbyte)modelContrast));

            /*─── action strings 30-34 ────────────────────────────────*/
            for (int i = 0; i < actions.Length; i++)
                if (actions[i] != null)
                    Emit(30 + i, () => o.WriteJagexString(actions[i]));

            /*─── recolour (40) ───────────────────────────────────────*/
            if (recolSrc != null)
                Emit(40, () =>
                {
                    o.WriteByte((byte)recolSrc.Length);
                    for (int i = 0; i < recolSrc.Length; i++)
                    {
                        o.WriteShort(recolSrc[i]);
                        o.WriteShort(recolDst[i]);
                    }
                });

            /*─── retexture (41) ──────────────────────────────────────*/
            if (retexSrc != null)
                Emit(41, () =>
                {
                    o.WriteByte((byte)retexSrc.Length);
                    for (int i = 0; i < retexSrc.Length; i++)
                    {
                        o.WriteShort(retexSrc[i]);
                        o.WriteShort(retexDst[i]);
                    }
                });

            /*─── texture priorities (42) ─────────────────────────────*/
            if (texturePriorities != null)
                Emit(42, () =>
                {
                    o.WriteByte((byte)texturePriorities.Length);
                    foreach (sbyte b in texturePriorities) o.WriteSignedByte(b);
                });

            /*─── render-side flags 62-75 ─────────────────────────────*/
            if (flipped) Emit(62);
            if (!castsShadow) Emit(64);
            if (scaleX != 0) Emit(65, () => o.WriteShort(scaleX));
            if (scaleY != 0) Emit(66, () => o.WriteShort(scaleY));
            if (scaleZ != 0) Emit(67, () => o.WriteShort(scaleZ));
            if (mapSceneId != 0) Emit(68, () => o.WriteShort(mapSceneId));
            if (minimapForceClip != 0)
                Emit(69, () => o.WriteByte(minimapForceClip));

            if (offsetX != 0) Emit(70, () => o.WriteShort((short)(offsetX >> 2)));
            if (offsetY != 0) Emit(71, () => o.WriteShort((short)(offsetY >> 2)));
            if (offsetZ != 0) Emit(72, () => o.WriteShort((short)(offsetZ >> 2)));

            if (obstructsWheelchair) Emit(73);
            if (isSolid) Emit(74);
            if (supportItems != 0) Emit(75, () => o.WriteByte(supportItems));

            /*─── morph table (77 / 92) ───────────────────────────────*/
            if (morphIds != null)
            {
                bool use92 = decoded[92];
                int op = use92 ? 92 : 77;
                Emit(op, () =>
                {
                    o.WriteShort(morphVarbit == -1 ? 0xFFFF : morphVarbit);
                    o.WriteShort(morphVarp == -1 ? 0xFFFF : morphVarp);

                    if (use92)
                        o.WriteShort(morphIds[^1] == -1 ? 0xFFFF : morphIds[^1]);

                    int count = morphIds.Length - 2;
                    o.WriteByte((byte)count);
                    for (int i = 0; i <= count; i++)
                        o.WriteShort(morphIds[i] == -1 ? 0xFFFF : morphIds[i]);
                });
            }

            /*─── ambient sounds (78 / 79) ────────────────────────────*/
            if (ambientSoundId != -1)
            {
                if (extraSounds == null)
                    Emit(78, () =>
                    {
                        o.WriteShort(ambientSoundId);
                        o.WriteByte((byte)ambientSoundLoops);
                    });
                else
                    Emit(79, () =>
                    {
                        o.WriteShort(ambientSoundId);
                        o.WriteByte((byte)ambientSoundLoops);
                        o.WriteByte((byte)extraSounds.Length);
                        foreach (int s in extraSounds) o.WriteShort(s);
                    });
            }

            /*─── menu-ops 150-154 ───────────────────────────────────*/
            for (int i = 0; i < menuOps.Length; i++)
                if (menuOps[i] != null)
                    Emit(150 + i, () => o.WriteJagexString(menuOps[i]));

            /*─── minimap icons (160) ────────────────────────────────*/
            if (minimapIcons != null)
                Emit(160, () =>
                {
                    o.WriteByte((byte)minimapIcons.Length);
                    foreach (ushort icon in minimapIcons) o.WriteShort(icon);
                });

            /*─── params (249) ───────────────────────────────────────*/
            if (parameters != null && parameters.Count > 0)
                Emit(249, () =>
                {
                    o.WriteByte((byte)parameters.Count);
                    foreach (var kv in parameters)
                    {
                        bool isStr = kv.Value is string;
                        o.WriteByte((byte)(isStr ? 1 : 0));
                        o.WriteMedium(kv.Key);
                        if (isStr) o.WriteJagexString((string)kv.Value);
                        else o.WriteInteger((int)kv.Value);
                    }
                });

            /* terminator */
            o.WriteByte(0);
            return o.Flip();
        }
    }
}
