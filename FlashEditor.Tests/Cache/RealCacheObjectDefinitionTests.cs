using FlashEditor.cache;
using FlashEditor.Definitions;
using FlashEditor.Tests.Cache.RealCache;
using System;
using System.Collections.Generic;
using System.Linq;
using Xunit;
using Xunit.Abstractions;

namespace FlashEditor.Tests.Cache
{
    /// <summary>
    ///     Runs every object definition in the real revision-639 cache through the production
    ///     decoder and encoder.
    /// </summary>
    /// <remarks>
    ///     The decoders in this project were derived from a build-637 Java client while the cache
    ///     is build 639 - see AGENTS.md. For most formats that gap is hard to test for, but an
    ///     object definition is an opcode stream: read an opcode byte, read the payload its
    ///     opcode implies, repeat until opcode 0. Nothing in the stream states how long a payload
    ///     is, so a decoder that mis-sizes one payload by a single byte desynchronises and every
    ///     later opcode is read from the wrong offset. Landing exactly on the terminator after
    ///     tens of thousands of definitions is therefore strong evidence that every payload size
    ///     the decoder believes in is the size the data actually uses.
    ///     <para>
    ///     The three tests are deliberately ordered by strength - decodes at all, consumes its
    ///     buffer exactly, re-encodes to the same bytes - and each sweeps independently so a
    ///     failure of the strongest does not hide the result of the weaker ones.
    ///     </para>
    /// </remarks>
    public class RealCacheObjectDefinitionTests : IClassFixture<RealCacheFixture>
    {
        /// <summary>Failures listed before the report is truncated.</summary>
        private const int MaxReportedFailures = 10;

        private readonly RealCacheFixture _cache;
        private readonly ITestOutputHelper _output;

        /// <summary>Archives the current sweep read, for the coverage line each test prints.</summary>
        private int _examinedArchives;

        /// <summary>Binds the shared open cache and the per-test output sink.</summary>
        public RealCacheObjectDefinitionTests(RealCacheFixture cache, ITestOutputHelper output)
        {
            _cache = cache;
            _output = output;
        }

        /// <summary>One object definition's id and the exact bytes the cache stores for it.</summary>
        private readonly struct StoredDefinition
        {
            /// <summary>The object id, which is the archive and file ids folded together.</summary>
            public int Id { get; }

            /// <summary>The definition's bytes as they sit in the unpacked archive.</summary>
            public byte[] Bytes { get; }

            public StoredDefinition(int id, byte[] bytes)
            {
                Id = id;
                Bytes = bytes;
            }
        }

        // ===================================================================
        //  1 - decodes at all
        // ===================================================================

        /// <summary>
        ///     Every object definition in the cache must come out of the decoder without throwing.
        /// </summary>
        /// <remarks>
        ///     The weakest of the three checks, and on its own nearly worthless: a decoder that
        ///     mis-sized a payload would usually still "succeed", because a desynchronised stream
        ///     mostly lands on opcodes that happen to be handled. It is here so that a hard
        ///     failure is reported as a hard failure rather than being folded into the exactness
        ///     count.
        /// </remarks>
        [RealCacheFact]
        public void AllObjectDefinitions_DecodeWithoutThrowing()
        {
            var failures = new List<string>();
            int decoded = 0;

            foreach (StoredDefinition stored in LoadDefinitions(failures))
            {
                try
                {
                    ObjectDefinition.DecodeFromStream(new JagStream(stored.Bytes));
                    decoded++;
                }
                catch (Exception ex)
                {
                    failures.Add($"object {stored.Id}: decode threw {ex.GetType().Name}: {ex.Message}");
                }
            }

            _output.WriteLine($"{decoded} object definitions decoded without throwing");
            ReportSampling();

            Assert.True(decoded > 0, "no object definition was decoded, so nothing was checked");
            AssertNoFailures(failures, "object definitions failed to decode");
        }

        // ===================================================================
        //  2 - consumes its buffer exactly
        // ===================================================================

        /// <summary>
        ///     Every object definition must be consumed to its last byte, stopping on the stream's
        ///     own terminator rather than on the end of the buffer.
        /// </summary>
        /// <remarks>
        ///     Asserting the position equals the length would not be enough on its own.
        ///     <see cref="JagStream.ReadByte"/> returns -1 at the end of the buffer and the decode
        ///     loop treats that exactly like the opcode-0 terminator, so a definition whose parse
        ///     ran off the end would leave the position sitting on the length and look perfect.
        ///     Decoding over the bytes plus one extra zero byte separates the two cases: a decoder
        ///     that stops on the real terminator finishes on the original length, and one that
        ///     overruns swallows the guard byte and finishes one past it.
        /// </remarks>
        [RealCacheFact]
        public void AllObjectDefinitions_ConsumeTheirBufferExactly()
        {
            var failures = new List<string>();
            var opcodesInFailures = new SortedDictionary<int, int>();
            var opcodesOverall = new SortedDictionary<int, int>();
            int exact = 0;

            foreach (StoredDefinition stored in LoadDefinitions(failures))
            {
                //One trailing zero: an overrun reads it as a terminator and lands past the real
                //end instead of throwing somewhere unrelated, which keeps the diagnosis readable.
                byte[] guarded = new byte[stored.Bytes.Length + 1];
                Array.Copy(stored.Bytes, guarded, stored.Bytes.Length);

                var stream = new JagStream(guarded);
                var def = new ObjectDefinition { id = stored.Id };

                try
                {
                    def.Decode(stream);
                }
                catch (Exception ex)
                {
                    failures.Add($"object {stored.Id}: decode threw {ex.GetType().Name} at offset " +
                                 $"{stream.Position} of {stored.Bytes.Length}; opcodes {Opcodes(def)}");
                    Tally(opcodesInFailures, def);
                    continue;
                }

                Tally(opcodesOverall, def);

                if (stream.Position == stored.Bytes.Length)
                {
                    exact++;
                    continue;
                }

                string how = stream.Position > stored.Bytes.Length ? "overran" : "stopped short of";
                failures.Add($"object {stored.Id}: {how} its {stored.Bytes.Length} bytes, ending at " +
                             $"{stream.Position}; opcodes {Opcodes(def)}; tail {Tail(stored.Bytes)}");
                Tally(opcodesInFailures, def);
            }

            _output.WriteLine($"{exact} object definitions consumed their buffer exactly");
            _output.WriteLine("opcodes seen: " + Histogram(opcodesOverall));
            if (opcodesInFailures.Count > 0)
                _output.WriteLine("opcodes seen in failing definitions: " + Histogram(opcodesInFailures));
            ReportSampling();

            Assert.True(exact > 0, "no object definition was decoded, so nothing was checked");
            AssertNoFailures(failures, "object definitions did not consume their buffer exactly");
        }

        // ===================================================================
        //  3 - re-encodes to the same bytes
        // ===================================================================

        /// <summary>
        ///     Every object definition must re-encode to the exact bytes it was decoded from.
        /// </summary>
        /// <remarks>
        ///     The editor rewrites a definition through this encoder whenever the user saves one,
        ///     so any field the decoder understands but the encoder drops, reorders or resizes is
        ///     lost the first time the definition is touched. Re-encoding is driven by the
        ///     decoder's opcode hit map rather than by field values, which is what makes an exact
        ///     match achievable; this test is what turns that design claim into a measurement.
        /// </remarks>
        [RealCacheFact]
        public void AllObjectDefinitions_ReEncodeToTheirCapturedBytes()
        {
            var failures = new List<string>();
            var opcodesInFailures = new SortedDictionary<int, int>();
            int identical = 0;
            int reordered = 0;

            foreach (StoredDefinition stored in LoadDefinitions(failures))
            {
                ObjectDefinition def;
                byte[] reencoded;

                try
                {
                    def = ObjectDefinition.DecodeFromStream(new JagStream(stored.Bytes));
                    def.id = stored.Id;
                    reencoded = def.Encode().ToArray();
                }
                catch (Exception ex)
                {
                    failures.Add($"object {stored.Id}: re-encode threw {ex.GetType().Name}: {ex.Message}");
                    continue;
                }

                if (reencoded.SequenceEqual(stored.Bytes))
                {
                    identical++;
                    continue;
                }

                byte[] storedSorted = (byte[])stored.Bytes.Clone();
                byte[] reencodedSorted = (byte[])reencoded.Clone();
                Array.Sort(storedSorted);
                Array.Sort(reencodedSorted);
                bool sameBytes = storedSorted.SequenceEqual(reencodedSorted);
                if (sameBytes)
                    reordered++;

                int at = FirstDifference(stored.Bytes, reencoded);
                failures.Add($"object {stored.Id}: re-encoded {reencoded.Length} bytes from a stored " +
                             $"{stored.Bytes.Length}, first difference at {at} " +
                             $"({ByteAt(stored.Bytes, at)} became {ByteAt(reencoded, at)}), " +
                             $"{(sameBytes ? "same bytes in a different order" : "different content")}; " +
                             $"opcodes {Opcodes(def)}");
                if (!sameBytes)
                    Tally(opcodesInFailures, def);
            }

            _output.WriteLine($"{identical} object definitions re-encoded to byte-identical output");
            if (reordered > 0)
            {
                _output.WriteLine($"{reordered} more carried the same bytes in a different order, " +
                                  "so the encoder is no longer replaying the stored opcode order");
            }
            if (opcodesInFailures.Count > 0)
                _output.WriteLine("opcodes seen in failing definitions: " + Histogram(opcodesInFailures));
            ReportSampling();

            Assert.True(identical > 0, "no object definition was re-encoded, so nothing was checked");
            AssertNoFailures(failures, "object definitions did not re-encode to their stored bytes");
        }

        // ===================================================================
        //  walkable
        // ===================================================================

        /// <summary>
        ///     Counts how many real definitions carry the walk-blocking opcodes, so the severity of
        ///     any defect in how <see cref="ObjectDefinition.walkable"/> is written back is a
        ///     measured number rather than a guess.
        /// </summary>
        [RealCacheFact]
        public void Walkable_IsSetByAMeasuredShareOfRealDefinitions()
        {
            var failures = new List<string>();
            int total = 0;
            int blocked = 0;
            int op17 = 0;
            int op18 = 0;

            foreach (StoredDefinition stored in LoadDefinitions(failures))
            {
                ObjectDefinition def = ObjectDefinition.DecodeFromStream(new JagStream(stored.Bytes));
                total++;

                if (def.decoded[17])
                    op17++;
                if (def.decoded[18])
                    op18++;
                if (!def.walkable)
                    blocked++;
            }

            _output.WriteLine($"{blocked} of {total} object definitions are not walkable " +
                              $"({op17} carry opcode 17, {op18} carry opcode 18)");
            ReportSampling();

            AssertNoFailures(failures, "object definitions could not be read");
            Assert.True(total > 0, "no object definition was examined");

            //Stated as a floor rather than an exact figure so the test pins the fact that the
            //field is in real use without breaking when the cache is swapped for another build.
            Assert.True(blocked > 1000,
                $"only {blocked} of {total} definitions block walking, which is too few for this " +
                "cache - the walk-blocking opcodes are probably being misread");
        }

        /// <summary>
        ///     Pins the round trip of <see cref="ObjectDefinition.walkable"/> through an edit: a
        ///     definition made unwalkable must encode to bytes that decode back as unwalkable.
        /// </summary>
        /// <remarks>
        ///     Needs no cache. The encoder emits opcodes 17 and 18 from the decoder's hit map, so
        ///     before the fix a walkable flag set in the UI on a definition that arrived without
        ///     either opcode was written back out as though it had never been touched. Both
        ///     directions are checked because clearing the flag was equally lossy in reverse.
        /// </remarks>
        [Fact]
        public void Walkable_SurvivesAnEditThroughEncodeAndDecode()
        {
            var original = new ObjectDefinition { name = "Gate" };
            Assert.True(original.walkable);

            original.walkable = false;
            ObjectDefinition blocked = ObjectDefinition.DecodeFromStream(new JagStream(original.Encode().ToArray()));
            Assert.False(blocked.walkable);

            blocked.walkable = true;
            ObjectDefinition cleared = ObjectDefinition.DecodeFromStream(new JagStream(blocked.Encode().ToArray()));
            Assert.True(cleared.walkable);
        }

        // ===================================================================
        //  Format facts, pinned without needing the cache
        // ===================================================================

        /// <summary>
        ///     Opcode 75 carries a one byte payload rather than being a bare flag.
        /// </summary>
        /// <remarks>
        ///     Read as a flag it swallows nothing, so the byte after it is taken for the next
        ///     opcode and the rest of the definition is parsed from the wrong offset. 1,591
        ///     definitions in the shipped cache carry it. The build-637 client reads it as
        ///     <c>readUnsignedByte</c> into <c>Class352.anInt2975</c>.
        /// </remarks>
        [Fact]
        public void Opcode75_CarriesAOneBytePayload()
        {
            //75 with payload 7, then opcode 23 - a bare flag - then the terminator.
            AssertConsumedExactlyAndReEncoded(new byte[] { 75, 7, 23, 0 }, def => Assert.True(def.decoded[23]));
        }

        /// <summary>
        ///     Opcode 72 carries a signed short, the same shape as the offsets at 70 and 71.
        /// </summary>
        /// <remarks>
        ///     Read as a single byte it leaves the low half of the short behind, which the decoder
        ///     then reads as an opcode. 371 definitions in the cache carry it. The build-637
        ///     client reads it as <c>readShort() &lt;&lt; 2</c> into <c>Class352.anInt2946</c>.
        /// </remarks>
        [Fact]
        public void Opcode72_CarriesASignedShortPayload()
        {
            AssertConsumedExactlyAndReEncoded(new byte[] { 72, 0xFF, 0xE2, 23, 0 }, def => Assert.True(def.decoded[23]));
        }

        /// <summary>
        ///     An opcode the stream carried is written back even when its payload happens to equal
        ///     the field's default.
        /// </summary>
        /// <remarks>
        ///     Emitting on the field value alone drops a stored <c>19 00</c> or <c>70 00 00</c>,
        ///     which changes the definition's bytes - and so its CRC - the first time the user
        ///     saves a file they never edited.
        /// </remarks>
        [Fact]
        public void OpcodesWhosePayloadEqualsTheDefault_AreStillWrittenBack()
        {
            //19 with category 0, then 70 with offset 0: both defaults, both present in the stream.
            AssertConsumedExactlyAndReEncoded(new byte[] { 19, 0, 70, 0, 0, 0 },
                def => Assert.Equal(0, def.category));
        }

        /// <summary>
        ///     Opcodes are written back in the order the stream presented them, not in ascending
        ///     order.
        /// </summary>
        /// <remarks>
        ///     The definitions in the cache are not stored in ascending opcode order, so an
        ///     encoder with its own fixed order rewrites all but a handful of them.
        /// </remarks>
        [Fact]
        public void OpcodeOrder_IsTakenFromTheStreamRatherThanFromTheEncoder()
        {
            //15 before 14, which is the reverse of the order the encoder would pick on its own.
            AssertConsumedExactlyAndReEncoded(new byte[] { 15, 3, 14, 2, 0 }, def =>
            {
                Assert.Equal(2, def.sizeX);
                Assert.Equal(3, def.sizeY);
            });
        }

        /// <summary>
        ///     A repeated opcode is written back at every position it occupied, keeping the value
        ///     each occurrence carried.
        /// </summary>
        /// <remarks>
        ///     268 definitions in the cache repeat an opcode with a different value each time. The
        ///     decoder keeps only the last, as the client does, so the earlier occurrences can be
        ///     reproduced only from the bytes they were read from.
        /// </remarks>
        [Fact]
        public void RepeatedOpcodes_KeepBothTheirPositionsAndTheirValues()
        {
            AssertConsumedExactlyAndReEncoded(new byte[] { 19, 4, 19, 9, 0 },
                def => Assert.Equal(9, def.category));
        }

        /// <summary>
        ///     A definition carrying both ambient sound opcodes keeps both.
        /// </summary>
        /// <remarks>
        ///     44 definitions in the cache carry 78 and 79 together. The two write the same fields,
        ///     so only the one read last still has its values to re-encode from; dropping the other
        ///     would shorten the definition.
        /// </remarks>
        [Fact]
        public void BothAmbientSoundOpcodesTogether_SurviveAReEncode()
        {
            byte[] stream =
            {
                78, 0x03, 0xE8, 0x05,                         // id 1000, 5 loops
                79, 0x03, 0xE9, 0x01, 0xF4, 0x02, 1, 0x01, 0x2C, // id 1001, extra 500, 2 loops, one sound 300
                0
            };

            AssertConsumedExactlyAndReEncoded(stream, def =>
            {
                Assert.Equal(1001, def.ambientSoundId);
                Assert.Single(def.extraSounds);
                Assert.Equal(300, def.extraSounds[0]);
            });
        }

        /// <summary>
        ///     Decodes a hand-built definition, checks it landed on the terminator rather than the
        ///     end of the buffer, and checks it re-encodes to the bytes it came from.
        /// </summary>
        /// <param name="stream">The definition bytes, terminator included.</param>
        /// <param name="check">Field assertions for the decoded definition.</param>
        private static void AssertConsumedExactlyAndReEncoded(byte[] stream, Action<ObjectDefinition> check)
        {
            byte[] guarded = new byte[stream.Length + 1];
            Array.Copy(stream, guarded, stream.Length);

            var reader = new JagStream(guarded);
            var def = new ObjectDefinition();
            def.Decode(reader);

            Assert.Equal(stream.Length, reader.Position);
            check(def);
            Assert.Equal(stream, def.Encode().ToArray());
        }

        // ===================================================================
        //  Helpers
        // ===================================================================

        /// <summary>
        ///     Reads every object definition the sweep covers straight out of the cache.
        /// </summary>
        /// <remarks>
        ///     Goes through the fixture rather than <see cref="RSCache.GetObjectDefinition"/>
        ///     because that path memoises every container it touches, which for the largest index
        ///     in the cache means holding the whole thing in memory for the length of the run.
        /// </remarks>
        /// <param name="failures">Collects archives that could not be read.</param>
        /// <returns>The stored bytes of each definition, ascending by object id.</returns>
        private IEnumerable<StoredDefinition> LoadDefinitions(List<string> failures)
        {
            RSReferenceTable table = _cache.Table(RSConstants.OBJECTS_DEFINITIONS_INDEX);
            _examinedArchives = 0;

            foreach (int archiveId in _cache.ArchivesToExamine(table))
            {
                _examinedArchives++;

                byte[] stored = _cache.RawContainer(RSConstants.OBJECTS_DEFINITIONS_INDEX, archiveId);
                if (stored == null)
                    continue;

                int[] fileIds = table.GetArchiveEntry(archiveId).GetValidFileIds();
                if (fileIds.Length == 0)
                    continue;

                RSArchive archive;
                try
                {
                    RSContainer container =
                        _cache.TryDecodeContainer(RSConstants.OBJECTS_DEFINITIONS_INDEX, archiveId, stored);
                    if (container == null)
                    {
                        failures.Add($"archive {archiveId}: container would not decode");
                        continue;
                    }

                    archive = RSArchive.Decode(container.GetStream(), fileIds);
                }
                catch (Exception ex)
                {
                    failures.Add($"archive {archiveId}: could not be unpacked - {ex.GetType().Name}: {ex.Message}");
                    continue;
                }

                foreach (int fileId in fileIds)
                {
                    byte[] bytes;
                    try
                    {
                        bytes = archive.GetFile(fileId)?.ToArray();
                    }
                    catch (Exception ex)
                    {
                        failures.Add($"archive {archiveId} file {fileId}: {ex.GetType().Name}: {ex.Message}");
                        continue;
                    }

                    if (bytes == null || bytes.Length == 0)
                        continue;

                    //The id RSCache.GetObjectDefinition assigns, so a failure names the object the
                    //editor would name.
                    yield return new StoredDefinition((archiveId * 256) + fileId, bytes);
                }
            }
        }

        private static void Tally(SortedDictionary<int, int> counts, ObjectDefinition def)
        {
            for (int op = 0; op < def.decoded.Length; op++)
            {
                if (!def.decoded[op])
                    continue;
                counts.TryGetValue(op, out int seen);
                counts[op] = seen + 1;
            }
        }

        private static string Histogram(SortedDictionary<int, int> counts)
        {
            return string.Join(", ", counts.Select(c => $"{c.Key}={c.Value}"));
        }

        private static string Opcodes(ObjectDefinition def)
        {
            var seen = new List<int>();
            for (int op = 0; op < def.decoded.Length; op++)
                if (def.decoded[op])
                    seen.Add(op);
            return "[" + string.Join(" ", seen) + "]";
        }

        private static string Tail(byte[] bytes)
        {
            int from = Math.Max(0, bytes.Length - 8);
            return BitConverter.ToString(bytes, from, bytes.Length - from);
        }

        private static string ByteAt(byte[] bytes, int offset)
        {
            return offset < bytes.Length ? $"0x{bytes[offset]:X2}" : "end of buffer";
        }

        private static int FirstDifference(byte[] expected, byte[] actual)
        {
            int shared = Math.Min(expected.Length, actual.Length);
            for (int i = 0; i < shared; i++)
                if (expected[i] != actual[i])
                    return i;
            return shared;
        }

        /// <summary>
        ///     States how much of the index the sweep actually covered.
        /// </summary>
        /// <remarks>
        ///     The object index holds fewer archives than the fixture's sample cap, so the default
        ///     run reads every definition and the full-sweep switch changes nothing here. Saying
        ///     so explicitly matters: "sampled" and "swept" are very different claims to make
        ///     about a codec, and the numbers below decide which one this run earned.
        /// </remarks>
        private void ReportSampling()
        {
            int total = _cache.Table(RSConstants.OBJECTS_DEFINITIONS_INDEX).GetArchiveCount();

            if (_examinedArchives >= total)
            {
                _output.WriteLine($"every one of the index's {total} archives was read" +
                                  (_cache.FullSweep ? "" : $" - it holds fewer than the " +
                                   $"{RealCacheFixture.SampleArchivesPerIndex}-archive sample cap"));
                return;
            }

            _output.WriteLine($"sampled {_examinedArchives} of the index's {total} archives; " +
                              $"set {RealCacheLocator.FullSweepVariable}=1 to read every one");
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
