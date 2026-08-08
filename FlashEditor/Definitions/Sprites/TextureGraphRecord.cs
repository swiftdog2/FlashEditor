using System;
using System.Collections.Generic;

namespace FlashEditor.Definitions.Sprites
{
    /// <summary>
    /// One opcode exactly as a texture graph file stored it: the opcode byte, and the payload
    /// span the decoder consumed for it.
    /// </summary>
    /// <remarks>
    /// The payload is kept as raw bytes rather than as the decoded value because index 9 is not
    /// canonical in at least five ways, and every one of them is lossy the moment an opcode is
    /// reduced to the field it assigns:
    /// <list type="bullet">
    /// <item>Opcode <b>order</b> within a node is free - <c>Texture.Decode</c> is a counted loop,
    /// so any order decodes to the same node.</item>
    /// <item>Opcode <b>repetition</b> is expressible, and only the last write survives in the
    /// node's fields.</item>
    /// <item><b>Aliased</b> opcodes write the same field from two different encodings: type 15
    /// opcode 0 sets both cell frequencies where 5 and 6 set them apart, and type 34 opcode 3
    /// sets both scales where 5 and 6 do.</item>
    /// <item><b>Absent versus default</b> is unanswerable from the fields, because
    /// <c>Texture.InitNodeDefaults</c> seeds real values - a type 0 node holding 4096 may or may
    /// not have carried opcode 0.</item>
    /// <item>Some payloads are <b>never decoded at all</b>: a type 29 shape record is skipped by
    /// width, and type 12's opcodes 2 and 4 are recognised while reading nothing.</item>
    /// </list>
    /// Replaying the span sidesteps all five, and an editor changes a value by rewriting the one
    /// span it owns rather than by re-deriving every span in the file.
    /// </remarks>
    public sealed class TextureOpcodeRecord
    {
        /// <summary>The opcode byte, as stored.</summary>
        public int Opcode { get; }

        /// <summary>
        /// The bytes this opcode consumed, which may legitimately be empty.
        /// </summary>
        /// <remarks>
        /// Settable so an edit can replace one field without the encoder having to reproduce the
        /// whole node. Assigning null is rejected rather than treated as an empty payload, since
        /// the two are indistinguishable in the encoded file and only one of them is a mistake.
        /// </remarks>
        public byte[] Payload
        {
            get => _payload;
            set => _payload = value ?? throw new ArgumentNullException(nameof(value));
        }

        private byte[] _payload;

        /// <summary>Binds an opcode to the payload span it consumed.</summary>
        /// <param name="opcode">The opcode byte.</param>
        /// <param name="payload">The bytes it consumed, empty when it consumed none.</param>
        public TextureOpcodeRecord(int opcode, byte[] payload)
        {
            Opcode = opcode;
            _payload = payload ?? throw new ArgumentNullException(nameof(payload));
        }
    }

    /// <summary>
    /// One graph node as the file stored it - the four header bytes, the opcode records in
    /// stream order, and the child-index bytes.
    /// </summary>
    /// <remarks>
    /// Two of the header bytes have no home on <c>TextureNode</c> because the client discards
    /// them too (<c>Node_Sub46_Sub11.method1581</c> reads a version byte it never uses, and an
    /// output-size byte only the GL upload path reads). They are still part of the file, so an
    /// encoder that synthesised them would rewrite archives nobody edited.
    /// </remarks>
    public sealed class TextureNodeRecord
    {
        /// <summary>The per-node version byte the client reads and throws away.</summary>
        public int Version { get; set; }

        /// <summary>The node type, which selects the opcode table and the child count.</summary>
        public int Type { get; set; }

        /// <summary>The output-size byte (<c>anInt3860</c>), stored but not used by this decoder.</summary>
        public int OutputSize { get; set; }

        /// <summary>The opcodes this node carried, in the order the file listed them.</summary>
        public List<TextureOpcodeRecord> Opcodes { get; } = new List<TextureOpcodeRecord>();

        /// <summary>
        /// The child-index bytes, one per input the node type declares.
        /// </summary>
        /// <remarks>
        /// Kept as bytes rather than rebuilt from <c>TextureNode.ChildIndices</c> so that the
        /// count written back is the count that was read, rather than whatever the current child
        /// table says. A wrong entry in that table desynchronises every node after this one, and
        /// an encoder driven by it would turn a decode bug into a corrupt cache.
        /// </remarks>
        public byte[] ChildIndices { get; set; } = Array.Empty<byte>();
    }

    /// <summary>
    /// A whole texture graph file as it was stored, sufficient to write it back byte for byte.
    /// </summary>
    /// <remarks>
    /// Index 9 had no encoder at all, so a texture could be viewed and never saved. This is the
    /// half of the codec that makes a write path possible, and it is deliberately a replay of the
    /// stored spans rather than a serialiser driven by <c>TextureNode</c>: see
    /// <see cref="TextureOpcodeRecord"/> for the five ways the format is non-canonical, any one
    /// of which would rewrite untouched files on the first save.
    /// </remarks>
    public sealed class TextureGraphRecord
    {
        /// <summary>The nodes, in file order. The index into this list is the child index.</summary>
        public List<TextureNodeRecord> Nodes { get; } = new List<TextureNodeRecord>();

        /// <summary>
        /// Whether the file carried the three output-node bytes.
        /// </summary>
        /// <remarks>
        /// The client only reads them when the node count is non-zero
        /// (<c>Node_Sub46_Sub19.java:111-114</c>), so an empty graph has none and writing three
        /// zeroes back would lengthen the file by three bytes.
        /// </remarks>
        public bool HasOutputIndices { get; set; }

        /// <summary>Node index sampled for colour.</summary>
        public int ColourOutputIndex { get; set; }

        /// <summary>Node index sampled for alpha.</summary>
        public int AlphaOutputIndex { get; set; }

        /// <summary>Node index sampled for brightness.</summary>
        public int BrightnessOutputIndex { get; set; }

        /// <summary>
        /// The bytes past the output indices, copied verbatim.
        /// </summary>
        /// <remarks>
        /// The 637 client stops reading at the output indices, so these are 639-era bytes it was
        /// never built to see - the usual case of the cache running ahead of the client. They are
        /// not constant across the index and no field meaning is established for them, so they are
        /// carried rather than synthesised. Guessing at them would corrupt every graph in the
        /// index on the first save of any one of them.
        /// </remarks>
        public byte[] Trailer { get; set; } = Array.Empty<byte>();

        /// <summary>
        /// Where the client's own parse ends, measured from the start of the file.
        /// </summary>
        /// <remarks>
        /// This is the number the exact-consumption claim is about: the graph proper has to
        /// account for every byte up to here, and the trailer is what is left. Reported separately
        /// from the stream position because the decoder now consumes the trailer as well, so the
        /// position alone can no longer tell a correct parse from one that stopped early and let
        /// the trailer read absorb the difference.
        /// </remarks>
        public long BodyLength { get; set; }

        /// <summary>
        /// Writes the graph back out in the layout <c>Node_Sub46_Sub19</c> reads.
        /// </summary>
        /// <remarks>
        /// Every count written here is re-derived rather than replayed - the node count from the
        /// node list, each node's opcode count from its opcode records, the child bytes from their
        /// recorded run - so a decoder that lost an opcode or mis-sized a child run produces a
        /// file of the wrong length instead of a silently reordered one. That is what the
        /// byte-identity sweep over index 9 is able to detect; the payload spans themselves are
        /// pinned by the exact-consumption sweep, not by this.
        /// </remarks>
        /// <returns>The encoded file, positioned at the start.</returns>
        public JagStream Encode()
        {
            var stream = new JagStream();
            stream.WriteByte(Nodes.Count);

            foreach (TextureNodeRecord node in Nodes)
            {
                stream.WriteByte(node.Version);
                stream.WriteByte(node.Type);
                stream.WriteByte(node.OutputSize);
                stream.WriteByte(node.Opcodes.Count);

                foreach (TextureOpcodeRecord opcode in node.Opcodes)
                {
                    stream.WriteByte(opcode.Opcode);
                    if (opcode.Payload.Length > 0)
                        stream.Write(opcode.Payload);
                }

                if (node.ChildIndices.Length > 0)
                    stream.Write(node.ChildIndices);
            }

            if (HasOutputIndices)
            {
                stream.WriteByte(ColourOutputIndex);
                stream.WriteByte(AlphaOutputIndex);
                stream.WriteByte(BrightnessOutputIndex);
            }

            if (Trailer.Length > 0)
                stream.Write(Trailer);

            return stream.Flip();
        }

        /// <summary>
        /// Replaces the payload of a node's single occurrence of an opcode.
        /// </summary>
        /// <remarks>
        /// The single-occurrence requirement is the point rather than a limitation. An opcode a
        /// node carries twice decodes to whichever value came last, so "the opcode that holds this
        /// value" is not well defined, and an editor that rewrote the first occurrence would move
        /// a value the graph never used while leaving the one it did. Refusing is the only answer
        /// that cannot silently write the wrong field.
        /// </remarks>
        /// <param name="nodeIndex">Index into <see cref="Nodes"/>.</param>
        /// <param name="opcode">The opcode whose payload is being replaced.</param>
        /// <param name="payload">The replacement bytes, which must be the same width as a decode
        /// of this opcode would consume.</param>
        /// <returns>
        /// Whether the payload was replaced. False when the node does not carry the opcode, or
        /// carries it more than once.
        /// </returns>
        public bool TryReplaceOpcodePayload(int nodeIndex, int opcode, byte[] payload)
        {
            if (payload == null)
                throw new ArgumentNullException(nameof(payload));
            if (nodeIndex < 0 || nodeIndex >= Nodes.Count)
                return false;

            TextureOpcodeRecord found = null;
            foreach (TextureOpcodeRecord record in Nodes[nodeIndex].Opcodes)
            {
                if (record.Opcode != opcode)
                    continue;
                if (found != null)
                    return false;
                found = record;
            }

            if (found == null || found.Payload.Length != payload.Length)
                return false;

            found.Payload = (byte[]) payload.Clone();
            return true;
        }
    }
}
