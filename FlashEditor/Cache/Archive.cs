using FlashEditor.Collections;
using FlashEditor.utils;
using System;

namespace FlashEditor.cache {
    class Archive {
        private Entry[] entries;

        /// <summary>
        /// Create a new Archive with <paramref name="size"/> entries
        /// </summary>
        /// <param name="size">The number of entries</param>
        public Archive(int size) {
            entries = new Entry[size];
        }

        /// <summary>
        /// Constructs an Archive from a stream
        /// </summary>
        /// <param name="stream">The stream containing the archive data</param>
        /// <param name="size">The total number of file entries</param>
        /// <returns></returns>
        public static Archive Decode(JagStream stream, int size) {
            //Allocate a new archive object
            Archive archive = new Archive(size);

            //Read the number of chunks at the end of the archive
            stream.Seek(stream.Length - 1);
            int chunks = stream.ReadUnsignedByte();

            //Read the sizes of the child entries and individual chunks
            int[][] chunkSizes = ArrayUtil.ReturnRectangularArray<int>(chunks, size);
            int[] sizes = new int[size];

            stream.Seek(stream.Length - 1 - chunks * size * 4);

            for(int chunk = 0; chunk < chunks; chunk++) {
                int chunkSize = 0;
                for(int id = 0; id < size; id++) {
                    //Read the delta-encoded chunk length
                    int delta = stream.ReadInt();
                    chunkSize += delta;

                    //Store the size of this chunk
                    chunkSizes[chunk][id] = chunkSize;

                    //And add it to the size of the whole file
                    sizes[id] += chunkSize;
                }
            }

            //Allocate the buffers for the child entries
            for(int id = 0; id < size; id++)
                archive.entries[id] = new Entry(sizes[id]);

            //Return the stream to 0 otherwise this shit doesn't work
            stream.Seek0();

            //Read the data into the buffers
            for(int chunk = 0; chunk < chunks; chunk++) {
                for(int id = 0; id < size; id++) {
                    //Get the length of this chunk
                    int chunkSize = chunkSizes[chunk][id];

                    //Copy this chunk into a temporary buffer
                    byte[] temp = new byte[chunkSize];
                    stream.Read(temp, 0, temp.Length);

                    //Copy the temporary buffer into the file buffer
                    archive.entries[id].stream.Write(temp, 0, temp.Length);
                    //archive.entries[id].PutEntry(id, new ChildEntry(new JagStream(temp)));
                }
            }

            //Flip all of the buffers
            for(int id = 0; id < size; id++)
                archive.entries[id].stream.Flip();

            //Return the archive
            return archive;
        }

        public virtual JagStream Encode() {
            JagStream stream = new JagStream();

            try {
                //Add the data for each entry
                for(int id = 0; id < entries.Length; id++) {
                    entries[id].stream.WriteTo(stream);
                    entries[id].stream.Seek0(); //Make sure to return stream to origin
                }

                //Write the chunk lengths
                int prev = 0;
                for(int id = 0; id < entries.Length; id++) {
                    /* 
                     * Since each file is stored in the only chunk,
                     * just write the delta-encoded file size
                     */
                    long chunkSize = entries[id].stream.Length;
                    stream.WriteInteger(chunkSize - prev);
                    prev = (int) chunkSize;
                }

                //We only used one chunk due to a limitation of the implementation
                stream.WriteByte(1);

                return stream.Flip();
            } finally {
                stream.Close();
            }
        }

        public void UpdateEntry(Entry entry, int id) {
            entries[id] = entry;
        }


        /// <summary>
        /// Returns the file at the specified index id
        /// </summary>
        /// <param name="id">The file entry eindex</param>
        /// <returns></returns>
        public virtual Entry GetEntry(int id) {
            return entries[id];
        }

        internal void UpdateEntry(int entryId, Entry entry) {
            DebugUtil.Debug("Updating archive entry " + entryId);
            entries[entryId] = entry;
        }
    }
}