using FlashEditor.Cache.CheckSum;
using FlashEditor.Cache.Util;
using FlashEditor.Cache.Util.Crypto;
using System.Numerics;
using System;

namespace FlashEditor.Cache {
    class ChecksumTable {
        /**
		 * Copyright (c) OpenRS
		 *
		 * Permission is hereby granted, free of charge, to any person obtaining a copy
		 * of this software and associated documentation files (the "Software"), to deal
		 * in the Software without restriction, including without limitation the rights
		 * to use, copy, modify, merge, publish, distribute, sublicense, and/or sell
		 * copies of the Software, and to permit persons to whom the Software is
		 * furnished to do so, subject to the following conditions:
		 * 
		 * The above copyright notice and this permission notice shall be included in all
		 * copies or substantial portions of the Software.
		 * 
		 * THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
		 * IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
		 * FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
		 * AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
		 * LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
		 * OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE
		 * SOFTWARE.
		 */

        /**
         * Decodes the {@link ChecksumTable} in the specified {@link JagStream}.
         * Whirlpool digests are not read.
         * 
         * @param buffer
         *            The {@link JagStream} containing the table.
         * @return The decoded {@link ChecksumTable}.
         * @throws IOException
         *             if an I/O error occurs.
         */
        public static ChecksumTable Decode(JagStream buffer) {
            return Decode(buffer, false);
        }

        /**
         * Decodes the {@link ChecksumTable} in the specified {@link JagStream}.
         * 
         * @param buffer
         *            The {@link JagStream} containing the table.
         * @param whirlpool
         *            If whirlpool digests should be read.
         * @return The decoded {@link ChecksumTable}.
         * @throws IOException
         *             if an I/O error occurs.
         */
        public static ChecksumTable Decode(JagStream buffer, bool whirlpool) {
            return Decode(buffer, whirlpool, null, null);
        }

        /**
         * Decodes the {@link ChecksumTable} in the specified {@link JagStream} and
         * decrypts the final whirlpool hash.
         * 
         * @param buffer
         *            The {@link JagStream} containing the table.
         * @param whirlpool
         *            If whirlpool digests should be read.
         * @param modulus
         *            The modulus.
         * @param publicKey
         *            The public key.
         * @return The decoded {@link ChecksumTable}.
         * @throws IOException
         *             if an I/O error occurs.
         */
        public static ChecksumTable Decode(JagStream buffer, bool whirlpool, BigInteger? modulus, BigInteger? publicKey) {
            //Find out how many entries there are and allocate a new table
            int size = whirlpool ? buffer.ReadUnsignedByte() : ((int) buffer.Length / 8);
            ChecksumTable table = new ChecksumTable(size);

            //calculate the whirlpool digest we expect to have at the end 
            byte[] masterDigest = null;
            if(whirlpool) {
                int tmpLen = size * 80 + 1;
                Span<byte> temp = tmpLen <= 4096 ? stackalloc byte[tmpLen] : new byte[tmpLen];
                buffer.Position = 0;
                buffer.Read(temp);
                masterDigest = Whirlpool.whirlpool(temp.ToArray(), 0, temp.Length);
            }

            //read the entries
            buffer.Seek(whirlpool ? 1 : 0);

            for(int i = 0; i < size; i++) {
                int crc = buffer.ReadInt();
                int version = buffer.ReadInt();
                int files = whirlpool ? buffer.ReadInt() : 0;
                int archiveSize = whirlpool ? buffer.ReadInt() : 0;

                //Read the whirlpool digest, if applicable
                Span<byte> digest = stackalloc byte[64];
                if(whirlpool)
                    buffer.Read(digest);

                table.entries[i] = new CheckSumEntry(crc, version, files, archiveSize, digest.ToArray());
            }

            //Read the trailing digest and check if it matches up
            if(whirlpool) {
                byte[] bytes = new byte[buffer.Remaining()];
                buffer.Read(bytes.AsSpan());
                JagStream temp = new JagStream(bytes);

                if(modulus.HasValue && publicKey.HasValue) {
                    temp = RSA.Crypt(buffer, modulus.Value, publicKey.Value);
                }

                if(temp.Length != 65)
                    throw new Exception("Decrypted data is not 65 bytes long");

                for(int i = 0; i < 64; i++) {
                    if(temp.Get(i + 1) != masterDigest[i])
                        throw new Exception("Whirlpool digest mismatch");
                }
            }

            //If it looks good return the table
            return table;
        }

        private CheckSumEntry[] entries;

        public ChecksumTable(int size) {
            entries = new CheckSumEntry[size];
        }

        public JagStream Encode() {
            return Encode(false);
        }

        public JagStream Encode(bool whirlpool) {
            return Encode(whirlpool, null, null);
        }

        public JagStream Encode(bool whirlpool, BigInteger? modulus, BigInteger? privateKey) {
            /*
            JagStream os = new JagStream();

            try {
                //As the new whirlpool format is more complicated, we must write the number of entries
                if(whirlpool)
                    os.WriteByte((byte) entries.Length);

                //Encode the individual entries
                for(int i = 0; i < entries.Length; i++) {
                    CheckSumEntry CheckSumEntry = entries[i];
                    os.WriteInteger(CheckSumEntry.GetCrc());
                    os.WriteInteger(CheckSumEntry.GetVersion());
                    if(whirlpool) {
                        os.WriteInteger(CheckSumEntry.GetFileCount());
                        os.WriteInteger(CheckSumEntry.GetSize());
                        os.Write(CheckSumEntry.GetWhirlpool(), 0, CheckSumEntry.GetWhirlpool().Length);
                    }
                }

                byte[] bytes;

                //Compute (and encrypt) the digest of the whole table
                if(whirlpool) {
                    bytes = bout.toByteArray();
                    JagStream temp = new JagStream(65);
                    temp.put((byte) 0);
                    temp.put(Whirlpool.whirlpool(bytes, 0, bytes.Length));
                    temp.Flip();

                    if(modulus.HasValue && privateKey.HasValue) {
                        temp = RSA.Crypt(temp, modulus.Value, privateKey.Value);
                    }

                    bytes = new byte[temp.Length];
                    temp.Read(bytes, 0, bytes.Length);
                    os.write(bytes);
                }

                bytes = bout.toByteArray();
                return new JagStream(bytes);
            } finally {
                os.close();
            }
            */

            return null;
        }

        public int GetSize() {
            return entries.Length;
        }

        public void setCheckSumEntry(int id, CheckSumEntry CheckSumEntry) {
            if(id < 0 || id >= entries.Length)
                throw new Exception();
            entries[id] = CheckSumEntry;
        }

        public CheckSumEntry getCheckSumEntry(int id) {
            if(id < 0 || id >= entries.Length)
                throw new Exception();
            return entries[id];
        }

    }
}
