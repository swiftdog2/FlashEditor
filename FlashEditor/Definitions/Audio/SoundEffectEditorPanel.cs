using BrightIdeasSoftware;
using FlashEditor.Cache;
using FlashEditor.Definitions.Editing;
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;
using static FlashEditor.Utils.DebugUtil;

namespace FlashEditor.Definitions.Audio {
    /// <summary>
    ///     The Sound tab: index 4 from a list of effects down to the breakpoints of one envelope.
    /// </summary>
    /// <remarks>
    ///     A sound effect is a synthesiser patch nested three deep - ten tone slots, each with up to
    ///     eight envelopes and a biquad cascade - so a flat grid can only show counts. The list is
    ///     that flat view and is worth having; the three grids beside it are what makes the counts
    ///     readable.
    ///     <para>
    ///     <b>Four levels, one of which <see cref="DefinitionListPanel"/> owns.</b> That panel cannot
    ///     express master/detail, but it raises
    ///     <see cref="DefinitionListPanel.SelectedRowChanged"/> for exactly this, so the effect list
    ///     is the panel driven by <see cref="SoundEffectListDescriptor"/> and the tone, envelope and
    ///     pole grids are this control's. None of the three enumerates an index, so none is a
    ///     descriptor's job.
    ///     </para>
    ///     <para>
    ///     <b>The detail is read only; the loop window is not.</b> Loop start and loop end are two
    ///     independent milliseconds that nothing else in the record depends on, so the descriptor
    ///     makes those two cells editable and <c>DefinitionListPanel</c> commits them. Everything
    ///     below a tone is shown rather than edited because the fields that look most editable are
    ///     the ones that are structure: an envelope's form byte is the marker that says its tone or
    ///     modulator exists at all, and equalising a filter's two gains deletes its sweep envelope
    ///     and changes the record's length (<c>Class182.java:61-63</c>). The codec refuses all three
    ///     rather than normalising them, which is what a per-field editor here would need to surface.
    ///     </para>
    ///     <para>
    ///     <b>No playback.</b> The synthesiser is out of scope, so nothing here renders audio.
    ///     </para>
    /// </remarks>
    public sealed class SoundEffectEditorPanel : UserControl {
        /// <summary>
        ///     The descriptor the effect list is driven by.
        /// </summary>
        /// <remarks>
        ///     One instance, held rather than built per bind, because <c>DefinitionListPanel.Bind</c>
        ///     treats a different descriptor as a different thing to show and would decode all 10,237
        ///     records again on every visit to the tab.
        /// </remarks>
        private static readonly IDefinitionListDescriptor Effects = new SoundEffectListDescriptor();

        /* Consolas 9 on every child. The form puts Consolas 12 on the tab control and everything
           under it inherits, which is half again what these grids are laid out for. */
        private static readonly Font GridFont = new Font("Consolas", 9F);

        private readonly DefinitionListPanel effects = new DefinitionListPanel();

        private readonly FastObjectListView tones = Grid();
        private readonly FastObjectListView envelopes = Grid();
        private readonly FastObjectListView poles = Grid();

        //AutoSize rather than a stated height, so the line the summary needs is the line it gets
        //whatever font the form ends up scaling to.
        private readonly Label header = new Label {
            AutoSize = true,
            Dock = DockStyle.Top,
            Font = GridFont,
            Text = NoCacheText
        };

        /* No splitter states a minimum size. Setting one re-checks the current distance against it,
           and a container is still at its 150x100 default when a field initialiser runs, so a
           minimum wide enough to be useful throws before the panel has ever been shown. */
        private readonly SplitContainer listAndDetail = new SplitContainer {
            Dock = DockStyle.Fill,
            Orientation = Orientation.Vertical
        };

        private readonly SplitContainer tonesAndParts = new SplitContainer {
            Dock = DockStyle.Fill,
            Orientation = Orientation.Horizontal
        };

        private readonly SplitContainer envelopesAndPoles = new SplitContainer {
            Dock = DockStyle.Fill,
            Orientation = Orientation.Horizontal
        };

        private const string NoCacheText = "No cache loaded";
        private const string NoSelectionText = "Select a sound effect to see its tones";

        private RSCache? cache;
        private bool splittersPlaced;

        /// <summary>Creates the panel.</summary>
        public SoundEffectEditorPanel() {
            Dock = DockStyle.Fill;
            BuildLayout();

            effects.SelectedRowChanged += (_, _) => ShowEffect(effects.SelectedRow as SoundEffectListing);
            tones.SelectedIndexChanged += (_, _) => ShowTone(tones.SelectedObject as ToneListing);
        }

        /// <summary>
        ///     Points the panel at a cache, or clears it when given none.
        /// </summary>
        /// <remarks>
        ///     <c>Editor.LoadEditorTab</c> calls this on every visit to the tab, so a rebind of the
        ///     cache already on display has to be a no-op - the effect sweep decodes every group in
        ///     index 4 and doing it again would also throw away the selection. Identity is the right
        ///     test because opening a cache builds a new <see cref="RSCache"/>.
        /// </remarks>
        /// <param name="newCache">The open cache, or null to unbind.</param>
        public void Bind(RSCache? newCache) {
            if (ReferenceEquals(newCache, cache))
                return;

            cache = newCache;
            tones.ClearObjects();
            envelopes.ClearObjects();
            poles.ClearObjects();
            header.Text = newCache == null ? NoCacheText : NoSelectionText;

            //The descriptor is passed either way. DefinitionListPanel only requires one alongside a
            //non-null cache, and keeping it constant means the columns survive an unbind.
            effects.Bind(newCache, Effects);
        }

        /// <summary>Places the splitters once the layout pass has given the containers a real size.</summary>
        /// <param name="levent">The event data.</param>
        protected override void OnLayout(LayoutEventArgs levent) {
            base.OnLayout(levent);
            PlaceSplitters();
        }

        /// <summary>
        ///     Divides the panel proportionally, once, when it first has a size worth dividing.
        /// </summary>
        /// <remarks>
        ///     A <see cref="SplitContainer"/> defaults to a splitter distance of 50 <i>pixels</i>, not
        ///     half, so the distance has to be stated - and stating it in the designer would make it
        ///     one more literal the form multiplies by its font ratio. A fraction of the measured size
        ///     is the same division at any font or DPI.
        ///     <para>
        ///     Deferred to layout rather than the constructor because assigning a distance the control
        ///     is not yet large enough for throws, and a field initialiser runs while the container is
        ///     still 150x100. Once only, so a user who drags a splitter keeps where they put it.
        ///     </para>
        /// </remarks>
        private void PlaceSplitters() {
            if (splittersPlaced || listAndDetail.Width < 200 || tonesAndParts.Height < 200 ||
                envelopesAndPoles.Height < 120)
                return;

            //Set before the assignments, not after: changing a splitter distance lays the panel out
            //again, and this is called from that layout.
            splittersPlaced = true;

            try {
                listAndDetail.SplitterDistance = Math.Max(listAndDetail.Panel1MinSize, listAndDetail.Width * 2 / 5);
                tonesAndParts.SplitterDistance =
                    Math.Max(tonesAndParts.Panel1MinSize, tonesAndParts.Height * 2 / 5);
                envelopesAndPoles.SplitterDistance =
                    Math.Max(envelopesAndPoles.Panel1MinSize, envelopesAndPoles.Height * 3 / 5);
            } catch (InvalidOperationException ex) {
                //Left for the next layout rather than clamped. A clamped distance sticks, and the
                //user would see a collapsed pane on a window that later has room for all four grids.
                splittersPlaced = false;
                Debug("Sound tab splitters not placed yet: " + ex.Message, LOG_DETAIL.ADVANCED);
            }
        }

        private void BuildLayout() {
            BuildToneColumns();
            BuildEnvelopeColumns();
            BuildPoleColumns();

            envelopesAndPoles.Panel1.Controls.Add(envelopes);
            envelopesAndPoles.Panel2.Controls.Add(poles);

            tonesAndParts.Panel1.Controls.Add(tones);
            tonesAndParts.Panel2.Controls.Add(envelopesAndPoles);

            //Docking resolves from the end of the Controls collection backwards, so the header has to
            //be added after the filled splitter or the splitter claims the whole panel.
            listAndDetail.Panel1.Controls.Add(effects);
            listAndDetail.Panel2.Controls.Add(tonesAndParts);
            listAndDetail.Panel2.Controls.Add(header);

            Controls.Add(listAndDetail);
        }

        private void BuildToneColumns() {
            AddColumn(tones, "Slot", 50, row => Tone(row).Slot);
            AddColumn(tones, "Waveform", 100, row => Tone(row).Waveform);
            AddColumn(tones, "Pitch Hz", 130, row => Tone(row).PitchSweep);
            AddColumn(tones, "Volume", 100, row => Tone(row).VolumeForm);
            AddColumn(tones, "Partials", 80, row => Tone(row).HarmonicCount);
            AddColumn(tones, "Modulation", 190, row => Tone(row).Modulation);
            AddColumn(tones, "Delay ms", 90, row => Tone(row).DelayTime);
            AddColumn(tones, "Feedback %", 100, row => Tone(row).DelayFeedback);
            AddColumn(tones, "Duration ms", 110, row => Tone(row).Duration);
            AddColumn(tones, "Offset ms", 90, row => Tone(row).Offset);
            AddColumn(tones, "Poles", 70, row => Tone(row).Poles);
            AddColumn(tones, "Gain", 110, row => Tone(row).Gain);
            AddColumn(tones, "Sweep", 70, row => Tone(row).SweepPoints);
            AddColumn(tones, "637 client", 100, row => Tone(row).ClientLimit);
        }

        private void BuildEnvelopeColumns() {
            AddColumn(envelopes, "Envelope", 150, row => Envelope(row).Role);
            AddColumn(envelopes, "Form", 100, row => Envelope(row).Form);
            AddColumn(envelopes, "Start Hz", 90, row => Envelope(row).Start);
            AddColumn(envelopes, "End Hz", 90, row => Envelope(row).End);
            AddColumn(envelopes, "Points", 70, row => Envelope(row).Points);
            AddColumn(envelopes, "Breakpoints (position of 65536 : value)", 500, row => Envelope(row).Shape);
        }

        private void BuildPoleColumns() {
            AddColumn(poles, "Set", 130, row => Pole(row).Set);
            AddColumn(poles, "Pole", 60, row => Pole(row).Index);
            AddColumn(poles, "Frequency", 100, row => Pole(row).StartFrequency);
            AddColumn(poles, "Range", 90, row => Pole(row).StartRange);
            AddColumn(poles, "Swept to", 100, row => Pole(row).EndFrequency);
            AddColumn(poles, "Swept range", 110, row => Pole(row).EndRange);
            AddColumn(poles, "Stored", 90, row => Pole(row).Interpolated);
        }

        /// <summary>One detail grid, laid out the same way as every other.</summary>
        /// <returns>The grid.</returns>
        private static FastObjectListView Grid() {
            return new FastObjectListView {
                Dock = DockStyle.Fill,
                Font = GridFont,
                FullRowSelect = true,
                GridLines = true,
                ShowGroups = false,
                UseFiltering = true,
                View = View.Details
            };
        }

        /// <summary>
        ///     Adds one column, reading its value through a delegate rather than an aspect name.
        /// </summary>
        /// <remarks>
        ///     Same reasoning as <see cref="DefinitionColumn"/>: a name looked up by reflection blanks
        ///     the column when the property is renamed, where a delegate stops compiling.
        /// </remarks>
        /// <param name="list">The grid to add to.</param>
        /// <param name="heading">The column heading.</param>
        /// <param name="width">The column width, in the grid's own pinned font.</param>
        /// <param name="read">Reads the displayed value off a row.</param>
        private static void AddColumn(FastObjectListView list, string heading, int width, Func<object, object?> read) {
            //Delegated so the null-row guard has one implementation. Ten copies of this method
            //existed and not one of them had it, which is how closing a cache crashed the
            //interfaces list.
            DetailGrid.AddColumn(list, heading, width, read);
        }

        private static ToneListing Tone(object row) {
            return (ToneListing) row;
        }

        private static EnvelopeListing Envelope(object row) {
            return (EnvelopeListing) row;
        }

        private static PoleListing Pole(object row) {
            return (PoleListing) row;
        }

        /// <summary>
        ///     Fills the tone grid from the selected effect.
        /// </summary>
        /// <remarks>
        ///     No cache read at all: the list row already carries the whole decoded record, because
        ///     one effect is one file and the descriptor decoded it to build the row.
        ///     <para>
        ///     Occupied slots only. An empty slot is a single zero byte and holds nothing to show, so
        ///     the slot column is what makes a gap visible - and a gap is real structure rather than a
        ///     rendering detail, for the reason <see cref="SoundEffectDefinition.Tones"/> gives.
        ///     </para>
        /// </remarks>
        /// <param name="effect">The selected effect, or null.</param>
        private void ShowEffect(SoundEffectListing? effect) {
            tones.ClearObjects();
            envelopes.ClearObjects();
            poles.ClearObjects();

            if (effect == null) {
                header.Text = cache == null ? NoCacheText : NoSelectionText;
                return;
            }

            header.Text = Describe(effect);

            var rows = new List<ToneListing>(effect.Tones);
            foreach (int slot in effect.Effect.OccupiedSlots) {
                SoundEffectTone? tone = effect.Effect.Tones[slot];
                if (tone != null)
                    rows.Add(new ToneListing(slot, tone));
            }

            tones.SetObjects(rows);
        }

        /// <summary>
        ///     Fills the envelope and pole grids from the selected tone.
        /// </summary>
        /// <remarks>
        ///     Every envelope the tone carries, in the order the record stores them, because their
        ///     order on the wire is what tells the three modulator slots apart - the synthesiser adds
        ///     the first to the pitch (<c>Class344.java:189</c>), scales the volume by the second
        ///     (<c>:195</c>) and chops the finished samples with the third (<c>:209-235</c>).
        /// </remarks>
        /// <param name="tone">The selected tone, or null.</param>
        private void ShowTone(ToneListing? tone) {
            envelopes.ClearObjects();
            poles.ClearObjects();

            if (tone == null)
                return;

            envelopes.SetObjects(EnvelopesOf(tone.Tone).ToList());
            poles.SetObjects(PolesOf(tone.Tone.Filter).ToList());
        }

        /// <summary>Every envelope one tone stores, in stream order.</summary>
        /// <param name="tone">The tone.</param>
        /// <returns>The envelope rows.</returns>
        private static IEnumerable<EnvelopeListing> EnvelopesOf(SoundEffectTone tone) {
            yield return new EnvelopeListing("pitch", tone.Pitch, true);
            yield return new EnvelopeListing("volume", tone.Volume, true);

            foreach (EnvelopeListing row in Pair("vibrato", tone.PitchModulation))
                yield return row;
            foreach (EnvelopeListing row in Pair("tremolo", tone.VolumeModulation))
                yield return row;
            foreach (EnvelopeListing row in Pair("gate", tone.Gate))
                yield return row;

            //Shape only. The filter's envelope is read by Class209.method2772 (Class182.java:62), so
            //its form and range are never on the wire and would be three zeroes invented here.
            SoundEffectEnvelope? sweep = tone.Filter.Sweep;
            if (sweep != null)
                yield return new EnvelopeListing("filter sweep", sweep, false);
        }

        private static IEnumerable<EnvelopeListing> Pair(string role, SoundEffectModulator? modulator) {
            if (modulator == null)
                yield break;

            yield return new EnvelopeListing(role + " rate", modulator.Rate, true);
            yield return new EnvelopeListing(role + " depth", modulator.Depth, true);
        }

        /// <summary>Every pole of both coefficient sets, or nothing when the filter is absent.</summary>
        /// <param name="filter">The tone's filter.</param>
        /// <returns>The pole rows.</returns>
        private static IEnumerable<PoleListing> PolesOf(SoundEffectFilter filter) {
            for (int set = 0; set < SoundEffectFilter.Sets; set++)
                for (int pole = 0; pole < filter.PoleCount(set); pole++)
                    yield return new PoleListing(filter, set, pole);
        }

        /// <summary>
        ///     The line above the detail grids: what the selected effect is made of.
        /// </summary>
        /// <remarks>
        ///     Deliberately carries nothing the loop cells can edit. The list refreshes its own row on
        ///     an edit and this label is not its to refresh, so a loop window shown here would go
        ///     stale the moment someone changed one; the list's own <c>Loop from</c>, <c>Loop to</c>
        ///     and <c>Loops</c> columns say it instead, and every figure here is read only.
        /// </remarks>
        /// <param name="effect">The selected effect.</param>
        /// <returns>The summary line.</returns>
        private static string Describe(SoundEffectListing effect) {
            string slots = effect.Tones == 0 ? "no tones" : effect.Tones + " tones in slots " + effect.Slots;

            string text = "Effect " + effect.EffectId + " - " + slots + " - " + effect.Harmonics +
                          " partials - " + effect.Filters + " filters - " + effect.LengthMs + " ms - " +
                          effect.SizeBytes + " bytes";

            /* Two client arrays are narrower than the format can express: three harmonic arrays of
               five (Class344.java:71-74) against a read loop of ten, and coefficient arrays of
               [2][2][4] (Class182.java:25-27) against a pole count that comes off a nibble. A file
               past either bound is well formed and still crashes the 637 client, and nothing shipped
               reaches them, so this reports an edit rather than the cache. */
            if (effect.Effect.ExceedsClientLimits)
                text += " - EXCEEDS 637 CLIENT LIMITS";

            return text;
        }

        /// <summary>
        ///     The waveform a form byte selects.
        /// </summary>
        /// <remarks>
        ///     <c>Class344.method3821</c> (<c>Class344.java:126-144</c>): 1 flips sign on the half
        ///     phase, 2 reads the sine table, 3 is a rising ramp minus its amplitude and 4 reads the
        ///     noise table. Anything else returns 0, so it is silent - and both 0 and 5 occur here,
        ///     which is why the stored number is shown beside the name rather than replaced by it.
        /// </remarks>
        /// <param name="form">The stored form byte.</param>
        /// <returns>The form and what the synthesiser makes of it.</returns>
        private static string Waveform(int form) {
            switch (form) {
                case 1:
                    return form + " square";
                case 2:
                    return form + " sine";
                case 3:
                    return form + " saw";
                case 4:
                    return form + " noise";
                default:
                    return form + " silent";
            }
        }

        /// <summary>One occupied tone slot of the selected effect, as a grid row.</summary>
        private sealed class ToneListing {
            internal ToneListing(int slot, SoundEffectTone tone) {
                Slot = slot;
                Tone = tone;
            }

            /// <summary>The slot the tone sits in, which is its position in the record.</summary>
            internal int Slot { get; }

            /// <summary>The decoded tone.</summary>
            internal SoundEffectTone Tone { get; }

            /// <summary>
            ///     The waveform the oscillator runs, which is the <i>pitch</i> envelope's form.
            /// </summary>
            /// <remarks>
            ///     <c>Class344.java:202-203</c> passes <c>aClass209_2880.anInt1584</c> - the pitch
            ///     envelope's form - as the waveform selector for every partial. The volume envelope's
            ///     form is read too but drives nothing, which is why the two are separate columns.
            /// </remarks>
            internal string Waveform => SoundEffectEditorPanel.Waveform(Tone.Pitch.Form);

            /// <summary>The frequency the tone starts and ends at, in Hz.</summary>
            internal string PitchSweep => Tone.Pitch.Start + " to " + Tone.Pitch.End;

            /// <summary>The volume envelope's form, which is 0 on almost every tone in this cache.</summary>
            internal string VolumeForm => SoundEffectEditorPanel.Waveform(Tone.Volume.Form);

            /// <summary>How many partials the tone mixes.</summary>
            internal int HarmonicCount => Tone.Harmonics.Count;

            /// <summary>Which of the three optional modulator slots the tone carries.</summary>
            /// <remarks>
            ///     Named by what the synthesiser does with each rather than by position: vibrato is
            ///     added to the pitch, tremolo scales the volume, and the gate blanks alternating
            ///     spans of the finished samples.
            /// </remarks>
            internal string Modulation {
                get {
                    var present = new List<string>(3);
                    if (Tone.PitchModulation != null)
                        present.Add("vibrato");
                    if (Tone.VolumeModulation != null)
                        present.Add("tremolo");
                    if (Tone.Gate != null)
                        present.Add("gate");
                    return string.Join(", ", present);
                }
            }

            /// <summary>The delay line's tap, in milliseconds.</summary>
            internal int DelayTime => Tone.DelayTime;

            /// <summary>How much of the delayed signal is fed back, as a percentage.</summary>
            internal int DelayFeedback => Tone.DelayFeedback;

            /// <summary>How long the tone sounds, in milliseconds.</summary>
            internal int Duration => Tone.Duration;

            /// <summary>How far into the effect the tone starts, in milliseconds.</summary>
            internal int Offset => Tone.Offset;

            /// <summary>The two sets' pole counts, or blank when the tone has no filter.</summary>
            /// <remarks>Feed-forward and then feedback, which is the order the packed byte states them.</remarks>
            internal string Poles => Tone.Filter.IsPresent
                ? Tone.Filter.PoleCount(0) + " + " + Tone.Filter.PoleCount(1)
                : string.Empty;

            /// <summary>The filter's gain at the start and end of the tone, or blank when it has none.</summary>
            internal string Gain => Tone.Filter.IsPresent
                ? Tone.Filter.Gain(0) + " to " + Tone.Filter.Gain(1)
                : string.Empty;

            /// <summary>How many breakpoints sweep the filter, or blank when nothing does.</summary>
            internal object? SweepPoints => Tone.Filter.Sweep?.Segments.Count;

            /// <summary>Whether this tone is shaped in a way the 637 client cannot load.</summary>
            /// <remarks>Blank for every tone in this cache; it is here to catch an edit, not the data.</remarks>
            internal string ClientLimit => Tone.ExceedsClientLimits ? "over limit" : string.Empty;
        }

        /// <summary>One envelope of the selected tone, as a grid row.</summary>
        /// <remarks>
        ///     <paramref name="hasRange"/> tells the two readers apart. A full envelope carries a form
        ///     byte and a frequency pair; the filter's sweep is read shape-first by
        ///     <c>Class209.method2772</c> and carries neither, so showing zeroes for them would report
        ///     three fields that are not in the file.
        /// </remarks>
        private sealed class EnvelopeListing {
            private readonly SoundEffectEnvelope envelope;
            private readonly bool hasRange;

            internal EnvelopeListing(string role, SoundEffectEnvelope envelope, bool hasRange) {
                Role = role;
                this.envelope = envelope;
                this.hasRange = hasRange;
            }

            /// <summary>What this envelope drives.</summary>
            internal string Role { get; }

            /// <summary>The form byte and the waveform it selects, or blank when it is not on the wire.</summary>
            internal string Form => hasRange ? SoundEffectEditorPanel.Waveform(envelope.Form) : string.Empty;

            /// <summary>The frequency the envelope starts at, or blank when it is not on the wire.</summary>
            internal object? Start => hasRange ? (object) envelope.Start : null;

            /// <summary>The frequency the envelope sweeps to, or blank when it is not on the wire.</summary>
            internal object? End => hasRange ? (object) envelope.End : null;

            /// <summary>How many breakpoints the envelope holds.</summary>
            internal int Points => envelope.Segments.Count;

            /// <summary>
            ///     Every breakpoint, in stream order.
            /// </summary>
            /// <remarks>
            ///     All of them rather than a summary, because a position is an absolute fraction of the
            ///     tone rather than a segment length (<c>Class209.java:44-47</c> compares it against a
            ///     counter that is never reset), so the list only means anything read whole and in
            ///     order.
            /// </remarks>
            internal string Shape =>
                string.Join("  ", envelope.Segments.Select(segment => segment.Position + ":" + segment.Value));
        }

        /// <summary>One pole of the selected tone's filter, at both phases.</summary>
        private sealed class PoleListing {
            private readonly SoundEffectFilter filter;
            private readonly int set;
            private readonly int pole;

            internal PoleListing(SoundEffectFilter filter, int set, int pole) {
                this.filter = filter;
                this.set = set;
                this.pole = pole;
            }

            /// <summary>Which half of the cascade the pole belongs to.</summary>
            /// <remarks>
            ///     Set 0's coefficients accumulate over the input history (<c>Class344.java:255-257</c>)
            ///     and set 1's are subtracted from the output history (<c>:259-261</c>), so the numbers
            ///     mean different things in the two halves.
            /// </remarks>
            internal string Set => set == 0 ? "0 feed-forward" : "1 feedback";

            /// <summary>The pole's position within its set.</summary>
            internal int Index => pole;

            /// <summary>The centre frequency at the start of the tone, in 1/8192ths of an octave.</summary>
            internal int StartFrequency => filter.Frequency(set, 0, pole);

            /// <summary>The resonance at the start of the tone.</summary>
            internal int StartRange => filter.Range(set, 0, pole);

            /// <summary>The centre frequency at the end of the tone.</summary>
            internal int EndFrequency => filter.Frequency(set, 1, pole);

            /// <summary>The resonance at the end of the tone.</summary>
            internal int EndRange => filter.Range(set, 1, pole);

            /// <summary>
            ///     Whether the end-of-tone pair was on the wire or copied from the start.
            /// </summary>
            /// <remarks>
            ///     <c>Class182.java:52</c> spells the bit as <c>1 &lt;&lt; set * 4 &lt;&lt; pole</c>.
            ///     Recomputed from the stored mask rather than by comparing the two phases: the decoder
            ///     copies phase 0 into phase 1 for every clear bit, so a set bit whose two phases hold
            ///     the same numbers is indistinguishable from a clear one by value, and it is the mask
            ///     that decides what the encoder writes.
            /// </remarks>
            internal string Interpolated =>
                (filter.InterpolationMask & (1 << (set * 4 + pole))) != 0 ? "both" : "copied";
        }
    }
}
