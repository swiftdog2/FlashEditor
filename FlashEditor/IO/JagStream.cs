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

        private static readonly StringBuilder SharedBuilder = new StringBuilder();

        /*
         * The modified set of 'extended ASCII' characters used by the client.
         */
        private static char[] CHARACTERS = { '\u20AC', '\0', '\u201A', '\u0192', '\u201E', '\u2026', '\u2020', '\u2021',
            '\u02C6', '\u2030', '\u0160', '\u2039', '\u0152', '\0', '\u017D', '\0', '\0', '\u2018', '\u2019', '\u201C',
            '\u201D', '\u2022', '\u2013', '\u2014', '\u02DC', '\u2122', '\u0161', '\u203A', '\u0153', '\0', '\u017E',
            '\u0178' };

        /// <summary>Maps Unicode → RuneScape extended byte (128-159).</summary>
        private static readonly Dictionary<char, byte> EXTENDED_REMAP = BuildReverse();

        private static Dictionary<char, byte> BuildReverse()
        {
            var map = new Dictionary<char, byte>(32);
            for (int i = 0; i < CHARACTERS.Length; i++)
                if (CHARACTERS[i] != '\0')
                    map[CHARACTERS[i]] = (byte)(i + 128);
            return map;
        }

        /*
         * Loading stream from file
         */
        public static JagStream LoadStream(string directory) {
            if(!File.Exists(directory))
                throw new FileNotFoundException("'" + directory + "' could not be found.");

            byte[] data = File.ReadAllBytes(directory);
            if(data.Length == 0)
                Debug("No data read for directory: " + directory);

            //We initialise it like this to ensure the stream is expandable
            JagStream stream = new JagStream();
            stream.Write(data, 0, data.Length);
            return stream;
        }

        /// <summary>
        /// The limit is set to the current position and then the position is set to zero.
        /// If the mark is defined then it is discarded.
        /// </summary>
        /// <returns>The buffer</returns>
        public JagStream Flip() {
            if(Position == 0 && Length > 0)
                throw new IOException("Idiot you're destroying the stream");
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

            string dirName = Path.GetDirectoryName(directory);
            if(!string.IsNullOrEmpty(dirName) && !Directory.Exists(dirName))
                Directory.CreateDirectory(dirName);

            using(FileStream file = new FileStream(directory, FileMode.Create, FileAccess.Write))
                file.Write(stream.ToArray(), 0, (int) stream.Length);
        }

        public void Save(string directory) {
            Save(this, directory);
        }

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

        public int ReadInt() {
            return (ReadUnsignedByte() << 24) + (ReadUnsignedByte() << 16) + (ReadUnsignedByte() << 8) + ReadUnsignedByte();
        }

        internal int ReadMedium() {
            return (ReadUnsignedByte() << 16) + (ReadUnsignedByte() << 8) + ReadUnsignedByte();
        }

        internal byte ReadUnsignedByte() {
            int result = ReadByte();
            if(result == -1)
                throw new EndOfStreamException("End of stream bro");

            return (byte) result;
        }

        internal int[] ReadUnsignedByteArray(int size) {
            Span<byte> byteBuffer = size <= 1024 ? stackalloc byte[size] : new byte[size];
            Read(byteBuffer);

            int[] result = new int[size];
            for (int i = 0; i < size; i++)
                result[i] = byteBuffer[i];

            return result;
        }

        // ➊  add this inside the JagStream class, anywhere among the other read helpers
        /// <summary>
        /// Reads a signed 8-bit value (-128 … 127) from the stream.
        /// Java’s <c>readSignedByte</c> equivalent.
        /// </summary>
        internal int ReadSignedByte()
        {
            int b = ReadByte();          // 0-255
            if (b == -1)
                throw new EndOfStreamException("End of stream");
            return (sbyte)b;             // cast preserves the sign
        }

        public int ReadUnsignedShort() {
            return (ReadByte() << 8) | ReadByte();
        }
        public int[] ReadUnsignedShortArray(int size) {
            Span<byte> byteBuffer = size * 2 <= 2048 ? stackalloc byte[size * 2] : new byte[size * 2];
            Read(byteBuffer);

            int[] shortBuffer = new int[size];
            int k = 0;
            for (int i = 0; i < size; i++) {
                shortBuffer[i] = (byteBuffer[k] << 8) | byteBuffer[k + 1];
                k += 2;
            }

            return shortBuffer;
        }

        internal string ReadString2() {
            SharedBuilder.Clear();
            int b;

            while((b = ReadByte()) != 0)
                SharedBuilder.Append((char) b);

            WriteLine("'");
            return SharedBuilder.ToString();
        }

        /**
         * Gets a null-terminated string from the specified buffer, using a
         * modified ISO-8859-1 character set.
         * @param buf The buffer.
         * @return The decoded string.
         */
        public string ReadJagexString() {
            SharedBuilder.Clear();
            int b;
            while((b = ReadByte()) != 0) {
                //If the byte read is between 127 and 159, it should be remapped
                if(b >= 127 && b < 160) {
                    char c = CHARACTERS[b - 128];
                    if(c == '\0') //if it needs to be remapped, as per the characters array
                        c = '\u003F'; //replace with question mark as placeholder to avoid rendering issues
                    SharedBuilder.Append(c);
                } else {
                    SharedBuilder.Append((char) b);
                }
            }

            return SharedBuilder.ToString();
        }

        internal byte Get(int pos) {
            //Store the current position
            long tempPosition = Position;

            //Move the the desired position
            Seek(pos);

            byte x = (byte) ReadByte();

            //Reset the position
            Position = tempPosition;

            return x;
        }

        internal void Skip(int skip) {
            Position += skip;
        }

        //Seems to work better? idk tbh my brain is fried...
        public string ReadFlashString() {
            SharedBuilder.Clear();
            int b;
            while((b = ReadByte()) != 0) {
                if(b >= 128 && b < 160) {
                    char c = CHARACTERS[b - 128];
                    SharedBuilder.Append(c == (char) 0 ? (char) 63 : c);
                } else {
                    //Nobody seems to have this (including client, openrs, etc)
                    //Seems to eliminate issues reading strings not terminated by 0
                    if(b < 32) {
                        //But also should we reduce the position because we over-read the stream?
                        Position--;
                        break;
                    }

                        SharedBuilder.Append((char) b);
                }
            }

            return SharedBuilder.ToString();
        }

        /// <summary>
        /// Writes a 0-terminated Jagex string using the cache’s
        /// modified CP-1252 encoding (bytes 0-255).
        /// </summary>
        public void WriteJagexString(string s)
        {
            foreach (char c in s)
            {
                if (c == 0)
                    continue;                 // never embed a NUL
                if (c < 128 || (c >= 160 && c <= 255))
                    WriteByte((byte)c);               // plain ASCII or 0xA0-0xFF
                else if (EXTENDED_REMAP.TryGetValue(c, out byte b))
                    WriteByte(b);                     // 0x80-0x9F mapping
                else
                    WriteByte((byte)'?');             // fallback
            }
            WriteByte(0);                            // terminator
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

        /// <summary>
        /// Writes a Java‐style signed byte (-128..127) into the stream.
        /// </summary>
        public void WriteSignedByte(sbyte value)
        {
            // cast to byte will wrap negative values into their two’s‐complement 0..255 form
            WriteByte((byte)value);
        }

        internal void WriteBytes(int bytes, object value) {
            Span<byte> data = bytes <= 8 ? stackalloc byte[bytes] : new byte[bytes];

            int val = value switch {
                int i => i,
                short s => (ushort)s,
                _ => 0
            };

            for (int i = 0; i < bytes; i++)
                data[i] = (byte)(val >> (8 * (bytes - i - 1)));

            Write(data);
        }

        public void WriteShort(short value) {
            WriteBytes(2, value);
        }

        public void WriteShort(int value) {
            WriteShort((short) value);
        }

        public void WriteMedium(int value) {
            WriteBytes(3, value);
        }

        public void WriteMedium(long value) {
            WriteBytes(3, (int) value);
        }

        public void WriteVarInt(int var63) {
            throw new NotImplementedException();
        }

        public void WriteInteger(int value) {
            WriteBytes(4, value);
        }

        public void WriteInteger(long value) {
            WriteInteger((int) value);
        }

        public int ReadShort() {
            int i = ReadUnsignedShort();
            return i > 32767 ? i -= 0x10000 : i;
        }

        //Returns a substream starting at ptr with length bytes

        public JagStream GetSubStream(int length, long ptr) {
            Seek(ptr);
            return GetSubStream(length);
        }

        public JagStream GetSubStream(int length) {
            return new JagStream(ReadBytes(length));
        }

        public byte[] ReadBytes(long length) {
            return ReadBytes((int) length);
        }

        public byte[] ReadBytes(int length) {
            byte[] buf = new byte[length];
            for(int k = 0; k < length; k++)
                buf[k] = (byte) ReadByte();

            return buf;
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
    }
}
