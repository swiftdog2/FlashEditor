using FlashEditor.cache.sprites;
using FlashEditor.Cache.Util;
using FlashEditor.Cache.Util.Crypto;
using FlashEditor.Definitions;
using FlashEditor.Utils;
using ICSharpCode.SharpZipLib.Checksum;
using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using static FlashEditor.Utils.DebugUtil;

namespace FlashEditor.cache {
    public class RSCache {
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

        internal void WriteCache() {
            Debug("Writing cache to disk...");

            SaveDataIndex();
            SaveIndexes();
        }

        /// <summary>
        /// Write the main data (dat2) to file
        /// </summary>
        internal void SaveDataIndex() {
            store.dataChannel.SaveTo(RSConstants.CACHE_OUTPUT_DIRECTORY + "main_file_cache.dat2");
        }

        /// <summary>
        /// Write the index streams to files
        /// </summary>
        internal void SaveIndexes() {
            var sb = new StringBuilder();
            foreach (KeyValuePair<int, RSIndex> index in GetStore().indexChannels) {
                sb.Clear();
                sb.Append("idx");
                sb.Append(index.Key);
                SaveIndex(index.Value, sb.ToString());
            }
        }

        internal void SaveIndex(RSIndex index, string directory) {
            Debug("Saving " + directory);
            JagStream.Save(index.GetStream(), RSConstants.CACHE_OUTPUT_DIRECTORY + "main_file_cache." + directory);
        }

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

            //Create a new archive for the container, if necessary
            RSArchive archive = container.GetArchive();
            if (archive == null) {
                Debug("Added new archive", LOG_DETAIL.INSANE);
                container.SetArchive(archive = new RSArchive());
            }

            //Create or update the file in the archive
            archive.PutFile(fileId, data);

            //Wrap the archive back into a container
            container.SetStream(archive.Encode());

            //Create 'dummy' file entries
            for (int id = 0 ; id < archive.FileCount() ; id++) {
                if (archive.GetFile(id) == null) {
                    archiveEntry.PutFileEntry(id, new RSFileEntry(id));
                    archive.PutFile(id, new JagStream(1));
                }
            }

            //Grab the bytes we need for the checksum
            JagStream stream = container.Encode(); //already checked definitely correct upto this point

            //Last two bytes are the version and shouldn't be included in the checksum
            JagStream hashableStream = new JagStream(stream.ReadBytes(stream.Length - 2));

            //Update the version and checksum for this file
            hashableStream.Seek0(); //allows the crc32 to slurp the blocks
            var crc = new Crc32();
            crc.Update(hashableStream.ToArray());      // feeds the bytes
            archiveEntry.SetCrc((int) crc.Value);              // .Value is UInt32
            archiveEntry.SetVersion(1337);

            //Calculate and update the whirlpool digest if we need to
            if (table.usesWhirlpool) {
                byte[] digest = Whirlpool.ComputeHash(hashableStream.ToArray());
                archiveEntry.SetWhirlpool(digest);
            }

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
                RSContainer reloaded = RSContainer.Decode(evictedData, ResolveXTEAKey(indexId, archiveId));
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
            RSContainer container = RSContainer.Decode(data, ResolveXTEAKey(indexId, archiveId));

            if (container == null)
                throw new FileNotFoundException("NULL CONTAINER? (index: " + indexId + ", archive: " + archiveId + ")");

            container.SetIndexId(indexId);
            container.SetId(archiveId);

            //Cache the container for later usage
            containers[indexId][archiveId] = container;

            return container;
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
            long pos = archiveId * RSIndex.SIZE;

            if (pos < 0 || pos >= index.GetStream().Length)
                throw new FileNotFoundException("Position is out of bounds for index " + indexId + ", archive " + archiveId);

            //Read the archive header, to get the container size and sector ID
            index.ReadContainerHeader(archiveId);

            //If the sector could not be located in the data stream
            if (index.GetSectorID() <= 0 || index.GetSectorID() > store.dataChannel.Length / RSSector.SIZE)
                return null;

            //Allocate buffers for the data and sector
            JagStream containerData = new JagStream(index.GetSize());

            int chunk = 0;
            int remaining = index.GetSize();
            int sectorId = index.GetSectorID();

            //Point to the start of the sector
            pos = sectorId * RSSector.SIZE;

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
                    pos = (sectorId = sector.GetNextSector()) * RSSector.SIZE;
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

            // Validate the requested file actually exists within this archive
            if (entry == null || !entry.GetFileEntries().ContainsKey(fileId))
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
        /// Resolves the XTEA key for the given index/archive pair, if the
        /// archive is flagged as encrypted and a key is available.
        /// </summary>
        private int[] ResolveXTEAKey(int indexId, int archiveId) {
            if (xteaKeys == null)
                return null;

            // Reference tables (META_INDEX 255) are never encrypted
            if (indexId == RSConstants.META_INDEX)
                return null;

            // Check whether the reference table flags this archive as XTEA-encrypted
            if (referenceTables != null && indexId < referenceTables.Length && referenceTables[indexId] != null) {
                RSArchiveEntry entry = referenceTables[indexId].GetArchiveEntry(archiveId);
                if (entry != null && !entry.UsesXtea)
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
