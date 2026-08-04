using System;
using System.Collections.Generic;

namespace FlashEditor.Definitions.SpotAnims {
    /// <summary>
    ///     One spot animation ("graphic") from JS5 index 21: which model to draw for an effect,
    ///     which animation plays on it, and how it is scaled, rotated, lit and recoloured.
    /// </summary>
    /// <remarks>
    ///     A graphic id splits <c>group = id &gt;&gt;&gt; 8</c> (Class329.java:39-46) and
    ///     <c>file = id &amp; 0xff</c> (Class314.java:11-18), so a group is a 256-record page.
    ///     Opcode table from <c>Class107.method1727</c> (Class107.java:247-320), driven by the loop
    ///     in <c>method1725</c> (:226-245).
    ///     <para>
    ///     The namespace is <c>SpotAnims</c> and the type is <c>GraphicDefinition</c> rather than
    ///     <c>Graphic</c> on purpose: a namespace or type called <c>Graphics</c>/<c>Graphic</c>
    ///     collides with <c>System.Drawing.Graphics</c> in every WinForms-touching file, the same
    ///     build break <c>Region</c> already caused.
    ///     </para>
    ///     <para>
    ///     Two cross-index joins, both read-only from here: <see cref="ModelId"/> is an index-7 group
    ///     id (Node_Sub6.java:59-66 fetches it as <c>getChildFromFolder(modelId, 0)</c>) and
    ///     <see cref="AnimationId"/> is an index-20 animation id (Class183.method2623).
    ///     </para>
    /// </remarks>
    public sealed class GraphicDefinition : OpcodeStreamDefinition {
        /// <summary>Scale the client assumes on both axes when opcodes 4 and 5 are absent.</summary>
        /// <remarks>Class107.java:86,90. 128 is unit scale for <c>Renderable.O</c>.</remarks>
        public const int DefaultScale = 128;

        /// <summary>Base ambient light the model is built with (Class107.java:178).</summary>
        /// <remarks>The stored opcode-7 value is added to this, so a stored 0 is not "no light".</remarks>
        public const int AmbientBase = 64;

        /// <summary>Base contrast the model is built with (Class107.java:179).</summary>
        public const int ContrastBase = 850;

        /// <summary>The parameter opcode 9 sets alongside effect kind 3 (Class107.java:263).</summary>
        public const int Opcode9Parameter = 8224;

        /// <summary>Effect opcode value meaning the record states no effect at all.</summary>
        public const int NoEffectOpcode = 0;

        /// <summary>The graphic id, which is <c>(group &lt;&lt; 8) | file</c>.</summary>
        public int Id { get; set; } = -1;

        /// <summary>Opcode 1. The index-7 model group drawn for this effect.</summary>
        public int ModelId { get; set; }

        /// <summary>Opcode 2. The index-20 animation played on that model, or -1 for none.</summary>
        /// <remarks>
        ///     Class107.java:137-138 resolves it through <c>Class183.method2623</c> and leaves the
        ///     model static when it is -1.
        /// </remarks>
        public int AnimationId { get; set; } = -1;

        /// <summary>Opcode 4. Horizontal scale, in 128ths.</summary>
        /// <remarks>
        ///     Applied as <c>renderable.O(scaleXZ, scaleY, scaleXZ)</c> (Class107.java:200), so this
        ///     one value drives both horizontal axes.
        /// </remarks>
        public int ScaleXZ { get; set; } = DefaultScale;

        /// <summary>Opcode 5. Vertical scale, in 128ths.</summary>
        public int ScaleY { get; set; } = DefaultScale;

        /// <summary>
        ///     Opcode 6. Rotation in degrees, of which only 90, 180 and 270 do anything.
        /// </summary>
        /// <remarks>
        ///     Class107.java:201-209 maps those three to <c>a(4096)</c>, <c>a(8192)</c> and
        ///     <c>a(12288)</c> and ignores every other value, so the field is a u16 the client reads
        ///     as an enumeration. Stored verbatim rather than normalised - an out-of-range value is
        ///     what the file says and has to be written back.
        /// </remarks>
        public int Rotation { get; set; }

        /// <summary>Opcode 7. Ambient light added to <see cref="AmbientBase"/> (Class107.java:178).</summary>
        public int Ambient { get; set; }

        /// <summary>Opcode 8. Contrast added to <see cref="ContrastBase"/> (Class107.java:179).</summary>
        public int Contrast { get; set; }

        /// <summary>
        ///     Opcode 10. Lets the entity's movement cancel or defer the animation this graphic
        ///     carries.
        /// </summary>
        /// <remarks>
        ///     Read outside <c>Class107</c>, which is why it looks unused from inside it:
        ///     Class333.java:65-79 and Class340.java:80-96 both require it before they consult the
        ///     animation's own moving/stationary interrupt fields, so a graphic without it plays
        ///     through regardless of what the entity is doing. A view over the recorded stream, since
        ///     the opcode has no payload to state anything else.
        /// </remarks>
        public bool RespectsMovementInterrupt {
            get => Opcodes.Has(10);
            set => SetFlag(10, value);
        }

        /// <summary>
        ///     Which opcode last set the effect, or <see cref="NoEffectOpcode"/> when none did.
        /// </summary>
        /// <remarks>
        ///     Stored rather than derived, and this is the whole reason the field exists. Opcodes 9,
        ///     15 and 16 all produce effect kind 3 and only differ in how the parameter is written -
        ///     none, u16, int32 - so the decoded pair does not say which opcode a record carried.
        ///     Recomputing that on the way out would rewrite the record in a different width.
        ///     <para>
        ///     None of the eight effect opcodes occurs in either cache, so no byte-identity sweep
        ///     defends any of this. It is implemented from the client and pinned by synthetic tests
        ///     for the same reason the unreachable reference-table branches are.
        ///     </para>
        /// </remarks>
        public int EffectOpcode { get; private set; } = NoEffectOpcode;

        /// <summary>
        ///     The effect kind the opcode selected, 0 when none.
        /// </summary>
        /// <remarks>
        ///     Handed straight to <c>Renderable.p</c> (Class107.java:211), which is abstract
        ///     (Renderable.java:755), so what any of 1 to 5 does is not settled by the client source
        ///     alone. Kept as the number the file implies.
        /// </remarks>
        public int EffectKind { get; private set; }

        /// <summary>The effect's numeric parameter, -1 when nothing set one.</summary>
        /// <remarks>
        ///     Opcode 14 stores it as a byte and multiplies by 256, opcode 15 as a u16 and opcode 16
        ///     as a signed int32, which is why <see cref="EffectOpcode"/> has to be carried beside it.
        /// </remarks>
        public int EffectParameter { get; private set; } = -1;

        /// <summary>Opcode 40. Source colours to replace on the model, paired with <see cref="RecolourTo"/>.</summary>
        /// <remarks>
        ///     Applied as <c>renderable.ia(from, to)</c> (Class107.java:182). Always the same length
        ///     as <see cref="RecolourTo"/>; one opcode writes both.
        /// </remarks>
        public int[] RecolourFrom { get; set; } = Array.Empty<int>();

        /// <summary>Opcode 40. The replacement colours.</summary>
        public int[] RecolourTo { get; set; } = Array.Empty<int>();

        /// <summary>Opcode 41. Source materials to replace, paired with <see cref="RetextureTo"/>.</summary>
        /// <remarks>
        ///     Applied as <c>renderable.aa(from, to)</c> (Class107.java:187). No record in either
        ///     cache carries opcode 41.
        /// </remarks>
        public int[] RetextureFrom { get; set; } = Array.Empty<int>();

        /// <summary>Opcode 41. The replacement materials.</summary>
        public int[] RetextureTo { get; set; } = Array.Empty<int>();

        /// <summary>The ambient value the client actually builds the model with.</summary>
        public int EffectiveAmbient => AmbientBase + Ambient;

        /// <summary>The contrast value the client actually builds the model with.</summary>
        public int EffectiveContrast => ContrastBase + Contrast;

        /// <summary>Whether the stored rotation is one the client acts on.</summary>
        /// <remarks>
        ///     Every other value leaves the model unrotated, so a graphic storing one is stating
        ///     something the client silently drops - worth surfacing in an editor rather than
        ///     correcting.
        /// </remarks>
        public bool RotationIsApplied => Rotation == 90 || Rotation == 180 || Rotation == 270;

        /// <summary>Reads one graphic record from its file.</summary>
        /// <param name="stream">The file, positioned at its first opcode.</param>
        /// <returns>This definition.</returns>
        public GraphicDefinition Decode(JagStream stream) {
            Opcodes.Clear();
            DecodeOpcodeStream(stream);
            return this;
        }

        /// <inheritdoc/>
        protected override bool DecodeOpcode(JagStream stream, int opcode) {
            switch (opcode) {
                case 1:
                    ModelId = stream.ReadUnsignedShort();
                    return true;

                case 2:
                    AnimationId = stream.ReadUnsignedShort();
                    return true;

                case 4:
                    ScaleXZ = stream.ReadUnsignedShort();
                    return true;

                case 5:
                    ScaleY = stream.ReadUnsignedShort();
                    return true;

                case 6:
                    Rotation = stream.ReadUnsignedShort();
                    return true;

                case 7:
                    Ambient = stream.ReadUnsignedByte();
                    return true;

                case 8:
                    Contrast = stream.ReadUnsignedByte();
                    return true;

                case 9:
                    ApplyEffect(9, 3, Opcode9Parameter);
                    return true;

                //10 is a bare flag: its presence is its whole payload.
                case 10:
                    return true;

                /* 11, 12 and 13 set the kind and leave the parameter as it was, which is what makes
                   the parameter's provenance separate from the kind's. */
                case 11:
                    ApplyEffect(11, 1, EffectParameter);
                    return true;

                case 12:
                    ApplyEffect(12, 4, EffectParameter);
                    return true;

                case 13:
                    ApplyEffect(13, 5, EffectParameter);
                    return true;

                case 14:
                    ApplyEffect(14, 2, stream.ReadUnsignedByte() * 256);
                    return true;

                case 15:
                    ApplyEffect(15, 3, stream.ReadUnsignedShort());
                    return true;

                case 16:
                    ApplyEffect(16, 3, stream.ReadInt());
                    return true;

                case 40: {
                        int count = stream.ReadUnsignedByte();
                        RecolourFrom = new int[count];
                        RecolourTo = new int[count];
                        for (int i = 0; i < count; i++) {
                            RecolourFrom[i] = stream.ReadUnsignedShort();
                            RecolourTo[i] = stream.ReadUnsignedShort();
                        }
                        return true;
                    }

                case 41: {
                        int count = stream.ReadUnsignedByte();
                        RetextureFrom = new int[count];
                        RetextureTo = new int[count];
                        for (int i = 0; i < count; i++) {
                            RetextureFrom[i] = stream.ReadUnsignedShort();
                            RetextureTo[i] = stream.ReadUnsignedShort();
                        }
                        return true;
                    }

                /* 3 is the only gap in 1..16 and the client has no handler for it, so there is no
                   payload width to guess at. It occurs nowhere in either 639 cache. */
                default:
                    return false;
            }
        }

        /// <summary>
        ///     Replaces whatever effect the record stated with one built from a chosen opcode.
        /// </summary>
        /// <remarks>
        ///     All eight effect opcodes are mutually exclusive statements of the same two fields, so
        ///     setting one has to drop the others - otherwise the replay would write the old opcode
        ///     back alongside the new one and the client would take whichever came last.
        ///     <para>
        ///     Opcode 14 stores its parameter as a byte scaled by 256, so a value that is not a
        ///     multiple of 256 cannot be expressed and is rounded down by the encoder.
        ///     </para>
        /// </remarks>
        /// <param name="opcode">One of 9, 11, 12, 13, 14, 15, 16, or <see cref="NoEffectOpcode"/> to clear.</param>
        /// <param name="parameter">The parameter, ignored by the opcodes that carry none.</param>
        /// <exception cref="ArgumentOutOfRangeException">The opcode does not set an effect.</exception>
        public void SetEffect(int opcode, int parameter = -1) {
            foreach (int existing in new[] { 9, 11, 12, 13, 14, 15, 16 })
                Opcodes.Remove(existing);

            switch (opcode) {
                case NoEffectOpcode:
                    EffectOpcode = NoEffectOpcode;
                    EffectKind = 0;
                    EffectParameter = -1;
                    return;

                case 9:
                    ApplyEffect(9, 3, Opcode9Parameter);
                    break;
                case 11:
                    ApplyEffect(11, 1, parameter);
                    break;
                case 12:
                    ApplyEffect(12, 4, parameter);
                    break;
                case 13:
                    ApplyEffect(13, 5, parameter);
                    break;
                case 14:
                    ApplyEffect(14, 2, parameter);
                    break;
                case 15:
                    ApplyEffect(15, 3, parameter);
                    break;
                case 16:
                    ApplyEffect(16, 3, parameter);
                    break;

                default:
                    throw new ArgumentOutOfRangeException(nameof(opcode), opcode,
                        "Only opcodes 9 and 11 to 16 set a graphic effect.");
            }

            //9, 11, 12 and 13 carry no payload, so the stream is the only place they can be stated.
            //14, 15 and 16 are appended by the replay from their re-encoded payload instead.
            if (opcode == 9 || opcode == 11 || opcode == 12 || opcode == 13)
                Opcodes.Add(opcode, Array.Empty<byte>());
        }

        /// <summary>Writes this graphic back to the file representation.</summary>
        /// <remarks>
        ///     442 of the 2,956 records in either cache store their opcodes out of ascending order,
        ///     graphic 0 among them, across 47 distinct sequences - so the recorded order is replayed
        ///     rather than derived. No record repeats an opcode, which is a property of the data and
        ///     not of the format, so the mechanism that would survive one is kept anyway.
        /// </remarks>
        /// <returns>The encoded file, positioned at 0.</returns>
        /// <exception cref="InvalidOperationException">
        ///     A recolour or retexture pair array is a different length from its partner.
        /// </exception>
        public JagStream Encode() {
            RequirePaired(RecolourFrom, RecolourTo, "recolour", 40);
            RequirePaired(RetextureFrom, RetextureTo, "retexture", 41);

            var records = new List<KeyValuePair<int, byte[]>>();

            /* Each block emits when the record carried the opcode OR when the field has moved off
               the client's default. The first arm is what keeps an opcode stored at its own default
               - one record stores scale 128, 17 store ambient 0 and 31 store contrast 0 - instead of
               dropping it and shortening a file nobody edited. */
            if (Opcodes.Has(1) || ModelId != 0)
                records.Add(Payload(1, buffer => buffer.WriteShort(ModelId)));
            if (Opcodes.Has(2) || AnimationId != -1)
                records.Add(Payload(2, buffer => buffer.WriteShort(AnimationId)));
            if (Opcodes.Has(4) || ScaleXZ != DefaultScale)
                records.Add(Payload(4, buffer => buffer.WriteShort(ScaleXZ)));
            if (Opcodes.Has(5) || ScaleY != DefaultScale)
                records.Add(Payload(5, buffer => buffer.WriteShort(ScaleY)));
            if (Opcodes.Has(6) || Rotation != 0)
                records.Add(Payload(6, buffer => buffer.WriteShort(Rotation)));
            if (Opcodes.Has(7) || Ambient != 0)
                records.Add(Payload(7, buffer => buffer.WriteByte((byte) Ambient)));
            if (Opcodes.Has(8) || Contrast != 0)
                records.Add(Payload(8, buffer => buffer.WriteByte((byte) Contrast)));

            /* Only the opcode that actually set the effect is re-encoded. A record carrying two of
               them - 15 then 16, say - keeps the superseded one exactly as it was read, because the
               field pair remembers nothing about the value the first one wrote. */
            if (EffectOpcode == 14)
                records.Add(Payload(14, buffer => buffer.WriteByte((byte) ((EffectParameter >> 8) & 0xFF))));
            else if (EffectOpcode == 15)
                records.Add(Payload(15, buffer => buffer.WriteShort(EffectParameter)));
            else if (EffectOpcode == 16)
                records.Add(Payload(16, buffer => buffer.WriteInteger(EffectParameter)));

            if (Opcodes.Has(40) || RecolourFrom.Length > 0)
                records.Add(Payload(40, buffer => WritePairs(buffer, RecolourFrom, RecolourTo)));
            if (Opcodes.Has(41) || RetextureFrom.Length > 0)
                records.Add(Payload(41, buffer => WritePairs(buffer, RetextureFrom, RetextureTo)));

            return Opcodes.Replay(records, appendInAscendingOrder: true);
        }

        /// <summary>The record in words, for logs and list rows.</summary>
        /// <returns>The id with the model and animation it names.</returns>
        public override string ToString() {
            return "graphic " + Id + ": model " + ModelId +
                   (AnimationId >= 0 ? ", animation " + AnimationId : ", no animation");
        }

        /// <summary>Records that one opcode set the effect, and what it set.</summary>
        /// <param name="opcode">The opcode that did it.</param>
        /// <param name="kind">The effect kind it selects.</param>
        /// <param name="parameter">The parameter it leaves behind.</param>
        private void ApplyEffect(int opcode, int kind, int parameter) {
            EffectOpcode = opcode;
            EffectKind = kind;
            EffectParameter = parameter;
        }

        /// <summary>Writes a count-prefixed run of (from, to) pairs.</summary>
        /// <param name="buffer">The payload being built.</param>
        /// <param name="from">The source values.</param>
        /// <param name="to">The replacement values.</param>
        private static void WritePairs(JagStream buffer, int[] from, int[] to) {
            buffer.WriteByte((byte) from.Length);
            for (int i = 0; i < from.Length; i++) {
                buffer.WriteShort(from[i]);
                buffer.WriteShort(to[i]);
            }
        }

        /// <summary>Refuses a pair of arrays a single count byte cannot describe.</summary>
        /// <param name="from">The source values.</param>
        /// <param name="to">The replacement values.</param>
        /// <param name="noun">What the pair is called, for the message.</param>
        /// <param name="opcode">The opcode that would carry them.</param>
        private void RequirePaired(int[] from, int[] to, string noun, int opcode) {
            if (from.Length == to.Length)
                return;

            throw new InvalidOperationException(
                "Graphic " + Id + " has " + from.Length + " " + noun + " sources against " + to.Length +
                " replacements. Opcode " + opcode + " writes one count for both, so a file cannot " +
                "express the difference.");
        }

        /// <summary>Adds or drops a bare flag opcode.</summary>
        /// <param name="opcode">The flag opcode.</param>
        /// <param name="set">Whether the flag should be present.</param>
        private void SetFlag(int opcode, bool set) {
            if (set == Opcodes.Has(opcode))
                return;

            if (set)
                Opcodes.Add(opcode, Array.Empty<byte>());
            else
                Opcodes.Remove(opcode);
        }

        /// <summary>Builds one opcode's payload into its own buffer.</summary>
        /// <param name="opcode">The opcode the payload belongs to.</param>
        /// <param name="write">Writes the payload.</param>
        /// <returns>The opcode paired with its bytes.</returns>
        private static KeyValuePair<int, byte[]> Payload(int opcode, Action<JagStream> write) {
            var buffer = new JagStream();
            write(buffer);
            return new KeyValuePair<int, byte[]>(opcode, buffer.Flip().ToArray());
        }
    }
}
