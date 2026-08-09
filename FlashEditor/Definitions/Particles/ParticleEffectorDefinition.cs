using System;
using System.Collections.Generic;
using FlashEditor.IO;

namespace FlashEditor.Definitions.Particles {
    /// <summary>
    ///     One particle effector from group 1 of JS5 index 27: a force field that pushes, pulls or
    ///     displaces the particles an emitter has already spawned.
    /// </summary>
    /// <remarks>
    ///     An effector id is a file id within group 1 - <c>Class21.method263</c> fetches it as
    ///     <c>getChildFromFolder(1, id)</c> (Class21.java:51). Opcode table from
    ///     <c>Class66.method686</c> (Class66.java:284-325), driven by the loop in <c>method682</c>
    ///     (:209-235).
    ///     <para>
    ///     It is a force field rather than anything visual, settled from what the client does with
    ///     it: Particle_Sub4_Sub2_Sub1.java:129-241 uses the opcode-3 vector, the opcode-4 falloff
    ///     law and the cone threshold derived from opcode 1 to accumulate acceleration on each
    ///     particle in range.
    ///     </para>
    ///     <para>
    ///     <b>Opcode 4 carries the falloff pair, not opcode 5.</b> The client spells the test as
    ///     <c>(i ^ 0xffffffff) == -5</c>, which is <c>i == 4</c>. Reading it as 5 mis-sizes half the
    ///     records in this index.
    ///     </para>
    /// </remarks>
    public sealed class ParticleEffectorDefinition : OpcodeStreamDefinition {
        /// <summary>The group effectors live in.</summary>
        public const int GroupId = 1;

        /// <summary>
        ///     How far left the client shifts opcode 1 before indexing its trigonometric table.
        /// </summary>
        /// <remarks>
        ///     Class66.java:245 reads <c>anIntArray6202[anInt510 &lt;&lt; 3]</c>, so the stored value
        ///     is a 1/8 step of the client's 14-bit angle index.
        /// </remarks>
        public const int ConeAngleShift = 3;

        /// <summary>The <see cref="Mode"/> value that registers an effector globally.</summary>
        /// <remarks>
        ///     Class21.java:58-61 puts a mode-2 effector into the 16-slot array at Class336.java:22,
        ///     which is what makes it reachable from an emitter's opcode-10 list rather than only
        ///     from the effectors placed in the scene.
        /// </remarks>
        public const int GlobalMode = 2;

        /// <summary>The effector id, which is its file id within group 1.</summary>
        public int Id { get; set; } = -1;

        /// <summary>
        ///     Opcode 1. Half-angle of the cone the effector acts inside, as stored.
        /// </summary>
        /// <remarks>
        ///     Class66.java:245 turns it into a cosine threshold that
        ///     Particle_Sub4_Sub2_Sub1.java:158-164 compares the normalised particle direction
        ///     against, so a particle outside the cone is left alone entirely.
        /// </remarks>
        public int ConeAngleStored { get; set; }

        /// <summary>
        ///     Opcode 2. A byte the 637 client reads and drops.
        /// </summary>
        /// <remarks>
        ///     Class66.java:290 is a bare <c>readUnsignedByte()</c> with no assignment. Neither
        ///     supported cache stores it.
        /// </remarks>
        public int UnusedByte2 { get; set; }

        /// <summary>Opcode 3, first field. Force vector, x.</summary>
        /// <remarks>
        ///     Class66.java:255-259 takes the length of the three together as the force magnitude,
        ///     and Particle_Sub4_Sub2_Sub1.java:174-186 adds the vector straight onto the particle.
        /// </remarks>
        public int DirectionX { get; set; }

        /// <summary>Opcode 3, second field. Force vector, y.</summary>
        public int DirectionY { get; set; }

        /// <summary>Opcode 3, third field. Force vector, z.</summary>
        public int DirectionZ { get; set; }

        /// <summary>
        ///     Opcode 4, first field. Which distance law scales the force, and whether it is bounded.
        /// </summary>
        /// <remarks>
        ///     Class66.java:260-270 and Particle_Sub4_Sub2_Sub1.java:166-171 agree on three values: 0
        ///     is unbounded and unscaled, 1 falls off with distance, 2 with distance squared. The
        ///     mode also sets the radius past which a particle is ignored.
        /// </remarks>
        public int FalloffMode { get; set; }

        /// <summary>
        ///     Opcode 4, second field. Divisor of the falloff law.
        /// </summary>
        /// <remarks>
        ///     Class66.java:257-259 replaces a stored 0 with 1 to avoid dividing by zero, which is a
        ///     load-time repair rather than a stored value - the 0 is kept here as the file has it.
        /// </remarks>
        public int Strength { get; set; }

        /// <summary>
        ///     Opcode 6. How the effector is reached: <see cref="GlobalMode"/> registers it globally.
        /// </summary>
        /// <remarks>
        ///     Also read on the scene path - Particle_Sub4_Sub2_Sub1.java:136 skips an effector whose
        ///     mode is 1 there, and Particle_Sub5.java:207 registers exactly those into the
        ///     attachment table an emitter's opcode 25 looks in.
        /// </remarks>
        public int Mode { get; set; }

        /// <summary>
        ///     Opcode 5. Present in no supported cache and handled by nothing in the 637 client.
        /// </summary>
        /// <remarks>
        ///     Like the emitter's opcode 11 it falls through every branch and reads no payload, so
        ///     the loop takes the following byte as the next opcode. Reproduced rather than
        ///     corrected: a width invented for it would desynchronise the first file that carries it.
        /// </remarks>
        public bool HasUnhandledFlag5 {
            get => Opcodes.Has(5);
            set => SetFlag(5, value);
        }

        /// <summary>Opcode 7. The other unhandled, payload-free opcode, on the same terms as opcode 5.</summary>
        public bool HasUnhandledFlag7 {
            get => Opcodes.Has(7);
            set => SetFlag(7, value);
        }

        /// <summary>Opcode 8. Displaces a particle's position rather than its velocity.</summary>
        /// <remarks>
        ///     Particle_Sub4_Sub2_Sub1.java:173-199 branches on it: cleared, the force is added to
        ///     the velocity accumulator; set, it moves the particle directly and leaves its velocity
        ///     alone.
        /// </remarks>
        public bool MovesPositionRatherThanVelocity {
            get => Opcodes.Has(8);
            set => SetFlag(8, value);
        }

        /// <summary>Opcode 9. Applies the force along the line from the effector instead of the stored vector.</summary>
        /// <remarks>
        ///     Particle_Sub4_Sub2_Sub1.java:186-198 replaces the opcode-3 vector with the normalised
        ///     particle-to-effector direction scaled by the vector's own length, which turns a
        ///     directional wind into a radial push.
        /// </remarks>
        public bool IsRadial {
            get => Opcodes.Has(9);
            set => SetFlag(9, value);
        }

        /// <summary>Opcode 10. Negates the force magnitude, turning a push into a pull.</summary>
        /// <remarks>
        ///     Class66.java:270-272 flips the sign of the derived magnitude, which reverses the
        ///     radial direction and inverts the cone test with it.
        /// </remarks>
        public bool IsInverted {
            get => Opcodes.Has(10);
            set => SetFlag(10, value);
        }

        /// <summary>Reads one effector record from its file.</summary>
        /// <param name="stream">The file, positioned at its first opcode.</param>
        /// <returns>This definition.</returns>
        public ParticleEffectorDefinition Decode(JagStream stream) {
            Opcodes.Clear();
            DecodeOpcodeStream(stream);
            return this;
        }

        /// <inheritdoc/>
        protected override bool DecodeOpcode(JagStream stream, int opcode) {
            switch (opcode) {
                case 1:
                    ConeAngleStored = stream.ReadUnsignedShort();
                    return true;

                case 2:
                    UnusedByte2 = stream.ReadUnsignedByte();
                    return true;

                case 3:
                    DirectionX = stream.ReadInt();
                    DirectionY = stream.ReadInt();
                    DirectionZ = stream.ReadInt();
                    return true;

                case 4:
                    FalloffMode = stream.ReadUnsignedByte();
                    Strength = stream.ReadInt();
                    return true;

                case 6:
                    Mode = stream.ReadUnsignedByte();
                    return true;

                //5 and 7 have no handler at all; 8, 9 and 10 are bare flags. All five read nothing.
                case 5:
                case 7:
                case 8:
                case 9:
                case 10:
                    return true;

                default:
                    return false;
            }
        }

        /// <summary>Writes this effector back to the file representation.</summary>
        /// <returns>The encoded file, positioned at 0.</returns>
        public JagStream Encode() {
            var records = new List<KeyValuePair<int, byte[]>>();

            if (Opcodes.Has(1) || ConeAngleStored != 0)
                records.Add(Payload(1, buffer => buffer.WriteShort(ConeAngleStored)));
            if (Opcodes.Has(2))
                records.Add(Payload(2, buffer => buffer.WriteByte((byte) UnusedByte2)));

            if (Opcodes.Has(3) || DirectionX != 0 || DirectionY != 0 || DirectionZ != 0) {
                records.Add(Payload(3, buffer => {
                    buffer.WriteInteger(DirectionX);
                    buffer.WriteInteger(DirectionY);
                    buffer.WriteInteger(DirectionZ);
                }));
            }

            if (Opcodes.Has(4) || FalloffMode != 0 || Strength != 0) {
                records.Add(Payload(4, buffer => {
                    buffer.WriteByte((byte) FalloffMode);
                    buffer.WriteInteger(Strength);
                }));
            }

            if (Opcodes.Has(6) || Mode != 0)
                records.Add(Payload(6, buffer => buffer.WriteByte((byte) Mode)));

            //5, 7, 8, 9 and 10 are not listed: they carry no payload, so the recorded stream is the
            //only statement of whether they are set.
            return Opcodes.Replay(records, appendInAscendingOrder: true);
        }

        /// <summary>Takes a copy no edit through this instance can reach.</summary>
        /// <returns>An independent definition holding the same values.</returns>
        public ParticleEffectorDefinition Clone() {
            var copy = (ParticleEffectorDefinition) MemberwiseClone();
            copy.DetachOpcodeStream();
            return copy;
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
