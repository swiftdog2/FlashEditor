using System;
using System.Collections.Generic;
using FlashEditor.Definitions;

namespace FlashEditor {
    /// <summary>
    /// RuneScape NPC definition for rev 639. Stores appearance, interaction
    /// options, sounds, and morph variant data.
    /// </summary>
    public class NPCDefinition : OpcodeStreamDefinition, ICloneable, IDefinition {
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
        /// <summary>Model IDs used for this NPC's appearance.</summary>
        public int[] modelIds;
        internal int[] recolorDst;
        internal int[] recolorSrc;
        internal int[] retextureDst;
        internal int[] retextureSrc;

        /// <summary>Whether the NPC plays idle animations.</summary>
        /// <remarks>
        ///     A view over opcode 111 rather than a field of its own, and the shape every bare
        ///     flag on this class takes.
        ///     <para>
        ///     The stream states a flag only by carrying the opcode - there is no payload to hold
        ///     a true or a false - and <see cref="Encode"/> writes an opcode back because the
        ///     definition carried it, whatever the fields now say. A plain field would therefore
        ///     be shown in the grid, edited by the user, and then silently overruled by the
        ///     replayed opcode on save: the row would change, the save would report success and
        ///     the cache would be untouched. Turning a flag off has to drop the opcode outright,
        ///     which is what <see cref="DropOpcode"/> does, and turning it on has to set the hit
        ///     map so <see cref="Encode"/> emits it.
        ///     </para>
        ///     <para>
        ///     Reading the value straight back off the hit map also settles what a definition that
        ///     never carried the opcode should report: exactly the value the client assumes in its
        ///     absence, which for 111 is idle animations enabled.
        ///     </para>
        /// </remarks>
        public bool animateIdle {
            get => !decoded[111];
            set {
                if (value) DropOpcode(111);
                else decoded[111] = true;
            }
        }
        /// <summary>Whether the NPC can be clicked on.</summary>
        /// <remarks>
        ///     A view over opcode 107, whose presence makes the NPC unclickable. See
        ///     <see cref="animateIdle"/> for why this is not a field. This one is bound to a grid
        ///     column, so before the fix every "Clickable" tick the user cleared was thrown away.
        /// </remarks>
        public bool clickable {
            get => !decoded[107];
            set {
                if (value) DropOpcode(107);
                else decoded[107] = true;
            }
        }
        /// <summary>Whether a yellow dot appears on the minimap.</summary>
        /// <remarks>
        ///     A view over opcode 93, whose presence removes the dot. See
        ///     <see cref="animateIdle"/>. Bound to a grid column, so its edits were lost too.
        /// </remarks>
        public bool drawMinimapDot {
            get => !decoded[93];
            set {
                if (value) DropOpcode(93);
                else decoded[93] = true;
            }
        }
        /// <summary>Whether this NPC renders above others at the same tile.</summary>
        /// <remarks>A view over opcode 99. See <see cref="animateIdle"/>.</remarks>
        public bool hasRenderPriority {
            get => decoded[99];
            set {
                if (value) decoded[99] = true;
                else DropOpcode(99);
            }
        }
        /// <summary>Whether the NPC has invisible render priority.</summary>
        /// <remarks>A view over opcode 143. See <see cref="animateIdle"/>.</remarks>
        public bool invisiblePriority {
            get => decoded[143];
            set {
                if (value) decoded[143] = true;
                else DropOpcode(143);
            }
        }
        /// <summary>Whether the NPC uses slow walk animations.</summary>
        /// <remarks>
        ///     A view over opcode 109, whose presence disables the slow walk. See
        ///     <see cref="animateIdle"/>.
        /// </remarks>
        public bool slowWalk {
            get => !decoded[109];
            set {
                if (value) DropOpcode(109);
                else decoded[109] = true;
            }
        }
        /// <summary>Whether the NPC has visible render priority.</summary>
        /// <remarks>
        ///     A view over opcode 141. See <see cref="animateIdle"/>. Bound to a grid column.
        /// </remarks>
        public bool visiblePriority {
            get => decoded[141];
            set {
                if (value) decoded[141] = true;
                else DropOpcode(141);
            }
        }
        /// <summary>Index of the primary right-click option.</summary>
        /// <remarks>
        ///     A view over opcodes 158 and 159, the bare flags that set the index to 1 and to 0.
        ///     Neither carries a payload, so the value exists in the file only as which of the two
        ///     opcodes is present, and it is read back off the hit map for the same reason
        ///     <see cref="animateIdle"/> is.
        ///     <para>
        ///     Zero is also the index the client assumes with neither opcode present, so setting
        ///     the index to zero drops 158 rather than inventing 159 - inventing it would lengthen
        ///     every definition that has no main option at all. A definition that already stored
        ///     159 keeps it, which is what lets those still re-encode to their stored bytes.
        ///     </para>
        ///     <para>
        ///     A definition carrying both flags takes its value from whichever came last, as the
        ///     client does, which is why the getter consults the recorded stream rather than the
        ///     hit map alone. Only 0 and 1 are representable, because those are the only values
        ///     the two opcodes produce; any other value set here is stored as 1.
        ///     </para>
        /// </remarks>
        public byte mainOptionIndex {
            get => decoded[158] && LastStreamIndexOf(158) >= LastStreamIndexOf(159) ? (byte) 1 : (byte) 0;
            set {
                if (value != 0) {
                    //159 has to go: left in place it would be replayed after 158 and reset the
                    //index the user just set back to zero.
                    DropOpcode(159);
                    decoded[158] = true;
                }
                else {
                    DropOpcode(158);
                }
            }
        }
        /// <summary>Destination palette for colour remapping.</summary>
        public byte[] recolorDstPalette;
        /// <summary>Ambient lighting modifier.</summary>
        public int ambient;
        /// <summary>Volume level for ambient sounds.</summary>
        public int ambientSoundVolume = 255;
        /// <summary>Army/clan icon sprite id.</summary>
        public int armyIcon = -1;
        /// <summary>Cursor sprite used for the attack option.</summary>
        public int attackOpCursor = -1;
        /// <summary>Contrast lighting modifier.</summary>
        public int contrast;
        /// <summary>Sound played during crawl animations.</summary>
        public int crawlSound = -1;
        /// <summary>Overhead prayer or status icon id.</summary>
        public int headIcon = -1;
        /// <summary>Override height for the NPC bounding box.</summary>
        public int height = -1;
        /// <summary>Custom hitbar sprite id.</summary>
        public int hitbarSprite = -1;
        /// <summary>Unique NPC identifier.</summary>
        public int id;
        /// <summary>Sound played while the NPC is idle.</summary>
        public int idleSound = -1;
        /// <summary>Reserved field.</summary>
        public int last;
        /// <summary>Combat level displayed on right-click.</summary>
        public int level = -1;
        /// <summary>Icon shown on the world map.</summary>
        public int mapIcon = -1;
        /// <summary>
        /// Raw byte carried by opcode 128. The 637 client reads it and discards it, so its
        /// meaning is unverified - it is kept only so the definition re-encodes byte-identically.
        /// </summary>
        public int unknownByte128;
        /// <summary>Custom cursor sprite for the primary option.</summary>
        public int primaryCursor = -1;
        /// <summary>Opcode for the primary option cursor.</summary>
        public int primaryCursorOp = -1;
        /// <summary>Render animation set id.</summary>
        public int renderTypeID = -1;
        /// <summary>Default facing angle in 1/2048ths of a revolution.</summary>
        public int rotation = 32;
        /// <summary>Sound played during run animations.</summary>
        public int runSound = -1;
        /// <summary>Horizontal scale factor (128 = normal).</summary>
        public int scaleXY = 128;
        /// <summary>Vertical scale factor (128 = normal).</summary>
        public int scaleZ = 128;
        /// <summary>Custom cursor sprite for the secondary option.</summary>
        public int secondaryCursor = -1;
        /// <summary>Opcode for the secondary option cursor.</summary>
        public int secondaryCursorOp = -1;
        /// <summary>Tile footprint size (default 1).</summary>
        public int size = 1;
        /// <summary>Maximum distance in tiles at which sounds are audible.</summary>
        public int soundDistance;
        /// <summary>Associated sprite id.</summary>
        public int spriteId = -1;
        /// <summary>Varbit id used for morph variant selection.</summary>
        public int varbit = -1;
        /// <summary>Varp id used for morph variant selection.</summary>
        public int varp = -1;
        /// <summary>Sound played during walk animations.</summary>
        public int walkSound = -1;
        /// <summary>Per-option cursor overrides.</summary>
        public int[] cursorOps;
        /// <summary>Array of NPC ids this NPC can morph into.</summary>
        public int[] morphs;
        /// <summary>Per-model translation offsets.</summary>
        public int[][] translations;
        /// <summary>Comma-separated model IDs for display in list views.</summary>
        public string ModelIdList => modelIds == null ? "" : string.Join(", ", modelIds);
        /// <summary>Display name shown on right-click.</summary>
        public string name = "null";
        /// <summary>Right-click menu options.</summary>
        public string[] options = new[] { null, null, null, null, null, "Examine" };
        short primaryShadowColour;
        short secondaryShadowColour;

        int anInt828;
        int anInt837;
        int anInt847;
        int anInt848;
        int anInt864;

        int[][] anIntArrayArray882;

        /// <summary>Unknown flag (opcode undetermined).</summary>
        public bool unknownBoolean7;
        /// <summary>Unknown byte arrays.</summary>
        public byte[] aByteArray8445, aByteArray8446;

        /// <summary>Unknown short value from opcode 44.</summary>
        public int op44;
        /// <summary>Unknown short value from opcode 45.</summary>
        public int op45;
        /// <summary>Unknown byte values from opcode 179.</summary>
        public int unknownByte1, unknownByte2, unknownByte3, unknownByte4, unknownByte5, unknownByte6;

        /// <summary>Unknown short array from opcodes 170-175.</summary>
        public int[] unknownOptions = { -1, -1, -1, -1, -1, -1 };


        /// <summary>
        ///     The opcode-249 parameters, in the order the file listed them.
        /// </summary>
        /// <remarks>
        ///     A dictionary is the natural shape for these and was what this held, but it loses
        ///     two things the file actually contains. The cache does not list parameter keys in
        ///     ascending order, so a sorted map re-emits 16 definitions with their parameters
        ///     shuffled, and one definition (NPC 13592) lists the same key twice, which a map
        ///     collapses into a single entry and shortens the record by eight bytes. Keeping the
        ///     list is what makes those seventeen re-encode to their stored bytes.
        /// </remarks>
        private List<KeyValuePair<int, object>> config;
        private sbyte someField112;
        private int someField1101;
        private int someField1090;
        private int anInt1101;
        private int anInt1090;
        private sbyte anInt1104;

        /// <summary>Which opcodes the decoded stream carried, indexed by opcode number.</summary>
        /// <remarks>
        ///     The rev-639 packer stores plenty of fields at the value the client would assume
        ///     anyway - opcode 12 with a size of 1, opcode 100 with an ambient of 0 - so an
        ///     encoder that emitted an opcode only when its field had moved off the default would
        ///     silently shorten definitions nobody edited. This map is what lets
        ///     <see cref="Encode"/> say "the file carried this opcode" independently of what the
        ///     field now holds.
        /// </remarks>
        public bool[] decoded = new bool[256];

        /// <summary>
        /// Constructs a new item definition from the stream data
        /// </summary>
        /// <param name="stream">The stream containing the encoded item data</param>
        public NPCDefinition(JagStream stream) {
            Decode(stream);
        }

        /// <summary>
        ///     Constructs an NPC definition holding nothing but the client-side defaults.
        /// </summary>
        /// <remarks>
        ///     A definition built this way carries no opcode record at all, so
        ///     <see cref="Encode"/> has only the field values to go on and emits exactly the
        ///     opcodes whose fields have been moved off their defaults. That is what lets the
        ///     editor create an NPC from scratch rather than only edit one the cache already had.
        /// </remarks>
        public NPCDefinition() {
        }

        /// <summary>
        /// Overrides defaults from the stream
        /// </summary>
        /// <param name="stream"></param>
        public void Decode(JagStream stream, int[] xteaKey = null) {
            DecodeOpcodeStream(stream);
        }

        /// <summary>
        ///     255 ends an NPC record as well as 0.
        /// </summary>
        /// <remarks>
        ///     No opcode 255 exists in this format, so the byte can only be a sentinel; treating it
        ///     as an opcode would fail a record the client reads happily.
        /// </remarks>
        /// <param name="opcode">The byte read where an opcode was expected.</param>
        /// <returns>True to stop reading.</returns>
        protected override bool IsTerminator(int opcode) => opcode <= 0 || opcode == 255;

        /// <summary>
        /// Overrides a specific property corresponding to opcode
        /// </summary>
        /// <param name="stream">The stream to read from</param>
        /// <param name="opcode">The opcode value signalling which type to read</param>
        /// <returns>True when the payload was consumed; false to fail the record.</returns>
        protected override bool DecodeOpcode(JagStream stream, int opcode) {
            decoded[opcode] = true;

            switch (opcode) {
                case 1: {
                        int length = stream.ReadByte();
                        modelIds = new int[length];
                        for (int k = 0 ; k < length ; k++) {
                            int id = stream.ReadUnsignedShort();
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
                case 34: {
                        int idx = opcode - 30;
                        options[idx] = stream.ReadJagexString();
                        if (string.Equals(options[idx], "Hidden", StringComparison.Ordinal))
                            options[idx] = null;
                        break;
                    }

                case 40: {
                        int length = stream.ReadByte();
                        recolorSrc = new int[length];
                        recolorDst = new int[length];
                        for (int i = 0 ; i < length ; i++) {
                            recolorSrc[i] = stream.ReadShort();
                            recolorDst[i] = stream.ReadShort();
                        }
                        break;
                    }

                case 41: {
                        int length = stream.ReadByte();
                        retextureSrc = new int[length];
                        retextureDst = new int[length];
                        for (int i = 0 ; i < length ; i++) {
                            retextureSrc[i] = stream.ReadShort();
                            retextureDst[i] = stream.ReadShort();
                        }
                        break;
                    }

                case 42: {
                        int length = stream.ReadByte();
                        recolorDstPalette = new byte[length];
                        for (int i = 0 ; i < length ; i++)
                            recolorDstPalette[i] = (byte) stream.ReadByte();
                        break;
                    }

                case 44:
                    op44 = stream.ReadShort();
                    break;

                case 45:
                    op45 = stream.ReadShort();
                    break;

                case 60: {
                        int length = stream.ReadByte();
                        dialogueModels = new int[length];
                        for (int i = 0 ; i < length ; i++)
                            dialogueModels[i] = stream.ReadUnsignedShort();
                        break;
                    }

                /* The bare flags - 93, 99, 107, 109, 111, 141, 143, 158 and 159 - have no payload
                   and no case body. Their properties are views over the opcode hit map, which the
                   decode loop has already written, so there is nothing left for a case to assign.
                   Assigning through the property would be worse than redundant: its setter drops
                   opcodes, and opcode 159's setter would erase a 158 the definition legitimately
                   carried earlier in the same stream. */

                case 93:    // drawMinimapDot off
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

                case 99:    // hasRenderPriority on
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
                case 118: {
                        varbit = stream.ReadUnsignedShort();
                        if (varbit == 65535) varbit = -1;

                        varp = stream.ReadUnsignedShort();
                        if (varp == 65535) varp = -1;

                        int last = -1;
                        if (opcode == 118) {
                            last = stream.ReadUnsignedShort();
                            if (last == 65535) last = -1;
                        }

                        int count = stream.ReadByte();
                        morphs = new int[count + 2];
                        for (int i = 0 ; i <= count ; i++) {
                            int m = stream.ReadUnsignedShort();
                            morphs[i] = (m == 65535 ? -1 : m);
                        }
                        morphs[count + 1] = last;
                        break;
                    }

                case 107:   // clickable off
                    break;

                case 109:   // slowWalk off
                    break;

                case 111:   // animateIdle off
                    break;

                case 112:
                    anInt1104 = (sbyte) stream.ReadByte();
                    break;

                case 113:
                    primaryShadowColour = (short) stream.ReadShort();
                    secondaryShadowColour = (short) stream.ReadShort();
                    break;

                case 114:
                    primaryShadowModifier = (sbyte) stream.ReadByte();
                    secondaryShadowModifier = (sbyte) stream.ReadByte();
                    break;

                case 119:
                    walkMask = (sbyte) stream.ReadByte();
                    break;

                // Translations (restored original logic)
                case 121: {
                        translations = new int[modelIds == null ? 0 : modelIds.Length][];
                        int length = stream.ReadByte();
                        for (int i_62_ = 0 ; i_62_ < length ; i_62_++) {
                            int index = stream.ReadByte();
                            int[] translations = (this.translations[index] = new int[3]);
                            translations[0] = stream.ReadSignedByte();
                            translations[1] = stream.ReadSignedByte();
                            translations[2] = stream.ReadSignedByte();
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
                    respawnDirection = (byte) stream.ReadByte();
                    break;

                case 127:
                    renderTypeID = stream.ReadShort();
                    break;

                case 128:
                    unknownByte128 = stream.ReadByte();
                    break;

                case 134: {
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

                case 141:   // visiblePriority on
                    break;

                case 142:
                    mapIcon = stream.ReadShort();
                    break;

                case 143:   // invisiblePriority on
                    break;

                case 150:
                case 151:
                case 152:
                case 153:
                case 154: {
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

                case 158:   // mainOptionIndex = 1
                    break;

                case 159:   // mainOptionIndex = 0
                    break;

                case 160: {
                        int length = stream.ReadByte();
                        campaigns = new int[length];
                        for (int i = 0 ; i < length ; i++)
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

                case 179: {
                        unknownByte1 = stream.ReadByte();
                        unknownByte2 = stream.ReadByte();
                        unknownByte3 = stream.ReadByte();
                        unknownByte4 = stream.ReadByte();
                        unknownByte5 = stream.ReadByte();
                        unknownByte6 = stream.ReadByte();
                        break;
                    }

                case 249: {
                        int cfgLen = stream.ReadByte();

                        //Replaced rather than appended to, so that a definition carrying 249
                        //twice behaves like every other repeated opcode: the last occurrence is
                        //what the fields hold and the earlier one is replayed from its bytes.
                        config = new List<KeyValuePair<int, object>>(cfgLen);
                        for (int k = 0 ; k < cfgLen ; k++) {
                            bool isString = stream.ReadByte() == 1;
                            int key = stream.ReadMedium();
                            object val = isString
                                ? (object) stream.ReadJagexString()
                                : stream.ReadInt();
                            config.Add(new KeyValuePair<int, object>(key, val));
                        }
                        break;
                    }

                default:
                    /* No opcode outside this switch occurs in either cache, so this never fires
                       for the data the editor ships against. The base loop turns it into a report
                       of where the parse stopped, which beats a silently wrong definition. */
                    return false;
            }

            return true;
        }



        /// <summary>
        ///     Writes this definition back out as the opcode stream the client reads.
        /// </summary>
        /// <remarks>
        ///     Each opcode's payload is built into its own buffer first and the buffers are laid
        ///     down afterwards in the order the definition arrived in, because the cache stores
        ///     opcodes in no fixed order and an encoder that imposed one would rewrite almost
        ///     every definition the user merely opened.
        ///     <para>
        ///     An opcode is emitted when the stream carried it - regardless of the value its
        ///     field now holds, since the packer does store fields at their default - or when the
        ///     field has moved off the value the client assumes in the opcode's absence, which is
        ///     what lets an edit on a definition that never carried that opcode still reach the
        ///     file.
        ///     </para>
        /// </remarks>
        /// <returns>A flipped stream holding the encoded definition.</returns>
        public JagStream Encode() {
            /* `o` is reassigned around each payload rather than passed in, because every payload
               lambda below closes over it. */
            var records = new List<KeyValuePair<int, byte[]>>();
            var o = new JagStream();

            void Emit(int op, Action payload = null) {
                JagStream outer = o;
                var buffer = new JagStream();
                o = buffer;
                payload?.Invoke();
                o = outer;
                records.Add(new KeyValuePair<int, byte[]>(op, buffer.Flip().ToArray()));
            }

            /* Every array-valued opcode below is optional in the file format, so the backing
               array stays null when the definition did not carry that opcode. Emitting the
               opcode regardless would dereference null - across the rev-639 cache opcode 42 is
               carried by no NPC at all and opcode 1 by only 12146 of 13359, so an unguarded
               encode throws for every real definition. Absent data means the opcode is omitted. */

            // 1: modelIds
            if (modelIds != null)
                Emit(1, () => {
                    o.WriteByte((byte) modelIds.Length);
                    foreach (var modelId in modelIds)
                        o.WriteShort(modelId == -1 ? 0xFFFF : modelId);
                });

            // 2: name
            if (decoded[2] || (name != null && name != "null"))
                Emit(2, () => o.WriteJagexString(name ?? "null"));

            // 12: size
            if (decoded[12] || size != 1)
                Emit(12, () => o.WriteByte((byte) size));

            /* 30-34 / 150-154: options. The two ranges are two spellings of the same five slots
               and this codec reads both into `options`. The encoder used to emit both ranges,
               which wrote every option twice and on its own guaranteed a byte mismatch for every
               definition in the cache. Which range a definition used is not recoverable from the
               fields, so it is taken from the recorded stream; an option set on a definition that
               carried neither goes out at 30+slot, the range the client reads first. Slot 5 is
               deliberately untouched: the array is six long only so the seeded "Examine" has
               somewhere to live, and no opcode reads or writes it. */
            for (int slot = 0 ; slot < 5 ; slot++) {
                int viaAction = LastStreamIndexOf(30 + slot);
                int viaMenu = LastStreamIndexOf(150 + slot);
                string option = options[slot];

                if (viaAction < 0 && viaMenu < 0) {
                    if (option != null)
                        Emit(30 + slot, () => o.WriteJagexString(option));
                    continue;
                }

                //"Hidden" is how the file spells an option that exists but is not shown, and the
                //decoder folds it to null, so it has to be spelled back out on the way in.
                Emit(viaMenu > viaAction ? 150 + slot : 30 + slot,
                    () => o.WriteJagexString(option ?? "Hidden"));
            }

            // 40: recolor
            if (recolorSrc != null && recolorDst != null)
                Emit(40, () => {
                    o.WriteByte((byte) recolorSrc.Length);
                    for (int i = 0 ; i < recolorSrc.Length ; i++) {
                        o.WriteShort(recolorSrc[i]);
                        o.WriteShort(recolorDst[i]);
                    }
                });

            // 41: retexture
            if (retextureSrc != null && retextureDst != null)
                Emit(41, () => {
                    o.WriteByte((byte) retextureSrc.Length);
                    for (int i = 0 ; i < retextureSrc.Length ; i++) {
                        o.WriteShort(retextureSrc[i]);
                        o.WriteShort(retextureDst[i]);
                    }
                });

            // 42: palette
            if (recolorDstPalette != null)
                Emit(42, () => {
                    o.WriteByte((byte) recolorDstPalette.Length);
                    foreach (var b in recolorDstPalette)
                        o.WriteByte(b);
                });

            // 44,45: op44/op45
            if (decoded[44] || op44 != 0) Emit(44, () => o.WriteShort(op44));
            if (decoded[45] || op45 != 0) Emit(45, () => o.WriteShort(op45));

            // 60: dialogueModels
            if (dialogueModels != null)
                Emit(60, () => {
                    o.WriteByte((byte) dialogueModels.Length);
                    foreach (var m in dialogueModels)
                        o.WriteShort(m);
                });

            /* Every bare flag below is emitted from the hit map alone. The properties behind them
               are views over that map, so a field test would be the same question asked twice;
               more to the point, clearing one of them drops the opcode from the map AND from the
               recorded stream, which is the only thing that stops the replay pass putting back a
               flag the user has just turned off. */

            // 93: drawMinimapDot off
            if (decoded[93]) Emit(93);

            // 95,97,98
            if (decoded[95] || level != -1) Emit(95, () => o.WriteShort(level));
            if (decoded[97] || scaleXY != 128) Emit(97, () => o.WriteShort(scaleXY));
            if (decoded[98] || scaleZ != 128) Emit(98, () => o.WriteShort(scaleZ));

            // 99: hasRenderPriority
            if (decoded[99]) Emit(99);

            // 100,101
            if (decoded[100] || ambient != 0) Emit(100, () => o.WriteByte((byte) ambient));
            if (decoded[101] || contrast != 0) Emit(101, () => o.WriteByte((byte) contrast));

            // 102,103
            if (decoded[102] || headIcon != -1) Emit(102, () => o.WriteShort(headIcon));
            if (decoded[103] || rotation != 32) Emit(103, () => o.WriteShort(rotation));

            // 106/118: morphs
            if (morphs != null) {
                int count = morphs.Length - 2;

                /* Both opcodes write the same fields, so on a definition carrying both only the
                   one read last still has its values; the other is replayed from its own bytes.
                   With neither present the trailing default id decides, since that is the only
                   thing opcode 118 adds over 106. */
                int via106 = LastStreamIndexOf(106);
                int via118 = LastStreamIndexOf(118);
                bool use118 = via106 < 0 && via118 < 0
                    ? morphs[count + 1] != -1
                    : via118 > via106;

                Emit(use118 ? 118 : 106, () => {
                    o.WriteShort(varbit == -1 ? 0xFFFF : varbit);
                    o.WriteShort(varp == -1 ? 0xFFFF : varp);
                    if (use118)
                        o.WriteShort(morphs[count + 1] == -1 ? 0xFFFF : morphs[count + 1]);
                    o.WriteByte((byte) count);
                    for (int i = 0 ; i <= count ; i++)
                        o.WriteShort(morphs[i] == -1 ? 0xFFFF : morphs[i]);
                });
            }

            // 107,109,111: clickable off, slowWalk off, animateIdle off
            if (decoded[107]) Emit(107);
            if (decoded[109]) Emit(109);
            if (decoded[111]) Emit(111);

            // 112
            if (decoded[112] || anInt1104 != 0) Emit(112, () => o.WriteSignedByte(anInt1104));

            // 113,114,119
            if (decoded[113] || primaryShadowColour != 0 || secondaryShadowColour != 0)
                Emit(113, () => {
                    o.WriteShort(primaryShadowColour);
                    o.WriteShort(secondaryShadowColour);
                });
            if (decoded[114] || primaryShadowModifier != -33 || secondaryShadowModifier != -113)
                Emit(114, () => {
                    o.WriteSignedByte(primaryShadowModifier);
                    o.WriteSignedByte(secondaryShadowModifier);
                });
            if (decoded[119] || walkMask != 0)
                Emit(119, () => o.WriteSignedByte(walkMask));

            // 121: translations
            if (translations != null)
                Emit(121, () => {
                    int tlen = translations.Length;

                    /* The array is sized to modelIds.Length but the decoder only fills the slots
                       named by the record index bytes, so unpopulated slots stay null. The count
                       written here must be the number of records actually emitted below, NOT the
                       array length: declaring the length makes the decoder read records that were
                       never written and overrun into the following opcode. */
                    int written = 0;
                    for (int idx = 0 ; idx < tlen ; idx++)
                        if (translations[idx] != null)
                            written++;

                    if (written > 255)
                        throw new InvalidOperationException("NPC " + id + " has " + written + " model translations; opcode 121 encodes the record count as a single byte");

                    o.WriteByte((byte) written);
                    for (int idx = 0 ; idx < tlen ; idx++) {
                        var t = translations[idx];
                        if (t == null) continue;
                        if (idx > 255)
                            throw new InvalidOperationException("NPC " + id + " has a model translation at index " + idx + "; opcode 121 encodes the slot index as a single byte");
                        o.WriteByte((byte) idx);
                        o.WriteByte((byte) t[0]);
                        o.WriteByte((byte) t[1]);
                        o.WriteByte((byte) t[2]);
                    }
                });

            // 122-128
            if (decoded[122] || hitbarSprite != -1) Emit(122, () => o.WriteShort(hitbarSprite));
            if (decoded[123] || height != -1) Emit(123, () => o.WriteShort(height));
            if (decoded[125] || respawnDirection != 7) Emit(125, () => o.WriteByte(respawnDirection));
            if (decoded[127] || renderTypeID != -1) Emit(127, () => o.WriteShort(renderTypeID));
            if (decoded[128] || unknownByte128 != 0) Emit(128, () => o.WriteByte((byte) unknownByte128));

            // 134: sounds
            if (decoded[134] || idleSound != -1 || crawlSound != -1 || walkSound != -1
                || runSound != -1 || soundDistance != 0)
                Emit(134, () => {
                    o.WriteShort(idleSound == -1 ? 0xFFFF : idleSound);
                    o.WriteShort(crawlSound == -1 ? 0xFFFF : crawlSound);
                    o.WriteShort(walkSound == -1 ? 0xFFFF : walkSound);
                    o.WriteShort(runSound == -1 ? 0xFFFF : runSound);
                    o.WriteByte((byte) soundDistance);
                });

            // 135-143
            if (decoded[135] || primaryCursorOp != -1 || primaryCursor != -1)
                Emit(135, () => { o.WriteByte((byte) primaryCursorOp); o.WriteShort(primaryCursor); });
            if (decoded[136] || secondaryCursorOp != -1 || secondaryCursor != -1)
                Emit(136, () => { o.WriteByte((byte) secondaryCursorOp); o.WriteShort(secondaryCursor); });
            if (decoded[137] || attackOpCursor != -1) Emit(137, () => o.WriteShort(attackOpCursor));
            if (decoded[138] || armyIcon != -1) Emit(138, () => o.WriteShort(armyIcon));
            if (decoded[139] || spriteId != -1) Emit(139, () => o.WriteShort(spriteId));
            if (decoded[140] || ambientSoundVolume != 255)
                Emit(140, () => o.WriteByte((byte) ambientSoundVolume));
            if (decoded[141]) Emit(141);
            if (decoded[142] || mapIcon != -1) Emit(142, () => o.WriteShort(mapIcon));
            if (decoded[143]) Emit(143);

            // 155
            if (decoded[155] || hue != 0 || saturation != 0 || lightness != 0 || opacity != 0)
                Emit(155, () => {
                    o.WriteByte((byte) hue);
                    o.WriteByte((byte) saturation);
                    o.WriteByte((byte) lightness);
                    o.WriteByte((byte) opacity);
                });

            /* 158/159 are the bare flags behind mainOptionIndex. Both come off the hit map: zero
               is the index the client assumes with neither present, so 159 is emitted only when
               the stream actually carried it - inventing it would lengthen every definition that
               has no main option at all - and setting the index to zero drops 158 instead. */
            if (decoded[158]) Emit(158);
            if (decoded[159]) Emit(159);

            // 160: campaigns
            if (campaigns != null)
                Emit(160, () => {
                    o.WriteByte((byte) campaigns.Length);
                    foreach (var c in campaigns) o.WriteShort(c);
                });

            // 162: anInt1101/anInt1090
            if (decoded[162] || anInt1101 != 0 || anInt1090 != 0)
                Emit(162, () => { o.WriteShort(anInt1101); o.WriteShort(anInt1090); });

            // 163-168
            if (decoded[163] || anInt864 != 0) Emit(163, () => o.WriteByte((byte) anInt864));
            if (decoded[164] || anInt848 != 0 || anInt837 != 0)
                Emit(164, () => { o.WriteShort(anInt848); o.WriteShort(anInt837); });
            if (decoded[165] || anInt847 != 0) Emit(165, () => o.WriteByte((byte) anInt847));
            if (decoded[168] || anInt828 != 0) Emit(168, () => o.WriteByte((byte) anInt828));

            // 170-175: unknownOptions
            for (int opc = 170 ; opc <= 175 ; opc++) {
                int val = unknownOptions[opc - 170];
                if (decoded[opc] || val != -1)
                    Emit(opc, () => o.WriteShort(val));
            }

            // 179
            if (decoded[179] || unknownByte1 != 0 || unknownByte2 != 0 || unknownByte3 != 0
                || unknownByte4 != 0 || unknownByte5 != 0 || unknownByte6 != 0)
                Emit(179, () => {
                    o.WriteByte((byte) unknownByte1);
                    o.WriteByte((byte) unknownByte2);
                    o.WriteByte((byte) unknownByte3);
                    o.WriteByte((byte) unknownByte4);
                    o.WriteByte((byte) unknownByte5);
                    o.WriteByte((byte) unknownByte6);
                });

            // 249: config
            if (decoded[249] || (config != null && config.Count > 0))
                Emit(249, () => {
                    o.WriteByte((byte) (config?.Count ?? 0));
                    foreach (var kv in config ?? new List<KeyValuePair<int, object>>()) {
                        bool isStr = kv.Value is string;
                        o.WriteByte((byte) (isStr ? 1 : 0));
                        o.WriteMedium(kv.Key);
                        if (isStr) o.WriteJagexString((string) kv.Value);
                        else o.WriteInteger((int) kv.Value);
                    }
                });

            /* Ascending, so a definition the editor built from nothing - which has no recorded
               stream to replay - still encodes in a predictable order. */
            return Opcodes.Replay(records, appendInAscendingOrder: true);
        }

        /// <summary>
        ///     Forgets an opcode entirely, so neither the hit map nor the recorded stream order
        ///     will put it back on the next encode.
        /// </summary>
        /// <remarks>
        ///     Clearing <see cref="decoded"/> alone is not enough. An opcode still listed in the
        ///     recorded stream is written back from the bytes it was read from, which is what
        ///     keeps a repeated opcode byte-exact but would also resurrect a flag the user had
        ///     just turned off - the row in the grid changes, the save reports success and the
        ///     definition in the cache is unaltered.
        ///     <para>
        ///     Suppressed rather than removed. Removing it forgot <b>where</b> the opcode was, so
        ///     turning the flag off and straight back on re-emitted it at the end of the record
        ///     instead of in place - a definition of the right length with a byte moved, which the
        ///     editor then staged as a real change. See <see cref="OpcodeStream.Suppress"/>.
        ///     </para>
        /// </remarks>
        /// <param name="op">The opcode to turn off.</param>
        private void DropOpcode(int op) {
            decoded[op] = false;
            Opcodes.Suppress(op);
        }

        /// <summary>
        ///     Where an opcode last appeared in the decoded stream, or -1 when it never did.
        /// </summary>
        /// <param name="op">The opcode to look for.</param>
        /// <returns>The index into the recorded stream, or -1.</returns>
        private int LastStreamIndexOf(int op) => Opcodes.LastIndexOf(op);

        internal void SetId(int id) {
            this.id = id;
        }

        /// <summary>
        ///     Takes an independent copy of this <see cref="NPCDefinition"/>.
        /// </summary>
        /// <remarks>
        ///     The editor clones a definition to hold what it looked like before an edit, so the
        ///     two cannot share the opcode hit map, the recorded stream or the options array:
        ///     every one of those is written through when a definition is edited, and the
        ///     snapshot would then agree with the edit it exists to remember.
        /// </remarks>
        /// <returns>A copy whose bookkeeping the original does not share.</returns>
        public NPCDefinition Clone() {
            var clone = (NPCDefinition) MemberwiseClone();
            clone.decoded = (bool[]) decoded.Clone();
            clone.DetachOpcodeStream();
            clone.options = (string[]) options.Clone();
            if (config != null)
                clone.config = new List<KeyValuePair<int, object>>(config);
            return clone;
        }

        object ICloneable.Clone() => Clone();
    }
}