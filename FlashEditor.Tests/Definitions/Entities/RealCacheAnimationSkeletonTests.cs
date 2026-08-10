using System;
using System.Collections.Generic;
using System.Linq;
using FlashEditor.Cache;
using FlashEditor.Definitions.Entities;
using FlashEditor.IO;
using FlashEditor.Rendering;
using FlashEditor.Tests.Cache.RealCache;
using Xunit;

namespace FlashEditor.Tests.Definitions.Entities {
    /// <summary>
    ///     The skeleton filter that stands in for an NPC-to-attack-animation link the cache does not
    ///     have.
    /// </summary>
    /// <remarks>
    ///     <b>The filter is a heuristic, so what is asserted here is the relationship and not a
    ///     number.</b> Nothing in the client checks that a sequence's skeleton matches the model it
    ///     is applied to - frames bind by bone-label index and a mismatch produces garbage rather
    ///     than an error - so there is no ground truth to compare against. What can be asserted is
    ///     that the filter is sound in the one direction that matters: an NPC's own animations, which
    ///     the cache does link through its render animation set, must survive a filter built for that
    ///     NPC. A filter that drops those is wrong however plausible the rest of its output looks.
    /// </remarks>
    public sealed class RealCacheAnimationSkeletonTests : IClassFixture<RealCacheFixture> {
        private readonly RealCacheFixture fixture;

        /// <summary>Binds the shared open cache.</summary>
        public RealCacheAnimationSkeletonTests(RealCacheFixture fixture) {
            this.fixture = fixture;
        }

        /// <summary>
        ///     Every animation an NPC's own render set names survives the filter built for it.
        /// </summary>
        /// <remarks>
        ///     The one direction with a right answer. A render animation set names the sequences the
        ///     client plays on that NPC, so those demonstrably animate it - if the filter excludes
        ///     one, the filter is wrong.
        ///     <para>
        ///     Swept over every NPC that names a render set rather than over a chosen few. That is
        ///     what caught the defect this test was written against: the filter was built from the
        ///     skeleton of the NPC's <i>first</i> animation, and 
        ///     <see cref="ARenderSetSpansMoreThanOneSkeleton"/> is the reason that was wrong.
        ///     </para>
        /// </remarks>
        [RealCacheFact]
        public void AnNpcsOwnAnimationsAreNeverFilteredOut() {
            RSCache cache = fixture.OpenCache();
            var frames = new CacheAnimationDataSource(cache);
            AnimationSkeletonIndex index = AnimationSkeletonIndex.Build(cache, frames);

            Assert.True(index.SequenceCount > 0,
                "No sequence resolved to a skeleton at all, so the sweep found nothing to filter.");

            int checkedNpcs = 0;
            var mismatches = new List<string>();

            foreach (NPCDefinition npc in Npcs(cache)) {
                if (npc.renderTypeID < 0)
                    continue;

                IReadOnlyList<NpcAnimation> own = NpcAnimationSet.For(cache, npc, out _);
                if (own.Count == 0)
                    continue;

                var ownIds = own.Select(animation => animation.AnimationId).ToList();
                IReadOnlyCollection<int> skeletons = index.SkeletonsOf(ownIds);
                if (skeletons.Count == 0)
                    continue;

                checkedNpcs++;
                var allowed = new HashSet<int>(index.SequencesFor(skeletons));

                foreach (int animation in ownIds) {
                    //A sequence whose skeleton could not be resolved is absent from the index
                    //entirely, which is a different thing from being filtered out.
                    if (index.SkeletonOf(animation) < 0)
                        continue;

                    if (!allowed.Contains(animation)) {
                        mismatches.Add("NPC " + npc.id + " names animation " + animation +
                            " (skeleton " + index.SkeletonOf(animation) +
                            ") which its own filter excludes");
                    }
                }
            }

            Assert.True(checkedNpcs > 0, "No NPC in the sample named a render set with animations.");
            Assert.Empty(mismatches);
        }

        /// <summary>
        ///     An NPC's render set can name animations built for different skeletons.
        /// </summary>
        /// <remarks>
        ///     <b>The fact that shaped the filter, pinned so it cannot be simplified away.</b> The
        ///     obvious design is one skeleton per NPC, taken from its idle animation, and it is
        ///     wrong: NPC 3284 names animation 8326 on skeleton 1750 while its idle sits on another,
        ///     so a filter built from the idle alone hides animations the cache itself says that NPC
        ///     plays. If this ever stops holding, the union in
        ///     <see cref="AnimationSkeletonIndex.SequencesFor(System.Collections.Generic.IEnumerable{int})"/>
        ///     becomes unnecessary - and if someone removes the union while it still holds, the test
        ///     above fails and points here.
        /// </remarks>
        [RealCacheFact]
        public void ARenderSetSpansMoreThanOneSkeleton() {
            RSCache cache = fixture.OpenCache();
            var frames = new CacheAnimationDataSource(cache);
            AnimationSkeletonIndex index = AnimationSkeletonIndex.Build(cache, frames);

            int spanning = 0;

            foreach (NPCDefinition npc in Npcs(cache)) {
                if (npc.renderTypeID < 0)
                    continue;

                IReadOnlyList<NpcAnimation> own = NpcAnimationSet.For(cache, npc, out _);
                if (own.Count == 0)
                    continue;

                if (index.SkeletonsOf(own.Select(animation => animation.AnimationId)).Count > 1)
                    spanning++;
            }

            Assert.True(spanning > 0,
                "No render set named animations across more than one skeleton, so the union in " +
                "SequencesFor is buying nothing and the single-skeleton filter would do.");
        }

        /// <summary>
        ///     The filter actually narrows the list, which is the whole point of it.
        /// </summary>
        /// <remarks>
        ///     A filter that returns everything is not a filter, and one that returns nothing is
        ///     worse than none at all. Both are stated as a relationship against the sweep's own
        ///     total rather than as a count, because the number of sequences differs between the two
        ///     supported caches and a written-down figure would belong to whichever produced it.
        /// </remarks>
        [RealCacheFact]
        public void TheFilterNarrowsWithoutEmptying() {
            RSCache cache = fixture.OpenCache();
            var frames = new CacheAnimationDataSource(cache);
            AnimationSkeletonIndex index = AnimationSkeletonIndex.Build(cache, frames);

            //The skeleton the most sequences are built for, which is the least favourable case for
            //a claim that the filter narrows anything.
            var perSkeleton = new Dictionary<int, int>();
            int total = 0;

            foreach (int sequence in AllSequences(index)) {
                int skeleton = index.SkeletonOf(sequence);
                perSkeleton[skeleton] = perSkeleton.TryGetValue(skeleton, out int seen) ? seen + 1 : 1;
                total++;
            }

            Assert.True(total > 0, "Nothing was indexed.");

            int widest = 0;
            foreach (KeyValuePair<int, int> entry in perSkeleton)
                if (entry.Value > widest)
                    widest = entry.Value;

            Assert.True(widest > 0, "Every skeleton bucket was empty.");
            Assert.True(widest < total,
                "The busiest skeleton holds every sequence in the cache, so filtering by skeleton " +
                "excludes nothing and the feature is a no-op.");
        }

        /// <summary>
        ///     Every NPC the reference table declares, read a group at a time.
        /// </summary>
        /// <remarks>
        ///     A group read rather than a read per NPC. <c>RSCache.ReadFile</c> releases the group as
        ///     soon as it has handed back one file, so walking 13,359 NPCs individually would decode
        ///     the same 106 groups 13,359 times.
        /// </remarks>
        private static IEnumerable<NPCDefinition> Npcs(RSCache cache) {
            CacheAddressing addressing = CacheAddressing.For(RSConstants.NPC_DEFINITIONS_INDEX);

            foreach (int group in cache.EnumerateGroups(RSConstants.NPC_DEFINITIONS_INDEX)) {
                IReadOnlyDictionary<int, JagStream> files;
                try {
                    files = cache.ReadGroup(RSConstants.NPC_DEFINITIONS_INDEX, group);
                }
                catch (Exception) {
                    continue;
                }

                foreach (KeyValuePair<int, JagStream> file in files) {
                    if (file.Value == null)
                        continue;

                    NPCDefinition npc;
                    try {
                        npc = new NPCDefinition(file.Value) { id = addressing.DefinitionId(group, file.Key) };
                    }
                    catch (Exception) {
                        continue;
                    }

                    yield return npc;
                }
            }
        }

        private static IEnumerable<int> AllSequences(AnimationSkeletonIndex index) {
            //The index exposes lookups rather than its contents, so this walks the id space the
            //same way a caller would. Bounded by the largest sequence id either cache declares.
            for (int sequence = 0; sequence < 65536; sequence++)
                if (index.SkeletonOf(sequence) >= 0)
                    yield return sequence;
        }
    }
}
