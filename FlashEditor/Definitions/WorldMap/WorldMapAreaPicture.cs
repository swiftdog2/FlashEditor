using System;
using System.Collections.Generic;
using FlashEditor.Cache;
using FlashEditor.Definitions.Config;
using FlashEditor.Definitions.Sprites;
using FlashEditor.Map;

namespace FlashEditor.Definitions.WorldMap {
    /// <summary>
    ///     One icon as the overview map places it: where it lands, and what config group 36 says to
    ///     draw there.
    /// </summary>
    /// <remarks>
    ///     The record is resolved once here rather than looked up per repaint, and the fields kept
    ///     are the ones the tab can show. The id is carried alongside them because it is the thing a
    ///     user cross-references against the Config tab, and because a row that showed only a label
    ///     would hide the 446 records that have none.
    /// </remarks>
    public sealed class WorldMapIconPlacement {
        /// <summary>Binds a placement to the map element it names.</summary>
        /// <param name="element">The placement, from the area's static-element group.</param>
        /// <param name="definition">The map element it resolves to, or null when it resolves to none.</param>
        /// <param name="canvasX">The canvas column, or -1 when no zone covers the placement.</param>
        /// <param name="canvasY">The canvas row, or -1 when no zone covers the placement.</param>
        public WorldMapIconPlacement(WorldMapElement element, MapElementDefinition? definition,
            int canvasX, int canvasY) {
            Element = element ?? throw new ArgumentNullException(nameof(element));
            Definition = definition;
            CanvasX = canvasX;
            CanvasY = canvasY;
        }

        /// <summary>The placement record, which carries the world position and the members flag.</summary>
        public WorldMapElement Element { get; }

        /// <summary>What config group 36 holds for this id, or null when it holds nothing.</summary>
        public MapElementDefinition? Definition { get; }

        /// <summary>The canvas column, or -1 when no zone of the area covers the placement.</summary>
        public int CanvasX { get; }

        /// <summary>The canvas row, or -1 when no zone of the area covers the placement.</summary>
        public int CanvasY { get; }

        /// <summary>Whether this icon has somewhere on the canvas to be drawn.</summary>
        public bool IsPlaced => CanvasX >= 0 && CanvasY >= 0;

        /// <summary>The file id within the area's static-element group.</summary>
        public int Id => Element.Id;

        /// <summary>The config group 36 file id this placement names.</summary>
        public int MapElementId => Element.MapElementId;

        /// <summary>
        ///     The label the client draws beside the icon, on one line, or empty when it has none.
        /// </summary>
        /// <remarks>
        ///     The stored label carries <c>&lt;br&gt;</c> where the client wraps it
        ///     (Node_Sub40.java:154-158). Folded to a space here because a grid cell is one line, and
        ///     folding rather than truncating is what keeps "Troll Stronghold" readable as two words.
        /// </remarks>
        public string Label => Definition?.Label == null
            ? string.Empty
            : Definition.Label.Replace("<br>", " ");

        /// <summary>The index-8 sprite group the icon is drawn from, or -1.</summary>
        public int SpriteId => Definition?.SpriteId ?? -1;

        /// <summary>The category the world map filters on, or -1.</summary>
        public int CategoryId => Definition?.CategoryId ?? -1;
    }

    /// <summary>
    ///     An area's overview map rendered to pixels, one pixel per tile, with its icons placed.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///     Pixels rather than a <c>Bitmap</c>, because this is built on a background worker and a
    ///     bitmap is a GDI+ handle the UI thread owns.
    ///     </para>
    ///     <para>
    ///     <b>One pixel per tile is the format's own resolution and not a choice.</b> The raster
    ///     stores exactly one record per tile, so any larger scale is interpolation and any smaller
    ///     one discards data the file went to the trouble of storing. The client draws the same
    ///     tiles at 1.5 to 8 screen pixels each depending on the area's zoom preset; the view scales
    ///     the finished picture instead, which is the same thing for a flat fill and is not the same
    ///     thing for the shape-masked overlays it does not draw.
    ///     </para>
    /// </remarks>
    public sealed class WorldMapAreaPicture {
        /// <summary>The checkerboard colour the client fills an untinted empty tile with.</summary>
        /// <remarks>
        ///     The literal <c>-11840664</c> at <c>Class278.java:334</c>, which is opaque
        ///     <c>#4B5BE8</c>. Written as the ARGB it is rather than as the signed integer the
        ///     decompile shows, because the signed form reads as a magic number and this is a colour.
        /// </remarks>
        public const int EmptyTileBlue = unchecked((int) 0xFF4B5BE8);

        internal WorldMapAreaPicture(WorldMapCanvas canvas, int[] pixels,
            IReadOnlyList<WorldMapIconPlacement> icons, string note, WorldMapPictureCounts counts) {
            Canvas = canvas;
            Pixels = pixels;
            Icons = icons;
            Note = note;
            Counts = counts;
        }

        /// <summary>The rectangle the picture covers, in map coordinates.</summary>
        public WorldMapCanvas Canvas { get; }

        /// <summary>The picture width in pixels, which is the canvas width in tiles.</summary>
        public int Width => Canvas.Width;

        /// <summary>The picture height in pixels.</summary>
        public int Height => Canvas.Height;

        /// <summary>
        ///     Row-major ARGB pixels, north at the top.
        /// </summary>
        /// <remarks>
        ///     The canvas counts y northward and a bitmap counts rows downward, so the renderer
        ///     flips as it writes. Flipping at the end would cost a second pass over an image that
        ///     reaches 4.7 megapixels here, and not flipping at all produces a map that is upside
        ///     down in a way that is surprisingly easy to miss on an area with no coastline.
        /// </remarks>
        public int[] Pixels { get; }

        /// <summary>Every static element of the area, placed.</summary>
        public IReadOnlyList<WorldMapIconPlacement> Icons { get; }

        /// <summary>What this picture is, and where it departs from what the client draws.</summary>
        public string Note { get; }

        /// <summary>What the raster held, for the tab's summary line.</summary>
        public WorldMapPictureCounts Counts { get; }

        /// <summary>Whether there is anything to draw.</summary>
        public bool HasImage => Pixels.Length > 0 && Width > 0 && Height > 0;
    }

    /// <summary>What one area's raster turned out to hold.</summary>
    /// <remarks>
    ///     Counted during the render rather than by a second walk over several million tiles. They
    ///     are shown because the shape of an area is not otherwise visible in a picture: a wholly
    ///     decorated area and a wholly terrain one look alike and are stored completely differently.
    /// </remarks>
    public readonly struct WorldMapPictureCounts {
        internal WorldMapPictureCounts(int blocks, int mapSquareBlocks, long tiles, long terrain,
            long decorated, long blank, long tileElements) {
            Blocks = blocks;
            MapSquareBlocks = mapSquareBlocks;
            Tiles = tiles;
            Terrain = terrain;
            Decorated = decorated;
            Blank = blank;
            TileElements = tileElements;
        }

        /// <summary>Blocks the raster file holds.</summary>
        public int Blocks { get; }

        /// <summary>How many of those cover a whole map square rather than one 8x8 zone.</summary>
        public int MapSquareBlocks { get; }

        /// <summary>Tiles across every block.</summary>
        public long Tiles { get; }

        /// <summary>Tiles naming a single floor.</summary>
        public long Terrain { get; }

        /// <summary>Tiles carrying per-plane decoration.</summary>
        public long Decorated { get; }

        /// <summary>Tiles naming no floor at all.</summary>
        public long Blank { get; }

        /// <summary>Object references carried by decorated tiles.</summary>
        public long TileElements { get; }
    }

    /// <summary>
    ///     Turns an area's raster into a picture, through the client's own colour model.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///     <b>The underlay blend is the client's and is not optional.</b> The world map averages
    ///     underlay colour over a sliding neighbourhood before packing it
    ///     (<c>Class278.method3310</c>, which mirrors the scene path's
    ///     <c>Class305.method3568</c>), and that is what makes terrain read as ground rather than as
    ///     a grid of flat swatches. It is reached through <see cref="UnderlayBlender"/> rather than
    ///     transcribed a second time, so both views of this cache blend identically.
    ///     </para>
    ///     <para>
    ///     <b>What this deliberately does not do</b>, all of it stated in
    ///     <see cref="WorldMapAreaPicture.Note"/> so a user comparing the tab against the game can
    ///     tell a documented choice from a defect:
    ///     </para>
    ///     <list type="bullet">
    ///     <item>An overlay fills its whole tile. The client masks it to one of the wall, corner and
    ///     diagonal shapes (<c>Class278.method3318</c>), which at one pixel per tile has nothing to
    ///     draw into.</item>
    ///     <item>Locations are not drawn. The client stamps a map-scene sprite for every object a
    ///     decorated tile names, resolving each through the object provider and its morph list
    ///     first; that is tens of thousands of index-16 decodes for a picture whose features are
    ///     one pixel across.</item>
    ///     <item>Icons are marked, not drawn. The client blits each element's index-8 sprite and
    ///     draws its label; the tab lists them beside the picture and marks their positions, because
    ///     a sprite is 15 pixels across on a map where a tile is one.</item>
    ///     </list>
    /// </remarks>
    public static class WorldMapAreaRenderer {
        /// <summary>Colour an empty tile takes on the other phase of the checkerboard.</summary>
        /// <remarks>
        ///     The client uses the floor overlay flagged as the world map's background
        ///     (<c>Class278.java:337</c> indexes its colour table by <c>Class32.anInt312</c>, which
        ///     is set when a floor overlay carries opcode 8). This is the fallback for a cache whose
        ///     overlay table declares none, and is deliberately the same blue rather than a
        ///     contrasting colour: an invented second colour would put a checkerboard on screen that
        ///     nothing in the cache asked for.
        /// </remarks>
        private const int FallbackBackground = WorldMapAreaPicture.EmptyTileBlue;

        /// <summary>
        ///     Renders one area.
        /// </summary>
        /// <remarks>
        ///     Runs on a worker. Every cache read it does is memoised by the caller-supplied
        ///     resolvers, because a floor lookup that re-decoded config group 1 per tile would run
        ///     4.7 million times on the surface area alone.
        /// </remarks>
        /// <param name="area">The area's details record, which states the canvas and the zones.</param>
        /// <param name="raster">The decoded <c>area</c> file.</param>
        /// <param name="elements">The area's static elements, already read.</param>
        /// <param name="floors">Resolves floor definitions and the background colour.</param>
        /// <returns>The picture.</returns>
        public static WorldMapAreaPicture Render(WorldMapAreaDefinition area, WorldMapAreaRaster raster,
            IReadOnlyList<WorldMapElement> elements, WorldMapFloorPalette floors) {
            if (area == null) throw new ArgumentNullException(nameof(area));
            if (raster == null) throw new ArgumentNullException(nameof(raster));
            if (elements == null) throw new ArgumentNullException(nameof(elements));
            if (floors == null) throw new ArgumentNullException(nameof(floors));

            WorldMapCanvas canvas = WorldMapCanvas.For(area);
            if (canvas.IsEmpty) {
                return new WorldMapAreaPicture(canvas, Array.Empty<int>(),
                    Array.Empty<WorldMapIconPlacement>(),
                    "This area declares no zone, so it has no rectangle to be drawn into. The client " +
                    "draws nothing for it either.",
                    default);
            }

            int width = canvas.Width;
            int height = canvas.Height;

            var underlayIds = new int[width, height];
            var overlayIds = new int[width, height];

            int blocks = 0, squares = 0;
            long tiles = 0, terrain = 0, decorated = 0, blank = 0, tileElements = 0;

            foreach (WorldMapRasterBlock block in raster.Blocks) {
                blocks++;
                if (block.IsMapSquare)
                    squares++;

                for (int i = 0; i < block.Tiles.Length; i++) {
                    int x = block.WorldXOf(i) - canvas.OriginX;
                    int y = block.WorldYOf(i) - canvas.OriginY;
                    tiles++;

                    //Dropped rather than clamped. A clamp would smear the edge column of an area
                    //whose blocks overflowed its own zones, which reads as terrain.
                    if (x < 0 || y < 0 || x >= width || y >= height)
                        continue;

                    WorldMapTile tile = block.Tiles[i];

                    if (tile.IsDecorated) {
                        decorated++;
                        WorldMapTileLevel[] levels = tile.Levels!;
                        if (levels.Length == 0)
                            continue;

                        //Level 0 is the tile itself; levels above it are the upper planes, which the
                        //client keeps apart and draws only when that plane is selected.
                        underlayIds[x, y] = levels[0].UnderlayId;
                        overlayIds[x, y] = levels[0].OverlayId;

                        foreach (WorldMapTileLevel level in levels)
                            tileElements += level.Elements.Length;
                        continue;
                    }

                    if (tile.IsBlank) {
                        blank++;
                        continue;
                    }

                    terrain++;
                    int floorId = tile.ResolveFloorId(raster);
                    if (floorId < 0)
                        continue;

                    if (tile.IsOverlay) {
                        overlayIds[x, y] = floorId;
                        underlayIds[x, y] = tile.UnderlayBeneathOverlay;
                    }
                    else {
                        underlayIds[x, y] = floorId;
                    }
                }
            }

            int[,] blended = UnderlayBlender.Blend(underlayIds, floors.ResolveUnderlay);
            int[] pixels = Compose(area, canvas, blended, overlayIds, floors);

            var icons = new List<WorldMapIconPlacement>(elements.Count);
            foreach (WorldMapElement element in elements) {
                bool placed = canvas.TryPlace(area, element.Plane, element.X, element.Y,
                    out int iconX, out int iconY);

                icons.Add(new WorldMapIconPlacement(element, floors.ResolveMapElement(element.MapElementId),
                    placed ? iconX : -1, placed ? iconY : -1));
            }

            return new WorldMapAreaPicture(canvas, pixels, icons, Describe(area),
                new WorldMapPictureCounts(blocks, squares, tiles, terrain, decorated, blank, tileElements));
        }

        /// <summary>Writes the finished pixels, north at the top.</summary>
        /// <param name="area">The area, for its empty-tile colour.</param>
        /// <param name="canvas">The canvas being filled.</param>
        /// <param name="blended">Packed HSL per tile, 0 where nothing resolved.</param>
        /// <param name="overlayIds">One-based overlay ids per tile, 0 for none.</param>
        /// <param name="floors">Resolves an overlay to its colour.</param>
        /// <returns>Row-major ARGB pixels.</returns>
        private static int[] Compose(WorldMapAreaDefinition area, WorldMapCanvas canvas, int[,] blended,
            int[,] overlayIds, WorldMapFloorPalette floors) {
            int width = canvas.Width;
            int height = canvas.Height;
            var pixels = new int[width * height];

            //-1 is the client's "no tint", and 0 is a real black tint that it would draw. Both occur.
            int tint = area.TintColour == -1
                ? MapPalette.NoColour
                : unchecked((int) 0xFF000000) | (area.TintColour & 0xFFFFFF);

            int background = floors.BackgroundRgb;

            for (int y = 0; y < height; y++) {
                int row = (height - 1 - y) * width;

                for (int x = 0; x < width; x++) {
                    int overlayId = overlayIds[x, y];
                    int overlayHsl = overlayId > 0 ? floors.ResolveOverlayColour(overlayId - 1) : MapPalette.NoColour;
                    int hsl = blended[x, y];

                    int argb;
                    if (overlayHsl != MapPalette.NoColour) {
                        //Whole tile rather than the client's shape mask. At one pixel per tile there
                        //is no sub-tile geometry to mask into.
                        argb = unchecked((int) 0xFF000000) | MapPalette.ToRgb(overlayHsl);
                    }
                    else if (hsl != 0) {
                        argb = unchecked((int) 0xFF000000) | MapPalette.ToRgb(hsl);
                    }
                    else if (tint != MapPalette.NoColour) {
                        argb = tint;
                    }
                    else {
                        /* The client's checkerboard, keyed on bit 2 of each map coordinate
                           (Class278.java:333). The coordinates are the map ones rather than the
                           canvas ones, so the pattern stays put when the canvas origin moves. */
                        argb = ((canvas.OriginX + x) & 4) != ((canvas.OriginY + y) & 4)
                            ? WorldMapAreaPicture.EmptyTileBlue
                            : background;
                    }

                    pixels[row + x] = argb;
                }
            }

            return pixels;
        }

        /// <summary>The sentence the view shows above the picture.</summary>
        /// <param name="area">The area being described.</param>
        /// <returns>What the picture is, and what it is not.</returns>
        private static string Describe(WorldMapAreaDefinition area) {
            /* Kept to two lines. It sits inside the pane the picture is drawn in, so every line it
               takes is a line the map does not get, and the tab's other notice already carries the
               statement that matters most - that this is not the terrain. */
            return "One pixel per stored tile, through the client's own underlay blend and palette. " +
                   "Deliberately not drawn: overlay shape masks, object map-scene sprites, and icon " +
                   "sprites with their labels - icons are marked instead. " +
                   (area.TintColour == -1
                       ? "Empty tiles take the client's blue checkerboard; this area stores no tint."
                       : "Empty tiles take this area's stored tint #" +
                         (area.TintColour & 0xFFFFFF).ToString("X6") + ".");
        }
    }

    /// <summary>
    ///     The cache lookups a render needs, memoised, so the same definition is decoded once.
    /// </summary>
    /// <remarks>
    ///     Held by the tab across areas rather than rebuilt per render. Index 23's largest raster is
    ///     4.7 MB of tiles and a floor lookup that reached the cache per tile would decode config
    ///     group 1 several million times; the client memoises for the same reason.
    ///     <para>
    ///     Every lookup swallows a failure and returns "nothing here". That is the client's own
    ///     behaviour for a floor the config archive does not carry, and it matters more on this index
    ///     than elsewhere: a single missing definition must cost one tile's colour, not the picture.
    ///     </para>
    /// </remarks>
    public sealed class WorldMapFloorPalette {
        private readonly RSCache cache;
        private readonly Dictionary<int, UnderlayColour?> underlays = new Dictionary<int, UnderlayColour?>();
        private readonly Dictionary<int, int> overlayColours = new Dictionary<int, int>();
        private readonly Dictionary<int, MapElementDefinition?> mapElements =
            new Dictionary<int, MapElementDefinition?>();

        private int background = WorldMapAreaPicture.EmptyTileBlue;
        private bool backgroundResolved;

        /// <summary>Binds the lookups to an open cache.</summary>
        /// <param name="openCache">The open cache.</param>
        public WorldMapFloorPalette(RSCache openCache) {
            cache = openCache ?? throw new ArgumentNullException(nameof(openCache));
        }

        /// <summary>
        ///     The colour the client fills the non-blue half of an untinted empty tile with.
        /// </summary>
        /// <remarks>
        ///     Exactly one floor overlay carries opcode 8, and it is that overlay's colour
        ///     (<c>Class278.java:337</c>). Found by scanning rather than by an id, because the id is
        ///     a property of one cache while the flag is a property of the format.
        /// </remarks>
        public int BackgroundRgb {
            get {
                if (backgroundResolved)
                    return background;

                backgroundResolved = true;
                try {
                    foreach (int id in cache.GetFileIds(RSConstants.CONFIG, RSConstants.FLOOR_OVERLAY_GROUP)) {
                        FloorOverlayDefinition overlay = cache.GetFloorOverlay(id);
                        if (!overlay.IsWorldMapBackground)
                            continue;

                        int hsl = ColourOf(overlay);
                        if (hsl != MapPalette.NoColour)
                            background = unchecked((int) 0xFF000000) | MapPalette.ToRgb(hsl);
                        break;
                    }
                }
                catch (Exception) {
                    //A cache with no floor overlay table keeps the blue, which is the only colour of
                    //the two that the client states as a literal.
                }

                return background;
            }
        }

        /// <summary>Resolves an underlay to the components the blender averages.</summary>
        /// <param name="definitionId">The zero-based underlay id.</param>
        /// <returns>The components, or null when the definition is absent.</returns>
        public UnderlayColour? ResolveUnderlay(int definitionId) {
            if (underlays.TryGetValue(definitionId, out UnderlayColour? known))
                return known;

            UnderlayColour? colour;
            try {
                colour = UnderlayColour.FromRgb(cache.GetFloorUnderlay(definitionId).Rgb);
            }
            catch (Exception) {
                colour = null;
            }

            underlays[definitionId] = colour;
            return colour;
        }

        /// <summary>Resolves an overlay to packed map HSL.</summary>
        /// <param name="definitionId">The zero-based overlay id.</param>
        /// <returns>The packed HSL, or <see cref="MapPalette.NoColour"/>.</returns>
        public int ResolveOverlayColour(int definitionId) {
            if (overlayColours.TryGetValue(definitionId, out int known))
                return known;

            int colour;
            try {
                colour = ColourOf(cache.GetFloorOverlay(definitionId));
            }
            catch (Exception) {
                colour = MapPalette.NoColour;
            }

            overlayColours[definitionId] = colour;
            return colour;
        }

        /// <summary>The map element a placement names, or null when group 36 does not hold it.</summary>
        /// <param name="mapElementId">The config group 36 file id.</param>
        /// <returns>The definition, or null.</returns>
        public MapElementDefinition? ResolveMapElement(int mapElementId) {
            if (mapElements.TryGetValue(mapElementId, out MapElementDefinition? known))
                return known;

            MapElementDefinition? definition;
            try {
                definition = new MapElementDefinition { Id = mapElementId };
                definition.Decode(cache.ReadFile(RSConstants.CONFIG, RSConstants.MAP_ELEMENT_GROUP, mapElementId));
            }
            catch (Exception) {
                definition = null;
            }

            mapElements[mapElementId] = definition;
            return definition;
        }

        /// <summary>
        ///     An overlay's colour, in the order the client picks it.
        /// </summary>
        /// <remarks>
        ///     Secondary, then the texture's declared colour, then primary
        ///     (<c>Node_Sub16.method1149</c>). The texture's <c>field1831</c> is already packed HSL
        ///     in the same space and is returned verbatim; routing it through the model palette
        ///     instead turns the whole map flat green, which is a mistake this project has already
        ///     made once in <c>MapRasteriser</c>.
        /// </remarks>
        /// <param name="overlay">The overlay definition.</param>
        /// <returns>The packed HSL, or <see cref="MapPalette.NoColour"/>.</returns>
        private static int ColourOf(FloorOverlayDefinition overlay) {
            if (overlay.SecondaryRgb != -1)
                return MapPalette.FromRgb(overlay.SecondaryRgb);

            if (overlay.TextureId >= 0 &&
                TextureManager.Textures.TryGetValue(overlay.TextureId, out TextureDefinition? texture) &&
                texture != null && !texture.field1825) {
                //Both consumers clamp the lightness before the palette lookup, so a texture declaring
                //either extreme would otherwise come out pure black or white.
                int hsl = texture.field1831 & 0xFFFF;
                return (hsl & 0xFF80) | Math.Clamp(hsl & 0x7F, 2, 126);
            }

            return overlay.HasPrimaryRgb ? MapPalette.FromRgb(overlay.PrimaryRgb) : MapPalette.NoColour;
        }
    }
}
