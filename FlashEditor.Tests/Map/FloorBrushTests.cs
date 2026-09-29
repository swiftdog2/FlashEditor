using FlashEditor.Cache.Region;
using FlashEditor.Map;
using Xunit;

using MapRegion = FlashEditor.Cache.Region.Region;

namespace FlashEditor.Tests.Map
{
    /// <summary>
    ///     Pins the unit the floor brush holds: a palette pick is a definition id, a tile stores
    ///     definition id + 1, and the brush carries the stored value.
    /// </summary>
    /// <remarks>
    ///     The client reads both floor fields as stored - 1 (<c>Class305.java:925</c> for overlays,
    ///     <c>:932</c> for underlays), so a brush that wrote the definition id straight into the tile
    ///     painted the record one below the swatch the user clicked.
    /// </remarks>
    public sealed class FloorBrushTests
    {
        private static MapRegion Square() => new MapRegion(MapSquareNames.RegionId(50, 50));

        [Fact]
        public void PalettePickOfOverlayDefinition18_PaintsStoredValue19()
        {
            MapRegion square = Square();

            FloorBrushLoad load = FloorBrush.Load(new FloorPick(FloorKind.Overlay, 18));
            Assert.Null(load.Refusal);

            new SetOverlayEdit(square, 0, 10, 10, load.StoredValue, 0, 0).Apply();

            Assert.Equal(19, square.GetOverlayId(0, 10, 10));
        }

        [Fact]
        public void PalettePickOfUnderlayDefinition40_PaintsStoredValue41()
        {
            MapRegion square = Square();

            FloorBrushLoad load = FloorBrush.Load(new FloorPick(FloorKind.Underlay, 40));
            Assert.Null(load.Refusal);

            new SetUnderlayEdit(square, 0, 10, 10, load.StoredValue).Apply();

            Assert.Equal(41, square.GetUnderlayId(0, 10, 10));
        }
    }
}
