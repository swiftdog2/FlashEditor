using System;
using System.Collections.Generic;
using System.Linq;
using FlashEditor.cache;
using FlashEditor.Cache.Util;
using FlashEditor.Definitions.Config;
using FlashEditor.Definitions.WorldMap;
using FlashEditor.Tests.Cache.RealCache;
using Xunit;
using Xunit.Abstractions;

namespace FlashEditor.Tests.Definitions
{
    /// <summary>
    ///     Settles the join the World Map Overview tab draws its icons through: a static element's
    ///     16-bit id against a file of config group 36.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///     <b>Why this is a separate suite from the byte-identity sweep.</b>
    ///     <c>RealCacheWorldMapTests.EveryStaticElementNamesAMapElementAndNotAnObject</c> already
    ///     shows that every id lands on a declared file of group 36 and that some of them are not
    ///     object ids, which rules out the other reading of the same two bytes. Both statements are
    ///     aggregates, and this cache's own history says an aggregate is the easiest thing here to
    ///     satisfy by accident: the track-name join matched 958 of 970 keys and was wrong, because
    ///     coverage cannot tell "resolves" from "resolves to the right record".
    ///     </para>
    ///     <para>
    ///     So the claim tested here is stronger and narrower. It is not that the ids resolve; it is
    ///     that the record each id resolves to <i>describes the place the icon was placed at</i>,
    ///     shown on rows that are checkable one at a time and falsified by shifting the join.
    ///     </para>
    /// </remarks>
    [Collection("RealCache")]
    public sealed class RealCacheWorldMapIconJoinTests : IClassFixture<RealCacheFixture>
    {
        /// <summary>How far either side of the real join the falsification sweep runs.</summary>
        /// <remarks>
        ///     Eight is well past any plausible off-by-one and still cheap. The point is not the
        ///     width: it is that the self-proving rows exist at exactly one offset and nowhere else,
        ///     which a single +1 probe could not establish.
        /// </remarks>
        private const int FalsificationReach = 8;

        private readonly RealCacheFixture _fixture;
        private readonly ITestOutputHelper _output;

        /// <summary>Binds the shared open cache and the per-test output sink.</summary>
        public RealCacheWorldMapIconJoinTests(RealCacheFixture fixture, ITestOutputHelper output)
        {
            _fixture = fixture;
            _output = output;
        }

        /// <summary>
        ///     Every id a static element names is a group-36 file that actually decodes.
        /// </summary>
        /// <remarks>
        ///     The weaker half of the proof, and stated as such. "Is a declared file id" only says
        ///     the id is in range; decoding each one is what says the join lands on a record the
        ///     editor can draw, which is the claim the tab makes when it puts an icon on the raster.
        ///     The denominators are read from the cache in front of it, because index 2's group 36
        ///     is one of the families that has to be measured rather than remembered.
        /// </remarks>
        [RealCacheFact]
        public void EveryStaticElementIdDecodesAsAMapElementDefinition()
        {
            RSCache cache = _fixture.OpenCache();
            Dictionary<int, MapElementDefinition> elements = MapElements(cache);
            var reader = new WorldMapReader(cache);

            int[] declared = cache.GetFileIds(RSConstants.CONFIG, RSConstants.MAP_ELEMENT_GROUP);
            Assert.True(declared.Length > 0, "config group 36 declares no file, so the join is untestable");

            var used = new SortedSet<int>();
            int placements = 0;
            foreach (WorldMapAreaDefinition area in reader.ReadAreas())
            {
                foreach (WorldMapElement element in reader.ReadStaticElements(area.InternalName))
                {
                    placements++;
                    used.Add(element.MapElementId);

                    Assert.True(elements.ContainsKey(element.MapElementId),
                        $"'{area.InternalName}' element {element.Id} names map element " +
                        $"{element.MapElementId}, which config group 36 does not hold");
                }
            }

            //Ids are dense here, so "id" and "position in the id list" are the same number and this
            //cache cannot tell those two readings apart at all. Said out loud rather than left for a
            //reader to assume the sweep covered it - that ambiguity is exactly what made the
            //track-name join look right.
            bool dense = declared.Length == declared.Max() + 1 && declared.Min() == 0;

            _output.WriteLine($"{placements} placements naming {used.Count} distinct map elements, " +
                              $"{used.Min}..{used.Max}, against {declared.Length} declared in group 36 " +
                              $"({declared.Min()}..{declared.Max()}, " +
                              (dense ? "dense" : "sparse") + ")");
            _output.WriteLine($"{elements.Values.Count(e => e.Label != null)} of the {elements.Count} " +
                              $"records carry a label and {elements.Values.Count(e => e.SpriteId != -1)} a sprite");

            Assert.True(placements > 0, "no static element was read, so nothing was checked");
            Assert.True(dense,
                "group 36's file ids are no longer dense here. That is worth knowing rather than " +
                "tolerating: while they are dense, an id and its position in the id list are the same " +
                "number, so no measurement in this cache can tell those two readings apart.");
        }

        /// <summary>
        ///     Two areas are each confirmed by a row that proves itself, and only at the real offset.
        /// </summary>
        /// <remarks>
        ///     <para>
        ///     A row proves itself when two routes that share nothing agree about one place. Index 23
        ///     declares an area group whose <b>name hash</b> resolves it, and that area's details
        ///     record spells out a display name and the world rectangles the area copies its tiles
        ///     from. Config group 36, an entirely different index reached by file id, gives the icon
        ///     a label. Where a placement on one area carries the exact display name of a
        ///     <i>different</i> area, and the icon sits inside that other area's own source rectangle,
        ///     the two routes have agreed on both the name and the position of one place. Measured
        ///     here: the surface's God Wars Dungeon and Troll Stronghold icons each sit inside the
        ///     x span the area of that name copies its own tiles from.
        ///     </para>
        ///     <para>
        ///     That is two rows out of 965 placements, and deliberately so. High coverage is what the
        ///     track-name join had, and it was still wrong. What it lacked was a row that falsifies a
        ///     wrong reading on its own.
        ///     </para>
        ///     <para>
        ///     <b>The statistic is the number of distinct areas confirmed, not the number of rows,
        ///     and that is not a convenience.</b> A single row reappears at offset +8, where a
        ///     <i>different</i> surface placement lands on the same "Troll Stronghold" record and is
        ///     also inside it - because half a dozen icons cluster around one dungeon entrance, so
        ///     shifting every id by the same amount reliably slides a neighbour onto the same record
        ///     without moving it out of the area. One agreement is therefore expected noise. Two
        ///     agreements naming two unrelated areas are not, and offset zero is the only offset in
        ///     <see cref="FalsificationReach"/> either side that produces them.
        ///     </para>
        ///     <para>
        ///     Only the x span is required. The named areas are underground and are copied from a
        ///     different y band than the surface entrance that names them - Troll Stronghold sits
        ///     6400 tiles north of its entrance, God Wars 1600 - and those offsets are per area, so
        ///     requiring y would be requiring a convention this cache does not state.
        ///     </para>
        /// </remarks>
        [RealCacheFact]
        public void TwoAreasAreConfirmedByTheirOwnIconAndOnlyAtTheRealOffset()
        {
            RSCache cache = _fixture.OpenCache();
            Dictionary<int, MapElementDefinition> elements = MapElements(cache);
            var reader = new WorldMapReader(cache);

            IReadOnlyList<WorldMapAreaDefinition> areas = reader.ReadAreas();
            var byDisplayName = new Dictionary<string, WorldMapAreaDefinition>(StringComparer.Ordinal);
            foreach (WorldMapAreaDefinition area in areas)
                byDisplayName[area.DisplayName] = area;

            //Read once. ReadStaticElements walks a group per call and the sweep below runs the whole
            //join 17 times over.
            var placed = new List<(WorldMapAreaDefinition Host, WorldMapElement Element)>();
            foreach (WorldMapAreaDefinition area in areas)
                foreach (WorldMapElement element in reader.ReadStaticElements(area.InternalName))
                    placed.Add((area, element));

            var confirmed = new Dictionary<int, SortedSet<int>>();

            for (int offset = -FalsificationReach; offset <= FalsificationReach; offset++)
            {
                var rows = new List<string>();
                var areasConfirmed = new SortedSet<int>();
                int named = 0;

                foreach ((WorldMapAreaDefinition host, WorldMapElement element) in placed)
                {
                    if (!elements.TryGetValue(element.MapElementId + offset, out MapElementDefinition? definition))
                        continue;

                    string label = Flatten(definition.Label);
                    if (label.Length == 0 ||
                        !byDisplayName.TryGetValue(label, out WorldMapAreaDefinition? target) ||
                        ReferenceEquals(target, host) || target.Zones.Count == 0)
                        continue;

                    named++;

                    int minX = target.Zones.Min(zone => zone.SourceMinX);
                    int maxX = target.Zones.Max(zone => zone.SourceMaxX);
                    if (element.X < minX || element.X > maxX)
                        continue;

                    areasConfirmed.Add(target.Id);
                    rows.Add($"'{host.DisplayName}' element {element.Id} names map element " +
                             $"{element.MapElementId + offset} labelled '{label}', and sits at x " +
                             $"{element.X}, inside area {target.Id} '{target.InternalName}' " +
                             $"(source x {minX}..{maxX})");
                }

                confirmed[offset] = areasConfirmed;
                _output.WriteLine($"offset {offset,3}: {named} placements labelled with another area's " +
                                  $"exact display name, {rows.Count} of them inside that area, " +
                                  $"confirming {areasConfirmed.Count} distinct area(s)");
                foreach (string row in rows)
                    _output.WriteLine("    " + row);
            }

            Assert.True(confirmed[0].Count >= 2,
                "fewer than two areas are confirmed by an icon that names them and sits inside them, " +
                "so this cache offers no self-proving row for the join and the only evidence left is " +
                "coverage - which this project has already been wrong about once");

            int[] asGood = confirmed
                .Where(entry => entry.Key != 0 && entry.Value.Count >= confirmed[0].Count)
                .Select(entry => entry.Key)
                .ToArray();

            Assert.True(asGood.Length == 0,
                "offsets " + string.Join(", ", asGood) + " confirm as many areas as the real join " +
                "does, so the agreement at offset zero is not evidence for it either");
        }

        /// <summary>
        ///     The label the tab shows survives the round trip that would let a user edit it.
        /// </summary>
        /// <remarks>
        ///     The tab reads an icon's label out of group 36 and shows it beside the raster. That is
        ///     only safe while the record it reads re-encodes to its stored bytes, because a decoder
        ///     that quietly dropped an opcode would show a label taken from a record it cannot write
        ///     back. Scoped to the records the world map actually reaches rather than to the whole
        ///     group, so the failure it reports names an icon on the map.
        /// </remarks>
        [RealCacheFact]
        public void EveryMapElementTheWorldMapReachesReEncodesToItsStoredBytes()
        {
            RSCache cache = _fixture.OpenCache();
            var reader = new WorldMapReader(cache);

            IReadOnlyDictionary<int, JagStream> stored =
                cache.ReadGroup(RSConstants.CONFIG, RSConstants.MAP_ELEMENT_GROUP);

            var used = new SortedSet<int>();
            foreach (WorldMapAreaDefinition area in reader.ReadAreas())
                foreach (WorldMapElement element in reader.ReadStaticElements(area.InternalName))
                    used.Add(element.MapElementId);

            var failures = new List<string>();
            foreach (int id in used)
            {
                JagStream payload = stored[id];
                payload.Seek0();
                byte[] source = payload.ToArray();

                payload.Seek0();
                var definition = new MapElementDefinition { Id = id };
                definition.Decode(payload);
                byte[] written = definition.Encode().ToArray();

                if (!written.AsSpan().SequenceEqual(source))
                    failures.Add($"map element {id}: re-encoded {written.Length} bytes from a stored " +
                                 $"{source.Length}");
            }

            _output.WriteLine($"{used.Count} map elements are reachable from the world map; " +
                              $"{used.Count - failures.Count} re-encode to their stored bytes");

            Assert.True(used.Count > 0, "no map element was reached, so nothing was checked");
            Assert.Empty(failures);
        }

        /// <summary>
        ///     A label as the tab renders it: the client's own line break folded to a space.
        /// </summary>
        /// <remarks>
        ///     The stored labels carry <c>&lt;br&gt;</c> where the client wraps them
        ///     (Node_Sub40.java:154-158), so "Troll&lt;br&gt;Stronghold" and the area display name
        ///     "Troll Stronghold" are the same words differently laid out. Folding is part of the
        ///     comparison rather than a convenience - without it the two routes cannot be compared at
        ///     all and the self-proving rows vanish for a typographic reason.
        /// </remarks>
        /// <param name="label">The stored label, or null.</param>
        /// <returns>The label on one line.</returns>
        private static string Flatten(string? label)
        {
            return label == null ? string.Empty : label.Replace("<br>", " ").Trim();
        }

        /// <summary>Every record of config group 36, decoded once.</summary>
        /// <param name="cache">The open cache.</param>
        /// <returns>The definitions, by file id.</returns>
        private static Dictionary<int, MapElementDefinition> MapElements(RSCache cache)
        {
            var elements = new Dictionary<int, MapElementDefinition>();

            foreach (KeyValuePair<int, JagStream> file in
                     cache.ReadGroup(RSConstants.CONFIG, RSConstants.MAP_ELEMENT_GROUP))
            {
                file.Value.Seek0();
                var definition = new MapElementDefinition { Id = file.Key };
                definition.Decode(file.Value);
                elements[file.Key] = definition;
            }

            return elements;
        }
    }
}
