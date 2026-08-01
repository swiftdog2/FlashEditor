using FlashEditor.Collections;
using FlashEditor.Utils;
using System;
using System.Collections.Generic;
using System.Linq;
using static FlashEditor.Utils.DebugUtil;

namespace FlashEditor.cache
{
    public class RSArchive
    {
        public SortedDictionary<int, JagStream> files = new SortedDictionary<int, JagStream>();
        public int chunks = 1;

        /// <summary>
        /// Create a new Archive
        /// </summary>
        public RSArchive()
        {
        }

        /// <summary>
        /// Constructs an Archive from an RSContainer stream
        /// </summary>
        /// <param name="stream">The stream containing the archive data</param>
        /// <param name="fileIds">The actual file IDs contained in the archive</param>
        /// <returns></returns>
        public static RSArchive Decode(JagStream stream, int[] fileIds)
        {
            int size = fileIds.Length;

            //Allocate a new archive object
            RSArchive archive = new RSArchive();

            //Single-file archives omit the trailer entirely - no size table and no
            //chunk-count byte. The whole payload is the file, so the last byte is data
            //and must not be read as a chunk count: doing so leaves a bogus count behind
            //that corrupts the size table if a second file is later added.
            if (size == 1)
            {
                archive.chunks = 1;
                Debug($"Chunk count: {archive.chunks}, size {size}", LOG_DETAIL.INSANE);

                stream.Seek0();
                byte[] allData = stream.ReadBytes(stream.Length);

                JagStream data = new JagStream();
                data.Write(allData);
                data.Flip();

                archive.files[fileIds[0]] = data;

                return archive;
            }

            //Multi-file archives end with the chunk count, followed backwards by the size table
            stream.Seek(stream.Length - 1);
            archive.chunks = stream.ReadByte();

            Debug($"Chunk count: {archive.chunks}, size {size}", LOG_DETAIL.INSANE);

            //Read the sizes of the file entries and individual chunks
            int[][] chunkSizes = ArrayUtil.ReturnRectangularArray<int>(archive.chunks, size);
            int[] fileSizes = new int[size];

            Debug("File count: " + size, LOG_DETAIL.INSANE);

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
                    fileSizes[id] += cumulativeChunkSize;
                    Debug("\t- File " + id + " size: " + cumulativeChunkSize, LOG_DETAIL.INSANE);
                }
            }

            //Allocate the buffers for the file entries, keyed by actual file ID
            for (int id = 0; id < size; id++)
                archive.files[fileIds[id]] = new JagStream();

            // Reset the stream to the start before reading
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
                    archive.files[fileIds[id]].Write(temp);
                }
            }

            //Flip all of the buffers
            for (int id = 0; id < size; id++)
                archive.files[fileIds[id]].Flip();

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
        /// <i>chunk payloads first</i> and the <i>trailer</i> afterwards.
        /// <see cref="Decode"/> therefore seeks to
        /// <c>stream.Length - 1 - (chunks ✕ fileCount ✕ 4)</c>, reads the size
        /// table, then rewinds to pull out each file unchanged.
        /// </para>
        /// <para>
        /// <b>Single-vs-multi-file archives.</b><br/>
        /// ─ If the archive holds only <c>1</c> file there is <i>no trailer at
        ///   all</i> - no size table and no chunk-count byte. The client's
        ///   unpacker special-cases a file count of <c>1</c> and takes the whole
        ///   payload verbatim, as <see cref="Decode"/> does, so writing a
        ///   chunk-count byte here would hand it straight back as file data and
        ///   grow the file by one byte on every save cycle.<br/>
        /// ─ For <c>&gt;1</c> files we output one <c>int32</c> per file and per
        ///   chunk, followed by the chunk-count byte.  Because this
        ///   implementation always stores exactly one chunk, the delta we emit
        ///   for file <i>i</i> is simply <c>len<i>i</i> - len<i>i-1</i></c> (the
        ///   convention expected by <see cref="Decode"/>).  See lines around
        ///   <c>cumulativeChunkSize += delta;</c> in the decoder for the
        ///   corresponding read-side logic.
        /// </para>
        /// <para>
        /// <b>Future multi-chunk support.</b>
        /// When <c>chunks &gt; 1</c> you must split each file into <c>chunks</c>
        /// equal parts, write them in
        /// <c>chunk0 file0 … fileN, chunk1 file0 …</c> order, and change the
        /// delta calculation to be "this chunk's size minus the previous chunk's
        /// size of the <i>same</i> file".
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
            foreach (var kvp in files)
            {
                kvp.Value.Seek0();          // defensive rewind
                kvp.Value.WriteTo(stream);  // copy verbatim
            }

            //------------------------------------------------------------------
            // 2)  Trailer - size table and chunk count (multi-file only)
            //------------------------------------------------------------------
            // A single-file archive has no trailer at all. Decode mirrors the client
            // and takes the entire payload as the file, so appending a chunk-count
            // byte here would hand that byte back as file data and grow the file by
            // one byte on every save cycle.
            int fileCount = files.Count;
            if (fileCount > 1)
            {
                for (int chunk = 0; chunk < chunks; ++chunk)            // always 1 today
                {
                    int prev = 0;
                    foreach (var kvp in files)                        // sorted by key
                    {
                        int chunkSize = (int)kvp.Value.Length;          // full len (1-chunk)
                        stream.WriteInteger(chunkSize - prev);          // Δ vs previous file
                        prev = chunkSize;
                    }
                }

                stream.WriteByte((byte)chunks);   // spec = 1, keeps decoder happy
            }

            return stream.Flip();             // ready for reading
        }


        /// <summary>
        /// Returns the file at the specified file id
        /// </summary>
        /// <param name="fileId">The file id</param>
        /// <returns></returns>
        public virtual JagStream GetFile(int fileId)
        {
            return files[fileId];
        }

        public int FileCount()
        {
            return files.Count;
        }

        /// <summary>
        /// Whether the archive holds a file under <paramref name="fileId"/>.
        /// </summary>
        /// <remarks>
        /// <see cref="GetFile"/> indexes the backing dictionary directly and throws for an
        /// absent id, so callers walking a candidate id range must test with this first.
        /// </remarks>
        /// <param name="fileId">The file id to test</param>
        /// <returns><c>true</c> if the file is present</returns>
        public bool HasFile(int fileId)
        {
            return files.ContainsKey(fileId);
        }

        /// <summary>
        /// Returns the file ids the archive actually holds, in ascending order.
        /// </summary>
        /// <remarks>
        /// A snapshot, so callers can add files while iterating it. File ids are sparse in
        /// real caches, so this is not interchangeable with <c>0..FileCount()</c>.
        /// </remarks>
        /// <returns>The file ids present in the archive</returns>
        public int[] GetFileIds()
        {
            return files.Keys.ToArray();
        }

        public void PutFile(int fileId, JagStream data)
        {
            if (files.ContainsKey(fileId))
            {
                //Update the file
                files[fileId] = data;
                Debug("Updated archive file " + fileId + ", len: " + data.Length, LOG_DETAIL.ADVANCED);
            }
            else
            {
                //Add a new file to the archive, expanding it
                files.Add(fileId, data);
                Debug("Added new file " + fileId + ", len: " + data.Length + ", total: " + files.Count, LOG_DETAIL.INSANE);
            }
        }
    }
}
