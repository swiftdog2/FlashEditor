using System;
using FlashEditor.Definitions.Animation;
using FlashEditor.Rendering;
using Xunit;

namespace FlashEditor.Tests.Rendering
{
    /// <summary>
    ///     Pins frame selection to the animation's own stored durations rather than to the redraw rate.
    /// </summary>
    /// <remarks>
    ///     This is the defect the playback loop is most likely to have, and the one that is hardest to
    ///     see: an animation driven off the redraw count still animates, just at the wrong speed, and
    ///     nothing but a stopwatch tells you. So the assertions here are about <b>time</b>, not about
    ///     redraws - the same wall-clock second advances the same number of cycles whether it arrived
    ///     in 30 pieces or 60.
    /// </remarks>
    public class AnimationPlaybackTests
    {
        /// <summary>Frame set group the test animations point at. Nothing reads it.</summary>
        private const int FrameSet = 10;

        /// <summary>
        ///     Each frame is held for exactly the number of cycles its stored duration says.
        /// </summary>
        /// <remarks>
        ///     Durations 3, 1 and 2 give a six-cycle animation: cycles 1 to 3 on step 0, cycle 4 on
        ///     step 1, cycles 5 and 6 on step 2. Driven a cycle at a time so the assertion is about
        ///     the rule and not about floating point.
        /// </remarks>
        [Fact]
        public void FrameSelection_FollowsTheStoredDurations()
        {
            var player = new AnimationPlayer();
            player.Play(Animation(3, 1, 2));

            Assert.Equal(0, player.FrameIndex);
            Assert.Equal(3, player.CurrentFrameDuration);

            int[] expected = { 0, 0, 0, 1, 2, 2 };
            for (int cycle = 0; cycle < expected.Length; cycle++)
            {
                player.Tick();
                Assert.Equal(expected[cycle], player.FrameIndex);
            }

            Assert.Equal(PlaybackState.Playing, player.State);

            //One more cycle runs off the end, and with no loop point the animation stops there.
            player.Tick();
            Assert.Equal(PlaybackState.Finished, player.State);
        }

        /// <summary>
        ///     The frame the playhead shows depends on elapsed time, not on how it was delivered.
        /// </summary>
        /// <remarks>
        ///     One second at 30fps and one second at 60fps both run 50 cycles, because a cycle is 20ms.
        ///     A loop that advanced one animation step per redraw would report 30 and 60 here, which is
        ///     the whole of the render-rate-is-not-the-animation-rate mistake in one assertion.
        /// </remarks>
        [Fact]
        public void Advance_RunsTheSameCyclesPerSecondAtAnyRenderRate()
        {
            var thirty = new AnimationPlayer();
            thirty.Play(Animation(3, 1, 2));
            thirty.RepeatIndefinitely = true;

            var sixty = new AnimationPlayer();
            sixty.Play(Animation(3, 1, 2));
            sixty.RepeatIndefinitely = true;

            for (int i = 0; i < 30; i++)
                thirty.Advance(1.0 / 30);
            for (int i = 0; i < 60; i++)
                sixty.Advance(1.0 / 60);

            Assert.Equal(50L, thirty.ElapsedCycles);
            Assert.Equal(50L, sixty.ElapsedCycles);
            Assert.Equal(thirty.FrameIndex, sixty.FrameIndex);
            Assert.Equal(1.0, thirty.ElapsedSeconds, 6);
        }

        /// <summary>
        ///     An exact multiple of the cycle advances exactly that many cycles.
        /// </summary>
        /// <remarks>
        ///     20ms has no exact binary representation, so <c>0.06 / 0.02</c> evaluates below three and
        ///     a bare truncation drops a cycle in three - a third off the speed of every animation.
        ///     This is the regression test for the epsilon that fixes it.
        /// </remarks>
        [Fact]
        public void Advance_DoesNotLoseACycleToFloatingPointOnExactMultiples()
        {
            var player = new AnimationPlayer();
            player.Play(Animation(3, 1, 2));

            player.Advance(0.06);
            Assert.Equal(3L, player.ElapsedCycles);
            Assert.Equal(0, player.FrameIndex);

            player.Advance(0.02);
            Assert.Equal(4L, player.ElapsedCycles);
            Assert.Equal(1, player.FrameIndex);
        }

        /// <summary>
        ///     A zero-duration step still costs a cycle, because the client tests its counter once.
        /// </summary>
        /// <remarks>
        ///     Durations 2, 0, 3: the advance onto step 1 happens on cycle 3, and step 1 is only left
        ///     on cycle 4. A cumulative sum over the durations would put cycle 3 on step 2 and be one
        ///     frame ahead for the rest of the animation.
        /// </remarks>
        [Fact]
        public void FrameSelection_StepsThroughAZeroDurationFrameRatherThanSkippingIt()
        {
            var player = new AnimationPlayer();
            player.Play(Animation(2, 0, 3));

            player.Tick();
            player.Tick();
            Assert.Equal(0, player.FrameIndex);

            player.Tick();
            Assert.Equal(1, player.FrameIndex);

            player.Tick();
            Assert.Equal(2, player.FrameIndex);
        }

        /// <summary>
        ///     Opcode 2 winds the playhead back and opcode 8 caps how often that may happen.
        /// </summary>
        /// <remarks>
        ///     A rewind of three on a three-step animation returns to step 0, so the animation plays
        ///     twice against a loop cap of two and then stops on its last frame.
        /// </remarks>
        [Fact]
        public void Looping_UsesTheAnimationsOwnRewindAndLoopCap()
        {
            AnimationDefinition definition = Animation(3, 1, 2);
            definition.FrameStep = 3;
            definition.MaxLoops = 2;

            var player = new AnimationPlayer();
            player.Play(definition);

            for (int cycle = 0; cycle < 6; cycle++)
                player.Tick();
            Assert.Equal(0, player.LoopsCompleted);

            //The seventh cycle runs off the end, rewinds by three and starts the second pass.
            player.Tick();
            Assert.Equal(PlaybackState.Playing, player.State);
            Assert.Equal(1, player.LoopsCompleted);
            Assert.Equal(0, player.FrameIndex);

            for (int cycle = 0; cycle < 5; cycle++)
                player.Tick();
            Assert.Equal(PlaybackState.Playing, player.State);
            Assert.Equal(2, player.FrameIndex);

            //And the pass after that hits the cap.
            player.Tick();
            Assert.Equal(PlaybackState.Finished, player.State);
            Assert.Equal(2, player.LoopsCompleted);
            Assert.Equal(2, player.FrameIndex);
        }

        /// <summary>
        ///     An animation with no opcode 2 plays once, because the default rewind runs off the end.
        /// </summary>
        /// <remarks>
        ///     <c>FrameStep</c> is -1 when the record did not carry opcode 2, and the client subtracts
        ///     it, so the playhead lands one <i>past</i> the end. That is not an accident of the
        ///     default - it is how an animation says "do not loop".
        /// </remarks>
        [Fact]
        public void Looping_IsOffWhenTheRecordCarriedNoLoopPoint()
        {
            AnimationDefinition definition = Animation(1, 1);
            Assert.Equal(-1, definition.FrameStep);

            var player = new AnimationPlayer();
            player.Play(definition);

            player.Tick();
            player.Tick();
            player.Tick();

            Assert.Equal(PlaybackState.Finished, player.State);
            Assert.Equal(1, player.LoopsCompleted);
        }

        /// <summary>
        ///     Repeating indefinitely restarts at step 0 and keeps going past the loop cap.
        /// </summary>
        /// <remarks>
        ///     The editor's preview toggle. It is the spot animation's wrap
        ///     (<c>Class340.java:41-43</c>) rather than an invention, so a looping preview still shows
        ///     something the client does somewhere.
        /// </remarks>
        [Fact]
        public void RepeatIndefinitely_RestartsRatherThanStopping()
        {
            var player = new AnimationPlayer();
            player.Play(Animation(1, 1));
            player.RepeatIndefinitely = true;

            for (int cycle = 0; cycle < 20; cycle++)
                player.Tick();

            Assert.Equal(PlaybackState.Playing, player.State);
            Assert.True(player.LoopsCompleted >= 5);
        }

        /// <summary>
        ///     Pausing holds the frame, and resuming carries on rather than restarting.
        /// </summary>
        [Fact]
        public void PauseAndResume_HoldTheFrameWithoutLosingThePlayhead()
        {
            var player = new AnimationPlayer();
            player.Play(Animation(3, 1, 2));

            player.Tick();
            player.Tick();
            player.Tick();
            player.Tick();
            Assert.Equal(1, player.FrameIndex);

            player.Pause();
            Assert.False(player.IsPlaying);
            player.Advance(10.0);
            Assert.Equal(1, player.FrameIndex);
            Assert.Equal(4L, player.ElapsedCycles);

            player.Resume();
            player.Tick();
            Assert.Equal(2, player.FrameIndex);
        }

        /// <summary>
        ///     A stalled viewport drops cycles rather than spinning, and says how many.
        /// </summary>
        /// <remarks>
        ///     Without the cap, a redraw that arrives ten minutes late spends ten minutes of cycles
        ///     inside one paint handler. Counting the loss is what turns "the animation jumped" into a
        ///     number a human can read.
        /// </remarks>
        [Fact]
        public void Advance_CapsAHugeGapAndReportsWhatItDropped()
        {
            var player = new AnimationPlayer();
            player.Play(Animation(1, 1));
            player.RepeatIndefinitely = true;

            player.Advance(10.0);

            Assert.Equal((long)AnimationPlayer.MaxCyclesPerAdvance, player.ElapsedCycles);
            Assert.Equal(500L - AnimationPlayer.MaxCyclesPerAdvance, player.DroppedCycles);
        }

        /// <summary>
        ///     An empty player and an animation with no frames both report a frame of -1.
        /// </summary>
        /// <remarks>
        ///     -1 rather than 0, because 0 is a real step and a panel that shows it cannot then say
        ///     whether anything is loaded.
        /// </remarks>
        [Fact]
        public void EmptyAnimation_ReportsNoFrameRatherThanFrameZero()
        {
            var player = new AnimationPlayer();
            Assert.Equal(PlaybackState.Empty, player.State);
            Assert.Equal(-1, player.FrameIndex);
            Assert.Equal(-1, player.PackedFrameId);

            player.Play(new AnimationDefinition());
            Assert.Equal(PlaybackState.Finished, player.State);
            Assert.Equal(-1, player.FrameIndex);
        }

        /// <summary>
        ///     The packed frame id splits into the index-0 group and file the frame lives at.
        /// </summary>
        [Fact]
        public void FrameId_ReportsTheIndexZeroAddress()
        {
            var player = new AnimationPlayer();
            player.Play(Animation(1, 1, 1));

            Assert.Equal(AnimationDefinition.PackFrame(FrameSet, 0), player.PackedFrameId);
            Assert.Equal(FrameSet, player.FrameGroup);
            Assert.Equal(0, player.FrameFileId);

            //Each step lasts one cycle, and the first advance happens on the cycle after the first,
            //so three cycles land on step 2.
            player.Tick();
            player.Tick();
            player.Tick();
            Assert.Equal(2, player.FrameIndex);
            Assert.Equal(2, player.FrameFileId);
            Assert.Equal(AnimationDefinition.PackFrame(FrameSet, 2), player.PackedFrameId);
        }

        /// <summary>Builds an animation whose steps play frame set 10, files 0 upward.</summary>
        /// <param name="durations">How long each step is held, in client cycles.</param>
        /// <returns>The animation.</returns>
        /// <summary>
        ///     The position readout wraps with the playhead instead of climbing across loops.
        /// </summary>
        /// <remarks>
        ///     Observed on the monitor as <c>51.120 s of 2.000 s</c>, on a two second animation left
        ///     looping. <see cref="AnimationPlayer.ElapsedCycles"/> counts every cycle ever run and is
        ///     right to, so both figures are wanted and the defect was showing the wrong one of them.
        ///     <para>
        ///     The assertion runs a six cycle animation twice round and checks the position at every
        ///     step of the second pass, because a wrap that fires a cycle early or late still looks
        ///     plausible on screen and would only ever be caught here.
        ///     </para>
        /// </remarks>
        [Fact]
        public void Position_WrapsWithThePlayheadWhileElapsedKeepsCounting()
        {
            var player = new AnimationPlayer { RepeatIndefinitely = true };
            player.Play(Animation(3, 1, 2));

            Assert.Equal(0, player.PositionCycles);

            //First pass: the position is the cycle count, since nothing has wrapped yet.
            for (int cycle = 1; cycle <= 6; cycle++)
            {
                player.Tick();
                Assert.Equal(cycle, player.PositionCycles);
            }

            //The seventh cycle runs off the end and restarts the pass.
            player.Tick();
            Assert.Equal(1, player.LoopsCompleted);
            Assert.Equal(0, player.PositionCycles);
            Assert.Equal(7, player.ElapsedCycles);

            //Second pass: position repeats the first pass exactly while elapsed keeps climbing.
            for (int cycle = 1; cycle <= 6; cycle++)
            {
                player.Tick();
                Assert.Equal(cycle, player.PositionCycles);
                Assert.Equal(7 + cycle, player.ElapsedCycles);
            }

            //And it never exceeds the animation's own length, which is what the readout divides by.
            Assert.True(player.PositionCycles <= player.TotalCycles,
                "the position ran past the animation's length, so the readout would show more " +
                "elapsed than total again");
        }

        /// <summary>
        ///     A finished animation rests on its full length rather than snapping back to zero.
        /// </summary>
        /// <remarks>
        ///     The wrap must not make a stopped preview read <c>0.000 s of 2.000 s</c> while showing
        ///     the animation's last pose, which would look like a player that never ran.
        /// </remarks>
        [Fact]
        public void Position_RestsOnTheFullLengthOnceTheAnimationHasFinished()
        {
            var player = new AnimationPlayer();
            player.Play(Animation(3, 1, 2));

            for (int cycle = 0; cycle < 7; cycle++)
                player.Tick();

            Assert.Equal(PlaybackState.Finished, player.State);
            Assert.Equal(6, player.PositionCycles);
        }

        private static AnimationDefinition Animation(params int[] durations)
        {
            var frames = new int[durations.Length];
            for (int i = 0; i < frames.Length; i++)
                frames[i] = AnimationDefinition.PackFrame(FrameSet, i);

            return new AnimationDefinition
            {
                Id = 1,
                FrameDurations = durations,
                FrameIds = frames
            };
        }
    }
}
