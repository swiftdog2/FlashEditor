using System;
using System.Collections.Generic;
using FlashEditor.cache;
using FlashEditor.Definitions.Editing;

namespace FlashEditor.Definitions.Interfaces {
    /// <summary>
    ///     One decoded interface component as the editor's list shows it.
    /// </summary>
    /// <remarks>
    ///     A thin wrapper rather than columns hung straight off
    ///     <see cref="InterfaceComponentDefinition"/>, because the two names and the address come
    ///     from the reference table and not from the component's own bytes.
    /// </remarks>
    public sealed class InterfaceComponentRow {
        /// <summary>Binds a decoded component to its address and the names the table gives it.</summary>
        /// <param name="address">Where the component is stored.</param>
        /// <param name="component">The decoded component.</param>
        /// <param name="groupNameHash">The interface's name hash, or -1 when unnamed.</param>
        /// <param name="componentNameHash">The component's name hash, or -1 when unnamed.</param>
        public InterfaceComponentRow(DefinitionAddress address, InterfaceComponentDefinition component,
            int groupNameHash, int componentNameHash) {
            Address = address;
            Component = component ?? throw new ArgumentNullException(nameof(component));
            GroupNameHash = groupNameHash;
            ComponentNameHash = componentNameHash;
        }

        /// <summary>Where the component is stored.</summary>
        public DefinitionAddress Address { get; }

        /// <summary>The decoded component.</summary>
        public InterfaceComponentDefinition Component { get; }

        /// <summary>The interface's name hash, or -1.</summary>
        public int GroupNameHash { get; }

        /// <summary>The component's name hash, or -1.</summary>
        public int ComponentNameHash { get; }

        /// <summary>The interface id.</summary>
        public int GroupId => Address.GroupId;

        /// <summary>The component's index within its interface.</summary>
        public int FileId => Address.FileId;

        /// <summary>
        ///     The interface's name if one is verifiable, otherwise its hash, otherwise nothing.
        /// </summary>
        /// <remarks>
        ///     A recovered name never replaces the hash silently: where no curated entry hashes to
        ///     what the table holds, the number is what is shown, because that is what is actually
        ///     known.
        /// </remarks>
        public string InterfaceName =>
            InterfaceNames.GroupName(GroupId, GroupNameHash)
            ?? (GroupNameHash == InterfaceNames.Unnamed ? "" : GroupNameHash.ToString());

        /// <summary>The component's name if one is verifiable, otherwise its hash, otherwise nothing.</summary>
        public string ComponentName =>
            InterfaceNames.ComponentName(FileId, ComponentNameHash)
            ?? (ComponentNameHash == InterfaceNames.Unnamed ? "" : ComponentNameHash.ToString());

        /// <summary>The component type, named where the client does something with it.</summary>
        public string TypeName => Component.ComponentType switch {
            0 => "0 layer",
            3 => "3 rectangle",
            4 => "4 text",
            5 => "5 sprite",
            6 => "6 model",
            9 => "9 line",
            //1, 2, 7 and 8 are expressible and nothing in the client reads them; 10..127 read no
            //type block at all. None occurs in this cache, so a bare number is the honest label.
            _ => Component.ComponentType.ToString()
        };

        /// <summary>The base rectangle, as the layout pass starts from it.</summary>
        public string Bounds =>
            Component.BasePositionX + "," + Component.BasePositionY + " " +
            Component.BaseWidth + "x" + Component.BaseHeight;

        /// <summary>The parent component's index within the same interface, or blank for a root.</summary>
        public string Parent =>
            Component.RawParentId == InterfaceComponentDefinition.NoParent
                ? ""
                : Component.RawParentId.ToString();

        /// <summary>The text a text component draws, or the tooltip, or nothing.</summary>
        /// <remarks>
        ///     One column rather than three, because the three are mutually exclusive in practice and
        ///     a list of 42,256 rows cannot afford a column that is blank on 41,000 of them.
        /// </remarks>
        public string Text {
            get {
                if (Component.ComponentType == 4 && !Component.Message.IsEmpty)
                    return Component.Message.Text;
                if (!Component.Tooltip.IsEmpty)
                    return Component.Tooltip.Text;
                return Component.ContextOptions.Count > 0 ? Component.ContextOptions[0].Text : "";
            }
        }

        /// <summary>The media the component draws: a sprite id, a model id, or nothing.</summary>
        public string Media => Component.ComponentType switch {
            5 => Component.SpriteId.ToString(),
            6 => Component.ModelId.ToString(),
            _ => ""
        };
    }

    /// <summary>
    ///     Index 3 presented as a list of components, one row per file, over one interface or over
    ///     all of them.
    /// </summary>
    /// <remarks>
    ///     Editable, because <see cref="InterfaceComponentDefinition.Encode"/> reproduces all 42,256
    ///     of this cache's components byte for byte - the condition
    ///     <see cref="DefinitionListDescriptor{TRow}"/> sets for turning cell editing on at all.
    ///     <para>
    ///     Only the fields that are safe to change in isolation are editable. Position, size and the
    ///     four resolution modes are; the parent is not, because a component's parent is stored as a
    ///     sixteen-bit sibling index and re-pointing one silently re-parents a subtree, and neither
    ///     are the hook arrays, which are CS2 bytecode operands rather than values.
    ///     </para>
    /// </remarks>
    public sealed class InterfaceComponentListDescriptor : DefinitionListDescriptor<InterfaceComponentRow> {
        /// <summary>The group argument that means "every interface in the index".</summary>
        public const int AllInterfaces = -1;

        private readonly int scopedGroupId;

        /// <summary>Lists every component of every interface.</summary>
        public InterfaceComponentListDescriptor() : this(AllInterfaces) {
        }

        /// <summary>
        ///     Lists the components of one interface, or of all of them.
        /// </summary>
        /// <remarks>
        ///     Scoping is a constructor argument rather than a mutable property because
        ///     <c>DefinitionListPanel.Bind</c> keys on descriptor <i>identity</i> - a rebind of the
        ///     same instance is a deliberate no-op, so a panel that changed the group in place would
        ///     go on showing the previous interface's components.
        /// </remarks>
        /// <param name="groupId">The interface to list, or <see cref="AllInterfaces"/> for the whole index.</param>
        public InterfaceComponentListDescriptor(int groupId) {
            scopedGroupId = groupId;
        }

        private static readonly IReadOnlyList<DefinitionColumn> ComponentColumns = new[] {
            DefinitionColumn.ReadOnly<InterfaceComponentRow>("Interface", row => row.GroupId, 70),
            DefinitionColumn.ReadOnly<InterfaceComponentRow>("Component", row => row.FileId, 80),
            DefinitionColumn.ReadOnly<InterfaceComponentRow>("Name", row => row.InterfaceName, 120),
            DefinitionColumn.ReadOnly<InterfaceComponentRow>("Component name", row => row.ComponentName, 130),
            DefinitionColumn.ReadOnly<InterfaceComponentRow>("Type", row => row.TypeName, 90),
            DefinitionColumn.Number<InterfaceComponentRow>("X", row => row.Component.BasePositionX,
                (row, value) => row.Component.BasePositionX = value, 60),
            DefinitionColumn.Number<InterfaceComponentRow>("Y", row => row.Component.BasePositionY,
                (row, value) => row.Component.BasePositionY = value, 60),
            DefinitionColumn.Number<InterfaceComponentRow>("Width", row => row.Component.BaseWidth,
                (row, value) => row.Component.BaseWidth = value, 60),
            DefinitionColumn.Number<InterfaceComponentRow>("Height", row => row.Component.BaseHeight,
                (row, value) => row.Component.BaseHeight = value, 60),
            DefinitionColumn.ReadOnly<InterfaceComponentRow>("Parent", row => row.Parent, 60),
            DefinitionColumn.ReadOnly<InterfaceComponentRow>("Hidden", row => row.Component.IsHidden, 60),
            DefinitionColumn.ReadOnly<InterfaceComponentRow>("Media", row => row.Media, 70),
            DefinitionColumn.Text<InterfaceComponentRow>("Text", row => row.Text, null, 240),
            DefinitionColumn.ReadOnly<InterfaceComponentRow>("Hooks", row => row.Component.HookArrayCount, 60),
            DefinitionColumn.ReadOnly<InterfaceComponentRow>("Mask",
                row => "0x" + row.Component.AccessMask.ToString("X6"), 90)
        };

        /// <inheritdoc/>
        public override int IndexId => RSConstants.INTERFACE_DEFINITIONS_INDEX;

        /// <inheritdoc/>
        public override string RowNoun => "interface component";

        /// <inheritdoc/>
        public override IReadOnlyList<DefinitionColumn> Columns => ComponentColumns;

        /// <inheritdoc/>
        public override bool IsEditable => true;

        /// <summary>The interface this descriptor is scoped to, or <see cref="AllInterfaces"/>.</summary>
        public int ScopedGroupId => scopedGroupId;

        /// <summary>
        ///     Every component of the scoped interface, or of the whole index.
        /// </summary>
        /// <remarks>
        ///     Filtered here rather than by loading all 42,256 rows and hiding most of them. The
        ///     panel's loader groups its addresses and reads one container per group, so a scoped
        ///     enumeration costs exactly one group decode instead of 1,078.
        /// </remarks>
        /// <param name="cache">The open cache.</param>
        /// <returns>The addresses to load.</returns>
        public override IEnumerable<DefinitionAddress> Enumerate(RSCache cache) {
            if (cache == null)
                throw new ArgumentNullException(nameof(cache));

            return scopedGroupId == AllInterfaces ? base.Enumerate(cache) : ScopedAddresses(cache);
        }

        /// <summary>The scoped group's declared files, in ascending id order.</summary>
        /// <param name="cache">The open cache.</param>
        /// <returns>The addresses.</returns>
        private IEnumerable<DefinitionAddress> ScopedAddresses(RSCache cache) {
            foreach (int file in cache.GetFileIds(IndexId, scopedGroupId))
                yield return Address(scopedGroupId, file);
        }

        /// <inheritdoc/>
        public override InterfaceComponentRow Decode(RSCache cache, DefinitionAddress address, JagStream payload) {
            if (cache == null)
                throw new ArgumentNullException(nameof(cache));
            if (payload == null)
                throw new ArgumentNullException(nameof(payload));

            var component = new InterfaceComponentDefinition(address.GroupId, address.FileId).Decode(payload);

            RSArchiveEntry? group = cache.GetReferenceTable(IndexId).GetArchiveEntry(address.GroupId);
            RSFileEntry? file = group?.GetFileEntry(address.FileId);

            return new InterfaceComponentRow(address, component,
                group?.GetIdentifier() ?? InterfaceNames.Unnamed,
                file?.GetIdentifier() ?? InterfaceNames.Unnamed);
        }

        /// <inheritdoc/>
        public override DefinitionAddress AddressOf(InterfaceComponentRow row) {
            if (row == null)
                throw new ArgumentNullException(nameof(row));
            return row.Address;
        }

        /// <inheritdoc/>
        public override JagStream Encode(InterfaceComponentRow row) {
            if (row == null)
                throw new ArgumentNullException(nameof(row));
            return row.Component.Encode();
        }
    }
}
