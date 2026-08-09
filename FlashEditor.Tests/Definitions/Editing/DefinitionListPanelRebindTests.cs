using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Runtime.ExceptionServices;
using System.Threading;
using System.Windows.Forms;
using BrightIdeasSoftware;
using FlashEditor.Cache;
using FlashEditor.Definitions;
using FlashEditor.Definitions.Editing;
using FlashEditor.Definitions.Entities;
using FlashEditor.Utils;
using Xunit;
using Xunit.Abstractions;
using FlashEditor.IO;

namespace FlashEditor.Tests.Definitions.Editing {
    /// <summary>
    ///     Swapping one <see cref="DefinitionListPanel"/> from one descriptor to another, with rows
    ///     loaded and a sort active.
    /// </summary>
    /// <remarks>
    ///     The transition the entity page's type selector performs, and the one that crashed in
    ///     ordinary use: sort a column, move the selector, and the load that follows threw
    ///     <c>This column reads a ObjectDefinition but was handed a ItemDefinition</c> out of a
    ///     <c>RunWorkerCompleted</c> handler. ObjectListView keeps the sorted-by column as an object
    ///     reference that <c>Columns.Clear()</c> does not clear, so the next <c>SetObjects</c>
    ///     re-sorted the new family's rows through the old family's aspect getter.
    ///     <para>
    ///     <b>The guard that threw is correct and stays.</b> A row of the wrong type reaching an
    ///     aspect getter can only mean a descriptor was wired to a row type it does not produce, and
    ///     blanking those cells would hide this permanently. So the assertion here is that no
    ///     wrong-type row ever reaches a getter, not that the getter tolerates one.
    ///     </para>
    ///     <para>
    ///     Driven over a synthetic two-index cache rather than a real one. The panel loads on a
    ///     <see cref="System.ComponentModel.BackgroundWorker"/> and publishes from
    ///     <c>RunWorkerCompleted</c>, and the whole question is what happens between the swap and
    ///     that publication - so the worker has to run for real. Eight items and eight objects are
    ///     enough: the sort is what triggers it, and a sort of eight rows makes the same comparisons
    ///     a sort of twenty thousand does.
    ///     </para>
    ///     <para>
    ///     The panel is exercised through its own public surface, on an STA thread with a message
    ///     pump, because <c>ObjectListView</c> is UI-thread only. The grid is reached through
    ///     <c>Controls</c> rather than by reflection - the panel adds it there - and is used only to
    ///     do what a header click does and to read back what the sort state became.
    ///     </para>
    /// </remarks>
    public sealed class DefinitionListPanelRebindTests : IDisposable {
        private const int SectorSize = 520;   // RSSector.SIZE is static readonly, unusable as a const

        private readonly ITestOutputHelper _output;
        private readonly string _dir;
        private readonly List<RSFileStore> _stores = new List<RSFileStore>();

        /// <summary>
        ///     What was raised while a message was being dispatched, or null.
        /// </summary>
        /// <remarks>
        ///     The defect surfaces inside <c>RunWorkerCompleted</c>, which runs from the message
        ///     pump rather than from the test's own call stack, so it has to be caught at the WinForms
        ///     boundary and re-thrown here. Left to itself it takes the whole test host down and
        ///     aborts the run, which reports every other test as never having executed.
        /// </remarks>
        private volatile Exception? _dispatchFailure;

        /// <summary>Builds an empty temp directory for this test's synthetic cache.</summary>
        /// <param name="output">Where the measured numbers are reported.</param>
        public DefinitionListPanelRebindTests(ITestOutputHelper output) {
            _output = output;
            DebugUtil.LOG_LEVEL = DebugUtil.LOG_DETAIL.NONE;
            _dir = Path.Combine(Path.GetTempPath(), "fe-rebind-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_dir);
        }

        /// <summary>Releases the dat2 handle so the temp directory can be removed.</summary>
        public void Dispose() {
            foreach (RSFileStore store in _stores)
                store.Dispose();

            if (Directory.Exists(_dir))
                Directory.Delete(_dir, true);
        }

        /// <summary>
        ///     Switching family with a sort active loads the new family without a wrong-type row
        ///     reaching a column.
        /// </summary>
        /// <remarks>
        ///     Both halves are needed. The load has to complete - a panel that published nothing
        ///     would pass an exception-only assertion while showing an empty grid - and the sort
        ///     state has to have been let go of, which is the invariant rather than the symptom: a
        ///     <c>PrimarySortColumn</c> that is not one of the grid's own columns is an orphan from a
        ///     descriptor that no longer exists, and it is only a matter of which row lands next
        ///     before it throws.
        /// </remarks>
        [Fact]
        public void SwitchingDescriptorWithASortActiveNeverSortsTheNewRowsThroughTheOldColumns() {
            OnUiThread(form => {
                RSCache cache = CreateCache();
                var panel = new DefinitionListPanel();
                form.Controls.Add(panel);

                FastObjectListView grid = GridOf(panel);

                panel.Bind(cache, new ItemListDescriptor());
                PumpUntilLoaded(panel, "items");
                Assert.All(panel.Rows, row => Assert.IsType<ItemDefinition>(row));
                _output.WriteLine($"items loaded: {panel.Rows.Count} rows, {grid.AllColumns.Count} columns");

                //What a click on the "Name" header does. Any column would do - every one of them is
                //typed to the descriptor's row - but a sort is what has to be active for the crash.
                grid.Sort(grid.AllColumns[1], SortOrder.Ascending);
                Assert.NotNull(grid.PrimarySortColumn);

                panel.Bind(cache, new ObjectListDescriptor());
                PumpUntilLoaded(panel, "objects");

                Assert.NotEmpty(panel.Rows);
                Assert.All(panel.Rows, row => Assert.IsType<ObjectDefinition>(row));

                //Null or live. An orphaned column - one no longer in the grid - is the defect
                //itself, whether or not this particular run of the sort happened to touch it.
                Assert.True(grid.PrimarySortColumn == null || grid.AllColumns.Contains(grid.PrimarySortColumn),
                    "The grid is still sorted by a column that belongs to the previous descriptor.");
                Assert.True(grid.SecondarySortColumn == null || grid.AllColumns.Contains(grid.SecondarySortColumn),
                    "The grid's secondary sort still belongs to the previous descriptor.");

                _output.WriteLine($"objects loaded: {panel.Rows.Count} rows, {grid.AllColumns.Count} columns, " +
                                  $"sorted by {grid.PrimarySortColumn?.Text ?? "<nothing>"}");
            });
        }

        /// <summary>
        ///     Replacing a row after a family switch does not reach the previous family's columns.
        /// </summary>
        /// <remarks>
        ///     The second route to the same orphaned column, and the reason the fix rebuilds the list
        ///     rather than only nulling the two properties. <c>ReplaceRow</c> - which is how the
        ///     entity page's import puts a decoded record on screen - goes through
        ///     <c>ObjectListView.AddObject</c>, and the fast data source re-sorts what it is given
        ///     with the comparer it kept from the last sort, which is a separate copy of the same
        ///     stale reference.
        /// </remarks>
        [Fact]
        public void ReplacingARowAfterAFamilySwitchDoesNotReachThePreviousFamilysColumns() {
            OnUiThread(form => {
                RSCache cache = CreateCache();
                var panel = new DefinitionListPanel();
                form.Controls.Add(panel);

                FastObjectListView grid = GridOf(panel);

                panel.Bind(cache, new ItemListDescriptor());
                PumpUntilLoaded(panel, "items");
                grid.Sort(grid.AllColumns[1], SortOrder.Ascending);

                panel.Bind(cache, new ObjectListDescriptor());
                PumpUntilLoaded(panel, "objects");

                object existing = panel.Rows[0];
                panel.ReplaceRow(existing, new ObjectDefinition { id = 4242, name = "imported" });

                Assert.Equal(panel.Rows.Count, grid.GetItemCount());
                _output.WriteLine($"replaced one of {grid.GetItemCount()} rows after the switch");
            });
        }

        /// <summary>
        ///     A load superseded before it published never puts its rows on screen.
        /// </summary>
        /// <remarks>
        ///     The other way the grid could end up holding one family's rows under another family's
        ///     columns, and it is closed by the panel rather than by the ordering: the previous
        ///     load's worker is cancelled, but cancellation is cooperative, so a worker already past
        ///     its last check still arrives at <c>RunWorkerCompleted</c> with a full result. What
        ///     stops it is the completion handler refusing to publish unless it is still the panel's
        ///     current worker.
        ///     <para>
        ///     The first load is waited for until it has decoded every row and <b>not</b> pumped, so
        ///     its completion is provably queued rather than merely likely to be: swapping without
        ///     waiting at all instead lets the worker take the cancelled path, and the test then
        ///     passes with the guard deleted, which was measured rather than assumed.
        ///     </para>
        /// </remarks>
        [Fact]
        public void ALoadSupersededBeforeItPublishedDoesNotPutItsRowsOnScreen() {
            OnUiThread(form => {
                RSCache cache = CreateCache();
                var panel = new DefinitionListPanel();
                form.Controls.Add(panel);

                var items = new SignallingItemDescriptor(FileIds.Length);
                panel.Bind(cache, items);

                //Waited for, never pumped: the worker runs on the pool, so it finishes without this
                //thread dispatching anything, and its completion is left sitting in the queue.
                Assert.True(items.EveryRowDecoded.Wait(TimeSpan.FromSeconds(10)),
                    "The superseded load never decoded its rows.");
                Thread.Sleep(100);

                panel.Bind(cache, new ObjectListDescriptor());
                PumpUntilLoaded(panel, "objects");

                Assert.NotEmpty(panel.Rows);
                Assert.All(panel.Rows, row => Assert.IsType<ObjectDefinition>(row));
                _output.WriteLine($"superseded load discarded after decoding {FileIds.Length} rows, " +
                                  $"{panel.Rows.Count} object rows published");
            });
        }

        /// <summary>
        ///     The item descriptor, with a signal for when its last row has been decoded.
        /// </summary>
        /// <remarks>
        ///     So the test can tell the difference between a load that was cancelled on the way in
        ///     and one that ran to the end and had its result thrown away at the door. Only the
        ///     second exercises the guard, and a synthetic index this small takes the first unless
        ///     something waits.
        /// </remarks>
        private sealed class SignallingItemDescriptor : DefinitionListDescriptor<ItemDefinition> {
            private readonly ItemListDescriptor _inner = new ItemListDescriptor();
            private readonly int _expected;
            private int _decoded;

            /// <summary>Signals a descriptor that reports when it has decoded every row.</summary>
            /// <param name="expected">How many rows the index holds.</param>
            internal SignallingItemDescriptor(int expected) {
                _expected = expected;
            }

            /// <summary>Set once every row has been decoded.</summary>
            internal ManualResetEventSlim EveryRowDecoded { get; } = new ManualResetEventSlim(false);

            /// <inheritdoc/>
            public override int IndexId => _inner.IndexId;

            /// <inheritdoc/>
            public override string RowNoun => _inner.RowNoun;

            /// <inheritdoc/>
            public override IReadOnlyList<DefinitionColumn> Columns => _inner.Columns;

            /// <inheritdoc/>
            public override ItemDefinition Decode(RSCache cache, DefinitionAddress address, JagStream payload) {
                ItemDefinition row = _inner.Decode(cache, address, payload);

                if (Interlocked.Increment(ref _decoded) == _expected)
                    EveryRowDecoded.Set();

                return row;
            }

            /// <inheritdoc/>
            public override DefinitionAddress AddressOf(ItemDefinition row) {
                return _inner.AddressOf(row);
            }
        }

        /// <summary>The grid the panel docks, which it adds to its own <c>Controls</c>.</summary>
        /// <param name="panel">The panel.</param>
        /// <returns>Its grid.</returns>
        private static FastObjectListView GridOf(DefinitionListPanel panel) {
            return panel.Controls.OfType<FastObjectListView>().Single();
        }

        /// <summary>
        ///     Runs the message pump until the panel has published a load.
        /// </summary>
        /// <remarks>
        ///     The publication is the point being tested, so it is waited for rather than assumed:
        ///     the panel loads on a worker and raises <c>RowsLoaded</c> from
        ///     <c>RunWorkerCompleted</c>, which only runs when messages are dispatched. A load that
        ///     never arrives fails here rather than making the assertions below it vacuous.
        /// </remarks>
        /// <param name="panel">The panel being loaded.</param>
        /// <param name="what">What is being waited for, for the failure message.</param>
        private void PumpUntilLoaded(DefinitionListPanel panel, string what) {
            bool loaded = false;
            void Handler(object? sender, EventArgs e) => loaded = true;

            panel.RowsLoaded += Handler;
            try {
                var clock = Stopwatch.StartNew();
                while (!loaded && _dispatchFailure == null && clock.Elapsed < TimeSpan.FromSeconds(30)) {
                    Application.DoEvents();
                    Thread.Sleep(1);
                }
            }
            finally {
                panel.RowsLoaded -= Handler;
            }

            //Re-thrown rather than asserted on, so the failure carries the stack that produced it -
            //which is the whole evidence for which column was handed which row.
            if (_dispatchFailure != null)
                ExceptionDispatchInfo.Capture(_dispatchFailure).Throw();

            Assert.True(loaded, "The " + what + " load never published its rows.");
        }

        /// <summary>
        ///     A cache holding eight item definitions and eight object definitions.
        /// </summary>
        /// <remarks>
        ///     Synthetic rather than the real 639 cache, because nothing here is about the bytes: the
        ///     records are a bare terminator each, and what is under test is the panel's transition
        ///     between two descriptors. It also keeps the test off the shared dat2 that the rest of
        ///     the suite memory-maps.
        /// </remarks>
        /// <returns>The open cache.</returns>
        private RSCache CreateCache() {
            //Sector 0 is burned: sector id 0 is the end-of-chain marker, so allocation must not
            //hand it out.
            File.WriteAllBytes(Path.Combine(_dir, "main_file_cache.dat2"), new byte[SectorSize]);

            foreach (int indexId in new[] {
                         RSConstants.OBJECTS_DEFINITIONS_INDEX,
                         RSConstants.ITEM_DEFINITIONS_INDEX,
                         RSConstants.META_INDEX
                     })
                File.WriteAllBytes(Path.Combine(_dir, "main_file_cache.idx" + indexId), Array.Empty<byte>());

            var store = new RSFileStore(_dir);
            _stores.Add(store);

            /* The meta index is written for every id up to the highest one used, not only for the
               two that hold anything: RSFileStore refuses a non-contiguous archive id, so writing
               table 16 with nothing at 0..15 fails on the write rather than on the read. The
               fillers declare no groups, which is a shape the real cache has too - index 36 is a
               table declaring zero groups. */
            for (int indexId = 0; indexId <= RSConstants.ITEM_DEFINITIONS_INDEX; indexId++) {
                bool populated = indexId == RSConstants.OBJECTS_DEFINITIONS_INDEX ||
                                 indexId == RSConstants.ITEM_DEFINITIONS_INDEX;

                if (populated) {
                    var archive = new RSArchive();
                    foreach (int fileId in FileIds)
                        archive.PutFile(fileId, new JagStream(new byte[] { 0 }));

                    store.Write(indexId, 0,
                        new RSContainer(indexId, 0, RSConstants.GZIP_COMPRESSION, archive.Encode(), 1).Encode());
                }

                store.Write(RSConstants.META_INDEX, indexId, EncodeReferenceTable(indexId, populated));
            }

            return new RSCache(store);
        }

        /// <summary>The files seeded into group 0 of both indexes.</summary>
        private static readonly int[] FileIds = { 0, 1, 2, 3, 4, 5, 6, 7 };

        /// <summary>The reference table declaring group 0 of one index, or declaring nothing.</summary>
        /// <param name="indexId">The index it describes.</param>
        /// <param name="populated">Whether it declares the seeded group at all.</param>
        /// <returns>The stored container.</returns>
        private static JagStream EncodeReferenceTable(int indexId, bool populated) {
            var table = new RSReferenceTable { format = 6, version = 1, flags = 0 };

            if (populated) {
                var entry = new RSArchiveEntry(0);
                entry.SetVersion(1);
                entry.SetValidFileIds(FileIds);
                entry.SetFileEntries(new SortedDictionary<int, RSFileEntry>(
                    FileIds.ToDictionary(id => id, id => new RSFileEntry(id))));
                table.PutArchiveEntry(0, entry);
            }

            return new RSContainer(RSConstants.META_INDEX, indexId, RSConstants.GZIP_COMPRESSION,
                ReferenceTableCodec.Encode(table), 1).Encode();
        }

        /// <summary>
        ///     Runs an action on a fresh STA thread, and rethrows what it threw.
        /// </summary>
        /// <remarks>
        ///     A real message loop rather than a bare thread with <c>DoEvents</c>, because the panel
        ///     publishes from <c>RunWorkerCompleted</c> and where that runs is the whole point. A
        ///     <c>BackgroundWorker</c> completes on whatever synchronisation context was current when
        ///     it started, and a thread with no loop has none - so the completion lands on a thread
        ///     pool thread, which is not what the application does and is also where nothing can
        ///     catch it: <c>SetObjects</c> then marshals the publication with <c>Control.Invoke</c>
        ///     and re-throws on the pool thread, killing the process instead of failing one test.
        ///     <para>
        ///     Inside the loop, an exception from a dispatched message is taken at the WinForms
        ///     boundary through <c>Application.ThreadException</c> and re-thrown from the pump. The
        ///     window is transparent and off the taskbar: this runs on a machine somebody is using.
        ///     </para>
        /// </remarks>
        /// <param name="action">What to run, given the form to host the panel in.</param>
        private void OnUiThread(Action<Form> action) {
            Exception? failure = null;

            var thread = new Thread(() => {
                void OnThreadException(object sender, ThreadExceptionEventArgs e) =>
                    _dispatchFailure ??= e.Exception;

                //First, before anything creates a control on this thread: WinForms refuses the
                //change once one exists.
                Application.SetUnhandledExceptionMode(UnhandledExceptionMode.CatchException);
                Application.ThreadException += OnThreadException;

                using var form = new Form {
                    Opacity = 0,
                    ShowInTaskbar = false,
                    StartPosition = FormStartPosition.Manual,
                    Location = new Point(-32000, -32000)
                };

                form.Shown += (_, _) => {
                    try {
                        action(form);
                    }
                    catch (Exception ex) {
                        failure = ex;
                    }
                    finally {
                        form.Close();
                    }
                };

                try {
                    Application.Run(form);
                }
                catch (Exception ex) {
                    failure ??= ex;
                }
                finally {
                    Application.ThreadException -= OnThreadException;
                }
            });

            thread.SetApartmentState(ApartmentState.STA);
            thread.Start();
            thread.Join();

            if (failure != null)
                ExceptionDispatchInfo.Capture(failure).Throw();

            //A dispatch failure that arrived after the last pump still fails the test.
            if (_dispatchFailure != null)
                ExceptionDispatchInfo.Capture(_dispatchFailure).Throw();
        }
    }
}
