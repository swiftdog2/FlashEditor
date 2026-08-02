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
        ///     How many bytes of each file live in each chunk, as <c>[chunk][file]</c> over the
        ///     files in ascending id order. Null for an archive that was not decoded from a
        ///     multi-chunk payload.
        /// </summary>
        /// <remarks>
        ///     A multi-chunk archive is stored chunk-major - every file's first slice, then every
        ///     file's second slice, and so on - so the split is part of the byte layout and cannot
        ///     be recovered from the reassembled files. Keeping it is what lets
        ///     <see cref="Encode"/> reproduce the payload it was decoded from; without it the
        ///     encoder lays the files out file-major and silently reorders the archive.
        ///     <para>
        ///     The file dimension is positional. <see cref="Decode"/> fills it in the order of
        ///     the <c>fileIds</c> it was handed, while <see cref="Encode"/> reads it in the sorted
        ///     order of <see cref="files"/>. Those agree because a reference table always yields
        ///     ascending file ids; if a caller ever passed unsorted ids the columns would refer
        ///     to different files, which <see cref="ChunkSizesMatchFiles"/> catches through the
        ///     per-file totals and degrades to a single chunk rather than writing a scrambled
        ///     archive.
        ///     </para>
        /// </remarks>
        private int[][] chunkSizes;

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

            //Remember the chunk split so Encode can put the payload back the way it was found
            archive.chunkSizes = chunkSizes;

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
        ///   chunk, followed by the chunk-count byte. The sizes are delta-encoded
        ///   <i>across the files within a chunk</i>, and the running total restarts
        ///   on each chunk - see the <c>cumulativeChunkSize</c> reset in the
        ///   decoder for the corresponding read-side logic.
        /// </para>
        /// <para>
        /// <b>Multi-chunk archives.</b>
        /// Chunks are <i>not</i> equal parts, and the payload is stored chunk-major:
        /// <c>chunk0 file0 … fileN, chunk1 file0 …</c>. The split is therefore part
        /// of the byte layout and cannot be recovered from the reassembled files,
        /// so <see cref="Decode"/> retains it and this method reproduces it. Most
        /// multi-file archives in a real 639 cache use three chunks; writing the
        /// files end to end instead yields a payload of exactly the same length
        /// with the bytes in the wrong order. An archive whose files have been
        /// edited no longer fits the split it was decoded with, so
        /// <see cref="PutFile"/> drops it and the archive is written as a single
        /// chunk - a shape the client reads for any archive.
        /// </para>
        /// </remarks>
        /// <returns>
        /// A flipped <see cref="JagStream"/> positioned at the start of the
        /// freshly encoded archive.
        /// </returns>
        public virtual JagStream Encode()
        {
            var stream = new JagStream();

            int fileCount = files.Count;
            bool multiChunk = fileCount > 1 && chunks > 1 && ChunkSizesMatchFiles();

            //------------------------------------------------------------------
            // 1)  Write raw payloads
            //------------------------------------------------------------------
            if (multiChunk)
            {
                //Chunk-major, exactly as Decode reads it back: chunk 0 of every file, then
                //chunk 1 of every file, and so on.
                byte[][] payloads = new byte[fileCount][];
                int position = 0;
                foreach (var kvp in files)                              // sorted by key
                    payloads[position++] = kvp.Value.ToArray();

                int[] offsets = new int[fileCount];
                for (int chunk = 0; chunk < chunks; chunk++)
                {
                    for (int index = 0; index < fileCount; index++)
                    {
                        int length = chunkSizes[chunk][index];
                        stream.Write(payloads[index], offsets[index], length);
                        offsets[index] += length;
                    }
                }
            }
            else
            {
                //One contiguous block per file, which is the single-chunk layout
                foreach (var kvp in files)
                {
                    kvp.Value.Seek0();          // defensive rewind
                    kvp.Value.WriteTo(stream);  // copy verbatim
                }
            }

            //------------------------------------------------------------------
            // 2)  Trailer - size table and chunk count (multi-file only)
            //------------------------------------------------------------------
            // A single-file archive has no trailer at all. Decode mirrors the client
            // and takes the entire payload as the file, so appending a chunk-count
            // byte here would hand that byte back as file data and grow the file by
            // one byte on every save cycle.
            if (fileCount > 1)
            {
                //Sizes are delta-encoded across the files *within* a chunk, and the running
                //total restarts on each chunk - see the cumulativeChunkSize reset in Decode.
                int chunkCount = multiChunk ? chunks : 1;
                for (int chunk = 0; chunk < chunkCount; ++chunk)
                {
                    int prev = 0;
                    int index = 0;
                    foreach (var kvp in files)                        // sorted by key
                    {
                        int chunkSize = multiChunk ? chunkSizes[chunk][index] : (int)kvp.Value.Length;
                        stream.WriteInteger(chunkSize - prev);          // Δ vs previous file
                        prev = chunkSize;
                        index++;
                    }
                }

                stream.WriteByte((byte)chunkCount);
            }

            return stream.Flip();             // ready for reading
        }

        /// <summary>
        ///     Whether the retained chunk split still describes the files currently held.
        /// </summary>
        /// <remarks>
        ///     Editing a file changes its length, at which point the split it was decoded with no
        ///     longer adds up. <see cref="PutFile"/> drops the split for that reason; this is the
        ///     belt-and-braces check that the shape matches before it is trusted.
        /// </remarks>
        private bool ChunkSizesMatchFiles()
        {
            if (chunkSizes == null || chunkSizes.Length != chunks)
                return false;

            foreach (int[] perFile in chunkSizes)
                if (perFile == null || perFile.Length != files.Count)
                    return false;

            int index = 0;
            foreach (var kvp in files)
            {
                long total = 0;
                for (int chunk = 0; chunk < chunks; chunk++)
                    total += chunkSizes[chunk][index];
                if (total != kvp.Value.Length)
                    return false;
                index++;
            }

            return true;
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

        /// <summary>
        ///     Adds or replaces a file, abandoning any retained multi-chunk split that the new
        ///     contents no longer fit.
        /// </summary>
        /// <remarks>
        ///     The split is a per-file byte budget: it says how many of file <i>n</i>'s bytes live
        ///     in each chunk. Replacing a file with one of the same length therefore leaves it
        ///     describing the archive exactly as well as it did before - the new bytes are simply
        ///     sliced by the same lengths - so it is kept. It stops describing anything the moment
        ///     a length changes or a file is added, and is dropped then; the archive is written
        ///     back as a single chunk instead, which holds the same files and is what the client
        ///     reads for any archive whose trailer says one chunk.
        ///     <para>
        ///     Keeping it for a same-length replacement is what makes a no-op save byte-neutral.
        ///     Most multi-file archives in a real 639 cache are stored across three chunks, and
        ///     dropping the split unconditionally re-laid every one of them out as a single chunk
        ///     - a payload of exactly the same length with the bytes in a different order, which
        ///     is a rewritten archive and a changed CRC for a save that changed nothing.
        ///     </para>
        /// </remarks>
        /// <param name="fileId">The file id to write.</param>
        /// <param name="data">The file payload.</param>
        public void PutFile(int fileId, JagStream data)
        {
            bool sameShape = data != null
                && files.TryGetValue(fileId, out JagStream previous)
                && previous != null
                && previous.Length == data.Length;

            if (!sameShape)
            {
                chunkSizes = null;
                chunks = 1;
            }

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
