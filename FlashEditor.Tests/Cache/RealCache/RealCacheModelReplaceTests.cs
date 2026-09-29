#nullable enable
using System;
using System.IO;
using System.Linq;
using FlashEditor.Cache;
using FlashEditor.Cache.Util.Crypto;
using FlashEditor.Definitions.Models;
using FlashEditor.Definitions.Models.Interchange;
using FlashEditor.Utils;
using Xunit;
using Xunit.Abstractions;

namespace FlashEditor.Tests.Cache.RealCache {
    /// <summary>
    ///     A shipped model rebuilt in place from an OBJ of a different mesh, against a copy of the
    ///     cache.
    /// </summary>
    /// <remarks>
    ///     Dharok's greataxe as worn (6702) is overwritten with half the faces of the inventory greataxe
    ///     (6579) as exported, so the face count moves and the geometry-only importer could not have
    ///     done it.
    ///     Persistence is checked by reopening the committed store, because a read through the cache
    ///     that staged the write returns the staged bytes either way.
    /// </remarks>
    [Collection("RealCache")]
    public sealed class RealCacheModelReplaceTests : IDisposable {
        private const int Models = RSConstants.MODELS_INDEX;
        private const int Worn = 6702;
        private const int Inventory = 6579;

        private readonly ITestOutputHelper _output;
        private readonly string? _workingCopy;

        /// <summary>Takes a private copy of the cache to write into.</summary>
        /// <param name="output">The test output sink.</param>
        public RealCacheModelReplaceTests(ITestOutputHelper output) {
            _output = output;
            DebugUtil.LOG_LEVEL = DebugUtil.LOG_DETAIL.NONE;

            string? source = RealCacheLocator.Directory;
            if (source == null)
                return;

            _workingCopy = Path.Combine(Path.GetTempPath(), "flasheditor-model-replace-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_workingCopy);
            foreach (string file in Directory.GetFiles(source, "main_file_cache.*"))
                File.Copy(file, Path.Combine(_workingCopy, Path.GetFileName(file)));

            //A shared key directory would let a run against one cache supply keys to the other.
            string? keys = XTEAKeyTable.FindKeyFile(source);
            if (keys != null)
                File.Copy(keys, Path.Combine(_workingCopy, Path.GetFileName(keys)), true);
        }

        [RealCacheFact]
        public void ReplaceFile_OfARealModel_WithADifferentFaceCount_PersistsTheBuiltModel_AndARepeatStagesNothing() {
            if (_workingCopy == null)
                return;

            string objPath = Path.Combine(_workingCopy, "replacement.obj");
            ModelFile built;
            byte[] untouched;
            int[] fileIds;
            int declared;

            using (var store = new RSFileStore(_workingCopy)) {
                var cache = new RSCache(store);
                untouched = cache.ReadFileBytes(Models, Inventory, 0);
                ModelFile inventory = ModelCodec.Decode(untouched, Inventory);
                ModelFile worn = ModelCodec.Decode(cache.ReadFileBytes(Models, Worn, 0), Worn);
                ModelObjExporter.Export(inventory).Save(objPath);

                //The two greataxes share a face count, so the replacement keeps every other face.
                int face = 0;
                File.WriteAllLines(objPath, File.ReadAllLines(objPath)
                    .Where(line => !line.StartsWith("f ", StringComparison.Ordinal) || face++ % 2 == 0));

                fileIds = cache.GetReferenceTable(Models).GetArchiveEntry(Worn).GetValidFileIds();
                declared = cache.GetReferenceTable(Models).GetArchiveCount();
                int next = cache.NextCreatableGroupId(Models);

                ModelImportResult result = NewModelFromObj.ReplaceFile(cache, Worn, objPath, NewModelFromObj.WeaponVertexLabel);
                Assert.True(result.Succeeded, result.Message);
                Assert.True(result.GeometryChanged);
                Assert.Equal(next, cache.NextCreatableGroupId(Models));
                built = result.Model!;
                Assert.NotEqual(worn.FaceCount, built.FaceCount);
                _output.WriteLine($"model {Worn}: {worn.FaceCount} faces -> {built.FaceCount}; {result.Message}");

                cache.WriteCache(_workingCopy);
            }

            using (var store = new RSFileStore(_workingCopy)) {
                var reopened = new RSCache(store);
                RSReferenceTable table = reopened.GetReferenceTable(Models);
                Assert.Equal(declared, table.GetArchiveCount());
                Assert.Equal(fileIds, table.GetArchiveEntry(Worn).GetValidFileIds());
                Assert.Equal(untouched, reopened.ReadFileBytes(Models, Inventory, 0));

                byte[] stored = reopened.ReadFileBytes(Models, Worn, fileIds[0]);
                ModelFile persisted = ModelCodec.Decode(stored, Worn);
                Assert.Equal(built.FaceCount, persisted.FaceCount);
                Assert.True(ModelGeometry.FromFile(built).Matches(ModelGeometry.FromFile(persisted)));
                Assert.Equal(built.FaceColours, persisted.FaceColours);
                Assert.All(persisted.VertexSkins!, label => Assert.Equal(NewModelFromObj.WeaponVertexLabel, label.Value));

                RSArchiveEntry before = table.GetArchiveEntry(Worn);
                (int crc, int version) = (before.GetCrc(), before.GetVersion());

                ModelImportResult repeat = NewModelFromObj.ReplaceFile(reopened, Worn, objPath, NewModelFromObj.WeaponVertexLabel);

                Assert.True(repeat.Succeeded, repeat.Message);
                Assert.False(repeat.GeometryChanged);
                Assert.False(reopened.HasUnsavedChanges);
                Assert.Equal(stored, reopened.ReadFileBytes(Models, Worn, fileIds[0]));
                RSArchiveEntry after = reopened.GetReferenceTable(Models).GetArchiveEntry(Worn);
                Assert.Equal((crc, version), (after.GetCrc(), after.GetVersion()));
            }
        }

        /// <summary>Removes the working copy.</summary>
        public void Dispose() {
            if (_workingCopy == null || !Directory.Exists(_workingCopy))
                return;

            try {
                Directory.Delete(_workingCopy, true);
            }
            catch (IOException) {
                //A leftover temp copy is untidy, not a failure.
            }
        }
    }
}
