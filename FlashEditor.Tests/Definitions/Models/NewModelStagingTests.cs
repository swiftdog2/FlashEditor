#nullable enable
using System;
using System.Collections.Generic;
using System.IO;
using FlashEditor.Cache;
using FlashEditor.Definitions.Models;
using FlashEditor.Definitions.Models.Interchange;
using FlashEditor.IO;
using FlashEditor.Utils;
using Xunit;

namespace FlashEditor.Tests.Definitions.Models {
    /// <summary>
    ///     A new model from an OBJ on disk, staged into index 7 at the next id and committed.
    /// </summary>
    /// <remarks>
    ///     A synthetic cache rather than a real one, because this stages and commits writes and the
    ///     real caches are read only. Persistence is checked by reopening the committed store: a
    ///     read through the cache that staged the write returns the staged bytes either way.
    /// </remarks>
    public sealed class NewModelStagingTests : IDisposable {
        private const int SectorSize = 520;
        private const int Models = RSConstants.MODELS_INDEX;

        private const string Obj =
            "mtllib axe.mtl\nv 0 0 0\nv 10 0 0\nv 0 10 0\nv 0 0 10\nusemtl gold\nf 1 2 3\nusemtl navy\nf 1 3 4\n";

        private const string Mtl = "newmtl gold\nKd 0.86 0.68 0.31\nnewmtl navy\nKd 0.09 0.13 0.24\n";

        private readonly string _root;
        private readonly List<RSFileStore> _stores = new List<RSFileStore>();

        public NewModelStagingTests() {
            DebugUtil.LOG_LEVEL = DebugUtil.LOG_DETAIL.NONE;
            _root = Path.Combine(Path.GetTempPath(), "fe-newmodel-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_root);
        }

        public void Dispose() {
            foreach (RSFileStore store in _stores)
                store.Dispose();
            if (Directory.Exists(_root))
                Directory.Delete(_root, true);
        }

        [Fact]
        public void StageFile_CreatesEachModelAtTheNextId_AndTheySurviveACommit() {
            (RSCache cache, byte[] existing) = SeedOneModel();
            string objPath = WritePair("axe");

            ModelImportResult first = NewModelFromObj.StageFile(cache, objPath, NewModelFromObj.WeaponVertexLabel);
            ModelImportResult second = NewModelFromObj.StageFile(cache, objPath, null);

            Assert.True(first.Succeeded, first.Message);
            Assert.True(second.Succeeded, second.Message);
            Assert.Equal(1, first.Model!.ModelId);
            Assert.Equal(2, second.Model!.ModelId);
            Assert.Contains("model 1", first.Message, StringComparison.OrdinalIgnoreCase);

            RSCache reopened = Commit(cache);
            Assert.Equal(existing, reopened.ReadFileBytes(Models, 0, 0));

            ModelFile created = ModelCodec.Decode(reopened.ReadFileBytes(Models, 1, 0), 1);
            Assert.Equal(4, created.VertexCount);
            Assert.Equal(2, created.FaceCount);
            Assert.Equal(ModelColour.FromRgb((219 << 16) | (173 << 8) | 79), created.FaceColours[0]);
            Assert.Equal(ModelColour.FromRgb((23 << 16) | (33 << 8) | 61), created.FaceColours[1]);
            Assert.All(created.VertexSkins!, label => Assert.Equal(50, label.Value));

            Assert.Null(ModelCodec.Decode(reopened.ReadFileBytes(Models, 2, 0), 2).VertexSkins);
        }

        [Fact]
        public void StageFile_Refused_StagesNothing() {
            (RSCache cache, _) = SeedOneModel();
            string objPath = WritePair("axe");
            File.WriteAllText(Path.Combine(_root, "axe.mtl"), "newmtl gold\nKd 1 1 1\n");

            ModelImportResult result = NewModelFromObj.StageFile(cache, objPath, null);

            Assert.False(result.Succeeded);
            Assert.Contains("\"navy\"", result.Message);
            Assert.Equal(1, cache.NextCreatableGroupId(Models));
        }

        [Fact]
        public void StageFile_OfAnObjWhoseLibraryIsMissing_Throws() {
            (RSCache cache, _) = SeedOneModel();
            string objPath = WritePair("axe");
            File.Delete(Path.Combine(_root, "axe.mtl"));

            Assert.ThrowsAny<IOException>(() => NewModelFromObj.StageFile(cache, objPath, null));
            Assert.Equal(1, cache.NextCreatableGroupId(Models));
        }

        [Fact]
        public void ReplaceFile_RebuildsTheModelInPlace_AtItsOwnIdAndFile_AndItSurvivesACommit() {
            (RSCache cache, _) = SeedOneModel();
            string objPath = WritePair("axe");

            ModelImportResult result = NewModelFromObj.ReplaceFile(cache, 0, objPath, NewModelFromObj.WeaponVertexLabel);

            Assert.True(result.Succeeded, result.Message);
            Assert.True(result.GeometryChanged);
            Assert.Equal(0, result.Model!.ModelId);
            Assert.Equal(1, cache.NextCreatableGroupId(Models));

            RSCache reopened = Commit(cache);
            RSReferenceTable table = reopened.GetReferenceTable(Models);
            Assert.Equal(new[] { 0 }, table.validArchiveIds);
            Assert.Equal(new[] { 0 }, table.GetArchiveEntry(0).GetValidFileIds());
            Assert.Equal(1, reopened.NextCreatableGroupId(Models));

            ModelFile replaced = ModelCodec.Decode(reopened.ReadFileBytes(Models, 0, 0), 0);
            Assert.Equal(4, replaced.VertexCount);
            Assert.Equal(2, replaced.FaceCount);
            Assert.Equal(ModelColour.FromRgb((219 << 16) | (173 << 8) | 79), replaced.FaceColours[0]);
            Assert.Equal(ModelColour.FromRgb((23 << 16) | (33 << 8) | 61), replaced.FaceColours[1]);
            Assert.All(replaced.VertexSkins!, label => Assert.Equal(50, label.Value));
        }

        // ===================================================================
        //  Helpers
        // ===================================================================

        private string WritePair(string stem) {
            string objPath = Path.Combine(_root, stem + ".obj");
            File.WriteAllText(objPath, Obj);
            File.WriteAllText(Path.Combine(_root, stem + ".mtl"), Mtl);
            return objPath;
        }

        private RSCache Commit(RSCache cache) {
            string outDir = Path.Combine(_root, "committed-" + Guid.NewGuid().ToString("N"));
            cache.WriteCache(outDir);
            var store = new RSFileStore(outDir);
            _stores.Add(store);
            return new RSCache(store);
        }

        /// <summary>An index 7 holding one model at group 0, so the next creatable id is 1.</summary>
        private (RSCache Cache, byte[] Stored) SeedOneModel() {
            byte[] stored = NewModelFromObj.Build(ObjParser.Parse("v 0 0 0\nv 1 0 0\nv 0 1 0\nusemtl a\nf 1 2 3\n"),
                new Dictionary<string, int> { ["a"] = 0x808080 }, 0, null).Model!.Encode().ToArray();

            string dir = Path.Combine(_root, "seed-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(dir);

            //Sector 0 is burned: allocation derives the next free sector from the data length, and
            //sector id 0 is the end-of-chain marker.
            File.WriteAllBytes(Path.Combine(dir, "main_file_cache.dat2"), new byte[SectorSize]);
            for (int i = 0; i <= Models; i++)
                File.WriteAllBytes(Path.Combine(dir, "main_file_cache.idx" + i), Array.Empty<byte>());
            File.WriteAllBytes(Path.Combine(dir, "main_file_cache.idx" + RSConstants.META_INDEX), Array.Empty<byte>());

            var store = new RSFileStore(dir);
            _stores.Add(store);

            store.Write(Models, 0, new RSContainer(Models, 0, RSConstants.GZIP_COMPRESSION, new JagStream(stored), 1).Encode());
            for (int i = 0; i < Models; i++)
                store.Write(RSConstants.META_INDEX, i, EncodeTable(i, null));
            store.Write(RSConstants.META_INDEX, Models, EncodeTable(Models, 0));

            return (new RSCache(store), stored);
        }

        /// <summary>A table declaring nothing, or one group holding file 0.</summary>
        private static JagStream EncodeTable(int indexId, int? groupId) {
            var table = new RSReferenceTable { format = 6, version = 1, flags = 0 };
            if (groupId.HasValue) {
                var entry = new RSArchiveEntry(0);
                entry.SetVersion(1);
                entry.SetValidFileIds(new[] { 0 });
                entry.SetFileEntries(new SortedDictionary<int, RSFileEntry> { [0] = new RSFileEntry(0) });
                table.PutArchiveEntry(groupId.Value, entry);
            }

            return new RSContainer(RSConstants.META_INDEX, indexId,
                RSConstants.GZIP_COMPRESSION, ReferenceTableCodec.Encode(table), 1).Encode();
        }
    }
}
