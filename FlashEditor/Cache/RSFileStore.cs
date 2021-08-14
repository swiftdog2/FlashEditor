using static FlashEditor.utils.DebugUtil;
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
            dataChannel = LoadIndex(RSConstants.DAT2_INDEX, cacheDir + "dat2");

            //And load in the data from the meta indexes, including reference tables
            for(int k = 0; k <= RSConstants.META_INDEX; k++)
                if(File.Exists(cacheDir + "idx" + k))
                    indexChannels.Add(k, LoadIndex(k, cacheDir + "idx" + k));
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
        private RSIndex LoadIndex(int id, string directory) {
            return new RSIndex(LoadStream(directory));
        }

        public JagStream LoadStream(string directory) {
            if(!File.Exists(directory))
                throw new FileNotFoundException("'" + directory + "' could not be found.");

            byte[] data = File.ReadAllBytes(directory);
            if(data.Length == 0)
                Debug("No data read for directory: " + directory);

            //We initialise it like this to ensure the stream is expandable
            JagStream stream = new JagStream();
            stream.Write(data, 0, data.Length);
            return stream;
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

        public void Write(int indexId, int containerId, JagStream data, bool overwrite) {
            Debug("Writing index " + indexId + ", container " + containerId + ", data len: " + data.Length);

            if((indexId < 0 || indexId >= indexChannels.Keys.Max()) && indexId != RSConstants.META_INDEX)
                throw new FileNotFoundException("Unable to write, invalid index ID: " + indexId);

            RSIndex index = GetIndex(indexId);

            int nextSector = 0;
            long ptr = containerId * RSIndex.SIZE;

            JagStream buf;
            RSIndex newIndex;

            Debug("Total sectors: " + (int) ((dataChannel.GetStream().Length + RSSector.SIZE - 1) / (long) RSSector.SIZE));

            //If we are overwriting an existing sector
            if(overwrite) {
                Debug("Overwriting existing sector...", LOG_DETAIL.ADVANCED);
                if(ptr < 0) {
                    Debug("Error, ptr " + ptr + " out of bounds " + index.GetStream().Length);
                    throw new IOException();
                } else if(ptr >= index.GetStream().Length) { //previously was GetSize())
                    Debug("Error, ptr " + ptr + " out of bounds " + index.GetStream().Length);
                    return;
                }

                index.GetStream().Seek(ptr);
                index.GetStream().ReadMedium(); //size
                nextSector = index.GetStream().ReadMedium();

                Debug("Next sector: " + nextSector);

                if(nextSector <= 0 || nextSector > dataChannel.GetStream().Length * RSSector.SIZE) {
                    Debug("ERROR!!!! s1, sector " + nextSector.ToString() + " / " + (dataChannel.GetStream().Length * (long) RSSector.SIZE).ToString() + " out of bounds", LOG_DETAIL.INSANE);
                    return;
                }
            } else {
                //We need to make a new sector?
                nextSector = (int) ((dataChannel.GetStream().Length + RSSector.SIZE - 1) / RSSector.SIZE);
                if(nextSector == 0)
                    nextSector = 1;
                Debug("Creating new sector, nextSector: " + nextSector, LOG_DETAIL.INSANE);
            }

            Debug("data remaining: " + data.Remaining() + ", next sector: " + nextSector);

            //Update the meta index with the updated size and sector ID
            index.GetStream().Seek(ptr);
            index.GetStream().WriteMedium(data.Remaining());
            index.GetStream().WriteMedium(nextSector);

            buf = new JagStream(RSSector.SIZE);

            int chunk = 0, remaining = data.Remaining();

            do {
                int curSector = nextSector;
                ptr = curSector * RSSector.SIZE;
                nextSector = 0;

                RSSector sector;

                if(overwrite) {
                    buf.Clear();

                    buf.Write(dataChannel.GetStream().ToArray(), (int) ptr, RSSector.SIZE);
                    PrintByteArray(buf.ToArray());

                    sector = RSSector.Decode(buf.Flip());

                    if(sector.GetType() != indexId) {
                        Debug("ERROR!!!! s2, mismatching sector type " + sector.GetType() + ", index: " + indexId, LOG_DETAIL.INSANE);
                        return;
                    }

                    if(sector.GetId() != containerId) {
                        Debug("ERROR!!!! s3, mismatching sector id " + sector.GetId() + ", containerId: " + containerId, LOG_DETAIL.INSANE);
                        return;
                    }

                    if(sector.GetChunk() != chunk) {
                        Debug("ERROR!!!! s4, mismatching sector chunk " + sector.GetChunk() + ", chunk: " + chunk, LOG_DETAIL.INSANE);
                        return;
                    }

                    nextSector = sector.GetNextSector();
                    if(nextSector < 0 || nextSector > dataChannel.GetStream().Length / RSSector.SIZE) {
                        Debug("ERROR!!!! s5, sector out of bounds.", LOG_DETAIL.INSANE);
                        return;
                    }
                }

                if(nextSector == 0) {
                    overwrite = false;
                    nextSector = (int) ((dataChannel.GetStream().Length + RSSector.SIZE - 1) / RSSector.SIZE);
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
                Debug("Writing sector - Index: " + indexId + ", container: " + containerId + ", chunk: " + chunk + ", nextSector: " + nextSector + ", bytes len: " + bytes.Length);
                dataChannel.GetStream().Seek((int) ptr);
                dataChannel.GetStream().Write(sectorData.ToArray(), 0, sectorData.ToArray().Length);
            } while(remaining > 0);
        }
    }
}
