using static FlashEditor.Utils.DebugUtil;
using System.IO;
using System;
using System.Collections.Generic;
using System.Linq;

namespace FlashEditor.cache {
    public class RSFileStore : IDisposable {
        internal StagedDataChannel dataChannel;
        internal SortedDictionary<int, RSIndex> indexChannels = new SortedDictionary<int, RSIndex>();

        private readonly string _cacheDir;
        private bool _dirty;

        /// <summary>
        ///     Whether any edit has been made since the cache was opened or last saved. Set
        ///     before the first mutation in <see cref="Write"/> so a write that fails part way
        ///     through still counts as dirty.
        /// </summary>
        internal bool IsDirty => _dirty;

        /// <summary>
        /// Loads the main data, metadata, and index files into a corresponding <c>JagStream</c>
        /// </summary>
        /// <param name="cacheDir">the base directory for the cache files</param>
        public RSFileStore(string cacheDir) {
            _cacheDir = cacheDir;

            //The dat2 is staged: the source file is opened read-only and never modified
            dataChannel = new StagedDataChannel(DataFile(cacheDir, "dat2"));

            //And load in the data from the meta indexes, including reference tables
            for(int k = 0 ; k <= RSConstants.META_INDEX ; k++) {
                string path = DataFile(cacheDir, "idx" + k);
                if(File.Exists(path))
                    indexChannels.Add(k, LoadIndex(path));
            }
        }

        private static string DataFile(string cacheDir, string suffix) {
            return Path.Combine(cacheDir, "main_file_cache." + suffix);
        }

        /// <summary>
        /// Gets the number of files of the specified index.
        /// </summary>
        /// <param name="indexId">The index id</param>
        /// <returns>The number of files</returns>
        public int GetFileCount(int indexId) {
            if(!indexChannels.ContainsKey(indexId))
                throw new FileNotFoundException("Index " + indexId + " invalid");

            return (int) (indexChannels[indexId].GetStream().Length / RSIndex.SIZE);
        }

        /// <summary>
        /// Reads binary data from a file into the specified stream
        /// </summary>
        /// <param name="directory">The directory of the binary file</param>
        private RSIndex LoadIndex(string directory) {
            return new RSIndex(JagStream.LoadStream(directory));
        }

        /// <summary>
        /// Returns the highest non-meta index rather than a true count.
        /// </summary>
        /// <returns>The maximum index present in the index channels</returns>
        internal int GetIndexCount() {
            if(indexChannels == null)
                throw new NullReferenceException("IndexChannels is null");

            //Don't include the meta index, of course
            int max = 0;
            foreach(int key in indexChannels.Keys) {
                if(key < RSConstants.META_INDEX && key > max)
                    max = key;
            }
            return max;
        }

        internal RSIndex GetIndexEntry(int indexId) {
            if(!indexChannels.ContainsKey(indexId))
                throw new FileNotFoundException("Index " + indexId + " could not be found.");

            return indexChannels[indexId];
        }

        /*
         * Imagine, hypothetically, there is an index with 3 archive headers as such:
         * Archive 0    Archive 1   Archive 2
         * [med,med]    [med,med]   [med,med]
         * If archiveId = 3, that will be index 18 ie equal to length of the index stream
         * This means we are required to expand the index
         * However, if archiveId = 4, this means we would be skipping 3 which is dumb af
         */

        /// <summary>
        ///     Stages an archive's payload and updates the corresponding six byte record inside
        ///     the index. Neither becomes durable until <see cref="SaveTo"/> runs.
        /// </summary>
        /// <param name="indexId">Index the archive belongs to.</param>
        /// <param name="archiveId">Archive id within the index.</param>
        /// <param name="data">Stream holding the encoded container.</param>
        /// <exception cref="ArgumentException">Thrown if <paramref name="data"/> is empty.</exception>
        /// <exception cref="FileNotFoundException">Thrown if the index does not exist.</exception>
        /// <exception cref="ArgumentOutOfRangeException">
        ///     Thrown if <paramref name="archiveId"/> is not contiguous with existing
        ///     records.
        /// </exception>
        public void Write(int indexId, int archiveId, JagStream data) {
            if(data == null || data.Length == 0)
                throw new ArgumentException("Refusing to write an empty archive: index " + indexId +
                    ", archive " + archiveId, nameof(data));

            Debug("Writing index " + indexId + ", archive " + archiveId + ", data len: " + data.Length);

            if(!indexChannels.ContainsKey(indexId))
                throw new FileNotFoundException("Unable to write, invalid index: " + indexId);

            //The index for which to update the archive header
            RSIndex index = GetIndexEntry(indexId);

            long ptr = (long) archiveId * RSIndex.SIZE;
            if(ptr > index.GetStream().Length)
                throw new ArgumentOutOfRangeException("Archive IDs must be contiguous -- " + archiveId + " @ " + ptr);

            //By default, appends the sectors to the end of the data stream
            int curSector = (int) (dataChannel.Length / RSSector.SIZE);

            //Are we adding a completely new archive?
            bool newArchive = ptr == index.GetStream().Length;
            int oldSectorCount = 0;

            //Read the existing header before anything is overwritten
            if(!newArchive) {
                Debug("**Overwriting archive header**");
                index.ReadContainerHeader(archiveId);
                int existingSector = index.GetSectorID(); //Find the first sector
                int oldSize = index.GetSize(); //Get the current sector size

                /* A record can exist and still describe nothing. An index padded out to reach a
                   later archive reads back as size 0, sector 0 - and sector 0 is the end-of-chain
                   marker, not a real location, which is why the reader rejects it. Reusing it as
                   a chain head appends the payload to the end of the dat2 and then records 0 as
                   its pointer, so the archive is written correctly and can never be found again.
                   Leaving both values alone here falls through to the append defaults instead. */
                if(existingSector > 0 && oldSize > 0) {
                    curSector = existingSector;
                    oldSectorCount = oldSize / RSSector.DATA_LEN +
                        (oldSize % RSSector.DATA_LEN > 0 ? 1 : 0);
                }
            }

            /* Walk the existing chain and allocate any extra sectors BEFORE touching the
               record. Everything that can throw is then in front of the mutation, so a failure
               cannot leave a header describing a chain that was never written. */
            List<int> sectors = new List<int>();
            int chainSector = curSector;

            for(int k = 0 ; k < oldSectorCount ; k++) {
                sectors.Add(chainSector);
                Debug("Overwriting sector: " + chainSector);
                byte[] sectorBytes = dataChannel.ReadBytes((long) chainSector * RSSector.SIZE, RSSector.SIZE);
                chainSector = RSSector.Decode(new JagStream(sectorBytes)).GetNextSector();
            }

            int newSectorCount = (int) data.Length / RSSector.DATA_LEN +
                (data.Length % RSSector.DATA_LEN > 0 ? 1 : 0);
            if(newSectorCount > oldSectorCount) {
                Debug("**Expanding the index**");

                int nextFreeSector = (int) (dataChannel.Length / RSSector.SIZE);
                for(int k = 0 ; k < newSectorCount - oldSectorCount ; k++) {
                    sectors.Add(nextFreeSector);
                    Debug("New sector: " + nextFreeSector);
                    nextFreeSector++;
                }
            }

            //Snapshot the record so a failed verification can put it back
            int recordLengthBefore = index.GetStream().Length;
            byte[]? recordBefore = null;
            if(!newArchive) {
                index.GetStream().Seek(ptr);
                recordBefore = new byte[RSIndex.SIZE];
                index.GetStream().Read(recordBefore, 0, RSIndex.SIZE);
            }

            _dirty = true;

            /* Seek for BOTH branches. Previously this only happened when overwriting, so a new
               archive written after an overwrite landed at whatever position the previous
               ReadContainerHeader left behind and silently clobbered a neighbouring record. */
            /* Record the sector the chain was actually written to, rather than the one read off
               the old record. The two agree whenever an existing chain is being reused, and
               differ exactly when it is not - so deriving the pointer from the allocation keeps
               them from drifting apart at all. */
            int firstSector = sectors[0];

            Debug("Updating archive header with size: " + data.Length + ", firstSector: " + firstSector);
            index.GetStream().Seek(ptr);
            index.GetStream().WriteMedium(data.Length); //Write the archive size
            index.GetStream().WriteMedium(firstSector); //Write the new sector ID

            try {
                int remaining = (int) data.Length;
                int chunk = 0; //The relative sector index for the archive data, actually

                data.Seek0();

                Debug("Beginning write of " + data.Length + " bytes...", LOG_DETAIL.ADVANCED);

                for(int k = 0 ; k < sectors.Count ; k++) {
                    int sectorId = sectors[k];
                    long sectorPtr = (long) sectorId * RSSector.SIZE;

                    Debug("\tSector " + sectorId + " @ " + sectorPtr + ", chunk " + chunk + ": " + remaining + " bytes remaining", LOG_DETAIL.ADVANCED);

                    //Read up to DATA_LEN bytes, or the remainder of, the archive data
                    byte[] chunkData = new byte[RSSector.DATA_LEN];
                    int bytesToRead = Math.Min(remaining, RSSector.DATA_LEN);
                    data.Read(chunkData, 0, bytesToRead);
                    PrintByteArray(chunkData);
                    remaining -= bytesToRead;

                    //For the last sector, mark as EOF
                    int nextSector = (k == sectors.Count - 1) ? 0 : sectors[k + 1];

                    Debug("Writing sector - Index: " + indexId + ", archive: " + archiveId + ", chunk: " + chunk + ", nextSector: " + nextSector + ", remaining: " + remaining);
                    JagStream sectorData = new RSSector(indexId, archiveId, chunk++, nextSector, chunkData).Encode();

                    byte[] sectorBytes = sectorData.ToArray();
                    dataChannel.WriteBytes(sectorPtr, sectorBytes, 0, sectorBytes.Length);
                }

                /* --- round-trip verification ---
                   Read back through the record that was just written, not through the sector
                   list still in hand. Verifying from sectors[0] only proves the sectors were
                   written; it says nothing about the pointer stored in the index, which is the
                   only thing a reader ever follows. That gap is precisely how a record pointing
                   at sector 0 survived a write path that already verified itself. */
                var expected = data.ToArray();

                index.ReadContainerHeader(archiveId);
                if(index.GetSize() != expected.Length || index.GetSectorID() != sectors[0])
                    throw new IOException("Index record does not describe the chain that was written: " +
                        "record says " + index.GetSize() + " bytes at sector " + index.GetSectorID() +
                        ", wrote " + expected.Length + " bytes at sector " + sectors[0]);

                JagStream verify = ReadSectorChain(index.GetSectorID(), index.GetSize());
                if(!verify.ToArray().SequenceEqual(expected))
                    throw new IOException("Sector chain verification failed");
            }
            catch {
                RestoreRecord(index, ptr, recordBefore, recordLengthBefore);
                throw;
            }
        }

        /// <summary>
        ///     Puts an archive header back after a failed write, so the index never describes a
        ///     chain that was not written.
        /// </summary>
        private static void RestoreRecord(RSIndex index, long ptr, byte[]? previous, int previousLength) {
            JagStream stream = index.GetStream();

            if(previous == null) {
                //The record was appended, so drop it again
                stream.Length = previousLength;
                stream.Seek(previousLength);
                return;
            }

            stream.Seek(ptr);
            stream.Write(previous, 0, previous.Length);
        }

        /// <summary>
        ///     Writes the staged cache to <paramref name="cacheDir"/>: the dat2 and every index
        ///     file together, so the result is always internally consistent.
        /// </summary>
        /// <param name="cacheDir">Destination directory. May be the directory the cache was opened from.</param>
        internal void SaveTo(string cacheDir) {
            Directory.CreateDirectory(cacheDir);

            /* Build the whole cache in a staging folder first. Nothing in the destination is
               touched until every file exists, so a failure part way through costs only a
               temporary directory rather than half-updating a real cache. */
            string staging = Path.Combine(cacheDir, ".fe-save-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(staging);

            try {
                dataChannel.SaveTo(Path.Combine(staging, "main_file_cache.dat2"));
                foreach(KeyValuePair<int, RSIndex> entry in indexChannels)
                    JagStream.Save(entry.Value.GetStream(), Path.Combine(staging, "main_file_cache.idx" + entry.Key));

                bool inPlace = PathsEqual(cacheDir, _cacheDir);

                //The mapping has to go before the source file can be replaced
                if(inPlace)
                    dataChannel.CloseMap();

                try {
                    /* Payload before pointer. If this is interrupted, unreferenced sectors are
                       harmless whereas records pointing at absent data are not. indexChannels is
                       sorted, so idx255 - the pointer to every reference table - lands last. */
                    Promote(staging, cacheDir, "main_file_cache.dat2");
                    foreach(KeyValuePair<int, RSIndex> entry in indexChannels)
                        Promote(staging, cacheDir, "main_file_cache.idx" + entry.Key);
                }
                finally {
                    if(inPlace)
                        dataChannel.Reopen(DataFile(cacheDir, "dat2"));
                }

                //Only once every file landed: until then the overlay is the only copy
                if(inPlace)
                    dataChannel.ClearStaged();

                _dirty = false;
            }
            finally {
                if(Directory.Exists(staging))
                    Directory.Delete(staging, true);
            }
        }

        private static void Promote(string staging, string destDir, string fileName) {
            File.Move(Path.Combine(staging, fileName), Path.Combine(destDir, fileName), overwrite: true);
        }

        private static bool PathsEqual(string a, string b) {
            return string.Equals(
                Path.TrimEndingDirectorySeparator(Path.GetFullPath(a)),
                Path.TrimEndingDirectorySeparator(Path.GetFullPath(b)),
                StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>
        /// Reads <paramref name="length"/> bytes starting from the given sector chain.
        /// </summary>
        private JagStream ReadSectorChain(int firstSector, int length) {
            JagStream result = new JagStream(length);
            int remaining = length;
            int sectorId = firstSector;
            while(remaining > 0 && sectorId > 0) {
                long ptr = (long) sectorId * RSSector.SIZE;
                byte[] raw = dataChannel.ReadBytes(ptr, RSSector.SIZE);
                RSSector sector = RSSector.Decode(new JagStream(raw));
                int bytes = Math.Min(remaining, RSSector.DATA_LEN);
                result.Write(sector.GetData(), 0, bytes);
                remaining -= bytes;
                sectorId = sector.GetNextSector();
            }
            return result.Flip();
        }

        /// <summary>
        ///     Releases the cache files. Deliberately persists nothing: this also runs when the
        ///     user opens a different cache, so saving here would commit edits silently.
        /// </summary>
        public void Dispose() {
            dataChannel?.Dispose();
            dataChannel = null!;
        }
    }
}
