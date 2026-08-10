using BrightIdeasSoftware;
using FlashEditor.Cache;
using FlashEditor.Definitions.Editing;
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Windows.Forms;
using static FlashEditor.Utils.DebugUtil;

namespace FlashEditor.Definitions.Enums {
    /// <summary>
    ///     The Enums tab: index 17 from a list of enums down to the key/value table one holds.
    /// </summary>
    /// <remarks>
    ///     Two levels because an enum <i>is</i> a table. The row says which enum, how it is typed and
    ///     what it answers for a key it does not hold; the pane beside it is the table itself, which
    ///     is the whole content of the record and cannot be a cell. Flattening the two would produce
    ///     one row per pair with no way to tell where an enum ends - and the largest enum in this
    ///     cache holds most of a thousand of them.
    ///     <para>
    ///     The list is a <see cref="DefinitionListPanel"/> so the load, the progress reporting and
    ///     the write-back are not restated here. The entry grid is this control's own, because it
    ///     enumerates nothing: the row already carries the decoded record, so selecting an enum
    ///     costs no cache read at all.
    ///     </para>
    ///     <para>
    ///     <b>The entry grid is read only.</b> The list commits an edit by re-encoding one file, and
    ///     the four scalar fields it offers are single values with a single meaning. A pair is not:
    ///     the format allows repeated and unsorted keys, so a grid that let one be retyped would also
    ///     have to say what happens to a duplicate, and quietly folding the table into a dictionary
    ///     is exactly the normalisation that breaks byte identity on an index nobody edited.
    ///     </para>
    /// </remarks>
    public sealed class EnumEditorPanel : UserControl {
        /// <summary>
        ///     The descriptor the enum list is driven by.
        /// </summary>
        /// <remarks>
        ///     One instance, held rather than built per bind, because <c>DefinitionListPanel.Bind</c>
        ///     treats a different descriptor as a different thing to show and would reload the whole
        ///     index on every visit to the tab.
        /// </remarks>
        private static readonly IDefinitionListDescriptor Descriptor = new EnumListDescriptor();

        /* Consolas 9 on every child. The form puts Consolas 12 on the tab control and everything
           under it inherits, which is half again what these grids are laid out for. */
        private static readonly Font GridFont = new Font("Consolas", 9F);

        private readonly DefinitionListPanel enums = new DefinitionListPanel();

        private readonly FastObjectListView entries = new FastObjectListView {
            Dock = DockStyle.Fill,
            Font = GridFont,
            FullRowSelect = true,
            GridLines = true,
            ShowGroups = false,
            UseFiltering = true,
            View = View.Details
        };

        //AutoSize rather than a stated height, so the line the summary needs is the line it gets
        //whatever font the form ends up scaling to.
        private readonly Label header = new Label {
            AutoSize = true,
            Dock = DockStyle.Top,
            Font = GridFont,
            Text = NoCacheText
        };

        /* No splitter states a minimum size. Setting one re-checks the current distance against it,
           and a container is still at its 150x100 default when a field initialiser runs, so a
           minimum wide enough to be useful throws before the panel has ever been shown. */
        private readonly SplitContainer listAndTable = new SplitContainer {
            Dock = DockStyle.Fill,
            Orientation = Orientation.Vertical
        };

        private const string NoCacheText = "No cache loaded";
        private const string NoSelectionText = "Select an enum to see its table";

        private RSCache? cache;
        private bool splitterPlaced;

        /// <summary>Creates the panel.</summary>
        public EnumEditorPanel() {
            Dock = DockStyle.Fill;
            BuildLayout();

            enums.SelectedRowChanged += (_, _) => ShowEnum(enums.SelectedRow as EnumListing);
        }

        /// <summary>
        ///     Points the panel at a cache, or clears it when given none.
        /// </summary>
        /// <remarks>
        ///     <c>Editor.LoadEditorTab</c> calls this on every visit to the tab, so a rebind of the
        ///     cache already on display has to be a no-op or the selection is thrown away each time.
        ///     Identity is the right test because opening a cache builds a new <see cref="RSCache"/>.
        /// </remarks>
        /// <param name="newCache">The open cache, or null to unbind.</param>
        public void Bind(RSCache? newCache) {
            if (ReferenceEquals(newCache, cache))
                return;

            cache = newCache;
            entries.ClearObjects();
            header.Text = newCache == null ? NoCacheText : NoSelectionText;
            enums.Bind(newCache, Descriptor);
        }

        /// <summary>Places the splitter once the layout pass has given the container a real size.</summary>
        /// <param name="levent">The event data.</param>
        protected override void OnLayout(LayoutEventArgs levent) {
            base.OnLayout(levent);
            PlaceSplitter();
        }

        /// <summary>
        ///     Divides the panel proportionally, once, when it first has a size worth dividing.
        /// </summary>
        /// <remarks>
        ///     A <see cref="SplitContainer"/> defaults to a splitter distance of 50 <i>pixels</i>, not
        ///     half, so the distance has to be stated - and stating it in the designer would make it
        ///     one more literal the form multiplies by its font ratio.
        /// </remarks>
        private void PlaceSplitter() {
            if (splitterPlaced || listAndTable.Width < 200)
                return;

            //Set before the assignment, not after: changing a splitter distance lays the panel out
            //again, and this is called from that layout.
            splitterPlaced = true;

            try {
                listAndTable.SplitterDistance = Math.Max(listAndTable.Panel1MinSize, listAndTable.Width * 3 / 5);
            } catch (InvalidOperationException ex) {
                //Left for the next layout rather than clamped. A clamped distance sticks, and the
                //user would see a collapsed pane on a window that later has room for both.
                splitterPlaced = false;
                Debug("Enum tab splitter not placed yet: " + ex.Message, LOG_DETAIL.ADVANCED);
            }
        }

        private void BuildLayout() {
            AddColumn(entries, "#", 60, row => Entry(row).Position);
            AddColumn(entries, "Key", 120, row => Entry(row).Key);
            AddColumn(entries, "Value", 520, row => Entry(row).Value);

            listAndTable.Panel1.Controls.Add(enums);

            //Docking resolves from the end of the Controls collection backwards, so the header has to
            //be added after the filled grid or the grid claims the whole pane.
            listAndTable.Panel2.Controls.Add(entries);
            listAndTable.Panel2.Controls.Add(header);

            Controls.Add(listAndTable);
        }

        /// <summary>
        ///     Adds one column, reading its value through a delegate rather than an aspect name.
        /// </summary>
        /// <remarks>
        ///     Same reasoning as <see cref="DefinitionColumn"/>: a name looked up by reflection blanks
        ///     the column when the property is renamed, where a delegate stops compiling.
        /// </remarks>
        /// <param name="list">The grid to add to.</param>
        /// <param name="heading">The column heading.</param>
        /// <param name="width">The column width, in the grid's own pinned font.</param>
        /// <param name="read">Reads the displayed value off a row.</param>
        private static void AddColumn(FastObjectListView list, string heading, int width, Func<object, object?> read) {
            //Delegated so the null-row guard has one implementation. Ten copies of this method
            //existed and not one of them had it, which is how closing a cache crashed the
            //interfaces list.
            DetailGrid.AddColumn(list, heading, width, read);
        }

        private static EntryListing Entry(object row) {
            return (EntryListing) row;
        }

        /// <summary>Fills the entry grid from the selected enum.</summary>
        /// <param name="listing">The selected enum, or null.</param>
        private void ShowEnum(EnumListing? listing) {
            entries.ClearObjects();

            if (cache == null || listing == null) {
                header.Text = cache == null ? NoCacheText : NoSelectionText;
                return;
            }

            header.Text = Describe(listing);

            var rows = new List<EntryListing>(listing.Record.Entries.Count);
            for (int position = 0; position < listing.Record.Entries.Count; position++)
                rows.Add(new EntryListing(position, listing.Record.Entries[position], listing.Record.ValuesAreStrings));
            entries.SetObjects(rows);
        }

        /// <summary>The line above the table: what the selected enum is.</summary>
        /// <param name="listing">The selected enum.</param>
        /// <returns>The summary line.</returns>
        private static string Describe(EnumListing listing) {
            if (listing.IsEmpty)
                return "Enum " + listing.EnumId + " - unallocated slot, one terminator byte";

            string types = listing.KeyType.Length > 0 || listing.ValueType.Length > 0
                ? " - key " + listing.KeyType + ", value " + listing.ValueType
                : string.Empty;

            string fallback = listing.Record.ValuesAreStrings
                ? " - missing key answers \"" + listing.DefaultString + "\""
                : " - missing key answers " + listing.DefaultInt;

            return "Enum " + listing.EnumId + " (group " + listing.GroupId + ", file " + listing.FileId + ")" +
                   types + " - " + listing.EntryCount.ToString("N0") + " entries" + fallback;
        }

        /// <summary>
        ///     One key/value pair of the selected enum, as a grid row.
        /// </summary>
        /// <remarks>
        ///     The stored position is a column because the format neither sorts nor de-duplicates
        ///     keys. Two pairs with the same key are different rows of the file, and without the
        ///     position they would be indistinguishable on screen.
        /// </remarks>
        private sealed class EntryListing {
            private readonly EnumEntry entry;
            private readonly bool valuesAreStrings;

            internal EntryListing(int position, EnumEntry entry, bool valuesAreStrings) {
                Position = position;
                this.entry = entry;
                this.valuesAreStrings = valuesAreStrings;
            }

            /// <summary>Where the pair sits in the stored table.</summary>
            internal int Position { get; }

            /// <summary>The key, always an int32 on the wire whatever the key type char claims.</summary>
            internal int Key => entry.Key;

            /// <summary>The value, rendered as whichever shape the table's opcode declared.</summary>
            internal string Value => valuesAreStrings ? entry.Text : entry.Number.ToString();
        }
    }
}
