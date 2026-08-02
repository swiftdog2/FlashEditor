using System;
using System.Collections.Generic;

namespace FlashEditor.Cache.Region {
    /// <summary>
    ///     One 64x64 map square, and the decoders for its terrain (<c>m</c>) and location
    ///     (<c>l</c>) files.
    /// </summary>
    /// <remarks>
    ///     Byte formats are documented in <c>reference/hydra-637-maps/02-terrain-m.md</c> and
    ///     <c>03-locs-l.md</c>, both derived from the bundled 637 client and verified by decoding
    ///     every map square in the shipped 639 cache to exact buffer consumption.
    /// </remarks>
    public class Region {
        /// <summary>Tiles along each axis of a map square.</summary>
        public const int WIDTH = 64;

        /// <summary>Tiles along each axis of a map square.</summary>
        public const int HEIGHT = 64;

        /// <summary>Planes in a surface terrain file.</summary>
        public const int PLANES = 4;

        /// <summary>
        ///     World units the terrain rises per unit of the stored height byte.
        /// </summary>
        /// <remarks>
        ///     This client is a 4x rescale of RS2 - tile size is 512 rather than 128 - so every
        ///     vertical quantity is four times the RS2 value. Class305.java:1970 computes
        ///     <c>-h * 8 &lt;&lt; 2</c>. Negative because Y-up is negative here.
        /// </remarks>
        public const int HEIGHT_UNITS_PER_STEP = 32;

        /// <summary>
        ///     World units each plane sits above the one below when a tile carries no height.
        /// </summary>
        /// <remarks>Class305.java:1940-1941. The RS2 value is 240.</remarks>
        public const int PLANE_HEIGHT_DROP = 960;

        private readonly int regionID;
        private readonly int baseX;
        private readonly int baseY;

        //Heights are a VERTEX grid, one larger on each axis than the tile grid: the renderer
        //reads vertex x+1 and y+1 when building a tile quad (Class305.java:127).
        private int[,,] tileHeights = new int[0, 0, 0];
        private byte[,,] renderRules = new byte[0, 0, 0];
        private int[,,] overlayIds = new int[0, 0, 0];
        private byte[,,] overlayShapes = new byte[0, 0, 0];
        private byte[,,] overlayRotations = new byte[0, 0, 0];
        private int[,,] underlayIds = new int[0, 0, 0];

        //Whether a tile stored its height explicitly (opcode 1) rather than leaving the
        //decoder to derive one (opcode 0). Some tiles store a height that happens to equal
        //the derived value, so the choice cannot be recovered by comparing them.
        private bool[,,] heightExplicit = new bool[0, 0, 0];

        //The height byte exactly as stored. It cannot be recomputed from the decoded height: the
        //decoder maps a stored 1 to 0, so bytes 0 and 1 both mean height 0, and the shipped files
        //use both.
        private byte[,,] rawHeightByte = new byte[0, 0, 0];

        //Whether an edit replaced the height, in which case the stored byte has to be recomputed.
        private bool[,,] heightEdited = new bool[0, 0, 0];

        private int planeCount;

        private readonly List<Location> locations = new List<Location>();

        /// <summary>Number of planes the last terrain load decoded.</summary>
        public int PlaneCount => planeCount;

        /// <summary>
        ///     The bytes of the terrain file following the tile grid, kept verbatim.
        /// </summary>
        /// <remarks>
        ///     Environment, point lights and a shadow map. Nothing here models them, but they
        ///     cannot be re-derived either, so a write path has to put them back untouched. 1324 of
        ///     the 1684 shipped terrain files carry one.
        /// </remarks>
        public byte[] ExtrasTail { get; private set; } = Array.Empty<byte>();

        /// <summary>
        ///     The terrain file exactly as decoded, or empty when none was loaded.
        /// </summary>
        /// <remarks>
        ///     Retained so an unedited square can be written back as the bytes it came from rather
        ///     than re-encoded. The archive CRC covers those bytes, so re-encoding a square nobody
        ///     touched risks changing them for no reason.
        /// </remarks>
        public byte[] RawTerrain { get; private set; } = Array.Empty<byte>();

        /// <summary>The location file exactly as decoded, or empty when none was loaded.</summary>
        public byte[] RawLocations { get; private set; } = Array.Empty<byte>();

        /// <summary>Creates a region from its packed id.</summary>
        /// <param name="id">Region id, <c>(regionX &lt;&lt; 8) | regionY</c>.</param>
        public Region(int id) {
            regionID = id;
            baseX = (id >> 8 & 0xFF) << 6;
            baseY = (id & 0xFF) << 6;
            Allocate(PLANES);
        }

        private void Allocate(int planes) {
            planeCount = planes;
            tileHeights = new int[planes, WIDTH + 1, HEIGHT + 1];
            renderRules = new byte[planes, WIDTH, HEIGHT];
            overlayIds = new int[planes, WIDTH, HEIGHT];
            overlayShapes = new byte[planes, WIDTH, HEIGHT];
            overlayRotations = new byte[planes, WIDTH, HEIGHT];
            underlayIds = new int[planes, WIDTH, HEIGHT];
            heightExplicit = new bool[planes, WIDTH, HEIGHT];
            rawHeightByte = new byte[planes, WIDTH, HEIGHT];
            heightEdited = new bool[planes, WIDTH, HEIGHT];
            locations.Clear();
            ExtrasTail = Array.Empty<byte>();
            RawTerrain = Array.Empty<byte>();
            RawLocations = Array.Empty<byte>();
        }

        /// <summary>
        ///     Decodes a terrain (<c>m</c> or <c>um</c>) file.
        /// </summary>
        /// <remarks>
        ///     Iteration is plane-major, then X, then Y (Class305.java:759-767). Getting that order
        ///     wrong transposes the square. After the grid the same buffer continues into a
        ///     variable-length extras section, which is captured whole rather than parsed.
        /// </remarks>
        /// <param name="buf">The decompressed terrain file.</param>
        /// <param name="planes">
        ///     Planes to decode. Surface squares carry 4; the underwater <c>um</c> squares carry 1,
        ///     and every one of the 900 shipped <c>um</c> files fails to consume exactly with more.
        /// </param>
        /// <exception cref="System.IO.InvalidDataException">The stream desynchronised.</exception>
        public void LoadTerrain(JagStream buf, int planes = PLANES) {
            Allocate(planes);

            int start = buf.Position;

            for (int z = 0; z < planes; z++) {
                for (int x = 0; x < WIDTH; x++) {
                    for (int y = 0; y < HEIGHT; y++) {
                        DecodeTile(buf, z, x, y);
                    }
                }
            }

            int tailStart = buf.Position;
            ParseExtrasTail(buf);

            int tailLength = buf.Position - tailStart;
            if (tailLength > 0) {
                buf.Seek(tailStart);
                ExtrasTail = buf.ReadBytes(tailLength);
            }

            int end = buf.Position;
            buf.Seek(start);
            RawTerrain = buf.ReadBytes(end - start);

            Dirty = false;
        }

        /// <summary>
        ///     Walks the environment, lighting and shadow section that follows the tile grid.
        /// </summary>
        /// <remarks>
        ///     Nothing here is modelled - the section is kept verbatim in <see cref="ExtrasTail"/>
        ///     so a write path can put it back. It is walked rather than skipped because doing so
        ///     is the only way to prove the grid decoder stopped in the right place: the grid has
        ///     no length prefix, so an error in it would otherwise be absorbed silently by
        ///     whatever bytes remain. Every opcode here is fixed-length or self-describing, so a
        ///     grid that ended one byte out lands on an opcode that does not exist.
        ///
        ///     The client throws on an unrecognised opcode too (Class305_Sub1.java:277).
        /// </remarks>
        /// <exception cref="System.IO.InvalidDataException">
        ///     An unknown opcode, which in practice means the tile grid desynchronised.
        /// </exception>
        private void ParseExtrasTail(JagStream buf) {
            while (buf.Remaining() > 0) {
                int opcode = buf.ReadUnsignedByte();

                switch (opcode) {
                    case 0:
                        //Environment. A bitmask followed by a field per set bit.
                        int mask = buf.ReadUnsignedByte();
                        if ((mask & 0x01) != 0) buf.ReadBytes(4);
                        if ((mask & 0x02) != 0) buf.ReadBytes(2);
                        if ((mask & 0x04) != 0) buf.ReadBytes(2);
                        if ((mask & 0x08) != 0) buf.ReadBytes(2);
                        if ((mask & 0x10) != 0) buf.ReadBytes(6);
                        if ((mask & 0x20) != 0) buf.ReadBytes(4);
                        if ((mask & 0x40) != 0) buf.ReadBytes(2);
                        if ((mask & 0x80) != 0) buf.ReadBytes(12);
                        break;

                    case 1:
                        int lights = buf.ReadUnsignedByte();
                        for (int i = 0; i < lights; i++)
                            SkipPointLight(buf);
                        break;

                    case 2:
                        buf.ReadBytes(3);
                        break;

                    case 128:
                        buf.ReadBytes(10);
                        break;

                    case 129:
                        //Shadow map: one signed kind byte per plane, and only kind 1 carries data.
                        for (int plane = 0; plane < PLANES; plane++) {
                            int kind = (sbyte) buf.ReadUnsignedByte();
                            if (kind == 1)
                                buf.ReadBytes(256);
                        }
                        break;

                    default:
                        throw new System.IO.InvalidDataException(
                            "Unknown terrain extras opcode " + opcode + " in region " + regionID +
                            " at offset " + (buf.Position - 1) +
                            " - the tile grid almost certainly desynchronised");
                }
            }
        }

        /// <summary>Skips one point-light record.</summary>
        private static void SkipPointLight(JagStream buf) {
            buf.ReadBytes(1);      //flags and plane
            buf.ReadBytes(6);      //x, z, y
            int n = buf.ReadUnsignedByte();
            buf.ReadBytes((2 * n + 1) * 2);
            buf.ReadBytes(2);      //colour

            int type = buf.ReadUnsignedByte();

            //Measured: this fires 376 times across the shipped cache, so it is not optional.
            if ((type & 0x1f) == 31)
                buf.ReadBytes(2);
        }

        /// <summary>Decodes the opcode run for a single tile.</summary>
        private void DecodeTile(JagStream buf, int z, int x, int y) {
            while (true) {
                //ReadUnsignedByte throws at EOF. ReadByte returns -1, which would fall into the
                //overlay arm below and spin here forever on a truncated file.
                int attribute = buf.ReadUnsignedByte();

                //Opcodes 0 and 1 are the only ones that end a tile, so this cannot be a switch:
                //a break inside one would bind to the switch rather than to this loop.
                if (attribute == 0) {
                    tileHeights[z, x, y] = z == 0
                        ? -HeightCalc.Calculate(baseX, baseY, x, y) * HEIGHT_UNITS_PER_STEP
                        : tileHeights[z - 1, x, y] - PLANE_HEIGHT_DROP;
                    heightExplicit[z, x, y] = false;
                    return;
                }

                if (attribute == 1) {
                    int height = buf.ReadUnsignedByte();
                    rawHeightByte[z, x, y] = (byte) height;

                    //A stored 1 means zero. Hot: 15.5% of all opcode-1 tiles in the shipped cache.
                    if (height == 1)
                        height = 0;

                    tileHeights[z, x, y] = z == 0
                        ? -height * HEIGHT_UNITS_PER_STEP
                        : tileHeights[z - 1, x, y] - height * HEIGHT_UNITS_PER_STEP;
                    heightExplicit[z, x, y] = true;
                    return;
                }

                if (attribute <= 49) {
                    overlayIds[z, x, y] = buf.ReadUnsignedByte();
                    overlayShapes[z, x, y] = (byte) ((attribute - 2) / 4);

                    //No rotation addend here. The client adds one, but on the static region path
                    //it is a literal 0 - it is the chunk rotation, and only the dynamic/instanced
                    //loader passes a non-zero value. Adding it unconditionally rotates every
                    //overlay in the world.
                    overlayRotations[z, x, y] = (byte) ((attribute - 2) & 3);
                }
                else if (attribute <= 81) {
                    renderRules[z, x, y] = (byte) (attribute - 49);
                }
                else {
                    underlayIds[z, x, y] = attribute - 81;
                }
            }
        }

        /// <summary>
        ///     Decodes a location (<c>l</c> or <c>ul</c>) file.
        /// </summary>
        /// <remarks>
        ///     Delta-encoded throughout. The object-id delta is an <em>extended</em> smart and the
        ///     position delta is a plain one - see
        ///     <see cref="JagStream.ReadExtendedUnsignedSmart"/> for why the difference matters.
        /// </remarks>
        /// <param name="buf">The decompressed, and where necessary decrypted, location file.</param>
        /// <exception cref="System.IO.InvalidDataException">The stream desynchronised.</exception>
        public void LoadLocations(JagStream buf) {
            locations.Clear();

            int start = buf.Position;
            int id = -1;
            int idOffset;

            while ((idOffset = buf.ReadExtendedUnsignedSmart()) != 0) {
                id += idOffset;

                int position = 0;
                int positionOffset;

                while ((positionOffset = buf.ReadUnsignedSmart()) != 0) {
                    position += positionOffset - 1;

                    int attributes = buf.ReadUnsignedByte();
                    int type = attributes >> 2;
                    int orientation = attributes & 0x3;

                    //The plane is not masked - Class305_Sub1.java:432 is a bare >> 12. The
                    //measured maximum position across every readable loc file is 16383, so a mask
                    //could only ever fire on a desynced stream, where it would turn a garbage
                    //plane into a plausible one and hide the fault. These two bounds are the
                    //cheapest detector available for the wrong smart reader.
                    if (position > 16383 || type > 22)
                        throw new System.IO.InvalidDataException(
                            "Loc stream desync in region " + regionID +
                            " (position " + position + ", shape " + type + ")");

                    int localY = position & 0x3F;
                    int localX = position >> 6 & 0x3F;
                    int plane = position >> 12;

                    locations.Add(new Location(
                        id, type, orientation, localX, localY, plane,
                        new Position(baseX + localX, baseY + localY, plane)));
                }
            }

            int end = buf.Position;
            buf.Seek(start);
            RawLocations = buf.ReadBytes(end - start);

            Dirty = false;
        }

        /// <summary>
        ///     Whether anything has been changed since the square was decoded.
        /// </summary>
        /// <remarks>
        ///     A square that is not dirty must be written back as the exact bytes it was read as,
        ///     not re-encoded. Re-encoding an untouched square risks changing bytes the archive CRC
        ///     covers for no reason.
        /// </remarks>
        public bool Dirty { get; private set; }

        /// <summary>Marks the square as modified.</summary>
        public void MarkDirty() => Dirty = true;

        /// <summary>Clears the modified flag, after a successful save.</summary>
        public void ClearDirty() => Dirty = false;

        /// <summary>Sets a vertex height in world units.</summary>
        public void SetTileHeight(int z, int x, int y, int height) {
            tileHeights[z, x, y] = height;

            //An edited height must be stored, not re-derived: the derivation would discard it.
            if (x < WIDTH && y < HEIGHT) {
                heightExplicit[z, x, y] = true;
                heightEdited[z, x, y] = true;
            }

            Dirty = true;
        }

        /// <summary>
        ///     Whether a tile stores its height explicitly rather than leaving it to be derived.
        /// </summary>
        /// <remarks>
        ///     Preserved from the decode so an untouched square re-encodes byte-for-byte. Tiles
        ///     exist whose stored height equals the value opcode 0 would produce, so the two forms
        ///     cannot be told apart after the fact.
        /// </remarks>
        public bool HasExplicitHeight(int z, int x, int y) => heightExplicit[z, x, y];

        /// <summary>The height byte as decoded, meaningful only when the height was not edited.</summary>
        public byte GetRawHeightByte(int z, int x, int y) => rawHeightByte[z, x, y];

        /// <summary>Whether an edit replaced this tile's height since it was decoded.</summary>
        public bool HasEditedHeight(int z, int x, int y) => heightEdited[z, x, y];

        /// <summary>Sets the tile flag byte.</summary>
        public void SetRenderRule(int z, int x, int y, byte flags) {
            renderRules[z, x, y] = flags;
            Dirty = true;
        }

        /// <summary>Sets the floor overlay id, 0 meaning none.</summary>
        public void SetOverlayId(int z, int x, int y, int id) {
            overlayIds[z, x, y] = id;
            Dirty = true;
        }

        /// <summary>Sets the overlay tile shape, 0..11.</summary>
        public void SetOverlayShape(int z, int x, int y, byte shape) {
            overlayShapes[z, x, y] = shape;
            Dirty = true;
        }

        /// <summary>Sets the overlay rotation, 0..3.</summary>
        public void SetOverlayRotation(int z, int x, int y, byte rotation) {
            overlayRotations[z, x, y] = rotation;
            Dirty = true;
        }

        /// <summary>Sets the floor underlay id, 0 meaning none.</summary>
        public void SetUnderlayId(int z, int x, int y, int id) {
            underlayIds[z, x, y] = id;
            Dirty = true;
        }

        /// <summary>Adds a location.</summary>
        /// <param name="location">The location to add.</param>
        public void AddLocation(Location location) {
            locations.Add(location);
            Dirty = true;
        }

        /// <summary>Removes a location.</summary>
        /// <param name="location">The location to remove.</param>
        /// <returns><c>true</c> when it was present.</returns>
        public bool RemoveLocation(Location location) {
            bool removed = locations.Remove(location);
            if (removed)
                Dirty = true;
            return removed;
        }

        /// <summary>Gets the packed region id.</summary>
        public int GetRegionID() => regionID;

        /// <summary>Gets the absolute world X of the square's western edge.</summary>
        public int GetBaseX() => baseX;

        /// <summary>Gets the absolute world Y of the square's southern edge.</summary>
        public int GetBaseY() => baseY;

        /// <summary>Gets a vertex height in world units. Valid to 64 inclusive on both axes.</summary>
        public int GetTileHeight(int z, int x, int y) => tileHeights[z, x, y];

        /// <summary>Gets the tile flag byte written by terrain opcodes 50..81.</summary>
        public byte GetRenderRule(int z, int x, int y) => renderRules[z, x, y];

        /// <summary>Gets the floor overlay id, 0 meaning none.</summary>
        public int GetOverlayId(int z, int x, int y) => overlayIds[z, x, y];

        /// <summary>Gets the overlay tile shape, 0..11.</summary>
        public byte GetOverlayShape(int z, int x, int y) => overlayShapes[z, x, y];

        /// <summary>Gets the overlay rotation, 0..3.</summary>
        public byte GetOverlayRotation(int z, int x, int y) => overlayRotations[z, x, y];

        /// <summary>Gets the floor underlay id, 0 meaning none.</summary>
        public int GetUnderlayId(int z, int x, int y) => underlayIds[z, x, y];

        /// <summary>
        ///     Whether this tile is bridged to the plane below.
        /// </summary>
        /// <remarks>
        ///     The client reads bit 0x2 from plane 1 specifically, not from the caller's plane
        ///     (Node_Sub31_Sub4.method1390:184), and only acts on it above plane 0.
        /// </remarks>
        public bool IsLinkedBelow(int x, int y) =>
            planeCount > 1 && (renderRules[1, x, y] & 0x2) != 0;

        /// <summary>Whether this tile forces rendering at plane 0 (tile flag bit 0x8).</summary>
        public bool IsVisibleBelow(int z, int x, int y) => (GetRenderRule(z, x, y) & 0x8) != 0;

        /// <summary>Gets the decoded locations.</summary>
        public List<Location> GetLocations() => locations;

        /// <summary>The index-5 group name holding this square's locations.</summary>
        public string GetLocationsIdentifier() => MapSquareNames.Locations(regionID);

        /// <summary>The index-5 group name holding this square's terrain.</summary>
        public string GetTerrainIdentifier() => MapSquareNames.Terrain(regionID);
    }
}
