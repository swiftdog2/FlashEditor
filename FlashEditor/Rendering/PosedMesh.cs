using System.Collections.Generic;
using System;
using FlashEditor.Definitions;

namespace FlashEditor.Rendering
{
    /// <summary>What a transform did, so the animator can tell three different failures apart.</summary>
    /// <remarks>
    ///     None of these is an error. They are what the diagnostics panel needs to answer the one
    ///     question nobody can answer by looking at a viewport nothing can capture: an animation that
    ///     visibly does nothing may be doing nothing, or may be reaching a model it cannot move, or may
    ///     be asking for something this viewer does not simulate.
    /// </remarks>
    public enum TransformOutcome
    {
        /// <summary>The transform reached at least one vertex or face of this mesh.</summary>
        Applied,

        /// <summary>
        ///     A known transform whose labels own nothing on this mesh.
        /// </summary>
        /// <remarks>
        ///     The normal case for a model posed against a skeleton built for a different one, which
        ///     is most of what a model viewer shows.
        /// </remarks>
        NoTargets,

        /// <summary>
        ///     A transform type this viewer does not implement.
        /// </summary>
        /// <remarks>
        ///     Types 8, 9 and 10 walk the model's <i>spawned particle instances</i>
        ///     (<c>Renderable_Sub2.java:3129-3178</c>), which the viewer does not keep a live list of.
        ///     Reported rather than ignored so a panel can say why an effect-heavy animation looks
        ///     inert.
        /// </remarks>
        Unsupported
    }

    /// <summary>
    ///     One model's mutable pose: the vertex positions and per-face colour a frame has produced.
    /// </summary>
    /// <remarks>
    ///     <b>Why this is on the CPU rather than a GPU skinning shader.</b> Two reasons, and the
    ///     second is the one that settles it.
    ///     <para>
    ///     The transforms are integer and lossy in a specific way. Coordinates are promoted to
    ///     sixteenths, rotated with a truncating fixed-point matrix, then reduced with
    ///     <c>+7 &gt;&gt; 4</c> (<c>Renderable_Sub2.java:4792-4809</c> and <c>:5429-5437</c>). A float
    ///     skinning shader would produce a smoother and slightly different pose, which is exactly what
    ///     a tool for inspecting this cache must not do - the question it answers is what the client
    ///     will draw.
    ///     </para>
    ///     <para>
    ///     And the posed coordinates have three consumers besides the screen. <see cref="PickMesh"/>
    ///     needs them to put the cursor on the posed model rather than the rest one;
    ///     <see cref="ParticleSystem"/> needs them so an emitter attached to a face follows that face
    ///     instead of spraying from where the model used to be; <see cref="PosedNormals"/> needs them
    ///     to relight. A pose that only exists in a vertex shader is invisible to all three, and the
    ///     failures that produces are silent: the picker still returns a face and the emitter still
    ///     emits.
    ///     </para>
    ///     <para>
    ///     A pose is absolute against the rest mesh, never a delta on the previous frame, so
    ///     <see cref="Reset"/> is a copy and repeated posing cannot drift.
    ///     </para>
    /// </remarks>
    public sealed class PosedMesh
    {
        /// <summary>Transform type 0: set the pivot the rotate and scale arms turn about.</summary>
        public const int TypePivot = 0;

        /// <summary>Transform type 1: translate the labelled vertices.</summary>
        public const int TypeTranslate = 1;

        /// <summary>Transform type 2: rotate the labelled vertices about the pivot.</summary>
        /// <remarks>
        ///     Type 6 never reaches here: the skeleton decoder rewrites a stored 6 to a 2
        ///     (<c>Node_Sub1.java:96-97</c>) before any frame is resolved against it.
        /// </remarks>
        public const int TypeRotate = 2;

        /// <summary>Transform type 3: scale the labelled vertices about the pivot.</summary>
        public const int TypeScale = 3;

        /// <summary>Transform type 5: shift the labelled <b>faces</b>' alpha.</summary>
        public const int TypeAlpha = 5;

        /// <summary>Transform type 7: shift the labelled <b>faces</b>' HSL colour.</summary>
        public const int TypeColour = 7;

        /// <summary>Bits the pivot, translate and rotate arms work in below a model unit.</summary>
        /// <remarks>
        ///     Sixteenths. Rotation is the reason: a fixed-point rotation of a small limb rounds to
        ///     nothing at whole-unit resolution, and every joint in the model would visibly snap.
        /// </remarks>
        private const int SubUnitBits = 4;

        /// <summary>Added before the reduction back to model units, so the truncation rounds.</summary>
        /// <remarks>
        ///     Seven, from <c>Renderable_Sub2.java:5433-5436</c>, which is one short of half a model
        ///     unit in sixteenths - so it rounds to nearest with ties falling downwards, and is
        ///     <b>not</b> the same shape as <see cref="SkeletalTrig.ShiftBias"/>, which is one short of
        ///     a whole unit. Do not derive either from the other.
        ///     <para>
        ///     Being under a whole sixteenth is what makes a promote followed immediately by a reduce
        ///     the identity - <c>(v &lt;&lt; 4) + 7 &gt;&gt; 4 == v</c> - so a frame that reaches no
        ///     vertex leaves the mesh exactly where it started rather than creeping.
        ///     </para>
        /// </remarks>
        private const int SubUnitBias = 7;

        /// <summary>Fractional bits in a type-3 scale factor, so 128 means unchanged.</summary>
        private const int ScaleBits = 7;

        /// <summary>Alpha units per unit of a type-5 transform's x value.</summary>
        /// <remarks><c>Renderable_Sub2.java:3046</c>. The stored value is a coarse step, not a byte.</remarks>
        private const int AlphaStep = 8;

        /// <summary>Largest face alpha, since it is stored in a byte.</summary>
        private const int MaxAlpha = 255;

        /// <summary>Bit position of hue in a packed HSL face colour.</summary>
        private const int HueShift = 10;

        /// <summary>Hue occupies six bits and <b>wraps</b> rather than clamping.</summary>
        private const int HueMask = 63;

        /// <summary>Bit position of saturation in a packed HSL face colour.</summary>
        private const int SaturationShift = 7;

        /// <summary>Saturation occupies three bits and clamps.</summary>
        private const int SaturationMask = 7;

        /// <summary>Lightness occupies the low seven bits and clamps.</summary>
        private const int LightnessMask = 127;

        /// <summary>
        ///     A type-7 saturation delta is quartered before it is applied.
        /// </summary>
        /// <remarks>
        ///     <c>Renderable_Sub2.java:3090</c>. The three channels are treated differently from one
        ///     another - hue wraps, saturation is quartered then clamped, lightness is clamped - which
        ///     is the part that a re-derivation from first principles would get wrong.
        /// </remarks>
        private const int SaturationDivisor = 4;

        /// <summary>The model and its label groups this pose was built over.</summary>
        public SkinnedModel Skin { get; }

        /// <summary>Posed x per vertex, in model units once <see cref="Finish"/> has run.</summary>
        public int[] VertexX { get; }

        /// <summary>Posed y per vertex, in model units once <see cref="Finish"/> has run.</summary>
        public int[] VertexY { get; }

        /// <summary>Posed z per vertex, in model units once <see cref="Finish"/> has run.</summary>
        public int[] VertexZ { get; }

        /// <summary>Posed alpha per face.</summary>
        public byte[] FaceAlpha { get; }

        /// <summary>Posed packed HSL colour per face.</summary>
        public short[] FaceColour { get; }

        /// <summary>Whether any type-5 transform touched <see cref="FaceAlpha"/> this frame.</summary>
        /// <remarks>
        ///     The client's equivalent is dropping its cached draw list
        ///     (<c>Renderable_Sub2.java:3059-3061</c>). Here it lets the uploader re-send the per-face
        ///     buffers only on the frames that changed them, which is most frames never.
        /// </remarks>
        public bool FaceAlphaChanged { get; private set; }

        /// <summary>Whether any type-7 transform touched <see cref="FaceColour"/> this frame.</summary>
        public bool FaceColourChanged { get; private set; }

        /// <summary>
        ///     Whether the vertex arrays are currently in sixteenths rather than model units.
        /// </summary>
        /// <remarks>
        ///     Public because the tests assert the promotion happens, and because a reader of
        ///     <see cref="VertexX"/> mid-frame would otherwise be off by a factor of sixteen with
        ///     nothing to warn them. It is false again after <see cref="Finish"/>.
        /// </remarks>
        public bool IsScaled { get; private set; }

        /// <summary>Pivot x that the rotate and scale arms turn about.</summary>
        public int PivotX { get; private set; }

        /// <summary>Pivot y that the rotate and scale arms turn about.</summary>
        public int PivotY { get; private set; }

        /// <summary>Pivot z that the rotate and scale arms turn about.</summary>
        public int PivotZ { get; private set; }

        /// <summary>Creates a pose over a model, at rest.</summary>
        /// <param name="skin">The model and its label groups.</param>
        /// <exception cref="ArgumentNullException"><paramref name="skin"/> is null.</exception>
        public PosedMesh(SkinnedModel skin)
        {
            Skin = skin ?? throw new ArgumentNullException(nameof(skin));

            ModelDefinition model = skin.Model;
            VertexX = new int[model.VertX.Length];
            VertexY = new int[model.VertX.Length];
            VertexZ = new int[model.VertX.Length];
            FaceAlpha = new byte[model.faceIndices1.Length];
            FaceColour = new short[model.faceIndices1.Length];

            Reset();
        }

        /// <summary>Puts every vertex, alpha and colour back to the rest model's value.</summary>
        /// <remarks>
        ///     Called at the start of every frame. It is a copy rather than an inverse of the previous
        ///     frame's transforms, which is what makes the pose absolute and free of drift, and it is
        ///     why the buffers are allocated once and reused rather than rebuilt.
        ///     <para>
        ///     Each copy is bounded by the shorter of the two lengths. A model whose alpha or colour
        ///     array disagrees with its face count is damaged rather than unusual, and truncating is a
        ///     better answer than refusing to show it.
        ///     </para>
        /// </remarks>
        public void Reset()
        {
            ModelDefinition model = Skin.Model;

            Array.Copy(model.VertX, VertexX, Math.Min(VertexX.Length, model.VertX.Length));
            Array.Copy(model.VertY, VertexY, Math.Min(VertexY.Length, model.VertY.Length));
            Array.Copy(model.VertZ, VertexZ, Math.Min(VertexZ.Length, model.VertZ.Length));

            if (model.FaceAlpha != null)
            {
                //Stored signed and used unsigned - the client reads it back with & 0xff
                //(Renderable_Sub2.java:3046), so a stored -1 is alpha 255 and not an error.
                int faces = Math.Min(FaceAlpha.Length, model.FaceAlpha.Length);
                for (int face = 0; face < faces; face++)
                {
                    FaceAlpha[face] = (byte)model.FaceAlpha[face];
                }
            }
            else
            {
                //No alpha array means fully opaque in the client's convention, which it spells as
                //255 - stored (Renderable_Sub2.java:3070-3071). Zero here is that convention's
                //"opaque", not "invisible".
                Array.Clear(FaceAlpha, 0, FaceAlpha.Length);
            }

            Array.Copy(model.FaceColour, FaceColour, Math.Min(FaceColour.Length, model.FaceColour.Length));

            IsScaled = false;
            FaceAlphaChanged = false;
            FaceColourChanged = false;
            PivotX = 0;
            PivotY = 0;
            PivotZ = 0;
        }

        /// <summary>Applies one frame transform to the labels a skeleton bone names.</summary>
        /// <remarks>
        ///     The dispatch is <c>Renderable_Sub2.method2344</c>
        ///     (<c>Renderable_Sub2.java:2788-3179</c>) arm for arm. Types the client's chain has no
        ///     arm for - 4 among them - fall through to <see cref="TransformOutcome.Unsupported"/>
        ///     rather than being silently dropped.
        /// </remarks>
        /// <param name="transformType">The skeleton bone's transform type.</param>
        /// <param name="labels">Label ids the bone owns.</param>
        /// <param name="x">The frame's x value for this slot; a scale factor or a hue shift depending on type.</param>
        /// <param name="y">The frame's y value for this slot.</param>
        /// <param name="z">The frame's z value for this slot.</param>
        /// <returns>What the transform did.</returns>
        /// <exception cref="ArgumentNullException"><paramref name="labels"/> is null.</exception>
        public TransformOutcome Apply(int transformType, IReadOnlyList<int> labels, int x, int y, int z)
        {
            if (labels == null)
            {
                throw new ArgumentNullException(nameof(labels));
            }

            return transformType switch
            {
                TypePivot => SetPivot(labels, x, y, z),
                TypeTranslate => Translate(labels, x, y, z),
                TypeRotate => Rotate(labels, x, y, z),
                TypeScale => Scale(labels, x, y, z),
                TypeAlpha => ShiftAlpha(labels, x),
                TypeColour => ShiftColour(labels, x, y, z),
                _ => TransformOutcome.Unsupported,
            };
        }

        /// <summary>Reduces the mesh back to model units once a frame's transforms are done.</summary>
        /// <remarks>
        ///     <c>Renderable_Sub2.wa()</c>, <c>:5429-5437</c>. Must be called even when nothing was
        ///     applied, because the promotion is lazy and a frame that set a pivot and then reached no
        ///     vertices still promoted. Idempotent: a second call with nothing in between does nothing.
        /// </remarks>
        public void Finish()
        {
            if (!IsScaled)
            {
                return;
            }

            for (int vertex = 0; vertex < VertexX.Length; vertex++)
            {
                VertexX[vertex] = VertexX[vertex] + SubUnitBias >> SubUnitBits;
                VertexY[vertex] = VertexY[vertex] + SubUnitBias >> SubUnitBits;
                VertexZ[vertex] = VertexZ[vertex] + SubUnitBias >> SubUnitBits;
            }

            PivotX = PivotX + SubUnitBias >> SubUnitBits;
            PivotY = PivotY + SubUnitBias >> SubUnitBits;
            PivotZ = PivotZ + SubUnitBias >> SubUnitBits;

            IsScaled = false;
        }

        /// <summary>
        ///     Type 0: puts the pivot on the centroid of the labelled vertices, offset by the frame.
        /// </summary>
        /// <remarks>
        ///     <c>Renderable_Sub2.java:2792-2828</c>. When the labels own nothing the pivot becomes the
        ///     offset <i>alone</i> (<c>:2820-2823</c>) rather than keeping whatever the last frame left
        ///     there. That distinction is load-bearing for an unskinned model posed against a full
        ///     entity skeleton: a stale pivot would make the next rotation swing the model about a
        ///     point from a previous frame.
        /// </remarks>
        /// <param name="labels">Label ids the pivot bone owns.</param>
        /// <param name="x">Offset from the centroid, in model units.</param>
        /// <param name="y">Offset from the centroid, in model units.</param>
        /// <param name="z">Offset from the centroid, in model units.</param>
        /// <returns>Whether any vertex contributed to the centroid.</returns>
        private TransformOutcome SetPivot(IReadOnlyList<int> labels, int x, int y, int z)
        {
            EnsureScaled();

            //The offset arrives in model units and the vertices are now in sixteenths, so it is the
            //offset that gets promoted here - which is what the client does, because its vertices
            //were already promoted before the frame started.
            int offsetX = x << SubUnitBits;
            int offsetY = y << SubUnitBits;
            int offsetZ = z << SubUnitBits;

            int sumX = 0;
            int sumY = 0;
            int sumZ = 0;
            int contributors = 0;

            for (int i = 0; i < labels.Count; i++)
            {
                foreach (int vertex in Skin.VerticesFor(labels[i]))
                {
                    sumX += VertexX[vertex];
                    sumY += VertexY[vertex];
                    sumZ += VertexZ[vertex];
                    contributors++;
                }
            }

            if (contributors <= 0)
            {
                PivotX = offsetX;
                PivotY = offsetY;
                PivotZ = offsetZ;
                return TransformOutcome.NoTargets;
            }

            PivotX = sumX / contributors + offsetX;
            PivotY = sumY / contributors + offsetY;
            PivotZ = sumZ / contributors + offsetZ;
            return TransformOutcome.Applied;
        }

        /// <summary>Type 1: moves the labelled vertices and nothing else.</summary>
        /// <remarks><c>Renderable_Sub2.java:2829-2848</c>.</remarks>
        /// <param name="labels">Label ids the bone owns.</param>
        /// <param name="x">Translation in model units.</param>
        /// <param name="y">Translation in model units.</param>
        /// <param name="z">Translation in model units.</param>
        /// <returns>Whether any vertex was reached.</returns>
        private TransformOutcome Translate(IReadOnlyList<int> labels, int x, int y, int z)
        {
            EnsureScaled();

            int deltaX = x << SubUnitBits;
            int deltaY = y << SubUnitBits;
            int deltaZ = z << SubUnitBits;
            bool reachedAnything = false;

            for (int i = 0; i < labels.Count; i++)
            {
                foreach (int vertex in Skin.VerticesFor(labels[i]))
                {
                    VertexX[vertex] += deltaX;
                    VertexY[vertex] += deltaY;
                    VertexZ[vertex] += deltaZ;
                    reachedAnything = true;
                }
            }

            return reachedAnything ? TransformOutcome.Applied : TransformOutcome.NoTargets;
        }

        /// <summary>
        ///     Type 2: turns the labelled vertices about the pivot, in the client's sense of rotation.
        /// </summary>
        /// <remarks>
        ///     <c>Renderable_Sub2.java:2849-2899</c>. Two things here are easy to get wrong and
        ///     invisible when you do.
        ///     <para>
        ///     <b>The sense.</b> The z matrix is <c>x' = cx + sy</c>, <c>y' = cy - sx</c>
        ///     (<c>:2864-2873</c>), which turns clockwise in the x-y plane rather than the
        ///     counter-clockwise the usual convention gives. A sign error mirrors every animation in
        ///     the cache and shows nothing at all on a symmetrical model.
        ///     </para>
        ///     <b>The order.</b> See the comment on the arms below.
        /// </remarks>
        /// <param name="labels">Label ids the bone owns.</param>
        /// <param name="x">Rotation about x, in <see cref="SkeletalTrig"/> steps.</param>
        /// <param name="y">Rotation about y, in <see cref="SkeletalTrig"/> steps.</param>
        /// <param name="z">Rotation about z, in <see cref="SkeletalTrig"/> steps.</param>
        /// <returns>Whether any vertex was reached.</returns>
        private TransformOutcome Rotate(IReadOnlyList<int> labels, int x, int y, int z)
        {
            bool reachedAnything = false;

            for (int i = 0; i < labels.Count; i++)
            {
                foreach (int vertex in Skin.VerticesFor(labels[i]))
                {
                    int localX = VertexX[vertex] - PivotX;
                    int localY = VertexY[vertex] - PivotY;
                    int localZ = VertexZ[vertex] - PivotZ;

                    /* The order is z, then x, then y - Renderable_Sub2.java:2864, :2875 and :2886
                       test the three values in that order. Rotations do not commute, so this is not
                       a detail: applied in the written order of the fields, a limb that should bend
                       twists sideways. Both poses look plausible; only one is the client's. */
                    if (z != 0)
                    {
                        int sin = SkeletalTrig.Sin(z);
                        int cos = SkeletalTrig.Cos(z);
                        int rotatedX = cos * localX + sin * localY + SkeletalTrig.ShiftBias >> SkeletalTrig.FractionBits;
                        localY = cos * localY - sin * localX + SkeletalTrig.ShiftBias >> SkeletalTrig.FractionBits;
                        localX = rotatedX;
                    }

                    if (x != 0)
                    {
                        int sin = SkeletalTrig.Sin(x);
                        int cos = SkeletalTrig.Cos(x);
                        int rotatedY = cos * localY - sin * localZ + SkeletalTrig.ShiftBias >> SkeletalTrig.FractionBits;
                        localZ = sin * localY + cos * localZ + SkeletalTrig.ShiftBias >> SkeletalTrig.FractionBits;
                        localY = rotatedY;
                    }

                    if (y != 0)
                    {
                        int sin = SkeletalTrig.Sin(y);
                        int cos = SkeletalTrig.Cos(y);
                        int rotatedX = cos * localX + sin * localZ + SkeletalTrig.ShiftBias >> SkeletalTrig.FractionBits;
                        localZ = cos * localZ - sin * localX + SkeletalTrig.ShiftBias >> SkeletalTrig.FractionBits;
                        localX = rotatedX;
                    }

                    VertexX[vertex] = localX + PivotX;
                    VertexY[vertex] = localY + PivotY;
                    VertexZ[vertex] = localZ + PivotZ;
                    reachedAnything = true;
                }
            }

            return reachedAnything ? TransformOutcome.Applied : TransformOutcome.NoTargets;
        }

        /// <summary>Type 3: scales the labelled vertices about the pivot, with 128 meaning unchanged.</summary>
        /// <remarks>
        ///     <c>Renderable_Sub2.java:3014-3035</c>. No promotion to sixteenths here, unlike the pivot
        ///     and translate arms - a scale factor is a ratio, so it works in whatever space the
        ///     vertices are already in.
        /// </remarks>
        /// <param name="labels">Label ids the bone owns.</param>
        /// <param name="x">Scale along x, over 128.</param>
        /// <param name="y">Scale along y, over 128.</param>
        /// <param name="z">Scale along z, over 128.</param>
        /// <returns>Whether any vertex was reached.</returns>
        private TransformOutcome Scale(IReadOnlyList<int> labels, int x, int y, int z)
        {
            bool reachedAnything = false;

            for (int i = 0; i < labels.Count; i++)
            {
                foreach (int vertex in Skin.VerticesFor(labels[i]))
                {
                    VertexX[vertex] = ((VertexX[vertex] - PivotX) * x >> ScaleBits) + PivotX;
                    VertexY[vertex] = ((VertexY[vertex] - PivotY) * y >> ScaleBits) + PivotY;
                    VertexZ[vertex] = ((VertexZ[vertex] - PivotZ) * z >> ScaleBits) + PivotZ;
                    reachedAnything = true;
                }
            }

            return reachedAnything ? TransformOutcome.Applied : TransformOutcome.NoTargets;
        }

        /// <summary>Type 5: shifts the labelled faces' alpha, in steps of eight.</summary>
        /// <remarks>
        ///     <c>Renderable_Sub2.java:3036-3057</c>. <b>Face</b> labels, not vertex labels - the two
        ///     are numbered independently and overlap in range, so a mesh whose vertex label 0 owns
        ///     everything and whose face label 0 owns nothing must not fade.
        /// </remarks>
        /// <param name="labels">Label ids the bone owns.</param>
        /// <param name="x">Steps of eight to add; may be negative.</param>
        /// <returns>Whether any face was reached.</returns>
        private TransformOutcome ShiftAlpha(IReadOnlyList<int> labels, int x)
        {
            bool reachedAnything = false;

            for (int i = 0; i < labels.Count; i++)
            {
                foreach (int face in Skin.FacesFor(labels[i]))
                {
                    int shifted = x * AlphaStep + (FaceAlpha[face] & 0xFF);
                    FaceAlpha[face] = (byte)Math.Clamp(shifted, 0, MaxAlpha);
                    reachedAnything = true;
                }
            }

            if (reachedAnything)
            {
                FaceAlphaChanged = true;
            }

            return reachedAnything ? TransformOutcome.Applied : TransformOutcome.NoTargets;
        }

        /// <summary>Type 7: shifts the labelled faces' packed HSL colour.</summary>
        /// <remarks>
        ///     <c>Renderable_Sub2.java:3075-3110</c>. The three channels are handled differently and
        ///     deliberately: hue <b>wraps</b> through six bits, saturation is <b>quartered</b> before
        ///     it is added and then clamped to three bits, lightness is clamped to seven. Treating all
        ///     three the same way is the mistake this arm invites, and it produces colours that look
        ///     reasonable and are not the client's.
        /// </remarks>
        /// <param name="labels">Label ids the bone owns.</param>
        /// <param name="x">Hue shift, wrapped.</param>
        /// <param name="y">Saturation shift, quartered then clamped.</param>
        /// <param name="z">Lightness shift, clamped.</param>
        /// <returns>Whether any face was reached.</returns>
        private TransformOutcome ShiftColour(IReadOnlyList<int> labels, int x, int y, int z)
        {
            bool reachedAnything = false;

            for (int i = 0; i < labels.Count; i++)
            {
                foreach (int face in Skin.FacesFor(labels[i]))
                {
                    int packed = FaceColour[face] & 0xFFFF;

                    int hue = ((packed >> HueShift) + x) & HueMask;
                    int saturation = Math.Clamp(
                        ((packed >> SaturationShift) & SaturationMask) + y / SaturationDivisor,
                        0, SaturationMask);
                    int lightness = Math.Clamp((packed & LightnessMask) + z, 0, LightnessMask);

                    FaceColour[face] = (short)((hue << HueShift) | (saturation << SaturationShift) | lightness);
                    reachedAnything = true;
                }
            }

            if (reachedAnything)
            {
                FaceColourChanged = true;
            }

            return reachedAnything ? TransformOutcome.Applied : TransformOutcome.NoTargets;
        }

        /// <summary>Promotes the vertex arrays and the pivot into sixteenths, once per frame.</summary>
        /// <remarks>
        ///     <c>Renderable_Sub2.NA()</c>, <c>:4792-4809</c>, where it runs unconditionally before a
        ///     frame's transforms. Here it is lazy, because a frame made entirely of type-5 and type-7
        ///     transforms touches no vertex and would pay for a promotion and a reduction it does not
        ///     need. The pivot is promoted alongside the vertices so the two stay in one space; the
        ///     client does not have to, because it zeroes the pivot at the same point (<c>:4805-4807</c>).
        /// </remarks>
        private void EnsureScaled()
        {
            if (IsScaled)
            {
                return;
            }

            for (int vertex = 0; vertex < VertexX.Length; vertex++)
            {
                VertexX[vertex] <<= SubUnitBits;
                VertexY[vertex] <<= SubUnitBits;
                VertexZ[vertex] <<= SubUnitBits;
            }

            PivotX <<= SubUnitBits;
            PivotY <<= SubUnitBits;
            PivotZ <<= SubUnitBits;

            IsScaled = true;
        }
    }
}
