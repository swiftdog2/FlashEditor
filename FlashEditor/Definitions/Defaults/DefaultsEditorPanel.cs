using BrightIdeasSoftware;
using FlashEditor.Cache;
using FlashEditor.Definitions.Editing;
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Windows.Forms;
using static FlashEditor.Utils.DebugUtil;

namespace FlashEditor.Definitions.Defaults {
    /// <summary>
    ///     The Defaults tab: index 28, one group at a time.
    /// </summary>
    /// <remarks>
    ///     A group selector rather than one list, because index 28 is not a record table. It holds
    ///     two unrelated config blobs that share nothing but an index: group 1 is the default
    ///     environment cube map plus the player-title enum tables, group 3 is the hitsplat slot
    ///     layout plus the renderer's benchmark model. They have no opcode in common, so a single
    ///     grid would need a union of headings that is wrong for both rows in it. This is the same
    ///     shape the Config tab uses on index 2 and for the same reason.
    ///     <para>
    ///     <b>The index has two groups, not four.</b> They are enumerated from the reference table,
    ///     which declares ids 1 and 3; <c>idx28</c> has four slots and the two spare ones are dead
    ///     records. Anything driven off the idx file's length would offer two groups that cannot be
    ///     read.
    ///     </para>
    ///     <para>
    ///     <b>Read only.</b> Both records round-trip, but neither is safely editable in a grid: group
    ///     1's fields are arrays whose length the client reads structurally, and group 3 stores a
    ///     slot count that allocates the offset array written after it, so the two cannot be changed
    ///     independently. The detail pane below the list shows every value the record carries.
    ///     </para>
    /// </remarks>
    public sealed class DefaultsEditorPanel : UserControl {
        /* Consolas 9 on every child. The form puts Consolas 12 on the tab control and everything
           under it inherits, which is half again what these grids are laid out for. */
        private static readonly Font GridFont = new Font("Consolas", 9F);

        private readonly ComboBox groups = new ComboBox {
            DropDownStyle = ComboBoxStyle.DropDownList,
            Font = GridFont
        };

        private readonly Label groupLabel = new Label {
            AutoSize = true,
            Font = GridFont,
            Text = "Group",
            TextAlign = ContentAlignment.MiddleLeft
        };

        //FlowLayoutPanel rather than absolute positions, so the caption and the box stay together at
        //whatever font ratio the form scales to.
        private readonly FlowLayoutPanel selector = new FlowLayoutPanel {
            AutoSize = true,
            Dock = DockStyle.Top,
            FlowDirection = FlowDirection.LeftToRight,
            WrapContents = false
        };

        //AutoSize rather than stated heights, so the lines these need are the lines they get whatever
        //font the form ends up scaling to.
        private readonly Label header = new Label {
            AutoSize = true,
            Dock = DockStyle.Top,
            Font = GridFont,
            Text = NoCacheText
        };

        private readonly DefinitionListPanel records = new DefinitionListPanel {
            //This pane is bound with a null cache while nothing is selected, so the panel's own
            //default would claim no cache is loaded while a cache is open behind it.
            EmptyMessage = NoSelectionText
        };

        private readonly FastObjectListView fields = new FastObjectListView {
            Dock = DockStyle.Fill,
            Font = GridFont,
            FullRowSelect = true,
            GridLines = true,
            ShowGroups = false,
            UseFiltering = true,
            View = View.Details
        };

        /* No splitter states a minimum size. Setting one re-checks the current distance against it,
           and a container is still at its 150x100 default when a field initialiser runs, so a
           minimum wide enough to be useful throws before the panel has ever been shown. */
        private readonly SplitContainer listAndFields = new SplitContainer {
            Dock = DockStyle.Fill,
            Orientation = Orientation.Horizontal
        };

        private const string NoCacheText = "No cache loaded";
        private const string NoSelectionText = "Select a group to see what it holds";

        private RSCache? cache;
        private bool splitterPlaced;

        /// <summary>Creates the panel.</summary>
        public DefaultsEditorPanel() {
            Dock = DockStyle.Fill;

            //Derived from the font rather than written as a pixel count: the form is
            //AutoScaleMode.Font, so a literal width is multiplied at runtime and clips the caption.
            groups.Width = groups.Font.Height * 34;

            BuildLayout();

            groups.SelectedIndexChanged += (_, _) => ShowGroup(groups.SelectedItem as GroupOption);
            records.SelectedRowChanged += (_, _) => ShowRecord(records.SelectedRow as IDefaultsListing);
        }

        /// <summary>
        ///     Points the panel at a cache, or clears it when given none.
        /// </summary>
        /// <remarks>
        ///     <c>Editor.LoadEditorTab</c> calls this on every visit to the tab, so a rebind of the
        ///     cache already on display has to be a no-op or the selected group is thrown away each
        ///     time. Identity is the right test because opening a cache builds a new
        ///     <see cref="RSCache"/>.
        /// </remarks>
        /// <param name="newCache">The open cache, or null to unbind.</param>
        public void Bind(RSCache? newCache) {
            if (ReferenceEquals(newCache, cache))
                return;

            cache = newCache;

            fields.ClearObjects();
            groups.Items.Clear();
            records.Bind(null, new SceneDefaultsListDescriptor());
            header.Text = newCache == null ? NoCacheText : NoSelectionText;

            if (newCache == null)
                return;

            try {
                var unmodelled = new List<int>();

                foreach (int group in newCache.EnumerateGroups(RSConstants.DEFAULTS)) {
                    int files = newCache.GetFileIds(RSConstants.DEFAULTS, group).Length;

                    if (group == SceneDefaultsDefinition.GroupId)
                        groups.Items.Add(new GroupOption(group, "Scene defaults", files));
                    else if (group == HitsplatLayoutDefinition.GroupId)
                        groups.Items.Add(new GroupOption(group, "Hitsplat layout", files));
                    else
                        unmodelled.Add(group);
                }

                if (groups.Items.Count > 0)
                    groups.SelectedIndex = 0;

                //Reported rather than silently dropped, and after the selection because selecting a
                //group rewrites the header. The 637 client reads groups 1 and 3 by literal and
                //nothing else, so a third group is either a repack addition the client ignores or
                //evidence that this index changed - and either is worth saying out loud.
                if (unmodelled.Count > 0)
                    header.Text += "   (index 28 also declares group(s) " + string.Join(", ", unmodelled) +
                                   ", which no codec here reads)";
            } catch (Exception ex) {
                //Reported rather than thrown: this runs from the tab loader, and an exception out of
                //it takes the form down on a cache that is merely missing a reference table.
                header.Text = "Index 28's reference table could not be read: " + ex.Message;
                Debug("Defaults tab could not list index 28: " + ex);
            }
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
            if (splitterPlaced || listAndFields.Height < 200)
                return;

            //Set before the assignment, not after: changing a splitter distance lays the panel out
            //again, and this is called from that layout.
            splitterPlaced = true;

            try {
                listAndFields.SplitterDistance = Math.Max(listAndFields.Panel1MinSize, listAndFields.Height / 3);
            } catch (InvalidOperationException ex) {
                //Left for the next layout rather than clamped. A clamped distance sticks, and the
                //user would see a collapsed pane on a window that later has room for both.
                splitterPlaced = false;
                Debug("Defaults tab splitter not placed yet: " + ex.Message, LOG_DETAIL.ADVANCED);
            }
        }

        private void BuildLayout() {
            AddColumn(fields, "Field", 300, row => Field(row).Name);
            AddColumn(fields, "Value", 700, row => Field(row).Value);

            selector.Controls.Add(groupLabel);
            selector.Controls.Add(groups);

            listAndFields.Panel1.Controls.Add(records);
            listAndFields.Panel2.Controls.Add(fields);

            //Docking resolves from the end of the Controls collection backwards, so the strips have
            //to be added after the filled splitter, and in bottom-to-top order among themselves.
            Controls.Add(listAndFields);
            Controls.Add(header);
            Controls.Add(selector);

            //Bound before any cache arrives so the record grid has headings from the start.
            records.Bind(null, new SceneDefaultsListDescriptor());
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

        private static DefaultsField Field(object row) {
            return (DefaultsField) row;
        }

        /// <summary>
        ///     Loads the selected group's record.
        /// </summary>
        /// <remarks>
        ///     A fresh descriptor per selection, because <c>DefinitionListPanel.Bind</c> treats the
        ///     same descriptor instance as the same thing to show and would keep the previous group's
        ///     row on screen.
        /// </remarks>
        /// <param name="option">The selected group, or null.</param>
        private void ShowGroup(GroupOption? option) {
            fields.ClearObjects();

            if (cache == null || option == null) {
                header.Text = cache == null ? NoCacheText : NoSelectionText;
                records.Bind(null, new SceneDefaultsListDescriptor());
                return;
            }

            header.Text = "Group " + option.GroupId + " - " + option.Name + " - " +
                          option.FileCount + " file" + (option.FileCount == 1 ? "" : "s");

            records.Bind(cache, option.GroupId == HitsplatLayoutDefinition.GroupId
                ? new HitsplatLayoutListDescriptor()
                : (IDefinitionListDescriptor) new SceneDefaultsListDescriptor());
        }

        /// <summary>Fills the field grid from the selected record.</summary>
        /// <remarks>
        ///     No cache read at all: the row already carries the whole decoded record, because a
        ///     group here is one file and the descriptor decoded it to build the row.
        /// </remarks>
        /// <param name="listing">The selected record, or null.</param>
        private void ShowRecord(IDefaultsListing? listing) {
            fields.ClearObjects();

            if (listing == null)
                return;

            header.Text = listing.Summary;
            fields.SetObjects(new List<DefaultsField>(listing.Fields));
        }

        /// <summary>One group of index 28 as an entry in the selector.</summary>
        private sealed class GroupOption {
            internal GroupOption(int groupId, string name, int fileCount) {
                GroupId = groupId;
                Name = name;
                FileCount = fileCount;
            }

            /// <summary>The group id, which the client reads by literal.</summary>
            internal int GroupId { get; }

            /// <summary>What the group holds, settled from what the client does with it.</summary>
            internal string Name { get; }

            /// <summary>How many files the reference table declares for it.</summary>
            internal int FileCount { get; }

            /// <summary>The entry as the combo box shows it.</summary>
            /// <returns>The group id and what it holds.</returns>
            public override string ToString() {
                return GroupId.ToString().PadLeft(2) + "  " + Name;
            }
        }
    }
}
