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
