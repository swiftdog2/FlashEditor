namespace FlashEditor.Definitions.Sprites
{
    /// <summary>
    /// Texture operation graph parsed from the TEXTURES index (index 9).
    /// Matches Class107 in the Hydra client — opcode-based format decoded by method1727.
    /// </summary>
    internal class Texture
    {
        // Opcode 40: primary sprite pairs (aShortArray922 + aShortArray919)
        private int[] _spriteIds;
        private int[] _spriteParams;

        // Opcode 41: secondary sprite pairs (aShortArray913 + aShortArray911)
        private int[] _spriteIds2;
        private int[] _spriteParams2;

        // Opcode 1-8: numeric fields
        private int _modelId;       // opcode 1 (anInt914)
        private int _field910 = -1; // opcode 2 (anInt910)
        private int _field920 = 128;// opcode 4 (anInt920)
        private int _field916 = 128;// opcode 5 (anInt916)
        private int _field924;      // opcode 6 (anInt924)
        private int _field915;      // opcode 7 (anInt915)
        private int _field917;      // opcode 8 (anInt917)

        // Opcode 9-16: blend fields
        private int _blendType;     // aByte923
        private int _blendColour = -1; // anInt918
        private bool _mirrored;     // opcode 10 (aBoolean909)

        /// <summary>Primary sprite file IDs (opcode 40, aShortArray922).</summary>
        public int[] FileIds => _spriteIds ?? System.Array.Empty<int>();

        /// <summary>Number of primary sprite references.</summary>
        public int Count => _spriteIds?.Length ?? 0;

        /// <summary>Returns the sprite file ID at the specified index.</summary>
        public int GetFileId(int index) => _spriteIds[index];

        /// <summary>
        /// Decodes a texture entry using the opcode-based format (Class107.method1725/1727).
        /// </summary>
        public static Texture Decode(JagStream buffer)
        {
            var tex = new Texture();
            for (;;)
            {
                int opcode = buffer.ReadUnsignedByte();
                if (opcode == 0)
                    break;
                tex.DecodeOpcode(buffer, opcode);
            }
            return tex;
        }

        private void DecodeOpcode(JagStream b, int op)
        {
            switch (op)
            {
                case 1: _modelId = b.ReadUnsignedShort(); break;
                case 2: _field910 = b.ReadUnsignedShort(); break;
                case 4: _field920 = b.ReadUnsignedShort(); break;
                case 5: _field916 = b.ReadUnsignedShort(); break;
                case 6: _field924 = b.ReadUnsignedShort(); break;
                case 7: _field915 = b.ReadUnsignedByte(); break;
                case 8: _field917 = b.ReadUnsignedByte(); break;
                case 9: _blendType = 3; _blendColour = 8224; break;
                case 10: _mirrored = true; break;
                case 11: _blendType = 1; break;
                case 12: _blendType = 4; break;
                case 13: _blendType = 5; break;
                case 14:
                    _blendType = 2;
                    _blendColour = b.ReadUnsignedByte() * 256;
                    break;
                case 15:
                    _blendType = 3;
                    _blendColour = b.ReadUnsignedShort();
                    break;
                case 16:
                    _blendType = 3;
                    _blendColour = b.ReadInt();
                    break;
                case 40:
                {
                    int count = b.ReadUnsignedByte();
                    _spriteIds = new int[count];
                    _spriteParams = new int[count];
                    for (int i = 0; i < count; i++)
                    {
                        _spriteIds[i] = b.ReadUnsignedShort();
                        _spriteParams[i] = b.ReadUnsignedShort();
                    }
                    break;
                }
                case 41:
                {
                    int count = b.ReadUnsignedByte();
                    _spriteIds2 = new int[count];
                    _spriteParams2 = new int[count];
                    for (int i = 0; i < count; i++)
                    {
                        _spriteIds2[i] = b.ReadUnsignedShort();
                        _spriteParams2[i] = b.ReadUnsignedShort();
                    }
                    break;
                }
            }
        }
    }
}
