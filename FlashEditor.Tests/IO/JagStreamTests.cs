using FlashEditor;
using System;
using Xunit;

namespace FlashEditor.Tests.IO
{
    public class JagStreamTests
    {
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
        public void LoadStream_NonExistingFile_ThrowsFileNotFound()
        {
            Assert.Throws<System.IO.FileNotFoundException>(() => JagStream.LoadStream("nonexistent.bin"));
        }

        [Fact]
        public void Save_And_LoadStream_WritesAndReadsFile()
        {
            // Arrange
            var tempPath = System.IO.Path.GetTempFileName();
            try
            {
                var stream = new JagStream();
                stream.WriteByte(1);

                // Act
                JagStream.Save(stream, tempPath);
                var loaded = JagStream.LoadStream(tempPath);

                // Assert
                Assert.Equal(new byte[]{1}, loaded.ToArray());
            }
            finally
            {
                if(System.IO.File.Exists(tempPath))
                    System.IO.File.Delete(tempPath);
            }
        }

        [Fact]
        public void Save_CreatesDirectoryIfMissing()
        {
            // Arrange
            string dir = System.IO.Path.Combine(System.IO.Path.GetTempPath(), System.Guid.NewGuid().ToString());
            string file = System.IO.Path.Combine(dir, "test.bin");
            var stream = new JagStream();
            stream.WriteByte(42);

            try
            {
                // Act
                JagStream.Save(stream, file);

                // Assert
                Assert.True(System.IO.File.Exists(file));
            }
            finally
            {
                if(System.IO.Directory.Exists(dir))
                    System.IO.Directory.Delete(dir, true);
            }
        }

        [Fact]
        public void ReadJagexString_NullTerminatedAscii_DecodesCorrectly()
        {
            // Arrange
            byte[] bytes = { (byte)'H', (byte)'i', 0 };
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
            byte[] bytes = { 128, (byte)'x', 153, 0 };
            var stream = new JagStream(bytes);

            // Act
            string result = stream.ReadJagexString();

            // Assert - CP-1252 mapping: 128 → U+20AC (€), 153 → U+2122 (™)
            Assert.Equal("\u20ACx\u2122", result);
        }

        [Fact]
        public void ReadUnsignedShortArray_ReadsAllValues()
        {
            // Arrange
            var stream = new JagStream();
            stream.WriteShort((short)1);
            stream.WriteShort((short)2);
            stream.Seek0();

            // Act
            int[] result = stream.ReadUnsignedShortArray(2);

            // Assert
            Assert.Equal(new[]{1,2}, result);
        }

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

        [Fact]
        public void Length_SetNegative_Throws()
        {
            var stream = new JagStream();

            Assert.Throws<ArgumentOutOfRangeException>(() => stream.Length = -1);
        }
    }
}
