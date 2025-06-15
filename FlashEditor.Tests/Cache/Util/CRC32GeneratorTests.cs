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
    }
}
