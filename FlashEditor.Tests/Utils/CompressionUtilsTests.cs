using FlashEditor;
using Xunit;

namespace FlashEditor.Tests.Utils
{
    public class CompressionUtilsTests
    {
        [Fact]
        public void SubArray_ReturnsCorrectSegment()
        {
            // Arrange
            int[] data = {1,2,3,4,5};

            // Act
            int[] result = CompressionUtils.SubArray(data, 1, 3);

            // Assert
            Assert.Equal(new[] {2,3,4}, result);
        }

        [Fact]
        public void Gzip_Then_Gunzip_RoundTripsBytes()
        {
            // Arrange
            byte[] data = {1, 2, 3, 4};

            // Act
            byte[] compressed = CompressionUtils.Gzip(data);
            byte[] result = CompressionUtils.Gunzip(compressed);

            // Assert
            Assert.Equal(data, result);
        }

        /// <summary>
        ///     GZip output must not carry a timestamp. The compressor writes the current
        ///     wall-clock time into the header by default, which makes compressing identical
        ///     bytes twice produce different results whenever the calls straddle a second
        ///     boundary. The archive CRC in the reference table covers the stored container, so
        ///     that would change the CRC of every group on a re-save with no edit made, and it
        ///     turns any byte-identity assertion into a coin toss decided by the clock - which is
        ///     exactly how it surfaced, as a codec test that failed roughly one run in two under
        ///     load and never in isolation.
        /// </summary>
        /// <remarks>
        ///     Zero is also the format's own convention: all 715 GZip payloads sampled from a
        ///     real revision 639 cache carry a modification time of 0.
        /// </remarks>
        [Fact]
        public void Gzip_WritesNoTimestamp()
        {
            byte[] compressed = CompressionUtils.Gzip(new byte[] { 1, 2, 3, 4 });

            //Bytes 4-7 of a GZip header are the modification time
            Assert.Equal(new byte[] { 0, 0, 0, 0 }, compressed[4..8]);
        }

        /// <summary>
        ///     The companion property, stated directly: the same input compresses to the same
        ///     bytes. This is what the rest of the suite's byte-identity assertions rest on.
        /// </summary>
        [Fact]
        public void Gzip_IsReproducible()
        {
            byte[] data = { 9, 8, 7, 6, 5, 4, 3, 2, 1 };

            Assert.Equal(CompressionUtils.Gzip(data), CompressionUtils.Gzip(data));
        }

        /// <summary>
        ///     Zeroing the timestamp must not disturb the payload, so the round trip still has to
        ///     hold for data large enough to span several deflate blocks.
        /// </summary>
        [Fact]
        public void Gzip_Then_Gunzip_RoundTripsLargerPayload()
        {
            byte[] data = new byte[40000];
            for (int i = 0; i < data.Length; i++)
                data[i] = (byte) (i % 251);

            Assert.Equal(data, CompressionUtils.Gunzip(CompressionUtils.Gzip(data)));
        }

        [Fact]
        public void Bzip2_Then_Bunzip2_RoundTripsBytes()
        {
            // Arrange
            byte[] data = {5, 6, 7, 8};

            // Act
            byte[] compressed = CompressionUtils.Bzip2(data);
            byte[] result = CompressionUtils.Bunzip2(compressed, data.Length);

            // Assert
            Assert.Equal(data, result);
        }
    }
}
