using FlashEditor.Cache.Util.Crypto;
using FlashEditor;
using Xunit;

namespace FlashEditor.Tests.Cache.Util
{
    public class XTEATests
    {
        [Fact]
        public void EncipherThenDecipher_RoundTripsData()
        {
            int[] key = { 1, 2, 3, 4 };
            byte[] original = { 1, 2, 3, 4, 5, 6, 7, 8 };
            var stream = new JagStream(original);

            XTEA.Encipher(stream, 0, (int)stream.Length, key);
            XTEA.Decipher(stream, 0, (int)stream.Length, key);

            Assert.Equal(original, stream.ToArray());
        }
    }
}
