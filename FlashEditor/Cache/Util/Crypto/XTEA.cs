using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace FlashEditor.Cache.Util.Crypto {
    //Straight rip no idea if it works or not lol

    class XTEA {
        /**
		 * The golden ratio.
		 */
        public static uint GOLDEN_RATIO = 0x9E3779B9;

        /**
		 * The number of rounds.
		 */
        public static int ROUNDS = 32;

        /**
		 * Deciphers the specified {@link JagStream} with the given key.
		 * 
		 * @param buffer
		 *            The buffer.
		 * @param key
		 *            The key.
		 * @throws IllegalArgumentException
		 *             if the key is not exactly 4 elements long.
		 */
        public static void Decipher(JagStream buffer, int start, int end, int[] key) {
            if(key.Length != 4)
                throw new ArgumentException("Key length is invalid");

            int numQuads = (end - start) / 8;
            for(int i = 0; i < numQuads; i++) {
                uint sum = (uint) (GOLDEN_RATIO * ROUNDS);

                buffer.Seek(start + i * 8);
                uint v0 = (uint) buffer.ReadInt();

                buffer.Seek(start + i * 8 + 4);
                uint v1 = (uint) buffer.ReadInt();

                for(int j = 0; j < ROUNDS; j++) {
                    v1 -= (uint) (((v0 << 4) ^ (v0 >> 5)) + v0) ^ (uint) (sum + key[(sum >> 11) & 3]);
                    sum -= GOLDEN_RATIO;
                    v0 -= (uint) (((v1 << 4) ^ (v1 >> 5)) + v1) ^ (uint) (sum + key[sum & 3]);
                }
                buffer.Seek(start + i * 8);
                buffer.WriteInteger(v0);

                buffer.Seek(start + i * 8 + 4);
                buffer.WriteInteger(v1);
            }
        }

        /**
		 * Enciphers the specified {@link JagStream} with the given key.
		 * 
		 * @param buffer
		 *            The buffer.
		 * @param key
		 *            The key.
		 * @throws IllegalArgumentException
		 *             if the key is not exactly 4 elements long.
		 */
        public static void Encipher(JagStream buffer, int start, int end, int[] key) {
            if(key.Length != 4)
                throw new ArgumentException();

            int numQuads = (end - start) / 8;
            for(int i = 0; i < numQuads; i++) {
                uint sum = 0;

                buffer.Seek(start + i * 8);
                uint v0 = (uint) buffer.ReadInt();

                buffer.Seek(start + i * 8 + 4);
                uint v1 = (uint) buffer.ReadInt();

                for(int j = 0; j < ROUNDS; j++) {
                    v0 += (((v1 << 4) ^ (v1 >> 5)) + v1) ^ (uint) (sum + key[sum & 3]);
                    sum += GOLDEN_RATIO;
                    v1 += (((v0 << 4) ^ (v0 >> 5)) + v0) ^ (uint) (sum + key[(sum >> 11) & 3]);
                }
                buffer.Seek(start + i * 8);
                buffer.WriteInteger(v0);

                buffer.Seek(start + i * 8 + 4);
                buffer.WriteInteger(v1);
            }
        }
    }
}
