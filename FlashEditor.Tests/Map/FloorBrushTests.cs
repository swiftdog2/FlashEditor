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

        [Fact]
        public void EyedropperOnAnOverlayThenPaint_StoresExactlyThePickedStoredValue()
        {
            MapRegion square = Square();
            square.SetUnderlayId(0, 5, 5, 60);
            square.SetOverlayId(0, 5, 5, 19);

            FloorPick? pick = FloorBrush.PickUp(square, 0, 5, 5);
            Assert.Equal(FloorKind.Overlay, pick?.Kind);

            FloorBrushLoad load = FloorBrush.Load(pick!.Value);
            new SetOverlayEdit(square, 0, 20, 20, load.StoredValue, 0, 0).Apply();

            Assert.Equal(19, square.GetOverlayId(0, 20, 20));
        }

        [Fact]
        public void EyedropperOnABareUnderlayThenPaint_StoresExactlyThePickedStoredValue()
        {
            MapRegion square = Square();
            square.SetUnderlayId(0, 5, 5, 60);

            FloorPick? pick = FloorBrush.PickUp(square, 0, 5, 5);
            Assert.Equal(FloorKind.Underlay, pick?.Kind);

            FloorBrushLoad load = FloorBrush.Load(pick!.Value);
            new SetUnderlayEdit(square, 0, 20, 20, load.StoredValue).Apply();

            Assert.Equal(60, square.GetUnderlayId(0, 20, 20));
        }

        [Theory]
        [InlineData(FloorKind.Underlay, MapToolLimits.MaximumUnderlayId - 1, true)]
        [InlineData(FloorKind.Underlay, MapToolLimits.MaximumUnderlayId, false)]
        [InlineData(FloorKind.Overlay, MapToolLimits.MaximumOverlayId - 1, true)]
        [InlineData(FloorKind.Overlay, MapToolLimits.MaximumOverlayId, false)]
        public void APickIsCheckedAgainstTheStoredCap_NotTheDefinitionId(
            FloorKind kind, int definitionId, bool accepted)
        {
            FloorBrushLoad load = FloorBrush.Load(new FloorPick(kind, definitionId));

            Assert.Equal(accepted, load.Refusal == null);
        }

        [Fact]
        public void EyedropperOnATileWithNoFloor_PicksNothing()
        {
            Assert.Null(FloorBrush.PickUp(Square(), 0, 5, 5));
        }
    }
}
