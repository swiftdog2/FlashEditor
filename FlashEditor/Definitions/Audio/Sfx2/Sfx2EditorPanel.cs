using BrightIdeasSoftware;
using FlashEditor.cache;
using FlashEditor.Definitions.Editing;
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
using System.Windows.Forms;
using static FlashEditor.Utils.DebugUtil;

namespace FlashEditor.Definitions.Audio.Sfx2 {
    /// <summary>
    ///     The SFX2 tab: index 14, one row per group, with the selected record's fields and its
    ///     Vorbis packet list beside them.
    /// </summary>
    /// <remarks>
    ///     Master and detail rather than a bare <see cref="DefinitionListPanel"/>, for two reasons
    ///     that are both about honesty rather than about space.
    ///     <para>
    ///     <b>The index holds two unrelated shapes.</b> Group 0 is the shared Vorbis setup header and
    ///     codebooks; every other group is a sample. The list carries both because the reference
    ///     table declares both and hiding one would misreport the index's size, but a grid row cannot
    ///     say what group 0 <i>is</i> - the detail pane can, and does, with its own field list rather
    ///     than a sample's headings left blank.
    ///     </para>
    ///     <para>
    ///     <b>The editor cannot play any of it, and has to say so.</b> That note is a docked label
    ///     rather than a disabled button, because a greyed-out play control reads as a bug in the
    ///     tab. See <see cref="PlaybackNote"/> for why no off-the-shelf decoder takes these bytes.
    ///     </para>
    ///     <para>
    ///     <b>Where playback lands when it is written.</b> A transport strip docks into the detail
    ///     half, driven by the selected row plus group 0, and the packet grid becomes the seek bar it
    ///     already looks like. Nothing above that level changes: the list, the descriptor and the
    ///     codec all stay as they are, because none of them assumes the audio is undecodable - only
    ///     this panel's note does, and the note is one string.
    ///     </para>
    /// </remarks>
    public sealed class Sfx2EditorPanel : UserControl {
        /* Consolas 9 on every child. The form puts Consolas 12 on the tab control and everything
           under it inherits, which is half again what these grids are laid out for. */
        private static readonly Font PanelFont = new Font("Consolas", 9F);

        /// <summary>
        ///     What this tab deliberately does not do, and why it is a choice rather than a defect.
        /// </summary>
        /// <remarks>
        ///     Stated on screen because a user comparing a sound effect here against the one they
        ///     hear in game has no other way to tell "not implemented" from "broken". The reason is
        ///     specific and worth carrying: group 0 is a hybrid of the two blocksize nibbles from the
        ///     Vorbis identification header and a setup header with no <c>\x01vorbis</c> magic, no
        ///     channel count, no sample rate and no framing bit, so it is not a packet any stock
        ///     Vorbis library will accept. See <see cref="Sfx2SetupHeader"/>.
        /// </remarks>
        private const string PlaybackNote =
            "This tab does not play audio, on purpose. Index 14 is Vorbis, but the setup header in " +
            "group 0 has no vorbis magic, no channel count and no framing bit, so no off-the-shelf " +
            "decoder accepts it and one has to be written against the client's Node_Sub13 and " +
            "Class71. Records here are read and written back byte for byte; nothing turns them into " +
            "PCM. Rate and the two loop points are editable, the packets are not.";

        private readonly Label playback = new Label {
            AutoSize = true,
            Dock = DockStyle.Top,
            Font = PanelFont,
            Text = PlaybackNote
        };

        //AutoSize rather than a stated height, so the line the summary needs is the line it gets.
        private readonly Label header = new Label {
            AutoSize = true,
            Dock = DockStyle.Top,
            Font = PanelFont,
            Text = NoCacheText
        };

        private readonly DefinitionListPanel records = new DefinitionListPanel();

        private readonly DetailFieldGrid fields = new DetailFieldGrid();

        private readonly FastObjectListView packets = new FastObjectListView {
            Dock = DockStyle.Fill,
            Font = PanelFont,
            FullRowSelect = true,
            GridLines = true,
            ShowGroups = false,
            UseFiltering = true,
            View = View.Details
        };

        /* No splitter states a minimum size. Setting one re-checks the current distance against it,
           and a container is still at its 150x100 default when a field initialiser runs. */
        private readonly SplitContainer listAndDetail = new SplitContainer {
            Dock = DockStyle.Fill,
            Orientation = Orientation.Horizontal
        };

        private readonly SplitContainer fieldsAndPackets = new SplitContainer {
            Dock = DockStyle.Fill,
            Orientation = Orientation.Vertical
        };

        /* One instance for the life of the panel. DefinitionListPanel.Bind treats the same
           (cache, descriptor) pair as the same thing to show, so a fresh descriptor on every bind
           would reload all 3,657 groups on each visit to the tab and throw away the sort. */
        private readonly Sfx2ListDescriptor descriptor = new Sfx2ListDescriptor();

        private const string NoCacheText = "No cache loaded";
        private const string NoSelectionText = "Select a sound effect to see its header and packets";

        private RSCache? cache;
        private bool listSplitterPlaced;
        private bool detailSplitterPlaced;

        /// <summary>Creates the panel.</summary>
        public Sfx2EditorPanel() {
            Dock = DockStyle.Fill;
            BuildLayout();

            records.SelectedRowChanged += (_, _) => ShowRecord(records.SelectedRow as Sfx2Listing);
        }

        /// <summary>
        ///     Points the panel at a cache, or clears it when given none.
        /// </summary>
        /// <remarks>
        ///     <c>Editor.LoadEditorTab</c> calls this on every visit to the tab, so a rebind of the
        ///     cache already on display has to be a no-op or the selection and the sort are thrown
        ///     away each time. Identity is the right test because opening a cache builds a new
        ///     <see cref="RSCache"/>.
        /// </remarks>
        /// <param name="newCache">The open cache, or null to unbind.</param>
        public void Bind(RSCache? newCache) {
            if (ReferenceEquals(newCache, cache))
                return;

            cache = newCache;
            fields.ClearObjects();
            packets.ClearObjects();
            header.Text = newCache == null ? NoCacheText : NoSelectionText;

            records.Bind(newCache, descriptor);
        }

        /// <summary>Places the splitters once the layout pass has given the containers a real size.</summary>
        /// <param name="levent">The event data.</param>
        protected override void OnLayout(LayoutEventArgs levent) {
            base.OnLayout(levent);
            WrapPlaybackNote();
            PlaceSplitters();
        }

        /// <summary>
        ///     Lets the playback note wrap at the width the panel actually has.
        /// </summary>
        /// <remarks>
        ///     An <c>AutoSize</c> label grows sideways rather than wrapping, so the note would run off
        ///     the right edge on a narrow window and take its last sentence with it. Capping the width
        ///     at the client area turns the same auto-sizing into height, which is what a docked strip
        ///     wants. Measured from the panel rather than stated as a pixel count, because the form
        ///     scales by DPI and a literal is only right at the one it was written on.
        /// </remarks>
        private void WrapPlaybackNote() {
            int available = ClientSize.Width;
            if (available <= 0 || playback.MaximumSize.Width == available)
                return;

            //Zero height means "no cap on the height", which is the whole point: the label grows
            //downwards by however many lines the wrapped text needs.
            playback.MaximumSize = new Size(available, 0);
        }

        /// <summary>Divides the panel proportionally, once each, when there is a size worth dividing.</summary>
        /// <remarks>
        ///     A <see cref="SplitContainer"/> defaults to a splitter distance of 50 <i>pixels</i>, not
        ///     half, so the distance has to be stated - and stating it in a field initialiser would
        ///     set it against the container's 150x100 default rather than against its real size.
        /// </remarks>
        private void PlaceSplitters() {
            if (!listSplitterPlaced && listAndDetail.Height >= 200) {
                //Set before the assignment, not after: changing a splitter distance lays the panel
                //out again, and this is called from that layout.
                listSplitterPlaced = true;
                try {
                    listAndDetail.SplitterDistance =
                        Math.Max(listAndDetail.Panel1MinSize, listAndDetail.Height * 3 / 5);
                } catch (InvalidOperationException ex) {
                    listSplitterPlaced = false;
                    Debug("SFX2 tab list splitter not placed yet: " + ex.Message, LOG_DETAIL.ADVANCED);
                }
            }

            if (detailSplitterPlaced || fieldsAndPackets.Width < 200)
                return;

            detailSplitterPlaced = true;
            try {
                fieldsAndPackets.SplitterDistance =
                    Math.Max(fieldsAndPackets.Panel1MinSize, fieldsAndPackets.Width * 3 / 5);
            } catch (InvalidOperationException ex) {
                detailSplitterPlaced = false;
                Debug("SFX2 tab detail splitter not placed yet: " + ex.Message, LOG_DETAIL.ADVANCED);
            }
        }

        private void BuildLayout() {
            BuildPacketColumns();

            fieldsAndPackets.Panel1.Controls.Add(fields);
            fieldsAndPackets.Panel2.Controls.Add(packets);

            listAndDetail.Panel1.Controls.Add(records);
            listAndDetail.Panel2.Controls.Add(fieldsAndPackets);

            //Docking resolves from the end of the Controls collection backwards, so the strips have
            //to be added after the filled splitter, and in bottom-to-top order among themselves.
            Controls.Add(listAndDetail);
            Controls.Add(header);
            Controls.Add(playback);

            //Bound before any cache arrives so the list has its headings from the start.
            records.Bind(null, descriptor);
        }

        private void BuildPacketColumns() {
            //Delegates rather than aspect names: a name looked up by reflection blanks the column
            //when the property is renamed, where a delegate stops compiling.
            AddPacketColumn("Packet", 80, row => row.Index);
            AddPacketColumn("Offset", 90, row => row.Offset);
            AddPacketColumn("Bytes", 80, row => row.Length);
            AddPacketColumn("Prefix", 80, row => row.PrefixBytes);
            AddPacketColumn("First bytes", 220, row => row.Preview);
        }

        private void AddPacketColumn(string heading, int width, Func<Sfx2PacketRow, object?> read) {
            var column = new OLVColumn(heading, null) {
                Width = width,
                Groupable = false,
                IsEditable = false,
                //A null row is a legitimate state: ObjectListView evaluates aspects for rows being
                //recycled during a scroll. A row of the wrong type still throws, because that could
                //only mean this grid was filled with something else.
                AspectGetter = row => row == null
                    ? null
                    : read(row as Sfx2PacketRow ?? throw new ArgumentException(
                        "The packet grid holds Sfx2PacketRow but was handed a " + row.GetType().Name + ".",
                        nameof(row)))
            };

            packets.AllColumns.Add(column);
            packets.Columns.Add(column);
        }

        /// <summary>Fills the detail pane from the selected row.</summary>
        /// <remarks>
        ///     No cache read: the row already carries the whole decoded record, packets included.
        /// </remarks>
        /// <param name="listing">The selected row, or null.</param>
        private void ShowRecord(Sfx2Listing? listing) {
            fields.ShowFields(listing);

            if (listing == null) {
                packets.ClearObjects();
                header.Text = cache == null ? NoCacheText : NoSelectionText;
                return;
            }

            header.Text = listing.Summary;
            packets.SetObjects(BuildPacketRows(listing.Sample));
        }

        /// <summary>
        ///     The selected record's packets, one row each.
        /// </summary>
        /// <remarks>
        ///     Empty for group 0 rather than absent, because the setup header genuinely holds no
        ///     packets - it is the codebooks the packets are decoded against. An empty grid beside a
        ///     field list that says so is the accurate picture; hiding the grid would suggest the tab
        ///     had failed to load something.
        /// </remarks>
        /// <param name="sample">The selected sample, or null when the setup header is selected.</param>
        /// <returns>The packet rows, in stream order.</returns>
        private static List<Sfx2PacketRow> BuildPacketRows(Sfx2Sample? sample) {
            if (sample == null)
                return new List<Sfx2PacketRow>();

            var rows = new List<Sfx2PacketRow>(sample.PacketCount);
            int offset = 0;

            for (int i = 0; i < sample.PacketCount; i++) {
                int length = sample.PacketLengths[i];
                rows.Add(new Sfx2PacketRow(i, offset, length, Preview(sample.Packet(i))));
                offset += length;
            }

            return rows;
        }

        /// <summary>
        ///     The first few bytes of a packet, in hex.
        /// </summary>
        /// <remarks>
        ///     A preview rather than the packet, because these are compressed audio and a full hex
        ///     dump of 431,000 packets is not something a grid should be asked to render. The leading
        ///     bytes are still worth seeing: the first bit of a Vorbis audio packet is its packet
        ///     type and the next few select the mode, so two packets that start alike are decoded
        ///     alike.
        /// </remarks>
        /// <param name="packet">The packet's bytes.</param>
        /// <returns>The preview.</returns>
        private static string Preview(ReadOnlySpan<byte> packet) {
            const int shown = 8;

            int count = Math.Min(shown, packet.Length);
            var text = new System.Text.StringBuilder(count * 3 + 4);

            for (int i = 0; i < count; i++) {
                if (i > 0)
                    text.Append(' ');
                text.Append(packet[i].ToString("X2", CultureInfo.InvariantCulture));
            }

            if (packet.Length > count)
                text.Append(" ...");

            return text.ToString();
        }

        /// <summary>One Vorbis packet of the selected record, as a grid row.</summary>
        private sealed class Sfx2PacketRow {
            internal Sfx2PacketRow(int index, int offset, int length, string preview) {
                Index = index;
                Offset = offset;
                Length = length;
                Preview = preview;
            }

            /// <summary>The packet's position in the record, which is its playback order.</summary>
            internal int Index { get; }

            /// <summary>Where the packet starts within the record's audio, excluding length prefixes.</summary>
            internal int Offset { get; }

            /// <summary>The packet's length in bytes.</summary>
            internal int Length { get; }

            /// <summary>
            ///     How many bytes the length prefix costs ahead of this packet.
            /// </summary>
            /// <remarks>
            ///     Shown because it is the one part of the format a sweep over this cache cannot
            ///     defend: no packet in either cache reaches 255 bytes, so every prefix here is one
            ///     byte and the continuation branch is never exercised. A column reading 2 would mean
            ///     imported audio has started to use it.
            /// </remarks>
            internal int PrefixBytes => Length / Sfx2Sample.PacketLengthRadix + 1;

            /// <summary>The packet's leading bytes in hex.</summary>
            internal string Preview { get; }
        }
    }
}
