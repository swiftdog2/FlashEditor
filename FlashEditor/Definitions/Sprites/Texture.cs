namespace FlashEditor.Definitions.Sprites
{
    /// <summary>
    /// Simple texture container parsed from a texture definition entry.
    /// </summary>
    internal class Texture
    {
        /// <summary>
        /// Parses a texture entry from a <see cref="JagStream"/>.
        /// </summary>
        /// <param name="buffer">Stream positioned at the texture entry.</param>
        /// <returns>The populated <see cref="Texture"/> instance.</returns>
        public static Texture Decode(JagStream buffer)
        {
            buffer.ReadShort();
            buffer.ReadByte();
            int count = buffer.ReadByte();

            Texture texture = new Texture(count);

            for (int i = 0; i < count; i++)
                texture._fileIds[i] = buffer.ReadUnsignedShort();

            return texture;
        }

        private readonly int[] _fileIds;

        public Texture(int count)
        {
            _fileIds = new int[count];
        }

        /// <summary>
        /// Returns the referenced file id at the specified index.
        /// </summary>
        public int GetFileId(int index) => _fileIds[index];
    }
}
