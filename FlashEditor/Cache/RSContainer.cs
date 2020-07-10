using FlashEditor.utils;
using System;
using System.IO;

namespace FlashEditor.cache {
    internal class RSContainer {
        private JagStream stream;
        private int type;
        private int file;
        private int length;
        private byte compressionType = 0;
        private int version = -1;

        public RSContainer(byte compressionType, JagStream stream, int version, int length) {
            this.compressionType = compressionType;
            this.stream = stream;
            this.version = version;
            this.length = length;
        }

        internal static JagStream Encode(RSContainer container) {
            JagStream stream = new JagStream();

            //Write out the compression type
            stream.WriteByte(container.GetCompressionType());

            //Write out the (compressed) data length
            stream.WriteInteger(stream.Length);

            //Write out the (compressed) data
            if(container.GetCompressionType() == RSConstants.NO_COMPRESSION) {
                stream.Write(container.GetStream().ToArray(), 0, container.GetLength());
            } else {
                //Write out the decompressed length
                stream.WriteInteger(container.GetLength());

                byte[] compressedData = CompressionUtils.Gzip(container.GetStream().ToArray());
                stream.Write(compressedData, 0, container.GetLength());
            }

            //Write out the version
            stream.WriteShort(container.GetVersion());

            return stream;
        }

        /// <summary>
        /// Constructs a new <see cref="RSContainer"/> from the stream data
        /// </summary>
        /// <param name="stream">The raw container data</param>
        /// <returns>The new container, or null if <paramref name="stream"/> is null</returns>
        internal static RSContainer Decode(JagStream stream) {
            if(stream == null)
                return null;

            DebugUtil.Debug("\t\tDecoding container, stream len: " + stream.Length + ", cap: " + stream.Capacity + ", pos: " + stream.Position);
            //Decode the type and length
            byte compressType = stream.ReadUnsignedByte();
            int length = stream.ReadInt();

            if(compressType == RSConstants.NO_COMPRESSION) {
                //Simply grab the data and wrap it in a buffer
                byte[] temp = new byte[length];
                stream.Read(temp, 0, length);

                //Decode the version if present
                int containerVersion = stream.Remaining() >= 2 ? stream.ReadUnsignedShort() : -1;

                DebugUtil.Debug("\t\t\tCompression type: " + compressType + ", length: " + length + ", version: " + containerVersion);

                //Return the decoded container
                return new RSContainer(compressType, new JagStream(temp), containerVersion, length);
            } else {
                //Grab the length of the uncompressed data
                int decompressedLength = stream.ReadInt();
                if(decompressedLength <= 0)
                    return null;

                //Read the data from the stream into a buffer
                byte[] data = new byte[length];
                stream.Read(data, 0, data.Length);

                //Decompress the data
                if(compressType == RSConstants.BZIP2_COMPRESSION) {
                    data = CompressionUtils.Bunzip2(data, decompressedLength);
                } else if(compressType == RSConstants.GZIP_COMPRESSION) {
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

                DebugUtil.Debug("\t\t\tCompression type: " + compressType + ", length: " + length + ", full length: " + decompressedLength + ", version: " + containerVersion);

                //And return the decoded container
                return new RSContainer(compressType, new JagStream(data), containerVersion, length);
            }
        }

        internal int GetLength() {
            return length;
        }

        internal void SetType(int type) {
            this.type = type;
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
            return type;
        }

        internal int GetFile() {
            return file;
        }
    }
}
