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
    }
}
