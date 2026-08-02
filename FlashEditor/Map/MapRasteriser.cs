using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using FlashEditor.cache;
using FlashEditor.Cache.Region;
using FlashEditor.Cache.Util;
using FlashEditor.Definitions;
using FlashEditor.cache.sprites;
using FlashEditor.Definitions.Sprites;

using MapRegion = FlashEditor.Cache.Region.Region;

namespace FlashEditor.Map {
    /// <summary>Layers a <see cref="MapRasteriser"/> can draw.</summary>
    [Flags]
    public enum MapLayers {
        /// <summary>Nothing.</summary>
        None = 0,

        /// <summary>Blended floor underlay colour.</summary>
        Underlay = 1,

        /// <summary>Shape-masked floor overlay colour.</summary>
        Overlay = 2,

        /// <summary>Walls and wall decorations, as edge lines.</summary>
        Walls = 4,

        /// <summary>Ground decorations, as small marks.</summary>
        GroundDecoration = 8,

        /// <summary>Game objects, as footprint outlines.</summary>
        GameObjects = 16,

        /// <summary>Tile flags, as a hatched wash.</summary>
        TileFlags = 32,

        /// <summary>Square and chunk boundaries.</summary>
        Grid = 64,

        /// <summary>Bank, altar, staircase and furnace icons.</summary>
        MapSceneIcons = 128,

        /// <summary>Everything that reads as terrain.</summary>
        Terrain = Underlay | Overlay,

        /// <summary>
        ///     The sensible default for a viewer.
        /// </summary>
        /// <remarks>
        ///     <see cref="GameObjects"/> is deliberately excluded. Outlining every object is an
        ///     editor affordance rather than something the client does - its minimap draws walls and
        ///     mapscene icons only - and at a dense square it puts a box around every tree and fence
        ///     post, which buries the terrain underneath.
        /// </remarks>
        Default = Underlay | Overlay | Walls | GroundDecoration | MapSceneIcons | Grid
    }

    /// <summary>
    ///     Draws a <see cref="MapScene"/> top-down into a bitmap.
    /// </summary>
    /// <remarks>
    ///     This is deliberately not a port of the client's minimap. That rasteriser orthographically
    ///     re-projects the built 3D ground mesh, which would mean porting geometry, normals and
    ///     lighting to obtain a picture that is worse for editing than a flat one. What is taken
    ///     from it is the tile colour model, the shape masks and the 4-pixels-per-tile convention.
    ///
    ///     See <c>reference/hydra-637-maps/05-colour-and-rendering.md</c>.
    /// </remarks>
    public sealed class MapRasteriser {
        private readonly RSCache cache;
        private readonly Dictionary<int, UnderlayColour?> underlayCache = new Dictionary<int, UnderlayColour?>();
        private readonly Dictionary<int, FloorOverlayDefinition> overlayCache = new Dictionary<int, FloorOverlayDefinition>();
        private readonly Dictionary<int, ObjectInfo> objectCache = new Dictionary<int, ObjectInfo>();
        private readonly Dictionary<int, int> textureColourCache = new Dictionary<int, int>();
        private readonly Dictionary<int, Bitmap> iconCache = new Dictionary<int, Bitmap>();
        private readonly Dictionary<int, bool> iconStretch = new Dictionary<int, bool>();

        /// <summary>Screen pixels per map tile. The client's minimap uses 4.</summary>
        public int TilePixels { get; set; } = 8;

        /// <summary>Colour drawn where a tile has neither underlay nor overlay.</summary>
        public Color VoidColour { get; set; } = Color.FromArgb(255, 12, 12, 16);

        /// <summary>Colour of wall marks.</summary>
        /// <remarks>The client randomises this per minimap regeneration; an editor should not.</remarks>
        public Color WallColour { get; set; } = Color.FromArgb(255, 236, 236, 236);

        /// <summary>Colour of game object footprint outlines.</summary>
        public Color ObjectColour { get; set; } = Color.FromArgb(190, 255, 190, 90);

        /// <summary>Colour of ground decoration marks.</summary>
        public Color GroundDecorationColour { get; set; } = Color.FromArgb(210, 120, 200, 255);

        /// <summary>Colour of square boundary lines.</summary>
        public Color SquareGridColour { get; set; } = Color.FromArgb(150, 255, 80, 80);

        /// <summary>Colour of chunk boundary lines, drawn only when zoomed in.</summary>
        public Color ChunkGridColour { get; set; } = Color.FromArgb(60, 255, 255, 255);

        /// <summary>Creates a rasteriser reading definitions from a cache.</summary>
        /// <param name="cache">The cache to resolve floor and object definitions from.</param>
        public MapRasteriser(RSCache cache) {
            this.cache = cache ?? throw new ArgumentNullException(nameof(cache));
        }

        /// <summary>
        ///     Renders a whole scene at the current zoom.
        /// </summary>
        /// <param name="scene">The scene to draw.</param>
        /// <param name="plane">The plane to draw.</param>
        /// <param name="layers">Which layers to include.</param>
        /// <returns>A new bitmap sized to the scene. The caller owns it.</returns>
        public DirectBitmap Render(MapScene scene, int plane, MapLayers layers = MapLayers.Default) {
            if (scene == null) throw new ArgumentNullException(nameof(scene));

            var target = new DirectBitmap(scene.WidthTiles * TilePixels, scene.HeightTiles * TilePixels);
            Render(scene, plane, target, layers);
            return target;
        }

        /// <summary>
        ///     Renders a scene into an existing bitmap.
        /// </summary>
        /// <param name="scene">The scene to draw.</param>
        /// <param name="plane">The plane to draw.</param>
        /// <param name="target">The bitmap to draw into.</param>
        /// <param name="layers">Which layers to include.</param>
        public void Render(MapScene scene, int plane, DirectBitmap target, MapLayers layers = MapLayers.Default) {
            if (scene == null) throw new ArgumentNullException(nameof(scene));
            if (target == null) throw new ArgumentNullException(nameof(target));

            using (Graphics g = Graphics.FromImage(target.Bitmap)) {
                g.Clear(VoidColour);

                //Aliasing is invisible at 4 or more pixels per tile, and antialiasing leaves seams
                //between adjacent fills where the coverage does not quite reach 1.
                g.SmoothingMode = SmoothingMode.None;
                g.PixelOffsetMode = PixelOffsetMode.Half;

                if ((layers & MapLayers.Underlay) != 0 || (layers & MapLayers.Overlay) != 0)
                    DrawTerrain(g, scene, plane, layers);

                if ((layers & MapLayers.TileFlags) != 0)
                    DrawTileFlags(g, scene, plane);

                DrawLocations(g, scene, plane, layers);

                if ((layers & MapLayers.Grid) != 0)
                    DrawGrid(g, scene);
            }
        }

        private void DrawTerrain(Graphics g, MapScene scene, int plane, MapLayers layers) {
            int[,] blended = (layers & MapLayers.Underlay) != 0
                ? UnderlayBlender.Blend(scene.UnderlayGrid(plane), ResolveUnderlay)
                : null;

            for (int x = 0; x < scene.WidthTiles; x++) {
                for (int y = 0; y < scene.HeightTiles; y++) {
                    int overlayId = scene.OverlayId(plane, x, y);
                    FloorOverlayDefinition overlay = overlayId > 0 ? ResolveOverlay(overlayId - 1) : null;

                    int overlayHsl = overlay == null ? MapPalette.NoColour : ResolveOverlayColour(overlay);
                    int shape = overlayId > 0 ? scene.OverlayShape(plane, x, y) : TileShapes.ShapeFullUnderlay;

                    //The hole rule: an overlay with neither colour suppresses the tile completely,
                    //and the underlay does NOT show through. This is what makes cave mouths and
                    //dungeon voids read correctly instead of appearing as solid ground.
                    if (overlay != null && overlayHsl == MapPalette.NoColour && overlay.SecondaryRgb == -1
                        && overlay.PrimaryRgb == FloorOverlayDefinition.TransparentRgb)
                        continue;

                    RectangleF tile = TileRect(scene, x, y);

                    bool overlayCoversTile = overlay != null && TileShapes.IsFullOverlay(shape);

                    if (blended != null && !overlayCoversTile) {
                        int hsl = blended[x, y];
                        if (hsl != 0)
                            FillRect(g, tile, MapPalette.ToRgb(hsl));
                    }

                    if (overlay == null || (layers & MapLayers.Overlay) == 0 || overlayHsl == MapPalette.NoColour)
                        continue;

                    int rgb = MapPalette.ToRgb(overlayHsl);
                    foreach (float[] triangle in TileShapes.OverlayTriangles(shape, scene.OverlayRotation(plane, x, y)))
                        FillTriangle(g, tile, triangle, rgb);
                }
            }
        }

        private void DrawTileFlags(Graphics g, MapScene scene, int plane) {
            using (var blocked = new SolidBrush(Color.FromArgb(70, 255, 40, 40)))
            using (var bridge = new SolidBrush(Color.FromArgb(70, 40, 160, 255))) {
                for (int x = 0; x < scene.WidthTiles; x++) {
                    for (int y = 0; y < scene.HeightTiles; y++) {
                        int flags = scene.TileFlags(plane, x, y);
                        if (flags == 0)
                            continue;

                        RectangleF tile = TileRect(scene, x, y);
                        if ((flags & 0x1) != 0) g.FillRectangle(blocked, tile);
                        if ((flags & 0x2) != 0) g.FillRectangle(bridge, tile);
                    }
                }
            }
        }

        private void DrawLocations(Graphics g, MapScene scene, int plane, MapLayers layers) {
            float wallWidth = Math.Max(1f, TilePixels / 4f);

            using (var wallPen = new Pen(WallColour, wallWidth))
            using (var objectPen = new Pen(ObjectColour, 1f))
            using (var decorationBrush = new SolidBrush(GroundDecorationColour)) {
                foreach ((Location loc, int sceneX, int sceneY) in scene.Locations(plane)) {
                    RectangleF tile = TileRect(scene, sceneX, sceneY);

                    /* An object carrying a map scene icon draws the icon INSTEAD of its default
                       mark, whatever group it belongs to - the client does this for walls, wall
                       decorations and ground decorations alike (Class277.java:122, :178, :203).
                       A bank booth is a wall, and it should read as a bank rather than as a line. */
                    if ((layers & MapLayers.MapSceneIcons) != 0 && DrawMapSceneIcon(g, scene, loc, sceneX, sceneY))
                        continue;

                    switch (LocGroups.Of(loc.Shape)) {
                        case LocGroup.Wall:
                        case LocGroup.WallDecoration:
                            if ((layers & MapLayers.Walls) != 0)
                                DrawWall(g, wallPen, tile, loc.Shape, loc.Orientation);
                            break;

                        case LocGroup.GroundDecoration:
                            if ((layers & MapLayers.GroundDecoration) != 0) {
                                float inset = TilePixels * 0.3f;
                                g.FillEllipse(decorationBrush, RectangleF.Inflate(tile, -inset, -inset));
                            }
                            break;

                        case LocGroup.GameObject:
                            if ((layers & MapLayers.GameObjects) != 0)
                                DrawFootprint(g, objectPen, scene, loc, sceneX, sceneY);
                            break;
                    }
                }
            }
        }

        /// <summary>
        ///     Draws a wall against the tile edge its shape and rotation select.
        /// </summary>
        /// <remarks>
        ///     Rotation 0 is west, 1 north, 2 east, 3 south. Shape 0 is a single edge, shape 2 is
        ///     the two edges meeting at a corner, and shapes 1 and 3 are corner posts.
        /// </remarks>
        private static void DrawWall(Graphics g, Pen pen, RectangleF tile, int shape, int rotation) {
            float l = tile.Left, r = tile.Right, t = tile.Top, b = tile.Bottom;

            //Screen Y grows downward while scene Y grows north, so the north edge is Top.
            PointF west = new PointF(l, t), westB = new PointF(l, b);
            PointF north = new PointF(l, t), northB = new PointF(r, t);
            PointF east = new PointF(r, t), eastB = new PointF(r, b);
            PointF south = new PointF(l, b), southB = new PointF(r, b);

            switch (shape) {
                case 0:
                case 4:
                case 5:
                case 6:
                case 7:
                case 8:
                    DrawEdge(g, pen, rotation, west, westB, north, northB, east, eastB, south, southB);
                    break;

                case 2:
                    //An L: the selected edge and the next one clockwise.
                    DrawEdge(g, pen, rotation, west, westB, north, northB, east, eastB, south, southB);
                    DrawEdge(g, pen, rotation + 1, west, westB, north, northB, east, eastB, south, southB);
                    break;

                case 1:
                case 3:
                    //Corner posts. The client draws these as a single pixel; a short diagonal reads
                    //better at editor zoom levels and still sits in the right corner.
                    DrawCornerPost(g, pen, tile, rotation);
                    break;
            }
        }

        private static void DrawEdge(Graphics g, Pen pen, int rotation,
            PointF west, PointF westB, PointF north, PointF northB,
            PointF east, PointF eastB, PointF south, PointF southB) {

            switch (rotation & 3) {
                case 0: g.DrawLine(pen, west, westB); break;
                case 1: g.DrawLine(pen, north, northB); break;
                case 2: g.DrawLine(pen, east, eastB); break;
                case 3: g.DrawLine(pen, south, southB); break;
            }
        }

        private static void DrawCornerPost(Graphics g, Pen pen, RectangleF tile, int rotation) {
            float size = Math.Max(1f, tile.Width * 0.35f);

            PointF corner = (rotation & 3) switch {
                0 => new PointF(tile.Left, tile.Top),
                1 => new PointF(tile.Right, tile.Top),
                2 => new PointF(tile.Right, tile.Bottom),
                _ => new PointF(tile.Left, tile.Bottom)
            };

            g.DrawLine(pen,
                new PointF(corner.X - size / 2, corner.Y),
                new PointF(corner.X + size / 2, corner.Y));
        }

        private void DrawFootprint(Graphics g, Pen pen, MapScene scene, Location loc, int sceneX, int sceneY) {
            RectangleF area = FootprintRect(scene, loc, sceneX, sceneY);
            g.DrawRectangle(pen, area.Left, area.Top, area.Width - 1, area.Height - 1);
        }

        /// <summary>
        ///     The screen rectangle a location's tile footprint covers.
        /// </summary>
        /// <remarks>
        ///     Anchored on the south-west tile, which is where a multi-tile location is recorded and
        ///     where the client anchors both its outline and its icon (Class122.java:122). Odd
        ///     rotations swap the two extents: the definition's opcode 14 is the X extent at
        ///     rotation 0 and opcode 15 the Y extent, despite the client's field names saying the
        ///     reverse - see reference/hydra-637-maps/03-locs-l.md.
        /// </remarks>
        private RectangleF FootprintRect(MapScene scene, Location loc, int sceneX, int sceneY) {
            ObjectFootprint footprint = ResolveFootprint(loc.Id);

            int extentX = (loc.Orientation & 1) == 0 ? footprint.SizeX : footprint.SizeY;
            int extentY = (loc.Orientation & 1) == 0 ? footprint.SizeY : footprint.SizeX;

            RectangleF origin = TileRect(scene, sceneX, sceneY);
            return new RectangleF(
                origin.Left,
                origin.Top - (extentY - 1) * TilePixels,
                extentX * TilePixels,
                extentY * TilePixels);
        }

        /// <summary>
        ///     Draws a location's map scene icon, if it has one.
        /// </summary>
        /// <remarks>
        ///     The client draws the sprite at its native pixel size, authored for 4 pixels per tile,
        ///     unless the icon sets its stretch flag - in which case it fills the footprint instead
        ///     (Class122.java:112-117). At any other zoom the world map scales the native size by
        ///     <c>pixelsPerTile / 4</c> (Class278.java:878), which is what happens here.
        /// </remarks>
        /// <returns><c>true</c> when an icon was drawn, so the caller skips the default mark.</returns>
        private bool DrawMapSceneIcon(Graphics g, MapScene scene, Location loc, int sceneX, int sceneY) {
            Bitmap icon = ResolveIcon(loc.Id);
            if (icon == null)
                return false;

            RectangleF area = FootprintRect(scene, loc, sceneX, sceneY);

            float width = icon.Width * TilePixels / 4f;
            float height = icon.Height * TilePixels / 4f;

            if (StretchIconToFootprint(loc.Id)) {
                width = area.Width;
                height = area.Height;
            }

            //Anchored to the footprint's south-west corner, growing north and east, matching the
            //client rather than centring the icon on the object.
            g.DrawImage(icon, area.Left, area.Bottom - height, width, height);
            return true;
        }

        private void DrawGrid(Graphics g, MapScene scene) {
            using (var squarePen = new Pen(SquareGridColour, 1f))
            using (var chunkPen = new Pen(ChunkGridColour, 1f)) {
                int width = scene.WidthTiles * TilePixels;
                int height = scene.HeightTiles * TilePixels;

                if (TilePixels >= 16) {
                    for (int t = 0; t <= scene.WidthTiles; t += 8)
                        g.DrawLine(chunkPen, t * TilePixels, 0, t * TilePixels, height);
                    for (int t = 0; t <= scene.HeightTiles; t += 8)
                        g.DrawLine(chunkPen, 0, t * TilePixels, width, t * TilePixels);
                }

                for (int t = 0; t <= scene.WidthTiles; t += MapRegion.WIDTH)
                    g.DrawLine(squarePen, t * TilePixels, 0, t * TilePixels, height);
                for (int t = 0; t <= scene.HeightTiles; t += MapRegion.HEIGHT)
                    g.DrawLine(squarePen, 0, t * TilePixels, width, t * TilePixels);
            }
        }

        /// <summary>
        ///     The screen rectangle for a scene tile.
        /// </summary>
        /// <remarks>Scene Y runs north, screen Y runs down, so the axis is flipped here.</remarks>
        private RectangleF TileRect(MapScene scene, int sceneX, int sceneY) {
            float top = (scene.HeightTiles - 1 - sceneY) * TilePixels;
            return new RectangleF(sceneX * TilePixels, top, TilePixels, TilePixels);
        }

        private static void FillRect(Graphics g, RectangleF rect, int rgb) {
            using (var brush = new SolidBrush(Color.FromArgb(255, (rgb >> 16) & 0xFF, (rgb >> 8) & 0xFF, rgb & 0xFF)))
                g.FillRectangle(brush, rect);
        }

        private static void FillTriangle(Graphics g, RectangleF tile, float[] triangle, int rgb) {
            //Triangle coordinates arrive in unit tile space with Y running north; flip to screen.
            var points = new[] {
                new PointF(tile.Left + triangle[0] * tile.Width, tile.Bottom - triangle[1] * tile.Height),
                new PointF(tile.Left + triangle[2] * tile.Width, tile.Bottom - triangle[3] * tile.Height),
                new PointF(tile.Left + triangle[4] * tile.Width, tile.Bottom - triangle[5] * tile.Height)
            };

            using (var brush = new SolidBrush(Color.FromArgb(255, (rgb >> 16) & 0xFF, (rgb >> 8) & 0xFF, rgb & 0xFF)))
                g.FillPolygon(brush, points);
        }

        private UnderlayColour? ResolveUnderlay(int definitionId) {
            if (underlayCache.TryGetValue(definitionId, out UnderlayColour? known))
                return known;

            UnderlayColour? colour;
            try {
                colour = UnderlayColour.FromRgb(cache.GetFloorUnderlay(definitionId).Rgb);
            }
            catch (Exception) {
                //A terrain file can name a definition the config archive does not carry. The client
                //renders nothing for it rather than failing the square.
                colour = null;
            }

            underlayCache[definitionId] = colour;
            return colour;
        }

        private FloorOverlayDefinition ResolveOverlay(int definitionId) {
            if (overlayCache.TryGetValue(definitionId, out FloorOverlayDefinition known))
                return known;

            FloorOverlayDefinition def;
            try {
                def = cache.GetFloorOverlay(definitionId);
            }
            catch (Exception) {
                def = null;
            }

            overlayCache[definitionId] = def;
            return def;
        }

        /// <summary>
        ///     Picks a tile's overlay colour the way the client does.
        /// </summary>
        /// <remarks>
        ///     Secondary colour, then the texture's representative colour, then primary
        ///     (Node_Sub16.method1149).
        /// </remarks>
        private int ResolveOverlayColour(FloorOverlayDefinition overlay) {
            if (overlay.SecondaryRgb != -1)
                return MapPalette.FromRgb(overlay.SecondaryRgb);

            int textured = ResolveTextureColour(overlay.TextureId);
            if (textured != MapPalette.NoColour)
                return textured;

            if (!overlay.HasPrimaryRgb)
                return MapPalette.NoColour;

            return MapPalette.FromRgb(overlay.PrimaryRgb);
        }

        /// <summary>
        ///     A texture's declared colour, as packed map HSL.
        /// </summary>
        /// <remarks>
        ///     <c>field1831</c> is already a packed HSL in the same space as an overlay's primary
        ///     and secondary colours, and the client returns it verbatim
        ///     (Node_Sub16.java:75-80, <c>return class238.aShort1831</c>). It must not be routed
        ///     through <c>TextureManager.RepresentativeRgb</c>, which converts it to RGB through the
        ///     <em>model</em> palette for the renderer's benefit; doing that and folding the result
        ///     back through the map palette turns the whole world flat green and drains the colour
        ///     out of water.
        ///
        ///     <c>field1825</c> is the client's <c>aBoolean1825</c> gate: when set, the texture
        ///     declines to stand in for a colour and the tile falls through to its primary.
        ///
        ///     Texture metadata is loaded by <c>GLTextureCache</c> at cache-open. A renderer used
        ///     before that, or in a headless test, sees an empty table and every textured tile falls
        ///     back to its primary colour rather than failing.
        /// </remarks>
        /// <param name="textureId">A texture id, or -1 for none.</param>
        /// <returns>Packed map HSL, or <see cref="MapPalette.NoColour"/>.</returns>
        private int ResolveTextureColour(int textureId) {
            if (textureId < 0)
                return MapPalette.NoColour;

            if (textureColourCache.TryGetValue(textureId, out int known))
                return known;

            int colour = MapPalette.NoColour;
            if (TextureManager.Textures.TryGetValue(textureId, out TextureDefinition def)
                && def != null && !def.field1825) {
                //Both consumers clamp the lightness before the palette lookup (Class345.method3825,
                //reached from Class278.java:731 and from the scene path). A texture whose declared
                //lightness sits at either extreme would otherwise come out pure black or white.
                int hsl = def.field1831 & 0xFFFF;
                int lightness = Math.Clamp(hsl & 0x7F, 2, 126);
                colour = (hsl & 0xFF80) | lightness;
            }

            //The resolved colour is cached, never the definition: reopening a cache calls
            //TextureManager.Clear(), which disposes every TextureDefinition it holds.
            textureColourCache[textureId] = colour;
            return colour;
        }

        /// <summary>
        ///     The icon bitmap for an object, or <c>null</c> when it has none.
        /// </summary>
        /// <remarks>
        ///     Memoised by object id. 3,267 object definitions carry an icon and there are only
        ///     around 100 distinct icons, but <c>RSCache.GetSprite</c> re-decodes on every call and
        ///     throws rather than returning null when a group is absent, so an unmemoised lookup
        ///     would decode the same sprite thousands of times per repaint.
        /// </remarks>
        private Bitmap ResolveIcon(int objectId) => ResolveObject(objectId).Icon;

        private bool StretchIconToFootprint(int objectId) => ResolveObject(objectId).StretchIcon;

        /// <summary>
        ///     Everything the renderer needs about an object, resolved once per id.
        /// </summary>
        /// <remarks>
        ///     Footprint and icon come from the same definition, so they are read together. Looking
        ///     them up separately doubled the number of <c>GetObjectDefinition</c> calls, and with
        ///     over twenty thousand locations in a dense scene that alone took the render from
        ///     150ms to well over a second.
        /// </remarks>
        private ObjectInfo ResolveObject(int objectId) {
            if (objectCache.TryGetValue(objectId, out ObjectInfo known))
                return known;

            var info = new ObjectInfo { SizeX = 1, SizeY = 1 };

            try {
                ObjectDefinition def = cache.GetObjectDefinition(objectId >> 8, objectId & 0xFF);

                //The size fields are bytes, so widen before comparing or the overload is ambiguous.
                info.SizeX = Math.Max(1, (int) def.sizeX);
                info.SizeY = Math.Max(1, (int) def.sizeY);

                //Opcode 102, despite the private field being called mapAreaId. Opcode 68 is dead.
                int iconId = def.mapSceneIcon;
                if (iconId >= 0) {
                    info.Icon = ResolveIconBitmap(iconId);
                    info.StretchIcon = iconStretch.TryGetValue(iconId, out bool stretch) && stretch;
                }
            }
            catch (Exception) {
                //Eight shipped loc files reference ids that index 16 does not carry, so a missing
                //definition is expected data rather than an error. A 1x1 footprint and no icon is
                //the safe read.
            }

            objectCache[objectId] = info;
            return info;
        }

        /// <summary>
        ///     The bitmap for one map scene icon, keyed by icon id rather than by object.
        /// </summary>
        /// <remarks>
        ///     There are about a hundred icons and several thousand objects referencing them, so
        ///     keying the bitmap - and the per-pixel tint that builds it - by object id would repeat
        ///     the same work dozens of times per icon.
        /// </remarks>
        private Bitmap ResolveIconBitmap(int iconId) {
            if (iconCache.TryGetValue(iconId, out Bitmap known))
                return known;

            Bitmap icon = null;
            try {
                MapSceneIconDefinition scene = cache.GetMapSceneIcon(iconId);
                iconStretch[iconId] = scene.StretchToFootprint;

                if (scene.SpriteGroupId > 0) {
                    SpriteDefinition sprite = cache.GetSprite(scene.SpriteGroupId);
                    if (sprite?.thumb != null)
                        icon = Tint(sprite.thumb, scene.TintRgb);
                }
            }
            catch (Exception) {
                //A missing icon definition or sprite group means no icon, not a failed render.
            }

            iconCache[iconId] = icon;
            return icon;
        }

        /// <summary>
        ///     Copies a sprite, optionally recolouring it to a flat tint.
        /// </summary>
        /// <remarks>
        ///     The client's tint is a silhouette fill, not a blend: it draws the sprite's shape
        ///     entirely in the tint colour (Class122.java:119-120 into ImageArchive.java:35-37).
        ///     Applied once per icon rather than per draw.
        /// </remarks>
        private static Bitmap Tint(Bitmap source, int tintRgb) {
            var copy = new Bitmap(source.Width, source.Height, System.Drawing.Imaging.PixelFormat.Format32bppArgb);

            for (int x = 0; x < source.Width; x++) {
                for (int y = 0; y < source.Height; y++) {
                    Color pixel = source.GetPixel(x, y);
                    if (pixel.A == 0)
                        continue;

                    copy.SetPixel(x, y, tintRgb == 0
                        ? pixel
                        : Color.FromArgb(pixel.A, (tintRgb >> 16) & 0xFF, (tintRgb >> 8) & 0xFF, tintRgb & 0xFF));
                }
            }

            return copy;
        }

        private ObjectFootprint ResolveFootprint(int objectId) {
            ObjectInfo info = ResolveObject(objectId);
            return new ObjectFootprint(info.SizeX, info.SizeY);
        }

        private readonly struct ObjectFootprint {
            public int SizeX { get; }
            public int SizeY { get; }

            public ObjectFootprint(int sizeX, int sizeY) {
                SizeX = sizeX;
                SizeY = sizeY;
            }
        }

        /// <summary>What the renderer needs about one object definition.</summary>
        private sealed class ObjectInfo {
            /// <summary>Tile extent along X at rotation 0, from opcode 14.</summary>
            public int SizeX { get; set; }

            /// <summary>Tile extent along Y at rotation 0, from opcode 15.</summary>
            public int SizeY { get; set; }

            /// <summary>The map scene icon to draw, or null.</summary>
            public Bitmap Icon { get; set; }

            /// <summary>Whether that icon stretches to the footprint rather than drawing at native size.</summary>
            public bool StretchIcon { get; set; }
        }
    }
}
