using System;
using System.Collections.Generic;
using System.Linq;
using FlashEditor.cache;
using FlashEditor.Definitions;
using FlashEditor.Definitions.Editing;
using FlashEditor.Definitions.Entities;
using FlashEditor.Tests.Cache.RealCache;
using Xunit;
using Xunit.Abstractions;

namespace FlashEditor.Tests.Definitions {
    /// <summary>
    ///     Editing a bare flag on a real record, which no sweep in this project covered.
    /// </summary>
    /// <remarks>
    ///     The byte-identity sweeps prove an <b>unedited</b> record re-encodes to what it was read
    ///     from. That is a different claim from this one, and the gap is exactly where the Entities
    ///     page put six new editable columns: <c>membersOnly</c>, <c>clickable</c>,
    ///     <c>drawMinimapDot</c>, <c>visiblePriority</c>, <c>walkable</c> and <c>isClipped</c>.
    ///     <para>
    ///     <b>A bare flag has no payload.</b> Its whole meaning is whether its opcode is in the
    ///     stream, so setting one adds or drops a byte and changes the record's length. Nothing else
    ///     in the editor changes a record's length from a single cell edit.
    ///     </para>
    ///     <para>
    ///     The third case below is the one that matters and the one that found a defect: set the
    ///     flag, set it back, and land on the <b>original stored bytes</b>. An asymmetric setter
    ///     passes the first two checks and fails only this. It did:
    ///     <c>NPCDefinition.DropOpcode</c> and <c>ObjectDefinition.DropOpcode</c> removed the opcode
    ///     from the recorded stream, which threw its position away, so turning the flag back on
    ///     re-emitted it at the end of the record. A definition of the right length with a byte
    ///     moved, which <c>DefinitionListPanel.CommitEdit</c> then staged as a real change - and an
    ///     archive CRC covers the stored bytes, so that drags in the reference-table entry of every
    ///     archive packed alongside it, for an edit that netted nothing.
    ///     </para>
    ///     <para>
    ///     Both directions are exercised against real records rather than only the direction each
    ///     cache happens to supply. Where a cache holds no record carrying a flag the case is built
    ///     synthetically from a real one instead of skipped, because an absent input is where this
    ///     project's sweeps have been proven blind more than once.
    ///     </para>
    /// </remarks>
    public sealed class RealCacheBareFlagEditTests : IClassFixture<RealCacheFixture> {
        private readonly RealCacheFixture _cache;
        private readonly ITestOutputHelper _output;

        public RealCacheBareFlagEditTests(RealCacheFixture cache, ITestOutputHelper output) {
            _cache = cache;
            _output = output;
        }

        /// <summary>
        ///     Opcodes that carry no payload and are not an editable flag, so nothing here covers them.
        /// </summary>
        /// <remarks>
        ///     Each is a bare opcode with a side effect rather than a flag: 21 and 94 select a
        ///     contour ground type, 23 and 103 an obstruction mode, 27 a clip type. They have no
        ///     property because the value they set is spelled by a family of opcodes rather than by
        ///     presence alone, so an "on/off" edit is not defined for them.
        ///     <para>
        ///     Stated as an exemption list rather than left out, because
        ///     <see cref="EveryBareOpcodeInTheCacheIsCoveredOrExempt"/> reads it: a bare flag added
        ///     to either codec later fails that test until it is either covered here or listed
        ///     here, which is what stops this file drifting back to partial coverage of a rule that
        ///     is shared by every flag.
        ///     </para>
        /// </remarks>
        private static readonly int[] BareOpcodesThatAreNotFlags = { 21, 23, 27, 94, 103 };

        /// <summary>
        ///     Opcodes spelled by a paired-flag property, covered by <c>RealCachePairedFlagEditTests</c>.
        /// </summary>
        /// <remarks>
        ///     <c>ObjectDefinition.walkable</c> is opcode 17 <i>or</i> 18 and
        ///     <c>NPCDefinition.mainOptionIndex</c> is 158 <i>or</i> 159, so neither is a single
        ///     opcode's presence and neither fits the three checks here. Both have already produced
        ///     a defect apiece, which is why they get a file of their own rather than an entry with
        ///     a caveat.
        /// </remarks>
        private static readonly int[] OpcodesCoveredByPairedFlags = { 17, 18, 158, 159 };

        /// <summary>
        ///     Setting a flag on a record that lacks it adds exactly its opcode, and nothing else moves.
        /// </summary>
        [RealCacheFact]
        public void SettingAFlagOnARecordThatLacksItAddsExactlyThatOpcode() {
            foreach (BareFlag flag in Flags())
                CheckTurningOn(flag);
        }

        /// <summary>
        ///     Clearing a flag on a record that carries it drops exactly its opcode, and nothing else moves.
        /// </summary>
        [RealCacheFact]
        public void ClearingAFlagOnARecordThatCarriesItDropsExactlyThatOpcode() {
            foreach (BareFlag flag in Flags())
                CheckTurningOff(flag);
        }

        /// <summary>
        ///     A flag set and set back lands on the original stored bytes, in both directions.
        /// </summary>
        /// <remarks>
        ///     The assertion an asymmetric setter cannot pass. Compared against the bytes the cache
        ///     stores rather than against a re-encode taken before the edit, because a re-encode
        ///     compared with itself would agree with a setter that moved the opcode both times.
        /// </remarks>
        [RealCacheFact]
        public void AFlagSetAndSetBackLandsOnTheOriginalStoredBytes() {
            foreach (BareFlag flag in Flags())
                CheckRoundTrip(flag);
        }

        /// <summary>
        ///     A flag whose opcode the file carries twice drops both occurrences and restores both.
        /// </summary>
        /// <remarks>
        ///     A real shape rather than a hypothetical: measured over index 16, identically in both
        ///     caches, 49 objects carry opcode 22 twice, 19 carry 64 twice, 12 each carry 73 and 88
        ///     twice, 8 carry 62 twice, 5 carry 74 twice and one each carry 17 and 89 twice.
        ///     <para>
        ///     It is the case that separates suppressing an opcode from dropping its last
        ///     occurrence. Turning the flag off has to drop <b>every</b> occurrence, or the client
        ///     still reads the flag as set while the grid says otherwise; turning it back on has to
        ///     restore every occurrence in place, or the record comes back a byte short. Neither
        ///     direction is a single-byte change, so the checks above do not apply and none of them
        ///     would fail if this went wrong.
        ///     </para>
        /// </remarks>
        [RealCacheFact]
        public void AFlagCarriedTwiceDropsBothOccurrencesAndRestoresBoth() {
            RSCache cache = _cache.OpenCache();
            int checkedFlags = 0;

            foreach (BareFlag flag in Flags()) {
                OpcodeFlagSurvey survey = OpcodeFlagSurvey.For(cache, flag.Descriptor);
                if (survey.Repeating(flag.Opcode).Count == 0) {
                    _output.WriteLine($"{flag.Name}: no record carries opcode {flag.Opcode} twice in this cache");
                    continue;
                }

                (byte[] stored, object row, DefinitionAddress address) =
                    FirstRecordThatReEncodes(cache, flag.Descriptor, survey.Repeating(flag.Opcode));

                int occurrences = Occurrences(row, flag.Opcode);
                Assert.True(occurrences > 1, "The survey named a record that does not repeat the opcode.");

                bool original = flag.Get(row);
                flag.Set(row, !original);
                byte[] dropped = flag.Descriptor.Encode(row).ToArray();

                //Every occurrence, not just the last. One left behind still spells the flag.
                Assert.Equal(stored.Length - occurrences, dropped.Length);
                AssertDiffersOnlyByOpcode(stored, dropped, flag.Opcode, added: false, times: occurrences);
                Assert.Equal(0, Occurrences(Redecode(flag, dropped), flag.Opcode));

                flag.Set(row, original);
                Assert.Equal(stored, flag.Descriptor.Encode(row).ToArray());

                checkedFlags++;
                _output.WriteLine($"{flag.Name}: opcode {flag.Opcode} occurs {occurrences} times at {address} " +
                                  $"({survey.RepeatedBy(flag.Opcode)} such records in this cache); " +
                                  "both dropped and both restored");
            }

            Assert.True(checkedFlags > 0,
                "No flag opcode is carried twice by any record, so this cache cannot exercise the case at all.");
        }

        /// <summary>
        ///     Every payload-free opcode the cache actually carries is either edit-tested or listed
        ///     as not a flag.
        /// </summary>
        /// <remarks>
        ///     The gate that stops this file drifting back to partial coverage. The rule under test
        ///     lives in <c>OpcodeStream.Replay</c> and is shared by every bare flag, so a flag added
        ///     to either codec later inherits a rule nothing exercises for it - which is how a
        ///     whole-index sweep over 431,558 packets once passed against a deliberately broken
        ///     shared length rule, no shipped record having reached the boundary.
        ///     <para>
        ///     Derived from the data rather than from the source: an opcode counts as bare when
        ///     every occurrence of it in the index consumed no payload. Items are out of scope
        ///     because their flags are driven by the field alone and never touch the recorded
        ///     stream.
        ///     </para>
        /// </remarks>
        [RealCacheFact]
        public void EveryBareOpcodeInTheCacheIsCoveredOrExempt() {
            RSCache cache = _cache.OpenCache();
            var covered = new HashSet<string>();
            foreach (BareFlag flag in Flags())
                covered.Add(flag.Descriptor.GetType().Name + ":" + flag.Opcode);

            foreach (IDefinitionListDescriptor descriptor in
                     new IDefinitionListDescriptor[] { new NPCListDescriptor(), new ObjectListDescriptor() }) {
                OpcodeFlagSurvey survey = OpcodeFlagSurvey.For(cache, descriptor);
                var uncovered = new List<int>();
                var found = new List<int>();

                for (int opcode = 0; opcode < 256; opcode++) {
                    if (survey.CarriedBy(opcode) == 0 || !survey.IsAlwaysBare(opcode))
                        continue;

                    found.Add(opcode);
                    if (covered.Contains(descriptor.GetType().Name + ":" + opcode))
                        continue;
                    if (BareOpcodesThatAreNotFlags.Contains(opcode) || OpcodesCoveredByPairedFlags.Contains(opcode))
                        continue;
                    uncovered.Add(opcode);
                }

                _output.WriteLine($"{descriptor.RowNoun}: payload-free opcodes carried by this cache: " +
                                  string.Join(", ", found));
                Assert.True(uncovered.Count == 0,
                    $"{descriptor.RowNoun} opcodes {string.Join(", ", uncovered)} carry no payload and no test edits " +
                    "them. Add a BareFlag entry, or list the opcode in BareOpcodesThatAreNotFlags with the reason.");
            }
        }

        /// <summary>
        ///     Every bare-flag property whose whole meaning is one opcode's presence.
        /// </summary>
        /// <remarks>
        ///     All of them, not a sample. The rule they share lives in <c>OpcodeStream.Replay</c>,
        ///     and a shared rule tested on a quarter of its callers is the shape that has already
        ///     failed twice in this project: the first version of this file covered six of them and
        ///     the two defects it found were both in properties it happened to include.
        ///     <para>
        ///     Both polarities are represented in every family, because the inverted ones are where
        ///     an off-by-one in a setter hides - the obvious reading of "drop the opcode" is wrong
        ///     for a flag whose presence means <i>false</i>.
        ///     </para>
        ///     <para>
        ///     The two paired-opcode properties are deliberately absent; see
        ///     <see cref="OpcodesCoveredByPairedFlags"/>.
        ///     </para>
        /// </remarks>
        private static IEnumerable<BareFlag> Flags() {
            //Item opcode 16. The item codec drives its bare flags from the field alone and never
            //touches the recorded stream, which is why these two were already symmetric.
            yield return new BareFlag("item.membersOnly", new ItemListDescriptor(), 16,
                presenceMeansTrue: true,
                row => ((ItemDefinition) row).membersOnly,
                (row, value) => ((ItemDefinition) row).membersOnly = value);

            //Item opcode 11, stored as an int rather than a bool, so the column writes 1 and 0.
            yield return new BareFlag("item.stackable", new ItemListDescriptor(), 11,
                presenceMeansTrue: true,
                row => ((ItemDefinition) row).stackable == 1,
                (row, value) => ((ItemDefinition) row).stackable = value ? 1 : 0);

            foreach (BareFlag flag in NpcFlags())
                yield return flag;

            foreach (BareFlag flag in ObjectFlags())
                yield return flag;
        }

        /// <summary>Every single-opcode bare flag on <see cref="NPCDefinition"/>.</summary>
        /// <remarks>
        ///     Four of the seven are inverted: 93, 107, 109 and 111 all read <i>false</i> when the
        ///     opcode is present.
        /// </remarks>
        private static IEnumerable<BareFlag> NpcFlags() {
            yield return new BareFlag("npc.drawMinimapDot", new NPCListDescriptor(), 93,
                presenceMeansTrue: false,
                row => ((NPCDefinition) row).drawMinimapDot,
                (row, value) => ((NPCDefinition) row).drawMinimapDot = value);

            yield return new BareFlag("npc.hasRenderPriority", new NPCListDescriptor(), 99,
                presenceMeansTrue: true,
                row => ((NPCDefinition) row).hasRenderPriority,
                (row, value) => ((NPCDefinition) row).hasRenderPriority = value);

            //Inverted: carrying 107 makes the NPC unclickable.
            yield return new BareFlag("npc.clickable", new NPCListDescriptor(), 107,
                presenceMeansTrue: false,
                row => ((NPCDefinition) row).clickable,
                (row, value) => ((NPCDefinition) row).clickable = value);

            yield return new BareFlag("npc.slowWalk", new NPCListDescriptor(), 109,
                presenceMeansTrue: false,
                row => ((NPCDefinition) row).slowWalk,
                (row, value) => ((NPCDefinition) row).slowWalk = value);

            yield return new BareFlag("npc.animateIdle", new NPCListDescriptor(), 111,
                presenceMeansTrue: false,
                row => ((NPCDefinition) row).animateIdle,
                (row, value) => ((NPCDefinition) row).animateIdle = value);

            yield return new BareFlag("npc.visiblePriority", new NPCListDescriptor(), 141,
                presenceMeansTrue: true,
                row => ((NPCDefinition) row).visiblePriority,
                (row, value) => ((NPCDefinition) row).visiblePriority = value);

            yield return new BareFlag("npc.invisiblePriority", new NPCListDescriptor(), 143,
                presenceMeansTrue: true,
                row => ((NPCDefinition) row).invisiblePriority,
                (row, value) => ((NPCDefinition) row).invisiblePriority = value);
        }

        /// <summary>Every single-opcode bare flag on <see cref="ObjectDefinition"/>.</summary>
        /// <remarks>
        ///     Five of the eighteen - 90, 96, 105, 177 and 189 - are carried by no object in either
        ///     cache, so their "carries the opcode" case is built rather than found and the test
        ///     says which. Building it is the point: an opcode absent from the data is exactly
        ///     where a codec change goes unnoticed.
        /// </remarks>
        private static IEnumerable<BareFlag> ObjectFlags() {
            yield return new BareFlag("object.isClipped", new ObjectListDescriptor(), 22,
                presenceMeansTrue: true,
                row => ((ObjectDefinition) row).isClipped,
                (row, value) => ((ObjectDefinition) row).isClipped = value);

            yield return new BareFlag("object.flipped", new ObjectListDescriptor(), 62,
                presenceMeansTrue: true,
                row => ((ObjectDefinition) row).flipped,
                (row, value) => ((ObjectDefinition) row).flipped = value);

            //Inverted: carrying 64 suppresses the shadow.
            yield return new BareFlag("object.castsShadow", new ObjectListDescriptor(), 64,
                presenceMeansTrue: false,
                row => ((ObjectDefinition) row).castsShadow,
                (row, value) => ((ObjectDefinition) row).castsShadow = value);

            yield return new BareFlag("object.obstructsWheelchair", new ObjectListDescriptor(), 73,
                presenceMeansTrue: true,
                row => ((ObjectDefinition) row).obstructsWheelchair,
                (row, value) => ((ObjectDefinition) row).obstructsWheelchair = value);

            yield return new BareFlag("object.isSolid", new ObjectListDescriptor(), 74,
                presenceMeansTrue: true,
                row => ((ObjectDefinition) row).isSolid,
                (row, value) => ((ObjectDefinition) row).isSolid = value);

            yield return new BareFlag("object.mergeNormals", new ObjectListDescriptor(), 82,
                presenceMeansTrue: true,
                row => ((ObjectDefinition) row).mergeNormals,
                (row, value) => ((ObjectDefinition) row).mergeNormals = value);

            yield return new BareFlag("object.noShadow", new ObjectListDescriptor(), 88,
                presenceMeansTrue: true,
                row => ((ObjectDefinition) row).noShadow,
                (row, value) => ((ObjectDefinition) row).noShadow = value);

            yield return new BareFlag("object.noDecor", new ObjectListDescriptor(), 89,
                presenceMeansTrue: true,
                row => ((ObjectDefinition) row).noDecor,
                (row, value) => ((ObjectDefinition) row).noDecor = value);

            yield return new BareFlag("object.unknownFlag90", new ObjectListDescriptor(), 90,
                presenceMeansTrue: true,
                row => ((ObjectDefinition) row).unknownFlag90,
                (row, value) => ((ObjectDefinition) row).unknownFlag90 = value);

            yield return new BareFlag("object.unknownFlag91", new ObjectListDescriptor(), 91,
                presenceMeansTrue: true,
                row => ((ObjectDefinition) row).unknownFlag91,
                (row, value) => ((ObjectDefinition) row).unknownFlag91 = value);

            yield return new BareFlag("object.unknownFlag96", new ObjectListDescriptor(), 96,
                presenceMeansTrue: true,
                row => ((ObjectDefinition) row).unknownFlag96,
                (row, value) => ((ObjectDefinition) row).unknownFlag96 = value);

            yield return new BareFlag("object.unknownFlag97", new ObjectListDescriptor(), 97,
                presenceMeansTrue: true,
                row => ((ObjectDefinition) row).unknownFlag97,
                (row, value) => ((ObjectDefinition) row).unknownFlag97 = value);

            yield return new BareFlag("object.unknownFlag98", new ObjectListDescriptor(), 98,
                presenceMeansTrue: true,
                row => ((ObjectDefinition) row).unknownFlag98,
                (row, value) => ((ObjectDefinition) row).unknownFlag98 = value);

            yield return new BareFlag("object.unknownFlag105", new ObjectListDescriptor(), 105,
                presenceMeansTrue: true,
                row => ((ObjectDefinition) row).unknownFlag105,
                (row, value) => ((ObjectDefinition) row).unknownFlag105 = value);

            yield return new BareFlag("object.unknownFlag168", new ObjectListDescriptor(), 168,
                presenceMeansTrue: true,
                row => ((ObjectDefinition) row).unknownFlag168,
                (row, value) => ((ObjectDefinition) row).unknownFlag168 = value);

            yield return new BareFlag("object.unknownFlag169", new ObjectListDescriptor(), 169,
                presenceMeansTrue: true,
                row => ((ObjectDefinition) row).unknownFlag169,
                (row, value) => ((ObjectDefinition) row).unknownFlag169 = value);

            yield return new BareFlag("object.unknownFlag177", new ObjectListDescriptor(), 177,
                presenceMeansTrue: true,
                row => ((ObjectDefinition) row).unknownFlag177,
                (row, value) => ((ObjectDefinition) row).unknownFlag177 = value);

            yield return new BareFlag("object.unknownFlag189", new ObjectListDescriptor(), 189,
                presenceMeansTrue: true,
                row => ((ObjectDefinition) row).unknownFlag189,
                (row, value) => ((ObjectDefinition) row).unknownFlag189 = value);
        }

        private void CheckTurningOn(BareFlag flag) {
            (byte[] stored, object row) = FindRecord(flag, carriesOpcode: false, out string how);

            flag.Set(row, !flag.Get(row));
            byte[] edited = flag.Descriptor.Encode(row).ToArray();

            //Exactly one byte longer, and that byte is the opcode. Comparing lengths alone would
            //pass a setter that added the right count of the wrong byte.
            Assert.Equal(stored.Length + 1, edited.Length);
            AssertDiffersOnlyByOpcode(stored, edited, flag.Opcode, added: true);

            object decoded = Redecode(flag, edited);
            Assert.Equal(flag.Get(row), flag.Get(decoded));
            AssertEveryOtherFieldMatches(flag, stored, edited, decoded);

            _output.WriteLine($"{flag.Name}: turning on added opcode {flag.Opcode}, " +
                              $"{stored.Length} -> {edited.Length} bytes ({how})");
        }

        private void CheckTurningOff(BareFlag flag) {
            (byte[] stored, object row) = FindRecord(flag, carriesOpcode: true, out string how);

            flag.Set(row, !flag.Get(row));
            byte[] edited = flag.Descriptor.Encode(row).ToArray();

            Assert.Equal(stored.Length - 1, edited.Length);
            AssertDiffersOnlyByOpcode(stored, edited, flag.Opcode, added: false);

            object decoded = Redecode(flag, edited);
            Assert.Equal(flag.Get(row), flag.Get(decoded));
            AssertEveryOtherFieldMatches(flag, stored, edited, decoded);

            _output.WriteLine($"{flag.Name}: turning off dropped opcode {flag.Opcode}, " +
                              $"{stored.Length} -> {edited.Length} bytes ({how})");
        }

        private void CheckRoundTrip(BareFlag flag) {
            foreach (bool carries in new[] { true, false }) {
                (byte[] stored, object row) = FindRecord(flag, carries, out string how);

                bool original = flag.Get(row);
                flag.Set(row, !original);
                byte[] moved = flag.Descriptor.Encode(row).ToArray();
                Assert.NotEqual(stored, moved);

                flag.Set(row, original);
                byte[] returned = flag.Descriptor.Encode(row).ToArray();

                Assert.Equal(stored, returned);
                _output.WriteLine($"{flag.Name}: round trip on a record that " +
                                  (carries ? "carries" : "lacks") +
                                  $" opcode {flag.Opcode} returns {returned.Length} identical bytes ({how})");
            }
        }

        /// <summary>
        ///     The two byte strings differ by exactly one occurrence of one opcode byte.
        /// </summary>
        /// <remarks>
        ///     Checked as a subsequence rather than by scanning for the byte: an opcode byte is not
        ///     distinguishable from a payload byte of the same value, so counting occurrences would
        ///     be wrong for any record whose payload happens to contain it. Walking the two in step
        ///     and allowing exactly one skip at the point they diverge is what identifies the
        ///     inserted or deleted byte unambiguously.
        /// </remarks>
        private static void AssertDiffersOnlyByOpcode(byte[] stored, byte[] edited, int opcode, bool added,
            int times = 1) {
            byte[] longer = added ? edited : stored;
            byte[] shorter = added ? stored : edited;

            int inShorter = 0;
            int inLonger = 0;
            int skipped = 0;

            while (inShorter < shorter.Length) {
                Assert.True(inLonger < longer.Length,
                    "The shorter encoding is not the longer one with opcode bytes removed.");

                if (shorter[inShorter] == longer[inLonger]) {
                    inShorter++;
                    inLonger++;
                    continue;
                }

                Assert.Equal(opcode, longer[inLonger]);
                inLonger++;
                skipped++;
                Assert.True(skipped <= times,
                    $"More than {times} byte(s) differ, so something other than opcode {opcode} moved.");
            }

            //Whatever is left in the longer encoding can only be the remaining skips, or the two
            //aligned in a way that hid a real difference behind them.
            while (inLonger < longer.Length) {
                Assert.Equal(opcode, longer[inLonger]);
                inLonger++;
                skipped++;
            }

            Assert.Equal(times, skipped);
        }

        /// <summary>
        ///     Everything the record says apart from the flag is unchanged.
        /// </summary>
        /// <remarks>
        ///     Asserted by decoding the edited bytes and re-encoding <i>that</i>, which has to
        ///     reproduce the edited bytes exactly. A field the edit corrupted would decode to
        ///     something else and re-encode differently, and unlike a field-by-field comparison this
        ///     covers every field the codec has rather than the ones a test author remembered.
        /// </remarks>
        private static void AssertEveryOtherFieldMatches(BareFlag flag, byte[] stored, byte[] edited, object decoded) {
            Assert.Equal(edited, flag.Descriptor.Encode(decoded).ToArray());
            Assert.NotEqual(stored, edited);
        }

        private object Redecode(BareFlag flag, byte[] bytes) {
            RSCache cache = _cache.OpenCache();
            //Any address of the right index: the address decides the id the row is stamped with and
            //nothing else, and nothing here compares ids.
            DefinitionAddress address = flag.Descriptor.Enumerate(cache).First();
            return flag.Descriptor.Decode(cache, address, new JagStream(bytes));
        }

        /// <summary>
        ///     A real record that does, or does not, carry the flag's opcode.
        /// </summary>
        /// <remarks>
        ///     Taken from a survey of the <b>whole</b> index rather than of the first few groups.
        ///     The sampled search this replaced could not tell "this cache holds no such record"
        ///     from "the sample missed it", and for the rarest flags it always missed: object
        ///     opcode 97 is carried by 9 records of 56,199 and 169 by 21, so both were tested
        ///     against a synthetic record while a real one existed. Which records carry which flag
        ///     is a property of the cache, so it is measured rather than named by id.
        ///     <para>
        ///     Only where the index genuinely holds no record of the wanted shape is one
        ///     <b>built</b> from a real one, and the test says so. Skipping instead would leave the
        ///     direction that is absent from this cache untested in this cache, which is the shape
        ///     of hole this project keeps finding.
        ///     </para>
        /// </remarks>
        private (byte[] Stored, object Row) FindRecord(BareFlag flag, bool carriesOpcode, out string how) {
            RSCache cache = _cache.OpenCache();
            IDefinitionListDescriptor descriptor = flag.Descriptor;
            OpcodeFlagSurvey survey = OpcodeFlagSurvey.For(cache, descriptor);

            //Records carrying the opcode once. A record carrying it twice is a different shape and
            //has its own test, because dropping the flag there changes the length by two.
            IReadOnlyList<DefinitionAddress> wanted = carriesOpcode
                ? survey.Carrying(flag.Opcode)
                : survey.Lacking(flag.Opcode);

            if (wanted.Count > 0) {
                (byte[] stored, object row, DefinitionAddress address) =
                    FirstRecordThatReEncodes(cache, descriptor, wanted);

                //A second reading of the same fact: the survey went by the recorded opcode stream
                //and this goes by the flag's own getter. They cannot disagree unless one of the two
                //is wrong about what the record says.
                Assert.Equal(carriesOpcode, CarriesOpcode(row, flag));
                how = "found at " + address + ", " + survey.CarriedBy(flag.Opcode) + " of " +
                      survey.RecordsExamined + " records carry opcode " + flag.Opcode;
                return (stored, row);
            }

            IReadOnlyList<DefinitionAddress> seeds = carriesOpcode
                ? survey.Lacking(flag.Opcode)
                : survey.Carrying(flag.Opcode);
            (byte[] _, object seed, DefinitionAddress seedAddress) =
                FirstRecordThatReEncodes(cache, descriptor, seeds);

            //Built rather than skipped. Toggling the flag on a real record produces exactly the
            //shape the index does not hold, and the built bytes are proven by decoding them back.
            flag.Set(seed, carriesOpcode == flag.PresenceMeansTrue);
            byte[] built = descriptor.Encode(seed).ToArray();
            object rebuilt = Redecode(flag, built);

            Assert.Equal(carriesOpcode, CarriesOpcode(rebuilt, flag));
            how = "built from " + seedAddress + " - no " + descriptor.RowNoun +
                  " in this cache carries opcode " + flag.Opcode;
            return (built, rebuilt);
        }

        /// <summary>
        ///     The first of the candidate addresses whose record re-encodes to its stored bytes.
        /// </summary>
        /// <remarks>
        ///     A record has to re-encode to what it was read from before it is any use as a
        ///     baseline. Every one does - that is what the byte-identity sweeps assert - so several
        ///     candidates are offered only so that a bad pick reads as a bad pick, and running out
        ///     of them fails loudly rather than falling through to a synthetic record and hiding a
        ///     codec regression behind a test that still passes.
        /// </remarks>
        private static (byte[] Stored, object Row, DefinitionAddress Address) FirstRecordThatReEncodes(
            RSCache cache, IDefinitionListDescriptor descriptor, IReadOnlyList<DefinitionAddress> candidates) {
            Assert.True(candidates.Count > 0,
                $"The survey offered no {descriptor.RowNoun} of the shape this case needs.");

            foreach (DefinitionAddress address in candidates) {
                byte[] stored = cache.ReadFileBytes(descriptor.IndexId, address.GroupId, address.FileId);
                object row = descriptor.Decode(cache, address, new JagStream(stored));
                if (!descriptor.Encode(row).ToArray().SequenceEqual(stored))
                    continue;

                //Decoded a second time from a fresh stream, because the row above has already been
                //re-encoded once and an encode is not required to leave a definition untouched.
                return (stored, descriptor.Decode(cache, address, new JagStream(stored)), address);
            }

            Assert.Fail($"None of the {candidates.Count} candidate {descriptor.RowNoun} records re-encoded to its " +
                        "stored bytes, which is a codec regression rather than a bad pick.");
            return default;
        }

        /// <summary>How many times the record carries an opcode, read off its recorded stream.</summary>
        /// <param name="row">The decoded record.</param>
        /// <param name="opcode">The opcode to count.</param>
        /// <returns>The occurrence count.</returns>
        private static int Occurrences(object row, int opcode) {
            OpcodeStream stream = ((OpcodeStreamDefinition) row).Opcodes;
            int count = 0;
            for (int i = 0; i < stream.Count; i++)
                if (stream[i].Opcode == opcode)
                    count++;
            return count;
        }

        /// <summary>
        ///     Whether the stored bytes carry the flag's opcode, read off the decoded record.
        /// </summary>
        /// <remarks>
        ///     Taken from the flag's own value rather than by scanning the bytes for the opcode
        ///     number, which cannot be told apart from a payload byte of the same value. Each flag
        ///     states which way its opcode reads, because half of them are inverted - the opcode's
        ///     presence is what makes an NPC <i>un</i>clickable.
        /// </remarks>
        private static bool CarriesOpcode(object row, BareFlag flag) {
            return flag.Get(row) == flag.PresenceMeansTrue;
        }

        /// <summary>One editable bare flag, and how to read and write it without knowing its type.</summary>
        private sealed class BareFlag {
            internal BareFlag(string name, IDefinitionListDescriptor descriptor, int opcode,
                bool presenceMeansTrue, Func<object, bool> get, Action<object, bool> set) {
                Name = name;
                Descriptor = descriptor;
                Opcode = opcode;
                PresenceMeansTrue = presenceMeansTrue;
                Get = get;
                Set = set;
            }

            /// <summary>Family and field, for the failure message.</summary>
            internal string Name { get; }

            /// <summary>The descriptor the entity page shows this family through.</summary>
            internal IDefinitionListDescriptor Descriptor { get; }

            /// <summary>The opcode whose presence is the whole of the flag.</summary>
            internal int Opcode { get; }

            /// <summary>
            ///     Whether carrying the opcode makes the flag read true.
            /// </summary>
            /// <remarks>
            ///     Half of these are inverted and it is not guessable from the name: NPC 107 makes
            ///     the NPC <i>un</i>clickable and object 64 <i>suppresses</i> the shadow, so both
            ///     read false when the opcode is present. Stating it per flag is what lets the
            ///     search ask for a record of a given shape without scanning the bytes for an
            ///     opcode number, which cannot be told apart from a payload byte of the same value.
            /// </remarks>
            internal bool PresenceMeansTrue { get; }

            /// <summary>Reads the flag off a row.</summary>
            internal Func<object, bool> Get { get; }

            /// <summary>Writes the flag onto a row.</summary>
            internal Action<object, bool> Set { get; }
        }
    }
}
