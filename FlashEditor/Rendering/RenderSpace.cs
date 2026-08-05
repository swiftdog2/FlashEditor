using System.Numerics;
using FlashEditor.Definitions;

namespace FlashEditor.Rendering
{
    /// <summary>
    ///     The one conversion from the cache's integer model coordinates into the viewport's world
    ///     coordinates.
    /// </summary>
    /// <remarks>
    ///     This type exists because the conversion has three independent consumers - the mesh uploader
    ///     (<c>ModelRenderer.cs:30</c>, which takes its scale straight off
    ///     <see cref="ModelUnitsPerWorldUnit"/>), the ray picker (<see cref="PickMesh"/>) and the
    ///     particle billboards (<see cref="ParticleBillboards"/>) - and a disagreement between any two
    ///     of them is invisible. Nothing in the suite can see the GL surface, so a picker on a
    ///     different scale from the uploader still returns a face and still highlights one; just not
    ///     the face under the cursor. Someone then attaches a particle emitter to it.
    ///     <para>
    ///     So the divisor is a named constant rather than a literal, and the tests assert the constant
    ///     itself (<c>PickMeshTests.RenderSpace_DividesAndFlipsYAndZ</c>). Spelling it as a bare
    ///     <c>128f</c> at a call site puts that site outside the agreement without any build error to
    ///     say so. One path still does: <c>Editor.cs:537-539</c> writes the arithmetic out by hand, so
    ///     it does not follow a change made here.
    ///     </para>
    /// </remarks>
    public static class RenderSpace
    {
        /// <summary>
        ///     Model units per world unit. Every conversion between the two spaces goes through it.
        /// </summary>
        /// <remarks>
        ///     A scale, not a unit of measurement - what matters is that every consumer uses the same
        ///     one, not what a world unit corresponds to in the game.
        /// </remarks>
        public const float ModelUnitsPerWorldUnit = 128f;

        /// <summary>
        ///     Converts a model-space coordinate triple into world space.
        /// </summary>
        /// <remarks>
        ///     Y and Z are negated. Model space measures y downwards and z away from the viewer, which
        ///     is what the client's software rasteriser wants; the viewport is a right-handed GL camera
        ///     with y up and -z forwards. Dropping either negation produces a model that is upside down
        ///     or inside out, and dropping <b>both</b> produces one that looks plausible from the front
        ///     and is mirrored - which is the case worth guarding, because it survives a glance.
        /// </remarks>
        /// <param name="x">Model-space x.</param>
        /// <param name="y">Model-space y, measured downwards.</param>
        /// <param name="z">Model-space z, measured away from the viewer.</param>
        /// <returns>The world-space position.</returns>
        public static Vector3 ToWorld(int x, int y, int z)
        {
            return new Vector3(
                x / ModelUnitsPerWorldUnit,
                -y / ModelUnitsPerWorldUnit,
                -z / ModelUnitsPerWorldUnit);
        }

        /// <summary>
        ///     Converts an already-fractional model-space coordinate triple into world space.
        /// </summary>
        /// <remarks>
        ///     Same conversion as the integer overload. It exists for callers that have divided a
        ///     fixed-point value down and have a fraction left over - a particle position is stored in
        ///     twelfths of a model unit - so they do not have to truncate before converting.
        /// </remarks>
        /// <param name="x">Model-space x.</param>
        /// <param name="y">Model-space y, measured downwards.</param>
        /// <param name="z">Model-space z, measured away from the viewer.</param>
        /// <returns>The world-space position.</returns>
        public static Vector3 ToWorld(float x, float y, float z)
        {
            return new Vector3(
                x / ModelUnitsPerWorldUnit,
                (0f - y) / ModelUnitsPerWorldUnit,
                (0f - z) / ModelUnitsPerWorldUnit);
        }

        /// <summary>Converts one of a model's rest vertices into world space.</summary>
        /// <param name="model">The model definition.</param>
        /// <param name="vertex">Index into the model's vertex arrays.</param>
        /// <returns>The world-space position of that vertex at rest.</returns>
        public static Vector3 ToWorld(ModelDefinition model, int vertex)
        {
            return ToWorld(model.VertX[vertex], model.VertY[vertex], model.VertZ[vertex]);
        }

        /// <summary>Converts one of a posed mesh's vertices into world space.</summary>
        /// <remarks>
        ///     The overload that keeps the picker honest during an animation. A posed vertex and a rest
        ///     vertex must reach world space by the same arithmetic, or the cursor picks the rest mesh
        ///     while the screen shows the posed one - an error that grows with the animation and looks
        ///     correct whenever the model is standing still.
        /// </remarks>
        /// <param name="mesh">The posed mesh.</param>
        /// <param name="vertex">Index into the pose's vertex arrays.</param>
        /// <returns>The world-space position of that vertex in the current pose.</returns>
        public static Vector3 ToWorld(PosedMesh mesh, int vertex)
        {
            return ToWorld(mesh.VertexX[vertex], mesh.VertexY[vertex], mesh.VertexZ[vertex]);
        }
    }
}
