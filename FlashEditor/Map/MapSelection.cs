using System;
using System.Collections.Generic;
using System.Drawing;

//System.Drawing.Region arrives through the implicit usings and collides with the map type.
using MapRegion = FlashEditor.Cache.Region.Region;

namespace FlashEditor.Map {
    /// <summary>
    ///     How a new set of tiles combines with the selection that is already there.
    /// </summary>
    /// <remarks>
    ///     Named rather than expressed as a pair of booleans, because the three cases are not
    ///     independent: "add and subtract" is not a state, and a caller holding two flags can spell
    ///     it.
    /// </remarks>
    public enum MapSelectionMode {
        /// <summary>Throw away what was selected and take the new tiles instead.</summary>
        Replace,

        /// <summary>Keep what was selected and add the new tiles. Shift.</summary>
        Add,

        /// <summary>Keep what was selected and take the new tiles out of it. Ctrl.</summary>
        Subtract
    }

    /// <summary>
    ///     The tiles an area operation will act on.
    /// </summary>
    /// <remarks>
    ///     <b>A set of world tiles, not a rectangle.</b> Freehand and the wand both produce shapes
    ///     no rectangle describes, and the three selection tools have to feed one thing that every
    ///     paint tool then reads. Held as world coordinates rather than square-local ones so a
    ///     selection can straddle a square boundary, which is the normal case rather than the
    ///     exception - a 30-tile brush stroke near an edge already does it.
    ///     <para>
    ///     <b>It knows which plane it was made on and drops itself when that changes.</b> A tile
    ///     coordinate means a different tile on another plane, so a selection carried across a
    ///     plane step would apply a fill to terrain the user never looked at.
    ///     </para>
    ///     <para>
    ///     <b>The square count is the number that matters, not the tile count.</b> Every square a
    ///     selection touches is re-encoded and rewritten when the cache is saved, so a 200-tile
    ///     selection laid across a square corner dirties four archives. <see cref="SquareCount"/>
    ///     exists so the status line can say that before the user commits to it.
    ///     </para>
    ///     <para>
    ///     No WinForms and no rendering here on purpose: every method is checkable without a window.
    ///     </para>
    /// </remarks>
    public sealed class MapSelection {
        /// <summary>
        ///     The most tiles a selection may hold, which is 64 map squares' worth.
        /// </summary>
        /// <remarks>
        ///     Not a memory limit - the set itself is small. It is a limit on the <em>squares</em> an
        ///     area operation would then have to decode, pin and rewrite: each one is a JS5 read on
        ///     the UI thread and a permanent pin for as long as the edit is undoable. Refusing at a
        ///     stated number is honest; silently truncating the shape the user drew is not.
        /// </remarks>
        public const int MaximumTiles = 64 * MapRegion.WIDTH * MapRegion.HEIGHT;

        private readonly HashSet<long> tiles = new HashSet<long>();

        private int plane;
        private int minX = int.MaxValue, minY = int.MaxValue, maxX = int.MinValue, maxY = int.MinValue;
        private bool boundsValid = true;

        /// <summary>Raised whenever the set or its plane changes.</summary>
        public event EventHandler? Changed;

        /// <summary>The plane the selection was made on.</summary>
        public int Plane => plane;

        /// <summary>How many tiles are selected.</summary>
        public int Count => tiles.Count;

        /// <summary>Whether nothing is selected.</summary>
        public bool IsEmpty => tiles.Count == 0;

        /// <summary>
        ///     How many map squares the selection spans.
        /// </summary>
        /// <remarks>
        ///     Counted rather than derived from the bounding box: a freehand or wand selection can
        ///     have a bounding box across nine squares while touching three of them, and telling the
        ///     user nine squares will be rewritten when three will is the same class of error as
        ///     telling them three when nine will.
        /// </remarks>
        public int SquareCount {
            get {
                var squares = new HashSet<int>();
                foreach (long packed in tiles)
                    squares.Add(SquareKey(UnpackX(packed), UnpackY(packed)));
                return squares.Count;
            }
        }

        /// <summary>Every selected tile, in world coordinates, in no particular order.</summary>
        public IEnumerable<(int WorldX, int WorldY)> Tiles {
            get {
                foreach (long packed in tiles)
                    yield return (UnpackX(packed), UnpackY(packed));
            }
        }

        /// <summary>Every map square the selection touches, as region coordinates.</summary>
        public IEnumerable<(int RegionX, int RegionY)> Squares {
            get {
                var seen = new HashSet<int>();
                foreach (long packed in tiles) {
                    int key = SquareKey(UnpackX(packed), UnpackY(packed));
                    if (seen.Add(key))
                        yield return (key >> 16, key & 0xFFFF);
                }
            }
        }

        /// <summary>Whether a world tile is selected.</summary>
        /// <param name="worldX">World X.</param>
        /// <param name="worldY">World Y.</param>
        /// <returns>Whether it is in the set.</returns>
        public bool Contains(int worldX, int worldY) => tiles.Contains(Pack(worldX, worldY));

        /// <summary>
        ///     The smallest tile rectangle containing the selection.
        /// </summary>
        /// <remarks>
        ///     For drawing a coarse marker at a zoom where individual tiles are sub-pixel, and for
        ///     the flash raised after an area fill. Empty when nothing is selected.
        /// </remarks>
        public Rectangle Bounds {
            get {
                EnsureBounds();
                return tiles.Count == 0
                    ? Rectangle.Empty
                    : Rectangle.FromLTRB(minX, minY, maxX + 1, maxY + 1);
            }
        }

        /// <summary>Empties the selection.</summary>
        public void Clear() {
            if (tiles.Count == 0)
                return;

            tiles.Clear();
            ResetBounds();
            Changed?.Invoke(this, EventArgs.Empty);
        }

        /// <summary>
        ///     Moves the selection to a plane, dropping it when the plane actually changes.
        /// </summary>
        /// <remarks>
        ///     Called from the panel's plane binding rather than left to the caller of every
        ///     selection tool, because the plane can change from three places - the combo, Ctrl and
        ///     the wheel, and PageUp - and a selection surviving any one of them applies a fill to
        ///     tiles nobody looked at.
        /// </remarks>
        /// <param name="newPlane">The plane now being viewed.</param>
        public void SetPlane(int newPlane) {
            if (newPlane == plane)
                return;

            plane = newPlane;
            tiles.Clear();
            ResetBounds();
            Changed?.Invoke(this, EventArgs.Empty);
        }

        /// <summary>
        ///     Combines a set of world tiles into the selection.
        /// </summary>
        /// <remarks>
        ///     The one mutator every selection tool goes through, so the cap and the change
        ///     notification are stated once. A <see cref="MapSelectionMode.Replace"/> that would
        ///     exceed <see cref="MaximumTiles"/> leaves the previous selection alone rather than
        ///     landing a truncated version of the shape that was drawn.
        /// </remarks>
        /// <param name="incoming">The tiles, in world coordinates.</param>
        /// <param name="mode">How they combine with what is already selected.</param>
        /// <returns>Whether the selection was changed, and why not when it was refused.</returns>
        public MapSelectionResult Apply(IEnumerable<(int WorldX, int WorldY)> incoming, MapSelectionMode mode) {
            if (incoming == null) throw new ArgumentNullException(nameof(incoming));

            var candidate = mode == MapSelectionMode.Replace
                ? new HashSet<long>()
                : new HashSet<long>(tiles);

            foreach ((int worldX, int worldY) in incoming) {
                long packed = Pack(worldX, worldY);
                if (mode == MapSelectionMode.Subtract)
                    candidate.Remove(packed);
                else
                    candidate.Add(packed);
            }

            if (candidate.Count > MaximumTiles)
                return MapSelectionResult.Refused(
                    $"That selection covers {candidate.Count:N0} tiles, past the {MaximumTiles:N0} " +
                    "an area operation will take. Every square it touches has to be decoded, pinned " +
                    "and rewritten on save, so it is refused rather than trimmed to a shape you did " +
                    "not draw.");

            if (candidate.SetEquals(tiles))
                return MapSelectionResult.Unchanged();

            tiles.Clear();
            foreach (long packed in candidate)
                tiles.Add(packed);

            ResetBounds();
            Changed?.Invoke(this, EventArgs.Empty);
            return MapSelectionResult.Changed();
        }

        /// <summary>
        ///     The tiles of an axis-aligned block, for the rectangle tool.
        /// </summary>
        /// <remarks>
        ///     Takes two opposite corners in either order, because a drag can start at any corner
        ///     and normalising at each call site is how a rectangle ends up empty when dragged up
        ///     and to the left.
        ///     <para>
        ///     Not named <c>Rectangle</c>: a method of that name would hide
        ///     <see cref="System.Drawing.Rectangle"/> everywhere inside this class, including in the
        ///     <see cref="Bounds"/> property that returns one.
        ///     </para>
        /// </remarks>
        /// <param name="x0">World X of one corner.</param>
        /// <param name="y0">World Y of one corner.</param>
        /// <param name="x1">World X of the opposite corner.</param>
        /// <param name="y1">World Y of the opposite corner.</param>
        /// <returns>Every tile in the block, inclusive of both corners.</returns>
        public static IEnumerable<(int WorldX, int WorldY)> RectangleTiles(int x0, int y0, int x1, int y1) {
            int left = Math.Min(x0, x1), right = Math.Max(x0, x1);
            int bottom = Math.Min(y0, y1), top = Math.Max(y0, y1);

            for (int y = bottom; y <= top; y++)
                for (int x = left; x <= right; x++)
                    yield return (x, y);
        }

        /// <summary>
        ///     How many tiles a rectangle would hold, without building it.
        /// </summary>
        /// <remarks>
        ///     A live drag readout needs the figure on every mouse move, and materialising a
        ///     quarter of a million tuples per move to count them is the kind of cost that turns a
        ///     drag into a slideshow.
        /// </remarks>
        /// <param name="x0">World X of one corner.</param>
        /// <param name="y0">World Y of one corner.</param>
        /// <param name="x1">World X of the opposite corner.</param>
        /// <param name="y1">World Y of the opposite corner.</param>
        /// <returns>The tile count.</returns>
        public static long RectangleTileCount(int x0, int y0, int x1, int y1) {
            long wide = Math.Abs((long) x1 - x0) + 1;
            long high = Math.Abs((long) y1 - y0) + 1;
            return wide * high;
        }

        /// <summary>
        ///     How many map squares a set of world tiles spans.
        /// </summary>
        /// <remarks>
        ///     Static so a drag preview can report the figure before anything is committed to the
        ///     selection, using exactly the arithmetic <see cref="SquareCount"/> uses.
        /// </remarks>
        /// <param name="worldTiles">The tiles.</param>
        /// <returns>The number of distinct squares.</returns>
        public static int SquareSpanOf(IEnumerable<(int WorldX, int WorldY)> worldTiles) {
            if (worldTiles == null) throw new ArgumentNullException(nameof(worldTiles));

            var squares = new HashSet<int>();
            foreach ((int worldX, int worldY) in worldTiles)
                squares.Add(SquareKey(worldX, worldY));
            return squares.Count;
        }

        private static int SquareKey(int worldX, int worldY) =>
            ((worldX / MapRegion.WIDTH) << 16) | (worldY / MapRegion.HEIGHT);

        //The world is 256 squares of 64 tiles on each axis, so 14 bits carries a coordinate and the
        //pair fits a long with room to spare. Packing rather than holding a tuple keeps the set's
        //hashing on a primitive.
        private static long Pack(int worldX, int worldY) => ((long) worldX << 32) | (uint) worldY;

        private static int UnpackX(long packed) => (int) (packed >> 32);

        private static int UnpackY(long packed) => (int) (packed & 0xFFFFFFFFL);

        private void ResetBounds() {
            boundsValid = tiles.Count == 0;
            minX = int.MaxValue;
            minY = int.MaxValue;
            maxX = int.MinValue;
            maxY = int.MinValue;
        }

        private void EnsureBounds() {
            if (boundsValid)
                return;

            foreach (long packed in tiles) {
                int x = UnpackX(packed), y = UnpackY(packed);
                if (x < minX) minX = x;
                if (x > maxX) maxX = x;
                if (y < minY) minY = y;
                if (y > maxY) maxY = y;
            }

            boundsValid = true;
        }
    }

    /// <summary>
    ///     What happened when a selection tool tried to change the selection.
    /// </summary>
    /// <remarks>
    ///     A refusal carries its own sentence rather than a code, for the same reason the underlay
    ///     cap does: the only useful thing to do with it is put it on the status line, and a caller
    ///     that has to write the sentence itself writes a different one on each of the three paths.
    /// </remarks>
    public readonly struct MapSelectionResult {
        private MapSelectionResult(bool changed, string? refusal) {
            DidChange = changed;
            Refusal = refusal;
        }

        /// <summary>Whether the selection is now different.</summary>
        public bool DidChange { get; }

        /// <summary>Why the change was refused, or <c>null</c> when it was not.</summary>
        public string? Refusal { get; }

        /// <summary>Whether the change was refused outright.</summary>
        public bool WasRefused => Refusal != null;

        internal static MapSelectionResult Changed() => new MapSelectionResult(true, null);

        internal static MapSelectionResult Unchanged() => new MapSelectionResult(false, null);

        internal static MapSelectionResult Refused(string reason) => new MapSelectionResult(false, reason);
    }
}
