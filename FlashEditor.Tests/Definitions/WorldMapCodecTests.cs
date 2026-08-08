using System;
using System.Collections.Generic;
using FlashEditor.Definitions.WorldMap;
using Xunit;

namespace FlashEditor.Tests.Definitions
{
    /// <summary>
    ///     Synthetic world-map records covering the branches no shipped byte exercises.
    /// </summary>
    /// <remarks>
    ///     Everything the two caches do exercise is already pinned by
    ///     <see cref="RealCacheWorldMapTests"/>, which re-encodes all 1043 files and compares them to
    ///     the bytes they came from. What that sweep cannot defend is a rule whose triggering input
    ///     is absent from both caches, and index 23 has three of those:
    ///     <list type="bullet">
    ///     <item>An escape to palette code 63 whose literal the file's palette could also express.
    ///     Both spellings occur in quantity, but never for the same value - so an encoder choosing
    ///     the spelling by looking the value up in the palette sweeps clean today and corrupts the
    ///     first tile edited into the overlap.</item>
    ///     <item>A blank tile with the overlay bit set. Code 62 stops the read before the shape byte
    ///     an overlay tile would otherwise carry, and every blank tile in both caches has the bit
    ///     clear.</item>
    ///     <item>A details record whose dropped eighth byte is not zero. The client reads it and
    ///     throws it away, and it is zero in all 39 records.</item>
    ///     </list>
    ///     <para>
    ///     These are hand-built byte arrays rather than encoder output compared with itself: a codec
    ///     agreeing with itself about the wrong answer is exactly what a round trip against its own
    ///     writer cannot see.
    ///     </para>
    /// </remarks>
    public sealed class WorldMapCodecTests
    {
        /// <summary>Builds a raster from a hand-written palette pair and tile stream.</summary>
        /// <param name="underlay">The underlay palette entries.</param>
        /// <param name="overlay">The overlay palette entries.</param>
        /// <param name="blockBody">The block header and tiles, byte for byte.</param>
        /// <returns>The stored bytes of a one-block raster file.</returns>
        private static byte[] Raster(byte[] underlay, byte[] overlay, params byte[] blockBody)
        {
            var bytes = new List<byte> { (byte) underlay.Length };
            bytes.AddRange(underlay);
            bytes.Add((byte) overlay.Length);
            bytes.AddRange(overlay);
            bytes.AddRange(blockBody);
            return bytes.ToArray();
        }

        /// <summary>A zone block header at 0,0 zone 0,0 - the shortest block that holds tiles.</summary>
        /// <remarks>Type 1 rather than 0 so the block is 8x8 rather than 64x64.</remarks>
        private static readonly byte[] ZoneHeader = { 1, 0, 0, 0, 0 };

        /// <summary>Fills a zone block out to its 64 tiles with blank ones.</summary>
        /// <param name="leading">The tiles to place first, byte for byte.</param>
        /// <returns>The block body.</returns>
        private static byte[] ZoneBlock(params byte[] leading)
        {
            //Palette code 62 in bits 2-7, every other bit clear: one byte, reads nothing more.
            const byte Blank = 62 << 2;

            var body = new List<byte>(ZoneHeader);
            var tiles = new List<byte>(leading);
            body.AddRange(tiles);

            int placed = CountTiles(tiles.ToArray());
            for (int i = placed; i < WorldMapRasterBlock.ZoneSpan * WorldMapRasterBlock.ZoneSpan; i++)
                body.Add(Blank);

            return body.ToArray();
        }

        /// <summary>How many tiles a hand-written run of tile bytes describes.</summary>
        /// <remarks>
        ///     Decoded with the production reader rather than counted by hand, so a test that
        ///     mis-writes a tile fails on the assertion it was written for instead of silently
        ///     producing a block of the wrong length.
        /// </remarks>
        /// <param name="tiles">The tile bytes.</param>
        /// <returns>The number of tiles they hold.</returns>
        private static int CountTiles(byte[] tiles)
        {
            var stream = new JagStream(tiles);
            int count = 0;
            while (stream.Position < stream.Length)
            {
                WorldMapTile.Decode(stream);
                count++;
            }
            return count;
        }

        private static byte[] RoundTrip(byte[] stored, out WorldMapAreaRaster raster)
        {
            var reading = new JagStream(stored);
            raster = new WorldMapAreaRaster().Decode(reading);
            Assert.Equal(stored.Length, reading.Position);
            return raster.Encode().ToArray();
        }

        /// <summary>
        ///     An escaped floor the palette could also express keeps its escape.
        /// </summary>
        /// <remarks>
        ///     The trap this whole design exists for. Palette entry 0 holds floor 7 and the tile
        ///     spells the same floor as code 63 plus a literal 7, so an encoder that picked the
        ///     spelling from the value would write the one-byte inline form and shorten the file.
        ///     Absent from both caches: not one of the 8334 escapes stores a value its own palette
        ///     holds.
        /// </remarks>
        [Fact]
        public void AnEscapedFloorThatThePaletteCouldExpressKeepsItsEscape()
        {
            const byte Escape = 63 << 2;
            byte[] stored = Raster(new byte[] { 7, 9 }, Array.Empty<byte>(),
                ZoneBlock(Escape, 7));

            byte[] written = RoundTrip(stored, out WorldMapAreaRaster raster);

            WorldMapTile tile = raster.Blocks[0].Tiles[0];
            Assert.True(tile.UsesFloorLiteral);
            Assert.Equal(7, tile.StoredFloorLiteral);
            Assert.Equal(7, tile.ResolveFloorId(raster));
            Assert.Equal(stored, written);
        }

        /// <summary>
        ///     The inline spelling of the same floor stays inline.
        /// </summary>
        /// <remarks>
        ///     The other half of the pair. Both tiles decode to floor 7 and they must not converge
        ///     on one spelling, which is what makes the flag byte the record rather than the value.
        /// </remarks>
        [Fact]
        public void AnInlineFloorStaysInlineEvenWhenTheEscapeWouldSayTheSameThing()
        {
            const byte Inline = 0 << 2;
            byte[] stored = Raster(new byte[] { 7, 9 }, Array.Empty<byte>(), ZoneBlock(Inline));

            byte[] written = RoundTrip(stored, out WorldMapAreaRaster raster);

            WorldMapTile tile = raster.Blocks[0].Tiles[0];
            Assert.False(tile.UsesFloorLiteral);
            Assert.Equal(0, tile.PaletteCode);
            Assert.Equal(7, tile.ResolveFloorId(raster));
            Assert.Equal(stored, written);
        }

        /// <summary>
        ///     A blank tile reads nothing more even when its overlay bit is set.
        /// </summary>
        /// <remarks>
        ///     Code 62 returns before the branch that would read an overlay tile's shape byte
        ///     (<c>Class278.java:203-219</c>), so a decoder that tested the overlay bit first would
        ///     swallow the following tile's flag byte and desynchronise the whole block. Every blank
        ///     tile in both caches has the bit clear, so nothing shipped exercises it.
        /// </remarks>
        [Fact]
        public void ABlankTileWithTheOverlayBitSetReadsNoFurtherBytes()
        {
            const byte BlankOverlay = (62 << 2) | WorldMapTile.OverlayFlag;
            const byte Inline = 1 << 2;
            byte[] stored = Raster(new byte[] { 3, 5 }, Array.Empty<byte>(),
                ZoneBlock(BlankOverlay, Inline));

            byte[] written = RoundTrip(stored, out WorldMapAreaRaster raster);

            WorldMapTile blank = raster.Blocks[0].Tiles[0];
            Assert.True(blank.IsBlank);
            Assert.True(blank.IsOverlay);
            Assert.Equal(-1, blank.ResolveFloorId(raster));

            //The tile after it must still be the one that was written, not a byte of the blank's.
            Assert.Equal(5, raster.Blocks[0].Tiles[1].ResolveFloorId(raster));
            Assert.Equal(stored, written);
        }

        /// <summary>
        ///     An overlay tile carries an underlay id, stored signed and read unsigned.
        /// </summary>
        /// <remarks>
        ///     The byte is written signed (<c>Class278.java:217</c>) into the plane the client's
        ///     terrain blender resolves underlay definitions out of, so the stored form and the read
        ///     form differ above 127 and both have to be right: the stored byte is what round-trips
        ///     and the unsigned reading is what names a floor. 0x80 is the boundary case - it
        ///     round-trips as -128 and means underlay 128.
        /// </remarks>
        [Fact]
        public void AnOverlayTileKeepsItsSignedUnderlayByteAndReadsItUnsigned()
        {
            const byte Overlay = (2 << 2) | WorldMapTile.OverlayFlag;
            byte[] stored = Raster(Array.Empty<byte>(), new byte[] { 11, 12, 13 },
                ZoneBlock(Overlay, 0x80));

            byte[] written = RoundTrip(stored, out WorldMapAreaRaster raster);

            WorldMapTile tile = raster.Blocks[0].Tiles[0];
            Assert.Equal(13, tile.ResolveFloorId(raster));
            Assert.Equal(-128, tile.StoredUnderlayByte);
            Assert.Equal(128, tile.UnderlayBeneathOverlay);
            Assert.Equal(stored, written);
        }

        /// <summary>
        ///     A decorated tile keeps its element-count flag even when it carries no elements.
        /// </summary>
        /// <remarks>
        ///     The flag is the only statement that a count byte is present, and a count of zero is
        ///     legal - 18,383 levels in the cache are exactly that - so an encoder that derived the
        ///     flag from the element list would drop a byte per level.
        /// </remarks>
        [Fact]
        public void ADecoratedTileKeepsAnElementCountFlagWithNoElements()
        {
            const byte Decorated = WorldMapTile.DecoratedFlag | WorldMapTile.CarriesElementCountFlag;
            byte[] stored = Raster(Array.Empty<byte>(), Array.Empty<byte>(),
                ZoneBlock(Decorated, 4, 0));

            byte[] written = RoundTrip(stored, out WorldMapAreaRaster raster);

            WorldMapTile tile = raster.Blocks[0].Tiles[0];
            Assert.True(tile.CarriesElementCount);
            Assert.Equal(1, tile.LevelCount);
            Assert.Equal(4, tile.Levels[0].UnderlayId);
            Assert.Empty(tile.Levels[0].Elements);
            Assert.Equal(stored, written);
        }

        /// <summary>
        ///     A decorated tile keeps its overlay pair even when both bytes are zero.
        /// </summary>
        /// <remarks>
        ///     Same shape as the element-count flag: two stored zeroes decode to exactly what an
        ///     absent pair would, and 17,883 levels in the cache store them.
        /// </remarks>
        [Fact]
        public void ADecoratedTileKeepsAZeroOverlayPair()
        {
            const byte Decorated = WorldMapTile.DecoratedFlag | WorldMapTile.CarriesOverlayFlag;
            byte[] stored = Raster(Array.Empty<byte>(), Array.Empty<byte>(),
                ZoneBlock(Decorated, 6, 0, 0));

            byte[] written = RoundTrip(stored, out WorldMapAreaRaster raster);

            WorldMapTile tile = raster.Blocks[0].Tiles[0];
            Assert.True(tile.CarriesOverlay);
            Assert.Equal(0, tile.Levels[0].OverlayId);
            Assert.Equal(0, tile.Levels[0].ShapeAndRotation);
            Assert.Equal(stored, written);
        }

        /// <summary>
        ///     A decorated tile's elements keep their order, ids and packed attribute byte.
        /// </summary>
        [Fact]
        public void ADecoratedTileKeepsItsElementsInOrder()
        {
            const byte Decorated = WorldMapTile.DecoratedFlag | WorldMapTile.CarriesElementCountFlag;
            byte[] stored = Raster(Array.Empty<byte>(), Array.Empty<byte>(),
                ZoneBlock(Decorated, 8, 2, 0xDF, 0xAA, 0x4A, 0x00, 0x03, 0x80));

            byte[] written = RoundTrip(stored, out WorldMapAreaRaster raster);

            WorldMapTileElement[] elements = raster.Blocks[0].Tiles[0].Levels[0].Elements;
            Assert.Equal(2, elements.Length);
            Assert.Equal(0xDFAA, elements[0].ObjectId);
            Assert.Equal(0x0A, elements[0].Shape);
            Assert.Equal(1, elements[0].Rotation);
            Assert.Equal(3, elements[1].ObjectId);
            Assert.Equal(0, elements[1].Shape);
            Assert.Equal(2, elements[1].Rotation);
            Assert.Equal(stored, written);
        }

        /// <summary>
        ///     Unknown high bits of a decorated tile's flag byte survive a round trip.
        /// </summary>
        /// <remarks>
        ///     Bits 5, 6 and 7 have no reader in the 637 client and are clear on all 304,940
        ///     decorated tiles here. Keeping the byte whole rather than rebuilding it from the four
        ///     known fields is what stops a future cache losing them on the first save, and is the
        ///     same rule the reference table's archive-flags byte follows.
        /// </remarks>
        [Fact]
        public void UnknownFlagBitsOnADecoratedTileAreWrittenBackWhole()
        {
            const byte Decorated = WorldMapTile.DecoratedFlag | 0xE0;
            byte[] stored = Raster(Array.Empty<byte>(), Array.Empty<byte>(), ZoneBlock(Decorated, 12));

            byte[] written = RoundTrip(stored, out WorldMapAreaRaster raster);

            Assert.Equal(Decorated, raster.Blocks[0].Tiles[0].Flags);
            Assert.Equal(stored, written);
        }

        /// <summary>
        ///     A map-square block holds 64x64 tiles and a zone block 8x8, from the type byte alone.
        /// </summary>
        [Fact]
        public void TheBlockTypeByteDecidesTheTileCount()
        {
            const byte Blank = 62 << 2;

            var square = new List<byte> { 0, 40, 50 };
            for (int i = 0; i < WorldMapRasterBlock.MapSquareSpan * WorldMapRasterBlock.MapSquareSpan; i++)
                square.Add(Blank);

            byte[] stored = Raster(Array.Empty<byte>(), Array.Empty<byte>(), square.ToArray());
            byte[] written = RoundTrip(stored, out WorldMapAreaRaster raster);

            WorldMapRasterBlock block = raster.Blocks[0];
            Assert.True(block.IsMapSquare);
            Assert.Equal(WorldMapRasterBlock.MapSquareSpan * WorldMapRasterBlock.MapSquareSpan,
                block.Tiles.Length);
            Assert.Equal(40 * 64, block.WorldXOf(0));
            Assert.Equal(50 * 64 + 3, block.WorldYOf(3));
            Assert.Equal(stored, written);
        }

        /// <summary>
        ///     A block-type byte other than 0 or 1 still takes the zone branch and is written back.
        /// </summary>
        /// <remarks>
        ///     The client's test is <c>readUnsignedByte() == 0</c>, so every other value means the
        ///     same thing to it. Only 0 and 1 occur here, so a codec that stored a bool would sweep
        ///     clean and rewrite anything else as 1.
        /// </remarks>
        [Fact]
        public void ABlockTypeOtherThanOneStillReadsAsAZone()
        {
            byte[] stored = Raster(Array.Empty<byte>(), Array.Empty<byte>(),
                Concat(new byte[] { 7, 2, 3, 4, 5 }, TilesAfterHeader(ZoneBlock())));

            byte[] written = RoundTrip(stored, out WorldMapAreaRaster raster);

            WorldMapRasterBlock block = raster.Blocks[0];
            Assert.Equal(7, block.BlockType);
            Assert.False(block.IsMapSquare);
            Assert.Equal(WorldMapRasterBlock.ZoneSpan * WorldMapRasterBlock.ZoneSpan, block.Tiles.Length);
            Assert.Equal(stored, written);
        }

        /// <summary>
        ///     An area details record keeps the byte the client reads and discards.
        /// </summary>
        /// <remarks>
        ///     It is zero in all 39 records of both caches, so the byte-identity sweep cannot tell a
        ///     decoder that keeps it from one that writes a constant zero.
        /// </remarks>
        [Fact]
        public void AnAreaKeepsTheByteTheClientDiscards()
        {
            byte[] stored = Details("main", "RuneScape Surface", 0x00CA00CA, -1, 1, 75, 0x5A);

            var reading = new JagStream(stored);
            var area = new WorldMapAreaDefinition { Id = 3 }.Decode(reading);

            Assert.Equal(stored.Length, reading.Position);
            Assert.Equal(0x5A, area.UnreadByte);
            Assert.Equal(stored, area.Encode().ToArray());
        }

        /// <summary>
        ///     An area's stored zoom of 255 is kept, while the zoom the client uses reads as zero.
        /// </summary>
        /// <remarks>
        ///     Aliased exactly like the terrain height byte on index 5: two stored values decode to
        ///     the same zoom, so the stored one cannot be recomputed. One area in this cache stores
        ///     it, but only one - hence the pin here as well.
        /// </remarks>
        [Fact]
        public void AnAreaKeepsAZoomOfTwoHundredAndFiftyFive()
        {
            byte[] stored = Details("null", "Loading...", 0x00180018, 0, 0,
                WorldMapAreaDefinition.ZoomStoredAsZero, 0);

            var reading = new JagStream(stored);
            var area = new WorldMapAreaDefinition { Id = 10 }.Decode(reading);

            Assert.Equal(WorldMapAreaDefinition.ZoomStoredAsZero, area.StoredZoom);
            Assert.Equal(0, area.Zoom);
            Assert.False(area.Enabled);
            Assert.Equal(stored, area.Encode().ToArray());
        }

        /// <summary>
        ///     An area's zones keep their source and destination rectangles the right way round.
        /// </summary>
        /// <remarks>
        ///     Two rectangles of four shorts each, in one flat run. Swapping a source bound for the
        ///     destination bound at the same offset still decodes, still round-trips, and puts every
        ///     icon in the wrong place - so the pairing is asserted against distinct values rather
        ///     than left to the byte comparison.
        /// </remarks>
        [Fact]
        public void AZoneKeepsItsSourceAndDestinationRectanglesApart()
        {
            byte[] stored = Details("zanaris", "Zanaris", 0, 0, 1, 100, 0,
                new byte[] { 2, 0x11, 0x11, 0x22, 0x22, 0x33, 0x33, 0x44, 0x44,
                             0x55, 0x55, 0x66, 0x66, 0x77, 0x77, 0x88, 0x88 },
                new byte[] { 0, 0x01, 0x02, 0x03, 0x04, 0x05, 0x06, 0x07, 0x08,
                             0x09, 0x0A, 0x0B, 0x0C, 0x0D, 0x0E, 0x0F, 0x10 });

            var reading = new JagStream(stored);
            var area = new WorldMapAreaDefinition { Id = 1 }.Decode(reading);

            Assert.Equal(stored.Length, reading.Position);
            Assert.Equal(2, area.Zones.Count);

            WorldMapZone zone = area.Zones[0];
            Assert.Equal(2, zone.Plane);
            Assert.Equal(0x1111, zone.SourceMinX);
            Assert.Equal(0x2222, zone.SourceMinY);
            Assert.Equal(0x3333, zone.SourceMaxX);
            Assert.Equal(0x4444, zone.SourceMaxY);
            Assert.Equal(0x5555, zone.DestinationMinX);
            Assert.Equal(0x6666, zone.DestinationMinY);
            Assert.Equal(0x7777, zone.DestinationMaxX);
            Assert.Equal(0x8888, zone.DestinationMaxY);

            Assert.Equal(stored, area.Encode().ToArray());
        }

        /// <summary>A static element unpacks its position the way the client does.</summary>
        [Fact]
        public void AStaticElementUnpacksItsPackedPosition()
        {
            const int Plane = 2;
            const int X = 3232;
            const int Y = 9694;
            int packed = (Plane << 28) | (X << 14) | Y;

            var stored = new List<byte>
            {
                (byte) (packed >> 24), (byte) (packed >> 16), (byte) (packed >> 8), (byte) packed,
                0x04, 0x0D,
                1
            };

            var reading = new JagStream(stored.ToArray());
            var element = new WorldMapElement { Id = 5 }.Decode(reading);

            Assert.Equal(stored.Count, reading.Position);
            Assert.Equal(Plane, element.Plane);
            Assert.Equal(X, element.X);
            Assert.Equal(Y, element.Y);
            Assert.Equal(0x040D, element.MapElementId);
            Assert.True(element.HiddenOnFreeWorlds);
            Assert.Equal(stored.ToArray(), element.Encode().ToArray());
        }

        /// <summary>
        ///     The encoder refuses to write a tile whose payload the flag byte cannot express.
        /// </summary>
        /// <remarks>
        ///     Everything about a tile's shape lives in its flag byte, so an editor that added a
        ///     level or an element without touching that byte would otherwise have its change
        ///     silently dropped on save and reported as successful.
        /// </remarks>
        [Fact]
        public void TheEncoderRefusesAPayloadTheFlagByteCannotExpress()
        {
            var stream = new JagStream();

            var tooManyLevels = new WorldMapTile(WorldMapTile.DecoratedFlag, 0, 0, new[]
            {
                new WorldMapTileLevel(1, 0, 0, Array.Empty<WorldMapTileElement>()),
                new WorldMapTileLevel(2, 0, 0, Array.Empty<WorldMapTileElement>())
            });
            Assert.Throws<InvalidOperationException>(() => tooManyLevels.Encode(stream));

            var unwritableElements = new WorldMapTile(WorldMapTile.DecoratedFlag, 0, 0, new[]
            {
                new WorldMapTileLevel(1, 0, 0, new[] { new WorldMapTileElement(4, 0) })
            });
            Assert.Throws<InvalidOperationException>(() => unwritableElements.Encode(stream));

            var terrainWithLevels = new WorldMapTile(0, 0, 0, new[]
            {
                new WorldMapTileLevel(1, 0, 0, Array.Empty<WorldMapTileElement>())
            });
            Assert.Throws<InvalidOperationException>(() => terrainWithLevels.Encode(stream));
        }

        /// <summary>Builds a details record from its fields.</summary>
        /// <param name="internalName">The area's internal name.</param>
        /// <param name="displayName">The name shown to the player.</param>
        /// <param name="packedOrigin">The packed origin.</param>
        /// <param name="tint">The tint colour.</param>
        /// <param name="enabled">The enabled byte.</param>
        /// <param name="zoom">The zoom byte.</param>
        /// <param name="dropped">The byte the client reads and discards.</param>
        /// <param name="zones">Zone records, each already laid out as 17 bytes.</param>
        /// <returns>The stored bytes.</returns>
        private static byte[] Details(string internalName, string displayName, int packedOrigin, int tint,
            byte enabled, byte zoom, byte dropped, params byte[][] zones)
        {
            var bytes = new List<byte>();
            foreach (char c in internalName)
                bytes.Add((byte) c);
            bytes.Add(0);
            foreach (char c in displayName)
                bytes.Add((byte) c);
            bytes.Add(0);

            bytes.AddRange(BigEndian(packedOrigin));
            bytes.AddRange(BigEndian(tint));
            bytes.Add(enabled);
            bytes.Add(zoom);
            bytes.Add(dropped);
            bytes.Add((byte) zones.Length);

            foreach (byte[] zone in zones)
            {
                Assert.Equal(17, zone.Length);
                bytes.AddRange(zone);
            }

            return bytes.ToArray();
        }

        private static byte[] BigEndian(int value)
        {
            return new[] { (byte) (value >> 24), (byte) (value >> 16), (byte) (value >> 8), (byte) value };
        }

        private static byte[] Concat(byte[] first, byte[] second)
        {
            var joined = new byte[first.Length + second.Length];
            Array.Copy(first, joined, first.Length);
            Array.Copy(second, 0, joined, first.Length, second.Length);
            return joined;
        }

        /// <summary>Strips a zone block's five header bytes, leaving its tiles.</summary>
        /// <param name="block">The block body.</param>
        /// <returns>The tile bytes.</returns>
        private static byte[] TilesAfterHeader(byte[] block)
        {
            var tiles = new byte[block.Length - ZoneHeader.Length];
            Array.Copy(block, ZoneHeader.Length, tiles, 0, tiles.Length);
            return tiles;
        }
    }
}
