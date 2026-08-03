using System;
using System.Collections.Generic;

namespace FlashEditor.Definitions.Audio {
    /// <summary>
    ///     One breakpoint of an envelope: where in the tone it lands, and the value it reaches.
    /// </summary>
    /// <remarks>
    ///     <see cref="Position"/> is an <b>absolute</b> fraction of the tone, not a segment length.
    ///     <c>Class209.method2770</c> (<c>Class209.java:44-47</c>) turns it into a sample index with
    ///     <c>position / 65536.0 * sampleCount</c> and compares it against a counter that is never
    ///     reset between segments, so a list of positions must ascend to mean anything. Reading them
    ///     as durations produces an envelope that is right only for the first breakpoint.
    /// </remarks>
    public sealed class SoundEffectEnvelopeSegment {
        /// <summary>Where the breakpoint falls, as a fraction of the tone in 1/65536ths.</summary>
        public int Position { get; set; }

        /// <summary>The value the envelope reaches at that point, 0 to 65535.</summary>
        public int Value { get; set; }
    }

    /// <summary>
    ///     A sound-effect envelope: a waveform selector, a value range, and a breakpoint list the
    ///     synthesiser interpolates between.
    /// </summary>
    /// <remarks>
    ///     <c>Class209</c> in the 637 client. It has two readers and both are used on this index, so
    ///     the type carries both rather than one with a flag:
    ///     <list type="bullet">
    ///     <item><c>method2771</c> (<c>Class209.java:54-60</c>) reads the whole thing - form, start,
    ///     end, then the shape. This is <see cref="Decode"/>.</item>
    ///     <item><c>method2772</c> (<c>:62-71</c>) reads the shape alone. The filter's trailing
    ///     interpolation envelope is read this way (<c>Class182.java:62</c>), so its
    ///     <see cref="Form"/>, <see cref="Start"/> and <see cref="End"/> are never on the wire and
    ///     never mean anything. This is <see cref="DecodeShape"/>.</item>
    ///     </list>
    ///     <para>
    ///     <b><see cref="Form"/> 0 is a legal stored value and is the common case</b> - 20,981 of the
    ///     20,990 volume envelopes in this cache carry it, and <c>Class344.method3821</c>
    ///     (<c>:126-144</c>) answers 0 for anything outside 1 to 4, so it means "silent". It is
    ///     nonetheless forbidden on any envelope that doubles as a presence marker, because the
    ///     reader tests that byte to decide whether the record even has the thing;
    ///     <see cref="SoundEffectTone"/> is where that is enforced.
    ///     </para>
    /// </remarks>
    public sealed class SoundEffectEnvelope {
        /// <summary>The most breakpoints an envelope can hold, because the count is a single byte.</summary>
        /// <remarks>Not reached in this cache: the longest shipped envelope has 99.</remarks>
        public const int MaxSegments = 255;

        /// <summary>The largest value the <c>u16</c> breakpoint fields can carry.</summary>
        public const int MaxSegmentField = 0xFFFF;

        /// <summary>
        ///     Which waveform the synthesiser drives from this envelope.
        /// </summary>
        /// <remarks>
        ///     <c>Class344.method3821</c> (<c>:126-144</c>): 1 square, 2 sine, 3 saw, 4 noise, and
        ///     anything else silent. Both 0 and 5 occur in this cache and both fall through to the
        ///     silent branch, which is why this is the raw stored byte rather than an enum - an enum
        ///     would have to invent a name for 5, and there are two of them.
        /// </remarks>
        public int Form { get; set; }

        /// <summary>The frequency the envelope starts at, in Hz.</summary>
        /// <remarks>
        ///     <c>Class209.anInt1587</c>. The unit follows from <c>Class344.java:161-162</c>: the
        ///     per-sample phase increment is <c>start * 32.768 / (samples per ms)</c> and the phase
        ///     wraps at 32768, which reduces to exactly <c>start</c> cycles per second at any sample
        ///     rate. Signed on the wire and negative in real records.
        /// </remarks>
        public int Start { get; set; }

        /// <summary>The frequency the envelope sweeps to, in Hz.</summary>
        /// <remarks><c>Class209.anInt1583</c>. Same unit as <see cref="Start"/>.</remarks>
        public int End { get; set; }

        /// <summary>The breakpoints, in stream order.</summary>
        public List<SoundEffectEnvelopeSegment> Segments { get; } = new List<SoundEffectEnvelopeSegment>();

        /// <summary>Decodes a full envelope - form, range and shape.</summary>
        /// <remarks><c>Class209.method2771</c> (<c>Class209.java:54-60</c>).</remarks>
        /// <param name="stream">The stream, positioned at the form byte.</param>
        /// <returns>This envelope.</returns>
        /// <exception cref="ArgumentNullException"><paramref name="stream"/> is null.</exception>
        public SoundEffectEnvelope Decode(JagStream stream) {
            if (stream == null)
                throw new ArgumentNullException(nameof(stream));

            Form = stream.ReadUnsignedByte();
            Start = stream.ReadInt();
            End = stream.ReadInt();
            return DecodeShape(stream);
        }

        /// <summary>Decodes the breakpoint list alone, leaving form and range untouched.</summary>
        /// <remarks>
        ///     <c>Class209.method2772</c> (<c>Class209.java:62-71</c>). The filter's interpolation
        ///     envelope is stored this way and nothing else on this index is.
        /// </remarks>
        /// <param name="stream">The stream, positioned at the breakpoint count.</param>
        /// <returns>This envelope.</returns>
        /// <exception cref="ArgumentNullException"><paramref name="stream"/> is null.</exception>
        public SoundEffectEnvelope DecodeShape(JagStream stream) {
            if (stream == null)
                throw new ArgumentNullException(nameof(stream));

            Segments.Clear();
            int count = stream.ReadUnsignedByte();
            for (int i = 0; i < count; i++) {
                Segments.Add(new SoundEffectEnvelopeSegment {
                    Position = stream.ReadUnsignedShort(),
                    Value = stream.ReadUnsignedShort()
                });
            }

            return this;
        }

        /// <summary>Writes a full envelope back.</summary>
        /// <param name="stream">The stream to append to.</param>
        /// <param name="what">What this envelope is, for a failure message.</param>
        /// <exception cref="ArgumentNullException"><paramref name="stream"/> is null.</exception>
        /// <exception cref="InvalidOperationException">A field has been edited past what the format stores.</exception>
        public void Encode(JagStream stream, string what) {
            if (stream == null)
                throw new ArgumentNullException(nameof(stream));

            if (Form < 0 || Form > 0xFF)
                throw new InvalidOperationException(
                    what + " has form " + Form + ", which is stored as a single byte.");

            stream.WriteByte(Form);
            stream.WriteInteger(Start);
            stream.WriteInteger(End);
            EncodeShape(stream, what);
        }

        /// <summary>Writes the breakpoint list alone.</summary>
        /// <param name="stream">The stream to append to.</param>
        /// <param name="what">What this envelope is, for a failure message.</param>
        /// <exception cref="ArgumentNullException"><paramref name="stream"/> is null.</exception>
        /// <exception cref="InvalidOperationException">A field has been edited past what the format stores.</exception>
        public void EncodeShape(JagStream stream, string what) {
            if (stream == null)
                throw new ArgumentNullException(nameof(stream));

            if (Segments.Count > MaxSegments)
                throw new InvalidOperationException(
                    what + " has " + Segments.Count + " breakpoints, and the count is stored as a " +
                    "single byte, so at most " + MaxSegments + " fit.");

            stream.WriteByte(Segments.Count);
            for (int i = 0; i < Segments.Count; i++) {
                SoundEffectEnvelopeSegment segment = Segments[i];
                stream.WriteShort(Field(segment.Position, what, "position", i));
                stream.WriteShort(Field(segment.Value, what, "value", i));
            }
        }

        /// <summary>Rejects a breakpoint field an edit has pushed outside the stored width.</summary>
        /// <remarks>
        ///     Reported rather than masked. A silently truncated 65,536 writes a 0, which moves a
        ///     breakpoint to the start of the tone - a change no sweep over unedited data can see.
        /// </remarks>
        /// <param name="value">The value to write.</param>
        /// <param name="what">What this envelope is.</param>
        /// <param name="field">Which of the pair it is.</param>
        /// <param name="index">The breakpoint's position in the list.</param>
        /// <returns>The value, when it fits.</returns>
        /// <exception cref="InvalidOperationException">It does not fit.</exception>
        private static int Field(int value, string what, string field, int index) {
            if (value < 0 || value > MaxSegmentField)
                throw new InvalidOperationException(
                    what + " breakpoint " + index + " has " + field + " " + value +
                    ", and the field is an unsigned short, so 0 to " + MaxSegmentField + " fit.");
            return value;
        }
    }
}
