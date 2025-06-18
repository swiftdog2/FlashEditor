using FlashEditor.Cache.Region;
using Xunit;

namespace FlashEditor.Tests.Cache
{
    public class PositionTests
    {
        [Fact]
        public void RegionConversions_CorrectValues()
        {
            var pos = new Position(3200, 3520, 2); // region coordinates 50,55

            Assert.Equal(50, pos.GetRegionX());
            Assert.Equal(55, pos.GetRegionY());
            Assert.Equal(((50 << 8) + 55), pos.GetRegionID());
            Assert.Equal((3520 + (3200 << 14) + (2 << 28)), pos.ToPositionPacked());
        }
    }
}

