using System.Numerics;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace FlashEditor.Cache.Util.Crypto {
    //Straight rip no idea if it works or not lol

    class RSA {
        /**
         * Encrypts/decrypts the specified buffer with the key and modulus.
         * 
         * @param buffer
         *            The input buffer.
         * @param modulus
         *            The modulus.
         * @param key
         *            The key.
         * @return The output buffer.
         */
        public static JagStream Crypt(JagStream buffer, BigInteger modulus, BigInteger key) {
            byte[] bytes = new byte[buffer.Length];
            buffer.Read(bytes, 0, bytes.Length);

            // System.Numerics.BigInteger expects little-endian byte arrays.
            Array.Reverse(bytes);
            byte[] temp = new byte[bytes.Length + 1];
            Array.Copy(bytes, 0, temp, 0, bytes.Length);

            BigInteger xin = new BigInteger(temp);
            BigInteger xout = BigInteger.ModPow(xin, key, modulus);

            // Convert the result back to big-endian.
            byte[] outBytes = xout.ToByteArray();
            int trim = outBytes.Length;
            while(trim > 1 && outBytes[trim - 1] == 0)
                trim--;
            Array.Resize(ref outBytes, trim);
            Array.Reverse(outBytes);

            return new JagStream(outBytes);
        }
    }
}
