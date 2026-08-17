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
        ///     elsewhere. Each row names the column and, in brackets, the client field it was read
        ///     off, so a reader who doubts a name has the string to search
        ///     <c>HydraScape/client/src</c> for without leaving the pane. <c>field1827</c> is
        ///     unnamed because no Java code in that tree reads it.
        /// </remarks>
        public IReadOnlyList<DetailField> Fields {
            get {
                var fields = new List<DetailField>(MaterialTable.ColumnCount + 4) {
                    new DetailField("texture id", TextureId.ToString(CultureInfo.InvariantCulture)),
                    new DetailField("index 9 graph", GraphState),
                    new DetailField("index 8 sprites", SpriteState),
                    new DetailField("suppress texture (aBoolean1825)", Record.suppressTexture.ToString()),
                    new DetailField("force 64x64 (aBoolean1822)", Record.force64x64.ToString()),
                    new DetailField("exclude from draw list (aBoolean1833)",
                        Record.excludeFromDrawList.ToString()),
                    new DetailField("colour gain (aByte1829)",
                        Record.colourGain.ToString(CultureInfo.InvariantCulture)),
                    new DetailField("grey blend weight (aByte1830)",
                        Record.greyBlendWeight.ToString(CultureInfo.InvariantCulture)),
                    new DetailField("effect program (aByte1820)",
                        Record.effectProgram.ToString(CultureInfo.InvariantCulture)),
                    new DetailField("effect params (aByte1816)",
                        Record.effectParams.ToString(CultureInfo.InvariantCulture)),
                    new DetailField("representative colour, 16-bit RS HSL (aShort1831)",
                        "0x" + Record.representativeHsl.ToString("X4", CultureInfo.InvariantCulture) + " -> 0x" +
                        RepresentativeRgb.ToString("X6", CultureInfo.InvariantCulture)),
                    new DetailField("scroll U (aByte1823)",
                        Record.scrollU.ToString(CultureInfo.InvariantCulture)),
                    new DetailField("scroll V (aByte1837)",
                        Record.scrollV.ToString(CultureInfo.InvariantCulture)),
                    new DetailField("field1827 (unread by the 637 client)", Record.field1827.ToString()),
                    new DetailField("transpose pixels (aBoolean1824)", Record.transposePixels.ToString()),
                    new DetailField("mipmap (aByte1832)", Record.mipmap.ToString(CultureInfo.InvariantCulture)),
                    new DetailField("repeat U (aBoolean1826)", Record.repeatU.ToString()),
                    new DetailField("repeat V (aBoolean1819)", Record.repeatV.ToString()),
                    new DetailField("half-float upload (aBoolean1817)", Record.halfFloatUpload.ToString()),
                    new DetailField("combine mode (anInt1821)",
                        Record.combineMode.ToString(CultureInfo.InvariantCulture)),
                    new DetailField("water params (anInt1835)",
                        "0x" + Record.waterParams.ToString("X8", CultureInfo.InvariantCulture)),
                    new DetailField("alpha mode (anInt1818)",
                        Record.alphaMode.ToString(CultureInfo.InvariantCulture))
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
    ///     <b>Eighteen of the nineteen columns are named for what the client does with them, and
    ///     every heading carries the client field beside the name.</b> The name is the claim and the
    ///     bracketed <c>aByte1829</c> is what makes it checkable without leaving the grid; the
    ///     evidence, cited line by line, is <c>reference/hydra-637-definitions/material-columns.md</c>.
    ///     <c>field1827</c> keeps its obfuscated name because no Java code in the 637 tree reads it,
    ///     and a plausible name here would be read as settled. One already was: <c>waterParams</c>
    ///     was taken for a tint and multiplied into the generated pixels, which scaled every texture
    ///     in the editor towards black.
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
        /// <summary>
        ///     Width shared by the nineteen material columns.
        /// </summary>
        /// <remarks>
        ///     Wider than the grid default because every heading carries its client field in
        ///     brackets, and a heading clipped to <c>greyBlendWeigh...</c> costs the reader the
        ///     citation that makes the name checkable. A list view column's width does not scale
        ///     with the list around it, so this is stated once rather than per column.
        /// </remarks>
        private const int NamedWidth = 170;

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
                //texture range, so a column's position is where its bytes are. Each heading keeps
                //the client field in brackets so the name above it can be checked against
                //HydraScape/client/src without leaving the grid.
                Flag("suppressTexture (aBoolean1825)", row => row.Record.suppressTexture,
                    (row, value) => row.Record.suppressTexture = value),
                Flag("force64x64 (aBoolean1822)", row => row.Record.force64x64,
                    (row, value) => row.Record.force64x64 = value),
                Flag("excludeFromDrawList (aBoolean1833)", row => row.Record.excludeFromDrawList,
                    (row, value) => row.Record.excludeFromDrawList = value),

                /* Unsigned rather than signed, and the correction that matters most on this tab:
                   every client read of these two masks & 0xff, so 255 is a near-total grey blend
                   and roughly a doubling of brightness. An sbyte surface showed both as -1, and
                   255 is the commonest non-zero value in the grey blend column of both caches. */
                Unsigned("colourGain (aByte1829)", row => row.Record.colourGain,
                    (row, value) => row.Record.colourGain = value),
                Unsigned("greyBlendWeight (aByte1830)", row => row.Record.greyBlendWeight,
                    (row, value) => row.Record.greyBlendWeight = value),

                Signed("effectProgram (aByte1820)", row => row.Record.effectProgram,
                    (row, value) => row.Record.effectProgram = value),
                Signed("effectParams (aByte1816)", row => row.Record.effectParams,
                    (row, value) => row.Record.effectParams = value),

                /* The only column that resolves to a colour. The cell text stays the stored 16-bit
                   HSL, which is the number an edit has to write back; the swatch is what the client
                   resolves it to, through the clamp Class345.method3825 applies. Converting the RGB
                   back would not reproduce it. */
                DefinitionColumn.EncodedColour<MaterialListing>("representativeHsl (aShort1831)",
                    row => row.Record.representativeHsl, row => row.RepresentativeRgb,
                    (row, value) => row.Record.representativeHsl = Math.Clamp(value, 0, 0xFFFF),
                    width: NamedWidth),

                Signed("scrollU (aByte1823)", row => row.Record.scrollU,
                    (row, value) => row.Record.scrollU = value),
                Signed("scrollV (aByte1837)", row => row.Record.scrollV,
                    (row, value) => row.Record.scrollV = value),

                //The one column with no name. Class260.java:166 assigns it and only oa.java:160 and
                //:880 read it, both native method argument lists, so there is nothing to name it
                //after and a guess here would read as settled.
                Flag("field1827 (unread)", row => row.Record.field1827,
                    (row, value) => row.Record.field1827 = value),

                Flag("transposePixels (aBoolean1824)", row => row.Record.transposePixels,
                    (row, value) => row.Record.transposePixels = value),
                Signed("mipmap (aByte1832)", row => row.Record.mipmap,
                    (row, value) => row.Record.mipmap = value),
                Flag("repeatU (aBoolean1826)", row => row.Record.repeatU,
                    (row, value) => row.Record.repeatU = value),
                Flag("repeatV (aBoolean1819)", row => row.Record.repeatV,
                    (row, value) => row.Record.repeatV = value),
                Flag("halfFloatUpload (aBoolean1817)", row => row.Record.halfFloatUpload,
                    (row, value) => row.Record.halfFloatUpload = value),
                Unsigned("combineMode (anInt1821)", row => row.Record.combineMode,
                    (row, value) => row.Record.combineMode = value),

                //Four bytes rather than one, so it is not clamped to a byte the way its neighbours
                //are. Packed water-shader parameters, not a tint - see
                //TextureDefinition.waterParams.
                DefinitionColumn.Number<MaterialListing>("waterParams (anInt1835)",
                    row => row.Record.waterParams, (row, value) => row.Record.waterParams = value,
                    NamedWidth),

                Unsigned("alphaMode (anInt1818)", row => row.Record.alphaMode,
                    (row, value) => row.Record.alphaMode = value)
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
        /// <param name="header">The column's name and, in brackets, the client field behind it.</param>
        /// <param name="read">Reads the flag off a row.</param>
        /// <param name="write">Writes an edited flag back.</param>
        /// <returns>The column.</returns>
        private static DefinitionColumn Flag(string header, Func<MaterialListing, bool> read,
            Action<MaterialListing, bool> write) {
            return DefinitionColumn.Flag(header, read, write, NamedWidth);
        }

        /// <summary>
        ///     A signed-byte column of the material table.
        /// </summary>
        /// <remarks>
        ///     Clamped to what one signed byte can hold rather than truncated. The column is one byte
        ///     wide, so a value outside the range has to be refused somewhere, and clamping at the
        ///     edit means the cell shows what was stored instead of the value wrapping into an
        ///     unrelated number on the next load.
        ///     <para>
        ///     Four columns use this and not seven. <c>colourGain</c> and <c>greyBlendWeight</c> are
        ///     stored as one byte and read <c>&amp; 0xff</c> by every client consumer, so they go
        ///     through <see cref="Unsigned"/> instead - a signed cell rendered their meaningful
        ///     maximum, 255, as -1.
        ///     </para>
        /// </remarks>
        /// <param name="header">The column's name and, in brackets, the client field behind it.</param>
        /// <param name="read">Reads the value off a row.</param>
        /// <param name="write">Writes an edited value back.</param>
        /// <returns>The column.</returns>
        private static DefinitionColumn Signed(string header, Func<MaterialListing, sbyte> read,
            Action<MaterialListing, sbyte> write) {
            return DefinitionColumn.Number<MaterialListing>(header, row => (int) read(row),
                (row, value) => write(row, (sbyte) Math.Clamp(value, sbyte.MinValue, sbyte.MaxValue)),
                NamedWidth);
        }

        /// <summary>An unsigned-byte column of the material table, clamped for the same reason.</summary>
        /// <param name="header">The column's name and, in brackets, the client field behind it.</param>
        /// <param name="read">Reads the value off a row.</param>
        /// <param name="write">Writes an edited value back.</param>
        /// <returns>The column.</returns>
        private static DefinitionColumn Unsigned(string header, Func<MaterialListing, int> read,
            Action<MaterialListing, int> write) {
            return DefinitionColumn.Number<MaterialListing>(header, row => read(row),
                (row, value) => write(row, Math.Clamp(value, 0, byte.MaxValue)), NamedWidth);
        }
    }
}
