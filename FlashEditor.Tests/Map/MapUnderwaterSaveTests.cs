using System;
using System.IO;
using FlashEditor.Cache;
using FlashEditor.Cache.Region;
using FlashEditor.Cache.Util.Crypto;
using FlashEditor.Tests.Cache.RealCache;
using FlashEditor.Utils;
using Xunit;
using Xunit.Abstractions;

using MapRegion = FlashEditor.Cache.Region.Region;

namespace FlashEditor.Tests.Map
{
    /// <summary>
    ///     Saving an underwater square must land on the <c>um</c> and <c>ul</c> groups and leave the
    ///     surface square alone.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///     This pins a real corruption. <c>MapSquareLoader.Save</c> resolved
    ///     <c>MapSquareNames.Terrain</c> and <c>Locations</c> unconditionally, so a square that came
    ///     back from <c>LoadUnderwater</c> - one plane, no extras tail - was encoded and written into
    ///     the four-plane <c>m</c> group. Nothing failed: the file is shorter, the CRC is recomputed
    ///     over it, the reference table is updated to match, and the square's entire surface terrain
    ///     is gone. It only stayed latent because nothing in the editor had an underwater edit path
    ///     to reach it with.
    ///     </para>
    ///     <para>
    ///     The assertion is on the <em>surface</em> square, not on the underwater one. A test that
    ///     only checked the underwater square came back edited would have passed against the broken
    ///     code, because the underwater write never happened at all and the reload simply read the
    ///     unchanged <c>um</c> group.
    ///     </para>
    ///     <para>It works on a copy. Neither real cache is ever written to.</para>
    /// </remarks>
    [Collection("RealCache")]
    public sealed class MapUnderwaterSaveTests : IDisposable
    {
        private readonly string workingCopy;
        private readonly bool available;
        private readonly ITestOutputHelper output;

        /// <summary>Copies the located cache into a temp directory this class owns.</summary>
        /// <param name="output">Where the chosen square is reported.</param>
        public MapUnderwaterSaveTests(ITestOutputHelper output)
        {
            this.output = output;
            DebugUtil.LOG_LEVEL = DebugUtil.LOG_DETAIL.NONE;

            string source = RealCacheLocator.Directory;
            if (source == null)
                return;

            workingCopy = Path.Combine(Path.GetTempPath(),
                "flasheditor-underwater-save-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(workingCopy);

            foreach (string file in Directory.GetFiles(source, "main_file_cache.*"))
                File.Copy(file, Path.Combine(workingCopy, Path.GetFileName(file)));

            //Same key-file probe the production loader uses, copied into the working copy so a
            //leftover from a run against the other cache cannot supply keys to this one.
            string keys = XTEAKeyTable.FindKeyFile(source);
            if (keys != null)
                File.Copy(keys, Path.Combine(workingCopy, Path.GetFileName(keys)), true);

            available = true;
        }

        /// <summary>An underwater edit must not reach the surface square.</summary>
        [RealCacheFact]
        public void SavingAnUnderwaterSquareLeavesTheSurfaceSquareUntouched()
        {
            if (!available)
                return;

            byte[] surfaceBefore;
            int surfaceTerrainVersionBefore;
            int surfaceLocationVersionBefore;
            int rx, ry;

            using (var store = new RSFileStore(workingCopy))
            {
                var cache = new RSCache(store);
                cache.TryAutoLoadXTEAKeys(workingCopy);
                var loader = new MapSquareLoader(cache);

                (rx, ry) = FirstSquareWithBothLayers(loader);
                output.WriteLine($"editing um{rx}_{ry} and watching m{rx}_{ry}");

                MapRegion surface = loader.Load(rx, ry, out _);
                surfaceBefore = (byte[]) surface.RawTerrain.Clone();
                surfaceTerrainVersionBefore = VersionOf(cache, loader, MapSquareNames.Terrain(rx, ry));
                surfaceLocationVersionBefore = VersionOf(cache, loader, MapSquareNames.Locations(rx, ry));

                MapRegion underwater = loader.LoadUnderwater(rx, ry);
                Assert.Equal(MapSquareLayer.Underwater, underwater.Layer);
                Assert.Equal(MapSquareLoader.UnderwaterPlanes, underwater.PlaneCount);

                //An untouched underwater square stages nothing, same as an untouched surface one.
                Assert.False(underwater.Dirty, $"um{rx}_{ry} came out of the decoder dirty");
                Assert.False(loader.Save(underwater, rx, ry),
                    "an untouched underwater square should write nothing");

                //The surface square has four planes and a far longer terrain file, so writing the
                //underwater encode over it is a visible truncation as well as a wrong one.
                Assert.Equal(MapRegion.PLANES, surface.PlaneCount);
                Assert.True(surface.RawTerrain.Length > underwater.RawTerrain.Length);

                underwater.SetUnderlayId(0, 5, 5, 42);
                Assert.True(loader.Save(underwater, rx, ry));

                cache.WriteCache(workingCopy);
            }

            using (var store = new RSFileStore(workingCopy))
            {
                var cache = new RSCache(store);
                cache.TryAutoLoadXTEAKeys(workingCopy);
                var loader = new MapSquareLoader(cache);

                MapRegion surface = loader.Load(rx, ry, out _);

                Assert.Equal(MapRegion.PLANES, surface.PlaneCount);

                //Compared by length and content rather than with Assert.Equal on the arrays, whose
                //failure message would be tens of kilobytes of hex.
                Assert.True(surfaceBefore.Length == surface.RawTerrain.Length,
                    $"m{rx}_{ry} was {surfaceBefore.Length} bytes and is now " +
                    $"{surface.RawTerrain.Length} - the underwater save landed on the surface square");
                Assert.True(surfaceBefore.AsSpan().SequenceEqual(surface.RawTerrain),
                    $"m{rx}_{ry} kept its length but its contents changed");

                //A rewrite bumps the archive version even when the payload happens to survive
                //comparison, so this catches either surface group having been touched at all. The
                //location half matters as much as the terrain half: Save writes both whenever the
                //square is dirty, and both name resolutions were wrong.
                Assert.Equal(surfaceTerrainVersionBefore,
                    VersionOf(cache, loader, MapSquareNames.Terrain(rx, ry)));
                Assert.Equal(surfaceLocationVersionBefore,
                    VersionOf(cache, loader, MapSquareNames.Locations(rx, ry)));

                //And the edit did land where it was meant to.
                MapRegion underwater = loader.LoadUnderwater(rx, ry);
                Assert.Equal(42, underwater.GetUnderlayId(0, 5, 5));
            }
        }

        /// <summary>
        ///     The first square that has both a surface and an underwater terrain file.
        /// </summary>
        /// <remarks>
        ///     Searched rather than hardcoded. Which squares have a seabed is a property of the
        ///     cache, and a literal pair would silently skip the whole test on a cache that
        ///     addressed its water differently.
        /// </remarks>
        /// <param name="loader">The loader to ask.</param>
        /// <returns>The region coordinates.</returns>
        private static (int, int) FirstSquareWithBothLayers(MapSquareLoader loader)
        {
            for (int rx = 0; rx < 256; rx++)
                for (int ry = 0; ry < 256; ry++)
                    if (loader.Exists(rx, ry) && loader.ExistsUnderwater(rx, ry))
                        return (rx, ry);

            Assert.Fail("no square in this cache has both an m and a um group");
            return (-1, -1);
        }

        private static int VersionOf(RSCache cache, MapSquareLoader loader, string groupName)
        {
            RSReferenceTable table = cache.GetReferenceTable(RSConstants.MAPS_INDEX);
            return table.GetArchiveEntry(loader.ResolveGroup(groupName)).GetVersion();
        }

        /// <summary>Removes the working copy.</summary>
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
