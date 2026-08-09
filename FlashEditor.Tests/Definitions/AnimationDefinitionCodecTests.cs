using System;
using System.Collections.Generic;
using System.Linq;
using FlashEditor.Definitions.Animation;
using Xunit;
using FlashEditor.IO;

namespace FlashEditor.Tests.Definitions
{
    /// <summary>
    ///     The animation codec against bytes lifted from a real revision-639 cache.
    /// </summary>
    /// <remarks>
    ///     Every fixture below is byte-identical in both supported caches - indexes 20 and 21 are
    ///     among the ones the repack did not touch - so the ids they were read at address the same
    ///     bytes wherever the suite is pointed. Each was chosen for a shape a sweep cannot argue
    ///     about on its own: one of only two records in the cache carrying opcode 16, the records
    ///     that store a scalar opcode twice, and the smallest record reaching opcodes 12, 13, 19
    ///     and 20.
    /// </remarks>
    public sealed class AnimationDefinitionCodecTests
    {
        /// <summary>
        ///     Animation 5857 (group 45 file 97): opcodes 15, 16, 1, 2.
        /// </summary>
        /// <remarks>
        ///     One of exactly two records in the cache carrying opcode 16. The other is animation
        ///     6495 at 202 bytes; this one is the shorter and is pinned literally so a decoder that
        ///     dropped the opcode fails here rather than only in a full sweep.
        /// </remarks>
        public static readonly byte[] WithOpcode16 =
        {
            0x0F, 0x10, 0x01, 0x00, 0x10, 0x00, 0x07, 0x00, 0x07, 0x00, 0x07, 0x00,
            0x07, 0x00, 0x07, 0x00, 0x07, 0x00, 0x07, 0x00, 0x07, 0x00, 0x07, 0x00,
            0x07, 0x00, 0x07, 0x00, 0x07, 0x00, 0x07, 0x00, 0x07, 0x00, 0x07, 0x00,
            0x07, 0x00, 0x00, 0x00, 0x09, 0x00, 0x04, 0x00, 0x07, 0x00, 0x02, 0x00,
            0x0F, 0x00, 0x05, 0x00, 0x0D, 0x00, 0x01, 0x00, 0x08, 0x00, 0x06, 0x00,
            0x0A, 0x00, 0x03, 0x00, 0x0C, 0x00, 0x0E, 0x00, 0x0B, 0x05, 0xEC, 0x05,
            0xEC, 0x05, 0xEC, 0x05, 0xEC, 0x05, 0xEC, 0x05, 0xEC, 0x05, 0xEC, 0x05,
            0xEC, 0x05, 0xEC, 0x05, 0xEC, 0x05, 0xEC, 0x05, 0xEC, 0x05, 0xEC, 0x05,
            0xEC, 0x05, 0xEC, 0x05, 0xEC, 0x02, 0x00, 0x10, 0x00
        };

        /// <summary>
        ///     Animation 7317 (group 57 file 21): opcodes 5, 5, 1, 3.
        /// </summary>
        /// <remarks>
        ///     Stores priority twice, 6 then 8. The client keeps only the second, so the first exists
        ///     nowhere but in the recorded payload bytes - which is the whole reason the opcode
        ///     stream keeps them verbatim.
        /// </remarks>
        public static readonly byte[] WithRepeatedPriority =
        {
            0x05, 0x06, 0x05, 0x08, 0x01, 0x00, 0x09, 0x00, 0x04, 0x00, 0x04, 0x00,
            0x04, 0x00, 0x05, 0x00, 0x04, 0x00, 0x04, 0x00, 0x05, 0x00, 0x05, 0x00,
            0x05, 0x01, 0xD1, 0x01, 0xAF, 0x01, 0x4C, 0x02, 0x3C, 0x01, 0x0B, 0x02,
            0x6C, 0x02, 0x50, 0x00, 0xF3, 0x01, 0x09, 0x07, 0x3A, 0x07, 0x3A, 0x07,
            0x3A, 0x07, 0x3A, 0x07, 0x3A, 0x07, 0x3A, 0x07, 0x3A, 0x07, 0x3A, 0x07,
            0x3A, 0x03, 0x0A, 0x07, 0x09, 0x0A, 0x0C, 0x0E, 0x10, 0x12, 0x13, 0x15,
            0x17, 0x00
        };

        /// <summary>
        ///     Animation 1771 (group 13 file 107): opcodes 7, 6, 7, 1.
        /// </summary>
        /// <remarks>
        ///     The only record in the cache that repeats opcode 7, and it repeats it with another
        ///     opcode in between - so an encoder that merely deduplicated would also have to know
        ///     where to put the survivor. Both hand slots end up at the 65535 sentinel.
        /// </remarks>
        public static readonly byte[] WithRepeatedHandItem =
        {
            0x07, 0x11, 0x92, 0x06, 0xFF, 0xFF, 0x07, 0xFF, 0xFF, 0x01, 0x00, 0x08,
            0x00, 0x01, 0x00, 0x02, 0x00, 0x08, 0x00, 0x03, 0x00, 0x08, 0x00, 0x07,
            0x00, 0x07, 0x00, 0x08, 0x00, 0x01, 0x00, 0x04, 0x00, 0x05, 0x00, 0x06,
            0x00, 0x07, 0x00, 0x08, 0x00, 0x09, 0x00, 0x0A, 0x01, 0x0C, 0x01, 0x0C,
            0x01, 0x0C, 0x01, 0x0C, 0x01, 0x0C, 0x01, 0x0C, 0x01, 0x0C, 0x01, 0x0C,
            0x00
        };

        /// <summary>
        ///     Animation 3138 (group 24 file 66): opcodes 1, 13, 18, 19, 20.
        /// </summary>
        /// <remarks>
        ///     The shortest record reaching the sound block: a one-row sound table, the alternate
        ///     emitter flag, a per-frame volume and a per-frame pitch range. Opcodes 19 and 20 are
        ///     replayed rather than re-encoded, so this is the fixture that says the replay puts them
        ///     back.
        /// </remarks>
        public static readonly byte[] WithSounds =
        {
            0x01, 0x00, 0x05, 0x00, 0x06, 0x00, 0x06, 0x00, 0x06, 0x00, 0x06, 0x00,
            0x06, 0x00, 0x00, 0x00, 0x01, 0x00, 0x02, 0x00, 0x03, 0x00, 0x04, 0x03,
            0xDB, 0x03, 0xDB, 0x03, 0xDB, 0x03, 0xDB, 0x03, 0xDB, 0x0D, 0x00, 0x01,
            0x01, 0x0D, 0xDE, 0x20, 0x12, 0x13, 0x00, 0x64, 0x14, 0x00, 0x00, 0xE6,
            0x01, 0x0E, 0x00
        };

        /// <summary>
        ///     Animation 400 (group 3 file 16): opcodes 5, 1, 3.
        /// </summary>
        /// <remarks>
        ///     Carries the blend-label list, which is what the client's post-decode pass keys off
        ///     when it fills in the two interrupt fields. Also out of ascending order.
        /// </remarks>
        public static readonly byte[] WithBlendLabels =
        {
            0x05, 0x06, 0x01, 0x00, 0x06, 0x00, 0x04, 0x00, 0x04, 0x00, 0x06, 0x00,
            0x03, 0x00, 0x12, 0x00, 0x04, 0x01, 0x2F, 0x01, 0x30, 0x01, 0x31, 0x01,
            0x33, 0x01, 0x34, 0x01, 0x35, 0x00, 0xCF, 0x00, 0xCF, 0x00, 0xCF, 0x00,
            0xCF, 0x00, 0xCF, 0x00, 0xCF, 0x03, 0x0E, 0x09, 0x0B, 0x0D, 0x0F, 0x11,
            0x13, 0xA5, 0xA7, 0xA9, 0xAB, 0xAD, 0xAF, 0xB1, 0xB3, 0x00
        };

        /// <summary>
        ///     Animation 7 (group 0 file 7): opcodes 1, 12.
        /// </summary>
        /// <remarks>The smallest record carrying the secondary frame table.</remarks>
        public static readonly byte[] WithSecondaryFrames =
        {
            0x01, 0x00, 0x0C, 0x00, 0x06, 0x00, 0x06, 0x00, 0x06, 0x00, 0x06, 0x00,
            0x06, 0x00, 0x06, 0x00, 0x06, 0x00, 0x06, 0x00, 0x06, 0x00, 0x06, 0x00,
            0x05, 0x00, 0x06, 0x00, 0x3F, 0x00, 0x2D, 0x00, 0x23, 0x00, 0x29, 0x00,
            0x2A, 0x00, 0x38, 0x00, 0x05, 0x00, 0x35, 0x00, 0x01, 0x00, 0x0E, 0x00,
            0x07, 0x00, 0x39, 0x06, 0xB3, 0x06, 0xB3, 0x06, 0xB3, 0x06, 0xB3, 0x06,
            0xB3, 0x06, 0xB3, 0x06, 0xB3, 0x06, 0xB3, 0x06, 0xB3, 0x06, 0xB3, 0x06,
            0xB3, 0x06, 0xB3, 0x0C, 0x09, 0x00, 0x32, 0x00, 0x1A, 0x00, 0x16, 0x00,
            0x00, 0x00, 0x09, 0x00, 0x40, 0x00, 0x26, 0x00, 0x30, 0x00, 0x1E, 0x06,
            0xB3, 0x06, 0xB3, 0x06, 0xB3, 0x06, 0xB3, 0x06, 0xB3, 0x06, 0xB3, 0x06,
            0xB3, 0x06, 0xB3, 0x06, 0xB3, 0x00
        };

        /// <summary>Every captured record, with the animation id it was read from.</summary>
        public static IEnumerable<object[]> EveryFixture()
        {
            yield return new object[] { 7, WithSecondaryFrames };
            yield return new object[] { 400, WithBlendLabels };
            yield return new object[] { 1771, WithRepeatedHandItem };
            yield return new object[] { 3138, WithSounds };
            yield return new object[] { 5857, WithOpcode16 };
            yield return new object[] { 7317, WithRepeatedPriority };
        }

        /// <summary>Every captured record consumes exactly and re-encodes to the bytes it came from.</summary>
        /// <param name="id">The animation id, so a failure names it.</param>
        /// <param name="stored">The captured bytes.</param>
        [Theory]
        [MemberData(nameof(EveryFixture))]
        public void EveryCapturedRecordRoundTrips(int id, byte[] stored)
        {
            var stream = new JagStream(stored);
            var definition = new AnimationDefinition { Id = id }.Decode(stream);

            Assert.True(stored.Length == stream.Position,
                $"animation {id} consumed {stream.Position} of its {stored.Length} bytes");
            Assert.True(stored.AsSpan().SequenceEqual(definition.Encode().ToArray()),
                $"animation {id} did not re-encode to the bytes it was decoded from");
        }

        /// <summary>
        ///     The frame table decodes to the frame set and frame indexes the client would load.
        /// </summary>
        /// <remarks>
        ///     The packed id is index 0's only address - that index carries no name hashes - so
        ///     getting the two halves the right way round is what makes indexes 0 and 20 join at all.
        ///     Animation 3138 names frame set 987, frames 0 to 4.
        /// </remarks>
        [Fact]
        public void TheFrameTableNamesAFrameSetAndFramesWithinIt()
        {
            var definition = new AnimationDefinition { Id = 3138 }.Decode(new JagStream(WithSounds));

            Assert.Equal(5, definition.FrameCount);
            Assert.Equal(new[] { 6, 6, 6, 6, 6 }, definition.FrameDurations);
            Assert.Equal(30, definition.TotalDuration);

            for (int i = 0; i < definition.FrameCount; i++)
            {
                Assert.Equal(987, AnimationDefinition.FrameGroupOf(definition.FrameIds[i]));
                Assert.Equal(i, AnimationDefinition.FrameIndexOf(definition.FrameIds[i]));
                Assert.Equal(definition.FrameIds[i], AnimationDefinition.PackFrame(987, i));
            }
        }

        /// <summary>The secondary frame table packs the same way as the main one.</summary>
        [Fact]
        public void TheSecondaryFrameTableIsPackedLikeTheMainOne()
        {
            var definition = new AnimationDefinition { Id = 7 }.Decode(new JagStream(WithSecondaryFrames));

            Assert.Equal(12, definition.FrameCount);
            Assert.Equal(9, definition.SecondaryFrameIds.Length);
            foreach (int packed in definition.SecondaryFrameIds)
                Assert.Equal(1715, AnimationDefinition.FrameGroupOf(packed));
            Assert.Equal(new[] { 50, 26, 22, 0, 9, 64, 38, 48, 30 },
                definition.SecondaryFrameIds.Select(AnimationDefinition.FrameIndexOf).ToArray());
        }

        /// <summary>The sound block decodes to the values the client's emitters read.</summary>
        /// <remarks>
        ///     The first entry of a sound row is 24 bits wide and every later one 16, so a decoder
        ///     that read them all at the same width would still consume the right number of bytes on
        ///     a single-entry row and produce the wrong number.
        /// </remarks>
        [Fact]
        public void TheSoundBlockDecodesToItsFields()
        {
            var definition = new AnimationDefinition { Id = 3138 }.Decode(new JagStream(WithSounds));

            Assert.Single(definition.FrameSounds);
            Assert.Equal(new[] { 0x0DDE20 }, definition.FrameSounds[0]);
            Assert.True(definition.SoundsUseTheAlternateEmitter);
            Assert.Equal(new[] { 100 }, definition.FrameSoundVolumes);
            Assert.Equal(new[] { 230 }, definition.FrameSoundPitchMin);
            Assert.Equal(new[] { 270 }, definition.FrameSoundPitchMax);
        }

        /// <summary>
        ///     A repeated scalar opcode keeps both occurrences, with the later value in the field.
        /// </summary>
        /// <remarks>
        ///     Animation 7317 stores priority 6 then 8. Keeping only the winner gives a file two
        ///     bytes shorter; keeping both but re-encoding the first from the field gives a file of
        ///     the right length and the wrong contents. Only replaying the earlier occurrence's own
        ///     bytes produces neither.
        /// </remarks>
        [Fact]
        public void ARepeatedScalarOpcodeKeepsBothOccurrences()
        {
            var definition = new AnimationDefinition { Id = 7317 }.Decode(new JagStream(WithRepeatedPriority));

            Assert.Equal(new[] { 5, 5, 1, 3 },
                definition.Opcodes.Select(record => record.Opcode).ToArray());
            Assert.Equal(8, definition.Priority);
            Assert.Equal(WithRepeatedPriority, definition.Encode().ToArray());
        }

        /// <summary>An opcode repeated with another between them keeps its position.</summary>
        [Fact]
        public void ARepeatedOpcodeKeepsItsPositionInTheStream()
        {
            var definition = new AnimationDefinition { Id = 1771 }.Decode(new JagStream(WithRepeatedHandItem));

            Assert.Equal(new[] { 7, 6, 7, 1 },
                definition.Opcodes.Select(record => record.Opcode).ToArray());
            Assert.Equal(65535, definition.LeftHandItem);
            Assert.Equal(65535, definition.RightHandItem);
            Assert.Equal(WithRepeatedHandItem, definition.Encode().ToArray());
        }

        /// <summary>The blend-label list keeps its order and its stored count.</summary>
        [Fact]
        public void BlendLabelsKeepTheOrderTheFileListsThemIn()
        {
            var definition = new AnimationDefinition { Id = 400 }.Decode(new JagStream(WithBlendLabels));

            Assert.Equal(new[] { 9, 11, 13, 15, 17, 19, 165, 167, 169, 171, 173, 175, 177, 179 },
                definition.BlendLabels);
            Assert.Equal(6, definition.Priority);
        }

        /// <summary>
        ///     The two interrupt fields keep the -1 the record stored, and the derived value is
        ///     offered separately.
        /// </summary>
        /// <remarks>
        ///     <c>Class97.method938</c> rewrites both after every load, and <c>Class183.java:260</c>
        ///     runs it on every load, so a decoder that folded the derivation into the fields would
        ///     re-encode opcodes 9 and 10 into a record that never carried them. That failure grows
        ///     the file by four bytes and is invisible to any assertion made on the decoded values
        ///     alone.
        /// </remarks>
        [Fact]
        public void TheDerivedInterruptFieldsAreKeptOutOfTheStoredOnes()
        {
            var withLabels = new AnimationDefinition { Id = 400 }.Decode(new JagStream(WithBlendLabels));

            Assert.Equal(-1, withLabels.MovingInterrupt);
            Assert.Equal(-1, withLabels.StationaryInterrupt);
            Assert.Equal(AnimationDefinition.BlendedInterruptBehaviour, withLabels.EffectiveMovingInterrupt);
            Assert.Equal(AnimationDefinition.BlendedInterruptBehaviour, withLabels.EffectiveStationaryInterrupt);

            byte[] encoded = withLabels.Encode().ToArray();
            Assert.Equal(WithBlendLabels, encoded);
            Assert.False(withLabels.Opcodes.Has(9));
            Assert.False(withLabels.Opcodes.Has(10));

            //And a record with no labels derives 0 rather than 2, which is the other arm of the same
            //rule and the one a record with neither opcode 3 nor opcode 9 relies on.
            var withoutLabels = new AnimationDefinition { Id = 7 }.Decode(new JagStream(WithSecondaryFrames));
            Assert.Equal(0, withoutLabels.EffectiveMovingInterrupt);
            Assert.Equal(0, withoutLabels.EffectiveStationaryInterrupt);
        }

        /// <summary>
        ///     A record that stores the value the derivation would have produced keeps its opcode.
        /// </summary>
        /// <remarks>
        ///     SYNTHETIC. No record in either cache stores opcode 9 or 10 at the value its own
        ///     opcode-3 state would derive, so no sweep can tell an encoder that recomputed the field
        ///     from one that kept it. The distinction is real the moment anyone edits such a record,
        ///     and this is the only thing that states it.
        /// </remarks>
        [Fact]
        public void AnInterruptStoredAtItsDerivedValueIsNotDropped()
        {
            //Opcode 3 with one label, then opcodes 9 and 10 both stating the 2 the derivation gives.
            byte[] stored = { 0x03, 0x01, 0x07, 0x09, 0x02, 0x0A, 0x02, 0x00 };

            var definition = new AnimationDefinition { Id = -1 }.Decode(new JagStream(stored));

            Assert.Equal(2, definition.MovingInterrupt);
            Assert.Equal(2, definition.StationaryInterrupt);
            Assert.Equal(definition.EffectiveMovingInterrupt, definition.MovingInterrupt);
            Assert.Equal(stored, definition.Encode().ToArray());
        }

        /// <summary>Clearing a bare flag removes its opcode rather than leaving one to be replayed.</summary>
        /// <remarks>
        ///     A flag has no payload, so the recorded stream is the only statement of whether it is
        ///     set. If clearing it only changed a field the replay would put the opcode back: the row
        ///     would change, the save would report success, and the flag would still be set.
        /// </remarks>
        [Fact]
        public void ClearingABareFlagRemovesItsOpcode()
        {
            var definition = new AnimationDefinition { Id = 5857 }.Decode(new JagStream(WithOpcode16));

            Assert.True(definition.Tweens);
            Assert.True(definition.TweensAcrossCachedFrames);

            definition.TweensAcrossCachedFrames = false;
            byte[] encoded = definition.Encode().ToArray();

            var reread = new AnimationDefinition { Id = 5857 }.Decode(new JagStream(encoded));
            Assert.False(reread.TweensAcrossCachedFrames);
            Assert.True(reread.Tweens);
            Assert.Equal(WithOpcode16.Length - 1, encoded.Length);

            //And setting it again puts it back, so the property is not one way.
            reread.TweensAcrossCachedFrames = true;
            Assert.True(new AnimationDefinition { Id = 5857 }
                .Decode(new JagStream(reread.Encode().ToArray())).TweensAcrossCachedFrames);
        }

        /// <summary>An empty record keeps every default and stays a single terminator byte.</summary>
        [Fact]
        public void AnEmptyRecordKeepsItsDefaults()
        {
            var definition = new AnimationDefinition { Id = 0 }.Decode(new JagStream(new byte[] { 0 }));

            Assert.Equal(0, definition.FrameCount);
            Assert.Equal(-1, definition.FrameStep);
            Assert.Equal(AnimationDefinition.DefaultPriority, definition.Priority);
            Assert.Equal(AnimationDefinition.DefaultMaxLoops, definition.MaxLoops);
            Assert.Equal(AnimationDefinition.DefaultRetriggerBehaviour, definition.RetriggerBehaviour);
            Assert.Equal(-1, definition.LeftHandItem);
            Assert.Equal(-1, definition.RightHandItem);
            Assert.Null(definition.FrameSoundVolumes);
            Assert.Equal(new byte[] { 0 }, definition.Encode().ToArray());
        }

        /// <summary>
        ///     A frame table and a sound row stored empty are kept rather than dropped.
        /// </summary>
        /// <remarks>
        ///     SYNTHETIC. A count of zero decodes to the same empty arrays an absent opcode leaves
        ///     behind, so nothing but the recorded opcode tells them apart - and a sound row of zero
        ///     entries is what the client stores as a null array, which is not the same as the row
        ///     being missing.
        /// </remarks>
        [Fact]
        public void ZeroLengthTablesAreDistinctFromAbsentOnes()
        {
            byte[] stored = { 0x01, 0x00, 0x00, 0x0D, 0x00, 0x02, 0x00, 0x00, 0x00 };

            var definition = new AnimationDefinition { Id = -1 }.Decode(new JagStream(stored));

            Assert.Empty(definition.FrameIds);
            Assert.Equal(2, definition.FrameSounds.Length);
            Assert.Empty(definition.FrameSounds[0]);
            Assert.Empty(definition.FrameSounds[1]);
            Assert.Equal(stored, definition.Encode().ToArray());
        }

        /// <summary>An opcode the client does not handle is refused rather than desynchronising.</summary>
        /// <remarks>
        ///     4 and 17 are the two gaps in the 637 chain and occur nowhere in either 639 cache, so
        ///     there is no data veto to weigh. The client consumes nothing and carries on, which
        ///     reads the next payload byte as an opcode and corrupts everything after it.
        /// </remarks>
        [Fact]
        public void UnknownOpcodesAreRefused()
        {
            foreach (byte opcode in new byte[] { 4, 17, 21, 200 })
            {
                Assert.Throws<InvalidOperationException>(() =>
                    new AnimationDefinition { Id = 0 }.Decode(new JagStream(new byte[] { opcode, 0, 0, 0 })));
            }
        }

        /// <summary>Frame arrays a single count cannot describe are refused on the way out.</summary>
        [Fact]
        public void MismatchedFrameArraysAreRefused()
        {
            var definition = new AnimationDefinition { Id = 0 }.Decode(new JagStream(WithSounds));
            definition.FrameDurations = new[] { 1, 2 };

            Assert.Throws<InvalidOperationException>(() => definition.Encode());
        }
    }
}
