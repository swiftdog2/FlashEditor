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

    /// <summary>Changes a tile's floor underlay.</summary>
    public sealed class SetUnderlayEdit : IMapEdit {
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

        /// <inheritdoc/>
        public void Apply() => Target.SetUnderlayId(plane, x, y, newId);

        /// <inheritdoc/>
        public void Undo() => Target.SetUnderlayId(plane, x, y, oldId);
    }

    /// <summary>Changes a tile's floor overlay, including its shape and rotation.</summary>
    public sealed class SetOverlayEdit : IMapEdit {
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

    /// <summary>Changes a tile's height.</summary>
    public sealed class SetHeightEdit : IMapEdit {
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

        /// <inheritdoc/>
        public void Apply() => Target.SetTileHeight(plane, x, y, newHeight);

        /// <inheritdoc/>
        public void Undo() => Target.SetTileHeight(plane, x, y, oldHeight);
    }

    /// <summary>Changes a tile's flag byte.</summary>
    public sealed class SetTileFlagsEdit : IMapEdit {
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

        /// <inheritdoc/>
        public void Apply() => Target.SetRenderRule(plane, x, y, newFlags);

        /// <inheritdoc/>
        public void Undo() => Target.SetRenderRule(plane, x, y, oldFlags);
    }

    /// <summary>Adds a location to a square.</summary>
    public sealed class AddLocationEdit : IMapEdit {
        private readonly Location location;

        /// <summary>Prepares to add a location.</summary>
        public AddLocationEdit(MapRegion target, Location location) {
            Target = target;
            this.location = location;
        }

        /// <inheritdoc/>
        public MapRegion Target { get; }

        /// <inheritdoc/>
        public string Description => $"Add loc {location.Id} at {location.LocalX},{location.LocalY}";

        /// <inheritdoc/>
        public void Apply() => Target.AddLocation(location);

        /// <inheritdoc/>
        public void Undo() => Target.RemoveLocation(location);
    }

    /// <summary>Removes a location from a square.</summary>
    public sealed class RemoveLocationEdit : IMapEdit {
        private readonly Location location;

        /// <summary>Prepares to remove a location.</summary>
        public RemoveLocationEdit(MapRegion target, Location location) {
            Target = target;
            this.location = location;
        }

        /// <inheritdoc/>
        public MapRegion Target { get; }

        /// <inheritdoc/>
        public string Description => $"Remove loc {location.Id} at {location.LocalX},{location.LocalY}";

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
    public sealed class ReplaceLocationEdit : IMapEdit {
        private readonly Location original;
        private readonly Location replacement;

        /// <summary>Prepares to swap one location for another.</summary>
        public ReplaceLocationEdit(MapRegion target, Location original, Location replacement) {
            Target = target;
            this.original = original;
            this.replacement = replacement;
        }

        /// <inheritdoc/>
        public MapRegion Target { get; }

        /// <inheritdoc/>
        public string Description =>
            $"Move loc {original.Id} from {original.LocalX},{original.LocalY} to {replacement.LocalX},{replacement.LocalY}";

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
