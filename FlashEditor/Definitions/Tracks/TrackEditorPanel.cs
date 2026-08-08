using BrightIdeasSoftware;
using FlashEditor.cache;
using FlashEditor.Definitions.Audio.Synth;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Drawing;
using System.IO;
using System.Windows.Forms;
using static FlashEditor.Utils.DebugUtil;

namespace FlashEditor.Definitions.Tracks {
    /// <summary>
    ///     The Tracks tab: every packed music track in the cache, its decoded MIDI statistics, and
    ///     an export.
    /// </summary>
    /// <remarks>
    ///     Built in code rather than through the designer, following <c>MapEditorPanel</c>, so the
    ///     tab drops into a page with one line and does not add to the shared
    ///     <c>Editor.Designer.cs</c>. Loading follows the item, NPC and object tabs instead: a
    ///     <see cref="BackgroundWorker"/> decodes everything with a progress bar, because decoding
    ///     1404 tracks takes a few seconds and the definition tabs already established what that
    ///     should look like.
    ///
    ///     <b>Export is MIDI; replace is not.</b> <see cref="Track"/> now re-encodes the packed format
    ///     byte for byte, so a track can be replaced with the bytes of another packed file - which is
    ///     what "Replace..." does. It deliberately does <b>not</b> accept a MIDI file. The decoder is a
    ///     projection: it takes the packed runs apart into a standard MIDI file and every field of the
    ///     packed form has more than one encoding that projects to the same MIDI, so going the other
    ///     way is a re-authoring problem rather than an inverse, and one this codec does not solve.
    ///     Offering it would produce plausible files that are not what the client reads.
    /// </remarks>
    public sealed class TrackEditorPanel : UserControl {
        /// <summary>
        ///     The indexes listed, in order.
        /// </summary>
        /// <remarks>
        ///     Both hold the same packed format and the client hands both to the same decoder
        ///     (InterfaceSettings.java:164,168 into <c>Node_Sub7.method985</c>), so listing only one
        ///     of them would hide half the tracks in the cache. The Index column tells them apart.
        /// </remarks>
        private static readonly (int IndexId, string Label)[] TrackIndexes = {
            (RSConstants.MUSIC_INDEX, "Music"),
            (RSConstants.MUSIC_2, "Jingles")
        };

        private readonly FastObjectListView list = new FastObjectListView {
            Dock = DockStyle.Fill,
            Font = new Font("Consolas", 9F),
            FullRowSelect = true,
            GridLines = true,
            ShowGroups = false,
            UseFiltering = true,
            View = View.Details
        };

        private readonly TextBox details = new TextBox {
            Dock = DockStyle.Fill,
            Multiline = true,
            ReadOnly = true,
            ScrollBars = ScrollBars.Vertical,
            Font = new Font("Consolas", 9F)
        };

        /* The form sets Consolas 12pt on the tab control and every child inherits it, which is
           half again the size these fixed-height strips were drawn for - the button clipped its
           own caption to "Export MIDT" and the status label lost its descenders. Each control that
           constrains its height states the font it is sized for rather than inheriting one. */
        private readonly Button exportButton = new Button {
            Text = "Export MIDI...",
            Dock = DockStyle.Top,
            Height = 32,
            Font = new Font("Consolas", 9F),
            Enabled = false
        };

        /* Docked Top like the export button and added before it, so the two stack in the order they
           are declared. Disabled until a single row is selected: replacing takes one target, and a
           multi-row selection has no single track to write the chosen file into. */
        private readonly Button replaceButton = new Button {
            Text = "Replace (.dat)...",
            Dock = DockStyle.Top,
            Height = 32,
            Font = new Font("Consolas", 9F),
            Enabled = false
        };

        /* Playback is a different kind of operation from the two above it: it runs until it is
           stopped rather than completing, so the pair is a mode rather than two commands and both
           buttons are present at once with only one of them enabled. */
        private readonly Button playButton = new Button {
            Text = "Play (cache instruments)",
            Dock = DockStyle.Top,
            Height = 32,
            Font = new Font("Consolas", 9F),
            Enabled = false
        };

        private readonly Button stopButton = new Button {
            Text = "Stop",
            Dock = DockStyle.Top,
            Height = 32,
            Font = new Font("Consolas", 9F),
            Enabled = false
        };

        private readonly CheckBox loopCheck = new CheckBox {
            Text = "Loop",
            Dock = DockStyle.Top,
            AutoSize = true,
            Font = new Font("Consolas", 9F)
        };

        /* States what the player does not reproduce, in the view, because a user comparing it
           against the game has no other way to tell a documented choice from a defect. The same
           rule the 3D viewer follows. */
        private readonly Label playerNote = new Label {
            Dock = DockStyle.Top,
            AutoSize = true,
            Font = new Font("Consolas", 8F),
            Text =
                "Playback uses the cache's own patch bank (index 15) and Vorbis samples (index 14),\r\n" +
                "not a General MIDI synth. It is not the client, and diverges on purpose:\r\n" +
                "  - index-4 procedural samples are silent (14 of the bank's 21,491 keys)\r\n" +
                "  - no voice stealing, so dense passages keep notes the client would drop\r\n" +
                "  - no portamento, no CC81 re-trigger, no aftertouch (the client discards that too)\r\n" +
                "Exported MIDI plays on General MIDI instead, so it will not sound like this."
        };

        private readonly ProgressBar progress = new ProgressBar { Dock = DockStyle.Bottom, Height = 20 };

        private readonly Label status = new Label {
            Dock = DockStyle.Bottom,
            Height = 24,
            Font = new Font("Consolas", 9F),
            TextAlign = ContentAlignment.MiddleLeft
        };

        /* The bound cache is the panel's whole identity: it decides whether a rebind is a no-op,
           and a worker compares against it to find out whether its result is still wanted. There
           is deliberately no handle on the worker itself - nothing cancels one, so holding it
           would only invite code that thinks it can. */
        private RSCache? cache;

        /* The running playback, or null. It owns its own thread and device, so the panel's only
           responsibilities are to stop the previous one before starting another and to stop it when
           the cache is rebound or the panel goes away - a player left running against a closed
           cache reads a disposed store on its next sample lookup. */
        private TrackPlayback? playback;

        /// <summary>Creates the panel.</summary>
        public TrackEditorPanel() {
            Dock = DockStyle.Fill;
            BuildLayout();

            list.SelectedIndexChanged += (_, _) => ShowDetails(list.SelectedObject as Track);
            exportButton.Click += (_, _) => ExportSelected();
            replaceButton.Click += (_, _) => ReplaceSelected();
            playButton.Click += (_, _) => PlaySelected();
            stopButton.Click += (_, _) => StopPlayback("Playback stopped");
        }

        /// <summary>Stops playback when the panel goes away.</summary>
        /// <param name="disposing">Whether managed state is being released.</param>
        protected override void Dispose(bool disposing) {
            if (disposing)
                StopPlayback(null);

            base.Dispose(disposing);
        }

        // ===================================================================
        //  Playback
        // ===================================================================

        /// <summary>
        ///     Plays the selected track through the cache's own instruments.
        /// </summary>
        /// <remarks>
        ///     Everything expensive happens on the playback thread: the patches and samples a track
        ///     needs are decoded the first time a note asks for one, and an index-14 sample is a full
        ///     Vorbis decode. Starting on the UI thread would freeze the window for the first bar.
        /// </remarks>
        private void PlaySelected() {
            if (cache == null || list.SelectedObject is not Track track)
                return;

            if (track.Midi == null || track.Midi.Length == 0) {
                status.Text = "Track " + track.Id + " built no MIDI, so there is nothing to play";
                return;
            }

            StopPlayback(null);

            try {
                var started = new TrackPlayback(track.Midi, cache, loopCheck.Checked);
                started.Completed += () => OnPlaybackEnded(started, null);
                started.Failed += error => OnPlaybackEnded(started, error);
                playback = started;
            } catch (Exception ex) {
                status.Text = "Playback failed: " + ex.Message;
                return;
            }

            playButton.Enabled = false;
            stopButton.Enabled = true;
            status.Text = "Playing " + LabelFor(track.IndexId).ToLowerInvariant() + " track " + track.Id +
                          (loopCheck.Checked ? " (looping)" : string.Empty);
        }

        /// <summary>Stops any running playback.</summary>
        /// <param name="message">What to show in the status line, or null to leave it alone.</param>
        private void StopPlayback(string? message) {
            TrackPlayback? running = playback;
            playback = null;
            running?.Dispose();

            if (IsDisposed)
                return;

            playButton.Enabled = cache != null && list.SelectedObjects.Count == 1;
            stopButton.Enabled = false;
            if (message != null)
                status.Text = message;
        }

        /// <summary>
        ///     Returns the panel to its stopped state when a track ends or fails.
        /// </summary>
        /// <remarks>
        ///     Both events arrive on the playback thread, so this marshals before touching a control.
        ///     It also checks that the playback reporting in is still the current one: a track
        ///     stopped and immediately restarted has two threads alive for a moment, and the old
        ///     one's completion must not disable the new one's Stop button.
        /// </remarks>
        /// <param name="source">The playback reporting in.</param>
        /// <param name="error">What went wrong, or null if the track simply ended.</param>
        private void OnPlaybackEnded(TrackPlayback source, Exception? error) {
            if (IsDisposed || !IsHandleCreated)
                return;

            BeginInvoke(new Action(() => {
                if (!ReferenceEquals(playback, source))
                    return;

                playback = null;
                playButton.Enabled = cache != null && list.SelectedObjects.Count == 1;
                stopButton.Enabled = false;
                status.Text = error == null ? "Playback finished" : "Playback failed: " + error.Message;
            }));
        }

        /// <summary>
        ///     Points the panel at a cache and starts decoding, or clears it when given none.
        /// </summary>
        /// <remarks>
        ///     <c>Editor.LoadEditorTab</c> calls this on every visit to the tab, not just the first
        ///     - the map tab is bound the same way and the <c>loaded</c> flags are checked after
        ///     both. Decoding 1404 tracks takes seconds, so re-binding the cache already on display
        ///     has to be a no-op rather than a second sweep that also throws away the selection.
        ///     Identity is the right test because opening a cache builds a new
        ///     <see cref="RSCache"/>, which is exactly when the list is stale.
        /// </remarks>
        /// <param name="newCache">The open cache, or null.</param>
        public void Bind(RSCache? newCache) {
            if (ReferenceEquals(newCache, cache))
                return;

            /* Before the field moves: a player left running against the old cache reads a store
               that is about to be closed the next time a note asks for a sample it has not decoded
               yet. */
            StopPlayback(null);

            cache = newCache;
            list.ClearObjects();
            details.Clear();
            exportButton.Enabled = false;
            replaceButton.Enabled = false;
            playButton.Enabled = false;

            if (cache == null) {
                status.Text = "No cache loaded";
                progress.Value = 0;
                return;
            }

            //A worker from the previous cache is left to finish; RunWorkerCompleted below refuses
            //to publish a result that a later Bind has already superseded
            StartLoad();
        }

        private void BuildLayout() {
            list.AllColumns.Add(Column("Index", "IndexId", 70));
            list.AllColumns.Add(Column("ID", "Id", 60));
            list.AllColumns.Add(Column("Name", "Name", 190));
            list.AllColumns.Add(Column("Name hash", "NameHash", 110));
            list.AllColumns.Add(Column("Packed", "PackedLength", 90));
            list.AllColumns.Add(Column("MIDI", "MidiLength", 90));
            list.AllColumns.Add(Column("Tracks", "TrackCount", 70));
            list.AllColumns.Add(Column("Division", "Division", 80));
            list.AllColumns.Add(Column("Tempo", "TempoEvents", 70));
            list.AllColumns.Add(Column("Notes", "NoteOnEvents", 80));
            list.AllColumns.Add(Column("Controls", "ControllerEvents", 90));

            foreach (OLVColumn column in list.AllColumns)
                list.Columns.Add(column);

            var split = new SplitContainer {
                Dock = DockStyle.Fill,
                Orientation = Orientation.Vertical,
                FixedPanel = FixedPanel.Panel2
            };

            var side = new Panel { Dock = DockStyle.Fill };
            var detailGroup = new GroupBox { Text = "Track", Dock = DockStyle.Fill };
            detailGroup.Controls.Add(details);
            /* Docked controls are laid out from the last added to the first, so this list reads
               bottom to top: the note sits directly above the Track box and the Play button ends up
               at the top of the strip. */
            side.Controls.Add(detailGroup);
            side.Controls.Add(playerNote);
            side.Controls.Add(exportButton);
            side.Controls.Add(replaceButton);
            side.Controls.Add(loopCheck);
            side.Controls.Add(stopButton);
            side.Controls.Add(playButton);

            split.Panel1.Controls.Add(list);
            split.Panel2.Controls.Add(side);

            Controls.Add(split);
            Controls.Add(status);
            Controls.Add(progress);

            //SplitterDistance is silently clamped until the control has a size
            split.HandleCreated += (_, _) => split.SplitterDistance = Math.Max(200, split.Width - 340);

            status.Text = "No cache loaded";
        }

        private static OLVColumn Column(string text, string aspect, int width) {
            return new OLVColumn(text, aspect) { Width = width, Groupable = false };
        }

        private void StartLoad() {
            progress.Value = 0;
            status.Text = "Loading tracks";

            //Bind assigns the field before calling here, and never with null
            RSCache open = cache!;
            var worker = new BackgroundWorker { WorkerReportsProgress = true };

            worker.ProgressChanged += (_, e) => {
                //A superseded worker keeps running to completion; its progress is not this list's
                if (!ReferenceEquals(cache, open))
                    return;
                progress.Value = Math.Clamp(e.ProgressPercentage, 0, 100);
                status.Text = e.UserState?.ToString() ?? status.Text;
            };

            worker.DoWork += (_, e) => e.Result = DecodeAll(open, worker);

            worker.RunWorkerCompleted += (_, e) => {
                //Another cache was bound while this ran, so its tracks are no longer what is shown
                if (!ReferenceEquals(cache, open))
                    return;

                if (e.Error != null) {
                    status.Text = "Failed to load tracks: " + e.Error.Message;
                    Debug("Track load failed: " + e.Error);
                    return;
                }

                //DoWork always assigns Result, and the error path returned above
                var tracks = (List<Track>) e.Result!;
                list.SetObjects(tracks);
                progress.Value = 100;
                status.Text = $"{tracks.Count} tracks";
            };

            worker.RunWorkerAsync();
        }

        /// <summary>Decodes every track in every listed index, skipping the ones that will not read.</summary>
        /// <remarks>
        ///     Takes the cache rather than reading the field, so a rebind part way through cannot
        ///     make a single sweep read half of one cache and half of another.
        /// </remarks>
        private static List<Track> DecodeAll(RSCache open, BackgroundWorker worker) {
            var tracks = new List<Track>();
            var groups = new List<(int IndexId, int GroupId)>();
            Dictionary<int, string> names = TrackNames.Load(open);

            foreach ((int indexId, string _) in TrackIndexes) {
                RSReferenceTable table;
                try {
                    table = open.GetReferenceTable(indexId);
                }
                catch (Exception ex) {
                    //An index the cache does not carry is not a defect, it is a different revision
                    Debug($"No reference table for track index {indexId}: {ex.Message}");
                    continue;
                }

                foreach (int groupId in table.GetArchiveEntries().Keys)
                    groups.Add((indexId, groupId));
            }

            int done = 0;
            int percentile = Math.Max(1, groups.Count / 100);

            foreach ((int indexId, int groupId) in groups) {
                try {
                    Track track = open.GetTrack(indexId, groupId);

                    /* Named through the group's own name hash, so a name can only ever be attached
                       to a group whose stored hash it reproduces - see TrackNames. Index 11 carries
                       no identifiers, so every jingle arrives here with -1 and stays unnamed. */
                    if (track.NameHash != -1 && names.TryGetValue(track.NameHash, out string? name))
                        track.Name = name;

                    tracks.Add(track);
                }
                catch (Exception ex) {
                    Debug($"Track {groupId} in index {indexId} failed to decode: {ex.Message}");
                }

                done++;
                if (done % percentile == 0 || done == groups.Count)
                    worker.ReportProgress(done * 100 / groups.Count, $"Decoded {done}/{groups.Count} tracks");
            }

            return tracks;
        }

        private void ShowDetails(Track? track) {
            exportButton.Enabled = list.SelectedObjects.Count > 0;
            replaceButton.Enabled = cache != null && list.SelectedObjects.Count == 1 && track != null;
            playButton.Enabled = cache != null && list.SelectedObjects.Count == 1 && track != null &&
                                 playback == null && track.MidiLength > 0;

            if (track == null) {
                details.Clear();
                return;
            }

            var sb = new System.Text.StringBuilder();
            sb.AppendLine($"{LabelFor(track.IndexId)} track {track.Id}");
            sb.AppendLine($"Name       {(track.Name.Length == 0 ? "unnamed" : track.Name)}");
            sb.AppendLine($"Index      {track.IndexId}");
            sb.AppendLine($"Name hash  {(track.NameHash == -1 ? "none" : track.NameHash.ToString())}");
            sb.AppendLine();
            sb.AppendLine($"Packed     {track.PackedLength:N0} bytes");
            sb.AppendLine($"MIDI       {track.MidiLength:N0} bytes");
            sb.AppendLine($"Format     {(track.TrackCount > 1 ? 1 : 0)}");
            sb.AppendLine($"Tracks     {track.TrackCount}");
            sb.AppendLine($"Division   {track.Division} ticks per quarter note");
            sb.AppendLine();
            sb.AppendLine("Events");
            sb.AppendLine($"  Tempo             {track.TempoEvents:N0}");
            sb.AppendLine($"  Note on           {track.NoteOnEvents:N0}");
            sb.AppendLine($"  Note off          {track.NoteOffEvents:N0}");
            sb.AppendLine($"  Controller        {track.ControllerEvents:N0}");
            sb.AppendLine($"  Pitch wheel       {track.PitchWheelEvents:N0}");
            sb.AppendLine($"  Channel pressure  {track.ChannelAfterTouchEvents:N0}");
            sb.AppendLine($"  Key pressure      {track.KeyAfterTouchEvents:N0}");
            sb.AppendLine($"  Program change    {track.ProgramChangeEvents:N0}");

            /* Track.RepairedMetaStatusBytes is deliberately not shown. Every line above describes
               the music; that one describes this decoder, and the export is already correct either
               way - the byte is written unconditionally so the file plays outside the client. There
               is no reading of the number that leads to a user action, and the earlier wording read
               as a warning about the very defect it had repaired. It stays a decoder property,
               documented on Track.Decode and pinned by RealCacheTrackTests. */

            details.Text = sb.ToString();
        }

        private static string LabelFor(int indexId) {
            foreach ((int id, string label) in TrackIndexes)
                if (id == indexId)
                    return label;
            return "Index " + indexId;
        }

        private void ExportSelected() {
            var selected = new List<(string FileName, byte[] Midi)>();
            foreach (object row in list.SelectedObjects) {
                if (row is not Track track)
                    continue;
                byte[]? midi = track.Midi;
                if (midi != null)
                    selected.Add((FileNameFor(track), midi));
            }

            if (selected.Count == 0)
                return;

            try {
                if (selected.Count == 1) {
                    using var save = new SaveFileDialog {
                        Filter = "MIDI file (*.mid)|*.mid",
                        FileName = selected[0].FileName
                    };
                    if (save.ShowDialog(this) != DialogResult.OK)
                        return;

                    File.WriteAllBytes(save.FileName, selected[0].Midi);
                    status.Text = "Exported " + Path.GetFileName(save.FileName);
                    return;
                }

                using var browse = new FolderBrowserDialog();
                if (browse.ShowDialog(this) != DialogResult.OK)
                    return;

                foreach ((string fileName, byte[] midi) in selected)
                    File.WriteAllBytes(Path.Combine(browse.SelectedPath, fileName), midi);

                status.Text = $"Exported {selected.Count} tracks to {browse.SelectedPath}";
            }
            catch (IOException ex) {
                status.Text = "Export failed: " + ex.Message;
                Debug("Track export failed: " + ex);
            }
            catch (UnauthorizedAccessException ex) {
                status.Text = "Export failed: " + ex.Message;
                Debug("Track export failed: " + ex);
            }
        }

        /// <summary>
        ///     Replaces the selected track's stored file with the bytes of a packed file on disk.
        /// </summary>
        /// <remarks>
        ///     <b>Packed bytes, not MIDI.</b> The picker's filter says so and the decode below is what
        ///     enforces it: a MIDI file starts <c>MThd</c>, and <c>Track.Decode</c> reads the track
        ///     count and division from the <i>last</i> three bytes and then walks an opcode stream the
        ///     client has a case for, so it refuses one long before anything is staged.
        ///     <para>
        ///     The file's own bytes are what gets stored rather than a re-encode of the decoded track.
        ///     They are the same thing for a file this decoder accepts - the codec is a concatenation
        ///     - but writing what was decoded rather than what was read would make the import depend on
        ///     our encoder agreeing with whatever produced the file, and there is no need for it to.
        ///     </para>
        ///     <para>
        ///     Nothing is written when the cache already holds those bytes. The comparison is against
        ///     the <b>decompressed</b> file, which is what <c>RSCache.ReadFileBytes</c> returns: a GZip
        ///     re-encode is never byte-identical in this cache, so comparing containers would report a
        ///     difference for every track and rewrite the group, its CRC and the reference-table entry
        ///     of every group packed beside it.
        ///     </para>
        /// </remarks>
        private void ReplaceSelected() {
            if (cache == null || list.SelectedObjects.Count != 1 || list.SelectedObject is not Track target) {
                status.Text = "Select a single track to replace";
                return;
            }

            using var picker = new OpenFileDialog {
                Title = "Replace " + LabelFor(target.IndexId).ToLowerInvariant() + " track " + target.Id,
                Filter = "Packed track (*.dat)|*.dat|All files (*.*)|*.*"
            };

            if (picker.ShowDialog(this) != DialogResult.OK)
                return;

            try {
                byte[] imported = File.ReadAllBytes(picker.FileName);

                //Decoded to validate. The packed format is self-describing only from its trailer
                //outwards, so "our decoder walks it to the end" is the whole of the check available
                //- and it is worth something, because the decoder throws on an opcode the client has
                //no case for rather than skipping it.
                Track decoded = new Track { Id = target.Id, IndexId = target.IndexId, NameHash = target.NameHash }
                    .Decode(new JagStream(imported));

                int fileId = FileIdOf(cache, target);

                if (cache.ReadFileBytes(target.IndexId, target.Id, fileId).AsSpan().SequenceEqual(imported)) {
                    status.Text = "Track " + target.Id + " already holds those bytes";
                    return;
                }

                cache.WriteFile(target.IndexId, target.Id, fileId, new JagStream(imported));

                decoded.Name = target.Name;
                list.RemoveObject(target);
                list.AddObject(decoded);
                list.SelectedObject = decoded;
                status.Text = "Staged " + imported.Length.ToString("N0") + " bytes into track " + target.Id;
            }
            catch (Exception ex) {
                //Reported rather than thrown: a malformed file must cost the replace and nothing else
                status.Text = "Replace failed: " + ex.Message;
                Debug("Track replace failed: " + ex);
                MessageBox.Show(this,
                    "Could not read that file as a packed track:" + Environment.NewLine + ex.Message,
                    "Replace track", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        /// <summary>
        ///     The file id a track's group holds, read off the reference table rather than assumed.
        /// </summary>
        /// <remarks>
        ///     Every group in both track indexes holds exactly one file, but the id of that file is
        ///     declared rather than derived - <c>CacheAddressing.FileOf</c> refuses to answer for a
        ///     <c>GroupPerId</c> index for exactly this reason, and index 23 is the case that proves
        ///     it is not always 0.
        /// </remarks>
        /// <param name="open">The open cache.</param>
        /// <param name="track">The track being replaced.</param>
        /// <returns>The file id within the track's group.</returns>
        /// <exception cref="InvalidOperationException">The group declares no file.</exception>
        private static int FileIdOf(RSCache open, Track track) {
            int[] fileIds = open.GetFileIds(track.IndexId, track.Id);
            if (fileIds.Length == 0)
                throw new InvalidOperationException(
                    "Index " + track.IndexId + " group " + track.Id + " declares no file to replace.");
            return fileIds[0];
        }

        /// <summary>Builds an export file name that is unique and says what the track is.</summary>
        /// <remarks>
        ///     The index has to be in the name because group ids restart at zero in each of the two
        ///     indexes, so exporting a selection spanning both would otherwise collide.
        /// </remarks>
        private static string FileNameFor(Track track) {
            string name = track.Name;
            foreach (char invalid in Path.GetInvalidFileNameChars())
                name = name.Replace(invalid, '_');

            return name.Length == 0
                ? $"track_{track.IndexId}_{track.Id}.mid"
                : $"track_{track.IndexId}_{track.Id}_{name}.mid";
        }
    }
}
