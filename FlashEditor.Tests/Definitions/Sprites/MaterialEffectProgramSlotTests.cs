using System;
using System.Collections.Generic;
using System.Linq;
using FlashEditor.Cache;
using FlashEditor.Definitions.Sprites;
using FlashEditor.Tests.Cache.RealCache;
using Xunit;
using Xunit.Abstractions;

namespace FlashEditor.Tests.Definitions.Sprites
{
    /// <summary>
    ///     Every index-26 slot whose Field1820 selects a high effect program, printed whole.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///     Written to settle one disagreement. A reading of the client concluded that Field1835 is
    ///     read only by the effect programs Field1820 selects at 8 and 9, and that this is why
    ///     Field1835 is zero everywhere; a census of the data found Field1820 reaching 8 in both
    ///     caches, which would make that explanation self-contradictory. Neither half can settle it,
    ///     so this prints the whole row for every slot that reaches the disputed value, and the
    ///     Field1820 distribution around it, and leaves the client reading to be checked against it.
    ///     </para>
    ///     <para>
    ///     <b>Read from the stored bytes as well as the decoded fields.</b> Field1820 decodes to a
    ///     signed byte, so "the maximum is 8" is a claim about one of two readings, and a stored 0xF8
    ///     would be -8 signed while looking nothing like 8 raw. Both are printed for every column so
    ///     the question cannot be answered by the decoder's choice alone.
    ///     </para>
    ///     <para>
    ///     Run it filtered, once per cache. Index 26 is one group of one file, so this is not a sweep
    ///     and must not be turned into one:
    ///     <c>dotnet test --filter FullyQualifiedName~MaterialEffectProgramSlotTests</c>, with
    ///     <c>FLASHEDITOR_TEST_CACHE</c> pointed at the repack for the second run.
    ///     </para>
    /// </remarks>
    [Collection("RealCache")]
    public sealed class MaterialEffectProgramSlotTests : IClassFixture<RealCacheFixture>
    {
        /// <summary>
        ///     The Field1820 value the client investigation attributed a Field1835 read to.
        /// </summary>
        private const int DisputedProgram = 8;

        private readonly RealCacheFixture _fixture;
        private readonly ITestOutputHelper _output;

        /// <summary>Binds the shared open cache and the per-test output sink.</summary>
        public MaterialEffectProgramSlotTests(RealCacheFixture fixture, ITestOutputHelper output)
        {
            _fixture = fixture;
            _output = output;
        }

        /// <summary>The columns in the order the file stores them.</summary>
        private static MaterialColumn[] Columns =>
            (MaterialColumn[]) Enum.GetValues(typeof(MaterialColumn));

        /// <summary>
        ///     Prints the Field1820 distribution and the full row of every slot that reaches the
        ///     disputed program index.
        /// </summary>
        /// <remarks>
        ///     Assertion-free by intent bar the two guards. Every figure below belongs to whichever
        ///     cache was loaded, and pinning one would make a measurement into a target; the claims
        ///     that hold index 26 to its format are asserted in <c>RealCacheMaterialTests</c>. The
        ///     guards are the only statements true of any cache at all - a table with no present
        ///     records has nothing to report, and a row read off a record whose bytes were never
        ///     captured would be printing zeroes this test invented.
        /// </remarks>
        [RealCacheFact]
        public void EffectProgramSlots_AreListedWithEveryColumnTheyHold()
        {
            RSCache cache = _fixture.OpenCache();
            MaterialTable table = MaterialTable.Load(cache);

            var slots = new List<int>();
            foreach (TextureDefinition def in table.Slots)
                if (def != null)
                    slots.Add(def.id);

            Assert.NotEmpty(slots);
            foreach (int slot in slots)
                Assert.NotNull(table.Slots[slot].StoredRecord);

            HashSet<int> graphed = GraphBearingGroups();

            _output.WriteLine("# Index 26 Field1820 effect programs - " + _fixture.Profile.Name);
            _output.WriteLine("");
            _output.WriteLine("Cache directory: `" + RealCacheLocator.Directory + "`");
            _output.WriteLine("");
            _output.WriteLine("- declared slots: " + table.Count);
            _output.WriteLine("- present records: " + slots.Count);
            _output.WriteLine("- index-9 groups the reference table declares: " + graphed.Count);
            _output.WriteLine("");

            WriteDistribution(table, slots);
            WriteHighSlots(table, slots, graphed);
            WriteField1835Census(table, slots);
        }

        /// <summary>
        ///     Group ids index 9's reference table declares, which is what makes a slot graph-bearing.
        /// </summary>
        /// <remarks>
        ///     Read from the table rather than by decoding index 9. The client gates every read on the
        ///     table, so an undeclared group is unreachable whatever its bytes hold, and decoding
        ///     hundreds of procedural graphs to answer a yes/no question would be paying for a sweep
        ///     this test is explicitly not doing.
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

        /// <summary>The whole Field1820 distribution, both raw and as the signed byte it decodes to.</summary>
        /// <param name="table">The decoded table.</param>
        /// <param name="slots">Present slot ids.</param>
        private void WriteDistribution(MaterialTable table, List<int> slots)
        {
            var counts = new SortedDictionary<long, int>();
            foreach (int slot in slots)
            {
                long raw = RawValue(table.Slots[slot].StoredRecord, MaterialColumn.Field1820);
                counts[raw] = counts.TryGetValue(raw, out int seen) ? seen + 1 : 1;
            }

            _output.WriteLine("## Field1820 distribution");
            _output.WriteLine("");
            _output.WriteLine("| stored byte | hex | signed | slots |");
            _output.WriteLine("|---|---|---|---|");

            foreach (KeyValuePair<long, int> entry in counts)
                _output.WriteLine("| " + entry.Key + " | 0x" + ((byte) entry.Key).ToString("X2") + " | " +
                                  unchecked((sbyte) (byte) entry.Key) + " | " + entry.Value + " |");

            _output.WriteLine("");
            _output.WriteLine("- distinct values: " + counts.Count);
            _output.WriteLine("- raw maximum: " + counts.Keys.Max() + ", raw minimum: " + counts.Keys.Min());
            _output.WriteLine("- slots at or above " + DisputedProgram + ": " +
                              counts.Where(e => e.Key >= DisputedProgram).Sum(e => e.Value));
            _output.WriteLine("- slots above " + DisputedProgram + ": " +
                              counts.Where(e => e.Key > DisputedProgram).Sum(e => e.Value));
            _output.WriteLine("- any slot with the high bit set, which would decode negative: " +
                              (counts.Keys.Any(v => v >= 128) ? "**yes**" : "no"));
            _output.WriteLine("");
        }

        /// <summary>
        ///     Every column of every slot whose Field1820 reaches the disputed program index or beyond.
        /// </summary>
        /// <param name="table">The decoded table.</param>
        /// <param name="slots">Present slot ids.</param>
        /// <param name="graphed">Index-9 group ids.</param>
        private void WriteHighSlots(MaterialTable table, List<int> slots, HashSet<int> graphed)
        {
            var high = slots.Where(slot =>
                RawValue(table.Slots[slot].StoredRecord, MaterialColumn.Field1820) >= DisputedProgram).ToList();

            _output.WriteLine("## Slots whose Field1820 is " + DisputedProgram + " or higher");
            _output.WriteLine("");

            if (high.Count == 0)
            {
                _output.WriteLine("None in this cache.");
                _output.WriteLine("");
                return;
            }

            _output.WriteLine("Slot ids: " + string.Join(", ", high));
            _output.WriteLine("");

            foreach (int slot in high)
            {
                TextureDefinition def = table.Slots[slot];
                byte[] row = def.StoredRecord;

                _output.WriteLine("### Slot " + slot);
                _output.WriteLine("");
                _output.WriteLine("- index-9 procedural graph declared for this id: " +
                                  (graphed.Contains(slot) ? "**yes**" : "**no**"));
                _output.WriteLine("- stored record: `" + BitConverter.ToString(row).Replace("-", " ") + "`");
                _output.WriteLine("");
                _output.WriteLine("| column | offset | width | stored decimal | stored hex | decoded field |");
                _output.WriteLine("|---|---|---|---|---|---|");

                foreach (MaterialColumn column in Columns)
                {
                    long raw = RawValue(row, column);
                    int width = MaterialTable.WidthOf(column);

                    _output.WriteLine("| " + column + " | " + MaterialTable.OffsetOf(column) + " | " +
                                      width + " | " + raw + " | 0x" + raw.ToString("X" + width * 2) +
                                      " | " + Decoded(def, column) + " |");
                }

                _output.WriteLine("");
                _output.WriteLine("- Field1835 raw: 0x" +
                                  RawValue(row, MaterialColumn.Field1835).ToString("X8") + " = " +
                                  RawValue(row, MaterialColumn.Field1835) + " decimal, decoded " +
                                  def.field1835);
                _output.WriteLine("- Field1816 raw: " + RawValue(row, MaterialColumn.Field1816) +
                                  " = 0x" + RawValue(row, MaterialColumn.Field1816).ToString("X2") +
                                  ", decoded " + def.field1816);
                _output.WriteLine("");
            }
        }

        /// <summary>
        ///     Whether any slot at all carries a non-zero Field1835, independent of Field1820.
        /// </summary>
        /// <remarks>
        ///     The other half of the disagreement. "Field1835 is zero because no slot selects the
        ///     effect that reads it" is only worth arguing about while Field1835 is in fact zero
        ///     everywhere, so the claim is measured here rather than carried over.
        /// </remarks>
        /// <param name="table">The decoded table.</param>
        /// <param name="slots">Present slot ids.</param>
        private void WriteField1835Census(MaterialTable table, List<int> slots)
        {
            var nonZero = new List<int>();
            foreach (int slot in slots)
                if (RawValue(table.Slots[slot].StoredRecord, MaterialColumn.Field1835) != 0)
                    nonZero.Add(slot);

            _output.WriteLine("## Field1835 across the whole table");
            _output.WriteLine("");
            _output.WriteLine("- non-zero in " + nonZero.Count + " of " + slots.Count + " present slots");

            if (nonZero.Count > 0)
                _output.WriteLine("- slots: " + string.Join(", ", nonZero.Take(64)) +
                                  (nonZero.Count > 64 ? ", ..." : ""));

            _output.WriteLine("");
        }

        /// <summary>Renders the field a column decodes to, whatever its type.</summary>
        /// <param name="def">The record.</param>
        /// <param name="column">The column.</param>
        /// <returns>The decoded value as text.</returns>
        private static string Decoded(TextureDefinition def, MaterialColumn column)
        {
            switch (column)
            {
                case MaterialColumn.Field1825: return def.field1825.ToString();
                case MaterialColumn.Field1822: return def.field1822.ToString();
                case MaterialColumn.Field1833: return def.field1833.ToString();
                case MaterialColumn.Field1829: return def.field1829.ToString();
                case MaterialColumn.Field1830: return def.field1830.ToString();
                case MaterialColumn.Field1820: return def.field1820.ToString();
                case MaterialColumn.Field1816: return def.field1816.ToString();
                case MaterialColumn.Field1831: return def.field1831.ToString();
                case MaterialColumn.Field1823: return def.field1823.ToString();
                case MaterialColumn.Field1837: return def.field1837.ToString();
                case MaterialColumn.Field1827: return def.field1827.ToString();
                case MaterialColumn.Field1824: return def.field1824.ToString();
                case MaterialColumn.Field1832: return def.field1832.ToString();
                case MaterialColumn.Field1826: return def.field1826.ToString();
                case MaterialColumn.Field1819: return def.field1819.ToString();
                case MaterialColumn.Field1817: return def.field1817.ToString();
                case MaterialColumn.Field1821: return def.field1821.ToString();
                case MaterialColumn.Field1835: return def.field1835.ToString();
                case MaterialColumn.Field1818: return def.field1818.ToString();

                default:
                    throw new ArgumentOutOfRangeException(nameof(column), column,
                        "The material table has no such column.");
            }
        }
    }
}
