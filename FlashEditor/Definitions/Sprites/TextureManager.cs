using FlashEditor;
using System.Collections.Generic;
using FlashEditor.cache;

namespace FlashEditor.Definitions.Sprites
{
    /// <summary>
    /// Loads all texture definitions from the cache.
    /// </summary>
    public class TextureManager
    {
        private readonly RSCache cache;
        public readonly List<TextureDefinition> Textures = new List<TextureDefinition>();

        public TextureManager(RSCache cache)
        {
            this.cache = cache;
        }

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
                Textures.Add(def);
            }
        }
    }
}
