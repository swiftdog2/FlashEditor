using FlashEditor.utils;
using System;
using System.Buffers;
using System.IO;
using System.Text;
using static FlashEditor.utils.DebugUtil;

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

        /// <summary>
        /// Reads a variable-length int exactly like Java’s “var-int”.
        /// Each byte contributes 7 data bits; the MSB is the “more” flag.
        /// </summary>
        public int ReadVarInt() {
            int value = 0;
            int shift = 0;
            byte chunk;

            do
            {
                chunk = ReadUnsignedByte();              // 0-255, never negative
                value |= (chunk & 0x7F) << shift;         // data bits
                shift += 7;
            }
            while ((chunk & 0x80) != 0);                  // MSB set ⇒ more bytes

            return value;
        }

        /// <summary>
        /// Read's smart_v1 from buffer.
        /// </summary>
        /// <returns></returns>
        public int ReadSmart()
        {
            int peek = GetBuffer()[Position];          // no advance
            if (peek < 128)
                return ReadUnsignedByte();         // consumes one
            return ReadUnsignedShort() - 32768;    // consumes two
        }

        public int ReadUnsignedSmart()
        {
            int peek = GetBuffer()[Position];
            return peek < 128 ? ReadUnsignedByte() - 64
                              : ReadUnsignedShort() - 49152;
        }

        public int ReadSpecialSmart()
        {
            int peek = GetBuffer()[Position];
            return peek < 128 ? ReadUnsignedByte() - 1
                              : ReadUnsignedShort() - 32769;
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

        /// <summary>
        /// Returns the next byte in the stream <strong>without</strong> advancing
        /// <see cref="Position"/>. The value is returned as an unsigned
        /// 8-bit integer (0-255). Throws <see cref="EndOfStreamException"/>
        /// if the current position is already at or beyond <see cref="Length"/>.
        /// </summary>
        internal int PeekUnsignedByte()
        {
            if (Position >= Length)
                throw new EndOfStreamException("Peek beyond end of JagStream.");

            // MemoryStream exposes its backing buffer; safe because the
            // RuneScape cache always loads into a contiguous byte[].
            return GetBuffer()[Position] & 0xFF;
        }


        internal int[] ReadUnsignedByteArray(int size)
        {
            byte[]? rented = null;
            Span<byte> span = size <= 1024
                ? stackalloc byte[size]                     // on the stack
                : (rented = ArrayPool<byte>.Shared.Rent(size)).AsSpan(0, size);

            int read = Read(span);                          // MemoryStream.Read
            if (read != size)
                throw new EndOfStreamException($"Needed {size} bytes, got {read}");

            int[] result = new int[size];
            for (int i = 0; i < size; i++)
                result[i] = span[i];

            if (rented != null)
                ArrayPool<byte>.Shared.Return(rented);      // return to pool
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

        /// <summary>
        /// Reads <paramref name="size"/> unsigned 16-bit values (big-endian) into an int[]
        /// without allocating on the LOH.
        /// </summary>
        public int[] ReadUnsignedShortArray(int size)
        {
            int byteCount = size * 2;

            byte[]? rent = null;
            Span<byte> span = byteCount <= 2048
                ? stackalloc byte[byteCount]                     // on the stack
                : (rent = ArrayPool<byte>.Shared.Rent(byteCount)).AsSpan(0, byteCount);

            try
            {
                int read = Read(span);
                if (read != byteCount)
                    throw new EndOfStreamException($"Wanted {byteCount} bytes, got {read}");

                int[] result = new int[size];
                for (int i = 0, j = 0; i < size; i++, j += 2)
                    result[i] = (span[j] << 8) | span[j + 1];

                return result;
            }
            finally
            {
                if (rent != null)
                    ArrayPool<byte>.Shared.Return(rent);
            }
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
                    if(c == '\0') //if it needs to be remapped, as per the characters array
                        c = '\u003F'; //replace with question mark as placeholder to avoid rendering issues
                    sb.Append(c);
                } else {
                    sb.Append((char) b);
                }
            }

            return sb.ToString();
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

        public void WriteByte(int value) => base.WriteByte((byte)value);


        /// <summary>
        /// Writes a Java‐style signed byte (-128..127) into the stream.
        /// </summary>
        public void WriteSignedByte(sbyte value) {
            // cast to byte will wrap negative values into their two’s‐complement 0..255 form
            WriteByte((byte)value);
        }

        internal void WriteBytes(int bytes, long value)
        {
            Span<byte> tmp = bytes <= 8 ? stackalloc byte[bytes] : new byte[bytes];
            for (int i = 0; i < bytes; i++)
                tmp[i] = (byte)(value >> (8 * (bytes - i - 1)));
            Write(tmp);
        }

        public void WriteShort(short v) => WriteBytes(2, v);
        public void WriteMedium(int v) => WriteBytes(3, v);
        public void WriteInteger(int v) => WriteBytes(4, v);
        public void WriteLong(long v) => WriteBytes(8, v);
        public void WriteShort(int value) => WriteShort((short)value);
        public void WriteMedium(long value) => WriteBytes(3, value);        // cast not needed; WriteBytes takes long
        public void WriteInteger(long value) => WriteInteger((int)value);   // 4-byte truncation is intentional
        
        public void WriteVarInt(int value)
        {
            uint v = (uint)value;            // work in unsigned space
            while (v >= 0x80)
            {
                WriteByte((byte)(v | 0x80)); // set "more" flag
                v >>= 7;
            }
            WriteByte((byte)v);              // final byte, MSB clear
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

        /// <summary>
        /// Clears this buffer. The position is set to zero, the limit is set to the
        /// capacity, and the mark is discarded.
        /// </summary>
        public void Clear() {
            Position = 0;
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
