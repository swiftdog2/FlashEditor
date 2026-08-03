using System.Collections.Generic;
using System.Linq;
using FlashEditor;
using FlashEditor.cache;
using FlashEditor.Tests.Cache.RealCache;
using Xunit;
using Xunit.Abstractions;

namespace FlashEditor.Tests.Cache
{
    /// <summary>
    ///     Pins <see cref="RSCache.ReadGroup"/> against the real cache.
    /// </summary>
    /// <remarks>
    ///     <c>ReadGroup</c> exists because <see cref="RSCache.ReadFile"/> releases the container as
    ///     soon as it has handed back one file, so a tab that walks a group file by file re-reads
    ///     and re-inflates that group once per file it holds. The whole point of the faster path is
    ///     that it returns the same bytes, so that is what is asserted - against <c>ReadFile</c>
    ///     itself rather than against a second reader living here.
    /// </remarks>
    public sealed class RealCacheReadGroupTests : IClassFixture<RealCacheFixture>
    {
        private readonly RealCacheFixture _cache;
        private readonly ITestOutputHelper _output;

        public RealCacheReadGroupTests(RealCacheFixture cache, ITestOutputHelper output)
        {
            _cache = cache;
            _output = output;
        }

        /// <summary>
        ///     A group read whole holds byte for byte what the same files read one at a time do.
        /// </summary>
        /// <remarks>
        ///     Sampled per index rather than swept: the two paths share their decoder, so what is
        ///     being tested is that the batched one drives it over the same file id list and does not
        ///     drop, reorder or truncate anything. Index 5 is excluded because a square whose XTEA
        ///     key is not published cannot be read by either path, which would prove nothing.
        /// </remarks>
        [RealCacheFact]
        public void ReadGroup_ReturnsTheSameBytesAsReadFile()
        {
            RSCache cache = _cache.OpenCache();
            int comparedFiles = 0;
            int comparedGroups = 0;

            foreach (int indexId in _cache.TableIndexes)
            {
                if (indexId == RSConstants.MAPS_INDEX || indexId == RSConstants.META_INDEX)
                    continue;

                foreach (int groupId in cache.EnumerateGroups(indexId).Take(5))
                {
                    IReadOnlyDictionary<int, JagStream> whole = cache.ReadGroup(indexId, groupId);
                    comparedGroups++;

                    foreach (int fileId in cache.GetFileIds(indexId, groupId))
                    {
                        byte[] singly = cache.ReadFileBytes(indexId, groupId, fileId);

                        Assert.True(whole.ContainsKey(fileId),
                            $"index {indexId} group {groupId} file {fileId} is missing from the group read");
                        Assert.Equal(singly, whole[fileId].ToArray());
                        comparedFiles++;
                    }
                }
            }

            _output.WriteLine($"compared {comparedFiles} files across {comparedGroups} groups");
            Assert.True(comparedFiles > 0, "no file was compared, so nothing was checked");
        }

        /// <summary>
        ///     Every file the table declares for an index comes back from the group reads.
        /// </summary>
        /// <remarks>
        ///     Index 3 rather than a sample, because it is the index the definition list panel
        ///     loads and the one where the two readers can most easily disagree. A batched reader
        ///     that silently dropped the tail of a group would still pass a spot check; it cannot
        ///     pass this.
        ///     <para>
        ///     The totals come from the reference table rather than from a literal. The claim is
        ///     that the group reads yield every declared group and every declared file, which is a
        ///     relationship and true of any cache - the two supported caches hold 1078 groups of
        ///     42,256 files and 1067 of 40,883 respectively, and writing either down would turn a
        ///     property of the reader into a property of one cache.
        ///     </para>
        /// </remarks>
        [RealCacheFact]
        public void ReadGroup_YieldsEveryDeclaredFileOfIndex3()
        {
            RSCache cache = _cache.OpenCache();

            int groups = 0;
            int files = 0;
            long bytes = 0;

            foreach (int groupId in cache.EnumerateGroups(RSConstants.INTERFACE_DEFINITIONS_INDEX))
            {
                groups++;
                foreach (KeyValuePair<int, JagStream> file in
                         cache.ReadGroup(RSConstants.INTERFACE_DEFINITIONS_INDEX, groupId))
                {
                    files++;
                    bytes += file.Value.Length;
                }
            }

            int declaredGroups = _cache.DeclaredGroups(RSConstants.INTERFACE_DEFINITIONS_INDEX);
            int declaredFiles = _cache.DeclaredFiles(RSConstants.INTERFACE_DEFINITIONS_INDEX);
            _output.WriteLine($"index 3: {groups} groups, {files} files, {bytes} bytes");

            //An empty index would satisfy "read everything declared" without reading anything.
            Assert.True(declaredGroups > 0 && declaredFiles > 0,
                "index 3's reference table declares nothing, so this run read nothing");

            Assert.Equal(declaredGroups, groups);
            Assert.Equal(declaredFiles, files);
            Assert.Equal(cache.CountFiles(RSConstants.INTERFACE_DEFINITIONS_INDEX), files);
        }
    }
}
