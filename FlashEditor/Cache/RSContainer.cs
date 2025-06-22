using static FlashEditor.Utils.DebugUtil;
using System.IO;
using System;
using FlashEditor.Utils;
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
        /// Constructs a new <see cref="RSContainer"/> from the stream data
        /// </summary>
        /// <param name="stream">The raw container data.</param>
        /// <param name="xteaKey">Optional XTEA key used to decrypt the payload.</param>
        /// <returns>The new container, or <c>null</c> if <paramref name="stream"/> is null.</returns>
        public static RSContainer Decode(JagStream stream, int[] xteaKey = null) {
            if (stream == null)
                return null;

            RSContainer container = new RSContainer();

            container.SetCompressionType((byte) stream.ReadByte());

            String compressionName = "None";
            if (container.GetCompressionType() == RSConstants.BZIP2_COMPRESSION)
                compressionName = "BZIP2";
            if (container.GetCompressionType() == RSConstants.GZIP_COMPRESSION)
                compressionName = "GZIP";

            container.SetDataLength(stream.ReadInt());

            if (container.GetCompressionType() == RSConstants.NO_COMPRESSION)
                container.SetDecompressedLength(container.GetDataLength()); // not compressed so it will match exactly
            else
                container.SetDecompressedLength(stream.ReadInt()); //read the expected compression length

            Debug("Data Length: " + container.GetDataLength(), LOG_DETAIL.ADVANCED);
            Debug("Compression type: " + compressionName, LOG_DETAIL.ADVANCED);
            Debug("Decompressed length: " + container.GetDecompressedLength());

            byte[] payload = stream.ReadBytes(container.GetDataLength());

            if (xteaKey != null) {
                JagStream t = new JagStream(payload);
                XTEA.Decipher(t, 0, (int) t.Length, xteaKey);
                payload = t.ToArray();
            }

            payload = container.GetCompressionType() switch {
                RSConstants.BZIP2_COMPRESSION => CompressionUtils.Bunzip2(payload, container.GetDecompressedLength()),
                RSConstants.GZIP_COMPRESSION => CompressionUtils.Gunzip(payload),
                RSConstants.NO_COMPRESSION => payload,
                _ => throw new IOException("Invalid compression type")
            };

            if (payload.Length != container.GetDecompressedLength())
                throw new IOException("Length mismatch. [ " + payload.Length + " != " + container.GetDecompressedLength() + " ]");

            container.SetStream(new JagStream(payload));
            container.SetVersion(stream.Remaining() >= 2 ? stream.ReadUnsignedShort() : -1);
            container.PrintInfo();
            return container;
        }

        /// <summary>
        ///     Encodes this container to the on‑disk binary representation.
        /// </summary>
        /// <remarks>
        ///     Revision 639 uses the modern JS5 container header format:
        ///     <c>compressionType</c> (1&nbsp;byte) followed by
        ///     <c>compressedLength</c> and then, when compression is used,
        ///     <c>uncompressedLength</c>. Earlier revisions swapped the two
        ///     length fields; maintaining this order ensures compatibility with
        ///     the 639 client.
        ///     The payload may be XTEA encrypted when
        ///     <paramref name="xteaKey"/> is supplied. Multi‑file archives must
        ///     already contain the cumulative length table.
        /// </remarks>
        /// <param name="xteaKey">Optional 4&nbsp;integer XTEA key. When
        /// <c>null</c> no encryption is performed.</param>
        /// <returns>The encoded container bytes.</returns>
        public JagStream Encode(int[] xteaKey = null) {
            Debug("Encoding RSContainer " + id + ", length " + GetStream().Length);

            JagStream stream = new JagStream();

            byte[] uncompressed = GetStream().ToArray();
            int uncompressedLen = uncompressed.Length;

            compressionType = GetCompressionType();
            byte[] data = compressionType switch {
                RSConstants.BZIP2_COMPRESSION => CompressionUtils.Bzip2(uncompressed),
                RSConstants.GZIP_COMPRESSION => CompressionUtils.Gzip(uncompressed),
                _ => uncompressed
            };

            int compressedLen = data.Length;

            Debug("Compressed " + uncompressedLen + " to : " + compressedLen);

            stream.WriteByte(compressionType);
            stream.WriteInteger(compressedLen);
            if (compressionType != RSConstants.NO_COMPRESSION)
                stream.WriteInteger(uncompressedLen);

            //Optionally encrypt the payload before writing it out
            if (xteaKey != null) {
                JagStream temp = new JagStream(data);
                Cache.Util.Crypto.XTEA.Encipher(temp, 0, (int) temp.Length, xteaKey);
                data = temp.ToArray();
            }

            //Write the compressed (and possibly encrypted) data
            stream.Write(data, 0, data.Length);

            PrintByteArray(data);

            //Write out the optional version value
            if (GetVersion() != -1)
                stream.WriteShort(GetVersion());

            Debug("\t\t\tENCODED Container, stream len: " + stream.Length);
            PrintInfo();

            //Finally, flip the buffer and return it
            return stream.Flip();
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
