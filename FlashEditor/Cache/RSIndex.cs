using FlashEditor.Utils;
using System;
using static FlashEditor.Utils.DebugUtil;
using FlashEditor.IO;

namespace FlashEditor.Cache {
    /// <summary>
    /// An RSIndex represents a series of archive headers.
    /// Sector is the ID of the first sector that stores the archive data in the dat2.
    /// The stream is simply the raw data read in from the disk from one of the idx files.
    /// </summary>

    class RSIndex {
        public const int SIZE = 6;

        private int size = -1;
        private int sector = -1;
        private JagStream stream;

        public RSIndex(JagStream stream) {
            this.stream = stream;
        }

        /// <summary>
        ///     Reads a six byte index entry for the given archive.
        ///     The entry is encoded big-endian as
        ///     <c>[length:3][sector:3]</c>.
        /// </summary>
        /// <param name="archiveId">Zero-based id of the archive.</param>
        public void ReadContainerHeader(int archiveId) {
            // seek to the archive header position
            stream.Seek(archiveId * SIZE);
            size = GetStream().ReadMedium();
            sector = GetStream().ReadMedium();
            Debug("Read archive " + archiveId + " header... size: " + size + ", sector: " + sector, LOG_DETAIL.ADVANCED);
        }

        /// <summary>Encodes the current record to a stream.</summary>
        internal JagStream Encode() {
            JagStream stream = new JagStream(SIZE);
            stream.WriteMedium(size);
            stream.WriteMedium(sector);
            return stream.Flip();
        }

        /// <summary>Gets the underlying data stream.</summary>
        public JagStream GetStream() {
            return stream;
        }

        /// <summary>Size of the archive's data in bytes.</summary>
        public int GetSize() {
            return size;
        }

        /// <summary>First sector id containing the archive data.</summary>
        public int GetSectorID() {
            return sector;
        }
    }
}
