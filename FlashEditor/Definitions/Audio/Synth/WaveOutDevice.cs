using System;
using System.Runtime.InteropServices;

namespace FlashEditor.Definitions.Audio.Synth {
    /// <summary>
    ///     A minimal streaming output device over the Windows multimedia <c>waveOut</c> API.
    /// </summary>
    /// <remarks>
    ///     <b>Why <c>winmm</c> rather than a package.</b> This project had no audio output of any
    ///     kind, so something had to be chosen. <c>winmm.dll</c> ships with every Windows this
    ///     application already requires - the project is <c>net9.0-windows</c> with WinForms - so it
    ///     adds no NuGet reference, no native redistributable and nothing to go stale. The
    ///     alternatives each cost something this does not: NAudio is a package to track and a
    ///     licence to carry for a few hundred lines of buffer plumbing, and OpenAL through the
    ///     OpenTK reference the project already has needs <c>OpenAL32.dll</c> or <c>soft_oal.dll</c>
    ///     present on the machine, which Windows does not provide.
    ///     <para>
    ///     What it costs: <c>waveOut</c> has higher latency than WASAPI and no shared-mode format
    ///     negotiation. Neither matters for playing a track back in an editor, where the buffer is
    ///     pre-rendered anyway.
    ///     </para>
    ///     <para>
    ///     Buffers are pinned for as long as the driver holds them. A buffer released while the
    ///     driver still owns it is a use-after-free inside the audio stack, which is why
    ///     <see cref="Dispose"/> resets the device before it unprepares anything.
    ///     </para>
    /// </remarks>
    public sealed class WaveOutDevice : IDisposable {
        private const int MmsyscallNoError = 0;
        private const int WaveMapper = -1;
        private const int WaveFormatPcm = 1;
        private const int WhdrDone = 0x00000001;
        private const int WhdrPrepared = 0x00000002;

        [StructLayout(LayoutKind.Sequential)]
        private struct WaveFormatEx {
            public short FormatTag;
            public short Channels;
            public int SamplesPerSecond;
            public int AverageBytesPerSecond;
            public short BlockAlign;
            public short BitsPerSample;
            public short Size;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct WaveHeader {
            public IntPtr Data;
            public int BufferLength;
            public int BytesRecorded;
            public IntPtr User;
            public int Flags;
            public int Loops;
            public IntPtr Next;
            public IntPtr Reserved;
        }

        [DllImport("winmm.dll")]
        private static extern int waveOutOpen(out IntPtr handle, int deviceId, ref WaveFormatEx format,
            IntPtr callback, IntPtr instance, int flags);

        [DllImport("winmm.dll")]
        private static extern int waveOutClose(IntPtr handle);

        [DllImport("winmm.dll")]
        private static extern int waveOutReset(IntPtr handle);

        [DllImport("winmm.dll")]
        private static extern int waveOutPause(IntPtr handle);

        [DllImport("winmm.dll")]
        private static extern int waveOutRestart(IntPtr handle);

        [DllImport("winmm.dll")]
        private static extern int waveOutPrepareHeader(IntPtr handle, IntPtr header, int size);

        [DllImport("winmm.dll")]
        private static extern int waveOutUnprepareHeader(IntPtr handle, IntPtr header, int size);

        [DllImport("winmm.dll")]
        private static extern int waveOutWrite(IntPtr handle, IntPtr header, int size);

        private readonly IntPtr[] headers;
        private readonly IntPtr[] buffers;
        private readonly int bufferBytes;
        private readonly int headerSize;
        private IntPtr handle;
        private int next;
        private bool disposed;

        /// <summary>How many frames each buffer holds.</summary>
        public int FramesPerBuffer { get; }

        /// <summary>How many samples make up one frame.</summary>
        public int Channels { get; }

        /// <summary>Opens the default output device.</summary>
        /// <remarks>
        ///     <b>The channel count is a parameter because the two callers disagree</b>, and it used
        ///     to be hardcoded to 2. The synthesiser renders interleaved stereo; an index-14 sound
        ///     effect is one mono channel. A mono buffer handed to a stereo device is read as half a
        ///     buffer of frames, so <c>Write</c> copied twice as many samples as the array held and
        ///     threw on the very first call - which is why no sound effect made any noise at all.
        /// </remarks>
        /// <param name="sampleRate">Frames per second.</param>
        /// <param name="framesPerBuffer">How many frames each queued buffer holds.</param>
        /// <param name="bufferCount">How many buffers to cycle through.</param>
        /// <param name="channels">Samples per frame: 1 for mono, 2 for interleaved stereo.</param>
        /// <exception cref="InvalidOperationException">The device would not open.</exception>
        public WaveOutDevice(int sampleRate, int framesPerBuffer, int bufferCount, int channels = 2) {
            if (channels != 1 && channels != 2)
                throw new ArgumentOutOfRangeException(nameof(channels), channels, "Mono or stereo only.");

            FramesPerBuffer = framesPerBuffer;
            Channels = channels;

            int blockAlign = channels * sizeof(short);
            bufferBytes = framesPerBuffer * blockAlign;
            headerSize = Marshal.SizeOf<WaveHeader>();

            var format = new WaveFormatEx {
                FormatTag = WaveFormatPcm,
                Channels = (short) channels,
                SamplesPerSecond = sampleRate,
                BitsPerSample = 16,
                BlockAlign = (short) blockAlign,
                AverageBytesPerSecond = sampleRate * blockAlign,
                Size = 0
            };

            int result = waveOutOpen(out handle, WaveMapper, ref format, IntPtr.Zero, IntPtr.Zero, 0);
            if (result != MmsyscallNoError)
                throw new InvalidOperationException(
                    "waveOutOpen failed with " + result + "; there is no usable output device.");

            headers = new IntPtr[bufferCount];
            buffers = new IntPtr[bufferCount];
            for (int i = 0; i < bufferCount; i++) {
                buffers[i] = Marshal.AllocHGlobal(bufferBytes);
                headers[i] = Marshal.AllocHGlobal(headerSize);
                Marshal.StructureToPtr(new WaveHeader(), headers[i], false);
            }
        }

        /// <summary>Whether the next buffer in the cycle is free for writing.</summary>
        /// <remarks>
        ///     The driver sets <c>WHDR_DONE</c> when it has finished with a buffer. Polling it is
        ///     enough here because the caller renders on its own thread and can afford to sleep;
        ///     a callback would buy lower latency and cost a marshalled delegate that has to outlive
        ///     the device.
        /// </remarks>
        public bool CanWrite {
            get {
                var header = Marshal.PtrToStructure<WaveHeader>(headers[next]);
                return (header.Flags & WhdrPrepared) == 0 || (header.Flags & WhdrDone) != 0;
            }
        }

        /// <summary>Queues one buffer of interleaved stereo frames.</summary>
        /// <param name="samples">The audio, two shorts per frame.</param>
        /// <param name="frames">How many frames of it to play.</param>
        /// <exception cref="InvalidOperationException">The device rejected the buffer.</exception>
        public void Write(short[] samples, int frames) {
            if (samples == null)
                throw new ArgumentNullException(nameof(samples));
            if (disposed)
                return;

            IntPtr headerPointer = headers[next];
            var header = Marshal.PtrToStructure<WaveHeader>(headerPointer);
            if ((header.Flags & WhdrPrepared) != 0) {
                int unprepared = waveOutUnprepareHeader(handle, headerPointer, headerSize);
                if (unprepared != MmsyscallNoError)
                    throw new InvalidOperationException("waveOutUnprepareHeader failed with " + unprepared + ".");
            }

            int bytes = Math.Min(bufferBytes, frames * Channels * sizeof(short));
            Marshal.Copy(samples, 0, buffers[next], bytes / sizeof(short));

            header = new WaveHeader { Data = buffers[next], BufferLength = bytes };
            Marshal.StructureToPtr(header, headerPointer, false);

            int prepared = waveOutPrepareHeader(handle, headerPointer, headerSize);
            if (prepared != MmsyscallNoError)
                throw new InvalidOperationException("waveOutPrepareHeader failed with " + prepared + ".");

            int written = waveOutWrite(handle, headerPointer, headerSize);
            if (written != MmsyscallNoError)
                throw new InvalidOperationException("waveOutWrite failed with " + written + ".");

            next = (next + 1) % headers.Length;
        }

        /// <summary>Whether every queued buffer has finished playing.</summary>
        /// <remarks>
        ///     <b>A caller that plays a finite sound has to wait for this before disposing.</b>
        ///     <see cref="Dispose"/> resets the device, which hands back every buffer the driver has
        ///     not finished with, so a sound short enough to still be queued is discarded rather than
        ///     played. Every index-14 effect is short enough: four 1024-frame buffers hold about
        ///     185 ms at 22 kHz, and most effects are shorter than that, so they were thrown away in
        ///     their entirety and the tab made no noise.
        /// </remarks>
        public bool Drained {
            get {
                if (handle == IntPtr.Zero)
                    return true;

                for (int i = 0; i < headers.Length; i++) {
                    var header = Marshal.PtrToStructure<WaveHeader>(headers[i]);

                    //Never queued at all, so there is nothing outstanding on this one.
                    if ((header.Flags & WhdrPrepared) == 0)
                        continue;

                    if ((header.Flags & WhdrDone) == 0)
                        return false;
                }

                return true;
            }
        }

        /// <summary>
        ///     Holds playback where it is, keeping every queued buffer.
        /// </summary>
        /// <remarks>
        ///     <b>This is what makes a pause a pause rather than a stop.</b> <see cref="Stop"/> is
        ///     <c>waveOutReset</c>, which hands back every buffer the driver has not finished with,
        ///     so what was still queued is discarded and resuming would have to re-render it -
        ///     which is only possible if the source can be rewound to the exact sample, and a
        ///     synthesiser cannot be. <c>waveOutPause</c> stops the write position advancing and
        ///     leaves the queue intact, so <see cref="Resume"/> carries on from the sample it
        ///     stopped on with no gap and no repeat.
        ///     <para>
        ///     Pausing a device with nothing queued is documented as having no effect, so there is
        ///     no ordering requirement against the first <see cref="Write"/>.
        ///     </para>
        /// </remarks>
        public void Pause() {
            if (handle != IntPtr.Zero)
                waveOutPause(handle);
        }

        /// <summary>Continues a paused device from where it stopped.</summary>
        /// <remarks>
        ///     Harmless on a device that is not paused, which is what lets the caller drive this
        ///     from a flag rather than having to track the device's state a second time.
        /// </remarks>
        public void Resume() {
            if (handle != IntPtr.Zero)
                waveOutRestart(handle);
        }

        /// <summary>Stops playback immediately and returns every queued buffer.</summary>
        public void Stop() {
            if (handle != IntPtr.Zero)
                waveOutReset(handle);
        }

        /// <summary>Closes the device and frees every pinned buffer.</summary>
        /// <remarks>
        ///     The reset comes first on purpose: freeing a buffer the driver is still reading is a
        ///     use-after-free inside the audio stack, which shows up as an intermittent crash a long
        ///     way from here.
        /// </remarks>
        public void Dispose() {
            if (disposed)
                return;

            disposed = true;

            if (handle != IntPtr.Zero) {
                waveOutReset(handle);

                for (int i = 0; i < headers.Length; i++) {
                    var header = Marshal.PtrToStructure<WaveHeader>(headers[i]);
                    if ((header.Flags & WhdrPrepared) != 0)
                        waveOutUnprepareHeader(handle, headers[i], headerSize);
                }

                waveOutClose(handle);
                handle = IntPtr.Zero;
            }

            for (int i = 0; i < headers.Length; i++) {
                if (buffers[i] != IntPtr.Zero)
                    Marshal.FreeHGlobal(buffers[i]);
                if (headers[i] != IntPtr.Zero)
                    Marshal.FreeHGlobal(headers[i]);

                buffers[i] = IntPtr.Zero;
                headers[i] = IntPtr.Zero;
            }
        }
    }
}
