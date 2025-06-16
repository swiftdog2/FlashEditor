using FlashEditor.Cache.Util;
using FlashEditor.cache;
using Xunit;

namespace FlashEditor.Tests.Cache.Util
{
    public class CRC32HelperTests
    {
        [Fact]
        public void ComputeCrc32_KnownValue_ReturnsExpectedCrc()
        {
            byte[] data = System.Text.Encoding.ASCII.GetBytes("123456789");
            uint crc = CRC32Helper.ComputeCrc32(data);
            Assert.Equal(0xCBF43926u, crc);
        }

        [Fact]
        public void Crc32_Extension_ReturnsSameValue()
        {
            byte[] data = { 1, 2, 3, 4 };
            uint crc1 = CRC32Helper.ComputeCrc32(data);
            uint crc2 = data.Crc32();
            Assert.Equal(crc1, crc2);
        }
    }
}