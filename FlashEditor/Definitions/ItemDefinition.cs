using static FlashEditor.utils.DebugUtil;
using System;
using System.Collections.Generic;
using System.Text;

namespace FlashEditor
{
    /// <summary>
    /// RuneScape “obj” (item-definition) – rev 639  
    /// Covers *all* opcodes in the original <code>readValues</code> plus
    /// safe raw-capture for anything unknown.
    /// </summary>
    internal sealed class ItemDefinition : ICloneable, IDefinition
    {
        /*──────────────────────────*
         *  ▌   PUBLIC FIELDS      ▐ *
         *──────────────────────────*/

        public string name;
        public int id;

        public bool[] decoded = new bool[256];                // diagnostics
        public string[] groundOptions = { null, null, "take", null, null };
        public string[] inventoryOptions = { null, null, null, null, "drop" };
        public string[] extraInventoryOps = new string[5];    // 150-154

        // model + render
        public int inventoryModelId;
        public int modelZoom;
        public int modelRotation1;
        public int modelRotation2;
        public int modelOffsetX;                              // signed (op 7)
        public int modelOffsetY;                              // signed (op 8)

        // equip models
        public int maleWearModel1, maleWearModel2;
        public int femaleWearModel1, femaleWearModel2;

        // equip data ---------------------------------------------
        public byte equipSlotId;          // opcode 13
        public byte equipId;              // opcode 14

        // wear offsets (signed << 2)
        public int manWearXOffset, manWearYOffset, manWearZOffset;
        public int womanWearXOffset, womanWearYOffset, womanWearZOffset;

        // recolour / retexture
        public short[] originalModelColors, modifiedModelColors;
        public short[] textureColour1, textureColour2;
        public sbyte[] texturePriorities;

        // flags / misc
        public int stackable;
        public int value;
        public int multiStackSize;
        public bool membersOnly;
        public bool unnoted;
        public int ambient, contrast;
        public int teamId;
        public int nameColor; public bool hasNameColor;

        // noted / lend
        public int notedId, notedTemplateId;
        public int lendId, lendTemplateId;

        // colour-equip overrides
        public int colourEquip1, colourEquip2;

        // ambient sound
        public int ambientSoundId = -1;
        public int ambientSoundLoops;
        public int[] extraSounds;

        // stack variants
        public int[] stackIds, stackAmounts;

        // arbitrary params
        public SortedDictionary<int, object> itemParams;

        /*──────── helper for “unknown but we must keep it” ────────*/
        private readonly Dictionary<int, byte[]> _rawUnknown = new();   // NEW

        /*──────────────────────────*
         *  ▌  SMALL HELPERS       ▐ *
         *──────────────────────────*/

        private static readonly StringBuilder SharedBuilder = new();

        public ItemDefinition Clone() => (ItemDefinition)MemberwiseClone();
        object ICloneable.Clone() => Clone();
        internal void SetId(int v) => id = v;
        public int GetId() => id;

        /*──────────────────────────*
         *  ▌  GLOBAL DECODE ENTRY  ▐ *
         *──────────────────────────*/

        public void Decode(JagStream s, int[] xteaKey = null)
        {
            int safety = 0;

            while (true)
            {
                int op = s.ReadByte();
                if (op == 0) break;                       // terminator
                DecodeOpcode(s, op);
                if (++safety > 256) break;                // corrupt-stream guard
            }
        }

        public static ItemDefinition DecodeFromStream(JagStream s)
        {
            var def = new ItemDefinition();
            def.Decode(s);
            return def;
        }

        /*──────────────────────────*
         *  ▌  PER-OPCODE HANDLER   ▐ *
         *──────────────────────────*/

        private void DecodeOpcode(JagStream buf, int op)
        {
            decoded[op] = true;                // (kept for debugging)

            switch (op)
            {
                /*──────── basic scalars ────────*/
                case 1: inventoryModelId = buf.ReadUnsignedShort(); return;
                case 2: name = buf.ReadJagexString(); return;
                case 3: modelZoom = buf.ReadUnsignedShort(); return;
                case 4: modelZoom = buf.ReadUnsignedShort(); return;
                case 5: modelRotation1 = buf.ReadUnsignedShort(); return;
                case 6: modelRotation2 = buf.ReadUnsignedShort(); return;

                /* signed offsets */
                case 7: modelOffsetX = buf.ReadShort(); return;
                case 8: modelOffsetY = buf.ReadShort(); return;

                /* value / stackable */
                case 11: stackable = 1; return;
                case 12: value = buf.ReadInt(); return;

                /* equip slot + id */
                case 13: equipSlotId = (byte) buf.ReadByte(); return;
                case 14: equipId = (byte) buf.ReadByte(); return;

                case 15: stackable = 0; return;
                case 17: stackable = 0; return; // +action reset (ignored)

                /* misc byte flags */
                case 16: membersOnly = true; return;
                case 19: _rawUnknown[op] = buf.ReadBytes(1); return;
                case 21:
                case 22: _rawUnknown[op] = buf.ReadBytes(0); return; // simple flags

                /* models */
                case 23: maleWearModel1 = buf.ReadUnsignedShort(); return;
                case 24: maleWearModel2 = buf.ReadUnsignedShort(); return;
                case 25: femaleWearModel1 = buf.ReadUnsignedShort(); return;
                case 26: femaleWearModel2 = buf.ReadUnsignedShort(); return;
                case 27: _rawUnknown[op] = buf.ReadBytes(1); return;

                /* zoom tweak, signed bytes */
                case 28: _rawUnknown[op] = buf.ReadBytes(1); return;
                case 29: _rawUnknown[op] = buf.ReadBytes(1); return;
                case 39: _rawUnknown[op] = buf.ReadBytes(1); return;

                /* ground / inventory menus */
                case int a when a >= 30 && a < 35:
                    groundOptions[a - 30] = buf.ReadJagexString(); return;

                case int b when b >= 35 && b < 40:
                    inventoryOptions[b - 35] = buf.ReadJagexString(); return;

                /* recolour */
                case 40:
                    {
                        int n = buf.ReadByte();
                        originalModelColors = new short[n];
                        modifiedModelColors = new short[n];
                        for (int i = 0; i < n; i++)
                        {
                            originalModelColors[i] = (short)buf.ReadUnsignedShort();
                            modifiedModelColors[i] = (short)buf.ReadUnsignedShort();
                        }
                        return;
                    }

                /* retexture */
                case 41:
                    {
                        int n = buf.ReadByte();
                        textureColour1 = new short[n];
                        textureColour2 = new short[n];
                        for (int i = 0; i < n; i++)
                        {
                            textureColour1[i] = (short)buf.ReadUnsignedShort();
                            textureColour2[i] = (short)buf.ReadUnsignedShort();
                        }
                        return;
                    }

                /* texture priority table */
                case 42:
                    {
                        int n = buf.ReadByte();
                        texturePriorities = new sbyte[n];
                        for (int i = 0; i < n; i++)
                            texturePriorities[i] = buf.ReadSignedByte();
                        return;
                    }

                /* unused ushort holders (keep raw) */
                case 44:
                case 45:
                    _rawUnknown[op] = buf.ReadBytes(2); return;

                /* unnoted flag */
                case 65: unnoted = true; return;

                /* sound blocks */
                case 78:
                    ambientSoundId = buf.ReadUnsignedShort();
                    ambientSoundLoops = buf.ReadByte();
                    return;

                case 79:
                    ambientSoundId = buf.ReadUnsignedShort();
                    buf.ReadUnsignedShort();              // vol placeholder
                    ambientSoundLoops = buf.ReadByte();
                    int c = buf.ReadByte();
                    extraSounds = new int[c];
                    for (int i = 0; i < c; i++) extraSounds[i] = buf.ReadUnsignedShort();
                    return;

                /* colour-equip overrides */
                case 90: colourEquip1 = buf.ReadUnsignedShort(); return;
                case 91: colourEquip2 = buf.ReadUnsignedShort(); return;

                /* npc/object links etc – keep raw */
                case 92:
                case 93:
                case 94:
                case 95:
                    _rawUnknown[op] = buf.ReadBytes(2); return;

                case 96: _rawUnknown[op] = buf.ReadBytes(1); return;

                /* noted pair */
                case 97: notedId = buf.ReadUnsignedShort(); return;
                case 98: notedTemplateId = buf.ReadUnsignedShort(); return;

                /* stack variants 100-109 */
                case int v when v >= 100 && v < 110:
                    if (stackIds == null) { stackIds = new int[10]; stackAmounts = new int[10]; }
                    stackIds[v - 100] = buf.ReadUnsignedShort();
                    stackAmounts[v - 100] = buf.ReadUnsignedShort();
                    return;

                /* model resize, keep raw */
                case 110:
                case 111:
                case 112:
                    _rawUnknown[op] = buf.ReadBytes(2); return;

                /* ambient / contrast */
                case 113: ambient = buf.ReadSignedByte(); return;
                case 114: contrast = buf.ReadSignedByte(); return;

                /* team id */
                case 115: teamId = buf.ReadByte(); return;

                /* lending */
                case 121: lendId = buf.ReadUnsignedShort(); return;
                case 122: lendTemplateId = buf.ReadUnsignedShort(); return;

                /* wear offsets */
                case 125:
                    manWearXOffset = buf.ReadSignedByte() << 2;
                    manWearYOffset = buf.ReadSignedByte() << 2;
                    manWearZOffset = buf.ReadSignedByte() << 2;
                    return;

                case 126:
                    womanWearXOffset = buf.ReadSignedByte() << 2;
                    womanWearYOffset = buf.ReadSignedByte() << 2;
                    womanWearZOffset = buf.ReadSignedByte() << 2;
                    return;

                /* cursor icons keep raw */
                case 127:
                case 128:
                case 129:
                case 130:
                    _rawUnknown[op] = buf.ReadBytes(3); return;

                /* variants list */
                case 132:
                    {
                        int n = buf.ReadByte();
                        _rawUnknown[op] = buf.ReadBytes(n * 2 + 1);
                        return;
                    }

                /* scale */
                case 134:
                    _rawUnknown[op] = buf.ReadBytes(1); return;

                /* bind / quest links keep raw */
                case 139:
                case 140:
                case 142:
                case 143:
                case 144:
                case 145:
                case 146:
                    _rawUnknown[op] = buf.ReadBytes(2); return;

                /* multi-map icons */
                case 160:
                    {
                        int n = buf.ReadByte();
                        _rawUnknown[op] = buf.ReadBytes(1 + n * 2);
                        return;
                    }

                /* shard / combine keep raw */
                case 161:
                case 162:
                case 163:
                    _rawUnknown[op] = buf.ReadBytes(2); return;
                case 164: _rawUnknown[op] = buf.ReadBytes(buf.ReadByte() + 1); return;

                /* 165-170 lighting offsets */
                case 165:
                case 166:
                case 167:
                case 168:
                case 169:
                case 170:
                    _rawUnknown[op] = buf.ReadBytes(op switch { 165 or 166 => 2, _ => 1 });
                    return;

                case 173: _rawUnknown[op] = buf.ReadBytes(4); return;
                case 177:
                case 178: _rawUnknown[op] = new byte[0]; return;

                /* params */
                case 249:
                    {
                        int n = buf.ReadByte();
                        itemParams = new SortedDictionary<int, object>();
                        for (int i = 0; i < n; i++)
                        {
                            bool isStr = buf.ReadByte() == 1;
                            int key = buf.ReadMedium();
                            object val = isStr ? buf.ReadJagexString() : buf.ReadInt();
                            if (!itemParams.ContainsKey(key)) itemParams.Add(key, val);
                        }
                        return;
                    }

                /* fallback – capture one byte so we don’t desync */
                default:
                    _rawUnknown[op] = [(byte) buf.ReadByte()];
                    return;
            }
        }

        /*──────────────────────────*
         *  ▌  ENCODE (round-trip)  ▐ *
         *──────────────────────────*/

        public JagStream Encode()
        {
            var o = new JagStream();

            /* helper */
            void Emit(int code, Action payload = null)
            {
                o.WriteByte((byte)code);
                payload?.Invoke();
                _rawUnknown.Remove(code);               // mark as written
            }

            /* model & basic */
            Emit(1, () => o.WriteShort(inventoryModelId));
            if (!string.IsNullOrEmpty(name)) Emit(2, () => o.WriteJagexString(name));
            Emit(3, () => o.WriteShort(modelZoom));
            Emit(4, () => o.WriteShort(modelZoom));
            Emit(5, () => o.WriteShort(modelRotation1));
            Emit(6, () => o.WriteShort(modelRotation2));
            Emit(7, () => o.WriteShort((short)modelOffsetX));
            Emit(8, () => o.WriteShort((short)modelOffsetY));

            /* stackable / value */
            if (stackable == 1) Emit(11);
            Emit(12, () => o.WriteInteger(value));

            /* equip */
            Emit(13, () => o.WriteByte((byte)equipSlotId));
            Emit(14, () => o.WriteByte((byte)equipId));
            if (membersOnly) Emit(16);

            Emit(18, () => o.WriteShort(multiStackSize));

            /* worn models */
            Emit(23, () => o.WriteShort(maleWearModel1));
            Emit(24, () => o.WriteShort(maleWearModel2));
            Emit(25, () => o.WriteShort(femaleWearModel1));
            Emit(26, () => o.WriteShort(femaleWearModel2));

            /* ground / inventory actions */
            for (int i = 0; i < 5; i++)
                if (groundOptions[i] != null)
                    Emit(30 + i, () => o.WriteJagexString(groundOptions[i]));

            for (int i = 0; i < 5; i++)
                if (inventoryOptions[i] != null)
                    Emit(35 + i, () => o.WriteJagexString(inventoryOptions[i]));

            /* recolour */
            if (originalModelColors != null)
                Emit(40, () =>
                {
                    o.WriteByte((byte)originalModelColors.Length);
                    for (int i = 0; i < originalModelColors.Length; i++)
                    {
                        o.WriteShort(originalModelColors[i]);
                        o.WriteShort(modifiedModelColors[i]);
                    }
                });

            /* retexture */
            if (textureColour1 != null)
                Emit(41, () =>
                {
                    o.WriteByte((byte)textureColour1.Length);
                    for (int i = 0; i < textureColour1.Length; i++)
                    {
                        o.WriteShort(textureColour1[i]);
                        o.WriteShort(textureColour2[i]);
                    }
                });

            /* priorities */
            if (texturePriorities != null)
                Emit(42, () =>
                {
                    o.WriteByte((byte)texturePriorities.Length);
                    foreach (sbyte b in texturePriorities) o.WriteSignedByte(b);
                });

            /* name colour */
            if (hasNameColor) Emit(43, () => o.WriteInteger(nameColor));

            /* unnoted */
            if (unnoted) Emit(65);

            /* sound */
            if (ambientSoundId >= 0)
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
                        o.WriteShort(0);
                        o.WriteByte((byte)ambientSoundLoops);
                        o.WriteByte((byte)extraSounds.Length);
                        foreach (int sid in extraSounds) o.WriteShort(sid);
                    });
            }

            /* colour-equip overrides */
            Emit(90, () => o.WriteShort(colourEquip1));
            Emit(91, () => o.WriteShort(colourEquip2));

            /* noted */
            Emit(97, () => o.WriteShort(notedId));
            Emit(98, () => o.WriteShort(notedTemplateId));

            /* stack variants */
            if (stackIds != null)
                for (int i = 0; i < 10; i++)
                    if (stackIds[i] != 0)
                        Emit(100 + i, () =>
                        {
                            o.WriteShort(stackIds[i]);
                            o.WriteShort(stackAmounts[i]);
                        });

            /* ambient / contrast */
            Emit(113, () => o.WriteSignedByte((sbyte)ambient));
            Emit(114, () => o.WriteSignedByte((sbyte)contrast));
            Emit(115, () => o.WriteByte((byte)teamId));

            /* lending */
            Emit(121, () => o.WriteShort(lendId));
            Emit(122, () => o.WriteShort(lendTemplateId));

            /* wear offsets */
            Emit(125, () =>
            {
                o.WriteSignedByte((sbyte)(manWearXOffset >> 2));
                o.WriteSignedByte((sbyte)(manWearYOffset >> 2));
                o.WriteSignedByte((sbyte)(manWearZOffset >> 2));
            });
            Emit(126, () =>
            {
                o.WriteSignedByte((sbyte)(womanWearXOffset >> 2));
                o.WriteSignedByte((sbyte)(womanWearYOffset >> 2));
                o.WriteSignedByte((sbyte)(womanWearZOffset >> 2));
            });

            /* extra inventory ops 150-154 */
            for (int i = 0; i < 5; i++)
                if (extraInventoryOps[i] != null)
                    Emit(150 + i, () => o.WriteJagexString(extraInventoryOps[i]));

            /* params */
            if (itemParams != null && itemParams.Count > 0)
                Emit(249, () =>
                {
                    o.WriteByte((byte)itemParams.Count);
                    foreach (var kv in itemParams)
                    {
                        bool isStr = kv.Value is string;
                        o.WriteByte((byte)(isStr ? 1 : 0));
                        o.WriteMedium(kv.Key);
                        if (isStr) o.WriteJagexString((string)kv.Value);
                        else o.WriteInteger((int)kv.Value);
                    }
                });

            /*──────────── replay any raw-captured opcodes ───────────*/
            foreach (var kv in _rawUnknown)
            {
                o.WriteByte((byte)kv.Key);
                o.Write(kv.Value);
            }

            /* terminator */
            o.WriteByte(0);
            return o.Flip();
        }
    }
}
