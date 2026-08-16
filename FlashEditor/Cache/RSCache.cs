using FlashEditor.Definitions.Sprites;
using FlashEditor.Cache.Util;
using FlashEditor.Cache.Util.Crypto;
using FlashEditor.Definitions;
using FlashEditor.Definitions.Tracks;
using FlashEditor.Utils;
using ICSharpCode.SharpZipLib.Checksum;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using static FlashEditor.Utils.DebugUtil;
using FlashEditor.IO;
using FlashEditor.Definitions.Config;
using FlashEditor.Definitions.Entities;
using FlashEditor.Definitions.Models;

namespace FlashEditor.Cache {
    public class RSCache {
        /// <summary>
        ///     Serialises every read and write that goes through this cache.
        /// </summary>
        /// <remarks>
        ///     Not merely a guard over <see cref="containers"/>. A container hands out one shared
        ///     <c>JagStream</c> with a single read position and is released with
        ///     <c>ReleaseData</c> the moment a caller has taken its file, so two threads inside
        ///     <see cref="ReadFile"/> or <see cref="GetSprite"/> on the same container corrupt each
        ///     other's decode and one can null the stream the other is mid-way through.
        ///
        ///     There are now three concurrent callers by design - the map render thread, the
        ///     texture worker's <c>Parallel.ForEach</c>, and the UI thread - so the whole read is
        ///     taken under this rather than just the container lookup. It is also held across
        ///     <see cref="WriteCache"/>, because <c>RSFileStore.SaveTo</c> unmaps the dat2, moves
        ///     it and remaps it: a decode running through that window reads a closed accessor.
        ///
        ///     A Monitor, so the nesting (<c>ReadFile</c> into <c>GetReferenceTable</c> into
        ///     <c>GetContainer</c>) is reentrant. Nothing taken inside it ever waits on another
        ///     lock, so it cannot take part in a cycle.
        /// </remarks>
        private readonly object _containerLock = new object();

        /// <summary>
        /// The backing file store providing index and data channel access.
        /// </summary>
        public RSFileStore store;
        /// <summary>
        /// Cached reference tables, one per index.
        /// </summary>
        public RSReferenceTable[] referenceTables;

        /// <summary>
        /// Decoded containers keyed by [index id][archive id].
        /// Each index has their own set of containers.
        /// </summary>
        public SortedDictionary<int, SortedDictionary<int, RSContainer>> containers = new SortedDictionary<int, SortedDictionary<int, RSContainer>>();

        /// <summary>
        /// Cached item definitions keyed by item id.
        /// </summary>
        public SortedDictionary<int, ItemDefinition> items = new SortedDictionary<int, ItemDefinition>();
        /// <summary>
        /// Cached object (loc) definitions keyed by object id.
        /// </summary>
        public SortedDictionary<int, ObjectDefinition> objects = new SortedDictionary<int, ObjectDefinition>();
        /// <summary>
        /// Cached NPC definitions keyed by NPC id.
        /// </summary>
        public SortedDictionary<int, NPCDefinition> npcs = new SortedDictionary<int, NPCDefinition>();

        /// <summary>
        /// Cached model definitions keyed by model id.
        /// </summary>
        public SortedDictionary<int, ModelDefinition> models = new SortedDictionary<int, ModelDefinition>();

        /// <summary>
        /// XTEA key table for decrypting encrypted archives (e.g. map data).
        /// </summary>
        private XTEAKeyTable xteaKeys;

        /// <summary>Indexes whose reference table a batch still has to write.</summary>
        private readonly HashSet<int> pendingTableWrites = new HashSet<int>();

        /// <summary>Nesting depth of <see cref="BeginBatch"/> scopes.</summary>
        private int batchDepth;

        /// <summary>
        /// Create a new Cache instance, and automatically memoizes the archives and their reference tables
        /// </summary>
        /// <param name="store">The filestore</param>
        public RSCache(RSFileStore store) {
            this.store = store;
            LoadReferenceTables();
        }

        /// <summary>
        ///     Writes the staged cache to <paramref name="cacheDir"/>. The dat2 and every index
        ///     file are committed together, so the saved cache is never half-updated. Until this
        ///     runs, no edit has touched the disk at all.
        /// </summary>
        /// <param name="cacheDir">Destination directory, which may be the one the cache was opened from.</param>
        internal void WriteCache(string cacheDir) {
            Debug("Writing cache to " + cacheDir);

            //Under the same lock every read takes. SaveTo closes the memory map, moves the dat2
            //and every index file over the originals, then remaps - and the map render thread
            //runs for the whole life of the Map tab, so a decode overlapping that window is the
            //normal case rather than a rare one.
            lock (_containerLock)
                store.SaveTo(cacheDir);
        }

        /// <summary>Whether any edit is staged and not yet saved.</summary>
        internal bool HasUnsavedChanges => store != null && store.IsDirty;

        /// <summary>
        /// Writes a file contained in an archive to the cache.
        /// </summary>
        /// <remarks>
        ///     A write whose payload turns out to be identical to the one already stored is
        ///     dropped entirely - see the unchanged path below - so opening an archive and saving
        ///     it without editing anything leaves the dat2 and the reference table untouched.
        /// </remarks>
        /// <param name="indexId">The index id</param>
        /// <param name="archiveId">The archive within the index</param>
        /// <param name="fileId">The file id within the archive</param>
        /// <param name="data">The encoded file data</param>
        public void WriteFile(int indexId, int archiveId, int fileId, JagStream data) {
            //Serialised against every read: this mutates the container, its archive and the
            //reference table entry in place, and a background decode holding the same container
            //would otherwise see it change underneath itself.
            lock (_containerLock)
                WriteFileLocked(indexId, archiveId, fileId, data);
        }

        private void WriteFileLocked(int indexId, int archiveId, int fileId, JagStream data) {
            if (indexId == RSConstants.META_INDEX)
                throw new IOException("Reference tables can only be modified with the low level FileStore API!");

            Debug("Writing File " + indexId + "," + archiveId + "," + fileId);

            //Get the reference table for the index
            RSReferenceTable table = GetReferenceTable(indexId);
            RSArchiveEntry archiveEntry;

            /* Whether the table already describes this archive at all. An archive it has never
               heard of has to be written even when its payload matches the bytes on disk,
               because the entry announcing it is the thing that is missing. */
            bool entryExisted = table.archiveEntries.ContainsKey(archiveId);

            //Retrieve the appropriate archive entry in the reference table
            if (entryExisted) {
                archiveEntry = table.GetArchiveEntry(archiveId);
                Debug("Found archive entry for RefTable(index " + indexId + ", archive " + archiveId + ", file " + fileId + ")", LOG_DETAIL.INSANE);
            }
            else {
                //Expand the reference table to add a new archive entry, if necessary
                archiveEntry = new RSArchiveEntry(archiveId);
                Debug("Generating archive entry for RefTable(" + indexId + ", " + archiveId + ", file " + fileId, LOG_DETAIL.INSANE);
            }

            //The file ids describing the payload as it currently sits on disk, captured
            //before the edit adds its own. RSArchive.Decode is driven by this list, so it
            //has to describe what was encoded, not what is about to be.
            int[] existingFileIds = archiveEntry.GetFileEntries().Keys.ToArray();

            RSContainer container = GetContainer(indexId, archiveId);

            //Generate a new container, if necessary
            if (container == null) {
                Debug("Added new container", LOG_DETAIL.INSANE);
                container = new RSContainer(indexId, archiveId, RSConstants.GZIP_COMPRESSION, null, 1337);
            }

            /* The payload the stored bytes currently encode, borrowed rather than copied: the
               archive encodes into a fresh stream, so this one is not disturbed and nothing has
               to be retained beyond the call. Null when the container was built here or has
               since been re-encoded, in which case there is no baseline to compare against and
               the write proceeds unconditionally. */
            JagStream storedPayload = container.PayloadIsAsStored && container.HasData
                ? container.GetStream()
                : null;

            RSArchive archive = container.GetArchive();
            if (archive == null) {
                if (container.HasData && existingFileIds.Length > 0) {
                    //Rehydrate from the container payload rather than starting blank.
                    //ReadFile releases the decoded archive as soon as it has handed the
                    //caller its file, so by edit time this is routinely null - and editing
                    //from an empty archive drops every other file in the group.
                    Debug("Rehydrated archive from container", LOG_DETAIL.INSANE);
                    archive = RSArchive.Decode(container.GetStream(), existingFileIds);
                }
                else {
                    Debug("Added new archive", LOG_DETAIL.INSANE);
                    archive = new RSArchive();
                }
                container.SetArchive(archive);
            }

            //Create or update the file in the archive
            archive.PutFile(fileId, data);

            /* Reconcile the archive against its reference table entry over ACTUAL file ids.
               Decode reads the archive through the entry's id list, so the two sets have to
               match exactly or the size table is read against the wrong number of files. The
               loop this replaces walked an ordinal counter from 0 to FileCount() and indexed
               the archive directly, so a sparse archive threw KeyNotFoundException on the first
               gap, and where it did not throw it re-registered every file under an ordinal id -
               reinstating exactly the renumbering the reference table encoder was fixed to stop.
               Only the archive is padded here; the entry is reconciled the other way once the
               write is known to be going ahead, so that the unchanged path leaves it untouched. */
            foreach (int id in existingFileIds)
                if (!archive.HasFile(id))
                    archive.PutFile(id, new JagStream(0));

            JagStream payload = archive.Encode();

            /* --- the unchanged path ---
               A save that changes nothing must change nothing on disk. The comparison is over
               the PAYLOAD, never the stored container: gzip is not canonical - Jagex deflated
               with Java's Deflater and this project uses SharpZipLib - so re-encoding an
               untouched payload yields different bytes of equal validity, and comparing those
               would never match. An identical payload means the bytes already in the store are
               still a correct encoding of it, so they are kept exactly as they are.

               Nothing is written, which is stronger than rewriting the original bytes: the
               reference table entry keeps the CRC, version and FLAG_SIZES pair it already
               carries, all of which describe those bytes and would otherwise be recomputed over
               a freshly compressed container; the entries of every other archive in the same
               table are spared the rewrite the table container's own re-encode would inflict;
               the sector chain is not reallocated; and an encrypted archive sidesteps
               re-encryption altogether, so it stays encrypted byte for byte whether or not its
               key is even loaded. */
            if (entryExisted && storedPayload != null && SameBytes(payload, storedPayload)) {
                Debug("Unchanged archive " + indexId + "," + archiveId + " - leaving the stored bytes alone");
                return;
            }

            //Add a file entry if one does not exist
            if (archiveEntry.GetFileEntry(fileId) == null) {
                archiveEntry.PutFileEntry(fileId, new RSFileEntry(fileId));
                Debug("Added new file entry " + fileId, LOG_DETAIL.INSANE);
            }

            foreach (int id in archive.GetFileIds())
                if (archiveEntry.GetFileEntry(id) == null)
                    archiveEntry.PutFileEntry(id, new RSFileEntry(id));

            //The valid file id list is the one Decode is handed, so it has to follow the
            //file entries. Left stale, a reloaded container decodes with the wrong ids.
            archiveEntry.SetValidFileIds(archiveEntry.GetFileEntries().Keys.ToArray());

            container.Dirty = true;

            //Wrap the archive back into a container
            container.SetStream(payload);

            /* Written back under the key it was read under, or not at all. An archive that was
               decrypted on read and written back as plaintext looks perfectly healthy from here
               - the CRC and the reference table below are both recomputed over the plaintext, so
               they agree with it - and is destroyed at the client, which deciphers it regardless
               and gets noise. On a format 6 table there is no flag to record the change of state,
               so nothing anywhere reports it. */
            int[] xteaKey = ResolveWriteKey(container, indexId, archiveId);

            //Grab the bytes we need for the checksum. The CRC below covers the STORED bytes, so
            //it has to be taken over this ciphertext rather than over the plaintext payload.
            JagStream stream = container.Encode(xteaKey);

            /* The trailing version short is not part of the checksummed span - but it is only
               present when the container carries a version at all. RSContainer.Decode leaves the
               version at -1 for a container stored without a trailer, and Encode then writes
               none, so subtracting a fixed 2 chops two bytes of real payload off the CRC and off
               the compressed size recorded below. CRC32Helper.ApplyCrcAndVersion already guards
               it this way. */
            int versionBytes = container.GetVersion() != -1 ? 2 : 0;
            JagStream hashableStream = new JagStream(stream.ReadBytes(stream.Length - versionBytes));

            //Update the version and checksum for this file
            hashableStream.Seek0(); //allows the crc32 to slurp the blocks
            var crc = new Crc32();
            crc.Update(hashableStream.ToArray());      // feeds the bytes
            archiveEntry.SetCrc((int) crc.Value);              // .Value is UInt32

            /* Bump the archive version rather than stamping a constant. It is a monotonic counter
               the JS5 update protocol compares against what a client already holds, so a fixed
               value tells every client the same thing regardless of how many times the archive has
               actually changed - and stamping it downward tells them their stale copy is current.
               Incrementing is the only behaviour that keeps the comparison meaningful. */
            archiveEntry.SetVersion(archiveEntry.GetVersion() + 1);

            /* Recompute the FLAG_SIZES pair. These describe the archive as it is now stored, so
               left alone they go stale on the first edit and stay wrong for every later one.
               They are set unconditionally: a table without the flag never encodes them, and an
               entry that reports its real size costs nothing to keep honest.

               - compressed   the stored container without its version trailer, i.e. exactly the
                              span the CRC above is taken over - which is why both read the same
                              stream rather than recomputing the trailer length separately.
               - uncompressed the archive payload before compression - what the container's own
                              header calls the uncompressed length. */
            archiveEntry.compressed = hashableStream.Length;
            archiveEntry.uncompressed = container.GetStream().Length;

            //Calculate and update the whirlpool digest if we need to
            if (table.usesWhirlpool) {
                byte[] digest = Whirlpool.ComputeHash(hashableStream.ToArray());
                archiveEntry.SetWhirlpool(digest);
            }

            /* Where a per-archive encryption flag exists, keep it describing what was just
               written. Only a format 7 table has one - which is exactly why the state has to be
               carried on the container as well, since a format 6 table cannot hold it. */
            if (table.format >= 7)
                archiveEntry.UsesXtea = xteaKey != null;

            //Add the archive entry to the reference table
            table.PutArchiveEntry(archiveId, archiveEntry);

            store.Write(indexId, archiveId, stream);

            /* The reference table is written once per file by default, which is correct but costly:
               index 5's table is a 114KB payload, so saving forty map squares re-encodes and
               rewrites it forty times, each rewrite reallocating its sector chain. Inside a batch
               the write is deferred to the end, where one rewrite covers every file in it. */
            if (batchDepth > 0)
                pendingTableWrites.Add(indexId);
            else
                WriteReferenceTable(indexId, table);

            /* The store now holds an encoding of this exact payload, so it becomes the baseline
               the next save is measured against. Asserted only once both writes have landed: a
               write that threw part way leaves the store describing something else, and claiming
               otherwise would let the following save skip a change that was never stored. */
            container.PayloadIsAsStored = true;
        }

        /// <summary>
        ///     Encodes a reference table and writes it into the meta index.
        /// </summary>
        /// <remarks>
        ///     The table's own container version is bumped alongside the write, for the same reason
        ///     the archive versions are: a client compares it to decide whether its cached copy of
        ///     the table is stale.
        /// </remarks>
        /// <param name="indexId">The index whose table is being written.</param>
        /// <param name="table">The table to write.</param>
        private void WriteReferenceTable(int indexId, RSReferenceTable table) {
            table.version++;

            var container = new RSContainer(RSConstants.META_INDEX, indexId,
                RSConstants.GZIP_COMPRESSION, ReferenceTableCodec.Encode(table), table.version);

            store.Write(RSConstants.META_INDEX, indexId, container.Encode());
        }

        /// <summary>
        ///     Defers reference-table writes until the returned scope is disposed.
        /// </summary>
        /// <remarks>
        ///     Use around a run of <see cref="WriteFile"/> calls. Each index's table is then encoded
        ///     and written once at the end rather than once per file. Nesting is supported; only the
        ///     outermost scope flushes.
        ///
        ///     This does not make the run atomic. A failure part way through leaves the archives
        ///     that were written on disk with a stale table describing them, which is recoverable
        ///     by saving again but is not a transaction.
        /// </remarks>
        /// <returns>A scope that flushes the deferred writes when disposed.</returns>
        public IDisposable BeginBatch() {
            batchDepth++;
            return new BatchScope(this);
        }

        private void EndBatch() {
            if (--batchDepth > 0)
                return;

            foreach (int indexId in pendingTableWrites) {
                RSReferenceTable table = GetReferenceTable(indexId);
                if (table != null)
                    WriteReferenceTable(indexId, table);
            }

            pendingTableWrites.Clear();
        }

        private sealed class BatchScope : IDisposable {
            private RSCache owner;

            public BatchScope(RSCache owner) {
                this.owner = owner;
            }

            public void Dispose() {
                if (owner == null)
                    return;
                RSCache target = owner;
                owner = null;
                target.EndBatch();
            }
        }

        /// <summary>
        ///     Whether two streams hold the same bytes.
        /// </summary>
        /// <remarks>
        ///     Compares the live buffers rather than copying either side out, because one of them
        ///     is a whole archive payload and this runs on every save.
        /// </remarks>
        private static bool SameBytes(JagStream a, JagStream b) {
            return a.Length == b.Length
                && a.GetBuffer().AsSpan(0, a.Length).SequenceEqual(b.GetBuffer().AsSpan(0, b.Length));
        }

        /// <summary>
        /// Reads the container from the index stream, decodes it,
        /// caches it for later and returns the container
        /// </summary>
        /// <param name="indexId">The index id</param>
        /// <param name="archiveId">The archive id</param>
        /// <returns>Container for archive <paramref name="archiveId"/> from the specified index</returns>
        public RSContainer GetContainer(int indexId, int archiveId) {
            if (archiveId < 0 || archiveId >= store.GetFileCount(indexId))
                throw new FileNotFoundException("Could not find container for index " + indexId);

            lock (_containerLock) {
                //Initialise the container dictionary
                if (!containers.ContainsKey(indexId))
                    containers.Add(indexId, new SortedDictionary<int, RSContainer>());

                //Return the container if already cached and still has data
                if (containers[indexId].ContainsKey(archiveId)) {
                    RSContainer cached = containers[indexId][archiveId];
                    if (cached.HasData)
                        return cached;

                    //Re-load evicted container from disk
                    JagStream evictedData = LoadContainer(indexId, archiveId);
                    RSContainer reloaded = DecodeContainer(evictedData, indexId, archiveId);
                    if (reloaded != null) {
                        reloaded.SetIndexId(indexId);
                        reloaded.SetId(archiveId);
                        containers[indexId][archiveId] = reloaded;
                        return reloaded;
                    }

                    //Evicted container could not be reloaded
                    throw new FileNotFoundException("NULL CONTAINER? (index: " + indexId + ", archive: " + archiveId + ")");
                }

                //Read the data from the index
                JagStream data = LoadContainer(indexId, archiveId);

                //Decode the container
                RSContainer container = DecodeContainer(data, indexId, archiveId);

                if (container == null)
                    throw new FileNotFoundException("NULL CONTAINER? (index: " + indexId + ", archive: " + archiveId + ")");

                container.SetIndexId(indexId);
                container.SetId(archiveId);

                //Cache the container for later usage
                containers[indexId][archiveId] = container;

                return container;
            }
        }

        /// <summary>
        /// Replaces or inserts a container in the in-memory cache.
        /// </summary>
        /// <param name="indexId">The index id</param>
        /// <param name="archiveId">The archive id within the index</param>
        /// <param name="container">The container to store</param>
        public void UpdateRSContainer(int indexId, int archiveId, RSContainer container) {
            lock (_containerLock) {
                if (!containers.ContainsKey(indexId))
                    containers.Add(indexId, new SortedDictionary<int, RSContainer>());

                //Return the container if already cached
                if (containers[indexId].ContainsKey(archiveId))
                    containers[indexId][archiveId] = container;
                else
                    containers[indexId].Add(archiveId, container);
            }
        }

        /// <summary>
        /// Replaces the cached reference table for a given index.
        /// </summary>
        /// <param name="indexId">The index whose reference table should be replaced</param>
        /// <param name="refTable">The new reference table</param>
        public void UpdateReferenceTable(int indexId, RSReferenceTable refTable) {
            //Strictly less than the length: the array is addressed by index id, so the last valid
            //id is Length - 1 and the old bound let the very next id through to an array throw
            if (indexId < 0 || indexId >= referenceTables.Length)
                throw new IndexOutOfRangeException("Invalid index when updating reference table cache");

            lock (_containerLock)
                referenceTables[indexId] = refTable;
        }

        /// <summary>
        /// Loads the container data from the RSIndex
        /// </summary>
        /// <param name="indexId">The index id</param>
        /// <param name="archiveId">The archive id</param>
        /// <returns>A <c>JagStream</c> containing the container data</returns>
        internal JagStream LoadContainer(int indexId, int archiveId) {
            Debug("Loading index " + indexId + ", archive " + archiveId, LOG_DETAIL.ADVANCED);
            RSIndex index = store.GetIndexEntry(indexId);

            //Find the beginning of the index
            long pos = (long) archiveId * RSIndex.SIZE;

            if (pos < 0 || pos >= index.GetStream().Length)
                throw new FileNotFoundException("Position is out of bounds for index " + indexId + ", archive " + archiveId);

            //Read the archive header, to get the container size and sector ID
            index.ReadContainerHeader(archiveId);

            //If the sector could not be located in the data stream
            //Sector ids are zero-based, so id == Length/SIZE is already one sector past the
            //end of the data. Accepting it reads a sector that was never written.
            if (index.GetSectorID() <= 0 || index.GetSectorID() >= store.dataChannel.Length / RSSector.SIZE)
                return null;

            //Allocate buffers for the data and sector
            JagStream containerData = new JagStream(index.GetSize());

            int chunk = 0;
            int remaining = index.GetSize();
            int sectorId = index.GetSectorID();

            //Point to the start of the sector
            pos = (long) sectorId * RSSector.SIZE;

            do {
                Debug("\tReading sector " + sectorId + " @ " + pos, LOG_DETAIL.INSANE);

                byte[] sectorData = store.dataChannel.ReadBytes(pos, RSSector.SIZE);

                //Read in the sector from the data channel
                RSSector sector = RSSector.Decode(new JagStream(sectorData));

                if (remaining > RSSector.DATA_LEN) {
                    //Cache this sector so far
                    containerData.Write(sector.GetData(), 0, RSSector.DATA_LEN);

                    //And subtract the sector we read from data remaining
                    remaining -= RSSector.DATA_LEN;

                    //Basically the cache was corrupted
                    if (sector.GetIndexId() != indexId)
                        throw new IOException("File index mismatch, " + sector.GetIndexId() + ", " + indexId);
                    if (sector.GetId() != archiveId)
                        throw new IOException("File id mismatch, " + sector.GetId() + ", " + archiveId);
                    if (sector.GetChunk() != chunk++)
                        throw new IOException("Chunk mismatch, " + sector.GetChunk() + ", " + chunk);

                    //Then move the pointer to the next sector
                    pos = (long) (sectorId = sector.GetNextSector()) * RSSector.SIZE;
                }
                else {
                    //Otherwise if the amount remaining is less than the sector size, put it down
                    containerData.Write(sector.GetData(), 0, remaining);
                    Debug("\t\t-Partial sector: " + remaining + "/512 bytes", LOG_DETAIL.INSANE);
                    //We've read the last sector in this index!
                    remaining = 0;
                }
            } while (remaining > 0);

            return containerData.Flip();
        }

        /// <summary>
        /// Memoize all of the reference tables from the cache
        /// </summary>
        public void LoadReferenceTables() {
            /* Sized by index id and not by how many indexes exist. The ids are not contiguous, so
               a cache holding {0, 1, 4} needs five slots to address index 4 at all - a true count
               of three would put it out of bounds. */
            referenceTables = new RSReferenceTable[store.HighestContentIndexId + 1];

            //And iterate the ids that are actually present, rather than counting up to the bound
            foreach (int indexId in store.ContentIndexIds) {
                try {
                    GetReferenceTable(indexId);
                }
                catch (FileNotFoundException ex) {
                    Debug(ex.Message);
                }
            }
        }

        /// <summary>
        /// Retrieve the memoized ReferenceTable from the cache if possible.
        /// Otherwise, memoize and return the specified ReferenceTable
        /// </summary>
        /// <param name="indexId">The reference table index</param>
        /// <returns></returns>

        public RSReferenceTable GetReferenceTable(int indexId) {
            //A bound on the id, not on how many indexes there are - see RSFileStore for why
            if (indexId < 0 || indexId > store.HighestContentIndexId)
                throw new FileNotFoundException("\tERROR - Reference table " + indexId + " out of bounds");

            //The decode reads the container's shared stream, so it has to be inside the lock and
            //not merely the container lookup that feeds it.
            lock (_containerLock) {
                if (referenceTables[indexId] == null) {
                    RSContainer container = GetContainer(RSConstants.META_INDEX, indexId);

                    if (container == null)
                        throw new FileNotFoundException("\tERROR - Reference table container " + indexId + " is null");

                    //Decode the reference table from the container stream and cache it
                    JagStream containerStream = container.GetStream();
                    RSReferenceTable refTable = ReferenceTableCodec.Decode(containerStream);
                    refTable.SetIndexId(indexId); //For the UI
                    referenceTables[indexId] = refTable;
                    Debug("...Decoded reference table " + indexId, LOG_DETAIL.ADVANCED);
                    Debug("", LOG_DETAIL.ADVANCED);
                }

                return referenceTables[indexId];
            }
        }

        /// <summary>
        ///     The group ids an index's reference table declares, ascending.
        /// </summary>
        /// <remarks>
        ///     <b>Table-driven.</b> This is what the client can address: <c>JS5Archive</c> gates
        ///     every read on the reference table, so a group the table does not list is unreachable
        ///     in game whether or not its bytes are in the dat2. Groups that exist in the idx file
        ///     and not in the table do occur here - index 4 has one and index 12 has two - and they
        ///     are reported by <see cref="EnumerateOrphanGroups"/> rather than silently folded in,
        ///     because a sweep that mixes the two populations cannot say which reading its count
        ///     belongs to.
        ///     <para>
        ///     The declared ids are sparse on most indexes, so this is the list to walk. Never
        ///     <c>0..Capacity</c>: index 13 declares 25 groups at ids up to 4040, and index 23
        ///     skips 45 to 63 entirely.
        ///     </para>
        ///     <para>
        ///     Snapshotted under the cache lock and returned whole, so a concurrent
        ///     <see cref="WriteFile"/> cannot invalidate the caller's iteration part way through.
        ///     </para>
        /// </remarks>
        /// <param name="indexId">The index to enumerate.</param>
        /// <returns>The declared group ids, or an empty sequence when the index has no reference table.</returns>
        public IEnumerable<int> EnumerateGroups(int indexId) {
            lock (_containerLock) {
                RSReferenceTable? table = TryGetReferenceTable(indexId);
                return table == null ? Array.Empty<int>() : table.GetArchiveEntries().Keys.ToArray();
            }
        }

        /// <summary>
        ///     Every (group, file) pair an index's reference table declares, in ascending group
        ///     then file order.
        /// </summary>
        /// <remarks>
        ///     The point of this is that it does not throw. Walking <c>0..255</c> per group and
        ///     letting <see cref="ReadFile"/> raise <see cref="FileNotFoundException"/> for the
        ///     holes costs over a thousand exceptions on a single index-16 tab load, and groups
        ///     really are sparse: 64 of index 16's 224 groups are short, and 63 of those have gaps
        ///     in the middle of their id range rather than at the end.
        ///     <para>
        ///     Table-driven, with the same caveat and the same escape hatch as
        ///     <see cref="EnumerateGroups"/>.
        ///     </para>
        /// </remarks>
        /// <param name="indexId">The index to enumerate.</param>
        /// <returns>The declared pairs, or an empty sequence when the index has no reference table.</returns>
        public IEnumerable<(int Group, int File)> EnumerateFiles(int indexId) {
            (int Group, int[] Files)[] snapshot;

            //Snapshot inside the lock and yield outside it. A lazy iterator holding the lock across
            //a yield would keep it for as long as the caller takes to decode each file, which is
            //the whole tab load; one that took no snapshot at all would throw
            //InvalidOperationException the moment a WriteFile added an archive entry mid-walk.
            lock (_containerLock) {
                RSReferenceTable? table = TryGetReferenceTable(indexId);
                if (table == null)
                    return Array.Empty<(int Group, int File)>();

                snapshot = table.GetArchiveEntries()
                    .Select(kv => (Group: kv.Key, Files: kv.Value.GetValidFileIds()))
                    .ToArray();
            }

            return WalkSnapshot(snapshot);
        }

        /// <summary>
        ///     Yields the snapshot taken by <see cref="EnumerateFiles(int)"/>.
        /// </summary>
        /// <remarks>
        ///     The captured arrays are the entries' own id lists rather than copies. Nothing
        ///     mutates one in place - <c>SetValidFileIds</c> replaces the reference - and only ints
        ///     leave this method, so no alias escapes to the caller.
        /// </remarks>
        private static IEnumerable<(int Group, int File)> WalkSnapshot((int Group, int[] Files)[] snapshot) {
            foreach ((int group, int[] files) in snapshot)
                foreach (int file in files)
                    yield return (group, file);
        }

        /// <summary>
        ///     The file ids a single group declares, ascending.
        /// </summary>
        /// <param name="indexId">The index the group belongs to.</param>
        /// <param name="groupId">The group id.</param>
        /// <returns>A copy of the declared file ids, or an empty array when the group is absent.</returns>
        public int[] GetFileIds(int indexId, int groupId) {
            lock (_containerLock) {
                RSReferenceTable? table = TryGetReferenceTable(indexId);
                RSArchiveEntry? entry = table?.GetArchiveEntry(groupId);

                //Copied rather than handed out directly: the array returned by GetValidFileIds is
                //the one RSArchive.Decode is driven by, and a caller that sorted or truncated it
                //would change how the stored payload is read.
                return entry == null ? Array.Empty<int>() : (int[]) entry.GetValidFileIds().Clone();
            }
        }

        /// <summary>
        ///     How many files an index's reference table declares in total.
        /// </summary>
        /// <remarks>
        ///     For sizing a progress bar without walking the index twice. This is the real total,
        ///     unlike <c>groupCount * pageSize</c>, which overstates every sparse index - index 19
        ///     has 80 groups and 20,470 items rather than 20,480.
        /// </remarks>
        /// <param name="indexId">The index to measure.</param>
        /// <returns>The declared file count, or 0 when the index has no reference table.</returns>
        public int CountFiles(int indexId) {
            lock (_containerLock) {
                RSReferenceTable? table = TryGetReferenceTable(indexId);
                if (table == null)
                    return 0;

                int total = 0;
                foreach (KeyValuePair<int, RSArchiveEntry> kv in table.GetArchiveEntries())
                    total += kv.Value.GetValidFileIds().Length;
                return total;
            }
        }

        /// <summary>
        ///     Groups that hold a live container in the idx file but are absent from the index's
        ///     reference table.
        /// </summary>
        /// <remarks>
        ///     The idx-driven complement to <see cref="EnumerateGroups"/>, which is table-driven.
        ///     Both readings are legitimate and they disagree: index 4's idx holds 10,238 records
        ///     against 10,237 declared (id 4787 is the orphan), and index 12's holds 4151 against
        ///     4149 (699 and 700). Their payloads parse cleanly; they are repack residue the client
        ///     can never load, because it resolves every group through the table.
        ///     <para>
        ///     A group counts as live only if its six byte idx record names a sector inside the
        ///     dat2, which is the same test <see cref="LoadContainer"/> applies. That excludes the
        ///     not-present marker - a length of <c>0xFF0000</c> pointing at sector 0 - which index
        ///     10 slot 0, index 13 slot 0 and index 31 slot 0 all carry.
        ///     </para>
        /// </remarks>
        /// <param name="indexId">The index to scan.</param>
        /// <returns>The orphaned group ids, ascending. Empty for most indexes.</returns>
        public IReadOnlyList<int> EnumerateOrphanGroups(int indexId) {
            lock (_containerLock) {
                var orphans = new List<int>();

                int slots;
                try {
                    slots = store.GetFileCount(indexId);
                }
                catch (FileNotFoundException) {
                    //No idx file for this index, so there is nothing to be orphaned from.
                    return orphans;
                }

                RSReferenceTable? table = TryGetReferenceTable(indexId);
                RSIndex index = store.GetIndexEntry(indexId);
                long sectorLimit = store.dataChannel.Length / RSSector.SIZE;

                for (int groupId = 0 ; groupId < slots ; groupId++) {
                    if (table != null && table.GetArchiveEntries().ContainsKey(groupId))
                        continue;

                    index.ReadContainerHeader(groupId);

                    if (index.GetSize() <= 0)
                        continue;
                    if (index.GetSectorID() <= 0 || index.GetSectorID() >= sectorLimit)
                        continue;

                    orphans.Add(groupId);
                }

                return orphans;
            }
        }

        /// <summary>
        ///     The reference table for an index, or <c>null</c> when the index has none.
        /// </summary>
        /// <remarks>
        ///     Indexes 34 and 35 have no idx255 record at all in this cache, so a caller sweeping
        ///     every index would otherwise have to special-case them. An index id outside the store
        ///     is indistinguishable from that here and also comes back null; the enumeration API is
        ///     read-only and reports empty for both, which is the answer either way.
        /// </remarks>
        private RSReferenceTable? TryGetReferenceTable(int indexId) {
            try {
                return GetReferenceTable(indexId);
            }
            catch (FileNotFoundException) {
                return null;
            }
        }

        /// <summary>
        /// Retrieve the file from the <paramref name="indexId"/> index, file <paramref name="fileId"/> in archive <paramref name="archiveId"/>
        /// </summary>
        /// <param name="indexId">The index to search</param>
        /// <param name="archiveId">The archive id</param>
        /// <param name="fileId">The file id within the archive</param>
        /// <returns>The file data within the archive</returns>
        internal JagStream ReadFile(int indexId, int archiveId, int fileId) {
            //The whole read is one critical section, not just the container lookup. GetArchive
            //decodes from the container's shared stream and ReleaseData drops that stream as soon
            //as the file has been taken, so a second thread part way through the same container
            //reads from a moved position or from nothing at all.
            lock (_containerLock)
                return ReadFileLocked(indexId, archiveId, fileId);
        }

        private JagStream ReadFileLocked(int indexId, int archiveId, int fileId) {
            //Check if the file is valid
            RSArchiveEntry entry = GetReferenceTable(indexId).GetArchiveEntry(archiveId);

            // Validate the archive exists before touching it: GetArchiveEntry returns
            // null for an archive id that is absent from the reference table, and the
            // message below dereferences entry to report the file count.
            if (entry == null)
                throw new FileNotFoundException("\tUnable to find archive " + archiveId + " in index " + indexId + " (requested file " + fileId + ")");

            // Validate the requested file actually exists within this archive
            if (!entry.GetFileEntries().ContainsKey(fileId))
                throw new FileNotFoundException("\tUnable to find file " + fileId + ", in index " + indexId + ", archive " + archiveId + ", len: " + entry.GetValidFileIds().Length);

            Debug($"Reading index {RSConstants.GetIndexName(indexId)}   archive {archiveId}   file {fileId}", LOG_DETAIL.ADVANCED);

            Debug($"Archive {archiveId} has {entry.GetValidFileIds().Length} files", LOG_DETAIL.INSANE);

            RSContainer container = GetContainer(indexId, archiveId);
            if (container == null) {
                return null;
            }

            RSArchive archive = GetArchive(container, entry.GetValidFileIds());

            if (archive == null) {
                Debug($"Archive {archiveId} is null for index {indexId}");
                return null;
            }

            JagStream result = archive.GetFile(fileId);
            container.ReleaseData();
            return result;
        }

        /// <summary>
        /// Returns the memoized archive if possible. Decodes and memoizes if not yet already done.
        /// </summary>
        /// <param name="container">The container from which the archive is built</param>
        /// <param name="fileIds">The actual file IDs contained in the archive</param>
        /// <returns>Returns the decoded archive instance</returns>
        public static RSArchive GetArchive(RSContainer container, int[] fileIds) {
            //Has the archive already been decoded from the container data?
            if (container.GetArchive() != null)
                return container.GetArchive();

            //Otherwise, construct the archive from the container
            RSArchive archive = RSArchive.Decode(container.GetStream(), fileIds);

            if (archive == null) {
                Debug("Corrupted archive in container " + container.GetId(), LOG_DETAIL.ADVANCED);
                throw new NullReferenceException("Archive is null");
            }

            container.SetArchive(archive);

            return archive;
        }

        /// <summary>
        /// Gets the number of files of the specified index.
        /// </summary>
        /// <param name="indexId">The index id</param>
        /// <returns>The total number of files of the specified index</returns>

        public int GetFileCount(int indexId) {
            return store.GetFileCount(indexId);
        }

        /// <summary>
        /// Returns the filestore for this cache
        /// </summary>
        /// <returns>The filestore for this cache</returns>
        internal RSFileStore GetStore() {
            return store;
        }

        /// <summary>
        /// Decodes and returns an item definition from the config index.
        /// </summary>
        /// <param name="archiveId">The archive containing the item file</param>
        /// <param name="fileId">The file id within the archive</param>
        /// <returns>The decoded <see cref="ItemDefinition"/></returns>
        public ItemDefinition GetItemDefinition(int archiveId, int fileId) {
            JagStream entry = ReadFile(RSConstants.ITEM_DEFINITIONS_INDEX, archiveId, fileId);
            ItemDefinition def = ItemDefinition.DecodeFromStream(entry);
            def.SetId(archiveId * 256 + fileId);
            return def;
        }

        /// <summary>
        /// Decodes and returns a sprite definition from the sprites index.
        /// </summary>
        /// <param name="containerId">The container id of the sprite</param>
        /// <returns>The decoded <see cref="SpriteDefinition"/></returns>
        public SpriteDefinition GetSprite(int containerId) {
            return GetSprite(RSConstants.SPRITES_INDEX, containerId);
        }

        /// <summary>
        ///     Decodes a sprite set from any index that stores the sprite format.
        /// </summary>
        /// <remarks>
        ///     Index 8 is not the only one holding this format. The client opens index <b>32</b>
        ///     (or 34 - <c>InterfaceSettings.java:73-74</c> picks between them) and hands it
        ///     straight to the same decoder at <c>Class114.java:82</c>, pairing each glyph sheet
        ///     with the font metrics it reads from index 13. Index 13 itself is <em>not</em> this
        ///     format: it holds <c>Class197</c> metrics, read by <c>Class119_Sub1.method2182</c>,
        ///     and none of its 25 groups parses as a sprite set. Index 32 is mixed - five of its
        ///     26 groups are 256-frame glyph sheets and the other 21 are JPEG, ending in the
        ///     <c>FF D9</c> marker - so a caller has to know which it is asking for.
        ///     <para>
        ///     The whole group container is handed to the decoder without going through
        ///     <see cref="RSArchive"/>, which is correct only while a group holds exactly one file -
        ///     true of every group in index 8. A second file would put the multi-file size table
        ///     where the sprite metadata is read from, and the decode would silently produce
        ///     nonsense rather than fail.
        ///     </para>
        /// </remarks>
        /// <param name="indexId">The index holding the sprite set.</param>
        /// <param name="containerId">The group id within that index.</param>
        /// <returns>The decoded <see cref="SpriteDefinition"/>.</returns>
        public SpriteDefinition GetSprite(int indexId, int containerId) {
            Debug($"GetSprite: index {indexId} container {containerId}", LOG_DETAIL.ADVANCED);

            //Decode and release together under the lock. Both the map render thread (map scene
            //icons) and the texture worker call this, and the release nulls the very stream the
            //other one is decoding from.
            lock (_containerLock) {
                //Get the sprite for the given archive
                RSContainer container = GetContainer(indexId, containerId);
                if (container == null || container.GetStream() == null)
                    throw new FileNotFoundException($"Sprite container {containerId} in index {indexId} not found or has no data");
                Debug($"Container index {container.GetIndexId()} id {container.GetId()} length {container.GetStream().Length}", LOG_DETAIL.INSANE);
                Debug($"Decoding sprite container {containerId}", LOG_DETAIL.ADVANCED);
                SpriteDefinition sprite = SpriteDefinition.DecodeFromStream(container.GetStream());
                container.ReleaseData();
                return sprite;
            }
        }

        /// <summary>
        ///     Decodes a floor underlay definition from the config index.
        /// </summary>
        /// <remarks>
        ///     JS5 index 2, group <see cref="RSConstants.FLOOR_UNDERLAY_GROUP"/>. The definition id
        ///     is the file id. The prior spec placed these in index 3, which is a different archive
        ///     entirely.
        /// </remarks>
        /// <param name="definitionId">The underlay id, 0..158 in the shipped cache.</param>
        /// <returns>The decoded definition.</returns>
        public FloorUnderlayDefinition GetFloorUnderlay(int definitionId) {
            JagStream data = ReadFile(RSConstants.CONFIG, RSConstants.FLOOR_UNDERLAY_GROUP, definitionId);
            return new FloorUnderlayDefinition { Id = definitionId }.Decode(data);
        }

        /// <summary>
        ///     Decodes a floor overlay definition from the config index.
        /// </summary>
        /// <remarks>
        ///     JS5 index 2, group <see cref="RSConstants.FLOOR_OVERLAY_GROUP"/>. The prior spec
        ///     placed these in index 4, which is a different archive entirely.
        /// </remarks>
        /// <param name="definitionId">The overlay id, 0..234 in the shipped cache.</param>
        /// <returns>The decoded definition.</returns>
        public FloorOverlayDefinition GetFloorOverlay(int definitionId) {
            JagStream data = ReadFile(RSConstants.CONFIG, RSConstants.FLOOR_OVERLAY_GROUP, definitionId);
            return new FloorOverlayDefinition { Id = definitionId }.Decode(data);
        }

        /// <summary>
        ///     Decodes a map scene icon definition from the config index.
        /// </summary>
        /// <remarks>
        ///     JS5 index 2, group <see cref="RSConstants.MAP_SCENE_GROUP"/>. An object definition
        ///     points at one of these through <c>ObjectDefinition.mapSceneIcon</c>, which is opcode
        ///     102 rather than the identically-named-looking opcode 68.
        /// </remarks>
        /// <param name="definitionId">The icon id.</param>
        /// <returns>The decoded definition.</returns>
        public MapSceneIconDefinition GetMapSceneIcon(int definitionId) {
            JagStream data = ReadFile(RSConstants.CONFIG, RSConstants.MAP_SCENE_GROUP, definitionId);
            return new MapSceneIconDefinition { Id = definitionId }.Decode(data);
        }

        /// <summary>
        ///     Decodes a music track into a standard MIDI file.
        /// </summary>
        /// <remarks>
        ///     Index 6 holds the music and index 11 the jingles. The client opens both and hands
        ///     either to the same decoder, so the index is a parameter rather than a constant
        ///     (InterfaceSettings.java:164,168 and Node_Sub7.method985).
        ///
        ///     Every group in both indexes holds exactly one file, so the file id comes from the
        ///     reference table rather than being assumed to be zero.
        /// </remarks>
        /// <param name="indexId">The index the group belongs to, 6 or 11.</param>
        /// <param name="groupId">The group id, which is also the track id.</param>
        /// <returns>The decoded track.</returns>
        /// <exception cref="FileNotFoundException">The group is absent or holds no file.</exception>
        public Track GetTrack(int indexId, int groupId) {
            RSArchiveEntry entry = GetReferenceTable(indexId).GetArchiveEntry(groupId);
            if (entry == null)
                throw new FileNotFoundException("No track group " + groupId + " in index " + indexId);

            int[] fileIds = entry.GetValidFileIds();
            if (fileIds.Length == 0)
                throw new FileNotFoundException("Track group " + groupId + " in index " + indexId + " holds no file");

            JagStream data = ReadFile(indexId, groupId, fileIds[0]);
            if (data == null)
                throw new FileNotFoundException("Track group " + groupId + " in index " + indexId + " could not be read");

            return new Track { Id = groupId, IndexId = indexId, NameHash = entry.GetIdentifier() }.Decode(data);
        }

        /// <summary>
        ///     Returns a file's bytes, as stored, for callers that want to decode it themselves.
        /// </summary>
        /// <param name="indexId">The index the archive belongs to.</param>
        /// <param name="archiveId">The archive id within the index.</param>
        /// <param name="fileId">The file id within the archive.</param>
        /// <returns>A copy of the file payload.</returns>
        public byte[] ReadFileBytes(int indexId, int archiveId, int fileId) {
            JagStream data = ReadFile(indexId, archiveId, fileId);
            return data?.ToArray() ?? Array.Empty<byte>();
        }

        /// <summary>
        ///     An index's name lookup, at both the group and the file level.
        /// </summary>
        /// <remarks>
        ///     Shared machinery rather than a per-tab helper: indexes 3, 5, 23, 30, 31, 32 and 33 all
        ///     carry names, and index 2 carries none and says so through
        ///     <see cref="CacheNameIndex.NameLookupRefusal"/> instead of answering -1 in silence.
        /// </remarks>
        /// <param name="indexId">The index to look up names in.</param>
        /// <returns>The lookup.</returns>
        public CacheNameIndex GetNameIndex(int indexId) {
            lock (_containerLock)
                return GetReferenceTable(indexId).Names;
        }

        /// <summary>
        ///     Reads a file the way the client addresses it, by group name and file name.
        /// </summary>
        /// <remarks>
        ///     <c>JS5Archive.method2739</c> lower-cases both halves, hashes each and resolves the
        ///     group then the file - so <c>"gl"/"transparent_water"</c> is a real address and not a
        ///     convenience. The file name is the empty string for a single-file group: every index-30
        ///     library is stored as <c>""</c> inside a group named for its path, and
        ///     <c>Class35.java:102</c> passes that empty string explicitly.
        /// </remarks>
        /// <param name="indexId">The index the group belongs to.</param>
        /// <param name="groupName">The group name, case-insensitive.</param>
        /// <param name="fileName">The file name within it, case-insensitive.</param>
        /// <returns>A copy of the file payload.</returns>
        /// <exception cref="FileNotFoundException">Either half of the name resolves to nothing.</exception>
        public byte[] ReadFileBytes(int indexId, string groupName, string fileName) {
            CacheNameIndex names = GetNameIndex(indexId);

            if (names.TryResolve(groupName, fileName, out int groupId, out int fileId))
                return ReadFileBytes(indexId, groupId, fileId);

            //The refusal is spliced in where there is one, because "not found" and "this index has
            //no names to search" are different answers and only the second is worth acting on.
            throw new FileNotFoundException(
                "\tNo file named \"" + groupName + "\"/\"" + fileName + "\" in index " + indexId + "." +
                (names.NameLookupRefusal == null ? string.Empty : " " + names.NameLookupRefusal));
        }

        /// <summary>
        ///     Every file a group declares, decoded from one pass over that group's payload.
        /// </summary>
        /// <remarks>
        ///     <see cref="ReadFile"/> calls <c>ReleaseData</c> as soon as it has handed back the one
        ///     file it was asked for, so a loader that walks a group file by file re-reads the
        ///     sector chain, re-inflates and re-decodes that group once per file. Index 3 declares
        ///     42,256 files across 1078 groups, so the file-at-a-time shape does forty times the
        ///     work of this one for the same bytes.
        ///     <para>
        ///     The streams handed back are the archive's own, exactly as <see cref="ReadFile"/>
        ///     returns them - the container is released afterwards, which drops the archive but not
        ///     the stream objects, so each one is the caller's from here on. A file the reference
        ///     table declares but the payload does not carry is omitted rather than returned empty,
        ///     so <c>Count</c> is the number of files that actually decoded.
        ///     </para>
        /// </remarks>
        /// <param name="indexId">The index the group belongs to.</param>
        /// <param name="groupId">The group id.</param>
        /// <returns>The decoded files keyed by file id, ascending.</returns>
        /// <exception cref="FileNotFoundException">The group is absent from the index's reference table.</exception>
        public IReadOnlyDictionary<int, JagStream> ReadGroup(int indexId, int groupId) {
            //One critical section for the whole group, for the reason ReadFile takes one for a
            //single file: the decode runs off the container's shared stream and the release at the
            //end drops it.
            lock (_containerLock) {
                RSArchiveEntry entry = GetReferenceTable(indexId).GetArchiveEntry(groupId);
                if (entry == null)
                    throw new FileNotFoundException("\tUnable to find archive " + groupId + " in index " + indexId);

                var files = new SortedDictionary<int, JagStream>();

                int[] fileIds = entry.GetValidFileIds();
                if (fileIds.Length == 0)
                    return files;

                RSContainer container = GetContainer(indexId, groupId);
                if (container == null)
                    return files;

                RSArchive archive = GetArchive(container, fileIds);
                foreach (int fileId in fileIds)
                    if (archive.HasFile(fileId))
                        files[fileId] = archive.GetFile(fileId);

                container.ReleaseData();
                return files;
            }
        }

        /// <summary>
        ///     The file ids present in a config group, ascending.
        /// </summary>
        /// <param name="groupId">The group within the config index.</param>
        /// <returns>The file ids, or an empty array when the group is absent.</returns>
        public int[] GetConfigFileIds(int groupId) {
            return GetFileIds(RSConstants.CONFIG, groupId);
        }

        /// <summary>
        /// Decodes and returns an object (loc) definition from the config index.
        /// </summary>
        /// <param name="archiveId">The archive containing the object file</param>
        /// <param name="fileId">The file id within the archive</param>
        /// <returns>The decoded <see cref="ObjectDefinition"/></returns>
        public ObjectDefinition GetObjectDefinition(int archiveId, int fileId) {
            JagStream objStream = ReadFile(RSConstants.OBJECTS_DEFINITIONS_INDEX, archiveId, fileId);
            ObjectDefinition def = ObjectDefinition.DecodeFromStream(objStream);
            def.id = CacheAddressing.For(RSConstants.OBJECTS_DEFINITIONS_INDEX).DefinitionId(archiveId, fileId);
            return def;
        }

        /// <remarks>
        ///     NPCs page 128 ids to a group, not 256. This folded the id with <c>* 256</c>, which is
        ///     the objects and items split, and produced an id naming a different NPC for every
        ///     group above zero. It was invisible because the only caller overwrote the id on the
        ///     next line and the encoder ignores it, so no sweep could see it. Routed through
        ///     <see cref="CacheAddressing"/> so the split is stated once per index rather than
        ///     open-coded per call site.
        /// </remarks>
        internal NPCDefinition GetNPCDefinition(int archiveId, int fileId) {
            JagStream npcStream = ReadFile(RSConstants.NPC_DEFINITIONS_INDEX, archiveId, fileId);
            NPCDefinition def = new NPCDefinition(npcStream);
            def.SetId(CacheAddressing.For(RSConstants.NPC_DEFINITIONS_INDEX).DefinitionId(archiveId, fileId));
            return def;
        }

        /// <summary>
        /// Enumerates references to all models present in the cache without
        /// decoding them.
        /// </summary>
        /// <returns>An enumerable of <see cref="ModelReference"/> records.</returns>
        internal IEnumerable<ModelReference> EnumerateModelReferences() {
            RSReferenceTable table = GetReferenceTable(RSConstants.MODELS_INDEX);
            foreach (var (archiveId, entry) in table.GetArchiveEntries())
                foreach (int fileId in entry.GetValidFileIds())
                    yield return new ModelReference(archiveId, fileId);
        }

        /// <summary>
        /// Decodes and returns a model definition from the models index.
        /// </summary>
        /// <param name="archiveId">The archive containing the model file</param>
        /// <param name="fileId">The file id within the archive</param>
        /// <returns>The decoded <see cref="ModelDefinition"/></returns>
        public ModelDefinition GetModelDefinition(int archiveId, int fileId) {
            int modelId = archiveId;
            try {
                JagStream data = ReadFile(RSConstants.MODELS_INDEX, archiveId, fileId);
                var def = new ModelDefinition();
                def.ModelID = modelId;
                def.Decode(data);
                return def;
            }
            catch (Exception ex) {
                var sb = new System.Text.StringBuilder();
                sb.AppendLine($"[Model ID {modelId}] archive={archiveId} file={fileId}");
                sb.AppendLine($"Type: {ex.GetType().Name}");
                sb.AppendLine($"Message: {ex.Message}");
                sb.AppendLine($"Stack: {ex.StackTrace}");
                // Optional: dump first/last 32 bytes of the model blob
                try {
                    JagStream raw = ReadFile(RSConstants.MODELS_INDEX, archiveId, fileId);
                    if (raw == null)
                        throw;
                    raw.Seek0();
                    byte[] head = raw.ReadBytes(Math.Min(32, raw.Length));
                    raw.Position = Math.Max(0, raw.Length - 32);
                    byte[] tail = raw.ReadBytes(Math.Min(32, raw.Length));
                    sb.AppendLine($"Data len={raw.Length}  head={BitConverter.ToString(head)}  tail={BitConverter.ToString(tail)}");
                }
                catch (Exception ex2) {
                    Debug($"Failed to load model file {modelId}: {ex2}", LOG_DETAIL.BASIC);

                }

                Debug(sb.ToString(), LOG_DETAIL.ADVANCED);
                throw;                               // re-throw so outer loop still logs "failed"
            }
        }

        /// <summary>
        ///     Decodes a container, applying an XTEA key when one is held and falling back to
        ///     reading it unencrypted when that key does not fit.
        /// </summary>
        /// <remarks>
        ///     A format 6 table carries no per-archive encryption flag, so the only signal that
        ///     an archive is encrypted is that a key exists for it. That signal is not reliable:
        ///     key dumps cover a whole build, while a cache may have had some archives decrypted
        ///     in place, which is common in cache repacks. Applying a key to an archive that is
        ///     already plaintext destroys it, so a key that fails is treated as evidence the
        ///     archive was not encrypted rather than as a fatal error. The reverse case cannot
        ///     be papered over the same way and still throws: an encrypted archive with no key
        ///     is unreadable by anyone.
        /// </remarks>
        /// <param name="data">Raw stored container bytes.</param>
        /// <param name="indexId">The index the archive belongs to.</param>
        /// <param name="archiveId">The archive id within the index.</param>
        /// <returns>The decoded container.</returns>
        private RSContainer DecodeContainer(JagStream data, int indexId, int archiveId) {
            int[] key = ResolveXTEAKey(indexId, archiveId);
            if (key == null)
                return RSContainer.Decode(data, null);

            byte[] raw = data.ToArray();

            try {
                return RSContainer.Decode(new JagStream(raw), key);
            }
            catch (Exception ex) {
                Debug("XTEA key did not fit index " + indexId + ", archive " + archiveId +
                      " (" + ex.Message + "); reading it unencrypted", LOG_DETAIL.ADVANCED);
                return RSContainer.Decode(new JagStream(raw), null);
            }
        }

        /// <summary>
        ///     Returns the key an archive has to be written back under, or null when it was
        ///     stored in the clear and must stay that way.
        /// </summary>
        /// <remarks>
        ///     Only two outcomes are acceptable, and neither of them is a guess. Either the
        ///     archive is written in the state it was read in, or the save fails: silently
        ///     writing plaintext over an encrypted map square destroys it with no error anywhere,
        ///     and silently encrypting an archive that was stored plaintext destroys it just as
        ///     thoroughly in the other direction. The key table cannot arbitrate that, because it
        ///     holds keys for many archives that are not encrypted - so the decision rests
        ///     entirely on <see cref="RSContainer.StoredEncrypted"/>, recorded when the archive
        ///     was decoded, and the key table is consulted only once that says encryption is
        ///     required.
        /// </remarks>
        /// <param name="container">The container about to be encoded.</param>
        /// <param name="indexId">The index the archive belongs to.</param>
        /// <param name="archiveId">The archive id within the index.</param>
        /// <returns>The key to encipher with, or null to write plaintext.</returns>
        /// <exception cref="InvalidOperationException">
        ///     The archive was stored encrypted but no key is available to re-encrypt it.
        /// </exception>
        private int[] ResolveWriteKey(RSContainer container, int indexId, int archiveId) {
            if (!container.StoredEncrypted)
                return null;

            int[] key = ResolveXTEAKey(indexId, archiveId);
            if (key == null)
                throw new InvalidOperationException(
                    "Refusing to write index " + indexId + ", archive " + archiveId +
                    " in plaintext: it was decrypted on read, so writing it unencrypted would" +
                    " corrupt it beyond recovery. Load the XTEA key file for this cache and retry.");

            return key;
        }

        /// <summary>
        /// Resolves the XTEA key for the given index/archive pair, if the
        /// archive is flagged as encrypted and a key is available.
        /// </summary>
        private int[] ResolveXTEAKey(int indexId, int archiveId) {
            if (xteaKeys == null)
                return null;

            // Reference tables (META_INDEX 255) are never encrypted
            if (indexId == RSConstants.META_INDEX)
                return null;

            /* Only a format 7 table carries a per-archive flags byte. On format 6 - which is
               every table in a revision 639 cache - UsesXtea is read off a byte that does not
               exist on the wire and so is always false, and gating on it here withheld the key
               from every archive in the cache. Where there is no flag to consult, the presence
               of a key in the table is the only signal available. */
            if (referenceTables != null && indexId < referenceTables.Length && referenceTables[indexId] != null) {
                RSReferenceTable table = referenceTables[indexId];
                RSArchiveEntry entry = table.GetArchiveEntry(archiveId);
                if (table.format >= 7 && entry != null && !entry.UsesXtea)
                    return null;
            }

            return xteaKeys.GetKey(indexId, archiveId);
        }

        /// <summary>
        /// Loads XTEA keys from the specified JSON file.
        /// </summary>
        public void LoadXTEAKeys(string filePath) {
            xteaKeys = XTEAKeyTable.LoadFromFile(filePath);
            Debug("Loaded " + xteaKeys.Count + " XTEA keys from " + filePath);
        }

        /// <summary>
        /// Attempts to auto-discover and load XTEA keys from near the cache directory.
        /// </summary>
        public void TryAutoLoadXTEAKeys(string cacheDir) {
            string keyFile = XTEAKeyTable.FindKeyFile(cacheDir);
            if (keyFile != null)
                LoadXTEAKeys(keyFile);
        }

        /// <summary>
        /// Returns the current XTEA key table, or null if none loaded.
        /// </summary>
        public XTEAKeyTable GetXTEAKeyTable() {
            return xteaKeys;
        }
    }
}
