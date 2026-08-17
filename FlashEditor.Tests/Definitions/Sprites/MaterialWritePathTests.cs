using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using FlashEditor;
using FlashEditor.Cache;
using FlashEditor.Definitions.Editing;
using FlashEditor.Definitions.Sprites;
using FlashEditor.IO;
using Xunit;

namespace FlashEditor.Tests.Definitions.Sprites
{
    /// <summary>
    ///     The write path behind the Materials tab: what an edit stages, and what an edit that was
    ///     undone stages.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///     <b>Index 26 could be edited and saved and the cache would not move.</b> The encoder
    ///     returned the stored blob whenever it had one, so every field edit was discarded in
    ///     silence, and no round trip of an unedited table could ever have said so. The per-column
    ///     dirty flag fixed that; these tests cover the half of the claim the byte-identity sweep
    ///     still cannot reach - <b>set a field, set it back, land on the original stored bytes</b> -
    ///     and they drive it through the same objects the grid edits and the same encoder the tab's
    ///     commit calls.
    ///     </para>
    ///     <para>
    ///     The cache is synthetic and built in a temp directory. The real cache is read-only, and a
    ///     persistence claim has to be checked by reopening the store rather than by reading back
    ///     through the <see cref="RSCache"/> that did the writing, which answers from its own overlay
    ///     whether or not anything was committed. The file it is seeded with carries a boolean byte
    ///     of 2 - false to the client, and not recoverable from the bool - because neither supported
    ///     cache holds one and it is the byte an over-eager encoder destroys.
    ///     </para>
    ///     <para>
    ///     In the "RealCache" collection because the descriptor drives <c>TextureManager</c>'s static
    ///     store, which <c>Clear</c> disposes out from under anything else reading it.
    ///     </para>
    /// </remarks>
    [Collection("RealCache")]
    public class MaterialWritePathTests : IDisposable
    {
        private const int SectorSize = 520;   // RSSector.SIZE is static readonly, unusable in a const context

        /// <summary>The group and file index 26 is stored as, which the client reads as (0, 0).</summary>
        private static readonly CacheAddressing Addressing = CacheAddressing.For(RSConstants.MATERIALS);

        private readonly string _dir;
        private readonly List<RSFileStore> _stores = new List<RSFileStore>();

        public MaterialWritePathTests()
        {
            _dir = Path.Combine(Path.GetTempPath(), "fe-material-write-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_dir);
        }

        public void Dispose()
        {
            //The static texture store may be holding definitions decoded from a cache that is about
            //to be deleted, and Clear is what releases them.
            TextureManager.Clear();

            //Each store holds an exclusive handle on its dat2 and must be released before the temp
            //directory can be removed.
            foreach (RSFileStore store in _stores)
                store.Dispose();

            if (Directory.Exists(_dir))
                Directory.Delete(_dir, true);
        }

        // ===================================================================
        //  Seeding
        // ===================================================================

        /// <summary>Builds a cache whose index 26 is one group holding one file: the given table.</summary>
        /// <param name="file">The index-26 file to store.</param>
        /// <returns>The open cache.</returns>
        private RSCache CreateCache(byte[] file)
        {
            //Sector 0 is burned: allocation derives the next free sector from the data length, and
            //sector id 0 is the end-of-chain marker.
            File.WriteAllBytes(Path.Combine(_dir, "main_file_cache.dat2"), new byte[SectorSize]);
            File.WriteAllBytes(Path.Combine(_dir, "main_file_cache.idx" + RSConstants.MATERIALS), Array.Empty<byte>());
            File.WriteAllBytes(Path.Combine(_dir, "main_file_cache.idx" + RSConstants.META_INDEX),
                               new byte[RSConstants.MATERIALS * RSIndex.SIZE]);

            var store = new RSFileStore(_dir);
            _stores.Add(store);

            var archive = new RSArchive();
            archive.PutFile(FileId, new JagStream(file));

            store.Write(RSConstants.MATERIALS, GroupId,
                        new RSContainer(RSConstants.MATERIALS, GroupId,
                                        RSConstants.GZIP_COMPRESSION, archive.Encode(), 1337).Encode());
            store.Write(RSConstants.META_INDEX, RSConstants.MATERIALS, EncodeReferenceTable());

            return new RSCache(store);
        }

        private static int GroupId => Addressing.GroupOf(MaterialTable.WholeTableDefinitionId);

        private static int FileId => Addressing.FileOf(MaterialTable.WholeTableDefinitionId);

        private static JagStream EncodeReferenceTable()
        {
            var table = new RSReferenceTable { format = 6, version = 1, flags = 0 };

            var entry = new RSArchiveEntry(GroupId);
            entry.SetVersion(1);
            entry.SetValidFileIds(new[] { FileId });
            entry.SetFileEntries(new SortedDictionary<int, RSFileEntry> { { FileId, new RSFileEntry(FileId) } });
            table.PutArchiveEntry(GroupId, entry);

            return new RSContainer(RSConstants.META_INDEX, RSConstants.MATERIALS,
                                   RSConstants.GZIP_COMPRESSION, ReferenceTableCodec.Encode(table), 1337).Encode();
        }

        /// <summary>Commits the cache to a fresh directory and reopens it through a new file store.</summary>
        private RSCache SaveAndReopen(RSCache cache)
        {
            string outDir = Path.Combine(_dir, "out-" + Guid.NewGuid().ToString("N"));
            cache.WriteCache(outDir);

            var reopened = new RSFileStore(outDir);
            _stores.Add(reopened);
            return new RSCache(reopened);
        }

        private static byte[] StoredFile(RSCache cache)
        {
            return cache.ReadFileBytes(RSConstants.MATERIALS, GroupId, FileId);
        }

        // ===================================================================
        //  The commit
        // ===================================================================

        /// <summary>A table nobody has edited stages nothing at all.</summary>
        /// <remarks>
        ///     The baseline every other test here rests on. Re-encoding rewrites the stored bytes and
        ///     therefore the archive CRC, which drags in the reference-table entry of everything
        ///     packed beside it, so "save changed nothing" has to mean no write rather than an
        ///     identical one.
        /// </remarks>
        [Fact]
        public void AnUneditedTable_StagesNothing()
        {
            (_, _, byte[] file) = MaterialFileBuilder.FileWithAnAliasedBoolean();
            RSCache cache = CreateCache(file);

            MaterialTable table = MaterialTable.Load(cache);

            Assert.False(table.IsDirty);
            Assert.False(table.SaveTo(cache), "an untouched table must not write");
            Assert.Equal(file, StoredFile(cache));
        }

        /// <summary>
        ///     An edited field is staged, and is still there after the cache has been written out and
        ///     reopened.
        /// </summary>
        /// <remarks>
        ///     The claim the whole tab rests on. Read back through a reopened store, because a read
        ///     through the cache that did the writing answers from its own staged overlay and says
        ///     the same thing whether or not anything reached the filesystem.
        /// </remarks>
        [Fact]
        public void AnEditedField_IsStaged_AndSurvivesASaveAndReopen()
        {
            (_, _, byte[] file) = MaterialFileBuilder.FileWithAnAliasedBoolean();
            RSCache cache = CreateCache(file);

            MaterialTable table = MaterialTable.Load(cache);
            table.Slots[1].representativeHsl = 0x1234;

            Assert.True(table.SaveTo(cache), "an edited table has to stage a write");

            RSCache reopened = SaveAndReopen(cache);
            MaterialTable persisted = MaterialTable.Decode(new JagStream(StoredFile(reopened)));

            Assert.Equal(0x1234, persisted.Slots[1].representativeHsl);

            //And the record nobody touched came back byte for byte, aliased boolean included.
            Assert.Equal(MaterialFileBuilder.AliasedBooleanByte,
                         persisted.Slots[0].StoredRecord[MaterialTable.OffsetOf(MaterialColumn.Force64x64)]);
        }

        /// <summary>
        ///     A field set and then set back stages nothing.
        /// </summary>
        /// <remarks>
        ///     The check the constraints section requires of every new edit path, and a different
        ///     claim from the byte-identity sweep: that proves an <i>unedited</i> record re-encodes to
        ///     what it was read from, where this is about an edit that nets nothing. Four real defects
        ///     in this repository have lived in the gap between the two.
        /// </remarks>
        [Fact]
        public void AFieldSetAndSetBack_StagesNothing()
        {
            (_, _, byte[] file) = MaterialFileBuilder.FileWithAnAliasedBoolean();
            RSCache cache = CreateCache(file);

            MaterialTable table = MaterialTable.Load(cache);
            TextureDefinition record = table.Slots[1];

            int hsl = record.representativeHsl;
            record.representativeHsl = hsl ^ 0x0FF0;
            Assert.True(table.IsDirty, "the edit has to register before its undoing means anything");

            record.representativeHsl = hsl;

            Assert.False(table.IsDirty);
            Assert.False(table.SaveTo(cache), "an edit that was undone must not write");
            Assert.Equal(file, StoredFile(cache));
        }

        /// <summary>
        ///     Unsetting a flag whose stored byte is neither 0 nor 1 stages nothing, rather than
        ///     normalising the byte.
        /// </summary>
        /// <remarks>
        ///     The case that makes this family worth writing. A boolean column decodes many-to-one, so
        ///     the bool cannot reproduce a stored 2; an encoder that rebuilt the column from its field
        ///     would write a 0, producing a file of the right length with a byte in it that nobody
        ///     edited. Neither supported cache carries such a byte, so no sweep over either would ever
        ///     catch it.
        /// </remarks>
        [Fact]
        public void AFlagSetAndUnset_StagesNothing_EvenWhenItsStoredByteIsAliased()
        {
            (_, _, byte[] file) = MaterialFileBuilder.FileWithAnAliasedBoolean();
            RSCache cache = CreateCache(file);

            MaterialTable table = MaterialTable.Load(cache);
            TextureDefinition aliased = table.Slots[0];

            Assert.False(aliased.force64x64, "a boolean byte of 2 is false to the client");

            aliased.force64x64 = true;
            Assert.True(table.IsDirty);

            aliased.force64x64 = false;

            Assert.False(table.IsDirty);
            Assert.False(table.SaveTo(cache), "a flag put back where it was must not write");
            Assert.Equal(file, StoredFile(cache));
        }

        // ===================================================================
        //  The grid's own path
        // ===================================================================

        /// <summary>
        ///     Every slot the table declares is a row, whether or not index 9 holds a graph for it.
        /// </summary>
        /// <remarks>
        ///     This cache has no index 9 at all, which is the extreme of the case that matters: in the
        ///     repack the tail of index 26 has no procedural content, and for those ids the
        ///     representative colour is the whole of what a player sees. A grid built from the graphs
        ///     rather than from the table would show nothing here.
        /// </remarks>
        [Fact]
        public void TheDescriptorListsEverySlotTheTableDeclares_IncludingThoseWithNoGraph()
        {
            (byte[] existence, _, byte[] file) = MaterialFileBuilder.FileWithAnAliasedBoolean();
            RSCache cache = CreateCache(file);

            var descriptor = new MaterialListDescriptor();
            List<DefinitionAddress> addresses = descriptor.Enumerate(cache).ToList();

            Assert.Equal(existence.Count(flag => flag == 1), addresses.Count);

            foreach (DefinitionAddress address in addresses)
            {
                //One file for the whole index, so the address is constant and the slot rides in the
                //definition id - which is what lets a link from elsewhere select a texture id here.
                Assert.Equal(GroupId, address.GroupId);
                Assert.Equal(FileId, address.FileId);

                var listing = (MaterialListing) ((IDefinitionListDescriptor) descriptor)
                    .Decode(cache, address, new JagStream(Array.Empty<byte>()));

                Assert.Equal(address.DefinitionId, listing.TextureId);
                Assert.Null(listing.Record.graph);
                Assert.Equal("none", listing.GraphState);
            }
        }

        /// <summary>
        ///     An edit made through the grid's own column reaches the encoder, and undoing it through
        ///     the same column reaches it too.
        /// </summary>
        /// <remarks>
        ///     Driven through <see cref="DefinitionColumn.Write"/> rather than by assigning the field,
        ///     because that is the path a cell edit takes and it is where the dirty flag has to land.
        ///     The encoder is the descriptor's, so this is the byte sequence the tab's commit compares
        ///     against what the cache holds.
        /// </remarks>
        [Fact]
        public void EditingThroughAGridColumn_ReachesTheEncoder_AndUndoingItReachesItToo()
        {
            (_, _, byte[] file) = MaterialFileBuilder.FileWithAnAliasedBoolean();
            RSCache cache = CreateCache(file);

            var descriptor = new MaterialListDescriptor();
            DefinitionAddress address = descriptor.Enumerate(cache).Last();
            MaterialListing listing = descriptor.Decode(cache, address, new JagStream(Array.Empty<byte>()));

            //The heading carries the client field in brackets, which is what lets a reader check the
            //name against HydraScape/client/src, so it is part of the string to match on.
            DefinitionColumn colour =
                descriptor.Columns.Single(column => column.Header == "representativeHsl (aShort1831)");
            Assert.True(colour.IsEditable, "the representative colour is the one column a user most wants to change");

            string original = (string) colour.Read(listing)!;
            colour.Write!(listing, "0x1234");

            byte[] edited = descriptor.Encode(listing).ToArray();
            Assert.NotEqual(file, edited);
            Assert.Equal(0x1234, MaterialTable.Decode(new JagStream(edited)).Slots[listing.TextureId].representativeHsl);

            //The cell text is the stored 16-bit HSL rather than the RGB the swatch shows, so putting
            //it back is what the user typing the old value again would do.
            colour.Write(listing, original);

            Assert.Equal(file, descriptor.Encode(listing).ToArray());
        }
    }
}
