#nullable enable
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

        /// <summary>An id outside 63607-63613, so the built model is read as newer-format.</summary>
        private const int NewModelId = 70000;

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

        /// <summary>
        ///     An exported model built back as a new one keeps its mesh and the colours the client
        ///     draws, and takes the new-weapon attributes rather than the source's.
        /// </summary>
        /// <remarks>
        ///     The export and the build are inverse conventions written separately - OBJ (x, y, z) is
        ///     model (x, -y, -z), and Kd is the drawn colour - so this fails if either side drifts.
        ///     Colours are compared as drawn rather than as words: several words draw alike, and the
        ///     word the model stores is not recoverable from its colour.
        /// </remarks>
        [RealCacheFact]
        public void NewModelFromObj_ExportedModel6702_RebuildsToTheSameGeometryAndDrawnColours() {
            StoredGreataxe(out ModelFile source);
            ObjDocument exported = ModelObjExporter.Export(source);

            ModelImportResult result = NewModelFromObj.Build(ObjParser.Parse(exported.ObjText),
                MtlParser.Parse(exported.MaterialText!), NewModelId, NewModelFromObj.WeaponVertexLabel);

            Assert.True(result.Succeeded, result.Message);
            ModelFile built = ModelCodec.Decode(result.Model!.Encode().ToArray(), NewModelId);

            Assert.True(ModelGeometry.FromFile(source).Matches(ModelGeometry.FromFile(built)));
            for (int i = 0; i < source.FaceCount; i++)
                Assert.Equal(ModelDefinition.RawHslToRgb(source.FaceColours[i]), ModelDefinition.RawHslToRgb(built.FaceColours[i]));

            Assert.Equal(ModelEncoding.Newer, built.Encoding);
            Assert.Equal(15, built.FormatType);
            Assert.Equal(10, built.PriorityFlag);
            Assert.Null(built.FaceTypeBytes);
            Assert.Equal(0, built.TexturedFaceCount);
            Assert.Null(built.FaceTextureIds);
            Assert.All(built.FaceSkins!, label => Assert.Equal(29, label.Value));
            Assert.All(built.VertexSkins!, label => Assert.Equal(50, label.Value));
        }

        // ===================================================================
        //  Helpers
        // ===================================================================

        private byte[] StoredGreataxe(out ModelFile decoded) {
            RSCache open = _cache.OpenCache();
            int fileId = _cache.Table(RSConstants.MODELS_INDEX).GetArchiveEntry(Greataxe)!.GetValidFileIds()[0];
            byte[] stored = open.ReadFileBytes(RSConstants.MODELS_INDEX, Greataxe, fileId);
            decoded = ModelCodec.Decode(stored, Greataxe);

            _output.WriteLine($"model {Greataxe}: format type {decoded.FormatType}, {decoded.VertexCount} vertices, " +
                              $"{decoded.FaceCount} faces, {decoded.TexturedFaceCount} textured triangles");
            return stored;
        }

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
