using FlashEditor.Cache;
using FlashEditor.Definitions;
using FlashEditor.Definitions.Models.Interchange;
using FlashEditor.Tests.Cache.RealCache;
using System;
using System.Collections.Generic;
using System.Linq;
using Xunit;
using Xunit.Abstractions;
using FlashEditor.IO;
using FlashEditor.Definitions.Models;

namespace FlashEditor.Tests.Cache.RealCache
{
    /// <summary>
    ///     Exports real models to OBJ, reads them straight back, and requires the bytes they came
    ///     from.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///     This is the strongest check available for an interchange path, and it needs no renderer:
    ///     an unedited round trip through a text format either reproduces the file or it does not.
    ///     A partial loss shows up immediately, because the import has to reconstruct the same
    ///     vertices and the same faces from the OBJ before it will agree that nothing changed, and
    ///     anything it gets wrong sends it down the rebuild path where the bytes move.
    ///     </para>
    ///     <para>
    ///     Three passes over the same decode, because reading index 7 is the expensive half by a
    ///     wide margin and each pass answers a question the others cannot. The round trip proves an
    ///     export is readable and complete. The rebuild proves the delta and strip-opcode encoder
    ///     produces something the production decoder reads back, which the round trip never
    ///     exercises - it keeps the original bytes precisely so that it does not. The edit proves an
    ///     actual change lands, and that every per-face array survives it untouched.
    ///     </para>
    ///     <para>
    ///     Sampled through <see cref="RealCacheFixture.ArchivesToExamine"/>, so the population comes
    ///     from the reference table rather than from a number written here, and
    ///     <c>FLASHEDITOR_TEST_CACHE_FULL</c> widens it to every model the table declares.
    ///     </para>
    /// </remarks>
    public class RealCacheModelObjTests : IClassFixture<RealCacheFixture>
    {
        /// <summary>Failures listed before the report is truncated.</summary>
        private const int MaxReportedFailures = 10;

        private readonly RealCacheFixture _cache;
        private readonly ITestOutputHelper _output;

        /// <summary>Binds the shared open cache and the per-test output sink.</summary>
        /// <param name="cache">The shared open cache.</param>
        /// <param name="output">Where the coverage lines go.</param>
        public RealCacheModelObjTests(RealCacheFixture cache, ITestOutputHelper output)
        {
            _cache = cache;
            _output = output;
        }

        /// <summary>
        ///     Every sampled model exports, re-imports unchanged, rebuilds through the codec, and
        ///     survives an edit with its per-face arrays intact.
        /// </summary>
        [RealCacheFact]
        public void EveryExportedModel_ReImportsToTheBytesItCameFromAndSurvivesAnEdit()
        {
            RSReferenceTable table = _cache.Table(RSConstants.MODELS_INDEX);
            CacheAddressing addressing = CacheAddressing.For(RSConstants.MODELS_INDEX);
            var failures = new List<string>();
            var encodings = new SortedDictionary<ModelEncoding, int>();

            int covered = 0;
            int identical = 0;
            int withTextureCoordinates = 0;
            int rebuilt = 0;
            int rebuildsRefused = 0;
            int edited = 0;
            int empty = 0;
            long storedBytes = 0;
            long rebuiltBytes = 0;

            foreach (int archiveId in _cache.ArchivesToExamine(table))
            {
                byte[] bytes = Payload(archiveId, table, failures);
                if (bytes == null)
                    continue;

                int modelId = addressing.DefinitionId(archiveId, table.GetArchiveEntry(archiveId).GetValidFileIds()[0]);

                ModelFile file;
                ModelDefinition projection;
                try
                {
                    file = ModelCodec.Decode(bytes, modelId);
                    projection = new ModelDefinition { ModelID = modelId };
                    projection.Decode(new JagStream(bytes));
                }
                catch (Exception ex)
                {
                    Add(failures, $"model {modelId}: could not be decoded - {ex.GetType().Name}: {ex.Message}");
                    continue;
                }

                covered++;
                encodings.TryGetValue(file.Encoding, out int seen);
                encodings[file.Encoding] = seen + 1;

                ObjDocument document = ModelObjExporter.Export(file, projection);
                ObjMesh mesh;
                try
                {
                    mesh = ObjParser.Parse(document.ObjText);
                }
                catch (ModelImportException failure)
                {
                    Add(failures, $"model {modelId}: its own export would not parse - {failure.Message}");
                    continue;
                }

                if (mesh.TexCoords.Count > 0)
                    withTextureCoordinates++;

                string defect = RoundTrips(file, mesh, bytes);
                if (defect != null)
                {
                    Add(failures, $"model {modelId}: {defect}");
                    continue;
                }

                identical++;

                defect = Rebuilds(file, modelId, ref rebuilt, ref rebuildsRefused, ref rebuiltBytes);
                if (defect != null)
                    Add(failures, $"model {modelId}: {defect}");

                storedBytes += bytes.Length;

                //A model with no vertices has nothing to move, so an edit pass over it would
                //assert that unchanged geometry came back changed.
                if (file.VertexCount == 0)
                {
                    empty++;
                    edited++;
                    continue;
                }

                defect = SurvivesAnEdit(file, mesh, modelId);
                if (defect != null)
                    Add(failures, $"model {modelId}: {defect}");
                else
                    edited++;
            }

            _output.WriteLine($"{identical} of {covered} sampled models exported and re-imported to " +
                              "the bytes they were read from");
            _output.WriteLine("  encodings covered: " +
                              string.Join(", ", encodings.Select(entry => $"{entry.Key}={entry.Value}")));
            _output.WriteLine($"  {withTextureCoordinates} carried texture coordinates");
            _output.WriteLine($"  {rebuilt} rebuilt through the delta and strip-opcode encoder and " +
                              $"decoded back, {rebuildsRefused} refused");
            _output.WriteLine($"  {edited} survived a one-unit translation with every per-face array " +
                              $"intact, {empty} of them vacuously because they hold no vertices");
            _output.WriteLine($"  {storedBytes} stored bytes became {rebuiltBytes} when the geometry " +
                              "blocks were rebuilt from scratch");
            if (!_cache.FullSweep)
            {
                _output.WriteLine($"sampled up to {RealCacheFixture.SampleArchivesPerIndex} models; " +
                                  $"set {RealCacheLocator.FullSweepVariable}=1 to cover every one");
            }

            AssertNoFailures(failures);
            Assert.True(covered > 0, "no model was exported, so nothing was checked");
            Assert.Equal(covered, identical);
            Assert.Equal(covered, edited);
        }

        // ===================================================================
        //  The three passes
        // ===================================================================

        /// <summary>
        ///     An unedited export must re-import as the very same model, and re-encode to the bytes
        ///     it was read from.
        /// </summary>
        /// <remarks>
        ///     The identity is the point. Rebuilding the blocks from unchanged geometry would
        ///     renormalise the strip opcodes and smart widths and rewrite a file nobody edited, so
        ///     the import has to recognise that there is nothing to do.
        /// </remarks>
        /// <returns>What went wrong, or null.</returns>
        private static string RoundTrips(ModelFile file, ObjMesh mesh, byte[] bytes)
        {
            ModelImportResult result = ModelObjImporter.Import(file, mesh);
            if (!result.Succeeded)
                return "its own export was refused on import - " + result.Message;
            if (result.GeometryChanged)
                return "its own export re-imported as changed geometry, so the export or the " +
                       "import lost something";
            if (!ReferenceEquals(result.Model, file))
                return "an unchanged import handed back a different model rather than the one it " +
                       "was given";

            byte[] again = ModelCodec.Encode(result.Model).ToArray();
            return again.AsSpan().SequenceEqual(bytes)
                ? null
                : $"re-encoded to {again.Length} bytes from a stored {bytes.Length} after a round trip";
        }

        /// <summary>
        ///     The freshly computed geometry blocks must read back through the production decoder.
        /// </summary>
        /// <remarks>
        ///     Handed the model's own geometry, so the mesh is not in question and only the encoding
        ///     of it is: the per-vertex masks, the three delta streams, the strip opcodes, the
        ///     face-index deltas and the four declared lengths that locate them.
        /// </remarks>
        /// <returns>What went wrong, or null.</returns>
        private static string Rebuilds(ModelFile file, int modelId, ref int succeeded, ref int refused,
            ref long bytes)
        {
            ModelGeometry geometry = ModelGeometry.FromFile(file);

            ModelFile rebuilt;
            try
            {
                rebuilt = ModelGeometryEncoder.Rebuild(file, geometry);
            }
            catch (ModelImportException)
            {
                //Only a new-protocol model carrying the trailing block's per-face flag, which
                //nothing in either cache does. Counted rather than ignored.
                refused++;
                return null;
            }

            byte[] encoded = ModelCodec.Encode(rebuilt).ToArray();
            bytes += encoded.Length;

            ModelFile again;
            try
            {
                again = ModelCodec.Decode(encoded, modelId);
            }
            catch (Exception ex)
            {
                return $"a rebuild of its own geometry would not decode - {ex.GetType().Name}: {ex.Message}";
            }

            if (!geometry.Matches(ModelGeometry.FromFile(again)))
                return "a rebuild of its own geometry decoded to different geometry";

            succeeded++;
            return null;
        }

        /// <summary>
        ///     A real edit must land, and must leave every array OBJ cannot express untouched.
        /// </summary>
        /// <remarks>
        ///     One unit along X, which is the smallest change that cannot be mistaken for no change.
        ///     The check that matters is what came through unaltered: the colours, render types,
        ///     priorities, alphas, skins, texture ids and textured-triangle blocks are compared
        ///     against the original after a full encode and decode, so a rebuild that dropped one
        ///     fails here rather than in the viewport.
        /// </remarks>
        /// <returns>What went wrong, or null.</returns>
        private static string SurvivesAnEdit(ModelFile file, ObjMesh mesh, int modelId)
        {
            var moved = new ObjMesh(
                mesh.Positions.Select(position => new ObjVertex(position.X + 1, position.Y, position.Z)).ToList(),
                mesh.TexCoords, mesh.Faces, mesh.NormalCount, mesh.TriangulatedPolygons, mesh.MaterialNames);

            ModelImportResult result = ModelObjImporter.Import(file, moved);
            if (!result.Succeeded)
            {
                //A new-protocol model with the trailing block's flag bits is the one legitimate
                //refusal, and neither cache holds one.
                return "a one-unit translation was refused - " + result.Message;
            }

            if (!result.GeometryChanged)
                return "a one-unit translation re-imported as unchanged geometry";

            ModelFile again;
            try
            {
                again = ModelCodec.Decode(ModelCodec.Encode(result.Model).ToArray(), modelId);
            }
            catch (Exception ex)
            {
                return $"an edited model would not decode - {ex.GetType().Name}: {ex.Message}";
            }

            ModelGeometry before = ModelGeometry.FromFile(file);
            ModelGeometry after = ModelGeometry.FromFile(again);
            if (after.VertexCount != before.VertexCount || after.FaceCount != before.FaceCount)
                return "an edit changed the counts";

            for (int i = 0; i < before.VertexCount; i++)
            {
                if (after.X[i] != before.X[i] + 1 || after.Y[i] != before.Y[i] || after.Z[i] != before.Z[i])
                    return $"vertex {i} did not move exactly one unit along X";
            }

            for (int i = 0; i < before.FaceCount; i++)
            {
                if (after.FaceA[i] != before.FaceA[i] || after.FaceB[i] != before.FaceB[i] ||
                    after.FaceC[i] != before.FaceC[i])
                    return $"face {i} names different vertices after an edit that moved none of them";
            }

            return Preserved(file, again);
        }

        /// <summary>
        ///     Compares everything OBJ cannot express, after a full encode and decode.
        /// </summary>
        /// <returns>What differs, or null.</returns>
        private static string Preserved(ModelFile before, ModelFile after)
        {
            if (!Same(before.FaceColours, after.FaceColours))
                return "the face colours changed";
            if (!Same(before.FaceTypeBytes, after.FaceTypeBytes))
                return "the face render types changed";
            if (!Same(before.FacePriorities, after.FacePriorities))
                return "the face priorities changed";
            if (!Same(before.FaceAlphas, after.FaceAlphas))
                return "the face alphas changed";
            if (!Same(before.FaceTextureIds, after.FaceTextureIds))
                return "the face texture ids changed";
            if (!SameSkins(before.FaceSkins, after.FaceSkins))
                return "the face skin groups changed";
            if (!SameSkins(before.VertexSkins, after.VertexSkins))
                return "the vertex skin groups changed";
            if (!SameSkins(before.TextureCoords, after.TextureCoords))
                return "the texture coordinate indices changed";
            if (!Same(before.TextureTypes, after.TextureTypes))
                return "the textured triangle types changed";
            if (!Same(before.TextureVertexA, after.TextureVertexA) ||
                !Same(before.TextureVertexB, after.TextureVertexB) ||
                !Same(before.TextureVertexC, after.TextureVertexC))
                return "the textured triangle reference vertices changed";
            if (!Same(before.TextureScaleP, after.TextureScaleP) ||
                !Same(before.TextureScaleQ, after.TextureScaleQ) ||
                !Same(before.TextureScaleR, after.TextureScaleR))
                return "the textured triangle projection scalars changed";
            if (before.Emitters?.Length != after.Emitters?.Length)
                return "the particle emitters changed";
            if (before.Effectors?.Length != after.Effectors?.Length)
                return "the particle effectors changed";
            if (before.Bonds?.Length != after.Bonds?.Length)
                return "the billboard bonds changed";
            if (before.Flags != after.Flags || before.FormatType != after.FormatType)
                return "the flags or the format type changed";
            if (before.Gap.Length != after.Gap.Length)
                return "the gap before the footer changed";

            return null;
        }

        private static bool Same<T>(T[] before, T[] after) where T : IEquatable<T>
        {
            if (before == null || after == null)
                return before == null && after == null;
            return before.AsSpan().SequenceEqual(after);
        }

        private static bool SameSkins(StoredSmart[] before, StoredSmart[] after)
        {
            if (before == null || after == null)
                return before == null && after == null;
            if (before.Length != after.Length)
                return false;

            for (int i = 0; i < before.Length; i++)
            {
                if (before[i].Value != after[i].Value || before[i].Width != after[i].Width)
                    return false;
            }

            return true;
        }

        // ===================================================================
        //  Reading
        // ===================================================================

        private byte[] Payload(int archiveId, RSReferenceTable table, List<string> failures)
        {
            byte[] stored = _cache.RawContainer(RSConstants.MODELS_INDEX, archiveId);
            if (stored == null)
                return null;

            int[] fileIds = table.GetArchiveEntry(archiveId)?.GetValidFileIds();
            if (fileIds == null || fileIds.Length == 0)
                return null;

            try
            {
                RSContainer container = _cache.TryDecodeContainer(RSConstants.MODELS_INDEX, archiveId, stored);
                if (container == null)
                {
                    Add(failures, $"model {archiveId}: container would not decode");
                    return null;
                }

                RSArchive archive = RSArchive.Decode(container.GetStream(), fileIds);
                byte[] bytes = archive.GetFile(fileIds[0])?.ToArray();
                if (bytes == null || bytes.Length == 0)
                {
                    Add(failures, $"model {archiveId}: unpacked to no bytes at all");
                    return null;
                }

                return bytes;
            }
            catch (Exception ex)
            {
                Add(failures, $"model {archiveId}: could not be read - {ex.GetType().Name}: {ex.Message}");
                return null;
            }
        }

        private static void Add(List<string> failures, string detail)
        {
            if (failures.Count < MaxReportedFailures)
                failures.Add(detail);
            else if (failures.Count == MaxReportedFailures)
                failures.Add("... and more, truncated");
        }

        private static void AssertNoFailures(List<string> failures)
        {
            if (failures.Count == 0)
                return;

            Assert.Fail("models did not survive the OBJ round trip:" + Environment.NewLine +
                        string.Join(Environment.NewLine, failures));
        }
    }
}
