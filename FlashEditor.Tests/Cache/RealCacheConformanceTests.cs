using FlashEditor.cache;
using FlashEditor.Cache.Util;
using FlashEditor.Tests.Cache.RealCache;
using FlashEditor.Utils;
using System;
using System.Collections.Generic;
using System.Linq;
using Xunit;
using Xunit.Abstractions;

namespace FlashEditor.Tests.Cache
{
    /// <summary>
    ///     Pins the cache codec against a real revision-639 cache.
    /// </summary>
    /// <remarks>
    ///     Every other codec test round-trips this encoder against this decoder, so the two
    ///     agreeing on a misreading of the wire format would pass every one of them. These tests
    ///     assert against bytes this project did not write: containers and reference tables as
    ///     they sit in a shipped dat2, and the CRCs the cache itself carries over them.
    ///     <para>
    ///     They skip when no cache is present - see <see cref="RealCacheLocator"/>. Set
    ///     <c>FLASHEDITOR_TEST_CACHE_FULL=1</c> to examine every archive rather than a sample.
    ///     </para>
    /// </remarks>
    public class RealCacheConformanceTests : IClassFixture<RealCacheFixture>
    {
        private readonly RealCacheFixture _cache;
        private readonly ITestOutputHelper _output;

        /// <summary>Failures reported per test before the list is truncated.</summary>
        private const int MaxReportedFailures = 10;

        /// <summary>Binds the shared open cache and the per-test output sink.</summary>
        public RealCacheConformanceTests(RealCacheFixture cache, ITestOutputHelper output)
        {
            _cache = cache;
            _output = output;
        }

        // ===================================================================
        //  Reference tables
        // ===================================================================

        /// <summary>
        ///     Every reference table in the cache must re-encode to the bytes it was decoded
        ///     from. The table is re-encoded on every edit, so any field the codec drops,
        ///     reorders or resizes is written back to disk the first time the user saves.
        /// </summary>
        /// <remarks>
        ///     Trailing zero bytes are the one permitted difference. Four indexes in the
        ///     reference cache carry a zero-filled block of four bytes per file after the last
        ///     real field, with the identifiers flag clear - the signature of a repacker that
        ///     emitted the per-file identifier block unconditionally. Nothing reads it and it
        ///     carries no information, but a re-encode that dropped a field with a *value* would
        ///     still be caught: only zero padding is tolerated, and only after the final field.
        /// </remarks>
        [RealCacheFact]
        public void ReferenceTables_ReEncodeToTheCapturedBytes()
        {
            var failures = new List<string>();
            int exact = 0;
            int padded = 0;
            long paddingBytes = 0;

            foreach (int indexId in _cache.TableIndexes)
            {
                byte[] captured = _cache.TablePayload(indexId);
                byte[] reencoded = ReferenceTableCodec.Encode(_cache.Table(indexId)).ToArray();

                if (reencoded.Length > captured.Length)
                {
                    failures.Add($"index {indexId}: re-encoded {reencoded.Length} bytes from a captured {captured.Length}");
                    continue;
                }

                if (!captured.AsSpan(0, reencoded.Length).SequenceEqual(reencoded))
                {
                    failures.Add($"index {indexId}: re-encoded bytes differ at offset {FirstDifference(captured, reencoded)}");
                    continue;
                }

                int surplus = captured.Length - reencoded.Length;
                if (surplus == 0)
                {
                    exact++;
                    continue;
                }

                if (captured.AsSpan(reencoded.Length).IndexOfAnyExcept((byte)0) >= 0)
                {
                    failures.Add($"index {indexId}: dropped {surplus} trailing bytes that are not all zero");
                    continue;
                }

                padded++;
                paddingBytes += surplus;
            }

            _output.WriteLine($"{_cache.TableIndexes.Count} reference tables: {exact} byte-identical, " +
                              $"{padded} identical but for {paddingBytes} bytes of trailing zero padding");

            AssertNoFailures(failures, "reference tables did not re-encode to their captured bytes");
        }

        // ===================================================================
        //  Containers
        // ===================================================================

        /// <summary>
        ///     The CRC each reference table carries per archive is checked against the stored
        ///     container bytes. Nothing in this project produced those CRCs, so they are an
        ///     independent statement of which span the checksum covers: the whole stored
        ///     container minus its version trailer. The write path recomputes both that CRC and
        ///     the <c>FLAG_SIZES</c> compressed size over the same span, so a wrong span would
        ///     put every rewritten archive out of step with the client that verifies it.
        /// </summary>
        [RealCacheFact]
        public void ArchiveCrcs_MatchTheCapturedContainerBytes()
        {
            var failures = new List<string>();
            int checkedArchives = 0;
            int examinedIndexes = 0;
            var trailerLengths = new SortedSet<int>();

            foreach (int indexId in _cache.TableIndexes)
            {
                RSReferenceTable table = _cache.Table(indexId);
                if (table.GetArchiveCount() == 0)
                    continue;
                examinedIndexes++;

                foreach (int archiveId in _cache.ArchivesToExamine(table))
                {
                    byte[] stored = _cache.RawContainer(indexId, archiveId);
                    if (stored == null)
                        continue;

                    int trailer = VersionTrailerLength(stored);
                    trailerLengths.Add(trailer);

                    uint actual = CRC32Helper.ComputeCrc32(stored.AsSpan(0, stored.Length - trailer));
                    int expected = table.GetArchiveEntry(archiveId).GetCrc();

                    checkedArchives++;
                    if (unchecked((int)actual) != expected)
                    {
                        failures.Add($"index {indexId} archive {archiveId}: crc {actual:X8} " +
                                     $"over {stored.Length - trailer} of {stored.Length} bytes, table says {expected:X8}");
                    }
                }
            }

            _output.WriteLine($"{checkedArchives} archives across {examinedIndexes} indexes; " +
                              $"version trailer lengths seen: {string.Join(", ", trailerLengths)}");
            ReportSampling();

            AssertNoFailures(failures, "archive CRCs did not match the captured container bytes");
        }

        /// <summary>
        ///     A container must survive decode and re-encode with its payload, version and
        ///     compression type intact. The encoded bytes themselves are not compared: encoding
        ///     recompresses, and neither GZip nor BZip2 promises to reproduce a third party's
        ///     output bit for bit. What has to hold is that nothing about the container's meaning
        ///     changes, because the write path re-encodes every container it touches.
        /// </summary>
        [RealCacheFact]
        public void Containers_PreserveTheirPayloadAndHeaderAcrossReEncode()
        {
            var failures = new List<string>();
            int checkedContainers = 0;
            int encrypted = 0;

            foreach (int indexId in _cache.TableIndexes)
            {
                RSReferenceTable table = _cache.Table(indexId);
                foreach (int archiveId in _cache.ArchivesToExamine(table))
                {
                    byte[] stored = _cache.RawContainer(indexId, archiveId);
                    if (stored == null)
                        continue;

                    RSContainer original = Decode(indexId, archiveId, stored, failures, ref encrypted);
                    if (original == null)
                        continue;

                    RSContainer roundTripped = RSContainer.Decode(new JagStream(original.Encode().ToArray()));
                    checkedContainers++;

                    if (roundTripped.GetCompressionType() != original.GetCompressionType())
                        failures.Add($"index {indexId} archive {archiveId}: compression {original.GetCompressionType()} became {roundTripped.GetCompressionType()}");
                    else if (roundTripped.GetVersion() != original.GetVersion())
                        failures.Add($"index {indexId} archive {archiveId}: version {original.GetVersion()} became {roundTripped.GetVersion()}");
                    else if (!roundTripped.GetStream().ToArray().SequenceEqual(original.GetStream().ToArray()))
                        failures.Add($"index {indexId} archive {archiveId}: payload changed across re-encode");
                }
            }

            _output.WriteLine($"{checkedContainers} containers round-tripped, {encrypted} skipped as encrypted");
            ReportSampling();

            AssertNoFailures(failures, "containers did not survive a re-encode intact");
        }

        // ===================================================================
        //  Archives
        // ===================================================================

        /// <summary>
        ///     An archive decoded from captured bytes must re-encode to exactly those bytes.
        ///     This is the assertion the round-trip tests cannot make: it compares against a
        ///     payload the client shipped, so it pins the file layout, the per-chunk size table
        ///     and the trailer rules to what the format actually is rather than to what this
        ///     encoder happens to emit.
        /// </summary>
        [RealCacheFact]
        public void Archives_ReEncodeToTheCapturedPayloadBytes()
        {
            var failures = new List<string>();
            int single = 0;
            int multi = 0;
            int encrypted = 0;
            var chunkCounts = new SortedSet<int>();

            foreach (int indexId in _cache.TableIndexes)
            {
                RSReferenceTable table = _cache.Table(indexId);
                foreach (int archiveId in _cache.ArchivesToExamine(table))
                {
                    byte[] stored = _cache.RawContainer(indexId, archiveId);
                    if (stored == null)
                        continue;

                    int[] fileIds = table.GetArchiveEntry(archiveId).GetValidFileIds();
                    if (fileIds.Length == 0)
                        continue;

                    RSContainer container = Decode(indexId, archiveId, stored, failures, ref encrypted);
                    if (container == null)
                        continue;

                    byte[] payload = container.GetStream().ToArray();
                    RSArchive archive = RSArchive.Decode(new JagStream(payload), fileIds);
                    byte[] reencoded = archive.Encode().ToArray();

                    if (fileIds.Length == 1)
                        single++;
                    else
                    {
                        multi++;
                        chunkCounts.Add(archive.chunks);
                    }

                    if (!reencoded.SequenceEqual(payload))
                    {
                        failures.Add($"index {indexId} archive {archiveId}: {fileIds.Length} files, " +
                                     $"{archive.chunks} chunk(s), re-encoded {reencoded.Length} bytes from a captured " +
                                     $"{payload.Length}, first difference at {FirstDifference(payload, reencoded)}");
                    }
                }
            }

            _output.WriteLine($"{single} single-file and {multi} multi-file archives, {encrypted} skipped as encrypted; " +
                              $"chunk counts seen in multi-file archives: {string.Join(", ", chunkCounts)}");
            ReportSampling();

            AssertNoFailures(failures, "archives did not re-encode to their captured payload bytes");
        }

        /// <summary>
        ///     The same claim, but made through the edit path rather than around it: hand every
        ///     file in an archive back the bytes it already holds, re-encode, and the payload has
        ///     to come out identical. That equality is the whole basis on which
        ///     <see cref="RSCache.WriteFile"/> leaves an unchanged archive's stored container -
        ///     and the CRC taken over it - alone, so every archive where it fails is one whose
        ///     no-op save still re-compresses and still moves its reference-table entry.
        /// </summary>
        /// <remarks>
        ///     <see cref="Archives_ReEncodeToTheCapturedPayloadBytes"/> exercises the decoder
        ///     against the encoder with nothing in between, so it says nothing about
        ///     <see cref="RSArchive.PutFile"/> - which used to discard the chunk split on every
        ///     call. Re-laying a chunk-major payload out as a single chunk produces exactly the
        ///     same length with the bytes in a different order, so that round trip stayed green
        ///     while most multi-file archives in this cache were still rewritten on save.
        ///     <para>
        ///     Single-file archives are skipped rather than swept. They have no size table, no
        ///     chunk count and so no split to lose - the payload simply is the file, and
        ///     <see cref="SingleFileArchives_CarryNoTrailerInTheCapturedBytes"/> establishes that
        ///     separately. Decoding the ninety-odd thousand of them a third time to re-derive that
        ///     costs more memory than the whole suite has to spare.
        ///     </para>
        /// </remarks>
        [RealCacheFact]
        public void UnchangedArchives_SurviveTheEditPathWithTheirPayloadIntact()
        {
            var failures = new List<string>();
            int multiFile = 0;
            int multiChunk = 0;
            int encrypted = 0;

            foreach (int indexId in _cache.TableIndexes)
            {
                RSReferenceTable table = _cache.Table(indexId);
                foreach (int archiveId in _cache.ArchivesToExamine(table))
                {
                    int[] fileIds = table.GetArchiveEntry(archiveId).GetValidFileIds();
                    if (fileIds.Length < 2)
                        continue;

                    byte[] stored = _cache.RawContainer(indexId, archiveId);
                    if (stored == null)
                        continue;

                    RSContainer container = Decode(indexId, archiveId, stored, failures, ref encrypted);
                    if (container == null)
                        continue;

                    byte[] payload = container.GetStream().ToArray();
                    RSArchive archive = RSArchive.Decode(new JagStream(payload), fileIds);

                    //What an editor does on a save with no edit: every file written back as it
                    //was, as a fresh stream rather than the instance the archive already holds
                    foreach (int fileId in fileIds)
                        archive.PutFile(fileId, new JagStream(archive.GetFile(fileId).ToArray()));

                    byte[] reencoded = archive.Encode().ToArray();

                    multiFile++;
                    if (archive.chunks > 1)
                        multiChunk++;

                    if (!reencoded.SequenceEqual(payload))
                    {
                        failures.Add($"index {indexId} archive {archiveId}: {fileIds.Length} files, " +
                                     $"{archive.chunks} chunk(s), a no-op edit re-encoded {reencoded.Length} bytes from a " +
                                     $"captured {payload.Length}, first difference at {FirstDifference(payload, reencoded)}");
                    }
                }
            }

            _output.WriteLine($"{multiFile} multi-file archives survived a no-op edit, {multiChunk} of them " +
                              $"multi-chunk; {encrypted} skipped as encrypted");
            ReportSampling();

            Assert.True(multiChunk > 0,
                "no multi-chunk archive was examined, so the case this guards was never exercised");
            AssertNoFailures(failures, "archives did not survive a no-op edit with their payload intact");
        }

        /// <summary>
        ///     The single-file rule holds that such an archive carries no trailer at all: no size
        ///     table and no chunk-count byte, the whole payload being the file. That rule was
        ///     argued from the client's unpacker rather than demonstrated, and it matters -
        ///     writing a trailer that should not be there grows the file by five bytes on every
        ///     save.
        /// </summary>
        /// <remarks>
        ///     A payload that genuinely ended in a one-file trailer would have a final byte of 1
        ///     and, four bytes before it, an int equal to the remaining length. Finding that
        ///     nowhere across the cache's single-file archives is positive evidence for the
        ///     special case, not merely an absence of evidence against it.
        /// </remarks>
        [RealCacheFact]
        public void SingleFileArchives_CarryNoTrailerInTheCapturedBytes()
        {
            var trailerLike = new List<string>();
            var failures = new List<string>();
            int examined = 0;
            int encrypted = 0;

            foreach (int indexId in _cache.TableIndexes)
            {
                RSReferenceTable table = _cache.Table(indexId);
                foreach (int archiveId in _cache.ArchivesToExamine(table))
                {
                    if (table.GetArchiveEntry(archiveId).GetValidFileIds().Length != 1)
                        continue;

                    byte[] stored = _cache.RawContainer(indexId, archiveId);
                    if (stored == null)
                        continue;

                    RSContainer container = Decode(indexId, archiveId, stored, failures, ref encrypted);
                    if (container == null)
                        continue;

                    byte[] payload = container.GetStream().ToArray();
                    if (payload.Length < 5)
                        continue;

                    examined++;

                    //What a one-file trailer would look like if the payload carried one
                    if (payload[payload.Length - 1] != 1)
                        continue;
                    int declared = ReadInt(payload, payload.Length - 5);
                    if (declared == payload.Length - 5)
                        trailerLike.Add($"index {indexId} archive {archiveId}");
                }
            }

            _output.WriteLine($"{examined} single-file archives examined, {encrypted} skipped as encrypted, " +
                              $"{trailerLike.Count} parse as a one-file trailer");
            ReportSampling();

            Assert.True(examined > 0, "no single-file archives were examined, so the rule was not exercised");
            AssertNoFailures(failures, "single-file archive containers would not decode");
            AssertNoFailures(trailerLike,
                "single-file archives whose payload parses as a one-file trailer - the no-trailer rule may be wrong");
        }

        // ===================================================================
        //  XTEA
        // ===================================================================

        /// <summary>
        ///     Every encrypted map archive for which a key is held must decrypt to a payload that
        ///     decompresses to its declared length. The keys are not ours - they come from a key
        ///     dump for this build - so this is an end-to-end check of the container's encrypted
        ///     span, the cipher, and the key table's lookup, against data none of them produced.
        /// </summary>
        /// <remarks>
        ///     Skips when no key file sits beside the cache, since there is then nothing to
        ///     check. Archives with no key are counted and reported rather than ignored: a key
        ///     dump is never complete, and a silent zero would look identical to success.
        /// </remarks>
        [RealCacheFact]
        public void EncryptedMapArchives_DecryptWithTheKeysForThisBuild()
        {
            RSReferenceTable table = _cache.Table(RSConstants.MAPS_INDEX);
            var failures = new List<string>();
            int plaintext = 0;
            int decrypted = 0;
            int noKey = 0;

            foreach (int archiveId in _cache.ArchivesToExamine(table))
            {
                byte[] stored = _cache.RawContainer(RSConstants.MAPS_INDEX, archiveId);
                if (stored == null)
                    continue;

                if (!_cache.IsEncrypted(RSConstants.MAPS_INDEX, archiveId, stored))
                {
                    plaintext++;
                    continue;
                }

                if (_cache.KeyFor(RSConstants.MAPS_INDEX, archiveId) == null)
                {
                    noKey++;
                    continue;
                }

                if (_cache.TryDecodeContainer(RSConstants.MAPS_INDEX, archiveId, stored) != null)
                    decrypted++;
                else
                    failures.Add($"archive {archiveId}: held a key but the payload would not decrypt");
            }

            _output.WriteLine($"map index: {plaintext} plaintext, {decrypted} decrypted, {noKey} with no key held");
            ReportSampling();

            Assert.True(decrypted > 0,
                "no map archive was decrypted, so the XTEA path was never exercised - is a key file present?");
            AssertNoFailures(failures, "map archives failed to decrypt with the key held for them");
        }

        // ===================================================================
        //  Index records
        // ===================================================================

        /// <summary>
        ///     Each six byte index record is a length and a first-sector pointer, both three byte
        ///     big-endian mediums. Reading and writing them must agree, because the write path
        ///     rewrites the record for every archive it touches.
        /// </summary>
        [RealCacheFact]
        public void IndexRecords_ReEncodeToTheCapturedBytes()
        {
            var failures = new List<string>();
            int examined = 0;

            foreach (int indexId in _cache.TableIndexes.Concat(new[] { RSConstants.META_INDEX }))
            {
                for (int archiveId = 0; archiveId < _cache.RecordCount(indexId); archiveId++)
                {
                    byte[] captured = _cache.RawIndexRecord(indexId, archiveId);
                    byte[] reencoded = _cache.ReEncodedIndexRecord(indexId, archiveId);
                    examined++;

                    if (!reencoded.SequenceEqual(captured))
                        failures.Add($"index {indexId} archive {archiveId}: {BitConverter.ToString(captured)} became {BitConverter.ToString(reencoded)}");
                }
            }

            _output.WriteLine($"{examined} index records re-encoded");
            AssertNoFailures(failures, "index records did not re-encode to their captured bytes");
        }

        // ===================================================================
        //  Helpers
        // ===================================================================

        /// <summary>
        ///     Decodes a captured container, tolerating the encrypted part of the map index and
        ///     nothing else.
        /// </summary>
        /// <remarks>
        ///     Roughly an eighth of the map archives in the reference cache are XTEA encrypted
        ///     and no key table ships alongside it, so their payloads cannot be decompressed.
        ///     Skipping them silently everywhere would let a genuine decode regression hide as a
        ///     skip, so anything that fails to decode outside the map index is a failure.
        /// </remarks>
        /// <param name="unreadable">Running count of map archives held back for want of a key.</param>
        /// <returns>The decoded container, or <c>null</c> when it was skipped or failed.</returns>
        private RSContainer Decode(int indexId, int archiveId, byte[] stored,
                                   List<string> failures, ref int unreadable)
        {
            RSContainer container = _cache.TryDecodeContainer(indexId, archiveId, stored);
            if (container != null)
                return container;

            /* A map archive with no key cannot be read by anyone and is not a defect. One that
               fails *with* a key in hand is, and so is any failure outside the map index. */
            if (indexId == RSConstants.MAPS_INDEX && _cache.KeyFor(indexId, archiveId) == null)
                unreadable++;
            else
                failures.Add($"index {indexId} archive {archiveId}: container payload would not decode");

            return null;
        }

        /// <summary>
        ///     Returns the length of a stored container's version trailer, which
        ///     <see cref="RSContainer.Decode"/> treats as present when two bytes remain after the
        ///     payload.
        /// </summary>
        private static int VersionTrailerLength(byte[] stored)
        {
            int headerLength = stored[0] == RSConstants.NO_COMPRESSION ? 5 : 9;
            int compressedLength = ReadInt(stored, 1);
            return stored.Length - (headerLength + compressedLength);
        }

        private static int ReadInt(byte[] data, int offset)
        {
            return (data[offset] << 24) | (data[offset + 1] << 16) | (data[offset + 2] << 8) | data[offset + 3];
        }

        private static int FirstDifference(byte[] expected, byte[] actual)
        {
            int shared = Math.Min(expected.Length, actual.Length);
            for (int i = 0; i < shared; i++)
                if (expected[i] != actual[i])
                    return i;
            return shared;
        }

        private void ReportSampling()
        {
            if (!_cache.FullSweep)
            {
                _output.WriteLine($"sampled up to {RealCacheFixture.SampleArchivesPerIndex} archives per index; " +
                                  $"set {RealCacheLocator.FullSweepVariable}=1 to examine every archive");
            }
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
