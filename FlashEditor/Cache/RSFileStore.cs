﻿using static FlashEditor.utils.DebugUtil;
using System.IO;
using System;
using System.Collections.Generic;
using System.Linq;

namespace FlashEditor.cache {
    public class RSFileStore {
        internal RSIndex dataChannel;
        internal SortedDictionary<int, RSIndex> indexChannels = new SortedDictionary<int, RSIndex>();

        /// <summary>
        /// Loads the main data, metadata, and indice files into a corresponding <c>JagStream</c>
        /// </summary>
        /// <param name="cacheDir">the base directory for the cache files</param>
        public RSFileStore(string cacheDir) {
            cacheDir += "/main_file_cache.";

            //Load the cache into memory
            dataChannel = LoadIndex(cacheDir + "dat2");

            //And load in the data from the meta indexes, including reference tables
            for(int k = 0; k <= RSConstants.META_INDEX; k++)
                if(File.Exists(cacheDir + "idx" + k))
                    indexChannels.Add(k, LoadIndex(cacheDir + "idx" + k));
        }

        /// <summary>
        /// Gets the number of files of the specified type.
        /// </summary>
        /// <param name="type">The index type</param>
        /// <returns>The number of files</returns>
        public int GetFileCount(int type) {
            if(!indexChannels.ContainsKey(type))
                throw new FileNotFoundException("Index " + type + " invalid");

            return (int) (indexChannels[type].GetStream().Length / RSIndex.SIZE);
        }

        /// <summary>
        /// Reads binary data from a file into the specified stream
        /// </summary>
        /// <param name="stream">The stream (reference) to read the data into</param>
        /// <param name="directory">The directory of the binary file</param>
        private RSIndex LoadIndex(string directory) {
            return new RSIndex(JagStream.LoadStream(directory));
        }

        /// <summary>
        /// Returns the total number of non-meta indices
        /// </summary>
        /// <returns>The length of the <param name="indexChannels"> array</returns>
        internal int GetTypeCount() {
            if(indexChannels == null)
                throw new NullReferenceException("IndexChannels is null");

            //Don't include the meta index as a type, of course
            return indexChannels.Keys.Where(x => x < RSConstants.META_INDEX).Max();
        }

        internal RSIndex GetIndex(int type) {
            if(!indexChannels.ContainsKey(type))
                throw new FileNotFoundException("Index " + type + " could not be found.");

            return indexChannels[type];
        }

        /**
        * Writes a file.
        * 
        * @param type
        *            The type of the file.
        * @param id
        *            The id of the file.
        * @param data
        *            A {@link ByteBuffer} containing the contents of the file.
        * @param overwrite
        *            A flag indicating if the existing file should be overwritten.
        * @return A flag indicating if the file was written successfully.
        * @throws IOException
        *             if an I/O error occurs.
        */

        /*
         * Imagine, hypothetically, there is an index with 3 container headers as such:
         * Container 0  Container 1 Container 2
         * [med,med]    [med,med]   [med,med]
         * If containerID = 3, that will be index 18 ie equal to length of the index stream
         * This means we are required to expand the index
         * However, if containerID = 4, this means we would be skipping 3 which is dumb af
         */

        public void Write(int type, int containerId, JagStream data) {
            Debug("Writing index " + type + ", container " + containerId + ", data len: " + data.Length);

            if(!indexChannels.ContainsKey(type))
                throw new FileNotFoundException("Unable to write, invalid type: " + type);

            //The index for which to update the container header
            RSIndex index = GetIndex(type);

            long ptr = containerId * RSIndex.SIZE;
            if(ptr > index.GetStream().Length)
                throw new ArgumentOutOfRangeException("Container IDs must be contiguous -- " + containerId + " @ " + ptr);

            //By default, appends the sectors to the end of the data stream
            int curSector = (int) dataChannel.GetStream().Length / RSSector.SIZE;

            //Are we adding a completely new container?
            bool newContainer = ptr == index.GetStream().Length;
            int oldSectorCount = 0;

            //Overwrite any existing container headers first
            if(!newContainer) {
                Debug("**Overwriting container header**");
                index.ReadContainerHeader(containerId);
                curSector = index.GetSectorID(); //Find the first sector
                int oldSize = index.GetSize(); //Get the current sector size
                index.GetStream().Seek(ptr);
                oldSectorCount = oldSize / 512 + (oldSize % 512 > 0 ? 1 : 0);
            }

            //Update the container header
            Debug("Updating container header with size: " + data.Length + ", curSector: " + curSector);
            index.GetStream().WriteMedium(data.Length); //Write the container size
            index.GetStream().WriteMedium(curSector); //Write the new sector ID

            //Prepare the sectors to overwrite
            List<int> sectors = new List<int>();

            for(int k = 0; k < oldSectorCount; k++) {
                sectors.Add(curSector);
                Debug("Overwriting sector: " + curSector);
                ptr = curSector * RSSector.SIZE;
                JagStream overwriteSector = dataChannel.GetStream().GetSubStream(RSSector.SIZE, ptr);
                curSector = RSSector.Decode(overwriteSector).GetNextSector();
            }

            int newSectorCount = (int) data.Length / 512 + (data.Length % 512 > 0 ? 1 : 0);
            if(newSectorCount > oldSectorCount) {
                Debug("**Expanding the index**");

                for(int k = 0; k < newSectorCount - oldSectorCount; k++) {
                    sectors.Add(++curSector);
                    Debug("New sector: " + curSector);
                }
            }

            int remaining = (int) data.Length;
            int chunk = 0; //The relative sector index for the container data, actually

            data.Seek0();

            Debug("Beginning write of " + data.Length + " bytes...", LOG_DETAIL.ADVANCED);

            for(int k = 0; k < sectors.Count; k++) {
                curSector = sectors[k];

                ptr = curSector * RSSector.SIZE;

                Debug("\tSector " + curSector + " @ " + ptr + ", chunk " + chunk + ": " + remaining + " bytes remaining", LOG_DETAIL.ADVANCED);

                //Read up to DATA_LEN bytes, or the remainder of, the container data
                byte[] chunkData = new byte[RSSector.DATA_LEN];
                int bytesToRead = Math.Min(remaining, RSSector.DATA_LEN);
                data.Read(chunkData, 0, bytesToRead);
                PrintByteArray(chunkData);
                remaining -= bytesToRead;

                //For the last sector, mark as EOF
                int nextSector = (k == sectors.Count - 1) ? 0 : sectors[k + 1];
                //If we just read the last sector, mark as EOF

                Debug("Writing sector - Index: " + type + ", container: " + containerId + ", chunk: " + chunk + ", nextSector: " + nextSector + ", remaining: " + remaining);
                JagStream sectorData = new RSSector(type, containerId, chunk++, nextSector, chunkData).Encode();

                dataChannel.GetStream().Seek(ptr);
                dataChannel.GetStream().Write(sectorData.ToArray(), 0, sectorData.ToArray().Length);
            }
        }
    }
}
