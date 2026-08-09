//This file implements a nullable-annotated interface member from a project that is not annotated.
#nullable enable

using System.Collections.Generic;
using System.Drawing;
using FlashEditor;
using FlashEditor.Cache;
using FlashEditor.Definitions.Editing;
using FlashEditor.Tests.Cache.RealCache;
using Xunit;

namespace FlashEditor.Tests.Definitions.Editing
{
    /// <summary>
    ///     <see cref="SpriteThumbnailRenderer"/>, against the index it claims to draw.
    /// </summary>
    /// <remarks>
    ///     The bounded cache above this is tested against a fake renderer, because what it does is
    ///     concurrency and nothing to do with the data. What is left to check here is the one thing
    ///     that <i>is</i> a claim about the cache: that every declared sprite group yields a tile of
    ///     the size the caller asked for, and that it does so without the caller having to know
    ///     which groups store no pixels. 2,377 of the vanilla capture's frames legitimately store a
    ///     zero-area plane, so "produced a tile" and "produced a picture" are different statements
    ///     and only the first is required.
    /// </remarks>
    public sealed class RealCacheSpriteThumbnailRendererTests : IClassFixture<RealCacheFixture>
    {
        private readonly RealCacheFixture _cache;

        public RealCacheSpriteThumbnailRendererTests(RealCacheFixture cache)
        {
            _cache = cache;
        }

        /// <summary>
        ///     Every sprite group the reference table declares yields a tile at the asked-for side.
        /// </summary>
        /// <remarks>
        ///     Sampled through the fixture rather than swept, because this is a property of the
        ///     renderer rather than a census of the index, and the sizes are stated by the caller
        ///     rather than by the data. The assertion has no <c>or</c> in it on purpose: a group
        ///     that would not decode still has to come back as a tile, because the sprite tab draws
        ///     one for that case and a null here would be a blank cell that reads as a defect.
        /// </remarks>
        [RealCacheFact]
        public void EveryDeclaredSpriteGroup_YieldsATileOfTheAskedForSide()
        {
            const int Side = 24;

            RSCache cache = _cache.OpenCache();
            using var renderer = new SpriteThumbnailRenderer(cache);

            Assert.True(renderer.Handles(RSConstants.SPRITES_INDEX));
            Assert.False(renderer.Handles(RSConstants.MODELS_INDEX));

            IReadOnlyList<int> groups = _cache.ArchivesToExamine(_cache.Table(RSConstants.SPRITES_INDEX));
            Assert.NotEmpty(groups);

            foreach (int group in groups)
            {
                using Bitmap? tile = renderer.Render(RSConstants.SPRITES_INDEX, group, Side);

                Assert.True(tile != null, "Sprite group " + group + " produced no tile at all.");
                Assert.Equal(Side, tile!.Width);
                Assert.Equal(Side, tile.Height);
            }
        }
    }
}
