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
    ///     Round-trips every map square in the real cache through the encoder.
    /// </summary>
    /// <remarks>
    ///     This is the gate on the write path. A square that does not survive decode, encode and
    ///     decode again is a square the editor cannot safely save, and because the archive CRC is
    ///     taken over the stored bytes, a wrong encode corrupts silently rather than failing.
    ///
    ///     Two properties are checked and they are not the same strength. <b>Byte identity</b> says
    ///     the encoder reproduces the original file exactly. <b>Semantic identity</b> says a
    ///     re-encode decodes back to the same model, which is what the client actually cares about.
    ///     Byte identity is required for untouched squares, since those are written back verbatim;
    ///     for a forced re-encode it measures how canonical the original encoder was.
    /// </remarks>
    [Collection("RealCache")]
    public sealed class RealCacheRegionCodecTests : IClassFixture<RealCacheFixture>
    {
        private readonly RealCacheFixture _fixture;
        private readonly ITestOutputHelper _output;

        /// <summary>
        ///     Takes the output helper so each sweep can state how many squares it actually swept.
        /// </summary>
        /// <remarks>
        ///     A byte-identity sweep that silently covers nothing is green and worthless, and these
        ///     three are the only ones in the suite whose population is not printed by the shared
        ///     definition harness. The counts are asserted below as well; printing them is what lets
        ///     a run against a different cache be audited without re-deriving the population.
        /// </remarks>
        public RealCacheRegionCodecTests(RealCacheFixture fixture, ITestOutputHelper output)
        {
            _fixture = fixture;
            _output = output;
        }

        /// <summary>An untouched square writes back the bytes it was read as.</summary>
        [RealCacheFact]
        public void UntouchedSquaresAreWrittenBackVerbatim()
        {
            RSCache cache = _fixture.OpenCache();
            var loader = new MapSquareLoader(cache);

            int checked_ = 0;
            var failures = new List<string>();

            foreach ((int rx, int ry) in EverySquare(loader, "m"))
            {
                MapRegion region = loader.Load(rx, ry, out _);
                Assert.False(region.Dirty, $"m{rx}_{ry} came out of the decoder dirty");

                byte[] written = RegionCodec.EncodeTerrain(region);
                if (!Same(region.RawTerrain, written))
                    failures.Add($"m{rx}_{ry}: verbatim write differs");

                checked_++;
                if (checked_ >= 200)
                    break;
            }

            _output.WriteLine($"{checked_} terrain squares were written back verbatim");

            AssertNoFailures(failures);
            Assert.True(checked_ > 0);
        }

        /// <summary>
        ///     A forced re-encode of every terrain file decodes back to an identical model.
        /// </summary>
        [RealCacheFact]
        public void EveryTerrainFileSurvivesAForcedReEncode()
        {
            RSCache cache = _fixture.OpenCache();
            var loader = new MapSquareLoader(cache);

            int total = 0, byteIdentical = 0;
            var failures = new List<string>();

            foreach ((int rx, int ry) in EverySquare(loader, "m"))
            {
                total++;
                try
                {
                    MapRegion original = loader.Load(rx, ry, out _);
                    byte[] encoded = RegionCodec.EncodeTerrain(original, force: true);

                    if (Same(original.RawTerrain, encoded))
                        byteIdentical++;

                    var reloaded = new MapRegion(MapSquareNames.RegionId(rx, ry));
                    reloaded.LoadTerrain(new JagStream(encoded));

                    string difference = CompareTerrain(original, reloaded);
                    if (difference != null)
                        failures.Add($"m{rx}_{ry}: {difference}");
                }
                catch (Exception ex)
                {
                    failures.Add($"m{rx}_{ry}: {ex.Message}");
                }
            }

            _output.WriteLine($"{byteIdentical} of {total} terrain squares re-encoded byte-identically " +
                              "under a forced re-encode");

            AssertNoFailures(failures);
            Assert.Equal(1684, total);

            //Every shipped terrain file must also come back byte-for-byte, which says the canonical
            //opcode order chosen here is the one the original encoder used. If this ever drops, the
            //semantic check above still passes and the saved bytes still work - but the editor would
            //start rewriting squares nobody edited, and every archive CRC with them.
            Assert.Equal(total, byteIdentical);
        }

        /// <summary>
        ///     A forced re-encode of every readable location file decodes back to the same locations.
        /// </summary>
        [RealCacheFact]
        public void EveryLocationFileSurvivesAForcedReEncode()
        {
            RSCache cache = _fixture.OpenCache();
            var loader = new MapSquareLoader(cache);

            int total = 0, byteIdentical = 0, locs = 0;
            var failures = new List<string>();

            foreach ((int rx, int ry) in EverySquare(loader, "l"))
            {
                MapRegion original = loader.Load(rx, ry, out LocationLoadResult result);
                if (result != LocationLoadResult.Loaded)
                    continue;

                total++;
                try
                {
                    byte[] encoded = RegionCodec.EncodeLocations(original, force: true);
                    if (Same(original.RawLocations, encoded))
                        byteIdentical++;

                    var reloaded = new MapRegion(MapSquareNames.RegionId(rx, ry));
                    reloaded.LoadLocations(new JagStream(encoded));

                    string difference = CompareLocations(original, reloaded);
                    if (difference != null)
                        failures.Add($"l{rx}_{ry}: {difference}");

                    locs += original.GetLocations().Count;
                }
                catch (Exception ex)
                {
                    failures.Add($"l{rx}_{ry}: {ex.Message}");
                }
            }

            _output.WriteLine($"{byteIdentical} of {total} readable location squares re-encoded " +
                              $"byte-identically under a forced re-encode, {locs} locations in them");

            AssertNoFailures(failures);
            Assert.True(total > 1500, $"only {total} loc files were readable");
            Assert.True(locs > 1_000_000, $"only {locs} locations round-tripped");
            Assert.Equal(total, byteIdentical);
        }

        /// <summary>An edited square encodes to something that decodes back to the edit.</summary>
        [RealCacheFact]
        public void AnEditedSquareEncodesTheEdit()
        {
            RSCache cache = _fixture.OpenCache();
            var loader = new MapSquareLoader(cache);

            MapRegion region = loader.Load(50, 50, out _);
            region.SetUnderlayId(0, 10, 10, 77);
            region.SetOverlayId(0, 11, 11, 5);
            region.SetOverlayShape(0, 11, 11, 3);
            region.SetOverlayRotation(0, 11, 11, 2);
            region.SetRenderRule(0, 12, 12, 0x9);

            Assert.True(region.Dirty);

            var reloaded = new MapRegion(MapSquareNames.RegionId(50, 50));
            reloaded.LoadTerrain(new JagStream(RegionCodec.EncodeTerrain(region)));

            Assert.Equal(77, reloaded.GetUnderlayId(0, 10, 10));
            Assert.Equal(5, reloaded.GetOverlayId(0, 11, 11));
            Assert.Equal(3, reloaded.GetOverlayShape(0, 11, 11));
            Assert.Equal(2, reloaded.GetOverlayRotation(0, 11, 11));
            Assert.Equal(0x9, reloaded.GetRenderRule(0, 12, 12));
        }

        /// <summary>The extras section survives a re-encode untouched.</summary>
        [RealCacheFact]
        public void TheExtrasTailIsPreservedThroughAnEdit()
        {
            RSCache cache = _fixture.OpenCache();
            var loader = new MapSquareLoader(cache);

            MapRegion region = loader.Load(50, 50, out _);
            Assert.NotEmpty(region.ExtrasTail);

            byte[] originalTail = (byte[]) region.ExtrasTail.Clone();
            region.SetUnderlayId(0, 1, 1, 3);

            var reloaded = new MapRegion(MapSquareNames.RegionId(50, 50));
            reloaded.LoadTerrain(new JagStream(RegionCodec.EncodeTerrain(region)));

            Assert.Equal(originalTail, reloaded.ExtrasTail);
        }

        private static string CompareTerrain(MapRegion a, MapRegion b)
        {
            if (a.PlaneCount != b.PlaneCount)
                return $"plane count {a.PlaneCount} to {b.PlaneCount}";

            for (int z = 0; z < a.PlaneCount; z++)
                for (int x = 0; x < MapRegion.WIDTH; x++)
                    for (int y = 0; y < MapRegion.HEIGHT; y++)
                    {
                        if (a.GetTileHeight(z, x, y) != b.GetTileHeight(z, x, y))
                            return $"height at {z},{x},{y}: {a.GetTileHeight(z, x, y)} to {b.GetTileHeight(z, x, y)}";
                        if (a.GetUnderlayId(z, x, y) != b.GetUnderlayId(z, x, y))
                            return $"underlay at {z},{x},{y}";
                        if (a.GetOverlayId(z, x, y) != b.GetOverlayId(z, x, y))
                            return $"overlay at {z},{x},{y}";
                        if (a.GetOverlayShape(z, x, y) != b.GetOverlayShape(z, x, y))
                            return $"overlay shape at {z},{x},{y}";
                        if (a.GetOverlayRotation(z, x, y) != b.GetOverlayRotation(z, x, y))
                            return $"overlay rotation at {z},{x},{y}";
                        if (a.GetRenderRule(z, x, y) != b.GetRenderRule(z, x, y))
                            return $"flags at {z},{x},{y}";
                    }

            return a.ExtrasTail.Length == b.ExtrasTail.Length ? null : "extras tail length";
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

        private static IEnumerable<(int, int)> EverySquare(MapSquareLoader loader, string prefix)
        {
            for (int rx = 0; rx < 256; rx++)
                for (int ry = 0; ry < 256; ry++)
                    if (loader.ResolveGroup(prefix + rx + "_" + ry) != -1)
                        yield return (rx, ry);
        }

        private static bool Same(byte[] a, byte[] b)
        {
            if (a.Length != b.Length)
                return false;
            for (int i = 0; i < a.Length; i++)
                if (a[i] != b[i])
                    return false;
            return true;
        }

        private static void AssertNoFailures(List<string> failures)
        {
            if (failures.Count == 0)
                return;

            var sb = new StringBuilder();
            sb.Append(failures.Count).Append(" squares failed to round trip:");
            for (int i = 0; i < failures.Count && i < 15; i++)
                sb.AppendLine().Append("  ").Append(failures[i]);
            if (failures.Count > 15)
                sb.AppendLine().Append("  ... and ").Append(failures.Count - 15).Append(" more");

            Assert.Fail(sb.ToString());
        }
    }
}
