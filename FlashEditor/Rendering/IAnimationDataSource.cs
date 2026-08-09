using System.Collections.Generic;
using System;
using FlashEditor.Definitions.Animation;
using FlashEditor.Cache;
using FlashEditor.IO;

namespace FlashEditor.Rendering
{
    /// <summary>
    ///     Where <see cref="SkeletalAnimator"/> gets its index-0 frames and index-1 skeletons.
    /// </summary>
    /// <remarks>
    ///     The seam exists so the pose arithmetic can be tested without a cache. A skeletal pose is a
    ///     join across three indexes and the expected coordinates have to be worked out by hand from
    ///     the geometry; doing that against real cache data would mean hunting for a frame with the
    ///     shape the test needs and re-deriving the answer whenever the cache moved.
    ///     <para>
    ///     Every method returns null rather than throwing for data that is not there. A missing frame
    ///     is an ordinary state of an editor pointed at an incomplete cache, and the animator turns
    ///     one into a sentence in the status bar.
    ///     </para>
    /// </remarks>
    public interface IAnimationDataSource
    {
        /// <summary>Reads one frame by its packed index-0 address.</summary>
        /// <param name="packedFrameId">Group in the high sixteen bits, file in the low sixteen.</param>
        /// <returns>The frame, or null when the cache does not hold it.</returns>
        FrameDefinition? GetFrame(int packedFrameId);

        /// <summary>Reads one skeleton by its index-1 group id.</summary>
        /// <param name="skeletonId">The group id, as a frame names it.</param>
        /// <returns>The skeleton, or null when the cache does not hold it.</returns>
        SkeletonDefinition? GetSkeleton(int skeletonId);
    }

    /// <summary>Reads frames and skeletons out of a real cache, caching what it decodes.</summary>
    /// <remarks>
    ///     The caching is not an optimisation to be tidied away. A frame is addressed by group and
    ///     file, and index 0 is the largest index in the cache - 3526 groups and 359,931 files, laid
    ///     out chunk-major so a group cannot be part-decoded. Reading one frame means decoding its
    ///     whole group. An animation plays consecutive files of the same group, so decoding per frame
    ///     would re-decode the same group on every step of every loop, several times a second.
    /// </remarks>
    public sealed class CacheAnimationDataSource : IAnimationDataSource
    {
        /// <summary>The cache to read from. Opened read-only and never written by this type.</summary>
        private readonly RSCache cache;

        /// <summary>Decoded frames per index-0 group.</summary>
        /// <remarks>
        ///     A group that failed to read is cached as an empty dictionary rather than left absent,
        ///     so a broken group is not re-read on every frame of a looping animation.
        /// </remarks>
        private readonly Dictionary<int, Dictionary<int, FrameDefinition>> frameSets =
            new Dictionary<int, Dictionary<int, FrameDefinition>>();

        /// <summary>Decoded skeletons per index-1 group, with null meaning "looked and found nothing".</summary>
        private readonly Dictionary<int, SkeletonDefinition?> skeletons = new Dictionary<int, SkeletonDefinition?>();

        /// <summary>How many index-0 groups have been decoded, for the diagnostics panel.</summary>
        public int CachedFrameSets => frameSets.Count;

        /// <summary>How many index-1 groups have been looked up, including the misses.</summary>
        public int CachedSkeletons => skeletons.Count;

        /// <summary>Creates a source over an open cache.</summary>
        /// <param name="cache">The cache.</param>
        /// <exception cref="ArgumentNullException"><paramref name="cache"/> is null.</exception>
        public CacheAnimationDataSource(RSCache cache)
        {
            this.cache = cache ?? throw new ArgumentNullException(nameof(cache));
        }

        /// <summary>Drops everything decoded so far.</summary>
        /// <remarks>Call after an edit to index 0 or 1, or the viewport keeps showing the old bytes.</remarks>
        public void Clear()
        {
            frameSets.Clear();
            skeletons.Clear();
        }

        /// <inheritdoc/>
        public FrameDefinition? GetFrame(int packedFrameId)
        {
            if (packedFrameId < 0)
            {
                return null;
            }

            int group = AnimationDefinition.FrameGroupOf(packedFrameId);
            int file = AnimationDefinition.FrameIndexOf(packedFrameId);

            if (!frameSets.TryGetValue(group, out Dictionary<int, FrameDefinition>? frames))
            {
                frames = ReadFrameSet(group);
                frameSets[group] = frames;
            }

            return frames.TryGetValue(file, out FrameDefinition? frame) ? frame : null;
        }

        /// <inheritdoc/>
        public SkeletonDefinition? GetSkeleton(int skeletonId)
        {
            if (skeletonId < 0)
            {
                return null;
            }

            //TryGetValue rather than a null check on the value, so a cached miss is not retried.
            if (skeletons.TryGetValue(skeletonId, out SkeletonDefinition? cached))
            {
                return cached;
            }

            SkeletonDefinition? skeleton = ReadSkeleton(skeletonId);
            skeletons[skeletonId] = skeleton;
            return skeleton;
        }

        /// <summary>Decodes every frame in one index-0 group.</summary>
        /// <remarks>
        ///     Each file is decoded inside its own try, so one damaged frame costs that frame rather
        ///     than the whole group - an animation with one bad step should still play the rest, and
        ///     the animator reports the gap as a missing frame.
        /// </remarks>
        /// <param name="group">The index-0 group id.</param>
        /// <returns>The frames it decoded, keyed by file id. Empty when the group could not be read.</returns>
        private Dictionary<int, FrameDefinition> ReadFrameSet(int group)
        {
            Dictionary<int, FrameDefinition> frames = new Dictionary<int, FrameDefinition>();
            IReadOnlyDictionary<int, JagStream> files;

            try
            {
                files = cache.ReadGroup(RSConstants.FRAMES_INDEX, group);
            }
            catch (Exception)
            {
                return frames;
            }

            foreach (KeyValuePair<int, JagStream> file in files)
            {
                try
                {
                    //The id is stamped before Decode, because a frame reports its own address in the
                    //error messages the animator builds and would otherwise say it was frame zero.
                    frames[file.Key] = new FrameDefinition
                    {
                        Id = AnimationDefinition.PackFrame(group, file.Key)
                    }.Decode(file.Value);
                }
                catch (Exception)
                {
                    //Left out of the dictionary, which the animator reports as a missing frame.
                }
            }

            return frames;
        }

        /// <summary>Decodes the first file of one index-1 group as a skeleton.</summary>
        /// <remarks>
        ///     Index 1 is one file per group across all 3106 of them, but the file id is not
        ///     necessarily zero, so this takes the first the group holds rather than asking for a
        ///     particular one.
        /// </remarks>
        /// <param name="skeletonId">The index-1 group id.</param>
        /// <returns>The skeleton, or null.</returns>
        private SkeletonDefinition? ReadSkeleton(int skeletonId)
        {
            IReadOnlyDictionary<int, JagStream> files;

            try
            {
                files = cache.ReadGroup(RSConstants.SKINS, skeletonId);
            }
            catch (Exception)
            {
                return null;
            }

            using IEnumerator<KeyValuePair<int, JagStream>> file = files.GetEnumerator();

            if (!file.MoveNext())
            {
                return null;
            }

            try
            {
                return new SkeletonDefinition { Id = skeletonId }.Decode(file.Current.Value);
            }
            catch (Exception)
            {
                return null;
            }
        }
    }

    /// <summary>A source holding frames and skeletons handed to it directly.</summary>
    /// <remarks>
    ///     What the pose tests build their fixtures from, and what a future editor preview would use
    ///     to show an edit that has not been saved yet.
    /// </remarks>
    public sealed class InMemoryAnimationDataSource : IAnimationDataSource
    {
        /// <summary>Frames by packed index-0 address.</summary>
        private readonly Dictionary<int, FrameDefinition> frames = new Dictionary<int, FrameDefinition>();

        /// <summary>Skeletons by index-1 group id.</summary>
        private readonly Dictionary<int, SkeletonDefinition> skeletons = new Dictionary<int, SkeletonDefinition>();

        /// <summary>Adds or replaces a frame.</summary>
        /// <param name="packedFrameId">Group in the high sixteen bits, file in the low sixteen.</param>
        /// <param name="frame">The frame.</param>
        /// <returns>This source, so a fixture reads as one expression.</returns>
        /// <exception cref="ArgumentNullException"><paramref name="frame"/> is null.</exception>
        public InMemoryAnimationDataSource AddFrame(int packedFrameId, FrameDefinition frame)
        {
            frames[packedFrameId] = frame ?? throw new ArgumentNullException(nameof(frame));
            return this;
        }

        /// <summary>Adds or replaces a skeleton.</summary>
        /// <param name="skeletonId">The index-1 group id.</param>
        /// <param name="skeleton">The skeleton.</param>
        /// <returns>This source, so a fixture reads as one expression.</returns>
        /// <exception cref="ArgumentNullException"><paramref name="skeleton"/> is null.</exception>
        public InMemoryAnimationDataSource AddSkeleton(int skeletonId, SkeletonDefinition skeleton)
        {
            skeletons[skeletonId] = skeleton ?? throw new ArgumentNullException(nameof(skeleton));
            return this;
        }

        /// <inheritdoc/>
        public FrameDefinition? GetFrame(int packedFrameId)
        {
            return frames.TryGetValue(packedFrameId, out FrameDefinition? frame) ? frame : null;
        }

        /// <inheritdoc/>
        public SkeletonDefinition? GetSkeleton(int skeletonId)
        {
            return skeletons.TryGetValue(skeletonId, out SkeletonDefinition? skeleton) ? skeleton : null;
        }
    }
}
