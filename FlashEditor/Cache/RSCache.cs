using FlashEditor.cache.sprites;
using FlashEditor.utils;
using System;
using System.Collections.Generic;
using System.IO;

namespace FlashEditor.cache {
    class RSCache {
        public RSFileStore store;
        public RSReferenceTable[] referenceTables;
        public SortedDictionary<int, SortedDictionary<int, RSContainer>> containers = new SortedDictionary<int, SortedDictionary<int, RSContainer>>();
        public SortedDictionary<int, SortedDictionary<int, RSArchive>> archives = new SortedDictionary<int, SortedDictionary<int, RSArchive>>();

        public List<ItemDefinition> items = new List<ItemDefinition>();

        /// <summary>
        /// Create a new Cache instance, and automatically memoizes the archives and their reference tables
        /// </summary>
        /// <param name="store">The filestore</param>
        public RSCache(RSFileStore store) {
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

        ///Indexes are comprised of container data
        ///Containers are made up of archives
        ///Archives contain entries
        internal void WriteIndex(int indexId, int containerId, JagStream stream, bool overwrite, string directory) {
            /*
            foreach(KeyValuePair<int, SortedDictionary<int, RSContainer>> container in containers) {
                DebugUtil.Debug("ContainerID: " + container.Key);
                foreach(KeyValuePair<int, RSContainer> archive in container.Value) {
                    DebugUtil.Debug("ArchiveID: " + archive.Key);

                }
            }*/

            //Ensure that the indexId is for valid indexes only
            if((indexId < 0 || indexId >= store.indexChannels.Length) && indexId != 255)
                throw new FileNotFoundException("Unable to write index " + indexId);

            //The meta index to write to
            RSIndex indexChannel = indexId == 255 ? store.metaChannel : store.indexChannels[indexId];

            int nextSector = 0;
            long ptr = containerId * RSIndex.SIZE;

            JagStream buf;
            RSIndex index;

            if(overwrite) {
                if(ptr < 0)
                    throw new IOException();
                else if(ptr >= indexChannel.GetSize())
                    return;

                buf = new JagStream(indexChannel.Encode().ToArray());

                index = new RSIndex(buf.Flip());

                nextSector = index.GetSectorID();
                if(nextSector <= 0 || nextSector > store.dataChannel.GetSize() * (long) RSSector.SIZE)
                    return;
            } else {
                nextSector = (int) ((store.dataChannel.GetSize() + RSSector.SIZE - 1) / (long) RSSector.SIZE);
                if(nextSector == 0)
                    nextSector = 1;
            }

            bool extended = containerId > 0xFFFF;
            index = new RSIndex(stream.Remaining(), nextSector);

            //indexChannel.Write(index.Encode(), ptr);

            buf = new JagStream(RSSector.SIZE);

            int chunk = 0, remaining = index.GetSize();

            do {
                int curSector = nextSector;
                ptr = (long) curSector * (long) RSSector.SIZE;
                nextSector = 0;

                RSSector sector;
                if(overwrite) {
                    buf = new JagStream(store.dataChannel.Encode().ToArray());
                    sector = RSSector.Decode(buf.Flip());

                    if(sector.GetType() != indexId)
                        return;

                    if(sector.GetId() != containerId)
                        return;

                    if(sector.GetChunk() != chunk)
                        return;

                    nextSector = sector.GetNextSector();
                    if(nextSector < 0 || nextSector > store.dataChannel.GetSize() / (long) RSSector.SIZE)
                        return;
                }

                if(nextSector == 0) {
                    overwrite = false;
                    nextSector = (int) ((store.dataChannel.GetSize() + RSSector.SIZE - 1) / (long) RSSector.SIZE);
                    if(nextSector == 0)
                        nextSector++;
                    if(nextSector == curSector)
                        nextSector++;
                }
                int dataSize = RSSector.DATA_LEN;
                byte[] bytes = new byte[dataSize];
                if(remaining < dataSize) {
                    stream.Read(bytes, 0, remaining);
                    nextSector = 0; // mark as EOF
                    remaining = 0;
                } else {
                    remaining -= dataSize;
                    stream.Read(bytes, 0, dataSize);
                }

                sector = new RSSector(indexId, containerId, chunk++, nextSector, bytes);
                //store.dataChannel.write(sector.encode(), ptr);

            } while(remaining > 0);

            JagStream.Save(index.Encode(), directory);
        }

        public JagStream Encode(int type) {
            DebugUtil.Debug("Saving container: " + RSConstants.getContainerNameForType(type));

            //If there is no container to save
            if(!containers.ContainsKey(type)) {
                DebugUtil.Debug("Unable to write container " + type + ", does not exist");
                return null;
            }

            //Write the container file
            foreach(KeyValuePair<int, RSContainer> kvp in containers[type]) {
                DebugUtil.Debug("Encoding container " + kvp.Key);
                JagStream containerData = kvp.Value.Encode();
            }

            return null;
        }

        /// <summary>
        /// Memoizes the specified container, then returns it
        /// </summary>
        /// <param name="index">The indice type</param>
        /// <param name="containerId">The container index</param>
        /// <returns>Container with index <paramref name="containerId"/> from the specified <paramref name="index"/></returns>
        public RSContainer GetContainer(int index, int containerId) {
            //If no container have yet been allocated for the type
            if(!containers.ContainsKey(index)) {
                DebugUtil.Debug("Creating new container dictionary for type " + index + ", fileCount: " + store.GetFileCount(index));
                containers.Add(index, new SortedDictionary<int, RSContainer>());
            }

            //If the container has not yet been memoized
            if(!containers[index].ContainsKey(containerId)) {
                if(containerId < 0 || containerId > store.GetFileCount(index))
                    throw new FileNotFoundException("Could not find container type " + index);

                //Read the data from the index
                JagStream data = LoadContainer(index, containerId);

                //Decode the container
                RSContainer container = RSContainer.Decode(data);

                if(container == null)
                    throw new FileNotFoundException("Could not find container type " + index + ", file " + containerId);

                container.SetFile(containerId);
                container.SetType(index);
                containers[index].Add(containerId, container);
            }

            //Return the cached container
            DebugUtil.Debug("Found index " + index + " container " + containerId);
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
            RSIndex index = GetIndex(indexId);

            //Find the beginning of the index
            long pos = containerId * RSIndex.SIZE;

            if(pos < 0 || pos >= index.GetStream().Length)
                throw new FileNotFoundException("Position is out of bounds for type " + indexId + ", id " + containerId);

            //Seek to the relevant sector within the index
            index.GetStream().Seek(pos);

            //Read the index header, to get the size and sector ID
            index.ReadHeader();

            if(index.GetSize() < 0)
                return null;

            if(index.GetSectorID() <= 0 || index.GetSectorID() > store.dataChannel.GetStream().Length / RSSector.SIZE)
                return null;

            //Allocate buffers for the data and sector
            JagStream containerData = new JagStream(index.GetSize());

            int chunk = 0, remaining = index.GetSize();

            //Point to the start of the sector
            pos = index.GetSectorID() * RSSector.SIZE;

            do {
                //Read from the data index into the sector buffer
                store.dataChannel.GetStream().Seek(pos);

                //Read in the sector from the data channel
                RSSector sector = RSSector.Decode(store.dataChannel.GetStream());

                if(remaining > RSSector.DATA_LEN) {
                    //Cache this sector so far
                    containerData.Write(sector.GetData(), 0, RSSector.DATA_LEN);

                    //And subtract the sector we read from data remaining
                    remaining -= RSSector.DATA_LEN;

                    if(sector.GetType() != indexId)
                        throw new IOException("File type mismatch.");

                    if(sector.GetId() != containerId)
                        throw new IOException("File id mismatch.");

                    if(sector.GetChunk() != chunk++)
                        throw new IOException("Chunk mismatch.");

                    //Then move the pointer to the next sector
                    pos = sector.GetNextSector() * RSSector.SIZE;
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

        internal RSIndex GetIndex(int type) {
            //Check if index is out of bounds
            if((type < 0 || type >= store.indexChannels.Length) && type != 255)
                throw new FileNotFoundException("Index " + type + " could not be found.");

            //Retrieve the index channel we are looking for
            return type == 255 ? store.metaChannel : store.indexChannels[type];
        }

        /// <summary>
        /// Memoize all of the reference tables from the cache
        /// </summary>
        public void LoadReferenceTables() {
            //Prepare the references array
            referenceTables = new RSReferenceTable[store.GetTypeCount()];

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

        public RSReferenceTable GetReferenceTable(int type) {
            return GetReferenceTable(type, false);
        }

        public RSReferenceTable GetReferenceTable(int containerId, bool reload) {
            //If the ReferenceTable has not yet been memoized
            if(reload || referenceTables[containerId] == null) {
                if(containerId < 0 || containerId > store.GetTypeCount())
                    throw new FileNotFoundException("ERROR - Reference table " + containerId + " out of bounds");

                //Get the container for the reference table
                RSContainer container = GetContainer(255, containerId);

                if(container == null)
                    throw new FileNotFoundException("ERROR - Reference table " + containerId + " is null");

                //Get the stream containing the Container data
                JagStream containerStream = container.GetStream();

                //Decode and cache the reference table
                if(container != null && containerStream.Length > 0 && containerStream.CanRead) {
                    referenceTables[containerId] = RSReferenceTable.Decode(containerStream);
                    DebugUtil.Debug("Decoded reference table " + containerId);
                }
            }

            //Return the cached reference table
            return referenceTables[containerId];
        }

        public void UpdateIndex(int index) {
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

                RSContainer container = containers[index][containerId];
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
            RSReferenceTable refTable = referenceTables[index];
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
        internal JagStream ReadEntry(int index, int archive, int file) {
            //Grab the container for the index and the reference table
            RSContainer container = GetContainer(index, archive);

            //Get the corresponding ReferenceTable for this container type
            RSReferenceTable table = GetReferenceTable(index);

            //Check if the file/member are valid
            RSEntry entry = table.GetEntry(archive);
            if(entry == null || file < 0 || file >= entry.GetValidFileIds().Length)
                throw new FileNotFoundException("Unable to find member " + file + ", in type " + index + ", file " + archive);

            //Get the archive for the type, file
            RSArchive arc = GetArchive(container, entry.GetValidFileIds().Length);

            return arc.GetEntry(file);
        }

        /// <summary>
        /// Returns the memoized archive if possible. Decodes and memoizes if not yet already done.
        /// </summary>
        /// <param name="container">The container from which the archive is built</param>
        /// <param name="fileCount">The number of files contained in the archive</param>
        /// <returns>Returns the decoded archive instance</returns>
        internal RSArchive GetArchive(RSContainer container, int fileCount) {
            //Get the container type and file indexes
            int type = container.GetType();
            int file = container.GetFile();

            //If the archive doesn't exist for the type, make it
            if(!archives.ContainsKey(type))
                archives.Add(type, new SortedDictionary<int, RSArchive>());

            //If the archive file has already been memoized, return it
            if(archives[type].ContainsKey(file))
                return archives[type][file];

            //Otherwise, construct the archive from the container
            RSArchive archive = RSArchive.Decode(container.GetStream(), fileCount);
            if(archive == null)
                DebugUtil.Debug("wat the fuck");

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
        internal RSFileStore GetStore() {
            return store;
        }

        public ItemDefinition GetItemDefinition(int archive, int file) {
            JagStream itemStream = ReadEntry(RSConstants.ITEM_DEFINITIONS_INDEX, archive, file);
            ItemDefinition def = new ItemDefinition(itemStream);
            def.setId(archive * 256 + file);
            itemStream.Clear();
            return def;
        }

        public SpriteDefinition GetSprite(int containerId) {
            //Get the sprite for the given entry
            RSContainer container = GetContainer(RSConstants.SPRITES_INDEX, containerId);
            return SpriteDefinition.Decode(container.GetStream());
        }

        internal NPCDefinition GetNPCDefinition(int archive, int file) {
            JagStream npcStream = ReadEntry(RSConstants.NPC_DEFINITIONS_INDEX, archive, file);
            NPCDefinition def = new NPCDefinition(npcStream);
            def.setId(archive * 256 + file);
            npcStream.Clear();
            return def;
        }
    }
}
