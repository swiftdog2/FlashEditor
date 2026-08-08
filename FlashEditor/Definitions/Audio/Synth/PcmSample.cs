using System;

namespace FlashEditor.Definitions.Audio.Synth {
    /// <summary>
    ///     One decoded instrument sample: signed 8-bit mono PCM with its rate and loop points.
    /// </summary>
    /// <remarks>
    ///     The client's <c>Node_Sub24_Sub1</c> (Node_Sub24_Sub1.java:7-11), which is what both sample
    ///     banks resolve to and what the voice mixer reads. Index 14 builds one at
    ///     <c>Node_Sub13.java:281</c> from the record's own header; index 4 builds one at
    ///     <c>Class37.java:70</c> at a fixed 22050 Hz with the loop points converted from
    ///     milliseconds.
    ///     <para>
    ///     8-bit is the client's own resolution, not a shortcut. Widening it would make the editor
    ///     sound better than the game.
    ///     </para>
    /// </remarks>
    public sealed class PcmSample {
        /// <summary>The audio, signed 8-bit mono.</summary>
        public sbyte[] Pcm { get; }

        /// <summary>Playback rate in Hz at which the sample sounds at its own pitch.</summary>
        public int SampleRate { get; }

        /// <summary>Where a loop returns to, in samples.</summary>
        public int LoopStart { get; }

        /// <summary>Where a loop turns round, in samples.</summary>
        public int LoopEnd { get; }

        /// <summary>
        ///     Whether the loop reverses at each end rather than jumping back to the start.
        /// </summary>
        /// <remarks>
        ///     <c>Node_Sub31_Sub5.java:819-826</c> reflects the position and negates the step, so a
        ///     ping-pong sample plays forwards then backwards without a discontinuity. Index-14
        ///     records carry it as the sign of their stored loop end; index-4 records never set it.
        /// </remarks>
        public bool PingPong { get; }

        /// <summary>Whether the sample has a usable loop region at all.</summary>
        public bool HasLoop => LoopEnd > LoopStart && LoopEnd <= Pcm.Length;

        /// <summary>Wraps decoded audio with the fields a voice needs to play it.</summary>
        /// <param name="pcm">The audio, signed 8-bit mono.</param>
        /// <param name="sampleRate">Its native rate in Hz.</param>
        /// <param name="loopStart">Where a loop returns to.</param>
        /// <param name="loopEnd">Where a loop turns round.</param>
        /// <param name="pingPong">Whether the loop reverses rather than jumping.</param>
        /// <exception cref="ArgumentNullException">The audio is null.</exception>
        public PcmSample(sbyte[] pcm, int sampleRate, int loopStart, int loopEnd, bool pingPong) {
            Pcm = pcm ?? throw new ArgumentNullException(nameof(pcm));
            SampleRate = sampleRate;
            LoopStart = loopStart;
            LoopEnd = loopEnd;
            PingPong = pingPong;
        }

        /// <summary>Reinterprets a decoder's unsigned buffer as the signed samples it holds.</summary>
        /// <remarks>
        ///     The Vorbis decoder writes <c>byte(value - 128)</c>, which is a signed sample stored in
        ///     an unsigned array; this is the reinterpretation and not a conversion.
        /// </remarks>
        /// <param name="pcm">The decoder's buffer.</param>
        /// <returns>The same bytes read as signed.</returns>
        public static sbyte[] AsSigned(byte[] pcm) {
            if (pcm == null)
                throw new ArgumentNullException(nameof(pcm));

            var signed = new sbyte[pcm.Length];
            for (int i = 0; i < pcm.Length; i++)
                signed[i] = unchecked((sbyte) pcm[i]);
            return signed;
        }
    }
}
