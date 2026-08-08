using System.Collections.Generic;
using System.Linq;
using FlashEditor.cache;
using FlashEditor.Definitions.Audio;
using FlashEditor.Tests.Cache.RealCache;
using Xunit;
using Xunit.Abstractions;

namespace FlashEditor.Tests.Definitions
{
    /// <summary>
    ///     Decodes every MIDI patch the index-15 reference table declares, requires exact buffer
    ///     consumption, and requires each one to re-encode to the bytes it came from.
    /// </summary>
    /// <remarks>
    ///     Index 15 is six run-length-encoded planes over 128 keys plus a pool of envelopes, and
    ///     none of it is self-delimiting: the run lists sit near the front and their values much
    ///     later, and the plane that says which keys hold a sample is read fifth while three
    ///     earlier planes are gated on it. So a field read one byte wide too many shifts every
    ///     plane after it and there is no terminator anywhere to land on. Exact consumption over
    ///     all 176 patches is therefore the whole statement about the layout, and
    ///     <c>NotOpcodeTerminated</c> is mandatory - a patch's last byte is a vibrato parameter and
    ///     is a zero only by coincidence.
    ///     <para>
    ///     The byte-identity half is what says the run lists are being kept rather than
    ///     recomputed. Where a run is split is a choice the packer made that the per-key values
    ///     cannot recover, so an encoder that rebuilt the runs from the decoded keys would
    ///     re-encode to the same 128 values in a file of a different length.
    ///     </para>
    /// </remarks>
    [Collection("RealCache")]
    public sealed class RealCacheMidiPatchTests : IClassFixture<RealCacheFixture>
    {
        /// <summary>
        ///     Patches whose id is a General MIDI melodic program, ids 0 to 127.
        /// </summary>
        /// <remarks>
        ///     Written down because it is a property of General MIDI rather than of either cache,
        ///     and because the test below checks that the shipped ids actually form that block
        ///     rather than assuming it.
        /// </remarks>
        private const int MelodicPrograms = 128;

        private readonly RealCacheFixture _fixture;
        private readonly ITestOutputHelper _output;

        /// <summary>Binds the shared open cache and the per-test output sink.</summary>
        public RealCacheMidiPatchTests(RealCacheFixture fixture, ITestOutputHelper output)
        {
            _fixture = fixture;
            _output = output;
        }

        /// <summary>
        ///     Groups the index-15 reference table declares, one patch each.
        /// </summary>
        /// <remarks>
        ///     Read from the table rather than written down, so the sweeps below assert the
        ///     relationship "every declared patch was swept" instead of a number that would belong
        ///     to whichever cache it was measured on.
        /// </remarks>
        private int PatchesDeclared => _fixture.DeclaredGroups(RSConstants.MIDI_PATCH_INDEX);

        /// <summary>
        ///     The patch bank bound to the production codec.
        /// </summary>
        /// <remarks>
        ///     Every group, not the 250-group sample: the whole index is under sixty kilobytes of
        ///     payload, so sampling buys nothing and costs the claim the counts below make.
        /// </remarks>
        /// <returns>A sweep over every patch the reference table declares.</returns>
        private DefinitionSweep<MidiPatchDefinition> Sweep()
        {
            return new DefinitionSweep<MidiPatchDefinition>(_fixture, _output, RSConstants.MIDI_PATCH_INDEX,
                new DefinitionCodec<MidiPatchDefinition>("MIDI patch",
                    (id, stream) => new MidiPatchDefinition { Id = id }.Decode(stream),
                    patch => patch.Encode()))
                .AcrossEveryGroup()
                .NotOpcodeTerminated();
        }

        /// <summary>Every patch decodes and finishes on the last byte of its file.</summary>
        /// <remarks>
        ///     Sharp because the harness decodes a padded copy as well as the genuine bytes. Four
        ///     lengths in this format are decided without being stated - how many sample references
        ///     the walk reads, how many per-key volumes it reads, how many envelopes the
        ///     back-reference chain creates, and which envelope parameters a zero suppresses - and
        ///     getting any of them wrong lands somewhere other than the file's end.
        /// </remarks>
        [RealCacheFact]
        public void EveryMidiPatch_DecodesAndConsumesItsBufferExactly()
        {
            DefinitionSweepResult swept = Sweep().AssertExactConsumption();

            Assert.Equal(PatchesDeclared, swept.Records);
            Assert.Equal(PatchesDeclared, swept.Groups);
            Assert.Equal(PatchesDeclared, swept.Passed);
        }

        /// <summary>Every patch re-encodes to the bytes it was decoded from.</summary>
        /// <remarks>
        ///     The editor rewrites a patch through this encoder on save, and the archive CRC covers
        ///     the stored bytes, so anything the encoder normalises changes patches nobody edited
        ///     and drags the reference-table entry of the whole index with it.
        /// </remarks>
        [RealCacheFact]
        public void EveryMidiPatch_ReEncodesToTheCapturedBytes()
        {
            DefinitionSweepResult swept = Sweep().AssertReEncodesToCapturedBytes();

            Assert.Equal(PatchesDeclared, swept.Records);
            Assert.Equal(PatchesDeclared, swept.Passed);
            Assert.Equal(0, swept.Reordered);
        }

        /// <summary>The encoder's own output decodes back to something that encodes identically.</summary>
        /// <remarks>
        ///     Independent of byte identity against the cache. This one fails on a field the encoder
        ///     writes in a shape its own decoder reads differently, which is the property the save
        ///     path depends on once a patch has actually been edited and no comparison with the
        ///     cache can reach.
        /// </remarks>
        [RealCacheFact]
        public void EveryMidiPatch_EncodeIsAFixedPointOfDecode()
        {
            Sweep().AssertEncodeIsAFixedPointOfDecode();
        }

        /// <summary>
        ///     What index 15 actually contains, so the codec's coverage is stated rather than
        ///     assumed.
        /// </summary>
        /// <remarks>
        ///     Every figure asserted here is identical in both supported caches - index 15's
        ///     reference table and every group CRC in it match - so these describe build 639's
        ///     patch bank rather than one cache. Several of them decide how much of the codec the
        ///     sweeps above can defend:
        ///     <list type="bullet">
        ///     <item>The id block is the giveaway that this is a patch bank at all: 0 to 127 are the
        ///     General MIDI melodic programs, and the ten ids from 128 up are the GM drum kits at
        ///     their canonical program offsets.</item>
        ///     <item>Both sample banks occur, so neither arm of the reference's bit 0 is dead - but
        ///     the split is <b>21,477 keys on index 14 against 14 on index 4</b>, so a decoder that
        ///     had the bit inverted would still look almost right and would silence the bank.</item>
        ///     <item>Most sample references are <b>two</b> bytes of variable-length quantity, 1060
        ///     against 91. An encoder that wrote the shortest form would rewrite nearly every patch
        ///     in the index, which is why the width is recorded at decode.</item>
        ///     <item>Every patch has at least one sounding key and 325 of the 326 envelopes carry
        ///     vibrato, so neither is a branch this cache leaves untested.</item>
        ///     </list>
        /// </remarks>
        [RealCacheFact]
        public void TheMidiPatchBank_HoldsWhatTheCodecClaimsItDoes()
        {
            var envelopesPerPatch = new SortedDictionary<int, int>();
            var referenceWidths = new SortedDictionary<int, int>();
            var ids = new List<int>();
            int usedKeys = 0;
            int silentPatches = 0;
            int vorbisKeys = 0;
            int soundEffectKeys = 0;
            int heldKeys = 0;
            int mutedKeys = 0;
            int volumeCurves = 0;
            int panCurves = 0;
            int envelopes = 0;
            int attackEnvelopes = 0;
            int releaseEnvelopes = 0;
            int vibratoEnvelopes = 0;
            int decayEnvelopes = 0;

            DefinitionSweepResult swept = Sweep().ForEachDecoded((record, patch) =>
            {
                ids.Add(record.Id);
                Count(envelopesPerPatch, patch.Envelopes.Count);
                envelopes += patch.Envelopes.Count;

                foreach (int width in patch.SampleReferenceWidths)
                    Count(referenceWidths, width);

                int used = 0;
                foreach (int key in patch.UsedKeys)
                {
                    used++;
                    if (patch.BankOf(key) == MidiSampleBank.Vorbis)
                        vorbisKeys++;
                    else
                        soundEffectKeys++;
                    if (patch.HeldOf(key))
                        heldKeys++;
                    if (patch.MuteGroupOf(key) >= 0)
                        mutedKeys++;
                }

                usedKeys += used;
                if (used == 0)
                    silentPatches++;

                if (patch.VolumeCurveLevels.Length > 0)
                    volumeCurves++;
                if (patch.PanCurveLevels.Length > 0)
                    panCurves++;

                foreach (MidiPatchEnvelope envelope in patch.Envelopes)
                {
                    if (envelope.AttackPoints > 0)
                        attackEnvelopes++;
                    if (envelope.ReleasePoints > 0)
                        releaseEnvelopes++;
                    if (envelope.VibratoRate > 0)
                        vibratoEnvelopes++;
                    if (envelope.Decay > 0)
                        decayEnvelopes++;
                }
            });

            _output.WriteLine("envelopes per patch: " + Histogram(envelopesPerPatch));
            _output.WriteLine("sample reference widths: " + Histogram(referenceWidths));
            _output.WriteLine($"{usedKeys} sounding keys across {swept.Records} patches, " +
                              $"{soundEffectKeys} on index 4 and {vorbisKeys} on index 14");
            _output.WriteLine($"{envelopes} envelopes: {attackEnvelopes} with an attack list, " +
                              $"{releaseEnvelopes} with a release list, {decayEnvelopes} decaying, " +
                              $"{vibratoEnvelopes} with vibrato");

            Assert.Equal(PatchesDeclared, swept.Records);

            //The id block is what identifies the index. Programs 0..127 are the General MIDI
            //melodic set, and the ten above them are the GM drum kits at their published program
            //numbers - standard, room, power, electronic, TR-808, jazz, brush, orchestra and SFX -
            //which is not a shape a sound-effect bank would have.
            Assert.Equal(Enumerable.Range(0, MelodicPrograms), ids.Take(MelodicPrograms));
            Assert.Equal(new[] { 128, 129, 136, 144, 152, 153, 168, 176, 178, 184 },
                ids.Skip(MelodicPrograms).Take(10));
            Assert.Equal(Enumerable.Range(256, 37).Prepend(255),
                ids.Skip(MelodicPrograms + 10));

            //Counts of the cache, and identical in both supported caches, so they belong to build
            //639's patch bank rather than to whichever cache is loaded.
            Assert.Equal(21491, usedKeys);
            Assert.Equal(0, silentPatches);
            Assert.Equal(14, soundEffectKeys);
            Assert.Equal(21477, vorbisKeys);
            Assert.Equal(17483, heldKeys);
            Assert.Equal(45, mutedKeys);
            Assert.Equal(326, envelopes);
            Assert.Equal(240, attackEnvelopes);
            Assert.Equal(263, releaseEnvelopes);
            Assert.Equal(85, decayEnvelopes);
            Assert.Equal(325, vibratoEnvelopes);
            Assert.Equal(6, volumeCurves);
            Assert.Equal(10, panCurves);

            //Most references are wider than they need to be, which is what makes the recorded
            //width load bearing rather than defensive: an encoder that wrote the shortest form
            //would fail the byte-identity sweep on 1060 of the 1151 references in the bank.
            Assert.Equal(new[] { 1, 2 }, referenceWidths.Keys.ToArray());
            Assert.Equal(91, referenceWidths[1]);
            Assert.Equal(1060, referenceWidths[2]);

            //One envelope covers 164 of the 176 patches, and the drum kits are where the pool
            //grows: a decoder that assumed a single envelope would decode most of the bank and
            //desynchronise on the rest.
            Assert.Equal(164, envelopesPerPatch[1]);
        }

        private static void Count(SortedDictionary<int, int> counts, int value)
        {
            counts.TryGetValue(value, out int seen);
            counts[value] = seen + 1;
        }

        private static string Histogram(SortedDictionary<int, int> counts)
        {
            return string.Join(", ", counts.Select(entry => entry.Key + "=" + entry.Value));
        }
    }
}
