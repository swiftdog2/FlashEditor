using static FlashEditor.utils.DebugUtil;
using System.IO;
using System;
using FlashEditor.utils;

namespace FlashEditor.cache {
    internal class RSContainer {
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

        public JagStream Encode() {
            Debug("Encoding RSContainer " + id + ", length " + GetStream().Length);

            JagStream stream = new JagStream();

            byte[] data = GetStream().ToArray();
            int oldLen = data.Length;

            //Write the data to the stream
            if(GetCompressionType() == RSConstants.BZIP2_COMPRESSION) {
                data = CompressionUtils.Bzip2(data);
                compressionType = RSConstants.BZIP2_COMPRESSION;
            } else if(GetCompressionType() == RSConstants.GZIP_COMPRESSION) {
                data = CompressionUtils.Gzip(data);
                compressionType = RSConstants.GZIP_COMPRESSION;
            }

            Debug("Compressed " + oldLen + " to : " + data.Length);

            //Write out the compression type
            stream.WriteByte(compressionType);

            //Write the stored ("compressed") length
            stream.WriteInteger(data.Length);
            SetDataLength(data.Length);

            //If compressed, write the decompressed length also
            if(compressionType != RSConstants.NO_COMPRESSION)
                stream.WriteInteger(GetStream().Length);

            //Write the compressed data
            stream.Write(data, 0, data.Length);

            PrintByteArray(data);

            //Write out the optional version
            if(GetVersion() != -1)
                stream.WriteShort(GetVersion());

            //Optionally, encrypt with XTEAS...
            /*
            if(keys[0] != 0 || keys[1] != 0 || keys[2] != 0 || keys[3] != 0) {
                Xtea.encipher(buf, 5, compressed.length + (type == COMPRESSION_NONE ? 5 : 9), keys);
            }
            */

            Debug("\t\t\tENCODED Container, stream len: " + stream.Length);
            PrintInfo();

            //Finally, flip the buffer and return it
            return stream.Flip();
        }

        /// <summary>
        /// Constructs a new <see cref="RSContainer"/> from the stream data
        /// </summary>
        /// <param name="stream">The raw container data</param>
        /// <returns>The new container, or null if <paramref name="stream"/> is null</returns>
        public static RSContainer Decode(JagStream stream) {
            if(stream == null)
                return null;

            RSContainer container = new RSContainer();

            container.SetCompressionType(stream.ReadUnsignedByte());
            container.SetDataLength(stream.ReadInt());

            if(container.GetCompressionType() == RSConstants.NO_COMPRESSION) {
                //Simply grab the data and wrap it in a buffer
                int len = container.GetDataLength();
                Span<byte> temp = len <= 4096 ? stackalloc byte[len] : new byte[len];
                stream.Read(temp);
                container.SetStream(new JagStream(temp.ToArray()));

                container.SetVersion(stream.Remaining() >= 2 ? stream.ReadUnsignedShort() : -1); //Decode the version if present
                container.PrintInfo();

                //Return the decoded container
                return container;
            } else {
                //Grab the length of the uncompressed data
                container.SetDecompressedLength(stream.ReadInt());

                //Read the data from the stream into a buffer
                byte[] data = new byte[container.GetDataLength()];
                stream.Read(data.AsSpan());

                //Decompress the data
                if(container.GetCompressionType() == RSConstants.BZIP2_COMPRESSION)
                    data = CompressionUtils.Bunzip2(data, container.GetDecompressedLength());
                else if(container.GetCompressionType() == RSConstants.GZIP_COMPRESSION)
                    data = CompressionUtils.Gunzip(data);
                else
                    throw new IOException("Invalid compression type");

                //Check if the decompressed length is what it should be
                if(data.Length != container.GetDecompressedLength())
                    throw new IOException("Length mismatch. [ " + data.Length + " != " + container.GetDecompressedLength() + " ]");

                container.SetStream(new JagStream(data));
                container.SetVersion(stream.Remaining() >= 2 ? stream.ReadUnsignedShort() : -1); //Decode the version if present
                container.PrintInfo();
                return container;
            }
        }

        public string GetCompressionString() {
            string compressType = "None";
            if(GetCompressionType() == RSConstants.BZIP2_COMPRESSION)
                compressType = "BZIP2";
            else if(GetCompressionType() == RSConstants.GZIP_COMPRESSION)
                compressType = "GZIP";
            return compressType;
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
