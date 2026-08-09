using System;
using System.Collections.Generic;
using FlashEditor.Cache;
using FlashEditor.Definitions.Editing;
using FlashEditor.IO;

namespace FlashEditor.Definitions.Entities {
    /// <summary>
    ///     One model the reference table declares, described without decoding it.
    /// </summary>
    /// <remarks>
    ///     A class rather than a struct because <c>DefinitionListDescriptor</c> is constrained to
    ///     reference types - ObjectListView holds rows as objects and compares them by reference,
    ///     so a boxed struct would fail to match the row it came from on a refresh.
    ///     <c>ModelReference</c> is a struct for exactly the reason this is not.
    /// </remarks>
    public sealed class ModelListing {
        /// <summary>Describes one model.</summary>
        /// <param name="address">Where the model file lives.</param>
        public ModelListing(DefinitionAddress address) {
            Address = address;
        }

        /// <summary>Where the model file lives in index 7.</summary>
        public DefinitionAddress Address { get; }

        /// <summary>
        ///     The model id, which is the group id.
        /// </summary>
        /// <remarks>
        ///     Index 7 is one group per id, established from the client rather than assumed: the
        ///     id is load bearing, since <c>Model.&lt;init&gt;:84</c> selects the new-protocol layout
        ///     from it, so a model decoded under the wrong group id is decoded with the wrong format.
        /// </remarks>
        public int ModelId => Address.GroupId;

        /// <summary>
        ///     The file within that group.
        /// </summary>
        /// <remarks>
        ///     Read off the reference table rather than assumed to be 0. Both caches declare exactly
        ///     one file per group across the whole index, and index 23 is the case that proves a
        ///     single-file group's id is not always zero.
        /// </remarks>
        public int FileId => Address.FileId;
    }

    /// <summary>
    ///     Index 7's models as a list of addresses, with nothing decoded.
    /// </summary>
    /// <remarks>
    ///     Replaces the bespoke arm the Models tab carried inside <c>Editor.LoadEditorTab</c>, which
    ///     already listed rather than decoded - so unlike the item, NPC and object migrations this
    ///     one is a straight move, and the reason <see cref="ReadsPayload"/> exists at all. Index 7
    ///     declares 63,607 groups in the vanilla capture and 63,614 in the repack, one file each;
    ///     opening every one of them to fill a column of ids would inflate every model in the cache
    ///     to print what the table already states.
    ///     <para>
    ///     Read only, and it must stay that way here. A model round-trips through
    ///     <c>ModelCodec</c> but nothing in the suite sweeps index 7 for byte identity, so
    ///     <see cref="DefinitionListDescriptor{TRow}.IsEditable"/> stays false and the panel switches
    ///     cell editing off entirely. The viewport beside the grid is where a model is inspected.
    ///     </para>
    /// </remarks>
    public sealed class ModelListDescriptor : DefinitionListDescriptor<ModelListing> {
        private static readonly IReadOnlyList<DefinitionColumn> ModelColumns = new[] {
            DefinitionColumn.ReadOnly<ModelListing>("Model", row => row.ModelId, 100),
            DefinitionColumn.ReadOnly<ModelListing>("File", row => row.FileId, 80)
        };

        /// <inheritdoc/>
        public override int IndexId => RSConstants.MODELS_INDEX;

        /// <inheritdoc/>
        public override string RowNoun => "model";

        /// <inheritdoc/>
        public override IReadOnlyList<DefinitionColumn> Columns => ModelColumns;

        /// <summary>
        ///     False: every column here comes from the reference table.
        /// </summary>
        /// <remarks>
        ///     The whole point of this descriptor. Leaving it true would decompress all 63,607 groups
        ///     on every visit to the page, which is a two-figure multiple of what the tab costs now
        ///     and buys nothing that is displayed.
        /// </remarks>
        public override bool ReadsPayload => false;

        /// <inheritdoc/>
        /// <remarks>
        ///     The payload is empty by contract, because <see cref="ReadsPayload"/> is false, and is
        ///     deliberately not read.
        /// </remarks>
        public override ModelListing Decode(RSCache cache, DefinitionAddress address, JagStream payload) {
            return new ModelListing(address);
        }

        /// <inheritdoc/>
        public override DefinitionAddress AddressOf(ModelListing row) {
            if (row == null)
                throw new ArgumentNullException(nameof(row));

            //Carried rather than derived: CacheAddressing.FileOf refuses to answer for a
            //group-per-id index, because the file id is declared by the table and not computed.
            return row.Address;
        }
    }
}
