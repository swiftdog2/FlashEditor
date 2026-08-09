using System;
using System.Collections.Generic;
using System.Text;
using FlashEditor.Cache;
using FlashEditor.Cache.Region;
using FlashEditor.Cache.Util;
using FlashEditor.Tests.Cache.RealCache;
using Xunit;
using Xunit.Abstractions;
using FlashEditor.IO;

namespace FlashEditor.Tests.Cache.RealCache
{
    /// <summary>
    ///     Decodes every map square in the real cache and requires each file to be consumed
    ///     exactly.
    /// </summary>
    /// <remarks>
    ///     Exact consumption is the whole point. The terrain and location formats are both
    ///     self-delimiting streams with no length prefix, so any error in a field width, an opcode
    ///     boundary or an iteration order desynchronises the parse and it runs off the end of the
    ///     buffer or stops short. Sweeping all 1684 squares and requiring the cursor to land on the
    ///     last byte is therefore a decisive test of the whole decoder, in a way that decoding one
    ///     hand-picked square is not.
    ///
    ///     Formats are documented in <c>reference/hydra-637-maps/</c>.
    /// </remarks>
    [Collection("RealCache")]
    public sealed class RealCacheMapDecodeTests : IClassFixture<RealCacheFixture>
    {
        private readonly RealCacheFixture _fixture;
        private readonly ITestOutputHelper _output;

        public RealCacheMapDecodeTests(RealCacheFixture fixture, ITestOutputHelper output)
        {
            _fixture = fixture;
            _output = output;
        }

        /// <summary>
        ///     Every index-5 group name resolves to one of the five known families, with nothing
        ///     left over.
        /// </summary>
        /// <remarks>
        ///     An unmatched name hash would mean a sixth per-region file family nobody has
        ///     identified, which the rest of this suite would silently ignore.
        /// </remarks>
        [RealCacheFact]
        public void Index5DecomposesIntoTheFiveKnownFamilies()
        {
            RSReferenceTable table = _fixture.Table(RSConstants.MAPS_INDEX);

            string[] prefixes = { "m", "l", "um", "ul", "n" };
            var counts = new Dictionary<string, int>();
            foreach (string prefix in prefixes)
                counts[prefix] = 0;

            int matched = 0;
            for (int regionX = 0; regionX < 256; regionX++)
            {
                for (int regionY = 0; regionY < 256; regionY++)
                {
                    foreach (string prefix in prefixes)
                    {
                        if (table.GetArchiveId(prefix + regionX + "_" + regionY) == -1)
                            continue;
                        counts[prefix]++;
                        matched++;
                    }
                }
            }

            //Every group in the table must be accounted for by one of the five names. A shortfall
            //means a sixth family exists that nothing in this suite exercises.
            Assert.Equal(table.GetArchiveEntries().Count, matched);
            Assert.Equal(1684, counts["m"]);
            Assert.Equal(1684, counts["l"]);
            Assert.Equal(900, counts["um"]);
            Assert.Equal(900, counts["ul"]);
            Assert.Equal(35, counts["n"]);
        }

        /// <summary>
        ///     Every terrain file decodes to exact buffer consumption.
        /// </summary>
        /// <remarks>
        ///     How many squares carry an extras section after the grid is scoped to the cache: 12
        ///     of the 1684 terrain squares differ between the two supported caches, so the split
        ///     between "ends at the grid" and "carries a tail" is a count of one cache's content.
        ///     The claim that holds either way is that every declared square decodes and that the
        ///     two populations account for all of them, which is what catches a grid decoder
        ///     stopping in the wrong place.
        /// </remarks>
        [RealCacheFact]
        public void EveryTerrainFileConsumesItsBufferExactly()
        {
            MapSquareLoader loader = NewLoader();
            RSReferenceTable table = _fixture.Table(RSConstants.MAPS_INDEX);

            int decoded = 0;
            int withTail = 0;
            var failures = new List<string>();

            foreach ((int rx, int ry) in EverySquare(table, "m"))
            {
                try
                {
                    Region region = loader.Load(rx, ry, out _);
                    Assert.NotNull(region);
                    decoded++;
                    if (region.ExtrasTail.Length > 0)
                        withTail++;
                }
                catch (Exception ex)
                {
                    failures.Add("m" + rx + "_" + ry + ": " + ex.Message);
                }
            }

            AssertNoFailures(failures);
            Assert.Equal(1684, decoded);

            //Both populations have to be occupied. A grid decoder that stopped a field short would
            //leave every square with a tail, and one that swallowed the extras would leave none
            //with one - and neither shows up as a consumption failure, because the loader sweeps
            //whatever the grid left into the tail either way.
            Assert.True(withTail > 0 && withTail < decoded,
                $"{withTail} of {decoded} terrain squares carry an extras section. All or none means " +
                "the grid decoder is stopping in the wrong place rather than the cache differing.");
            _output.WriteLine($"{_fixture.Profile.Name}: {withTail} of {decoded} terrain squares carry " +
                              $"an extras section, {decoded - withTail} end at the grid");
            _fixture.Profile.AssertCensus(_output, "map.terrainWithExtras", withTail);
        }

        /// <summary>
        ///     Every readable location file decodes, and the encrypted ones behave as the client
        ///     does rather than throwing.
        /// </summary>
        [RealCacheFact]
        public void EveryLocationFileDecodesOrReportsAMissingKey()
        {
            MapSquareLoader loader = NewLoader();
            RSReferenceTable table = _fixture.Table(RSConstants.MAPS_INDEX);

            int loaded = 0;
            int missingKey = 0;
            int locs = 0;
            var failures = new List<string>();

            foreach ((int rx, int ry) in EverySquare(table, "l"))
            {
                try
                {
                    Region region = loader.Load(rx, ry, out LocationLoadResult result);
                    Assert.NotNull(region);

                    if (result == LocationLoadResult.MissingKey)
                    {
                        missingKey++;
                        //The client's behaviour for an unreadable loc file is an empty square,
                        //not an error, so this must not surface as a failure.
                        Assert.Empty(region.GetLocations());
                        continue;
                    }

                    loaded++;
                    foreach (Location loc in region.GetLocations())
                    {
                        locs++;
                        Assert.InRange(loc.Shape, 0, 22);
                        Assert.InRange(loc.Orientation, 0, 3);
                        Assert.InRange(loc.LocalX, 0, 63);
                        Assert.InRange(loc.LocalY, 0, 63);
                        Assert.InRange(loc.Plane, 0, 3);
                    }
                }
                catch (Exception ex)
                {
                    failures.Add("l" + rx + "_" + ry + ": " + ex.Message);
                }
            }

            AssertNoFailures(failures);
            Assert.Equal(1684, loaded + missingKey);
            Assert.True(locs > 0, "no locations decoded at all");
        }

        /// <summary>
        ///     Reading the object-id delta as a plain smart instead of an extended one corrupts
        ///     real squares.
        /// </summary>
        /// <remarks>
        ///     This pins the defect that motivated <see cref="JagStream.ReadExtendedUnsignedSmart"/>.
        ///     Without it the suite would pass just as happily with the wrong reader, because the
        ///     squares that need the continuation are a minority and the rest are unaffected. So
        ///     the assertion that matters is that the minority is not empty: a cache where no
        ///     square reached the continuation would let the plain reader through unnoticed.
        ///     <para>
        ///     How large that minority is belongs to the cache. 991 of the 1684 location squares
        ///     differ between the two supported caches, and which of them are readable at all
        ///     differs further - the repack holds 1025 as plaintext and the vanilla capture 35 -
        ///     so the count is scoped rather than written down.
        ///     </para>
        /// </remarks>
        [RealCacheFact]
        public void ThePlainSmartReaderCorruptsSquaresThatUseTheContinuation()
        {
            MapSquareLoader loader = NewLoader();
            RSReferenceTable table = _fixture.Table(RSConstants.MAPS_INDEX);

            int readable = 0;
            int divergent = 0;

            foreach ((int rx, int ry) in EverySquare(table, "l"))
            {
                int group = loader.ResolveGroup(MapSquareNames.Locations(rx, ry));
                if (group == -1)
                    continue;

                byte[] payload = TryPayload(loader, group);
                if (payload == null)
                    continue;

                readable++;
                if (UsesSmartContinuation(payload))
                    divergent++;
            }

            _output.WriteLine($"{_fixture.Profile.Name}: {divergent} of {readable} readable l groups " +
                              "carry a 32767 continuation in the id delta");

            Assert.True(divergent > 0,
                $"none of the {readable} readable location groups reaches the 32767 continuation, so " +
                "nothing here would notice the plain smart reader being used instead");
            _fixture.Profile.AssertCensus(_output, "map.locationsUsingTheSmartContinuation", divergent);
        }

        /// <summary>
        ///     Underwater terrain is a single plane, and decoding it as four fails.
        /// </summary>
        [RealCacheFact]
        public void UnderwaterTerrainIsSinglePlane()
        {
            MapSquareLoader loader = NewLoader();
            RSReferenceTable table = _fixture.Table(RSConstants.MAPS_INDEX);

            int decoded = 0;
            var failures = new List<string>();

            foreach ((int rx, int ry) in EverySquare(table, "um"))
            {
                try
                {
                    Region region = loader.LoadUnderwater(rx, ry);
                    Assert.NotNull(region);
                    Assert.Equal(1, region.PlaneCount);

                    //No um file carries a tail. A non-empty one would mean the single-plane grid
                    //stopped short and the remainder was swept up as extras.
                    Assert.Empty(region.ExtrasTail);
                    decoded++;
                }
                catch (Exception ex)
                {
                    failures.Add("um" + rx + "_" + ry + ": " + ex.Message);
                }
            }

            AssertNoFailures(failures);
            Assert.Equal(900, decoded);
        }

        /// <summary>
        ///     The procedural height generator produces varying output.
        /// </summary>
        /// <remarks>
        ///     Guards the specific regression this port started from: the cosine table was built by
        ///     a method nothing called, so it stayed all-zero and every procedural height came out
        ///     identical. A constant generator is invisible in any exact-consumption test, because
        ///     it consumes no bytes.
        /// </remarks>
        [Fact]
        public void ProceduralHeightsVaryAcrossTiles()
        {
            var seen = new HashSet<int>();
            for (int x = 0; x < 64; x++)
                for (int y = 0; y < 64; y++)
                    seen.Add(HeightCalc.Calculate(3200, 3200, x, y));

            Assert.True(seen.Count > 1,
                "procedural heights are constant - the cosine table is probably unpopulated");

            foreach (int h in seen)
                Assert.InRange(h, 10, 60);
        }

        private MapSquareLoader NewLoader() => new MapSquareLoader(_fixture.OpenCache());

        private static byte[] TryPayload(MapSquareLoader loader, int group)
        {
            try
            {
                return loader.ReadGroupBytes(group);
            }
            catch (Exception)
            {
                return null;
            }
        }

        /// <summary>
        ///     Whether the id-delta field of a loc stream ever hits the 32767 continuation.
        /// </summary>
        private static bool UsesSmartContinuation(byte[] payload)
        {
            var buf = new JagStream(payload);
            int id = -1;

            while (true)
            {
                int first = buf.ReadUnsignedSmart();
                if (first == 0)
                    return false;
                if (first == 32767)
                    return true;

                id += first;

                int position = 0;
                int delta;
                while ((delta = buf.ReadUnsignedSmart()) != 0)
                {
                    position += delta - 1;
                    buf.ReadUnsignedByte();
                }
            }
        }

        private IEnumerable<(int, int)> EverySquare(RSReferenceTable table, string prefix)
        {
            for (int rx = 0; rx < 256; rx++)
                for (int ry = 0; ry < 256; ry++)
                    if (table.GetArchiveId(prefix + rx + "_" + ry) != -1)
                        yield return (rx, ry);
        }

        private static void AssertNoFailures(List<string> failures)
        {
            if (failures.Count == 0)
                return;

            var sb = new StringBuilder();
            sb.Append(failures.Count).Append(" map squares failed to decode:");
            for (int i = 0; i < failures.Count && i < 20; i++)
                sb.AppendLine().Append("  ").Append(failures[i]);
            if (failures.Count > 20)
                sb.AppendLine().Append("  ... and ").Append(failures.Count - 20).Append(" more");

            Assert.Fail(sb.ToString());
        }
    }
}
