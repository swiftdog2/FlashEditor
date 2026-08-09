using System.Collections.Generic;
using System.Linq;
using FlashEditor.Cache;
using FlashEditor.Definitions.Animation;
using FlashEditor.Tests.Cache.RealCache;
using Xunit;
using Xunit.Abstractions;

namespace FlashEditor.Tests.Definitions.Animation
{
    /// <summary>
    ///     Decodes every animation skeleton in the real revision-639 cache, requires exact buffer
    ///     consumption, and requires each one to re-encode to the bytes it came from.
    /// </summary>
    /// <remarks>
    ///     Index 1 is a counted format rather than an opcode stream, which changes what the sweep can
    ///     claim. Nothing is self-delimiting: the leading bone count sizes five blocks of four
    ///     different widths, so a field read one byte wide too many or too few shifts everything after
    ///     it and the record cannot land on its own last byte. Exact consumption across all 3106
    ///     files is therefore the whole statement about the layout, and there is no opcode 0
    ///     terminator to check on top of it.
    ///     <para>
    ///     The byte-identity half is the one that matters to the editor: it re-encodes a skeleton on
    ///     every save, and the archive CRC covers the stored bytes, so an encoder that normalised a
    ///     single value would rewrite files nobody edited.
    ///     </para>
    /// </remarks>
    [Collection("RealCache")]
    public sealed class RealCacheSkeletonTests : IClassFixture<RealCacheFixture>
    {
        /// <summary>Bones across every skeleton in the shipped cache.</summary>
        private const int BonesInCache = 173749;

        /// <summary>Label entries across every bone in the shipped cache.</summary>
        private const int LabelEntriesInCache = 936887;

        private readonly RealCacheFixture _fixture;
        private readonly ITestOutputHelper _output;

        /// <summary>Binds the shared open cache and the per-test output sink.</summary>
        public RealCacheSkeletonTests(RealCacheFixture fixture, ITestOutputHelper output)
        {
            _fixture = fixture;
            _output = output;
        }

        /// <summary>Groups index 1's reference table declares, one skeleton each.</summary>
        /// <remarks>
        ///     Read from the table so the sweeps assert a relationship - every declared skeleton
        ///     was read - rather than a count belonging to one cache. The content figures above
        ///     stay literal because index 1's reference table and every group CRC in it are
        ///     byte-identical across both supported caches.
        /// </remarks>
        private int SkeletonsInCache => _fixture.DeclaredGroups(RSConstants.SKINS);

        /// <summary>
        ///     The skeleton index bound to the production codec.
        /// </summary>
        /// <remarks>
        ///     Every group, not the 250-group sample: the whole index decompresses to well under two
        ///     megabytes, and the counts asserted below are statements about the cache that a sample
        ///     cannot make. <c>NotOpcodeTerminated</c> drops the terminator assertion, which this
        ///     format has no equivalent of - the last byte of a skeleton is a label id.
        /// </remarks>
        /// <returns>A sweep over every skeleton the cache declares.</returns>
        private DefinitionSweep<SkeletonDefinition> Sweep()
        {
            return new DefinitionSweep<SkeletonDefinition>(_fixture, _output, RSConstants.SKINS,
                new DefinitionCodec<SkeletonDefinition>("skeleton",
                    (id, stream) => new SkeletonDefinition { Id = id }.Decode(stream),
                    skeleton => skeleton.Encode()))
                .AcrossEveryGroup()
                .NotOpcodeTerminated();
        }

        /// <summary>
        ///     Every skeleton decodes and finishes on the last byte of its file.
        /// </summary>
        /// <remarks>
        ///     The harness decodes a padded copy as well as the genuine bytes, which is what makes
        ///     this sharp: the label block runs to the end of the file, so a decoder reading one label
        ///     too few would stop short and one too many would run into the padding.
        /// </remarks>
        [RealCacheFact]
        public void EverySkeleton_DecodesAndConsumesItsBufferExactly()
        {
            DefinitionSweepResult swept = Sweep().AssertExactConsumption();

            Assert.Equal(SkeletonsInCache, swept.Records);
            Assert.Equal(SkeletonsInCache, swept.Groups);
        }

        /// <summary>Every skeleton re-encodes to the bytes it was decoded from.</summary>
        [RealCacheFact]
        public void EverySkeleton_ReEncodesToTheCapturedBytes()
        {
            DefinitionSweepResult swept = Sweep().AssertReEncodesToCapturedBytes();

            Assert.Equal(SkeletonsInCache, swept.Records);
            Assert.Equal(SkeletonsInCache, swept.Passed);
        }

        /// <summary>The encoder's own output decodes back to something that encodes identically.</summary>
        /// <remarks>
        ///     Independent of byte identity against the cache: this one fails on a field the encoder
        ///     writes in a shape its own decoder reads differently, which is the property the save
        ///     path depends on once a skeleton has actually been edited.
        /// </remarks>
        [RealCacheFact]
        public void EverySkeleton_EncodeIsAFixedPointOfDecode()
        {
            Sweep().AssertEncodeIsAFixedPointOfDecode();
        }

        /// <summary>
        ///     What index 1 actually contains, so the codec's coverage is stated rather than assumed.
        /// </summary>
        /// <remarks>
        ///     Counts of the cache, not of this suite, so they do not go stale. Two of them decide how
        ///     much of the codec the sweeps above can defend at all:
        ///     <list type="bullet">
        ///     <item>Transform type 6 occurs <b>zero</b> times, so no shipped record exercises the
        ///     client's lossy <c>6 -&gt; 2</c> remap. The byte-identity sweep passes whether or not the
        ///     decoder folds it in, and <c>SkeletonDefinitionCodecTests</c> is the only thing that
        ///     catches it.</item>
        ///     <item>The flag byte is only ever 0 or 1 and the mask is <c>0xFFFF</c> on every bone, so
        ///     the same is true of storing the flag as a bool and of any assumption about the mask.
        ///     </item>
        ///     </list>
        ///     If a repack ever introduces a 6, this assertion is what says so, and the codec already
        ///     handles it.
        /// </remarks>
        [RealCacheFact]
        public void TheSkeletonIndex_HoldsWhatTheCodecClaimsItDoes()
        {
            var transformTypes = new SortedDictionary<int, int>();
            var flags = new SortedDictionary<int, int>();
            var masks = new SortedDictionary<int, int>();
            int bones = 0;
            int labels = 0;
            int zeroBoneSkeletons = 0;
            int skeletonsWithAnAliasedType = 0;

            DefinitionSweepResult swept = Sweep().ForEachDecoded((record, skeleton) =>
            {
                bones += skeleton.BoneCount;
                labels += skeleton.TotalLabelCount;
                if (skeleton.BoneCount == 0)
                    zeroBoneSkeletons++;
                if (skeleton.Bones.Any(bone => bone.TransformType == SkeletonBone.AliasedTransformType))
                    skeletonsWithAnAliasedType++;

                foreach (SkeletonBone bone in skeleton.Bones)
                {
                    Count(transformTypes, bone.TransformType);
                    Count(flags, bone.Flag);
                    Count(masks, bone.Mask);
                }
            });

            _output.WriteLine("transform types: " + Histogram(transformTypes));
            _output.WriteLine("flag bytes: " + Histogram(flags));
            _output.WriteLine("masks: " + Histogram(masks));

            //Stated outright here, and derived everywhere else - index 1 is byte-identical across
            //both supported caches, so 3106 is a property of build 639 rather than of one of them.
            Assert.Equal(3106, swept.Records);
            Assert.Equal(SkeletonsInCache, swept.Records);
            Assert.Equal(BonesInCache, bones);
            Assert.Equal(LabelEntriesInCache, labels);
            Assert.Equal(2, zeroBoneSkeletons);

            //The remap trap is latent here, not live. Nothing in the shipped data covers it.
            Assert.Equal(0, skeletonsWithAnAliasedType);
            Assert.Equal(0, transformTypes.GetValueOrDefault(SkeletonBone.AliasedTransformType));

            //Both fields the client normalises on read are single-valued or near enough that the
            //normalisation would round-trip this cache unnoticed. Stated so nobody re-derives it.
            Assert.Equal(new[] { 0, 1 }, flags.Keys.ToArray());
            Assert.Equal(new[] { 0xFFFF }, masks.Keys.ToArray());
            Assert.Equal(BonesInCache, masks[0xFFFF]);
        }

        /// <summary>
        ///     The bytes <c>SkeletonDefinitionCodecTests</c> asserts against are still what the cache
        ///     holds.
        /// </summary>
        /// <remarks>
        ///     Without this the offline tests pin the codec to a literal nobody can check, which is
        ///     the shape a hand-built test takes when it asserts a bug rather than catching one.
        /// </remarks>
        [RealCacheFact]
        public void TheCapturedFixture_IsStillWhatTheCacheStores()
        {
            RSCache cache = _fixture.OpenCache();

            byte[] stored = cache.ReadFileBytes(RSConstants.SKINS,
                SkeletonDefinitionCodecTests.CapturedSkeletonId, 0);

            Assert.Equal(SkeletonDefinitionCodecTests.CapturedSkeletonBytes(), stored);
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
