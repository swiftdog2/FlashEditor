using static FlashEditor.Utils.DebugUtil;
using System;
using System.Collections.Generic;
using System.Text;

namespace FlashEditor {
    /// <summary>
    /// RuneScape "obj" (item-definition) – rev 639
    /// Opcode table sourced from decompiled rev 640 client (openrs2-nonfree639).
    /// </summary>
    public class ItemDefinition : ICloneable, IDefinition {
        /*──────────────────────────*
         *  ▌   PUBLIC FIELDS      ▐ *
         *──────────────────────────*/

        /// <summary>Display name shown in-game.</summary>
        public string name;
        /// <summary>Unique item identifier.</summary>
        public int id;

        /// <summary>Diagnostic flag array tracking which opcodes were read.</summary>
        public bool[] decoded = new bool[256];
        /// <summary>Right-click menu options when the item is on the ground.</summary>
        public string[] groundOptions = { null, null, "take", null, null };
        /// <summary>Right-click menu options when the item is in the inventory.</summary>
        public string[] inventoryOptions = { null, null, null, null, "drop" };

        /// <summary>Model id used when rendering the item in the inventory.</summary>
        public int inventoryModelId;
        /// <summary>Zoom level for the inventory model.</summary>
        public int modelZoom = 2000;
        /// <summary>Primary rotation angle of the inventory model (xan2d).</summary>
        public int modelRotation1;
        /// <summary>Secondary rotation angle of the inventory model (yan2d).</summary>
        public int modelRotation2;
        /// <summary>Z-axis rotation angle of the inventory model (zan2d).</summary>
        public int zan2d;
        /// <summary>Horizontal pixel offset when rendering the inventory icon.</summary>
        public int modelOffsetX;
        /// <summary>Vertical pixel offset when rendering the inventory icon.</summary>
        public int modelOffsetY;

        /// <summary>Primary and secondary male equipment model ids.</summary>
        public int maleWearModel1, maleWearModel2;
        /// <summary>Tertiary male equipment model id.</summary>
        public int maleWearModel3;
        /// <summary>Primary and secondary female equipment model ids.</summary>
        public int femaleWearModel1, femaleWearModel2;
        /// <summary>Tertiary female equipment model id.</summary>
        public int femaleWearModel3;

        /// <summary>Male chathead model ids.</summary>
        public int maleHeadModel1, maleHeadModel2;
        /// <summary>Female chathead model ids.</summary>
        public int femaleHeadModel1, femaleHeadModel2;

        /// <summary>Equipment slot this item occupies (UI binding, not in rev 639 cache).</summary>
        public byte equipSlotId;
        /// <summary>Equipment appearance identifier (UI binding, not in rev 639 cache).</summary>
        public byte equipId;

        /// <summary>Male equipment translation offsets (pre-shifted by 2 bits).</summary>
        public int manWearXOffset, manWearYOffset, manWearZOffset;
        /// <summary>Female equipment translation offsets (pre-shifted by 2 bits).</summary>
        public int womanWearXOffset, womanWearYOffset, womanWearZOffset;

        /// <summary>Source and destination HSL colour replacement pairs.</summary>
        public short[] originalModelColors, modifiedModelColors;
        /// <summary>Source and destination texture replacement pairs.</summary>
        public short[] textureColour1, textureColour2;
        /// <summary>Per-texture rendering priority overrides.</summary>
        public sbyte[] texturePriorities;

        /// <summary>Whether the item stacks in inventory (1 = stackable).</summary>
        public int stackable;
        /// <summary>Base value in coins used by shops and alchemy.</summary>
        public int value = 1;
        /// <summary>Stack size display variant.</summary>
        public int multiStackSize = -1;
        /// <summary>Whether the item is restricted to members worlds.</summary>
        public bool membersOnly;
        /// <summary>Whether this item is tradeable on the GE.</summary>
        public bool unnoted;
        /// <summary>Lighting ambient parameter for the inventory model.</summary>
        public int ambient;
        /// <summary>Lighting contrast parameter for the inventory model.</summary>
        public int contrast;
        /// <summary>Team cape identifier for PvP grouping.</summary>
        public int teamId;
        /// <summary>Dummy item type flag.</summary>
        public int dummyItem;

        /// <summary>Item id of the noted variant and its template.</summary>
        public int notedId, notedTemplateId;
        /// <summary>Item id of the lent variant and its template.</summary>
        public int lendId, lendTemplateId;
        /// <summary>Item id of the bind/shard variant and its template.</summary>
        public int bindId, bindTemplateId;

        /// <summary>Model resize factors (default 128).</summary>
        public int resizeX = 128, resizeY = 128, resizeZ = 128;
        /// <summary>Pick size shift value.</summary>
        public int pickSizeShift;

        /// <summary>Visual model variants at specific stack counts.</summary>
        public int[] stackIds, stackAmounts;

        /// <summary>Quest id requirements.</summary>
        public int[] quests;

        /// <summary>Cursor overrides (op, id) for ground and inventory.</summary>
        public int cursor1Op = -1, cursor1Id = -1;
        public int cursor2Op = -1, cursor2Id = -1;
        public int cursor3Op = -1, cursor3Id = -1;
        public int cursor4Op = -1, cursor4Id = -1;
        public int cursor5Op = -1, cursor5Id = -1;

        /// <summary>Arbitrary key-value parameters (opcode 249).</summary>
        public SortedDictionary<int, object> itemParams;

        /*──────────────────────────*
         *  ▌  SMALL HELPERS       ▐ *
         *──────────────────────────*/

        private static readonly StringBuilder SharedBuilder = new();

        public ItemDefinition Clone() => (ItemDefinition) MemberwiseClone();
        object ICloneable.Clone() => Clone();
        internal void SetId(int v) => id = v;
        public int GetId() => id;

        /*──────────────────────────*
         *  ▌  GLOBAL DECODE ENTRY  ▐ *
         *──────────────────────────*/

        public void Decode(JagStream s, int[] xteaKey = null) {
            int safety = 0;

            while (true) {
                int op = s.ReadByte();
                if (op <= 0) break;                       // 0 = terminator, -1 = EOF
                DecodeOpcode(s, op);
                if (++safety > 256) break;                // corrupt-stream guard
            }
        }

        public static ItemDefinition DecodeFromStream(JagStream s) {
            var def = new ItemDefinition();
            def.Decode(s);
            return def;
        }

        /*──────────────────────────*
         *  ▌  PER-OPCODE HANDLER   ▐ *
         *──────────────────────────*/

        private void DecodeOpcode(JagStream buf, int op) {
            decoded[op] = true;

            switch (op) {
                case 1: inventoryModelId = buf.ReadUnsignedShort(); return;
                case 2: name = buf.ReadJagexString(); return;
                case 4: modelZoom = buf.ReadUnsignedShort(); return;
                case 5: modelRotation1 = buf.ReadUnsignedShort(); return;
                case 6: modelRotation2 = buf.ReadUnsignedShort(); return;

                case 7:
                    modelOffsetX = buf.ReadUnsignedShort();
                    if (modelOffsetX > 32767) modelOffsetX -= 65536;
                    return;
                case 8:
                    modelOffsetY = buf.ReadUnsignedShort();
                    if (modelOffsetY > 32767) modelOffsetY -= 65536;
                    return;

                case 11: stackable = 1; return;
                case 12: value = buf.ReadInt(); return;
                case 16: membersOnly = true; return;
                case 18: multiStackSize = buf.ReadUnsignedShort(); return;

                /* worn models */
                case 23: maleWearModel1 = buf.ReadUnsignedShort(); return;
                case 24: maleWearModel2 = buf.ReadUnsignedShort(); return;
                case 25: femaleWearModel1 = buf.ReadUnsignedShort(); return;
                case 26: femaleWearModel2 = buf.ReadUnsignedShort(); return;

                /* ground / inventory menus */
                case int a when a >= 30 && a < 35:
                    groundOptions[a - 30] = buf.ReadJagexString(); return;

                case int b when b >= 35 && b < 40:
                    inventoryOptions[b - 35] = buf.ReadJagexString(); return;

                /* recolour */
                case 40: {
                        int n = buf.ReadByte();
                        originalModelColors = new short[n];
                        modifiedModelColors = new short[n];
                        for (int i = 0 ; i < n ; i++) {
                            originalModelColors[i] = (short) buf.ReadUnsignedShort();
                            modifiedModelColors[i] = (short) buf.ReadUnsignedShort();
                        }
                        return;
                    }

                /* retexture */
                case 41: {
                        int n = buf.ReadByte();
                        textureColour1 = new short[n];
                        textureColour2 = new short[n];
                        for (int i = 0 ; i < n ; i++) {
                            textureColour1[i] = (short) buf.ReadUnsignedShort();
                            textureColour2[i] = (short) buf.ReadUnsignedShort();
                        }
                        return;
                    }

                /* texture priority table */
                case 42: {
                        int n = buf.ReadByte();
                        texturePriorities = new sbyte[n];
                        for (int i = 0 ; i < n ; i++)
                            texturePriorities[i] = buf.ReadSignedByte();
                        return;
                    }

                /* GE tradeable */
                case 65: unnoted = true; return;

                /* tertiary worn models */
                case 78: maleWearModel3 = buf.ReadUnsignedShort(); return;
                case 79: femaleWearModel3 = buf.ReadUnsignedShort(); return;

                /* chathead models */
                case 90: maleHeadModel1 = buf.ReadUnsignedShort(); return;
                case 91: maleHeadModel2 = buf.ReadUnsignedShort(); return;
                case 92: femaleHeadModel1 = buf.ReadUnsignedShort(); return;
                case 93: femaleHeadModel2 = buf.ReadUnsignedShort(); return;

                /* z-axis rotation */
                case 95: zan2d = buf.ReadUnsignedShort(); return;

                /* dummy item */
                case 96: dummyItem = buf.ReadByte(); return;

                /* noted pair */
                case 97: notedId = buf.ReadUnsignedShort(); return;
                case 98: notedTemplateId = buf.ReadUnsignedShort(); return;

                /* stack variants 100-109 */
                case int v when v >= 100 && v < 110:
                    if (stackIds == null) { stackIds = new int[10]; stackAmounts = new int[10]; }
                    stackIds[v - 100] = buf.ReadUnsignedShort();
                    stackAmounts[v - 100] = buf.ReadUnsignedShort();
                    return;

                /* model resize */
                case 110: resizeX = buf.ReadUnsignedShort(); return;
                case 111: resizeY = buf.ReadUnsignedShort(); return;
                case 112: resizeZ = buf.ReadUnsignedShort(); return;

                /* ambient / contrast */
                case 113: ambient = buf.ReadSignedByte(); return;
                case 114: contrast = buf.ReadSignedByte() * 5; return;

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

                /* cursor overrides */
                case 127:
                    cursor1Op = buf.ReadByte();
                    cursor1Id = buf.ReadUnsignedShort();
                    return;
                case 128:
                    cursor2Op = buf.ReadByte();
                    cursor2Id = buf.ReadUnsignedShort();
                    return;
                case 129:
                    cursor3Op = buf.ReadByte();
                    cursor3Id = buf.ReadUnsignedShort();
                    return;
                case 130:
                    cursor4Op = buf.ReadByte();
                    cursor4Id = buf.ReadUnsignedShort();
                    return;
                case 131:
                    cursor5Op = buf.ReadByte();
                    cursor5Id = buf.ReadUnsignedShort();
                    return;

                /* quest requirements */
                case 132: {
                        int n = buf.ReadByte();
                        quests = new int[n];
                        for (int i = 0 ; i < n ; i++)
                            quests[i] = buf.ReadUnsignedShort();
                        return;
                    }

                /* pick size shift */
                case 134: pickSizeShift = buf.ReadByte(); return;

                /* bind/shard pair */
                case 139: bindId = buf.ReadUnsignedShort(); return;
                case 140: bindTemplateId = buf.ReadUnsignedShort(); return;

                /* params */
                case 249: {
                        int n = buf.ReadByte();
                        itemParams = new SortedDictionary<int, object>();
                        for (int i = 0 ; i < n ; i++) {
                            bool isStr = buf.ReadByte() == 1;
                            int key = buf.ReadMedium();
                            object val = isStr ? buf.ReadJagexString() : buf.ReadInt();
                            if (!itemParams.ContainsKey(key)) itemParams.Add(key, val);
                        }
                        return;
                    }

                /* unknown opcode - bail out */
                default:
                    Debug($"Unknown item opcode {op} at position {buf.Position}/{buf.Length} for item {id}");
                    throw new InvalidOperationException($"Unknown item opcode {op}");
            }
        }

        /*──────────────────────────*
         *  ▌  ENCODE (round-trip)  ▐ *
         *──────────────────────────*/

        public JagStream Encode() {
            var o = new JagStream();

            void Emit(int code, Action payload = null) {
                o.WriteByte((byte) code);
                payload?.Invoke();
            }

            /* model & basic */
            Emit(1, () => o.WriteShort(inventoryModelId));
            if (!string.IsNullOrEmpty(name)) Emit(2, () => o.WriteJagexString(name));
            Emit(4, () => o.WriteShort(modelZoom));
            Emit(5, () => o.WriteShort(modelRotation1));
            Emit(6, () => o.WriteShort(modelRotation2));
            if (modelOffsetX != 0) Emit(7, () => o.WriteShort((short) modelOffsetX));
            if (modelOffsetY != 0) Emit(8, () => o.WriteShort((short) modelOffsetY));

            /* stackable / value */
            if (stackable == 1) Emit(11);
            Emit(12, () => o.WriteInteger(value));
            if (membersOnly) Emit(16);
            if (multiStackSize != -1) Emit(18, () => o.WriteShort(multiStackSize));

            /* worn models */
            if (maleWearModel1 != 0) Emit(23, () => o.WriteShort(maleWearModel1));
            if (maleWearModel2 != 0) Emit(24, () => o.WriteShort(maleWearModel2));
            if (femaleWearModel1 != 0) Emit(25, () => o.WriteShort(femaleWearModel1));
            if (femaleWearModel2 != 0) Emit(26, () => o.WriteShort(femaleWearModel2));

            /* ground / inventory actions */
            for (int i = 0 ; i < 5 ; i++)
                if (groundOptions[i] != null)
                    Emit(30 + i, () => o.WriteJagexString(groundOptions[i]));

            for (int i = 0 ; i < 5 ; i++)
                if (inventoryOptions[i] != null)
                    Emit(35 + i, () => o.WriteJagexString(inventoryOptions[i]));

            /* recolour */
            if (originalModelColors != null)
                Emit(40, () => {
                    o.WriteByte((byte) originalModelColors.Length);
                    for (int i = 0 ; i < originalModelColors.Length ; i++) {
                        o.WriteShort(originalModelColors[i]);
                        o.WriteShort(modifiedModelColors[i]);
                    }
                });

            /* retexture */
            if (textureColour1 != null)
                Emit(41, () => {
                    o.WriteByte((byte) textureColour1.Length);
                    for (int i = 0 ; i < textureColour1.Length ; i++) {
                        o.WriteShort(textureColour1[i]);
                        o.WriteShort(textureColour2[i]);
                    }
                });

            /* priorities */
            if (texturePriorities != null)
                Emit(42, () => {
                    o.WriteByte((byte) texturePriorities.Length);
                    foreach (sbyte b in texturePriorities) o.WriteSignedByte(b);
                });

            /* GE tradeable */
            if (unnoted) Emit(65);

            /* tertiary worn models */
            if (maleWearModel3 != 0) Emit(78, () => o.WriteShort(maleWearModel3));
            if (femaleWearModel3 != 0) Emit(79, () => o.WriteShort(femaleWearModel3));

            /* chathead models */
            if (maleHeadModel1 != 0) Emit(90, () => o.WriteShort(maleHeadModel1));
            if (maleHeadModel2 != 0) Emit(91, () => o.WriteShort(maleHeadModel2));
            if (femaleHeadModel1 != 0) Emit(92, () => o.WriteShort(femaleHeadModel1));
            if (femaleHeadModel2 != 0) Emit(93, () => o.WriteShort(femaleHeadModel2));

            /* z-axis rotation */
            if (zan2d != 0) Emit(95, () => o.WriteShort(zan2d));

            /* dummy item */
            if (dummyItem != 0) Emit(96, () => o.WriteByte((byte) dummyItem));

            /* noted */
            if (notedId != 0) Emit(97, () => o.WriteShort(notedId));
            if (notedTemplateId != 0) Emit(98, () => o.WriteShort(notedTemplateId));

            /* stack variants */
            if (stackIds != null)
                for (int i = 0 ; i < 10 ; i++)
                    if (stackIds[i] != 0)
                        Emit(100 + i, () => {
                            o.WriteShort(stackIds[i]);
                            o.WriteShort(stackAmounts[i]);
                        });

            /* model resize */
            if (resizeX != 128) Emit(110, () => o.WriteShort(resizeX));
            if (resizeY != 128) Emit(111, () => o.WriteShort(resizeY));
            if (resizeZ != 128) Emit(112, () => o.WriteShort(resizeZ));

            /* ambient / contrast */
            if (ambient != 0) Emit(113, () => o.WriteSignedByte((sbyte) ambient));
            if (contrast != 0) Emit(114, () => o.WriteSignedByte((sbyte) (contrast / 5)));
            if (teamId != 0) Emit(115, () => o.WriteByte((byte) teamId));

            /* lending */
            if (lendId != 0) Emit(121, () => o.WriteShort(lendId));
            if (lendTemplateId != 0) Emit(122, () => o.WriteShort(lendTemplateId));

            /* wear offsets */
            if (manWearXOffset != 0 || manWearYOffset != 0 || manWearZOffset != 0)
                Emit(125, () => {
                    o.WriteSignedByte((sbyte) (manWearXOffset >> 2));
                    o.WriteSignedByte((sbyte) (manWearYOffset >> 2));
                    o.WriteSignedByte((sbyte) (manWearZOffset >> 2));
                });
            if (womanWearXOffset != 0 || womanWearYOffset != 0 || womanWearZOffset != 0)
                Emit(126, () => {
                    o.WriteSignedByte((sbyte) (womanWearXOffset >> 2));
                    o.WriteSignedByte((sbyte) (womanWearYOffset >> 2));
                    o.WriteSignedByte((sbyte) (womanWearZOffset >> 2));
                });

            /* cursor overrides */
            if (cursor1Op >= 0) Emit(127, () => { o.WriteByte((byte) cursor1Op); o.WriteShort(cursor1Id); });
            if (cursor2Op >= 0) Emit(128, () => { o.WriteByte((byte) cursor2Op); o.WriteShort(cursor2Id); });
            if (cursor3Op >= 0) Emit(129, () => { o.WriteByte((byte) cursor3Op); o.WriteShort(cursor3Id); });
            if (cursor4Op >= 0) Emit(130, () => { o.WriteByte((byte) cursor4Op); o.WriteShort(cursor4Id); });
            if (cursor5Op >= 0) Emit(131, () => { o.WriteByte((byte) cursor5Op); o.WriteShort(cursor5Id); });

            /* quest requirements */
            if (quests != null && quests.Length > 0)
                Emit(132, () => {
                    o.WriteByte((byte) quests.Length);
                    foreach (int q in quests) o.WriteShort(q);
                });

            /* pick size shift */
            if (pickSizeShift != 0) Emit(134, () => o.WriteByte((byte) pickSizeShift));

            /* bind/shard */
            if (bindId != 0) Emit(139, () => o.WriteShort(bindId));
            if (bindTemplateId != 0) Emit(140, () => o.WriteShort(bindTemplateId));

            /* params */
            if (itemParams != null && itemParams.Count > 0)
                Emit(249, () => {
                    o.WriteByte((byte) itemParams.Count);
                    foreach (var kv in itemParams) {
                        bool isStr = kv.Value is string;
                        o.WriteByte((byte) (isStr ? 1 : 0));
                        o.WriteMedium(kv.Key);
                        if (isStr) o.WriteJagexString((string) kv.Value);
                        else o.WriteInteger((int) kv.Value);
                    }
                });

            /* terminator */
            o.WriteByte(0);
            return o.Flip();
        }
    }
}
