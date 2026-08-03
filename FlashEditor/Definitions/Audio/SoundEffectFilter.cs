using System;

namespace FlashEditor.Definitions.Audio {
    /// <summary>
    ///     The biquad cascade a tone is run through, and the envelope that sweeps it.
    /// </summary>
    /// <remarks>
    ///     <c>Class182.method2612</c> (<c>Class182.java:35-67</c>). Two independent sets of poles,
    ///     each described at two phases and interpolated between them:
    ///     <list type="bullet">
    ///     <item>Set <b>0</b> is the feed-forward half. <c>Class344.java:255-257</c> accumulates its
    ///     coefficients over the <i>input</i> history with <c>+=</c>.</item>
    ///     <item>Set <b>1</b> is the feedback half, subtracted from the <i>output</i> history at
    ///     <c>:259-261</c>.</item>
    ///     </list>
    ///     Phase 0 is the filter at the start of the tone and phase 1 at the end;
    ///     <c>method2613</c> (<c>:69-106</c>) interpolates every field between them by a fraction the
    ///     <see cref="Sweep"/> envelope produces.
    ///     <para>
    ///     <b>CLIENT BUG, LATENT.</b> The client's coefficient arrays are <c>[2][2][4]</c>
    ///     (<c>Class182.java:25-27</c>) while the pole count comes off a nibble and can reach 15, so a
    ///     five-pole set would throw <c>ArrayIndexOutOfBounds</c> in the 637 client. This decoder sizes
    ///     by the declared count instead, because truncating at 4 would silently mis-parse the rest of
    ///     the record rather than reproduce the crash. Nothing in this cache reaches it: the most
    ///     poles any shipped set declares is 4.
    ///     </para>
    /// </remarks>
    public sealed class SoundEffectFilter {
        /// <summary>Coefficient sets: feed-forward and feedback.</summary>
        public const int Sets = 2;

        /// <summary>Phases each set is described at: the start of the tone and the end.</summary>
        public const int Phases = 2;

        /// <summary>The most poles a set can declare, because the count is a nibble.</summary>
        public const int MaxPolesPerSet = 15;

        /// <summary>
        ///     The most poles per set the 637 client can actually hold.
        /// </summary>
        /// <remarks>
        ///     Its arrays are fixed at this width (<c>Class182.java:25-27</c>). Writing more produces
        ///     a well-formed file that crashes the client, so an editor should refuse it - which is a
        ///     policy decision and not the codec's, so nothing here enforces it.
        ///     <see cref="ExceedsClientLimits"/> reports it instead.
        /// </remarks>
        public const int ClientPolesPerSet = 4;

        /// <summary>The largest value the <c>u16</c> coefficient and gain fields can carry.</summary>
        public const int MaxField = 0xFFFF;

        private readonly int[] poleCounts = new int[Sets];
        private readonly int[] gains = new int[Sets];
        private int[][][] frequencies = Empty();
        private int[][][] ranges = Empty();

        /// <summary>How many poles a set declares.</summary>
        /// <remarks>
        ///     The packed byte is <c>(set 0 &lt;&lt; 4) | set 1</c> (<c>Class182.java:37-39</c>), so
        ///     set 0 is the <b>high</b> nibble. Getting that round the wrong way is undetectable on
        ///     the 21,485 tones whose sets are both empty and wrong everywhere else.
        /// </remarks>
        /// <param name="set">0 for feed-forward, 1 for feedback.</param>
        /// <returns>The pole count.</returns>
        public int PoleCount(int set) => poleCounts[Check(set, Sets, nameof(set))];

        /// <summary>Whether the filter is present at all.</summary>
        /// <remarks>
        ///     The whole block is one byte when both nibbles are zero (<c>Class182.java:40,64-66</c>),
        ///     and that is the common case - 7106 of the 20,990 tones in this cache carry no filter.
        /// </remarks>
        public bool IsPresent => poleCounts[0] != 0 || poleCounts[1] != 0;

        /// <summary>The overall gain at a phase, in 1/3.2768 dB of attenuation.</summary>
        /// <remarks>
        ///     <c>Class182.anIntArray1440</c>. <c>method2613</c> (<c>:72-75</c>) interpolates the pair,
        ///     scales by 0.0030517578 (which is <c>1/327.68</c>) and raises 0.1 to a twentieth of it,
        ///     so the stored number is decibels times 327.68 divided by 100.
        /// </remarks>
        /// <param name="phase">0 for the start of the tone, 1 for the end.</param>
        /// <returns>The gain.</returns>
        public int Gain(int phase) => gains[Check(phase, Phases, nameof(phase))];

        /// <summary>Sets the gain at a phase.</summary>
        /// <remarks>
        ///     Editing the two to be equal while <see cref="InterpolationMask"/> is zero <b>deletes</b>
        ///     <see cref="Sweep"/> from the file, because that pair of conditions is exactly what the
        ///     format uses to say the envelope is absent (<c>Class182.java:61-63</c>). It is the one
        ///     place on this index where changing a value changes the record's length.
        /// </remarks>
        /// <param name="phase">0 for the start of the tone, 1 for the end.</param>
        /// <param name="value">The gain.</param>
        public void SetGain(int phase, int value) => gains[Check(phase, Phases, nameof(phase))] = value;

        /// <summary>
        ///     Which poles store a distinct phase-1 coefficient pair, bit <c>set * 4 + pole</c>.
        /// </summary>
        /// <remarks>
        ///     Kept as the stored byte rather than recomputed from whether the two phases differ.
        ///     Recomputing is lossy in both directions: a set bit whose two phases happen to hold the
        ///     same numbers would be dropped, and the byte can carry bits above the declared pole
        ///     count, which the reader never looks at (<c>Class182.java:52</c>) and an encoder would
        ///     have no way to reinvent. No shipped filter sets a bit past its pole count, so that half
        ///     is latent - but the byte also decides whether <see cref="Sweep"/> is written, so it has
        ///     to survive verbatim regardless.
        /// </remarks>
        public int InterpolationMask { get; set; }

        /// <summary>
        ///     The envelope that sweeps the filter from phase 0 to phase 1, or null when the format
        ///     leaves it out.
        /// </summary>
        /// <remarks>
        ///     Present iff <c>mask != 0 || gain1 != gain0</c> (<c>Class182.java:61-63</c>), so its
        ///     presence is <b>derived</b> and not stored - which is why this is not accompanied by a
        ///     flag that could contradict it. Shape only: it is read by <c>method2772</c>, so its form
        ///     and range are not on the wire. 12,874 of the 13,884 filters in this cache have one.
        /// </remarks>
        public SoundEffectEnvelope? Sweep { get; set; }

        /// <summary>The centre frequency of one pole, before scaling.</summary>
        /// <remarks>
        ///     <c>Class182.anIntArrayArrayArray1433</c>. <c>method2615</c> (<c>:108-114</c>) scales by
        ///     1.2207031E-4 (<c>1/8192</c>) into octaves above 32.703 Hz, so the stored number is
        ///     8192 octaves.
        /// </remarks>
        /// <param name="set">0 for feed-forward, 1 for feedback.</param>
        /// <param name="phase">0 for the start of the tone, 1 for the end.</param>
        /// <param name="pole">The pole within the set.</param>
        /// <returns>The stored frequency.</returns>
        public int Frequency(int set, int phase, int pole) => Coefficient(frequencies, set, phase, pole);

        /// <summary>The resonance of one pole, before scaling.</summary>
        /// <remarks>
        ///     <c>Class182.anIntArrayArrayArray1434</c>. <c>method2617</c> (<c>:116-122</c>) scales by
        ///     0.0015258789 (<c>1/655.36</c>) into decibels and folds it to <c>1 - 10^(-dB/20)</c>, the
        ///     pole radius.
        /// </remarks>
        /// <param name="set">0 for feed-forward, 1 for feedback.</param>
        /// <param name="phase">0 for the start of the tone, 1 for the end.</param>
        /// <param name="pole">The pole within the set.</param>
        /// <returns>The stored range.</returns>
        public int Range(int set, int phase, int pole) => Coefficient(ranges, set, phase, pole);

        /// <summary>Sets a pole's centre frequency.</summary>
        /// <param name="set">0 for feed-forward, 1 for feedback.</param>
        /// <param name="phase">0 for the start of the tone, 1 for the end.</param>
        /// <param name="pole">The pole within the set.</param>
        /// <param name="value">The stored frequency.</param>
        public void SetFrequency(int set, int phase, int pole, int value) {
            frequencies[Check(set, Sets, nameof(set))][Check(phase, Phases, nameof(phase))]
                [Check(pole, poleCounts[set], nameof(pole))] = value;
        }

        /// <summary>Sets a pole's resonance.</summary>
        /// <param name="set">0 for feed-forward, 1 for feedback.</param>
        /// <param name="phase">0 for the start of the tone, 1 for the end.</param>
        /// <param name="pole">The pole within the set.</param>
        /// <param name="value">The stored range.</param>
        public void SetRange(int set, int phase, int pole, int value) {
            ranges[Check(set, Sets, nameof(set))][Check(phase, Phases, nameof(phase))]
                [Check(pole, poleCounts[set], nameof(pole))] = value;
        }

        /// <summary>Whether this filter is shaped in a way the 637 client cannot load.</summary>
        /// <remarks>
        ///     False for every filter in this cache. A tab that lets someone add a pole should consult
        ///     it before saving, because the file it would write is well formed and still crashes the
        ///     client.
        /// </remarks>
        public bool ExceedsClientLimits =>
            poleCounts[0] > ClientPolesPerSet || poleCounts[1] > ClientPolesPerSet;

        /// <summary>Resizes a set, discarding any coefficients past the new count.</summary>
        /// <param name="set">0 for feed-forward, 1 for feedback.</param>
        /// <param name="poles">The new pole count, 0 to <see cref="MaxPolesPerSet"/>.</param>
        /// <exception cref="ArgumentOutOfRangeException">The count does not fit a nibble.</exception>
        public void SetPoleCount(int set, int poles) {
            Check(set, Sets, nameof(set));
            if (poles < 0 || poles > MaxPolesPerSet)
                throw new ArgumentOutOfRangeException(nameof(poles), poles,
                    "A pole count is stored in a nibble, so 0 to " + MaxPolesPerSet + " fit.");

            poleCounts[set] = poles;
            for (int phase = 0; phase < Phases; phase++) {
                Array.Resize(ref frequencies[set][phase], poles);
                Array.Resize(ref ranges[set][phase], poles);
            }
        }

        /// <summary>Decodes one filter block.</summary>
        /// <remarks>
        ///     <c>Class182.method2612</c> (<c>Class182.java:35-67</c>) in order. The second pass over
        ///     the poles copies phase 0 into phase 1 for every pole the mask leaves clear
        ///     (<c>:55-58</c>), so both phases are always populated here even though only some of them
        ///     were on the wire - which is what lets the encoder write back exactly the ones that were.
        /// </remarks>
        /// <param name="stream">The stream, positioned at the packed pole-count byte.</param>
        /// <returns>This filter.</returns>
        /// <exception cref="ArgumentNullException"><paramref name="stream"/> is null.</exception>
        public SoundEffectFilter Decode(JagStream stream) {
            if (stream == null)
                throw new ArgumentNullException(nameof(stream));

            int packed = stream.ReadUnsignedByte();
            frequencies = Empty();
            ranges = Empty();
            SetPoleCount(0, packed >> 4);
            SetPoleCount(1, packed & 0xF);
            InterpolationMask = 0;
            Sweep = null;

            if (packed == 0) {
                //Both gains are cleared rather than left alone, matching Class182.java:65.
                gains[0] = 0;
                gains[1] = 0;
                return this;
            }

            gains[0] = stream.ReadUnsignedShort();
            gains[1] = stream.ReadUnsignedShort();
            InterpolationMask = stream.ReadUnsignedByte();

            for (int set = 0; set < Sets; set++) {
                for (int pole = 0; pole < poleCounts[set]; pole++) {
                    frequencies[set][0][pole] = stream.ReadUnsignedShort();
                    ranges[set][0][pole] = stream.ReadUnsignedShort();
                }
            }

            for (int set = 0; set < Sets; set++) {
                for (int pole = 0; pole < poleCounts[set]; pole++) {
                    if (IsInterpolated(set, pole)) {
                        frequencies[set][1][pole] = stream.ReadUnsignedShort();
                        ranges[set][1][pole] = stream.ReadUnsignedShort();
                    } else {
                        frequencies[set][1][pole] = frequencies[set][0][pole];
                        ranges[set][1][pole] = ranges[set][0][pole];
                    }
                }
            }

            if (HasSweep)
                Sweep = new SoundEffectEnvelope().DecodeShape(stream);

            return this;
        }

        /// <summary>Writes this filter back.</summary>
        /// <param name="stream">The stream to append to.</param>
        /// <param name="what">What this filter belongs to, for a failure message.</param>
        /// <exception cref="ArgumentNullException"><paramref name="stream"/> is null.</exception>
        /// <exception cref="InvalidOperationException">
        ///     A field has been edited past what the format stores, or <see cref="Sweep"/> disagrees
        ///     with the mask and gains about whether it exists.
        /// </exception>
        public void Encode(JagStream stream, string what) {
            if (stream == null)
                throw new ArgumentNullException(nameof(stream));

            int packed = (poleCounts[0] << 4) | poleCounts[1];
            stream.WriteByte(packed);
            if (packed == 0) {
                //One byte and nothing else, so an absent filter cannot smuggle a gain or a sweep
                //into the file where the client would never look for one.
                if (Sweep != null)
                    throw new InvalidOperationException(
                        what + " declares no poles, so the format has nowhere to put its sweep envelope.");
                return;
            }

            if (InterpolationMask < 0 || InterpolationMask > 0xFF)
                throw new InvalidOperationException(
                    what + " has interpolation mask " + InterpolationMask + ", which is a single byte.");

            stream.WriteShort(Field(gains[0], what, "gain at phase 0"));
            stream.WriteShort(Field(gains[1], what, "gain at phase 1"));
            stream.WriteByte(InterpolationMask);

            for (int set = 0; set < Sets; set++) {
                for (int pole = 0; pole < poleCounts[set]; pole++) {
                    stream.WriteShort(Field(frequencies[set][0][pole], what, "set " + set + " pole " + pole + " frequency"));
                    stream.WriteShort(Field(ranges[set][0][pole], what, "set " + set + " pole " + pole + " range"));
                }
            }

            for (int set = 0; set < Sets; set++) {
                for (int pole = 0; pole < poleCounts[set]; pole++) {
                    if (!IsInterpolated(set, pole))
                        continue;
                    stream.WriteShort(Field(frequencies[set][1][pole], what, "set " + set + " pole " + pole + " swept frequency"));
                    stream.WriteShort(Field(ranges[set][1][pole], what, "set " + set + " pole " + pole + " swept range"));
                }
            }

            if (!HasSweep) {
                if (Sweep != null)
                    throw new InvalidOperationException(
                        what + " carries a sweep envelope, but its mask is zero and its two gains are " +
                        "equal, which is how the format says there is none. Give the gains different " +
                        "values or set a mask bit, or drop the envelope.");
                return;
            }

            if (Sweep == null)
                throw new InvalidOperationException(
                    what + " has a non-zero mask or unequal gains, which is how the format says a " +
                    "sweep envelope follows, but it has none.");

            Sweep.EncodeShape(stream, what + " sweep");
        }

        /// <summary>Whether the format stores a sweep envelope for this filter.</summary>
        /// <remarks><c>Class182.java:61</c>. Derived, never stored.</remarks>
        private bool HasSweep => InterpolationMask != 0 || gains[1] != gains[0];

        /// <summary>Whether a pole's phase-1 pair is on the wire rather than copied from phase 0.</summary>
        /// <remarks>
        ///     <c>Class182.java:52</c> spells the bit as <c>1 &lt;&lt; set * 4 &lt;&lt; pole</c>, which
        ///     is <c>1 &lt;&lt; (set * 4 + pole)</c>. A set may declare more than four poles, at which
        ///     point the bits of the two sets would collide - the client cannot reach that because its
        ///     arrays stop at four, so the shifted form is kept verbatim rather than widened to a
        ///     scheme the client does not implement.
        /// </remarks>
        /// <param name="set">0 for feed-forward, 1 for feedback.</param>
        /// <param name="pole">The pole within the set.</param>
        /// <returns>Whether the mask bit is set.</returns>
        private bool IsInterpolated(int set, int pole) => (InterpolationMask & (1 << (set * 4 + pole))) != 0;

        private int Coefficient(int[][][] table, int set, int phase, int pole) {
            return table[Check(set, Sets, nameof(set))][Check(phase, Phases, nameof(phase))]
                [Check(pole, poleCounts[set], nameof(pole))];
        }

        private static int[][][] Empty() {
            var table = new int[Sets][][];
            for (int set = 0; set < Sets; set++) {
                table[set] = new int[Phases][];
                for (int phase = 0; phase < Phases; phase++)
                    table[set][phase] = Array.Empty<int>();
            }
            return table;
        }

        private static int Check(int value, int limit, string name) {
            if (value >= 0 && value < limit)
                return value;

            throw new ArgumentOutOfRangeException(name, value, limit == 0
                ? "This filter set declares no poles, so there is nothing to address."
                : "Must be 0 to " + (limit - 1) + ".");
        }

        private static int Field(int value, string what, string field) {
            if (value < 0 || value > MaxField)
                throw new InvalidOperationException(
                    what + " has " + field + " " + value + ", and the field is an unsigned short, so 0 to " +
                    MaxField + " fit.");
            return value;
        }
    }
}
