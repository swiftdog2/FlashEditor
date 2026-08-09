using System;
using System.Collections.Generic;
using FlashEditor;
using FlashEditor.Cache;
using FlashEditor.Definitions.Editing;
using Xunit;
using FlashEditor.IO;

namespace FlashEditor.Tests.Definitions
{
    /// <summary>
    ///     The parts of the definition-list descriptor contract that hold with no cache open.
    /// </summary>
    /// <remarks>
    ///     These are the rules a new index editor is written against, so they are worth stating
    ///     independently of any one index: a read-only descriptor must refuse to encode, an index
    ///     with an established split must fill in the definition id, and an index without one must
    ///     leave it absent rather than fabricate it.
    /// </remarks>
    public sealed class DefinitionListDescriptorTests
    {
        /// <summary>
        ///     An index whose group/file split is recorded gets its definition id filled in.
        /// </summary>
        /// <remarks>
        ///     Index 19 pages 256 ids to a group, so file 7 of group 3 is item 775. The point is that
        ///     the panel never has to know that: the split comes from <see cref="CacheAddressing"/>,
        ///     which is where every index states it once.
        /// </remarks>
        [Fact]
        public void Address_DerivesTheDefinitionId_ForAPagedIndex()
        {
            var descriptor = new ProbeDescriptor(RSConstants.ITEM_DEFINITIONS_INDEX);

            DefinitionAddress address = descriptor.AddressFor(3, 7);

            Assert.True(address.HasDefinitionId);
            Assert.Equal(3 * 256 + 7, address.DefinitionId);
            Assert.Equal(3, address.GroupId);
            Assert.Equal(7, address.FileId);
        }

        /// <summary>
        ///     An index with no recorded split has no definition id, rather than a made-up one.
        /// </summary>
        /// <remarks>
        ///     Index 2 is the case: <see cref="CacheAddressing.For"/> throws for it, because it holds
        ///     thirty-five unrelated config families in one index and no arithmetic relates a family's
        ///     id to a group. A panel that answered <c>group * 256 + file</c> anyway would hand every
        ///     caller downstream a number that reads exactly like a real id and names a different file
        ///     the moment it is folded back.
        ///     <para>
        ///     This test named index 3 until its split was settled from the client -
        ///     <c>EntityEnumType.java:46</c> folds a component id as <c>(group &lt;&lt; 16) | file</c> -
        ///     so index 2 is now the standing example. The property under test is unchanged.
        ///     </para>
        /// </remarks>
        [Fact]
        public void Address_LeavesTheDefinitionIdAbsent_ForAnIndexWithNoRecordedSplit()
        {
            Assert.False(CacheAddressing.TryGetFor(RSConstants.CONFIG, out _));

            var descriptor = new ProbeDescriptor(RSConstants.CONFIG);

            DefinitionAddress address = descriptor.AddressFor(772, 4);

            Assert.False(address.HasDefinitionId);
            Assert.Equal(-1, address.DefinitionId);
            Assert.Equal(772, address.GroupId);
            Assert.Equal(4, address.FileId);
        }

        /// <summary>
        ///     A name-hashed index gets no definition id either.
        /// </summary>
        /// <remarks>
        ///     Its split is recorded, so <c>TryGetFor</c> succeeds - but the recorded answer is that
        ///     no arithmetic relates an id to a group, and asking <c>DefinitionId</c> anyway throws.
        ///     A descriptor for index 5 must reach a listing, not an exception.
        /// </remarks>
        [Fact]
        public void Address_LeavesTheDefinitionIdAbsent_ForANameHashedIndex()
        {
            var descriptor = new ProbeDescriptor(RSConstants.MAPS_INDEX);

            DefinitionAddress address = descriptor.AddressFor(50, 0);

            Assert.False(address.HasDefinitionId);
        }

        /// <summary>
        ///     A descriptor that has not implemented an encoder refuses to be asked for one.
        /// </summary>
        /// <remarks>
        ///     This is what keeps a listing of an undecoded index from offering an edit that would
        ///     write nonsense. The panel gates editing on <c>IsEditable</c>, and the throw is the
        ///     backstop for anything that reaches <c>Encode</c> another way.
        /// </remarks>
        [Fact]
        public void Encode_ThrowsByDefault_SoAReadOnlyIndexCannotBeWritten()
        {
            var descriptor = new ProbeDescriptor(RSConstants.CONFIG);

            Assert.False(descriptor.IsEditable);
            Assert.Throws<NotSupportedException>(() => descriptor.Encode(new ProbeRow()));
        }

        /// <summary>
        ///     A read-only column carries no setter, and an editable one carries one that converts.
        /// </summary>
        /// <remarks>
        ///     The conversion matters because the cell editor decides the type it hands back - a
        ///     <c>NumericUpDown</c> yields a <c>decimal</c>, a text box a <c>string</c> - so a
        ///     column that cast the value directly would throw on whichever editor it was not
        ///     written for.
        /// </remarks>
        [Fact]
        public void Number_ConvertsWhateverTheCellEditorHandsBack()
        {
            DefinitionColumn readOnly = DefinitionColumn.ReadOnly<ProbeRow>("Value", row => row.Value);
            DefinitionColumn editable = DefinitionColumn.Number<ProbeRow>("Value", row => row.Value,
                (row, value) => row.Value = value);

            Assert.False(readOnly.IsEditable);
            Assert.True(editable.IsEditable);

            var target = new ProbeRow();

            editable.Write!(target, 12m);
            Assert.Equal(12, target.Value);

            editable.Write!(target, "34");
            Assert.Equal(34, target.Value);

            Assert.Equal(34, editable.Read(target));
        }

        /// <summary>A column handed the wrong row type says so rather than returning nothing.</summary>
        [Fact]
        public void AColumnRejectsARowItWasNotWrittenFor()
        {
            DefinitionColumn column = DefinitionColumn.ReadOnly<ProbeRow>("Value", row => row.Value);

            Assert.Throws<ArgumentException>(() => column.Read("not a row"));
        }

        private sealed class ProbeRow
        {
            public int Value { get; set; }
        }

        /// <summary>
        ///     A descriptor that exists only to reach the protected address helper.
        /// </summary>
        private sealed class ProbeDescriptor : DefinitionListDescriptor<ProbeRow>
        {
            private readonly int _indexId;

            internal ProbeDescriptor(int indexId)
            {
                _indexId = indexId;
            }

            public override int IndexId => _indexId;

            public override string RowNoun => "probe row";

            public override IReadOnlyList<DefinitionColumn> Columns => Array.Empty<DefinitionColumn>();

            public override ProbeRow Decode(RSCache cache, DefinitionAddress address, JagStream payload)
            {
                return new ProbeRow();
            }

            public override DefinitionAddress AddressOf(ProbeRow row) => new DefinitionAddress(0, 0);

            internal DefinitionAddress AddressFor(int groupId, int fileId) => Address(groupId, fileId);
        }
    }
}
