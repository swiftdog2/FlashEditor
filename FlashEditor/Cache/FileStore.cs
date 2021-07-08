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
            indexChannels = new Index[count];

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

            return (int) ((type == 255 ? metaChannel : indexChannels[type]).GetStream().Length / Index.SIZE);
        }

        /// <summary>
        /// Reads binary data from a file into the specified stream
        /// </summary>
        /// <param name="stream">The stream (reference) to read the data into</param>
        /// <param name="directory">The directory of the binary file</param>
        private Index LoadIndex(string directory) {
            if(!File.Exists(directory)) {
                string errorMsg = "'" + directory + "' could not be found.";
                DebugUtil.Debug(errorMsg);
                throw new FileNotFoundException(errorMsg);
            }

            byte[] data = File.ReadAllBytes(directory);
            if(data.Length == 0)
                DebugUtil.Debug("No data read for directory: " + directory);

            return new Index(new JagStream(data));
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

        public void Write(int type, int id, JagStream data, bool overwrite) {
            /*
            if((type < 0 || type >= indexChannels.Length) && type != 255)
                throw new FileNotFoundException();

            Index indexChannel = type == 255 ? metaChannel : indexChannels[type];

            int nextSector = 0;
            long ptr = id * (long) Index.SIZE;

            JagStream buf;

            if(overwrite) {
                if(ptr < 0)
                    throw new IOException();
                else if(ptr >= indexChannel.GetStream().Length)
                    return;

                buf = new JagStream(Index.SIZE);


                indexChannel.GetStream().Write()
                FileChannelUtils.readFully(indexChannel, buf, ptr);

                Index index = Index.Decode((JagStream) buf.Flip());
                nextSector = index.GetSectorID();
                if(nextSector <= 0 || nextSector > dataChannel.GetSize() * (long) Sector.SIZE)
                    return;
            } else {
                nextSector = (int) ((dataChannel.GetSize() + Sector.SIZE - 1) / (long) Sector.SIZE);
                if(nextSector == 0)
                    nextSector = 1;
            }

            bool extended = id > 0xFFFF;
            Index index = new Index(data.remaining(), nextSector);
            indexChannel.write(index.Encode(), ptr);

            buf = new JagStream(Sector.SIZE);

            int chunk = 0, remaining = index.GetSize();
            do {
                int curSector = nextSector;
                ptr = (long) curSector * (long) Sector.SIZE;
                nextSector = 0;

                if(overwrite) {
                    buf.clear();
                    FileChannelUtils.readFully(dataChannel, buf, ptr);

                    Sector sector = extended ? Sector.decodeExtended((ByteBuffer) buf.flip())
                        : Sector.decode((ByteBuffer) buf.flip());

                    if(sector.getType() != type)
                        return false;

                    if(sector.getId() != id)
                        return false;

                    if(sector.getChunk() != chunk)
                        return false;

                    nextSector = sector.getNextSector();
                    if(nextSector < 0 || nextSector > dataChannel.size() / (long) Sector.SIZE)
                        return false;
                }

                if(nextSector == 0) {
                    overwrite = false;
                    nextSector = (int) ((dataChannel.size() + Sector.SIZE - 1) / (long) Sector.SIZE);
                    if(nextSector == 0)
                        nextSector++;
                    if(nextSector == curSector)
                        nextSector++;
                }
                int dataSize = extended ? Sector.EXTENDED_DATA_SIZE : Sector.DATA_SIZE;
                byte[] bytes = new byte[dataSize];
                if(remaining < dataSize) {
                    data.get(bytes, 0, remaining);
                    nextSector = 0; // mark as EOF
                    remaining = 0;
                } else {
                    remaining -= dataSize;
                    data.get(bytes, 0, dataSize);
                }

                Sector sector = new Sector(type, id, chunk++, nextSector, bytes);
                dataChannel.write(sector.encode(), ptr);
            } while(remaining > 0);
        */
        }
    }
}
