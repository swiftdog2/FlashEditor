using static FlashEditor.Utils.DebugUtil;
using System.IO;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace FlashEditor.cache {
    public class RSFileStore : IDisposable {
        internal MappedDataChannel dataChannel;
        internal SortedDictionary<int, RSIndex> indexChannels = new SortedDictionary<int, RSIndex>();

        /// <summary>
        /// Loads the main data, metadata, and index files into a corresponding <c>JagStream</c>
        /// </summary>
        /// <param name="cacheDir">the base directory for the cache files</param>
        public RSFileStore(string cacheDir) {
            cacheDir += "/main_file_cache.";

            //Load the dat2 via memory-mapped file
            dataChannel = new MappedDataChannel(cacheDir + "dat2");

            //And load in the data from the meta indexes, including reference tables
            var sb = new StringBuilder();
            for(int k = 0; k <= RSConstants.META_INDEX; k++) {
                sb.Clear();
                sb.Append(cacheDir).Append("idx").Append(k);
                string path = sb.ToString();
                if(File.Exists(path))
                    indexChannels.Add(k, LoadIndex(path));
            }
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
        ///     Writes an archive's payload to <c>main_file_cache.dat2</c> and updates
        ///     the corresponding six byte record inside the index file.
        ///     Any additional sectors required are appended to the end of the data file.
        /// </summary>
        /// <param name="indexId">Index the archive belongs to.</param>
        /// <param name="archiveId">Archive id within the index.</param>
        /// <param name="data">Stream holding the encoded container.</param>
        /// <exception cref="FileNotFoundException">Thrown if the index does not exist.</exception>
        /// <exception cref="ArgumentOutOfRangeException">
        ///     Thrown if <paramref name="archiveId"/> is not contiguous with existing
        ///     records.
        /// </exception>
        public void Write(int indexId, int archiveId, JagStream data) {
            Debug("Writing index " + indexId + ", archive " + archiveId + ", data len: " + data.Length);

            if(!indexChannels.ContainsKey(indexId))
                throw new FileNotFoundException("Unable to write, invalid index: " + indexId);

            //The index for which to update the archive header
            RSIndex index = GetIndexEntry(indexId);

            long ptr = archiveId * RSIndex.SIZE;
            if(ptr > index.GetStream().Length)
                throw new ArgumentOutOfRangeException("Archive IDs must be contiguous -- " + archiveId + " @ " + ptr);

            //By default, appends the sectors to the end of the data stream
            int curSector = (int) (dataChannel.Length / RSSector.SIZE);

            //Are we adding a completely new archive?
            bool newArchive = ptr == index.GetStream().Length;
            int oldSectorCount = 0;

            //Overwrite any existing archive headers first
            if(!newArchive) {
                Debug("**Overwriting archive header**");
                index.ReadContainerHeader(archiveId);
                curSector = index.GetSectorID(); //Find the first sector
                int oldSize = index.GetSize(); //Get the current sector size
                index.GetStream().Seek(ptr);
                oldSectorCount = oldSize / RSSector.DATA_LEN +
                    (oldSize % RSSector.DATA_LEN > 0 ? 1 : 0);
            }

            //Update the archive header
            Debug("Updating archive header with size: " + data.Length + ", curSector: " + curSector);
            index.GetStream().WriteMedium(data.Length); //Write the archive size
            index.GetStream().WriteMedium(curSector); //Write the new sector ID

            //Prepare the sectors to overwrite
            List<int> sectors = new List<int>();

            for(int k = 0; k < oldSectorCount; k++) {
                sectors.Add(curSector);
                Debug("Overwriting sector: " + curSector);
                ptr = curSector * RSSector.SIZE;
                byte[] sectorBytes = dataChannel.ReadBytes(ptr, RSSector.SIZE);
                curSector = RSSector.Decode(new JagStream(sectorBytes)).GetNextSector();
            }

            int newSectorCount = (int)data.Length / RSSector.DATA_LEN +
                (data.Length % RSSector.DATA_LEN > 0 ? 1 : 0);
            if (newSectorCount > oldSectorCount) {
                Debug("**Expanding the index**");

                int nextFreeSector = (int)(dataChannel.Length / RSSector.SIZE);
                for (int k = 0; k < newSectorCount - oldSectorCount; k++) {
                    sectors.Add(nextFreeSector);
                    Debug("New sector: " + nextFreeSector);
                    nextFreeSector++;
                }
            }

            int remaining = (int) data.Length;
            int chunk = 0; //The relative sector index for the archive data, actually

            data.Seek0();

            Debug("Beginning write of " + data.Length + " bytes...", LOG_DETAIL.ADVANCED);

            for(int k = 0; k < sectors.Count; k++) {
                curSector = sectors[k];

                ptr = curSector * RSSector.SIZE;

                Debug("\tSector " + curSector + " @ " + ptr + ", chunk " + chunk + ": " + remaining + " bytes remaining", LOG_DETAIL.ADVANCED);

                //Read up to DATA_LEN bytes, or the remainder of, the archive data
                byte[] chunkData = new byte[RSSector.DATA_LEN];
                int bytesToRead = Math.Min(remaining, RSSector.DATA_LEN);
                data.Read(chunkData, 0, bytesToRead);
                PrintByteArray(chunkData);
                remaining -= bytesToRead;

                //For the last sector, mark as EOF
                int nextSector = (k == sectors.Count - 1) ? 0 : sectors[k + 1];
                //If we just read the last sector, mark as EOF

                Debug("Writing sector - Index: " + indexId + ", archive: " + archiveId + ", chunk: " + chunk + ", nextSector: " + nextSector + ", remaining: " + remaining);
                JagStream sectorData = new RSSector(indexId, archiveId, chunk++, nextSector, chunkData).Encode();

                byte[] sectorBytes = sectorData.ToArray();
                dataChannel.WriteBytes(ptr, sectorBytes, 0, sectorBytes.Length);
            }

            // --- round-trip verification ---
            var expected = data.ToArray();
            JagStream verify = ReadSectorChain(sectors[0], expected.Length);
            if(!verify.ToArray().SequenceEqual(expected))
                throw new IOException("Sector chain verification failed");
        }

        /// <summary>
        /// Reads <paramref name="length"/> bytes starting from the given sector chain.
        /// </summary>
        private JagStream ReadSectorChain(int firstSector, int length) {
            JagStream result = new JagStream(length);
            int remaining = length;
            int sectorId = firstSector;
            while(remaining > 0 && sectorId > 0) {
                long ptr = sectorId * RSSector.SIZE;
                byte[] raw = dataChannel.ReadBytes(ptr, RSSector.SIZE);
                RSSector sector = RSSector.Decode(new JagStream(raw));
                int bytes = Math.Min(remaining, RSSector.DATA_LEN);
                result.Write(sector.GetData(), 0, bytes);
                remaining -= bytes;
                sectorId = sector.GetNextSector();
            }
            return result.Flip();
        }

        public void Dispose() {
            dataChannel?.Dispose();
            dataChannel = null;
        }
    }
}
