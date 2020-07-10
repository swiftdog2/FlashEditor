using System;

namespace FlashEditor.cache {
    class RSIndex {
        public const int SIZE = 6;

        private int size;
        private int sector;
        private JagStream stream;

        public RSIndex(int size, int sector) {
            this.size = size;
            this.sector = sector;
        }

        public RSIndex() {
        }

        public RSIndex(JagStream stream) {
            SetStream(stream);
        }

        public void ReadHeader() {
            this.size = GetStream().ReadMedium();
            this.sector = GetStream().ReadMedium();
        }

        public ref JagStream GetStream() {
            return ref stream;
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
    }
}
