using System;
using System.Drawing;

namespace FlashEditor.Definitions.Sprites {
    /// <summary>
    /// Texture definition metadata from the materials index (index 26).
    /// Fields correspond to Class238 in the Hydra client.
    /// The material index stores ALL texture definitions in a single file
    /// using a columnar (pass-based) binary format - decoded and encoded by
    /// <see cref="MaterialTable"/>.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///     The nineteen material fields are properties rather than fields so that assigning one
    ///     records <em>which</em> column changed. Without that the write path cannot tell an edited
    ///     record from an untouched one and has to choose between two wrong answers: replay the
    ///     stored bytes and discard every edit in silence, which is what it used to do, or re-encode
    ///     everything from fields and rewrite bytes nobody touched.
    ///     </para>
    ///     <para>
    ///     Assigning the value a field already holds is not an edit. That matters more here than it
    ///     looks: a property grid writes every cell back on commit, and treating those as edits would
    ///     rewrite the whole table - and its archive CRC - for a dialog somebody only opened.
    ///     </para>
    ///     <para>
    ///     <see cref="spriteFileIds"/>, <see cref="graph"/> and <see cref="thumb"/> are deliberately
    ///     plain fields: they come from index 9 and index 8, not from index 26, and merging them in
    ///     at load time must not mark the material table as edited.
    ///     </para>
    /// </remarks>
    public class TextureDefinition : IDisposable {
        /// <summary>The texture id, which is this record's slot in the material table.</summary>
        public int id;

        /// <summary>
        ///     Which material columns have been assigned a value different from the one decoded.
        /// </summary>
        /// <remarks>
        ///     A bit per <see cref="MaterialColumn"/>. Per column rather than per record because the
        ///     format is not canonical in three of its columns: a boolean column decodes many-to-one,
        ///     so re-encoding a column from its bool cannot reproduce a stored byte outside {0,1}.
        ///     Neither supported cache holds one, which is exactly why the granularity has to be
        ///     designed in rather than discovered - no sweep over this cache can catch it.
        /// </remarks>
        private int _dirtyColumns;

        private bool _field1825;
        private bool _field1822;
        private bool _field1833;
        private sbyte _field1829;
        private sbyte _field1830;
        private sbyte _field1820;
        private sbyte _field1816;
        private int _field1831;
        private sbyte _field1823;
        private sbyte _field1837;
        private bool _field1827;
        private bool _field1824;
        private sbyte _field1832;
        private bool _field1826;
        private bool _field1819;
        private bool _field1817;
        private int _field1821;
        private int _field1835;
        private int _field1818;

        // --- Class238 fields (columnar read order) ---

        /// <summary>Stored inverted: true when the byte is 0 (<c>Class260.java:116</c>).</summary>
        public bool field1825 {
            get => _field1825;
            set => Set(ref _field1825, value, MaterialColumn.Field1825);
        }

        /// <summary>True when the byte is 1 (<c>Class260.java:121</c>).</summary>
        public bool field1822 {
            get => _field1822;
            set => Set(ref _field1822, value, MaterialColumn.Field1822);
        }

        /// <summary>True when the byte is 1 (<c>Class260.java:126</c>).</summary>
        public bool field1833 {
            get => _field1833;
            set => Set(ref _field1833, value, MaterialColumn.Field1833);
        }

        /// <summary>Signed byte, read at <c>Class260.java:131</c>.</summary>
        public sbyte field1829 {
            get => _field1829;
            set => Set(ref _field1829, value, MaterialColumn.Field1829);
        }

        /// <summary>Signed byte, read at <c>Class260.java:136</c>.</summary>
        public sbyte field1830 {
            get => _field1830;
            set => Set(ref _field1830, value, MaterialColumn.Field1830);
        }

        /// <summary>Signed byte, read at <c>Class260.java:141</c>.</summary>
        public sbyte field1820 {
            get => _field1820;
            set => Set(ref _field1820, value, MaterialColumn.Field1820);
        }

        /// <summary>Signed byte, read at <c>Class260.java:146</c>.</summary>
        public sbyte field1816 {
            get => _field1816;
            set => Set(ref _field1816, value, MaterialColumn.Field1816);
        }

        /// <summary>
        /// The texture's representative colour, packed as a raw 16-bit RS HSL.
        /// </summary>
        /// <remarks>
        /// Not a speed or timing value, whatever the field tables say. The client feeds it to
        /// <c>Class345.method3825</c>, whose body is the standard HSL light-shade
        /// (<c>(hsl &amp; 0xff80) + clamped lightness</c>), and then to the palette lookup - see
        /// <c>Node_Sub16:79</c> and <c>Class278:731</c>. It is what the client draws wherever a
        /// texture cannot be generated, which in this cache is every texture id at or above 946.
        /// <para>
        /// Held unsigned while <c>Class260.java:151</c> casts the same two bytes to a signed
        /// <c>short</c>, so records the client reads as negative read as positive here. The stored
        /// bytes are identical either way - the encoder writes the low sixteen bits - but an editor
        /// must not "correct" a value above 32767 by storing a signed one.
        /// </para>
        /// </remarks>
        public int field1831 {
            get => _field1831;
            set => Set(ref _field1831, value, MaterialColumn.Field1831);
        }

        /// <summary>Signed byte, read at <c>Class260.java:156</c>.</summary>
        public sbyte field1823 {
            get => _field1823;
            set => Set(ref _field1823, value, MaterialColumn.Field1823);
        }

        /// <summary>Signed byte, read at <c>Class260.java:161</c>.</summary>
        public sbyte field1837 {
            get => _field1837;
            set => Set(ref _field1837, value, MaterialColumn.Field1837);
        }

        /// <summary>True when the byte is 1 (<c>Class260.java:166</c>).</summary>
        public bool field1827 {
            get => _field1827;
            set => Set(ref _field1827, value, MaterialColumn.Field1827);
        }

        /// <summary>
        ///     True when the byte is 1 (<c>Class260.java:171</c>); the pixel transposition flag the
        ///     graph evaluator is driven by.
        /// </summary>
        public bool field1824 {
            get => _field1824;
            set => Set(ref _field1824, value, MaterialColumn.Field1824);
        }

        /// <summary>Signed byte, read at <c>Class260.java:176</c>.</summary>
        public sbyte field1832 {
            get => _field1832;
            set => Set(ref _field1832, value, MaterialColumn.Field1832);
        }

        /// <summary>True when the byte is 1 (<c>Class260.java:181</c>).</summary>
        public bool field1826 {
            get => _field1826;
            set => Set(ref _field1826, value, MaterialColumn.Field1826);
        }

        /// <summary>True when the byte is 1 (<c>Class260.java:186</c>).</summary>
        public bool field1819 {
            get => _field1819;
            set => Set(ref _field1819, value, MaterialColumn.Field1819);
        }

        /// <summary>True when the byte is 1 (<c>Class260.java:191</c>).</summary>
        public bool field1817 {
            get => _field1817;
            set => Set(ref _field1817, value, MaterialColumn.Field1817);
        }

        /// <summary>Unsigned byte, read at <c>Class260.java:196</c>.</summary>
        public int field1821 {
            get => _field1821;
            set => Set(ref _field1821, value, MaterialColumn.Field1821);
        }

        /// <summary>
        ///     A full four-byte int of renderer state, read at <c>Class260.java:201</c>.
        /// </summary>
        /// <remarks>
        ///     Not a tint. It is passed through to the renderer at
        ///     <c>RenderType_Sub1.java:4441</c> and never multiplied into the generated pixels;
        ///     doing that here once scaled every texture towards black.
        /// </remarks>
        public int field1835 {
            get => _field1835;
            set => Set(ref _field1835, value, MaterialColumn.Field1835);
        }

        /// <summary>Unsigned byte, read at <c>Class260.java:206</c>.</summary>
        public int field1818 {
            get => _field1818;
            set => Set(ref _field1818, value, MaterialColumn.Field1818);
        }

        /// <summary>Sprite file IDs decoded from the TEXTURES index (9).</summary>
        public int[] spriteFileIds;

        /// <summary>Parsed procedural texture graph for lazy rendering.</summary>
        public TextureGraph graph;

        /// <summary>Thumbnail for GUI display.</summary>
        public Bitmap? thumb;

        /// <summary>
        ///     The 23 bytes this record was decoded from, or null when it was never decoded.
        /// </summary>
        /// <remarks>
        ///     Held so a column nobody edited re-encodes to what it came from rather than to what
        ///     its field would produce. Set by <see cref="MaterialTable"/> only - a record with no
        ///     stored bytes is encoded entirely from its fields.
        /// </remarks>
        internal byte[]? StoredRecord { get; set; }

        /// <summary>Whether any material column now differs from the bytes it was decoded from.</summary>
        public bool IsDirty => _dirtyColumns != 0;

        /// <summary>Whether one material column has been edited.</summary>
        /// <param name="column">The column to test.</param>
        /// <returns>Whether that column must be re-encoded from its field.</returns>
        internal bool IsColumnDirty(MaterialColumn column) => (_dirtyColumns & (1 << (int) column)) != 0;

        /// <summary>
        ///     Declares the stored bytes and the fields to agree again.
        /// </summary>
        /// <remarks>
        ///     Called after a decode, where they agree by construction, and after a save, where the
        ///     bytes just written have been adopted as the stored ones. Calling it at any other
        ///     point loses an edit.
        /// </remarks>
        internal void MarkClean() => _dirtyColumns = 0;

        /// <summary>
        ///     Assigns a field and records its column as edited, unless the value is unchanged.
        /// </summary>
        /// <typeparam name="T">The field's type.</typeparam>
        /// <param name="slot">The backing field.</param>
        /// <param name="value">The new value.</param>
        /// <param name="column">The column the field is stored in.</param>
        private void Set<T>(ref T slot, T value, MaterialColumn column) where T : struct, IEquatable<T> {
            if (slot.Equals(value))
                return;

            slot = value;
            _dirtyColumns |= 1 << (int) column;
        }

        /// <summary>Releases the thumbnail and drops the parsed graph.</summary>
        public void Dispose() {
            thumb?.Dispose();
            thumb = null;
            graph = null;
        }
    }
}
