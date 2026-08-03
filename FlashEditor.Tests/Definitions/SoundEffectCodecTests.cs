using System;
using System.Linq;
using FlashEditor.Definitions.Audio;
using Xunit;

namespace FlashEditor.Tests.Definitions
{
    /// <summary>
    ///     Pins the index-4 sound-effect codec against bytes it did not produce.
    /// </summary>
    /// <remarks>
    ///     Round-tripping this encoder against this decoder proves nothing, so every record here is a
    ///     verbatim capture from the 639 cache and every expected value was read out of those bytes by
    ///     an independent walk of the 637 client's reader, not by this decoder.
    ///     <c>RealCacheSoundEffectTests.TheCapturedFixtures_AreStillWhatTheCacheStores</c> asserts the
    ///     captures are still what the cache holds.
    ///     <para>
    ///     The four were chosen to cover the branches a sweep cannot single out. Effect 3384 is the
    ///     minimum record; 160 is the plain two-tone case; 5 has a gap in its tone slots and a filter
    ///     whose sweep envelope exists only because its two gains differ; 4787 is the orphan group the
    ///     reference table does not declare, and carries the other filter arm - a non-zero
    ///     interpolation mask with equal gains.
    ///     </para>
    /// </remarks>
    public class SoundEffectCodecTests
    {
        /// <summary>Effect 3384: ten empty slots and a zero loop window, the shortest legal record.</summary>
        /// <remarks>
        ///     14 bytes. It exists to stop anyone assuming a record has at least one tone - two
        ///     effects in this cache have none, and a decoder that read a tone unconditionally would
        ///     consume the loop window as an envelope header.
        /// </remarks>
        public const int EmptyEffectId = 3384;

        private const string EmptyEffectHex = "0000000000000000000000000000";

        /// <summary>Effect 160: two tones in slots 0 and 1, three partials between them, no filter.</summary>
        public const int PlainEffectId = 160;

        private const string PlainEffectHex =
            "0400000050000001900400000000875d4dd3dfbcf3b7ffffffff0000000000000000640500000000483d0b448e60" +
            "2d0fd4833334ffff39590000005a400050c0780000006403fc000000040000001400000bb803000027f069e80313" +
            "ffff0313000000000000000064040000851f28ad54fead3d072cffff000000000050400000006401f403fc000000" +
            "00000000000003fc04c4";

        /// <summary>Effect 5: tones in slots 0, 4 and 6, and a filter whose sweep hangs off its gains.</summary>
        /// <remarks>
        ///     The gap is the point. 1884 effects in this cache leave a slot empty in the middle, and
        ///     an encoder that compacted them would produce a file of the same length with every tone
        ///     starting at a different millisecond.
        /// </remarks>
        public const int GappedEffectId = 5;

        private const string GappedEffectHex =
            "0200000032000003e8040000b22e0001ffff9c67ffffffffffff00000000000000006405000000002b7c0a3e6881" +
            "0b44c7e30313ffff00000400000000000000850200008000ffff800000000000000000006403000013757fff31aa" +
            "ffff395900003c400032c0b40000010007d00000000000000200000032000003e804000073b71edcb22e9e82b43a" +
            "ffff8d50000000000000000064050000020d376809387ef21db3d2690419ffff0000040000000000000085020000" +
            "8000ffff800000000000000000006403000013757fff31aaffff395900003c400032c0b40000010005dc00000000" +
            "0400000032000003e803000008327956051fffff00000000000000000000640500000000413a0c4a7038051fa907" +
            "020dffff000004000000000000000a05000080003fff80007ffe8000bffd8000ffff80000000000000000000" +
            "6405000080003fff80007ffe8000bffd8000ffff800004000000000000003205000080003fff80007ffe8000bffd" +
            "8000ffff8000000000000000000064050000ffff36b4ffff78a2ffffc2fbffffffffffff000540000000640fa000" +
            "00019ba50000002043570a020000c396ffffe87300000000000000";

        /// <summary>Effect 4787: the group idx4 holds and the reference table does not declare.</summary>
        /// <remarks>
        ///     Kept as a codec fixture rather than only as a curiosity, because it is the one shipped
        ///     record that puts a non-zero interpolation mask together with two equal gains - the
        ///     other half of the condition that decides whether a filter stores a sweep envelope.
        /// </remarks>
        public const int OrphanEffectId = 4787;

        private const string OrphanEffectHex =
            "0400000032000003e80200000000ffffffff0000000000000000640600000000426b0a1f5eca15407b283dba7e9d" +
            "0b22ffff0000000001000000000000000105000080003fff80007ffe8000bffd8000ffff80000000000000000000" +
            "6405000080003fff80007ffe8000bffd8000ffff8000808c4000000064025800961200000000300bc31bde199910" +
            "84f9c5153fe02c4feff9c52f8e0500000000341130247189fffff017c3d5ffffbc2c00000000000000000000000000";

        /// <summary>The captured bytes for one of the fixtures.</summary>
        /// <param name="effectId">One of the fixture ids declared here.</param>
        /// <returns>A fresh copy of the stored file.</returns>
        /// <exception cref="ArgumentOutOfRangeException">No fixture was captured for that id.</exception>
        public static byte[] CapturedBytes(int effectId)
        {
            string hex = effectId switch
            {
                EmptyEffectId => EmptyEffectHex,
                PlainEffectId => PlainEffectHex,
                GappedEffectId => GappedEffectHex,
                OrphanEffectId => OrphanEffectHex,
                _ => throw new ArgumentOutOfRangeException(nameof(effectId), effectId,
                    "No index-4 fixture was captured for that id.")
            };

            return Convert.FromHexString(hex);
        }

        /// <summary>Every fixture id, for a test that wants all of them.</summary>
        public static TheoryData<int> CapturedEffectIds() =>
            new TheoryData<int> { EmptyEffectId, PlainEffectId, GappedEffectId, OrphanEffectId };

        /// <summary>An effect with no tones is ten zero bytes and a loop window, and nothing else.</summary>
        [Fact]
        public void AnEffectWithNoTones_IsTenMarkersAndALoopWindow()
        {
            byte[] bytes = CapturedBytes(EmptyEffectId);
            var stream = new JagStream(bytes);
            var effect = new SoundEffectDefinition { Id = EmptyEffectId }.Decode(stream);

            Assert.Equal(14, bytes.Length);
            Assert.Equal(bytes.Length, stream.Position);
            Assert.Equal(0, effect.ToneCount);
            Assert.All(effect.Tones, tone => Assert.Null(tone));
            Assert.Equal(0, effect.LoopStart);
            Assert.Equal(0, effect.LoopEnd);
            Assert.False(effect.Loops);
        }

        /// <summary>
        ///     A plain two-tone effect decodes into the fields the client reads, in the client's order.
        /// </summary>
        /// <remarks>
        ///     The values are the check on the read order. Everything in this format is positional, so
        ///     a decoder that swapped the two <c>int32</c>s of an envelope or read the harmonic triple
        ///     in the wrong order still consumes exactly 148 bytes and produces a different patch.
        ///     Tone 0's second partial is 120 tenths of a semitone above the first, which is one octave
        ///     - the constant at <c>Class344.java:179</c> is 2^(1/120) - so a signed smart read as an
        ///     unsigned one would show here as a wildly detuned partial rather than as a length error.
        /// </remarks>
        [Fact]
        public void ACapturedEffect_DecodesIntoTheClientsFieldOrder()
        {
            byte[] bytes = CapturedBytes(PlainEffectId);
            var stream = new JagStream(bytes);
            var effect = new SoundEffectDefinition { Id = PlainEffectId }.Decode(stream);

            Assert.Equal(bytes.Length, stream.Position);
            Assert.Equal(2, effect.ToneCount);
            Assert.Equal(new[] { 0, 1 }, effect.OccupiedSlots);
            Assert.Equal(1020, effect.LoopStart);
            Assert.Equal(1220, effect.LoopEnd);
            Assert.True(effect.Loops);

            SoundEffectTone first = effect.Tones[0]!;
            Assert.Equal(4, first.Pitch.Form);
            Assert.Equal(80, first.Pitch.Start);
            Assert.Equal(400, first.Pitch.End);
            Assert.Equal(4, first.Pitch.Segments.Count);
            Assert.Equal(34653, first.Pitch.Segments[1].Position);
            Assert.Equal(19923, first.Pitch.Segments[1].Value);

            //The volume envelope's form is 0 and that is legal - only an envelope that doubles as a
            //presence marker is forbidden from carrying it.
            Assert.Equal(0, first.Volume.Form);
            Assert.Equal(0, first.Volume.Start);
            Assert.Equal(100, first.Volume.End);
            Assert.Equal(5, first.Volume.Segments.Count);

            Assert.Null(first.PitchModulation);
            Assert.Null(first.VolumeModulation);
            Assert.Null(first.Gate);

            Assert.Equal(2, first.Harmonics.Count);
            Assert.Equal(90, first.Harmonics[0].Amplitude);
            Assert.Equal(0, first.Harmonics[0].PitchOffset);
            Assert.Equal(0, first.Harmonics[0].Delay);
            Assert.Equal(80, first.Harmonics[1].Amplitude);
            Assert.Equal(120, first.Harmonics[1].PitchOffset);

            Assert.Equal(0, first.DelayTime);
            Assert.Equal(100, first.DelayFeedback);
            Assert.Equal(1020, first.Duration);
            Assert.Equal(0, first.Offset);
            Assert.False(first.Filter.IsPresent);

            //The second tone starts where the first ends, which is what the mix offset is for.
            SoundEffectTone second = effect.Tones[1]!;
            Assert.Equal(1020, second.Offset);
            Assert.Equal(500, second.Duration);
            Assert.Single(second.Harmonics);
        }

        /// <summary>
        ///     A tone in slot 4 stays in slot 4.
        /// </summary>
        /// <remarks>
        ///     The whole reason <see cref="SoundEffectDefinition.Tones"/> is a fixed array rather than
        ///     a list. An empty slot is one byte on the wire, so compacting three tones from slots
        ///     0, 4, 6 into 0, 1, 2 writes a file of exactly the same length whose tones sound at
        ///     different times - a change a length check cannot see.
        /// </remarks>
        [Fact]
        public void ACapturedEffect_KeepsItsTonesInTheSlotsTheyWereStoredIn()
        {
            byte[] bytes = CapturedBytes(GappedEffectId);
            var stream = new JagStream(bytes);
            var effect = new SoundEffectDefinition { Id = GappedEffectId }.Decode(stream);

            Assert.Equal(bytes.Length, stream.Position);
            Assert.Equal(new[] { 0, 4, 6 }, effect.OccupiedSlots);
            Assert.Equal(3, effect.ToneCount);
            Assert.Null(effect.Tones[1]);
            Assert.Null(effect.Tones[5]);
        }

        /// <summary>
        ///     A modulator pair is read as a pair, and the slot it sits in decides what it modulates.
        /// </summary>
        /// <remarks>
        ///     The three slots share one wire format and are told apart only by position, so a decoder
        ///     that read them in the wrong order produces a record that re-encodes byte for byte and
        ///     describes a different sound. Effect 5's third tone carries the first two and not the
        ///     third; effect 4787 carries the third and neither of the first two, so between them the
        ///     two fixtures pin all three positions.
        /// </remarks>
        [Fact]
        public void ModulatorSlots_AreReadInTheClientsOrder()
        {
            var gapped = new SoundEffectDefinition { Id = GappedEffectId }
                .Decode(new JagStream(CapturedBytes(GappedEffectId)));

            SoundEffectTone tone = gapped.Tones[6]!;
            Assert.NotNull(tone.PitchModulation);
            Assert.NotNull(tone.VolumeModulation);
            Assert.Null(tone.Gate);
            Assert.Equal(4, tone.PitchModulation!.Rate.Form);
            Assert.Equal(10, tone.PitchModulation.Rate.End);
            Assert.Equal(0, tone.PitchModulation.Depth.Form);
            Assert.Equal(50, tone.VolumeModulation!.Rate.End);

            var orphan = new SoundEffectDefinition { Id = OrphanEffectId }
                .Decode(new JagStream(CapturedBytes(OrphanEffectId)));

            SoundEffectTone gatedTone = orphan.Tones[0]!;
            Assert.Null(gatedTone.PitchModulation);
            Assert.Null(gatedTone.VolumeModulation);
            Assert.NotNull(gatedTone.Gate);
            Assert.Equal(1, gatedTone.Gate!.Rate.Form);
        }

        /// <summary>
        ///     A filter with an interpolation mask reads a second coefficient pair only for the poles
        ///     the mask names, and copies phase 0 for the rest.
        /// </summary>
        /// <remarks>
        ///     Effect 4787's filter declares one feed-forward pole and two feedback poles with mask
        ///     0x30, which names feedback poles 0 and 1 and leaves the feed-forward pole clear. So the
        ///     two feedback poles must differ between the phases and the feed-forward one must not.
        ///     A decoder that read the mask bit as <c>1 &lt;&lt; pole</c> instead of
        ///     <c>1 &lt;&lt; (set * 4 + pole)</c> would read four bytes too few here.
        /// </remarks>
        [Fact]
        public void AFilterMask_SelectsWhichPolesStoreASecondPhase()
        {
            var effect = new SoundEffectDefinition { Id = OrphanEffectId }
                .Decode(new JagStream(CapturedBytes(OrphanEffectId)));

            SoundEffectFilter filter = effect.Tones[0]!.Filter;
            Assert.True(filter.IsPresent);
            Assert.Equal(1, filter.PoleCount(0));
            Assert.Equal(2, filter.PoleCount(1));
            Assert.Equal(0x30, filter.InterpolationMask);
            Assert.Equal(0, filter.Gain(0));
            Assert.Equal(0, filter.Gain(1));

            //Clear bit: phase 1 is the copy the client makes at Class182.java:56-57.
            Assert.Equal(3011, filter.Frequency(0, 0, 0));
            Assert.Equal(3011, filter.Frequency(0, 1, 0));
            Assert.Equal(7134, filter.Range(0, 0, 0));
            Assert.Equal(7134, filter.Range(0, 1, 0));

            //Set bits: both feedback poles carry a distinct second pair.
            Assert.Equal(6553, filter.Frequency(1, 0, 0));
            Assert.Equal(57388, filter.Frequency(1, 1, 0));
            Assert.Equal(63941, filter.Frequency(1, 0, 1));
            Assert.Equal(12174, filter.Range(1, 1, 1));

            //The mask alone is enough to put the sweep envelope on the wire, even with equal gains.
            Assert.NotNull(filter.Sweep);
            Assert.Equal(5, filter.Sweep!.Segments.Count);
        }

        /// <summary>
        ///     A filter with a zero mask still stores a sweep envelope when its two gains differ.
        /// </summary>
        /// <remarks>
        ///     The other arm of <c>Class182.java:61</c>, and the more surprising one - there is no bit
        ///     anywhere saying the envelope is there. 5244 filters in this cache are in this state and
        ///     1226 are in the mask-only state, so both arms are live data rather than defensive code.
        /// </remarks>
        [Fact]
        public void AFilterWithNoMask_StillStoresASweepWhenItsGainsDiffer()
        {
            var effect = new SoundEffectDefinition { Id = GappedEffectId }
                .Decode(new JagStream(CapturedBytes(GappedEffectId)));

            SoundEffectFilter filter = effect.Tones[6]!.Filter;
            Assert.Equal(0, filter.PoleCount(0));
            Assert.Equal(1, filter.PoleCount(1));
            Assert.Equal(0, filter.InterpolationMask);
            Assert.Equal(39845, filter.Gain(0));
            Assert.Equal(0, filter.Gain(1));
            Assert.NotNull(filter.Sweep);
            Assert.Equal(2, filter.Sweep!.Segments.Count);
        }

        /// <summary>Every captured record re-encodes to the bytes it was read from.</summary>
        [Theory]
        [MemberData(nameof(CapturedEffectIds))]
        public void ACapturedEffect_ReEncodesToItsStoredBytes(int effectId)
        {
            byte[] bytes = CapturedBytes(effectId);
            var effect = new SoundEffectDefinition { Id = effectId }.Decode(new JagStream(bytes));

            Assert.Equal(bytes, effect.Encode().ToArray());
        }

        /// <summary>
        ///     Setting a present tone's pitch form to 0 is refused rather than written.
        /// </summary>
        /// <remarks>
        ///     That byte is what tells the reader the slot is occupied (<c>Class37.java:30-35</c>).
        ///     Writing it as 0 produces a file the client parses without complaint and reads as a
        ///     completely different effect, because every field after it shifts. There is no
        ///     representation of "a tone with waveform 0", so the only safe answer is to refuse.
        /// </remarks>
        [Fact]
        public void AToneWhosePitchFormIsZero_IsRefusedRatherThanWrittenAsAnEmptySlot()
        {
            var effect = new SoundEffectDefinition { Id = PlainEffectId }
                .Decode(new JagStream(CapturedBytes(PlainEffectId)));

            effect.Tones[0]!.Pitch.Form = 0;

            Assert.Throws<InvalidOperationException>(() => effect.Encode());
        }

        /// <summary>
        ///     Setting a modulator's rate form to 0 is refused for the same reason.
        /// </summary>
        [Fact]
        public void AModulatorWhoseRateFormIsZero_IsRefused()
        {
            var effect = new SoundEffectDefinition { Id = GappedEffectId }
                .Decode(new JagStream(CapturedBytes(GappedEffectId)));

            effect.Tones[6]!.PitchModulation!.Rate.Form = 0;

            Assert.Throws<InvalidOperationException>(() => effect.Encode());
        }

        /// <summary>
        ///     Levelling a filter's gains while its mask is zero is refused, not silently shortened.
        /// </summary>
        /// <remarks>
        ///     This is the one field on this index where changing a value changes the record's length:
        ///     the sweep envelope is present iff <c>mask != 0 || gain1 != gain0</c>, so equalising the
        ///     gains deletes it. Dropping it quietly would be a legal file and a lost envelope, so the
        ///     encoder reports the contradiction and leaves the caller to decide.
        /// </remarks>
        [Fact]
        public void LevellingAFiltersGains_IsRefusedWhileItStillCarriesASweep()
        {
            var effect = new SoundEffectDefinition { Id = GappedEffectId }
                .Decode(new JagStream(CapturedBytes(GappedEffectId)));

            SoundEffectFilter filter = effect.Tones[6]!.Filter;
            Assert.NotNull(filter.Sweep);
            filter.SetGain(0, filter.Gain(1));

            Assert.Throws<InvalidOperationException>(() => effect.Encode());

            //Dropping the envelope as well is the coherent edit, and it is accepted.
            filter.Sweep = null;
            byte[] shortened = effect.Encode().ToArray();
            Assert.True(shortened.Length < CapturedBytes(GappedEffectId).Length);
        }

        /// <summary>
        ///     A harmonic with amplitude 0 is refused, because 0 is the list terminator.
        /// </summary>
        [Fact]
        public void AHarmonicWithZeroAmplitude_IsRefused()
        {
            var effect = new SoundEffectDefinition { Id = PlainEffectId }
                .Decode(new JagStream(CapturedBytes(PlainEffectId)));

            effect.Tones[0]!.Harmonics[0].Amplitude = 0;

            Assert.Throws<InvalidOperationException>(() => effect.Encode());
        }

        /// <summary>
        ///     A full ten partials end the list by exhausting the loop, with no terminator byte.
        /// </summary>
        /// <remarks>
        ///     Nothing in this cache reaches ten - the most any shipped tone carries is five - so the
        ///     byte-identity sweep says nothing about this branch at all. Laid out by hand against
        ///     <c>Class344.java:108-116</c>: the loop's own bound ends it, so a terminator written
        ///     there would be read back as the delay time and everything after would shift by a byte.
        ///     The check is that the encoder's own output decodes to the same ten partials.
        /// </remarks>
        [Fact]
        public void AToneWithTenHarmonics_WritesNoTerminator()
        {
            var effect = new SoundEffectDefinition { Id = PlainEffectId }
                .Decode(new JagStream(CapturedBytes(PlainEffectId)));

            SoundEffectTone tone = effect.Tones[0]!;
            tone.Harmonics.Clear();
            for (int i = 0; i < SoundEffectTone.MaxHarmonics; i++)
                tone.Harmonics.Add(new SoundEffectHarmonic { Amplitude = i + 1, PitchOffset = -i, Delay = i });

            byte[] encoded = effect.Encode().ToArray();
            var stream = new JagStream(encoded);
            var reread = new SoundEffectDefinition { Id = PlainEffectId }.Decode(stream);

            Assert.Equal(encoded.Length, stream.Position);
            Assert.Equal(SoundEffectTone.MaxHarmonics, reread.Tones[0]!.Harmonics.Count);
            Assert.Equal(Enumerable.Range(1, SoundEffectTone.MaxHarmonics),
                reread.Tones[0]!.Harmonics.Select(harmonic => harmonic.Amplitude));
            Assert.Equal(Enumerable.Range(0, SoundEffectTone.MaxHarmonics).Select(i => -i),
                reread.Tones[0]!.Harmonics.Select(harmonic => harmonic.PitchOffset));
            Assert.Equal(reread.Tones[0]!.DelayTime, tone.DelayTime);
            Assert.Equal(reread.Tones[0]!.DelayFeedback, tone.DelayFeedback);
        }

        /// <summary>
        ///     A record with more poles than the 637 client can hold encodes and decodes here, and says
        ///     so.
        /// </summary>
        /// <remarks>
        ///     Two of the client's arrays are narrower than the format: five poles per set
        ///     (<c>Class182.java:25-27</c>) and six partials (<c>Class344.java:71-74</c>) both throw
        ///     there. Nothing in this cache reaches either, so a sweep cannot say what this codec does
        ///     with one. It reads and writes it - truncating would desynchronise the rest of the record
        ///     rather than reproduce the crash - and flags it, so an editor can refuse before saving.
        /// </remarks>
        [Fact]
        public void APatchTheClientCannotHold_RoundTripsAndIsFlagged()
        {
            var effect = new SoundEffectDefinition { Id = OrphanEffectId }
                .Decode(new JagStream(CapturedBytes(OrphanEffectId)));

            Assert.False(effect.ExceedsClientLimits);

            SoundEffectFilter filter = effect.Tones[0]!.Filter;
            filter.SetPoleCount(0, 5);
            for (int phase = 0; phase < SoundEffectFilter.Phases; phase++)
                for (int pole = 0; pole < 5; pole++)
                {
                    filter.SetFrequency(0, phase, pole, 1000 + pole);
                    filter.SetRange(0, phase, pole, 2000 + pole);
                }

            Assert.True(effect.ExceedsClientLimits);

            byte[] encoded = effect.Encode().ToArray();
            var stream = new JagStream(encoded);
            var reread = new SoundEffectDefinition { Id = OrphanEffectId }.Decode(stream);

            Assert.Equal(encoded.Length, stream.Position);
            Assert.Equal(5, reread.Tones[0]!.Filter.PoleCount(0));
            Assert.Equal(1004, reread.Tones[0]!.Filter.Frequency(0, 0, 4));
            Assert.True(reread.ExceedsClientLimits);
        }
    }
}
