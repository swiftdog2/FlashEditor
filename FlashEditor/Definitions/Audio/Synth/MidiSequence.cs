using System;
using System.Collections.Generic;
using System.IO;

namespace FlashEditor.Definitions.Audio.Synth {
    /// <summary>One event of a parsed sequence, at an absolute tick.</summary>
    public readonly struct MidiSequenceEvent {
        /// <summary>When the event happens, in sequence ticks from the start.</summary>
        public long Tick { get; }

        /// <summary>The status byte, or 0 for a tempo change.</summary>
        public int Status { get; }

        /// <summary>The first data byte, or the new microseconds per quarter note for a tempo change.</summary>
        public int Data1 { get; }

        /// <summary>The second data byte.</summary>
        public int Data2 { get; }

        /// <summary>Whether this is a tempo change rather than a channel message.</summary>
        public bool IsTempo => Status == 0;

        /// <summary>Binds an event to its tick.</summary>
        /// <param name="tick">When it happens.</param>
        /// <param name="status">The status byte, or 0 for a tempo change.</param>
        /// <param name="data1">The first data byte, or the tempo.</param>
        /// <param name="data2">The second data byte.</param>
        public MidiSequenceEvent(long tick, int status, int data1, int data2) {
            Tick = tick;
            Status = status;
            Data1 = data1;
            Data2 = data2;
        }
    }

    /// <summary>
    ///     A standard MIDI file flattened into one time-ordered event list.
    /// </summary>
    /// <remarks>
    ///     The client parses the same shape with <c>Class173</c> - <c>Node_Sub7</c> re-emits the
    ///     cache's own column-oriented track format as a real SMF (<c>Node_Sub7.java:168-172</c>) and
    ///     then reads it back - so parsing <c>Track.Midi</c> here is the same seam the client uses
    ///     rather than a shortcut around the cache format.
    ///     <para>
    ///     Only what the synthesiser can act on is kept: channel messages and tempo. Track structure
    ///     is discarded because the synthesiser has no per-track state; the client's does not either.
    ///     </para>
    /// </remarks>
    public sealed class MidiSequence {
        /// <summary>The events, ascending by tick, with a stable order within a tick.</summary>
        public IReadOnlyList<MidiSequenceEvent> Events { get; }

        /// <summary>Ticks per quarter note, from the file header.</summary>
        public int Division { get; }

        /// <summary>The tick the last event falls on.</summary>
        public long LengthInTicks { get; }

        /// <summary>Parses a standard MIDI file.</summary>
        /// <param name="midi">The file.</param>
        /// <exception cref="ArgumentNullException">The file is null.</exception>
        /// <exception cref="InvalidDataException">The file is not a standard MIDI file this can read.</exception>
        public MidiSequence(byte[] midi) {
            if (midi == null)
                throw new ArgumentNullException(nameof(midi));
            if (midi.Length < 14 || midi[0] != 'M' || midi[1] != 'T' || midi[2] != 'h' || midi[3] != 'd')
                throw new InvalidDataException("Not a standard MIDI file: the header chunk is missing.");

            int tracks = (midi[10] << 8) | midi[11];
            Division = (midi[12] << 8) | midi[13];
            if (Division <= 0)
                throw new InvalidDataException(
                    "Division " + Division + "; SMPTE timing is negative here and the cache's tracks are " +
                    "all metrical, so nothing produces one.");

            var events = new List<(long Tick, int Order, MidiSequenceEvent Event)>();
            int offset = 8 + ((midi[4] << 24) | (midi[5] << 16) | (midi[6] << 8) | midi[7]);
            int order = 0;

            for (int track = 0; track < tracks && offset + 8 <= midi.Length; track++) {
                if (midi[offset] != 'M' || midi[offset + 1] != 'T' || midi[offset + 2] != 'r' ||
                    midi[offset + 3] != 'k') {
                    //Skip a chunk that is not a track, which the format permits.
                    int skip = (midi[offset + 4] << 24) | (midi[offset + 5] << 16) | (midi[offset + 6] << 8) |
                               midi[offset + 7];
                    offset += 8 + skip;
                    track--;
                    continue;
                }

                int length = (midi[offset + 4] << 24) | (midi[offset + 5] << 16) | (midi[offset + 6] << 8) |
                             midi[offset + 7];
                int position = offset + 8;
                int end = Math.Min(midi.Length, position + length);
                offset = position + length;

                long tick = 0;
                int running = 0;

                while (position < end) {
                    tick += ReadVariableLength(midi, ref position, end);
                    if (position >= end)
                        break;

                    int status = midi[position];
                    if (status < 0x80) {
                        //Running status: the previous status byte is reused and only data follows.
                        status = running;
                    } else {
                        position++;
                        if (status < 0xf0)
                            running = status;
                    }

                    if (status == 0xff) {
                        if (position >= end)
                            break;
                        int type = midi[position++];
                        int metaLength = ReadVariableLength(midi, ref position, end);
                        if (type == 0x51 && metaLength == 3 && position + 3 <= end) {
                            int tempo = (midi[position] << 16) | (midi[position + 1] << 8) | midi[position + 2];
                            events.Add((tick, order++, new MidiSequenceEvent(tick, 0, tempo, 0)));
                        }

                        position += metaLength;
                        continue;
                    }

                    if (status == 0xf0 || status == 0xf7) {
                        int sysexLength = ReadVariableLength(midi, ref position, end);
                        position += sysexLength;
                        continue;
                    }

                    int data1 = position < end ? midi[position++] : 0;
                    int data2 = 0;
                    if (!IsSingleDataByte(status))
                        data2 = position < end ? midi[position++] : 0;

                    events.Add((tick, order++, new MidiSequenceEvent(tick, status, data1, data2)));
                }
            }

            /* Ordered by tick, then by the order the events were read. Two events on the same tick
               in different tracks must keep a deterministic relative order, or a program change and
               the note that follows it can swap and the note plays on the previous instrument. */
            events.Sort((a, b) => a.Tick != b.Tick ? a.Tick.CompareTo(b.Tick) : a.Order.CompareTo(b.Order));

            var flattened = new MidiSequenceEvent[events.Count];
            for (int i = 0; i < events.Count; i++)
                flattened[i] = events[i].Event;

            Events = flattened;
            LengthInTicks = flattened.Length == 0 ? 0 : flattened[flattened.Length - 1].Tick;
        }

        /// <summary>Whether a status byte takes one data byte rather than two.</summary>
        /// <remarks>
        ///     Program change and channel aftertouch only. The client keeps the same answer as a
        ///     128-entry table at <c>Class173.java:6-9</c>.
        /// </remarks>
        /// <param name="status">The status byte.</param>
        /// <returns>Whether it takes one data byte.</returns>
        private static bool IsSingleDataByte(int status) {
            int kind = status & 0xf0;
            return kind == 0xc0 || kind == 0xd0;
        }

        /// <summary>Reads a MIDI variable-length quantity.</summary>
        /// <param name="data">The file.</param>
        /// <param name="position">Where to read from; advanced past the value.</param>
        /// <param name="end">Where the current chunk ends.</param>
        /// <returns>The value.</returns>
        private static int ReadVariableLength(byte[] data, ref int position, int end) {
            int value = 0;
            while (position < end) {
                int part = data[position++];
                value = (value << 7) | (part & 0x7f);
                if ((part & 0x80) == 0)
                    break;
            }

            return value;
        }
    }
}
