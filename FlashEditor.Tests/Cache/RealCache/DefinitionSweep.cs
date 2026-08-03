using FlashEditor.cache;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Xunit;
using Xunit.Abstractions;

namespace FlashEditor.Tests.Cache.RealCache
{
    /// <summary>
    ///     One definition file exactly as the cache stores it, with the ids that address it.
    /// </summary>
    /// <remarks>
    ///     Both halves of the address are carried, not only the folded definition id, because a
    ///     failure has to be reportable in the terms the editor uses (the definition id) and in the
    ///     terms the cache uses (group and file). The two disagree the moment an index's page size
    ///     is wrong, which is exactly when the report matters.
    /// </remarks>
    public readonly struct DefinitionRecord
    {
        /// <summary>The definition id, as <see cref="CacheAddressing"/> joins group and file.</summary>
        public int Id { get; }

        /// <summary>The group (archive) the file sits in.</summary>
        public int GroupId { get; }

        /// <summary>The file id within that group.</summary>
        public int FileId { get; }

        /// <summary>The definition's bytes, decompressed and unpacked, as stored.</summary>
        public byte[] Bytes { get; }

        /// <summary>Binds a definition's bytes to the address they were read from.</summary>
        public DefinitionRecord(int id, int groupId, int fileId, byte[] bytes)
        {
            Id = id;
            GroupId = groupId;
            FileId = fileId;
            Bytes = bytes;
        }
    }

    /// <summary>
    ///     Decodes one definition from the bytes the cache holds for it.
    /// </summary>
    /// <remarks>
    ///     The id is handed in rather than assigned afterwards because three of the four codecs
    ///     want it before anything else happens - the object and floor decoders take it through an
    ///     initialiser and the item and NPC decoders through a setter - and a sweep that assigned
    ///     it late would encode from a definition the editor could never produce.
    /// </remarks>
    /// <typeparam name="T">The definition type.</typeparam>
    /// <param name="definitionId">The id the definition is addressed by.</param>
    /// <param name="stream">The bytes to decode, positioned at the start.</param>
    /// <returns>The decoded definition.</returns>
    public delegate T DefinitionFactory<out T>(int definitionId, JagStream stream);

    /// <summary>Helpers shared by every codec description.</summary>
    public static class DefinitionCodec
    {
        /// <summary>
        ///     Reads the opcodes a definition carried out of the <c>decoded</c> hit map the item,
        ///     NPC and object codecs all expose.
        /// </summary>
        /// <remarks>
        ///     The hit map loses repetition and order, so it says which opcodes a record carried
        ///     and not how many times or in what sequence. That is enough for the histogram a
        ///     failure report prints and is not enough to re-encode from, which is why the codecs
        ///     keep the stream separately.
        /// </remarks>
        /// <param name="hitMap">The per-opcode flags.</param>
        /// <returns>The opcodes that were set, ascending.</returns>
        public static IEnumerable<int> FromHitMap(bool[] hitMap)
        {
            for (int opcode = 0; opcode < hitMap.Length; opcode++)
                if (hitMap[opcode])
                    yield return opcode;
        }
    }

    /// <summary>
    ///     Everything <see cref="DefinitionSweep{T}"/> needs to know about one definition codec.
    /// </summary>
    /// <typeparam name="T">The definition type.</typeparam>
    public sealed class DefinitionCodec<T>
    {
        /// <summary>Singular noun for one record, used in every failure line - "item", "NPC".</summary>
        public string Label { get; }

        /// <summary>Decodes a definition from its stored bytes.</summary>
        public DefinitionFactory<T> Decode { get; }

        /// <summary>Encodes a definition back to the bytes that would be stored for it.</summary>
        public Func<T, JagStream> Encode { get; }

        /// <summary>
        ///     The opcodes a decoded definition carried, or <c>null</c> when the codec cannot say.
        /// </summary>
        /// <remarks>
        ///     Only ever used for diagnostics. A histogram of the opcodes present in the failing
        ///     records is what turns "1,400 definitions differ" into "every one of them carries
        ///     opcode 75", so it is worth supplying wherever the codec can.
        /// </remarks>
        public Func<T, IEnumerable<int>> OpcodesOf { get; }

        /// <summary>Describes a codec to the sweep harness.</summary>
        /// <param name="label">Singular noun for one record.</param>
        /// <param name="decode">Decodes a definition from its stored bytes.</param>
        /// <param name="encode">Encodes a definition back to storable bytes.</param>
        /// <param name="opcodesOf">The opcodes a decoded definition carried, for diagnostics.</param>
        public DefinitionCodec(string label, DefinitionFactory<T> decode, Func<T, JagStream> encode,
            Func<T, IEnumerable<int>> opcodesOf = null)
        {
            Label = label ?? throw new ArgumentNullException(nameof(label));
            Decode = decode ?? throw new ArgumentNullException(nameof(decode));
            Encode = encode ?? throw new ArgumentNullException(nameof(encode));
            OpcodesOf = opcodesOf;
        }
    }

    /// <summary>What one sweep over an index measured.</summary>
    public readonly struct DefinitionSweepResult
    {
        /// <summary>Records handed to the codec.</summary>
        public int Records { get; }

        /// <summary>Records that met the property the sweep asserts.</summary>
        public int Passed { get; }

        /// <summary>Groups read, before any per-record skipping.</summary>
        public int Groups { get; }

        /// <summary>
        ///     Records whose bytes came back as the same multiset in a different order.
        /// </summary>
        /// <remarks>
        ///     Only meaningful for the byte-identity sweep, where it is the single most useful
        ///     number in the report: same bytes reordered means the encoder stopped replaying the
        ///     stored opcode order, and different content means a payload is being mis-encoded.
        ///     The two have nothing to do with each other and are fixed in different places.
        /// </remarks>
        public int Reordered { get; }

        /// <summary>Binds the counts one sweep produced.</summary>
        public DefinitionSweepResult(int records, int passed, int groups, int reordered)
        {
            Records = records;
            Passed = passed;
            Groups = groups;
            Reordered = reordered;
        }
    }

    /// <summary>
    ///     The byte-identity sweep every definition index gets: enumerate what the reference table
    ///     declares, decode it, require exact buffer consumption, re-encode it and compare against
    ///     the bytes it came from.
    /// </summary>
    /// <remarks>
    ///     This exists because the item, NPC, object and floor suites had each written the same
    ///     sweep, and each had a different subset of the diagnostics that make a failure
    ///     actionable. Four properties are non-negotiable and are why the harness is a type rather
    ///     than a copied loop:
    ///     <list type="bullet">
    ///     <item>File ids come from the reference table's declared list, never from
    ///     <c>0..count-1</c>. Sparse groups are normal - index 16 alone has 64 short groups - and a
    ///     counted loop asks for files that do not exist while missing the ones that do.</item>
    ///     <item>Decode runs a second time over a padded copy. <see cref="JagStream.ReadByte"/>
    ///     answers -1 at the end of the buffer and every one of these decoders treats that as the
    ///     opcode 0 terminator, so a record that ran off its end leaves the position sitting on the
    ///     length and looks exact. The padding turns that into a visible overshoot.</item>
    ///     <item>Comparison is against the decompressed payload, never the stored container. A
    ///     GZip re-encode is byte-identical for 0 of the 96,183 GZip containers in this cache, so
    ///     comparing containers measures the compressor and says nothing about the codec.</item>
    ///     <item>The assertions carry no <c>or</c>. A sweep that scores "decoded or reported
    ///     missing" passes on a cache where everything failed; the count assertion here is that
    ///     nothing failed and that something was checked.</item>
    ///     </list>
    /// </remarks>
    /// <typeparam name="T">The definition type.</typeparam>
    public sealed class DefinitionSweep<T>
    {
        /// <summary>Failing records listed before the report is truncated.</summary>
        private const int MaxReportedFailures = 20;

        /// <summary>
        ///     How many failures get the opcode-boundary trace before the rest are merely listed.
        /// </summary>
        /// <remarks>
        ///     The trace costs one decode per byte of the record, so a wholesale format mismatch
        ///     would otherwise turn a failing run into a hang.
        /// </remarks>
        private const int MaxDiagnosedFailures = 20;

        /// <summary>Sentinel bytes appended past a record so an over-read is visible.</summary>
        private const int SentinelPadding = 32;

        /// <summary>A non-zero pad byte, so it can never be mistaken for a terminator.</summary>
        private const byte SentinelByte = 0xAA;

        private readonly RealCacheFixture _cache;
        private readonly ITestOutputHelper _output;
        private readonly int _indexId;
        private readonly DefinitionCodec<T> _codec;

        private int? _groupId;
        private bool _skipEmptyRecords;

        /// <summary>Binds a codec to an index of the open cache.</summary>
        /// <param name="cache">The shared open cache.</param>
        /// <param name="output">Where the coverage and histogram lines go.</param>
        /// <param name="indexId">The index the definitions live in.</param>
        /// <param name="codec">How to decode and encode one definition.</param>
        public DefinitionSweep(RealCacheFixture cache, ITestOutputHelper output, int indexId,
            DefinitionCodec<T> codec)
        {
            _cache = cache ?? throw new ArgumentNullException(nameof(cache));
            _output = output ?? throw new ArgumentNullException(nameof(output));
            _indexId = indexId;
            _codec = codec ?? throw new ArgumentNullException(nameof(codec));
        }

        /// <summary>
        ///     Restricts the sweep to one group of a shared index, the definition id then being the
        ///     file id within it.
        /// </summary>
        /// <remarks>
        ///     Index 2 holds thirty-five unrelated config families in one index, so its addressing
        ///     is per group rather than per index and <see cref="CacheAddressing.For"/> has no row
        ///     for it. A single group is never sampled away, so this sweep always covers the whole
        ///     family whatever the full-sweep switch says.
        /// </remarks>
        /// <param name="groupId">The group holding the family.</param>
        /// <returns>This sweep, for chaining.</returns>
        public DefinitionSweep<T> WithinGroup(int groupId)
        {
            _groupId = groupId;
            return this;
        }

        /// <summary>
        ///     Passes over files the archive holds as zero bytes instead of reporting them.
        /// </summary>
        /// <remarks>
        ///     Only for an index already known to carry them. An empty record is otherwise a
        ///     failure: it cannot end with a terminator, and it re-encodes to the single
        ///     terminator byte the encoder writes for an empty definition, so tolerating it
        ///     silently would hide a group that unpacked wrongly. The count is printed either way.
        /// </remarks>
        /// <returns>This sweep, for chaining.</returns>
        public DefinitionSweep<T> SkippingEmptyRecords()
        {
            _skipEmptyRecords = true;
            return this;
        }

        /// <summary>
        ///     How this index folds a group and a file into a definition id.
        /// </summary>
        /// <remarks>
        ///     Taken from <see cref="CacheAddressing"/> rather than open-coded, so an index whose
        ///     page size has never been established fails loudly here instead of producing ids that
        ///     look plausible and name the wrong record.
        /// </remarks>
        private CacheAddressing Addressing => _groupId.HasValue
            ? CacheAddressing.SingleGroup(_groupId.Value)
            : CacheAddressing.For(_indexId);

        // ===================================================================
        //  The sweeps
        // ===================================================================

        /// <summary>
        ///     Every record must come out of the decoder without throwing.
        /// </summary>
        /// <remarks>
        ///     The weakest of the sweeps and nearly worthless alone: a decoder that mis-sized a
        ///     payload usually still "succeeds", because a desynchronised stream mostly lands on
        ///     bytes that happen to be handled opcodes. It is here so a hard failure is reported as
        ///     one rather than folded into the exactness count.
        /// </remarks>
        /// <returns>What the sweep measured.</returns>
        public DefinitionSweepResult AssertEveryRecordDecodes()
        {
            var scope = new Scope();
            int decoded = 0;

            foreach (DefinitionRecord record in Records(scope))
            {
                try
                {
                    _codec.Decode(record.Id, new JagStream(record.Bytes));
                    decoded++;
                }
                catch (Exception ex)
                {
                    scope.Failures.Add(Describe(record, $"decode threw {ex.GetType().Name}: {ex.Message}"));
                }
            }

            _output.WriteLine($"{decoded} {_codec.Label} definitions decoded without throwing");
            ReportScope(scope);

            Assert.True(decoded > 0, $"no {_codec.Label} definition was decoded, so nothing was checked");
            AssertNoFailures(scope.Failures, $"{_codec.Label} definitions failed to decode");
            return new DefinitionSweepResult(scope.Records, decoded, scope.Groups, 0);
        }

        /// <summary>
        ///     Every record must be consumed to its last byte, stopping on the stream's own
        ///     terminator rather than on the end of the buffer.
        /// </summary>
        /// <remarks>
        ///     This is the sharp instrument for an opcode stream. Nothing in the file states how
        ///     long a payload is, so a decoder that mis-sizes one by a byte reads the following
        ///     opcode's bytes as payload and every field after that is garbage. Landing on the
        ///     terminator across tens of thousands of records is as close to a proof of the field
        ///     layout as the data can give.
        ///     <para>
        ///     Three separate things are checked, because each catches a shape the others do not:
        ///     the genuine bytes must decode at all; a copy with sentinel padding must consume
        ///     exactly the original length, which is what an over-read cannot fake; and the record
        ///     must end on the opcode 0 terminator, so a record truncated inside an element count
        ///     cannot pass by stopping where the buffer happens to end.
        ///     </para>
        /// </remarks>
        /// <returns>What the sweep measured.</returns>
        public DefinitionSweepResult AssertExactConsumption()
        {
            var scope = new Scope();
            var opcodesOverall = new SortedDictionary<int, int>();
            var opcodesInFailures = new SortedDictionary<int, int>();
            int diagnosed = 0;
            int exact = 0;
            long bytes = 0;

            foreach (DefinitionRecord record in Records(scope))
            {
                //The genuine bytes first: this is the "decodes without throwing" assertion, and
                //keeping it separate means a throw is never reported as a consumption failure.
                T definition;
                try
                {
                    definition = _codec.Decode(record.Id, new JagStream(record.Bytes));
                }
                catch (Exception ex)
                {
                    scope.Failures.Add(Describe(record, $"decode threw {ex.GetType().Name}: {ex.Message}") +
                                       Diagnose(record, ref diagnosed));
                    continue;
                }

                Tally(opcodesOverall, definition);

                //Then the padded copy, which is what actually pins the opcode payload sizes.
                var padded = new JagStream(Pad(record.Bytes));
                try
                {
                    _codec.Decode(record.Id, padded);
                }
                catch (Exception ex)
                {
                    scope.Failures.Add(Describe(record,
                        $"decode ran past its {record.Bytes.Length} bytes - {ex.GetType().Name} at " +
                        $"{padded.Position}: {ex.Message}; opcodes {Opcodes(definition)}; " +
                        $"tail {Tail(record.Bytes)}") + Diagnose(record, ref diagnosed));
                    Tally(opcodesInFailures, definition);
                    continue;
                }

                if (padded.Position != record.Bytes.Length)
                {
                    string how = padded.Position > record.Bytes.Length ? "overran" : "stopped short of";
                    scope.Failures.Add(Describe(record,
                        $"{how} its {record.Bytes.Length} bytes, ending at {padded.Position} " +
                        $"({padded.Position - record.Bytes.Length:+#;-#;0}); opcodes {Opcodes(definition)}; " +
                        $"tail {Tail(record.Bytes)}") + Diagnose(record, ref diagnosed));
                    Tally(opcodesInFailures, definition);
                    continue;
                }

                if (record.Bytes.Length == 0 || record.Bytes[record.Bytes.Length - 1] != 0)
                {
                    scope.Failures.Add(Describe(record,
                        $"consumed all {record.Bytes.Length} bytes but does not end with the opcode 0 " +
                        $"terminator; opcodes {Opcodes(definition)}; tail {Tail(record.Bytes)}") +
                        Diagnose(record, ref diagnosed));
                    Tally(opcodesInFailures, definition);
                    continue;
                }

                exact++;
                bytes += record.Bytes.Length;
            }

            _output.WriteLine($"{exact} {_codec.Label} definitions consumed their buffer exactly, " +
                              $"{bytes} bytes of payload");
            if (opcodesOverall.Count > 0)
                _output.WriteLine("opcodes seen: " + Histogram(opcodesOverall));
            if (opcodesInFailures.Count > 0)
                _output.WriteLine("opcodes seen in failing definitions: " + Histogram(opcodesInFailures));
            ReportScope(scope);

            Assert.True(exact > 0, $"no {_codec.Label} definition was decoded, so nothing was checked");
            AssertNoFailures(scope.Failures, $"{_codec.Label} definitions did not consume their buffer exactly");
            return new DefinitionSweepResult(scope.Records, exact, scope.Groups, 0);
        }

        /// <summary>
        ///     Every record must re-encode to the exact bytes the cache stores for it.
        /// </summary>
        /// <remarks>
        ///     The editor rewrites a definition through its encoder whenever the user saves one, so
        ///     anything the encoder reorders, duplicates or drops changes the archive - and its CRC,
        ///     and therefore the reference-table entry of every archive packed alongside it - for a
        ///     definition nobody edited.
        ///     <para>
        ///     The comparison is against the captured bytes rather than against a second encode of
        ///     the codec's own output, because the latter passes just as happily on an encoder that
        ///     agrees with itself about the wrong answer.
        ///     </para>
        /// </remarks>
        /// <returns>What the sweep measured, including how many records were merely reordered.</returns>
        public DefinitionSweepResult AssertReEncodesToCapturedBytes()
        {
            var scope = new Scope();
            var opcodesInFailures = new SortedDictionary<int, int>();
            var coveringOpcodes = new SortedDictionary<int, int>();
            int diagnosed = 0;
            int compared = 0;
            int identical = 0;
            int reordered = 0;

            foreach (DefinitionRecord record in Records(scope))
            {
                T definition;
                byte[] reencoded;

                try
                {
                    definition = _codec.Decode(record.Id, new JagStream(record.Bytes));
                    reencoded = _codec.Encode(definition).ToArray();
                }
                catch (Exception ex)
                {
                    scope.Failures.Add(Describe(record, $"re-encode threw {ex.GetType().Name}: {ex.Message}"));
                    continue;
                }

                compared++;
                if (reencoded.AsSpan().SequenceEqual(record.Bytes))
                {
                    identical++;
                    continue;
                }

                //Same multiset of bytes means the content survived and only the layout moved,
                //which points at the opcode order rather than at a mis-encoded payload.
                bool sameBytes = SameMultiset(record.Bytes, reencoded);
                if (sameBytes)
                    reordered++;
                else
                    Tally(opcodesInFailures, definition);

                int at = FirstDifference(record.Bytes, reencoded);
                scope.Failures.Add(Describe(record,
                    $"re-encoded {reencoded.Length} bytes from a stored {record.Bytes.Length}, " +
                    $"first difference at {at} ({ByteAt(record.Bytes, at)} became {ByteAt(reencoded, at)}), " +
                    $"{(sameBytes ? "same bytes in a different order" : "different content")}; " +
                    $"opcodes {Opcodes(definition)}") +
                    CompareTraces(record, reencoded, at, coveringOpcodes, ref diagnosed));
            }

            _output.WriteLine($"{identical} of {compared} {_codec.Label} definitions re-encoded to " +
                              "byte-identical output");
            if (reordered > 0)
            {
                _output.WriteLine($"{reordered} more carried the same bytes in a different order, " +
                                  "so the encoder is no longer replaying the stored opcode order");
            }
            if (coveringOpcodes.Count > 0)
            {
                _output.WriteLine($"across the {diagnosed} definitions traced, the first difference fell " +
                                  "inside these opcodes: " + Histogram(coveringOpcodes));
            }
            if (opcodesInFailures.Count > 0)
                _output.WriteLine("opcodes seen in failing definitions: " + Histogram(opcodesInFailures));
            ReportScope(scope);

            Assert.True(compared > 0, $"no {_codec.Label} definition was re-encoded, so nothing was checked");
            Assert.True(identical > 0, $"not one {_codec.Label} definition re-encoded to its stored bytes");
            AssertNoFailures(scope.Failures, $"{_codec.Label} definitions did not re-encode to their stored bytes");
            return new DefinitionSweepResult(scope.Records, identical, scope.Groups, reordered);
        }

        /// <summary>
        ///     Whatever the encoder writes, its own decoder must read back and write out again
        ///     unchanged.
        /// </summary>
        /// <remarks>
        ///     Weaker than byte-identity against the cache, and independent of it. Byte-identity
        ///     also fails when the cache's layout differs from the encoder's - a different opcode
        ///     order, an opcode the packer wrote at its default - whereas this isolates the part
        ///     that is purely this project's fault: a payload the encoder writes in a shape its own
        ///     decoder reads back differently. That is the property a save path depends on once a
        ///     definition has actually been edited, and no comparison with the cache reaches it.
        /// </remarks>
        /// <returns>What the sweep measured.</returns>
        public DefinitionSweepResult AssertEncodeIsAFixedPointOfDecode()
        {
            var scope = new Scope();
            int compared = 0;
            int stable = 0;
            int byteIdentical = 0;

            foreach (DefinitionRecord record in Records(scope))
            {
                byte[] first;
                try
                {
                    first = _codec.Encode(_codec.Decode(record.Id, new JagStream(record.Bytes))).ToArray();
                }
                catch (Exception ex)
                {
                    scope.Failures.Add(Describe(record,
                        $"decoding then encoding it threw {ex.GetType().Name}: {ex.Message}"));
                    continue;
                }

                if (first.AsSpan().SequenceEqual(record.Bytes))
                    byteIdentical++;

                //The encoder's own output must be a well-formed opcode stream, or the editor
                //writes a cache it cannot read back.
                var padded = new JagStream(Pad(first));
                byte[] second;
                try
                {
                    second = _codec.Encode(_codec.Decode(record.Id, padded)).ToArray();
                }
                catch (Exception ex)
                {
                    scope.Failures.Add(Describe(record,
                        $"re-decoding the encoded stream threw {ex.GetType().Name}: {ex.Message}"));
                    continue;
                }

                if (padded.Position != first.Length)
                {
                    scope.Failures.Add(Describe(record,
                        $"the encoded stream is {first.Length} bytes but re-decoding consumed {padded.Position}"));
                    continue;
                }

                compared++;
                if (first.AsSpan().SequenceEqual(second))
                {
                    stable++;
                    continue;
                }

                int at = FirstDifference(first, second);
                scope.Failures.Add(Describe(record,
                    $"encoder output re-encoded to {second.Length} bytes from {first.Length}, " +
                    $"first difference at {at} ({ByteAt(first, at)} became {ByteAt(second, at)})"));
            }

            _output.WriteLine($"{stable} of {compared} {_codec.Label} definitions survived an " +
                              "encode-decode-encode cycle");
            _output.WriteLine($"{byteIdentical} encoded streams were byte-identical to the cache, which " +
                              "the byte-identity sweep asserts on its own");
            ReportScope(scope);

            Assert.True(compared > 0, $"no {_codec.Label} definition was round-tripped, so nothing was checked");
            AssertNoFailures(scope.Failures,
                $"{_codec.Label} definitions did not survive an encode-decode-encode cycle");
            return new DefinitionSweepResult(scope.Records, stable, scope.Groups, 0);
        }

        /// <summary>
        ///     Decodes every record and hands each one to the caller, for measurements the harness
        ///     has no opinion about.
        /// </summary>
        /// <remarks>
        ///     Reading the cache is the expensive and error-prone half; counting how many
        ///     definitions carry a flag is neither. This keeps that split rather than growing a
        ///     sweep method per question.
        /// </remarks>
        /// <param name="visit">Called once per decoded definition.</param>
        /// <returns>What the sweep measured.</returns>
        public DefinitionSweepResult ForEachDecoded(Action<DefinitionRecord, T> visit)
        {
            var scope = new Scope();
            int decoded = 0;

            foreach (DefinitionRecord record in Records(scope))
            {
                T definition;
                try
                {
                    definition = _codec.Decode(record.Id, new JagStream(record.Bytes));
                }
                catch (Exception ex)
                {
                    scope.Failures.Add(Describe(record, $"decode threw {ex.GetType().Name}: {ex.Message}"));
                    continue;
                }

                decoded++;
                visit(record, definition);
            }

            ReportScope(scope);

            Assert.True(decoded > 0, $"no {_codec.Label} definition was examined");
            AssertNoFailures(scope.Failures, $"{_codec.Label} definitions could not be read");
            return new DefinitionSweepResult(scope.Records, decoded, scope.Groups, 0);
        }

        // ===================================================================
        //  Reading the definitions out of the cache
        // ===================================================================

        /// <summary>Counters and failures belonging to one run of one sweep.</summary>
        private sealed class Scope
        {
            /// <summary>Everything that went wrong, in the order it was found.</summary>
            public readonly List<string> Failures = new List<string>();

            /// <summary>Groups read.</summary>
            public int Groups;

            /// <summary>Records handed to the codec.</summary>
            public int Records;

            /// <summary>Declared files the archive held as zero bytes.</summary>
            public int Empty;

            /// <summary>Declared files the unpacked archive does not hold at all.</summary>
            public int Absent;
        }

        /// <summary>
        ///     Yields every definition the sweep covers, straight out of the stored container.
        /// </summary>
        /// <remarks>
        ///     Goes through the fixture rather than <see cref="RSCache.ReadFile"/> because that
        ///     path memoises every container it touches: fine for the one definition an editor pane
        ///     shows, ruinous across an index of 56,199 records.
        ///     <para>
        ///     File ids come from the reference table entry, which is the only statement of which
        ///     files a group holds. Counting them and walking 0..n-1 is wrong on every sparse group
        ///     in the cache, and index 16 alone has 64 groups that are not full.
        ///     </para>
        /// </remarks>
        /// <param name="scope">Collects the counters and the failures of this run.</param>
        /// <returns>The records, ascending by group then file.</returns>
        private IEnumerable<DefinitionRecord> Records(Scope scope)
        {
            CacheAddressing addressing = Addressing;
            RSReferenceTable table = _cache.Table(_indexId);

            foreach (int groupId in GroupsToRead(table))
            {
                scope.Groups++;

                byte[] stored = _cache.RawContainer(_indexId, groupId);
                if (stored == null)
                    continue;

                int[] fileIds = table.GetArchiveEntry(groupId)?.GetValidFileIds();
                if (fileIds == null || fileIds.Length == 0)
                    continue;

                RSArchive archive;
                try
                {
                    RSContainer container = _cache.TryDecodeContainer(_indexId, groupId, stored);
                    if (container == null)
                    {
                        scope.Failures.Add($"{_codec.Label} group {groupId}: container would not decode");
                        continue;
                    }

                    archive = RSArchive.Decode(container.GetStream(), fileIds);
                }
                catch (Exception ex)
                {
                    scope.Failures.Add($"{_codec.Label} group {groupId}: could not be unpacked - " +
                                       $"{ex.GetType().Name}: {ex.Message}");
                    continue;
                }

                foreach (int fileId in fileIds)
                {
                    byte[] bytes;
                    try
                    {
                        if (!archive.HasFile(fileId))
                        {
                            //The reference table declared this file and RSArchive.Decode was
                            //driven by that same list, so an id the unpacked archive does not
                            //hold means the group was split wrongly - which is exactly what
                            //these sweeps exist to catch, not something to pass over.
                            scope.Absent++;
                            scope.Failures.Add($"{_codec.Label} group {groupId} file {fileId}: " +
                                               "declared by the reference table but absent from the " +
                                               "unpacked group");
                            continue;
                        }

                        bytes = archive.GetFile(fileId)?.ToArray();
                    }
                    catch (Exception ex)
                    {
                        scope.Failures.Add($"{_codec.Label} group {groupId} file {fileId}: " +
                                           $"{ex.GetType().Name}: {ex.Message}");
                        continue;
                    }

                    if (bytes == null || bytes.Length == 0)
                    {
                        scope.Empty++;
                        if (_skipEmptyRecords)
                            continue;
                        bytes = bytes ?? Array.Empty<byte>();
                    }

                    int definitionId;
                    try
                    {
                        definitionId = addressing.DefinitionId(groupId, fileId);
                    }
                    catch (ArgumentOutOfRangeException ex)
                    {
                        //The page size recorded for this index cannot hold the file ids the
                        //reference table declares, so every id this sweep reported would name a
                        //different definition than the client would load.
                        scope.Failures.Add($"{_codec.Label} group {groupId} file {fileId}: " +
                                           $"{addressing} cannot address it - {ex.Message}");
                        continue;
                    }

                    scope.Records++;
                    yield return new DefinitionRecord(definitionId, groupId, fileId, bytes);
                }
            }
        }

        /// <summary>The groups this sweep reads, honouring the sampling contract.</summary>
        /// <param name="table">The index's reference table.</param>
        /// <returns>The group ids to read, ascending.</returns>
        private IReadOnlyList<int> GroupsToRead(RSReferenceTable table)
        {
            if (_groupId.HasValue)
                return new int[] { _groupId.Value };

            return _cache.ArchivesToExamine(table);
        }

        // ===================================================================
        //  Reporting
        // ===================================================================

        /// <summary>
        ///     States how much of the index the run actually covered, so a sample is never mistaken
        ///     for a sweep.
        /// </summary>
        /// <remarks>
        ///     "Sampled" and "swept" are very different claims to make about a codec, and several
        ///     of these indexes hold fewer groups than the sample cap - so the default run reads
        ///     every record and the full-sweep switch changes nothing. That is worth printing
        ///     rather than assuming in either direction.
        /// </remarks>
        /// <param name="scope">The counters of the run being reported.</param>
        private void ReportScope(Scope scope)
        {
            if (scope.Absent > 0)
            {
                _output.WriteLine($"{scope.Absent} declared file ids were absent from their unpacked group, " +
                                  "each of them a failure above");
            }
            if (scope.Empty > 0)
            {
                _output.WriteLine($"{scope.Empty} declared files held zero bytes" +
                                  (_skipEmptyRecords ? " and were skipped" : ""));
            }

            if (_groupId.HasValue)
            {
                _output.WriteLine($"index {_indexId} group {_groupId.Value}: {scope.Records} " +
                                  $"{_codec.Label} definitions read, which is the whole family");
                return;
            }

            int total = _cache.Table(_indexId).GetArchiveCount();
            if (scope.Groups >= total)
            {
                _output.WriteLine($"every one of index {_indexId}'s {total} groups was read" +
                                  (_cache.FullSweep
                                      ? ""
                                      : " - it holds fewer than the " +
                                        $"{RealCacheFixture.SampleArchivesPerIndex}-group sample cap"));
                return;
            }

            _output.WriteLine($"sampled {scope.Groups} of index {_indexId}'s {total} groups; set " +
                              $"{RealCacheLocator.FullSweepVariable}=1 to read every one");
        }

        /// <summary>Prefixes a failure line with the address the record was read from.</summary>
        /// <param name="record">The failing record.</param>
        /// <param name="detail">What went wrong.</param>
        /// <returns>The failure line.</returns>
        private string Describe(DefinitionRecord record, string detail)
        {
            return $"{_codec.Label} {record.Id} (group {record.GroupId} file {record.FileId}): {detail}";
        }

        /// <summary>
        ///     Describes where a record's opcode stream went wrong, or nothing once enough failures
        ///     have been described.
        /// </summary>
        /// <param name="record">The failing record.</param>
        /// <param name="diagnosed">Running count of records already described.</param>
        /// <returns>A trailing detail block to append to the failure line.</returns>
        private string Diagnose(DefinitionRecord record, ref int diagnosed)
        {
            if (diagnosed >= MaxDiagnosedFailures)
                return "";
            diagnosed++;

            IReadOnlyList<int> boundaries = OpcodeBoundaries(record);
            int lastGood = boundaries.Count == 0 ? 0 : boundaries[boundaries.Count - 1];

            return Environment.NewLine +
                   $"    opcodes: {Trace(record.Bytes, boundaries)}" + Environment.NewLine +
                   $"    stalled at {lastGood} of {record.Bytes.Length}, bytes from there: " +
                   Hex(record.Bytes, lastGood, 24);
        }

        /// <summary>
        ///     Lays the captured and re-encoded opcode streams side by side, or returns nothing
        ///     once enough failures have been described.
        /// </summary>
        /// <remarks>
        ///     A length that matches while the bytes do not usually means the two streams carry the
        ///     same opcodes in a different order, or one opcode swapped for another of the same
        ///     width. Neither is visible from a byte offset alone.
        /// </remarks>
        /// <param name="record">The record as the cache stores it.</param>
        /// <param name="reencoded">The bytes the encoder produced.</param>
        /// <param name="at">Offset of the first byte the two disagree on.</param>
        /// <param name="coveringOpcodes">Histogram of the opcode each difference fell inside.</param>
        /// <param name="diagnosed">Running count of records already described.</param>
        /// <returns>A trailing detail block to append to the failure line.</returns>
        private string CompareTraces(DefinitionRecord record, byte[] reencoded, int at,
            SortedDictionary<int, int> coveringOpcodes, ref int diagnosed)
        {
            if (diagnosed >= MaxDiagnosedFailures)
                return "";
            diagnosed++;

            IReadOnlyList<int> boundaries = OpcodeBoundaries(record);
            int covering = Covering(record.Bytes, boundaries, at);
            coveringOpcodes.TryGetValue(covering, out int seen);
            coveringOpcodes[covering] = seen + 1;

            var ours = new DefinitionRecord(record.Id, record.GroupId, record.FileId, reencoded);
            return Environment.NewLine +
                   $"    cache: {Trace(record.Bytes, boundaries)}" + Environment.NewLine +
                   $"    ours : {Trace(reencoded, OpcodeBoundaries(ours))}";
        }

        /// <summary>
        ///     Names the opcode whose payload spans an offset, so a byte difference is reported
        ///     against the field it belongs to rather than as a raw offset.
        /// </summary>
        /// <param name="bytes">The record bytes.</param>
        /// <param name="boundaries">The opcode boundaries within them.</param>
        /// <param name="offset">The byte offset of interest.</param>
        /// <returns>The covering opcode, or <c>-1</c> when the offset is not inside one.</returns>
        private static int Covering(byte[] bytes, IReadOnlyList<int> boundaries, int offset)
        {
            if (offset < 0 || offset >= bytes.Length)
                return -1;

            int covering = -1;
            foreach (int boundary in boundaries)
            {
                if (boundary > offset)
                    break;
                if (boundary < bytes.Length)
                    covering = bytes[boundary];
            }
            return covering;
        }

        /// <summary>
        ///     Finds every position in a record the decoder reaches as an opcode boundary.
        /// </summary>
        /// <remarks>
        ///     Truncating the buffer cannot change the path the decoder takes through it, only cut
        ///     that path short, so decoding the first <c>p</c> bytes finishes on exactly <c>p</c>
        ///     when <c>p</c> is a boundary the full decode also passes through: any shorter payload
        ///     throws on the read that runs off the end, and a real terminator stops the decode
        ///     before <c>p</c>. That builds an opcode trace out of nothing but the production
        ///     decoder, so it cannot disagree with it the way a second hand-written parser would.
        ///     <para>
        ///     Opcodes that read an element count with <see cref="JagStream.ReadByte"/> report one
        ///     spurious boundary each, because that call answers -1 at the end of the stream rather
        ///     than throwing and a count of -1 yields an empty block. It shows up as an extra entry
        ///     immediately after such an opcode, which is its count read as though it were an
        ///     opcode.
        ///     </para>
        ///     <para>It costs a decode per byte, which is why only the first few failures get one.</para>
        /// </remarks>
        /// <param name="record">The record to trace.</param>
        /// <returns>The reachable opcode boundaries, ascending.</returns>
        private IReadOnlyList<int> OpcodeBoundaries(DefinitionRecord record)
        {
            var boundaries = new List<int>();

            for (int prefix = 0; prefix <= record.Bytes.Length; prefix++)
            {
                var stream = new JagStream(record.Bytes.AsSpan(0, prefix).ToArray());
                try
                {
                    _codec.Decode(record.Id, stream);
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

        /// <summary>Renders a record's opcodes as <c>opcode@offset</c> pairs.</summary>
        /// <param name="bytes">The record bytes.</param>
        /// <param name="boundaries">The opcode boundaries within them.</param>
        /// <returns>The opcode sequence, in stream order.</returns>
        private static string Trace(byte[] bytes, IReadOnlyList<int> boundaries)
        {
            var trace = new List<string>();
            foreach (int at in boundaries)
            {
                if (at >= bytes.Length)
                    break;
                trace.Add($"{bytes[at]}@{at}");
            }
            return string.Join(" ", trace);
        }

        /// <summary>Counts how many definitions carried each opcode.</summary>
        /// <param name="counts">The running histogram.</param>
        /// <param name="definition">The definition to fold in.</param>
        private void Tally(SortedDictionary<int, int> counts, T definition)
        {
            if (_codec.OpcodesOf == null)
                return;

            foreach (int opcode in _codec.OpcodesOf(definition))
            {
                counts.TryGetValue(opcode, out int seen);
                counts[opcode] = seen + 1;
            }
        }

        /// <summary>The opcodes one definition carried, for a failure line.</summary>
        /// <param name="definition">The decoded definition.</param>
        /// <returns>The opcodes in brackets, or an empty string when the codec cannot say.</returns>
        private string Opcodes(T definition)
        {
            if (_codec.OpcodesOf == null)
                return "[unknown]";
            return "[" + string.Join(" ", _codec.OpcodesOf(definition)) + "]";
        }

        private static string Histogram(SortedDictionary<int, int> counts)
        {
            return string.Join(", ", counts.Select(c => $"{c.Key}={c.Value}"));
        }

        private static bool SameMultiset(byte[] left, byte[] right)
        {
            if (left.Length != right.Length)
                return false;

            byte[] leftSorted = (byte[])left.Clone();
            byte[] rightSorted = (byte[])right.Clone();
            Array.Sort(leftSorted);
            Array.Sort(rightSorted);
            return leftSorted.AsSpan().SequenceEqual(rightSorted);
        }

        private static int FirstDifference(byte[] expected, byte[] actual)
        {
            int shared = Math.Min(expected.Length, actual.Length);
            for (int i = 0; i < shared; i++)
                if (expected[i] != actual[i])
                    return i;
            return shared;
        }

        private static string ByteAt(byte[] bytes, int offset)
        {
            return offset < bytes.Length ? $"0x{bytes[offset]:X2}" : "end of buffer";
        }

        private static string Tail(byte[] bytes)
        {
            if (bytes.Length == 0)
                return "(empty)";
            int from = Math.Max(0, bytes.Length - 8);
            return BitConverter.ToString(bytes, from, bytes.Length - from);
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

        /// <summary>
        ///     Copies a record with sentinel bytes past its end, so an over-read is visible.
        /// </summary>
        /// <remarks>
        ///     <see cref="JagStream.ReadByte"/> returns -1 at the end of the stream without
        ///     advancing, and every one of these decode loops treats anything below 1 as the
        ///     terminator, so a decoder that ran off the end still reports a position equal to the
        ///     length and looks exact. Reading into the padding advances instead, and 0xAA is
        ///     neither a terminator nor a plausible payload length.
        /// </remarks>
        /// <param name="data">The record bytes.</param>
        /// <returns>A padded copy.</returns>
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
