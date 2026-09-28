using System;
using FlashEditor.LiveEdit;
using Xunit;

namespace FlashEditor.Tests.LiveEdit {
    /// <summary>
    ///     Pins leg A of <c>reference/live-edit-protocol.md</c> byte for byte. The expected arrays
    ///     are written out from the document's frame layout, not produced by the encoder, because
    ///     the server decodes these bytes with its own Java codec and a round trip through this
    ///     encoder and decoder alone would agree with itself whatever the layout was.
    /// </summary>
    public sealed class LiveEditFrameCodecTests {
        [Theory]
        [InlineData(LiveEditKind.Terrain, 0x00)]
        [InlineData(LiveEditKind.Locations, 0x01)]
        [InlineData(LiveEditKind.UnderwaterTerrain, 0x02)]
        [InlineData(LiveEditKind.UnderwaterLocations, 0x03)]
        public void EncodesAMapFrameToTheExactProtocolBytes(LiveEditKind kind, byte kindByte) {
            //Square 50,50 is (50 << 8) | 50 = 0x3232.
            byte[] frame = LiveEditFrameCodec.Encode(new LiveEditFrame(kind, 0x3232, new byte[] { 1, 2, 3 }));

            Assert.Equal(new byte[] {
                0x46, 0x4C, 0x01, kindByte, 0x32, 0x32, 0x00, 0x00, 0x00, 0x03, 1, 2, 3
            }, frame);
        }

        [Theory]
        [InlineData(LiveEditKind.FloorUnderlay, 0x04)]
        [InlineData(LiveEditKind.FloorOverlay, 0x05)]
        public void EncodesAFloorDefinitionFrameToTheExactProtocolBytes(LiveEditKind kind, byte kindByte) {
            byte[] frame = LiveEditFrameCodec.Encode(new LiveEditFrame(kind, 300, new byte[] { 0xAA, 0xBB }));

            Assert.Equal(new byte[] {
                0x46, 0x4C, 0x01, kindByte, 0x01, 0x2C, 0x00, 0x00, 0x00, 0x02, 0xAA, 0xBB
            }, frame);
        }

        [Fact]
        public void EncodesTheLengthAsFourBigEndianBytes() {
            byte[] frame = LiveEditFrameCodec.Encode(
                new LiveEditFrame(LiveEditKind.Terrain, 0xFFFF, new byte[0x012345]));

            Assert.Equal(new byte[] { 0xFF, 0xFF, 0x00, 0x01, 0x23, 0x45 }, frame.AsSpan(4, 6).ToArray());
            Assert.Equal(10 + 0x012345, frame.Length);
        }

        [Fact]
        public void DecodesWhatTheProtocolDocumentDescribes() {
            byte[] wire = { 0x46, 0x4C, 0x01, 0x05, 0x01, 0x2C, 0x00, 0x00, 0x00, 0x02, 0xAA, 0xBB };

            LiveEditFrame frame = LiveEditFrameCodec.Decode(wire);

            Assert.Equal(LiveEditKind.FloorOverlay, frame.Kind);
            Assert.Equal(300, frame.Id);
            Assert.Equal(new byte[] { 0xAA, 0xBB }, frame.Payload);
        }

        [Fact]
        public void DecodeRejectsABadMagic() {
            byte[] wire = { 0x46, 0x4D, 0x01, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00 };
            Assert.Throws<LiveEditProtocolException>(() => LiveEditFrameCodec.Decode(wire));
        }

        [Fact]
        public void DecodeRejectsAnUnknownVersion() {
            byte[] wire = { 0x46, 0x4C, 0x02, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00 };
            Assert.Throws<LiveEditProtocolException>(() => LiveEditFrameCodec.Decode(wire));
        }

        [Theory]
        [InlineData(6)]
        [InlineData(7)]
        [InlineData(255)]
        public void DecodeRejectsAKindLegADoesNotCarry(byte kind) {
            byte[] wire = { 0x46, 0x4C, 0x01, kind, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00 };
            Assert.Throws<LiveEditProtocolException>(() => LiveEditFrameCodec.Decode(wire));
        }

        [Fact]
        public void DecodeRejectsALengthOverTheLimitBeforeReadingThePayload() {
            //262145 = 0x00040001: one over the limit, with no payload behind it.
            //The server decides on the length alone, so the refusal has to name the limit rather
            //than fall through to the truncation check, which would also throw here.
            byte[] wire = { 0x46, 0x4C, 0x01, 0x00, 0x00, 0x00, 0x00, 0x04, 0x00, 0x01 };
            var refused = Assert.Throws<LiveEditProtocolException>(() => LiveEditFrameCodec.Decode(wire));
            Assert.Contains("limit", refused.Message);
        }

        [Fact]
        public void DecodeRejectsAFrameShorterThanItsDeclaredLength() {
            byte[] wire = { 0x46, 0x4C, 0x01, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x02, 0xAA };
            Assert.Throws<LiveEditProtocolException>(() => LiveEditFrameCodec.Decode(wire));
        }

        [Fact]
        public void EncodeAcceptsAPayloadAtExactlyTheLimit() {
            byte[] frame = LiveEditFrameCodec.Encode(new LiveEditFrame(LiveEditKind.Terrain, 0, new byte[262144]));
            Assert.Equal(10 + 262144, frame.Length);
        }

        [Fact]
        public void EncodeRefusesAPayloadOverTheLimit() {
            Assert.Throws<ArgumentException>(() =>
                LiveEditFrameCodec.Encode(new LiveEditFrame(LiveEditKind.Terrain, 0, new byte[262145])));
        }

        [Fact]
        public void EncodeRefusesTheRebuildKindWhichIsServerToClientOnly() {
            Assert.Throws<ArgumentException>(() =>
                LiveEditFrameCodec.Encode(new LiveEditFrame(LiveEditKind.Rebuild, 0, Array.Empty<byte>())));
        }

        [Theory]
        [InlineData(-1)]
        [InlineData(65536)]
        public void EncodeRefusesAnIdThatDoesNotFitSixteenBits(int id) {
            Assert.Throws<ArgumentException>(() =>
                LiveEditFrameCodec.Encode(new LiveEditFrame(LiveEditKind.Terrain, id, Array.Empty<byte>())));
        }
    }
}