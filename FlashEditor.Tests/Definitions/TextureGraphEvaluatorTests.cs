using FlashEditor.Definitions.Sprites;
using FlashEditor.Utils;
using System;
using System.Drawing;
using System.Reflection;
using Xunit;

namespace FlashEditor.Tests.Definitions
{
    /// <summary>
    /// Coverage of the procedural texture graph evaluator.
    ///
    /// Render is the only public entry point, so every case drives a hand-built graph
    /// through it. All node types except 18 and 39 are pure functions of their params,
    /// children and the coordinate LUTs, so a null RSCache is safe throughout.
    ///
    /// Tests named *_DocumentsKnownDefect pin CURRENT behaviour that is known to be wrong,
    /// so a future fix surfaces as a deliberate test change rather than a silent swap.
    /// </summary>
    public class TextureGraphEvaluatorTests
    {
        private const int Size = 8;              // keep renders small: several node types are O(n^2)
        private const int White = 0xFFFFFF;      // type 1 packs RGB as 0xRRGGBB
        private const int Black = 0x000000;

        public TextureGraphEvaluatorTests()
        {
            DebugUtil.LOG_LEVEL = DebugUtil.LOG_DETAIL.NONE;
        }

        // ===================================================================
        //  Helpers
        // ===================================================================

        private static TextureGraph Graph(int colourOutputIndex, params TextureNode[] nodes) =>
            new TextureGraph { Nodes = nodes, ColourOutputIndex = colourOutputIndex };

        /// <summary>Single-node graph whose only node is the colour output.</summary>
        private static TextureGraph Single(TextureNode node) => Graph(0, node);

        private static TextureNode Constant(int value) =>
            new TextureNode { Type = 0, IntParam0 = value };

        private static TextureNode ConstantColour(int packedRgb) =>
            new TextureNode { Type = 1, IntParam0 = packedRgb };

        private static Color[,] Render(TextureGraph graph, int w = Size, int h = Size, bool transpose = false)
        {
            using Bitmap bmp = TextureGraphEvaluator.Render(graph, w, h, null, transpose);
            Assert.NotNull(bmp);

            var px = new Color[w, h];
            for (int x = 0; x < w; x++)
                for (int y = 0; y < h; y++)
                    px[x, y] = bmp.GetPixel(x, y);
            return px;
        }

        /// <summary>
        /// The value every guard clause falls back to (2040, mid-grey) once it has been
        /// through the gamma LUT. Derived by rendering rather than hardcoded, so the test
        /// does not restate the gamma implementation.
        /// </summary>
        private static byte MidGrey => Render(Single(Constant(2040)))[0, 0].R;

        // ===================================================================
        //  Render contract
        // ===================================================================

        [Fact]
        public void Render_NullGraph_ReturnsNull() =>
            Assert.Null(TextureGraphEvaluator.Render(null, Size, Size, null));

        [Fact]
        public void Render_NullNodeArray_ReturnsNull() =>
            Assert.Null(TextureGraphEvaluator.Render(new TextureGraph { Nodes = null }, Size, Size, null));

        [Fact]
        public void Render_EmptyNodeArray_ReturnsNull() =>
            Assert.Null(TextureGraphEvaluator.Render(new TextureGraph { Nodes = Array.Empty<TextureNode>() }, Size, Size, null));

        [Theory]
        [InlineData(-1)]
        [InlineData(1)]
        [InlineData(99)]
        public void Render_ColourOutputIndexOutOfRange_ReturnsNull(int index) =>
            Assert.Null(TextureGraphEvaluator.Render(Graph(index, Constant(0)), Size, Size, null));

        [Fact]
        public void Render_ColourOutputIndexPointsAtNullNode_ReturnsNull() =>
            Assert.Null(TextureGraphEvaluator.Render(Graph(0, (TextureNode) null), Size, Size, null));

        [Fact]
        public void Render_ProducesBitmapOfRequestedSize()
        {
            using Bitmap bmp = TextureGraphEvaluator.Render(Single(Constant(2048)), 16, 4, null);
            Assert.Equal(16, bmp.Width);
            Assert.Equal(4, bmp.Height);
        }

        [Fact]
        public void Render_NullCache_IsSafeForPureNodes()
        {
            // No node type other than 18/39 dereferences the cache, and those are gated on
            // SpriteId >= 0. Passing null must never throw.
            var ex = Record.Exception(() => Render(Single(Constant(1000))));
            Assert.Null(ex);
        }

        // ===================================================================
        //  Fixed point, clamping and gamma
        // ===================================================================

        [Theory]
        [InlineData(0, 0)]          // floor
        [InlineData(4080, 255)]     // FP_MAX maps to full scale
        [InlineData(4096, 255)]     // FP_ONE clamps down to FP_MAX
        [InlineData(9000, 255)]     // above range clamps
        [InlineData(-500, 0)]       // below range clamps
        public void Render_ConstantMono_ClampsTo12BitRangeBeforeGamma(int value, byte expected)
        {
            var px = Render(Single(Constant(value)));

            Assert.Equal(expected, px[0, 0].R);
            Assert.Equal(px[0, 0].R, px[0, 0].G);   // mono replicates across channels
            Assert.Equal(px[0, 0].R, px[0, 0].B);
        }

        [Fact]
        public void Render_ConstantMono_IsUniformAcrossTheWholeBitmap()
        {
            var px = Render(Single(Constant(4080)));

            for (int x = 0; x < Size; x++)
                for (int y = 0; y < Size; y++)
                    Assert.Equal(255, px[x, y].R);
        }

        [Fact]
        public void Render_GammaIsMonotonicNonDecreasing()
        {
            byte previous = 0;
            for (int v = 0; v <= 4080; v += 240)
            {
                byte current = Render(Single(Constant(v)))[0, 0].R;
                Assert.True(current >= previous, $"gamma decreased at {v}: {current} < {previous}");
                previous = current;
            }
        }

        // ===================================================================
        //  Colour channel ordering
        // ===================================================================

        [Theory]
        [InlineData(0xFF0000, 255, 0, 0)]
        [InlineData(0x00FF00, 0, 255, 0)]
        [InlineData(0x0000FF, 0, 0, 255)]
        [InlineData(0xFFFFFF, 255, 255, 255)]
        [InlineData(0x000000, 0, 0, 0)]
        public void Render_ConstantColour_UnpacksRgbInTheRightOrder(int packed, byte r, byte g, byte b)
        {
            var px = Render(Single(ConstantColour(packed)));

            Assert.Equal(r, px[0, 0].R);
            Assert.Equal(g, px[0, 0].G);
            Assert.Equal(b, px[0, 0].B);
        }

        // ===================================================================
        //  Alpha
        // ===================================================================

        /// <summary>
        /// DEFECT: the alpha output node is hard-disabled, so every rendered pixel is fully
        /// opaque regardless of what AlphaOutputIndex points at. Per-face transparency is
        /// handled at model level instead, but the graph's own alpha channel is discarded.
        /// </summary>
        [Fact]
        public void Render_AlphaOutputNode_IsNotSampled_MatchingMethod1631()
        {
            // Not a defect: Render follows the client's method1631, which derives alpha from
            // the colour and never reads the alpha output node. Sampling it is method1633,
            // a separate entry point used for GL uploads.
            var graph = Graph(0, ConstantColour(White), Constant(0));
            graph.AlphaOutputIndex = 1;   // a constant-zero node: would be fully transparent if honoured

            var px = Render(graph);

            for (int x = 0; x < Size; x++)
                for (int y = 0; y < Size; y++)
                    Assert.Equal(255, px[x, y].A);
        }

        // ===================================================================
        //  Coordinate-driven generators
        // ===================================================================

        [Fact]
        public void Render_HorizontalGradient_VariesAlongXAndIsConstantAlongY()
        {
            var px = Render(Single(new TextureNode { Type = 2 }));

            Assert.Equal(0, px[0, 0].R);                       // xCoord[0] == 0
            Assert.True(px[Size - 1, 0].R > px[0, 0].R);       // increases left to right

            for (int y = 1; y < Size; y++)
                Assert.Equal(px[3, 0].R, px[3, y].R);          // no dependence on the row
        }

        [Fact]
        public void Render_VerticalGradient_VariesAlongYAndIsConstantAlongX()
        {
            var px = Render(Single(new TextureNode { Type = 3 }));

            Assert.Equal(0, px[0, 0].R);
            Assert.True(px[0, Size - 1].R > px[0, 0].R);

            for (int x = 1; x < Size; x++)
                Assert.Equal(px[0, 3].R, px[x, 3].R);
        }

        // ===================================================================
        //  Blend operations - the densest logic in the evaluator
        // ===================================================================

        private static TextureGraph BlendGraph(int mode, int aRgb, int bRgb)
        {
            var a = ConstantColour(aRgb);
            var b = ConstantColour(bRgb);
            var blend = new TextureNode
            {
                Type = 7,
                BlendMode = mode,
                Children = new[] { a, b }
            };
            return Graph(0, blend, a, b);
        }

        // a = 4080 (white), b = 0 (black). Modes 6 to 12 changed when the table was
        // replaced with the client's - the previous set had min/max/difference sitting on
        // ids 6/7/8, three places below where Node_Sub10_Sub7 puts them.
        [Theory]
        [InlineData(0, 0)]      // unknown mode yields b
        [InlineData(1, 255)]    // add
        [InlineData(2, 255)]    // subtract
        [InlineData(3, 0)]      // multiply
        [InlineData(4, 255)]    // divide, b == 0 short-circuits to one
        [InlineData(5, 255)]    // screen
        [InlineData(6, 0)]      // hard light, b below half takes the multiply arm
        [InlineData(7, 0)]      // colour dodge: b / (1 - a)
        [InlineData(8, 0)]      // colour burn: 1 - (1 - b) / a, negative here
        [InlineData(9, 0)]      // darken
        [InlineData(10, 255)]   // lighten
        [InlineData(11, 255)]   // difference
        [InlineData(12, 255)]   // vivid add
        [InlineData(99, 0)]     // unknown mode yields b
        public void Render_BlendMode_WhiteOverBlack_ProducesExpectedChannel(int mode, byte expected)
        {
            var px = Render(BlendGraph(mode, White, Black));
            Assert.Equal(expected, px[0, 0].R);
        }

        [Fact]
        public void Render_BlendMode_DivideByZero_ReturnsMaxRatherThanThrowing()
        {
            var ex = Record.Exception(() => Render(BlendGraph(4, White, Black)));
            Assert.Null(ex);
        }

        [Fact]
        public void Render_BlendWithNoModeOpcode_UsesHardLight()
        {
            // Node_Sub10_Sub7.anInt5574 initialises to 6, so a blend node that carries no
            // mode opcode still blends. The decoder seeds it; a hand-built node states it.
            var a = ConstantColour(White);
            var b = ConstantColour(White);
            var blend = new TextureNode { Type = 7, BlendMode = 6, Children = new[] { a, b } };

            // b is above half, so hard light takes the screen arm and stays at full scale.
            var px = Render(Graph(0, blend, a, b));
            Assert.Equal(255, px[0, 0].R);
        }

        // ===================================================================
        //  Guard clauses
        // ===================================================================

        [Theory]
        [InlineData(7)]    // colour blend, needs 2 children
        [InlineData(8)]    // curve transfer, needs 1
        [InlineData(99)]   // unknown mono type
        public void Render_NodeWithMissingChildren_FallsBackToMidGrey(int type)
        {
            var px = Render(Single(new TextureNode { Type = type }));

            Assert.Equal(MidGrey, px[0, 0].R);
            Assert.Equal(MidGrey, px[0, 0].G);
            Assert.Equal(MidGrey, px[0, 0].B);
        }

        // ===================================================================
        //  Type 25: colour key scale
        // ===================================================================

        /// <summary>
        /// A pixel outside the key tolerance on any one channel must come through untouched.
        /// </summary>
        /// <remarks>
        /// The pass-through arm is the half of <c>Node_Sub10_Sub14.method997</c> that texture
        /// 911 never exercises - its tolerance is 4096, so every pixel is keyed - which leaves
        /// the branch with no cover from the cache sweep. The assertion is identity rather than
        /// a restatement of the arithmetic, so it cannot pin an invented formula: the client
        /// copies the input channels across verbatim and so must this.
        /// </remarks>
        [Fact]
        public void Render_ColourKeyScale_LeavesPixelsOutsideTheToleranceAlone()
        {
            //Key black with a tolerance of 1, against a mid-grey input: no channel is within
            //reach of the key, so all three scales are ignored.
            var child = ConstantColour(0x808080);
            var keyed = new TextureNode
            {
                Type = 25,
                IntParam0 = 1,        // tolerance
                IntParam1 = 0,        // blue scale - would blank the channel if applied
                IntParam2 = 0,        // green scale
                IntParam3 = 0,        // red scale
                IntParam4 = 0x000000, // key colour
                Children = new[] { child }
            };

            Color actual = Render(Graph(0, keyed, child))[0, 0];
            Color untouched = Render(Single(ConstantColour(0x808080)))[0, 0];

            Assert.Equal(untouched.R, actual.R);
            Assert.Equal(untouched.G, actual.G);
            Assert.Equal(untouched.B, actual.B);
        }

        // ===================================================================
        //  Node types the dispatch used to route to the wrong evaluator
        // ===================================================================

        /// <summary>
        /// Type 24 averages its child's three channels into one.
        /// </summary>
        /// <remarks>
        /// <c>Node_Sub10_Sub16</c> is <c>super(1, true)</c> and overrides only <c>method990</c>,
        /// which reads the child's colour and writes <c>(r + g + b) / 3</c>. It was dispatched from
        /// the colour side, where a monochrome node never arrives, so <c>EvalMono</c> had no case
        /// for it and every type 24 node rendered flat mid-grey.
        /// <para>
        /// The expected value is rendered from a constant node carrying the average rather than
        /// written down, so this pins the merge without restating the gamma ramp - and it is
        /// checked against mid-grey too, since a defect that renders everything mid-grey would
        /// otherwise satisfy any single-value assertion by luck.
        /// </para>
        /// </remarks>
        [Fact]
        public void Render_Type24MergeRgb_AveragesItsChildsChannelsIntoOne()
        {
            //ConstantColour expands each 8-bit channel to 12 bits, so 0xFF0000 is (4080, 0, 0).
            var child = ConstantColour(0xFF0000);
            var merge = new TextureNode { Type = 24, Children = new[] { child } };

            var px = Render(Graph(0, merge, child));
            byte expected = Render(Single(Constant((4080 + 0 + 0) / 3)))[0, 0].R;

            Assert.NotEqual(MidGrey, expected);
            Assert.Equal(expected, px[0, 0].R);
            //A merge emits one channel, so the three come out equal after the mono replication.
            Assert.Equal(px[0, 0].R, px[0, 0].G);
            Assert.Equal(px[0, 0].R, px[0, 0].B);
        }

        /// <summary>
        /// Type 21 interpolates between its first two inputs by its third, on both paths.
        /// </summary>
        /// <remarks>
        /// <c>Node_Sub10_Sub12</c> is <c>super(3, false)</c>, and its <c>method990</c> and
        /// <c>method997</c> are the same mix: child 2's mono row is the factor, child 0 wins at
        /// 4096 and child 1 at 0. It had no colour arm at all, so all 128 of them in this index
        /// passed child 0 through untouched, and the mono arm was an emboss - a different operation
        /// reading a strength parameter a type 21 node cannot carry, since its only opcode is
        /// claimed by the monochrome flag.
        /// </remarks>
        [Theory]
        [InlineData(4096, 255, 0)]   // full factor selects child 0
        [InlineData(0, 0, 255)]      // zero factor selects child 1
        public void Render_Type21Mix_SelectsOneInputAtEitherEndOfTheFactor(int factor, byte red, byte green)
        {
            var px = MixOf(0xFF0000, 0x00FF00, factor);

            Assert.Equal(red, px.R);
            Assert.Equal(green, px.G);
            Assert.Equal(0, px.B);
        }

        /// <summary>Halfway between full red and full green is both channels at half.</summary>
        /// <remarks>
        ///     The expected value is <see cref="MidGrey"/> because half of 4080 is 2040, which is
        ///     the same 12-bit value every guard clause falls back to. Deriving it rather than
        ///     writing the post-gamma byte down keeps this from restating the gamma ramp.
        /// </remarks>
        [Fact]
        public void Render_Type21Mix_HalfFactorIsHalfOfEachInput()
        {
            Color px = MixOf(0xFF0000, 0x00FF00, 2048);

            Assert.Equal(MidGrey, px.R);
            Assert.Equal(MidGrey, px.G);
            Assert.Equal(0, px.B);
        }

        /// <summary>Renders a type 21 node over two constant colours and a constant factor.</summary>
        private static Color MixOf(int aRgb, int bRgb, int factor)
        {
            var a = ConstantColour(aRgb);
            var b = ConstantColour(bRgb);
            var blend = Constant(factor);
            var mix = new TextureNode { Type = 21, Children = new[] { a, b, blend } };

            return Render(Graph(0, mix, a, b, blend))[0, 0];
        }

        /// <summary>
        /// Type 33 turns a height field into a surface normal rather than passing it through.
        /// </summary>
        /// <remarks>
        /// <c>Node_Sub10_Sub20.method997</c> emits the normalised <c>(dx, dy, 4096)</c> of its
        /// child, one axis per channel, so a horizontal gradient - which slopes in x and is flat in
        /// y - must produce three channels that differ from one another and from the input. With no
        /// colour arm the node fell through to the pass-through default and reproduced the gradient
        /// exactly, which is what this asserts against.
        /// </remarks>
        [Fact]
        public void Render_Type33SurfaceNormal_EmitsANormalRatherThanItsChild()
        {
            var childForOp = new TextureNode { Type = 2 };
            var normal = new TextureNode
            {
                Type = 33,
                IntParam0 = 4096,   // anInt5637, the slope scale
                IntParam1 = 1,      // aBoolean5636, the signed-to-unsigned remap
                Children = new[] { childForOp }
            };

            var actual = Render(Graph(0, normal, childForOp));
            var childAlone = Render(Single(new TextureNode { Type = 2 }));

            //The x slope of a horizontal gradient is constant across the row bar the wrap, so the
            //red channel is flat where the child ramps.
            Assert.NotEqual(childAlone[Size - 1, 0].R, actual[Size - 1, 0].R);
            Assert.Equal(actual[2, 0].R, actual[3, 0].R);

            //Flat in y, so the green axis is the remap's midpoint everywhere, and z is not.
            Assert.NotEqual(actual[2, 0].G, actual[2, 0].B);
            for (int y = 0; y < Size; y++)
                Assert.Equal(actual[2, 0].G, actual[2, y].G);
        }

        /// <summary>
        /// A transposed render of a non-square graph produces the transposed image.
        /// </summary>
        /// <remarks>
        /// The transposed pixel index used the untransposed row length as its stride, so it walked
        /// off the end of the buffer for any width and height that differ. Production only ever
        /// renders square, which is why it survived. The transpose of a w by h image is an h by w
        /// one, so the bitmap's dimensions swap with it.
        /// </remarks>
        [Fact]
        public void Render_TransposeWithNonSquareBitmap_ProducesTheTransposedImage()
        {
            using Bitmap normal = TextureGraphEvaluator.Render(
                Single(new TextureNode { Type = 2 }), 8, 4, null);
            using Bitmap transposed = TextureGraphEvaluator.Render(
                Single(new TextureNode { Type = 2 }), 8, 4, null, transpose: true);

            Assert.Equal(4, transposed.Width);
            Assert.Equal(8, transposed.Height);

            for (int x = 0; x < 8; x++)
                for (int y = 0; y < 4; y++)
                    Assert.Equal(normal.GetPixel(x, y), transposed.GetPixel(y, x));
        }

        [Fact]
        public void Render_TransposeWithSquareBitmap_MirrorsAcrossTheDiagonal()
        {
            var normal = Render(Single(new TextureNode { Type = 2 }));                       // varies along x
            var transposed = Render(Single(new TextureNode { Type = 2 }), transpose: true);  // should vary along y

            for (int x = 0; x < Size; x++)
                for (int y = 0; y < Size; y++)
                    Assert.Equal(normal[x, y].R, transposed[y, x].R);
        }

        // ===================================================================
        //  Determinism
        // ===================================================================

        [Fact]
        public void Render_IsDeterministicAcrossRepeatedCalls()
        {
            var first = Render(Single(new TextureNode { Type = 12, IntParam0 = 4242 }));
            var second = Render(Single(new TextureNode { Type = 12, IntParam0 = 4242 }));

            for (int x = 0; x < Size; x++)
                for (int y = 0; y < Size; y++)
                    Assert.Equal(first[x, y], second[x, y]);
        }

        [Fact]
        public void Render_NoiseRespondsToItsSeed()
        {
            var a = Render(Single(new TextureNode { Type = 12, IntParam0 = 1 }));
            var b = Render(Single(new TextureNode { Type = 12, IntParam0 = 999 }));

            bool anyDifference = false;
            for (int x = 0; x < Size && !anyDifference; x++)
                for (int y = 0; y < Size && !anyDifference; y++)
                    if (a[x, y] != b[x, y]) anyDifference = true;

            Assert.True(anyDifference, "changing the noise seed produced an identical image");
        }

        [Theory]
        [InlineData(12)]   // hash noise
        [InlineData(13)]   // voronoi
        [InlineData(14)]   // sine wave
        [InlineData(15)]   // perlin
        public void Render_GeneratorNodes_StayWithinDisplayableRange(int type)
        {
            // The double-based generators are asserted structurally rather than exactly,
            // since their values depend on transcendental functions.
            var px = Render(Single(new TextureNode { Type = type, IntParam0 = 3, IntParam1 = 2, IntParam2 = 7 }));

            for (int x = 0; x < Size; x++)
                for (int y = 0; y < Size; y++)
                {
                    Assert.InRange(px[x, y].R, 0, 255);
                    bool black = px[x, y].R == 0 && px[x, y].G == 0 && px[x, y].B == 0;
                    Assert.Equal(black ? 0 : 255, px[x, y].A);
                }
        }

        // ===================================================================
        //  Sprite source nodes (types 18 and 39)
        // ===================================================================

        [Theory]
        [InlineData(18)]
        [InlineData(39)]
        public void Render_SpriteSourceWithNoSpriteLoaded_RendersMagentaPlaceholder(int type)
        {
            // SpriteId defaults to -1, so the cache is never consulted and no sprite loads.
            var px = Render(Single(new TextureNode { Type = type }));

            Assert.Equal(255, px[0, 0].R);
            Assert.Equal(0, px[0, 0].G);
            Assert.Equal(255, px[0, 0].B);
        }

        [Fact]
        public void Render_SpriteSourceWithSeededPixels_ResamplesNearestNeighbour()
        {
            // Seeding the internal sprite fields directly exercises the resample path with
            // no RSCache: a 2x2 sprite scaled up to 8x8 becomes four solid quadrants.
            var node = new TextureNode { Type = 39 };
            node.SpritePixels = new[]
            {
                unchecked((int) 0xFFFF0000),   // top-left  red
                unchecked((int) 0xFF00FF00),   // top-right green
                unchecked((int) 0xFF0000FF),   // bottom-left blue
                unchecked((int) 0xFFFFFFFF)    // bottom-right white
            };
            node.SpriteWidth = 2;
            node.SpriteHeight = 2;

            var px = Render(Single(node));

            Assert.Equal(255, px[0, 0].R); Assert.Equal(0, px[0, 0].G);      // red quadrant
            Assert.Equal(0, px[7, 0].R); Assert.Equal(255, px[7, 0].G);      // green quadrant
            Assert.Equal(0, px[0, 7].R); Assert.Equal(255, px[0, 7].B);      // blue quadrant
            Assert.Equal(255, px[7, 7].R); Assert.Equal(255, px[7, 7].G);    // white quadrant
        }

        [Fact]
        public void Render_SpriteSourceWithFullyTransparentPixel_RendersBlack()
        {
            var node = new TextureNode { Type = 39 };
            node.SpritePixels = new[] { 0x00FF0000 };   // alpha 0 over red
            node.SpriteWidth = 1;
            node.SpriteHeight = 1;

            var px = Render(Single(node));

            // Alpha zero zeroes all three channels, and method1631 leaves a pure-black
            // pixel transparent rather than opaque.
            Assert.Equal(0, px[0, 0].R);
            Assert.Equal(0, px[0, 0].G);
            Assert.Equal(0, px[0, 0].B);
            Assert.Equal(0, px[0, 0].A);
        }

        // ===================================================================
        //  Composition clone
        // ===================================================================

        /// <summary>
        ///     Cloning a graph must carry every field the decoder populated.
        /// </summary>
        /// <remarks>
        ///     Render works on a clone, so a decoded field that the clone drops is silently
        ///     absent at evaluation time and the node falls back to whatever its guard clause
        ///     does. That is exactly how the type 8 transfer curve came to be built correctly
        ///     and then never applied. Reflection rather than a field list, so a field added
        ///     later is covered without anyone remembering to update this.
        /// </remarks>
        [Fact]
        public void CloneForComposition_PreservesEveryPopulatedField()
        {
            var node = new TextureNode
            {
                Type = 8,
                MonoOverride = true,
                ChildIndices = new[] { 0 },
                IntParam0 = 1, IntParam1 = 2, IntParam2 = 3, IntParam3 = 4,
                IntParam4 = 5, IntParam5 = 6, IntParam6 = 7, IntParam7 = 8, IntParam8 = 9,
                BlendMode = 6,
                CurveData = new[] { 1 },
                GradientData = new[] { new[] { 0, 0 } },
                GradientPreset = 2,
                GradientCount = 1,
                SpriteId = 11,
                SpritePixels = new[] { 0xFFFFFF },
                SpriteWidth = 2,
                SpriteHeight = 2,
                ShapeIds = new[] { 1 },
                ShortData = new[] { 1 },
                NestedTextureId = 12,
                Permutation = new byte[512],
                Amplitudes = new[] { 1 },
                Frequencies = new[] { 1 },
                Jitter = new int[512],
                CurveLut = new int[257],
            };

            var clone = new TextureGraph { Nodes = new[] { node }, ColourOutputIndex = 0 }
                .CloneForComposition().Nodes[0];

            const BindingFlags Scope = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
            foreach (FieldInfo field in typeof(TextureNode).GetFields(Scope))
            {
                //Evaluation scratch is meant to be left behind; everything else must survive.
                if (field.Name is "MonoCache" or "ColourCache" or "MonoCachedRow" or "ColourCachedRow"
                    or "SampledMonoRows" or "SampledColourRows"
                    or "Width" or "Height" or "XCoord" or "YCoord"
                    or "Children" or "GradientColourLUT")
                    continue;

                object original = field.GetValue(node);
                object copied = field.GetValue(clone);
                Assert.True(Equals(original, copied),
                    $"TextureNode.{field.Name} was not carried across CloneForComposition " +
                    $"({original ?? "null"} became {copied ?? "null"}).");
            }
        }
    }
}
