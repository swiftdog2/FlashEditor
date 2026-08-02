using System;
using System.Collections.Generic;
using FlashEditor.Cache.Region;
using FlashEditor.Map;
using Xunit;

using MapRegion = FlashEditor.Cache.Region.Region;

namespace FlashEditor.Tests.Map
{
    /// <summary>
    ///     Pins the editing model: every edit reverses exactly, and the history stays linear.
    /// </summary>
    /// <remarks>
    ///     The property that matters is that undo restores the prior state <em>exactly</em>, not
    ///     approximately. An editor whose undo recomputes a value rather than restoring the captured
    ///     one drifts, and the drift only shows up on save.
    /// </remarks>
    public sealed class MapEditTests
    {
        private static MapRegion Square() => new MapRegion(MapSquareNames.RegionId(50, 50));

        [Fact]
        public void UnderlayEditRoundTrips()
        {
            MapRegion square = Square();
            square.SetUnderlayId(0, 5, 5, 42);
            square.ClearDirty();

            var edit = new SetUnderlayEdit(square, 0, 5, 5, 99);
            edit.Apply();
            Assert.Equal(99, square.GetUnderlayId(0, 5, 5));
            Assert.True(square.Dirty);

            edit.Undo();
            Assert.Equal(42, square.GetUnderlayId(0, 5, 5));
        }

        [Fact]
        public void OverlayEditRestoresShapeAndRotationToo()
        {
            MapRegion square = Square();
            square.SetOverlayId(0, 3, 4, 7);
            square.SetOverlayShape(0, 3, 4, 2);
            square.SetOverlayRotation(0, 3, 4, 1);

            var edit = new SetOverlayEdit(square, 0, 3, 4, 12, 5, 3);
            edit.Apply();

            Assert.Equal(12, square.GetOverlayId(0, 3, 4));
            Assert.Equal(5, square.GetOverlayShape(0, 3, 4));
            Assert.Equal(3, square.GetOverlayRotation(0, 3, 4));

            edit.Undo();

            //All three fields, not just the id. An overlay is meaningless without its shape.
            Assert.Equal(7, square.GetOverlayId(0, 3, 4));
            Assert.Equal(2, square.GetOverlayShape(0, 3, 4));
            Assert.Equal(1, square.GetOverlayRotation(0, 3, 4));
        }

        [Fact]
        public void HeightAndFlagEditsRoundTrip()
        {
            MapRegion square = Square();
            square.SetTileHeight(0, 1, 1, -320);
            square.SetRenderRule(0, 1, 1, 0x4);

            var height = new SetHeightEdit(square, 0, 1, 1, -960);
            var flags = new SetTileFlagsEdit(square, 0, 1, 1, 0x9);

            height.Apply();
            flags.Apply();
            Assert.Equal(-960, square.GetTileHeight(0, 1, 1));
            Assert.Equal(0x9, square.GetRenderRule(0, 1, 1));

            flags.Undo();
            height.Undo();
            Assert.Equal(-320, square.GetTileHeight(0, 1, 1));
            Assert.Equal(0x4, square.GetRenderRule(0, 1, 1));
        }

        [Fact]
        public void LocationAddAndRemoveRoundTrip()
        {
            MapRegion square = Square();
            Location loc = NewLoc(1234, 3, 7);

            var add = new AddLocationEdit(square, loc);
            add.Apply();
            Assert.Single(square.GetLocations());

            add.Undo();
            Assert.Empty(square.GetLocations());

            add.Apply();
            var remove = new RemoveLocationEdit(square, loc);
            remove.Apply();
            Assert.Empty(square.GetLocations());

            remove.Undo();
            Assert.Single(square.GetLocations());
            Assert.Same(loc, square.GetLocations()[0]);
        }

        [Fact]
        public void MovingALocationPreservesTheOriginalForUndo()
        {
            MapRegion square = Square();
            Location original = NewLoc(1234, 3, 7);
            square.AddLocation(original);

            Location moved = NewLoc(1234, 10, 20);
            var edit = new ReplaceLocationEdit(square, original, moved);

            edit.Apply();
            Assert.Single(square.GetLocations());
            Assert.Equal(10, square.GetLocations()[0].LocalX);

            edit.Undo();
            Assert.Single(square.GetLocations());

            //The same instance, not an equal copy: a rebuilt location would lose anything the
            //decoder captured that the constructor does not take.
            Assert.Same(original, square.GetLocations()[0]);
        }

        [Fact]
        public void CompositeUndoesInReverseOrder()
        {
            MapRegion square = Square();
            square.SetUnderlayId(0, 0, 0, 1);

            //Two edits to the same tile. Undoing them in order rather than in reverse would leave
            //the first edit's value behind.
            var first = new SetUnderlayEdit(square, 0, 0, 0, 2);
            first.Apply();
            var second = new SetUnderlayEdit(square, 0, 0, 0, 3);

            var composite = new CompositeEdit("brush", new IMapEdit[] { first, second });

            //first has already been applied once; Apply runs both again, which is idempotent here.
            composite.Apply();
            Assert.Equal(3, square.GetUnderlayId(0, 0, 0));

            composite.Undo();
            Assert.Equal(1, square.GetUnderlayId(0, 0, 0));
        }

        [Fact]
        public void EmptyCompositeIsRejected()
        {
            Assert.Throws<ArgumentException>(() => new CompositeEdit("nothing", Array.Empty<IMapEdit>()));
        }

        [Fact]
        public void HistoryUndoesAndRedoes()
        {
            MapRegion square = Square();
            var history = new MapEditHistory();

            Assert.False(history.CanUndo);
            Assert.False(history.CanRedo);

            history.Apply(new SetUnderlayEdit(square, 0, 0, 0, 5));
            history.Apply(new SetUnderlayEdit(square, 0, 0, 0, 9));

            Assert.Equal(9, square.GetUnderlayId(0, 0, 0));
            Assert.Equal(2, history.Count);

            history.Undo();
            Assert.Equal(5, square.GetUnderlayId(0, 0, 0));
            Assert.True(history.CanRedo);

            history.Undo();
            Assert.Equal(0, square.GetUnderlayId(0, 0, 0));
            Assert.False(history.CanUndo);

            history.Redo();
            Assert.Equal(5, square.GetUnderlayId(0, 0, 0));
        }

        [Fact]
        public void ApplyingAfterUndoDiscardsTheRedoStack()
        {
            MapRegion square = Square();
            var history = new MapEditHistory();

            history.Apply(new SetUnderlayEdit(square, 0, 0, 0, 5));
            history.Undo();
            Assert.True(history.CanRedo);

            history.Apply(new SetUnderlayEdit(square, 0, 0, 0, 7));

            //Branching history is what makes an undo stack confusing; the branch is dropped.
            Assert.False(history.CanRedo);
            Assert.Equal(7, square.GetUnderlayId(0, 0, 0));
        }

        [Fact]
        public void UndoingEverythingRestoresTheDecodedState()
        {
            MapRegion square = Square();
            square.SetUnderlayId(0, 2, 2, 11);
            square.SetOverlayId(0, 2, 2, 3);
            square.SetTileHeight(0, 2, 2, -640);
            Location loc = NewLoc(500, 2, 2);
            square.AddLocation(loc);

            var history = new MapEditHistory();
            history.Apply(new SetUnderlayEdit(square, 0, 2, 2, 44));
            history.Apply(new SetOverlayEdit(square, 0, 2, 2, 8, 3, 1));
            history.Apply(new SetHeightEdit(square, 0, 2, 2, -1280));
            history.Apply(new RemoveLocationEdit(square, loc));

            while (history.CanUndo)
                history.Undo();

            Assert.Equal(11, square.GetUnderlayId(0, 2, 2));
            Assert.Equal(3, square.GetOverlayId(0, 2, 2));
            Assert.Equal(-640, square.GetTileHeight(0, 2, 2));
            Assert.Single(square.GetLocations());
        }

        [Fact]
        public void DirtyTracksEditsAndClears()
        {
            MapRegion square = Square();
            Assert.False(square.Dirty);

            square.SetUnderlayId(0, 0, 0, 1);
            Assert.True(square.Dirty);

            square.ClearDirty();
            Assert.False(square.Dirty);

            //Undo is itself a write, so a square stays dirty after one. That is deliberate: the
            //square no longer matches whatever was last saved, even if it matches what was decoded.
            new SetUnderlayEdit(square, 0, 0, 0, 2).Apply();
            Assert.True(square.Dirty);
        }

        [Fact]
        public void RemovingAnAbsentLocationReportsFailure()
        {
            MapRegion square = Square();
            Assert.False(square.RemoveLocation(NewLoc(1, 1, 1)));
            Assert.False(square.Dirty);
        }

        private static Location NewLoc(int id, int x, int y) =>
            new Location(id, 10, 0, x, y, 0, new Position(3200 + x, 3200 + y, 0));
    }
}
