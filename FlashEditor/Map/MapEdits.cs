using System;
using System.Collections.Generic;
using FlashEditor.Cache.Region;

using MapRegion = FlashEditor.Cache.Region.Region;

namespace FlashEditor.Map {
    /// <summary>A reversible change to a map square.</summary>
    /// <remarks>
    ///     Every edit captures the value it replaced at construction, so undo restores exactly what
    ///     was there rather than recomputing it. That matters for fields with sentinel values - an
    ///     overlay id of 0 and an overlay id that happens to decode to 0 are not the same thing to
    ///     the encoder.
    /// </remarks>
    public interface IMapEdit {
        /// <summary>A short description for an undo menu.</summary>
        string Description { get; }

        /// <summary>The square this edit applies to.</summary>
        MapRegion Target { get; }

        /// <summary>Applies the change.</summary>
        void Apply();

        /// <summary>Reverses the change.</summary>
        void Undo();
    }

    /// <summary>
    ///     An edit that can name the tiles it changed, so a view can highlight them.
    /// </summary>
    /// <remarks>
    ///     Separate from <see cref="IMapEdit"/> rather than folded into it, because a
    ///     <see cref="CompositeEdit"/> spans squares and has no single area to report. A caller
    ///     tests for this and simply draws nothing when an edit does not implement it.
    ///
    ///     It exists so that undo and redo flash the same way an edit does. Working the footprint
    ///     out at the click site would have covered only the forward direction, and an undo that
    ///     silently reverts something off screen is the same "did anything happen" problem in
    ///     reverse.
    /// </remarks>
    public interface IMapEditArea {
        /// <summary>The plane the change is on.</summary>
        int Plane { get; }

        /// <summary>Square-local X of the south-west tile of the changed block, 0..63.</summary>
        int LocalX { get; }

        /// <summary>Square-local Y of the south-west tile of the changed block, 0..63.</summary>
        int LocalY { get; }

        /// <summary>Tiles east the change covers, at least one.</summary>
        int TilesWide { get; }

        /// <summary>Tiles north the change covers, at least one.</summary>
        int TilesHigh { get; }
    }

    /// <summary>Changes a tile's floor underlay.</summary>
    public sealed class SetUnderlayEdit : IMapEdit, IMapEditArea {
        private readonly int plane, x, y, newId, oldId;

        /// <summary>Captures the current underlay and prepares to replace it.</summary>
        public SetUnderlayEdit(MapRegion target, int plane, int x, int y, int newId) {
            Target = target;
            this.plane = plane;
            this.x = x;
            this.y = y;
            this.newId = newId;
            oldId = target.GetUnderlayId(plane, x, y);
        }

        /// <inheritdoc/>
        public MapRegion Target { get; }

        /// <inheritdoc/>
        public string Description => $"Underlay {oldId} to {newId} at {x},{y}";

        /// <summary>The underlay that was there before.</summary>
        public int OldId => oldId;

        /// <summary>The underlay written.</summary>
        public int NewId => newId;

        /// <inheritdoc/>
        public int Plane => plane;

        /// <inheritdoc/>
        public int LocalX => x;

        /// <inheritdoc/>
        public int LocalY => y;

        /// <inheritdoc/>
        public int TilesWide => 1;

        /// <inheritdoc/>
        public int TilesHigh => 1;

        /// <inheritdoc/>
        public void Apply() => Target.SetUnderlayId(plane, x, y, newId);

        /// <inheritdoc/>
        public void Undo() => Target.SetUnderlayId(plane, x, y, oldId);
    }

    /// <summary>Changes a tile's floor overlay, including its shape and rotation.</summary>
    public sealed class SetOverlayEdit : IMapEdit, IMapEditArea {
        private readonly int plane, x, y, newId, oldId;
        private readonly byte newShape, oldShape, newRotation, oldRotation;

        /// <summary>Captures the current overlay and prepares to replace it.</summary>
        public SetOverlayEdit(MapRegion target, int plane, int x, int y, int id, byte shape, byte rotation) {
            Target = target;
            this.plane = plane;
            this.x = x;
            this.y = y;

            newId = id;
            newShape = shape;
            newRotation = rotation;

            oldId = target.GetOverlayId(plane, x, y);
            oldShape = target.GetOverlayShape(plane, x, y);
            oldRotation = target.GetOverlayRotation(plane, x, y);
        }

        /// <inheritdoc/>
        public MapRegion Target { get; }

        /// <inheritdoc/>
        public string Description => $"Overlay {oldId} to {newId} at {x},{y}";

        /// <summary>The overlay that was there before.</summary>
        public int OldId => oldId;

        /// <summary>The overlay written.</summary>
        public int NewId => newId;

        /// <summary>The shape written.</summary>
        public byte NewShape => newShape;

        /// <summary>The rotation written.</summary>
        public byte NewRotation => newRotation;

        /// <inheritdoc/>
        public int Plane => plane;

        /// <inheritdoc/>
        public int LocalX => x;

        /// <inheritdoc/>
        public int LocalY => y;

        /// <inheritdoc/>
        public int TilesWide => 1;

        /// <inheritdoc/>
        public int TilesHigh => 1;

        /// <inheritdoc/>
        public void Apply() => Write(newId, newShape, newRotation);

        /// <inheritdoc/>
        public void Undo() => Write(oldId, oldShape, oldRotation);

        private void Write(int id, byte shape, byte rotation) {
            Target.SetOverlayId(plane, x, y, id);
            Target.SetOverlayShape(plane, x, y, shape);
            Target.SetOverlayRotation(plane, x, y, rotation);
        }
    }

    /// <summary>
    ///     Changes a tile's height, which is the elevation of its south-west corner vertex.
    /// </summary>
    /// <remarks>
    ///     Worth stating on the type, because it is the single most misread thing in the editor.
    ///     The value is not the altitude of a tile: it is the altitude of one vertex, shared with
    ///     the three tiles to the west, the south and the south-west, so writing it bends a
    ///     two-by-two block of the terrain surface. Heights are also negative-up, so
    ///     <see cref="NewHeight"/> being <em>lower</em> than <see cref="OldHeight"/> means the
    ///     ground went up. <see cref="StepDelta"/> exists so callers stop getting that backwards.
    /// </remarks>
    public sealed class SetHeightEdit : IMapEdit, IMapEditArea {
        private readonly int plane, x, y, newHeight, oldHeight;

        /// <summary>Captures the current height and prepares to replace it.</summary>
        public SetHeightEdit(MapRegion target, int plane, int x, int y, int height) {
            Target = target;
            this.plane = plane;
            this.x = x;
            this.y = y;
            newHeight = height;
            oldHeight = target.GetTileHeight(plane, x, y);
        }

        /// <inheritdoc/>
        public MapRegion Target { get; }

        /// <inheritdoc/>
        public string Description => $"Height {oldHeight} to {newHeight} at {x},{y}";

        /// <summary>The height before the edit, in world units.</summary>
        public int OldHeight => oldHeight;

        /// <summary>The height after the edit, in world units.</summary>
        public int NewHeight => newHeight;

        /// <summary>
        ///     How many storable steps the ground rose, negative for a drop.
        /// </summary>
        /// <remarks>
        ///     Sign-corrected here rather than at every call site. Heights are negative-up and one
        ///     step is <c>Region.HEIGHT_UNITS_PER_STEP</c> world units, so the arithmetic that
        ///     turns a raw pair of heights into "went up by one" is exactly the arithmetic a reader
        ///     gets wrong.
        /// </remarks>
        public int StepDelta => (oldHeight - newHeight) / MapRegion.HEIGHT_UNITS_PER_STEP;

        /// <inheritdoc/>
        public int Plane => plane;

        /// <inheritdoc/>
        public int LocalX => x;

        /// <inheritdoc/>
        public int LocalY => y;

        /// <inheritdoc/>
        public int TilesWide => 1;

        /// <inheritdoc/>
        public int TilesHigh => 1;

        /// <inheritdoc/>
        public void Apply() => Target.SetTileHeight(plane, x, y, newHeight);

        /// <inheritdoc/>
        public void Undo() => Target.SetTileHeight(plane, x, y, oldHeight);
    }

    /// <summary>Changes a tile's flag byte.</summary>
    public sealed class SetTileFlagsEdit : IMapEdit, IMapEditArea {
        private readonly int plane, x, y;
        private readonly byte newFlags, oldFlags;

        /// <summary>Captures the current flags and prepares to replace them.</summary>
        public SetTileFlagsEdit(MapRegion target, int plane, int x, int y, byte flags) {
            Target = target;
            this.plane = plane;
            this.x = x;
            this.y = y;
            newFlags = flags;
            oldFlags = target.GetRenderRule(plane, x, y);
        }

        /// <inheritdoc/>
        public MapRegion Target { get; }

        /// <inheritdoc/>
        public string Description => $"Flags 0x{oldFlags:X2} to 0x{newFlags:X2} at {x},{y}";

        /// <summary>The flag byte before the edit.</summary>
        public byte OldFlags => oldFlags;

        /// <summary>The flag byte written.</summary>
        public byte NewFlags => newFlags;

        /// <inheritdoc/>
        public int Plane => plane;

        /// <inheritdoc/>
        public int LocalX => x;

        /// <inheritdoc/>
        public int LocalY => y;

        /// <inheritdoc/>
        public int TilesWide => 1;

        /// <inheritdoc/>
        public int TilesHigh => 1;

        /// <inheritdoc/>
        public void Apply() => Target.SetRenderRule(plane, x, y, newFlags);

        /// <inheritdoc/>
        public void Undo() => Target.SetRenderRule(plane, x, y, oldFlags);
    }

    /// <summary>Adds a location to a square.</summary>
    public sealed class AddLocationEdit : IMapEdit, IMapEditArea {
        private readonly Location location;

        /// <summary>
        ///     Prepares to add a location.
        /// </summary>
        /// <remarks>
        ///     The footprint is passed in rather than read from the object definition here, because
        ///     that lookup needs the cache and this type deliberately holds only the square. It is
        ///     used for the edit highlight and nothing else, so a caller with no definition to hand
        ///     can leave it at one tile and lose only some of the feedback.
        /// </remarks>
        /// <param name="target">The square.</param>
        /// <param name="location">The location to add.</param>
        /// <param name="tilesWide">The object's footprint east, after rotation.</param>
        /// <param name="tilesHigh">The object's footprint north, after rotation.</param>
        public AddLocationEdit(MapRegion target, Location location, int tilesWide = 1, int tilesHigh = 1) {
            Target = target;
            this.location = location;
            TilesWide = Math.Max(1, tilesWide);
            TilesHigh = Math.Max(1, tilesHigh);
        }

        /// <inheritdoc/>
        public MapRegion Target { get; }

        /// <inheritdoc/>
        public string Description => $"Add loc {location.Id} at {location.LocalX},{location.LocalY}";

        /// <summary>The location added.</summary>
        public Location Location => location;

        /// <inheritdoc/>
        public int Plane => location.Plane;

        /// <inheritdoc/>
        public int LocalX => location.LocalX;

        /// <inheritdoc/>
        public int LocalY => location.LocalY;

        /// <inheritdoc/>
        public int TilesWide { get; }

        /// <inheritdoc/>
        public int TilesHigh { get; }

        /// <inheritdoc/>
        public void Apply() => Target.AddLocation(location);

        /// <inheritdoc/>
        public void Undo() => Target.RemoveLocation(location);
    }

    /// <summary>Removes a location from a square.</summary>
    public sealed class RemoveLocationEdit : IMapEdit, IMapEditArea {
        private readonly Location location;

        /// <summary>Prepares to remove a location.</summary>
        /// <param name="target">The square.</param>
        /// <param name="location">The location to remove.</param>
        /// <param name="tilesWide">The object's footprint east, for the edit highlight.</param>
        /// <param name="tilesHigh">The object's footprint north, for the edit highlight.</param>
        public RemoveLocationEdit(MapRegion target, Location location, int tilesWide = 1, int tilesHigh = 1) {
            Target = target;
            this.location = location;
            TilesWide = Math.Max(1, tilesWide);
            TilesHigh = Math.Max(1, tilesHigh);
        }

        /// <inheritdoc/>
        public MapRegion Target { get; }

        /// <inheritdoc/>
        public string Description => $"Remove loc {location.Id} at {location.LocalX},{location.LocalY}";

        /// <summary>The location removed.</summary>
        public Location Location => location;

        /// <inheritdoc/>
        public int Plane => location.Plane;

        /// <inheritdoc/>
        public int LocalX => location.LocalX;

        /// <inheritdoc/>
        public int LocalY => location.LocalY;

        /// <inheritdoc/>
        public int TilesWide { get; }

        /// <inheritdoc/>
        public int TilesHigh { get; }

        /// <inheritdoc/>
        public void Apply() => Target.RemoveLocation(location);

        /// <inheritdoc/>
        public void Undo() => Target.AddLocation(location);
    }

    /// <summary>
    ///     Replaces a location with a modified copy.
    /// </summary>
    /// <remarks>
    ///     <see cref="Location"/> is immutable, so moving or rotating one is a remove and an add
    ///     rather than a mutation. Keeping it immutable is what lets an edit hold a reference to the
    ///     original and restore it exactly.
    /// </remarks>
    public sealed class ReplaceLocationEdit : IMapEdit, IMapEditArea {
        private readonly Location original;
        private readonly Location replacement;

        /// <summary>Prepares to swap one location for another.</summary>
        /// <param name="target">The square.</param>
        /// <param name="original">The location being replaced.</param>
        /// <param name="replacement">What takes its place.</param>
        /// <param name="tilesWide">The replacement's footprint east, for the edit highlight.</param>
        /// <param name="tilesHigh">The replacement's footprint north, for the edit highlight.</param>
        public ReplaceLocationEdit(MapRegion target, Location original, Location replacement,
            int tilesWide = 1, int tilesHigh = 1) {
            Target = target;
            this.original = original;
            this.replacement = replacement;
            TilesWide = Math.Max(1, tilesWide);
            TilesHigh = Math.Max(1, tilesHigh);
        }

        /// <inheritdoc/>
        public MapRegion Target { get; }

        /// <inheritdoc/>
        public string Description =>
            $"Move loc {original.Id} from {original.LocalX},{original.LocalY} to {replacement.LocalX},{replacement.LocalY}";

        /// <summary>The location that was replaced.</summary>
        public Location Original => original;

        /// <summary>What replaced the original.</summary>
        public Location Replacement => replacement;

        /// <inheritdoc/>
        public int Plane => replacement.Plane;

        /// <inheritdoc/>
        public int LocalX => replacement.LocalX;

        /// <inheritdoc/>
        public int LocalY => replacement.LocalY;

        /// <inheritdoc/>
        public int TilesWide { get; }

        /// <inheritdoc/>
        public int TilesHigh { get; }

        /// <inheritdoc/>
        public void Apply() {
            Target.RemoveLocation(original);
            Target.AddLocation(replacement);
        }

        /// <inheritdoc/>
        public void Undo() {
            Target.RemoveLocation(replacement);
            Target.AddLocation(original);
        }
    }

    /// <summary>Several edits applied and undone as one.</summary>
    /// <remarks>Used by brush strokes, where one drag is one undo step.</remarks>
    public sealed class CompositeEdit : IMapEdit {
        private readonly List<IMapEdit> edits;

        /// <summary>Groups edits into a single step.</summary>
        /// <param name="description">What the group did.</param>
        /// <param name="edits">The edits, in application order.</param>
        public CompositeEdit(string description, IEnumerable<IMapEdit> edits) {
            Description = description;
            this.edits = new List<IMapEdit>(edits);
            if (this.edits.Count == 0)
                throw new ArgumentException("A composite edit needs at least one edit", nameof(edits));
        }

        /// <inheritdoc/>
        public string Description { get; }

        /// <inheritdoc/>
        public MapRegion Target => edits[0].Target;

        /// <summary>
        ///     The edits in the group, in application order.
        /// </summary>
        /// <remarks>
        ///     Exposed so a caller can ask what kind of change the group made. The height tools'
        ///     "you will not see this without relief shading" warning is the case that needs it: it
        ///     tested the edit for <see cref="SetHeightEdit"/>, which an area fill of ten thousand
        ///     height edits is not, so the warning went silent exactly where it mattered most.
        /// </remarks>
        public IReadOnlyList<IMapEdit> Edits => edits;

        /// <summary>The squares this group touches.</summary>
        public IEnumerable<MapRegion> Targets {
            get {
                var seen = new HashSet<MapRegion>();
                foreach (IMapEdit edit in edits)
                    if (seen.Add(edit.Target))
                        yield return edit.Target;
            }
        }

        /// <inheritdoc/>
        public void Apply() {
            foreach (IMapEdit edit in edits)
                edit.Apply();
        }

        /// <inheritdoc/>
        public void Undo() {
            //Reverse order, so overlapping edits unwind in the order they were laid down.
            for (int i = edits.Count - 1; i >= 0; i--)
                edits[i].Undo();
        }
    }
}
