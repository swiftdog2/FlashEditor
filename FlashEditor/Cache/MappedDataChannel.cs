using System;
using System.IO;
using System.IO.MemoryMappedFiles;

namespace FlashEditor.cache {
    /// <summary>
    /// Provides memory-mapped access to the main data file (dat2),
    /// avoiding a full managed byte-array copy.
    /// </summary>
    public class MappedDataChannel : IDisposable {
        private FileStream _fileStream;
        private MemoryMappedFile _mmf;
        private MemoryMappedViewAccessor _accessor;
        private long _mappedLength;
        private long _dataLength;

        /// <summary>The actual data length (excludes Remap padding).</summary>
        public long Length => _dataLength;

        public MappedDataChannel(string path) {
            _fileStream = new FileStream(path, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.Read);
            _dataLength = _fileStream.Length;
            MapFile();
        }

        private void MapFile() {
            long fileLen = Math.Max(_fileStream.Length, 1);
            _mappedLength = fileLen;
            _mmf = MemoryMappedFile.CreateFromFile(
                _fileStream,
                mapName: null,
                capacity: _mappedLength,
                MemoryMappedFileAccess.ReadWrite,
                HandleInheritability.None,
                leaveOpen: true);
            _accessor = _mmf.CreateViewAccessor(0, _mappedLength, MemoryMappedFileAccess.ReadWrite);
        }

        private void Remap(long requiredLength) {
            _accessor?.Dispose();
            _mmf?.Dispose();
            _mappedLength = requiredLength + (requiredLength / 4);
            _mmf = MemoryMappedFile.CreateFromFile(
                _fileStream,
                mapName: null,
                capacity: _mappedLength,
                MemoryMappedFileAccess.ReadWrite,
                HandleInheritability.None,
                leaveOpen: true);
            _accessor = _mmf.CreateViewAccessor(0, _mappedLength, MemoryMappedFileAccess.ReadWrite);
        }

        /// <summary>Reads <paramref name="count"/> bytes starting at <paramref name="position"/>.</summary>
        public byte[] ReadBytes(long position, int count) {
            byte[] buffer = new byte[count];
            _accessor.ReadArray(position, buffer, 0, count);
            return buffer;
        }

        /// <summary>Writes bytes to the data file, growing and remapping if needed.</summary>
        public void WriteBytes(long position, byte[] data, int offset, int count) {
            long required = position + count;
            if(required > _mappedLength)
                Remap(required);
            _accessor.WriteArray(position, data, offset, count);
            _dataLength = Math.Max(_dataLength, required);
        }

        /// <summary>Flushes pending writes to disk.</summary>
        public void Flush() {
            _accessor.Flush();
            _fileStream.Flush();
        }

        /// <summary>Flushes and copies only the actual data bytes to <paramref name="destPath"/>.</summary>
        public void SaveTo(string destPath) {
            Flush();
            using var dest = new FileStream(destPath, FileMode.Create, FileAccess.Write, FileShare.None);
            _fileStream.Position = 0;
            CopyBytes(_fileStream, dest, _dataLength);
        }

        private static void CopyBytes(Stream source, Stream dest, long count) {
            byte[] buf = new byte[81920];
            while (count > 0) {
                int toRead = (int)Math.Min(buf.Length, count);
                int read = source.Read(buf, 0, toRead);
                if (read == 0) break;
                dest.Write(buf, 0, read);
                count -= read;
            }
        }

        public void Dispose() {
            _accessor?.Dispose();
            _mmf?.Dispose();
            _accessor = null;
            _mmf = null;

            if (_fileStream != null) {
                _fileStream.SetLength(_dataLength);
                _fileStream.Dispose();
                _fileStream = null;
            }
        }
    }
}
