using System;
using FlashEditor.IO;

namespace FlashEditor.Definitions.WorldMap {
    /// <summary>
    ///     One map element placed on a decorated tile, as the tile stream stores it.
    /// </summary>
    /// <remarks>
    ///     <b><see cref="ObjectId"/> is an object definition id, not a map element id.</b> The client
    ///     resolves it through <c>Class302.method3546</c> (Class302.java:84, the object provider) and
    ///     then reads the object's own icon fields - <c>anInt2990</c>, our opcode 102 map scene icon,
    ///     at <c>Class278.java:871</c>, and <c>anInt2958</c>, our opcode 107 map element, at
    ///     <c>:84</c>. Measured over the whole index: all 5255 distinct ids stored here are declared
    ///     object ids, and 5148 of them are outside config group 36 entirely, so reading them as map
    ///     elements resolves 2% of them and silently mis-draws the rest.
    /// </remarks>
    public readonly struct WorldMapTileElement {
        /// <summary>Mask isolating the shape from <see cref="Attributes"/>.</summary>
        public const int ShapeMask = 0x3F;

        /// <summary>How far to shift <see cref="Attributes"/> to reach the rotation.</summary>
        public const int RotationShift = 6;

        /// <summary>Mask isolating the rotation once shifted.</summary>
        public const int RotationMask = 0x3;

        /// <summary>Binds an element reference to the attribute byte stored with it.</summary>
        /// <param name="objectId">The object definition id.</param>
        /// <param name="attributes">The packed shape and rotation byte.</param>
        public WorldMapTileElement(int objectId, sbyte attributes) {
            ObjectId = objectId;
            Attributes = attributes;
        }

        /// <summary>The object definition whose icon fields say what is drawn.</summary>
        public int ObjectId { get; }

        /// <summary>
        ///     Shape in the low six bits, rotation in bits 6 and 7, exactly as stored.
        /// </summary>
        /// <remarks>
        ///     Kept as one signed byte rather than split into two properties because that is the
        ///     unit the client masks - <c>Class278.java:912,939,947</c> - and because the byte is
        ///     genuinely signed on the wire (<c>readSignedByte</c>), so splitting it into unsigned
        ///     halves and reassembling would have to reproduce the sign extension exactly.
        /// </remarks>
        public sbyte Attributes { get; }

        /// <summary>Which of the wall, corner and floor shapes the icon is drawn as.</summary>
        public int Shape => Attributes & ShapeMask;

        /// <summary>The icon's rotation, 0-3.</summary>
        public int Rotation => (Attributes >> RotationShift) & RotationMask;
    }

    /// <summary>
    ///     One plane's worth of a decorated tile.
    /// </summary>
    /// <remarks>
    ///     Level 0 is the tile itself and lands in the base underlay, overlay and shape arrays;
    ///     levels 1 and above become the per-plane <c>Particle_Sub8</c> entries at
    ///     <c>Class278.java:269-272</c>. The floor ids here are <b>raw</b>, not palette indices - the
    ///     decoration branch never touches the two palettes the terrain branch reads through, which
    ///     is why <see cref="WorldMapTile"/> keeps the two forms apart rather than normalising them.
    /// </remarks>
    public readonly struct WorldMapTileLevel {
        /// <summary>Binds the three floor bytes to the elements stored beside them.</summary>
        /// <param name="underlayId">The stored underlay byte.</param>
        /// <param name="overlayId">The stored overlay byte, or 0 when the tile does not carry one.</param>
        /// <param name="shapeAndRotation">The stored shape byte, or 0 when the tile does not carry one.</param>
        /// <param name="elements">The elements on this level, empty when there are none.</param>
        public WorldMapTileLevel(byte underlayId, byte overlayId, byte shapeAndRotation,
            WorldMapTileElement[] elements) {
            UnderlayId = underlayId;
            OverlayId = overlayId;
            ShapeAndRotation = shapeAndRotation;
            Elements = elements ?? throw new ArgumentNullException(nameof(elements));
        }

        /// <summary>
        ///     The floor underlay, offset by one so that 0 means none.
        /// </summary>
        /// <remarks>
        ///     Settled from use: <c>Class278.java:636</c> fetches the definition as
        ///     <c>method2483(value - 1)</c> and only when the value is above zero.
        /// </remarks>
        public byte UnderlayId { get; }

        /// <summary>
        ///     The floor overlay, offset by one so that 0 means none.
        /// </summary>
        /// <remarks>
        ///     Same offset, from <c>Class278.java:914</c> indexing a colour table built one slot
        ///     higher than the overlay count (<c>:157,498</c>). Zero when the tile's flag byte does
        ///     not carry the pair.
        /// </remarks>
        public byte OverlayId { get; }

        /// <summary>Shape in the low six bits, rotation in bits 6 and 7.</summary>
        public byte ShapeAndRotation { get; }

        /// <summary>The elements drawn on this level. Never null; empty when there are none.</summary>
        public WorldMapTileElement[] Elements { get; }
    }

    /// <summary>
    ///     One tile of an area's overview raster, kept in the spelling the packer chose.
    /// </summary>
    /// <remarks>
    ///     Decoded from <c>Class278.method3300</c> (Class278.java:196-274). The flag byte selects
    ///     between two entirely different payloads and, within the terrain payload, between three
    ///     ways of naming a floor - so it is stored verbatim and every re-encode is driven from it
    ///     rather than from the decoded values.
    ///     <para>
    ///     <b>That is not defensive; the flag genuinely is not derivable.</b> Both spellings of a
    ///     palette entry occur in this cache - 2,334,797 tiles name their floor by a six-bit palette
    ///     index and 8,334 escape to code 63 and store the floor id as a literal byte - and no rule
    ///     over the decoded value tells them apart, because the two forms carry different numbers:
    ///     an inline code indexes the palette while a literal is the floor id itself.
    ///     </para>
    ///     <para>
    ///     <b>And the case that would settle it is absent from both caches.</b> Not one of the 8,334
    ///     escapes stores a value that also appears in its file's palette, so an encoder that chose
    ///     the escape only for floors the palette cannot express would sweep clean today and corrupt
    ///     the first tile anyone edited into the overlap. The same shape appears twice more: the
    ///     element-count flag is set with a count of zero on 18,383 levels and the overlay pair is
    ///     carried as two zero bytes on 17,883, so neither flag follows from the values either.
    ///     </para>
    /// </remarks>
    public readonly struct WorldMapTile {
        /// <summary>Bit 0: the tile carries per-plane decoration rather than a single floor.</summary>
        public const int DecoratedFlag = 0x1;

        /// <summary>Bit 1 of a terrain tile: the palette code names an overlay rather than an underlay.</summary>
        public const int OverlayFlag = 0x2;

        /// <summary>Bit 3 of a decorated tile: each level carries an overlay id and a shape byte.</summary>
        public const int CarriesOverlayFlag = 0x8;

        /// <summary>Bit 4 of a decorated tile: each level carries an element count.</summary>
        public const int CarriesElementCountFlag = 0x10;

        /// <summary>How far to shift a terrain flag byte to reach its palette code.</summary>
        public const int PaletteCodeShift = 2;

        /// <summary>Mask isolating the palette code once shifted.</summary>
        public const int PaletteCodeMask = 0x3F;

        /// <summary>The palette code meaning "nothing here", which reads no further bytes.</summary>
        public const int BlankPaletteCode = 62;

        /// <summary>The palette code meaning "the floor id follows as a literal byte".</summary>
        public const int LiteralPaletteCode = 63;

        /// <summary>Binds a tile's stored flag byte to the payload that followed it.</summary>
        /// <param name="flags">The flag byte, verbatim.</param>
        /// <param name="storedFloorLiteral">The byte following palette code 63, else 0.</param>
        /// <param name="storedUnderlayByte">The signed byte following an overlay terrain tile, else 0.</param>
        /// <param name="levels">The decoration levels, or null for a terrain tile.</param>
        public WorldMapTile(byte flags, byte storedFloorLiteral, sbyte storedUnderlayByte, WorldMapTileLevel[]? levels) {
            Flags = flags;
            StoredFloorLiteral = storedFloorLiteral;
            StoredUnderlayByte = storedUnderlayByte;
            Levels = levels;
        }

        /// <summary>The flag byte exactly as stored. Everything else about the tile follows from it.</summary>
        public byte Flags { get; }

        /// <summary>
        ///     The floor id stored inline after palette code 63, or 0 when the tile does not use one.
        /// </summary>
        public byte StoredFloorLiteral { get; }

        /// <summary>
        ///     The byte that follows an overlay terrain tile, or 0 when the tile is not one.
        /// </summary>
        /// <remarks>
        ///     Signed on the wire (<c>Class278.java:217</c>) and stored verbatim, because the value
        ///     is what round-trips; <see cref="UnderlayBeneathOverlay"/> is the reading of it.
        /// </remarks>
        public sbyte StoredUnderlayByte { get; }

        /// <summary>
        ///     The floor <b>underlay</b> drawn beneath this tile's overlay, one-based, 0 for none.
        /// </summary>
        /// <remarks>
        ///     <b>This was called <c>OverlayShape</c> and it is not a shape.</b> The client writes it
        ///     into the same plane its terrain blender reads underlay ids out of -
        ///     <c>Class278.java:217</c> assigns it to <c>aByteArray2081</c>, which
        ///     <c>method3310</c> (<c>:634-636</c>) resolves with <c>method2483(value - 1)</c> to a
        ///     <c>FloorUnderlay</c> - and the same branch sets the shape plane
        ///     <c>aByteArray2073</c> to a literal <c>0</c> two lines earlier. A shape it is not,
        ///     because the shape is written beside it and is zero.
        ///     <para>
        ///     Settled a third way, through neither the client nor this decoder: the stored byte read
        ///     unsigned spans <b>0 to 150 across 88 distinct values</b> over the whole index, against
        ///     <b>159</b> declared floor underlays, so every value is a live underlay id and none is
        ///     in the range a packed shape and rotation would need. Pinned by
        ///     <c>RealCacheWorldMapRasterTests.TheByteOnAnOverlayTerrainTileIsAnUnderlayId</c>.
        ///     </para>
        ///     <para>
        ///     The name mattered: a renderer that treated it as a shape drew no ground colour at all
        ///     under any overlay tile, and no byte-identity sweep would have noticed, because the
        ///     byte round-trips whatever it is called.
        ///     </para>
        /// </remarks>
        public int UnderlayBeneathOverlay => StoredUnderlayByte & 0xFF;

        /// <summary>The decoration levels, or null when this is a terrain tile.</summary>
        public WorldMapTileLevel[]? Levels { get; }

        /// <summary>Whether this tile carries per-plane decoration.</summary>
        public bool IsDecorated => (Flags & DecoratedFlag) != 0;

        /// <summary>Whether a terrain tile's palette code names an overlay rather than an underlay.</summary>
        public bool IsOverlay => (Flags & OverlayFlag) != 0;

        /// <summary>A terrain tile's six-bit palette code.</summary>
        public int PaletteCode => (Flags >> PaletteCodeShift) & PaletteCodeMask;

        /// <summary>Whether this terrain tile names no floor at all.</summary>
        public bool IsBlank => !IsDecorated && PaletteCode == BlankPaletteCode;

        /// <summary>Whether this terrain tile escapes the palette and stores a literal floor id.</summary>
        public bool UsesFloorLiteral => !IsDecorated && PaletteCode == LiteralPaletteCode;

        /// <summary>How many planes a decorated tile describes, as its flag byte states.</summary>
        public int LevelCount => IsDecorated ? ((Flags >> 1) & 0x3) + 1 : 0;

        /// <summary>Whether each decoration level carries an overlay id and shape byte.</summary>
        public bool CarriesOverlay => (Flags & CarriesOverlayFlag) != 0;

        /// <summary>Whether each decoration level carries an element count.</summary>
        public bool CarriesElementCount => (Flags & CarriesElementCountFlag) != 0;

        /// <summary>
        ///     The floor this terrain tile names, or -1 when it names none.
        /// </summary>
        /// <remarks>
        ///     The resolution the client performs at <c>Class278.java:203-210</c>: code 62 is
        ///     nothing, code 63 is the literal that follows, and anything else indexes the underlay
        ///     or overlay palette depending on bit 1.
        /// </remarks>
        /// <param name="raster">The raster this tile belongs to, for its two palettes.</param>
        /// <returns>The floor id, or -1.</returns>
        public int ResolveFloorId(WorldMapAreaRaster raster) {
            if (raster == null)
                throw new ArgumentNullException(nameof(raster));
            if (IsDecorated || IsBlank)
                return -1;
            if (UsesFloorLiteral)
                return StoredFloorLiteral;

            byte[] palette = IsOverlay ? raster.OverlayPalette : raster.UnderlayPalette;
            int code = PaletteCode;
            return code < palette.Length ? palette[code] : -1;
        }

        /// <summary>Reads one tile.</summary>
        /// <param name="stream">Positioned at the tile's flag byte.</param>
        /// <returns>The decoded tile.</returns>
        public static WorldMapTile Decode(JagStream stream) {
            byte flags = (byte) stream.ReadUnsignedByte();

            if ((flags & DecoratedFlag) == 0) {
                int code = (flags >> PaletteCodeShift) & PaletteCodeMask;
                byte literal = 0;
                sbyte shape = 0;

                //Code 62 is the whole tile: the client reads nothing more, not even the shape byte
                //an overlay tile would otherwise carry.
                if (code != BlankPaletteCode) {
                    if (code == LiteralPaletteCode)
                        literal = (byte) stream.ReadUnsignedByte();
                    if ((flags & OverlayFlag) != 0)
                        shape = stream.ReadSignedByte();
                }

                return new WorldMapTile(flags, literal, shape, null);
            }

            int levelCount = ((flags >> 1) & 0x3) + 1;
            bool carriesOverlay = (flags & CarriesOverlayFlag) != 0;
            bool carriesCount = (flags & CarriesElementCountFlag) != 0;
            var levels = new WorldMapTileLevel[levelCount];

            for (int level = 0 ; level < levelCount ; level++) {
                byte underlay = (byte) stream.ReadUnsignedByte();
                byte overlay = 0;
                byte shapeAndRotation = 0;

                if (carriesOverlay) {
                    overlay = (byte) stream.ReadUnsignedByte();
                    shapeAndRotation = (byte) stream.ReadUnsignedByte();
                }

                int count = carriesCount ? stream.ReadUnsignedByte() : 0;
                WorldMapTileElement[] elements = count == 0
                    ? Array.Empty<WorldMapTileElement>()
                    : new WorldMapTileElement[count];

                for (int i = 0 ; i < count ; i++) {
                    int objectId = stream.ReadUnsignedShort();
                    sbyte attributes = stream.ReadSignedByte();
                    elements[i] = new WorldMapTileElement(objectId, attributes);
                }

                levels[level] = new WorldMapTileLevel(underlay, overlay, shapeAndRotation, elements);
            }

            return new WorldMapTile(flags, 0, 0, levels);
        }

        /// <summary>Writes this tile back, in the spelling its flag byte states.</summary>
        /// <param name="stream">The stream to append to.</param>
        /// <exception cref="InvalidOperationException">
        ///     The payload disagrees with the flag byte, so writing it would either drop data or
        ///     produce a stream that decodes differently.
        /// </exception>
        public void Encode(JagStream stream) {
            stream.WriteByte(Flags);

            if (!IsDecorated) {
                if (Levels != null)
                    throw new InvalidOperationException(
                        "Tile flags 0x" + Flags.ToString("X2") + " describe a terrain tile but it " +
                        "carries decoration levels, which the encoder would silently drop.");

                int code = PaletteCode;
                if (code == BlankPaletteCode)
                    return;
                if (code == LiteralPaletteCode)
                    stream.WriteByte(StoredFloorLiteral);
                if (IsOverlay)
                    stream.WriteSignedByte(StoredUnderlayByte);
                return;
            }

            WorldMapTileLevel[] levels = Levels ?? throw new InvalidOperationException(
                "Tile flags 0x" + Flags.ToString("X2") + " describe a decorated tile with no levels.");

            if (levels.Length != LevelCount)
                throw new InvalidOperationException(
                    "Tile flags 0x" + Flags.ToString("X2") + " state " + LevelCount +
                    " levels but the tile holds " + levels.Length +
                    ". The level count lives in the flag byte, so change that rather than the list.");

            foreach (WorldMapTileLevel level in levels) {
                stream.WriteByte(level.UnderlayId);

                if (CarriesOverlay) {
                    stream.WriteByte(level.OverlayId);
                    stream.WriteByte(level.ShapeAndRotation);
                }

                if (!CarriesElementCount) {
                    //The flag is the only place a count can be written, so elements added without
                    //setting it would vanish on save with nothing reporting it.
                    if (level.Elements.Length > 0)
                        throw new InvalidOperationException(
                            "Tile flags 0x" + Flags.ToString("X2") + " carry no element count, so the " +
                            level.Elements.Length + " elements on this level cannot be written.");
                    continue;
                }

                if (level.Elements.Length > byte.MaxValue)
                    throw new InvalidOperationException(
                        "A tile level stores its element count in one byte, so it cannot hold " +
                        level.Elements.Length + " elements.");

                stream.WriteByte((byte) level.Elements.Length);
                foreach (WorldMapTileElement element in level.Elements) {
                    stream.WriteShort(element.ObjectId);
                    stream.WriteSignedByte(element.Attributes);
                }
            }
        }
    }
}
