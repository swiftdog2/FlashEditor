using BrightIdeasSoftware;
using FlashEditor.cache;
using FlashEditor.Definitions.Editing;
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Windows.Forms;
using static FlashEditor.Utils.DebugUtil;

namespace FlashEditor.Definitions.Animation {
    /// <summary>
    ///     The Animations tab: index 20, and the index-0 frames each animation names.
    /// </summary>
    /// <remarks>
    ///     The step grid is the reason this is a panel rather than a bare list. Index 0 carries no
    ///     name hashes, so the packed id inside an animation's opcode 1 is the <b>only</b> statement
    ///     anywhere in the cache of which frame set plays for which animation
    ///     (<c>Class97.java:130-131</c> splits it as <c>set = id &gt;&gt; 16</c>,
    ///     <c>frame = id &amp; 0xffff</c>). A flat list of animations shows a number; this shows the
    ///     number taken apart and held against what index 0 actually declares, which is what turns the
    ///     Animation tab's frame sets into something reachable from the animation that plays them.
    ///     <para>
    ///     <b>The join is checked, not assumed.</b> Each step reports whether index 0 declares that
    ///     set and that file. Nothing in the format requires a packed id to resolve, and an animation
    ///     naming a frame the cache does not hold is a real condition worth seeing rather than a row
    ///     to hide.
    ///     </para>
    ///     <para>
    ///     Read only, following <see cref="AnimationListDescriptor"/>: the codec re-encodes byte for
    ///     byte, but the fields worth editing here are the frame table and the per-frame sound rows,
    ///     neither of which a single cell can express.
    ///     </para>
    /// </remarks>
    public sealed class AnimationDefinitionEditorPanel : UserControl {
        /* Consolas 9 on every child. The form puts Consolas 12 on the tab control and everything
           under it inherits, which is half again what these grids are laid out for. */
        private static readonly Font GridFont = new Font("Consolas", 9F);

        private readonly DefinitionListPanel animations = new DefinitionListPanel();

        private readonly FastObjectListView steps = new FastObjectListView {
            Dock = DockStyle.Fill,
            Font = GridFont,
            FullRowSelect = true,
            GridLines = true,
            ShowGroups = false,
            UseFiltering = true,
            View = View.Details
        };

        //AutoSize rather than a stated height, so the line the summary needs is the line it gets
        //whatever font ratio the form ends up scaling to.
        private readonly Label header = new Label {
            AutoSize = true,
            Dock = DockStyle.Top,
            Font = GridFont,
            Text = NoCacheText
        };

        /* No splitter states a minimum size. Setting one re-checks the current distance against it,
           and a container is still at its 150x100 default when a field initialiser runs, so a
           minimum wide enough to be useful throws before the panel has ever been shown. */
        private readonly SplitContainer listAndSteps = new SplitContainer {
            Dock = DockStyle.Fill,
            Orientation = Orientation.Vertical
        };

        private const string NoCacheText = "No cache loaded";
        private const string NoSelectionText = "Select an animation to see the frames it names";

        /// <summary>
        ///     The descriptor the animation list is driven by.
        /// </summary>
        /// <remarks>
        ///     One instance, held rather than built per bind, because <c>DefinitionListPanel.Bind</c>
        ///     treats a different descriptor as a different thing to show and would reload the whole
        ///     index on every visit to the tab.
        /// </remarks>
        private readonly IDefinitionListDescriptor descriptor = new AnimationListDescriptor();

        private RSCache? cache;
        private bool splitterPlaced;

        /// <summary>Creates the panel.</summary>
        public AnimationDefinitionEditorPanel() {
            Dock = DockStyle.Fill;
            BuildLayout();

            animations.SelectedRowChanged += (_, _) => ShowAnimation(animations.SelectedRow as AnimationListing);
        }

        /// <summary>
        ///     Points the panel at a cache, or clears it when given none.
        /// </summary>
        /// <remarks>
        ///     <c>Editor.LoadEditorTab</c> calls this on every visit to the tab, so a rebind of the
        ///     cache already on display has to be a no-op or the index-20 sweep runs again and takes
        ///     the sort and the selection with it. Identity is the right test because opening a cache
        ///     builds a new <see cref="RSCache"/>.
        /// </remarks>
        /// <param name="newCache">The open cache, or null to unbind.</param>
        public void Bind(RSCache? newCache) {
            if (ReferenceEquals(newCache, cache))
                return;

            cache = newCache;
            steps.ClearObjects();
            header.Text = newCache == null ? NoCacheText : NoSelectionText;

            //The descriptor is passed either way. DefinitionListPanel only requires one alongside a
            //non-null cache, and keeping it constant means the columns survive an unbind.
            animations.Bind(newCache, descriptor);
        }

        /// <summary>Places the splitter once the layout pass has given the container a real size.</summary>
        /// <param name="levent">The event data.</param>
        protected override void OnLayout(LayoutEventArgs levent) {
            base.OnLayout(levent);
            PlaceSplitter();
        }

        /// <summary>
        ///     Divides the panel proportionally, once, when it first has a size worth dividing.
        /// </summary>
        /// <remarks>
        ///     A <see cref="SplitContainer"/> defaults to a splitter distance of 50 <i>pixels</i>, not
        ///     half, so the distance has to be stated - and stating it in the designer would make it
        ///     one more literal the form multiplies by its font ratio.
        /// </remarks>
        private void PlaceSplitter() {
            if (splitterPlaced || listAndSteps.Width < 200)
                return;

            //Set before the assignment, not after: changing a splitter distance lays the panel out
            //again, and this is called from that layout.
            splitterPlaced = true;

            try {
                listAndSteps.SplitterDistance = Math.Max(listAndSteps.Panel1MinSize, listAndSteps.Width * 3 / 5);
            } catch (InvalidOperationException ex) {
                //Left for the next layout rather than clamped. A clamped distance sticks, and the
                //user would see a collapsed pane on a window that later has room for both.
                splitterPlaced = false;
                Debug("Animations tab splitter not placed yet: " + ex.Message, LOG_DETAIL.ADVANCED);
            }
        }

        private void BuildLayout() {
            BuildStepColumns();

            //Docking resolves from the end of the Controls collection backwards, so the header has to
            //be added after the filled grid or the grid claims the whole pane.
            listAndSteps.Panel1.Controls.Add(animations);
            listAndSteps.Panel2.Controls.Add(steps);
            listAndSteps.Panel2.Controls.Add(header);

            Controls.Add(listAndSteps);
        }

        private void BuildStepColumns() {
            AddColumn(steps, "Step", 60, row => Step(row).Index);
            AddColumn(steps, "Cycles", 70, row => Step(row).Duration);
            AddColumn(steps, "Packed", 110, row => Step(row).Packed);
            AddColumn(steps, "Frame set", 90, row => Step(row).FrameSet);
            AddColumn(steps, "Frame", 70, row => Step(row).FrameIndex);
            AddColumn(steps, "In index 0", 110, row => Step(row).Resolution);
            AddColumn(steps, "Secondary", 110, row => Step(row).Secondary);
            AddColumn(steps, "Sounds", 200, row => Step(row).Sounds);
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
            var column = new OLVColumn(heading, null) {
                Width = width,
                Groupable = false,
                IsEditable = false,
                AspectGetter = row => read(row)
            };

            list.AllColumns.Add(column);
            list.Columns.Add(column);
        }

        private static StepListing Step(object row) {
            return (StepListing) row;
        }

        /// <summary>
        ///     Fills the step grid from the selected animation, resolving each packed id against
        ///     index 0.
        /// </summary>
        /// <remarks>
        ///     The reference table is asked once per distinct frame set rather than once per step: a
        ///     long animation names the same set for every one of its frames, and
        ///     <c>RSCache.GetFileIds</c> copies the id array on each call.
        ///     <para>
        ///     No group is opened. Whether index 0 <i>declares</i> the file is the whole question, and
        ///     that is a reference-table lookup - decoding the frame would cost a container inflate
        ///     per step to answer something the table already states.
        ///     </para>
        /// </remarks>
        /// <param name="listing">The selected animation, or null.</param>
        private void ShowAnimation(AnimationListing? listing) {
            steps.ClearObjects();

            if (cache == null || listing == null) {
                header.Text = cache == null ? NoCacheText : NoSelectionText;
                return;
            }

            AnimationDefinition record = listing.Record;
            var declared = new Dictionary<int, HashSet<int>>();
            var rows = new List<StepListing>(record.FrameCount);
            int unresolved = 0;

            for (int step = 0; step < record.FrameCount; step++) {
                int packed = record.FrameIds[step];
                int set = AnimationDefinition.FrameGroupOf(packed);
                int frame = AnimationDefinition.FrameIndexOf(packed);

                bool resolves = FilesOf(declared, set).Contains(frame);
                if (!resolves)
                    unresolved++;

                rows.Add(new StepListing(
                    step,
                    step < record.FrameDurations.Length ? record.FrameDurations[step] : -1,
                    packed,
                    set,
                    frame,
                    resolves,
                    step < record.SecondaryFrameIds.Length ? record.SecondaryFrameIds[step] : -1,
                    step < record.FrameSounds.Length ? record.FrameSounds[step] : Array.Empty<int>()));
            }

            steps.SetObjects(rows);
            header.Text = Describe(listing, unresolved);
        }

        /// <summary>The file ids index 0 declares for a frame set, memoised for this animation.</summary>
        /// <param name="memo">The per-selection memo.</param>
        /// <param name="set">The frame set's group id in index 0.</param>
        /// <returns>The declared file ids.</returns>
        private HashSet<int> FilesOf(Dictionary<int, HashSet<int>> memo, int set) {
            if (memo.TryGetValue(set, out HashSet<int>? files))
                return files;

            //Bind assigns the field before anything calls here, and the null case returned above
            files = new HashSet<int>(cache!.GetFileIds(RSConstants.FRAMES_INDEX, set));
            memo[set] = files;
            return files;
        }

        /// <summary>The one-line summary above the step grid.</summary>
        /// <param name="listing">The selected animation.</param>
        /// <param name="unresolved">How many steps name a frame index 0 does not declare.</param>
        /// <returns>The summary line.</returns>
        private static string Describe(AnimationListing listing, int unresolved) {
            string text = "Animation " + listing.AnimationId + " - " + listing.Frames + " steps over " +
                          listing.Cycles + " cycles - frame set(s) " +
                          (listing.FrameSets.Length == 0 ? "none" : listing.FrameSets);

            if (unresolved > 0)
                text += "   (" + unresolved + " step(s) name a frame index 0 does not declare)";

            return text;
        }

        /// <summary>
        ///     One playback step of the selected animation, with its packed frame id taken apart.
        /// </summary>
        /// <remarks>
        ///     The packed value is shown beside the two halves rather than instead of them. It is what
        ///     the file stores and what a bug report would quote, and the split is what says where to
        ///     look in index 0.
        /// </remarks>
        private sealed class StepListing {
            private readonly int duration;
            private readonly int packed;
            private readonly bool resolves;
            private readonly int secondary;
            private readonly int[] sounds;

            internal StepListing(int index, int duration, int packed, int frameSet, int frameIndex,
                bool resolves, int secondary, int[] sounds) {
                Index = index;
                this.duration = duration;
                this.packed = packed;
                FrameSet = frameSet;
                FrameIndex = frameIndex;
                this.resolves = resolves;
                this.secondary = secondary;
                this.sounds = sounds;
            }

            /// <summary>The step's position in the animation.</summary>
            internal int Index { get; }

            /// <summary>How many client cycles the step is held for, or blank when opcode 1 was short.</summary>
            internal object? Duration => duration < 0 ? null : (object) duration;

            /// <summary>The packed id exactly as stored, in hex.</summary>
            /// <remarks>Hex because it is two 16-bit fields in one word, which decimal hides.</remarks>
            internal string Packed => "0x" + packed.ToString("X8");

            /// <summary>The index-0 group the packed id names.</summary>
            internal int FrameSet { get; }

            /// <summary>The file within that group.</summary>
            internal int FrameIndex { get; }

            /// <summary>Whether index 0's reference table declares that file.</summary>
            internal string Resolution => resolves ? "yes" : "MISSING";

            /// <summary>The opcode 12 frame id for this step, or blank when the record stores none.</summary>
            /// <remarks>
            ///     Split the same way as the primary id, since it is the same packing - showing it as
            ///     a bare number would invite it being read as a frame index.
            /// </remarks>
            internal string Secondary {
                get {
                    if (secondary < 0)
                        return string.Empty;
                    return AnimationDefinition.FrameGroupOf(secondary) + ":" +
                           AnimationDefinition.FrameIndexOf(secondary);
                }
            }

            /// <summary>The sound ids opcode 13 attaches to this step.</summary>
            internal string Sounds => sounds.Length == 0 ? string.Empty : string.Join(", ", sounds);
        }
    }
}
