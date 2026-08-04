using System;

namespace FlashEditor.Definitions.ModelInterchange {
    /// <summary>
    ///     The part of a model OBJ can express: absolute vertex coordinates and the three vertices
    ///     of each face.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///     Coordinates are in the <b>stored</b> space - the deltas accumulated, with no vertex
    ///     shift applied. <see cref="ModelDefinition"/> shifts by <see cref="ModelFile.VertexShift"/>
    ///     because that is what the client draws, but the shift is a scale by four that only
    ///     survives a round trip when every coordinate happens to be a multiple of four. Exporting
    ///     the stored numbers makes the transform exactly invertible over integers, which is what
    ///     lets an unedited export re-import to the same bytes. The cost is that a format-12 model
    ///     and a format-15 model come out at different scales relative to each other, so the
    ///     exporter states the shift in the file header.
    ///     </para>
    ///     <para>
    ///     This is the only thing an import is allowed to replace. Everything else in a
    ///     <see cref="ModelFile"/> - render types, priorities, alphas, skins, textured-triangle
    ///     types, particle attachments, bonds - has no OBJ representation at all.
    ///     </para>
    /// </remarks>
    public sealed class ModelGeometry {
        /// <summary>X coordinate of each vertex, in stored space.</summary>
        public int[] X { get; }

        /// <summary>Y coordinate of each vertex, in stored space. Negative is up.</summary>
        public int[] Y { get; }

        /// <summary>Z coordinate of each vertex, in stored space.</summary>
        public int[] Z { get; }

        /// <summary>First vertex index of each face.</summary>
        public int[] FaceA { get; }

        /// <summary>Second vertex index of each face.</summary>
        public int[] FaceB { get; }

        /// <summary>Third vertex index of each face.</summary>
        public int[] FaceC { get; }

        /// <summary>How many vertices the mesh holds.</summary>
        public int VertexCount => X.Length;

        /// <summary>How many faces the mesh holds.</summary>
        public int FaceCount => FaceA.Length;

        /// <summary>Binds already-built coordinate and index arrays.</summary>
        /// <param name="x">X coordinate per vertex.</param>
        /// <param name="y">Y coordinate per vertex.</param>
        /// <param name="z">Z coordinate per vertex.</param>
        /// <param name="faceA">First vertex index per face.</param>
        /// <param name="faceB">Second vertex index per face.</param>
        /// <param name="faceC">Third vertex index per face.</param>
        /// <exception cref="ArgumentException">The arrays do not agree on a count.</exception>
        public ModelGeometry(int[] x, int[] y, int[] z, int[] faceA, int[] faceB, int[] faceC) {
            X = x ?? throw new ArgumentNullException(nameof(x));
            Y = y ?? throw new ArgumentNullException(nameof(y));
            Z = z ?? throw new ArgumentNullException(nameof(z));
            FaceA = faceA ?? throw new ArgumentNullException(nameof(faceA));
            FaceB = faceB ?? throw new ArgumentNullException(nameof(faceB));
            FaceC = faceC ?? throw new ArgumentNullException(nameof(faceC));

            if (y.Length != x.Length || z.Length != x.Length)
                throw new ArgumentException("The three coordinate arrays must be the same length.");
            if (faceB.Length != faceA.Length || faceC.Length != faceA.Length)
                throw new ArgumentException("The three face-index arrays must be the same length.");
        }

        /// <summary>
        ///     Replays a stored model's delta and strip-opcode streams into absolute geometry.
        /// </summary>
        /// <remarks>
        ///     The same replay <see cref="ModelDefinition"/> performs, minus the vertex shift. It is
        ///     duplicated rather than reused because the projection applies the shift and clears
        ///     <c>VertSkins</c> as it goes, and an exporter that has to be exactly invertible cannot
        ///     start from a lossy view.
        /// </remarks>
        /// <param name="file">The model as stored.</param>
        /// <returns>The absolute geometry.</returns>
        public static ModelGeometry FromFile(ModelFile file) {
            if (file == null)
                throw new ArgumentNullException(nameof(file));

            int vertexCount = file.VertexCount;
            var x = new int[vertexCount];
            var y = new int[vertexCount];
            var z = new int[vertexCount];

            int cx = 0, cy = 0, cz = 0;
            int nextX = 0, nextY = 0, nextZ = 0;
            for (int i = 0; i < vertexCount; i++) {
                int mask = file.VertexFlags[i];
                if ((mask & 1) != 0) cx += file.VertexDeltasX[nextX++].Value;
                if ((mask & 2) != 0) cy += file.VertexDeltasY[nextY++].Value;
                if ((mask & 4) != 0) cz += file.VertexDeltasZ[nextZ++].Value;
                x[i] = cx;
                y[i] = cy;
                z[i] = cz;
            }

            int faceCount = file.FaceCount;
            var faceA = new int[faceCount];
            var faceB = new int[faceCount];
            var faceC = new int[faceCount];

            int opcodeMask = OpcodeMask(file.Encoding);
            int a = 0, b = 0, c = 0, offset = 0, next = 0;
            for (int i = 0; i < faceCount; i++) {
                int opcode = file.FaceOpcodes[i] & opcodeMask;

                if (opcode == 1) {
                    a = offset + file.FaceIndexDeltas[next++].Value;
                    b = a + file.FaceIndexDeltas[next++].Value;
                    c = b + file.FaceIndexDeltas[next++].Value;
                    offset = c;
                }
                else if (opcode == 2) {
                    b = c;
                    c = offset + file.FaceIndexDeltas[next++].Value;
                    offset = c;
                }
                else if (opcode == 3) {
                    a = c;
                    c = offset + file.FaceIndexDeltas[next++].Value;
                    offset = c;
                }
                else if (opcode == 4) {
                    int swap = a;
                    a = b;
                    b = swap;
                    c = offset + file.FaceIndexDeltas[next++].Value;
                    offset = c;
                }

                faceA[i] = a;
                faceB[i] = b;
                faceC[i] = c;
            }

            return new ModelGeometry(x, y, z, faceA, faceB, faceC);
        }

        /// <summary>
        ///     The bits of a strip opcode byte the decoder actually dispatches on.
        /// </summary>
        /// <remarks>
        ///     The new-protocol decoder masks to three bits (Model.java:1071) because the bits above
        ///     carry the per-face flag its trailing block would consume; the other two layouts use
        ///     the byte whole.
        /// </remarks>
        /// <param name="encoding">The layout the model uses.</param>
        /// <returns>The mask.</returns>
        public static int OpcodeMask(ModelEncoding encoding) =>
            encoding == ModelEncoding.NewProtocol ? 0x7 : 0xFF;

        /// <summary>
        ///     Whether another mesh has the same vertices and faces, in the same order.
        /// </summary>
        /// <remarks>
        ///     What decides whether an import has anything to write. An unedited round trip must
        ///     leave the stored bytes alone: re-deriving the deltas and strip opcodes from
        ///     unchanged geometry would still change the file, because any face can legally be
        ///     written as opcode 1 with three fresh deltas instead of 2, 3 or 4, and the shipped
        ///     encoder did not always take the shortest form.
        /// </remarks>
        /// <param name="other">The mesh to compare against.</param>
        /// <returns>True when the two are identical.</returns>
        public bool Matches(ModelGeometry other) {
            if (other == null)
                return false;
            if (other.VertexCount != VertexCount || other.FaceCount != FaceCount)
                return false;

            for (int i = 0; i < VertexCount; i++) {
                if (other.X[i] != X[i] || other.Y[i] != Y[i] || other.Z[i] != Z[i])
                    return false;
            }

            for (int i = 0; i < FaceCount; i++) {
                if (other.FaceA[i] != FaceA[i] || other.FaceB[i] != FaceB[i] || other.FaceC[i] != FaceC[i])
                    return false;
            }

            return true;
        }
    }
}
