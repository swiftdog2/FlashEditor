using FlashEditor.utils;
using System.IO;

namespace FlashEditor.cache {
    public class FileStore {
        internal Index dataChannel;
        internal Index metaChannel;
        internal Index[] indexChannels;

        /// <summary>
        /// Loads the main data, metadata, and indice files into a corresponding <c>JagStream</c>
        /// </summary>
        /// <param name="cacheDir">the base directory for the cache files</param>
        public FileStore(string cacheDir) {
            //Initialise the channels
            dataChannel = new Index();
            metaChannel = new Index();

            cacheDir += "/main_file_cache.";

            //Load the cache into memory
            dataChannel = LoadIndex(Constants.DAT2_INDEX, cacheDir + "dat2"); //Load the main data file
            metaChannel = LoadIndex(Constants.CRCTABLE_INDEX, cacheDir + "idx255"); //Load the metadata file

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
            indexChannels = new Index[count];

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
            if((type < 0 || type >= indexChannels.Length) && type != Constants.CRCTABLE_INDEX)
                throw new FileNotFoundException("Unable to get file count for type " + type);

            return (int) ((type == Constants.CRCTABLE_INDEX ? metaChannel : indexChannels[type]).GetStream().Length / Index.SIZE);
        }

        /// <summary>
        /// Reads binary data from a file into the specified stream
        /// </summary>
        /// <param name="stream">The stream (reference) to read the data into</param>
        /// <param name="directory">The directory of the binary file</param>
        private Index LoadIndex(int id, string directory) {
            if(!File.Exists(directory)) {
                string errorMsg = "'" + directory + "' could not be found.";
                DebugUtil.Debug(errorMsg);
                throw new FileNotFoundException(errorMsg);
            }

            byte[] data = File.ReadAllBytes(directory);
            if(data.Length == 0)
                DebugUtil.Debug("No data read for directory: " + directory);

            return new Index(id, new JagStream(data));
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

        public void Write(int index, int sectorId, JagStream data, bool overwrite) {
            if((index < 0 || index >= indexChannels.Length) && index != 255)
                throw new FileNotFoundException();

            Index indexChannel = index == Constants.CRCTABLE_INDEX ? metaChannel : indexChannels[index];

            int nextSector;
            long ptr = sectorId * Index.SIZE;

            JagStream buf;
            Index idx;

            if(overwrite) {
                if(ptr < 0)
                    throw new IOException();
                else if(ptr >= indexChannel.GetSize())
                    return;

                buf = new JagStream(Index.SIZE);
                buf.Read(indexChannel.GetStream().ToArray(), (int) ptr, Index.SIZE);

                idx = Index.Decode(buf.Flip());
                nextSector = idx.GetSectorID();

                if(nextSector <= 0 || nextSector > dataChannel.GetSize() * (long) Sector.SIZE)
                    return;

            } else {
                nextSector = (int) ((dataChannel.GetSize() + Sector.SIZE - 1) / (long) Sector.SIZE);
                if(nextSector == 0)
                    nextSector = 1;
            }

            bool extended = sectorId > 0xFFFF;
            idx = new Index(index, data.Remaining(), nextSector);
            JagStream newIndexStream = idx.Encode();

            indexChannel.GetStream().Write(newIndexStream.ToArray(), (int) ptr, newIndexStream.ToArray().Length);

            buf = new JagStream(Sector.SIZE);

            int chunk = 0, remaining = idx.GetSize();
            do {
                int curSector = nextSector;
                ptr = curSector * Sector.SIZE;
                nextSector = 0;

                Sector sector;

                if(overwrite) {
                    buf.Clear();

                    buf.Read(dataChannel.GetStream().ToArray(), (int) ptr, Sector.SIZE);

                    sector = Sector.Decode(buf.Flip());

                    if(sector.GetType() != index)
                        return;

                    if(sector.GetId() != sectorId)
                        return;

                    if(sector.GetChunk() != chunk)
                        return;

                    nextSector = sector.GetNextSector();
                    if(nextSector < 0 || nextSector > dataChannel.GetSize() / (long) Sector.SIZE)
                        return;
                }

                if(nextSector == 0) {
                    overwrite = false;
                    nextSector = (int) ((dataChannel.GetSize() + Sector.SIZE - 1) / (long) Sector.SIZE);
                    if(nextSector == 0)
                        nextSector++;
                    if(nextSector == curSector)
                        nextSector++;
                }

                int dataSize = Sector.DATA_LEN;

                byte[] bytes = new byte[dataSize];
                if(remaining < dataSize) {
                    data.Read(bytes, 0, remaining);
                    nextSector = 0; // mark as EOF
                    remaining = 0;
                } else {
                    remaining -= dataSize;
                    data.Read(bytes, 0, dataSize);
                }

                sector = new Sector(index, sectorId, chunk++, nextSector, bytes);

                JagStream sectorData = sector.Encode();

                dataChannel.GetStream().Write(sectorData.ToArray(), (int) ptr, sectorData.ToArray().Length);
            } while(remaining > 0);
        }
    }
}
