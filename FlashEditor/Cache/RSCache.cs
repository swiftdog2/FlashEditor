using FlashEditor.cache.sprites;
using FlashEditor.utils;
using System;
using System.Collections.Generic;
using System.IO;

namespace FlashEditor.cache {
    class RSCache {
        private RSFileStore store;
        private RSReferenceTable[] references;
        private SortedDictionary<int, SortedDictionary<int, RSContainer>> containers = new SortedDictionary<int, SortedDictionary<int, RSContainer>>();
        private SortedDictionary<int, SortedDictionary<int, RSArchive>> archives = new SortedDictionary<int, SortedDictionary<int, RSArchive>>();

        /// <summary>
        /// Create a new Cache instance, and automatically memoizes the archives and their reference tables
        /// </summary>
        /// <param name="store">The filestore</param>
        public RSCache(RSFileStore store) {
            this.store = store;
            LoadReferenceTables();
        }

        /// <summary>
        /// Decodes and memoizes the specified container, then returns it
        /// </summary>
        /// <param name="type">The indice type</param>
        /// <param name="file">The container index</param>
        /// <returns>Container with index <paramref name="file"/> from the specified <paramref name="type"/></returns>
        public RSContainer GetContainer(int type, int file) {
            //If no container have yet been allocated for the type
            if(!containers.ContainsKey(type)) {
                DebugUtil.Debug("Creating new container dictionary for type " + type + ", fileCount: " + store.GetFileCount(type));
                containers.Add(type, new SortedDictionary<int, RSContainer>());
            }

            //If the container has not yet been memoized
            if(!containers[type].ContainsKey(file)) {
                if(file < 0 || file > store.GetFileCount(type))
                    throw new FileNotFoundException("Could not find container type " + type);

                //Read the data from the index
                JagStream data = store.GetContainer(type, file);

                //Decode the container
                RSContainer container = RSContainer.Decode(data);

                if(container == null)
                    throw new FileNotFoundException("Could not find container type " + type + ", file " + file);

                container.SetFile(file);
                container.SetType(type);
                containers[type].Add(file, container);
            }

            //Return the cached container
            return containers[type][file];
        }

        public bool WriteContainer(int type) {
            DebugUtil.Debug("Saving container: " + type);

            //If no container have yet been allocated for the type
            if(!containers.ContainsKey(type)) {
                DebugUtil.Debug("Unable to write container " + type + ", does not exist");
                return false;
            }

            //Rewrite the container file
            foreach(KeyValuePair<int, RSContainer> kvp in containers[type]) {
                RSContainer container = kvp.Value;

                DebugUtil.Debug(kvp.Key + " : " + container.GetLength());
            }

            return true;
        }

        /// <summary>
        /// Memoize all of the reference tables from the cache
        /// </summary>
        public void LoadReferenceTables() {
            //Prepare the references array
            references = new RSReferenceTable[store.GetTypeCount()];

            //Attempt to load all of the reference tables
            for(int type = 0; type < store.GetTypeCount(); type++) {
                try {
                    GetReferenceTable(type);
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

        public RSReferenceTable GetReferenceTable(int type, bool reload) {
            //If the ReferenceTable has not yet been memoized
            if(reload || references[type] == null) {
                if(type < 0 || type > store.GetTypeCount())
                    throw new FileNotFoundException("ERROR - Reference table " + type + " out of bounds");

                //Get the container for the reference table
                RSContainer container = GetContainer(255, type);

                if(container == null)
                    throw new FileNotFoundException("ERROR - Reference table " + type + " is null");

                //Get the stream containing the Container data
                JagStream containerStream = container.GetStream();

                //Decode and cache the reference table
                if(container != null && containerStream.Length > 0 && containerStream.CanRead) {
                    references[type] = RSReferenceTable.Decode(containerStream);
                    DebugUtil.Debug("Decoded reference table " + type);
                }
            }

            //Return the cached reference table
            return references[type];
        }

        /// <summary>
        /// Retrieve the file from the <paramref name="type"/> index, entry <paramref name="member"/> in archive <paramref name="file"/>
        /// </summary>
        /// <param name="type">The index to search</param>
        /// <param name="file">The container index</param>
        /// <param name="member">The individual file entry index</param>
        /// <returns>The entry within the archive</returns>
        internal JagStream ReadEntry(int type, int file, int member) {
            //Grab the container for the index and the reference table
            RSContainer container = GetContainer(type, file);

            //Get the corresponding ReferenceTable for this container type
            RSReferenceTable table = GetReferenceTable(type);

            //Check if the file/member are valid
            RSEntry entry = table.GetEntry(file);
            if(entry == null || member < 0 || member >= entry.GetValidFileIds().Length)
                throw new FileNotFoundException("Unable to find member " + member + ", in type " + type + ", file " + file);

            //Get the archive for the type, file
            RSArchive archive = GetArchive(container, entry.GetValidFileIds().Length);

            return archive.GetEntry(member);
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

        public SpriteDefinition GetSprite(int index) {
            //Get the sprite for the given entry
            RSContainer container = GetContainer(RSConstants.SPRITES_INDEX, index);
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
