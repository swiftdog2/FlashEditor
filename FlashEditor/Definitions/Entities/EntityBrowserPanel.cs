using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Windows.Forms;
using FlashEditor.Cache;
using FlashEditor.Definitions.Editing;
using static FlashEditor.Utils.DebugUtil;
using FlashEditor.IO;

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
    ///     destroys its window handle and the GL context with it, so the one context in the
    ///     application stays where the form built it - in the left half of the page's splitter - and
    ///     this panel occupies the right half and swaps only its grid. That is the whole reason the
    ///     type selector lives on the grid side rather than above both.
    ///     </para>
    ///     <para>
    ///     Each family is a <see cref="DefinitionListDescriptor{TRow}"/> rather than an arm of
    ///     <c>Editor.LoadEditorTab</c>. All four had their own copy of the worker, the progress
    ///     reporting, the list population and the edit commit before this, and three of them read
    ///     definitions one file at a time - which re-inflates a group once per file it holds.
    ///     </para>
    /// </remarks>
    public sealed class EntityBrowserPanel : UserControl {
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

        private readonly Label noticeLabel = new Label { AutoSize = true };

        private readonly Label animationLabel = new Label { AutoSize = true, Text = "Animations" };
        private readonly ComboBox animationSelector = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList };
        private readonly Button previousAnimation = new Button { AutoSize = true, Text = "<" };
        private readonly Button nextAnimation = new Button { AutoSize = true, Text = ">" };
        private readonly Label animationStatus = new Label { AutoSize = true };

        private RSCache? cache;
        private EntityKind kind = EntityKind.Item;

        /// <summary>Set while the animation list is refilled, so the refill does not play anything.</summary>
        private bool animationsPopulating;

        /// <summary>Creates an unbound page.</summary>
        public EntityBrowserPanel() {
            Dock = DockStyle.Fill;

            kindSelector.Items.AddRange(new object[] { "Items", "NPCs", "Objects", "Models" });
            kindSelector.SelectedIndex = 0;
            kindSelector.SelectedIndexChanged += KindSelector_SelectedIndexChanged;

            exportButton.Click += ExportButton_Click;
            importButton.Click += ImportButton_Click;

            animationSelector.SelectedIndexChanged += AnimationSelector_SelectedIndexChanged;
            previousAnimation.Click += (_, _) => StepAnimation(-1);
            nextAnimation.Click += (_, _) => StepAnimation(1);

            toolStrip.Controls.Add(kindLabel);
            toolStrip.Controls.Add(kindSelector);
            toolStrip.Controls.Add(exportButton);
            toolStrip.Controls.Add(importButton);
            toolStrip.Controls.Add(noticeLabel);

            /* The two cycle buttons sit between the caption and the box rather than after it. The
               strip wraps, and the widest control on it is the box - so with the box in the middle
               the first thing to be pushed onto a second row is a bare ">" with no context, which is
               what a narrow splitter produced. Putting the box last means the box is what wraps. */
            animationStrip.Controls.Add(animationLabel);
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
        ///     Keeps the two combo boxes in proportion to the font they draw in.
        /// </summary>
        /// <remarks>
        ///     A <see cref="ComboBox"/> cannot auto-size its width, so these are the only two controls
        ///     on the page whose size is stated at all - everything beside them is <c>AutoSize</c>.
        ///     Measured against the widest string each can hold rather than written down, because a
        ///     literal is only correct at the DPI it was chosen at and this form scales by
        ///     <c>AutoScaleMode.Dpi</c>. The animation box is the one that matters: its entries are a
        ///     label and an id, so "Turn on spot - (12345)" is what it has to fit, and a default-width
        ///     box shows about half of that.
        /// </remarks>
        private void SizeSelectors() {
            int arrow = SystemInformation.VerticalScrollBarWidth;
            kindSelector.Width = TextRenderer.MeasureText("Objects_", Font).Width + arrow;
            animationSelector.Width = TextRenderer.MeasureText("Turn on spot - (65535)", Font).Width + arrow;
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
        /// </remarks>
        /// <param name="animations">The animations the NPC names.</param>
        /// <param name="emptyReason">Why there are none, when there are none.</param>
        public void ShowAnimations(IReadOnlyList<NpcAnimation> animations, string emptyReason) {
            animationsPopulating = true;

            try {
                animationSelector.BeginUpdate();
                animationSelector.Items.Clear();

                foreach (NpcAnimation animation in animations)
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
            animationStatus.Text = any
                ? animationSelector.Items.Count + " named by the render animation set"
                : emptyReason;

            if (any)
                animationSelector.SelectedIndex = 0;
        }

        /// <summary>Empties the animation selector and says why it is empty.</summary>
        /// <param name="reason">What to show instead of the list.</param>
        public void ClearAnimations(string reason = "Pick an NPC to list the animations it names.") {
            ShowAnimations(Array.Empty<NpcAnimation>(), reason);
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
            noticeLabel.Text = descriptor.IsEditable
                ? "Index " + descriptor.IndexId + ". Picking a row decodes its models and rebuilds the viewport. An edit is staged, not written."
                : "Index " + descriptor.IndexId + ". Listed from the reference table without decoding, so it is read only. Picking a row decodes the model and rebuilds the viewport.";

            importButton.Enabled = cache != null;
            exportButton.Enabled = cache != null;

            list.EmptyMessage = cache == null
                ? "No cache loaded"
                : "No " + descriptor.RowNoun + "s in index " + descriptor.IndexId;

            list.Bind(cache, cache == null ? null : descriptor);
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
    }
}
