using System.Collections.Generic;
using System.Linq;
using FlashEditor.Cache;
using FlashEditor.Cache.Region;
using FlashEditor.Map;
using FlashEditor.Tests.Cache.RealCache;
using Xunit;

using MapRegion = FlashEditor.Cache.Region.Region;

namespace FlashEditor.Tests.Map
{
    /// <summary>
    ///     Pins the floor brush's unit against tiles the game shipped, rather than against this
    ///     project's own reading of the format.
    /// </summary>
    [Collection("RealCache")]
    public sealed class RealCacheFloorBrushTests : IClassFixture<RealCacheFixture>
    {
        /// <summary>The lava texture. The overlay definition carrying it is read from the table.</summary>
        private const int LavaTexture = 31;

        //World tile 2848, 3165 is inside the Karamja volcano crater.
        private const int VolcanoRegionX = 2848 / MapRegion.WIDTH;
        private const int VolcanoRegionY = 3165 / MapRegion.HEIGHT;

        private readonly RealCacheFixture _fixture;

        public RealCacheFloorBrushTests(RealCacheFixture fixture)
        {
            _fixture = fixture;
        }

        /// <summary>
        ///     Picking the lava swatch paints the value the shipped crater stores, and the square
        ///     stores the bare definition id nowhere - which is what the brush used to write.
        /// </summary>
        [RealCacheFact]
        public void PalettePickOfLava_PaintsTheValueTheShippedCraterStores()
        {
            RSCache cache = _fixture.OpenCache();

            int[] lava = cache.GetFileIds(RSConstants.CONFIG, 4)
                .Where(id => cache.GetFloorOverlay(id).TextureId == LavaTexture)
                .ToArray();
            int lavaDefinition = Assert.Single(lava);

            MapRegion crater = new MapSquareLoader(cache).Load(VolcanoRegionX, VolcanoRegionY, out _);
            var shipped = new HashSet<int>();
            for (int x = 0; x < MapRegion.WIDTH; x++)
                for (int y = 0; y < MapRegion.HEIGHT; y++)
                    shipped.Add(crater.GetOverlayId(0, x, y));

            FloorBrushLoad load = FloorBrush.Load(new FloorPick(FloorKind.Overlay, lavaDefinition));
            Assert.Null(load.Refusal);

            var blank = new MapRegion(MapSquareNames.RegionId(VolcanoRegionX, VolcanoRegionY));
            new SetOverlayEdit(blank, 0, 0, 0, load.StoredValue, 0, 0).Apply();
            int painted = blank.GetOverlayId(0, 0, 0);

            Assert.Contains(painted, shipped);
            Assert.DoesNotContain(lavaDefinition, shipped);
        }
    }
}
