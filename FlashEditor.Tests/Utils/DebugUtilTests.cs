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
        [InlineData(unchecked((short)0xFFFF), "1111111111111111")]
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

        [Fact]
        public void PrintByteArray_WritesExpectedFormat()
        {
            // Arrange
            byte[] data = {1, 2, 3};
            using var writer = new System.IO.StringWriter();
            var originalOut = System.Console.Out;
            System.Console.SetOut(writer);

            try
            {
                // Act
                DebugUtil.PrintByteArray(data);
            }
            finally
            {
                System.Console.SetOut(originalOut);
            }

            // Assert
            Assert.Contains("3/3", writer.ToString());
        }
    }
}
