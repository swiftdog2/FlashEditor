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
    ///     Setting an existing item's inventory view and params by name, as CacheStage's item mode does.
    /// </summary>
    /// <remarks>
    ///     A synthetic cache, because a real one is read only and these commit. Expected bytes are
    ///     written out by hand from the opcode table (<c>reference/hydra-637-definitions/item-opcodes.md</c>),
    ///     not produced by the encoder under test.
    /// </remarks>
    public sealed class ItemViewEditTests : IDisposable {
        private const int SectorSize = 520;
        private const int Items = RSConstants.ITEM_DEFINITIONS_INDEX;

        /// <summary>
        ///     Model 6579, zoom 1710, xan2d 280, yan2d 2004, yoff2d 51, params {644=1580}: the view
        ///     opcodes in the order item 4718 stores them.
        /// </summary>
        private static readonly byte[] Template = {
            1, 0x19, 0xB3,
            8, 0x00, 0x33,
            4, 0x06, 0xAE,
            6, 0x07, 0xD4,
            5, 0x01, 0x18,
            249, 1, 0, 0x00, 0x02, 0x84, 0x00, 0x00, 0x06, 0x2C,
            0
        };

        /// <summary>An item with a name and nothing else.</summary>
        private static readonly byte[] Filler = { 2, (byte) 'F', 0, 0 };

        private readonly string _root;
        private readonly List<RSFileStore> _stores = new List<RSFileStore>();

        public ItemViewEditTests() {
            DebugUtil.LOG_LEVEL = DebugUtil.LOG_DETAIL.NONE;
            _root = Path.Combine(Path.GetTempPath(), "fe-itemview-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_root);
        }

        public void Dispose() {
            foreach (RSFileStore store in _stores)
                store.Dispose();
            if (Directory.Exists(_root))
                Directory.Delete(_root, true);
        }

        [Fact]
        public void Apply_SetsViewFieldsInPlaceAndAppendsANewParam_AndItSurvivesACommit() {
            RSCache cache = Seed();

            ItemEditOutcome outcome = ItemViewEdit.Apply(cache, 1,
                new[] { "zoom=1850", "xan2d=440", "zan2d=12", "param.687=1" });

            Assert.True(outcome.Staged);
            Assert.Equal("zoom 1710 xan2d 280 yan2d 2004 zan2d 0 xoff2d 0 yoff2d 51 params 644=1580", outcome.Before);
            Assert.Equal("zoom 1850 xan2d 440 yan2d 2004 zan2d 12 xoff2d 0 yoff2d 51 params 644=1580, 687=1", outcome.After);
            byte[] expected = {
                1, 0x19, 0xB3,
                8, 0x00, 0x33,
                4, 0x07, 0x3A,
                6, 0x07, 0xD4,
                5, 0x01, 0xB8,
                249, 2, 0, 0x00, 0x02, 0x84, 0x00, 0x00, 0x06, 0x2C, 0, 0x00, 0x02, 0xAF, 0x00, 0x00, 0x00, 0x01,
                95, 0x00, 0x0C,
                0
            };
            Assert.Equal(expected, Commit(cache).ReadFileBytes(Items, 0, 1));
        }

        [Fact]
        public void Apply_OfTheValuesTheItemAlreadyHolds_StagesNothing() {
            RSCache cache = Seed();

            ItemEditOutcome outcome = ItemViewEdit.Apply(cache, 1,
                new[] { "zoom=1710", "yoff2d=51", "zan2d=0", "param.644=1580" });

            Assert.False(outcome.Staged);
            Assert.Equal(outcome.Before, outcome.After);
            Assert.False(cache.HasUnsavedChanges);
        }

        [Theory]
        [InlineData("zoom", "zoom")]
        [InlineData("scale=5", "scale")]
        [InlineData("zoom=65536", "65535")]
        [InlineData("xoff2d=-32769", "-32768")]
        [InlineData("xan2d=abc", "xan2d")]
        [InlineData("param.x=1", "param key")]
        [InlineData("param.687=one", "687")]
        public void Apply_OfAnAssignmentTheItemCannotStore_IsRefusedAndStagesNothing(string bad, string named) {
            RSCache cache = Seed();

            var refusal = Assert.Throws<FormatException>(() =>
                ItemViewEdit.Apply(cache, 1, new[] { "zoom=1850", bad }));

            Assert.Contains(named, refusal.Message);
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

        /// <summary>An index 19 of one group of three items, item 1 being <see cref="Template"/>.</summary>
        private RSCache Seed() {
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
            var archive = new RSArchive();
            var entries = new SortedDictionary<int, RSFileEntry>();
            var ids = new[] { 0, 1, 2 };
            foreach (int file in ids) {
                archive.PutFile(file, new JagStream(file == 1 ? Template : Filler));
                entries[file] = new RSFileEntry(file);
            }
            store.Write(Items, 0, new RSContainer(Items, 0, RSConstants.GZIP_COMPRESSION, archive.Encode(), 1).Encode());

            var entry = new RSArchiveEntry(0);
            entry.SetVersion(1);
            entry.SetValidFileIds(ids);
            entry.SetFileEntries(entries);
            table.PutArchiveEntry(0, entry);

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
