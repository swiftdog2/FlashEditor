using System;
using System.Collections.Generic;
using System.Linq;
using FlashEditor.cache;
using FlashEditor.Definitions.WorldMap;
using FlashEditor.Tests.Cache.RealCache;
using Xunit;
using Xunit.Abstractions;

namespace FlashEditor.Tests.Definitions
{
    /// <summary>
    ///     What the World Map Overview tab draws, checked against the index rather than against the
    ///     renderer.
    /// </summary>
    /// <remarks>
    ///     Nothing in this suite covers WinForms or a paint handler, so none of this says the picture
    ///     looks right - that is judged from a capture. What it does pin is everything the picture is
    ///     computed from: the meaning of the bytes the colours come from, and the canvas geometry the
    ///     blocks and icons are placed into. Both are the kind of mistake that produces a plausible
    ///     picture rather than a crash.
    /// </remarks>
    [Collection("RealCache")]
    public sealed class RealCacheWorldMapRasterTests : IClassFixture<RealCacheFixture>
    {
        private readonly RealCacheFixture _fixture;
        private readonly ITestOutputHelper _output;

        /// <summary>Binds the shared open cache and the per-test output sink.</summary>
        public RealCacheWorldMapRasterTests(RealCacheFixture fixture, ITestOutputHelper output)
        {
            _fixture = fixture;
            _output = output;
        }

        /// <summary>
        ///     The byte after an overlay terrain tile names a floor underlay, not a tile shape.
        /// </summary>
        /// <remarks>
        ///     <para>
        ///     This decoder called it <c>OverlayShape</c> until the renderer needed it. The client
        ///     assigns it to <c>aByteArray2081</c> (<c>Class278.java:217</c>), which is the plane its
        ///     terrain blender resolves through <c>method2483(value - 1)</c> into a
        ///     <c>FloorUnderlay</c> (<c>:634-636</c>), and the same branch writes a literal zero into
        ///     the shape plane <c>aByteArray2073</c> one line earlier. So the shape is present, it is
        ///     zero, and this is something else.
        ///     </para>
        ///     <para>
        ///     Settled here a third way, through neither the client nor the decoder: every stored
        ///     value read unsigned is a live floor-underlay id. A packed shape and rotation would run
        ///     to 255 and cluster in the low bits; an underlay id cannot exceed what the config group
        ///     declares, and the assertion is written against that declared count rather than against
        ///     the 150 measured, so it holds in a cache with a different floor table.
        ///     </para>
        ///     <para>
        ///     No byte-identity sweep could have caught the wrong name, because the byte round-trips
        ///     whatever it is called. What it cost was the picture: a renderer treating it as a shape
        ///     draws no ground colour at all beneath any overlay tile.
        ///     </para>
        /// </remarks>
        [RealCacheFact]
        public void TheByteOnAnOverlayTerrainTileIsAnUnderlayId()
        {
            RSCache cache = _fixture.OpenCache();
            var reader = new WorldMapReader(cache);

            int underlays = cache.GetFileIds(RSConstants.CONFIG, RSConstants.FLOOR_UNDERLAY_GROUP).Length;
            Assert.True(underlays > 0, "no floor underlay is declared, so the range test says nothing");

            var values = new SortedSet<int>();
            long tiles = 0;
            long none = 0;

            foreach (WorldMapAreaDefinition area in reader.ReadAreas())
            {
                WorldMapAreaRaster raster = reader.ReadRaster(area.InternalName);
                Assert.NotNull(raster);

                foreach (WorldMapRasterBlock block in raster.Blocks)
                {
                    foreach (WorldMapTile tile in block.Tiles)
                    {
                        if (tile.IsDecorated || tile.IsBlank || !tile.IsOverlay)
                            continue;

                        tiles++;
                        values.Add(tile.UnderlayBeneathOverlay);
                        if (tile.UnderlayBeneathOverlay == 0)
                            none++;
                    }
                }
            }

            _output.WriteLine($"{tiles} overlay terrain tiles carry the byte; {none} store 0 for " +
                              $"\"no underlay\"; {values.Count} distinct values, unsigned " +
                              $"{values.Min}..{values.Max}, against {underlays} declared floor underlays");

            Assert.True(tiles > 0, "no overlay terrain tile occurs, so the byte is never exercised");
            Assert.True(values.Max <= underlays,
                $"an overlay tile stores {values.Max}, which is past the {underlays} floor underlays " +
                "this cache declares, so the byte cannot be an underlay id after all");
            Assert.True(values.Count > 16,
                values.Count + " distinct values is few enough to be a shape code, which is the " +
                "reading this test exists to exclude");
        }

        /// <summary>
        ///     Every raster block and every icon lands inside the canvas the area's zones describe.
        /// </summary>
        /// <remarks>
        ///     <para>
        ///     The canvas is not stored. The client derives it from the area's own zone rectangles -
        ///     <c>Class339.java:29-35</c> rounds the least destination corner down to a map square
        ///     and sizes the picture from the greatest - and then places raster blocks at
        ///     <c>blockX * 64 - originX</c> (<c>Class278.java:527-529</c>) and icons at the position
        ///     a zone translates into (<c>:477-478</c>). Two independent statements about the same
        ///     rectangle, one from the details record and one from the raster file.
        ///     </para>
        ///     <para>
        ///     Asserted rather than clamped. A renderer that silently dropped out-of-range writes
        ///     would draw a picture with a hole in it and report nothing, which is precisely the
        ///     failure that reads as "the cache is like that".
        ///     </para>
        /// </remarks>
        [RealCacheFact]
        public void EveryBlockAndIconLandsInsideTheCanvasTheZonesDescribe()
        {
            RSCache cache = _fixture.OpenCache();
            var reader = new WorldMapReader(cache);

            var failures = new List<string>();
            long blocks = 0;
            long icons = 0;
            long unplaceable = 0;
            long pixels = 0;

            foreach (WorldMapAreaDefinition area in reader.ReadAreas())
            {
                WorldMapCanvas canvas = WorldMapCanvas.For(area);
                if (canvas.IsEmpty)
                {
                    //An area with no zone has no canvas at all, which is a real shape here: two
                    //areas carry no static element and one carries no zone.
                    _output.WriteLine($"area {area.Id} '{area.InternalName}' has no zone, so no canvas");
                    continue;
                }

                pixels += (long) canvas.Width * canvas.Height;

                WorldMapAreaRaster raster = reader.ReadRaster(area.InternalName);
                Assert.NotNull(raster);

                foreach (WorldMapRasterBlock block in raster.Blocks)
                {
                    blocks++;
                    for (int i = 0; i < block.Tiles.Length; i++)
                    {
                        int x = block.WorldXOf(i) - canvas.OriginX;
                        int y = block.WorldYOf(i) - canvas.OriginY;

                        if (x < 0 || y < 0 || x >= canvas.Width || y >= canvas.Height)
                        {
                            failures.Add($"'{area.InternalName}' block at {block.BlockX},{block.BlockY} " +
                                         $"puts a tile at {x},{y} of a {canvas.Width}x{canvas.Height} canvas");
                            break;
                        }
                    }
                }

                foreach (WorldMapElement element in reader.ReadStaticElements(area.InternalName))
                {
                    icons++;
                    if (!canvas.TryPlace(area, element.Plane, element.X, element.Y, out int ix, out int iy))
                    {
                        //No zone of this area covers the icon's world position, so the client draws
                        //it nowhere either (Class278.java:474-479 skips on a false from method1573).
                        unplaceable++;
                        continue;
                    }

                    if (ix < 0 || iy < 0 || ix >= canvas.Width || iy >= canvas.Height)
                    {
                        failures.Add($"'{area.InternalName}' element {element.Id} places at {ix},{iy} " +
                                     $"of a {canvas.Width}x{canvas.Height} canvas");
                    }
                }
            }

            _output.WriteLine($"{blocks} raster blocks and {icons} icons across " +
                              $"{pixels:N0} canvas tiles; {unplaceable} icons sit on no zone of their " +
                              "own area and so are drawn nowhere by the client either");

            Assert.True(blocks > 0, "no block was placed, so nothing was checked");
            if (failures.Count > 0)
                Assert.Fail($"{failures.Count} placements fell outside their canvas:" +
                            Environment.NewLine + string.Join(Environment.NewLine, failures.Take(20)));
        }
    }
}
