using System;
using System.IO;
using System.Text;

namespace FlashEditor {
    class JagStream : MemoryStream {
        public JagStream(int size) : base(size) { }
        public JagStream(byte[] buffer) : base(buffer) { }
        public JagStream() { }

        /*
         * Stream reading utils
         */

        public long Seek0() {
            return Seek(0);
        }

        public long Seek(long offset) {
            return Seek(offset, SeekOrigin.Begin);
        }

        /*
         * Methods for reading data from the stream
         */

        internal int ReadInt() {
            return (ReadUnsignedByte() << 24) + (ReadUnsignedByte() << 16) + (ReadUnsignedByte() << 8) + ReadUnsignedByte();
        }

        internal int ReadMedium() {
            return (ReadUnsignedByte() << 16) | (ReadUnsignedByte() << 8) | ReadUnsignedByte();
        }

        internal byte ReadUnsignedByte() {
            return (byte) (ReadByte() & 0xFF);
        }

        internal int ReadUnsignedShort() {
            return (ReadByte() << 8) | ReadByte();
        }

        internal string ReadString() {
            StringBuilder sb = new StringBuilder();
            int b;
            while((b = ReadByte()) != 0)
                sb.Append((char) b);
            return sb.ToString();
        }

        /*
         * Methods for writing to the JagStream
         */

        internal void WriteString(string s) {
            foreach(char c in s.ToCharArray())
                WriteByte((byte) c);
        }

        internal void WriteShort(short value) {
            WriteByte((byte) (value & 0xFF));
            WriteByte((byte) ((value >> 8) & 0xFF));
        }

        internal void WriteShort(int value) {
            WriteShort((short) value);
        }

        internal void WriteMedium(int value) {
            WriteByte((byte) (value >> 16));
            WriteByte((byte) (value >> 8));
            WriteByte((byte) value);
        }

        internal void WriteInteger(int value) {
            WriteByte((byte) (value >> 24));
            WriteByte((byte) (value >> 16));
            WriteByte((byte) (value >> 8));
            WriteByte((byte) value);
        }

        internal void WriteInteger(long value) {
            WriteInteger((int) value);
        }

        internal int ReadShort() {
            int i = ReadUnsignedShort();
            return i > 32767 ? i -= 0x10000 : i;
        }

        /// <summary>
        /// Clears this buffer. The position is set to zero, the limit is set to the
        /// capacity, and the mark is discarded.
        /// </summary>
        public void Clear() {
            Seek0();
            SetLength(Capacity);
        }

        /// <summary>
        /// Computes the number of bytes that can be read before reaching the Length (limit)
        /// </summary>
        /// <returns>The number of bytes remaining left to be read</returns>
        public int Remaining() {
            return (int) (Length - Position);
        }

        /// <summary>
        /// The limit is set to the current position and then the position is set to zero.
        /// If the mark is defined then it is discarded.
        /// </summary>
        /// <returns>The buffer</returns>
        internal JagStream Flip() {
            SetLength(Position);
            Seek0();
            return this;
        }

        ///<summary>
        ///Writes binary data from a JagStream to a file
        /// </summary>
        /// <param name="stream">The stream to write to file</param>
        /// <param name="directory">The directory to write the file to</param>
        public static void Save(JagStream stream, string directory) {
            using(FileStream file = new FileStream(directory, FileMode.Create, FileAccess.Write)) {
                byte[] bytes = new byte[stream.Length];
                file.Write(stream.ToArray(), 0, (int) stream.Length);
                stream.Close();
            }
        }

        public void Save(string directory) {
            Save(this, directory);
        }

        public static JagStream Load(string directory) {
            byte[] bytes;
            using(FileStream file = new FileStream(directory, FileMode.Open, FileAccess.Read)) {
                byte[] stream = new byte[file.Length];
                file.Read(stream, 0, (int) file.Length);
                return new JagStream(stream);
            }
        }
    }
}
