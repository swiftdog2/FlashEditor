using System;
using System.Collections.Generic;
using FlashEditor.Cache;
using FlashEditor.Definitions.Editing;
using FlashEditor.IO;

namespace FlashEditor.Definitions.Billboards {
    /// <summary>
    ///     One billboard from index 29 as a list row.
    /// </summary>
    /// <remarks>
    ///     A billboard id is a file id within the index's single group, so the row's id is the file
    ///     id and there is no second level to descend into: the record says what the quad is, and
    ///     which face of which model wears it is stated by the model, not here.
    /// </remarks>
    public sealed class BillboardListing {
        /// <summary>Binds one decoded billboard to where it came from.</summary>
        /// <param name="address">The group and file, and the billboard id they carry.</param>
        /// <param name="record">The decoded record.</param>
        public BillboardListing(DefinitionAddress address, BillboardDefinition record) {
            Address = address;
            Record = record ?? throw new ArgumentNullException(nameof(record));
        }

        /// <summary>Where the record lives in the cache.</summary>
        public DefinitionAddress Address { get; }

        /// <summary>The decoded record.</summary>
        public BillboardDefinition Record { get; }

        /// <summary>The billboard id, which is its file id in group 0.</summary>
        public int BillboardId => Record.Id;

        /// <summary>The material this quad is drawn with, or nothing when it has none.</summary>
        /// <remarks>
        ///     Null rather than -1 so "no material" reads as an empty cell instead of sorting among
        ///     the real ids. The stored form is a 16-bit sentinel, which the record keeps as it found
        ///     it.
        /// </remarks>
        public object? MaterialId => Record.MaterialId < 0 ? null : Record.MaterialId;

        /// <summary>Quad width in 1/128 screen units.</summary>
        /// <remarks>
        ///     A screen-space size rather than a texture dimension: the client sizes the quad as
        ///     <c>scale * width * fov / (z * 128)</c> after placing it at the source face's centroid
        ///     (Renderable_Sub1.java:2044-2070).
        /// </remarks>
        public int Width => Record.Width;

        /// <summary>Quad height in the same units.</summary>
        public int Height => Record.Height;

        /// <summary>Which raster loop draws the quad.</summary>
        public int RasterMode => Record.RasterMode;

        /// <summary>How the texel is combined with the face colour.</summary>
        public int CombineMode => Record.CombineMode;

        /// <summary>The signed byte the 637 client reads and throws away.</summary>
        /// <remarks>
        ///     A column because it is a real field of the format that a majority of records carry,
        ///     and because it has to survive a save verbatim. Its meaning is unknown and the heading
        ///     deliberately does not guess at one.
        /// </remarks>
        public sbyte UnusedByte3 => Record.UnusedByte3;

        /// <summary>Whether the quad is suppressed while the shader renderer is active.</summary>
        public string HiddenOnShaderRenderer => Record.HiddenOnShaderRenderer ? "yes" : "no";

        /// <summary>Whether the face the quad replaces is dropped from the draw list.</summary>
        public string HidesSourceFace => Record.HidesSourceFace ? "yes" : "no";

        /// <summary>The opcodes the record stored, in the order it stored them.</summary>
        /// <remarks>
        ///     Worth a column here more than anywhere else in the editor. Eight distinct orderings
        ///     occur across this index and not one of them is ascending - opcode 1 is written last in
        ///     every record - so the stored order is the thing an encoder has to replay rather than
        ///     derive.
        /// </remarks>
        public string OpcodeOrder {
            get {
                var parts = new List<string>(Record.Opcodes.Count);
                for (int i = 0; i < Record.Opcodes.Count; i++)
                    parts.Add(Record.Opcodes[i].Opcode.ToString());
                return string.Join(",", parts);
            }
        }
    }

    /// <summary>
    ///     Index 29 as a definition list: one flat row per billboard.
    /// </summary>
    /// <remarks>
    ///     Flat because the index is: one group holding every record, read by the client as
    ///     <c>getChildFromFolder(0, id)</c> (Class177.java:21). There is no group level to show and
    ///     no name hash to recover - the table's flags are zero - so a record is addressable only by
    ///     its id.
    ///     <para>
    ///     <b>Editable in the numeric fields.</b> Every opcode on this index is independent, so
    ///     changing one rewrites that opcode's payload in place and leaves the recorded order alone.
    ///     The two flags are editable too but parse strictly: an unrecognised cell leaves the record
    ///     as it was rather than guessing, because a flag's presence in the opcode stream is its only
    ///     statement and a wrong guess would silently drop or add one.
    ///     </para>
    /// </remarks>
    public sealed class BillboardListDescriptor : DefinitionListDescriptor<BillboardListing> {
        private readonly IReadOnlyList<DefinitionColumn> columns;

        /// <summary>Lists every billboard the index declares.</summary>
        public BillboardListDescriptor() {
            columns = new[] {
                DefinitionColumn.ReadOnly<BillboardListing>("Billboard", row => row.BillboardId, 100),
                DefinitionColumn.Number<BillboardListing>("Material", row => row.MaterialId,
                    (row, value) => row.Record.MaterialId = value, 90),
                DefinitionColumn.Number<BillboardListing>("Width", row => row.Width,
                    (row, value) => row.Record.Width = value, 80),
                DefinitionColumn.Number<BillboardListing>("Height", row => row.Height,
                    (row, value) => row.Record.Height = value, 80),
                DefinitionColumn.Number<BillboardListing>("Raster", row => row.RasterMode,
                    (row, value) => row.Record.RasterMode = value, 80),
                DefinitionColumn.Number<BillboardListing>("Combine", row => row.CombineMode,
                    (row, value) => row.Record.CombineMode = value, 80),
                DefinitionColumn.Number<BillboardListing>("Opcode 3", row => (int) row.UnusedByte3,
                    (row, value) => row.Record.UnusedByte3 = (sbyte) Math.Clamp(value, sbyte.MinValue, sbyte.MaxValue), 90),
                DefinitionColumn.Text<BillboardListing>("Hide on shader", row => row.HiddenOnShaderRenderer,
                    (row, value) => SetFlag(text => row.Record.HiddenOnShaderRenderer = text, value), 130),
                DefinitionColumn.Text<BillboardListing>("Hides face", row => row.HidesSourceFace,
                    (row, value) => SetFlag(text => row.Record.HidesSourceFace = text, value), 110),
                DefinitionColumn.ReadOnly<BillboardListing>("Opcodes", row => row.OpcodeOrder, 130)
            };
        }

        /// <inheritdoc/>
        public override int IndexId => RSConstants.CONFIG_BILLBOARD;

        /// <inheritdoc/>
        public override string RowNoun => "billboard";

        /// <inheritdoc/>
        public override IReadOnlyList<DefinitionColumn> Columns => columns;

        /// <inheritdoc/>
        public override bool IsEditable => true;

        /// <inheritdoc/>
        public override BillboardListing Decode(RSCache cache, DefinitionAddress address, JagStream payload) {
            var record = new BillboardDefinition { Id = address.DefinitionId };
            record.Decode(payload);
            return new BillboardListing(address, record);
        }

        /// <inheritdoc/>
        public override DefinitionAddress AddressOf(BillboardListing row) {
            if (row == null)
                throw new ArgumentNullException(nameof(row));
            return row.Address;
        }

        /// <inheritdoc/>
        public override JagStream Encode(BillboardListing row) {
            if (row == null)
                throw new ArgumentNullException(nameof(row));
            return row.Record.Encode();
        }

        /// <summary>
        ///     Applies a flag cell, or leaves the record alone when the cell says neither.
        /// </summary>
        /// <remarks>
        ///     Strict rather than truthy. The flag's presence in the opcode stream is the whole of
        ///     its state, so a cell that cannot be read as yes or no must change nothing at all -
        ///     treating anything non-empty as "set" would add an opcode on a typo.
        /// </remarks>
        /// <param name="apply">Sets the flag on the record.</param>
        /// <param name="text">The cell's text.</param>
        private static void SetFlag(Action<bool> apply, string? text) {
            string trimmed = (text ?? string.Empty).Trim();

            if (trimmed.Equals("yes", StringComparison.OrdinalIgnoreCase) ||
                trimmed.Equals("true", StringComparison.OrdinalIgnoreCase) ||
                trimmed == "1")
                apply(true);
            else if (trimmed.Equals("no", StringComparison.OrdinalIgnoreCase) ||
                     trimmed.Equals("false", StringComparison.OrdinalIgnoreCase) ||
                     trimmed == "0")
                apply(false);
        }
    }
}
