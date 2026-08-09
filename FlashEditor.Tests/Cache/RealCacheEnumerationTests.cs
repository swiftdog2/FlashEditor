using System.Collections.Generic;
using System.IO;
using System.Linq;
using FlashEditor.Cache;
using FlashEditor.Tests.Cache.RealCache;
using Xunit;
using Xunit.Abstractions;

namespace FlashEditor.Tests.Cache
{
    /// <summary>
    ///     Pins the cache enumeration API against the real cache.
    /// </summary>
    /// <remarks>
    ///     The API exists so a tab loader can ask which files exist instead of walking 0..255 and
    ///     catching <c>FileNotFoundException</c> for the holes. That only pays off if it agrees
    ///     with the cache, so these assert against counts the cache itself fixes rather than
    ///     against a second implementation living here.
    /// </remarks>
    public sealed class RealCacheEnumerationTests : IClassFixture<RealCacheFixture>
    {
        private readonly RealCacheFixture _cache;
        private readonly ITestOutputHelper _output;

        public RealCacheEnumerationTests(RealCacheFixture cache, ITestOutputHelper output)
        {
            _cache = cache;
            _output = output;
        }

        /// <summary>
        ///     Enumeration follows the table's declared ids rather than a dense range.
        /// </summary>
        /// <remarks>
        ///     Index 2 is the sharpest case available: it declares 35 groups whose ids run up to
        ///     48 with holes throughout, so a parser that walked 0..count-1 would read the wrong
        ///     groups and still produce a plausible-looking count. Asserting the ids themselves is
        ///     what tells the two apart.
        /// </remarks>
        [RealCacheFact]
        public void EnumerateGroups_FollowsTheDeclaredIds_NotADenseRange()
        {
            RSCache cache = _cache.OpenCache();

            foreach (int indexId in _cache.TableIndexes)
            {
                var declared = new List<int>(_cache.Table(indexId).GetArchiveEntries().Keys);
                var enumerated = cache.EnumerateGroups(indexId).ToList();

                Assert.Equal(declared, enumerated);
            }
        }

        /// <summary>
        ///     Every enumerated pair names a file that actually reads.
        /// </summary>
        /// <remarks>
        ///     Sampled per index rather than swept: the point is that the pairs are addressable at
        ///     all, and the definition sweeps already read every file of the indexes that carry
        ///     definitions. Index 5 is excluded because its locations are XTEA encrypted and a
        ///     square with no published key cannot be read whatever the enumeration says.
        /// </remarks>
        [RealCacheFact]
        public void EnumerateFiles_NamesFilesThatRead()
        {
            RSCache cache = _cache.OpenCache();
            int checkedPairs = 0;

            foreach (int indexId in _cache.TableIndexes)
            {
                if (indexId == RSConstants.MAPS_INDEX || indexId == RSConstants.META_INDEX)
                    continue;

                foreach ((int group, int file) in cache.EnumerateFiles(indexId).Take(25))
                {
                    Assert.NotNull(cache.ReadFile(indexId, group, file));
                    checkedPairs++;
                }
            }

            _output.WriteLine($"read {checkedPairs} enumerated (group, file) pairs");
            Assert.True(checkedPairs > 0, "no pair was enumerated, so nothing was checked");
        }

        /// <summary>
        ///     CountFiles agrees with what EnumerateFiles yields.
        /// </summary>
        /// <remarks>
        ///     They are separate code paths over the same table, so a disagreement means one of
        ///     them is reading the file-id block wrongly. Cheap to state and it costs nothing to
        ///     keep.
        /// </remarks>
        [RealCacheFact]
        public void CountFiles_AgreesWithEnumeration()
        {
            RSCache cache = _cache.OpenCache();

            foreach (int indexId in _cache.TableIndexes)
                Assert.Equal(cache.EnumerateFiles(indexId).Count(), cache.CountFiles(indexId));
        }

        /// <summary>
        ///     A group the idx file holds and the reference table does not declare is reported as
        ///     an orphan, on every index, and never enumerated.
        /// </summary>
        /// <remarks>
        ///     <para>
        ///     This is what makes the API's table-driven choice honest rather than accidental. A
        ///     group in the idx but not the table is unreachable in game - the client gates every
        ///     read on the table - so it must not appear in <c>EnumerateGroups</c>, and it must
        ///     not vanish without trace either.
        ///     </para>
        ///     <para>
        ///     The orphan set is derived here rather than written down, by walking every idx
        ///     record against the table's declared ids and applying the same liveness rule
        ///     <c>EnumerateOrphanGroups</c> does. That makes the assertion a comparison of two
        ///     independent readings of the cache, which holds on any cache and catches both
        ///     failure modes: an enumeration that dropped the difference and one that invented it.
        ///     It is also strictly stronger than the list of ids it replaces, which agreed with a
        ///     reader that happened to be wrong about a slot nobody had recorded.
        ///     </para>
        ///     <para>
        ///     Which ids those are is a fact about one cache, so it is scoped to the profile. The
        ///     repack carries eight orphans across four indexes, all of them repacking residue;
        ///     the vanilla b639 capture carries none at all, so on it the idx-driven and
        ///     table-driven readings agree everywhere. That is informative rather than a gap - the
        ///     derived comparison above is what carries the test there.
        ///     </para>
        /// </remarks>
        [RealCacheFact]
        public void EnumerateOrphanGroups_FindsTheGroupsMissingFromTheirTable()
        {
            RSCache cache = _cache.OpenCache();

            var orphansByIndex = new SortedDictionary<int, IReadOnlyList<int>>();
            int measuredIndexes = 0;

            foreach (int indexId in _cache.TableIndexes)
            {
                if (indexId == RSConstants.META_INDEX)
                    continue;

                measuredIndexes++;
                IReadOnlyList<int> orphans = cache.EnumerateOrphanGroups(indexId);

                //A second, independent reading: the idx records the table does not account for.
                Assert.Equal(UndeclaredLiveSlots(indexId), orphans);

                if (orphans.Count > 0)
                    orphansByIndex[indexId] = orphans;
            }

            foreach ((int indexId, IReadOnlyList<int> orphans) in orphansByIndex)
                _output.WriteLine($"index {indexId}: {orphans.Count} orphan group(s) [{string.Join(", ", orphans)}]");

            _output.WriteLine($"{_cache.Profile.Name}: {orphansByIndex.Count} of {measuredIndexes} " +
                              "indexes hold a group their table does not declare");
            Assert.True(measuredIndexes > 0, "no index was compared, so nothing was checked");

            //An orphan is never enumerated, because the client cannot reach it either.
            foreach ((int indexId, IReadOnlyList<int> orphans) in orphansByIndex)
            {
                var enumerated = new HashSet<int>(cache.EnumerateGroups(indexId));
                foreach (int orphan in orphans)
                    Assert.DoesNotContain(orphan, enumerated);
            }

            if (_cache.Profile.OrphanGroups == null)
                return;

            Assert.Equal(_cache.Profile.OrphanGroups.Keys.OrderBy(id => id).ToArray(),
                orphansByIndex.Keys.ToArray());
            foreach ((int indexId, int[] expected) in _cache.Profile.OrphanGroups)
                Assert.Equal(expected, orphansByIndex[indexId].ToArray());
        }

        /// <summary>
        ///     Ids whose idx record points at real data and whose reference table says nothing
        ///     about them.
        /// </summary>
        /// <remarks>
        ///     Read straight out of the six-byte idx records and the dat2's length rather than
        ///     through <see cref="RSCache.EnumerateOrphanGroups"/>, so the comparison above is
        ///     between two readings and not between a reader and itself. The liveness rule is the
        ///     production one: a positive size, and a first sector inside the data file.
        /// </remarks>
        /// <param name="indexId">The index to walk.</param>
        /// <returns>The undeclared live group ids, ascending.</returns>
        private IReadOnlyList<int> UndeclaredLiveSlots(int indexId)
        {
            var declared = new HashSet<int>(_cache.Table(indexId).GetArchiveEntries().Keys);
            long sectorLimit = new FileInfo(Path.Combine(RealCacheLocator.Directory, "main_file_cache.dat2"))
                .Length / RSSector.SIZE;

            var undeclared = new List<int>();
            int slots = _cache.RecordCount(indexId);

            for (int groupId = 0; groupId < slots; groupId++)
            {
                if (declared.Contains(groupId))
                    continue;

                byte[] record = _cache.RawIndexRecord(indexId, groupId);
                int size = (record[0] << 16) | (record[1] << 8) | record[2];
                int sector = (record[3] << 16) | (record[4] << 8) | record[5];

                if (size > 0 && sector > 0 && sector < sectorLimit)
                    undeclared.Add(groupId);
            }

            return undeclared;
        }
    }
}
