using System;
using System.IO;
using FlashEditor.cache;
using FlashEditor.Cache.Region;
using FlashEditor.Tests.Cache.RealCache;
using FlashEditor.Utils;
using Xunit;

using MapRegion = FlashEditor.Cache.Region.Region;

namespace FlashEditor.Tests.Map
{
    /// <summary>
    ///     Saves an edited map square into a copy of the real cache and reads it back.
    /// </summary>
    /// <remarks>
    ///     Everything else in the suite stops at the encoder. This is the only test that exercises
    ///     the whole write path - container, sector chain, index record and reference table - and
    ///     then reopens the cache from disk to check what actually landed.
    ///
    ///     It works on a copy. The real cache is never written to by any test.
    /// </remarks>
    [Collection("RealCache")]
    public sealed class MapSaveRoundTripTests : IDisposable
    {
        private readonly string workingCopy;
        private readonly bool available;

        public MapSaveRoundTripTests()
        {
            DebugUtil.LOG_LEVEL = DebugUtil.LOG_DETAIL.NONE;

            string source = RealCacheLocator.Directory;
            if (source == null)
                return;

            workingCopy = Path.Combine(Path.GetTempPath(), "flasheditor-map-save-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(workingCopy);

            foreach (string file in Directory.GetFiles(source, "main_file_cache.*"))
                File.Copy(file, Path.Combine(workingCopy, Path.GetFileName(file)));

            //The key file lives beside the cache, and the loc groups cannot be read without it.
            string keys = Path.Combine(source, "..", "xteas");
            if (Directory.Exists(keys))
            {
                string target = Path.Combine(workingCopy, "..", "xteas");
                if (!Directory.Exists(target))
                    Directory.CreateDirectory(target);
                foreach (string file in Directory.GetFiles(keys))
                    File.Copy(file, Path.Combine(target, Path.GetFileName(file)), true);
            }

            available = true;
        }

        [RealCacheFact]
        public void AnEditedSquareSurvivesASaveAndReload()
        {
            if (!available)
                return;

            const int rx = 50, ry = 50;

            //Save.
            int expectedVersionAfter;
            using (var store = new RSFileStore(workingCopy))
            {
                var cache = new RSCache(store);
                cache.TryAutoLoadXTEAKeys(workingCopy);
                var loader = new MapSquareLoader(cache);

                MapRegion region = loader.Load(rx, ry, out _);
                Assert.False(loader.Save(region, rx, ry), "an untouched square should write nothing");

                region.SetUnderlayId(0, 10, 10, 77);
                region.SetOverlayId(0, 11, 11, 5);
                region.SetOverlayShape(0, 11, 11, 3);
                region.SetOverlayRotation(0, 11, 11, 2);
                region.SetRenderRule(0, 12, 12, 0x9);

                int group = loader.ResolveGroup(MapSquareNames.Terrain(rx, ry));
                expectedVersionAfter = VersionOf(cache, group) + 1;

                Assert.True(loader.Save(region, rx, ry));
                Assert.False(region.Dirty);

                //Save only stages. Nothing reaches the disk until the cache is committed, which is
                //what lets a whole editing session land as one consistent set of files.
                Assert.True(cache.HasUnsavedChanges);
                cache.WriteCache(workingCopy);
                Assert.False(cache.HasUnsavedChanges);
            }

            //Reopen from disk and check what landed.
            using (var store = new RSFileStore(workingCopy))
            {
                var cache = new RSCache(store);
                cache.TryAutoLoadXTEAKeys(workingCopy);
                var loader = new MapSquareLoader(cache);

                MapRegion region = loader.Load(rx, ry, out _);

                Assert.Equal(77, region.GetUnderlayId(0, 10, 10));
                Assert.Equal(5, region.GetOverlayId(0, 11, 11));
                Assert.Equal(3, region.GetOverlayShape(0, 11, 11));
                Assert.Equal(2, region.GetOverlayRotation(0, 11, 11));
                Assert.Equal(0x9, region.GetRenderRule(0, 12, 12));

                //The rest of the square is untouched.
                Assert.NotEmpty(region.ExtrasTail);
                Assert.True(region.GetLocations().Count > 1000);

                //And the archive version advanced by exactly one, rather than being stamped.
                int group = loader.ResolveGroup(MapSquareNames.Terrain(rx, ry));
                Assert.Equal(expectedVersionAfter, VersionOf(cache, group));
            }
        }

        /// <summary>Saving an edited location list preserves the other squares.</summary>
        [RealCacheFact]
        public void EditingLocationsDoesNotDisturbNeighbours()
        {
            if (!available)
                return;

            const int rx = 48, ry = 54;
            int neighbourLocs;

            using (var store = new RSFileStore(workingCopy))
            {
                var cache = new RSCache(store);
                cache.TryAutoLoadXTEAKeys(workingCopy);
                var loader = new MapSquareLoader(cache);

                MapRegion neighbour = loader.Load(rx + 1, ry, out _);
                neighbourLocs = neighbour.GetLocations().Count;

                MapRegion region = loader.Load(rx, ry, out LocationLoadResult result);
                Assert.Equal(LocationLoadResult.Loaded, result);

                Location victim = region.GetLocations()[0];
                region.RemoveLocation(victim);
                Assert.True(loader.Save(region, rx, ry));
                cache.WriteCache(workingCopy);
            }

            using (var store = new RSFileStore(workingCopy))
            {
                var cache = new RSCache(store);
                cache.TryAutoLoadXTEAKeys(workingCopy);
                var loader = new MapSquareLoader(cache);

                MapRegion neighbour = loader.Load(rx + 1, ry, out _);
                Assert.Equal(neighbourLocs, neighbour.GetLocations().Count);
            }
        }

        private static int VersionOf(RSCache cache, int groupId)
        {
            RSReferenceTable table = cache.GetReferenceTable(RSConstants.MAPS_INDEX);
            return table.GetArchiveEntry(groupId).GetVersion();
        }

        public void Dispose()
        {
            if (workingCopy == null || !Directory.Exists(workingCopy))
                return;

            try
            {
                Directory.Delete(workingCopy, true);
            }
            catch (IOException)
            {
                //A leftover temp copy is untidy, not a failure.
            }
        }
    }
}
