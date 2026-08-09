using BrightIdeasSoftware;
using FlashEditor.Cache;
using FlashEditor.Definitions.Editing;
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Windows.Forms;
using static FlashEditor.Utils.DebugUtil;
using FlashEditor.IO;

namespace FlashEditor.Definitions.Config {
    /// <summary>
    ///     The Config tab: index 2, one family at a time.
    /// </summary>
    /// <remarks>
    ///     One tab with a group selector rather than a tab per family, because a group in index 2
    ///     <b>is</b> a record type - thirty-five unrelated families share the index and nothing
    ///     arithmetic relates a definition id to a group. A single flat list of all 16,981 files would
    ///     put varplayers and cursors in one grid under one set of headings.
    ///     <para>
    ///     <b>Every group the reference table declares is offered</b>, not only the ten this editor
    ///     models. A group with no codec falls to <see cref="ConfigFamily.Unmodelled"/>, which reads no
    ///     opcodes and classifies each record from its own bytes, so the grid shows the id space and
    ///     the record lengths instead of blank rows. That matters more here than on any other index:
    ///     8,694 of index 2's 16,981 files are a single <c>0x00</c> terminator, so for about half the
    ///     index "empty" is the whole truth and it is worth saying out loud.
    ///     </para>
    ///     <para>
    ///     <b>The empty count on the header line is measured, not asserted.</b> Selecting a group reads
    ///     it once and counts the records that terminate immediately. A hardcoded list of empty groups
    ///     would be a claim about one cache that the tab could not check; a count taken off the bytes
    ///     in front of it is true of whatever cache is open.
    ///     </para>
    /// </remarks>
    public sealed class ConfigEditorPanel : UserControl {
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

        private readonly Label notes = new Label {
            AutoSize = true,
            Dock = DockStyle.Top,
            Font = GridFont,
            Text = string.Empty
        };

        private readonly DefinitionListPanel records = new DefinitionListPanel();
        private readonly FastObjectListView fields = Grid();
        private readonly FastObjectListView opcodes = Grid();

        /* No splitter states a minimum size. Setting one re-checks the current distance against it,
           and a container is still at its 150x100 default when a field initialiser runs, so a
           minimum wide enough to be useful throws before the panel has ever been shown. */
        private readonly SplitContainer listAndDetail = new SplitContainer {
            Dock = DockStyle.Fill,
            Orientation = Orientation.Vertical
        };

        private readonly SplitContainer fieldsAndOpcodes = new SplitContainer {
            Dock = DockStyle.Fill,
            Orientation = Orientation.Horizontal
        };

        private const string NoCacheText = "No cache loaded";
        private const string NoSelectionText = "Select a config group to see its records";

        private RSCache? cache;
        private bool splittersPlaced;

        /// <summary>Creates the panel.</summary>
        public ConfigEditorPanel() {
            Dock = DockStyle.Fill;

            //Derived from the font rather than written as a pixel count, for the reason
            //DefinitionListPanel sizes its progress bar that way: the form is AutoScaleMode.Font, so
            //a literal width is multiplied at runtime and clips the longest family name.
            groups.Width = groups.Font.Height * 32;

            BuildLayout();

            groups.SelectedIndexChanged += (_, _) => ShowGroup(groups.SelectedItem as GroupOption);
            records.SelectedRowChanged += (_, _) => ShowRecord(records.SelectedRow as ConfigListing);
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
            opcodes.ClearObjects();
            groups.Items.Clear();
            records.Bind(null, EmptyDescriptor());
            header.Text = newCache == null ? NoCacheText : NoSelectionText;
            notes.Text = string.Empty;

            if (newCache == null)
                return;

            try {
                foreach (int group in newCache.EnumerateGroups(RSConstants.CONFIG))
                    groups.Items.Add(new GroupOption(ConfigFamily.For(group),
                        newCache.GetFileIds(RSConstants.CONFIG, group).Length));

                //Selected rather than left blank, so the tab shows a family on arrival rather than an
                //empty grid. Whichever group the table declares first is what gets picked - nothing
                //here assumes which that is.
                if (groups.Items.Count > 0)
                    groups.SelectedIndex = 0;
            } catch (Exception ex) {
                //Reported rather than thrown: this runs from the tab loader, and an exception out of
                //it takes the form down on a cache that is merely missing a reference table.
                header.Text = "Index 2's reference table could not be read: " + ex.Message;
                Debug("Config tab could not list index 2: " + ex);
            }
        }

        /// <summary>Places the splitters once the layout pass has given the containers a real size.</summary>
        /// <param name="levent">The event data.</param>
        protected override void OnLayout(LayoutEventArgs levent) {
            base.OnLayout(levent);
            PlaceSplitters();
        }

        /// <summary>
        ///     Divides the panel proportionally, once, when it first has a size worth dividing.
        /// </summary>
        /// <remarks>
        ///     A <see cref="SplitContainer"/> defaults to a splitter distance of 50 <i>pixels</i>, not
        ///     half, so the distance has to be stated - and stating it in the designer would make it
        ///     one more literal the form multiplies by its font ratio. A fraction of the measured size
        ///     is the same division at any font or DPI.
        ///     <para>
        ///     Deferred to layout rather than the constructor because assigning a distance the control
        ///     is not yet large enough for throws, and a field initialiser runs while the container is
        ///     still 150x100. Once only, so a user who drags a splitter keeps where they put it.
        ///     </para>
        /// </remarks>
        private void PlaceSplitters() {
            if (splittersPlaced || listAndDetail.Width < 200 || fieldsAndOpcodes.Height < 160)
                return;

            //Set before the assignments, not after: changing a splitter distance lays the panel out
            //again, and this is called from that layout.
            splittersPlaced = true;

            try {
                listAndDetail.SplitterDistance = Math.Max(listAndDetail.Panel1MinSize, listAndDetail.Width * 3 / 5);
                fieldsAndOpcodes.SplitterDistance =
                    Math.Max(fieldsAndOpcodes.Panel1MinSize, fieldsAndOpcodes.Height / 2);
            } catch (InvalidOperationException ex) {
                //Left for the next layout rather than clamped. A clamped distance sticks, and the
                //user would see a collapsed pane on a window that later has room for all three.
                splittersPlaced = false;
                Debug("Config tab splitters not placed yet: " + ex.Message, LOG_DETAIL.ADVANCED);
            }
        }

        private void BuildLayout() {
            BuildFieldColumns();
            BuildOpcodeColumns();

            selector.Controls.Add(groupLabel);
            selector.Controls.Add(groups);

            fieldsAndOpcodes.Panel1.Controls.Add(fields);
            fieldsAndOpcodes.Panel2.Controls.Add(opcodes);

            listAndDetail.Panel1.Controls.Add(records);
            listAndDetail.Panel2.Controls.Add(fieldsAndOpcodes);

            //Docking resolves from the end of the Controls collection backwards, so the strips have
            //to be added after the filled splitter, and in bottom-to-top order among themselves.
            Controls.Add(listAndDetail);
            Controls.Add(notes);
            Controls.Add(header);
            Controls.Add(selector);

            //Bound before any cache arrives so the record grid has headings from the start.
            records.Bind(null, EmptyDescriptor());
        }

        private void BuildFieldColumns() {
            AddColumn(fields, "Field", 200, row => Field(row).Name);
            AddColumn(fields, "Value", 560, row => Field(row).Value);
        }

        private void BuildOpcodeColumns() {
            AddColumn(opcodes, "#", 50, row => Opcode(row).Position);
            AddColumn(opcodes, "Opcode", 80, row => Opcode(row).Opcode);
            AddColumn(opcodes, "Payload", 620, row => Opcode(row).Detail);
        }

        /// <summary>One grid, laid out the same way as every other.</summary>
        /// <returns>The grid.</returns>
        private static FastObjectListView Grid() {
            return new FastObjectListView {
                Dock = DockStyle.Fill,
                Font = GridFont,
                FullRowSelect = true,
                GridLines = true,
                ShowGroups = false,
                UseFiltering = true,
                View = View.Details
            };
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

        /// <summary>
        ///     A descriptor that carries the record columns while no group is selected.
        /// </summary>
        /// <remarks>
        ///     Bound with a null cache rather than left unbound, because <c>DefinitionListPanel.Bind</c>
        ///     tears its columns down when handed no descriptor - and an empty grid with headings reads
        ///     as "nothing selected" where a headingless one reads as broken. A fresh instance each
        ///     time is deliberate: the panel treats the same instance as the same thing to show.
        /// </remarks>
        /// <returns>The descriptor.</returns>
        private static IDefinitionListDescriptor EmptyDescriptor() {
            return new ConfigListDescriptor(ConfigFamily.Unmodelled(0));
        }

        private static FieldListing Field(object row) {
            return (FieldListing) row;
        }

        private static OpcodeListing Opcode(object row) {
            return (OpcodeListing) row;
        }

        /// <summary>Loads the selected group's records and describes what the group holds.</summary>
        /// <param name="option">The selected group, or null.</param>
        private void ShowGroup(GroupOption? option) {
            fields.ClearObjects();
            opcodes.ClearObjects();

            if (cache == null || option == null) {
                header.Text = cache == null ? NoCacheText : NoSelectionText;
                notes.Text = string.Empty;
                records.Bind(null, EmptyDescriptor());
                return;
            }

            header.Text = Describe(cache, option);
            notes.Text = option.Family.Notes;
            records.Bind(cache, new ConfigListDescriptor(option.Family));
        }

        /// <summary>Fills the field and opcode grids from the selected record.</summary>
        /// <remarks>
        ///     No cache read at all: the list row already carries the whole decoded record, because one
        ///     config record is one file and the descriptor decoded it to build the row.
        /// </remarks>
        /// <param name="listing">The selected record, or null.</param>
        private void ShowRecord(ConfigListing? listing) {
            fields.ClearObjects();
            opcodes.ClearObjects();

            if (listing == null)
                return;

            var fieldRows = new List<FieldListing>(listing.Record.Fields.Count);
            foreach (ConfigField field in listing.Record.Fields)
                fieldRows.Add(new FieldListing(field));
            fields.SetObjects(fieldRows);

            var rows = new List<OpcodeListing>(listing.Record.Opcodes.Count);
            for (int position = 0; position < listing.Record.Opcodes.Count; position++)
                rows.Add(new OpcodeListing(position, listing.Record.Opcodes[position]));
            opcodes.SetObjects(rows);
        }

        /// <summary>
        ///     The header line: what the group is, how many records it holds, and how many of them are
        ///     empty.
        /// </summary>
        /// <remarks>
        ///     The empty count is taken off the bytes rather than from a table of known-empty groups.
        ///     One container inflate per selection buys a statement that stays true when the editor is
        ///     pointed at a different cache.
        /// </remarks>
        /// <param name="open">The open cache.</param>
        /// <param name="option">The selected group.</param>
        /// <returns>The header line.</returns>
        private static string Describe(RSCache open, GroupOption option) {
            string prefix = "Group " + option.Family.GroupId + " - " + option.Family.Name + " - " +
                            option.FileCount.ToString("N0") + " records";

            try {
                int empty = 0;
                foreach (JagStream file in open.ReadGroup(RSConstants.CONFIG, option.Family.GroupId).Values)
                    if (IsEmptyRecord(file))
                        empty++;

                return empty == 0
                    ? prefix + ", none empty"
                    : prefix + ", " + empty.ToString("N0") + " of them empty";
            } catch (Exception ex) {
                //A group that will not open costs the count, not the tab. The record list below
                //reports the same failure in its own status line.
                Debug("Config group " + option.Family.GroupId + " could not be counted: " + ex.Message);
                return prefix;
            }
        }

        /// <summary>
        ///     Whether a record terminates immediately - a single <c>0x00</c> and nothing else.
        /// </summary>
        /// <remarks>
        ///     Leaves the stream where it found it. The record list reads the same group again through
        ///     its own <c>ReadGroup</c> call, which hands back fresh streams, but restoring the
        ///     position keeps this from being a trap for anything that later shares one.
        /// </remarks>
        /// <param name="file">The stored record.</param>
        /// <returns>Whether it is empty.</returns>
        private static bool IsEmptyRecord(JagStream file) {
            if (file.Length - file.Position != 1)
                return false;

            int start = file.Position;
            int first = file.ReadUnsignedByte();
            file.Position = start;
            return first == 0;
        }

        /// <summary>One group of index 2 as an entry in the selector.</summary>
        private sealed class GroupOption {
            internal GroupOption(ConfigFamily family, int fileCount) {
                Family = family;
                FileCount = fileCount;
            }

            /// <summary>The family the group holds, modelled or not.</summary>
            internal ConfigFamily Family { get; }

            /// <summary>How many records the reference table declares for it.</summary>
            internal int FileCount { get; }

            /// <summary>The entry as the combo box shows it.</summary>
            /// <returns>The group id, the family name and the record count.</returns>
            public override string ToString() {
                return Family.GroupId.ToString().PadLeft(2) + "  " + Family.Name +
                       "  (" + FileCount.ToString("N0") + ")";
            }
        }

        /// <summary>
        ///     One decoded field of the selected record, as a grid row.
        /// </summary>
        /// <remarks>
        ///     A class wrapping the struct rather than the struct itself.
        ///     <see cref="FastObjectListView"/> keys its model-to-row map on the object it is handed,
        ///     and two boxed <see cref="ConfigField"/> values with the same name and value compare
        ///     equal - so a record with a repeated pair would lose a row to the map rather than show
        ///     both.
        /// </remarks>
        private sealed class FieldListing {
            private readonly ConfigField field;

            internal FieldListing(ConfigField field) {
                this.field = field;
            }

            /// <summary>The field's name.</summary>
            internal string Name => field.Name;

            /// <summary>The field's value, rendered.</summary>
            internal string Value => field.Value;
        }

        /// <summary>One opcode occurrence of the selected record, as a grid row.</summary>
        /// <remarks>
        ///     The stream position is a column because it is load bearing on this index: the stored
        ///     order is what the encoder replays, and a record can carry the same opcode twice with
        ///     different payloads. Without it, two occurrences of one opcode are indistinguishable
        ///     rows.
        /// </remarks>
        private sealed class OpcodeListing {
            private readonly ConfigOpcodeRow row;

            internal OpcodeListing(int position, ConfigOpcodeRow row) {
                Position = position;
                this.row = row;
            }

            /// <summary>Where the occurrence sits in the stored opcode stream.</summary>
            internal int Position { get; }

            /// <summary>The opcode byte.</summary>
            internal int Opcode => row.Opcode;

            /// <summary>The stored payload in hex, or the decoded value for the three older codecs.</summary>
            internal string Detail => row.Detail;
        }
    }
}
