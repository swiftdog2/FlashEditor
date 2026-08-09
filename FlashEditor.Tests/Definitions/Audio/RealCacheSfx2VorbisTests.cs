using System;
using System.Collections.Generic;
using FlashEditor.Cache;
using FlashEditor.Definitions.Audio.Sfx2;
using FlashEditor.Definitions.Audio.Sfx2.Vorbis;
using FlashEditor.Tests.Cache.RealCache;
using Xunit;
using Xunit.Abstractions;
using FlashEditor.IO;

namespace FlashEditor.Tests.Definitions.Audio
{
    /// <summary>
    ///     Decodes every sample the index-14 reference table declares, all the way to PCM, and holds
    ///     each one to what the format states about itself.
    /// </summary>
    /// <remarks>
    ///     <b>This is the gate the track player stands on.</b> Index 14's setup header has no magic,
    ///     no channel count and no framing bit, so no off-the-shelf decoder opens it and the only
    ///     available specification is the client. That makes a transcription unverifiable by
    ///     comparison - there is nothing to compare against - so it is verified against the
    ///     redundancy already in the data instead.
    ///     <para>
    ///     The load-bearing assertion is <b>exact packet consumption</b>. A Vorbis packet is padded
    ///     to a byte boundary, so a correct decode of an <c>n</c>-byte packet consumes more than
    ///     <c>8(n-1)</c> bits and at most <c>8n</c>. Every codebook width, every floor field, every
    ///     residue partition and every classword feeds that number, and a single one read at the
    ///     wrong width moves the end of the packet somewhere else. Asserting it once per packet
    ///     across the whole index is what a byte-identity sweep is for a codec.
    ///     </para>
    ///     <para>
    ///     Nothing here says the audio <b>sounds</b> right, and nothing in this suite can.
    ///     <c>reference/track-player-listening-checklist.md</c> is where that is judged.
    ///     </para>
    /// </remarks>
    [Collection("RealCache")]
    public sealed class RealCacheSfx2VorbisTests : IClassFixture<RealCacheFixture>
    {
        private readonly RealCacheFixture _fixture;
        private readonly ITestOutputHelper _output;

        /// <summary>Binds the shared open cache and the per-test output sink.</summary>
        public RealCacheSfx2VorbisTests(RealCacheFixture fixture, ITestOutputHelper output)
        {
            _fixture = fixture;
            _output = output;
        }

        /// <summary>Reads one group of index 14 as the cache stores it.</summary>
        /// <param name="table">The index-14 reference table.</param>
        /// <param name="groupId">The group to read.</param>
        /// <returns>The unpacked file.</returns>
        private byte[] Group(RSReferenceTable table, int groupId)
        {
            byte[] stored = _fixture.RawContainer(RSConstants.SFX2_INDEX, groupId);
            Assert.True(stored != null, $"index 14 group {groupId} is declared but its index record is empty");

            int[] fileIds = table.GetArchiveEntry(groupId).GetValidFileIds();
            RSContainer container = _fixture.TryDecodeContainer(RSConstants.SFX2_INDEX, groupId, stored);
            Assert.True(container != null, $"index 14 group {groupId}: container would not decode");

            RSArchive archive = RSArchive.Decode(container.GetStream(), fileIds);
            return archive.GetFile(fileIds[0]).ToArray();
        }

        /// <summary>
        ///     Every sample on the index decodes, and every packet in every one of them ends inside
        ///     its own last byte.
        /// </summary>
        /// <remarks>
        ///     The counts printed are read from the reference table and from the records themselves
        ///     rather than written down here, because index 14's population is a property of the
        ///     loaded cache. The assertion is the relationship "every declared sample decoded and
        ///     every packet consumed exactly", which holds whichever cache is loaded.
        /// </remarks>
        [RealCacheFact]
        public void EverySample_DecodesAndEveryPacketEndsInsideItsOwnLastByte()
        {
            RSReferenceTable table = _fixture.Table(RSConstants.SFX2_INDEX);
            var groupIds = new List<int>(table.GetArchiveEntries().Keys);
            groupIds.Sort();

            var setup = new VorbisSetup(Group(table, Sfx2SetupHeader.SetupGroupId));
            Assert.True(setup.ConsumedBits > setup.TotalBits - 8 && setup.ConsumedBits <= setup.TotalBits,
                "the setup parse consumed " + setup.ConsumedBits + " of " + setup.TotalBits + " bits.");

            var failures = new List<string>();
            int decoded = 0;
            int packets = 0;
            long pcmBytes = 0;
            int silent = 0;
            int window = (setup.Blocksize1 + setup.Blocksize1) >> 2;

            foreach (int groupId in groupIds)
            {
                if (groupId == Sfx2SetupHeader.SetupGroupId)
                    continue;

                Sfx2Sample sample;
                Sfx2VorbisDecoder decoder;
                byte[] pcm;
                try
                {
                    sample = new Sfx2Sample { Id = groupId }.Decode(new JagStream(Group(table, groupId)));
                    decoder = new Sfx2VorbisDecoder(setup);
                    pcm = decoder.Decode(sample);
                }
                catch (Exception ex)
                {
                    failures.Add($"group {groupId}: {ex.GetType().Name}: {ex.Message}");
                    continue;
                }

                decoded++;
                packets += sample.PacketCount;
                pcmBytes += pcm.Length;

                if (!decoder.EveryPacketConsumedExactly)
                    failures.Add($"group {groupId}: a packet did not end inside its own last byte");

                int overshoot = decoder.ProducedSamples - sample.PcmByteCount;
                if (overshoot < 0 || overshoot >= window)
                    failures.Add($"group {groupId}: declares {sample.PcmByteCount} PCM bytes and produced " +
                                 $"{decoder.ProducedSamples}, an overshoot of {overshoot} outside 0..{window - 1}");

                bool anySound = false;
                foreach (byte b in pcm)
                {
                    if (b == 0)
                        continue;
                    anySound = true;
                    break;
                }

                if (!anySound)
                    silent++;
            }

            _output.WriteLine($"index 14: {groupIds.Count} groups declared, {decoded} samples decoded, " +
                              $"{packets} packets, {pcmBytes} PCM bytes, {silent} decoded to pure silence");
            _output.WriteLine($"setup: blocksizes {setup.Blocksize0}/{setup.Blocksize1}, " +
                              $"{setup.Codebooks.Length} codebooks, {setup.Floors.Length} floors, " +
                              $"{setup.Residues.Length} residues, {setup.Mappings.Length} mappings, " +
                              $"{setup.ModeBlockFlags.Length} modes, {setup.ConsumedBits}/{setup.TotalBits} bits");

            Assert.True(decoded == groupIds.Count - 1,
                $"{decoded} of {groupIds.Count - 1} declared samples decoded");
            Assert.True(failures.Count == 0,
                $"{failures.Count} of {decoded} samples failed:\n  " + string.Join("\n  ", failures.GetRange(0,
                    Math.Min(20, failures.Count))));

            /* A bank in which everything decoded to silence would satisfy every structural
               assertion above, so the population is held to being audible as well. This is a
               property of the data rather than a threshold anyone tuned: a sample that decodes to
               nothing but zero bytes is one the client would play as nothing. */
            Assert.True(silent * 20 < decoded,
                $"{silent} of {decoded} samples decoded to pure silence, which reads as a decode " +
                "producing nothing rather than as a bank of quiet effects");
        }
    }
}
