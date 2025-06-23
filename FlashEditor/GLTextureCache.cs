using FlashEditor.cache;
using FlashEditor.Definitions.Sprites;
using FlashEditor.cache.util;
using FlashEditor.cache.sprites;
using OpenTK.Graphics.OpenGL;
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using static FlashEditor.Utils.DebugUtil;

namespace FlashEditor
{
    /// <summary>
    /// Creates OpenGL texture objects from cache texture definitions and memoises them.
    /// </summary>
    public sealed class GLTextureCache
    {
        private readonly RSCache _cache;
        private readonly Dictionary<int, int> _textures = new();
        private readonly TextureManager _manager;

        /// <summary>
        /// Initializes a new instance for the given cache and loads all texture definitions.
        /// </summary>
        public GLTextureCache(RSCache cache)
        {
            _cache = cache ?? throw new ArgumentNullException(nameof(cache));
            _manager = new TextureManager(cache);
            Debug("Initializing GLTextureCache", LOG_DETAIL.BASIC);
            _manager.Load();
            Debug("Textures loaded", LOG_DETAIL.BASIC);
        }

        /// <summary>
        /// Gets the OpenGL texture handle for the given texture id, loading it on demand.
        /// </summary>
        /// <param name="textureId">Texture definition id.</param>
        /// <returns>OpenGL texture handle or 0 if not found.</returns>
        public int GetTexture(int textureId)
        {
            Debug($"Request for texture {textureId}", LOG_DETAIL.ADVANCED);
            if (_textures.TryGetValue(textureId, out int handle))
            {
                Debug($"Texture {textureId} cached -> handle {handle}", LOG_DETAIL.ADVANCED);
                return handle;
            }

            if (!TextureManager.Textures.TryGetValue(textureId, out TextureDefinition def) || def.fileIds == null || def.fileIds.Length == 0)
            {
                Debug($"Texture definition {textureId} not found", LOG_DETAIL.BASIC);
                return 0;
            }

            Debug($"Loading sprite {def.fileIds[0]} for texture {textureId}", LOG_DETAIL.ADVANCED);
            SpriteDefinition sprite = _cache.GetSprite(def.fileIds[0]);
            Debug($"Creating bitmap for texture {textureId}", LOG_DETAIL.ADVANCED);
            Bitmap bmp = sprite.GetFrame(0).GetSprite();
            Debug($"Uploading texture {textureId} to GL", LOG_DETAIL.ADVANCED);
            handle = CreateGLTexture(bmp);
            _textures[textureId] = handle;
            Debug($"Texture {textureId} -> GL handle {handle}", LOG_DETAIL.ADVANCED);
            return handle;
        }

        private static int CreateGLTexture(Bitmap bmp)
        {
            int tex = GL.GenTexture();
            GL.BindTexture(TextureTarget.Texture2D, tex);
            GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMinFilter, (int)TextureMinFilter.Linear);
            GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMagFilter, (int)TextureMagFilter.Linear);

            BitmapData data = bmp.LockBits(new Rectangle(0, 0, bmp.Width, bmp.Height), ImageLockMode.ReadOnly, System.Drawing.Imaging.PixelFormat.Format32bppArgb);
            GL.TexImage2D(TextureTarget.Texture2D, 0, PixelInternalFormat.Rgba, bmp.Width, bmp.Height, 0, OpenTK.Graphics.OpenGL.PixelFormat.Bgra, PixelType.UnsignedByte, data.Scan0);
            bmp.UnlockBits(data);
            GL.GenerateMipmap(GenerateMipmapTarget.Texture2D);
            GL.BindTexture(TextureTarget.Texture2D, 0);
            return tex;
        }

        /// <summary>
        /// Deletes all OpenGL textures managed by this instance.
        /// </summary>
        public void Dispose()
        {
            foreach (var kvp in _textures)
                GL.DeleteTexture(kvp.Value);
            _textures.Clear();
        }
    }
}
