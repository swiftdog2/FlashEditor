using System;
using System.Collections.Generic;
using System.Linq;
using FlashEditor.Cache;
using FlashEditor.Definitions.Billboards;
using FlashEditor.Definitions.Editing;
using FlashEditor.Definitions.Entities;
using FlashEditor.Definitions.SpotAnims;
using FlashEditor.Export;
using FlashEditor.IO;
using Xunit;
using Xunit.Abstractions;

namespace FlashEditor.Tests.Cache.RealCache {
    /// <summary>
    ///     Every link a grid offers is a join the export already makes.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///     <b>This is the ceiling rule with teeth.</b> The measured join list is a ceiling rather
    ///     than a starting point, and <c>CacheExportJoins</c> is where the project states it once. A
    ///     link column is a second surface for the same relations, and a second surface is exactly
    ///     where an unmeasured one gets added quietly - it looks like a display change rather than
    ///     like a claim about the format. So the columns are checked <i>against</i> the export
    ///     rather than beside it: a link a grid draws that the export does not make is a join
    ///     nothing has evidence for.
    ///     </para>
    ///     <para>
    ///     The standing lesson is the world map icon join, whose first evidence was two self-proving
    ///     rows and a shift sweep too narrow to falsify itself. This sweep is the falsifying kind:
    ///     it runs over every declared record of each index rather than over a sample, and it fails
    ///     on the first link the export does not corroborate rather than reporting a proportion.
    ///     </para>
    ///     <para>
    ///     <b>What it deliberately does not assert.</b> Not that every link resolves to a declared
    ///     record. Some ids in this cache do dangle, that is a real property of the data rather than
    ///     a defect in the editor, and an assertion of the form "resolved or dangling" would be an
    ///     <c>or</c> that a cache whose links had all stopped resolving would pass unchanged.
    ///     Existence is reported by the preview at the point the user hovers, where it is a finding
    ///     rather than a failure.
    ///     </para>
    /// </remarks>
    /* BOTH, and each does a different job. IClassFixture supplies the opened cache, which
       [Collection] cannot: no CollectionDefinition declares "RealCache", so the attribute names a
       collection with no fixture in it. [Collection] is what stops this class running in parallel
       with the sprite suites - the billboard join it walks resolves into index 26 and so reaches
       TextureManager's process-wide store, which another collection may be clearing at the time.
       Dropping the attribute for the fixture made the sprite tests flake. */
    [Collection("RealCache")]
    public sealed class RealCacheLinkColumnTests : IClassFixture<RealCacheFixture> {
        private readonly RealCacheFixture cache;
        private readonly ITestOutputHelper output;

        /// <summary>Binds the shared open cache.</summary>
        /// <param name="cache">The shared cache.</param>
        /// <param name="output">Where the census is printed.</param>
        public RealCacheLinkColumnTests(RealCacheFixture cache, ITestOutputHelper output) {
            this.cache = cache;
            this.output = output;
        }

        /// <summary>The three indexes whose grids declare link columns and decode their own rows.</summary>
        /// <remarks>
        ///     The interface components grid is not here, and its absence is a scoping decision
        ///     rather than an oversight: its descriptor lists one interface at a time, so sweeping it
        ///     means driving the descriptor per group, and the export already walks index 3 whole.
        ///     The model grid is not here either, because it reads no payload at all - the footer its
        ///     references live in is read one model at a time, by the preview, on demand.
        /// </remarks>
        public static IEnumerable<object[]> LinkedIndexes() {
            yield return new object[] { RSConstants.OBJECTS_DEFINITIONS_INDEX };
            yield return new object[] { RSConstants.GRAPHICS_INDEX };
            yield return new object[] { RSConstants.CONFIG_BILLBOARD };
        }

        [RealCacheTheory]
        [MemberData(nameof(LinkedIndexes))]
        public void EveryLinkAGridDrawsIsAJoinTheExportMakes(int indexId) {
            RSCache open = cache.OpenCache();
            var resolver = new CacheReferenceResolver(open);
            IDefinitionListDescriptor descriptor = DescriptorFor(indexId);

            var linkColumns = descriptor.Columns.Where(column => column.Visual != null).ToList();
            Assert.NotEmpty(linkColumns);

            int records = 0;
            int links = 0;

            foreach (IGrouping<int, DefinitionAddress> group in
                descriptor.Enumerate(open).GroupBy(address => address.GroupId)) {
                IReadOnlyDictionary<int, JagStream> files = open.ReadGroup(indexId, group.Key);

                foreach (DefinitionAddress address in group) {
                    if (!files.TryGetValue(address.FileId, out JagStream payload))
                        continue;

                    object row = descriptor.Decode(open, address, payload);
                    records++;

                    /* What the export says this record points at, as (index, group, id). The
                       columns must not produce a triple that is not in here. */
                    HashSet<(int Index, int Group, int Id)> exported = CacheExportJoins
                        .Extract(row, resolver)
                        .Select(reference =>
                            (reference.TargetIndex,
                             reference.TargetIndex == RSConstants.CONFIG ? reference.TargetGroup : -1,
                             reference.Id))
                        .ToHashSet();

                    foreach (DefinitionColumn column in linkColumns) {
                        DefinitionCellVisual visual = column.Visual!(row);
                        if (visual.Art != DefinitionCellArt.Link && visual.Art != DefinitionCellArt.Thumbnail)
                            continue;

                        links++;

                        Assert.True(
                            exported.Contains((visual.IndexId, visual.GroupId, visual.TargetId)),
                            "Index " + indexId + " " + address + ", column '" + column.Header +
                            "', draws a link to index " + visual.IndexId +
                            (visual.GroupId >= 0 ? " group " + visual.GroupId : "") +
                            " id " + visual.TargetId +
                            ", which CacheExportJoins does not make for this record. Either the" +
                            " column reads the wrong field, or it states a join that is not in the" +
                            " measured list - and a join earns its place by what the relation" +
                            " rejects, not by being plausible.");
                    }
                }
            }

            //Printed rather than asserted. The link count belongs to whichever cache produced it,
            //and the two disagree on eleven indexes.
            output.WriteLine("Index " + indexId + ": " + records.ToString("N0") + " records, " +
                links.ToString("N0") + " links drawn, all corroborated by CacheExportJoins.");

            Assert.True(links > 0, "Index " + indexId + " declares link columns and drew none, so" +
                " this swept nothing. Either every record stores -1 for those fields, which would be" +
                " a decoder regression, or the columns are reading a field the records do not carry.");
        }

        /// <summary>The grid descriptor for an index.</summary>
        /// <param name="indexId">The index.</param>
        /// <returns>The descriptor the tab binds.</returns>
        private static IDefinitionListDescriptor DescriptorFor(int indexId) {
            return indexId switch {
                RSConstants.OBJECTS_DEFINITIONS_INDEX => new ObjectListDescriptor(),
                RSConstants.GRAPHICS_INDEX => new GraphicListDescriptor(),
                RSConstants.CONFIG_BILLBOARD => new BillboardListDescriptor(),
                _ => throw new ArgumentOutOfRangeException(nameof(indexId), indexId,
                    "No link-bearing descriptor is registered for this index here.")
            };
        }
    }
}
