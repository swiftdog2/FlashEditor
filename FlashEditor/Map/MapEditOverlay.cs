using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;

//System.Drawing.Region arrives via the WinForms implicit usings and collides with the map type.
using MapRegion = FlashEditor.Cache.Region.Region;

namespace FlashEditor.Map {
    /// <summary>
    ///     What a flash is saying, which is the only thing that decides its colour.
    /// </summary>
    /// <remarks>
    ///     Four kinds rather than one, because "something happened" is not the feedback that was
    ///     missing - "what happened" is. A write, a deletion and a refusal all used to look
    ///     identical, which is to say invisible.
    /// </remarks>
    public enum MapFlashKind {
        /// <summary>Something was written to the square. Amber.</summary>
        Edit,

        /// <summary>The tiles that share a height vertex that just moved. Cyan.</summary>
        Vertex,

        /// <summary>Something was deleted. Red.</summary>
        Removal,

        /// <summary>The tool declined and nothing was written. Grey.</summary>
        Rejected,

        /// <summary>
        ///     A tile was READ rather than changed. Green.
        /// </summary>
        /// <remarks>
        ///     Its own kind rather than reusing <see cref="Edit"/>, because every other mark on this
        ///     overlay means the square was written to and the eyedropper writes nothing. Flashing
        ///     amber for a read would tell a user their map had changed when it had not, on a tab
        ///     whose whole difficulty is knowing which edits have happened.
        /// </remarks>
        Sampled
    }

    /// <summary>
    ///     The cursor highlight and the post-edit flashes, drawn over the map view's cached tiles.
    /// </summary>
    /// <remarks>
    ///     Strictly an overlay, and that is the point. The world view draws from per-square bitmaps
    ///     that cost a JS5 decode, a blend and a relief pass to produce, so a highlight that went
    ///     through the tile cache would re-rasterise a square on every mouse move. Everything here
    ///     is painted in <c>OnPaint</c> after the tiles are blitted and touches neither the cache
    ///     nor the rasteriser.
    ///
    ///     It holds no <see cref="System.Windows.Forms.Control"/> reference either, so the geometry
    ///     - which is all vertical-flip arithmetic and therefore all sign traps - can be reasoned
    ///     about against <see cref="MapCamera"/> alone.
    /// </remarks>
    public sealed class MapEditOverlay {
        /// <summary>
        ///     How long a flash lives, in milliseconds.
        /// </summary>
        /// <remarks>
        ///     Long enough to be noticed after the eye has moved to the status line and back, short
        ///     enough that a run of edits does not leave the map covered in stale marks. The
        ///     inspector's "last edit" line is what carries the information once this has faded, so
        ///     the flash only has to be long enough to say <em>where</em>.
        /// </remarks>
        public const int FlashMilliseconds = 900;

        /// <summary>Fraction of the flash spent fading in rather than out.</summary>
        private const float AttackFraction = 0.12f;

        /// <summary>
        ///     Pixels of slack added around anything drawn, when working out what to invalidate.
        /// </summary>
        /// <remarks>
        ///     The glow pass strokes several pixels outside the rectangle it is given and a label
        ///     sits above it, so an invalidation sized to the rectangle itself leaves a fringe of
        ///     the previous frame's highlight behind when the cursor moves on.
        /// </remarks>
        private const int GlowMargin = 10;

        /// <summary>Horizontal slack for a flash label, which is far wider than a one-tile flash.</summary>
        private const int LabelMargin = 150;

        /// <summary>Vertical slack above a flash, where its label is drawn.</summary>
        private const int LabelHeadroom = 28;

        private readonly List<Flash> flashes = new List<Flash>();

        /// <summary>The tile under the cursor, or <c>null</c> when the cursor is off the world.</summary>
        public TileHit? Hover { get; set; }

        /// <summary>
        ///     The tiles an area operation would act on, or <c>null</c> when nothing is selected.
        /// </summary>
        /// <remarks>
        ///     Drawn here rather than through the tile cache for exactly the reason the hover
        ///     highlight is: a selection changes on every mouse move of a drag, and routing it
        ///     through the cache would re-rasterise a square per move.
        /// </remarks>
        public MapSelection? Selection { get; set; }

        /// <summary>
        ///     Whether to show which vertex a height edit would move.
        /// </summary>
        /// <remarks>
        ///     Set only while a height tool is selected. Terrain is a heightmap and the value stored
        ///     against a tile is the elevation of its <em>south-west corner vertex</em>, which four
        ///     tiles share - so "raise this tile" actually deforms a two-by-two block of the
        ///     surface. Drawing that block before the click is the cheapest possible answer to
        ///     "does the tile just start hovering mid-air"; showing it always would be noise under
        ///     every other tool.
        /// </remarks>
        public bool ShowVertexAffordance { get; set; }

        /// <summary>Flashes still held, expired ones included until the next paint prunes them.</summary>
        public int FlashCount => flashes.Count;

        /// <summary>
        ///     Adds a flash over a block of world tiles.
        /// </summary>
        /// <param name="worldX">World X of the block's south-west tile.</param>
        /// <param name="worldY">World Y of the block's south-west tile.</param>
        /// <param name="tilesWide">Tiles east, at least one.</param>
        /// <param name="tilesHigh">Tiles north, at least one.</param>
        /// <param name="plane">The plane the change is on; it is not drawn from another plane.</param>
        /// <param name="kind">What the flash is saying.</param>
        /// <param name="label">A short caption drawn above it, or <c>null</c> for none.</param>
        public void Add(int worldX, int worldY, int tilesWide, int tilesHigh, int plane,
            MapFlashKind kind, string? label = null) {
            flashes.Add(new Flash {
                WorldX = worldX,
                WorldY = worldY,
                TilesWide = Math.Max(1, tilesWide),
                TilesHigh = Math.Max(1, tilesHigh),
                Plane = plane,
                Kind = kind,
                Label = label,
                StartedAt = Environment.TickCount64
            });
        }

        /// <summary>Drops every flash, for example when the bound cache changes.</summary>
        public void ClearFlashes() => flashes.Clear();

        /// <summary>
        ///     The screen rectangle everything currently drawn occupies.
        /// </summary>
        /// <remarks>
        ///     Deliberately does not prune. A flash that has just expired still has to be included,
        ///     or the region it was drawn in is never invalidated and the last frame of it stays on
        ///     screen until something else repaints over it.
        /// </remarks>
        /// <param name="camera">The view transform.</param>
        /// <param name="plane">The plane being viewed.</param>
        /// <param name="tileScale">
        ///     <c>true</c> when a tile is big enough to outline on its own. See
        ///     <see cref="Paint"/> for what the false case draws instead.
        /// </param>
        /// <returns>The rectangle to invalidate, or <see cref="Rectangle.Empty"/>.</returns>
        public Rectangle Bounds(MapCamera camera, int plane, bool tileScale) {
            if (camera == null) throw new ArgumentNullException(nameof(camera));

            Rectangle result = Rectangle.Empty;

            if (Hover != null && Hover.Plane == plane) {
                //Three tiles wide, not one: the vertex affordance reaches one tile west and one
                //south of the hovered tile.
                RectangleF hover = tileScale
                    ? TileRect(camera, Hover.WorldX - 1, Hover.WorldY - 1, 3, 3)
                    : TileRect(camera, Hover.RegionX * MapRegion.WIDTH, Hover.RegionY * MapRegion.HEIGHT,
                        MapRegion.WIDTH, MapRegion.HEIGHT);

                result = Merge(result, Grow(hover, GlowMargin, GlowMargin));
            }

            foreach (Flash flash in flashes) {
                if (flash.Plane != plane)
                    continue;

                RectangleF rect = TileRect(camera, flash.WorldX, flash.WorldY, flash.TilesWide, flash.TilesHigh);
                int sideways = flash.Label == null ? GlowMargin : LabelMargin;
                result = Merge(result, Grow(rect, sideways, GlowMargin + LabelHeadroom));
            }

            /* The selection has to be in here even though it is drawn last. Bounds is what Paint
               tests for emptiness before it draws anything at all, so a selection left out of it
               would never be painted on a frame with no hover and no flash - which is every frame
               after a drag ends. */
            Rectangle selected = Selection == null ? Rectangle.Empty : Selection.Bounds;
            if (!selected.IsEmpty && Selection != null && Selection.Plane == plane) {
                RectangleF box = TileRect(camera, selected.Left, selected.Top,
                    selected.Width, selected.Height);
                result = Merge(result, Grow(box, GlowMargin, GlowMargin));
            }

            return result;
        }

        /// <summary>
        ///     Draws the flashes, the vertex affordance and the cursor highlight, in that order.
        /// </summary>
        /// <remarks>
        ///     Cursor last so it is never buried under a flash it caused.
        ///
        ///     <paramref name="tileScale"/> is the answer to what happens below roughly two pixels
        ///     per tile, where a tile is sub-pixel and outlining one would draw a smudge in the
        ///     rough vicinity of the truth. Below it the <em>map square</em> under the cursor is
        ///     outlined instead, which is both the smallest honest unit at that zoom and exactly
        ///     the unit the editor refuses to work below - so the highlight doubles as the
        ///     affordance for "can I edit here".
        /// </remarks>
        /// <param name="g">Where to draw.</param>
        /// <param name="camera">The view transform.</param>
        /// <param name="plane">The plane being viewed.</param>
        /// <param name="tileScale"><c>true</c> to highlight a tile, <c>false</c> to highlight a square.</param>
        /// <param name="font">The font for flash labels.</param>
        /// <returns>The rectangle drawn into, for the caller to remember as dirty.</returns>
        public Rectangle Paint(Graphics g, MapCamera camera, int plane, bool tileScale, Font? font) {
            if (g == null) throw new ArgumentNullException(nameof(g));
            if (camera == null) throw new ArgumentNullException(nameof(camera));

            PruneExpired();

            Rectangle covered = Bounds(camera, plane, tileScale);
            if (covered.IsEmpty)
                return Rectangle.Empty;

            SmoothingMode smoothing = g.SmoothingMode;
            g.SmoothingMode = SmoothingMode.AntiAlias;

            try {
                //Under the flashes, so a fill's own flash reads on top of the area it filled.
                PaintSelection(g, camera, plane, tileScale);

                foreach (Flash flash in flashes)
                    if (flash.Plane == plane)
                        PaintFlash(g, camera, flash, font);

                TileHit? hover = Hover;
                if (hover == null || hover.Plane != plane)
                    return covered;

                if (!tileScale) {
                    PaintSquareHighlight(g, camera, hover);
                    return covered;
                }

                if (ShowVertexAffordance)
                    PaintVertexAffordance(g, camera, hover);

                PaintTileHighlight(g, camera, hover);
            }
            finally {
                g.SmoothingMode = smoothing;
            }

            return covered;
        }

        /// <summary>
        ///     Forgets flashes whose time is up.
        /// </summary>
        /// <remarks>
        ///     Public because a paint is not guaranteed to arrive. On a tab the user has switched
        ///     away from an <c>Invalidate</c> produces no <c>WM_PAINT</c>, so a caller that drives
        ///     the fade from a timer has to be able to retire the flash itself or the list never
        ///     empties and the timer invalidates forever.
        /// </remarks>
        public void PruneExpired() {
            long now = Environment.TickCount64;
            for (int i = flashes.Count - 1; i >= 0; i--)
                if (now - flashes[i].StartedAt >= FlashMilliseconds)
                    flashes.RemoveAt(i);
        }

        private static void PaintFlash(Graphics g, MapCamera camera, Flash flash, Font? font) {
            float envelope = Envelope(Environment.TickCount64 - flash.StartedAt);
            if (envelope <= 0f)
                return;

            RectangleF rect = TileRect(camera, flash.WorldX, flash.WorldY, flash.TilesWide, flash.TilesHigh);
            Color core = CoreColour(flash.Kind);
            int alpha = (int) Math.Clamp(envelope * 255f, 0f, 255f);

            //The vertex block is stroke-only: its whole job is to say which tiles moved with the
            //one that was clicked, and filling it would hide the terrain that is the evidence.
            if (flash.Kind != MapFlashKind.Vertex)
                using (var fill = new SolidBrush(Color.FromArgb(Scale(alpha, 80), core)))
                    g.FillRectangle(fill, rect);

            StrokeTwoTone(g, rect, core, alpha, StrokeWidth(camera));

            if (flash.Label != null && font != null)
                DrawLabel(g, font, flash.Label, rect, alpha);
        }

        /// <summary>
        ///     A flash's opacity over its life: a fast rise, then a pulsing fall.
        /// </summary>
        /// <remarks>
        ///     Pulsed rather than a plain fade because a single fade at the far end of a large
        ///     monitor reads as a rendering artefact. Two beats read as deliberate.
        /// </remarks>
        /// <param name="elapsedMs">Milliseconds since the flash was raised.</param>
        /// <returns>Opacity, 0 to 1.</returns>
        private static float Envelope(long elapsedMs) {
            float t = elapsedMs / (float) FlashMilliseconds;
            if (t < 0f || t >= 1f)
                return 0f;

            float level = t < AttackFraction
                ? t / AttackFraction
                : 1f - (t - AttackFraction) / (1f - AttackFraction);

            float pulse = 0.7f + 0.3f * (float) Math.Cos(t * Math.PI * 4.0);
            return Math.Clamp(level * pulse, 0f, 1f);
        }

        /// <summary>
        ///     Draws the selection: a wash over every selected tile and a hard edge round the
        ///     outside of the shape.
        /// </summary>
        /// <remarks>
        ///     <b>Only the boundary is stroked, not every tile.</b> A grid of ten thousand outlined
        ///     tiles is a solid block of ink at any zoom a selection is made at, and it hides the
        ///     terrain that is the whole reason for selecting. An edge is drawn where the neighbour
        ///     is <em>not</em> selected, which is what makes a lasso read as one shape rather than as
        ///     a heap of tiles.
        ///     <para>
        ///     <b>Two fallbacks, and both are stated on screen elsewhere.</b> Below the editing zoom
        ///     a tile is sub-pixel and the shape is drawn as its bounding box, because outlining
        ///     sub-pixel tiles produces a smudge in the rough vicinity of the truth - the same
        ///     reasoning that makes the cursor highlight fall back to the map square. And past
        ///     <see cref="MaxOutlinedTiles"/> the per-tile pass is dropped for the bounding box too:
        ///     the cost is per tile and a paint happens on every mouse move of a drag, so a
        ///     quarter-million-tile selection would make its own resizing unusable.
        ///     </para>
        /// </remarks>
        private void PaintSelection(Graphics g, MapCamera camera, int plane, bool tileScale) {
            MapSelection? selection = Selection;
            if (selection == null || selection.IsEmpty || selection.Plane != plane)
                return;

            Rectangle bounds = selection.Bounds;

            if (!tileScale || selection.Count > MaxOutlinedTiles) {
                RectangleF box = TileRect(camera, bounds.Left, bounds.Top, bounds.Width, bounds.Height);
                using (var wash = new SolidBrush(Color.FromArgb(30, SelectionColour)))
                    g.FillRectangle(wash, box);
                StrokeTwoTone(g, box, SelectionColour, 220, 1.4f);
                return;
            }

            //Culled to the viewport before anything is measured. A selection can legitimately be
            //larger than the window, and the tiles off screen cost exactly as much to draw.
            Rectangle regions = camera.VisibleRegionBounds();
            int leftTile = regions.Left * MapRegion.WIDTH - 1;
            int rightTile = regions.Right * MapRegion.WIDTH + 1;
            int bottomTile = regions.Top * MapRegion.HEIGHT - 1;
            int topTile = regions.Bottom * MapRegion.HEIGHT + 1;

            float stroke = StrokeWidth(camera);

            using var fill = new SolidBrush(Color.FromArgb(46, SelectionColour));
            using var halo = new Pen(Color.FromArgb(170, 6, 8, 12), stroke + 1.4f);
            using var edge = new Pen(SelectionColour, stroke);

            foreach ((int worldX, int worldY) in selection.Tiles) {
                if (worldX < leftTile || worldX > rightTile || worldY < bottomTile || worldY > topTile)
                    continue;

                RectangleF tile = TileRect(camera, worldX, worldY, 1, 1);
                g.FillRectangle(fill, tile);

                //World Y grows north and screen Y grows down, so the northern neighbour is the tile
                //above on screen. Getting this pair the wrong way round draws a correct-looking
                //outline one tile off, which is the hardest kind of wrong to notice.
                if (!selection.Contains(worldX, worldY + 1))
                    StrokeEdge(g, halo, edge, tile.Left, tile.Top, tile.Right, tile.Top);
                if (!selection.Contains(worldX, worldY - 1))
                    StrokeEdge(g, halo, edge, tile.Left, tile.Bottom, tile.Right, tile.Bottom);
                if (!selection.Contains(worldX - 1, worldY))
                    StrokeEdge(g, halo, edge, tile.Left, tile.Top, tile.Left, tile.Bottom);
                if (!selection.Contains(worldX + 1, worldY))
                    StrokeEdge(g, halo, edge, tile.Right, tile.Top, tile.Right, tile.Bottom);
            }
        }

        private static void StrokeEdge(Graphics g, Pen halo, Pen edge, float x0, float y0, float x1, float y1) {
            g.DrawLine(halo, x0, y0, x1, y1);
            g.DrawLine(edge, x0, y0, x1, y1);
        }

        private static void PaintTileHighlight(Graphics g, MapCamera camera, TileHit hover) {
            RectangleF tile = TileRect(camera, hover.WorldX, hover.WorldY, 1, 1);

            //A wash rather than a solid fill: enough to lift the tile off whatever is under it
            //without hiding the underlay colour that is being edited.
            using (var wash = new SolidBrush(Color.FromArgb(38, 255, 255, 255)))
                g.FillRectangle(wash, tile);

            StrokeTwoTone(g, tile, HoverColour, 255, StrokeWidth(camera));
        }

        /// <summary>
        ///     Outlines the map square under the cursor, for zooms where a tile is sub-pixel.
        /// </summary>
        /// <remarks>
        ///     Dimmer and unfilled: at this zoom a square is the size a tile is when editing, and a
        ///     filled 64-tile block would read as a selection rather than as a cursor.
        /// </remarks>
        private static void PaintSquareHighlight(Graphics g, MapCamera camera, TileHit hover) {
            RectangleF square = TileRect(camera, hover.RegionX * MapRegion.WIDTH,
                hover.RegionY * MapRegion.HEIGHT, MapRegion.WIDTH, MapRegion.HEIGHT);

            StrokeTwoTone(g, square, HoverColour, 190, 1.4f);
        }

        /// <summary>
        ///     Marks the vertex a height edit would move, and the four tiles that share it.
        /// </summary>
        /// <remarks>
        ///     This is the whole explanation of the height tools, drawn rather than written: the dot
        ///     sits on the hovered tile's south-west corner, and the dashed box is the two-by-two
        ///     block of tiles whose surface bends when that one vertex moves. The hovered tile is
        ///     the north-east quarter of it, which is why raising a tile visibly pulls its
        ///     neighbours up too.
        /// </remarks>
        private static void PaintVertexAffordance(Graphics g, MapCamera camera, TileHit hover) {
            RectangleF block = TileRect(camera, hover.WorldX - 1, hover.WorldY - 1, 2, 2);

            using (var pen = new Pen(Color.FromArgb(150, VertexColour), 1.2f)) {
                pen.DashStyle = DashStyle.Dash;
                g.DrawRectangle(pen, block.X, block.Y, block.Width, block.Height);
            }

            PointF vertex = camera.WorldToScreen(hover.WorldX, hover.WorldY);
            float radius = (float) Math.Clamp(camera.PixelsPerTile * 0.22, 2.5, 5.0);
            var dot = new RectangleF(vertex.X - radius, vertex.Y - radius, radius * 2f, radius * 2f);

            using (var halo = new Pen(Color.FromArgb(200, 6, 8, 12), 2.4f))
                g.DrawEllipse(halo, dot);
            using (var fill = new SolidBrush(Color.FromArgb(235, VertexColour)))
                g.FillEllipse(fill, dot);
        }

        /// <summary>
        ///     Strokes a rectangle as a bright core inside a dark halo, inside a soft glow.
        /// </summary>
        /// <remarks>
        ///     Three passes rather than one, because a single flat colour is guaranteed to vanish
        ///     against one half of this map: a white line disappears into desert and snow, a dark
        ///     one into deep water and cave floor. The dark halo gives the core a guaranteed
        ///     contrasting edge whatever it lands on, and the glow is what keeps the pair readable
        ///     as a highlight rather than as a hairline at two pixels per tile.
        /// </remarks>
        /// <param name="g">Where to draw.</param>
        /// <param name="rect">The rectangle to stroke.</param>
        /// <param name="core">The bright inner colour.</param>
        /// <param name="alpha">Overall opacity, 0 to 255.</param>
        /// <param name="coreWidth">Width of the inner stroke.</param>
        private static void StrokeTwoTone(Graphics g, RectangleF rect, Color core, int alpha, float coreWidth) {
            //Kept narrow deliberately. At the minimum editing zoom a tile is two pixels across, so a
            //glow much wider than this stops being an outline of the tile and becomes a blob over
            //the general area of it - which is precisely the vagueness being fixed.
            using (var glow = new Pen(Color.FromArgb(Scale(alpha, 55), core), coreWidth + 3.6f))
                g.DrawRectangle(glow, rect.X, rect.Y, rect.Width, rect.Height);

            using (var halo = new Pen(Color.FromArgb(Scale(alpha, 185), 6, 8, 12), coreWidth + 1.6f))
                g.DrawRectangle(halo, rect.X, rect.Y, rect.Width, rect.Height);

            using (var pen = new Pen(Color.FromArgb(alpha, core), coreWidth))
                g.DrawRectangle(pen, rect.X, rect.Y, rect.Width, rect.Height);
        }

        /// <summary>
        ///     A caption above a flash, on its own backing plate.
        /// </summary>
        /// <remarks>
        ///     Plated rather than outlined text. The map is a full-bleed photograph as far as text
        ///     is concerned, and glyph outlining still leaves numbers unreadable over a wall mark
        ///     or an icon.
        /// </remarks>
        private static void DrawLabel(Graphics g, Font font, string text, RectangleF anchor, int alpha) {
            SizeF size = g.MeasureString(text, font);

            float x = anchor.X + anchor.Width / 2f - size.Width / 2f;
            float y = anchor.Y - size.Height - 6f;
            var plate = new RectangleF(x - 5f, y - 3f, size.Width + 10f, size.Height + 6f);

            using (var back = new SolidBrush(Color.FromArgb(Scale(alpha, 215), 10, 12, 18)))
                g.FillRectangle(back, plate);
            using (var edge = new Pen(Color.FromArgb(Scale(alpha, 110), 255, 255, 255), 1f))
                g.DrawRectangle(edge, plate.X, plate.Y, plate.Width, plate.Height);
            using (var ink = new SolidBrush(Color.FromArgb(alpha, 244, 246, 250)))
                g.DrawString(text, font, ink, x, y);
        }

        /// <summary>
        ///     The screen rectangle a block of world tiles occupies.
        /// </summary>
        /// <remarks>
        ///     Taken from the block's <em>north-west</em> corner, because world Y grows north and
        ///     screen Y grows down. Taking it from the south-west corner - the one the coordinates
        ///     name - puts every highlight exactly one block too low, which at one tile is the
        ///     off-by-one that looks like a working highlight on the wrong tile.
        /// </remarks>
        private static RectangleF TileRect(MapCamera camera, int worldX, int worldY, int tilesWide, int tilesHigh) {
            PointF northWest = camera.WorldToScreen(worldX, worldY + tilesHigh);
            return new RectangleF(northWest.X, northWest.Y,
                (float) (tilesWide * camera.PixelsPerTile),
                (float) (tilesHigh * camera.PixelsPerTile));
        }

        /// <summary>Stroke width that stays a line at 2 px/tile and a border at 16.</summary>
        private static float StrokeWidth(MapCamera camera) =>
            (float) Math.Clamp(camera.PixelsPerTile * 0.14, 1.0, 2.2);

        private static int Scale(int alpha, int ceiling) => Math.Clamp(alpha * ceiling / 255, 0, 255);

        private static Rectangle Merge(Rectangle current, Rectangle next) {
            if (next.IsEmpty)
                return current;
            return current.IsEmpty ? next : Rectangle.Union(current, next);
        }

        private static Rectangle Grow(RectangleF rect, int sideways, int vertically) =>
            Rectangle.Inflate(Rectangle.Ceiling(RectangleF.Inflate(rect, 1f, 1f)), sideways, vertically);

        /// <summary>Near-white, so the dark halo has something to contrast against.</summary>
        private static readonly Color HoverColour = Color.FromArgb(255, 248, 252, 255);

        /// <summary>Cyan, kept clear of the amber an edit flash uses.</summary>
        private static readonly Color VertexColour = Color.FromArgb(255, 120, 226, 255);

        /// <summary>
        ///     Violet: the one hue not already spoken for by a flash kind or the cursor.
        /// </summary>
        /// <remarks>
        ///     A selection is on screen for minutes while flashes last under a second, so it must
        ///     not be mistakable for any of them. Amber is a write, red a deletion, grey a refusal,
        ///     green a read, cyan the height vertex and near-white the cursor.
        /// </remarks>
        private static readonly Color SelectionColour = Color.FromArgb(255, 196, 156, 255);

        /// <summary>
        ///     Past this many tiles the selection is drawn as its bounding box rather than tile by
        ///     tile.
        /// </summary>
        /// <remarks>
        ///     Four map squares' worth. The per-tile pass costs a fill and up to four strokes each
        ///     and runs on every mouse move of a drag, so the ceiling is set where a drag would
        ///     start to stutter rather than where the drawing would stop being useful.
        /// </remarks>
        private const int MaxOutlinedTiles = 4 * MapRegion.WIDTH * MapRegion.HEIGHT;

        private static Color CoreColour(MapFlashKind kind) {
            switch (kind) {
                case MapFlashKind.Vertex:
                    return VertexColour;
                case MapFlashKind.Removal:
                    return Color.FromArgb(255, 255, 116, 106);
                case MapFlashKind.Rejected:
                    return Color.FromArgb(255, 206, 208, 216);
                case MapFlashKind.Sampled:
                    return Color.FromArgb(255, 138, 226, 138);
                default:
                    return Color.FromArgb(255, 255, 206, 92);
            }
        }

        private sealed class Flash {
            public int WorldX { get; set; }
            public int WorldY { get; set; }
            public int TilesWide { get; set; }
            public int TilesHigh { get; set; }
            public int Plane { get; set; }
            public MapFlashKind Kind { get; set; }
            public string? Label { get; set; }
            public long StartedAt { get; set; }
        }
    }
}
