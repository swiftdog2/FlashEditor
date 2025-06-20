using static FlashEditor.utils.DebugUtil;
using System;

namespace FlashEditor.cache {
    /// <summary>
    ///     An <seealso cref="RSSector" /> contains a header and data. The header contains information
    ///     used to verify the integrity of the cache like the current file id, type and
    ///     chunk. It also contains a pointer to the next sector such that the sectors
    ///     form a singly-linked list. The data is simply up to 512 bytes of the file.
    /// </summary>
    class RSSector {
        public const int HEADER_LEN = 8;
        public const int DATA_LEN = 512;
        public static readonly int SIZE = HEADER_LEN + DATA_LEN;
        private int chunk;
        private byte[] data;
        private int id;
        private int nextSector;
        private int type;

        /// <summary>
        /// Constructs a sector record.
        /// </summary>
        /// <param name="type">Index the sector belongs to.</param>
        /// <param name="id">Container id.</param>
        /// <param name="chunk">Chunk number within the container.</param>
        /// <param name="nextSector">Pointer to the next sector.</param>
        /// <param name="data">Sector payload.</param>
        public RSSector(int type, int id, int chunk, int nextSector, byte[] data) {
            this.type = type;
            this.id = id;
            this.chunk = chunk;
            this.nextSector = nextSector;
            this.data = data;
        }

        /// <summary>
        /// Reads the 8-byte Sector header
        /// </summary>
        /// <param name="stream">The stream to read from</param>
        /// <returns>Decoded sector instance.</returns>
        public static RSSector Decode(JagStream stream) {
            if(stream.Length < SIZE)
                throw new ArgumentException("Invalid sector length : " + stream.Length + "/" + SIZE);

            /*
             * Information  Type	            Description
             * File ID      Unsigned Short	    The file that this Sector belongs to
             * Chunk ID     Unsigned Short	    Which chunk of the file the data of the Sector is
             * Sector ID    Medium (3 Bytes)	Which Sector of the data file this is
             * Type ID      Unsigned Byte	    The type of file this Sector belongs to
             * Data         512 Bytes	        The raw data that this Section contains
             */

            int id = stream.ReadUnsignedShort();
            int chunk = stream.ReadUnsignedShort();
            int nextSector = stream.ReadMedium();
            int type = stream.ReadByte();
            byte[] data = new byte[DATA_LEN];
            stream.Read(data, 0, data.Length);

            return new RSSector(type, id, chunk, nextSector, data);
        }


        /// <summary>
        /// Writes this sector to a stream.
        /// </summary>
        /// <returns>Buffer containing the encoded sector.</returns>
        public JagStream Encode() {
            JagStream stream = new JagStream(SIZE);
            stream.WriteShort(id);
            stream.WriteShort(chunk);
            stream.WriteMedium(nextSector);
            stream.WriteByte((byte) type);
            stream.Write(data, 0, data.Length);
            return stream.Flip();
        }

        /// <summary>Gets the sector type (index).</summary>
        public new int GetType() {
            return type;
        }

        /// <summary>Container id for this sector.</summary>
        public int GetId() {
            return id;
        }

        /// <summary>Chunk number within the container.</summary>
        public int GetChunk() {
            return chunk;
        }

        /// <summary>Next sector pointer or zero.</summary>
        public int GetNextSector() {
            return nextSector;
        }

        /// <summary>Raw sector data.</summary>
        public byte[] GetData() {
            return data;
        }
    }
}
