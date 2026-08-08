using System;
using System.IO;

namespace FlashEditor.Definitions.Audio.Sfx2.Vorbis {
    /// <summary>
    ///     A Vorbis residue: the fine spectral detail, read as vector-quantised partitions.
    /// </summary>
    /// <remarks>
    ///     Transcribed from <c>Class311</c> (Class311.java:5-90). Note the read order: the type,
    ///     begin and end fields are Java <b>field initialisers</b> (:9, :11, :13), so they run in
    ///     declaration order before the constructor body at :15 and are read first. Reading them in
    ///     the order they appear in the constructor instead would take the partition size as the
    ///     type and desynchronise the rest of the setup header.
    /// </remarks>
    internal sealed class VorbisResidue {
        private readonly int type;
        private readonly int begin;
        private readonly int end;
        private readonly int partitionSize;
        private readonly int classifications;
        private readonly int classbook;
        private readonly int[] books;

        /// <summary>Reads one residue configuration from the setup header.</summary>
        /// <param name="reader">The setup header's bit reader.</param>
        /// <exception cref="InvalidDataException">The type is one no Vorbis stream defines.</exception>
        internal VorbisResidue(Sfx2BitReader reader) {
            if (reader == null)
                throw new ArgumentNullException(nameof(reader));

            type = reader.Read(16);
            begin = reader.Read(24);
            end = reader.Read(24);

            /* The client does not check the type and would treat anything but 0 as type 2's layout
               (:64), so this check is stricter than the client. It is worth having because it turns
               a mis-sized earlier field into a named failure here rather than into silent noise. */
            if (type > 2)
                throw new InvalidDataException(
                    "Residue type " + type + " at bit " + (reader.BitPosition - 64) +
                    "; Vorbis defines 0, 1 and 2 only, so the field before it was read at the wrong width.");

            partitionSize = reader.Read(24) + 1;
            classifications = reader.Read(6) + 1;
            classbook = reader.Read(8);

            var cascade = new int[classifications];
            for (int i = 0; i < classifications; i++) {
                int high = 0;
                int low = reader.Read(3);
                if (reader.ReadBit() != 0)
                    high = reader.Read(5);
                cascade[i] = (high << 3) | low;
            }

            books = new int[classifications * 8];
            for (int i = 0; i < books.Length; i++)
                books[i] = (cascade[i >> 3] & (1 << (i & 7))) != 0 ? reader.Read(8) : -1;
        }

        /// <summary>
        ///     Clears the spectrum and, unless the packet has no floor, fills it from the packet.
        /// </summary>
        /// <remarks>
        ///     <c>Class311.method3619</c> (:36-90). The clear happens either way, which is what makes
        ///     a floorless packet decode to silence rather than to whatever the previous packet left
        ///     in the buffer.
        ///     <para>
        ///     The partition count is derived from the residue's own stored begin and end, not from
        ///     the block size, so a short block reads the same number of partitions as a long one.
        ///     That is what the client does and it is only harmless here because both block sizes in
        ///     this cache are 1024.
        ///     </para>
        /// </remarks>
        /// <param name="reader">The packet's bit reader.</param>
        /// <param name="codebooks">The setup header's codebooks.</param>
        /// <param name="spectrum">The spectrum buffer to fill.</param>
        /// <param name="length">How many bins to clear.</param>
        /// <param name="noFloor">Whether the packet carried no floor, in which case nothing is read.</param>
        internal void Decode(Sfx2BitReader reader, VorbisCodebook[] codebooks, float[] spectrum, int length,
            bool noFloor) {
            for (int i = 0; i < length; i++)
                spectrum[i] = 0.0F;

            if (noFloor)
                return;

            int classwordsPerCodeword = codebooks[classbook].Dimensions;
            int partitionsToRead = (end - begin) / partitionSize;
            var partitionClasses = new int[partitionsToRead];

            for (int pass = 0; pass < 8; pass++) {
                int partition = 0;
                while (partition < partitionsToRead) {
                    if (pass == 0) {
                        /* One codeword carries several partitions' classes packed as digits of a
                           base-`classifications` number, most significant first. */
                        int classword = codebooks[classbook].DecodeScalar(reader);
                        for (int i = classwordsPerCodeword - 1; i >= 0; i--) {
                            if (partition + i < partitionsToRead)
                                partitionClasses[partition + i] = classword % classifications;
                            classword /= classifications;
                        }
                    }

                    for (int i = 0; i < classwordsPerCodeword; i++) {
                        int book = books[partitionClasses[partition] * 8 + pass];
                        if (book >= 0) {
                            int offset = begin + partition * partitionSize;
                            VorbisCodebook codebook = codebooks[book];

                            if (type == 0) {
                                //Interleaved: one vector supplies every `step`-th bin.
                                int step = partitionSize / codebook.Dimensions;
                                for (int j = 0; j < step; j++) {
                                    float[] vector = codebook.DecodeVector(reader);
                                    for (int k = 0; k < codebook.Dimensions; k++)
                                        spectrum[offset + j + k * step] += vector[k];
                                }
                            } else {
                                //Contiguous: vectors are laid end to end across the partition.
                                int j = 0;
                                while (j < partitionSize) {
                                    float[] vector = codebook.DecodeVector(reader);
                                    for (int k = 0; k < codebook.Dimensions; k++)
                                        spectrum[offset + j++] += vector[k];
                                }
                            }
                        }

                        if (++partition >= partitionsToRead)
                            break;
                    }
                }
            }
        }
    }
}
