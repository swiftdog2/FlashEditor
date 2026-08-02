using FlashEditor.cache;
using FlashEditor.Tests.Cache.RealCache;
using System;
using System.Collections.Generic;
using System.Linq;
using Xunit;
using Xunit.Abstractions;

namespace FlashEditor.Tests.Cache
{
    /// <summary>
    ///     Runs every NPC definition in the cache through the production codec.
    /// </summary>
    /// <remarks>
    ///     The NPC decoder was derived from the Java client bundled with this cache, and that
    ///     client is build 637 while the cache is build 639 - see AGENTS.md. Unlike a model, an
    ///     NPC definition is a self-delimiting opcode stream: read an opcode byte, read the
    ///     payload its number implies, repeat until the terminator. Nothing in the file states
    ///     how long a payload is, so a decoder that mis-sizes even one of them consumes the
    ///     following opcode's bytes as payload and every field after that is garbage.
    ///     <para>
    ///     That makes exact buffer consumption the sharp instrument here. If the whole opcode
    ///     table is right the last byte read is the terminator and it is the last byte of the
    ///     buffer; if any payload size is wrong the walk almost certainly ends somewhere else.
    ///     Across 13,359 definitions the chance of a wrong table landing on the end every single
    ///     time is negligible, so this is as close to a proof of the 639 field layout as the
    ///     data can give.
    ///     </para>
    /// </remarks>
    public class RealCacheNpcDefinitionTests : IClassFixture<RealCacheFixture>
    {
        private readonly RealCacheFixture _cache;
        private readonly ITestOutputHelper _output;

        /// <summary>Failures listed before the report is truncated.</summary>
        private const int MaxReportedFailures = 10;

        /// <summary>
        ///     Sentinel bytes appended past the end of a definition so an over-read is visible.
        /// </summary>
        /// <remarks>
        ///     <see cref="JagStream.ReadByte"/> returns -1 at end of stream without advancing,
        ///     and the decode loop treats anything below 1 as the terminator. A decoder that ran
        ///     off the end would therefore still report a position equal to the length and look
        ///     exact. Decoding a padded copy removes that blind spot: reading past the true end
        ///     now advances into the padding and shows up as a positive overshoot.
        /// </remarks>
        private const int SentinelPadding = 32;

        /// <summary>A non-zero, non-255 pad byte, so it cannot be mistaken for a terminator.</summary>
        private const byte SentinelByte = 0xAA;

        /// <summary>Binds the shared open cache and the per-test output sink.</summary>
        public RealCacheNpcDefinitionTests(RealCacheFixture cache, ITestOutputHelper output)
        {
            _cache = cache;
            _output = output;
        }

        /// <summary>
        ///     Decodes every NPC definition and requires each one to land exactly on the end of
        ///     its own buffer - no bytes left over, none read past the end.
        /// </summary>
        [RealCacheFact]
        public void AllNpcDefinitions_Decode_AndConsumeTheirBufferExactly()
        {
            var failures = new List<string>();
            int decoded = 0;
            long bytes = 0;

            foreach ((int npcId, byte[] data) in Definitions())
            {
                decoded++;
                bytes += data.Length;

                //The genuine bytes first: this is the "decodes without throwing" assertion.
                try
                {
                    NPCDefinition unused = new NPCDefinition(new JagStream(data));
                }
                catch (Exception ex)
                {
                    failures.Add($"npc {npcId} ({data.Length} bytes): decode threw {ex.GetType().Name}: {ex.Message}");
                    continue;
                }

                //Then the padded copy, which is what actually pins the opcode payload sizes.
                long consumed;
                try
                {
                    var padded = new JagStream(Pad(data));
                    NPCDefinition unused = new NPCDefinition(padded);
                    consumed = padded.Position;
                }
                catch (Exception ex)
                {
                    failures.Add($"npc {npcId} ({data.Length} bytes): decode ran past the end - " +
                                 $"{ex.GetType().Name}: {ex.Message}");
                    continue;
                }

                if (consumed != data.Length)
                {
                    failures.Add($"npc {npcId}: consumed {consumed} of {data.Length} bytes " +
                                 $"({consumed - data.Length:+#;-#;0})");
                }
            }

            _output.WriteLine($"{decoded} NPC definitions decoded, {bytes} bytes consumed exactly");
            if (!_cache.FullSweep)
            {
                _output.WriteLine($"sampled up to {RealCacheFixture.SampleArchivesPerIndex} archives; " +
                                  $"set {RealCacheLocator.FullSweepVariable}=1 to decode every definition");
            }

            Assert.True(decoded > 0, "no NPC definition was decoded, so nothing was checked");
            AssertNoFailures(failures, "NPC definitions did not consume their buffer exactly");
        }

        /// <summary>
        ///     Re-encodes every decoded definition and requires the encoder to survive the real
        ///     data and to settle after one round trip.
        /// </summary>
        /// <remarks>
        ///     Byte-identical re-encode is deliberately reported rather than asserted, because
        ///     <see cref="NPCDefinition.Encode"/> cannot produce it for any definition in this
        ///     cache and the reason is structural, not a bug that can be patched here. The cache
        ///     stores opcodes in no particular order - only 127 of 13,359 definitions have them
        ///     in ascending numeric order - and 538 definitions repeat an opcode. Reproducing
        ///     the original bytes therefore needs the decoder to retain the opcode sequence it
        ///     saw, which the definition model does not do; the encoder instead emits a fixed
        ///     opcode order derived from the fields it holds.
        ///     <para>
        ///     What is assertable, and is asserted, is that the encoder round-trips through its
        ///     own decoder without loss: encode, decode, encode again, and the two encodings
        ///     must be byte-identical. That is the property a save path actually depends on, and
        ///     it is what caught the opcode-121 record-count defect already covered by
        ///     NPCDefinitionCodecTests.
        ///     </para>
        /// </remarks>
        [RealCacheFact]
        public void AllNpcDefinitions_ReEncode_WithoutLossThroughTheirOwnDecoder()
        {
            var failures = new List<string>();
            int encoded = 0;
            int byteIdentical = 0;

            foreach ((int npcId, byte[] data) in Definitions())
            {
                NPCDefinition definition;
                try
                {
                    definition = new NPCDefinition(new JagStream(data));
                }
                catch (Exception)
                {
                    //Already reported by the decode test; nothing to add here.
                    continue;
                }

                byte[] first;
                try
                {
                    first = definition.Encode().ToArray();
                    encoded++;
                }
                catch (Exception ex)
                {
                    failures.Add($"npc {npcId}: encode threw {ex.GetType().Name}: {ex.Message}");
                    continue;
                }

                if (first.Length == data.Length && first.AsSpan().SequenceEqual(data))
                    byteIdentical++;

                //The encoder's own output must be a well-formed opcode stream, or the editor
                //writes a cache it cannot read back.
                long consumed;
                byte[] second;
                try
                {
                    var padded = new JagStream(Pad(first));
                    NPCDefinition reread = new NPCDefinition(padded);
                    consumed = padded.Position;
                    second = reread.Encode().ToArray();
                }
                catch (Exception ex)
                {
                    failures.Add($"npc {npcId}: re-decoding the encoded stream threw " +
                                 $"{ex.GetType().Name}: {ex.Message}");
                    continue;
                }

                if (consumed != first.Length)
                {
                    failures.Add($"npc {npcId}: the encoded stream is {first.Length} bytes but " +
                                 $"re-decoding consumed {consumed}");
                    continue;
                }

                if (!second.AsSpan().SequenceEqual(first))
                {
                    failures.Add($"npc {npcId}: encode is not stable - {first.Length} bytes " +
                                 $"became {second.Length} on the second pass");
                }
            }

            _output.WriteLine($"{encoded} NPC definitions re-encoded, {byteIdentical} byte-identical to the cache");
            _output.WriteLine("byte-identical re-encode is not expected: the encoder emits a fixed opcode " +
                              "order while the cache stores opcodes unordered and sometimes repeated");
            if (!_cache.FullSweep)
            {
                _output.WriteLine($"sampled up to {RealCacheFixture.SampleArchivesPerIndex} archives; " +
                                  $"set {RealCacheLocator.FullSweepVariable}=1 to encode every definition");
            }

            Assert.True(encoded > 0, "no NPC definition was encoded, so nothing was checked");
            AssertNoFailures(failures, "NPC definitions did not survive a re-encode");
        }

        /// <summary>
        ///     A definition carrying none of the optional array opcodes must still encode.
        /// </summary>
        /// <remarks>
        ///     Regression test for an encoder that dereferenced seven optional arrays without a
        ///     null check. Opcode 42 is carried by no NPC in the rev-639 cache and opcode 1 by
        ///     only 12,146 of 13,359, so the unguarded encoder threw NullReferenceException for
        ///     every real definition, not merely for an unusual one.
        /// </remarks>
        [Fact]
        public void NPCDefinition_WithNoOptionalArrays_EncodesWithoutThrowing()
        {
            var empty = new JagStream();
            empty.WriteByte(0);
            empty.Flip();

            var definition = new NPCDefinition(new JagStream(empty.ToArray()));

            //Every optional array is absent, which is what used to make Encode throw.
            Assert.Null(definition.modelIds);
            Assert.Null(definition.recolorDstPalette);
            Assert.Null(definition.morphs);

            byte[] encoded = definition.Encode().ToArray();
            var reread = new NPCDefinition(new JagStream(encoded));

            //An absent opcode must stay absent rather than being invented by the encoder.
            Assert.Null(reread.modelIds);
            Assert.Null(reread.recolorDstPalette);
            Assert.Null(reread.morphs);
            Assert.Equal(encoded, reread.Encode().ToArray());
        }

        /// <summary>
        ///     An opcode with no known payload size must be reported, not skipped.
        /// </summary>
        /// <remarks>
        ///     Skipping it reads the next payload byte as an opcode, so the whole remainder of
        ///     the definition decodes into nonsense while appearing to succeed. The stream
        ///     carries no length prefix, so there is no way to recover - the only honest
        ///     outcome is to say where the parse stopped.
        /// </remarks>
        [Fact]
        public void NPCDefinition_UnknownOpcode_IsReportedRatherThanSilentlySkipped()
        {
            var stream = new JagStream();
            stream.WriteByte(12);
            stream.WriteByte(3);
            stream.WriteByte(200);   //no opcode 200 exists in any known revision of this format
            stream.WriteByte(1);
            stream.WriteByte(2);
            stream.WriteByte(0);
            stream.Flip();

            var thrown = Assert.Throws<InvalidOperationException>(
                () => new NPCDefinition(new JagStream(stream.ToArray())));
            Assert.Contains("200", thrown.Message);
        }

        /// <summary>
        ///     Confirms the exact-consumption check can actually fail, so a green sweep means
        ///     something.
        /// </summary>
        /// <remarks>
        ///     A trailing byte after the terminator leaves the stream short of its end, which is
        ///     precisely the shape a mis-sized payload produces. Without this the sweep could be
        ///     passing because the assertion is unreachable rather than because the codec is
        ///     right.
        /// </remarks>
        [Fact]
        public void ExactConsumption_DetectsAStreamThatIsNotFullyRead()
        {
            var stream = new JagStream();
            stream.WriteByte(12);
            stream.WriteByte(3);
            stream.WriteByte(0);     //terminator
            stream.WriteByte(99);    //slack the decoder must not reach
            stream.Flip();

            byte[] data = stream.ToArray();
            var reader = new JagStream(data);
            NPCDefinition unused = new NPCDefinition(reader);

            Assert.NotEqual(data.Length, reader.Position);
        }

        /// <summary>
        ///     Walks the NPC index, yielding every definition's raw bytes with its NPC id.
        /// </summary>
        /// <remarks>
        ///     Reads through the fixture rather than <see cref="RSCache.GetNPCDefinition"/>
        ///     because that path memoises every container it touches, which is fine for the one
        ///     definition an editor pane shows and ruinous across the whole index.
        /// </remarks>
        /// <returns>Each NPC id paired with the file bytes backing it.</returns>
        private IEnumerable<(int npcId, byte[] data)> Definitions()
        {
            RSReferenceTable table = _cache.Table(RSConstants.NPC_DEFINITIONS_INDEX);

            foreach (int archiveId in _cache.ArchivesToExamine(table))
            {
                byte[] stored = _cache.RawContainer(RSConstants.NPC_DEFINITIONS_INDEX, archiveId);
                if (stored == null)
                    continue;

                int[] fileIds = table.GetArchiveEntry(archiveId).GetValidFileIds();
                if (fileIds.Length == 0)
                    continue;

                RSContainer container =
                    _cache.TryDecodeContainer(RSConstants.NPC_DEFINITIONS_INDEX, archiveId, stored);
                if (container == null)
                    continue;

                RSArchive archive = RSArchive.Decode(container.GetStream(), fileIds);

                //NPC ids are packed 256 to an archive, the same mapping GetNPCDefinition uses.
                foreach (int fileId in fileIds)
                    yield return (archiveId * 256 + fileId, archive.GetFile(fileId).ToArray());
            }
        }

        private static byte[] Pad(byte[] data)
        {
            byte[] padded = new byte[data.Length + SentinelPadding];
            Array.Copy(data, padded, data.Length);
            for (int i = data.Length; i < padded.Length; i++)
                padded[i] = SentinelByte;
            return padded;
        }

        private static void AssertNoFailures(List<string> failures, string summary)
        {
            if (failures.Count == 0)
                return;

            string detail = string.Join(Environment.NewLine, failures.Take(MaxReportedFailures));
            if (failures.Count > MaxReportedFailures)
                detail += $"{Environment.NewLine}... and {failures.Count - MaxReportedFailures} more";

            Assert.Fail($"{failures.Count} {summary}:{Environment.NewLine}{detail}");
        }
    }
}
