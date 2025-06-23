using FlashEditor;
using System;
using System.Collections.Generic;
using System.Drawing;
using static FlashEditor.Utils.DebugUtil;

namespace FlashEditor.Definitions.Sprites {
    /// <summary>
    /// Representation of a texture definition from the RuneScape cache.
    /// </summary>
    public class TextureDefinition : IDefinition {
        public int field1777;
        public bool field1778;
        public int id;
        public int[] fileIds;
        public int[] field1780;
        public int[] field1781;
        public int[] field1786;
        public int animationSpeed;
        public int animationDirection;

        public int[] pixels;

        /// <summary>
        /// Thumbnail representation for GUI display.
        /// </summary>
        public Bitmap? thumb;
        public int width;

        public static TextureDefinition DecodeFromStream(int id, JagStream stream) {
            var def = new TextureDefinition { id = id };
            def.Decode(stream);
            return def;
        }

        public void Decode(JagStream s, int[] xteaKey = null) {
            Debug($"Decoding texture {id}", LOG_DETAIL.ADVANCED);
            field1777 = s.ReadUnsignedShort();
            field1778 = s.ReadByte() != 0;

            int count = s.ReadUnsignedByte();
            Debug($"Texture has {count} sprite references", LOG_DETAIL.ADVANCED);
            fileIds = new int[count];
            for (int i = 0 ; i < count ; i++)
            {
                fileIds[i] = s.ReadUnsignedShort();
                Debug($"\tSprite file {i}: {fileIds[i]}", LOG_DETAIL.INSANE);
            }
            Debug($"Parsed {count} sprite references", LOG_DETAIL.ADVANCED);

            if (count > 1) {
                field1780 = new int[count - 1];
                for (int i = 0 ; i < count - 1 ; i++)
                    field1780[i] = s.ReadUnsignedByte();

                field1781 = new int[count - 1];
                for (int i = 0 ; i < count - 1 ; i++)
                    field1781[i] = s.ReadUnsignedByte();
            }

            field1786 = new int[count];
            for (int i = 0 ; i < count ; i++)
            {
                field1786[i] = s.ReadInt();
                Debug($"\tColor {i}: 0x{field1786[i]:X8}", LOG_DETAIL.INSANE);
            }
            Debug("Color table loaded", LOG_DETAIL.ADVANCED);

            animationDirection = s.ReadUnsignedByte();
            animationSpeed = s.ReadUnsignedByte();
            Debug($"Animation dir {animationDirection}, speed {animationSpeed}", LOG_DETAIL.ADVANCED);
            Debug("Texture decode finished", LOG_DETAIL.ADVANCED);
        }



        public JagStream Encode() {
            var s = new JagStream();
            s.WriteShort(field1777);
            s.WriteByte((byte) (field1778 ? 1 : 0));

            int count = fileIds?.Length ?? 0;
            s.WriteByte((byte) count);
            for (int i = 0 ; i < count ; i++)
                s.WriteShort((short) fileIds[i]);

            if (count > 1) {
                for (int i = 0 ; i < count - 1 ; i++)
                    s.WriteByte((byte) field1780[i]);
                for (int i = 0 ; i < count - 1 ; i++)
                    s.WriteByte((byte) field1781[i]);
            }

            for (int i = 0 ; i < count ; i++)
                s.WriteInteger(field1786[i]);

            s.WriteByte((byte) animationDirection);
            s.WriteByte((byte) animationSpeed);
            s.Flip();
            return s;
        }
    }
}
