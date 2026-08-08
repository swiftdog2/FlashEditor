using System;
using System.Threading;
using FlashEditor.cache;

namespace FlashEditor.Definitions.Audio.Synth {
    /// <summary>
    ///     Plays one track through the cache's own instruments on a background thread.
    /// </summary>
    /// <remarks>
    ///     Owns the thread, the device and the renderer, so a caller starts and stops and does not
    ///     touch any of them. The thread renders ahead into a small ring of <c>waveOut</c> buffers
    ///     and sleeps when they are all queued; nothing here runs on the UI thread and nothing here
    ///     touches a control.
    ///     <para>
    ///     <b>The first seconds of a track are expensive.</b> Every distinct sample a patch names is
    ///     a full Vorbis decode of up to a couple of hundred kilobytes of PCM, and they are only
    ///     decoded when a note first asks for one. The bank caches them, so the cost is paid once
    ///     per sample per playback session rather than once per note.
    ///     </para>
    /// </remarks>
    public sealed class TrackPlayback : IDisposable {
        /// <summary>
        ///     Frames in each queued buffer, about 46 milliseconds.
        /// </summary>
        /// <remarks>
        ///     A whole number of control ticks so a buffer boundary never falls inside one, which
        ///     keeps the envelope clock and the buffer clock in step.
        /// </remarks>
        private const int FramesPerBuffer = MidiSynthesiser.ControlTick * 5;

        /// <summary>How many buffers to keep queued, which is what covers a scheduling hiccup.</summary>
        private const int BufferCount = 4;

        private readonly TrackRenderer renderer;
        private readonly Thread thread;
        private volatile bool stopping;
        private WaveOutDevice? device;
        private bool disposed;

        /// <summary>Whether playback is still running.</summary>
        public bool IsPlaying => thread.IsAlive && !stopping;

        /// <summary>Raised on the playback thread when the track ends of its own accord.</summary>
        public event Action? Completed;

        /// <summary>Raised on the playback thread when playback fails.</summary>
        public event Action<Exception>? Failed;

        /// <summary>The renderer, for its counters and the synthesiser's.</summary>
        public TrackRenderer Renderer => renderer;

        /// <summary>Starts playing a standard MIDI file through a cache's instruments.</summary>
        /// <param name="midi">The track's MIDI, as <c>Track.Midi</c> holds it.</param>
        /// <param name="cache">The cache to draw patches and samples from. It is only read.</param>
        /// <param name="loop">Whether to repeat when the track ends.</param>
        /// <exception cref="ArgumentNullException">The MIDI or the cache is null.</exception>
        /// <exception cref="System.IO.InvalidDataException">The MIDI will not parse.</exception>
        public TrackPlayback(byte[] midi, RSCache cache, bool loop) {
            if (cache == null)
                throw new ArgumentNullException(nameof(cache));

            var sequence = new MidiSequence(midi ?? throw new ArgumentNullException(nameof(midi)));
            renderer = new TrackRenderer(sequence, new MidiSynthesiser(new MidiSoundBank(cache))) { Loop = loop };

            thread = new Thread(Run) { IsBackground = true, Name = "FlashEditor track playback" };
            thread.Start();
        }

        /// <summary>Stops playback and waits briefly for the thread to notice.</summary>
        public void Stop() {
            stopping = true;
            device?.Stop();
            if (thread.IsAlive && thread != Thread.CurrentThread)
                thread.Join(TimeSpan.FromSeconds(2));
        }

        /// <summary>Stops playback and releases the device.</summary>
        public void Dispose() {
            if (disposed)
                return;

            disposed = true;
            Stop();
        }

        /// <summary>The playback thread: render ahead, queue, sleep.</summary>
        private void Run() {
            var buffer = new short[FramesPerBuffer * 2];

            try {
                device = new WaveOutDevice(MidiSynthesiser.OutputRate, FramesPerBuffer, BufferCount);

                while (!stopping) {
                    if (!device.CanWrite) {
                        //Every buffer is queued, so there is nothing to do until one drains. A
                        //shorter sleep than a buffer keeps the queue from running dry.
                        Thread.Sleep(10);
                        continue;
                    }

                    int frames = renderer.Render(buffer, FramesPerBuffer);
                    if (frames <= 0)
                        break;

                    //A short final block would otherwise play whatever the previous pass left in
                    //the tail of the buffer.
                    for (int i = frames * 2; i < buffer.Length; i++)
                        buffer[i] = 0;

                    device.Write(buffer, FramesPerBuffer);
                }

                if (!stopping)
                    Completed?.Invoke();
            } catch (Exception ex) {
                Failed?.Invoke(ex);
            } finally {
                WaveOutDevice? open = device;
                device = null;
                open?.Dispose();
            }
        }
    }
}
