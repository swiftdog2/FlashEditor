using System;
using System.Collections.Generic;
using System.Drawing;
using System.Windows.Forms;
using BrightIdeasSoftware;
using FlashEditor.Cache;
using FlashEditor.Definitions.Editing;
using FlashEditor.UI;

namespace FlashEditor.Definitions.Interfaces {
    /// <summary>
    ///     The behaviour a component carries: twenty hook slots, what writes each, and the script
    ///     each one calls.
    /// </summary>
    /// <remarks>
    ///     <b>A hook is the only behaviour an interface file holds.</b> Everything a component does
    ///     when it is clicked, hovered, dragged or opened is a CS2 script fired from one of these
    ///     slots, and the tab used to show them as rows buried among sixty field rows - which put
    ///     the one part of the record that is a program in the same list as its line height.
    ///     <para>
    ///     <b>Every slot, not only the stored ones.</b> The mapping from slot to client field to CS2
    ///     setter is a property of the format rather than of the component on screen, so a list that
    ///     shrank to the four slots a button uses would never show that slot 0 has no setter at all.
    ///     The toolbar hides the empty ones for anyone who wants the short view.
    ///     </para>
    ///     <para>
    ///     <b>The rows come from <see cref="InterfaceHookRow"/> and are built without touching a
    ///     control</b>, so the mapping is testable. Nothing in this repository's suite covers
    ///     WinForms, and a table assembled inside a population loop would be defended by nothing.
    ///     </para>
    /// </remarks>
    public sealed class InterfaceHookPanel : UserControl {
        /* Consolas 9, as every grid in this application pins it. The form puts Consolas 12 on the
           tab control and everything under it inherits, which is half again what these columns are
           laid out for. */
        private static readonly Font GridFont = new Font("Consolas", 9F);

        /// <summary>
        ///     The script column, which is the one cell here that names a record in another index.
        /// </summary>
        /// <remarks>
        ///     A link rather than a number, so following it is the shared navigation the rest of the
        ///     editor uses. What following it <i>does</i> is the form's decision and not this
        ///     panel's - the same split <c>DefinitionListPanel</c> makes.
        /// </remarks>
        private static readonly DefinitionColumn ScriptColumn =
            DefinitionColumn.Link<InterfaceHookRow>("Script", RSConstants.CLIENT_SCRIPTS_INDEX,
                row => row.ScriptId >= 0 ? row.ScriptId : null, 90);

        private readonly FastObjectListView list = new FastObjectListView {
            Dock = DockStyle.Fill,
            Font = GridFont,
            FullRowSelect = true,
            GridLines = true,
            ShowGroups = false,
            UseFiltering = true,
            View = View.Details
        };

        private readonly EditorToolStrip tools = new EditorToolStrip {
            Dock = DockStyle.Top,
            GripStyle = ToolStripGripStyle.Hidden
        };

        private readonly List<InterfaceHookRow> rows = new();

        private bool onlyStored;

        /// <summary>Creates an empty panel with its columns already built.</summary>
        public InterfaceHookPanel() {
            Dock = DockStyle.Fill;

            BuildColumns();
            BuildTools();

            //Added after the filled grid, because docking resolves from the end of the collection
            //backwards and the grid would otherwise claim the whole panel.
            Controls.Add(list);
            Controls.Add(tools);

            list.CellClick += OnCellClick;
        }

        /// <summary>
        ///     Raised when the user activates a cell naming a record elsewhere in the cache.
        /// </summary>
        /// <remarks>
        ///     The same event type a definition list raises, so the form's existing handler takes it
        ///     unchanged and there is exactly one place that decides what following a reference
        ///     means. A second navigation mechanism for this one panel would be a second history and
        ///     a second back button.
        /// </remarks>
        public event EventHandler<DefinitionCellActivatedEventArgs>? ReferenceActivated;

        /// <summary>
        ///     Shows one component's hooks, or clears the panel.
        /// </summary>
        /// <remarks>
        ///     Deliberately not called <c>Show</c>: this derives from a <see cref="Control"/>, and an
        ///     overload of <c>Control.Show</c> meaning something else entirely is the kind of name
        ///     that reads correctly and does the wrong thing.
        /// </remarks>
        /// <param name="component">The selected component, or null.</param>
        public void ShowHooks(InterfaceComponentDefinition? component) {
            rows.Clear();

            if (component != null)
                rows.AddRange(InterfaceHookRow.For(component));

            Republish();
        }

        private void Republish() {
            if (!onlyStored) {
                list.SetObjects(new List<InterfaceHookRow>(rows));
                return;
            }

            var stored = new List<InterfaceHookRow>();
            foreach (InterfaceHookRow row in rows) {
                if (row.IsStored)
                    stored.Add(row);
            }

            list.SetObjects(stored);
        }

        private void BuildColumns() {
            AddColumn("Slot", 50, row => Row(row).Slot < 0 ? "-" : Row(row).Slot.ToString());
            AddColumn("Storage", 190, row => Row(row).Storage);
            AddColumn("Set by", 260, row => Row(row).Setter);
            AddColumn("Triggers", 170, row => Row(row).Triggers);

            var script = new OLVColumn(ScriptColumn.Header, null) {
                Width = ScriptColumn.Width,
                Groupable = false,
                IsEditable = false,
                AspectGetter = row => row == null ? null : ScriptColumn.Read(row),
                Renderer = new DefinitionCellRenderer(ScriptColumn, () => null)
            };

            list.AllColumns.Add(script);
            list.Columns.Add(script);

            AddColumn("Call", 320, row => Row(row).Call);
            AddColumn("Stored operands", 260, row => Row(row).Operands);
        }

        private void BuildTools() {
            tools.AddToggle(EditorIcon.Visible,
                "Show only the slots this component actually stores", Keys.None, (sender, _) => {
                    if (sender is not EditorToolButton button)
                        return;

                    onlyStored = button.Checked;
                    Republish();
                });

            tools.Items.Add(new ToolStripControlHost(InfoAffordance.For(list, InfoKind.Limitation,
                "These twenty slots are ALL the behaviour an interface file carries.\n\n" +
                "The format stores no per-state appearance at all: a component holds one colour, " +
                "one sprite and one font, and hover, pressed and selected are produced at runtime by " +
                "CS2 scripts fired from these slots. That is why the canvas draws a bank window with " +
                "nothing selected, no item icons and no counts - it is showing what the file is, not " +
                "failing to show what the game does.\n\n" +
                "The slots are named after their storage and their setter, never after an event. " +
                "Which event fires which array is decided outside the CS2 dispatcher, so 'on-click' " +
                "would be invented rather than derived. The client field and the opcode are both " +
                "checkable against RSInterface.unpackConfig and the 1400 setter block.\n\n" +
                "Slot 0 has no setter. It is the hook the client fires itself over every component " +
                "as an interface opens (Class247.java:4130-4136).\n\n" +
                "Slots 5, 6, 7, 18 and 19 each pair with a trigger array, and their CS2 setters " +
                "assign the hook and its triggers in one statement, so the two are shown together.\n\n" +
                "Ten further CS2 opcodes, " + InterfaceHookSlots.RuntimeOnlySetters + ", set hook " +
                "arrays that are not in the wire format at all. They have no row here and there is " +
                "nothing to look for in the bytes.")) {
                Alignment = ToolStripItemAlignment.Right
            });
        }

        /// <summary>
        ///     Reports an activated link, without deciding what following it means.
        /// </summary>
        /// <remarks>
        ///     Guarded on the renderer rather than on the column index, so inserting a column cannot
        ///     silently point this at a different cell.
        /// </remarks>
        private void OnCellClick(object? sender, CellClickEventArgs e) {
            if (e.Model == null || e.Column?.Renderer is not DefinitionCellRenderer hit)
                return;

            DefinitionCellVisual visual = hit.VisualFor(e.Model);
            if (visual.Art == DefinitionCellArt.None)
                return;

            ReferenceActivated?.Invoke(this,
                new DefinitionCellActivatedEventArgs(e.Model, visual, hit.DescribedColumn));
        }

        private void AddColumn(string heading, int width, Func<object, object?> read) {
            //Through the shared helper, which is where the null-row guard lives: the grid evaluates
            //aspects for rows it is recycling and for cells measured before a model is attached.
            DetailGrid.AddColumn(list, heading, width, read);
        }

        private static InterfaceHookRow Row(object row) {
            return (InterfaceHookRow) row;
        }
    }
}
