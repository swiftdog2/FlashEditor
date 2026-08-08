using System;
using System.IO;

namespace FlashEditor.Definitions.Audio.Sfx2.Vorbis {
    /// <summary>
    ///     The fully parsed contents of index 14 group 0: everything every sample on the index is
    ///     decoded through.
    /// </summary>
    /// <remarks>
    ///     <see cref="Sfx2SetupHeader"/> reads the first four fields and keeps the rest as bytes,
    ///     because the codec deliberately does not decode audio. This does decode it, transcribing
    ///     <c>Node_Sub13.method1143</c> (Node_Sub13.java:135-212) in full.
    ///     <para>
    ///     <b>This is not a Vorbis stream and no library will take it.</b> The group is a Vorbis
    ///     setup header payload with the two blocksize nibbles of the identification header stuck on
    ///     the front, and with the <c>\x05vorbis</c> packet header, the channel count, the sample
    ///     rate and the trailing framing bit all absent. Channels are one, implied by the client
    ///     emitting a single byte per sample (:266-270), and the rate is per sample rather than per
    ///     stream. So the decoder is written against the client and the format is checked against
    ///     itself: every codebook must open with the sync pattern, the floor type must be 1, and the
    ///     parse must land inside the group's last byte.
    ///     </para>
    ///     <para>
    ///     Parse once and share. It costs several megabytes of codebook vectors and every one of the
    ///     3,656 samples on the index is decoded through the same tables.
    ///     </para>
    /// </remarks>
    public sealed class VorbisSetup {
        internal VorbisCodebook[] Codebooks { get; }
        internal VorbisFloor[] Floors { get; }
        internal VorbisResidue[] Residues { get; }
        internal VorbisMapping[] Mappings { get; }
        internal bool[] ModeBlockFlags { get; }
        internal int[] ModeMappings { get; }

        /// <summary>The short window size.</summary>
        public int Blocksize0 { get; }

        /// <summary>The long window size.</summary>
        public int Blocksize1 { get; }

        /// <summary>How many bits of the group the parse consumed.</summary>
        /// <remarks>
        ///     Exposed so a test can assert exact consumption. A setup header is byte-aligned as a
        ///     whole, so a correct parse ends inside the final byte; ending anywhere else means a
        ///     field width is wrong, and nothing else in the decode path would report it.
        /// </remarks>
        public int ConsumedBits { get; }

        /// <summary>How many bits the group holds.</summary>
        public int TotalBits { get; }

        /// <summary>The trig and permutation tables for the short window.</summary>
        internal VorbisWindow ShortWindow { get; }

        /// <summary>The trig and permutation tables for the long window.</summary>
        internal VorbisWindow LongWindow { get; }

        /// <summary>The largest point count of any floor, which sizes a decoder's scratch.</summary>
        internal int MaximumFloorPoints { get; }

        /// <summary>Parses index 14's group 0.</summary>
        /// <param name="setup">The group as the codec read it.</param>
        /// <exception cref="ArgumentNullException">The group is null.</exception>
        /// <exception cref="InvalidDataException">The bytes are not a setup header this client could read.</exception>
        public VorbisSetup(Sfx2SetupHeader setup) : this((setup ?? throw new ArgumentNullException(nameof(setup))).RawBytes) {
        }

        /// <summary>Parses index 14's group 0 from its raw bytes.</summary>
        /// <param name="stored">The group's bytes.</param>
        /// <exception cref="ArgumentNullException">The bytes are null.</exception>
        /// <exception cref="InvalidDataException">The bytes are not a setup header this client could read.</exception>
        public VorbisSetup(byte[] stored) {
            if (stored == null)
                throw new ArgumentNullException(nameof(stored));

            var reader = new Sfx2BitReader(stored);
            TotalBits = stored.Length * 8;

            Blocksize0 = 1 << reader.Read(4);
            Blocksize1 = 1 << reader.Read(4);

            ShortWindow = new VorbisWindow(Blocksize0);
            LongWindow = new VorbisWindow(Blocksize1);

            Codebooks = new VorbisCodebook[reader.Read(8) + 1];
            for (int i = 0; i < Codebooks.Length; i++)
                Codebooks[i] = new VorbisCodebook(reader);

            //Time-domain transforms. The client allocates nothing for them and discards the value.
            int timeTransforms = reader.Read(6) + 1;
            for (int i = 0; i < timeTransforms; i++)
                reader.Read(16);

            Floors = new VorbisFloor[reader.Read(6) + 1];
            for (int i = 0; i < Floors.Length; i++) {
                Floors[i] = new VorbisFloor(reader);
                if (Floors[i].PointCount > MaximumFloorPoints)
                    MaximumFloorPoints = Floors[i].PointCount;
            }

            Residues = new VorbisResidue[reader.Read(6) + 1];
            for (int i = 0; i < Residues.Length; i++)
                Residues[i] = new VorbisResidue(reader);

            Mappings = new VorbisMapping[reader.Read(6) + 1];
            for (int i = 0; i < Mappings.Length; i++)
                Mappings[i] = new VorbisMapping(reader);

            int modes = reader.Read(6) + 1;
            ModeBlockFlags = new bool[modes];
            ModeMappings = new int[modes];
            for (int i = 0; i < modes; i++) {
                ModeBlockFlags[i] = reader.ReadBit() != 0;
                reader.Read(16);                //window type, always 0
                reader.Read(16);                //transform type, always 0
                ModeMappings[i] = reader.Read(8);
            }

            ConsumedBits = reader.BitPosition;
        }

        /// <summary>How many bits are read to select a mode, which is how the audio packet opens.</summary>
        /// <remarks><c>Node_Sub13.java:288</c>: <c>ilog(modes - 1)</c>, so a single-mode stream spends none.</remarks>
        internal int ModeBits => VorbisMath.Ilog(ModeBlockFlags.Length - 1);
    }
}
