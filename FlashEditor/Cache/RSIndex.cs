using FlashEditor.utils;
using System;

namespace FlashEditor.cache {
    //RsIndex is simply the raw data that was read from disk in to a stream
    class RSIndex {
        public const int SIZE = 6;

        private int id;
        private int size;
        private int sector;
        private JagStream stream;

        public RSIndex(int id, int size, int sector) {
            this.id = id;
            this.size = size;
            this.sector = sector;
        }

        public RSIndex() {
        }

        public RSIndex(int id, JagStream stream) {
            this.id = id;
            SetStream(stream);
        }

        public void ReadContainerHeader() {
            size = GetStream().ReadMedium();
            sector = GetStream().ReadMedium();
        }

        public static RSIndex Decode(JagStream stream) {
            RSIndex index = new RSIndex();
            index.SetStream(stream);
            index.ReadContainerHeader();
            return index;
        }

        public JagStream GetStream() {
            return stream;
        }

        public int GetId() {
            return id;
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
        public JagStream Encode() {
            JagStream s = new JagStream();
            s.WriteMedium((int) stream.Length);
            s.WriteMedium(sector);
            s.Write(stream.ToArray(), 0, stream.ToArray().Length);
            return stream.Flip();
        }
    }
}
