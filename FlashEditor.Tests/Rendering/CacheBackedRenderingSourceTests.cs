using System;
using FlashEditor.cache;
using FlashEditor.Definitions;
using FlashEditor.Definitions.Animation;
using FlashEditor.Rendering;
using FlashEditor.Tests.Cache.RealCache;
using Xunit;

namespace FlashEditor.Tests.Rendering
{
    /// <summary>
    ///     The two cache bridges, against a real 639 cache.
    /// </summary>
    /// <remarks>
    ///     <c>CacheAnimationDataSource</c> and <c>CacheParticleDataSource</c> had no coverage at all
    ///     while nothing in the editor constructed them, and they are the only part of the rendering
    ///     layer that reads bytes - everything above them is exercised by the in-memory sources the
    ///     rest of this folder uses. So the join they perform was the least-defended thing in the
    ///     layer at exactly the moment it became reachable.
    ///     <para>
    ///     Every figure asserted here was measured to be <b>identical in the vanilla b639 capture and
    ///     in the repack</b>, which is what makes it a property of build 639 rather than of one cache
    ///     on this machine. Nothing here is scoped to <see cref="RealCacheProfile"/> for that reason.
    ///     The pairings themselves come from index 21: a spot animation names a model and an
    ///     animation together, so the two are known to belong to one another rather than assumed to.
    ///     </para>
    /// </remarks>
    public class CacheBackedRenderingSourceTests : IClassFixture<RealCacheFixture>
    {
        /// <summary>Spot animation 0's model, which skeleton 2625 fully reaches.</summary>
        private const int SkinnedModelId = 49768;

        /// <summary>The animation spot animation 0 names alongside <see cref="SkinnedModelId"/>.</summary>
        private const int SkinnedAnimationId = 12358;

        /// <summary>
        ///     A model the same animation cannot reach, as the negative control.
        /// </summary>
        /// <remarks>
        ///     Static scenery carrying no vertex label the skeleton names. On screen that is
        ///     indistinguishable from an animation that holds still, which is the whole reason the
        ///     animator counts outcomes rather than reporting a bool.
        /// </remarks>
        private const int UnreachableModelId = 15748;

        /// <summary>Spot animation 813's model, which carries six emitter attachments.</summary>
        private const int EmitterModelId = 57600;

        private readonly RealCacheFixture fixture;

        public CacheBackedRenderingSourceTests(RealCacheFixture fixture)
        {
            this.fixture = fixture;
        }

        /// <summary>
        ///     The frame and skeleton a real animation names resolve, and every transform lands.
        /// </summary>
        /// <remarks>
        ///     Asserted without an <c>or</c>. "Posed something" would pass on a source that returned a
        ///     frame and a skeleton that had nothing to do with each other, because the animator would
        ///     still produce a pose; the claim that cannot be met by giving up is that all 47 resolved
        ///     transforms reached the model and none of them matched no label.
        /// </remarks>
        [RealCacheFact]
        public void CacheAnimationDataSource_ResolvesTheFrameAndSkeletonAnAnimationNames()
        {
            RSCache cache = fixture.OpenCache();
            var source = new CacheAnimationDataSource(cache);
            var animator = new SkeletalAnimator(source);

            animator.SetModels(new[] { cache.GetModelDefinition(SkinnedModelId, 0) });
            animator.Play(ReadAnimation(cache, SkinnedAnimationId));

            Assert.Null(animator.LastError);
            Assert.True(animator.HasPose);
            Assert.Equal(2625, animator.SkeletonId);
            Assert.Equal(75, animator.BoneCount);
            Assert.Equal(47, animator.ResolvedTransformCount);
            Assert.Equal(47, animator.AppliedTransformCount);
            Assert.Equal(0, animator.NoTargetTransformCount);
        }

        /// <summary>
        ///     A model the animation cannot reach is reported as reaching nothing, not as posed.
        /// </summary>
        /// <remarks>
        ///     The failure this guards is silent by construction: the pose succeeds, the mesh is
        ///     rebuilt, the viewport draws, and nothing moves. Only the counts say why.
        /// </remarks>
        [RealCacheFact]
        public void CacheAnimationDataSource_ReportsAnAnimationThatReachesNoLabelOnTheModel()
        {
            RSCache cache = fixture.OpenCache();
            var animator = new SkeletalAnimator(new CacheAnimationDataSource(cache));

            animator.SetModels(new[] { cache.GetModelDefinition(UnreachableModelId, 0) });
            animator.Play(ReadAnimation(cache, SkinnedAnimationId));

            Assert.Null(animator.LastError);
            Assert.True(animator.HasPose);
            Assert.Equal(47, animator.ResolvedTransformCount);
            Assert.Equal(0, animator.AppliedTransformCount);
            Assert.Equal(47, animator.NoTargetTransformCount);
        }

        /// <summary>
        ///     Reading two frames of one index-0 group decodes that group once.
        /// </summary>
        /// <remarks>
        ///     Not an optimisation detail. A frame is addressed by group and file, index 0 is the
        ///     largest index in the cache and is laid out chunk-major so a group cannot be
        ///     part-decoded, and an animation plays consecutive files of the same group. Decoding per
        ///     frame would re-decode the same group on every step of every loop, several times a
        ///     second, for as long as the tab is open.
        /// </remarks>
        [RealCacheFact]
        public void CacheAnimationDataSource_DecodesAFrameGroupOnceHoweverManyFramesAreRead()
        {
            RSCache cache = fixture.OpenCache();
            var source = new CacheAnimationDataSource(cache);
            AnimationDefinition record = ReadAnimation(cache, SkinnedAnimationId);

            Assert.Equal(0, source.CachedFrameSets);

            foreach (int packed in record.FrameIds)
                Assert.NotNull(source.GetFrame(packed));

            //Every step of this animation is a file of one frame set, so one group decode covers all
            //thirteen of them.
            Assert.Equal(1, source.CachedFrameSets);
            Assert.Equal(1, DistinctFrameGroups(record));
        }

        /// <summary>
        ///     Every emitter a real model attaches resolves to a definition in index 27.
        /// </summary>
        /// <remarks>
        ///     The attachment count and the resolved count are asserted separately, because a source
        ///     that returned null for every lookup would still attach nothing and still report a
        ///     system that runs - the missing-definition counters are what tell those apart.
        /// </remarks>
        [RealCacheFact]
        public void CacheParticleDataSource_ResolvesEveryEmitterAModelAttaches()
        {
            RSCache cache = fixture.OpenCache();
            var system = new ParticleSystem(new CacheParticleDataSource(cache));

            system.SetModels(new[] { cache.GetModelDefinition(EmitterModelId, 0) });

            Assert.Null(system.LastError);
            Assert.Equal(6, system.EmitterCount);
            Assert.Equal(0, system.MissingEmitterCount);
            Assert.Equal(0, system.MissingEffectorCount);
            Assert.Equal(0, system.OutOfRangeAttachmentCount);

            //Stepped at the viewport's own redraw rate rather than in one long step: the simulation
            //scales every rate by the step count, so a single one-second step spawns and ages a whole
            //effect inside it and reports nothing alive at the end of it.
            int peakLive = 0;
            for (int tick = 0; tick < 60; tick++)
            {
                system.Advance(1.0 / 30.0);
                peakLive = Math.Max(peakLive, system.LiveParticleCount);
            }

            Assert.Equal(6, system.ActiveEmitterCount);
            Assert.True(peakLive > 0, "Six resolved emitters produced no particle in two seconds.");
            Assert.True(peakLive <= system.MaximumParticles);
        }

        /// <summary>Reads one animation record by id.</summary>
        /// <param name="cache">The open cache.</param>
        /// <param name="animationId">The index-20 id.</param>
        /// <returns>The decoded record.</returns>
        private static AnimationDefinition ReadAnimation(RSCache cache, int animationId)
        {
            CacheAddressing addressing = CacheAddressing.For(RSConstants.ANIMATIONS_INDEX);

            return new AnimationDefinition { Id = animationId }
                .Decode(cache.ReadFile(RSConstants.ANIMATIONS_INDEX,
                    addressing.GroupOf(animationId), addressing.FileOf(animationId)));
        }

        /// <summary>How many distinct index-0 groups an animation's steps are spread across.</summary>
        /// <param name="record">The animation.</param>
        /// <returns>The count.</returns>
        private static int DistinctFrameGroups(AnimationDefinition record)
        {
            var groups = new System.Collections.Generic.HashSet<int>();

            foreach (int packed in record.FrameIds)
                groups.Add(AnimationDefinition.FrameGroupOf(packed));

            return groups.Count;
        }
    }
}
