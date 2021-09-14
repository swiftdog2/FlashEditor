using FlashEditor.Collections;
using FlashEditor.utils;
using System;
using System.Collections.Generic;
using static FlashEditor.utils.DebugUtil;

namespace FlashEditor.cache {
    class RSArchive {
        public SortedDictionary<int, JagStream> entries = new SortedDictionary<int, JagStream>();
        public int chunks = 1;

        /// <summary>
        /// Create a new Archive with <paramref name="size"/> entries
        /// </summary>
        /// <param name="size">The number of entries</param>
        public RSArchive() {
        }

        /// <summary>
        /// Constructs an Archive from an RSContainer stream
        /// </summary>
        /// <param name="stream">The stream containing the archive data</param>
        /// <param name="size">The total number of file entries</param>
        /// <returns></returns>
        public static RSArchive Decode(JagStream stream, int size) {
            //Allocate a new archive object
            RSArchive archive = new RSArchive();

            //Read the number of chunks at the end of the archive
            stream.Seek(stream.Length - 1);
            archive.chunks = stream.ReadUnsignedByte();

            Debug("Chunk count: " + archive.chunks, LOG_DETAIL.INSANE);

            //Read the sizes of the child entries and individual chunks
            int[][] chunkSizes = ArrayUtil.ReturnRectangularArray<int>(archive.chunks, size);
            int[] entrySizes = new int[size];

            Debug("Entry count: " + size, LOG_DETAIL.INSANE);

            stream.Seek(stream.Length - 1 - archive.chunks * size * 4);

            //Read the chunks
            for(int chunk = 0; chunk < archive.chunks; chunk++) {
                Debug("chunk size: " + size, LOG_DETAIL.INSANE);
                int cumulativeChunkSize = 0;
                for(int id = 0; id < size; id++) {
                    //Read the delta-encoded chunk length
                    int delta = stream.ReadInt();
                    cumulativeChunkSize += delta;
                    Debug(" " + delta, LOG_DETAIL.INSANE);

                    //Store the size of this chunk
                    chunkSizes[chunk][id] = cumulativeChunkSize;

                    //And add it to the size of the whole file
                    entrySizes[id] += cumulativeChunkSize;
                    Debug("\t- Entry " + id + " size: " + cumulativeChunkSize, LOG_DETAIL.INSANE);
                }
            }

            //Allocate the buffers for the child entries
            for(int id = 0; id < size; id++)
                archive.entries[id] = new JagStream(entrySizes[id]);

            //Return the stream to 0 otherwise this shit doesn't work
            stream.Seek0();

            //Read the data into the buffers 
            for(int chunk = 0; chunk < archive.chunks; chunk++) {
                for(int id = 0; id < size; id++) {
                    //Get the length of this chunk
                    int chunkSize = chunkSizes[chunk][id];

                    //Copy this chunk into a temporary buffer
                    byte[] temp = new byte[chunkSize];
                    stream.Read(temp, 0, temp.Length);

                    //Copy the temporary buffer into the file buffer
                    archive.entries[id].Write(temp, 0, temp.Length);
                }
            }

            //Flip all of the buffers
            for(int id = 0; id < size; id++)
                archive.entries[id].Flip();

            //Return the archive
            return archive;
        }

        public virtual JagStream Encode() {
            JagStream stream = new JagStream();

            /*
             * Reason why this looks backwards is because in the Decode() we have this line:
             *             stream.Seek(stream.Length - 1 - archive.chunks * size * 4);
             * Here, we are writing out the chunk data before the sizes so it all works out
             */

            //Write the chunk data for each entry stream
            foreach(KeyValuePair<int, JagStream> entry in entries)
                entry.Value.WriteTo(stream);

            //Write the chunk lengths
            int prev = 0;
            for(int chunk = 0; chunk < chunks; chunk++) {
                foreach(KeyValuePair<int, JagStream> entry in entries) {
                    //Archive is broken into chunks, which is the entry stream data
                    int chunkSize = (int) entry.Value.Length; //Therefore chunk size is entry stream length
                    stream.WriteInteger(chunkSize - prev); //So delta is the difference between the chunk sizes
                    prev = chunkSize; //Store the size of the last entry
                }
            }

            //Write out the number of chunks that the archive is split up into
            //We only used one chunk due to a limitation of the implementation
            stream.WriteByte(1);

            return stream.Flip();
        }

        /// <summary>
        /// Returns the file at the specified index id
        /// </summary>
        /// <param name="id">The file entry eindex</param>
        /// <returns></returns>
        public virtual JagStream GetEntry(int id) {
            return entries[id];
        }

        public int EntryCount() {
            return entries.Count;
        }

        internal void PutEntry(int entryId, JagStream entry) {
            if(entries.ContainsKey(entryId)) {
                //Update the entry
                entries[entryId] = entry;
                Debug("Updated archive entry " + entryId + ", len: " + entry.Length, LOG_DETAIL.ADVANCED);
            } else {
                //Add a new entry to the archive, expanding it
                entries.Add(entryId, entry);
                Debug("Added new entry " + entryId + ", len: " + entry.Length + ", total: " + entries.Count, LOG_DETAIL.INSANE);
            }
        }
    }
}