using FlashEditor;
namespace FlashEditor.Definitions.Sprites
{
    /// <summary>
    /// Decodes raw texture definition data.
    /// </summary>
    public class TextureLoader
    {
        public TextureDefinition Load(int id, byte[] data)
        {
            return TextureDefinition.DecodeFromStream(id, new JagStream(data));
        }
    }
}
