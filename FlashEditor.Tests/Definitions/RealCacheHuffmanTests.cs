using System;
using System.Collections.Generic;
using System.Linq;
using FlashEditor.cache;
using FlashEditor.Cache.Util;
using FlashEditor.Definitions.Compression;
using FlashEditor.Tests.Cache.RealCache;
using Xunit;
using Xunit.Abstractions;

namespace FlashEditor.Tests.Definitions
{
    /// <summary>
    ///     Decodes the chat Huffman table index 10 declares, requires it to re-encode to the bytes
    ///     it came from, and then uses it to compress and expand real text.
    /// </summary>
    /// <remarks>
    ///     Index 10 is one group holding one file, so the byte-identity sweep here is small. What
    ///     makes it worth more than its size is that the derived structures can be checked directly
    ///     rather than inferred: a Huffman table either encodes every byte value into a prefix-free
    ///     code whose tree reads it back, or it does not, and no amount of decoding is evidence
    ///     either way.
    ///     <para>
    ///     Everything asserted is a relationship - the swept count equals the declared count, the
    ///     Kraft sum equals one, every codeword walks the tree to its own value - so nothing here
    ///     depends on which of the two supported caches is loaded. The measurements themselves are
    ///     printed instead of asserted, since a bit-length histogram is a fact about one table.
    ///     </para>
    /// </remarks>
    [Collection("RealCache")]
    public sealed class RealCacheHuffmanTests : IClassFixture<RealCacheFixture>
    {
        private readonly RealCacheFixture _fixture;
        private readonly ITestOutputHelper _output;

        /// <summary>Binds the shared open cache and the per-test output sink.</summary>
        public RealCacheHuffmanTests(RealCacheFixture fixture, ITestOutputHelper output)
        {
            _fixture = fixture;
            _output = output;
        }

        /// <summary>Groups the index-10 reference table declares.</summary>
        /// <remarks>Read from the table. A count the cache states is never written down here.</remarks>
        private int GroupsDeclared => _fixture.DeclaredGroups(RSConstants.HUFFMAN_INDEX);

        /// <summary>Files the index-10 reference table declares across every group.</summary>
        private int FilesDeclared => _fixture.DeclaredFiles(RSConstants.HUFFMAN_INDEX);

        /// <summary>The Huffman index bound to the production codec.</summary>
        /// <remarks>
        ///     <c>NotOpcodeTerminated</c> because the record is not an opcode stream at all - it is
        ///     256 fixed-width bit lengths, and its last byte is one of them. Exact consumption
        ///     still applies and is the whole statement about the layout here: the file carries no
        ///     length of its own, so a decoder that read to the end of the buffer would accept any
        ///     file size, and the padded decode is what refuses that.
        /// </remarks>
        /// <returns>A sweep over the chat table.</returns>
        private DefinitionSweep<HuffmanTable> Sweep()
        {
            return new DefinitionSweep<HuffmanTable>(_fixture, _output, RSConstants.HUFFMAN_INDEX,
                new DefinitionCodec<HuffmanTable>("chat table",
                    (_, stream) => HuffmanTable.Decode(stream),
                    table => table.Encode()))
                .AcrossEveryGroup()
                .NotOpcodeTerminated();
        }

        /// <summary>The chat table decodes and finishes on the last byte of its file.</summary>
        [RealCacheFact]
        public void TheChatTable_DecodesAndConsumesItsBufferExactly()
        {
            DefinitionSweepResult swept = Sweep().AssertExactConsumption();

            Assert.True(FilesDeclared > 0, "index 10 declares no files, so nothing was checked");
            Assert.Equal(GroupsDeclared, swept.Groups);
            Assert.Equal(FilesDeclared, swept.Records);
            Assert.Equal(FilesDeclared, swept.Passed);
        }

        /// <summary>The chat table re-encodes to the bytes it was decoded from.</summary>
        /// <remarks>
        ///     The property the editor depends on. The codewords and the decode tree are derived
        ///     state and an encoder that tried to rebuild the file from either of them would produce
        ///     a different table for the same decoded value - the assignment is many-to-one in the
        ///     direction that matters.
        /// </remarks>
        [RealCacheFact]
        public void TheChatTable_ReEncodesToItsStoredBytes()
        {
            DefinitionSweepResult swept = Sweep().AssertReEncodesToCapturedBytes();

            Assert.True(FilesDeclared > 0, "index 10 declares no files, so nothing was checked");
            Assert.Equal(FilesDeclared, swept.Records);
            Assert.Equal(FilesDeclared, swept.Passed);
            Assert.Equal(0, swept.Reordered);
        }

        /// <summary>
        ///     The group is found by hashing its name, and the name proves the join.
        /// </summary>
        /// <remarks>
        ///     <c>InterfaceSettings.java:310</c> asks index 10 for <c>"huffman"</c> and never uses
        ///     an id. The identifier the reference table carries is
        ///     <c>hash("huffman")</c> exactly, which is a self-proving join rather than a plausible
        ///     one - so the locator keys on the hash and a cache that renumbered the group would
        ///     still be read correctly.
        /// </remarks>
        [RealCacheFact]
        public void TheChatTable_IsAddressedByItsNameHash()
        {
            RSCache cache = _fixture.OpenCache();
            (int groupId, int fileId) = HuffmanTable.Locate(cache);

            RSReferenceTable table = cache.GetReferenceTable(RSConstants.HUFFMAN_INDEX);
            int identifier = table.GetArchiveEntry(groupId).GetIdentifier();

            Assert.Equal(NameHasher.GetNameHash(HuffmanTable.GroupName), identifier);

            byte[] stored = cache.ReadFileBytes(RSConstants.HUFFMAN_INDEX, groupId, fileId);
            _output.WriteLine($"index 10 group {groupId} file {fileId}: {stored.Length} bytes, " +
                              $"identifier 0x{identifier:X8}");
            Assert.Equal(HuffmanTable.EntryCount, stored.Length);
        }

        /// <summary>
        ///     Every codeword the table derives walks the decode tree to its own data value, in
        ///     exactly its own number of bits.
        /// </summary>
        /// <remarks>
        ///     This is what the byte-identity sweep cannot say anything about. The stored file is
        ///     lengths only; the codewords and the tree are two separate derivations from those
        ///     lengths, and nothing forces them to agree. A codeword that reached a leaf early would
        ///     mean the code is not prefix-free, and one that reached the wrong leaf would mean the
        ///     two derivations disagree - both silent under a round trip that used only one of them.
        /// </remarks>
        [RealCacheFact]
        public void EveryCodeword_WalksTheDecodeTreeBackToItsOwnValue()
        {
            HuffmanTable table = HuffmanTable.Load(_fixture.OpenCache());
            IReadOnlyList<int> tree = table.DecodeTree();
            var failures = new List<string>();
            int checkedValues = 0;

            for (int value = 0; value < table.Entries; value++)
            {
                int bits = table.BitLengthOf(value);
                if (bits <= 0)
                    continue;

                checkedValues++;
                int codeword = table.CodewordOf(value);
                int node = 0;
                int reached = int.MinValue;
                int consumed = 0;

                for (int bit = 0; bit < bits; bit++)
                {
                    node = (codeword & (int) (0x80000000u >> bit)) != 0 ? tree[node] : node + 1;
                    consumed++;

                    if (tree[node] >= 0)
                        continue;

                    reached = ~tree[node];
                    break;
                }

                if (reached != value)
                    failures.Add($"value {value} ({table.CodewordBits(value)}) reached {reached}");
                else if (consumed != bits)
                    failures.Add($"value {value} reached its leaf after {consumed} of {bits} bits, " +
                                 "so the code is not prefix free");
            }

            _output.WriteLine($"{checkedValues} of {table.Entries} data values have a codeword, " +
                              $"{tree.Count} decode-tree nodes");
            Assert.True(checkedValues > 0, "no data value has a codeword, so nothing was checked");
            Assert.Empty(failures);
        }

        /// <summary>
        ///     The code is complete: the bit lengths satisfy the Kraft equality exactly.
        /// </summary>
        /// <remarks>
        ///     Equality rather than "at most one". Below one, some bit patterns decode to nothing
        ///     and the tree has dangling nodes an inbound packet could walk into; above one, two
        ///     values share a prefix. Reported alongside the bit-length histogram, which is a fact
        ///     about the shipped table rather than about the format and so is printed, not asserted.
        /// </remarks>
        [RealCacheFact]
        public void TheBitLengths_FormACompleteCode()
        {
            HuffmanTable table = HuffmanTable.Load(_fixture.OpenCache());
            var histogram = new SortedDictionary<int, int>();
            long kraft = 0;
            int unencodable = 0;

            for (int value = 0; value < table.Entries; value++)
            {
                int bits = table.BitLengthOf(value);
                histogram.TryGetValue(bits, out int seen);
                histogram[bits] = seen + 1;

                if (bits <= 0)
                {
                    unencodable++;
                    continue;
                }

                //Scaled by 2^32 so the sum is exact in integers: a length of 32 contributes 1.
                kraft += 1L << (32 - bits);
            }

            _output.WriteLine("bit lengths: " +
                              string.Join(", ", histogram.Select(entry => $"{entry.Key}={entry.Value}")));
            _output.WriteLine($"{unencodable} data values have no codeword");

            Assert.Equal(1L << 32, kraft);
        }

        /// <summary>
        ///     The table compresses and expands real text, including every byte value it can carry.
        /// </summary>
        /// <remarks>
        ///     A table that parses is not a table that works. This is the one index where the
        ///     derived structure can be exercised end to end rather than argued about, so it is
        ///     driven through the framed form the client actually puts on the wire - a smart
        ///     character count and then the packed bits - rather than through the raw bit packer.
        ///     <para>
        ///     The all-values case is what covers the bit packer's spill arms: the longest codeword
        ///     in this table straddles four destination bytes when it starts near the end of one,
        ///     and a run of every encodable value hits every start offset.
        ///     </para>
        /// </remarks>
        [RealCacheFact]
        public void TheChatTable_CompressesAndExpandsRealText()
        {
            HuffmanTable table = HuffmanTable.Load(_fixture.OpenCache());

            string[] messages =
            {
                "hello world",
                "Cabbage",
                "the quick brown fox jumps over the lazy dog",
                "Buying gf 10k!!! ::home",
                "1234567890 -_=+[]{};:'\"\\|,.<>/?"
            };

            foreach (string message in messages)
            {
                byte[] framed = table.EncodeChatMessage(message);
                Assert.Equal(message, table.DecodeChatMessage(framed));
                _output.WriteLine($"{message.Length} characters compressed to {framed.Length} " +
                                  "packet bytes including the length");
            }

            //Every byte value the table can encode, at every bit alignment a run produces.
            byte[] plain = Enumerable.Range(0, table.Entries)
                .Where(value => table.BitLengthOf(value) > 0)
                .Select(value => (byte) value)
                .ToArray();

            var packed = new byte[plain.Length * 4 + 8];
            int written = table.Compress(plain, 0, plain.Length, packed, 0);

            var expanded = new byte[plain.Length];
            int consumed = table.Decompress(packed, 0, expanded, 0, expanded.Length);

            Assert.Equal(plain, expanded);
            Assert.Equal(written, consumed);
            _output.WriteLine($"{plain.Length} encodable byte values packed into {written} bytes");
        }
    }
}
