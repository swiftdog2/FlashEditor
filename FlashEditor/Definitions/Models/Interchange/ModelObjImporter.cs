using System;
using System.Collections.Generic;
using System.Globalization;

namespace FlashEditor.Definitions.Models.Interchange {
    /// <summary>
    ///     Writes an OBJ's vertices and faces back over a model, and nothing else.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///     <b>Geometry replacement, not file replacement.</b> OBJ can express a position and a
    ///     triangle. It has no field for a face's render type, its priority, its alpha, its skin
    ///     group, its texture id or texture-coordinate index, none for a textured triangle's type
    ///     or projection scalars, and none for a particle emitter, an effector or a billboard bond.
    ///     A naive round trip through OBJ would drop all of it silently, which is worse than having
    ///     no import at all - so every one of those arrays comes back from the model already in the
    ///     cache, by reference, and the import refuses outright when the mesh would leave one of
    ///     them addressed by an index that moved.
    ///     </para>
    ///     <para>
    ///     The rules, stated rather than inferred:
    ///     </para>
    ///     <list type="bullet">
    ///     <item><b>The face count must match.</b> Always, with no exception. Colours are per face
    ///     and are not optional, so there is no model whose per-face arrays survive a face-count
    ///     change, and there is no way to know which new face inherits which old attributes.</item>
    ///     <item><b>The vertex count may change,</b> but only when nothing else addresses a vertex:
    ///     no per-vertex skin groups, no textured triangles - each names three reference vertices -
    ///     and no particle effectors, which anchor to a vertex where an emitter anchors to a face.
    ///     Otherwise it is refused for the same reason.</item>
    ///     <item><b>Face order is meaning.</b> Per-face arrays are matched by position, so
    ///     reordering faces in a modeller reassigns colours and render types without anything being
    ///     able to detect it. The exporter says so in the file header.</item>
    ///     <item><b>Unchanged geometry writes nothing.</b> A rebuild would normalise the strip
    ///     opcodes and smart widths, and the format has more than one legal encoding of the same
    ///     mesh, so an untouched export must come back as the very same
    ///     <see cref="ModelFile"/>.</item>
    ///     </list>
    /// </remarks>
    public static class ModelObjImporter {
        /// <summary>
        ///     How far from the origin a vertex may sit before the import gives up on it.
        /// </summary>
        /// <remarks>
        ///     Far beyond anything a model holds, and small enough that negating a coordinate cannot
        ///     overflow. The real limit is tighter and is enforced where it belongs: consecutive
        ///     vertices are stored as signed smarts, so no two may be more than 16,383 apart.
        /// </remarks>
        private const double CoordinateLimit = 1 << 30;

        /// <summary>Reads an OBJ from disk and writes its mesh over a model.</summary>
        /// <param name="original">The model as stored. Never modified.</param>
        /// <param name="path">The OBJ to read.</param>
        /// <returns>What happened, and the model to save when it succeeded.</returns>
        public static ModelImportResult ImportFile(ModelFile original, string path) {
            if (original == null)
                throw new ArgumentNullException(nameof(original));
            if (path == null)
                throw new ArgumentNullException(nameof(path));

            try {
                return Import(original, ObjParser.ParseFile(path));
            }
            catch (ModelImportException failure) {
                return Refused(failure.Message);
            }
        }

        /// <summary>Reads an OBJ from text and writes its mesh over a model.</summary>
        /// <param name="original">The model as stored. Never modified.</param>
        /// <param name="objText">The OBJ text.</param>
        /// <returns>What happened, and the model to save when it succeeded.</returns>
        public static ModelImportResult Import(ModelFile original, string objText) {
            if (original == null)
                throw new ArgumentNullException(nameof(original));
            if (objText == null)
                throw new ArgumentNullException(nameof(objText));

            try {
                return Import(original, ObjParser.Parse(objText));
            }
            catch (ModelImportException failure) {
                return Refused(failure.Message);
            }
        }

        /// <summary>Writes a parsed mesh over a model.</summary>
        /// <param name="original">The model as stored. Never modified.</param>
        /// <param name="mesh">The mesh to write in.</param>
        /// <returns>What happened, and the model to save when it succeeded.</returns>
        public static ModelImportResult Import(ModelFile original, ObjMesh mesh) {
            if (original == null)
                throw new ArgumentNullException(nameof(original));
            if (mesh == null)
                throw new ArgumentNullException(nameof(mesh));

            try {
                ModelGeometry incoming = ToGeometry(mesh, out double snapped);
                ModelGeometry stored = ModelGeometry.FromFile(original);

                if (incoming.Matches(stored))
                    return Unchanged(original, mesh, snapped);

                ModelFile rebuilt = ModelGeometryEncoder.Rebuild(original, incoming);
                return Changed(original, rebuilt, incoming, stored, mesh, snapped);
            }
            catch (ModelImportException failure) {
                return Refused(failure.Message);
            }
        }

        // ===================================================================
        //  Reading the mesh
        // ===================================================================

        /// <summary>
        ///     Turns an OBJ mesh into stored-space integer geometry.
        /// </summary>
        /// <remarks>
        ///     The inverse of the exporter's two fixed conventions: negate Y and Z back, and take
        ///     the coordinates as they stand with no vertex shift. Both are exact over the integers
        ///     an export writes, so an untouched file comes back to the numbers it left as.
        ///     <para>
        ///     A coordinate that is not whole is rounded rather than refused - a modeller has no
        ///     reason to keep a vertex on an integer grid - and the largest distance anything moved
        ///     is reported, so a user who did not expect any snapping can see that some happened.
        ///     </para>
        ///     <para>
        ///     An empty file is not rejected here. It is either an empty model, in which case it
        ///     matches and nothing is written, or it is the wrong file, in which case the count
        ///     refusal below says which counts disagreed - which is more use than "no faces".
        ///     </para>
        /// </remarks>
        /// <param name="mesh">The parsed OBJ.</param>
        /// <param name="snapped">The largest distance any coordinate moved to reach an integer.</param>
        /// <returns>The mesh in the model's own space.</returns>
        private static ModelGeometry ToGeometry(ObjMesh mesh, out double snapped) {
            int count = mesh.Positions.Count;
            var x = new int[count];
            var y = new int[count];
            var z = new int[count];
            snapped = 0.0;

            for (int i = 0; i < count; i++) {
                ObjVertex position = mesh.Positions[i];
                x[i] = Whole(position.X, i, "X", ref snapped);
                y[i] = -Whole(position.Y, i, "Y", ref snapped);
                z[i] = -Whole(position.Z, i, "Z", ref snapped);
            }

            int faces = mesh.Faces.Count;
            var faceA = new int[faces];
            var faceB = new int[faces];
            var faceC = new int[faces];
            for (int i = 0; i < faces; i++) {
                ObjFace face = mesh.Faces[i];
                faceA[i] = face.A;
                faceB[i] = face.B;
                faceC[i] = face.C;
            }

            return new ModelGeometry(x, y, z, faceA, faceB, faceC);
        }

        private static int Whole(double value, int vertex, string axis, ref double snapped) {
            //Bounded well inside int so that negating Y and Z afterwards cannot overflow.
            double rounded = Math.Round(value, MidpointRounding.AwayFromZero);
            if (rounded < -CoordinateLimit || rounded > CoordinateLimit) {
                throw new ModelImportException(
                    $"Vertex {vertex} has {axis} = " + value.ToString("R", CultureInfo.InvariantCulture) +
                    ", which is far outside anything a model can store.");
            }

            double moved = Math.Abs(value - rounded);
            if (moved > snapped)
                snapped = moved;
            return (int) rounded;
        }

        // ===================================================================
        //  Reporting
        // ===================================================================

        private static ModelImportResult Unchanged(ModelFile original, ObjMesh mesh, double snapped) {
            var entries = new List<ModelImportEntry> {
                new ModelImportEntry("geometry", ModelImportDisposition.Preserved,
                    $"{original.VertexCount} vertices and {original.FaceCount} faces, identical to " +
                    "the model's own, so the stored bytes are left exactly as they were")
            };
            AddPreserved(entries, original);
            AddIgnored(entries, mesh, snapped);

            return new ModelImportResult(true, false, original,
                $"Model {original.ModelId} is unchanged: the OBJ holds the same " +
                $"{original.VertexCount} vertices and {original.FaceCount} faces, so nothing was " +
                "rewritten.", entries);
        }

        private static ModelImportResult Changed(ModelFile original, ModelFile rebuilt,
            ModelGeometry incoming, ModelGeometry stored, ObjMesh mesh, double snapped) {
            var entries = new List<ModelImportEntry> {
                new ModelImportEntry("vertex coordinates", ModelImportDisposition.Replaced,
                    Counts(stored.VertexCount, incoming.VertexCount) + ", taken from the OBJ"),
                new ModelImportEntry("face vertex indices", ModelImportDisposition.Replaced,
                    $"{incoming.FaceCount} faces, taken from the OBJ. Per-face attributes below are " +
                    "matched by position"),
                new ModelImportEntry("vertex flag masks and delta blocks", ModelImportDisposition.Replaced,
                    $"X {original.VertexXLength} to {rebuilt.VertexXLength} bytes, " +
                    $"Y {original.VertexYLength} to {rebuilt.VertexYLength}, " +
                    $"Z {original.VertexZLength} to {rebuilt.VertexZLength}; any unread remainder " +
                    "of the old blocks is dropped"),
                new ModelImportEntry("strip opcodes and face index block", ModelImportDisposition.Replaced,
                    $"{original.FaceIndexLength} to {rebuilt.FaceIndexLength} bytes; each face uses " +
                    "the shortest opcode that reaches it from the one before")
            };

            AddPreserved(entries, original);
            AddIgnored(entries, mesh, snapped);

            int preserved = 0;
            foreach (ModelImportEntry entry in entries) {
                if (entry.Disposition == ModelImportDisposition.Preserved)
                    preserved++;
            }

            return new ModelImportResult(true, true, rebuilt,
                $"Model {original.ModelId}: replaced " + Counts(stored.VertexCount, incoming.VertexCount) +
                $" and {incoming.FaceCount} faces, and preserved {preserved} arrays the OBJ cannot " +
                "express.", entries);
        }

        private static string Counts(int before, int after) =>
            before == after ? $"{after} vertices" : $"{before} vertices becoming {after}";

        /// <summary>
        ///     Lists every part of the model the OBJ could not have carried, present or not.
        /// </summary>
        /// <remarks>
        ///     An absent array is listed rather than skipped. "This model has no per-face alphas" is
        ///     as much a thing the user needs to read as "its 918 alphas were kept", and a grid that
        ///     shows only what exists gives no way to tell the two apart.
        /// </remarks>
        private static void AddPreserved(List<ModelImportEntry> entries, ModelFile original) {
            Preserve(entries, "face colours", original.FaceColours.Length,
                "HSL words, one per face");
            Preserve(entries, original.Encoding == ModelEncoding.Legacy
                    ? "face render/texture mask bytes"
                    : "face render types",
                original.FaceTypeBytes?.Length,
                original.Encoding == ModelEncoding.Legacy
                    ? "the packed byte holding the render type and the texture-coordinate index"
                    : "0 Gouraud, 1 flat, 2 not drawn at all");
            Preserve(entries, "face priorities", original.FacePriorities?.Length,
                original.FacePriorities == null
                    ? "this model uses the global priority " + original.PriorityFlag
                    : "render priority, one per face");
            Preserve(entries, "face alphas", original.FaceAlphas?.Length, "transparency, one per face");
            Preserve(entries, "face skin groups", original.FaceSkins?.Length,
                "animation groups, one per face");
            Preserve(entries, "vertex skin groups", original.VertexSkins?.Length,
                "animation groups, one per vertex, which is why a vertex-count change is refused " +
                "when they are present");
            Preserve(entries, "face texture ids", original.FaceTextureIds?.Length,
                "index-9 texture per face");
            Preserve(entries, "texture coordinate indices", original.TextureCoords.Length,
                "which textured triangle each textured face maps through");
            Preserve(entries, "textured triangles", original.TexturedFaceCount == 0
                    ? (int?) null
                    : original.TexturedFaceCount,
                "types, three reference vertices each, projection scalars and layer bytes");
            Preserve(entries, "particle emitters", original.Emitters?.Length,
                "each anchored to a face");
            Preserve(entries, "particle effectors", original.Effectors?.Length,
                "each anchored to a vertex");
            Preserve(entries, "billboard bonds", original.Bonds?.Length, "each anchored to a face");
            Preserve(entries, "footer flags and format type", 1,
                $"flags 0x{original.Flags:X2}, format type {original.FormatType}, " +
                $"{original.Encoding} encoding");
        }

        private static void Preserve(List<ModelImportEntry> entries, string field, int? count,
            string detail) {
            string text = count == null || count.Value == 0
                ? "absent from this model"
                : count.Value.ToString(CultureInfo.InvariantCulture) + ": " + detail;
            entries.Add(new ModelImportEntry(field, ModelImportDisposition.Preserved, text));
        }

        private static void AddIgnored(List<ModelImportEntry> entries, ObjMesh mesh, double snapped) {
            entries.Add(new ModelImportEntry("OBJ texture coordinates", ModelImportDisposition.Ignored,
                mesh.TexCoords.Count + " vt entries. This format maps a textured face through a " +
                "reference triangle and projection scalars, which a per-corner UV cannot state"));
            entries.Add(new ModelImportEntry("OBJ normals", ModelImportDisposition.Ignored,
                mesh.NormalCount + " vn entries. Shading follows the per-face render type, which " +
                "the model stores and OBJ has no field for"));
            entries.Add(new ModelImportEntry("OBJ materials", ModelImportDisposition.Ignored,
                mesh.MaterialNames.Count + " named materials. Colours, alphas and texture ids come " +
                "from the model"));

            if (mesh.TriangulatedPolygons > 0) {
                entries.Add(new ModelImportEntry("OBJ polygons", ModelImportDisposition.Ignored,
                    mesh.TriangulatedPolygons + " faces had more than three corners and were fanned " +
                    "into triangles, which changes the face count"));
            }

            if (snapped > 0.0) {
                entries.Add(new ModelImportEntry("coordinate rounding", ModelImportDisposition.Replaced,
                    "the furthest a vertex moved to reach a whole number was " +
                    snapped.ToString("0.######", CultureInfo.InvariantCulture) +
                    "; model coordinates are integers"));
            }
        }

        private static ModelImportResult Refused(string message) {
            var entries = new List<ModelImportEntry> {
                new ModelImportEntry("import", ModelImportDisposition.Refused, message)
            };
            return new ModelImportResult(false, false, null, message, entries);
        }
    }
}
