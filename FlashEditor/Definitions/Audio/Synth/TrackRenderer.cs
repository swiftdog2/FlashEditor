using System;

namespace FlashEditor.Definitions.Audio.Synth {
    /// <summary>
    ///     Drives a sequence through a synthesiser and produces the 16-bit stereo the output device
    ///     wants.
    /// </summary>
    /// <remarks>
    ///     The sequencer half is <c>Node_Sub31_Sub2.method1325</c> (:311-340), which splits the block
    ///     it was asked for at the next event rather than dispatching events on a grid; the format
    ///     half is <c>Class268_Sub2.method3257</c> (:82-97), which clamps the 24-bit accumulator and
    ///     writes its top 16 bits.
    ///     <para>
    ///     Rendering is pull-based and stateful, so one renderer plays one track once and is not
    ///     thread-safe. That is what lets the caller decide between streaming it to a device and
    ///     writing it to a file without the two paths disagreeing about anything.
    ///     </para>
    /// </remarks>
    public sealed class TrackRenderer {
        /// <summary>
        ///     The accumulator's headroom before it is clamped.
        /// </summary>
        /// <remarks>
        ///     <c>Class268_Sub2.java:90-91</c> clamps to plus or minus this and then takes bits 8 to
        ///     23, so the mix has eight bits of headroom above a full-scale voice. That is why a
        ///     dense chord does not distort where the same voices would clip a 16-bit accumulator.
        /// </remarks>
        private const int ClampLimit = 0x7fffff;

        private readonly MidiSequence sequence;
        private readonly MidiSynthesiser synthesiser;
        private readonly int[] mix = new int[MidiSynthesiser.ControlTick * 2];

        private volatile bool loop;
        private int eventIndex;
        private long lastTick;
        private double lastEventSample;
        private double samplesPerTick;
        private double sampleClock;

        /// <summary>Whether every event has been dispatched and no voice is left sounding.</summary>
        public bool Finished => eventIndex >= sequence.Events.Count && synthesiser.ActiveVoices == 0;

        /// <summary>How many frames have been rendered.</summary>
        public long RenderedFrames { get; private set; }

        /// <summary>The synthesiser being driven, for its counters.</summary>
        public MidiSynthesiser Synthesiser => synthesiser;

        /// <summary>Whether to restart the sequence when it runs out rather than stopping.</summary>
        /// <remarks>
        ///     The one member of this class that may be touched from another thread, and the
        ///     backing field is <c>volatile</c> for that reason. It is what a Loop control in a
        ///     transport is bound to, so it is written from the UI thread while
        ///     <see cref="Render"/> reads it on the playback thread; an ordinary field could be
        ///     hoisted out of that loop and the change would never be seen. Everything else here is
        ///     sequencer state and remains single-threaded.
        /// </remarks>
        public bool Loop {
            get => loop;
            set => loop = value;
        }

        /// <summary>Binds a sequence to a synthesiser.</summary>
        /// <param name="sequence">The events to play.</param>
        /// <param name="synthesiser">The synthesiser to play them through.</param>
        /// <exception cref="ArgumentNullException">Either argument is null.</exception>
        public TrackRenderer(MidiSequence sequence, MidiSynthesiser synthesiser) {
            this.sequence = sequence ?? throw new ArgumentNullException(nameof(sequence));
            this.synthesiser = synthesiser ?? throw new ArgumentNullException(nameof(synthesiser));

            //120 beats per minute until a tempo event says otherwise (Class173.java:165).
            SetTempo(500000);
        }

        /// <summary>
        ///     Renders the next stretch of audio as interleaved signed 16-bit stereo.
        /// </summary>
        /// <param name="output">Where to write, two shorts per frame.</param>
        /// <param name="frames">How many frames to produce.</param>
        /// <returns>How many frames were produced, which is fewer than asked at the end of a track.</returns>
        /// <exception cref="ArgumentNullException">The buffer is null.</exception>
        public int Render(short[] output, int frames) {
            if (output == null)
                throw new ArgumentNullException(nameof(output));

            int produced = 0;
            while (produced < frames) {
                if (Finished) {
                    if (!Loop)
                        break;

                    Restart();
                }

                DispatchDueEvents();

                int chunk = Math.Min(frames - produced, MidiSynthesiser.ControlTick);
                chunk = Math.Min(chunk, FramesUntilNextEvent());
                if (chunk <= 0)
                    chunk = 1;

                synthesiser.Render(mix, chunk);

                for (int i = 0; i < chunk * 2; i++) {
                    int value = mix[i];
                    if (value > ClampLimit)
                        value = ClampLimit;
                    else if (value < -ClampLimit)
                        value = -ClampLimit;

                    output[(produced * 2) + i] = (short) (value >> 8);
                }

                produced += chunk;
                sampleClock += chunk;
                RenderedFrames += chunk;
            }

            return produced;
        }

        /// <summary>Rewinds to the start of the sequence, silencing anything still sounding.</summary>
        private void Restart() {
            synthesiser.AllSoundOff();
            synthesiser.ResetControllers();
            eventIndex = 0;
            lastTick = 0;
            lastEventSample = sampleClock;
            SetTempo(500000);
        }

        /// <summary>Applies every event whose time has arrived.</summary>
        private void DispatchDueEvents() {
            while (eventIndex < sequence.Events.Count) {
                MidiSequenceEvent next = sequence.Events[eventIndex];
                double due = lastEventSample + (next.Tick - lastTick) * samplesPerTick;
                if (due > sampleClock)
                    return;

                lastEventSample = due;
                lastTick = next.Tick;
                eventIndex++;

                if (next.IsTempo)
                    SetTempo(next.Data1);
                else
                    synthesiser.Send(next.Status, next.Data1, next.Data2);
            }
        }

        /// <summary>How many frames may be rendered before the next event is due.</summary>
        /// <returns>The gap, or a whole control tick when nothing is pending.</returns>
        private int FramesUntilNextEvent() {
            if (eventIndex >= sequence.Events.Count)
                return MidiSynthesiser.ControlTick;

            MidiSequenceEvent next = sequence.Events[eventIndex];
            double due = lastEventSample + (next.Tick - lastTick) * samplesPerTick;
            double gap = due - sampleClock;
            return gap <= 0 ? 1 : (int) Math.Min(MidiSynthesiser.ControlTick, Math.Ceiling(gap));
        }

        /// <summary>
        ///     Sets how many output frames one sequence tick lasts.
        /// </summary>
        /// <remarks>
        ///     The client keeps its clock in microseconds and multiplies by the division
        ///     (<c>Node_Sub31_Sub2.java:314-317</c>); this states the same relationship in frames,
        ///     which is the unit everything else here counts in.
        /// </remarks>
        /// <param name="microsecondsPerQuarter">The tempo as the file states it.</param>
        private void SetTempo(int microsecondsPerQuarter) {
            if (microsecondsPerQuarter <= 0)
                return;

            samplesPerTick = (double) MidiSynthesiser.OutputRate * microsecondsPerQuarter /
                             (1000000.0 * sequence.Division);
        }
    }
}
