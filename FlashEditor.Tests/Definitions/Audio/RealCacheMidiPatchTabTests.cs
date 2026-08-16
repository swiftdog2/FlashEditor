using System.Collections.Generic;
using System.Linq;
using FlashEditor.Cache;
using FlashEditor.Definitions.Audio;
using FlashEditor.Definitions.Audio.Synth;
using FlashEditor.Definitions.Editing;
using FlashEditor.IO;
using FlashEditor.Tests.Cache.RealCache;
using Xunit;
using Xunit.Abstractions;

namespace FlashEditor.Tests.Definitions.Audio
{
    /// <summary>
    ///     What the MIDI patch tab derives from index 15, checked against the whole declared bank.
    /// </summary>
    /// <remarks>
    ///     <c>RealCacheMidiPatchTests</c> pins the codec: every patch decodes, consumes its buffer
    ///     exactly and re-encodes to the bytes it came from. None of that reaches the tab. The tab
    ///     adds three readings the codec has no opinion about, and each one can be wrong while every
    ///     existing sweep stays green:
    ///     <list type="bullet">
    ///     <item>a per-patch census taken over the sounding keys, which a walk that counted silent
    ///     keys would inflate on almost every patch;</item>
    ///     <item>a name taken from General MIDI keyed on the group id, which is only safe while the
    ///     id layout holds and which nothing on disk backs;</item>
    ///     <item>a bank-select derivation used to audition a key, which selects the wrong instrument
    ///     while looking and sounding like a working player.</item>
    ///     </list>
    ///     <para>
    ///     <b>The audio itself is not tested here and cannot be.</b> Nothing in this suite renders a
    ///     sample to a device, so what these assert is that the right patch is selected, the right
    ///     sample named and the right keys reported unplayable. Whether it sounds correct is a
    ///     listening pass.
    ///     </para>
    /// </remarks>
    [Collection("RealCache")]
    public sealed class RealCacheMidiPatchTabTests : IClassFixture<RealCacheFixture>
    {
        /// <summary>Patches whose id is a General MIDI melodic program, ids 0 to 127.</summary>
        private const int MelodicPrograms = 128;

        private readonly RealCacheFixture _fixture;
        private readonly ITestOutputHelper _output;

        /// <summary>Binds the shared open cache and the per-test output sink.</summary>
        public RealCacheMidiPatchTabTests(RealCacheFixture fixture, ITestOutputHelper output)
        {
            _fixture = fixture;
            _output = output;
        }

        /// <summary>Groups the index-15 reference table declares, one patch each.</summary>
        private int PatchesDeclared => _fixture.DeclaredGroups(RSConstants.MIDI_PATCH_INDEX);

        /// <summary>Every declared patch becomes a row, and every row is addressed by its own id.</summary>
        /// <remarks>
        ///     Index 15 is one file per group and the group id is the patch id
        ///     (<c>Class355.java:15-19</c> fetches it through the single-file accessor with no
        ///     arithmetic in between), which is what lets the tab label a row from its address. A
        ///     patch whose id and group disagreed would be labelled as a different instrument.
        /// </remarks>
        [RealCacheFact]
        public void EveryDeclaredPatch_BecomesARowTheTabCanShow()
        {
            List<MidiPatchListing> rows = LoadRows(out var descriptor);

            Assert.Equal(PatchesDeclared, rows.Count);

            foreach (MidiPatchListing row in rows)
            {
                Assert.Equal(row.Address.GroupId, row.Id);
                Assert.Equal(0, row.Address.FileId);
                Assert.Equal(row.Address, descriptor.AddressOf(row));

                Assert.False(string.IsNullOrWhiteSpace(row.Name), "Patch " + row.Id + " has no name.");
                Assert.False(string.IsNullOrWhiteSpace(row.Family));

                //Every sounding key is on one bank or the other, so the two columns partition the
                //third rather than overlapping it.
                Assert.Equal(row.SoundingKeys, row.VorbisKeys + row.EffectKeys);
                Assert.True(row.SoundingKeys > 0, "Patch " + row.Id + " sounds on no key at all.");
            }
        }

        /// <summary>
        ///     The census the tab shows agrees with the pinned per-key accessors, patch by patch.
        /// </summary>
        /// <remarks>
        ///     Recomputed here over all 128 keys rather than over the sounding ones, which is the
        ///     opposite of how <see cref="MidiPatchListing"/> does it. That is deliberate: the two
        ///     walks only agree if the guard on the sounding key is being applied correctly, so a
        ///     census that had lost the guard would disagree here rather than being confirmed by a
        ///     copy of itself.
        /// </remarks>
        [RealCacheFact]
        public void TheTabsCensus_AgreesWithThePerKeyAccessors()
        {
            List<MidiPatchListing> rows = LoadRows(out _);

            int sounding = 0;
            int vorbis = 0;
            int effects = 0;
            int held = 0;
            int muted = 0;
            int effectPatches = 0;
            var effectSamples = new SortedSet<int>();

            foreach (MidiPatchListing row in rows)
            {
                int rowSounding = 0;
                int rowVorbis = 0;
                int rowEffects = 0;
                int rowHeld = 0;
                int rowMuted = 0;
                var rowGroups = new SortedSet<int>();

                for (int key = 0; key < MidiPatchDefinition.Keys; key++)
                {
                    if (!row.Patch.IsKeyUsed(key))
                        continue;

                    rowSounding++;
                    if (row.Patch.BankOf(key) == MidiSampleBank.Vorbis)
                        rowVorbis++;
                    else
                        rowEffects++;

                    if (row.Patch.HeldOf(key))
                        rowHeld++;
                    if (row.Patch.MuteGroupOf(key) >= 0)
                    {
                        rowMuted++;
                        rowGroups.Add(row.Patch.MuteGroupOf(key));
                    }
                }

                Assert.Equal(rowSounding, row.SoundingKeys);
                Assert.Equal(rowVorbis, row.VorbisKeys);
                Assert.Equal(rowEffects, row.EffectKeys);
                Assert.Equal(rowHeld, row.HeldKeys);
                Assert.Equal(rowMuted, row.MuteGroupKeys);
                Assert.Equal(rowGroups.Count, row.MuteGroups);

                //And the snapshot the keyboard is painted from says the same thing as the accessors.
                foreach (MidiKeySnapshot key in row.Keys)
                {
                    Assert.Equal(row.Patch.IsKeyUsed(key.Key), key.Sounds);
                    Assert.Equal(row.Patch.BankOf(key.Key), key.Bank);
                    Assert.Equal(row.Patch.SampleIdOf(key.Key), key.SampleId);
                    Assert.Equal(row.Patch.MuteGroupOf(key.Key), key.MuteGroup);
                    Assert.Equal(row.Patch.PanOf(key.Key), key.Pan);
                    Assert.Equal(row.Patch.VolumeOf(key.Key), key.Volume);
                    Assert.Equal(row.Patch.EnvelopeOf(key.Key), key.Envelope);
                    Assert.Equal(key.Sounds && key.Bank == MidiSampleBank.SoundEffects, key.SilentHere);
                }

                sounding += rowSounding;
                vorbis += rowVorbis;
                effects += rowEffects;
                held += rowHeld;
                muted += rowMuted;

                if (rowEffects == 0)
                    continue;

                effectPatches++;
                foreach (MidiKeySnapshot key in row.Keys)
                    if (key.SilentHere)
                        effectSamples.Add(key.SampleId);
            }

            _output.WriteLine(sounding + " sounding keys, " + effects + " of them on index 4 across " +
                              effectPatches + " patches and " + effectSamples.Count + " samples");

            /* Identical in both supported caches, so these describe build 639's patch bank rather
               than whichever cache is loaded. The same figures RealCacheMidiPatchTests asserts,
               reached through the tab's own census, which is the point. */
            Assert.Equal(21491, sounding);
            Assert.Equal(21477, vorbis);
            Assert.Equal(14, effects);
            Assert.Equal(17483, held);
            Assert.Equal(45, muted);

            //The two figures the tab's index-4 notice states, and the ones MidiSoundBank's own
            //remarks quote.
            Assert.Equal(10, effectPatches);
            Assert.Equal(6, effectSamples.Count);
        }

        /// <summary>
        ///     The id layout the naming rests on, checked against the loaded cache.
        /// </summary>
        /// <remarks>
        ///     <b>This is the whole licence for the labels.</b> Index 15 has no name hashes, so the
        ///     tab calls patch 40 a violin only because ids 0 to 127 are the General MIDI melodic
        ///     block. If the layout ever stopped holding, every label in the tab would be wrong and
        ///     nothing else would say so. <c>GeneralMidiTests</c> checks the table against the same
        ///     list without a cache, so the two cannot drift apart silently.
        /// </remarks>
        [RealCacheFact]
        public void ThePatchIds_AreTheLayoutTheLabelsAssume()
        {
            List<int> ids = LoadRows(out _).Select(row => row.Id).ToList();

            Assert.Equal(PatchesDeclared, ids.Count);
            Assert.Equal(Enumerable.Range(0, MelodicPrograms), ids.Take(MelodicPrograms));
            Assert.Equal(new[] { 128, 129, 136, 144, 152, 153, 168, 176, 178, 184 },
                ids.Skip(MelodicPrograms).Take(10));
            Assert.Equal(Enumerable.Range(256, 37).Prepend(255), ids.Skip(MelodicPrograms + 10));

            foreach (int id in ids)
                Assert.False(string.IsNullOrWhiteSpace(GeneralMidi.PatchName(id)),
                    "Patch " + id + " has no label.");

            //The melodic block is the only part of the bank whose names are published, so it is the
            //only part where a name can be checked against anything.
            for (int program = 0; program < MelodicPrograms; program++)
                Assert.Equal(MidiPatchFamily.Melodic, GeneralMidi.FamilyOf(ids[program]));
        }

        /// <summary>
        ///     Auditioning any patch in the bank selects that patch and not another.
        /// </summary>
        /// <remarks>
        ///     Over every declared id rather than a sample, because the derivation
        ///     <c>(bankSelect &lt;&lt; 7) | program</c> only misbehaves above 127 and only 48 of the
        ///     bank's patches are up there. The synthesiser is bound to the open cache so the run is
        ///     the same one the tab makes, but no note is played and no device is opened.
        /// </remarks>
        [RealCacheFact]
        public void EveryPatchPreview_SelectsThePatchItNames()
        {
            RSCache cache = _fixture.OpenCache();

            foreach (MidiPatchListing row in LoadRows(out _))
            {
                var synthesiser = new MidiSynthesiser(new MidiSoundBank(cache));

                foreach (MidiSequenceEvent message in
                         new MidiSequence(MidiKeyPreview.BuildSingleNote(row.Id, 60)).Events)
                {
                    //The note-on is where a cache read would start, so the walk stops before it.
                    if ((message.Status & 0xf0) == 0x90)
                        break;

                    if (!message.IsTempo)
                        synthesiser.Send(message.Status, message.Data1, message.Data2);
                }

                Assert.Equal(row.Id, synthesiser.PatchIdOf(MidiKeyPreview.Channel));
            }
        }

        /// <summary>
        ///     The keys the tab marks unplayable are exactly the ones the sound bank refuses.
        /// </summary>
        /// <remarks>
        ///     The tab decides "silent here" from the decoded bank bit and the player decides it from
        ///     <c>MidiSoundBank.Sample</c>, and the two are separate pieces of code. If they ever
        ///     disagreed the tab would either promise audio it cannot produce or hide keys that play
        ///     perfectly well, and a listener would have no way to tell which.
        /// </remarks>
        [RealCacheFact]
        public void TheKeysMarkedSilent_AreTheOnesTheSoundBankRefuses()
        {
            var bank = new MidiSoundBank(_fixture.OpenCache());
            int refused = 0;

            foreach (MidiPatchListing row in LoadRows(out _))
            {
                if (row.EffectKeys == 0)
                    continue;

                foreach (MidiKeySnapshot key in row.Keys)
                {
                    if (!key.SilentHere)
                        continue;

                    Assert.Null(bank.Sample(MidiSampleBank.SoundEffects, key.SampleId));
                    refused++;
                    Assert.Equal(refused, bank.UnrenderedEffectKeys);
                }
            }

            //Every key the tab hatches is a key the player counts as unrendered, and there are as
            //many of them as the bank's own remarks state.
            Assert.Equal(14, refused);
        }

        /// <summary>
        ///     A row the user has not edited re-encodes, through the tab's descriptor, to its bytes.
        /// </summary>
        /// <remarks>
        ///     Separate from the codec's own byte-identity sweep because the write path the tab uses
        ///     goes through the descriptor, and <c>DefinitionListPanel.CommitEdit</c> compares its
        ///     output against <c>RSCache.ReadFileBytes</c> to decide whether to write anything at
        ///     all. A descriptor that perturbed a record would stage all 176 patches on the first
        ///     cell edit, and re-encoding rewrites the archive CRC and so the reference-table entry
        ///     of everything packed alongside.
        /// </remarks>
        [RealCacheFact]
        public void AnUneditedRow_StagesNothingThroughTheDescriptor()
        {
            RSCache cache = _fixture.OpenCache();
            List<MidiPatchListing> rows = LoadRows(out var descriptor);

            foreach (MidiPatchListing row in rows)
            {
                byte[] stored = cache.ReadFileBytes(RSConstants.MIDI_PATCH_INDEX, row.Address.GroupId,
                    row.Address.FileId);

                Assert.Equal(stored, descriptor.Encode(row).ToArray());
            }
        }

        /// <summary>Loads every declared patch through the tab's own descriptor.</summary>
        /// <param name="descriptor">The descriptor the rows were built with.</param>
        /// <returns>The rows, in the order the reference table declares them.</returns>
        private List<MidiPatchListing> LoadRows(out MidiPatchListDescriptor descriptor)
        {
            RSCache cache = _fixture.OpenCache();
            descriptor = new MidiPatchListDescriptor();

            var rows = new List<MidiPatchListing>();
            foreach (DefinitionAddress address in descriptor.Enumerate(cache))
            {
                JagStream payload = cache.ReadFile(RSConstants.MIDI_PATCH_INDEX, address.GroupId,
                    address.FileId);
                rows.Add(descriptor.Decode(cache, address, payload));
            }

            return rows;
        }
    }
}
