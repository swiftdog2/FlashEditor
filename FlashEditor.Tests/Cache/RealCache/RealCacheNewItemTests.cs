#nullable enable
using System;
using FlashEditor.Cache;
using FlashEditor.Definitions.Entities;
using FlashEditor.IO;
using Xunit;
using Xunit.Abstractions;

namespace FlashEditor.Tests.Cache.RealCache {
    /// <summary>
    ///     A new item copied from one the cache shipped, checked against that item's stored bytes.
    /// </summary>
    /// <remarks>
    ///     Item 4718 is Dharok's greataxe, the reference weapon for a new one, and is in both caches.
    ///     The expected bytes are the stored record with two three-byte windows cut out, found by
    ///     searching the raw bytes rather than by asking this project's decoder where each opcode
    ///     starts, so the framing is not checked against itself.
    /// </remarks>
    public sealed class RealCacheNewItemTests : IClassFixture<RealCacheFixture> {
        private const int Greataxe = 4718;
        private const int GreataxeNote = 4719;
        private const int NewId = 20473;

        private readonly RealCacheFixture _cache;
        private readonly ITestOutputHelper _output;

        public RealCacheNewItemTests(RealCacheFixture cache, ITestOutputHelper output) {
            _cache = cache;
            _output = output;
        }

        [RealCacheFact]
        public void Duplicate_OfItem4718_ReEncodesToItsStoredBytesMinusExactlyItsNoteLink() {
            byte[] stored = StoredBytes(Greataxe);
            ItemDefinition template = Decode(Greataxe, stored);

            ItemDefinition copy = NewItemFromTemplate.Duplicate(template, NewId);
            byte[] encoded = copy.Encode().ToArray();

            _output.WriteLine("4718 stored: " + BitConverter.ToString(stored));
            _output.WriteLine("copy       : " + BitConverter.ToString(encoded));

            //4718 carries a note link and, in both caches, no lend link.
            Assert.False(template.Opcodes.Has(121));
            Assert.True(IsStoredMinusOneWindowEach(stored, encoded, 97),
                "The copy is not the stored record with exactly one opcode-97 three-byte window removed.");

            Assert.Equal(NewId, copy.id);
            Assert.Equal(0, copy.notedId);

            //The fields the brief names for the reference weapon, carried into the copy.
            Assert.Equal(6579, copy.inventoryModelId);
            Assert.Equal(6702, copy.maleWearModel1);
            Assert.Equal(6702, copy.femaleWearModel1);
            Assert.Equal((12, -44, 24), (copy.womanWearXOffset, copy.womanWearYOffset, copy.womanWearZOffset));
            Assert.Equal("Wield", copy.inventoryOptions[1]);
            Assert.Equal(1580, copy.itemParams[644]);

            //And the template itself is left exactly as it was read.
            Assert.Equal(stored, template.Encode().ToArray());
            Assert.Equal(GreataxeNote, template.notedId);
        }

        [RealCacheFact]
        public void Duplicate_OfTheFirstLendableItem_ReEncodesToItsStoredBytesMinusBothLinks() {
            CacheAddressing addressing = CacheAddressing.For(RSConstants.ITEM_DEFINITIONS_INDEX);
            foreach ((int group, int file) in _cache.OpenCache().EnumerateFiles(RSConstants.ITEM_DEFINITIONS_INDEX)) {
                int id = addressing.DefinitionId(group, file);
                byte[] stored = StoredBytes(id);
                ItemDefinition template = Decode(id, stored);
                if (!template.Opcodes.Has(97) || !template.Opcodes.Has(121) || template.notedTemplateId != 0 ||
                    template.lendTemplateId != 0)
                    continue;

                byte[] encoded = NewItemFromTemplate.Duplicate(template, NewId).Encode().ToArray();
                _output.WriteLine("item " + id + " stored: " + BitConverter.ToString(stored));
                Assert.True(IsStoredMinusOneWindowEach(stored, encoded, 97, 121),
                    "Item " + id + ": the copy is not the stored record with exactly one opcode-97 and one " +
                    "opcode-121 three-byte window removed.");
                return;
            }

            Assert.Fail("No item carries both a note link and a lend link.");
        }

        [RealCacheFact]
        public void Duplicate_OfANote_IsRefusedAndNamesTheItemItStandsFor() {
            ItemDefinition note = Decode(GreataxeNote, StoredBytes(GreataxeNote));
            Assert.NotEqual(0, note.notedTemplateId);

            var refusal = Assert.Throws<NewItemRefusedException>(() => NewItemFromTemplate.Duplicate(note, NewId));
            Assert.Contains(Greataxe.ToString(), refusal.Message);
        }

        [RealCacheFact]
        public void NextItemId_IsOnePastTheHighestDeclaredItem() {
            RSCache cache = _cache.OpenCache();
            int next = NewItemFromTemplate.NextItemId(cache);
            CacheAddressing addressing = CacheAddressing.For(RSConstants.ITEM_DEFINITIONS_INDEX);

            int[] lastGroupFiles = cache.GetFileIds(RSConstants.ITEM_DEFINITIONS_INDEX, addressing.GroupOf(next - 1));
            Assert.Contains(addressing.FileOf(next - 1), lastGroupFiles);
            foreach ((int group, int file) in cache.EnumerateFiles(RSConstants.ITEM_DEFINITIONS_INDEX))
                Assert.True(addressing.DefinitionId(group, file) < next);

            _cache.Profile.AssertCensus(_output, "item.nextId", next);
        }

        /// <summary>
        ///     Every item's text cells parse back to the values they show, so editing one cell of a
        ///     real item cannot quietly change another value held in the same cell.
        /// </summary>
        [RealCacheFact]
        public void EveryItem_TextCellsParseBackToTheValuesTheyShow() {
            RSCache cache = _cache.OpenCache();
            CacheAddressing addressing = CacheAddressing.For(RSConstants.ITEM_DEFINITIONS_INDEX);
            int checkedItems = 0;

            foreach ((int group, int file) in cache.EnumerateFiles(RSConstants.ITEM_DEFINITIONS_INDEX)) {
                int id = addressing.DefinitionId(group, file);
                ItemDefinition item = Decode(id, cache.ReadFileBytes(RSConstants.ITEM_DEFINITIONS_INDEX, group, file));

                Assert.Equal(item.groundOptions, ItemFieldText.ParseOptions(ItemFieldText.FormatOptions(item.groundOptions)));
                Assert.Equal(item.inventoryOptions, ItemFieldText.ParseOptions(ItemFieldText.FormatOptions(item.inventoryOptions)));

                var recolours = ItemFieldText.ParseRecolours(
                    ItemFieldText.FormatRecolours(item.originalModelColors, item.modifiedModelColors));
                Assert.Equal(item.originalModelColors, recolours?.From);
                Assert.Equal(item.modifiedModelColors, recolours?.To);

                Assert.Equal(item.itemParams, ItemFieldText.ParseParams(ItemFieldText.FormatParams(item.itemParams!)));

                Assert.Equal((item.manWearXOffset, item.manWearYOffset, item.manWearZOffset), ItemFieldText.ParseOffset(
                    ItemFieldText.FormatOffset(item.manWearXOffset, item.manWearYOffset, item.manWearZOffset)));
                Assert.Equal((item.womanWearXOffset, item.womanWearYOffset, item.womanWearZOffset), ItemFieldText.ParseOffset(
                    ItemFieldText.FormatOffset(item.womanWearXOffset, item.womanWearYOffset, item.womanWearZOffset)));
                checkedItems++;
            }

            Assert.Equal(_cache.DeclaredFiles(RSConstants.ITEM_DEFINITIONS_INDEX), checkedItems);
        }

        private byte[] StoredBytes(int itemId) {
            CacheAddressing addressing = CacheAddressing.For(RSConstants.ITEM_DEFINITIONS_INDEX);
            return _cache.OpenCache().ReadFileBytes(RSConstants.ITEM_DEFINITIONS_INDEX,
                addressing.GroupOf(itemId), addressing.FileOf(itemId));
        }

        private static ItemDefinition Decode(int itemId, byte[] stored) {
            ItemDefinition definition = ItemDefinition.DecodeFromStream(new JagStream(stored));
            definition.id = itemId;
            return definition;
        }

        /// <summary>
        ///     Whether removing one three-byte window starting with each of <paramref name="opcodes"/>
        ///     from <paramref name="stored"/> gives <paramref name="encoded"/>, searching every
        ///     position rather than trusting a decoder to say where an opcode starts.
        /// </summary>
        private static bool IsStoredMinusOneWindowEach(byte[] stored, byte[] encoded, params int[] opcodes) {
            if (opcodes.Length == 0)
                return stored.AsSpan().SequenceEqual(encoded);

            int[] rest = opcodes[1..];
            for (int i = 0; i + 3 <= stored.Length; i++) {
                if (stored[i] != opcodes[0])
                    continue;
                byte[] cut = new byte[stored.Length - 3];
                Array.Copy(stored, 0, cut, 0, i);
                Array.Copy(stored, i + 3, cut, i, stored.Length - i - 3);
                if (IsStoredMinusOneWindowEach(cut, encoded, rest))
                    return true;
            }
            return false;
        }
    }
}
