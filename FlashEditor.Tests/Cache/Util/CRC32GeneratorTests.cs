using FlashEditor.Cache.Util;
using Xunit;

namespace FlashEditor.Tests.Cache.Util
{
    public class CRC32GeneratorTests
    {
        [Fact]
        public void GetHash_KnownValue_ReturnsExpectedCrc()
        {
            // Arrange
            byte[] data = System.Text.Encoding.ASCII.GetBytes("123456789");

            // Act
            int crc = CRC32Generator.GetHash(data);

            // Assert
            Assert.Equal(-873187034, crc); // CRC32 for "123456789" is 0xCBF43926
        }

        [Fact]
        public void GetHash_ArraySegment_ReturnsSameAsWholeArray()
        {
            // Arrange
            byte[] data = {1, 2, 3, 4, 5};
            var segment = new System.ArraySegment<byte>(data, 1, 3);

            // Act
            int fullHash = CRC32Generator.GetHash(data, 1, 3);
            int segHash = CRC32Generator.GetHash(segment);

            // Assert
            Assert.Equal(fullHash, segHash);
        }

        [Fact]
        public void UpdateByte_UpdatesInternalValue()
        {
            // Arrange
            var crc = new CRC32Generator();

            // Act
            crc.UpdateByte(0xFF);

            // Assert
            Assert.NotEqual(~CRC32Generator.GetHash(new byte[0]), crc.Value);
        }
    }
}
