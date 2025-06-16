using System;
using System.Text;
using FlashEditor.Cache.Util;
using Xunit;

namespace FlashEditor.Tests.Cache.Util
{
    public class WhirlpoolTests
    {
        [Fact]
        public void EmptyString_HashesToExpectedVector()
        {
            const string expected = "19FA61D75522A4669B44E39C1D2E1726C530232130D407F89AFEE0964997F7A73E83BE698B288FEBCF88E3E03C4F0757EA8964E59B63D93708B138CC42A66EB3";

            byte[] empty = Array.Empty<byte>();
            string actual = Whirlpool.ComputeHashHex(empty);

            Assert.Equal(expected, actual);
        }

        [Fact]
        public void QuickBrownFox_HashesToExpectedVector()
        {
            const string input = "The quick brown fox jumps over the lazy dog";
            const string expected = "B97DE512E91E3828B40D2B0FDCE9CEB3C4A71F9BEA8D88E75C4FA854DF36725FD2B52EB6544EDCACD6F8BEDDFEA403CB55AE31F03AD62A5EF54E42EE82C3FB35";

            byte[] data = Encoding.ASCII.GetBytes(input);
            string actual = Whirlpool.ComputeHashHex(data);

            Assert.Equal(expected, actual);
        }

        [Theory]
        [InlineData("Hydrascape", "D4347B1546086CBD0386CD7DAC899CCE8FDF716B7B2CC91054F4563FFD22A92FCE5C7E4A5ED929C45314A90E53906C49B4FA0623B3A43D6E9BD1274CFC643FC6")]
        public void KnownVectors_HashCorrectly(string text, string expectedHex)
        {
            byte[] data = Encoding.ASCII.GetBytes(text);
            string actual = Whirlpool.ComputeHashHex(data);
            Assert.Equal(expectedHex, actual);
        }
    }
}
