using System;

namespace FlashEditor.cache {
    //RsIndex is simply the raw data that was read from disk in to a stream
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
            size = GetStream().ReadMedium();
            sector = GetStream().ReadMedium();
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

        /// <summary>
        /// Read the index data
        /// </summary>
        /// <param name="indexId">The index type</param>
        /// <param name="containerId">The container index</param>
        /// <returns>A <c>JagStream</c> containing the container data</returns>
        internal JagStream Encode() {
            JagStream s = new JagStream();
            s.WriteMedium(size);
            s.WriteMedium(sector);
            s.Write(stream.ToArray(), 0, stream.ToArray().Length);
            return stream.Flip();
        }
    }
}
