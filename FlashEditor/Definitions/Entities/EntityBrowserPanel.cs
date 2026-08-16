using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Windows.Forms;
using FlashEditor.Cache;
using FlashEditor.Definitions.Editing;
using static FlashEditor.Utils.DebugUtil;
using FlashEditor.IO;
using FlashEditor.Definitions.Models;
using FlashEditor.Definitions.Models.Interchange;
using FlashEditor.Rendering;
using FlashEditor.UI;

namespace FlashEditor.Definitions.Entities {
    /// <summary>Which definition family the entity page is showing.</summary>
    public enum EntityKind {
        /// <summary>Index 19.</summary>
        Item,

        /// <summary>Index 18.</summary>
        Npc,

        /// <summary>Index 16.</summary>
        Object,

        /// <summary>Index 7.</summary>
        Model
    }

    /// <summary>What the entity page is showing and which row is picked.</summary>
    public sealed class EntitySelectionEventArgs : EventArgs {
        /// <summary>Records one selection.</summary>
        /// <param name="kind">Which family the grid is showing.</param>
        /// <param name="row">The selected row, or null when the selection was cleared.</param>
        public EntitySelectionEventArgs(EntityKind kind, object? row) {
            Kind = kind;
            Row = row;
        }

        /// <summary>Which family the grid is showing.</summary>
        public EntityKind Kind { get; }

        /// <summary>The selected row, or null.</summary>
        public object? Row { get; }
    }

    /// <summary>
    ///     The four model-bearing definition families behind one type selector, beside one viewport.
    /// </summary>
    /// <remarks>
    ///     This page exists because seeing an item's model used to mean opening Models, then Items,
    ///     then Models again: the grid and the GL surface were on different pages, so the two things
    ///     a user compares could never be on screen together.
    ///     <para>
    ///     <b>The viewport is deliberately not in here.</b> Moving a <c>GLControl</c> between parents
    ///     destroys its window handle and the GL context with it, so this page's context stays where
    ///     the form built it - in the left half of the page's splitter - and this panel occupies the
    ///     right half and swaps only its grid. That is the whole reason the type selector lives on
    ///     the grid side rather than above both. The rule is about reparenting rather than about how
    ///     many contexts exist: <see cref="Particles.ParticlePreviewPanel"/> holds a second one, and
    ///     it is safe for the same reason - nothing moves it either.
    ///     </para>
    ///     <para>
    ///     Each family is a <see cref="DefinitionListDescriptor{TRow}"/> rather than an arm of
    ///     <c>Editor.LoadEditorTab</c>. All four had their own copy of the worker, the progress
    ///     reporting, the list population and the edit commit before this, and three of them read
    ///     definitions one file at a time - which re-inflates a group once per file it holds.
    ///     </para>
    /// </remarks>
    public sealed class EntityBrowserPanel : UserControl {
        /// <summary>Which population the animation selector is currently holding.</summary>
        /// <remarks>
        ///     The two are answers to different questions and the selector cannot merge them. The
        ///     named list is what the cache states; the skeleton list is what this editor infers. A
        ///     single list holding both would put a fact and a guess on adjacent rows with nothing
        ///     between them saying which is which.
        /// </remarks>
        private enum AnimationPopulation {
            /// <summary>The two to four animations the NPC's render animation set names.</summary>
            Named,

            /// <summary>Every index-20 sequence built for a skeleton the NPC demonstrably animates.</summary>
            Skeleton
        }

        //One descriptor instance per family, held rather than rebuilt. DefinitionListPanel treats a
        //different descriptor object as a different thing to show, so building one per switch would
        //reload the index every time the selector moved and throw away the sort with it.
        private readonly ItemListDescriptor items = new ItemListDescriptor();
        private readonly NPCListDescriptor npcs = new NPCListDescriptor();
        private readonly ObjectListDescriptor objects = new ObjectListDescriptor();
        private readonly ModelListDescriptor models = new ModelListDescriptor();

        private readonly DefinitionListPanel list = new DefinitionListPanel();

        private readonly FlowLayoutPanel toolStrip = new FlowLayoutPanel {
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            Dock = DockStyle.Top,
            FlowDirection = FlowDirection.LeftToRight,
            WrapContents = true
        };

        private readonly FlowLayoutPanel animationStrip = new FlowLayoutPanel {
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            Dock = DockStyle.Top,
            FlowDirection = FlowDirection.LeftToRight,
            WrapContents = true
        };

        private readonly Label kindLabel = new Label { AutoSize = true, Text = "Show" };
        private readonly ComboBox kindSelector = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList };
        private readonly Button exportButton = new Button { AutoSize = true, Text = "Export selected (.dat)" };
        private readonly Button importButton = new Button { AutoSize = true, Text = "Import over selected..." };

        //Shorter than the two beside them on purpose. Six controls plus the notice is already more
        //than the strip fits at the default splitter width, and the notice is what has to stay on
        //the first row.
        private readonly Button exportObjButton = new Button { AutoSize = true, Text = "Export .obj..." };
        private readonly Button importObjButton = new Button { AutoSize = true, Text = "Import .obj..." };

        private readonly Label noticeLabel = new Label { AutoSize = true };

        private readonly Label animationLabel = new Label { AutoSize = true, Text = "Animations" };
        private readonly ComboBox animationSelector = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList };
        private readonly Button previousAnimation = new Button { AutoSize = true, Text = "<" };
        private readonly Button nextAnimation = new Button { AutoSize = true, Text = ">" };
        private readonly Label animationStatus = new Label { AutoSize = true };

        //No caption of its own, unlike every other combo on the page. Its two entries are whole
        //sentences about what the box beside it holds, so a "List" label in front of them would be
        //a seventh control on a strip that already wraps, saying nothing the entries do not.
        private readonly ComboBox populationSelector = new ComboBox {
            AccessibleName = "Which animations to list",
            DropDownStyle = ComboBoxStyle.DropDownList
        };

        private readonly InfoAffordance skeletonNotice = new InfoAffordance();

        private RSCache? cache;
        private EntityKind kind = EntityKind.Item;

        /// <summary>Set while the animation list is refilled, so the refill does not play anything.</summary>
        private bool animationsPopulating;

        /// <summary>The animations the selected NPC's render animation set names.</summary>
        /// <remarks>
        ///     Held rather than read back out of the combo box, because the box is the one thing that
        ///     does not always hold them: switching to the skeleton population replaces its contents,
        ///     and switching back has to restore what the cache stated without asking the form to
        ///     resend it.
        /// </remarks>
        private IReadOnlyList<NpcAnimation> namedAnimations = Array.Empty<NpcAnimation>();

        /// <summary>Why <see cref="namedAnimations"/> is empty, when it is.</summary>
        private string namedEmptyReason = string.Empty;

        /// <summary>
        ///     Every index-20 sequence mapped to the skeleton it animates, or null until it is built.
        /// </summary>
        /// <remarks>
        ///     One per loaded cache. Building it sweeps all 3,526 index-20 groups and resolves a frame
        ///     out of index 0 for each sequence, so it is built on the first switch to the skeleton
        ///     population and held until <see cref="Bind"/> replaces the cache under it.
        /// </remarks>
        private AnimationSkeletonIndex? skeletonIndex;

        /// <summary>The sweep in flight, or null. Held so a rebind can cancel it.</summary>
        private System.ComponentModel.BackgroundWorker? skeletonIndexer;

        /// <summary>
        ///     Stops the sweep, separately from the worker that runs it.
        /// </summary>
        /// <remarks>
        ///     <c>BackgroundWorker.CancelAsync</c> only raises a flag the work has to poll, and the
        ///     sweep is a walk over index 20 and index 0 that hands control back at nothing finer
        ///     than a group boundary. A token is what <c>AnimationSkeletonIndex.Build</c> checks
        ///     there. The first version stopped it by throwing from the progress callback instead,
        ///     which worked only because the callback happens to be invoked outside every try in
        ///     that loop, and would have stopped working silently for a caller passing no callback.
        /// </remarks>
        private System.Threading.CancellationTokenSource? skeletonIndexStop;

        /// <summary>How far the sweep has got, for the status line while it runs.</summary>
        private int skeletonIndexPercent;

        /// <summary>The OBJ export in flight, or null. Held so a rebind can cancel it.</summary>
        private System.ComponentModel.BackgroundWorker? objExporter;

        /// <summary>Set while an OBJ export runs, so the actions cannot be re-entered.</summary>
        private bool exporting;

        /// <summary>Creates an unbound page.</summary>
        public EntityBrowserPanel() {
            Dock = DockStyle.Fill;

            kindSelector.Items.AddRange(new object[] { "Items", "NPCs", "Objects", "Models" });
            kindSelector.SelectedIndex = 0;
            kindSelector.SelectedIndexChanged += KindSelector_SelectedIndexChanged;

            exportButton.Click += ExportButton_Click;
            importButton.Click += ImportButton_Click;
            exportObjButton.Click += ExportObjButton_Click;
            importObjButton.Click += ImportObjButton_Click;

            animationSelector.SelectedIndexChanged += AnimationSelector_SelectedIndexChanged;
            previousAnimation.Click += (_, _) => StepAnimation(-1);
            nextAnimation.Click += (_, _) => StepAnimation(1);

            populationSelector.Items.AddRange(new object[] {
                "Named by this NPC", "Compatible with this skeleton"
            });
            populationSelector.SelectedIndex = (int) AnimationPopulation.Named;
            populationSelector.SelectedIndexChanged += PopulationSelector_SelectedIndexChanged;

            skeletonNotice.Describes = populationSelector;
            skeletonNotice.Kind = InfoKind.Limitation;
            skeletonNotice.Caption = "What a compatible list is not";
            skeletonNotice.Body = SkeletonFilterNote;

            toolStrip.Controls.Add(kindLabel);
            toolStrip.Controls.Add(kindSelector);
            toolStrip.Controls.Add(exportButton);
            toolStrip.Controls.Add(importButton);
            toolStrip.Controls.Add(exportObjButton);
            toolStrip.Controls.Add(importObjButton);
            toolStrip.Controls.Add(noticeLabel);

            /* The two cycle buttons sit between the caption and the box rather than after it. The
               strip wraps, and the widest control on it is the box - so with the box in the middle
               the first thing to be pushed onto a second row is a bare ">" with no context, which is
               what a narrow splitter produced. Putting the box last means the box is what wraps.
               The population selector and its note go in front of the buttons for the same reason
               read the other way: they decide what the box holds, and a note that wrapped away from
               the control it qualifies is a limitation the user never sees. */
            animationStrip.Controls.Add(animationLabel);
            animationStrip.Controls.Add(populationSelector);
            animationStrip.Controls.Add(skeletonNotice);
            animationStrip.Controls.Add(previousAnimation);
            animationStrip.Controls.Add(nextAnimation);
            animationStrip.Controls.Add(animationSelector);
            animationStrip.Controls.Add(animationStatus);

            //Docking resolves from the end of the Controls collection backwards, so the filled grid
            //has to go in first or it claims the whole panel and neither strip gets any height.
            Controls.Add(list);
            Controls.Add(animationStrip);
            Controls.Add(toolStrip);

            list.SelectedRowChanged += List_SelectedRowChanged;

            SizeSelectors();
            ShowKind();
        }

        /// <summary>
        ///     What the (i) beside the population selector says, in full.
        /// </summary>
        /// <remarks>
        ///     A constant rather than a literal in the constructor only so that the reasoning behind
        ///     the wording sits beside it. The obligation is <c>CLAUDE.md</c>'s "say what the editor
        ///     cannot do", and this is the sharpest case of it in the application: a filtered list is
        ///     read as a list of correct answers, and every other view that diverges from the client
        ///     at least looks unusual on screen. This one looks authoritative.
        /// </remarks>
        private const string SkeletonFilterNote =
            "This is a guess, and the client does not make it.\n" +
            "\n" +
            "Every entry here is a sequence built for a skeleton this NPC demonstrably animates, " +
            "and that is the whole of the claim. It is not a list of animations the NPC plays.\n" +
            "\n" +
            "The cache holds no link at all from an NPC to its attack, death or special animations. " +
            "An NPC record names exactly one animation-valued thing - opcode 127, the render " +
            "animation set - and that set holds only idle, walk, run and turn. Everything else is " +
            "chosen by the server and sent over the wire, so no amount of reading the cache recovers " +
            "it. That is why the list beside this one is so short, and it is not a defect.\n" +
            "\n" +
            "The filter is also weaker than it looks. Frames bind to a model by bone-label index, a " +
            "model stores no skeleton reference at all, and the client checks nothing before applying " +
            "one - so a mismatched skeleton produces garbage rather than an error, and a matching one " +
            "is plausibility rather than proof. The skeleton id is shown against each entry so you " +
            "can see why it was offered. Play it and judge the pose.\n" +
            "\n" +
            "The first switch to this list sweeps every index-20 group and reads one frame per " +
            "sequence out of index 0, which is why it reports progress. The result is kept until the " +
            "cache is reloaded.";

        /// <summary>
        ///     Keeps the three combo boxes in proportion to the font they draw in.
        /// </summary>
        /// <remarks>
        ///     A <see cref="ComboBox"/> cannot auto-size its width, so these are the only three
        ///     controls on the page whose size is stated at all - everything beside them is
        ///     <c>AutoSize</c>. Measured against the widest string each can hold rather than written
        ///     down, because a literal is only correct at the DPI it was chosen at and this form
        ///     scales by <c>AutoScaleMode.Dpi</c>. The animation box is the one that matters: its
        ///     entries are a label and an id, so "Turn on spot - (12345)" is what it has to fit, and a
        ///     default-width box shows about half of that.
        ///     <para>
        ///     It now has to fit a skeleton-filtered entry as well, which is the longer of the two -
        ///     so the width is the wider of the two shapes rather than the locomotion one, or every
        ///     entry in the filtered list would be clipped at the skeleton id, which is precisely the
        ///     part that says why the entry is there.
        ///     </para>
        /// </remarks>
        private void SizeSelectors() {
            int arrow = SystemInformation.VerticalScrollBarWidth;
            kindSelector.Width = TextRenderer.MeasureText("Objects_", Font).Width + arrow;

            animationSelector.Width = arrow + Math.Max(
                TextRenderer.MeasureText("Turn on spot - (65535)", Font).Width,
                TextRenderer.MeasureText("Animation 65535 (skeleton 65535)", Font).Width);

            //Measured from the items themselves rather than from a copy of their text, so a reworded
            //entry cannot end up wider than the box that has to show it.
            int widest = 0;
            foreach (object entry in populationSelector.Items)
                widest = Math.Max(widest, TextRenderer.MeasureText(entry.ToString(), Font).Width);

            populationSelector.Width = widest + arrow;
        }

        /// <summary>Re-measures the two combo boxes when the font they inherit changes.</summary>
        /// <param name="e">The event data.</param>
        protected override void OnFontChanged(EventArgs e) {
            base.OnFontChanged(e);
            SizeSelectors();
        }

        /// <summary>Raised when the grid's selection moves, or the family being shown changes.</summary>
        /// <remarks>
        ///     What the form hangs the viewport off. The panel deliberately loads no models itself:
        ///     the GL context belongs to the form, and every upload has to happen where a context is
        ///     current.
        /// </remarks>
        public event EventHandler<EntitySelectionEventArgs>? EntitySelected;

        /// <summary>Raised when an animation is picked or cycled, with the index-20 animation id.</summary>
        public event EventHandler<int>? AnimationChosen;

        /// <summary>Which family the grid is showing.</summary>
        [System.ComponentModel.Browsable(false)]
        [System.ComponentModel.DesignerSerializationVisibility(
            System.ComponentModel.DesignerSerializationVisibility.Hidden)]
        public EntityKind Kind => kind;

        /// <summary>The selected row, or null when nothing is picked.</summary>
        [System.ComponentModel.Browsable(false)]
        [System.ComponentModel.DesignerSerializationVisibility(
            System.ComponentModel.DesignerSerializationVisibility.Hidden)]
        public object? SelectedRow => list.SelectedRow;

        /// <summary>
        ///     The width this page needs to show its widest family's grid without a scrollbar.
        /// </summary>
        /// <remarks>
        ///     Measured from the descriptors rather than stated, so the splitter beside the viewport
        ///     is derived the way the navigation column widths and the two combo boxes are. The page
        ///     was placed at a literal 620 pixels, which is the failure this form has already had at
        ///     scale: it is `AutoScaleMode.Dpi` against 96 dpi, and a literal is only right at the
        ///     dpi it was chosen at.
        ///     <para>
        ///     The <b>widest</b> family, not the selected one, because the splitter must not jump
        ///     every time the type selector moves - a viewport that resized itself under the cursor
        ///     as the user browsed would be worse than one column too narrow. Items is the widest at
        ///     fourteen columns, but that is measured rather than assumed here so a new column
        ///     anywhere widens it.
        ///     </para>
        /// </remarks>
        [System.ComponentModel.Browsable(false)]
        [System.ComponentModel.DesignerSerializationVisibility(
            System.ComponentModel.DesignerSerializationVisibility.Hidden)]
        public int PreferredGridWidth {
            get {
                int widest = 0;

                foreach (IDefinitionListDescriptor descriptor in new[] {
                             (IDefinitionListDescriptor) items, npcs, objects, models
                         }) {
                    int width = 0;
                    foreach (DefinitionColumn column in descriptor.Columns)
                        width += column.Width;
                    widest = Math.Max(widest, width);
                }

                //The grid's own vertical scrollbar and its border, neither of which is a column but
                //both of which sit inside the same rectangle. Every family here is long enough that
                //the scrollbar is always present.
                return widest + SystemInformation.VerticalScrollBarWidth + SystemInformation.Border3DSize.Width * 2;
            }
        }

        /// <summary>
        ///     Points the page at a cache, or unbinds it.
        /// </summary>
        /// <remarks>
        ///     Unbinding matters as much as binding: the grid's worker walks a whole index, so one
        ///     left bound across a cache reload would keep decoding out of a file store that is about
        ///     to be disposed.
        /// </remarks>
        /// <param name="openCache">The open cache, or null to unbind.</param>
        public void Bind(RSCache? openCache) {
            //Same reason the grid's own worker is cancelled: an OBJ export walks index 7 decoding as
            //it goes, and one left running across a reload keeps reading out of a store about to be
            //disposed.
            objExporter?.CancelAsync();

            /* And the skeleton sweep, for the same reason and more urgently: it walks index 20 and
               index 0, which is the largest index in the cache, so it is the longest-running reader
               on the page. The index it produced belongs to the cache it was built from - a sequence
               id means a different record in the two caches on disk - so it goes with it. */
            skeletonIndexStop?.Cancel();
            skeletonIndexStop = null;
            skeletonIndexer?.CancelAsync();
            skeletonIndexer = null;
            skeletonIndex = null;

            cache = openCache;
            ClearAnimations();
            ShowKind();
        }

        /// <summary>Turns alternating row shading on or off, for the form's View menu.</summary>
        /// <param name="enabled">Whether alternate rows are shaded.</param>
        /// <param name="colour">The shade.</param>
        public void SetAlternatingRows(bool enabled, Color colour) {
            list.SetAlternatingRows(enabled, colour);
        }

        /// <summary>Says what an action did, on the grid's own status line.</summary>
        /// <param name="text">What to say.</param>
        public void ReportStatus(string text) {
            list.ReportStatus(text);
        }

        /// <summary>
        ///     Fills the animation selector from the NPC's render animation set.
        /// </summary>
        /// <remarks>
        ///     Called by the form once it knows the selection is an NPC. It lives here rather than on
        ///     the form because the list belongs beside the grid, and the ids come from the NPC
        ///     record rather than from anything the viewport knows.
        ///     <para>
        ///     The strip stays visible for every family so the page does not change height as the
        ///     selector moves - it says why it is empty instead. A strip that appears and disappears
        ///     reflows the grid under the cursor.
        ///     </para>
        ///     <para>
        ///     These are recorded rather than shown directly, because the box may be holding the
        ///     skeleton-filtered population instead. Which of the two is on screen is the user's
        ///     standing choice and does not reset when the selection moves; the named list is kept so
        ///     that switching back does not need the form to resend it.
        ///     </para>
        /// </remarks>
        /// <param name="animations">The animations the NPC names.</param>
        /// <param name="emptyReason">Why there are none, when there are none.</param>
        public void ShowAnimations(IReadOnlyList<NpcAnimation> animations, string emptyReason) {
            namedAnimations = animations ?? Array.Empty<NpcAnimation>();
            namedEmptyReason = emptyReason ?? string.Empty;

            RefreshAnimationList();
        }

        /// <summary>Empties the animation selector and says why it is empty.</summary>
        /// <param name="reason">What to show instead of the list.</param>
        public void ClearAnimations(string reason = "Pick an NPC to list the animations it names.") {
            ShowAnimations(Array.Empty<NpcAnimation>(), reason);
        }

        /// <summary>Which population the selector is set to.</summary>
        private AnimationPopulation Population =>
            (AnimationPopulation) Math.Max(0, populationSelector.SelectedIndex);

        /// <summary>
        ///     Refills the animation selector from whichever population is selected.
        /// </summary>
        /// <remarks>
        ///     The one writer of the box's contents. Both the NPC selection moving and the population
        ///     switching change what belongs in it, and two fill paths is how a box ends up holding
        ///     one NPC's named list under another NPC's caption.
        /// </remarks>
        private void RefreshAnimationList() {
            IReadOnlyList<NpcAnimation> entries = namedAnimations;
            string status;

            if (Population == AnimationPopulation.Named) {
                status = namedAnimations.Count > 0
                    ? namedAnimations.Count + " named by the render animation set"
                    : namedEmptyReason;
            }
            else if (skeletonIndexer != null) {
                /* The named list stays in the box while the sweep runs rather than being emptied to
                   a spinner. Emptying it would take the animation the user is watching out of the
                   viewport's selector for the length of an index-20 walk, and an empty box reads as
                   a broken control. */
                status = "Indexing index 20 by skeleton, " + skeletonIndexPercent + "%...";
            }
            else if (skeletonIndex == null) {
                status = cache == null
                    ? "No cache loaded."
                    : "Not indexed - switch this list away and back to sweep index 20 again.";
            }
            else {
                entries = CompatibleAnimations(out status);
            }

            FillAnimationSelector(entries, status);
        }

        /// <summary>
        ///     Every sequence built for a skeleton the selected NPC demonstrably animates.
        /// </summary>
        /// <remarks>
        ///     Derived from the whole named set rather than from its idle. A render set can name
        ///     animations on more than one skeleton - NPC 3284 names animation 8326 on skeleton 1750
        ///     while its idle is elsewhere - so filtering to one of them would hide animations the
        ///     cache itself says that NPC plays, which is the one thing a filter must never do.
        /// </remarks>
        /// <param name="status">What to say beside the box.</param>
        /// <returns>The entries, each carrying the skeleton that put it there.</returns>
        private IReadOnlyList<NpcAnimation> CompatibleAnimations(out string status) {
            AnimationSkeletonIndex built = skeletonIndex!;

            var own = new List<int>(namedAnimations.Count);
            foreach (NpcAnimation animation in namedAnimations)
                own.Add(animation.AnimationId);

            IReadOnlyCollection<int> skeletons = built.SkeletonsOf(own);

            if (skeletons.Count == 0) {
                //Two different empties, and the difference matters to whoever is looking at it: no
                //NPC selected at all, against an NPC whose own animations resolved to no skeleton
                //and therefore gave the filter nothing to work from.
                status = namedAnimations.Count == 0
                    ? namedEmptyReason
                    : "None of the " + namedAnimations.Count + " animations this NPC names resolved to a skeleton, so there is nothing to filter by.";
                return Array.Empty<NpcAnimation>();
            }

            IReadOnlyList<int> sequences = built.SequencesFor(skeletons);

            var entries = new List<NpcAnimation>(sequences.Count);
            foreach (int sequence in sequences)
                entries.Add(new NpcAnimation(string.Empty, sequence, built.SkeletonOf(sequence)));

            //"Plausible" rather than a bare count, because the count is the part that reads as
            //authoritative. The (i) beside the selector carries the full reason.
            status = entries.Count + " plausible on skeleton " + string.Join(", ", skeletons) +
                ", out of " + built.SequenceCount + " indexed. A guess, not the NPC's own list.";

            return entries;
        }

        /// <summary>Puts a population into the box and states what it is.</summary>
        /// <param name="entries">The animations to offer.</param>
        /// <param name="status">What to say beside the box.</param>
        private void FillAnimationSelector(IReadOnlyList<NpcAnimation> entries, string status) {
            animationsPopulating = true;

            try {
                animationSelector.BeginUpdate();
                animationSelector.Items.Clear();

                foreach (NpcAnimation animation in entries)
                    animationSelector.Items.Add(animation);

                animationSelector.EndUpdate();
            }
            finally {
                animationsPopulating = false;
            }

            bool any = animationSelector.Items.Count > 0;
            animationSelector.Enabled = any;
            previousAnimation.Enabled = any;
            nextAnimation.Enabled = any;
            animationStatus.Text = status;

            if (any)
                animationSelector.SelectedIndex = 0;
        }

        /// <summary>
        ///     Switches the box between what the cache states and what the skeleton filter infers.
        /// </summary>
        /// <remarks>
        ///     The sweep starts here rather than when the cache is bound. Most sessions never ask for
        ///     the filtered list, and a walk of every index-20 group on every cache open would be
        ///     paid by every one of them.
        /// </remarks>
        private void PopulationSelector_SelectedIndexChanged(object? sender, EventArgs e) {
            if (Population == AnimationPopulation.Skeleton
                && skeletonIndex == null && skeletonIndexer == null && cache != null) {
                StartSkeletonIndex(cache);
            }

            RefreshAnimationList();
        }

        /// <summary>
        ///     Builds the sequence-to-skeleton index off the UI thread.
        /// </summary>
        /// <remarks>
        ///     <b>Its own frame source, not the viewport's.</b> <see cref="CacheAnimationDataSource"/>
        ///     memoises into a plain <see cref="Dictionary{TKey,TValue}"/> and the form's instance is
        ///     read by the animator on the UI thread on every posed frame, so sharing it would be an
        ///     unsynchronised write under a concurrent reader - which corrupts the map rather than
        ///     throwing. This one is local to the sweep and is dropped when it returns, which also
        ///     releases the index-0 groups it decoded on the way through.
        ///     <para>
        ///     Progress arrives on percent boundaries because <c>AnimationSkeletonIndex.Build</c>
        ///     reports on them; <see cref="System.ComponentModel.BackgroundWorker.ReportProgress(int)"/>
        ///     marshals to the UI thread on every call, and there are 3,526 groups to walk.
        ///     </para>
        /// </remarks>
        /// <param name="open">The cache to sweep, passed rather than read from the field so a rebind
        ///     part way through cannot make one index describe two caches.</param>
        private void StartSkeletonIndex(RSCache open) {
            var indexer = new System.ComponentModel.BackgroundWorker {
                WorkerReportsProgress = true,
                WorkerSupportsCancellation = true
            };

            skeletonIndexer = indexer;
            skeletonIndexPercent = 0;

            var stop = new System.Threading.CancellationTokenSource();
            skeletonIndexStop = stop;

            indexer.DoWork += (_, args) => {
                var frames = new CacheAnimationDataSource(open);

                //A token rather than throwing from the progress callback. Build checks it between
                //groups, so a sweep started with no callback is still interruptible.
                args.Result = AnimationSkeletonIndex.Build(open, frames,
                    percent => indexer.ReportProgress(percent), stop.Token);
            };

            indexer.ProgressChanged += (_, args) => {
                if (!ReferenceEquals(skeletonIndexer, indexer))
                    return;

                skeletonIndexPercent = args.ProgressPercentage;

                //The status line only, not a refill. The box already holds the named list and
                //rebuilding it a hundred times would fight the user's selection all the way through.
                if (Population == AnimationPopulation.Skeleton)
                    animationStatus.Text = "Indexing index 20 by skeleton, " + skeletonIndexPercent + "%...";
            };

            indexer.RunWorkerCompleted += (_, args) => {
                //A superseded sweep is discarded whole rather than allowed to install an index built
                //from a cache that is no longer open.
                if (!ReferenceEquals(skeletonIndexer, indexer))
                    return;

                skeletonIndexer = null;

                //Cancellation is silence rather than a message. The only canceller is Bind, which
                //drops the reference above first, so this is the case where the sweep threw out of
                //its own progress callback and there is nothing left to say about a cache that has
                //already been replaced.
                if (args.Cancelled || args.Error is OperationCanceledException)
                    return;

                if (args.Error != null) {
                    //Reported rather than thrown: an exception out of a completion handler takes the
                    //form down, and a sweep that failed costs the filter and nothing else.
                    Debug("Animation skeleton index failed: " + args.Error);
                    animationStatus.Text = "Could not index index 20 by skeleton: " + args.Error.Message;
                    return;
                }

                skeletonIndex = (AnimationSkeletonIndex) args.Result!;
                RefreshAnimationList();
            };

            //Said before the worker starts rather than from its first progress report, so the strip
            //never sits reading as though the switch did nothing.
            animationStatus.Text = "Indexing index 20 by skeleton, 0%...";
            indexer.RunWorkerAsync();
        }

        /// <summary>
        ///     Selects one record of whichever family edits a cache index, for a link followed from
        ///     another tab.
        /// </summary>
        /// <remarks>
        ///     <b>This page is four cache indexes, and only one of them routes to it.</b> Indexes 19,
        ///     18, 16 and 7 share the page because seeing an item's model used to mean opening three
        ///     tabs; the registration names 19 and the other three are listed beside it. So a link
        ///     into index 7 or 16 reaches the right page and then has to say which family - the
        ///     selector is what decides that, and nothing outside could reach it before.
        ///     <para>
        ///     The record is asked for through <c>DefinitionListPanel.SelectRecord</c>, which holds
        ///     the request until the rows exist. Switching family starts a load, and a link followed
        ///     from another tab routinely finds this page not loaded at all.
        ///     </para>
        /// </remarks>
        /// <param name="indexId">The cache index the record lives in.</param>
        /// <param name="recordId">The record, or -1 to show the family alone.</param>
        /// <returns>Whether this page holds an editor for that index.</returns>
        public bool Show(int indexId, int recordId) {
            EntityKind? wanted = indexId switch {
                RSConstants.ITEM_DEFINITIONS_INDEX => EntityKind.Item,
                RSConstants.NPC_DEFINITIONS_INDEX => EntityKind.Npc,
                RSConstants.OBJECTS_DEFINITIONS_INDEX => EntityKind.Object,
                RSConstants.MODELS_INDEX => EntityKind.Model,
                _ => null
            };

            if (wanted == null)
                return false;

            //Through the selector rather than by assigning the field, so the combo agrees with the
            //grid and the viewport is cleared of the previous family's model. Assigning it raises
            //the handler that does both.
            if (kindSelector.SelectedIndex != (int) wanted.Value)
                kindSelector.SelectedIndex = (int) wanted.Value;

            if (recordId >= 0)
                list.SelectRecord(recordId);

            return true;
        }

        /// <summary>The descriptor for the family currently selected.</summary>
        private IDefinitionListDescriptor Descriptor => kind switch {
            EntityKind.Item => items,
            EntityKind.Npc => npcs,
            EntityKind.Object => objects,
            EntityKind.Model => models,
            _ => throw new InvalidOperationException("No descriptor for entity kind " + kind + ".")
        };

        /// <summary>Where an export of the current family is written, under the output directory.</summary>
        private string ExportFolder => kind switch {
            EntityKind.Item => "items",
            EntityKind.Npc => "npcs",
            EntityKind.Object => "objects",
            EntityKind.Model => "models",
            _ => "entities"
        };

        private void KindSelector_SelectedIndexChanged(object? sender, EventArgs e) {
            kind = (EntityKind) kindSelector.SelectedIndex;
            ClearAnimations();
            ShowKind();

            //The viewport is showing whatever the previous family had selected, so the form is told
            //the selection is now nothing rather than left drawing a model from a grid that has gone.
            EntitySelected?.Invoke(this, new EntitySelectionEventArgs(kind, null));
        }

        /// <summary>Binds the grid to the selected family and restates what the page cannot do.</summary>
        private void ShowKind() {
            IDefinitionListDescriptor descriptor = Descriptor;

            /* Mark what a selection costs, because none of it is visible on screen. Picking a row
               decodes its models out of index 7 and re-uploads the viewport's buffers; picking an
               NPC or an object picks up several models at once. */
            noticeLabel.Text = kind == EntityKind.Model
                ? "Index 7. Cells are read only, but an OBJ round trip is not: an export carries vertices and faces only, and an import replaces those and keeps the rest. Staged, not written."
                : descriptor.IsEditable
                    ? "Index " + descriptor.IndexId + ". Picking a row decodes its models and rebuilds the viewport. An edit is staged, not written."
                    : "Index " + descriptor.IndexId + ". Listed from the reference table without decoding, so it is read only. Picking a row decodes the model and rebuilds the viewport.";

            UpdateActions();

            list.EmptyMessage = cache == null
                ? "No cache loaded"
                : "No " + descriptor.RowNoun + "s in index " + descriptor.IndexId;

            list.Bind(cache, cache == null ? null : descriptor);
        }

        /// <summary>The one place any action button's enablement is decided.</summary>
        /// <remarks>
        ///     Extracted from <see cref="ShowKind"/> rather than duplicated into the export worker.
        ///     Two writers of <c>Enabled</c> is how a button ends up permanently disabled after a
        ///     failed export - the state is computed from the panel's fields here instead, and every
        ///     caller changes a field and asks again.
        ///     <para>
        ///     The OBJ actions are disabled rather than hidden for the other three families. A strip
        ///     that gains and loses controls reflows, and this one wraps - so hiding them would move
        ///     the grid under the cursor as the type selector moved.
        ///     </para>
        /// </remarks>
        private void UpdateActions() {
            bool ready = cache != null && !exporting;
            bool isModel = kind == EntityKind.Model;

            importButton.Enabled = ready;
            exportButton.Enabled = ready;
            exportObjButton.Enabled = ready && isModel;
            importObjButton.Enabled = ready && isModel;
        }

        private void List_SelectedRowChanged(object? sender, EventArgs e) {
            EntitySelected?.Invoke(this, new EntitySelectionEventArgs(kind, list.SelectedRow));
        }

        private void AnimationSelector_SelectedIndexChanged(object? sender, EventArgs e) {
            if (animationsPopulating)
                return;

            if (animationSelector.SelectedItem is NpcAnimation animation)
                AnimationChosen?.Invoke(this, animation.AnimationId);
        }

        /// <summary>
        ///     Moves the animation selection by one, wrapping at both ends.
        /// </summary>
        /// <remarks>
        ///     Wrapping rather than stopping. The list is short - a render animation names a handful
        ///     of ids, not a page of them - and the point of the two buttons is to cycle through what
        ///     an NPC does without looking at the box.
        /// </remarks>
        /// <param name="step">-1 for the previous animation, 1 for the next.</param>
        private void StepAnimation(int step) {
            int count = animationSelector.Items.Count;
            if (count == 0)
                return;

            int next = animationSelector.SelectedIndex + step;
            if (next < 0)
                next = count - 1;
            else if (next >= count)
                next = 0;

            animationSelector.SelectedIndex = next;
        }

        /// <summary>
        ///     Writes the selected rows out as the bytes the cache stores for them.
        /// </summary>
        /// <remarks>
        ///     The stored bytes rather than a re-encode. The item tab used to export
        ///     <c>definition.Encode()</c>, which is the editor's spelling of the record rather than
        ///     the cache's - and this format is not canonical, so the two legitimately differ in
        ///     opcode order for a record nobody has touched. An export that is not the file is not
        ///     an export.
        /// </remarks>
        private void ExportButton_Click(object? sender, EventArgs e) {
            if (cache == null)
                return;

            IReadOnlyList<object> rows = list.SelectedRows;
            if (rows.Count == 0) {
                ReportStatus("Select the rows to export first.");
                return;
            }

            IDefinitionListDescriptor descriptor = Descriptor;
            string directory = Path.Combine(RSConstants.CACHE_OUTPUT_DIRECTORY, ExportFolder);

            int written = 0;
            int failed = 0;

            try {
                Directory.CreateDirectory(directory);

                foreach (object row in rows) {
                    DefinitionAddress address = descriptor.AddressOf(row);

                    try {
                        byte[] stored = cache.ReadFileBytes(descriptor.IndexId, address.GroupId, address.FileId);
                        string name = address.HasDefinitionId
                            ? address.DefinitionId.ToString()
                            : address.GroupId + "_" + address.FileId;
                        File.WriteAllBytes(Path.Combine(directory, name + ".dat"), stored);
                        written++;
                    }
                    catch (Exception failure) {
                        failed++;
                        Debug("Export of " + descriptor.RowNoun + " " + address + " failed: " + failure.Message);
                    }
                }
            }
            catch (Exception failure) {
                //Reported rather than thrown: this runs from a click handler, and an exception out of
                //one takes the form down.
                ReportStatus("Export failed: " + failure.Message);
                Debug("Entity export failed: " + failure);
                return;
            }

            ReportStatus("Exported " + written + " " + descriptor.RowNoun + "s to " + directory +
                (failed > 0 ? ", " + failed + " failed" : string.Empty));
        }

        /// <summary>
        ///     Writes the selected models out as Wavefront OBJ, to a location the user picks.
        /// </summary>
        /// <remarks>
        ///     A picker rather than the silent write into the output directory that the <c>.dat</c>
        ///     button beside it does, because the two are different kinds of thing. That one exports
        ///     the file; this one exports a lossy derivation of it, as a pair of files, which the user
        ///     is about to open in a modeller - so where it lands is their call. It matches the MIDI
        ///     and PNG exports elsewhere in the editor rather than the byte exports.
        ///     <para>
        ///     The dialog also supplies two things <see cref="ObjDocument.Save"/> deliberately does
        ///     not: a directory that exists, and a prompt before an overwrite.
        ///     </para>
        /// </remarks>
        private void ExportObjButton_Click(object? sender, EventArgs e) {
            if (cache == null || kind != EntityKind.Model || exporting)
                return;

            var listings = new List<ModelListing>();
            foreach (object row in list.SelectedRows) {
                if (row is ModelListing listing)
                    listings.Add(listing);
            }

            if (listings.Count == 0) {
                ReportStatus("Select the models to export first.");
                return;
            }

            string directory;
            string? singlePath = null;

            if (listings.Count == 1) {
                using SaveFileDialog save = new SaveFileDialog {
                    Title = "Export model " + listings[0].ModelId + " as OBJ",
                    Filter = "Wavefront OBJ (*.obj)|*.obj|All files (*.*)|*.*",
                    FileName = "model_" + listings[0].ModelId + ".obj",
                    DefaultExt = "obj"
                };

                if (save.ShowDialog(this) != DialogResult.OK)
                    return;

                singlePath = save.FileName;
                directory = Path.GetDirectoryName(Path.GetFullPath(singlePath)) ?? string.Empty;
            }
            else {
                using FolderBrowserDialog browse = new FolderBrowserDialog {
                    Description = "Export " + listings.Count + " models as OBJ",
                    UseDescriptionForTitle = true
                };

                if (browse.ShowDialog(this) != DialogResult.OK)
                    return;

                directory = browse.SelectedPath;
            }

            StartObjExport(cache, listings, directory, singlePath);
        }

        /// <summary>Runs an OBJ export off the UI thread.</summary>
        /// <remarks>
        ///     One code path for a single row and for many, rather than a synchronous arm for the
        ///     common case - a second branch here would be a second branch nothing exercises.
        /// </remarks>
        /// <param name="open">The cache to read, passed rather than read from the field.</param>
        /// <param name="listings">The models to write.</param>
        /// <param name="directory">Where they land, for the status line.</param>
        /// <param name="singlePath">The exact path picked, when exactly one row was selected.</param>
        private void StartObjExport(RSCache open, IReadOnlyList<ModelListing> listings,
                string directory, string? singlePath) {
            var exporter = new System.ComponentModel.BackgroundWorker {
                WorkerReportsProgress = true,
                WorkerSupportsCancellation = true
            };

            objExporter = exporter;
            exporting = true;
            UpdateActions();
            ReportStatus("Exporting " + listings.Count + " model" + (listings.Count == 1 ? string.Empty : "s") + " as OBJ...");

            exporter.ProgressChanged += (_, args) => {
                if (!ReferenceEquals(objExporter, exporter))
                    return;

                ReportStatus(args.UserState?.ToString() ?? string.Empty);
            };

            /* The cache, the rows and the paths are arguments rather than fields, for the reason
               DefinitionListPanel.DecodeRows takes them: a rebind part way through must not be able
               to make one export read half of one cache and half of another. */
            exporter.DoWork += (_, args) =>
                args.Result = ExportObjBatch(open, listings, directory, singlePath, exporter, args);

            exporter.RunWorkerCompleted += (_, args) => {
                //A superseded export is discarded whole rather than allowed to report over the one
                //that replaced it.
                if (!ReferenceEquals(objExporter, exporter))
                    return;

                objExporter = null;
                exporting = false;
                UpdateActions();

                if (args.Cancelled) {
                    ReportStatus("OBJ export cancelled");
                    return;
                }

                if (args.Error != null) {
                    //Reported rather than thrown: an exception out of a completion handler takes the
                    //form down.
                    Debug("Model OBJ export failed: " + args.Error);
                    ReportStatus("OBJ export failed: " + args.Error.Message);
                    return;
                }

                ReportStatus((string) args.Result!);
            };

            exporter.RunWorkerAsync();
        }

        /// <summary>Decodes and writes each model, off the UI thread.</summary>
        /// <returns>What to say on the status line.</returns>
        private static string ExportObjBatch(RSCache open, IReadOnlyList<ModelListing> listings,
                string directory, string? singlePath,
                System.ComponentModel.BackgroundWorker exporter,
                System.ComponentModel.DoWorkEventArgs args) {
            int written = 0;
            int failed = 0;
            int files = 0;
            int percentile = Math.Max(1, listings.Count / 100);
            string last = string.Empty;

            for (int i = 0; i < listings.Count; i++) {
                if (exporter.CancellationPending) {
                    args.Cancel = true;
                    return string.Empty;
                }

                ModelListing listing = listings[i];

                try {
                    string objPath = singlePath ?? Path.Combine(directory, "model_" + listing.ModelId + ".obj");

                    /* The library is named after the OBJ rather than after the model. The exporter's
                       own default is model_<id>.mtl, so a user who renamed the file in the save
                       dialog would get widget.obj pointing its mtllib at a file called something
                       else - correct, and confusing to keep track of. */
                    ObjDocument document = BuildModelObj(open, listing,
                        Path.GetFileNameWithoutExtension(objPath) + ".mtl");

                    IReadOnlyList<string> paths = document.Save(objPath);
                    files += paths.Count;
                    written++;
                    last = "Exported model " + listing.ModelId + " to " + objPath +
                        (paths.Count > 1 ? " and its material library" : string.Empty);

                    foreach (string line in document.Summary)
                        Debug("model " + listing.ModelId + " OBJ: " + line);
                }
                catch (Exception failure) {
                    //One model that will not decode costs itself and not the rest of the selection.
                    failed++;
                    Debug("OBJ export of model " + listing.ModelId + " failed: " + failure.Message);
                }

                //On percent boundaries only. ReportProgress marshals to the UI thread on every call,
                //so one post per model would cost more than the decode it is reporting.
                if ((i + 1) % percentile == 0 || i + 1 == listings.Count)
                    exporter.ReportProgress(0, "Exported " + (i + 1) + "/" + listings.Count + " models");
            }

            if (written == 1 && failed == 0)
                return last;

            return "Exported " + written + " models as " + files + " files to " + directory +
                (failed > 0 ? ", " + failed + " failed" : string.Empty);
        }

        /// <summary>Decodes one model out of index 7 and renders it as OBJ text.</summary>
        /// <remarks>
        ///     <c>GetModelDefinition</c> rather than <c>ModelCodec.Decode</c>, because the decoded
        ///     projection is what carries texture coordinates - they are computed from each textured
        ///     face's reference triangle at decode, and are the only exported quantity that is derived
        ///     rather than stored. Without one the mesh still exports in full, just with no vt lines.
        ///     <para>
        ///     Deliberately not memoised through the form's decoded-model dictionary. That is a
        ///     <c>SortedDictionary</c> mutated from the UI thread by the form's own model loader, and
        ///     this runs on a worker - an unsynchronised reader there corrupts the tree rather than
        ///     throwing. A batch export would also retain a fully projected definition per row for
        ///     something read exactly once.
        ///     </para>
        /// </remarks>
        /// <param name="open">The cache to read.</param>
        /// <param name="row">Which model.</param>
        /// <param name="materialFileName">What to call the material library, or null for the default.</param>
        /// <returns>The OBJ and its material library, unwritten.</returns>
        internal static ObjDocument BuildModelObj(RSCache open, ModelListing row, string? materialFileName) {
            ModelDefinition definition = open.GetModelDefinition(row.Address.GroupId, row.FileId);
            return ModelObjExporter.Export(definition, materialFileName);
        }

        /// <summary>
        ///     Stages a file on disk over the selected row.
        /// </summary>
        /// <remarks>
        ///     The file's own bytes are what gets stored, after decoding them to check that they
        ///     parse. Re-encoding would substitute this editor's opcode order for the one the file
        ///     carries, and the format has more than one valid spelling of the same record.
        ///     <para>
        ///     A descriptor that does not read payloads cannot check anything, and says so rather
        ///     than pretending to: index 7 is listed from the reference table, so an imported model
        ///     is stored unvalidated.
        ///     </para>
        /// </remarks>
        private void ImportButton_Click(object? sender, EventArgs e) {
            if (cache == null)
                return;

            object? row = list.SelectedRow;
            if (row == null) {
                ReportStatus("Select the row to overwrite first.");
                return;
            }

            IDefinitionListDescriptor descriptor = Descriptor;
            DefinitionAddress address = descriptor.AddressOf(row);

            using OpenFileDialog picker = new OpenFileDialog {
                Title = "Import over " + descriptor.RowNoun + " at " + address,
                Filter = "Definition file (*.dat)|*.dat|All files (*.*)|*.*"
            };

            if (picker.ShowDialog(this) != DialogResult.OK)
                return;

            try {
                byte[] imported = File.ReadAllBytes(picker.FileName);

                object? replacement = null;
                if (descriptor.ReadsPayload)
                    replacement = descriptor.Decode(cache, address, new JagStream(imported));

                byte[] stored = cache.ReadFileBytes(descriptor.IndexId, address.GroupId, address.FileId);
                if (imported.AsSpan().SequenceEqual(stored)) {
                    ReportStatus("No change at " + address + " - the file already holds those bytes.");
                    return;
                }

                cache.WriteFile(descriptor.IndexId, address.GroupId, address.FileId, new JagStream(imported));

                if (replacement != null)
                    list.ReplaceRow(row, replacement);

                ReportStatus("Staged " + descriptor.RowNoun + " at " + address +
                    (descriptor.ReadsPayload ? string.Empty : " - stored unvalidated, index " +
                        descriptor.IndexId + " is listed without decoding"));
            }
            catch (Exception failure) {
                //A malformed file must cost the import and nothing else.
                Debug("Entity import failed: " + failure);
                ReportStatus("Import failed: " + failure.Message);
                MessageBox.Show(this,
                    "Could not import that file over the selected " + descriptor.RowNoun + ":" +
                    Environment.NewLine + failure.Message,
                    "Import", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        /// <summary>
        ///     Reads an OBJ over the selected model and stages the result.
        /// </summary>
        /// <remarks>
        ///     A separate action from the <c>.dat</c> import rather than another extension on its
        ///     filter, because the two contracts are opposites. That one stores the file's own bytes
        ///     verbatim and argues at length that it must; this one has to re-encode, because an OBJ
        ///     carries a mesh and not a model file. Sharing a filter would also mean choosing the arm
        ///     by extension, and its second entry is <c>All files</c> - so an OBJ renamed <c>.dat</c>
        ///     would be stored verbatim over a model and corrupt the entry silently.
        /// </remarks>
        private void ImportObjButton_Click(object? sender, EventArgs e) {
            if (cache == null || kind != EntityKind.Model)
                return;

            if (list.SelectedRow is not ModelListing listing) {
                ReportStatus("Select the model to overwrite first.");
                return;
            }

            using OpenFileDialog picker = new OpenFileDialog {
                Title = "Import an OBJ over model " + listing.ModelId,
                Filter = "Wavefront OBJ (*.obj)|*.obj|All files (*.*)|*.*"
            };

            if (picker.ShowDialog(this) != DialogResult.OK)
                return;

            try {
                bool staged = StageModelObj(cache, listing, picker.FileName, out ModelImportResult result);

                foreach (ModelImportEntry entry in result.Entries)
                    Debug("model " + listing.ModelId + " import: " + entry);

                if (!result.Succeeded) {
                    ReportStatus("Import refused for model " + listing.ModelId + " - " + result.Message);
                    MessageBox.Show(this, Account(result), "Import model",
                        MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    return;
                }

                if (!staged) {
                    ReportStatus("No change to model " + listing.ModelId + " - that OBJ holds the same mesh.");
                    return;
                }

                /* The viewport has to re-decode rather than be patched: normals and texture
                   coordinates are computed at decode and cannot be recomputed in place. The form
                   memoises decoded models by id, so the stale entry goes before the reselect, or the
                   viewport redraws the model exactly as it was before the import. */
                cache.models.Remove(listing.ModelId);
                EntitySelected?.Invoke(this, new EntitySelectionEventArgs(kind, listing));

                ReportStatus("Staged model " + listing.ModelId + " from " +
                    Path.GetFileName(picker.FileName) + " - " + result.Message);
            }
            catch (Exception failure) {
                //A malformed file must cost the import and nothing else.
                Debug("Model OBJ import failed: " + failure);
                ReportStatus("Import failed: " + failure.Message);
                MessageBox.Show(this,
                    "Could not import that OBJ over model " + listing.ModelId + ":" +
                    Environment.NewLine + failure.Message,
                    "Import model", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        /// <summary>
        ///     Reads an OBJ over the model at a row and stages the result, unless nothing changed.
        /// </summary>
        /// <remarks>
        ///     <para>
        ///     Parsed here rather than through <c>ModelObjImporter.ImportFile</c>, for the one thing
        ///     that call cannot report. Quads and n-gons are fanned into triangles silently, which
        ///     changes the face count, and a face-count change is the importer's hardest refusal - so
        ///     the commonest failure this feature will produce arrives as two bare counts with nothing
        ///     said about the fanning that caused it. Holding the parsed mesh lets the refusal name it.
        ///     </para>
        ///     <para>
        ///     It also removes a trap: <c>ImportFile(model, path)</c> and <c>Import(model, objText)</c>
        ///     are both <c>(ModelFile, string)</c>, so handing a path to the second compiles and parses
        ///     the path itself as if it were an OBJ. Neither is called here.
        ///     </para>
        /// </remarks>
        /// <param name="open">The cache to read and stage into.</param>
        /// <param name="row">Which model is being overwritten.</param>
        /// <param name="objPath">The OBJ on disk.</param>
        /// <param name="result">The account of what happened, whatever the outcome.</param>
        /// <returns>
        ///     True when bytes were staged. False for a refusal and for an unchanged mesh alike -
        ///     <see cref="ModelImportResult.Succeeded"/> tells those two apart.
        /// </returns>
        internal static bool StageModelObj(RSCache open, ModelListing row, string objPath,
                out ModelImportResult result) {
            ModelDefinition definition = open.GetModelDefinition(row.Address.GroupId, row.FileId);
            ModelFile original = definition.Source
                ?? throw new InvalidOperationException("Model " + row.ModelId + " has no stored form to import over.");

            //A file that will not parse is a bad file rather than a mesh that disagrees with the
            //model, so it throws out of here instead of becoming a refusal. The caller reports it
            //as the failure it is.
            ObjMesh mesh = ObjParser.ParseFile(objPath);
            result = ModelObjImporter.Import(original, mesh);

            if (!result.Succeeded) {
                if (mesh.TriangulatedPolygons > 0)
                    result = WithFanningNote(result, mesh.TriangulatedPolygons);

                return false;
            }

            /* An import that changed nothing must write nothing. A rebuild renormalises the strip
               opcodes and the smart widths, and the format has more than one legal spelling of the
               same mesh - so the bytes would move for a file nobody edited, and with them the archive
               CRC and the reference-table entry of every archive packed beside it. */
            if (!result.GeometryChanged)
                return false;

            byte[] encoded = result.Model!.Encode().ToArray();
            byte[] stored = open.ReadFileBytes(RSConstants.MODELS_INDEX, row.Address.GroupId, row.FileId);

            /* Never reached while the check above stands, and not dead: removing that one and
               re-running the wiring tests showed this catching the unedited case on its own, because
               re-encoding an untouched model is byte-identical. Which of the two stops a spurious
               write is therefore not observable from a test, so keep both - this one is the weaker
               claim, since a model whose re-encode is not byte-stable would slip past it. */
            if (encoded.AsSpan().SequenceEqual(stored))
                return false;

            open.WriteFile(RSConstants.MODELS_INDEX, row.Address.GroupId, row.FileId, new JagStream(encoded));
            return true;
        }

        /// <summary>Restates a refusal with the triangulation that most likely caused it.</summary>
        /// <param name="refusal">The refusal as the importer gave it.</param>
        /// <param name="fanned">How many faces the parser fanned into triangles.</param>
        /// <returns>The same refusal, with the fanning named.</returns>
        private static ModelImportResult WithFanningNote(ModelImportResult refusal, int fanned) {
            string note = fanned + " faces in that OBJ had more than three corners and were fanned " +
                "into triangles before anything was compared, which is what moved the face count.";

            var entries = new List<ModelImportEntry>(refusal.Entries) {
                new ModelImportEntry("OBJ polygons", ModelImportDisposition.Refused, note)
            };

            return new ModelImportResult(false, false, null, refusal.Message + " " + note, entries);
        }

        /// <summary>Renders an import's account as one block, which is what its rows are shaped for.</summary>
        /// <param name="result">The account.</param>
        /// <returns>The message, then one line per row.</returns>
        private static string Account(ModelImportResult result) {
            var text = new System.Text.StringBuilder(result.Message).AppendLine().AppendLine();

            foreach (ModelImportEntry entry in result.Entries)
                text.AppendLine(entry.ToString());

            return text.ToString();
        }
    }
}
