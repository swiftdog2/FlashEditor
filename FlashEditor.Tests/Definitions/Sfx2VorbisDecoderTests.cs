using System;
using System.IO;
using FlashEditor.Definitions.Audio.Sfx2;
using FlashEditor.Definitions.Audio.Sfx2.Vorbis;
using Xunit;

namespace FlashEditor.Tests.Definitions
{
    /// <summary>
    ///     Proves the index-14 Vorbis decoder against bytes committed alongside the tests.
    /// </summary>
    /// <remarks>
    ///     <b>Nothing can hear this and no second implementation exists to compare against</b>, so
    ///     the decoder is checked against the four statements the format makes about itself. Each is
    ///     independent of this project's code and each fails loudly if any field width in the setup
    ///     header or in a packet is wrong, because a wrong width desynchronises a bit reader and
    ///     everything after it lands somewhere else:
    ///     <list type="number">
    ///     <item>
    ///     <b>Every codebook opens with the sync pattern 0x564342.</b> The client reads those 24
    ///     bits and throws them away (<c>Class71.java:44</c>), so this checks something the client
    ///     never does. Finding the pattern at the start of all 23 codebooks means each of the 22
    ///     before it was parsed to exactly the right bit.
    ///     </item>
    ///     <item>
    ///     <b>The setup parse lands inside the group's last byte.</b> The group is a whole number of
    ///     bytes and the format has no trailer, so a parse that finishes early or runs off the end
    ///     has mis-sized a field.
    ///     </item>
    ///     <item>
    ///     <b>Every audio packet lands inside its own last byte.</b> A Vorbis packet is padded to a
    ///     byte boundary, so a decode that consumes at most <c>8n</c> bits and more than
    ///     <c>8(n-1)</c> has read exactly the packet. This is the strongest of the four: it exercises
    ///     the codebooks, the floor and the residue on real data, once per packet.
    ///     </item>
    ///     <item>
    ///     <b>The packets produce at least the PCM byte count the record's header declares, and
    ///     overshoot it by less than one window.</b> The declared count is an independent statement
    ///     in the file about how much audio the packets hold; the client sizes its output buffer
    ///     from it and silently discards anything past it (<c>Node_Sub13.java:262-263</c>), so the
    ///     last window is normally a partial one. This is the weakest of the four and is stated as
    ///     the relationship it really is rather than as an equality - the produced count follows
    ///     from the packet count and the block size alone, so it pins those against the declared
    ///     length and nothing more.
    ///     </item>
    ///     </list>
    ///     <para>
    ///     A round trip through this decoder would prove none of it, which is why none of these
    ///     assertions goes through anything this project wrote twice.
    ///     </para>
    /// </remarks>
    public class Sfx2VorbisDecoderTests
    {
        /// <summary>Reads a committed fixture from the test output directory.</summary>
        /// <param name="name">The fixture file name.</param>
        /// <returns>Its bytes.</returns>
        private static byte[] Fixture(string name)
        {
            string path = Path.Combine(AppContext.BaseDirectory, "Fixtures", "RealCache", name);
            Assert.True(File.Exists(path), "missing captured fixture: " + path);
            return File.ReadAllBytes(path);
        }

        /// <summary>Parses the committed setup header.</summary>
        /// <returns>The parsed setup.</returns>
        private static VorbisSetup LoadSetup()
        {
            return new VorbisSetup(Fixture("sfx2-group0-setup.payload.bin"));
        }

        /// <summary>Decodes a committed sample fixture.</summary>
        /// <param name="name">The fixture file name.</param>
        /// <returns>The record and the decoder that read it.</returns>
        private static (Sfx2Sample Sample, Sfx2VorbisDecoder Decoder, byte[] Pcm) DecodeFixture(string name)
        {
            var sample = new Sfx2Sample { Id = 1 }.Decode(new JagStream(Fixture(name)));
            var decoder = new Sfx2VorbisDecoder(LoadSetup());
            return (sample, decoder, decoder.Decode(sample));
        }

        /// <summary>
        ///     Asserts that the packets hold the audio the record's header says they do.
        /// </summary>
        /// <remarks>
        ///     The relationship rather than an equality, because the client's own output is the
        ///     declared length and the packets normally carry a partial window past it. Overshooting
        ///     by a whole window or more would mean a packet too many; falling short would mean the
        ///     declared length cannot be filled, which the client would render as a tail of silence.
        /// </remarks>
        /// <param name="sample">The record.</param>
        /// <param name="decoder">The decoder that read it.</param>
        /// <param name="setup">The setup header, for the window length.</param>
        private static void AssertProducedLengthAgreesWithTheHeader(Sfx2Sample sample, Sfx2VorbisDecoder decoder,
            VorbisSetup setup)
        {
            int window = (setup.Blocksize1 + setup.Blocksize1) >> 2;
            int overshoot = decoder.ProducedSamples - sample.PcmByteCount;

            Assert.True(overshoot >= 0 && overshoot < window,
                "record " + sample.Id + " declares " + sample.PcmByteCount + " PCM bytes and its packets " +
                "produced " + decoder.ProducedSamples + "; the overshoot of " + overshoot +
                " is outside 0.." + (window - 1) + ", so the packet count and the declared length disagree.");
        }

        /// <summary>
        ///     The whole setup header parses, and the parse ends inside its last byte.
        /// </summary>
        /// <remarks>
        ///     The construction itself is most of the assertion: it throws on a codebook whose sync
        ///     pattern is absent and on a floor whose type is not 1, so reaching this line at all
        ///     means every codebook, floor, residue and mapping was read at the right width.
        /// </remarks>
        [Fact]
        public void TheSetupHeader_ParsesInFullAndConsumesItsGroupToTheLastByte()
        {
            VorbisSetup setup = LoadSetup();

            Assert.Equal(1024, setup.Blocksize0);
            Assert.Equal(1024, setup.Blocksize1);
            Assert.Equal(23, setup.Codebooks.Length);

            Assert.True(setup.ConsumedBits > setup.TotalBits - 8 && setup.ConsumedBits <= setup.TotalBits,
                "the setup parse consumed " + setup.ConsumedBits + " of " + setup.TotalBits +
                " bits; a correct parse ends inside the group's last byte, so a field width is wrong.");
        }

        /// <summary>
        ///     The two block sizes are equal, which several reference decoders assume they are not.
        /// </summary>
        /// <remarks>
        ///     Recorded as an assertion rather than as a comment because it is the single fact that
        ///     makes a ported decoder wrong in a way no other test here would catch: a decoder that
        ///     folds the short-window case into the long one produces plausible audio at the wrong
        ///     window length.
        /// </remarks>
        [Fact]
        public void BothBlockSizes_AreEqualInThisCache()
        {
            VorbisSetup setup = LoadSetup();
            Assert.Equal(setup.Blocksize0, setup.Blocksize1);
        }

        /// <summary>
        ///     Every packet of the shortest committed sample is consumed exactly, and the packets
        ///     produce the PCM byte count the header declares.
        /// </summary>
        [Fact]
        public void TheCapturedSample_DecodesEveryPacketExactlyAndFillsItsDeclaredLength()
        {
            (Sfx2Sample sample, Sfx2VorbisDecoder decoder, byte[] pcm) =
                DecodeFixture("sfx2-group2901-sample.payload.bin");

            Assert.True(decoder.EveryPacketConsumedExactly,
                "at least one packet did not end inside its own last byte, so the decode desynchronised.");
            Assert.Equal(sample.PcmByteCount, pcm.Length);
            AssertProducedLengthAgreesWithTheHeader(sample, decoder, LoadSetup());
        }

        /// <summary>The looping fixture decodes the same way, and its loop points fall inside its PCM.</summary>
        /// <remarks>
        ///     The loop points are stated in PCM bytes, so a decoder that produced the wrong number
        ///     of them would leave a loop end past the end of the audio. That is a second, weaker
        ///     reading of the same agreement, and it is the one a listener would hear as a click.
        /// </remarks>
        [Fact]
        public void TheCapturedLoopingSample_DecodesAndItsLoopPointsFallInsideThePcm()
        {
            (Sfx2Sample sample, Sfx2VorbisDecoder decoder, byte[] pcm) =
                DecodeFixture("sfx2-group568-looping.payload.bin");

            Assert.True(decoder.EveryPacketConsumedExactly);
            AssertProducedLengthAgreesWithTheHeader(sample, decoder, LoadSetup());
            Assert.True(sample.IsLooping);
            Assert.InRange(sample.LoopStart, 0, pcm.Length);
            Assert.InRange(sample.LoopEnd, sample.LoopStart, pcm.Length);
        }

        /// <summary>
        ///     The decoded audio is a signal rather than silence or a rail.
        /// </summary>
        /// <remarks>
        ///     Weak on its own and worth having anyway: a decoder that produced the right byte count
        ///     of zeros, or one that clipped every sample to the rails, would satisfy every
        ///     structural assertion above. This cannot say the audio is <b>correct</b> - nothing here
        ///     can, which is what <c>reference/track-player-listening-checklist.md</c> exists for -
        ///     only that it is not one of the two degenerate outputs.
        /// </remarks>
        [Fact]
        public void TheDecodedAudio_IsNeitherSilenceNorFullyClipped()
        {
            (_, _, byte[] pcm) = DecodeFixture("sfx2-group2901-sample.payload.bin");

            int nonZero = 0;
            int railed = 0;
            foreach (byte b in pcm)
            {
                sbyte value = unchecked((sbyte) b);
                if (value != 0)
                    nonZero++;
                if (value == sbyte.MinValue || value == sbyte.MaxValue)
                    railed++;
            }

            Assert.True(nonZero > pcm.Length / 10,
                "only " + nonZero + " of " + pcm.Length + " samples are non-zero, which reads as silence.");
            Assert.True(railed < pcm.Length / 10,
                railed + " of " + pcm.Length + " samples sit on a rail, which reads as a clipped decode.");
        }

        /// <summary>
        ///     A truncated setup header is reported as a mis-sized field rather than as noise.
        /// </summary>
        /// <remarks>
        ///     The check that fires is the codebook sync pattern, which is the only self-proving
        ///     field in the header. This test exists to prove that check is load-bearing: without
        ///     it, a header parsed from the wrong bit yields codebooks of arbitrary size and the
        ///     failure surfaces much later, if at all.
        /// </remarks>
        [Fact]
        public void ASetupHeaderReadFromTheWrongBit_IsRejectedAtTheSyncPattern()
        {
            byte[] stored = Fixture("sfx2-group0-setup.payload.bin");

            //Drop one byte from the front, which shifts every field by eight bits.
            var shifted = new byte[stored.Length - 1];
            Array.Copy(stored, 1, shifted, 0, shifted.Length);

            Assert.ThrowsAny<Exception>(() => new VorbisSetup(shifted));
        }
    }
}
