using FlashEditor;
using System.Collections.Generic;
using System.Drawing;
using FlashEditor.cache;
using static FlashEditor.Utils.DebugUtil;

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

        public void Load()
        {
            Debug("Loading texture reference table", LOG_DETAIL.ADVANCED);
            RSReferenceTable table = cache.GetReferenceTable(RSConstants.TEXTURES);
            RSEntry entry = table.GetEntry(0);
            if (entry == null)
            {
                Debug("Texture archive 0 not found", LOG_DETAIL.BASIC);
                return;
            }

            Debug($"Found {entry.GetValidFileIds().Length} texture entries", LOG_DETAIL.ADVANCED);
            Debug("Beginning texture decode", LOG_DETAIL.ADVANCED);

            var loader = new TextureLoader();
            foreach (int fileId in entry.GetValidFileIds())
            {
                Debug($"Reading texture {fileId}", LOG_DETAIL.ADVANCED);
                JagStream data = cache.ReadEntry(RSConstants.TEXTURES, 0, fileId);
                if (data == null)
                {
                    Debug($"Texture {fileId} returned null data", LOG_DETAIL.BASIC);
                    throw new Exception("Texture entry data is null");
                }
                Debug($"Decoding texture {fileId}", LOG_DETAIL.ADVANCED);
                var def = loader.Load(fileId, data.ToArray());
                Debug($"\tLoaded texture {def.id} with {def.fileIds?.Length ?? 0} sprites", LOG_DETAIL.ADVANCED);
                Debug($"Texture {def.id} references {def.fileIds?.Length ?? 0} sprites", LOG_DETAIL.ADVANCED);
                Textures[def.id] = def;
            }
            Debug("Finished loading textures", LOG_DETAIL.BASIC);
        }

        internal static Image GetThumbnailForTexture(string key) {
            if (int.TryParse(key, out int id) && Textures.TryGetValue(id, out var def) && def.thumb != null)
                return def.thumb;

            return new Bitmap(100, 100);
        }
    }
}
