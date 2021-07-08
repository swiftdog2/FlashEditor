using FlashEditor.utils;
using System;
using System.IO;

namespace FlashEditor.cache {
    internal class Container {
        private JagStream stream;
        private int index;
        private int file;
        private int length;
        private byte compressionType = 0;
        private int version = -1;
        int decompressedLength = -1;

        public Container(byte compressionType, JagStream stream, int version, int length, int decompressedLength) {
            this.compressionType = compressionType;
            this.stream = stream;
            this.version = version;
            this.length = length;
            this.decompressedLength = decompressedLength;
        }

        public Container(int index, JagStream stream) {
            this.index = index;
            this.stream = stream;
        }

        public void UpdateStream(JagStream stream) {
            this.stream = stream;
        }

        internal JagStream Encode() {
            DebugUtil.Debug("Encoding RSContainer... type " + index + " file " + file + ", length " + length);

            JagStream stream = new JagStream();

            //Write out the compression type
            stream.WriteByte(GetCompressionType());

            //Write out the (compressed) data length
            stream.WriteInteger(stream.Length);

            //Write the data to the stream
            if(GetCompressionType() == Constants.NO_COMPRESSION) {
                stream.Write(GetStream().ToArray(), 0, GetLength());
            } else {
                //Write the decompressed length
                stream.WriteInteger(decompressedLength);

                /*
                 * TODO: add BZIP compression
                 */
                byte[] compressedData = CompressionUtils.Gzip(GetStream().ToArray());
                stream.Write(compressedData, 0, GetLength());
            }

            //Write out the version
            if(version != -1)
                stream.WriteShort(GetVersion());

            //Optionally, encrypt with XTEAS...

            //Finally, flip the buffer and return it
            return stream.Flip();
        }

        /// <summary>
        /// Constructs a new <see cref="Container"/> from the stream data
        /// </summary>
        /// <param name="stream">The raw container data</param>
        /// <returns>The new container, or null if <paramref name="stream"/> is null</returns>
        internal static Container Decode(JagStream stream) {
            if(stream == null)
                return null;

            DebugUtil.Debug("\t\tDecoding container, stream len: " + stream.Length + ", cap: " + stream.Capacity + ", pos: " + stream.Position);
            //Decode the type and length
            byte compressType = stream.ReadUnsignedByte();
            int length = stream.ReadInt();

            if(compressType == Constants.NO_COMPRESSION) {
                //Simply grab the data and wrap it in a buffer
                byte[] temp = new byte[length];
                stream.Read(temp, 0, length);

                //Decode the version if present
                int containerVersion = stream.Remaining() >= 2 ? stream.ReadUnsignedShort() : -1;

                DebugUtil.Debug("\t\t\tCompression type: " + compressType + ", length: " + length + ", version: " + containerVersion);

                //Return the decoded container
                return new Container(compressType, new JagStream(temp), containerVersion, length, -1);
            } else {
                //Grab the length of the uncompressed data
                int decompressedLength = stream.ReadInt();

                /*
                 * //shouldnt happen tbh
                if(decompressedLength <= 0)
                    return null;*/

                //Read the data from the stream into a buffer
                byte[] data = new byte[length];
                stream.Read(data, 0, data.Length);

                //Decompress the data
                if(compressType == Constants.BZIP2_COMPRESSION) {
                    data = CompressionUtils.Bunzip2(data, decompressedLength);
                } else if(compressType == Constants.GZIP_COMPRESSION) {
                    data = CompressionUtils.Gunzip(data);
                } else {
                    throw new IOException("Invalid compression type");
                }

                //Check if the decompressed length is what it should be
                if(data.Length != decompressedLength)
                    throw new IOException("Length mismatch. [ " + data.Length + " != " + decompressedLength + " ]");

                //Decode the version if present
                int containerVersion = -1;
                if(stream.Remaining() >= 2)
                    containerVersion = stream.ReadUnsignedShort();

                string compressionName = "none";
                if(compressType == Constants.BZIP2_COMPRESSION)
                    compressionName = "BZIP2";
                else if(compressType == Constants.GZIP_COMPRESSION)
                    compressionName = "GZIP";

                DebugUtil.Debug("\t\t\tCompression type: " + compressionName + ", length: " + length + ", full length: " + decompressedLength + ", version: " + containerVersion);

                //And return the decoded container
                return new Container(compressType, new JagStream(data), containerVersion, length, decompressedLength);
            }
        }

        internal int GetLength() {
            return length;
        }

        internal void SetType(int type) {
            this.index = type;
        }

        internal void SetFile(int file) {
            this.file = file;
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

        internal new int GetType() {
            return index;
        }

        internal int GetFile() {
            return file;
        }
    }
}
