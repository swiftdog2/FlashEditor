using System;
using System.Collections.Generic;
using System.Linq;
using FlashEditor.Cache;
using FlashEditor.Definitions.QuickChat;
using Xunit;
using FlashEditor.IO;

namespace FlashEditor.Tests.Definitions.QuickChat
{
    /// <summary>
    ///     The quick-chat codec against bytes lifted from a real revision-639 cache, plus the
    ///     branches no shipped record reaches.
    /// </summary>
    /// <remarks>
    ///     Both banks are represented. Index 24 is the only one whose messages carry substitution
    ///     slots and index 25 the only one carrying a record whose opcodes are not ascending, so a
    ///     fixture set drawn from either alone would leave half the format untested.
    ///     <para>
    ///     The addresses each fixture came from are recorded so it can be re-read from the cache
    ///     rather than trusted. Both supported caches store identical bytes at all of them.
    ///     </para>
    /// </remarks>
    public sealed class QuickChatDefinitionCodecTests
    {
        /// <summary>Index 24, menu 85 - the root "Quick Chat" node. Order 1, 2.</summary>
        public static readonly byte[] MenuWithSubmenusOnly =
        {
            0x01, 0x51, 0x75, 0x69, 0x63, 0x6B, 0x20, 0x43, 0x68, 0x61, 0x74, 0x00,
            0x02, 0x06,
            0x00, 0x3A, 0x67,
            0x00, 0x4B, 0x74,
            0x00, 0x5B, 0x73,
            0x00, 0x00, 0x65,
            0x00, 0x51, 0x63,
            0x00, 0x56, 0x6D,
            0x00
        };

        /// <summary>Index 24, menu 0 - "Group events". Order 1, 2, 3.</summary>
        public static readonly byte[] MenuWithBothLists =
        {
            0x01, 0x47, 0x72, 0x6F, 0x75, 0x70, 0x20, 0x65, 0x76, 0x65, 0x6E, 0x74, 0x73, 0x00,
            0x02, 0x06,
            0x00, 0x01, 0x6F,
            0x00, 0x02, 0x62,
            0x00, 0x0B, 0x63,
            0x00, 0x23, 0x66,
            0x00, 0x2D, 0x71,
            0x00, 0x31, 0x73,
            0x03, 0x01,
            0x00, 0xF2, 0x00,
            0x00
        };

        /// <summary>Index 25, menu 4 - "Hello", messages only. Order 1, 3.</summary>
        public static readonly byte[] MenuWithMessagesOnly =
        {
            0x01, 0x48, 0x65, 0x6C, 0x6C, 0x6F, 0x00,
            0x03, 0x05,
            0x00, 0x01, 0x00,
            0x00, 0x02, 0x00,
            0x00, 0x03, 0x00,
            0x00, 0x04, 0x00,
            0x00, 0x05, 0x00,
            0x00
        };

        /// <summary>
        ///     Index 25, menu 1 - the second bank's root. Order 4, 1, 2.
        /// </summary>
        /// <remarks>
        ///     The only record in either cache whose opcodes are not ascending, and the only one
        ///     carrying menu opcode 4 at all. The 637 client has no handler for that opcode and
        ///     reads nothing for it, so the record still parses to its terminator there.
        /// </remarks>
        public static readonly byte[] MenuWithLeadingFlag =
        {
            0x04,
            0x01, 0x51, 0x75, 0x69, 0x63, 0x6B, 0x20, 0x43, 0x68, 0x61, 0x74, 0x00,
            0x02, 0x02,
            0x00, 0x02, 0x67,
            0x00, 0x0C, 0x6D,
            0x00
        };

        /// <summary>Index 24, message 3 - "Race you!". Order 1.</summary>
        public static readonly byte[] MessageTemplateOnly =
        {
            0x01, 0x52, 0x61, 0x63, 0x65, 0x20, 0x79, 0x6F, 0x75, 0x21, 0x00,
            0x00
        };

        /// <summary>Index 25, message 5 - "How are you?" with four suggested replies. Order 1, 2.</summary>
        public static readonly byte[] MessageWithResponses =
        {
            0x01, 0x48, 0x6F, 0x77, 0x20, 0x61, 0x72, 0x65, 0x20, 0x79, 0x6F, 0x75, 0x3F, 0x00,
            0x02, 0x04, 0x00, 0x12, 0x00, 0x13, 0x00, 0x14, 0x00, 0x15,
            0x00
        };

        /// <summary>Index 24, message 1 - "My Agility level is &lt;." Order 1, 2, 3.</summary>
        public static readonly byte[] MessageWithOneSlot =
        {
            0x01, 0x4D, 0x79, 0x20, 0x41, 0x67, 0x69, 0x6C, 0x69, 0x74, 0x79, 0x20, 0x6C, 0x65,
            0x76, 0x65, 0x6C, 0x20, 0x69, 0x73, 0x20, 0x3C, 0x2E, 0x00,
            0x02, 0x04, 0x02, 0x64, 0x02, 0x65, 0x02, 0x66, 0x00, 0x01,
            0x03, 0x01, 0x00, 0x04, 0x00, 0x10,
            0x00
        };

        /// <summary>
        ///     Index 24, message 105 - "My current Slayer assignment is: &lt;." Order 1, 3.
        /// </summary>
        /// <remarks>The only record in either cache carrying slot type 6, which stores two words.</remarks>
        public static readonly byte[] MessageWithTwoWordSlot =
        {
            0x01, 0x4D, 0x79, 0x20, 0x63, 0x75, 0x72, 0x72, 0x65, 0x6E, 0x74, 0x20, 0x53, 0x6C,
            0x61, 0x79, 0x65, 0x72, 0x20, 0x61, 0x73, 0x73, 0x69, 0x67, 0x6E, 0x6D, 0x65, 0x6E,
            0x74, 0x20, 0x69, 0x73, 0x3A, 0x20, 0x3C, 0x2E, 0x00,
            0x03, 0x01, 0x00, 0x06, 0x06, 0x1B, 0x01, 0x8B,
            0x00
        };

        /// <summary>
        ///     Index 24, message 615 - three slots of two different widths in one record.
        /// </summary>
        /// <remarks>
        ///     The sharpest fixture for the ported word counts: a one-word type followed by two
        ///     two-word types. Any wrong entry shifts everything after it and the record stops
        ///     ending on its terminator.
        /// </remarks>
        public static readonly byte[] MessageWithThreeSlots =
        {
            0x01, 0x54, 0x68, 0x65, 0x20, 0x74, 0x6F, 0x74, 0x61, 0x6C, 0x20, 0x61, 0x6D, 0x6F,
            0x75, 0x6E, 0x74, 0x20, 0x6F, 0x66, 0x20, 0x65, 0x78, 0x70, 0x65, 0x72, 0x69, 0x65,
            0x6E, 0x63, 0x65, 0x20, 0x62, 0x65, 0x74, 0x77, 0x65, 0x65, 0x6E, 0x20, 0x6C, 0x65,
            0x76, 0x65, 0x6C, 0x73, 0x20, 0x3C, 0x20, 0x61, 0x6E, 0x64, 0x20, 0x3C, 0x20, 0x69,
            0x73, 0x20, 0x3C, 0x2E, 0x00,
            0x03, 0x03,
            0x00, 0x04, 0x00, 0x10,
            0x00, 0x0B, 0x03, 0x28, 0x00, 0x10,
            0x00, 0x0B, 0x08, 0x5C, 0x00, 0x10,
            0x00
        };

        /// <summary>Every captured menu record, with the index and file id it was read from.</summary>
        public static IEnumerable<object[]> EveryMenuFixture()
        {
            yield return new object[] { RSConstants.QUICK_CHAT_MESSAGES, 85, MenuWithSubmenusOnly };
            yield return new object[] { RSConstants.QUICK_CHAT_MESSAGES, 0, MenuWithBothLists };
            yield return new object[] { RSConstants.QUICK_CHAT_MENU, 4, MenuWithMessagesOnly };
            yield return new object[] { RSConstants.QUICK_CHAT_MENU, 1, MenuWithLeadingFlag };
        }

        /// <summary>Every captured message record, with the index and file id it was read from.</summary>
        public static IEnumerable<object[]> EveryMessageFixture()
        {
            yield return new object[] { RSConstants.QUICK_CHAT_MESSAGES, 3, MessageTemplateOnly };
            yield return new object[] { RSConstants.QUICK_CHAT_MENU, 5, MessageWithResponses };
            yield return new object[] { RSConstants.QUICK_CHAT_MESSAGES, 1, MessageWithOneSlot };
            yield return new object[] { RSConstants.QUICK_CHAT_MESSAGES, 105, MessageWithTwoWordSlot };
            yield return new object[] { RSConstants.QUICK_CHAT_MESSAGES, 615, MessageWithThreeSlots };
        }

        /// <summary>Every captured menu record consumes exactly and re-encodes to its own bytes.</summary>
        /// <param name="indexId">The bank the record came from, so a failure names it.</param>
        /// <param name="id">The file id within the bank's menu group.</param>
        /// <param name="stored">The captured bytes.</param>
        [Theory]
        [MemberData(nameof(EveryMenuFixture))]
        public void EveryCapturedMenuRecordRoundTrips(int indexId, int id, byte[] stored)
        {
            var stream = new JagStream(stored);
            var definition = new QuickChatMenuDefinition { Id = id }.Decode(stream);

            Assert.True(stored.Length == stream.Position,
                $"index {indexId} menu {id} consumed {stream.Position} of its {stored.Length} bytes");
            Assert.True(stored.AsSpan().SequenceEqual(definition.Encode().ToArray()),
                $"index {indexId} menu {id} did not re-encode to the bytes it was decoded from");
        }

        /// <summary>Every captured message record consumes exactly and re-encodes to its own bytes.</summary>
        /// <param name="indexId">The bank the record came from, so a failure names it.</param>
        /// <param name="id">The file id within the bank's message group.</param>
        /// <param name="stored">The captured bytes.</param>
        [Theory]
        [MemberData(nameof(EveryMessageFixture))]
        public void EveryCapturedMessageRecordRoundTrips(int indexId, int id, byte[] stored)
        {
            var stream = new JagStream(stored);
            var definition = new QuickChatMessageDefinition { Id = id }.Decode(stream);

            Assert.True(stored.Length == stream.Position,
                $"index {indexId} message {id} consumed {stream.Position} of its {stored.Length} bytes");
            Assert.True(stored.AsSpan().SequenceEqual(definition.Encode().ToArray()),
                $"index {indexId} message {id} did not re-encode to the bytes it was decoded from");
        }

        /// <summary>A menu record decodes to the caption and links the client reads out of it.</summary>
        [Fact]
        public void AMenuRecordDecodesToItsFields()
        {
            var definition = new QuickChatMenuDefinition { Id = 0 }.Decode(new JagStream(MenuWithBothLists));

            Assert.Equal("Group events", definition.Caption);
            Assert.Equal(6, definition.Submenus.Count);
            Assert.Equal(1, definition.Submenus[0].TargetId);
            Assert.Equal('o', definition.Submenus[0].ShortcutChar);
            Assert.Equal(49, definition.Submenus[5].TargetId);
            Assert.Equal('s', definition.Submenus[5].ShortcutChar);

            Assert.Single(definition.Messages);
            Assert.Equal(242, definition.Messages[0].TargetId);
            Assert.Equal('\0', definition.Messages[0].ShortcutChar);
            Assert.False(definition.UnknownFlag4);
        }

        /// <summary>A message record decodes to the template, replies and slots the client reads.</summary>
        [Fact]
        public void AMessageRecordDecodesToItsFields()
        {
            var definition = new QuickChatMessageDefinition { Id = 1 }.Decode(new JagStream(MessageWithOneSlot));

            Assert.Equal("My Agility level is <.", definition.Template);
            Assert.Equal(new[] { 612, 613, 614, 1 }, definition.ResponseIds);
            Assert.Single(definition.Slots);
            Assert.Equal(4, definition.Slots[0].SlotTypeId);
            Assert.Equal(new[] { 16 }, definition.Slots[0].Words);
            Assert.Equal(1, definition.MarkerCount);
            Assert.False(definition.HiddenFromSearch);
        }

        /// <summary>
        ///     A record with several slots of different widths reads each one at its own length.
        /// </summary>
        /// <remarks>
        ///     This is the assertion the ported <see cref="QuickChatSlotType"/> table lives or dies
        ///     by. Nothing in the file states a slot's width, so a wrong entry does not fail here as
        ///     a wrong value - it shifts every field after it.
        /// </remarks>
        [Fact]
        public void SlotWidthsComeFromTheClientTable()
        {
            var definition = new QuickChatMessageDefinition { Id = 615 }
                .Decode(new JagStream(MessageWithThreeSlots));

            Assert.Equal(3, definition.Slots.Count);
            Assert.Equal(3, definition.MarkerCount);

            Assert.Equal(4, definition.Slots[0].SlotTypeId);
            Assert.Equal(new[] { 16 }, definition.Slots[0].Words);

            Assert.Equal(11, definition.Slots[1].SlotTypeId);
            Assert.Equal(new[] { 808, 16 }, definition.Slots[1].Words);

            Assert.Equal(11, definition.Slots[2].SlotTypeId);
            Assert.Equal(new[] { 2140, 16 }, definition.Slots[2].Words);
        }

        /// <summary>The ported slot-type table matches the fourteen the 637 client constructs.</summary>
        /// <remarks>
        ///     Transcribed straight from the client's own construction sites rather than from the
        ///     production table, so the two disagreeing is a failure rather than a tautology. The
        ///     word count is the <b>fourth</b> constructor argument (Class348.java:72-80), which is
        ///     the easiest thing in this port to get wrong.
        /// </remarks>
        [Fact]
        public void TheSlotTypeTableMatchesTheClient()
        {
            var client = new Dictionary<int, int>
            {
                { 0, 1 }, { 1, 0 }, { 2, 0 }, { 4, 1 }, { 6, 2 }, { 7, 1 }, { 8, 1 },
                { 9, 1 }, { 10, 0 }, { 11, 2 }, { 12, 0 }, { 13, 0 }, { 14, 1 }, { 15, 0 }
            };

            Assert.Equal(client.Keys.OrderBy(id => id), QuickChatSlotType.KnownTypeIds.OrderBy(id => id));
            foreach (KeyValuePair<int, int> row in client)
            {
                Assert.True(QuickChatSlotType.IsKnown(row.Key), $"slot type {row.Key} is missing");
                Assert.Equal(row.Value, QuickChatSlotType.WordCount(row.Key));
            }

            //3 and 5 are gaps in the client's own set, not omissions here.
            Assert.False(QuickChatSlotType.IsKnown(3));
            Assert.False(QuickChatSlotType.IsKnown(5));
        }

        /// <summary>
        ///     An unrecognised slot type consumes no words and is written back unchanged.
        /// </summary>
        /// <remarks>
        ///     Synthetic: every slot type in both caches is one the client defines, so nothing in
        ///     the data reaches this branch. Reading no words is the client's own behaviour
        ///     (Node_Sub46_Sub11.java:144 guards the word loop on the lookup succeeding), and
        ///     keeping the type id is this project diverging from it - the client drops the id, and
        ///     a codec that did the same could not write the record back.
        /// </remarks>
        [Fact]
        public void AnUnknownSlotTypeReadsNoWordsAndSurvivesAReEncode()
        {
            byte[] stored =
            {
                0x01, 0x58, 0x00,
                0x03, 0x02,
                0x00, 0x03,
                0x00, 0x04, 0x00, 0x10,
                0x00
            };

            var stream = new JagStream(stored);
            var definition = new QuickChatMessageDefinition { Id = 0 }.Decode(stream);

            Assert.Equal(stored.Length, stream.Position);
            Assert.Equal(2, definition.Slots.Count);
            Assert.Equal(3, definition.Slots[0].SlotTypeId);
            Assert.False(definition.Slots[0].IsKnownType);
            Assert.Empty(definition.Slots[0].Words);
            Assert.Equal(4, definition.Slots[1].SlotTypeId);
            Assert.Equal(new[] { 16 }, definition.Slots[1].Words);
            Assert.Equal(stored, definition.Encode().ToArray());
        }

        /// <summary>
        ///     A slot whose word list does not match its type is refused rather than written.
        /// </summary>
        /// <remarks>
        ///     The length is not in the file, so a slot carrying one word too many is not a longer
        ///     slot - it is a shorter one followed by bytes the client reads as the next slot, and
        ///     every field after it shifts. Refusing is the only outcome that does not corrupt the
        ///     record silently.
        /// </remarks>
        [Fact]
        public void ASlotWhoseWordCountDisagreesWithItsTypeIsRefused()
        {
            var definition = new QuickChatMessageDefinition { Id = 0 }
                .Decode(new JagStream(MessageWithOneSlot));

            definition.Slots[0].Words.Add(7);

            Assert.Throws<InvalidOperationException>(() => definition.Encode().ToArray());
        }

        /// <summary>
        ///     A stored string keeps the byte values the cp1252 decode cannot express.
        /// </summary>
        /// <remarks>
        ///     <b>The trap no sweep catches.</b> Five bytes in the 0x80-0x9F band are unassigned and
        ///     both the client's reader and ours map them to a question mark, so a codec that kept
        ///     only the decoded string would rewrite them as 0x3F. No string in either supported
        ///     cache carries one - or any byte above 0x7F at all - so the byte-identity sweeps go
        ///     green either way and this is the only thing defending an edit in a cache that does.
        /// </remarks>
        [Fact]
        public void UnassignedCp1252BytesSurviveAReEncode()
        {
            byte[] stored = { 0x01, 0x41, 0x81, 0x8D, 0x8F, 0x90, 0x9D, 0x42, 0x00, 0x00 };
            byte[] caption = { 0x41, 0x81, 0x8D, 0x8F, 0x90, 0x9D, 0x42 };

            var menu = new QuickChatMenuDefinition { Id = 0 }.Decode(new JagStream(stored));
            Assert.Equal(caption, menu.CaptionBytes);
            Assert.Equal(stored, menu.Encode().ToArray());

            var message = new QuickChatMessageDefinition { Id = 0 }.Decode(new JagStream(stored));
            Assert.Equal(caption, message.TemplateBytes);
            Assert.Equal(stored, message.Encode().ToArray());

            //And the loss is real, so keeping the bytes is not belt and braces: routing the same
            //string back through the text form replaces all five with a question mark.
            Assert.Equal("A?????B", menu.Caption);
            Assert.Equal(new byte[] { 0x41, 0x3F, 0x3F, 0x3F, 0x3F, 0x3F, 0x42 },
                QuickChatText.ToBytes(menu.Caption));
        }

        /// <summary>Setting the caption re-encodes it, and text that survives the remap round trips.</summary>
        [Fact]
        public void SettingTheCaptionRewritesTheStoredBytes()
        {
            //A horizontal ellipsis, which is one of the assigned slots in the 0x80-0x9F band and so
            //stores as the single byte 0x85 rather than as a multi-byte sequence.
            string renamed = "Group events " + (char) 0x2026;

            var definition = new QuickChatMenuDefinition { Id = 0 }.Decode(new JagStream(MenuWithBothLists));
            definition.Caption = renamed;

            byte[] encoded = definition.Encode().ToArray();
            var reread = new QuickChatMenuDefinition { Id = 0 }.Decode(new JagStream(encoded));

            Assert.Equal(renamed, reread.Caption);
            Assert.Equal(0x85, reread.CaptionBytes[reread.CaptionBytes.Length - 1]);
        }

        /// <summary>
        ///     Menu opcode 4 keeps its place ahead of the caption rather than being sorted.
        /// </summary>
        /// <remarks>
        ///     The one record in either cache whose opcodes are not ascending. An encoder emitting
        ///     its own order reproduces every other menu record in both banks and corrupts this one.
        /// </remarks>
        [Fact]
        public void TheLeadingFlagKeepsItsPosition()
        {
            var definition = new QuickChatMenuDefinition { Id = 1 }.Decode(new JagStream(MenuWithLeadingFlag));

            Assert.Equal(new[] { 4, 1, 2 }, definition.Opcodes.Select(record => record.Opcode));
            Assert.True(definition.UnknownFlag4);
            Assert.Equal("Quick Chat", definition.Caption);
            Assert.Equal(MenuWithLeadingFlag, definition.Encode().ToArray());
        }

        /// <summary>Clearing a bare flag removes its opcode rather than leaving one to be replayed.</summary>
        [Fact]
        public void ClearingABareFlagRemovesItsOpcode()
        {
            var definition = new QuickChatMenuDefinition { Id = 1 }.Decode(new JagStream(MenuWithLeadingFlag));

            definition.UnknownFlag4 = false;
            byte[] encoded = definition.Encode().ToArray();

            Assert.Equal(1, encoded[0]);
            Assert.False(new QuickChatMenuDefinition { Id = 1 }.Decode(new JagStream(encoded)).UnknownFlag4);

            definition.UnknownFlag4 = true;
            Assert.True(new QuickChatMenuDefinition { Id = 1 }
                .Decode(new JagStream(definition.Encode().ToArray())).UnknownFlag4);
        }

        /// <summary>
        ///     Message opcode 4 encodes and decodes even though no shipped record carries it.
        /// </summary>
        /// <remarks>
        ///     It is a real opcode with a real consumer - it takes the message out of the quick-chat
        ///     search (JS5Archive.java:106) - and zero occurrences in either cache, so no sweep
        ///     defends it. Same category as the reference-table flags that are set nowhere on disk.
        /// </remarks>
        [Fact]
        public void TheSearchHidingFlagIsImplementedThoughNoRecordCarriesIt()
        {
            var definition = new QuickChatMessageDefinition { Id = 0 }.Decode(new JagStream(new byte[] { 0 }));
            definition.HiddenFromSearch = true;

            byte[] encoded = definition.Encode().ToArray();
            Assert.Equal(new byte[] { 0x04, 0x00 }, encoded);
            Assert.True(new QuickChatMessageDefinition { Id = 0 }
                .Decode(new JagStream(encoded)).HiddenFromSearch);
        }

        /// <summary>An empty record keeps its defaults and stays a single terminator byte.</summary>
        [Fact]
        public void AnEmptyRecordKeepsItsDefaults()
        {
            var menu = new QuickChatMenuDefinition { Id = 0 }.Decode(new JagStream(new byte[] { 0 }));
            Assert.Equal(string.Empty, menu.Caption);
            Assert.Empty(menu.Submenus);
            Assert.Empty(menu.Messages);
            Assert.False(menu.UnknownFlag4);
            Assert.Equal(new byte[] { 0 }, menu.Encode().ToArray());

            var message = new QuickChatMessageDefinition { Id = 0 }.Decode(new JagStream(new byte[] { 0 }));
            Assert.Equal(string.Empty, message.Template);
            Assert.Empty(message.ResponseIds);
            Assert.Empty(message.Slots);
            Assert.False(message.HiddenFromSearch);
            Assert.Equal(new byte[] { 0 }, message.Encode().ToArray());
        }

        /// <summary>A record built from nothing writes its opcodes in ascending order.</summary>
        /// <remarks>
        ///     There is no recorded stream to replay, so the appended order is the only one
        ///     available. It has to be deterministic or two runs of the editor produce different
        ///     bytes for the same record.
        /// </remarks>
        [Fact]
        public void ARecordBuiltFromNothingEncodesAscending()
        {
            var definition = new QuickChatMenuDefinition { Id = 0 };
            definition.Messages.Add(new QuickChatLink(7, 0));
            definition.Caption = "New";
            definition.Submenus.Add(new QuickChatLink(3, (byte) 'n'));

            byte[] encoded = definition.Encode().ToArray();
            var reread = new QuickChatMenuDefinition { Id = 0 }.Decode(new JagStream(encoded));

            Assert.Equal(new[] { 1, 2, 3 }, reread.Opcodes.Select(record => record.Opcode));
            Assert.Equal("New", reread.Caption);
            Assert.Equal(3, reread.Submenus[0].TargetId);
            Assert.Equal(7, reread.Messages[0].TargetId);
        }

        /// <summary>
        ///     A list stored with a count of zero is kept rather than dropped.
        /// </summary>
        /// <remarks>
        ///     An empty list and an absent opcode decode to the same thing, so the recorded stream is
        ///     the only statement of which it was. Deciding from the value alone would shorten the
        ///     file.
        /// </remarks>
        [Fact]
        public void AnEmptyListIsNotDroppedOnTheWayOut()
        {
            byte[] stored = { 0x01, 0x58, 0x00, 0x02, 0x00, 0x00 };

            var definition = new QuickChatMenuDefinition { Id = 0 }.Decode(new JagStream(stored));

            Assert.Empty(definition.Submenus);
            Assert.True(definition.Opcodes.Has(2));
            Assert.Equal(stored, definition.Encode().ToArray());
        }

        /// <summary>An opcode neither format defines is refused rather than desynchronising.</summary>
        [Fact]
        public void UnknownOpcodesAreRefused()
        {
            foreach (byte opcode in new byte[] { 5, 6, 100 })
            {
                Assert.Throws<InvalidOperationException>(() =>
                    new QuickChatMenuDefinition { Id = 0 }.Decode(new JagStream(new byte[] { opcode, 0, 0 })));
                Assert.Throws<InvalidOperationException>(() =>
                    new QuickChatMessageDefinition { Id = 0 }.Decode(new JagStream(new byte[] { opcode, 0, 0 })));
            }
        }

        /// <summary>
        ///     A global quick-chat id splits into the bank that holds it and the file within it.
        /// </summary>
        /// <remarks>
        ///     The client picks index 25 with the id masked to 15 bits when the second-bank bit is
        ///     set, and index 24 with the id unchanged otherwise (Class212.java:65-66,
        ///     Class280.java:377-378). An editor that showed global ids without folding through this
        ///     would point every second-bank reference at the wrong bank.
        /// </remarks>
        [Fact]
        public void AGlobalIdPicksItsBank()
        {
            Assert.Equal(RSConstants.QUICK_CHAT_MESSAGES, QuickChatBank.IndexOf(0));
            Assert.Equal(RSConstants.QUICK_CHAT_MESSAGES, QuickChatBank.IndexOf(1087));
            Assert.Equal(0, QuickChatBank.FileIdOf(0));
            Assert.Equal(1087, QuickChatBank.FileIdOf(1087));

            Assert.Equal(RSConstants.QUICK_CHAT_MENU, QuickChatBank.IndexOf(0x8000));
            Assert.Equal(RSConstants.QUICK_CHAT_MENU, QuickChatBank.IndexOf(0x8000 | 68));
            Assert.Equal(0, QuickChatBank.FileIdOf(0x8000));
            Assert.Equal(68, QuickChatBank.FileIdOf(0x8000 | 68));

            //And back again: a record's stored id never carries the bit, so which bank it belongs
            //to is context rather than content.
            Assert.Equal(4, QuickChatBank.GlobalId(RSConstants.QUICK_CHAT_MESSAGES, 4));
            Assert.Equal(0x8004, QuickChatBank.GlobalId(RSConstants.QUICK_CHAT_MENU, 4));
            Assert.Throws<ArgumentOutOfRangeException>(() =>
                QuickChatBank.GlobalId(RSConstants.QUICK_CHAT_MENU, 0x8004));
        }

        /// <summary>
        ///     A first-bank record may name an id in the second bank, so the encoder allows it.
        /// </summary>
        /// <remarks>
        ///     Tempting to reject, and wrong: only a record <i>in</i> index 25 is barred from storing
        ///     the second-bank bit, because the client ORs that bit onto every id such a record
        ///     carries (Node_Sub46_Sub1.method1531). A record in index 24 storing 0x8000 or more is
        ///     resolved against index 25 by the same lookup that resolves everything else. Nothing in
        ///     either cache does it, so only this says the codec permits it.
        /// </remarks>
        [Fact]
        public void ALinkMayCrossIntoTheSecondBank()
        {
            byte[] stored = { 0x01, 0x58, 0x00, 0x02, 0x01, 0x80, 0x05, 0x67, 0x00 };

            var definition = new QuickChatMenuDefinition { Id = 0 }.Decode(new JagStream(stored));

            Assert.Equal(0x8005, definition.Submenus[0].TargetId);
            Assert.Equal(RSConstants.QUICK_CHAT_MENU, QuickChatBank.IndexOf(definition.Submenus[0].TargetId));
            Assert.Equal(5, QuickChatBank.FileIdOf(definition.Submenus[0].TargetId));
            Assert.Equal(stored, definition.Encode().ToArray());
        }
    }
}
