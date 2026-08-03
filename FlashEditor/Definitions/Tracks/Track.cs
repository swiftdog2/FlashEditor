using System.IO;

namespace FlashEditor.Definitions.Tracks {
    /// <summary>
    ///     A music track: RuneScape's column-major packing of a standard MIDI file.
    /// </summary>
    /// <remarks>
    ///     JS5 index 6 (music) and index 11 (jingles), one file per group. Both indexes hold this
    ///     same format - the client opens 6 as <c>Node_Sub10_Sub1.aJS5Archive_5544</c> and 11 as
    ///     <c>Class61.aJS5Archive_481</c> (InterfaceSettings.java:164,168) and hands either to the
    ///     same decoder, <c>Node_Sub7.method985</c>.
    ///
    ///     The packing is what makes this worth a decoder rather than a file copy. A MIDI file
    ///     interleaves everything - status byte, note, velocity, delta time - per event. Jagex
    ///     splits the same data into one contiguous run per field, so that all the note numbers sit
    ///     together, all the velocities sit together, and so on. Runs of similar bytes compress far
    ///     better, and most of the runs are stored as deltas against the previous value in the same
    ///     run rather than absolutely. Decoding is therefore three passes: count the events to work
    ///     out where each run begins, walk the runs in parallel, and re-interleave.
    ///
    ///     The header is the last three bytes of the file, not the first
    ///     (<c>Node_Sub7.java:22</c>), because the opcode stream has to start at offset zero: the
    ///     second pass indexes it from the raw buffer at index 0 while the first pass reads it
    ///     through the cursor.
    ///
    ///     Decode only. Nothing here writes back into the cache, so there is no encoder and no
    ///     byte-identity sweep to pair with one.
    /// </remarks>
    public class Track {
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
        ///     it reproduces - see <see cref="TrackNames"/>. Empty for the 365 index-6 groups no
        ///     listed name hashes to, and for every jingle in index 11.
        /// </remarks>
        public string Name { get; set; } = string.Empty;

        /// <summary>Length of the packed file, for comparison against <see cref="MidiLength"/>.</summary>
        public int PackedLength { get; private set; }

        /// <summary>Number of MIDI tracks, the <c>ntrks</c> field of the emitted MThd chunk.</summary>
        public int TrackCount { get; private set; }

        /// <summary>Ticks per quarter note, the <c>division</c> field of the emitted MThd chunk.</summary>
        public int Division { get; private set; }

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

        /// <summary>Program-change events across all tracks.</summary>
        public int ProgramChangeEvents { get; private set; }

        /// <summary>
        ///     The MIDI length the packed file's own event counts predict.
        /// </summary>
        /// <remarks>
        ///     The client sizes its output buffer from this and then writes into it without ever
        ///     re-measuring (<c>Node_Sub7.java:166</c>), so it is the format's own statement of how
        ///     long the decoded file must be. Comparing it against what the second pass actually
        ///     emitted is the only check available that does not just run this decoder against its
        ///     own assumptions.
        /// </remarks>
        public int ExpectedMidiLength { get; private set; }

        /// <summary>
        ///     Meta-event status bytes emitted beyond what <see cref="ExpectedMidiLength"/> allows for.
        /// </summary>
        /// <remarks>
        ///     See the CLIENT BUG note on <see cref="Decode"/>. Non-zero means this track is one the
        ///     client would have written as unplayable MIDI.
        /// </remarks>
        public int RepairedMetaStatusBytes { get; private set; }

        /// <summary>The decoded standard MIDI file, or null before <see cref="Decode"/> has run.</summary>
        public byte[]? Midi { get; private set; }

        /// <summary>Length of <see cref="Midi"/>, for display before the bytes are wanted.</summary>
        public int MidiLength => Midi == null ? 0 : Midi.Length;

        /// <summary>
        ///     Decodes the packed file into a standard MIDI file.
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
        ///     reconciles.
        /// </remarks>
        /// <param name="buf">The packed file, positioned anywhere.</param>
        /// <returns>This track.</returns>
        /// <exception cref="InvalidDataException">The opcode stream carries an opcode the client has no case for.</exception>
        public Track Decode(JagStream buf) {
            /* Index the payload directly rather than through the cursor. The second pass reads
               several runs at once, so it cannot use a single position, and copying the array once
               here keeps that out of the inner loop - the previous version called ToArray() per
               event, which copies the whole file each time. */
            byte[] data = buf.ToArray();
            PackedLength = data.Length;

            //The three-byte header is at the end of the file (Node_Sub7.java:22)
            buf.Seek(data.Length - 3);
            int trackCount = buf.ReadUnsignedByte();
            int division = buf.ReadUnsignedShort();

            //MThd (14 bytes) plus an MTrk header (10 bytes) per track
            int midiLength = 14 + trackCount * 10;

            buf.Seek0();

            int tempoCount = 0;
            int controllerCount = 0;
            int noteOnCount = 0;
            int noteOffCount = 0;
            int pitchWheelCount = 0;
            int channelAfterTouchCount = 0;
            int keyAfterTouchCount = 0;
            int programChangeCount = 0;

            /* Pass one: walk the opcode stream and count the events. Every run's length falls out
               of these counts, which is what makes the second pass able to seek straight to each
               one. A status byte is emitted only when the opcode differs from the previous
               opcode's low nibble, so counting the transitions here sizes the output exactly. */
            for (int track = 0; track < trackCount; track++) {
                int previous = -1;

                while (true) {
                    int packed = buf.ReadUnsignedByte();
                    if (packed != previous)
                        midiLength++;

                    previous = packed & 15;

                    if (packed == 7)
                        break;

                    if (packed == 23)
                        tempoCount++;
                    else if (previous == 0)
                        noteOnCount++;
                    else if (previous == 1)
                        noteOffCount++;
                    else if (previous == 2)
                        controllerCount++;
                    else if (previous == 3)
                        pitchWheelCount++;
                    else if (previous == 4)
                        channelAfterTouchCount++;
                    else if (previous == 5)
                        keyAfterTouchCount++;
                    else if (previous == 6)
                        programChangeCount++;
                    else
                        //The client throws here too (Node_Sub7.java:62-68). Nothing in the 639 data
                        //reaches it, and carrying on would misplace every run boundary below.
                        throw new InvalidDataException("Unhandled track opcode " + packed + " in track " + Id);
                }
            }

            midiLength += 5 * tempoCount;
            midiLength += 2 * (noteOnCount + noteOffCount + controllerCount + pitchWheelCount + keyAfterTouchCount);
            midiLength += channelAfterTouchCount + programChangeCount;

            //Pass two: the delta times, one variable-length quantity per event plus one per track
            int deltaTimeStart = buf.Position;
            int eventCount = trackCount + tempoCount + controllerCount + noteOnCount + noteOffCount
                    + pitchWheelCount + channelAfterTouchCount + keyAfterTouchCount + programChangeCount;

            for (int i = 0; i < eventCount; i++)
                buf.ReadVarInt();

            //Delta times are copied through unchanged, so they cost what they occupy
            midiLength += buf.Position - deltaTimeStart;

            /* Pass three: the controller numbers, delta-encoded against the previous one. Every
               controller gets its values stored in a run of its own, so the numbers have to be
               replayed before the run boundaries below can be worked out. */
            int controllerNumberStart = buf.Position;

            int modulationCount = 0;
            int modulationLsbCount = 0;
            int volumeCount = 0;
            int volumeLsbCount = 0;
            int panCount = 0;
            int panLsbCount = 0;
            int nrpnMsbCount = 0;
            int nrpnLsbCount = 0;
            int rpnMsbCount = 0;
            int rpnLsbCount = 0;
            int switchedControllerCount = 0;
            int otherControllerCount = 0;

            //Kept apart from programChangeCount because bank select shares the program run below,
            //and the reported event count should stay the number of program changes
            ProgramChangeEvents = programChangeCount;

            int controllerNumber = 0;
            for (int i = 0; i < controllerCount; i++) {
                controllerNumber = controllerNumber + buf.ReadUnsignedByte() & 127;

                switch (controllerNumber) {
                    case 0:
                    case 32:
                        //Bank select shares the program-change run
                        programChangeCount++;
                        break;
                    case 1: modulationCount++; break;
                    case 33: modulationLsbCount++; break;
                    case 7: volumeCount++; break;
                    case 39: volumeLsbCount++; break;
                    case 10: panCount++; break;
                    case 42: panLsbCount++; break;
                    case 99: nrpnMsbCount++; break;
                    case 98: nrpnLsbCount++; break;
                    case 101: rpnMsbCount++; break;
                    case 100: rpnLsbCount++; break;
                    case 64:
                    case 65:
                    case 120:
                    case 121:
                    case 123:
                        //Switch controllers: sustain, portamento, all-off, reset, all-notes-off
                        switchedControllerCount++;
                        break;
                    default: otherControllerCount++; break;
                }
            }

            /* Everything left is one run per field, laid out back to back in this fixed order. The
               cursors are the only record of where each starts, so they are captured as the
               position walks past. */
            int switchedControllerCursor = buf.Position; buf.Skip(switchedControllerCount);
            int keyPressureCursor = buf.Position; buf.Skip(keyAfterTouchCount);
            int channelPressureCursor = buf.Position; buf.Skip(channelAfterTouchCount);
            int pitchWheelHighCursor = buf.Position; buf.Skip(pitchWheelCount);
            int modulationCursor = buf.Position; buf.Skip(modulationCount);
            int volumeCursor = buf.Position; buf.Skip(volumeCount);
            int panCursor = buf.Position; buf.Skip(panCount);

            //Note numbers are shared by note-on, note-off and polyphonic key pressure
            int noteCursor = buf.Position; buf.Skip(noteOnCount + noteOffCount + keyAfterTouchCount);

            int noteOnVelocityCursor = buf.Position; buf.Skip(noteOnCount);
            int otherControllerCursor = buf.Position; buf.Skip(otherControllerCount);
            int noteOffVelocityCursor = buf.Position; buf.Skip(noteOffCount);
            int modulationLsbCursor = buf.Position; buf.Skip(modulationLsbCount);
            int volumeLsbCursor = buf.Position; buf.Skip(volumeLsbCount);
            int panLsbCursor = buf.Position; buf.Skip(panLsbCount);
            int programCursor = buf.Position; buf.Skip(programChangeCount);
            int pitchWheelLowCursor = buf.Position; buf.Skip(pitchWheelCount);
            int nrpnMsbCursor = buf.Position; buf.Skip(nrpnMsbCount);
            int nrpnLsbCursor = buf.Position; buf.Skip(nrpnLsbCount);
            int rpnMsbCursor = buf.Position; buf.Skip(rpnMsbCount);
            int rpnLsbCursor = buf.Position; buf.Skip(rpnLsbCount);
            int tempoCursor = buf.Position; buf.Skip(tempoCount * 3);

            JagStream midi = new JagStream(midiLength);

            midi.WriteInteger(1297377380); //MThd
            midi.WriteInteger(6); //header length
            midi.WriteShort((short) (trackCount > 1 ? 1 : 0)); //format 1 once there is more than one track
            midi.WriteShort((short) trackCount);
            midi.WriteShort((short) division);

            //Pass four: re-interleave. Delta times resume where pass two started them.
            buf.Seek(deltaTimeStart);

            int opcodeCursor = 0;
            int channel = 0;
            int note = 0;
            int noteOnVelocity = 0;
            int noteOffVelocity = 0;
            int pitchWheel = 0;
            int channelPressure = 0;
            int keyPressure = 0;
            int repairedMetaStatusBytes = 0;

            //Controller values are delta-encoded per controller number, not per run
            int[] controllerValues = new int[128];
            controllerNumber = 0;

            for (int track = 0; track < trackCount; track++) {
                midi.WriteInteger(1297379947); //MTrk

                /* Reserve the chunk length. The client leaves a hole and back-patches it
                   (Node_Sub7.java:185,303); writing a placeholder is the same thing on a stream
                   that cannot seek past its own end. */
                int lengthField = midi.Position;
                midi.WriteInteger(0);
                int trackStart = midi.Position;

                int previous = -1;

                while (true) {
                    midi.WriteVarInt(buf.ReadVarInt());

                    int packed = data[opcodeCursor++] & 255;
                    bool statusChanged = packed != previous;
                    previous = packed & 15;

                    if (packed == 7) {
                        //End of track, the one place the client can drop the status byte. See the
                        //CLIENT BUG note: outside the client it is not optional.
                        if (!statusChanged)
                            repairedMetaStatusBytes++;
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
                        midi.WriteByte(data[tempoCursor++]);
                        midi.WriteByte(data[tempoCursor++]);
                        midi.WriteByte(data[tempoCursor++]);
                        continue;
                    }

                    //The high nibble is a channel delta, exclusive-ored into the running channel
                    channel ^= packed >> 4;

                    switch (previous) {
                        case 0:
                            if (statusChanged)
                                midi.WriteByte((byte) (144 + channel));
                            note += (sbyte) data[noteCursor++];
                            noteOnVelocity += (sbyte) data[noteOnVelocityCursor++];
                            midi.WriteByte((byte) (note & 127));
                            midi.WriteByte((byte) (noteOnVelocity & 127));
                            break;
                        case 1:
                            if (statusChanged)
                                midi.WriteByte((byte) (128 + channel));
                            note += (sbyte) data[noteCursor++];
                            noteOffVelocity += (sbyte) data[noteOffVelocityCursor++];
                            midi.WriteByte((byte) (note & 127));
                            midi.WriteByte((byte) (noteOffVelocity & 127));
                            break;
                        case 2:
                            if (statusChanged)
                                midi.WriteByte((byte) (176 + channel));

                            controllerNumber = controllerNumber + data[controllerNumberStart++] & 127;
                            midi.WriteByte((byte) controllerNumber);

                            sbyte delta;
                            switch (controllerNumber) {
                                case 0:
                                case 32: delta = (sbyte) data[programCursor++]; break;
                                case 1: delta = (sbyte) data[modulationCursor++]; break;
                                case 33: delta = (sbyte) data[modulationLsbCursor++]; break;
                                case 7: delta = (sbyte) data[volumeCursor++]; break;
                                case 39: delta = (sbyte) data[volumeLsbCursor++]; break;
                                case 10: delta = (sbyte) data[panCursor++]; break;
                                case 42: delta = (sbyte) data[panLsbCursor++]; break;
                                case 99: delta = (sbyte) data[nrpnMsbCursor++]; break;
                                case 98: delta = (sbyte) data[nrpnLsbCursor++]; break;
                                case 101: delta = (sbyte) data[rpnMsbCursor++]; break;
                                case 100: delta = (sbyte) data[rpnLsbCursor++]; break;
                                case 64:
                                case 65:
                                case 120:
                                case 121:
                                case 123: delta = (sbyte) data[switchedControllerCursor++]; break;
                                default: delta = (sbyte) data[otherControllerCursor++]; break;
                            }

                            int value = delta + controllerValues[controllerNumber];
                            controllerValues[controllerNumber] = value;
                            midi.WriteByte((byte) (value & 127));
                            break;
                        case 3:
                            if (statusChanged)
                                midi.WriteByte((byte) (224 + channel));

                            /* Both halves must be read as signed. The low half feeds bit 7 upwards
                               through the >> 7 below, so reading it unsigned changes the second
                               output byte rather than being masked away like the others. */
                            pitchWheel += (sbyte) data[pitchWheelLowCursor++];
                            pitchWheel += (sbyte) data[pitchWheelHighCursor++] << 7;
                            midi.WriteByte((byte) (pitchWheel & 127));
                            midi.WriteByte((byte) (pitchWheel >> 7 & 127));
                            break;
                        case 4:
                            if (statusChanged)
                                midi.WriteByte((byte) (208 + channel));
                            channelPressure += (sbyte) data[channelPressureCursor++];
                            midi.WriteByte((byte) (channelPressure & 127));
                            break;
                        case 5:
                            if (statusChanged)
                                midi.WriteByte((byte) (160 + channel));
                            note += (sbyte) data[noteCursor++];
                            keyPressure += (sbyte) data[keyPressureCursor++];
                            midi.WriteByte((byte) (note & 127));
                            midi.WriteByte((byte) (keyPressure & 127));
                            break;
                        default:
                            if (previous != 6)
                                throw new InvalidDataException("Unhandled track opcode " + packed + " in track " + Id);
                            if (statusChanged)
                                midi.WriteByte((byte) (192 + channel));
                            midi.WriteByte(data[programCursor++]);
                            break;
                    }
                }

                int trackEnd = midi.Position;
                midi.Seek(lengthField);
                midi.WriteInteger(trackEnd - trackStart);
                midi.Seek(trackEnd);
            }

            TrackCount = trackCount;
            Division = division;
            TempoEvents = tempoCount;
            NoteOnEvents = noteOnCount;
            NoteOffEvents = noteOffCount;
            ControllerEvents = controllerCount;
            PitchWheelEvents = pitchWheelCount;
            ChannelAfterTouchEvents = channelAfterTouchCount;
            KeyAfterTouchEvents = keyAfterTouchCount;
            ExpectedMidiLength = midiLength;
            RepairedMetaStatusBytes = repairedMetaStatusBytes;

            Midi = midi.Flip().ToArray();
            return this;
        }
    }
}
