using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace FlashEditor.Cache.CheckSum {
    class CheckSumEntry {
        /**
         * Represents a single entry in a {@link ChecksumTable}. Each entry contains
         * a CRC32 checksum and version of the corresponding {@link ReferenceTable}.
         * 
         * @author Graham Edgecombe
         */
        private int crc;
        private int version;
        private int files;
        private int size;
        private byte[] whirlpool;

        /**
         * Creates a new entry.
         * 
         * @param crc
         *            The CRC32 checksum of the slave table.
         * @param version
         *            The version of the slave table.
         * @param whirlpool
         *            The whirlpool digest of the reference table.
         */
        public CheckSumEntry(int crc, int version, int fileCount, int size, byte[] whirlpool) {
            if(whirlpool.Length != 64)
                throw new Exception();

            this.crc = crc;
            this.version = version;
            this.files = fileCount;
            this.size = size;
            this.whirlpool = whirlpool;
        }

        public int GetCrc() {
            return crc;
        }

        public int GetVersion() {
            return version;
        }

        public int GetFileCount() {
            return files;
        }

        public int GetSize() {
            return size;
        }

        public byte[] GetWhirlpool() {
            return whirlpool;

        }
    }
}
