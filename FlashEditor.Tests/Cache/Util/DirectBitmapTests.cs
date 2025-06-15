using FlashEditor.Cache.Util;
using System.Drawing;
using Xunit;

namespace FlashEditor.Tests.Cache.Util
{
    public class DirectBitmapTests
    {
        [Fact]
        public void SetPixel_Then_GetPixel_ReturnsSameColor()
        {
            // Arrange
            using var bmp = new DirectBitmap(2, 2);
            Color expected = Color.Red;

            // Act
            bmp.SetPixel(1, 1, expected);
            var result = bmp.GetPixel(1, 1);

            // Assert
            Assert.Equal(expected.ToArgb(), result.ToArgb());
        }
    }
}
