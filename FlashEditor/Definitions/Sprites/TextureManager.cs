using FlashEditor;
using System.Collections.Generic;
using System.Drawing;
using FlashEditor.cache;

namespace FlashEditor.Definitions.Sprites
{
    /// <summary>
    /// Loads all texture definitions from the cache.
    /// </summary>
    public class TextureManager
    {
        private readonly RSCache cache;
        public static readonly SortedDictionary<int, TextureDefinition> Textures = new();

        public TextureManager(RSCache cache)
        {
            this.cache = cache;
        }

        /// <summary>
        /// Reads all texture definitions from the cache into <see cref="Textures"/>.
        /// </summary>
        public void Load()
        {
            RSReferenceTable table = cache.GetReferenceTable(RSConstants.TEXTURES);
            RSEntry entry = table.GetEntry(0);
            if (entry == null)
                return;

            var loader = new TextureLoader();
            foreach (int fileId in entry.GetValidFileIds())
            {
                JagStream data = cache.ReadEntry(RSConstants.TEXTURES, 0, fileId);
                if(data == null) {
                    throw new Exception("Texture entry data is null");
                    continue;
                }
                var def = loader.Load(fileId, data.ToArray());
                Textures[def.id] = def;
            }
        }

        /// <summary>
        /// Attempts to fetch the cached thumbnail for the given texture id string.
        /// </summary>
        internal static Image GetThumbnailForTexture(string key) {
            if (int.TryParse(key, out int id) && Textures.TryGetValue(id, out var def) && def.thumb != null)
                return def.thumb;

            return new Bitmap(100, 100);
        }
    }
}
