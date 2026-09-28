using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using FlashEditor.Cache;
using FlashEditor.Definitions.Entities;
using FlashEditor.IO;
using FlashEditor.Tests.Cache.RealCache;
using FlashEditor.Utils;
using Xunit;
using Xunit.Abstractions;

namespace FlashEditor.Tests.Definitions.Entities {
    /// <summary>
    ///     The "Export selected (.dat)" button's OBJ half, for rows that are not models.
    /// </summary>
    /// <remarks>
    ///     An item, NPC or object row carries model ids rather than a mesh, so the export has to
    ///     find and write the models it points at. The worked item is checked by name, because an
    ///     item id that is right in one build names a different item in another.
    /// </remarks>
    public sealed class EntityExportObjTests : IClassFixture<RealCacheFixture>, IDisposable {
        private const int SaradominSword = 11730;

        private readonly RealCacheFixture _cache;
        private readonly ITestOutputHelper _output;
        private readonly string _root;

        public EntityExportObjTests(RealCacheFixture cache, ITestOutputHelper output) {
            _cache = cache;
            _output = output;

            DebugUtil.LOG_LEVEL = DebugUtil.LOG_DETAIL.NONE;
            _root = Path.Combine(Path.GetTempPath(), "fe-entityobj-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_root);
        }

        public void Dispose() {
            if (Directory.Exists(_root))
                Directory.Delete(_root, true);
        }

        [Fact]
        public void AnItemYieldsEveryModelItSetsAndSkipsTheAbsentOnes() {
            var item = new ItemDefinition {
                inventoryModelId = 2754,
                maleWearModel1 = 2753,
                femaleWearModel1 = 2753,
                maleWearModel2 = 0xFFFF
            };

            var models = EntityBrowserPanel.ReferencedModels(item).ToList();

            Assert.Equal(new[] { "_inventory_model2754", "_male1_model2753", "_female1_model2753" },
                models.Select(m => m.Suffix));
            Assert.All(models, m => Assert.Equal(0, m.FileId));
        }

        [RealCacheFact]
        public void SaradominSwordExportsItsInventoryAndWornModelsAsObj() {
            RSCache open = _cache.OpenCache();

            byte[] stored = open.ReadFileBytes(RSConstants.ITEM_DEFINITIONS_INDEX, SaradominSword >> 8, SaradominSword & 0xFF);
            var item = new ItemDefinition();
            item.Decode(new JagStream(stored));
            Assert.Equal("Saradomin sword", item.name);

            var models = EntityBrowserPanel.ReferencedModels(item).ToList();
            Assert.Contains(models, m => m.Suffix.StartsWith("_inventory_", StringComparison.Ordinal));
            Assert.Contains(models, m => m.Suffix.StartsWith("_male1_", StringComparison.Ordinal));

            foreach ((string suffix, int modelId, int fileId) in models) {
                string name = SaradominSword + suffix;
                Assert.True(EntityBrowserPanel.WriteModelObj(open, modelId, fileId, _root, name),
                    "model " + modelId + " did not export");

                string obj = Path.Combine(_root, name + ".obj");
                Assert.True(File.Exists(obj), obj);
                Assert.Contains("mtllib " + name + ".mtl", File.ReadAllText(obj));
                Assert.True(File.Exists(Path.Combine(_root, name + ".mtl")));
                _output.WriteLine(name + ".obj");
            }
        }
    }
}
