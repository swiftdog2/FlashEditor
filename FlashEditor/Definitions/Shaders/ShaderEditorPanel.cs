using System;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Windows.Forms;
using FlashEditor.Cache;
using FlashEditor.Definitions.Editing;
using static FlashEditor.Utils.DebugUtil;

namespace FlashEditor.Definitions.Shaders {
    /// <summary>
    ///     The Graphics Shaders tab: index 31, the client's water and underwater programs.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///     Two groups, one per rendering backend, seven programs each and the same seven names in
    ///     both. <c>gl</c> is plaintext - ARB assembly and GLSL - and is genuinely editable here.
    ///     <c>dx</c> is compiled Direct3D 9 bytecode and can only be replaced: producing new
    ///     bytecode needs an external HLSL compiler, there is no in-tree path to one, and none
    ///     should be invented. The tab shows a hex view for those rather than pretending.
    ///     </para>
    ///     <para>
    ///     <b>The line endings are the reason this tab is harder than it looks.</b> Four of the ARB
    ///     programs use bare LF and carry no CRLF at all, <c>transparent_water</c> and both GLSL
    ///     files use CRLF, and only one of the seven ends with a newline. A text box shows and
    ///     returns CRLF whatever it is given, so the naive implementation rewrites four files just
    ///     by displaying and saving them - and the result compiles, reads correctly and no longer
    ///     matches the bytes nobody edited. <see cref="ShaderTextDocument"/> records the convention
    ///     at decode and replays it on the way out, and refuses to edit any file it cannot reproduce
    ///     byte for byte. Saving an untouched file stages nothing and says so, which is the check
    ///     that proves the mechanism rather than merely claiming it.
    ///     </para>
    ///     <para>
    ///     <b>This tab does not compile, link or run anything.</b> It cannot tell you whether an
    ///     edited shader is valid - only the driver can - so a staged edit is a claim about bytes
    ///     and nothing more.
    ///     </para>
    /// </remarks>
    public sealed class ShaderEditorPanel : UserControl {
        /* Consolas 9 on every child. The form puts Consolas 12 on the tab control and everything
           under it inherits, which is half again what these grids are laid out for. */
        private static readonly Font GridFont = new Font("Consolas", 9F);

        private const string NoCacheText = "No cache loaded";
        private const string NoSelectionText = "Select a shader to see its source";

        private readonly Label header = new Label {
            AutoSize = true,
            Dock = DockStyle.Top,
            Font = GridFont,
            Text = NoCacheText
        };

        private readonly Label notice = new Label {
            AutoSize = true,
            Dock = DockStyle.Top,
            Font = GridFont,
            Text = "A group is a rendering backend and a file is one named shader program. The names are not in " +
                   "the cache - each is recovered by hashing the name the client asks for and requiring an exact " +
                   "match - and \"gl\"/\"transparent_water\" is literally the address the client uses." +
                   Environment.NewLine +
                   "Line endings here are not uniform: some files use bare LF, some use CRLF, and only one ends " +
                   "with a newline. What was read is what is written back, and a file that cannot be reproduced " +
                   "byte for byte is shown but not editable. Saving without changing anything stages nothing." +
                   Environment.NewLine +
                   "Nothing here compiles or runs a shader. The editor checks bytes, not validity."
        };

        private readonly DefinitionListPanel shaders = new DefinitionListPanel {
            //Bound with a null cache before a cache arrives so the grid keeps its headings, and the
            //panel's own default would then claim no cache is loaded.
            EmptyMessage = NoCacheText
        };

        private readonly Label editorNote = new Label {
            AutoSize = true,
            Dock = DockStyle.Top,
            Font = GridFont,
            Text = NoSelectionText
        };

        /* One control for both views. A hex dump is text, so switching between a source editor and a
           read-only dump is a ReadOnly flag and a different string rather than a second control that
           has to be shown, hidden and kept in step. */
        private readonly TextBox editor = new TextBox {
            AcceptsReturn = true,
            AcceptsTab = true,
            Dock = DockStyle.Fill,
            Font = GridFont,
            Multiline = true,
            ScrollBars = ScrollBars.Both,
            WordWrap = false
        };

        private readonly DetailFieldGrid fields = new DetailFieldGrid();

        /* No splitter states a minimum size. Setting one re-checks the current distance against it,
           and a container is still at its 150x100 default when a field initialiser runs. */
        private readonly SplitContainer listAndEditor = new SplitContainer {
            Dock = DockStyle.Fill,
            Orientation = Orientation.Vertical
        };

        private readonly SplitContainer editorAndFields = new SplitContainer {
            Dock = DockStyle.Fill,
            Orientation = Orientation.Horizontal
        };

        private readonly FlowLayoutPanel actions = new FlowLayoutPanel {
            AutoSize = true,
            Dock = DockStyle.Bottom,
            FlowDirection = FlowDirection.LeftToRight,
            WrapContents = true
        };

        private readonly Button save = new Button {
            AutoSize = true,
            Enabled = false,
            Font = GridFont,
            Text = "Stage edited source"
        };

        private readonly Button revert = new Button {
            AutoSize = true,
            Enabled = false,
            Font = GridFont,
            Text = "Revert to stored"
        };

        private readonly CachePayloadTransferStrip transfer = new CachePayloadTransferStrip {
            BatchCaption = "Export every shader..."
        };

        private RSCache? cache;
        private ShaderFileListing? selected;
        private bool splitterPlaced;

        /// <summary>Creates the panel with its grid headings already in place.</summary>
        public ShaderEditorPanel() {
            Dock = DockStyle.Fill;

            BuildLayout();

            shaders.SelectedRowChanged += (_, _) => ShowShader(shaders.SelectedRow as ShaderFileListing);
            shaders.RowsLoaded += (_, _) => DescribeIndex();
            save.Click += (_, _) => StageEdit();
            revert.Click += (_, _) => ShowShader(selected);
            transfer.Imported += (_, _) => Reload();
            transfer.BatchProvider = () => shaders.Rows.OfType<ShaderFileListing>()
                .Select(TargetFor)
                .ToList();
        }

        /// <summary>
        ///     Points the tab at a cache, or clears it when given none.
        /// </summary>
        /// <remarks>
        ///     <c>Editor.LoadEditorTab</c> calls this on every visit to the tab, so a rebind of the
        ///     cache already on display has to be a no-op or an in-progress edit is thrown away each
        ///     time the user clicks away and back.
        /// </remarks>
        /// <param name="newCache">The open cache, or null to unbind.</param>
        public void Bind(RSCache? newCache) {
            if (ReferenceEquals(newCache, cache))
                return;

            cache = newCache;
            transfer.Bind(newCache);
            ShowShader(null);
            Reload();
        }

        /// <summary>Places the splitters once the layout pass has given them a real size.</summary>
        /// <param name="levent">The event data.</param>
        protected override void OnLayout(LayoutEventArgs levent) {
            base.OnLayout(levent);
            PlaceSplitters();
            WrapNotices();
        }

        /// <summary>
        ///     Lets the explanatory labels wrap instead of running off the right edge.
        /// </summary>
        /// <remarks>
        ///     An <c>AutoSize</c> label docked to an edge grows sideways and is clipped by its
        ///     container; it only wraps once <see cref="Control.MaximumSize"/> states a width. These
        ///     carry the sentences saying what the tab will not do, and one cut off half way through
        ///     is worse than one never written.
        /// </remarks>
        private void WrapNotices() {
            Wrap(header, ClientSize.Width);
            Wrap(notice, ClientSize.Width);
            Wrap(editorNote, editorAndFields.Panel1.ClientSize.Width);
        }

        private static void Wrap(Label label, int width) {
            if (width > 0 && label.MaximumSize.Width != width)
                label.MaximumSize = new Size(width, 0);
        }

        /// <summary>
        ///     Divides the panel proportionally, once, when it first has a size worth dividing.
        /// </summary>
        /// <remarks>
        ///     A <see cref="SplitContainer"/> defaults to a splitter distance of 50 <i>pixels</i>, so
        ///     the distance has to be stated - and stating it in a designer would make it one more
        ///     literal the form scales by its DPI factor.
        /// </remarks>
        private void PlaceSplitters() {
            if (splitterPlaced || listAndEditor.Width < 400 || editorAndFields.Height < 200)
                return;

            //Set before the assignments, not after: changing a splitter distance lays the panel out
            //again, and this is called from that layout.
            splitterPlaced = true;

            try {
                //Two fifths to the list. Fourteen rows need little height and a shader is the thing
                //being read, so the source pane gets the larger share.
                listAndEditor.SplitterDistance =
                    Math.Max(listAndEditor.Panel1MinSize, listAndEditor.Width * 2 / 5);
                editorAndFields.SplitterDistance =
                    Math.Max(editorAndFields.Panel1MinSize, editorAndFields.Height * 2 / 3);
            }
            catch (InvalidOperationException ex) {
                splitterPlaced = false;
                Debug("Shader tab splitters not placed yet: " + ex.Message, LOG_DETAIL.ADVANCED);
            }
        }

        private void BuildLayout() {
            actions.Controls.Add(save);
            actions.Controls.Add(revert);

            editorAndFields.Panel1.Controls.Add(editor);
            editorAndFields.Panel1.Controls.Add(actions);
            editorAndFields.Panel1.Controls.Add(editorNote);
            editorAndFields.Panel2.Controls.Add(fields);

            listAndEditor.Panel1.Controls.Add(shaders);
            listAndEditor.Panel2.Controls.Add(editorAndFields);

            //Docking resolves from the end of the Controls collection backwards, so the strips have
            //to be added after the filled splitter and in inside-out order among themselves.
            Controls.Add(listAndEditor);
            Controls.Add(transfer);
            Controls.Add(notice);
            Controls.Add(header);

            //Bound before any cache arrives so the grid has headings from the start.
            shaders.Bind(null, new ShaderListDescriptor());
        }

        /// <summary>
        ///     Reloads the list against the bound cache.
        /// </summary>
        /// <remarks>
        ///     A fresh descriptor every time, because <c>DefinitionListPanel.Bind</c> treats the same
        ///     cache and descriptor pair as the same thing to show and would leave the previous rows
        ///     on screen. That is what makes this usable after a staged edit, where one row's size
        ///     and line-ending profile may both have changed.
        /// </remarks>
        private void Reload() {
            if (cache == null) {
                header.Text = NoCacheText;
                shaders.Bind(null, new ShaderListDescriptor());
                return;
            }

            shaders.Bind(cache, new ShaderListDescriptor());
        }

        /// <summary>
        ///     Says what this cache's index 31 holds, once its rows are in.
        /// </summary>
        /// <remarks>
        ///     Counted from the rows rather than written down, and the line-ending census is part of
        ///     it because that is the property of this index a user has to know before editing.
        /// </remarks>
        private void DescribeIndex() {
            var rows = shaders.Rows.OfType<ShaderFileListing>().ToList();
            if (cache == null || rows.Count == 0)
                return;

            int named = rows.Count(row => row.ClientAddress.Length > 0);

            header.Text = "Index 31 - " + rows.Count + " shader(s) across " +
                          rows.Select(row => row.Address.GroupId).Distinct().Count() + " backend(s), " +
                          named + " addressable by name. " +
                          string.Join(", ", rows.GroupBy(row => row.Document.EndingText)
                              .OrderBy(family => family.Key)
                              .Select(family => family.Count() + " " + family.Key)) +
                          "; " + rows.Count(row => row.Document.EndsWithNewline) +
                          " end with a newline.";
        }

        /// <summary>Shows the selected shader as source or as a hex dump, and offers it for transfer.</summary>
        /// <param name="listing">The selected row, or null.</param>
        private void ShowShader(ShaderFileListing? listing) {
            selected = listing;
            fields.ShowFields(listing);
            transfer.Show(listing == null ? null : TargetFor(listing));

            if (listing == null) {
                editor.Clear();
                editor.ReadOnly = true;
                editorNote.Text = cache == null ? NoCacheText : NoSelectionText;
                save.Enabled = false;
                revert.Enabled = false;
                return;
            }

            bool editable = listing.IsEditableText && cache != null;

            editor.ReadOnly = !editable;
            editor.Text = listing.Document.IsText ? listing.Document.DisplayText : HexDump(listing.Stored);

            save.Enabled = editable;
            revert.Enabled = editable;

            editorNote.Text = listing.Summary + Environment.NewLine +
                              (listing.Document.EditRefusal ??
                               "Editable. The " + listing.Document.EndingText + " line endings this file uses are" +
                               " what will be written back, whatever the text box shows, and no trailing newline is" +
                               " added. Staging an unchanged file writes nothing.");
        }

        /// <summary>
        ///     Encodes the edited text in the stored file's own convention and stages it.
        /// </summary>
        /// <remarks>
        ///     Through <see cref="CachePayloadTransfer.Stage"/> rather than straight to
        ///     <c>RSCache.WriteFile</c>, so an in-tab edit takes the same unchanged-payload check a
        ///     file import does. That check is what makes "save without editing writes nothing" true
        ///     rather than merely intended.
        /// </remarks>
        private void StageEdit() {
            if (cache == null || selected == null || !selected.IsEditableText)
                return;

            try {
                byte[] encoded = selected.Document.Encode(editor.Text);
                CachePayloadTransfer.Outcome outcome =
                    CachePayloadTransfer.Stage(cache, TargetFor(selected), encoded, "the editor");

                transfer.Report(outcome.Message);

                if (outcome.Changed)
                    Reload();
            }
            catch (Exception ex) {
                //Reported rather than thrown: an exception out of a button handler takes the form down.
                transfer.Report("Stage failed: " + ex.Message);
                Debug("Shader edit failed: " + ex);
            }
        }

        /// <summary>
        ///     Describes one row for the transfer strip.
        /// </summary>
        /// <remarks>
        ///     The relative path is <c>&lt;backend&gt;/&lt;shader&gt;</c>, so exporting the whole
        ///     index writes the two backends into their own folders rather than colliding the seven
        ///     identically-named programs onto each other.
        /// </remarks>
        /// <param name="listing">The row.</param>
        /// <returns>The transfer target.</returns>
        private static CachePayloadTarget TargetFor(ShaderFileListing listing) {
            string backend = listing.Backend ?? ("group" + listing.Address.GroupId);
            string shader = listing.Shader ?? ("file" + listing.Address.FileId);

            //Extension from what the payload is, not from the backend. The dx group is compiled
            //bytecode whatever it is named, and an exported .txt of it would be a lie about the file.
            string extension = listing.Shape.Kind switch {
                ShaderProgramKind.ArbAssembly => ".arb",
                ShaderProgramKind.Glsl => ".glsl",
                ShaderProgramKind.Direct3DBytecode => ".fxo",
                _ => ".bin"
            };

            return new CachePayloadTarget(RSConstants.GRAPHICS_SHADERS, listing.Address, listing.Stored,
                shader + extension, backend + "/" + shader + extension,
                "Shader (*" + extension + ")|*" + extension + "|All files (*.*)|*.*",
                backend + "/" + shader);
        }

        /// <summary>
        ///     A classic offset, hex, ASCII dump.
        /// </summary>
        /// <remarks>
        ///     For the compiled backend, which cannot be edited and would otherwise be shown as
        ///     mojibake or as nothing at all. Read-only by construction - the dump is not what
        ///     <see cref="StageEdit"/> encodes, and the save button is disabled for any payload that
        ///     reaches this.
        /// </remarks>
        /// <param name="bytes">The payload.</param>
        /// <returns>The dump.</returns>
        private static string HexDump(byte[] bytes) {
            var text = new StringBuilder(bytes.Length * 4);

            for (int offset = 0; offset < bytes.Length; offset += 16) {
                int run = Math.Min(16, bytes.Length - offset);

                text.Append(offset.ToString("X8", CultureInfo.InvariantCulture)).Append("  ");

                for (int i = 0; i < 16; i++) {
                    text.Append(i < run ? bytes[offset + i].ToString("X2", CultureInfo.InvariantCulture) : "  ");
                    text.Append(i == 7 ? "  " : " ");
                }

                text.Append(' ');
                for (int i = 0; i < run; i++) {
                    byte value = bytes[offset + i];
                    text.Append(value >= 0x20 && value <= 0x7E ? (char) value : '.');
                }

                text.Append(Environment.NewLine);
            }

            return text.ToString();
        }
    }
}
