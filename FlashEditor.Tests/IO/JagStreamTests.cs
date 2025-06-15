using FlashEditor;
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
    }
}
