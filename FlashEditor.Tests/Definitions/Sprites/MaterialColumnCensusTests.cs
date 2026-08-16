using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using FlashEditor.Cache;
using FlashEditor.Definitions.Sprites;
using FlashEditor.IO;
using FlashEditor.Tests.Cache.RealCache;
using Xunit;
using Xunit.Abstractions;

namespace FlashEditor.Tests.Definitions.Sprites
{
    /// <summary>
    ///     A measurement of what each of the nineteen index-26 columns actually holds, printed
    ///     rather than asserted.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///     This exists so a claim about what a column <em>means</em> can be falsified against the
    ///     data instead of resting on the client alone. It names nothing and asserts nothing about
    ///     any field's purpose: it reports distributions, constancy, the {0,1} question, whether a
    ///     signed reading is exercised at all, whether two columns are indistinguishable by data,
    ///     and how each column splits between slots that carry an index-9 procedural graph and
    ///     slots that do not.
    ///     </para>
    ///     <para>
    ///     <b>Read from the stored bytes, not from the decoded fields.</b> Three columns decode
    ///     many-to-one and one is stored inverted, so a census taken off the properties would
    ///     report the decoder's opinion rather than the cache's contents - and would hide a
    ///     mistake in the inversion behind the very field that made it.
    ///     </para>
    ///     <para>
    ///     Run it filtered, once per cache. It is one group of one file, so it is cheap and does
    ///     not want a sweep:
    ///     <c>dotnet test --filter FullyQualifiedName~MaterialColumnCensusTests</c>, with
    ///     <c>FLASHEDITOR_TEST_CACHE</c> pointed at the repack for the second run.
    ///     </para>
    /// </remarks>
    [Collection("RealCache")]
    public sealed class MaterialColumnCensusTests : IClassFixture<RealCacheFixture>
    {
        /// <summary>Distinct values listed per distribution before the rest are summarised.</summary>
        private const int TopValues = 10;

        private readonly RealCacheFixture _fixture;
        private readonly ITestOutputHelper _output;

        /// <summary>Binds the shared open cache and the per-test output sink.</summary>
        public MaterialColumnCensusTests(RealCacheFixture fixture, ITestOutputHelper output)
        {
            _fixture = fixture;
            _output = output;
        }

        /// <summary>The columns in the order the file stores them.</summary>
        private static MaterialColumn[] Columns =>
            (MaterialColumn[]) Enum.GetValues(typeof(MaterialColumn));

        /// <summary>
        ///     Columns this codec reads back as a signed byte, so a raw byte at or above 128
        ///     decodes negative.
        /// </summary>
        /// <remarks>
        ///     Listed here rather than derived because the decoder spells signedness inline in
        ///     <c>MaterialTable.Unpack</c>; the point of the census is to say whether the cache
        ///     ever exercises the distinction, which needs the claim stated separately from the
        ///     code making it.
        /// </remarks>
        private static readonly MaterialColumn[] SignedByteColumns =
        {
            MaterialColumn.Field1829, MaterialColumn.Field1830, MaterialColumn.Field1820,
            MaterialColumn.Field1816, MaterialColumn.Field1823, MaterialColumn.Field1837,
            MaterialColumn.Field1832
        };

        /// <summary>
        ///     Prints the whole census as markdown, and asserts only that there was data to census.
        /// </summary>
        /// <remarks>
        ///     Deliberately close to assertion-free. Every figure here is a property of one of the
        ///     two caches, and pinning any of them would turn a measurement into a target - the
        ///     assertions that hold index 26 to its format live in <c>RealCacheMaterialTests</c>.
        ///     The two guards below are the only claims that are true of any cache at all: a table
        ///     with no present records has nothing to say, and a census read off rows that were
        ///     never captured would be reporting zeroes of its own making.
        /// </remarks>
        [RealCacheFact]
        public void MaterialColumns_AreCensusedAgainstTheLoadedCache()
        {
            RSCache cache = _fixture.OpenCache();
            CacheAddressing addressing = CacheAddressing.For(RSConstants.MATERIALS);
            byte[] stored = cache.ReadFileBytes(RSConstants.MATERIALS,
                addressing.GroupOf(MaterialTable.WholeTableDefinitionId),
                addressing.FileOf(MaterialTable.WholeTableDefinitionId));
            MaterialTable table = MaterialTable.Decode(new JagStream(stored));

            var slots = new List<int>();
            foreach (TextureDefinition def in table.Slots)
                if (def != null)
                    slots.Add(def.id);

            Assert.NotEmpty(slots);
            foreach (int slot in slots)
                Assert.NotNull(table.Slots[slot].StoredRecord);

            HashSet<int> graphed = GraphBearingGroups();

            //Raw, unsigned, big-endian per column. Signedness is a decoder choice and is reported
            //separately; the storage is what this reads.
            var raw = new Dictionary<MaterialColumn, long[]>();
            foreach (MaterialColumn column in Columns)
            {
                var values = new long[slots.Count];
                for (int i = 0; i < slots.Count; i++)
                    values[i] = RawValue(table.Slots[slots[i]].StoredRecord, column);
                raw[column] = values;
            }

            int withGraph = slots.Count(slot => graphed.Contains(slot));

            _output.WriteLine("# Index 26 column census - " + _fixture.Profile.Name);
            _output.WriteLine("");
            _output.WriteLine("Cache directory: `" + RealCacheLocator.Directory + "`");
            _output.WriteLine("");
            _output.WriteLine("- declared slots: " + table.Count);
            _output.WriteLine("- present records: " + slots.Count);
            _output.WriteLine("- file length: " + stored.Length + " bytes");
            _output.WriteLine("- index-9 groups the reference table declares: " + graphed.Count);
            _output.WriteLine("- present slots whose id is an index-9 group: " + withGraph);
            _output.WriteLine("- present slots with no index-9 group: " + (slots.Count - withGraph));
            _output.WriteLine("");

            WriteSummaryTable(raw, slots.Count);
            WritePerColumn(raw, slots.Count);
            WriteInversionCheck(table, slots);
            WriteSignedReading(raw);
            WriteWideColumns(raw);
            WriteGraphCorrelation(raw, slots, graphed);
            WritePairwiseIdentity(raw);
        }

        /// <summary>
        ///     Group ids index 9's reference table declares, which is what makes a texture slot
        ///     graph-bearing.
        /// </summary>
        /// <remarks>
        ///     Taken from the table rather than by decoding index 9. The client gates every read on
        ///     the table, so an undeclared group is unreachable whatever its bytes say, and reading
        ///     946 procedural graphs to answer a yes/no question would be paying for a sweep this
        ///     test is explicitly not doing.
        /// </remarks>
        /// <returns>The declared index-9 group ids.</returns>
        private HashSet<int> GraphBearingGroups()
        {
            return new HashSet<int>(_fixture.Table(RSConstants.TEXTURES).GetArchiveEntries().Keys);
        }

        /// <summary>Reads one column out of a record's stored bytes, unsigned and big-endian.</summary>
        /// <param name="row">The record's 23 stored bytes.</param>
        /// <param name="column">The column to read.</param>
        /// <returns>The stored value, widened without sign extension.</returns>
        private static long RawValue(byte[] row, MaterialColumn column)
        {
            int at = MaterialTable.OffsetOf(column);
            int width = MaterialTable.WidthOf(column);

            long value = 0;
            for (int i = 0; i < width; i++)
                value = (value << 8) | row[at + i];
            return value;
        }

        /// <summary>One line per column, for scanning the shape of the whole table at once.</summary>
        /// <param name="raw">Stored values per column.</param>
        /// <param name="present">Present record count.</param>
        private void WriteSummaryTable(Dictionary<MaterialColumn, long[]> raw, int present)
        {
            _output.WriteLine("## Summary");
            _output.WriteLine("");
            _output.WriteLine("| column | offset | width | distinct | min | max | zero slots | constant | only {0,1} |");
            _output.WriteLine("|---|---|---|---|---|---|---|---|---|");

            foreach (MaterialColumn column in Columns)
            {
                long[] values = raw[column];
                var distinct = new HashSet<long>(values);
                bool binary = distinct.All(v => v == 0 || v == 1);

                _output.WriteLine("| " + column + " | " + MaterialTable.OffsetOf(column) + " | " +
                                  MaterialTable.WidthOf(column) + " | " + distinct.Count + " | " +
                                  values.Min() + " | " + values.Max() + " | " +
                                  values.Count(v => v == 0) + " of " + present + " | " +
                                  (distinct.Count == 1 ? "**yes**" : "no") + " | " +
                                  (binary ? "yes" : "no") + " |");
            }

            _output.WriteLine("");
        }

        /// <summary>The value distribution of every column, capped at the ten most common.</summary>
        /// <param name="raw">Stored values per column.</param>
        /// <param name="present">Present record count.</param>
        private void WritePerColumn(Dictionary<MaterialColumn, long[]> raw, int present)
        {
            _output.WriteLine("## Distributions");
            _output.WriteLine("");

            foreach (MaterialColumn column in Columns)
            {
                long[] values = raw[column];
                var counts = Tally(values);

                _output.WriteLine("### " + column + " (offset " + MaterialTable.OffsetOf(column) +
                                  ", width " + MaterialTable.WidthOf(column) + ")");
                _output.WriteLine("");
                _output.WriteLine("| stored value | slots |");
                _output.WriteLine("|---|---|");

                int listed = 0;
                long accounted = 0;
                foreach (KeyValuePair<long, int> entry in counts.OrderByDescending(e => e.Value)
                             .ThenBy(e => e.Key))
                {
                    if (listed == TopValues)
                        break;
                    _output.WriteLine("| " + entry.Key + " | " + entry.Value + " |");
                    accounted += entry.Value;
                    listed++;
                }

                if (counts.Count > listed)
                    _output.WriteLine("| _" + (counts.Count - listed) + " further values_ | " +
                                      (present - accounted) + " |");

                _output.WriteLine("");
                _output.WriteLine("- distinct: " + counts.Count + ", min " + values.Min() +
                                  ", max " + values.Max() + ", zero in " + values.Count(v => v == 0) +
                                  " of " + present + " slots");
                _output.WriteLine("- constant across every slot: " +
                                  (counts.Count == 1 ? "**yes**" : "no"));
                _output.WriteLine("- takes only 0 and 1: " +
                                  (counts.Keys.All(v => v == 0 || v == 1) ? "yes" : "**no**"));
                _output.WriteLine("");
            }
        }

        /// <summary>
        ///     The stored bytes of the inverted column against the bool the decoder produces.
        /// </summary>
        /// <remarks>
        ///     Reported as both halves because the decoded bool is where a mistake in the inversion
        ///     would be invisible: reading the field alone cannot tell "stored 0, decoded true" from
        ///     an encoder that had the sense the other way round.
        /// </remarks>
        /// <param name="table">The decoded table.</param>
        /// <param name="slots">Present slot ids.</param>
        private void WriteInversionCheck(MaterialTable table, List<int> slots)
        {
            _output.WriteLine("## Field1825, stored against decoded");
            _output.WriteLine("");
            _output.WriteLine("| stored byte | decoded true | decoded false |");
            _output.WriteLine("|---|---|---|");

            var byStored = new SortedDictionary<long, int[]>();
            foreach (int slot in slots)
            {
                TextureDefinition def = table.Slots[slot];
                long stored = RawValue(def.StoredRecord, MaterialColumn.Field1825);
                if (!byStored.TryGetValue(stored, out int[] pair))
                    byStored[stored] = pair = new int[2];
                pair[def.field1825 ? 0 : 1]++;
            }

            foreach (KeyValuePair<long, int[]> entry in byStored)
                _output.WriteLine("| " + entry.Key + " | " + entry.Value[0] + " | " + entry.Value[1] + " |");

            _output.WriteLine("");
        }

        /// <summary>Whether any slot exercises the signed reading of a signed-byte column.</summary>
        /// <param name="raw">Stored values per column.</param>
        private void WriteSignedReading(Dictionary<MaterialColumn, long[]> raw)
        {
            _output.WriteLine("## Signed byte columns");
            _output.WriteLine("");
            _output.WriteLine("| column | slots with the high bit set | signed min | signed max | signed reading exercised |");
            _output.WriteLine("|---|---|---|---|---|");

            foreach (MaterialColumn column in SignedByteColumns)
            {
                long[] values = raw[column];
                int negative = values.Count(v => v >= 128);
                var signed = values.Select(v => (long) unchecked((sbyte) (byte) v)).ToArray();

                _output.WriteLine("| " + column + " | " + negative + " | " + signed.Min() + " | " +
                                  signed.Max() + " | " + (negative > 0 ? "**yes**" : "no") + " |");
            }

            _output.WriteLine("");
        }

        /// <summary>The two columns wider than a byte, reported in the units they are stored in.</summary>
        /// <param name="raw">Stored values per column.</param>
        private void WriteWideColumns(Dictionary<MaterialColumn, long[]> raw)
        {
            _output.WriteLine("## Wide columns");
            _output.WriteLine("");

            long[] two = raw[MaterialColumn.Field1831];
            _output.WriteLine("- Field1831 (2 bytes): distinct " + new HashSet<long>(two).Count +
                              ", min " + two.Min() + ", max " + two.Max() + ", zero in " +
                              two.Count(v => v == 0) + " slots, at or above 32768 in " +
                              two.Count(v => v >= 32768) + " slots");

            long[] four = raw[MaterialColumn.Field1835];
            int nonZero = four.Count(v => v != 0);
            _output.WriteLine("- Field1835 (4 bytes): non-zero in **" + nonZero + "** of " +
                              four.Length + " slots, min " + four.Min() + ", max " + four.Max() +
                              ", distinct " + new HashSet<long>(four).Count);
            _output.WriteLine("");
        }

        /// <summary>
        ///     Every column split by whether the slot's id is an index-9 group.
        /// </summary>
        /// <remarks>
        ///     The cross-cut that can actually falsify a naming claim. A column whose value set on
        ///     graph-bearing slots is disjoint from its value set on graphless ones is saying
        ///     something about that distinction; one whose sets coincide is not.
        /// </remarks>
        /// <param name="raw">Stored values per column.</param>
        /// <param name="slots">Present slot ids, in the order the values were gathered.</param>
        /// <param name="graphed">Index-9 group ids.</param>
        private void WriteGraphCorrelation(Dictionary<MaterialColumn, long[]> raw, List<int> slots,
            HashSet<int> graphed)
        {
            _output.WriteLine("## Correlation with index-9 graph presence");
            _output.WriteLine("");
            _output.WriteLine("| column | values on graph-bearing slots | values on graphless slots | relationship |");
            _output.WriteLine("|---|---|---|---|");

            foreach (MaterialColumn column in Columns)
            {
                long[] values = raw[column];
                var withGraph = new List<long>();
                var without = new List<long>();

                for (int i = 0; i < slots.Count; i++)
                    (graphed.Contains(slots[i]) ? withGraph : without).Add(values[i]);

                var a = new HashSet<long>(withGraph);
                var b = new HashSet<long>(without);

                string relationship;
                if (b.Count == 0)
                    relationship = "no graphless slots in this cache";
                else if (!a.Overlaps(b))
                    relationship = "**disjoint**";
                else if (a.SetEquals(b))
                    relationship = "same value set";
                else
                    relationship = "overlapping";

                _output.WriteLine("| " + column + " | " + Describe(withGraph) + " | " +
                                  Describe(without) + " | " + relationship + " |");
            }

            _output.WriteLine("");
        }

        /// <summary>Which columns hold the same value as another column in every slot.</summary>
        /// <remarks>
        ///     Two columns that agree everywhere cannot be told apart by this cache at all, so any
        ///     naming that distinguishes them rests entirely on the client. Reported as pairs
        ///     rather than as groups so a chain of three shows up as the three pairs it is.
        /// </remarks>
        /// <param name="raw">Stored values per column.</param>
        private void WritePairwiseIdentity(Dictionary<MaterialColumn, long[]> raw)
        {
            _output.WriteLine("## Pairwise identical columns");
            _output.WriteLine("");

            MaterialColumn[] columns = Columns;
            var pairs = new List<string>();

            for (int i = 0; i < columns.Length; i++)
                for (int j = i + 1; j < columns.Length; j++)
                {
                    long[] left = raw[columns[i]];
                    long[] right = raw[columns[j]];

                    bool same = true;
                    for (int k = 0; k < left.Length && same; k++)
                        same = left[k] == right[k];

                    if (same)
                        pairs.Add("- " + columns[i] + " == " + columns[j] +
                                  " in every slot (both " + Describe(left.ToList()) + ")");
                }

            if (pairs.Count == 0)
                _output.WriteLine("No two columns hold the same value in every slot.");
            else
                foreach (string pair in pairs)
                    _output.WriteLine(pair);

            _output.WriteLine("");
        }

        /// <summary>Counts each distinct value.</summary>
        /// <param name="values">The values.</param>
        /// <returns>Value to slot count.</returns>
        private static Dictionary<long, int> Tally(IEnumerable<long> values)
        {
            var counts = new Dictionary<long, int>();
            foreach (long value in values)
                counts[value] = counts.TryGetValue(value, out int seen) ? seen + 1 : 1;
            return counts;
        }

        /// <summary>Renders a value set compactly enough to sit in a table cell.</summary>
        /// <param name="values">The values.</param>
        /// <returns>Up to ten value:count pairs, then a count of the rest.</returns>
        private static string Describe(List<long> values)
        {
            if (values.Count == 0)
                return "_none_";

            Dictionary<long, int> counts = Tally(values);
            var text = new StringBuilder();
            int listed = 0;

            foreach (KeyValuePair<long, int> entry in counts.OrderByDescending(e => e.Value)
                         .ThenBy(e => e.Key))
            {
                if (listed == TopValues)
                    break;
                if (listed > 0)
                    text.Append(", ");
                text.Append(entry.Key).Append(':').Append(entry.Value);
                listed++;
            }

            if (counts.Count > listed)
                text.Append(" (+").Append(counts.Count - listed).Append(" more values)");

            return text.ToString();
        }
    }
}
