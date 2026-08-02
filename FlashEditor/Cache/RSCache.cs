using FlashEditor.cache.sprites;
using FlashEditor.Cache.Util;
using FlashEditor.Cache.Util.Crypto;
using FlashEditor.Definitions;
using FlashEditor.Utils;
using ICSharpCode.SharpZipLib.Checksum;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using static FlashEditor.Utils.DebugUtil;

namespace FlashEditor.cache {
    public class RSCache {
        /// <summary>
        /// Lock protecting <see cref="containers"/> from concurrent access
        /// (e.g. Parallel.ForEach in texture loading).
        /// </summary>
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
            store.SaveTo(cacheDir);
        }

        /// <summary>Whether any edit is staged and not yet saved.</summary>
        internal bool HasUnsavedChanges => store != null && store.IsDirty;

        /// <summary>
        /// Writes a file contained in an archive to the cache.
        /// </summary>
        /// <param name="indexId">The index id</param>
        /// <param name="archiveId">The archive within the index</param>
        /// <param name="fileId">The file id within the archive</param>
        /// <param name="data">The encoded file data</param>
        public void WriteFile(int indexId, int archiveId, int fileId, JagStream data) {
            if (indexId == RSConstants.META_INDEX)
                throw new IOException("Reference tables can only be modified with the low level FileStore API!");

            Debug("Writing File " + indexId + "," + archiveId + "," + fileId);

            //Get the reference table for the index
            RSReferenceTable table = GetReferenceTable(indexId);
            RSArchiveEntry archiveEntry;

            //Retrieve the appropriate archive entry in the reference table
            if (table.archiveEntries.ContainsKey(archiveId)) {
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

            //Add a file entry if one does not exist
            if (archiveEntry.GetFileEntry(fileId) == null) {
                archiveEntry.PutFileEntry(fileId, new RSFileEntry(fileId));
                Debug("Added new file entry " + fileId, LOG_DETAIL.INSANE);
            }

            RSContainer container = GetContainer(indexId, archiveId);

            //Generate a new container, if necessary
            if (container == null) {
                Debug("Added new container", LOG_DETAIL.INSANE);
                container = new RSContainer(indexId, archiveId, RSConstants.GZIP_COMPRESSION, null, 1337);
            }

            container.Dirty = true;

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

            /* Reconcile the archive against its reference table entry over ACTUAL file ids,
               in both directions. Decode reads the archive through the entry's id list, so
               the two sets have to match exactly or the size table is read against the wrong
               number of files. The loop this replaces walked an ordinal counter from 0 to
               FileCount() and indexed the archive directly, so a sparse archive threw
               KeyNotFoundException on the first gap, and where it did not throw it
               re-registered every file under an ordinal id - reinstating exactly the
               renumbering the reference table encoder was fixed to stop. */
            foreach (int id in archiveEntry.GetFileEntries().Keys.ToArray())
                if (!archive.HasFile(id))
                    archive.PutFile(id, new JagStream(0));

            foreach (int id in archive.GetFileIds())
                if (archiveEntry.GetFileEntry(id) == null)
                    archiveEntry.PutFileEntry(id, new RSFileEntry(id));

            //The valid file id list is the one Decode is handed, so it has to follow the
            //file entries. Left stale, a reloaded container decodes with the wrong ids.
            archiveEntry.SetValidFileIds(archiveEntry.GetFileEntries().Keys.ToArray());

            //Wrap the archive back into a container
            container.SetStream(archive.Encode());

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
            archiveEntry.SetVersion(1337);

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

            //Write out the reference table
            RSContainer tableContainer = new RSContainer(RSConstants.META_INDEX, indexId, RSConstants.GZIP_COMPRESSION, ReferenceTableCodec.Encode(table), 1337);
            store.Write(RSConstants.META_INDEX, indexId, tableContainer.Encode());
            store.Write(indexId, archiveId, stream);
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
            if (!containers.ContainsKey(indexId))
                containers.Add(indexId, new SortedDictionary<int, RSContainer>());

            //Return the container if already cached
            if (containers[indexId].ContainsKey(archiveId))
                containers[indexId][archiveId] = container;
            else
                containers[indexId].Add(archiveId, container);
        }

        /// <summary>
        /// Replaces the cached reference table for a given index.
        /// </summary>
        /// <param name="indexId">The index whose reference table should be replaced</param>
        /// <param name="refTable">The new reference table</param>
        public void UpdateReferenceTable(int indexId, RSReferenceTable refTable) {
            if (indexId < 0 || indexId > referenceTables.Length)
                throw new IndexOutOfRangeException("Invalid index when updating reference table cache");
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
            //Reset the references array
            referenceTables = new RSReferenceTable[store.GetIndexCount()];

            //Attempt to load all of the reference tables
            for (int indexId = 0 ; indexId < store.GetIndexCount() ; indexId++) {
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
            if (indexId < 0 || indexId >= store.GetIndexCount())
                throw new FileNotFoundException("\tERROR - Reference table " + indexId + " out of bounds");

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

        /// <summary>
        /// Retrieve the file from the <paramref name="indexId"/> index, file <paramref name="fileId"/> in archive <paramref name="archiveId"/>
        /// </summary>
        /// <param name="indexId">The index to search</param>
        /// <param name="archiveId">The archive id</param>
        /// <param name="fileId">The file id within the archive</param>
        /// <returns>The file data within the archive</returns>
        internal JagStream ReadFile(int indexId, int archiveId, int fileId) {
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
            Debug($"GetSprite: {containerId}", LOG_DETAIL.ADVANCED);
            //Get the sprite for the given archive
            RSContainer container = GetContainer(RSConstants.SPRITES_INDEX, containerId);
            if (container == null || container.GetStream() == null)
                throw new FileNotFoundException($"Sprite container {containerId} not found or has no data");
            Debug($"Container index {container.GetIndexId()} id {container.GetId()} length {container.GetStream().Length}", LOG_DETAIL.INSANE);
            Debug($"Decoding sprite container {containerId}", LOG_DETAIL.ADVANCED);
            SpriteDefinition sprite = SpriteDefinition.DecodeFromStream(container.GetStream());
            container.ReleaseData();
            return sprite;
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
            def.id = archiveId * 256 + fileId;
            return def;
        }

        internal NPCDefinition GetNPCDefinition(int archiveId, int fileId) {
            JagStream npcStream = ReadFile(RSConstants.NPC_DEFINITIONS_INDEX, archiveId, fileId);
            NPCDefinition def = new NPCDefinition(npcStream);
            def.SetId(archiveId * 256 + fileId);
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
