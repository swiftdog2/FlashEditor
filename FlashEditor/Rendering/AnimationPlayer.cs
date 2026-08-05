using FlashEditor.Definitions.Animation;

namespace FlashEditor.Rendering
{
    /// <summary>Where the playhead is, as a panel needs to describe it.</summary>
    public enum PlaybackState
    {
        /// <summary>No animation loaded.</summary>
        Empty,

        /// <summary>An animation is loaded and the playhead is held.</summary>
        Paused,

        /// <summary>Running.</summary>
        Playing,

        /// <summary>Ran to its end and stopped there, on its last frame.</summary>
        /// <remarks>Distinct from <see cref="Paused"/> so a resume can rewind rather than stall.</remarks>
        Finished
    }

    /// <summary>
    ///     Drives the playhead of one index-20 animation from wall-clock time.
    /// </summary>
    /// <remarks>
    ///     <b>The render rate and the animation rate are different things, and conflating them is the
    ///     defect this type exists to prevent.</b> The client runs a fixed 20 ms cycle
    ///     (<c>Class212.java:13</c> sets the interval to 20,000,000 ns) and its outer loop asks how
    ///     many cycles are due, runs that many, and <i>then</i> paints once
    ///     (<c>Applet_Sub1.java:861-871</c>). An animation's stored durations are counted in those
    ///     cycles, not in frames drawn.
    ///     <para>
    ///     A viewport that advanced one animation step per redraw would still animate. It would run at
    ///     whatever rate the machine happened to paint at - faster on a fast machine, faster again
    ///     when the window is small - and nothing but a stopwatch would tell you. So
    ///     <see cref="Advance"/> takes elapsed <b>seconds</b> and converts, and the tests assert that
    ///     one wall-clock second advances the same number of cycles whether it arrived in 30 pieces or
    ///     60.
    ///     </para>
    ///     <para>
    ///     This type holds only the playhead. It never reads a frame or a skeleton;
    ///     <see cref="SkeletalAnimator"/> does that from <see cref="PackedFrameId"/>. Keeping them
    ///     apart is what lets the timing be tested without a cache.
    ///     </para>
    /// </remarks>
    public sealed class AnimationPlayer
    {
        /// <summary>Milliseconds in one client cycle.</summary>
        /// <remarks><c>Class212.java:13</c>, as 20,000,000 nanoseconds.</remarks>
        public const int CycleMilliseconds = 20;

        /// <summary>One client cycle in seconds.</summary>
        public const double CycleSeconds = CycleMilliseconds / 1000.0;

        /// <summary>The rate the viewport is redrawn at, which is unrelated to <see cref="CycleMilliseconds"/>.</summary>
        /// <remarks>
        ///     Here only so a caller wiring up a timer has one place to read it from. Nothing in the
        ///     playback arithmetic uses it, and that is the point.
        /// </remarks>
        public const int RenderFramesPerSecond = 30;

        /// <summary>One redraw in seconds, at <see cref="RenderFramesPerSecond"/>.</summary>
        public const double RenderFrameSeconds = 1.0 / RenderFramesPerSecond;

        /// <summary>
        ///     Most cycles one <see cref="Advance"/> will run before it gives up on the backlog.
        /// </summary>
        /// <remarks>
        ///     Without a cap, a redraw that arrives ten minutes late spends ten minutes of cycles
        ///     inside one paint handler and the window stops responding.
        ///     <para>
        ///     The client caps at ten and silently discards the rest
        ///     (<c>Class240_Sub1.java:91-99</c>). This is fifty, and counts what it drops into
        ///     <see cref="DroppedCycles"/>. Both differences are deliberate: a WinForms paint handler
        ///     can be blocked far longer than a game canvas - loading a model off the cache will do
        ///     it - so a larger window keeps a brief stall from being visible at all, and counting the
        ///     loss is what turns "the animation jumped" into a number a human can read.
        ///     </para>
        /// </remarks>
        public const int MaxCyclesPerAdvance = 50;

        /// <summary>
        ///     Slack added before the division into whole cycles, in cycles.
        /// </summary>
        /// <remarks>
        ///     Not a fudge factor. 0.02 has no exact binary representation, so <c>0.06 / 0.02</c>
        ///     evaluates just below three and a bare truncation drops one cycle in three - a third off
        ///     the speed of every animation in the cache, and a defect that looks like nothing at all.
        ///     A billionth of a cycle is far below any real timer's resolution and far above the
        ///     representation error being corrected.
        /// </remarks>
        private const double CycleEpsilon = 1E-09;

        /// <summary>The animation being played, or null.</summary>
        private AnimationDefinition? animation;

        /// <summary>Elapsed time not yet worth a whole cycle, carried into the next advance.</summary>
        /// <remarks>
        ///     A 30 fps redraw is 33.33 ms, which is one cycle and a third. Dropping the third each
        ///     time would run every animation two thirds of a cycle slow per redraw - about 30% - and
        ///     look merely sluggish rather than broken.
        /// </remarks>
        private double carrySeconds;

        /// <summary>The animation being played, or null.</summary>
        public AnimationDefinition? Animation => animation;

        /// <summary>Where the playhead is.</summary>
        public PlaybackState State { get; private set; } = PlaybackState.Empty;

        /// <summary>Whether <see cref="Advance"/> will do anything.</summary>
        public bool IsPlaying => State == PlaybackState.Playing;

        /// <summary>
        ///     Which step of the animation is showing, or -1.
        /// </summary>
        /// <remarks>
        ///     -1 rather than 0 when nothing is loaded, because 0 is a real step and a panel showing it
        ///     could not then say whether anything is loaded at all.
        /// </remarks>
        public int FrameIndex { get; private set; } = -1;

        /// <summary>How many steps the animation has.</summary>
        public int FrameCount => animation?.FrameCount ?? 0;

        /// <summary>
        ///     The index-0 address of the frame the current step plays, or -1.
        /// </summary>
        /// <remarks>
        ///     Packed: group in the high sixteen bits, file in the low sixteen. This is the only thing
        ///     <see cref="SkeletalAnimator"/> reads off the player.
        /// </remarks>
        public int PackedFrameId
        {
            get
            {
                AnimationDefinition? definition = animation;

                if (definition == null || (uint)FrameIndex >= (uint)definition.FrameIds.Length)
                {
                    return -1;
                }

                return definition.FrameIds[FrameIndex];
            }
        }

        /// <summary>The index-0 group the current frame lives in, or -1.</summary>
        public int FrameGroup => PackedFrameId == -1 ? -1 : AnimationDefinition.FrameGroupOf(PackedFrameId);

        /// <summary>The file within that group, or -1.</summary>
        public int FrameFileId => PackedFrameId == -1 ? -1 : AnimationDefinition.FrameIndexOf(PackedFrameId);

        /// <summary>
        ///     The second frame this step blends with, or -1 when there is none.
        /// </summary>
        /// <remarks>
        ///     Reported for the diagnostics panel and not otherwise used - nothing here blends two
        ///     frames. 65535 is the record's "absent" value rather than a real frame, so it is mapped
        ///     to -1 here to match every other "no frame" in this type.
        /// </remarks>
        public int SecondaryPackedFrameId
        {
            get
            {
                AnimationDefinition? definition = animation;

                if (definition == null || (uint)FrameIndex >= (uint)definition.SecondaryFrameIds.Length)
                {
                    return -1;
                }

                int packed = definition.SecondaryFrameIds[FrameIndex];
                return packed == 65535 ? -1 : packed;
            }
        }

        /// <summary>How many cycles the current step is held for, per the record.</summary>
        public int CurrentFrameDuration
        {
            get
            {
                AnimationDefinition? definition = animation;

                if (definition == null || (uint)FrameIndex >= (uint)definition.FrameDurations.Length)
                {
                    return 0;
                }

                return definition.FrameDurations[FrameIndex];
            }
        }

        /// <summary>How many cycles of the current step have elapsed, counting from one.</summary>
        /// <remarks>See <see cref="Tick"/> for why it restarts at one rather than zero.</remarks>
        public int CyclesIntoFrame { get; private set; }

        /// <summary>Cycles run since the animation started, across loops.</summary>
        public long ElapsedCycles { get; private set; }

        /// <summary>Cycles run, in seconds.</summary>
        public double ElapsedSeconds => ElapsedCycles * CycleSeconds;

        /// <summary>The animation's whole length in seconds, summed over its stored durations.</summary>
        public double TotalSeconds => (animation?.TotalDuration ?? 0) * CycleSeconds;

        /// <summary>How many times the playhead has run off the end.</summary>
        public int LoopsCompleted { get; private set; }

        /// <summary>Cycles thrown away to <see cref="MaxCyclesPerAdvance"/>.</summary>
        /// <remarks>
        ///     Public because a stall and a slow animation look identical on a viewport nothing can
        ///     capture, and a rising number here is how a human tells them apart.
        /// </remarks>
        public long DroppedCycles { get; private set; }

        /// <summary>
        ///     Whether to restart at step 0 instead of honouring the record's loop point and cap.
        /// </summary>
        /// <remarks>
        ///     The editor's preview toggle. It is not an invention: it is the spot animation's wrap
        ///     (<c>Class340.java:39-42</c>, which resets both the step and the cycle counter
        ///     unconditionally), so a looping preview still shows something the client does somewhere.
        ///     Left off, playback follows the animation's own <c>FrameStep</c> and <c>MaxLoops</c>.
        /// </remarks>
        public bool RepeatIndefinitely { get; set; }

        /// <summary>Loads an animation and starts it from the beginning.</summary>
        /// <param name="definition">The animation, or null to clear.</param>
        public void Play(AnimationDefinition? definition)
        {
            animation = definition;
            Rewind();

            State = definition == null
                ? PlaybackState.Empty
                : definition.FrameCount == 0 ? PlaybackState.Finished : PlaybackState.Playing;
        }

        /// <summary>Puts the playhead back to the start, keeping the animation loaded.</summary>
        /// <remarks>
        ///     A paused player stays paused. Rewinding is a position change, and silently resuming
        ///     would take the pause button away from whoever pressed it.
        /// </remarks>
        public void Rewind()
        {
            FrameIndex = animation == null || animation.FrameCount == 0 ? -1 : 0;
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
                //An animation record with no opcode 1 is legal and holds no frames at all. Finished
                //rather than Playing, so a caller does not spin waiting for a step that cannot come.
                State = PlaybackState.Finished;
            }
            else if (State == PlaybackState.Finished || State == PlaybackState.Empty)
            {
                State = PlaybackState.Playing;
            }
        }

        /// <summary>Holds the playhead where it is.</summary>
        public void Pause()
        {
            if (State == PlaybackState.Playing)
            {
                State = PlaybackState.Paused;
            }
        }

        /// <summary>Starts running again, rewinding first if the animation had run to its end.</summary>
        public void Resume()
        {
            if (animation == null || animation.FrameCount == 0)
            {
                return;
            }

            if (State == PlaybackState.Finished)
            {
                Rewind();
            }

            State = PlaybackState.Playing;
        }

        /// <summary>Unloads the animation and clears every counter.</summary>
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

        /// <summary>Jumps to a step and pauses there, for scrubbing.</summary>
        /// <remarks>
        ///     Pauses deliberately. Someone dragging a scrub bar wants to look at the step they landed
        ///     on, and a player that carried on running would take it away again before they could.
        ///     <see cref="ElapsedCycles"/> is left alone, because it measures how long playback has
        ///     been running rather than where the playhead is.
        /// </remarks>
        /// <param name="frameIndex">The step to show.</param>
        /// <returns><c>false</c> when there is no such step.</returns>
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

        /// <summary>Runs however many whole cycles the elapsed wall-clock time is worth.</summary>
        /// <param name="seconds">Wall-clock time since the last call.</param>
        /// <returns>
        ///     <c>true</c> only when the <b>step</b> changed, so a caller can skip re-posing the mesh
        ///     on the cycles that merely advanced the counter within a step. Most cycles do.
        /// </returns>
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
            int cycles = (int)(carrySeconds / CycleSeconds + CycleEpsilon);

            if (cycles <= 0)
            {
                return false;
            }

            //Only whole cycles are consumed; the remainder is carried rather than discarded.
            carrySeconds -= cycles * CycleSeconds;

            if (cycles > MaxCyclesPerAdvance)
            {
                DroppedCycles += cycles - MaxCyclesPerAdvance;
                cycles = MaxCyclesPerAdvance;
            }

            int stepBefore = FrameIndex;

            for (int cycle = 0; cycle < cycles; cycle++)
            {
                //An animation that finishes part way through the backlog stops there rather than
                //burning the rest of the cycles against a stopped playhead.
                if (State != PlaybackState.Playing)
                {
                    break;
                }

                Tick();
            }

            return FrameIndex != stepBefore;
        }

        /// <summary>Runs exactly one client cycle.</summary>
        /// <remarks>
        ///     <c>Class340.java:105-136</c>, the entity animation tick, arm for arm.
        ///     <para>
        ///     <b>The counter restarts at one, and the test is strictly greater</b> (<c>:106-111</c>).
        ///     So a step whose stored duration is <c>d</c> is held for exactly <c>d</c> cycles: the
        ///     cycle that advances the step sets the new step's counter to 1 rather than 0, because
        ///     that cycle has already been spent on it. Using <c>&gt;=</c> here drops one cycle per
        ///     step, which shortens every animation in the cache by its own frame count and is
        ///     invisible on anything short. A zero-duration step still costs a cycle for the same
        ///     reason, which is why a cumulative sum over the durations is not an equivalent
        ///     formulation.
        ///     </para>
        ///     <para>
        ///     <b>Running off the end is how an animation says how it ends</b> (<c>:117-134</c>). The
        ///     playhead is rewound by the record's own <c>FrameStep</c>, which is -1 when the record
        ///     carried no opcode 2 - and subtracting -1 lands one <i>past</i> the end, which is the
        ///     out-of-range test below firing on purpose. That is not an accident of the default; it
        ///     is how a non-looping animation is written.
        ///     </para>
        /// </remarks>
        public void Tick()
        {
            AnimationDefinition? definition = animation;

            if (definition == null || State != PlaybackState.Playing)
            {
                return;
            }

            int frameCount = definition.FrameCount;

            if (frameCount == 0)
            {
                State = PlaybackState.Finished;
                return;
            }

            ElapsedCycles++;
            CyclesIntoFrame++;

            if (FrameIndex < frameCount && CyclesIntoFrame > DurationAt(definition, FrameIndex))
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

            FrameIndex -= definition.FrameStep;

            //Either the loop cap has been reached or the rewind landed outside the animation. The
            //client stops on both (:124-133); this settles on the last step rather than on nothing,
            //so a finished preview shows the pose the animation ends in.
            if (LoopsCompleted >= definition.MaxLoops || (uint)FrameIndex >= (uint)frameCount)
            {
                FrameIndex = frameCount - 1;
                CyclesIntoFrame = DurationAt(definition, FrameIndex);
                State = PlaybackState.Finished;
            }
        }

        /// <summary>How long a step is held, or zero when the durations are short of the frame list.</summary>
        /// <remarks>
        ///     Zero rather than a throw. The two arrays come from the same opcode and are the same
        ///     length in every shipped record, but a step with no duration should pass through in one
        ///     cycle rather than stop the render tick.
        /// </remarks>
        /// <param name="definition">The animation.</param>
        /// <param name="frameIndex">The step.</param>
        /// <returns>The stored duration in cycles.</returns>
        private static int DurationAt(AnimationDefinition definition, int frameIndex)
        {
            return (uint)frameIndex < (uint)definition.FrameDurations.Length
                ? definition.FrameDurations[frameIndex]
                : 0;
        }
    }
}
