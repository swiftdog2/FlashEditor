using System;
using System.Linq;
using FlashEditor.Cache;
using FlashEditor.Definitions.Models;
using FlashEditor.Definitions.Models.Interchange;
using Xunit;
using Xunit.Abstractions;

namespace FlashEditor.Tests.Cache.RealCache {
    /// <summary>
    ///     A model built from nothing, checked against a model the cache shipped.
    /// </summary>
    /// <remarks>
    ///     Model 6702 is Dharok's greataxe as worn: the reference weapon for a new one, and in both
    ///     caches. Rebuilding it from its own decoded fields and requiring the stored bytes is what
    ///     makes the builder's choices - block order, footer, flags, smart widths, strip opcodes -
    ///     answer to an encoder this project did not write. A round trip through our own decoder
    ///     could not.
    /// </remarks>
    public sealed class RealCacheNewModelTests : IClassFixture<RealCacheFixture> {
        /// <summary>Dharok's greataxe, worn.</summary>
        private const int Greataxe = 6702;

        private readonly RealCacheFixture _cache;
        private readonly ITestOutputHelper _output;

        public RealCacheNewModelTests(RealCacheFixture cache, ITestOutputHelper output) {
            _cache = cache;
            _output = output;
        }

        [RealCacheFact]
        public void ModelFileBuilder_RebuildsModel6702FromItsOwnFields_ToTheStoredBytes() {
            byte[] stored = StoredGreataxe(out ModelFile decoded);

            ModelFile built = ModelFileBuilder.Build(BlueprintOf(decoded));

            Assert.Equal(stored, built.Encode().ToArray());
        }

        // ===================================================================
        //  Helpers
        // ===================================================================

        private byte[] StoredGreataxe(out ModelFile decoded) {
            RSCache open = _cache.OpenCache();
            int fileId = _cache.Table(RSConstants.MODELS_INDEX).GetArchiveEntry(Greataxe)!.GetValidFileIds()[0];
            byte[] stored = open.ReadFileBytes(RSConstants.MODELS_INDEX, Greataxe, fileId);
            decoded = ModelCodec.Decode(stored, Greataxe);

            _output.WriteLine($"model {Greataxe}: {decoded.Encoding}, format type {decoded.FormatType}, " +
                              $"{decoded.VertexCount} vertices, {decoded.FaceCount} faces, " +
                              $"priority flag {decoded.PriorityFlag}, alpha flag {decoded.AlphaFlag}, " +
                              $"flags 0x{decoded.Flags:X2}, textured {decoded.TexturedFaceCount}");
            _output.WriteLine("vertex skins: " + Distinct(decoded.VertexSkins?.Select(s => s.Value)));
            _output.WriteLine("face skins: " + Distinct(decoded.FaceSkins?.Select(s => s.Value)));
            _output.WriteLine("raw face texture ids: " + Distinct(decoded.FaceTextureIds?.Select(t => (int) t)) +
                              ", textured triangle types: " + Distinct(decoded.TextureTypes?.Select(t => (int) t)) +
                              ", projection slack: " + BitConverter.ToString(decoded.SlackTextureScale));
            _output.WriteLine("render types: " + Distinct(decoded.FaceTypeBytes?.Select(b => (int) b)));
            return stored;
        }

        private static string Distinct(System.Collections.Generic.IEnumerable<int>? values) =>
            values == null ? "absent" : string.Join(",", values.Distinct().OrderBy(v => v));

        /// <summary>Everything a from-scratch model states, read off a decoded one.</summary>
        private static ModelBlueprint BlueprintOf(ModelFile file) => new ModelBlueprint {
            ModelId = file.ModelId,
            FormatType = file.FormatType,
            Geometry = ModelGeometry.FromFile(file),
            FaceColours = file.FaceColours,
            FaceRenderTypes = file.FaceTypeBytes,
            GlobalPriority = file.PriorityFlag,
            FaceSkins = file.FaceSkins?.Select(s => s.Value).ToArray(),
            VertexSkins = file.VertexSkins?.Select(s => s.Value).ToArray(),
            Texturing = new ModelTexturing {
                FaceTextureIds = file.FaceTextureIds ?? Array.Empty<ushort>(),
                TextureCoords = file.TextureCoords.Select(c => c.Value).ToArray(),
                TextureTypes = file.TextureTypes!,
                VertexA = file.TextureVertexA,
                VertexB = file.TextureVertexB,
                VertexC = file.TextureVertexC,
                ScaleP = file.TextureScaleP,
                ScaleQ = file.TextureScaleQ,
                ScaleR = file.TextureScaleR,
                FieldA = file.TextureFieldA,
                FieldB = file.TextureFieldB,
                FieldC = file.TextureFieldC,
                Type2FieldA = file.TextureType2FieldA,
                Type2FieldB = file.TextureType2FieldB,
                ScaleSlack = file.SlackTextureScale
            }
        };
    }
}
