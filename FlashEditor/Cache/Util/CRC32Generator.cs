using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace FlashEditor.Cache.Util {
    //Ripped from CacheIO
    //Straight rip no idea if it works or not lol

    public class CRC32Generator {
        private const uint kCrcPoly = 0xEDB88320;
        private const uint kInitial = 0xFFFFFFFF;
        private static readonly uint[] Table;
        private const uint CRC_NUM_TABLES = 8;

        static CRC32Generator() {
            unchecked {
                Table = new uint[256 * CRC_NUM_TABLES];
                uint i;
                for(i = 0; i < 256; i++) {
                    uint r = i;
                    for(int j = 0; j < 8; j++)
                        r = (r >> 1) ^ (kCrcPoly & ~((r & 1) - 1));
                    Table[i] = r;
                }
                for(; i < 256 * CRC_NUM_TABLES; i++) {
                    uint r = Table[i - 256];
                    Table[i] = Table[r & 0xFF] ^ (r >> 8);
                }
            }
        }

        private uint value;

        public CRC32Generator() {
            Init();
        }

        /// <summary>
        /// Resets the CRC accumulator for new data.
        /// </summary>
        public void Init() {
            value = kInitial;
        }

        /// <summary>
        /// Gets the computed CRC32 hash.
        /// </summary>
        public int Value {
            get { return (int) ~value; }
        }

        /// <summary>
        /// Updates the hash with a single byte.
        /// </summary>
        /// <param name="b">The byte value to incorporate.</param>
        public void UpdateByte(byte b) {
            value = (value >> 8) ^ Table[(byte) value ^ b];
        }

        /// <summary>
        /// Updates the hash with a block of bytes.
        /// </summary>
        /// <param name="data">The buffer containing data.</param>
        /// <param name="offset">Zero‑based index in <paramref name="data"/> to
        /// start reading.</param>
        /// <param name="count">The number of bytes to read from the buffer.</param>
        /// <exception cref="ArgumentOutOfRangeException">Thrown when
        /// <paramref name="offset"/> or <paramref name="count"/> is out of range
        /// for <paramref name="data"/>.</exception>
        /// <exception cref="ArgumentNullException">Thrown when
        /// <paramref name="data"/> is null.</exception>
        public void Update(byte[] data, int offset, int count) {
            new ArraySegment<byte>(data, offset, count);     // check arguments
            if(count == 0)
                return;

            var table = CRC32Generator.Table;        // important for performance!

            uint crc = value;

            for(; (offset & 7) != 0 && count != 0; count--)
                crc = (crc >> 8) ^ table[(byte) crc ^ data[offset++]];

            if(count >= 8) {
                /*
                 * Idea from 7-zip project sources (http://7-zip.org/sdk.html)
                 */

                int to = (count - 8) & ~7;
                count -= to;
                to += offset;

                while(offset != to) {
                    crc ^= (uint) (data[offset] + (data[offset + 1] << 8) + (data[offset + 2] << 16) + (data[offset + 3] << 24));
                    uint high = (uint)(data[offset + 4] + (data[offset + 5] << 8) + (data[offset + 6] << 16) + (data[offset + 7] << 24));
                    offset += 8;

                    crc = table[(byte) crc + 0x700]
                        ^ table[(byte) (crc >>= 8) + 0x600]
                        ^ table[(byte) (crc >>= 8) + 0x500]
                        ^ table[/*(byte)*/(crc >> 8) + 0x400]
                        ^ table[(byte) (high) + 0x300]
                        ^ table[(byte) (high >>= 8) + 0x200]
                        ^ table[(byte) (high >>= 8) + 0x100]
                        ^ table[/*(byte)*/(high >> 8) + 0x000];
                }
            }

            while(count-- != 0)
                crc = (crc >> 8) ^ table[(byte) crc ^ data[offset++]];

            value = crc;
        }

        /// <summary>
        /// Computes the CRC32 hash for a segment of a byte array.
        /// </summary>
        /// <param name="data">The source buffer.</param>
        /// <param name="offset">Zero‑based index in <paramref name="data"/> to
        /// start hashing.</param>
        /// <param name="size">The number of bytes to process.</param>
        /// <returns>The 32‑bit CRC value.</returns>
        public static int GetHash(byte[] data, int offset, int size) {
            var crc = new CRC32Generator();
            crc.Update(data, offset, size);
            return crc.Value;
        }

        /// <summary>
        /// Computes the CRC32 hash for the entire buffer.
        /// </summary>
        /// <param name="data">The buffer to process.</param>
        /// <returns>The 32‑bit CRC value.</returns>
        public static int GetHash(byte[] data) {
            return GetHash(data, 0, data.Length);
        }

        /// <summary>
        /// Computes the CRC32 hash for an array segment.
        /// </summary>
        /// <param name="block">The segment containing the bytes.</param>
        /// <returns>The 32‑bit CRC value.</returns>
        public static int GetHash(ArraySegment<byte> block) {
            return GetHash(block.Array, block.Offset, block.Count);
        }
    }
}
