using FlashEditor.cache;
using FlashEditor.Cache.Region;
using FlashEditor.Cache.Util;
using FlashEditor.Cache.Util.Crypto;
using FlashEditor.Tests.Cache.RealCache;
using System;
using System.Collections.Generic;
using System.IO;
using Xunit;
using Xunit.Abstractions;

namespace FlashEditor.Tests.Cache
{
    /// <summary>
    ///     Measures how much of the encrypted map actually opens with the shipped XTEA keys.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///     <see cref="RealCacheMapDecodeTests.EveryLocationFileDecodesOrReportsAMissingKey"/> sweeps
    ///     the same 1684 groups but asserts only <c>loaded + missingKey == 1684</c>, which counts a
    ///     square that failed to decrypt exactly the same as one that succeeded. A cache whose keys
    ///     had all stopped working would pass it, because every square would simply land in
    ///     <see cref="LocationLoadResult.MissingKey"/> instead. Nothing else in the suite closes
    ///     that hole, so a key regression - a broken key file, a wrong dialect in
    ///     <c>XTEAKeyTable</c>, a cipher change - is invisible to the merge gate.
    ///     </para>
    ///     <para>
    ///     The assertion below is the one that cannot be satisfied by giving up: a group the key
    ///     table has a key for, and which does not open without it, <b>must</b> open with it.
    ///     Squares with no published key are excluded rather than tolerated, so they cannot absorb
    ///     a real failure the way the existing count does.
    ///     </para>
    /// </remarks>
    [Collection("RealCache")]
    public sealed class RealCacheXteaCoverageTests : IClassFixture<RealCacheFixture>
    {
        private readonly RealCacheFixture _fixture;
        private readonly ITestOutputHelper _output;

        /// <summary>Failures listed before the report is truncated.</summary>
        private const int MaxReportedFailures = 20;

        public RealCacheXteaCoverageTests(RealCacheFixture fixture, ITestOutputHelper output)
        {
            _fixture = fixture;
            _output = output;
        }

        /// <summary>
        ///     Every encrypted location group the key table covers decrypts to a decodable file.
        /// </summary>
        /// <remarks>
        ///     Holding a key and failing to open the group is the defect this exists to catch. It
        ///     means either the key is wrong for this cache or the decrypt path is, and both are
        ///     silent everywhere else: the loader reports the same
        ///     <see cref="LocationLoadResult.MissingKey"/> it reports for a square nobody ever
        ///     published a key for.
        /// </remarks>
        [RealCacheFact]
        public void EveryEncryptedLocationGroupWithAKeyDecrypts()
        {
            MapSquareLoader loader = new MapSquareLoader(_fixture.OpenCache());
            RSReferenceTable table = _fixture.Table(RSConstants.MAPS_INDEX);

            int total = 0;
            int encrypted = 0;
            int encryptedWithKey = 0;
            int decrypted = 0;
            int plaintext = 0;
            int noKeyPublished = 0;
            var failures = new List<string>();

            foreach ((int rx, int ry) in EveryLocationSquare(table))
            {
                total++;

                int group = table.GetArchiveId(MapSquareNames.Locations(rx, ry));
                byte[] stored = _fixture.RawContainer(RSConstants.MAPS_INDEX, group);
                if (stored == null)
                    continue;

                bool isEncrypted = _fixture.IsEncrypted(RSConstants.MAPS_INDEX, group, stored);
                bool hasKey = _fixture.KeyFor(RSConstants.MAPS_INDEX, group) != null;

                if (!isEncrypted)
                {
                    plaintext++;
                    continue;
                }

                encrypted++;

                if (!hasKey)
                {
                    //No key was ever published for this square. The client renders it with no
                    //objects and so do we, so it is not a failure - but it must not be counted
                    //as a success either, which is exactly what the existing sweep does.
                    noKeyPublished++;
                    continue;
                }

                encryptedWithKey++;

                LocationLoadResult result;
                try
                {
                    loader.Load(rx, ry, out result);
                }
                catch (Exception ex)
                {
                    failures.Add($"l{rx}_{ry} (group {group}): threw {ex.GetType().Name}: {ex.Message}");
                    continue;
                }

                if (result == LocationLoadResult.Loaded)
                    decrypted++;
                else
                    failures.Add($"l{rx}_{ry} (group {group}): has a key but reported {result}");
            }

            _output.WriteLine($"l groups                     : {total}");
            _output.WriteLine($"  plaintext                  : {plaintext}");
            _output.WriteLine($"  encrypted                  : {encrypted}");
            _output.WriteLine($"    with a published key     : {encryptedWithKey}");
            _output.WriteLine($"      decrypted successfully : {decrypted}");
            _output.WriteLine($"    no key in the key file   : {noKeyPublished}");

            AssertNoFailures(failures);

            //A run where nothing was encrypted proves nothing about the keys, and would let a
            //key file that had silently stopped loading pass as though it were fine.
            Assert.True(encryptedWithKey > 0,
                "no encrypted location group had a key, so this run tested no decryption at all");
            Assert.Equal(encryptedWithKey, decrypted);
        }

        /// <summary>
        ///     The key table is loaded, from a file beside the cache the suite actually opened.
        /// </summary>
        /// <remarks>
        ///     Split from the sweep above so that "the key file did not load at all" reads as its
        ///     own failure rather than as a thousand identical decrypt failures.
        ///     <para>
        ///     The path is named because the two supported caches keep their keys in different
        ///     places - the repack under <c>xteas/xteas.json</c> a level above the cache, the
        ///     OpenRS2 capture as <c>keys.json</c> beside it - and
        ///     <see cref="XTEAKeyTable.FindKeyFile"/> probes <c>xteas.json</c> before
        ///     <c>keys.json</c> at each root. So a file dropped beside a capture under the earlier
        ///     name would silently replace the shipped dump, and every square would then report a
        ///     missing key, which looks exactly like the keys being wrong. Asserting that the file
        ///     sits under the cache directory or its parent is what keeps that visible.
        ///     </para>
        /// </remarks>
        [RealCacheFact]
        public void TheKeyTableIsLoaded()
        {
            XTEAKeyTable keys = _fixture.OpenCache().GetXTEAKeyTable();

            _output.WriteLine($"cache   : {RealCacheLocator.Directory}");
            _output.WriteLine($"profile : {_fixture.Profile.Name}");
            _output.WriteLine($"key file: {_fixture.KeyFile ?? "(none found)"}");

            Assert.NotNull(keys);
            _output.WriteLine($"keys loaded: {keys.Count}");
            Assert.True(keys.Count > 0,
                "no XTEA keys were loaded - XTEAKeyTable.FindKeyFile only probes for xteas.json, " +
                "xtea.json, keys.json and xteakeys.json beside the cache or its parent");

            Assert.NotNull(_fixture.KeyFile);
            string cacheDir = Path.GetFullPath(RealCacheLocator.Directory);
            string keyRoot = Path.GetFullPath(_fixture.KeyFile);
            Assert.True(keyRoot.StartsWith(cacheDir, StringComparison.OrdinalIgnoreCase) ||
                        keyRoot.StartsWith(Path.GetFullPath(Path.Combine(cacheDir, "..")),
                            StringComparison.OrdinalIgnoreCase),
                $"the keys came from {keyRoot}, which is neither inside {cacheDir} nor beside it");
        }

        private static IEnumerable<(int, int)> EveryLocationSquare(RSReferenceTable table)
        {
            for (int rx = 0; rx < 256; rx++)
                for (int ry = 0; ry < 256; ry++)
                    if (table.GetArchiveId(MapSquareNames.Locations(rx, ry)) != -1)
                        yield return (rx, ry);
        }

        private static void AssertNoFailures(List<string> failures)
        {
            if (failures.Count == 0)
                return;

            var reported = failures.Count > MaxReportedFailures
                ? failures.GetRange(0, MaxReportedFailures)
                : failures;
            string detail = string.Join(Environment.NewLine + "  ", reported);
            if (failures.Count > MaxReportedFailures)
                detail += $"{Environment.NewLine}  ... and {failures.Count - MaxReportedFailures} more";

            Assert.Fail($"{failures.Count} encrypted location groups hold a key that did not " +
                        $"decrypt them:{Environment.NewLine}  {detail}");
        }
    }
}
