using System;

namespace FlashEditor.Definitions.Audio.Synth {
    /// <summary>
    ///     One sounding note: the sample it plays, where it has got to, and the envelope clocks that
    ///     will eventually end it.
    /// </summary>
    /// <remarks>
    ///     The client splits this across <c>Node_Sub16</c> (the note) and <c>Node_Sub31_Sub5</c> (the
    ///     resampling stream). They are one object here because the split buys nothing without the
    ///     client's stream-priority machinery, which this player does not have.
    ///     <para>
    ///     Positions and steps are 8.8 fixed point, <c>256</c> being one input sample per output
    ///     sample (<c>Node_Sub31_Sub5.java:600</c>). Gains are 6-bit fixed point against a sample
    ///     scaled by 256, so a gain of 64 is unity (<c>Node_Sub31_Sub5.java:18-19</c>).
    ///     </para>
    /// </remarks>
    internal sealed class SynthVoice {
        /// <summary>Unity gain, in the 6-bit fixed point the mix kernels use.</summary>
        internal const int UnityGain = 64;

        /// <summary>One input sample per output sample, in the 8.8 fixed point the position uses.</summary>
        internal const int UnityStep = 256;

        /// <summary>The audio this note plays.</summary>
        internal PcmSample Sample { get; }

        /// <summary>The patch the note came from, kept for its whole-patch volume.</summary>
        internal MidiPatchDefinition Patch { get; }

        /// <summary>The envelope the key names, or null when the key names none.</summary>
        internal MidiPatchEnvelope? Envelope { get; }

        /// <summary>The MIDI channel that started the note.</summary>
        internal int Channel { get; }

        /// <summary>The MIDI key number.</summary>
        internal int Note { get; set; }

        /// <summary>The key's mute group, or -1 for none.</summary>
        internal int MuteGroup { get; }

        /// <summary>
        ///     The velocity and patch term of the gain, computed once at note-on.
        /// </summary>
        /// <remarks>
        ///     <c>Node_Sub31_Sub2.java:977-978</c>:
        ///     <c>(1024 + patchVolume * velocity * (velocity * keyVolume)) &gt;&gt; 11</c>. Velocity
        ///     is squared, which is why a soft note is much quieter than a linear curve would make it.
        /// </remarks>
        internal int BaseGain { get; }

        /// <summary>The key's own pan, 0 hard left to 128 hard right.</summary>
        internal int KeyPan { get; }

        /// <summary>
        ///     The note's pitch offset from the sample's own pitch, in 256ths of a semitone.
        /// </summary>
        /// <remarks>
        ///     <c>Node_Sub31_Sub2.java:980</c>: <c>(note &lt;&lt; 8) - (tuning &amp; 0x7fff)</c>. The
        ///     tuning word's top bit is the held flag and is deliberately masked out of the number.
        /// </remarks>
        internal int PitchOffset { get; set; }

        /// <summary>How many control ticks the note has been sounding, for the vibrato ramp.</summary>
        internal int Ticks { get; set; }

        /// <summary>The vibrato oscillator's phase, advanced once per control tick.</summary>
        internal int LfoPhase { get; set; }

        /// <summary>The while-held decay clock.</summary>
        internal int DecayClock { get; set; }

        /// <summary>The attack envelope's clock.</summary>
        internal int AttackClock { get; set; }

        /// <summary>Which pair of the attack envelope the clock currently sits in.</summary>
        internal int AttackIndex { get; set; }

        /// <summary>
        ///     The release envelope's clock, negative while the key is still down.
        /// </summary>
        /// <remarks>
        ///     <c>Node_Sub31_Sub2.java:1188</c> sets it to 0 on note-off, and everything downstream
        ///     tests the sign rather than a separate flag. A voice with a negative release clock is
        ///     held; one with a non-negative clock is releasing.
        /// </remarks>
        internal int ReleaseClock { get; set; } = -1;

        /// <summary>Which pair of the release envelope the clock currently sits in.</summary>
        internal int ReleaseIndex { get; set; }

        /// <summary>Whether the voice has finished and should be dropped.</summary>
        internal bool Finished { get; set; }

        /// <summary>Playback position in the sample, 8.8 fixed point.</summary>
        internal int Position { get; set; }

        /// <summary>Playback step, 8.8 fixed point, negative while a ping-pong loop runs backwards.</summary>
        internal int Step { get; set; } = UnityStep;

        /// <summary>Whether the sample loops for as long as the voice lasts.</summary>
        /// <remarks>
        ///     Set from the tuning word's top bit at <c>Node_Sub31_Sub2.java:997-999</c>, not from
        ///     the sample. A sample with loop points that a non-held key names is played once.
        /// </remarks>
        internal bool Looping { get; set; }

        /// <summary>Current left gain, 6-bit fixed point.</summary>
        internal int LeftGain { get; set; }

        /// <summary>Current right gain.</summary>
        internal int RightGain { get; set; }

        /// <summary>Left gain to reach by the end of the current control tick.</summary>
        internal int TargetLeftGain { get; set; }

        /// <summary>Right gain to reach by the end of the current control tick.</summary>
        internal int TargetRightGain { get; set; }

        /// <summary>Starts a voice for one key of one patch.</summary>
        /// <param name="sample">The audio to play.</param>
        /// <param name="patch">The patch the key belongs to.</param>
        /// <param name="envelope">The envelope the key names.</param>
        /// <param name="channel">The MIDI channel.</param>
        /// <param name="note">The MIDI key.</param>
        /// <param name="muteGroup">The key's mute group, or -1.</param>
        /// <param name="baseGain">The velocity and patch term of the gain.</param>
        /// <param name="keyPan">The key's own pan.</param>
        /// <param name="pitchOffset">The pitch offset in 256ths of a semitone.</param>
        internal SynthVoice(PcmSample sample, MidiPatchDefinition patch, MidiPatchEnvelope? envelope, int channel,
            int note, int muteGroup, int baseGain, int keyPan, int pitchOffset) {
            Sample = sample;
            Patch = patch;
            Envelope = envelope;
            Channel = channel;
            Note = note;
            MuteGroup = muteGroup;
            BaseGain = baseGain;
            KeyPan = keyPan;
            PitchOffset = pitchOffset;
        }

        /// <summary>Whether the key that started this note is still down.</summary>
        internal bool Held => ReleaseClock < 0;

        /// <summary>
        ///     Mixes <paramref name="frames"/> frames of this voice into a stereo accumulator.
        /// </summary>
        /// <remarks>
        ///     <c>Node_Sub31_Sub5</c>'s interpolating stereo kernel (:15-21, :206-213). Two
        ///     divergences from it, both stated rather than hidden:
        ///     <list type="bullet">
        ///     <item>
        ///     The client picks between eight kernels and drops interpolation entirely when the step
        ///     is exactly unity and the position is aligned. That is a speed optimisation which
        ///     produces the same numbers, so there is one kernel here.
        ///     </item>
        ///     <item>
        ///     At a loop point or the end of the sample the client passes the exact neighbouring
        ///     sample in from the caller (:807-923); this clamps to the last sample in range
        ///     instead. It differs only on the single interpolated frame that straddles the
        ///     boundary, and only when the step is not unity.
        ///     </item>
        ///     </list>
        ///     The gain ramps linearly across the block, which is what
        ///     <c>Node_Sub31_Sub2.java:813-814</c> asks for by passing one control tick as the ramp
        ///     length: without it every ten milliseconds of envelope is a step change and the result
        ///     is audible as a buzz on every sustained note.
        /// </remarks>
        /// <param name="mix">The interleaved stereo accumulator.</param>
        /// <param name="offset">Where in it to start, in samples rather than frames.</param>
        /// <param name="frames">How many frames to mix.</param>
        internal void Mix(int[] mix, int offset, int frames) {
            sbyte[] pcm = Sample.Pcm;
            if (pcm.Length == 0 || frames <= 0) {
                Finished = true;
                return;
            }

            int last = pcm.Length - 1;
            int leftDelta = TargetLeftGain - LeftGain;
            int rightDelta = TargetRightGain - RightGain;

            for (int frame = 0; frame < frames; frame++) {
                if (!Advance(last))
                    break;

                int index = Position >> 8;
                int here = pcm[index];
                int next = pcm[index < last ? index + 1 : last];
                int value = (here << 8) + (next - here) * (Position & 0xff);

                int left = LeftGain + leftDelta * frame / frames;
                int right = RightGain + rightDelta * frame / frames;

                mix[offset + frame * 2] += (value * left) >> 6;
                mix[offset + frame * 2 + 1] += (value * right) >> 6;

                Position += Step;
            }

            LeftGain = TargetLeftGain;
            RightGain = TargetRightGain;
        }

        /// <summary>
        ///     Wraps, reflects or ends the voice when the position leaves the playable region.
        /// </summary>
        /// <remarks>
        ///     <c>Node_Sub31_Sub5.method1325</c> (:772-932). A ping-pong loop reflects about the
        ///     boundary and negates the step; a forward loop takes the position modulo the loop
        ///     length; a one-shot ends.
        /// </remarks>
        /// <param name="last">The highest valid sample index.</param>
        /// <returns>Whether the voice is still playable.</returns>
        private bool Advance(int last) {
            if (Looping && Sample.HasLoop) {
                int loopStart = Sample.LoopStart << 8;
                int loopEnd = Sample.LoopEnd << 8;
                int span = loopEnd - loopStart;
                if (span <= 0) {
                    Finished = true;
                    return false;
                }

                if (Sample.PingPong) {
                    if (Position >= loopEnd) {
                        Position = loopEnd + loopEnd - 1 - Position;
                        Step = -Step;
                    } else if (Position < loopStart) {
                        Position = loopStart + loopStart - Position;
                        Step = -Step;
                    }
                } else if (Position >= loopEnd) {
                    Position = loopStart + (Position - loopStart) % span;
                } else if (Position < loopStart) {
                    Position = loopStart;
                }
            }

            if (Position < 0 || (Position >> 8) > last) {
                Finished = true;
                return false;
            }

            return true;
        }
    }
}
