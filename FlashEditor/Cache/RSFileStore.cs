using FlashEditor.utils;
using System.IO;

namespace FlashEditor.cache {
    public class RSFileStore {
        internal RSIndex dataChannel;
        internal RSIndex metaChannel;
        internal RSIndex[] indexChannels;

        /// <summary>
        /// Loads the main data, metadata, and indice files into a corresponding <c>JagStream</c>
        /// </summary>
        /// <param name="cacheDir">the base directory for the cache files</param>
        public RSFileStore(string cacheDir) {
            //Initialise the channels
            dataChannel = new RSIndex();
            metaChannel = new RSIndex();

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
    }
}
