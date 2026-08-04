using System.Collections.Generic;
using System.Globalization;
using System;
using FlashEditor.Definitions.Animation;
using FlashEditor.Definitions;

namespace FlashEditor.Rendering
{
    public sealed class SkeletalAnimator
    {
        public const int FullMask = 65535;

        private readonly IAnimationDataSource source;

        private readonly List<SkinnedModel> models = new List<SkinnedModel>();

        private readonly List<PosedMesh> poses = new List<PosedMesh>();

        private bool poseIsStale = true;

        public AnimationPlayer Player { get; } = new AnimationPlayer();


        public IReadOnlyList<SkinnedModel> Models => models;

        public IReadOnlyList<PosedMesh> Poses => poses;

        public bool HasPose { get; private set; }

        public int SkeletonId { get; private set; } = -1;


        public int BoneCount { get; private set; }

        public int FrameTransformCount { get; private set; }

        public int ResolvedTransformCount { get; private set; }

        public int AppliedTransformCount { get; private set; }

        public int NoTargetTransformCount { get; private set; }

        public int UnsupportedTransformCount { get; private set; }

        public int PartialMaskBoneCount { get; private set; }

        public string? LastError { get; private set; }

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
                    return "Frame " + Player.FrameIndex + " posed, but no transform reached this model - " + ResolvedTransformCount + " resolved, " + NoTargetTransformCount + " matched no label.";
                }
                return "Frame " + Player.FrameIndex + " of " + Player.FrameCount + ", " + AppliedTransformCount + " of " + ResolvedTransformCount + " transforms applied.";
            }
        }

        public IReadOnlyList<KeyValuePair<string, string>> Diagnostics
        {
            get
            {
                List<KeyValuePair<string, string>> rows = new List<KeyValuePair<string, string>>();
                Row("State", Player.State);
                Row("Frame index", Player.FrameIndex + " of " + Player.FrameCount);
                Row("Frame id", (Player.PackedFrameId == -1) ? "none" : (Player.PackedFrameId + " (group " + Player.FrameGroup + ", file " + Player.FrameFileId + ")"));
                Row("Secondary frame id", (Player.SecondaryPackedFrameId == -1) ? "none" : Player.SecondaryPackedFrameId.ToString(CultureInfo.InvariantCulture));
                Row("Frame duration", Player.CyclesIntoFrame + " of " + Player.CurrentFrameDuration + " cycles");
                Row("Elapsed", Player.ElapsedSeconds.ToString("0.000", CultureInfo.InvariantCulture) + " s (" + Player.ElapsedCycles + " cycles of " + 20 + " ms)");
                Row("Animation length", Player.TotalSeconds.ToString("0.000", CultureInfo.InvariantCulture) + " s");
                Row("Loops completed", Player.LoopsCompleted);
                Row("Dropped cycles", Player.DroppedCycles);
                Row("Skeleton", (SkeletonId == -1) ? "none" : (SkeletonId + " (" + BoneCount + " bones)"));
                Row("Frame transforms", FrameTransformCount);
                Row("Resolved transforms", ResolvedTransformCount);
                Row("Applied transforms", AppliedTransformCount);
                Row("Transforms with no target", NoTargetTransformCount);
                Row("Unsupported transforms", UnsupportedTransformCount);
                Row("Partial-mask bones", PartialMaskBoneCount);
                Row("Models", models.Count);
                return rows;
                void Row(string name, object value)
                {
                    rows.Add(new KeyValuePair<string, string>(name, Convert.ToString(value, CultureInfo.InvariantCulture) ?? ""));
                }
            }
        }

        public SkeletalAnimator(IAnimationDataSource source)
        {
            this.source = source ?? throw new ArgumentNullException("source");
        }

        public void SetModels(IEnumerable<ModelDefinition> definitions)
        {
            if (definitions == null)
            {
                throw new ArgumentNullException("definitions");
            }
            models.Clear();
            poses.Clear();
            foreach (ModelDefinition definition in definitions)
            {
                SkinnedModel skinnedModel = new SkinnedModel(definition);
                models.Add(skinnedModel);
                poses.Add(skinnedModel.CreatePose());
            }
            HasPose = false;
            poseIsStale = true;
        }

        public void Play(AnimationDefinition? animation)
        {
            Player.Play(animation);
            poseIsStale = true;
            HasPose = false;
            RefreshPose();
        }

        public bool Advance(double seconds)
        {
            if (!Player.Advance(seconds) && !poseIsStale)
            {
                return false;
            }
            return RefreshPose();
        }

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
            int packedFrameId = Player.PackedFrameId;
            if (models.Count == 0 || packedFrameId == -1)
            {
                foreach (PosedMesh pose2 in poses)
                {
                    pose2.Finish();
                }
                HasPose = false;
                return false;
            }
            FrameDefinition? frame = source.GetFrame(packedFrameId);
            if (frame == null)
            {
                LastError = "Index 0 has no frame " + AnimationDefinition.FrameIndexOf(packedFrameId) + " in group " + AnimationDefinition.FrameGroupOf(packedFrameId) + ".";
                Finish();
                HasPose = false;
                return false;
            }
            SkeletonId = frame.SkeletonId;
            FrameTransformCount = frame.TransformCount;
            SkeletonDefinition? skeleton = source.GetSkeleton(frame.SkeletonId);
            if (skeleton == null)
            {
                LastError = "Index 1 has no skeleton " + frame.SkeletonId + ", which frame " + packedFrameId + " names.";
                Finish();
                HasPose = false;
                return false;
            }
            BoneCount = skeleton.BoneCount;
            int[] effectiveTransformTypes = skeleton.GetEffectiveTransformTypes();
            ResolvedFrame resolvedFrame;
            try
            {
                resolvedFrame = frame.Resolve(effectiveTransformTypes);
            }
            catch (ArgumentException ex)
            {
                LastError = ex.Message;
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

        private void ApplyPoses(ResolvedFrame resolved, SkeletonDefinition skeleton, int[] types)
        {
            for (int i = 0; i < resolved.Poses.Count; i++)
            {
                FramePose framePose = resolved.Poses[i];
                if ((uint)framePose.Slot >= (uint)skeleton.Bones.Count)
                {
                    continue;
                }
                SkeletonBone skeletonBone = skeleton.Bones[framePose.Slot];
                if ((skeletonBone.Mask & 0xFFFF) != 65535)
                {
                    PartialMaskBoneCount++;
                }
                if ((uint)framePose.PivotSlot < (uint)skeleton.Bones.Count)
                {
                    List<int> labels = skeleton.Bones[framePose.PivotSlot].Labels;
                    foreach (PosedMesh pose in poses)
                    {
                        pose.Apply(0, labels, 0, 0, 0);
                    }
                }
                bool flag = false;
                bool flag2 = false;
                foreach (PosedMesh pose2 in poses)
                {
                    switch (pose2.Apply(types[framePose.Slot], skeletonBone.Labels, framePose.X, framePose.Y, framePose.Z))
                    {
                    case TransformOutcome.Applied:
                        flag = true;
                        break;
                    case TransformOutcome.Unsupported:
                        flag2 = true;
                        break;
                    }
                }
                if (flag)
                {
                    AppliedTransformCount++;
                }
                else if (flag2)
                {
                    UnsupportedTransformCount++;
                }
                else
                {
                    NoTargetTransformCount++;
                }
            }
        }

        private void Finish()
        {
            foreach (PosedMesh pose in poses)
            {
                pose.Finish();
            }
        }
    }
}
