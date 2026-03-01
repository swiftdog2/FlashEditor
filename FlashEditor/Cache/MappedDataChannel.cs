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

        /// <summary>The actual length of the underlying file.</summary>
        public long Length => _fileStream.Length;

        public MappedDataChannel(string path) {
            _fileStream = new FileStream(path, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.Read);
            MapFile();
        }

        private void MapFile() {
            long fileLen = Math.Max(_fileStream.Length, 1);
            _mappedLength = fileLen + (fileLen / 4); // over-map by 1.25x
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
            if(required > _fileStream.Length)
                _fileStream.SetLength(required);
        }

        /// <summary>Flushes pending writes to disk.</summary>
        public void Flush() {
            _accessor.Flush();
            _fileStream.Flush();
        }

        /// <summary>Flushes and copies the backing file to <paramref name="destPath"/>.</summary>
        public void SaveTo(string destPath) {
            Flush();
            File.Copy(_fileStream.Name, destPath, overwrite: true);
        }

        public void Dispose() {
            _accessor?.Dispose();
            _mmf?.Dispose();
            _fileStream?.Dispose();
            _accessor = null;
            _mmf = null;
            _fileStream = null;
        }
    }
}
