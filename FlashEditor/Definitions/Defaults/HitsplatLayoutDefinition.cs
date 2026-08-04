using System;
using System.Collections.Generic;

namespace FlashEditor.Definitions.Defaults {
    /// <summary>
    ///     Group 3 of JS5 index 28: how many hitsplats an entity can show at once, where each is
    ///     drawn, and the model the hardware renderer benchmarks itself with.
    /// </summary>
    /// <remarks>
    ///     Decoded by <c>Class155.method2495</c> (Class155.java:20-70). The slot count caps the
    ///     hitsplat arrays sized from it at Particle_Sub3_Sub4_Sub2.java:206-241, and the offset
    ///     pairs are added to the overhead draw position at IntegerNode.java:375-377 inside the
    ///     <c>i &lt; Class362.anInt3090</c> loop at :333. Opcode 2's id is preloaded at
    ///     InterfaceSettings.java:239-240 and drawn 500 times by <c>Class66.method683</c> as the FPS
    ///     benchmark, so it is a model id in index 7.
    ///     <para>
    ///     <b>Signedness differs from the sibling group and no sweep can catch getting it wrong.</b>
    ///     Opcode 1 reads <em>signed</em> shorts (Class155.java:39-40, <c>RSBuffer.readShort</c>)
    ///     where <see cref="SceneDefaultsDefinition"/>'s opcodes read unsigned ones. The first value
    ///     that matters is <c>FFEC</c>, which is -20 as a vertical offset and 65516 read unsigned -
    ///     and the file round-trips byte-identically either way. Only the client settles it.
    ///     </para>
    /// </remarks>
    public sealed class HitsplatLayoutDefinition : OpcodeStreamDefinition {
        /// <summary>The group id the client reads this record from.</summary>
        public const int GroupId = 3;

        /// <summary>Slots the client assumes when opcode 3 is absent.</summary>
        /// <remarks>
        ///     Class155.java:35-37 and :54-56 both allocate 4, and the fallback path then synthesises
        ///     the offsets as <c>y = 20 * slot</c>. Absent and present-with-value-4 are therefore
        ///     different bytes for the same decoded state, which is why
        ///     <see cref="StoresSlotCount"/> exists rather than a comparison against this.
        /// </remarks>
        public const int DefaultSlotCount = 4;

        private int slotCount = DefaultSlotCount;

        /// <summary>
        ///     Opcode 3. How many hitsplats an entity can display at once.
        /// </summary>
        /// <remarks>
        ///     This is the count the file stored, which is not necessarily
        ///     <see cref="OffsetX"/>'s length. The client sizes its arrays from whichever opcode it
        ///     met first, so a record writing 1 before 3 has an opcode-1 payload of
        ///     <see cref="DefaultSlotCount"/> pairs and a different count after it. Both are kept as
        ///     the file states them.
        /// </remarks>
        public int SlotCount {
            get => slotCount;
            set => slotCount = value;
        }

        /// <summary>Whether the record stored opcode 3 rather than relying on the client's default.</summary>
        public bool StoresSlotCount => Opcodes.Has(3);

        /// <summary>Opcode 1, first of each pair. Horizontal draw offset per slot.</summary>
        /// <remarks>Null when the record did not carry opcode 1, which the client also allows.</remarks>
        public short[]? OffsetX { get; set; }

        /// <summary>Opcode 1, second of each pair. Vertical draw offset per slot.</summary>
        public short[]? OffsetY { get; set; }

        /// <summary>Opcode 2. The model the hardware renderer draws to benchmark itself.</summary>
        public int BenchmarkModelId { get; set; }

        /// <summary>Whether the record stored a benchmark model id.</summary>
        public bool StoresBenchmarkModel => Opcodes.Has(2);

        /// <summary>Reads the record.</summary>
        /// <param name="stream">The group's single file, positioned at its first opcode.</param>
        /// <returns>This definition.</returns>
        public HitsplatLayoutDefinition Decode(JagStream stream) {
            Opcodes.Clear();
            slotCount = DefaultSlotCount;
            DecodeOpcodeStream(stream);
            return this;
        }

        /// <inheritdoc/>
        protected override bool DecodeOpcode(JagStream stream, int opcode) {
            switch (opcode) {
                case 1: {
                        /* Sized from whatever the count is right now, exactly as the client sizes it
                           from the array it has already allocated. That is what makes the order
                           load bearing: opcode 3 arriving first is what makes this six pairs. */
                        var x = new short[slotCount];
                        var y = new short[slotCount];
                        for (int i = 0; i < slotCount; i++) {
                            x[i] = (short) stream.ReadShort();
                            y[i] = (short) stream.ReadShort();
                        }
                        OffsetX = x;
                        OffsetY = y;
                        return true;
                    }

                case 2:
                    BenchmarkModelId = stream.ReadUnsignedShort();
                    return true;

                case 3:
                    slotCount = stream.ReadUnsignedByte();
                    return true;

                default:
                    return false;
            }
        }

        /// <summary>Writes the record back.</summary>
        /// <remarks>
        ///     Not <see cref="OpcodeStream.Replay"/>, because that appends an opcode the record did
        ///     not carry and opcode 3 cannot go last. It allocates the arrays opcode 1 fills
        ///     (Class155.java:46-48) while opcode 1 loops over the length it finds (:38), so a count
        ///     written after the offsets makes the client read the old number of pairs and then
        ///     throw them away by reallocating. Everything else about the replay is the same:
        ///     recorded order is preserved, the last occurrence of an opcode takes the freshly
        ///     encoded payload, and a superseded occurrence is replayed from its own bytes.
        /// </remarks>
        /// <returns>The encoded file, positioned at 0.</returns>
        public JagStream Encode() {
            var fresh = new Dictionary<int, byte[]>();

            if (OffsetX is short[] x && OffsetY is short[] y) {
                fresh[1] = Payload(buffer => {
                    for (int i = 0; i < x.Length && i < y.Length; i++) {
                        buffer.WriteShort(x[i]);
                        buffer.WriteShort(y[i]);
                    }
                });
            }

            if (StoresBenchmarkModel || BenchmarkModelId != 0)
                fresh[2] = Payload(buffer => buffer.WriteShort(BenchmarkModelId));

            if (StoresSlotCount || slotCount != DefaultSlotCount)
                fresh[3] = Payload(buffer => buffer.WriteByte((byte) slotCount));

            var output = new JagStream();
            var written = new HashSet<int>();

            void Put(int opcode, byte[] payload) {
                output.WriteByte((byte) opcode);
                if (payload.Length > 0)
                    output.Write(payload, 0, payload.Length);
            }

            bool leadWithCount = fresh.ContainsKey(3) && !Opcodes.Has(3);
            if (leadWithCount) {
                Put(3, fresh[3]);
                written.Add(3);
            }

            for (int i = 0; i < Opcodes.Count; i++) {
                OpcodeRecord record = Opcodes[i];
                if (Opcodes.IsLastOccurrence(i) && fresh.TryGetValue(record.Opcode, out byte[]? payload)) {
                    Put(record.Opcode, payload);
                    written.Add(record.Opcode);
                }
                else {
                    Put(record.Opcode, record.Payload);
                }
            }

            //Anything the recorded stream did not carry, in ascending order so a record built from
            //nothing still encodes predictably. Opcode 3 has already been written when it had to
            //lead, so it cannot land here twice.
            foreach (int opcode in new[] { 1, 2, 3 }) {
                if (written.Contains(opcode) || !fresh.TryGetValue(opcode, out byte[]? added))
                    continue;

                written.Add(opcode);
                Put(opcode, added);
            }

            output.WriteByte(0);
            return output.Flip();
        }

        /// <summary>Builds one opcode's payload into its own buffer.</summary>
        /// <param name="write">Writes the payload.</param>
        /// <returns>The payload bytes.</returns>
        private static byte[] Payload(Action<JagStream> write) {
            var buffer = new JagStream();
            write(buffer);
            return buffer.Flip().ToArray();
        }
    }
}
