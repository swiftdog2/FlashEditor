namespace FlashEditor.Definitions.Sprite {
    class Texture {
        public static Texture Decode(JagStream buffer) {
            buffer.ReadShort();
            buffer.ReadByte();
            int count = buffer.ReadByte();

            Texture texture = new Texture(count);

            for(int i = 0; i < count; i++)
                texture.fileIds[i] = buffer.ReadUnsignedShort();

            return texture;
        }

        private int[] fileIds;

        public Texture(int count) {
            fileIds = new int[count];
        }

        public int GetIds(int i) {
            return fileIds[i];
        }
    }
}
