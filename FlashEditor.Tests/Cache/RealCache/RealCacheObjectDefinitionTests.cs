using FlashEditor.Cache;
using FlashEditor.Definitions;
using FlashEditor.Tests.Cache.RealCache;
using System;
using Xunit;
using Xunit.Abstractions;
using FlashEditor.IO;
using FlashEditor.Definitions.Entities;

namespace FlashEditor.Tests.Cache.RealCache
{
    /// <summary>
    ///     Runs every object definition in the real revision-639 cache through the production
    ///     decoder and encoder.
    /// </summary>
    /// <remarks>
    ///     The decoders in this project were derived from a build-637 Java client while the cache
    ///     is build 639 - see AGENTS.md. For most formats that gap is hard to test for, but an
    ///     object definition is an opcode stream: read an opcode byte, read the payload its
    ///     opcode implies, repeat until opcode 0. Nothing in the stream states how long a payload
    ///     is, so a decoder that mis-sizes one payload by a single byte desynchronises and every
    ///     later opcode is read from the wrong offset. Landing exactly on the terminator after
    ///     tens of thousands of definitions is therefore strong evidence that every payload size
    ///     the decoder believes in is the size the data actually uses.
    ///     <para>
    ///     The three tests are deliberately ordered by strength - decodes at all, consumes its
    ///     buffer exactly, re-encodes to the same bytes - and each sweeps independently so a
    ///     failure of the strongest does not hide the result of the weaker ones.
    ///     </para>
    /// </remarks>
    public class RealCacheObjectDefinitionTests : IClassFixture<RealCacheFixture>
    {
        private readonly RealCacheFixture _cache;
        private readonly ITestOutputHelper _output;

        /// <summary>Binds the shared open cache and the per-test output sink.</summary>
        public RealCacheObjectDefinitionTests(RealCacheFixture cache, ITestOutputHelper output)
        {
            _cache = cache;
            _output = output;
        }

        /// <summary>
        ///     The object index bound to the production codec, for the shared byte-identity
        ///     harness.
        /// </summary>
        /// <remarks>
        ///     Empty files are passed over rather than reported. This index is the only one of the
        ///     four that holds any, and a zero-byte record cannot carry a terminator and re-encodes
        ///     to the one byte an empty definition encodes to, so scoring it would report a
        ///     difference the cache genuinely contains. The count is printed on every run.
        /// </remarks>
        /// <returns>A sweep over every object definition the cache declares.</returns>
        private DefinitionSweep<ObjectDefinition> Sweep()
        {
            return new DefinitionSweep<ObjectDefinition>(_cache, _output,
                RSConstants.OBJECTS_DEFINITIONS_INDEX,
                new DefinitionCodec<ObjectDefinition>("object",
                    (id, stream) =>
                    {
                        var definition = new ObjectDefinition { id = id };
                        definition.Decode(stream);
                        return definition;
                    },
                    definition => definition.Encode(),
                    definition => DefinitionCodec.FromHitMap(definition.decoded)))
                .SkippingEmptyRecords();
        }

        // ===================================================================
        //  1 - decodes at all
        // ===================================================================

        /// <summary>
        ///     Every object definition in the cache must come out of the decoder without throwing.
        /// </summary>
        /// <remarks>
        ///     The weakest of the three checks, and on its own nearly worthless: a decoder that
        ///     mis-sized a payload would usually still "succeed", because a desynchronised stream
        ///     mostly lands on opcodes that happen to be handled. It is here so that a hard
        ///     failure is reported as a hard failure rather than being folded into the exactness
        ///     count.
        /// </remarks>
        [RealCacheFact]
        public void AllObjectDefinitions_DecodeWithoutThrowing()
        {
            Sweep().AssertEveryRecordDecodes();
        }

        // ===================================================================
        //  2 - consumes its buffer exactly
        // ===================================================================

        /// <summary>
        ///     Every object definition must be consumed to its last byte, stopping on the stream's
        ///     own terminator rather than on the end of the buffer.
        /// </summary>
        /// <remarks>
        ///     Asserting the position equals the length would not be enough on its own.
        ///     <see cref="JagStream.ReadByte"/> returns -1 at the end of the buffer and the decode
        ///     loop treats that exactly like the opcode-0 terminator, so a definition whose parse
        ///     ran off the end would leave the position sitting on the length and look perfect.
        ///     Decoding a copy padded with non-zero sentinel bytes separates the two cases: a
        ///     decoder that stops on the real terminator finishes on the original length, and one
        ///     that overruns advances into the padding and is reported as an overrun.
        /// </remarks>
        [RealCacheFact]
        public void AllObjectDefinitions_ConsumeTheirBufferExactly()
        {
            Sweep().AssertExactConsumption();
        }

        // ===================================================================
        //  3 - re-encodes to the same bytes
        // ===================================================================

        /// <summary>
        ///     Every object definition must re-encode to the exact bytes it was decoded from.
        /// </summary>
        /// <remarks>
        ///     The editor rewrites a definition through this encoder whenever the user saves one,
        ///     so any field the decoder understands but the encoder drops, reorders or resizes is
        ///     lost the first time the definition is touched. Re-encoding is driven by the
        ///     decoder's opcode hit map rather than by field values, which is what makes an exact
        ///     match achievable; this test is what turns that design claim into a measurement.
        /// </remarks>
        [RealCacheFact]
        public void AllObjectDefinitions_ReEncodeToTheirCapturedBytes()
        {
            Sweep().AssertReEncodesToCapturedBytes();
        }

        // ===================================================================
        //  walkable
        // ===================================================================

        /// <summary>
        ///     Counts how many real definitions carry the walk-blocking opcodes, so the severity of
        ///     any defect in how <see cref="ObjectDefinition.walkable"/> is written back is a
        ///     measured number rather than a guess.
        /// </summary>
        [RealCacheFact]
        public void Walkable_IsSetByAMeasuredShareOfRealDefinitions()
        {
            int blocked = 0;
            int op17 = 0;
            int op18 = 0;

            DefinitionSweepResult swept = Sweep().ForEachDecoded((record, def) =>
            {
                if (def.decoded[17])
                    op17++;
                if (def.decoded[18])
                    op18++;
                if (!def.walkable)
                    blocked++;
            });

            _output.WriteLine($"{blocked} of {swept.Passed} object definitions are not walkable " +
                              $"({op17} carry opcode 17, {op18} carry opcode 18)");

            //Stated as a floor rather than an exact figure so the test pins the fact that the
            //field is in real use without breaking when the cache is swapped for another build.
            Assert.True(blocked > 1000,
                $"only {blocked} of {swept.Passed} definitions block walking, which is too few for this " +
                "cache - the walk-blocking opcodes are probably being misread");
        }

        /// <summary>
        ///     Pins the round trip of <see cref="ObjectDefinition.walkable"/> through an edit: a
        ///     definition made unwalkable must encode to bytes that decode back as unwalkable.
        /// </summary>
        /// <remarks>
        ///     Needs no cache. The encoder emits opcodes 17 and 18 from the decoder's hit map, so
        ///     before the fix a walkable flag set in the UI on a definition that arrived without
        ///     either opcode was written back out as though it had never been touched. Both
        ///     directions are checked because clearing the flag was equally lossy in reverse.
        /// </remarks>
        [Fact]
        public void Walkable_SurvivesAnEditThroughEncodeAndDecode()
        {
            var original = new ObjectDefinition { name = "Gate" };
            Assert.True(original.walkable);

            original.walkable = false;
            ObjectDefinition blocked = ObjectDefinition.DecodeFromStream(new JagStream(original.Encode().ToArray()));
            Assert.False(blocked.walkable);

            blocked.walkable = true;
            ObjectDefinition cleared = ObjectDefinition.DecodeFromStream(new JagStream(blocked.Encode().ToArray()));
            Assert.True(cleared.walkable);
        }

        // ===================================================================
        //  Bare flags
        // ===================================================================

        /// <summary>
        ///     Every presence-only opcode on an object definition other than the walk-blocking
        ///     pair, with the accessor that reads and writes it phrased as "the stream carries
        ///     this opcode".
        /// </summary>
        /// <remarks>
        ///     Opcodes 17 and 18 are left out because they are two opcodes behind one property and
        ///     are covered by <see cref="Walkable_SurvivesAnEditThroughEncodeAndDecode"/>. Opcode
        ///     64 reads inverted - its presence suppresses the shadow - which is why the table
        ///     states carriage rather than the raw property value.
        /// </remarks>
        private static readonly (int Opcode, string Name,
            Func<ObjectDefinition, bool> Carried, Action<ObjectDefinition, bool> SetCarried)[] BareFlags =
        {
            (22,  "isClipped",           d => d.isClipped,            (d, on) => d.isClipped = on),
            (62,  "flipped",             d => d.flipped,              (d, on) => d.flipped = on),
            (64,  "castsShadow",         d => !d.castsShadow,         (d, on) => d.castsShadow = !on),
            (73,  "obstructsWheelchair", d => d.obstructsWheelchair,  (d, on) => d.obstructsWheelchair = on),
            (74,  "isSolid",             d => d.isSolid,              (d, on) => d.isSolid = on),
            (82,  "mergeNormals",        d => d.mergeNormals,         (d, on) => d.mergeNormals = on),
            (88,  "noShadow",            d => d.noShadow,             (d, on) => d.noShadow = on),
            (89,  "noDecor",             d => d.noDecor,              (d, on) => d.noDecor = on),
            (90,  "unknownFlag90",       d => d.unknownFlag90,        (d, on) => d.unknownFlag90 = on),
            (91,  "unknownFlag91",       d => d.unknownFlag91,        (d, on) => d.unknownFlag91 = on),
            (96,  "unknownFlag96",       d => d.unknownFlag96,        (d, on) => d.unknownFlag96 = on),
            (97,  "unknownFlag97",       d => d.unknownFlag97,        (d, on) => d.unknownFlag97 = on),
            (98,  "unknownFlag98",       d => d.unknownFlag98,        (d, on) => d.unknownFlag98 = on),
            (105, "unknownFlag105",      d => d.unknownFlag105,       (d, on) => d.unknownFlag105 = on),
            (168, "unknownFlag168",      d => d.unknownFlag168,       (d, on) => d.unknownFlag168 = on),
            (169, "unknownFlag169",      d => d.unknownFlag169,       (d, on) => d.unknownFlag169 = on),
            (177, "unknownFlag177",      d => d.unknownFlag177,       (d, on) => d.unknownFlag177 = on),
            (189, "unknownFlag189",      d => d.unknownFlag189,       (d, on) => d.unknownFlag189 = on),
        };

        /// <summary>
        ///     Turning a bare flag off removes its opcode, so the next encode does not carry it.
        /// </summary>
        /// <remarks>
        ///     This is the defect the flag properties exist to fix, and the same one
        ///     <see cref="ObjectDefinition.walkable"/> was fixed for first. The encoder replays the
        ///     opcodes the decoder recorded, so a flag held in an ordinary field could be cleared
        ///     and then written straight back out from the recording: the grid row would change,
        ///     the save would report success and the definition in the cache would be untouched.
        /// </remarks>
        [Fact]
        public void ABareFlagTurnedOff_IsRemovedFromTheEncodedStream()
        {
            foreach ((int opcode, string name, var carried, var setCarried) in BareFlags)
            {
                //Opcode 14 gives the definition something to keep, so a dropped flag is
                //distinguishable from an encoder that lost the whole record.
                ObjectDefinition definition = ObjectDefinition.DecodeFromStream(
                    new JagStream(new byte[] { 14, 2, (byte)opcode, 0 }));
                Assert.True(carried(definition), $"{name}: opcode {opcode} did not decode as carried");

                setCarried(definition, false);

                byte[] encoded = definition.Encode().ToArray();
                Assert.Equal(new byte[] { 14, 2, 0 }, encoded);

                ObjectDefinition reread = ObjectDefinition.DecodeFromStream(new JagStream(encoded));
                Assert.False(carried(reread), $"{name}: opcode {opcode} came back after being cleared");
                Assert.Equal(2, reread.sizeX);
            }
        }

        /// <summary>
        ///     Turning a bare flag on emits its opcode, even on a definition that never carried it.
        /// </summary>
        /// <remarks>
        ///     The other half of the same defect. A flag read off the opcode hit map has to be
        ///     written to that map when it is set, or nothing the user ticks reaches the file.
        /// </remarks>
        [Fact]
        public void ABareFlagTurnedOn_IsAppendedToTheEncodedStream()
        {
            foreach ((int opcode, string name, var carried, var setCarried) in BareFlags)
            {
                ObjectDefinition definition = ObjectDefinition.DecodeFromStream(
                    new JagStream(new byte[] { 14, 2, 0 }));
                Assert.False(carried(definition), $"{name}: opcode {opcode} was carried by a stream without it");

                setCarried(definition, true);

                //14 was recorded so it keeps its place; the new opcode is appended after it.
                byte[] encoded = definition.Encode().ToArray();
                Assert.Equal(new byte[] { 14, 2, (byte)opcode, 0 }, encoded);

                ObjectDefinition reread = ObjectDefinition.DecodeFromStream(new JagStream(encoded));
                Assert.True(carried(reread), $"{name}: opcode {opcode} did not survive being set");
            }
        }

        /// <summary>
        ///     A definition that never carried a bare flag reports the client-side default for it
        ///     and encodes without it.
        /// </summary>
        /// <remarks>
        ///     The flags are views over the opcode hit map rather than fields with initialisers, so
        ///     the default now has to come out of an absent opcode. Get the polarity wrong for one
        ///     of them and the encoder invents that opcode for every definition in the cache -
        ///     56,199 records grow by a byte the first time anyone saves one.
        /// </remarks>
        [Fact]
        public void ADefinitionThatNeverCarriedABareFlag_KeepsTheDefaultAndEncodesWithoutIt()
        {
            ObjectDefinition definition = ObjectDefinition.DecodeFromStream(new JagStream(new byte[] { 14, 2, 0 }));

            //castsShadow is the only one the client assumes true, so it is the only inverted view.
            Assert.True(definition.castsShadow);
            foreach ((int opcode, string name, var carried, _) in BareFlags)
                Assert.False(carried(definition), $"{name}: opcode {opcode} reported as carried by a stream without it");

            Assert.Equal(new byte[] { 14, 2, 0 }, definition.Encode().ToArray());
        }

        /// <summary>
        ///     A definition carrying every bare flag re-encodes to the bytes it came from when
        ///     nothing is edited.
        /// </summary>
        /// <remarks>
        ///     The regression guard for the two tests above. Making a flag droppable is only safe
        ///     if nothing drops on its own: no setter runs for a definition the user merely opened,
        ///     so the recorded stream has to replay untouched, opcode order included. The stream
        ///     below is deliberately not in ascending order.
        /// </remarks>
        [Fact]
        public void BareFlagsLeftUntouched_ReEncodeToTheirStoredBytes()
        {
            byte[] stream =
            {
                189, 22, 14, 2, 62, 64, 73, 74, 82, 88, 89, 90, 91,
                96, 97, 98, 105, 168, 169, 177, 0
            };

            AssertConsumedExactlyAndReEncoded(stream, def =>
            {
                Assert.True(def.isClipped);
                Assert.False(def.castsShadow);
                Assert.True(def.unknownFlag189);
                Assert.Equal(2, def.sizeX);
            });
        }

        /// <summary>
        ///     Clearing a bare flag removes every occurrence of its opcode, not merely the last.
        /// </summary>
        /// <remarks>
        ///     Repeated opcodes are replayed from the bytes they were read from, which is what
        ///     keeps the definitions that store one twice byte-exact. Dropping only the last
        ///     occurrence would leave an earlier copy in the stream and the client would still read
        ///     the flag as set, so the edit would look applied and do nothing.
        /// </remarks>
        [Fact]
        public void ClearingARepeatedBareFlag_RemovesEveryOccurrence()
        {
            ObjectDefinition definition = ObjectDefinition.DecodeFromStream(
                new JagStream(new byte[] { 22, 14, 2, 22, 0 }));
            Assert.True(definition.isClipped);

            definition.isClipped = false;

            Assert.Equal(new byte[] { 14, 2, 0 }, definition.Encode().ToArray());
        }

        // ===================================================================
        //  Format facts, pinned without needing the cache
        // ===================================================================

        /// <summary>
        ///     Opcode 75 carries a one byte payload rather than being a bare flag.
        /// </summary>
        /// <remarks>
        ///     Read as a flag it swallows nothing, so the byte after it is taken for the next
        ///     opcode and the rest of the definition is parsed from the wrong offset. 1,591
        ///     definitions in the shipped cache carry it. The build-637 client reads it as
        ///     <c>readUnsignedByte</c> into <c>Class352.anInt2975</c>.
        /// </remarks>
        [Fact]
        public void Opcode75_CarriesAOneBytePayload()
        {
            //75 with payload 7, then opcode 23 - a bare flag - then the terminator.
            AssertConsumedExactlyAndReEncoded(new byte[] { 75, 7, 23, 0 }, def => Assert.True(def.decoded[23]));
        }

        /// <summary>
        ///     Opcode 72 carries a signed short, the same shape as the offsets at 70 and 71.
        /// </summary>
        /// <remarks>
        ///     Read as a single byte it leaves the low half of the short behind, which the decoder
        ///     then reads as an opcode. 371 definitions in the cache carry it. The build-637
        ///     client reads it as <c>readShort() &lt;&lt; 2</c> into <c>Class352.anInt2946</c>.
        /// </remarks>
        [Fact]
        public void Opcode72_CarriesASignedShortPayload()
        {
            AssertConsumedExactlyAndReEncoded(new byte[] { 72, 0xFF, 0xE2, 23, 0 }, def => Assert.True(def.decoded[23]));
        }

        /// <summary>
        ///     An opcode the stream carried is written back even when its payload happens to equal
        ///     the field's default.
        /// </summary>
        /// <remarks>
        ///     Emitting on the field value alone drops a stored <c>19 00</c> or <c>70 00 00</c>,
        ///     which changes the definition's bytes - and so its CRC - the first time the user
        ///     saves a file they never edited.
        /// </remarks>
        [Fact]
        public void OpcodesWhosePayloadEqualsTheDefault_AreStillWrittenBack()
        {
            //19 with category 0, then 70 with offset 0: both defaults, both present in the stream.
            AssertConsumedExactlyAndReEncoded(new byte[] { 19, 0, 70, 0, 0, 0 },
                def => Assert.Equal(0, def.category));
        }

        /// <summary>
        ///     Opcodes are written back in the order the stream presented them, not in ascending
        ///     order.
        /// </summary>
        /// <remarks>
        ///     The definitions in the cache are not stored in ascending opcode order, so an
        ///     encoder with its own fixed order rewrites all but a handful of them.
        /// </remarks>
        [Fact]
        public void OpcodeOrder_IsTakenFromTheStreamRatherThanFromTheEncoder()
        {
            //15 before 14, which is the reverse of the order the encoder would pick on its own.
            AssertConsumedExactlyAndReEncoded(new byte[] { 15, 3, 14, 2, 0 }, def =>
            {
                Assert.Equal(2, def.sizeX);
                Assert.Equal(3, def.sizeY);
            });
        }

        /// <summary>
        ///     A repeated opcode is written back at every position it occupied, keeping the value
        ///     each occurrence carried.
        /// </summary>
        /// <remarks>
        ///     268 definitions in the cache repeat an opcode with a different value each time. The
        ///     decoder keeps only the last, as the client does, so the earlier occurrences can be
        ///     reproduced only from the bytes they were read from.
        /// </remarks>
        [Fact]
        public void RepeatedOpcodes_KeepBothTheirPositionsAndTheirValues()
        {
            AssertConsumedExactlyAndReEncoded(new byte[] { 19, 4, 19, 9, 0 },
                def => Assert.Equal(9, def.category));
        }

        /// <summary>
        ///     A definition carrying both ambient sound opcodes keeps both.
        /// </summary>
        /// <remarks>
        ///     44 definitions in the cache carry 78 and 79 together. The two write the same fields,
        ///     so only the one read last still has its values to re-encode from; dropping the other
        ///     would shorten the definition.
        /// </remarks>
        [Fact]
        public void BothAmbientSoundOpcodesTogether_SurviveAReEncode()
        {
            byte[] stream =
            {
                78, 0x03, 0xE8, 0x05,                         // id 1000, 5 loops
                79, 0x03, 0xE9, 0x01, 0xF4, 0x02, 1, 0x01, 0x2C, // id 1001, extra 500, 2 loops, one sound 300
                0
            };

            AssertConsumedExactlyAndReEncoded(stream, def =>
            {
                Assert.Equal(1001, def.ambientSoundId);
                Assert.Single(def.extraSounds);
                Assert.Equal(300, def.extraSounds[0]);
            });
        }

        /// <summary>
        ///     Decodes a hand-built definition, checks it landed on the terminator rather than the
        ///     end of the buffer, and checks it re-encodes to the bytes it came from.
        /// </summary>
        /// <param name="stream">The definition bytes, terminator included.</param>
        /// <param name="check">Field assertions for the decoded definition.</param>
        private static void AssertConsumedExactlyAndReEncoded(byte[] stream, Action<ObjectDefinition> check)
        {
            byte[] guarded = new byte[stream.Length + 1];
            Array.Copy(stream, guarded, stream.Length);

            var reader = new JagStream(guarded);
            var def = new ObjectDefinition();
            def.Decode(reader);

            Assert.Equal(stream.Length, reader.Position);
            check(def);
            Assert.Equal(stream, def.Encode().ToArray());
        }
    }
}
