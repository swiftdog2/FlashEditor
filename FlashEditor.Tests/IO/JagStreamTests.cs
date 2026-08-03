using FlashEditor;
using FlashEditor.Utils;
using System;
using System.Buffers;
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
    /// Nine tests here were once named *_DocumentsKnownDefect, pinning behaviour that was known
    /// to be wrong so that any fix would show up as a deliberate, visible test change rather
    /// than a silent behaviour swap. All nine defects have since been fixed and every one of
    /// those tests now asserts the corrected behaviour. Their doc comments still describe the
    /// defect that was there, because knowing what a method used to do wrong is what stops it
    /// being reintroduced.
    ///
    /// A note on the surface, because it is asymmetric and the asymmetry drives several tests
    /// below: there is a WriteLong with no ReadLong, a ReadString2 with no WriteString2, and
    /// ReadSignedSmart and ReadSpecialSmart still have no writers. Where no inverse exists the
    /// test asserts against hand-built wire bytes instead.
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

        /// <summary>
        ///     The span overloads are the real implementations; the array overloads forward to
        ///     them. Exercised directly so a change to either forwarding wrapper cannot hide a
        ///     change to the primitive underneath.
        /// </summary>
        [Fact]
        public void ReadAndWrite_SpanOverloads_AreTheSameAsTheArrayOverloads()
        {
            var stream = new JagStream();

            stream.Write(new ReadOnlySpan<byte>(new byte[] { 1, 2, 3, 4 }));
            Assert.Equal(4, stream.Length);

            stream.Seek0();
            Span<byte> destination = new byte[3];
            int got = stream.Read(destination);

            Assert.Equal(3, got);
            Assert.Equal(new byte[] { 1, 2, 3 }, destination.ToArray());
            Assert.Equal(3, stream.Position);
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
            Assert.Equal(3, stream.Position);
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
        ///     ReadVarInt and WriteVarInt are the MIDI delta-time codec on the track path
        ///     (Track.cs:218 and :343). This comment used to say they had no callers at all,
        ///     which was true when it was written and stopped being true when index 6 landed.
        ///     The ordering is worth pinning regardless: MSB-first here, the opposite of the
        ///     LSB-first scheme most "varint" implementations use.
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
        ///     ReadVarInt used to have no width limit. It kept shifting left for as long as the
        ///     continuation bit was set, so a corrupt or hostile stream could push every
        ///     meaningful bit off the top of the accumulator and the method returned a plausible
        ///     small number instead of rejecting the input. Six groups is enough: this wire
        ///     encodes 2^35, and it used to decode to 0 with no error at all. It is now reported
        ///     as the malformed wire data it is.
        /// </summary>
        [Fact]
        public void ReadVarInt_SequenceWiderThanThirtyTwoBits_ThrowsInvalidData()
        {
            var stream = Wire(0x81, 0x80, 0x80, 0x80, 0x80, 0x00);

            Assert.Throws<InvalidDataException>(() => stream.ReadVarInt());
        }

        /// <summary>
        ///     The width guard has to reject only what genuinely will not fit. Five groups is the
        ///     widest form the writer emits, and its first group carries the top four bits, so
        ///     0x0F is the last first-group value that fits in 32 bits and 0x10 is the first that
        ///     does not. An over-eager guard would break the -1 and int.MinValue round-trips.
        /// </summary>
        [Theory]
        [InlineData(new byte[] { 0x8F, 0xFF, 0xFF, 0xFF, 0x7F }, true)]
        [InlineData(new byte[] { 0x88, 0x80, 0x80, 0x80, 0x00 }, true)]
        [InlineData(new byte[] { 0x90, 0x80, 0x80, 0x80, 0x00 }, false)]
        public void ReadVarInt_AcceptsTheWidestFormThatStillFitsInThirtyTwoBits(byte[] wire, bool fits)
        {
            var stream = Wire(wire);

            if (fits)
                stream.ReadVarInt();
            else
                Assert.Throws<InvalidDataException>(() => stream.ReadVarInt());
        }

        #endregion

        #region Smart encodings

        /// <summary>
        ///     The 127/128 boundary is the whole encoding: the branch is taken on the first
        ///     byte's high bit, peeked via Get before anything is consumed. 0x7F is the last
        ///     one-byte form and 0x80 is the first two-byte form, so an off-by-one here shifts
        ///     the reader by a byte and desynchronises the rest of the record.
        /// </summary>
        /// <param name="wire">Hand-built bytes: one byte for the short form, two for the long.</param>
        /// <param name="expected">
        ///     One-byte values carry a -64 bias, two-byte values a -0xC000 bias, so 0x7F reads
        ///     as 63 while the very next encodable value, 0x8000, reads as -16384.
        /// </param>
        [Theory]
        [InlineData(new byte[] { 0x00 }, -64)]
        [InlineData(new byte[] { 0x40 }, 0)]
        [InlineData(new byte[] { 0x7F }, 63)]
        [InlineData(new byte[] { 0x80, 0x00 }, -16384)]
        [InlineData(new byte[] { 0xC0, 0x00 }, 0)]
        [InlineData(new byte[] { 0xFF, 0xFF }, 16383)]
        public void ReadSmart_AppliesTheSixtyFourAndC000Biases(byte[] wire, int expected)
        {
            var stream = new JagStream(wire);

            Assert.Equal(expected, stream.ReadSmart());
            Assert.Equal(wire.Length, stream.Position);
        }

        /// <param name="expected">
        ///     Unbiased in the one-byte form, and biased by -32768 rather than -0xC000 in the
        ///     two-byte form. That 16384 difference from ReadSmart is the entire distinction
        ///     between the two readers.
        /// </param>
        [Theory]
        [InlineData(new byte[] { 0x00 }, 0)]
        [InlineData(new byte[] { 0x01 }, 1)]
        [InlineData(new byte[] { 0x7F }, 127)]
        [InlineData(new byte[] { 0x80, 0x00 }, 0)]
        [InlineData(new byte[] { 0x80, 0x80 }, 128)]
        [InlineData(new byte[] { 0xFF, 0xFF }, 32767)]
        public void ReadUnsignedSmart_AppliesNoBiasAndThe32768Bias(byte[] wire, int expected)
        {
            var stream = new JagStream(wire);

            Assert.Equal(expected, stream.ReadUnsignedSmart());
            Assert.Equal(wire.Length, stream.Position);
        }

        /// <summary>
        ///     The two biases in one assertion. ReadSmart subtracts 0xC000 and ReadUnsignedSmart
        ///     subtracts 0x8000, so on identical two-byte input they differ by exactly 16384
        ///     while agreeing to within a fixed 64 on one-byte input. Swapping one for the other
        ///     at a call site is a silent, uniform offset rather than a crash.
        /// </summary>
        [Fact]
        public void ReadSmart_And_ReadUnsignedSmart_DifferBy16384OnTheTwoByteForm()
        {
            byte[] wire = { 0xC3, 0x21 };

            Assert.Equal(16384, Wire(wire[0], wire[1]).ReadUnsignedSmart() - Wire(wire[0], wire[1]).ReadSmart());

            //One-byte form: the difference is the fixed -64 bias instead
            Assert.Equal(64, Wire(0x33).ReadUnsignedSmart() - Wire(0x33).ReadSmart());
        }

        /// <summary>
        ///     ReadShortSmart is a pure alias of ReadSmart, and ModelDefinition calls it about
        ///     thirty times. If it ever acquired a bias of its own, every vertex and texture
        ///     coordinate in the model codec would shift by that amount at once, so the identity
        ///     is asserted across the whole encodable range rather than at one point.
        /// </summary>
        [Theory]
        [InlineData(new byte[] { 0x00 })]
        [InlineData(new byte[] { 0x7F })]
        [InlineData(new byte[] { 0x80, 0x00 })]
        [InlineData(new byte[] { 0xC0, 0x00 })]
        [InlineData(new byte[] { 0xFF, 0xFF })]
        public void ReadShortSmart_IsIdenticalToReadSmart(byte[] wire)
        {
            var viaAlias = new JagStream(wire);
            var viaSmart = new JagStream(wire);

            Assert.Equal(viaSmart.ReadSmart(), viaAlias.ReadShortSmart());
            Assert.Equal(viaSmart.Position, viaAlias.Position);
        }

        /// <summary>
        ///     ReadSignedSmart is a zig-zag decode layered on ReadUnsignedSmart, so it alternates
        ///     sign as the encoded value counts up. It is the delta encoding, and a reader that
        ///     dropped the zig-zag would still decode 0 correctly, which is why the odd values
        ///     are pinned too.
        /// </summary>
        /// <param name="encoded">The unsigned smart value that the zig-zag is applied to.</param>
        [Theory]
        [InlineData(0, 0)]
        [InlineData(1, -1)]
        [InlineData(2, 1)]
        [InlineData(3, -2)]
        [InlineData(4, 2)]
        [InlineData(127, -64)]
        public void ReadSignedSmart_ZigZagDecodesTheUnsignedSmart(byte encoded, int expected)
        {
            Assert.Equal(expected, Wire(encoded).ReadSignedSmart());
        }

        [Fact]
        public void ReadSignedSmart_TwoByteForm_ZigZagDecodesTheBiasedValue()
        {
            // 0x8005 reads as unsigned smart 5, which zig-zag decodes to -3
            Assert.Equal(-3, Wire(0x80, 0x05).ReadSignedSmart());
        }

        /// <param name="expected">
        ///     Special smart is biased by -1 in the one-byte form and -32769 in the two-byte
        ///     form, so 0x00 and 0x8000 both decode to -1. That collision is deliberate in the
        ///     format: it is the "absent" sentinel.
        /// </param>
        [Theory]
        [InlineData(new byte[] { 0x00 }, -1)]
        [InlineData(new byte[] { 0x01 }, 0)]
        [InlineData(new byte[] { 0x7F }, 126)]
        [InlineData(new byte[] { 0x80, 0x00 }, -1)]
        [InlineData(new byte[] { 0xFF, 0xFF }, 32766)]
        public void ReadSpecialSmart_AppliesTheOneAnd32769Biases(byte[] wire, int expected)
        {
            var stream = new JagStream(wire);

            Assert.Equal(expected, stream.ReadSpecialSmart());
            Assert.Equal(wire.Length, stream.Position);
        }

        /// <param name="value">
        ///     127 is the last single-byte form and 128 the first two-byte form, the only
        ///     boundary the writer has. 32767 is the largest value it can encode.
        /// </param>
        [Theory]
        [InlineData(0)]
        [InlineData(1)]
        [InlineData(127)]
        [InlineData(128)]
        [InlineData(255)]
        [InlineData(32767)]
        public void WriteUnsignedSmart_And_ReadUnsignedSmart_RoundTrip(int value)
        {
            var stream = new JagStream();

            stream.WriteUnsignedSmart(value);
            stream.Seek0();

            Assert.Equal(value, stream.ReadUnsignedSmart());
            Assert.Equal(0, stream.Remaining());
        }

        [Theory]
        [InlineData(0, 1)]
        [InlineData(127, 1)]
        [InlineData(128, 2)]
        [InlineData(32767, 2)]
        public void WriteUnsignedSmart_ChoosesTheWidthFromThe127Boundary(int value, int expectedLength)
        {
            var stream = new JagStream();

            stream.WriteUnsignedSmart(value);

            Assert.Equal(expectedLength, stream.Length);
        }

        /// <summary>
        ///     WriteUnsignedSmart used to validate nothing. Below 0 it took the single-byte
        ///     branch and emitted a byte with
        ///     the high bit set, which the reader then treats as the first half of a two-byte
        ///     form; at or above 32768 the "+ 32768" wrapped the short. -1 wrote "FF", 32768
        ///     wrote "00-00" and 65535 wrote "7F-FF" - none of which encode the value asked for.
        ///     Both cases corrupted the stream silently and desynchronised everything after them,
        ///     which is far worse than a loud rejection. It now rejects.
        /// </summary>
        /// <param name="value">
        ///     -1 took the wrong branch; 32768 is the first value the bias wrapped; 65535 wrapped
        ///     to 0x7FFF and read back as 127 with a byte left over.
        /// </param>
        [Theory]
        [InlineData(-1)]
        [InlineData(32768)]
        [InlineData(65535)]
        [InlineData(int.MinValue)]
        [InlineData(int.MaxValue)]
        public void WriteUnsignedSmart_OutOfRangeValue_ThrowsArgumentOutOfRange(int value)
        {
            var stream = new JagStream();

            Assert.Throws<ArgumentOutOfRangeException>(() => stream.WriteUnsignedSmart(value));

            //Nothing was written, so the stream is still where the caller left it
            Assert.Equal(0, stream.Length);
            Assert.Equal(0, stream.Position);
        }

        /// <summary>
        ///     ReadSpecialSmart used to index the backing array directly while every other smart
        ///     reader peeked through Get. Past the end of the buffer that raised
        ///     IndexOutOfRangeException, which is not the exception any sibling raises and not one
        ///     a caller would think to catch. It now reports the condition the same way they do.
        /// </summary>
        [Fact]
        public void ReadSpecialSmart_PastCapacity_ThrowsArgumentOutOfRangeLikeItsSiblings()
        {
            var stream = Wire(0x05);
            stream.ReadSpecialSmart();

            Assert.Throws<ArgumentOutOfRangeException>(() => stream.ReadSpecialSmart());

            //The same condition, reported identically by every other smart reader
            var other = Wire(0x05);
            other.Seek(1);
            Assert.Throws<ArgumentOutOfRangeException>(() => other.ReadSmart());
            Assert.Throws<ArgumentOutOfRangeException>(() => other.ReadUnsignedSmart());
            Assert.Throws<ArgumentOutOfRangeException>(() => other.ReadShortSmart());
            Assert.Throws<ArgumentOutOfRangeException>(() => other.ReadSignedSmart());
        }

        /// <summary>
        ///     The worse half of the same defect, and the reason it was worth fixing rather than
        ///     tolerating. With spare capacity behind the write cursor, the raw index used to read
        ///     a byte that was not part of the stream at all, choose its branch from that padding,
        ///     and return -2 with no error - a value nothing downstream could tell apart from a
        ///     genuine one. Bounds-checking against Length rather than the array makes it throw.
        /// </summary>
        [Fact]
        public void ReadSpecialSmart_PastLengthWithinCapacity_ThrowsRatherThanReadingPadding()
        {
            var stream = new JagStream(16);
            stream.WriteByte(0x0A);
            stream.Seek0();
            stream.ReadSpecialSmart();
            Assert.Equal(1, stream.Position);

            //Position is at Length with 15 bytes of spare capacity behind it
            Assert.Equal(1, stream.Length);
            Assert.True(stream.Capacity > stream.Length);

            Assert.Throws<ArgumentOutOfRangeException>(() => stream.ReadSpecialSmart());
            Assert.Equal(1, stream.Position);
        }

        [Fact]
        public void SmartReaders_AtEndOfStream_ThrowArgumentOutOfRange()
        {
            Assert.Throws<ArgumentOutOfRangeException>(() => new JagStream().ReadSmart());
            Assert.Throws<ArgumentOutOfRangeException>(() => new JagStream().ReadUnsignedSmart());
            Assert.Throws<ArgumentOutOfRangeException>(() => new JagStream().ReadShortSmart());
            Assert.Throws<ArgumentOutOfRangeException>(() => new JagStream().ReadSignedSmart());
        }

        [Fact]
        public void ReadSmart_TwoByteFormWithOnlyOneByteLeft_ThrowsEndOfStream()
        {
            Assert.Throws<EndOfStreamException>(() => Wire(0x80).ReadSmart());
            Assert.Throws<EndOfStreamException>(() => Wire(0x80).ReadUnsignedSmart());
        }

        /// <param name="value">
        ///     The boundaries of both widths. -64 and 63 are the last values the one-byte form
        ///     holds, -65 and 64 the first that need two, and -16384/16383 are the extremes of
        ///     the two-byte form - which stops at -16384 rather than the -49152 a bare
        ///     "u16 - 49152" implies, because the reader only takes that branch when the leading
        ///     byte has its high bit set.
        /// </param>
        [Theory]
        [InlineData(-16384)]
        [InlineData(-16383)]
        [InlineData(-65)]
        [InlineData(-64)]
        [InlineData(-1)]
        [InlineData(0)]
        [InlineData(1)]
        [InlineData(63)]
        [InlineData(64)]
        [InlineData(16383)]
        public void WriteSmart_And_ReadSmart_RoundTrip(int value)
        {
            var stream = new JagStream();

            stream.WriteSmart(value);
            stream.Seek0();

            Assert.Equal(value, stream.ReadSmart());
            Assert.Equal(0, stream.Remaining());
        }

        /// <summary>
        ///     Shortest-form is the default because it is what Jagex's encoder emitted: over all
        ///     359,931 index-0 frame files, none of the 11,871,643 two-byte signed smarts holds a
        ///     value the one-byte form could have carried. A writer that widened anything inside
        ///     -64 to 63 would fail the byte-identity sweep on every one of those files.
        /// </summary>
        [Theory]
        [InlineData(-16384, 2)]
        [InlineData(-65, 2)]
        [InlineData(-64, 1)]
        [InlineData(0, 1)]
        [InlineData(63, 1)]
        [InlineData(64, 2)]
        [InlineData(16383, 2)]
        public void WriteSmart_ChoosesTheNarrowestFormThatHoldsTheValue(int value, int expectedLength)
        {
            var stream = new JagStream();

            stream.WriteSmart(value);

            Assert.Equal(expectedLength, stream.Length);
        }

        /// <summary>
        ///     Against hand-built bytes rather than against our own reader, because a writer and
        ///     reader that share a wrong bias round-trip perfectly. These are the client's:
        ///     value + 64 in one byte, value + 0xC000 in two.
        /// </summary>
        [Theory]
        [InlineData(-64, "00")]
        [InlineData(0, "40")]
        [InlineData(63, "7F")]
        [InlineData(-16384, "80-00")]
        [InlineData(-65, "BF-BF")]
        [InlineData(64, "C0-40")]
        [InlineData(16383, "FF-FF")]
        public void WriteSmart_EmitsTheClientsSixtyFourAndC000Biases(int value, string expected)
        {
            var stream = new JagStream();

            stream.WriteSmart(value);

            Assert.Equal(expected, BitConverter.ToString(stream.ToArray()));
        }

        /// <param name="wire">
        ///     Both widths, including the two-byte encodings of values the one-byte form could
        ///     have held. Those are the only ones where the width is information the decoded
        ///     value does not carry.
        /// </param>
        [Theory]
        [InlineData(new byte[] { 0x00 }, JagStream.SmartWidth.OneByte, -64)]
        [InlineData(new byte[] { 0x40 }, JagStream.SmartWidth.OneByte, 0)]
        [InlineData(new byte[] { 0x7F }, JagStream.SmartWidth.OneByte, 63)]
        [InlineData(new byte[] { 0x80, 0x00 }, JagStream.SmartWidth.TwoByte, -16384)]
        [InlineData(new byte[] { 0xBF, 0xC0 }, JagStream.SmartWidth.TwoByte, -64)]
        [InlineData(new byte[] { 0xC0, 0x00 }, JagStream.SmartWidth.TwoByte, 0)]
        [InlineData(new byte[] { 0xFF, 0xFF }, JagStream.SmartWidth.TwoByte, 16383)]
        public void ReadSmart_ReportsTheWidthItConsumed(byte[] wire, JagStream.SmartWidth expectedWidth, int expectedValue)
        {
            var stream = new JagStream(wire);

            Assert.Equal(expectedValue, stream.ReadSmart(out var width));
            Assert.Equal(expectedWidth, width);
            Assert.Equal(wire.Length, stream.Position);
        }

        /// <summary>
        ///     The contract the width overload exists for. -64 to 63 has two legal encodings, so
        ///     a decoder that keeps only the value cannot reproduce a file that used the long
        ///     form; recording the width and replaying it can. Three of these six wires are the
        ///     long form of a value the short form holds, and shortest-form would rewrite all
        ///     three.
        /// </summary>
        [Theory]
        [InlineData(new byte[] { 0x00 }, -64)]
        [InlineData(new byte[] { 0xBF, 0xC0 }, -64)]
        [InlineData(new byte[] { 0x40 }, 0)]
        [InlineData(new byte[] { 0xC0, 0x00 }, 0)]
        [InlineData(new byte[] { 0x7F }, 63)]
        [InlineData(new byte[] { 0xC0, 0x3F }, 63)]
        public void ReadSmart_ThenWriteSmartWithTheRecordedWidth_ReproducesTheBytes(byte[] wire, int expected)
        {
            int value = new JagStream(wire).ReadSmart(out var width);
            Assert.Equal(expected, value);

            var replay = new JagStream();
            replay.WriteSmart(value, width);

            Assert.Equal(wire, replay.ToArray());
        }

        /// <summary>
        ///     The half of the previous test that a shortest-form-only writer gets wrong: the
        ///     same value, written both ways, is a different number of bytes. If these ever
        ///     agreed the width parameter would be doing nothing.
        /// </summary>
        [Fact]
        public void WriteSmart_ForcedTwoByte_WidensAValueTheShortFormWouldHaveHeld()
        {
            var forced = new JagStream();
            forced.WriteSmart(0, JagStream.SmartWidth.TwoByte);
            Assert.Equal("C0-00", BitConverter.ToString(forced.ToArray()));

            var shortest = new JagStream();
            shortest.WriteSmart(0);
            Assert.Equal("40", BitConverter.ToString(shortest.ToArray()));

            //Both decode to the value that was written, which is exactly why the format is
            //non-canonical and why the stored width has to be carried rather than derived
            forced.Seek0();
            shortest.Seek0();
            Assert.Equal(0, forced.ReadSmart());
            Assert.Equal(0, shortest.ReadSmart());
        }

        [Theory]
        [InlineData(-64)]
        [InlineData(0)]
        [InlineData(63)]
        public void WriteSmart_ForcedOneByte_MatchesTheShortestFormInsideTheNarrowRange(int value)
        {
            var forced = new JagStream();
            var shortest = new JagStream();

            forced.WriteSmart(value, JagStream.SmartWidth.OneByte);
            shortest.WriteSmart(value);

            Assert.Equal(shortest.ToArray(), forced.ToArray());
        }

        /// <summary>
        ///     A one-byte width forced onto a value that does not fit is a contradiction: the
        ///     caller replaying a recorded width has necessarily changed the value. Widening it
        ///     silently would lengthen the field and shift every byte after it, so it is rejected
        ///     instead.
        /// </summary>
        [Theory]
        [InlineData(-65)]
        [InlineData(64)]
        [InlineData(16383)]
        public void WriteSmart_ForcedOneByteOnAWideValue_ThrowsRatherThanWidening(int value)
        {
            var stream = new JagStream();

            Assert.Throws<ArgumentOutOfRangeException>(
                () => stream.WriteSmart(value, JagStream.SmartWidth.OneByte));

            Assert.Equal(0, stream.Length);
        }

        /// <param name="value">
        ///     -16385 and 16384 are the first values either side of the encodable range. The two
        ///     int extremes are there because the bias is an addition, and an unvalidated one
        ///     wraps into a byte pair that reads back as a plausible small number.
        /// </param>
        [Theory]
        [InlineData(-16385)]
        [InlineData(16384)]
        [InlineData(int.MinValue)]
        [InlineData(int.MaxValue)]
        public void WriteSmart_OutOfRangeValue_ThrowsArgumentOutOfRange(int value)
        {
            var stream = new JagStream();

            Assert.Throws<ArgumentOutOfRangeException>(() => stream.WriteSmart(value));
            Assert.Throws<ArgumentOutOfRangeException>(
                () => stream.WriteSmart(value, JagStream.SmartWidth.TwoByte));

            //Nothing was written, so the stream is still where the caller left it
            Assert.Equal(0, stream.Length);
            Assert.Equal(0, stream.Position);
        }

        [Fact]
        public void WriteSmart_UndefinedWidth_ThrowsArgumentOutOfRange()
        {
            var stream = new JagStream();

            Assert.Throws<ArgumentOutOfRangeException>(
                () => stream.WriteSmart(0, (JagStream.SmartWidth) 7));

            Assert.Equal(0, stream.Length);
        }

        /// <summary>
        ///     The mistake the signed writer exists to prevent. WriteUnsignedSmart was for a long
        ///     time the only smart writer, and it carries the 0/32768 biases, so reaching for it
        ///     on a ReadSmart field emits a well-formed value that is wrong by a uniform 64 or
        ///     16384 - which nothing downstream can detect. The two writers must not agree on the
        ///     same input.
        /// </summary>
        [Theory]
        [InlineData(0)]
        [InlineData(63)]
        [InlineData(1000)]
        public void WriteSmart_And_WriteUnsignedSmart_ProduceDifferentBytesForTheSameValue(int value)
        {
            var signed = new JagStream();
            var unsigned = new JagStream();

            signed.WriteSmart(value);
            unsigned.WriteUnsignedSmart(value);

            Assert.NotEqual(
                BitConverter.ToString(unsigned.ToArray()),
                BitConverter.ToString(signed.ToArray()));

            //And reading the unsigned bytes back as a signed smart returns the wrong number
            //rather than failing, which is the whole hazard
            unsigned.Seek0();
            Assert.NotEqual(value, unsigned.ReadSmart());
        }

        #endregion

        #region Strings

        [Fact]
        public void WriteJagexString_And_ReadJagexString_RoundTripAscii()
        {
            var stream = new JagStream();

            stream.WriteJagexString("Dragon longsword");
            stream.Seek0();

            Assert.Equal("Dragon longsword", stream.ReadJagexString());
        }

        /// <param name="raw">
        ///     Sampled across the 128-159 remap table: the first slot, an early one, the middle,
        ///     and the last. These are the bytes CP-1252 defines and plain Latin-1 does not.
        /// </param>
        [Theory]
        [InlineData(128, (char) 0x20AC)]
        [InlineData(130, (char) 0x201A)]
        [InlineData(140, (char) 0x0152)]
        [InlineData(145, (char) 0x2018)]
        [InlineData(153, (char) 0x2122)]
        [InlineData(159, (char) 0x0178)]
        public void ReadJagexString_ExtendedBand_DecodesThroughTheRemapTable(byte raw, char expected)
        {
            Assert.Equal(expected.ToString(), Wire(raw, 0).ReadJagexString());
        }

        /// <summary>
        ///     The remap table covers bytes 128-159, but five of those thirty-two slots have no
        ///     character assigned and collapse to '?'. That is a lossy decode, and because '?'
        ///     is also a legitimate character there is no way downstream to tell a real question
        ///     mark from a byte the table could not name.
        /// </summary>
        /// <param name="undefined">
        ///     The five holes in the table. They are the reason the reverse map holds 27 entries
        ///     rather than 32.
        /// </param>
        [Theory]
        [InlineData(129)]
        [InlineData(141)]
        [InlineData(143)]
        [InlineData(144)]
        [InlineData(157)]
        public void ReadJagexString_UndefinedExtendedSlot_DecodesToQuestionMark(byte undefined)
        {
            Assert.Equal("?", Wire(undefined, 0).ReadJagexString());
        }

        /// <summary>
        ///     Exactly five slots are undefined, no more and no fewer. Adding a character to the
        ///     table or removing one silently changes how already-shipped cache bytes decode, so
        ///     the count is asserted rather than assumed.
        /// </summary>
        [Fact]
        public void ReadJagexString_ExtendedBand_HasExactlyFiveUndefinedSlots()
        {
            int undefined = 0;
            for (int b = 128; b < 160; b++)
                if (Wire((byte) b, 0).ReadJagexString() == "?")
                    undefined++;

            Assert.Equal(5, undefined);
        }

        /// <summary>
        ///     Bytes 160-255 are not remapped at all: they pass through as the Latin-1 code point
        ///     of the same value. Only the 128-159 window is special, and widening the branch
        ///     would corrupt every accented character in the cache.
        /// </summary>
        [Theory]
        [InlineData(160)]
        [InlineData(161)]
        [InlineData(200)]
        [InlineData(254)]
        [InlineData(255)]
        public void ReadJagexString_AboveTheRemapBand_PassesTheByteThroughUnchanged(byte raw)
        {
            string decoded = Wire(raw, 0).ReadJagexString();

            Assert.Equal(1, decoded.Length);
            Assert.Equal(raw, (int) decoded[0]);
        }

        /// <summary>
        ///     Every byte the table names must survive a decode-then-encode cycle, which is what
        ///     makes the item and object codecs byte-faithful when they re-save a record they
        ///     only read.
        /// </summary>
        [Fact]
        public void WriteJagexString_ExtendedBand_RoundTripsEveryDefinedSlot()
        {
            for (int b = 128; b < 160; b++)
            {
                string decoded = Wire((byte) b, 0).ReadJagexString();
                if (decoded == "?") continue;               // the five undefined slots

                var stream = new JagStream();
                stream.WriteJagexString(decoded);

                Assert.Equal(new byte[] { (byte) b, 0 }, stream.ToArray());
            }
        }

        /// <summary>
        ///     Decoding is lossy for the five undefined slots, so the byte round trip is broken
        ///     there: the byte becomes '?' and '?' encodes back as 0x3F. A codec that re-saves a
        ///     record containing one of these bytes rewrites it, and the change is permanent
        ///     after one save.
        /// </summary>
        [Theory]
        [InlineData(129)]
        [InlineData(141)]
        [InlineData(143)]
        [InlineData(144)]
        [InlineData(157)]
        public void WriteJagexString_UndefinedExtendedSlot_IsNotAByteRoundTrip(byte undefined)
        {
            string decoded = Wire(undefined, 0).ReadJagexString();
            var stream = new JagStream();

            stream.WriteJagexString(decoded);

            Assert.Equal(new byte[] { 0x3F, 0 }, stream.ToArray());
            Assert.NotEqual(undefined, stream.Get(0));
        }

        /// <summary>
        ///     Nothing above U+00FF fits the one-byte encoding, so it degrades to '?' rather than
        ///     truncating to a low byte. Truncation would produce a plausible wrong character
        ///     instead of an obviously wrong one.
        /// </summary>
        [Theory]
        [InlineData((char) 0x0100)]
        [InlineData((char) 0x4E2D)]
        [InlineData((char) 0xFFFD)]
        public void WriteJagexString_CharacterAboveTheByteRange_EncodesAsQuestionMark(char c)
        {
            var stream = new JagStream();

            stream.WriteJagexString(c.ToString());

            Assert.Equal(new byte[] { 0x3F, 0 }, stream.ToArray());
        }

        /// <summary>
        ///     An embedded NUL is dropped rather than written, because writing it would terminate
        ///     the string early and everything after it would be read as the next field. The
        ///     result is that the string that comes back is shorter than the one that went in,
        ///     with no error raised.
        /// </summary>
        [Fact]
        public void WriteJagexString_EmbeddedNul_IsSilentlyDroppedSoTheStringShortens()
        {
            var stream = new JagStream();

            stream.WriteJagexString("a\0b");

            Assert.Equal(new byte[] { (byte) 'a', (byte) 'b', 0 }, stream.ToArray());

            stream.Seek0();
            Assert.Equal("ab", stream.ReadJagexString());
        }

        /// <summary>
        ///     An empty string is still one byte on the wire. Emitting nothing would make the
        ///     next field's first byte the terminator of this one, shifting the whole record.
        /// </summary>
        [Fact]
        public void WriteJagexString_EmptyString_StillEmitsTheTerminator()
        {
            var stream = new JagStream();

            stream.WriteJagexString("");

            Assert.Equal(new byte[] { 0 }, stream.ToArray());
            stream.Seek0();
            Assert.Equal("", stream.ReadJagexString());
        }

        [Fact]
        public void ReadString2_NullTerminatedAscii_DecodesAndConsumesTheTerminator()
        {
            var stream = Wire((byte) 'H', (byte) 'i', 0, 42);

            Assert.Equal("Hi", stream.ReadString2());
            Assert.Equal(3, stream.Position);
        }

        /// <summary>
        ///     ReadString2 is the raw reader and ReadJagexString the remapping one. They agree
        ///     everywhere except the 128-159 window, and there is no WriteString2, so a string
        ///     read with ReadString2 and written back with WriteJagexString does not round trip.
        /// </summary>
        [Theory]
        [InlineData(128, (char) 0x0080, (char) 0x20AC)]
        [InlineData(145, (char) 0x0091, (char) 0x2018)]
        [InlineData(159, (char) 0x009F, (char) 0x0178)]
        public void ReadString2_And_ReadJagexString_DivergeAcrossTheRemapBand(byte raw, char plain, char remapped)
        {
            Assert.Equal(plain.ToString(), Wire(raw, 0).ReadString2());
            Assert.Equal(remapped.ToString(), Wire(raw, 0).ReadJagexString());
        }

        [Fact]
        public void ReadString2_And_ReadJagexString_AgreeOutsideTheRemapBand()
        {
            byte[] wire = { (byte) 'a', 0x7F, 200, 255, 0 };

            Assert.Equal(Wire(wire).ReadString2(), Wire(wire).ReadJagexString());
        }

        [Fact]
        public void ReadString2_Unterminated_ThrowsEndOfStream()
        {
            Assert.Throws<EndOfStreamException>(() => Wire((byte) 'A').ReadString2());
        }

        [Fact]
        public void ReadJagexString_Unterminated_ThrowsEndOfStream()
        {
            Assert.Throws<EndOfStreamException>(() => Wire((byte) 'A').ReadJagexString());
        }

        [Fact]
        public void ReadString2_ImmediateTerminator_ReturnsEmpty()
        {
            var stream = Wire(0);

            Assert.Equal("", stream.ReadString2());
            Assert.Equal(1, stream.Position);
        }

        #endregion

        #region Array readers

        [Fact]
        public void ReadUnsignedByteArray_ReadsEachByteUnsigned()
        {
            var stream = Wire(0x00, 0x7F, 0x80, 0xFF);

            Assert.Equal(new[] { 0, 127, 128, 255 }, stream.ReadUnsignedByteArray(4));
            Assert.Equal(4, stream.Position);
        }

        [Fact]
        public void ReadUnsignedByteArray_ZeroSize_ReturnsEmptyAndConsumesNothing()
        {
            var stream = Wire(1, 2);

            Assert.Empty(stream.ReadUnsignedByteArray(0));
            Assert.Equal(0, stream.Position);
        }

        /// <param name="size">
        ///     1024 is the last size that goes on the stack and 1025 the first that rents from
        ///     the pool, so the pair covers both allocation strategies.
        /// </param>
        [Theory]
        [InlineData(1024)]
        [InlineData(1025)]
        public void ReadUnsignedByteArray_EitherSideOfThePoolingThreshold_ReadsTheSameValues(int size)
        {
            byte[] payload = new byte[size];
            for (int i = 0; i < size; i++)
                payload[i] = (byte) (i & 0xFF);

            int[] result = new JagStream(payload).ReadUnsignedByteArray(size);

            Assert.Equal(size, result.Length);
            Assert.Equal(payload[0], result[0]);
            Assert.Equal(payload[size - 1], result[size - 1]);
        }

        [Fact]
        public void ReadUnsignedByteArray_ShorterThanRequested_ThrowsEndOfStream()
        {
            Assert.Throws<EndOfStreamException>(() => Wire(1).ReadUnsignedByteArray(4));
        }

        [Fact]
        public void ReadUnsignedShortArray_ReadsBigEndianPairsUnsigned()
        {
            var stream = Wire(0x00, 0x01, 0x7F, 0xFF, 0x80, 0x00, 0xFF, 0xFF);

            Assert.Equal(new[] { 1, 32767, 32768, 65535 }, stream.ReadUnsignedShortArray(4));
            Assert.Equal(8, stream.Position);
        }

        /// <param name="size">
        ///     The threshold is on the byte count, not the element count: 1024 shorts is 2048
        ///     bytes and stays on the stack, 1025 rents.
        /// </param>
        [Theory]
        [InlineData(1024)]
        [InlineData(1025)]
        public void ReadUnsignedShortArray_EitherSideOfThePoolingThreshold_ReadsTheSameValues(int size)
        {
            var source = new JagStream();
            for (int i = 0; i < size; i++)
                source.WriteShort((short) (i & 0xFFFF));
            source.Seek0();

            int[] result = source.ReadUnsignedShortArray(size);

            Assert.Equal(size, result.Length);
            Assert.Equal(0, result[0]);
            Assert.Equal((size - 1) & 0xFFFF, result[size - 1]);
        }

        [Fact]
        public void ReadUnsignedShortArray_ShorterThanRequested_ThrowsEndOfStream()
        {
            Assert.Throws<EndOfStreamException>(() => Wire(1).ReadUnsignedShortArray(4));
        }

        /// <summary>
        ///     Both array readers rent from ArrayPool above their stackalloc threshold. The
        ///     EndOfStreamException path used to return before the line that gave the rental back,
        ///     so a decoder meeting a run of truncated archives bled pooled memory in proportion
        ///     to the number of failures - the one situation where the rental is least likely to
        ///     be missed and most likely to recur.
        ///
        ///     The success path is asserted first, so the test proves the pool really does hand
        ///     the same array back when the reader behaves. That is what makes the identity check
        ///     on the failure path evidence about this code rather than about pool internals.
        /// </summary>
        [Fact]
        public void ReadUnsignedByteArray_EndOfStreamOnAPooledRead_StillReturnsTheRental()
        {
            const int size = 2048;   // above the 1024 stackalloc threshold, so it rents

            byte[] control = ArrayPool<byte>.Shared.Rent(size);
            ArrayPool<byte>.Shared.Return(control);
            new JagStream(new byte[size]).ReadUnsignedByteArray(size);
            byte[] afterSuccess = ArrayPool<byte>.Shared.Rent(size);
            Assert.Same(control, afterSuccess);
            ArrayPool<byte>.Shared.Return(afterSuccess);

            Assert.Throws<EndOfStreamException>(() => Wire(1, 2, 3).ReadUnsignedByteArray(size));

            byte[] afterFailure = ArrayPool<byte>.Shared.Rent(size);
            Assert.Same(control, afterFailure);
            ArrayPool<byte>.Shared.Return(afterFailure);
        }

        /// <summary>
        ///     The same path in the short reader, which is the more exposed of the two: its throw
        ///     sits inside the read loop rather than after it, so it could leak on any truncated
        ///     element rather than only the last.
        /// </summary>
        [Fact]
        public void ReadUnsignedShortArray_EndOfStreamOnAPooledRead_StillReturnsTheRental()
        {
            const int size = 2048;         // 4096 bytes, above the 2048 byte threshold
            const int byteCount = size * 2;

            byte[] control = ArrayPool<byte>.Shared.Rent(byteCount);
            ArrayPool<byte>.Shared.Return(control);
            new JagStream(new byte[byteCount]).ReadUnsignedShortArray(size);
            byte[] afterSuccess = ArrayPool<byte>.Shared.Rent(byteCount);
            Assert.Same(control, afterSuccess);
            ArrayPool<byte>.Shared.Return(afterSuccess);

            Assert.Throws<EndOfStreamException>(() => Wire(1, 2, 3).ReadUnsignedShortArray(size));

            byte[] afterFailure = ArrayPool<byte>.Shared.Rent(byteCount);
            Assert.Same(control, afterFailure);
            ArrayPool<byte>.Shared.Return(afterFailure);
        }

        #endregion

        #region Get, Skip, sub-streams and bulk bytes

        [Fact]
        public void Get_ReturnsTheByteWithoutMovingPosition()
        {
            var stream = Wire(10, 20, 30);
            stream.Seek(2);

            Assert.Equal(10, stream.Get(0));
            Assert.Equal(30, stream.Get(2));
            Assert.Equal(2, stream.Position);
        }

        /// <param name="pos">
        ///     -1 and Length are the two off-by-one misses. Get bounds-checks against Length, not
        ///     capacity, so index Length is out of range even though the array holds a byte there.
        /// </param>
        [Theory]
        [InlineData(-1)]
        [InlineData(3)]
        [InlineData(int.MaxValue)]
        public void Get_OutOfRange_ThrowsArgumentOutOfRange(int pos)
        {
            Assert.Throws<ArgumentOutOfRangeException>(() => Wire(1, 2, 3).Get(pos));
        }

        [Fact]
        public void Get_WithinCapacityButPastLength_IsStillOutOfRange()
        {
            var stream = new JagStream(64);
            stream.WriteByte(1);

            Assert.Throws<ArgumentOutOfRangeException>(() => stream.Get(1));
        }

        [Fact]
        public void Skip_WithinBounds_AdvancesPosition()
        {
            var stream = Wire(1, 2, 3, 4);

            stream.Skip(2);

            Assert.Equal(2, stream.Position);
            Assert.Equal(3, stream.ReadByte());
        }

        /// <summary>
        ///     Skip used to validate nothing. It accepted a negative argument as a rewind, and it
        ///     clamped an overshoot in either direction rather than reporting it, so a codec that
        ///     skipped a payload whose declared length was corrupt landed quietly at the end of
        ///     the stream and carried on decoding as though it had skipped the right amount. Seek,
        ///     given the same out-of-range destination, always threw; the two now agree, and this
        ///     asserts that agreement rather than the old divergence.
        /// </summary>
        /// <param name="skip">
        ///     A forward overshoot, a negative rewind, a negative undershoot past the start, and
        ///     int.MinValue, which would wrap the position arithmetic back into range if the
        ///     bound were computed in int.
        /// </param>
        [Theory]
        [InlineData(99)]
        [InlineData(-1)]
        [InlineData(-99)]
        [InlineData(int.MinValue)]
        [InlineData(int.MaxValue)]
        public void Skip_OutOfBounds_ThrowsIOExceptionLikeSeek(int skip)
        {
            var stream = Wire(1, 2, 3);

            Assert.Throws<IOException>(() => stream.Skip(skip));

            //The rejected skip left the position alone
            Assert.Equal(0, stream.Position);

            //Seek rejects exactly what Skip used to absorb
            Assert.Throws<IOException>(() => stream.Seek(skip > 0 ? skip : -1));
        }

        [Fact]
        public void Skip_ToExactlyTheEnd_IsAllowed()
        {
            var stream = Wire(1, 2, 3);

            stream.Skip(3);

            Assert.Equal(3, stream.Position);
            Assert.Equal(0, stream.Remaining());
        }

        /// <summary>
        ///     Clear zeroes the buffer and rewinds, but it used to then set Length to the full
        ///     capacity rather than to zero, so a "cleared" stream came back longer than it went
        ///     in and read as a run of zero bytes instead of being empty. Reusing a stream by
        ///     clearing it handed the next caller a padded stream, and every Remaining or Length
        ///     check on it was wrong. It now empties the stream, keeping the capacity so the
        ///     buffer can still be reused without reallocating.
        /// </summary>
        [Fact]
        public void Clear_EmptiesTheStreamButKeepsTheCapacity()
        {
            var stream = new JagStream(16);
            stream.WriteByte(1);
            stream.WriteByte(2);
            Assert.Equal(2, stream.Length);

            stream.Clear();

            Assert.Equal(0, stream.Position);
            Assert.Equal(0, stream.Length);
            Assert.Equal(0, stream.Remaining());
            Assert.Empty(stream.ToArray());

            //Reading an empty stream reports the end rather than handing back a padding byte
            Assert.Equal(-1, stream.ReadByte());

            //The buffer survives, so a cleared stream can be refilled without reallocating
            Assert.Equal(16, stream.Capacity);
        }

        /// <summary>
        ///     The bytes that were written are zeroed, not merely made unreachable, so a stream
        ///     reused after Clear cannot leak the previous payload through the spare capacity.
        /// </summary>
        [Fact]
        public void Clear_ZeroesTheBytesItMakesUnreachable()
        {
            var stream = new JagStream(16);
            stream.WriteByte(1);
            stream.WriteByte(2);

            stream.Clear();

            Assert.Equal(new byte[16], stream.GetBuffer());
        }

        [Fact]
        public void GetSubStream_TakesTheNextBytesAndAdvancesTheParent()
        {
            var parent = Wire(1, 2, 3, 4, 5);
            parent.Seek(1);

            JagStream sub = parent.GetSubStream(3);

            Assert.Equal(new byte[] { 2, 3, 4 }, sub.ToArray());
            Assert.Equal(0, sub.Position);
            Assert.Equal(4, parent.Position);
        }

        /// <summary>
        ///     A sub-stream is a copy, not a view. Codecs decode an archive from a sub-stream and
        ///     then edit it; if the slice aliased its parent, that edit would reach back into the
        ///     container the slice came from.
        /// </summary>
        [Fact]
        public void GetSubStream_IsIsolatedFromTheParentBuffer()
        {
            var parent = Wire(1, 2, 3, 4);
            JagStream sub = parent.GetSubStream(2);

            parent.GetBuffer()[0] = 99;

            Assert.Equal(1, sub.Get(0));
        }

        [Fact]
        public void GetSubStream_WithAPointer_SeeksFirst()
        {
            var parent = Wire(1, 2, 3, 4, 5);

            JagStream sub = parent.GetSubStream(2, 3);

            Assert.Equal(new byte[] { 4, 5 }, sub.ToArray());
            Assert.Equal(5, parent.Position);
        }

        [Fact]
        public void GetSubStream_PastTheEnd_ThrowsEndOfStream()
        {
            Assert.Throws<EndOfStreamException>(() => Wire(1, 2).GetSubStream(3));
        }

        /// <summary>
        ///     The pointer overload seeks before it slices, so an out-of-range pointer surfaces
        ///     as the Seek failure (IOException) rather than the slice failure
        ///     (EndOfStreamException). Two different exceptions for what a caller experiences as
        ///     one bad request.
        /// </summary>
        [Fact]
        public void GetSubStream_WithAPointerPastTheEnd_ThrowsIOExceptionNotEndOfStream()
        {
            Assert.Throws<IOException>(() => Wire(1, 2).GetSubStream(1, 99));
        }

        [Fact]
        public void ReadBytes_ReturnsACopyAndAdvances()
        {
            var stream = Wire(1, 2, 3, 4);
            stream.Seek(1);

            byte[] taken = stream.ReadBytes(2);

            Assert.Equal(new byte[] { 2, 3 }, taken);
            Assert.Equal(3, stream.Position);

            taken[0] = 99;
            Assert.Equal(2, stream.Get(1));
        }

        [Fact]
        public void ReadBytes_ZeroLength_ReturnsEmptyAndConsumesNothing()
        {
            var stream = Wire(1, 2);

            Assert.Empty(stream.ReadBytes(0));
            Assert.Equal(0, stream.Position);
        }

        [Fact]
        public void ReadBytes_MoreThanRemaining_ThrowsEndOfStream()
        {
            Assert.Throws<EndOfStreamException>(() => Wire(1, 2).ReadBytes(3));
        }

        /// <param name="count">Every width from a single byte up to the full eight-byte long.</param>
        [Theory]
        [InlineData(1, 0x78L, "78")]
        [InlineData(2, 0x5678L, "56-78")]
        [InlineData(3, 0x345678L, "34-56-78")]
        [InlineData(4, 0x12345678L, "12-34-56-78")]
        [InlineData(8, 0x0123456789ABCDEFL, "01-23-45-67-89-AB-CD-EF")]
        public void WriteBytes_WritesTheLowCountBytesBigEndian(int count, long value, string expected)
        {
            var stream = new JagStream();

            stream.WriteBytes(count, value);

            Assert.Equal(expected, BitConverter.ToString(stream.ToArray()));
        }

        [Fact]
        public void WriteBytes_ZeroCount_WritesNothing()
        {
            var stream = new JagStream();

            stream.WriteBytes(0, 0x1234L);

            Assert.Equal(0, stream.Length);
        }

        /// <summary>
        ///     Past eight bytes the shift distance exceeds the width of a long, and C# masks the
        ///     shift count to six bits rather than yielding zero. The extra leading bytes that a
        ///     wider request should have filled therefore repeated the value's low byte -
        ///     WriteBytes(9, 0x0123456789ABCDEF) emitted EF-01-23-45-67-89-AB-CD-EF - and the
        ///     result was not the big-endian encoding the method promises. Nothing in the repo
        ///     calls it with a count above eight today, which is the only reason it never bit.
        ///     The shift is now clamped to 63, so the leading bytes carry the value's sign fill
        ///     and the encoding decodes back to what was passed in.
        /// </summary>
        /// <param name="count">Nine and sixteen bytes, either side of the old wrap point.</param>
        /// <param name="expected">
        ///     Zero fill for a non-negative value, 0xFF fill for a negative one: that is the
        ///     value's two's-complement big-endian encoding at the requested width.
        /// </param>
        [Theory]
        [InlineData(9, 0x0123456789ABCDEFL, "00-01-23-45-67-89-AB-CD-EF")]
        [InlineData(12, 0x0123456789ABCDEFL, "00-00-00-00-01-23-45-67-89-AB-CD-EF")]
        [InlineData(9, -1L, "FF-FF-FF-FF-FF-FF-FF-FF-FF")]
        [InlineData(9, long.MinValue, "FF-80-00-00-00-00-00-00-00")]
        public void WriteBytes_CountAboveEight_SignExtendsInsteadOfWrappingTheShift(int count, long value, string expected)
        {
            var stream = new JagStream();

            stream.WriteBytes(count, value);

            Assert.Equal(expected, BitConverter.ToString(stream.ToArray()));
        }

        [Fact]
        public void WriteBytes_NegativeCount_ThrowsArgumentOutOfRange()
        {
            var stream = new JagStream();

            Assert.Throws<ArgumentOutOfRangeException>(() => stream.WriteBytes(-1, 0x1234L));
        }

        #endregion
    }
}
