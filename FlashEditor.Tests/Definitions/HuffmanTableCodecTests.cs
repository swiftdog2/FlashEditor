using System;
using System.IO;
using System.Linq;
using System.Text;
using FlashEditor.Definitions.Compression;
using Xunit;
using FlashEditor.IO;

namespace FlashEditor.Tests.Definitions
{
    /// <summary>
    ///     Pins the chat-table codec against tables small enough to work out by hand.
    /// </summary>
    /// <remarks>
    ///     Round-tripping this compressor against this decompressor proves nothing on its own - a
    ///     wrong codeword assignment agrees with itself perfectly. So the tables here are three,
    ///     four and six records long, and their codewords, decode trees and packed bytes were
    ///     derived by stepping <c>Class213.java:182-276</c> by hand rather than by running this
    ///     code.
    ///     <para>
    ///     <b><see cref="ThreeRecordTable"/> is the one that matters.</b> Its lengths are 2, 1, 2 in
    ///     data-value order, and a textbook canonical assignment over those lengths gives value 1
    ///     the codeword <c>0</c> and values 0 and 2 the codewords <c>10</c> and <c>11</c>. The
    ///     client gives value 0 the codeword <c>00</c>, because it assigns in data-value order and
    ///     backtracks to hand the stranded <c>1</c> prefix to the shorter length
    ///     (<c>:206-222</c>). Both are prefix-free and complete, both round-trip against
    ///     themselves, and only one agrees with the client on the wire.
    ///     </para>
    /// </remarks>
    public class HuffmanTableCodecTests
    {
        /// <summary>Lengths 2, 1, 2 - the case a canonical assignment gets wrong.</summary>
        private static readonly byte[] ThreeRecordTable = { 2, 1, 2 };

        /// <summary>Lengths 1, 2, 3, 3 - already in canonical order, so both schemes agree.</summary>
        private static readonly byte[] FourRecordTable = { 1, 2, 3, 3 };

        /// <summary>
        ///     Lengths 3, 3, 3, 3, 2, 2 - eleven tree nodes, so the eight-node array has to grow.
        /// </summary>
        private static readonly byte[] SixRecordTable = { 3, 3, 3, 3, 2, 2 };

        /// <summary>The client's assignment, not canonical Huffman, for the three-record table.</summary>
        [Fact]
        public void ThreeRecordTable_AssignsTheCodewordsTheClientAssigns()
        {
            var table = new HuffmanTable(ThreeRecordTable);

            Assert.Equal("00", table.CodewordBits(0));
            Assert.Equal("1", table.CodewordBits(1));
            Assert.Equal("01", table.CodewordBits(2));
        }

        /// <summary>
        ///     The same lengths under a canonical assignment give different codewords, which is why
        ///     the test above is worth having.
        /// </summary>
        /// <remarks>
        ///     Stated as an assertion rather than a comment so that a future canonical
        ///     reimplementation cannot pass by making both tests agree with each other.
        /// </remarks>
        [Fact]
        public void ThreeRecordTable_DisagreesWithACanonicalAssignment()
        {
            var table = new HuffmanTable(ThreeRecordTable);

            //Canonical: shortest length first, in data-value order within a length, incrementing.
            Assert.NotEqual("0", table.CodewordBits(1));
            Assert.NotEqual("10", table.CodewordBits(0));
            Assert.NotEqual("11", table.CodewordBits(2));
        }

        /// <summary>The decode tree the client would build for the three-record table.</summary>
        /// <remarks>
        ///     A negative entry is a leaf holding <c>~value</c>; a non-negative one is where a 1-bit
        ///     goes, a 0-bit always going to the next node along. The trailing zeros are the unused
        ///     tail of the eight-node array the constructor starts with.
        /// </remarks>
        [Fact]
        public void ThreeRecordTable_BuildsTheDecodeTreeTheClientBuilds()
        {
            var table = new HuffmanTable(ThreeRecordTable);

            Assert.Equal(new[] { 3, 4, -1, -2, -3, 0, 0, 0 }, table.DecodeTree().ToArray());
        }

        /// <summary>The packed bits for the three-record table, worked out from its codewords.</summary>
        /// <remarks>
        ///     <c>00</c> then <c>1</c> then <c>01</c> is <c>00101</c>, padded to <c>00101000</c>.
        ///     A canonical assignment over the same lengths would emit <c>10</c> <c>0</c> <c>11</c>
        ///     = <c>0xB0</c>, so this byte is what tells the two apart.
        /// </remarks>
        [Fact]
        public void ThreeRecordTable_PacksTheBitsTheClientWouldPack()
        {
            var table = new HuffmanTable(ThreeRecordTable);
            var packed = new byte[4];

            int written = table.Compress(new byte[] { 0, 1, 2 }, 0, 3, packed, 0);

            Assert.Equal(1, written);
            Assert.Equal(0x28, packed[0]);
        }

        /// <summary>A table already in canonical order, where the two schemes coincide.</summary>
        [Fact]
        public void FourRecordTable_AssignsAndPacksAsWorkedOutByHand()
        {
            var table = new HuffmanTable(FourRecordTable);
            var packed = new byte[4];

            Assert.Equal("0", table.CodewordBits(0));
            Assert.Equal("10", table.CodewordBits(1));
            Assert.Equal("110", table.CodewordBits(2));
            Assert.Equal("111", table.CodewordBits(3));
            Assert.Equal(new[] { 2, -1, 4, -2, 6, -3, -4, 0 }, table.DecodeTree().ToArray());

            //0 10 110 111 = 0101 1011 1, padded to 0101 1011 1000 0000.
            int written = table.Compress(new byte[] { 0, 1, 2, 3 }, 0, 4, packed, 0);
            Assert.Equal(2, written);
            Assert.Equal(new byte[] { 0x5B, 0x80 }, packed.Take(2));
        }

        /// <summary>The tree array grows past its initial eight nodes when a table needs it.</summary>
        /// <remarks>
        ///     Six records need eleven nodes. The client doubles the array in place
        ///     (<c>:252-261</c>) and the growth has to preserve every node already written, since a
        ///     1-child recorded before the resize is an index into the same array afterwards.
        /// </remarks>
        [Fact]
        public void SixRecordTable_GrowsTheDecodeTreeWithoutLosingNodes()
        {
            var table = new HuffmanTable(SixRecordTable);
            var packed = new byte[4];

            Assert.Equal(16, table.TreeSize);
            Assert.Equal(new[] { 8, 5, 4, -1, -2, 7, -3, -4, 10, -5, -6, 0, 0, 0, 0, 0 },
                table.DecodeTree().ToArray());

            //000 001 010 011 10 11 = 0000 0101 0011 1011.
            int written = table.Compress(new byte[] { 0, 1, 2, 3, 4, 5 }, 0, 6, packed, 0);
            Assert.Equal(2, written);
            Assert.Equal(new byte[] { 0x05, 0x3B }, packed.Take(2));
        }

        /// <summary>Packed bits expand back to what they were packed from, on every hand table.</summary>
        /// <remarks>
        ///     The weakest of the assertions here on its own, and the only one that exercises the
        ///     tree walk end to end. It is worth having beside the codeword pins rather than
        ///     instead of them.
        /// </remarks>
        [Theory]
        [InlineData(3)]
        [InlineData(4)]
        [InlineData(6)]
        public void HandTables_ExpandBackToWhatTheyPacked(int records)
        {
            byte[] lengths = records == 3 ? ThreeRecordTable : records == 4 ? FourRecordTable : SixRecordTable;
            var table = new HuffmanTable(lengths);

            //Every value twice over, so a codeword that starts mid-byte is packed as well as one
            //that starts on a boundary.
            byte[] plain = Enumerable.Range(0, records)
                .Concat(Enumerable.Range(0, records).Reverse())
                .Select(value => (byte) value)
                .ToArray();

            var packed = new byte[plain.Length * 4 + 4];
            int written = table.Compress(plain, 0, plain.Length, packed, 0);

            var expanded = new byte[plain.Length];
            int consumed = table.Decompress(packed, 0, expanded, 0, expanded.Length);

            Assert.Equal(plain, expanded);
            Assert.Equal(written, consumed);
        }

        /// <summary>The stored bytes come back out untouched, whatever the derived state looks like.</summary>
        /// <remarks>
        ///     The encoder writes the array the decoder read and never rebuilds it from the
        ///     codewords, which is the separation the whole type is arranged around. A constructed
        ///     table rather than the shipped one, so the property is about the codec rather than
        ///     about one file - and it carries zero-length entries, which is exactly what a
        ///     rebuild-from-derived-state would lose.
        /// </remarks>
        [Fact]
        public void Encode_WritesBackTheStoredBytes()
        {
            byte[] lengths = EightBitTable();

            //Four legal edits to it, so the encoder cannot pass by writing a constant. Dropping one
            //value of a pair and shortening its sibling to 7 keeps the code complete.
            foreach (int pair in new[] { 0, 100, 200, 250 })
            {
                lengths[pair] = 7;
                lengths[pair + 1] = 0;
            }

            var table = new HuffmanTable(lengths);

            Assert.Equal(lengths, table.Encode().ToArray());
        }

        /// <summary>
        ///     A table of 256 eight-bit codewords is the identity codec, which is what makes it a
        ///     useful fixture.
        /// </summary>
        /// <remarks>
        ///     The construction assigns in data-value order and increments by <c>1 &lt;&lt; 24</c>
        ///     per eight-bit codeword, so value <c>v</c> gets the codeword <c>v</c> and compressing
        ///     is a copy. That makes the expected output of the string and framing tests readable
        ///     without a bit-level derivation, while still going through the real bit packer.
        /// </remarks>
        /// <returns>256 lengths of 8.</returns>
        private static byte[] EightBitTable()
        {
            var lengths = new byte[HuffmanTable.EntryCount];
            for (int value = 0; value < lengths.Length; value++)
                lengths[value] = 8;
            return lengths;
        }

        /// <summary>The eight-bit table packs each byte to itself, through the real bit packer.</summary>
        [Fact]
        public void EightBitTable_IsTheIdentityCodec()
        {
            var table = new HuffmanTable(EightBitTable());

            for (int value = 0; value < HuffmanTable.EntryCount; value++)
                Assert.Equal(value, (int) ((uint) table.CodewordOf(value) >> 24));

            (byte[] packed, int characters) = table.Compress("the quick brown fox");

            Assert.Equal("the quick brown fox", Encoding.ASCII.GetString(packed));
            Assert.Equal(19, characters);
            Assert.Equal("the quick brown fox", table.Decompress(packed, characters));
        }

        /// <summary>Decode takes exactly 256 bytes and refuses a shorter file.</summary>
        /// <remarks>
        ///     The record carries no length of its own, so a decoder that read to the end of the
        ///     buffer would accept any file length - including one the client cannot index, since
        ///     it looks up the length array with an unsigned byte.
        /// </remarks>
        [Fact]
        public void Decode_TakesExactlyTheRecordCountAndRefusesLess()
        {
            var padded = new byte[HuffmanTable.EntryCount + 16];
            for (int value = 0; value < HuffmanTable.EntryCount; value++)
                padded[value] = 8;

            var stream = new JagStream(padded);
            HuffmanTable table = HuffmanTable.Decode(stream);

            Assert.Equal(HuffmanTable.EntryCount, table.Entries);
            Assert.Equal(HuffmanTable.EntryCount, stream.Position);

            Assert.Throws<InvalidDataException>(() =>
                HuffmanTable.Decode(new JagStream(new byte[HuffmanTable.EntryCount - 1])));
        }

        /// <summary>A zero bit length means the byte cannot be sent, and the compressor says so.</summary>
        /// <remarks>
        ///     <c>Class213.java:296-298</c> throws rather than skipping the character. Skipping it
        ///     would shift every character after it and produce a message that expands into
        ///     something else entirely.
        /// </remarks>
        [Fact]
        public void Compress_RefusesAValueWithNoCodeword()
        {
            var table = new HuffmanTable(new byte[] { 1, 1, 0 });
            var packed = new byte[4];

            InvalidOperationException thrown = Assert.Throws<InvalidOperationException>(
                () => table.Compress(new byte[] { 2 }, 0, 1, packed, 0));

            Assert.Contains("No codeword for data value 2", thrown.Message);
        }

        /// <summary>
        ///     A stored length the client would index its working array with out of bounds is
        ///     refused rather than reinterpreted.
        /// </summary>
        /// <remarks>
        ///     <c>Class213.java:195</c> reads a signed Java byte, so 0x80 is -128 and 0xFF is -1.
        ///     Reading the same byte unsigned would give 128 and 255, build a tree from lengths the
        ///     client cannot use, and diverge silently.
        /// </remarks>
        [Theory]
        [InlineData(0x80)]
        [InlineData(0xFF)]
        [InlineData(33)]
        public void Rebuild_RefusesALengthOutsideWhatTheClientCanUse(int stored)
        {
            Assert.Throws<InvalidDataException>(() => new HuffmanTable(new byte[] { (byte) stored, 1 }));
        }

        /// <summary>A refused edit leaves both the stored bytes and the derived state as they were.</summary>
        /// <remarks>
        ///     The editor's cell is a free-text integer, so a length the client could not use has to
        ///     be refused without the table half-changing underneath it - a stored array that no
        ///     longer matches the codewords being compressed against is the one state this type is
        ///     arranged to make impossible.
        /// </remarks>
        [Fact]
        public void SetBitLength_RefusesALengthTheClientCouldNotUse()
        {
            var table = new HuffmanTable(ThreeRecordTable);

            Assert.Throws<ArgumentOutOfRangeException>(() => table.SetBitLength(0, MaxBitLengthPlusOne));
            Assert.Throws<ArgumentOutOfRangeException>(() => table.SetBitLength(0, -1));
            Assert.Throws<ArgumentOutOfRangeException>(() => table.SetBitLength(3, 1));

            Assert.Equal(ThreeRecordTable, table.Encode().ToArray());
            Assert.Equal("00", table.CodewordBits(0));
        }

        /// <summary>One past the longest length the client's 33-entry working array can hold.</summary>
        private const int MaxBitLengthPlusOne = HuffmanTable.MaxBitLength + 1;

        /// <summary>Changing one length re-derives the codewords of the values around it.</summary>
        /// <remarks>
        ///     The reason <c>HuffmanEntryRow</c> reads through the table rather than snapshotting a
        ///     codeword, and the reason the editor refreshes every row after an edit.
        /// </remarks>
        [Fact]
        public void SetBitLength_ReDerivesTheWholeTable()
        {
            var table = new HuffmanTable(FourRecordTable);
            Assert.Equal("110", table.CodewordBits(2));

            //1, 1 is a complete code on its own, so values 2 and 3 lose their codewords.
            table.SetBitLength(2, 0);
            table.SetBitLength(3, 0);
            table.SetBitLength(1, 1);

            Assert.Equal("0", table.CodewordBits(0));
            Assert.Equal("1", table.CodewordBits(1));
            Assert.Equal(string.Empty, table.CodewordBits(2));
            Assert.Equal(new byte[] { 1, 1, 0, 0 }, table.Encode().ToArray());
        }

        /// <summary>The framed form is a smart character count and then the packed bits.</summary>
        /// <remarks>
        ///     <c>Class284_Sub1_Sub1.method3368</c> writes the count of <b>characters</b>, not of
        ///     packed bytes, which is what lets the reader stop part way through a byte.
        /// </remarks>
        [Fact]
        public void EncodeChatMessage_WritesTheCountThenTheBits()
        {
            var table = new HuffmanTable(EightBitTable());

            byte[] framed = table.EncodeChatMessage("hi");

            //Two characters, then the identity table's packing of them.
            Assert.Equal(new byte[] { 2, (byte) 'h', (byte) 'i' }, framed);
            Assert.Equal("hi", table.DecodeChatMessage(framed));
        }

        /// <summary>
        ///     A message of 128 characters or more takes the two-byte smart, and still reads back.
        /// </summary>
        /// <remarks>
        ///     The count is a smart rather than a byte, so the boundary at 128 is where a reader
        ///     that assumed one byte would start expanding the message's own first byte as part of
        ///     its length.
        /// </remarks>
        [Fact]
        public void ChatMessage_SurvivesTheSmartLengthBoundary()
        {
            var table = new HuffmanTable(EightBitTable());
            string text = new string('a', 200);

            byte[] framed = table.EncodeChatMessage(text);

            Assert.Equal(202, framed.Length);
            Assert.Equal(text, table.DecodeChatMessage(framed));
        }

        /// <summary>A packet whose length outruns its bits is reported rather than read past.</summary>
        /// <remarks>
        ///     The client walks the source with no limit at all and relies on
        ///     <c>Node_Sub10_Sub26.method1084</c> catching whatever comes of it and returning the
        ///     literal "Cabbage". A deliberate divergence: an editor wants the reason.
        /// </remarks>
        [Fact]
        public void Decompress_ReportsAPacketThatRunsOutOfBits()
        {
            var table = new HuffmanTable(EightBitTable());
            var expanded = new byte[8];

            Assert.Throws<InvalidDataException>(
                () => table.Decompress(new byte[] { 0x41, 0x42 }, 0, expanded, 0, expanded.Length));
        }
    }
}
