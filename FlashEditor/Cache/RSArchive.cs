using FlashEditor.Collections;
using FlashEditor.utils;
using System;
using System.Collections.Generic;
using static FlashEditor.utils.DebugUtil;

namespace FlashEditor.cache
{
    public class RSArchive
    {
        public SortedDictionary<int, JagStream> entries = new SortedDictionary<int, JagStream>();
        public int chunks = 1;

        /// <summary>
        /// Create a new Archive with <paramref name="size"/> entries
        /// </summary>
        /// <param name="size">The number of entries</param>
        public RSArchive()
        {
        }

        /// <summary>
        /// Constructs an Archive from an RSContainer stream
        /// </summary>
        /// <param name="stream">The stream containing the archive data</param>
        /// <param name="size">The total number of file entries</param>
        /// <returns></returns>
        public static RSArchive Decode(JagStream stream, int size)
        {
            //Allocate a new archive object
            RSArchive archive = new RSArchive();

            //Read the number of chunks at the end of the archive
            stream.Seek(stream.Length - 1);
            archive.chunks = stream.ReadByte();

            Debug("Chunk count: " + archive.chunks, LOG_DETAIL.INSANE);

            //Single‑file archives omit the size table entirely.
            if (size == 1)
            {
                stream.Seek0();

                byte[] allData = stream.ReadBytes(stream.Length);

                JagStream data = new JagStream();
                data.Write(allData);
                data.Flip();

                archive.entries[0] = data;
                return archive;
            }

            //Read the sizes of the child entries and individual chunks
            int[][] chunkSizes = ArrayUtil.ReturnRectangularArray<int>(archive.chunks, size);
            int[] entrySizes = new int[size];

            Debug("Entry count: " + size, LOG_DETAIL.INSANE);

            stream.Seek(stream.Length - 1 - archive.chunks * size * 4);

            //Read the chunks
            for (int chunk = 0; chunk < archive.chunks; chunk++)
            {
                Debug("chunk size: " + size, LOG_DETAIL.INSANE);
                int cumulativeChunkSize = 0;
                for (int id = 0; id < size; id++)
                {
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
            for (int id = 0; id < size; id++)
                archive.entries[id] = new JagStream(/*entrySizes[id]*/);

            //Return the stream to 0 otherwise this shit doesn't work
            stream.Seek0();

            //--- allocate a single reusable heap buffer up-front
            byte[] smallBuffer = new byte[4096];

            //Read the data into the buffers
            for (int chunk = 0; chunk < archive.chunks; chunk++)
            {
                for (int id = 0; id < size; id++)
                {
                    int chunkSize = chunkSizes[chunk][id];

                    Span<byte> temp = chunkSize <= 4096
                        ? smallBuffer.AsSpan(0, chunkSize)         // reuse stack-safe buffer
                        : new byte[chunkSize];                     // allocate ONLY when > 4 KB

                    stream.Read(temp);
                    archive.entries[id].Write(temp);
                }
            }

            //Flip all of the buffers
            for (int id = 0; id < size; id++)
                archive.entries[id].Flip();

            //Return the archive
            return archive;
        }

        /// <summary>
        /// Serialises this <see cref="RSArchive"/> into the exact binary
        /// format consumed by <see cref="Decode"/>.
        /// </summary>
        /// <remarks>
        /// <para>
        /// <b>Write–order.</b>  Like the original Jagex client, we write the
        /// <i>chunk payloads first</i> and the <i>size-table</i> afterwards.  
        /// <see cref="Decode"/> therefore seeks to
        /// <c>stream.Length - 1 - (chunks ✕ fileCount ✕ 4)</c>, reads the size
        /// table, then rewinds to pull out each file unchanged.
        /// </para>
        /// <para>
        /// <b>Single-vs-multi-file archives.</b><br/>
        /// ─ If the archive holds only <c>1</c> file the spec omits the size
        ///   table entirely; only the final chunk-count byte (<c>1</c>) is
        ///   written.<br/>
        /// ─ For <c>&gt;1</c> files we output one <c>int32</c> per file and per
        ///   chunk.  Because this implementation always stores exactly one
        ///   chunk, the delta we emit for file <i>i</i> is simply
        ///   <c>len<i>i</i> - len<i>i-1</i></c> (the convention expected by
        ///   <see cref="Decode"/>).  See lines around
        ///   <c>cumulativeChunkSize += delta;</c> in the decoder for the
        ///   corresponding read-side logic. :contentReference[oaicite:0]{index=0}
        /// </para>
        /// <para>
        /// <b>Future multi-chunk support.</b>  
        /// When <c>chunks &gt; 1</c> you must split each file into <c>chunks</c>
        /// equal parts, write them in
        /// <c>chunk0 file0 … fileN, chunk1 file0 …</c> order, and change the
        /// delta calculation to be “this chunk’s size minus the previous chunk’s
        /// size of the <i>same</i> file”.
        /// </para>
        /// </remarks>
        /// <returns>
        /// A flipped <see cref="JagStream"/> positioned at the start of the
        /// freshly encoded archive.
        /// </returns>
        public virtual JagStream Encode()
        {
            var stream = new JagStream();

            //------------------------------------------------------------------
            // 1)  Write raw payloads – one contiguous block per file
            //------------------------------------------------------------------
            foreach (var kvp in entries)
            {
                kvp.Value.Seek0();          // defensive rewind
                kvp.Value.WriteTo(stream);  // copy verbatim
            }

            //------------------------------------------------------------------
            // 2)  Write the delta-encoded size table (multi-file only)
            //------------------------------------------------------------------
            int fileCount = entries.Count;
            if (fileCount > 1)
            {
                for (int chunk = 0; chunk < chunks; ++chunk)            // always 1 today
                {
                    int prev = 0;
                    foreach (var kvp in entries)                        // sorted by key
                    {
                        int chunkSize = (int)kvp.Value.Length;          // full len (1-chunk)
                        stream.WriteInteger(chunkSize - prev);          // Δ vs previous file
                        prev = chunkSize;
                    }
                }
            }

            //------------------------------------------------------------------
            // 3)  Final byte – number of chunks
            //------------------------------------------------------------------
            stream.WriteByte((byte)chunks);   // spec = 1, keeps decoder happy

            return stream.Flip();             // ready for reading
        }


        /// <summary>
        /// Returns the file at the specified index id
        /// </summary>
        /// <param name="id">The file entry eindex</param>
        /// <returns></returns>
        public virtual JagStream GetEntry(int id)
        {
            return entries[id];
        }

        public int EntryCount()
        {
            return entries.Count;
        }

        public void PutEntry(int entryId, JagStream entry)
        {
            if (entries.ContainsKey(entryId))
            {
                //Update the entry
                entries[entryId] = entry;
                Debug("Updated archive entry " + entryId + ", len: " + entry.Length, LOG_DETAIL.ADVANCED);
            }
            else
            {
                //Add a new entry to the archive, expanding it
                entries.Add(entryId, entry);
                Debug("Added new entry " + entryId + ", len: " + entry.Length + ", total: " + entries.Count, LOG_DETAIL.INSANE);
            }
        }
    }
}