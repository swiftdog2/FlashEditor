using BrightIdeasSoftware;
using FlashEditor.Cache;
using FlashEditor.Definitions.Editing;
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Windows.Forms;
using static FlashEditor.Utils.DebugUtil;

namespace FlashEditor.Definitions.Interfaces {
    /// <summary>
    ///     The Interfaces tab: index 3 from a list of interfaces, down to the components one holds,
    ///     down to every field one component stores.
    /// </summary>
    /// <remarks>
    ///     A group is one interface and a file is one component, so the index has two real levels and
    ///     a flat listing of 42,256 files hides the one that matters - which components belong
    ///     together. This tab shows the group level first and loads a single group's components on
    ///     selection.
    ///     <para>
    ///     <b>Only the middle level is a <see cref="DefinitionListPanel"/>.</b> The interface list is
    ///     built from the reference table alone and reads no payload at all, so it appears instantly
    ///     and a cache open costs nothing; the panel would have had to inflate all 1,078 containers to
    ///     produce the same 1,078 rows. The component list is the panel, scoped by
    ///     <see cref="InterfaceComponentListDescriptor"/> to the selected group, which is what keeps
    ///     cell editing and the write-back path rather than re-implementing them here.
    ///     </para>
    ///     <para>
    ///     <b>Both levels carry name hashes and both are shown as hashes.</b> A hash is not a name:
    ///     <see cref="InterfaceNames"/> returns one only where re-hashing a candidate reproduces what
    ///     the table stores, so the Name column is blank far more often than the hash column is. The
    ///     two are separate columns for that reason - collapsing them would present a number as if it
    ///     were a recovered name.
    ///     </para>
    ///     <para>
    ///     <b>The field pane is read only.</b> The four editable cells are position and size, which
    ///     the descriptor already offers and commits. Everything else is either structure (a parent id
    ///     is a sibling index, so re-pointing one re-parents a subtree) or CS2 bytecode operands, and
    ///     a name/value grid is the wrong place to change either.
    ///     </para>
    /// </remarks>
    public sealed class InterfaceEditorPanel : UserControl {
        /// <summary>
        ///     Carries the component columns while nothing is selected.
        /// </summary>
        /// <remarks>
        ///     Bound with a null cache rather than left unbound, because <c>DefinitionListPanel.Bind</c>
        ///     tears its columns down when handed no descriptor - and an empty grid with headings reads
        ///     as "nothing selected" where a headingless one reads as broken.
        /// </remarks>
        private static readonly IDefinitionListDescriptor NoInterface = new InterfaceComponentListDescriptor();

        /* Consolas 9 on every child. The form puts Consolas 12 on the tab control and everything
           under it inherits, which is half again what these grids are laid out for. */
        private static readonly Font GridFont = new Font("Consolas", 9F);

        private readonly FastObjectListView interfaces = Grid();
        private readonly DefinitionListPanel components = new DefinitionListPanel {
            //This pane is bound with a null cache while nothing is selected, so the panel's own
            //default would claim no cache is loaded while the list beside it is full of rows.
            EmptyMessage = NoSelectionText
        };
        private readonly FastObjectListView fields = Grid();

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

        private readonly SplitContainer componentsAndFields = new SplitContainer {
            Dock = DockStyle.Fill,
            Orientation = Orientation.Horizontal
        };

        private const string NoCacheText = "No cache loaded";
        private const string NoSelectionText = "Select an interface to see its components";

        private RSCache? cache;
        private bool splittersPlaced;

        /// <summary>Creates the panel.</summary>
        public InterfaceEditorPanel() {
            Dock = DockStyle.Fill;
            BuildLayout();

            interfaces.SelectedIndexChanged += (_, _) => ShowInterface(interfaces.SelectedObject as InterfaceListing);
            components.SelectedRowChanged += (_, _) => ShowComponent(components.SelectedRow as InterfaceComponentRow);
        }

        /// <summary>
        ///     Points the panel at a cache, or clears it when given none.
        /// </summary>
        /// <remarks>
        ///     <c>Editor.LoadEditorTab</c> calls this on every visit to the tab, so a rebind of the
        ///     cache already on display has to be a no-op or the selection is thrown away each time.
        ///     Identity is the right test because opening a cache builds a new <see cref="RSCache"/>.
        /// </remarks>
        /// <param name="newCache">The open cache, or null to unbind.</param>
        public void Bind(RSCache? newCache) {
            if (ReferenceEquals(newCache, cache))
                return;

            cache = newCache;

            interfaces.ClearObjects();
            fields.ClearObjects();
            components.Bind(null, NoInterface);
            header.Text = newCache == null ? NoCacheText : NoSelectionText;

            if (newCache == null)
                return;

            try {
                interfaces.SetObjects(ListInterfaces(newCache));
            } catch (Exception ex) {
                //Reported rather than thrown: this runs from the tab loader, and an exception out of
                //it takes the form down on a cache that is merely missing a reference table.
                header.Text = "Index 3's reference table could not be read: " + ex.Message;
                Debug("Interface tab could not list index 3: " + ex);
            }
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
            if (splittersPlaced || listAndDetail.Width < 200 || componentsAndFields.Height < 200)
                return;

            //Set before the assignments, not after: changing a splitter distance lays the panel out
            //again, and this is called from that layout.
            splittersPlaced = true;

            try {
                listAndDetail.SplitterDistance = Math.Max(listAndDetail.Panel1MinSize, listAndDetail.Width / 3);
                componentsAndFields.SplitterDistance =
                    Math.Max(componentsAndFields.Panel1MinSize, componentsAndFields.Height * 3 / 5);
            } catch (InvalidOperationException ex) {
                //Left for the next layout rather than clamped. A clamped distance sticks, and the
                //user would see a collapsed pane on a window that later has room for all three.
                splittersPlaced = false;
                Debug("Interface tab splitters not placed yet: " + ex.Message, LOG_DETAIL.ADVANCED);
            }
        }

        private void BuildLayout() {
            BuildInterfaceColumns();
            BuildFieldColumns();

            componentsAndFields.Panel1.Controls.Add(components);
            componentsAndFields.Panel2.Controls.Add(fields);

            //Docking resolves from the end of the Controls collection backwards, so the header has to
            //be added after the filled splitter or the splitter claims the whole panel.
            listAndDetail.Panel1.Controls.Add(interfaces);
            listAndDetail.Panel2.Controls.Add(componentsAndFields);
            listAndDetail.Panel2.Controls.Add(header);

            Controls.Add(listAndDetail);

            //Bound before any cache arrives so the component grid has headings from the start.
            components.Bind(null, NoInterface);
        }

        private void BuildInterfaceColumns() {
            AddColumn(interfaces, "Interface", 90, row => Listing(row).GroupId);
            AddColumn(interfaces, "Name", 130, row => Listing(row).Name);
            AddColumn(interfaces, "Name hash", 120, row => Listing(row).NameHashOrNothing);
            AddColumn(interfaces, "Components", 100, row => Listing(row).ComponentCount);
            AddColumn(interfaces, "Named", 70, row => Listing(row).NamedComponents);
            AddColumn(interfaces, "Ids", 110, row => Listing(row).IdRange);
        }

        private void BuildFieldColumns() {
            AddColumn(fields, "Section", 110, row => Field(row).Section);
            AddColumn(fields, "Field", 190, row => Field(row).Name);
            AddColumn(fields, "Value", 620, row => Field(row).Value);
        }

        /// <summary>One grid, laid out the same way as every other.</summary>
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
            var column = new OLVColumn(heading, null) {
                Width = width,
                Groupable = false,
                IsEditable = false,
                AspectGetter = row => read(row)
            };

            list.AllColumns.Add(column);
            list.Columns.Add(column);
        }

        private static InterfaceListing Listing(object row) {
            return (InterfaceListing) row;
        }

        private static FieldListing Field(object row) {
            return (FieldListing) row;
        }

        /// <summary>
        ///     Every interface the reference table declares, described without reading a single group.
        /// </summary>
        /// <remarks>
        ///     Table-driven through <see cref="RSCache.EnumerateGroups"/>, which is what the client can
        ///     address: a group the table omits cannot be loaded at all.
        /// </remarks>
        /// <param name="open">The open cache.</param>
        /// <returns>One row per interface.</returns>
        private static List<InterfaceListing> ListInterfaces(RSCache open) {
            RSReferenceTable table = open.GetReferenceTable(RSConstants.INTERFACE_DEFINITIONS_INDEX);
            var rows = new List<InterfaceListing>();

            foreach (int group in open.EnumerateGroups(RSConstants.INTERFACE_DEFINITIONS_INDEX)) {
                RSArchiveEntry? entry = table.GetArchiveEntry(group);
                if (entry == null)
                    continue;

                int[] files = entry.GetValidFileIds();
                int named = 0;
                foreach (int file in files) {
                    RSFileEntry? child = entry.GetFileEntry(file);
                    //Components a name was actually recovered for, not components that merely carry
                    //an identifier. The vanilla capture gives all 40,883 of its components an
                    //identifier, so the second reading would report every interface as fully named
                    //while the grid beside it showed a column of bare hashes.
                    if (child != null &&
                        InterfaceNames.ComponentName(group, file, child.GetIdentifier()) != null)
                        named++;
                }

                rows.Add(new InterfaceListing(group, entry.GetIdentifier(), files, named));
            }

            return rows;
        }

        /// <summary>
        ///     Loads the selected interface's components.
        /// </summary>
        /// <remarks>
        ///     A fresh descriptor per selection, because <c>DefinitionListPanel.Bind</c> treats the
        ///     same descriptor instance as the same thing to show and would keep the previous
        ///     interface's rows on screen.
        /// </remarks>
        /// <param name="listing">The selected interface, or null.</param>
        private void ShowInterface(InterfaceListing? listing) {
            fields.ClearObjects();

            if (cache == null || listing == null) {
                header.Text = cache == null ? NoCacheText : NoSelectionText;
                components.Bind(null, NoInterface);
                return;
            }

            header.Text = Describe(listing);
            components.Bind(cache, new InterfaceComponentListDescriptor(listing.GroupId));
        }

        /// <summary>Fills the field grid from the selected component.</summary>
        /// <remarks>
        ///     No cache read at all: the component list row already carries the whole decoded record,
        ///     because one component is one file and the descriptor decoded it to build the row.
        /// </remarks>
        /// <param name="row">The selected component, or null.</param>
        private void ShowComponent(InterfaceComponentRow? row) {
            fields.ClearObjects();

            if (row == null)
                return;

            fields.SetObjects(new List<FieldListing>(DescribeComponent(row)));
        }

        /// <summary>The line above the detail grids: what the selected interface is.</summary>
        /// <param name="listing">The selected interface.</param>
        /// <returns>The summary line.</returns>
        private static string Describe(InterfaceListing listing) {
            string name = listing.Name.Length > 0
                ? " \"" + listing.Name + "\""
                : listing.NameHash == InterfaceNames.Unnamed ? " (unnamed)" : " (hash " + listing.NameHash + ")";

            return "Interface " + listing.GroupId + name + " - " + listing.ComponentCount +
                   " components, " + listing.NamedComponents + " named - ids " + listing.IdRange;
        }

        /// <summary>
        ///     Every field one component stores, grouped by the block of the format it came from.
        /// </summary>
        /// <remarks>
        ///     Only the type block the component's own type reads is listed. The other five are shared
        ///     storage that the decoder never wrote, so showing them would report five blocks of zeroes
        ///     as if they were in the file.
        /// </remarks>
        /// <param name="row">The selected component.</param>
        /// <returns>The field rows.</returns>
        private static IEnumerable<FieldListing> DescribeComponent(InterfaceComponentRow row) {
            InterfaceComponentDefinition component = row.Component;

            yield return new FieldListing("address", "Interface", row.GroupId.ToString());
            yield return new FieldListing("address", "Component", row.FileId.ToString());
            yield return new FieldListing("address", "Component id",
                component.ComponentId + " (0x" + component.ComponentId.ToString("X8") + ")");
            yield return new FieldListing("address", "Interface name", row.InterfaceName);
            yield return new FieldListing("address", "Component name", row.ComponentName);

            yield return new FieldListing("header", "Version byte", component.RawVersion +
                (component.RawVersion == InterfaceComponentDefinition.If3Version ? " (if3)" : ""));
            yield return new FieldListing("header", "Type", row.TypeName);
            yield return new FieldListing("header", "Authoring name",
                component.AuthoringName == null ? "" : component.AuthoringName.Text);
            yield return new FieldListing("header", "Content type", component.ContentType.ToString());
            yield return new FieldListing("header", "Position", component.BasePositionX + ", " + component.BasePositionY);
            yield return new FieldListing("header", "Size", component.BaseWidth + " x " + component.BaseHeight);
            yield return new FieldListing("header", "Modes", "width " + component.WidthMode + ", height " +
                component.HeightMode + ", x " + component.XMode + ", y " + component.YMode);
            yield return new FieldListing("header", "Parent",
                component.RawParentId == InterfaceComponentDefinition.NoParent
                    ? "none (65535)"
                    : component.RawParentId + " (component id " + component.ParentComponentId + ")");
            yield return new FieldListing("header", "Settings",
                Hex(component.SettingsFlags, 2) + (component.IsHidden ? " hidden" : ""));

            foreach (FieldListing field in DescribeTypeBlock(component))
                yield return field;

            yield return new FieldListing("tail", "Access mask", Hex(component.AccessMask, 6));
            yield return new FieldListing("tail", "Slots", DescribeSlots(component));
            yield return new FieldListing("tail", "Option base", component.OptionBase.Text);
            yield return new FieldListing("tail", "Action nibble", component.ActionHighNibble.ToString());
            yield return new FieldListing("tail", "Context options", DescribeOptions(component));
            yield return new FieldListing("tail", "Selected action", component.SelectedAction.Text);
            yield return new FieldListing("tail", "Drag", component.DragDeadzone + " px deadzone, " +
                component.DragDelay + " tick delay");
            yield return new FieldListing("tail", "Hint slot", component.HintSlot.ToString());
            yield return new FieldListing("tail", "Tooltip", component.Tooltip.Text);

            if (component.HasTargetShorts)
                yield return new FieldListing("tail", "Target shorts",
                    component.RawTargetVerb + ", " + component.RawTargetCursor + ", " +
                    component.RawTargetOperand);

            for (int hook = 0; hook < InterfaceComponentDefinition.HookCount; hook++)
                if (component.Hooks[hook].Length > 0)
                    yield return new FieldListing("hooks", "Hook " + hook, DescribeHook(component.Hooks[hook]));

            for (int trigger = 0; trigger < InterfaceComponentDefinition.TriggerCount; trigger++)
                if (component.Triggers[trigger].Length > 0)
                    yield return new FieldListing("triggers", "Trigger " + trigger,
                        string.Join(", ", component.Triggers[trigger]));
        }

        /// <summary>The one type block this component's type reads.</summary>
        /// <param name="component">The decoded component.</param>
        /// <returns>The field rows for that block, or nothing for a type that reads none.</returns>
        private static IEnumerable<FieldListing> DescribeTypeBlock(InterfaceComponentDefinition component) {
            switch (component.ComponentType) {
                case 0:
                    yield return new FieldListing("layer", "Scroll max",
                        component.ScrollMaxHorizontal + " x " + component.ScrollMaxVertical);
                    yield return new FieldListing("layer", "Flag byte", component.LayerFlagByte.ToString());
                    break;

                case 3:
                    yield return new FieldListing("rectangle", "Colour", Hex(component.Colour, 6));
                    yield return new FieldListing("rectangle", "Filled",
                        component.RectangleFilledByte + (component.RectangleFilled ? " filled" : " outline"));
                    yield return new FieldListing("rectangle", "Transparency",
                        component.Transparency + " (0 is opaque)");
                    break;

                case 4:
                    yield return new FieldListing("text", "Font", component.FontId.ToString());
                    yield return new FieldListing("text", "Message", component.Message.Text);
                    yield return new FieldListing("text", "Line height", component.LineHeight.ToString());
                    yield return new FieldListing("text", "Alignment",
                        "h " + component.HorizontalAlignment + ", v " + component.VerticalAlignment);
                    yield return new FieldListing("text", "Shadow",
                        component.ShadowByte + (component.HasShadow ? " shadowed" : ""));
                    yield return new FieldListing("text", "Colour", Hex(component.Colour, 6));
                    yield return new FieldListing("text", "Transparency",
                        component.Transparency + " (0 is opaque)");
                    break;

                case 5:
                    yield return new FieldListing("sprite", "Sprite", component.SpriteId.ToString());
                    yield return new FieldListing("sprite", "Transform", component.SpriteTransform.ToString());
                    yield return new FieldListing("sprite", "Flags", Hex(component.SpriteFlags, 2) +
                        (component.SpriteTransformed ? " transformed" : "") +
                        (component.SpriteTiled ? " tiled" : ""));
                    yield return new FieldListing("sprite", "Transparency",
                        component.Transparency + " (0 is opaque)");
                    yield return new FieldListing("sprite", "Outline",
                        component.OutlineThickness + " px, " + Hex(component.OutlineColour, 6));
                    yield return new FieldListing("sprite", "Image transforms",
                        component.SpriteTransform1Byte + ", " + component.SpriteTransform2Byte);
                    yield return new FieldListing("sprite", "Tint", Hex(component.Colour, 6));
                    break;

                case 6:
                    yield return new FieldListing("model", "Model", component.ModelId.ToString());
                    yield return new FieldListing("model", "Settings", Hex(component.ModelSettings, 2) +
                        (component.HasModelTransform ? " transform" : "") +
                        (component.HasExtendedModelTransform ? " extended transform" : "") +
                        (component.ModelFlag2 ? " flag2" : "") + (component.ModelFlag3 ? " flag3" : ""));

                    if (component.HasModelTransform || component.HasExtendedModelTransform) {
                        yield return new FieldListing("model", "Offset",
                            component.ModelOffsetX + ", " + component.ModelOffsetY);
                        yield return new FieldListing("model", "Rotation", component.ModelRotateX + ", " +
                            component.ModelRotateY + ", " + component.ModelRotateZ);
                        yield return new FieldListing("model", "Zoom", component.ModelZoom.ToString());
                    }

                    if (component.HasExtendedModelTransform)
                        yield return new FieldListing("model", "Extended offset",
                            component.ModelExtendedOffset.ToString());

                    yield return new FieldListing("model", "Animation", component.AnimationId.ToString());

                    if (component.WidthMode != 0)
                        yield return new FieldListing("model", "Width extra", component.ModelWidthExtra.ToString());
                    if (component.HeightMode != 0)
                        yield return new FieldListing("model", "Height extra", component.ModelHeightExtra.ToString());
                    break;

                case 9:
                    yield return new FieldListing("line", "Width", component.LineWidth.ToString());
                    yield return new FieldListing("line", "Colour", Hex(component.Colour, 6));
                    yield return new FieldListing("line", "Flipped",
                        component.LineFlippedByte + (component.LineFlipped ? " flipped" : ""));
                    break;
            }
        }

        /// <summary>The slot table, in stream order.</summary>
        /// <param name="component">The decoded component.</param>
        /// <returns>The entries, or a note that the table is absent.</returns>
        private static string DescribeSlots(InterfaceComponentDefinition component) {
            if (component.Slots.Count == 0)
                return "none";

            var parts = new List<string>(component.Slots.Count);
            foreach (InterfaceSlotEntry entry in component.Slots)
                parts.Add("slot " + entry.Slot + " = " + entry.Value + " (" + entry.First + ", " + entry.Second + ")");
            return string.Join("  ", parts);
        }

        private static string DescribeOptions(InterfaceComponentDefinition component) {
            if (component.ContextOptions.Count == 0)
                return "none";

            var parts = new List<string>(component.ContextOptions.Count);
            for (int option = 0; option < component.ContextOptions.Count; option++)
                parts.Add((option + 1) + ". " + component.ContextOptions[option].Text);
            return string.Join("  ", parts);
        }

        /// <summary>
        ///     One CS2 hook array as operands.
        /// </summary>
        /// <remarks>
        ///     Strings are quoted and integers are not, because the type byte is the only thing that
        ///     tells them apart on the wire and a hook that reads "1" is ambiguous without it.
        /// </remarks>
        /// <param name="operands">The hook's operands.</param>
        /// <returns>The operands in stream order.</returns>
        private static string DescribeHook(InterfaceScriptOperand[] operands) {
            var parts = new List<string>(operands.Length);
            foreach (InterfaceScriptOperand operand in operands)
                parts.Add(operand.TypeByte == InterfaceScriptOperand.StringType
                    ? "\"" + (operand.Text?.Text ?? "") + "\""
                    : operand.Integer.ToString());
            return string.Join(", ", parts);
        }

        /// <summary>A value in hex, zero padded to the width the format gives it.</summary>
        /// <param name="value">The value.</param>
        /// <param name="digits">How many hex digits the stored field holds.</param>
        /// <returns>The value in hex.</returns>
        private static string Hex(int value, int digits) {
            return "0x" + value.ToString("X" + digits);
        }

        /// <summary>One interface as a row of the master list, taken entirely from the reference table.</summary>
        private sealed class InterfaceListing {
            private readonly int[] fileIds;

            internal InterfaceListing(int groupId, int nameHash, int[] fileIds, int namedComponents) {
                GroupId = groupId;
                NameHash = nameHash;
                this.fileIds = fileIds;
                NamedComponents = namedComponents;
            }

            /// <summary>The interface id, which is the group id.</summary>
            internal int GroupId { get; }

            /// <summary>The group's stored identifier, or -1 when the table marks it unnamed.</summary>
            internal int NameHash { get; }

            /// <summary>How many components the table declares.</summary>
            internal int ComponentCount => fileIds.Length;

            /// <summary>How many of those components a name was recovered for.</summary>
            /// <remarks>
            ///     Recovered, not merely hashed. Every component in the vanilla capture carries an
            ///     identifier, so counting those would print the same number twice and say nothing.
            /// </remarks>
            internal int NamedComponents { get; }

            /// <summary>The interface's name where one is verifiable, otherwise blank.</summary>
            /// <remarks>
            ///     Blank rather than the hash. The hash has its own column, and putting a number here
            ///     would present it as a recovered name.
            /// </remarks>
            internal string Name => InterfaceNames.GroupName(GroupId, NameHash) ?? string.Empty;

            /// <summary>The name hash, or nothing when the table marks the group unnamed.</summary>
            /// <remarks>
            ///     Null rather than -1, so an unnamed group reads as an empty cell instead of sorting
            ///     as if it held a real hash.
            /// </remarks>
            internal object? NameHashOrNothing => NameHash == InterfaceNames.Unnamed ? null : NameHash;

            /// <summary>
            ///     The component ids the group declares, as a range.
            /// </summary>
            /// <remarks>
            ///     Worth a column because index 3's groups are sparse: the count and the highest id
            ///     disagree, so a reader who assumed 0..count-1 would ask for components that do not
            ///     exist.
            /// </remarks>
            internal string IdRange => fileIds.Length == 0
                ? "none"
                : fileIds[0] + ".." + fileIds[fileIds.Length - 1];
        }

        /// <summary>One field of the selected component, as a grid row.</summary>
        private sealed class FieldListing {
            internal FieldListing(string section, string name, string value) {
                Section = section;
                Name = name;
                Value = value;
            }

            /// <summary>Which block of the record the field came from.</summary>
            internal string Section { get; }

            /// <summary>The field's name.</summary>
            internal string Name { get; }

            /// <summary>The field's value, rendered.</summary>
            internal string Value { get; }
        }
    }
}
