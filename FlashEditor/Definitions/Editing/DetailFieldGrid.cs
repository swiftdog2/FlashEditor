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
        /// <summary>Creates an empty grid with its two columns already built.</summary>
        public DetailFieldGrid() {
            Dock = DockStyle.Fill;
            Font = new Font("Consolas", 9F);
            FullRowSelect = true;
            GridLines = true;
            ShowGroups = false;
            UseFiltering = true;
            View = View.Details;

            //Delegates rather than aspect names: a name looked up by reflection blanks the column
            //when the property is renamed, where a delegate stops compiling.
            AddColumn("Field", 320, row => ((DetailField) row).Name);
            AddColumn("Value", 760, row => ((DetailField) row).Value);
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
                ClearObjects();
                return;
            }

            SetObjects(new List<DetailField>(row.Fields));
        }

        private void AddColumn(string heading, int width, Func<object, object?> read) {
            var column = new OLVColumn(heading, null) {
                Width = width,
                Groupable = false,
                IsEditable = false,
                //Null is what the grid hands an aspect getter for a row it is recycling, for a
                //cell measured before a model is attached, and while a bind tears the list down.
                AspectGetter = row => row == null ? null : read(row)
            };

            AllColumns.Add(column);
            Columns.Add(column);
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
