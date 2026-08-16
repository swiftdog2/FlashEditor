using System;
using System.Collections.Generic;
using System.IO;
using FlashEditor.IO;

namespace FlashEditor.Definitions.Config {
    /// <summary>
    ///     An identity kit: the models one body part of a player is built from, with the recolour and
    ///     retexture tables applied to them.
    /// </summary>
    /// <remarks>
    ///     JS5 index 2, group <see cref="ConfigGroup.IdentityKit"/>. Decoded by
    ///     <c>Class152.method2480</c> (:265-286) dispatching to <c>method2479</c> (:220-263); the
    ///     provider is <c>Class83</c>, which names the group at Class83.java:158.
    ///     <para>
    ///     Settled by usage: the only consumer is <c>PlayerAppearance</c> (:811, 827, 1167, 1195,
    ///     1360), which builds a body model out of <see cref="ModelIds"/> through
    ///     <c>Class152.method2473</c> or out of <see cref="HeadModelIds"/> through <c>method2476</c>,
    ///     applying both colour tables to the result.
    ///     </para>
    ///     <para>
    ///     Two hazards, both of them client defects rather than format rules. Opcode 1's byte is read
    ///     and thrown away, so it survives only as <see cref="Unknown1"/>; and
    ///     <c>anIntArray1222</c> is <c>int[5]</c> (Class152.java:87) while the dispatcher accepts
    ///     opcodes 60 to 69, so opcodes 65-69 would throw <c>ArrayIndexOutOfBounds</c> in the client.
    ///     See <see cref="HeadModelIds"/>.
    ///     </para>
    /// </remarks>
    public sealed class IdentityKitDefinition : ConfigDefinition {
        /// <summary>
        ///     How many slots opcodes 60 to 69 can address here.
        /// </summary>
        /// <remarks>
        ///     Ten, one per opcode the client's dispatcher accepts, rather than the five its array
        ///     holds. Sizing this to five would make a record carrying opcode 65 undecodable and so
        ///     unable to round-trip, which is a worse failure than storing a slot the 637 client
        ///     cannot reach - and the client cannot reach it either way, because it crashes.
        /// </remarks>
        public const int HeadModelSlots = 10;

        /// <summary>Opcode 1. The byte the client reads and discards, kept verbatim.</summary>
        /// <remarks>
        ///     <c>Class152.java:257</c> calls <c>readUnsignedByte()</c> and assigns it to nothing, so
        ///     no field of the client's record holds it and nothing in the 637 source settles what it
        ///     means. It has to be kept as the stored byte because there is nothing to recompute it
        ///     from. Measured over both caches: it occurs on all 652 records and takes 13 distinct
        ///     values in 0..13, which is consistent with a body-part slot and is not proof of one.
        /// </remarks>
        public int Unknown1 { get; set; } = -1;

        /// <summary>Opcode 2. Model ids in JS5 index 7, in draw order.</summary>
        /// <remarks>
        ///     Loaded and merged into one <c>Model</c> by <c>Class152.method2473</c> (:97-133).
        ///     Measured: 647 records carry one model and 5 carry two.
        /// </remarks>
        public int[]? ModelIds { get; set; }

        /// <summary>Opcode 3. Present in 13 records; the client's dispatcher does nothing with it.</summary>
        /// <remarks>
        ///     Class152.java:226 is an empty arm - the opcode consumes no payload and sets no field -
        ///     so its presence is its whole content and only the opcode list records it. Kept as a
        ///     flag so an edit can add or drop it.
        /// </remarks>
        public bool Unknown3 {
            get => _unknown3;

            /* The setter has to move the opcode as well as the field. Opcode 3 carries no
               payload, so its whole meaning is whether it is in the stream - assigning the
               field alone leaves the record re-encoding to the bytes it already held, which
               is an edit that vanishes with no error anywhere. */
            set {
                _unknown3 = value;
                SetBareOpcode(3, value);
            }
        }

        private bool _unknown3;

        /// <summary>Opcode 40. The colour each entry of <see cref="RecolourTo"/> replaces.</summary>
        /// <remarks>
        ///     <c>aShortArray1219</c>, the first short of each pair and the second argument of
        ///     <c>model.recolor(0, from, to)</c> at Class152.java:206.
        /// </remarks>
        public short[]? RecolourFrom { get; set; }

        /// <summary>Opcode 40. The colour written in place of the matching <see cref="RecolourFrom"/>.</summary>
        public short[]? RecolourTo { get; set; }

        /// <summary>Opcode 41. The texture each entry of <see cref="RetextureTo"/> replaces.</summary>
        /// <remarks>
        ///     <c>aShortArray1224</c>, stored first in each pair. The client passes the pair to
        ///     <c>model.method2590</c> in the opposite order to the one it stores them in
        ///     (Class152.java:211), which is why the two arrays are named from the model call rather
        ///     than from their storage position.
        /// </remarks>
        public short[]? RetextureFrom { get; set; }

        /// <summary>Opcode 41. The texture written in place of the matching <see cref="RetextureFrom"/>.</summary>
        public short[]? RetextureTo { get; set; }

        /// <summary>
        ///     Opcodes 60 to 69. Models drawn on the head, one slot per opcode, -1 for an empty slot.
        /// </summary>
        /// <remarks>
        ///     <c>anIntArray1222</c>, read by <c>Class152.method2476</c>. The client's array holds
        ///     <b>five</b> entries while its dispatcher accepts ten opcodes
        ///     (<c>(i^-1) &lt;= -61 &amp;&amp; (i^-1) &gt; -71</c>, Class152.java:236), so opcodes
        ///     65 to 69 would throw in the client. Measured over both caches: only opcode 60 occurs,
        ///     on 248 records, so the defect is latent rather than live and no sweep exercises the
        ///     upper five slots. This array is ten long so that such a record could still be decoded
        ///     and written back unchanged; that is deliberate and is not a claim that the client
        ///     would load it.
        /// </remarks>
        public int[] HeadModelIds { get; } = CreateHeadModelSlots();

        /// <summary>Decodes one identity kit definition.</summary>
        /// <param name="stream">The definition file.</param>
        /// <returns>This definition.</returns>
        public IdentityKitDefinition DecodeFrom(JagStream stream) {
            Decode(stream);
            return this;
        }

        /// <inheritdoc/>
        protected override void ReadPayload(int opcode, JagStream stream) {
            switch (opcode) {
                case 1: Unknown1 = stream.ReadUnsignedByte(); break;
                case 2: ModelIds = ReadShorts(stream); break;
                case 3: Unknown3 = true; break;

                case 40: {
                    int count = stream.ReadUnsignedByte();
                    RecolourFrom = new short[count];
                    RecolourTo = new short[count];
                    for (int i = 0; i < count; i++) {
                        RecolourFrom[i] = (short) stream.ReadUnsignedShort();
                        RecolourTo[i] = (short) stream.ReadUnsignedShort();
                    }
                    break;
                }

                case 41: {
                    int count = stream.ReadUnsignedByte();
                    RetextureFrom = new short[count];
                    RetextureTo = new short[count];
                    for (int i = 0; i < count; i++) {
                        RetextureFrom[i] = (short) stream.ReadUnsignedShort();
                        RetextureTo[i] = (short) stream.ReadUnsignedShort();
                    }
                    break;
                }

                default:
                    if (opcode < 60 || opcode > 69)
                        throw Unknown(opcode);
                    HeadModelIds[opcode - 60] = stream.ReadUnsignedShort();
                    break;
            }
        }

        /// <inheritdoc/>
        protected override void WritePayload(int opcode, JagStream stream) {
            switch (opcode) {
                case 1: stream.WriteByte(Unknown1); break;
                case 2: WriteShorts(stream, ModelIds); break;
                case 3: break;
                case 40: WritePairs(stream, RecolourFrom, RecolourTo, "recolour"); break;
                case 41: WritePairs(stream, RetextureFrom, RetextureTo, "retexture"); break;

                default:
                    if (opcode < 60 || opcode > 69)
                        throw Unknown(opcode);
                    stream.WriteShort(HeadModelIds[opcode - 60]);
                    break;
            }
        }

        /// <inheritdoc/>
        protected override IEnumerable<int> AddedOpcodes() {
            if (!Has(1) && Unknown1 != -1) yield return 1;
            if (!Has(2) && ModelIds != null) yield return 2;
            if (!Has(3) && Unknown3) yield return 3;
            if (!Has(40) && RecolourFrom != null) yield return 40;
            if (!Has(41) && RetextureFrom != null) yield return 41;

            for (int slot = 0; slot < HeadModelIds.Length; slot++)
                if (!Has(60 + slot) && HeadModelIds[slot] != -1)
                    yield return 60 + slot;
        }

        /// <summary>The empty head-model table, every slot -1 as the client's constructor leaves it.</summary>
        /// <returns>The slots.</returns>
        private static int[] CreateHeadModelSlots() {
            int[] slots = new int[HeadModelSlots];
            for (int i = 0; i < slots.Length; i++)
                slots[i] = -1;
            return slots;
        }

        /// <summary>Reads a byte-counted list of unsigned shorts.</summary>
        /// <param name="stream">The definition file, positioned at the count.</param>
        /// <returns>The values.</returns>
        private static int[] ReadShorts(JagStream stream) {
            int[] values = new int[stream.ReadUnsignedByte()];
            for (int i = 0; i < values.Length; i++)
                values[i] = stream.ReadUnsignedShort();
            return values;
        }

        /// <summary>Writes a byte-counted list of unsigned shorts.</summary>
        /// <param name="stream">The stream to write to.</param>
        /// <param name="values">The values, treated as empty when null.</param>
        private static void WriteShorts(JagStream stream, int[]? values) {
            int[] list = values ?? Array.Empty<int>();
            stream.WriteByte(list.Length);
            foreach (int value in list)
                stream.WriteShort(value);
        }

        /// <summary>
        ///     Writes a byte-counted list of short pairs.
        /// </summary>
        /// <remarks>
        ///     One count sizes both arrays, so a pair of arrays that disagree has no encoding at all.
        ///     Refusing is better than padding: a padded record reads back cleanly and describes a
        ///     colour swap nobody asked for.
        /// </remarks>
        /// <param name="stream">The stream to write to.</param>
        /// <param name="from">The source values.</param>
        /// <param name="to">The replacement values.</param>
        /// <param name="what">The table's name, for the failure message.</param>
        private void WritePairs(JagStream stream, short[]? from, short[]? to, string what) {
            short[] sources = from ?? Array.Empty<short>();
            short[] targets = to ?? Array.Empty<short>();

            if (sources.Length != targets.Length)
                throw new InvalidDataException("Identity kit " + Id + " has " + sources.Length +
                    " " + what + " sources and " + targets.Length + " targets; the opcode stores " +
                    "one count for both.");

            stream.WriteByte(sources.Length);
            for (int i = 0; i < sources.Length; i++) {
                stream.WriteShort(sources[i]);
                stream.WriteShort(targets[i]);
            }
        }
    }
}
