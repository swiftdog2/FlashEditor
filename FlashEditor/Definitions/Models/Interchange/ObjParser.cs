using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;

namespace FlashEditor.Definitions.Models.Interchange {
    /// <summary>
    ///     Reads the subset of OBJ a mesh needs: positions, texture coordinates and faces.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///     Everything else in the format is skipped rather than rejected, because an exporter is
    ///     free to write smoothing groups, object names, curves and free-form surfaces beside the
    ///     mesh and none of them changes what a triangle is. What is <b>not</b> skipped is a line
    ///     this does understand and cannot make sense of - a face naming a vertex that does not
    ///     exist, a coordinate that is not a number - because that is a corrupt mesh rather than an
    ///     unsupported feature, and reading past it would put the wrong geometry into the cache.
    ///     </para>
    ///     <para>
    ///     Negative indices are relative to the end of the list at the point the face appears,
    ///     which is what the format specifies and what Blender writes when told to.
    ///     </para>
    /// </remarks>
    public static class ObjParser {
        /// <summary>Reads an OBJ from text.</summary>
        /// <param name="text">The whole file.</param>
        /// <returns>The mesh it describes.</returns>
        /// <exception cref="ModelImportException">A line this understands is malformed.</exception>
        public static ObjMesh Parse(string text) {
            if (text == null)
                throw new ArgumentNullException(nameof(text));

            using var reader = new StringReader(text);
            return Parse(reader);
        }

        /// <summary>Reads an OBJ from a file on disk.</summary>
        /// <param name="path">The file to read.</param>
        /// <returns>The mesh it describes.</returns>
        /// <exception cref="ModelImportException">A line this understands is malformed.</exception>
        public static ObjMesh ParseFile(string path) {
            if (path == null)
                throw new ArgumentNullException(nameof(path));

            using var reader = new StreamReader(path);
            return Parse(reader);
        }

        /// <summary>Reads an OBJ from a reader.</summary>
        /// <param name="reader">The source.</param>
        /// <returns>The mesh it describes.</returns>
        /// <exception cref="ModelImportException">A line this understands is malformed.</exception>
        public static ObjMesh Parse(TextReader reader) {
            if (reader == null)
                throw new ArgumentNullException(nameof(reader));

            var positions = new List<ObjVertex>();
            var texCoords = new List<ObjTexCoord>();
            var faces = new List<ObjFace>();
            var materialNames = new List<string>();
            int normals = 0;
            int polygons = 0;
            string? material = null;

            int lineNumber = 0;
            string? line;
            while ((line = reader.ReadLine()) != null) {
                lineNumber++;

                int comment = line.IndexOf('#');
                if (comment >= 0)
                    line = line.Substring(0, comment);

                string[] parts = line.Split(Whitespace, StringSplitOptions.RemoveEmptyEntries);
                if (parts.Length == 0)
                    continue;

                switch (parts[0]) {
                    case "v":
                        Require(parts.Length >= 4, lineNumber,
                            "a v line needs three coordinates, and this one has " + (parts.Length - 1));
                        positions.Add(new ObjVertex(
                            Number(parts[1], lineNumber), Number(parts[2], lineNumber),
                            Number(parts[3], lineNumber)));
                        break;

                    case "vt":
                        Require(parts.Length >= 2, lineNumber, "a vt line needs at least a U");
                        texCoords.Add(new ObjTexCoord(Number(parts[1], lineNumber),
                            parts.Length >= 3 ? Number(parts[2], lineNumber) : 0.0));
                        break;

                    case "vn":
                        normals++;
                        break;

                    case "usemtl":
                        material = parts.Length >= 2 ? parts[1] : null;
                        if (material != null && !materialNames.Contains(material))
                            materialNames.Add(material);
                        break;

                    case "f":
                        polygons += ReadFace(parts, lineNumber, positions.Count, texCoords.Count,
                            material, faces);
                        break;
                }
            }

            return new ObjMesh(positions, texCoords, faces, normals, polygons, materialNames);
        }

        /// <summary>
        ///     Reads one <c>f</c> line, fanning anything with more than three corners.
        /// </summary>
        /// <remarks>
        ///     A fan is the only triangulation available without knowing whether the polygon is
        ///     convex, and it changes the face count - which is the one thing an import refuses to
        ///     do - so the count of fanned polygons travels back to the user rather than being
        ///     swallowed.
        /// </remarks>
        /// <param name="parts">The whitespace-split line.</param>
        /// <param name="lineNumber">Where it is, for failure messages.</param>
        /// <param name="vertexCount">Positions seen so far, for resolving negative indices.</param>
        /// <param name="texCoordCount">Texture coordinates seen so far, for the same.</param>
        /// <param name="material">The material in force.</param>
        /// <param name="into">Where the triangles go.</param>
        /// <returns>1 when the face was a polygon that had to be fanned, otherwise 0.</returns>
        private static int ReadFace(string[] parts, int lineNumber, int vertexCount,
            int texCoordCount, string? material, List<ObjFace> into) {
            int corners = parts.Length - 1;
            Require(corners >= 3, lineNumber,
                "a face needs at least three corners, and this one has " + corners);

            var vertices = new int[corners];
            var uvs = new int[corners];
            for (int i = 0; i < corners; i++) {
                string[] fields = parts[i + 1].Split('/');
                vertices[i] = Resolve(fields[0], vertexCount, lineNumber, "vertex");
                uvs[i] = fields.Length >= 2 && fields[1].Length > 0
                    ? Resolve(fields[1], texCoordCount, lineNumber, "texture coordinate")
                    : -1;
            }

            for (int i = 1; i + 1 < corners; i++) {
                into.Add(new ObjFace(vertices[0], vertices[i], vertices[i + 1],
                    uvs[0], uvs[i], uvs[i + 1], material));
            }

            return corners > 3 ? 1 : 0;
        }

        /// <summary>Turns an OBJ index into a zero-based one, resolving the relative form.</summary>
        /// <param name="field">The index as written.</param>
        /// <param name="count">How many of that element have been seen so far.</param>
        /// <param name="lineNumber">Where it is, for failure messages.</param>
        /// <param name="what">Which list it indexes, for failure messages.</param>
        /// <returns>A zero-based index.</returns>
        private static int Resolve(string field, int count, int lineNumber, string what) {
            if (!int.TryParse(field, NumberStyles.Integer, CultureInfo.InvariantCulture, out int index)) {
                throw new ModelImportException(
                    $"Line {lineNumber} of the OBJ names {what} \"{field}\", which is not a whole number.");
            }

            if (index > 0)
                index -= 1;
            else if (index < 0)
                index += count;
            else {
                throw new ModelImportException(
                    $"Line {lineNumber} of the OBJ uses {what} index 0, and OBJ indices start at 1.");
            }

            if (index < 0 || index >= count) {
                throw new ModelImportException(
                    $"Line {lineNumber} of the OBJ names {what} {field}, but only {count} had been " +
                    "declared by that point in the file.");
            }

            return index;
        }

        private static double Number(string field, int lineNumber) {
            if (double.TryParse(field, NumberStyles.Float, CultureInfo.InvariantCulture, out double value)
                && !double.IsNaN(value) && !double.IsInfinity(value))
                return value;

            throw new ModelImportException(
                $"Line {lineNumber} of the OBJ has \"{field}\" where a number belongs.");
        }

        private static void Require(bool condition, int lineNumber, string complaint) {
            if (!condition)
                throw new ModelImportException($"Line {lineNumber} of the OBJ: {complaint}.");
        }

        private static readonly char[] Whitespace = { ' ', '\t', '\r' };
    }
}
