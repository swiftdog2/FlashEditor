using System;

namespace FlashEditor.Definitions.Audio.Sfx2.Vorbis {
    /// <summary>
    ///     The trigonometric and bit-reversal tables the inverse MDCT of one block size needs.
    /// </summary>
    /// <remarks>
    ///     Built by <c>Node_Sub13.method1143</c> (Node_Sub13.java:141-177), once per block size at
    ///     setup time. The client builds the two sets independently and never assumes the short
    ///     window is shorter than the long one, which matters here: <b>both block sizes are 1024 in
    ///     both supported caches</b>, so a decoder ported from a reference implementation that folds
    ///     the two cases together on that assumption will mis-window every packet.
    /// </remarks>
    internal sealed class VorbisWindow {
        /// <summary>The block size this set of tables serves.</summary>
        internal int Size { get; }

        /// <summary>The half-size table indexed through the butterfly stages.</summary>
        /// <remarks><c>aFloatArray3886</c> for the short window and <c>aFloatArray3883</c> for the long.</remarks>
        internal float[] A { get; }

        /// <summary>The half-size table applied in the final rotation.</summary>
        /// <remarks><c>aFloatArray3899</c> and <c>aFloatArray3888</c>.</remarks>
        internal float[] B { get; }

        /// <summary>The quarter-size table applied to the interleaved halves.</summary>
        /// <remarks><c>aFloatArray3907</c> and <c>aFloatArray3887</c>.</remarks>
        internal float[] C { get; }

        /// <summary>The eighth-size bit-reversal permutation.</summary>
        /// <remarks><c>anIntArray3897</c> and <c>anIntArray3891</c>.</remarks>
        internal int[] BitReverse { get; }

        /// <summary>Builds the tables for one block size.</summary>
        /// <param name="size">The block size, a power of two.</param>
        internal VorbisWindow(int size) {
            Size = size;

            int half = size >> 1;
            int quarter = size >> 2;
            int eighth = size >> 3;

            A = new float[half];
            for (int i = 0; i < quarter; i++) {
                A[2 * i] = (float) Math.Cos(4 * i * Math.PI / size);
                A[2 * i + 1] = -(float) Math.Sin(4 * i * Math.PI / size);
            }

            B = new float[half];
            for (int i = 0; i < quarter; i++) {
                B[2 * i] = (float) Math.Cos((2 * i + 1) * Math.PI / (2 * size));
                B[2 * i + 1] = (float) Math.Sin((2 * i + 1) * Math.PI / (2 * size));
            }

            C = new float[quarter];
            for (int i = 0; i < eighth; i++) {
                C[2 * i] = (float) Math.Cos((4 * i + 2) * Math.PI / size);
                C[2 * i + 1] = -(float) Math.Sin((4 * i + 2) * Math.PI / size);
            }

            BitReverse = new int[eighth];
            int bits = VorbisMath.Ilog(eighth - 1);
            for (int i = 0; i < eighth; i++)
                BitReverse[i] = VorbisMath.BitReverse(bits, i);
        }
    }
}
