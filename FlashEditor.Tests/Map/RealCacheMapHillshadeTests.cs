using System;
using System.Collections.Generic;
using FlashEditor.cache;
using FlashEditor.Cache.Region;
using FlashEditor.Cache.Util;
using FlashEditor.Definitions.Sprites;
using FlashEditor.Map;
using FlashEditor.Tests.Cache.RealCache;
using Xunit;

namespace FlashEditor.Tests.Map
{
    /// <summary>
    ///     Relief shading against real terrain, including the seam detector.
    /// </summary>
    /// <remarks>
    ///     The unit tests pin the maths on synthetic grids. These pin that it is fed real heights
    ///     resolved across square boundaries, which is the part that fails silently: an unpopulated
    ///     height grid, a dropped negation or an unfixed vertex seam all still render a picture.
    /// </remarks>
    [Collection("RealCache")]
    public sealed class RealCacheMapHillshadeTests : IClassFixture<RealCacheFixture>
    {
        /// <summary>
        ///     The roughest square in the shipped cache, at 262 world units of mean tile-to-tile
        ///     height change against a median of 33 across all 1684. Flat squares like Lumbridge
        ///     would make a relief assertion vacuous.
        /// </summary>
        private const int RoughRegionX = 36;
        private const int RoughRegionY = 52;

        private readonly RealCacheFixture _fixture;

        public RealCacheMapHillshadeTests(RealCacheFixture fixture)
        {
            _fixture = fixture;
        }

        [RealCacheFact]
        public void ReliefVariesAcrossRealTerrain()
        {
            MapScene scene = LoadScene(RoughRegionX, RoughRegionY);
            float[,] shade = Build(scene);

            var distinct = new HashSet<float>();
            float min = float.MaxValue, max = float.MinValue;

            for (int x = 64; x < 128; x++)
            {
                for (int y = 64; y < 128; y++)
                {
                    distinct.Add(shade[x, y]);
                    min = Math.Min(min, shade[x, y]);
                    max = Math.Max(max, shade[x, y]);
                }
            }

            //A zeroed height grid or a lost gradient collapses this to a single value.
            Assert.True(distinct.Count > 50, $"only {distinct.Count} distinct shades over the square");
            Assert.True(max - min > 0.2f, $"shade spread of only {max - min:F3} on the roughest square");
        }

        /// <summary>
        ///     Flat terrain stays neutral even on real data.
        /// </summary>
        /// <remarks>
        ///     Guards against a systematic bias that would dim or brighten the whole map. Lumbridge
        ///     is largely flat, so its median tile must sit on 1.
        /// </remarks>
        [RealCacheFact]
        public void FlatRealTerrainStaysNeutral()
        {
            float[,] shade = Build(LoadScene(50, 50));

            var values = new List<float>();
            for (int x = 64; x < 128; x++)
                for (int y = 64; y < 128; y++)
                    values.Add(shade[x, y]);

            values.Sort();
            Assert.Equal(1.0f, values[values.Count / 2], 2);
        }

        /// <summary>
        ///     No crease along a square boundary.
        /// </summary>
        /// <remarks>
        ///     This is the strongest fail-if-the-seam-is-unfixed test, because it runs on real
        ///     heights where an unresolved shared vertex reads 0 against ground at -320 to -1900.
        ///     Compares the mean absolute shade step across each interior column and row against the
        ///     median step. Median rather than mean, because real terrain has genuine cliffs and a
        ///     mean is not robust to them.
        /// </remarks>
        [RealCacheFact]
        public void ThereIsNoSeamAtASquareBoundary()
        {
            float[,] shade = Build(LoadScene(RoughRegionX, RoughRegionY));

            int width = shade.GetLength(0);
            int height = shade.GetLength(1);

            double[] columnStep = new double[width];
            for (int x = 1; x < width; x++)
            {
                double total = 0;
                for (int y = 0; y < height; y++)
                    total += Math.Abs(shade[x, y] - shade[x - 1, y]);
                columnStep[x] = total / height;
            }

            double[] rowStep = new double[height];
            for (int y = 1; y < height; y++)
            {
                double total = 0;
                for (int x = 0; x < width; x++)
                    total += Math.Abs(shade[x, y] - shade[x, y - 1]);
                rowStep[y] = total / width;
            }

            double columnMedian = Median(columnStep, 1);
            double rowMedian = Median(rowStep, 1);

            //64 and 128 are the two interior square boundaries of a 3x3 scene.
            foreach (int boundary in new[] { 64, 128 })
            {
                Assert.True(columnStep[boundary] < 4 * columnMedian,
                    $"column {boundary} steps {columnStep[boundary]:F4} against a median of {columnMedian:F4} - the vertex seam is back");
                Assert.True(rowStep[boundary] < 4 * rowMedian,
                    $"row {boundary} steps {rowStep[boundary]:F4} against a median of {rowMedian:F4} - the vertex seam is back");
            }
        }

        /// <summary>
        ///     Turning relief off, by either route, reproduces the unshaded render exactly.
        /// </summary>
        [RealCacheFact]
        public void TurningReliefOffReproducesTheUnshadedRender()
        {
            RSCache cache = _fixture.OpenCache();
            new TextureManager(cache).Load();

            var loader = new MapSquareLoader(cache);
            var rasteriser = new MapRasteriser(cache) { TilePixels = 4 };
            MapScene scene = MapScene.Load(loader, RoughRegionX, RoughRegionY);

            int[] layerOff = Snapshot(rasteriser, scene, MapLayers.Default & ~MapLayers.Hillshade);

            rasteriser.HillshadeStrength = 0f;
            int[] strengthZero = Snapshot(rasteriser, scene, MapLayers.Default);

            rasteriser.HillshadeStrength = 0.65f;
            int[] shaded = Snapshot(rasteriser, scene, MapLayers.Default);

            //Both off-switches must be bit-exact, not merely close.
            Assert.Equal(layerOff, strengthZero);
            Assert.NotEqual(layerOff, shaded);
        }

        /// <summary>Rendering never dirties a square, so relief cannot provoke a save.</summary>
        [RealCacheFact]
        public void RenderingWithReliefDirtiesNothing()
        {
            RSCache cache = _fixture.OpenCache();
            var loader = new MapSquareLoader(cache);
            var rasteriser = new MapRasteriser(cache) { TilePixels = 4 };

            MapScene scene = MapScene.Load(loader, RoughRegionX, RoughRegionY);

            for (int dx = 0; dx < scene.SquaresX; dx++)
                for (int dy = 0; dy < scene.SquaresY; dy++)
                    scene.Square(dx, dy)?.ClearDirty();

            using (DirectBitmap _ = rasteriser.Render(scene, 0, MapLayers.Default)) { }

            for (int dx = 0; dx < scene.SquaresX; dx++)
                for (int dy = 0; dy < scene.SquaresY; dy++)
                    Assert.False(scene.Square(dx, dy)?.Dirty ?? false,
                        $"square {dx},{dy} was dirtied by rendering");
        }

        private MapScene LoadScene(int rx, int ry) =>
            MapScene.Load(new MapSquareLoader(_fixture.OpenCache()), rx, ry);

        private static float[,] Build(MapScene scene) =>
            Hillshade.Build(scene.HeightGrid(0),
                Hillshade.DefaultAzimuthDegrees, Hillshade.DefaultAltitudeDegrees, 0.65f);

        private static int[] Snapshot(MapRasteriser rasteriser, MapScene scene, MapLayers layers)
        {
            using DirectBitmap bitmap = rasteriser.Render(scene, 0, layers);
            return (int[]) bitmap.Bits.Clone();
        }

        private static double Median(double[] values, int from)
        {
            var copy = new List<double>();
            for (int i = from; i < values.Length; i++)
                copy.Add(values[i]);
            copy.Sort();
            return copy[copy.Count / 2];
        }
    }
}
