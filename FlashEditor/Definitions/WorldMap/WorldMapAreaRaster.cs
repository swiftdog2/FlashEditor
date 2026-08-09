using System;
using System.Collections.Generic;
using FlashEditor.IO;

namespace FlashEditor.Definitions.WorldMap {
    /// <summary>
    ///     One block of an area's overview raster: either a whole 64x64 map square or a single 8x8
    ///     zone within one.
    /// </summary>
    /// <remarks>
    ///     <c>Class278.method3305</c> (Class278.java:520-544) branches on the leading byte being zero
    ///     and reads a different header and a different tile count for each case. Any non-zero byte
    ///     takes the zone branch, so the byte is kept rather than reduced to a bool - the two caches
    ///     store only 0 and 1, and an encoder that rebuilt it from a bool would rewrite any other
    ///     value the format allows.
    ///     <para>
    ///     Tiles are stored x-major: the outer loop walks the block's x and the inner its y.
    ///     </para>
    /// </remarks>
    public sealed class WorldMapRasterBlock {
        /// <summary>Tiles across a map-square block, in each direction.</summary>
        public const int MapSquareSpan = 64;

        /// <summary>Tiles across a zone block, in each direction.</summary>
        public const int ZoneSpan = 8;

        /// <summary>The block-type byte exactly as stored. Zero selects the map-square shape.</summary>
        public byte BlockType { get; set; }

        /// <summary>The map square's x, in map squares.</summary>
        public byte BlockX { get; set; }

        /// <summary>The map square's y, in map squares.</summary>
        public byte BlockY { get; set; }

        /// <summary>The zone's x within the map square, or 0 for a map-square block.</summary>
        public byte ZoneX { get; set; }

        /// <summary>The zone's y within the map square, or 0 for a map-square block.</summary>
        public byte ZoneY { get; set; }

        /// <summary>The tiles, x-major, <see cref="Span"/> squared of them.</summary>
        public WorldMapTile[] Tiles { get; set; } = Array.Empty<WorldMapTile>();

        /// <summary>Whether this block covers a whole map square rather than one zone.</summary>
        public bool IsMapSquare => BlockType == 0;

        /// <summary>Tiles across this block, in each direction.</summary>
        public int Span => IsMapSquare ? MapSquareSpan : ZoneSpan;

        /// <summary>
        ///     The world x of a tile in this block.
        /// </summary>
        /// <param name="tileIndex">The tile's position in <see cref="Tiles"/>.</param>
        /// <returns>The world x, in tiles.</returns>
        public int WorldXOf(int tileIndex) {
            return BlockX * MapSquareSpan + (IsMapSquare ? 0 : ZoneX * ZoneSpan) + tileIndex / Span;
        }

        /// <summary>
        ///     The world y of a tile in this block.
        /// </summary>
        /// <param name="tileIndex">The tile's position in <see cref="Tiles"/>.</param>
        /// <returns>The world y, in tiles.</returns>
        public int WorldYOf(int tileIndex) {
            return BlockY * MapSquareSpan + (IsMapSquare ? 0 : ZoneY * ZoneSpan) + tileIndex % Span;
        }

        /// <summary>Reads one block and every tile in it.</summary>
        /// <param name="stream">Positioned at the block-type byte.</param>
        /// <returns>The decoded block.</returns>
        public static WorldMapRasterBlock Decode(JagStream stream) {
            var block = new WorldMapRasterBlock();
            block.BlockType = (byte) stream.ReadUnsignedByte();
            block.BlockX = (byte) stream.ReadUnsignedByte();
            block.BlockY = (byte) stream.ReadUnsignedByte();

            if (!block.IsMapSquare) {
                block.ZoneX = (byte) stream.ReadUnsignedByte();
                block.ZoneY = (byte) stream.ReadUnsignedByte();
            }

            int span = block.Span;
            var tiles = new WorldMapTile[span * span];
            for (int i = 0 ; i < tiles.Length ; i++)
                tiles[i] = WorldMapTile.Decode(stream);
            block.Tiles = tiles;

            return block;
        }

        /// <summary>Writes this block back.</summary>
        /// <param name="stream">The stream to append to.</param>
        /// <exception cref="InvalidOperationException">The tile count disagrees with the block type.</exception>
        public void Encode(JagStream stream) {
            int span = Span;
            if (Tiles.Length != span * span)
                throw new InvalidOperationException(
                    "A block of type " + BlockType + " holds " + (span * span) + " tiles, not " +
                    Tiles.Length + ". The count follows from the block type, which is not stored " +
                    "anywhere else in the block.");

            stream.WriteByte(BlockType);
            stream.WriteByte(BlockX);
            stream.WriteByte(BlockY);

            if (!IsMapSquare) {
                stream.WriteByte(ZoneX);
                stream.WriteByte(ZoneY);
            }

            foreach (WorldMapTile tile in Tiles)
                tile.Encode(stream);
        }
    }

    /// <summary>
    ///     An area's overview raster: the <c>area</c> file of that area's index-23 group.
    /// </summary>
    /// <remarks>
    ///     Two floor palettes, then blocks until the buffer runs out - the loop condition is the
    ///     end of the buffer and nothing else (<c>Class278.java:520</c>), so there is no terminator
    ///     and no count. That is why the exact-consumption check on this record is that the decode
    ///     lands on the last byte rather than that it stops at a terminator: appending padding
    ///     changes what the format says the file contains.
    ///     <para>
    ///     This is the bulk of index 23 - just over 6 MB across the 39 areas, one of which is 4.7 MB
    ///     on its own - so nothing here holds more than the stream did.
    ///     </para>
    /// </remarks>
    public sealed class WorldMapAreaRaster {
        /// <summary>
        ///     Floor underlay ids a terrain tile's six-bit code indexes.
        /// </summary>
        /// <remarks>
        ///     Kept as ids rather than resolved definitions. A code of 62 or 63 is reserved, so a
        ///     palette can hold at most 62 entries before its tail becomes unaddressable; the widest
        ///     in this cache is exactly 62.
        /// </remarks>
        public byte[] UnderlayPalette { get; set; } = Array.Empty<byte>();

        /// <summary>Floor overlay ids, indexed the same way when a tile's bit 1 is set.</summary>
        public byte[] OverlayPalette { get; set; } = Array.Empty<byte>();

        /// <summary>The blocks, in the order the file stores them.</summary>
        public List<WorldMapRasterBlock> Blocks { get; } = new List<WorldMapRasterBlock>();

        /// <summary>Reads one area raster.</summary>
        /// <param name="stream">The file, positioned at its first byte.</param>
        /// <returns>This raster.</returns>
        public WorldMapAreaRaster Decode(JagStream stream) {
            if (stream == null)
                throw new ArgumentNullException(nameof(stream));

            UnderlayPalette = ReadPalette(stream);
            OverlayPalette = ReadPalette(stream);

            Blocks.Clear();
            while (stream.Position < stream.Length)
                Blocks.Add(WorldMapRasterBlock.Decode(stream));

            return this;
        }

        /// <summary>Writes this raster back to the file representation.</summary>
        /// <returns>The encoded file, positioned at 0.</returns>
        public JagStream Encode() {
            var stream = new JagStream(EstimateLength());

            WritePalette(stream, UnderlayPalette);
            WritePalette(stream, OverlayPalette);

            foreach (WorldMapRasterBlock block in Blocks)
                block.Encode(stream);

            return stream.Flip();
        }

        /// <summary>
        ///     A starting capacity that avoids re-growing a multi-megabyte buffer from 256 bytes.
        /// </summary>
        /// <remarks>
        ///     One byte per tile is the floor of what the stream can be, so this always
        ///     under-estimates and never over-allocates - it removes most of the doublings rather
        ///     than all of them.
        /// </remarks>
        /// <returns>A lower bound on the encoded length.</returns>
        private int EstimateLength() {
            int tiles = 0;
            foreach (WorldMapRasterBlock block in Blocks)
                tiles += block.Tiles.Length;
            return 2 + UnderlayPalette.Length + OverlayPalette.Length + tiles;
        }

        private static byte[] ReadPalette(JagStream stream) {
            int count = stream.ReadUnsignedByte();
            if (count == 0)
                return Array.Empty<byte>();

            var palette = new byte[count];
            for (int i = 0 ; i < count ; i++)
                palette[i] = (byte) stream.ReadUnsignedByte();
            return palette;
        }

        private static void WritePalette(JagStream stream, byte[] palette) {
            if (palette.Length > byte.MaxValue)
                throw new InvalidOperationException(
                    "A floor palette stores its length in one byte, so it cannot hold " +
                    palette.Length + " entries.");

            stream.WriteByte((byte) palette.Length);
            foreach (byte id in palette)
                stream.WriteByte(id);
        }
    }
}
