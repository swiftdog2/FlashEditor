using FlashEditor.utils;
using System;
using static FlashEditor.utils.DebugUtil;

namespace FlashEditor.cache {
    /// <summary>
    /// An RSIndex represents a series of container headers
    /// Sector is the ID of the first sector that stores the container data in the dat2
    /// The stream is simply the raw data read in from the disk from one of the idx files
    /// </summary>

    class RSIndex {
        public const int SIZE = 6;

        private int size = -1;
        private int sector = -1;
        private JagStream stream;

        public RSIndex(JagStream stream) {
            this.stream = stream;
        }

        public void ReadContainerHeader(int containerId) {
            stream.Seek(containerId * SIZE); //seek to the container header position
            size = GetStream().ReadMedium();
            sector = GetStream().ReadMedium();
            Debug("Read container " + containerId + " header... size: " + size + ", sector: " + sector, LOG_DETAIL.ADVANCED);
        }

        internal JagStream Encode() {
            JagStream stream = new JagStream(SIZE);
            stream.WriteMedium(size);
            stream.WriteMedium(sector);
            return stream.Flip();
        }

        public JagStream GetStream() {
            return stream;
        }

        //The size of the container stream (in bytes)
        public int GetSize() {
            return size;
        }

        //The first sector in the dat2 in which the container data is stored
        public int GetSectorID() {
            return sector;
        }
    }
}
