using System.Collections.Generic;
using System;
using FlashEditor.Definitions;

namespace FlashEditor.Rendering
{
    /// <summary>
    ///     A model with its per-vertex and per-face skin arrays inverted into label groups.
    /// </summary>
    /// <remarks>
    ///     The cache stores the join the wrong way round for posing. A model file records, for each
    ///     vertex, which label it belongs to; a skeleton bone names a <i>list of labels</i> and the
    ///     transform has to move every vertex in them. Walking the skin array once per label per bone
    ///     per frame is the obvious reading of that and is quadratic, so the client inverts it once at
    ///     load (<c>Model.method2595</c>, <c>Model.java:1733-1762</c> for vertices;
    ///     <c>Renderable_Sub2.java:1008-1036</c> for faces) and this does the same.
    ///     <para>
    ///     The inversion is per model rather than per pose, so several poses of the same model - a
    ///     preview and a picker, say - share it.
    ///     </para>
    /// </remarks>
    public sealed class SkinnedModel
    {
        /// <summary>Shared empty group, so an unlabelled lookup allocates nothing per frame.</summary>
        private static readonly int[] NoMembers = Array.Empty<int>();

        /// <summary>The model these groups were built from.</summary>
        public ModelDefinition Model { get; }

        /// <summary>Vertex indices per label id, indexed by label.</summary>
        /// <remarks>
        ///     Prefer <see cref="VerticesFor"/>. This is exposed for a panel that wants to show how a
        ///     model is skinned, and it is short of the label ids a skeleton may name - a bone can
        ///     name a label no vertex carries.
        /// </remarks>
        public int[][] VertexLabelGroups { get; }

        /// <summary>Face indices per label id, indexed by label.</summary>
        /// <remarks>
        ///     Numbered independently of <see cref="VertexLabelGroups"/> and overlapping it in range.
        ///     The alpha and colour transforms read this one and every other transform reads the other,
        ///     which is the distinction the whole type exists to keep straight.
        /// </remarks>
        public int[][] FaceLabelGroups { get; }

        /// <summary>Whether any transform can reach this model at all.</summary>
        /// <remarks>
        ///     A model with neither kind of label is not broken - most static scenery has none - but
        ///     posing it against a full entity skeleton produces a frame that visibly does nothing.
        ///     The animator reports that rather than leaving it looking like a still frame.
        /// </remarks>
        public bool IsSkinned => VertexLabelGroups.Length != 0 || FaceLabelGroups.Length != 0;

        /// <summary>Inverts a model's skin arrays into label groups.</summary>
        /// <param name="model">The model.</param>
        /// <exception cref="ArgumentNullException"><paramref name="model"/> is null.</exception>
        public SkinnedModel(ModelDefinition model)
        {
            Model = model ?? throw new ArgumentNullException(nameof(model));
            VertexLabelGroups = ResolveVertexGroups(model);
            FaceLabelGroups = Group(model.FaceSkin, model.faceIndices1.Length);
        }

        /// <summary>The vertices a label owns.</summary>
        /// <remarks>
        ///     Out-of-range is an empty group rather than an error. A skeleton is shared across many
        ///     models and routinely names labels a given model does not carry, so this is the ordinary
        ///     case and not a fault worth throwing on inside a render tick.
        /// </remarks>
        /// <param name="label">Label id, as a skeleton bone names it.</param>
        /// <returns>The vertex indices, or an empty array.</returns>
        public int[] VerticesFor(int label)
        {
            return (uint)label < (uint)VertexLabelGroups.Length ? VertexLabelGroups[label] : NoMembers;
        }

        /// <summary>The faces a label owns.</summary>
        /// <param name="label">Label id, as a skeleton bone names it.</param>
        /// <returns>The face indices, or an empty array.</returns>
        public int[] FacesFor(int label)
        {
            return (uint)label < (uint)FaceLabelGroups.Length ? FaceLabelGroups[label] : NoMembers;
        }

        /// <summary>Creates a fresh pose over this model, at rest.</summary>
        /// <returns>The pose.</returns>
        public PosedMesh CreatePose()
        {
            return new PosedMesh(this);
        }

        /// <summary>Takes the vertex groups the decoder already built, or inverts the skins.</summary>
        /// <remarks>
        ///     Some model formats carry the grouping directly rather than a label per vertex, in which
        ///     case the decoder has already done this work and re-deriving it from
        ///     <see cref="ModelDefinition.VertSkins"/> would be both wasted and lossy.
        /// </remarks>
        /// <param name="model">The model.</param>
        /// <returns>The vertex label groups.</returns>
        private static int[][] ResolveVertexGroups(ModelDefinition model)
        {
            return model.VertexGroups ?? Group(model.VertSkins, model.VertX.Length);
        }

        /// <summary>
        ///     Inverts a per-element label array into per-label element lists.
        /// </summary>
        /// <remarks>
        ///     Three passes and no intermediate lists, which is the client's shape
        ///     (<c>Model.java:1733-1762</c>): count each label's members, allocate each group at its
        ///     exact size, then fill. Building <c>List&lt;int&gt;</c> per label instead would be
        ///     shorter and would allocate once per label per model load.
        ///     <para>
        ///     A negative label means the element belongs to no label at all, and is skipped in both
        ///     the counting and the filling pass - the client's guard is explicitly <c>&gt;= 0</c>
        ///     (<c>Model.java:1744</c> and <c>:1758</c>). Letting a -1 fall into group 0 would attach
        ///     every unassigned vertex in the model to whichever bone owns label 0, which is usually a
        ///     real bone.
        ///     </para>
        /// </remarks>
        /// <param name="labels">Label id per element, or null when the model carries none.</param>
        /// <param name="count">
        ///     How many elements there are. Taken from the geometry rather than from
        ///     <paramref name="labels"/>, and the shorter of the two wins - a skin array that
        ///     disagrees with the vertex count is a damaged file, not a reason to read past an array.
        /// </param>
        /// <returns>Element indices per label, or an empty array when there are no labels.</returns>
        private static int[][] Group(IReadOnlyList<int>? labels, int count)
        {
            if (labels == null || count <= 0)
            {
                return Array.Empty<int[]>();
            }

            int elements = Math.Min(count, labels.Count);
            int highestLabel = -1;

            for (int element = 0; element < elements; element++)
            {
                if (labels[element] > highestLabel)
                {
                    highestLabel = labels[element];
                }
            }

            //Every label was negative, so nothing is skinned.
            if (highestLabel < 0)
            {
                return Array.Empty<int[]>();
            }

            int[] membersPerLabel = new int[highestLabel + 1];
            for (int element = 0; element < elements; element++)
            {
                if (labels[element] >= 0)
                {
                    membersPerLabel[labels[element]]++;
                }
            }

            int[][] groups = new int[highestLabel + 1][];
            for (int label = 0; label <= highestLabel; label++)
            {
                //Reusing the one shared empty array for the gaps matters: label ids are sparse, and a
                //skeleton's highest label leaves most of this array holding nothing.
                groups[label] = membersPerLabel[label] == 0 ? NoMembers : new int[membersPerLabel[label]];

                //Rewound so the fill pass can use it as a write cursor.
                membersPerLabel[label] = 0;
            }

            for (int element = 0; element < elements; element++)
            {
                int label = labels[element];
                if (label >= 0)
                {
                    groups[label][membersPerLabel[label]++] = element;
                }
            }

            return groups;
        }
    }
}
