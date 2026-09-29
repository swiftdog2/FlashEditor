#nullable enable
using System;
using System.Collections.Generic;
using System.IO;
using FlashEditor.Cache;
using FlashEditor.Definitions.Entities;
using FlashEditor.IO;
using FlashEditor.Utils;
using Xunit;

namespace FlashEditor.Tests.Definitions.Entities {
    /// <summary>
    ///     A new item copied from a template, staged into index 19 at the next id and committed.
    /// </summary>
    /// <remarks>
    ///     A synthetic cache rather than a real one, because this stages and commits writes and the
    ///     real caches are read only. Persistence is checked by reopening the committed store: a
    ///     read through the cache that staged the write returns the staged bytes either way.
    /// </remarks>
    public sealed class NewItemStagingTests : IDisposable {
        private const int SectorSize = 520;
        private const int Items = RSConstants.ITEM_DEFINITIONS_INDEX;

        /// <summary>Name "Axe", note link 97 = 7, lend link 121 = 8, members.</summary>
        private static readonly byte[] Template = {
            2, (byte) 'A', (byte) 'x', (byte) 'e', 0, 97, 0, 7, 121, 0, 8, 16, 0
        };

        /// <summary>The template without its two links.</summary>
        private static readonly byte[] Copy = { 2, (byte) 'A', (byte) 'x', (byte) 'e', 0, 16, 0 };

        /// <summary>An item with a name and nothing else.</summary>
        private static readonly byte[] Filler = { 2, (byte) 'F', 0, 0 };

        private readonly string _root;
        private readonly List<RSFileStore> _stores = new List<RSFileStore>();

        public NewItemStagingTests() {
            DebugUtil.LOG_LEVEL = DebugUtil.LOG_DETAIL.NONE;
            _root = Path.Combine(Path.GetTempPath(), "fe-newitem-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_root);
        }

        public void Dispose() {
            foreach (RSFileStore store in _stores)
                store.Dispose();
            if (Directory.Exists(_root))
                Directory.Delete(_root, true);
        }

        [Fact]
        public void Stage_WritesTheCopyAtTheNextFileOfTheLastGroup_AndItSurvivesACommit() {
            RSCache cache = Seed(lastGroup: 0, filesInLastGroup: 3);
            Assert.Equal(3, NewItemFromTemplate.NextItemId(cache));

            ItemDefinition staged = NewItemFromTemplate.Stage(cache, 1);

            Assert.Equal(3, staged.id);
            RSCache reopened = Commit(cache);
            Assert.Equal(Copy, reopened.ReadFileBytes(Items, 0, 3));
            Assert.Equal(Template, reopened.ReadFileBytes(Items, 0, 1));
            Assert.Equal(Filler, reopened.ReadFileBytes(Items, 0, 2));
            Assert.Equal(4, NewItemFromTemplate.NextItemId(reopened));
        }

        [Fact]
        public void Stage_AfterAFullGroup_CreatesTheNextGroup_AndItSurvivesACommit() {
            RSCache cache = Seed(lastGroup: 0, filesInLastGroup: 256);

            ItemDefinition staged = NewItemFromTemplate.Stage(cache, 1);

            Assert.Equal(256, staged.id);
            RSCache reopened = Commit(cache);
            Assert.Equal(Copy, reopened.ReadFileBytes(Items, 1, 0));
            Assert.Equal(new[] { 0 }, reopened.GetFileIds(Items, 1));
            Assert.Equal(257, NewItemFromTemplate.NextItemId(reopened));
        }

        [Fact]
        public void Stage_WhenTheNextIdIs20474_IsRefusedAndStagesNothing() {
            RSCache cache = Seed(lastGroup: 79, filesInLastGroup: 250);
            Assert.Equal(20474, NewItemFromTemplate.NextItemId(cache));

            var refusal = Assert.Throws<NewItemRefusedException>(() => NewItemFromTemplate.Stage(cache, 1));

            Assert.Contains("20474", refusal.Message);
            Assert.Contains("isFullHelmet", refusal.Message);
            Assert.False(cache.HasUnsavedChanges);
        }

        [Fact]
        public void Stage_OfANote_IsRefusedAndStagesNothing() {
            RSCache cache = Seed(lastGroup: 0, filesInLastGroup: 3, noteAt: 2);

            Assert.Throws<NewItemRefusedException>(() => NewItemFromTemplate.Stage(cache, 2));
            Assert.False(cache.HasUnsavedChanges);
        }

        // ===================================================================
        //  Helpers
        // ===================================================================

        private RSCache Commit(RSCache cache) {
            string outDir = Path.Combine(_root, "committed-" + Guid.NewGuid().ToString("N"));
            cache.WriteCache(outDir);
            var store = new RSFileStore(outDir);
            _stores.Add(store);
            return new RSCache(store);
        }

        /// <summary>
        ///     An index 19 whose groups 0 to <paramref name="lastGroup"/> are full except the last,
        ///     which holds <paramref name="filesInLastGroup"/> files. Item 1 is <see cref="Template"/>.
        /// </summary>
        private RSCache Seed(int lastGroup, int filesInLastGroup, int noteAt = -1) {
            string dir = Path.Combine(_root, "seed-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(dir);

            //Sector 0 is burned: allocation derives the next free sector from the data length, and
            //sector id 0 is the end-of-chain marker.
            File.WriteAllBytes(Path.Combine(dir, "main_file_cache.dat2"), new byte[SectorSize]);
            for (int i = 0; i <= Items; i++)
                File.WriteAllBytes(Path.Combine(dir, "main_file_cache.idx" + i), Array.Empty<byte>());
            File.WriteAllBytes(Path.Combine(dir, "main_file_cache.idx" + RSConstants.META_INDEX), Array.Empty<byte>());

            var store = new RSFileStore(dir);
            _stores.Add(store);

            var table = new RSReferenceTable { format = 6, version = 1, flags = 0 };
            for (int group = 0; group <= lastGroup; group++) {
                int count = group == lastGroup ? filesInLastGroup : 256;
                var archive = new RSArchive();
                var entries = new SortedDictionary<int, RSFileEntry>();
                var ids = new int[count];
                for (int file = 0; file < count; file++) {
                    int id = (group << 8) | file;
                    byte[] bytes = id == 1 ? Template
                        : id == noteAt ? new byte[] { 97, 0, 1, 98, 0, 9, 0 }
                        : Filler;
                    archive.PutFile(file, new JagStream(bytes));
                    entries[file] = new RSFileEntry(file);
                    ids[file] = file;
                }

                store.Write(Items, group,
                    new RSContainer(Items, group, RSConstants.GZIP_COMPRESSION, archive.Encode(), 1).Encode());

                var entry = new RSArchiveEntry(group);
                entry.SetVersion(1);
                entry.SetValidFileIds(ids);
                entry.SetFileEntries(entries);
                table.PutArchiveEntry(group, entry);
            }

            for (int i = 0; i < Items; i++)
                store.Write(RSConstants.META_INDEX, i, EncodeTable(i, new RSReferenceTable { format = 6, version = 1 }));
            store.Write(RSConstants.META_INDEX, Items, EncodeTable(Items, table));

            //Committed and reopened, so the seed is not itself an unsaved change.
            return Commit(new RSCache(store));
        }

        private static JagStream EncodeTable(int indexId, RSReferenceTable table) {
            return new RSContainer(RSConstants.META_INDEX, indexId,
                RSConstants.GZIP_COMPRESSION, ReferenceTableCodec.Encode(table), 1).Encode();
        }
    }
}
