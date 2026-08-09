using System;
using System.Collections.Generic;
using System.Linq;
using FlashEditor.Cache;
using FlashEditor.Cache.Util;
using FlashEditor.Definitions.Config;
using FlashEditor.Definitions.WorldMap;
using FlashEditor.Tests.Cache.RealCache;
using Xunit;
using Xunit.Abstractions;
using FlashEditor.IO;

namespace FlashEditor.Tests.Definitions
{
    /// <summary>
    ///     Settles the join the World Map Overview tab draws its icons through: a static element's
    ///     16-bit id against a file of config group 36.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///     <b>The client settles the join outright, so nothing here has to argue it from coverage.</b>
    ///     The chain is unbroken and every link is a literal:
    ///     </para>
    ///     <list type="number">
    ///     <item><c>Class52.method491</c> reads one index-23 static-element file as
    ///     <c>readInt()</c>, <c>readUnsignedShort()</c>, <c>readUnsignedByte()</c>
    ///     (Class52.java:89-91) and parks the short in <c>anIntArray3138</c> (:96). That is the
    ///     seven-byte record <see cref="WorldMapElement"/> decodes.</item>
    ///     <item><c>Class278.method3302</c> hands that short, unmodified, to
    ///     <c>new Node_Sub47(...)</c> (Class278.java:476), which stores it as <c>anInt4268</c>
    ///     (Node_Sub47.java:57-61).</item>
    ///     <item>Every consumer passes <c>anInt4268</c> straight to
    ///     <c>Class341.method3807</c> - the world-map draw loop (Class86.java:36), the highlight
    ///     pass (Class202.java:228), the visibility gate (Particle_Sub3.java:19,
    ///     Class256_Sub1.java:54) and the right-click builder (Particle_Sub4.java:66-67).</item>
    ///     <item><c>Class341.method3807</c> resolves it as
    ///     <c>aJS5Archive_2855.getChildFromFolder(36, i_0_)</c> (Class341.java:185), where
    ///     <c>aJS5Archive_2855</c> is the archive the constructor was handed (:140) and
    ///     InterfaceSettings.java:273-274 constructs it with <c>client.BIT_CONFIG</c>, opened as
    ///     JS5 index 2 at InterfaceSettings.java:160. <c>getChildFromFolder</c> is a plain
    ///     <c>(group, file)</c> accessor (JS5Archive.java:203-205).</item>
    ///     </list>
    ///     <para>
    ///     So the group is the literal constant <b>36</b> and the file is the raw 16-bit value, with
    ///     no arithmetic and no enumeration anywhere on the path. <b>That answers id-versus-position
    ///     directly, which is what the track-name join lacked.</b> Two further statements in the same
    ///     client corroborate it: <c>method3807</c> writes the value back onto the definition as its
    ///     identity (<c>class24.anInt228 = i_0_</c>, Class341.java:189) and the world map's hide-list
    ///     is keyed on that field (ModelParticle.java:73); and CS2 opcodes 6800-6804 pop a value off
    ///     the script stack and feed it to the same method (Class247.java:7263-7323), which is a
    ///     definition id by construction.
    ///     </para>
    ///     <para>
    ///     The contrast is what makes the absence of arithmetic meaningful rather than an oversight.
    ///     This client <i>does</i> split an id into group and file for the config types that own a
    ///     whole index - locations at Class302.java:96 through <c>za.java:19</c>
    ///     (<c>i &gt;&gt;&gt; 8</c>) and <c>Class151.java:27</c> (<c>i &amp; 0xff</c>), items at
    ///     Class205.java:217, enums at Class29.java:239. None of that appears anywhere near
    ///     <c>Class341</c>.
    ///     </para>
    ///     <para>
    ///     <b>What this suite is for, now that the client has answered.</b> It pins our decoders to
    ///     that path: that every id in this cache resolves through it, that the record each id
    ///     reaches survives the round trip an edit would put it through, and that the placement's own
    ///     coordinates are unpacked the way the geometry declared elsewhere in index 23 requires.
    ///     </para>
    /// </remarks>
    [Collection("RealCache")]
    public sealed class RealCacheWorldMapIconJoinTests : IClassFixture<RealCacheFixture>
    {
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
        ///     Coverage, and stated as coverage. It says the join is total in this cache - no icon on
        ///     the tab resolves to nothing - which is worth pinning because the client tolerates a
        ///     miss (<c>if(is != null)</c>, Class341.java:192) and would draw a blank rather than
        ///     fail. It is not, on its own, evidence about <i>which</i> record an id reaches; that is
        ///     settled by the client chain in this class's own remarks.
        ///     <para>
        ///     The density check is kept as a loud tripwire rather than as an argument. While
        ///     group 36's ids are dense from zero, an id and its position in the id list are the same
        ///     number, so no measurement in this cache can tell those two readings apart - and the
        ///     day that stops being true is the day a measurement could, which is worth being told
        ///     about.
        ///     </para>
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
        ///     Every placement lands inside a rectangle its own area declares it copies tiles from.
        /// </summary>
        /// <remarks>
        ///     <para>
        ///     The full-coverage half of this suite, and the one with a control. The client builds a
        ///     <c>Node_Sub47</c> - and therefore performs the group-36 lookup at all - only for a
        ///     placement whose packed position falls inside one of its area's source rectangles:
        ///     <c>Class278.java:473-475</c> gates on <c>method1573</c>, which walks the area's zones
        ///     and tests plane, x and y (<c>Node_Sub6.method976</c>, Node_Sub6.java:105-106). So this
        ///     is the precondition of the join, asserted on every placement rather than on a sample.
        ///     </para>
        ///     <para>
        ///     <b>It settles a real ambiguity that the client cannot.</b> The call at
        ///     Class278.java:473-474 passes <c>packed &amp; 0x3fff</c> and
        ///     <c>packed &gt;&gt; 14 &amp; 0x3fff</c> into an obfuscated parameter list, so reading
        ///     the argument order off that line cannot say which half is x. The data can, because the
        ///     zones are a second, independent statement of where these places are: taking the high
        ///     half as x puts every placement inside its own area, and swapping the two halves does
        ///     not. That is the case in which the cache decides and the client is silent.
        ///     </para>
        ///     <para>
        ///     The swapped reading is asserted to be strictly worse rather than merely different,
        ///     because a control that passes either way is not a control. It is not asserted to be
        ///     near zero: many source rectangles are square and sit close enough to the diagonal that
        ///     a swap still lands inside one, which is exactly why the gap and not the absolute
        ///     number is the evidence.
        ///     </para>
        /// </remarks>
        [RealCacheFact]
        public void EveryPlacementSitsInsideItsOwnAreasSourceRectangle()
        {
            RSCache cache = _fixture.OpenCache();
            IReadOnlyList<(WorldMapAreaDefinition Host, WorldMapElement Element)> placed = Placements(cache);

            var outside = new List<string>();
            int swapped = 0;

            foreach ((WorldMapAreaDefinition host, WorldMapElement element) in placed)
            {
                if (host.Zones.Count == 0)
                {
                    outside.Add($"'{host.InternalName}' element {element.Id} has a placement but its " +
                                "area declares no zone at all, so the client would draw nothing");
                    continue;
                }

                bool inside = host.Zones.Any(zone =>
                    zone.Plane == element.Plane &&
                    element.X >= zone.SourceMinX && element.X <= zone.SourceMaxX &&
                    element.Y >= zone.SourceMinY && element.Y <= zone.SourceMaxY);

                if (!inside)
                    outside.Add($"'{host.InternalName}' element {element.Id} sits at plane " +
                                $"{element.Plane} ({element.X},{element.Y}), which is outside every " +
                                "rectangle that area copies its tiles from");

                if (host.Zones.Any(zone =>
                        element.Y >= zone.SourceMinX && element.Y <= zone.SourceMaxX &&
                        element.X >= zone.SourceMinY && element.X <= zone.SourceMaxY))
                    swapped++;
            }

            _output.WriteLine($"{placed.Count} placements, {placed.Count - outside.Count} inside their " +
                              $"own area on plane, x and y; the same test with the two coordinate " +
                              $"halves swapped puts {swapped} inside");

            Assert.NotEmpty(placed);
            Assert.Empty(outside);
            Assert.True(swapped < placed.Count,
                $"swapping the packed position's two halves puts all {swapped} placements inside their " +
                "area as well, so this test cannot tell the right unpacking from the wrong one and is " +
                "no longer evidence for either");
        }

        /// <summary>
        ///     Coverage narrows the target to four groups of index 2 and cannot pick between them.
        /// </summary>
        /// <remarks>
        ///     <para>
        ///     Kept because it draws the line in the right place. Of index 2's groups, more than one
        ///     declares a file for every id the world map uses - measured here, groups 16, 18, 26 and
        ///     36 all do - so "every id resolves" is satisfied by four different readings of the same
        ///     two bytes and is not evidence for any of them. What picks group 36 is
        ///     <c>getChildFromFolder(36, i_0_)</c> at Class341.java:185, a literal in the client.
        ///     </para>
        ///     <para>
        ///     <b>Do not put an offset sweep back here.</b> An earlier version of this suite swept
        ///     the join at offsets -8..+8 and treated "two areas confirmed at offset zero, one or
        ///     none elsewhere" as a discriminator. Measured over -16..+16, it is not one. The
        ///     ancient_cavern area places eleven icons inside its own mapped rectangle on
        ///     consecutive-ish ids around 726, so <i>every</i> offset from -9 to +1 slides one of
        ///     them onto record 726 and confirms that area on both axes; the x-only predicate the
        ///     old test used confirmed an area at +8 as well. Clustered placements on clustered ids
        ///     defeat a shift sweep by construction, whatever the predicate, and a threshold of two
        ///     against one was noise reading as proof.
        ///     </para>
        ///     <para>
        ///     The rows that read as self-proving are printed rather than asserted, for the same
        ///     reason: a placement whose group-36 label is the exact display name of an area, sitting
        ///     inside that area's own source span, is worth a reader's attention and is not worth an
        ///     assertion, because four such rows exist and one of them - the Kalphite Hive icon at
        ///     the surface entrance - is outside the area it names.
        ///     </para>
        /// </remarks>
        [RealCacheFact]
        public void CoverageAloneCannotSayWhichConfigGroupTheIdAddresses()
        {
            RSCache cache = _fixture.OpenCache();
            IReadOnlyList<(WorldMapAreaDefinition Host, WorldMapElement Element)> placed = Placements(cache);
            var used = new SortedSet<int>(placed.Select(entry => entry.Element.MapElementId));

            var candidates = new List<int>();
            foreach (int groupId in cache.EnumerateGroups(RSConstants.CONFIG).OrderBy(id => id))
            {
                var declared = new HashSet<int>(cache.GetFileIds(RSConstants.CONFIG, groupId));
                if (used.All(declared.Contains))
                    candidates.Add(groupId);
            }

            _output.WriteLine($"{used.Count} distinct ids, {used.Min}..{used.Max}; index 2 groups " +
                              $"declaring a file for every one of them: " +
                              string.Join(", ", candidates));

            PrintTheRowsThatReadAsSelfProving(cache, placed);

            Assert.Contains(RSConstants.MAP_ELEMENT_GROUP, candidates);
            Assert.True(candidates.Count > 1,
                "only config group 36 can hold every id the world map uses, so coverage now does " +
                "discriminate and this test is understating the evidence. Say so rather than " +
                "leaving the remark that it cannot.");
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
        ///     Prints every placement whose label is an area's display name, and whether it is inside.
        /// </summary>
        /// <remarks>
        ///     Output rather than assertion. These are the rows two independent routes agree on - a
        ///     name hash into index 23 on one side, a file id into index 2 on the other - and they
        ///     are the most a reader can check by hand, which is why they are worth printing. They
        ///     are also only four rows out of hundreds, and one of them disagrees on position, so
        ///     they cannot carry the claim on their own.
        /// </remarks>
        /// <param name="cache">The open cache.</param>
        /// <param name="placed">Every placement with the area whose group holds it.</param>
        private void PrintTheRowsThatReadAsSelfProving(
            RSCache cache,
            IReadOnlyList<(WorldMapAreaDefinition Host, WorldMapElement Element)> placed)
        {
            Dictionary<int, MapElementDefinition> elements = MapElements(cache);

            var byDisplayName = new Dictionary<string, WorldMapAreaDefinition>(StringComparer.Ordinal);
            foreach (WorldMapAreaDefinition area in new WorldMapReader(cache).ReadAreas())
                byDisplayName[area.DisplayName] = area;

            foreach ((WorldMapAreaDefinition host, WorldMapElement element) in placed)
            {
                if (!elements.TryGetValue(element.MapElementId, out MapElementDefinition definition))
                    continue;

                string label = Flatten(definition.Label);
                if (label.Length == 0 ||
                    !byDisplayName.TryGetValue(label, out WorldMapAreaDefinition target) ||
                    target.Zones.Count == 0)
                    continue;

                bool insideX = target.Zones.Any(zone =>
                    element.X >= zone.SourceMinX && element.X <= zone.SourceMaxX);
                bool insideY = target.Zones.Any(zone =>
                    element.Y >= zone.SourceMinY && element.Y <= zone.SourceMaxY);

                _output.WriteLine($"    '{host.InternalName}' element {element.Id} at " +
                                  $"({element.X},{element.Y}) names record {element.MapElementId} " +
                                  $"labelled '{label}', which is area {target.Id} " +
                                  $"'{target.InternalName}' - inside it in x: {insideX}, in y: {insideY}");
            }
        }

        /// <summary>
        ///     A label as the tab renders it: the client's own line break folded to a space.
        /// </summary>
        /// <remarks>
        ///     The stored labels carry <c>&lt;br&gt;</c> where the client wraps them
        ///     (Node_Sub40.java:154-158), so "Troll&lt;br&gt;Stronghold" and the area display name
        ///     "Troll Stronghold" are the same words differently laid out.
        /// </remarks>
        /// <param name="label">The stored label, or null.</param>
        /// <returns>The label on one line.</returns>
        private static string Flatten(string label)
        {
            return label == null ? string.Empty : label.Replace("<br>", " ").Trim();
        }

        /// <summary>Every static element of every area, with the area whose group holds it.</summary>
        /// <param name="cache">The open cache.</param>
        /// <returns>The placements, area by area.</returns>
        private static IReadOnlyList<(WorldMapAreaDefinition Host, WorldMapElement Element)> Placements(
            RSCache cache)
        {
            var reader = new WorldMapReader(cache);
            var placed = new List<(WorldMapAreaDefinition, WorldMapElement)>();

            foreach (WorldMapAreaDefinition area in reader.ReadAreas())
                foreach (WorldMapElement element in reader.ReadStaticElements(area.InternalName))
                    placed.Add((area, element));

            return placed;
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
