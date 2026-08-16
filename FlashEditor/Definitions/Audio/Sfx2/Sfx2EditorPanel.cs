using BrightIdeasSoftware;
using FlashEditor.Cache;
using FlashEditor.Definitions.Audio.Sfx2.Vorbis;
using FlashEditor.Definitions.Audio.Synth;
using FlashEditor.Definitions.Editing;
using FlashEditor.IO;
using FlashEditor.UI;
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
    ///     <b>It plays, and it did not used to.</b> This tab carried a note saying no off-the-shelf
    ///     decoder takes these bytes and that one would have to be written - which was true when it
    ///     was written and stopped being true when <see cref="Vorbis.Sfx2VorbisDecoder"/> landed and
    ///     the music player started rendering index-14 samples through it. The note now says what
    ///     playback here does and does not do rather than that there is none.
    ///     </para>
    ///     <para>
    ///     <b>Looping is deliberately not applied.</b> A record carries two loop points and a flag,
    ///     and the game uses them; playing the buffer once from first sample to last is what lets a
    ///     user hear the record itself, which is what an editor is for. The note says so, because
    ///     an effect that sounds shorter than it does in game is otherwise read as a decode fault.
    ///     It reads off the (i) on the transport rather than off a paragraph docked above the grids.
    ///     </para>
    ///     <para>
    ///     <b>Where a full transport lands when it is written.</b> A strip docks into the detail
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
        ///     Reachable on screen because a user comparing a sound effect here against the one they
        ///     hear in game has no other way to tell "not implemented" from "broken". The reason is
        ///     specific and worth carrying: group 0 is a hybrid of the two blocksize nibbles from the
        ///     Vorbis identification header and a setup header with no <c>\x01vorbis</c> magic, no
        ///     channel count, no sample rate and no framing bit, so it is not a packet any stock
        ///     Vorbis library will accept. See <see cref="Sfx2SetupHeader"/>.
        /// </remarks>
        private const string PlaybackNote =
            "Index 14 is Vorbis, but not a stream any stock decoder accepts: the setup header in " +
            "group 0 has no vorbis magic, no channel count and no framing bit, so it was decoded " +
            "by a Vorbis implementation written against the client. Play uses that. Looping is not " +
            "applied - an effect plays once, from the first sample to the last, so what you hear is " +
            "the record rather than the record as the game would loop it. Rate and the two loop " +
            "points are editable, the packets are not.";

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

        /* The same transport the Tracks tab has, for the same reason: playback is a mode rather
           than a command that completes, so the controls are a state a user reads at a glance. An
           EditorToolStrip because CLAUDE.md says a toolbar is one - it gets the theme's ink, its
           hover and pressed fills, and tooltips carrying the captions the icons replace. */
        private readonly EditorToolStrip transport = new EditorToolStrip {
            Dock = DockStyle.None,
            AutoSize = true
        };

        private readonly EditorToolButton previousButton;
        private readonly EditorToolButton playPauseButton;
        private readonly EditorToolButton nextButton;

        private RSCache? cache;

        /* Decoded once per cache, because every effect shares it: group 0 is the Vorbis setup for
           the whole index, not a per-record header. Null once it has failed, so a broken group 0
           does not cost a re-read on every click. */
        private VorbisSetup? setup;
        private bool setupFailed;

        private Sfx2Playback? playing;

        private bool listSplitterPlaced;
        private bool detailSplitterPlaced;

        /// <summary>Creates the panel.</summary>
        public Sfx2EditorPanel() {
            Dock = DockStyle.Fill;

            /* Built here rather than in a field initialiser because AddAction is an instance method
               on the strip, and a field initialiser cannot reach another field. */
            previousButton = transport.AddAction(EditorIcon.PreviousTrack, "Previous effect",
                Keys.None, (_, _) => Step(-1));
            playPauseButton = transport.AddAction(EditorIcon.Play, "Play the selected effect",
                Keys.None, (_, _) => TogglePlayPause());
            nextButton = transport.AddAction(EditorIcon.NextTrack, "Next effect",
                Keys.None, (_, _) => Step(1));

            BuildLayout();

            records.SelectedRowChanged += (_, _) => {
                var listing = records.SelectedRow as Sfx2Listing;
                ShowRecord(listing);

                UpdateTransport();
            };
        }

        /// <summary>
        ///     Play, hold, carry on - the one control the transport is built around.
        /// </summary>
        /// <remarks>
        ///     Resuming a held effect rather than restarting it is the whole point of the icon. A
        ///     button captioned with a pause glyph that silently replayed from the first sample
        ///     would be describing something the player does not do.
        /// </remarks>
        private void TogglePlayPause() {
            Sfx2Playback? running = playing;

            if (running != null && running.IsPlaying) {
                if (running.IsPaused)
                    running.Resume();
                else
                    running.Pause();

                UpdateTransport();
                return;
            }

            PlaySelected();
        }

        /// <summary>
        ///     Moves the selection by one row and plays what it lands on.
        /// </summary>
        /// <remarks>
        ///     Wraps at both ends, matching the Tracks transport: a Next that goes dead on the last
        ///     row reads as broken rather than as finished. Rows that carry no audio are stepped
        ///     over rather than selected and refused - group 0 is the shared setup header and is a
        ///     row in this list like any other, so a plain step would stall on it.
        /// </remarks>
        /// <param name="direction">-1 for the previous effect, 1 for the next.</param>
        private void Step(int direction) {
            int count = records.Rows.Count;
            if (count == 0)
                return;

            //IReadOnlyList has no IndexOf, and the row identity is the object rather than an id.
            int at = -1;
            for (int i = 0; i < count; i++) {
                if (ReferenceEquals(records.Rows[i], records.SelectedRow)) {
                    at = i;
                    break;
                }
            }

            for (int step = 1; step <= count; step++) {
                int next = ((at + direction * step) % count + count) % count;

                if (records.Rows[next] is Sfx2Listing candidate && candidate.Sample != null) {
                    records.SelectRow(candidate);
                    PlaySelected();
                    return;
                }
            }
        }

        /// <summary>
        ///     Puts the transport into the state the playback is actually in.
        /// </summary>
        /// <remarks>
        ///     One method rather than the conditions rebuilt at each site that changes them. The
        ///     Tracks panel had three copies of its play button's enabled state and they had already
        ///     drifted, one requiring a length the others did not.
        /// </remarks>
        private void UpdateTransport() {
            Sfx2Playback? running = playing;
            bool held = running != null && running.IsPaused;
            bool live = running != null && running.IsPlaying;

            //Never disabled while there are rows: Step wraps, so there is always somewhere to go.
            previousButton.Enabled = records.Rows.Count > 0;
            nextButton.Enabled = previousButton.Enabled;

            //Only a sample can be played. Group 0 is the shared setup header and has no audio.
            bool startable = cache != null && records.SelectedRow is Sfx2Listing listing &&
                             listing.Sample != null;

            playPauseButton.Enabled = live || startable;

            /* Pause only while it is actually sounding. A held effect shows Play, because that is
               what the next click does - the icon states the action, not the state. */
            bool showPause = live && !held;
            playPauseButton.Icon = showPause ? EditorIcon.Pause : EditorIcon.Play;
            playPauseButton.Describe(showPause
                ? "Pause, keeping the position and the queued audio"
                : held ? "Resume" : "Play the selected effect");
        }

        /// <summary>
        ///     Decodes the selected effect and plays it once.
        /// </summary>
        /// <remarks>
        ///     Any effect already playing is stopped first, so clicking down a list of effects plays
        ///     each rather than layering them - and because two open <c>waveOut</c> devices at
        ///     different rates is not something to find out about by accident.
        /// </remarks>
        /// <summary>
        ///     Puts a message on the status line from the playback thread.
        /// </summary>
        /// <remarks>
        ///     The events fire on <c>Sfx2Playback</c>'s own thread, and the status line is a control.
        ///     Marshalled rather than called directly, and dropped outright once the handle has gone
        ///     - a device that fails while the tab is closing must not take the form down with it.
        /// </remarks>
        /// <summary>Puts the transport back to Play when a playback ends on its own.</summary>
        private void RefreshTransportFromPlaybackThread() {
            if (IsDisposed || !IsHandleCreated)
                return;

            try {
                BeginInvoke(new Action(UpdateTransport));
            }
            catch (ObjectDisposedException) {
                //The panel went away between the check and the post.
            }
        }

        private void ReportFromPlaybackThread(string message) {
            if (IsDisposed || !IsHandleCreated)
                return;

            try {
                BeginInvoke(new Action(() => records.ReportStatus(message)));
            }
            catch (ObjectDisposedException) {
                //The panel went away between the check and the post. Nothing to report to.
            }
        }

        private void PlaySelected() {
            playing?.Dispose();
            playing = null;

            if (records.SelectedRow is not Sfx2Listing listing || listing.Sample == null || cache == null)
                return;

            VorbisSetup? header = Setup();
            if (header == null) {
                records.ReportStatus("Group 0 could not be decoded, so nothing in this index can be played.");
                return;
            }

            try {
                byte[] pcm = new Sfx2VorbisDecoder(header).Decode(listing.Sample);

                /* THE DECODER PRODUCES 8-BIT PCM, and waveOut is opened for 16. Each sample is
                   shifted rather than cast: an sbyte assigned straight to a short is a value in
                   -128..127, which against a 16-bit full scale is silence with a faint buzz - and
                   that reads as a broken decoder rather than as a scaling mistake. */
                sbyte[] eightBit = PcmSample.AsSigned(pcm);
                var samples = new short[eightBit.Length];
                for (int i = 0; i < eightBit.Length; i++)
                    samples[i] = (short) (eightBit[i] << 8);

                if (samples.Length == 0) {
                    records.ReportStatus("Effect " + listing.Sample.Id + " decoded to no samples at all.");
                    return;
                }

                int id = listing.Sample.Id;
                var started = new Sfx2Playback(samples, listing.Sample.SampleRate);

                /* Subscribed, because nothing was. The playback thread reports a device failure
                   through this event and the first version of this panel ignored it, so a throw on
                   the very first buffer left the status line reading "Playing effect N" while the
                   machine stayed silent - a defect that presented as "the audio does not work"
                   with no clue anywhere as to why. */
                started.Failed += error => ReportFromPlaybackThread(
                    "Effect " + id + " could not be played: " + error.Message);

                //Both ends of a playback put the transport back to Play. Marshalled, because they
                //are raised on the playback thread and these are controls.
                started.Failed += _ => RefreshTransportFromPlaybackThread();
                started.Completed += RefreshTransportFromPlaybackThread;

                playing = started;
                UpdateTransport();
                records.ReportStatus("Playing effect " + id + ", " + samples.Length +
                    " samples at " + listing.Sample.SampleRate + " Hz");
            }
            catch (Exception ex) {
                //Reported rather than thrown: this runs from a button on a tab, and an exception out
                //of it takes the form down over one record that will not decode.
                records.ReportStatus("Effect " + listing.Sample.Id + " could not be played: " + ex.Message);
                Debug("SFX2 playback failed: " + ex);
            }
        }

        /// <summary>The shared Vorbis setup from group 0, decoded once per cache.</summary>
        private VorbisSetup? Setup() {
            if (setup != null || setupFailed)
                return setup;

            try {
                JagStream? group = cache?.ReadFile(RSConstants.SFX2_INDEX, Sfx2SetupHeader.SetupGroupId, 0);
                if (group != null)
                    setup = new VorbisSetup(group.ToArray());
            }
            catch (Exception ex) {
                setup = null;
                Debug("SFX2 setup header could not be decoded: " + ex.Message);
            }

            setupFailed = setup == null;
            return setup;
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

            //The setup header belongs to the cache being replaced, and an effect mid-flight was
            //decoded from it.
            playing?.Dispose();
            playing = null;
            setup = null;
            setupFailed = false;

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
            PlaceSplitters();
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

            /* The note is on the transport itself rather than docked as a paragraph above it. It is
               entirely about what pressing Play does and does not do, so the glyph that reveals it
               belongs on the strip carrying that button - which is also the only place it can sit
               without costing the grids four lines of height on every visit to the tab. */
            transport.Items.Add(new ToolStripControlHost(
                InfoAffordance.For(transport, InfoKind.Limitation, PlaybackNote)) {
                Alignment = ToolStripItemAlignment.Right
            });

            //Docking resolves from the end of the Controls collection backwards, so the strips have
            //to be added after the filled splitter, and in bottom-to-top order among themselves.
            var transportStrip = new FlowLayoutPanel {
                Dock = DockStyle.Top,
                FlowDirection = FlowDirection.LeftToRight,
                AutoSize = true,
                AutoSizeMode = AutoSizeMode.GrowAndShrink,
                WrapContents = false
            };
            transportStrip.Controls.Add(transport);

            Controls.Add(listAndDetail);
            Controls.Add(header);
            Controls.Add(transportStrip);

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
            ///     <para>
            ///     Asked of the codec rather than worked out here, for exactly the reason above: a
            ///     restatement of the width rule in the display would agree with the encoder on every
            ///     byte the cache holds and could only diverge on data no test has.
            ///     </para>
            /// </remarks>
            internal int PrefixBytes => Sfx2Sample.PacketLengthPrefixBytes(Length);

            /// <summary>The packet's leading bytes in hex.</summary>
            internal string Preview { get; }
        }
    }
}
