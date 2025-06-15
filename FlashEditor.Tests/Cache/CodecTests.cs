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

        [Fact]
        public void Container_EncodeDecode_RoundTrips()
        {
            // Arrange
            var payload = new JagStream();
            payload.WriteByte(1);
            payload.WriteByte(2);
            var container = new RSContainer(RSConstants.ITEM_DEFINITIONS_INDEX, 0,
                                            RSConstants.NO_COMPRESSION, payload, 1);

            // Act
            JagStream encoded = container.Encode();
            RSContainer decoded = RSContainer.Decode(new JagStream(encoded.ToArray()));
            JagStream reencoded = decoded.Encode();

            // Assert
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
    }
}
