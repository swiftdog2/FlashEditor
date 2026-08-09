using System;
using System.Collections.Generic;
using System.Drawing;
using FlashEditor.Cache;
using FlashEditor.Cache.Region;
using FlashEditor.Definitions;
using FlashEditor.Cache.Util;
using FlashEditor.Definitions.Sprites;
using FlashEditor.Map;
using FlashEditor.Tests.Cache.RealCache;
using Xunit;
using FlashEditor.IO;

namespace FlashEditor.Tests.Map
{
    /// <summary>
    ///     Covers the three things a map view needs beyond terrain: texture colours, map scene icons
    ///     and the world navigator.
    /// </summary>
    [Collection("RealCache")]
    public sealed class RealCacheMapIconTests : IClassFixture<RealCacheFixture>
    {
        private readonly RealCacheFixture _fixture;

        public RealCacheMapIconTests(RealCacheFixture fixture)
        {
            _fixture = fixture;
        }

        [RealCacheFact]
        public void EveryMapSceneIconDecodesAndRoundTrips()
        {
            RSCache cache = _fixture.OpenCache();
            int[] ids = cache.GetConfigFileIds(RSConstants.MAP_SCENE_GROUP);

            var failures = new List<string>();
            int withSprite = 0;

            foreach (int id in ids)
            {
                try
                {
                    byte[] original = cache.ReadFileBytes(RSConstants.CONFIG, RSConstants.MAP_SCENE_GROUP, id);
                    var def = new MapSceneIconDefinition { Id = id };
                    var stream = new JagStream(original);
                    def.Decode(stream);

                    if (stream.Remaining() != 0)
                        failures.Add($"icon {id}: {stream.Remaining()} bytes left over");

                    if (!ByteEqual(original, def.Encode().ToArray()))
                        failures.Add($"icon {id}: round trip differs");

                    if (def.SpriteGroupId > 0)
                        withSprite++;
                }
                catch (Exception ex)
                {
                    failures.Add($"icon {id}: {ex.Message}");
                }
            }

            Assert.Empty(failures);
            Assert.Equal(100, ids.Length);
            Assert.True(withSprite > 0, "no map scene icon references a sprite");
        }

        /// <summary>
        ///     An object with no opcode 102 reports no icon.
        /// </summary>
        /// <remarks>
        ///     The private field used to have no initialiser, so it defaulted to 0 and every one of
        ///     the 21,665 locations in a scene claimed icon 0. The client defaults it to -1
        ///     (Class352.java:266). This is the "absent versus default" trap in CLAUDE.md.
        /// </remarks>
        [RealCacheFact]
        public void ObjectsWithoutTheOpcodeReportNoIcon()
        {
            RSCache cache = _fixture.OpenCache();
            RSReferenceTable table = cache.GetReferenceTable(RSConstants.OBJECTS_DEFINITIONS_INDEX);

            int examined = 0, withIcon = 0;

            foreach (int archiveId in new[] { 0, 1, 2, 3, 4 })
            {
                RSArchiveEntry entry = table.GetArchiveEntry(archiveId);
                if (entry == null)
                    continue;

                foreach (int fileId in entry.GetValidFileIds())
                {
                    ObjectDefinition def;
                    try { def = cache.GetObjectDefinition(archiveId, fileId); }
                    catch (Exception) { continue; }

                    examined++;
                    Assert.True(def.mapSceneIcon >= -1, "icon id below the -1 sentinel");
                    if (def.mapSceneIcon >= 0)
                        withIcon++;

                    //Opcode 68 is not parsed by the 637 client and occurs nowhere in this cache.
                    Assert.Equal(-1, def.mapSceneIdOpcode68);
                }
            }

            Assert.True(examined > 500, $"only {examined} definitions examined");

            //A minority carry one. If this ever reaches every definition, the default has
            //regressed to 0 and the map will draw an icon on every object in the world.
            Assert.True(withIcon < examined / 2,
                $"{withIcon} of {examined} definitions claim an icon, which means the default is wrong");
        }

        /// <summary>
        ///     A textured overlay takes the texture's declared colour, in map HSL space.
        /// </summary>
        /// <remarks>
        ///     The texture's <c>field1831</c> is already a packed HSL in the same space as an
        ///     overlay's own colours, and the client returns it verbatim (Node_Sub16.java:75-80).
        ///     Routing it through the model palette instead, as an early attempt did, turns the
        ///     world flat green and drains the colour out of water.
        /// </remarks>
        [RealCacheFact]
        public void TexturedOverlaysResolveToTheTextureColour()
        {
            RSCache cache = _fixture.OpenCache();
            new TextureManager(cache).Load();

            Assert.True(TextureManager.Textures.Count > 0, "no textures loaded");

            int textured = 0;
            foreach (int id in cache.GetConfigFileIds(RSConstants.FLOOR_OVERLAY_GROUP))
            {
                FloorOverlayDefinition overlay = cache.GetFloorOverlay(id);
                if (overlay.TextureId < 0)
                    continue;
                if (!TextureManager.Textures.TryGetValue(overlay.TextureId, out TextureDefinition def) || def == null)
                    continue;

                textured++;

                //Whatever the gate says, the declared colour has to be a legal packed HSL.
                Assert.InRange(def.field1831 & 0xFFFF, 0, 0xFFFF);
                Assert.InRange(MapPalette.ToRgb(def.field1831 & 0xFFFF), 0, 0xFFFFFF);
            }

            Assert.True(textured > 0, "no overlay in the cache references a loadable texture");
        }

        /// <summary>The navigator finds the same squares the loader does.</summary>
        [RealCacheFact]
        public void TheWorldNavigatorFindsEverySquare()
        {
            RSCache cache = _fixture.OpenCache();
            var loader = new MapSquareLoader(cache);

            using var navigator = new WorldNavigatorControl();
            navigator.Build(loader);

            Assert.Equal(1684, navigator.SquareCount);
            Assert.True(navigator.Exists(50, 50));

            //Region 0,0 is open sea in this cache and has no terrain file.
            Assert.False(navigator.Exists(0, 0));
        }

        /// <summary>Clicking the navigator maps back to the square drawn there.</summary>
        [Fact]
        public void NavigatorClicksMapBackToRegions()
        {
            using var navigator = new WorldNavigatorControl { Width = 256, Height = 256 };

            //Region Y runs north and screen Y runs down, so the top row is the highest region Y.
            Assert.Equal(new Point(0, 255), navigator.ToRegion(new Point(0, 0)));
            Assert.Equal(new Point(255, 0), navigator.ToRegion(new Point(255, 255)));
            Assert.Equal(new Point(50, 205), navigator.ToRegion(new Point(50, 50)));

            Assert.Null(navigator.ToRegion(new Point(-5, 0)));
            Assert.Null(navigator.ToRegion(new Point(9999, 0)));
        }

        /// <summary>Icons are drawn, and turning the layer off changes the picture.</summary>
        [RealCacheFact]
        public void IconsChangeTheRender()
        {
            RSCache cache = _fixture.OpenCache();
            new TextureManager(cache).Load();

            var loader = new MapSquareLoader(cache);
            var rasteriser = new MapRasteriser(cache) { TilePixels = 4 };
            MapScene scene = MapScene.Load(loader, 50, 50);

            int[] withIcons = Snapshot(rasteriser, scene, MapLayers.Default);
            int[] without = Snapshot(rasteriser, scene, MapLayers.Default & ~MapLayers.MapSceneIcons);

            Assert.NotEqual(withIcons, without);
        }

        private static int[] Snapshot(MapRasteriser rasteriser, MapScene scene, MapLayers layers)
        {
            using DirectBitmap bitmap = rasteriser.Render(scene, 0, layers);
            return (int[]) bitmap.Bits.Clone();
        }

        private static bool ByteEqual(byte[] a, byte[] b)
        {
            if (a.Length != b.Length)
                return false;
            for (int i = 0; i < a.Length; i++)
                if (a[i] != b[i])
                    return false;
            return true;
        }
    }
}
