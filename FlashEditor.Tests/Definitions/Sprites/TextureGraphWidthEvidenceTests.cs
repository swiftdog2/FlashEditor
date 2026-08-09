using System.Collections.Generic;
using System.Linq;
using FlashEditor;
using FlashEditor.Definitions.Sprites;
using Xunit;
using FlashEditor.IO;

namespace FlashEditor.Tests.Definitions.Sprites
{
    /// <summary>
    ///     States, by construction rather than by argument, what each layer of index 9's evidence can
    ///     and cannot detect about an opcode payload width.
    /// </summary>
    /// <remarks>
    ///     Index 9's byte-identity sweep is weaker than its name suggests, and this is where that is
    ///     written down in a form that fails if it stops being true.
    ///     <c>TextureGraphRecord.Encode</c> replays the payload spans the decoder captured, so
    ///     wrongly-sized spans concatenate back into the original file exactly as well as correct
    ///     ones: the sweep proves the structural re-derivation - node count, per-node opcode count,
    ///     child run width, output-index presence, trailer width - and is blind to a mis-sized
    ///     payload.
    ///     <para>
    ///     The exact-consumption sweep was then the only thing constraining the widths. It asserts
    ///     one equation per file: that every width in it, plus the structure, sums to the file's
    ///     length. That is a sum, not a per-field statement, and the test below builds a file two
    ///     different width tables both consume exactly - which is the whole claim, made concrete.
    ///     </para>
    ///     <para>
    ///     No cache is needed for any of this, so it runs on every build rather than only where a
    ///     639 cache is on disk.
    ///     </para>
    /// </remarks>
    public sealed class TextureGraphWidthEvidenceTests
    {
        /// <summary>Node type 4, whose opcode 0 reads one byte and opcode 2 reads two.</summary>
        /// <remarks>
        ///     <c>Node_Sub10_Sub38.method991</c>. Chosen because it carries both widths in one node,
        ///     which is what a compensating pair needs, and because it declares no child inputs, so
        ///     nothing but the two payloads can absorb the difference.
        /// </remarks>
        private const int BrickNode = 4;

        /// <summary>
        ///     A graph file whose two opcode payloads a wrong width table mis-slices while consuming
        ///     exactly the same number of bytes.
        /// </summary>
        /// <remarks>
        ///     One node of type 4 carrying opcode 0 then opcode 2, so the truthful reading is a
        ///     one-byte payload followed by a two-byte one. The second payload's first byte is
        ///     deliberately <c>2</c>, which is what lets the swapped reading stay aligned: it reads
        ///     opcode 0 as two bytes, lands on that <c>2</c> and reads it as an opcode, then reads
        ///     one byte for it and arrives at exactly the same place.
        /// </remarks>
        /// <returns>The file bytes, trailer included, ready for <c>Texture.Decode</c>.</returns>
        private static byte[] CompensatingPairFile()
        {
            var file = new List<byte>
            {
                1,           //one node
                0,           //version byte, read and discarded
                BrickNode,   //node type
                0,           //output-size byte
                2,           //two opcodes
                0,           //opcode 0, one payload byte by the client
                0x5B,        //  its payload
                2,           //opcode 2, two payload bytes by the client
                2,           //  high byte, and the opcode the swapped reading lands on
                0x37,        //  low byte
                0, 0, 0,     //colour, alpha and brightness output indices
            };

            //The 639 trailer the 637 client never reads. Its width is what the production decoder
            //takes, so the file has to carry it or the decode stops short of the end.
            file.AddRange(Enumerable.Repeat((byte) 0, Texture.TrailerBytes));
            return file.ToArray();
        }

        /// <summary>
        ///     The production decoder reads the constructed file, consuming every byte of it.
        /// </summary>
        /// <remarks>
        ///     Establishes that the file below is a well-formed graph rather than a shape invented to
        ///     make a point. Everything the next two tests claim is about a file this decoder accepts
        ///     without complaint.
        /// </remarks>
        [Fact]
        public void TheConstructedGraph_IsOneTheProductionDecoderAccepts()
        {
            byte[] file = CompensatingPairFile();
            var stream = new JagStream(file);

            Texture texture = Texture.Decode(stream);

            Assert.Equal(file.Length, stream.Position);
            Assert.Equal(-1, texture.UnhandledOpcode);

            TextureNodeRecord node = Assert.Single(texture.Record.Nodes);
            Assert.Equal(BrickNode, node.Type);
            Assert.Equal(new[] { 0, 2 }, node.Opcodes.Select(o => o.Opcode));
            Assert.Equal(new[] { 1, 2 }, node.Opcodes.Select(o => o.Payload.Length));
        }

        /// <summary>
        ///     Exact consumption cannot tell the right widths from a compensating pair of wrong ones.
        /// </summary>
        /// <remarks>
        ///     The point of the whole file. Two width tables that disagree about both of this graph's
        ///     opcodes finish on the same byte, so the assertion "the decode consumed the file
        ///     exactly" is satisfied by both. It is one equation and there are two unknowns in it.
        ///     <para>
        ///     Read this as a statement about the <em>assertion</em>, not about the current decoder:
        ///     the production widths are the client's, pinned per occurrence by
        ///     <c>RealCacheTextureGraphTests.EveryOpcodePayload_IsTheWidthTheClientReads</c>. What
        ///     this fixes is the belief that consumption alone was already enough.
        ///     </para>
        /// </remarks>
        [Fact]
        public void ExactConsumption_IsSatisfiedByACompensatingPairOfWrongWidths()
        {
            byte[] file = CompensatingPairFile();

            ClientGraphLayout truthful = ClientTextureGraphReader.Read(file);
            ClientGraphLayout swapped = ClientTextureGraphReader.Read(file, SwappedWidths);

            //Both readings account for every byte up to the trailer, which is the only thing the
            //exact-consumption sweep asks of them.
            int body = file.Length - Texture.TrailerBytes;
            Assert.Equal(body, truthful.BodyLength);
            Assert.Equal(body, swapped.BodyLength);

            //And they disagree about both payloads, in width and in position.
            Assert.Equal(new[] { 1, 2 }, truthful.Opcodes.Select(o => o.Width));
            Assert.Equal(new[] { 2, 1 }, swapped.Opcodes.Select(o => o.Width));
            Assert.Equal(new[] { 6, 8 }, truthful.Opcodes.Select(o => o.Offset));
            Assert.Equal(new[] { 6, 9 }, swapped.Opcodes.Select(o => o.Offset));
        }

        /// <summary>
        ///     A per-occurrence width check separates the two readings that exact consumption cannot.
        /// </summary>
        /// <remarks>
        ///     The same comparison the real-cache sweep runs, reduced to the one file the previous
        ///     test showed consumption is blind to. If this ever passes without a disagreement, the
        ///     width sweep has stopped comparing anything.
        /// </remarks>
        [Fact]
        public void APerOccurrenceWidthCheck_SeparatesThemAtTheOpcodeThatIsWrong()
        {
            byte[] file = CompensatingPairFile();
            List<ClientOpcodeSpan> truthful = ClientTextureGraphReader.Read(file).Opcodes;
            List<ClientOpcodeSpan> swapped = ClientTextureGraphReader.Read(file, SwappedWidths).Opcodes;

            var disagreements = new List<string>();
            for (int i = 0; i < truthful.Count; i++)
                if (truthful[i].Width != swapped[i].Width)
                    disagreements.Add($"{truthful[i]}: {truthful[i].Width} against {swapped[i].Width}");

            Assert.Equal(2, disagreements.Count);
        }

        /// <summary>
        ///     Node type 4's opcodes 0 and 2 with their widths exchanged, and every other opcode left
        ///     alone.
        /// </summary>
        /// <remarks>
        ///     A deliberately wrong table, standing in for a decoder that had read one field a byte
        ///     wide and its neighbour a byte narrow. Kept to two entries so that what the constructed
        ///     file demonstrates is exactly the compensating pair and nothing else.
        /// </remarks>
        /// <param name="nodeType">The node type.</param>
        /// <param name="opcode">The opcode byte.</param>
        /// <returns>The wrong width, or the client's own for every opcode not swapped.</returns>
        private static int SwappedWidths(int nodeType, int opcode)
        {
            if (nodeType == BrickNode && opcode == 0)
                return 2;
            if (nodeType == BrickNode && opcode == 2)
                return 1;
            return ClientTextureGraphReader.DeclaredWidth(nodeType, opcode);
        }
    }
}
