using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace FlashEditor.Cache.Util.Crypto {
    /// <summary>
    ///     Implementation of the standard XTEA block cipher used by the
    ///     RuneScape cache. The cipher operates on 8 byte blocks and
    ///     always performs 32 rounds.
    /// </summary>
    public static class XTEA {
        /**
		 * The golden ratio.
		 */
        public static readonly uint GOLDEN_RATIO = 0x9E3779B9u;

        /**
		 * The number of rounds.
		 */
        public const int ROUNDS = 32;

        /// <summary>
        ///     Decrypts <paramref name="buffer"/> in-place using the supplied
        ///     128-bit key.
        /// </summary>
        /// <param name="buffer">Stream containing the cipher text.</param>
        /// <param name="start">Starting offset.</param>
        /// <param name="end">End offset.</param>
        /// <param name="key">Four element XTEA key.</param>
        /// <exception cref="ArgumentException">Key length is not 4.</exception>
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

        /// <summary>
        ///     Encrypts <paramref name="buffer"/> in-place using the supplied
        ///     128-bit key.
        /// </summary>
        /// <param name="buffer">Stream containing the plain text.</param>
        /// <param name="start">Starting offset.</param>
        /// <param name="end">End offset.</param>
        /// <param name="key">Four element XTEA key.</param>
        /// <exception cref="ArgumentException">Key length is not 4.</exception>
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

