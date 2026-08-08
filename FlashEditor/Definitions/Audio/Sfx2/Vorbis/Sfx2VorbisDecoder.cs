using System;
using System.IO;

namespace FlashEditor.Definitions.Audio.Sfx2.Vorbis {
    /// <summary>
    ///     Turns an index-14 sample's Vorbis packets into the 8-bit signed mono PCM the client plays.
    /// </summary>
    /// <remarks>
    ///     Transcribed from <c>Node_Sub13.method1135</c> (Node_Sub13.java:284-492) for one packet and
    ///     <c>method1132</c> (:242-282) for the walk across a record's packets.
    ///     <para>
    ///     <b>Why a hand-written decoder rather than a library.</b> Index 14 has no Ogg framing, no
    ///     identification header, no channel count and no framing bit, so nothing off the shelf will
    ///     open it; and the two block sizes are equal, which several reference implementations
    ///     assume they are not. The client is therefore the only specification available, and this
    ///     follows it including the places where it is unusual.
    ///     </para>
    ///     <para>
    ///     <b>The output is 8-bit, and that is the client's choice rather than a shortcut here.</b>
    ///     <c>method1132</c> maps each float to <c>(int)(128 + f * 128)</c>, clamps by a trick that
    ///     turns any out-of-range value into 0 or -1, and writes <c>byte(value - 128)</c>. Widening
    ///     that to 16 bits would sound better than the game does.
    ///     </para>
    ///     <para>
    ///     One instance decodes one record. It carries the overlap-add state between packets, so it
    ///     is neither reusable nor thread-safe.
    ///     </para>
    /// </remarks>
    public sealed class Sfx2VorbisDecoder {
        private readonly VorbisSetup setup;
        private readonly VorbisFloorScratch floorScratch;

        private float[] current;
        private float[] previous;
        private int previousSize;
        private int previousRightSpan;
        private bool previousHadNoFloor;

        /// <summary>
        ///     How many PCM samples the packets produced, before the record's declared length clamps
        ///     them.
        /// </summary>
        /// <remarks>
        ///     The client discards the excess silently (<c>Node_Sub13.java:262-263</c>). Keeping the
        ///     raw figure is what lets a sweep assert that the declared <c>PcmByteCount</c> and the
        ///     packets agree, rather than assert that a truncation happened to fill the buffer.
        /// </remarks>
        public int ProducedSamples { get; private set; }

        /// <summary>
        ///     Whether every packet ended inside its own last byte.
        /// </summary>
        /// <remarks>
        ///     A Vorbis packet is padded to a byte boundary, so a correct decode of a packet of
        ///     <c>n</c> bytes consumes more than <c>8(n-1)</c> bits and at most <c>8n</c>. That makes
        ///     it a per-packet exact-consumption check, and it is the strongest evidence available
        ///     that the codebooks, floors and residues are all being read at the right widths - a
        ///     single wrong field width desynchronises the packet and lands the end somewhere else.
        /// </remarks>
        public bool EveryPacketConsumedExactly { get; private set; } = true;

        /// <summary>Creates a decoder for one record.</summary>
        /// <param name="setup">The index's shared setup header.</param>
        /// <exception cref="ArgumentNullException">The setup is null.</exception>
        public Sfx2VorbisDecoder(VorbisSetup setup) {
            this.setup = setup ?? throw new ArgumentNullException(nameof(setup));
            floorScratch = new VorbisFloorScratch(setup.MaximumFloorPoints);
            current = new float[setup.Blocksize1];
            previous = new float[setup.Blocksize1];
        }

        /// <summary>
        ///     Decodes a whole record to PCM.
        /// </summary>
        /// <remarks>
        ///     The buffer is exactly <see cref="Sfx2Sample.PcmByteCount"/> long because that is what
        ///     the client allocates (<c>Node_Sub13.java:250</c>) and what the sample's loop points
        ///     are stated in. If the packets produce fewer samples than that the tail stays zero,
        ///     which is what the client leaves too.
        /// </remarks>
        /// <param name="sample">The record.</param>
        /// <returns>Signed 8-bit mono PCM at the record's own sample rate.</returns>
        /// <exception cref="ArgumentNullException">The record is null.</exception>
        /// <exception cref="InvalidDataException">A packet does not decode.</exception>
        public byte[] Decode(Sfx2Sample sample) {
            if (sample == null)
                throw new ArgumentNullException(nameof(sample));

            var pcm = new byte[Math.Max(0, sample.PcmByteCount)];
            int written = 0;
            ProducedSamples = 0;

            for (int index = 0; index < sample.PacketCount; index++) {
                float[] block = DecodePacket(sample.Packet(index).ToArray());
                if (block == null)
                    continue;

                ProducedSamples += block.Length;

                int count = block.Length;
                if (count > pcm.Length - written)
                    count = pcm.Length - written;

                for (int i = 0; i < count; i++) {
                    int value = (int) (128.0F + block[i] * 128.0F);

                    /* The client's clamp, verbatim: anything outside 0..255 becomes 0 when it was
                       low and -1 when it was high, and the following subtraction then lands on
                       -128 and 127 respectively. Written as a comparison it would be the same
                       numbers, and would stop being a transcription. */
                    if ((value & ~0xff) != 0)
                        value = (value ^ -1) >> 31;

                    pcm[written++] = (byte) (value - 128);
                }
            }

            return pcm;
        }

        /// <summary>
        ///     Decodes one packet and returns the PCM window it completes, or null for the first
        ///     packet, which only primes the overlap.
        /// </summary>
        /// <remarks>
        ///     <c>Node_Sub13.method1135</c>. The returned length is
        ///     <c>(previousBlockSize + thisBlockSize) / 4</c>, so it depends on the two packets
        ///     either side of the boundary and not on this one alone.
        /// </remarks>
        /// <param name="packet">The packet's bytes.</param>
        /// <returns>The completed window, or null.</returns>
        private float[] DecodePacket(byte[] packet) {
            var reader = new Sfx2BitReader(packet);

            reader.ReadBit();                                   //packet type: audio packets carry 0
            int mode = reader.Read(setup.ModeBits);
            if (mode >= setup.ModeBlockFlags.Length)
                throw new InvalidDataException(
                    "Mode " + mode + " where the setup header declares " + setup.ModeBlockFlags.Length + ".");

            bool longBlock = setup.ModeBlockFlags[mode];
            int size = longBlock ? setup.Blocksize1 : setup.Blocksize0;
            VorbisWindow window = longBlock ? setup.LongWindow : setup.ShortWindow;

            bool previousLong = false;
            bool nextLong = false;
            if (longBlock) {
                previousLong = reader.ReadBit() != 0;
                nextLong = reader.ReadBit() != 0;
            }

            int half = size >> 1;

            /* Where the lapping windows rise and fall. A long block next to a short one only laps
               over the short block's width, centred in its own quarter. */
            int leftBegin;
            int leftEnd;
            int leftSpan;
            if (longBlock && !previousLong) {
                leftBegin = (size >> 2) - (setup.Blocksize0 >> 2);
                leftEnd = (size >> 2) + (setup.Blocksize0 >> 2);
                leftSpan = setup.Blocksize0 >> 1;
            } else {
                leftBegin = 0;
                leftEnd = half;
                leftSpan = size >> 1;
            }

            int rightBegin;
            int rightEnd;
            int rightSpan;
            if (longBlock && !nextLong) {
                rightBegin = size - (size >> 2) - (setup.Blocksize0 >> 2);
                rightEnd = size - (size >> 2) + (setup.Blocksize0 >> 2);
                rightSpan = setup.Blocksize0 >> 1;
            } else {
                rightBegin = half;
                rightEnd = size;
                rightSpan = size >> 1;
            }

            VorbisMapping mapping = setup.Mappings[setup.ModeMappings[mode]];
            VorbisFloor floor = setup.Floors[mapping.Floors[mapping.ChannelSubmap]];

            //A cleared floor bit means the whole block is silent, and the residue is skipped too.
            bool noFloor = !floor.DecodeCurve(reader, setup.Codebooks, floorScratch);

            for (int submap = 0; submap < mapping.SubmapCount; submap++)
                setup.Residues[mapping.Residues[submap]].Decode(reader, setup.Codebooks, current, half, noFloor);

            if (!noFloor)
                floor.ApplyCurve(floorScratch, current, half);

            if (reader.BitPosition <= (packet.Length - 1) * 8 || reader.BitPosition > packet.Length * 8)
                EveryPacketConsumedExactly = false;

            if (noFloor) {
                for (int i = half; i < size; i++)
                    current[i] = 0.0F;
            } else {
                InverseMdct(current, size, window);
                ApplyWindow(current, leftBegin, leftEnd, leftSpan, rightBegin, rightEnd, rightSpan);
            }

            float[] output = null;
            if (previousSize > 0) {
                output = new float[(previousSize + size) >> 2];

                //The tail of the previous block, unless that block was silent.
                if (!previousHadNoFloor)
                    for (int i = 0; i < previousRightSpan; i++)
                        output[i] += previous[(previousSize >> 1) + i];

                //The head of this one, unless this block is silent.
                if (!noFloor)
                    for (int i = leftBegin; i < half; i++)
                        output[output.Length - half + i] += current[i];
            }

            float[] swap = previous;
            previous = current;
            current = swap;

            previousSize = size;
            previousRightSpan = rightEnd - half;
            previousHadNoFloor = noFloor;

            return output;
        }

        /// <summary>
        ///     The lapping window, applied to the block's rising and falling edges.
        /// </summary>
        /// <remarks>
        ///     <c>Node_Sub13.java:458-466</c>. It is the Vorbis slope, <c>sin(pi/2 * sin^2(...))</c>,
        ///     evaluated in double precision per bin rather than tabulated.
        /// </remarks>
        /// <param name="block">The block.</param>
        /// <param name="leftBegin">Where the rising edge starts.</param>
        /// <param name="leftEnd">Where it ends.</param>
        /// <param name="leftSpan">Its width in bins.</param>
        /// <param name="rightBegin">Where the falling edge starts.</param>
        /// <param name="rightEnd">Where it ends.</param>
        /// <param name="rightSpan">Its width in bins.</param>
        private static void ApplyWindow(float[] block, int leftBegin, int leftEnd, int leftSpan,
            int rightBegin, int rightEnd, int rightSpan) {
            for (int i = leftBegin; i < leftEnd; i++) {
                float slope = (float) Math.Sin((i - leftBegin + 0.5) / leftSpan * 0.5 * Math.PI);
                block[i] *= (float) Math.Sin(Math.PI / 2.0 * slope * slope);
            }

            for (int i = rightBegin; i < rightEnd; i++) {
                float slope = (float) Math.Sin((i - rightBegin + 0.5) / rightSpan * 0.5 * Math.PI + Math.PI / 2.0);
                block[i] *= (float) Math.Sin(Math.PI / 2.0 * slope * slope);
            }
        }

        /// <summary>
        ///     The inverse MDCT, in place.
        /// </summary>
        /// <remarks>
        ///     <c>Node_Sub13.java:342-457</c>, transcribed stage for stage: the spectrum is halved
        ///     and mirrored, then pre-rotated, then run through <c>ilog(n - 1) - 3</c> butterfly
        ///     stages, then bit-reverse permuted, then post-rotated and unfolded into the time
        ///     domain.
        ///     <para>
        ///     It is left as one method on purpose. Splitting it into named stages would mean
        ///     naming stages nobody has verified the boundaries of, and every index in it is
        ///     relative to a different one of <c>n</c>, <c>n/2</c>, <c>n/4</c> and <c>n/8</c> - the
        ///     sort of code where a helpful rewrite is how a defect gets in.
        ///     </para>
        /// </remarks>
        /// <param name="block">The spectrum, which becomes the time-domain block.</param>
        /// <param name="size">The block size.</param>
        /// <param name="window">That block size's tables.</param>
        private static void InverseMdct(float[] block, int size, VorbisWindow window) {
            int half = size >> 1;
            int quarter = size >> 2;
            int eighth = size >> 3;

            float[] a = window.A;
            float[] b = window.B;
            float[] c = window.C;
            int[] bitReverse = window.BitReverse;

            for (int i = 0; i < half; i++)
                block[i] *= 0.5F;

            for (int i = half; i < size; i++)
                block[i] = -block[size - i - 1];

            for (int i = 0; i < quarter; i++) {
                float x = block[4 * i] - block[size - 4 * i - 1];
                float y = block[4 * i + 2] - block[size - 4 * i - 3];
                float cos = a[2 * i];
                float sin = a[2 * i + 1];
                block[size - 4 * i - 1] = x * cos - y * sin;
                block[size - 4 * i - 3] = x * sin + y * cos;
            }

            for (int i = 0; i < eighth; i++) {
                float x = block[half + 3 + 4 * i];
                float y = block[half + 1 + 4 * i];
                float u = block[4 * i + 3];
                float v = block[4 * i + 1];
                block[half + 3 + 4 * i] = x + u;
                block[half + 1 + 4 * i] = y + v;
                float cos = a[half - 4 - 4 * i];
                float sin = a[half - 3 - 4 * i];
                block[4 * i + 3] = (x - u) * cos - (y - v) * sin;
                block[4 * i + 1] = (y - v) * cos + (x - u) * sin;
            }

            int stages = VorbisMath.Ilog(size - 1);
            for (int stage = 0; stage < stages - 3; stage++) {
                int stride = size >> (stage + 2);
                int step = 8 << stage;
                for (int group = 0; group < 2 << stage; group++) {
                    int first = size - stride * 2 * group;
                    int second = size - stride * (2 * group + 1);
                    for (int i = 0; i < size >> (stage + 4); i++) {
                        int offset = 4 * i;
                        float x = block[first - 1 - offset];
                        float y = block[first - 3 - offset];
                        float u = block[second - 1 - offset];
                        float v = block[second - 3 - offset];
                        block[first - 1 - offset] = x + u;
                        block[first - 3 - offset] = y + v;
                        float cos = a[i * step];
                        float sin = a[i * step + 1];
                        block[second - 1 - offset] = (x - u) * cos - (y - v) * sin;
                        block[second - 3 - offset] = (y - v) * cos + (x - u) * sin;
                    }
                }
            }

            for (int i = 1; i < eighth - 1; i++) {
                int target = bitReverse[i];
                if (i >= target)
                    continue;

                int from = 8 * i;
                int to = 8 * target;
                for (int odd = 1; odd < 8; odd += 2) {
                    float swap = block[from + odd];
                    block[from + odd] = block[to + odd];
                    block[to + odd] = swap;
                }
            }

            for (int i = 0; i < half; i++)
                block[i] = block[2 * i + 1];

            for (int i = 0; i < eighth; i++) {
                block[size - 1 - 2 * i] = block[4 * i];
                block[size - 2 - 2 * i] = block[4 * i + 1];
                block[size - quarter - 1 - 2 * i] = block[4 * i + 2];
                block[size - quarter - 2 - 2 * i] = block[4 * i + 3];
            }

            for (int i = 0; i < eighth; i++) {
                float cos = c[2 * i];
                float sin = c[2 * i + 1];
                float x = block[half + 2 * i];
                float y = block[half + 2 * i + 1];
                float u = block[size - 2 - 2 * i];
                float v = block[size - 1 - 2 * i];
                float rotated = sin * (x - u) + cos * (y + v);
                block[half + 2 * i] = (x + u + rotated) * 0.5F;
                block[size - 2 - 2 * i] = (x + u - rotated) * 0.5F;
                rotated = sin * (y + v) - cos * (x - u);
                block[half + 2 * i + 1] = (y - v + rotated) * 0.5F;
                block[size - 1 - 2 * i] = (-y + v + rotated) * 0.5F;
            }

            for (int i = 0; i < quarter; i++) {
                block[i] = block[2 * i + half] * b[2 * i] + block[2 * i + 1 + half] * b[2 * i + 1];
                block[half - 1 - i] = block[2 * i + half] * b[2 * i + 1] - block[2 * i + 1 + half] * b[2 * i];
            }

            for (int i = 0; i < quarter; i++)
                block[size - quarter + i] = -block[i];

            for (int i = 0; i < quarter; i++)
                block[i] = block[quarter + i];

            for (int i = 0; i < quarter; i++)
                block[quarter + i] = -block[quarter - i - 1];

            for (int i = 0; i < quarter; i++)
                block[half + i] = block[size - i - 1];
        }
    }
}
