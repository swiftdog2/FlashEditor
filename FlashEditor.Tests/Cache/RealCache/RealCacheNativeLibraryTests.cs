using System;
using System.Collections.Generic;
using System.Linq;
using FlashEditor.Cache;
using FlashEditor.Cache.Util;
using FlashEditor.Definitions.Natives;
using Xunit;
using Xunit.Abstractions;

namespace FlashEditor.Tests.Cache.RealCache
{
    /// <summary>
    ///     Index 30 against the shipped bytes: the recovered names, the payload classification, and
    ///     the whirlpool digests that live nowhere else in this cache.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///     Index 30 is the <b>only</b> table in either cache that sets the whirlpool flag, so it is
    ///     the sole real-world exercise of that branch of <see cref="ReferenceTableCodec"/> and of
    ///     the recompute in <c>RSCache.WriteFile</c>. Two claims are separable and both are worth
    ///     pinning: which span the digest covers, which is settled here against digests this project
    ///     did not produce, and whether a write recomputes it correctly, which needs a write and is
    ///     in <c>RealCacheWhirlpoolWriteTests</c>.
    ///     </para>
    ///     <para>
    ///     Nothing here writes. The real cache is opened read-only and stays that way.
    ///     </para>
    /// </remarks>
    public class RealCacheNativeLibraryTests : IClassFixture<RealCacheFixture>
    {
        private readonly RealCacheFixture _cache;
        private readonly ITestOutputHelper _output;

        /// <summary>Binds the shared open cache and the per-test output sink.</summary>
        /// <param name="cache">The shared cache fixture.</param>
        /// <param name="output">The test output sink.</param>
        public RealCacheNativeLibraryTests(RealCacheFixture cache, ITestOutputHelper output)
        {
            _cache = cache;
            _output = output;
        }

        private RSReferenceTable Table => _cache.Table(RSConstants.NATIVE_LIBRARIES);

        /// <summary>
        ///     Every declared group is named by the committed table, and every committed name is used.
        /// </summary>
        /// <remarks>
        ///     A bijection rather than a coverage figure. A near-total match is the easiest wrong
        ///     answer to reach in this cache - the track-name join scored 958 of 970 and was wrong -
        ///     so the assertion is that nothing is unmatched on <i>either</i> side, which a
        ///     plausible-but-wrong candidate list cannot satisfy.
        /// </remarks>
        [RealCacheFact]
        public void EveryGroupIsNamedByTheCommittedTableAndEveryNameIsUsed()
        {
            var byGroup = new SortedDictionary<int, string>();
            var unnamed = new List<int>();

            foreach (KeyValuePair<int, RSArchiveEntry> group in Table.GetArchiveEntries())
            {
                if (NativeLibraryNames.TryGetName(group.Value.GetIdentifier(), out string name))
                    byGroup[group.Key] = name;
                else
                    unnamed.Add(group.Key);
            }

            _output.WriteLine($"index 30: {byGroup.Count} of {Table.GetArchiveCount()} groups named");
            foreach (KeyValuePair<int, string> pair in byGroup)
                _output.WriteLine($"  {pair.Key,3} {pair.Value}");

            Assert.Empty(unnamed);
            Assert.Equal(_cache.DeclaredGroups(RSConstants.NATIVE_LIBRARIES), byGroup.Count);

            //Nothing left over on the other side either: a candidate that named no group would mean
            //the list has drifted from the cache and nothing else would say so.
            Assert.Equal(NativeLibraryNames.KnownNames.OrderBy(name => name, StringComparer.Ordinal).ToArray(),
                byGroup.Values.OrderBy(name => name, StringComparer.Ordinal).ToArray());
        }

        /// <summary>
        ///     The file inside every group is named the empty string, not the library filename.
        /// </summary>
        /// <remarks>
        ///     The client passes <c>""</c> explicitly (<c>Class35.java:102</c>), and this is what the
        ///     per-file name index has to get right: an implementation keyed by array position would
        ///     answer a lookup of <c>""</c> out of its own padding.
        /// </remarks>
        [RealCacheFact]
        public void EveryGroupHoldsOneFileNamedTheEmptyString()
        {
            RSCache cache = _cache.OpenCache();
            CacheNameIndex names = cache.GetNameIndex(RSConstants.NATIVE_LIBRARIES);

            var wrong = new List<string>();

            foreach (KeyValuePair<int, RSArchiveEntry> group in Table.GetArchiveEntries())
            {
                int[] files = group.Value.GetValidFileIds();
                if (files.Length != 1)
                {
                    wrong.Add($"group {group.Key} holds {files.Length} files");
                    continue;
                }

                if (group.Value.GetFileEntry(files[0]).GetIdentifier() != NameHasher.GetNameHash(""))
                    wrong.Add($"group {group.Key} file {files[0]} is not named the empty string");

                if (names.FileId(group.Key, "") != files[0])
                    wrong.Add($"group {group.Key} does not resolve \"\" to file {files[0]}");
            }

            Assert.Empty(wrong);
            Assert.Equal(_cache.DeclaredFiles(RSConstants.NATIVE_LIBRARIES), Table.GetArchiveCount());
        }

        /// <summary>
        ///     Reading by the two-part name the client uses returns the same bytes as reading by id.
        /// </summary>
        /// <remarks>
        ///     The point of the per-file name index. Before it, the file half of
        ///     <c>"windows/x86/jaggl.dll"/""</c> could not be resolved at all.
        /// </remarks>
        [RealCacheFact]
        public void ReadingByNameReturnsWhatReadingByIdReturns()
        {
            RSCache cache = _cache.OpenCache();
            int checkedGroups = 0;

            foreach (KeyValuePair<int, RSArchiveEntry> group in Table.GetArchiveEntries())
            {
                if (!NativeLibraryNames.TryGetName(group.Value.GetIdentifier(), out string name))
                    continue;

                int fileId = group.Value.GetValidFileIds().Single();
                byte[] byId = cache.ReadFileBytes(RSConstants.NATIVE_LIBRARIES, group.Key, fileId);
                byte[] byName = cache.ReadFileBytes(RSConstants.NATIVE_LIBRARIES, name, "");

                Assert.Equal(byId, byName);
                checkedGroups++;
            }

            Assert.Equal(_cache.DeclaredGroups(RSConstants.NATIVE_LIBRARIES), checkedGroups);
        }

        /// <summary>
        ///     Every payload is a recognised executable, and its header agrees with its name.
        /// </summary>
        /// <remarks>
        ///     The two are read independently - the name from the reference table, the architecture
        ///     from the binary's own COFF, ELF or Mach-O header - so their agreement is a measurement
        ///     rather than a restatement. A tab that derived one from the other could not make it.
        /// </remarks>
        [RealCacheFact]
        public void EveryPayloadIsARecognisedExecutableAndAgreesWithItsName()
        {
            RSCache cache = _cache.OpenCache();
            var failures = new List<string>();
            var formats = new SortedDictionary<string, int>();

            foreach (KeyValuePair<int, RSArchiveEntry> group in Table.GetArchiveEntries())
            {
                int fileId = group.Value.GetValidFileIds().Single();
                byte[] payload = cache.ReadFileBytes(RSConstants.NATIVE_LIBRARIES, group.Key, fileId);

                NativeBinaryShape shape = NativeBinaryShape.Of(payload);
                formats[shape.Format] = formats.TryGetValue(shape.Format, out int seen) ? seen + 1 : 1;

                if (shape.Kind == NativeBinaryKind.Unknown)
                {
                    failures.Add($"group {group.Key}: payload magic is {shape.Format}");
                    continue;
                }

                NativeLibraryNames.TryGetName(group.Value.GetIdentifier(), out string named);
                NativeLibraryName name = NativeLibraryName.Parse(named);
                if (!name.IsWellFormed || shape.Bits == 0)
                    continue;

                bool claims64 = name.Architecture.Contains("64", StringComparison.Ordinal);
                if (claims64 != (shape.Bits == 64))
                    failures.Add($"group {group.Key} is named {name.Path} and its header says {shape.Bits}-bit");
            }

            foreach (KeyValuePair<string, int> format in formats)
                _output.WriteLine($"{format.Value} {format.Key}");

            Assert.Empty(failures);
        }

        /// <summary>
        ///     Exactly the groups whose architecture token is the minority spelling are reported.
        /// </summary>
        /// <remarks>
        ///     The anomaly the survey found: one group is <c>windows/x64/jagmisc.dll</c> where the
        ///     other 64-bit Windows libraries are under <c>windows/x86_64/</c>. The 637 client only
        ///     ever emits <c>x86_64/</c> (<c>Class365.java:70-72</c>), so it asks for a name no group
        ///     carries and cannot load jagmisc on 64-bit Windows.
        ///     <para>
        ///     Asserted through the derived rule and then confirmed on the one case that is checkable
        ///     on its own - the name that is present and the name that is not. A count alone would
        ///     pass for the wrong reason if the rule fired on a different group.
        ///     </para>
        /// </remarks>
        [RealCacheFact]
        public void TheOddlyNamedGroupIsReportedAndNotCorrected()
        {
            RSCache cache = _cache.OpenCache();
            NativeLibraryCensus census = NativeLibraryCensus.Build(cache);

            foreach (int groupId in census.AnomalousGroups.OrderBy(id => id))
                _output.WriteLine($"group {groupId}: {census.AnomalyFor(groupId)}");

            //The name the cache stores resolves; the one the client asks for does not. Both halves
            //are needed - the second is what makes this a defect rather than a spelling preference.
            CacheNameIndex names = cache.GetNameIndex(RSConstants.NATIVE_LIBRARIES);
            int stored = names.GroupId("windows/x64/jagmisc.dll");
            Assert.True(stored >= 0, "windows/x64/jagmisc.dll is not in this cache");
            Assert.Equal(-1, names.GroupId("windows/x86_64/jagmisc.dll"));

            Assert.Contains(stored, census.AnomalousGroups);

            //And nothing else is flagged, so the rule is not sweeping up siblings that are fine.
            Assert.Equal(new[] { stored }, census.AnomalousGroups.ToArray());
        }

        /// <summary>
        ///     Every stored whirlpool digest is the hash of the container minus its version trailer.
        /// </summary>
        /// <remarks>
        ///     <para>
        ///     <b>Nothing in this project produced these digests</b>, so they are an independent
        ///     statement of which span the hash covers - the same role the CRC sweep plays for the
        ///     checksum. <c>RSCache.WriteFile</c> recomputes the digest over exactly that span on
        ///     every write, so a wrong span would put every rewritten archive out of step with a
        ///     client that verifies it, and index 30 is the only place in this cache where the
        ///     question can be asked at all.
        ///     </para>
        ///     <para>
        ///     The two negative checks are the point. A test that only confirmed the right span
        ///     passes just as well if the digest happens to match several spans, and the span that
        ///     would be reached for by mistake is the full stored bytes.
        ///     </para>
        /// </remarks>
        [RealCacheFact]
        public void EveryWhirlpoolDigestIsTheHashOfTheContainerWithoutItsVersionTrailer()
        {
            Assert.True(Table.usesWhirlpool, "index 30 does not carry whirlpool digests in this cache");

            var failures = new List<string>();
            int matchedTrimmed = 0;
            int matchedWhole = 0;
            int matchedPayload = 0;

            RSCache cache = _cache.OpenCache();

            foreach (KeyValuePair<int, RSArchiveEntry> group in Table.GetArchiveEntries())
            {
                byte[] stored = _cache.RawContainer(RSConstants.NATIVE_LIBRARIES, group.Key);
                if (stored == null)
                {
                    failures.Add($"group {group.Key} has no stored container");
                    continue;
                }

                byte[] digest = group.Value.GetWhirlpool();

                //The version trailer is two bytes and is outside the hashed span, exactly as it is
                //outside the CRC span.
                if (Whirlpool.ComputeHash(stored.AsSpan(0, stored.Length - 2).ToArray()).AsSpan().SequenceEqual(digest))
                    matchedTrimmed++;
                else
                    failures.Add($"group {group.Key}: the stored digest is not the hash of the container minus its trailer");

                if (Whirlpool.ComputeHash(stored).AsSpan().SequenceEqual(digest))
                    matchedWhole++;

                int fileId = group.Value.GetValidFileIds().Single();
                byte[] payload = cache.ReadFileBytes(RSConstants.NATIVE_LIBRARIES, group.Key, fileId);
                if (Whirlpool.ComputeHash(payload).AsSpan().SequenceEqual(digest))
                    matchedPayload++;
            }

            _output.WriteLine($"index 30: {matchedTrimmed} of {Table.GetArchiveCount()} digests match the " +
                              $"container minus its trailer, {matchedWhole} match the whole stored bytes, " +
                              $"{matchedPayload} match the decompressed payload");

            Assert.Empty(failures);
            Assert.Equal(_cache.DeclaredGroups(RSConstants.NATIVE_LIBRARIES), matchedTrimmed);

            //Neither of the two spans that would be reached for by mistake matches anything, so the
            //assertion above cannot be passing for the wrong reason.
            Assert.Equal(0, matchedWhole);
            Assert.Equal(0, matchedPayload);
        }

        /// <summary>
        ///     The whirlpool branch of the reference-table codec re-encodes to the captured bytes.
        /// </summary>
        /// <remarks>
        ///     The generic conformance sweep covers this index alongside every other, but this is the
        ///     only table in the cache whose payload reaches
        ///     <c>ReferenceTableCodec</c>'s 64-byte-per-group write at all - so a regression there
        ///     would show up as one failure among thirty-five with nothing saying which branch was
        ///     lost. Named separately for that reason, and it asserts the digests survive the round
        ///     trip individually as well as the table surviving it whole.
        /// </remarks>
        [RealCacheFact]
        public void TheWhirlpoolBranchOfTheTableCodecSurvivesADecodeAndReEncode()
        {
            byte[] captured = _cache.TablePayload(RSConstants.NATIVE_LIBRARIES);

            RSReferenceTable decoded = ReferenceTableCodec.Decode(new FlashEditor.IO.JagStream(captured));
            byte[] reencoded = ReferenceTableCodec.Encode(decoded).ToArray();

            Assert.Equal(captured, reencoded);

            //And the digests specifically, rather than only the table as a whole: a codec that wrote
            //64 zero bytes per group and read them back would still round-trip a table it had itself
            //produced, which is why this compares against the fixture's own decode.
            foreach (KeyValuePair<int, RSArchiveEntry> group in Table.GetArchiveEntries())
            {
                byte[] expected = group.Value.GetWhirlpool();
                Assert.Equal(expected, decoded.GetArchiveEntries()[group.Key].GetWhirlpool());
                Assert.Contains(expected, value => value != 0);
            }
        }
    }
}
