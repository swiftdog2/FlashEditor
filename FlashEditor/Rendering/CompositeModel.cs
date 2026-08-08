using System.Collections.Generic;
using System;
using FlashEditor.Definitions;

namespace FlashEditor.Rendering
{
    /// <summary>
    ///     Several of an entity's model files joined into the one mesh the client poses.
    /// </summary>
    /// <remarks>
    ///     <b>Why an entity has to be merged before it is posed.</b> An NPC or a player is not one
    ///     model file, it is a head, a torso, a pair of hands and so on, and the client never animates
    ///     them separately: <c>Class141.java:801</c> builds <c>new Model(models, models.length)</c>
    ///     whenever there is more than one, and <c>Node_Sub3.java:172</c> does the same on the
    ///     equipped-model path. Everything downstream is then a property of the whole body -
    ///     <c>Renderable_Sub2.java:997</c> inverts the vertex labels over the merged model, and
    ///     <c>:2803-2827</c> sums a rotation centroid over it.
    ///     <para>
    ///     Posing the parts one at a time gets both of those wrong, and does it silently. A pivot bone
    ///     names labels; the centroid of those labels within <i>one part</i> is not the centroid within
    ///     the body, and a part carrying none of them at all falls back to the bare offset
    ///     (<c>Renderable_Sub2.java:2820-2823</c>) - the model origin, which on a character sits on the
    ///     floor between the feet. Measured on NPC 1, frame 15204474: the jaw's part resolved a pivot
    ///     of (0,0,0) where the head's part resolved (0,-10954,78), and the hands' (0,0,0) against the
    ///     arms' (1514,-9841,2345). Each part then rotated about a different centre, which is the jaw
    ///     leaving the face and the hands leaving the wrists.
    ///     </para>
    ///     <para>
    ///     <b>The weld is the second half of it.</b> Two parts meeting at a seam each carry their own
    ///     copy of the shared vertex, and the two copies may carry <i>different</i> labels, so two
    ///     bones drive what has to be one point. <c>Model.method2598</c>
    ///     (<c>Model.java:1824-1848</c>) reuses an existing vertex at the same coordinate and keeps the
    ///     <b>first</b> contributor's label - it rewrites the source mask on the reused vertex
    ///     (<c>:1841</c>) and never <c>anIntArray1411</c> - so the seam follows one bone. On NPC 1, 46
    ///     rest coordinates are shared by two parts and 3 of them are labelled differently.
    ///     </para>
    ///     <para>
    ///     <b>What is deliberately not merged.</b> Lighting. <see cref="PosedNormals"/> still runs per
    ///     part, so a smooth-shaded normal at a seam is averaged within its own part where the client
    ///     would average it across the join. That is a shading difference at the seam rather than a
    ///     geometry one, it predates this type, and folding it in means merging the normal pass too.
    ///     </para>
    /// </remarks>
    public sealed class CompositeModel
    {
        /// <summary>Label meaning "this vertex belongs to no bone".</summary>
        /// <remarks>
        ///     The client's guard is explicitly <c>&gt;= 0</c> (<c>Model.java:1744</c>), and
        ///     <c>method2598</c> stores <c>-1</c> for a source that carries no label array at all
        ///     (<c>:1842-1843</c>). Letting an unlabelled vertex fall into label 0 would attach it to
        ///     whichever bone owns that label, which is usually a real one.
        /// </remarks>
        private const int NoLabel = -1;

        /// <summary>The joined mesh, carrying only what the posing path reads.</summary>
        /// <remarks>
        ///     Vertices, faces, per-vertex and per-face labels, colour and alpha. Textures, normals and
        ///     the model's own particle attachments are not carried, because nothing poses through this
        ///     model - the renderer, the picker and the particle system all still work against the
        ///     original per-part models, which is what keeps their model-position contract intact.
        /// </remarks>
        public ModelDefinition Model { get; }

        /// <summary>The joined mesh with its label groups inverted, ready to pose.</summary>
        public SkinnedModel Skin { get; }

        /// <summary>Composite vertex index per source vertex, indexed by part then by vertex.</summary>
        /// <remarks>
        ///     The whole reason a merge does not break the rest of the viewport. Every consumer of a
        ///     pose - the renderer's vertex buffers, <see cref="PickMesh"/>, <see cref="ParticleSystem"/>
        ///     and the hover overlay - indexes by model position and vertex within that model, so the
        ///     merged pose is read back out through this map into one pose per part and the contract
        ///     they were written against never changes.
        /// </remarks>
        public IReadOnlyList<int[]> VertexMap { get; }

        /// <summary>Composite index of each part's first face.</summary>
        /// <remarks>
        ///     Faces are concatenated and never welded - two coincident faces are still two faces - so
        ///     a part's faces occupy one contiguous run and an offset is the whole mapping.
        /// </remarks>
        public IReadOnlyList<int> FaceOffset { get; }

        /// <summary>Which parts contributed to each composite vertex, one bit per part.</summary>
        /// <remarks>
        ///     <c>Model.aShortArray1408</c>, written by <c>method2598</c> at <c>Model.java:1841</c> and
        ///     <c>:1836</c>. Held as an <c>int</c> rather than the client's <c>short</c> because
        ///     <c>1 &lt;&lt; i</c> in a Java short is meaningless past 16 parts and this is diagnostic
        ///     here rather than load-bearing; parts past the 32nd contribute no bit at all, which is
        ///     recorded by <see cref="PartsBeyondTheMask"/> rather than passed off as a full mask.
        /// </remarks>
        public IReadOnlyList<int> VertexSourceMask { get; }

        /// <summary>How many source vertices landed on a composite vertex an earlier part had placed.</summary>
        /// <remarks>
        ///     The seam count, and the number that says whether merging did anything. Zero means the
        ///     parts share no coordinate, in which case the merge changed only the pivots.
        /// </remarks>
        public int WeldedVertexCount { get; }

        /// <summary>How many parts sat past the 32nd and so carry no bit in the source mask.</summary>
        public int PartsBeyondTheMask { get; }

        /// <summary>Joins a set of models the way the client's merging constructor does.</summary>
        /// <remarks>
        ///     <c>Model.java:148-379</c>. The ordering matters and is preserved: parts in the order
        ///     given, and within a part its faces in order, because <c>method2598</c> returns the
        ///     <i>first</i> composite vertex at a coordinate and so the first part to reach a seam is
        ///     the one whose label survives. Vertices no face names are appended after that part's
        ///     faces rather than dropped - the client drops them, because it only ever welds what a
        ///     face, a particle or an effector points at, but every one of them still needs a place to
        ///     read its posed position back from.
        ///     <para>
        ///     A dictionary stands in for the client's linear rescan of every placed vertex
        ///     (<c>:1829-1836</c>). It returns the same answer: the scan runs upwards from zero and
        ///     stops at the first match, which is exactly the entry a coordinate-keyed map holds.
        ///     </para>
        /// </remarks>
        /// <param name="parts">The models, in the order the viewport was given them.</param>
        /// <exception cref="ArgumentNullException"><paramref name="parts"/> is null.</exception>
        /// <exception cref="ArgumentException"><paramref name="parts"/> holds fewer than two models.</exception>
        public CompositeModel(IReadOnlyList<ModelDefinition> parts)
        {
            if (parts == null)
            {
                throw new ArgumentNullException(nameof(parts));
            }

            //Refused rather than handled, because the client refuses too - Class141.java:801 takes
            //models[0] untouched when there is one - and a single model merged with itself would weld
            //its own coincident vertices, which is a change to how every static model poses today.
            if (parts.Count < 2)
            {
                throw new ArgumentException(
                    "A composite needs at least two models; the client leaves a single model unmerged"
                    + " (Class141.java:801).", nameof(parts));
            }

            int vertexCapacity = 0;
            int faceCount = 0;
            bool anyVertexLabels = false;
            bool anyFaceLabels = false;
            bool anyFaceAlpha = false;

            foreach (ModelDefinition part in parts)
            {
                vertexCapacity += part.VertX.Length;
                faceCount += part.faceIndices1.Length;
                anyVertexLabels |= part.VertexGroups != null || part.VertSkins != null;
                anyFaceLabels |= part.FaceSkin != null;
                anyFaceAlpha |= part.FaceAlpha != null;
            }

            int[] vertexX = new int[vertexCapacity];
            int[] vertexY = new int[vertexCapacity];
            int[] vertexZ = new int[vertexCapacity];
            int[] vertexLabels = new int[vertexCapacity];
            int[] sourceMask = new int[vertexCapacity];

            int[] faceA = new int[faceCount];
            int[] faceB = new int[faceCount];
            int[] faceC = new int[faceCount];
            short[] faceColour = new short[faceCount];
            sbyte[]? faceAlpha = anyFaceAlpha ? new sbyte[faceCount] : null;
            int[]? faceLabels = anyFaceLabels ? new int[faceCount] : null;
            sbyte[] faceRenderType = new sbyte[faceCount];

            int[][] vertexMap = new int[parts.Count][];
            int[] faceOffsets = new int[parts.Count];

            Dictionary<(int, int, int), int> placed = new Dictionary<(int, int, int), int>(vertexCapacity);
            int vertices = 0;
            int welded = 0;
            int beyondTheMask = 0;
            int faceCursor = 0;

            for (int part = 0; part < parts.Count; part++)
            {
                ModelDefinition model = parts[part];
                int[] sourceLabels = VertexLabels(model);
                int[] map = new int[model.VertX.Length];
                Array.Fill(map, -1);
                vertexMap[part] = map;
                faceOffsets[part] = faceCursor;

                //Only the low 32 parts get a bit. The client's short runs out at 16 and wraps in
                //silence; running out is recorded here instead.
                int bit = part < 32 ? 1 << part : 0;

                if (bit == 0)
                {
                    beyondTheMask++;
                }

                for (int face = 0; face < model.faceIndices1.Length; face++)
                {
                    faceA[faceCursor] = Weld(model.faceIndices1[face]);
                    faceB[faceCursor] = Weld(model.faceIndices2[face]);
                    faceC[faceCursor] = Weld(model.faceIndices3[face]);

                    faceColour[faceCursor] = face < model.FaceColour.Length ? model.FaceColour[face] : (short)0;

                    if (faceAlpha != null)
                    {
                        //A part with no alpha array is opaque in the client's convention, which it
                        //spells as a stored zero (Renderable_Sub2.java:3070-3071). So zero-filling a
                        //part that has none beside one that does is the merge, not a default.
                        faceAlpha[faceCursor] = model.FaceAlpha != null && face < model.FaceAlpha.Length
                            ? model.FaceAlpha[face]
                            : (sbyte)0;
                    }

                    if (faceLabels != null)
                    {
                        faceLabels[faceCursor] = model.FaceSkin != null && face < model.FaceSkin.Length
                            ? model.FaceSkin[face]
                            : NoLabel;
                    }

                    faceRenderType[faceCursor] = model.FaceRenderType != null && face < model.FaceRenderType.Length
                        ? model.FaceRenderType[face]
                        : (sbyte)0;

                    faceCursor++;
                }

                //Whatever no face of this part named. The client never places these, because nothing
                //it merges points at them; here they still need a composite vertex to read a posed
                //position back from, and welding them keeps a coincident one following the same bone.
                for (int vertex = 0; vertex < map.Length; vertex++)
                {
                    if (map[vertex] < 0)
                    {
                        Weld(vertex);
                    }
                }

                //Model.method2598, Model.java:1824-1848.
                int Weld(int sourceVertex)
                {
                    if ((uint)sourceVertex >= (uint)map.Length)
                    {
                        //A face naming a vertex its model does not have. Index 7 holds them, and the
                        //rest of the layer already tolerates them by skipping; the index is passed
                        //through so the composite face stays out of range too and is skipped again.
                        return sourceVertex;
                    }

                    if (map[sourceVertex] >= 0)
                    {
                        return map[sourceVertex];
                    }

                    (int, int, int) key = (
                        model.VertX[sourceVertex], model.VertY[sourceVertex], model.VertZ[sourceVertex]);

                    if (placed.TryGetValue(key, out int existing))
                    {
                        //:1841 - the mask gains this part and the label is left alone, so the first
                        //contributor's bone keeps the seam.
                        sourceMask[existing] |= bit;
                        map[sourceVertex] = existing;
                        welded++;
                        return existing;
                    }

                    int index = vertices++;
                    vertexX[index] = model.VertX[sourceVertex];
                    vertexY[index] = model.VertY[sourceVertex];
                    vertexZ[index] = model.VertZ[sourceVertex];
                    vertexLabels[index] = sourceVertex < sourceLabels.Length
                        ? sourceLabels[sourceVertex]
                        : NoLabel;
                    sourceMask[index] = bit;
                    placed[key] = index;
                    map[sourceVertex] = index;
                    return index;
                }
            }

            Model = new ModelDefinition
            {
                VertX = Trim(vertexX, vertices),
                VertY = Trim(vertexY, vertices),
                VertZ = Trim(vertexZ, vertices),
                VertSkins = anyVertexLabels ? Trim(vertexLabels, vertices) : null,
                faceIndices1 = faceA,
                faceIndices2 = faceB,
                faceIndices3 = faceC,
                FaceColour = faceColour,
                FaceAlpha = faceAlpha,
                FaceSkin = faceLabels,
                FaceRenderType = faceRenderType,
            };

            Skin = new SkinnedModel(Model);
            VertexMap = vertexMap;
            FaceOffset = faceOffsets;
            VertexSourceMask = Trim(sourceMask, vertices);
            WeldedVertexCount = welded;
            PartsBeyondTheMask = beyondTheMask;
        }

        /// <summary>One label per vertex, however the part happens to store its skins.</summary>
        /// <remarks>
        ///     <see cref="ModelDefinition.ComputeAnimationTables"/> inverts <c>VertSkins</c> into
        ///     <see cref="ModelDefinition.VertexGroups"/> and then nulls the array it came from, so a
        ///     decoded model normally carries only the groups. The inversion is lossless - every vertex
        ///     is placed in exactly one group - which is what makes turning it back safe.
        /// </remarks>
        /// <param name="model">The part.</param>
        /// <returns>The label per vertex, <see cref="NoLabel"/> where a vertex has none.</returns>
        private static int[] VertexLabels(ModelDefinition model)
        {
            int[] labels = new int[model.VertX.Length];
            Array.Fill(labels, NoLabel);

            if (model.VertexGroups != null)
            {
                for (int label = 0; label < model.VertexGroups.Length; label++)
                {
                    int[] members = model.VertexGroups[label];

                    if (members == null)
                    {
                        continue;
                    }

                    foreach (int vertex in members)
                    {
                        if ((uint)vertex < (uint)labels.Length)
                        {
                            labels[vertex] = label;
                        }
                    }
                }

                return labels;
            }

            if (model.VertSkins != null)
            {
                int shared = Math.Min(labels.Length, model.VertSkins.Length);
                Array.Copy(model.VertSkins, labels, shared);
            }

            return labels;
        }

        /// <summary>Cuts an over-allocated array down to what the weld actually used.</summary>
        /// <param name="values">The array, sized to the unwelded upper bound.</param>
        /// <param name="length">How many entries are real.</param>
        /// <returns>The array itself when nothing welded, or a copy of its used prefix.</returns>
        private static int[] Trim(int[] values, int length)
        {
            if (length == values.Length)
            {
                return values;
            }

            int[] trimmed = new int[length];
            Array.Copy(values, trimmed, length);
            return trimmed;
        }
    }
}
