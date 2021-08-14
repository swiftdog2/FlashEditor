using static FlashEditor.utils.DebugUtil;
using System.IO;
using System;

namespace FlashEditor.cache {
    internal class RSContainer {
        private JagStream stream;
        public int type;
        public int id;
        public int length;
        public byte compressionType = 0;
        public int version = -1;
        public int decompressedLength = -1;

        //The archive that is represented by the container
        public RSArchive archive;

        public RSContainer(byte compressionType, JagStream stream, int version, int length, int decompressedLength) {
            this.compressionType = compressionType;
            this.stream = stream;
            this.version = version;
            this.length = length;
            this.decompressedLength = decompressedLength;
        }

        public RSContainer(byte compressionType, JagStream stream, int version) {
            this.compressionType = compressionType;
            this.stream = stream;
            this.version = version;
        }

        public RSContainer(JagStream stream, int version) {
            this.stream = stream;
            this.version = version;
        }

        public RSContainer(int compressType, JagStream stream) {
            this.stream = stream;
        }

        public JagStream Encode() {
            Debug("Encoding RSContainer " + id + ", length " + this.stream.Length);

            JagStream stream = new JagStream();

            byte[] compressed;

            //Write the data to the stream
            if(GetCompressionType() == RSConstants.NO_COMPRESSION) {
                compressed = GetStream().ToArray();
            } else {
                //TODO: add BZIP compression
                if(GetCompressionType() == RSConstants.BZIP2_COMPRESSION)
                    Debug("BZIP2 Unavailable, GZipping instead...");

                //Gzip compression type instead of bzip until fixed
                compressed = CompressionUtils.Gzip(GetStream().ToArray());
                compressionType = RSConstants.GZIP_COMPRESSION;
            }

            //Write out the compression type
            stream.WriteByte(GetCompressionType());

            Debug("\tCompression type: " + GetCompressionType(), LOG_DETAIL.INSANE);

            //Write the stored ("uncompressed") length
            stream.WriteInteger(compressed.Length);

            //If compressed, write the decompressed length also
            if(GetCompressionType() != RSConstants.NO_COMPRESSION)
                stream.WriteInteger(stream.Length);

            //Write the compressed data
            stream.Write(compressed, 0, compressed.Length);

            //Write out the optional version
            if(version != -1)
                stream.WriteShort(GetVersion());

            //Optionally, encrypt with XTEAS...
            /*
            if(keys[0] != 0 || keys[1] != 0 || keys[2] != 0 || keys[3] != 0) {
                Xtea.encipher(buf, 5, compressed.length + (type == COMPRESSION_NONE ? 5 : 9), keys);
            }
            */

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

            //Decode the type and length
            byte compressType = stream.ReadUnsignedByte(); //1 
            int length = stream.ReadInt(); //1+4=5
            Debug("Decoding container, Compression type: " + compressType + ", length: " + length, LOG_DETAIL.ADVANCED);

            if(compressType == RSConstants.NO_COMPRESSION) {
                //Simply grab the data and wrap it in a buffer
                byte[] temp = new byte[length];
                stream.Read(temp, 0, length);

                //Decode the version if present
                int containerVersion = stream.Remaining() >= 2 ? stream.ReadUnsignedShort() : -1; //5+2=7

                Debug("\t\tCompression type: " + compressType + ", length: " + length + ", version: " + containerVersion, LOG_DETAIL.INSANE);
                //Return the decoded container
                return new RSContainer(compressType, new JagStream(temp), containerVersion, length, -1);
            } else {
                //Grab the length of the uncompressed data
                int decompressedLength = stream.ReadInt(); //5+4=9 byte header for compressed data

                //Read the data from the stream into a buffer
                byte[] data = new byte[length];
                stream.Read(data, 0, data.Length);

                //Decompress the data
                if(compressType == RSConstants.BZIP2_COMPRESSION)
                    data = CompressionUtils.Bunzip2(data, decompressedLength);
                else if(compressType == RSConstants.GZIP_COMPRESSION)
                    data = CompressionUtils.Gunzip(data);
                else
                    throw new IOException("Invalid compression type");

                //Check if the decompressed length is what it should be
                if(data.Length != decompressedLength)
                    throw new IOException("Length mismatch. [ " + data.Length + " != " + decompressedLength + " ]");

                //Decode the version if present
                int containerVersion = -1;
                if(stream.Remaining() >= 2)
                    containerVersion = stream.ReadUnsignedShort(); //9+2=11 byte

                string compressionName = "none";
                if(compressType == RSConstants.BZIP2_COMPRESSION)
                    compressionName = "BZIP2";
                else if(compressType == RSConstants.GZIP_COMPRESSION)
                    compressionName = "GZIP";
                Debug("\t\t\tCompression type: " + compressionName + ", length: " + length + ", full length: " + decompressedLength + ", version: " + containerVersion, LOG_DETAIL.ADVANCED);

                //And return the decoded container
                return new RSContainer(compressType, new JagStream(data), containerVersion, length, decompressedLength);
            }
        }

        internal int GetLength() {
            return length;
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
    }
}
