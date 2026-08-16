using System;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
using System.Windows.Forms;
using FlashEditor.Cache;
using FlashEditor.Definitions.Audio.Synth;
using FlashEditor.Definitions.Editing;
using FlashEditor.UI;
using static FlashEditor.Utils.DebugUtil;

namespace FlashEditor.Definitions.Audio {
    /// <summary>
    ///     The MIDI patch tab: index 15, one row per patch, with the selected patch drawn as a
    ///     playable 128-key keyboard.
    /// </summary>
    /// <remarks>
    ///     Index 15 is the layer that turns "program 40, key 60" into a sound, so the tab is built
    ///     around a keyboard rather than a grid. A patch is 128 keys and every interesting question
    ///     about one is positional: which register is sampled, where a kit puts its hi-hats, which
    ///     keys cut each other. See <see cref="MidiKeyboardControl"/> for what the drawing states.
    ///     <para>
    ///     <b>Two things this tab says out loud, because nothing else on screen would.</b> A key's
    ///     sample reference is three fields packed into one integer, and one of them chooses between
    ///     two entirely different sample formats in two different indexes. And one of those indexes
    ///     has no renderer in this project at all, so some keys are silent here by omission rather
    ///     than by fault. Both are stated in the notes docked above the list and again on the
    ///     selected key's detail pane.
    ///     </para>
    ///     <para>
    ///     <b>Playback goes through the track player rather than a third transport.</b> Auditioning a
    ///     key builds a one-note standard MIDI file (<see cref="MidiKeyPreview"/>) and hands it to
    ///     <see cref="TrackPlayback"/>, which already owns the thread, the device, the pause that
    ///     holds a voice mid-envelope and the drain wait that stops the last buffer being thrown
    ///     away on disposal.
    ///     </para>
    /// </remarks>
    public sealed class MidiPatchEditorPanel : UserControl {
        /// <summary>
        ///     What a sample reference actually is, said before a user has to work it out.
        /// </summary>
        /// <remarks>
        ///     The reference appears everywhere as a single number and is three unrelated fields:
        ///     <c>Node_Sub44.java:215-219</c> takes bit 1 as the sustain flag and <c>:476-485</c>
        ///     routes bit 0 between the two sample archives. A user reading the id straight off the
        ///     stored value is off by a factor of four and in the wrong index half the time.
        /// </remarks>
        private const string ReferenceNote =
            "A key's sample reference is three fields in one number. Take the stored value less one: " +
            "bit 0 selects the bank - 0 is index 4, the procedural synth bank, 1 is index 14, the " +
            "recorded Vorbis bank - bit 1 is the sustain flag, which the client folds into the top " +
            "bit of the key's tuning word, and the sample id is the rest, v >> 2. A stored 0 means " +
            "the key is silent.";

        /// <summary>
        ///     That index 4 is decoded here and not rendered here, which is a gap and not a fault.
        /// </summary>
        /// <remarks>
        ///     <c>MidiSoundBank.Sample</c> counts every such key into
        ///     <c>MidiSoundBank.UnrenderedEffectKeys</c> rather than dropping the note quietly, and
        ///     this is where that count is put on screen. Porting the index-4 synthesiser is separate
        ///     work and is not what this tab is.
        /// </remarks>
        private const string RendererNote =
            "THIS EDITOR HAS NO INDEX-4 RENDERER. A key whose bank bit is 0 names a procedural synth " +
            "patch that nothing here turns into audio, so playing it is silent rather than wrong. " +
            "The Index 4 column, the hatched band on the keyboard and the census line above all say " +
            "which keys those are.";

        /// <summary>What auditioning a key does, and where it differs from hearing it in game.</summary>
        /// <remarks>
        ///     Stated because a user comparing a key here against the same instrument in a track has
        ///     no way to tell a documented choice from a defect. The differences are all
        ///     <see cref="MidiSynthesiser"/>'s and are listed on it; what belongs here is the two this
        ///     tab adds, the fixed hold and the cut that ends the note.
        /// </remarks>
        private const string PlaybackNote =
            "Play strikes the selected key once, holds it for a second and then cuts everything a " +
            "further two seconds later. The cut is part of the note's own sequence rather than a " +
            "timer: a voice that owns its mute group never advances its release - that is the drum " +
            "choke - so some keys would otherwise ring for as long as the tab was open. Playback is " +
            "this project's synthesiser, not the game's mixer: no voice stealing, and no channel " +
            "volume, expression or pan, so a key is heard on its own terms.";

        private readonly Label census = new Label {
            AutoSize = true,
            Dock = DockStyle.Top,
            Font = EditorTheme.NoticeFont,
            Text = NoCacheText
        };

        private readonly Label notes = new Label {
            AutoSize = true,
            Dock = DockStyle.Top,
            Font = EditorTheme.NoticeFont,
            Text = ReferenceNote + "\r\n" + RendererNote + "\r\n" + PlaybackNote
        };

        private readonly Label keyHeader = new Label {
            AutoSize = true,
            Dock = DockStyle.Top,
            Font = EditorTheme.UiFont,
            Text = NoSelectionText
        };

        private readonly DefinitionListPanel patches = new DefinitionListPanel();

        private readonly MidiKeyboardControl keyboard = new MidiKeyboardControl { Dock = DockStyle.Fill };

        private readonly DetailFieldGrid keyFields = new DetailFieldGrid();

        private readonly DetailFieldGrid patchFields = new DetailFieldGrid();

        /* No splitter states a minimum size. Setting one re-checks the current distance against it,
           and a container is still at its 150x100 default when a field initialiser runs. */
        private readonly SplitContainer listAndPatch = new SplitContainer {
            Dock = DockStyle.Fill,
            Orientation = Orientation.Horizontal
        };

        private readonly SplitContainer keyboardAndFields = new SplitContainer {
            Dock = DockStyle.Fill,
            Orientation = Orientation.Horizontal
        };

        private readonly SplitContainer keyAndPatchFields = new SplitContainer {
            Dock = DockStyle.Fill,
            Orientation = Orientation.Vertical
        };

        /* One instance for the life of the panel. DefinitionListPanel.Bind treats the same
           (cache, descriptor) pair as the same thing to show, so a fresh descriptor on every bind
           would reload the whole index on each visit and throw away the sort. */
        private readonly MidiPatchListDescriptor descriptor = new MidiPatchListDescriptor();

        private readonly EditorToolStrip transport = new EditorToolStrip {
            Dock = DockStyle.None,
            AutoSize = true
        };

        private readonly EditorToolButton previousButton;
        private readonly EditorToolButton playPauseButton;
        private readonly EditorToolButton nextButton;

        private const string NoCacheText = "No cache loaded";
        private const string NoSelectionText = "Select a patch, then click a key to hear it";

        private RSCache? cache;
        private TrackPlayback? playing;

        private bool listSplitterPlaced;
        private bool detailSplitterPlaced;
        private bool fieldsSplitterPlaced;

        /// <summary>Creates the panel.</summary>
        public MidiPatchEditorPanel() {
            Dock = DockStyle.Fill;

            //Built here rather than in a field initialiser because AddAction is an instance method on
            //the strip, and a field initialiser cannot reach another field.
            previousButton = transport.AddAction(EditorIcon.PreviousTrack, "Previous playable key",
                Keys.None, (_, _) => Step(-1));
            playPauseButton = transport.AddAction(EditorIcon.Play, "Play the selected key",
                Keys.None, (_, _) => TogglePlayPause());
            nextButton = transport.AddAction(EditorIcon.NextTrack, "Next playable key",
                Keys.None, (_, _) => Step(1));

            BuildLayout();

            patches.SelectedRowChanged += (_, _) => ShowPatch(patches.SelectedRow as MidiPatchListing);
            patches.RowsLoaded += (_, _) => ShowCensus();

            keyboard.SelectedKeyChanged += (_, _) => {
                ShowKey();
                UpdateTransport();
            };
            keyboard.KeyActivated += (_, e) => PlayKey(e.Key);
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

            //A note in flight was rendered from the cache being replaced, and its sound bank is still
            //reading patches and samples out of it on the playback thread.
            playing?.Dispose();
            playing = null;

            cache = newCache;
            keyboard.Bind(null);
            keyFields.ClearObjects();
            patchFields.ClearObjects();
            keyHeader.Text = newCache == null ? NoCacheText : NoSelectionText;
            census.Text = newCache == null ? NoCacheText : "Loading the patch bank";

            patches.Bind(newCache, descriptor);
            UpdateTransport();
        }

        /// <summary>Stops anything sounding when the tab goes away.</summary>
        /// <param name="disposing">Whether managed state is being released.</param>
        protected override void Dispose(bool disposing) {
            if (disposing) {
                playing?.Dispose();
                playing = null;
            }

            base.Dispose(disposing);
        }

        /// <summary>Places the splitters once the layout pass has given the containers a real size.</summary>
        /// <param name="levent">The event data.</param>
        protected override void OnLayout(LayoutEventArgs levent) {
            base.OnLayout(levent);
            WrapNotes();
            PlaceSplitters();
        }

        // ===================================================================
        //  Transport
        // ===================================================================

        /// <summary>Play, hold, carry on - the one control the transport is built around.</summary>
        /// <remarks>
        ///     Resuming a held note rather than restarting it is the whole point of the icon, and it
        ///     is what <see cref="TrackPlayback.Pause"/> exists to make possible: the sequencer's
        ///     position, every sounding voice and its envelope are all held exactly as they were.
        /// </remarks>
        private void TogglePlayPause() {
            TrackPlayback? running = playing;

            if (running != null && running.IsPlaying) {
                if (running.IsPaused)
                    running.Resume();
                else
                    running.Pause();

                UpdateTransport();
                return;
            }

            PlayKey(keyboard.SelectedKey);
        }

        /// <summary>
        ///     Moves the selection to the next key this editor can actually play, and plays it.
        /// </summary>
        /// <remarks>
        ///     Wraps at both ends, matching the other two transports: a Next that goes dead on the
        ///     last key reads as broken rather than as finished. Keys with no sample are stepped over
        ///     because there is nothing to hear, and so are index-4 keys, because stepping onto one
        ///     would produce silence that reads as a broken player. Clicking one directly still
        ///     selects it, which is how its fields are read.
        /// </remarks>
        /// <param name="direction">-1 for the previous key, 1 for the next.</param>
        private void Step(int direction) {
            if (patches.SelectedRow is not MidiPatchListing listing)
                return;

            int at = keyboard.SelectedKey;
            for (int step = 1; step <= MidiPatchDefinition.Keys; step++) {
                int next = ((at + (direction * step)) % MidiPatchDefinition.Keys +
                            MidiPatchDefinition.Keys) % MidiPatchDefinition.Keys;

                MidiKeySnapshot candidate = listing.Keys[next];
                if (!candidate.Sounds || candidate.SilentHere)
                    continue;

                keyboard.SelectedKey = next;
                PlayKey(next);
                return;
            }

            patches.ReportStatus("Patch " + listing.Id.ToString(CultureInfo.InvariantCulture) +
                                 " has no key this editor can play.");
        }

        /// <summary>
        ///     Puts the transport into the state the playback is actually in.
        /// </summary>
        /// <remarks>
        ///     One method rather than the conditions rebuilt at each site that changes them, which is
        ///     the mistake the Tracks panel made three times over before it was collapsed.
        /// </remarks>
        private void UpdateTransport() {
            TrackPlayback? running = playing;
            bool held = running != null && running.IsPaused;
            bool live = running != null && running.IsPlaying;

            //Never disabled while a patch is selected: Step wraps, so there is always somewhere to go.
            previousButton.Enabled = patches.SelectedRow is MidiPatchListing;
            nextButton.Enabled = previousButton.Enabled;

            MidiKeySnapshot? selected = keyboard.SelectedSnapshot();
            bool startable = cache != null && selected.HasValue && selected.Value.Sounds;

            playPauseButton.Enabled = live || startable;

            /* Pause only while it is actually sounding. A held note shows Play, because that is what
               the next click does - the icon states the action, not the state. */
            bool showPause = live && !held;
            playPauseButton.Icon = showPause ? EditorIcon.Pause : EditorIcon.Play;
            playPauseButton.Describe(showPause
                ? "Pause, keeping the voice and the queued audio"
                : held ? "Resume" : "Play the selected key");
        }

        /// <summary>
        ///     Strikes one key of the selected patch.
        /// </summary>
        /// <remarks>
        ///     Anything already sounding is stopped first, so clicking along a keyboard plays each key
        ///     rather than layering them, and so that two <c>waveOut</c> devices are never open at
        ///     once.
        /// </remarks>
        /// <param name="key">The key to strike.</param>
        private void PlayKey(int key) {
            playing?.Dispose();
            playing = null;
            UpdateTransport();

            if (cache == null || patches.SelectedRow is not MidiPatchListing listing ||
                key < 0 || key >= MidiPatchDefinition.Keys)
                return;

            MidiKeySnapshot snapshot = listing.Keys[key];
            string where = "patch " + listing.Id.ToString(CultureInfo.InvariantCulture) + " key " +
                           key.ToString(CultureInfo.InvariantCulture) + " (" +
                           GeneralMidi.KeyLabel(listing.Id, key) + ")";

            if (!snapshot.Sounds) {
                patches.ReportStatus(where + " names no sample, so there is nothing to play.");
                return;
            }

            if (snapshot.SilentHere) {
                patches.ReportStatus(where + " plays index-4 sample " +
                                     snapshot.SampleId.ToString(CultureInfo.InvariantCulture) +
                                     ", which this editor decodes and cannot render, so it is silent here.");
                return;
            }

            try {
                /* A fresh TrackPlayback builds its own MidiSoundBank, so every click re-decodes the
                   patch and the key's Vorbis sample rather than reusing a decode from the click
                   before. That is a real cost - a sample is up to a couple of hundred kilobytes of
                   PCM - and it is taken deliberately: a bank kept across playbacks would be read by
                   the outgoing playback's thread while the incoming one used it, and Dispose joins
                   with a timeout rather than a guarantee. Correctness over a warm cache until
                   TrackPlayback can be handed a bank it does not own. */
                byte[] midi = MidiKeyPreview.BuildSingleNote(listing.Id, key);
                var started = new TrackPlayback(midi, cache, loop: false);

                /* Subscribed because nothing else reports a device failure: the playback thread
                   raises it and a tab that ignored it would leave the status line reading "playing"
                   while the machine stayed silent. */
                started.Failed += error => ReportFromPlaybackThread(
                    where + " could not be played: " + error.Message);

                //Both ends of a playback put the transport back to Play. Marshalled, because they are
                //raised on the playback thread and these are controls.
                started.Failed += _ => RefreshTransportFromPlaybackThread();
                started.Completed += RefreshTransportFromPlaybackThread;

                playing = started;
                UpdateTransport();
                patches.ReportStatus("Playing " + where + ": index-14 sample " +
                                     snapshot.SampleId.ToString(CultureInfo.InvariantCulture) +
                                     ", volume " + snapshot.Volume.ToString(CultureInfo.InvariantCulture) +
                                     ", pan " + snapshot.Pan.ToString(CultureInfo.InvariantCulture) +
                                     " of 128" + (snapshot.Held ? ", held until released" : string.Empty));
            }
            catch (Exception ex) {
                //Reported rather than thrown: this runs from a click on a keyboard, and an exception
                //out of it takes the form down over one key that will not play.
                patches.ReportStatus(where + " could not be played: " + ex.Message);
                Debug("MIDI patch preview failed: " + ex);
            }
        }

        /// <summary>Puts the transport back to Play when a playback ends on its own.</summary>
        /// <remarks>
        ///     Marshalled rather than called directly, and dropped outright once the handle has gone:
        ///     a device that fails while the tab is closing must not take the form down with it.
        /// </remarks>
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

        /// <summary>Puts a message on the status line from the playback thread.</summary>
        /// <param name="message">The message.</param>
        private void ReportFromPlaybackThread(string message) {
            if (IsDisposed || !IsHandleCreated)
                return;

            try {
                BeginInvoke(new Action(() => patches.ReportStatus(message)));
            }
            catch (ObjectDisposedException) {
                //The panel went away between the check and the post. Nothing to report to.
            }
        }

        // ===================================================================
        //  Selection
        // ===================================================================

        /// <summary>Puts a patch on the keyboard and in the patch detail pane.</summary>
        /// <param name="listing">The selected patch, or null.</param>
        private void ShowPatch(MidiPatchListing? listing) {
            patchFields.ShowFields(listing);
            keyboard.Bind(listing);

            //Bind moves the selection, which raises SelectedKeyChanged and fills the key pane; a
            //patch with no sounding key at all leaves it empty, so it is cleared here as well.
            ShowKey();
            UpdateTransport();
        }

        /// <summary>Fills the key detail pane from whatever the keyboard has selected.</summary>
        private void ShowKey() {
            MidiKeySnapshot? selected = keyboard.SelectedSnapshot();

            if (patches.SelectedRow is not MidiPatchListing listing || !selected.HasValue) {
                keyFields.ClearObjects();
                keyHeader.Text = cache == null ? NoCacheText : NoSelectionText;
                return;
            }

            var detail = new MidiKeyDetail(listing, selected.Value);
            keyFields.ShowFields(detail);
            keyHeader.Text = detail.Summary;
        }

        /// <summary>
        ///     Counts what the loaded cache's patch bank actually holds, once the rows are in.
        /// </summary>
        /// <remarks>
        ///     <b>Measured rather than written down.</b> The two caches disagree on eleven indexes and
        ///     a figure typed into a label would belong to whichever one it was measured on, so this
        ///     is derived from the rows on screen. Only the patches that carry an index-4 key are
        ///     expanded key by key, because that is the only figure the row census cannot already
        ///     answer and there are very few of them.
        /// </remarks>
        private void ShowCensus() {
            if (cache == null) {
                census.Text = NoCacheText;
                return;
            }

            int patchCount = 0;
            int sounding = 0;
            int vorbis = 0;
            int effects = 0;
            int effectPatches = 0;
            var effectSamples = new SortedSet<int>();

            foreach (object row in patches.Rows) {
                if (row is not MidiPatchListing listing)
                    continue;

                patchCount++;
                sounding += listing.SoundingKeys;
                vorbis += listing.VorbisKeys;
                effects += listing.EffectKeys;

                if (listing.EffectKeys == 0)
                    continue;

                effectPatches++;
                foreach (MidiKeySnapshot key in listing.Keys)
                    if (key.SilentHere)
                        effectSamples.Add(key.SampleId);
            }

            census.Text =
                patchCount.ToString("N0", CultureInfo.InvariantCulture) + " patches, " +
                sounding.ToString("N0", CultureInfo.InvariantCulture) + " sounding keys: " +
                vorbis.ToString("N0", CultureInfo.InvariantCulture) + " on index 14 and " +
                effects.ToString("N0", CultureInfo.InvariantCulture) + " on index 4, the latter across " +
                effectPatches.ToString("N0", CultureInfo.InvariantCulture) + " patches and " +
                effectSamples.Count.ToString("N0", CultureInfo.InvariantCulture) +
                " distinct samples. Measured from the loaded cache, not written down.";
        }

        // ===================================================================
        //  Layout
        // ===================================================================

        private void BuildLayout() {
            var keyboardHost = new Panel { Dock = DockStyle.Fill };
            keyboardHost.Controls.Add(keyboard);
            keyboardHost.Controls.Add(keyHeader);

            keyAndPatchFields.Panel1.Controls.Add(keyFields);
            keyAndPatchFields.Panel2.Controls.Add(patchFields);

            keyboardAndFields.Panel1.Controls.Add(keyboardHost);
            keyboardAndFields.Panel2.Controls.Add(keyAndPatchFields);

            listAndPatch.Panel1.Controls.Add(patches);
            listAndPatch.Panel2.Controls.Add(keyboardAndFields);

            /* The transport sits above the notes it qualifies, so the button is beside the sentences
               explaining what pressing it does and does not do. */
            var transportStrip = new FlowLayoutPanel {
                Dock = DockStyle.Top,
                FlowDirection = FlowDirection.LeftToRight,
                AutoSize = true,
                AutoSizeMode = AutoSizeMode.GrowAndShrink,
                WrapContents = false
            };
            transportStrip.Controls.Add(transport);

            //Docking resolves from the end of the Controls collection backwards, so the strips have to
            //be added after the filled splitter, and in bottom-to-top order among themselves.
            Controls.Add(listAndPatch);
            Controls.Add(notes);
            Controls.Add(census);
            Controls.Add(transportStrip);

            //Bound before any cache arrives so the list has its headings from the start.
            patches.Bind(null, descriptor);
        }

        /// <summary>
        ///     Lets the docked notes wrap at the width the panel actually has.
        /// </summary>
        /// <remarks>
        ///     An <c>AutoSize</c> label grows sideways rather than wrapping, so the notes would run
        ///     off the right edge on a narrow window and take their last sentence with them. Capping
        ///     the width at the client area turns the same auto-sizing into height. Measured from the
        ///     panel rather than stated as a pixel count.
        /// </remarks>
        private void WrapNotes() {
            int available = ClientSize.Width;
            if (available <= 0)
                return;

            //Zero height means "no cap on the height", which is the point: the label grows downwards
            //by however many lines the wrapped text needs.
            if (notes.MaximumSize.Width != available)
                notes.MaximumSize = new Size(available, 0);
            if (census.MaximumSize.Width != available)
                census.MaximumSize = new Size(available, 0);
        }

        /// <summary>Divides the panel proportionally, once each, when there is a size worth dividing.</summary>
        /// <remarks>
        ///     A <see cref="SplitContainer"/> defaults to a splitter distance of 50 <i>pixels</i>, not
        ///     half, so the distance has to be stated - and stating it in a field initialiser would
        ///     set it against the container's 150x100 default rather than against its real size.
        /// </remarks>
        private void PlaceSplitters() {
            Place(listAndPatch, ref listSplitterPlaced, listAndPatch.Height >= 200,
                listAndPatch.Height * 2 / 5, "patch list");
            Place(keyboardAndFields, ref detailSplitterPlaced, keyboardAndFields.Height >= 200,
                keyboardAndFields.Height * 2 / 5, "keyboard");
            Place(keyAndPatchFields, ref fieldsSplitterPlaced, keyAndPatchFields.Width >= 200,
                keyAndPatchFields.Width / 2, "detail");
        }

        /// <summary>Sets one splitter's distance, once.</summary>
        /// <param name="container">The container to divide.</param>
        /// <param name="placed">Whether it has already been divided; set before the distance is.</param>
        /// <param name="ready">Whether it is big enough to divide.</param>
        /// <param name="distance">Where to put the splitter.</param>
        /// <param name="name">The container's name, for the log.</param>
        private static void Place(SplitContainer container, ref bool placed, bool ready, int distance,
            string name) {
            if (placed || !ready)
                return;

            /* Flagged before the assignment, not after: changing a splitter distance lays the panel
               out again and re-enters this method, so a flag set afterwards would recurse. */
            placed = true;
            try {
                container.SplitterDistance = Math.Max(container.Panel1MinSize, distance);
            }
            catch (InvalidOperationException ex) {
                placed = false;
                Debug("MIDI patch tab " + name + " splitter not placed yet: " + ex.Message,
                    LOG_DETAIL.ADVANCED);
            }
        }
    }
}
