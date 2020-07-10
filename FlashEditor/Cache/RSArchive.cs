using FlashEditor.Collections;

namespace FlashEditor.cache {
    class RSArchive {
        private readonly JagStream[] entries;

        /// <summary>
        /// Create a new Archive with <paramref name="size"/> entries
        /// </summary>
        /// <param name="size">The number of entries</param>
        public RSArchive(int size) {
            entries = new JagStream[size];
        }

        /// <summary>
        /// Constructs an Archive from a stream
        /// </summary>
        /// <param name="stream">The stream containing the archive data</param>
        /// <param name="size">The total number of file entries</param>
        /// <returns></returns>
        public static RSArchive Decode(JagStream stream, int size) {
            //Allocate a new archive object
            RSArchive archive = new RSArchive(size);

            stream.Seek0();
            byte[] data = new byte[stream.Length];
            stream.Read(data, 0, data.Length);

            //Read the number of chunks at the end of the archive
            stream.Seek(stream.Length - 1);
            int chunks = stream.ReadUnsignedByte();

            //Read the sizes of the child entries and individual chunks
            int[][] chunkSizes = ArrayUtil.ReturnRectangularArray<int>(chunks, size);
            int[] sizes = new int[size];

            long ptr = stream.Length - 1 - chunks * size * 4;
            stream.Seek(ptr);

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
                if(sizes[id] <= 0)
                    archive.entries[id] = new JagStream();
                else
                    archive.entries[id] = new JagStream(sizes[id]);

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
            JagStream os = new JagStream();

            try {
                //Add the data for each entry
                for(int id = 0; id < entries.Length; id++) {
                    entries[id].Seek0();
                    entries[id].WriteTo(os);
                    entries[id].Seek0();
                }

                //Write the chunk lengths
                int prev = 0;
                for(int id = 0; id < entries.Length; id++) {
                    /* 
                     * Since each file is stored in the only chunk,
                     * just write the delta-encoded file size
                     */
                    long chunkSize = entries[id].Length;
                    os.WriteInteger(chunkSize - prev);
                    prev = (int) chunkSize;
                }

                //We only used one chunk due to a limitation of the implementation
                os.WriteByte(1);

                return os.Flip();
            } finally {
                os.Close();
            }
        }

        /// <summary>
        /// Returns the file at the specified index id
        /// </summary>
        /// <param name="id">The file entry eindex</param>
        /// <returns></returns>
        public virtual JagStream GetEntry(int id) {
            return entries[id];
        }
    }
}