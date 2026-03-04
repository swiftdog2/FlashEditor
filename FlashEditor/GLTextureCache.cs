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
    /// With the Hydra columnar format, texture metadata (Class238) doesn't contain
    /// direct sprite references — those live in the per-texture operation graphs
    /// (index 9). For now, textures are rendered as solid colours derived from
    /// field1835 (which encodes an RGB tint).
    /// </summary>
    public sealed class GLTextureCache
    {
        private readonly RSCache _cache;
        private readonly Dictionary<int, int> _textures = new();
        private readonly TextureManager _manager;

        public GLTextureCache(RSCache cache)
        {
            _cache = cache ?? throw new ArgumentNullException(nameof(cache));
            _manager = new TextureManager(cache);
            Debug("Initializing GLTextureCache", LOG_DETAIL.BASIC);
            _manager.Load();
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

            // Try loading the actual sprite texture from the cache
            if (def.spriteFileIds != null && def.spriteFileIds.Length > 0)
            {
                try
                {
                    var sprite = _cache.GetSprite(def.spriteFileIds[0]);
                    if (sprite?.GetFrameCount() > 0)
                    {
                        var frame = sprite.GetFrame(0);
                        if (frame?.thumb != null)
                        {
                            Debug($"Creating GL texture {textureId} from sprite {def.spriteFileIds[0]} ({frame.thumb.Width}x{frame.thumb.Height})", LOG_DETAIL.ADVANCED);
                            handle = CreateGLTexture(frame.thumb);
                            _textures[textureId] = handle;
                            return handle;
                        }
                    }
                }
                catch (Exception ex)
                {
                    Debug($"Failed to load sprite for texture {textureId}: {ex.Message}", LOG_DETAIL.ADVANCED);
                }
            }

            // Fall back to solid-colour 1x1 texture from the tint value.
            // field1835 == 0 means "no tint" — use white so the greyscale
            // vertex lighting passes through at the correct brightness.
            {
                int rgb = def.field1835;
                int r = (rgb >> 16) & 0xFF;
                int g = (rgb >> 8) & 0xFF;
                int b = rgb & 0xFF;
                if (r == 0 && g == 0 && b == 0) { r = 255; g = 255; b = 255; }

                Debug($"Generating solid texture {textureId} (rgb={r},{g},{b})", LOG_DETAIL.BASIC);

                using var bmp = new Bitmap(1, 1);
                bmp.SetPixel(0, 0, Color.FromArgb(255, r, g, b));
                handle = CreateGLTexture(bmp);
            }
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

        public void Dispose()
        {
            foreach (var kvp in _textures)
                GL.DeleteTexture(kvp.Value);
            _textures.Clear();
        }
    }
}
