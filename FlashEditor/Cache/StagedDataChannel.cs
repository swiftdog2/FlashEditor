using System;
using System.Collections.Generic;
using System.IO;
using System.IO.MemoryMappedFiles;

namespace FlashEditor.cache {
    /// <summary>
    ///     Read-through staging layer over <c>main_file_cache.dat2</c>.
    ///
    ///     The source file is opened READ-ONLY and is never modified. Writes are held in an
    ///     in-memory overlay of fixed size blocks and only become durable when
    ///     <see cref="RSFileStore.SaveTo"/> commits them alongside the index files, so an edit
    ///     can never leave the on-disk cache half-updated.
    ///
    ///     Reads resolve overlay first, then the mapped source, then zeros. That read-through
    ///     behaviour is load bearing: <see cref="RSFileStore.Write"/> reads sectors back
    ///     immediately after writing them to verify the chain, long before any save.
    /// </summary>
    public class StagedDataChannel : IDisposable {
        /// <summary>
        ///     Overlay granularity. One block is exactly one cache sector, which is the unit
        ///     every caller reads and writes, so blocks map one to one onto sectors.
        /// </summary>
        private const int BlockSize = 520; // RSSector.SIZE, which is static readonly and so unusable here

        /// <summary>
        ///     Guards the overlay and the file handles. Definitions and textures are loaded on
        ///     background threads (Parallel.ForEach and Task.Run in Editor), so reads genuinely
        ///     race writes. A Dictionary read concurrent with an insert is undefined behaviour.
        /// </summary>
        private readonly object _gate = new object();
        private readonly Dictionary<long, byte[]> _staged = new Dictionary<long, byte[]>();

        //Null between CloseMap and Reopen, and whenever the source file is empty
        private FileStream? _fileStream;
        private MemoryMappedFile? _mmf;
        private MemoryMappedViewAccessor? _accessor;
        private string _path = string.Empty;
        private long _sourceLength;
        private long _dataLength;

        /// <summary>
        ///     Logical high water mark of bytes ever written, starting at the source length.
        ///     Sector allocation derives the next free sector from this, so it must never shrink.
        /// </summary>
        public long Length {
            get { lock(_gate) return _dataLength; }
        }

        /// <summary>Whether any write is staged and not yet saved.</summary>
        public bool HasStagedChanges {
            get { lock(_gate) return _staged.Count > 0; }
        }

        public StagedDataChannel(string path) {
            Open(path);
        }

        private void Open(string path) {
            _path = path;

            //Opening read-only cannot create the file, so make an empty one first.
            if(!File.Exists(path))
                File.Create(path).Dispose();

            _fileStream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
            _sourceLength = _fileStream.Length;
            _dataLength = _sourceLength;

            //A zero length file cannot be mapped at all - CreateFileMapping rejects a zero
            //size. Leave the map null and serve every read from the overlay or from zeros.
            if(_sourceLength > 0) {
                _mmf = MemoryMappedFile.CreateFromFile(
                    _fileStream,
                    mapName: null,
                    capacity: _sourceLength,
                    MemoryMappedFileAccess.Read,
                    HandleInheritability.None,
                    leaveOpen: true);
                _accessor = _mmf.CreateViewAccessor(0, _sourceLength, MemoryMappedFileAccess.Read);
            }
        }

        /// <summary>Reads <paramref name="count"/> bytes starting at <paramref name="position"/>.</summary>
        public byte[] ReadBytes(long position, int count) {
            if(position < 0)
                throw new ArgumentOutOfRangeException(nameof(position), "Negative data offset: " + position);
            if(count < 0)
                throw new ArgumentOutOfRangeException(nameof(count), "Negative read length: " + count);

            byte[] buffer = new byte[count];
            if(count == 0)
                return buffer;

            lock(_gate) {
                //Starting past the end means an index record points at a sector that was never
                //allocated. Fail loudly here rather than handing back a plausible zero sector.
                if(position >= _dataLength)
                    throw new ArgumentOutOfRangeException(nameof(position),
                        "Read at " + position + " starts past the data length " + _dataLength);

                //A read that merely runs off the end is the unaligned final sector of a
                //truncated cache; zero fill the tail rather than refusing to load it.
                int readable = (int) Math.Min(count, _dataLength - position);
                CopyOut(position, buffer, 0, readable);
            }
            return buffer;
        }

        /// <summary>Stages <paramref name="count"/> bytes at <paramref name="position"/>.</summary>
        public void WriteBytes(long position, byte[] data, int offset, int count) {
            if(position < 0)
                throw new ArgumentOutOfRangeException(nameof(position), "Negative data offset: " + position);
            if(count < 0)
                throw new ArgumentOutOfRangeException(nameof(count), "Negative write length: " + count);
            if(count == 0)
                return;

            lock(_gate) {
                long pos = position;
                int consumed = 0;

                while(consumed < count) {
                    long blockStart = (pos / BlockSize) * BlockSize;
                    int inBlock = (int) (pos - blockStart);
                    int chunk = Math.Min(BlockSize - inBlock, count - consumed);

                    byte[] block = MaterialiseBlock(blockStart);
                    Array.Copy(data, offset + consumed, block, inBlock, chunk);

                    pos += chunk;
                    consumed += chunk;
                }

                _dataLength = Math.Max(_dataLength, position + count);
            }
        }

        /// <summary>
        ///     Writes the staged image - source bytes overlaid with staged blocks - to
        ///     <paramref name="destPath"/>, creating the directory if needed.
        /// </summary>
        public void SaveTo(string destPath) {
            string dir = Path.GetDirectoryName(destPath);
            if(!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
                Directory.CreateDirectory(dir);

            lock(_gate) {
                using var dest = new FileStream(destPath, FileMode.Create, FileAccess.Write, FileShare.None);
                byte[] buffer = new byte[BlockSize];
                long written = 0;

                while(written < _dataLength) {
                    //The final block is usually partial; emit exactly Length bytes so a
                    //reopened cache reports the same allocation cursor.
                    int chunk = (int) Math.Min(BlockSize, _dataLength - written);
                    Array.Clear(buffer, 0, buffer.Length);
                    CopyOut(written, buffer, 0, chunk);
                    dest.Write(buffer, 0, chunk);
                    written += chunk;
                }
            }
        }

        /// <summary>
        ///     Releases the source handles so the file can be replaced. Reads throw until
        ///     <see cref="Reopen"/> runs, so callers must hold the whole close/replace/reopen
        ///     sequence together.
        /// </summary>
        internal void CloseMap() {
            lock(_gate)
                ReleaseHandles();
        }

        /// <summary>Reopens the channel against <paramref name="path"/>, resetting the source view.</summary>
        internal void Reopen(string path) {
            lock(_gate) {
                ReleaseHandles();
                Open(path);
            }
        }

        /// <summary>
        ///     Drops the overlay. Only valid once the staged bytes are durable in the source,
        ///     otherwise the writes are simply lost.
        /// </summary>
        internal void ClearStaged() {
            lock(_gate)
                _staged.Clear();
        }

        /// <summary>
        ///     Returns the staged block covering <paramref name="blockStart"/>, seeding it from
        ///     the source on first touch so a partial write keeps the bytes around it. Caller
        ///     holds the gate.
        /// </summary>
        private byte[] MaterialiseBlock(long blockStart) {
            if(_staged.TryGetValue(blockStart, out byte[]? existing))
                return existing;

            byte[] block = new byte[BlockSize];
            ReadSource(blockStart, block, 0, BlockSize);
            _staged[blockStart] = block;
            return block;
        }

        /// <summary>Copies out of the overlay first, falling back to the mapped source. Caller holds the gate.</summary>
        private void CopyOut(long position, byte[] destination, int destOffset, int count) {
            long pos = position;
            int written = 0;

            while(written < count) {
                long blockStart = (pos / BlockSize) * BlockSize;
                int inBlock = (int) (pos - blockStart);
                int chunk = Math.Min(BlockSize - inBlock, count - written);

                if(_staged.TryGetValue(blockStart, out byte[]? block))
                    Array.Copy(block, inBlock, destination, destOffset + written, chunk);
                else
                    ReadSource(pos, destination, destOffset + written, chunk);

                pos += chunk;
                written += chunk;
            }
        }

        /// <summary>
        ///     Reads from the mapped source. Anything past the source end is a hole opened up by
        ///     staged writes further on and stays as the zeros the caller's buffer already holds.
        ///     Caller holds the gate.
        /// </summary>
        private void ReadSource(long position, byte[] destination, int offset, int count) {
            if(_accessor == null || position >= _sourceLength)
                return;

            int available = (int) Math.Min(count, _sourceLength - position);
            if(available > 0)
                _accessor.ReadArray(position, destination, offset, available);
        }

        /// <summary>Caller holds the gate.</summary>
        private void ReleaseHandles() {
            _accessor?.Dispose();
            _mmf?.Dispose();
            _accessor = null;
            _mmf = null;
            _fileStream?.Dispose();
            _fileStream = null;
        }

        /// <summary>
        ///     Releases handles. Deliberately writes nothing: this runs when the user opens a
        ///     different cache as well as on shutdown, so persisting here would silently commit.
        /// </summary>
        public void Dispose() {
            lock(_gate) {
                ReleaseHandles();
                _staged.Clear();
            }
        }
    }
}
