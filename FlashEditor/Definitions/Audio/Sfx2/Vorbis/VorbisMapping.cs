using System;

namespace FlashEditor.Definitions.Audio.Sfx2.Vorbis {
    /// <summary>
    ///     A Vorbis mapping: which floor and which residue a block's channel is decoded through.
    /// </summary>
    /// <remarks>
    ///     Transcribed from <c>Class371</c> (Class371.java:12-30). The client's reader is a
    ///     one-channel reader and reads the coupling and mux fields only far enough to skip them,
    ///     which is correct for this cache and would be wrong for a stereo stream: with one channel
    ///     the specification's per-step magnitude and angle fields are zero bits wide, so there is
    ///     nothing left to read after the step count.
    /// </remarks>
    internal sealed class VorbisMapping {
        /// <summary>How many submaps the mapping declares.</summary>
        internal int SubmapCount { get; }

        /// <summary>Which submap the single channel is routed through.</summary>
        /// <remarks>
        ///     <c>Class371.anInt3144</c>, read only when there is more than one submap (:20-22) and
        ///     zero otherwise. It indexes <see cref="Floors"/> and not the submap loop, which is why
        ///     it is kept separately from it.
        /// </remarks>
        internal int ChannelSubmap { get; }

        /// <summary>The floor each submap uses.</summary>
        internal int[] Floors { get; }

        /// <summary>The residue each submap uses.</summary>
        internal int[] Residues { get; }

        /// <summary>Reads one mapping from the setup header.</summary>
        /// <param name="reader">The setup header's bit reader.</param>
        internal VorbisMapping(Sfx2BitReader reader) {
            if (reader == null)
                throw new ArgumentNullException(nameof(reader));

            reader.Read(16);                                            //mapping type, unchecked by the client
            SubmapCount = reader.ReadBit() != 0 ? reader.Read(4) + 1 : 1;

            if (reader.ReadBit() != 0)
                reader.Read(8);                                         //coupling step count, unusable with one channel

            reader.Read(2);                                             //reserved

            if (SubmapCount > 1)
                ChannelSubmap = reader.Read(4);

            Floors = new int[SubmapCount];
            Residues = new int[SubmapCount];
            for (int i = 0; i < SubmapCount; i++) {
                reader.Read(8);                                         //time-domain transform, discarded
                Floors[i] = reader.Read(8);
                Residues[i] = reader.Read(8);
            }
        }
    }
}
