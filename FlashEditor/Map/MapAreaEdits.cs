using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;

//System.Drawing.Region arrives through the implicit usings and collides with the map type.
using MapRegion = FlashEditor.Cache.Region.Region;

namespace FlashEditor.Map {
    /// <summary>What an area operation writes to every tile it covers.</summary>
    /// <remarks>
    ///     Deliberately smaller than the panel's tool list. Only the tools whose effect is defined
    ///     per tile can be applied over an area at all - cycling the shape of whatever object
    ///     happens to be on top of each of ten thousand tiles is not an operation anybody means to
    ///     ask for, and neither is placing ten thousand copies of one object.
    /// </remarks>
    public enum MapAreaTool {
        /// <summary>Write one underlay id to every tile.</summary>
        Underlay,

        /// <summary>Write one overlay id, shape and rotation to every tile.</summary>
        Overlay,

        /// <summary>Set or clear the blocked bit on every tile.</summary>
        BlockedFlag,

        /// <summary>Raise every tile's own south-west vertex by one storable step.</summary>
        RaiseHeight,

        /// <summary>Lower every tile's own south-west vertex by one storable step.</summary>
        LowerHeight
    }

    /// <summary>What an area operation is being asked to write.</summary>
    /// <remarks>
    ///     One struct rather than a parameter list, because five of the six fields are only read by
    ///     one tool each and a positional call would be five zeroes and a value at every site.
    /// </remarks>
    public struct MapAreaOptions {
        /// <summary>The underlay or overlay id to write.</summary>
        public int Value { get; set; }

        /// <summary>The overlay tile shape to write, 0 being the whole tile.</summary>
        public byte OverlayShape { get; set; }

        /// <summary>The overlay rotation to write, 0..3.</summary>
        public byte OverlayRotation { get; set; }

        /// <summary>Whether the blocked bit is being set rather than cleared.</summary>
        public bool Blocked { get; set; }
    }

    /// <summary>
    ///     The outcome of asking for an area operation: one undo step, or a refusal in words.
    /// </summary>
    /// <remarks>
    ///     <see cref="Skipped"/> is not noise. An area fill that changes 4,000 of 4,096 tiles has
    ///     told the user something real - the other 96 already held the value - and an operation
    ///     that changes nothing at all looks exactly like a broken tool unless it says so.
    /// </remarks>
    public sealed class MapAreaEditResult {
        private MapAreaEditResult(CompositeEdit? edit, int changed, int skipped, int squares,
            Rectangle bounds, string? refusal) {
            Edit = edit;
            Changed = changed;
            Skipped = skipped;
            Squares = squares;
            Bounds = bounds;
            Refusal = refusal;
        }

        /// <summary>The single undo step to apply, or <c>null</c> when nothing would change.</summary>
        public CompositeEdit? Edit { get; }

        /// <summary>How many tiles the edit writes to.</summary>
        public int Changed { get; }

        /// <summary>How many covered tiles already held the value and were left alone.</summary>
        public int Skipped { get; }

        /// <summary>How many map squares the edit touches, all of which are rewritten on save.</summary>
        public int Squares { get; }

        /// <summary>The tile rectangle the edit spans, for a flash.</summary>
        public Rectangle Bounds { get; }

        /// <summary>Why the operation was refused, or <c>null</c>.</summary>
        public string? Refusal { get; }

        /// <summary>Whether the operation was refused outright.</summary>
        public bool WasRefused => Refusal != null;

        internal static MapAreaEditResult Refused(string reason) =>
            new MapAreaEditResult(null, 0, 0, 0, Rectangle.Empty, reason);

        internal static MapAreaEditResult Built(CompositeEdit? edit, int changed, int skipped,
            int squares, Rectangle bounds) =>
            new MapAreaEditResult(edit, changed, skipped, squares, bounds, null);
    }

    /// <summary>
    ///     Turns a set of tiles and a tool into one undoable edit.
    /// </summary>
    /// <remarks>
    ///     <b>Why this is not in the panel.</b> Three rules meet here and every one of them is
    ///     easier to get wrong at a call site than to state once: the underlay cap is a property of
    ///     the byte a tile is stored in and has to refuse rather than clamp, a fill is one undo step
    ///     rather than ten thousand, and a tile that already holds the value must not be written at
    ///     all. Written into the click handler they would be checkable only through a window, which
    ///     is to say not at all - nothing in the suite covers WinForms.
    ///     <para>
    ///     <b>Tiles are visited in a stated order.</b> The selection is a hash set, whose
    ///     enumeration order is an implementation detail, so the edits are sorted before they are
    ///     grouped. It does not change the bytes - the per-tile edits are independent - but it does
    ///     decide the order undo unwinds them in, and a reproducible undo is worth the sort.
    ///     Ordered with <c>OrderBy</c> rather than <c>List.Sort</c>, which is the standing rule on
    ///     this path: <c>Sort</c> is unstable and has already reordered two equal loc records.
    ///     </para>
    /// </remarks>
    public static class MapAreaEdits {
        /// <summary>
        ///     Builds one undo step covering every tile a tool should write.
        /// </summary>
        /// <param name="tiles">The tiles to cover, in world coordinates.</param>
        /// <param name="plane">The plane to write on.</param>
        /// <param name="tool">What to write.</param>
        /// <param name="options">The values to write.</param>
        /// <param name="squareAt">
        ///     Resolves a world tile to the square holding it, decoding it if need be, or answers
        ///     <c>null</c> where the cache has no square. Passed in rather than taken as a store, so
        ///     this can be exercised against squares built by hand.
        /// </param>
        /// <returns>The result, refusal included.</returns>
        public static MapAreaEditResult Build(IEnumerable<(int WorldX, int WorldY)> tiles, int plane,
            MapAreaTool tool, MapAreaOptions options, Func<int, int, MapRegion?> squareAt) {
            if (tiles == null) throw new ArgumentNullException(nameof(tiles));
            if (squareAt == null) throw new ArgumentNullException(nameof(squareAt));

            string? refusal = CheckValue(tool, options.Value);
            if (refusal != null)
                return MapAreaEditResult.Refused(refusal);

            var edits = new List<IMapEdit>();
            var squares = new HashSet<MapRegion>();

            int skipped = 0;
            int minX = int.MaxValue, minY = int.MaxValue, maxX = int.MinValue, maxY = int.MinValue;

            foreach ((int worldX, int worldY) in tiles.OrderBy(t => t.WorldY).ThenBy(t => t.WorldX)) {
                MapRegion? square = squareAt(worldX, worldY);
                if (square == null)
                    continue;

                int localX = worldX - square.GetBaseX();
                int localY = worldY - square.GetBaseY();

                if (localX < 0 || localY < 0 || localX >= MapRegion.WIDTH || localY >= MapRegion.HEIGHT)
                    continue;

                //The 900 underwater squares carry a single plane, and Region indexes its grids
                //unguarded, so a selection dragged across one on plane 1 would throw mid-fill and
                //leave the history holding half a stroke.
                if (plane < 0 || plane >= square.PlaneCount)
                    continue;

                IMapEdit? edit = BuildOne(tool, options, square, plane, localX, localY);
                if (edit == null) {
                    skipped++;
                    continue;
                }

                edits.Add(edit);
                squares.Add(square);

                if (worldX < minX) minX = worldX;
                if (worldX > maxX) maxX = worldX;
                if (worldY < minY) minY = worldY;
                if (worldY > maxY) maxY = worldY;
            }

            if (edits.Count == 0)
                return MapAreaEditResult.Built(null, 0, skipped, 0, Rectangle.Empty);

            var bounds = Rectangle.FromLTRB(minX, minY, maxX + 1, maxY + 1);
            var composite = new CompositeEdit(Describe(tool, options, edits.Count, squares.Count), edits);

            return MapAreaEditResult.Built(composite, edits.Count, skipped, squares.Count, bounds);
        }

        /// <summary>
        ///     Whether a value is one a tile can actually store, in the user's terms.
        /// </summary>
        /// <remarks>
        ///     <b>The underlay cap lives here as well as at the palette, and it refuses rather than
        ///     clamps.</b> A tile writes its underlay as <c>id + 81</c> in one byte
        ///     (<c>RegionCodec.EncodeTile</c>), so 175 and above wraps and the tile silently decodes
        ///     as something else entirely. Clamping a fill to 174 would write a floor the user did
        ///     not choose across ten thousand tiles, which is far worse than doing nothing; and an
        ///     area fill that reached the edit types directly, without this, would be exactly the
        ///     route around the check that the single-tile path already refuses to take.
        /// </remarks>
        /// <param name="tool">The tool.</param>
        /// <param name="value">The value it would write.</param>
        /// <returns>The refusal, or <c>null</c> when the value fits.</returns>
        private static string? CheckValue(MapAreaTool tool, int value) {
            switch (tool) {
                case MapAreaTool.Underlay:
                    return value < 0 || value > MapToolLimits.MaximumUnderlayId
                        ? $"Underlay {value} is past the {MapToolLimits.MaximumUnderlayId} a tile can " +
                          "store - it is written as id + 81 in one byte - so nothing was filled."
                        : null;

                case MapAreaTool.Overlay:
                    return value < 0 || value > MapToolLimits.MaximumOverlayId
                        ? $"Overlay {value} is past the {MapToolLimits.MaximumOverlayId} a tile can " +
                          "store in its one byte, so nothing was filled."
                        : null;

                default:
                    return null;
            }
        }

        /// <summary>
        ///     One tile's edit, or <c>null</c> when the tile already holds what is being written.
        /// </summary>
        /// <remarks>
        ///     The no-op test is per field rather than on the edit as a whole, because an overlay
        ///     carries three of them and matching on the id alone would skip a tile whose shape or
        ///     rotation was about to change.
        /// </remarks>
        private static IMapEdit? BuildOne(MapAreaTool tool, MapAreaOptions options, MapRegion square,
            int plane, int x, int y) {
            switch (tool) {
                case MapAreaTool.Underlay:
                    return square.GetUnderlayId(plane, x, y) == options.Value
                        ? null
                        : new SetUnderlayEdit(square, plane, x, y, options.Value);

                case MapAreaTool.Overlay:
                    return square.GetOverlayId(plane, x, y) == options.Value
                           && square.GetOverlayShape(plane, x, y) == options.OverlayShape
                           && square.GetOverlayRotation(plane, x, y) == options.OverlayRotation
                        ? null
                        : new SetOverlayEdit(square, plane, x, y, options.Value,
                            options.OverlayShape, options.OverlayRotation);

                case MapAreaTool.BlockedFlag: {
                    byte current = square.GetRenderRule(plane, x, y);
                    byte wanted = (byte) (options.Blocked ? current | 0x1 : current & ~0x1);
                    return current == wanted ? null : new SetTileFlagsEdit(square, plane, x, y, wanted);
                }

                case MapAreaTool.RaiseHeight:
                case MapAreaTool.LowerHeight: {
                    int direction = tool == MapAreaTool.RaiseHeight ? +1 : -1;
                    int height = StepHeight(square, plane, x, y, direction);
                    return height == square.GetTileHeight(plane, x, y)
                        ? null
                        : new SetHeightEdit(square, plane, x, y, height);
                }

                default:
                    return null;
            }
        }

        /// <summary>
        ///     Moves a tile's height by whole storable steps.
        /// </summary>
        /// <remarks>
        ///     One step is 32 world units, not the 8 of RS2. Step 1 is skipped because the decoder
        ///     maps a stored 1 to 0, so a height of exactly one step below the reference has no
        ///     encoding and would be rejected on save.
        ///     <para>
        ///     Public because the single-tile height tools and the area version must not compute
        ///     this twice. Two copies of "which steps are storable" is one copy too many.
        ///     </para>
        /// </remarks>
        /// <param name="square">The square.</param>
        /// <param name="plane">The plane.</param>
        /// <param name="x">Tile X within the square.</param>
        /// <param name="y">Tile Y within the square.</param>
        /// <param name="direction">+1 to raise, -1 to lower.</param>
        /// <returns>The new height in world units.</returns>
        public static int StepHeight(MapRegion square, int plane, int x, int y, int direction) {
            if (square == null) throw new ArgumentNullException(nameof(square));

            int reference = plane == 0 ? 0 : square.GetTileHeight(plane - 1, x, y);
            int steps = (reference - square.GetTileHeight(plane, x, y)) / MapRegion.HEIGHT_UNITS_PER_STEP;

            steps += direction;
            if (steps == 1)
                steps += direction;

            steps = Math.Clamp(steps, 0, 255);
            return reference - steps * MapRegion.HEIGHT_UNITS_PER_STEP;
        }

        private static string Describe(MapAreaTool tool, MapAreaOptions options, int tiles, int squares) {
            string what = tool switch {
                MapAreaTool.Underlay => "underlay " + options.Value,
                MapAreaTool.Overlay => "overlay " + options.Value,
                MapAreaTool.BlockedFlag => options.Blocked ? "blocked" : "unblocked",
                MapAreaTool.RaiseHeight => "raised",
                MapAreaTool.LowerHeight => "lowered",
                _ => "changed"
            };

            return $"Fill {what} over {tiles:N0} tile(s) in {squares} square(s)";
        }
    }
}
