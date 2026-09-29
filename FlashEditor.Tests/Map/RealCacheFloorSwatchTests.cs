using System.Drawing;
using FlashEditor.Cache;
using FlashEditor.Cache.Region;
using FlashEditor.Definitions.Sprites;
using FlashEditor.Map;
using FlashEditor.Tests.Cache.RealCache;
using Xunit;

using MapRegion = FlashEditor.Cache.Region.Region;

namespace FlashEditor.Tests.Map
{
    /// <summary>
    ///     Every floor swatch the palette builds against the colour the rasteriser actually draws for
    ///     a tile carrying that record.
    /// </summary>
    /// <remarks>
    ///     The comparison goes through <see cref="MapRasteriser.TileColourOrNone"/> on a scene built
    ///     for the purpose, not through <see cref="FloorColour"/>, so a rasteriser that stopped
    ///     drawing through the shared resolver fails here. Textures are loaded first so textured
    ///     overlays exercise the texture branch rather than falling back to the primary.
    /// </remarks>
    [Collection("RealCache")]
    public sealed class RealCacheFloorSwatchTests : IClassFixture<RealCacheFixture>
    {
        private const int RegionX = 50;
        private const int RegionY = 50;

        private readonly RealCacheFixture _fixture;

        public RealCacheFloorSwatchTests(RealCacheFixture fixture)
        {
            _fixture = fixture;
        }

        [RealCacheFact]
        public void EveryOverlaySwatch_IsTheColourTheMapDrawsForIt()
        {
            RSCache cache = _fixture.OpenCache();
            TextureManager.EnsureLoaded(cache);
            using var rasteriser = new MapRasteriser(cache);

            int[] ids = cache.GetFileIds(RSConstants.CONFIG, RSConstants.FLOOR_OVERLAY_GROUP);
            int holes = 0, coloured = 0;

            foreach (int id in ids)
            {
                FloorSwatch swatch = FloorSwatch.Overlay(cache.GetFloorOverlay(id), FloorColour.TextureHsl);

                var square = new MapRegion(MapSquareNames.RegionId(RegionX, RegionY));
                square.SetOverlayId(0, 0, 0, id + 1);
                MapScene scene = MapScene.FromSquares(RegionX, RegionY, new[,] { { square } });

                int drawn = rasteriser.TileColourOrNone(scene, 0, 0, 0, null, Rectangle.Empty, 1f,
                    MapLayers.Terrain, out bool isHole);

                switch (swatch.Fill)
                {
                    case FloorSwatchFill.Colour:
                        Assert.Equal(swatch.MapRgb, drawn);
                        coloured++;
                        break;
                    case FloorSwatchFill.Hole:
                        Assert.True(isHole, swatch.Caption);
                        holes++;
                        break;
                    default:
                        Assert.False(isHole, swatch.Caption);
                        Assert.Equal(MapPalette.NoColour, drawn);
                        break;
                }
            }

            //Without both, the sweep would pass over a cache whose overlays all took one branch.
            Assert.True(coloured > 0);
            Assert.True(holes > 0);
        }

        [RealCacheFact]
        public void EveryUnderlaySwatch_IsTheColourAFieldOfItDraws()
        {
            RSCache cache = _fixture.OpenCache();
            using var rasteriser = new MapRasteriser(cache);

            foreach (int id in cache.GetFileIds(RSConstants.CONFIG, RSConstants.FLOOR_UNDERLAY_GROUP))
            {
                FloorSwatch swatch = FloorSwatch.Underlay(cache.GetFloorUnderlay(id));

                var square = new MapRegion(MapSquareNames.RegionId(RegionX, RegionY));
                for (int x = 0; x < 64; x++)
                    for (int y = 0; y < 64; y++)
                        square.SetUnderlayId(0, x, y, id + 1);
                MapScene scene = MapScene.FromSquares(RegionX, RegionY, new[,] { { square } });

                var window = new Rectangle(32, 32, 1, 1);
                int[,] blended = rasteriser.BlendWindow(scene, 0, window);

                int drawn = rasteriser.TileColourOrNone(scene, 0, 32, 32, blended, window, 1f,
                    MapLayers.Terrain, out _);

                //A blend that packs to 0 is not drawn at all; the swatch still shows its black.
                Assert.Equal(swatch.MapRgb, drawn == MapPalette.NoColour ? 0 : drawn);
            }
        }
    }
}
