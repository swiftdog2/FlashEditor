using System;
using System.Collections.Generic;
using FlashEditor.cache;
using FlashEditor.Definitions.Audio;
using FlashEditor.Tests.Cache.RealCache;
using Xunit;
using Xunit.Abstractions;

namespace FlashEditor.Tests.Definitions
{
    /// <summary>
    ///     Joins index 15's per-key sample references to the two banks they name, and requires every
    ///     one of them to land on a group those banks actually declare.
    /// </summary>
    /// <remarks>
    ///     The join is the claim a synthesiser rests on and nothing else in the suite makes it. Index
    ///     15's own sweep proves each patch re-encodes to its bytes; index 14's proves every sample
    ///     decodes; neither says the reference in a patch names a sample that exists. A bank bit
    ///     read the wrong way round, or an id shifted by the wrong number of places, would leave both
    ///     sweeps green and every note pointing at the wrong instrument - and because both banks are
    ///     densely populated, most wrong ids would still resolve to <b>something</b>, which is
    ///     exactly the "plausible mapping confirmed by accident" this project warns about.
    ///     <para>
    ///     So this asserts the whole population lands in range, which a wrong shift cannot achieve:
    ///     <c>(reference - 1) &gt;&gt; 2</c> against <c>&gt;&gt; 1</c> doubles every id and pushes the
    ///     top of the range past both banks' declared group counts.
    ///     </para>
    /// </remarks>
    [Collection("RealCache")]
    public sealed class RealCacheMidiSampleMappingTests : IClassFixture<RealCacheFixture>
    {
        private readonly RealCacheFixture _fixture;
        private readonly ITestOutputHelper _output;

        /// <summary>Binds the shared open cache and the per-test output sink.</summary>
        public RealCacheMidiSampleMappingTests(RealCacheFixture fixture, ITestOutputHelper output)
        {
            _fixture = fixture;
            _output = output;
        }

        /// <summary>Reads one single-file group as the cache stores it.</summary>
        /// <param name="index">The cache index.</param>
        /// <param name="groupId">The group.</param>
        /// <returns>The unpacked file.</returns>
        private byte[] Group(int index, int groupId)
        {
            RSReferenceTable table = _fixture.Table(index);
            byte[] stored = _fixture.RawContainer(index, groupId);
            Assert.True(stored != null, $"index {index} group {groupId} is declared but its index record is empty");

            int[] fileIds = table.GetArchiveEntry(groupId).GetValidFileIds();
            RSContainer container = _fixture.TryDecodeContainer(index, groupId, stored);
            Assert.True(container != null, $"index {index} group {groupId}: container would not decode");

            RSArchive archive = RSArchive.Decode(container.GetStream(), fileIds);
            return archive.GetFile(fileIds[0]).ToArray();
        }

        /// <summary>
        ///     Every key that names a sample names one its bank declares, and the census of which
        ///     bank the bank leans on is printed rather than written down.
        /// </summary>
        /// <remarks>
        ///     The bank populations come off the reference tables on each run. They are identical in
        ///     both supported caches today, which is a fact about today's caches and not about the
        ///     format, so the assertion is the relationship and the numbers go to the output.
        /// </remarks>
        [RealCacheFact]
        public void EveryKeysSampleReference_NamesAGroupItsBankDeclares()
        {
            var vorbisGroups = new HashSet<int>(_fixture.Table(RSConstants.SFX2_INDEX).GetArchiveEntries().Keys);
            var effectGroups = new HashSet<int>(_fixture.Table(RSConstants.SOUND_EFFECTS).GetArchiveEntries().Keys);
            var patchIds = new List<int>(_fixture.Table(RSConstants.MIDI_PATCH_INDEX).GetArchiveEntries().Keys);
            patchIds.Sort();

            var failures = new List<string>();
            int patches = 0;
            int usedKeys = 0;
            int vorbisKeys = 0;
            int effectKeys = 0;
            int heldKeys = 0;
            int muteGroupKeys = 0;
            int vibratoKeys = 0;
            var distinctVorbis = new HashSet<int>();
            var distinctEffects = new HashSet<int>();
            var patchesTouchingEffects = new HashSet<int>();

            foreach (int patchId in patchIds)
            {
                var patch = new MidiPatchDefinition { Id = patchId }
                    .Decode(new JagStream(Group(RSConstants.MIDI_PATCH_INDEX, patchId)));
                patches++;

                foreach (int key in patch.UsedKeys)
                {
                    usedKeys++;
                    if (patch.HeldOf(key))
                        heldKeys++;

                    if (patch.MuteGroupOf(key) >= 0)
                        muteGroupKeys++;

                    int envelope = patch.EnvelopeOf(key);
                    if (envelope >= 0 && envelope < patch.Envelopes.Count && patch.Envelopes[envelope].VibratoRate > 0)
                        vibratoKeys++;

                    int sampleId = patch.SampleIdOf(key);
                    MidiSampleBank bank = patch.BankOf(key).Value;

                    if (bank == MidiSampleBank.Vorbis)
                    {
                        vorbisKeys++;
                        distinctVorbis.Add(sampleId);
                        if (!vorbisGroups.Contains(sampleId))
                            failures.Add($"patch {patchId} key {key}: index-14 sample {sampleId} is not declared");
                    }
                    else
                    {
                        effectKeys++;
                        distinctEffects.Add(sampleId);
                        patchesTouchingEffects.Add(patchId);
                        if (!effectGroups.Contains(sampleId))
                            failures.Add($"patch {patchId} key {key}: index-4 sample {sampleId} is not declared");
                    }
                }
            }

            _output.WriteLine($"index 15: {patches} patches, {usedKeys} keys naming a sample, {heldKeys} held");
            _output.WriteLine($"  {muteGroupKeys} keys carry a mute group, {vibratoKeys} name an envelope with vibrato");
            _output.WriteLine($"  index 14 (Vorbis): {vorbisKeys} keys over {distinctVorbis.Count} distinct samples " +
                              $"of {vorbisGroups.Count} declared");
            _output.WriteLine($"  index 4 (effects): {effectKeys} keys over {distinctEffects.Count} distinct samples " +
                              $"of {effectGroups.Count} declared, across {patchesTouchingEffects.Count} patches");

            Assert.True(failures.Count == 0,
                $"{failures.Count} of {usedKeys} keys name a sample their bank does not declare:\n  " +
                string.Join("\n  ", failures.GetRange(0, Math.Min(20, failures.Count))));

            //A patch bank that named nothing at all would pass the loop above without entering it.
            Assert.True(usedKeys > 0, "no key in the whole bank names a sample");
        }
    }
}
