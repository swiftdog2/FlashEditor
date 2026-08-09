using System;
using System.Collections.Generic;

namespace FlashEditor.Tests.Definitions.Sprites
{
    /// <summary>
    ///     One opcode as the client's reader accounts for it: which opcode it is, where its payload
    ///     starts, and how many bytes the client consumes for it.
    /// </summary>
    public readonly struct ClientOpcodeSpan
    {
        /// <summary>Index of the node in the file, which is also the child index.</summary>
        public int NodeIndex { get; }

        /// <summary>The node type, which selects the opcode table.</summary>
        public int NodeType { get; }

        /// <summary>The opcode byte.</summary>
        public int Opcode { get; }

        /// <summary>Offset of the first payload byte from the start of the file.</summary>
        public int Offset { get; }

        /// <summary>Payload bytes the client reads, which is legitimately zero for many opcodes.</summary>
        public int Width { get; }

        /// <summary>Binds one opcode's identity to the span the client reads for it.</summary>
        public ClientOpcodeSpan(int nodeIndex, int nodeType, int opcode, int offset, int width)
        {
            NodeIndex = nodeIndex;
            NodeType = nodeType;
            Opcode = opcode;
            Offset = offset;
            Width = width;
        }

        /// <summary>Names the opcode the way every failure line in this suite does.</summary>
        public override string ToString() => $"node {NodeIndex} (type {NodeType}) opcode {Opcode}";
    }

    /// <summary>One node's header and child run, as the client's reader accounts for them.</summary>
    public sealed class ClientNodeLayout
    {
        /// <summary>The per-node version byte the client reads and discards.</summary>
        public int Version { get; set; }

        /// <summary>The node type.</summary>
        public int Type { get; set; }

        /// <summary>The output-size byte, <c>anInt3860</c>.</summary>
        public int OutputSize { get; set; }

        /// <summary>The opcodes this node carried, in file order.</summary>
        public List<ClientOpcodeSpan> Opcodes { get; } = new List<ClientOpcodeSpan>();

        /// <summary>Child-index bytes, one per input the node type declares.</summary>
        public int ChildCount { get; set; }
    }

    /// <summary>Where every field of one graph file sits, according to the client alone.</summary>
    public sealed class ClientGraphLayout
    {
        /// <summary>The nodes, in file order.</summary>
        public List<ClientNodeLayout> Nodes { get; } = new List<ClientNodeLayout>();

        /// <summary>Whether the file carried the three output-node bytes.</summary>
        public bool HasOutputIndices { get; set; }

        /// <summary>Offset the client's own parse ends at, measured from the start of the file.</summary>
        public int BodyLength { get; set; }

        /// <summary>Every opcode in the file, flattened, in file order.</summary>
        public List<ClientOpcodeSpan> Opcodes { get; } = new List<ClientOpcodeSpan>();
    }

    /// <summary>
    ///     Reads an index 9 graph file's layout from the 637 client's own node classes, with no
    ///     reference to <c>Texture.Decode</c>.
    /// </summary>
    /// <remarks>
    ///     This exists to close a hole the byte-identity sweep cannot reach.
    ///     <c>TextureGraphRecord.Encode</c> replays the payload spans the decoder captured, so
    ///     concatenating wrongly-sized spans still reproduces the file byte for byte: if the decoder
    ///     believed an opcode consumed two bytes where it consumes three, the sweep is silent,
    ///     because the spans still tile the file exactly.
    ///     <para>
    ///     The exact-consumption sweep is the only thing that constrained those widths, and it
    ///     constrains them <em>in aggregate</em>: it asserts one equation per file - that every width
    ///     in it sums, with the structure, to the file's length. Any error vector in the null space
    ///     of that equation passes. This reader asserts one equality per <em>occurrence</em> instead,
    ///     which is the same claim made tens of thousands of times more sharply.
    ///     </para>
    ///     <para>
    ///     Every width below is transcribed from a <c>Node_Sub10_Sub*.method991</c> arm in the
    ///     bundled 637 client, and the node type to class mapping from the forty returns of
    ///     <c>PlayerAppearance.method3630</c> (<c>PlayerAppearance.java:386-503</c>). It is
    ///     deliberately a second implementation. Do not "simplify" it by calling the production
    ///     decoder, or it stops being evidence about anything.
    ///     </para>
    ///     <para>
    ///     What it does <b>not</b> catch: an error that keeps a payload's total width while splitting
    ///     it into the wrong fields - reading a four-byte curve marker as a three-byte and a one-byte
    ///     value, say. Nothing driven by widths can, because the width is right. That needs a value
    ///     comparison, and is stated here rather than left for someone to assume otherwise.
    ///     </para>
    /// </remarks>
    public static class ClientTextureGraphReader
    {
        /// <summary>
        ///     Payload width in bytes for every fixed-width opcode the client's node classes read.
        /// </summary>
        /// <remarks>
        ///     Indexed by node type, then by opcode. A <c>-1</c> marks an opcode whose width the file
        ///     itself decides, handled by <see cref="VariableWidth"/>. An opcode past the end of a
        ///     type's row, or on a type with no row, reads nothing: <c>Node_Sub10.method991</c> is an
        ///     empty method, so the client silently consumes zero bytes for an opcode a node does not
        ///     recognise. That is a real shape in this data rather than a defensive default - four
        ///     opcodes reach it on node type 12 alone.
        ///     <para>
        ///     The opcodes that write the node's monochrome flag rather than a config field are in
        ///     here at width 1 like any other, because the client reads them with the same
        ///     <c>readUnsignedByte</c>; where the production decoder handles them is its own business
        ///     and not something this table should know.
        ///     </para>
        /// </remarks>
        private static readonly int[][] Widths = BuildWidths();

        /// <summary>
        ///     Child-index bytes each node type declares, from the first argument of its
        ///     <c>super(inputCount, isMonochrome)</c> call.
        /// </summary>
        /// <remarks>
        ///     Read off the client rather than shared with <c>Texture.ChildCounts</c> on purpose. A
        ///     wrong entry here or there desynchronises every node after it, and two tables that
        ///     cannot disagree cannot catch that.
        /// </remarks>
        private static readonly int[] ChildCounts = {
        //  0  1  2  3  4  5  6  7  8  9 10 11 12 13 14 15 16 17 18 19
            0, 0, 0, 0, 0, 1, 1, 2, 1, 1, 1, 1, 0, 0, 0, 0, 0, 1, 0, 3,
        // 20 21 22 23 24 25 26 27 28 29 30 31 32 33 34 35 36 37 38 39
            1, 3, 1, 1, 1, 1, 1, 0, 0, 0, 1, 0, 1, 1, 0, 1, 0, 0, 0, 0,
        };

        /// <summary>
        ///     Octave count a type 34 node starts with, <c>Node_Sub10_Sub35.anInt5733 = 4</c>
        ///     (<c>Node_Sub10_Sub35.java:21</c>).
        /// </summary>
        /// <remarks>
        ///     Load-bearing rather than cosmetic: opcode 2's explicit amplitude run is
        ///     <c>anInt5733</c> shorts long, and <c>anInt5733</c> is opcode 1's value. A node that
        ///     carries opcode 2 without opcode 1 reads four shorts, so a default of zero here would
        ///     silently shorten the payload and desynchronise the rest of the file.
        /// </remarks>
        private const int DefaultOctaves = 4;

        /// <summary>Marks an opcode whose payload width the file's own bytes decide.</summary>
        private const int Variable = -1;

        /// <summary>
        ///     The width the client reads for one opcode, or <c>-1</c> when the file decides it.
        /// </summary>
        /// <param name="nodeType">The node type.</param>
        /// <param name="opcode">The opcode byte.</param>
        /// <returns>The fixed payload width, <see cref="Variable"/>, or zero for an opcode the node
        /// does not recognise.</returns>
        public static int DeclaredWidth(int nodeType, int opcode)
        {
            if (nodeType < 0 || nodeType >= Widths.Length || opcode < 0)
                return 0;

            int[] row = Widths[nodeType];
            return opcode < row.Length ? row[opcode] : 0;
        }

        /// <summary>
        ///     Walks a graph file and reports where the client would find every field in it.
        /// </summary>
        /// <remarks>
        ///     Reads bytes out of the array directly rather than through <c>JagStream</c>, so a
        ///     defect in the stream primitives cannot make both readings wrong in the same way.
        /// </remarks>
        /// <param name="bytes">The whole decompressed graph file.</param>
        /// <param name="widthOf">
        ///     How wide each fixed-width opcode is. Supplied only so a test can perturb one entry and
        ///     watch what notices; leave it null for the client's own table.
        /// </param>
        /// <returns>The layout, with <see cref="ClientGraphLayout.BodyLength"/> at the point the
        /// client stops reading.</returns>
        /// <exception cref="InvalidOperationException">The file ends inside a field.</exception>
        public static ClientGraphLayout Read(byte[] bytes, Func<int, int, int> widthOf = null)
        {
            if (bytes == null)
                throw new ArgumentNullException(nameof(bytes));
            widthOf = widthOf ?? DeclaredWidth;

            var layout = new ClientGraphLayout();
            int at = 0;
            int nodeCount = Byte(bytes, ref at);

            for (int n = 0; n < nodeCount; n++)
            {
                var node = new ClientNodeLayout
                {
                    Version = Byte(bytes, ref at),
                    Type = Byte(bytes, ref at),
                    OutputSize = Byte(bytes, ref at),
                };
                layout.Nodes.Add(node);

                int opcodeCount = Byte(bytes, ref at);

                //The octave count only matters to node type 34, but it is tracked per node rather
                //than per file because it is a field on the node the client constructs.
                int octaves = DefaultOctaves;

                for (int op = 0; op < opcodeCount; op++)
                {
                    int opcode = Byte(bytes, ref at);
                    int width = widthOf(node.Type, opcode);
                    if (width == Variable)
                        width = VariableWidth(bytes, at, node.Type, opcode, octaves);

                    if (node.Type == 34 && opcode == 1 && width > 0)
                        octaves = bytes[at];

                    var span = new ClientOpcodeSpan(n, node.Type, opcode, at, width);
                    node.Opcodes.Add(span);
                    layout.Opcodes.Add(span);

                    Advance(bytes, ref at, width);
                }

                node.ChildCount = node.Type >= 0 && node.Type < ChildCounts.Length
                    ? ChildCounts[node.Type]
                    : 0;
                Advance(bytes, ref at, node.ChildCount);
            }

            //Node_Sub46_Sub19.java:111-114 only writes the three output indices when the graph
            //declares at least one node, so an empty graph legitimately has none.
            if (nodeCount > 0)
            {
                Advance(bytes, ref at, 3);
                layout.HasOutputIndices = true;
            }

            layout.BodyLength = at;
            return layout;
        }

        /// <summary>
        ///     Width of the four opcodes that size themselves from a count they carry.
        /// </summary>
        /// <remarks>
        ///     These are the ones a fixed table cannot state, and they are also where a width error
        ///     is most plausible, because the length lives in a byte some distance from the field it
        ///     governs - type 34's amplitude run is sized by a sibling opcode entirely.
        /// </remarks>
        /// <param name="bytes">The whole file.</param>
        /// <param name="at">Offset of the first payload byte.</param>
        /// <param name="nodeType">The node type.</param>
        /// <param name="opcode">The opcode byte.</param>
        /// <param name="octaves">The type 34 octave count in force for this node.</param>
        /// <returns>The payload width in bytes.</returns>
        private static int VariableWidth(byte[] bytes, int at, int nodeType, int opcode, int octaves)
        {
            switch (nodeType)
            {
                //Node_Sub10_Sub9.method991: an interpolation mode, then a marker count, then that
                //many (position, value) pairs of unsigned shorts.
                case 8:
                    return 2 + At(bytes, at + 1) * 4;

                //Node_Sub10_Sub33.method991: a preset id, and only preset 0 carries an explicit
                //ramp of (position, r, g, b) - the other presets are built by method1100 from
                //tables in the client and read nothing.
                case 10:
                {
                    int preset = At(bytes, at);
                    return preset != 0 ? 1 : 2 + At(bytes, at + 1) * 5;
                }

                //Node_Sub10_Sub36.method991: a shape count, then per entry a shape id selecting one
                //of four fixed-size records. An unrecognised id reads nothing further, which is what
                //the client's fallthrough does rather than an error.
                case 29:
                {
                    int count = At(bytes, at);
                    int width = 1;
                    for (int i = 0; i < count; i++)
                    {
                        int shape = At(bytes, at + width);
                        width += 1 + ShapeRecordWidth(shape);
                    }
                    return width;
                }

                //Node_Sub10_Sub35.method991: a signed short, and a negative one means the
                //amplitudes are listed explicitly - anInt5733 of them, as shorts.
                case 34:
                {
                    int high = At(bytes, at);
                    int low = At(bytes, at + 1);
                    int value = (high << 8) | low;
                    if (value > 32767)
                        value -= 65536;
                    return value < 0 ? 2 + octaves * 2 : 2;
                }
            }

            throw new InvalidOperationException(
                $"node type {nodeType} opcode {opcode} is marked variable-width with no rule for it");
        }

        /// <summary>
        ///     Bytes one type 29 shape record occupies, by shape id.
        /// </summary>
        /// <remarks>
        ///     Every one is a fixed field list rather than a length-prefixed blob:
        ///     <c>Class255.method3192</c> reads four shorts, a medium and a byte; the client's own
        ///     <c>Node_Sub10_Sub14.method1046</c> reads eight shorts, a medium and a byte;
        ///     <c>Class258.method3203</c> and <c>Class300.method3533</c> both read four shorts, two
        ///     mediums and a byte. That is where 12, 20, 15 and 15 come from - none of them is a
        ///     round number and none should be guessed.
        /// </remarks>
        /// <param name="shapeId">The shape id byte.</param>
        /// <returns>The record width, or zero for an id the client has no reader for.</returns>
        private static int ShapeRecordWidth(int shapeId)
        {
            switch (shapeId)
            {
                case 0: return 4 * 2 + 3 + 1;
                case 1: return 8 * 2 + 3 + 1;
                case 2: return 4 * 2 + 3 + 3 + 1;
                case 3: return 4 * 2 + 3 + 3 + 1;
                default: return 0;
            }
        }

        /// <summary>Reads one byte and advances, failing loudly rather than off the end.</summary>
        private static int Byte(byte[] bytes, ref int at)
        {
            int value = At(bytes, at);
            at++;
            return value;
        }

        /// <summary>Reads one byte without advancing.</summary>
        private static int At(byte[] bytes, int offset)
        {
            if (offset < 0 || offset >= bytes.Length)
                throw new InvalidOperationException(
                    $"the graph file ends at {bytes.Length} bytes and offset {offset} was needed");
            return bytes[offset];
        }

        /// <summary>Skips a field, failing when the file cannot hold it.</summary>
        private static void Advance(byte[] bytes, ref int at, int width)
        {
            if (width < 0 || at + width > bytes.Length)
                throw new InvalidOperationException(
                    $"a {width}-byte field at {at} does not fit in {bytes.Length} bytes");
            at += width;
        }

        /// <summary>
        ///     The client's opcode widths, one row per node type.
        /// </summary>
        /// <remarks>
        ///     Written out as a method rather than as a field initialiser so each row can carry the
        ///     class it was transcribed from. The mapping from node type to class is
        ///     <c>PlayerAppearance.method3630</c>, whose forty returns run in node-type order from
        ///     <c>PlayerAppearance.java:386</c>.
        /// </remarks>
        /// <returns>Payload widths indexed by node type then opcode.</returns>
        private static int[][] BuildWidths()
        {
            var widths = new int[40][];
            for (int type = 0; type < widths.Length; type++)
                widths[type] = Array.Empty<int>();

            widths[0] = new[] { 1 };                                 //Sub13: (byte << 12) / 255
            widths[1] = new[] { 3 };                                 //Sub22: method1186, a medium
            //Sub18, Sub3, Sub8 and Sub16 declare no method991 at all, so types 2, 3, 13 and 24
            //read nothing for any opcode. Left as empty rows rather than rows of zeroes so that
            //"no arm" and "an arm that reads nothing" stay distinguishable in the source.
            widths[4] = new[] { 1, 1, 2, 2, 2, 2, 2, 2 };            //Sub38
            widths[5] = new[] { 1, 1, 1 };                           //Sub24, opcode 2 the mono flag
            widths[6] = new[] { 2, 2, 1 };                           //Sub15, opcode 2 the mono flag
            widths[7] = new[] { 1, 1 };                              //Sub7, opcode 1 the mono flag
            widths[8] = new[] { Variable };                          //Sub9: a curve, sized by a count
            widths[9] = new[] { 1, 1, 1 };                           //Sub11, opcode 2 the mono flag
            widths[10] = new[] { Variable };                         //Sub33: a gradient, preset-gated
            widths[11] = new[] { 2, 2, 2 };                          //Sub4
            widths[12] = new[] { 1, 1, 0, 1 };                       //Sub30: no arm for opcode 2
            widths[14] = new[] { 2 };                                //Sub17
            widths[15] = new[] { 1, 1, 2, 1, 1, 1, 1 };              //Sub26
            widths[16] = new[] { 1, 1, 2 };                          //Sub32
            widths[17] = new[] { 2, 1, 1 };                          //Sub6: readShort then two signed bytes
            widths[18] = new[] { 2 };                                //Sub5_Sub1, inheriting Sub5
            widths[19] = new[] { 2, 1 };                             //Sub2, opcode 1 the mono flag
            widths[20] = new[] { 1, 1 };                             //Sub29
            widths[21] = new[] { 1 };                                //Sub12, opcode 0 the mono flag
            widths[22] = new[] { 1 };                                //Sub39, opcode 0 the mono flag
            widths[23] = new[] { 1 };                                //Sub27, opcode 0 the mono flag
            widths[25] = new[] { 2, 2, 2, 2, 3 };                    //Sub14, opcode 4 a medium
            widths[26] = new[] { 2, 2 };                             //Sub31
            widths[27] = new[] { 1, 2, 1 };                          //Sub23
            widths[28] = new[] { 1, 2, 2, 2, 2, 2, 1, 2, 2 };        //Sub28
            widths[29] = new[] { Variable, 1 };                      //Sub36: a shape list; 1 is mono
            widths[30] = new[] { 2, 2, 1 };                          //Sub10, opcode 2 the mono flag
            widths[31] = new[] { 2, 2, 2, 2 };                       //Sub34
            widths[32] = new[] { 2, 2, 2 };                          //Sub37
            widths[33] = new[] { 2, 1 };                             //Sub20
            widths[34] = new[] { 1, 1, Variable, 1, 1, 1, 1 };       //Sub35: opcode 2 sized by opcode 1
            widths[35] = new[] { 2 };                                //Sub1
            widths[36] = new[] { 2 };                                //Sub25
            widths[37] = new[] { 2, 2, 2, 2, 2, 2, 2 };              //Sub21
            widths[38] = new[] { 1, 2, 1, 2, 2 };                    //Sub19
            widths[39] = new[] { 2 };                                //Sub5

            return widths;
        }
    }
}
