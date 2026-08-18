using BrightIdeasSoftware;
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Windows.Forms;

namespace FlashEditor.Definitions.Editing {
    /// <summary>One named value of a decoded record, as a detail-grid row.</summary>
    /// <remarks>
    ///     A name and a rendered string rather than a typed value. The records these describe carry
    ///     arrays, sentinels and flag bits whose meaning is in how they are written out, so the
    ///     rendering is part of the statement and doing it at the last moment would put it in the
    ///     column instead of in the record that knows what the value means.
    /// </remarks>
    public sealed class DetailField {
        /// <summary>Names one value.</summary>
        /// <param name="name">What the value is, including the opcode that carries it where that helps.</param>
        /// <param name="value">The value, rendered.</param>
        public DetailField(string name, string value) {
            Name = name ?? throw new ArgumentNullException(nameof(name));
            Value = value ?? string.Empty;
        }

        /// <summary>What the value is.</summary>
        public string Name { get; }

        /// <summary>The value, rendered.</summary>
        public string Value { get; }
    }

    /// <summary>
    ///     A record that can show every field it carries beside a list, without the list knowing what
    ///     kind of record it is.
    /// </summary>
    /// <remarks>
    ///     The indexes that need this - 27's two particle families, 33's manifest and screens, 24 and
    ///     25's menus and messages - each hold two unrelated formats in one index, so a shared detail
    ///     pane is the only thing the two halves of such a tab can agree on. It is deliberately the
    ///     smallest such surface rather than an attempt to unify the records.
    /// </remarks>
    public interface IDetailRow {
        /// <summary>The record in one line, for the header above the detail grid.</summary>
        string Summary { get; }

        /// <summary>Every value the record carries, in the order the format states them.</summary>
        IReadOnlyList<DetailField> Fields { get; }
    }

    /// <summary>
    ///     The two-column grid a tab puts beside its list to show everything a selected record holds.
    /// </summary>
    /// <remarks>
    ///     Its own type rather than three copies of the same twenty lines. The font is pinned for the
    ///     reason every grid in this project pins it: the form sets Consolas 12 on the tab control and
    ///     every child inherits it, which is half again what these columns are laid out for.
    /// </remarks>
    public sealed class DetailFieldGrid : FastObjectListView {
        /// <summary>
        ///     What the value column keeps for itself however long the field names run.
        /// </summary>
        /// <remarks>
        ///     Wide enough for the values these panes actually hold, which are flags, small integers
        ///     and hex words. Without a floor the field column takes the pane and the values leave
        ///     the control entirely, which is what a stated 320 and 760 did in a pane a third of a
        ///     tab wide: the value column sat behind a horizontal scrollbar nobody scrolls, so the
        ///     grid read as a list of names with no values at all.
        /// </remarks>
        private const int ValueFloor = 90;

        /// <summary>Room for the cell margin and the grid line, which the text measurement excludes.</summary>
        private const int NamePadding = 16;

        private readonly OLVColumn fieldColumn;

        private IReadOnlyList<DetailField> shown = Array.Empty<DetailField>();

        /// <summary>Creates an empty grid with its two columns already built.</summary>
        public DetailFieldGrid() {
            Dock = DockStyle.Fill;
            Font = new Font("Consolas", 9F);

            /* FullRowSelect is also what makes a truncated cell readable: the list shows the full
               text of a clipped value on hover, and only ever for a full-row-select list view.
               Several field names carry the client field the name above them was read off, and a
               citation clipped mid-name reads as checkable when it is not. */
            FullRowSelect = true;
            GridLines = true;
            ShowGroups = false;
            UseFiltering = true;
            View = View.Details;

            //Delegates rather than aspect names: a name looked up by reflection blanks the column
            //when the property is renamed, where a delegate stops compiling.
            fieldColumn = AddColumn("Field", row => ((DetailField) row).Name);
            OLVColumn value = AddColumn("Value", row => ((DetailField) row).Value);
            value.FillsFreeSpace = true;
            value.MinimumWidth = ValueFloor;
        }

        /// <summary>Shows one record's fields, or clears the grid when there is no record.</summary>
        /// <remarks>
        ///     Deliberately not called <c>Show</c>: this derives from a <see cref="Control"/>, and an
        ///     overload of <c>Control.Show</c> that means something else entirely is the kind of name
        ///     that reads correctly and does the wrong thing.
        /// </remarks>
        /// <param name="row">The selected record, or null.</param>
        public void ShowFields(IDetailRow? row) {
            if (row == null) {
                shown = Array.Empty<DetailField>();
                ClearObjects();
                return;
            }

            shown = new List<DetailField>(row.Fields);
            SetObjects(shown);
            SizeFieldColumn();
        }

        /// <summary>Re-divides the two columns when the pane holding them changes width.</summary>
        /// <param name="e">The event data.</param>
        protected override void OnClientSizeChanged(EventArgs e) {
            base.OnClientSizeChanged(e);
            SizeFieldColumn();
        }

        /// <summary>
        ///     Gives the field column the width its longest name needs, up to what the value column
        ///     can spare.
        /// </summary>
        /// <remarks>
        ///     Measured rather than stated, which is the layout rule this project works to, and
        ///     measured here rather than through <c>AutoResizeColumn</c>: this is a virtual list, and
        ///     content-based auto-sizing is not supported in virtual mode - it reports the header's
        ///     width and the names are what overflow.
        /// </remarks>
        private void SizeFieldColumn() {
            int room = ClientSize.Width - ValueFloor;
            if (room <= 0)
                return;

            int widest = TextRenderer.MeasureText(fieldColumn.Text, Font).Width;
            foreach (DetailField field in shown)
                widest = Math.Max(widest, TextRenderer.MeasureText(field.Name, Font).Width);

            int width = Math.Min(widest + NamePadding, room);
            if (fieldColumn.Width != width)
                fieldColumn.Width = width;
        }

        private OLVColumn AddColumn(string heading, Func<object, object?> read) {
            var column = new OLVColumn(heading, null) {
                Groupable = false,
                IsEditable = false,
                //Null is what the grid hands an aspect getter for a row it is recycling, for a
                //cell measured before a model is attached, and while a bind tears the list down.
                AspectGetter = row => row == null ? null : read(row)
            };

            AllColumns.Add(column);
            Columns.Add(column);
            return column;
        }
    }

    /// <summary>Rendering helpers the detail rows share, so "absent" reads the same everywhere.</summary>
    /// <remarks>
    ///     Absent and empty are different things in every one of these formats - a null array means
    ///     the client takes a different branch - so one place decides how each is written rather than
    ///     each listing inventing its own wording.
    /// </remarks>
    public static class DetailText {
        /// <summary>An integer array as a comma-separated list, distinguishing absent from empty.</summary>
        /// <param name="values">The array, which may be null.</param>
        /// <returns>The rendered list.</returns>
        public static string Ids(IReadOnlyList<int>? values) {
            if (values == null)
                return "not stored";
            if (values.Count == 0)
                return "none";

            var parts = new List<string>(values.Count);
            for (int i = 0; i < values.Count; i++)
                parts.Add(values[i].ToString());
            return string.Join(", ", parts);
        }

        /// <summary>A value that uses a negative sentinel to mean "the record did not state it".</summary>
        /// <param name="value">The stored value.</param>
        /// <param name="absent">The sentinel that means absent.</param>
        /// <returns>The rendered value.</returns>
        public static string OrAbsent(int value, int absent = -1) {
            return value == absent ? "not stored" : value.ToString();
        }

        /// <summary>The opcodes a record stored, in the order it stored them.</summary>
        /// <remarks>
        ///     Worth a field on every opcode-stream record. The order is not derivable - several of
        ///     these formats store opcodes out of ascending order and some repeat one - so it is the
        ///     thing an encoder replays, and seeing it is how a user can tell a re-encode apart from
        ///     a rewrite.
        /// </remarks>
        /// <param name="opcodes">The recorded stream.</param>
        /// <returns>The opcode numbers, comma separated.</returns>
        public static string Order(OpcodeStream opcodes) {
            if (opcodes == null)
                return string.Empty;

            var parts = new List<string>(opcodes.Count);
            for (int i = 0; i < opcodes.Count; i++)
                parts.Add(opcodes[i].Opcode.ToString());
            return string.Join(",", parts);
        }
    }
}
