using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using FlashEditor.Cache;
using FlashEditor.Definitions.Editing;
using FlashEditor.Definitions.Entities;
using FlashEditor.Definitions.Models;
using FlashEditor.Definitions.Models.Interchange;
using FlashEditor.IO;
using FlashEditor.Tests.Cache.RealCache;
using FlashEditor.Utils;
using Xunit;
using Xunit.Abstractions;

namespace FlashEditor.Tests.Definitions.Entities {
    /// <summary>
    ///     The entity page's OBJ export and import, driven the way the buttons drive them.
    /// </summary>
    /// <remarks>
    ///     The interchange layer itself is already pinned against real cache bytes by
    ///     <c>RealCacheModelObjTests</c>, and its refusal modes by <c>ModelObjInterchangeTests</c>.
    ///     Neither covers the <b>wiring</b>, which is what this file exists for and which differs from
    ///     what those two assert in three ways that can each fail on their own.
    ///     <para>
    ///     First, the layer's tests work on <c>ObjDocument.ObjText</c> in memory; the editor writes two
    ///     files to disk and the OBJ names the other one, so the pair has to agree after a round trip
    ///     through the filesystem. Second, the editor decides <i>whether</i> to write at all, and "an
    ///     import that changed nothing must write nothing" is a property of that decision rather than
    ///     of the codec. Third, the fanning note is this file's own code.
    ///     </para>
    ///     <para>
    ///     The cache-facing halves of both handlers are <c>internal static</c> on
    ///     <see cref="EntityBrowserPanel"/> for this reason: they are callable here with no STA thread
    ///     and no message pump. What is left in the handlers - the dialogs, the buttons, their
    ///     enablement and the viewport reselect - is not covered by anything and is checked by eye.
    ///     </para>
    /// </remarks>
    public sealed class EntityModelObjWiringTests : IClassFixture<RealCacheFixture>, IDisposable {
        //RSSector.SIZE is static readonly, so it cannot be used in a const context.
        private const int SectorSize = 520;

        private readonly RealCacheFixture _cache;
        private readonly ITestOutputHelper _output;
        private readonly string _root;
        private readonly List<RSFileStore> _stores = new List<RSFileStore>();

        public EntityModelObjWiringTests(RealCacheFixture cache, ITestOutputHelper output) {
            _cache = cache;
            _output = output;

            DebugUtil.LOG_LEVEL = DebugUtil.LOG_DETAIL.NONE;
            _root = Path.Combine(Path.GetTempPath(), "fe-objwiring-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_root);
        }

        public void Dispose() {
            //Each store holds an exclusive handle on its own dat2 and has to release it before the
            //directory underneath can go.
            foreach (RSFileStore store in _stores)
                store.Dispose();

            if (Directory.Exists(_root))
                Directory.Delete(_root, true);
        }

        /// <summary>
        ///     The two files the editor writes agree with each other.
        /// </summary>
        /// <remarks>
        ///     The claim the interchange tests cannot make. They assert <see cref="ObjDocument"/>'s
        ///     fields; this asserts that what landed on disk is self-consistent - the OBJ's
        ///     <c>mtllib</c> line names a file that is actually beside it, under exactly that name.
        ///     <para>
        ///     Both the default library name and a renamed one, because the editor does not use the
        ///     default: it names the library after whatever the user called the OBJ in the save
        ///     dialog, so a renamed export is the case that actually ships.
        ///     </para>
        /// </remarks>
        [RealCacheFact]
        public void TheFileTheEditorWrites_IsTheOneTheObjPointsAt() {
            RSCache open = _cache.OpenCache();
            ModelListing listing = FirstTexturedModel(open);

            foreach (string stem in new[] { "model_" + listing.ModelId, "widget" }) {
                string directory = Path.Combine(_root, "pair-" + stem);
                Directory.CreateDirectory(directory);

                string objPath = Path.Combine(directory, stem + ".obj");
                ObjDocument document = EntityBrowserPanel.BuildModelObj(open, listing, stem + ".mtl");
                IReadOnlyList<string> written = document.Save(objPath);

                Assert.Equal(2, written.Count);
                Assert.All(written, path => Assert.True(File.Exists(path), path + " was reported written and is not there."));

                //OBJ first, then the library beside it.
                Assert.Equal(objPath, written[0]);
                Assert.Equal(directory, Path.GetDirectoryName(written[1]));

                string named = MtllibToken(File.ReadAllText(objPath));
                Assert.Equal(stem + ".mtl", named);
                Assert.Equal(named, Path.GetFileName(written[1]));
            }
        }

        /// <summary>
        ///     An export read back off the disk is still the same mesh.
        /// </summary>
        /// <remarks>
        ///     The only test of the write-then-read leg. <c>RealCacheModelObjTests</c> deliberately
        ///     goes through <c>ObjText</c> in memory, so nothing else here covers the encoding, the
        ///     line endings, or the absent byte order mark - and a BOM is the specific failure
        ///     <see cref="ObjDocument"/> writes UTF-8 without one to avoid, because several readers
        ///     otherwise take the first token as <c>﻿v</c> and drop the vertex.
        /// </remarks>
        [RealCacheFact]
        public void AnExportReadBackFromDiskIsStillUnchangedGeometry() {
            RSCache open = _cache.OpenCache();
            ModelListing listing = FirstTexturedModel(open);

            string objPath = Path.Combine(_root, "readback.obj");
            EntityBrowserPanel.BuildModelObj(open, listing, "readback.mtl").Save(objPath);

            byte[] raw = File.ReadAllBytes(objPath);
            Assert.NotEqual(0xEF, raw[0]);

            ModelFile stored = open.GetModelDefinition(listing.Address.GroupId, listing.FileId).Source!;
            ModelImportResult result = ModelObjImporter.Import(stored, ObjParser.ParseFile(objPath));

            Assert.True(result.Succeeded, result.Message);
            Assert.False(result.GeometryChanged);
        }

        /// <summary>
        ///     Re-importing an untouched export stages nothing at all.
        /// </summary>
        /// <remarks>
        ///     "A save that changes nothing must write nothing", pinned at the wiring rather than at
        ///     the codec. A re-encode would renormalise the strip opcodes and the smart widths, and
        ///     this format has more than one legal spelling of the same mesh - so the bytes would move
        ///     for a model nobody edited, and the archive CRC and every reference-table entry packed
        ///     beside it would move with them.
        ///     <para>
        ///     It also catches the overload trap for free. <c>ImportFile(model, path)</c> and
        ///     <c>Import(model, objText)</c> are both <c>(ModelFile, string)</c>; hand the path to the
        ///     second and it parses the path itself as OBJ, yielding an empty mesh whose face count
        ///     disagrees with the model - so this test fails loudly rather than silently passing.
        ///     </para>
        ///     <para>
        ///     <b>What it does not pin.</b> <c>StageModelObj</c> refuses the write twice, once on
        ///     <c>GeometryChanged</c> and once on comparing the encoded bytes, and this cannot tell
        ///     which one fired: disabling the first leaves all six tests green, because re-encoding an
        ///     untouched model is byte-identical and the second catches it. The invariant is pinned;
        ///     the division of labour behind it is not, and no assertion here can be.
        ///     </para>
        /// </remarks>
        [RealCacheFact]
        public void AnUneditedObjStagesNothing() {
            (RSCache seeded, ModelListing listing, byte[] before) = SeedOneModel();

            string objPath = Path.Combine(_root, "unedited.obj");
            EntityBrowserPanel.BuildModelObj(seeded, listing, "unedited.mtl").Save(objPath);

            bool staged = EntityBrowserPanel.StageModelObj(seeded, listing, objPath, out ModelImportResult result);

            Assert.True(result.Succeeded, result.Message);
            Assert.False(result.GeometryChanged);
            Assert.False(staged);
            Assert.Equal(before, seeded.ReadFileBytes(RSConstants.MODELS_INDEX, listing.Address.GroupId, listing.FileId));
        }

        /// <summary>
        ///     An edited OBJ stages bytes that survive a commit and decode to the edited mesh.
        /// </summary>
        /// <remarks>
        ///     Verified by reopening the store rather than by reading back through the cache that did
        ///     the write: a read through the same <see cref="RSCache"/> returns the staged bytes
        ///     whether or not anything was ever committed, so it cannot tell a staged write from a
        ///     persisted one.
        ///     <para>
        ///     The per-face arrays are checked as well as the geometry, because they are what an OBJ
        ///     cannot carry and therefore what the rebuild has to bring across from the model already
        ///     in the cache. A rebuild that dropped them would move the vertices correctly and still
        ///     be wrong.
        ///     </para>
        /// </remarks>
        [RealCacheFact]
        public void AnEditedObjStagesBytesThatDecodeToTheEditedMesh() {
            (RSCache seeded, ModelListing listing, byte[] before) = SeedOneModel();

            string objPath = Path.Combine(_root, "edited.obj");
            EntityBrowserPanel.BuildModelObj(seeded, listing, "edited.mtl").Save(objPath);

            ModelFile original = ModelCodec.Decode(before, listing.ModelId);
            File.WriteAllText(objPath, ShiftAlongX(File.ReadAllText(objPath), 1));

            bool staged = EntityBrowserPanel.StageModelObj(seeded, listing, objPath, out ModelImportResult result);

            Assert.True(result.Succeeded, result.Message);
            Assert.True(result.GeometryChanged);
            Assert.True(staged);

            string outDir = Path.Combine(_root, "committed-" + Guid.NewGuid().ToString("N"));
            seeded.WriteCache(outDir);

            var reopenedStore = new RSFileStore(outDir);
            _stores.Add(reopenedStore);
            var reopened = new RSCache(reopenedStore);

            byte[] after = reopened.ReadFileBytes(RSConstants.MODELS_INDEX, listing.Address.GroupId, listing.FileId);
            Assert.NotEqual(before, after);

            ModelFile rebuilt = ModelCodec.Decode(after, listing.ModelId);

            ModelGeometry was = ModelGeometry.FromFile(original);
            ModelGeometry now = ModelGeometry.FromFile(rebuilt);

            Assert.Equal(was.VertexCount, now.VertexCount);
            Assert.Equal(was.FaceCount, now.FaceCount);

            //OBJ (x, y, z) is model (x, -y, -z), so a shift along the OBJ's X is a shift along the
            //model's X and the other two axes are untouched rather than negated.
            for (int i = 0; i < was.VertexCount; i++) {
                Assert.Equal(was.X[i] + 1, now.X[i]);
                Assert.Equal(was.Y[i], now.Y[i]);
                Assert.Equal(was.Z[i], now.Z[i]);
            }

            Assert.Equal(was.FaceA, now.FaceA);
            Assert.Equal(was.FaceB, now.FaceB);
            Assert.Equal(was.FaceC, now.FaceC);

            //What OBJ cannot express, and therefore what the rebuild had to bring across from the
            //model already in the cache rather than from the file.
            Assert.Equal(original.FacePriorities, rebuilt.FacePriorities);
            Assert.Equal(original.FaceAlphas, rebuilt.FaceAlphas);
            Assert.Equal(original.TextureTypes, rebuilt.TextureTypes);
            Assert.Equal(original.TexturedFaceCount, rebuilt.TexturedFaceCount);
        }

        /// <summary>
        ///     A refused import blames the triangulation when triangulation is what moved the count.
        /// </summary>
        /// <remarks>
        ///     This one pins our code rather than the layer's. The importer adds its "n polygons were
        ///     fanned" note only on the paths that succeed, so a refusal arrives as two bare face
        ///     counts - and a Blender export that was never triangulated is the commonest way this
        ///     feature will be made to fail. The panel holds the parsed mesh so the refusal can say so.
        ///     <para>
        ///     The quad is built by repeating a corner rather than by naming a fourth vertex. The
        ///     parser fans any four-corner face into two triangles whatever its corners are, so this
        ///     moves the face count by exactly one without needing to know anything about the model's
        ///     topology.
        ///     </para>
        /// </remarks>
        [RealCacheFact]
        public void AnObjWithQuadsIsRefusedAndSaysTheFanningMovedTheFaceCount() {
            (RSCache seeded, ModelListing listing, _) = SeedOneModel();

            string objPath = Path.Combine(_root, "quads.obj");
            EntityBrowserPanel.BuildModelObj(seeded, listing, "quads.mtl").Save(objPath);
            File.WriteAllText(objPath, WithOneQuad(File.ReadAllText(objPath)));

            bool staged = EntityBrowserPanel.StageModelObj(seeded, listing, objPath, out ModelImportResult result);

            Assert.False(staged);
            Assert.False(result.Succeeded);
            Assert.Null(result.Model);
            Assert.Contains("faces", result.Message);
            Assert.Contains("fanned", result.Message);
            Assert.Contains(result.Entries, entry =>
                entry.Disposition == ModelImportDisposition.Refused && entry.Detail.Contains("fanned"));
        }

        /// <summary>
        ///     A file that is not there costs the import and nothing else.
        /// </summary>
        /// <remarks>
        ///     It throws rather than reporting a refusal, which is the distinction the handler's catch
        ///     depends on: a file that will not open is a bad file, not a mesh that disagrees with the
        ///     model. A version that swallowed this would return false and be reported to the user as
        ///     "no change", which is the wrong thing to say about a failure.
        /// </remarks>
        [RealCacheFact]
        public void AMissingFileCostsTheImportAndNothingElse() {
            (RSCache seeded, ModelListing listing, byte[] before) = SeedOneModel();

            string missing = Path.Combine(_root, "not-here-" + Guid.NewGuid().ToString("N") + ".obj");

            Assert.ThrowsAny<Exception>(() =>
                EntityBrowserPanel.StageModelObj(seeded, listing, missing, out _));

            Assert.Equal(before, seeded.ReadFileBytes(RSConstants.MODELS_INDEX, listing.Address.GroupId, listing.FileId));
        }

        // ===================================================================
        //  Helpers
        // ===================================================================

        /// <summary>
        ///     The first sampled model that decodes and carries materials.
        /// </summary>
        /// <remarks>
        ///     Textured rather than merely decodable, because a model with no faces has no materials,
        ///     and <see cref="ObjDocument.Save"/> then writes one file rather than two - which would
        ///     make the pair assertions vacuously pass.
        /// </remarks>
        private ModelListing FirstTexturedModel(RSCache open) {
            RSReferenceTable table = _cache.Table(RSConstants.MODELS_INDEX);

            foreach (int groupId in _cache.ArchivesToExamine(table)) {
                int[] fileIds = table.GetArchiveEntry(groupId)?.GetValidFileIds() ?? Array.Empty<int>();
                if (fileIds.Length == 0)
                    continue;

                var listing = new ModelListing(new DefinitionAddress(groupId, fileIds[0]));

                try {
                    if (EntityBrowserPanel.BuildModelObj(open, listing, "probe.mtl").MaterialFileName != null) {
                        _output.WriteLine("Model " + listing.ModelId + " (group " + groupId + ", file " + fileIds[0] + ")");
                        return listing;
                    }
                }
                catch (Exception failure) {
                    _output.WriteLine("model " + groupId + " skipped: " + failure.Message);
                }
            }

            throw new InvalidOperationException("No sampled model in index 7 decoded to an OBJ with materials.");
        }

        /// <summary>
        ///     A synthetic single-index cache holding one real model, at its real group id.
        /// </summary>
        /// <remarks>
        ///     At its <b>real</b> group id deliberately. <c>ModelCodec</c> selects the new-protocol
        ///     layout from the model id, so a model reseeded at group 0 would decode under a format it
        ///     was not written in. Keeping the id means the classification comes from the same place
        ///     it comes from in the shipped cache.
        ///     <para>
        ///     Synthetic rather than the fixture's cache because these tests stage writes, and the
        ///     real cache is read only. <c>WriteFile</c> only stages into an in-memory overlay so it
        ///     would not reach the disk either way, but the fixture is shared across the class and the
        ///     rule is that nothing writes to it at all.
        ///     </para>
        /// </remarks>
        /// <returns>The cache, the row addressing the model in it, and the bytes it was seeded with.</returns>
        private (RSCache Cache, ModelListing Listing, byte[] Stored) SeedOneModel() {
            RSCache open = _cache.OpenCache();
            ModelListing source = FirstTexturedModel(open);

            int groupId = source.Address.GroupId;
            int fileId = source.FileId;
            byte[] stored = open.ReadFileBytes(RSConstants.MODELS_INDEX, groupId, fileId);

            string dir = Path.Combine(_root, "seed-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(dir);

            //Sector 0 is burned: allocation derives the next free sector from the data length, and
            //sector id 0 is the end-of-chain marker.
            File.WriteAllBytes(Path.Combine(dir, "main_file_cache.dat2"), new byte[SectorSize]);
            for (int i = 0; i <= RSConstants.MODELS_INDEX; i++)
                File.WriteAllBytes(Path.Combine(dir, "main_file_cache.idx" + i), Array.Empty<byte>());
            File.WriteAllBytes(Path.Combine(dir, "main_file_cache.idx" + RSConstants.META_INDEX), Array.Empty<byte>());

            var store = new RSFileStore(dir);
            _stores.Add(store);

            //A single-file group is the payload with no archive framing at all - no size table and no
            //chunk count - so the model bytes go in as the container payload directly.
            store.Write(RSConstants.MODELS_INDEX, groupId,
                new RSContainer(RSConstants.MODELS_INDEX, groupId, RSConstants.GZIP_COMPRESSION,
                    new JagStream(stored), 1).Encode());

            /* The meta index is written archive by archive and refuses a gap, so every index below 7
               gets a table declaring nothing before 7 gets a real one. They are the shape index 36
               already has in the shipped cache - a stub declaring zero groups - rather than an
               invention. */
            for (int i = 0; i < RSConstants.MODELS_INDEX; i++)
                store.Write(RSConstants.META_INDEX, i, EncodeEmptyTable(i));

            store.Write(RSConstants.META_INDEX, RSConstants.MODELS_INDEX, EncodeModelTable(groupId, fileId));

            var seeded = new RSCache(store);
            return (seeded, new ModelListing(new DefinitionAddress(groupId, fileId)), stored);
        }

        /// <summary>A reference table declaring no groups at all, to fill an index the test ignores.</summary>
        private static JagStream EncodeEmptyTable(int indexId) {
            var table = new RSReferenceTable { format = 6, version = 1, flags = 0 };

            return new RSContainer(RSConstants.META_INDEX, indexId,
                RSConstants.GZIP_COMPRESSION, ReferenceTableCodec.Encode(table), 1).Encode();
        }

        /// <summary>A reference table for index 7 declaring one group holding one file.</summary>
        private static JagStream EncodeModelTable(int groupId, int fileId) {
            var table = new RSReferenceTable { format = 6, version = 1, flags = 0 };

            var entry = new RSArchiveEntry(0);
            entry.SetVersion(1);
            entry.SetValidFileIds(new[] { fileId });
            entry.SetFileEntries(new SortedDictionary<int, RSFileEntry> { [fileId] = new RSFileEntry(fileId) });
            table.PutArchiveEntry(groupId, entry);

            return new RSContainer(RSConstants.META_INDEX, RSConstants.MODELS_INDEX,
                RSConstants.GZIP_COMPRESSION, ReferenceTableCodec.Encode(table), 1).Encode();
        }

        /// <summary>The file name the OBJ's <c>mtllib</c> line names.</summary>
        private static string MtllibToken(string objText) {
            foreach (string line in objText.Split('\n')) {
                string trimmed = line.Trim();
                if (trimmed.StartsWith("mtllib ", StringComparison.Ordinal))
                    return trimmed.Substring("mtllib ".Length).Trim();
            }

            throw new InvalidOperationException("The OBJ carries no mtllib line.");
        }

        /// <summary>Moves every vertex along X, leaving every other line exactly as it was.</summary>
        /// <remarks>
        ///     The exporter writes stored coordinates as whole numbers, so this stays integral and the
        ///     importer's rounding never enters into it.
        /// </remarks>
        private static string ShiftAlongX(string objText, int by) {
            string[] lines = objText.Split('\n');

            for (int i = 0; i < lines.Length; i++) {
                string line = lines[i].TrimEnd('\r');
                if (!line.StartsWith("v ", StringComparison.Ordinal))
                    continue;

                string[] parts = line.Split(' ', StringSplitOptions.RemoveEmptyEntries);
                Assert.Equal(4, parts.Length);
                lines[i] = "v " + (int.Parse(parts[1]) + by) + " " + parts[2] + " " + parts[3];
            }

            return string.Join("\n", lines);
        }

        /// <summary>Turns the first face into a four-corner one by repeating its first corner.</summary>
        private static string WithOneQuad(string objText) {
            string[] lines = objText.Split('\n');

            for (int i = 0; i < lines.Length; i++) {
                string line = lines[i].TrimEnd('\r');
                if (!line.StartsWith("f ", StringComparison.Ordinal))
                    continue;

                string[] parts = line.Split(' ', StringSplitOptions.RemoveEmptyEntries);
                lines[i] = line + " " + parts[1];
                return string.Join("\n", lines);
            }

            throw new InvalidOperationException("The OBJ carries no faces to widen.");
        }
    }
}
