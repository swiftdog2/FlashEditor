using System;
using System.Collections.Generic;
using FlashEditor.Cache;
using FlashEditor.Definitions.Audio.Sfx2;
using FlashEditor.Definitions.Audio.Sfx2.Vorbis;
using FlashEditor.IO;

namespace FlashEditor.Definitions.Audio.Synth {
    /// <summary>
    ///     Resolves the three cache indexes a MIDI voice needs: patches from 15, samples from 14 and
    ///     from 4.
    /// </summary>
    /// <remarks>
    ///     <c>Particle_Sub3_Sub5_Sub2.java:99-100</c> hands the client's synthesiser exactly these
    ///     three archives, and <c>Node_Sub44.method1517</c> (Node_Sub44.java:476-485) is what routes
    ///     a key's sample reference between the two sample banks.
    ///     <para>
    ///     <b>Index 4 is not rendered.</b> It is a procedural bank - each record is a description of
    ///     a synthesiser patch that the client's <c>Class344</c> renders to PCM - and this project has
    ///     a codec for it and no renderer. Measured over both caches, that costs <b>14 of the 21,491
    ///     keys</b> in the whole patch bank, across 10 patches and 6 distinct samples. Those keys go
    ///     silent and are counted in <see cref="UnrenderedEffectKeys"/> so the player can say so
    ///     rather than quietly dropping notes.
    ///     </para>
    ///     <para>
    ///     Everything is cached, because a track hits the same handful of patches and samples
    ///     thousands of times and each index-14 sample costs a full Vorbis decode.
    ///     </para>
    /// </remarks>
    public sealed class MidiSoundBank {
        private readonly RSCache? cache;
        private readonly Dictionary<int, MidiPatchDefinition?> patches = new Dictionary<int, MidiPatchDefinition?>();
        private readonly Dictionary<int, PcmSample?> vorbisSamples = new Dictionary<int, PcmSample?>();
        private VorbisSetup? setup;
        private bool setupFailed;

        /// <summary>How many notes were dropped because their sample lives in the unrendered index-4 bank.</summary>
        public int UnrenderedEffectKeys { get; private set; }

        /// <summary>How many notes were dropped because their patch or sample would not decode.</summary>
        public int FailedLookups { get; private set; }

        /// <summary>
        ///     Binds a bank to an open cache, or to none.
        /// </summary>
        /// <remarks>
        ///     A null cache is a bank that resolves nothing, following the same convention as a
        ///     detail pane bound with a null cache: the synthesiser is constructible and inert
        ///     rather than absent, so a caller does not have to special-case the state where no
        ///     cache is loaded.
        /// </remarks>
        /// <param name="cache">The cache to read from, or null. It is only read.</param>
        public MidiSoundBank(RSCache? cache) {
            this.cache = cache;
        }

        /// <summary>
        ///     The patch a program number selects, or null when the bank holds none.
        /// </summary>
        /// <remarks>
        ///     The patch id is the group id on index 15 outright - <c>Class355.java:15-19</c> fetches
        ///     it through the single-file accessor with no arithmetic in between. What produces the
        ///     id is the caller: see <see cref="MidiSynthesiser"/> for the bank-select combination.
        /// </remarks>
        /// <param name="patchId">The patch id.</param>
        /// <returns>The patch, or null.</returns>
        public MidiPatchDefinition? Patch(int patchId) {
            if (patches.TryGetValue(patchId, out MidiPatchDefinition? cached))
                return cached;

            MidiPatchDefinition? patch = null;
            try {
                JagStream? file = cache?.ReadFile(RSConstants.MIDI_PATCH_INDEX, patchId, 0);
                if (file != null)
                    patch = new MidiPatchDefinition { Id = patchId }.Decode(file);
            } catch (Exception) {
                //A patch that will not decode is a silent instrument rather than a dead player.
                patch = null;
            }

            if (patch == null)
                FailedLookups++;

            patches[patchId] = patch;
            return patch;
        }

        /// <summary>
        ///     The sample a key names, decoded, or null when it cannot be played.
        /// </summary>
        /// <param name="bank">Which index the key names.</param>
        /// <param name="sampleId">The sample id within that bank.</param>
        /// <returns>The sample, or null.</returns>
        public PcmSample? Sample(MidiSampleBank bank, int sampleId) {
            if (bank == MidiSampleBank.SoundEffects) {
                UnrenderedEffectKeys++;
                return null;
            }

            if (vorbisSamples.TryGetValue(sampleId, out PcmSample? cached))
                return cached;

            PcmSample? sample = null;
            try {
                VorbisSetup? header = Setup();
                JagStream? file = header == null ? null : cache?.ReadFile(RSConstants.SFX2_INDEX, sampleId, 0);
                if (file != null) {
                    var record = new Sfx2Sample { Id = sampleId }.Decode(file);
                    byte[] pcm = new Sfx2VorbisDecoder(header!).Decode(record);
                    sample = new PcmSample(PcmSample.AsSigned(pcm), record.SampleRate, record.LoopStart,
                        record.LoopEnd, record.IsLooping);
                }
            } catch (Exception) {
                sample = null;
            }

            if (sample == null)
                FailedLookups++;

            vorbisSamples[sampleId] = sample;
            return sample;
        }

        /// <summary>
        ///     Index 14's shared setup header, parsed once.
        /// </summary>
        /// <remarks>
        ///     A failure is remembered rather than retried. Without group 0 nothing on the index can
        ///     be decoded at all, so retrying it once per note would cost a cache read per note to
        ///     reach the same answer.
        /// </remarks>
        /// <returns>The setup, or null when group 0 is unreadable.</returns>
        private VorbisSetup? Setup() {
            if (setup != null || setupFailed)
                return setup;

            try {
                JagStream? group = cache?.ReadFile(RSConstants.SFX2_INDEX, Sfx2SetupHeader.SetupGroupId, 0);
                if (group != null)
                    setup = new VorbisSetup(group.ToArray());
            } catch (Exception) {
                setup = null;
            }

            setupFailed = setup == null;
            return setup;
        }
    }
}
