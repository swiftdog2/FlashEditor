using FlashEditor.Cache;
using FlashEditor.Cache.Region;
using FlashEditor.Cache.Util;
using FlashEditor.Cache.Util.Crypto;
using FlashEditor.IO;
using FlashEditor.Tests.Cache.RealCache;
using ICSharpCode.SharpZipLib.Zip.Compression;
using ICSharpCode.SharpZipLib.Zip.Compression.Streams;
using System;
using System.Collections.Generic;
using System.IO;
using Xunit;
using Xunit.Abstractions;

namespace FlashEditor.Tests.Cache.RealCache
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
        ///     Every encrypted location group re-enciphers to the ciphertext already on disk.
        /// </summary>
        /// <remarks>
        ///     <para>
        ///     The sweep above proves <see cref="XTEA.Decipher"/> against bytes this project did
        ///     not write. Nothing proved <see cref="XTEA.Encipher"/> against anything of the kind:
        ///     every other test of it runs this encipher against this decipher, and a pair that is
        ///     inverse and wrong round-trips perfectly while producing bytes no client can read.
        ///     That is the half the editor actually writes, so it is the half a defect ships in.
        ///     </para>
        ///     <para>
        ///     Deciphering first and requiring the encipher to land back on the stored bytes is
        ///     what makes the target third-party: the ciphertext compared against is the one the
        ///     cache arrived with. The intermediate is required to differ from it, so a cipher
        ///     that did nothing at all cannot satisfy both directions by standing still.
        ///     </para>
        /// </remarks>
        [RealCacheFact]
        public void EveryEncryptedLocationGroupReEnciphersToTheCipherTextOnDisk()
        {
            RSReferenceTable table = _fixture.Table(RSConstants.MAPS_INDEX);

            int reEnciphered = 0;
            var failures = new List<string>();

            foreach ((int rx, int ry) in EveryLocationSquare(table))
            {
                int group = table.GetArchiveId(MapSquareNames.Locations(rx, ry));
                byte[] stored = _fixture.RawContainer(RSConstants.MAPS_INDEX, group);
                if (stored == null)
                    continue;

                int[] key = _fixture.KeyFor(RSConstants.MAPS_INDEX, group);
                if (key == null || !_fixture.IsEncrypted(RSConstants.MAPS_INDEX, group, stored))
                    continue;

                //The enciphered region starts after the compression type and the compressed
                //length, and takes in the uncompressed-length field that sits inside it.
                int regionLength = ReadInt(stored, 1) +
                                   (stored[0] == RSConstants.NO_COMPRESSION ? 0 : 4);
                byte[] cipherText = stored.AsSpan(5, regionLength).ToArray();

                //JagStream aliases what it is handed and both operations work in place.
                var plain = new JagStream((byte[]) cipherText.Clone());
                XTEA.Decipher(plain, 0, regionLength, key);
                byte[] plainText = plain.ToArray();

                if (plainText.AsSpan().SequenceEqual(cipherText))
                {
                    failures.Add($"l{rx}_{ry} (group {group}): deciphering left the bytes unchanged");
                    continue;
                }

                var again = new JagStream(plainText);
                XTEA.Encipher(again, 0, regionLength, key);

                if (again.ToArray().AsSpan().SequenceEqual(cipherText))
                    reEnciphered++;
                else
                    failures.Add($"l{rx}_{ry} (group {group}): re-enciphering did not reproduce the " +
                                 $"{regionLength} stored ciphertext bytes");
            }

            _output.WriteLine($"encrypted l groups re-enciphered to their stored bytes: {reEnciphered}");

            AssertNoFailures(failures, "did not re-encipher to the ciphertext stored for them");
            Assert.True(reEnciphered > 0,
                "no encrypted location group was re-enciphered, so this run tested no encryption at all");
        }

        /// <summary>
        ///     Encryption detection agrees, on every location group, with the gzip magic - and the
        ///     hazard that would make it disagree is present in this cache.
        /// </summary>
        /// <remarks>
        ///     <para>
        ///     Settles a claim carried as unverified: that encryption cannot be detected by "does
        ///     it inflate", because some encrypted groups inflate over their own ciphertext into a
        ///     few bytes of garbage. <b>The hazard is real</b> - the count below is not zero - but
        ///     it needs a <em>raw</em> inflate, one that skips the ten header bytes without reading
        ///     them, which is what the 637 client does. This project never performs one:
        ///     <c>CompressionUtils.Gunzip</c> goes through <c>GZipInputStream</c>, which validates
        ///     the magic, and <c>RSContainer.Decode</c> then checks the inflated length against the
        ///     field that sits <em>inside</em> the encrypted region and is therefore garbage over
        ///     ciphertext. Two independent gates, either one sufficient.
        ///     </para>
        ///     <para>
        ///     So the recommended remedy - detect on the magic instead - is pinned here as an
        ///     equivalence rather than adopted as a change: the two methods are required to return
        ///     the same answer for every group. The raw-inflate count is asserted non-zero because
        ///     without it this test would keep passing in a cache where the trap had gone away,
        ///     and would then be evidence of nothing at all.
        ///     </para>
        /// </remarks>
        [RealCacheFact]
        public void EncryptionDetectionAgreesWithTheGzipMagicOnEveryLocationGroup()
        {
            RSReferenceTable table = _fixture.Table(RSConstants.MAPS_INDEX);

            int swept = 0;
            int magicAbsent = 0;
            int rawInflateOverCipherText = 0;
            var failures = new List<string>();

            foreach ((int rx, int ry) in EveryLocationSquare(table))
            {
                int group = table.GetArchiveId(MapSquareNames.Locations(rx, ry));
                byte[] stored = _fixture.RawContainer(RSConstants.MAPS_INDEX, group);
                if (stored == null)
                    continue;

                swept++;

                //A gzip container's payload starts at offset 9, after the compression type, the
                //compressed length and the uncompressed length. Only the first two are outside
                //the enciphered region, so on an encrypted group these three bytes are ciphertext.
                bool magic = stored.Length > 11 &&
                             stored[9] == 0x1F && stored[10] == 0x8B && stored[11] == 0x08;
                bool detected = _fixture.IsEncrypted(RSConstants.MAPS_INDEX, group, stored);

                if (magic == detected)
                {
                    failures.Add($"l{rx}_{ry} (group {group}): the gzip magic says " +
                                 $"{(magic ? "plaintext" : "encrypted")} and decoding says " +
                                 $"{(detected ? "encrypted" : "plaintext")}");
                    continue;
                }

                if (magic)
                    continue;

                magicAbsent++;

                //The trap itself, measured rather than assumed: a raw inflate that takes the ten
                //header bytes on trust and starts at the deflate stream.
                int dataLength = ReadInt(stored, 1);
                if (dataLength < 10 || 9 + dataLength > stored.Length)
                    continue;

                try
                {
                    using var source = new MemoryStream(stored.AsSpan(19, dataLength - 10).ToArray());
                    using var inflated = new InflaterInputStream(source, new Inflater(true));
                    using var sink = new MemoryStream();
                    inflated.CopyTo(sink);
                    rawInflateOverCipherText++;
                }
                catch (Exception)
                {
                    //Not inflating is the ordinary case and carries no information.
                }
            }

            _output.WriteLine($"{swept} l groups: the gzip magic and \"does it decode\" agree on all of them");
            _output.WriteLine($"of {magicAbsent} encrypted groups, {rawInflateOverCipherText} raw-inflate " +
                              "over their own ciphertext - the false positives a header-blind detector would take");

            AssertNoFailures(failures, "were classified differently by the gzip magic and by decoding");

            Assert.True(rawInflateOverCipherText > 0,
                "no encrypted group raw-inflated over its own ciphertext, so this cache does not " +
                "exercise the hazard and the agreement above is evidence of nothing");
        }

        /// <summary>Reads a big-endian 32-bit integer.</summary>
        private static int ReadInt(byte[] data, int offset)
        {
            return (data[offset] << 24) | (data[offset + 1] << 16) | (data[offset + 2] << 8) | data[offset + 3];
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

        /// <summary>Fails with the collected detail, truncated so the report stays readable.</summary>
        /// <param name="failures">The failures collected by a sweep.</param>
        /// <param name="what">
        ///     What the failing groups did, completing "N encrypted location groups ...". Named by
        ///     the caller because the two sweeps here fail for unrelated reasons and a shared
        ///     message would describe one of them wrongly.
        /// </param>
        private static void AssertNoFailures(List<string> failures,
            string what = "hold a key that did not decrypt them")
        {
            if (failures.Count == 0)
                return;

            var reported = failures.Count > MaxReportedFailures
                ? failures.GetRange(0, MaxReportedFailures)
                : failures;
            string detail = string.Join(Environment.NewLine + "  ", reported);
            if (failures.Count > MaxReportedFailures)
                detail += $"{Environment.NewLine}  ... and {failures.Count - MaxReportedFailures} more";

            Assert.Fail($"{failures.Count} encrypted location groups {what}:{Environment.NewLine}  {detail}");
        }
    }
}
