using System;
using System.Collections.Generic;
using System.IO;
using FlashEditor.IO;

namespace FlashEditor.Definitions.Tracks {
    /// <summary>
    ///     A music track: RuneScape's column-major packing of a standard MIDI file.
    /// </summary>
    /// <remarks>
    ///     JS5 index 6 (music) and index 11 (jingles), one file per group. Both indexes hold this
    ///     same format, and that is settled by the client's dispatch rather than by their contents
    ///     looking alike: index 6 is opened as <c>Node_Sub10_Sub1.aJS5Archive_5544</c> and index 11
    ///     as <c>Class61.aJS5Archive_481</c> (InterfaceSettings.java:164,168), both are parked in the
    ///     one static <c>Class269.aJS5Archive_2025</c> (Class226.java:36 and Class64_Sub13.java:74),
    ///     and that static feeds the only call to the only decoder there is
    ///     (ClientScript.java:55 into <c>Node_Sub7.method985</c>). There is no second reader to
    ///     disagree with.
    ///
    ///     The packing is what makes this worth a codec rather than a file copy. A MIDI file
    ///     interleaves everything - status byte, note, velocity, delta time - per event. Jagex
    ///     splits the same data into one contiguous run per field, so that all the note numbers sit
    ///     together, all the velocities sit together, and so on. Runs of similar bytes compress far
    ///     better, and most of the runs are stored as deltas against the previous value in the same
    ///     run rather than absolutely.
    ///
    ///     The header is the last three bytes of the file, not the first
    ///     (<c>Node_Sub7.java:22</c>), because the opcode stream has to start at offset zero: the
    ///     re-interleave indexes it from the raw buffer at index 0 while the counting pass reads it
    ///     through the cursor.
    ///
    ///     <b>The stored form is what this type holds, and the MIDI is a projection of it.</b> That
    ///     split is forced by the format rather than chosen. Every run is a stream of signed byte
    ///     deltas accumulated into a running <c>int</c> that carries across every MTrk in the file,
    ///     and the emitted MIDI byte is that accumulator masked to seven bits. So the accumulator
    ///     routinely holds a value the output cannot express, many distinct stored streams project
    ///     to byte-identical MIDI, and no encoder can recompute the deltas from the result. The
    ///     packed runs are therefore kept verbatim and <see cref="Encode"/> replays them - the same
    ///     rule the terrain codec follows for its aliased height bytes.
    /// </remarks>
    public class Track {
        /// <summary>The runs in the order the packed file lays them out.</summary>
        private static readonly TrackRun[] RunOrder = (TrackRun[]) Enum.GetValues(typeof(TrackRun));

        /// <summary>Bytes of header, which the format stores at the end rather than the start.</summary>
        private const int TrailerLength = 3;

        private readonly byte[][] _runs = EmptyRuns();

        /// <summary>The group id this track was read from.</summary>
        public int Id { get; set; } = -1;

        /// <summary>The index the group came from, 6 for music and 11 for jingles.</summary>
        public int IndexId { get; set; } = -1;

        /// <summary>
        ///     The group's name hash, or -1 when the index carries no identifiers.
        /// </summary>
        /// <remarks>
        ///     Lives in the reference table rather than in the file, but it is the only naming
        ///     information the cache holds for a track, so it is carried here for display. The hash
        ///     is one way: index 6 is addressable by name - the client asks it for
        ///     <c>"scape main"</c> at InterfaceSettings.java:216 - but the name cannot be recovered
        ///     from the hash. Index 11 has no identifiers at all.
        /// </remarks>
        public int NameHash { get; set; } = -1;

        /// <summary>
        ///     The display name, or an empty string when the cache names no track for this group.
        /// </summary>
        /// <remarks>
        ///     Recovered by hashing the music player's own name list and matching
        ///     <see cref="NameHash"/>, so a name is only ever attached to a group whose stored hash
        ///     it reproduces - see <see cref="TrackNames"/>. Empty for the index-6 groups no listed
        ///     name hashes to, and for every jingle in index 11.
        /// </remarks>
        public string Name { get; set; } = string.Empty;

        // ===================================================================
        //  The stored form
        // ===================================================================

        /// <summary>
        ///     The opcode stream, every track's run of opcodes concatenated including its
        ///     terminating <c>7</c>.
        /// </summary>
        /// <remarks>
        ///     Kept whole rather than split per track because the re-interleave walks it as one
        ///     array from offset zero (<c>Node_Sub7.java:193</c>) while the counting pass walks it
        ///     through the cursor, and the two must agree byte for byte.
        /// </remarks>
        public byte[] Opcodes { get; private set; } = Array.Empty<byte>();

        /// <summary>
        ///     The delta times, as the variable-length quantities the file stores.
        /// </summary>
        /// <remarks>
        ///     Retained as bytes rather than as decoded values because a variable-length quantity
        ///     has more than one encoding of the same number, and the projection re-encodes what it
        ///     reads. Replaying the bytes cannot normalise a wide form into a narrow one.
        /// </remarks>
        public byte[] DeltaTimes { get; private set; } = Array.Empty<byte>();

        /// <summary>
        ///     The controller numbers, delta-encoded against the previous one.
        /// </summary>
        /// <remarks>
        ///     Read unsigned and masked to seven bits on accumulation
        ///     (<c>Node_Sub7.java:235</c>), so bit 7 of every one of these bytes is invisible in the
        ///     decoded MIDI. That alone makes the stored bytes unrecoverable from the output.
        /// </remarks>
        public byte[] ControllerNumbers { get; private set; } = Array.Empty<byte>();

        /// <summary>
        ///     Bytes between the last run and the three-byte trailer.
        /// </summary>
        /// <remarks>
        ///     Expected to be empty: the runs are sized from the event counts the opcode stream
        ///     implies and should meet the trailer exactly. Carried rather than rejected so a file
        ///     with a tail still re-encodes to itself instead of being silently truncated - the same
        ///     tolerance the reference-table reader has to have. Whether any shipped file actually
        ///     has a tail is a measurement, and the sweep is what makes it, so the assertion lives
        ///     there and not here.
        /// </remarks>
        public byte[] TrailingBytes { get; private set; } = Array.Empty<byte>();

        /// <summary>Length of the packed file, for comparison against <see cref="MidiLength"/>.</summary>
        public int PackedLength { get; private set; }

        /// <summary>
        ///     What <see cref="Encode"/> will write, summed field by field from the stored form.
        /// </summary>
        /// <remarks>
        ///     Deliberately independent of <see cref="PackedLength"/>, which records what was read.
        ///     Requiring the two to agree is the exact-consumption statement for a format that
        ///     states no lengths anywhere: nothing in the file says how long a run is, so a
        ///     miscounted event shortens one run, lengthens the next, and lands off the end.
        /// </remarks>
        public int StoredLength {
            get {
                int length = Opcodes.Length + DeltaTimes.Length + ControllerNumbers.Length
                             + TrailingBytes.Length + TrailerLength;
                foreach (byte[] run in _runs)
                    length += run.Length;
                return length;
            }
        }

        /// <summary>Number of MIDI tracks, the <c>ntrks</c> field of the emitted MThd chunk.</summary>
        public int TrackCount { get; private set; }

        /// <summary>Ticks per quarter note, the <c>division</c> field of the emitted MThd chunk.</summary>
        public int Division { get; private set; }

        /// <summary>The runs the packed file lays out, in its own order.</summary>
        public static IReadOnlyList<TrackRun> StoredRunOrder => RunOrder;

        /// <summary>
        ///     One packed run exactly as the file stores it.
        /// </summary>
        /// <param name="run">Which run.</param>
        /// <returns>The run's bytes, empty when the track carries no event that feeds it.</returns>
        public byte[] Run(TrackRun run) {
            return _runs[(int) run];
        }

        // ===================================================================
        //  Derived from the stored form
        // ===================================================================

        /// <summary>Set-tempo meta events across all tracks.</summary>
        public int TempoEvents { get; private set; }

        /// <summary>Note-on events across all tracks.</summary>
        public int NoteOnEvents { get; private set; }

        /// <summary>Note-off events across all tracks.</summary>
        public int NoteOffEvents { get; private set; }

        /// <summary>Control-change events across all tracks.</summary>
        public int ControllerEvents { get; private set; }

        /// <summary>Pitch-wheel events across all tracks.</summary>
        public int PitchWheelEvents { get; private set; }

        /// <summary>Channel-pressure events across all tracks.</summary>
        public int ChannelAfterTouchEvents { get; private set; }

        /// <summary>Polyphonic key-pressure events across all tracks.</summary>
        public int KeyAfterTouchEvents { get; private set; }

        /// <summary>
        ///     Program-change events across all tracks, not counting the bank selects that share
        ///     their run.
        /// </summary>
        public int ProgramChangeEvents { get; private set; }

        /// <summary>
        ///     The MIDI length the packed file's own event counts predict.
        /// </summary>
        /// <remarks>
        ///     The client sizes its output buffer from this and then writes into it without ever
        ///     re-measuring (<c>Node_Sub7.java:166</c>), so it is the format's own statement of how
        ///     long the decoded file must be. Comparing it against what the projection actually
        ///     emitted is the only check available that does not just run this decoder against its
        ///     own assumptions.
        /// </remarks>
        public int ExpectedMidiLength { get; private set; }

        /// <summary>
        ///     Meta-event status bytes emitted beyond what <see cref="ExpectedMidiLength"/> allows for.
        /// </summary>
        /// <remarks>
        ///     See the CLIENT BUG note on <see cref="Decode"/>. Non-zero means this track is one the
        ///     client would have written as unplayable MIDI. It is a property of the projection
        ///     only: <see cref="Encode"/> replays the stored opcode stream, which has no
        ///     representation of that byte at all, so the repair can never leak back into the packed
        ///     file.
        /// </remarks>
        public int RepairedMetaStatusBytes { get; private set; }

        /// <summary>The projected standard MIDI file, or null before <see cref="Decode"/> has run.</summary>
        public byte[]? Midi { get; private set; }

        /// <summary>Length of <see cref="Midi"/>, for display before the bytes are wanted.</summary>
        public int MidiLength => Midi == null ? 0 : Midi.Length;

        // ===================================================================
        //  Decode
        // ===================================================================

        /// <summary>
        ///     Reads the packed file into the stored form and projects the MIDI from it.
        /// </summary>
        /// <remarks>
        ///     CLIENT BUG: the client gates the <c>0xFF</c> meta-event status byte on the same
        ///     running-status test it uses for channel messages (<c>Node_Sub7.java:196-212</c>).
        ///     The test compares the raw opcode against the previous opcode's low nibble, and the
        ///     two meta opcodes are 7 (end of track) and 23 (set tempo), both of which mask to 7.
        ///     A tempo change can never lose its status byte, because 23 cannot equal a nibble; an
        ///     end of track loses its own whenever the event before it was a tempo change, and then
        ///     the track closes with a bare <c>2F 00</c>. The MIDI specification forbids running
        ///     status across meta events, and the client only gets away with it because its own
        ///     reader implements the matching rule (<c>Class173.method2549</c> falls back to the
        ///     retained status byte, which is 255). Anything else refuses the file, and this decoder
        ///     exists to hand tracks to something else. The byte is written unconditionally, and the
        ///     count of the ones the client would have dropped is kept in
        ///     <see cref="RepairedMetaStatusBytes"/> so <see cref="ExpectedMidiLength"/> still
        ///     reconciles. It is added to the projection and never to the stored form, so it does
        ///     not disturb the round trip.
        /// </remarks>
        /// <param name="buf">The packed file, positioned anywhere.</param>
        /// <returns>This track.</returns>
        /// <exception cref="InvalidDataException">
        ///     The file is shorter than its trailer, carries an opcode the client has no case for,
        ///     or implies runs that do not fit in front of the trailer.
        /// </exception>
        public Track Decode(JagStream buf) {
            /* Index the payload directly rather than through the cursor. The runs are read in
               parallel by the projection, so it cannot use a single position, and copying the array
               once here keeps that out of the inner loop. */
            byte[] data = buf.ToArray();
            PackedLength = data.Length;

            if (data.Length < TrailerLength)
                throw new InvalidDataException("Track " + Id + " is " + data.Length +
                    " bytes, too short to hold its " + TrailerLength + "-byte trailer");

            //The three-byte header is at the end of the file (Node_Sub7.java:22)
            buf.Seek(data.Length - TrailerLength);
            TrackCount = buf.ReadUnsignedByte();
            Division = buf.ReadUnsignedShort();

            /* Pass one: walk the opcode stream and count the events. Every run's length falls out
               of these counts, which is what makes the runs locatable at all - nothing in the file
               states where one ends. */
            OpcodeCensus opcodes = CountOpcodes(data, TrackCount, Id);
            Opcodes = Slice(data, 0, opcodes.Length);

            /* Pass two: the delta times, one variable-length quantity per event plus one per track.
               Read through the cursor because a variable-length quantity's width is only known by
               reading it. */
            buf.Seek(opcodes.Length);
            int events = TrackCount + opcodes.Events;
            for (int i = 0; i < events; i++)
                buf.ReadVarInt();
            DeltaTimes = Slice(data, opcodes.Length, buf.Position - opcodes.Length);

            /* Pass three: the controller numbers. Every controller gets its values stored in a run
               of its own, so the numbers have to be replayed before the run lengths are known. */
            int cursor = buf.Position;
            int runsEnd = data.Length - TrailerLength;
            if (cursor + opcodes.Controller > runsEnd)
                throw new InvalidDataException("Track " + Id + " declares " + opcodes.Controller +
                    " controller events, more than its " + data.Length + " bytes can hold");

            ControllerNumbers = Slice(data, cursor, opcodes.Controller);
            cursor += opcodes.Controller;

            //Everything left is one run per field, laid out back to back in TrackRun's order
            int[] lengths = RunLengths(opcodes, ControllerNumbers);

            foreach (TrackRun run in RunOrder) {
                int length = lengths[(int) run];
                if (cursor + length > runsEnd)
                    throw new InvalidDataException("Track " + Id + "'s " + run + " run needs " +
                        length + " bytes at offset " + cursor + ", past the trailer at " + runsEnd);

                _runs[(int) run] = Slice(data, cursor, length);
                cursor += length;
            }

            TrailingBytes = Slice(data, cursor, runsEnd - cursor);

            return Project(opcodes);
        }

        // ===================================================================
        //  Encode
        // ===================================================================

        /// <summary>
        ///     Writes the stored form back out, reproducing the packed file byte for byte.
        /// </summary>
        /// <remarks>
        ///     A concatenation and nothing more, which is the whole point: every field this format
        ///     stores has more than one encoding that projects to the same MIDI, so anything
        ///     recomputed here would rewrite files nobody edited. The archive CRC covers the stored
        ///     bytes, so that would drag the reference-table entry of every group packed alongside
        ///     it along with it.
        /// </remarks>
        /// <returns>The packed file, positioned at the start.</returns>
        public JagStream Encode() {
            JagStream packed = new JagStream(StoredLength);

            packed.Write(Opcodes);
            packed.Write(DeltaTimes);
            packed.Write(ControllerNumbers);

            foreach (TrackRun run in RunOrder)
                packed.Write(_runs[(int) run]);

            packed.Write(TrailingBytes);

            //The header the format keeps at the end
            packed.WriteByte(TrackCount);
            packed.WriteShort(Division);

            return packed.Flip();
        }

        // ===================================================================
        //  Projection
        // ===================================================================

        /// <summary>
        ///     Rebuilds <see cref="Midi"/> and every derived statistic from the stored form.
        /// </summary>
        /// <remarks>
        ///     Public so an edited stored form can be re-projected without going back through the
        ///     cache. It re-derives the event counts from the retained opcode stream rather than
        ///     trusting anything <see cref="Decode"/> worked out, so a stored form that has been
        ///     changed inconsistently is reported here instead of producing plausible MIDI.
        /// </remarks>
        /// <returns>This track.</returns>
        /// <exception cref="InvalidDataException">
        ///     The opcode stream does not hold exactly <see cref="TrackCount"/> tracks' worth of
        ///     opcodes.
        /// </exception>
        public Track Project() {
            OpcodeCensus opcodes = CountOpcodes(Opcodes, TrackCount, Id);
            if (opcodes.Length != Opcodes.Length)
                throw new InvalidDataException("Track " + Id + "'s " + TrackCount +
                    " opcode streams end at " + opcodes.Length + " of " + Opcodes.Length + " bytes");

            return Project(opcodes);
        }

        /// <summary>Projects the MIDI and the statistics from an already-taken census.</summary>
        /// <param name="opcodes">The census of <see cref="Opcodes"/>.</param>
        /// <returns>This track.</returns>
        private Track Project(OpcodeCensus opcodes) {
            TempoEvents = opcodes.Tempo;
            NoteOnEvents = opcodes.NoteOn;
            NoteOffEvents = opcodes.NoteOff;
            ControllerEvents = opcodes.Controller;
            PitchWheelEvents = opcodes.PitchWheel;
            ChannelAfterTouchEvents = opcodes.ChannelPressure;
            KeyAfterTouchEvents = opcodes.KeyPressure;
            ProgramChangeEvents = opcodes.ProgramChange;

            ExpectedMidiLength = 14 + TrackCount * 10 + opcodes.StatusBytes + DeltaTimes.Length
                                 + 5 * opcodes.Tempo
                                 + 2 * (opcodes.NoteOn + opcodes.NoteOff + opcodes.Controller
                                        + opcodes.PitchWheel + opcodes.KeyPressure)
                                 + opcodes.ChannelPressure + opcodes.ProgramChange;

            Midi = BuildMidi(out int repaired);
            RepairedMetaStatusBytes = repaired;
            return this;
        }

        /// <summary>
        ///     Re-interleaves the runs into a standard MIDI file.
        /// </summary>
        /// <remarks>
        ///     Arm for arm the client's re-interleave (<c>Node_Sub7.java:183-304</c>), with one
        ///     deliberate divergence, the meta status byte documented on <see cref="Decode"/>.
        ///     <para>
        ///     Two things here are load-bearing and look like details. The accumulators and the
        ///     running channel are declared outside the track loop, matching
        ///     <c>Node_Sub7.java:174-182</c>: resetting them per MTrk desynchronises every chunk
        ///     after the first while still emitting structurally valid MIDI. And every run byte is
        ///     read signed, the pitch-wheel low half most of all - it feeds bit 7 upward through the
        ///     <c>&gt;&gt; 7</c> rather than being masked away like the others, so reading it
        ///     unsigned silently changes the second output byte.
        ///     </para>
        /// </remarks>
        /// <param name="repaired">Meta status bytes written that the client would have dropped.</param>
        /// <returns>The MIDI file.</returns>
        private byte[] BuildMidi(out int repaired) {
            JagStream midi = new JagStream(ExpectedMidiLength);
            JagStream deltas = new JagStream(DeltaTimes);

            int[] cursors = new int[RunOrder.Length];

            byte Next(TrackRun run) {
                return _runs[(int) run][cursors[(int) run]++];
            }

            midi.WriteInteger(1297377380); //MThd
            midi.WriteInteger(6); //header length
            midi.WriteShort(TrackCount > 1 ? 1 : 0); //format 1 once there is more than one track
            midi.WriteShort(TrackCount);
            midi.WriteShort(Division);

            int opcodeCursor = 0;
            int controllerNumberCursor = 0;
            int channel = 0;
            int note = 0;
            int noteOnVelocity = 0;
            int noteOffVelocity = 0;
            int pitchWheel = 0;
            int channelPressure = 0;
            int keyPressure = 0;
            repaired = 0;

            //Controller values are delta-encoded per controller number, not per run
            int[] controllerValues = new int[128];
            int controllerNumber = 0;

            for (int track = 0 ; track < TrackCount ; track++) {
                midi.WriteInteger(1297379947); //MTrk

                /* Reserve the chunk length. The client leaves a hole and back-patches it
                   (Node_Sub7.java:185,303); writing a placeholder is the same thing on a stream
                   that cannot seek past its own end. */
                int lengthField = midi.Position;
                midi.WriteInteger(0);
                int trackStart = midi.Position;

                int previous = -1;

                while (true) {
                    midi.WriteVarInt(deltas.ReadVarInt());

                    int packed = Opcodes[opcodeCursor++];
                    bool statusChanged = packed != previous;
                    previous = packed & 15;

                    if (packed == 7) {
                        //End of track, the one place the client can drop the status byte. See the
                        //CLIENT BUG note: outside the client it is not optional.
                        if (!statusChanged)
                            repaired++;
                        midi.WriteByte((byte) 255);
                        midi.WriteByte((byte) 47);
                        midi.WriteByte((byte) 0);
                        break;
                    }

                    if (packed == 23) {
                        /* Set tempo. The client writes this status byte too: statusChanged compares
                           23 against a value masked to four bits, so it is always true here. */
                        midi.WriteByte((byte) 255);
                        midi.WriteByte((byte) 81);
                        midi.WriteByte((byte) 3);
                        midi.WriteByte(Next(TrackRun.Tempo));
                        midi.WriteByte(Next(TrackRun.Tempo));
                        midi.WriteByte(Next(TrackRun.Tempo));
                        continue;
                    }

                    //The high nibble is a channel delta, exclusive-ored into the running channel
                    channel ^= packed >> 4;

                    switch (previous) {
                        case 0:
                            if (statusChanged)
                                midi.WriteByte((byte) (144 + channel));
                            note += (sbyte) Next(TrackRun.Note);
                            noteOnVelocity += (sbyte) Next(TrackRun.NoteOnVelocity);
                            midi.WriteByte((byte) (note & 127));
                            midi.WriteByte((byte) (noteOnVelocity & 127));
                            break;
                        case 1:
                            if (statusChanged)
                                midi.WriteByte((byte) (128 + channel));
                            note += (sbyte) Next(TrackRun.Note);
                            noteOffVelocity += (sbyte) Next(TrackRun.NoteOffVelocity);
                            midi.WriteByte((byte) (note & 127));
                            midi.WriteByte((byte) (noteOffVelocity & 127));
                            break;
                        case 2:
                            if (statusChanged)
                                midi.WriteByte((byte) (176 + channel));

                            controllerNumber = controllerNumber
                                               + ControllerNumbers[controllerNumberCursor++] & 127;
                            midi.WriteByte((byte) controllerNumber);

                            int value = (sbyte) Next(RunOf(controllerNumber))
                                        + controllerValues[controllerNumber];
                            controllerValues[controllerNumber] = value;
                            midi.WriteByte((byte) (value & 127));
                            break;
                        case 3:
                            /* Both halves must be read as signed. The low half feeds bit 7 upwards
                               through the >> 7 below, so reading it unsigned changes the second
                               output byte rather than being masked away like the others. */
                            if (statusChanged)
                                midi.WriteByte((byte) (224 + channel));
                            pitchWheel += (sbyte) Next(TrackRun.PitchWheelLow);
                            pitchWheel += (sbyte) Next(TrackRun.PitchWheelHigh) << 7;
                            midi.WriteByte((byte) (pitchWheel & 127));
                            midi.WriteByte((byte) (pitchWheel >> 7 & 127));
                            break;
                        case 4:
                            if (statusChanged)
                                midi.WriteByte((byte) (208 + channel));
                            channelPressure += (sbyte) Next(TrackRun.ChannelPressure);
                            midi.WriteByte((byte) (channelPressure & 127));
                            break;
                        case 5:
                            if (statusChanged)
                                midi.WriteByte((byte) (160 + channel));
                            note += (sbyte) Next(TrackRun.Note);
                            keyPressure += (sbyte) Next(TrackRun.KeyPressure);
                            midi.WriteByte((byte) (note & 127));
                            midi.WriteByte((byte) (keyPressure & 127));
                            break;
                        default:
                            //CountOpcodes has already rejected anything else, so this arm is
                            //program change or the census and the projection disagree.
                            if (previous != 6)
                                throw new InvalidDataException("Unhandled track opcode " + packed +
                                    " in track " + Id);
                            if (statusChanged)
                                midi.WriteByte((byte) (192 + channel));
                            midi.WriteByte(Next(TrackRun.Program));
                            break;
                    }
                }

                int trackEnd = midi.Position;
                midi.Seek(lengthField);
                midi.WriteInteger(trackEnd - trackStart);
                midi.Seek(trackEnd);
            }

            return midi.Flip().ToArray();
        }

        // ===================================================================
        //  Working out where the runs are
        // ===================================================================

        /// <summary>What the opcode stream says the file holds.</summary>
        private sealed class OpcodeCensus {
            /// <summary>Bytes of opcode stream the declared tracks consumed.</summary>
            public int Length;

            /// <summary>Status bytes the projection will emit, one per opcode that changes it.</summary>
            public int StatusBytes;

            /// <summary>Set-tempo events.</summary>
            public int Tempo;

            /// <summary>Note-on events.</summary>
            public int NoteOn;

            /// <summary>Note-off events.</summary>
            public int NoteOff;

            /// <summary>Control-change events.</summary>
            public int Controller;

            /// <summary>Pitch-wheel events.</summary>
            public int PitchWheel;

            /// <summary>Channel-pressure events.</summary>
            public int ChannelPressure;

            /// <summary>Polyphonic key-pressure events.</summary>
            public int KeyPressure;

            /// <summary>Program-change events, before the bank selects that share their run.</summary>
            public int ProgramChange;

            /// <summary>Events carrying a delta time, excluding the per-track end of track.</summary>
            public int Events => Tempo + NoteOn + NoteOff + Controller + PitchWheel
                                 + ChannelPressure + KeyPressure + ProgramChange;
        }

        /// <summary>
        ///     Walks the opcode stream and counts what it declares.
        /// </summary>
        /// <remarks>
        ///     Takes the whole packed file as happily as the extracted stream, because it stops on
        ///     the <paramref name="trackCount"/>-th terminator rather than on the end of the array.
        ///     That is what lets <see cref="Decode"/> use it to find where the stream ends and
        ///     <see cref="Project()"/> use it to check that it ends where it should.
        /// </remarks>
        /// <param name="data">The opcode stream, starting at offset zero.</param>
        /// <param name="trackCount">How many terminators to walk to.</param>
        /// <param name="trackId">The track id, for the failure message.</param>
        /// <returns>The census.</returns>
        /// <exception cref="InvalidDataException">
        ///     The stream ends early, or carries an opcode the client has no case for.
        /// </exception>
        private static OpcodeCensus CountOpcodes(byte[] data, int trackCount, int trackId) {
            OpcodeCensus census = new OpcodeCensus();
            int cursor = 0;

            for (int track = 0 ; track < trackCount ; track++) {
                int previous = -1;

                while (true) {
                    if (cursor >= data.Length)
                        throw new InvalidDataException("Track " + trackId + " runs out of opcodes " +
                            "during MTrk " + track + " of " + trackCount);

                    int packed = data[cursor++];
                    if (packed != previous)
                        census.StatusBytes++;

                    previous = packed & 15;

                    if (packed == 7)
                        break;

                    if (packed == 23)
                        census.Tempo++;
                    else if (previous == 0)
                        census.NoteOn++;
                    else if (previous == 1)
                        census.NoteOff++;
                    else if (previous == 2)
                        census.Controller++;
                    else if (previous == 3)
                        census.PitchWheel++;
                    else if (previous == 4)
                        census.ChannelPressure++;
                    else if (previous == 5)
                        census.KeyPressure++;
                    else if (previous == 6)
                        census.ProgramChange++;
                    else
                        //The client throws here too (Node_Sub7.java:62-68). Nothing in the 639 data
                        //reaches it, and carrying on would misplace every run boundary below.
                        throw new InvalidDataException("Unhandled track opcode " + packed +
                            " in track " + trackId);
                }
            }

            census.Length = cursor;
            return census;
        }

        /// <summary>
        ///     Which run a control change takes its value from.
        /// </summary>
        /// <remarks>
        ///     The one statement of the controller-to-run table, read by both
        ///     <see cref="RunLengths"/> and the projection. A controller counted into one run but
        ///     read out of another shifts every run boundary after it, and nothing in the file would
        ///     contradict it, so the two cannot be allowed to be separate switches.
        /// </remarks>
        /// <param name="controllerNumber">The MIDI controller number, 0..127.</param>
        /// <returns>The run holding its value.</returns>
        private static TrackRun RunOf(int controllerNumber) {
            switch (controllerNumber) {
                //Bank select shares the program-change run
                case 0:
                case 32: return TrackRun.Program;
                case 1: return TrackRun.Modulation;
                case 33: return TrackRun.ModulationLsb;
                case 7: return TrackRun.Volume;
                case 39: return TrackRun.VolumeLsb;
                case 10: return TrackRun.Pan;
                case 42: return TrackRun.PanLsb;
                case 99: return TrackRun.NrpnMsb;
                case 98: return TrackRun.NrpnLsb;
                case 101: return TrackRun.RpnMsb;
                case 100: return TrackRun.RpnLsb;
                //Switch controllers: sustain, portamento, all-off, reset, all-notes-off
                case 64:
                case 65:
                case 120:
                case 121:
                case 123: return TrackRun.SwitchedController;
                default: return TrackRun.OtherController;
            }
        }

        /// <summary>
        ///     How long each run is, which is the only thing that says where one ends.
        /// </summary>
        /// <remarks>
        ///     Every control change contributes one byte to whichever run its controller number
        ///     selects, so the numbers have to be replayed here rather than counted - the number is
        ///     itself delta-encoded (<c>Node_Sub7.java:94</c>) and cannot be read out of order.
        /// </remarks>
        /// <param name="opcodes">The opcode census.</param>
        /// <param name="controllerNumbers">The delta-encoded controller numbers.</param>
        /// <returns>Run lengths, indexed by <see cref="TrackRun"/>.</returns>
        private static int[] RunLengths(OpcodeCensus opcodes, byte[] controllerNumbers) {
            int[] lengths = new int[RunOrder.Length];

            int controllerNumber = 0;
            foreach (byte delta in controllerNumbers) {
                controllerNumber = controllerNumber + delta & 127;
                lengths[(int) RunOf(controllerNumber)]++;
            }

            lengths[(int) TrackRun.KeyPressure] += opcodes.KeyPressure;
            lengths[(int) TrackRun.ChannelPressure] += opcodes.ChannelPressure;

            //The pitch wheel is stored as two runs, one per half
            lengths[(int) TrackRun.PitchWheelHigh] += opcodes.PitchWheel;
            lengths[(int) TrackRun.PitchWheelLow] += opcodes.PitchWheel;

            //Note numbers are shared by note-on, note-off and polyphonic key pressure
            lengths[(int) TrackRun.Note] += opcodes.NoteOn + opcodes.NoteOff + opcodes.KeyPressure;

            lengths[(int) TrackRun.NoteOnVelocity] += opcodes.NoteOn;
            lengths[(int) TrackRun.NoteOffVelocity] += opcodes.NoteOff;

            //Program changes share their run with the bank selects already counted above
            lengths[(int) TrackRun.Program] += opcodes.ProgramChange;

            lengths[(int) TrackRun.Tempo] += opcodes.Tempo * 3;

            return lengths;
        }

        private static byte[][] EmptyRuns() {
            byte[][] runs = new byte[RunOrder.Length][];
            for (int i = 0 ; i < runs.Length ; i++)
                runs[i] = Array.Empty<byte>();
            return runs;
        }

        private static byte[] Slice(byte[] data, int offset, int length) {
            if (length <= 0)
                return Array.Empty<byte>();

            byte[] slice = new byte[length];
            Array.Copy(data, offset, slice, 0, length);
            return slice;
        }
    }
}
