using System;
using System.Drawing;

namespace FlashEditor.Definitions.Sprites {
    /// <summary>
    /// Texture definition metadata from the materials index (index 26).
    /// Fields correspond to Class238 in the Hydra client.
    /// The material index stores ALL texture definitions in a single file
    /// using a columnar (pass-based) binary format — decoded/encoded by
    /// <see cref="TextureManager"/>.
    /// </summary>
    public class TextureDefinition : IDisposable {
        public int id;

        // --- Class238 fields (columnar read order) ---
        public bool field1825;       // ubyte; true when byte == 0 (inverted)
        public bool field1822;       // ubyte; true when byte == 1
        public bool field1833;       // ubyte; true when byte == 1
        public sbyte field1829;      // signed byte
        public sbyte field1830;      // signed byte
        public sbyte field1820;      // signed byte
        public sbyte field1816;      // signed byte
        /// <summary>
        /// The texture's representative colour, packed as a raw 16-bit RS HSL.
        /// </summary>
        /// <remarks>
        /// Not a speed or timing value, whatever the field tables say. The client feeds it to
        /// <c>Class345.method3825</c>, whose body is the standard HSL light-shade
        /// (<c>(hsl &amp; 0xff80) + clamped lightness</c>), and then to the palette lookup - see
        /// <c>Node_Sub16:79</c> and <c>Class278:731</c>. It is what the client draws wherever a
        /// texture cannot be generated, which in this cache is every texture id at or above 946.
        /// </remarks>
        public int field1831;        // unsigned short (stored as short in client)
        public sbyte field1823;      // signed byte
        public sbyte field1837;      // signed byte
        public bool field1827;       // ubyte; true when byte == 1
        public bool field1824;       // ubyte; true when byte == 1 (pixel transposition flag)
        public sbyte field1832;      // signed byte
        public bool field1826;       // ubyte; true when byte == 1
        public bool field1819;       // ubyte; true when byte == 1
        public bool field1817;       // ubyte; true when byte == 1
        public int field1821;        // unsigned byte (stored as int)
        public int field1835;        // full 4-byte int
        public int field1818;        // unsigned byte (stored as int)

        /// <summary>Sprite file IDs decoded from the TEXTURES index (9).</summary>
        public int[] spriteFileIds;

        /// <summary>Parsed procedural texture graph for lazy rendering.</summary>
        public TextureGraph graph;

        /// <summary>Thumbnail for GUI display.</summary>
        public Bitmap? thumb;

        public void Dispose() {
            thumb?.Dispose();
            thumb = null;
            graph = null;
        }
    }
}
