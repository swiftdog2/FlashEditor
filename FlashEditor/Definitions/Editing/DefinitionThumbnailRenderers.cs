using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using FlashEditor.Cache;
using FlashEditor.Definitions.Sprites;
using FlashEditor.IO;

namespace FlashEditor.Definitions.Editing {
    /// <summary>
    ///     Turns one index's id into a picture.
    /// </summary>
    /// <remarks>
    ///     Called on <see cref="DefinitionThumbnailCache"/>'s producer thread, one call at a time,
    ///     and <b>never</b> on the UI thread. An implementation may therefore read the cache, decode
    ///     and rasterise freely, and must not touch a control, an <c>ImageList</c>, or any bitmap it
    ///     has already handed back.
    ///     <para>
    ///     Returning null means "this id has no picture", which the cache records so the id is not
    ///     asked for again. It is a permanent answer, not a retry: a renderer that cannot produce a
    ///     tile <i>yet</i> has nowhere to say so, because there is nothing above it that would ever
    ///     ask a second time.
    ///     </para>
    /// </remarks>
    public interface IDefinitionThumbnailRenderer {
        /// <summary>Whether this renderer draws ids from the given index.</summary>
        /// <param name="indexId">The index.</param>
        /// <returns><c>true</c> when <see cref="Render"/> should be asked.</returns>
        bool Handles(int indexId);

        /// <summary>Draws one id, or returns null when it has no picture.</summary>
        /// <param name="indexId">The index the id addresses.</param>
        /// <param name="id">The id.</param>
        /// <param name="side">The tile side in pixels.</param>
        /// <returns>An unattached bitmap the caller owns, or null.</returns>
        Bitmap? Render(int indexId, int id, int side);
    }

    /// <summary>
    ///     Draws index-8 sprite sets as the sprite grid already draws them.
    /// </summary>
    /// <remarks>
    ///     Frame 0 only. A set with more than one frame is expanded row by row by the sprite tab
    ///     rather than here, because 44 of the vanilla capture's 4,593 sets are multi-frame and
    ///     rasterising every frame of every set to show one tile each would render eleven thousand
    ///     pictures to fill four and a half thousand cells.
    ///     <para>
    ///     Everything about how a sprite is drawn - the transparency checkerboard, the letterboxing,
    ///     the four-state empty/failed vocabulary - comes from <see cref="SpritePainter"/> rather
    ///     than being decided again here, so a thumbnail and the sprite tab cannot disagree about
    ///     what a record contains. In particular the pixels go through
    ///     <see cref="SpritePainter.ToDisplayBitmap"/> and never through <c>SpriteDefinition.thumb</c>:
    ///     the rasteriser's buffer is labelled premultiplied and holds straight ARGB, which is
    ///     invisible while every pixel is opaque and wrong for exactly the frames carrying an alpha
    ///     plane.
    ///     </para>
    /// </remarks>
    public sealed class SpriteThumbnailRenderer : IDefinitionThumbnailRenderer, IDisposable {
        private readonly RSCache cache;

        //One font per tile side rather than per tile. The precedent for getting this wrong is in
        //this repository: a font created per row cost 4,593 GDI objects on the sprite page.
        private readonly Dictionary<int, Font> markerFonts = new Dictionary<int, Font>();

        private readonly bool composited;

        /// <summary>Draws sprite sets out of an open cache.</summary>
        /// <param name="cache">The open cache to read from.</param>
        /// <param name="composited">
        ///     Whether the frame is composited onto a tile with the transparency checkerboard and a
        ///     marker for an empty or failed record.
        /// </param>
        /// <remarks>
        ///     <b>A grid wants a tile and a canvas wants a sprite, and they are not the same
        ///     picture.</b> A tile is square, padded, and backed by the checkerboard that tells a
        ///     user which pixels are transparent - exactly right when the sprite is the subject.
        ///     On an interface canvas the sprite is a layer over other layers, so the checkerboard
        ///     becomes opaque grey squares covering whatever the interface put underneath, which
        ///     reads as a corrupt sprite. Uncomposited keeps the alpha and lets the layers below
        ///     show through.
        /// </remarks>
        public SpriteThumbnailRenderer(RSCache cache, bool composited = true) {
            this.cache = cache ?? throw new ArgumentNullException(nameof(cache));
            this.composited = composited;
        }

        /// <inheritdoc/>
        public bool Handles(int indexId) => indexId == RSConstants.SPRITES_INDEX;

        /// <inheritdoc/>
        public Bitmap? Render(int indexId, int id, int side) {
            Font marker = MarkerFont(side);

            /* The file id is read off the reference table rather than assumed to be 0. Every group
               in both supported caches holds exactly one file whose id is 0, but a single-file
               group's id is not always 0 - index 23 is the case that proves it - and CacheAddressing
               refuses to answer for a group-per-id index for that reason. */
            int[] fileIds;
            try {
                fileIds = cache.GetFileIds(RSConstants.SPRITES_INDEX, id);
            }
            catch (Exception) {
                return SpritePainter.RenderTile(null, side, SpriteTileContent.Failed, marker);
            }

            if (fileIds.Length == 0)
                return null;

            var set = new SpriteDefinition();
            Bitmap? picture = null;

            try {
                set.Decode(new JagStream(cache.ReadFileBytes(RSConstants.SPRITES_INDEX, id, fileIds[0])));

                /* The tile shows frame 0, so the tile's state is frame 0's - and a frame with a
                   zero-area plane is empty rather than failed. 2,377 of the vanilla capture's
                   11,177 frames store one and they are legitimate records, so this cannot be
                   decided from the canvas, which is the set's rather than the frame's. */
                SpriteTileContent content = SpritePainter.ContentOf(set, 0);
                if (content == SpriteTileContent.Picture) {
                    picture = SpritePainter.ToDisplayBitmap(set.GetFrame(0));
                    if (picture == null)
                        content = SpriteTileContent.Empty;
                }

                if (!composited) {
                    /* The frame itself, at its own size, alpha intact. Not scaled to the requested
                       side either: a canvas draws a sprite into the rectangle its component
                       resolved to, and pre-scaling here to a square would distort every sprite
                       whose component is not square. The caller does the one scale that is wanted. */
                    if (picture == null)
                        return null;

                    Bitmap bare = picture;
                    picture = null;
                    return bare;
                }

                return SpritePainter.RenderTile(picture, side, content, marker);
            }
            catch (Exception) {
                //A tile rather than a null, because a group that will not decode is a fact about
                //that record and drawing nothing would present it as an id with no picture.
                return SpritePainter.RenderTile(null, side, SpriteTileContent.Failed, marker);
            }
            finally {
                picture?.Dispose();

                //The rendered frames are a pinned pixel buffer and a GDI bitmap each, and the tile
                //has already taken the only copy that is wanted.
                set.Dispose();
            }
        }

        /// <inheritdoc/>
        public void Dispose() {
            foreach (Font font in markerFonts.Values)
                font.Dispose();

            markerFonts.Clear();
        }

        /// <summary>
        ///     The font the empty and failed markers are written in, sized from the tile.
        /// </summary>
        /// <remarks>
        ///     Derived rather than stated, the same way the sprite tab derives its own, so a marker
        ///     stays legible at a tile side this renderer was never tried at. Only the producer
        ///     thread reaches this, so the dictionary needs no lock.
        /// </remarks>
        private Font MarkerFont(int side) {
            if (markerFonts.TryGetValue(side, out Font? font))
                return font;

            font = new Font(FontFamily.GenericSansSerif, Math.Max(7f, side / 6f),
                FontStyle.Regular, GraphicsUnit.Pixel);
            markerFonts[side] = font;
            return font;
        }
    }

    /// <summary>
    ///     Draws index-9 texture graphs, falling back to the colour index 26 declares.
    /// </summary>
    /// <remarks>
    ///     <b>The flat colour is not a placeholder.</b> Index 26 declares more textures than index 9
    ///     holds graphs for, and for every id with no graph the client draws that declared colour and
    ///     nothing else - so it is the correct answer for those ids rather than a stand-in for a
    ///     failure. It is also what a graph that overruns its budget or throws falls back to, and in
    ///     that case it <i>is</i> a stand-in; the two are indistinguishable on screen, which is a
    ///     known limitation of showing a texture as one tile.
    ///     <para>
    ///     <b>The graph is evaluated at the tile side, not at the 128 pixels the Textures tab uses.</b>
    ///     Evaluation is per pixel per node and a graph can compose whole other textures, which is why
    ///     the evaluator carries a fifteen-second ceiling at all; rendering at a grid tile's side
    ///     rather than at 128 cuts the sample count by the square of the ratio, and it is what keeps
    ///     one pathological texture from holding the single producer thread for a quarter of a
    ///     minute. The cost is that a tile is a coarse sampling of the texture rather than a
    ///     downscaling of it, so fine detail in a noisy graph is not reduced, it is missed.
    ///     </para>
    /// </remarks>
    public sealed class TextureThumbnailRenderer : IDefinitionThumbnailRenderer {
        private readonly RSCache cache;

        /// <summary>Draws textures out of an open cache.</summary>
        /// <param name="cache">The open cache to read from.</param>
        public TextureThumbnailRenderer(RSCache cache) {
            this.cache = cache ?? throw new ArgumentNullException(nameof(cache));
        }

        /// <summary>
        ///     Whether an index addresses a texture.
        /// </summary>
        /// <remarks>
        ///     Both halves answer, because a texture id is one id addressing two indexes: the graph
        ///     lives on index 9 and the declared colour on index 26, and a descriptor naming either
        ///     means the same texture.
        /// </remarks>
        /// <param name="indexId">The index.</param>
        /// <returns><c>true</c> when this renderer should be asked.</returns>
        public bool Handles(int indexId) =>
            indexId == RSConstants.TEXTURES || indexId == RSConstants.MATERIALS;

        /// <inheritdoc/>
        public Bitmap? Render(int indexId, int id, int side) {
            /* TextureManager.Textures is static and TextureManager.Clear disposes everything in it,
               so a cache reopen can empty this dictionary underneath the producer. The read is
               guarded rather than locked because there is no lock to take, and the failure is a
               missing tile rather than a wrong one - the cache's generation refuses whatever this
               produced against the cache that has gone. */
            TextureDefinition? definition;
            try {
                if (!TextureManager.Textures.TryGetValue(id, out definition) || definition == null)
                    return null;
            }
            catch (Exception) {
                return null;
            }

            if (definition.graph != null) {
                Bitmap? rendered = TextureGraphEvaluator.Render(
                    definition.graph, side, side, cache, definition.field1824, definition.id);

                if (rendered != null)
                    return rendered;
            }

            return Solid(TextureManager.RepresentativeRgb(definition), side);
        }

        private static Bitmap Solid(int rgb, int side) {
            var tile = new Bitmap(side, side, PixelFormat.Format32bppArgb);

            using Graphics graphics = Graphics.FromImage(tile);
            graphics.Clear(Color.FromArgb(0xFF, (rgb >> 16) & 0xFF, (rgb >> 8) & 0xFF, rgb & 0xFF));

            return tile;
        }
    }
}
