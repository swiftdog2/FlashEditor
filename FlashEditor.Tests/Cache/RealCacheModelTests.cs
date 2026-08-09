using FlashEditor.Cache;
using FlashEditor.Definitions;
using FlashEditor.Tests.Cache.RealCache;
using FlashEditor.Utils;
using System;
using System.Collections.Generic;
using System.Linq;
using Xunit;
using Xunit.Abstractions;
using FlashEditor.IO;
using FlashEditor.Definitions.Models;

namespace FlashEditor.Tests.Cache
{
    /// <summary>
    ///     Runs every model in the cache through the production decoder.
    /// </summary>
    /// <remarks>
    ///     The model decoder was written against the Java client bundled with this cache, and
    ///     that client is build 637 while the cache is build 639 - see AGENTS.md. Two builds is
    ///     a small gap and the model format very likely did not move across it, but "very
    ///     likely" is the sort of assumption the rest of this suite exists to replace.
    ///     <para>
    ///     A model is not a flat record: the decoder seeks to offsets computed from a footer at
    ///     the end of the buffer, so a misparse does not overrun and throw - it silently reads
    ///     the wrong bytes. What it cannot fake is self-consistency. Every face names three
    ///     vertices by index, so if the vertex and face sections were located wrongly those
    ///     indices point outside the vertex array. Across tens of thousands of models that is a
    ///     sensitive detector for a format mismatch, and it is the closest thing available to
    ///     "the parse consumed exactly the bytes it should have".
    ///     </para>
    /// </remarks>
    public class RealCacheModelTests : IClassFixture<RealCacheFixture>
    {
        private readonly RealCacheFixture _cache;
        private readonly ITestOutputHelper _output;

        /// <summary>Failures listed before the report is truncated.</summary>
        private const int MaxReportedFailures = 10;

        /// <summary>Binds the shared open cache and the per-test output sink.</summary>
        public RealCacheModelTests(RealCacheFixture cache, ITestOutputHelper output)
        {
            _cache = cache;
            _output = output;
        }

        /// <summary>
        ///     Decodes every model and checks the geometry it produces refers only to vertices
        ///     that model actually declares.
        /// </summary>
        [RealCacheFact]
        public void AllModels_DecodeToGeometryThatIndexesItsOwnVertices()
        {
            RSReferenceTable table = _cache.Table(RSConstants.MODELS_INDEX);
            var failures = new List<string>();
            var formats = new SortedDictionary<ModelDefinition.ModelFormat, int>();
            int decoded = 0;
            long vertices = 0;
            long triangles = 0;

            foreach (int archiveId in _cache.ArchivesToExamine(table))
            {
                byte[] stored = _cache.RawContainer(RSConstants.MODELS_INDEX, archiveId);
                if (stored == null)
                    continue;

                int[] fileIds = table.GetArchiveEntry(archiveId).GetValidFileIds();
                if (fileIds.Length == 0)
                    continue;

                JagStream data;
                try
                {
                    RSContainer container = _cache.TryDecodeContainer(RSConstants.MODELS_INDEX, archiveId, stored);
                    if (container == null)
                    {
                        failures.Add($"model {archiveId}: container would not decode");
                        continue;
                    }

                    RSArchive archive = RSArchive.Decode(container.GetStream(), fileIds);
                    data = archive.GetFile(fileIds[0]);
                }
                catch (Exception ex)
                {
                    failures.Add($"model {archiveId}: could not be read - {ex.GetType().Name}: {ex.Message}");
                    continue;
                }

                var model = new ModelDefinition { ModelID = archiveId };

                try
                {
                    ModelDefinition.ModelFormat format =
                        ModelDefinition.GetModelFormat(new JagStream(data.ToArray()), archiveId);
                    formats.TryGetValue(format, out int seen);
                    formats[format] = seen + 1;

                    model.Decode(data);
                    decoded++;
                }
                catch (Exception ex)
                {
                    failures.Add($"model {archiveId}: decode threw {ex.GetType().Name}: {ex.Message}");
                    continue;
                }

                string defect = Validate(model);
                if (defect != null)
                    failures.Add($"model {archiveId}: {defect}");

                vertices += model.VertexCount;
                triangles += model.TriangleCount;
            }

            _output.WriteLine($"{decoded} models decoded, {vertices} vertices and {triangles} triangles in total");
            _output.WriteLine("formats seen: " + string.Join(", ", formats.Select(f => $"{f.Key}={f.Value}")));
            if (!_cache.FullSweep)
            {
                _output.WriteLine($"sampled up to {RealCacheFixture.SampleArchivesPerIndex} models; " +
                                  $"set {RealCacheLocator.FullSweepVariable}=1 to decode every one");
            }

            Assert.True(decoded > 0, "no model was decoded, so nothing was checked");
            AssertNoFailures(failures, "models did not decode to self-consistent geometry");
        }

        /// <summary>
        ///     Returns a description of the first structural problem in a decoded model, or
        ///     <c>null</c> when it is self-consistent.
        /// </summary>
        /// <remarks>
        ///     The vertex-index bound is the load-bearing check. The array-length checks come
        ///     first only because a length mismatch would otherwise surface as a confusing
        ///     out-of-range report.
        /// </remarks>
        private static string Validate(ModelDefinition model)
        {
            if (model.VertexCount < 0 || model.TriangleCount < 0)
                return $"negative counts - {model.VertexCount} vertices, {model.TriangleCount} triangles";

            if (model.VertX.Length < model.VertexCount ||
                model.VertY.Length < model.VertexCount ||
                model.VertZ.Length < model.VertexCount)
            {
                return $"declares {model.VertexCount} vertices but holds " +
                       $"{model.VertX.Length}/{model.VertY.Length}/{model.VertZ.Length} coordinates";
            }

            if (model.faceIndices1.Length < model.TriangleCount ||
                model.faceIndices2.Length < model.TriangleCount ||
                model.faceIndices3.Length < model.TriangleCount)
            {
                return $"declares {model.TriangleCount} triangles but holds " +
                       $"{model.faceIndices1.Length}/{model.faceIndices2.Length}/{model.faceIndices3.Length} indices";
            }

            for (int i = 0; i < model.TriangleCount; i++)
            {
                if (OutOfRange(model.faceIndices1[i], model.VertexCount) ||
                    OutOfRange(model.faceIndices2[i], model.VertexCount) ||
                    OutOfRange(model.faceIndices3[i], model.VertexCount))
                {
                    return $"face {i} indexes vertices " +
                           $"({model.faceIndices1[i]}, {model.faceIndices2[i]}, {model.faceIndices3[i]}) " +
                           $"of {model.VertexCount}";
                }
            }

            return null;
        }

        private static bool OutOfRange(int index, int vertexCount)
        {
            return index < 0 || index >= vertexCount;
        }

        private static void AssertNoFailures(List<string> failures, string summary)
        {
            if (failures.Count == 0)
                return;

            string detail = string.Join(Environment.NewLine, failures.Take(MaxReportedFailures));
            if (failures.Count > MaxReportedFailures)
                detail += $"{Environment.NewLine}... and {failures.Count - MaxReportedFailures} more";

            Assert.Fail($"{failures.Count} {summary}:{Environment.NewLine}{detail}");
        }
    }
}
