using System;
using System.Collections.Generic;
using System.Linq;
using FlashEditor.Cache;
using FlashEditor.Cache.Util;
using FlashEditor.Definitions;
using FlashEditor.Definitions.WorldMap;
using FlashEditor.Tests.Cache.RealCache;
using Xunit;
using Xunit.Abstractions;
using FlashEditor.IO;

namespace FlashEditor.Tests.Definitions
{
    /// <summary>
    ///     Decodes every file index 23's reference table declares, requires each to be consumed to
    ///     its last byte, and requires each to re-encode to the bytes it came from.
    /// </summary>
    /// <remarks>
    ///     The index holds three unrelated record families and none of them is addressable by
    ///     arithmetic, so the sweep is driven by the same name hashes the client uses: the
    ///     <c>details</c> group names every area, each area names its raster group and its
    ///     static-element group, and the raster file is found by name inside its group because its
    ///     id is not fixed. Every group and file the table declares has to fall out of that walk,
    ///     which is asserted rather than assumed - a family the naming rules cannot reach would
    ///     otherwise be skipped silently.
    ///     <para>
    ///     Comparison is against the decompressed payload throughout. Index 23 is BZip2-dominant
    ///     with a GZip third, and no GZip container in either cache re-encodes byte-identically, so
    ///     comparing stored containers would measure the compressor.
    ///     </para>
    /// </remarks>
    [Collection("RealCache")]
    public sealed class RealCacheWorldMapTests : IClassFixture<RealCacheFixture>
    {
        private readonly RealCacheFixture _fixture;
        private readonly ITestOutputHelper _output;

        /// <summary>Binds the shared open cache and the per-test output sink.</summary>
        public RealCacheWorldMapTests(RealCacheFixture fixture, ITestOutputHelper output)
        {
            _fixture = fixture;
            _output = output;
        }

        /// <summary>Which of the three families a group belongs to.</summary>
        private enum Family
        {
            /// <summary>The one group holding an area details record per file.</summary>
            Details,

            /// <summary>An area's group, holding its overview raster.</summary>
            Raster,

            /// <summary>An area's fixed-position map elements.</summary>
            StaticElements
        }

        /// <summary>Groups the index-23 reference table declares.</summary>
        private int GroupsDeclared => _fixture.DeclaredGroups(RSConstants.WORLD_MAP);

        /// <summary>Files the index-23 reference table declares across every group.</summary>
        private int FilesDeclared => _fixture.DeclaredFiles(RSConstants.WORLD_MAP);

        /// <summary>
        ///     Resolves every declared group to a family by name, the way the client reaches it.
        /// </summary>
        /// <remarks>
        ///     Built from the details group outwards, so nothing here knows a group id. The returned
        ///     map is asserted against the reference table's own group list by every caller.
        /// </remarks>
        /// <param name="cache">The open cache.</param>
        /// <param name="areas">The decoded area records.</param>
        /// <returns>The family of each group the naming rules reach.</returns>
        private static Dictionary<int, Family> GroupFamilies(RSCache cache,
            IReadOnlyList<WorldMapAreaDefinition> areas)
        {
            var families = new Dictionary<int, Family>();

            int details = WorldMapNaming.GroupIdFor(cache, WorldMapNaming.DetailsGroup);
            Assert.True(details >= 0, "index 23 has no group named 'details'");
            families[details] = Family.Details;

            foreach (WorldMapAreaDefinition area in areas)
            {
                int raster = WorldMapNaming.GroupIdFor(cache, area.InternalName);
                Assert.True(raster >= 0,
                    $"area {area.Id} names itself '{area.InternalName}' but no group hashes to it");
                families[raster] = Family.Raster;

                int elements = WorldMapNaming.GroupIdFor(cache,
                    WorldMapNaming.StaticElementGroupFor(area.InternalName));

                //Three areas have no static-element group and the client tolerates it, so an
                //absent group is an ordinary answer rather than a failure.
                if (elements >= 0)
                    families[elements] = Family.StaticElements;
            }

            return families;
        }

        /// <summary>
        ///     Every declared file decodes, lands on its last byte, and re-encodes unchanged.
        /// </summary>
        /// <remarks>
        ///     Exact consumption is stated as "the decode finished on the last byte" rather than "it
        ///     stopped on a terminator", because none of the three formats has one. The area raster
        ///     in particular reads blocks until the buffer runs out (<c>Class278.java:520</c>), so
        ///     appending sentinel padding changes what the format says the file contains and the
        ///     usual padded-decode check cannot be applied to it.
        /// </remarks>
        [RealCacheFact]
        public void EveryDeclaredFile_DecodesConsumesExactlyAndReEncodesToItsStoredBytes()
        {
            RSCache cache = _fixture.OpenCache();
            var reader = new WorldMapReader(cache);
            IReadOnlyList<WorldMapAreaDefinition> areas = reader.ReadAreas();
            Dictionary<int, Family> families = GroupFamilies(cache, areas);

            var declared = new SortedSet<int>(cache.EnumerateGroups(RSConstants.WORLD_MAP));
            Assert.True(declared.Count > 0, "index 23 declares no group, so nothing was checked");

            int[] unreachable = declared.Except(families.Keys).ToArray();
            Assert.True(unreachable.Length == 0,
                "groups " + string.Join(", ", unreachable) + " are declared but no world-map name " +
                "resolves to them, so the sweep would skip them");
            Assert.Equal(declared.Count, families.Count);
            Assert.Equal(GroupsDeclared, declared.Count);

            var failures = new List<string>();
            int files = 0;
            int detailFiles = 0;
            int rasterFiles = 0;
            int elementFiles = 0;
            long payloadBytes = 0;
            long tiles = 0;
            long zones = 0;

            foreach (int groupId in declared)
            {
                Family family = families[groupId];
                foreach (KeyValuePair<int, JagStream> file in cache.ReadGroup(RSConstants.WORLD_MAP, groupId))
                {
                    byte[] stored = file.Value.ToArray();
                    files++;
                    payloadBytes += stored.Length;

                    var reading = new JagStream(stored);
                    byte[] written = null;

                    try
                    {
                        switch (family)
                        {
                            case Family.Details:
                                {
                                    var area = new WorldMapAreaDefinition { Id = file.Key }.Decode(reading);
                                    zones += area.Zones.Count;
                                    written = area.Encode().ToArray();
                                    detailFiles++;
                                    break;
                                }

                            case Family.Raster:
                                {
                                    int named = WorldMapNaming.FileIdFor(cache, groupId, WorldMapNaming.RasterFile);
                                    Assert.True(named == file.Key,
                                        $"group {groupId} declares file {file.Key} but the name " +
                                        $"'{WorldMapNaming.RasterFile}' resolves to {named}");

                                    WorldMapAreaRaster raster = new WorldMapAreaRaster().Decode(reading);
                                    foreach (WorldMapRasterBlock block in raster.Blocks)
                                        tiles += block.Tiles.Length;
                                    written = raster.Encode().ToArray();
                                    rasterFiles++;
                                    break;
                                }

                            case Family.StaticElements:
                                {
                                    var element = new WorldMapElement { Id = file.Key }.Decode(reading);
                                    written = element.Encode().ToArray();
                                    elementFiles++;
                                    break;
                                }
                        }
                    }
                    catch (Exception ex)
                    {
                        failures.Add($"{family} group {groupId} file {file.Key}: " +
                                     $"{ex.GetType().Name} at {reading.Position} of {stored.Length}: {ex.Message}");
                        continue;
                    }

                    if (reading.Position != stored.Length)
                    {
                        failures.Add($"{family} group {groupId} file {file.Key}: consumed " +
                                     $"{reading.Position} of {stored.Length} bytes");
                        continue;
                    }

                    if (!written.AsSpan().SequenceEqual(stored))
                    {
                        failures.Add($"{family} group {groupId} file {file.Key}: re-encoded " +
                                     $"{written.Length} bytes from a stored {stored.Length}, first " +
                                     $"difference at {FirstDifference(stored, written)}");
                    }
                }
            }

            _output.WriteLine($"{files} files across {declared.Count} groups, {payloadBytes} bytes: " +
                              $"{detailFiles} area details holding {zones} zones, {rasterFiles} rasters " +
                              $"holding {tiles} tiles, {elementFiles} static elements");

            if (failures.Count > 0)
            {
                Assert.Fail($"{failures.Count} world-map files did not round-trip:" + Environment.NewLine +
                            string.Join(Environment.NewLine, failures.Take(20)));
            }

            Assert.True(FilesDeclared > 0, "index 23 declares no files, so nothing was checked");
            Assert.Equal(FilesDeclared, files);
            Assert.Equal(areas.Count, detailFiles);
            Assert.Equal(areas.Count, rasterFiles);
            Assert.True(elementFiles > 0, "no static element was read, so that family was not checked");
        }

        /// <summary>
        ///     The raster file's id is not fixed, so it has to be found by name inside its group.
        /// </summary>
        /// <remarks>
        ///     Stated as "more than one id occurs" rather than as a table of ids and counts, so it
        ///     holds in any cache. A reader that assumed a constant id would work on most areas and
        ///     fail on the rest, which is the failure mode worth pinning.
        /// </remarks>
        [RealCacheFact]
        public void TheRasterFileIdIsNotFixedAcrossAreas()
        {
            RSCache cache = _fixture.OpenCache();
            var reader = new WorldMapReader(cache);

            var ids = new SortedDictionary<int, int>();
            foreach (WorldMapAreaDefinition area in reader.ReadAreas())
            {
                int groupId = WorldMapNaming.GroupIdFor(cache, area.InternalName);
                int fileId = WorldMapNaming.FileIdFor(cache, groupId, WorldMapNaming.RasterFile);

                Assert.True(fileId >= 0,
                    $"group {groupId} ('{area.InternalName}') holds no file named " +
                    $"'{WorldMapNaming.RasterFile}'");

                ids.TryGetValue(fileId, out int seen);
                ids[fileId] = seen + 1;
            }

            _output.WriteLine("raster file ids: " +
                              string.Join(", ", ids.Select(entry => $"{entry.Key}={entry.Value}")));

            Assert.True(ids.Count > 1,
                "every area stores its raster under the same file id here, so nothing in this cache " +
                "punishes a reader that assumes one");
        }

        /// <summary>
        ///     A group resolves on the hash of its <b>lower-cased</b> name, and this index proves it.
        /// </summary>
        /// <remarks>
        ///     The cheapest self-proving case of the rule <c>AGENTS.md</c> states. Most names in the
        ///     cache are already lower case and resolve either way; here one area's details record
        ///     spells its own name with capitals, so the hash of the stored spelling matches no group
        ///     at all and the hash of the folded spelling matches exactly one.
        ///     <para>
        ///     Derived rather than named: the test finds whichever record is not already lower case,
        ///     so it keeps working if the cache in front of it spells a different one that way.
        ///     </para>
        /// </remarks>
        [RealCacheFact]
        public void AGroupResolvesOnTheHashOfItsLowerCasedName()
        {
            RSCache cache = _fixture.OpenCache();
            RSReferenceTable table = cache.GetReferenceTable(RSConstants.WORLD_MAP);
            var reader = new WorldMapReader(cache);

            var mixedCase = reader.ReadAreas()
                .Where(area => area.InternalName != area.InternalName.ToLowerInvariant())
                .ToArray();

            Assert.True(mixedCase.Length > 0,
                "every area name is already lower case in this cache, so the folding rule is not " +
                "exercised and this test proves nothing");

            foreach (WorldMapAreaDefinition area in mixedCase)
            {
                int folded = NameHasher.GetNameHash(area.InternalName);
                int verbatim = RawHash(area.InternalName);
                int groupId = table.GetArchiveId(area.InternalName);

                Assert.True(groupId >= 0, $"'{area.InternalName}' resolves to no group");
                Assert.Equal(folded, table.GetArchiveEntry(groupId).GetIdentifier());
                Assert.NotEqual(verbatim, folded);

                _output.WriteLine($"'{area.InternalName}': stored spelling hashes to {verbatim}, " +
                                  $"lower-cased to {folded}, which is group {groupId}'s identifier");
            }
        }

        /// <summary>
        ///     Both spellings of a terrain floor occur, and no rule over the decoded value tells
        ///     them apart.
        /// </summary>
        /// <remarks>
        ///     A terrain tile names its floor either as a six-bit index into the file's palette or
        ///     as the escape code 63 followed by the floor id as a literal byte. Both occur in
        ///     quantity, which is why the flag byte is stored rather than recomputed.
        ///     <para>
        ///     The measurement that matters is the last one: how many escapes carry a value that is
        ///     <i>also</i> in the same file's palette. That is the case an encoder choosing the
        ///     shorter spelling would get wrong, and it does not occur in either cache - so no sweep
        ///     over shipped bytes can defend the rule and
        ///     <c>WorldMapCodecTests.AnEscapedFloorThatThePaletteCouldExpressKeepsItsEscape</c>
        ///     pins it synthetically instead.
        ///     </para>
        /// </remarks>
        [RealCacheFact]
        public void BothTerrainSpellingsOccurAndTheAmbiguousCaseDoesNot()
        {
            RSCache cache = _fixture.OpenCache();
            var reader = new WorldMapReader(cache);

            long inline = 0;
            long escaped = 0;
            long blank = 0;
            long decorated = 0;
            long escapedButInlineable = 0;
            long countFlagWithNoElements = 0;
            long countFlagLevels = 0;

            foreach (WorldMapAreaDefinition area in reader.ReadAreas())
            {
                WorldMapAreaRaster raster = reader.ReadRaster(area.InternalName);
                Assert.NotNull(raster);

                foreach (WorldMapRasterBlock block in raster.Blocks)
                {
                    foreach (WorldMapTile tile in block.Tiles)
                    {
                        if (tile.IsDecorated)
                        {
                            decorated++;
                            if (!tile.CarriesElementCount)
                                continue;

                            foreach (WorldMapTileLevel level in tile.Levels)
                            {
                                countFlagLevels++;
                                if (level.Elements.Length == 0)
                                    countFlagWithNoElements++;
                            }
                            continue;
                        }

                        if (tile.IsBlank)
                        {
                            blank++;
                            continue;
                        }

                        if (!tile.UsesFloorLiteral)
                        {
                            inline++;
                            continue;
                        }

                        escaped++;
                        byte[] palette = tile.IsOverlay ? raster.OverlayPalette : raster.UnderlayPalette;
                        if (Array.IndexOf(palette, tile.StoredFloorLiteral) >= 0)
                            escapedButInlineable++;
                    }
                }
            }

            _output.WriteLine($"terrain tiles: {inline} inline, {escaped} escaped, {blank} blank; " +
                              $"{decorated} decorated");
            _output.WriteLine($"{escapedButInlineable} escapes store a floor the same file's palette " +
                              "already holds");
            _output.WriteLine($"{countFlagWithNoElements} of {countFlagLevels} levels carry an element " +
                              "count of zero");

            Assert.True(inline > 0, "no tile names its floor through the palette");
            Assert.True(escaped > 0,
                "no tile escapes to code 63, so the branch the flag byte exists to preserve is never " +
                "taken in this cache");
            Assert.True(blank > 0, "no tile is blank, so code 62 is never exercised");
            Assert.True(countFlagWithNoElements > 0,
                "the element-count flag is never set with a count of zero here, so an encoder that " +
                "derived the flag from the element list would sweep clean");
        }

        /// <summary>
        ///     A static element's id is a map element in config group 36, and cannot be an object id.
        /// </summary>
        /// <remarks>
        ///     Both halves are needed. Every id resolving into group 36 makes the join possible;
        ///     some of them being outside the object index at all is what makes it the <i>only</i>
        ///     possible one, and is the check that would have caught the reverse reading rather than
        ///     confirming it by coverage.
        /// </remarks>
        [RealCacheFact]
        public void EveryStaticElementNamesAMapElementAndNotAnObject()
        {
            RSCache cache = _fixture.OpenCache();
            var reader = new WorldMapReader(cache);

            var mapElements = new HashSet<int>(cache.GetFileIds(RSConstants.CONFIG, RSConstants.MAP_ELEMENT_GROUP));
            Assert.True(mapElements.Count > 0, "config group 36 declares no file, so the join is untestable");

            CacheAddressing objects = CacheAddressing.For(RSConstants.OBJECTS_DEFINITIONS_INDEX);
            var objectIds = new HashSet<int>(cache.EnumerateFiles(RSConstants.OBJECTS_DEFINITIONS_INDEX)
                .Select(pair => objects.DefinitionId(pair.Group, pair.File)));

            var ids = new SortedSet<int>();
            int elements = 0;
            int membersOnly = 0;

            foreach (WorldMapAreaDefinition area in reader.ReadAreas())
            {
                foreach (WorldMapElement element in reader.ReadStaticElements(area.InternalName))
                {
                    elements++;
                    ids.Add(element.MapElementId);
                    if (element.HiddenOnFreeWorlds)
                        membersOnly++;

                    Assert.True(mapElements.Contains(element.MapElementId),
                        $"'{area.InternalName}' element {element.Id} names {element.MapElementId}, " +
                        "which config group 36 does not declare");
                }
            }

            int notAnObject = ids.Count(id => !objectIds.Contains(id));
            _output.WriteLine($"{elements} static elements naming {ids.Count} distinct map elements, " +
                              $"{ids.Min}..{ids.Max} against {mapElements.Count} declared; " +
                              $"{membersOnly} are members only");
            _output.WriteLine($"{notAnObject} of those ids are not object ids at all");

            Assert.True(elements > 0, "no static element was read, so nothing was checked");
            Assert.True(notAnObject > 0,
                "every static element id happens to be a valid object id too, so this cache cannot " +
                "tell the two readings apart");
        }

        /// <summary>
        ///     A tile element's id is an object definition, and mostly cannot be a map element.
        /// </summary>
        /// <remarks>
        ///     The mirror of the static-element join and the correction it forced: the tile stream
        ///     and the static-element records both store a 16-bit id and they point at different
        ///     indexes. The client resolves this one through the object provider
        ///     (<c>Class302.method3546</c>, Class302.java:84) and then reads the object's own icon
        ///     fields - opcode 102 at <c>Class278.java:871</c> and opcode 107 at <c>:84</c>.
        ///     <para>
        ///     Coverage alone would not have settled it, since a map-element reading also "resolves"
        ///     for the low ids. What settles it is that most of these ids are outside config group
        ///     36 entirely while every one of them is a declared object.
        ///     </para>
        /// </remarks>
        [RealCacheFact]
        public void EveryTileElementNamesAnObjectRatherThanAMapElement()
        {
            RSCache cache = _fixture.OpenCache();
            var reader = new WorldMapReader(cache);

            var ids = new SortedSet<int>();
            long references = 0;

            foreach (WorldMapAreaDefinition area in reader.ReadAreas())
            {
                WorldMapAreaRaster raster = reader.ReadRaster(area.InternalName);
                Assert.NotNull(raster);

                foreach (WorldMapRasterBlock block in raster.Blocks)
                {
                    foreach (WorldMapTile tile in block.Tiles)
                    {
                        if (!tile.IsDecorated)
                            continue;

                        foreach (WorldMapTileLevel level in tile.Levels)
                        {
                            foreach (WorldMapTileElement element in level.Elements)
                            {
                                references++;
                                ids.Add(element.ObjectId);
                            }
                        }
                    }
                }
            }

            Assert.True(ids.Count > 0, "no tile names an element, so the join was not exercised");

            CacheAddressing objects = CacheAddressing.For(RSConstants.OBJECTS_DEFINITIONS_INDEX);
            var objectIds = new HashSet<int>(cache.EnumerateFiles(RSConstants.OBJECTS_DEFINITIONS_INDEX)
                .Select(pair => objects.DefinitionId(pair.Group, pair.File)));
            var mapElements = new HashSet<int>(cache.GetFileIds(RSConstants.CONFIG, RSConstants.MAP_ELEMENT_GROUP));

            int[] notAnObject = ids.Where(id => !objectIds.Contains(id)).ToArray();
            int outsideMapElements = ids.Count(id => !mapElements.Contains(id));

            Assert.True(notAnObject.Length == 0,
                notAnObject.Length + " tile element ids are not declared objects: " +
                string.Join(", ", notAnObject.Take(20)));
            Assert.True(outsideMapElements > 0,
                "every tile element id is also a valid map element id, so this cache cannot tell " +
                "the two readings apart");

            //Following the client: an object with no icon of its own may still get one from the
            //morph variant it resolves to, so the two are counted apart rather than summed.
            int withIcon = 0;
            int withMorphs = 0;
            int withNeither = 0;

            foreach (IGrouping<int, int> group in ids.GroupBy(id => objects.GroupOf(id)))
            {
                IReadOnlyDictionary<int, JagStream> files =
                    cache.ReadGroup(RSConstants.OBJECTS_DEFINITIONS_INDEX, group.Key);

                foreach (int id in group)
                {
                    JagStream payload = files[objects.FileOf(id)];
                    payload.Seek0();
                    ObjectDefinition definition = ObjectDefinition.DecodeFromStream(payload);

                    if (definition.mapSceneIcon >= 0 || definition.mapElementId >= 0)
                        withIcon++;
                    else if (definition.morphIds != null)
                        withMorphs++;
                    else
                        withNeither++;
                }
            }

            _output.WriteLine($"{references} tile element references naming {ids.Count} distinct " +
                              $"objects, {ids.Min}..{ids.Max}; {outsideMapElements} of them are not " +
                              "map element ids at all");
            _output.WriteLine($"{withIcon} carry a map icon of their own, {withMorphs} resolve one " +
                              $"through a morph list, {withNeither} carry neither");

            Assert.True(withIcon > 0, "not one referenced object carries a map icon, so the id cannot " +
                                      "be an object id after all");
        }

        /// <summary>
        ///     The two details bytes that cannot be recovered from what the client keeps are decoded.
        /// </summary>
        /// <remarks>
        ///     The zoom byte is aliased - a stored 255 becomes 0 at
        ///     <c>Node_Sub46_Sub10.java:483-485</c> - and the eighth constructor argument is read and
        ///     dropped. Both are pinned by the byte-identity sweep as long as the cache exercises
        ///     them, so this test's job is to say whether it does: the aliased zoom occurs and the
        ///     dropped byte is zero everywhere, which is exactly the case
        ///     <c>WorldMapCodecTests</c> has to cover synthetically.
        /// </remarks>
        [RealCacheFact]
        public void TheAliasedZoomOccursAndTheDroppedByteDoesNot()
        {
            var reader = new WorldMapReader(_fixture.OpenCache());

            var zooms = new SortedDictionary<int, int>();
            int aliasedZoom = 0;
            int nonZeroDroppedByte = 0;
            int enabled = 0;
            int tinted = 0;

            IReadOnlyList<WorldMapAreaDefinition> areas = reader.ReadAreas();
            foreach (WorldMapAreaDefinition area in areas)
            {
                zooms.TryGetValue(area.StoredZoom, out int seen);
                zooms[area.StoredZoom] = seen + 1;

                if (area.StoredZoom == WorldMapAreaDefinition.ZoomStoredAsZero)
                {
                    aliasedZoom++;
                    Assert.Equal(0, area.Zoom);
                }

                if (area.UnreadByte != 0)
                    nonZeroDroppedByte++;
                if (area.Enabled)
                    enabled++;
                if (area.TintColour != -1)
                    tinted++;
            }

            _output.WriteLine("stored zoom values: " +
                              string.Join(", ", zooms.Select(entry => $"{entry.Key}={entry.Value}")));
            _output.WriteLine($"{aliasedZoom} areas store the zoom the client folds to 0; " +
                              $"{nonZeroDroppedByte} carry a non-zero dropped byte; " +
                              $"{enabled} of {areas.Count} are enabled, {tinted} carry a tint");

            Assert.True(aliasedZoom > 0,
                "no area stores the aliased zoom, so the byte-identity sweep does not defend it");
            Assert.True(nonZeroDroppedByte == 0,
                nonZeroDroppedByte + " areas carry a non-zero dropped byte, which no cache measured " +
                "here does. That is new information rather than a defect: the byte-identity sweep " +
                "now defends the field on its own, so record the figure and delete this assertion.");
        }

        /// <summary>Where two byte runs first disagree, for a failure line.</summary>
        /// <param name="expected">The stored bytes.</param>
        /// <param name="actual">The re-encoded bytes.</param>
        /// <returns>The offset of the first difference, or the shorter length when one is a prefix.</returns>
        private static int FirstDifference(byte[] expected, byte[] actual)
        {
            int shared = Math.Min(expected.Length, actual.Length);
            for (int i = 0; i < shared; i++)
                if (expected[i] != actual[i])
                    return i;
            return shared;
        }

        /// <summary>
        ///     The name hash without the case folding, for the comparison that proves the folding.
        /// </summary>
        /// <remarks>
        ///     Written out here rather than reached through a flag on the production hasher, so
        ///     nothing in the editor can ever be asked for a hash the cache does not use.
        /// </remarks>
        /// <param name="name">The name, hashed exactly as spelled.</param>
        /// <returns>The 32-bit hash.</returns>
        private static int RawHash(string name)
        {
            unchecked
            {
                int hash = 0;
                foreach (char c in name)
                    hash = c + ((hash << 5) - hash);
                return hash;
            }
        }
    }
}
