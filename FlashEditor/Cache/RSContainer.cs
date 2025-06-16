using static FlashEditor.utils.DebugUtil;
using System.IO;
using System;
using FlashEditor.utils;
using FlashEditor.Cache.Util.Crypto;

namespace FlashEditor.cache {
    public class RSContainer {
        private JagStream stream; //the archive stream
        public int type;
        public int id;
        public int length;
        public byte compressionType = 0;
        public int version = -1;
        public int decompressedLength = -1;

        //The archive that is represented by the container
        public RSArchive archive;

        public RSContainer() {

        }

        public RSContainer(RSContainer container) {
            type = container.GetIndexType();
            id = container.GetId();
            length = container.GetLength();
            compressionType = container.GetCompressionType();
            version = container.GetVersion();
            decompressedLength = container.GetDecompressedLength();
        }

        public RSContainer(int type, int id, byte compressionType, JagStream stream, int version) {
            this.type = type;
            this.id = id;
            this.compressionType = compressionType;
            this.stream = stream;
            this.version = version;
    }

        /// <summary>
        /// Encodes this container to the on-disk format.
        /// </summary>
        /// <remarks>
        /// The header consists of a single byte compression type followed by
        /// the <em>uncompressed</em> payload length. The payload is optionally
        /// XTEA encrypted when <paramref name="xteaKey"/> is provided. If the
        /// container represents a multi-file archive then the payload already
        /// contains the cumulative length table produced by
        /// <see cref="RSArchive.Encode"/>.
        /// </remarks>
        /// <param name="xteaKey">Optional 4 integer XTEA key. When
        /// <c>null</c> no encryption is performed.</param>
        /// <returns>A <see cref="JagStream"/> containing the encoded bytes.</returns>
        public JagStream Encode(int[] xteaKey = null) {
            Debug("Encoding RSContainer " + id + ", length " + GetStream().Length);

            JagStream stream = new JagStream();

            byte[] data = GetStream().ToArray();
            int oldLen = data.Length;

            //Write the data to the stream
            compressionType = GetCompressionType();
            data = compressionType switch {
                RSConstants.BZIP2_COMPRESSION => CompressionUtils.Bzip2(data),
                RSConstants.GZIP_COMPRESSION => CompressionUtils.Gzip(data),
                _ => data
            };

            Debug("Compressed " + oldLen + " to : " + data.Length);

            //Write the header
            stream.WriteByte(compressionType);
            stream.WriteInteger(GetStream().Length);

            //Optionally encrypt the payload before writing it out
            if (xteaKey != null)
            {
                JagStream temp = new JagStream(data);
                Cache.Util.Crypto.XTEA.Encipher(temp, 0, temp.Length, xteaKey);
                data = temp.ToArray();
            }

            //Write the compressed (and possibly encrypted) data
            stream.Write(data, 0, data.Length);

            PrintByteArray(data);

            //Write out the optional version value
            if(GetVersion() != -1)
                stream.WriteShort(GetVersion());

            Debug("\t\t\tENCODED Container, stream len: " + stream.Length);
            PrintInfo();

            //Finally, flip the buffer and return it
            return stream.Flip();
        }

        /// <summary>
        /// Constructs a new <see cref="RSContainer"/> from the stream data
        /// </summary>
        /// <param name="stream">The raw container data.</param>
        /// <param name="xteaKey">Optional XTEA key used to decrypt the payload.</param>
        /// <returns>The new container, or <c>null</c> if <paramref name="stream"/> is null.</returns>
        public static RSContainer Decode(JagStream stream, int[] xteaKey = null) {
            if(stream == null)
                return null;

            RSContainer container = new RSContainer();

            container.SetCompressionType(stream.ReadUnsignedByte());
            container.SetDecompressedLength(stream.ReadInt());

            int payloadLength = (int)(stream.Length - stream.Position);
            byte[] payload = stream.ReadBytes(payloadLength);

            int version = -1;
            Span<byte> encrypted = payload;

            // Attempt to remove a trailing version value if present.
            byte[] dataPortion = payload;
            if (payloadLength > 2)
            {
                dataPortion = payload.AsSpan(0, payloadLength - 2).ToArray();
                Span<byte> testBuf = dataPortion;
                if (xteaKey != null)
                {
                    JagStream t = new JagStream(dataPortion);
                    XTEA.Decipher(t, 0, t.Length, xteaKey);
                    testBuf = t.ToArray();
                }

                byte[] test = container.GetCompressionType() switch
                {
                    RSConstants.BZIP2_COMPRESSION => CompressionUtils.Bunzip2(testBuf.ToArray(), container.GetDecompressedLength()),
                    RSConstants.GZIP_COMPRESSION => CompressionUtils.Gunzip(testBuf.ToArray()),
                    RSConstants.NO_COMPRESSION => testBuf.ToArray(),
                    _ => throw new IOException("Invalid compression type")
                };

                if (test.Length == container.GetDecompressedLength())
                {
                    encrypted = dataPortion;
                    version = (payload[payloadLength - 2] << 8) | payload[payloadLength - 1];
                    payload = test;
                }
                else
                {
                    encrypted = payload;
                }
            }

            if (encrypted != payload)
            {
                // Already decrypted as part of test above
            }
            else if (xteaKey != null)
            {
                JagStream temp = new JagStream(encrypted.ToArray());
                XTEA.Decipher(temp, 0, temp.Length, xteaKey);
                encrypted = temp.ToArray();
            }

            if (payload == encrypted)
            {
                payload = container.GetCompressionType() switch
                {
                    RSConstants.BZIP2_COMPRESSION => CompressionUtils.Bunzip2(encrypted.ToArray(), container.GetDecompressedLength()),
                    RSConstants.GZIP_COMPRESSION => CompressionUtils.Gunzip(encrypted.ToArray()),
                    RSConstants.NO_COMPRESSION => encrypted.ToArray(),
                    _ => throw new IOException("Invalid compression type")
                };

                if (payload.Length != container.GetDecompressedLength())
                    throw new IOException("Length mismatch. [ " + payload.Length + " != " + container.GetDecompressedLength() + " ]");
            }

            container.SetStream(new JagStream(payload));
            container.SetVersion(version == -1 && stream.Remaining() >= 2 ? stream.ReadUnsignedShort() : version);
            container.PrintInfo();
            return container;
        }

        public string GetCompressionString() {
            return GetCompressionType() switch {
                RSConstants.BZIP2_COMPRESSION => "BZIP2",
                RSConstants.GZIP_COMPRESSION => "GZIP",
                _ => "None"
            };
        }

        private int GetDecompressedLength() {
            return decompressedLength;
        }

        private void SetDecompressedLength(int decompressedLength) {
            this.decompressedLength = decompressedLength;
        }

        private void SetVersion(int version) {
            this.version = version;
        }

        private void SetDataLength(int length) {
            this.length = length;
        }

        private int GetDataLength() {
            return length;
        }

        private void PrintInfo() {
            Debug("\t\t\tCompression type: " + GetCompressionString()
                + (stream == null ? "" : ", streamlen: " + stream.Length)
                + ", datalen: " + GetDataLength()
                + ", version: " + GetVersion(),
            LOG_DETAIL.ADVANCED);
        }

        public void SetStream(JagStream stream) {
            this.stream = stream;
        }

        private void SetCompressionType(byte compressionType) {
            this.compressionType = compressionType;
        }

        /// <summary>
        /// Returns the compression type for the container
        /// </summary>
        /// <returns>The container's compression type</returns>
        internal byte GetCompressionType() {
            return compressionType;
        }

        /// <summary>
        /// Returns the version for the container
        /// </summary>
        /// <returns>The container's version</returns>
        internal int GetVersion() {
            return version;
        }

        /// <summary>
        /// Return the stream associated with this container
        /// </summary>
        /// <returns>The container stream</returns>
        internal JagStream GetStream() {
            return stream;
        }

        internal RSArchive GetArchive() {
            return archive;
        }

        internal void SetArchive(RSArchive archive) {
            this.archive = archive;
        }

        internal void SetType(int type) {
            this.type = type;
        }

        internal void SetId(int id) {
            this.id = id;
        }

        public int GetIndexType() {
            return type;
        }

        public int GetId() {
            return id;
        }

        public int GetLength() {
            return length;
        }
    }
}
