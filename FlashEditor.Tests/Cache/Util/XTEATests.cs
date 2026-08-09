using FlashEditor.Cache.Util.Crypto;
using FlashEditor;
using Xunit;
using FlashEditor.IO;

namespace FlashEditor.Tests.Cache.Util
{
    public class XTEATests
    {
        [Fact]
        public void EncipherThenDecipher_RoundTripsData()
        {
            int[] key = { 1, 2, 3, 4 };
            byte[] original = { 1, 2, 3, 4, 5, 6, 7, 8 };
            byte[] expected = (byte[])original.Clone();
            var stream = new JagStream(original);

            XTEA.Encipher(stream, 0, (int)stream.Length, key);

            // Encipher must actually mutate the data
            Assert.NotEqual(expected, stream.ToArray());

            XTEA.Decipher(stream, 0, (int)stream.Length, key);

            // Decipher must restore the original plaintext
            Assert.Equal(expected, stream.ToArray());
        }
    }
}
