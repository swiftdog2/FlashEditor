using System.Collections.Generic;
using System.Linq;
using FlashEditor.cache;
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
        ///     Index 2 is the sharpest case available: it declares 49 groups but its ids are not
        ///     contiguous, so a parser that walked 0..count-1 would read the wrong groups and
        ///     still produce a plausible-looking count. Asserting the ids themselves is what tells
        ///     the two apart.
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
        ///     The two indexes whose idx holds groups their reference table does not declare.
        /// </summary>
        /// <remarks>
        ///     This is the assertion that makes the API's table-driven choice honest rather than
        ///     accidental. These are the only indexes where the idx-driven and table-driven
        ///     readings differ, so they are the only ones that can catch an enumeration which
        ///     silently dropped the difference or silently included it.
        ///
        ///     A group in the idx but not the table is unreachable in game: the client gates every
        ///     read on the table. So it must not appear in <c>EnumerateGroups</c>, and it must not
        ///     vanish without trace either.
        ///
        ///     The exact ids are asserted because they are a property of the cache, which does not
        ///     change. Four indexes carry orphans, not the two that were first reported: indexes 3
        ///     and 32 were missed, and both are on the worklist, so an implementer told "only 4 and
        ///     12 differ" would have sized index 3's group count wrongly.
        /// </remarks>
        [RealCacheFact]
        public void EnumerateOrphanGroups_FindsTheGroupsMissingFromTheirTable()
        {
            RSCache cache = _cache.OpenCache();

            var orphansByIndex = new SortedDictionary<int, IReadOnlyList<int>>();
            foreach (int indexId in _cache.TableIndexes)
            {
                IReadOnlyList<int> orphans = cache.EnumerateOrphanGroups(indexId);
                if (orphans.Count > 0)
                    orphansByIndex[indexId] = orphans;
            }

            foreach ((int indexId, IReadOnlyList<int> orphans) in orphansByIndex)
                _output.WriteLine($"index {indexId}: {orphans.Count} orphan group(s) [{string.Join(", ", orphans)}]");

            //An orphan is never enumerated, because the client cannot reach it either.
            foreach ((int indexId, IReadOnlyList<int> orphans) in orphansByIndex)
            {
                var enumerated = new HashSet<int>(cache.EnumerateGroups(indexId));
                foreach (int orphan in orphans)
                    Assert.DoesNotContain(orphan, enumerated);
            }

            Assert.Equal(
                new[]
                {
                    RSConstants.INTERFACE_DEFINITIONS_INDEX,
                    RSConstants.SOUND_EFFECTS,
                    RSConstants.CLIENT_SCRIPTS_INDEX,
                    RSConstants.LOADING_SPRITES
                },
                orphansByIndex.Keys.ToArray());

            Assert.Equal(new[] { 772, 825, 891 }, orphansByIndex[RSConstants.INTERFACE_DEFINITIONS_INDEX].ToArray());
            Assert.Equal(new[] { 4787 }, orphansByIndex[RSConstants.SOUND_EFFECTS].ToArray());
            Assert.Equal(new[] { 699, 700 }, orphansByIndex[RSConstants.CLIENT_SCRIPTS_INDEX].ToArray());
            Assert.Equal(new[] { 498, 1407 }, orphansByIndex[RSConstants.LOADING_SPRITES].ToArray());
        }
    }
}
