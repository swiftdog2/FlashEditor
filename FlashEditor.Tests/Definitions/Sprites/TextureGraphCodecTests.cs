using System;
using System.Linq;
using FlashEditor.Cache;
using FlashEditor.Definitions.Sprites;
using FlashEditor.Utils;
using Xunit;
using FlashEditor.IO;

namespace FlashEditor.Tests.Definitions.Sprites
{
    /// <summary>
    ///     Offline coverage of the index 9 encoder: the layout it writes, and the one edit it
    ///     supports.
    /// </summary>
    /// <remarks>
    ///     The cache sweep in <see cref="RealCacheTextureGraphTests"/> compares the encoder against
    ///     the bytes the cache holds, which is the stronger evidence but only covers the shapes the
    ///     shipped data happens to contain. These pin the shapes it does not, and every assertion
    ///     here is against a literal byte array rather than against a second pass through this
    ///     project's own codec - a round trip of an encoder against its own decoder agrees with
    ///     itself about the wrong answer just as readily as about the right one.
    /// </remarks>
    public class TextureGraphCodecTests
    {
        public TextureGraphCodecTests()
        {
            DebugUtil.LOG_LEVEL = DebugUtil.LOG_DETAIL.NONE;
        }

        /// <summary>
        ///     A graph whose type 12 node carries the two opcodes the client's own node class does
        ///     not handle.
        /// </summary>
        /// <remarks>
        ///     <c>Node_Sub10_Sub30.method991</c> has arms for opcodes 0, 1 and 3 only, so 2 and 4
        ///     fall through to the empty base method and read nothing. Consumption therefore
        ///     balances whether or not anything records that they were there, which is exactly why
        ///     an encoder driven by decoded node state drops them and shortens the file. Two graphs
        ///     in the shipped cache carry them.
        /// </remarks>
        private static byte[] GraphWithZeroWidthOpcodes() => new byte[]
        {
            0x02,                          // two nodes

            0x00, 0x00, 0x01, 0x01,        // node 0: version, type 0 (constant), output size, 1 opcode
            0x00, 0xFF,                    //   opcode 0, one payload byte

            0x00, 0x0C, 0x01, 0x03,        // node 1: version, type 12 (noise), output size, 3 opcodes
            0x00, 0x2A,                    //   opcode 0, one payload byte
            0x02,                          //   opcode 2 - reads nothing
            0x04,                          //   opcode 4 - reads nothing

            0x01, 0x01, 0x01,              // colour, alpha and brightness output indices

            //The ten byte trailer the 637 client never reads. Arbitrary here, and copied verbatim
            //by the codec, which is the whole point of carrying it.
            0x00, 0x00, 0x01, 0x01, 0x00, 0x00, 0x00, 0x22, 0x00, 0x00,
        };

        /// <summary>
        ///     A one node graph whose only node names a sprite, for the edit path.
        /// </summary>
        private static byte[] GraphWithASpriteNode() => new byte[]
        {
            0x01,                          // one node
            0x00, 0x27, 0x01, 0x01,        // node 0: version, type 39 (sprite source), output size, 1 opcode
            0x00, 0x04, 0xD2,              //   opcode 0, sprite id 1234 as an unsigned short
            0x00, 0x00, 0x00,              // output indices
            0x00, 0x00, 0x01, 0x01, 0x00, 0x00, 0x00, 0x22, 0x00, 0x00,
        };

        /// <summary>
        ///     The encoder writes the layout <c>Node_Sub46_Sub19</c> reads, opcodes that consume no
        ///     bytes included.
        /// </summary>
        [Fact]
        public void Encode_ReproducesTheStoredLayout_IncludingOpcodesThatReadNothing()
        {
            byte[] stored = GraphWithZeroWidthOpcodes();

            Texture texture = Texture.Decode(new JagStream(stored));

            //Stated separately from the byte comparison: a file that came back identical because
            //the encoder replayed a blob it never parsed would satisfy the comparison alone.
            Assert.Equal(2, texture.Record.Nodes.Count);
            Assert.Equal(new[] { 0, 2, 4 },
                texture.Record.Nodes[1].Opcodes.Select(o => o.Opcode).ToArray());
            Assert.Equal(new[] { 1, 0, 0 },
                texture.Record.Nodes[1].Opcodes.Select(o => o.Payload.Length).ToArray());

            Assert.Equal(stored, texture.Encode().ToArray());
        }

        /// <summary>
        ///     The per-node version byte and the output-size byte survive, though nothing reads
        ///     them.
        /// </summary>
        /// <remarks>
        ///     Both are discarded by the client's parse and by the evaluator, so nothing in the
        ///     rendering path would notice them being written back as zero - and every graph in the
        ///     index would change on its first save. The literals here are deliberately not the
        ///     values the shipped data uses.
        /// </remarks>
        [Fact]
        public void Encode_CarriesTheHeaderBytesNothingDecodes()
        {
            byte[] stored = GraphWithZeroWidthOpcodes();
            stored[1] = 0x07;   // node 0's version byte
            stored[3] = 0x05;   // node 0's output size byte

            Texture texture = Texture.Decode(new JagStream(stored));

            Assert.Equal(7, texture.Record.Nodes[0].Version);
            Assert.Equal(5, texture.Record.Nodes[0].OutputSize);
            Assert.Equal(stored, texture.Encode().ToArray());
        }

        /// <summary>The ten trailing bytes are copied rather than synthesised.</summary>
        /// <remarks>
        ///     They are 639-era data the 637 client stops short of, they are not constant across
        ///     the index, and no field meaning is established for any of them - so the only safe
        ///     thing an encoder can do is put back what it read.
        /// </remarks>
        [Fact]
        public void Encode_CopiesTheTrailerVerbatim()
        {
            byte[] stored = GraphWithZeroWidthOpcodes();
            byte[] trailer = { 0x01, 0x02, 0x03, 0x04, 0x05, 0xFB, 0xFC, 0x22, 0x05, 0x01 };
            Array.Copy(trailer, 0, stored, stored.Length - trailer.Length, trailer.Length);

            Texture texture = Texture.Decode(new JagStream(stored));

            Assert.Equal(trailer, texture.Record.Trailer);
            Assert.Equal(stored, texture.Encode().ToArray());
        }

        /// <summary>A file with fewer trailing bytes than the format carries is reported, not padded.</summary>
        /// <remarks>
        ///     The trailer width is a measurement over the whole index rather than a field anything
        ///     declares, so the decoder has to fail loudly when a file disagrees with it. Reading
        ///     "whatever is left" instead would make every truncated file look well formed and
        ///     would defeat the padded-buffer consumption sweep, which relies on the decoder
        ///     stopping at a width it decided in advance.
        /// </remarks>
        [Fact]
        public void Decode_FileShorterThanTheTrailer_Throws()
        {
            byte[] stored = GraphWithZeroWidthOpcodes();
            byte[] truncated = stored.AsSpan(0, stored.Length - 1).ToArray();

            Assert.ThrowsAny<Exception>(() => Texture.Decode(new JagStream(truncated)));
        }

        /// <summary>Replacing one opcode's payload changes those bytes and nothing else.</summary>
        [Fact]
        public void TryReplaceOpcodePayload_RewritesOnlyTheSpanItOwns()
        {
            byte[] stored = GraphWithASpriteNode();
            Texture texture = Texture.Decode(new JagStream(stored));
            Assert.Equal(1234, texture.Graph.Nodes[0].SpriteId);

            Assert.True(texture.Record.TryReplaceOpcodePayload(0, 0, new byte[] { 0x00, 0x2A }));

            byte[] edited = texture.Encode().ToArray();
            Assert.Equal(stored.Length, edited.Length);

            int[] differing = Enumerable.Range(0, stored.Length)
                .Where(i => stored[i] != edited[i])
                .ToArray();
            Assert.Equal(new[] { 6, 7 }, differing);
            Assert.Equal(42, Texture.Decode(new JagStream(edited)).Graph.Nodes[0].SpriteId);
        }

        /// <summary>An opcode a node carries twice cannot be edited by opcode alone.</summary>
        /// <remarks>
        ///     The decoder keeps the last write, so "the opcode holding this value" is not well
        ///     defined once it repeats - and an editor that rewrote the first occurrence would move
        ///     a value the graph never used and leave the one it did. Refusing is the only answer
        ///     that cannot silently write the wrong field. Opcode repetition is one of the five ways
        ///     this format is non-canonical and it does occur elsewhere in this cache, so it is a
        ///     shape to design for rather than to assume away.
        /// </remarks>
        [Fact]
        public void TryReplaceOpcodePayload_RefusesARepeatedOpcode()
        {
            byte[] stored =
            {
                0x01,
                0x00, 0x27, 0x01, 0x02,        // one type 39 node carrying opcode 0 twice
                0x00, 0x04, 0xD2,
                0x00, 0x00, 0x2A,
                0x00, 0x00, 0x00,
                0x00, 0x00, 0x01, 0x01, 0x00, 0x00, 0x00, 0x22, 0x00, 0x00,
            };

            Texture texture = Texture.Decode(new JagStream(stored));
            Assert.Equal(42, texture.Graph.Nodes[0].SpriteId);   // the last write is what survives

            Assert.False(texture.Record.TryReplaceOpcodePayload(0, 0, new byte[] { 0x00, 0x01 }));
            Assert.Equal(stored, texture.Encode().ToArray());
        }

        /// <summary>A payload of the wrong width is refused rather than written.</summary>
        /// <remarks>
        ///     Nothing in a graph file states how long an opcode's payload is, so a payload of the
        ///     wrong width does not corrupt one field - it shifts every byte after it, and the node
        ///     count read from the next byte is then whatever the shift landed on.
        /// </remarks>
        [Fact]
        public void TryReplaceOpcodePayload_RefusesAPayloadOfADifferentWidth()
        {
            Texture texture = Texture.Decode(new JagStream(GraphWithASpriteNode()));

            Assert.False(texture.Record.TryReplaceOpcodePayload(0, 0, new byte[] { 0x2A }));
            Assert.False(texture.Record.TryReplaceOpcodePayload(0, 1, new byte[] { 0x00, 0x2A }));
            Assert.False(texture.Record.TryReplaceOpcodePayload(9, 0, new byte[] { 0x00, 0x2A }));
        }
    }
}
