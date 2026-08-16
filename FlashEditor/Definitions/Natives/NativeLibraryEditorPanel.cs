using System;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
using System.Linq;
using System.Windows.Forms;
using FlashEditor.Cache;
using FlashEditor.Definitions.Editing;
using FlashEditor.UI;
using static FlashEditor.Utils.DebugUtil;

namespace FlashEditor.Definitions.Natives {
    /// <summary>
    ///     The Native Libraries tab: index 30, the client's own compiled binaries.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///     <b>There is nothing to decode and that is the point.</b> Each group holds one file - named
    ///     <c>""</c>, not after the library - whose bytes are a PE, an ELF or a Mach-O image that the
    ///     client writes straight to disk and loads (<c>Class35.java:92-102</c> then
    ///     <c>Signlink.java:554-561</c>). It is what makes OpenGL and DirectX mode work. So the tab
    ///     is an extract and replace surface with classification beside it, and it exists at all only
    ///     because <see cref="CachePayloadTransfer"/> now does raw bytes in both directions.
    ///     </para>
    ///     <para>
    ///     <b>The name and the header are shown separately on purpose.</b> The name is a claim the
    ///     cache makes - it is the whole address, since the client hashes
    ///     <c>"&lt;os&gt;/&lt;arch&gt;/&lt;lib&gt;&lt;ext&gt;"</c> - and the header is what the binary
    ///     is. Deriving either from the other would hide the one thing about this index worth
    ///     reporting.
    ///     </para>
    ///     <para>
    ///     <b>This tab does not run, load or verify anything.</b> It cannot tell you whether a
    ///     library works, only what shape it is. A replaced binary is written into the cache exactly
    ///     as supplied and is the client's problem from there.
    ///     </para>
    /// </remarks>
    public sealed class NativeLibraryEditorPanel : UserControl {
        /* Consolas 9 on every child. The form puts Consolas 12 on the tab control and everything
           under it inherits, which is half again what these grids are laid out for. */
        private static readonly Font GridFont = new Font("Consolas", 9F);

        private const string NoCacheText = "No cache loaded";
        private const string NoSelectionText = "Select a library to see what it is";

        private readonly Label header = new Label {
            AutoSize = true,
            Dock = DockStyle.Top,
            Font = GridFont,
            Text = NoCacheText
        };

        /// <summary>
        ///     What this tab will not do, behind an (i) rather than docked across the page.
        /// </summary>
        /// <remarks>
        ///     Static prose: it says nothing about the loaded cache, so nothing rewrites it and it
        ///     was pure chrome after the first read. The two statements that <i>are</i> measured -
        ///     the index census in <see cref="header"/> and the name anomalies in
        ///     <see cref="anomalyNotice"/> - stay docked, because a figure nobody sees is a figure
        ///     nobody checks.
        /// </remarks>
        private const string TabNotice =
            "A group here is one compiled binary and its name is the whole address - the client builds " +
            "\"<os>/<arch>/<library><extension>\" and hashes it, and the file inside is named \"\". " +
            "The names are not in the cache: every one was recovered by hashing a candidate and requiring " +
            "an exact match, so a group with no name is one nothing has matched rather than one without.\n\n" +
            "Format, architecture and word width are read from the payload's own MZ, ELF or Mach-O header, " +
            "never from the name. The Agrees column compares the two.\n\n" +
            "This tab does not load or verify a library. It says what shape a file is and nothing about " +
            "whether it runs.";

        //A strip rather than the glyphs docked on their own, so the two notes share one row.
        private readonly FlowLayoutPanel notices = new FlowLayoutPanel {
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            Dock = DockStyle.Top,
            FlowDirection = FlowDirection.LeftToRight,
            WrapContents = false
        };

        private readonly Label anomalyNotice = new Label {
            AutoSize = true,
            Dock = DockStyle.Top,
            Font = GridFont,
            Text = string.Empty,
            Visible = false
        };

        private readonly DefinitionListPanel libraries = new DefinitionListPanel {
            //Bound with a null cache before a cache arrives so the grid keeps its headings, and the
            //panel's own default would then claim no cache is loaded.
            EmptyMessage = NoCacheText
        };

        private readonly Label detailNote = new Label {
            AutoSize = true,
            Dock = DockStyle.Top,
            Font = GridFont,
            Text = NoSelectionText
        };

        private readonly DetailFieldGrid fields = new DetailFieldGrid();

        /* No splitter states a minimum size. Setting one re-checks the current distance against it,
           and a container is still at its 150x100 default when a field initialiser runs. */
        private readonly SplitContainer listAndDetail = new SplitContainer {
            Dock = DockStyle.Fill,
            Orientation = Orientation.Vertical
        };

        private readonly CachePayloadTransferStrip transfer = new CachePayloadTransferStrip {
            BatchCaption = "Export every library..."
        };

        /// <summary>What replacing a library costs, behind a (!) beside the button that does it.</summary>
        private const string ReplaceCost =
            "Replace stores the file you pick byte for byte - there is no transcode, so what the client " +
            "extracts is your file. It rewrites the group's CRC, its whirlpool digest and the " +
            "reference-table entry of every archive packed beside it, and stages the change; nothing " +
            "reaches disk until the cache is saved.\n\n" +
            "A replacement whose container format differs from the one already stored is refused - a .so " +
            "dropped onto a windows/ group would be accepted by the cache and would fail at the client, " +
            "where nothing here would report it.";

        private RSCache? cache;
        private bool splitterPlaced;

        /// <summary>Creates the panel with its grid headings already in place.</summary>
        public NativeLibraryEditorPanel() {
            Dock = DockStyle.Fill;

            BuildLayout();

            libraries.SelectedRowChanged += (_, _) => ShowLibrary(libraries.SelectedRow as NativeLibraryListing);
            libraries.RowsLoaded += (_, _) => DescribeIndex();
            transfer.Imported += (_, _) => Reload();
            transfer.BatchProvider = () => libraries.Rows.OfType<NativeLibraryListing>()
                .Select(TargetFor)
                .ToList();
        }

        /// <summary>
        ///     Points the tab at a cache, or clears it when given none.
        /// </summary>
        /// <remarks>
        ///     <c>Editor.LoadEditorTab</c> calls this on every visit to the tab, so a rebind of the
        ///     cache already on display has to be a no-op or the selection and every decoded payload
        ///     are thrown away each time.
        /// </remarks>
        /// <param name="newCache">The open cache, or null to unbind.</param>
        public void Bind(RSCache? newCache) {
            if (ReferenceEquals(newCache, cache))
                return;

            cache = newCache;
            transfer.Bind(newCache);
            ShowLibrary(null);
            Reload();
        }

        /// <summary>Places the splitter once the layout pass has given it a real size.</summary>
        /// <param name="levent">The event data.</param>
        protected override void OnLayout(LayoutEventArgs levent) {
            base.OnLayout(levent);
            PlaceSplitter();
            WrapNotices();
        }

        /// <summary>
        ///     Lets the explanatory labels wrap instead of running off the right edge.
        /// </summary>
        /// <remarks>
        ///     An <c>AutoSize</c> label docked to an edge grows sideways and is clipped by its
        ///     container; it only wraps once <see cref="Control.MaximumSize"/> states a width. These
        ///     labels carry the sentences that say what the tab cannot do, and one cut off half way
        ///     through is worse than one never written.
        /// </remarks>
        private void WrapNotices() {
            Wrap(header, ClientSize.Width);
            Wrap(anomalyNotice, ClientSize.Width);
            Wrap(detailNote, listAndDetail.Panel2.ClientSize.Width);
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
        private void PlaceSplitter() {
            if (splitterPlaced || listAndDetail.Width < 400)
                return;

            //Set before the assignment, not after: changing a splitter distance lays the panel out
            //again, and this is called from that layout.
            splitterPlaced = true;

            try {
                //Two thirds to the list. It carries eleven columns and the detail pane carries two.
                listAndDetail.SplitterDistance =
                    Math.Max(listAndDetail.Panel1MinSize, listAndDetail.Width * 2 / 3);
            }
            catch (InvalidOperationException ex) {
                splitterPlaced = false;
                Debug("Native libraries tab splitter not placed yet: " + ex.Message, LOG_DETAIL.ADVANCED);
            }
        }

        private void BuildLayout() {
            listAndDetail.Panel1.Controls.Add(libraries);
            listAndDetail.Panel2.Controls.Add(fields);
            listAndDetail.Panel2.Controls.Add(detailNote);

            /* Behind an (i) and a (!) rather than docked as two paragraphs, which is the 18.4 rule:
               a permanent block of prose is read once and then becomes chrome. The cost note goes on
               the transfer strip so it sits beside the Replace button it is about, and it is a Cost
               rather than a Limitation because it is the only one here about a pending action. */
            notices.Controls.Add(InfoAffordance.For(libraries, InfoKind.Limitation, TabNotice));
            transfer.AddNotice(new InfoAffordance {
                Describes = transfer,
                Kind = InfoKind.Cost,
                Caption = "What replacing a library costs",
                Body = ReplaceCost
            });

            //Docking resolves from the end of the Controls collection backwards, so the strips have
            //to be added after the filled splitter and in inside-out order among themselves.
            Controls.Add(listAndDetail);
            Controls.Add(transfer);
            Controls.Add(anomalyNotice);
            Controls.Add(notices);
            Controls.Add(header);

            //Bound before any cache arrives so the grid has headings from the start.
            libraries.Bind(null, new NativeLibraryListDescriptor());
        }

        /// <summary>
        ///     Reloads the list against the bound cache.
        /// </summary>
        /// <remarks>
        ///     A fresh descriptor every time, because <c>DefinitionListPanel.Bind</c> treats the same
        ///     cache and descriptor pair as the same thing to show and would leave the previous rows
        ///     on screen. That is what makes this usable after a replace, where one row's size and
        ///     format have both changed.
        /// </remarks>
        private void Reload() {
            if (cache == null) {
                header.Text = NoCacheText;
                anomalyNotice.Visible = false;
                libraries.Bind(null, new NativeLibraryListDescriptor());
                return;
            }

            libraries.Bind(cache, new NativeLibraryListDescriptor());
        }

        /// <summary>
        ///     Says what this cache's index 30 holds, once its rows are in.
        /// </summary>
        /// <remarks>
        ///     Counted from the rows rather than written down. Both caches carry thirty-six groups,
        ///     but a figure in a tab is read as a target the moment it is wrong once.
        /// </remarks>
        private void DescribeIndex() {
            var rows = libraries.Rows.OfType<NativeLibraryListing>().ToList();
            if (cache == null || rows.Count == 0)
                return;

            int named = rows.Count(row => row.Name.Path.Length > 0);
            int disagreeing = rows.Count(row => row.NameMatchesHeader == "NO");

            header.Text = "Index 30 - " + rows.Count + " group(s), one library each, " + named +
                          " of them named. " +
                          string.Join(", ", rows.GroupBy(row => row.Shape.Format)
                              .OrderBy(family => family.Key)
                              .Select(family => family.Count() + " " + family.Key)) +
                          ". " + (disagreeing == 0
                              ? "Every recovered name agrees with its payload's header."
                              : disagreeing + " name(s) disagree with their payload's header.");

            ShowAnomalies(rows);
        }

        /// <summary>
        ///     Puts every disagreeing name on screen, in full.
        /// </summary>
        /// <remarks>
        ///     Above the grid rather than only in a cell, because the whole point of the finding is
        ///     that it is invisible until someone compares a group against its siblings. In this
        ///     cache it is one group: <c>windows/x64/jagmisc.dll</c>, where the other five 64-bit
        ///     Windows libraries are under <c>windows/x86_64/</c>. The name is reported and never
        ///     corrected - the stored hash is the fact.
        /// </remarks>
        private void ShowAnomalies(IReadOnlyList<NativeLibraryListing> rows) {
            List<NativeLibraryListing> odd = rows.Where(row => row.Anomaly != null).ToList();

            anomalyNotice.Visible = odd.Count > 0;
            if (odd.Count == 0)
                return;

            anomalyNotice.Text = "Anomaly: " + string.Join(Environment.NewLine,
                odd.Select(row => "group " + row.GroupId + " (" + row.PathOrHash + ") - " + row.Anomaly));
        }

        /// <summary>Fills the detail pane from the selected row and offers it for transfer.</summary>
        /// <param name="listing">The selected row, or null.</param>
        private void ShowLibrary(NativeLibraryListing? listing) {
            fields.ShowFields(listing);
            transfer.Show(listing == null ? null : TargetFor(listing));

            if (listing == null) {
                detailNote.Text = cache == null ? NoCacheText : NoSelectionText;
                return;
            }

            detailNote.Text = listing.Summary + Environment.NewLine + Compression(listing.GroupId);
        }

        /// <summary>
        ///     How the selected group is compressed, read on selection rather than per row.
        /// </summary>
        /// <remarks>
        ///     Per selection because the container has to be re-read to answer it, and this index
        ///     holds multi-megabyte payloads. Worth showing at all because the compression is mixed
        ///     here - both GZip and BZip2 occur - and nothing must normalise it to one on a save.
        /// </remarks>
        /// <param name="groupId">The selected group.</param>
        /// <returns>A line for the detail note.</returns>
        private string Compression(int groupId) {
            if (cache == null)
                return string.Empty;

            try {
                RSContainer container = cache.GetContainer(RSConstants.NATIVE_LIBRARIES, groupId);
                if (container == null)
                    return string.Empty;

                string name = container.GetCompressionType() switch {
                    RSConstants.NO_COMPRESSION => "uncompressed",
                    RSConstants.BZIP2_COMPRESSION => "BZip2",
                    RSConstants.GZIP_COMPRESSION => "GZip",
                    _ => "compression type " + container.GetCompressionType()
                };

                return "Container: " + name + ", version " + container.GetVersion() +
                       ". Index 30's compression is mixed and a save must not normalise it.";
            }
            catch (Exception ex) {
                //Reported rather than thrown: this runs from a selection change, and an exception
                //out of one takes the form down. The row itself is still fully described above.
                Debug("Native library container could not be read: " + ex.Message, LOG_DETAIL.ADVANCED);
                return string.Empty;
            }
        }

        /// <summary>
        ///     Describes one row for the transfer strip.
        /// </summary>
        /// <remarks>
        ///     The relative path is the group's own name, so exporting the whole index rebuilds the
        ///     <c>windows/x86/</c> tree the client extracts into rather than colliding six libraries
        ///     onto one <c>jaggl.dll</c>.
        /// </remarks>
        /// <param name="listing">The row.</param>
        /// <returns>The transfer target.</returns>
        private static CachePayloadTarget TargetFor(NativeLibraryListing listing) {
            string leaf = listing.Name.FileName.Length > 0
                ? listing.Name.FileName
                : "index30_group" + listing.GroupId + ".bin";

            string relative = listing.Name.Path.Length > 0
                ? listing.Name.Path
                : leaf;

            return new CachePayloadTarget(RSConstants.NATIVE_LIBRARIES, listing.Address, listing.Stored,
                leaf, relative,
                "Native library (*.dll;*.so;*.dylib)|*.dll;*.so;*.dylib|All files (*.*)|*.*",
                listing.PathOrHash,
                validate: bytes => RefuseFormatChange(listing, bytes));
        }

        /// <summary>
        ///     Refuses a replacement whose container format is not the one already stored.
        /// </summary>
        /// <remarks>
        ///     The cache would take it, the CRC and digest would be recomputed over it, and every
        ///     check in this editor would pass - the failure would only appear at the client, which
        ///     writes the bytes to a file named for a platform they are not for and then loads them.
        ///     A refusal here is the only place it can be caught.
        /// </remarks>
        /// <param name="listing">The row being replaced.</param>
        /// <param name="bytes">The file the user picked.</param>
        /// <returns>A refusal, or null to accept.</returns>
        private static string? RefuseFormatChange(NativeLibraryListing listing, byte[] bytes) {
            NativeBinaryShape replacement = NativeBinaryShape.Of(bytes);

            if (replacement.Kind == listing.Shape.Kind)
                return null;

            return "that file is " + replacement.Format + " and " + listing.PathOrHash + " holds " +
                   listing.Shape.Format + ". Replacing one container format with another is accepted by the" +
                   " cache and fails at the client, where nothing reports it.";
        }
    }
}
