using FlashEditor.utils;
using System.IO;

namespace FlashEditor.cache {
    public class RSFileStore {
        private RSIndex dataChannel;
        private RSIndex metaChannel;
        private RSIndex[] indexChannels;

        /// <summary>
        /// Loads the main data, metadata, and indice files into a corresponding <c>JagStream</c>
        /// </summary>
        /// <param name="cacheDir">the base directory for the cache files</param>
        public RSFileStore(string cacheDir) {
            //Initialise the channels
            dataChannel = new RSIndex();
            metaChannel = new RSIndex();

            //Append the cache file name prefix
            cacheDir += "/main_file_cache.";

            //Load the cache into memory
            dataChannel = LoadIndex(cacheDir + "dat2"); //Load the main data file
            metaChannel = LoadIndex(cacheDir + "idx255"); //Load the metadata file

            int count = 0;

            //Count how many idx files exist
            for(int k = 0; k < 254; k++) {
                if(!File.Exists(cacheDir + "idx" + k)) {
                    //If we didn't read any, check the directory is set correctly
                    if(count == 0)
                        throw new FileNotFoundException("No index files could be found");
                    break;
                }
                count++;
            }

            //Load the indexes
            indexChannels = new RSIndex[count];

            //And load in the data
            for(int k = 0; k < count; k++)
                indexChannels[k] = LoadIndex(cacheDir + "idx" + k);
        }

        /// <summary>
        /// Write the index to a file
        /// </summary>
        /// <param name="type">The index type</param>
        /// <returns>Whether or not the index was successfully written</returns>
        internal bool WriteIndex(int type) {
            //Check if index is out of bounds
            if((type < 0 || type >= indexChannels.Length) && type != 255)
                throw new FileNotFoundException("Index " + type + " could not be found.");

            //Retrieve the index channel we are looking for
            RSIndex indexChannel = type == 255 ? metaChannel : indexChannels[type];




            //JagStream.Save(indexChannel.GetStream(), RSConstants.CACHE_DIRECTORY + "/main_file_cache.idx" + type);

            return true;
        }

        internal RSIndex GetIndex(int type) {
            //Check if index is out of bounds
            if((type < 0 || type >= indexChannels.Length) && type != 255)
                throw new FileNotFoundException("Index " + type + " could not be found.");

            //Retrieve the index channel we are looking for
            return type == 255 ? metaChannel : indexChannels[type];
        }

        /// <summary>
        /// Read the index data
        /// </summary>
        /// <param name="type">The index type</param>
        /// <param name="id">The container index</param>
        /// <returns>A <c>JagStream</c> containing the container data</returns>
        internal JagStream GetContainer(int type, int id) {
            //Get the specified index
            RSIndex index = GetIndex(type);

            //Find the beginning of the index
            long pos = id * RSIndex.SIZE;

            if(pos < 0 || pos >= index.GetStream().Length)
                throw new FileNotFoundException("Position is out of bounds for type " + type + ", id " + id);

            //Seek to the relevant sector within the index
            index.GetStream().Seek(pos);

            //Read the archive header, to get the size and sector ID
            index.ReadHeader();

            //Not sure if this is 100% necessary, but basically reset the position
            //index.GetStream().Seek0();

            if(index.GetSize() < 0)
                return null;

            if(index.GetSectorID() <= 0 || index.GetSectorID() > dataChannel.GetStream().Length / RSSector.SIZE)
                return null;

            //Allocate buffers for the data and sector
            JagStream containerData = new JagStream(index.GetSize());

            int chunk = 0, remaining = index.GetSize();

            //Point to the start of the sector
            pos = index.GetSectorID() * RSSector.SIZE;

            do {
                //Read from the data index into the sector buffer
                dataChannel.GetStream().Seek(pos);

                //Read in the sector from the data channel
                RSSector sector = RSSector.Decode(dataChannel.GetStream());

                if(remaining > RSSector.DATA_LEN) {
                    //Cache this sector so far
                    containerData.Write(sector.GetData(), 0, RSSector.DATA_LEN);

                    //And subtract the sector we read from data remaining
                    remaining -= RSSector.DATA_LEN;

                    if(sector.GetType() != type)
                        throw new IOException("File type mismatch.");

                    if(sector.GetId() != id)
                        throw new IOException("File id mismatch.");

                    if(sector.GetChunk() != chunk++)
                        throw new IOException("Chunk mismatch.");

                    //Then move the pointer to the next sector
                    pos = sector.GetNextSector() * RSSector.SIZE;
                } else {
                    //Otherwise if the amount remaining is less than the sector size, put it down
                    containerData.Write(sector.GetData(), 0, remaining);

                    //We've read the last sector in this index!
                    remaining = 0;
                }
            }
            while(remaining > 0);

            //Return the data stream back to it's original position
            dataChannel.GetStream().Seek(0);

            return containerData.Flip();
        }

        /// <summary>
        /// Gets the number of files of the specified type.
        /// </summary>
        /// <param name="type">The index type</param>
        /// <returns>The number of files</returns>
        public int GetFileCount(int type) {
            if((type < 0 || type >= indexChannels.Length) && type != 255)
                throw new FileNotFoundException("Unable to get file count for type " + type);

            return (int) ((type == 255 ? metaChannel : indexChannels[type]).GetStream().Length / RSIndex.SIZE);
        }

        /// <summary>
        /// Reads binary data from a file into the specified stream
        /// </summary>
        /// <param name="stream">The stream (reference) to read the data into</param>
        /// <param name="directory">The directory of the binary file</param>
        private RSIndex LoadIndex(string directory) {
            if(!File.Exists(directory)) {
                string errorMsg = "'" + directory + "' could not be found.";
                DebugUtil.Debug(errorMsg);
                throw new FileNotFoundException(errorMsg);
            }

            byte[] data = File.ReadAllBytes(directory);
            if(data.Length == 0)
                DebugUtil.Debug("No data read for directory: " + directory);

            return new RSIndex(new JagStream(data));
        }

        /// <summary>
        /// Returns the total number of non-meta indices
        /// </summary>
        /// <returns>The length of the <param name="indexChannels"> array</returns>
        internal int GetTypeCount() {
            return indexChannels.Length;
        }

        /// <summary>
        /// Disposes all of the memorystreams
        /// </summary>
        public void DisposeAll() {
            /*
            if(dataChannel != null)
                dataChannel.GetStream().Dispose();

            if(metaChannel != null)
                metaChannel.GetStream().Dispose();

            foreach(RSIndex index in indexChannels)
                if(index != null)
                    index.GetStream().Dispose();
             */
        }
    }
}
