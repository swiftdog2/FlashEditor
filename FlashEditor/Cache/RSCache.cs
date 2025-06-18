using FlashEditor.cache.sprites;
using FlashEditor.Cache.Util;
using FlashEditor.Definitions;
using FlashEditor.utils;
using ICSharpCode.SharpZipLib.Checksum;
using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using static FlashEditor.utils.DebugUtil;

namespace FlashEditor.cache
{
    class RSCache
    {
        public RSFileStore store;
        public RSReferenceTable[] referenceTables;

        //Each index has their own set of containers
        public SortedDictionary<int, SortedDictionary<int, RSContainer>> containers = new SortedDictionary<int, SortedDictionary<int, RSContainer>>();

        //Better to make generic type Definition and store each of them under their respective indexes but that's for later anyway
        public SortedDictionary<int, ItemDefinition> items = new SortedDictionary<int, ItemDefinition>();
        public SortedDictionary<int, ObjectDefinition> objects = new SortedDictionary<int, ObjectDefinition>();
        public SortedDictionary<int, NPCDefinition> npcs = new SortedDictionary<int, NPCDefinition>();

        public SortedDictionary<int, ModelDefinition> models = new SortedDictionary<int, ModelDefinition>();

        /// <summary>
        /// Create a new Cache instance, and automatically memoizes the archives and their reference tables
        /// </summary>
        /// <param name="store">The filestore</param>
        public RSCache(RSFileStore store)
        {
            this.store = store;
            LoadReferenceTables();
        }

        internal void WriteCache()
        {
            Debug(@"   _____             _                   ");
            Debug(@"  / ____|           (_)                  ");
            Debug(@" | (___   __ ___   ___ _ __   __ _       ");
            Debug(@"  \___ \ / _` \ \ / / | '_ \ / _` |      ");
            Debug(@"  ____) | (_| |\ V /| | | | | (_| |_ _ _ ");
            Debug(@" |_____/ \__,_| \_/ |_|_| |_|\__, (_|_|_)");
            Debug(@"                              __/ |      ");
            Debug(@"                             |___/       ");

            SaveDataIndex();
            SaveIndexes();
        }

        /// <summary>
        /// Write the main data (dat2) to file
        /// </summary>
        /// <param name="type">The index type</param>
        /// <returns>Whether or not the index was successfully written</returns>
        internal void SaveDataIndex()
        {
            SaveIndex(store.dataChannel, "dat2");
        }

        /// <summary>
        /// Write the index stream to a file
        /// </summary>
        /// <param name="type">The index type</param>
        /// <returns>Whether or not the index was successfully written</returns>
        internal void SaveIndexes()
        {
            var sb = new StringBuilder();
            foreach (KeyValuePair<int, RSIndex> index in GetStore().indexChannels)
            {
                sb.Clear();
                sb.Append("idx");
                sb.Append(index.Key);
                SaveIndex(index.Value, sb.ToString());
            }
        }

        internal void SaveIndex(RSIndex index, string directory)
        {
            Debug("Saving " + directory);
            JagStream.Save(index.GetStream(), RSConstants.CACHE_OUTPUT_DIRECTORY + "main_file_cache." + directory);
        }

        /// <summary>
        /// Writes a file contained in an archive to the cache.
        /// </summary>
        /// <param name="type">The index type</param>
        /// <param name="containerId">The container within the index</param>
        /// <param name="entryId">The entry id within the archive</param>
        /// <param name="data">The encoded entry</param>
        public void WriteEntry(int type, int containerId, int entryId, JagStream data)
        {
            if (type == RSConstants.META_INDEX)
                throw new IOException("Reference tables can only be modified with the low level FileStore API!");

            Debug("Writing Entry " + type + "," + containerId + "," + entryId);

            //Get the reference table for the index
            RSReferenceTable table = GetReferenceTable(type);
            RSEntry entry;

            //Retrieve the appropriate entry in the reference table
            if (table.entries.ContainsKey(containerId))
            {
                entry = table.GetEntry(containerId);
                Debug("Found Entry for RefTable(index " + type + ", container " + containerId + ", entry " + entryId + ")", LOG_DETAIL.INSANE);
            }
            else
            {
                //Expand the reference table to add a new container entry, if necessary
                entry = new RSEntry(containerId);
                Debug("Generating entry for RefTable(" + type + ", " + containerId + ", entry " + entryId, LOG_DETAIL.INSANE);
            }

            //Add a child entry if one does not exist
            //In the context of a reference table, this is the archive ID
            if (entry.GetChildEntry(entryId) == null)
            {
                entry.PutEntry(entryId, new RSChildEntry(entryId));
                Debug("Added new child entry " + entryId, LOG_DETAIL.INSANE);
            }

            RSContainer container = GetContainer(type, containerId);

            //Generate a new container, if necessary
            if (container == null)
            {
                Debug("Added new container", LOG_DETAIL.INSANE);
                container = new RSContainer(type, containerId, RSConstants.GZIP_COMPRESSION, null, 1337);
            }

            //Create a new archive for the container, if necessary
            RSArchive archive = container.GetArchive();
            if (archive == null)
            {
                Debug("Added new archive", LOG_DETAIL.INSANE);
                container.SetArchive(archive = new RSArchive());
            }

            //Create or update the entry in the container archive
            archive.PutEntry(entryId, data);

            //Wrap the archive back into a container
            container.SetStream(archive.Encode());

            //Create 'dummy' entries
            for (int id = 0; id < archive.EntryCount(); id++)
            {
                if (archive.GetEntry(id) == null)
                {
                    entry.PutEntry(id, new RSChildEntry(id));
                    archive.PutEntry(id, new JagStream(1));
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
            entry.SetCrc((int)crc.Value);              // .Value is UInt32
            entry.SetVersion(1337);

            //Calculate and update the whirlpool digest if we need to
            if (table.usesWhirlpool)
            {
                byte[] digest = Whirlpool.ComputeHash(hashableStream.ToArray());
                entry.SetWhirlpool(digest);
            }

            //Add the entry to the reference table
            table.PutEntry(containerId, entry);

            //Update the reference table stream
            //RSContainer tableContainer = GetContainer(RSConstants.META_INDEX, type);
            //tableContainer.SetStream(table.Encode());

            //Write out the reference table
            RSContainer tableContainer = new RSContainer(RSConstants.META_INDEX, type, RSConstants.GZIP_COMPRESSION, ReferenceTableCodec.Encode(table), 1337);
            store.Write(RSConstants.META_INDEX, type, tableContainer.Encode());
            store.Write(type, containerId, stream);
        }

        /// <summary>
        /// Reads the container from the index stream, decodes it,
        /// caches it for later and returns the container
        /// </summary>
        /// <param name="type">The indice type</param>
        /// <param name="containerId">The container index</param>
        /// <returns>Container with index <paramref name="containerId"/> from the specified <paramref name="type"/></returns>
        public RSContainer GetContainer(int type, int containerId)
        {
            if (containerId < 0 || containerId >= store.GetFileCount(type))
                throw new FileNotFoundException("Could not find container type " + type);

            //Initialise the container dictionary
            if (!containers.ContainsKey(type))
                containers.Add(type, new SortedDictionary<int, RSContainer>());

            //Return the container if already cached
            if (containers[type].ContainsKey(containerId))
                return containers[type][containerId];

            //Read the data from the index
            JagStream data = LoadContainer(type, containerId);

            //Decode the container
            RSContainer container = RSContainer.Decode(data);

            if (container == null)
                throw new FileNotFoundException("NULL CONTAINER? (type: " + type + ", container: " + containerId + ")");

            container.SetType(type);
            container.SetId(containerId);

            //Cache the container for later usage
            containers[type].Add(containerId, container);

            return containers[type][containerId];
        }

        public void UpdateRSContainer(int type, int containerId, RSContainer container)
        {
            if (!containers.ContainsKey(type))
                containers.Add(type, new SortedDictionary<int, RSContainer>());

            //Return the container if already cached
            if (containers[type].ContainsKey(containerId))
                containers[type][containerId] = container;
            else
                containers[type].Add(containerId, container);
        }

        public void UpdateReferenceTable(int type, RSReferenceTable refTable)
        {
            if (type < 0 || type > referenceTables.Length)
                throw new IndexOutOfRangeException("Invalid type when updating reference table cache");
            referenceTables[type] = refTable;
        }

        /// <summary>
        /// Loads the container data from the RSIndex
        /// </summary>
        /// <param name="type">The index type</param>
        /// <param name="containerId">The container index</param>
        /// <returns>A <c>JagStream</c> containing the container data</returns>
        internal JagStream LoadContainer(int type, int containerId)
        {
            Debug("Loading type " + type + ", container " + containerId, LOG_DETAIL.ADVANCED);
            RSIndex index = store.GetIndex(type);

            //Find the beginning of the index
            long pos = containerId * RSIndex.SIZE;

            if (pos < 0 || pos >= index.GetStream().Length)
                throw new FileNotFoundException("Position is out of bounds for type " + type + ", id " + containerId);

            //Read the container header, to get the container size and sector ID
            index.ReadContainerHeader(containerId);

            //If the sector could not be located in the data stream
            if (index.GetSectorID() <= 0 || index.GetSectorID() > store.dataChannel.GetStream().Length / RSSector.SIZE)
                return null;

            //Allocate buffers for the data and sector
            JagStream containerData = new JagStream(index.GetSize());

            int chunk = 0;
            int remaining = index.GetSize();
            int sectorId = index.GetSectorID();

            //Point to the start of the sector
            pos = sectorId * RSSector.SIZE;

            do
            {
                //Read from the data index into the sector buffer
                store.dataChannel.GetStream().Seek(pos);

                Debug("\tReading sector " + sectorId + " @ " + pos, LOG_DETAIL.INSANE);

                byte[] sectorData = store.dataChannel.GetStream().ReadBytes(RSSector.SIZE);

                //Read in the sector from the data channel
                RSSector sector = RSSector.Decode(new JagStream(sectorData));

                if (remaining > RSSector.DATA_LEN)
                {
                    //Cache this sector so far
                    containerData.Write(sector.GetData(), 0, RSSector.DATA_LEN);

                    //And subtract the sector we read from data remaining
                    remaining -= RSSector.DATA_LEN;

                    //Basically the cache was corrupted
                    if (sector.GetType() != type)
                        throw new IOException("File type mismatch, " + sector.GetType() + ", " + type);
                    if (sector.GetId() != containerId)
                        throw new IOException("File id mismatch, " + sector.GetId() + ", " + containerId);
                    if (sector.GetChunk() != chunk++)
                        throw new IOException("Chunk mismatch, " + sector.GetChunk() + ", " + chunk);

                    //Then move the pointer to the next sector
                    pos = (sectorId = sector.GetNextSector()) * RSSector.SIZE;
                }
                else
                {
                    //Otherwise if the amount remaining is less than the sector size, put it down
                    containerData.Write(sector.GetData(), 0, remaining);
                    Debug("\t\t-Partial sector: " + remaining + "/512 bytes", LOG_DETAIL.INSANE);
                    //We've read the last sector in this index!
                    remaining = 0;
                }
            } while (remaining > 0);

            //Return the data stream back to it's original position
            store.dataChannel.GetStream().Seek(0);

            return containerData.Flip();
        }

        /// <summary>
        /// Memoize all of the reference tables from the cache
        /// </summary>
        public void LoadReferenceTables()
        {
            //Reset the references array
            referenceTables = new RSReferenceTable[store.GetTypeCount()];

            Debug(@"  _                     _ _               _____       __ _______    _     _           ");
            Debug(@" | |                   | (_)             |  __ \     / _|__   __|  | |   | |          ");
            Debug(@" | |     ___   __ _  __| |_ _ __   __ _  | |__) |___| |_   | | __ _| |__ | | ___  ___ ");
            Debug(@" | |    / _ \ / _` |/ _` | | '_ \ / _` | |  _  // _ \  _|  | |/ _` | '_ \| |/ _ \/ __|");
            Debug(@" | |___| (_) | (_| | (_| | | | | | (_| | | | \ \  __/ |    | | (_| | |_) | |  __/\__ \");
            Debug(@" |______\___/ \__,_|\__,_|_|_| |_|\__, | |_|  \_\___|_|    |_|\__,_|_.__/|_|\___||___/");
            Debug(@"                                   __/ |                                              ");
            Debug(@"                                  |___/                                               ");

            //Attempt to load all of the reference tables
            for (int type = 0; type < store.GetTypeCount(); type++)
            {
                try
                {
                    GetReferenceTable(type);
                }
                catch (FileNotFoundException ex)
                {
                    Debug(ex.Message);
                }
            }
        }

        /// <summary>
        /// Retrieve the memoized ReferenceTable from the cache if possible.
        /// Otherwise, memoize and return the specified ReferenceTable
        /// </summary>
        /// <param name="type">The reference table index</param>
        /// <returns></returns>

        public RSReferenceTable GetReferenceTable(int type)
        {
            if (type < 0 || type >= store.GetTypeCount())
                throw new FileNotFoundException("\tERROR - Reference table " + type + " out of bounds");

            if (referenceTables[type] == null)
            {
                RSContainer container = GetContainer(RSConstants.META_INDEX, type);

                if (container == null)
                    throw new FileNotFoundException("\tERROR - Reference table container " + type + " is null");

                //Decode the reference table from the container stream and cache it 
                JagStream containerStream = container.GetStream();
                RSReferenceTable refTable = ReferenceTableCodec.Decode(containerStream);
                refTable.SetType(type); //For the UI
                referenceTables[type] = refTable;
                Debug("...Decoded reference table " + type, LOG_DETAIL.ADVANCED);
                Debug("", LOG_DETAIL.ADVANCED);
            }

            return referenceTables[type];
        }

        /// <summary>
        /// Retrieve the file from the <paramref name="type"/> index, entry <paramref name="file"/> in archive <paramref name="archive"/>
        /// </summary>
        /// <param name="type">The index to search</param>
        /// <param name="archive">The archive index</param>
        /// <param name="file">The individual file entry index</param>
        /// <returns>The entry within the archive</returns>
        internal JagStream ReadEntry(int type, int archive, int file)
        {
            //Check if the file/member are valid
            RSEntry entry = GetReferenceTable(type).GetEntry(archive);

            if (entry == null || file < 0 || file >= entry.GetValidFileIds().Length)
                throw new FileNotFoundException("\tUnable to find member " + file + ", in type " + type + ", archive " + archive + ", len: " + entry.GetValidFileIds().Length);

            //Get the Entry from the Container's Archive
            Debug("Reading " + RSConstants.GetContainerNameForType(type) + ", Archive" + archive + " - file " + file, LOG_DETAIL.ADVANCED);
            return GetArchive(GetContainer(type, archive), entry.GetValidFileIds().Length).GetEntry(file);
        }

        /// <summary>
        /// Returns the memoized archive if possible. Decodes and memoizes if not yet already done.
        /// </summary>
        /// <param name="container">The container from which the archive is built</param>
        /// <param name="fileCount">The number of files contained in the archive</param>
        /// <returns>Returns the decoded archive instance</returns>
        internal RSArchive GetArchive(RSContainer container, int fileCount)
        {
            //Has the archive already been decoded from the container data?
            if (container.GetArchive() != null)
                return container.GetArchive();

            //Otherwise, construct the archive from the container
            RSArchive archive = RSArchive.Decode(container.GetStream(), fileCount);
            if (archive == null)
                throw new NullReferenceException("Archive is null");

            container.SetArchive(archive);

            return archive;
        }

        /// <summary>
        /// Gets the number of files of the specified type.
        /// </summary>
        /// <param name="type">The non-meta channel index</param>
        /// <returns>The total number of files of the specified type</returns>

        public int GetFileCount(int type)
        {
            return store.GetFileCount(type);
        }

        /// <summary>
        /// Returns the filestore for this cache
        /// </summary>
        /// <returns>The filestore for this cache</returns>
        internal RSFileStore GetStore()
        {
            return store;
        }

        public ItemDefinition GetItemDefinition(int archive, int entryId)
        {
            JagStream entry = ReadEntry(RSConstants.ITEM_DEFINITIONS_INDEX, archive, entryId);
            ItemDefinition def = ItemDefinition.DecodeFromStream(entry);
            def.SetId(archive * 256 + entryId);
            return def;
        }

        public SpriteDefinition GetSprite(int containerId)
        {
            //Get the sprite for the given entry
            RSContainer container = GetContainer(RSConstants.SPRITES_INDEX, containerId);
            return SpriteDefinition.DecodeFromStream(container.GetStream());
        }

        public ObjectDefinition GetObjectDefinition(int archive, int entry)
        {
            JagStream objStream = ReadEntry(RSConstants.OBJECTS_DEFINITIONS_INDEX, archive, entry);
            ObjectDefinition def = ObjectDefinition.DecodeFromStream(objStream);
            def.id = archive * 256 + entry;
            return def;
        }

        internal NPCDefinition GetNPCDefinition(int archive, int entry)
        {
            JagStream npcStream = ReadEntry(RSConstants.NPC_DEFINITIONS_INDEX, archive, entry);
            NPCDefinition def = new NPCDefinition(npcStream);
            def.SetId(archive * 256 + entry);
            return def;
        }

        public ModelDefinition GetModelDefinition(int archive, int entry)
        {
            JagStream data = ReadEntry(RSConstants.MODELS_INDEX, archive, entry);
            var def = new ModelDefinition();
            def.Decode(data);
            def.ModelID = (archive << 8) | entry;
            return def;
        }
    }
}
