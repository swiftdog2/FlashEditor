using BrightIdeasSoftware;
using FlashEditor.cache;
using FlashEditor.Definitions.Editing;
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Windows.Forms;
using static FlashEditor.Utils.DebugUtil;

namespace FlashEditor.Definitions.ClientScripts {
    /// <summary>
    ///     The Client Scripts tab: index 12 from a list of compiled CS2 scripts down to the
    ///     instruction stream and switch tables one of them holds.
    /// </summary>
    /// <remarks>
    ///     Two levels because a script <i>is</i> an instruction stream. The row says which script,
    ///     how large its frame is and what it takes as parameters; the panes beside it are the
    ///     content of the record, which cannot be a cell.
    ///     <para>
    ///     <b>Only the selected script's instructions are ever rows.</b> Measured over both supported
    ///     caches, which agree exactly on this index: 4,149 scripts holding 335,158 instructions, and
    ///     script 978 alone holds 7,084 of them. A flat grid of every instruction in the index would
    ///     therefore be eighty times the list it replaced, with nothing on it to say where one script
    ///     ends, and the worst single selection here is two orders of magnitude smaller than that.
    ///     Filling the grid from the selected row also means selecting a script costs no cache read
    ///     at all: the row already carries the decoded record, exactly as the Enums and Interfaces
    ///     tabs do.
    ///     </para>
    ///     <para>
    ///     <b>The list itself is a <see cref="DefinitionListPanel"/></b>, so the worker, the
    ///     percent-boundary progress, the UI-thread population and the write-back are not restated
    ///     here. It decodes every declared script once at bind, which is where the memory goes: the
    ///     decoded instructions are held so the detail panes are free and so the four editable counts
    ///     have a whole record to re-encode. That is affordable because the whole index is small -
    ///     2,554,245 decompressed bytes across the 4,149 groups - unlike the indexes whose tabs
    ///     deliberately list the reference table instead.
    ///     </para>
    ///     <para>
    ///     <b>The detail panes are read only.</b> The list commits an edit by re-encoding one file,
    ///     and the four counts it offers are single values with a single meaning. An instruction is
    ///     not: its operand width follows from its opcode, and with no disassembler a user retyping
    ///     an opcode has no way to know what the new one reads or does. Saying that in the pane is
    ///     the point - it is the difference between a documented omission and a tab that looks
    ///     broken.
    ///     </para>
    /// </remarks>
    public sealed class ClientScriptEditorPanel : UserControl {
        /// <summary>
        ///     The descriptor the script list is driven by.
        /// </summary>
        /// <remarks>
        ///     One instance, held rather than built per bind, because <c>DefinitionListPanel.Bind</c>
        ///     treats a different descriptor as a different thing to show and would reload the whole
        ///     index on every visit to the tab.
        /// </remarks>
        private static readonly IDefinitionListDescriptor Descriptor = new ClientScriptListDescriptor();

        /// <summary>The opcode whose operand names one of the script's switch tables.</summary>
        /// <remarks>
        ///     <c>Class247.java:7975</c> indexes the block array with the instruction's own operand,
        ///     which is what the "Switched at" column joins on. The join is self-proving rather than
        ///     merely plausible, which is the bar this cache demands: both supported caches hold 831
        ///     switch blocks and exactly 831 opcode-51 instructions, and every one of the 831 blocks
        ///     is named by one of them. A blank cell in that column would therefore falsify the
        ///     reading on screen, which is why it is a column rather than a comment.
        /// </remarks>
        private const int SwitchOpcode = 51;

        /* Consolas 9 on every child. The form puts Consolas 12 on the tab control and everything
           under it inherits, which is half again what these grids are laid out for. */
        private static readonly Font GridFont = new Font("Consolas", 9F);

        private readonly DefinitionListPanel scripts = new DefinitionListPanel();

        private readonly FastObjectListView instructions = Grid();
        private readonly FastObjectListView switchCases = Grid();

        /* AutoSize rather than a stated height on every label here, so the lines the text needs are
           the lines it gets whatever font the form ends up scaling to. The notice is three short
           lines rather than one long one for the same reason: a docked label is given the container's
           width and does not wrap, so a line longer than the window is a line with its tail cut off. */
        private readonly Label notice = new Label {
            AutoSize = true,
            Dock = DockStyle.Top,
            Font = GridFont,
            Text = NoticeText
        };

        private readonly Label header = new Label {
            AutoSize = true,
            Dock = DockStyle.Top,
            Font = GridFont,
            Text = NoCacheText
        };

        private readonly Label switchNote = new Label {
            AutoSize = true,
            Dock = DockStyle.Top,
            Font = GridFont,
            Text = SwitchNoteText
        };

        /* No splitter states a minimum size. Setting one re-checks the current distance against it,
           and a container is still at its 150x100 default when a field initialiser runs, so a
           minimum wide enough to be useful throws before the panel has ever been shown. */
        private readonly SplitContainer listAndDetail = new SplitContainer {
            Dock = DockStyle.Fill,
            Orientation = Orientation.Vertical
        };

        private readonly SplitContainer streamAndSwitches = new SplitContainer {
            Dock = DockStyle.Fill,
            Orientation = Orientation.Horizontal
        };

        private const string NoCacheText = "No cache loaded";
        private const string NoSelectionText = "Select a script to see its instructions";

        /// <summary>
        ///     What this tab deliberately does not do, stated where a user can read it.
        /// </summary>
        /// <remarks>
        ///     Required by the UI conventions and by the codec itself: <c>ClientScriptInstruction</c>
        ///     keeps the numeric opcode because naming one needs a table over the roughly 580 opcodes
        ///     this cache uses, spread across three dispatchers in the client's <c>Class247</c>. A
        ///     user comparing this grid against a decompiler has no way to tell a missing feature
        ///     from a defect unless the tab says which it is.
        /// </remarks>
        private const string NoticeText =
            "Opcodes are the raw numbers the file stores. This tab has no disassembler - naming them needs a table " +
            "over the ~580 opcodes in use across three dispatchers in the client's Class247, which is separate work.\n" +
            "The identifier is not a name hash. A few are packed interface hooks; most are unexplained 32-bit values, " +
            "and no script name has ever been recovered from this index.\n" +
            "The four counts are editable. Committing one re-encodes the whole script and so changes its archive CRC. " +
            "A parameter count above its matching local count writes a script the client cannot call.";

        private const string SwitchNoteText =
            "Switch tables. Jump is a program-counter delta in instructions, applied after the counter has moved " +
            "past the switch (Class247.java:7975-7980), so the target is the switch position plus one plus the jump.";

        private RSCache? cache;
        private bool splittersPlaced;

        /// <summary>Creates the panel.</summary>
        public ClientScriptEditorPanel() {
            Dock = DockStyle.Fill;
            BuildLayout();

            scripts.SelectedRowChanged += (_, _) => ShowScript(scripts.SelectedRow as ClientScriptListing);
        }

        /// <summary>
        ///     Points the panel at a cache, or clears it when given none.
        /// </summary>
        /// <remarks>
        ///     <c>Editor.LoadEditorTab</c> calls this on every visit to the tab, so a rebind of the
        ///     cache already on display has to be a no-op or the selection is thrown away and the
        ///     whole index decoded again each time. Identity is the right test because opening a
        ///     cache builds a new <see cref="RSCache"/>.
        /// </remarks>
        /// <param name="newCache">The open cache, or null to unbind.</param>
        public void Bind(RSCache? newCache) {
            if (ReferenceEquals(newCache, cache))
                return;

            cache = newCache;
            instructions.ClearObjects();
            switchCases.ClearObjects();
            header.Text = newCache == null ? NoCacheText : NoSelectionText;
            scripts.Bind(newCache, Descriptor);
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
        ///     one more literal the form multiplies by its scale factor. A fraction of the measured
        ///     size is the same division at any font or DPI.
        ///     <para>
        ///     Deferred to layout rather than the constructor because assigning a distance the control
        ///     is not yet large enough for throws, and a field initialiser runs while the container is
        ///     still 150x100. Once only, so a user who drags a splitter keeps where they put it.
        ///     </para>
        /// </remarks>
        private void PlaceSplitters() {
            if (splittersPlaced || listAndDetail.Width < 200 || streamAndSwitches.Height < 200)
                return;

            //Set before the assignments, not after: changing a splitter distance lays the panel out
            //again, and this is called from that layout.
            splittersPlaced = true;

            try {
                listAndDetail.SplitterDistance = Math.Max(listAndDetail.Panel1MinSize, listAndDetail.Width * 3 / 5);
                //Two thirds to the stream: 485 of the 4,149 scripts hold a switch table at all, so
                //the lower pane is empty roughly seven selections out of eight.
                streamAndSwitches.SplitterDistance =
                    Math.Max(streamAndSwitches.Panel1MinSize, streamAndSwitches.Height * 2 / 3);
            } catch (InvalidOperationException ex) {
                //Left for the next layout rather than clamped. A clamped distance sticks, and the
                //user would see a collapsed pane on a window that later has room for all three.
                splittersPlaced = false;
                Debug("Client script tab splitters not placed yet: " + ex.Message, LOG_DETAIL.ADVANCED);
            }
        }

        private void BuildLayout() {
            BuildInstructionColumns();
            BuildSwitchColumns();

            streamAndSwitches.Panel1.Controls.Add(instructions);

            //Docking resolves from the end of the Controls collection backwards, so every label has
            //to be added after the filled control beside it or that control claims the whole pane.
            streamAndSwitches.Panel2.Controls.Add(switchCases);
            streamAndSwitches.Panel2.Controls.Add(switchNote);

            listAndDetail.Panel1.Controls.Add(scripts);
            listAndDetail.Panel2.Controls.Add(streamAndSwitches);
            listAndDetail.Panel2.Controls.Add(header);

            Controls.Add(listAndDetail);
            Controls.Add(notice);
        }

        private void BuildInstructionColumns() {
            AddColumn(instructions, "#", 60, row => Instruction(row).Position);
            AddColumn(instructions, "Offset", 80, row => Instruction(row).Offset);
            AddColumn(instructions, "Opcode", 80, row => Instruction(row).Opcode);
            AddColumn(instructions, "Operand", 90, row => Instruction(row).OperandWidth);
            AddColumn(instructions, "Value", 460, row => Instruction(row).Value);
        }

        private void BuildSwitchColumns() {
            AddColumn(switchCases, "Block", 70, row => SwitchCase(row).Block);
            AddColumn(switchCases, "Case", 70, row => SwitchCase(row).Position);
            AddColumn(switchCases, "Value", 130, row => SwitchCase(row).Value);
            AddColumn(switchCases, "Jump", 90, row => SwitchCase(row).Jump);
            AddColumn(switchCases, "Switched at", 200, row => SwitchCase(row).SwitchedAt);
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
        ///     The row as an instruction listing, or a placeholder for the null row.
        /// </summary>
        /// <remarks>
        ///     ObjectListView evaluates aspect getters for rows being recycled during a scroll and for
        ///     cells it measures before a model is attached, so a null row is a legitimate state and
        ///     renders as an empty cell. A row of the <i>wrong</i> type still throws through the cast,
        ///     because that can only mean these columns were wired to a grid that holds something
        ///     else.
        /// </remarks>
        /// <param name="row">The row ObjectListView handed over.</param>
        /// <returns>The listing, or an empty one.</returns>
        private static InstructionListing Instruction(object? row) {
            return row == null ? InstructionListing.Empty : (InstructionListing) row;
        }

        /// <summary>The row as a switch-case listing, on the same terms as <see cref="Instruction"/>.</summary>
        /// <param name="row">The row ObjectListView handed over.</param>
        /// <returns>The listing, or an empty one.</returns>
        private static SwitchCaseListing SwitchCase(object? row) {
            return row == null ? SwitchCaseListing.Empty : (SwitchCaseListing) row;
        }

        /// <summary>Fills both detail grids from the selected script.</summary>
        /// <remarks>
        ///     No cache read at all: the list row already carries the decoded record, because one
        ///     script is one file and the descriptor decoded it to build the row.
        /// </remarks>
        /// <param name="listing">The selected script, or null.</param>
        private void ShowScript(ClientScriptListing? listing) {
            instructions.ClearObjects();
            switchCases.ClearObjects();

            if (cache == null || listing == null) {
                header.Text = cache == null ? NoCacheText : NoSelectionText;
                return;
            }

            header.Text = Describe(listing);
            instructions.SetObjects(ListInstructions(listing.Record));
            switchCases.SetObjects(ListSwitchCases(listing.Record));
        }

        /// <summary>The line above the detail grids: what the selected script is.</summary>
        /// <param name="listing">The selected script.</param>
        /// <returns>The summary line.</returns>
        private static string Describe(ClientScriptListing listing) {
            return "Script " + listing.ScriptId + " - " + listing.StoredLength.ToString("N0") + " bytes, " +
                   listing.InstructionCount.ToString("N0") + " instructions, frame " +
                   listing.IntegerLocalCount + " int / " + listing.StringLocalCount + " string, takes " +
                   listing.IntegerParameterCount + " int / " + listing.StringParameterCount + " string, " +
                   listing.SwitchBlockCount + " switch blocks (" + listing.SwitchCaseCount + " cases) - " +
                   "identifier " + listing.Identifier + " (" + listing.IdentifierHex + ") - name " +
                   listing.NameOrAbsent;
        }

        /// <summary>
        ///     One row per instruction of the selected script, in execution order.
        /// </summary>
        /// <remarks>
        ///     The byte offset is accumulated here rather than stored on the instruction, because it
        ///     is a property of the record's layout and not of the instruction: the same instruction
        ///     sits at a different offset in every script that holds it. It is worth a column at all
        ///     so the grid can be checked against a hex dump of the file.
        /// </remarks>
        /// <param name="record">The decoded script.</param>
        /// <returns>The instruction rows.</returns>
        private static List<InstructionListing> ListInstructions(ClientScriptDefinition record) {
            var rows = new List<InstructionListing>(record.Instructions.Count);

            //The name field precedes the stream and always costs at least its terminator, which is
            //the byte a nameless record stores on its own.
            int offset = (record.NameBytes?.Length ?? 0) + 1;

            for (int position = 0; position < record.Instructions.Count; position++) {
                ClientScriptInstruction instruction = record.Instructions[position];
                rows.Add(new InstructionListing(position, offset, instruction));
                offset += instruction.StoredLength;
            }

            return rows;
        }

        /// <summary>
        ///     One row per switch arm of the selected script, with the instructions that reach it.
        /// </summary>
        /// <param name="record">The decoded script.</param>
        /// <returns>The switch rows, in stored order.</returns>
        private static List<SwitchCaseListing> ListSwitchCases(ClientScriptDefinition record) {
            var rows = new List<SwitchCaseListing>();
            if (record.SwitchBlocks.Count == 0)
                return rows;

            IReadOnlyList<string> switchedAt = FindSwitchSites(record);

            for (int block = 0; block < record.SwitchBlocks.Count; block++) {
                IList<ClientScriptSwitchCase> cases = record.SwitchBlocks[block].Cases;
                string sites = block < switchedAt.Count ? switchedAt[block] : string.Empty;

                for (int arm = 0; arm < cases.Count; arm++)
                    rows.Add(new SwitchCaseListing(block, arm, cases[arm], sites));
            }

            return rows;
        }

        /// <summary>
        ///     Which instructions select each switch block, joined on the operand the client indexes
        ///     the block array with.
        /// </summary>
        /// <remarks>
        ///     A list rather than the single site it usually is, because nothing in the format stops
        ///     two instructions naming the same block, and printing only the first would quietly hide
        ///     the second.
        /// </remarks>
        /// <param name="record">The decoded script.</param>
        /// <returns>The instruction positions per block, as text.</returns>
        private static IReadOnlyList<string> FindSwitchSites(ClientScriptDefinition record) {
            var sites = new List<int>[record.SwitchBlocks.Count];

            for (int position = 0; position < record.Instructions.Count; position++) {
                ClientScriptInstruction instruction = record.Instructions[position];
                if (instruction.Opcode != SwitchOpcode)
                    continue;

                int block = instruction.IntegerOperand;
                if (block < 0 || block >= sites.Length)
                    continue;

                (sites[block] ??= new List<int>()).Add(position);
            }

            var text = new string[sites.Length];
            for (int block = 0; block < sites.Length; block++)
                text[block] = sites[block] == null ? string.Empty : string.Join(", ", sites[block]!);

            return text;
        }

        /// <summary>One instruction of the selected script, as a grid row.</summary>
        private sealed class InstructionListing {
            /// <summary>The row rendered for a null model, which ObjectListView asks for while recycling.</summary>
            internal static readonly InstructionListing Empty = new InstructionListing();

            private readonly ClientScriptInstruction? instruction;
            private readonly bool present;

            private InstructionListing() {
            }

            internal InstructionListing(int position, int offset, ClientScriptInstruction instruction) {
                Position = position;
                Offset = offset;
                this.instruction = instruction;
                present = true;
            }

            /// <summary>Where the instruction sits in the stream, which is what a jump is relative to.</summary>
            internal object? Position { get; }

            /// <summary>Where the instruction starts in the decompressed file.</summary>
            internal object? Offset { get; }

            /// <summary>The stored opcode, unnamed by design.</summary>
            internal object? Opcode => present ? instruction!.Opcode : null;

            /// <summary>
            ///     How wide the operand is stored, which follows from the opcode.
            /// </summary>
            /// <remarks>
            ///     A column because the width rule is the one thing about this format that can
            ///     desynchronise a reader, and because it is the only clue on screen as to what kind
            ///     of thing the value beside it is.
            /// </remarks>
            internal object? OperandWidth {
                get {
                    if (!present)
                        return null;

                    return instruction!.OperandKind switch {
                        ClientScriptOperandKind.Text => "string",
                        ClientScriptOperandKind.Byte => "byte",
                        _ => "int32"
                    };
                }
            }

            /// <summary>The operand, quoted when it is text so an empty string is visible as one.</summary>
            internal object? Value {
                get {
                    if (!present)
                        return null;

                    return instruction!.OperandKind == ClientScriptOperandKind.Text
                        ? "\"" + instruction.TextOperand + "\""
                        : instruction.IntegerOperand.ToString();
                }
            }
        }

        /// <summary>One arm of one switch table of the selected script, as a grid row.</summary>
        private sealed class SwitchCaseListing {
            /// <summary>The row rendered for a null model, which ObjectListView asks for while recycling.</summary>
            internal static readonly SwitchCaseListing Empty = new SwitchCaseListing();

            private readonly ClientScriptSwitchCase arm;
            private readonly bool present;

            private SwitchCaseListing() {
            }

            internal SwitchCaseListing(int block, int position, ClientScriptSwitchCase arm, string switchedAt) {
                Block = block;
                Position = position;
                this.arm = arm;
                SwitchedAt = switchedAt;
                present = true;
            }

            /// <summary>Which table the arm belongs to, which is the operand that selects it.</summary>
            internal object? Block { get; }

            /// <summary>Where the arm sits in its table, which is load bearing: the order is stored.</summary>
            internal object? Position { get; }

            /// <summary>The value popped off the integer stack that takes this arm.</summary>
            internal object? Value => present ? arm.Value : null;

            /// <summary>The program-counter delta, in instructions, and legitimately negative.</summary>
            internal object? Jump => present ? arm.JumpOffset : null;

            /// <summary>The instructions that select this arm's table, or blank when none do.</summary>
            internal object? SwitchedAt { get; }
        }
    }
}
