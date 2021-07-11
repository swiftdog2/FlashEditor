using FlashEditor.cache.sprites;
using FlashEditor.Cache.Util;
using FlashEditor.utils;
using Ionic.Crc;
using System;
using System.Collections.Generic;
using System.IO;

namespace FlashEditor.cache {
    class RSCache {
        public RSFileStore store;
        public RSReferenceTable[] referenceTables;

        //Each index has their own set of containers
        public SortedDictionary<int, SortedDictionary<int, RSContainer>> containers = new SortedDictionary<int, SortedDictionary<int, RSContainer>>();

        public SortedDictionary<int, ItemDefinition> items = new SortedDictionary<int, ItemDefinition>();

        /// <summary>
        /// Create a new Cache instance, and automatically memoizes the archives and their reference tables
        /// </summary>
        /// <param name="store">The filestore</param>
        public RSCache(RSFileStore store) {
            this.store = store;
            LoadReferenceTables();
        }

        internal void WriteCache() {
            DebugUtil.Debug(@"   _____             _                   ");
            DebugUtil.Debug(@"  / ____|           (_)                  ");
            DebugUtil.Debug(@" | (___   __ ___   ___ _ __   __ _       ");
            DebugUtil.Debug(@"  \___ \ / _` \ \ / / | '_ \ / _` |      ");
            DebugUtil.Debug(@"  ____) | (_| |\ V /| | | | | (_| |_ _ _ ");
            DebugUtil.Debug(@" |_____/ \__,_| \_/ |_|_| |_|\__, (_|_|_)");
            DebugUtil.Debug(@"                              __/ |      ");
            DebugUtil.Debug(@"                             |___/       ");

            WriteDataIndex();
            WriteReferenceTable();
            WriteIndexes();
        }

        /// <summary>
        /// Write the main data (dat2) to file
        /// </summary>
        /// <param name="type">The index type</param>
        /// <returns>Whether or not the index was successfully written</returns>
        internal void WriteDataIndex() {
            DebugUtil.Debug("Saving Data Index...");
            JagStream.Save(store.dataChannel.GetStream(), RSConstants.CACHE_OUTPUT_DIRECTORY + "main_file_cache.dat2");
        }

        /// <summary>
        /// Write the index stream to a file
        /// </summary>
        /// <param name="type">The index type</param>
        /// <returns>Whether or not the index was successfully written</returns>
        internal void WriteReferenceTable() {
            DebugUtil.Debug("Saving Reference Tables...");
            WriteIndex(store.metaChannel, RSConstants.CACHE_OUTPUT_DIRECTORY + "main_file_cache.idx255");
        }

        /// <summary>
        /// Write the index stream to a file
        /// </summary>
        /// <param name="type">The index type</param>
        /// <returns>Whether or not the index was successfully written</returns>
        internal void WriteIndexes() {
            DebugUtil.Debug("Saving meta indexes...");

            //Write the index channels
            for(int index = 0; index < GetStore().indexChannels.Length; index++)
                WriteIndex(GetStore().indexChannels[index], RSConstants.CACHE_OUTPUT_DIRECTORY + "main_file_cache.idx" + index);
        }

        //Simply saves the index encoding to file...
        internal void WriteIndex(RSIndex index, string directory) {
            JagStream.Save(index.Encode(), directory);
        }

        /**
        * Writes a file to the cache and updates the ReferenceTable that it is associated with.
        */
        internal void WriteIndex(int indexId, int containerId, RSContainer container) {
            if(indexId == RSConstants.CRCTABLE_INDEX)
                return;

            //Get the reference table container
            RSContainer tableContainer = GetContainer(RSConstants.CRCTABLE_INDEX, indexId);

            //Decode the reference table for this index
            RSReferenceTable table = GetReferenceTable(indexId);

            //Grab the bytes we need for the checksum
            JagStream stream = container.Encode();

            //Last two bytes are the version and shouldn't be included in the checksum
            JagStream hashableStream = new JagStream();
            stream.Write(stream.ToArray(), 0, (int) stream.Length - 2);
            stream = hashableStream;

            //Calculate the new CRC checksum
            CRC32 crc = new CRC32();

            //Update the version and checksum for this file
            RSEntry entry = table.GetEntry(containerId);

            if(entry == null) {
                //Create a new entry for the file
                entry = new RSEntry(containerId);
                table.entries.Add(containerId, entry);
            }

            entry.SetVersion(container.GetVersion());

            //Calculate the CRC
            int crcValue = crc.GetCrc32(stream);
            entry.SetCrc(crcValue);

            //Calculate and update the whirlpool digest if we need to
            if(table.usesWhirlpool) {
                byte[] whirlpool = Whirlpool.GetHash(stream.ToArray());
                entry.SetWhirlpool(whirlpool);
            }

            //Save the reference table
            tableContainer = new RSContainer(tableContainer.GetType(), table.Encode());
            store.Write(RSConstants.CRCTABLE_INDEX, indexId, tableContainer.Encode(), false);

            //Save the file itself
            store.Write(indexId, containerId, stream, true);
        }

        /**
         * Writes a file contained in an archive to the cache.
         * 
         * @param type
         *            The type of file.
         * @param file
         *            The id of the archive.
         * @param member
         *            The file within the archive.
         * @param data
         *            The data to write.
         * @param keys
         *            The encryption keys.
         * @throws IOException
         *             if an I/O error occurs.
         */
        public void Write(int index, int containerId, int member, JagStream data) {
            //Grab the container
            RSContainer tableContainer = GetContainer(index, containerId);

            //Grab the reference table
            RSReferenceTable table = GetReferenceTable(index);

            //Create a new entry if necessary
            RSEntry entry = table.entries[containerId];
            DebugUtil.Debug("Entry " + containerId + " is " + (entry == null ? "NULLLLLLLLL" : "valid"));

            int oldArchiveSize = -1;
            if(entry == null) {
                entry = new RSEntry(containerId);
                table.entries.Add(containerId, entry);
                DebugUtil.Debug("Created new entry " + containerId);
            } else {
                oldArchiveSize = entry.Capacity();
                DebugUtil.Debug("Old archive size: " + entry.Capacity());
            }

            //Add a child entry if one does not exist
            RSChildEntry child = entry.childEntries[member];
            DebugUtil.Debug("Child Entry " + member + " is " + (child == null ? "NULLLLLLLLL" : "valid"));

            if(child == null) {
                child = new RSChildEntry(member);
                entry.childEntries.Add(member, child);
                DebugUtil.Debug("Added new child entry " + member);
            }

            //Extract the current archive into memory so we can modify it
            RSArchive archive = tableContainer.GetArchive();
            RSContainer container = GetContainer(index, containerId);

            int containerType, containerVersion;
            if(member < store.GetFileCount(index) && oldArchiveSize != -1) {
                containerType = container.GetType();
                containerVersion = container.GetVersion();
                archive = RSArchive.Decode(container.GetStream(), oldArchiveSize);
            } else {
                containerType = RSConstants.GZIP_COMPRESSION;
                containerVersion = 1;
                archive = new RSArchive(member + 1);
            }

            if(archive == null) {
                DebugUtil.Debug("Container does not have a valid archive... you need more sleep.");
                archive = new RSArchive(member + 1);
            }

            //Expand the archive if it is not large enough
            if(member >= archive.entries.Length) {
                DebugUtil.Debug("Archive entries too small, expanding...");
                //Copy the entries to a new archive
                RSArchive newArchive = new RSArchive(member + 1);
                for(int id = 0; id < archive.entries.Length; id++)
                    newArchive.entries[id] = archive.GetEntry(id);
                archive = newArchive;
            }

            //Put the member into the archive
            archive.entries[member] = new RSEntry(data);
            DebugUtil.Debug("Updated entry " + member);

            //Create 'dummy' entries 
            for(int id = 0; id < archive.entries.Length; id++) {
                if(archive.GetEntry(id) == null) {
                    entry.childEntries.Add(id, new RSChildEntry(id));
                    archive.entries[id] = new RSEntry(id);
                    DebugUtil.Debug("Added dummy child entry + " + id + " to archive");
                }
            }

            //Write the reference table out again
            JagStream tableStream = table.Encode();
            JagStream containerStream = tableContainer.Encode();

            DebugUtil.Debug("Reference table stream is " + tableStream.Length + " bytes");
            DebugUtil.Debug("container stream is " + containerStream.Length + " bytes");

            tableContainer = new RSContainer(tableContainer.GetType(), tableStream);
            store.Write(RSConstants.CRCTABLE_INDEX, index, containerStream, false);

            //And write the archive back to memory
            container = new RSContainer(containerType, archive.Encode(), containerVersion);
            WriteIndex(index, containerId, container);
        }


        /// <summary>
        /// Reads the container from the index stream, decodes it,
        /// caches it for later and returns the container
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

                //Decode the container's archive data if possible
                if(index != RSConstants.CRCTABLE_INDEX) {
                    RSReferenceTable refTable = GetReferenceTable(index);

                    //Check if the file/member are valid
                    RSEntry entry = refTable.GetEntry(containerId);

                    if(entry == null)
                        throw new FileNotFoundException("\tUnable to find entry " + containerId + " in reference table.");

                    //Decode the container's archive
                    container.SetArchive(RSArchive.Decode(container.GetStream(), entry.GetValidFileIds().Length));
                }

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
            RSIndex index = GetIndex(indexId);

            //Find the beginning of the index
            long pos = containerId * RSIndex.SIZE;

            if(pos < 0 || pos >= index.GetStream().Length)
                throw new FileNotFoundException("Position is out of bounds for type " + indexId + ", id " + containerId);

            //Seek to the container within the index
            index.GetStream().Seek(pos);

            //Read the index header, to get the size and sector ID
            index.ReadHeader();

            //If this happens, the cache was corrupted
            if(index.GetSize() < 0)
                return null;

            //If this happens, cache was corrupted
            if(index.GetSectorID() <= 0 || index.GetSectorID() > store.dataChannel.GetStream().Length / RSSector.SIZE)
                return null;

            //Allocate buffers for the data and sector
            JagStream containerData = new JagStream(index.GetSize());

            int chunk = 0, remaining = index.GetSize();

            //Point to the start of the sector
            pos = index.GetSectorID() * RSSector.SIZE;

            while(remaining > 0) {
                //Read from the data index into the sector buffer
                store.dataChannel.GetStream().Seek(pos);

                //Read in the sector from the data channel
                RSSector sector = RSSector.Decode(store.dataChannel.GetStream());

                if(remaining > RSSector.DATA_LEN) {
                    //Cache this sector so far
                    containerData.Write(sector.GetData(), 0, RSSector.DATA_LEN);

                    //And subtract the sector we read from data remaining
                    remaining -= RSSector.DATA_LEN;

                    //Basically the cache was corrupted
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

            //Return the data stream back to it's original position
            store.dataChannel.GetStream().Seek(0);

            return containerData.Flip();
        }

        internal RSIndex GetIndex(int type) {
            //Check if index is out of bounds
            if((type < 0 || type >= store.indexChannels.Length) && type != RSConstants.CRCTABLE_INDEX)
                throw new FileNotFoundException("Index " + type + " could not be found.");

            //Retrieve the index channel we are looking for
            return type == RSConstants.CRCTABLE_INDEX ? store.metaChannel : store.indexChannels[type];
        }

        /// <summary>
        /// Memoize all of the reference tables from the cache
        /// </summary>
        public void LoadReferenceTables() {
            //Prepare the references array
            referenceTables = new RSReferenceTable[store.GetTypeCount()];

            DebugUtil.Debug(@"  _                     _ _               _____       __ _______    _     _           ");
            DebugUtil.Debug(@" | |                   | (_)             |  __ \     / _|__   __|  | |   | |          ");
            DebugUtil.Debug(@" | |     ___   __ _  __| |_ _ __   __ _  | |__) |___| |_   | | __ _| |__ | | ___  ___ ");
            DebugUtil.Debug(@" | |    / _ \ / _` |/ _` | | '_ \ / _` | |  _  // _ \  _|  | |/ _` | '_ \| |/ _ \/ __|");
            DebugUtil.Debug(@" | |___| (_) | (_| | (_| | | | | | (_| | | | \ \  __/ |    | | (_| | |_) | |  __/\__ \");
            DebugUtil.Debug(@" |______\___/ \__,_|\__,_|_|_| |_|\__, | |_|  \_\___|_|    |_|\__,_|_.__/|_|\___||___/");
            DebugUtil.Debug(@"                                   __/ |                                              ");
            DebugUtil.Debug(@"                                  |___/                                               ");

            //Attempt to load all of the reference tables
            for(int index = 0; index < store.GetTypeCount(); index++) {
                try {
                    DebugUtil.Debug("Loading reference table " + index);
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
        /// <param name="index">The reference table index</param>
        /// <returns></returns>

        public RSReferenceTable GetReferenceTable(int index) {
            return GetReferenceTable(index, false);
        }

        public RSReferenceTable GetReferenceTable(int index, bool reload) {
            //If the ReferenceTable has been cached, return it
            if(index < referenceTables.Length && referenceTables[index] != null)
                return referenceTables[index];

            if(index < 0 || index > store.GetTypeCount())
                throw new FileNotFoundException("ERROR - Reference table " + index + " out of bounds");

            //Get the container for the reference table
            RSContainer container = GetContainer(RSConstants.CRCTABLE_INDEX, index);

            if(container == null)
                throw new FileNotFoundException("ERROR - Reference table " + index + " is null");

            //Get the stream containing the Container data
            JagStream containerStream = container.GetStream();

            //Decode and cache the reference table
            if(container != null && containerStream.Length > 0 && containerStream.CanRead) {
                referenceTables[index] = RSReferenceTable.Decode(containerStream);
                DebugUtil.Debug("...Decoded reference table " + index);
            }

            return referenceTables[index];
        }

        /// <summary>
        /// Retrieve the file from the <paramref name="index"/> index, entry <paramref name="file"/> in archive <paramref name="archive"/>
        /// </summary>
        /// <param name="index">The index to search</param>
        /// <param name="archive">The archive index</param>
        /// <param name="file">The individual file entry index</param>
        /// <returns>The entry within the archive</returns>
        internal RSEntry ReadEntry(int index, int archive, int file) {
            //Grab the container for the index and the reference table
            RSContainer container = GetContainer(index, archive);

            //Get the corresponding ReferenceTable for this container type
            RSReferenceTable refTable = GetReferenceTable(index);

            //Check if the file/member are valid
            RSEntry entry = refTable.GetEntry(archive);
            if(entry == null || file < 0 || file >= entry.GetValidFileIds().Length)
                throw new FileNotFoundException("\tUnable to find member " + file + ", in type " + index + ", file " + archive);

            //Get the Entry from the Container's Archive
            return container.GetArchive().GetEntry(file);
        }

        /// <summary>
        /// Returns the memoized archive if possible. Decodes and memoizes if not yet already done.
        /// </summary>
        /// <param name="container">The container from which the archive is built</param>
        /// <param name="fileCount">The number of files contained in the archive</param>
        /// <returns>Returns the decoded archive instance</returns>
        internal RSArchive GetArchive(RSContainer container, int fileCount) {
            //If the container archive has been decoded
            if(container.GetArchive() != null)
                return container.GetArchive();

            //Otherwise, decode the archive from the container data
            RSArchive archive = RSArchive.Decode(container.GetStream(), fileCount);

            //Cache the archive
            container.SetArchive(archive);

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

        public ItemDefinition GetItemDefinition(int archive, int entryId) {
            RSEntry entry = ReadEntry(RSConstants.ITEM_DEFINITIONS_INDEX, archive, entryId);
            ItemDefinition def = new ItemDefinition(entry.stream);
            def.SetId(archive * 256 + entryId);
            //entry.stream.Clear();
            return def;
        }

        public SpriteDefinition GetSprite(int containerId) {
            //Get the sprite for the given entry
            RSContainer container = GetContainer(RSConstants.SPRITES_INDEX, containerId);
            return SpriteDefinition.Decode(container.GetStream());
        }

        internal NPCDefinition GetNPCDefinition(int archive, int entry) {
            RSEntry npcStream = ReadEntry(RSConstants.NPC_DEFINITIONS_INDEX, archive, entry);
            NPCDefinition def = new NPCDefinition(npcStream.stream);
            def.SetId(archive * 256 + entry);
            npcStream.stream.Clear();
            return def;
        }
    }
}
