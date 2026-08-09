using FlashEditor.Cache;
using FlashEditor.Tests.Cache.RealCache;
using System;
using System.Collections.Generic;
using System.Linq;
using Xunit;
using Xunit.Abstractions;
using FlashEditor.IO;

namespace FlashEditor.Tests.Cache
{
    /// <summary>
    ///     Measures which shape the reference tables in a real revision-639 cache actually take:
    ///     which optional blocks the flags byte declares, which format byte each table carries,
    ///     and how many bytes sit past the last field the format defines.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///     These properties decide which branches of <see cref="ReferenceTableCodec"/> the shipped
    ///     data ever reaches. A branch no table exercises is a branch no byte-identity sweep can
    ///     defend, so it is worth knowing by measurement rather than by report which of them are
    ///     live here - both to stop documentation describing a shape this cache never takes, and
    ///     to stop a reader assuming a block is absent when it is merely unmeasured.
    ///     </para>
    ///     <para>
    ///     The trailing-byte count is deliberately taken against a length computed field by field
    ///     from the format rather than against <see cref="ReferenceTableCodec.Encode"/>. Measuring
    ///     the codec against itself would report zero surplus for any field the codec invented,
    ///     and the point of the measurement is to locate bytes the format does not account for.
    ///     Three independent figures then have to agree on the same offset: that formula, where
    ///     <see cref="ReferenceTableCodec.Decode"/> leaves the stream, and how many bytes
    ///     <see cref="ReferenceTableCodec.Encode"/> writes. A surplus only survives all three if
    ///     the bytes really are past the end of the table rather than a field this project drops.
    ///     </para>
    /// </remarks>
    public class RealCacheReferenceTableShapeTests : IClassFixture<RealCacheFixture>
    {
        private readonly RealCacheFixture _cache;
        private readonly ITestOutputHelper _output;

        /// <summary>Failures reported per test before the list is truncated.</summary>
        private const int MaxReportedFailures = 10;

        /// <summary>Binds the shared open cache and the per-test output sink.</summary>
        public RealCacheReferenceTableShapeTests(RealCacheFixture cache, ITestOutputHelper output)
        {
            _cache = cache;
            _output = output;
        }

        // ===================================================================
        //  Flags
        // ===================================================================

        /// <summary>
        ///     Only the identifiers and whirlpool flags are set anywhere in this cache. The sizes
        ///     block and the per-archive entry hash are declared by no table at all, so both of
        ///     the codec branches that read them are dead against this data.
        /// </summary>
        /// <remarks>
        ///     Sizes matters most: <c>AGENTS.md</c> documents the block in operational detail,
        ///     including the rule that its compressed figure covers the stored container minus its
        ///     version trailer. That rule is correct and unreachable here, and a reader who assumes
        ///     it is live will look for a discrepancy that cannot exist. Index 2 carrying no
        ///     identifiers is the other half of it - config groups are addressed by id, and nothing
        ///     may fall back to a name lookup there.
        /// </remarks>
        [RealCacheFact]
        public void ReferenceTableFlags_DeclareOnlyIdentifiersAndWhirlpoolInThisCache()
        {
            var identifiers = new SortedSet<int>();
            var whirlpool = new SortedSet<int>();
            var sizes = new SortedSet<int>();
            var hashes = new SortedSet<int>();
            var unknownBits = new List<string>();

            foreach (int indexId in _cache.TableIndexes)
            {
                RSReferenceTable table = _cache.Table(indexId);

                if (table.hasIdentifiers)
                    identifiers.Add(indexId);
                if (table.usesWhirlpool)
                    whirlpool.Add(indexId);
                if (table.sizes)
                    sizes.Add(indexId);
                if (table.entryHashes)
                    hashes.Add(indexId);

                int known = RSReferenceTable.FLAG_IDENTIFIERS | RSReferenceTable.FLAG_WHIRLPOOL
                          | RSReferenceTable.FLAG_SIZES | RSReferenceTable.FLAG_HASH;
                if ((table.flags & ~known) != 0)
                    unknownBits.Add($"index {indexId}: flags 0x{table.flags:X2} carries a bit outside the four documented ones");
            }

            _output.WriteLine($"identifiers (0x01): {Describe(identifiers)}");
            _output.WriteLine($"whirlpool   (0x02): {Describe(whirlpool)}");
            _output.WriteLine($"sizes       (0x04): {Describe(sizes)}");
            _output.WriteLine($"hash        (0x08): {Describe(hashes)}");

            AssertNoFailures(unknownBits, "reference tables set a flag bit outside the documented four");

            Assert.True(sizes.Count == 0,
                $"the sizes block is not dead after all - flag 0x04 is set on {Describe(sizes)}");
            Assert.True(hashes.Count == 0,
                $"the per-archive entry hash is not dead after all - flag 0x08 is set on {Describe(hashes)}");

            Assert.Equal(new[] { 3, 5, 6, 8, 10, 12, 13, 23, 30, 31, 32, 33 }, identifiers.ToArray());
            Assert.Equal(new[] { 30 }, whirlpool.ToArray());

            //Named separately because the map path resolves index 5 by name hash and nothing may
            //assume the same is possible on the config index.
            Assert.False(_cache.Table(RSConstants.CONFIG).hasIdentifiers,
                "index 2 declares identifiers, so it does carry name hashes");
        }

        // ===================================================================
        //  Format
        // ===================================================================

        /// <summary>
        ///     No table in this cache is format 7, so the per-archive flags byte - and with it the
        ///     only in-table statement that an archive is XTEA encrypted - exists nowhere on disk.
        ///     Encryption here can only ever be inferred, which is what makes the read path's
        ///     guessing and the write path's refusal to guess necessary rather than defensive.
        /// </summary>
        /// <remarks>
        ///     Index 36 is the outlier and has to stay decodable: a format-5 stub of four bytes
        ///     declaring zero groups, which is the table that once took the whole decode down
        ///     through <c>Max()</c> on an empty sequence.
        /// </remarks>
        [RealCacheFact]
        public void ReferenceTableFormats_AreSixExceptTheEmptyStubOnIndex36()
        {
            var byFormat = new SortedDictionary<int, SortedSet<int>>();

            foreach (int indexId in _cache.TableIndexes)
            {
                int format = _cache.Table(indexId).format;
                if (!byFormat.TryGetValue(format, out SortedSet<int> indexes))
                    byFormat[format] = indexes = new SortedSet<int>();
                indexes.Add(indexId);
            }

            foreach (var kv in byFormat)
                _output.WriteLine($"format {kv.Key}: {kv.Value.Count} tables - {Describe(kv.Value)}");

            //Every "nowhere in this cache" statement in this class is only as strong as the set it
            //swept, so the size of that set is pinned here rather than left implicit. The meta
            //index declares more records than it fills: 34 and 35 hold nothing at all.
            _output.WriteLine($"{_cache.TableIndexes.Count} tables across " +
                              $"{_cache.RecordCount(RSConstants.META_INDEX)} meta index records");
            Assert.Equal(35, _cache.TableIndexes.Count);

            var formatSeven = byFormat.Where(kv => kv.Key >= 7).SelectMany(kv => kv.Value).ToArray();
            Assert.True(formatSeven.Length == 0,
                $"format 7 does occur after all, on index(es) {string.Join(", ", formatSeven)}, " +
                "so the per-archive flags byte is live in this cache");

            Assert.Equal(new[] { 5, 6 }, byFormat.Keys.ToArray());
            Assert.Equal(new[] { 36 }, byFormat[5].ToArray());

            RSReferenceTable stub = _cache.Table(36);
            Assert.Equal(0, stub.GetArchiveCount());
            Assert.Equal(4, _cache.TablePayload(36).Length);
        }

        // ===================================================================
        //  Trailing bytes
        // ===================================================================

        /// <summary>
        ///     A reference table either consumes its payload to the byte or carries exactly four
        ///     zero bytes per <em>file</em> past it. Nothing else occurs, and nothing requires a
        ///     tail to be there at all.
        /// </summary>
        /// <remarks>
        ///     <para>
        ///     The tail is a property of one cache rather than of the format. The repack carries
        ///     it on four indexes - 9 has 3784 bytes, 26 has 4, 27 has 1684 and 29 has 728 - and
        ///     every one of the vanilla b639 capture's 35 tables consumes to the byte. So it is
        ///     repacker residue, which is why the shape assertion below is universal and the set
        ///     of indexes carrying one is scoped to the cache: a parser must tolerate a tail and
        ///     must never require one.
        ///     </para>
        ///     <para>
        ///     Per file, not per group, and the distinction is not pedantry. Index 27 holds 421
        ///     files across 2 groups and index 29 holds 182 across 1, so a per-group reading
        ///     predicts 8 and 4 bytes where the repack has 1684 and 728. A parser sized from the
        ///     wrong one would either assert exact consumption and reject four of its tables, or
        ///     skip a fixed per-group stride and leave the rest of the block in the stream. That
        ///     discrimination is asserted wherever the data can express it.
        ///     </para>
        ///     <para>
        ///     What is measured is the width and the position. The obvious reading of them is a
        ///     per-file identifier block emitted with the identifiers flag clear, since that is
        ///     the field the format would put at exactly that offset, but nothing here proves the
        ///     provenance.
        ///     </para>
        /// </remarks>
        [RealCacheFact]
        public void ReferenceTableTrailingBytes_AreAbsentOrFourZeroBytesPerFile()
        {
            var failures = new List<string>();
            var trailingByIndex = new SortedDictionary<int, int>();
            var groupsByIndex = new SortedDictionary<int, int>();
            var filesByIndex = new SortedDictionary<int, int>();

            foreach (int indexId in _cache.TableIndexes)
            {
                RSReferenceTable table = _cache.Table(indexId);
                byte[] payload = _cache.TablePayload(indexId);

                int groups = table.GetArchiveCount();
                int files = table.GetArchiveEntries().Values.Sum(e => e.GetValidFileIds().Length);
                groupsByIndex[indexId] = groups;
                filesByIndex[indexId] = files;

                int documented = DocumentedTableLength(table, groups, files);

                //Where Decode actually stopped. Without this the surplus is only a claim about a
                //formula, and a field the decoder silently skipped would read as a trailing byte.
                var stream = new JagStream(payload);
                ReferenceTableCodec.Decode(stream);
                int consumed = stream.Position;
                if (consumed != documented)
                {
                    failures.Add($"index {indexId}: the format accounts for {documented} bytes but Decode consumed {consumed}");
                    continue;
                }

                //The codec's own output has to land on the same byte, or the field-by-field length
                //above is measuring something other than what Decode consumed.
                int encoded = ReferenceTableCodec.Encode(table).ToArray().Length;
                if (encoded != documented)
                {
                    failures.Add($"index {indexId}: the format accounts for {documented} bytes but the codec emits {encoded}");
                    continue;
                }

                if (documented > payload.Length)
                {
                    failures.Add($"index {indexId}: the format accounts for {documented} bytes of a {payload.Length} byte table");
                    continue;
                }

                int trailing = payload.Length - documented;
                trailingByIndex[indexId] = trailing;

                if (trailing > 0 && payload.AsSpan(documented).IndexOfAnyExcept((byte)0) >= 0)
                    failures.Add($"index {indexId}: {trailing} trailing bytes past the table are not all zero");
            }

            foreach (var kv in trailingByIndex.Where(kv => kv.Value > 0))
            {
                _output.WriteLine($"index {kv.Key}: {kv.Value} trailing bytes, " +
                                  $"{groupsByIndex[kv.Key]} groups ({groupsByIndex[kv.Key] * 4} bytes if per group), " +
                                  $"{filesByIndex[kv.Key]} files ({filesByIndex[kv.Key] * 4} bytes if per file)");
            }

            AssertNoFailures(failures, "reference tables did not account for their payload as the format describes");

            //Every table was measured, so an empty tail set is a measured absence rather than a
            //loop that never ran.
            Assert.Equal(_cache.TableIndexes.Count, trailingByIndex.Count);
            Assert.True(trailingByIndex.Count > 0, "no reference table was measured at all");

            var withTrailing = trailingByIndex.Where(kv => kv.Value > 0).Select(kv => kv.Key).ToArray();
            _output.WriteLine($"{_cache.Profile.Name}: {withTrailing.Length} of {trailingByIndex.Count} " +
                              "tables carry a tail");

            //Four per file, on every table that carries one. This is the whole shape claim and it
            //holds in any cache: a tail of some other width is a field this project drops, not
            //residue past the end of the table.
            foreach (int indexId in withTrailing)
            {
                Assert.Equal(filesByIndex[indexId] * 4, trailingByIndex[indexId]);
            }

            //Per file and specifically not per group. Only a table holding more files than groups
            //can tell the two readings apart, so the discrimination is asserted over exactly those
            //- and a cache whose tails all sit on one-file-per-group indexes must not be read as
            //having settled the question.
            if (withTrailing.Length > 0)
            {
                int[] discriminating = withTrailing.Where(i => filesByIndex[i] != groupsByIndex[i]).ToArray();
                Assert.True(discriminating.Length > 0,
                    "every table with a tail holds one file per group, so nothing here distinguishes " +
                    "four bytes per file from four per group");

                foreach (int indexId in discriminating)
                    Assert.NotEqual(groupsByIndex[indexId] * 4, trailingByIndex[indexId]);
            }

            //Which indexes carry one is a fact about this cache, not about the format.
            if (_cache.Profile.TablesWithATail != null)
                Assert.Equal(_cache.Profile.TablesWithATail, withTrailing);

            //Named separately because "some indexes may have trailing bytes" is only useful
            //alongside proof that the two the map and config paths read consume to the byte.
            Assert.Equal(0, trailingByIndex[RSConstants.CONFIG]);
            Assert.Equal(0, trailingByIndex[RSConstants.MAPS_INDEX]);
        }

        // ===================================================================
        //  Helpers
        // ===================================================================

        /// <summary>
        ///     The number of bytes the reference-table format accounts for, summed field by field
        ///     from the table's own header rather than by encoding it.
        /// </summary>
        /// <remarks>
        ///     Every block is a fixed width times either the group count or the file count, so the
        ///     length is computable without walking the payload. Deriving it independently is the
        ///     whole point: it is what makes "these bytes are past the end of the table" a claim
        ///     about the format instead of a claim about this project's encoder.
        /// </remarks>
        /// <param name="table">The decoded table, for its format and flags.</param>
        /// <param name="groups">The number of groups the table describes.</param>
        /// <param name="files">The total number of files across every group.</param>
        /// <returns>The length in bytes of the table as the format defines it.</returns>
        private static int DocumentedTableLength(RSReferenceTable table, int groups, int files)
        {
            int length = 1;                                     //format byte
            if (table.format >= 6)
                length += 4;                                    //table version
            length += 1;                                        //flags byte
            length += 2;                                        //group count
            length += 2 * groups;                               //delta-encoded group ids

            if (table.hasIdentifiers)
                length += 4 * groups;                           //per-group name hash
            length += 4 * groups;                                //per-group crc
            if (table.entryHashes)
                length += 4 * groups;                           //per-group entry hash
            if (table.usesWhirlpool)
                length += 64 * groups;                          //per-group whirlpool digest
            if (table.sizes)
                length += 8 * groups;                           //per-group compressed and uncompressed sizes
            length += 4 * groups;                                //per-group version
            if (table.format >= 7)
                length += groups;                               //per-group flags byte

            length += 2 * groups;                                //per-group file count
            length += 2 * files;                                 //delta-encoded file ids
            if (table.hasIdentifiers)
                length += 4 * files;                             //per-file name hash

            return length;
        }

        private static string Describe(IEnumerable<int> indexes)
        {
            var ids = indexes.ToArray();
            return ids.Length == 0 ? "none" : string.Join(", ", ids);
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
