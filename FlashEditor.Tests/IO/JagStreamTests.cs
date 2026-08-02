using FlashEditor;
using FlashEditor.Utils;
using System;
using System.IO;
using Xunit;

namespace FlashEditor.Tests.IO
{
    /// <summary>
    /// Comprehensive coverage of JagStream: the buffer primitive every codec in the project
    /// reads and writes through. Covers construction, position and length management, every
    /// typed read/write pair, the four "smart" encodings, the modified CP-1252 string path,
    /// and the error surface method by method.
    ///
    /// Several tests are named *_DocumentsKnownDefect. Those pin CURRENT behaviour that is
    /// known to be wrong, so the defect is recorded and any future fix shows up as a
    /// deliberate, visible test change rather than a silent behaviour swap. They are not
    /// endorsements of the behaviour they assert.
    ///
    /// A note on the surface, because it is asymmetric and the asymmetry drives several tests
    /// below: there is a WriteLong with no ReadLong, a WriteJagexString with no WriteString2,
    /// and WriteUnsignedSmart is the only writer among the five smart readers. Where no
    /// inverse exists the test asserts against hand-built wire bytes instead.
    /// </summary>
    public class JagStreamTests : IDisposable
    {
        private readonly string _dir;

        public JagStreamTests()
        {
            DebugUtil.LOG_LEVEL = DebugUtil.LOG_DETAIL.NONE;
            _dir = Path.Combine(Path.GetTempPath(), "fe-jagstream-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_dir);
        }

        public void Dispose()
        {
            if (Directory.Exists(_dir))
                Directory.Delete(_dir, true);
        }

        /// <summary>Path inside the fixture's scratch directory. The file need not exist.</summary>
        private string TempPath(string name) => Path.Combine(_dir, name);

        /// <summary>A stream positioned at 0 over exactly the given bytes.</summary>
        private static JagStream Wire(params byte[] bytes) => new JagStream(bytes);

        #region Constructors

        [Fact]
        public void Constructor_WithCapacity_StartsEmptyAtThatCapacity()
        {
            var stream = new JagStream(64);

            Assert.Equal(0, stream.Length);
            Assert.Equal(0, stream.Position);
            Assert.Equal(64, stream.Capacity);
            Assert.Empty(stream.ToArray());
        }

        [Fact]
        public void Constructor_Default_StartsEmptyAt256Bytes()
        {
            var stream = new JagStream();

            Assert.Equal(0, stream.Length);
            Assert.Equal(256, stream.Capacity);
        }

        /// <summary>
        ///     Wrapping an existing buffer must not disturb it. The length setter zero-fills what
        ///     a grow exposes, so a constructor routing through it would erase the data it was
        ///     handed.
        /// </summary>
        [Fact]
        public void Constructor_OverAnExistingBuffer_PreservesItsContents()
        {
            byte[] payload = { 9, 8, 7, 6 };

            Assert.Equal(payload, new JagStream(payload).ToArray());
            Assert.Equal(new byte[] { 8, 7 }, new JagStream(payload, 1, 2).ToArray());
        }

        /// <summary>
        ///     The whole-buffer constructor aliases the caller's array while the slicing one
        ///     copies. Codecs pass sub-streams around freely, so a slice that aliased its parent
        ///     would let an edit to one archive corrupt its neighbour.
        /// </summary>
        [Fact]
        public void Constructor_Slicing_CopiesRatherThanAliasing()
        {
            byte[] source = { 1, 2, 3, 4 };

            var aliasing = new JagStream(source);
            var copying = new JagStream(source, 0, 4);
            source[0] = 99;

            Assert.Equal(99, aliasing.Get(0));
            Assert.Equal(1, copying.Get(0));
        }

        [Fact]
        public void Constructor_SlicingPastTheSourceArray_Throws()
        {
            Assert.Throws<ArgumentException>(() => new JagStream(new byte[2], 1, 5));
        }

        #endregion

        #region Load and Save

        [Fact]
        public void LoadStream_NonExistingFile_ThrowsFileNotFound()
        {
            Assert.Throws<System.IO.FileNotFoundException>(() => JagStream.LoadStream("nonexistent.bin"));
        }

        [Fact]
        public void Save_And_LoadStream_WritesAndReadsFile()
        {
            // Arrange
            var tempPath = TempPath("roundtrip.bin");
            var stream = new JagStream();
            stream.WriteByte(1);

            // Act
            JagStream.Save(stream, tempPath);
            var loaded = JagStream.LoadStream(tempPath);

            // Assert
            Assert.Equal(new byte[] { 1 }, loaded.ToArray());
        }

        [Fact]
        public void Save_CreatesDirectoryIfMissing()
        {
            // Arrange
            string dir = TempPath(Guid.NewGuid().ToString("N"));
            string file = Path.Combine(dir, "test.bin");
            var stream = new JagStream();
            stream.WriteByte(42);

            // Act
            JagStream.Save(stream, file);

            // Assert
            Assert.True(File.Exists(file));
        }

        /// <summary>
        ///     Save writes ToArray, not the backing buffer, so the padding a stream carries above
        ///     Length must never reach the file. Writing capacity instead would append trailing
        ///     zeroes to every archive the editor saves.
        /// </summary>
        [Fact]
        public void Save_StreamWithSpareCapacity_WritesOnlyTheValidRegion()
        {
            string path = TempPath("valid-region.bin");
            var stream = new JagStream(4096);
            stream.WriteMedium(0xABCDEF);

            stream.Save(path);

            Assert.Equal(new byte[] { 0xAB, 0xCD, 0xEF }, File.ReadAllBytes(path));
        }

        [Fact]
        public void Save_Instance_WritesTheSameBytesAsTheStaticOverload()
        {
            string viaInstance = TempPath("instance.bin");
            string viaStatic = TempPath("static.bin");
            var stream = new JagStream();
            stream.WriteInteger(0x11223344);

            stream.Save(viaInstance);
            JagStream.Save(stream, viaStatic);

            Assert.Equal(File.ReadAllBytes(viaStatic), File.ReadAllBytes(viaInstance));
        }

        [Fact]
        public void LoadStream_EmptyFile_ReturnsAnEmptyStream()
        {
            string path = TempPath("empty.bin");
            File.WriteAllBytes(path, Array.Empty<byte>());

            var loaded = JagStream.LoadStream(path);

            Assert.Equal(0, loaded.Length);
            Assert.Equal(0, loaded.Capacity);
            Assert.Equal(-1, loaded.ReadByte());
        }

        /// <summary>
        ///     Save(null, path) raises NullReferenceException where WriteTo(null) raises
        ///     ArgumentNullException. Pinned because it is inconsistent, not because it is right:
        ///     a caller cannot write one catch clause that covers both.
        /// </summary>
        [Fact]
        public void Save_NullStream_ThrowsNullReferenceException()
        {
            Assert.Throws<NullReferenceException>(() => JagStream.Save(null, TempPath("never.bin")));
        }

        #endregion

        #region Buffer and position management

        /// <summary>
        ///     GetBuffer hands out the live array and ToArray hands out a copy. Codecs use both;
        ///     if ToArray ever started aliasing, an edit to a decoded archive would reach back
        ///     into the stream it came from.
        /// </summary>
        [Fact]
        public void GetBuffer_AliasesTheStream_WhileToArrayCopies()
        {
            var stream = Wire(1, 2, 3);
            byte[] copy = stream.ToArray();

            stream.GetBuffer()[0] = 77;

            Assert.Equal(77, stream.Get(0));
            Assert.Equal(1, copy[0]);
        }

        [Fact]
        public void ToArray_ReturnsOnlyTheValidRegionNotTheCapacity()
        {
            var stream = new JagStream(128);
            stream.WriteShort(0x0102);

            Assert.Equal(new byte[] { 0x01, 0x02 }, stream.ToArray());
            Assert.Equal(128, stream.Capacity);
        }

        [Fact]
        public void Flip_AfterWriting_SetsLengthToPositionAndRewinds()
        {
            var stream = new JagStream();
            stream.WriteInteger(0x12345678);

            JagStream returned = stream.Flip();

            Assert.Same(stream, returned);
            Assert.Equal(4, stream.Length);
            Assert.Equal(0, stream.Position);
        }

        /// <summary>
        ///     Flip refuses to truncate a populated stream that has not been written to. Without
        ///     the guard, flipping a freshly loaded stream would set its length to zero and
        ///     silently discard the file that was just read.
        /// </summary>
        [Fact]
        public void Flip_AtPositionZeroWithContent_ThrowsIOException()
        {
            Assert.Throws<IOException>(() => Wire(1, 2).Flip());
        }

        [Fact]
        public void Flip_EmptyStream_IsAllowed()
        {
            var stream = new JagStream();

            stream.Flip();

            Assert.Equal(0, stream.Length);
            Assert.Equal(0, stream.Position);
        }

        [Fact]
        public void Remaining_TracksLengthMinusPosition()
        {
            var stream = Wire(1, 2, 3, 4);

            Assert.Equal(4, stream.Remaining());
            stream.ReadShort();
            Assert.Equal(2, stream.Remaining());
            stream.Seek(4);
            Assert.Equal(0, stream.Remaining());
        }

        /// <param name="offset">
        ///     0 and Length are the two in-bounds extremes. Seeking to Length is legal and leaves
        ///     the stream at EOF, which is how every codec's "did I consume everything" check is
        ///     written.
        /// </param>
        [Theory]
        [InlineData(0)]
        [InlineData(1)]
        [InlineData(4)]
        public void Seek_WithinBounds_MovesPositionAndReturnsIt(int offset)
        {
            var stream = Wire(1, 2, 3, 4);

            long result = stream.Seek(offset);

            Assert.Equal(offset, result);
            Assert.Equal(offset, stream.Position);
        }

        /// <param name="offset">
        ///     -1 and Length+1 are the two off-by-one misses either side of the legal range;
        ///     int.MaxValue is the gross overshoot a corrupt sector pointer produces.
        /// </param>
        [Theory]
        [InlineData(-1)]
        [InlineData(5)]
        [InlineData(int.MaxValue)]
        public void Seek_OutOfBounds_ThrowsIOException(int offset)
        {
            Assert.Throws<IOException>(() => Wire(1, 2, 3, 4).Seek(offset));
        }

        [Fact]
        public void Seek_WithOrigin_ResolvesAgainstBeginCurrentAndEnd()
        {
            var stream = Wire(1, 2, 3, 4);

            Assert.Equal(2, stream.Seek(2, SeekOrigin.Begin));
            Assert.Equal(3, stream.Seek(1, SeekOrigin.Current));
            Assert.Equal(2, stream.Seek(-2, SeekOrigin.End));
        }

        [Fact]
        public void Seek_UnknownOrigin_ThrowsArgumentOutOfRange()
        {
            Assert.Throws<ArgumentOutOfRangeException>(() => new JagStream().Seek(0, (SeekOrigin) 99));
        }

        [Fact]
        public void Seek0_ReturnsToTheStart()
        {
            var stream = Wire(1, 2, 3, 4);
            stream.Seek(3);

            Assert.Equal(0, stream.Seek0());
            Assert.Equal(0, stream.Position);
        }

        /// <summary>
        ///     Position is a public field with no validation, so it can be parked past Length.
        ///     Reads then behave as EOF rather than returning the spare capacity, which is what
        ///     stops an over-seek from silently handing back padding as content.
        /// </summary>
        [Fact]
        public void Position_AssignedPastLength_IsUncheckedAndReadsAsEndOfStream()
        {
            var stream = new JagStream(64);
            stream.WriteByte(1);

            stream.Position = 40;

            Assert.Equal(40, stream.Position);
            Assert.Equal(-1, stream.ReadByte());
            Assert.Throws<EndOfStreamException>(() => stream.ReadUnsignedByte());
        }

        #endregion

        #region Length invariants

        /// <summary>
        ///     Growing the length must allocate the bytes it claims.
        /// </summary>
        /// <remarks>
        ///     Length was a plain field, so this assignment used to make the stream claim bytes
        ///     that were never allocated. The failure that produced was badly misleading: every
        ///     read guards on Length and then indexes the backing array, so the guard passed and
        ///     the indexer threw IndexOutOfRangeException out of a method written to raise
        ///     EndOfStreamException for precisely that case.
        /// </remarks>
        [Fact]
        public void Length_GrownBeyondCapacity_AllocatesAndReadsAsZero()
        {
            var stream = new JagStream(Array.Empty<byte>());

            stream.Length = 18;

            Assert.Equal(18, stream.Length);
            Assert.True(stream.Capacity >= 18, "the backing array must actually hold what Length claims");

            //Reads the whole span rather than throwing, and the new region is zero
            stream.Seek0();
            Assert.Equal(0, stream.ReadMedium());
            Assert.Equal(0, stream.ReadMedium());
        }

        /// <summary>
        ///     A grow must zero the region it exposes, so recycled bytes from an earlier write
        ///     cannot reappear as content.
        /// </summary>
        [Fact]
        public void Length_GrownAfterAShrink_DoesNotResurrectOldBytes()
        {
            var stream = new JagStream();
            stream.WriteInteger(unchecked((int) 0xDEADBEEF));
            stream.Flip();

            stream.Length = 0;
            stream.Length = 4;

            stream.Seek0();
            Assert.Equal(0, stream.ReadInt());
        }

        /// <summary>
        ///     Shrinking must pull the cursor back with it, or Position sits past the end and the
        ///     next read starts outside the stream.
        /// </summary>
        [Fact]
        public void Length_Shrunk_ClampsPosition()
        {
            var stream = new JagStream(new byte[] { 1, 2, 3, 4, 5, 6 });
            stream.Seek(6);

            stream.Length = 2;

            Assert.Equal(2, stream.Position);
            Assert.Equal(0, stream.Remaining());
        }

        [Fact]
        public void Length_SetNegative_Throws()
        {
            var stream = new JagStream();

            Assert.Throws<ArgumentOutOfRangeException>(() => stream.Length = -1);
        }

        /// <summary>
        ///     The core invariant behind the capacity fix: Length can never name a byte the
        ///     backing array does not hold. Every typed read guards on Length and then indexes
        ///     the array directly, so breaking this turns a clean EndOfStreamException into an
        ///     IndexOutOfRangeException raised from deep inside a reader.
        /// </summary>
        /// <param name="target">
        ///     1 sits inside the default capacity; 256 is exactly the default capacity, the
        ///     off-by-one boundary where the doubling decision is made; 257 and 100000 force a
        ///     reallocation.
        /// </param>
        [Theory]
        [InlineData(0)]
        [InlineData(1)]
        [InlineData(256)]
        [InlineData(257)]
        [InlineData(100000)]
        public void Length_GrownToAnySize_NeverExceedsCapacity(int target)
        {
            var stream = new JagStream();

            stream.Length = target;

            Assert.Equal(target, stream.Length);
            Assert.True(stream.Capacity >= stream.Length, "Length must never outrun the backing array");
            Assert.Equal(new byte[target], stream.ToArray());
        }

        /// <summary>
        ///     A grow moves the end of the stream, not the cursor. Codecs set Length to reserve
        ///     space and then keep writing where they left off; pulling Position along would
        ///     silently relocate every subsequent write.
        /// </summary>
        [Fact]
        public void Length_Grown_LeavesPositionWhereItWas()
        {
            var stream = Wire(1, 2);
            stream.Seek(1);

            stream.Length = 10;

            Assert.Equal(1, stream.Position);
        }

        [Fact]
        public void Length_Shrunk_MakesTheTruncatedBytesUnreachable()
        {
            var stream = Wire(1, 2, 3, 4);

            stream.Length = 2;
            stream.Seek0();

            Assert.Equal(new byte[] { 1, 2 }, stream.ToArray());
            Assert.Throws<EndOfStreamException>(() => stream.ReadInt());
            Assert.Throws<ArgumentOutOfRangeException>(() => stream.Get(2));
        }

        /// <summary>
        ///     The regression the capacity fix nearly caused, pinned so it cannot recur. Write
        ///     and WriteByte extend the backing field rather than the property precisely because
        ///     the property zero-fills the region a grow exposes, and here that region IS the
        ///     bytes just written. Routing those two paths through the property would blank every
        ///     write in the codebase while every length assertion still passed.
        /// </summary>
        [Fact]
        public void Write_ExtendingTheStream_DoesNotZeroTheBytesItJustWrote()
        {
            var byteAtATime = new JagStream(2);
            byteAtATime.WriteByte(0xAB);
            byteAtATime.WriteByte(0xCD);
            byteAtATime.WriteByte(0xEF);          // forces a grow

            var spanAtOnce = new JagStream(2);
            spanAtOnce.Write(new byte[] { 0xAB, 0xCD, 0xEF }, 0, 3);

            Assert.Equal(new byte[] { 0xAB, 0xCD, 0xEF }, byteAtATime.ToArray());
            Assert.Equal(new byte[] { 0xAB, 0xCD, 0xEF }, spanAtOnce.ToArray());
        }

        [Fact]
        public void Capacity_WriteBeyondTheBuffer_GrowsByDoublingToAtLeastTheRequirement()
        {
            var stream = new JagStream(4);
            stream.WriteInteger(1);
            Assert.Equal(4, stream.Capacity);

            stream.WriteByte(1);
            Assert.Equal(8, stream.Capacity);

            // Position is 5, so 105 bytes are required. Doubling 8 would not cover it, and the
            // grow takes max(double, required) rather than doubling until it fits.
            stream.Write(new byte[100], 0, 100);
            Assert.Equal(105, stream.Capacity);
        }

        #endregion

        #region Existing round trips retained

        [Fact]
        public void WriteAndReadShort_RoundTripsValue()
        {
            // Arrange
            var stream = new JagStream();
            const short value = 0x1234;

            // Act
            stream.WriteShort(value);
            stream.Seek0();
            int result = stream.ReadUnsignedShort();

            // Assert
            Assert.Equal(value, result);
        }

        [Fact]
        public void WriteAndReadInteger_RoundTripsValue()
        {
            // Arrange
            var stream = new JagStream();
            const int value = 0x12345678;

            // Act
            stream.WriteInteger(value);
            stream.Seek0();
            int result = stream.ReadInt();

            // Assert
            Assert.Equal(value, result);
        }

        [Fact]
        public void ReadJagexString_NullTerminatedAscii_DecodesCorrectly()
        {
            // Arrange
            byte[] bytes = { (byte) 'H', (byte) 'i', 0 };
            var stream = new JagStream(bytes);

            // Act
            string result = stream.ReadJagexString();

            // Assert
            Assert.Equal("Hi", result);
        }

        [Fact]
        public void ReadJagexString_WithExtendedCharacters_DecodesCP1252()
        {
            // Arrange - byte 128 = euro sign, byte 153 = trademark
            byte[] bytes = { 128, (byte) 'x', 153, 0 };
            var stream = new JagStream(bytes);

            // Act
            string result = stream.ReadJagexString();

            // Assert - CP-1252 mapping: 128 -> U+20AC, 153 -> U+2122
            Assert.Equal("\u20ACx\u2122", result);
        }

        [Fact]
        public void ReadUnsignedShortArray_ReadsAllValues()
        {
            // Arrange
            var stream = new JagStream();
            stream.WriteShort((short) 1);
            stream.WriteShort((short) 2);
            stream.Seek0();

            // Act
            int[] result = stream.ReadUnsignedShortArray(2);

            // Assert
            Assert.Equal(new[] { 1, 2 }, result);
        }

        #endregion

        #region Raw byte access

        /// <summary>
        ///     ReadByte is the only reader that reports EOF by value instead of by exception,
        ///     and it must not move the cursor when it does. ItemDefinition's opcode loop reads
        ///     an opcode and breaks on any non-positive result, so 0 (terminator) and -1 (EOF)
        ///     share a path; if EOF advanced Position, a truncated record would leave the cursor
        ///     one byte past the end and the caller's "did I consume it all" check would compare
        ///     against the wrong number.
        ///
        ///     It is also why a bare Position == Length assertion can pass vacuously: repeated
        ///     reads at EOF are idempotent, so the check holds no matter how many times the
        ///     decoder over-read.
        /// </summary>
        [Fact]
        public void ReadByte_AtEndOfStream_ReturnsMinusOneWithoutAdvancingPosition()
        {
            var stream = Wire(7);
            Assert.Equal(7, stream.ReadByte());
            Assert.Equal(1, stream.Position);

            Assert.Equal(-1, stream.ReadByte());
            Assert.Equal(1, stream.Position);

            //Idempotent: over-reading can never push Position past Length
            Assert.Equal(-1, stream.ReadByte());
            Assert.Equal(-1, stream.ReadByte());
            Assert.Equal(1, stream.Position);
            Assert.Equal(stream.Length, stream.Position);
        }

        /// <param name="raw">
        ///     0x7F and 0x80 straddle the sign boundary. ReadByte is the unsigned view, so 0x80
        ///     must come back as 128; anything that sign-extends here turns every high byte in
        ///     the cache into a negative opcode and terminates the decode loop early.
        /// </param>
        [Theory]
        [InlineData(0x00, 0)]
        [InlineData(0x01, 1)]
        [InlineData(0x7F, 127)]
        [InlineData(0x80, 128)]
        [InlineData(0xFF, 255)]
        public void ReadByte_AnyValue_ReturnsItUnsigned(byte raw, int expected)
        {
            Assert.Equal(expected, Wire(raw).ReadByte());
        }

        [Fact]
        public void Read_IntoALargerDestination_ReturnsOnlyWhatWasAvailable()
        {
            var stream = Wire(1, 2, 3);
            byte[] destination = new byte[10];

            int got = stream.Read(destination, 0, 10);

            Assert.Equal(3, got);
            Assert.Equal(3, stream.Position);
            Assert.Equal(new byte[] { 1, 2, 3, 0, 0, 0, 0, 0, 0, 0 }, destination);
        }

        /// <summary>
        ///     Read reports EOF as 0, not -1 like ReadByte. A caller that copied ReadByte's
        ///     convention and tested for -1 would loop forever on a truncated stream.
        /// </summary>
        [Fact]
        public void Read_AtEndOfStream_ReturnsZero()
        {
            var stream = Wire(1);
            stream.Seek(1);

            Assert.Equal(0, stream.Read(new byte[4], 0, 4));
            Assert.Equal(1, stream.Position);
        }

        [Fact]
        public void Read_WithOffsetAndCount_FillsOnlyTheRequestedWindow()
        {
            var stream = Wire(1, 2, 3, 4);
            byte[] destination = new byte[4];

            int got = stream.Read(destination, 1, 2);

            Assert.Equal(2, got);
            Assert.Equal(new byte[] { 0, 1, 2, 0 }, destination);
            Assert.Equal(2, stream.Position);
        }

        [Fact]
        public void Read_WithAWindowPastTheDestination_ThrowsArgumentOutOfRange()
        {
            Assert.Throws<ArgumentOutOfRangeException>(() => Wire(1, 2, 3, 4).Read(new byte[2], 1, 5));
        }

        [Fact]
        public void Write_WithAWindowPastTheSource_ThrowsArgumentOutOfRange()
        {
            Assert.Throws<ArgumentOutOfRangeException>(() => new JagStream().Write(new byte[2], 1, 5));
        }

        /// <summary>
        ///     Writing mid-stream overwrites in place and must not truncate what follows. Codecs
        ///     backfill headers this way: write a placeholder, seek back, write the real value.
        ///     A write that reset Length to Position would discard everything after the patch.
        /// </summary>
        [Fact]
        public void Write_AtANonZeroPosition_OverwritesInPlaceWithoutTruncating()
        {
            var stream = Wire(1, 2, 3, 4, 5);
            stream.Seek(1);

            stream.WriteShort(unchecked((short) 0xAABB));

            Assert.Equal(5, stream.Length);
            Assert.Equal(new byte[] { 1, 0xAA, 0xBB, 4, 5 }, stream.ToArray());
        }

        [Fact]
        public void WriteTo_CopiesTheValidRegionAndLeavesTheSourceUntouched()
        {
            var source = Wire(1, 2, 3);
            source.Seek(3);
            var destination = new JagStream();
            destination.WriteByte(9);

            source.WriteTo(destination);

            Assert.Equal(new byte[] { 9, 1, 2, 3 }, destination.ToArray());
            Assert.Equal(3, source.Position);
            Assert.Equal(4, destination.Position);
        }

        /// <summary>
        ///     WriteTo(null) raises ArgumentNullException where the static Save(null, path)
        ///     raises NullReferenceException. Pinned as a record of that inconsistency.
        /// </summary>
        [Fact]
        public void WriteTo_NullDestination_ThrowsArgumentNullException()
        {
            Assert.Throws<ArgumentNullException>(() => new JagStream().WriteTo(null));
        }

        /// <param name="value">
        ///     0x1FF and -1 both carry bits above the byte. The int overload casts rather than
        ///     validating, so the high bits are dropped silently.
        /// </param>
        [Theory]
        [InlineData(0x00, 0x00)]
        [InlineData(0xFF, 0xFF)]
        [InlineData(0x1FF, 0xFF)]
        [InlineData(-1, 0xFF)]
        [InlineData(256, 0x00)]
        public void WriteByte_IntOverload_TruncatesToTheLowByte(int value, byte expected)
        {
            var stream = new JagStream();

            stream.WriteByte(value);

            Assert.Equal(new[] { expected }, stream.ToArray());
        }

        #endregion

        #region Byte level types

        /// <param name="value">
        ///     The full signed byte range plus both sides of zero. -128 and 127 are the extremes
        ///     the cast has to survive without saturating.
        /// </param>
        [Theory]
        [InlineData((sbyte) 0)]
        [InlineData((sbyte) 1)]
        [InlineData((sbyte) (-1))]
        [InlineData((sbyte) 127)]
        [InlineData((sbyte) (-128))]
        public void WriteSignedByte_And_ReadSignedByte_RoundTrip(sbyte value)
        {
            var stream = new JagStream();

            stream.WriteSignedByte(value);
            stream.Seek0();

            Assert.Equal(value, stream.ReadSignedByte());
        }

        [Theory]
        [InlineData(0x00)]
        [InlineData(0x01)]
        [InlineData(0x7F)]
        [InlineData(0x80)]
        [InlineData(0xFF)]
        public void WriteByte_And_ReadUnsignedByte_RoundTrip(int value)
        {
            var stream = new JagStream();

            stream.WriteByte((byte) value);
            stream.Seek0();

            Assert.Equal(value, stream.ReadUnsignedByte());
        }

        /// <summary>
        ///     One byte, two answers, and the codecs pick between them per opcode. Any opcode
        ///     that reads the wrong one is indistinguishable from the right one until a value
        ///     reaches 0x80, at which point the divergence is a 256-wide error.
        /// </summary>
        [Fact]
        public void ReadSignedByte_And_ReadUnsignedByte_AgreeBelow0x80AndDivergeAtOrAbove()
        {
            Assert.Equal(127, Wire(0x7F).ReadSignedByte());
            Assert.Equal(127, Wire(0x7F).ReadUnsignedByte());

            Assert.Equal(-128, Wire(0x80).ReadSignedByte());
            Assert.Equal(128, Wire(0x80).ReadUnsignedByte());

            Assert.Equal(-1, Wire(0xFF).ReadSignedByte());
            Assert.Equal(255, Wire(0xFF).ReadUnsignedByte());
        }

        /// <summary>
        ///     Both peeks must leave the cursor alone, and they disagree on sign for the same
        ///     reason the two readers do. Every smart reader and several opcode dispatchers
        ///     branch on a peeked byte and then re-read it, so a peek that advanced would drop
        ///     that byte from the stream entirely.
        /// </summary>
        [Fact]
        public void Peek_And_PeekUnsignedByte_DoNotAdvanceAndDisagreeOnSign()
        {
            var stream = Wire(0x80, 0x01);

            Assert.Equal(-128, stream.Peek());
            Assert.Equal(0, stream.Position);
            Assert.Equal(128, stream.PeekUnsignedByte());
            Assert.Equal(0, stream.Position);

            //The peeked byte is still there to be consumed
            Assert.Equal(128, stream.ReadUnsignedByte());
        }

        [Fact]
        public void Peek_AtEndOfStream_ThrowsWithoutMovingPosition()
        {
            var empty = new JagStream();

            Assert.Throws<EndOfStreamException>(() => empty.Peek());
            Assert.Throws<EndOfStreamException>(() => empty.PeekUnsignedByte());
            Assert.Equal(0, empty.Position);
        }

        [Fact]
        public void ReadSignedByte_AtEndOfStream_ThrowsEndOfStream()
        {
            Assert.Throws<EndOfStreamException>(() => new JagStream().ReadSignedByte());
        }

        [Fact]
        public void ReadUnsignedByte_AtEndOfStream_ThrowsEndOfStream()
        {
            Assert.Throws<EndOfStreamException>(() => new JagStream().ReadUnsignedByte());
        }

        #endregion

        #region Short

        /// <param name="value">
        ///     0x7FFF/0x8000 is the sign boundary. A related audit found roughly 15 opcodes
        ///     where a signed-versus-unsigned mix-up is unobservable today only because no value
        ///     in the shipped cache reaches 0x8000, so the boundary is pinned deliberately.
        /// </param>
        [Theory]
        [InlineData(0x0000)]
        [InlineData(0x0001)]
        [InlineData(0x7FFF)]
        [InlineData(0x8000)]
        [InlineData(0xFFFF)]
        public void WriteShort_And_ReadUnsignedShort_RoundTrip(int value)
        {
            var stream = new JagStream();

            stream.WriteShort((short) value);
            stream.Seek0();

            Assert.Equal(value, stream.ReadUnsignedShort());
        }

        [Theory]
        [InlineData((short) 0)]
        [InlineData((short) 1)]
        [InlineData((short) (-1))]
        [InlineData((short) 32767)]
        [InlineData((short) (-32768))]
        public void WriteShort_And_ReadShort_RoundTripSignedValues(short value)
        {
            var stream = new JagStream();

            stream.WriteShort(value);
            stream.Seek0();

            Assert.Equal(value, stream.ReadShort());
        }

        /// <summary>
        ///     The same two bytes, read two ways. Below 0x8000 the readers are
        ///     indistinguishable, which is exactly what makes a wrong choice survive review;
        ///     at and above it they differ by 65536.
        /// </summary>
        /// <param name="high">First wire byte.</param>
        /// <param name="low">Second wire byte.</param>
        /// <param name="signed">What ReadShort returns.</param>
        /// <param name="unsigned">What ReadUnsignedShort returns.</param>
        [Theory]
        [InlineData(0x00, 0x00, 0, 0)]
        [InlineData(0x7F, 0xFF, 32767, 32767)]
        [InlineData(0x80, 0x00, -32768, 32768)]
        [InlineData(0xFF, 0xFF, -1, 65535)]
        public void ReadShort_And_ReadUnsignedShort_DivergeAtTheSignBoundary(byte high, byte low, int signed, int unsigned)
        {
            Assert.Equal(signed, Wire(high, low).ReadShort());
            Assert.Equal(unsigned, Wire(high, low).ReadUnsignedShort());
        }

        [Fact]
        public void WriteShort_IsBigEndian()
        {
            var stream = new JagStream();

            stream.WriteShort(0x1234);

            Assert.Equal(new byte[] { 0x12, 0x34 }, stream.ToArray());
        }

        [Fact]
        public void WriteShort_IntOverload_TruncatesToTheLowTwoBytes()
        {
            var stream = new JagStream();

            stream.WriteShort(0x12345);

            Assert.Equal(new byte[] { 0x23, 0x45 }, stream.ToArray());
        }

        [Fact]
        public void ReadUnsignedShort_WithOnlyOneByteLeft_ThrowsEndOfStream()
        {
            Assert.Throws<EndOfStreamException>(() => Wire(1).ReadUnsignedShort());
        }

        #endregion

        #region Medium

        [Theory]
        [InlineData(0x000000)]
        [InlineData(0x000001)]
        [InlineData(0x7FFFFF)]
        [InlineData(0x800000)]
        [InlineData(0xFFFFFF)]
        public void WriteMedium_And_ReadMedium_RoundTrip(int value)
        {
            var stream = new JagStream();

            stream.WriteMedium(value);
            stream.Seek0();

            Assert.Equal(value, stream.ReadMedium());
        }

        /// <summary>
        ///     ReadMedium has no signed counterpart: it ORs three bytes into an int and can only
        ///     ever return 0..16777215. This is the sector-pointer and index-record encoding, so
        ///     state it as a property of the format rather than of one call site. A caller that
        ///     wrote -1 as a sentinel gets 16777215 back, which is a valid-looking sector number
        ///     roughly 8 GB into the data file.
        /// </summary>
        [Theory]
        [InlineData(-1, 16777215)]
        [InlineData(-2, 16777214)]
        [InlineData(int.MinValue, 0)]
        public void WriteMedium_NegativeValue_ReadsBackUnsigned(int written, int readBack)
        {
            var stream = new JagStream();

            stream.WriteMedium(written);
            stream.Seek0();

            Assert.Equal(readBack, stream.ReadMedium());
            Assert.True(stream.Position == 3);
        }

        [Fact]
        public void WriteMedium_IsBigEndianAndDropsTheTopByte()
        {
            var stream = new JagStream();

            stream.WriteMedium(unchecked((int) 0xFF123456));

            Assert.Equal(new byte[] { 0x12, 0x34, 0x56 }, stream.ToArray());
        }

        [Fact]
        public void ReadMedium_WithTwoBytesLeft_ThrowsEndOfStream()
        {
            Assert.Throws<EndOfStreamException>(() => Wire(1, 2).ReadMedium());
        }

        #endregion

        #region Int and long

        [Theory]
        [InlineData(0)]
        [InlineData(1)]
        [InlineData(-1)]
        [InlineData(int.MaxValue)]
        [InlineData(int.MinValue)]
        public void WriteInteger_And_ReadInt_RoundTrip(int value)
        {
            var stream = new JagStream();

            stream.WriteInteger(value);
            stream.Seek0();

            Assert.Equal(value, stream.ReadInt());
        }

        [Fact]
        public void WriteInteger_IsBigEndian()
        {
            var stream = new JagStream();

            stream.WriteInteger(0x12345678);

            Assert.Equal(new byte[] { 0x12, 0x34, 0x56, 0x78 }, stream.ToArray());
        }

        /// <summary>
        ///     The uint overload exists so callers do not have to cast, and it must lay down the
        ///     identical four bytes. If the two ever diverged, a CRC written through one overload
        ///     would fail verification when read back through the other.
        /// </summary>
        [Theory]
        [InlineData(0u)]
        [InlineData(1u)]
        [InlineData(0x7FFFFFFFu)]
        [InlineData(0x80000000u)]
        [InlineData(uint.MaxValue)]
        public void WriteInteger_UnsignedOverload_EmitsTheSameBytesAsTheSignedOne(uint value)
        {
            var unsigned = new JagStream();
            var signed = new JagStream();

            unsigned.WriteInteger(value);
            signed.WriteInteger(unchecked((int) value));

            Assert.Equal(signed.ToArray(), unsigned.ToArray());

            unsigned.Seek0();
            Assert.Equal(unchecked((int) value), unsigned.ReadInt());
        }

        [Fact]
        public void ReadInt_WithThreeBytesLeft_ThrowsEndOfStream()
        {
            Assert.Throws<EndOfStreamException>(() => Wire(1, 2, 3).ReadInt());
        }

        /// <summary>
        ///     There is no ReadLong, so WriteLong is a write-only primitive and the only way to
        ///     verify it is against wire bytes. If a ReadLong is ever added, this is the encoding
        ///     it has to match: eight bytes, big-endian, two's complement.
        /// </summary>
        /// <param name="value">
        ///     long.MinValue and -1 are the two values where a byte-at-a-time implementation is
        ///     most likely to get sign handling wrong.
        /// </param>
        [Theory]
        [InlineData(0L, "00-00-00-00-00-00-00-00")]
        [InlineData(1L, "00-00-00-00-00-00-00-01")]
        [InlineData(-1L, "FF-FF-FF-FF-FF-FF-FF-FF")]
        [InlineData(long.MaxValue, "7F-FF-FF-FF-FF-FF-FF-FF")]
        [InlineData(long.MinValue, "80-00-00-00-00-00-00-00")]
        public void WriteLong_HasNoReader_SoPinTheWireEncoding(long value, string expected)
        {
            var stream = new JagStream();

            stream.WriteLong(value);

            Assert.Equal(8, stream.Length);
            Assert.Equal(expected, BitConverter.ToString(stream.ToArray()));
        }

        [Fact]
        public void WriteLong_ReadBackAsTwoInts_RecomposesTheOriginal()
        {
            var stream = new JagStream();
            stream.WriteLong(0x0123456789ABCDEFL);
            stream.Seek0();

            long high = (uint) stream.ReadInt();
            long low = (uint) stream.ReadInt();

            Assert.Equal(0x0123456789ABCDEFL, (high << 32) | low);
        }

        #endregion

        #region ReadBytesAsInt

        /// <param name="count">
        ///     1 to 4 covers every width a caller can ask for without overflowing the int the
        ///     method returns.
        /// </param>
        [Theory]
        [InlineData(1, 0x12)]
        [InlineData(2, 0x1234)]
        [InlineData(3, 0x123456)]
        [InlineData(4, 0x12345678)]
        public void ReadBytesAsInt_AnyWidth_ReadsBigEndian(int count, int expected)
        {
            var stream = Wire(0x12, 0x34, 0x56, 0x78);

            Assert.Equal(expected, stream.ReadBytesAsInt(count));
            Assert.Equal(count, stream.Position);
        }

        /// <summary>
        ///     At four bytes the accumulator shifts into the sign bit, so the method is unsigned
        ///     for widths 1 to 3 and signed at 4. Any caller using it as a length or an offset
        ///     has to know that.
        /// </summary>
        [Fact]
        public void ReadBytesAsInt_FourBytesOfFF_ReturnsMinusOne()
        {
            Assert.Equal(-1, Wire(0xFF, 0xFF, 0xFF, 0xFF).ReadBytesAsInt(4));
            Assert.Equal(16777215, Wire(0xFF, 0xFF, 0xFF, 0xFF).ReadBytesAsInt(3));
        }

        [Fact]
        public void ReadBytesAsInt_ZeroWidth_ReturnsZeroAndConsumesNothing()
        {
            var stream = Wire(1, 2);

            Assert.Equal(0, stream.ReadBytesAsInt(0));
            Assert.Equal(0, stream.Position);
        }

        [Fact]
        public void ReadBytesAsInt_PastTheEnd_ThrowsEndOfStream()
        {
            Assert.Throws<EndOfStreamException>(() => Wire(1, 2).ReadBytesAsInt(4));
        }

        #endregion

        #region VarInt

        /// <summary>
        ///     ReadVarInt and WriteVarInt have no callers anywhere in the repository. They are
        ///     covered so that if a codec ever adopts them the encoding is already pinned, and
        ///     because the MSB-first ordering here is the opposite of the LSB-first scheme most
        ///     "varint" implementations use.
        /// </summary>
        /// <param name="value">
        ///     127/128 is the one-to-two byte boundary. -1 and int.MinValue exercise the
        ///     unsigned right shift in the writer against the sign-extending left shift in the
        ///     reader, which is where a MIDI-style VLQ most often loses negative values.
        /// </param>
        [Theory]
        [InlineData(0)]
        [InlineData(1)]
        [InlineData(127)]
        [InlineData(128)]
        [InlineData(16383)]
        [InlineData(16384)]
        [InlineData(int.MaxValue)]
        [InlineData(-1)]
        [InlineData(int.MinValue)]
        public void WriteVarInt_And_ReadVarInt_RoundTrip(int value)
        {
            var stream = new JagStream();

            stream.WriteVarInt(value);
            stream.Seek0();

            Assert.Equal(value, stream.ReadVarInt());
        }

        /// <param name="value">Chosen to show the width boundaries: 1, 2 and 5 byte forms.</param>
        [Theory]
        [InlineData(0, "00")]
        [InlineData(127, "7F")]
        [InlineData(128, "81-00")]
        [InlineData(-1, "8F-FF-FF-FF-7F")]
        [InlineData(int.MinValue, "88-80-80-80-00")]
        public void WriteVarInt_EmitsMsbFirstSevenBitGroupsWithAContinuationFlag(int value, string expected)
        {
            var stream = new JagStream();

            stream.WriteVarInt(value);

            Assert.Equal(expected, BitConverter.ToString(stream.ToArray()));
        }

        [Fact]
        public void ReadVarInt_UnterminatedSequence_ThrowsEndOfStream()
        {
            Assert.Throws<EndOfStreamException>(() => Wire(0x80, 0x80).ReadVarInt());
        }

        /// <summary>
        ///     ReadVarInt has no width limit. It keeps shifting left for as long as the
        ///     continuation bit is set, so a corrupt or hostile stream can push every meaningful
        ///     bit off the top of the accumulator and the method returns a plausible small number
        ///     instead of rejecting the input. Six groups is enough: this wire encodes 2^35 and
        ///     decodes to 0.
        /// </summary>
        [Fact]
        public void ReadVarInt_SequenceWiderThanThirtyTwoBits_SilentlyOverflows_DocumentsKnownDefect()
        {
            var stream = Wire(0x81, 0x80, 0x80, 0x80, 0x80, 0x00);

            Assert.Equal(0, stream.ReadVarInt());
            Assert.Equal(6, stream.Position);
        }

        #endregion
    }
}
