using System;
using System.Collections.Generic;
using System.IO;
using FlashEditor.cache;
using FlashEditor.Cache.Region;
using Xunit;

namespace FlashEditor.Tests.Map
{
    /// <summary>
    ///     The index-5 <c>n</c> codec, against hand-written bytes rather than against itself.
    /// </summary>
    /// <remarks>
    ///     Round-tripping this encoder against this decoder proves nothing - two real defects in
    ///     this project have survived exactly that - so the bytes here are written out by hand from
    ///     the client's reader (<c>Particle_Sub3_Sub2.method3005</c>) and the decoded fields are
    ///     asserted against what that reader would produce.
    /// </remarks>
    public sealed class NpcSpawnCodecTests
    {
        /// <summary>The packed word splits into plane, local X and local Y the way the client does.</summary>
        /// <remarks>
        ///     <c>plane = p &gt;&gt; 14</c>, <c>localX = (p &gt;&gt; 7) &amp; 0x3f</c>,
        ///     <c>localY = p &amp; 0x3f</c>. Chosen so that every field is a different value and a
        ///     swapped pair cannot pass: plane 2, X 41, Y 19.
        /// </remarks>
        [Fact]
        public void APackedWordSplitsIntoPlaneAndLocalCoordinates()
        {
            int packed = (2 << 14) | (41 << 7) | 19;

            List<NpcSpawn> spawns = RegionCodec.DecodeNpcSpawns(
                new JagStream(new byte[] { (byte) (packed >> 8), (byte) packed, 0x30, 0x39 }));

            NpcSpawn spawn = Assert.Single(spawns);
            Assert.Equal(2, spawn.Plane);
            Assert.Equal(41, spawn.LocalX);
            Assert.Equal(19, spawn.LocalY);
            Assert.Equal(0x3039, spawn.NpcId);
        }

        /// <summary>Records are read to the end of the buffer, with no count and no terminator.</summary>
        [Fact]
        public void RecordsAreReadUntilTheBufferRunsOut()
        {
            byte[] file =
            {
                0x00, 0x01, 0x00, 0x02,
                0x40, 0x03, 0x00, 0x04,
                0x80, 0x05, 0x00, 0x06
            };

            List<NpcSpawn> spawns = RegionCodec.DecodeNpcSpawns(new JagStream(file));

            Assert.Equal(3, spawns.Count);
            Assert.Equal(new[] { 0, 1, 2 }, new[] { spawns[0].Plane, spawns[1].Plane, spawns[2].Plane });
            Assert.Equal(new[] { 2, 4, 6 }, new[] { spawns[0].NpcId, spawns[1].NpcId, spawns[2].NpcId });

            //A zero word is a real record at plane 0, tile 0,0 - not a terminator. A decoder that
            //stopped on it would silently drop every spawn after the first origin tile.
            Assert.Equal(file, RegionCodec.EncodeNpcSpawns(spawns));
        }

        /// <summary>An empty file decodes to no spawns, and encodes back to no bytes.</summary>
        /// <remarks>
        ///     Both supported caches ship spawn tables that decompress to zero bytes. An encoder
        ///     that emitted a header or a terminator would grow those files the first time anything
        ///     saved them.
        /// </remarks>
        [Fact]
        public void AnEmptyTableIsZeroBytesBothWays()
        {
            Assert.Empty(RegionCodec.DecodeNpcSpawns(new JagStream(Array.Empty<byte>())));
            Assert.Empty(RegionCodec.EncodeNpcSpawns(new List<NpcSpawn>()));
        }

        /// <summary>A file that is not a whole number of records is rejected, not truncated.</summary>
        /// <remarks>
        ///     The client would read past the end of its own array here. Accepting the leftover
        ///     bytes silently would mean an editor that reads a file, drops a byte or three, and
        ///     writes a shorter one back.
        /// </remarks>
        [Fact]
        public void ATrailingPartialRecordIsRejected()
        {
            Assert.Throws<InvalidDataException>(() =>
                RegionCodec.DecodeNpcSpawns(new JagStream(new byte[] { 0x00, 0x01, 0x00, 0x02, 0x00 })));
        }

        /// <summary>Every plane and every corner of the square survives a round trip.</summary>
        /// <remarks>
        ///     The packed word has two unused bits, 6 and 13, and neither is set anywhere in the
        ///     shipped data. That is what lets the encoder rebuild the word rather than keeping it
        ///     verbatim - so a boundary value that set one would be a silent loss, and these cover
        ///     the extremes on both sides of both gaps.
        /// </remarks>
        [Fact]
        public void EveryFieldExtremeRoundTrips()
        {
            var spawns = new List<NpcSpawn>();
            foreach (int plane in new[] { 0, 3 })
                foreach (int x in new[] { 0, 63 })
                    foreach (int y in new[] { 0, 63 })
                        spawns.Add(new NpcSpawn(65535, plane, x, y));

            List<NpcSpawn> decoded = RegionCodec.DecodeNpcSpawns(
                new JagStream(RegionCodec.EncodeNpcSpawns(spawns)));

            Assert.Equal(spawns.Count, decoded.Count);
            for (int i = 0; i < spawns.Count; i++)
            {
                Assert.Equal(spawns[i].Plane, decoded[i].Plane);
                Assert.Equal(spawns[i].LocalX, decoded[i].LocalX);
                Assert.Equal(spawns[i].LocalY, decoded[i].LocalY);
                Assert.Equal(spawns[i].NpcId, decoded[i].NpcId);
            }
        }
    }
}
