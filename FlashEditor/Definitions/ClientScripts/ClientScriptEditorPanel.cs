using BrightIdeasSoftware;
using FlashEditor.Cache;
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

        /* The opcode whose operand names one of the script's switch tables is
           ClientScriptOpcodes.SwitchOpcode, held there rather than here because the disassembler
           needs it too. Class247.java:7975 indexes the block array with the instruction's own
           operand, which is what the "Switched at" column joins on. The join is self-proving rather
           than merely plausible, which is the bar this cache demands: both supported caches hold 831
           switch blocks and exactly 831 opcode-51 instructions, and every one of the 831 blocks is
           named by one of them. A blank cell in that column would therefore falsify the reading on
           screen, which is why it is a column rather than a comment. */

        /* Consolas 9 on every child. The form puts Consolas 12 on the tab control and everything
           under it inherits, which is half again what these grids are laid out for. */
        private static readonly Font GridFont = new Font("Consolas", 9F);

        private readonly DefinitionListPanel scripts = new DefinitionListPanel();

        private readonly FastObjectListView instructions = Grid();
        private readonly FastObjectListView switchCases = Grid();

        /* AutoSize rather than a stated height on every label here, so the lines the text needs are
           the lines it gets whatever font the form ends up scaling to. Height only - the width comes
           from the dock, and wrapping within it is what ConstrainLabels arranges. */
        private readonly Label notice = new Label {
            AutoSize = true,
            Dock = DockStyle.Top,
            Font = GridFont,
            Text = NoticeText
        };

        /* Separate from the notice because it is measured rather than written: the notice states
           the policy and this states what that policy currently buys, from the rows in hand. */
        private readonly Label coverage = new Label {
            AutoSize = true,
            Dock = DockStyle.Top,
            Font = GridFont,
            Text = CoverageUnknownText
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
        ///     What this tab does and, more importantly, what it still cannot do.
        /// </summary>
        /// <remarks>
        ///     Required by the UI conventions. The disassembler names only the opcodes the 637
        ///     client's dispatch settles, so the grid holds a mixture of named and unnamed
        ///     instructions and a user has no way to tell which is which - or to tell an unnamed
        ///     opcode from a broken one - unless the tab says so. The measured share is filled in at
        ///     load time from the rows themselves rather than written here, because a figure written
        ///     down is a figure about whichever cache someone measured.
        /// </remarks>
        private const string NoticeText =
            "The Opcode column is always the raw number; the Mnemonic beside it is this editor's reading of it, " +
            "carried only where the 637 client's own dispatch proves it - the Client column cites the line. " +
            "A blank mnemonic means not yet named, not broken.\n" +
            "The cc_ and if_ prefixes name an addressing mode: cc_ acts on the interpreter's active component and " +
            "if_ pops its target off the stack. The client writes cc_ itself, in opcode 101's exception message; " +
            "if_ appears nowhere in it and is the conventional spelling for a mechanism this editor re-derived. " +
            "For a cc_ instruction the Value column shows which of the two active-component registers the operand " +
            "byte selects, .active or active, beside the raw byte.\n" +
            "Jump targets are resolved and the In column marks a position something jumps to. Basic blocks, loops " +
            "and if/else structure are not reconstructed: this is a linear listing and implies no structure.\n" +
            "The identifier is not a name hash - a few are packed interface hooks, most are unexplained. The four " +
            "counts are editable; committing one re-encodes the whole script and so changes its archive CRC.";

        private const string SwitchNoteText =
            "Switch tables. Jump is a delta in instructions rather than bytes, applied once the counter has moved " +
            "past the switch (Class247.java:7975-7980), so the target instruction is the switch position plus one " +
            "plus the jump. Both are signed and both are legitimately negative. Target is that arithmetic resolved " +
            "against the instruction that selects the block, so a block two instructions reach has two sets of " +
            "targets.";

        private RSCache? cache;
        private bool splittersPlaced;

        /// <summary>What the coverage line says before a cache has been loaded and measured.</summary>
        private const string CoverageUnknownText = "Disassembler coverage is measured once the index has loaded.";

        /// <summary>Creates the panel.</summary>
        public ClientScriptEditorPanel() {
            Dock = DockStyle.Fill;
            BuildLayout();

            scripts.SelectedRowChanged += (_, _) => ShowScript(scripts.SelectedRow as ClientScriptListing);
            scripts.RowsLoaded += (_, _) => coverage.Text = MeasureCoverage();
        }

        /// <summary>
        ///     States how much of the loaded index the opcode table can name, as a share of
        ///     instructions.
        /// </summary>
        /// <remarks>
        ///     <b>Instructions, not distinct opcodes, and the difference is enormous.</b> The 32
        ///     opcodes the client handles in its in-line chain are 5% of the roughly 580 this index
        ///     uses and carry 85% of the instructions, so a percentage of distinct opcodes would read
        ///     as derisory while describing a tab that names nearly everything on screen. Measured
        ///     from the rows in hand rather than written down, because the figure belongs to whichever
        ///     cache is open.
        /// </remarks>
        /// <returns>The coverage line.</returns>
        private string MeasureCoverage() {
            long instructions = 0;
            long named = 0;
            var distinct = new HashSet<int>();
            var distinctNamed = new HashSet<int>();

            foreach (object row in scripts.Rows) {
                if (row is not ClientScriptListing listing)
                    continue;

                foreach (ClientScriptInstruction instruction in listing.Record.Instructions) {
                    instructions++;
                    distinct.Add(instruction.Opcode);

                    if (ClientScriptOpcodes.MnemonicOf(instruction.Opcode) == null)
                        continue;

                    named++;
                    distinctNamed.Add(instruction.Opcode);
                }
            }

            if (instructions == 0)
                return CoverageUnknownText;

            return "Disassembler coverage in this cache: " + named.ToString("N0") + " of " +
                   instructions.ToString("N0") + " instructions named (" +
                   (100.0 * named / instructions).ToString("F2") + "%), which is " + distinctNamed.Count +
                   " of the " + distinct.Count + " distinct opcodes in use. The rest keep the number.";
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
            coverage.Text = CoverageUnknownText;
            scripts.Bind(newCache, Descriptor);
        }

        /// <summary>Places the splitters and re-measures the labels once the layout pass has run.</summary>
        /// <param name="levent">The event data.</param>
        protected override void OnLayout(LayoutEventArgs levent) {
            base.OnLayout(levent);
            PlaceSplitters();
            ConstrainLabels();
        }

        /// <summary>
        ///     Lets each docked label wrap inside its own pane rather than run off the edge of it.
        /// </summary>
        /// <remarks>
        ///     An <c>AutoSize</c> label docked to the top is handed the container's width and then
        ///     measures its text on one line, so a sentence longer than the pane is a sentence with
        ///     its tail cut off. The switch note lost its client citation exactly that way in the
        ///     narrower right-hand pane, and the summary line - which names a script, its size, its
        ///     frame, its parameters and its identifier - is longer still.
        ///     <para>
        ///     A <c>MaximumSize</c> width is what turns the measurement into a wrapping one. It is
        ///     the one pixel count in this panel, and it is taken from the measured pane on every
        ///     layout rather than written down, so it stays right at any font, DPI or splitter
        ///     position. Assigned only when it changes, because assigning it lays the panel out
        ///     again and this runs from that layout.
        ///     </para>
        /// </remarks>
        private void ConstrainLabels() {
            Constrain(notice, ClientSize.Width);
            Constrain(coverage, ClientSize.Width);
            Constrain(header, listAndDetail.Panel2.ClientSize.Width);
            Constrain(switchNote, streamAndSwitches.Panel2.ClientSize.Width);
        }

        /// <summary>Caps one label's width so its text wraps at the pane edge.</summary>
        /// <param name="label">The label.</param>
        /// <param name="width">The pane's measured client width.</param>
        private static void Constrain(Label label, int width) {
            //Height stays 0, which is MaximumSize's spelling of "no limit" - the label has to be
            //free to grow downwards, since wrapping is the whole point of capping the width.
            if (width > 0 && label.MaximumSize.Width != width)
                label.MaximumSize = new Size(width, 0);
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
                //Two fifths to the list. This was half while the instruction grid had five columns,
                //on the reasoning that the list's columns are all fixed-width numbers with no slack
                //to give up. The disassembler took that grid to ten columns and inverted it: at a
                //half split the Flow column fell off the right edge, and a control flow edge that
                //needs a horizontal scroll to see is one nobody sees. The list loses nothing it was
                //showing, because it was already scrolling at half.
                listAndDetail.SplitterDistance =
                    Math.Max(listAndDetail.Panel1MinSize, listAndDetail.Width * 2 / 5);
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
            Controls.Add(coverage);
            Controls.Add(notice);
        }

        /* Opcode before Mnemonic, and both always present. The number is the thing the file holds
           and the name is this project's reading of it, so the reading sits beside the evidence and
           a wrong name is one glance from being caught.

           Ordered so the seven columns that make the stream readable - through Flow - fit the pane
           at its default split, because a control flow edge that needs a horizontal scroll to see
           is one nobody sees. Stored width, the stack operands, Effect and the client citation are
           the ones to scroll for: they answer a question about a particular instruction rather than
           carrying the listing. Stored as and Takes sit together because both describe the calling
           convention - one the byte in the file, the other what the arm pulls off the stacks. */
        private void BuildInstructionColumns() {
            AddColumn(instructions, "#", 45, row => Instruction(row).Position);
            AddColumn(instructions, "In", 34, row => Instruction(row).LabelMark);
            AddColumn(instructions, "Offset", 62, row => Instruction(row).Offset);
            AddColumn(instructions, "Opcode", 62, row => Instruction(row).Opcode);
            AddColumn(instructions, "Mnemonic", MnemonicColumnWidth(), row => Instruction(row).Mnemonic);
            AddColumn(instructions, "Value", 150, row => Instruction(row).Value);
            AddColumn(instructions, "Flow", 88, row => Instruction(row).Flow);
            AddColumn(instructions, "Stored as", 80, row => Instruction(row).OperandWidth);
            AddColumn(instructions, "Takes", 240, row => Instruction(row).StackOperands);
            AddColumn(instructions, "Effect", 520, row => Instruction(row).Effect);
            AddColumn(instructions, "Client", 150, row => Instruction(row).Citation);
        }

        /// <summary>
        ///     Measures the mnemonic column from the widest name the table actually holds.
        /// </summary>
        /// <remarks>
        ///     The one column here whose content this project controls and will keep adding to, so a
        ///     written-down width would be right until the next opcode is named and silently wrong
        ///     afterwards - a clipped mnemonic reads as a different mnemonic, which is the one
        ///     failure this whole tab is built to avoid. Measured in the grid's own pinned font
        ///     rather than the form's, since that is what will render it, which also means the width
        ///     survives the DPI scaling that the UI conventions warn about.
        /// </remarks>
        /// <returns>The column width in pixels.</returns>
        private static int MnemonicColumnWidth() {
            int widest = TextRenderer.MeasureText("Mnemonic", GridFont).Width;

            foreach (int opcode in ClientScriptOpcodes.NamedOpcodes) {
                int width = TextRenderer.MeasureText(ClientScriptOpcodes.MnemonicOf(opcode), GridFont).Width;
                if (width > widest)
                    widest = width;
            }

            //MeasureText excludes the cell's own padding, and a name flush against the next column
            //boundary reads as truncated even when it is whole.
            return widest + 12;
        }

        private void BuildSwitchColumns() {
            AddColumn(switchCases, "Block", 60, row => SwitchCase(row).Block);
            AddColumn(switchCases, "Case", 60, row => SwitchCase(row).Position);
            AddColumn(switchCases, "Value", 120, row => SwitchCase(row).Value);
            AddColumn(switchCases, "Jump", 80, row => SwitchCase(row).Jump);
            AddColumn(switchCases, "Target", 90, row => SwitchCase(row).Target);
            AddColumn(switchCases, "Switched at", 180, row => SwitchCase(row).SwitchedAt);
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
            //Delegated so the null-row guard has one implementation. Ten copies of this method
            //existed and not one of them had it, which is how closing a cache crashed the
            //interfaces list.
            DetailGrid.AddColumn(list, heading, width, read);
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

            ClientScriptDisassembly disassembly = ClientScriptDisassembly.Of(listing.Record);

            header.Text = Describe(listing, disassembly);
            instructions.SetObjects(ListInstructions(disassembly));
            switchCases.SetObjects(ListSwitchCases(listing.Record));
        }

        /// <summary>The line above the detail grids: what the selected script is.</summary>
        /// <param name="listing">The selected script.</param>
        /// <param name="disassembly">The script's disassembly, for the per-script naming figure.</param>
        /// <returns>The summary line.</returns>
        private static string Describe(ClientScriptListing listing, ClientScriptDisassembly disassembly) {
            string named = disassembly.InstructionCount == 0
                ? "no instructions"
                : disassembly.NamedInstructions + " of " + disassembly.InstructionCount + " named (" +
                  (100.0 * disassembly.NamedInstructions / disassembly.InstructionCount).ToString("F1") + "%)";

            //Only mentioned when it is not zero, which it is everywhere in both supported caches.
            //A permanent "0 unresolvable" would train a reader to stop reading the line.
            string unresolvable = disassembly.UnresolvableTargets == 0
                ? string.Empty
                : " - " + disassembly.UnresolvableTargets + " jump targets land outside the script";

            return "Script " + listing.ScriptId + " - " + listing.StoredLength.ToString("N0") + " bytes, " +
                   listing.InstructionCount.ToString("N0") + " instructions, " + named + ", frame " +
                   listing.IntegerLocalCount + " int / " + listing.StringLocalCount + " string, takes " +
                   listing.IntegerParameterCount + " int / " + listing.StringParameterCount + " string, " +
                   listing.SwitchBlockCount + " switch blocks (" + listing.SwitchCaseCount + " cases) - " +
                   "identifier " + listing.Identifier + " (" + listing.IdentifierHex + ") - name " +
                   listing.NameOrAbsent + unresolvable;
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
        /// <param name="disassembly">The disassembled script.</param>
        /// <returns>The instruction rows.</returns>
        private static List<InstructionListing> ListInstructions(ClientScriptDisassembly disassembly) {
            var rows = new List<InstructionListing>(disassembly.InstructionCount);

            foreach (ClientScriptDisassemblyLine line in disassembly.Lines)
                rows.Add(new InstructionListing(line));

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

            IReadOnlyList<List<int>> sitesPerBlock = FindSwitchSites(record);

            for (int block = 0; block < record.SwitchBlocks.Count; block++) {
                IList<ClientScriptSwitchCase> cases = record.SwitchBlocks[block].Cases;
                List<int> sites = sitesPerBlock[block];

                for (int arm = 0; arm < cases.Count; arm++)
                    rows.Add(new SwitchCaseListing(block, arm, cases[arm], sites,
                        DescribeTargets(record, sites, cases[arm].JumpOffset)));
            }

            return rows;
        }

        /// <summary>
        ///     Resolves one arm's delta against every instruction that selects its block.
        /// </summary>
        /// <remarks>
        ///     A list rather than a single value because the delta is measured from the switch
        ///     instruction, not from the block: two opcode-51 instructions sharing a block resolve
        ///     the same arm to two different targets, and printing one of them would be a quiet lie.
        ///     A block nothing selects has no target at all, which is why the cell can be blank.
        /// </remarks>
        /// <param name="record">The decoded script.</param>
        /// <param name="sites">The instructions that select this arm's block.</param>
        /// <param name="jumpOffset">The arm's stored delta.</param>
        /// <returns>The resolved targets, as text.</returns>
        private static string DescribeTargets(ClientScriptDefinition record, List<int> sites, int jumpOffset) {
            var targets = new List<string>(sites.Count);

            foreach (int site in sites) {
                int? target = ClientScriptDisassembly.ResolveSwitchTarget(record, site, jumpOffset);
                targets.Add(target == null ? "off end" : target.Value.ToString());
            }

            return string.Join(", ", targets);
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
        private static IReadOnlyList<List<int>> FindSwitchSites(ClientScriptDefinition record) {
            var sites = new List<int>[record.SwitchBlocks.Count];
            for (int block = 0; block < sites.Length; block++)
                sites[block] = new List<int>();

            for (int position = 0; position < record.Instructions.Count; position++) {
                ClientScriptInstruction instruction = record.Instructions[position];
                if (instruction.Opcode != ClientScriptOpcodes.SwitchOpcode)
                    continue;

                int block = instruction.IntegerOperand;
                if (block < 0 || block >= sites.Length)
                    continue;

                sites[block].Add(position);
            }

            return sites;
        }

        /// <summary>One instruction of the selected script, as a grid row.</summary>
        private sealed class InstructionListing {
            /// <summary>The row rendered for a null model, which ObjectListView asks for while recycling.</summary>
            internal static readonly InstructionListing Empty = new InstructionListing();

            private readonly ClientScriptDisassemblyLine? line;
            private readonly bool present;

            private InstructionListing() {
            }

            internal InstructionListing(ClientScriptDisassemblyLine line) {
                this.line = line;
                present = true;
            }

            /// <summary>Where the instruction sits in the stream, which is what a jump is relative to.</summary>
            internal object? Position => present ? line!.Position : null;

            /// <summary>
            ///     Marks a position something else in this script jumps to.
            /// </summary>
            /// <remarks>
            ///     The cheap half of control flow, and the half that is provable on its own. Without
            ///     it a reader scrolling a seven thousand instruction stream cannot see where a loop
            ///     re-enters, and with it the entry points are visible without claiming any block
            ///     structure around them.
            /// </remarks>
            internal object? LabelMark => present && line!.IsLabel ? "<-" : string.Empty;

            /// <summary>Where the instruction starts in the decompressed file.</summary>
            internal object? Offset => present ? line!.Offset : null;

            /// <summary>The stored opcode, which is always shown whether or not it is named.</summary>
            internal object? Opcode => present ? line!.Instruction.Opcode : null;

            /// <summary>The proven mnemonic, or blank where none has been established.</summary>
            /// <remarks>
            ///     Blank rather than a placeholder such as "unknown". A blank cell beside a populated
            ///     Effect column reads as "described but not named", which is exactly the state, while
            ///     a word in the cell would sort and filter as if it were a mnemonic of its own.
            /// </remarks>
            internal object? Mnemonic => present ? line!.Info.Mnemonic ?? string.Empty : null;

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

                    return line!.Instruction.OperandKind switch {
                        ClientScriptOperandKind.Text => "string",
                        ClientScriptOperandKind.Byte => "byte",
                        _ => "int32"
                    };
                }
            }

            /// <summary>
            ///     The operand, quoted when it is text so an empty string is visible as one, and named
            ///     where the byte is a selector rather than a value.
            /// </summary>
            /// <remarks>
            ///     Every opcode at or above 100 stores a one-byte operand, and for the component family
            ///     that byte is not a value at all: it picks which of the interpreter's two
            ///     active-component registers the arm reads or writes, which the client spells
            ///     <c>.active-component</c> and <c>active-component</c> in the one message that names
            ///     either (<c>Class247.java:246</c>, <c>:249</c>). The raw byte stays on screen beside
            ///     the name, because nothing proves the flag is only ever 0 or 1 in shipped data and a
            ///     third value would otherwise be invisible.
            /// </remarks>
            internal object? Value {
                get {
                    if (!present)
                        return null;

                    string stored = line!.OperandText();

                    if (line.Info.Addressing != ClientScriptComponentAddressing.ActiveComponent)
                        return stored;

                    return line.Instruction.IntegerOperand switch {
                        0 => "active (" + stored + ")",
                        1 => ".active (" + stored + ")",
                        _ => stored
                    };
                }
            }

            /// <summary>
            ///     What the instruction consumes off the two stacks, in the order a script pushes it.
            /// </summary>
            /// <remarks>
            ///     Blank where this project has not read the arm, which is most opcodes, rather than
            ///     "nothing" - the two are different claims and only one of them is knowledge. A
            ///     trailing <c>$</c> marks a value that comes off the string stack rather than the
            ///     integer one.
            /// </remarks>
            internal object? StackOperands => present ? line!.Info.Operands.Text() : null;

            /// <summary>
            ///     Where control goes from here, for the instructions that decide it.
            /// </summary>
            /// <remarks>
            ///     Resolved, not restated: the stored operand is already in the Value column, and a
            ///     delta is not something a reader should be asked to add to a row number by hand.
            /// </remarks>
            internal object? Flow {
                get {
                    if (!present)
                        return null;

                    if (line!.BranchTarget != null)
                        return "-> " + line.BranchTarget;

                    if (line.SwitchBlock != null)
                        return "switch " + line.SwitchBlock;

                    return line.Instruction.Opcode == ClientScriptOpcodes.ReturnOpcode ? "return" : string.Empty;
                }
            }

            /// <summary>What the client's dispatch arm does, named or not.</summary>
            internal object? Effect => present ? line!.Info.Summary : null;

            /// <summary>Where in the 637 client the row above it can be checked.</summary>
            internal object? Citation => present ? line!.Info.Citation : null;
        }

        /// <summary>One arm of one switch table of the selected script, as a grid row.</summary>
        private sealed class SwitchCaseListing {
            /// <summary>The row rendered for a null model, which ObjectListView asks for while recycling.</summary>
            internal static readonly SwitchCaseListing Empty = new SwitchCaseListing();

            private readonly ClientScriptSwitchCase arm;
            private readonly bool present;

            private SwitchCaseListing() {
            }

            internal SwitchCaseListing(int block, int position, ClientScriptSwitchCase arm, List<int> switchedAt,
                string target) {
                Block = block;
                Position = position;
                this.arm = arm;
                SwitchedAt = string.Join(", ", switchedAt);
                Target = target;
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

            /// <summary>Where the arm lands, resolved against each instruction that selects it.</summary>
            internal object? Target { get; }
        }
    }
}
