using FlashEditor;
using FlashEditor.cache;
using FlashEditor.Definitions.Editing;
using FlashEditor.Tests.Cache.RealCache;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Xunit;

namespace FlashEditor.Tests.Cache
{
    /// <summary>
    ///     Covers the write path behind the NPC grid: the commit an edited row runs, and whether
    ///     what it stages is still there after the cache has been written out and reopened.
    /// </summary>
    /// <remarks>
    ///     Index 18's codec has been swept byte for byte since long before this, and none of that
    ///     touched the write. The commit was complete, correct and unreachable - the grid's
    ///     <c>CellEditActivation</c> was never set, so ObjectListView never raised the handler that
    ///     called it, and nothing in the suite called it either. Exercising
    ///     <see cref="DefinitionWriter.Save"/> here is exercising the same code the handler runs,
    ///     which is why the commit lives outside the form.
    ///     <para>
    ///     The definitions are seeded from the real cache but written into a synthetic one in a temp
    ///     directory: the real cache is read-only, and a persistence claim has to be checked by
    ///     reopening the store rather than by reading back through the <see cref="RSCache"/> that
    ///     did the writing, which answers from its own overlay whether or not anything was committed.
    ///     </para>
    /// </remarks>
    public class NpcDefinitionWritePathTests : IClassFixture<RealCacheFixture>, IDisposable
    {
        private const int SectorSize = 520;   // RSSector.SIZE is static readonly, unusable in a const context

        /// <summary>The group the seeded definitions are placed in, so an id is its file id.</summary>
        private const int SeededGroup = 0;

        private readonly RealCacheFixture _fixture;
        private readonly string _dir;
        private readonly List<RSFileStore> _stores = new List<RSFileStore>();

        public NpcDefinitionWritePathTests(RealCacheFixture fixture)
        {
            _fixture = fixture;
            _dir = Path.Combine(Path.GetTempPath(), "fe-npc-write-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_dir);
        }

        public void Dispose()
        {
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

        /// <summary>
        ///     The captured definitions the synthetic cache is seeded with, keyed by file id.
        /// </summary>
        /// <remarks>
        ///     Taken from whichever group the reference table declares first, and from the file ids
        ///     that group declares, so the seed is a real opcode stream at a real address rather
        ///     than a hand-built one. How many there are is read off the table for the same reason a
        ///     sweep reads its population off the table: the two supported caches disagree on
        ///     several indexes and a literal would belong to one of them.
        /// </remarks>
        private SortedDictionary<int, byte[]> CapturedDefinitions(int wanted)
        {
            RSCache real = _fixture.OpenCache();
            RSReferenceTable table = real.GetReferenceTable(RSConstants.NPC_DEFINITIONS_INDEX);

            Assert.True(table.GetArchiveCount() > 0,
                "index " + RSConstants.NPC_DEFINITIONS_INDEX + " declares no groups, so there is no definition to seed from");

            int groupId = table.GetArchiveEntries().Keys.First();
            int[] fileIds = table.GetArchiveEntry(groupId).GetValidFileIds();

            Assert.True(fileIds.Length > 0, "group " + groupId + " declares no files");

            var captured = new SortedDictionary<int, byte[]>();
            foreach (int fileId in fileIds.Take(wanted))
            {
                byte[] stored = real.ReadFileBytes(RSConstants.NPC_DEFINITIONS_INDEX, groupId, fileId);
                if (stored.Length > 0)
                    captured[fileId] = stored;
            }

            Assert.NotEmpty(captured);
            return captured;
        }

        /// <summary>
        ///     Seeds a cache whose NPC index holds one group carrying the captured definitions.
        /// </summary>
        /// <remarks>
        ///     Index 19 is created empty and never used: <c>GetIndexCount</c> reports the highest
        ///     non-meta index id rather than a count, so without an index above it the NPC index is
        ///     one past the end and its reference table cannot be loaded at all. The meta index is
        ///     pre-sized to as many empty records as there are indexes below 18, because
        ///     <see cref="RSFileStore.Write"/> only ever appends contiguously.
        /// </remarks>
        private RSCache CreateCache(SortedDictionary<int, byte[]> definitions)
        {
            //Sector 0 is burned: allocation derives the next free sector from the data length, and
            //sector id 0 is the end-of-chain marker.
            File.WriteAllBytes(Path.Combine(_dir, "main_file_cache.dat2"), new byte[SectorSize]);
            File.WriteAllBytes(Path.Combine(_dir, "main_file_cache.idx" + RSConstants.NPC_DEFINITIONS_INDEX), Array.Empty<byte>());
            File.WriteAllBytes(Path.Combine(_dir, "main_file_cache.idx" + (RSConstants.NPC_DEFINITIONS_INDEX + 1)), Array.Empty<byte>());
            File.WriteAllBytes(Path.Combine(_dir, "main_file_cache.idx" + RSConstants.META_INDEX),
                               new byte[RSConstants.NPC_DEFINITIONS_INDEX * RSIndex.SIZE]);

            var store = new RSFileStore(_dir);
            _stores.Add(store);

            var archive = new RSArchive();
            foreach (KeyValuePair<int, byte[]> definition in definitions)
                archive.PutFile(definition.Key, new JagStream(definition.Value));

            store.Write(RSConstants.NPC_DEFINITIONS_INDEX, SeededGroup,
                        new RSContainer(RSConstants.NPC_DEFINITIONS_INDEX, SeededGroup,
                                        RSConstants.GZIP_COMPRESSION, archive.Encode(), 1337).Encode());
            store.Write(RSConstants.META_INDEX, RSConstants.NPC_DEFINITIONS_INDEX,
                        EncodeReferenceTable(definitions.Keys.ToArray()));

            return new RSCache(store);
        }

        private static JagStream EncodeReferenceTable(int[] fileIds)
        {
            var table = new RSReferenceTable { format = 6, version = 1, flags = 0 };

            var entry = new RSArchiveEntry(SeededGroup);
            entry.SetVersion(1);
            entry.SetValidFileIds(fileIds);
            entry.SetFileEntries(new SortedDictionary<int, RSFileEntry>(
                fileIds.ToDictionary(id => id, id => new RSFileEntry(id))));
            table.PutArchiveEntry(SeededGroup, entry);

            return new RSContainer(RSConstants.META_INDEX, RSConstants.NPC_DEFINITIONS_INDEX,
                                   RSConstants.GZIP_COMPRESSION, ReferenceTableCodec.Encode(table), 1337).Encode();
        }

        /// <summary>
        ///     Commits the cache to a fresh directory and reopens it through a new file store.
        /// </summary>
        /// <remarks>
        ///     A read through the cache that did the writing answers from its own staged overlay, so
        ///     it says the same thing whether or not anything reached the filesystem. Only a reopen
        ///     tells the two apart.
        /// </remarks>
        private RSCache SaveAndReopen(RSCache cache)
        {
            string outDir = Path.Combine(_dir, "out-" + Guid.NewGuid().ToString("N"));
            cache.WriteCache(outDir);

            var reopened = new RSFileStore(outDir);
            _stores.Add(reopened);
            return new RSCache(reopened);
        }

        /// <summary>The definition id a seeded file carries, by the split the client states.</summary>
        private static int DefinitionIdOf(int fileId)
        {
            return CacheAddressing.For(RSConstants.NPC_DEFINITIONS_INDEX).DefinitionId(SeededGroup, fileId);
        }

        /// <summary>
        ///     Edits a definition in a way that is guaranteed to change its bytes.
        /// </summary>
        /// <remarks>
        ///     Opcode 95 is emitted whenever the level is anything but -1, whether or not the file
        ///     carried it, so this changes the encoding of any definition rather than only of one
        ///     that already stored a level.
        /// </remarks>
        private static int EditLevel(NPCDefinition definition)
        {
            int edited = definition.level == 4242 ? 4243 : 4242;
            definition.level = edited;
            return edited;
        }

        // ===================================================================
        //  The commit
        // ===================================================================

        /// <summary>
        ///     The claim the whole tab rests on: an edit staged by the commit is in the cache on
        ///     disk afterwards, at the address the NPC id names.
        /// </summary>
        [RealCacheFact]
        public void Save_EditedDefinition_PersistsThroughASaveAndReopen()
        {
            SortedDictionary<int, byte[]> seeded = CapturedDefinitions(3);
            RSCache cache = CreateCache(seeded);

            int fileId = seeded.Keys.First();
            int npcId = DefinitionIdOf(fileId);

            var definition = new NPCDefinition(new JagStream(seeded[fileId]));
            definition.SetId(npcId);
            int edited = EditLevel(definition);

            Assert.True(DefinitionWriter.Save(cache, RSConstants.NPC_DEFINITIONS_INDEX, npcId,
                                              definition.Encode().ToArray()),
                        "an edited definition has to stage a write");

            RSCache reopened = SaveAndReopen(cache);

            //Read back at the address the id splits into, not at the one it was seeded at: writing
            //to the wrong slot overwrites a different NPC and reports success either way.
            CacheAddressing addressing = CacheAddressing.For(RSConstants.NPC_DEFINITIONS_INDEX);
            byte[] persisted = reopened.ReadFileBytes(RSConstants.NPC_DEFINITIONS_INDEX,
                                                      addressing.GroupOf(npcId), addressing.FileOf(npcId));

            Assert.Equal(edited, new NPCDefinition(new JagStream(persisted)).level);
        }

        /// <summary>
        ///     The other half of persistence: the definitions packed alongside the edited one share
        ///     its group, so they are re-encoded with it and have to come back unchanged.
        /// </summary>
        [RealCacheFact]
        public void Save_EditedDefinition_LeavesTheRestOfItsGroupByteIdentical()
        {
            SortedDictionary<int, byte[]> seeded = CapturedDefinitions(3);
            Assert.True(seeded.Count > 1, "this needs a neighbour in the same group to be worth running");

            RSCache cache = CreateCache(seeded);

            int fileId = seeded.Keys.First();
            int npcId = DefinitionIdOf(fileId);

            var definition = new NPCDefinition(new JagStream(seeded[fileId]));
            definition.SetId(npcId);
            EditLevel(definition);

            DefinitionWriter.Save(cache, RSConstants.NPC_DEFINITIONS_INDEX, npcId, definition.Encode().ToArray());
            RSCache reopened = SaveAndReopen(cache);

            foreach (KeyValuePair<int, byte[]> neighbour in seeded.Skip(1))
                Assert.Equal(neighbour.Value,
                             reopened.ReadFileBytes(RSConstants.NPC_DEFINITIONS_INDEX, SeededGroup, neighbour.Key));
        }

        /// <summary>
        ///     A commit that changes nothing must write nothing.
        /// </summary>
        /// <remarks>
        ///     Every cell edit ends in a commit whether or not the value moved, so this is the
        ///     common case rather than the odd one. Re-encoding rewrites the stored container and
        ///     therefore the archive CRC, which drags the reference-table entry of every definition
        ///     in the group into a save nobody asked for - and gzip is not canonical here, so the
        ///     rewritten bytes differ even when the payload does not.
        /// </remarks>
        [RealCacheFact]
        public void Save_UneditedDefinition_StagesNothingAndLeavesTheStoredBytesAlone()
        {
            SortedDictionary<int, byte[]> seeded = CapturedDefinitions(3);
            RSCache cache = CreateCache(seeded);

            int fileId = seeded.Keys.First();
            int npcId = DefinitionIdOf(fileId);

            byte[] groupBefore = cache.LoadContainer(RSConstants.NPC_DEFINITIONS_INDEX, SeededGroup).ToArray();
            byte[] tableBefore = cache.LoadContainer(RSConstants.META_INDEX, RSConstants.NPC_DEFINITIONS_INDEX).ToArray();

            var definition = new NPCDefinition(new JagStream(seeded[fileId]));
            definition.SetId(npcId);

            Assert.False(DefinitionWriter.Save(cache, RSConstants.NPC_DEFINITIONS_INDEX, npcId,
                                               definition.Encode().ToArray()),
                         "re-encoding a definition nobody edited must not stage a write");

            Assert.Equal(groupBefore, cache.LoadContainer(RSConstants.NPC_DEFINITIONS_INDEX, SeededGroup).ToArray());
            Assert.Equal(tableBefore, cache.LoadContainer(RSConstants.META_INDEX, RSConstants.NPC_DEFINITIONS_INDEX).ToArray());
        }

        /// <summary>
        ///     An edit put back the way it was is still no change. The commit compares against the
        ///     bytes the cache holds rather than against a snapshot taken when editing began, which
        ///     is the only reading that gets this case right.
        /// </summary>
        [RealCacheFact]
        public void Save_EditRevertedBeforeCommitting_StagesNothing()
        {
            SortedDictionary<int, byte[]> seeded = CapturedDefinitions(2);
            RSCache cache = CreateCache(seeded);

            int fileId = seeded.Keys.First();
            int npcId = DefinitionIdOf(fileId);

            var definition = new NPCDefinition(new JagStream(seeded[fileId]));
            definition.SetId(npcId);

            int original = definition.level;
            EditLevel(definition);
            definition.level = original;

            Assert.False(DefinitionWriter.Save(cache, RSConstants.NPC_DEFINITIONS_INDEX, npcId,
                                               definition.Encode().ToArray()),
                         "a value edited back to what it was is not a change");
        }

        /// <summary>
        ///     Writing the imported file's own bytes, as the Import NPC button does, has to land the
        ///     file verbatim: the format has more than one valid spelling of the same definition, so
        ///     an import that re-encoded would substitute this project's opcode order for the one
        ///     the file carries.
        /// </summary>
        [RealCacheFact]
        public void Save_RawBytesOfAnotherDefinition_PersistVerbatim()
        {
            SortedDictionary<int, byte[]> seeded = CapturedDefinitions(2);
            Assert.True(seeded.Count > 1, "this needs a second definition to import from");

            RSCache cache = CreateCache(seeded);

            int target = seeded.Keys.First();
            byte[] imported = seeded[seeded.Keys.Last()];
            int npcId = DefinitionIdOf(target);

            Assert.False(imported.AsSpan().SequenceEqual(seeded[target]),
                         "the two seeded definitions are identical, so an import of one over the other proves nothing");

            Assert.True(DefinitionWriter.Save(cache, RSConstants.NPC_DEFINITIONS_INDEX, npcId, imported));

            RSCache reopened = SaveAndReopen(cache);

            Assert.Equal(imported, reopened.ReadFileBytes(RSConstants.NPC_DEFINITIONS_INDEX, SeededGroup, target));
        }
    }
}
