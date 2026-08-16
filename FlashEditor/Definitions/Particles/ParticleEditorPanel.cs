using FlashEditor.Cache;
using FlashEditor.Definitions.Editing;
using System;
using System.Drawing;
using System.Windows.Forms;
using static FlashEditor.Utils.DebugUtil;

namespace FlashEditor.Definitions.Particles {
    /// <summary>
    ///     The Particles tab: index 27, one record family at a time.
    /// </summary>
    /// <remarks>
    ///     A family selector rather than one list, because index 27 is not a record table. It holds
    ///     two unrelated formats that share nothing but an index: group 0 is emitters, group 1 is the
    ///     effectors they name. They have no opcode in common, so a single grid would need a union of
    ///     headings that is wrong for both rows in it. Same shape as the Config and Defaults tabs, and
    ///     the same reason.
    ///     <para>
    ///     <b>Read only.</b> Both records round-trip, and neither is safely editable in a grid: almost
    ///     every field is one member of a multi-field opcode, and the emitter's size bounds are stored
    ///     through one of two aliased opcodes whose choice no cell can express. The detail pane below
    ///     the list shows every value the record carries.
    ///     </para>
    /// </remarks>
    public sealed class ParticleEditorPanel : UserControl {
        /* Consolas 9 on every child. The form puts Consolas 12 on the tab control and everything
           under it inherits, which is half again what these grids are laid out for. */
        private static readonly Font GridFont = new Font("Consolas", 9F);

        private readonly ComboBox families = new ComboBox {
            DropDownStyle = ComboBoxStyle.DropDownList,
            Font = GridFont
        };

        private readonly Label familyLabel = new Label {
            AutoSize = true,
            Font = GridFont,
            Text = "Records",
            TextAlign = ContentAlignment.MiddleLeft
        };

        //FlowLayoutPanel rather than absolute positions, so the caption and the box stay together at
        //whatever font ratio the form scales to.
        private readonly FlowLayoutPanel selector = new FlowLayoutPanel {
            AutoSize = true,
            Dock = DockStyle.Top,
            FlowDirection = FlowDirection.LeftToRight,
            WrapContents = false
        };

        //AutoSize rather than a stated height, so the line the summary needs is the line it gets.
        private readonly Label header = new Label {
            AutoSize = true,
            Dock = DockStyle.Top,
            Font = GridFont,
            Text = NoCacheText
        };

        private readonly DefinitionListPanel records = new DefinitionListPanel {
            //Bound with a null cache while nothing is selected, so the panel's own default would
            //claim no cache is loaded while a cache is open behind it.
            EmptyMessage = NoSelectionText
        };

        private readonly DetailFieldGrid fields = new DetailFieldGrid();

        /* No splitter states a minimum size. Setting one re-checks the current distance against it,
           and a container is still at its 150x100 default when a field initialiser runs. */
        private readonly SplitContainer listAndFields = new SplitContainer {
            Dock = DockStyle.Fill,
            Orientation = Orientation.Horizontal
        };

        private readonly ParticlePreviewPanel preview = new ParticlePreviewPanel();

        //The records and their preview, side by side. Vertical rather than horizontal so the field
        //grid keeps its full height: an emitter carries about thirty rows and stacking the preview
        //under it would push most of them off the page.
        private readonly SplitContainer recordsAndPreview = new SplitContainer {
            Dock = DockStyle.Fill,
            Orientation = Orientation.Vertical
        };

        private const string NoCacheText = "No cache loaded";
        private const string NoSelectionText = "Select a record to see what it holds";

        private RSCache? cache;
        private bool splitterPlaced;

        /// <summary>Creates the panel.</summary>
        public ParticleEditorPanel() {
            Dock = DockStyle.Fill;

            //Derived from the font rather than written as a pixel count: the form is
            //AutoScaleMode.Font, so a literal width is multiplied at runtime and clips the caption.
            families.Width = families.Font.Height * 28;

            BuildLayout();

            families.SelectedIndexChanged += (_, _) => ShowFamily();
            records.SelectedRowChanged += (_, _) => ShowRecord(records.SelectedRow as IParticleListing);
        }

        /// <summary>
        ///     Points the panel at a cache, or clears it when given none.
        /// </summary>
        /// <remarks>
        ///     <c>Editor.LoadEditorTab</c> calls this on every visit to the tab, so a rebind of the
        ///     cache already on display has to be a no-op or the selected family is thrown away each
        ///     time. Identity is the right test because opening a cache builds a new
        ///     <see cref="RSCache"/>.
        /// </remarks>
        /// <param name="newCache">The open cache, or null to unbind.</param>
        public void Bind(RSCache? newCache) {
            if (ReferenceEquals(newCache, cache))
                return;

            cache = newCache;
            fields.ClearObjects();
            preview.Bind(newCache);
            header.Text = newCache == null ? NoCacheText : NoSelectionText;

            if (newCache == null) {
                records.Bind(null, new ParticleEmitterListDescriptor());
                return;
            }

            ShowFamily();
        }

        /// <summary>Places the splitter once the layout pass has given the container a real size.</summary>
        /// <param name="levent">The event data.</param>
        protected override void OnLayout(LayoutEventArgs levent) {
            base.OnLayout(levent);
            PlaceSplitter();
        }

        /// <summary>Divides the panel proportionally, once, when it first has a size worth dividing.</summary>
        /// <remarks>
        ///     A <see cref="SplitContainer"/> defaults to a splitter distance of 50 <i>pixels</i>, not
        ///     half, so the distance has to be stated - and stating it in the designer would make it
        ///     one more literal the form multiplies by its font ratio.
        /// </remarks>
        private void PlaceSplitter() {
            if (splitterPlaced || listAndFields.Height < 200 || recordsAndPreview.Width < 400)
                return;

            //Set before the assignment, not after: changing a splitter distance lays the panel out
            //again, and this is called from that layout.
            splitterPlaced = true;

            try {
                //Two thirds to the records, because the preview is one emitter's worth of quads and
                //stays legible small while the field grid has columns to fit.
                recordsAndPreview.SplitterDistance = Math.Max(recordsAndPreview.Panel1MinSize,
                    recordsAndPreview.Width * 2 / 3);
                listAndFields.SplitterDistance = Math.Max(listAndFields.Panel1MinSize, listAndFields.Height / 2);
            } catch (InvalidOperationException ex) {
                splitterPlaced = false;
                Debug("Particles tab splitter not placed yet: " + ex.Message, LOG_DETAIL.ADVANCED);
            }
        }

        private void BuildLayout() {
            families.Items.Add(new FamilyOption(ParticleEmitterDefinition.GroupId, "Emitters"));
            families.Items.Add(new FamilyOption(ParticleEffectorDefinition.GroupId, "Effectors"));
            families.SelectedIndex = 0;

            selector.Controls.Add(familyLabel);
            selector.Controls.Add(families);

            listAndFields.Panel1.Controls.Add(records);
            listAndFields.Panel2.Controls.Add(fields);

            recordsAndPreview.Panel1.Controls.Add(listAndFields);
            recordsAndPreview.Panel2.Controls.Add(preview);

            //Docking resolves from the end of the Controls collection backwards, so the strips have
            //to be added after the filled splitter, and in bottom-to-top order among themselves.
            Controls.Add(recordsAndPreview);
            Controls.Add(header);
            Controls.Add(selector);

            //Bound before any cache arrives so the record grid has headings from the start.
            records.Bind(null, new ParticleEmitterListDescriptor());
        }

        /// <summary>
        ///     Selects one record of one particle family, for a link followed from another tab.
        /// </summary>
        /// <remarks>
        ///     <b>The group is not derivable from the id.</b> Index 27 holds emitters in group 0 and
        ///     effectors in group 1, two formats with no opcode in common, and an id is a file
        ///     within one of them - so emitter 40 and effector 40 are different records and a caller
        ///     that handed over 40 alone would land on whichever family the selector was left on.
        ///     A model's footer names both kinds, which is exactly where that mistake arrives from.
        /// </remarks>
        /// <param name="groupId">The group within index 27.</param>
        /// <param name="fileId">The record within that group, or -1 to show the family alone.</param>
        /// <returns>Whether that group is one of the two this index holds.</returns>
        public bool Show(int groupId, int fileId) {
            foreach (object entry in families.Items) {
                if (entry is not FamilyOption family || family.GroupId != groupId)
                    continue;

                //Through the selector, so the combo agrees with the grid. Assigning it raises the
                //handler that loads the group.
                if (!ReferenceEquals(families.SelectedItem, family))
                    families.SelectedItem = family;

                if (fileId >= 0)
                    records.SelectRecord(fileId);

                return true;
            }

            return false;
        }

        /// <summary>
        ///     Loads whichever group the selector names.
        /// </summary>
        /// <remarks>
        ///     A fresh descriptor per selection, because <c>DefinitionListPanel.Bind</c> treats the
        ///     same descriptor instance as the same thing to show and would keep the previous group's
        ///     rows on screen.
        /// </remarks>
        private void ShowFamily() {
            fields.ClearObjects();
            preview.ShowEmitter(null);

            if (cache == null || families.SelectedItem is not FamilyOption family) {
                header.Text = cache == null ? NoCacheText : NoSelectionText;
                return;
            }

            int declared = cache.GetFileIds(RSConstants.CONFIG_PARTICLES, family.GroupId).Length;
            header.Text = "Group " + family.GroupId + " - " + family.Name + " - " +
                          declared + " record(s) declared";

            records.Bind(cache, family.GroupId == ParticleEffectorDefinition.GroupId
                ? new ParticleEffectorListDescriptor()
                : (IDefinitionListDescriptor) new ParticleEmitterListDescriptor());
        }

        /// <summary>Fills the field grid and the preview from the selected record.</summary>
        /// <remarks>
        ///     No cache read: the row already carries the whole decoded record, and the preview takes
        ///     that object rather than re-reading index 27 by id.
        /// </remarks>
        /// <param name="listing">The selected record, or null.</param>
        private void ShowRecord(IParticleListing? listing) {
            fields.ShowFields(listing);

            switch (listing) {
                case ParticleEmitterListing emitter:
                    preview.ShowEmitter(emitter.Record);
                    break;

                //An effector has no particles of its own, and the preview says so rather than going
                //blank beside a selected row.
                case ParticleEffectorListing:
                    preview.ShowEffector();
                    break;

                default:
                    preview.ShowEmitter(null);
                    break;
            }

            if (listing != null)
                header.Text = listing.Summary;
        }

        /// <summary>One group of index 27 as an entry in the selector.</summary>
        private sealed class FamilyOption {
            internal FamilyOption(int groupId, string name) {
                GroupId = groupId;
                Name = name;
            }

            /// <summary>The group id, which the client reads by literal.</summary>
            internal int GroupId { get; }

            /// <summary>What the group holds, settled from what the client does with it.</summary>
            internal string Name { get; }

            /// <summary>The entry as the combo box shows it.</summary>
            /// <returns>The group id and what it holds.</returns>
            public override string ToString() {
                return GroupId + "  " + Name;
            }
        }
    }
}
