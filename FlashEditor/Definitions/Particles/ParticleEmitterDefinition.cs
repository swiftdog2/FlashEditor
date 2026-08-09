using System;
using System.Collections.Generic;
using FlashEditor.IO;

namespace FlashEditor.Definitions.Particles {
    /// <summary>
    ///     One particle emitter from group 0 of JS5 index 27: what a particle system spawns, how
    ///     fast, for how long, and how its colour and size change over a particle's life.
    /// </summary>
    /// <remarks>
    ///     An emitter id is a file id within group 0 - <c>ParticleType.list</c> fetches it as
    ///     <c>getChildFromFolder(0, id)</c> (ParticleType.java:11). Opcode table from
    ///     <c>ParticleType.method895</c> (ParticleType.java:519-664), driven by the loop in
    ///     <c>method894</c> (:489-505).
    ///     <para>
    ///     Only what the file stores lives here. <c>ParticleType.method897</c> (:668-760) derives a
    ///     further twenty fields from these - the per-channel colour deltas, the interpolation rates,
    ///     the "has a height bound" flag - and every one of them is recomputed from the stored values
    ///     at load. Storing a derived field would give the encoder two sources for the same bytes and
    ///     no way to tell which the file came from.
    ///     </para>
    ///     <para>
    ///     <b>Order capture is mandatory on this index, not defensive.</b> Not one record in either
    ///     supported cache stores its opcodes in ascending order, so an encoder emitting its own
    ///     order reproduces none of them.
    ///     </para>
    /// </remarks>
    public sealed class ParticleEmitterDefinition : OpcodeStreamDefinition {
        /// <summary>The group emitters live in.</summary>
        public const int GroupId = 0;

        /// <summary>
        ///     How far left the client shifts each stored angle bound on opcode 1.
        /// </summary>
        /// <remarks>
        ///     ParticleType.java:527-533 shifts all four by 3 into a <c>short</c>, so the client's
        ///     angle is a 14-bit direction index. The stored value is kept instead of the shifted one
        ///     because the shift is into a signed 16-bit field and therefore not invertible for every
        ///     stored value.
        /// </remarks>
        public const int AngleShift = 3;

        /// <summary>
        ///     How far left the client shifts the stored size bounds on opcodes 5, 27 and 31.
        /// </summary>
        /// <remarks>
        ///     Written as two shifts by an obfuscated constant in the client
        ///     (ParticleType.java:541, :593-596, :614-615); both resolve to 12 then 2. Sizes are
        ///     therefore a 1/16384 fixed-point scale.
        /// </remarks>
        public const int SizeShift = 14;

        /// <summary>Value opcode 15 stores when the emitter names no material.</summary>
        public const int NoMaterial = -1;

        /// <summary>Percentage of a particle's life the client assumes for the speed ramp.</summary>
        public const int DefaultRampPercent = 100;

        private int sizeMinStored;
        private int sizeMaxStored;

        /// <summary>The emitter id, which is its file id within group 0.</summary>
        public int Id { get; set; } = -1;

        // ===================================================================
        //  Spawn direction and rate
        // ===================================================================

        /// <summary>Opcode 1, first field. Lower yaw bound, as stored.</summary>
        /// <remarks>
        ///     Particle_Sub9.java:213-216 spreads the spawn direction uniformly between this and
        ///     <see cref="YawEndStored"/>. Shift by <see cref="AngleShift"/> for the client's value.
        /// </remarks>
        public int YawStartStored { get; set; }

        /// <summary>Opcode 1, second field. Upper yaw bound, as stored.</summary>
        public int YawEndStored { get; set; }

        /// <summary>Opcode 1, third field. Lower pitch bound, as stored.</summary>
        /// <remarks>Particle_Sub9.java:217-218 pairs it with <see cref="PitchEndStored"/>.</remarks>
        public int PitchStartStored { get; set; }

        /// <summary>Opcode 1, fourth field. Upper pitch bound, as stored.</summary>
        public int PitchEndStored { get; set; }

        /// <summary>
        ///     Opcode 2. A byte the 637 client reads and drops.
        /// </summary>
        /// <remarks>
        ///     ParticleType.java:534 is a bare <c>readUnsignedByte()</c> with no assignment, so its
        ///     meaning is unknown and deliberately not guessed at. Neither supported cache stores it.
        /// </remarks>
        public int UnusedByte2 { get; set; }

        /// <summary>Opcode 3, first field. Lower spawn-speed bound.</summary>
        /// <remarks>
        ///     Particle_Sub9.java:275-277 picks a speed uniformly in this range and hands it to the
        ///     particle as its velocity magnitude (Particle_Sub4_Sub2_Sub1.java:29).
        /// </remarks>
        public int SpeedMin { get; set; }

        /// <summary>Opcode 3, second field. Upper spawn-speed bound.</summary>
        public int SpeedMax { get; set; }

        /// <summary>
        ///     Opcode 4, first field. Which distance law slows a particle down.
        /// </summary>
        /// <remarks>
        ///     Particle_Sub4_Sub2_Sub1.java:111-126: 1 damps by the distance from the emitter, 2 by
        ///     its square, anything else not at all.
        /// </remarks>
        public int DragMode { get; set; }

        /// <summary>Opcode 4, second field. How hard the drag law bites, signed.</summary>
        public sbyte DragStrength { get; set; }

        /// <summary>Opcode 8, first field. Lower spawn-rate bound.</summary>
        /// <remarks>
        ///     Particle_Sub9.java:221-223 accumulates a random value in this range per elapsed unit
        ///     and spawns one particle per 64 accumulated, so the pair is a rate in 1/64 particles.
        /// </remarks>
        public int SpawnRateMin { get; set; }

        /// <summary>Opcode 8, second field. Upper spawn-rate bound.</summary>
        public int SpawnRateMax { get; set; }

        /// <summary>
        ///     Opcode 14. How many spawn steps run the first time the system is drawn.
        /// </summary>
        /// <remarks>
        ///     Particle_Sub5.java:158-160 runs the emitter this many extra times on the frame the
        ///     system starts, so a long-lived cloud appears already populated rather than growing
        ///     from nothing.
        /// </remarks>
        public int PrimeSteps { get; set; }

        // ===================================================================
        //  Lifetime, size and colour
        // ===================================================================

        /// <summary>Opcode 7, first field. Lower particle lifetime bound, in ticks.</summary>
        /// <remarks>
        ///     Particle_Sub9.java:278-280 picks a lifetime in this range;
        ///     Particle_Sub4_Sub2_Sub1.java:36-38 counts it down and destroys the particle at zero.
        /// </remarks>
        public int LifetimeMin { get; set; }

        /// <summary>
        ///     Opcode 7, second field. Upper particle lifetime bound, in ticks.
        /// </summary>
        /// <remarks>
        ///     Also the base every ramp percentage is taken of - ParticleType.java:696-697, :735 and
        ///     :750 all scale it by a percentage opcode rather than the per-particle lifetime.
        /// </remarks>
        public int LifetimeMax { get; set; }

        /// <summary>Opcodes 5 and 31, first field. Lower spawn-size bound, as stored.</summary>
        /// <remarks>
        ///     Shift by <see cref="SizeShift"/> for the client's fixed-point value. Setting it apart
        ///     from <see cref="SizeMaxStored"/> drops opcode 5, which cannot express two bounds - see
        ///     <see cref="Encode"/>.
        /// </remarks>
        public int SizeMinStored {
            get => sizeMinStored;
            set {
                sizeMinStored = value;
                ReconcileSizeAlias();
            }
        }

        /// <summary>Opcode 31, second field. Upper spawn-size bound, as stored.</summary>
        public int SizeMaxStored {
            get => sizeMaxStored;
            set {
                sizeMaxStored = value;
                ReconcileSizeAlias();
            }
        }

        /// <summary>
        ///     Whether the file stored the size bounds as opcode 5's single value rather than
        ///     opcode 31's pair.
        /// </summary>
        /// <remarks>
        ///     The two are aliases for the same fields, so the decoded values cannot say which was
        ///     used and both occur in this cache. Kept as a view over the recorded stream, which is
        ///     the only statement of it.
        /// </remarks>
        public bool StoresSizeAsASingleValue => Opcodes.Has(5) && !Opcodes.Has(31);

        /// <summary>Opcode 27. Size a particle ramps towards, as stored, or -1 for no ramp.</summary>
        /// <remarks>
        ///     ParticleType.java:733-742 turns it into a per-tick rate over
        ///     <see cref="SizeRampPercent"/> of <see cref="LifetimeMax"/>; the guard there is against
        ///     -1, which is also the constructor default.
        /// </remarks>
        public int EndSizeStored { get; set; } = -1;

        /// <summary>Opcode 28. Percentage of the maximum lifetime the size ramp spans.</summary>
        public int SizeRampPercent { get; set; } = DefaultRampPercent;

        /// <summary>Opcode 22. Speed a particle ramps towards, or -1 for no ramp.</summary>
        /// <remarks>ParticleType.java:744-755, guarded against -1 the same way.</remarks>
        public int EndSpeed { get; set; } = -1;

        /// <summary>Opcode 23. Percentage of the maximum lifetime the speed ramp spans.</summary>
        public int SpeedRampPercent { get; set; } = DefaultRampPercent;

        /// <summary>Opcode 6, first field. Colour a particle spawns with, packed ARGB.</summary>
        /// <remarks>
        ///     ParticleType.java:681-696 unpacks both this and <see cref="SpawnColourEnd"/> into
        ///     per-channel bases and deltas; a particle takes a random point between them.
        /// </remarks>
        public int SpawnColourStart { get; set; }

        /// <summary>Opcode 6, second field. Far end of the spawn-colour range, packed ARGB.</summary>
        public int SpawnColourEnd { get; set; }

        /// <summary>Opcode 18. Colour a particle ramps towards, packed ARGB, or 0 for no ramp.</summary>
        /// <remarks>
        ///     ParticleType.java:695 gates the whole colour ramp on this being non-zero, so 0 is the
        ///     "no ramp" value rather than black.
        /// </remarks>
        public int FadeColour { get; set; }

        /// <summary>Opcode 20. Percentage of the maximum lifetime the RGB fade spans.</summary>
        public int FadeColourPercent { get; set; } = DefaultRampPercent;

        /// <summary>Opcode 21. Percentage of the maximum lifetime the alpha fade spans.</summary>
        public int FadeAlphaPercent { get; set; } = DefaultRampPercent;

        /// <summary>Opcode 15. Material id drawn for each particle, or -1 for an untextured quad.</summary>
        /// <remarks>Reaches the particle as its material at Particle_Sub9.java:301-306.</remarks>
        public int MaterialId { get; set; } = NoMaterial;

        // ===================================================================
        //  Effectors and detail
        // ===================================================================

        /// <summary>
        ///     Opcode 9. Effector ids searched for among the effectors placed in the scene.
        /// </summary>
        /// <remarks>
        ///     Particle_Sub4_Sub2_Sub1.java:129-145 walks every effector instance the scene holds and
        ///     matches its definition id against this list, which is why these are ids into group 1
        ///     of this index. Null when the file carried no opcode 9.
        /// </remarks>
        public int[]? SceneEffectorIds { get; set; }

        /// <summary>
        ///     Opcode 10. Effector ids resolved through the global effector registry.
        /// </summary>
        /// <remarks>
        ///     Particle_Sub4_Sub2_Sub1.java:283-295 loads each through <c>Class21.method263</c>,
        ///     which is what registers an effector whose <see cref="ParticleEffectorDefinition.Mode"/>
        ///     is 2 into the 16-slot global array. Also ids into group 1.
        /// </remarks>
        public int[]? GlobalEffectorIds { get; set; }

        /// <summary>
        ///     Opcode 25. Attachment keys of effectors looked up directly rather than searched for.
        /// </summary>
        /// <remarks>
        ///     <b>Not effector definition ids.</b> Particle_Sub4_Sub2_Sub1.java:209-213 looks each up
        ///     in the table Particle_Sub5.java:207-211 fills, and the key inserted there is the model
        ///     attachment's own id, not the effector's. Neither supported cache stores opcode 25.
        /// </remarks>
        public int[]? AttachedEffectorKeys { get; set; }

        /// <summary>Opcode 17. Emitter substituted for this one on the software renderer, or -1.</summary>
        /// <remarks>Particle_Sub9.java:106-108 swaps the whole definition for it.</remarks>
        public int LowDetailEmitterId { get; set; } = -1;

        /// <summary>Opcode 19. Lowest particle-detail setting at which this emitter runs.</summary>
        /// <remarks>Particle_Sub9.java:148 suppresses the emitter entirely below it.</remarks>
        public int MinimumDetailLevel { get; set; }

        // ===================================================================
        //  Duty cycle
        // ===================================================================

        /// <summary>Opcode 16, first field. Stored byte deciding which half of the cycle emits.</summary>
        /// <remarks>
        ///     Particle_Sub9.java:161-166 compares only against 1 (<c>== 1</c> at
        ///     ParticleType.java:648), so every other stored value means the same thing to the client
        ///     but not to a re-encode. Kept as the byte rather than as the bool
        ///     <see cref="EmitsBeforeThreshold"/> exposes.
        /// </remarks>
        public int CycleFlagStored { get; set; } = 1;

        /// <summary>Whether the emitter is active before rather than after the cycle threshold.</summary>
        public bool EmitsBeforeThreshold => CycleFlagStored == 1;

        /// <summary>Opcode 16, second field. Point in the cycle the emitter switches at.</summary>
        public int CycleThreshold { get; set; } = -1;

        /// <summary>Opcode 16, third field. Cycle length in client cycles, or -1 for no cycle.</summary>
        /// <remarks>
        ///     Particle_Sub9.java:153-159 takes the elapsed time modulo this, and the elapsed time it
        ///     is handed is the client's 20 ms cycle counter rather than a wall clock - see
        ///     <c>ParticleUnits.MillisecondsPerCycle</c>. So a stored period is in the same unit as a
        ///     lifetime, and a duty cycle needs no conversion of its own.
        /// </remarks>
        public int CyclePeriod { get; set; } = -1;

        /// <summary>Opcode 16, fourth field. Stored byte deciding whether the cycle repeats.</summary>
        /// <remarks>Same <c>== 1</c> comparison as <see cref="CycleFlagStored"/>, kept raw for the same reason.</remarks>
        public int CycleRepeatsStored { get; set; } = 1;

        /// <summary>Whether the cycle runs more than once.</summary>
        public bool CycleRepeats => CycleRepeatsStored == 1;

        /// <summary>
        ///     Opcode 29. A signed short the 637 client reads and drops.
        /// </summary>
        /// <remarks>
        ///     ParticleType.java:610 is a bare <c>readShort()</c>. Neither supported cache stores it.
        /// </remarks>
        public int UnusedShort29 { get; set; }

        // ===================================================================
        //  Height bounds and flags
        // ===================================================================

        /// <summary>Opcode 12. Plane above which a particle is destroyed, or -2 for none.</summary>
        /// <remarks>
        ///     Particle_Sub4_Sub2_Sub1.java:371-383: -1 means the plane the particle is on, 0 and up
        ///     name an absolute plane, and -2 - the constructor default - disables the test.
        /// </remarks>
        public int CeilingPlane { get; set; } = -2;

        /// <summary>Opcode 13. Plane below which a particle is destroyed, or -2 for none.</summary>
        public int FloorPlane { get; set; } = -2;

        /// <summary>
        ///     Opcode 11. Present in no supported cache and handled by nothing in the 637 client.
        /// </summary>
        /// <remarks>
        ///     It falls through every branch of <c>ParticleType.method895</c> and reads no payload, so
        ///     the loop takes the following byte as the next opcode. That is reproduced rather than
        ///     corrected: giving it a width would desynchronise the stream on the first file that
        ///     carries it, and nothing in either cache would catch that.
        /// </remarks>
        public bool HasUnhandledFlag11 {
            get => Opcodes.Has(11);
            set => SetFlag(11, value);
        }

        /// <summary>
        ///     Opcode 24. Randomises each colour channel separately rather than together.
        /// </summary>
        /// <remarks>
        ///     Particle_Sub9.java:284-297: without it one random factor drives red, green and blue,
        ///     so a particle's colour lies on the line between the two spawn colours. With it each
        ///     channel is drawn independently and the colour lies anywhere in the box between them.
        ///     <para>
        ///     <b>It occurs twice in some records</b>, which is legal because it carries no payload -
        ///     the bytes are literally <c>18 18</c>. Both occurrences are replayed from the stream.
        ///     </para>
        /// </remarks>
        public bool RandomisesColourChannelsIndependently {
            get => Opcodes.Has(24);
            set => SetFlag(24, value);
        }

        /// <summary>
        ///     Opcode 26. A flag the 637 client decodes, passes on, and never reads.
        /// </summary>
        /// <remarks>
        ///     It reaches the particle as the thirteenth argument of
        ///     <c>Particle_Sub4_Sub2_Sub1.method3112</c> (:496) and of the constructor (:17), and
        ///     neither body uses that parameter. Its meaning cannot be settled from this client.
        /// </remarks>
        public bool UnusedFlag26 {
            get => Opcodes.Has(26);
            set => SetFlag(26, value);
        }

        /// <summary>Opcode 30. Keeps the material on the software renderer.</summary>
        /// <remarks>
        ///     Particle_Sub9.java:302-304 blanks the material id unless this is set whenever the
        ///     hardware renderer is not in use.
        /// </remarks>
        public bool KeepsMaterialOnSoftwareRenderer {
            get => Opcodes.Has(30);
            set => SetFlag(30, value);
        }

        /// <summary>Opcode 32. Clears the flag that groups particles into a draw batch.</summary>
        /// <remarks>
        ///     Reaches the particle as <c>aBoolean6174</c> and both sorters break the batch when it
        ///     changes (Class360.java:415-421, Class81.java:572-577), alongside a change of material.
        /// </remarks>
        public bool BreaksTheDrawBatch {
            get => Opcodes.Has(32);
            set => SetFlag(32, value);
        }

        /// <summary>Opcode 33. Destroys a particle that meets scene geometry.</summary>
        /// <remarks>Particle_Sub4_Sub2_Sub1.java:438-448 tests the particle against the tile's collision shape.</remarks>
        public bool DiesOnCollision {
            get => Opcodes.Has(33);
            set => SetFlag(33, value);
        }

        /// <summary>Opcode 34. Lets a particle survive below the ground plane.</summary>
        /// <remarks>
        ///     Particle_Sub4_Sub2_Sub1.java:405-407 destroys a particle that falls below plane 0
        ///     unless this opcode cleared the flag.
        /// </remarks>
        public bool SurvivesBelowTheGround {
            get => Opcodes.Has(34);
            set => SetFlag(34, value);
        }

        // ===================================================================
        //  Codec
        // ===================================================================

        /// <summary>Reads one emitter record from its file.</summary>
        /// <param name="stream">The file, positioned at its first opcode.</param>
        /// <returns>This definition.</returns>
        public ParticleEmitterDefinition Decode(JagStream stream) {
            Opcodes.Clear();
            DecodeOpcodeStream(stream);
            return this;
        }

        /// <inheritdoc/>
        protected override bool DecodeOpcode(JagStream stream, int opcode) {
            switch (opcode) {
                case 1:
                    YawStartStored = stream.ReadUnsignedShort();
                    YawEndStored = stream.ReadUnsignedShort();
                    PitchStartStored = stream.ReadUnsignedShort();
                    PitchEndStored = stream.ReadUnsignedShort();
                    return true;

                case 2:
                    UnusedByte2 = stream.ReadUnsignedByte();
                    return true;

                case 3:
                    SpeedMin = stream.ReadInt();
                    SpeedMax = stream.ReadInt();
                    return true;

                case 4:
                    DragMode = stream.ReadUnsignedByte();
                    DragStrength = stream.ReadSignedByte();
                    return true;

                //Opcode 5 sets both size bounds from one value; opcode 31 gives each its own.
                //Assigning the fields rather than the properties so decoding the alias does not
                //trip the reconciliation that exists for edits.
                case 5:
                    sizeMinStored = sizeMaxStored = stream.ReadUnsignedShort();
                    return true;

                case 6:
                    SpawnColourStart = stream.ReadInt();
                    SpawnColourEnd = stream.ReadInt();
                    return true;

                case 7:
                    LifetimeMin = stream.ReadUnsignedShort();
                    LifetimeMax = stream.ReadUnsignedShort();
                    return true;

                case 8:
                    SpawnRateMin = stream.ReadUnsignedShort();
                    SpawnRateMax = stream.ReadUnsignedShort();
                    return true;

                case 9:
                    SceneEffectorIds = ReadIdList(stream);
                    return true;

                case 10:
                    GlobalEffectorIds = ReadIdList(stream);
                    return true;

                //11 has no handler in the client and reads nothing. See HasUnhandledFlag11.
                case 11:
                    return true;

                case 12:
                    CeilingPlane = stream.ReadSignedByte();
                    return true;

                case 13:
                    FloorPlane = stream.ReadSignedByte();
                    return true;

                case 14:
                    PrimeSteps = stream.ReadUnsignedShort();
                    return true;

                case 15:
                    MaterialId = stream.ReadUnsignedShort();
                    return true;

                case 16:
                    CycleFlagStored = stream.ReadUnsignedByte();
                    CycleThreshold = stream.ReadUnsignedShort();
                    CyclePeriod = stream.ReadUnsignedShort();
                    CycleRepeatsStored = stream.ReadUnsignedByte();
                    return true;

                case 17:
                    LowDetailEmitterId = stream.ReadUnsignedShort();
                    return true;

                case 18:
                    FadeColour = stream.ReadInt();
                    return true;

                case 19:
                    MinimumDetailLevel = stream.ReadUnsignedByte();
                    return true;

                case 20:
                    FadeColourPercent = stream.ReadUnsignedByte();
                    return true;

                case 21:
                    FadeAlphaPercent = stream.ReadUnsignedByte();
                    return true;

                case 22:
                    EndSpeed = stream.ReadInt();
                    return true;

                case 23:
                    SpeedRampPercent = stream.ReadUnsignedByte();
                    return true;

                //24, 26, 30, 32, 33 and 34 are bare flags: presence is the whole payload.
                case 24:
                case 26:
                case 30:
                case 32:
                case 33:
                case 34:
                    return true;

                case 25:
                    AttachedEffectorKeys = ReadIdList(stream);
                    return true;

                case 27:
                    EndSizeStored = stream.ReadUnsignedShort();
                    return true;

                case 28:
                    SizeRampPercent = stream.ReadUnsignedByte();
                    return true;

                case 29:
                    UnusedShort29 = stream.ReadShort();
                    return true;

                case 31:
                    sizeMinStored = stream.ReadUnsignedShort();
                    sizeMaxStored = stream.ReadUnsignedShort();
                    return true;

                default:
                    return false;
            }
        }

        /// <summary>Writes this emitter back to the file representation.</summary>
        /// <remarks>
        ///     Each block emits when the record carried the opcode <em>or</em> when the field has
        ///     moved off the client's constructor default. The first arm is what keeps an opcode
        ///     stored at its own default rather than dropping it and shortening a file nobody edited.
        /// </remarks>
        /// <returns>The encoded file, positioned at 0.</returns>
        public JagStream Encode() {
            var records = new List<KeyValuePair<int, byte[]>>();

            if (Opcodes.Has(1) || YawStartStored != 0 || YawEndStored != 0 ||
                PitchStartStored != 0 || PitchEndStored != 0) {
                records.Add(Payload(1, buffer => {
                    buffer.WriteShort(YawStartStored);
                    buffer.WriteShort(YawEndStored);
                    buffer.WriteShort(PitchStartStored);
                    buffer.WriteShort(PitchEndStored);
                }));
            }

            if (Opcodes.Has(2))
                records.Add(Payload(2, buffer => buffer.WriteByte((byte) UnusedByte2)));

            if (Opcodes.Has(3) || SpeedMin != 0 || SpeedMax != 0) {
                records.Add(Payload(3, buffer => {
                    buffer.WriteInteger(SpeedMin);
                    buffer.WriteInteger(SpeedMax);
                }));
            }

            if (Opcodes.Has(4) || DragMode != 0 || DragStrength != 0) {
                records.Add(Payload(4, buffer => {
                    buffer.WriteByte((byte) DragMode);
                    buffer.WriteSignedByte(DragStrength);
                }));
            }

            /* Opcodes 5 and 31 are aliases for the same pair of fields and exactly one of them
               appears in any record this cache holds. 31 wins whenever it is present, so a file
               carrying both keeps the reading the client would end up with; 5 is written only
               while it can still express the pair, which the property setters guarantee. */
            if (Opcodes.Has(5) && !Opcodes.Has(31)) {
                records.Add(Payload(5, buffer => buffer.WriteShort(sizeMinStored)));
            }
            else if (Opcodes.Has(31) || sizeMinStored != 0 || sizeMaxStored != 0) {
                records.Add(Payload(31, buffer => {
                    buffer.WriteShort(sizeMinStored);
                    buffer.WriteShort(sizeMaxStored);
                }));
            }

            if (Opcodes.Has(6) || SpawnColourStart != 0 || SpawnColourEnd != 0) {
                records.Add(Payload(6, buffer => {
                    buffer.WriteInteger(SpawnColourStart);
                    buffer.WriteInteger(SpawnColourEnd);
                }));
            }

            if (Opcodes.Has(7) || LifetimeMin != 0 || LifetimeMax != 0) {
                records.Add(Payload(7, buffer => {
                    buffer.WriteShort(LifetimeMin);
                    buffer.WriteShort(LifetimeMax);
                }));
            }

            if (Opcodes.Has(8) || SpawnRateMin != 0 || SpawnRateMax != 0) {
                records.Add(Payload(8, buffer => {
                    buffer.WriteShort(SpawnRateMin);
                    buffer.WriteShort(SpawnRateMax);
                }));
            }

            if (Opcodes.Has(9) || SceneEffectorIds != null)
                records.Add(Payload(9, buffer => WriteIdList(buffer, SceneEffectorIds)));
            if (Opcodes.Has(10) || GlobalEffectorIds != null)
                records.Add(Payload(10, buffer => WriteIdList(buffer, GlobalEffectorIds)));

            if (Opcodes.Has(12) || CeilingPlane != -2)
                records.Add(Payload(12, buffer => buffer.WriteSignedByte((sbyte) CeilingPlane)));
            if (Opcodes.Has(13) || FloorPlane != -2)
                records.Add(Payload(13, buffer => buffer.WriteSignedByte((sbyte) FloorPlane)));
            if (Opcodes.Has(14) || PrimeSteps != 0)
                records.Add(Payload(14, buffer => buffer.WriteShort(PrimeSteps)));
            if (Opcodes.Has(15) || MaterialId != NoMaterial)
                records.Add(Payload(15, buffer => buffer.WriteShort(MaterialId)));

            if (Opcodes.Has(16) || CycleFlagStored != 1 || CycleThreshold != -1 ||
                CyclePeriod != -1 || CycleRepeatsStored != 1) {
                records.Add(Payload(16, buffer => {
                    buffer.WriteByte((byte) CycleFlagStored);
                    buffer.WriteShort(CycleThreshold);
                    buffer.WriteShort(CyclePeriod);
                    buffer.WriteByte((byte) CycleRepeatsStored);
                }));
            }

            if (Opcodes.Has(17) || LowDetailEmitterId != -1)
                records.Add(Payload(17, buffer => buffer.WriteShort(LowDetailEmitterId)));
            if (Opcodes.Has(18) || FadeColour != 0)
                records.Add(Payload(18, buffer => buffer.WriteInteger(FadeColour)));
            if (Opcodes.Has(19) || MinimumDetailLevel != 0)
                records.Add(Payload(19, buffer => buffer.WriteByte((byte) MinimumDetailLevel)));
            if (Opcodes.Has(20) || FadeColourPercent != DefaultRampPercent)
                records.Add(Payload(20, buffer => buffer.WriteByte((byte) FadeColourPercent)));
            if (Opcodes.Has(21) || FadeAlphaPercent != DefaultRampPercent)
                records.Add(Payload(21, buffer => buffer.WriteByte((byte) FadeAlphaPercent)));
            if (Opcodes.Has(22) || EndSpeed != -1)
                records.Add(Payload(22, buffer => buffer.WriteInteger(EndSpeed)));
            if (Opcodes.Has(23) || SpeedRampPercent != DefaultRampPercent)
                records.Add(Payload(23, buffer => buffer.WriteByte((byte) SpeedRampPercent)));

            if (Opcodes.Has(25) || AttachedEffectorKeys != null)
                records.Add(Payload(25, buffer => WriteIdList(buffer, AttachedEffectorKeys)));

            if (Opcodes.Has(27) || EndSizeStored != -1)
                records.Add(Payload(27, buffer => buffer.WriteShort(EndSizeStored)));
            if (Opcodes.Has(28) || SizeRampPercent != DefaultRampPercent)
                records.Add(Payload(28, buffer => buffer.WriteByte((byte) SizeRampPercent)));
            if (Opcodes.Has(29))
                records.Add(Payload(29, buffer => buffer.WriteShort(UnusedShort29)));

            /* 11, 24, 26, 30, 32, 33 and 34 are not listed. They carry no payload, so there is
               nothing to re-encode, and the recorded stream is the only statement of whether they
               are set - which is exactly what their properties read and write. Leaving them out is
               also what keeps opcode 24's second occurrence, which no field could express. */
            return Opcodes.Replay(records, appendInAscendingOrder: true);
        }

        /// <summary>Takes a copy no edit through this instance can reach.</summary>
        /// <returns>An independent definition holding the same values.</returns>
        public ParticleEmitterDefinition Clone() {
            var copy = (ParticleEmitterDefinition) MemberwiseClone();
            copy.DetachOpcodeStream();
            copy.SceneEffectorIds = (int[]?) SceneEffectorIds?.Clone();
            copy.GlobalEffectorIds = (int[]?) GlobalEffectorIds?.Clone();
            copy.AttachedEffectorKeys = (int[]?) AttachedEffectorKeys?.Clone();
            return copy;
        }

        /// <summary>
        ///     Drops opcode 5 once the two size bounds no longer agree.
        /// </summary>
        /// <remarks>
        ///     Opcode 5 stores one value for both bounds, so a record that carried it cannot express
        ///     an edit that pulls them apart. Leaving it in place would write a file the client reads
        ///     back with both bounds equal while the editor shows two, which is a silently discarded
        ///     edit; dropping it lets <see cref="Encode"/> fall through to opcode 31.
        /// </remarks>
        private void ReconcileSizeAlias() {
            if (sizeMinStored != sizeMaxStored)
                Opcodes.Remove(5);
        }

        /// <summary>Reads a count-prefixed list of unsigned short ids.</summary>
        /// <param name="stream">The stream, positioned at the count.</param>
        /// <returns>The ids, never null.</returns>
        private static int[] ReadIdList(JagStream stream) {
            int count = stream.ReadUnsignedByte();
            int[] ids = new int[count];
            for (int i = 0; i < count; i++)
                ids[i] = stream.ReadUnsignedShort();
            return ids;
        }

        /// <summary>Writes a count-prefixed list of unsigned short ids.</summary>
        /// <param name="buffer">The payload buffer.</param>
        /// <param name="ids">The ids, or null for an empty list.</param>
        private static void WriteIdList(JagStream buffer, int[]? ids) {
            int count = ids?.Length ?? 0;
            if (count > byte.MaxValue)
                throw new InvalidOperationException(
                    "A particle id list is length-prefixed with a single byte, so it cannot hold " +
                    count + " entries.");

            buffer.WriteByte((byte) count);
            for (int i = 0; i < count; i++)
                buffer.WriteShort(ids![i]);
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
