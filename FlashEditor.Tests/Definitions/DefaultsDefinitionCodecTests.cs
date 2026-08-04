using System;
using FlashEditor.Definitions.Defaults;
using Xunit;

namespace FlashEditor.Tests.Definitions
{
    /// <summary>
    ///     The two index-28 records against the bytes a real revision-639 cache stores for them.
    /// </summary>
    /// <remarks>
    ///     Round-tripping this encoder against this decoder proves nothing, so both records are
    ///     committed here as literals and the field values are asserted against what the client does
    ///     with them. Both are short enough to read, and both supported caches store exactly these
    ///     bytes - a failure of the companion cache test means a cache this project has not seen,
    ///     not a codec regression.
    /// </remarks>
    public sealed class DefaultsDefinitionCodecTests
    {
        /// <summary>Group 1 exactly as the cache stores it.</summary>
        /// <remarks>
        ///     Six unsigned shorts under opcode 1, then opcode 4 with a count of one and the id
        ///     1093. Opcode 5 is absent, and its absence is what keeps the client's
        ///     <c>Class35.anIntArray333 != null</c> branch (Player.java:479) off.
        /// </remarks>
        public static readonly byte[] SceneDefaultsBytes =
        {
            0x01, 0x02, 0xAA, 0x02, 0xAB, 0x02, 0xAC, 0x02, 0xAD, 0x02, 0xAE, 0x02, 0xAF,
            0x04, 0x01, 0x04, 0x45,
            0x00
        };

        /// <summary>Group 3 exactly as the cache stores it.</summary>
        /// <remarks>
        ///     Opcode 3 leads with a slot count of 6, then opcode 1 carries six pairs of signed
        ///     shorts, then opcode 2 the benchmark model id. The order is the record's whole
        ///     difficulty: opcode 3 sizes the arrays opcode 1 fills.
        /// </remarks>
        public static readonly byte[] HitsplatLayoutBytes =
        {
            0x03, 0x06,
            0x01, 0x00, 0x00, 0xFF, 0xEC, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x14,
                  0x00, 0x00, 0x00, 0x28, 0x00, 0x00, 0x00, 0x3C, 0x00, 0x00, 0x00, 0x50,
            0x02, 0xB7, 0x98,
            0x00
        };

        /// <summary>Group 1 decodes to the cube map and title table the client reads.</summary>
        [Fact]
        public void SceneDefaultsDecodesToTheCubeMapAndTitleTable()
        {
            var stream = new JagStream(SceneDefaultsBytes);
            var definition = new SceneDefaultsDefinition().Decode(stream);

            Assert.Equal(SceneDefaultsBytes.Length, stream.Position);
            Assert.Equal(new[] { 682, 683, 684, 685, 686, 687 }, definition.CubeMapTextureIds);
            Assert.Equal(new[] { 1093 }, definition.MaleTitleEnumIds);

            //Absent, not empty. An empty array here would flip a branch in the client.
            Assert.Null(definition.FemaleTitleEnumIds);
        }

        /// <summary>Group 1 re-encodes to the bytes it was decoded from.</summary>
        [Fact]
        public void SceneDefaultsReEncodesToTheCapturedBytes()
        {
            var definition = new SceneDefaultsDefinition().Decode(new JagStream(SceneDefaultsBytes));

            Assert.Equal(SceneDefaultsBytes, definition.Encode().ToArray());
        }

        /// <summary>
        ///     Group 3 decodes to six slots of signed offsets and a model id.
        /// </summary>
        /// <remarks>
        ///     The vertical offsets run -20, 0, 20, 40, 60, 80. The first is the value that settles
        ///     the signedness: read unsigned it is 65516, and the record still round-trips
        ///     byte-identically, so only this assertion and the client say which is right.
        /// </remarks>
        [Fact]
        public void HitsplatLayoutDecodesToSignedOffsets()
        {
            var stream = new JagStream(HitsplatLayoutBytes);
            var definition = new HitsplatLayoutDefinition().Decode(stream);

            Assert.Equal(HitsplatLayoutBytes.Length, stream.Position);
            Assert.Equal(6, definition.SlotCount);
            Assert.True(definition.StoresSlotCount);
            Assert.Equal(new short[] { 0, 0, 0, 0, 0, 0 }, definition.OffsetX);
            Assert.Equal(new short[] { -20, 0, 20, 40, 60, 80 }, definition.OffsetY);
            Assert.Equal(47000, definition.BenchmarkModelId);
        }

        /// <summary>Group 3 re-encodes to the bytes it was decoded from, opcode order included.</summary>
        [Fact]
        public void HitsplatLayoutReEncodesToTheCapturedBytes()
        {
            var definition = new HitsplatLayoutDefinition().Decode(new JagStream(HitsplatLayoutBytes));

            Assert.Equal(HitsplatLayoutBytes, definition.Encode().ToArray());
        }

        /// <summary>
        ///     A slot count that has to be added is written before the offsets it sizes.
        /// </summary>
        /// <remarks>
        ///     This is the case no shipped record exercises and the one the client cannot survive:
        ///     opcode 3 allocates the arrays (Class155.java:46-48) and opcode 1 fills whatever
        ///     length it finds (:38), so a count appended after the offsets makes the client read
        ///     the old number of pairs and then throw them away by reallocating.
        /// </remarks>
        [Fact]
        public void AnAddedSlotCountIsWrittenBeforeTheOffsets()
        {
            //Opcode 1 alone, with the four pairs the client assumes in the absence of opcode 3.
            byte[] withoutCount =
            {
                0x01, 0x00, 0x01, 0x00, 0x02, 0x00, 0x03, 0x00, 0x04,
                      0x00, 0x05, 0x00, 0x06, 0x00, 0x07, 0x00, 0x08,
                0x00
            };

            var definition = new HitsplatLayoutDefinition().Decode(new JagStream(withoutCount));

            Assert.False(definition.StoresSlotCount);
            Assert.Equal(HitsplatLayoutDefinition.DefaultSlotCount, definition.SlotCount);
            Assert.Equal(withoutCount, definition.Encode().ToArray());

            //Six slots now, so the count has to be stated - and stated first.
            definition.SlotCount = 6;
            definition.OffsetX = new short[] { 1, 2, 3, 4, 5, 6 };
            definition.OffsetY = new short[] { -1, -2, -3, -4, -5, -6 };

            byte[] encoded = definition.Encode().ToArray();
            Assert.Equal(3, encoded[0]);
            Assert.Equal(6, encoded[1]);
            Assert.Equal(1, encoded[2]);

            //And the client's own reading of that stream has to give the six pairs back.
            var reread = new HitsplatLayoutDefinition().Decode(new JagStream(encoded));
            Assert.Equal(6, reread.SlotCount);
            Assert.Equal(definition.OffsetX, reread.OffsetX);
            Assert.Equal(definition.OffsetY, reread.OffsetY);
        }

        /// <summary>
        ///     The "no id" sentinel on the title tables survives a round trip as 0xFFFF.
        /// </summary>
        /// <remarks>
        ///     Nothing in either supported cache stores it, so the byte-identity sweep cannot defend
        ///     this branch and a bug here would surface only on a cache that uses it.
        /// </remarks>
        [Fact]
        public void TheAbsentIdSentinelRoundTrips()
        {
            byte[] withSentinel = { 0x04, 0x02, 0xFF, 0xFF, 0x04, 0x45, 0x00 };

            var definition = new SceneDefaultsDefinition().Decode(new JagStream(withSentinel));

            Assert.Equal(new[] { -1, 1093 }, definition.MaleTitleEnumIds);
            Assert.Equal(withSentinel, definition.Encode().ToArray());
        }

        /// <summary>An opcode neither decoder handles is refused rather than silently desynchronising.</summary>
        /// <remarks>
        ///     The client's loops have no default arm: an unrecognised opcode consumes nothing and
        ///     the next payload byte is read as an opcode, so every field after it is garbage while
        ///     the decode still appears to succeed.
        /// </remarks>
        [Fact]
        public void UnknownOpcodesAreRefused()
        {
            Assert.Throws<InvalidOperationException>(() =>
                new SceneDefaultsDefinition().Decode(new JagStream(new byte[] { 2, 0, 0, 0 })));
            Assert.Throws<InvalidOperationException>(() =>
                new HitsplatLayoutDefinition().Decode(new JagStream(new byte[] { 4, 0, 0, 0 })));
        }
    }
}
