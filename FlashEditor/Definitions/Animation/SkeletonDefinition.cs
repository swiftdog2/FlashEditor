using System;
using System.Collections.Generic;
using System.IO;
using FlashEditor.IO;

namespace FlashEditor.Definitions.Animation {
    /// <summary>
    ///     An animation skeleton - the bone table an index-0 keyframe is played against.
    /// </summary>
    /// <remarks>
    ///     JS5 index 1 (<c>RSConstants.SKINS</c>). 3106 groups in the shipped 639 cache, one file per
    ///     group, so the group id is the skeleton id: <c>JS5Archive.method2733</c>
    ///     (<c>JS5Archive.java:591-611</c>) returns <c>getChildFromFolder(id, 0)</c> and throws unless
    ///     the group holds exactly one file. The reference table sets no flags, so a skeleton has no
    ///     name and is addressable by id alone.
    ///     <para>
    ///     The record is <b>not</b> an opcode stream. Everything is sized by the leading bone count,
    ///     so a decoder cannot desynchronise gradually - it either lands exactly on the end of the
    ///     file or it is wrong about a field width. All 3106 files consume exactly.
    ///     </para>
    ///     <para>
    ///     Field order and widths are <c>Node_Sub1.java:87-117</c> verbatim, column-major:
    ///     <c>u8</c> bone count (:87), then <c>u8[n]</c> transform type (:93-99), <c>u8[n]</c> flag
    ///     (:101-103), <c>u16[n]</c> mask (:105-107), <c>u8[n]</c> label count (:109-111) and finally
    ///     the label bytes, all bones' concatenated (:113-117). The two label passes are separate in
    ///     the client and must stay separate here: every count is read before any label is.
    ///     </para>
    ///     <para>
    ///     <b>Editing a skeleton is not a local change.</b> A frame stores its per-bone deltas
    ///     positionally against the skeleton it names, so adding, removing or reordering a bone
    ///     invalidates every index-0 frame that references this id. Changing a mask, a flag or a label
    ///     list in place is safe; changing <see cref="BoneCount"/> is not, unless the referencing
    ///     frames are rewritten too.
    ///     </para>
    /// </remarks>
    public sealed class SkeletonDefinition {
        /// <summary>The most bones a skeleton can hold, because the count is a single byte.</summary>
        /// <remarks>Reached in this cache: the largest skeleton has exactly 255 bones.</remarks>
        public const int MaxBones = 255;

        /// <summary>The largest value any byte-wide field can carry.</summary>
        private const int MaxByte = 255;

        /// <summary>The largest value the 16-bit mask can carry.</summary>
        private const int MaxMask = 0xFFFF;

        /// <summary>The skeleton id, which is also the group id it was read from.</summary>
        public int Id { get; set; } = -1;

        /// <summary>The bones, in the order the file stores them.</summary>
        /// <remarks>
        ///     Order is the identity of a bone: a frame addresses it by position, not by any id in
        ///     the file. Reordering this list re-points every frame that uses the skeleton.
        /// </remarks>
        public List<SkeletonBone> Bones { get; } = new List<SkeletonBone>();

        /// <summary>How many bones the skeleton holds.</summary>
        public int BoneCount => Bones.Count;

        /// <summary>How many label entries the skeleton holds across all its bones.</summary>
        public int TotalLabelCount {
            get {
                int total = 0;
                foreach (SkeletonBone bone in Bones)
                    total += bone.Labels.Count;
                return total;
            }
        }

        /// <summary>
        ///     The transform types with the client's <c>6 -&gt; 2</c> remap applied, indexed by bone.
        /// </summary>
        /// <remarks>
        ///     This is the client's <c>anIntArray3812</c> as a frame decoder sees it
        ///     (<c>Class7.java:61</c>), which is what index 0 needs: the type decides whether a frame's
        ///     value defaults to 0 or 128 and whether it is rescaled into a 14-bit angle, so a frame
        ///     decoded against the raw types would be wrong wherever a 6 occurred.
        ///     <para>
        ///     A method rather than a property because it builds a fresh array on every call, and a
        ///     frame set decodes hundreds of frames against one skeleton - resolve it once.
        ///     </para>
        /// </remarks>
        /// <returns>One entry per bone, in bone order.</returns>
        public int[] GetEffectiveTransformTypes() {
            int[] types = new int[Bones.Count];
            for (int i = 0; i < types.Length; i++)
                types[i] = Bones[i].EffectiveTransformType;
            return types;
        }

        /// <summary>Decodes one skeleton record.</summary>
        /// <param name="stream">The stored file, positioned at its start.</param>
        /// <returns>This definition.</returns>
        /// <exception cref="ArgumentNullException"><paramref name="stream"/> is null.</exception>
        /// <exception cref="EndOfStreamException">The record is shorter than its bone count declares.</exception>
        public SkeletonDefinition Decode(JagStream stream) {
            if (stream == null)
                throw new ArgumentNullException(nameof(stream));

            Bones.Clear();

            int boneCount = stream.ReadUnsignedByte();
            for (int i = 0; i < boneCount; i++)
                Bones.Add(new SkeletonBone());

            for (int i = 0; i < boneCount; i++)
                Bones[i].TransformType = stream.ReadUnsignedByte();

            for (int i = 0; i < boneCount; i++)
                Bones[i].Flag = stream.ReadUnsignedByte();

            for (int i = 0; i < boneCount; i++)
                Bones[i].Mask = stream.ReadUnsignedShort();

            /* Every count first, then every label. The client sizes all the arrays in one loop
               (Node_Sub1.java:109-111) and fills them in a second (:113-117); interleaving the two
               would read the first bone's labels out of the count block. */
            int[] labelCounts = new int[boneCount];
            for (int i = 0; i < boneCount; i++)
                labelCounts[i] = stream.ReadUnsignedByte();

            for (int i = 0; i < boneCount; i++) {
                List<int> labels = Bones[i].Labels;
                for (int label = 0; label < labelCounts[i]; label++)
                    labels.Add(stream.ReadUnsignedByte());
            }

            return this;
        }

        /// <summary>Encodes this skeleton back to its stored representation.</summary>
        /// <remarks>
        ///     Byte-identical for an unedited skeleton, because nothing in the format has more than
        ///     one representation and every field is kept as the stored byte. The two normalisations
        ///     the client applies at load - transform type 6 folded onto 2, and the flag byte folded
        ///     onto a bool - happen on <see cref="SkeletonBone.EffectiveTransformType"/> and
        ///     <see cref="SkeletonBone.IsFlagSet"/>, which this never reads.
        /// </remarks>
        /// <returns>The encoded file, positioned at 0.</returns>
        /// <exception cref="InvalidOperationException">
        ///     A field has been edited to a value the format cannot express. Reported rather than
        ///     truncated: a silently masked 256 writes a 0 and moves a bone onto a different label
        ///     group, which no sweep over unedited data would ever see.
        /// </exception>
        public JagStream Encode() {
            if (Bones.Count > MaxBones)
                throw new InvalidOperationException(
                    "Skeleton " + Id + " has " + Bones.Count + " bones, and the count is stored as a " +
                    "single byte, so at most " + MaxBones + " fit.");

            JagStream stream = new JagStream();
            stream.WriteByte(Bones.Count);

            for (int i = 0; i < Bones.Count; i++)
                stream.WriteByte(Checked(Bones[i].TransformType, MaxByte, "transform type", i));

            for (int i = 0; i < Bones.Count; i++)
                stream.WriteByte(Checked(Bones[i].Flag, MaxByte, "flag", i));

            for (int i = 0; i < Bones.Count; i++)
                stream.WriteShort(Checked(Bones[i].Mask, MaxMask, "mask", i));

            for (int i = 0; i < Bones.Count; i++)
                stream.WriteByte(Checked(Bones[i].Labels.Count, MaxByte, "label count", i));

            for (int i = 0; i < Bones.Count; i++) {
                List<int> labels = Bones[i].Labels;
                for (int label = 0; label < labels.Count; label++)
                    stream.WriteByte(Checked(labels[label], MaxByte, "label " + label, i));
            }

            return stream.Flip();
        }

        /// <summary>Rejects a field an edit has pushed outside what the format can store.</summary>
        /// <param name="value">The value to write.</param>
        /// <param name="max">The largest value its field can hold.</param>
        /// <param name="what">The field, for the message.</param>
        /// <param name="boneIndex">The bone it belongs to, for the message.</param>
        /// <returns>The value, when it fits.</returns>
        private int Checked(int value, int max, string what, int boneIndex) {
            if (value < 0 || value > max)
                throw new InvalidOperationException(
                    "Skeleton " + Id + " bone " + boneIndex + ": " + what + " is " + value +
                    ", which does not fit the 0.." + max + " the format stores it in.");
            return value;
        }
    }
}
