using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace FlashEditor.Definitions.ModelInterchange {
    /// <summary>
    ///     Writes a model out as OBJ: vertices, faces, texture coordinates where there are any, and
    ///     a material per distinct appearance.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///     The export is lossy by construction and says so in its own header, because the file it
    ///     produces is what a user will edit and re-import. OBJ has no field for a face's render
    ///     type, priority, alpha flag, skin group, textured-triangle type, particle attachment or
    ///     bond, so none of that survives the trip out - which is exactly why
    ///     <see cref="ModelObjImporter"/> takes those back from the model already in the cache
    ///     rather than from the file.
    ///     </para>
    ///     <para>
    ///     Two conventions are fixed rather than optional, so that an import never has to guess
    ///     which one a file used:
    ///     </para>
    ///     <list type="bullet">
    ///     <item><b>Axes.</b> OBJ <c>(x, y, z)</c> is model <c>(x, -y, -z)</c>. A model's Y grows
    ///     downwards, so a straight copy stands every model on its head in a modeller. Negating
    ///     both Y and Z is a half turn about X rather than a mirror, so it leaves handedness and
    ///     therefore face winding alone - which negating Y on its own would not.</item>
    ///     <item><b>Scale.</b> Stored coordinates, with no vertex shift applied. See
    ///     <see cref="ModelGeometry"/> for why exactness wins over consistency here; the header
    ///     states the shift so a reader knows what the client would do with it.</item>
    ///     </list>
    /// </remarks>
    public static class ModelObjExporter {
        /// <summary>The line separator, fixed so an export is reproducible byte for byte.</summary>
        private const string NewLine = "\n";

        /// <summary>
        ///     Exports a model, taking texture coordinates from a projection when one is supplied.
        /// </summary>
        /// <remarks>
        ///     The projection is optional because UVs are the only thing here that is derived rather
        ///     than stored - <see cref="ModelDefinition"/> computes them from the reference triangle
        ///     of each textured face, which is far more work than reading a block. Without one the
        ///     mesh still exports in full; it just carries no <c>vt</c> lines.
        /// </remarks>
        /// <param name="file">The model as stored.</param>
        /// <param name="projection">
        ///     The same model as decoded for the viewer, for its texture coordinates, or null.
        /// </param>
        /// <param name="materialFileName">
        ///     The name to give the material library, or null for one derived from the model id.
        /// </param>
        /// <returns>The OBJ and its material library.</returns>
        public static ObjDocument Export(ModelFile file, ModelDefinition? projection = null,
            string? materialFileName = null) {
            if (file == null)
                throw new ArgumentNullException(nameof(file));

            ModelGeometry geometry = ModelGeometry.FromFile(file);
            List<Material> materials = BuildMaterials(file, out int[] faceMaterial);
            float[][]? u = UvsFor(file, projection, out float[][]? v);

            string? library = materials.Count == 0
                ? null
                : materialFileName ?? "model_" + Int(file.ModelId) + ".mtl";
            var summary = new List<string>();
            var obj = new StringBuilder();

            WriteHeader(obj, file, geometry, materials, u != null, summary);
            if (library != null)
                obj.Append("mtllib ").Append(library).Append(NewLine);
            obj.Append("o model_").Append(Int(file.ModelId)).Append(NewLine);

            for (int i = 0; i < geometry.VertexCount; i++) {
                obj.Append("v ").Append(Int(geometry.X[i]))
                   .Append(' ').Append(Int(-geometry.Y[i]))
                   .Append(' ').Append(Int(-geometry.Z[i])).Append(NewLine);
            }

            int[] uvBase = WriteTextureCoordinates(obj, geometry.FaceCount, u, v);
            WriteFaces(obj, geometry, faceMaterial, materials, uvBase);

            return new ObjDocument(obj.ToString(), BuildMaterialLibrary(file, materials),
                library, summary);
        }

        /// <summary>
        ///     Exports a decoded model, which is the form the editor holds one in.
        /// </summary>
        /// <param name="definition">A model that was decoded from the cache.</param>
        /// <param name="materialFileName">
        ///     The name to give the material library, or null for one derived from the model id.
        /// </param>
        /// <returns>The OBJ and its material library.</returns>
        /// <exception cref="ArgumentException">
        ///     The definition was assembled by hand and has no stored form, so there is no geometry
        ///     to write that could ever be written back.
        /// </exception>
        public static ObjDocument Export(ModelDefinition definition, string? materialFileName = null) {
            if (definition == null)
                throw new ArgumentNullException(nameof(definition));
            if (definition.Source == null)
                throw new ArgumentException(
                    "This model was not decoded from a cache file, so it has no stored form to " +
                    "export. Decode it first.", nameof(definition));

            return Export(definition.Source, definition, materialFileName);
        }

        // ===================================================================
        //  Header
        // ===================================================================

        private static void WriteHeader(StringBuilder obj, ModelFile file, ModelGeometry geometry,
            List<Material> materials, bool hasUvs, List<string> summary) {
            Say(obj, summary, $"model {file.ModelId}, {file.Encoding} encoding, format type {file.FormatType}");
            Say(obj, summary, $"{geometry.VertexCount} vertices, {geometry.FaceCount} faces, " +
                              $"{file.TexturedFaceCount} textured triangles, {materials.Count} materials");
            Say(obj, summary, "axes: OBJ (x, y, z) = model (x, -y, -z), a half turn about X, so " +
                              "face winding is unchanged");
            Say(obj, summary, file.VertexShift == 0
                ? "scale: stored coordinates, which is what the client draws for this format type"
                : $"scale: stored coordinates. The client shifts them left by {file.VertexShift} " +
                  $"before drawing, so it draws this model {1 << file.VertexShift} times larger");
            Say(obj, summary, hasUvs
                ? "texture coordinates are the client's own, in image space; flip V in the " +
                  "modeller if a texture appears upside down"
                : "no texture coordinates: none were computed for this export");
            Say(obj, summary, "re-importing replaces vertices and faces only. Render types, " +
                              "priorities, alphas, skin groups, textured-triangle types, particle " +
                              "attachments and bonds come from the model in the cache");
            Say(obj, summary, "the face count must not change, and per-face attributes are matched " +
                              "by position, so reordering faces here reassigns them");
        }

        private static void Say(StringBuilder obj, List<string> summary, string line) {
            obj.Append("# ").Append(line).Append(NewLine);
            summary.Add(line);
        }

        // ===================================================================
        //  Geometry
        // ===================================================================

        /// <summary>
        ///     Emits every <c>vt</c> line up front and reports where each face's three landed.
        /// </summary>
        /// <remarks>
        ///     All of them before any face, because the format defines an index as a position in the
        ///     list as it stands when the face is read, and a reader that builds the lists as it
        ///     goes resolves a forward reference to nothing.
        /// </remarks>
        /// <returns>
        ///     The zero-based index of each face's first texture coordinate, or -1 where the face
        ///     has none.
        /// </returns>
        private static int[] WriteTextureCoordinates(StringBuilder obj, int faceCount,
            float[][]? u, float[][]? v) {
            var uvBase = new int[faceCount];
            for (int i = 0; i < faceCount; i++)
                uvBase[i] = -1;

            if (u == null || v == null)
                return uvBase;

            int emitted = 0;
            for (int i = 0; i < faceCount; i++) {
                float[]? faceU = i < u.Length ? u[i] : null;
                float[]? faceV = i < v.Length ? v[i] : null;
                if (faceU == null || faceV == null || faceU.Length < 3 || faceV.Length < 3)
                    continue;

                uvBase[i] = emitted;
                for (int corner = 0; corner < 3; corner++) {
                    obj.Append("vt ").Append(Real(faceU[corner]))
                       .Append(' ').Append(Real(faceV[corner])).Append(NewLine);
                }
                emitted += 3;
            }

            return uvBase;
        }

        private static void WriteFaces(StringBuilder obj, ModelGeometry geometry, int[] faceMaterial,
            List<Material> materials, int[] uvBase) {
            int current = -1;
            for (int i = 0; i < geometry.FaceCount; i++) {
                if (faceMaterial[i] != current) {
                    current = faceMaterial[i];
                    obj.Append("usemtl ").Append(materials[current].Name).Append(NewLine);
                }

                obj.Append('f');
                Corner(obj, geometry.FaceA[i], uvBase[i], 0);
                Corner(obj, geometry.FaceB[i], uvBase[i], 1);
                Corner(obj, geometry.FaceC[i], uvBase[i], 2);
                obj.Append(NewLine);
            }
        }

        private static void Corner(StringBuilder obj, int vertex, int uvBase, int corner) {
            obj.Append(' ').Append(Int(vertex + 1));
            if (uvBase >= 0)
                obj.Append('/').Append(Int(uvBase + corner + 1));
        }

        // ===================================================================
        //  Materials
        // ===================================================================

        /// <summary>
        ///     Groups the faces by the appearance a modeller can show: texture, colour and alpha.
        /// </summary>
        /// <remarks>
        ///     Nothing here round-trips - the importer takes all three back from the cache - so this
        ///     exists only so a model does not arrive in Blender as one undifferentiated grey lump.
        /// </remarks>
        /// <param name="file">The model as stored.</param>
        /// <param name="faceMaterial">Which material each face uses, by index into the result.</param>
        /// <returns>The distinct materials, in first-seen order.</returns>
        private static List<Material> BuildMaterials(ModelFile file, out int[] faceMaterial) {
            var materials = new List<Material>();
            var seen = new Dictionary<(int Texture, int Colour, int Alpha), int>();
            faceMaterial = new int[file.FaceCount];

            for (int i = 0; i < file.FaceCount; i++) {
                Appearance(file, i, out int texture, out int colour, out int alpha);
                var key = (texture, colour, alpha);
                if (!seen.TryGetValue(key, out int index)) {
                    index = materials.Count;
                    seen[key] = index;
                    materials.Add(new Material(Name(texture, colour, alpha), texture, colour, alpha));
                }
                faceMaterial[i] = index;
            }

            return materials;
        }

        /// <summary>
        ///     What a face looks like: its texture id, its colour word and its alpha.
        /// </summary>
        /// <remarks>
        ///     The legacy encoding needs unpacking first. There, a face's mask byte says whether it
        ///     is textured, and a textured face's colour word <em>is</em> the texture id, with the
        ///     drawn colour replaced by the neutral 127 (Model.java:1497-1505). Reading the raw
        ///     colour word on those faces would give a material coloured by a texture number.
        /// </remarks>
        private static void Appearance(ModelFile file, int face, out int texture, out int colour,
            out int alpha) {
            alpha = file.FaceAlphas != null ? file.FaceAlphas[face] : 0;

            if (file.Encoding == ModelEncoding.Legacy) {
                int mask = file.FaceTypeBytes != null ? file.FaceTypeBytes[face] : 0;
                if ((mask & 2) == 2) {
                    texture = file.FaceColours[face];
                    colour = 127;
                    return;
                }

                texture = -1;
                colour = file.FaceColours[face];
                return;
            }

            texture = file.FaceTextureIds != null ? file.FaceTextureIds[face] - 1 : -1;
            colour = file.FaceColours[face];
        }

        private static string Name(int texture, int colour, int alpha) {
            var name = new StringBuilder();
            if (texture >= 0)
                name.Append("tex").Append(Int(texture)).Append('_');
            name.Append("hsl").Append(Int(colour));
            if (alpha != 0)
                name.Append("_a").Append(Int(alpha));
            return name.ToString();
        }

        /// <summary>
        ///     Writes the material library, or null when the model has no faces at all.
        /// </summary>
        /// <remarks>
        ///     <c>d</c> is <c>(255 - alpha) / 255</c> because the stored byte is transparency rather
        ///     than opacity: the client packs it as <c>255 - alpha</c> into the ARGB word
        ///     (Renderable_Sub2.java:542), so a stored 0 is fully opaque.
        ///     <para>
        ///     A textured face gets its base colour as <c>Kd</c> and a comment naming the texture,
        ///     not a <c>map_Kd</c>. Index 9 holds procedural texture graphs rather than images, so
        ///     there is no file to point at, and a <c>map_Kd</c> naming one that does not exist
        ///     makes a modeller report a missing texture on every import.
        ///     </para>
        /// </remarks>
        private static string? BuildMaterialLibrary(ModelFile file, List<Material> materials) {
            if (materials.Count == 0)
                return null;

            var mtl = new StringBuilder();
            mtl.Append("# materials for model ").Append(Int(file.ModelId)).Append(NewLine);
            mtl.Append("# colours are the client's HSL words resolved through its own palette; ")
               .Append("they are informational and are not read back on import").Append(NewLine);

            foreach (Material material in materials) {
                int rgb = ModelDefinition.RawHslToRgb(material.Colour);
                mtl.Append(NewLine).Append("newmtl ").Append(material.Name).Append(NewLine);
                if (material.Texture >= 0)
                    mtl.Append("# index 9 texture ").Append(Int(material.Texture)).Append(NewLine);
                mtl.Append("Kd ").Append(Real(((rgb >> 16) & 0xFF) / 255f))
                   .Append(' ').Append(Real(((rgb >> 8) & 0xFF) / 255f))
                   .Append(' ').Append(Real((rgb & 0xFF) / 255f)).Append(NewLine);
                mtl.Append("d ").Append(Real((255 - material.Alpha) / 255f)).Append(NewLine);
                mtl.Append("illum 1").Append(NewLine);
            }

            return mtl.ToString();
        }

        // ===================================================================
        //  Helpers
        // ===================================================================

        private static float[][]? UvsFor(ModelFile file, ModelDefinition? projection,
            out float[][]? v) {
            v = null;
            if (projection == null || file.TexturedFaceCount == 0)
                return null;
            if (projection.FaceTextureUCoordinates == null || projection.FaceTextureVCoordinates == null)
                return null;
            if (projection.TriangleCount != file.FaceCount)
                return null;

            v = projection.FaceTextureVCoordinates;
            return projection.FaceTextureUCoordinates;
        }

        private static string Int(int value) => value.ToString(CultureInfo.InvariantCulture);

        /// <summary>
        ///     Formats a number for OBJ, turning anything not finite into zero.
        /// </summary>
        /// <remarks>
        ///     Only reachable through a texture coordinate, which is informational and is not read
        ///     back on import. A literal <c>NaN</c> in the file would make it unreadable to every
        ///     parser including this one, so a wrong UV on one corner is the smaller failure.
        /// </remarks>
        /// <param name="value">The number.</param>
        /// <returns>The text.</returns>
        private static string Real(float value) {
            if (float.IsNaN(value) || float.IsInfinity(value))
                return "0";
            return value.ToString("0.######", CultureInfo.InvariantCulture);
        }

        /// <summary>One distinct face appearance.</summary>
        private readonly struct Material {
            /// <summary>The <c>newmtl</c> name, unique across the three fields below.</summary>
            public string Name { get; }

            /// <summary>Index-9 texture id, or -1 when the face is untextured.</summary>
            public int Texture { get; }

            /// <summary>The stored HSL colour word.</summary>
            public int Colour { get; }

            /// <summary>The stored alpha byte, where 0 is opaque.</summary>
            public int Alpha { get; }

            /// <summary>Binds one appearance.</summary>
            /// <param name="name">The material name.</param>
            /// <param name="texture">Texture id, or -1.</param>
            /// <param name="colour">The stored HSL colour word.</param>
            /// <param name="alpha">The stored alpha byte.</param>
            public Material(string name, int texture, int colour, int alpha) {
                Name = name;
                Texture = texture;
                Colour = colour;
                Alpha = alpha;
            }
        }
    }
}
