using FlashEditor.cache;
using FlashEditor.Tests.Cache.RealCache;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Xunit;
using Xunit.Abstractions;

namespace FlashEditor.Tests.Cache
{
    /// <summary>
    ///     Runs every item definition in the real revision-639 cache through the production
    ///     opcode decoder and encoder.
    /// </summary>
    /// <remarks>
    ///     The item decoder was derived from a build-637 client while this cache is build 639 -
    ///     see AGENTS.md - and that gap has never been checked against real bytes. An item
    ///     definition is the one shape where it can be: it is a self-delimiting opcode stream,
    ///     read opcode byte, read its payload, repeat until opcode 0. Nothing in the file says
    ///     how long any payload is, so a decoder that mis-sizes one desynchronises for the rest
    ///     of the record. It then either throws, meets a byte that is not a known opcode, or
    ///     stops on a stray zero and leaves a tail unread.
    ///     <para>
    ///     "Consumed the buffer exactly" is therefore the assertion that matters here: across
    ///     twenty thousand records a wrong payload size cannot stay hidden behind it.
    ///     </para>
    /// </remarks>
    public class RealCacheItemDefinitionTests : IClassFixture<RealCacheFixture>
    {
        private readonly RealCacheFixture _cache;
        private readonly ITestOutputHelper _output;

        /// <summary>Failures listed before the report is truncated.</summary>
        private const int MaxReportedFailures = 10;

        /// <summary>
        ///     How many failures get the quadratic opcode-boundary trace before the rest are
        ///     merely counted, so a wholesale format mismatch does not hang the run.
        /// </summary>
        private const int MaxDiagnosedFailures = 20;

        /// <summary>Binds the shared open cache and the per-test output sink.</summary>
        public RealCacheItemDefinitionTests(RealCacheFixture cache, ITestOutputHelper output)
        {
            _cache = cache;
            _output = output;
        }

        // ===================================================================
        //  Decode
        // ===================================================================

        /// <summary>
        ///     Every item definition must decode without throwing and finish with the read
        ///     position on the end of its buffer.
        /// </summary>
        /// <remarks>
        ///     Landing short means an opcode payload was sized wrongly earlier in the record and
        ///     the decoder stopped on a data byte that happened to be zero, so the fields after
        ///     that point are garbage even though nothing threw. That is the failure this whole
        ///     class exists to catch, and it is reported with the opcode trace that led to it
        ///     rather than as a bare count.
        /// </remarks>
        [RealCacheFact]
        public void AllItemDefinitions_DecodeAndConsumeTheirBufferExactly()
        {
            var failures = new List<string>();
            var opcodeCounts = new SortedDictionary<int, int>();
            int diagnosed = 0;
            int decoded = 0;
            int exact = 0;
            long bytes = 0;

            foreach (ItemRecord record in ItemRecords(failures))
            {
                var definition = new ItemDefinition();
                var stream = new JagStream(record.Payload);

                try
                {
                    definition.Decode(stream);
                    decoded++;
                }
                catch (Exception ex)
                {
                    failures.Add($"item {record.ItemId}: decode threw {ex.GetType().Name}: {ex.Message}" +
                                 Diagnose(record, ref diagnosed));
                    continue;
                }

                bytes += record.Payload.Length;
                for (int opcode = 0; opcode < definition.decoded.Length; opcode++)
                {
                    if (!definition.decoded[opcode])
                        continue;
                    opcodeCounts.TryGetValue(opcode, out int seen);
                    opcodeCounts[opcode] = seen + 1;
                }

                if (stream.Position != stream.Length)
                {
                    failures.Add($"item {record.ItemId}: stopped at {stream.Position} of {stream.Length} " +
                                 $"({stream.Length - stream.Position} bytes unread)" +
                                 Diagnose(record, ref diagnosed));
                    continue;
                }

                //Landing on the end is not quite enough on its own. Several opcodes read their
                //element count with JagStream.ReadByte, which answers -1 at the end of the
                //stream instead of throwing, so a record truncated inside one of those counts
                //would also finish exactly on the end. Requiring the last byte to be the
                //terminator is what rules that out.
                if (record.Payload.Length == 0 || record.Payload[record.Payload.Length - 1] != 0)
                {
                    failures.Add($"item {record.ItemId}: consumed {stream.Length} bytes but the record " +
                                 "does not end with the opcode 0 terminator" +
                                 Diagnose(record, ref diagnosed));
                    continue;
                }

                exact++;
            }

            _output.WriteLine($"{decoded} item definitions decoded, {bytes} bytes of payload");
            _output.WriteLine($"{exact} of {decoded} consumed their buffer exactly");
            _output.WriteLine("opcodes seen: " + string.Join(", ", opcodeCounts.Select(o => $"{o.Key}x{o.Value}")));
            ReportSweepScope();

            Assert.True(decoded > 0, "no item definition was decoded, so nothing was checked");
            AssertNoFailures(failures, "item definitions did not decode cleanly to the end of their buffer");
        }

        // ===================================================================
        //  Encode
        // ===================================================================

        /// <summary>
        ///     Every item definition must re-encode to the bytes it was decoded from.
        /// </summary>
        /// <remarks>
        ///     This is the strongest check available: it fails not only on a payload the decoder
        ///     sized wrongly but on any field it read and then failed to write back, and on any
        ///     opcode the encoder emits that the cache does not carry. The editor re-encodes a
        ///     definition on every save, so a difference here is a difference that lands on the
        ///     user's disk.
        /// </remarks>
        [RealCacheFact]
        public void AllItemDefinitions_ReEncodeToTheCapturedBytes()
        {
            var failures = new List<string>();
            var differingOpcodes = new SortedDictionary<int, int>();
            int diagnosed = 0;
            int compared = 0;
            int identical = 0;

            foreach (ItemRecord record in ItemRecords(failures))
            {
                var definition = new ItemDefinition();
                byte[] reencoded;

                try
                {
                    definition.Decode(new JagStream(record.Payload));
                    reencoded = definition.Encode().ToArray();
                }
                catch (Exception ex)
                {
                    failures.Add($"item {record.ItemId}: re-encode threw {ex.GetType().Name}: {ex.Message}");
                    continue;
                }

                compared++;
                if (record.Payload.AsSpan().SequenceEqual(reencoded))
                {
                    identical++;
                    continue;
                }

                int offset = FirstDifference(record.Payload, reencoded);
                int opcode = OpcodeCovering(record.Payload, offset);
                differingOpcodes.TryGetValue(opcode, out int seen);
                differingOpcodes[opcode] = seen + 1;

                failures.Add($"item {record.ItemId}: re-encoded {reencoded.Length} bytes from a captured " +
                             $"{record.Payload.Length}, first difference at {offset} inside opcode {opcode}" +
                             CompareTraces(record.Payload, reencoded, ref diagnosed));
            }

            _output.WriteLine($"{identical} of {compared} item definitions re-encoded byte-identically");
            if (differingOpcodes.Count > 0)
            {
                _output.WriteLine("first difference fell inside these opcodes: " +
                                  string.Join(", ", differingOpcodes.Select(o => $"{o.Key}x{o.Value}")));
            }
            ReportSweepScope();

            Assert.True(compared > 0, "no item definition was re-encoded, so nothing was checked");
            AssertNoFailures(failures, "item definitions did not re-encode to the captured bytes");
        }

        /// <summary>
        ///     The encoder's output must decode back to something that encodes identically
        ///     again.
        /// </summary>
        /// <remarks>
        ///     Byte-identity against the cache also fails when the cache's own layout differs
        ///     from the encoder's - a different opcode order, or an opcode the packer wrote at
        ///     its default value. This weaker check isolates the part that is purely this
        ///     project's fault: whatever the encoder writes, the decoder must read back to the
        ///     same state. A payload size the two disagree on shows up here as a second encode
        ///     that no longer matches the first, with no dependence on how Jagex packed it.
        /// </remarks>
        [RealCacheFact]
        public void AllItemDefinitions_EncodeIsAFixedPointOfDecode()
        {
            var failures = new List<string>();
            int compared = 0;
            int stable = 0;

            foreach (ItemRecord record in ItemRecords(failures))
            {
                byte[] first;
                byte[] second;

                try
                {
                    var once = new ItemDefinition();
                    once.Decode(new JagStream(record.Payload));
                    first = once.Encode().ToArray();

                    var twice = new ItemDefinition();
                    twice.Decode(new JagStream(first));
                    second = twice.Encode().ToArray();
                }
                catch (Exception ex)
                {
                    failures.Add($"item {record.ItemId}: re-decoding the encoder's own output threw " +
                                 $"{ex.GetType().Name}: {ex.Message}");
                    continue;
                }

                compared++;
                if (first.AsSpan().SequenceEqual(second))
                {
                    stable++;
                    continue;
                }

                int offset = FirstDifference(first, second);
                failures.Add($"item {record.ItemId}: encoder output re-encoded to {second.Length} bytes from " +
                             $"{first.Length}, first difference at {offset} inside opcode " +
                             $"{OpcodeCovering(first, offset)}");
            }

            _output.WriteLine($"{stable} of {compared} item definitions survived an encode-decode-encode cycle");
            ReportSweepScope();

            Assert.True(compared > 0, "no item definition was round-tripped, so nothing was checked");
            AssertNoFailures(failures, "item definitions did not survive an encode-decode-encode cycle");
        }

        // ===================================================================
        //  Regressions, pinned without needing a cache
        // ===================================================================

        /// <summary>
        ///     A definition that has never been touched must encode to nothing but the
        ///     terminator.
        /// </summary>
        /// <remarks>
        ///     The encoder used to write opcodes 1, 4, 5, 6 and 12 unconditionally, along with
        ///     the "take" and "drop" menu entries the decoder seeds. Against the real cache that
        ///     added eleven bytes of defaults to every item that had not stored them, so no item
        ///     in the cache re-encoded to the bytes it came from.
        /// </remarks>
        [Fact]
        public void Encode_WritesNothingForFieldsLeftAtTheirDefaults()
        {
            byte[] encoded = new ItemDefinition().Encode().ToArray();

            Assert.Equal(new byte[] { 0 }, encoded);
        }

        /// <summary>
        ///     A record that stores its opcodes out of order, repeats one, writes fields at
        ///     their default value and repeats a parameter key must come back byte for byte.
        /// </summary>
        /// <remarks>
        ///     Every one of those is something the revision-639 packer actually does and the
        ///     decoded fields cannot express: opcode order is free, a superseded opcode reaches
        ///     no field, an explicitly stored default is indistinguishable from an absent one,
        ///     and a repeated parameter key collapses in the dictionary. They are pinned
        ///     together because they only ever showed up together, in the same records.
        /// </remarks>
        [Fact]
        public void Encode_ReproducesAPackerLayoutTheFieldsCannotExpress()
        {
            var source = new JagStream();

            source.WriteByte(1); source.WriteShort(123);                  // superseded model id
            source.WriteByte(7); source.WriteShort(-5);                   // opcodes out of order
            source.WriteByte(4); source.WriteShort(2000);                 // stored at its default
            source.WriteByte(12); source.WriteInteger(1);                 // stored at its default
            source.WriteByte(1); source.WriteShort(456);                  // repeated opcode wins
            source.WriteByte(32); source.WriteJagexString("take");        // seeded ground option
            source.WriteByte(39); source.WriteJagexString("drop");        // seeded inventory option

            source.WriteByte(249);
            source.WriteByte(3);
            source.WriteByte(0); source.WriteMedium(7); source.WriteInteger(9);
            source.WriteByte(0); source.WriteMedium(3); source.WriteInteger(8);
            source.WriteByte(0); source.WriteMedium(7); source.WriteInteger(5);   // repeated key

            source.WriteByte(2); source.WriteJagexString("Bucket");       // name last, as packed
            source.WriteByte(0);

            byte[] captured = source.Flip().ToArray();

            var definition = new ItemDefinition();
            var stream = new JagStream(captured);
            definition.Decode(stream);

            Assert.Equal(captured.Length, stream.Position);
            Assert.Equal(456, definition.inventoryModelId);
            Assert.Equal(2000, definition.modelZoom);
            Assert.Equal("Bucket", definition.name);
            Assert.Equal(9, definition.itemParams[7]);
            Assert.Equal(captured, definition.Encode().ToArray());
        }

        /// <summary>
        ///     A field the editor sets on a definition whose record never carried that opcode
        ///     must still be written.
        /// </summary>
        /// <remarks>
        ///     Replaying the decoded opcode order is what makes an untouched definition
        ///     round-trip, but on its own it would freeze the record: an edit to a field the
        ///     packer left out would go nowhere. This is the other half of that.
        /// </remarks>
        [Fact]
        public void Encode_AppendsAnOpcodeTheRecordNeverCarried()
        {
            var source = new JagStream();
            source.WriteByte(2);
            source.WriteJagexString("Bucket");
            source.WriteByte(0);

            var definition = new ItemDefinition();
            definition.Decode(new JagStream(source.Flip().ToArray()));

            definition.teamId = 3;
            byte[] encoded = definition.Encode().ToArray();

            var reread = new ItemDefinition();
            reread.Decode(new JagStream(encoded));

            Assert.Equal(3, reread.teamId);
            Assert.Equal("Bucket", reread.name);
            Assert.Equal(new[] { 2, 115 }, reread.opcodeOrder);
        }

        // ===================================================================
        //  Bare flags
        // ===================================================================

        /// <summary>
        ///     Every presence-only opcode on an item definition, with the accessor that reads and
        ///     writes it phrased as "the record carries this opcode".
        /// </summary>
        /// <remarks>
        ///     Unlike the NPC and object codecs, this one already emitted these three from their
        ///     fields rather than from the recorded opcode list, so clearing one has always
        ///     dropped the last occurrence. The tests below pin that, and pin the one case it did
        ///     not cover - a record that stores the same flag twice.
        /// </remarks>
        private static readonly (int Opcode, string Name,
            Func<ItemDefinition, bool> Carried, Action<ItemDefinition, bool> SetCarried)[] BareFlags =
        {
            (11, "stackable",   d => d.stackable == 1, (d, on) => d.stackable = on ? 1 : 0),
            (16, "membersOnly", d => d.membersOnly,    (d, on) => d.membersOnly = on),
            (65, "unnoted",     d => d.unnoted,        (d, on) => d.unnoted = on),
        };

        /// <summary>
        ///     Turning a bare flag off removes its opcode, so the next encode does not carry it.
        /// </summary>
        /// <remarks>
        ///     membersOnly is bound to an editable grid column. If this regresses, every "Members"
        ///     tick the user clears is written straight back out from the recorded record: the row
        ///     changes, the save reports success and the item stays members-only in the cache.
        /// </remarks>
        [Fact]
        public void ABareFlagTurnedOff_IsRemovedFromTheEncodedStream()
        {
            foreach ((int opcode, string name, var carried, var setCarried) in BareFlags)
            {
                //Opcode 115 gives the record something to keep, so a dropped flag is
                //distinguishable from an encoder that lost the whole record.
                var definition = new ItemDefinition();
                definition.Decode(new JagStream(new byte[] { 115, 3, (byte)opcode, 0 }));
                Assert.True(carried(definition), $"{name}: opcode {opcode} did not decode as carried");

                setCarried(definition, false);

                byte[] encoded = definition.Encode().ToArray();
                Assert.Equal(new byte[] { 115, 3, 0 }, encoded);

                var reread = new ItemDefinition();
                reread.Decode(new JagStream(encoded));
                Assert.False(carried(reread), $"{name}: opcode {opcode} came back after being cleared");
                Assert.Equal(3, reread.teamId);
            }
        }

        /// <summary>
        ///     Turning a bare flag on emits its opcode, even on a record that never carried it.
        /// </summary>
        [Fact]
        public void ABareFlagTurnedOn_IsAppendedToTheEncodedStream()
        {
            foreach ((int opcode, string name, var carried, var setCarried) in BareFlags)
            {
                var definition = new ItemDefinition();
                definition.Decode(new JagStream(new byte[] { 115, 3, 0 }));
                Assert.False(carried(definition), $"{name}: opcode {opcode} was carried by a record without it");

                setCarried(definition, true);

                //115 was recorded so it keeps its place; the new opcode is appended after it.
                byte[] encoded = definition.Encode().ToArray();
                Assert.Equal(new byte[] { 115, 3, (byte)opcode, 0 }, encoded);

                var reread = new ItemDefinition();
                reread.Decode(new JagStream(encoded));
                Assert.True(carried(reread), $"{name}: opcode {opcode} did not survive being set");
            }
        }

        /// <summary>
        ///     A record that never carried a bare flag reports the client-side default for it and
        ///     encodes without it.
        /// </summary>
        [Fact]
        public void ARecordThatNeverCarriedABareFlag_KeepsTheDefaultAndEncodesWithoutIt()
        {
            var definition = new ItemDefinition();
            definition.Decode(new JagStream(new byte[] { 115, 3, 0 }));

            Assert.Equal(0, definition.stackable);
            Assert.False(definition.membersOnly);
            Assert.False(definition.unnoted);
            Assert.Equal(new byte[] { 115, 3, 0 }, definition.Encode().ToArray());
        }

        /// <summary>
        ///     A record carrying every bare flag re-encodes to the bytes it came from when nothing
        ///     is edited.
        /// </summary>
        /// <remarks>
        ///     The regression guard for the tests above: no setter runs for an item the user merely
        ///     opened, so the recorded record has to replay untouched, opcode order included.
        /// </remarks>
        [Fact]
        public void BareFlagsLeftUntouched_ReEncodeToTheirStoredBytes()
        {
            byte[] captured = { 65, 115, 3, 11, 16, 0 };

            var definition = new ItemDefinition();
            var stream = new JagStream(captured);
            definition.Decode(stream);

            Assert.Equal(captured.Length, stream.Position);
            Assert.Equal(1, definition.stackable);
            Assert.True(definition.membersOnly);
            Assert.True(definition.unnoted);
            Assert.Equal(captured, definition.Encode().ToArray());
        }

        /// <summary>
        ///     Clearing a bare flag removes every occurrence of its opcode, not merely the last.
        /// </summary>
        /// <remarks>
        ///     A superseded occurrence is normally replayed from the bytes it was read from, which
        ///     is what keeps the three hundred items that repeat an opcode byte-exact. A bare flag
        ///     has no bytes beyond the opcode itself, so replaying it would leave an earlier copy
        ///     behind and the client would still read the flag as set - the edit would look applied
        ///     and do nothing. Repeated occurrences of a flag left switched on are still both
        ///     written, which is the half this must not break.
        /// </remarks>
        [Fact]
        public void ClearingARepeatedBareFlag_RemovesEveryOccurrence()
        {
            byte[] captured = { 16, 115, 3, 16, 0 };

            var stored = new ItemDefinition();
            stored.Decode(new JagStream(captured));
            Assert.Equal(captured, stored.Encode().ToArray());

            var edited = new ItemDefinition();
            edited.Decode(new JagStream(captured));
            edited.membersOnly = false;

            byte[] encoded = edited.Encode().ToArray();
            Assert.Equal(new byte[] { 115, 3, 0 }, encoded);

            var reread = new ItemDefinition();
            reread.Decode(new JagStream(encoded));
            Assert.False(reread.membersOnly);
        }

        // ===================================================================
        //  Reading the definitions out of the cache
        // ===================================================================

        /// <summary>One item definition file, with the item id it is addressed by.</summary>
        private readonly struct ItemRecord
        {
            public ItemRecord(int itemId, byte[] payload)
            {
                ItemId = itemId;
                Payload = payload;
            }

            /// <summary>The item id, which is how the archive and file ids are addressed.</summary>
            public int ItemId { get; }

            /// <summary>The raw definition bytes as they sit in the archive.</summary>
            public byte[] Payload { get; }
        }

        /// <summary>
        ///     Yields every item definition file in the config index.
        /// </summary>
        /// <remarks>
        ///     Goes through the fixture rather than <see cref="RSCache.GetItemDefinition"/>
        ///     because that path memoises every container it touches, and holding the whole item
        ///     index in memory at once is a needless cost for a sweep that reads each archive
        ///     once. The addressing is the same one it uses: item id is
        ///     <c>archiveId * 256 + fileId</c>.
        /// </remarks>
        /// <param name="failures">Collects archives that could not be read at all.</param>
        /// <returns>The definitions, ascending by item id.</returns>
        private IEnumerable<ItemRecord> ItemRecords(List<string> failures)
        {
            RSReferenceTable table = _cache.Table(RSConstants.ITEM_DEFINITIONS_INDEX);

            foreach (int archiveId in _cache.ArchivesToExamine(table))
            {
                byte[] stored = _cache.RawContainer(RSConstants.ITEM_DEFINITIONS_INDEX, archiveId);
                if (stored == null)
                    continue;

                int[] fileIds = table.GetArchiveEntry(archiveId).GetValidFileIds();
                if (fileIds.Length == 0)
                    continue;

                RSArchive archive;
                try
                {
                    RSContainer container =
                        _cache.TryDecodeContainer(RSConstants.ITEM_DEFINITIONS_INDEX, archiveId, stored);
                    if (container == null)
                    {
                        failures.Add($"item archive {archiveId}: container would not decode");
                        continue;
                    }

                    archive = RSArchive.Decode(container.GetStream(), fileIds);
                }
                catch (Exception ex)
                {
                    failures.Add($"item archive {archiveId}: could not be read - {ex.GetType().Name}: {ex.Message}");
                    continue;
                }

                foreach (int fileId in fileIds)
                {
                    if (!archive.HasFile(fileId))
                        continue;
                    yield return new ItemRecord(archiveId * 256 + fileId, archive.GetFile(fileId).ToArray());
                }
            }
        }

        // ===================================================================
        //  Diagnosis
        // ===================================================================

        /// <summary>
        ///     Describes where a definition's opcode stream went wrong, or an empty string once
        ///     enough failures have been described.
        /// </summary>
        /// <param name="record">The definition that failed.</param>
        /// <param name="diagnosed">Running count of definitions already described.</param>
        /// <returns>A trailing detail string to append to the failure line.</returns>
        private static string Diagnose(ItemRecord record, ref int diagnosed)
        {
            if (diagnosed >= MaxDiagnosedFailures)
                return "";
            diagnosed++;

            IReadOnlyList<int> boundaries = OpcodeBoundaries(record.Payload);
            var trace = new List<string>();
            for (int i = 0; i < boundaries.Count; i++)
            {
                int at = boundaries[i];
                if (at >= record.Payload.Length)
                    break;
                trace.Add($"{record.Payload[at]}@{at}");
            }

            int lastGood = boundaries.Count == 0 ? 0 : boundaries[boundaries.Count - 1];
            string tail = Hex(record.Payload, lastGood, 24);

            return Environment.NewLine +
                   $"    opcodes: {string.Join(" ", trace)}" + Environment.NewLine +
                   $"    stalled at {lastGood} of {record.Payload.Length}, bytes from there: {tail}";
        }

        /// <summary>
        ///     Lays the captured and re-encoded opcode streams side by side, or returns an empty
        ///     string once enough failures have been described.
        /// </summary>
        /// <remarks>
        ///     A length that matches while the bytes do not usually means the two streams carry
        ///     the same opcodes in a different order, or one opcode swapped for another of the
        ///     same width. Neither is visible from a byte offset alone.
        /// </remarks>
        /// <param name="captured">The bytes as the cache stores them.</param>
        /// <param name="reencoded">The bytes the encoder produced.</param>
        /// <param name="diagnosed">Running count of definitions already described.</param>
        /// <returns>A trailing detail string to append to the failure line.</returns>
        private static string CompareTraces(byte[] captured, byte[] reencoded, ref int diagnosed)
        {
            if (diagnosed >= MaxDiagnosedFailures)
                return "";
            diagnosed++;

            return Environment.NewLine +
                   $"    cache: {OpcodeTrace(captured)}" + Environment.NewLine +
                   $"    ours : {OpcodeTrace(reencoded)}";
        }

        /// <summary>Renders a definition's opcodes as <c>opcode@offset</c> pairs.</summary>
        /// <param name="payload">The definition bytes.</param>
        /// <returns>The opcode sequence, in stream order.</returns>
        private static string OpcodeTrace(byte[] payload)
        {
            var trace = new List<string>();
            foreach (int boundary in OpcodeBoundaries(payload))
            {
                if (boundary >= payload.Length)
                    break;
                trace.Add($"{payload[boundary]}@{boundary}");
            }
            return string.Join(" ", trace);
        }

        /// <summary>
        ///     Finds every position in a definition that the decoder reaches as an opcode
        ///     boundary.
        /// </summary>
        /// <remarks>
        ///     Truncating the buffer cannot change the path the decoder takes through it, only
        ///     cut that path short, so decoding the first <c>p</c> bytes finishes on exactly
        ///     <c>p</c> when <c>p</c> is a boundary the full decode also passes through: any
        ///     shorter payload throws on the read that runs off the end, and a real terminator
        ///     stops the decode before <c>p</c>. That builds an opcode trace out of nothing but
        ///     the production decoder, so it cannot disagree with it the way a second
        ///     hand-written parser would.
        ///     <para>
        ///     One position is reported that is not a boundary. Opcodes 40, 41, 42, 132 and 249
        ///     read their element count with <see cref="JagStream.ReadByte"/>, which answers -1
        ///     at the end of the stream rather than throwing, and for 249 a count of -1 simply
        ///     yields an empty block. Cutting the buffer immediately after a 249 opcode byte
        ///     therefore also lands on <c>p</c>. It shows up in a trace as a spurious entry right
        ///     after opcode 249, which is the parameter count read as though it were an opcode.
        ///     </para>
        ///     <para>
        ///     It costs a decode per byte, which is why only the first few failures get one.
        ///     </para>
        /// </remarks>
        /// <param name="payload">The definition bytes.</param>
        /// <returns>The reachable opcode boundaries, ascending.</returns>
        private static IReadOnlyList<int> OpcodeBoundaries(byte[] payload)
        {
            var boundaries = new List<int>();

            for (int prefix = 0; prefix <= payload.Length; prefix++)
            {
                var stream = new JagStream(payload.AsSpan(0, prefix).ToArray());
                try
                {
                    new ItemDefinition().Decode(stream);
                }
                catch (Exception)
                {
                    //Ran off the end mid-payload, or met a byte that is not a known opcode.
                    //Either way this prefix does not end on a boundary.
                    continue;
                }

                if (stream.Position == prefix)
                    boundaries.Add(prefix);
            }

            return boundaries;
        }

        /// <summary>
        ///     Names the opcode whose payload spans <paramref name="offset"/>, so a byte
        ///     difference is reported against the field it belongs to rather than as a raw
        ///     offset.
        /// </summary>
        /// <param name="payload">The definition bytes.</param>
        /// <param name="offset">The byte offset of interest.</param>
        /// <returns>The covering opcode, or <c>-1</c> when the offset is not inside one.</returns>
        private static int OpcodeCovering(byte[] payload, int offset)
        {
            if (offset < 0 || offset >= payload.Length)
                return -1;

            int covering = -1;
            foreach (int boundary in OpcodeBoundaries(payload))
            {
                if (boundary > offset)
                    break;
                if (boundary < payload.Length)
                    covering = payload[boundary];
            }
            return covering;
        }

        private static string Hex(byte[] data, int from, int count)
        {
            if (from >= data.Length)
                return "(none)";

            int take = Math.Min(count, data.Length - from);
            var text = new StringBuilder();
            for (int i = 0; i < take; i++)
                text.Append(data[from + i].ToString("X2")).Append(' ');
            if (take < data.Length - from)
                text.Append("...");
            return text.ToString().TrimEnd();
        }

        private static int FirstDifference(byte[] left, byte[] right)
        {
            int shared = Math.Min(left.Length, right.Length);
            for (int i = 0; i < shared; i++)
                if (left[i] != right[i])
                    return i;
            return shared;
        }

        /// <summary>
        ///     States how much of the index was actually read, so a partial run is never mistaken
        ///     for a sweep.
        /// </summary>
        /// <remarks>
        ///     The config index holds far fewer archives than the per-index sample cap, so the
        ///     sample and the sweep coincide here. That is worth printing rather than assuming.
        /// </remarks>
        private void ReportSweepScope()
        {
            RSReferenceTable table = _cache.Table(RSConstants.ITEM_DEFINITIONS_INDEX);
            int total = table.GetArchiveEntries().Count;
            int examined = _cache.ArchivesToExamine(table).Count;

            _output.WriteLine(examined == total
                ? $"every one of the {total} item archives was read"
                : $"{examined} of {total} item archives read; set " +
                  $"{RealCacheLocator.FullSweepVariable}=1 to read them all");
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
