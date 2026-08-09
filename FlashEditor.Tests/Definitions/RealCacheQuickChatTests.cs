using System.Collections.Generic;
using System.Linq;
using FlashEditor.Cache;
using FlashEditor.Definitions.QuickChat;
using FlashEditor.Tests.Cache.RealCache;
using Xunit;
using Xunit.Abstractions;

namespace FlashEditor.Tests.Definitions
{
    /// <summary>
    ///     Sweeps both quick-chat banks: every menu record and every message record the reference
    ///     tables of indexes 24 and 25 declare must decode, consume its buffer exactly, and
    ///     re-encode to the bytes it came from.
    /// </summary>
    /// <remarks>
    ///     Both indexes are swept by every test here rather than only the larger one, because the
    ///     two are the same format in two id namespaces and each exercises what the other does not:
    ///     index 24 is the only bank whose messages carry substitution slots, and index 25 is the
    ///     only one carrying a record whose opcodes are not in ascending order.
    ///     <para>
    ///     Every population is read off the reference table on each run. Nothing here states how
    ///     many records a group holds - the claim being made is "every record the table declares",
    ///     which is a relationship that holds in any cache, and a literal would instead be a fact
    ///     about whichever cache it was measured on.
    ///     </para>
    /// </remarks>
    [Collection("RealCache")]
    public sealed class RealCacheQuickChatTests : IClassFixture<RealCacheFixture>
    {
        private readonly RealCacheFixture _fixture;
        private readonly ITestOutputHelper _output;

        /// <summary>Binds the shared open cache and the per-test output sink.</summary>
        public RealCacheQuickChatTests(RealCacheFixture fixture, ITestOutputHelper output)
        {
            _fixture = fixture;
            _output = output;
        }

        /// <summary>
        ///     The two indexes that hold a complete quick-chat bank.
        /// </summary>
        /// <remarks>
        ///     Not a count of anything in the cache: the client opens exactly these two and hands
        ///     both to the same pair of loaders (InterfaceSettings.java:180-181, :297-300).
        /// </remarks>
        public static IEnumerable<int> Banks
        {
            get
            {
                yield return RSConstants.QUICK_CHAT_MESSAGES;
                yield return RSConstants.QUICK_CHAT_MENU;
            }
        }

        /// <summary>Files a bank's reference table declares in one of its two groups.</summary>
        /// <param name="indexId">The bank index.</param>
        /// <param name="groupId">The group within it.</param>
        /// <returns>The declared file count.</returns>
        private int DeclaredFiles(int indexId, int groupId)
        {
            return _fixture.Table(indexId).GetArchiveEntry(groupId)?.GetValidFileIds().Length ?? 0;
        }

        /// <summary>The menu group of one bank, bound to the production codec.</summary>
        /// <param name="indexId">The bank index.</param>
        /// <returns>A sweep over every declared menu record.</returns>
        private DefinitionSweep<QuickChatMenuDefinition> MenuSweep(int indexId)
        {
            return new DefinitionSweep<QuickChatMenuDefinition>(_fixture, _output, indexId,
                new DefinitionCodec<QuickChatMenuDefinition>("index " + indexId + " quick-chat menu",
                    (id, stream) => new QuickChatMenuDefinition { Id = id }.Decode(stream),
                    definition => definition.Encode(),
                    definition => definition.Opcodes.Select(record => record.Opcode)))
                .WithinGroup(QuickChatBank.MenuGroup);
        }

        /// <summary>The message group of one bank, bound to the production codec.</summary>
        /// <param name="indexId">The bank index.</param>
        /// <returns>A sweep over every declared message record.</returns>
        private DefinitionSweep<QuickChatMessageDefinition> MessageSweep(int indexId)
        {
            return new DefinitionSweep<QuickChatMessageDefinition>(_fixture, _output, indexId,
                new DefinitionCodec<QuickChatMessageDefinition>("index " + indexId + " quick-chat message",
                    (id, stream) => new QuickChatMessageDefinition { Id = id }.Decode(stream),
                    definition => definition.Encode(),
                    definition => definition.Opcodes.Select(record => record.Opcode)))
                .WithinGroup(QuickChatBank.MessageGroup);
        }

        /// <summary>
        ///     Both banks declare both halves, and neither half is empty.
        /// </summary>
        /// <remarks>
        ///     The premise every other test here rests on. Menu-versus-message is group 0 versus
        ///     group 1 <i>within</i> each index rather than a split across the two, so a bank
        ///     missing a group would silently reduce every sweep below to half a bank while still
        ///     passing its own count assertion.
        /// </remarks>
        [RealCacheFact]
        public void BothBanksDeclareAMenuGroupAndAMessageGroup()
        {
            foreach (int indexId in Banks)
            {
                var groups = _fixture.Table(indexId).GetArchiveEntries().Keys;

                Assert.Contains(QuickChatBank.MenuGroup, groups);
                Assert.Contains(QuickChatBank.MessageGroup, groups);

                int menus = DeclaredFiles(indexId, QuickChatBank.MenuGroup);
                int messages = DeclaredFiles(indexId, QuickChatBank.MessageGroup);

                Assert.True(menus > 0, $"index {indexId} declares no menu records, so nothing is checked");
                Assert.True(messages > 0, $"index {indexId} declares no message records, so nothing is checked");

                _output.WriteLine($"index {indexId}: {menus} menu records, {messages} message records");
            }
        }

        /// <summary>
        ///     A bank's file ids are not densely numbered, so they have to be read off the table.
        /// </summary>
        /// <remarks>
        ///     Index 25's message group spans 0 to 69 with 62 absent, in both caches. A
        ///     <c>0..count-1</c> walk over it asks for a file that does not exist and never asks for
        ///     the one that does - and would still report the right number of records, so no count
        ///     assertion above would notice. Stated as a property rather than as the gap's id,
        ///     because it is the sparseness that matters and not which id is missing.
        /// </remarks>
        [RealCacheFact]
        public void SomeGroupIsSparselyNumberedSoFileIdsMustComeFromTheTable()
        {
            int sparse = 0;

            foreach (int indexId in Banks)
            {
                foreach (int groupId in new[] { QuickChatBank.MenuGroup, QuickChatBank.MessageGroup })
                {
                    int[] fileIds = _fixture.Table(indexId).GetArchiveEntry(groupId).GetValidFileIds();
                    Assert.True(fileIds.Length > 0, $"index {indexId} group {groupId} declares no files");

                    //Delta-decoded from the table, so ascending: the span is the last id less the
                    //first, and a span wider than the count is a hole.
                    int span = fileIds[fileIds.Length - 1] - fileIds[0] + 1;

                    if (span == fileIds.Length)
                        continue;

                    sparse++;
                    _output.WriteLine($"index {indexId} group {groupId}: {fileIds.Length} files spanning " +
                                      $"ids {fileIds[0]}..{fileIds[fileIds.Length - 1]}");
                }
            }

            Assert.True(sparse > 0,
                "every group in both banks is densely numbered, so nothing here shows that the " +
                "declared file ids are load bearing");
        }

        /// <summary>Every declared menu record decodes and finishes on the last byte of its file.</summary>
        [RealCacheFact]
        public void EveryMenuRecord_DecodesAndConsumesItsBufferExactly()
        {
            foreach (int indexId in Banks)
            {
                DefinitionSweepResult swept = MenuSweep(indexId).AssertExactConsumption();
                int declared = DeclaredFiles(indexId, QuickChatBank.MenuGroup);

                Assert.True(declared > 0, $"index {indexId} declares no menu records, so nothing was checked");
                Assert.Equal(declared, swept.Records);
                Assert.Equal(declared, swept.Passed);
            }
        }

        /// <summary>Every declared message record decodes and finishes on the last byte of its file.</summary>
        /// <remarks>
        ///     This is what proves the ported slot-type word counts. Opcode 3 is the only
        ///     variable-length payload in either format whose length is stated nowhere in the file,
        ///     so a wrong entry in <see cref="QuickChatSlotType"/> reads the following bytes as the
        ///     next slot and the record stops landing on its terminator.
        /// </remarks>
        [RealCacheFact]
        public void EveryMessageRecord_DecodesAndConsumesItsBufferExactly()
        {
            foreach (int indexId in Banks)
            {
                DefinitionSweepResult swept = MessageSweep(indexId).AssertExactConsumption();
                int declared = DeclaredFiles(indexId, QuickChatBank.MessageGroup);

                Assert.True(declared > 0, $"index {indexId} declares no message records, so nothing was checked");
                Assert.Equal(declared, swept.Records);
                Assert.Equal(declared, swept.Passed);
            }
        }

        /// <summary>Every declared menu record re-encodes to the bytes it was decoded from.</summary>
        [RealCacheFact]
        public void EveryMenuRecord_ReEncodesToTheCapturedBytes()
        {
            foreach (int indexId in Banks)
            {
                DefinitionSweepResult swept = MenuSweep(indexId).AssertReEncodesToCapturedBytes();
                int declared = DeclaredFiles(indexId, QuickChatBank.MenuGroup);

                Assert.True(declared > 0, $"index {indexId} declares no menu records, so nothing was checked");
                Assert.Equal(declared, swept.Records);
                Assert.Equal(declared, swept.Passed);
                Assert.Equal(0, swept.Reordered);
            }
        }

        /// <summary>Every declared message record re-encodes to the bytes it was decoded from.</summary>
        [RealCacheFact]
        public void EveryMessageRecord_ReEncodesToTheCapturedBytes()
        {
            foreach (int indexId in Banks)
            {
                DefinitionSweepResult swept = MessageSweep(indexId).AssertReEncodesToCapturedBytes();
                int declared = DeclaredFiles(indexId, QuickChatBank.MessageGroup);

                Assert.True(declared > 0, $"index {indexId} declares no message records, so nothing was checked");
                Assert.Equal(declared, swept.Records);
                Assert.Equal(declared, swept.Passed);
                Assert.Equal(0, swept.Reordered);
            }
        }

        /// <summary>The encoders' own output decodes back to something that encodes identically.</summary>
        /// <remarks>
        ///     Independent of the byte-identity sweep and weaker in a different direction: it is the
        ///     property a save depends on once a record has actually been edited, which no
        ///     comparison against the cache reaches.
        /// </remarks>
        [RealCacheFact]
        public void EveryRecord_EncodeIsAFixedPointOfDecode()
        {
            foreach (int indexId in Banks)
            {
                MenuSweep(indexId).AssertEncodeIsAFixedPointOfDecode();
                MessageSweep(indexId).AssertEncodeIsAFixedPointOfDecode();
            }
        }

        /// <summary>
        ///     Every substitution slot in either bank names a type the 637 client defines.
        /// </summary>
        /// <remarks>
        ///     The 639 data cannot state a slot's word count, so the only thing that can corroborate
        ///     the ported table is that the ids it covers are the ids the data uses. An unknown type
        ///     is not an error - the client reads no words for one and carries on - so it would not
        ///     show up as a consumption failure either; it would silently drop a slot.
        /// </remarks>
        [RealCacheFact]
        public void EverySlotTypeIsOneTheClientDefines()
        {
            var seen = new SortedDictionary<int, int>();
            int unknown = 0;
            int slots = 0;

            foreach (int indexId in Banks)
            {
                MessageSweep(indexId).ForEachDecoded((record, definition) =>
                {
                    foreach (QuickChatSlot slot in definition.Slots)
                    {
                        slots++;
                        seen.TryGetValue(slot.SlotTypeId, out int count);
                        seen[slot.SlotTypeId] = count + 1;

                        if (!slot.IsKnownType)
                        {
                            unknown++;
                            _output.WriteLine($"index {indexId} message {record.Id} names slot type " +
                                              $"{slot.SlotTypeId}, which the 637 client has no entry for");
                        }
                    }
                });
            }

            _output.WriteLine($"{slots} slots across both banks, types " +
                              string.Join(", ", seen.Select(entry => $"{entry.Key}={entry.Value}")));

            Assert.True(slots > 0, "no message carries a substitution slot, so the ported table was not exercised");
            Assert.Equal(0, unknown);
        }

        /// <summary>
        ///     A menu's submenu entries all carry a shortcut key and its message entries never do.
        /// </summary>
        /// <remarks>
        ///     This is the evidence that opcode 2 is the submenu list and opcode 3 the message list,
        ///     rather than the assumption that the lower opcode comes first. Both are read as the
        ///     same <c>{u16 id, byte key}</c> pair, so nothing in the format distinguishes them and
        ///     swapping the two would still round-trip byte for byte - which means no sweep above
        ///     could catch it and only this can.
        ///     <para>
        ///     It agrees with what the client does with each list: <c>method1528</c> searches the
        ///     opcode 2 entries by pressed character (Node_Sub46_Sub1.java:69-86), while an opcode 3
        ///     entry with no key can only be chosen by position.
        ///     </para>
        /// </remarks>
        [RealCacheFact]
        public void SubmenuEntriesAreKeyedAndMessageEntriesAreNot()
        {
            int submenus = 0;
            int messages = 0;

            foreach (int indexId in Banks)
            {
                MenuSweep(indexId).ForEachDecoded((record, definition) =>
                {
                    foreach (QuickChatLink link in definition.Submenus)
                    {
                        submenus++;
                        Assert.True(link.Shortcut != 0,
                            $"index {indexId} menu {record.Id} lists submenu {link.TargetId} with no " +
                            "shortcut key, so opcode 2 is not the keyed list after all");
                    }

                    foreach (QuickChatLink link in definition.Messages)
                    {
                        messages++;
                        Assert.True(link.Shortcut == 0,
                            $"index {indexId} menu {record.Id} lists message {link.TargetId} with " +
                            $"shortcut 0x{link.Shortcut:X2}, so opcode 3 is keyed after all");
                    }
                });
            }

            _output.WriteLine($"{submenus} keyed submenu entries and {messages} unkeyed message entries " +
                              "across both banks");

            Assert.True(submenus > 0 && messages > 0,
                "one of the two link lists is empty across both banks, so the distinction was not exercised");
        }

        /// <summary>
        ///     At least one record in the banks stores its opcodes out of ascending order.
        /// </summary>
        /// <remarks>
        ///     The whole justification for replaying the recorded stream instead of emitting a fixed
        ///     order. Index 25's root menu stores opcode 4 ahead of its caption; an ascending encoder
        ///     reproduces every other record in both banks and rewrites that one, which changes the
        ///     group, its CRC and the reference-table entry of the group packed beside it.
        /// </remarks>
        [RealCacheFact]
        public void SomeMenuRecordDoesNotStoreItsOpcodesInAscendingOrder()
        {
            var orders = new SortedDictionary<string, int>();
            int unordered = 0;

            foreach (int indexId in Banks)
            {
                MenuSweep(indexId).ForEachDecoded((record, definition) =>
                {
                    int[] opcodes = definition.Opcodes.Select(entry => entry.Opcode).ToArray();
                    string order = indexId + ":" + string.Join(",", opcodes);
                    orders.TryGetValue(order, out int seen);
                    orders[order] = seen + 1;

                    if (!opcodes.SequenceEqual(opcodes.OrderBy(opcode => opcode)))
                        unordered++;
                });
            }

            foreach (KeyValuePair<string, int> order in orders)
                _output.WriteLine($"opcode order [{order.Key}]: {order.Value}");

            Assert.True(unordered > 0,
                "every menu record stores its opcodes ascending, so nothing here shows that the " +
                "recorded order is needed");
        }

        /// <summary>
        ///     No string in either bank exercises the lossy half of the cp1252 remap.
        /// </summary>
        /// <remarks>
        ///     Deliberately a measurement rather than a guard. Five byte values in the 0x80-0x9F band
        ///     are unassigned and decode to a question mark, so a codec that kept only the decoded
        ///     string would rewrite them on the first save - and this asserts that the byte-identity
        ///     sweeps above <b>cannot</b> see that, because no shipped string carries one. The bytes
        ///     are kept anyway and pinned by
        ///     <see cref="QuickChatDefinitionCodecTests.UnassignedCp1252BytesSurviveAReEncode"/>,
        ///     which is the only thing defending an edit made in a cache that does.
        /// </remarks>
        [RealCacheFact]
        public void NoStoredStringExercisesTheLossyRemap()
        {
            int strings = 0;
            int withHighBytes = 0;

            void Check(int indexId, int recordId, string what, byte[] stored)
            {
                strings++;
                foreach (byte value in stored)
                {
                    if (value < 0x80)
                        continue;

                    withHighBytes++;
                    _output.WriteLine($"index {indexId} {what} {recordId} stores byte 0x{value:X2}");
                    break;
                }

                Assert.Equal(stored, QuickChatText.ToBytes(QuickChatText.ToText(stored)));
            }

            foreach (int indexId in Banks)
            {
                MenuSweep(indexId).ForEachDecoded((record, definition) =>
                    Check(indexId, record.Id, "menu", definition.CaptionBytes));
                MessageSweep(indexId).ForEachDecoded((record, definition) =>
                    Check(indexId, record.Id, "message", definition.TemplateBytes));
            }

            _output.WriteLine($"{strings} stored strings, {withHighBytes} carrying a byte above 0x7F");

            Assert.True(strings > 0, "no string was examined");
            Assert.Equal(0, withHighBytes);
        }
    }
}
