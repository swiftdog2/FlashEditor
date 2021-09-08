using FlashEditor.cache;
using static FlashEditor.utils.DebugUtil;
using System;
using System.Diagnostics;

namespace FlashEditor {
    class ItemTests {
        /// <summary>
        /// Loads the main program
        /// </summary>
        static void Main() {
            //Load the Hydrascape cache
            cache.RSCache cache = new cache.RSCache(new RSFileStore(RSConstants.CACHE_DIRECTORY));
            TestAllItems(cache);
            System.Console.ReadLine();
        }

        /// <summary>
        /// Reads an item file from the cache and returns the corresponding ItemDefinition
        /// </summary>
        /// <param name="cache">The cache to search</param>
        /// <param name="archive">The archive to search</param>
        /// <param name="file">The archive entry </param>
        /// <returns>The decoded item definition</returns>
        static ItemDefinition LoadItemDefinition(cache.RSCache cache, int archive, int file) {
            RSEntry item = cache.ReadEntry(RSConstants.ITEM_DEFINITIONS_INDEX, archive, file);
            ItemDefinition def = ItemDefinition.Decode(item.GetStream());
            return def;
        }

        /// <summary>
        /// Loads all of the items in the items definition index
        /// </summary>
        /// <param name="cache">The cache to test</param>
        static long TestAllItems(cache.RSCache cache) {
            Stopwatch sw = new Stopwatch();
            sw.Start();
            RSReferenceTable table = cache.GetReferenceTable(RSConstants.ITEM_DEFINITIONS_INDEX);
            Debug("Table entry total: " + table.GetEntryTotal());
            for(int archive = 0; archive < table.GetEntryTotal(); archive++) {
                for(int file = 0; file < 256; file++) {
                    try {
                        LoadItemDefinition(cache, archive, file);
                        //Console.WriteLine("{0}: {1}", archive * 256 + file, LoadItemDefinition(cache, archive, file).name);
                    } catch(Exception ex) {
                        Debug(ex.Message);
                    }
                }
            }
            sw.Stop();
            return sw.ElapsedMilliseconds;
        }

        /// <summary>
        /// Times how long it takes to load a singular item definition
        /// </summary>
        /// <param name="cache"></param>
        /// <param name="archive"></param>
        /// <param name="file"></param>
        /// <returns></returns>
        static long TimeItemLoad(cache.RSCache cache, int archive, int file) {
            Stopwatch sw = new Stopwatch();
            sw.Start();
            LoadItemDefinition(cache, archive, file);
            sw.Stop();
            return sw.ElapsedMilliseconds;
        }
    }
}
