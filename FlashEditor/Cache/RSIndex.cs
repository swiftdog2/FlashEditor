using FlashEditor.utils;
using System;
using static FlashEditor.utils.DebugUtil;

namespace FlashEditor.cache {
    //RsIndex is simply the raw data that was read from disk in to a stream
    class RSIndex {
        public const int SIZE = 6;

        private int size = -1;
        private int sector = -1;
        private JagStream stream;

        public RSIndex(JagStream stream) {
            SetStream(stream);
        }

        public void ReadContainerHeader() {
            Debug("Reading container header...", LOG_DETAIL.INSANE);
            size = GetStream().ReadMedium();
            sector = GetStream().ReadMedium();
        }

        public static RSIndex Decode(JagStream stream) {
            RSIndex index = new RSIndex(stream);
            index.ReadContainerHeader();
            return index;
        }

        public JagStream GetStream() {
            return stream;
        }

        public int GetSize() {
            return size;
        }

        public int GetSectorID() {
            return sector;
        }

        internal void SetStream(JagStream stream) {
            this.stream = stream;
        }

        internal JagStream Encode() {
            JagStream stream = new JagStream(SIZE);
            stream.WriteMedium(size);
            stream.WriteMedium(sector);
            return stream.Flip();
        }
    }
}
