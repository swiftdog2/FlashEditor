using System;
using System.Collections.Generic;
using FlashEditor.IO;

namespace FlashEditor.Definitions.Audio {
    /// <summary>
    ///     One partial of a tone: how loud it is, how far it is detuned, and how late it starts.
    /// </summary>
    /// <remarks>
    ///     <c>Class344.java:108-116</c> reads the triple and <c>:173-181</c> turns it into oscillator
    ///     state. The list terminates on an <see cref="Amplitude"/> of 0, so a zero amplitude cannot
    ///     be stored - it is the terminator.
    /// </remarks>
    public sealed class SoundEffectHarmonic {
        /// <summary>Level as a percentage of the tone's own, 100 being unity.</summary>
        /// <remarks><c>Class344.java:177</c>: <c>(amplitude &lt;&lt; 14) / 100</c>. Never 0.</remarks>
        public int Amplitude { get; set; }

        /// <summary>Detune in tenths of a semitone.</summary>
        /// <remarks>
        ///     <c>Class344.java:179</c> raises 1.0057929410678534 to this power, and that constant is
        ///     2^(1/120) - so 120 units make an octave and 10 make a semitone. Signed, and negative in
        ///     real records.
        /// </remarks>
        public int PitchOffset { get; set; }

        /// <summary>How late this partial enters, as a fraction of the tone in 1/65536ths.</summary>
        /// <remarks><c>Class344.java:176</c> scales it by the samples-per-unit factor.</remarks>
        public int Delay { get; set; }
    }

    /// <summary>
    ///     A pair of envelopes that modulate something: one drives the modulator's own phase, the
    ///     other its depth.
    /// </summary>
    /// <remarks>
    ///     The three optional slots in a tone are all this shape, and all three are gated by the same
    ///     one-byte marker (<c>Class344.java:84-107</c>) - so both envelopes are present or neither
    ///     is, which is why they are one object rather than two nullable properties that could
    ///     disagree.
    ///     <para>
    ///     <b><see cref="Rate"/>'s form byte is the marker.</b> The reader peeks it, and a zero means
    ///     the whole pair is absent. Writing a rate envelope with form 0 produces a file whose
    ///     remaining fields are read as a different record entirely.
    ///     </para>
    /// </remarks>
    public sealed class SoundEffectModulator {
        /// <summary>The envelope driving the modulator's phase. Its form byte doubles as the presence marker.</summary>
        public SoundEffectEnvelope Rate { get; set; } = new SoundEffectEnvelope();

        /// <summary>The envelope driving how far the modulation swings.</summary>
        public SoundEffectEnvelope Depth { get; set; } = new SoundEffectEnvelope();
    }

    /// <summary>
    ///     One of the ten voices a sound effect mixes: an oscillator with its envelopes, partials,
    ///     delay line and filter.
    /// </summary>
    /// <remarks>
    ///     <c>Class344.method3820</c> (<c>Class344.java:78-124</c>) verbatim, in order. The three
    ///     modulator slots are told apart by what the synthesiser does with each, not by their
    ///     position alone:
    ///     <list type="bullet">
    ///     <item><see cref="PitchModulation"/> (<c>aClass209_2874</c>/<c>_2869</c>) is added to the
    ///     pitch envelope's output at <c>:189</c> - vibrato.</item>
    ///     <item><see cref="VolumeModulation"/> (<c>_2876</c>/<c>_2878</c>) scales the volume
    ///     envelope's output at <c>:195</c> - tremolo.</item>
    ///     <item><see cref="Gate"/> (<c>_2872</c>/<c>_2877</c>) drives the pass at <c>:209-235</c>
    ///     that zeroes alternating spans of the finished samples - a chopper, not a modulator of
    ///     either envelope.</item>
    ///     </list>
    ///     <para>
    ///     <b>CLIENT BUG, LATENT.</b> The client's harmonic arrays are length 5
    ///     (<c>Class344.java:71-74</c>) while the read loop runs to 10 (<c>:108-116</c>), so a sixth
    ///     partial throws <c>ArrayIndexOutOfBounds</c> in the 637 client - and the synthesiser only
    ///     ever reads five (<c>:173</c>, <c>:198</c>). This decoder follows the loop rather than the
    ///     arrays, because stopping at five would leave the stream in the middle of a record. The most
    ///     any shipped tone carries is 5, so it never fires; <see cref="ExceedsClientLimits"/> is what
    ///     an editor should check before letting anyone add one.
    ///     </para>
    /// </remarks>
    public sealed class SoundEffectTone {
        /// <summary>The most partials the format can express, because the read loop stops at ten.</summary>
        /// <remarks>
        ///     At exactly ten there is no terminating zero on the wire - the loop ends on its own
        ///     bound (<c>Class344.java:108</c>). Not reached in this cache, where the most any tone
        ///     carries is 5, so no shipped record exercises the terminator-less form.
        /// </remarks>
        public const int MaxHarmonics = 10;

        /// <summary>The most partials the 637 client can hold.</summary>
        /// <remarks>Its arrays are fixed at this width (<c>Class344.java:71-74</c>).</remarks>
        public const int ClientHarmonics = 5;

        /// <summary>The largest value the <c>u16</c> timing fields can carry.</summary>
        public const int MaxTimingField = 0xFFFF;

        /// <summary>
        ///     The pitch envelope. Its form byte is what tells the reader this tone exists.
        /// </summary>
        /// <remarks>
        ///     <c>Class37.java:30-35</c> peeks the byte, treats zero as an empty slot and rewinds on
        ///     anything else, so this envelope's form is load-bearing structure rather than only a
        ///     waveform choice. No shipped tone stores 0 here.
        /// </remarks>
        public SoundEffectEnvelope Pitch { get; set; } = new SoundEffectEnvelope();

        /// <summary>The volume envelope.</summary>
        /// <remarks>
        ///     Its form is free to be 0 and almost always is - 20,981 of the 20,990 tones in this
        ///     cache - because nothing peeks at it.
        /// </remarks>
        public SoundEffectEnvelope Volume { get; set; } = new SoundEffectEnvelope();

        /// <summary>Vibrato, or null when the tone has none.</summary>
        public SoundEffectModulator? PitchModulation { get; set; }

        /// <summary>Tremolo, or null when the tone has none.</summary>
        public SoundEffectModulator? VolumeModulation { get; set; }

        /// <summary>The chopper that blanks alternating spans, or null when the tone has none.</summary>
        public SoundEffectModulator? Gate { get; set; }

        /// <summary>The partials, in stream order.</summary>
        public List<SoundEffectHarmonic> Harmonics { get; } = new List<SoundEffectHarmonic>();

        /// <summary>The delay line's tap, in milliseconds.</summary>
        /// <remarks>
        ///     <c>Class344.anInt2882</c>, applied at <c>:236-241</c>. The line only runs when both this
        ///     and <see cref="DelayFeedback"/> are above zero.
        /// </remarks>
        public int DelayTime { get; set; }

        /// <summary>How much of the delayed signal is fed back, as a percentage.</summary>
        /// <remarks><c>Class344.anInt2868</c>, <c>:239</c> divides by 100.</remarks>
        public int DelayFeedback { get; set; }

        /// <summary>How long the tone sounds, in milliseconds.</summary>
        /// <remarks><c>Class344.anInt2870</c>. <c>Class37.java:89</c> turns it into a sample count at 22050 Hz.</remarks>
        public int Duration { get; set; }

        /// <summary>How far into the effect the tone starts, in milliseconds.</summary>
        /// <remarks>
        ///     <c>Class344.anInt2867</c>. <c>Class37.java:90,97</c> uses it as the write offset into
        ///     the mix, so tones overlap rather than queue.
        /// </remarks>
        public int Offset { get; set; }

        /// <summary>The filter the tone is run through. Never null; an absent filter is one zero byte.</summary>
        public SoundEffectFilter Filter { get; set; } = new SoundEffectFilter();

        /// <summary>Whether this tone is shaped in a way the 637 client cannot load.</summary>
        /// <remarks>False for every tone in this cache. See the type's remarks for the two arrays involved.</remarks>
        public bool ExceedsClientLimits =>
            Harmonics.Count > ClientHarmonics || Filter.ExceedsClientLimits;

        /// <summary>Decodes one tone.</summary>
        /// <remarks><c>Class344.method3820</c> (<c>Class344.java:78-124</c>).</remarks>
        /// <param name="stream">The stream, positioned at the pitch envelope's form byte.</param>
        /// <returns>This tone.</returns>
        /// <exception cref="ArgumentNullException"><paramref name="stream"/> is null.</exception>
        public SoundEffectTone Decode(JagStream stream) {
            if (stream == null)
                throw new ArgumentNullException(nameof(stream));

            Pitch = new SoundEffectEnvelope().Decode(stream);
            Volume = new SoundEffectEnvelope().Decode(stream);
            PitchModulation = DecodeModulator(stream);
            VolumeModulation = DecodeModulator(stream);
            Gate = DecodeModulator(stream);

            Harmonics.Clear();
            for (int i = 0; i < MaxHarmonics; i++) {
                int amplitude = stream.ReadUnsignedSmart();
                if (amplitude == 0)
                    break;

                Harmonics.Add(new SoundEffectHarmonic {
                    Amplitude = amplitude,
                    PitchOffset = stream.ReadSmart(),
                    Delay = stream.ReadUnsignedSmart()
                });
            }

            DelayTime = stream.ReadUnsignedSmart();
            DelayFeedback = stream.ReadUnsignedSmart();
            Duration = stream.ReadUnsignedShort();
            Offset = stream.ReadUnsignedShort();
            Filter = new SoundEffectFilter().Decode(stream);
            return this;
        }

        /// <summary>Writes this tone back.</summary>
        /// <remarks>
        ///     Every smart is written in its shortest form. That is not an assumption: across all
        ///     10,238 records this index stores 125,592 unsigned smarts and 31,311 signed ones, and
        ///     not one of them uses the two-byte form for a value the one-byte form could hold, so
        ///     shortest-form reproduces the file. The byte-identity sweep is what keeps that true.
        /// </remarks>
        /// <param name="stream">The stream to append to.</param>
        /// <param name="what">Which tone this is, for a failure message.</param>
        /// <exception cref="ArgumentNullException"><paramref name="stream"/> is null.</exception>
        /// <exception cref="InvalidOperationException">A field has been edited past what the format stores.</exception>
        public void Encode(JagStream stream, string what) {
            if (stream == null)
                throw new ArgumentNullException(nameof(stream));

            if (Pitch.Form == 0)
                throw new InvalidOperationException(
                    what + " has a pitch envelope with form 0, and that byte is what marks the tone " +
                    "as present, so the record would read back with this tone missing and every " +
                    "field after it shifted.");

            Pitch.Encode(stream, what + " pitch envelope");
            Volume.Encode(stream, what + " volume envelope");
            EncodeModulator(stream, PitchModulation, what + " pitch modulation");
            EncodeModulator(stream, VolumeModulation, what + " volume modulation");
            EncodeModulator(stream, Gate, what + " gate");

            if (Harmonics.Count > MaxHarmonics)
                throw new InvalidOperationException(
                    what + " has " + Harmonics.Count + " harmonics, and the read loop stops at " +
                    MaxHarmonics + ", so the rest would be read as the fields that follow them.");

            for (int i = 0; i < Harmonics.Count; i++) {
                SoundEffectHarmonic harmonic = Harmonics[i];
                if (harmonic.Amplitude <= 0)
                    throw new InvalidOperationException(
                        what + " harmonic " + i + " has amplitude " + harmonic.Amplitude +
                        ", and 0 is the list terminator, so the harmonics after it would be lost.");

                stream.WriteUnsignedSmart(harmonic.Amplitude);
                stream.WriteSmart(harmonic.PitchOffset);
                stream.WriteUnsignedSmart(harmonic.Delay);
            }

            //The terminator only exists below the loop's own bound. A full ten partials end the
            //list by running out of iterations, and writing a zero there would be read as the
            //delay time.
            if (Harmonics.Count < MaxHarmonics)
                stream.WriteUnsignedSmart(0);

            stream.WriteUnsignedSmart(DelayTime);
            stream.WriteUnsignedSmart(DelayFeedback);
            stream.WriteShort(Timing(Duration, what, "duration"));
            stream.WriteShort(Timing(Offset, what, "offset"));
            Filter.Encode(stream, what + " filter");
        }

        /// <summary>Reads an optional modulator pair, or consumes the zero byte that says there is none.</summary>
        /// <remarks>
        ///     <c>Class344.java:84-91</c>. The marker byte is consumed either way: the client reads it,
        ///     and only rewinds when it is non-zero, so the byte becomes the rate envelope's form.
        /// </remarks>
        /// <param name="stream">The stream, positioned at the marker.</param>
        /// <returns>The pair, or null.</returns>
        private static SoundEffectModulator? DecodeModulator(JagStream stream) {
            if (stream.PeekUnsignedByte() == 0) {
                stream.ReadUnsignedByte();
                return null;
            }

            return new SoundEffectModulator {
                Rate = new SoundEffectEnvelope().Decode(stream),
                Depth = new SoundEffectEnvelope().Decode(stream)
            };
        }

        /// <summary>Writes an optional modulator pair, or the zero byte that says there is none.</summary>
        /// <param name="stream">The stream to append to.</param>
        /// <param name="modulator">The pair, or null.</param>
        /// <param name="what">Which slot this is, for a failure message.</param>
        /// <exception cref="InvalidOperationException">The rate envelope's form would be read as an empty slot.</exception>
        private static void EncodeModulator(JagStream stream, SoundEffectModulator? modulator, string what) {
            if (modulator == null) {
                stream.WriteByte(0);
                return;
            }

            if (modulator.Rate.Form == 0)
                throw new InvalidOperationException(
                    what + " has a rate envelope with form 0, and that byte is what marks the pair as " +
                    "present, so the record would read back with this slot empty and every field " +
                    "after it shifted.");

            modulator.Rate.Encode(stream, what + " rate envelope");
            modulator.Depth.Encode(stream, what + " depth envelope");
        }

        private static int Timing(int value, string what, string field) {
            if (value < 0 || value > MaxTimingField)
                throw new InvalidOperationException(
                    what + " has " + field + " " + value + " ms, and the field is an unsigned short, so " +
                    "0 to " + MaxTimingField + " fit.");
            return value;
        }
    }
}
