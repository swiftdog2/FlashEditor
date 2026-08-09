using FlashEditor.Cache;
using FlashEditor.Cache.Region;
using FlashEditor.Cache.Util;
using FlashEditor.Map;
using FlashEditor.Tests.Cache.RealCache;
using Xunit;

using MapRegion = FlashEditor.Cache.Region.Region;

namespace FlashEditor.Tests.Map
{
    /// <summary>
    ///     Exercises the edit loop against a real square, all the way to rendered pixels.
    /// </summary>
    /// <remarks>
    ///     The unit tests prove an edit reverses in the model. This proves the model actually drives
    ///     the picture, and that undo returns the picture to exactly what it was - which is the
    ///     property a user judges an undo stack by.
    /// </remarks>
    [Collection("RealCache")]
    public sealed class RealCacheMapEditTests : IClassFixture<RealCacheFixture>
    {
        private readonly RealCacheFixture _fixture;

        public RealCacheMapEditTests(RealCacheFixture fixture)
        {
            _fixture = fixture;
        }

        [RealCacheFact]
        public void EditingChangesThePictureAndUndoRestoresIt()
        {
            RSCache cache = _fixture.OpenCache();
            var loader = new MapSquareLoader(cache);
            var rasteriser = new MapRasteriser(cache) { TilePixels = 4 };

            MapScene scene = MapScene.Load(loader, 50, 50);
            MapRegion centre = scene.Square(1, 1);
            Assert.NotNull(centre);

            int[] before = Snapshot(rasteriser, scene);

            var history = new MapEditHistory();

            //Repaint a 12x12 block in the middle of the square with a single underlay, which is
            //large enough to survive the blend and show up in the render.
            for (int x = 20; x < 32; x++)
                for (int y = 20; y < 32; y++)
                    history.Apply(new SetUnderlayEdit(centre, 0, x, y, 40));

            int[] after = Snapshot(rasteriser, scene);
            Assert.NotEqual(before, after);
            Assert.True(centre.Dirty);

            while (history.CanUndo)
                history.Undo();

            int[] restored = Snapshot(rasteriser, scene);
            Assert.Equal(before, restored);
        }

        /// <summary>
        ///     A painted overlay appears, and cycling its shape changes the picture again.
        /// </summary>
        [RealCacheFact]
        public void OverlayShapeCyclingChangesTheRender()
        {
            RSCache cache = _fixture.OpenCache();
            var loader = new MapSquareLoader(cache);
            var rasteriser = new MapRasteriser(cache) { TilePixels = 8 };

            MapScene scene = MapScene.Load(loader, 50, 50);
            MapRegion centre = scene.Square(1, 1);

            var history = new MapEditHistory();

            //Overlay 1 with shape 0 fills the tile.
            for (int x = 30; x < 34; x++)
                for (int y = 30; y < 34; y++)
                    history.Apply(new SetOverlayEdit(centre, 0, x, y, 1, 0, 0));

            int[] full = Snapshot(rasteriser, scene);

            //Shape 1 covers only part of it, so the picture must differ.
            for (int x = 30; x < 34; x++)
                for (int y = 30; y < 34; y++)
                    history.Apply(new SetOverlayEdit(centre, 0, x, y, 1, 1, 0));

            int[] partial = Snapshot(rasteriser, scene);
            Assert.NotEqual(full, partial);
        }

        /// <summary>Deleting a location removes it from the square and from the scene.</summary>
        [RealCacheFact]
        public void DeletingALocationRemovesItFromTheScene()
        {
            RSCache cache = _fixture.OpenCache();
            var loader = new MapSquareLoader(cache);

            MapScene scene = MapScene.Load(loader, 50, 50);
            MapRegion centre = scene.Square(1, 1);

            int originalCount = centre.GetLocations().Count;
            Assert.True(originalCount > 0, "the centre square has no locations to delete");

            Location victim = centre.GetLocations()[0];
            var history = new MapEditHistory();
            history.Apply(new RemoveLocationEdit(centre, victim));

            Assert.Equal(originalCount - 1, centre.GetLocations().Count);

            history.Undo();
            Assert.Equal(originalCount, centre.GetLocations().Count);
        }

        private static int[] Snapshot(MapRasteriser rasteriser, MapScene scene)
        {
            using DirectBitmap bitmap = rasteriser.Render(scene, 0);
            return (int[]) bitmap.Bits.Clone();
        }
    }
}
