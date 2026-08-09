using System;
using System.Collections.Generic;
using System.Text;
using FlashEditor.Cache;
using FlashEditor.Cache.Region;
using FlashEditor.Tests.Cache.RealCache;
using Xunit;
using Xunit.Abstractions;

namespace FlashEditor.Tests.Map
{
    /// <summary>
    ///     Decode and byte-identity sweeps over the index-5 <c>n</c> family, the per-square NPC
    ///     spawn tables.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///     The format has no length prefix, no count and no terminator, so exact consumption is the
    ///     whole statement: the file is a whole number of four-byte records or the record width is
    ///     wrong. The reader is <c>Particle_Sub3_Sub2.method3005</c>, which loops on
    ///     <c>caret &lt; length</c>.
    ///     </para>
    ///     <para>
    ///     <b>No XTEA key is used, deliberately.</b> The client hands the real keys to this family
    ///     and <c>null</c> to <c>l</c>, which is exactly backwards - <c>Class181.java:44</c> against
    ///     <c>:76-77</c>. Every <c>n</c> group here reads as plaintext, which is the measurement
    ///     that settles it.
    ///     </para>
    /// </remarks>
    [Collection("RealCache")]
    public sealed class RealCacheNpcSpawnTests : IClassFixture<RealCacheFixture>
    {
        private readonly RealCacheFixture _fixture;
        private readonly ITestOutputHelper _output;

        /// <summary>Takes the shared cache and an output helper for the population lines.</summary>
        /// <param name="fixture">The shared open cache.</param>
        /// <param name="output">Where the swept populations are reported.</param>
        public RealCacheNpcSpawnTests(RealCacheFixture fixture, ITestOutputHelper output)
        {
            _fixture = fixture;
            _output = output;
        }

        /// <summary>
        ///     Every spawn table decodes to a whole number of records and re-encodes to the bytes it
        ///     was read from.
        /// </summary>
        /// <remarks>
        ///     Decode and encode are asserted together because the format is too small to separate
        ///     them usefully: with fixed-width records, "consumed the buffer exactly" and
        ///     "re-encoded identically" fail on the same defects, and running both leaves no room
        ///     for an encoder that happens to agree with a broken decoder about the length.
        /// </remarks>
        [RealCacheFact]
        public void EveryNpcSpawnTableSurvivesAReEncode()
        {
            var loader = new MapSquareLoader(_fixture.OpenCache());

            int declared = 0, swept = 0, byteIdentical = 0, records = 0, empty = 0;
            var planes = new SortedDictionary<int, int>();
            var failures = new List<string>();

            foreach ((int rx, int ry) in EverySquare("n"))
            {
                declared++;
                try
                {
                    int group = loader.ResolveGroup(MapSquareNames.NpcSpawns(rx, ry));
                    byte[] stored = loader.ReadGroupBytes(group);

                    List<NpcSpawn> spawns = loader.LoadNpcSpawns(rx, ry);
                    Assert.NotNull(spawns);
                    swept++;

                    if (spawns.Count == 0)
                        empty++;

                    //Restates the record width against the byte count rather than trusting the
                    //loop that produced it - a decoder reading five bytes per record would still
                    //produce a list, just a shorter one.
                    if (spawns.Count * RegionCodec.NpcSpawnRecordBytes != stored.Length)
                        failures.Add($"n{rx}_{ry}: {spawns.Count} records do not account for " +
                                     $"{stored.Length} bytes");

                    foreach (NpcSpawn spawn in spawns)
                    {
                        records++;
                        planes.TryGetValue(spawn.Plane, out int seen);
                        planes[spawn.Plane] = seen + 1;

                        //Every field is masked out of the packed word, so these cannot fail on
                        //arithmetic - they fail when the word is not a packed position at all,
                        //which is what a wrong record width produces.
                        Assert.InRange(spawn.Plane, 0, 3);
                        Assert.InRange(spawn.LocalX, 0, 63);
                        Assert.InRange(spawn.LocalY, 0, 63);
                    }

                    byte[] encoded = RegionCodec.EncodeNpcSpawns(spawns);
                    if (stored.AsSpan().SequenceEqual(encoded))
                        byteIdentical++;
                    else
                        failures.Add($"n{rx}_{ry}: re-encoded {encoded.Length} bytes from a stored " +
                                     $"{stored.Length}");
                }
                catch (Exception ex)
                {
                    failures.Add($"n{rx}_{ry}: {ex.GetType().Name}: {ex.Message}");
                }
            }

            _output.WriteLine($"{_fixture.Profile.Name}: {byteIdentical} of {swept} NPC spawn tables " +
                              $"re-encoded byte-identically, {records} spawns in them, {empty} tables empty");
            _output.WriteLine("spawns by plane: " +
                              string.Join(", ", Histogram(planes)));

            AssertNoFailures(failures);
            Assert.True(declared > 0, "the reference table declares no n groups, so nothing was swept");
            Assert.Equal(declared, swept);
            Assert.Equal(swept, byteIdentical);
            Assert.True(records > 0, "no NPC spawn decoded at all");
        }

        /// <summary>
        ///     Every spawn table reads without a key, which is what makes the client's wiring a bug
        ///     rather than a format detail.
        /// </summary>
        /// <remarks>
        ///     The client passes <c>InterfaceSettings.MAP_XTEA_KEYS</c> to this family and
        ///     <c>null</c> to <c>l</c>. Copying that would leave both unreadable. This asserts the
        ///     half that can be measured from the data: no <c>n</c> group needs a key.
        /// </remarks>
        [RealCacheFact]
        public void NoNpcSpawnTableIsEncrypted()
        {
            var loader = new MapSquareLoader(_fixture.OpenCache());

            int checked_ = 0;
            var failures = new List<string>();

            foreach ((int rx, int ry) in EverySquare("n"))
            {
                int group = loader.ResolveGroup(MapSquareNames.NpcSpawns(rx, ry));
                byte[] stored = _fixture.RawContainer(RSConstants.MAPS_INDEX, group);

                if (_fixture.IsEncrypted(RSConstants.MAPS_INDEX, group, stored))
                    failures.Add($"n{rx}_{ry} (group {group}) does not decode without a key");
                else
                    checked_++;
            }

            AssertNoFailures(failures);
            Assert.True(checked_ > 0, "no n group was examined");
            _output.WriteLine($"all {checked_} NPC spawn tables decode as plaintext");
        }

        private IEnumerable<(int, int)> EverySquare(string prefix)
        {
            RSReferenceTable table = _fixture.Table(RSConstants.MAPS_INDEX);
            for (int rx = 0; rx < 256; rx++)
                for (int ry = 0; ry < 256; ry++)
                    if (table.GetArchiveId(prefix + rx + "_" + ry) != -1)
                        yield return (rx, ry);
        }

        private static IEnumerable<string> Histogram(SortedDictionary<int, int> counts)
        {
            foreach (KeyValuePair<int, int> entry in counts)
                yield return $"{entry.Key}={entry.Value}";
        }

        private static void AssertNoFailures(List<string> failures)
        {
            if (failures.Count == 0)
                return;

            var sb = new StringBuilder();
            sb.Append(failures.Count).Append(" NPC spawn tables failed:");
            for (int i = 0; i < failures.Count && i < 15; i++)
                sb.AppendLine().Append("  ").Append(failures[i]);
            if (failures.Count > 15)
                sb.AppendLine().Append("  ... and ").Append(failures.Count - 15).Append(" more");

            Assert.Fail(sb.ToString());
        }
    }
}
