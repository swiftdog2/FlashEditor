using System;
using System.Collections.Generic;
using System.Linq;
using FlashEditor.cache;
using FlashEditor.Definitions.Animation;
using FlashEditor.Tests.Cache.RealCache;
using Xunit;
using Xunit.Abstractions;

namespace FlashEditor.Tests.Definitions
{
    /// <summary>
    ///     Decodes every animation frame in the real revision-639 cache, requires exact buffer
    ///     consumption, and requires each one to re-encode to the bytes it came from.
    /// </summary>
    /// <remarks>
    ///     Index 0 is the largest index in the cache by file count - 359,931 frames in 3526 groups -
    ///     and it is a counted format rather than an opcode stream, so there is no terminator to check
    ///     and <c>NotOpcodeTerminated</c> drops that assertion. What replaces it is sharper than
    ///     anything an opcode index offers: the value stream is sized entirely by the axis bits in the
    ///     flag block, so a decoder that misread one flag byte, one width or one field lands somewhere
    ///     other than the last byte of the file. The client makes the same check and throws
    ///     (<c>Class7.java:112-114</c>).
    ///     <para>
    ///     The whole index is swept on every run rather than the 250-group sample, because the counts
    ///     below are statements about the cache that a sample cannot make. There is no
    ///     encode-decode-encode sweep here: byte identity across all 359,931 files already says the
    ///     encoder's output is the cache's bytes, and the decoder is proven against those, so the
    ///     fixed-point property is implied rather than independent - and this is the one index where a
    ///     fourth pass over every container is worth declining.
    ///     </para>
    /// </remarks>
    [Collection("RealCache")]
    public sealed class RealCacheFrameTests : IClassFixture<RealCacheFixture>
    {
        /// <summary>Groups index 0 holds in the shipped cache, one animation's frame set each.</summary>
        private const int FrameSetsInCache = 3526;

        /// <summary>Frame files across every group in the shipped cache.</summary>
        private const int FramesInCache = 359931;

        /// <summary>Frames that declare no transforms at all, and are four bytes long.</summary>
        /// <remarks>
        ///     The index-0 survey says 1568. It is wrong: two independent measurements agree on 1573 -
        ///     this codec, and a read-only sweep that never decodes a frame at all, counting files whose
        ///     archive size table totals four bytes. A frame with no transforms has no flag block and no
        ///     value stream, so four bytes and a zero slot count are the same population.
        /// </remarks>
        private const int EmptyFramesInCache = 1573;

        /// <summary>Signed smarts stored in the one-byte form across the whole index.</summary>
        private const int OneByteValuesInCache = 8270387;

        /// <summary>Signed smarts stored in the two-byte form across the whole index.</summary>
        private const int TwoByteValuesInCache = 11871643;

        /// <summary>
        ///     The nine groups that hold a single file, so the whole group payload is the frame.
        /// </summary>
        /// <remarks>
        ///     Every other group in this index is packed into exactly three chunks, and index 0 holds
        ///     all 3517 multi-chunk groups in the entire cache. These nine take the single-file branch
        ///     of the archive unpacker instead - no size table and no chunk-count byte - so they are the
        ///     only frames whose bytes reach the codec by a different route.
        /// </remarks>
        private static readonly int[] SingleFileGroups =
            { 22, 605, 757, 1836, 2374, 2435, 2633, 3047, 3290 };

        /// <summary>The one single-file group whose file is not id 0.</summary>
        private const int SparseSingleFileGroup = 757;

        private readonly RealCacheFixture _fixture;
        private readonly ITestOutputHelper _output;

        /// <summary>Binds the shared open cache and the per-test output sink.</summary>
        public RealCacheFrameTests(RealCacheFixture fixture, ITestOutputHelper output)
        {
            _fixture = fixture;
            _output = output;
        }

        /// <summary>The frame index bound to the production codec.</summary>
        /// <returns>A sweep over every frame the cache declares.</returns>
        private DefinitionSweep<FrameDefinition> Sweep()
        {
            return new DefinitionSweep<FrameDefinition>(_fixture, _output, RSConstants.FRAMES_INDEX,
                new DefinitionCodec<FrameDefinition>("frame",
                    (id, stream) => new FrameDefinition { Id = id }.Decode(stream),
                    frame => frame.Encode()))
                .AcrossEveryGroup()
                .NotOpcodeTerminated();
        }

        /// <summary>
        ///     Every frame decodes and finishes on the last byte of its file.
        /// </summary>
        /// <remarks>
        ///     The harness decodes a padded copy as well as the genuine bytes, which matters more here
        ///     than on any other index: a frame carries no terminator and no length, so a decoder that
        ///     over-read would otherwise stop where the buffer happens to end and report itself exact.
        ///     The padding is 0xAA, which reads as the leading byte of a two-byte smart, so an over-read
        ///     consumes two bytes of it and shows.
        /// </remarks>
        [RealCacheFact]
        public void EveryFrame_DecodesAndConsumesItsBufferExactly()
        {
            DefinitionSweepResult swept = Sweep().AssertExactConsumption();

            Assert.Equal(FramesInCache, swept.Records);
            Assert.Equal(FrameSetsInCache, swept.Groups);
            Assert.Equal(FramesInCache, swept.Passed);
        }

        /// <summary>Every frame re-encodes to the bytes it was decoded from.</summary>
        /// <remarks>
        ///     The signed smart has two legal encodings for -64 to 63, so this is the one index in the
        ///     cache where byte identity could turn on an encoding choice rather than on a field
        ///     layout. The codec records the width each value arrived in and replays it, which makes
        ///     the property hold by construction; <see cref="TheFrameIndex_HoldsWhatTheCodecClaimsItDoes"/>
        ///     separately measures that this cache never uses the wide form for a narrow value, so a
        ///     shortest-form encoder would also pass here and would be relying on the data rather than
        ///     on the format.
        /// </remarks>
        [RealCacheFact]
        public void EveryFrame_ReEncodesToTheCapturedBytes()
        {
            DefinitionSweepResult swept = Sweep().AssertReEncodesToCapturedBytes();

            Assert.Equal(FramesInCache, swept.Records);
            Assert.Equal(FramesInCache, swept.Passed);
            Assert.Equal(0, swept.Reordered);
        }

        /// <summary>
        ///     What index 0 actually contains, so the codec's coverage is stated rather than assumed.
        /// </summary>
        /// <remarks>
        ///     Counts of the cache, not of this suite, so they do not go stale. Three of them decide
        ///     what the sweeps above can be read as saying:
        ///     <list type="bullet">
        ///     <item>Not one of the 11,871,643 two-byte smarts holds a number the one-byte form could
        ///     have carried. So this cache is canonical in practice, a shortest-form encoder would
        ///     reproduce it, and no sweep over shipped data distinguishes that encoder from the one
        ///     that replays the stored width. This assertion is what makes the difference visible if a
        ///     repack ever introduces a widened value.</item>
        ///     <item>The leading byte is 1 in every file, which is why nothing may recompute it: the
        ///     client discards it, so the data is the only evidence about it there is.</item>
        ///     <item>Every group names exactly one skeleton across all its files, and every id it names
        ///     is a group index 1 really holds. That is the join, and it proves itself rather than
        ///     merely being plausible.</item>
        ///     </list>
        /// </remarks>
        [RealCacheFact]
        public void TheFrameIndex_HoldsWhatTheCodecClaimsItDoes()
        {
            var leadingBytes = new SortedDictionary<int, int>();
            var flagBytes = new SortedDictionary<int, int>();
            var skeletonsPerGroup = new Dictionary<int, HashSet<int>>();
            int emptyFrames = 0;
            int oneByteValues = 0;
            int twoByteValues = 0;
            int widenedNarrowValues = 0;
            int maxTransforms = 0;
            int maxSkeletonId = -1;

            DefinitionSweepResult swept = Sweep().ForEachDecoded((record, frame) =>
            {
                Count(leadingBytes, frame.LeadingByte);
                if (frame.TransformCount == 0)
                    emptyFrames++;
                if (frame.TransformCount > maxTransforms)
                    maxTransforms = frame.TransformCount;
                if (frame.SkeletonId > maxSkeletonId)
                    maxSkeletonId = frame.SkeletonId;

                if (!skeletonsPerGroup.TryGetValue(record.GroupId, out HashSet<int> named))
                {
                    named = new HashSet<int>();
                    skeletonsPerGroup[record.GroupId] = named;
                }
                named.Add(frame.SkeletonId);

                foreach (FrameTransform transform in frame.Transforms)
                {
                    Count(flagBytes, transform.Flag);
                    if (transform.HasX)
                        Tally(transform.X, ref oneByteValues, ref twoByteValues, ref widenedNarrowValues);
                    if (transform.HasY)
                        Tally(transform.Y, ref oneByteValues, ref twoByteValues, ref widenedNarrowValues);
                    if (transform.HasZ)
                        Tally(transform.Z, ref oneByteValues, ref twoByteValues, ref widenedNarrowValues);
                }
            });

            _output.WriteLine("leading bytes: " + Histogram(leadingBytes));
            _output.WriteLine($"{flagBytes.Count} distinct flag bytes, highest {flagBytes.Keys.Last()}");
            _output.WriteLine($"{oneByteValues} one-byte and {twoByteValues} two-byte signed smarts");
            _output.WriteLine($"largest frame declares {maxTransforms} transforms; highest skeleton id " +
                              $"named is {maxSkeletonId}");

            Assert.Equal(FramesInCache, swept.Records);
            Assert.Equal(FrameSetsInCache, swept.Groups);
            Assert.Equal(EmptyFramesInCache, emptyFrames);

            //The client reads this byte and drops it, so nothing but the data says what it holds.
            Assert.Equal(new[] { FrameDefinition.LeadingByteInThisCache }, leadingBytes.Keys.ToArray());
            Assert.Equal(FramesInCache, leadingBytes[FrameDefinition.LeadingByteInThisCache]);

            Assert.Equal(OneByteValuesInCache, oneByteValues);
            Assert.Equal(TwoByteValuesInCache, twoByteValues);
            Assert.Equal(0, widenedNarrowValues);

            //The slot count is a single byte and the cache reaches the ceiling.
            Assert.Equal(FrameDefinition.MaxTransforms, maxTransforms);

            //Every group names one skeleton, and every skeleton it names exists.
            Assert.Equal(FrameSetsInCache, skeletonsPerGroup.Count);
            Assert.All(skeletonsPerGroup.Values, named => Assert.Single(named));

            var skeletonGroups = new HashSet<int>(_fixture.Table(RSConstants.SKINS).GetArchiveEntries().Keys);
            int[] namedSkeletons = skeletonsPerGroup.Values.Select(set => set.Single()).Distinct().ToArray();
            _output.WriteLine($"{namedSkeletons.Length} distinct skeletons are named by the " +
                              $"{FrameSetsInCache} frame sets");
            Assert.All(namedSkeletons, id => Assert.Contains(id, skeletonGroups));
        }

        /// <summary>
        ///     Every frame's slots fit the skeleton it names, and resolve into poses.
        /// </summary>
        /// <remarks>
        ///     This is the half of the format that byte identity cannot reach. A frame's slot is a
        ///     position in the skeleton's bone table and the file says nothing about which bone that
        ///     is, so a frame declaring more slots than its skeleton has bones would re-encode
        ///     perfectly and animate nothing: the client indexes the array unguarded
        ///     (<c>Class7.java:61</c>) and swallows the exception (<c>:130-134</c>), leaving a frame of
        ///     zero poses. Resolving every frame in the cache is what says that never happens here, and
        ///     it exercises the type-driven defaults, the rescale and the pivot chain over real data.
        /// </remarks>
        [RealCacheFact]
        public void EveryFrame_ResolvesAgainstTheSkeletonItNames()
        {
            RSCache cache = _fixture.OpenCache();
            var typesBySkeleton = new Dictionary<int, int[]>();
            var modelFlags = new SortedDictionary<int, int>();
            var overlong = new List<string>();
            long poses = 0;
            long defaulted = 0;
            long pivoted = 0;
            int resolvedFrames = 0;

            DefinitionSweepResult swept = Sweep().ForEachDecoded((record, frame) =>
            {
                if (!typesBySkeleton.TryGetValue(frame.SkeletonId, out int[] types))
                {
                    var skeleton = new SkeletonDefinition { Id = frame.SkeletonId };
                    skeleton.Decode(new JagStream(
                        cache.ReadFileBytes(RSConstants.SKINS, frame.SkeletonId, 0)));
                    types = skeleton.GetEffectiveTransformTypes();
                    typesBySkeleton[frame.SkeletonId] = types;
                }

                //Collected rather than thrown, so the report says how many frames are mismatched
                //instead of stopping the sweep on the first one.
                if (frame.TransformCount > types.Length)
                {
                    overlong.Add($"frame {record.Id} (group {record.GroupId} file {record.FileId}) " +
                                 $"declares {frame.TransformCount} transforms against skeleton " +
                                 $"{frame.SkeletonId}'s {types.Length} bones");
                    return;
                }

                ResolvedFrame resolved = frame.Resolve(types);
                resolvedFrames++;
                poses += resolved.Poses.Count;
                Count(modelFlags, resolved.ModelBuildFlags);

                foreach (FramePose pose in resolved.Poses)
                {
                    if (pose.PivotSlot >= 0)
                        pivoted++;
                }

                foreach (FrameTransform transform in frame.Transforms)
                {
                    if (!transform.IsSkipped && transform.StoredValueCount < 3)
                        defaulted++;
                }
            });

            _output.WriteLine($"{poses} poses resolved, {pivoted} of them taking a pivot");
            _output.WriteLine($"{defaulted} slots leave at least one axis to the transform type's default");
            _output.WriteLine("model-build flags: " + Histogram(modelFlags));
            _output.WriteLine($"{typesBySkeleton.Count} distinct skeletons were loaded");

            Assert.True(overlong.Count == 0,
                $"{overlong.Count} frames declare more transforms than their skeleton has bones:" +
                Environment.NewLine + string.Join(Environment.NewLine, overlong.Take(20)));
            Assert.Equal(FramesInCache, swept.Records);
            Assert.Equal(FramesInCache, resolvedFrames);
            Assert.True(poses > 0, "no pose resolved, so nothing was checked");
        }

        /// <summary>
        ///     Exactly nine groups hold a single file, and they parse by the same rules as the rest.
        /// </summary>
        /// <remarks>
        ///     Worth pinning separately from the sweep because the difference is in the archive layer
        ///     rather than the frame layer: a single-file group has no size table and no chunk-count
        ///     byte, so its whole payload is the frame. The sweep covers both routes and cannot say
        ///     which is which, and if a tenth group ever turned single-file the sweep would still pass.
        /// </remarks>
        [RealCacheFact]
        public void TheSingleFileGroups_AreTheNineExpectedAndCarryOneFrameEach()
        {
            SortedDictionary<int, RSArchiveEntry> entries =
                _fixture.Table(RSConstants.FRAMES_INDEX).GetArchiveEntries();

            var single = new List<int>();
            var soleFileId = new Dictionary<int, int>();
            int files = 0;
            foreach (KeyValuePair<int, RSArchiveEntry> entry in entries)
            {
                int[] fileIds = entry.Value.GetValidFileIds();
                files += fileIds.Length;
                if (fileIds.Length == 1)
                {
                    single.Add(entry.Key);
                    soleFileId[entry.Key] = fileIds[0];
                }
            }
            single.Sort();

            Assert.Equal(FrameSetsInCache, entries.Count);
            Assert.Equal(FramesInCache, files);
            Assert.Equal(SingleFileGroups, single.ToArray());

            //Group 757's one frame is file 40, so a single file does not imply file 0. Frame arrays
            //are sized by capacity and indexed by id (JS5Archive.java:207-221 against :807-830), which
            //is what makes the holes legal, and this is the cache's cheapest proof of it.
            Assert.Equal(40, soleFileId[SparseSingleFileGroup]);

            //And each of them decodes and re-encodes through the same codec as every other frame.
            RSCache cache = _fixture.OpenCache();
            foreach (int groupId in SingleFileGroups)
            {
                int fileId = soleFileId[groupId];
                byte[] stored = cache.ReadFileBytes(RSConstants.FRAMES_INDEX, groupId, fileId);
                var stream = new JagStream(stored);
                var frame = new FrameDefinition { Id = (groupId << 16) | fileId }.Decode(stream);

                Assert.Equal(stored.Length, stream.Position);
                Assert.Equal(stored, frame.Encode().ToArray());
            }
        }

        /// <summary>
        ///     The bytes <c>FrameDefinitionCodecTests</c> asserts against are still what the cache
        ///     holds.
        /// </summary>
        /// <remarks>
        ///     Without this the offline tests pin the codec to a literal nobody can check, which is the
        ///     shape a hand-built test takes when it asserts a bug rather than catching one.
        /// </remarks>
        [RealCacheFact]
        public void TheCapturedFixture_IsStillWhatTheCacheStores()
        {
            RSCache cache = _fixture.OpenCache();

            byte[] stored = cache.ReadFileBytes(RSConstants.FRAMES_INDEX,
                FrameDefinitionCodecTests.CapturedFrameGroupId, FrameDefinitionCodecTests.CapturedFrameFileId);

            Assert.Equal(FrameDefinitionCodecTests.CapturedFrameBytes(), stored);
        }

        /// <summary>Counts one value's width, and whether the wide form was used unnecessarily.</summary>
        /// <param name="value">The stored value.</param>
        /// <param name="oneByte">Running count of one-byte values.</param>
        /// <param name="twoByte">Running count of two-byte values.</param>
        /// <param name="widened">Running count of two-byte values the one-byte form could hold.</param>
        private static void Tally(FrameValue value, ref int oneByte, ref int twoByte, ref int widened)
        {
            if (value.Width == JagStream.SmartWidth.OneByte)
            {
                oneByte++;
                return;
            }

            twoByte++;
            if (value.Value >= -64 && value.Value <= 63)
                widened++;
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
