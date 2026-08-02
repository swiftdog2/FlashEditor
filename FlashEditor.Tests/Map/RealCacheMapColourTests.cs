using System.Collections.Generic;
using FlashEditor.cache;
using FlashEditor.Cache.Region;
using FlashEditor.Definitions;
using FlashEditor.Map;
using FlashEditor.Tests.Cache.RealCache;
using Xunit;

namespace FlashEditor.Tests.Map
{
    /// <summary>
    ///     Blends real map squares with real floor definitions and checks the result is a plausible
    ///     terrain image rather than a plausible-looking constant.
    /// </summary>
    /// <remarks>
    ///     A colour pipeline cannot be validated against cache bytes, so these assert properties
    ///     that a broken pipeline would violate: that the output varies, that it stays in gamut,
    ///     and that a square's edges match its neighbour's when the apron is loaded.
    /// </remarks>
    [Collection("RealCache")]
    public sealed class RealCacheMapColourTests : IClassFixture<RealCacheFixture>
    {
        private readonly RealCacheFixture _fixture;

        public RealCacheMapColourTests(RealCacheFixture fixture)
        {
            _fixture = fixture;
        }

        private static UnderlayBlender.Resolver ResolverFor(RSCache cache)
        {
            var cacheById = new Dictionary<int, UnderlayColour?>();

            return definitionId =>
            {
                if (cacheById.TryGetValue(definitionId, out UnderlayColour? known))
                    return known;

                UnderlayColour? colour;
                try
                {
                    FloorUnderlayDefinition def = cache.GetFloorUnderlay(definitionId);
                    colour = UnderlayColour.FromRgb(def.Rgb);
                }
                catch (System.Exception)
                {
                    //A terrain file may name an underlay the config archive does not carry. The
                    //client would render nothing for it rather than fail the whole square.
                    colour = null;
                }

                cacheById[definitionId] = colour;
                return colour;
            };
        }

        [RealCacheFact]
        public void LumbridgeBlendsToVariedTerrainColour()
        {
            RSCache cache = _fixture.OpenCache();
            var loader = new MapSquareLoader(cache);

            MapScene scene = MapScene.Load(loader, 50, 50);
            int[,] blended = UnderlayBlender.Blend(scene.UnderlayGrid(0), ResolverFor(cache));

            var distinct = new HashSet<int>();
            int coloured = 0;

            //Only the centre square, which is the one whose blend is fully supported by the apron.
            for (int x = 64; x < 128; x++)
            {
                for (int y = 64; y < 128; y++)
                {
                    int hsl = blended[x, y];
                    distinct.Add(hsl);
                    if (hsl != 0)
                    {
                        coloured++;
                        Assert.InRange(MapPalette.ToRgb(hsl), 0, 0xFFFFFF);
                    }
                }
            }

            //A constant generator, an unpopulated palette or a divide-by-count hue would all
            //collapse this to a handful of values.
            Assert.True(distinct.Count > 50, $"only {distinct.Count} distinct colours across the square");
            Assert.True(coloured > 3000, $"only {coloured} of 4096 tiles took a colour");
        }

        /// <summary>
        ///     Loading the apron changes the colours along a square's edge, and not its middle.
        /// </summary>
        /// <remarks>
        ///     This is the test that justifies <see cref="MapScene"/> existing. If the apron made no
        ///     difference the blend window would not be reaching across the boundary, and if it
        ///     changed the middle too then something other than the window is at work.
        /// </remarks>
        [RealCacheFact]
        public void TheApronChangesEdgeColoursOnly()
        {
            RSCache cache = _fixture.OpenCache();
            var loader = new MapSquareLoader(cache);
            UnderlayBlender.Resolver resolve = ResolverFor(cache);

            //With neighbours.
            MapScene withApron = MapScene.Load(loader, 50, 50);
            int[,] blendedWith = UnderlayBlender.Blend(withApron.UnderlayGrid(0), resolve);

            //Alone.
            Region alone = loader.Load(50, 50, out _);
            MapScene isolated = MapScene.FromSquares(50, 50, new[,] { { alone } });
            int[,] blendedAlone = UnderlayBlender.Blend(isolated.UnderlayGrid(0), resolve);

            int edgeDiffs = 0;
            int middleDiffs = 0;

            for (int x = 0; x < 64; x++)
            {
                for (int y = 0; y < 64; y++)
                {
                    bool same = blendedWith[64 + x, 64 + y] == blendedAlone[x, y];
                    if (same)
                        continue;

                    //A tile's window spans x-ReachBack .. x+ReachForward, so it is self-contained
                    //only from ReachBack to 63-ReachForward. The two reaches are not
                    //interchangeable here, and swapping them leaves a one-tile band on each of the
                    //north and east edges misclassified as interior.
                    bool nearEdge = x < UnderlayBlender.ReachBack
                                 || y < UnderlayBlender.ReachBack
                                 || x >= 64 - UnderlayBlender.ReachForward
                                 || y >= 64 - UnderlayBlender.ReachForward;

                    if (nearEdge) edgeDiffs++;
                    else middleDiffs++;
                }
            }

            Assert.True(edgeDiffs > 0, "the apron made no difference at all - the window is not crossing the boundary");
            Assert.Equal(0, middleDiffs);
        }

        /// <summary>
        ///     Real overlay colours convert without leaving the palette.
        /// </summary>
        [RealCacheFact]
        public void EveryOverlayColourConvertsIntoGamut()
        {
            RSCache cache = _fixture.OpenCache();
            int transparent = 0;
            int converted = 0;

            foreach (int id in cache.GetConfigFileIds(RSConstants.FLOOR_OVERLAY_GROUP))
            {
                FloorOverlayDefinition def = cache.GetFloorOverlay(id);
                if (!def.HasPrimaryRgb)
                    continue;

                int hsl = MapPalette.FromRgb(def.PrimaryRgb);
                if (hsl == MapPalette.NoColour)
                {
                    transparent++;
                    continue;
                }

                converted++;
                Assert.InRange(hsl, 0, 0xFFFF);
                Assert.InRange(MapPalette.ToRgb(hsl), 0, 0xFFFFFF);
            }

            //233 definitions carry a primary colour and 29 of those are the magenta sentinel.
            Assert.Equal(233, transparent + converted);
            Assert.Equal(29, transparent);
        }
    }
}
