using System;
using System.Collections.Generic;
using static FlashEditor.Utils.DebugUtil;

namespace FlashEditor.Definitions.Sprites
{
    /// <summary>
    /// Decodes a procedural texture graph from the TEXTURES index (index 9).
    /// Rev 639 stores textures as a DAG of operation nodes (Node_Sub46_Sub19).
    /// Builds a full <see cref="TextureGraph"/> for evaluation by
    /// <see cref="TextureGraphEvaluator"/>.
    /// </summary>
    internal class Texture
    {
        private int[] _spriteIds = Array.Empty<int>();
        private TextureGraph _graph;
        private TextureGraphRecord _record;

        /// <summary>
        /// Bytes every graph file carries after the three output-node indices.
        /// </summary>
        /// <remarks>
        /// The 637 client stops reading at the output indices, so this is 639-era data it was
        /// never built to see. The width is measured rather than declared anywhere: every graph
        /// in both supported caches leaves exactly this many bytes, which is what
        /// <c>TextureGraphConformanceTests.EveryTextureGraph_ConsumesItsFileExactlyBarTheTrailer</c>
        /// asserts over the whole index. Reading them here rather than leaving them in the stream
        /// is what lets the encoder put them back; a decoder that stopped short would have nothing
        /// to write and would shorten every file in the index on its first save.
        /// </remarks>
        internal const int TrailerBytes = 10;

        /// <summary>
        /// Node type of the first opcode this decoder had no case for, or -1 when every opcode
        /// in the graph was recognised. Purely diagnostic: an unhandled opcode consumes no bytes
        /// and does not stop the parse, exactly as in the client.
        /// </summary>
        public int UnhandledNodeType = -1;

        /// <summary>The opcode recorded alongside <see cref="UnhandledNodeType"/>.</summary>
        public int UnhandledOpcode = -1;

        /// <summary>Sprite file IDs referenced by this texture.</summary>
        public int[] FileIds => _spriteIds;

        /// <summary>Number of sprite references.</summary>
        public int Count => _spriteIds.Length;

        /// <summary>The parsed texture graph for evaluation.</summary>
        public TextureGraph Graph => _graph;

        /// <summary>
        /// The file exactly as it was stored, which is what an edit is applied to and what
        /// <see cref="Encode"/> writes back.
        /// </summary>
        /// <remarks>
        /// Kept alongside <see cref="Graph"/> rather than folded into it because the two answer
        /// different questions. The graph is what the evaluator renders and is deliberately lossy -
        /// the post-decode hooks overwrite decoded parameters with derived ones, so
        /// <c>InitFractalNoise</c> replaces the octave count opcode 1 wrote with the trimmed one.
        /// The record is what the cache holds.
        /// </remarks>
        public TextureGraphRecord Record => _record;

        /// <summary>
        /// Writes this graph back out as the bytes the cache stores for it.
        /// </summary>
        /// <returns>The encoded file, positioned at the start.</returns>
        public JagStream Encode() => _record.Encode();

        /// <summary>Returns the sprite file ID at the specified index.</summary>
        public int GetFileId(int index) => _spriteIds[index];

        // Node type → child input count, read off the first argument of each node class's
        // super(inputCount, isMonochrome) call in the client. The graph reads exactly this
        // many child-index bytes per node, so a wrong entry desyncs the rest of the file.
        private static readonly int[] ChildCounts = {
        //  0  1  2  3  4  5  6  7  8  9 10 11 12 13 14 15 16 17 18 19
            0, 0, 0, 0, 0, 1, 1, 2, 1, 1, 1, 1, 0, 0, 0, 0, 0, 1, 0, 3,
        // 20 21 22 23 24 25 26 27 28 29 30 31 32 33 34 35 36 37 38 39
            1, 3, 1, 1, 1, 1, 1, 0, 0, 0, 1, 0, 1, 1, 0, 1, 0, 0, 0, 0,
        };

        // Node type → default monochrome flag, the second argument of the same super() call.
        // Individual nodes override it from the data (see MonoOverrideOpcode), so this is only
        // the starting value.
        private static readonly bool[] MonoDefaults = {
        //  0      1      2     3     4     5      6      7      8     9
            true,  false, true, true, true, false, false, false, true, false,
        // 10     11     12    13    14    15    16    17     18     19
            false, false, true, true, true, true, true, false, false, false,
        // 20     21     22     23     24    25     26    27    28    29
            false, false, false, false, true, false, true, true, true, true,
        // 30     31    32    33     34    35    36     37    38    39
            false, true, true, false, true, true, false, true, true, false,
        };

        // Node type → the opcode that overwrites the node's monochrome flag, or -1 when the
        // node has none. The client stores these straight into aBoolean3861 rather than into a
        // config field, so a graph can flip a nominally-colour node to a single channel.
        private static readonly int[] MonoOverrideOpcode = {
        //  0   1   2   3   4   5   6   7   8   9
            -1, -1, -1, -1, -1,  2,  2,  1, -1,  2,
        // 10  11  12  13  14  15  16  17  18  19
            -1, -1, -1, -1, -1, -1, -1, -1, -1,  1,
        // 20  21  22  23  24  25  26  27  28  29
            -1,  0,  0,  0, -1, -1, -1, -1, -1,  1,
        // 30  31  32  33  34  35  36  37  38  39
             2, -1, -1, -1, -1, -1, -1, -1, -1, -1,
        };

        /// <summary>The monochrome flag a node of this type starts with.</summary>
        internal static bool DefaultIsMonochrome(int nodeType) =>
            nodeType >= 0 && nodeType < MonoDefaults.Length ? MonoDefaults[nodeType] : true;

        /// <summary>
        /// Applies the field initialisers the client's node classes declare, for the two cases
        /// where a later opcode's decode or the evaluator depends on the starting value rather
        /// than on zero.
        /// </summary>
        private static void InitNodeDefaults(TextureNode node, int nodeType)
        {
            switch (nodeType)
            {
                //Node_Sub10_Sub13.anInt5601 = 4096. A constant node carrying no opcode is full
                //white, not black - and it is the most common node in the cache, so defaulting
                //it to zero turned whole graphs black.
                case 0: node.IntParam0 = 4096; break;
                //Node_Sub10_Sub7.anInt5574 = 6. A blend node that carries no opcode 0 still
                //blends, and it blends in mode 6.
                case 7: node.BlendMode = 6; break;
                //Node_Sub10_Sub35: remap on, 4 octaves, persistence 1638, scales 4/4. The octave
                //count doubles as the element count opcode 2 reads its amplitude array against.
                case 34:
                    node.IntParam0 = 1;
                    node.IntParam1 = 4;
                    node.IntParam2 = 1638;
                    node.IntParam3 = 4;
                    node.IntParam4 = 4;
                    break;
                //Node_Sub10_Sub10 remaps into [anInt5591, anInt5592] = [1024, 3072].
                case 30: node.IntParam0 = 1024; node.IntParam1 = 3072; break;
                //Node_Sub10_Sub15 clamps into [0, 4096]. The upper bound has to start at 4096
                //rather than be substituted when the field reads zero, or a node that genuinely
                //encodes an upper bound of zero gets the opposite of what it asked for.
                case 6: node.IntParam1 = 4096; break;
                //Node_Sub10_Sub14: tolerance anInt5604 = 409, and the three per-channel scales
                //anInt5611/anInt5607/anInt5605 all start at 4096. Zero-initialised they would
                //multiply every keyed pixel to black, which is the opposite of the identity the
                //client starts from. The one type 25 node in this cache carries all four
                //opcodes, so this only matters for a node the editor builds by hand.
                case 25:
                    node.IntParam0 = 409;
                    node.IntParam1 = node.IntParam2 = node.IntParam3 = 4096;
                    break;
                //Node_Sub10_Sub20: slope scale anInt5637 = 4096, and aBoolean5636 = true, which
                //folds each normal axis into the upper half of the range. Zero-initialised the
                //scale would flatten every normal to (0, 0, 4096) and the node would emit one
                //constant colour, which is the opposite of what a node carrying no opcode asks
                //for.
                case 33: node.IntParam0 = 4096; node.IntParam1 = 1; break;
                //Node_Sub10_Sub26: 5/5 cells, seed 0, jitter 2048, output mode 2, metric 1.
                case 15:
                    node.IntParam0 = node.IntParam1 = 5;
                    node.IntParam3 = 2048;
                    node.IntParam4 = 2;
                    node.IntParam5 = 1;
                    break;
            }
        }

        /// <summary>
        /// Runs the client's <c>method1001</c> for the node types that have one - the hook that
        /// turns decoded parameters into the lookup tables their evaluators read.
        /// </summary>
        /// <remarks>
        /// This has to run after the whole opcode list, not per opcode, because the tables
        /// depend on several parameters at once.
        /// </remarks>
        private static void PostInitNode(TextureNode node, int nodeType)
        {
            switch (nodeType)
            {
                case 8: InitCurveTransfer(node); break;
                case 15: InitWorley(node); break;
                case 34: InitFractalNoise(node); break;
            }
        }

        /// <summary>
        /// Builds the 257-entry transfer curve a type 8 node maps its input through, matching
        /// <c>Node_Sub10_Sub9.method1001</c> and <c>method1031</c>.
        /// </summary>
        /// <remarks>
        /// All three interpolations run in 12-bit fixed point against the client's own cosine
        /// table, because the curve feeds a palette index and floating-point rounding shows up
        /// as banding. The previous implementation rebuilt an approximation of this on every
        /// scanline.
        /// </remarks>
        internal static void InitCurveTransfer(TextureNode node)
        {
            int[][] markers = node.GradientData;

            //Node_Sub10_Sub9.method1001: an absent curve is the identity ramp.
            if (markers == null || markers.Length == 0)
                markers = new[] { new[] { 0, 0 }, new[] { 4096, 4096 } };

            //The client throws here. A malformed graph should cost one texture rather than the
            //whole tab, so this leaves the curve null and the evaluator passes its input through.
            if (markers.Length < 2)
                return;

            int mode = node.GradientPreset;
            int count = markers.Length;

            //method1034, only built for the cubic. Both are reflections, and only the y matters.
            //The trailing one reflects m[n-2] through m[n-1]'s side, which reads backwards but
            //is what the client computes.
            int virtualBeforeY = 0, virtualAfterY = 0;
            if (mode == 2)
            {
                virtualBeforeY = 2 * markers[0][1] - markers[1][1];
                virtualAfterY = 2 * markers[count - 2][1] - markers[count - 1][1];
            }

            var lut = new int[257];
            for (int i = 0; i <= 256; i++)
            {
                int pos = i << 4;

                //The search stops at count - 1, so a position past the last marker keeps the
                //final segment and the interpolation runs on with t above 4096 - the client
                //extrapolates here rather than clamping.
                int k = 1;
                while (k < count - 1 && pos >= markers[k][0])
                    k++;

                int[] lo = markers[k - 1];
                int[] hi = markers[k];

                int span = hi[0] - lo[0];
                //Two markers at the same position would divide by zero in the client.
                int t = span != 0 ? ((pos - lo[0]) << 12) / span : 0;

                int value;
                switch (mode)
                {
                    case 1: // cosine
                    {
                        int s = (4096 - TextureNoise.Cos[(t & 0x1fe0) >> 5]) >> 1;
                        value = (hi[1] * s + (4096 - s) * lo[1]) >> 12;
                        break;
                    }
                    case 2: // cubic
                    {
                        int p0 = CurveY(markers, virtualBeforeY, virtualAfterY, k - 2);
                        int p1 = lo[1];
                        int p2 = hi[1];
                        int p3 = CurveY(markers, virtualBeforeY, virtualAfterY, k + 1);

                        //Paul Bourke's cubic: the knot tangent is p2 - p0, not the half-scaled
                        //Catmull-Rom tangent. Both pass through p1 and p2, so the difference is
                        //invisible at the knots and wrong everywhere between them.
                        int a = p3 - p2 - p0 + p1;
                        int b = p0 - p1 - a;
                        int c = p2 - p0;

                        int tSq = t * t >> 12;
                        value = p1 + (tSq * b >> 12) + ((a * t >> 12) * tSq >> 12) + (c * t >> 12);
                        break;
                    }
                    default: // linear
                        value = (hi[1] * t + (4096 - t) * lo[1]) >> 12;
                        break;
                }

                if (value <= -32768) value = -32767;
                else if (value >= 32768) value = 32767;
                lut[i] = (short)value;
            }

            node.CurveLut = lut;
        }

        /// <summary>
        /// The curve y at <paramref name="index"/>, substituting the reflected virtual markers
        /// off either end. Matches <c>Node_Sub10_Sub9.method1035</c>.
        /// </summary>
        private static int CurveY(int[][] markers, int beforeY, int afterY, int index)
        {
            if (index < 0)
                return beforeY;
            if (index >= markers.Length)
                return afterY;
            return markers[index][1];
        }

        private static void InitWorley(TextureNode node)
        {
            node.Permutation = TextureNoise.Permutation(node.IntParam2);

            //A second, independently seeded pass over the same seed - the client builds the
            //jitter table from a fresh Random rather than continuing the shuffle's stream.
            var random = new TextureNoise.JavaRandom(node.IntParam2);
            int magnitude = node.IntParam3 > 0 ? node.IntParam3 : 1;
            node.Jitter = new int[512];
            for (int i = 0; i < 512; i++)
                node.Jitter[i] = TextureNoise.NextBounded(magnitude, random);
        }

        private static void InitFractalNoise(TextureNode node)
        {
            node.Permutation = TextureNoise.Permutation(node.IntParam5);

            int octaves = node.IntParam1;
            int persistence = node.IntParam2;
            if (octaves < 1)
                octaves = 1;

            if (persistence <= 0)
            {
                //Negative persistence meant "the amplitudes are listed explicitly", and they
                //were read into ShortData. The client only builds the frequency ladder.
                if (node.ShortData == null || node.ShortData.Length != octaves)
                    return;
                node.Amplitudes = node.ShortData;
                node.Frequencies = new int[octaves];
                for (int i = 0; i < octaves; i++)
                    node.Frequencies[i] = (short)(int)Math.Pow(2.0, i);
            }
            else
            {
                node.Amplitudes = new int[octaves];
                node.Frequencies = new int[octaves];
                for (int i = 0; i < octaves; i++)
                {
                    node.Amplitudes[i] = (short)(int)(4096.0 * Math.Pow(persistence / 4096.0f, i));
                    node.Frequencies[i] = (short)(int)Math.Pow(2.0, i);
                }
            }

            //Drop trailing octaves that contribute nothing, never going below one.
            while (octaves > 1 && Math.Abs(node.Amplitudes[octaves - 1]) <= 8)
                octaves--;
            node.IntParam1 = octaves;
        }

        /// <summary>
        /// Decodes a texture graph, building the full node graph and extracting sprite IDs.
        /// </summary>
        public static Texture Decode(JagStream buffer)
        {
            var tex = new Texture();
            var spriteIds = new List<int>();
            var record = new TextureGraphRecord();
            tex._record = record;

            int nodeCount = buffer.ReadUnsignedByte();
            var nodes = new TextureNode[nodeCount];

            for (int n = 0; n < nodeCount; n++)
            {
                var node = new TextureNode();
                nodes[n] = node;
                var nodeRecord = new TextureNodeRecord();
                record.Nodes.Add(nodeRecord);

                // Node header (Node_Sub46_Sub11.method1581)
                nodeRecord.Version = buffer.ReadUnsignedByte(); // read and discarded by the client
                int nodeType = buffer.ReadUnsignedByte();
                node.Type = nodeType;
                nodeRecord.Type = nodeType;
                InitNodeDefaults(node, nodeType);
                nodeRecord.OutputSize = buffer.ReadUnsignedByte(); // anInt3860
                int opcodeCount = buffer.ReadUnsignedByte();

                // Decode each opcode into config fields.
                for (int op = 0; op < opcodeCount; op++)
                {
                    int opcode = buffer.ReadUnsignedByte();

                    //Captured either side of the payload rather than derived from the opcode,
                    //because several opcodes read a variable number of bytes and some read bytes
                    //nothing decodes - a type 29 shape record is skipped by width, and several type
                    //12 opcodes are recognised while consuming nothing.
                    //Capturing the span is also what makes a width error invisible to the
                    //byte-identity sweep, since wrongly-sized spans still tile the file exactly.
                    //RealCacheTextureGraphTests.EveryOpcodePayload_IsTheWidthTheClientReads is what
                    //closes that, by measuring every span against the client's own node classes.
                    long payloadStart = buffer.Position;

                    //A few opcodes do not configure the node at all - they overwrite its
                    //monochrome flag, which decides whether the evaluator asks it for one
                    //channel or three. The client keeps that in the node rather than in a
                    //config field (aBoolean3861), so it has to be applied here.
                    if (nodeType >= 0 && nodeType < MonoOverrideOpcode.Length &&
                        MonoOverrideOpcode[nodeType] == opcode)
                    {
                        node.MonoOverride = buffer.ReadUnsignedByte() == 1;
                    }
                    else if (!DecodeNodeOpcode(node, nodeType, opcode, buffer))
                    {
                        //Node_Sub10.method991 is an empty method, so the client consumes no
                        //bytes for an opcode the node does not recognise and carries on. Doing
                        //anything else here - in particular abandoning the graph, which this
                        //decoder used to do - throws away textures the client renders fine.
                        Debug($"Texture graph: unhandled opcode {opcode} for node type {nodeType}", LOG_DETAIL.ADVANCED);
                        if (tex.UnhandledNodeType < 0)
                        {
                            tex.UnhandledNodeType = nodeType;
                            tex.UnhandledOpcode = opcode;
                        }
                    }

                    nodeRecord.Opcodes.Add(new TextureOpcodeRecord(opcode,
                        Span(buffer, payloadStart, buffer.Position)));
                }

                PostInitNode(node, nodeType);

                // Read child connection indices
                int childCount = nodeType < ChildCounts.Length ? ChildCounts[nodeType] : 0;
                node.ChildIndices = new int[childCount];
                nodeRecord.ChildIndices = new byte[childCount];
                for (int c = 0; c < childCount; c++)
                {
                    node.ChildIndices[c] = buffer.ReadUnsignedByte();
                    nodeRecord.ChildIndices[c] = (byte) node.ChildIndices[c];
                }

                // Collect sprite IDs
                if (node.SpriteId >= 0)
                    spriteIds.Add(node.SpriteId);
            }

            // 3 output node indices (colour, alpha, brightness)
            int colourIdx = -1, alphaIdx = -1, brightnessIdx = -1;
            if (nodeCount > 0)
            {
                colourIdx = buffer.ReadUnsignedByte();
                alphaIdx = buffer.ReadUnsignedByte();
                brightnessIdx = buffer.ReadUnsignedByte();
                record.HasOutputIndices = true;
                record.ColourOutputIndex = colourIdx;
                record.AlphaOutputIndex = alphaIdx;
                record.BrightnessOutputIndex = brightnessIdx;
            }

            //Everything the client parses is behind us; what is left is the trailer it never
            //reads. Taken at a fixed width rather than as "whatever remains" so that a file
            //carrying more or fewer trailing bytes than the index is measured to have fails here
            //instead of being absorbed silently - the same reason the exact-consumption sweeps
            //decode against a padded buffer.
            record.BodyLength = buffer.Position;
            record.Trailer = buffer.ReadBytes(TrailerBytes);

            // Wire up child references (second pass)
            for (int n = 0; n < nodeCount; n++)
            {
                var node = nodes[n];
                if (node.ChildIndices != null)
                {
                    node.Children = new TextureNode[node.ChildIndices.Length];
                    for (int c = 0; c < node.ChildIndices.Length; c++)
                    {
                        int idx = node.ChildIndices[c];
                        if (idx >= 0 && idx < nodeCount)
                            node.Children[c] = nodes[idx];
                    }
                }
            }

            tex._spriteIds = spriteIds.ToArray();
            tex._graph = new TextureGraph
            {
                Nodes = nodes,
                ColourOutputIndex = colourIdx,
                AlphaOutputIndex = alphaIdx,
                BrightnessOutputIndex = brightnessIdx,
            };
            return tex;
        }

        /// <summary>
        /// Decodes a single opcode for a node, storing the value in the node's config fields.
        /// Returns true on success, false if the opcode is unparseable.
        /// </summary>
        private static bool DecodeNodeOpcode(TextureNode node, int nodeType, int opcode, JagStream buf)
        {
            switch (nodeType)
            {
                case 0: // Constant (Sub13)
                    //(b << 12) / 255, not b << 4: the client's scale reaches a full 4096 at 255
                    //rather than stopping at 4080.
                    if (opcode == 0) { node.IntParam0 = (buf.ReadUnsignedByte() << 12) / 255; return true; }
                    return false;

                case 1: // ConstantColour (Sub22) — 3-byte medium
                    if (opcode == 0) { node.IntParam0 = buf.ReadMedium(); return true; }
                    return false;

                case 2: // HorizontalGrad (Sub18) — no opcodes
                case 3: // VerticalGrad (Sub3) — no opcodes
                case 13: // Voronoi (Sub8) — no opcodes
                case 24: // MergeRGB (Sub16) — no opcodes
                    return true;

                case 4: // Brick (Sub38)
                    if (opcode == 0) { node.IntParam0 = buf.ReadUnsignedByte(); return true; }
                    if (opcode == 1) { node.IntParam1 = buf.ReadUnsignedByte(); return true; }
                    if (opcode == 2) { node.IntParam2 = buf.ReadUnsignedShort(); return true; }
                    if (opcode == 3) { node.IntParam3 = buf.ReadUnsignedShort(); return true; }
                    if (opcode == 4) { node.IntParam4 = buf.ReadUnsignedShort(); return true; }
                    if (opcode == 5) { node.IntParam5 = buf.ReadUnsignedShort(); return true; }
                    if (opcode == 6) { node.IntParam6 = buf.ReadUnsignedShort(); return true; }
                    if (opcode == 7) { node.IntParam7 = buf.ReadUnsignedShort(); return true; }
                    return false;

                case 5: // BoxBlur (Sub24)
                    if (opcode == 0) { node.IntParam0 = buf.ReadUnsignedByte(); return true; } // horizontal radius
                    if (opcode == 1) { node.IntParam1 = buf.ReadUnsignedByte(); return true; } // vertical radius
                    if (opcode == 2) { node.IntParam2 = buf.ReadUnsignedByte(); return true; } // mono flag
                    return false;

                case 6: // Clamp (Sub15)
                    if (opcode == 0) { node.IntParam0 = buf.ReadUnsignedShort(); return true; } // lower bound
                    if (opcode == 1) { node.IntParam1 = buf.ReadUnsignedShort(); return true; } // upper bound
                    if (opcode == 2) { node.IntParam2 = buf.ReadUnsignedByte(); return true; }  // mono flag
                    return false;

                case 7: // ColourBlend (Sub7)
                    //Opcode 0 is the blend mode itself (anInt5574, dispatched over 1..12), not a
                    //scaled amount - shifting it left by 4 selected a mode that does not exist.
                    //Opcode 1 is the monochrome flag and is consumed before this switch.
                    if (opcode == 0) { node.BlendMode = buf.ReadUnsignedByte(); return true; }
                    return false;

                case 8: // CurveTransfer (Sub9) — mono spline/curve
                    if (opcode == 0)
                    {
                        node.GradientPreset = buf.ReadUnsignedByte(); // interp: 0=linear, 1=cosine, 2=catmull-rom
                        int count = buf.ReadUnsignedByte();
                        node.GradientCount = count;
                        node.GradientData = new int[count][];
                        for (int i = 0; i < count; i++)
                        {
                            node.GradientData[i] = new int[2];
                            node.GradientData[i][0] = buf.ReadUnsignedShort(); // x position
                            node.GradientData[i][1] = buf.ReadUnsignedShort(); // y value
                        }
                        return true;
                    }
                    return false;

                case 9: // Invert (Sub11)
                    if (opcode == 0) { node.IntParam0 = buf.ReadUnsignedByte(); return true; }
                    if (opcode == 1) { node.IntParam1 = buf.ReadUnsignedByte(); return true; }
                    if (opcode == 2) { node.IntParam2 = buf.ReadUnsignedByte(); return true; }
                    return false;

                case 10: // Gradient/Transfer (Sub33) — variable opcode 0
                    if (opcode == 0)
                    {
                        int preset = buf.ReadUnsignedByte();
                        node.GradientPreset = preset;
                        if (preset == 0)
                        {
                            int count = buf.ReadUnsignedByte();
                            node.GradientCount = count;
                            node.GradientData = new int[count][];
                            for (int i = 0; i < count; i++)
                            {
                                node.GradientData[i] = new int[4];
                                node.GradientData[i][0] = buf.ReadUnsignedShort(); // position
                                node.GradientData[i][1] = buf.ReadUnsignedByte();  // r
                                node.GradientData[i][2] = buf.ReadUnsignedByte();  // g
                                node.GradientData[i][3] = buf.ReadUnsignedByte();  // b
                            }
                        }
                        return true;
                    }
                    return false;

                case 11: // HSLAdjust (Sub4)
                    if (opcode == 0) { node.IntParam0 = buf.ReadUnsignedShort(); return true; }
                    if (opcode == 1) { node.IntParam1 = buf.ReadUnsignedShort(); return true; }
                    if (opcode == 2) { node.IntParam2 = buf.ReadUnsignedShort(); return true; }
                    return false;

                case 12: // Noise (Sub30)
                    if (opcode == 0) { node.IntParam0 = buf.ReadUnsignedByte(); return true; }
                    if (opcode == 1) { node.IntParam1 = buf.ReadUnsignedByte(); return true; }
                    if (opcode == 3) { node.IntParam2 = buf.ReadUnsignedByte(); return true; }
                    //Node_Sub10_Sub30's arms stop at 0, 1 and 3. Measured over both caches, four
                    //further opcodes reach here - 2, 4, 5 and 6, two graphs each - and every one
                    //falls through to the base class and reads nothing. Recognised rather than
                    //reported, so the unhandled-opcode check keeps meaning "the opcode tables have
                    //a gap".
                    //An earlier version of this comment said "opcodes 2 and 4", which was taken
                    //from the survey rather than measured, and the survey was wrong. The census in
                    //RealCacheTextureGraphTests prints the real set; read it rather than this line
                    //if they ever disagree again.
                    return true;

                case 14: // SineWave (Sub17)
                    if (opcode == 0) { node.IntParam0 = buf.ReadUnsignedShort(); return true; }
                    return false;

                case 15: // WorleyNoise (Sub26)
                    //IntParam0/1 x and y cell frequency, 2 seed, 3 jitter, 4 output mode,
                    //5 distance metric. Opcode 0 sets both frequencies at once.
                    if (opcode == 0) { node.IntParam0 = node.IntParam1 = buf.ReadUnsignedByte(); return true; }
                    if (opcode == 1) { node.IntParam2 = buf.ReadUnsignedByte(); return true; }  // seed
                    if (opcode == 2) { node.IntParam3 = buf.ReadUnsignedShort(); return true; } // jitter
                    if (opcode == 3) { node.IntParam4 = buf.ReadUnsignedByte(); return true; }  // output mode
                    if (opcode == 4) { node.IntParam5 = buf.ReadUnsignedByte(); return true; }  // metric
                    if (opcode == 5) { node.IntParam0 = buf.ReadUnsignedByte(); return true; }  // x frequency
                    if (opcode == 6) { node.IntParam1 = buf.ReadUnsignedByte(); return true; }  // y frequency
                    return false;

                case 16: // Threshold (Sub32)
                    if (opcode == 0) { node.IntParam0 = buf.ReadUnsignedByte() << 4; return true; }
                    if (opcode == 1) { node.IntParam1 = buf.ReadUnsignedByte() << 4; return true; }
                    if (opcode == 2) { node.IntParam2 = buf.ReadUnsignedShort(); return true; }
                    return false;

                case 17: // Blur (Sub6)
                    if (opcode == 0) { node.IntParam0 = buf.ReadUnsignedShort(); return true; }
                    if (opcode == 1) { node.IntParam1 = buf.ReadSignedByte(); return true; }
                    if (opcode == 2) { node.IntParam2 = buf.ReadSignedByte(); return true; }
                    return false;

                case 18: // SpriteSourceTiled (Sub5_Sub1) — inherits type 39
                case 39: // SpriteSource (Sub5)
                    if (opcode == 0) { node.SpriteId = buf.ReadUnsignedShort(); return true; }
                    return false;

                case 19: // PolarDistortion (Sub2)
                    if (opcode == 0) { node.IntParam0 = buf.ReadUnsignedShort() << 4; return true; } // scale, default 32768
                    if (opcode == 1) { node.IntParam1 = buf.ReadUnsignedByte(); return true; }       // mono flag
                    return false;

                case 20: // Tile/Scale (Sub29) — tile division counts, NOT 12-bit values
                    if (opcode == 0) { node.IntParam0 = buf.ReadUnsignedByte(); return true; }
                    if (opcode == 1) { node.IntParam1 = buf.ReadUnsignedByte(); return true; }
                    return false;

                case 21: // Emboss (Sub12)
                    if (opcode == 0) { node.IntParam0 = buf.ReadUnsignedByte() << 4; return true; }
                    return false;

                case 22: // FlipH (Sub39)
                    if (opcode == 0) { node.IntParam0 = buf.ReadUnsignedByte(); return true; }
                    return false;

                case 23: // FlipV (Sub27)
                    if (opcode == 0) { node.IntParam0 = buf.ReadUnsignedByte(); return true; }
                    return false;

                case 25: // ColourKeyScale (Sub14)
                    //0 is the match tolerance, then the blue, green and red scales in that
                    //order - Node_Sub10_Sub14.method991 assigns anInt5611, anInt5607 and
                    //anInt5605, and method997 reads them back as B, G and R.
                    if (opcode <= 3) { SetIntParam(node, opcode, buf.ReadUnsignedShort()); return true; }
                    if (opcode == 4) { node.IntParam4 = buf.ReadMedium(); return true; }
                    return false;

                case 26: // Turbulence (Sub31)
                    if (opcode == 0) { node.IntParam0 = buf.ReadUnsignedShort(); return true; }
                    if (opcode == 1) { node.IntParam1 = buf.ReadUnsignedShort(); return true; }
                    return false;

                case 27: // Lines/Scratch (Sub23)
                    if (opcode == 0) { node.IntParam0 = buf.ReadUnsignedByte(); return true; }
                    if (opcode == 1) { node.IntParam1 = buf.ReadUnsignedShort(); return true; }
                    if (opcode == 2) { node.IntParam2 = buf.ReadUnsignedByte(); return true; }
                    return false;

                case 28: // Mandelbrot (Sub28)
                    if (opcode == 0) { node.IntParam0 = buf.ReadUnsignedByte(); return true; }
                    if (opcode == 1) { node.IntParam1 = buf.ReadUnsignedShort(); return true; }
                    if (opcode == 2) { node.IntParam2 = buf.ReadUnsignedShort(); return true; }
                    if (opcode == 3) { node.IntParam3 = buf.ReadUnsignedShort(); return true; }
                    if (opcode == 4) { node.IntParam4 = buf.ReadUnsignedShort(); return true; }
                    if (opcode == 5) { node.IntParam5 = buf.ReadUnsignedShort(); return true; }
                    if (opcode == 6) { node.IntParam6 = buf.ReadUnsignedByte(); return true; }
                    if (opcode == 7) { node.IntParam7 = buf.ReadUnsignedShort(); return true; }
                    if (opcode == 8) { node.IntParam8 = buf.ReadUnsignedShort(); return true; }
                    return false;

                case 29: // ShapeList (Sub36)
                    //Opcode 0 is a shape list, not an opaque blob: a count, then per entry a
                    //shape id that selects one of four fixed-size records. Bailing on it cost
                    //every graph that uses one. Opcode 1 is the monochrome flag, consumed
                    //before this switch.
                    if (opcode == 0)
                    {
                        int shapeCount = buf.ReadUnsignedByte();
                        node.ShapeIds = new int[shapeCount];
                        for (int i = 0; i < shapeCount; i++)
                        {
                            int shapeId = buf.ReadUnsignedByte();
                            node.ShapeIds[i] = shapeId;
                            switch (shapeId)
                            {
                                case 0: buf.Skip(12); break; // Class255.method3192
                                case 1: buf.Skip(20); break; // Node_Sub10_Sub14.method1046
                                case 2: buf.Skip(15); break; // Class258.method3203
                                case 3: buf.Skip(15); break; // Class300.method3533
                                default: break;              // unknown id reads nothing
                            }
                        }
                        return true;
                    }
                    return false;

                case 30: // EdgeDetect (Sub10)
                    if (opcode == 0) { node.IntParam0 = buf.ReadUnsignedShort(); return true; }
                    if (opcode == 1) { node.IntParam1 = buf.ReadUnsignedShort(); return true; }
                    if (opcode == 2) { node.IntParam2 = buf.ReadUnsignedByte(); return true; }
                    return false;

                case 31: // Square (Sub34)
                    if (opcode == 0) { node.IntParam0 = buf.ReadUnsignedShort(); return true; }
                    if (opcode == 1) { node.IntParam1 = buf.ReadUnsignedShort(); return true; }
                    if (opcode == 2) { node.IntParam2 = buf.ReadUnsignedShort(); return true; }
                    if (opcode == 3) { node.IntParam3 = buf.ReadUnsignedShort(); return true; }
                    return false;

                case 32: // PolarWarp (Sub37)
                    if (opcode == 0) { node.IntParam0 = buf.ReadUnsignedShort(); return true; }
                    if (opcode == 1) { node.IntParam1 = buf.ReadUnsignedShort(); return true; }
                    if (opcode == 2) { node.IntParam2 = buf.ReadUnsignedShort(); return true; }
                    return false;

                case 33: // SurfaceNormal (Sub20)
                    //Not an offset or a scroll, whatever this arm used to be labelled:
                    //Node_Sub10_Sub20.method997 reads its child as a height field and emits a
                    //surface normal. IntParam0 is anInt5637, the slope scale; IntParam1 is
                    //aBoolean5636, the signed-to-unsigned remap.
                    if (opcode == 0) { node.IntParam0 = buf.ReadUnsignedShort(); return true; }
                    if (opcode == 1) { node.IntParam1 = buf.ReadUnsignedByte(); return true; }
                    return false;

                case 34: // FractalNoise (Sub35)
                    //IntParam0 signed-to-unsigned remap, 1 octave count, 2 persistence,
                    //3 x scale, 4 y scale, 5 permutation seed. Opcode 3 sets both scales.
                    if (opcode == 0) { node.IntParam0 = buf.ReadUnsignedByte(); return true; }
                    if (opcode == 1) { node.IntParam1 = buf.ReadUnsignedByte(); return true; }
                    if (opcode == 2)
                    {
                        //A signed short, and when it is negative a run of IntParam1 shorts
                        //follows. IntParam1 is opcode 1's count and defaults to 4, which is why
                        //this looked unparseable in isolation - the length lives in a sibling
                        //opcode. This single bail discarded a third of every texture graph in
                        //the cache.
                        node.IntParam2 = buf.ReadShort();
                        if (node.IntParam2 < 0)
                        {
                            int count = node.IntParam1;
                            node.ShortData = new int[count];
                            for (int i = 0; i < count; i++)
                                node.ShortData[i] = buf.ReadShort();
                        }
                        return true;
                    }
                    //Opcode 3 writes both scales at once; 5 and 6 then set them individually.
                    if (opcode == 3) { node.IntParam3 = node.IntParam4 = buf.ReadUnsignedByte(); return true; }
                    if (opcode == 4) { node.IntParam5 = buf.ReadUnsignedByte(); return true; }
                    if (opcode == 5) { node.IntParam3 = buf.ReadUnsignedByte(); return true; }
                    if (opcode == 6) { node.IntParam4 = buf.ReadUnsignedByte(); return true; }
                    return false;

                case 35: // Scale (Sub1)
                    if (opcode == 0) { node.IntParam0 = buf.ReadUnsignedShort(); return true; }
                    return false;

                case 36: // NestedTexture (Sub25)
                    //Not a pattern generator: Node_Sub10_Sub25.method992 hands this value back
                    //as a texture dependency and method998 renders that texture into the node.
                    if (opcode == 0) { node.NestedTextureId = buf.ReadUnsignedShort(); return true; }
                    return false;

                case 37: // Abs/Mirror (Sub21)
                    if (opcode <= 6) { SetIntParam(node, opcode, buf.ReadUnsignedShort()); return true; }
                    return false;

                case 38: // Tile/Wrap (Sub19)
                    if (opcode == 0) { node.IntParam0 = buf.ReadUnsignedByte(); return true; }
                    if (opcode == 1) { node.IntParam1 = buf.ReadUnsignedShort(); return true; }
                    if (opcode == 2) { node.IntParam2 = buf.ReadUnsignedByte(); return true; }
                    if (opcode == 3) { node.IntParam3 = buf.ReadUnsignedShort(); return true; }
                    if (opcode == 4) { node.IntParam4 = buf.ReadUnsignedShort(); return true; }
                    return false;
            }

            // Unknown node type or opcode
            return false;
        }

        /// <summary>
        /// Copies the bytes a decode step consumed, without disturbing the read head.
        /// </summary>
        /// <remarks>
        /// Taken from the stream's own positions rather than from a per-opcode width table, so it
        /// stays correct for the opcodes whose payload length depends on data - a type 8 curve, a
        /// type 10 gradient, a type 29 shape list and type 34's explicit amplitude run all size
        /// themselves from a count they carry.
        /// </remarks>
        /// <param name="buffer">The stream being decoded.</param>
        /// <param name="from">Offset the step started at.</param>
        /// <param name="to">Offset it ended at.</param>
        /// <returns>The consumed bytes, empty when the step read none.</returns>
        private static byte[] Span(JagStream buffer, long from, long to)
        {
            int length = (int) (to - from);
            if (length <= 0)
                return Array.Empty<byte>();

            var bytes = new byte[length];
            for (int i = 0; i < length; i++)
                bytes[i] = buffer.Get((int) from + i);
            return bytes;
        }

        private static void SetIntParam(TextureNode node, int index, int value)
        {
            switch (index)
            {
                case 0: node.IntParam0 = value; break;
                case 1: node.IntParam1 = value; break;
                case 2: node.IntParam2 = value; break;
                case 3: node.IntParam3 = value; break;
                case 4: node.IntParam4 = value; break;
                case 5: node.IntParam5 = value; break;
                case 6: node.IntParam6 = value; break;
                case 7: node.IntParam7 = value; break;
                case 8: node.IntParam8 = value; break;
            }
        }
    }
}
