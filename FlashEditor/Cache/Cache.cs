using FlashEditor.cache.sprites;
using FlashEditor.Cache.Util;
using FlashEditor.utils;
using Ionic.Crc;
using System;
using System.Collections.Generic;
using System.IO;

namespace FlashEditor.cache {
    class Cache {
        public FileStore store;
        public ReferenceTable[] referenceTables;
        public SortedDictionary<int, SortedDictionary<int, Container>> containers = new SortedDictionary<int, SortedDictionary<int, Container>>();
        public SortedDictionary<int, SortedDictionary<int, Archive>> archives = new SortedDictionary<int, SortedDictionary<int, Archive>>();

        public List<ItemDefinition> items = new List<ItemDefinition>();

        /// <summary>
        /// Create a new Cache instance, and automatically memoizes the archives and their reference tables
        /// </summary>
        /// <param name="store">The filestore</param>
        public Cache(FileStore store) {
            this.store = store;
            LoadReferenceTables();
        }

        internal void WriteCache() {
            DebugUtil.Debug("Encoding RSFileStore...");

            WriteDataIndex();
            WriteReferenceTables();
            WriteIndexes();
        }

        /// <summary>
        /// Write the main data (dat2) to file
        /// </summary>
        /// <param name="type">The index type</param>
        /// <returns>Whether or not the index was successfully written</returns>
        internal void WriteDataIndex() {
            DebugUtil.Debug("Writing Data Index...");
            //WriteIndex(GetStore().dataChannel, RSConstants.CACHE_OUTPUT_DIRECTORY + "main_file_cache.dat2");
        }

        /// <summary>
        /// Write the index stream to a file
        /// </summary>
        /// <param name="type">The index type</param>
        /// <returns>Whether or not the index was successfully written</returns>
        internal void WriteReferenceTables() {
            DebugUtil.Debug("Writing Reference Tables...");
            //WriteIndex(GetStore().metaChannel, RSConstants.CACHE_OUTPUT_DIRECTORY + "main_file_cache.idx255");
        }

        /// <summary>
        /// Write the index stream to a file
        /// </summary>
        /// <param name="type">The index type</param>
        /// <returns>Whether or not the index was successfully written</returns>
        internal void WriteIndexes() {
            DebugUtil.Debug("Writing meta indexes...");

            //Write the index channels
            for(int index = 0; index < GetStore().indexChannels.Length; index++)
                if(GetStore().indexChannels[index] != null)
                    ;
            //WriteIndex(index, RSConstants.CACHE_OUTPUT_DIRECTORY + "main_file_cache.idx" + index);
        }

        /**
        * Writes a file to the cache and updates the ReferenceTable that it is associated with.
        */
        internal void WriteIndex(int indexId, int containerId, Container container) {
            //Cecode the reference table for this index
            ReferenceTable table = GetReferenceTable(indexId);

            //Grab the bytes we need for the checksum
            JagStream stream = container.Encode();

            //Last two bytes are the version and shouldn't be included in the checksum
            JagStream hashableStream = new JagStream();
            stream.Write(stream.ToArray(), 0, (int) stream.Length - 2);
            stream = hashableStream;

            //Calculate the new CRC checksum
            CRC32 crc = new CRC32();

            //Update the version and checksum for this file
            Entry entry = table.GetEntry(containerId);

            if(entry == null) {
                //Create a new entry for the file
                entry = new Entry(containerId);
                table.entries.Add(containerId, entry);
            }

            entry.SetVersion(container.GetVersion());
            entry.SetCrc(crc.GetCrc32(stream));
            
            //Calculate and update the whirlpool digest if we need to
            if((table.flags & ReferenceTable.FLAG_WHIRLPOOL) != 0) {
                byte[] whirlpool = Whirlpool.GetHash(stream.ToArray());
                entry.SetWhirlpool(whirlpool);
            }

            Container c = new Container(indexId, table.Encode());

            //Save the reference table
            store.Write(Constants.CRCTABLE_INDEX, indexId, c.Encode(), false);

            //Save the file itself
            store.Write(indexId, containerId, stream, false);
        }

        /// <summary>
        /// Reads the container from the index stream, decodes it,
        /// caches it for later and returns the container
        /// </summary>
        /// <param name="index">The indice type</param>
        /// <param name="containerId">The container index</param>
        /// <returns>Container with index <paramref name="containerId"/> from the specified <paramref name="index"/></returns>
        public Container GetContainer(int index, int containerId) {
            //If no container have yet been allocated for the type
            if(!containers.ContainsKey(index)) {
                DebugUtil.Debug("Creating new container dictionary for type " + index + ", fileCount: " + store.GetFileCount(index));
                containers.Add(index, new SortedDictionary<int, Container>());
            }

            //If the container has not yet been memoized
            if(!containers[index].ContainsKey(containerId)) {
                if(containerId < 0 || containerId > store.GetFileCount(index))
                    throw new FileNotFoundException("Could not find container type " + index);

                //Read the data from the index
                JagStream data = LoadContainer(index, containerId);

                //Decode the container
                Container container = Container.Decode(data);

                if(container == null)
                    throw new FileNotFoundException("Could not find container type " + index + ", file " + containerId);

                container.SetFile(containerId);
                container.SetType(index);
                containers[index].Add(containerId, container);
            }

            //Return the cached container
            //DebugUtil.Debug("Found index " + index + " container " + containerId);
            return containers[index][containerId];
        }

        /// <summary>
        /// Loads the container data from the RSIndex
        /// </summary>
        /// <param name="indexId">The index type</param>
        /// <param name="containerId">The container index</param>
        /// <returns>A <c>JagStream</c> containing the container data</returns>
        internal JagStream LoadContainer(int indexId, int containerId) {
            //Get the specified index
            Index index = GetIndex(indexId);

            //Find the beginning of the index
            long pos = containerId * Index.SIZE;

            if(pos < 0 || pos >= index.GetStream().Length)
                throw new FileNotFoundException("Position is out of bounds for type " + indexId + ", id " + containerId);

            //Seek to the relevant sector within the index
            index.GetStream().Seek(pos);

            //Read the index header, to get the size and sector ID
            index.ReadHeader();

            //If this happens, the cache was corrupted
            if(index.GetSize() < 0)
                return null;

            //If this happens, cache was corrupted
            if(index.GetSectorID() <= 0 || index.GetSectorID() > store.dataChannel.GetStream().Length / Sector.SIZE)
                return null;

            //Allocate buffers for the data and sector
            JagStream containerData = new JagStream(index.GetSize());

            int chunk = 0, remaining = index.GetSize();

            //Point to the start of the sector
            pos = index.GetSectorID() * Sector.SIZE;

            do {
                //Read from the data index into the sector buffer
                store.dataChannel.GetStream().Seek(pos);

                //Read in the sector from the data channel
                Sector sector = Sector.Decode(store.dataChannel.GetStream());

                if(remaining > Sector.DATA_LEN) {
                    //Cache this sector so far
                    containerData.Write(sector.GetData(), 0, Sector.DATA_LEN);

                    //And subtract the sector we read from data remaining
                    remaining -= Sector.DATA_LEN;

                    if(sector.GetType() != indexId)
                        throw new IOException("File type mismatch.");

                    if(sector.GetId() != containerId)
                        throw new IOException("File id mismatch.");

                    if(sector.GetChunk() != chunk++)
                        throw new IOException("Chunk mismatch.");

                    //Then move the pointer to the next sector
                    pos = sector.GetNextSector() * Sector.SIZE;
                } else {
                    //Otherwise if the amount remaining is less than the sector size, put it down
                    containerData.Write(sector.GetData(), 0, remaining);

                    //We've read the last sector in this index!
                    remaining = 0;
                }
            }
            while(remaining > 0);

            //Return the data stream back to it's original position
            store.dataChannel.GetStream().Seek(0);

            return containerData.Flip();
        }

        internal Index GetIndex(int type) {
            //Check if index is out of bounds
            if((type < 0 || type >= store.indexChannels.Length) && type != Constants.CRCTABLE_INDEX)
                throw new FileNotFoundException("Index " + type + " could not be found.");

            //Retrieve the index channel we are looking for
            return type == Constants.CRCTABLE_INDEX ? store.metaChannel : store.indexChannels[type];
        }

        /// <summary>
        /// Memoize all of the reference tables from the cache
        /// </summary>
        public void LoadReferenceTables() {
            //Prepare the references array
            referenceTables = new ReferenceTable[store.GetTypeCount()];

            //Attempt to load all of the reference tables
            for(int index = 0; index < store.GetTypeCount(); index++) {
                try {
                    GetReferenceTable(index);
                } catch(FileNotFoundException ex) {
                    DebugUtil.Debug(ex.Message);
                }
            }
        }

        /// <summary>
        /// Retrieve the memoized ReferenceTable from the cache if possible.
        /// Otherwise, memoize and return the specified ReferenceTable
        /// </summary>
        /// <param name="type">The reference table file</param>
        /// <returns></returns>

        public ReferenceTable GetReferenceTable(int type) {
            return GetReferenceTable(type, false);
        }

        public ReferenceTable GetReferenceTable(int containerId, bool reload) {
            //If the ReferenceTable has not yet been memoized
            if(reload || referenceTables[containerId] == null) {
                if(containerId < 0 || containerId > store.GetTypeCount())
                    throw new FileNotFoundException("ERROR - Reference table " + containerId + " out of bounds");

                //Get the container for the reference table
                Container container = GetContainer(Constants.CRCTABLE_INDEX, containerId);

                if(container == null)
                    throw new FileNotFoundException("ERROR - Reference table " + containerId + " is null");

                //Get the stream containing the Container data
                JagStream containerStream = container.GetStream();

                //Decode and cache the reference table
                if(container != null && containerStream.Length > 0 && containerStream.CanRead) {
                    referenceTables[containerId] = ReferenceTable.Decode(containerStream);
                    DebugUtil.Debug("Decoded reference table " + containerId);
                }
            }

            //Return the cached reference table
            return referenceTables[containerId];
        }

        public void UpdateContainer(int index) {
            DebugUtil.Debug("Updating index " + index + "...");

            //Make sure some stupid person didn't put an invalid index
            if(index < 0 || index > containers[index].Count)
                throw new FileNotFoundException("ERROR - Index out of bounds");

            //If the table container is null (doesn't exist, or not loaded properly, etc)
            if(containers[index] == null)
                throw new FileNotFoundException("ERROR - Container " + index + " is null");

            DebugUtil.Debug("(not rly) Updating index " + index + " containers...");
            for(int containerId = 0; containerId < containers[index].Count; containerId++) {
                if(!containers[index].ContainsKey(containerId))
                    throw new FileNotFoundException("ERROR - Only god knows...");

                DebugUtil.Debug("Updating index " + index + " container " + containerId);

                Container container = containers[index][containerId];
                container.UpdateStream(container.Encode());
            }

            DebugUtil.Debug("...FINISHED UPDATING INDEX " + index);
        }

        public void UpdateReferenceTable(int index) {
            DebugUtil.Debug("UPDATING REFERENCE TABLE " + index + "...");

            //Make sure some stupid person didn't put an invalid containerId
            if(index < 0 || index > store.GetTypeCount())
                throw new FileNotFoundException("\tERROR - Reference table " + index + " out of bounds");

            //If the reference table container is null (doesn't exist, or not loaded properly, etc)
            if(referenceTables[index] == null)
                throw new FileNotFoundException("\tERROR - Container " + index + " is null");

            //Update the stream based on the new reference table container data
            ReferenceTable refTable = referenceTables[index];
            refTable.UpdateStream(refTable.Encode());

            DebugUtil.Debug("...UPDATED REFERENCE TABLE INDEX " + index);
        }

        /// <summary>
        /// Retrieve the file from the <paramref name="index"/> index, entry <paramref name="file"/> in archive <paramref name="archive"/>
        /// </summary>
        /// <param name="index">The index to search</param>
        /// <param name="archive">The archive index</param>
        /// <param name="file">The individual file entry index</param>
        /// <returns>The entry within the archive</returns>
        internal Entry ReadEntry(int index, int archive, int file) {
            //Grab the container for the index and the reference table
            Container container = GetContainer(index, archive);

            //Get the corresponding ReferenceTable for this container type
            ReferenceTable refTable = GetReferenceTable(index);

            //Check if the file/member are valid
            Entry entry = refTable.GetEntry(archive);
            if(entry == null || file < 0 || file >= entry.GetValidFileIds().Length)
                throw new FileNotFoundException("\tUnable to find member " + file + ", in type " + index + ", file " + archive);

            //Get the archive for the entry
            Archive arc = GetArchive(container, entry.GetValidFileIds().Length);

            //Get the entry
            return arc.GetEntry(file);
        }

        /// <summary>
        /// Returns the memoized archive if possible. Decodes and memoizes if not yet already done.
        /// </summary>
        /// <param name="container">The container from which the archive is built</param>
        /// <param name="fileCount">The number of files contained in the archive</param>
        /// <returns>Returns the decoded archive instance</returns>
        internal Archive GetArchive(Container container, int fileCount) {
            //Get the container type and file indexes
            int type = container.GetType();
            int file = container.GetFile();

            //If the archive doesn't exist for the type, make it
            if(!archives.ContainsKey(type))
                archives.Add(type, new SortedDictionary<int, Archive>());

            //If the archive file has already been memoized, return it
            if(archives[type].ContainsKey(file))
                return archives[type][file];

            //Otherwise, construct the archive from the container
            Archive archive = Archive.Decode(container.GetStream(), fileCount);

            if(archive == null)
                DebugUtil.DebugWTF();

            archives[type].Add(file, archive);

            //Finally, return the archive
            return archive;
        }

        /// <summary>
        /// Gets the number of files of the specified type.
        /// </summary>
        /// <param name="type">The non-meta channel index</param>
        /// <returns>The total number of files of the specified type</returns>

        public int GetFileCount(int type) {
            return store.GetFileCount(type);
        }

        /// <summary>
        /// Returns the filestore for this cache
        /// </summary>
        /// <returns>The filestore for this cache</returns>
        internal FileStore GetStore() {
            return store;
        }

        public ItemDefinition GetItemDefinition(int archive, int entryId) {
            Entry entry = ReadEntry(Constants.ITEM_DEFINITIONS_INDEX, archive, entryId);
            ItemDefinition def = new ItemDefinition(entry.stream);
            def.setId(archive * 256 + entryId);
            //entry.stream.Clear();
            return def;
        }

        public SpriteDefinition GetSprite(int containerId) {
            //Get the sprite for the given entry
            Container container = GetContainer(Constants.SPRITES_INDEX, containerId);
            return SpriteDefinition.Decode(container.GetStream());
        }

        internal NPCDefinition GetNPCDefinition(int archive, int entry) {
            Entry npcStream = ReadEntry(Constants.NPC_DEFINITIONS_INDEX, archive, entry);
            NPCDefinition def = new NPCDefinition(npcStream.stream);
            def.setId(archive * 256 + entry);
            npcStream.stream.Clear();
            return def;
        }
    }
}
