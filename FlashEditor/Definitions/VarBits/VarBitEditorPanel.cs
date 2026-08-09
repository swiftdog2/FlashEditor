using BrightIdeasSoftware;
using FlashEditor.Cache;
using FlashEditor.Definitions.Editing;
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Windows.Forms;
using static FlashEditor.Utils.DebugUtil;

namespace FlashEditor.Definitions.VarBits {
    /// <summary>
    ///     The Varbits tab: index 22 from a list of varbits down to the player variable one is
    ///     carved out of.
    /// </summary>
    /// <remarks>
    ///     Two levels because a varbit only means something in relation to a varplayer. On its own a
    ///     record is three numbers - a varp id and two bit positions - and nothing on the row says
    ///     whether those bits are the whole variable, one flag among thirty, or a range that overlaps
    ///     the varbit next to it. The pane beside the list is that variable: every varbit pointing at
    ///     the selected one's varp, in id order, with the bits each claims.
    ///     <para>
    ///     The reverse index is built by <see cref="VarBitListDescriptor"/> while it decodes, because
    ///     the panel only ever sees the selected row and rebuilding it here would mean sweeping the
    ///     index twice. It therefore describes the cache <i>as loaded</i>: a staged edit that repoints
    ///     a varbit at another varp shows in the list immediately and in this pane after the tab is
    ///     reloaded.
    ///     </para>
    /// </remarks>
    public sealed class VarBitEditorPanel : UserControl {
        /// <summary>
        ///     The descriptor the varbit list is driven by.
        /// </summary>
        /// <remarks>
        ///     One instance, held rather than built per bind, because <c>DefinitionListPanel.Bind</c>
        ///     treats a different descriptor as a different thing to show and would reload the whole
        ///     index on every visit to the tab. It also owns the varp index this panel reads.
        /// </remarks>
        private readonly VarBitListDescriptor descriptor = new VarBitListDescriptor();

        /* Consolas 9 on every child. The form puts Consolas 12 on the tab control and everything
           under it inherits, which is half again what these grids are laid out for. */
        private static readonly Font GridFont = new Font("Consolas", 9F);

        private readonly DefinitionListPanel varbits = new DefinitionListPanel();

        private readonly FastObjectListView siblings = new FastObjectListView {
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
        private readonly SplitContainer listAndVarp = new SplitContainer {
            Dock = DockStyle.Fill,
            Orientation = Orientation.Vertical
        };

        private const string NoCacheText = "No cache loaded";
        private const string NoSelectionText = "Select a varbit to see the varplayer it sits in";

        private RSCache? cache;
        private bool splitterPlaced;

        /// <summary>Creates the panel.</summary>
        public VarBitEditorPanel() {
            Dock = DockStyle.Fill;
            BuildLayout();

            varbits.SelectedRowChanged += (_, _) => ShowVarp(varbits.SelectedRow as VarBitListing);
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
            siblings.ClearObjects();
            header.Text = newCache == null ? NoCacheText : NoSelectionText;
            varbits.Bind(newCache, descriptor);
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
            if (splitterPlaced || listAndVarp.Width < 200)
                return;

            //Set before the assignment, not after: changing a splitter distance lays the panel out
            //again, and this is called from that layout.
            splitterPlaced = true;

            try {
                listAndVarp.SplitterDistance = Math.Max(listAndVarp.Panel1MinSize, listAndVarp.Width * 3 / 5);
            } catch (InvalidOperationException ex) {
                //Left for the next layout rather than clamped. A clamped distance sticks, and the
                //user would see a collapsed pane on a window that later has room for both.
                splitterPlaced = false;
                Debug("Varbit tab splitter not placed yet: " + ex.Message, LOG_DETAIL.ADVANCED);
            }
        }

        private void BuildLayout() {
            AddColumn(siblings, "Varbit", 90, row => Sibling(row).VarBitId);
            AddColumn(siblings, "Bits", 90, row => Sibling(row).BitRange);
            AddColumn(siblings, "Width", 70, row => Sibling(row).Width);
            AddColumn(siblings, "Mask", 110, row => Sibling(row).Mask);
            AddColumn(siblings, "Layout", 300, row => BitDiagram(Sibling(row)));

            listAndVarp.Panel1.Controls.Add(varbits);

            //Docking resolves from the end of the Controls collection backwards, so the header has to
            //be added after the filled grid or the grid claims the whole pane.
            listAndVarp.Panel2.Controls.Add(siblings);
            listAndVarp.Panel2.Controls.Add(header);

            Controls.Add(listAndVarp);
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
            var column = new OLVColumn(heading, null) {
                Width = width,
                Groupable = false,
                IsEditable = false,
                AspectGetter = row => read(row)
            };

            list.AllColumns.Add(column);
            list.Columns.Add(column);
        }

        private static VarBitListing Sibling(object row) {
            return (VarBitListing) row;
        }

        /// <summary>Fills the varp pane from the selected varbit.</summary>
        /// <param name="listing">The selected varbit, or null.</param>
        private void ShowVarp(VarBitListing? listing) {
            siblings.ClearObjects();

            if (cache == null || listing == null) {
                header.Text = cache == null ? NoCacheText : NoSelectionText;
                return;
            }

            if (!listing.IsStored) {
                header.Text = "Varbit " + listing.VarBitId + " - declared slot, one terminator byte, no varplayer";
                return;
            }

            IReadOnlyList<VarBitListing> rows = descriptor.SiblingsOf(listing.VarpId);
            header.Text = Describe(listing, rows);
            siblings.SetObjects(new List<VarBitListing>(rows));
            siblings.SelectedObject = listing;
        }

        /// <summary>The line above the varp pane: which variable, and how much of it is claimed.</summary>
        /// <param name="listing">The selected varbit.</param>
        /// <param name="rows">Every varbit pointing at the same varp.</param>
        /// <returns>The summary line.</returns>
        private static string Describe(VarBitListing listing, IReadOnlyList<VarBitListing> rows) {
            //Counted over the union of the ranges rather than summed, because overlapping varbits are
            //legal and a sum would report more bits claimed than a 32-bit variable has.
            uint claimed = 0;
            foreach (VarBitListing row in rows)
                claimed |= Bits(row);

            int count = 0;
            for (uint bit = claimed; bit != 0; bit >>= 1)
                count += (int) (bit & 1);

            return "Varplayer " + listing.VarpId + " - " + rows.Count + " varbit" + (rows.Count == 1 ? "" : "s") +
                   " claiming " + count + " of 32 bits";
        }

        /// <summary>The bits one varbit claims, as a mask over the whole variable.</summary>
        /// <remarks>
        ///     Guarded rather than shifted blindly: the client's mask table has 32 entries, so a
        ///     range wider than that is a record it would throw on, and a shift by 32 or more is
        ///     undefined in C# as well.
        /// </remarks>
        /// <param name="row">The varbit.</param>
        /// <returns>The mask, or zero when the range is one the client could not load.</returns>
        private static uint Bits(VarBitListing row) {
            if (!row.IsStored || !row.Record.FitsTheClientMaskTable || row.FromBit > 31)
                return 0;

            return (uint) row.Record.Mask << row.FromBit;
        }

        /// <summary>
        ///     One varbit's range drawn across the variable, most significant bit first.
        /// </summary>
        /// <remarks>
        ///     A picture rather than two numbers, because that is the whole reason this pane exists:
        ///     reading four rows of "bits 4..7" and working out by hand whether they abut or overlap
        ///     is the part a list cannot do.
        /// </remarks>
        /// <param name="row">The varbit.</param>
        /// <returns>32 characters, one per bit.</returns>
        private static string BitDiagram(VarBitListing row) {
            uint mask = Bits(row);
            if (mask == 0)
                return string.Empty;

            var drawn = new char[32];
            for (int bit = 31; bit >= 0; bit--)
                drawn[31 - bit] = (mask & (1u << bit)) != 0 ? '#' : '.';
            return new string(drawn);
        }
    }
}
