using FlashEditor.utils;
using Xunit;

namespace FlashEditor.Tests.Utils
{
    public class DebugUtilTests
    {
        [Theory]
        [InlineData((byte)0x3F, "00111111")]
        [InlineData((byte)0xFF, "11111111")]
        public void ToBitString_Byte_ReturnsBinaryString(byte input, string expected)
        {
            // Act
            string result = DebugUtil.ToBitString(input);

            // Assert
            Assert.Equal(expected, result);
        }

        [Theory]
        [InlineData((short)0x1F, "0000000000011111")]
        [InlineData((short)-1, "1111111111111111")]
        public void ToBitString_Short_ReturnsBinaryString(short input, string expected)
        {
            // Act
            string result = DebugUtil.ToBitString(input);

            // Assert
            Assert.Equal(expected, result);
        }

        [Theory]
        [InlineData(1, "00000000000000000000000000000001")]
        [InlineData(-1, "11111111111111111111111111111111")]
        public void ToBitString_Int_ReturnsBinaryString(int input, string expected)
        {
            // Act
            string result = DebugUtil.ToBitString(input);

            // Assert
            Assert.Equal(expected, result);
        }
    }
}
