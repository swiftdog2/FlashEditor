using System;
using System.Collections.Generic;
using System.Text;
using FlashEditor.cache;
using FlashEditor.Cache.Region;
using FlashEditor.Tests.Cache.RealCache;
using Xunit;
using Xunit.Abstractions;

using MapRegion = FlashEditor.Cache.Region.Region;

namespace FlashEditor.Tests.Map
{
    /// <summary>
    ///     Byte-identity sweeps over the underwater families, <c>um</c> and <c>ul</c>.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///     The surface families had these and the underwater ones did not, which meant 1800 groups
    ///     the editor could open and had no evidence it could write. They share the terrain and
    ///     location codecs with <c>m</c> and <c>l</c>, so this is not a second decoder being
    ///     proven - it is the claim that the one decoder is right about a single-plane grid and
    ///     about a loc file addressed by a different name, neither of which the surface sweeps say
    ///     anything about.
    ///     </para>
    ///     <para>
    ///     Every count comes from the reference table. The two supported caches happen to declare
    ///     the same index-5 shape, but a sweep's claim is "every group the table declares was swept
    ///     and re-encoded identically", and that is a relationship rather than a number.
    ///     </para>
    /// </remarks>
    [Collection("RealCache")]
    public sealed class RealCacheUnderwaterCodecTests : IClassFixture<RealCacheFixture>
    {
        private readonly RealCacheFixture _fixture;
        private readonly ITestOutputHelper _output;

        /// <summary>Takes the shared cache and an output helper for the population lines.</summary>
        /// <param name="fixture">The shared open cache.</param>
        /// <param name="output">Where the swept populations are reported.</param>
        public RealCacheUnderwaterCodecTests(RealCacheFixture fixture, ITestOutputHelper output)
        {
            _fixture = fixture;
            _output = output;
        }

        /// <summary>
        ///     Every underwater terrain file re-encodes to the bytes it was read from.
        /// </summary>
        /// <remarks>
        ///     Forced, so the encoder is exercised rather than the verbatim-clone shortcut. The
        ///     single-plane loop is the part under test: <c>EncodeTerrain</c> writes
        ///     <c>region.PlaneCount</c> planes, so an encoder that assumed four would produce a file
        ///     four times too long here while every surface square still passed.
        /// </remarks>
        [RealCacheFact]
        public void EveryUnderwaterTerrainFileSurvivesAForcedReEncode()
        {
            var loader = new MapSquareLoader(_fixture.OpenCache());

            int declared = Declared("um");
            int total = 0, byteIdentical = 0, withTail = 0;
            var failures = new List<string>();

            foreach ((int rx, int ry) in EverySquare("um"))
            {
                total++;
                try
                {
                    MapRegion original = loader.LoadUnderwater(rx, ry);
                    Assert.Equal(MapSquareLoader.UnderwaterPlanes, original.PlaneCount);
                    if (original.ExtrasTail.Length > 0)
                        withTail++;

                    byte[] encoded = RegionCodec.EncodeTerrain(original, force: true);
                    if (Same(original.RawTerrain, encoded))
                        byteIdentical++;
                    else
                        failures.Add($"um{rx}_{ry}: re-encoded {encoded.Length} bytes from a stored " +
                                     $"{original.RawTerrain.Length}");
                }
                catch (Exception ex)
                {
                    failures.Add($"um{rx}_{ry}: {ex.Message}");
                }
            }

            _output.WriteLine($"{_fixture.Profile.Name}: {byteIdentical} of {total} underwater terrain " +
                              $"squares re-encoded byte-identically, {withTail} carry an extras section");

            AssertNoFailures(failures);
            Assert.True(declared > 0, "the reference table declares no um groups, so nothing was swept");
            Assert.Equal(declared, total);
            Assert.Equal(total, byteIdentical);
        }

        /// <summary>
        ///     Every underwater location file re-encodes to the bytes it was read from, and decodes
        ///     back to the same objects.
        /// </summary>
        /// <remarks>
        ///     The <c>ul</c> family was decoded incidentally inside <c>LoadUnderwater</c> and
        ///     asserted by nothing at all, so until this existed not one underwater object had ever
        ///     been read back in a test.
        /// </remarks>
        [RealCacheFact]
        public void EveryUnderwaterLocationFileSurvivesAForcedReEncode()
        {
            var loader = new MapSquareLoader(_fixture.OpenCache());

            int declared = Declared("ul");
            int total = 0, byteIdentical = 0, locs = 0;
            var failures = new List<string>();

            foreach ((int rx, int ry) in EverySquare("ul"))
            {
                total++;
                try
                {
                    MapRegion original = loader.LoadUnderwater(rx, ry, out LocationLoadResult result);

                    //No ul group is encrypted, so anything other than a clean load here is a
                    //finding rather than something to count and pass over.
                    Assert.Equal(LocationLoadResult.Loaded, result);

                    byte[] encoded = RegionCodec.EncodeLocations(original, force: true);
                    if (Same(original.RawLocations, encoded))
                        byteIdentical++;
                    else
                        failures.Add($"ul{rx}_{ry}: re-encoded {encoded.Length} bytes from a stored " +
                                     $"{original.RawLocations.Length}");

                    var reloaded = new MapRegion(MapSquareNames.RegionId(rx, ry), MapSquareLayer.Underwater);
                    reloaded.LoadLocations(new JagStream(encoded));

                    string difference = CompareLocations(original, reloaded);
                    if (difference != null)
                        failures.Add($"ul{rx}_{ry}: {difference}");

                    foreach (Location loc in original.GetLocations())
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
                    failures.Add($"ul{rx}_{ry}: {ex.Message}");
                }
            }

            _output.WriteLine($"{_fixture.Profile.Name}: {byteIdentical} of {total} underwater location " +
                              $"squares re-encoded byte-identically, {locs} objects in them");

            AssertNoFailures(failures);
            Assert.True(declared > 0, "the reference table declares no ul groups, so nothing was swept");
            Assert.Equal(declared, total);
            Assert.Equal(total, byteIdentical);
            Assert.True(locs > 0, "no underwater object decoded at all");
        }

        /// <summary>
        ///     A square loaded from the underwater family knows it, and one from the surface family
        ///     knows that.
        /// </summary>
        /// <remarks>
        ///     The layer is what stops <c>MapSquareLoader.Save</c> writing a seabed over a surface
        ///     square, and nothing else in the decode path would notice it being wrong.
        /// </remarks>
        [RealCacheFact]
        public void ALoadedSquareCarriesTheFamilyItCameFrom()
        {
            var loader = new MapSquareLoader(_fixture.OpenCache());

            foreach ((int rx, int ry) in EverySquare("um"))
            {
                MapRegion underwater = loader.LoadUnderwater(rx, ry);
                Assert.Equal(MapSquareLayer.Underwater, underwater.Layer);
                Assert.Equal(MapSquareNames.UnderwaterTerrain(rx, ry), underwater.GetTerrainIdentifier());
                Assert.Equal(MapSquareNames.UnderwaterLocations(rx, ry), underwater.GetLocationsIdentifier());

                if (!loader.Exists(rx, ry))
                    continue;

                MapRegion surface = loader.Load(rx, ry, out _);
                Assert.Equal(MapSquareLayer.Surface, surface.Layer);
                Assert.Equal(MapSquareNames.Terrain(rx, ry), surface.GetTerrainIdentifier());
                Assert.Equal(MapSquareNames.Locations(rx, ry), surface.GetLocationsIdentifier());
                return;
            }

            Assert.Fail("no square in this cache has a um group");
        }

        /// <summary>How many groups of a family the index-5 reference table declares.</summary>
        /// <param name="prefix">The family prefix.</param>
        /// <returns>The declared count.</returns>
        private int Declared(string prefix)
        {
            RSReferenceTable table = _fixture.Table(RSConstants.MAPS_INDEX);
            int count = 0;
            for (int rx = 0; rx < 256; rx++)
                for (int ry = 0; ry < 256; ry++)
                    if (table.GetArchiveId(prefix + rx + "_" + ry) != -1)
                        count++;
            return count;
        }

        private IEnumerable<(int, int)> EverySquare(string prefix)
        {
            RSReferenceTable table = _fixture.Table(RSConstants.MAPS_INDEX);
            for (int rx = 0; rx < 256; rx++)
                for (int ry = 0; ry < 256; ry++)
                    if (table.GetArchiveId(prefix + rx + "_" + ry) != -1)
                        yield return (rx, ry);
        }

        private static string CompareLocations(MapRegion a, MapRegion b)
        {
            List<Location> left = Sorted(a);
            List<Location> right = Sorted(b);

            if (left.Count != right.Count)
                return $"{left.Count} locations to {right.Count}";

            for (int i = 0; i < left.Count; i++)
            {
                if (left[i].Id != right[i].Id
                    || left[i].PackedPosition != right[i].PackedPosition
                    || left[i].PackedAttributes != right[i].PackedAttributes)
                    return $"location {i} differs (id {left[i].Id} vs {right[i].Id})";
            }

            return null;
        }

        private static List<Location> Sorted(MapRegion region)
        {
            var list = new List<Location>(region.GetLocations());
            list.Sort((p, q) => p.Id != q.Id
                ? p.Id.CompareTo(q.Id)
                : p.PackedPosition.CompareTo(q.PackedPosition));
            return list;
        }

        private static bool Same(byte[] a, byte[] b) => a.AsSpan().SequenceEqual(b);

        private static void AssertNoFailures(List<string> failures)
        {
            if (failures.Count == 0)
                return;

            var sb = new StringBuilder();
            sb.Append(failures.Count).Append(" underwater squares failed to round trip:");
            for (int i = 0; i < failures.Count && i < 15; i++)
                sb.AppendLine().Append("  ").Append(failures[i]);
            if (failures.Count > 15)
                sb.AppendLine().Append("  ... and ").Append(failures.Count - 15).Append(" more");

            Assert.Fail(sb.ToString());
        }
    }
}
