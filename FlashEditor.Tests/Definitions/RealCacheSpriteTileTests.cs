using System;
using System.Collections.Generic;
using System.Drawing;
using FlashEditor.Cache;
using FlashEditor.Definitions.Sprites;
using FlashEditor.Cache.Util;
using FlashEditor.Tests.Cache.RealCache;
using Xunit;
using Xunit.Abstractions;
using FlashEditor.IO;

namespace FlashEditor.Tests.Definitions
{
    /// <summary>
    ///     What the sprite grid decides to draw, held against every set the cache declares.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///     The paint path itself is judged by eye - nothing in the suite covers WinForms - but the
    ///     two decisions behind it are code with real failure modes, and this runs both over the
    ///     whole index rather than over the handful of sizes anyone thought of.
    ///     </para>
    ///     <para>
    ///     The first is <c>SpritePainter.ContentOf</c>, which says whether a row has a picture at
    ///     all. It has to be sufficient as well as necessary: a set it calls drawable must really
    ///     rasterise, or the tab throws while painting, and a set it calls empty must really have no
    ///     pixels, or the tab hides artwork behind an "empty" marker. The second is
    ///     <c>SpriteTileFit</c>, which must never clip a sprite and never change its shape.
    ///     </para>
    /// </remarks>
    [Collection("RealCache")]
    public sealed class RealCacheSpriteTileTests : IClassFixture<RealCacheFixture>
    {
        /// <summary>The tile side the grid uses at 96 DPI with its Consolas 9 grid font.</summary>
        /// <remarks>
        ///     Stated here rather than read from the form, because the form measures it from a font
        ///     it only has once it is on a screen. The claims below hold at any side - the sweep over
        ///     several sides says so - so this one is a representative rather than a constant the
        ///     production code has to agree with.
        /// </remarks>
        private const int TileSide = 60;

        private readonly RealCacheFixture _fixture;
        private readonly ITestOutputHelper _output;

        /// <summary>Binds the shared open cache and the per-test output sink.</summary>
        /// <param name="fixture">The shared cache.</param>
        /// <param name="output">Where the census is written.</param>
        public RealCacheSpriteTileTests(RealCacheFixture fixture, ITestOutputHelper output)
        {
            _fixture = fixture;
            _output = output;
        }

        /// <summary>Sprite sets the reference table declares, one per group.</summary>
        private int DeclaredSets => _fixture.DeclaredGroups(RSConstants.SPRITES_INDEX);

        /// <summary>Every declared sprite set, decoded by the production codec.</summary>
        /// <returns>The sweep.</returns>
        private DefinitionSweep<SpriteDefinition> Sweep()
        {
            return new DefinitionSweep<SpriteDefinition>(_fixture, _output, RSConstants.SPRITES_INDEX,
                new DefinitionCodec<SpriteDefinition>("sprite set", DecodeSet, sprite => sprite.Encode()))
                .AcrossEveryGroup()
                .NotOpcodeTerminated();
        }

        private static SpriteDefinition DecodeSet(int definitionId, JagStream stream)
        {
            var sprite = new SpriteDefinition();
            sprite.Decode(stream);
            sprite.SetIndex(definitionId);
            return sprite;
        }

        /// <summary>
        ///     A set the tab says it can draw really does rasterise, and to the canvas it declared.
        /// </summary>
        /// <remarks>
        ///     The guard is what stands between the grid and an exception thrown from inside a paint.
        ///     A frame's canvas is the set's canvas grown to fit a frame that overflows it, and a
        ///     bitmap of zero width cannot exist, so a set with a zero canvas has to be refused
        ///     before <c>GetFrames</c> is asked for anything - it rasterises the whole set at once,
        ///     so one bad frame costs every frame beside it.
        /// </remarks>
        [RealCacheFact]
        public void EverySetTheGridWillDraw_Rasterises()
        {
            var failures = new List<string>();
            int drawable = 0;
            int empty = 0;

            DefinitionSweepResult swept = Sweep().ForEachDecoded((record, sprite) =>
            {
                if (SpritePainter.ContentOf(sprite, 0) != SpriteTileContent.Picture)
                {
                    empty++;

                    //Necessary as well as sufficient: nothing may be called empty while it still has
                    //pixels, or the grid draws a marker over real artwork.
                    if (sprite.Frames != null && sprite.Frames.Count > 0 && sprite.Frames[0].Area > 0 &&
                        SpritePainter.CanRasterise(sprite))
                        failures.Add($"set {record.Id} was called empty and stores " +
                                     $"{sprite.Frames[0].Area} pixels in frame 0");
                    return;
                }

                drawable++;

                try
                {
                    RSBufferedImage frame = sprite.GetFrame(0);
                    SpriteFrame stored = sprite.Frames[0];

                    int canvasWidth = Math.Max(sprite.width, stored.OffsetX + stored.SubWidth);
                    int canvasHeight = Math.Max(sprite.height, stored.OffsetY + stored.SubHeight);

                    if (frame.GetWidth() != canvasWidth || frame.GetHeight() != canvasHeight)
                        failures.Add($"set {record.Id} rasterised to {frame.GetWidth()}x{frame.GetHeight()} " +
                                     $"where its canvas is {canvasWidth}x{canvasHeight}");

                    using Bitmap display = SpritePainter.ToDisplayBitmap(frame);
                    if (display == null)
                        failures.Add($"set {record.Id} is drawable and produced no display bitmap");
                }
                catch (Exception ex)
                {
                    failures.Add($"set {record.Id} was called drawable and threw: {ex.GetType().Name}: {ex.Message}");
                }
                finally
                {
                    //Released per set. The whole index at once is 11,177 pinned pixel buffers.
                    sprite.Dispose();
                }
            });

            _output.WriteLine($"{drawable} sets draw a picture in the grid, {empty} draw the empty marker");

            Assert.Empty(failures);
            Assert.Equal(DeclaredSets, swept.Records);
            Assert.Equal(DeclaredSets, drawable + empty);
        }

        /// <summary>
        ///     No sprite in the index is clipped by its tile or drawn out of shape.
        /// </summary>
        /// <remarks>
        ///     Run at four tile sides rather than one, because the side is measured from the grid's
        ///     font at run time and so is whatever the user's DPI makes it. A claim that only held at
        ///     60 pixels would be a claim about this machine.
        /// </remarks>
        [RealCacheFact]
        public void NoSpriteIsClippedOrStretchedByItsTile()
        {
            int[] sides = { 24, 48, TileSide, 96 };
            var failures = new List<string>();
            var upscaled = new Dictionary<int, int>();
            int oneToOne = 0;
            int shrunk = 0;
            int widest = 0;
            int tallest = 0;

            DefinitionSweepResult swept = Sweep().ForEachDecoded((record, sprite) =>
            {
                if (SpritePainter.ContentOf(sprite, 0) != SpriteTileContent.Picture)
                    return;

                SpriteFrame stored = sprite.Frames[0];
                int width = Math.Max(sprite.width, stored.OffsetX + stored.SubWidth);
                int height = Math.Max(sprite.height, stored.OffsetY + stored.SubHeight);

                widest = Math.Max(widest, width);
                tallest = Math.Max(tallest, height);

                foreach (int side in sides)
                {
                    SpriteTileFit fit = SpriteTileFit.Fit(width, height, side, side);

                    if (fit.Bounds.X < 0 || fit.Bounds.Y < 0 ||
                        fit.Bounds.Right > side || fit.Bounds.Bottom > side)
                        failures.Add($"set {record.Id} at tile {side}: {width}x{height} placed at {fit.Bounds}");

                    if (fit.Upscale >= 1)
                    {
                        //An exact multiple, so the pixels stay square whatever the sampling does
                        if (fit.Bounds.Width != width * fit.Upscale || fit.Bounds.Height != height * fit.Upscale)
                            failures.Add($"set {record.Id} at tile {side}: {width}x{height} magnified " +
                                         $"x{fit.Upscale} came out {fit.Bounds.Width}x{fit.Bounds.Height}");
                    }
                    else
                    {
                        long skew = Math.Abs((long) fit.Bounds.Width * height - (long) fit.Bounds.Height * width);
                        if (skew > Math.Max(width, height))
                            failures.Add($"set {record.Id} at tile {side}: {width}x{height} shrunk to " +
                                         $"{fit.Bounds.Width}x{fit.Bounds.Height} is a different shape");
                    }

                    if (side != TileSide)
                        continue;

                    if (fit.Upscale > 1)
                        upscaled[fit.Upscale] = upscaled.TryGetValue(fit.Upscale, out int seen) ? seen + 1 : 1;
                    else if (fit.Upscale == 1)
                        oneToOne++;
                    else
                        shrunk++;
                }
            });

            _output.WriteLine($"largest sprite canvas in this cache: {widest}x{tallest}");
            _output.WriteLine($"at a {TileSide}px tile: {oneToOne} sets at 1:1, {shrunk} shrunk to fit, " +
                              $"{SumOf(upscaled)} magnified");
            foreach (KeyValuePair<int, int> factor in SortedByFactor(upscaled))
                _output.WriteLine($"  x{factor.Key}: {factor.Value} sets");

            Assert.Empty(failures);
            Assert.Equal(DeclaredSets, swept.Records);
        }

        private static int SumOf(Dictionary<int, int> counts)
        {
            int total = 0;
            foreach (int count in counts.Values)
                total += count;
            return total;
        }

        private static IEnumerable<KeyValuePair<int, int>> SortedByFactor(Dictionary<int, int> counts)
        {
            var keys = new List<int>(counts.Keys);
            keys.Sort();
            foreach (int key in keys)
                yield return new KeyValuePair<int, int>(key, counts[key]);
        }
    }
}
