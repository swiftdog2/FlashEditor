using System.Collections.Generic;
using System.Globalization;
using System;
using FlashEditor.Definitions.Animation;
using FlashEditor.Definitions;
using FlashEditor.Definitions.Models;

namespace FlashEditor.Rendering
{
    /// <summary>
    ///     Joins an index-20 animation, an index-0 frame and an index-1 skeleton into a posed mesh.
    /// </summary>
    /// <remarks>
    ///     The three indexes only mean anything together. Index 20 gives the running order and the
    ///     durations, index 0 gives the per-slot values for one frame, and the index-1 skeleton the
    ///     frame names supplies both the transform type of each slot and the labels it moves. Nothing
    ///     is decodable into a pose on its own.
    ///     <para>
    ///     Everything below the status line is <see cref="AnimationPlayer"/>'s and
    ///     <see cref="PosedMesh"/>'s; what this type adds is the join, and the counts that say whether
    ///     the join worked. That matters more than it sounds. A model with no vertex skins cannot be
    ///     moved by any bone, and on screen that is indistinguishable from an animation that happens
    ///     to hold still - nothing in this suite can see the GL surface, and on this machine nothing
    ///     can. So <see cref="Status"/> and <see cref="Diagnostics"/> are the thing that tells them
    ///     apart, and they are load-bearing rather than decoration.
    ///     </para>
    ///     <para>
    ///     Nothing here throws on missing data. A frame the cache does not hold and a skeleton it does
    ///     not hold are both reported by name into <see cref="LastError"/>, because a render tick is
    ///     the wrong place to throw and "index 1 has no skeleton 3" is a sentence someone can act on
    ///     where an empty viewport is not.
    ///     </para>
    /// </remarks>
    public sealed class SkeletalAnimator
    {
        /// <summary>A bone mask with every bit set, meaning no caller filter can exclude it.</summary>
        /// <remarks>
        ///     The client ANDs a caller-supplied mask with the bone's own before deciding whether to
        ///     apply a slot (<c>Renderable.java:325</c>). Nothing here supplies one, so a partial mask
        ///     changes nothing - it is counted rather than acted on, so a frame that would behave
        ///     differently under a filter can be spotted.
        /// </remarks>
        public const int FullMask = 65535;

        /// <summary>Where frames and skeletons come from. A cache, or a hand-built set in a test.</summary>
        private readonly IAnimationDataSource source;

        /// <summary>The models being animated, with their label groups inverted.</summary>
        private readonly List<SkinnedModel> models = new List<SkinnedModel>();

        /// <summary>One pose per model, in the same order, reused every frame.</summary>
        private readonly List<PosedMesh> poses = new List<PosedMesh>();

        /// <summary>
        ///     The parts joined into one mesh, or null when there is nothing to join.
        /// </summary>
        /// <remarks>
        ///     Null for zero or one model, which is the client's own condition -
        ///     <c>Class141.java:801</c> merges only when <c>models.length != 1</c>. Merging a lone
        ///     model would weld its own coincident vertices and change how every static model poses.
        /// </remarks>
        private CompositeModel? composite;

        /// <summary>The pose of <see cref="composite"/>, which is what the transforms are applied to.</summary>
        private PosedMesh? compositePose;

        /// <summary>Whatever the frame's transforms are applied to: the merged body, or the one model.</summary>
        /// <remarks>
        ///     Aliased rather than rebuilt per frame, and it is either a single-element list holding
        ///     <see cref="compositePose"/> or <see cref="poses"/> itself.
        /// </remarks>
        private IReadOnlyList<PosedMesh> targets = Array.Empty<PosedMesh>();

        /// <summary>Whether the poses need rebuilding even though the playhead has not moved.</summary>
        /// <remarks>
        ///     Set when the models or the animation change. Without it, loading a new model while an
        ///     animation is paused would leave the old pose on screen until something advanced the
        ///     playhead.
        /// </remarks>
        private bool poseIsStale = true;

        /// <summary>The playhead. Exposed so a transport panel can drive it directly.</summary>
        public AnimationPlayer Player { get; } = new AnimationPlayer();

        /// <summary>The models being animated.</summary>
        public IReadOnlyList<SkinnedModel> Models => models;

        /// <summary>The current pose of each model, in the same order as <see cref="Models"/>.</summary>
        /// <remarks>
        ///     Live buffers rather than copies - <see cref="PickMesh.ApplyPose"/> and
        ///     <see cref="ParticleSystem.ApplyPose"/> read straight out of them every frame. They are
        ///     rewritten in place by <see cref="RefreshPose"/>, so a caller must not hold a reference
        ///     to their contents across a frame.
        /// </remarks>
        public IReadOnlyList<PosedMesh> Poses => poses;

        /// <summary>Whether <see cref="Poses"/> currently holds a real frame's pose.</summary>
        public bool HasPose { get; private set; }

        /// <summary>The index-1 skeleton the current frame names, or -1.</summary>
        public int SkeletonId { get; private set; } = -1;

        /// <summary>How many bones that skeleton declares.</summary>
        public int BoneCount { get; private set; }

        /// <summary>How many slots the frame record declares, before resolution.</summary>
        /// <remarks>
        ///     Around half of a shipped frame's slots carry a flag byte of zero and contribute
        ///     nothing, so this being well above <see cref="ResolvedTransformCount"/> is the normal
        ///     case. The <i>ratio</i> is what says whether a frame is doing anything, which is why
        ///     both are shown.
        /// </remarks>
        public int FrameTransformCount { get; private set; }

        /// <summary>How many of those slots resolved into a transform with values.</summary>
        public int ResolvedTransformCount { get; private set; }

        /// <summary>How many resolved transforms moved something on at least one model.</summary>
        public int AppliedTransformCount { get; private set; }

        /// <summary>How many resolved transforms named labels no loaded model carries.</summary>
        /// <remarks>The count that distinguishes "posed and still" from "posed and reached nothing".</remarks>
        public int NoTargetTransformCount { get; private set; }

        /// <summary>How many resolved transforms were of a type this viewer does not simulate.</summary>
        /// <remarks>See <see cref="TransformOutcome.Unsupported"/>.</remarks>
        public int UnsupportedTransformCount { get; private set; }

        /// <summary>How many applied bones carry a mask short of <see cref="FullMask"/>.</summary>
        public int PartialMaskBoneCount { get; private set; }

        /// <summary>Whether the models were joined into one mesh before posing.</summary>
        /// <remarks>
        ///     False for a single model, which the client does not merge either
        ///     (<c>Class141.java:801</c>). Exposed because "was this posed as a body or as a pile of
        ///     parts" is the difference between a jaw that stays on and one that does not, and nothing
        ///     on this machine can see the viewport to tell.
        /// </remarks>
        public bool IsMerged => composite != null;

        /// <summary>Vertices in the merged mesh, or in the one model when nothing was merged.</summary>
        public int MergedVertexCount => composite?.Model.VertX.Length
            ?? (models.Count == 1 ? models[0].Model.VertX.Length : 0);

        /// <summary>How many of the parts' vertices were welded onto one an earlier part had placed.</summary>
        /// <remarks>
        ///     The seam count. Zero on a set whose parts share no coordinate, in which case merging
        ///     changed the pivots and nothing else.
        /// </remarks>
        public int WeldedVertexCount => composite?.WeldedVertexCount ?? 0;

        /// <summary>What went wrong with the last pose attempt, or null.</summary>
        public string? LastError { get; private set; }

        /// <summary>One line describing what the animator is doing, for the status bar.</summary>
        /// <remarks>
        ///     The order of the tests is the order the failures happen in, so the first thing that is
        ///     wrong is what gets reported. The <c>AppliedTransformCount == 0</c> case is the one this
        ///     property exists for: a frame that resolved transforms and moved nothing is the failure
        ///     that looks exactly like success.
        /// </remarks>
        public string Status
        {
            get
            {
                if (LastError != null)
                {
                    return LastError;
                }

                if (models.Count == 0)
                {
                    return "No model loaded.";
                }

                if (Player.Animation == null)
                {
                    return "No animation loaded.";
                }

                if (!HasPose)
                {
                    return "Animation loaded, no frame posed yet.";
                }

                if (AppliedTransformCount == 0)
                {
                    return "Frame " + Player.FrameIndex + " posed, but no transform reached this model - "
                        + ResolvedTransformCount + " resolved, " + NoTargetTransformCount + " matched no label.";
                }

                return "Frame " + Player.FrameIndex + " of " + Player.FrameCount + ", "
                    + AppliedTransformCount + " of " + ResolvedTransformCount + " transforms applied.";
            }
        }

        /// <summary>Name and value rows for the diagnostics panel.</summary>
        /// <remarks>
        ///     Built fresh on each read and formatted with the invariant culture, because these are
        ///     ids and counts rather than presentation numbers - a frame id that picked up a thousands
        ///     separator is not searchable and not quotable in a bug report.
        /// </remarks>
        public IReadOnlyList<KeyValuePair<string, string>> Diagnostics
        {
            get
            {
                List<KeyValuePair<string, string>> rows = new List<KeyValuePair<string, string>>();

                Row("State", Player.State);
                Row("Frame index", Player.FrameIndex + " of " + Player.FrameCount);
                Row("Frame id", Player.PackedFrameId == -1
                    ? "none"
                    : Player.PackedFrameId + " (group " + Player.FrameGroup + ", file " + Player.FrameFileId + ")");
                Row("Secondary frame id", Player.SecondaryPackedFrameId == -1
                    ? "none"
                    : Player.SecondaryPackedFrameId.ToString(CultureInfo.InvariantCulture));
                Row("Frame duration", Player.CyclesIntoFrame + " of " + Player.CurrentFrameDuration + " cycles");
                Row("Elapsed", Player.ElapsedSeconds.ToString("0.000", CultureInfo.InvariantCulture)
                    + " s (" + Player.ElapsedCycles + " cycles of " + AnimationPlayer.CycleMilliseconds + " ms)");
                Row("Animation length", Player.TotalSeconds.ToString("0.000", CultureInfo.InvariantCulture) + " s");
                Row("Loops completed", Player.LoopsCompleted);
                Row("Dropped cycles", Player.DroppedCycles);
                Row("Skeleton", SkeletonId == -1 ? "none" : SkeletonId + " (" + BoneCount + " bones)");
                Row("Frame transforms", FrameTransformCount);
                Row("Resolved transforms", ResolvedTransformCount);
                Row("Applied transforms", AppliedTransformCount);
                Row("Transforms with no target", NoTargetTransformCount);
                Row("Unsupported transforms", UnsupportedTransformCount);
                Row("Partial-mask bones", PartialMaskBoneCount);
                Row("Models", models.Count);
                Row("Posed as", IsMerged
                    ? "one merged mesh of " + MergedVertexCount + " vertices, " + WeldedVertexCount + " welded"
                    : models.Count == 1 ? "a single model, unmerged" : "nothing");

                return rows;

                void Row(string name, object value)
                {
                    rows.Add(new KeyValuePair<string, string>(
                        name, Convert.ToString(value, CultureInfo.InvariantCulture) ?? ""));
                }
            }
        }

        /// <summary>Creates an animator reading frames and skeletons from a source.</summary>
        /// <param name="source">Where to read index-0 frames and index-1 skeletons from.</param>
        /// <exception cref="ArgumentNullException"><paramref name="source"/> is null.</exception>
        public SkeletalAnimator(IAnimationDataSource source)
        {
            this.source = source ?? throw new ArgumentNullException(nameof(source));
        }

        /// <summary>Replaces the set of models being animated.</summary>
        /// <remarks>
        ///     A set rather than one model, because an entity is several: a player is a head, a torso
        ///     and so on, all driven by one skeleton.
        ///     <para>
        ///     <b>They are posed as one mesh, not one at a time.</b> That is the client's behaviour -
        ///     <c>Class141.java:801</c> builds <c>new Model(models, models.length)</c> whenever there
        ///     is more than one, and <c>Node_Sub3.java:172</c> does the same for equipped models - and
        ///     it is not a detail. A pivot bone's centroid is summed over the whole body
        ///     (<c>Renderable_Sub2.java:2803-2827</c>), so posing each part against its own vertices
        ///     gives every part a different rotation centre, and a part carrying none of the pivot
        ///     bone's labels falls back to the model origin on the floor between the feet. This class
        ///     built one pose per model until 2026-08-09, which is why an NPC's jaw came off its face
        ///     and its hands off its arms. See <see cref="CompositeModel"/> for the merge and the
        ///     measurements.
        ///     </para>
        ///     <para>
        ///     <see cref="Poses"/> still holds one entry per model in the order given. The merged pose
        ///     is read back out through the composite's vertex map, so the renderer, the picker, the
        ///     particle system and the hover overlay keep addressing a vertex as "model m, vertex v".
        ///     </para>
        /// </remarks>
        /// <param name="definitions">The models.</param>
        /// <exception cref="ArgumentNullException"><paramref name="definitions"/> is null.</exception>
        public void SetModels(IEnumerable<ModelDefinition> definitions)
        {
            if (definitions == null)
            {
                throw new ArgumentNullException(nameof(definitions));
            }

            models.Clear();
            poses.Clear();
            composite = null;
            compositePose = null;

            List<ModelDefinition> parts = new List<ModelDefinition>();

            foreach (ModelDefinition definition in definitions)
            {
                SkinnedModel skinned = new SkinnedModel(definition);
                models.Add(skinned);
                poses.Add(skinned.CreatePose());
                parts.Add(definition);
            }

            if (parts.Count > 1)
            {
                composite = new CompositeModel(parts);
                compositePose = composite.Skin.CreatePose();
                targets = new[] { compositePose };
            }
            else
            {
                targets = poses;
            }

            HasPose = false;
            poseIsStale = true;
        }

        /// <summary>Loads an animation, starts it, and poses its first frame at once.</summary>
        /// <remarks>
        ///     Posing immediately rather than waiting for the first <see cref="Advance"/> is what makes
        ///     a paused preview show something. Passing null clears the animation, and the pose then
        ///     goes back to the rest mesh.
        /// </remarks>
        /// <param name="animation">The animation, or null to clear.</param>
        public void Play(AnimationDefinition? animation)
        {
            Player.Play(animation);
            poseIsStale = true;
            HasPose = false;
            RefreshPose();
        }

        /// <summary>Advances the playhead by elapsed wall-clock time and re-poses if the step moved.</summary>
        /// <param name="seconds">Wall-clock time since the last call.</param>
        /// <returns><c>true</c> when the poses changed and a redraw is worth doing.</returns>
        public bool Advance(double seconds)
        {
            //Most cycles advance the counter without changing the step, and re-posing on those would
            //redo every transform on every model for an identical result.
            if (!Player.Advance(seconds) && !poseIsStale)
            {
                return false;
            }

            return RefreshPose();
        }

        /// <summary>Rebuilds every pose from the frame the playhead is currently on.</summary>
        /// <remarks>
        ///     Every exit resets the poses to rest first and calls <see cref="PosedMesh.Finish"/> on
        ///     the way out, including the failure paths. Leaving a mesh promoted to sixteenths after an
        ///     early return would hand a mesh sixteen times too large to the picker and the uploader,
        ///     which is a spectacular and confusing failure for a cause as ordinary as a missing frame.
        /// </remarks>
        /// <returns><c>true</c> when a real frame was posed.</returns>
        public bool RefreshPose()
        {
            poseIsStale = false;
            LastError = null;
            SkeletonId = -1;
            BoneCount = 0;
            FrameTransformCount = 0;
            ResolvedTransformCount = 0;
            AppliedTransformCount = 0;
            NoTargetTransformCount = 0;
            UnsupportedTransformCount = 0;
            PartialMaskBoneCount = 0;

            foreach (PosedMesh pose in poses)
            {
                pose.Reset();
            }

            //Reset apart from the loop above, because the merged pose is not one of the per-part ones
            //and a frame that returns early has to leave both at rest.
            compositePose?.Reset();

            int packedFrameId = Player.PackedFrameId;

            //Nothing loaded, or the playhead is off the end. Not an error - it is the state a viewer
            //sits in before anything is chosen.
            if (models.Count == 0 || packedFrameId == -1)
            {
                Finish();
                HasPose = false;
                return false;
            }

            FrameDefinition? frame = source.GetFrame(packedFrameId);

            if (frame == null)
            {
                LastError = "Index 0 has no frame " + AnimationDefinition.FrameIndexOf(packedFrameId)
                    + " in group " + AnimationDefinition.FrameGroupOf(packedFrameId) + ".";
                Finish();
                HasPose = false;
                return false;
            }

            SkeletonId = frame.SkeletonId;
            FrameTransformCount = frame.TransformCount;

            SkeletonDefinition? skeleton = source.GetSkeleton(frame.SkeletonId);

            if (skeleton == null)
            {
                LastError = "Index 1 has no skeleton " + frame.SkeletonId
                    + ", which frame " + packedFrameId + " names.";
                Finish();
                HasPose = false;
                return false;
            }

            BoneCount = skeleton.BoneCount;

            //The skeleton's stored transform types with the client's one rewrite applied - a stored 6
            //is a 2 (Node_Sub1.java:96-97). The frame decoder needs the effective types too, because
            //which slots get their values shifted into angles depends on them (Class7.java:91-95).
            int[] effectiveTransformTypes = skeleton.GetEffectiveTransformTypes();

            ResolvedFrame resolvedFrame;

            try
            {
                resolvedFrame = frame.Resolve(effectiveTransformTypes);
            }
            catch (ArgumentException failure)
            {
                //A frame whose slot count disagrees with the skeleton it names. Reported rather than
                //propagated, for the same reason as the two lookups above.
                LastError = failure.Message;
                Finish();
                HasPose = false;
                return false;
            }

            ResolvedTransformCount = resolvedFrame.Poses.Count;
            ApplyPoses(resolvedFrame, skeleton, effectiveTransformTypes);
            Finish();
            HasPose = true;
            return true;
        }

        /// <summary>Applies every resolved slot of a frame to every model.</summary>
        /// <remarks>
        ///     <c>Renderable.method2330</c>, <c>Renderable.java:313-327</c>. Two things about that
        ///     loop are worth spelling out.
        ///     <para>
        ///     <b>A slot may carry its own pivot, and it is applied first with zero offsets.</b> The
        ///     frame decoder records which earlier slot owns the pivot for a translate, rotate or
        ///     scale (<c>Class7.java:96-101</c>), and the client re-derives the centroid from that
        ///     bone's labels immediately before the transform (<c>Renderable.java:317-322</c>, and the
        ///     same shape at <c>:722-727</c>). It has to be re-derived rather than remembered, because
        ///     an earlier transform in the same frame may have moved the vertices it is the centroid
        ///     of. A pose that resolved the pivot any other way rotates about the model origin
        ///     instead, which produces a plausible-looking and completely wrong pose.
        ///     </para>
        ///     <para>
        ///     <b>The outcome is scored across the whole model set, not per model.</b> A transform
        ///     that reached one model of five has been applied, not failed four times - the other four
        ///     simply do not carry that label. Counting per model would make every multi-part entity
        ///     look broken. Once the parts are merged there is only one target to score, which reaches
        ///     the same answer by construction rather than by the loop below being careful.
        ///     </para>
        /// </remarks>
        /// <param name="resolved">The frame's slots with their values.</param>
        /// <param name="skeleton">The skeleton the frame names.</param>
        /// <param name="types">Effective transform type per bone.</param>
        private void ApplyPoses(ResolvedFrame resolved, SkeletonDefinition skeleton, int[] types)
        {
            for (int i = 0; i < resolved.Poses.Count; i++)
            {
                FramePose framePose = resolved.Poses[i];

                //A slot naming a bone the skeleton does not have. Skipped rather than counted,
                //because it is a damaged pairing rather than a transform that failed to reach.
                if ((uint)framePose.Slot >= (uint)skeleton.Bones.Count)
                {
                    continue;
                }

                SkeletonBone bone = skeleton.Bones[framePose.Slot];

                if ((bone.Mask & FullMask) != FullMask)
                {
                    PartialMaskBoneCount++;
                }

                if ((uint)framePose.PivotSlot < (uint)skeleton.Bones.Count)
                {
                    List<int> pivotLabels = skeleton.Bones[framePose.PivotSlot].Labels;

                    foreach (PosedMesh pose in targets)
                    {
                        pose.Apply(PosedMesh.TypePivot, pivotLabels, 0, 0, 0);
                    }
                }

                bool reachedAModel = false;
                bool unsupportedOnSomeModel = false;

                foreach (PosedMesh pose in targets)
                {
                    switch (pose.Apply(types[framePose.Slot], bone.Labels, framePose.X, framePose.Y, framePose.Z))
                    {
                        case TransformOutcome.Applied:
                            reachedAModel = true;
                            break;

                        case TransformOutcome.Unsupported:
                            unsupportedOnSomeModel = true;
                            break;
                    }
                }

                //Applied wins over unsupported, and unsupported over no-target, so each transform is
                //counted exactly once under the most informative thing that happened to it.
                if (reachedAModel)
                {
                    AppliedTransformCount++;
                }
                else if (unsupportedOnSomeModel)
                {
                    UnsupportedTransformCount++;
                }
                else
                {
                    NoTargetTransformCount++;
                }
            }
        }

        /// <summary>
        ///     Reduces the posed mesh back to model units and hands each part its share of it.
        /// </summary>
        /// <remarks>
        ///     The read-back is unconditional rather than only on the success path. Every exit from
        ///     <see cref="RefreshPose"/> resets both the parts and the merged mesh first, so on a
        ///     failure path the merged mesh is at rest and scattering it writes each part the rest
        ///     coordinates it already had - a welded vertex is coincident with its source by
        ///     definition, which is what makes that a no-op rather than a coincidence.
        /// </remarks>
        private void Finish()
        {
            foreach (PosedMesh pose in targets)
            {
                pose.Finish();
            }

            if (composite == null || compositePose == null)
            {
                return;
            }

            for (int part = 0; part < poses.Count; part++)
            {
                poses[part].ReadBackFrom(compositePose, composite.VertexMap[part], composite.FaceOffset[part]);
            }
        }
    }
}
