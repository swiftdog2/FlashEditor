﻿using FlashEditor.utils;
using System;

namespace FlashEditor.cache {
    /// <summary>
    ///     An <seealso cref="Sector" /> contains a header and data. The header contains information
    ///     used to verify the integrity of the cache like the current file id, type and
    ///     chunk. It also contains a pointer to the next sector such that the sectors
    ///     form a singly-linked list. The data is simply up to 512 bytes of the file.
    ///     @author Graham
    ///     @author `Discardedx2
    /// </summary>
    class Sector {
        public const int HEADER_LEN = 8;
        public const int DATA_LEN = 512;
        public static readonly int SIZE = HEADER_LEN + DATA_LEN;
        private readonly int chunk;
        private readonly byte[] data;
        private readonly int id;
        private readonly int nextSector;
        private readonly int type;

        public Sector(int type, int id, int chunk, int nextSector, byte[] data) {
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
        /// <returns></returns>
        public static Sector Decode(JagStream stream) {
            if(stream.Length < SIZE)
                throw new ArgumentException("Invalid sector length : " + stream.Remaining() + "/" + Sector.SIZE);

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
            int index = stream.ReadUnsignedByte();
            byte[] data = new byte[DATA_LEN];
            stream.Read(data, 0, data.Length);

            return new Sector(index, id, chunk, nextSector, data);
        }

        public new int GetType() {
            return type;
        }

        public int GetId() {
            return id;
        }

        public int GetChunk() {
            return chunk;
        }

        public int GetNextSector() {
            return nextSector;
        }

        public byte[] GetData() {
            return data;
        }

        /// <summary>
        /// Writes the Sector header
        /// </summary>
        /// <returns>A buffer containing the sector header data</returns>
        public JagStream Encode() {
            //Create a variable size JagStream
            JagStream sector = new JagStream(SIZE);

            if(id > ushort.MaxValue)
                sector.WriteInteger(id);
            else
                sector.WriteShort(id);

            sector.WriteShort((short) chunk);
            sector.WriteMedium(nextSector);
            sector.WriteByte((byte) type);
            sector.Write(data, 0, data.Length);

            return sector.Flip();
        }
    }
}