using System;
using System.Collections.Generic;

namespace FlashEditor.Definitions.ModelInterchange {
    /// <summary>
    ///     One <c>v</c> line: a position in the OBJ's own coordinate space.
    /// </summary>
    /// <remarks>
    ///     Held as doubles because that is what the file says. Nothing here knows about the model
    ///     format; <see cref="ModelObjImporter"/> is what turns these into the integer stored space
    ///     and reports how far anything had to move to get there.
    /// </remarks>
    public readonly struct ObjVertex {
        /// <summary>X as written.</summary>
        public double X { get; }

        /// <summary>Y as written.</summary>
        public double Y { get; }

        /// <summary>Z as written.</summary>
        public double Z { get; }

        /// <summary>Binds a position.</summary>
        /// <param name="x">X as written.</param>
        /// <param name="y">Y as written.</param>
        /// <param name="z">Z as written.</param>
        public ObjVertex(double x, double y, double z) {
            X = x;
            Y = y;
            Z = z;
        }
    }

    /// <summary>One <c>vt</c> line.</summary>
    public readonly struct ObjTexCoord {
        /// <summary>U as written.</summary>
        public double U { get; }

        /// <summary>V as written, or zero when the line carried only a U.</summary>
        public double V { get; }

        /// <summary>Binds a texture coordinate.</summary>
        /// <param name="u">U as written.</param>
        /// <param name="v">V as written.</param>
        public ObjTexCoord(double u, double v) {
            U = u;
            V = v;
        }
    }

    /// <summary>
    ///     One triangle, with zero-based indices into <see cref="ObjMesh.Positions"/> and
    ///     <see cref="ObjMesh.TexCoords"/>.
    /// </summary>
    /// <remarks>
    ///     A polygon of more than three corners becomes several of these by fan triangulation, so a
    ///     face here is not necessarily a face in the file.
    /// </remarks>
    public readonly struct ObjFace {
        /// <summary>First vertex.</summary>
        public int A { get; }

        /// <summary>Second vertex.</summary>
        public int B { get; }

        /// <summary>Third vertex.</summary>
        public int C { get; }

        /// <summary>First texture coordinate, or -1 when the corner named none.</summary>
        public int TexA { get; }

        /// <summary>Second texture coordinate, or -1.</summary>
        public int TexB { get; }

        /// <summary>Third texture coordinate, or -1.</summary>
        public int TexC { get; }

        /// <summary>The material in force at this face, or null.</summary>
        public string? Material { get; }

        /// <summary>Binds a triangle.</summary>
        /// <param name="a">First vertex.</param>
        /// <param name="b">Second vertex.</param>
        /// <param name="c">Third vertex.</param>
        /// <param name="texA">First texture coordinate, or -1.</param>
        /// <param name="texB">Second texture coordinate, or -1.</param>
        /// <param name="texC">Third texture coordinate, or -1.</param>
        /// <param name="material">The material in force, or null.</param>
        public ObjFace(int a, int b, int c, int texA, int texB, int texC, string? material) {
            A = a;
            B = b;
            C = c;
            TexA = texA;
            TexB = texB;
            TexC = texC;
            Material = material;
        }
    }

    /// <summary>
    ///     An OBJ file's contents, in the file's own terms.
    /// </summary>
    /// <remarks>
    ///     Deliberately free of model semantics: no axis flip, no rounding, no vertex shift. That
    ///     keeps <see cref="ObjParser"/> testable against OBJ syntax alone and leaves every
    ///     interpretation in <see cref="ModelObjImporter"/>, where it can be reported to the user.
    /// </remarks>
    public sealed class ObjMesh {
        /// <summary>Every <c>v</c> line, in file order.</summary>
        public IReadOnlyList<ObjVertex> Positions { get; }

        /// <summary>Every <c>vt</c> line, in file order.</summary>
        public IReadOnlyList<ObjTexCoord> TexCoords { get; }

        /// <summary>Every triangle, in file order, with polygons already fanned.</summary>
        public IReadOnlyList<ObjFace> Faces { get; }

        /// <summary>How many <c>vn</c> lines the file carried.</summary>
        /// <remarks>
        ///     Counted rather than kept. A model's shading comes from its per-face render type,
        ///     which the format stores and OBJ has no field for, so an imported normal would have
        ///     nowhere to go.
        /// </remarks>
        public int NormalCount { get; }

        /// <summary>How many faces in the file had more than three corners.</summary>
        /// <remarks>Reported to the user, because a fan is a choice and it changes the face count.</remarks>
        public int TriangulatedPolygons { get; }

        /// <summary>Distinct <c>usemtl</c> names, in first-seen order.</summary>
        public IReadOnlyList<string> MaterialNames { get; }

        /// <summary>Binds a parsed file.</summary>
        /// <param name="positions">Every <c>v</c> line.</param>
        /// <param name="texCoords">Every <c>vt</c> line.</param>
        /// <param name="faces">Every triangle.</param>
        /// <param name="normalCount">How many <c>vn</c> lines were seen.</param>
        /// <param name="triangulatedPolygons">How many faces had more than three corners.</param>
        /// <param name="materialNames">Distinct material names, in first-seen order.</param>
        public ObjMesh(IReadOnlyList<ObjVertex> positions, IReadOnlyList<ObjTexCoord> texCoords,
            IReadOnlyList<ObjFace> faces, int normalCount, int triangulatedPolygons,
            IReadOnlyList<string> materialNames) {
            Positions = positions ?? throw new ArgumentNullException(nameof(positions));
            TexCoords = texCoords ?? throw new ArgumentNullException(nameof(texCoords));
            Faces = faces ?? throw new ArgumentNullException(nameof(faces));
            NormalCount = normalCount;
            TriangulatedPolygons = triangulatedPolygons;
            MaterialNames = materialNames ?? throw new ArgumentNullException(nameof(materialNames));
        }
    }
}
