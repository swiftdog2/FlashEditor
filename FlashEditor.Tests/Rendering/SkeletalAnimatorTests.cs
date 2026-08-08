using System;
using System.Collections.Generic;
using System.Linq;
using FlashEditor.Definitions;
using FlashEditor.Definitions.Animation;
using FlashEditor.Rendering;
using Xunit;

namespace FlashEditor.Tests.Rendering
{
    /// <summary>
    ///     Composes an animation, a frame and a skeleton into a pose, and checks the numbers a panel
    ///     will print.
    /// </summary>
    /// <remarks>
    ///     The three indexes only mean anything together: index 20 gives the running order, index 0
    ///     gives the per-slot values, and the index-1 skeleton the frame names gives the transform
    ///     type and the labels. A join that looks plausible is the easiest thing in this cache to get
    ///     wrong by accident, so the expected coordinates below are worked out geometrically and
    ///     stated, not read off a run.
    /// </remarks>
    public class SkeletalAnimatorTests
    {
        /// <summary>Index-0 group the test frames sit in.</summary>
        private const int FrameSet = 7;

        /// <summary>Index-1 group the test skeleton sits in.</summary>
        private const int SkeletonId = 3;

        /// <summary>A quarter turn in the client's angle unit.</summary>
        private const int QuarterTurn = SkeletalTrig.AngleSteps / 4;

        /// <summary>
        ///     The stored rotation value is a quarter of the angle, because the frame decoder shifts
        ///     it left by two.
        /// </summary>
        /// <remarks><c>Class7.java:91-95</c>: <c>value &lt;&lt; 2 &amp; 0x3fff</c> for types 2 and 9.</remarks>
        private const int StoredQuarterTurn = QuarterTurn >> 2;

        /// <summary>
        ///     A rotation slot turns its vertices about the pivot the skeleton's type-0 bone defines.
        /// </summary>
        /// <remarks>
        ///     Vertex 1 sits at x = 100 and the pivot bone owns vertex 0 at x = 40, so a quarter turn
        ///     about z swings vertex 1 from 60 along x to 60 along negative y, landing on (40, -60, 0).
        ///     <para>
        ///     The pivot is reached the hard way on purpose: the frame's type-0 slot carries a flag
        ///     byte of zero, so the client skips it and the <i>next</i> slot claims it instead,
        ///     re-deriving the centroid with zero offsets (<c>Class7.java:96-101</c> paired with
        ///     <c>Renderable.java:718-726</c>). A pose that resolved the pivot any other way would
        ///     rotate about the origin and put vertex 1 at (0, -100, 0).
        ///     </para>
        /// </remarks>
        [Fact]
        public void Pose_RotatesAboutThePivotTheSkeletonDefines()
        {
            ModelDefinition model = TwoVertexModel();
            SkeletonDefinition skeleton = PivotThenRotateSkeleton();
            FrameDefinition frame = Frame(
                slot0Flag: 0x00,
                rotationFlag: FrameTransform.ZPresent,
                rotationZ: StoredQuarterTurn);

            SkeletalAnimator animator = Animator(model, skeleton, frame);

            Assert.Null(animator.LastError);
            Assert.True(animator.HasPose);

            PosedMesh pose = animator.Poses[0];
            Assert.Equal(new[] { 40, 40 }, pose.VertexX);
            Assert.Equal(new[] { 0, -60 }, pose.VertexY);
            Assert.Equal(new[] { 0, 0 }, pose.VertexZ);
        }

        /// <summary>
        ///     A frame slot the flag byte marks empty contributes nothing but is still counted.
        /// </summary>
        /// <remarks>
        ///     Around half a shipped frame's slots are empty, so the two counts differing is the
        ///     normal case. Both are printed beside the viewport because their <i>ratio</i> is what
        ///     says whether a frame is doing anything.
        /// </remarks>
        [Fact]
        public void Diagnostics_SeparateDeclaredSlotsFromResolvedOnes()
        {
            SkeletalAnimator animator = Animator(TwoVertexModel(), PivotThenRotateSkeleton(),
                Frame(slot0Flag: 0x00, rotationFlag: FrameTransform.ZPresent, rotationZ: StoredQuarterTurn));

            Assert.Equal(2, animator.FrameTransformCount);
            Assert.Equal(1, animator.ResolvedTransformCount);
            Assert.Equal(1, animator.AppliedTransformCount);
            Assert.Equal(0, animator.NoTargetTransformCount);
            Assert.Equal(0, animator.UnsupportedTransformCount);
            Assert.Equal(0, animator.PartialMaskBoneCount);
            Assert.Equal(SkeletonId, animator.SkeletonId);
            Assert.Equal(2, animator.BoneCount);
        }

        /// <summary>
        ///     A pose that reaches nothing says so instead of looking like a still frame.
        /// </summary>
        /// <remarks>
        ///     This is the failure the whole diagnostics surface exists for. A model with no vertex
        ///     skins cannot be moved by any bone, and on screen that is indistinguishable from an
        ///     animation that happens to hold still. The status line has to be the thing that tells
        ///     them apart, because no capture on this machine can show the viewport.
        /// </remarks>
        [Fact]
        public void Status_NamesAPoseThatReachedNothing()
        {
            ModelDefinition model = TwoVertexModel();
            model.VertSkins = null;
            model.VertexGroups = null;

            SkeletalAnimator animator = Animator(model, PivotThenRotateSkeleton(),
                Frame(slot0Flag: 0x00, rotationFlag: FrameTransform.ZPresent, rotationZ: StoredQuarterTurn));

            Assert.Equal(0, animator.AppliedTransformCount);
            Assert.Equal(1, animator.NoTargetTransformCount);
            Assert.Contains("no transform reached this model", animator.Status);
            Assert.Equal(new[] { 40, 100 }, animator.Poses[0].VertexX);
        }

        /// <summary>A missing frame or skeleton is reported by name rather than thrown.</summary>
        /// <remarks>
        ///     A render tick is the wrong place to throw, and "index 1 has no skeleton 3" is a
        ///     sentence a human can act on where an empty viewport is not.
        /// </remarks>
        [Fact]
        public void MissingData_IsReportedInTheStatusLine()
        {
            var noSkeleton = new InMemoryAnimationDataSource()
                .AddFrame(AnimationDefinition.PackFrame(FrameSet, 0),
                    Frame(slot0Flag: 0x00, rotationFlag: FrameTransform.ZPresent, rotationZ: StoredQuarterTurn));

            var animator = new SkeletalAnimator(noSkeleton);
            animator.SetModels(new[] { TwoVertexModel() });
            animator.Play(OneFrameAnimation());

            Assert.False(animator.HasPose);
            Assert.Contains("no skeleton " + SkeletonId, animator.Status);

            var nothing = new InMemoryAnimationDataSource();
            var second = new SkeletalAnimator(nothing);
            second.SetModels(new[] { TwoVertexModel() });
            second.Play(OneFrameAnimation());

            Assert.False(second.HasPose);
            Assert.Contains("no frame 0 in group " + FrameSet, second.Status);
        }

        /// <summary>
        ///     Stopping the animation puts the mesh back exactly where the rest model has it.
        /// </summary>
        /// <remarks>
        ///     The pose is absolute against the rest mesh rather than a delta on the previous frame,
        ///     so a reset is a copy and cannot drift. Asserted because the promotion to sixteenths and
        ///     the reduction back are lossy in principle - <c>(v &lt;&lt; 4) + 7 &gt;&gt; 4</c> is only
        ///     the identity because the bias is under a whole unit.
        /// </remarks>
        [Fact]
        public void StoppingPutsTheMeshBackToRest()
        {
            SkeletalAnimator animator = Animator(TwoVertexModel(), PivotThenRotateSkeleton(),
                Frame(slot0Flag: 0x00, rotationFlag: FrameTransform.ZPresent, rotationZ: StoredQuarterTurn));

            Assert.Equal(new[] { 40, 40 }, animator.Poses[0].VertexX);

            animator.Play(null);

            Assert.False(animator.HasPose);
            Assert.Equal(new[] { 40, 100 }, animator.Poses[0].VertexX);
            Assert.Equal(new[] { 0, 0 }, animator.Poses[0].VertexY);
        }

        /// <summary>
        ///     Every diagnostic a panel needs is present and none of them is blank.
        /// </summary>
        /// <remarks>
        ///     A weak assertion deliberately: the values are pinned by the tests above, and what this
        ///     one guards is that the rows a GUI phase will bind to keep existing.
        /// </remarks>
        [Fact]
        public void Diagnostics_CarryTheRowsAPanelBindsTo()
        {
            SkeletalAnimator animator = Animator(TwoVertexModel(), PivotThenRotateSkeleton(),
                Frame(slot0Flag: 0x00, rotationFlag: FrameTransform.ZPresent, rotationZ: StoredQuarterTurn));

            string[] names = animator.Diagnostics.Select(row => row.Key).ToArray();

            Assert.Contains("Frame index", names);
            Assert.Contains("Frame id", names);
            Assert.Contains("Elapsed", names);
            Assert.Contains("Resolved transforms", names);
            Assert.Contains("Applied transforms", names);
            Assert.All(animator.Diagnostics, row => Assert.False(string.IsNullOrWhiteSpace(row.Value)));
        }

        /// <summary>
        ///     A part carrying none of the pivot bone's labels still turns about the body's centre.
        /// </summary>
        /// <remarks>
        ///     ROOT 1 of the detached-limb defect, pinned without a cache and without a seam. The two
        ///     parts here share no coordinate at all, so welding cannot account for the result - only
        ///     the merge can. The client merges before it poses (<c>Class141.java:801</c>) and sums the
        ///     pivot centroid over the merged model (<c>Renderable_Sub2.java:2803-2827</c>).
        ///     <para>
        ///     Same arithmetic as <see cref="Pose_RotatesAboutThePivotTheSkeletonDefines"/>, split
        ///     across two model files. The pivot bone owns label 0, which lives entirely in the first
        ///     part; the second part carries only label 1 and is the one that rotates. Posed as a body,
        ///     the pivot is (40,0,0) and the quarter turn swings the second part's vertex from 60 along
        ///     x to 60 along negative y, landing on (40,-60,0).
        ///     </para>
        ///     <para>
        ///     Posed a part at a time - which is what this animator did until 2026-08-09 - the second
        ///     part finds no label-0 vertex of its own, falls back to the bare offset
        ///     (<c>Renderable_Sub2.java:2820-2823</c>) and rotates about the model origin, landing on
        ///     (0,-100,0). That is the jaw leaving the face, reduced to one vertex.
        ///     </para>
        /// </remarks>
        [Fact]
        public void Pose_TurnsAPartAboutTheWholeBodysPivotRatherThanItsOwn()
        {
            SkeletalAnimator animator = Animator(
                new[] { OneVertexModel(40, label: 0), OneVertexModel(100, label: 1) },
                PivotThenRotateSkeleton(),
                Frame(slot0Flag: 0x00, rotationFlag: FrameTransform.ZPresent, rotationZ: StoredQuarterTurn));

            Assert.Null(animator.LastError);
            Assert.True(animator.IsMerged);

            //Two parts of one vertex each, sharing no coordinate, so nothing welds.
            Assert.Equal(2, animator.MergedVertexCount);
            Assert.Equal(0, animator.WeldedVertexCount);

            Assert.Equal(new[] { 40 }, animator.Poses[0].VertexX);
            Assert.Equal(new[] { 0 }, animator.Poses[0].VertexY);

            Assert.Equal(new[] { 40 }, animator.Poses[1].VertexX);
            Assert.Equal(new[] { -60 }, animator.Poses[1].VertexY);
        }

        /// <summary>
        ///     Two parts meeting at a coordinate stay one point, and the first part's label wins.
        /// </summary>
        /// <remarks>
        ///     ROOT 2. <c>Model.method2598</c> (<c>Model.java:1824-1848</c>) reuses a vertex already
        ///     placed at the same coordinate and rewrites only its source mask, never its label
        ///     (<c>:1841</c>), so a seam whose two parts disagree about which bone owns it follows the
        ///     first one rather than being pulled apart by both.
        ///     <para>
        ///     Both parts here carry a vertex at x = 100. The first labels it 1, which the rotate bone
        ///     owns; the second labels it 0, which only the pivot bone owns. Welded, there is one
        ///     vertex, it keeps label 1, and both parts read back (40,-60,0). Unwelded, the second
        ///     part's copy is untouched at (100,0,0) and the seam opens by 100 units.
        ///     </para>
        /// </remarks>
        [Fact]
        public void Pose_WeldsACoincidentVertexAndKeepsTheFirstPartsLabel()
        {
            SkeletalAnimator animator = Animator(
                new[] { TwoVertexModel(), OneVertexModel(100, label: 0) },
                PivotThenRotateSkeleton(),
                Frame(slot0Flag: 0x00, rotationFlag: FrameTransform.ZPresent, rotationZ: StoredQuarterTurn));

            Assert.Null(animator.LastError);
            Assert.True(animator.IsMerged);

            //Three source vertices, two of them at x = 100, so one weld and two vertices left.
            Assert.Equal(2, animator.MergedVertexCount);
            Assert.Equal(1, animator.WeldedVertexCount);

            Assert.Equal(new[] { 40 }, animator.Poses[1].VertexX);
            Assert.Equal(new[] { -60 }, animator.Poses[1].VertexY);
            Assert.Equal(animator.Poses[0].VertexX[1], animator.Poses[1].VertexX[0]);
            Assert.Equal(animator.Poses[0].VertexY[1], animator.Poses[1].VertexY[0]);
        }

        /// <summary>
        ///     One model is posed unmerged, which is what the client does.
        /// </summary>
        /// <remarks>
        ///     <c>Class141.java:801</c> takes <c>models[0]</c> untouched when there is only one.
        ///     Merging it anyway would weld its own coincident vertices, and index 7 is full of models
        ///     that have them, so every static model in the viewer would start posing differently on a
        ///     change that was only ever about entities.
        /// </remarks>
        [Fact]
        public void Pose_LeavesASingleModelUnmerged()
        {
            SkeletalAnimator animator = Animator(TwoVertexModel(), PivotThenRotateSkeleton(),
                Frame(slot0Flag: 0x00, rotationFlag: FrameTransform.ZPresent, rotationZ: StoredQuarterTurn));

            Assert.False(animator.IsMerged);
            Assert.Equal(0, animator.WeldedVertexCount);
        }

        /// <summary>One vertex on the x axis, in a label group of its own.</summary>
        /// <param name="x">Where on the x axis.</param>
        /// <param name="label">The vertex label a skeleton bone would name.</param>
        /// <returns>The model.</returns>
        private static ModelDefinition OneVertexModel(int x, int label) => new ModelDefinition
        {
            VertX = new[] { x },
            VertY = new[] { 0 },
            VertZ = new[] { 0 },
            VertSkins = new[] { label },
            faceIndices1 = new[] { 0 },
            faceIndices2 = new[] { 0 },
            faceIndices3 = new[] { 0 }
        };

        /// <summary>Two vertices in their own label groups, 40 and 100 along x.</summary>
        /// <returns>The model.</returns>
        private static ModelDefinition TwoVertexModel() => new ModelDefinition
        {
            VertX = new[] { 40, 100 },
            VertY = new[] { 0, 0 },
            VertZ = new[] { 0, 0 },
            VertSkins = new[] { 0, 1 },
            faceIndices1 = new[] { 0 },
            faceIndices2 = new[] { 1 },
            faceIndices3 = new[] { 0 }
        };

        /// <summary>Bone 0 is the pivot over label 0; bone 1 rotates label 1.</summary>
        /// <returns>The skeleton.</returns>
        private static SkeletonDefinition PivotThenRotateSkeleton()
        {
            var skeleton = new SkeletonDefinition { Id = SkeletonId };

            var pivot = new SkeletonBone { TransformType = PosedMesh.TypePivot };
            pivot.Labels.Add(0);
            skeleton.Bones.Add(pivot);

            var rotate = new SkeletonBone { TransformType = PosedMesh.TypeRotate };
            rotate.Labels.Add(1);
            skeleton.Bones.Add(rotate);

            return skeleton;
        }

        /// <summary>A two-slot frame against <see cref="PivotThenRotateSkeleton"/>.</summary>
        /// <param name="slot0Flag">The pivot slot's flag byte. Zero makes the client skip it.</param>
        /// <param name="rotationFlag">The rotation slot's flag byte.</param>
        /// <param name="rotationZ">The stored z value, before the decoder's shift into an angle.</param>
        /// <returns>The frame.</returns>
        private static FrameDefinition Frame(int slot0Flag, int rotationFlag, int rotationZ)
        {
            var frame = new FrameDefinition { SkeletonId = SkeletonId };
            frame.Transforms.Add(new FrameTransform { Flag = slot0Flag });
            frame.Transforms.Add(new FrameTransform
            {
                Flag = rotationFlag,
                Z = new FrameValue(rotationZ)
            });
            return frame;
        }

        /// <summary>An animation whose only step plays frame set 7, file 0.</summary>
        /// <returns>The animation.</returns>
        private static AnimationDefinition OneFrameAnimation() => new AnimationDefinition
        {
            Id = 1,
            FrameDurations = new[] { 4 },
            FrameIds = new[] { AnimationDefinition.PackFrame(FrameSet, 0) }
        };

        /// <summary>Builds an animator over one model, one skeleton and one frame, already posed.</summary>
        /// <param name="model">The model to animate.</param>
        /// <param name="skeleton">The skeleton the frame names.</param>
        /// <param name="frame">The frame.</param>
        /// <returns>The animator, with its first frame posed.</returns>
        private static SkeletalAnimator Animator(ModelDefinition model, SkeletonDefinition skeleton,
            FrameDefinition frame)
        {
            return Animator(new[] { model }, skeleton, frame);
        }

        /// <summary>Builds an animator over a set of models, one skeleton and one frame, already posed.</summary>
        /// <remarks>
        ///     More than one model is the entity case, and the animator merges them before posing.
        ///     Passing one leaves it unmerged, which is the client's own condition
        ///     (<c>Class141.java:801</c>) and is why the two overloads are not the same call.
        /// </remarks>
        /// <param name="models">The models, in the order the viewport would upload them.</param>
        /// <param name="skeleton">The skeleton the frame names.</param>
        /// <param name="frame">The frame.</param>
        /// <returns>The animator, with its first frame posed.</returns>
        private static SkeletalAnimator Animator(ModelDefinition[] models, SkeletonDefinition skeleton,
            FrameDefinition frame)
        {
            var source = new InMemoryAnimationDataSource()
                .AddFrame(AnimationDefinition.PackFrame(FrameSet, 0), frame)
                .AddSkeleton(SkeletonId, skeleton);

            var animator = new SkeletalAnimator(source);
            animator.SetModels(models);
            animator.Play(OneFrameAnimation());
            return animator;
        }
    }
}
