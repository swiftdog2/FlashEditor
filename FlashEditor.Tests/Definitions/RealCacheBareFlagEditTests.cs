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

        /// <summary>How many groups are searched for a record carrying, and a record lacking, each flag.</summary>
        /// <remarks>
        ///     Enough to find both cases without sweeping the index. Both are reported, and the test
        ///     fails rather than skips if either is missing after the search, so a shrinking search
        ///     cannot quietly turn this into a one-sided check.
        /// </remarks>
        private const int GroupsSearched = 6;

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
        ///     One flag per family, since the three are three separate opcode tables.
        /// </summary>
        /// <remarks>
        ///     Six are covered rather than three: two per family, one whose opcode's presence means
        ///     <i>true</i> and one whose presence means <i>false</i>. The inverted ones are where an
        ///     off-by-one in a setter hides, because the obvious reading of "drop the opcode" is
        ///     wrong for them.
        /// </remarks>
        private static IEnumerable<BareFlag> Flags() {
            //Item opcode 16. The item codec drives its bare flags from the field alone and never
            //touches the recorded stream, which is why these three were already symmetric.
            yield return new BareFlag("item.membersOnly", new ItemListDescriptor(), 16,
                presenceMeansTrue: true,
                row => ((ItemDefinition) row).membersOnly,
                (row, value) => ((ItemDefinition) row).membersOnly = value);

            //Item opcode 11, stored as an int rather than a bool, so the column writes 1 and 0.
            yield return new BareFlag("item.stackable", new ItemListDescriptor(), 11,
                presenceMeansTrue: true,
                row => ((ItemDefinition) row).stackable == 1,
                (row, value) => ((ItemDefinition) row).stackable = value ? 1 : 0);

            //NPC opcode 107, inverted: carrying it makes the NPC unclickable.
            yield return new BareFlag("npc.clickable", new NPCListDescriptor(), 107,
                presenceMeansTrue: false,
                row => ((NPCDefinition) row).clickable,
                (row, value) => ((NPCDefinition) row).clickable = value);

            //NPC opcode 141, not inverted.
            yield return new BareFlag("npc.visiblePriority", new NPCListDescriptor(), 141,
                presenceMeansTrue: true,
                row => ((NPCDefinition) row).visiblePriority,
                (row, value) => ((NPCDefinition) row).visiblePriority = value);

            //Object opcode 22, not inverted.
            yield return new BareFlag("object.isClipped", new ObjectListDescriptor(), 22,
                presenceMeansTrue: true,
                row => ((ObjectDefinition) row).isClipped,
                (row, value) => ((ObjectDefinition) row).isClipped = value);

            //Object opcode 64, inverted: carrying it suppresses the shadow.
            yield return new BareFlag("object.castsShadow", new ObjectListDescriptor(), 64,
                presenceMeansTrue: false,
                row => ((ObjectDefinition) row).castsShadow,
                (row, value) => ((ObjectDefinition) row).castsShadow = value);
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
        private static void AssertDiffersOnlyByOpcode(byte[] stored, byte[] edited, int opcode, bool added) {
            byte[] longer = added ? edited : stored;
            byte[] shorter = added ? stored : edited;

            int at = 0;
            while (at < shorter.Length && shorter[at] == longer[at])
                at++;

            Assert.True(at < longer.Length,
                "The two encodings never diverged, so no opcode was added or dropped.");
            Assert.Equal(opcode, longer[at]);

            for (int i = at; i < shorter.Length; i++)
                Assert.Equal(shorter[i], longer[i + 1]);
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
        ///     Searched rather than named by id, because which records carry which bare flag is a
        ///     property of the cache and the two caches differ on eleven indexes. If the search
        ///     finds no record of the wanted shape, one is <b>built</b> from a real record by
        ///     toggling the flag and re-encoding, and the test says so - skipping would leave the
        ///     direction that is absent from this cache untested in this cache, which is the shape
        ///     of hole this project keeps finding.
        /// </remarks>
        private (byte[] Stored, object Row) FindRecord(BareFlag flag, bool carriesOpcode, out string how) {
            RSCache cache = _cache.OpenCache();
            IDefinitionListDescriptor descriptor = flag.Descriptor;

            List<DefinitionAddress> addresses = descriptor.Enumerate(cache).ToList();
            int[] groups = addresses.Select(address => address.GroupId).Distinct().Take(GroupsSearched).ToArray();

            object fallbackRow = null;
            byte[] fallbackStored = null;

            foreach (int group in groups) {
                IReadOnlyDictionary<int, JagStream> files = cache.ReadGroup(descriptor.IndexId, group);

                foreach (DefinitionAddress address in addresses.Where(a => a.GroupId == group)) {
                    if (!files.TryGetValue(address.FileId, out JagStream payload))
                        continue;

                    byte[] stored = cache.ReadFileBytes(descriptor.IndexId, address.GroupId, address.FileId);
                    object row = descriptor.Decode(cache, address, payload);

                    //The record has to re-encode to what it was read from before it is any use as a
                    //baseline. Every one does - that is what the sweeps assert - so a failure here
                    //is a codec regression rather than a bad pick.
                    if (!descriptor.Encode(row).ToArray().SequenceEqual(stored))
                        continue;

                    if (CarriesOpcode(row, flag) == carriesOpcode) {
                        how = "found at " + address;
                        //Decoded a second time from a fresh stream: the first decode left the
                        //payload's position at the terminator, and the row above has already been
                        //re-encoded once to check it against the cache.
                        return (stored, descriptor.Decode(cache, address, new JagStream(stored)));
                    }

                    fallbackRow ??= row;
                    fallbackStored ??= stored;
                }
            }

            Assert.True(fallbackStored != null,
                $"No {descriptor.RowNoun} in the first {GroupsSearched} groups re-encoded to its stored bytes.");

            //Built rather than skipped. Toggling the flag on a real record produces exactly the
            //shape the search could not find, and the built bytes are proven by decoding them back.
            flag.Set(fallbackRow, carriesOpcode == flag.PresenceMeansTrue);
            byte[] built = descriptor.Encode(fallbackRow).ToArray();
            object rebuilt = Redecode(flag, built);

            Assert.Equal(carriesOpcode, CarriesOpcode(rebuilt, flag));
            how = "built synthetically - this cache holds no such record in the searched groups";
            return (built, rebuilt);
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
