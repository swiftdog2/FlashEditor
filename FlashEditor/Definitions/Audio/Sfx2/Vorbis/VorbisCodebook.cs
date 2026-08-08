using System;
using System.IO;

namespace FlashEditor.Definitions.Audio.Sfx2.Vorbis {
    /// <summary>
    ///     One Vorbis codebook: a Huffman code over entries, optionally carrying a vector of floats
    ///     per entry.
    /// </summary>
    /// <remarks>
    ///     Transcribed from <c>Class71</c> (Class71.java:42-259). The client builds the Huffman
    ///     decoder as a flat array of forward offsets rather than as a tree of objects
    ///     (<c>method712</c>, :140-236), and this keeps that shape: it is not a performance choice
    ///     here so much as a fidelity one, because the array is what decides which entry an
    ///     ambiguous code lands on and rebuilding it "properly" would be a second implementation
    ///     that only agrees with the first on well-formed books.
    ///     <para>
    ///     Every codebook opens with a 24-bit sync pattern that <c>Class71.java:44</c> reads and
    ///     discards without checking. This checks it, because it is the one field in the whole setup
    ///     header that is self-proving: if the previous codebook was parsed to the wrong bit, the
    ///     next sync will not be 0x564342.
    ///     </para>
    /// </remarks>
    internal sealed class VorbisCodebook {
        private readonly int[] huffman;
        private readonly float[][] valueVectors;

        /// <summary>How many floats an entry expands to; also the classword radix for a residue classbook.</summary>
        /// <remarks><c>Class71.anInt530</c>, read as <c>read(16)</c> at Class71.java:45.</remarks>
        internal int Dimensions { get; }

        /// <summary>How many entries the book holds.</summary>
        internal int Entries { get; }

        /// <summary>Whether the book carries value vectors, as opposed to being scalar only.</summary>
        internal bool HasValueVectors => valueVectors != null;

        /// <summary>Reads one codebook from the setup header.</summary>
        /// <param name="reader">The setup header's bit reader, positioned at the sync pattern.</param>
        /// <exception cref="InvalidDataException">The sync pattern is absent, which means the previous field was mis-sized.</exception>
        internal VorbisCodebook(Sfx2BitReader reader) {
            if (reader == null)
                throw new ArgumentNullException(nameof(reader));

            int sync = reader.Read(24);
            if (sync != Sfx2SetupHeader.VorbisCodebookSyncPattern)
                throw new InvalidDataException(
                    "A codebook must open with the sync pattern 0x" +
                    Sfx2SetupHeader.VorbisCodebookSyncPattern.ToString("x6") + " and this one opens with 0x" +
                    sync.ToString("x6") + " at bit " + (reader.BitPosition - 24) +
                    "; the field before it was read at the wrong width.");

            Dimensions = reader.Read(16);
            Entries = reader.Read(24);

            var lengths = new int[Entries];
            bool ordered = reader.ReadBit() != 0;

            if (ordered) {
                /* Lengths run in non-decreasing order, so the file states how many entries share
                   each length instead of stating a length per entry. */
                int entry = 0;
                int length = reader.Read(5) + 1;
                while (entry < Entries) {
                    int run = reader.Read(VorbisMath.Ilog(Entries - entry));
                    for (int i = 0; i < run; i++) {
                        if (entry >= Entries)
                            throw new InvalidDataException(
                                "An ordered codebook declares more entries at length " + length +
                                " than the book holds.");
                        lengths[entry++] = length;
                    }

                    length++;
                }
            } else {
                bool sparse = reader.ReadBit() != 0;
                for (int entry = 0; entry < Entries; entry++)
                    lengths[entry] = sparse && reader.ReadBit() == 0 ? 0 : reader.Read(5) + 1;
            }

            huffman = BuildHuffman(lengths);

            int lookupType = reader.Read(4);
            if (lookupType <= 0)
                return;

            float minimum = VorbisMath.Float32Unpack(Read32(reader));
            float delta = VorbisMath.Float32Unpack(Read32(reader));
            int valueBits = reader.Read(4) + 1;
            bool sequential = reader.ReadBit() != 0;

            int multiplicandCount = lookupType == 1
                ? VorbisMath.Lookup1Values(Entries, Dimensions)
                : Entries * Dimensions;

            var multiplicands = new int[multiplicandCount];
            for (int i = 0; i < multiplicandCount; i++)
                multiplicands[i] = reader.Read(valueBits);

            valueVectors = new float[Entries][];
            for (int entry = 0; entry < Entries; entry++)
                valueVectors[entry] = new float[Dimensions];

            if (lookupType == 1) {
                for (int entry = 0; entry < Entries; entry++) {
                    float last = 0.0F;
                    int divisor = 1;
                    for (int dimension = 0; dimension < Dimensions; dimension++) {
                        int index = entry / divisor % multiplicandCount;
                        float value = multiplicands[index] * delta + minimum + last;
                        valueVectors[entry][dimension] = value;
                        if (sequential)
                            last = value;
                        divisor *= multiplicandCount;
                    }
                }
            } else {
                for (int entry = 0; entry < Entries; entry++) {
                    float last = 0.0F;
                    int index = entry * Dimensions;
                    for (int dimension = 0; dimension < Dimensions; dimension++) {
                        float value = multiplicands[index] * delta + minimum + last;
                        valueVectors[entry][dimension] = value;
                        if (sequential)
                            last = value;
                        index++;
                    }
                }
            }
        }

        /// <summary>
        ///     Reads one entry number out of the packet, one bit at a time.
        /// </summary>
        /// <remarks>
        ///     <c>Class71.method714</c> (:241-251). The table holds a forward offset at a node and
        ///     the complement of an entry number at a leaf, so a non-negative cell means "keep
        ///     walking" and the sign bit is the only terminator.
        /// </remarks>
        /// <param name="reader">The packet's bit reader.</param>
        /// <returns>The entry number.</returns>
        internal int DecodeScalar(Sfx2BitReader reader) {
            int node = 0;
            while (huffman[node] >= 0)
                node = reader.ReadBit() != 0 ? huffman[node] : node + 1;

            return ~huffman[node];
        }

        /// <summary>Reads one entry and returns its value vector.</summary>
        /// <remarks><c>Class71.method715</c> (:256-258).</remarks>
        /// <param name="reader">The packet's bit reader.</param>
        /// <returns>The entry's vector, which the caller must not modify.</returns>
        internal float[] DecodeVector(Sfx2BitReader reader) {
            if (valueVectors == null)
                throw new InvalidDataException(
                    "This codebook carries no value vectors, so a residue that reads one from it has " +
                    "named the wrong book.");

            return valueVectors[DecodeScalar(reader)];
        }

        /// <summary>
        ///     Reads a 32-bit field as two 16-bit halves, low half first.
        /// </summary>
        /// <remarks>
        ///     The client's reader accumulates into a signed int and is called with a width of 32
        ///     for the two float fields, so the top bit lands in the sign. Splitting the read is
        ///     bit-for-bit identical - the reader fills from the low end upwards, so the two halves
        ///     occupy disjoint bit ranges and combining them with an or is the same as the client's
        ///     shifted addition.
        /// </remarks>
        /// <param name="reader">The bit reader.</param>
        /// <returns>The 32 bits.</returns>
        private static int Read32(Sfx2BitReader reader) {
            int low = reader.Read(16);
            int high = reader.Read(16);
            return low | (high << 16);
        }

        /// <summary>
        ///     Builds the flat Huffman table from the per-entry code lengths.
        /// </summary>
        /// <remarks>
        ///     Two passes, both transcribed from <c>Class71.method712</c> (:140-236). The first
        ///     assigns each entry its canonical code by keeping one "next free code" per length and
        ///     propagating a carry upward and downward when a code is consumed. The second walks each
        ///     code bit by bit through a growable array of forward offsets, storing <c>~entry</c>
        ///     where the walk ends.
        ///     <para>
        ///     Entries of length zero are skipped in both passes, which is what makes a sparse book
        ///     work at all.
        ///     </para>
        /// </remarks>
        /// <param name="lengths">Each entry's code length, zero for an unused entry.</param>
        /// <returns>The decode table.</returns>
        private static int[] BuildHuffman(int[] lengths) {
            var codes = new int[lengths.Length];
            var nextCode = new int[33];

            for (int entry = 0; entry < lengths.Length; entry++) {
                int length = lengths[entry];
                if (length == 0)
                    continue;

                int bit = 1 << (32 - length);
                int code = nextCode[length];
                codes[entry] = code;

                int successor;
                if ((code & bit) != 0) {
                    successor = nextCode[length - 1];
                } else {
                    successor = code | bit;
                    for (int shorter = length - 1; shorter >= 1; shorter--) {
                        int candidate = nextCode[shorter];
                        if (candidate != code)
                            break;

                        int shorterBit = 1 << (32 - shorter);
                        if ((candidate & shorterBit) != 0) {
                            nextCode[shorter] = nextCode[shorter - 1];
                            break;
                        }

                        nextCode[shorter] = candidate | shorterBit;
                    }
                }

                nextCode[length] = successor;

                for (int longer = length + 1; longer <= 32; longer++)
                    if (nextCode[longer] == code)
                        nextCode[longer] = successor;
            }

            var table = new int[8];
            int used = 0;

            for (int entry = 0; entry < lengths.Length; entry++) {
                int length = lengths[entry];
                if (length == 0)
                    continue;

                int code = codes[entry];
                int node = 0;

                for (int bit = 0; bit < length; bit++) {
                    int mask = (int) (0x80000000u >> bit);
                    if ((code & mask) != 0) {
                        if (table[node] == 0)
                            table[node] = used;
                        node = table[node];
                    } else {
                        node++;
                    }

                    if (node >= table.Length) {
                        var grown = new int[table.Length * 2];
                        Array.Copy(table, grown, table.Length);
                        table = grown;
                    }
                }

                table[node] = ~entry;
                if (node >= used)
                    used = node + 1;
            }

            return table;
        }
    }
}
