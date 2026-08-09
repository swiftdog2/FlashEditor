using FlashEditor.Cache;
using FlashEditor.Tests.Cache.RealCache;
using System;
using Xunit;
using Xunit.Abstractions;
using FlashEditor.Definitions;
using FlashEditor.IO;
using FlashEditor.Definitions.Entities;

namespace FlashEditor.Tests.Cache
{
    /// <summary>
    ///     Runs every item definition in the real revision-639 cache through the production
    ///     opcode decoder and encoder.
    /// </summary>
    /// <remarks>
    ///     The item decoder was derived from a build-637 client while this cache is build 639 -
    ///     see AGENTS.md - and that gap has never been checked against real bytes. An item
    ///     definition is the one shape where it can be: it is a self-delimiting opcode stream,
    ///     read opcode byte, read its payload, repeat until opcode 0. Nothing in the file says
    ///     how long any payload is, so a decoder that mis-sizes one desynchronises for the rest
    ///     of the record. It then either throws, meets a byte that is not a known opcode, or
    ///     stops on a stray zero and leaves a tail unread.
    ///     <para>
    ///     "Consumed the buffer exactly" is therefore the assertion that matters here: across
    ///     twenty thousand records a wrong payload size cannot stay hidden behind it.
    ///     </para>
    /// </remarks>
    public class RealCacheItemDefinitionTests : IClassFixture<RealCacheFixture>
    {
        private readonly RealCacheFixture _cache;
        private readonly ITestOutputHelper _output;

        /// <summary>Binds the shared open cache and the per-test output sink.</summary>
        public RealCacheItemDefinitionTests(RealCacheFixture cache, ITestOutputHelper output)
        {
            _cache = cache;
            _output = output;
        }

        /// <summary>
        ///     The item index bound to the production codec, for the shared byte-identity harness.
        /// </summary>
        /// <remarks>
        ///     A fresh sweep per test rather than a shared field: each enumerates the index lazily
        ///     and holds no record beyond the one it is checking, which is what keeps a 20,470
        ///     record index off the large object heap.
        /// </remarks>
        /// <returns>A sweep over every item definition the cache declares.</returns>
        private DefinitionSweep<ItemDefinition> Sweep()
        {
            return new DefinitionSweep<ItemDefinition>(_cache, _output, RSConstants.ITEM_DEFINITIONS_INDEX,
                new DefinitionCodec<ItemDefinition>("item",
                    (id, stream) =>
                    {
                        var definition = new ItemDefinition();
                        definition.Decode(stream);
                        definition.SetId(id);
                        return definition;
                    },
                    definition => definition.Encode(),
                    definition => DefinitionCodec.FromHitMap(definition.decoded)));
        }

        // ===================================================================
        //  Decode
        // ===================================================================

        /// <summary>
        ///     Every item definition must decode without throwing and finish with the read
        ///     position on the end of its buffer.
        /// </summary>
        /// <remarks>
        ///     Landing short means an opcode payload was sized wrongly earlier in the record and
        ///     the decoder stopped on a data byte that happened to be zero, so the fields after
        ///     that point are garbage even though nothing threw. That is the failure this whole
        ///     class exists to catch, and it is reported with the opcode trace that led to it
        ///     rather than as a bare count.
        ///     <para>
        ///     Landing on the end is not quite enough on its own, which is why the harness also
        ///     decodes a padded copy and requires the last byte to be the opcode 0 terminator.
        ///     Several opcodes read their element count with <see cref="JagStream.ReadByte"/>,
        ///     which answers -1 at the end of the stream instead of throwing, so a record
        ///     truncated inside one of those counts would otherwise finish exactly on the end.
        ///     </para>
        /// </remarks>
        [RealCacheFact]
        public void AllItemDefinitions_DecodeAndConsumeTheirBufferExactly()
        {
            Sweep().AssertExactConsumption();
        }

        // ===================================================================
        //  Encode
        // ===================================================================

        /// <summary>
        ///     Every item definition must re-encode to the bytes it was decoded from.
        /// </summary>
        /// <remarks>
        ///     This is the strongest check available: it fails not only on a payload the decoder
        ///     sized wrongly but on any field it read and then failed to write back, and on any
        ///     opcode the encoder emits that the cache does not carry. The editor re-encodes a
        ///     definition on every save, so a difference here is a difference that lands on the
        ///     user's disk.
        /// </remarks>
        [RealCacheFact]
        public void AllItemDefinitions_ReEncodeToTheCapturedBytes()
        {
            Sweep().AssertReEncodesToCapturedBytes();
        }

        /// <summary>
        ///     The encoder's output must decode back to something that encodes identically
        ///     again.
        /// </summary>
        /// <remarks>
        ///     Byte-identity against the cache also fails when the cache's own layout differs
        ///     from the encoder's - a different opcode order, or an opcode the packer wrote at
        ///     its default value. This weaker check isolates the part that is purely this
        ///     project's fault: whatever the encoder writes, the decoder must read back to the
        ///     same state. A payload size the two disagree on shows up here as a second encode
        ///     that no longer matches the first, with no dependence on how Jagex packed it.
        /// </remarks>
        [RealCacheFact]
        public void AllItemDefinitions_EncodeIsAFixedPointOfDecode()
        {
            Sweep().AssertEncodeIsAFixedPointOfDecode();
        }

        // ===================================================================
        //  Regressions, pinned without needing a cache
        // ===================================================================

        /// <summary>
        ///     A definition that has never been touched must encode to nothing but the
        ///     terminator.
        /// </summary>
        /// <remarks>
        ///     The encoder used to write opcodes 1, 4, 5, 6 and 12 unconditionally, along with
        ///     the "take" and "drop" menu entries the decoder seeds. Against the real cache that
        ///     added eleven bytes of defaults to every item that had not stored them, so no item
        ///     in the cache re-encoded to the bytes it came from.
        /// </remarks>
        [Fact]
        public void Encode_WritesNothingForFieldsLeftAtTheirDefaults()
        {
            byte[] encoded = new ItemDefinition().Encode().ToArray();

            Assert.Equal(new byte[] { 0 }, encoded);
        }

        /// <summary>
        ///     A record that stores its opcodes out of order, repeats one, writes fields at
        ///     their default value and repeats a parameter key must come back byte for byte.
        /// </summary>
        /// <remarks>
        ///     Every one of those is something the revision-639 packer actually does and the
        ///     decoded fields cannot express: opcode order is free, a superseded opcode reaches
        ///     no field, an explicitly stored default is indistinguishable from an absent one,
        ///     and a repeated parameter key collapses in the dictionary. They are pinned
        ///     together because they only ever showed up together, in the same records.
        /// </remarks>
        [Fact]
        public void Encode_ReproducesAPackerLayoutTheFieldsCannotExpress()
        {
            var source = new JagStream();

            source.WriteByte(1); source.WriteShort(123);                  // superseded model id
            source.WriteByte(7); source.WriteShort(-5);                   // opcodes out of order
            source.WriteByte(4); source.WriteShort(2000);                 // stored at its default
            source.WriteByte(12); source.WriteInteger(1);                 // stored at its default
            source.WriteByte(1); source.WriteShort(456);                  // repeated opcode wins
            source.WriteByte(32); source.WriteJagexString("take");        // seeded ground option
            source.WriteByte(39); source.WriteJagexString("drop");        // seeded inventory option

            source.WriteByte(249);
            source.WriteByte(3);
            source.WriteByte(0); source.WriteMedium(7); source.WriteInteger(9);
            source.WriteByte(0); source.WriteMedium(3); source.WriteInteger(8);
            source.WriteByte(0); source.WriteMedium(7); source.WriteInteger(5);   // repeated key

            source.WriteByte(2); source.WriteJagexString("Bucket");       // name last, as packed
            source.WriteByte(0);

            byte[] captured = source.Flip().ToArray();

            var definition = new ItemDefinition();
            var stream = new JagStream(captured);
            definition.Decode(stream);

            Assert.Equal(captured.Length, stream.Position);
            Assert.Equal(456, definition.inventoryModelId);
            Assert.Equal(2000, definition.modelZoom);
            Assert.Equal("Bucket", definition.name);
            Assert.Equal(9, definition.itemParams[7]);
            Assert.Equal(captured, definition.Encode().ToArray());
        }

        /// <summary>
        ///     A field the editor sets on a definition whose record never carried that opcode
        ///     must still be written.
        /// </summary>
        /// <remarks>
        ///     Replaying the decoded opcode order is what makes an untouched definition
        ///     round-trip, but on its own it would freeze the record: an edit to a field the
        ///     packer left out would go nowhere. This is the other half of that.
        /// </remarks>
        [Fact]
        public void Encode_AppendsAnOpcodeTheRecordNeverCarried()
        {
            var source = new JagStream();
            source.WriteByte(2);
            source.WriteJagexString("Bucket");
            source.WriteByte(0);

            var definition = new ItemDefinition();
            definition.Decode(new JagStream(source.Flip().ToArray()));

            definition.teamId = 3;
            byte[] encoded = definition.Encode().ToArray();

            var reread = new ItemDefinition();
            reread.Decode(new JagStream(encoded));

            Assert.Equal(3, reread.teamId);
            Assert.Equal("Bucket", reread.name);
            Assert.Equal(new[] { 2, 115 }, reread.opcodeOrder);
        }

        // ===================================================================
        //  Bare flags
        // ===================================================================

        /// <summary>
        ///     Every presence-only opcode on an item definition, with the accessor that reads and
        ///     writes it phrased as "the record carries this opcode".
        /// </summary>
        /// <remarks>
        ///     Unlike the NPC and object codecs, this one already emitted these three from their
        ///     fields rather than from the recorded opcode list, so clearing one has always
        ///     dropped the last occurrence. The tests below pin that, and pin the one case it did
        ///     not cover - a record that stores the same flag twice.
        /// </remarks>
        private static readonly (int Opcode, string Name,
            Func<ItemDefinition, bool> Carried, Action<ItemDefinition, bool> SetCarried)[] BareFlags =
        {
            (11, "stackable",   d => d.stackable == 1, (d, on) => d.stackable = on ? 1 : 0),
            (16, "membersOnly", d => d.membersOnly,    (d, on) => d.membersOnly = on),
            (65, "unnoted",     d => d.unnoted,        (d, on) => d.unnoted = on),
        };

        /// <summary>
        ///     Turning a bare flag off removes its opcode, so the next encode does not carry it.
        /// </summary>
        /// <remarks>
        ///     membersOnly is bound to an editable grid column. If this regresses, every "Members"
        ///     tick the user clears is written straight back out from the recorded record: the row
        ///     changes, the save reports success and the item stays members-only in the cache.
        /// </remarks>
        [Fact]
        public void ABareFlagTurnedOff_IsRemovedFromTheEncodedStream()
        {
            foreach ((int opcode, string name, var carried, var setCarried) in BareFlags)
            {
                //Opcode 115 gives the record something to keep, so a dropped flag is
                //distinguishable from an encoder that lost the whole record.
                var definition = new ItemDefinition();
                definition.Decode(new JagStream(new byte[] { 115, 3, (byte)opcode, 0 }));
                Assert.True(carried(definition), $"{name}: opcode {opcode} did not decode as carried");

                setCarried(definition, false);

                byte[] encoded = definition.Encode().ToArray();
                Assert.Equal(new byte[] { 115, 3, 0 }, encoded);

                var reread = new ItemDefinition();
                reread.Decode(new JagStream(encoded));
                Assert.False(carried(reread), $"{name}: opcode {opcode} came back after being cleared");
                Assert.Equal(3, reread.teamId);
            }
        }

        /// <summary>
        ///     Turning a bare flag on emits its opcode, even on a record that never carried it.
        /// </summary>
        [Fact]
        public void ABareFlagTurnedOn_IsAppendedToTheEncodedStream()
        {
            foreach ((int opcode, string name, var carried, var setCarried) in BareFlags)
            {
                var definition = new ItemDefinition();
                definition.Decode(new JagStream(new byte[] { 115, 3, 0 }));
                Assert.False(carried(definition), $"{name}: opcode {opcode} was carried by a record without it");

                setCarried(definition, true);

                //115 was recorded so it keeps its place; the new opcode is appended after it.
                byte[] encoded = definition.Encode().ToArray();
                Assert.Equal(new byte[] { 115, 3, (byte)opcode, 0 }, encoded);

                var reread = new ItemDefinition();
                reread.Decode(new JagStream(encoded));
                Assert.True(carried(reread), $"{name}: opcode {opcode} did not survive being set");
            }
        }

        /// <summary>
        ///     A record that never carried a bare flag reports the client-side default for it and
        ///     encodes without it.
        /// </summary>
        [Fact]
        public void ARecordThatNeverCarriedABareFlag_KeepsTheDefaultAndEncodesWithoutIt()
        {
            var definition = new ItemDefinition();
            definition.Decode(new JagStream(new byte[] { 115, 3, 0 }));

            Assert.Equal(0, definition.stackable);
            Assert.False(definition.membersOnly);
            Assert.False(definition.unnoted);
            Assert.Equal(new byte[] { 115, 3, 0 }, definition.Encode().ToArray());
        }

        /// <summary>
        ///     A record carrying every bare flag re-encodes to the bytes it came from when nothing
        ///     is edited.
        /// </summary>
        /// <remarks>
        ///     The regression guard for the tests above: no setter runs for an item the user merely
        ///     opened, so the recorded record has to replay untouched, opcode order included.
        /// </remarks>
        [Fact]
        public void BareFlagsLeftUntouched_ReEncodeToTheirStoredBytes()
        {
            byte[] captured = { 65, 115, 3, 11, 16, 0 };

            var definition = new ItemDefinition();
            var stream = new JagStream(captured);
            definition.Decode(stream);

            Assert.Equal(captured.Length, stream.Position);
            Assert.Equal(1, definition.stackable);
            Assert.True(definition.membersOnly);
            Assert.True(definition.unnoted);
            Assert.Equal(captured, definition.Encode().ToArray());
        }

        /// <summary>
        ///     Clearing a bare flag removes every occurrence of its opcode, not merely the last.
        /// </summary>
        /// <remarks>
        ///     A superseded occurrence is normally replayed from the bytes it was read from, which
        ///     is what keeps the three hundred items that repeat an opcode byte-exact. A bare flag
        ///     has no bytes beyond the opcode itself, so replaying it would leave an earlier copy
        ///     behind and the client would still read the flag as set - the edit would look applied
        ///     and do nothing. Repeated occurrences of a flag left switched on are still both
        ///     written, which is the half this must not break.
        /// </remarks>
        [Fact]
        public void ClearingARepeatedBareFlag_RemovesEveryOccurrence()
        {
            byte[] captured = { 16, 115, 3, 16, 0 };

            var stored = new ItemDefinition();
            stored.Decode(new JagStream(captured));
            Assert.Equal(captured, stored.Encode().ToArray());

            var edited = new ItemDefinition();
            edited.Decode(new JagStream(captured));
            edited.membersOnly = false;

            byte[] encoded = edited.Encode().ToArray();
            Assert.Equal(new byte[] { 115, 3, 0 }, encoded);

            var reread = new ItemDefinition();
            reread.Decode(new JagStream(encoded));
            Assert.False(reread.membersOnly);
        }
    }
}
