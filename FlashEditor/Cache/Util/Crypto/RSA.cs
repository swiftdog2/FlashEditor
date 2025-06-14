using java.math;
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

            BigInteger xin = new BigInteger(bytes);
            BigInteger xout = xin.modPow(key, modulus);

            return new JagStream(xout.toByteArray());
        }
    }
}
