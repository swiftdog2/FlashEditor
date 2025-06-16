using FlashEditor.cache;
using FlashEditor.utils;
using System;
using System.Diagnostics;

namespace FlashEditor.Tests {
    class CodecTests {
        public static void Main() {
            try {
                //Load the cache and the reference tables
                RSFileStore store = new RSFileStore(Properties.Settings.Default.cacheDir);
                Stopwatch sw = new Stopwatch();
                sw.Start();
                RSCache cache = new RSCache(store);
                sw.Stop();

                DebugUtil.Debug("Loaded cache in " + sw.ElapsedMilliseconds + "ms");

                //Reference table
                RSReferenceTable itemRefIn = cache.GetReferenceTable(RSConstants.ITEM_DEFINITIONS_INDEX);
                JagStream itemRefTableEncoded = ReferenceTableCodec.Encode(itemRefIn);
                RSReferenceTable itemRefOut = ReferenceTableCodec.Decode(itemRefTableEncoded);
                JagStream itemRefTableEncoded2 = ReferenceTableCodec.Encode(itemRefOut);
                StreamTests.StreamDifference(itemRefTableEncoded, itemRefTableEncoded2, "item refs");

                //Reference table container
                /*
                RSContainer refContainerIn = new RSContainer(cache.GetContainer(RSConstants.META_INDEX, RSConstants.ITEM_DEFINITIONS_INDEX).GetCompressionType(), itemRefTableEncoded2, cache.GetContainer(RSConstants.META_INDEX, RSConstants.ITEM_DEFINITIONS_INDEX).GetVersion());
                JagStream refContainerEncoded = refContainerIn.Encode();
                RSContainer refContainerOut = RSContainer.Decode(refContainerEncoded);
                JagStream refContainerEncoded2 = refContainerOut.Encode();
                StreamTests.StreamDifference(refContainerEncoded, refContainerEncoded2, "item ref container");*/

                //Container
                RSContainer containerIn = cache.GetContainer(RSConstants.ITEM_DEFINITIONS_INDEX, 0);
                JagStream containerEncoded = containerIn.Encode();
                RSContainer containerOut = RSContainer.Decode(containerEncoded);
                JagStream containerEncoded2 = containerOut.Encode();
                StreamTests.StreamDifference(containerEncoded, containerEncoded2, "item container");

                //Archive
                RSArchive archiveIn = cache.GetArchive(containerIn, itemRefIn.GetEntry(0).GetValidFileIds().Length);
                JagStream archiveEncoded = archiveIn.Encode();
                RSArchive archiveOut = RSArchive.Decode(archiveEncoded, archiveIn.entries.Count);
                JagStream archiveEncoded2 = archiveOut.Encode();
                StreamTests.StreamDifference(archiveEncoded, archiveEncoded2, "item archive");
            } catch(Exception ex) {
                DebugUtil.Debug(ex.StackTrace);
            }
        }

    }
}
