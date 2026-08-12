using FlashEditor.Cache;
using FlashEditor.Definitions.Sprites;
using OpenTK.Graphics.OpenGL;
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using static FlashEditor.Utils.DebugUtil;

namespace FlashEditor.Rendering
{
    /// <summary>
    /// Creates OpenGL texture objects from cache texture definitions and memoises them.
    /// With the Hydra columnar format, texture metadata (Class238) doesn't contain
    /// direct sprite references - those live in the per-texture operation graphs
    /// (index 9). For now, textures are rendered as solid colours derived from
    /// field1835 (which encodes an RGB tint).
    /// </summary>
    /// <remarks>
    /// The constructor runs <see cref="TextureManager.Load"/>, which decodes the index-26
    /// columnar block and all 946 index-9 graphs on whatever thread opens the cache - the UI
    /// thread, on every cache open, whether or not the Textures tab is ever visited. It
    /// evaluates no graphs and decodes no sprites, so it is far cheaper than the render sweep,
    /// but it is still synchronous work on the wrong thread. Lifting it out so both the
    /// Textures worker and <see cref="GetTexture"/> trigger it on demand touches the map and
    /// model render paths and belongs in its own change.
    /// </remarks>
    public sealed class GLTextureCache
    {
        private readonly RSCache _cache;
        private readonly Dictionary<int, int> _textures = new();
        /// <summary>
        ///     Creates a texture cache for one GL context.
        /// </summary>
        /// <remarks>
        ///     <b>One of these per context, not one per application.</b> A GL texture handle belongs
        ///     to the context that created it, so the particle preview cannot bind a handle the
        ///     Entities viewport uploaded. What it can share is everything below the handle: the
        ///     decoded index-26 metadata and index-9 graphs live in <see cref="TextureManager"/>'s
        ///     static store, and this holds only the per-context handle map on top of them.
        ///     <para>
        ///     <see cref="TextureManager.EnsureLoaded"/> rather than <c>Load</c>, and the difference
        ///     matters: <c>Load</c> opens with <c>Clear</c>, which disposes every definition in that
        ///     shared store. Constructing a second cache used to mean tearing down the first one's
        ///     rasters mid-session.
        ///     </para>
        /// </remarks>
        /// <param name="cache">The open cache to read texture data from.</param>
        public GLTextureCache(RSCache cache)
        {
            _cache = cache ?? throw new ArgumentNullException(nameof(cache));
            Debug("Initializing GLTextureCache", LOG_DETAIL.BASIC);
            TextureManager.EnsureLoaded(cache);
            Debug("Textures loaded", LOG_DETAIL.BASIC);
        }

        public int GetTexture(int textureId)
        {
            Debug($"Request for texture {textureId}", LOG_DETAIL.ADVANCED);
            if (_textures.TryGetValue(textureId, out int handle))
            {
                Debug($"Texture {textureId} cached -> handle {handle}", LOG_DETAIL.ADVANCED);
                return handle;
            }

            if (!TextureManager.Textures.TryGetValue(textureId, out TextureDefinition def))
            {
                Debug($"Texture definition {textureId} not found", LOG_DETAIL.BASIC);
                return 0;
            }

            // Lazily render the texture if not yet done
            TextureManager.EnsureRendered(def);

            if (def.thumb != null)
            {
                Debug($"Creating GL texture {textureId} from pre-loaded thumbnail ({def.thumb.Width}x{def.thumb.Height})", LOG_DETAIL.ADVANCED);
                handle = CreateGLTexture(def.thumb);
                _textures[textureId] = handle;
                return handle;
            }

            // Fall back to a solid 1x1 texture in the material's own colour, which is what the
            // client does for a texture it cannot generate. field1835 was used here before, but
            // that is renderer state rather than a colour and is zero for most of the cache.
            {
                int rgb = TextureManager.RepresentativeRgb(def);
                int r = (rgb >> 16) & 0xFF;
                int g = (rgb >> 8) & 0xFF;
                int b = rgb & 0xFF;

                Debug($"Generating solid texture {textureId} (rgb={r},{g},{b})", LOG_DETAIL.BASIC);

                using var bmp = new Bitmap(1, 1);
                bmp.SetPixel(0, 0, Color.FromArgb(255, r, g, b));
                handle = CreateGLTexture(bmp);
            }
            _textures[textureId] = handle;
            Debug($"Texture {textureId} -> GL handle {handle}", LOG_DETAIL.ADVANCED);
            return handle;
        }

        /// <summary>One material rasterised the way the client rasterises it, before any GL call.</summary>
        /// <param name="Pixels">Packed ARGB, row-major, <paramref name="Side"/> square.</param>
        /// <param name="Side">Edge length in pixels.</param>
        /// <param name="RepeatS">Whether to repeat rather than clamp horizontally.</param>
        /// <param name="RepeatT">Whether to repeat rather than clamp vertically.</param>
        /// <param name="Mipmapped">Whether the material asks for mipmaps.</param>
        private readonly record struct RasterisedMaterial(int[] Pixels, int Side, bool RepeatS, bool RepeatT,
            bool Mipmapped);

        /// <summary>Materials rasterised off the paint path, keyed by material id.</summary>
        private readonly System.Collections.Concurrent.ConcurrentDictionary<int, RasterisedMaterial> _warmed = new();

        /// <summary>GL handles for the warmed materials, created on the paint path.</summary>
        private readonly Dictionary<int, int> _materialTextures = new();

        /// <summary>
        ///     Rasterises a material the way the client does, off the thread that paints.
        /// </summary>
        /// <remarks>
        ///     This is the deliberate answer to evaluating a procedural texture graph from
        ///     <c>Gl_Paint</c>: it is not done there at all. Index 9 holds operation graphs, not
        ///     pixels, and evaluating one is unbounded work - <c>TextureGraphEvaluator</c> carries a
        ///     fifteen-second budget precisely because some of them approach it - so a graph
        ///     evaluated inside a paint handler stalls the UI thread for as long as it takes. It
        ///     makes no GL calls, so it is safe on any thread; <see cref="GetParticleTexture"/> does
        ///     the upload later, on the thread that holds the context.
        ///     <para>
        ///     Which rasterisation is <c>Class364.method3931</c>'s (<c>:113-121</c>) and it is per
        ///     material, not per feature: alpha comes from the graph's own alpha output node when
        ///     <c>anInt1818 == 2</c> or <c>aByte1820</c> is 1 or 7, and is derived from the colour
        ///     otherwise. The size is 64 when <c>aBoolean1822</c> is set and the renderer's default
        ///     otherwise (<c>:110</c>), and the wrap modes come from <c>aBoolean1826</c> for S and
        ///     <c>aBoolean1819</c> for T (<c>Class42_Sub1.method383:11-12</c>) - note that those two
        ///     are the opposite way round to the argument order.
        ///     </para>
        /// </remarks>
        /// <param name="materialId">The material to rasterise.</param>
        /// <returns>Whether the material resolved to a graph and was rasterised.</returns>
        public bool PrewarmParticleMaterial(int materialId)
        {
            if (materialId < 0 || _warmed.ContainsKey(materialId))
                return _warmed.ContainsKey(materialId);

            if (!TextureManager.Textures.TryGetValue(materialId, out TextureDefinition def) || def?.graph == null)
            {
                Debug($"Particle material {materialId} has no texture graph", LOG_DETAIL.BASIC);
                return false;
            }

            //Class364.method3931:110. The client reads its own default from the renderer; 128 is
            //what this project has always rendered a texture at.
            int side = def.field1822 ? 64 : 128;

            bool sampleAlpha = def.field1818 == 2 || def.field1820 == 1 || def.field1820 == 7;

            int[] pixels = TextureGraphEvaluator.RenderArgb(def.graph, side, side, _cache, def.field1824, materialId,
                sampleAlpha);

            if (pixels == null)
            {
                Debug($"Particle material {materialId} did not rasterise", LOG_DETAIL.BASIC);
                return false;
            }

            _warmed[materialId] = new RasterisedMaterial(pixels, side, def.field1826, def.field1819,
                def.field1832 != 0);

            Debug($"Particle material {materialId} warmed at {side}x{side}, alphaOutput={sampleAlpha}",
                LOG_DETAIL.BASIC);
            return true;
        }

        /// <summary>
        ///     The GL texture for a particle's material, or 0 when it has not been warmed yet.
        /// </summary>
        /// <remarks>
        ///     Called from the paint handler once per material batch, so it does nothing but a
        ///     dictionary lookup and, on the first frame after a warm, one upload. It deliberately
        ///     never rasterises: a material nobody warmed returns 0 and the caller falls back to a
        ///     flat texture for that frame, which is a visible wrong picture rather than a frozen
        ///     window.
        ///     <para>
        ///     Kept apart from <see cref="GetTexture"/>, which serves the model draw from
        ///     <c>def.thumb</c>. That path derives alpha from the colour and is shared with the
        ///     Textures tab's thumbnails, so widening it to the client's per-material rule would
        ///     change how every model and every thumbnail is drawn - a separate change with its own
        ///     evidence, not a side effect of teaching particles to sample their material.
        ///     </para>
        /// </remarks>
        /// <param name="materialId">The material named by the particle.</param>
        /// <returns>A GL texture handle, or 0.</returns>
        public int GetParticleTexture(int materialId)
        {
            if (materialId < 0)
                return 0;

            if (_materialTextures.TryGetValue(materialId, out int handle))
                return handle;

            if (!_warmed.TryGetValue(materialId, out RasterisedMaterial warm))
                return 0;

            handle = GL.GenTexture();
            GL.BindTexture(TextureTarget.Texture2D, handle);

            //Mipmaps are generated below when the material asks for them, so the min filter has to
            //agree - asking for a mipmapped filter without supplying levels leaves the texture
            //incomplete and every sample comes back black.
            GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMinFilter,
                warm.Mipmapped ? (int)TextureMinFilter.LinearMipmapLinear : (int)TextureMinFilter.Linear);
            GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMagFilter,
                (int)TextureMagFilter.Linear);
            GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureWrapS,
                warm.RepeatS ? (int)TextureWrapMode.Repeat : (int)TextureWrapMode.ClampToEdge);
            GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureWrapT,
                warm.RepeatT ? (int)TextureWrapMode.Repeat : (int)TextureWrapMode.ClampToEdge);

            //Bgra because the pixels are packed ARGB in a little-endian int, which is B, G, R, A in
            //memory order. Rgba here silently swaps red and blue.
            GL.TexImage2D(TextureTarget.Texture2D, 0, PixelInternalFormat.Rgba, warm.Side, warm.Side, 0,
                OpenTK.Graphics.OpenGL.PixelFormat.Bgra, PixelType.UnsignedByte, warm.Pixels);

            if (warm.Mipmapped)
                GL.GenerateMipmap(GenerateMipmapTarget.Texture2D);

            GL.BindTexture(TextureTarget.Texture2D, 0);

            _materialTextures[materialId] = handle;
            return handle;
        }

        private static int CreateGLTexture(Bitmap bmp)
        {
            int tex = GL.GenTexture();
            GL.BindTexture(TextureTarget.Texture2D, tex);
            GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMinFilter, (int)TextureMinFilter.Linear);
            GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMagFilter, (int)TextureMagFilter.Linear);
            GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureWrapS, (int)TextureWrapMode.Repeat);
            GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureWrapT, (int)TextureWrapMode.Repeat);

            BitmapData data = bmp.LockBits(new Rectangle(0, 0, bmp.Width, bmp.Height), ImageLockMode.ReadOnly, System.Drawing.Imaging.PixelFormat.Format32bppArgb);
            GL.TexImage2D(TextureTarget.Texture2D, 0, PixelInternalFormat.Rgba, bmp.Width, bmp.Height, 0, OpenTK.Graphics.OpenGL.PixelFormat.Bgra, PixelType.UnsignedByte, data.Scan0);
            bmp.UnlockBits(data);
            GL.GenerateMipmap(GenerateMipmapTarget.Texture2D);
            GL.BindTexture(TextureTarget.Texture2D, 0);
            return tex;
        }

        public void Dispose()
        {
            foreach (var kvp in _textures)
                GL.DeleteTexture(kvp.Value);
            _textures.Clear();

            foreach (var kvp in _materialTextures)
                GL.DeleteTexture(kvp.Value);
            _materialTextures.Clear();
            _warmed.Clear();
        }
    }
}
