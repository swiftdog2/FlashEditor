using System;
using System.Collections.Generic;
using System.Linq;

namespace FlashEditor.Definitions.Audio {
    /// <summary>
    ///     One procedural sound effect: up to ten tones mixed together, and an optional loop window.
    /// </summary>
    /// <remarks>
    ///     JS5 index 4 (<c>RSConstants.SOUND_EFFECTS</c>). Not sampled audio - the record is a
    ///     synthesiser patch, and the client renders it to 8-bit signed mono PCM at 22050 Hz
    ///     (<c>Class37.java:67-71</c>, clamped at <c>:94-96</c>). <b>Nothing here renders anything</b>;
    ///     this is the field-level codec only.
    ///     <para>
    ///     One file per group and the group id is the effect id: <c>Class37.method342</c>
    ///     (<c>Class37.java:9</c>) fetches <c>getChildFromFolder(id, fileId)</c> and every caller
    ///     passes a literal 0 (<c>Class280.java:192-193</c>, <c>:258-259</c>,
    ///     <c>Particle_Sub3_Sub2.java:24</c>). The reference table sets no flags at all, so an effect
    ///     has no name and is addressable by id alone.
    ///     </para>
    ///     <para>
    ///     The record is <b>not</b> an opcode stream and it is <b>canonical</b>, which is rare in this
    ///     cache. Everything is positional or counted, and the only field with two legal
    ///     representations is a smart - measured across all 10,238 records, 125,592 unsigned smarts
    ///     and 31,311 signed ones, none of which uses the wide form for a value the narrow form holds.
    ///     So no encoding-choice capture is needed and shortest-form re-encoding reproduces the file.
    ///     The two things that <i>are</i> load-bearing and easy to normalise away are recorded on the
    ///     types that own them: a tone's slot <b>position</b> (1884 records have a gap in theirs), and
    ///     the filter's raw interpolation mask.
    ///     </para>
    /// </remarks>
    public sealed class SoundEffectDefinition {
        /// <summary>Tone slots in a record, always written whether or not they hold a tone.</summary>
        /// <remarks><c>Class37.java:29</c>. An empty slot is a single zero byte.</remarks>
        public const int ToneSlots = 10;

        /// <summary>The largest value the <c>u16</c> loop fields can carry.</summary>
        public const int MaxLoopField = 0xFFFF;

        /// <summary>The effect id, which is the group id.</summary>
        public int Id { get; set; }

        /// <summary>
        ///     The ten tone slots, null where the record holds none.
        /// </summary>
        /// <remarks>
        ///     <b>The index is the slot and must be preserved.</b> The reader walks all ten positions
        ///     (<c>Class37.java:29-36</c>), so a record with tones at slots 0 and 2 is not the same
        ///     file as one with tones at 0 and 1 - it has an extra zero byte in a different place.
        ///     1884 of the 10,238 effects in this cache have a gap, the commonest being a single tone
        ///     sitting alone in slot 1. Compacting them would change 1884 files that nobody edited.
        ///     <para>
        ///     Slot order carries no priority: the mixdown sums every slot into the same buffer at its
        ///     own <see cref="SoundEffectTone.Offset"/> (<c>Class37.java:87-99</c>).
        ///     </para>
        /// </remarks>
        public SoundEffectTone?[] Tones { get; } = new SoundEffectTone?[ToneSlots];

        /// <summary>Where the loop begins, in milliseconds.</summary>
        /// <remarks>
        ///     <c>Class37.anInt352</c>, converted to samples at <c>:70</c>. The effect loops only when
        ///     this is below <see cref="LoopEnd"/> (<c>:49,60</c>), which 1009 of the effects in this
        ///     cache satisfy; the rest store two numbers that are never compared for anything else.
        /// </remarks>
        public int LoopStart { get; set; }

        /// <summary>Where the loop ends, in milliseconds.</summary>
        /// <remarks><c>Class37.anInt353</c>.</remarks>
        public int LoopEnd { get; set; }

        /// <summary>How many of the ten slots hold a tone.</summary>
        public int ToneCount => Tones.Count(tone => tone != null);

        /// <summary>The slots that hold a tone, ascending.</summary>
        public IEnumerable<int> OccupiedSlots {
            get {
                for (int slot = 0; slot < ToneSlots; slot++)
                    if (Tones[slot] != null)
                        yield return slot;
            }
        }

        /// <summary>Whether the effect loops, by the client's own test.</summary>
        /// <remarks><c>Class37.java:49,60</c> - strictly less than, so an empty window does not loop.</remarks>
        public bool Loops => LoopStart < LoopEnd;

        /// <summary>Whether any tone is shaped in a way the 637 client cannot load.</summary>
        /// <remarks>
        ///     False for every effect in this cache. Two fixed-size arrays in the client are narrower
        ///     than what the format can express, and a file that exceeds either is well formed and
        ///     still crashes it - see <see cref="SoundEffectTone"/> and <see cref="SoundEffectFilter"/>.
        /// </remarks>
        public bool ExceedsClientLimits =>
            Tones.Any(tone => tone != null && tone.ExceedsClientLimits);

        /// <summary>Decodes one sound effect.</summary>
        /// <remarks>
        ///     <c>Class37</c>'s buffer constructor (<c>Class37.java:26-39</c>). The slot marker is
        ///     peeked rather than read and rewound as the client does it - same bytes consumed, and it
        ///     keeps the rewind out of a stream type that has no <c>caret--</c>.
        /// </remarks>
        /// <param name="stream">The stored file, positioned at its start.</param>
        /// <returns>This definition.</returns>
        /// <exception cref="ArgumentNullException"><paramref name="stream"/> is null.</exception>
        /// <exception cref="System.IO.EndOfStreamException">The record is shorter than its own fields declare.</exception>
        public SoundEffectDefinition Decode(JagStream stream) {
            if (stream == null)
                throw new ArgumentNullException(nameof(stream));

            for (int slot = 0; slot < ToneSlots; slot++) {
                if (stream.PeekUnsignedByte() == 0) {
                    stream.ReadUnsignedByte();
                    Tones[slot] = null;
                    continue;
                }

                Tones[slot] = new SoundEffectTone().Decode(stream);
            }

            LoopStart = stream.ReadUnsignedShort();
            LoopEnd = stream.ReadUnsignedShort();
            return this;
        }

        /// <summary>Encodes this effect back to its stored representation.</summary>
        /// <remarks>
        ///     Byte-identical for an unedited effect. Every field is written back at the width and in
        ///     the position it was read from, and the format has no opcode ordering, no repetition and
        ///     no aliased value to reproduce.
        /// </remarks>
        /// <returns>The encoded file, positioned at 0.</returns>
        /// <exception cref="InvalidOperationException">
        ///     A field has been edited to something the format cannot express, or to something that
        ///     would make the record read back as a different one.
        /// </exception>
        public JagStream Encode() {
            JagStream stream = new JagStream();

            for (int slot = 0; slot < ToneSlots; slot++) {
                SoundEffectTone? tone = Tones[slot];
                if (tone == null) {
                    stream.WriteByte(0);
                    continue;
                }

                tone.Encode(stream, "Sound effect " + Id + " tone " + slot);
            }

            stream.WriteShort(Loop(LoopStart, "loop start"));
            stream.WriteShort(Loop(LoopEnd, "loop end"));
            return stream.Flip();
        }

        private int Loop(int value, string field) {
            if (value < 0 || value > MaxLoopField)
                throw new InvalidOperationException(
                    "Sound effect " + Id + " has " + field + " " + value + " ms, and the field is an " +
                    "unsigned short, so 0 to " + MaxLoopField + " fit.");
            return value;
        }
    }
}
