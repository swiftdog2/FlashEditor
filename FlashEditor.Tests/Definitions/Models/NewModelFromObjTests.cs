using System.Collections.Generic;
using FlashEditor.Definitions.Models;
using FlashEditor.Definitions.Models.Interchange;
using Xunit;

namespace FlashEditor.Tests.Definitions.Models {
    /// <summary>What a new model from an OBJ refuses, and the ids it may be stored at.</summary>
    public sealed class NewModelFromObjTests {
        private const string Triangle =
            "v 0 0 0\nv 10 0 0\nv 0 10 0\nusemtl red\nf 1 2 3\n";

        private static readonly IReadOnlyDictionary<string, int> Red = new Dictionary<string, int> { ["red"] = 0xFF0000 };

        [Theory]
        [InlineData(63606)]
        [InlineData(63614)]
        public void Build_StoresAtAnIdTheClientReadsAsNewerFormat(int modelId) {
            ModelImportResult result = NewModelFromObj.Build(ObjParser.Parse(Triangle), Red, modelId, null);

            Assert.True(result.Succeeded, result.Message);
            Assert.Equal(modelId, result.Model!.ModelId);
        }

        /// <summary>The client hard-codes these to its new-protocol decoder (Model.java:90-94).</summary>
        [Theory]
        [InlineData(63607)]
        [InlineData(63610)]
        [InlineData(63613)]
        public void Build_RefusesTheIdsTheClientDecodesAsNewProtocol(int modelId) {
            ModelImportResult result = NewModelFromObj.Build(ObjParser.Parse(Triangle), Red, modelId, null);

            Assert.False(result.Succeeded);
            Assert.Null(result.Model);
            Assert.Contains("63607-63613", result.Message);
            Assert.Contains("Model.java:90-94", result.Message);
        }

        [Fact]
        public void Build_RefusesAFaceBeforeAnyUsemtl() {
            ModelImportResult result = NewModelFromObj.Build(
                ObjParser.Parse("v 0 0 0\nv 1 0 0\nv 0 1 0\nf 1 2 3\n"), Red, 1, null);

            Assert.False(result.Succeeded);
            Assert.Contains("Face 0", result.Message);
        }

        [Fact]
        public void Build_RefusesAMaterialTheLibraryGivesNoKd() {
            ModelImportResult result = NewModelFromObj.Build(ObjParser.Parse(Triangle),
                new Dictionary<string, int>(), 1, null);

            Assert.False(result.Succeeded);
            Assert.Contains("\"red\"", result.Message);
        }

        [Fact]
        public void Build_RefusesAnObjWithNoFaces() {
            ModelImportResult result = NewModelFromObj.Build(ObjParser.Parse("v 0 0 0\n"), Red, 1, null);

            Assert.False(result.Succeeded);
            Assert.Contains("no faces", result.Message);
        }

        [Fact]
        public void Build_WithNoVertexLabel_StoresNoVertexLabelBlock() {
            ModelFile model = NewModelFromObj.Build(ObjParser.Parse(Triangle), Red, 1, null).Model!;
            ModelFile decoded = ModelCodec.Decode(model.Encode().ToArray(), 1);

            Assert.Equal(0, decoded.VertexSkinFlag);
            Assert.Null(decoded.VertexSkins);
        }

        /// <summary>A small worked case of the axes: OBJ (x, y, z) is model (x, -y, -z).</summary>
        [Fact]
        public void Build_TakesObjUpAsModelUp() {
            ModelFile model = NewModelFromObj.Build(
                ObjParser.Parse("v 1 2 3\nv 4 5 6\nv 7 8 9\nusemtl red\nf 1 2 3\n"), Red, 1, null).Model!;

            ModelGeometry geometry = ModelGeometry.FromFile(ModelCodec.Decode(model.Encode().ToArray(), 1));

            Assert.Equal(new[] { 1, 4, 7 }, geometry.X);
            Assert.Equal(new[] { -2, -5, -8 }, geometry.Y);
            Assert.Equal(new[] { -3, -6, -9 }, geometry.Z);
        }
    }
}
