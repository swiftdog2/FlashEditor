using FlashEditor;
using FlashEditor.cache;
using FlashEditor.utils;
using System.Collections.Generic;
using Xunit;

namespace FlashEditor.Tests.Cache
{
    public class CodecTests
    {
        public CodecTests()
        {
            // Disable blocking debug prompts during test execution
            DebugUtil.LOG_LEVEL = DebugUtil.LOG_DETAIL.NONE;
        }
        [Fact]
        public void ReferenceTable_EncodeDecode_RoundTrips()
        {
            // Arrange - build a minimal reference table with one entry and one child
            var table = new RSReferenceTable
            {
                format = 6,
                version = 1,
                flags = 0
            };

            var entry = new RSEntry(0);
            entry.SetVersion(1);
            entry.SetValidFileIds(new int[] { 0 });
            entry.SetChildEntries(new SortedDictionary<int, RSChildEntry>
            {
                { 0, new RSChildEntry(0) }
            });

            table.PutEntry(0, entry);

            // Act
            JagStream encoded = table.Encode();
            RSReferenceTable decoded = RSReferenceTable.Decode(new JagStream(encoded.ToArray()));
            JagStream reencoded = decoded.Encode();

            // Assert
            Assert.Equal(encoded.ToArray(), reencoded.ToArray());
        }

        [Theory]
        [InlineData(RSConstants.NO_COMPRESSION)]
        [InlineData(RSConstants.BZIP2_COMPRESSION)]
        [InlineData(RSConstants.GZIP_COMPRESSION)]
        public void Container_EncodeDecode_RoundTrips(byte compression)
        {
            var payload = new JagStream();
            payload.Write(new byte[] {1, 2, 3}, 0, 3);
            var container = new RSContainer(RSConstants.ITEM_DEFINITIONS_INDEX, 0,
                                            compression, payload, 1);

            JagStream encoded = container.Encode();
            RSContainer decoded = RSContainer.Decode(new JagStream(encoded.ToArray()));
            JagStream reencoded = decoded.Encode();

            Assert.Equal(encoded.ToArray(), reencoded.ToArray());
        }

        [Theory]
        [InlineData(RSConstants.NO_COMPRESSION)]
        [InlineData(RSConstants.BZIP2_COMPRESSION)]
        [InlineData(RSConstants.GZIP_COMPRESSION)]
        public void Container_MultiFile_RoundTrips(byte compression)
        {
            var archive = new RSArchive();
            archive.PutEntry(0, new JagStream(new byte[] { 1, 2 }));
            archive.PutEntry(1, new JagStream(new byte[] { 3, 4, 5 }));

            var container = new RSContainer(RSConstants.ITEM_DEFINITIONS_INDEX, 0,
                                            compression, archive.Encode(), 1);

            JagStream encoded = container.Encode();
            RSContainer decoded = RSContainer.Decode(new JagStream(encoded.ToArray()));
            JagStream reencoded = decoded.Encode();

            Assert.Equal(encoded.ToArray(), reencoded.ToArray());
        }

        [Fact]
        public void Archive_EncodeDecode_RoundTrips()
        {
            // Arrange
            var archive = new RSArchive();
            archive.PutEntry(0, new JagStream(new byte[] { 1, 2, 3 }));
            archive.PutEntry(1, new JagStream(new byte[] { 4, 5 }));

            // Act
            JagStream encoded = archive.Encode();
            RSArchive decoded = RSArchive.Decode(new JagStream(encoded.ToArray()), archive.EntryCount());
            JagStream reencoded = decoded.Encode();

            // Assert
            Assert.Equal(encoded.ToArray(), reencoded.ToArray());
        }

        /// <summary>
        ///     Ensures container headers remain byte‑accurate through multiple
        ///     encode/decode cycles for all compression methods and for
        ///     multi‑file archives.
        /// </summary>
        [Theory]
        [InlineData(RSConstants.NO_COMPRESSION)]
        [InlineData(RSConstants.BZIP2_COMPRESSION)]
        [InlineData(RSConstants.GZIP_COMPRESSION)]
        public void Container_RoundTrip_PreservesBytes(byte compression)
        {
            var payload = new JagStream(System.Text.Encoding.ASCII.GetBytes("hello"));
            var container = new RSContainer(RSConstants.ITEM_DEFINITIONS_INDEX, 0,
                                            compression, payload, 1);

            JagStream initial = container.Encode();
            RSContainer decoded = RSContainer.Decode(new JagStream(initial.ToArray()));
            JagStream re = decoded.Encode();
            RSContainer again = RSContainer.Decode(new JagStream(re.ToArray()));
            Assert.Equal(initial.ToArray(), re.ToArray());

            var archive = new RSArchive();
            archive.PutEntry(0, payload);
            archive.PutEntry(1, new JagStream(System.Text.Encoding.ASCII.GetBytes("bye")));

            var multi = new RSContainer(RSConstants.ITEM_DEFINITIONS_INDEX, 1,
                                        compression, archive.Encode(), 1);

            JagStream initArchive = multi.Encode();
            RSContainer decArchive = RSContainer.Decode(new JagStream(initArchive.ToArray()));
            JagStream reArchive = decArchive.Encode();
            RSContainer againArchive = RSContainer.Decode(new JagStream(reArchive.ToArray()));
            Assert.Equal(initArchive.ToArray(), reArchive.ToArray());
        }
    }
}
