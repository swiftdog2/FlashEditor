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
            const string expected =
                "19FA61D75522A4669B44E39C5D5E5FA4" +
                "5E04CE6F21513EF0AE0990B56341E39F" +
                "099EFCF03C3B70228C83323E3C1EAB60" +
                "3AADBE12E6E04A5FBD7A4EF2468E7FB9";

            byte[] empty = Array.Empty<byte>();
            string actual = Whirlpool.ComputeHashHex(empty);

            Assert.Equal(expected, actual);
        }

        [Fact]
        public void QuickBrownFox_HashesToExpectedVector()
        {
            const string input = "The quick brown fox jumps over the lazy dog";
            const string expected =
                "B97DEEF07E2B726A3B663619E0D9A742" +
                "4ED328A430DB5C45CCA127E0EBED7C82" +
                "ACC6D5AE7F46E6C4FE18F8C6BA3CFBEA" +
                "5CB8AA7D2CE4D9A8420B7086CD801304";

            byte[] data = Encoding.ASCII.GetBytes(input);
            string actual = Whirlpool.ComputeHashHex(data);

            Assert.Equal(expected, actual);
        }

        [Theory]
        [InlineData("abc", "4E2448A4C6F486BB16B6562C73B4026B" +
                         "5DD36BDBA0DE5C2BB3E68E62BD07C5A2" +
                         "91144EF7D7E35300095E0E71E80B065B" +
                         "6516A1E5D8AAEA279E7D1A77B82D0EA8")]
        public void KnownVectors_HashCorrectly(string text, string expectedHex)
        {
            byte[] data = Encoding.ASCII.GetBytes(text);
            string actual = Whirlpool.ComputeHashHex(data);
            Assert.Equal(expectedHex, actual);
        }
    }
}
