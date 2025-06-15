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
    }
}
