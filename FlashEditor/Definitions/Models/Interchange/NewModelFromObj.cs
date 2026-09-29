using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using FlashEditor.Cache;

namespace FlashEditor.Definitions.Models.Interchange {
    /// <summary>
    ///     Builds a brand-new model from an OBJ and the colours of its materials.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///     The counterpart of <see cref="ModelObjImporter"/>, which writes a mesh over a model that
    ///     exists and takes everything OBJ cannot say from it. Here there is no such model, so what
    ///     OBJ cannot say is fixed, and fixed to what a worn weapon needs - the values Dharok's
    ///     greataxe (model 6702) carries: newer format, format type 15 so the client draws it at
    ///     stored scale, face label 29 on every face, global priority 10, render type 0, untextured.
    ///     </para>
    ///     <para>
    ///     Axes are the exact inverse of <see cref="ModelObjExporter"/>: OBJ (x, y, z) is model
    ///     (x, -y, -z). Nothing is scaled; one model unit is one OBJ unit, and a tile is 512.
    ///     </para>
    /// </remarks>
    public static class NewModelFromObj {
        /// <summary>The vertex label every vertex of a worn weapon carries, as on model 6702.</summary>
        public const int WeaponVertexLabel = 50;

        /// <summary>The face label every face carries, as on model 6702.</summary>
        public const int FaceLabel = 29;

        /// <summary>The render priority every face shares, as on model 6702.</summary>
        public const int Priority = 10;

        /// <summary>The format type: 13 or more, so the client draws stored coordinates unscaled.</summary>
        public const int FormatType = 15;

        /// <summary>Builds a model from a parsed OBJ.</summary>
        /// <param name="mesh">The parsed OBJ.</param>
        /// <param name="materialRgb">Each material's <c>Kd</c> as 24-bit RGB, from <see cref="MtlParser"/>.</param>
        /// <param name="modelId">The id the model will be stored at.</param>
        /// <param name="vertexLabel">
        ///     The label every vertex carries, 0 to 255, or null for no vertex labels.
        ///     <see cref="WeaponVertexLabel"/> for a worn weapon.
        /// </param>
        /// <returns>
        ///     The built model, or a refusal saying why there is none. Never null.
        /// </returns>
        public static ModelImportResult Build(ObjMesh mesh, IReadOnlyDictionary<string, int> materialRgb,
            int modelId, int? vertexLabel) {
            if (mesh == null)
                throw new ArgumentNullException(nameof(mesh));
            if (materialRgb == null)
                throw new ArgumentNullException(nameof(materialRgb));

            try {
                if (mesh.Faces.Count == 0)
                    throw new ModelImportException("The OBJ has no faces, so there is no model to build.");

                ModelGeometry geometry = ModelObjImporter.ToGeometry(mesh, out double snapped);
                ushort[] colours = Colours(mesh, materialRgb, out int materialsUsed);

                int[]? vertexLabels = null;
                if (vertexLabel.HasValue) {
                    vertexLabels = new int[geometry.VertexCount];
                    Array.Fill(vertexLabels, vertexLabel.Value);
                }

                var faceLabels = new int[geometry.FaceCount];
                Array.Fill(faceLabels, FaceLabel);

                ModelFile model = ModelFileBuilder.Build(new ModelBlueprint {
                    ModelId = modelId,
                    FormatType = FormatType,
                    Geometry = geometry,
                    FaceColours = colours,
                    GlobalPriority = Priority,
                    FaceSkins = faceLabels,
                    VertexSkins = vertexLabels
                });

                return Built(model, mesh, materialsUsed, vertexLabel, snapped);
            }
            catch (ModelImportException failure) {
                return new ModelImportResult(false, false, null, failure.Message, new[] {
                    new ModelImportEntry("new model", ModelImportDisposition.Refused, failure.Message)
                });
            }
        }

        /// <summary>
        ///     Builds a model from an OBJ on disk and its material libraries, and stages it into
        ///     index 7 as a new group at the next model id.
        /// </summary>
        /// <remarks>
        ///     <para>
        ///     The id is not a choice: an idx file cannot express a hole, so
        ///     <see cref="RSCache.NextCreatableGroupId"/> is the only id a group can be created at.
        ///     When that id is one of 63607-63613 the build is refused, and no model can be created
        ///     in that cache until the range is behind it - which is where the vanilla b639 capture
        ///     stands, since it ends at 63606.
        ///     </para>
        ///     <para>
        ///     Staged like any edit: nothing reaches the disk until the cache is saved. Each call
        ///     takes the next id, so a second call creates a second model.
        ///     </para>
        /// </remarks>
        /// <param name="cache">The open cache to stage into.</param>
        /// <param name="objPath">The OBJ. Its <c>mtllib</c> files are read from beside it.</param>
        /// <param name="vertexLabel">
        ///     The label every vertex carries, or null for none; <see cref="WeaponVertexLabel"/> for
        ///     a worn weapon.
        /// </param>
        /// <returns>
        ///     The model staged, whose <see cref="ModelFile.ModelId"/> is the id it took, or a refusal
        ///     that staged nothing.
        /// </returns>
        /// <exception cref="IOException">The OBJ or a library it names cannot be read.</exception>
        /// <exception cref="ModelImportException">The OBJ or a library is malformed.</exception>
        public static ModelImportResult StageFile(RSCache cache, string objPath, int? vertexLabel) {
            if (cache == null)
                throw new ArgumentNullException(nameof(cache));
            if (objPath == null)
                throw new ArgumentNullException(nameof(objPath));

            string objText = File.ReadAllText(objPath);
            ObjMesh mesh = ObjParser.Parse(objText);
            IReadOnlyDictionary<string, int> materials = ReadLibraries(objText, objPath);

            int modelId = cache.NextCreatableGroupId(RSConstants.MODELS_INDEX);
            ModelImportResult result = Build(mesh, materials, modelId, vertexLabel);
            if (!result.Succeeded)
                return result;

            cache.CreateGroup(RSConstants.MODELS_INDEX, modelId, new[] {
                new RSGroupFile(0, result.Model!.Encode())
            });
            return result;
        }

        /// <summary>
        ///     Every <c>Kd</c> in the libraries an OBJ names, later libraries overriding earlier ones.
        /// </summary>
        /// <remarks>
        ///     Read from the OBJ text here because <see cref="ObjMesh"/> carries only the mesh. A
        ///     library that is named and missing throws rather than yielding no colours, so the failure
        ///     names the file instead of every face.
        /// </remarks>
        private static IReadOnlyDictionary<string, int> ReadLibraries(string objText, string objPath) {
            string directory = Path.GetDirectoryName(Path.GetFullPath(objPath)) ?? string.Empty;
            var merged = new Dictionary<string, int>(StringComparer.Ordinal);

            foreach (string raw in objText.Split('\n')) {
                string line = raw.Trim();
                if (!line.StartsWith("mtllib ", StringComparison.Ordinal) && !line.StartsWith("mtllib\t", StringComparison.Ordinal))
                    continue;

                //The format allows several names on one line, and Blender writes a name with
                //spaces as it stands, so a single name is taken whole when it exists.
                string rest = line.Substring("mtllib".Length).Trim();
                string[] names = File.Exists(Path.Combine(directory, rest))
                    ? new[] { rest }
                    : rest.Split((char[]?) null, StringSplitOptions.RemoveEmptyEntries);

                foreach (string name in names) {
                    foreach (KeyValuePair<string, int> entry in MtlParser.ParseFile(Path.Combine(directory, name)))
                        merged[entry.Key] = entry.Value;
                }
            }

            return merged;
        }

        /// <summary>
        ///     Each face's colour word, from the <c>Kd</c> of the material in force at it.
        /// </summary>
        /// <remarks>
        ///     A face with no material, or one whose material has no <c>Kd</c>, is refused rather
        ///     than given a default, because a default is a colour nobody chose written into the
        ///     cache.
        /// </remarks>
        private static ushort[] Colours(ObjMesh mesh, IReadOnlyDictionary<string, int> materialRgb,
            out int materialsUsed) {
            var colours = new ushort[mesh.Faces.Count];
            var words = new Dictionary<string, ushort>(StringComparer.Ordinal);

            for (int i = 0; i < colours.Length; i++) {
                string? material = mesh.Faces[i].Material;
                if (material == null) {
                    throw new ModelImportException(
                        $"Face {i} comes before any usemtl, so it has no colour. Every face needs a " +
                        "material whose Kd gives its colour.");
                }

                if (!words.TryGetValue(material, out ushort word)) {
                    if (!materialRgb.TryGetValue(material, out int rgb)) {
                        throw new ModelImportException(
                            $"Face {i} uses material \"{material}\", and the material library states " +
                            "no Kd for it, so the face has no colour. Check the OBJ's mtllib names " +
                            "the right file.");
                    }
                    word = ModelColour.FromRgb(rgb);
                    words[material] = word;
                }
                colours[i] = word;
            }

            materialsUsed = words.Count;
            return colours;
        }

        private static ModelImportResult Built(ModelFile model, ObjMesh mesh, int materialsUsed,
            int? vertexLabel, double snapped) {
            var entries = new List<ModelImportEntry> {
                new ModelImportEntry("vertex coordinates", ModelImportDisposition.Replaced,
                    $"{model.VertexCount} vertices, taken from the OBJ as model (x, -y, -z)"),
                new ModelImportEntry("faces", ModelImportDisposition.Replaced,
                    $"{model.FaceCount} faces, taken from the OBJ in file order"),
                new ModelImportEntry("face colours", ModelImportDisposition.Replaced,
                    $"{materialsUsed} materials, each Kd converted to the colour word the client " +
                    "draws nearest to it; unlit, as every model colour is"),
                new ModelImportEntry("vertex labels", ModelImportDisposition.Replaced, vertexLabel.HasValue
                    ? $"{vertexLabel.Value} on every vertex"
                    : "none, so no animation moves this model"),
                new ModelImportEntry("face labels, priority, format", ModelImportDisposition.Replaced,
                    $"face label {FaceLabel} on every face, global priority {Priority}, render type 0, " +
                    $"format type {FormatType} (drawn at stored scale), newer format, untextured"),
                new ModelImportEntry("OBJ texture coordinates", ModelImportDisposition.Ignored,
                    mesh.TexCoords.Count + " vt entries; the model is untextured"),
                new ModelImportEntry("OBJ normals", ModelImportDisposition.Ignored,
                    mesh.NormalCount + " vn entries; the client computes its own")
            };

            if (mesh.TriangulatedPolygons > 0) {
                entries.Add(new ModelImportEntry("OBJ polygons", ModelImportDisposition.Replaced,
                    mesh.TriangulatedPolygons + " faces had more than three corners and were fanned " +
                    "into triangles"));
            }

            if (snapped > 0.0) {
                entries.Add(new ModelImportEntry("coordinate rounding", ModelImportDisposition.Replaced,
                    "the furthest a vertex moved to reach a whole number was " +
                    snapped.ToString("0.######", CultureInfo.InvariantCulture) +
                    "; model coordinates are integers"));
            }

            return new ModelImportResult(true, true, model,
                $"Model {model.ModelId}: {model.VertexCount} vertices, {model.FaceCount} faces, " +
                $"{materialsUsed} colours.", entries);
        }
    }
}
