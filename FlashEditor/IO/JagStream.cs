using FlashEditor.utils;
using System;
using System.Buffers;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace FlashEditor {
    /// <summary>
    /// A high-performance, span-based replacement for the old MemoryStream-based JagStream.
    /// </summary>
    public class JagStream {
        private byte[] Buffer;
        public int Position;
        public int Length;
        public int Capacity => Buffer.Length;

        /// <summary>
        /// The modified set of 'extended ASCII' characters used by the client.
        /// </summary>
        private static readonly char[] CHARACTERS = {
            '\u20AC','\0','\u201A','\u0192','\u201E','\u2026','\u2020','\u2021',
            '\u02C6','\u2030','\u0160','\u2039','\u0152','\0','\u017D','\0',
            '\0','\u2018','\u2019','\u201C','\u201D','\u2022','\u2013','\u2014',
            '\u02DC','\u2122','\u0161','\u203A','\u0153','\0','\u017E','\u0178'
        };

        /// <summary>Maps Unicode → RuneScape extended byte (128–159).</summary>
        private static readonly Dictionary<char, byte> EXTENDED_REMAP = BuildReverse();

        private static Dictionary<char, byte> BuildReverse() {
            var map = new Dictionary<char, byte>(32);
            for (int i = 0 ; i < CHARACTERS.Length ; i++)
                if (CHARACTERS[i] != '\0')
                    map[CHARACTERS[i]] = (byte) (i + 128);
            return map;
        }

        #region Constructors

        /// <summary>
        /// Creates an expandable JagStream with the given initial capacity.
        /// </summary>
        public JagStream(int capacity) {
            Buffer = new byte[capacity];
            Length = 0;
            Position = 0;
        }

        /// <summary>
        /// Creates a JagStream over an existing buffer (read/write).
        /// </summary>
        public JagStream(byte[] buffer) {
            Buffer = buffer;
            Length = buffer.Length;
            Position = 0;
        }

        /// <summary>
        /// Default constructor: starts with a small expandable buffer.
        /// </summary>
        public JagStream() : this(256) { }

        /// <summary>
        /// Creates a JagStream over a portion of an existing buffer (read-only, expandable).
        /// </summary>
        public JagStream(byte[] buffer, int index, int count) {
            // publiclyVisible: we expose the full buffer via GetBuffer()
            Buffer = new byte[count];
            Array.Copy(buffer, index, Buffer, 0, count);
            Length = count;
            Position = 0;
        }

        #endregion

        #region Load/Save

        /// <summary>
        /// Loads an entire file into a new JagStream (expandable).
        /// </summary>
        public static JagStream LoadStream(string path) {
            if (!File.Exists(path))
                throw new FileNotFoundException($"'{path}' could not be found.");

            byte[] data = File.ReadAllBytes(path);
            if (data.Length == 0)
                DebugUtil.Debug($"No data read for path: {path}");

            var js = new JagStream();
            js.Write(data, 0, data.Length);
            return js;
        }

        /// <summary>
        /// Writes binary data from a JagStream to a file.
        /// </summary>
        public static void Save(JagStream stream, string path) {
            if (stream == null)
                throw new NullReferenceException("Stream was null");

            string dir = Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
                Directory.CreateDirectory(dir);

            File.WriteAllBytes(path, stream.ToArray());
        }

        /// <summary>
        /// Instance overload for Save.
        /// </summary>
        public void Save(string path) => Save(this, path);

        #endregion

        #region Buffer/Position Management

        /// <summary>
        /// Exposes the raw buffer (capacity).
        /// </summary>
        public byte[] GetBuffer() => Buffer;

        /// <summary>
        /// Copies the valid portion of the buffer to a fresh array.
        /// </summary>
        public byte[] ToArray() {
            var output = new byte[Length];
            Array.Copy(Buffer, 0, output, 0, Length);
            return output;
        }

        /// <summary>
        /// Sets length = position, resets position to 0.
        /// </summary>
        /// <returns>This stream.</returns>
        public JagStream Flip() {
            if (Position == 0 && Length > 0)
                throw new IOException("Idiot you're destroying the stream");
            Length = Position;
            Position = 0;
            return this;
        }

        /// <summary>
        /// Clears this buffer. Position=0, length=capacity.
        /// </summary>
        public void Clear() {
            Position = 0;
            Length = Buffer.Length;
        }

        /// <summary>
        /// Computes the number of bytes remaining.
        /// </summary>
        public int Remaining() => Length - Position;

        /// <summary>
        /// Seek to absolute offset from beginning.
        /// </summary>
        public long Seek(long offset) => Seek(offset, SeekOrigin.Begin);

        /// <summary>
        /// Seek with origin.
        /// </summary>
        public long Seek(long offset, SeekOrigin origin) {
            int newPos = origin switch {
                SeekOrigin.Begin => (int) offset,
                SeekOrigin.Current => Position + (int) offset,
                SeekOrigin.End => Length + (int) offset,
                _ => throw new ArgumentOutOfRangeException(nameof(origin))
            };
            if (newPos < 0 || newPos > Length)
                throw new IOException($"Seek out of bounds: {newPos}");
            Position = newPos;
            return Position;
        }

        /// <summary>
        /// Seek to 0.
        /// </summary>
        public long Seek0() => Seek(0, SeekOrigin.Begin);

        #endregion

        #region Underlying Read/Write Helpers

        private void EnsureCapacity(int required) {
            if (required <= Buffer.Length) return;
            int newSize = Math.Max(Buffer.Length * 2, required);
            Array.Resize(ref Buffer, newSize);
        }

        /// <summary>
        /// Reads up to <paramref name="destination"/>.Length bytes into the provided span,
        /// advances the position, and returns the actual number of bytes read (0 on EOF).
        /// </summary>
        public int Read(Span<byte> destination) {
            int remaining = Length - Position;
            if (remaining <= 0) return 0;
            int toCopy = Math.Min(remaining, destination.Length);
            Buffer.AsSpan(Position, toCopy).CopyTo(destination);
            Position += toCopy;
            return toCopy;
        }

        /// <summary>
        /// Reads up to <paramref name="count"/> bytes into <paramref name="buffer"/> at <paramref name="offset"/>,
        /// advances the position, and returns the number of bytes read.
        /// </summary>
        public int Read(byte[] buffer, int offset, int count) {
            return Read(buffer.AsSpan(offset, count));
        }

        /// <summary>
        /// Reads one byte (0–255) or -1 if EOF.
        /// </summary>
        public int ReadByte() {
            if (Position >= Length) return -1;
            return Buffer[Position++];
        }

        /// <summary>
        /// Writes raw bytes from span.
        /// </summary>
        public void Write(ReadOnlySpan<byte> span) {
            EnsureCapacity(Position + span.Length);
            span.CopyTo(Buffer.AsSpan(Position));
            Position += span.Length;
            if (Position > Length) Length = Position;
        }

        /// <summary>
        /// Writes the entire contents of this JagStream (from 0 up to Length)
        /// into the provided destination stream.
        /// </summary>
        /// <param name="destination">The stream to write into.</param>
        public void WriteTo(JagStream destination) {
            if (destination == null)
                throw new ArgumentNullException(nameof(destination));

            // Write the valid portion of our internal buffer as a ReadOnlySpan<byte>
            destination.Write(Buffer.AsSpan(0, Length));
        }

        /// <summary>
        /// Writes raw bytes from array[offset..offset+count].
        /// </summary>
        public void Write(byte[] data, int offset, int count) => Write(data.AsSpan(offset, count));

        /// <summary>
        /// Writes a single byte.
        /// </summary>
        public void WriteByte(byte value) {
            EnsureCapacity(Position + 1);
            Buffer[Position++] = value;
            if (Position > Length) Length = Position;
        }

        /// <summary>
        /// Writes a single byte (int overload).
        /// </summary>
        public void WriteByte(int value) => WriteByte((byte) value);

        #endregion

        #region Primitive Readers/Writers

        /// <summary>
        /// Reads a variable-length integer encoded in 7-bit chunks (Java’s readVarInt equivalent).
        /// </summary>
        public int ReadVarInt() {
            sbyte b = ReadSignedByte();
            int value = 0;
            while (b < 0) {
                value = (value | (b & 0x7F)) << 7;
                b = ReadSignedByte();
            }
            return value | b;
        }

        /// <summary>
        /// Writes a Java‐style varint.
        /// </summary>
        public void WriteVarInt(int value) {
            uint v = (uint) value;
            while (v >= 0x80) {
                WriteByte((byte) (v | 0x80));
                v >>= 7;
            }
            WriteByte((byte) v);
        }

        /// <summary>
        /// Reads next byte as signed (-128..127).
        /// </summary>
        public sbyte ReadSignedByte() {
            int b = ReadByte();
            if (b < 0) throw new EndOfStreamException("End of stream");
            return unchecked((sbyte) b);
        }

        /// <summary>
        /// Writes a Java‐style signed byte.
        /// </summary>
        public void WriteSignedByte(sbyte value) => WriteByte((byte) value);

        /// <summary>
        /// Reads next byte as unsigned (0–255), throws at EOF.
        /// </summary>
        public int ReadUnsignedByte() {
            return ReadByte() & 0xFF;
        }

        /// <summary>
        /// Peeks next byte without advancing.
        /// </summary>
        public int Peek() {
            int p = Position;
            int v = ReadSignedByte();
            Position = p;
            return v;
        }

        /// <summary>
        /// Peeks next unsigned byte without advancing.
        /// </summary>
        public byte PeekUnsignedByte() {
            int p = Position;
            int b = ReadByte();
            if (b < 0) throw new EndOfStreamException();
            Position = p;
            return (byte) b;
        }

        /// <summary>
        /// Reads a 2-byte big-endian unsigned int.
        /// </summary>
        public int ReadUnsignedShort() {
            if (Position + 2 > Length) throw new EndOfStreamException();
            int val = BinaryPrimitives.ReadUInt16BigEndian(Buffer.AsSpan(Position));
            Position += 2;
            return val;
        }

        /// <summary>
        /// Reads a signed 2-byte big-endian short (-32768..32767).
        /// </summary>
        public int ReadShort() {
            int u = ReadUnsignedShort();
            return u > 32767 ? u - 0x10000 : u;
        }

        /// <summary>
        /// Writes a 2-byte big-endian short.
        /// </summary>
        public void WriteShort(short v) {
            Span<byte> tmp = stackalloc byte[2];
            BinaryPrimitives.WriteInt16BigEndian(tmp, v);
            Write(tmp);
        }

        /// <summary>
        /// Writes a 2-byte big-endian short (int overload).
        /// </summary>
        public void WriteShort(int value) => WriteShort((short) value);

        /// <summary>
        /// Reads a 3-byte “medium” int.
        /// </summary>
        public int ReadMedium() {
            if (Position + 3 > Length) throw new EndOfStreamException();
            int val = (Buffer[Position++] << 16)
                    | (Buffer[Position++] << 8)
                    | Buffer[Position++];
            return val;
        }

        /// <summary>
        /// Writes a 3-byte medium.
        /// </summary>
        public void WriteMedium(int v) {
            Span<byte> tmp = stackalloc byte[3];
            tmp[0] = (byte) (v >> 16);
            tmp[1] = (byte) (v >> 8);
            tmp[2] = (byte) v;
            Write(tmp);
        }

        /// <summary>
        /// Reads a 4-byte big-endian int.
        /// </summary>
        public int ReadInt() {
            if (Position + 4 > Length) throw new EndOfStreamException();
            int val = BinaryPrimitives.ReadInt32BigEndian(Buffer.AsSpan(Position));
            Position += 4;
            return val;
        }

        /// <summary>
        /// Writes a 4-byte big-endian int.
        /// </summary>
        public void WriteInteger(int v) {
            Span<byte> tmp = stackalloc byte[4];
            BinaryPrimitives.WriteInt32BigEndian(tmp, v);
            Write(tmp);
        }

        /// <summary>
        /// Writes a 4-byte unsigned big-endian integer.
        /// </summary>
        /// <param name="v">The unsigned integer to write.</param>
        public void WriteInteger(uint v) {
            Span<byte> tmp = stackalloc byte[4];
            BinaryPrimitives.WriteUInt32BigEndian(tmp, v);
            Write(tmp);
        }

        /// <summary>
        /// Writes an 8-byte big-endian long.
        /// </summary>
        public void WriteLong(long v) {
            Span<byte> tmp = stackalloc byte[8];
            BinaryPrimitives.WriteInt64BigEndian(tmp, v);
            Write(tmp);
        }

        /// <summary>
        /// Reads next 3 bytes as signed medium.
        /// </summary>
        public int ReadBytesAsInt(int count) {
            if (Position + count > Length) throw new EndOfStreamException();
            int val = 0;
            for (int i = 0 ; i < count ; i++)
                val = (val << 8) | Buffer[Position++];
            return val;
        }

        #endregion

        #region “Smart” Readers

        /// <summary>
        /// **smart_v1**: 1- or 2-byte value (<128 = one-byte; otherwise ushort − 0x8000).
        /// </summary>
        public int ReadSmart() {
            int p = Position;
            if (p >= Length) throw new EndOfStreamException("Cannot peek Smart");
            int peek = Buffer[p] & 0xFF;
            return peek < 128
                ? ReadUnsignedByte()
                : ReadUnsignedShort() - 0x8000;
        }

        /// <summary>
        /// Reads a “short smart” (signed): single byte −64 or ushort −0xC000.
        /// </summary>
        public int ReadShortSmart() {
            int peek = Peek() & 0xFF;
            return peek < 128
                ? ReadByte() - 64
                : ReadUnsignedShort() - 0xC000;
        }

        /// <summary>
        /// Unsigned smart (cache file): one byte 0–127; two-byte (value+32768).
        /// </summary>
        public int ReadUnsignedSmart() {
            int peek = PeekUnsignedByte();
            return peek < 128
                ? ReadByte()
                : ReadUnsignedShort() - 32768;
        }

        /// <summary>
        /// Signed smart (delta-encoded): zig-zag decode of unsignedSmart.
        /// </summary>
        public int ReadSignedSmart() {
            int val = ReadUnsignedSmart();
            return (val >> 1) ^ (-(val & 1));
        }

        /// <summary>
        /// Special smart: single-byte-1 or unsignedShort-32769.
        /// </summary>
        public int ReadSpecialSmart() {
            int peek = Buffer[Position] & 0xFF;
            return peek < 128
                ? ReadByte() - 1
                : ReadUnsignedShort() - 32769;
        }

        #endregion

        #region Array Readers

        /// <summary>
        /// Reads <paramref name="size"/> unsigned bytes into an int[], pooling for large sizes.
        /// </summary>
        public int[] ReadUnsignedByteArray(int size) {
            byte[]? rent = null;
            Span<byte> span = size <= 1024
                ? stackalloc byte[size]
                : (rent = ArrayPool<byte>.Shared.Rent(size)).AsSpan(0, size);

            int got = 0;
            for (; got < size ; got++) {
                int b = ReadByte();
                if (b < 0) break;
                span[got] = (byte) b;
            }
            if (got != size)
                throw new EndOfStreamException($"Needed {size}, got {got}");

            var result = new int[size];
            for (int i = 0 ; i < size ; i++)
                result[i] = span[i];

            if (rent != null) ArrayPool<byte>.Shared.Return(rent);
            return result;
        }

        /// <summary>
        /// Reads <paramref name="size"/> unsigned shorts into an int[], pooling for large sizes.
        /// </summary>
        public int[] ReadUnsignedShortArray(int size) {
            int byteCount = size * 2;
            byte[]? rent = null;
            Span<byte> span = byteCount <= 2048
                ? stackalloc byte[2048]
                : (rent = ArrayPool<byte>.Shared.Rent(byteCount)).AsSpan(0, byteCount);

            for (int got = 0 ; got < byteCount ; got++) {
                int b = ReadByte();
                if (b < 0) throw new EndOfStreamException($"Wanted {byteCount} bytes, eof at {got}");
                span[(int) got] = (byte) b;
            }

            var result = new int[size];
            for (int i = 0, j = 0 ; i < size ; i++, j += 2)
                result[i] = (span[j] << 8) | span[j + 1];

            if (rent != null) ArrayPool<byte>.Shared.Return(rent);
            return result;
        }

        #endregion

        #region String Readers/Writers

        /// <summary>
        /// Reads a null-terminated ASCII string (0 terminator).
        /// </summary>
        public string ReadString2() {
            var sb = new StringBuilder();
            int b;
            while ((b = ReadByte()) != 0) {
                if (b < 0) throw new EndOfStreamException();
                sb.Append((char) b);
            }
            DebugUtil.WriteLine("'");
            return sb.ToString();
        }

        /// <summary>
        /// Reads a null-terminated “Jagex string” using CP-1252+ remap.
        /// </summary>
        public string ReadJagexString() {
            var sb = new StringBuilder();
            int b;
            while ((b = ReadByte()) != 0) {
                if (b < 0) throw new EndOfStreamException();
                if (b >= 127 && b < 160) {
                    char c = CHARACTERS[b - 128];
                    sb.Append(c == '\0' ? '?' : c);
                }
                else {
                    sb.Append((char) b);
                }
            }
            return sb.ToString();
        }

        /// <summary>
        /// Writes a 0-terminated Jagex string using modified CP-1252.
        /// </summary>
        public void WriteJagexString(string s) {
            foreach (char c in s) {
                if (c == 0) continue;
                if (c < 128 || (c >= 160 && c <= 255))
                    WriteByte((byte) c);
                else if (EXTENDED_REMAP.TryGetValue(c, out byte b))
                    WriteByte(b);
                else
                    WriteByte((byte) '?');
            }
            WriteByte(0);
        }

        #endregion

        #region Miscellaneous

        /// <summary>
        /// Returns the byte at <paramref name="pos"/> without changing <see cref="Position"/>.
        /// </summary>
        public byte Get(int pos) {
            if (pos < 0 || pos >= Length)
                throw new ArgumentOutOfRangeException(nameof(pos));
            return Buffer[pos];
        }

        /// <summary>
        /// Advances position by <paramref name="skip"/>.
        /// </summary>
        public void Skip(int skip) {
            Position += skip;
            if (Position > Length) Position = Length;
            if (Position < 0) Position = 0;
        }

        /// <summary>
        /// Returns a sub-stream starting at ptr with length bytes.
        /// </summary>
        public JagStream GetSubStream(int length, long ptr) {
            Seek(ptr, SeekOrigin.Begin);
            return GetSubStream(length);
        }

        /// <summary>
        /// Returns a sub-stream of the next <paramref name="length"/> bytes.
        /// </summary>
        public JagStream GetSubStream(int length) {
            if (Position + length > Length)
                throw new EndOfStreamException("Not enough data for substream");
            var slice = new JagStream(Buffer, Position, length);
            slice.Position = 0;
            Position += length;
            return slice;
        }

        /// <summary>
        /// Reads exactly <paramref name="length"/> bytes into a new array.
        /// </summary>
        public byte[] ReadBytes(int length) {
            if (Position + length > Length)
                throw new EndOfStreamException($"Requested {length}, have {Remaining()}");
            var dst = new byte[length];
            Array.Copy(Buffer, Position, dst, 0, length);
            Position += length;
            return dst;
        }

        /// <summary>
        /// Clears the buffer: position=0, length=capacity.
        /// </summary>
        public void WriteBytes(int count, long value) {
            Span<byte> tmp = count <= 8 ? stackalloc byte[8] : new byte[count];
            for (int i = 0 ; i < count ; i++)
                tmp[i] = (byte) (value >> (8 * (count - i - 1)));
            Write(tmp.Slice(0, count));
        }

        #endregion
    }
}
