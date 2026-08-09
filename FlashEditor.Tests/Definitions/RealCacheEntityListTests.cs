using System.Collections.Generic;
using System.Linq;
using FlashEditor.Cache;
using FlashEditor.Definitions;
using FlashEditor.Definitions.Config;
using FlashEditor.Definitions.Editing;
using FlashEditor.Definitions.Entities;
using FlashEditor.Tests.Cache.RealCache;
using Xunit;
using Xunit.Abstractions;
using FlashEditor.IO;

namespace FlashEditor.Tests.Definitions {
    /// <summary>
    ///     The entity page's descriptors, driven the way <c>DefinitionListPanel</c> drives them.
    /// </summary>
    /// <remarks>
    ///     The byte-identity sweeps already pin the item, NPC and object codecs over every record the
    ///     reference table declares. What they do not cover is the <b>descriptor</b> path that the
    ///     page now goes through, which is new and different in one way that matters: it decodes out
    ///     of a whole group read with <see cref="RSCache.ReadGroup"/> rather than out of a per-file
    ///     <c>ReadFile</c>, and it puts the id on the row from the address rather than from the
    ///     codec. A descriptor that took the wrong file out of the group, or stamped the wrong id on
    ///     it, would round-trip perfectly and still show and write the wrong record.
    ///     <para>
    ///     So the assertion here is the one the sweeps cannot make: <b>the row the descriptor
    ///     produces re-encodes to the bytes stored at the address the descriptor says it came
    ///     from</b>. That closes the loop through the address rather than around it.
    ///     </para>
    /// </remarks>
    public sealed class RealCacheEntityListTests : IClassFixture<RealCacheFixture> {
        private readonly RealCacheFixture _cache;
        private readonly ITestOutputHelper _output;

        public RealCacheEntityListTests(RealCacheFixture cache, ITestOutputHelper output) {
            _cache = cache;
            _output = output;
        }

        /// <summary>
        ///     How many groups each family is sampled over.
        /// </summary>
        /// <remarks>
        ///     A sample rather than a sweep, deliberately. The whole-index claim is already made by
        ///     the byte-identity sweeps; this exists to catch an addressing mistake, and an addressing
        ///     mistake shows on the first group above zero. Kept small because four other agents run
        ///     against the same dat2.
        /// </remarks>
        private const int GroupsSampled = 4;

        /// <summary>
        ///     A decoded row re-encodes to the bytes stored at the address the descriptor gives for it.
        /// </summary>
        /// <remarks>
        ///     Two claims in one, and both are needed. The re-encode is byte identity; the address is
        ///     that the row can be written back where it came from. Reading the comparison bytes
        ///     through <see cref="RSCache.ReadFileBytes"/> at the <i>descriptor's own</i> address is
        ///     what makes the second one testable - a descriptor that shifted every id by a group
        ///     would still re-encode identically against the payload it was handed.
        ///     <para>
        ///     One test over the three rather than a theory per index: <c>RealCacheFact</c> is the
        ///     only skip-aware attribute the suite has, so a theory here would run against a machine
        ///     with no cache and fail rather than skip.
        ///     </para>
        /// </remarks>
        [RealCacheFact]
        public void ARowReEncodesToTheBytesAtTheAddressItReportsCameFrom() {
            foreach (int indexId in new[] {
                         RSConstants.ITEM_DEFINITIONS_INDEX,
                         RSConstants.NPC_DEFINITIONS_INDEX,
                         RSConstants.OBJECTS_DEFINITIONS_INDEX
                     })
                CheckRoundTrip(indexId);
        }

        /// <summary>Runs the round trip for one index.</summary>
        /// <param name="indexId">The index to sample.</param>
        private void CheckRoundTrip(int indexId) {
            RSCache cache = _cache.OpenCache();
            IDefinitionListDescriptor descriptor = DescriptorFor(indexId);

            List<DefinitionAddress> addresses = descriptor.Enumerate(cache).ToList();
            Assert.NotEmpty(addresses);

            int[] groups = addresses.Select(address => address.GroupId).Distinct().ToArray();
            int[] sampled = Sample(groups, GroupsSampled);

            int checked_ = 0;
            foreach (int group in sampled) {
                IReadOnlyDictionary<int, JagStream> files = cache.ReadGroup(indexId, group);

                foreach (DefinitionAddress address in addresses.Where(a => a.GroupId == group)) {
                    JagStream payload = files[address.FileId];
                    object row = descriptor.Decode(cache, address, payload);

                    DefinitionAddress reported = descriptor.AddressOf(row);
                    Assert.Equal(address, reported);

                    byte[] stored = cache.ReadFileBytes(indexId, reported.GroupId, reported.FileId);
                    Assert.Equal(stored, descriptor.Encode(row).ToArray());
                    checked_++;
                }
            }

            _output.WriteLine($"Index {indexId}: {checked_} of {addresses.Count} records over " +
                              $"{sampled.Length} of {groups.Length} groups");
            Assert.True(checked_ > 0);
        }

        /// <summary>
        ///     The model listing addresses exactly the files index 7's reference table declares.
        /// </summary>
        /// <remarks>
        ///     Compared pair for pair rather than by count, for the reason every enumeration check
        ///     here is: a count agrees with plenty of wrong walks. The row count is not written down -
        ///     index 7 declares 63,607 groups in the vanilla capture and 63,614 in the repack.
        ///     <para>
        ///     Derived a second way as well, through <c>RSCache.EnumerateModelReferences</c>, which is
        ///     the walk the Models tab used before this page absorbed it. The two reach the table
        ///     through different accessors - the descriptor through <c>EnumerateFiles</c> and that
        ///     through <c>GetArchiveEntries</c> and <c>GetValidFileIds</c> - so agreeing is evidence
        ///     the migration listed the same models the tab did, which comparing the descriptor
        ///     against its own source is not.
        ///     </para>
        ///     <para>
        ///     And it must cost no group reads at all, which is the point of the opt-out. That is
        ///     asserted by the flag rather than by a timing, because a timing is a property of the
        ///     machine.
        ///     </para>
        /// </remarks>
        [RealCacheFact]
        public void TheModelListingAddressesTheFilesTheTableDeclares() {
            RSCache cache = _cache.OpenCache();
            var descriptor = new ModelListDescriptor();

            Assert.False(descriptor.ReadsPayload);

            var declared = cache.EnumerateFiles(RSConstants.MODELS_INDEX)
                .Select(pair => (pair.Group, pair.File))
                .OrderBy(pair => pair.Group).ThenBy(pair => pair.File)
                .ToList();

            var listed = descriptor.Enumerate(cache)
                .Select(address => (address.GroupId, address.FileId))
                .OrderBy(pair => pair.GroupId).ThenBy(pair => pair.FileId)
                .ToList();

            Assert.Equal(declared, listed);

            var byReference = cache.EnumerateModelReferences()
                .Select(reference => (reference.ArchiveId, reference.FileId))
                .OrderBy(pair => pair.ArchiveId).ThenBy(pair => pair.FileId)
                .ToList();

            Assert.Equal(byReference, listed);
            _output.WriteLine($"Index 7 lists {listed.Count} models, table-driven, " +
                              $"and matches the {byReference.Count} the Models tab enumerated");
        }

        /// <summary>
        ///     A model row is fully built from its address, with an empty payload.
        /// </summary>
        /// <remarks>
        ///     This is exactly what <c>DefinitionListPanel</c> hands a descriptor that clears
        ///     <c>ReadsPayload</c>, so it is the contract rather than a hypothetical: a descriptor
        ///     that started reading the payload would produce empty rows in the grid and nothing else
        ///     would say so.
        /// </remarks>
        [RealCacheFact]
        public void AModelRowIsBuiltFromItsAddressAlone() {
            RSCache cache = _cache.OpenCache();
            var descriptor = new ModelListDescriptor();

            DefinitionAddress address = descriptor.Enumerate(cache).Skip(100).First();
            var row = (ModelListing) descriptor.Decode(cache, address, new JagStream(System.Array.Empty<byte>()));

            Assert.Equal(address.GroupId, row.ModelId);
            Assert.Equal(address.FileId, row.FileId);
            Assert.Equal(address, descriptor.AddressOf(row));
        }

        /// <summary>
        ///     Every animation an NPC's render animation set names is one index 20 declares.
        /// </summary>
        /// <remarks>
        ///     The join this feature rests on, checked the only way a join can be: by falsifying it
        ///     against the other index rather than by counting how much of it lands. An NPC names no
        ///     animation directly - opcode 127 names a record in index 2 group 32, and that record's
        ///     idle, walk, run and turn fields are the animation ids - so if the group were wrong, or
        ///     the fields were read in the wrong order, the ids would still be shorts and would still
        ///     look like animation ids. Requiring every one of them to be an id index 20 actually
        ///     declares is what a wrong join cannot survive.
        ///     <para>
        ///     Sampled over the first few NPC groups rather than all 106, for the reason
        ///     <see cref="GroupsSampled"/> gives. Coverage is reported so a run that resolved nothing
        ///     cannot pass quietly.
        ///     </para>
        /// </remarks>
        [RealCacheFact]
        public void EveryAnimationAnNpcNamesIsOneIndex20Declares() {
            RSCache cache = _cache.OpenCache();
            var descriptor = new NPCListDescriptor();

            var animationIds = new HashSet<int>();
            foreach ((int group, int file) in cache.EnumerateFiles(RSConstants.ANIMATIONS_INDEX))
                animationIds.Add(CacheAddressing.For(RSConstants.ANIMATIONS_INDEX).DefinitionId(group, file));

            var renderSets = new HashSet<int>(cache.GetFileIds(RSConstants.CONFIG, ConfigGroup.RenderAnimation));

            List<DefinitionAddress> addresses = descriptor.Enumerate(cache).ToList();
            int[] groups = Sample(addresses.Select(a => a.GroupId).Distinct().ToArray(), GroupsSampled);

            int withSet = 0;
            int resolved = 0;
            int named = 0;

            foreach (int group in groups) {
                IReadOnlyDictionary<int, JagStream> files = cache.ReadGroup(RSConstants.NPC_DEFINITIONS_INDEX, group);

                foreach (DefinitionAddress address in addresses.Where(a => a.GroupId == group)) {
                    var npc = (NPCDefinition) descriptor.Decode(cache, address, files[address.FileId]);
                    if (npc.renderTypeID < 0)
                        continue;

                    withSet++;

                    //An NPC naming a set index 2 does not carry is a fact about the cache, not a
                    //defect in the join - so it is excluded rather than counted as a success.
                    if (!renderSets.Contains(npc.renderTypeID))
                        continue;

                    IReadOnlyList<NpcAnimation> animations = NpcAnimationSet.For(cache, npc, out string reason);
                    Assert.True(string.IsNullOrEmpty(reason) || animations.Count == 0, reason);

                    if (animations.Count == 0)
                        continue;

                    resolved++;
                    foreach (NpcAnimation animation in animations) {
                        Assert.Contains(animation.AnimationId, animationIds);
                        Assert.False(string.IsNullOrWhiteSpace(animation.Label));
                        named++;
                    }
                }
            }

            _output.WriteLine($"{withSet} NPCs name a render animation set, {resolved} resolved, " +
                              $"{named} animation ids and every one is declared by index 20");

            //Without this the test passes on a cache that resolved nothing at all, which is the hole
            //an "or" in an assertion usually leaves.
            Assert.True(resolved > 0, "No NPC in the sample resolved a render animation set.");
            Assert.True(named > 0, "No render animation set in the sample named an animation.");
        }

        /// <summary>The descriptor the entity page shows for one index.</summary>
        /// <param name="indexId">The index.</param>
        /// <returns>The descriptor.</returns>
        private static IDefinitionListDescriptor DescriptorFor(int indexId) {
            if (indexId == RSConstants.ITEM_DEFINITIONS_INDEX)
                return new ItemListDescriptor();
            if (indexId == RSConstants.NPC_DEFINITIONS_INDEX)
                return new NPCListDescriptor();
            if (indexId == RSConstants.OBJECTS_DEFINITIONS_INDEX)
                return new ObjectListDescriptor();

            throw new System.ArgumentOutOfRangeException(nameof(indexId), indexId,
                "The entity page shows no editable descriptor for that index.");
        }

        /// <summary>
        ///     Groups spread across the range rather than the first few.
        /// </summary>
        /// <remarks>
        ///     The first few would all be group 0 and its neighbours, and every addressing defect
        ///     this project has had was invisible in group 0 - <c>id / 256</c> and <c>id / 128</c>
        ///     agree there.
        /// </remarks>
        /// <param name="groups">Every group the index declares, ascending.</param>
        /// <param name="wanted">How many to take.</param>
        /// <returns>The sample.</returns>
        private static int[] Sample(int[] groups, int wanted) {
            if (groups.Length <= wanted)
                return groups;

            var sampled = new int[wanted];
            for (int i = 0; i < wanted; i++)
                sampled[i] = groups[(int) ((long) i * (groups.Length - 1) / (wanted - 1))];
            return sampled;
        }
    }
}
