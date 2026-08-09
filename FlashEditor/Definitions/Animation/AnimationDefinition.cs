using System;
using System.Collections.Generic;
using FlashEditor.IO;

namespace FlashEditor.Definitions.Animation {
    /// <summary>
    ///     One animation ("sequence") from JS5 index 20: which frames play, in what order, for how
    ///     long, and how the playback interacts with everything else the entity is doing.
    /// </summary>
    /// <remarks>
    ///     An animation id splits <c>group = id &gt;&gt;&gt; 7</c> (Class299_Sub2.java:132) and
    ///     <c>file = id &amp; 0x7f</c> (Node_Sub10_Sub32.java:18), so a group is a bank of 128
    ///     animations and a file is one record. Opcode table from <c>Class97.method939</c>
    ///     (Class97.java:416-539), driven by the loop in <c>method933</c> (:264-285).
    ///     <para>
    ///     This is what makes indexes 0 and 1 addressable at all. Index 0 carries no name hashes, so
    ///     the only route to a frame is the packed id an animation stores: the client splits it as
    ///     <c>method2624(2, id &gt;&gt; 16)</c> then <c>id &amp;= 0xffff</c> (Class97.java:130-131),
    ///     which is the frame set's group id in index 0 and the frame's position within it. See
    ///     <see cref="FrameGroupOf"/>.
    ///     </para>
    ///     <para>
    ///     <b>Stored state and derived state are kept apart.</b> The client runs
    ///     <c>method938</c> (:385-413) after the opcode loop and rewrites two fields that were left
    ///     at -1, and <c>Class183.java:260</c> does that on every load. Encoding from the
    ///     post-processed values would write opcodes 9 and 10 into records that never carried them,
    ///     so the decoded fields keep the -1 and <see cref="EffectiveMovingInterrupt"/> and
    ///     <see cref="EffectiveStationaryInterrupt"/> apply the rule separately.
    ///     </para>
    /// </remarks>
    public sealed class AnimationDefinition : OpcodeStreamDefinition {
        /// <summary>Loop count the client assumes when opcode 8 is absent (Class97.java:109).</summary>
        public const int DefaultMaxLoops = 99;

        /// <summary>Priority the client assumes when opcode 5 is absent (Class97.java:117).</summary>
        public const int DefaultPriority = 5;

        /// <summary>Re-trigger behaviour the client assumes when opcode 11 is absent (Class97.java:111).</summary>
        public const int DefaultRetriggerBehaviour = 2;

        /// <summary>Per-frame sound volume the client fills opcode 19's array with (Class97.java:475).</summary>
        public const int DefaultSoundVolume = 255;

        /// <summary>Per-frame sound pitch the client fills opcode 20's arrays with (Class97.java:462-463).</summary>
        public const int DefaultSoundPitch = 256;

        /// <summary>
        ///     The interrupt behaviour <c>method938</c> derives for a record that carries opcode 3.
        /// </summary>
        /// <remarks>
        ///     Class97.java:397,407. A record without opcode 3 derives 0 instead. Neither value is
        ///     ever written back - see the class remarks.
        /// </remarks>
        public const int BlendedInterruptBehaviour = 2;

        /// <summary>The animation id, which is <c>(group &lt;&lt; 7) | file</c>.</summary>
        public int Id { get; set; } = -1;

        /// <summary>
        ///     Opcode 1, first array. How many client cycles each frame is held for.
        /// </summary>
        /// <remarks>
        ///     Class340.java:30 advances to the next frame once a per-cycle counter passes this, so
        ///     it is a duration rather than a frame rate. Always the same length as
        ///     <see cref="FrameIds"/> - one opcode writes both.
        /// </remarks>
        public int[] FrameDurations { get; set; } = Array.Empty<int>();

        /// <summary>
        ///     Opcode 1, second array. The packed index-0 frame each step plays.
        /// </summary>
        /// <remarks>
        ///     Packed as <c>(frameSetGroup &lt;&lt; 16) | frameIndex</c>; the file stores the two
        ///     halves as separate u16 runs and adds them (Class97.java:425-431). Split it with
        ///     <see cref="FrameGroupOf"/> and <see cref="FrameIndexOf"/> rather than by hand.
        /// </remarks>
        public int[] FrameIds { get; set; } = Array.Empty<int>();

        /// <summary>
        ///     Opcode 2. How far the playhead is wound back when the animation loops.
        /// </summary>
        /// <remarks>
        ///     Subtracted from the entity's frame counter at every loop point (Class340.java:123,
        ///     141, 211; Class359.java:232). -1 when absent.
        /// </remarks>
        public int FrameStep { get; set; } = -1;

        /// <summary>
        ///     Opcode 3. The skeleton labels this animation supplies when it is blended over
        ///     another, in the order the file lists them.
        /// </summary>
        /// <remarks>
        ///     The client expands these into a <c>boolean[256]</c> (Class97.java:526-530) and hands
        ///     the mask to the two-animation blend at Class141.java:1357-1359. The raw list is kept
        ///     instead because the expansion is lossy in two ways a re-encode would show: it forgets
        ///     the order, and it collapses a label listed twice. Presence is
        ///     <c>Opcodes.Has(3)</c>, not a non-empty list - a record may store a count of zero, and
        ///     the client's derived state keys off the array existing rather than off its contents.
        /// </remarks>
        public int[] BlendLabels { get; set; } = Array.Empty<int>();

        /// <summary>Opcode 5. Which of two competing animations survives, higher wins.</summary>
        /// <remarks>
        ///     Class266.java:52-53 replaces a queued animation only when the newcomer's priority is
        ///     greater than or equal to the incumbent's.
        /// </remarks>
        public int Priority { get; set; } = DefaultPriority;

        /// <summary>Opcode 6. Item id forced into the off-hand slot while this plays, or -1.</summary>
        /// <remarks>
        ///     PlayerAppearance.java:615-617 writes it into appearance slot 5, which is the shield
        ///     hand. A stored 65535 means "no item" there and is kept as read rather than folded to
        ///     -1, because the two are different bytes.
        /// </remarks>
        public int LeftHandItem { get; set; } = -1;

        /// <summary>Opcode 7. Item id forced into the weapon slot while this plays, or -1.</summary>
        /// <remarks>PlayerAppearance.java:625-627 writes it into appearance slot 3.</remarks>
        public int RightHandItem { get; set; } = -1;

        /// <summary>Opcode 8. How many times the animation may loop before it is dropped.</summary>
        /// <remarks>Compared against the entity's loop counter at Class340.java:124.</remarks>
        public int MaxLoops { get; set; } = DefaultMaxLoops;

        /// <summary>
        ///     Opcode 9. What becomes of the animation while the entity is walking.
        /// </summary>
        /// <remarks>
        ///     Read only when the entity has queued movement left (Class333.java:49, 68, 88;
        ///     Class340.java:81-96, where 3 abandons the graphic and 1 defers it a tick). -1 means
        ///     the record did not state it; <see cref="EffectiveMovingInterrupt"/> applies the
        ///     client's fallback without disturbing this.
        /// </remarks>
        public int MovingInterrupt { get; set; } = -1;

        /// <summary>
        ///     Opcode 10. The same decision for an entity that is standing still.
        /// </summary>
        /// <remarks>Class333.java:55-56, 74, 94, always on the branch where no movement is queued.</remarks>
        public int StationaryInterrupt { get; set; } = -1;

        /// <summary>
        ///     Opcode 11. What happens when the animation already playing is triggered again.
        /// </summary>
        /// <remarks>
        ///     Class266.java:56-70: 0 cancels it outright, 1 restarts from frame 0, 2 resets only
        ///     the sub-cycle counter.
        /// </remarks>
        public int RetriggerBehaviour { get; set; } = DefaultRetriggerBehaviour;

        /// <summary>
        ///     Opcode 12. A second packed index-0 frame per step, drawn over the first.
        /// </summary>
        /// <remarks>
        ///     Same packing as <see cref="FrameIds"/> (Class97.java:506-514). The client skips an
        ///     entry equal to 65535 (Class97.java:311), which is frame set 0 frame 65535, so that
        ///     value is a sentinel rather than an address. Shorter than the main array in general -
        ///     it is length-checked separately at Class97.java:309.
        /// </remarks>
        public int[] SecondaryFrameIds { get; set; } = Array.Empty<int>();

        /// <summary>
        ///     Opcode 13. One row per frame naming the sounds that fire when the frame is reached.
        /// </summary>
        /// <remarks>
        ///     An empty row is a frame with no sound and costs one zero byte; the client leaves the
        ///     row's array null there (Class97.java:491-503), which is the same statement. The first
        ///     entry of a non-empty row is 24 bits and packs the sound id in its top bits
        ///     (<c>value &gt;&gt; 8</c> at Class280.java:83-84) together with a repeat field; the
        ///     rest are 16-bit alternatives the client picks between at random. The whole entry is
        ///     kept as one number so the packing never has to be rebuilt.
        /// </remarks>
        public int[][] FrameSounds { get; set; } = Array.Empty<int[]>();

        /// <summary>
        ///     Opcode 14. Sets renderable flag 0x200 for the frames this animation drives.
        /// </summary>
        /// <remarks>
        ///     Class97.java:142-144 ORs the flag in and :167 passes the same boolean into the
        ///     skeletal transform; Class141.java:1359 ORs it across a blended pair. A view over the
        ///     recorded stream rather than a stored bool, because the opcode has no payload and
        ///     clearing a field would leave an opcode behind for the replay to put back.
        /// </remarks>
        public bool Stretches {
            get => Opcodes.Has(14);
            set => SetFlag(14, value);
        }

        /// <summary>
        ///     Opcode 15. Interpolates between this frame and the next rather than snapping.
        /// </summary>
        /// <remarks>
        ///     Class97.java:136 and :298 gate the second frame lookup on it, alongside a global
        ///     override the client's own developer command calls "tween"
        ///     (PlayerUpdateMask.java:804-810).
        /// </remarks>
        public bool Tweens {
            get => Opcodes.Has(15);
            set => SetFlag(15, value);
        }

        /// <summary>
        ///     Opcode 16. The same tween statement, read where a cached renderable is reused.
        /// </summary>
        /// <remarks>
        ///     Class359.java:398 pairs it with the same global override to decide that a cached
        ///     renderable is stale once the second frame moves. Whether it is meant to be the same
        ///     property as <see cref="Tweens"/> is not settled - no consumer reads both - so it is a
        ///     field of its own. Two records in either cache carry it, which is exactly the
        ///     population a sampled sweep would miss.
        /// </remarks>
        public bool TweensAcrossCachedFrames {
            get => Opcodes.Has(16);
            set => SetFlag(16, value);
        }

        /// <summary>
        ///     Opcode 18. Queues this animation's sounds on the second emitter.
        /// </summary>
        /// <remarks>
        ///     Class280.java:97-101 and ScriptRuntime.java:153-157 both branch on it, building a
        ///     <c>Class338</c> of type 1 when clear and type 2 when set. What the two types differ in
        ///     is not visible from <c>Class338</c>, so the name says which branch it selects rather
        ///     than guessing at the effect.
        /// </remarks>
        public bool SoundsUseTheAlternateEmitter {
            get => Opcodes.Has(18);
            set => SetFlag(18, value);
        }

        /// <summary>
        ///     Opcode 19. Playback volume for each frame's sound, or null when none was stored.
        /// </summary>
        /// <remarks>
        ///     Read only, and deliberately: each occurrence of opcode 19 writes one slot, so a
        ///     record carries as many of them as it has non-default frames - 45 records carry more
        ///     than one. <c>OpcodeStream.Replay</c> can substitute a fresh payload for the last
        ///     occurrence of an opcode only, so re-encoding this from the array would rewrite the
        ///     last slot and leave the earlier ones as they were read. It is replayed verbatim
        ///     instead, which is correct for every record and simply does not offer an edit.
        /// </remarks>
        public int[]? FrameSoundVolumes { get; private set; }

        /// <summary>Opcode 20, first field. Lower bound of each frame's random sound pitch.</summary>
        /// <remarks>
        ///     ScriptRuntime.java:145-148 picks uniformly between this and
        ///     <see cref="FrameSoundPitchMax"/>. Replay-only for the reason given on
        ///     <see cref="FrameSoundVolumes"/>; 185 records carry opcode 20 more than once.
        /// </remarks>
        public int[]? FrameSoundPitchMin { get; private set; }

        /// <summary>Opcode 20, second field. Upper bound of the same range.</summary>
        public int[]? FrameSoundPitchMax { get; private set; }

        /// <summary>How many steps the animation plays.</summary>
        public int FrameCount => FrameIds.Length;

        /// <summary>How long one pass through the animation lasts, in client cycles.</summary>
        public int TotalDuration {
            get {
                int total = 0;
                for (int i = 0; i < FrameDurations.Length; i++)
                    total += FrameDurations[i];
                return total;
            }
        }

        /// <summary>
        ///     <see cref="MovingInterrupt"/> as the client sees it once <c>method938</c> has run.
        /// </summary>
        /// <remarks>
        ///     Class97.java:390-398. A record that did not state it gets 2 when it carries opcode 3
        ///     and 0 when it does not. Separate from the stored field so the encoder cannot pick this
        ///     up - a record that never carried opcode 9 must not grow one.
        /// </remarks>
        public int EffectiveMovingInterrupt =>
            MovingInterrupt != -1 ? MovingInterrupt : (Opcodes.Has(3) ? BlendedInterruptBehaviour : 0);

        /// <summary><see cref="StationaryInterrupt"/> after the same post-processing.</summary>
        /// <remarks>Class97.java:401-408, which applies the identical rule.</remarks>
        public int EffectiveStationaryInterrupt =>
            StationaryInterrupt != -1 ? StationaryInterrupt : (Opcodes.Has(3) ? BlendedInterruptBehaviour : 0);

        /// <summary>The index-0 group holding the frame a packed frame id names.</summary>
        /// <param name="packedFrameId">A value from <see cref="FrameIds"/> or <see cref="SecondaryFrameIds"/>.</param>
        /// <returns>The frame set's group id in index 0.</returns>
        public static int FrameGroupOf(int packedFrameId) => (int) ((uint) packedFrameId >> 16);

        /// <summary>The frame's position within its set.</summary>
        /// <param name="packedFrameId">A value from <see cref="FrameIds"/> or <see cref="SecondaryFrameIds"/>.</param>
        /// <returns>The frame index inside the set.</returns>
        public static int FrameIndexOf(int packedFrameId) => packedFrameId & 0xFFFF;

        /// <summary>Builds the packed form the file stores.</summary>
        /// <param name="frameSetGroup">The index-0 group id.</param>
        /// <param name="frameIndex">The frame's position within that set.</param>
        /// <returns>The packed frame id.</returns>
        public static int PackFrame(int frameSetGroup, int frameIndex) =>
            (frameSetGroup << 16) | (frameIndex & 0xFFFF);

        /// <summary>Reads one animation record from its file.</summary>
        /// <param name="stream">The file, positioned at its first opcode.</param>
        /// <returns>This definition.</returns>
        public AnimationDefinition Decode(JagStream stream) {
            Opcodes.Clear();
            DecodeOpcodeStream(stream);
            return this;
        }

        /// <inheritdoc/>
        protected override bool DecodeOpcode(JagStream stream, int opcode) {
            switch (opcode) {
                case 1: {
                        int count = stream.ReadUnsignedShort();
                        FrameDurations = new int[count];
                        for (int i = 0; i < count; i++)
                            FrameDurations[i] = stream.ReadUnsignedShort();

                        FrameIds = ReadPackedFrames(stream, count);
                        return true;
                    }

                case 2:
                    FrameStep = stream.ReadUnsignedShort();
                    return true;

                case 3: {
                        int count = stream.ReadUnsignedByte();
                        BlendLabels = new int[count];
                        for (int i = 0; i < count; i++)
                            BlendLabels[i] = stream.ReadUnsignedByte();
                        return true;
                    }

                case 5:
                    Priority = stream.ReadUnsignedByte();
                    return true;

                case 6:
                    LeftHandItem = stream.ReadUnsignedShort();
                    return true;

                case 7:
                    RightHandItem = stream.ReadUnsignedShort();
                    return true;

                case 8:
                    MaxLoops = stream.ReadUnsignedByte();
                    return true;

                case 9:
                    MovingInterrupt = stream.ReadUnsignedByte();
                    return true;

                case 10:
                    StationaryInterrupt = stream.ReadUnsignedByte();
                    return true;

                case 11:
                    RetriggerBehaviour = stream.ReadUnsignedByte();
                    return true;

                //A byte count here rather than opcode 1's short, so a secondary table cannot be
                //longer than 255 entries whatever the main one holds.
                case 12: {
                        int count = stream.ReadUnsignedByte();
                        SecondaryFrameIds = ReadPackedFrames(stream, count);
                        return true;
                    }

                case 13: {
                        int rows = stream.ReadUnsignedShort();
                        FrameSounds = new int[rows][];
                        for (int row = 0; row < rows; row++) {
                            int entries = stream.ReadUnsignedByte();
                            if (entries == 0) {
                                FrameSounds[row] = Array.Empty<int>();
                                continue;
                            }

                            int[] sounds = new int[entries];
                            sounds[0] = stream.ReadMedium();
                            for (int i = 1; i < entries; i++)
                                sounds[i] = stream.ReadUnsignedShort();
                            FrameSounds[row] = sounds;
                        }
                        return true;
                    }

                //14, 15, 16 and 18 are bare flags: their presence is their whole payload.
                case 14:
                case 15:
                case 16:
                case 18:
                    return true;

                case 19: {
                        int slot = stream.ReadUnsignedByte();
                        int volume = stream.ReadUnsignedByte();
                        int[] volumes = Slot(FrameSoundVolumes, slot, DefaultSoundVolume);
                        volumes[slot] = volume;
                        FrameSoundVolumes = volumes;
                        return true;
                    }

                case 20: {
                        int slot = stream.ReadUnsignedByte();
                        int low = stream.ReadUnsignedShort();
                        int high = stream.ReadUnsignedShort();
                        int[] min = Slot(FrameSoundPitchMin, slot, DefaultSoundPitch);
                        int[] max = Slot(FrameSoundPitchMax, slot, DefaultSoundPitch);
                        min[slot] = low;
                        max[slot] = high;
                        FrameSoundPitchMin = min;
                        FrameSoundPitchMax = max;
                        return true;
                    }

                /* 4 and 17 have no handler in the 637 client and occur nowhere in either 639 cache,
                   so there is no payload width to guess at and no data veto to reconcile. */
                default:
                    return false;
            }
        }

        /// <summary>Writes this animation back to the file representation.</summary>
        /// <remarks>
        ///     Order capture is not defensive here. 7,940 of the 15,260 records in either cache store
        ///     their opcodes out of ascending order, ten different opcodes lead somewhere, and five
        ///     scalar opcodes repeat within a record - so an encoder emitting a fixed order would
        ///     rewrite half the index the first time a group was saved.
        ///     <para>
        ///     Opcodes 19 and 20 are replayed rather than re-encoded, and the bare flags carry no
        ///     payload to re-encode at all. See <see cref="FrameSoundVolumes"/>.
        ///     </para>
        /// </remarks>
        /// <returns>The encoded file, positioned at 0.</returns>
        /// <exception cref="InvalidOperationException">
        ///     The two opcode-1 arrays are different lengths, which no file can express.
        /// </exception>
        public JagStream Encode() {
            if (FrameDurations.Length != FrameIds.Length)
                throw new InvalidOperationException(
                    "Animation " + Id + " has " + FrameDurations.Length + " frame durations against " +
                    FrameIds.Length + " frame ids. Opcode 1 writes one count for both arrays, so a " +
                    "file cannot express the difference.");

            var records = new List<KeyValuePair<int, byte[]>>();

            /* Each block emits when the record carried the opcode OR when the field has moved off
               the client's default. The first arm is what keeps an opcode stored at its own default
               - a priority of 5, a loop count of 99 - rather than dropping it and shortening a file
               nobody edited. */
            if (Opcodes.Has(1) || FrameIds.Length > 0) {
                records.Add(Payload(1, buffer => {
                    buffer.WriteShort(FrameIds.Length);
                    foreach (int duration in FrameDurations)
                        buffer.WriteShort(duration);
                    WritePackedFrames(buffer, FrameIds);
                }));
            }

            if (Opcodes.Has(2) || FrameStep != -1)
                records.Add(Payload(2, buffer => buffer.WriteShort(FrameStep)));

            if (Opcodes.Has(3) || BlendLabels.Length > 0) {
                records.Add(Payload(3, buffer => {
                    buffer.WriteByte((byte) BlendLabels.Length);
                    foreach (int label in BlendLabels)
                        buffer.WriteByte((byte) label);
                }));
            }

            if (Opcodes.Has(5) || Priority != DefaultPriority)
                records.Add(Payload(5, buffer => buffer.WriteByte((byte) Priority)));
            if (Opcodes.Has(6) || LeftHandItem != -1)
                records.Add(Payload(6, buffer => buffer.WriteShort(LeftHandItem)));
            if (Opcodes.Has(7) || RightHandItem != -1)
                records.Add(Payload(7, buffer => buffer.WriteShort(RightHandItem)));
            if (Opcodes.Has(8) || MaxLoops != DefaultMaxLoops)
                records.Add(Payload(8, buffer => buffer.WriteByte((byte) MaxLoops)));
            if (Opcodes.Has(9) || MovingInterrupt != -1)
                records.Add(Payload(9, buffer => buffer.WriteByte((byte) MovingInterrupt)));
            if (Opcodes.Has(10) || StationaryInterrupt != -1)
                records.Add(Payload(10, buffer => buffer.WriteByte((byte) StationaryInterrupt)));
            if (Opcodes.Has(11) || RetriggerBehaviour != DefaultRetriggerBehaviour)
                records.Add(Payload(11, buffer => buffer.WriteByte((byte) RetriggerBehaviour)));

            if (Opcodes.Has(12) || SecondaryFrameIds.Length > 0) {
                records.Add(Payload(12, buffer => {
                    buffer.WriteByte((byte) SecondaryFrameIds.Length);
                    WritePackedFrames(buffer, SecondaryFrameIds);
                }));
            }

            if (Opcodes.Has(13) || FrameSounds.Length > 0) {
                records.Add(Payload(13, buffer => {
                    buffer.WriteShort(FrameSounds.Length);
                    foreach (int[] row in FrameSounds) {
                        int entries = row.Length;
                        buffer.WriteByte((byte) entries);
                        if (entries == 0)
                            continue;

                        buffer.WriteMedium(row[0]);
                        for (int i = 1; i < entries; i++)
                            buffer.WriteShort(row[i]);
                    }
                }));
            }

            return Opcodes.Replay(records, appendInAscendingOrder: true);
        }

        /// <summary>The record in words, for logs and list rows.</summary>
        /// <returns>The id, frame count and total duration.</returns>
        public override string ToString() {
            return "animation " + Id + ": " + FrameCount + " frames, " + TotalDuration + " cycles";
        }

        /// <summary>
        ///     Reads a run of packed frame ids, stored as all the low halves then all the high ones.
        /// </summary>
        /// <remarks>
        ///     The split is not an encoding choice - the low half is a full u16, so no value can
        ///     carry into the high half and the packing is unambiguous in both directions.
        /// </remarks>
        /// <param name="stream">The stream, positioned at the first low half.</param>
        /// <param name="count">How many frames the run holds.</param>
        /// <returns>The packed ids.</returns>
        private static int[] ReadPackedFrames(JagStream stream, int count) {
            int[] frames = new int[count];
            for (int i = 0; i < count; i++)
                frames[i] = stream.ReadUnsignedShort();
            for (int i = 0; i < count; i++)
                frames[i] |= stream.ReadUnsignedShort() << 16;
            return frames;
        }

        /// <summary>Writes a run of packed frame ids back in the two-pass layout.</summary>
        /// <param name="buffer">The payload being built.</param>
        /// <param name="frames">The packed ids.</param>
        private static void WritePackedFrames(JagStream buffer, int[] frames) {
            foreach (int frame in frames)
                buffer.WriteShort(FrameIndexOf(frame));
            foreach (int frame in frames)
                buffer.WriteShort(FrameGroupOf(frame));
        }

        /// <summary>
        ///     Returns an array long enough to hold <paramref name="slot"/>, filling new entries with
        ///     the client's default.
        /// </summary>
        /// <remarks>
        ///     The client sizes these from the opcode-13 table and would throw on a record that
        ///     addressed past it or omitted opcode 13 entirely (Class97.java:455-465, 471-477).
        ///     Neither happens in either cache - every record carrying 19 or 20 carries 13 first, and
        ///     no slot reaches past the table - but a decoder that inherited the fault would turn a
        ///     malformed file into a crash rather than a report, so it grows instead.
        /// </remarks>
        /// <param name="existing">The array so far, or null.</param>
        /// <param name="slot">The frame index being written.</param>
        /// <param name="fill">The value the client pre-fills with.</param>
        /// <returns>An array with at least <paramref name="slot"/> + 1 entries.</returns>
        private int[] Slot(int[]? existing, int slot, int fill) {
            int needed = Math.Max(slot + 1, FrameSounds.Length);
            if (existing != null && existing.Length >= needed)
                return existing;

            int[] grown = new int[needed];
            for (int i = 0; i < grown.Length; i++)
                grown[i] = fill;
            existing?.CopyTo(grown, 0);
            return grown;
        }

        /// <summary>Adds or drops a bare flag opcode.</summary>
        /// <remarks>
        ///     An added flag lands at the end of the stream. Safe here: none of the four says
        ///     anything about how a later opcode is read, unlike opcode 13, which sizes the arrays
        ///     opcodes 19 and 20 write into.
        /// </remarks>
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
