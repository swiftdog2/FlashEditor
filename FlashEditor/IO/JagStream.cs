using static FlashEditor.utils.DebugUtil;
using FlashEditor.utils;
using System;
using System.IO;
using System.Text;

namespace FlashEditor {
    public class JagStream : MemoryStream {
        public JagStream(int size) : base(size) { }
        public JagStream(byte[] buffer) : base(buffer) { }
        public JagStream() { }

        /*
         * The modified set of 'extended ASCII' characters used by the client.
         */
        private static char[] CHARACTERS = { '\u20AC', '\0', '\u201A', '\u0192', '\u201E', '\u2026', '\u2020', '\u2021',
            '\u02C6', '\u2030', '\u0160', '\u2039', '\u0152', '\0', '\u017D', '\0', '\0', '\u2018', '\u2019', '\u201C',
            '\u201D', '\u2022', '\u2013', '\u2014', '\u02DC', '\u2122', '\u0161', '\u203A', '\u0153', '\0', '\u017E',
            '\u0178' };


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

        public int ReadVarInt() {
            byte val = (byte) ReadByte();

            int k;
            for(k = 0; val < 0; val = (byte) ReadByte())
                k = (k | val & 0x7F) << 7;

            return k | val;
        }

        /// <summary>
        /// Read's smart_v1 from buffer.
        /// </summary>
        /// <returns></returns>
        public int ReadSmart() {
            int first = ReadUnsignedByte();
            Position -= 1; // go back a one.
            if(first < 128)
                return ReadUnsignedByte();
            return ReadUnsignedShort() - 32768;
        }

        public int ReadUnsignedSmart() {
            int first = ReadUnsignedByte();
            Position -= 1; // go back a one.
            return first < 128 ? ReadUnsignedByte() - 64
                    : ReadUnsignedShort() - 49152;
        }

        public int ReadSpecialSmart() {
            int first = ReadUnsignedByte();
            Position -= 1; // go back a one.
            if(first < 128)
                return ReadUnsignedByte() - 1;
            return ReadUnsignedShort() - 32769;
        }

        internal int ReadInt() {
            return (ReadUnsignedByte() << 24) + (ReadUnsignedByte() << 16) + (ReadUnsignedByte() << 8) + ReadUnsignedByte();
        }

        internal int ReadMedium() {
            return (ReadUnsignedByte() << 16) | (ReadUnsignedByte() << 8) | ReadUnsignedByte();
        }

        internal byte ReadUnsignedByte() {
            int result = ReadByte();
            if(result == -1)
                Debug("End of stream bro");

            return (byte) result;
            //return (byte) (ReadByte() & 0xFF);
        }

        internal int[] ReadUnsignedByteArray(int size) {
            byte[] byteBuffer = new byte[size];
            Read(byteBuffer, 0, byteBuffer.Length);
            return Array.ConvertAll(byteBuffer, Convert.ToInt32);
        }

        internal int ReadUnsignedShort() {
            return (ReadByte() << 8) | ReadByte();
        }

        internal int[] ReadUnsignedShortArray(int size) {
            //Read 2x length contiguous block of bytes
            byte[] byteBuffer = new byte[size * 2];
            Read(byteBuffer, 0, byteBuffer.Length);

            int[] shortBuffer = new int[size];

            int k = 0;

            //Recombine into shorts
            for(int i = 0; i < size; i++) {
                shortBuffer[i] = (byteBuffer[k] << 8) | byteBuffer[k + 1];
                k += 2;
            }

            return shortBuffer;
        }

        internal string ReadString2() {
            StringBuilder sb = new StringBuilder();
            int b;

            while((b = ReadByte()) != 0)
                sb.Append((char) b);

            WriteLine("'");
            return sb.ToString();
        }

        /**
         * Gets a null-terminated string from the specified buffer, using a
         * modified ISO-8859-1 character set.
         * @param buf The buffer.
         * @return The decoded string.
         */
        public string ReadJagexString() {
            StringBuilder sb = new StringBuilder();
            int b;
            while((b = ReadByte()) != 0) {
                //If the byte read is between 127 and 159, it should be remapped
                if(b >= 127 && b < 160) {
                    char c = CHARACTERS[b - 128];
                    if(c.Equals('\0')) { //if it needs to be remapped, as per the characters array
                        c = '\u003F'; //replace with question mark as placeholder to avoid rendering issues
                    }
                    sb.Append(c);
                } else {
                    sb.Append((char) b);
                }
            }

            return sb.ToString();
        }

        internal byte Get(int pos) {
            //Remember the current position
            long tempPosition = Position;

            //Move the the desired position
            Seek(pos);
            //Read the byte
            byte x = (byte) ReadByte();

            //Reset the position
            //Position = tempPosition;

            //Return the byte read at position v
            return x;
        }

        internal void Skip(int skip) {
            Position += skip;
        }

        //Seems to work better? idk tbh my brain is fried...
        public string ReadFlashString() {
            StringBuilder sb = new StringBuilder();
            int b;
            while((b = ReadByte()) != 0) {
                if(b >= 128 && b < 160) {
                    char c = CHARACTERS[b - 128];
                    sb.Append(c == (char) 0 ? (char) 63 : c);
                } else {
                    //Nobody seems to have this (including client, openrs, etc)
                    //Seems to eliminate issues reading strings not terminated by 0
                    if(b < 32) {
                        //But also should we reduce the position because we over-read the stream?
                        Position--;
                        break;
                    }

                    sb.Append((char) b);
                }
            }

            return sb.ToString();
        }

        /*
         * Methods for writing to the JagStream
         */

        internal void WriteUByte(byte b) {
            WriteByte((byte) (b & 0xFF));
        }

        internal void WriteString(string s) {
            foreach(char c in s.ToCharArray())
                WriteByte((byte) c);

            //terminate the string with 0
            WriteByte(0);

            //apparently 317 format is terminated with 10
        }

        /**
         * Interesting method ripped from kfricilone's openRS
         * Converts the contents of the specified byte buffer to a string, which is
        * formatted similarly to the output of the {@link Arrays#toString()}
        * method.
        * 
        * @param buffer
        *            The buffer.
        * @return The string.
        */
        /*
        public static String toString(ByteBuffer buffer) {
            StringBuilder builder = new StringBuilder("[");
            for(int i = 0; i < buffer.limit(); i++) {
                String hex = Integer.toHexString(buffer.get(i) & 0xFF).toUpperCase();
                if(hex.length() == 1)
                    hex = "0" + hex;

                builder.append("0x").append(hex);
                if(i != buffer.limit() - 1) {
                    builder.append(", ");
                }
            }
            builder.append("]");
            return builder.toString();
        }
        */

        internal void WriteShort(short value) {
            byte[] bytes = { (byte) (value & 0xFF), (byte) ((value >> 8) & 0xFF) };
            Write(bytes, 0, 2);
        }

        internal void WriteShort(int value) {
            WriteShort((short) value);
        }

        internal void WriteMedium(int value) {
            byte[] bytes = { (byte) (value >> 16) , (byte) (value >> 8) , (byte) value };
            Write(bytes, 0, 3);
        }

        internal void WriteVarInt(int var63) {
            throw new NotImplementedException();
        }

        internal void WriteInteger(int value) {
            byte[] bytes = { (byte) (value >> 24), (byte) (value >> 16), (byte) (value >> 8), (byte) value };
            Write(bytes, 0, 4);
        }

        internal void WriteInteger(long value) {
            WriteInteger((int) value);
        }

        internal int ReadShort() {
            int i = ReadUnsignedShort();
            return i > 32767 ? i -= 0x10000 : i;
        }

        internal void PutLengthFromMark(long v) {
            throw new NotImplementedException();
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

        internal void put(object v) {
            throw new NotImplementedException();
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
            if(stream == null)
                throw new NullReferenceException("Stream was null");

            using(FileStream file = new FileStream(directory, FileMode.Create, FileAccess.Write)) {
                byte[] bytes = new byte[stream.Length];
                file.Write(stream.ToArray(), 0, (int) stream.Length);
            }
        }

        public void Save(string directory) {
            Save(this, directory);
        }

        public static JagStream Load(string directory) {
            using(FileStream file = new FileStream(directory, FileMode.Open, FileAccess.Read)) {
                byte[] stream = new byte[file.Length];
                file.Read(stream, 0, (int) file.Length);
                return new JagStream(stream);
            }
        }
    }
}
