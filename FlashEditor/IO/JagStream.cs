using FlashEditor.Utils;
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
        private int _length;

        /// <summary>
        ///     The number of bytes the stream holds.
        /// </summary>
        /// <remarks>
        ///     Assigning this used to be able to lie. It was a plain field, so setting it past
        ///     the backing array made the stream claim bytes that were never allocated, and the
        ///     next read failed in a particularly misleading way: every read guards on
        ///     <see cref="Length"/> and then indexes <see cref="Buffer"/>, so the guard passed and
        ///     the indexer threw <see cref="IndexOutOfRangeException"/> from a method written to
        ///     raise <see cref="EndOfStreamException"/> for exactly that case.
        ///     <para>
        ///     Growing now allocates and zero-fills the new region, so the invariant that
        ///     <see cref="Length"/> never exceeds capacity cannot be broken from outside, and a
        ///     grow behaves like it does on any other stream. Shrinking leaves the bytes above
        ///     the new length in place but unreachable, and <see cref="Position"/> is pulled back
        ///     so it can never sit past the end.
        ///     </para>
        /// </remarks>
        /// <exception cref="ArgumentOutOfRangeException">The value is negative.</exception>
        public int Length {
            get => _length;
            set {
                if (value < 0)
                    throw new ArgumentOutOfRangeException(nameof(value), "Stream length cannot be negative");

                if (value > _length) {
                    EnsureCapacity(value);

                    /* Array.Resize zeroes only what it appends, so the span between the old
                       length and the old capacity can still hold bytes from a previous write. */
                    Buffer.AsSpan(_length, value - _length).Clear();
                }

                _length = value;

                if (Position > _length)
                    Position = _length;
            }
        }

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

            /* The constructors assign the backing field rather than the property. The buffer is
               already populated by the time the length is set, and the property zero-fills the
               region a grow exposes - which here is the data itself. */
            _length = 0;
            Position = 0;
        }

        /// <summary>
        /// Creates a JagStream over an existing buffer (read/write).
        /// </summary>
        public JagStream(byte[] buffer) {
            Buffer = buffer;
            _length = buffer.Length;
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
            _length = count;
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

            return new JagStream(data);
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
                throw new IOException("Cannot flip: position is zero while length is non-zero");
            Length = Position;
            Position = 0;
            return this;
        }

        /// <summary>
        /// Zeros the written bytes and empties the stream: position and length both become 0.
        /// The capacity is kept so the buffer can be reused without reallocating.
        /// </summary>
        /// <remarks>
        ///     This zeroed the buffer and rewound, but then set <see cref="Length"/> to the full
        ///     capacity rather than to zero, so a "cleared" stream came back longer than it went
        ///     in and read as a run of zero bytes instead of being empty. Reusing a stream by
        ///     clearing it handed the next caller a padded stream, and every Remaining or Length
        ///     check on it was wrong.
        /// </remarks>
        public void Clear() {
            //Zero first, while Length still describes the region that was written
            Buffer.AsSpan(0, Length).Clear();
            Position = 0;
            Length = 0;
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

            /* Extend the backing field rather than the property. The bytes between the old
               length and the new position are what was just written, and the property zero-fills
               exactly that region when it grows. */
            if (Position > _length) _length = Position;
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

            //Backing field, not the property - see the note in Write(ReadOnlySpan<byte>)
            if (Position > _length) _length = Position;
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
        /// <remarks>
        ///     There used to be no width limit here. The loop shifted left for as long as the
        ///     continuation bit was set, so a corrupt or hostile stream could push every
        ///     meaningful bit off the top of the accumulator and the method returned a plausible
        ///     small number instead of rejecting the input - six groups encoding 2^35 decoded to
        ///     0. Malformed wire data now reports itself.
        /// </remarks>
        /// <exception cref="InvalidDataException">
        ///     The sequence encodes a value too wide to fit in 32 bits.
        /// </exception>
        /// <exception cref="EndOfStreamException">The sequence is unterminated.</exception>
        public int ReadVarInt() {
            sbyte b = ReadSignedByte();
            int value = 0;
            while (b < 0) {
                int accumulated = value | (b & 0x7F);

                /* The next shift moves everything left by seven. Any bit at or above bit 25 is
                   about to leave the accumulator, which is exactly the silent truncation this
                   guard exists to stop. Compared as uint so an accumulator that has already
                   reached the sign bit is caught rather than read as negative. */
                if ((uint) accumulated > (uint.MaxValue >> 7))
                    throw new InvalidDataException("VarInt sequence is wider than 32 bits");

                value = accumulated << 7;
                b = ReadSignedByte();
            }
            // Loop exit guarantees b >= 0, so b is 0..127 and the sbyte->int sign extension
            // cannot inject high bits. The mask is a no-op that states the invariant.
            return value | (b & 0x7F);
        }

        /// <summary>
        /// Writes a MIDI-style variable-length quantity (MSB-first, matching <see cref="ReadVarInt"/>).
        /// </summary>
        public void WriteVarInt(int value) {
            Span<byte> buf = stackalloc byte[5];
            int pos = 4;
            buf[pos] = (byte) (value & 0x7F);
            value >>>= 7;
            while (value > 0) {
                buf[--pos] = (byte) ((value & 0x7F) | 0x80);
                value >>>= 7;
            }
            Write(buf.Slice(pos, 5 - pos));
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
            int b = ReadByte();
            if (b < 0) throw new EndOfStreamException("End of stream");
            return b;
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
        /// Reads the next <paramref name="count"/> bytes as a big-endian integer.
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
        /// Which of the signed smart's two widths a value was stored in, or an instruction to
        /// pick one.
        /// </summary>
        /// <remarks>
        ///     The encoding is not canonical: -64 to 63 is representable in both widths, so the
        ///     decoded value alone does not determine the bytes. A codec that must re-encode
        ///     byte-identically records what <see cref="ReadSmart(out SmartWidth)"/> reported and
        ///     hands it back to <see cref="WriteSmart(int, SmartWidth)"/>.
        /// </remarks>
        public enum SmartWidth {
            /// <summary>Emit the narrowest form that holds the value.</summary>
            Shortest = 0,

            /// <summary>One byte, biased by 64. Only -64 to 63 fit.</summary>
            OneByte = 1,

            /// <summary>Two bytes, biased by 0xC000. -16384 to 16383 fit.</summary>
            TwoByte = 2
        }

        /// <summary>
        /// Reads the client's signed smart: one byte 0-127 biased by -64, or an unsigned short
        /// biased by -0xC000.
        /// </summary>
        /// <remarks>
        ///     This is <c>RSBuffer.method1239</c> (RSBuffer.java:606-612) exactly. The branch is
        ///     taken on the first byte's high bit, so the two-byte form can only carry
        ///     0x8000-0xFFFF and its reachable range is -16384 to 16383 - not the -49152 that a
        ///     bare reading of "u16 - 49152" suggests, which is why the writer rejects below
        ///     -16384. Use <see cref="ReadSmart(out SmartWidth)"/> wherever the stored width has
        ///     to survive a re-encode.
        ///     <para>
        ///     <see cref="WriteSmart(int)"/> is the inverse. <see cref="WriteUnsignedSmart"/> is
        ///     not: it carries the 0/32768 biases and pairs with
        ///     <see cref="ReadUnsignedSmart"/>.
        ///     </para>
        /// </remarks>
        /// <returns>-16384 to 16383.</returns>
        public int ReadSmart() => ReadSmart(out _);

        /// <summary>
        /// Reads a signed smart and reports which of its two widths was on the wire.
        /// </summary>
        /// <remarks>
        ///     -64 to 63 has two legal encodings, so a decoder that keeps only the value cannot
        ///     reproduce the bytes it read. That is the same non-canonical hazard the terrain and
        ///     floor codecs already record per field; a byte-identity sweep is the only thing
        ///     that can tell you whether a given index needs the width kept.
        /// </remarks>
        /// <param name="width">The width actually present on the wire.</param>
        /// <returns>-16384 to 16383.</returns>
        public int ReadSmart(out SmartWidth width) {
            int peek = Get(Position) & 0xFF;
            if (peek < 128) {
                width = SmartWidth.OneByte;
                return ReadUnsignedByte() - 64;
            }

            width = SmartWidth.TwoByte;
            return ReadUnsignedShort() - 0xC000;
        }

        /// <summary>
        /// Writes a signed smart in the narrowest form that holds the value. Inverse of
        /// <see cref="ReadSmart()"/>.
        /// </summary>
        /// <remarks>
        ///     Shortest-form is the right default because it is what Jagex's own encoder did.
        ///     Measured over all 359,931 index-0 frame files: of 20,142,030 signed smarts,
        ///     11,871,643 are two-byte and not one of those encodes a value the one-byte form
        ///     could have held. So in this cache the encoding is canonical in practice even
        ///     though the format permits otherwise, and shortest-form reproduces it byte for
        ///     byte.
        ///     <para>
        ///     That is a measurement, not a guarantee. A caller that has recorded the stored
        ///     width should replay it through <see cref="WriteSmart(int, SmartWidth)"/> rather
        ///     than trust the measurement to hold for an index nobody has swept yet.
        ///     </para>
        /// </remarks>
        /// <param name="value">-16384 to 16383.</param>
        /// <exception cref="ArgumentOutOfRangeException">
        ///     The value is outside the range this encoding can represent.
        /// </exception>
        public void WriteSmart(int value) => WriteSmart(value, SmartWidth.Shortest);

        /// <summary>
        /// Writes a signed smart in a caller-chosen width, so a decoder that recorded the stored
        /// width can reproduce the original bytes for a value both widths can express.
        /// </summary>
        /// <remarks>
        ///     Nothing is written when the value does not fit the requested width. Forcing
        ///     <see cref="SmartWidth.OneByte"/> on a value outside -64 to 63 is a contradiction
        ///     and is rejected rather than silently widened, because a caller replaying a
        ///     recorded width has necessarily edited the value if that happens, and quietly
        ///     changing the field's length shifts every byte after it.
        /// </remarks>
        /// <param name="value">-16384 to 16383, narrowing to -64 to 63 for a forced one-byte write.</param>
        /// <param name="width">The width to emit, or <see cref="SmartWidth.Shortest"/> to choose.</param>
        /// <exception cref="ArgumentOutOfRangeException">
        ///     The value does not fit the chosen width, or the width is not a defined member.
        /// </exception>
        public void WriteSmart(int value, SmartWidth width) {
            bool twoByte = width switch {
                SmartWidth.Shortest => value < -64 || value > 63,
                SmartWidth.OneByte => false,
                SmartWidth.TwoByte => true,
                _ => throw new ArgumentOutOfRangeException(nameof(width), width,
                    "Not a signed smart width")
            };

            if (!twoByte) {
                if (value < -64 || value > 63)
                    throw new ArgumentOutOfRangeException(nameof(value), value,
                        "A one-byte signed smart must be between -64 and 63");

                WriteByte((byte) (value + 64));
                return;
            }

            if (value < -16384 || value > 16383)
                throw new ArgumentOutOfRangeException(nameof(value), value,
                    "Signed smart values must be between -16384 and 16383");

            /* Biasing by 0xC000 lands the whole range in 0x8000-0xFFFF, which is exactly the
               span whose leading byte sets the high bit and so re-reads as the two-byte form. */
            int biased = value + 0xC000;
            WriteByte((byte) (biased >> 8));
            WriteByte((byte) biased);
        }

        /// <summary>
        /// Unsigned smart (cache file): single byte 0-127 or unsigned short with 32768 bias.
        /// </summary>
        /// <remarks>
        ///     Not <see cref="ReadSmart()"/>, which biases by -64 and -0xC000 instead. Pairs with
        ///     <see cref="WriteUnsignedSmart"/>.
        /// </remarks>
        public int ReadUnsignedSmart() {
            int peek = Get(Position) & 0xFF;
            return peek < 128
                ? ReadUnsignedByte()
                : ReadUnsignedShort() - 32768;
        }

        /// <summary>
        /// Alias for <see cref="ReadSmart()"/>: signed smart with -64 / -0xC000 bias.
        /// </summary>
        /// <remarks>
        ///     There is no matching WriteShortSmart. <see cref="WriteSmart(int)"/> is the
        ///     inverse of both names.
        /// </remarks>
        public int ReadShortSmart() => ReadSmart();


        /// <summary>
        /// Signed smart (delta-encoded): zig-zag decode of unsignedSmart.
        /// </summary>
        public int ReadSignedSmart() {
            int val = ReadUnsignedSmart();
            return (val >> 1) ^ (-(val & 1));
        }

        /// <summary>
        /// Unsigned smart with a 32767 continuation: reads smarts while each one is the
        /// maximum 32767, accumulating, so the encodable range is not capped at a short.
        /// </summary>
        /// <remarks>
        ///     This is a third smart form, distinct from <see cref="ReadUnsignedSmart"/> and
        ///     <see cref="ReadSignedSmart"/>. The 637 client reads it with <c>RSBuffer.method1208</c>
        ///     (RSBuffer.java:288-304) and uses it for exactly one field: the object-id delta in the
        ///     loc stream. Reading that field as a plain smart terminates the continuation early and
        ///     desynchronises the rest of the file - 63 of the shipped <c>l</c> groups and 260 of the
        ///     <c>ul</c> groups contain a continuation. The position delta in the same stream is a
        ///     plain smart and must not be read with this.
        /// </remarks>
        /// <returns>The accumulated value.</returns>
        public int ReadExtendedUnsignedSmart() {
            int total = 0;
            int value;
            while ((value = ReadUnsignedSmart()) == 32767)
                total += 32767;
            return total + value;
        }

        /// <summary>
        /// Writes the extended unsigned smart form read by <see cref="ReadExtendedUnsignedSmart"/>.
        /// </summary>
        /// <remarks>
        ///     Emits as many 32767 continuation chunks as the value needs, then the remainder. A
        ///     value that is exactly 32767 therefore takes two smarts, 32767 followed by 0, because
        ///     a single 32767 would be read as a continuation with nothing after it.
        /// </remarks>
        /// <param name="value">The value to write. Must not be negative.</param>
        /// <exception cref="ArgumentOutOfRangeException">The value is negative.</exception>
        public void WriteExtendedUnsignedSmart(int value) {
            if (value < 0)
                throw new ArgumentOutOfRangeException(nameof(value), value, "Cannot be negative");

            while (value >= 32767) {
                WriteUnsignedSmart(32767);
                value -= 32767;
            }

            WriteUnsignedSmart(value);
        }

        /// <summary>
        /// Writes an unsigned smart: single byte for 0-127, two-byte short
        /// (value + 32768) for 128-32767. Inverse of <see cref="ReadUnsignedSmart"/>.
        /// </summary>
        /// <remarks>
        ///     <b>Not the writer for <see cref="ReadSmart()"/>.</b> That one carries the
        ///     -64 / -0xC000 biases and is <see cref="WriteSmart(int)"/>. The two forms differ by
        ///     a uniform 16384 on the two-byte branch and 64 on the one-byte branch, so picking
        ///     the wrong one produces a well-formed field holding the wrong number rather than
        ///     anything that fails loudly.
        ///     <para>
        ///     This validated nothing. Below 0 it took the single-byte branch and emitted a byte
        ///     with the high bit set, which <see cref="ReadUnsignedSmart"/> then treats as the
        ///     first half of a two-byte form; at or above 32768 the "+ 32768" wrapped the short.
        ///     Either way the bytes written did not encode the value asked for, and everything
        ///     read after them was off by a byte with nothing to indicate it.
        ///     </para>
        /// </remarks>
        /// <exception cref="ArgumentOutOfRangeException">
        ///     The value is outside 0 to 32767, the range this encoding can represent.
        /// </exception>
        public void WriteUnsignedSmart(int value) {
            if (value < 0 || value > 32767)
                throw new ArgumentOutOfRangeException(nameof(value), value,
                    "Unsigned smart values must be between 0 and 32767");

            if (value < 128)
                WriteByte((byte) value);
            else
                WriteShort((short) (value + 32768));
        }

        /// <summary>
        /// Special smart: single-byte-1 or unsignedShort-32769.
        /// </summary>
        public int ReadSpecialSmart() {
            /* Peek through Get, as every other smart reader does. Indexing Buffer directly read
               past Length into whatever the spare capacity still held, then chose its branch from
               that byte and returned a plausible number with no error at all - and past capacity
               it raised IndexOutOfRangeException, which is not the exception any sibling raises.
               Get bounds-checks against Length and throws ArgumentOutOfRangeException, matching
               ReadSmart and ReadUnsignedSmart. */
            int peek = Get(Position) & 0xFF;
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

            /* The return has to happen on the throwing path too. A short read is the normal way
               a truncated stream is reported, so leaking the rental there leaks it precisely
               when the caller is already in trouble. */
            try {
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

                return result;
            }
            finally {
                if (rent != null) ArrayPool<byte>.Shared.Return(rent);
            }
        }

        /// <summary>
        /// Reads <paramref name="size"/> unsigned shorts into an int[], pooling for large sizes.
        /// </summary>
        public int[] ReadUnsignedShortArray(int size) {
            int byteCount = size * 2;
            byte[]? rent = null;
            Span<byte> span = byteCount <= 2048
                ? stackalloc byte[byteCount]
                : (rent = ArrayPool<byte>.Shared.Rent(byteCount)).AsSpan(0, byteCount);

            //The rental has to come back on the throwing path too - see ReadUnsignedByteArray
            try {
                for (int got = 0 ; got < byteCount ; got++) {
                    int b = ReadByte();
                    if (b < 0) throw new EndOfStreamException($"Wanted {byteCount} bytes, eof at {got}");
                    span[(int) got] = (byte) b;
                }

                var result = new int[size];
                for (int i = 0, j = 0 ; i < size ; i++, j += 2)
                    result[i] = (span[j] << 8) | span[j + 1];

                return result;
            }
            finally {
                if (rent != null) ArrayPool<byte>.Shared.Return(rent);
            }
        }

        #endregion

        #region String Readers/Writers

        /// <summary>
        /// Reads a null-terminated string with every byte taken at face value as a code point.
        /// <b>This is not the cache's string encoding.</b> Use <see cref="ReadJagexString"/> for
        /// anything read out of the cache.
        /// </summary>
        /// <remarks>
        ///     The cache stores strings in the client's modified cp1252, where bytes 0x80-0x9F
        ///     name characters that are nothing like the Latin-1 code points of the same value -
        ///     0x80 is the euro sign, not U+0080. This reader skips that remap, so it silently
        ///     produces C1 control characters where the cache holds punctuation, and there is no
        ///     WriteString2 to pair with it: a string read here and written back through
        ///     <see cref="WriteJagexString"/> does not round trip.
        ///     <para>
        ///     It is wrong for every string-bearing index, indexes 17, 23, 24, 25 and 33
        ///     included. It survives because a non-cache caller may legitimately want raw bytes
        ///     out of a null-terminated field; it has no production callers today.
        ///     </para>
        /// </remarks>
        public string ReadString2() {
            var sb = new StringBuilder();
            int b;
            while ((b = ReadByte()) != 0) {
                if (b < 0) throw new EndOfStreamException();
                sb.Append((char) b);
            }
            return sb.ToString();
        }

        /// <summary>
        /// Reads a null-terminated string in the client's modified cp1252. <b>This is the reader
        /// for cache strings</b>, not <see cref="ReadString2"/>.
        /// </summary>
        /// <remarks>
        ///     Matches <c>RSBuffer.readString</c> (RSBuffer.java:878-894) including the fallback
        ///     to '?' for the five unassigned slots in the 0x80-0x9F band. The remap table is
        ///     carried in this class rather than taken from
        ///     <c>Encoding.GetEncoding(1252)</c>, which is deliberate: .NET 9 ships only ASCII,
        ///     Latin-1 and the UTF family, so code page 1252 needs the
        ///     System.Text.Encoding.CodePages package and a CodePagesEncodingProvider
        ///     registration. Neither exists in this solution, and the table makes both
        ///     unnecessary here.
        /// </remarks>
        public string ReadJagexString() {
            var sb = new StringBuilder();
            int b;
            while ((b = ReadByte()) != 0) {
                if (b < 0) throw new EndOfStreamException();
                if (b >= 128 && b < 160) {
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
        /// Writes a 0-terminated string in the client's modified cp1252. The only string writer,
        /// and the inverse of <see cref="ReadJagexString"/>.
        /// </summary>
        /// <remarks>
        ///     Lossy in two places, and both are permanent after one save: the five unassigned
        ///     slots in the 0x80-0x9F band decode to '?' and re-encode as 0x3F, and an embedded
        ///     NUL is dropped rather than written, since writing it would terminate the string
        ///     early and hand the rest of the record back as the next field.
        /// </remarks>
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
        /// <remarks>
        ///     This used to validate nothing: it accepted a negative argument as a rewind and
        ///     clamped an overshoot in either direction rather than reporting it. A codec that
        ///     skipped a payload whose declared length was corrupt landed quietly at the end of
        ///     the stream and carried on decoding as though it had skipped the right amount.
        ///     <see cref="Seek(long, SeekOrigin)"/>, given the same out-of-range destination,
        ///     always threw; the two now agree.
        /// </remarks>
        /// <exception cref="IOException">
        ///     The resulting position would be before the start or past <see cref="Length"/>.
        /// </exception>
        public void Skip(int skip) {
            /* Widened so that int.MinValue, and a large positive skip from a large position,
               are rejected rather than wrapping into an in-range destination. */
            long target = (long) Position + skip;
            if (target < 0 || target > Length)
                throw new IOException($"Skip out of bounds: {target}");
            Position = (int) target;
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
        /// Writes <paramref name="count"/> bytes from <paramref name="value"/> in big-endian order.
        /// A count above eight sign-extends: the leading bytes are 0x00 for a non-negative value
        /// and 0xFF for a negative one, which is what the value's two's-complement encoding is.
        /// </summary>
        /// <remarks>
        ///     Past eight bytes the shift distance exceeds the width of a long, and C# masks the
        ///     shift count to six bits rather than yielding zero. The extra leading bytes that a
        ///     wider request should have filled therefore repeated the value's low byte, and the
        ///     result was not the big-endian encoding this method promises. Clamping the shift to
        ///     63 lets the arithmetic right shift produce the sign fill instead, so the bytes
        ///     written always decode back to the value that was passed in.
        /// </remarks>
        /// <exception cref="ArgumentOutOfRangeException">The count is negative.</exception>
        public void WriteBytes(int count, long value) {
            if (count < 0)
                throw new ArgumentOutOfRangeException(nameof(count), count, "Byte count cannot be negative");

            Span<byte> tmp = count <= 8 ? stackalloc byte[8] : new byte[count];
            for (int i = 0 ; i < count ; i++) {
                int shift = 8 * (count - i - 1);
                tmp[i] = (byte) (value >> (shift < 63 ? shift : 63));
            }
            Write(tmp.Slice(0, count));
        }

        #endregion
    }
}
