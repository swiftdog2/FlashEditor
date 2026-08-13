using System;
using System.Threading;
using FlashEditor.Definitions.Audio.Synth;

namespace FlashEditor.Definitions.Audio.Sfx2 {
    /// <summary>
    ///     Plays one decoded sound effect, once.
    /// </summary>
    /// <remarks>
    ///     <b>Deliberately not the track player.</b> That one drives a synthesiser that renders
    ///     forever at a fixed rate; this plays a finite buffer at whatever rate the record stores,
    ///     and index 14's records run from 8 kHz to 44.1 kHz. Sharing the device would mean
    ///     resampling a sample to the synthesiser's rate to play it, which is a worse answer than
    ///     opening the device at the rate the file asks for.
    ///     <para>
    ///     <b>Mono, because the records are.</b> <c>Sfx2VorbisDecoder</c> produces one channel, and
    ///     the frame count handed to <c>Write</c> is therefore the sample count rather than half of
    ///     it - the interleaved-stereo assumption the track player makes would play every effect at
    ///     double speed and half length.
    ///     </para>
    /// </remarks>
    public sealed class Sfx2Playback : IDisposable {
        /// <summary>
        ///     Frames per buffer.
        /// </summary>
        /// <remarks>
        ///     Small, because an effect can be a few hundred milliseconds long and a buffer that
        ///     took a meaningful fraction of it would make Stop feel broken - the device finishes
        ///     whatever is queued before it goes quiet.
        /// </remarks>
        private const int FramesPerBuffer = 1024;

        private const int BufferCount = 4;

        private readonly short[] samples;
        private readonly int sampleRate;
        private readonly Thread thread;

        private volatile bool stopping;
        private volatile bool paused;

        /// <summary>Starts playing a decoded effect on its own thread.</summary>
        /// <param name="samples">The signed 16-bit mono samples.</param>
        /// <param name="sampleRate">The rate the record stores.</param>
        public Sfx2Playback(short[] samples, int sampleRate) {
            this.samples = samples ?? throw new ArgumentNullException(nameof(samples));

            //A record with a nonsense rate would open the device at it and either fail or play
            //something unrecognisable, so it is clamped to what waveOut will actually accept.
            this.sampleRate = Math.Clamp(sampleRate, 4000, 96000);

            thread = new Thread(Run) { IsBackground = true, Name = "Sfx2Playback" };
            thread.Start();
        }

        /// <summary>Raised on the playback thread when the effect finishes on its own.</summary>
        public event Action? Completed;

        /// <summary>Raised on the playback thread when the device or the feed failed.</summary>
        public event Action<Exception>? Failed;

        /// <summary>Whether the effect is still running, whether or not it is held.</summary>
        public bool IsPlaying => thread.IsAlive && !stopping;

        /// <summary>Whether a running effect is held part way through.</summary>
        public bool IsPaused => paused && IsPlaying;

        /// <summary>
        ///     Holds the effect where it is, without rewinding it.
        /// </summary>
        /// <remarks>
        ///     The position is an index into an array of samples that were decoded before playback
        ///     began, so nothing has to be preserved for a resume to continue from the right place -
        ///     unlike the track player, where the pause has to hold a whole synthesiser mid-voice.
        ///     What the flag does buy is the same as there: the queued buffers stay queued rather
        ///     than being reset away, so the join is inaudible.
        ///     <para>
        ///     Read by the playback thread rather than acted on here. The device belongs to that
        ///     thread and does not exist at all between construction and the first loop pass, so a
        ///     caller touching it from outside would be racing the field.
        ///     </para>
        /// </remarks>
        public void Pause() {
            paused = true;
        }

        /// <summary>Continues a held effect from the sample it stopped on.</summary>
        public void Resume() {
            paused = false;
        }

        /// <summary>Stops playing and releases the device.</summary>
        /// <remarks>
        ///     Joins with a timeout rather than forever: the thread's only blocking wait is a ten
        ///     millisecond sleep, so it always returns quickly, and a hang here would take the UI
        ///     thread with it.
        /// </remarks>
        public void Dispose() {
            stopping = true;
            thread.Join(TimeSpan.FromSeconds(2));
        }

        private void Run() {
            WaveOutDevice? device = null;
            var buffer = new short[FramesPerBuffer];

            try {
                //Mono, matching the record. The device used to be stereo only, and a mono buffer
                //handed to it is read as half a buffer of frames - twice as many samples as the
                //array holds - so the first Write threw and nothing ever played.
                device = new WaveOutDevice(sampleRate, FramesPerBuffer, BufferCount, channels: 1);

                int position = 0;

                /* What the device was last told, so a transition is applied once rather than on
                   every pass. waveOutPause and waveOutRestart are both idempotent, but calling
                   either a hundred times a second is noise in the audio stack for no reason. */
                bool held = false;

                while (!stopping && position < samples.Length) {
                    if (paused) {
                        if (!held) {
                            device.Pause();
                            held = true;
                        }

                        Thread.Sleep(10);
                        continue;
                    }

                    if (held) {
                        device.Resume();
                        held = false;
                    }

                    if (!device.CanWrite) {
                        //Every buffer is queued, so there is nothing to do until one drains.
                        Thread.Sleep(10);
                        continue;
                    }

                    int frames = Math.Min(FramesPerBuffer, samples.Length - position);
                    Array.Copy(samples, position, buffer, 0, frames);

                    //A short final block would otherwise replay whatever the previous pass left in
                    //the tail of the buffer, which is an audible click on every effect.
                    for (int i = frames; i < buffer.Length; i++)
                        buffer[i] = 0;

                    device.Write(buffer, FramesPerBuffer);
                    position += frames;
                }

                /* Wait for the driver to finish what is queued before the finally block disposes
                   the device, because disposing resets it and a reset hands back every buffer the
                   driver has not played yet. Four 1024-frame buffers are about 185 ms at 22 kHz and
                   most effects are shorter than that, so without this wait the entire sound was
                   discarded and the tab was silent. Bounded, so a driver that never reports done
                   cannot hang the thread. */
                for (int spin = 0; spin < 500 && !stopping && !paused && !device.Drained; spin++)
                    Thread.Sleep(10);

                if (!stopping)
                    Completed?.Invoke();
            }
            catch (Exception ex) {
                Failed?.Invoke(ex);
            }
            finally {
                device?.Dispose();
            }
        }
    }
}
