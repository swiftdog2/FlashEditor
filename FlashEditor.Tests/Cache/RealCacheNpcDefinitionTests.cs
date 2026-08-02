using FlashEditor.cache;
using FlashEditor.Tests.Cache.RealCache;
using System;
using System.Collections.Generic;
using System.Linq;
using Xunit;
using Xunit.Abstractions;

namespace FlashEditor.Tests.Cache
{
    /// <summary>
    ///     Runs every NPC definition in the cache through the production codec.
    /// </summary>
    /// <remarks>
    ///     The NPC decoder was derived from the Java client bundled with this cache, and that
    ///     client is build 637 while the cache is build 639 - see AGENTS.md. Unlike a model, an
    ///     NPC definition is a self-delimiting opcode stream: read an opcode byte, read the
    ///     payload its number implies, repeat until the terminator. Nothing in the file states
    ///     how long a payload is, so a decoder that mis-sizes even one of them consumes the
    ///     following opcode's bytes as payload and every field after that is garbage.
    ///     <para>
    ///     That makes exact buffer consumption the sharp instrument here. If the whole opcode
    ///     table is right the last byte read is the terminator and it is the last byte of the
    ///     buffer; if any payload size is wrong the walk almost certainly ends somewhere else.
    ///     Across 13,359 definitions the chance of a wrong table landing on the end every single
    ///     time is negligible, so this is as close to a proof of the 639 field layout as the
    ///     data can give.
    ///     </para>
    /// </remarks>
    public class RealCacheNpcDefinitionTests : IClassFixture<RealCacheFixture>
    {
        private readonly RealCacheFixture _cache;
        private readonly ITestOutputHelper _output;

        /// <summary>Failures listed before the report is truncated.</summary>
        private const int MaxReportedFailures = 10;

        /// <summary>
        ///     Sentinel bytes appended past the end of a definition so an over-read is visible.
        /// </summary>
        /// <remarks>
        ///     <see cref="JagStream.ReadByte"/> returns -1 at end of stream without advancing,
        ///     and the decode loop treats anything below 1 as the terminator. A decoder that ran
        ///     off the end would therefore still report a position equal to the length and look
        ///     exact. Decoding a padded copy removes that blind spot: reading past the true end
        ///     now advances into the padding and shows up as a positive overshoot.
        /// </remarks>
        private const int SentinelPadding = 32;

        /// <summary>A non-zero, non-255 pad byte, so it cannot be mistaken for a terminator.</summary>
        private const byte SentinelByte = 0xAA;

        /// <summary>Binds the shared open cache and the per-test output sink.</summary>
        public RealCacheNpcDefinitionTests(RealCacheFixture cache, ITestOutputHelper output)
        {
            _cache = cache;
            _output = output;
        }

        /// <summary>
        ///     Decodes every NPC definition and requires each one to land exactly on the end of
        ///     its own buffer - no bytes left over, none read past the end.
        /// </summary>
        [RealCacheFact]
        public void AllNpcDefinitions_Decode_AndConsumeTheirBufferExactly()
        {
            var failures = new List<string>();
            int decoded = 0;
            long bytes = 0;

            foreach ((int npcId, byte[] data) in Definitions())
            {
                decoded++;
                bytes += data.Length;

                //The genuine bytes first: this is the "decodes without throwing" assertion.
                try
                {
                    NPCDefinition unused = new NPCDefinition(new JagStream(data));
                }
                catch (Exception ex)
                {
                    failures.Add($"npc {npcId} ({data.Length} bytes): decode threw {ex.GetType().Name}: {ex.Message}");
                    continue;
                }

                //Then the padded copy, which is what actually pins the opcode payload sizes.
                long consumed;
                try
                {
                    var padded = new JagStream(Pad(data));
                    NPCDefinition unused = new NPCDefinition(padded);
                    consumed = padded.Position;
                }
                catch (Exception ex)
                {
                    failures.Add($"npc {npcId} ({data.Length} bytes): decode ran past the end - " +
                                 $"{ex.GetType().Name}: {ex.Message}");
                    continue;
                }

                if (consumed != data.Length)
                {
                    failures.Add($"npc {npcId}: consumed {consumed} of {data.Length} bytes " +
                                 $"({consumed - data.Length:+#;-#;0})");
                }
            }

            _output.WriteLine($"{decoded} NPC definitions decoded, {bytes} bytes consumed exactly");
            if (!_cache.FullSweep)
            {
                _output.WriteLine($"sampled up to {RealCacheFixture.SampleArchivesPerIndex} archives; " +
                                  $"set {RealCacheLocator.FullSweepVariable}=1 to decode every definition");
            }

            Assert.True(decoded > 0, "no NPC definition was decoded, so nothing was checked");
            AssertNoFailures(failures, "NPC definitions did not consume their buffer exactly");
        }

        /// <summary>
        ///     Re-encodes every decoded definition and requires the encoder to survive the real
        ///     data and to settle after one round trip.
        /// </summary>
        /// <remarks>
        ///     Byte-identity against the cache is asserted separately by
        ///     <see cref="AllNpcDefinitions_ReEncodeToTheCapturedBytes"/>. What this test pins is
        ///     the weaker but independent property that the encoder's own output is a well-formed
        ///     opcode stream: encode, decode, encode again, and the two encodings must be
        ///     byte-identical while the decode consumes exactly what the encode produced. That is
        ///     the property a save path depends on once a definition has actually been edited,
        ///     and it is what caught the opcode-121 record-count defect already covered by
        ///     NPCDefinitionCodecTests.
        ///     <para>
        ///     The two are not the same check. Byte-identity compares against the cache and so
        ///     depends on the recorded opcode order surviving; this one compares the encoder
        ///     against itself and would still catch a payload the encoder writes in a shape its
        ///     own decoder reads back differently, which no comparison with the cache reaches.
        ///     </para>
        /// </remarks>
        [RealCacheFact]
        public void AllNpcDefinitions_ReEncode_WithoutLossThroughTheirOwnDecoder()
        {
            var failures = new List<string>();
            int encoded = 0;
            int byteIdentical = 0;

            foreach ((int npcId, byte[] data) in Definitions())
            {
                NPCDefinition definition;
                try
                {
                    definition = new NPCDefinition(new JagStream(data));
                }
                catch (Exception)
                {
                    //Already reported by the decode test; nothing to add here.
                    continue;
                }

                byte[] first;
                try
                {
                    first = definition.Encode().ToArray();
                    encoded++;
                }
                catch (Exception ex)
                {
                    failures.Add($"npc {npcId}: encode threw {ex.GetType().Name}: {ex.Message}");
                    continue;
                }

                if (first.Length == data.Length && first.AsSpan().SequenceEqual(data))
                    byteIdentical++;

                //The encoder's own output must be a well-formed opcode stream, or the editor
                //writes a cache it cannot read back.
                long consumed;
                byte[] second;
                try
                {
                    var padded = new JagStream(Pad(first));
                    NPCDefinition reread = new NPCDefinition(padded);
                    consumed = padded.Position;
                    second = reread.Encode().ToArray();
                }
                catch (Exception ex)
                {
                    failures.Add($"npc {npcId}: re-decoding the encoded stream threw " +
                                 $"{ex.GetType().Name}: {ex.Message}");
                    continue;
                }

                if (consumed != first.Length)
                {
                    failures.Add($"npc {npcId}: the encoded stream is {first.Length} bytes but " +
                                 $"re-decoding consumed {consumed}");
                    continue;
                }

                if (!second.AsSpan().SequenceEqual(first))
                {
                    failures.Add($"npc {npcId}: encode is not stable - {first.Length} bytes " +
                                 $"became {second.Length} on the second pass");
                }
            }

            _output.WriteLine($"{encoded} NPC definitions re-encoded, {byteIdentical} byte-identical to the cache");
            _output.WriteLine("byte-identity itself is asserted by " +
                              nameof(AllNpcDefinitions_ReEncodeToTheCapturedBytes));
            if (!_cache.FullSweep)
            {
                _output.WriteLine($"sampled up to {RealCacheFixture.SampleArchivesPerIndex} archives; " +
                                  $"set {RealCacheLocator.FullSweepVariable}=1 to encode every definition");
            }

            Assert.True(encoded > 0, "no NPC definition was encoded, so nothing was checked");
            AssertNoFailures(failures, "NPC definitions did not survive a re-encode");
        }

        /// <summary>
        ///     Every NPC definition must re-encode to the exact bytes the cache stores for it.
        /// </summary>
        /// <remarks>
        ///     The editor rewrites a definition through this encoder whenever the user saves one,
        ///     so anything the encoder reorders, duplicates or drops changes the archive and its
        ///     CRC for a definition nobody edited. The comparison is against the captured cache
        ///     bytes rather than against a second encode of the codec's own output, because the
        ///     latter would pass just as happily on an encoder that agreed with itself about the
        ///     wrong answer.
        ///     <para>
        ///     Three properties have to hold at once for this to pass, and each was separately
        ///     broken: the recorded opcode order has to be replayed, since only 127 of the 13,359
        ///     definitions store their opcodes in ascending order; a repeated opcode has to keep
        ///     every occurrence, since 538 definitions repeat one and the decoder keeps only the
        ///     last value; and an opcode stored at its field default has to be written back
        ///     rather than skipped as redundant.
        ///     </para>
        /// </remarks>
        [RealCacheFact]
        public void AllNpcDefinitions_ReEncodeToTheCapturedBytes()
        {
            var failures = new List<string>();
            var opcodesInFailures = new SortedDictionary<int, int>();
            int identical = 0;
            int reordered = 0;

            foreach ((int npcId, byte[] data) in Definitions())
            {
                NPCDefinition definition;
                byte[] reencoded;

                try
                {
                    definition = new NPCDefinition(new JagStream(data));
                    definition.SetId(npcId);
                    reencoded = definition.Encode().ToArray();
                }
                catch (Exception ex)
                {
                    failures.Add($"npc {npcId}: re-encode threw {ex.GetType().Name}: {ex.Message}");
                    continue;
                }

                if (reencoded.AsSpan().SequenceEqual(data))
                {
                    identical++;
                    continue;
                }

                //Same multiset of bytes means the content survived and only the layout moved,
                //which points at the opcode order rather than at a mis-encoded payload.
                byte[] storedSorted = (byte[])data.Clone();
                byte[] reencodedSorted = (byte[])reencoded.Clone();
                Array.Sort(storedSorted);
                Array.Sort(reencodedSorted);
                bool sameBytes = storedSorted.AsSpan().SequenceEqual(reencodedSorted);
                if (sameBytes)
                    reordered++;

                int at = FirstDifference(data, reencoded);
                failures.Add($"npc {npcId}: re-encoded {reencoded.Length} bytes from a stored " +
                             $"{data.Length}, first difference at {at} " +
                             $"({ByteAt(data, at)} became {ByteAt(reencoded, at)}), " +
                             $"{(sameBytes ? "same bytes in a different order" : "different content")}; " +
                             $"opcodes {Opcodes(definition)}");
                Tally(opcodesInFailures, definition);
            }

            _output.WriteLine($"{identical} NPC definitions re-encoded to byte-identical output");
            if (reordered > 0)
            {
                _output.WriteLine($"{reordered} more carried the same bytes in a different order, " +
                                  "so the encoder is no longer replaying the stored opcode order");
            }
            if (opcodesInFailures.Count > 0)
                _output.WriteLine("opcodes seen in failing definitions: " + Histogram(opcodesInFailures));
            if (!_cache.FullSweep)
            {
                _output.WriteLine($"sampled up to {RealCacheFixture.SampleArchivesPerIndex} archives; " +
                                  $"set {RealCacheLocator.FullSweepVariable}=1 to encode every definition");
            }

            Assert.True(identical > 0, "no NPC definition was re-encoded, so nothing was checked");
            AssertNoFailures(failures, "NPC definitions did not re-encode to their stored bytes");
        }

        // ===================================================================
        //  Bare flags, pinned without needing the cache
        // ===================================================================

        /// <summary>
        ///     Every presence-only opcode on an NPC definition, with the accessor that reads and
        ///     writes it phrased as "the stream carries this opcode".
        /// </summary>
        /// <remarks>
        ///     Phrasing them all the same way is what lets one test cover the whole set. Four of
        ///     the seven booleans read inverted - opcode 93 is what removes the minimap dot, not
        ///     what adds it - so a table of raw property values would need a polarity column and a
        ///     reader would have to carry it in their head through every assertion below.
        /// </remarks>
        private static readonly (int Opcode, string Name,
            Func<NPCDefinition, bool> Carried, Action<NPCDefinition, bool> SetCarried)[] BareFlags =
        {
            (93,  "drawMinimapDot",    d => !d.drawMinimapDot,      (d, on) => d.drawMinimapDot = !on),
            (99,  "hasRenderPriority", d => d.hasRenderPriority,    (d, on) => d.hasRenderPriority = on),
            (107, "clickable",         d => !d.clickable,           (d, on) => d.clickable = !on),
            (109, "slowWalk",          d => !d.slowWalk,            (d, on) => d.slowWalk = !on),
            (111, "animateIdle",       d => !d.animateIdle,         (d, on) => d.animateIdle = !on),
            (141, "visiblePriority",   d => d.visiblePriority,      (d, on) => d.visiblePriority = on),
            (143, "invisiblePriority", d => d.invisiblePriority,    (d, on) => d.invisiblePriority = on),
            (158, "mainOptionIndex",   d => d.mainOptionIndex == 1, (d, on) => d.mainOptionIndex = (byte)(on ? 1 : 0)),
        };

        /// <summary>
        ///     Turning a bare flag off removes its opcode, so the next encode does not carry it.
        /// </summary>
        /// <remarks>
        ///     This is the defect the flag properties exist to fix. The encoder replays the opcodes
        ///     the decoder recorded, so a flag held in an ordinary field could be cleared in the
        ///     grid and then written straight back out from the recording: the row would change,
        ///     the save would report success and the definition in the cache would be untouched.
        ///     Two of these - clickable and drawMinimapDot - are bound to editable grid columns,
        ///     so a regression here is directly visible to the user.
        /// </remarks>
        [Fact]
        public void ABareFlagTurnedOff_IsRemovedFromTheEncodedStream()
        {
            foreach ((int opcode, string name, var carried, var setCarried) in BareFlags)
            {
                //Opcode 12 gives the definition something to keep, so a dropped flag is
                //distinguishable from an encoder that lost the whole record.
                var definition = new NPCDefinition(new JagStream(new byte[] { 12, 3, (byte)opcode, 0 }));
                Assert.True(carried(definition), $"{name}: opcode {opcode} did not decode as carried");

                setCarried(definition, false);

                byte[] encoded = definition.Encode().ToArray();
                Assert.Equal(new byte[] { 12, 3, 0 }, encoded);

                var reread = new NPCDefinition(new JagStream(encoded));
                Assert.False(carried(reread), $"{name}: opcode {opcode} came back after being cleared");
                Assert.Equal(3, reread.size);
            }
        }

        /// <summary>
        ///     Turning a bare flag on emits its opcode, even on a definition that never carried it.
        /// </summary>
        /// <remarks>
        ///     The other half of the same defect. A flag read off the opcode hit map is written
        ///     from the hit map, so setting one has to reach the map rather than only a field, or
        ///     ticking the box in the grid saves nothing at all.
        /// </remarks>
        [Fact]
        public void ABareFlagTurnedOn_IsAppendedToTheEncodedStream()
        {
            foreach ((int opcode, string name, var carried, var setCarried) in BareFlags)
            {
                var definition = new NPCDefinition(new JagStream(new byte[] { 12, 3, 0 }));
                Assert.False(carried(definition), $"{name}: opcode {opcode} was carried by a stream without it");

                setCarried(definition, true);

                //12 was recorded so it keeps its place; the new opcode is appended after it.
                byte[] encoded = definition.Encode().ToArray();
                Assert.Equal(new byte[] { 12, 3, (byte)opcode, 0 }, encoded);

                var reread = new NPCDefinition(new JagStream(encoded));
                Assert.True(carried(reread), $"{name}: opcode {opcode} did not survive being set");
            }
        }

        /// <summary>
        ///     A definition that never carried a bare flag reports the client-side default for it
        ///     and encodes without it.
        /// </summary>
        /// <remarks>
        ///     The flags are views over the opcode hit map rather than fields with initialisers,
        ///     so the default now has to come out of an absent opcode. Get the polarity wrong and
        ///     the encoder invents an opcode for every definition in the cache - 13,359 records
        ///     grow by a byte the first time anyone saves one.
        /// </remarks>
        [Fact]
        public void ADefinitionThatNeverCarriedABareFlag_KeepsTheDefaultAndEncodesWithoutIt()
        {
            var definition = new NPCDefinition(new JagStream(new byte[] { 12, 3, 0 }));

            //The client-side defaults, stated as the property values rather than as opcodes.
            Assert.True(definition.animateIdle);
            Assert.True(definition.clickable);
            Assert.True(definition.drawMinimapDot);
            Assert.True(definition.slowWalk);
            Assert.False(definition.hasRenderPriority);
            Assert.False(definition.visiblePriority);
            Assert.False(definition.invisiblePriority);
            Assert.Equal(0, definition.mainOptionIndex);

            Assert.Equal(new byte[] { 12, 3, 0 }, definition.Encode().ToArray());
        }

        /// <summary>
        ///     A definition carrying every bare flag re-encodes to the bytes it came from when
        ///     nothing is edited.
        /// </summary>
        /// <remarks>
        ///     The regression guard for the two tests above. Making a flag droppable is only safe
        ///     if nothing drops on its own: no setter runs for a definition the user merely
        ///     opened, so the recorded stream has to replay untouched, opcode order included. The
        ///     stream below is deliberately out of ascending order and carries 159 before 158, the
        ///     two flags that write the same field.
        /// </remarks>
        [Fact]
        public void BareFlagsLeftUntouched_ReEncodeToTheirStoredBytes()
        {
            byte[] stream = { 143, 12, 3, 111, 99, 159, 93, 107, 158, 141, 109, 95, 0, 42, 0 };

            AssertReEncodesToTheSameBytes(stream, def =>
            {
                Assert.False(def.animateIdle);
                Assert.False(def.clickable);
                Assert.False(def.drawMinimapDot);
                Assert.False(def.slowWalk);
                Assert.True(def.hasRenderPriority);
                Assert.True(def.visiblePriority);
                Assert.True(def.invisiblePriority);
                Assert.Equal(42, def.level);
            });
        }

        /// <summary>
        ///     Clearing a bare flag removes every occurrence of its opcode, not merely the last.
        /// </summary>
        /// <remarks>
        ///     Repeated opcodes are replayed from the bytes they were read from, which is what
        ///     keeps 538 definitions in the cache byte-exact. Dropping only the last occurrence
        ///     would leave an earlier copy in the stream and the client would still read the flag
        ///     as set, so the edit would look applied and do nothing.
        /// </remarks>
        [Fact]
        public void ClearingARepeatedBareFlag_RemovesEveryOccurrence()
        {
            var definition = new NPCDefinition(new JagStream(new byte[] { 93, 12, 3, 93, 0 }));
            Assert.False(definition.drawMinimapDot);

            definition.drawMinimapDot = true;

            Assert.Equal(new byte[] { 12, 3, 0 }, definition.Encode().ToArray());
        }

        /// <summary>
        ///     Where both opcode 158 and opcode 159 are present, the later one decides the index,
        ///     and both are still written back.
        /// </summary>
        /// <remarks>
        ///     158 and 159 are two bare flags writing one field, so the hit map alone cannot say
        ///     which value the client would end up with - only their positions can. Reading the
        ///     hit map alone reports 1 for a definition whose 159 came last, which is the value
        ///     the client would never see.
        /// </remarks>
        [Fact]
        public void MainOptionIndex_TakesTheLaterOfTheTwoFlagsThatWriteIt()
        {
            AssertReEncodesToTheSameBytes(new byte[] { 158, 159, 0 },
                def => Assert.Equal(0, def.mainOptionIndex));

            AssertReEncodesToTheSameBytes(new byte[] { 159, 158, 0 },
                def => Assert.Equal(1, def.mainOptionIndex));
        }

        /// <summary>
        ///     Setting the main option index to 1 drops a stored opcode 159 rather than leaving it
        ///     to overrule the edit.
        /// </summary>
        /// <remarks>
        ///     Opcode 159 resets the index to zero. Left in the recorded stream it would be
        ///     replayed after the 158 the setter added, and the client would read the index the
        ///     user had just changed away from.
        /// </remarks>
        [Fact]
        public void SettingTheMainOptionIndex_DropsTheFlagThatWouldOverruleIt()
        {
            var definition = new NPCDefinition(new JagStream(new byte[] { 12, 3, 159, 0 }));
            Assert.Equal(0, definition.mainOptionIndex);

            definition.mainOptionIndex = 1;

            byte[] encoded = definition.Encode().ToArray();
            Assert.Equal(new byte[] { 12, 3, 158, 0 }, encoded);
            Assert.Equal(1, new NPCDefinition(new JagStream(encoded)).mainOptionIndex);
        }

        /// <summary>
        ///     Setting the main option index to zero on a definition that stored opcode 159 leaves
        ///     that opcode alone.
        /// </summary>
        /// <remarks>
        ///     Zero is also what the client assumes with neither flag present, so the setter has
        ///     nothing to add - and inventing 159 would lengthen every definition that has no main
        ///     option at all, which is most of the index.
        /// </remarks>
        [Fact]
        public void SettingTheMainOptionIndexToZero_NeitherInventsNorRemovesOpcode159()
        {
            var stored = new NPCDefinition(new JagStream(new byte[] { 12, 3, 159, 0 }));
            stored.mainOptionIndex = 0;
            Assert.Equal(new byte[] { 12, 3, 159, 0 }, stored.Encode().ToArray());

            var never = new NPCDefinition(new JagStream(new byte[] { 12, 3, 0 }));
            never.mainOptionIndex = 0;
            Assert.Equal(new byte[] { 12, 3, 0 }, never.Encode().ToArray());
        }

        // ===================================================================
        //  Format facts, pinned without needing the cache
        // ===================================================================

        /// <summary>
        ///     Opcodes are written back in the order the stream presented them, not in ascending
        ///     order.
        /// </summary>
        /// <remarks>
        ///     Only 127 of the 13,359 definitions in the rev-639 cache store their opcodes in
        ///     ascending numeric order, so an encoder with its own fixed order rewrites 13,232 of
        ///     them the moment the user saves one they never touched.
        /// </remarks>
        [Fact]
        public void OpcodeOrder_IsTakenFromTheStreamRatherThanFromTheEncoder()
        {
            //95 before 12, which is the reverse of the order the encoder would pick on its own.
            AssertReEncodesToTheSameBytes(new byte[] { 95, 0, 42, 12, 3, 0 }, def =>
            {
                Assert.Equal(3, def.size);
                Assert.Equal(42, def.level);
            });
        }

        /// <summary>
        ///     A repeated opcode is written back at every position it occupied, keeping the value
        ///     each occurrence carried.
        /// </summary>
        /// <remarks>
        ///     538 definitions in the cache repeat an opcode - 224 of them repeat opcode 95 alone.
        ///     The decoder keeps only the last value, as the client does, so the earlier
        ///     occurrences exist nowhere but in the bytes they were read from and can only be
        ///     reproduced by replaying those bytes verbatim.
        /// </remarks>
        [Fact]
        public void RepeatedOpcodes_KeepBothTheirPositionsAndTheirValues()
        {
            AssertReEncodesToTheSameBytes(new byte[] { 95, 0, 42, 95, 0, 99, 0 },
                def => Assert.Equal(99, def.level));
        }

        /// <summary>
        ///     An opcode the stream carried is written back even when its payload happens to equal
        ///     the field's default.
        /// </summary>
        /// <remarks>
        ///     The packer does store defaults - opcode 12 with a size of 1, opcode 97 with the
        ///     unscaled 128 - so an encoder that emitted only fields which had moved off their
        ///     default would shorten a definition the user merely opened, changing its bytes and
        ///     its CRC.
        /// </remarks>
        [Fact]
        public void OpcodesWhosePayloadEqualsTheDefault_AreStillWrittenBack()
        {
            //12 with size 1 and 97 with scaleXY 128: both are exactly the client-side defaults.
            AssertReEncodesToTheSameBytes(new byte[] { 12, 1, 97, 0, 128, 0 }, def =>
            {
                Assert.Equal(1, def.size);
                Assert.Equal(128, def.scaleXY);
            });
        }

        /// <summary>
        ///     An option stored at opcode 150-154 is written back there and not also at 30-34.
        /// </summary>
        /// <remarks>
        ///     The two ranges are two spellings of the same five option slots and this codec reads
        ///     both into one array, so an encoder driven by that array alone emitted every option
        ///     twice - once at 30+slot and again at 150+slot - which on its own put every
        ///     definition in the cache out of byte-identity.
        /// </remarks>
        [Fact]
        public void OptionsStoredAtTheHighOpcodeRange_AreNotAlsoWrittenAtTheLowRange()
        {
            //151: option slot 1, holding the null-terminated string "Talk".
            byte[] stream = { 151, (byte)'T', (byte)'a', (byte)'l', (byte)'k', 0, 0 };

            AssertReEncodesToTheSameBytes(stream, def => Assert.Equal("Talk", def.options[1]));
        }

        /// <summary>
        ///     A definition carrying the same option slot at both of its opcodes keeps both.
        /// </summary>
        /// <remarks>
        ///     Only the occurrence the decoder read last still has its value in the field, so the
        ///     other can be reproduced only from the bytes it was read from. Dropping it would
        ///     shorten the definition.
        /// </remarks>
        [Fact]
        public void AnOptionSlotStoredAtBothOpcodes_SurvivesAReEncode()
        {
            byte[] stream =
            {
                30, (byte)'A', (byte)'t', (byte)'t', 0,
                150, (byte)'U', (byte)'s', (byte)'e', 0,
                0
            };

            AssertReEncodesToTheSameBytes(stream, def => Assert.Equal("Use", def.options[0]));
        }

        /// <summary>
        ///     Opcode 249 parameters keep the order the file listed them in, duplicate keys
        ///     included.
        /// </summary>
        /// <remarks>
        ///     Held in a sorted map - the natural shape, and what this codec used - the
        ///     parameters come back out in ascending key order, which reorders 16 definitions in
        ///     the cache, and a repeated key collapses into one entry, which shortens NPC 13592
        ///     by the eight bytes of the parameter it swallowed. These seventeen were the last
        ///     definitions in the index not to re-encode to their stored bytes.
        /// </remarks>
        [Fact]
        public void Opcode249Parameters_KeepTheirFileOrderAndTheirDuplicates()
        {
            byte[] stream =
            {
                249, 3,
                0, 0, 0, 5, 0, 0, 0, 7,     // int parameter, key 5, value 7
                0, 0, 0, 1, 0, 0, 0, 9,     // int parameter, key 1 - out of ascending order
                0, 0, 0, 5, 0, 0, 0, 11,    // int parameter, key 5 again - a duplicate key
                0
            };

            AssertReEncodesToTheSameBytes(stream, def => Assert.True(def.decoded[249]));
        }

        /// <summary>
        ///     A definition built from nothing rather than decoded still encodes, and encodes only
        ///     what has actually been set on it.
        /// </summary>
        /// <remarks>
        ///     Replaying a recorded opcode order is no use to a definition that has no recorded
        ///     order, so the encoder has to fall back to the field values for one. Without that
        ///     fallback an NPC created in the editor would save as an empty record.
        /// </remarks>
        [Fact]
        public void ANewlyCreatedNpc_EncodesTheFieldsThatWereSet()
        {
            var created = new NPCDefinition { name = "Test dummy", size = 3, level = 42 };

            byte[] encoded = created.Encode().ToArray();
            var reread = new NPCDefinition(new JagStream(encoded));

            Assert.Equal("Test dummy", reread.name);
            Assert.Equal(3, reread.size);
            Assert.Equal(42, reread.level);

            //Untouched fields must not have been invented into the stream on the way out.
            Assert.False(reread.decoded[103]);
            Assert.False(reread.decoded[134]);
            Assert.Equal(encoded, reread.Encode().ToArray());
        }

        /// <summary>
        ///     An edit to a definition that never carried the matching opcode still reaches the
        ///     encoded stream, appended after the opcodes the definition did carry.
        /// </summary>
        [Fact]
        public void AFieldSetOnADefinitionThatLackedItsOpcode_IsAppended()
        {
            var definition = new NPCDefinition(new JagStream(new byte[] { 12, 3, 0 }));
            Assert.False(definition.decoded[95]);

            definition.level = 7;
            var reread = new NPCDefinition(new JagStream(definition.Encode().ToArray()));

            Assert.Equal(3, reread.size);
            Assert.Equal(7, reread.level);
        }

        /// <summary>
        ///     A clone must not share the opcode bookkeeping with the definition it was taken from.
        /// </summary>
        /// <remarks>
        ///     The editor clones a definition to remember its pre-edit state. With the hit map,
        ///     the recorded stream and the options array shared by reference, editing the original
        ///     writes straight through into the snapshot and the two agree about everything the
        ///     snapshot exists to disagree about.
        /// </remarks>
        [Fact]
        public void Clone_DoesNotShareTheOpcodeBookkeeping()
        {
            var original = new NPCDefinition(new JagStream(new byte[] { 12, 3, 0 }));
            NPCDefinition snapshot = original.Clone();

            original.options[0] = "Attack";
            original.level = 42;
            byte[] edited = original.Encode().ToArray();

            Assert.Null(snapshot.options[0]);
            Assert.Equal(new byte[] { 12, 3, 0 }, snapshot.Encode().ToArray());
            Assert.NotEqual(edited, snapshot.Encode().ToArray());
        }

        /// <summary>
        ///     Decodes a hand-built definition, checks it landed on the terminator rather than the
        ///     end of the buffer, and checks it re-encodes to the bytes it came from.
        /// </summary>
        /// <param name="stream">The definition bytes, terminator included.</param>
        /// <param name="check">Field assertions for the decoded definition.</param>
        private static void AssertReEncodesToTheSameBytes(byte[] stream, Action<NPCDefinition> check)
        {
            var reader = new JagStream(Pad(stream));
            var definition = new NPCDefinition(reader);

            Assert.Equal(stream.Length, reader.Position);
            check(definition);
            Assert.Equal(stream, definition.Encode().ToArray());
        }

        /// <summary>
        ///     A definition carrying none of the optional array opcodes must still encode.
        /// </summary>
        /// <remarks>
        ///     Regression test for an encoder that dereferenced seven optional arrays without a
        ///     null check. Opcode 42 is carried by no NPC in the rev-639 cache and opcode 1 by
        ///     only 12,146 of 13,359, so the unguarded encoder threw NullReferenceException for
        ///     every real definition, not merely for an unusual one.
        /// </remarks>
        [Fact]
        public void NPCDefinition_WithNoOptionalArrays_EncodesWithoutThrowing()
        {
            var empty = new JagStream();
            empty.WriteByte(0);
            empty.Flip();

            var definition = new NPCDefinition(new JagStream(empty.ToArray()));

            //Every optional array is absent, which is what used to make Encode throw.
            Assert.Null(definition.modelIds);
            Assert.Null(definition.recolorDstPalette);
            Assert.Null(definition.morphs);

            byte[] encoded = definition.Encode().ToArray();
            var reread = new NPCDefinition(new JagStream(encoded));

            //An absent opcode must stay absent rather than being invented by the encoder.
            Assert.Null(reread.modelIds);
            Assert.Null(reread.recolorDstPalette);
            Assert.Null(reread.morphs);
            Assert.Equal(encoded, reread.Encode().ToArray());
        }

        /// <summary>
        ///     An opcode with no known payload size must be reported, not skipped.
        /// </summary>
        /// <remarks>
        ///     Skipping it reads the next payload byte as an opcode, so the whole remainder of
        ///     the definition decodes into nonsense while appearing to succeed. The stream
        ///     carries no length prefix, so there is no way to recover - the only honest
        ///     outcome is to say where the parse stopped.
        /// </remarks>
        [Fact]
        public void NPCDefinition_UnknownOpcode_IsReportedRatherThanSilentlySkipped()
        {
            var stream = new JagStream();
            stream.WriteByte(12);
            stream.WriteByte(3);
            stream.WriteByte(200);   //no opcode 200 exists in any known revision of this format
            stream.WriteByte(1);
            stream.WriteByte(2);
            stream.WriteByte(0);
            stream.Flip();

            var thrown = Assert.Throws<InvalidOperationException>(
                () => new NPCDefinition(new JagStream(stream.ToArray())));
            Assert.Contains("200", thrown.Message);
        }

        /// <summary>
        ///     Confirms the exact-consumption check can actually fail, so a green sweep means
        ///     something.
        /// </summary>
        /// <remarks>
        ///     A trailing byte after the terminator leaves the stream short of its end, which is
        ///     precisely the shape a mis-sized payload produces. Without this the sweep could be
        ///     passing because the assertion is unreachable rather than because the codec is
        ///     right.
        /// </remarks>
        [Fact]
        public void ExactConsumption_DetectsAStreamThatIsNotFullyRead()
        {
            var stream = new JagStream();
            stream.WriteByte(12);
            stream.WriteByte(3);
            stream.WriteByte(0);     //terminator
            stream.WriteByte(99);    //slack the decoder must not reach
            stream.Flip();

            byte[] data = stream.ToArray();
            var reader = new JagStream(data);
            NPCDefinition unused = new NPCDefinition(reader);

            Assert.NotEqual(data.Length, reader.Position);
        }

        /// <summary>
        ///     Walks the NPC index, yielding every definition's raw bytes with its NPC id.
        /// </summary>
        /// <remarks>
        ///     Reads through the fixture rather than <see cref="RSCache.GetNPCDefinition"/>
        ///     because that path memoises every container it touches, which is fine for the one
        ///     definition an editor pane shows and ruinous across the whole index.
        /// </remarks>
        /// <returns>Each NPC id paired with the file bytes backing it.</returns>
        private IEnumerable<(int npcId, byte[] data)> Definitions()
        {
            RSReferenceTable table = _cache.Table(RSConstants.NPC_DEFINITIONS_INDEX);

            foreach (int archiveId in _cache.ArchivesToExamine(table))
            {
                byte[] stored = _cache.RawContainer(RSConstants.NPC_DEFINITIONS_INDEX, archiveId);
                if (stored == null)
                    continue;

                int[] fileIds = table.GetArchiveEntry(archiveId).GetValidFileIds();
                if (fileIds.Length == 0)
                    continue;

                RSContainer container =
                    _cache.TryDecodeContainer(RSConstants.NPC_DEFINITIONS_INDEX, archiveId, stored);
                if (container == null)
                    continue;

                RSArchive archive = RSArchive.Decode(container.GetStream(), fileIds);

                //NPC ids are packed 256 to an archive, the same mapping GetNPCDefinition uses.
                foreach (int fileId in fileIds)
                    yield return (archiveId * 256 + fileId, archive.GetFile(fileId).ToArray());
            }
        }

        /// <summary>Counts how many failing definitions carried each opcode.</summary>
        private static void Tally(SortedDictionary<int, int> counts, NPCDefinition def)
        {
            for (int op = 0; op < def.decoded.Length; op++)
            {
                if (!def.decoded[op])
                    continue;
                counts.TryGetValue(op, out int seen);
                counts[op] = seen + 1;
            }
        }

        private static string Histogram(SortedDictionary<int, int> counts)
        {
            return string.Join(", ", counts.Select(c => $"{c.Key}={c.Value}"));
        }

        private static string Opcodes(NPCDefinition def)
        {
            var seen = new List<int>();
            for (int op = 0; op < def.decoded.Length; op++)
                if (def.decoded[op])
                    seen.Add(op);
            return "[" + string.Join(" ", seen) + "]";
        }

        private static string ByteAt(byte[] bytes, int offset)
        {
            return offset < bytes.Length ? $"0x{bytes[offset]:X2}" : "end of buffer";
        }

        private static int FirstDifference(byte[] expected, byte[] actual)
        {
            int shared = Math.Min(expected.Length, actual.Length);
            for (int i = 0; i < shared; i++)
                if (expected[i] != actual[i])
                    return i;
            return shared;
        }

        private static byte[] Pad(byte[] data)
        {
            byte[] padded = new byte[data.Length + SentinelPadding];
            Array.Copy(data, padded, data.Length);
            for (int i = data.Length; i < padded.Length; i++)
                padded[i] = SentinelByte;
            return padded;
        }

        private static void AssertNoFailures(List<string> failures, string summary)
        {
            if (failures.Count == 0)
                return;

            string detail = string.Join(Environment.NewLine, failures.Take(MaxReportedFailures));
            if (failures.Count > MaxReportedFailures)
                detail += $"{Environment.NewLine}... and {failures.Count - MaxReportedFailures} more";

            Assert.Fail($"{failures.Count} {summary}:{Environment.NewLine}{detail}");
        }
    }
}
