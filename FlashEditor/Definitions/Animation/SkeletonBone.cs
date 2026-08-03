using System.Collections.Generic;

namespace FlashEditor.Definitions.Animation {
    /// <summary>
    ///     One bone of an animation skeleton: a transform kind, the vertex or face label groups it
    ///     moves, and the mask that decides whether it runs at all.
    /// </summary>
    /// <remarks>
    ///     The five arrays a skeleton file stores are column-major - every transform type, then every
    ///     flag, then every mask, then every label count, then every label - so a "bone" is a position
    ///     shared across five separate blocks rather than a contiguous record
    ///     (<c>Node_Sub1.java:93-117</c>). This type is the row view of those columns, which is what an
    ///     editor and the frame decoder both want; <see cref="SkeletonDefinition.Encode"/> writes the
    ///     columns back out.
    ///     <para>
    ///     Every field here is the <b>stored</b> byte, never a normalised one. The client normalises
    ///     twice on the way in and both are lossy - see <see cref="TransformType"/> and
    ///     <see cref="Flag"/> - so a decoder that kept only what the client keeps could not write the
    ///     file back.
    ///     </para>
    /// </remarks>
    public sealed class SkeletonBone {
        /// <summary>The transform type the client rewrites on load, and the value it rewrites it to.</summary>
        /// <remarks>
        ///     <c>Node_Sub1.java:96-98</c>: <c>if (type == 6) type = 2;</c>. Nothing writes the array
        ///     back, so for the client the two are the same transform from that point on.
        /// </remarks>
        public const int AliasedTransformType = 6;

        /// <summary>What <see cref="AliasedTransformType"/> is folded into.</summary>
        public const int AliasTargetTransformType = 2;

        /// <summary>The flag byte value the client reads as set.</summary>
        /// <remarks>
        ///     <c>Node_Sub1.java:102</c> is <c>(readUnsignedByte() ^ 0xffffffff) == -2</c>, which is
        ///     <c>== 1</c> written obfuscated. Any other stored value reads as unset, so folding the
        ///     byte into a bool would re-encode a stored 2 as a 0.
        /// </remarks>
        public const int FlagSetValue = 1;

        /// <summary>
        ///     The transform kind, exactly as the file stores it.
        /// </summary>
        /// <remarks>
        ///     Read <see cref="EffectiveTransformType"/> to get what the client would act on. Storing
        ///     the remapped value here would make a skeleton carrying a 6 re-encode as a 2, silently
        ///     rewriting a file nobody edited - and the archive CRC covers those bytes.
        ///     <para>
        ///     What the value means is settled by what the transform dispatcher does with it, not by
        ///     any name: <c>Renderable.java:324</c> passes it as the first argument of
        ///     <c>method2344</c>, whose arms in <c>Renderable_Sub2.java</c> are 0 (accumulate the
        ///     labelled vertices into a pivot, :2792), 1 (translate, :2829), 2 (rotate, :2849), 3
        ///     (scale by <c>&gt;&gt; 7</c>, :3014) and 5 (the only arm that indexes the model's
        ///     <i>face</i> label groups rather than its vertex groups, :3036). Types 4, 7, 8, 9 and 10
        ///     also occur in this cache; type 4 has no arm in any of the client's three renderers.
        ///     None of that changes the codec, which treats the value as an opaque byte.
        ///     </para>
        /// </remarks>
        public int TransformType { get; set; }

        /// <summary>
        ///     The transform kind the client would act on, with its <c>6 -&gt; 2</c> remap applied.
        /// </summary>
        /// <remarks>
        ///     This is the array an index-0 frame decodes against (<c>Class7.java:61</c> reads
        ///     <c>anIntArray3812</c>, which is post-remap), so anything interpreting a frame must use
        ///     this and not <see cref="TransformType"/>.
        /// </remarks>
        public int EffectiveTransformType =>
            TransformType == AliasedTransformType ? AliasTargetTransformType : TransformType;

        /// <summary>
        ///     The per-bone flag byte, exactly as the file stores it.
        /// </summary>
        /// <remarks>
        ///     Raw rather than a <c>bool</c>, for the same reason as <see cref="TransformType"/>: the
        ///     client's test is <c>== 1</c>, so a stored 2 would decode false and re-encode 0. Only 0
        ///     and 1 occur across the 173,749 bones in this cache, so the hazard is latent here and
        ///     costs one field to remove.
        /// </remarks>
        public int Flag { get; set; }

        /// <summary>Whether the client would treat this bone's flag as set.</summary>
        /// <remarks>Gates a separate skeletal path at <c>Renderable.java:721</c>.</remarks>
        public bool IsFlagSet => Flag == FlagSetValue;

        /// <summary>
        ///     The 16-bit mask, ANDed with a caller-supplied mask before the transform runs.
        /// </summary>
        /// <remarks>
        ///     Named no further than that, because that is all the client proves:
        ///     <c>Renderable.java:320,325</c> pass <c>i_29_ &amp; mask</c> into the transform and
        ///     nothing else reads it. It is <c>0xFFFF</c> on all 173,749 bones in this cache, so the
        ///     data cannot distinguish any hypothesis about its bit meanings either.
        /// </remarks>
        public int Mask { get; set; } = 0xFFFF;

        /// <summary>
        ///     The label group ids this bone transforms.
        /// </summary>
        /// <remarks>
        ///     Indices into the model's label tables, and <b>which</b> table depends on the transform
        ///     type: types 0-3 index the vertex label groups (<c>anIntArrayArray4888</c>,
        ///     <c>Renderable_Sub2.java:2806,2837,2853,3018</c>) while type 5 indexes the face label
        ///     groups (<c>anIntArrayArray4870</c>, :3041). Resolving a label without checking the type
        ///     mislabels every alpha bone.
        ///     <para>
        ///     Both the count and each id are stored as unsigned bytes, so 255 is a hard ceiling on
        ///     each; <see cref="SkeletonDefinition.Encode"/> enforces it rather than truncating.
        ///     </para>
        /// </remarks>
        public List<int> Labels { get; } = new List<int>();
    }
}
