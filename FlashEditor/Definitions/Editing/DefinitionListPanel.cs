using BrightIdeasSoftware;
using FlashEditor.cache;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;
using static FlashEditor.Utils.DebugUtil;

namespace FlashEditor.Definitions.Editing {
    /// <summary>
    ///     One cache index presented as a sortable list, loaded on a background worker and edited in
    ///     place.
    /// </summary>
    /// <remarks>
    ///     This exists so an index editor is a descriptor rather than another arm in
    ///     <c>Editor.LoadEditorTab</c>. The four tabs written before it each re-implement the same
    ///     worker, the same progress reporting, the same list population and the same edit commit,
    ///     and around twenty-five more indexes are still to come.
    ///     <para>
    ///     <b>Threading.</b> <see cref="ObjectListView"/> is UI-thread only, so the worker decodes
    ///     into a plain list and the completion handler is the only thing that touches the control.
    ///     The item, sprite, NPC and object tabs call <c>SetObjects</c> from inside <c>DoWork</c>,
    ///     which is cross-thread control access that happens to work; that is not copied here.
    ///     </para>
    ///     <para>
    ///     <b>Progress.</b> Reported on one-percent boundaries. <c>ReportProgress</c> marshals to the
    ///     UI thread on every call, so one post per row would flood the message pump with 42,000
    ///     posts on index 3 alone and make the load slower than the decode it is reporting.
    ///     </para>
    /// </remarks>
    public sealed class DefinitionListPanel : UserControl {
        /* Consolas 9 rather than the tab control's Consolas 12, which every child would otherwise
           inherit. The descriptor states column widths in pixels, and those only mean anything
           against a font this panel controls. */
        private readonly FastObjectListView list = new FastObjectListView {
            Dock = DockStyle.Fill,
            Font = new Font("Consolas", 9F),
            FullRowSelect = true,
            GridLines = true,
            ShowGroups = false,
            UseFiltering = true,
            View = View.Details
        };

        private readonly ProgressBar progress = new ProgressBar { Dock = DockStyle.Bottom };

        //AutoSize rather than a literal height: the form scales by font ratio, so a fixed height is
        //multiplied at runtime and clips the text it was measured for.
        private readonly Label status = new Label {
            AutoSize = true,
            Dock = DockStyle.Bottom,
            Font = new Font("Consolas", 9F),
            TextAlign = ContentAlignment.MiddleLeft,
            Text = "No cache loaded"
        };

        /* The bound pair is the panel's identity, and a rebind of the same pair has to be a no-op:
           Editor.LoadEditorTab calls Bind on every visit to the tab, and reloading 42,000 rows
           because someone clicked away and back would also throw away the sort and the selection. */
        private RSCache? cache;
        private IDefinitionListDescriptor? descriptor;

        /* Held so a rebind can cancel the load it is superseding. The tracks panel deliberately
           keeps no handle on its worker, but that one decodes 1404 rows; this one can be asked for
           42,000, and leaving a superseded sweep running competes with the load that replaced it
           for the cache lock. */
        private BackgroundWorker? worker;

        /// <summary>Creates an unbound panel.</summary>
        public DefinitionListPanel() {
            Dock = DockStyle.Fill;
            progress.Height = Math.Max(10, Font.Height);

            //Docking resolves from the end of the Controls collection backwards, so the bottom
            //strips have to be added after the filled list or the list claims the whole panel.
            Controls.Add(list);
            Controls.Add(status);
            Controls.Add(progress);

            list.SelectedIndexChanged += (_, _) => SelectedRowChanged?.Invoke(this, EventArgs.Empty);
            list.CellEditFinished += (_, e) => CommitEdit(e.RowObject);
        }

        /// <summary>The row the user has selected, or null when there is none.</summary>
        [Browsable(false)]
        [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
        public object? SelectedRow => list.SelectedObject;

        /// <summary>Raised when <see cref="SelectedRow"/> changes.</summary>
        /// <remarks>
        ///     For a tab that shows something alongside the list - a model, a preview, a detail pane.
        ///     The panel deliberately owns no such pane itself, because what belongs beside the list
        ///     differs per index while everything above does not.
        /// </remarks>
        public event EventHandler? SelectedRowChanged;

        /// <summary>
        ///     What the status line says when the panel holds no rows.
        /// </summary>
        /// <remarks>
        ///     Defaults to the literal truth for the common case, a panel with no cache behind it.
        ///     It is settable because a detail pane is deliberately bound with a null cache to keep
        ///     its column headings while nothing is selected, and "No cache loaded" is then false -
        ///     the cache is open and the master list beside it is full of rows from that cache.
        ///     A status line that contradicts what the user can see is worse than no status line.
        /// </remarks>
        [Browsable(false)]
        [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
        public string EmptyMessage { get; set; } = "No cache loaded";

        /// <summary>
        ///     Points the panel at a cache and a descriptor, and starts loading.
        /// </summary>
        /// <remarks>
        ///     Idempotent for the same pair, so a tab revisit costs nothing. Passing a null cache
        ///     unbinds: it cancels any load in flight and empties the list, which is what a cache
        ///     being closed underneath the panel requires.
        /// </remarks>
        /// <param name="newCache">The open cache, or null to unbind.</param>
        /// <param name="newDescriptor">The index to present. Required unless <paramref name="newCache"/> is null.</param>
        public void Bind(RSCache? newCache, IDefinitionListDescriptor? newDescriptor = null) {
            if (newCache != null && newDescriptor == null)
                throw new ArgumentNullException(nameof(newDescriptor), "A bound cache needs a descriptor to present it with.");

            if (ReferenceEquals(newCache, cache) && ReferenceEquals(newDescriptor, descriptor))
                return;

            //Cancelled rather than left running. The completion handler also refuses to publish a
            //superseded result, because cancellation is cooperative and the worker may already be
            //past its last check.
            worker?.CancelAsync();
            worker = null;

            bool columnsChanged = !ReferenceEquals(newDescriptor, descriptor);
            cache = newCache;
            descriptor = newDescriptor;

            list.ClearObjects();

            if (columnsChanged)
                BuildColumns();

            if (cache == null || descriptor == null) {
                status.Text = EmptyMessage;
                progress.Value = 0;
                return;
            }

            StartLoad(cache, descriptor);
        }

        /// <summary>
        ///     Keeps the progress bar in proportion to the font the form scaled everything else by.
        /// </summary>
        /// <remarks>
        ///     A <see cref="ProgressBar"/> cannot auto-size, so this is the one strip whose height
        ///     has to be stated. Deriving it from the font rather than writing a pixel count is what
        ///     keeps it from being multiplied by the font ratio into something out of scale.
        /// </remarks>
        /// <param name="e">The event data.</param>
        protected override void OnFontChanged(EventArgs e) {
            base.OnFontChanged(e);
            progress.Height = Math.Max(10, Font.Height);
        }

        private void BuildColumns() {
            list.AllColumns.Clear();
            list.Columns.Clear();

            if (descriptor == null)
                return;

            foreach (DefinitionColumn column in descriptor.Columns) {
                var olv = new OLVColumn(column.Header, null) {
                    Width = column.Width,
                    Groupable = false,
                    //Editable only where the descriptor supplies a setter, and only at all where it
                    //can re-encode the row. A column with no way back into the cache must not offer
                    //an edit that silently goes nowhere.
                    IsEditable = descriptor.IsEditable && column.IsEditable,
                    AspectGetter = row => column.Read(row)
                };

                if (column.Write != null)
                    olv.AspectPutter = (row, value) => column.Write(row, value);

                list.AllColumns.Add(olv);
                list.Columns.Add(olv);
            }

            list.CellEditActivation = descriptor.IsEditable
                ? ObjectListView.CellEditActivateMode.DoubleClick
                : ObjectListView.CellEditActivateMode.None;
        }

        private void StartLoad(RSCache open, IDefinitionListDescriptor openDescriptor) {
            progress.Value = 0;
            status.Text = "Loading " + RSConstants.GetIndexName(openDescriptor.IndexId) + " (index " + openDescriptor.IndexId + ")";

            var loader = new BackgroundWorker { WorkerReportsProgress = true, WorkerSupportsCancellation = true };
            worker = loader;

            loader.ProgressChanged += (_, e) => {
                if (!ReferenceEquals(worker, loader))
                    return;
                progress.Value = Math.Clamp(e.ProgressPercentage, 0, 100);
                status.Text = e.UserState?.ToString() ?? status.Text;
            };

            loader.DoWork += (_, e) => e.Result = DecodeRows(open, openDescriptor, loader, e);

            loader.RunWorkerCompleted += (_, e) => {
                //A superseded load is discarded whole. Cancellation is cooperative, so a worker that
                //was already past its last check still arrives here with a full result.
                if (!ReferenceEquals(worker, loader))
                    return;

                worker = null;

                if (e.Cancelled) {
                    status.Text = "Load cancelled";
                    return;
                }

                if (e.Error != null) {
                    status.Text = "Failed to load index " + openDescriptor.IndexId + ": " + e.Error.Message;
                    Debug("DefinitionListPanel load failed: " + e.Error);
                    return;
                }

                //DoWork assigns Result on every path that is not cancelled or faulted
                var result = (LoadResult) e.Result!;
                list.SetObjects(result.Rows);
                progress.Value = 100;
                status.Text = result.Describe(openDescriptor.RowNoun);
            };

            loader.RunWorkerAsync();
        }

        /// <summary>
        ///     Decodes every row the descriptor names, one group at a time.
        /// </summary>
        /// <remarks>
        ///     Grouped rather than read file by file. <see cref="RSCache.ReadFile"/> releases the
        ///     container as soon as it has handed back one file, so a per-file walk re-reads and
        ///     re-inflates each group once per file it holds - 42,256 group decodes over index 3
        ///     where this does 1078, for the same bytes.
        ///     <para>
        ///     Takes the cache and the descriptor as arguments rather than reading the fields, so a
        ///     rebind part way through cannot make one sweep read half of one cache and half of
        ///     another.
        ///     </para>
        /// </remarks>
        private static LoadResult DecodeRows(RSCache open, IDefinitionListDescriptor openDescriptor,
            BackgroundWorker loader, DoWorkEventArgs args) {
            List<DefinitionAddress> addresses = openDescriptor.Enumerate(open).ToList();
            var result = new LoadResult(addresses.Count);

            if (addresses.Count == 0)
                return result;

            int done = 0;
            int percentile = Math.Max(1, addresses.Count / 100);

            foreach (IGrouping<int, DefinitionAddress> group in addresses.GroupBy(address => address.GroupId)) {
                if (loader.CancellationPending) {
                    args.Cancel = true;
                    return result;
                }

                IReadOnlyDictionary<int, JagStream> files;
                try {
                    files = open.ReadGroup(openDescriptor.IndexId, group.Key);
                }
                catch (Exception ex) {
                    //A group that will not open costs its rows, not the tab. Encrypted map squares
                    //with no published key reach this, and so does a store closed underneath a load
                    //that a rebind has already cancelled.
                    Debug($"Index {openDescriptor.IndexId} group {group.Key} could not be read: {ex.Message}");
                    int lost = group.Count();
                    result.Failed += lost;
                    done += lost;
                    Report(loader, done, addresses.Count, percentile, openDescriptor);
                    continue;
                }

                foreach (DefinitionAddress address in group) {
                    try {
                        if (files.TryGetValue(address.FileId, out JagStream? payload))
                            result.Rows.Add(openDescriptor.Decode(open, address, payload));
                        else
                            result.Missing++;
                    }
                    catch (Exception ex) {
                        result.Failed++;
                        Debug($"Index {openDescriptor.IndexId} {address} failed to decode: {ex.Message}");
                    }

                    done++;
                    Report(loader, done, addresses.Count, percentile, openDescriptor);
                }
            }

            return result;
        }

        private static void Report(BackgroundWorker loader, int done, int total, int percentile,
            IDefinitionListDescriptor openDescriptor) {
            if (done % percentile != 0 && done != total)
                return;

            loader.ReportProgress(done * 100 / total,
                $"Loaded {done}/{total} {openDescriptor.RowNoun}s ({done * 100 / total}%)");
        }

        /// <summary>What one load produced, and what it could not.</summary>
        /// <remarks>
        ///     The two failure counts are separate because they mean different things. A missing file
        ///     is a reference table declaring something the payload does not carry; a failed one
        ///     decoded badly or would not open at all. Folding them together would let a decoder
        ///     regression hide inside a number that already had a benign reason to be non-zero.
        /// </remarks>
        private sealed class LoadResult {
            internal LoadResult(int capacity) {
                Rows = new List<object>(capacity);
            }

            internal List<object> Rows { get; }

            /// <summary>Files the reference table declared that the group payload did not hold.</summary>
            internal int Missing { get; set; }

            /// <summary>Rows that threw on the way in.</summary>
            internal int Failed { get; set; }

            internal string Describe(string rowNoun) {
                string text = $"{Rows.Count:N0} {rowNoun}s";
                if (Missing > 0)
                    text += $", {Missing:N0} missing";
                if (Failed > 0)
                    text += $", {Failed:N0} failed";
                return text;
            }
        }

        /// <summary>
        ///     Writes an edited row back, unless re-encoding it produces the bytes already stored.
        /// </summary>
        /// <remarks>
        ///     The comparison is against what the cache holds right now rather than against a copy
        ///     taken when the edit began, so a cell edited back to its original value writes nothing.
        ///     That matters more here than it looks: re-encoding rewrites the stored bytes and so the
        ///     archive CRC, which drags the reference-table entry of every archive packed alongside
        ///     it into the save.
        /// </remarks>
        private void CommitEdit(object? row) {
            if (row == null || cache == null || descriptor == null || !descriptor.IsEditable)
                return;

            try {
                DefinitionAddress address = descriptor.AddressOf(row);
                byte[] encoded = descriptor.Encode(row).ToArray();
                byte[] stored = cache.ReadFileBytes(descriptor.IndexId, address.GroupId, address.FileId);

                if (encoded.AsSpan().SequenceEqual(stored)) {
                    status.Text = "No change at " + address;
                    return;
                }

                cache.WriteFile(descriptor.IndexId, address.GroupId, address.FileId, new JagStream(encoded));
                list.RefreshObject(row);
                status.Text = "Staged " + descriptor.RowNoun + " at " + address;
            }
            catch (Exception ex) {
                //Reported rather than thrown: this runs from a cell editor, and an exception out of
                //an ObjectListView event handler takes the form down.
                status.Text = "Edit failed: " + ex.Message;
                Debug("DefinitionListPanel edit failed: " + ex);
            }
        }
    }
}
