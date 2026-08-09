using BrightIdeasSoftware;
using FlashEditor.Cache;
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
using System.Text;
using System.Windows.Forms;
using static FlashEditor.Utils.DebugUtil;
using FlashEditor.IO;

namespace FlashEditor.Definitions.Compression {
    /// <summary>
    ///     The Huffman tab: index 10's one file as 256 editable bit lengths, beside a live chat
    ///     compressor.
    /// </summary>
    /// <remarks>
    ///     There is nothing to list here - the index holds one group holding one file - so this is
    ///     not a <c>DefinitionListPanel</c> tab. The grid's rows are the <i>records within</i> that
    ///     file, which no descriptor can enumerate because they are not cache files.
    ///     <para>
    ///     <b>The codec box is the point.</b> A bit length on its own says nothing about whether the
    ///     table works; a message that compresses and expands back to itself does. It is also the
    ///     only place in this editor where an edit's effect can be seen immediately, since changing
    ///     one length re-derives codewords across the whole table.
    ///     </para>
    /// </remarks>
    public sealed class HuffmanEditorPanel : UserControl {
        /* Consolas 9 on every child. The form puts Consolas 12 on the tab control and everything
           under it inherits, which is half again what these controls are laid out for. */
        private static readonly Font PanelFont = new Font("Consolas", 9F);

        private readonly FastObjectListView entries = new FastObjectListView {
            CellEditActivation = ObjectListView.CellEditActivateMode.DoubleClick,
            Dock = DockStyle.Fill,
            Font = PanelFont,
            FullRowSelect = true,
            GridLines = true,
            ShowGroups = false,
            UseFiltering = true,
            View = View.Details
        };

        private readonly Label header = new Label {
            AutoSize = true,
            Dock = DockStyle.Top,
            Font = PanelFont,
            Text = NoCacheText
        };

        private readonly Label status = new Label {
            AutoSize = true,
            Dock = DockStyle.Bottom,
            Font = PanelFont,
            Text = NoCacheText
        };

        private readonly TextBox message = new TextBox {
            Dock = DockStyle.Fill,
            Font = PanelFont,
            Multiline = true,
            ScrollBars = ScrollBars.Vertical,
            Text = "the quick brown fox jumps over the lazy dog"
        };

        private readonly TextBox result = new TextBox {
            Dock = DockStyle.Fill,
            Font = PanelFont,
            Multiline = true,
            ReadOnly = true,
            ScrollBars = ScrollBars.Vertical
        };

        private readonly SplitContainer tableAndCodec = new SplitContainer {
            Dock = DockStyle.Fill,
            Orientation = Orientation.Vertical
        };

        private readonly SplitContainer inputAndOutput = new SplitContainer {
            Dock = DockStyle.Fill,
            Orientation = Orientation.Horizontal
        };

        private const string NoCacheText = "No cache loaded";

        /* Held so an edit can refresh every row rather than the one that changed: a single bit
           length re-derives the codewords of an unpredictable number of the other 255. */
        private readonly List<HuffmanEntryRow> rows = new List<HuffmanEntryRow>();

        private RSCache? cache;
        private HuffmanTable? table;
        private int groupId = -1;
        private int fileId = -1;
        private bool splitterPlaced;

        /* Why the last cell edit was refused, or null. The cell editor's setter cannot throw - an
           exception out of an ObjectListView callback takes the form down - so a rejected length is
           recorded here and reported when the edit is committed. */
        private string? pendingEditError;

        /// <summary>Creates the panel.</summary>
        public HuffmanEditorPanel() {
            Dock = DockStyle.Fill;
            BuildLayout();

            entries.CellEditFinished += (_, e) => CommitEdit(e.RowObject);
            message.TextChanged += (_, _) => ShowCodecResult();
        }

        /// <summary>
        ///     Points the panel at a cache, or clears it when given none.
        /// </summary>
        /// <remarks>
        ///     <c>Editor.LoadEditorTab</c> calls this on every visit to the tab, so a rebind of the
        ///     cache already on display has to be a no-op - it would otherwise throw away an
        ///     uncommitted edit. Identity is the right test because opening a cache builds a new
        ///     <see cref="RSCache"/>.
        /// </remarks>
        /// <param name="newCache">The open cache, or null to unbind.</param>
        public void Bind(RSCache? newCache) {
            if (ReferenceEquals(newCache, cache))
                return;

            cache = newCache;
            table = null;
            groupId = -1;
            fileId = -1;
            pendingEditError = null;
            rows.Clear();
            entries.ClearObjects();

            if (newCache == null) {
                header.Text = NoCacheText;
                status.Text = NoCacheText;
                result.Text = string.Empty;
                return;
            }

            HuffmanTable loaded;
            try {
                (groupId, fileId) = HuffmanTable.Locate(newCache);
                loaded = HuffmanTable.Load(newCache);
            }
            catch (Exception ex) {
                //Reported rather than thrown: a cache with no readable index 10 must cost this tab
                //and nothing else.
                header.Text = "Index " + RSConstants.HUFFMAN_INDEX + " could not be read: " + ex.Message;
                status.Text = header.Text;
                Debug("Huffman tab load failed: " + ex);
                return;
            }

            table = loaded;
            for (int value = 0; value < loaded.Entries; value++)
                rows.Add(new HuffmanEntryRow(loaded, value));
            entries.SetObjects(rows);

            header.Text = Describe(loaded);
            status.Text = "Loaded group " + groupId + " file " + fileId;
            ShowCodecResult();
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
        ///     A <see cref="SplitContainer"/> defaults to a splitter distance of 50 <i>pixels</i>,
        ///     and stating one in a field initialiser throws because the container is still at its
        ///     150x100 default. A fraction of the measured size is the same division at any font or
        ///     DPI, and it is applied once so a dragged splitter stays where it was put.
        /// </remarks>
        private void PlaceSplitters() {
            if (splitterPlaced || tableAndCodec.Width < 200 || inputAndOutput.Height < 120)
                return;

            //Set before the assignments, not after: changing a splitter distance lays the panel out
            //again, and this is called from that layout.
            splitterPlaced = true;

            try {
                tableAndCodec.SplitterDistance =
                    Math.Max(tableAndCodec.Panel1MinSize, tableAndCodec.Width * 3 / 5);
                inputAndOutput.SplitterDistance =
                    Math.Max(inputAndOutput.Panel1MinSize, inputAndOutput.Height / 3);
            }
            catch (InvalidOperationException ex) {
                //Left for the next layout rather than clamped. A clamped distance sticks, and the
                //user would see a collapsed pane on a window that later has room for both.
                splitterPlaced = false;
                Debug("Huffman tab splitter not placed yet: " + ex.Message, LOG_DETAIL.ADVANCED);
            }
        }

        private void BuildLayout() {
            BuildColumns();

            inputAndOutput.Panel1.Controls.Add(message);
            inputAndOutput.Panel2.Controls.Add(result);

            tableAndCodec.Panel1.Controls.Add(entries);
            tableAndCodec.Panel2.Controls.Add(inputAndOutput);

            //Docking resolves from the end of the Controls collection backwards, so the strips have
            //to be added after the filled splitter or the splitter claims the whole panel.
            Controls.Add(tableAndCodec);
            Controls.Add(header);
            Controls.Add(status);
        }

        private void BuildColumns() {
            AddColumn("Value", 70, row => row.Value);
            AddColumn("Byte", 70, row => row.Hex);
            AddColumn("Char", 60, row => row.Character);
            AddEditableBitLengthColumn();
            AddColumn("Codeword", 280, row => row.Codeword);
            AddColumn("Encodable", 90, row => row.Encodable);
        }

        /// <summary>
        ///     Adds one read-only column, reading its value through a delegate rather than an aspect
        ///     name.
        /// </summary>
        /// <remarks>
        ///     A name looked up by reflection blanks the column when the property is renamed, where
        ///     a delegate stops compiling.
        /// </remarks>
        /// <param name="heading">The column heading.</param>
        /// <param name="width">The column width, in the grid's own pinned font.</param>
        /// <param name="read">Reads the displayed value off a row.</param>
        private void AddColumn(string heading, int width, Func<HuffmanEntryRow, object?> read) {
            var column = new OLVColumn(heading, null) {
                Width = width,
                Groupable = false,
                IsEditable = false,
                AspectGetter = row => read((HuffmanEntryRow) row)
            };

            entries.AllColumns.Add(column);
            entries.Columns.Add(column);
        }

        /// <summary>
        ///     Adds the one editable column.
        /// </summary>
        /// <remarks>
        ///     The setter goes through <see cref="HuffmanTable.SetBitLength"/>, which rebuilds the
        ///     derived state and puts the old byte back if the new set of lengths is unusable. That
        ///     is why the edit is committed here rather than by writing into a copied field: a
        ///     length is only valid in the context of the other 255.
        /// </remarks>
        private void AddEditableBitLengthColumn() {
            var column = new OLVColumn("Bits", null) {
                Width = 60,
                Groupable = false,
                IsEditable = true,
                AspectGetter = row => ((HuffmanEntryRow) row).BitLength,
                AspectPutter = (row, value) => ApplyBitLength(row, value)
            };

            entries.AllColumns.Add(column);
            entries.Columns.Add(column);
        }

        /// <summary>
        ///     Applies an edited bit length, recording rather than throwing when it is refused.
        /// </summary>
        /// <remarks>
        ///     The conversion is here because the cell editor decides the type it hands back - a
        ///     <c>NumericUpDown</c> yields a <c>decimal</c>, a text box a <c>string</c>.
        /// </remarks>
        /// <param name="row">The row being edited.</param>
        /// <param name="value">Whatever the cell editor produced.</param>
        private void ApplyBitLength(object row, object? value) {
            pendingEditError = null;

            try {
                ((HuffmanEntryRow) row).BitLength = Convert.ToInt32(value, CultureInfo.InvariantCulture);
            }
            catch (Exception ex) {
                pendingEditError = ex.Message;
            }
        }

        /// <summary>
        ///     Writes the edited table back, unless it re-encodes to the bytes already stored.
        /// </summary>
        /// <remarks>
        ///     The comparison is against what the cache holds right now, so a length edited back to
        ///     its original value writes nothing. Re-encoding rewrites the stored bytes and so the
        ///     archive CRC, which drags the reference-table entry of every archive packed alongside
        ///     it into the save.
        ///     <para>
        ///     The whole grid is refreshed rather than the edited row, because one length re-derives
        ///     codewords across the table.
        ///     </para>
        /// </remarks>
        /// <param name="row">The edited row.</param>
        private void CommitEdit(object? row) {
            if (row == null || cache == null || table == null)
                return;

            if (pendingEditError != null) {
                status.Text = "Edit refused: " + pendingEditError;
                pendingEditError = null;
                entries.RefreshObjects(rows);
                return;
            }

            try {
                byte[] encoded = table.Encode().ToArray();
                byte[] stored = cache.ReadFileBytes(RSConstants.HUFFMAN_INDEX, groupId, fileId);

                entries.RefreshObjects(rows);
                header.Text = Describe(table);
                ShowCodecResult();

                if (encoded.AsSpan().SequenceEqual(stored)) {
                    status.Text = "No change to the chat table";
                    return;
                }

                cache.WriteFile(RSConstants.HUFFMAN_INDEX, groupId, fileId, new JagStream(encoded));
                status.Text = "Staged the chat table at group " + groupId + " file " + fileId;
            }
            catch (Exception ex) {
                //Reported rather than thrown: this runs from a cell editor, and an exception out of
                //an ObjectListView event handler takes the form down.
                status.Text = "Edit failed: " + ex.Message;
                Debug("Huffman tab edit failed: " + ex);
            }
        }

        /// <summary>
        ///     Runs the message through the table both ways and shows what happened.
        /// </summary>
        /// <remarks>
        ///     Both directions, because a compressor that agrees with a broken decompressor proves
        ///     nothing on its own. Showing the packet the client would put on the wire - the smart
        ///     character count and then the packed bits - is what makes the two comparable to a
        ///     capture.
        /// </remarks>
        private void ShowCodecResult() {
            if (table == null) {
                result.Text = string.Empty;
                return;
            }

            try {
                byte[] packet = table.EncodeChatMessage(message.Text);
                string back = table.DecodeChatMessage(packet);

                var text = new StringBuilder();
                text.AppendLine(message.Text.Length + " characters in, " + packet.Length +
                                " packet bytes out (smart length then packed bits)");
                text.AppendLine(Hex(packet));
                text.AppendLine();
                text.AppendLine(back == message.Text
                    ? "Expands back to the same text."
                    : "Expands back to something else: " + back);
                result.Text = text.ToString();
            }
            catch (Exception ex) {
                //A zeroed bit length makes its byte unsendable, which is the failure this box
                //exists to surface rather than to hide.
                result.Text = "Cannot compress this message: " + ex.Message;
            }
        }

        /// <summary>A one-line summary of the table's shape.</summary>
        /// <param name="loaded">The decoded table.</param>
        /// <returns>The summary.</returns>
        private static string Describe(HuffmanTable loaded) {
            int shortest = int.MaxValue;
            int longest = 0;
            int unencodable = 0;

            for (int value = 0; value < loaded.Entries; value++) {
                int bits = loaded.BitLengthOf(value);
                if (bits <= 0) {
                    unencodable++;
                    continue;
                }

                shortest = Math.Min(shortest, bits);
                longest = Math.Max(longest, bits);
            }

            string range = longest == 0 ? "no codewords" : shortest + " to " + longest + " bits";
            return loaded.Entries + " records, " + range + ", " + loaded.TreeSize +
                   " decode-tree nodes, " + unencodable + " byte values with no codeword";
        }

        private static string Hex(byte[] data) {
            var text = new StringBuilder(data.Length * 3);
            foreach (byte value in data) {
                if (text.Length > 0)
                    text.Append(' ');
                text.Append(value.ToString("X2"));
            }
            return text.ToString();
        }
    }
}
