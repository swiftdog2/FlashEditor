using FlashEditor.Collections;
using FlashEditor.utils;
using System;

namespace FlashEditor.cache {
    class RSArchive {
        private RSEntry[] entries;
        public int chunks = 1;

        /// <summary>
        /// Create a new Archive with <paramref name="size"/> entries
        /// </summary>
        /// <param name="size">The number of entries</param>
        public RSArchive(int size) {
            entries = new RSEntry[size];
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

            //Read the number of chunks at the end of the archive
            stream.Seek(stream.Length - 1);
            archive.chunks = stream.ReadUnsignedByte();

            //Read the sizes of the child entries and individual chunks
            int[,] chunkSizes = new int[archive.chunks, size];
            int[] entrySizes = new int[size];

            stream.Seek(stream.Length - 1 - archive.chunks * size * 4);

            for(int chunk = 0; chunk < archive.chunks; chunk++) {
                int chunkSize = 0;
                for(int id = 0; id < size; id++) {
                    //Read the delta-encoded chunk length
                    int delta = stream.ReadInt();
                    chunkSize += delta;

                    //Store the size of this chunk
                    chunkSizes[chunk, id] = chunkSize;

                    //And add it to the size of the whole file
                    entrySizes[id] += chunkSize;
                }
            }

            //Allocate the buffers for the child entries
            for(int id = 0; id < size; id++)
                archive.entries[id] = new RSEntry(entrySizes[id]);

            //Return the stream to 0 otherwise this shit doesn't work
            stream.Seek0();

            //Read the data into the buffers
            for(int chunk = 0; chunk < archive.chunks; chunk++) {
                for(int id = 0; id < size; id++) {
                    //Get the length of this chunk
                    int chunkSize = chunkSizes[chunk, id];

                    //Copy this chunk into a temporary buffer
                    byte[] temp = new byte[chunkSize];
                    stream.Read(temp, 0, temp.Length);

                    //Copy the temporary buffer into the file buffer
                    archive.entries[id].stream.Write(temp, 0, temp.Length);
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

            /*
             * Reason why this looks backwards is because in the Decode() we have this line:
             *             stream.Seek(stream.Length - 1 - archive.chunks * size * 4);
             * Here, we are writing out the chunk data before the sizes so it all works out
             */

            //Write the chunk data for each entry stream
            for(int id = 0; id < entries.Length; id++) {
                JagStream chunkData = entries[id].stream;
                chunkData.WriteTo(stream);
            }

            //Write the chunk lengths
            int prev = 0;
            for(int chunk = 0; chunk < chunks; chunk++) {
                for(int id = 0; id < entries.Length; id++) {
                    //Archive is broken into chunks, which is the entry stream data
                    long chunkSize = entries[id].stream.Length; //Therefore chunk size is entry stream length
                    stream.WriteInteger(chunkSize - prev); //So delta is the difference between the chunk sizes
                    prev = (int) chunkSize; //Store the size of the last entry
                }
            }

            //Write out the number of chunks that the archive is split up into
            //We only used one chunk due to a limitation of the implementation
            stream.WriteByte(1);

            return stream.Flip();
        }

        public void UpdateEntry(RSEntry entry, int id) {
            entries[id] = entry;
        }


        /// <summary>
        /// Returns the file at the specified index id
        /// </summary>
        /// <param name="id">The file entry eindex</param>
        /// <returns></returns>
        public virtual RSEntry GetEntry(int id) {
            return entries[id];
        }

        internal void UpdateEntry(int entryId, RSEntry entry) {
            DebugUtil.Debug("Updating archive entry " + entryId);
            entries[entryId] = entry;
        }
    }
}