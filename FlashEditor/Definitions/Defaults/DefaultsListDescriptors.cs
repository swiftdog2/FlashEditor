using System;
using System.Collections.Generic;
using FlashEditor.cache;
using FlashEditor.Definitions.Editing;

namespace FlashEditor.Definitions.Defaults {
    /// <summary>One named value of an index-28 record, as a detail-grid row.</summary>
    public sealed class DefaultsField {
        /// <summary>Names one value.</summary>
        /// <param name="name">What the value is.</param>
        /// <param name="value">The value, rendered.</param>
        public DefaultsField(string name, string value) {
            Name = name;
            Value = value;
        }

        /// <summary>What the value is.</summary>
        public string Name { get; }

        /// <summary>The value, rendered.</summary>
        public string Value { get; }
    }

    /// <summary>
    ///     What the Defaults tab needs from a row whichever of index 28's two records it holds.
    /// </summary>
    /// <remarks>
    ///     The two groups have nothing in common beyond living in the same index - one is a cube map
    ///     and two enum tables, the other a hitsplat layout and a benchmark model - so this is
    ///     deliberately the smallest surface the shared detail pane can be written against rather
    ///     than an attempt to unify the records.
    /// </remarks>
    public interface IDefaultsListing {
        /// <summary>Where the record lives in the cache.</summary>
        DefinitionAddress Address { get; }

        /// <summary>The record in one line, for the header above the detail grid.</summary>
        string Summary { get; }

        /// <summary>Every value the record carries, in the order the format states them.</summary>
        IReadOnlyList<DefaultsField> Fields { get; }
    }

    /// <summary>
    ///     Group 1 of index 28 as a list row: the default environment cube map and the player-title
    ///     enum tables.
    /// </summary>
    public sealed class SceneDefaultsListing : IDefaultsListing {
        /// <summary>Binds the decoded record to where it came from.</summary>
        /// <param name="address">The group and file.</param>
        /// <param name="record">The decoded record.</param>
        public SceneDefaultsListing(DefinitionAddress address, SceneDefaultsDefinition record) {
            Address = address;
            Record = record ?? throw new ArgumentNullException(nameof(record));
        }

        /// <inheritdoc/>
        public DefinitionAddress Address { get; }

        /// <summary>The decoded record.</summary>
        public SceneDefaultsDefinition Record { get; }

        /// <summary>The six texture ids of the cube map, or a note that the record omits them.</summary>
        public string CubeMap => DefaultsText.Ids(Record.CubeMapTextureIds);

        /// <summary>The male player-title enum ids.</summary>
        public string MaleTitles => DefaultsText.Ids(Record.MaleTitleEnumIds);

        /// <summary>The female player-title enum ids.</summary>
        /// <remarks>
        ///     Absent in both supported caches, and the absence is load bearing: the client branches
        ///     on the array being null, so materialising an empty one changes what it does.
        /// </remarks>
        public string FemaleTitles => DefaultsText.Ids(Record.FemaleTitleEnumIds);

        /// <summary>The opcodes the file stored, in the order it stored them.</summary>
        public string OpcodeOrder => DefaultsText.Order(Record.Opcodes);

        /// <inheritdoc/>
        public string Summary =>
            "Group 1 - default environment cube map and player titles - opcodes " + OpcodeOrder;

        /// <inheritdoc/>
        public IReadOnlyList<DefaultsField> Fields => new[] {
            new DefaultsField("Cube map texture ids (opcode 1)", CubeMap),
            new DefaultsField("Male title enums (opcode 4)", MaleTitles),
            new DefaultsField("Female title enums (opcode 5)", FemaleTitles),
            new DefaultsField("Stored opcode order", OpcodeOrder)
        };
    }

    /// <summary>
    ///     Group 3 of index 28 as a list row: hitsplat slots and the renderer's benchmark model.
    /// </summary>
    public sealed class HitsplatLayoutListing : IDefaultsListing {
        /// <summary>Binds the decoded record to where it came from.</summary>
        /// <param name="address">The group and file.</param>
        /// <param name="record">The decoded record.</param>
        public HitsplatLayoutListing(DefinitionAddress address, HitsplatLayoutDefinition record) {
            Address = address;
            Record = record ?? throw new ArgumentNullException(nameof(record));
        }

        /// <inheritdoc/>
        public DefinitionAddress Address { get; }

        /// <summary>The decoded record.</summary>
        public HitsplatLayoutDefinition Record { get; }

        /// <summary>
        ///     How many hitsplats an entity can show at once, and whether the file said so.
        /// </summary>
        /// <remarks>
        ///     Both, because absent and present-with-the-default-value are different bytes for the
        ///     same decoded state on this record.
        /// </remarks>
        public string SlotCount => Record.SlotCount +
            (Record.StoresSlotCount ? " (stored)" : " (absent, client default)");

        /// <summary>The per-slot draw offsets, as pairs.</summary>
        public string Offsets {
            get {
                short[]? x = Record.OffsetX;
                short[]? y = Record.OffsetY;
                if (x == null || y == null)
                    return "absent";

                var parts = new List<string>(x.Length);
                for (int slot = 0; slot < x.Length && slot < y.Length; slot++)
                    parts.Add(x[slot] + "," + y[slot]);
                return string.Join("  ", parts);
            }
        }

        /// <summary>The model the hardware renderer draws to benchmark itself.</summary>
        public string BenchmarkModel => Record.StoresBenchmarkModel
            ? Record.BenchmarkModelId.ToString()
            : "absent";

        /// <summary>The opcodes the file stored, in the order it stored them.</summary>
        /// <remarks>
        ///     Load bearing on this record rather than merely non-canonical: opcode 3 allocates the
        ///     arrays opcode 1 fills, so the count has to precede the offsets or the client reads the
        ///     wrong number of pairs and then discards them.
        /// </remarks>
        public string OpcodeOrder => DefaultsText.Order(Record.Opcodes);

        /// <inheritdoc/>
        public string Summary =>
            "Group 3 - hitsplat layout and benchmark model - opcodes " + OpcodeOrder;

        /// <inheritdoc/>
        public IReadOnlyList<DefaultsField> Fields => new[] {
            new DefaultsField("Hitsplat slots (opcode 3)", SlotCount),
            new DefaultsField("Draw offsets x,y per slot (opcode 1)", Offsets),
            new DefaultsField("Benchmark model (opcode 2)", BenchmarkModel),
            new DefaultsField("Stored opcode order", OpcodeOrder)
        };
    }

    /// <summary>Rendering shared by both index-28 records.</summary>
    internal static class DefaultsText {
        /// <summary>An id array, or a note that the record did not carry the opcode.</summary>
        /// <remarks>
        ///     "absent" rather than an empty string, because absent and empty are different states
        ///     here and the client branches on the difference.
        /// </remarks>
        /// <param name="ids">The ids, or null.</param>
        /// <returns>The ids, or "absent".</returns>
        internal static string Ids(int[]? ids) {
            if (ids == null)
                return "absent";
            if (ids.Length == 0)
                return "empty";
            return string.Join(", ", ids);
        }

        /// <summary>The recorded opcode stream as the order it was stored in.</summary>
        /// <param name="opcodes">The recorded stream.</param>
        /// <returns>The opcodes, comma separated.</returns>
        internal static string Order(OpcodeStream opcodes) {
            var parts = new List<string>(opcodes.Count);
            for (int i = 0; i < opcodes.Count; i++)
                parts.Add(opcodes[i].Opcode.ToString());
            return string.Join(",", parts);
        }
    }

    /// <summary>
    ///     The shared half of index 28's two descriptors: one group, whatever files it declares.
    /// </summary>
    /// <remarks>
    ///     Scoped to a group rather than listing the index, because index 28 is not a record table -
    ///     it holds two unrelated config blobs whose formats have no opcode in common. Two
    ///     descriptors over one index is what lets each state its own columns.
    /// </remarks>
    /// <typeparam name="TRow">The row type the group decodes to.</typeparam>
    public abstract class DefaultsGroupDescriptor<TRow> : DefinitionListDescriptor<TRow> where TRow : class {
        /// <summary>Lists one group of index 28.</summary>
        /// <param name="groupId">The group id, which the client reads by literal.</param>
        protected DefaultsGroupDescriptor(int groupId) {
            GroupId = groupId;
        }

        /// <summary>The group this descriptor presents.</summary>
        public int GroupId { get; }

        /// <inheritdoc/>
        public override int IndexId => RSConstants.DEFAULTS;

        /// <summary>
        ///     The files of this group alone.
        /// </summary>
        /// <remarks>
        ///     Driven off the reference table's file ids rather than assuming file 0. The client
        ///     reaches both groups through the single-file accessor, which throws when a group holds
        ///     anything other than one file (JS5Archive.java:612), but the id of that one file is
        ///     declared rather than fixed.
        /// </remarks>
        /// <param name="cache">The open cache.</param>
        /// <returns>The addresses to load.</returns>
        public override IEnumerable<DefinitionAddress> Enumerate(RSCache cache) {
            if (cache == null)
                throw new ArgumentNullException(nameof(cache));

            foreach (int file in cache.GetFileIds(IndexId, GroupId))
                yield return Address(GroupId, file);
        }
    }

    /// <summary>Group 1 of index 28 as a definition list.</summary>
    /// <remarks>
    ///     Read only. Both fields that could be edited are arrays whose length the client reads
    ///     structurally - six cube-map faces, and a title table indexed by the player's rank - and a
    ///     name/value grid is the wrong place to resize either.
    /// </remarks>
    public sealed class SceneDefaultsListDescriptor : DefaultsGroupDescriptor<SceneDefaultsListing> {
        private readonly IReadOnlyList<DefinitionColumn> columns;

        /// <summary>Lists group 1.</summary>
        public SceneDefaultsListDescriptor() : base(SceneDefaultsDefinition.GroupId) {
            columns = new[] {
                DefinitionColumn.ReadOnly<SceneDefaultsListing>("Group", row => row.Address.GroupId, 70),
                DefinitionColumn.ReadOnly<SceneDefaultsListing>("File", row => row.Address.FileId, 60),
                DefinitionColumn.ReadOnly<SceneDefaultsListing>("Cube map textures", row => row.CubeMap, 220),
                DefinitionColumn.ReadOnly<SceneDefaultsListing>("Male titles", row => row.MaleTitles, 140),
                DefinitionColumn.ReadOnly<SceneDefaultsListing>("Female titles", row => row.FemaleTitles, 140),
                DefinitionColumn.ReadOnly<SceneDefaultsListing>("Opcodes", row => row.OpcodeOrder, 110)
            };
        }

        /// <inheritdoc/>
        public override string RowNoun => "scene default";

        /// <inheritdoc/>
        public override IReadOnlyList<DefinitionColumn> Columns => columns;

        /// <inheritdoc/>
        public override SceneDefaultsListing Decode(RSCache cache, DefinitionAddress address, JagStream payload) {
            var record = new SceneDefaultsDefinition();
            record.Decode(payload);
            return new SceneDefaultsListing(address, record);
        }

        /// <inheritdoc/>
        public override DefinitionAddress AddressOf(SceneDefaultsListing row) {
            if (row == null)
                throw new ArgumentNullException(nameof(row));
            return row.Address;
        }
    }

    /// <summary>Group 3 of index 28 as a definition list.</summary>
    /// <remarks>
    ///     Read only, and for a sharper reason than group 1. The slot count allocates the offset
    ///     arrays and must be written before them, so changing one of the two without the other
    ///     produces a record the client mis-reads rather than an invalid one it refuses.
    /// </remarks>
    public sealed class HitsplatLayoutListDescriptor : DefaultsGroupDescriptor<HitsplatLayoutListing> {
        private readonly IReadOnlyList<DefinitionColumn> columns;

        /// <summary>Lists group 3.</summary>
        public HitsplatLayoutListDescriptor() : base(HitsplatLayoutDefinition.GroupId) {
            columns = new[] {
                DefinitionColumn.ReadOnly<HitsplatLayoutListing>("Group", row => row.Address.GroupId, 70),
                DefinitionColumn.ReadOnly<HitsplatLayoutListing>("File", row => row.Address.FileId, 60),
                DefinitionColumn.ReadOnly<HitsplatLayoutListing>("Slots", row => row.SlotCount, 200),
                DefinitionColumn.ReadOnly<HitsplatLayoutListing>("Offsets", row => row.Offsets, 300),
                DefinitionColumn.ReadOnly<HitsplatLayoutListing>("Benchmark model", row => row.BenchmarkModel, 140),
                DefinitionColumn.ReadOnly<HitsplatLayoutListing>("Opcodes", row => row.OpcodeOrder, 110)
            };
        }

        /// <inheritdoc/>
        public override string RowNoun => "hitsplat layout";

        /// <inheritdoc/>
        public override IReadOnlyList<DefinitionColumn> Columns => columns;

        /// <inheritdoc/>
        public override HitsplatLayoutListing Decode(RSCache cache, DefinitionAddress address, JagStream payload) {
            var record = new HitsplatLayoutDefinition();
            record.Decode(payload);
            return new HitsplatLayoutListing(address, record);
        }

        /// <inheritdoc/>
        public override DefinitionAddress AddressOf(HitsplatLayoutListing row) {
            if (row == null)
                throw new ArgumentNullException(nameof(row));
            return row.Address;
        }
    }
}
