using System.Collections.Generic;
using FlashEditor.Cache;
using FlashEditor.Definitions.Sprites;
using FlashEditor.Tests.Cache.RealCache;
using Xunit;
using Xunit.Abstractions;
using FlashEditor.IO;

namespace FlashEditor.Tests.Definitions
{
    /// <summary>
    ///     Byte-identity and edit-visibility sweeps over index 26, the material table.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///     The index is one group holding one file, so the sweep is over a single record - but that
    ///     record is the entire table, and the client sizes its texture array from the count inside
    ///     it. Exact consumption is therefore the whole statement about the column widths: the file
    ///     is <c>2 + count + present * 23</c> bytes, and a decoder that got any one of the nineteen
    ///     widths wrong cannot land on the last byte.
    ///     </para>
    ///     <para>
    ///     The declared count is read from the file rather than written down here. Index 26 declares
    ///     as many textures as index 9 holds graphs in the vanilla capture and more than that in the
    ///     repack, so any literal would be a fact about one of them.
    ///     </para>
    ///     <para>
    ///     Nothing here writes. The one test that drives the save path asserts it stages
    ///     <em>nothing</em>, which is the only assertion about writing that a read-only cache can
    ///     support - and is also the rule that matters most, since re-encoding an untouched table
    ///     would move the archive CRC and with it the reference-table entry of everything packed
    ///     beside it.
    ///     </para>
    /// </remarks>
    [Collection("RealCache")]
    public sealed class RealCacheMaterialTests : IClassFixture<RealCacheFixture>
    {
        private readonly RealCacheFixture _fixture;
        private readonly ITestOutputHelper _output;

        /// <summary>Binds the shared open cache and the per-test output sink.</summary>
        public RealCacheMaterialTests(RealCacheFixture fixture, ITestOutputHelper output)
        {
            _fixture = fixture;
            _output = output;
        }

        /// <summary>Groups the index-26 reference table declares.</summary>
        private int GroupsDeclared => _fixture.DeclaredGroups(RSConstants.MATERIALS);

        /// <summary>Files the index-26 reference table declares across every group.</summary>
        private int FilesDeclared => _fixture.DeclaredFiles(RSConstants.MATERIALS);

        /// <summary>The material table bound to the production codec.</summary>
        /// <remarks>
        ///     <c>NotOpcodeTerminated</c> because the file is not an opcode stream: it is a leading
        ///     count and nineteen fixed-width columns, and its last byte is one of them.
        /// </remarks>
        /// <returns>A sweep over index 26.</returns>
        private DefinitionSweep<MaterialTable> Sweep()
        {
            return new DefinitionSweep<MaterialTable>(_fixture, _output, RSConstants.MATERIALS,
                new DefinitionCodec<MaterialTable>("material table",
                    (_, stream) => MaterialTable.Decode(stream),
                    table => table.Encode()))
                .AcrossEveryGroup()
                .NotOpcodeTerminated();
        }

        /// <summary>Reads the table the same way the write path addresses it.</summary>
        /// <returns>The stored file bytes and the table decoded from them.</returns>
        private (byte[] Stored, MaterialTable Table) LoadTable()
        {
            RSCache cache = _fixture.OpenCache();
            CacheAddressing addressing = CacheAddressing.For(RSConstants.MATERIALS);
            byte[] stored = cache.ReadFileBytes(RSConstants.MATERIALS,
                addressing.GroupOf(MaterialTable.WholeTableDefinitionId),
                addressing.FileOf(MaterialTable.WholeTableDefinitionId));

            return (stored, MaterialTable.Decode(new JagStream(stored)));
        }

        /// <summary>The table decodes and finishes on the last byte of its file.</summary>
        [RealCacheFact]
        public void TheMaterialTable_DecodesAndConsumesItsBufferExactly()
        {
            DefinitionSweepResult swept = Sweep().AssertExactConsumption();

            Assert.True(FilesDeclared > 0, "index 26 declares no files, so nothing was checked");
            Assert.Equal(GroupsDeclared, swept.Groups);
            Assert.Equal(FilesDeclared, swept.Records);
            Assert.Equal(FilesDeclared, swept.Passed);
        }

        /// <summary>The table re-encodes to the bytes it was decoded from.</summary>
        /// <remarks>
        ///     The property the editor depends on, and the one the index has never had: the write
        ///     path used to hand back the captured blob whatever the fields held, so this passing
        ///     while edits were being discarded was exactly the state of the world. It is paired with
        ///     <see cref="AnEditedRecord_ChangesTheEncodedOutput"/> for that reason - neither claim
        ///     is worth anything without the other.
        /// </remarks>
        [RealCacheFact]
        public void TheMaterialTable_ReEncodesToItsStoredBytes()
        {
            DefinitionSweepResult swept = Sweep().AssertReEncodesToCapturedBytes();

            Assert.True(FilesDeclared > 0, "index 26 declares no files, so nothing was checked");
            Assert.Equal(FilesDeclared, swept.Records);
            Assert.Equal(FilesDeclared, swept.Passed);
            Assert.Equal(0, swept.Reordered);
        }

        /// <summary>
        ///     The declared count and the present records account for the file exactly, to the byte.
        /// </summary>
        /// <remarks>
        ///     Two claims, and neither replaces the other. The relationship - the file length is
        ///     exactly the count, the existence column and 23 bytes per present texture - pins the
        ///     format and holds in any cache, but it is satisfied by any pair of counts that agree
        ///     with the file, so a table that had gained or lost records would still pass it. The
        ///     absolute figures pin the population, and because the two supported caches disagree on
        ///     them - 915 slots against 1408 - they are asserted through the census, which is scoped
        ///     to the cache each was measured on.
        /// </remarks>
        [RealCacheFact]
        public void TheDeclaredTextureCount_AccountsForTheWholeFile()
        {
            (byte[] stored, MaterialTable table) = LoadTable();

            int present = 0;
            foreach (TextureDefinition def in table.Slots)
                if (def != null)
                    present++;

            _output.WriteLine($"index 26 declares {table.Count} textures, {present} of them present, " +
                              $"in {stored.Length} bytes");
            _fixture.Profile.AssertCensus(_output, "materials.declaredTextures", table.Count);
            _fixture.Profile.AssertCensus(_output, "materials.presentRecords", present);

            Assert.True(table.Count > 0, "index 26 declares no textures, so nothing was checked");
            Assert.Equal(2 + table.Count + present * MaterialTable.BytesPerRecord, stored.Length);
        }

        /// <summary>
        ///     Editing one field changes the encoded file, in exactly that field's bytes.
        /// </summary>
        /// <remarks>
        ///     The test the index was missing, and the one that would have caught what it did
        ///     instead. Every material field was editable in memory and none of it could reach the
        ///     cache, because the encoder returned the captured blob whenever there was one - which
        ///     the load path guarantees. A round trip cannot see that; only an edit can.
        ///     <para>
        ///     The width assertion is the second half. An encoder that rebuilt the whole file from
        ///     fields would also make this differ, and would quietly rewrite every column of every
        ///     record while doing it.
        ///     </para>
        /// </remarks>
        [RealCacheFact]
        public void AnEditedRecord_ChangesTheEncodedOutput()
        {
            (byte[] stored, MaterialTable table) = LoadTable();
            Assert.Equal(stored, table.Encode().ToArray());
            Assert.False(table.IsDirty);

            TextureDefinition edited = null;
            foreach (TextureDefinition def in table.Slots)
            {
                if (def == null)
                    continue;
                edited = def;
                break;
            }

            Assert.NotNull(edited);

            //Kept inside sixteen bits: the column is two bytes and an editor that offered more
            //would be offering something the format cannot store.
            int replacement = (edited.field1831 ^ 0x1234) & 0xFFFF;
            edited.field1831 = replacement;

            Assert.True(edited.IsDirty);
            Assert.True(table.IsDirty);

            byte[] reencoded = table.Encode().ToArray();
            Assert.Equal(stored.Length, reencoded.Length);

            int moved = 0;
            for (int i = 0; i < stored.Length; i++)
                if (stored[i] != reencoded[i])
                    moved++;

            _output.WriteLine($"editing texture {edited.id}'s field1831 moved {moved} of " +
                              $"{stored.Length} bytes");

            Assert.Equal(MaterialTable.WidthOf(MaterialColumn.Field1831), moved);

            //And the edit survives a decode, which is what the client would do with it.
            MaterialTable readBack = MaterialTable.Decode(new JagStream(reencoded));
            Assert.Equal(replacement, readBack.Slots[edited.id].field1831);

            //Every other record came back byte for byte.
            var differing = new List<int>();
            for (int slot = 0; slot < table.Count; slot++)
            {
                if (slot == edited.id || table.Slots[slot] == null)
                    continue;

                if (!ByteArrayEquals(table.Slots[slot].StoredRecord, readBack.Slots[slot].StoredRecord))
                    differing.Add(slot);
            }

            Assert.Empty(differing);
        }

        /// <summary>
        ///     Saving a table nobody edited stages nothing at all.
        /// </summary>
        /// <remarks>
        ///     Drives the real write path - encode, address, compare against the bytes the cache
        ///     holds - and asserts the outcome that leaves the cache untouched, so it can run against
        ///     a read-only cache without qualification. A save that changed nothing but rewrote the
        ///     archive would move its CRC and drag in the reference-table entry of everything packed
        ///     alongside it.
        /// </remarks>
        [RealCacheFact]
        public void SavingAnUntouchedTable_StagesNothing()
        {
            RSCache cache = _fixture.OpenCache();
            (_, MaterialTable table) = LoadTable();

            //Stated before the save as well, so a failure afterwards is attributable to it. This
            //fixture opens the cache and reads; nothing in the class stages anything.
            Assert.False(cache.HasUnsavedChanges, "reading index 26 must not stage an edit");
            Assert.False(table.IsDirty);
            Assert.False(table.SaveTo(cache), "an untouched material table must not be written back");
            Assert.False(cache.HasUnsavedChanges, "saving an untouched material table staged a write");
        }

        private static bool ByteArrayEquals(byte[] left, byte[] right)
        {
            if (left == null || right == null)
                return ReferenceEquals(left, right);
            if (left.Length != right.Length)
                return false;

            for (int i = 0; i < left.Length; i++)
                if (left[i] != right[i])
                    return false;

            return true;
        }
    }
}
