using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using FlashEditor.Cache;
using FlashEditor.Definitions.Editing;
using FlashEditor.IO;

namespace FlashEditor.Definitions.Sprites {
    /// <summary>
    ///     One index-26 material record as a list row.
    /// </summary>
    /// <remarks>
    ///     A wrapper rather than the definition itself, so the grid can show what the slot means
    ///     against index 9 - whether a procedural graph exists for the same id - without hanging a
    ///     presentation property off the record every other reader of it would inherit.
    ///     <para>
    ///     <b>The record it wraps is the one the renderer holds.</b> It comes out of
    ///     <see cref="TextureManager.Materials"/>, which is the same object
    ///     <see cref="TextureManager.Textures"/> hands the map path and the model draw path, so an
    ///     edit here is visible to them without a reload and the write path encodes exactly what the
    ///     grid shows.
    ///     </para>
    /// </remarks>
    public sealed class MaterialListing : IDetailRow {
        /// <summary>Binds one material record to where the table that holds it lives.</summary>
        /// <param name="address">The group and file of the whole table, carrying the slot as its id.</param>
        /// <param name="record">The decoded record.</param>
        public MaterialListing(DefinitionAddress address, TextureDefinition record) {
            Address = address;
            Record = record ?? throw new ArgumentNullException(nameof(record));
        }

        /// <summary>Where the table this record belongs to lives in the cache.</summary>
        /// <remarks>
        ///     The same address for every row: index 26 is one file, so a row's identity is its slot
        ///     and the address is constant. <see cref="DefinitionAddress.DefinitionId"/> carries the
        ///     slot so a cross-navigation to a texture id lands on the right row.
        /// </remarks>
        public DefinitionAddress Address { get; }

        /// <summary>The record, which is the object the renderer reads.</summary>
        public TextureDefinition Record { get; }

        /// <summary>The texture id, which is this record's slot in the table.</summary>
        public int TextureId => Record.id;

        /// <summary>
        ///     Whether index 9 holds procedural content for the same id.
        /// </summary>
        /// <remarks>
        ///     The column that stops the grid implying index 26 and index 9 are one population. They
        ///     are the same size in the vanilla b639 capture and are not in the repack, where the
        ///     slots past the last graph carry nothing but a colour - and for those the colour is the
        ///     whole of what a player ever sees, which makes them the rows that matter most rather
        ///     than the ones to filter out.
        /// </remarks>
        public string GraphState => Record.graph != null ? "index 9" : "none";

        /// <summary>Sprite ids the index-9 record names, for the detail pane.</summary>
        public string SpriteState => DetailText.Ids(Record.spriteFileIds);

        /// <summary>The colour the client draws where the pixels cannot be generated.</summary>
        public int RepresentativeRgb => TextureManager.RepresentativeRgb(Record);

        /// <inheritdoc/>
        public string Summary =>
            "Texture " + TextureId + " - " + (Record.graph != null
                ? "index 9 holds a graph for this id"
                : "no procedural content: the representative colour is all the client draws");

        /// <inheritdoc/>
        /// <remarks>
        ///     Nineteen rows in the order the file stores its columns, plus what the id resolves to
        ///     elsewhere. Seventeen of the nineteen are named after the client's own obfuscated
        ///     fields, because nothing settles what they mean and a guessed name in a detail pane
        ///     reads as an established one.
        /// </remarks>
        public IReadOnlyList<DetailField> Fields {
            get {
                var fields = new List<DetailField>(MaterialTable.ColumnCount + 4) {
                    new DetailField("texture id", TextureId.ToString(CultureInfo.InvariantCulture)),
                    new DetailField("index 9 graph", GraphState),
                    new DetailField("index 8 sprites", SpriteState),
                    new DetailField("field1825", Record.field1825.ToString()),
                    new DetailField("field1822", Record.field1822.ToString()),
                    new DetailField("field1833", Record.field1833.ToString()),
                    new DetailField("field1829", Record.field1829.ToString(CultureInfo.InvariantCulture)),
                    new DetailField("field1830", Record.field1830.ToString(CultureInfo.InvariantCulture)),
                    new DetailField("field1820", Record.field1820.ToString(CultureInfo.InvariantCulture)),
                    new DetailField("field1816", Record.field1816.ToString(CultureInfo.InvariantCulture)),
                    new DetailField("field1831 (16-bit RS HSL)",
                        "0x" + Record.field1831.ToString("X4", CultureInfo.InvariantCulture) + " -> 0x" +
                        RepresentativeRgb.ToString("X6", CultureInfo.InvariantCulture)),
                    new DetailField("field1823", Record.field1823.ToString(CultureInfo.InvariantCulture)),
                    new DetailField("field1837", Record.field1837.ToString(CultureInfo.InvariantCulture)),
                    new DetailField("field1827", Record.field1827.ToString()),
                    new DetailField("field1824 (pixel transposition)", Record.field1824.ToString()),
                    new DetailField("field1832", Record.field1832.ToString(CultureInfo.InvariantCulture)),
                    new DetailField("field1826", Record.field1826.ToString()),
                    new DetailField("field1819", Record.field1819.ToString()),
                    new DetailField("field1817", Record.field1817.ToString()),
                    new DetailField("field1821", Record.field1821.ToString(CultureInfo.InvariantCulture)),
                    new DetailField("field1835",
                        "0x" + Record.field1835.ToString("X8", CultureInfo.InvariantCulture)),
                    new DetailField("field1818", Record.field1818.ToString(CultureInfo.InvariantCulture))
                };

                return fields;
            }
        }
    }

    /// <summary>
    ///     Index 26 as a definition list: the roster of texture slots and the nineteen columns of
    ///     per-slot render state beside it.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///     <b>Every row addresses the same file.</b> The index is one group holding one file
    ///     (<c>Class260.java:106</c>), so a slot is a position inside that file rather than a file of
    ///     its own: <see cref="AddressOf"/> answers the same group and file for every row and
    ///     <see cref="Encode"/> returns the whole table. That is what makes an edit to one field a
    ///     rewrite of the index, and it is stated here rather than discovered by whoever wonders why
    ///     the panel's commit rewrote everything.
    ///     </para>
    ///     <para>
    ///     <b>Two of the nineteen columns have established meanings and the other seventeen do
    ///     not.</b> <c>field1831</c> is the representative colour in raw 16-bit RS HSL and gets a
    ///     swatch; <c>field1824</c> is the pixel transposition flag the graph evaluator is driven by
    ///     and gets a flag cell. The rest carry the client's own obfuscated field names on purpose - a
    ///     plausible name here would be read as settled, and one already was: <c>field1835</c> was
    ///     taken for a tint and multiplied into the generated pixels, which scaled every texture in
    ///     the editor towards black.
    ///     </para>
    ///     <para>
    ///     <b>The table is read through <see cref="TextureManager"/> rather than decoded again.</b>
    ///     A second decode would give the grid its own <see cref="TextureDefinition"/> objects while
    ///     the renderer kept the first set, so an edit would reach one of them and the save would
    ///     encode the other. It also means the payload is never read - <see cref="ReadsPayload"/> is
    ///     false - because the manager has already read it.
    ///     </para>
    /// </remarks>
    public sealed class MaterialListDescriptor : DefinitionListDescriptor<MaterialListing> {
        private readonly IReadOnlyList<DefinitionColumn> columns;

        /// <summary>Lists every slot the material table declares a record for.</summary>
        public MaterialListDescriptor() {
            columns = new[] {
                DefinitionColumn.ReadOnly<MaterialListing>("Texture", row => row.TextureId, 80),
                DefinitionColumn.Thumbnail<MaterialListing>("Preview", RSConstants.MATERIALS,
                    row => row.TextureId, 90),
                DefinitionColumn.ReadOnly<MaterialListing>("Graph", row => row.GraphState, 80),

                //The nineteen columns follow, in the order the file stores them, because that order
                //is the format: Class260.java:114-208 runs one pass per column over the whole
                //texture range, so a column's position is where its bytes are.
                Flag("field1825", row => row.Record.field1825, (row, value) => row.Record.field1825 = value),
                Flag("field1822", row => row.Record.field1822, (row, value) => row.Record.field1822 = value),
                Flag("field1833", row => row.Record.field1833, (row, value) => row.Record.field1833 = value),
                Signed("field1829", row => row.Record.field1829, (row, value) => row.Record.field1829 = value),
                Signed("field1830", row => row.Record.field1830, (row, value) => row.Record.field1830 = value),
                Signed("field1820", row => row.Record.field1820, (row, value) => row.Record.field1820 = value),
                Signed("field1816", row => row.Record.field1816, (row, value) => row.Record.field1816 = value),

                /* The one column whose meaning is settled well enough to draw. The cell text stays
                   the stored 16-bit HSL, which is the number an edit has to write back; the swatch
                   is what the client resolves it to, through the same clamp Class345.method3825
                   applies. Converting the RGB back would not reproduce the stored value. */
                DefinitionColumn.EncodedColour<MaterialListing>("field1831",
                    row => row.Record.field1831, row => row.RepresentativeRgb,
                    (row, value) => row.Record.field1831 = Math.Clamp(value, 0, 0xFFFF)),

                Signed("field1823", row => row.Record.field1823, (row, value) => row.Record.field1823 = value),
                Signed("field1837", row => row.Record.field1837, (row, value) => row.Record.field1837 = value),
                Flag("field1827", row => row.Record.field1827, (row, value) => row.Record.field1827 = value),

                //The other settled column: the evaluator transposes the generated pixels when it is
                //set (TextureGraphEvaluator, reached through TextureManager.EnsureRendered).
                Flag("field1824", row => row.Record.field1824, (row, value) => row.Record.field1824 = value),

                Signed("field1832", row => row.Record.field1832, (row, value) => row.Record.field1832 = value),
                Flag("field1826", row => row.Record.field1826, (row, value) => row.Record.field1826 = value),
                Flag("field1819", row => row.Record.field1819, (row, value) => row.Record.field1819 = value),
                Flag("field1817", row => row.Record.field1817, (row, value) => row.Record.field1817 = value),
                Unsigned("field1821", row => row.Record.field1821, (row, value) => row.Record.field1821 = value),

                //Four bytes rather than one, so it is not clamped to a byte the way its neighbours
                //are. Renderer state, not a tint - see TextureDefinition.field1835.
                DefinitionColumn.Number<MaterialListing>("field1835", row => row.Record.field1835,
                    (row, value) => row.Record.field1835 = value, 110),

                Unsigned("field1818", row => row.Record.field1818, (row, value) => row.Record.field1818 = value)
            };
        }

        /// <inheritdoc/>
        public override int IndexId => RSConstants.MATERIALS;

        /// <inheritdoc/>
        public override string RowNoun => "material";

        /// <inheritdoc/>
        public override IReadOnlyList<DefinitionColumn> Columns => columns;

        /// <inheritdoc/>
        public override bool IsEditable => true;

        /// <inheritdoc/>
        /// <remarks>
        ///     The table arrives through <see cref="TextureManager"/>, so opening index 26's group a
        ///     second time would decode the same bytes into records nothing else holds.
        /// </remarks>
        public override bool ReadsPayload => false;

        /// <summary>
        ///     One address per declared slot, all naming the single file the table is stored as.
        /// </summary>
        /// <remarks>
        ///     Not the inherited walk over the index's files: that yields one address, because the
        ///     whole index is one file, and the grid would then hold one row for a table of hundreds
        ///     of records. The slot rides in the definition id instead, which is also what lets a
        ///     link from elsewhere in the editor select a texture id here.
        ///     <para>
        ///     Slots the table marks absent are skipped rather than shown empty. An existence byte
        ///     other than 1 means the client reads no material state for that id at all
        ///     (<c>Class260.java:110</c>), so there is nothing to display and nothing to edit - and
        ///     the byte itself is replayed verbatim by the encoder whatever this does.
        ///     </para>
        /// </remarks>
        /// <param name="cache">The open cache.</param>
        /// <returns>The addresses to load.</returns>
        public override IEnumerable<DefinitionAddress> Enumerate(RSCache cache) {
            MaterialTable table = TableOf(cache);
            CacheAddressing addressing = CacheAddressing.For(RSConstants.MATERIALS);
            int groupId = addressing.GroupOf(MaterialTable.WholeTableDefinitionId);
            int fileId = addressing.FileOf(MaterialTable.WholeTableDefinitionId);

            for (int slot = 0; slot < table.Count; slot++)
                if (table.Slots[slot] != null)
                    yield return new DefinitionAddress(groupId, fileId, slot);
        }

        /// <inheritdoc/>
        public override MaterialListing Decode(RSCache cache, DefinitionAddress address, JagStream payload) {
            MaterialTable table = TableOf(cache);

            TextureDefinition? record = address.DefinitionId >= 0 && address.DefinitionId < table.Count
                ? table.Slots[address.DefinitionId]
                : null;

            if (record == null)
                throw new InvalidDataException(
                    "The material table declares no record at slot " + address.DefinitionId + ".");

            return new MaterialListing(address, record);
        }

        /// <inheritdoc/>
        public override DefinitionAddress AddressOf(MaterialListing row) {
            if (row == null)
                throw new ArgumentNullException(nameof(row));
            return row.Address;
        }

        /// <summary>
        ///     Re-encodes the whole table, which is what one row's edit rewrites.
        /// </summary>
        /// <remarks>
        ///     <b>The row is not read, and that is the format rather than an oversight.</b> Every
        ///     record shares one file, and the file is column-major - a record's 23 bytes are
        ///     scattered across nineteen places in it - so there is no smaller unit to write.
        ///     <para>
        ///     Through <see cref="TextureManager.EncodeColumnar"/>, which replays each record's
        ///     stored bytes per column and re-encodes only the columns whose fields disagree with
        ///     them. Encoding from fields instead would normalise every aliased byte in the file on
        ///     the first edit anyone made, and the caller compares against the stored bytes, so a
        ///     table nobody has changed stages nothing.
        ///     </para>
        /// </remarks>
        /// <param name="row">The edited row, whose table is the unit being written.</param>
        /// <returns>The whole index-26 file.</returns>
        public override JagStream Encode(MaterialListing row) {
            if (row == null)
                throw new ArgumentNullException(nameof(row));

            return TextureManager.EncodeColumnar();
        }

        /// <summary>
        ///     The loaded material table, loading it for this cache if nothing has yet.
        /// </summary>
        /// <remarks>
        ///     <see cref="TextureManager.EnsureLoaded"/> rather than <c>Load</c>: <c>Load</c> begins
        ///     by disposing every definition in a store the whole application shares, so calling it
        ///     for a cache that is already loaded would destroy the rasters the model path and the
        ///     Textures tab are holding. Called from the list panel's worker, which is the thread
        ///     this decode belongs on.
        /// </remarks>
        /// <param name="cache">The open cache.</param>
        /// <returns>The table.</returns>
        private static MaterialTable TableOf(RSCache cache) {
            if (cache == null)
                throw new ArgumentNullException(nameof(cache));

            TextureManager.EnsureLoaded(cache);

            return TextureManager.Materials ?? throw new InvalidDataException(
                "This cache holds no readable index-26 material table, so there are no texture slots to list.");
        }

        /// <summary>A boolean column of the material table.</summary>
        /// <param name="header">The client's own field name.</param>
        /// <param name="read">Reads the flag off a row.</param>
        /// <param name="write">Writes an edited flag back.</param>
        /// <returns>The column.</returns>
        private static DefinitionColumn Flag(string header, Func<MaterialListing, bool> read,
            Action<MaterialListing, bool> write) {
            return DefinitionColumn.Flag(header, read, write, 90);
        }

        /// <summary>
        ///     A signed-byte column of the material table.
        /// </summary>
        /// <remarks>
        ///     Clamped to what one signed byte can hold rather than truncated. The column is one byte
        ///     wide, so a value outside the range has to be refused somewhere, and clamping at the
        ///     edit means the cell shows what was stored instead of the value wrapping into an
        ///     unrelated number on the next load.
        /// </remarks>
        /// <param name="header">The client's own field name.</param>
        /// <param name="read">Reads the value off a row.</param>
        /// <param name="write">Writes an edited value back.</param>
        /// <returns>The column.</returns>
        private static DefinitionColumn Signed(string header, Func<MaterialListing, sbyte> read,
            Action<MaterialListing, sbyte> write) {
            return DefinitionColumn.Number<MaterialListing>(header, row => (int) read(row),
                (row, value) => write(row, (sbyte) Math.Clamp(value, sbyte.MinValue, sbyte.MaxValue)), 90);
        }

        /// <summary>An unsigned-byte column of the material table, clamped for the same reason.</summary>
        /// <param name="header">The client's own field name.</param>
        /// <param name="read">Reads the value off a row.</param>
        /// <param name="write">Writes an edited value back.</param>
        /// <returns>The column.</returns>
        private static DefinitionColumn Unsigned(string header, Func<MaterialListing, int> read,
            Action<MaterialListing, int> write) {
            return DefinitionColumn.Number<MaterialListing>(header, row => read(row),
                (row, value) => write(row, Math.Clamp(value, 0, byte.MaxValue)), 90);
        }
    }
}
