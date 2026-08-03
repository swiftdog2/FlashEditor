using FlashEditor.cache;
using System;
using System.Collections.Generic;
using System.Linq;
using Xunit;
using Xunit.Abstractions;

namespace FlashEditor.Tests.Cache.RealCache
{
    /// <summary>Which revision-639 cache the suite is running against.</summary>
    public enum RealCacheKind
    {
        /// <summary>A 639 cache this project has not measured.</summary>
        Unrecognised,

        /// <summary>The vanilla live-server capture, OpenRS2 cache id 1194.</summary>
        VanillaB639,

        /// <summary>A private-server repack: a 639 base with local modifications.</summary>
        Repack
    }

    /// <summary>
    ///     Recognises which revision-639 cache is loaded, and holds the facts that are true of
    ///     that cache alone.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///     Two 639 caches are supported and they disagree on eleven indexes. Most of what the
    ///     suite asserts is a property of the format or of build 639 and holds in both; a few
    ///     things are properties of one cache's contents - which groups its idx files hold that
    ///     no reference table declares, whether its tables carry a tail, how many components
    ///     carry a given flag. Deleting those would lose real coverage on the repack, and
    ///     asserting them unconditionally would fail on the vanilla capture, so they are scoped
    ///     here instead.
    ///     </para>
    ///     <para>
    ///     Recognition is by measured content, never by directory name: a cache copied, renamed
    ///     or pointed at through <see cref="RealCacheLocator.PathVariable"/> must still be
    ///     recognised as what it is. Reference-table <em>versions</em> are deliberately not used
    ///     - index 3 carries version 1131 in both caches while holding 11 more groups and 1373
    ///     more files in the repack, so a matching version is not evidence that an index is
    ///     untouched. Declared group and file counts are read instead, from three indexes at
    ///     once so a single edit cannot make one cache masquerade as the other.
    ///     </para>
    ///     <para>
    ///     A cache matching neither fingerprint is <see cref="RealCacheKind.Unrecognised"/>
    ///     rather than an error. Everything universal still runs against it; the scoped facts
    ///     report themselves as unrecorded instead of asserting a number measured somewhere else.
    ///     </para>
    /// </remarks>
    public sealed class RealCacheProfile
    {
        /// <summary>Indexes whose declared counts form the fingerprint, in fingerprint order.</summary>
        private static readonly int[] FingerprintIndexes =
        {
            RSConstants.INTERFACE_DEFINITIONS_INDEX, RSConstants.TEXTURES, RSConstants.ITEM_DEFINITIONS_INDEX
        };

        /// <summary>Prefix on every line reporting a figure this profile has no expectation for.</summary>
        /// <remarks>
        ///     Distinctive so a run against a cache the project has not measured can be grepped
        ///     for the figures to record, rather than the gap being invisible in the log.
        /// </remarks>
        public const string UnrecordedPrefix = "UNRECORDED";

        private readonly IReadOnlyDictionary<string, long> _census;

        /// <summary>Which cache this is.</summary>
        public RealCacheKind Kind { get; }

        /// <summary>Human-readable name, for failure messages and output lines.</summary>
        public string Name { get; }

        /// <summary>
        ///     Indexes whose reference table carries bytes past the last field the format defines,
        ///     or <c>null</c> when this cache has not been measured.
        /// </summary>
        /// <remarks>
        ///     The repack alone carries them: four zero bytes per file on indexes 9, 26, 27 and 29.
        ///     Every one of the vanilla capture's 35 tables consumes to the byte, which is what
        ///     settles the tail as repacker residue rather than a feature of the format - and is
        ///     why a parser has to tolerate a tail without ever requiring one.
        /// </remarks>
        public IReadOnlyList<int> TablesWithATail { get; }

        /// <summary>
        ///     Group ids each index holds in its idx file but does not declare in its reference
        ///     table, or <c>null</c> when this cache has not been measured.
        /// </summary>
        /// <remarks>
        ///     Every orphan in the repack is residue from repacking. The vanilla capture has none
        ///     on any index, so the idx-driven and table-driven readings of it agree everywhere -
        ///     which is informative in itself, and is why the enumeration test asserts the two
        ///     readings against each other rather than against a list of ids.
        /// </remarks>
        public IReadOnlyDictionary<int, int[]> OrphanGroups { get; }

        /// <summary>
        ///     Stands in for a profile when no cache was located, so nothing has to be nullable.
        /// </summary>
        /// <remarks>
        ///     Never reached by a test body: <c>RealCacheFact</c> skips every cache-backed test
        ///     when the fixture has no cache, so this exists only so the fixture's property is
        ///     never null.
        /// </remarks>
        public static RealCacheProfile Unopened { get; } = new RealCacheProfile(
            RealCacheKind.Unrecognised, "no cache", null, null, new Dictionary<string, long>());

        private RealCacheProfile(RealCacheKind kind, string name, IReadOnlyList<int> tablesWithATail,
            IReadOnlyDictionary<int, int[]> orphanGroups, IReadOnlyDictionary<string, long> census)
        {
            Kind = kind;
            Name = name;
            TablesWithATail = tablesWithATail;
            OrphanGroups = orphanGroups;
            _census = census;
        }

        /// <summary>
        ///     Recognises a cache from the counts its reference tables declare.
        /// </summary>
        /// <param name="table">Resolves an index's decoded reference table.</param>
        /// <returns>The matching profile, or the unrecognised one.</returns>
        public static RealCacheProfile Identify(Func<int, RSReferenceTable> table)
        {
            var fingerprint = new List<int>();
            try
            {
                foreach (int indexId in FingerprintIndexes)
                {
                    RSReferenceTable declared = table(indexId);
                    fingerprint.Add(declared.GetArchiveCount());
                    fingerprint.Add(declared.GetArchiveEntries().Values.Sum(entry => entry.GetValidFileIds().Length));
                }
            }
            catch (Exception ex)
            {
                //A cache missing one of the fingerprint indexes is simply not one of the two this
                //project has measured. Failing to recognise it must not stop the fixture opening,
                //or every cache-backed test would report the same unrelated error.
                return new RealCacheProfile(RealCacheKind.Unrecognised,
                    "a 639 cache that could not be fingerprinted (" + ex.GetType().Name + ")",
                    null, null, new Dictionary<string, long>());
            }

            //Index 3 groups and files, index 9 groups and files, index 19 groups and files.
            if (fingerprint.SequenceEqual(new[] { 1067, 40883, 915, 915, 80, 20427 }))
                return Vanilla();
            if (fingerprint.SequenceEqual(new[] { 1078, 42256, 946, 946, 80, 20470 }))
                return Repack();

            return new RealCacheProfile(RealCacheKind.Unrecognised,
                "an unrecognised 639 cache (" + string.Join("/", fingerprint) + ")",
                null, null, new Dictionary<string, long>());
        }

        /// <summary>
        ///     The vanilla live-server capture, OpenRS2 cache id 1194, dated 2011-02-23.
        /// </summary>
        /// <remarks>
        ///     <para>
        ///     No table carries a tail and no index carries an orphan, both measured by decoding
        ///     all 35 tables and walking every idx file.
        ///     </para>
        ///     <para>
        ///     The content figures were measured by a scratchpad reader that walks the sector
        ///     chains and the container and archive layers itself, so none of them came through
        ///     this project's decoders. Each was validated by running the same reader against the
        ///     repack and requiring it to reproduce the figure already recorded there - which it
        ///     did, exactly, for every one below. That is the third method
        ///     <c>CLAUDE.md</c> asks for, rather than a value copied out of a document.
        ///     </para>
        ///     <para>
        ///     Twelve of them - the selected-action, sentinel, slot, hook, trigger, action nibble
        ///     and operand populations - could only come from a full component decode, so they were
        ///     taken from the suite's own first run against this cache rather than out of band.
        ///     Seven land on the repack's figure exactly (model and animation sentinels, slot
        ///     tables and entries, target gating, trigger arrays, the high action nibble), which is
        ///     what says the branches they defend are build-639 properties and not repack residue;
        ///     the five that moved are the ones the repack's extra interface groups add to.
        ///     </para>
        /// </remarks>
        private static RealCacheProfile Vanilla()
        {
            return new RealCacheProfile(RealCacheKind.VanillaB639, "the vanilla b639 capture (OpenRS2 1194)",
                Array.Empty<int>(),
                new Dictionary<int, int[]>(),
                new Dictionary<string, long>
                {
                    //Index 3. Every group and every file of it carries a name here, so the two
                    //unnamed counts are zero rather than absent - which is why the listing test
                    //asserts that real names came through instead of counting the unnamed.
                    ["interface.payloadBytes"] = 3387696,
                    ["interface.type.0"] = 6551,
                    ["interface.type.3"] = 4517,
                    ["interface.type.4"] = 9007,
                    ["interface.type.5"] = 13432,
                    ["interface.type.6"] = 7009,
                    ["interface.type.9"] = 367,
                    ["interface.rootComponents"] = 8337,
                    ["interface.parentedComponents"] = 32546,
                    ["interface.nonZeroContentTypes"] = 21,
                    ["interface.emptySelectedActions"] = 40790,
                    ["interface.modelSentinels"] = 1001,
                    ["interface.animationSentinels"] = 6466,
                    ["interface.slotTables"] = 43,
                    ["interface.slotEntries"] = 55,
                    ["interface.targetGated"] = 140,
                    ["interface.hookArrays"] = 12916,
                    ["interface.triggerArrays"] = 1391,
                    ["interface.actionHighNibble.0"] = 40744,
                    ["interface.actionHighNibble.1"] = 139,
                    ["interface.operand.integer"] = 38223,
                    ["interface.operand.string"] = 1513,
                    ["interface.rowsInUnnamedGroups"] = 0,
                    ["interface.unnamedFiles"] = 0,

                    //Index 5. The terrain figure moved by one against the repack, which is the 12
                    //differing m squares showing through. The location figure did not move at all:
                    //991 l squares differ and the same 63 of them still reach the continuation,
                    //because what the repack changed was the encryption, not the object lists.
                    ["map.terrainWithExtras"] = 1323,
                    ["map.locationsUsingTheSmartContinuation"] = 63
                });
        }

        /// <summary>
        ///     The private-server repack: a 639 base with local modifications on at least eleven
        ///     indexes.
        /// </summary>
        /// <remarks>
        ///     Every figure here was measured against this cache and this cache only. They are
        ///     kept rather than deleted because each one pins a real branch - a component flag
        ///     that occurs 139 times, a group the table does not declare - and losing them would
        ///     narrow the repack's coverage to pay for the vanilla capture's.
        /// </remarks>
        private static RealCacheProfile Repack()
        {
            return new RealCacheProfile(RealCacheKind.Repack, "the private-server repack",
                new[] { RSConstants.TEXTURES, RSConstants.MATERIALS, RSConstants.CONFIG_PARTICLES, RSConstants.CONFIG_BILLBOARD },
                new Dictionary<int, int[]>
                {
                    [RSConstants.INTERFACE_DEFINITIONS_INDEX] = new[] { 772, 825, 891 },
                    [RSConstants.SOUND_EFFECTS] = new[] { 4787 },
                    [RSConstants.CLIENT_SCRIPTS_INDEX] = new[] { 699, 700 },
                    [RSConstants.LOADING_SPRITES] = new[] { 498, 1407 }
                },
                new Dictionary<string, long>
                {
                    //Index 3. The file counts recovered for the three undeclared groups, and the
                    //population of every branch the byte-identity sweep cannot speak for.
                    ["interface.undeclared.772.files"] = 14,
                    ["interface.undeclared.825.files"] = 32,
                    ["interface.undeclared.891.files"] = 43,
                    ["interface.payloadBytes"] = 3550506,
                    ["interface.type.0"] = 6573,
                    ["interface.type.3"] = 4528,
                    ["interface.type.4"] = 10317,
                    ["interface.type.5"] = 13462,
                    ["interface.type.6"] = 7009,
                    ["interface.type.9"] = 367,
                    ["interface.rootComponents"] = 8413,
                    ["interface.parentedComponents"] = 33843,
                    ["interface.nonZeroContentTypes"] = 21,
                    ["interface.emptySelectedActions"] = 42163,
                    ["interface.modelSentinels"] = 1001,
                    ["interface.animationSentinels"] = 6466,
                    ["interface.slotTables"] = 43,
                    ["interface.slotEntries"] = 55,
                    ["interface.targetGated"] = 140,
                    ["interface.hookArrays"] = 15541,
                    ["interface.triggerArrays"] = 1391,
                    ["interface.actionHighNibble.0"] = 42117,
                    ["interface.actionHighNibble.1"] = 139,
                    ["interface.operand.integer"] = 46033,
                    ["interface.operand.string"] = 1505,
                    ["interface.rowsInUnnamedGroups"] = 1377,
                    ["interface.unnamedFiles"] = 1721,

                    //Index 5. Both are format-coverage measurements over content the two caches
                    //disagree on - 12 of the 1684 terrain squares and 991 of the location squares
                    //differ - so neither transfers.
                    ["map.terrainWithExtras"] = 1324,
                    ["map.locationsUsingTheSmartContinuation"] = 63
                });
        }

        /// <summary>
        ///     Asserts a measured figure against this cache's recorded value, or reports it as
        ///     unrecorded.
        /// </summary>
        /// <remarks>
        ///     A count of decoded content is a fact about one cache, so it can only be asserted
        ///     against the cache it was measured on. Where no figure has been recorded the
        ///     measurement is printed under <see cref="UnrecordedPrefix"/> rather than dropped, so
        ///     the run that first sweeps a new cache produces exactly the lines needed to record
        ///     it. Whatever the caller does here, the relationships around the figure - a
        ///     histogram summing to the record count, two counters that must agree - are asserted
        ///     unconditionally by the caller and are what defends an unrecognised cache.
        /// </remarks>
        /// <param name="output">Where the unrecorded line goes.</param>
        /// <param name="key">The figure's name in the census.</param>
        /// <param name="measured">What this run measured.</param>
        public void AssertCensus(ITestOutputHelper output, string key, long measured)
        {
            if (!_census.TryGetValue(key, out long expected))
            {
                output.WriteLine($"{UnrecordedPrefix} {Name}: {key} = {measured}");
                return;
            }

            Assert.True(expected == measured,
                $"{key} is {measured} in {Name}, which recorded {expected}. If the cache really has " +
                "changed, re-measure it and record the new figure; if it has not, something decodes " +
                "differently than it did.");
        }
    }
}
