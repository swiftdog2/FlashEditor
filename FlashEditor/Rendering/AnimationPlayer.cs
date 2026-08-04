using FlashEditor.Definitions.Animation;

namespace FlashEditor.Rendering
{
    public enum PlaybackState
    {
        Empty,
        Paused,
        Playing,
        Finished
    }

    public sealed class AnimationPlayer
    {
        public const int CycleMilliseconds = 20;

        public const double CycleSeconds = 0.02;

        public const int RenderFramesPerSecond = 30;

        public const double RenderFrameSeconds = 1.0 / 30.0;

        public const int MaxCyclesPerAdvance = 50;

        private const double CycleEpsilon = 1E-09;

        private AnimationDefinition? animation;

        private double carrySeconds;

        public AnimationDefinition? Animation => animation;

        public PlaybackState State { get; private set; } = PlaybackState.Empty;


        public bool IsPlaying => State == PlaybackState.Playing;

        public int FrameIndex { get; private set; } = -1;


        public int FrameCount => animation?.FrameCount ?? 0;

        public int PackedFrameId
        {
            get
            {
                AnimationDefinition? animationDefinition = animation;
                if (animationDefinition == null || (uint)FrameIndex >= (uint)animationDefinition.FrameIds.Length)
                {
                    return -1;
                }
                return animationDefinition.FrameIds[FrameIndex];
            }
        }

        public int FrameGroup => (PackedFrameId == -1) ? (-1) : AnimationDefinition.FrameGroupOf(PackedFrameId);

        public int FrameFileId => (PackedFrameId == -1) ? (-1) : AnimationDefinition.FrameIndexOf(PackedFrameId);

        public int SecondaryPackedFrameId
        {
            get
            {
                AnimationDefinition? animationDefinition = animation;
                if (animationDefinition == null || (uint)FrameIndex >= (uint)animationDefinition.SecondaryFrameIds.Length)
                {
                    return -1;
                }
                int num = animationDefinition.SecondaryFrameIds[FrameIndex];
                return (num == 65535) ? (-1) : num;
            }
        }

        public int CurrentFrameDuration
        {
            get
            {
                AnimationDefinition? animationDefinition = animation;
                if (animationDefinition == null || (uint)FrameIndex >= (uint)animationDefinition.FrameDurations.Length)
                {
                    return 0;
                }
                return animationDefinition.FrameDurations[FrameIndex];
            }
        }

        public int CyclesIntoFrame { get; private set; }

        public long ElapsedCycles { get; private set; }

        public double ElapsedSeconds => (double)ElapsedCycles * 0.02;

        public double TotalSeconds => (double)(animation?.TotalDuration ?? 0) * 0.02;

        public int LoopsCompleted { get; private set; }

        public long DroppedCycles { get; private set; }

        public bool RepeatIndefinitely { get; set; }

        public void Play(AnimationDefinition? definition)
        {
            animation = definition;
            Rewind();
            State = ((definition != null) ? ((definition.FrameCount == 0) ? PlaybackState.Finished : PlaybackState.Playing) : PlaybackState.Empty);
        }

        public void Rewind()
        {
            FrameIndex = ((animation == null || animation.FrameCount == 0) ? (-1) : 0);
            CyclesIntoFrame = 0;
            ElapsedCycles = 0L;
            LoopsCompleted = 0;
            DroppedCycles = 0L;
            carrySeconds = 0.0;
            if (animation == null)
            {
                State = PlaybackState.Empty;
            }
            else if (animation.FrameCount == 0)
            {
                State = PlaybackState.Finished;
            }
            else if (State == PlaybackState.Finished || State == PlaybackState.Empty)
            {
                State = PlaybackState.Playing;
            }
        }

        public void Pause()
        {
            if (State == PlaybackState.Playing)
            {
                State = PlaybackState.Paused;
            }
        }

        public void Resume()
        {
            if (animation != null && animation.FrameCount != 0)
            {
                if (State == PlaybackState.Finished)
                {
                    Rewind();
                }
                State = PlaybackState.Playing;
            }
        }

        public void Stop()
        {
            animation = null;
            FrameIndex = -1;
            CyclesIntoFrame = 0;
            ElapsedCycles = 0L;
            LoopsCompleted = 0;
            DroppedCycles = 0L;
            carrySeconds = 0.0;
            State = PlaybackState.Empty;
        }

        public bool Seek(int frameIndex)
        {
            if (animation == null || (uint)frameIndex >= (uint)animation.FrameCount)
            {
                return false;
            }
            FrameIndex = frameIndex;
            CyclesIntoFrame = 0;
            carrySeconds = 0.0;
            State = PlaybackState.Paused;
            return true;
        }

        public bool Advance(double seconds)
        {
            if (State != PlaybackState.Playing || seconds <= 0.0 || double.IsNaN(seconds))
            {
                return false;
            }
            carrySeconds += seconds;

            /* The epsilon is not a fudge. 0.02 has no exact binary form, so 0.06 / 0.02 evaluates
               just below three and a bare truncation drops one cycle in three - a third off the
               speed of every animation in the cache. */
            int num = (int)(carrySeconds / CycleSeconds + CycleEpsilon);
            if (num <= 0)
            {
                return false;
            }
            carrySeconds -= (double)num * 0.02;
            if (num > 50)
            {
                DroppedCycles += num - 50;
                num = 50;
            }
            int frameIndex = FrameIndex;
            for (int i = 0; i < num; i++)
            {
                if (State != PlaybackState.Playing)
                {
                    break;
                }
                Tick();
            }
            return FrameIndex != frameIndex;
        }

        public void Tick()
        {
            AnimationDefinition? animationDefinition = animation;
            if (animationDefinition == null || State != PlaybackState.Playing)
            {
                return;
            }
            int frameCount = animationDefinition.FrameCount;
            if (frameCount == 0)
            {
                State = PlaybackState.Finished;
                return;
            }
            ElapsedCycles++;
            CyclesIntoFrame++;
            /* The client's test is strictly greater, so a frame with a stored duration of d is held
               for d cycles and the counter restarts at 1 rather than 0 on the cycle that advances
               it. Using >= here drops one cycle per frame, which shortens every animation in the
               cache by its own frame count. */
            if (FrameIndex < frameCount && CyclesIntoFrame > DurationAt(animationDefinition, FrameIndex))
            {
                CyclesIntoFrame = 1;
                FrameIndex++;
            }
            if (FrameIndex < frameCount)
            {
                return;
            }
            LoopsCompleted++;
            if (RepeatIndefinitely)
            {
                FrameIndex = 0;
                CyclesIntoFrame = 0;
                return;
            }
            FrameIndex -= animationDefinition.FrameStep;
            if (LoopsCompleted >= animationDefinition.MaxLoops || (uint)FrameIndex >= (uint)frameCount)
            {
                FrameIndex = frameCount - 1;
                CyclesIntoFrame = DurationAt(animationDefinition, FrameIndex);
                State = PlaybackState.Finished;
            }
        }

        private static int DurationAt(AnimationDefinition definition, int frameIndex)
        {
            return ((uint)frameIndex < (uint)definition.FrameDurations.Length) ? definition.FrameDurations[frameIndex] : 0;
        }
    }
}
