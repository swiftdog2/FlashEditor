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
            dataChannel = LoadIndex(RSConstants.DAT2_INDEX, cacheDir + "dat2"); //Load the main data file
            metaChannel = LoadIndex(RSConstants.CRCTABLE_INDEX, cacheDir + "idx255"); //Load the metadata file

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
                indexChannels[k] = LoadIndex(k, cacheDir + "idx" + k);
        }

        /// <summary>
        /// Gets the number of files of the specified type.
        /// </summary>
        /// <param name="type">The index type</param>
        /// <returns>The number of files</returns>
        public int GetFileCount(int type) {
            if((type < 0 || type >= indexChannels.Length) && type != RSConstants.CRCTABLE_INDEX)
                throw new FileNotFoundException("Unable to get file count for type " + type);

            return (int) ((type == RSConstants.CRCTABLE_INDEX ? metaChannel : indexChannels[type]).GetStream().Length / RSIndex.SIZE);
        }

        /// <summary>
        /// Reads binary data from a file into the specified stream
        /// </summary>
        /// <param name="stream">The stream (reference) to read the data into</param>
        /// <param name="directory">The directory of the binary file</param>
        private RSIndex LoadIndex(int id, string directory) {
            if(!File.Exists(directory)) {
                string errorMsg = "'" + directory + "' could not be found.";
                DebugUtil.Debug(errorMsg);
                throw new FileNotFoundException(errorMsg);
            }

            byte[] data = File.ReadAllBytes(directory);
            if(data.Length == 0)
                DebugUtil.Debug("No data read for directory: " + directory);

            return new RSIndex(id, new JagStream(data));
        }

        /// <summary>
        /// Returns the total number of non-meta indices
        /// </summary>
        /// <returns>The length of the <param name="indexChannels"> array</returns>
        internal int GetTypeCount() {
            return indexChannels.Length;
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

        public void Write(int indexId, int containerId, JagStream data, bool overwrite) {
            DebugUtil.Debug("Writing index " + indexId + ", container " + containerId + ", data len: " + data.Length);

            if((indexId < 0 || indexId >= indexChannels.Length) && indexId != 255)
                throw new FileNotFoundException();

            RSIndex index = indexId == RSConstants.CRCTABLE_INDEX ? metaChannel : indexChannels[indexId];

            int nextSector = 0;
            long ptr = containerId * RSIndex.SIZE;

            JagStream buf;

            //If we are overwriting the existing sector ("filling the bucket"?)
            if(overwrite) {
                if(ptr < 0)
                    throw new IOException();
                else if(ptr >= index.GetSize())
                    return;

                //Seems completely useless tbh?
                JagStream newIndex = index.Encode();
                RSIndex x = RSIndex.Decode(newIndex);
                nextSector = index.GetSectorID();
                if(nextSector <= 0 || nextSector > dataChannel.GetSize() * (long) RSSector.SIZE)
                    return;
            } else {
                //We need to make a new sector
                nextSector = (int) ((dataChannel.GetSize() + RSSector.SIZE - 1) / (long) RSSector.SIZE);
                if(nextSector == 0)
                    nextSector = 1;
            }

            bool extended = containerId > 0xFFFF;

            //Update the index
            index = new RSIndex(indexId, (int) data.Length, nextSector);
            index.SetStream(data); //does this make sense?

            if(indexId == RSConstants.CRCTABLE_INDEX)
                metaChannel = index;
            else
                indexChannels[indexId] = index;

            buf = new JagStream(RSSector.SIZE);

            int chunk = 0, remaining = index.GetSize();

            JagStream newDataStream = new JagStream();

            while(remaining > 0) {
                int curSector = nextSector;
                ptr = curSector * RSSector.SIZE;
                nextSector = 0;

                RSSector sector;

                if(overwrite) {
                    buf.Clear();

                    buf.Read(dataChannel.GetStream().ToArray(), (int) ptr, RSSector.SIZE);

                    sector = RSSector.Decode(buf.Flip());

                    if(sector.GetType() != indexId)
                        return;

                    if(sector.GetId() != containerId)
                        return;

                    if(sector.GetChunk() != chunk)
                        return;

                    nextSector = sector.GetNextSector();
                    if(nextSector < 0 || nextSector > dataChannel.GetSize() / (long) RSSector.SIZE)
                        return;
                }

                if(nextSector == 0) {
                    overwrite = false;
                    nextSector = (int) ((dataChannel.GetSize() + RSSector.SIZE - 1) / (long) RSSector.SIZE);
                    if(nextSector == 0)
                        nextSector++;
                    if(nextSector == curSector)
                        nextSector++;
                }

                int dataSize = RSSector.DATA_LEN;

                byte[] bytes = new byte[dataSize];
                if(remaining < dataSize) {
                    data.Read(bytes, 0, remaining);
                    nextSector = 0; // mark as EOF
                    remaining = 0;
                } else {
                    remaining -= dataSize;
                    data.Read(bytes, 0, dataSize);
                }

                sector = new RSSector(indexId, containerId, chunk++, nextSector, bytes);

                JagStream sectorData = sector.Encode();
                DebugUtil.Debug("Writing sector - Index: " + indexId + ", container: " + containerId + ", chunk: " + chunk + ", nextSector: " + nextSector + ", bytes len: " + bytes.Length);
                DebugUtil.Debug("Data stream len : " + dataChannel.GetStream().Length + ", sectorData len: " + sectorData.Length + ", ptr: " + ptr);
                dataChannel.GetStream().Seek((int) ptr);
                dataChannel.GetStream().Write(sectorData.ToArray(), 0, sectorData.ToArray().Length);
            }
        }
    }
}
