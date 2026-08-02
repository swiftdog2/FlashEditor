using static FlashEditor.Utils.DebugUtil;
using System;
using System.Collections.Generic;
using System.Text;

namespace FlashEditor.Definitions
{
    /// <summary>
    /// Represents a single "loc" / world-object definition.
    /// Opcode layout matches the 633-official client (closest public reference to rev 639).
    /// For build &lt;670, readBigSmart() == readUnsignedShort().
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
        /// <summary>Unique object identifier.</summary>
        public int id;

        // ─── Name & menu strings ─────────────────────────
        /// <summary>Display name shown on right-click.</summary>
        public string name;
        /// <summary>Right-click menu options (opcodes 30-34).</summary>
        public string[] actions = new string[5];        // op-codes 30-34
        /// <summary>Additional menu options (opcodes 150-154).</summary>
        public string[] menuOps = new string[5];        // op-codes 150-154

        // ─── Geometry / render flags ─────────────────────
        /// <summary>East-west tile footprint (default 1).</summary>
        public byte sizeX = 1;          // op-code 14
        /// <summary>North-south tile footprint (default 1).</summary>
        public byte sizeY = 1;          // op-code 15
        /// <summary>Whether players can walk through this object.</summary>
        /// <remarks>
        /// A view over the walk-blocking opcodes rather than a field of its own. Nothing in the
        /// stream states walkability directly - opcodes 17 and 18 are bare flags whose presence
        /// blocks the tile - and the encoder emits those flags from the opcode hit map, so a
        /// standalone field would be read by the grid and then thrown away on save. Clearing the
        /// flag drops the opcode outright, which is why the recorded stream position goes too.
        /// </remarks>
        public bool walkable
        {
            get => !(decoded[17] || decoded[18]);
            set
            {
                if (value)
                {
                    DropOpcode(17);
                    DropOpcode(18);
                }
                else if (!decoded[17] && !decoded[18])
                {
                    //18 rather than 17: it blocks the tile without also resetting the clip type,
                    //so it is the narrower of the two claims to make on the user's behalf.
                    decoded[18] = true;
                }
            }
        }
        /*───────────────────────────────────────────*
         *  ▌  Bare flags (presence-only opcodes)   ▐
         *───────────────────────────────────────────*/
        /* Every flag below is a view over the opcode hit map, for the reason set out on
           <see cref="walkable"/>: the file states a flag only by carrying its opcode, no payload
           is involved, and Encode writes an opcode back because the definition carried it. A
           plain field would be read by the grid, edited, and then overruled by the replayed
           opcode on save - the row changes, the save reports success, the cache does not move.
           Turning a flag off has to call DropOpcode so the recorded stream forgets it too;
           turning it on sets the hit map, which is what Encode reads.

           Reading the value off the hit map also fixes what a definition that never carried the
           opcode reports: exactly the value the client assumes in its absence. That is why 64 is
           inverted - the shadow is cast unless the opcode says otherwise - and the rest are not. */

        /// <summary>Whether the object contributes to the collision map (opcode 22).</summary>
        public bool isClipped
        {
            get => decoded[22];
            set { if (value) decoded[22] = true; else DropOpcode(22); }
        }

        /// <summary>Whether the model is mirrored on the X axis (opcode 62).</summary>
        public bool flipped
        {
            get => decoded[62];
            set { if (value) decoded[62] = true; else DropOpcode(62); }
        }

        /// <summary>Whether the object casts a shadow (opcode 64 suppresses it).</summary>
        public bool castsShadow
        {
            get => !decoded[64];
            set { if (value) DropOpcode(64); else decoded[64] = true; }
        }

        /// <summary>Whether the object blocks a wheelchair route (opcode 73).</summary>
        public bool obstructsWheelchair
        {
            get => decoded[73];
            set { if (value) decoded[73] = true; else DropOpcode(73); }
        }

        /// <summary>Whether clipping is ignored on an alternative route (opcode 74).</summary>
        public bool isSolid
        {
            get => decoded[74];
            set { if (value) decoded[74] = true; else DropOpcode(74); }
        }

        /// <summary>Whether adjoining model normals are merged (opcode 82).</summary>
        public bool mergeNormals
        {
            get => decoded[82];
            set { if (value) decoded[82] = true; else DropOpcode(82); }
        }

        /// <summary>Whether the object is excluded from the shadow pass (opcode 88).</summary>
        public bool noShadow
        {
            get => decoded[88];
            set { if (value) decoded[88] = true; else DropOpcode(88); }
        }

        /// <summary>Whether the object suppresses decorative overlays (opcode 89).</summary>
        public bool noDecor
        {
            get => decoded[89];
            set { if (value) decoded[89] = true; else DropOpcode(89); }
        }

        /// <summary>Unnamed render flag, build-637 <c>aBoolean3870</c> (opcode 90).</summary>
        public bool unknownFlag90
        {
            get => decoded[90];
            set { if (value) decoded[90] = true; else DropOpcode(90); }
        }

        /// <summary>Unnamed render flag, build-637 <c>aBoolean3873</c> (opcode 91).</summary>
        public bool unknownFlag91
        {
            get => decoded[91];
            set { if (value) decoded[91] = true; else DropOpcode(91); }
        }

        /// <summary>Unnamed render flag, build-637 <c>aBoolean3924</c> (opcode 96).</summary>
        public bool unknownFlag96
        {
            get => decoded[96];
            set { if (value) decoded[96] = true; else DropOpcode(96); }
        }

        /// <summary>Unnamed render flag, build-637 <c>aBoolean3866</c> (opcode 97).</summary>
        public bool unknownFlag97
        {
            get => decoded[97];
            set { if (value) decoded[97] = true; else DropOpcode(97); }
        }

        /// <summary>Unnamed render flag, build-637 <c>aBoolean3923</c> (opcode 98).</summary>
        public bool unknownFlag98
        {
            get => decoded[98];
            set { if (value) decoded[98] = true; else DropOpcode(98); }
        }

        /// <summary>Unnamed render flag, build-637 <c>aBoolean3906</c> (opcode 105).</summary>
        public bool unknownFlag105
        {
            get => decoded[105];
            set { if (value) decoded[105] = true; else DropOpcode(105); }
        }

        /// <summary>Unnamed render flag, build-637 <c>aBoolean3894</c> (opcode 168).</summary>
        public bool unknownFlag168
        {
            get => decoded[168];
            set { if (value) decoded[168] = true; else DropOpcode(168); }
        }

        /// <summary>Unnamed render flag, build-637 <c>aBoolean3845</c> (opcode 169).</summary>
        public bool unknownFlag169
        {
            get => decoded[169];
            set { if (value) decoded[169] = true; else DropOpcode(169); }
        }

        /// <summary>Unnamed render flag (opcode 177).</summary>
        public bool unknownFlag177
        {
            get => decoded[177];
            set { if (value) decoded[177] = true; else DropOpcode(177); }
        }

        /// <summary>Unnamed render flag (opcode 189).</summary>
        public bool unknownFlag189
        {
            get => decoded[189];
            set { if (value) decoded[189] = true; else DropOpcode(189); }
        }
        /// <summary>
        /// Ambient lighting term for the 3D model - a read-only view over opcode 29.
        /// </summary>
        /// <remarks>
        /// A view rather than a field so the grid cannot show a number the encoder would never
        /// write: <see cref="Encode"/> emits opcode 29 from <c>ambientLighting</c> alone. It
        /// previously mirrored opcode 28 (<c>decorDisplacement</c>), which is neither a
        /// brightness nor a lighting value at all.
        /// </remarks>
        public int modelBrightness => ambientLighting;   // opcode 29
        /// <summary>
        /// Contrast lighting term for the 3D model - a read-only view over opcode 39.
        /// </summary>
        /// <remarks>
        /// See <see cref="modelBrightness"/>. This previously mirrored opcode 29, leaving the
        /// real contrast opcode unreadable from the grid.
        /// </remarks>
        public int modelContrast => contrastLighting;    // opcode 39

        /// <summary>
        /// The map scene icon this object draws on the minimap and world map, or -1 for none.
        /// </summary>
        /// <remarks>
        /// A read-only view over opcode <b>102</b>, whose private field is named
        /// <c>mapAreaId</c>. The name is a misnomer inherited from an early guess and the field is
        /// left alone rather than renamed, because the codec tests and the reference tables are
        /// written against it.
        ///
        /// The field spelled <see cref="mapSceneIdOpcode68"/> is <b>not</b> this. Opcode 68 is not
        /// handled by the 637 client at all - the else-if chain steps straight from 67 to 69
        /// (Class352.java:1106-1108) - and no definition in the 639 cache carries it. Opcode 102 is
        /// the one the client feeds to every mapscene draw site (Class122.java:92,
        /// Class277.java:121, Class278.java:872), and 3,267 definitions carry it.
        /// </remarks>
        public int mapSceneIcon => mapAreaId;            // opcode 102

        /// <summary>
        /// Opcode 68, which this codec reads and the 637 client does not. Almost certainly unused.
        /// </summary>
        /// <remarks>
        /// Exposed only so the discrepancy is visible rather than hidden behind a private field.
        /// Zero definitions in the 639 cache carry opcode 68. Use <see cref="mapSceneIcon"/>.
        /// </remarks>
        public int mapSceneIdOpcode68 => mapSceneId;     // opcode 68, dead

        /// <summary>
        /// The map element marker for this object, or -1 for none - a read-only view over opcode 107.
        /// </summary>
        /// <remarks>
        /// A different definition type from <see cref="mapSceneIcon"/>: map elements live in config
        /// group 36 (Class341.java:141) and drive world-map markers and labels, whereas mapscene
        /// icons live in group 34. 170 definitions carry it.
        /// </remarks>
        public int mapElementId => mapIconId;            // opcode 107

        // ───── misc metadata ──────────────────────────
        /// <summary>Object category grouping id.</summary>
        public byte category;          // opcode 19

        // ─── Model groups (op-codes 1 / 5) ───────────────
        /// <summary>Whether model data uses opcode 5 encoding instead of opcode 1.</summary>
        public bool usesOpcode5;                        // true → encode with 5, else 1
        /// <summary>Render type per model group.</summary>
        public sbyte[] modelTypes;                // per group
        /// <summary>Model ids per render type group.</summary>
        public ushort[][] modelIds;                  // per group

        // ─── Animation (op-code 24) ──────────────────────
        /// <summary>Default animation played by this object.</summary>
        public int animationId = -1;

        // ─── Scale (65-67) ─────────────────────────────
        /// <summary>Horizontal X scale factor (128 = normal).</summary>
        public int scaleX;         // 65  (anInt3902)
        /// <summary>Horizontal Y scale factor (128 = normal).</summary>
        public int scaleY;         // 66  (anInt3841)
        /// <summary>Vertical scale factor (128 = normal).</summary>
        public int scaleZ;         // 67  (anInt3917)

        // ─── Morph varbits / varps (77 / 92) ─────────────
        /// <summary>Varbit id for morph variant selection.</summary>
        public int morphVarbit = -1;
        /// <summary>Varp id for morph variant selection.</summary>
        public int morphVarp = -1;
        /// <summary>Array of object ids this object can morph into.</summary>
        public int[] morphIds;

        // ─── Sound / ambience (78 / 79) ──────────────────
        /// <summary>Looping sound effect id.</summary>
        public int ambientSoundId = -1;
        /// <summary>Sound repeat count (op 78).</summary>
        public int ambientSoundLoops;
        /// <summary>Secondary sound field (op 79).</summary>
        private int ambientSoundExtra;
        /// <summary>Additional sound effect ids.</summary>
        public int[] extraSounds;

        // ─── Colour / texture swaps (40 / 41) ────────────
        /// <summary>Source and destination HSL colour replacement pairs.</summary>
        public short[] recolSrc, recolDst;
        /// <summary>Source and destination texture replacement pairs.</summary>
        public short[] retexSrc, retexDst;

        // ─── Minimap icons (op-code 160) ────────────────
        /// <summary>Minimap icon sprite ids.</summary>
        public ushort[] minimapIcons;

        // ─── Params (op-code 249) ───────────────────────
        /// <summary>Arbitrary key-value parameters (opcode 249).</summary>
        public SortedDictionary<int, object> parameters;

        // ─── Internal state fields ──────────────────────
        private int clipType = 2;           // 17 / 27
        private bool projectileClipped = true; // 17 / 18
        private byte contourGroundType;     // 21 / 81 / 93 / 94 / 95 / 162
        private int contourGroundParam = -1;// 81 / 93 / 95 / 162
        private int obstructsGround = -1;   // 23 / 103
        private bool randomAnimStart;       // 27
        private sbyte[] texturePriorities;  // 42
        private int mapSceneId = -1;        // 68  (not parsed by the 637 client; absent from the 639 cache)
        private byte minimapForceClip;      // 69  (cflag)
        private int offsetX;                // 70
        private int offsetY;                // 71
        private int offsetZ;                // 72  (anInt2946, signed short << 2)
        private int unknownByte75;          // 75  (anInt2975, 1 UByte - not a flag)
        private int decorDisplacement;      // 28  (anInt3892)
        private int ambientLighting;        // 29  (anInt3878)
        private int contrastLighting;       // 39  (anInt3840)
        private byte[] unknownArray3;       // 44
        private byte[] unknownArray4;       // 45
        private int cursorType1;            // 99  (anInt3857 = UByte)
        private int cursorSprite1;          // 99  (anInt3835 = UShort)
        private int cursorType2;            // 100 (anInt3844 = UByte)
        private int cursorSprite2;          // 100 (anInt3913 = UShort)
        private int ambientVolume;          // 101 (anInt3865)
        private int mapAreaId = -1;         // 102 (anInt3838) - the MAP SCENE icon id; client default -1 at Class352.java:266
        private int soundVolume;            // 104 (anInt3850)
        private int[] animationIds;         // 106 (animations[])
        private int[] animationWeights;     // 106 (anIntArray3869[])
        private int mapIconId = -1;         // 107 (anInt3851) - client default -1 at Class352.java:256
        private int unknownInt162;          // 162 (4-byte int)
        private sbyte unknownByte163a;      // 163
        private sbyte unknownByte163b;      // 163
        private sbyte unknownByte163c;      // 163
        private sbyte unknownByte163d;      // 163
        private int unknownShort164;        // 164 (anInt3834)
        private int unknownShort165;        // 165 (anInt3875)
        private int unknownShort166;        // 166 (anInt3877)
        private int unknownShort167;        // 167 (anInt3921)
        private int unknownSmart170;        // 170
        private int unknownSmart171;        // 171
        private int unknownShort173a;       // 173
        private int unknownShort173b;       // 173
        private int unknownByte178;         // 178
        private int[] extraOpcodeArray;     // 190-195

        /// <summary>Raw bytes of the extra model group block from opcode 5 (for round-trip encoding).</summary>
        private byte[] _op5ExtraRaw;

        // ─── Misc bookkeeping ───────────────────────────
        /// <summary>Diagnostic flag array tracking which opcodes were read.</summary>
        public bool[] decoded = new bool[256];  // opcode hit-map

        /// <summary>
        /// Every opcode the stream carried, in order, paired with the exact payload bytes it was
        /// read from.
        /// </summary>
        /// <remarks>
        /// Nothing in the format fixes an opcode order, and the definitions shipped in the cache
        /// are not in ascending order, so an encoder that emitted its own order would rewrite
        /// every definition the user merely opened. Replaying the order the decoder saw is what
        /// makes an untouched definition re-encode to the bytes it came from.
        /// <para>
        /// The payload is kept as well because a few hundred definitions repeat an opcode with a
        /// different value each time. The decoder keeps only the last value, as the client does,
        /// so the earlier occurrences can only be reproduced from the bytes they were read from.
        /// </para>
        /// </remarks>
        private List<KeyValuePair<int, byte[]>> _streamRecords =
            new List<KeyValuePair<int, byte[]>>();

        /*───────────────────────────────────────────*
         *  ▌  Clone support                        ▐
         *───────────────────────────────────────────*/
        /// <summary>Takes an independent copy of this definition.</summary>
        /// <remarks>
        /// The editor clones a definition to hold what it looked like before an edit, so the two
        /// cannot share the opcode hit map or the recorded stream: turning a flag off writes to
        /// both, and the snapshot would then agree with the edit it exists to remember.
        /// </remarks>
        /// <returns>A copy whose bookkeeping the original does not share.</returns>
        public ObjectDefinition Clone()
        {
            var clone = (ObjectDefinition)MemberwiseClone();
            clone.decoded = (bool[])decoded.Clone();
            clone._streamRecords = new List<KeyValuePair<int, byte[]>>(_streamRecords);
            return clone;
        }

        object ICloneable.Clone() => Clone();

        /*───────────────────────────────────────────*
         *  ▌  Public decode entry-point            ▐
         *───────────────────────────────────────────*/
        public void Decode(JagStream stream, int[] xteaKey = null)
        {
            int safeGuard = 0;
            SharedBuilder.Clear();

            while (true)
            {
                int op = stream.ReadByte();
                if (op <= 0) break;          // 0 = terminator, -1 = EOF
                decoded[op] = true;

                int payloadStart = stream.Position;
                Decode(stream, op);
                int payloadEnd = stream.Position;
                stream.Position = payloadStart;
                _streamRecords.Add(new KeyValuePair<int, byte[]>(op, stream.ReadBytes(payloadEnd - payloadStart)));

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
                        int groupCt = buf.ReadByte();
                        modelTypes = new sbyte[groupCt];
                        modelIds = new ushort[groupCt][];

                        for (int g = 0; g < groupCt; g++)
                        {
                            modelTypes[g] = buf.ReadSignedByte();
                            int modelCt = buf.ReadByte();
                            var ids = new ushort[modelCt];
                            for (int m = 0; m < modelCt; m++) ids[m] = (ushort)buf.ReadUnsignedShort();
                            modelIds[g] = ids;
                        }

                        // Opcode 5 has an additional model group block that must be consumed.
                        // In the 633 client, aBoolean1162 is always false, so this always runs for op 5.
                        if (op == 5)
                        {
                            int startPos = buf.Position;
                            SkipReadModelIds(buf);
                            int endPos = buf.Position;
                            // Save raw bytes for round-trip encoding
                            int len = endPos - startPos;
                            buf.Position = startPos;
                            _op5ExtraRaw = buf.ReadBytes(len);
                        }
                        break;
                    }

                /*──────── scalar flags ────────*/
                case 2: name = buf.ReadJagexString(); break;
                case 14: sizeX = (byte) buf.ReadByte(); break;
                case 15: sizeY = (byte) buf.ReadByte(); break;

                //Both flags block walking; that is read back off the opcode hit map rather than
                //stored, so these cases only carry the side effects the flags also have.
                case 17:
                    clipType = 0;
                    projectileClipped = false;
                    break;
                case 18:
                    projectileClipped = false;
                    break;

                // category/id-grouping
                case 19:
                    category = (byte) buf.ReadByte();
                    return;

                /* ─────────────── clip & contour flags ─────────────── */
                case 21: contourGroundType = 1; return;             // flag only (0 bytes)

                /* The bare flags - 22, 62, 64, 73, 74, 82, 88-91, 96-98, 105, 168, 169, 177 and
                   189 - have no payload and no case body. Their properties read straight off the
                   opcode hit map, which the decode loop has already written, so there is nothing
                   left to assign. Going through the property here would be worse than redundant:
                   its setter drops opcodes from the recorded stream. */
                case 22: return;                                    // isClipped (aBoolean3867)
                case 23: obstructsGround = 1; return;              // flag only (thirdInt = 1)
                case 24: animationId = buf.ReadUnsignedShort(); return; // readBigSmart = UShort for <670
                case 27: clipType = 1; return;                     // flag only
                case 28: decorDisplacement = buf.ReadByte() << 2; break; // 1 byte
                case 29: ambientLighting = buf.ReadSignedByte(); break; // 1 byte

                /*──────── action strings 30-34 ────────*/
                case int a when (a >= 30 && a < 35):
                    actions[a - 30] = buf.ReadJagexString();
                    break;

                /*──────── 39: contrast ────────*/
                case 39:
                    contrastLighting = buf.ReadSignedByte() * 5;    // 1 signed byte
                    break;

                /*──────── recolour 40 ────────*/
                case 40:
                    {
                        int n = buf.ReadByte();
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
                        int n = buf.ReadByte();
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
                        int n = buf.ReadByte();
                        texturePriorities = new sbyte[n];
                        for (int i = 0; i < n; i++)
                            texturePriorities[i] = buf.ReadSignedByte();
                        return;
                    }

                /*──────── 44 / 45: bitmap-decoded byte arrays ────────*/
                case 44:
                    {
                        int bits = (short)buf.ReadUnsignedShort();  // 2 bytes
                        int count = 0;
                        for (int tmp = bits; tmp > 0; tmp >>= 1) count++;
                        unknownArray3 = new byte[count];
                        byte idx = 0;
                        for (int i = 0; i < count; i++)
                        {
                            if ((bits & (1 << i)) > 0) unknownArray3[i] = idx++;
                            else unknownArray3[i] = 0xFF; // -1 as unsigned byte
                        }
                        break;
                    }
                case 45:
                    {
                        int bits = (short)buf.ReadUnsignedShort();  // 2 bytes
                        int count = 0;
                        for (int tmp = bits; tmp > 0; tmp >>= 1) count++;
                        unknownArray4 = new byte[count];
                        byte idx = 0;
                        for (int i = 0; i < count; i++)
                        {
                            if ((bits & (1 << i)) > 0) unknownArray4[i] = idx++;
                            else unknownArray4[i] = 0xFF;
                        }
                        break;
                    }

                /* ─────────────── render-side flags ─────────────── */
                case 62: return;                                    // flipped - mirror on X
                case 64: return;                                    // castsShadow off
                case 65: scaleX = buf.ReadUnsignedShort(); return;  // 2 bytes
                case 66: scaleY = buf.ReadUnsignedShort(); return;  // 2 bytes
                case 67: scaleZ = buf.ReadUnsignedShort(); return;  // 2 bytes
                case 68: mapSceneId = buf.ReadUnsignedShort(); return; // 2 bytes (may not be in 633, kept for safety)
                case 69: minimapForceClip = (byte) buf.ReadByte(); return;  // 1 byte

                /* signed short offsets */
                case 70: offsetX = buf.ReadShort() << 2; return;    // 2 bytes
                case 71: offsetY = buf.ReadShort() << 2; return;    // 2 bytes
                case 72: offsetZ = buf.ReadShort() << 2; return;    // 2 bytes (anInt2946)

                case 73: return;                                    // obstructsWheelchair
                case 74: return;                                    // isSolid
                case 75: unknownByte75 = buf.ReadUnsignedByte(); return; // 1 byte (anInt2975)

                /*──────── morph (77 / 92) ────────*/
                case 77:
                case 92:
                    {
                        morphVarbit = SmartOrMinus1(buf);
                        morphVarp = SmartOrMinus1(buf);
                        int defaultId = (op == 92) ? SmartOrMinus1(buf) : -1;

                        int ct = buf.ReadByte();
                        morphIds = new int[ct + 2];
                        for (int i = 0; i <= ct; i++) morphIds[i] = SmartOrMinus1(buf);
                        morphIds[ct + 1] = defaultId;
                        break;
                    }

                /*──────── sounds 78 / 79 ────────*/
                case 78:
                    {
                        ambientSoundId = buf.ReadUnsignedShort();   // 2 bytes
                        ambientSoundLoops = buf.ReadByte();         // 1 byte
                        break;
                    }
                case 79:
                    {
                        ambientSoundId = buf.ReadUnsignedShort();   // 2 bytes (anInt3900)
                        ambientSoundExtra = buf.ReadUnsignedShort();// 2 bytes (anInt3905) — was missing!
                        ambientSoundLoops = buf.ReadUnsignedByte(); // 1 byte  (anInt3904)
                        int count = buf.ReadByte();                 // 1 byte
                        extraSounds = new int[count];
                        for (int i = 0; i < count; i++)
                            extraSounds[i] = buf.ReadUnsignedShort();
                        break;
                    }

                /*──────── contour ground variants (81 / 93 / 94 / 95) ───*/
                case 81:
                    contourGroundType = 2;
                    contourGroundParam = 256 * buf.ReadByte();      // 1 byte
                    break;
                case 82: break;                                     // mergeNormals
                case 88: break;                                     // noShadow (aBoolean3853)
                case 89: break;                                     // noDecor (aBoolean3895)
                case 90: break;                                     // unknownFlag90
                case 91: break;                                     // unknownFlag91
                case 93:
                    contourGroundType = 3;
                    contourGroundParam = buf.ReadUnsignedShort();    // 2 bytes
                    break;
                case 94: contourGroundType = 4; break;              // flag only
                case 95:
                    contourGroundType = 5;
                    contourGroundParam = buf.ReadShort();            // 2 bytes (signed)
                    break;
                case 96: break;                                     // unknownFlag96
                case 97: break;                                     // unknownFlag97
                case 98: break;                                     // unknownFlag98

                /*──────── cursor overrides (99 / 100) ────────*/
                case 99:
                    cursorType1 = buf.ReadByte();                   // 1 byte
                    cursorSprite1 = buf.ReadUnsignedShort();        // 2 bytes
                    break;
                case 100:
                    cursorType2 = buf.ReadByte();                   // 1 byte
                    cursorSprite2 = buf.ReadUnsignedShort();        // 2 bytes
                    break;

                /*──────── misc single-field opcodes ────────*/
                case 101: ambientVolume = buf.ReadByte(); break;       // 1 byte
                case 102: mapAreaId = buf.ReadUnsignedShort(); break;  // 2 bytes
                case 103: obstructsGround = 0; break;                 // flag only (thirdInt = 0)
                case 104: soundVolume = buf.ReadByte(); break;         // 1 byte
                case 105: break;                                       // unknownFlag105

                /*──────── animation table (106) ────────*/
                case 106:
                    {
                        int count = buf.ReadByte();
                        animationIds = new int[count];
                        animationWeights = new int[count];
                        for (int i = 0; i < count; i++)
                        {
                            animationIds[i] = buf.ReadUnsignedShort(); // readBigSmart = UShort for <670
                            animationWeights[i] = buf.ReadByte();
                        }
                        break;
                    }

                /*──────── map icon (107) ────────*/
                case 107: mapIconId = buf.ReadUnsignedShort(); break;  // 2 bytes

                /*──────── menuOps 150-154 ────────*/
                case int m when (m >= 150 && m < 155):
                    menuOps[m - 150] = buf.ReadJagexString();
                    break;

                /*──────── minimap icons (160) ─────*/
                case 160:
                    {
                        int n = buf.ReadByte();
                        minimapIcons = new ushort[n];
                        for (int i = 0; i < n; i++) minimapIcons[i] = (ushort)buf.ReadUnsignedShort();
                        break;
                    }

                /*──────── extended opcodes (162-195) ────────*/
                case 162:
                    contourGroundType = 3;
                    unknownInt162 = buf.ReadInt();                   // 4 bytes
                    break;
                case 163:
                    unknownByte163a = buf.ReadSignedByte();          // 1 byte
                    unknownByte163b = buf.ReadSignedByte();          // 1 byte
                    unknownByte163c = buf.ReadSignedByte();          // 1 byte
                    unknownByte163d = buf.ReadSignedByte();          // 1 byte
                    break;
                case 164: unknownShort164 = buf.ReadShort(); break; // 2 bytes (signed)
                case 165: unknownShort165 = buf.ReadShort(); break; // 2 bytes (signed)
                case 166: unknownShort166 = buf.ReadShort(); break; // 2 bytes (signed)
                case 167: unknownShort167 = buf.ReadUnsignedShort(); break; // 2 bytes
                case 168: break;                                    // unknownFlag168
                case 169: break;                                    // unknownFlag169
                case 170: unknownSmart170 = buf.ReadUnsignedSmart(); break; // 1-2 bytes
                case 171: unknownSmart171 = buf.ReadUnsignedSmart(); break; // 1-2 bytes
                case 173:
                    unknownShort173a = buf.ReadUnsignedShort();     // 2 bytes
                    unknownShort173b = buf.ReadUnsignedShort();     // 2 bytes
                    break;
                case 177: break;                                    // unknownFlag177
                case 178: unknownByte178 = buf.ReadByte(); break;   // 1 byte
                case 189: break;                                    // unknownFlag189

                case int e when (e >= 190 && e < 196):
                    {
                        if (extraOpcodeArray == null)
                        {
                            extraOpcodeArray = new int[6];
                            Array.Fill(extraOpcodeArray, -1);
                        }
                        extraOpcodeArray[e - 190] = buf.ReadUnsignedShort(); // 2 bytes each
                        break;
                    }

                /*──────── arbitrary params (249) ───*/
                case 249:
                    {
                        int len = buf.ReadByte();
                        parameters = new SortedDictionary<int, object>();

                        for (int i = 0; i < len; i++)
                        {
                            bool isString = buf.ReadByte() == 1;
                            int key = buf.ReadMedium();
                            object value = isString ? (object)buf.ReadJagexString() : buf.ReadInt();
                            if (!parameters.ContainsKey(key)) parameters.Add(key, value);
                        }
                        break;
                    }

                /*──────── unhandled opcodes ────────*/
                default:
                    Debug($"ObjectDef {id}: unhandled opcode {op} at pos {buf.Position}", LOG_DETAIL.BASIC);
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

        /// <summary>
        /// Skips over an opcode-5 extra model-group block.
        /// Format: UByte groupCount, then per group: skip 1 byte + UByte modelCount + UShort per model.
        /// </summary>
        private static void SkipReadModelIds(JagStream buf)
        {
            int length = buf.ReadByte();
            for (int i = 0; i < length; i++)
            {
                buf.Skip(1); // type byte
                int modelCount = buf.ReadByte();
                for (int m = 0; m < modelCount; m++)
                    buf.ReadUnsignedShort(); // readBigSmart = UShort for <670
            }
        }

        /*───────────────────────────────────────────*
         *  ▌  Encode back to binary                 ▐
         *───────────────────────────────────────────*/
        public JagStream Encode()
        {
            /* Each opcode's payload is built into its own buffer first, so the records can then
               be laid down in whatever order the definition arrived in. `o` is reassigned around
               each payload rather than passed in because every payload below closes over it. */
            var records = new List<KeyValuePair<int, byte[]>>();
            var o = new JagStream();

            /* local helper */
            void Emit(int op, Action payload = null)
            {
                JagStream outer = o;
                var buffer = new JagStream();
                o = buffer;
                payload?.Invoke();
                o = outer;
                records.Add(new KeyValuePair<int, byte[]>(op, buffer.Flip().ToArray()));
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
                    // Opcode 5: write the extra model group block
                    if (usesOpcode5 && _op5ExtraRaw != null)
                        o.Write(_op5ExtraRaw);
                    else if (usesOpcode5)
                        o.WriteByte(0); // empty extra block (0 groups)
                });
            }

            /* Each block below emits when the stream carried the opcode OR when the field says it
               is needed. The hit-map arm is what keeps an opcode whose payload happens to equal
               the field's default - a stored "19 00" or "70 00 00" - instead of silently dropping
               it and changing the definition's bytes the first time the user saves. */

            /*─── 2 – name ──────────────────────────────────────────────*/
            if (decoded[2] || !string.IsNullOrEmpty(name))
                Emit(2, () => o.WriteJagexString(name ?? ""));

            /*─── size (14 / 15) ───────────────────────────────────────*/
            if (decoded[14] || sizeX != 1) Emit(14, () => o.WriteByte(sizeX));
            if (decoded[15] || sizeY != 1) Emit(15, () => o.WriteByte(sizeY));

            /*─── walk-blocking flags (17 / 18) ────────────────────────*/
            if (decoded[17]) Emit(17);
            else if (decoded[18]) Emit(18);

            /*─── 19 – category id ─────────────────────────────────────*/
            if (decoded[19] || category != 0)
                Emit(19, () => o.WriteByte(category));

            /*─── 21 – contour ground type 1 (flag only) ──────────────*/
            /* Every bare flag is emitted from the hit map alone. The properties behind them are
               views over that map, so a field test would be the same question asked twice; more
               to the point, clearing one drops the opcode from the map AND from the recorded
               stream, which is the only thing that stops WriteRecordsInStreamOrder replaying a
               flag the user has just turned off. */
            if (decoded[21]) Emit(21);
            if (decoded[22]) Emit(22);                  // isClipped
            if (decoded[23]) Emit(23);
            if (decoded[24] || animationId != -1) Emit(24, () => o.WriteShort(animationId));

            /*─── 27 – clip type 1 ─────────────────────────────────────*/
            if (decoded[27]) Emit(27);

            /*─── decor displacement / ambient lighting (28 / 29) ─────*/
            if (decoded[28])
                Emit(28, () => o.WriteByte((byte)(decorDisplacement >> 2)));
            if (decoded[29])
                Emit(29, () => o.WriteSignedByte((sbyte)ambientLighting));

            /*─── action strings 30-34 ────────────────────────────────*/
            for (int i = 0; i < actions.Length; i++)
                if (actions[i] != null)
                    Emit(30 + i, () => o.WriteJagexString(actions[i]));

            /*─── 39 – contrast lighting ──────────────────────────────*/
            if (decoded[39])
                Emit(39, () => o.WriteSignedByte((sbyte)(contrastLighting / 5)));

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

            /*─── 44 / 45: bitmap arrays ──────────────────────────────*/
            if (decoded[44] && unknownArray3 != null)
                Emit(44, () => o.WriteShort(EncodeBitmapArray(unknownArray3)));
            if (decoded[45] && unknownArray4 != null)
                Emit(45, () => o.WriteShort(EncodeBitmapArray(unknownArray4)));

            /*─── render-side flags 62-75 ─────────────────────────────*/
            if (decoded[62]) Emit(62);                  // flipped
            if (decoded[64]) Emit(64);                  // castsShadow off
            if (decoded[65] || scaleX != 0) Emit(65, () => o.WriteShort(scaleX));
            if (decoded[66] || scaleY != 0) Emit(66, () => o.WriteShort(scaleY));
            if (decoded[67] || scaleZ != 0) Emit(67, () => o.WriteShort(scaleZ));
            if (decoded[68]) Emit(68, () => o.WriteShort(mapSceneId));
            if (decoded[69] || minimapForceClip != 0)
                Emit(69, () => o.WriteByte(minimapForceClip));

            if (decoded[70] || offsetX != 0) Emit(70, () => o.WriteShort((short)(offsetX >> 2)));
            if (decoded[71] || offsetY != 0) Emit(71, () => o.WriteShort((short)(offsetY >> 2)));
            if (decoded[72]) Emit(72, () => o.WriteShort((short)(offsetZ >> 2)));

            if (decoded[73]) Emit(73);                  // obstructsWheelchair
            if (decoded[74]) Emit(74);                  // isSolid
            if (decoded[75]) Emit(75, () => o.WriteByte((byte)unknownByte75));

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
            if (decoded[78] || decoded[79] || ambientSoundId != -1)
            {
                /* This codec writes both opcodes into the same fields, and 81 definitions in the
                   cache carry both, so only the one the decoder read last still has its values in
                   the fields. The other is replayed from its own bytes by WriteRecordsInStreamOrder,
                   which is what keeps those 81 byte-exact.
                   The conflation is ours, not the format's: the 637 client reads 78 and 79 into
                   two distinct fields and forwards them to the sound emitter as separate slots.
                   So the bytes survive a round trip, but editing ambient sound on one of those 81
                   reaches only the later opcode. See reference/hydra-637-definitions/object-opcodes.md. */
                bool use79 = LastStreamIndexOf(79) > LastStreamIndexOf(78)
                             || (!decoded[78] && !decoded[79] && extraSounds != null);

                if (!use79)
                    Emit(78, () =>
                    {
                        o.WriteShort(ambientSoundId);
                        o.WriteByte((byte)ambientSoundLoops);
                    });
                else
                    Emit(79, () =>
                    {
                        o.WriteShort(ambientSoundId);
                        o.WriteShort(ambientSoundExtra);
                        o.WriteByte((byte)ambientSoundLoops);
                        o.WriteByte((byte)extraSounds.Length);
                        foreach (int s in extraSounds) o.WriteShort(s);
                    });
            }

            /*─── contour ground variants (81 / 93 / 94 / 95) ────────*/
            if (decoded[81]) Emit(81, () => o.WriteByte((byte)(contourGroundParam / 256)));
            if (decoded[82]) Emit(82);
            if (decoded[88]) Emit(88);
            if (decoded[89]) Emit(89);
            if (decoded[90]) Emit(90);
            if (decoded[91]) Emit(91);
            if (decoded[93]) Emit(93, () => o.WriteShort(contourGroundParam));
            if (decoded[94]) Emit(94);
            if (decoded[95]) Emit(95, () => o.WriteShort(contourGroundParam));
            if (decoded[96]) Emit(96);
            if (decoded[97]) Emit(97);
            if (decoded[98]) Emit(98);

            /*─── cursor overrides (99 / 100) ─────────────────────────*/
            if (decoded[99]) Emit(99, () => { o.WriteByte((byte)cursorType1); o.WriteShort(cursorSprite1); });
            if (decoded[100]) Emit(100, () => { o.WriteByte((byte)cursorType2); o.WriteShort(cursorSprite2); });

            /*─── misc single-field opcodes ───────────────────────────*/
            if (decoded[101]) Emit(101, () => o.WriteByte((byte)ambientVolume));
            if (decoded[102]) Emit(102, () => o.WriteShort(mapAreaId));
            if (decoded[103]) Emit(103);
            if (decoded[104]) Emit(104, () => o.WriteByte((byte)soundVolume));
            if (decoded[105]) Emit(105);

            /*─── animation table (106) ───────────────────────────────*/
            if (decoded[106] && animationIds != null)
                Emit(106, () =>
                {
                    o.WriteByte((byte)animationIds.Length);
                    for (int i = 0; i < animationIds.Length; i++)
                    {
                        o.WriteShort(animationIds[i]);
                        o.WriteByte((byte)animationWeights[i]);
                    }
                });

            /*─── map icon (107) ──────────────────────────────────────*/
            if (decoded[107]) Emit(107, () => o.WriteShort(mapIconId));

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

            /*─── extended opcodes (162-195) ─────────────────────────*/
            if (decoded[162]) Emit(162, () => o.WriteInteger(unknownInt162));
            if (decoded[163]) Emit(163, () =>
            {
                o.WriteSignedByte(unknownByte163a);
                o.WriteSignedByte(unknownByte163b);
                o.WriteSignedByte(unknownByte163c);
                o.WriteSignedByte(unknownByte163d);
            });
            if (decoded[164]) Emit(164, () => o.WriteShort(unknownShort164));
            if (decoded[165]) Emit(165, () => o.WriteShort(unknownShort165));
            if (decoded[166]) Emit(166, () => o.WriteShort(unknownShort166));
            if (decoded[167]) Emit(167, () => o.WriteShort(unknownShort167));
            if (decoded[168]) Emit(168);
            if (decoded[169]) Emit(169);
            if (decoded[170]) Emit(170, () => o.WriteUnsignedSmart(unknownSmart170));
            if (decoded[171]) Emit(171, () => o.WriteUnsignedSmart(unknownSmart171));
            if (decoded[173]) Emit(173, () => { o.WriteShort(unknownShort173a); o.WriteShort(unknownShort173b); });
            if (decoded[177]) Emit(177);
            if (decoded[178]) Emit(178, () => o.WriteByte((byte)unknownByte178));
            if (decoded[189]) Emit(189);

            if (extraOpcodeArray != null)
                for (int i = 0; i < 6; i++)
                    if (decoded[190 + i])
                        Emit(190 + i, () => o.WriteShort(extraOpcodeArray[i]));

            /*─── params (249) ───────────────────────────────────────*/
            if (decoded[249] || (parameters != null && parameters.Count > 0))
                Emit(249, () =>
                {
                    o.WriteByte((byte)(parameters?.Count ?? 0));
                    foreach (var kv in parameters ?? new SortedDictionary<int, object>())
                    {
                        bool isStr = kv.Value is string;
                        o.WriteByte((byte)(isStr ? 1 : 0));
                        o.WriteMedium(kv.Key);
                        if (isStr) o.WriteJagexString((string)kv.Value);
                        else o.WriteInteger((int)kv.Value);
                    }
                });

            return WriteRecordsInStreamOrder(records);
        }

        /// <summary>
        /// Forgets an opcode entirely, so neither the hit map nor the recorded stream order will
        /// put it back on the next encode.
        /// </summary>
        /// <remarks>
        /// Clearing <see cref="decoded"/> alone is not enough: an opcode still listed in
        /// <see cref="_streamRecords"/> is written back from the bytes it was read from, which is
        /// what keeps repeated opcodes byte-exact but would also resurrect a flag the user just
        /// turned off.
        /// </remarks>
        /// <param name="op">The opcode to remove.</param>
        private void DropOpcode(int op)
        {
            decoded[op] = false;
            _streamRecords.RemoveAll(record => record.Key == op);
        }

        /// <summary>
        /// Where an opcode last appeared in the decoded stream, or -1 when it never did.
        /// </summary>
        /// <param name="op">The opcode to look for.</param>
        /// <returns>The index into <see cref="_streamRecords"/>, or -1.</returns>
        private int LastStreamIndexOf(int op)
        {
            for (int i = _streamRecords.Count - 1; i >= 0; i--)
                if (_streamRecords[i].Key == op)
                    return i;
            return -1;
        }

        /// <summary>
        /// Lays the encoded opcode records down in the order the definition was decoded in, then
        /// appends anything the decoder never saw.
        /// </summary>
        /// <remarks>
        /// A freshly encoded record with no place in <see cref="_streamRecords"/> is one the field
        /// values asked for but the original stream did not carry - a value the user set on a
        /// definition that arrived without that opcode. Appending it keeps such an edit rather
        /// than dropping it, which is what the value-driven encoder above did before the order was
        /// recorded.
        /// <para>
        /// Only the last occurrence of an opcode takes the freshly encoded payload, because that
        /// is the occurrence whose value the decoder kept and therefore the only one an edit can
        /// have changed. Every earlier occurrence, and any opcode the field-driven pass declined
        /// to re-emit at all - opcode 78 on a definition that also carries 79, for instance - is
        /// written back from the bytes it was read from.
        /// </para>
        /// </remarks>
        /// <param name="records">Each opcode and the payload bytes freshly encoded for it.</param>
        /// <returns>The complete definition stream, terminator included, ready to read.</returns>
        private JagStream WriteRecordsInStreamOrder(List<KeyValuePair<int, byte[]>> records)
        {
            var o = new JagStream();
            var encoded = new Dictionary<int, byte[]>(records.Count);
            foreach (KeyValuePair<int, byte[]> record in records)
                encoded[record.Key] = record.Value;

            var lastOccurrence = new Dictionary<int, int>();
            for (int i = 0; i < _streamRecords.Count; i++)
                lastOccurrence[_streamRecords[i].Key] = i;

            var replaced = new HashSet<int>();

            void Put(int op, byte[] payload)
            {
                o.WriteByte((byte)op);
                if (payload.Length > 0)
                    o.Write(payload);
            }

            for (int i = 0; i < _streamRecords.Count; i++)
            {
                int op = _streamRecords[i].Key;

                if (lastOccurrence[op] == i && encoded.TryGetValue(op, out byte[] fresh))
                {
                    Put(op, fresh);
                    replaced.Add(op);
                }
                else
                {
                    Put(op, _streamRecords[i].Value);
                }
            }

            foreach (KeyValuePair<int, byte[]> record in records)
                if (!replaced.Contains(record.Key))
                    Put(record.Key, record.Value);

            /* terminator */
            o.WriteByte(0);
            return o.Flip();
        }

        /// <summary>
        /// Re-encodes a bitmap byte array back to the UShort bitmask used by opcodes 44/45.
        /// </summary>
        private static short EncodeBitmapArray(byte[] arr)
        {
            int bits = 0;
            for (int i = 0; i < arr.Length; i++)
                if (arr[i] != 0xFF) bits |= (1 << i);
            return (short)bits;
        }
    }
}
