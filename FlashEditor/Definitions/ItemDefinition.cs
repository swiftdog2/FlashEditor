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

        /// <summary>The opcodes this definition was decoded from, in the order they appeared.</summary>
        /// <remarks>
        ///     The revision-639 packer does not write item opcodes in ascending order - a typical
        ///     record runs 1, 7, 8, 4, 6, 5, ... with the name near the end - and nothing in the
        ///     format says it should, because the client dispatches on whatever opcode it reads
        ///     next. Order is therefore not recoverable from the decoded fields, so it is kept
        ///     here and replayed by <see cref="Encode"/>. Without it a saved item is semantically
        ///     identical but byte-different, which changes the archive, its CRC, and the
        ///     reference table entry for every item packed alongside it.
        /// </remarks>
        public readonly List<int> opcodeOrder = new List<int>();

        /// <summary>The raw payload of each entry in <see cref="opcodeOrder"/>, index for index.</summary>
        /// <remarks>
        ///     Three hundred of the twenty thousand items in a revision-639 cache write the same
        ///     opcode twice, and the client keeps only the second - the first never reaches a
        ///     field, so nothing but its bytes records that it was ever there.
        ///     <see cref="Encode"/> replays those bytes verbatim and re-derives only the last
        ///     occurrence from the fields, which is the one an edit would have changed.
        /// </remarks>
        public readonly List<byte[]> opcodePayloads = new List<byte[]>();
        /// <summary>
        ///     The ground menu the client assumes when no opcode 30-34 is present.
        /// </summary>
        /// <remarks>
        ///     Held separately from the field initialiser so <see cref="Encode"/> can tell an
        ///     option the cache actually stored from one that is only there because the decoder
        ///     seeded it. Emitting the seeded value back would add opcodes the cache never
        ///     carried.
        /// </remarks>
        public static readonly string[] DefaultGroundOptions = { null, null, "take", null, null };

        /// <summary>
        ///     The inventory menu the client assumes when no opcode 35-39 is present.
        /// </summary>
        public static readonly string[] DefaultInventoryOptions = { null, null, null, null, "drop" };

        /// <summary>Right-click menu options when the item is on the ground.</summary>
        public string[] groundOptions = (string[]) DefaultGroundOptions.Clone();
        /// <summary>Right-click menu options when the item is in the inventory.</summary>
        public string[] inventoryOptions = (string[]) DefaultInventoryOptions.Clone();

        /// <summary>Model id used when rendering the item in the inventory.</summary>
        public int inventoryModelId;
        /// <summary>Zoom level the client assumes when opcode 4 is absent.</summary>
        public const int DefaultModelZoom = 2000;
        /// <summary>Zoom level for the inventory model.</summary>
        public int modelZoom = DefaultModelZoom;
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

        /// <summary>Male chathead model ids - opcodes 90 and 92.</summary>
        public int maleHeadModel1, maleHeadModel2;
        /// <summary>Female chathead model ids - opcodes 91 and 93.</summary>
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
        /// <summary>Base value the client assumes when opcode 12 is absent.</summary>
        public const int DefaultValue = 1;
        /// <summary>Base value in coins used by shops and alchemy.</summary>
        public int value = DefaultValue;
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

        /// <summary>Resize factor the client assumes when opcodes 110-112 are absent.</summary>
        public const int DefaultResize = 128;
        /// <summary>Model resize factors.</summary>
        public int resizeX = DefaultResize, resizeY = DefaultResize, resizeZ = DefaultResize;
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

        /// <summary>The opcode 249 entries exactly as the record stored them, in stream order.</summary>
        /// <remarks>
        ///     <see cref="itemParams"/> is sorted so lookups and the editor's UI see a stable
        ///     order, and it is a dictionary so a key can appear only once. The cache honours
        ///     neither constraint: parameter blocks are unsorted and a handful of items repeat a
        ///     key. Both facts are lost the moment the block lands in the dictionary, so they are
        ///     recorded here for <see cref="Encode"/> to put back.
        /// </remarks>
        public readonly List<KeyValuePair<int, object>> itemParamEntries =
            new List<KeyValuePair<int, object>>();

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

        /// <summary>Reads an item definition from the opcode stream at the stream's position.</summary>
        /// <remarks>
        ///     The stream is self-delimiting: nothing states how long a record is, so the payload
        ///     of every opcode has to be sized correctly or the read desynchronises for the rest
        ///     of the record. Each payload's bytes are kept alongside the opcode so
        ///     <see cref="Encode"/> can reproduce the record it was read from.
        /// </remarks>
        /// <param name="s">The stream to read from.</param>
        /// <param name="xteaKey">Unused; item definitions are not separately encrypted.</param>
        public void Decode(JagStream s, int[] xteaKey = null) {
            int safety = 0;

            while (true) {
                int op = s.ReadByte();
                if (op <= 0) break;                       // 0 = terminator, -1 = EOF

                int payloadStart = s.Position;
                DecodeOpcode(s, op);

                byte[] payload = new byte[s.Position - payloadStart];
                if (payload.Length > 0) {
                    s.Position = payloadStart;
                    s.Read(payload, 0, payload.Length);
                }

                opcodeOrder.Add(op);
                opcodePayloads.Add(payload);

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

                /* chathead models - the client pairs 90 with 92 (male) and 91 with 93 (female) */
                case 90: maleHeadModel1 = buf.ReadUnsignedShort(); return;
                case 91: femaleHeadModel1 = buf.ReadUnsignedShort(); return;
                case 92: maleHeadModel2 = buf.ReadUnsignedShort(); return;
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
                        //A repeated opcode 249 replaces the whole block, so the entries recorded
                        //for the previous one describe nothing any more.
                        itemParams = new SortedDictionary<int, object>();
                        itemParamEntries.Clear();
                        for (int i = 0 ; i < n ; i++) {
                            bool isStr = buf.ReadByte() == 1;
                            int key = buf.ReadMedium();
                            object val = isStr ? buf.ReadJagexString() : buf.ReadInt();
                            itemParamEntries.Add(new KeyValuePair<int, object>(key, val));
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

        /// <summary>
        ///     Writes this definition back out as the opcode stream the client reads.
        /// </summary>
        /// <remarks>
        ///     Opcodes the definition was decoded from are replayed first, in their original
        ///     order, and then any remaining field that is not at its default is appended in
        ///     ascending opcode order. Both halves matter: the first is what makes an untouched
        ///     definition re-encode to the bytes it came from, and the second is what lets a
        ///     field the editor sets on a definition that never carried that opcode still reach
        ///     the file.
        ///     <para>
        ///     Where an opcode was stored more than once only its final occurrence is rebuilt
        ///     from the fields, since that is the one the decoder let reach them; the earlier
        ///     occurrences are copied back byte for byte.
        ///     </para>
        /// </remarks>
        /// <returns>A flipped stream holding the encoded definition.</returns>
        public JagStream Encode() {
            var o = new JagStream();
            var written = new bool[256];

            var lastOccurrence = new int[256];
            for (int op = 0 ; op < lastOccurrence.Length ; op++)
                lastOccurrence[op] = -1;
            for (int i = 0 ; i < opcodeOrder.Count ; i++) {
                int op = opcodeOrder[i];
                if (op > 0 && op < 256)
                    lastOccurrence[op] = i;
            }

            for (int i = 0 ; i < opcodeOrder.Count ; i++) {
                int op = opcodeOrder[i];
                if (op <= 0 || op > 255)
                    continue;

                if (i == lastOccurrence[op]) {
                    written[op] = true;
                    EmitOpcode(o, op, true);
                    continue;
                }

                /* A bare flag - 11, 16 and 65 are the only zero-payload item opcodes - carries no
                   value, so a superseded occurrence of one is the same single byte as its last
                   occurrence and can be rebuilt from the field just as well. Rebuilding it is
                   what lets an edit that turns the flag off remove every copy: replayed verbatim,
                   the earlier copy would survive and the client would still read the flag as set,
                   so the row would change, the save would report success and the item would come
                   back members-only. */
                byte[] payload = i < opcodePayloads.Count ? opcodePayloads[i] : null;
                if (payload == null || payload.Length == 0) {
                    EmitOpcode(o, op, true);
                    continue;
                }

                //A superseded occurrence with a payload has no field left to rebuild it from, so
                //its bytes are all that is left of it.
                o.WriteByte((byte) op);
                o.Write(payload, 0, payload.Length);
            }

            for (int op = 1 ; op < 256 ; op++) {
                if (written[op])
                    continue;
                EmitOpcode(o, op, false);
            }

            /* terminator */
            o.WriteByte(0);
            return o.Flip();
        }

        /// <summary>
        ///     Writes one opcode and its payload, if this definition has anything to say with it.
        /// </summary>
        /// <remarks>
        ///     An opcode the record already carried is written back whatever its value, because
        ///     the revision-639 packer does store fields at their default - opcode 12 with a
        ///     value of 1 is common - and dropping them would shorten the record. An opcode the
        ///     record did not carry is written only when its field has moved off the value the
        ///     client assumes in its absence, so that saving an untouched item cannot grow it.
        /// </remarks>
        /// <param name="o">The stream to append to.</param>
        /// <param name="op">The opcode to consider writing.</param>
        /// <param name="stored">Whether the decoded record carried this opcode.</param>
        private void EmitOpcode(JagStream o, int op, bool stored) {
            void Emit(Action payload = null) {
                o.WriteByte((byte) op);
                payload?.Invoke();
            }

            switch (op) {
                /* model and basic */
                case 1: if (stored || inventoryModelId != 0) Emit(() => o.WriteShort(inventoryModelId)); return;
                case 2: if (name != null && (stored || name.Length > 0)) Emit(() => o.WriteJagexString(name)); return;
                case 4: if (stored || modelZoom != DefaultModelZoom) Emit(() => o.WriteShort(modelZoom)); return;
                case 5: if (stored || modelRotation1 != 0) Emit(() => o.WriteShort(modelRotation1)); return;
                case 6: if (stored || modelRotation2 != 0) Emit(() => o.WriteShort(modelRotation2)); return;
                case 7: if (stored || modelOffsetX != 0) Emit(() => o.WriteShort((short) modelOffsetX)); return;
                case 8: if (stored || modelOffsetY != 0) Emit(() => o.WriteShort((short) modelOffsetY)); return;

                /* stackable / value. Opcodes 11 and 16 are bare flags, so the field alone says
                   whether they belong in the stream. */
                case 11: if (stackable == 1) Emit(); return;
                case 12: if (stored || value != DefaultValue) Emit(() => o.WriteInteger(value)); return;
                case 16: if (membersOnly) Emit(); return;
                case 18: if (stored || multiStackSize != -1) Emit(() => o.WriteShort(multiStackSize)); return;

                /* worn models */
                case 23: if (stored || maleWearModel1 != 0) Emit(() => o.WriteShort(maleWearModel1)); return;
                case 24: if (stored || maleWearModel2 != 0) Emit(() => o.WriteShort(maleWearModel2)); return;
                case 25: if (stored || femaleWearModel1 != 0) Emit(() => o.WriteShort(femaleWearModel1)); return;
                case 26: if (stored || femaleWearModel2 != 0) Emit(() => o.WriteShort(femaleWearModel2)); return;

                /* ground / inventory menus. An option the record did not carry is compared
                   against the seeded default rather than against null: the decoder starts
                   groundOptions[2] at "take" and inventoryOptions[4] at "drop", so a null test
                   would write those two back for every item in the cache. */
                case int g when g >= 30 && g < 35: {
                        string option = groundOptions[g - 30];
                        if (option != null && (stored || option != DefaultGroundOptions[g - 30]))
                            Emit(() => o.WriteJagexString(option));
                        return;
                    }

                case int v when v >= 35 && v < 40: {
                        string option = inventoryOptions[v - 35];
                        if (option != null && (stored || option != DefaultInventoryOptions[v - 35]))
                            Emit(() => o.WriteJagexString(option));
                        return;
                    }

                /* recolour */
                case 40:
                    if (originalModelColors != null)
                        Emit(() => {
                            o.WriteByte((byte) originalModelColors.Length);
                            for (int i = 0 ; i < originalModelColors.Length ; i++) {
                                o.WriteShort(originalModelColors[i]);
                                o.WriteShort(modifiedModelColors[i]);
                            }
                        });
                    return;

                /* retexture */
                case 41:
                    if (textureColour1 != null)
                        Emit(() => {
                            o.WriteByte((byte) textureColour1.Length);
                            for (int i = 0 ; i < textureColour1.Length ; i++) {
                                o.WriteShort(textureColour1[i]);
                                o.WriteShort(textureColour2[i]);
                            }
                        });
                    return;

                /* texture priorities */
                case 42:
                    if (texturePriorities != null)
                        Emit(() => {
                            o.WriteByte((byte) texturePriorities.Length);
                            foreach (sbyte b in texturePriorities) o.WriteSignedByte(b);
                        });
                    return;

                /* GE tradeable */
                case 65: if (unnoted) Emit(); return;

                /* tertiary worn models */
                case 78: if (stored || maleWearModel3 != 0) Emit(() => o.WriteShort(maleWearModel3)); return;
                case 79: if (stored || femaleWearModel3 != 0) Emit(() => o.WriteShort(femaleWearModel3)); return;

                /* chathead models - the client pairs 90 with 92 (male) and 91 with 93 (female) */
                case 90: if (stored || maleHeadModel1 != 0) Emit(() => o.WriteShort(maleHeadModel1)); return;
                case 91: if (stored || femaleHeadModel1 != 0) Emit(() => o.WriteShort(femaleHeadModel1)); return;
                case 92: if (stored || maleHeadModel2 != 0) Emit(() => o.WriteShort(maleHeadModel2)); return;
                case 93: if (stored || femaleHeadModel2 != 0) Emit(() => o.WriteShort(femaleHeadModel2)); return;

                /* z-axis rotation */
                case 95: if (stored || zan2d != 0) Emit(() => o.WriteShort(zan2d)); return;

                /* dummy item */
                case 96: if (stored || dummyItem != 0) Emit(() => o.WriteByte((byte) dummyItem)); return;

                /* noted pair */
                case 97: if (stored || notedId != 0) Emit(() => o.WriteShort(notedId)); return;
                case 98: if (stored || notedTemplateId != 0) Emit(() => o.WriteShort(notedTemplateId)); return;

                /* stack variants 100-109 */
                case int s when s >= 100 && s < 110: {
                        int slot = s - 100;
                        if (stackIds != null && (stored || stackIds[slot] != 0))
                            Emit(() => {
                                o.WriteShort(stackIds[slot]);
                                o.WriteShort(stackAmounts[slot]);
                            });
                        return;
                    }

                /* model resize */
                case 110: if (stored || resizeX != DefaultResize) Emit(() => o.WriteShort(resizeX)); return;
                case 111: if (stored || resizeY != DefaultResize) Emit(() => o.WriteShort(resizeY)); return;
                case 112: if (stored || resizeZ != DefaultResize) Emit(() => o.WriteShort(resizeZ)); return;

                /* ambient / contrast / team */
                case 113: if (stored || ambient != 0) Emit(() => o.WriteSignedByte((sbyte) ambient)); return;
                case 114: if (stored || contrast != 0) Emit(() => o.WriteSignedByte((sbyte) (contrast / 5))); return;
                case 115: if (stored || teamId != 0) Emit(() => o.WriteByte((byte) teamId)); return;

                /* lending */
                case 121: if (stored || lendId != 0) Emit(() => o.WriteShort(lendId)); return;
                case 122: if (stored || lendTemplateId != 0) Emit(() => o.WriteShort(lendTemplateId)); return;

                /* wear offsets */
                case 125:
                    if (stored || manWearXOffset != 0 || manWearYOffset != 0 || manWearZOffset != 0)
                        Emit(() => {
                            o.WriteSignedByte((sbyte) (manWearXOffset >> 2));
                            o.WriteSignedByte((sbyte) (manWearYOffset >> 2));
                            o.WriteSignedByte((sbyte) (manWearZOffset >> 2));
                        });
                    return;

                case 126:
                    if (stored || womanWearXOffset != 0 || womanWearYOffset != 0 || womanWearZOffset != 0)
                        Emit(() => {
                            o.WriteSignedByte((sbyte) (womanWearXOffset >> 2));
                            o.WriteSignedByte((sbyte) (womanWearYOffset >> 2));
                            o.WriteSignedByte((sbyte) (womanWearZOffset >> 2));
                        });
                    return;

                /* cursor overrides. The -1 default cannot be written as an unsigned byte, so an
                   unset cursor has nothing to emit whether or not the record carried it. */
                case 127: if (cursor1Op >= 0) Emit(() => { o.WriteByte((byte) cursor1Op); o.WriteShort(cursor1Id); }); return;
                case 128: if (cursor2Op >= 0) Emit(() => { o.WriteByte((byte) cursor2Op); o.WriteShort(cursor2Id); }); return;
                case 129: if (cursor3Op >= 0) Emit(() => { o.WriteByte((byte) cursor3Op); o.WriteShort(cursor3Id); }); return;
                case 130: if (cursor4Op >= 0) Emit(() => { o.WriteByte((byte) cursor4Op); o.WriteShort(cursor4Id); }); return;
                case 131: if (cursor5Op >= 0) Emit(() => { o.WriteByte((byte) cursor5Op); o.WriteShort(cursor5Id); }); return;

                /* quest requirements */
                case 132:
                    if (quests != null)
                        Emit(() => {
                            o.WriteByte((byte) quests.Length);
                            foreach (int q in quests) o.WriteShort(q);
                        });
                    return;

                /* pick size shift */
                case 134: if (stored || pickSizeShift != 0) Emit(() => o.WriteByte((byte) pickSizeShift)); return;

                /* bind/shard pair */
                case 139: if (stored || bindId != 0) Emit(() => o.WriteShort(bindId)); return;
                case 140: if (stored || bindTemplateId != 0) Emit(() => o.WriteShort(bindTemplateId)); return;

                /* params */
                case 249:
                    if (itemParams != null)
                        Emit(() => WriteParams(o));
                    return;

                /* an opcode the format does not define carries no field to write */
                default: return;
            }
        }

        /// <summary>
        ///     Writes the opcode 249 parameter block in the order the record stored it.
        /// </summary>
        /// <remarks>
        ///     Values come from <see cref="itemParams"/> so an edit reaches the file, but only
        ///     for the first occurrence of a key: that is the one the decoder let into the
        ///     dictionary, so a repeated key's later values exist nowhere but
        ///     <see cref="itemParamEntries"/>. A key dropped from the dictionary is dropped from
        ///     the block, and a key added to it is appended after the recorded entries.
        /// </remarks>
        /// <param name="o">The stream to append to.</param>
        private void WriteParams(JagStream o) {
            var entries = new List<KeyValuePair<int, object>>();
            var seen = new HashSet<int>();

            foreach (var recorded in itemParamEntries) {
                if (!itemParams.TryGetValue(recorded.Key, out object current))
                    continue;
                entries.Add(new KeyValuePair<int, object>(
                    recorded.Key, seen.Add(recorded.Key) ? current : recorded.Value));
            }

            foreach (var kv in itemParams)
                if (!seen.Contains(kv.Key))
                    entries.Add(kv);

            o.WriteByte((byte) entries.Count);
            foreach (var kv in entries)
                WriteParam(o, kv.Key, kv.Value);
        }

        /// <summary>Writes one opcode 249 key-value pair.</summary>
        /// <param name="o">The stream to append to.</param>
        /// <param name="key">The parameter key, a 24 bit id.</param>
        /// <param name="param">The parameter value, a string or an int.</param>
        private static void WriteParam(JagStream o, int key, object param) {
            bool isStr = param is string;
            o.WriteByte((byte) (isStr ? 1 : 0));
            o.WriteMedium(key);
            if (isStr) o.WriteJagexString((string) param);
            else o.WriteInteger((int) param);
        }
    }
}

