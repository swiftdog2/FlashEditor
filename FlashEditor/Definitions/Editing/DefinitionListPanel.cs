using BrightIdeasSoftware;
using FlashEditor.Cache;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;
using static FlashEditor.Utils.DebugUtil;
using FlashEditor.IO;

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
        private IDefinitionThumbnailSource? thumbnails;

        /* Held so a rebind can cancel the load it is superseding. The tracks panel deliberately
           keeps no handle on its worker, but that one decodes 1404 rows; this one can be asked for
           42,000, and leaving a superseded sweep running competes with the load that replaced it
           for the cache lock. */
        private BackgroundWorker? worker;

        /* The published rows, kept beside the list because ObjectListView's own collection is
           filtered and re-ordered by the user and a tab measuring the whole index needs what was
           loaded rather than what is currently on screen. */
        private IReadOnlyList<object> rows = Array.Empty<object>();

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
            list.CellClick += OnCellClick;
        }

        /// <summary>
        ///     Where a thumbnail column's pictures come from, or null to draw ids as plain text.
        /// </summary>
        /// <remarks>
        ///     Null by default, so a descriptor that asks for pictures does not break a tab that has
        ///     not supplied a source: the cell falls back to the id it was already showing.
        /// </remarks>
        [Browsable(false)]
        [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
        public IDefinitionThumbnailSource? Thumbnails {
            get => thumbnails;
            set {
                if (ReferenceEquals(thumbnails, value))
                    return;

                if (thumbnails != null)
                    thumbnails.TilesReady -= OnTilesReady;

                thumbnails = value;

                if (thumbnails != null)
                    thumbnails.TilesReady += OnTilesReady;

                list.Invalidate();
            }
        }

        /// <summary>
        ///     Raised when the user activates a cell that names something elsewhere in the cache.
        /// </summary>
        /// <remarks>
        ///     The panel deliberately does not act on it. What following a reference means - select
        ///     a tab, select a row, open a picker - is the form's decision, and a panel that decided
        ///     it would have to know about every tab.
        /// </remarks>
        public event EventHandler<DefinitionCellActivatedEventArgs>? CellActivated;

        private void OnCellClick(object? sender, CellClickEventArgs e) {
            if (e.Model == null || e.Column?.Renderer is not DefinitionCellRenderer hit)
                return;

            DefinitionCellVisual visual = hit.VisualFor(e.Model);

            //A swatch is activatable too, and for the same reason a link is: the cell names
            //something the user wants to reach, and typing six hex digits from memory is the thing
            //this whole layer exists to remove.
            if (visual.Art == DefinitionCellArt.None)
                return;

            CellActivated?.Invoke(this, new DefinitionCellActivatedEventArgs(e.Model, visual));
        }

        /// <summary>
        ///     Repaints when queued tiles land.
        /// </summary>
        /// <remarks>
        ///     <c>Invalidate</c> rather than <c>RefreshObjects</c>: it needs no map from id back to
        ///     row, so a column sort cannot make it refresh the wrong rows, and it costs a repaint
        ///     of what is on screen rather than per-row work over everything that landed.
        /// </remarks>
        private void OnTilesReady(object? sender, EventArgs e) {
            if (IsDisposed || !IsHandleCreated)
                return;

            if (list.InvokeRequired)
                list.BeginInvoke(new Action(list.Invalidate));
            else
                list.Invalidate();
        }

        /// <summary>The row the user has selected, or null when there is none.</summary>
        [Browsable(false)]
        [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
        public object? SelectedRow => list.SelectedObject;

        /// <summary>Every selected row, in the order the grid holds them.</summary>
        /// <remarks>
        ///     Copied out rather than handed over as the control's own collection. Anything that
        ///     walks a selection is about to do work per row, and the grid's collection is only
        ///     valid on the UI thread and only until the selection next moves.
        /// </remarks>
        [Browsable(false)]
        [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
        public IReadOnlyList<object> SelectedRows {
            get {
                var rows = new List<object>(list.SelectedObjects.Count);
                foreach (object row in list.SelectedObjects)
                    rows.Add(row);
                return rows;
            }
        }

        /// <summary>The descriptor currently bound, or null when the panel holds no cache.</summary>
        [Browsable(false)]
        [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
        public IDefinitionListDescriptor? Descriptor => descriptor;

        /// <summary>Says what the panel is doing, in the same line a load reports through.</summary>
        /// <remarks>
        ///     Exposed so a tab that adds its own actions beside the list - an import, an export -
        ///     reports through the same line the load does rather than growing a second status label
        ///     that contradicts it.
        /// </remarks>
        /// <param name="text">What to say.</param>
        public void ReportStatus(string text) {
            status.Text = text ?? string.Empty;
        }

        /// <summary>
        ///     Replaces one row in place, keeping the sort and the selection.
        /// </summary>
        /// <remarks>
        ///     For an action that produces a different object for the same address - an import
        ///     decodes the file it staged, and the decoded record is not the row that was selected.
        ///     Adding and removing would reorder the grid; this does not.
        /// </remarks>
        /// <param name="oldRow">The row on screen.</param>
        /// <param name="newRow">What it becomes.</param>
        public void ReplaceRow(object oldRow, object newRow) {
            if (oldRow == null || newRow == null)
                return;

            list.RemoveObject(oldRow);
            list.AddObject(newRow);
            list.SelectedObject = newRow;
        }

        /// <summary>
        ///     Selects a row and scrolls it into view, for a companion control driving the list.
        /// </summary>
        /// <remarks>
        ///     For a tree, a canvas or a navigation link beside the grid: the user picks a thing
        ///     over there and the grid has to follow. Setting <c>SelectedObject</c> alone selects
        ///     without scrolling, so a selection five thousand rows down looks like nothing
        ///     happened.
        ///     <para>
        ///     A row the grid does not hold clears the selection rather than throwing, because the
        ///     user's filter box can legitimately have hidden it.
        ///     </para>
        /// </remarks>
        /// <param name="row">The row to select, or null to clear the selection.</param>
        public void SelectRow(object? row) {
            if (row == null) {
                list.SelectedObjects = null;
                return;
            }

            list.SelectObject(row, true);
            list.EnsureModelVisible(row);
        }

        /// <summary>Turns alternating row shading on or off, for the form's View menu.</summary>
        /// <param name="enabled">Whether alternate rows are shaded.</param>
        /// <param name="colour">The shade.</param>
        public void SetAlternatingRows(bool enabled, Color colour) {
            list.UseAlternatingBackColors = enabled;
            list.AlternateRowBackColor = colour;
            list.Refresh();
        }

        /// <summary>Raised when <see cref="SelectedRow"/> changes.</summary>
        /// <remarks>
        ///     For a tab that shows something alongside the list - a model, a preview, a detail pane.
        ///     The panel deliberately owns no such pane itself, because what belongs beside the list
        ///     differs per index while everything above does not.
        /// </remarks>
        public event EventHandler? SelectedRowChanged;

        /// <summary>
        ///     The rows on display, which is empty until a load completes.
        /// </summary>
        /// <remarks>
        ///     Exposed so a tab can state something about the whole index rather than only about the
        ///     selection - the Client Scripts tab measures how much of its opcode stream it can name
        ///     from these rows instead of printing a figure someone wrote down, which would be a
        ///     figure about one cache pinned into a tab that opens either.
        /// </remarks>
        [Browsable(false)]
        [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
        public IReadOnlyList<object> Rows => rows;

        /// <summary>Raised on the UI thread once a load has published its rows.</summary>
        /// <remarks>
        ///     Not raised for a cancelled or faulted load, so a handler can take <see cref="Rows"/>
        ///     as complete. Binding a null cache clears the rows without raising it.
        /// </remarks>
        public event EventHandler? RowsLoaded;

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

            //Emptied before the columns are replaced, never after: a column built for the new
            //descriptor must never be able to see a row produced by the old one.
            list.ClearObjects();
            rows = Array.Empty<object>();

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
        ///     Frees tiles the thumbnail cache evicted, before anything in this frame is drawn.
        /// </summary>
        /// <remarks>
        ///     This is the other half of the cache's disposal contract and it has to run here rather
        ///     than at eviction. The producer thread evicts from a background decode and can pick a
        ///     bitmap the UI thread is currently inside <c>DrawImage</c> on, which is a use-after-free
        ///     with no exception to catch. Draining at the top of a paint means every tile freed was
        ///     last drawn in a previous frame, so nothing can still be reading it.
        ///     <para>
        ///     On the panel rather than the renderer: a grid can carry several renderers over one
        ///     cache, and draining per renderer would free the same queue several times per frame.
        ///     </para>
        /// </remarks>
        /// <param name="e">The paint data.</param>
        protected override void OnPaint(PaintEventArgs e) {
            thumbnails?.DrainRetired();
            base.OnPaint(e);
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

        /// <summary>
        ///     Replaces the grid's columns with the bound descriptor's, and drops the sort with them.
        /// </summary>
        /// <remarks>
        ///     <b>The sort has to go first, and this is the whole of the entity page's type-switch
        ///     crash.</b> ObjectListView remembers what the user sorted by as an object reference -
        ///     <c>PrimarySortColumn</c>, and <c>SecondarySortColumn</c> beside it - and clearing
        ///     <c>Columns</c> does not clear either. They go on pointing at an <c>OLVColumn</c> built
        ///     for the <i>previous</i> descriptor, whose aspect getter is closed over a
        ///     <see cref="DefinitionColumn"/> typed to that descriptor's row. Nothing happens while
        ///     the grid is empty. The next <c>SetObjects</c> - the load completing for the family the
        ///     user switched <i>to</i> - calls <c>BuildList</c>, which re-sorts through that orphaned
        ///     column, and every row of the new type is handed to a getter typed to the old one:
        ///     <c>This column reads a ObjectDefinition but was handed a ItemDefinition</c>, thrown out
        ///     of a <c>RunWorkerCompleted</c> handler where nothing catches it.
        ///     <para>
        ///     It only bites once a header has been clicked, because an unsorted grid leaves
        ///     <c>PrimarySortColumn</c> null and <c>BuildList</c> then sorts nothing - which is why
        ///     the crash reads as intermittent and as sort-dependent, and it is both.
        ///     </para>
        ///     <para>
        ///     <see cref="ObjectListView.Unsort"/> rather than assigning the properties, because it
        ///     also rebuilds the list, which is what makes the data source forget the comparer it
        ///     keeps for <c>AddObject</c>. <see cref="ReplaceRow"/> goes through <c>AddObject</c>, so
        ///     an import over a row after a type switch reaches the same orphaned column by a second
        ///     route.
        ///     </para>
        /// </remarks>
        private void BuildColumns() {
            list.SecondarySortColumn = null;
            list.SecondarySortOrder = SortOrder.None;
            list.Unsort();

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

                //Only where the descriptor asked for one, so every column that does not want art
                //keeps the grid's own renderer and nothing changes for it.
                if (column.Visual != null)
                    olv.Renderer = new DefinitionCellRenderer(column, () => thumbnails);

                list.AllColumns.Add(olv);
                list.Columns.Add(olv);
            }

            /* Derived from the font and the art, never written as a pixel count - the same rule
               that sizes this panel's progress bar from Font.Height. -1 hands the measurement back
               to the grid, which is what every descriptor without art wants. */
            bool hasArt = false;
            foreach (DefinitionColumn column in descriptor.Columns) {
                if (column.Visual == null)
                    continue;
                hasArt = true;
                break;
            }

            list.RowHeight = hasArt
                ? Math.Max(list.Font.Height + 4, DefinitionCellRenderer.ArtSide + 4)
                : -1;

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
                rows = result.Rows;
                list.SetObjects(result.Rows);
                progress.Value = 100;
                status.Text = result.Describe(openDescriptor.RowNoun);
                RowsLoaded?.Invoke(this, EventArgs.Empty);
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

            /* A descriptor that says its rows are fully described by their address never has a group
               opened for it. Index 7 is the case: 63,607 groups of one file, every column of which
               the reference table already states, so reading them would inflate every model in the
               cache to print a list of ids. The payload handed over is empty rather than null so the
               decode signature stays the same for both kinds of descriptor. */
            if (!openDescriptor.ReadsPayload) {
                JagStream nothing = new JagStream(Array.Empty<byte>());

                foreach (DefinitionAddress address in addresses) {
                    if (loader.CancellationPending) {
                        args.Cancel = true;
                        return result;
                    }

                    try {
                        result.Rows.Add(openDescriptor.Decode(open, address, nothing));
                    }
                    catch (Exception ex) {
                        result.Failed++;
                        Debug($"Index {openDescriptor.IndexId} {address} failed to list: {ex.Message}");
                    }

                    done++;
                    Report(loader, done, addresses.Count, percentile, openDescriptor);
                }

                return result;
            }

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
        ///     Writes a row back after something other than a cell editor changed it.
        /// </summary>
        /// <remarks>
        ///     For a companion surface that edits the same records - a canvas being dragged, a
        ///     colour picker, a property pane. It is deliberately the <b>same</b> path a cell edit
        ///     takes, including the comparison that writes nothing when the bytes have not changed,
        ///     because a second write path would be a second place for that rule to be forgotten.
        /// </remarks>
        /// <param name="row">The row that was changed.</param>
        public void CommitRow(object? row) {
            CommitEdit(row);
            if (row != null)
                list.RefreshObject(row);
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
