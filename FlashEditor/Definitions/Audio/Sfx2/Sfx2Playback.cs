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
                device = new WaveOutDevice(sampleRate, FramesPerBuffer, BufferCount);

                int position = 0;
                while (!stopping && position < samples.Length) {
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
