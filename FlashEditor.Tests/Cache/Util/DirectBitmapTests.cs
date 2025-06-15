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

        [Fact]
        [Trait("Category", "Integration")]
        public void Save_WritesBitmapToFile()
        {
            // Arrange
            using var bmp = new DirectBitmap(1, 1);
            bmp.SetPixel(0, 0, Color.Blue);
            string path = System.IO.Path.GetTempFileName();

            try
            {
                // Act
                bmp.Save(path);

                // Assert
                Assert.True(System.IO.File.Exists(path));
            }
            finally
            {
                if (System.IO.File.Exists(path))
                    System.IO.File.Delete(path);
            }
        }
    }
}
