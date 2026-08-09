using System;
using System.Collections.Generic;
using System.Linq;
using FlashEditor.Cache;
using FlashEditor.Definitions.Audio;
using FlashEditor.Tests.Cache.RealCache;
using Xunit;
using Xunit.Abstractions;
using FlashEditor.IO;

namespace FlashEditor.Tests.Definitions
{
    /// <summary>
    ///     Decodes every sound effect the index-4 reference table declares, requires exact buffer
    ///     consumption, and requires each one to re-encode to the bytes it came from.
    /// </summary>
    /// <remarks>
    ///     Index 4 is a counted, positional format rather than an opcode stream, so exact consumption
    ///     is the whole statement about the layout: nothing is self-delimiting, and a field read one
    ///     byte wide too many shifts everything after it with no terminator to land on. There is no
    ///     opcode 0 to check, so <c>NotOpcodeTerminated</c> is on - a record's last byte is the low
    ///     half of its loop end and is a 0 only by coincidence.
    ///     <para>
    ///     The byte-identity half is what the editor depends on. The archive CRC covers the stored
    ///     bytes, so an encoder that normalised a single value would rewrite files nobody edited and
    ///     drag the reference-table entry of every group packed alongside them with it.
    ///     </para>
    /// </remarks>
    [Collection("RealCache")]
    public sealed class RealCacheSoundEffectTests : IClassFixture<RealCacheFixture>
    {
        /// <summary>Tones across every declared effect in the shipped cache.</summary>
        private const int TonesInCache = 20989;

        /// <summary>Partials across every one of those tones.</summary>
        private const int HarmonicsInCache = 31310;

        /// <summary>Tones that carry a filter block rather than a single zero byte.</summary>
        private const int FiltersInCache = 13883;

        /// <summary>Filters that carry a sweep envelope.</summary>
        private const int FilterSweepsInCache = 12873;

        /// <summary>Effects that leave a tone slot empty in the middle of their run.</summary>
        private const int GappedEffectsInCache = 1884;

        /// <summary>Effects the client would loop, by its own <c>loopStart &lt; loopEnd</c> test.</summary>
        private const int LoopingEffectsInCache = 1009;

        /// <summary>Envelopes across every declared effect, including the filters' sweeps.</summary>
        private const int EnvelopesInCache = 107239;

        /// <summary>Breakpoints across every one of those envelopes.</summary>
        private const int EnvelopeBreakpointsInCache = 486150;

        private readonly RealCacheFixture _fixture;
        private readonly ITestOutputHelper _output;

        /// <summary>Binds the shared open cache and the per-test output sink.</summary>
        public RealCacheSoundEffectTests(RealCacheFixture fixture, ITestOutputHelper output)
        {
            _fixture = fixture;
            _output = output;
        }

        /// <summary>
        ///     Groups the index-4 reference table declares, one sound effect each.
        /// </summary>
        /// <remarks>
        ///     Read from the table rather than written down: the sweeps below claim that every
        ///     declared effect was decoded, which is a relationship and holds in any cache. The
        ///     content counts further down are literals because index 4's reference table and
        ///     every group CRC in it are byte-identical across both supported caches, so they
        ///     describe build 639's sound data rather than one cache's.
        /// </remarks>
        private int SoundEffectsDeclared => _fixture.DeclaredGroups(RSConstants.SOUND_EFFECTS);

        /// <summary>
        ///     The sound-effect index bound to the production codec.
        /// </summary>
        /// <remarks>
        ///     Every group, not the 250-group sample. The whole index decompresses to about three and
        ///     a half megabytes, and the counts asserted below are statements about the cache that a
        ///     sample cannot make.
        /// </remarks>
        /// <returns>A sweep over every sound effect the reference table declares.</returns>
        private DefinitionSweep<SoundEffectDefinition> Sweep()
        {
            return new DefinitionSweep<SoundEffectDefinition>(_fixture, _output, RSConstants.SOUND_EFFECTS,
                new DefinitionCodec<SoundEffectDefinition>("sound effect",
                    (id, stream) => new SoundEffectDefinition { Id = id }.Decode(stream),
                    effect => effect.Encode()))
                .AcrossEveryGroup()
                .NotOpcodeTerminated();
        }

        /// <summary>
        ///     Every sound effect decodes and finishes on the last byte of its file.
        /// </summary>
        /// <remarks>
        ///     Sharp because the harness decodes a padded copy as well as the genuine bytes. Three
        ///     branches in this format decide a length without stating it - the tone slot markers, the
        ///     harmonic list's zero terminator and the filter's sweep envelope, whose presence follows
        ///     from the mask and the gains rather than from a flag - and getting any of them wrong
        ///     lands somewhere other than the file's end.
        /// </remarks>
        [RealCacheFact]
        public void EverySoundEffect_DecodesAndConsumesItsBufferExactly()
        {
            DefinitionSweepResult swept = Sweep().AssertExactConsumption();

            Assert.Equal(SoundEffectsDeclared, swept.Records);
            Assert.Equal(SoundEffectsDeclared, swept.Groups);
            Assert.Equal(SoundEffectsDeclared, swept.Passed);
        }

        /// <summary>Every sound effect re-encodes to the bytes it was decoded from.</summary>
        /// <remarks>
        ///     This is also the measurement behind the claim that the format is canonical. Both smart
        ///     forms can express the same small values in one byte or two, and this index uses the
        ///     narrow form every time it can - so the encoder writes shortest-form and carries no
        ///     recorded-width machinery. If a repack ever introduces a wide encoding of a small value,
        ///     this sweep is what says so, and the answer would be to capture the width rather than to
        ///     relax the assertion.
        /// </remarks>
        [RealCacheFact]
        public void EverySoundEffect_ReEncodesToTheCapturedBytes()
        {
            DefinitionSweepResult swept = Sweep().AssertReEncodesToCapturedBytes();

            Assert.Equal(SoundEffectsDeclared, swept.Records);
            Assert.Equal(SoundEffectsDeclared, swept.Passed);
            Assert.Equal(0, swept.Reordered);
        }

        /// <summary>The encoder's own output decodes back to something that encodes identically.</summary>
        /// <remarks>
        ///     Independent of byte identity against the cache: this one fails on a field the encoder
        ///     writes in a shape its own decoder reads differently, which is the property the save path
        ///     depends on once an effect has actually been edited.
        /// </remarks>
        [RealCacheFact]
        public void EverySoundEffect_EncodeIsAFixedPointOfDecode()
        {
            Sweep().AssertEncodeIsAFixedPointOfDecode();
        }

        /// <summary>
        ///     What index 4 actually contains, so the codec's coverage is stated rather than assumed.
        /// </summary>
        /// <remarks>
        ///     Counts of the cache, not of this suite, so they do not go stale. Several of them decide
        ///     how much of the codec the sweeps above can defend at all:
        ///     <list type="bullet">
        ///     <item>Two effects carry <b>no tones</b>, so a decoder that assumed at least one breaks
        ///     on shipped data.</item>
        ///     <item>1884 effects leave a slot empty in the middle. A list-of-tones model that
        ///     compacted them would re-encode to the same length and the wrong file.</item>
        ///     <item>The most partials any tone carries is <b>5</b> and the most poles any filter set
        ///     declares is <b>4</b>, which are exactly the widths of the client's fixed arrays. Both
        ///     of its latent overruns are therefore untouched by shipped data, and
        ///     <c>SoundEffectCodecTests</c> is the only thing that covers them.</item>
        ///     <item>Both arms of the filter's sweep condition occur, so neither is dead code.</item>
        ///     </list>
        /// </remarks>
        [RealCacheFact]
        public void TheSoundEffectIndex_HoldsWhatTheCodecClaimsItDoes()
        {
            var tonesPerEffect = new SortedDictionary<int, int>();
            var harmonicsPerTone = new SortedDictionary<int, int>();
            var polesPerSet = new SortedDictionary<int, int>();
            var modulatorsPerTone = new SortedDictionary<int, int>();
            var pitchForms = new SortedDictionary<int, int>();
            var volumeForms = new SortedDictionary<int, int>();
            int tones = 0;
            int harmonics = 0;
            int filters = 0;
            int sweeps = 0;
            int sweepsFromGainsAlone = 0;
            int sweepsFromMaskAlone = 0;
            int gapped = 0;
            int looping = 0;
            int envelopes = 0;
            int breakpoints = 0;
            int beyondClientLimits = 0;

            DefinitionSweepResult swept = Sweep().ForEachDecoded((record, effect) =>
            {
                int[] slots = effect.OccupiedSlots.ToArray();
                Count(tonesPerEffect, slots.Length);
                tones += slots.Length;
                if (slots.Length > 0 && !slots.SequenceEqual(Enumerable.Range(0, slots.Length)))
                    gapped++;
                if (effect.Loops)
                    looping++;
                if (effect.ExceedsClientLimits)
                    beyondClientLimits++;

                foreach (SoundEffectTone tone in effect.Tones.OfType<SoundEffectTone>())
                {
                    Count(pitchForms, tone.Pitch.Form);
                    Count(volumeForms, tone.Volume.Form);
                    Count(harmonicsPerTone, tone.Harmonics.Count);
                    harmonics += tone.Harmonics.Count;

                    // No "?" annotation: the test project has no #nullable context, where it
                    // would only raise CS8632. The slots really are optional - OfType filters them.
                    SoundEffectModulator[] modulators =
                        { tone.PitchModulation, tone.VolumeModulation, tone.Gate };
                    Count(modulatorsPerTone, modulators.Count(modulator => modulator != null));

                    Measure(tone.Pitch, ref envelopes, ref breakpoints);
                    Measure(tone.Volume, ref envelopes, ref breakpoints);
                    foreach (SoundEffectModulator modulator in modulators.OfType<SoundEffectModulator>())
                    {
                        Measure(modulator.Rate, ref envelopes, ref breakpoints);
                        Measure(modulator.Depth, ref envelopes, ref breakpoints);
                    }

                    SoundEffectFilter filter = tone.Filter;
                    Count(polesPerSet, filter.PoleCount(0));
                    Count(polesPerSet, filter.PoleCount(1));
                    if (!filter.IsPresent)
                        continue;

                    filters++;
                    if (filter.Sweep == null)
                        continue;

                    sweeps++;
                    Measure(filter.Sweep, ref envelopes, ref breakpoints);
                    if (filter.InterpolationMask == 0)
                        sweepsFromGainsAlone++;
                    else if (filter.Gain(0) == filter.Gain(1))
                        sweepsFromMaskAlone++;
                }
            });

            _output.WriteLine("tones per effect: " + Histogram(tonesPerEffect));
            _output.WriteLine("harmonics per tone: " + Histogram(harmonicsPerTone));
            _output.WriteLine("modulator pairs per tone: " + Histogram(modulatorsPerTone));
            _output.WriteLine("poles per filter set: " + Histogram(polesPerSet));
            _output.WriteLine("pitch envelope forms: " + Histogram(pitchForms));
            _output.WriteLine("volume envelope forms: " + Histogram(volumeForms));

            Assert.Equal(SoundEffectsDeclared, swept.Records);
            Assert.Equal(TonesInCache, tones);
            Assert.Equal(HarmonicsInCache, harmonics);
            Assert.Equal(FiltersInCache, filters);
            Assert.Equal(FilterSweepsInCache, sweeps);
            Assert.Equal(GappedEffectsInCache, gapped);
            Assert.Equal(LoopingEffectsInCache, looping);
            Assert.Equal(EnvelopesInCache, envelopes);
            Assert.Equal(EnvelopeBreakpointsInCache, breakpoints);

            //Two effects have no tones at all, so "at least one tone" is not an invariant.
            Assert.Equal(2, tonesPerEffect[0]);

            //Both arms of Class182.java:61 are live data. Neither can be dropped as unreachable.
            Assert.Equal(5244, sweepsFromGainsAlone);
            Assert.Equal(1226, sweepsFromMaskAlone);

            //Every tone's pitch envelope carries a non-zero form, because that byte is what marks the
            //slot occupied. The volume envelope next to it is free to carry 0 and nearly always does,
            //which is the pair of facts that makes the marker safe to rely on.
            Assert.DoesNotContain(0, pitchForms.Keys);
            Assert.Equal(20980, volumeForms[0]);

            //The client's two fixed arrays are exactly as wide as the widest shipped record, so both
            //of its latent overruns are untouched by this cache.
            Assert.Equal(SoundEffectTone.ClientHarmonics, harmonicsPerTone.Keys.Max());
            Assert.Equal(SoundEffectFilter.ClientPolesPerSet, polesPerSet.Keys.Max());
            Assert.Equal(0, beyondClientLimits);
        }

        /// <summary>
        ///     The repack's idx4 holds one more live group than its reference table declares, and
        ///     it is a valid record.
        /// </summary>
        /// <remarks>
        ///     <para>
        ///     The two readings of "how many sound effects are there" differ by one in that cache
        ///     and both are right: 10,237 is what the client can reach, because it resolves every
        ///     group through the table, and 10,238 is what the idx file physically holds. Group
        ///     4787 is repacking residue - a 156-byte container that decompresses to a well-formed
        ///     185-byte patch and is byte-identical to no other group in the index. The
        ///     table-driven sweeps above cannot see it, which is correct behaviour and not a gap,
        ///     so it is pinned here instead and carried as a codec fixture by
        ///     <c>SoundEffectCodecTests</c>.
        ///     </para>
        ///     <para>
        ///     The vanilla b639 capture has no orphan on index 4, or on any other index, so the
        ///     cache half of this is scoped to the profile. The codec half is not: the captured
        ///     bytes are committed in <c>SoundEffectCodecTests</c> and decode without touching the
        ///     cache at all, so the record stays covered whichever cache is loaded.
        ///     </para>
        /// </remarks>
        [RealCacheFact]
        public void TheOrphanGroup_IsARecordTheReferenceTableDoesNotDeclare()
        {
            RSCache cache = _fixture.OpenCache();
            IReadOnlyList<int> orphans = cache.EnumerateOrphanGroups(RSConstants.SOUND_EFFECTS);

            _output.WriteLine($"{_fixture.Profile.Name}: index 4 declares {SoundEffectsDeclared} groups " +
                              $"and holds {orphans.Count} the table does not [{string.Join(", ", orphans)}]");

            if (_fixture.Profile.OrphanGroups != null)
            {
                _fixture.Profile.OrphanGroups.TryGetValue(RSConstants.SOUND_EFFECTS, out int[] expected);
                Assert.Equal(expected ?? Array.Empty<int>(), orphans.ToArray());
            }

            //Whatever the cache holds, the record itself still has to decode and re-encode, so the
            //orphan stays pinned even where no cache on disk contains it.
            byte[] stored = SoundEffectCodecTests.CapturedBytes(SoundEffectCodecTests.OrphanEffectId);
            var stream = new JagStream(stored);
            var effect = new SoundEffectDefinition { Id = SoundEffectCodecTests.OrphanEffectId }.Decode(stream);

            Assert.Equal(stored.Length, stream.Position);
            Assert.Equal(1, effect.ToneCount);
            Assert.Equal(stored, effect.Encode().ToArray());
        }

        /// <summary>
        ///     The bytes <c>SoundEffectCodecTests</c> asserts against are still what the cache holds.
        /// </summary>
        /// <remarks>
        ///     Without this the offline tests pin the codec to literals nobody can check, which is the
        ///     shape a hand-built test takes when it asserts a bug rather than catching one. The
        ///     orphan is excluded because <see cref="RSCache.ReadFileBytes"/> resolves through the
        ///     reference table and the table has no entry for it - that is exactly the point of it,
        ///     and the test above covers it instead.
        /// </remarks>
        [RealCacheFact]
        public void TheCapturedFixtures_AreStillWhatTheCacheStores()
        {
            RSCache cache = _fixture.OpenCache();

            foreach (int effectId in new[]
                     {
                         SoundEffectCodecTests.EmptyEffectId,
                         SoundEffectCodecTests.PlainEffectId,
                         SoundEffectCodecTests.GappedEffectId
                     })
            {
                int[] files = cache.GetFileIds(RSConstants.SOUND_EFFECTS, effectId);
                Assert.Equal(new[] { 0 }, files);

                byte[] stored = cache.ReadFileBytes(RSConstants.SOUND_EFFECTS, effectId, files[0]);
                Assert.Equal(SoundEffectCodecTests.CapturedBytes(effectId), stored);
            }
        }

        private static void Measure(SoundEffectEnvelope envelope, ref int envelopes, ref int breakpoints)
        {
            envelopes++;
            breakpoints += envelope.Segments.Count;
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
