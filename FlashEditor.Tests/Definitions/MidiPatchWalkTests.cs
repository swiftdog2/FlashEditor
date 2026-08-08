using System;
using System.Collections.Generic;
using FlashEditor.Definitions.Audio;
using Xunit;

namespace FlashEditor.Tests.Definitions
{
    /// <summary>
    ///     Pins how an index-15 patch expands its six run-length planes into per-key values.
    /// </summary>
    /// <remarks>
    ///     <b>Nothing else can.</b> <c>MidiPatchDefinition.Encode</c> replays the stored run lists
    ///     and value blocks verbatim, so a walk that expanded them with an off-by-one would still
    ///     re-encode every one of the 176 patches byte for byte and the whole-index sweep would stay
    ///     green. The aggregate tallies in <c>RealCacheMidiPatchTests</c> cannot see it either: a run
    ///     boundary moved by one key changes which key gets which value and leaves the histogram of
    ///     values almost unchanged.
    ///     <para>
    ///     So this builds one patch as bytes, by hand, with every plane chosen so that its run
    ///     boundaries fall on different keys from every other plane's, and states the expected value
    ///     for all 128 keys as a hand-written table of ranges rather than by re-deriving it. The
    ///     expectations come from <c>Node_Sub44.java:205-289</c>, which is where the client performs
    ///     the same five walks; each assertion below cites the lines it was read from.
    ///     </para>
    ///     <para>
    ///     The two boundaries that matter, and that a wrong walk gets wrong in opposite directions:
    ///     the sample plane advances on <b>every</b> key, while the mute-group, pan and envelope
    ///     planes advance <b>only on keys that hold a sample</b> (the <c>anIntArray4246[i] != 0</c>
    ///     guard at :225, :242 and :259). The volume plane is a third case again - it advances with
    ///     the sample run list but consumes a byte only when the run's first key holds a sample
    ///     (:275-288).
    ///     </para>
    /// </remarks>
    public class MidiPatchWalkTests
    {
        // ===================================================================
        //  The hand-built patch
        // ===================================================================
        //
        //  Sample plane, run list [2, 3, 4] then the unbounded run:
        //      keys   0.. 1   reference 0        silent
        //      keys   2.. 4   reference 22       id 5, Vorbis bank,       not held
        //      keys   5.. 8   reference 15       id 3, sound-effect bank, held
        //      keys   9..127  reference 40       id 9, Vorbis bank,       held
        //
        //  So the keys that hold a sample are 2..127, and every plane below indexes into that
        //  sequence rather than into the keys directly.

        /// <summary>Run lengths of the sample plane; the unbounded fourth run is implied.</summary>
        private static readonly sbyte[] SampleRuns = { 2, 3, 4 };

        /// <summary>The four sample references, in the order the walk reads them.</summary>
        /// <remarks>
        ///     <c>(reference - 1)</c> splits into bank in bit 0, held in bit 1 and the sample id
        ///     above them (<c>Node_Sub44.java:215-219</c>). 0 is silence.
        /// </remarks>
        private static readonly int[] SampleReferences = { 0, 22, 15, 40 };

        /// <summary>
        ///     How many bytes each reference occupied, deliberately not all minimal.
        /// </summary>
        /// <remarks>
        ///     Reference 22 is written in two bytes here where one would do, because the shipped
        ///     bank does exactly that for 1060 of its 1151 references and a decoder that normalised
        ///     the width would rewrite nearly every patch on the index.
        /// </remarks>
        private static readonly int[] SampleReferenceWidths = { 1, 2, 1, 1 };

        /// <summary>Run lengths of the mute-group plane, counted in keys that hold a sample.</summary>
        private static readonly sbyte[] MuteGroupRuns = { 4, 6 };

        /// <summary>Stored mute-group bytes, one per run plus one for the unbounded run.</summary>
        /// <remarks>The client subtracts one (<c>Node_Sub44.java:227</c>), so a stored 0 is "none".</remarks>
        private static readonly byte[] MuteGroupValues = { 1, 0, 5 };

        /// <summary>Run lengths of the pan plane, counted in keys that hold a sample.</summary>
        private static readonly sbyte[] PanRuns = { 10 };

        /// <summary>Stored pan bytes, read signed and mapped by <c>(b + 16) &lt;&lt; 2</c>.</summary>
        /// <remarks><c>Node_Sub44.java:244</c>. -16 is hard left and +16 is hard right.</remarks>
        private static readonly byte[] PanValues = { unchecked((byte) -16), 16 };

        /// <summary>Run lengths of the envelope plane, counted in keys that hold a sample.</summary>
        private static readonly sbyte[] EnvelopeRuns = { 7, 5 };

        /// <summary>
        ///     The one stored byte of the envelope back-reference chain.
        /// </summary>
        /// <remarks>
        ///     Slots 0 and 1 are implicit envelopes 0 and 1 (<c>Node_Sub44.java:152-153</c>). A
        ///     stored 1 at slot 2 is at or below the current selection, so it is biased down to 0 and
        ///     selects envelope 0 again (:158-161) - which is the branch a naive reader gets wrong,
        ///     because it looks like "envelope 1".
        /// </remarks>
        private const byte EnvelopeChainByte = 1;

        /// <summary>Per-run volume bytes; only runs whose first key holds a sample store one.</summary>
        /// <remarks>Run 0 is silent and stores nothing, so there are three bytes for four runs.</remarks>
        private static readonly byte[] VolumeValues = { 63, 0, 99 };

        /// <summary>The whole-patch volume byte, stored biased down by one.</summary>
        private const byte StoredPatchVolume = 100;

        // ===================================================================
        //  Expected per-key values, written out by hand
        // ===================================================================

        /// <summary>One hand-written expectation: every key from <c>From</c> to <c>To</c> holds <c>Value</c>.</summary>
        private readonly struct Band
        {
            public Band(int from, int to, int value)
            {
                From = from;
                To = to;
                Value = value;
            }

            /// <summary>First key the band covers.</summary>
            public int From { get; }

            /// <summary>Last key the band covers, inclusive.</summary>
            public int To { get; }

            /// <summary>What every key in the band should read.</summary>
            public int Value { get; }
        }

        /// <summary>
        ///     Asserts a per-key reading against a hand-written band table, and that the table is
        ///     total.
        /// </summary>
        /// <remarks>
        ///     The totality check is the point: a band table with a gap in it would let a walk that
        ///     is wrong on exactly the unlisted key pass. Every one of the 128 keys must be named by
        ///     exactly one band.
        /// </remarks>
        /// <param name="plane">The plane's name, for the failure message.</param>
        /// <param name="bands">The expectation.</param>
        /// <param name="read">How to read the value for a key.</param>
        private static void AssertBands(string plane, Band[] bands, Func<int, int> read)
        {
            var covered = new int[MidiPatchDefinition.Keys];
            foreach (Band band in bands)
                for (int key = band.From; key <= band.To; key++)
                    covered[key]++;

            for (int key = 0; key < MidiPatchDefinition.Keys; key++)
                Assert.True(covered[key] == 1,
                    plane + ": key " + key + " is named by " + covered[key] + " bands, not exactly one.");

            foreach (Band band in bands)
                for (int key = band.From; key <= band.To; key++)
                    Assert.True(read(key) == band.Value,
                        plane + ": key " + key + " read " + read(key) + ", expected " + band.Value + ".");
        }

        // ===================================================================
        //  The tests
        // ===================================================================

        /// <summary>The hand-built bytes decode, and re-encode to exactly themselves.</summary>
        /// <remarks>
        ///     Not the interesting assertion on its own - the whole-index sweep already makes it -
        ///     but it is what licenses every test below to talk about "the patch these bytes
        ///     describe" rather than about a definition assembled in memory.
        /// </remarks>
        [Fact]
        public void TheHandBuiltPatch_RoundTripsToItsOwnBytes()
        {
            byte[] stored = BuildPatchBytes();
            MidiPatchDefinition patch = Decode(stored);

            Assert.Equal(stored, patch.Encode().ToArray());
        }

        /// <summary>
        ///     The sample plane advances on every key, silent ones included.
        /// </summary>
        /// <remarks>
        ///     <c>Node_Sub44.java:208-220</c>: the loop has no guard, so a run of silence consumes
        ///     run-list entries like any other run.
        /// </remarks>
        [Fact]
        public void WalkSamples_AdvancesOnEveryKeyIncludingSilentOnes()
        {
            MidiPatchDefinition patch = Decode(BuildPatchBytes());

            AssertBands("sample reference", new[]
            {
                new Band(0, 1, 0),
                new Band(2, 4, 22),
                new Band(5, 8, 15),
                new Band(9, 127, 40)
            }, patch.SampleReferenceOf);
        }

        /// <summary>The three fields packed into one sample reference come apart the way the client splits them.</summary>
        /// <remarks><c>Node_Sub44.java:215-219</c> for the held bit, <c>:476-485</c> for the bank and id.</remarks>
        [Fact]
        public void BankSampleIdAndHeld_SplitTheSameReferenceThreeWays()
        {
            MidiPatchDefinition patch = Decode(BuildPatchBytes());

            AssertBands("sample id", new[]
            {
                new Band(0, 1, -1),
                new Band(2, 4, 5),
                new Band(5, 8, 3),
                new Band(9, 127, 9)
            }, patch.SampleIdOf);

            AssertBands("bank", new[]
            {
                new Band(0, 1, -1),
                new Band(2, 4, (int) MidiSampleBank.Vorbis),
                new Band(5, 8, (int) MidiSampleBank.SoundEffects),
                new Band(9, 127, (int) MidiSampleBank.Vorbis)
            }, key => (int?) patch.BankOf(key) ?? -1);

            AssertBands("held", new[]
            {
                new Band(0, 1, 0),
                new Band(2, 4, 0),
                new Band(5, 8, 1),
                new Band(9, 127, 1)
            }, key => patch.HeldOf(key) ? 1 : 0);
        }

        /// <summary>
        ///     The mute-group plane advances only on keys that hold a sample.
        /// </summary>
        /// <remarks>
        ///     <c>Node_Sub44.java:224-237</c>. Keys 0 and 1 are silent, so run 0's four keys are
        ///     2..5 rather than 0..3 - the whole point of the guard, and the case that separates a
        ///     correct walk from one that indexes by key.
        /// </remarks>
        [Fact]
        public void WalkMuteGroups_CountsOnlyKeysThatHoldASample()
        {
            MidiPatchDefinition patch = Decode(BuildPatchBytes());

            AssertBands("mute group", new[]
            {
                new Band(0, 1, -1),   //silent, so the plane never reaches them
                new Band(2, 5, 0),    //run of 4 used keys, stored 1, less one
                new Band(6, 11, -1),  //run of 6 used keys, stored 0, less one
                new Band(12, 127, 4)  //the unbounded run, stored 5, less one
            }, patch.MuteGroupOf);
        }

        /// <summary>
        ///     The pan plane advances only on used keys, and maps its stored byte before storing it.
        /// </summary>
        /// <remarks>
        ///     <c>Node_Sub44.java:241-253</c>. The stored byte is read signed and mapped by
        ///     <c>(b + 16) &lt;&lt; 2</c>, so -16 is 0 and +16 is 128; reading it unsigned would give
        ///     1088 truncated to 64 and put hard left in the middle of the field.
        /// </remarks>
        [Fact]
        public void WalkPans_CountsOnlyUsedKeysAndMapsTheStoredByte()
        {
            MidiPatchDefinition patch = Decode(BuildPatchBytes());

            AssertBands("pan", new[]
            {
                new Band(0, 1, -1),
                new Band(2, 11, 0),
                new Band(12, 127, 128)
            }, patch.PanOf);
        }

        /// <summary>
        ///     The envelope plane resolves its back-reference chain and advances only on used keys.
        /// </summary>
        /// <remarks>
        ///     <c>Node_Sub44.java:155-166</c> for the chain and <c>:258-271</c> for the walk. The
        ///     selector is read at the run index <b>without</b> advancing it, so the selector and the
        ///     run length share one cursor - a walk that advanced the selector separately would go
        ///     one entry out of step at the second run.
        /// </remarks>
        [Fact]
        public void WalkEnvelopes_ResolvesTheChainAndCountsOnlyUsedKeys()
        {
            MidiPatchDefinition patch = Decode(BuildPatchBytes());

            Assert.Equal(2, patch.Envelopes.Count);
            Assert.Equal(new[] { 0, 1, 0 }, patch.EnvelopeSelectors);

            AssertBands("envelope", new[]
            {
                new Band(0, 1, -1),   //silent
                new Band(2, 8, 0),    //run of 7 used keys on envelope 0
                new Band(9, 13, 1),   //run of 5 used keys on envelope 1
                new Band(14, 127, 0)  //stored 1, biased down to 0 because it is not above the current
            }, patch.EnvelopeOf);
        }

        /// <summary>
        ///     The volume plane walks the sample run list but consumes a byte only for runs that sound.
        /// </summary>
        /// <remarks>
        ///     <c>Node_Sub44.java:275-288</c>. Run 0 is silent and stores nothing, so keys 0 and 1
        ///     read whatever the counter held before the walk started, which is zero; every later run
        ///     stores one byte and the value is that byte plus one. A walk that consumed a byte per
        ///     run regardless would hand every sounding key the previous run's volume.
        /// </remarks>
        [Fact]
        public void WalkVolumes_ConsumesAByteOnlyForRunsThatSound()
        {
            MidiPatchDefinition patch = Decode(BuildPatchBytes());

            AssertBands("volume", new[]
            {
                new Band(0, 1, 0),
                new Band(2, 4, 64),
                new Band(5, 8, 1),
                new Band(9, 127, 100)
            }, patch.VolumeOf);
        }

        /// <summary>
        ///     Tuning accumulates both delta planes up to the key, and the held bit rides the top bit.
        /// </summary>
        /// <remarks>
        ///     <c>Node_Sub44.java:196-204</c> accumulates the fine deltas into the low byte and the
        ///     coarse deltas into the high byte, and <c>:217</c> then sets bit 15 for a held key. The
        ///     accumulation is what a per-key reader gets wrong: the stored bytes are differences, so
        ///     reading key <c>n</c>'s byte as key <c>n</c>'s tuning detunes everything above the first
        ///     non-zero delta.
        /// </remarks>
        [Fact]
        public void TuningOf_AccumulatesBothDeltaPlanesAndCarriesTheHeldBit()
        {
            MidiPatchDefinition patch = Decode(BuildPatchBytes());

            //Fine deltas: 10 at key 0, 5 at key 3. Coarse deltas: 1 at key 0, 2 at key 9.
            //So the running totals are (fine 10, coarse 1) from key 0, (15, 1) from key 3,
            //and (15, 3) from key 9 - and keys 5 and up are held, which adds 0x8000.
            Assert.Equal(unchecked((short) (10 + (1 << 8))), patch.TuningOf(0));
            Assert.Equal(unchecked((short) (10 + (1 << 8))), patch.TuningOf(2));
            Assert.Equal(unchecked((short) (15 + (1 << 8))), patch.TuningOf(3));
            Assert.Equal(unchecked((short) (15 + (1 << 8))), patch.TuningOf(4));
            Assert.Equal(unchecked((short) (15 + (1 << 8) + 0x8000)), patch.TuningOf(5));
            Assert.Equal(unchecked((short) (15 + (1 << 8) + 0x8000)), patch.TuningOf(8));
            Assert.Equal(unchecked((short) (15 + (3 << 8) + 0x8000)), patch.TuningOf(9));
            Assert.Equal(unchecked((short) (15 + (3 << 8) + 0x8000)), patch.TuningOf(127));
        }

        /// <summary>Every accessor refuses a key outside the 128 a patch describes.</summary>
        [Fact]
        public void EveryPerKeyAccessor_RefusesAKeyOutsideTheKeyboard()
        {
            MidiPatchDefinition patch = Decode(BuildPatchBytes());

            foreach (Func<int, object> accessor in new Func<int, object>[]
                     {
                         key => patch.SampleReferenceOf(key),
                         key => patch.BankOf(key),
                         key => patch.SampleIdOf(key),
                         key => patch.HeldOf(key),
                         key => patch.TuningOf(key),
                         key => patch.MuteGroupOf(key),
                         key => patch.PanOf(key),
                         key => patch.EnvelopeOf(key),
                         key => patch.VolumeOf(key)
                     })
            {
                Assert.Throws<ArgumentOutOfRangeException>(() => accessor(-1));
                Assert.Throws<ArgumentOutOfRangeException>(() => accessor(MidiPatchDefinition.Keys));
            }
        }

        // ===================================================================
        //  Building the bytes
        // ===================================================================

        /// <summary>Decodes a patch from bytes.</summary>
        /// <param name="stored">The file.</param>
        /// <returns>The decoded patch.</returns>
        private static MidiPatchDefinition Decode(byte[] stored)
        {
            return new MidiPatchDefinition { Id = 0 }.Decode(new JagStream(stored));
        }

        /// <summary>
        ///     Writes the patch described by the constants above, in the client's field order.
        /// </summary>
        /// <remarks>
        ///     Written out here rather than produced by <c>MidiPatchDefinition.Encode</c> on purpose.
        ///     Round-tripping this project's encoder through its own decoder proves nothing about
        ///     either, so the bytes are laid out from <c>Node_Sub44.java:103-447</c> directly and the
        ///     encoder is then checked against them.
        /// </remarks>
        /// <returns>The patch file.</returns>
        private static byte[] BuildPatchBytes()
        {
            var bytes = new List<byte>();

            //Mute-group run list, its terminator, then one value per run plus the unbounded run.
            foreach (sbyte run in MuteGroupRuns)
                bytes.Add(unchecked((byte) run));
            bytes.Add(0);
            bytes.AddRange(MuteGroupValues);

            //Pan, same shape.
            foreach (sbyte run in PanRuns)
                bytes.Add(unchecked((byte) run));
            bytes.Add(0);
            bytes.AddRange(PanValues);

            //Envelope run list, then the back-reference chain, which stores nothing for slots 0
            //and 1 and one byte for every slot after them.
            foreach (sbyte run in EnvelopeRuns)
                bytes.Add(unchecked((byte) run));
            bytes.Add(0);
            bytes.Add(EnvelopeChainByte);

            //Two envelopes. The first has two attack points and one release point; the second has
            //neither, which is the shape that decides whether its rate bytes appear at all.
            bytes.Add(2);   //envelope 0 attack points
            bytes.Add(1);   //envelope 0 release points
            bytes.Add(0);   //envelope 1 attack points
            bytes.Add(0);   //envelope 1 release points

            bytes.Add(0);   //volume curve points
            bytes.Add(0);   //pan curve points

            foreach (sbyte run in SampleRuns)
                bytes.Add(unchecked((byte) run));
            bytes.Add(0);

            //Tuning deltas: 128 fine then 128 coarse, almost all zero so the accumulation is legible.
            var fine = new byte[MidiPatchDefinition.Keys];
            fine[0] = 10;
            fine[3] = 5;
            bytes.AddRange(fine);

            var coarse = new byte[MidiPatchDefinition.Keys];
            coarse[0] = 1;
            coarse[9] = 2;
            bytes.AddRange(coarse);

            //One variable-length sample reference per run, at the widths declared above.
            for (int i = 0; i < SampleReferences.Length; i++)
                WriteVarInt(bytes, SampleReferences[i], SampleReferenceWidths[i]);

            bytes.AddRange(VolumeValues);
            bytes.Add(StoredPatchVolume);

            //Envelope 0's two attack levels; it has one release point, which stores zero levels.
            bytes.Add(unchecked((byte) (sbyte) -40));
            bytes.Add(unchecked((byte) (sbyte) 60));

            //Release time deltas, then attack time deltas, each in envelope order.
            bytes.Add(7);   //envelope 0, one release point
            bytes.Add(9);   //envelope 0, one attack time delta for two attack points

            bytes.Add(3);   //envelope 0 decay
            bytes.Add(0);   //envelope 1 decay

            bytes.Add(11);  //envelope 0 attack rate, present because it has attack levels
            bytes.Add(13);  //envelope 0 release rate, present because it has release time deltas
            bytes.Add(17);  //envelope 0 decay rate, present because its decay is above zero

            bytes.Add(21);  //envelope 0 vibrato rate
            bytes.Add(0);   //envelope 1 vibrato rate
            bytes.Add(23);  //envelope 0 vibrato depth, present because its rate is above zero
            bytes.Add(29);  //envelope 0 vibrato delay, present because its depth is above zero

            return bytes.ToArray();
        }

        /// <summary>Writes a variable-length quantity in a stated number of bytes.</summary>
        /// <param name="bytes">The file being built.</param>
        /// <param name="value">The value.</param>
        /// <param name="width">How many bytes to spend on it.</param>
        private static void WriteVarInt(List<byte> bytes, int value, int width)
        {
            for (int shift = (width - 1) * 7; shift > 0; shift -= 7)
                bytes.Add((byte) (((value >> shift) & 0x7F) | 0x80));
            bytes.Add((byte) (value & 0x7F));
        }
    }
}
