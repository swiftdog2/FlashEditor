using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Drawing;
using System.IO;
using System.Windows.Forms;
using FlashEditor.Cache;
using static FlashEditor.Utils.DebugUtil;

namespace FlashEditor.Definitions.Editing {
    /// <summary>
    ///     The extract and import buttons, for any tab that has stored bytes to hand.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///     Before this the editor could not write an arbitrary payload to disk or read one back at
    ///     all. The four export paths that did exist - sprites as PNG, models as OBJ, tracks as
    ///     MIDI, loading sprites as JPEG - each write a <i>rendering</i> of a decoded record, which
    ///     is no use for an index whose payload is a compiled DLL or a shader with no codec. Two
    ///     whole indexes had no user interface for exactly that reason.
    ///     </para>
    ///     <para>
    ///     The strip owns the dialogs and the reporting; <see cref="CachePayloadTransfer"/> owns the
    ///     rules. A host binds a cache once and then hands over the selected
    ///     <see cref="CachePayloadTarget"/> whenever the selection moves.
    ///     </para>
    /// </remarks>
    public sealed class CachePayloadTransferStrip : FlowLayoutPanel {
        /* Consolas 9, because the form puts Consolas 12 on the tab control and every child inherits
           it, which is half again what these strips are laid out for. */
        private static readonly Font StripFont = new Font("Consolas", 9F);

        private readonly Button export = new Button {
            AutoSize = true,
            Enabled = false,
            Font = StripFont,
            Text = "Export stored bytes..."
        };

        private readonly Button exportAll = new Button {
            AutoSize = true,
            Enabled = false,
            Font = StripFont,
            Text = "Export all...",
            Visible = false
        };

        private readonly Button import = new Button {
            AutoSize = true,
            Enabled = false,
            Font = StripFont,
            Text = "Replace from file..."
        };

        private readonly Label status = new Label {
            AutoSize = true,
            Font = StripFont,
            TextAlign = ContentAlignment.MiddleLeft,
            Text = string.Empty
        };

        private RSCache? cache;
        private CachePayloadTarget? target;
        private Func<IReadOnlyList<CachePayloadTarget>>? batch;

        /// <summary>Creates the strip with nothing selected.</summary>
        public CachePayloadTransferStrip() {
            AutoSize = true;
            Dock = DockStyle.Bottom;
            FlowDirection = FlowDirection.LeftToRight;
            WrapContents = true;

            Controls.Add(export);
            Controls.Add(exportAll);
            Controls.Add(import);
            Controls.Add(status);

            export.Click += (_, _) => DoExport();
            exportAll.Click += (_, _) => DoExportAll();
            import.Click += (_, _) => DoImport();
        }

        /// <summary>Raised after an import has staged a change, so the host can reload its rows.</summary>
        public event EventHandler? Imported;

        /// <summary>Points the strip at a cache, or clears it.</summary>
        /// <param name="openCache">The open cache, or null.</param>
        public void Bind(RSCache? openCache) {
            cache = openCache;
            Show(null);
        }

        /// <summary>
        ///     Every payload the tab could export at once, or null for no batch button.
        /// </summary>
        /// <remarks>
        ///     A delegate rather than a list, because the set changes with the cache and with any
        ///     staged import, and a list captured at bind time would export what the tab held when it
        ///     opened. Worth having at all because a whole index is often the useful unit: index 30's
        ///     thirty-six libraries exported together rebuild the tree the client extracts.
        /// </remarks>
        [Browsable(false)]
        [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
        public Func<IReadOnlyList<CachePayloadTarget>>? BatchProvider {
            get => batch;
            set {
                batch = value;
                exportAll.Visible = value != null;
                exportAll.Enabled = value != null && cache != null;
            }
        }

        /// <summary>
        ///     What the batch export is called, so a tab can say what it is exporting.
        /// </summary>
        [Browsable(false)]
        [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
        public string BatchCaption {
            get => exportAll.Text;
            set => exportAll.Text = value;
        }

        /// <summary>
        ///     Offers this file for transfer, or nothing when the selection is empty.
        /// </summary>
        /// <remarks>
        ///     A target that refuses import leaves the button disabled and puts the reason on the
        ///     status line rather than silently greying out, because a disabled button with no
        ///     explanation reads as a broken tab. Index 31's <c>dx</c> group is the case: compiled
        ///     Direct3D bytecode can be replaced from a file but never edited here, and the tab has
        ///     to say which.
        /// </remarks>
        /// <param name="selected">The selected file, or null.</param>
        public void Show(CachePayloadTarget? selected) {
            target = selected;

            export.Enabled = selected != null;
            import.Enabled = selected != null && selected.CanImport && cache != null;
            exportAll.Enabled = batch != null && cache != null;

            if (selected?.ImportRefusal != null)
                status.Text = selected.ImportRefusal;
            else if (selected == null)
                status.Text = string.Empty;
        }

        /// <summary>Says what the strip last did.</summary>
        /// <param name="text">What to say.</param>
        public void Report(string text) {
            status.Text = text ?? string.Empty;
        }

        private void DoExport() {
            if (target == null)
                return;

            using var dialog = new SaveFileDialog {
                Filter = target.Filter,
                FileName = target.FileName
            };

            if (dialog.ShowDialog(this) != DialogResult.OK)
                return;

            Run(() => CachePayloadTransfer.Export(target, dialog.FileName), "Export");
        }

        private void DoExportAll() {
            if (batch == null)
                return;

            using var dialog = new FolderBrowserDialog {
                Description = "Choose a folder to write the whole index into",
                UseDescriptionForTitle = true
            };

            if (dialog.ShowDialog(this) != DialogResult.OK)
                return;

            IReadOnlyList<CachePayloadTarget> all = batch();
            Run(() => CachePayloadTransfer.ExportAll(all, dialog.SelectedPath), "Export");
        }

        private void DoImport() {
            if (cache == null || target == null)
                return;

            using var dialog = new OpenFileDialog { Filter = target.Filter };

            if (dialog.ShowDialog(this) != DialogResult.OK)
                return;

            RSCache open = cache;
            CachePayloadTarget selected = target;

            if (Run(() => CachePayloadTransfer.Import(open, selected, dialog.FileName), "Replace"))
                Imported?.Invoke(this, EventArgs.Empty);
        }

        /// <summary>
        ///     Runs one transfer and reports it, whatever it does.
        /// </summary>
        /// <remarks>
        ///     Every failure is reported rather than thrown. This runs from a button handler, and an
        ///     exception out of one takes the form down - which on a file dialog is the ordinary case
        ///     rather than the exotic one, since a read-only directory and a file held by another
        ///     process are both normal.
        /// </remarks>
        /// <param name="transfer">The transfer to run.</param>
        /// <param name="verb">What to call it when it fails.</param>
        /// <returns>Whether it changed anything.</returns>
        private bool Run(Func<CachePayloadTransfer.Outcome> transfer, string verb) {
            try {
                CachePayloadTransfer.Outcome outcome = transfer();
                status.Text = outcome.Message;
                return outcome.Changed;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or NotSupportedException) {
                status.Text = verb + " failed: " + ex.Message;
                Debug("Cache payload " + verb.ToLowerInvariant() + " failed: " + ex);
                return false;
            }
        }
    }
}
