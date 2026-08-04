using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using FlashEditor.cache;
using FlashEditor.Definitions.Audio.Sfx2;
using FlashEditor.Tests.Cache.RealCache;
using Xunit;
using Xunit.Abstractions;

namespace FlashEditor.Tests.Definitions
{
    /// <summary>
    ///     Decodes every group the index-14 reference table declares and requires each one to
    ///     re-encode to the bytes it came from.
    /// </summary>
    /// <remarks>
    ///     The index holds two unrelated record shapes and the sweep covers both, because a sweep
    ///     that quietly skipped group 0 would leave the one group nothing else on the index looks
    ///     like undefended. That is also why this class does not use
    ///     <see cref="DefinitionSweep{T}"/>: its exact-consumption check pads the record and requires
    ///     the decoder to stop on the original length, and the setup header has no length field at
    ///     all - it is the whole file - so it would read the padding and be reported as an over-read.
    ///     The consumption check therefore applies to the samples here, and group 0 is pinned by the
    ///     Vorbis sync pattern instead.
    ///     <para>
    ///     Every population comes off the reference table on each run. Index 14 happens to be
    ///     identical in both supported caches, but that is a fact about today's two caches rather
    ///     than about the codec, so nothing here is written down.
    ///     </para>
    /// </remarks>
    [Collection("RealCache")]
    public sealed class RealCacheSfx2Tests : IClassFixture<RealCacheFixture>
    {
        private readonly RealCacheFixture _fixture;
        private readonly ITestOutputHelper _output;

        /// <summary>Binds the shared open cache and the per-test output sink.</summary>
        public RealCacheSfx2Tests(RealCacheFixture fixture, ITestOutputHelper output)
        {
            _fixture = fixture;
            _output = output;
        }

        /// <summary>Groups the index-14 reference table declares.</summary>
        private int GroupsDeclared => _fixture.DeclaredGroups(RSConstants.SFX2_INDEX);

        /// <summary>Files the index-14 reference table declares across every group.</summary>
        private int FilesDeclared => _fixture.DeclaredFiles(RSConstants.SFX2_INDEX);

        /// <summary>One group of index 14 as the cache stores it.</summary>
        private readonly struct StoredGroup
        {
            /// <summary>The group id, which is the sound-effect id.</summary>
            public int GroupId { get; }

            /// <summary>The file id the reference table declares for it.</summary>
            public int FileId { get; }

            /// <summary>The unpacked file, exactly as stored.</summary>
            public byte[] Bytes { get; }

            /// <summary>Binds a group's bytes to the address they were read from.</summary>
            public StoredGroup(int groupId, int fileId, byte[] bytes)
            {
                GroupId = groupId;
                FileId = fileId;
                Bytes = bytes;
            }
        }

        /// <summary>
        ///     Every group the reference table declares, unpacked.
        /// </summary>
        /// <remarks>
        ///     Goes through the fixture's raw container path rather than
        ///     <see cref="RSCache.ReadFile"/>, which memoises every container it touches - fine for
        ///     one record in an editor pane, wasteful across an index whose payload runs to tens of
        ///     megabytes.
        ///     <para>
        ///     File ids come off the reference table entry. Every group on this index declares
        ///     exactly one file, which is asserted rather than assumed.
        ///     </para>
        /// </remarks>
        /// <returns>The groups, ascending by id.</returns>
        private IEnumerable<StoredGroup> Groups()
        {
            RSReferenceTable table = _fixture.Table(RSConstants.SFX2_INDEX);

            foreach (int groupId in table.GetArchiveEntries().Keys)
            {
                byte[] stored = _fixture.RawContainer(RSConstants.SFX2_INDEX, groupId);
                Assert.True(stored != null, $"index 14 group {groupId} is declared but its index record is empty");

                int[] fileIds = table.GetArchiveEntry(groupId).GetValidFileIds();
                Assert.True(fileIds.Length == 1,
                    $"index 14 group {groupId} declares {fileIds.Length} files; the client reads this " +
                    "index through the single-file accessor (JS5Archive.method2733), so anything else " +
                    "would throw before a decoder ran");

                RSContainer container = _fixture.TryDecodeContainer(RSConstants.SFX2_INDEX, groupId, stored);
                Assert.True(container != null, $"index 14 group {groupId}: container would not decode");

                RSArchive archive = RSArchive.Decode(container.GetStream(), fileIds);
                Assert.True(archive.HasFile(fileIds[0]),
                    $"index 14 group {groupId} file {fileIds[0]}: declared by the reference table but " +
                    "absent from the unpacked group");

                yield return new StoredGroup(groupId, fileIds[0], archive.GetFile(fileIds[0]).ToArray());
            }
        }

        /// <summary>
        ///     Every declared group decodes and re-encodes to the bytes the cache stores for it.
        /// </summary>
        /// <remarks>
        ///     The primary regression detector for this index. The comparison is against the
        ///     decompressed payload rather than the stored container, because most of this index is
        ///     stored uncompressed and the rest is GZip - and no GZip container in either cache
        ///     re-encodes byte-identically, so comparing containers would measure the compressor.
        /// </remarks>
        [RealCacheFact]
        public void EveryGroup_ReEncodesToItsStoredBytes()
        {
            var failures = new List<string>();
            int swept = 0;
            int identical = 0;
            int setupHeaders = 0;
            int samples = 0;
            long bytes = 0;

            foreach (StoredGroup group in Groups())
            {
                swept++;

                Sfx2Entry entry;
                byte[] reencoded;
                try
                {
                    entry = Sfx2Entry.Decode(group.GroupId, new JagStream(group.Bytes));
                    reencoded = entry.Encode().ToArray();
                }
                catch (Exception ex)
                {
                    failures.Add($"group {group.GroupId}: re-encode threw {ex.GetType().Name}: {ex.Message}");
                    continue;
                }

                if (entry is Sfx2SetupHeader)
                    setupHeaders++;
                else
                    samples++;

                if (reencoded.AsSpan().SequenceEqual(group.Bytes))
                {
                    identical++;
                    bytes += group.Bytes.Length;
                    continue;
                }

                failures.Add($"group {group.GroupId}: re-encoded {reencoded.Length} bytes from a stored " +
                             $"{group.Bytes.Length}, first difference at " +
                             $"{FirstDifference(group.Bytes, reencoded)}");
            }

            _output.WriteLine($"{identical} of {swept} index-14 groups re-encoded byte-identically, " +
                              $"{bytes} bytes of payload");
            _output.WriteLine($"{setupHeaders} setup header, {samples} samples");
            Report(failures, "index-14 groups did not re-encode to their stored bytes");

            Assert.True(GroupsDeclared > 0, "index 14 declares no groups, so nothing was checked");
            Assert.Equal(GroupsDeclared, swept);
            Assert.Equal(FilesDeclared, swept);
            Assert.Equal(swept, identical);

            //One setup header exactly, which is what makes the remaining groups samples.
            Assert.Equal(1, setupHeaders);
            Assert.Equal(swept - 1, samples);
        }

        /// <summary>
        ///     Every sample record is consumed to its last byte, stopping where the packet list ends
        ///     rather than where the buffer does.
        /// </summary>
        /// <remarks>
        ///     The sharp instrument for this codec. The header is five fixed int32s and the packet
        ///     list is self-describing, so a wrong header width or a mis-read length prefix
        ///     desynchronises the walk and lands somewhere other than the end. Decoding a padded copy
        ///     is what makes an over-read visible: a decoder that ran off the end of the genuine
        ///     bytes would otherwise report a position equal to the length and look exact.
        /// </remarks>
        [RealCacheFact]
        public void EverySample_ConsumesItsPayloadExactly()
        {
            const int padding = 32;
            const byte sentinel = 0xAA;

            var failures = new List<string>();
            int exact = 0;
            long packets = 0;

            foreach (StoredGroup group in Groups())
            {
                if (group.GroupId == Sfx2SetupHeader.SetupGroupId)
                    continue;

                byte[] padded = new byte[group.Bytes.Length + padding];
                Array.Copy(group.Bytes, padded, group.Bytes.Length);
                for (int i = group.Bytes.Length; i < padded.Length; i++)
                    padded[i] = sentinel;

                var stream = new JagStream(padded);
                try
                {
                    Sfx2Sample sample = new Sfx2Sample { Id = group.GroupId }.Decode(stream);
                    packets += sample.PacketCount;
                }
                catch (Exception ex)
                {
                    failures.Add($"group {group.GroupId}: decode ran past its {group.Bytes.Length} bytes - " +
                                 $"{ex.GetType().Name} at {stream.Position}: {ex.Message}");
                    continue;
                }

                if (stream.Position != group.Bytes.Length)
                {
                    string how = stream.Position > group.Bytes.Length ? "overran" : "stopped short of";
                    failures.Add($"group {group.GroupId}: {how} its {group.Bytes.Length} bytes, ending at " +
                                 $"{stream.Position} ({stream.Position - group.Bytes.Length:+#;-#;0})");
                    continue;
                }

                exact++;
            }

            _output.WriteLine($"{exact} sample records consumed their payload exactly, {packets} Vorbis packets");
            Report(failures, "index-14 sample records did not consume their payload exactly");

            Assert.True(GroupsDeclared > 1, "index 14 declares no sample groups, so nothing was checked");
            Assert.Equal(GroupsDeclared - 1, exact);
        }

        /// <summary>
        ///     Group 0 is the Vorbis setup header and no other group is, which is the claim the
        ///     whole codec is split on.
        /// </summary>
        /// <remarks>
        ///     Checked rather than assumed, because dispatching on the group id is a rule taken from
        ///     the client (<c>Node_Sub13.java:32</c> against <c>:76</c>) and the cache is two builds
        ///     later. Two independent statements, either of which would catch the rule being wrong:
        ///     group 0 carries the Vorbis codebook sync pattern, which only assembles under the
        ///     client's bit order and appears nowhere in a sample's header; and group 0's first
        ///     int32 is negative, so it cannot be the sample rate of anything, while every other
        ///     group's is a positive rate.
        /// </remarks>
        [RealCacheFact]
        public void Group0_IsTheSetupHeaderAndNoOtherGroupIs()
        {
            var failures = new List<string>();
            var rates = new SortedDictionary<int, int>();
            Sfx2SetupHeader setup = null;

            foreach (StoredGroup group in Groups())
            {
                if (group.GroupId == Sfx2SetupHeader.SetupGroupId)
                {
                    setup = new Sfx2SetupHeader { Id = group.GroupId }.Decode(new JagStream(group.Bytes));
                    continue;
                }

                var sample = new Sfx2Sample { Id = group.GroupId }.Decode(new JagStream(group.Bytes));
                rates.TryGetValue(sample.SampleRate, out int seen);
                rates[sample.SampleRate] = seen + 1;

                if (sample.SampleRate <= 0)
                    failures.Add($"group {group.GroupId} decodes to a sample rate of {sample.SampleRate}");
            }

            Assert.True(setup != null, "index 14 declares no group 0, so the setup header was not found");
            _output.WriteLine($"group 0: {setup.RawBytes.Length} bytes, blocksize {setup.Blocksize0}/" +
                              $"{setup.Blocksize1}, {setup.CodebookCount} codebooks, first codebook sync " +
                              $"0x{setup.FirstCodebookSync:X}");
            _output.WriteLine("sample rates: " + string.Join(", ", Describe(rates)));
            Report(failures, "index-14 groups do not decode as samples");

            Assert.True(setup.HasCodebookSyncPattern,
                $"group 0's first codebook opens with 0x{setup.FirstCodebookSync:X} rather than the " +
                $"Vorbis sync pattern 0x{Sfx2SetupHeader.VorbisCodebookSyncPattern:X}, so either it is " +
                "not a setup header or the bit reader is wrong");

            //The other half of the split: read as a sample record, group 0's header is not one.
            int asSampleRate = BinaryPrimitives.ReadInt32BigEndian(setup.RawBytes);
            Assert.True(asSampleRate < 0,
                $"group 0's first int32 is {asSampleRate}, which could pass for a sample rate - the " +
                "two shapes are then no longer distinguishable by content");

            Assert.True(rates.Count > 1, "every sample shares one rate, so the field was not exercised");
        }

        /// <summary>
        ///     A looping record stores the bitwise complement of its loop end, not its negation.
        /// </summary>
        /// <remarks>
        ///     Asserted against the stored int32 read straight out of the payload rather than against
        ///     this project's encoder, so it is a statement about the cache and the client's
        ///     <c>anInt3900 ^ 0xffffffff</c> (Node_Sub13.java:502) rather than about our own
        ///     round trip. The two rules differ by one and only a handful of groups loop, so a
        ///     sampled read of the index could miss it entirely.
        /// </remarks>
        [RealCacheFact]
        public void ALoopingRecordStoresTheComplementedLoopEnd()
        {
            var failures = new List<string>();
            int looping = 0;
            int loopPointsWithoutLooping = 0;

            foreach (StoredGroup group in Groups())
            {
                if (group.GroupId == Sfx2SetupHeader.SetupGroupId)
                    continue;

                var sample = new Sfx2Sample { Id = group.GroupId }.Decode(new JagStream(group.Bytes));
                int stored = BinaryPrimitives.ReadInt32BigEndian(group.Bytes.AsSpan(12));

                if (stored < 0)
                {
                    looping++;
                    if (!sample.IsLooping || sample.LoopEnd != ~stored)
                        failures.Add($"group {group.GroupId}: stored loop end {stored} decoded to " +
                                     $"looping={sample.IsLooping}, end={sample.LoopEnd}; the client's " +
                                     $"complement gives {~stored} and its negation {-stored}");
                    continue;
                }

                if (sample.IsLooping || sample.LoopEnd != stored)
                    failures.Add($"group {group.GroupId}: stored loop end {stored} decoded to " +
                                 $"looping={sample.IsLooping}, end={sample.LoopEnd}");

                if (sample.LoopStart != 0 || sample.LoopEnd != 0)
                    loopPointsWithoutLooping++;
            }

            _output.WriteLine($"{looping} records loop; {loopPointsWithoutLooping} carry non-zero loop " +
                              "points without looping");
            Report(failures, "index-14 records disagree with the client on the loop flag");

            Assert.True(looping > 0, "no record loops, so the sign-bit rule was not exercised");

            //Clearing the loop points on a record that does not loop would rewrite these.
            Assert.True(loopPointsWithoutLooping > 0,
                "no non-looping record carries loop points, so nothing here would notice them being zeroed");
        }

        /// <summary>
        ///     No packet in the cache is long enough to need a continuation byte, so the sweeps
        ///     cannot defend the base-255 length encoder past its first byte.
        /// </summary>
        /// <remarks>
        ///     This test exists to state a gap rather than to close one. The packet length is written
        ///     <c>while (n &gt;= 255) { put 255; n -= 255 } put n</c>, and every plausible wrong
        ///     variant - stopping at 254, continuing only above 255, writing two bytes big-endian -
        ///     agrees with the correct one on every length below 255. Since the index holds no packet
        ///     at or above that, a byte-identity sweep over all of it would pass on all of them, and
        ///     the first record anyone imported longer audio into would be silently corrupt. The rule
        ///     is pinned synthetically in <c>Sfx2CodecTests</c> instead.
        ///     <para>
        ///     If this ever fails, the data has grown a case the sweep now covers - which is good
        ///     news, and the note above needs revisiting rather than the assertion relaxing.
        ///     </para>
        /// </remarks>
        [RealCacheFact]
        public void ThePacketLengthContinuationByteIsUnreachableInThisCache()
        {
            int longest = -1;
            int longestGroup = -1;
            long packets = 0;

            foreach (StoredGroup group in Groups())
            {
                if (group.GroupId == Sfx2SetupHeader.SetupGroupId)
                    continue;

                var sample = new Sfx2Sample { Id = group.GroupId }.Decode(new JagStream(group.Bytes));
                packets += sample.PacketCount;

                foreach (int length in sample.PacketLengths)
                {
                    if (length <= longest)
                        continue;
                    longest = length;
                    longestGroup = group.GroupId;
                }
            }

            _output.WriteLine($"longest of {packets} packets is {longest} bytes, in group {longestGroup}; " +
                              $"the length prefix continues at {Sfx2Sample.PacketLengthRadix}");

            Assert.True(packets > 0, "no packet was read, so nothing was measured");
            Assert.True(longest < Sfx2Sample.PacketLengthRadix,
                $"group {longestGroup} holds a {longest}-byte packet, so the multi-byte length prefix " +
                "is now exercised by the data and this test's premise no longer holds");
        }

        /// <summary>Renders a sample-rate histogram, commonest first.</summary>
        /// <param name="rates">How many records carry each rate.</param>
        /// <returns>The histogram entries.</returns>
        private static IEnumerable<string> Describe(SortedDictionary<int, int> rates)
        {
            foreach (KeyValuePair<int, int> rate in rates)
                yield return $"{rate.Key}Hz={rate.Value}";
        }

        private static int FirstDifference(byte[] expected, byte[] actual)
        {
            int shared = Math.Min(expected.Length, actual.Length);
            for (int i = 0; i < shared; i++)
                if (expected[i] != actual[i])
                    return i;
            return shared;
        }

        private static void Report(List<string> failures, string summary)
        {
            if (failures.Count == 0)
                return;

            const int maxReported = 20;
            string detail = string.Join(Environment.NewLine, failures.GetRange(0, Math.Min(maxReported, failures.Count)));
            if (failures.Count > maxReported)
                detail += $"{Environment.NewLine}... and {failures.Count - maxReported} more";

            Assert.Fail($"{failures.Count} {summary}:{Environment.NewLine}{detail}");
        }
    }
}
