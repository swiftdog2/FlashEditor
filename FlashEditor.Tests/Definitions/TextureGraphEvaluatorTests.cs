using FlashEditor.Definitions.Sprites;
using FlashEditor.Utils;
using System;
using System.Drawing;
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
        public void Render_AlphaIsAlwaysOpaque_EvenWithAnAlphaOutputNode_DocumentsKnownDefect()
        {
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
                IntParam0 = 4096,          // full factor: output is the raw blend result
                Children = new[] { a, b }
            };
            return Graph(0, blend, a, b);
        }

        [Theory]
        [InlineData(0, 0)]      // b
        [InlineData(1, 255)]    // a + b
        [InlineData(2, 255)]    // a - b
        [InlineData(3, 0)]      // a * b
        [InlineData(4, 255)]    // a / b, b == 0 short-circuits to FP_MAX
        [InlineData(5, 255)]    // screen
        [InlineData(6, 0)]      // min
        [InlineData(7, 255)]    // max
        [InlineData(8, 255)]    // |a - b|
        [InlineData(9, 255)]    // hard light on a
        [InlineData(10, 0)]     // hard light on b
        [InlineData(12, 0)]     // unknown mode falls back to b
        [InlineData(99, 0)]     // ditto
        public void Render_BlendMode_WhiteOverBlack_ProducesExpectedChannel(int mode, byte expected)
        {
            var px = Render(BlendGraph(mode, White, Black));
            Assert.Equal(expected, px[0, 0].R);
        }

        [Fact]
        public void Render_BlendMode11_Composite_LandsJustBelowFullScale()
        {
            // Mode 11 is the only mode whose white-over-black result is not an exact
            // endpoint (4064 of 4080), so it is asserted as a range rather than a constant.
            var px = Render(BlendGraph(11, White, Black));
            Assert.InRange(px[0, 0].R, 250, 255);
        }

        [Fact]
        public void Render_BlendMode_DivideByZero_ReturnsMaxRatherThanThrowing()
        {
            var ex = Record.Exception(() => Render(BlendGraph(4, White, Black)));
            Assert.Null(ex);
        }

        [Fact]
        public void Render_BlendFactorZero_YieldsTheFirstChildUnchanged()
        {
            var a = ConstantColour(White);
            var b = ConstantColour(Black);
            var blend = new TextureNode { Type = 7, BlendMode = 6, IntParam0 = 0, Children = new[] { a, b } };

            // output = va + Mul12(blended - va, 0) == va
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
        //  Known evaluator defects
        // ===================================================================

        /// <summary>
        /// DEFECT: type 24 is absent from the colour-node list, so it is classified mono and
        /// dispatched through EvalMono, which has no case for it. It always renders flat
        /// mid-grey and EvalMergeRGB is dead code that can never run.
        /// </summary>
        [Fact]
        public void Render_Type24MergeRgb_IsUnreachableAndRendersMidGrey_DocumentsKnownDefect()
        {
            var child = ConstantColour(0xFF0000);
            var merge = new TextureNode { Type = 24, Children = new[] { child } };

            var px = Render(Graph(0, merge, child));

            Assert.Equal(MidGrey, px[0, 0].R);
            Assert.Equal(MidGrey, px[0, 0].G);
            Assert.Equal(MidGrey, px[0, 0].B);
        }

        /// <summary>
        /// DEFECT: types 21, 25, 30 and 33 are classified as colour nodes but have no colour
        /// implementation, so EvalColour routes them to the passthrough default. Their own
        /// operation is silently discarded and the child's colour is copied through unchanged.
        /// </summary>
        [Theory]
        [InlineData(21)]   // Emboss
        [InlineData(25)]   // Curve remap
        [InlineData(30)]   // Edge detect
        [InlineData(33)]   // Offset / scroll
        public void Render_ColourTypesWithoutColourImplementation_PassChildThrough_DocumentsKnownDefect(int type)
        {
            // A horizontal gradient child gives a spatially varying signal, so a genuine
            // emboss/offset/edge-detect would visibly change it.
            var childForOp = new TextureNode { Type = 2 };
            var op = new TextureNode
            {
                Type = type,
                IntParam0 = 1000,   // a large parameter that a real implementation would act on
                IntParam1 = 1000,
                Children = new[] { childForOp }
            };

            var actual = Render(Graph(0, op, childForOp));
            var childAlone = Render(Single(new TextureNode { Type = 2 }));

            for (int x = 0; x < Size; x++)
                for (int y = 0; y < Size; y++)
                    Assert.Equal(childAlone[x, y].R, actual[x, y].R);
        }

        /// <summary>
        /// DEFECT: the transpose branch indexes with `x * width + y` where it should use
        /// `x * height + y`, so any non-square transposed render walks off the end of the
        /// pixel buffer. Production only ever calls this 128x128, which is why it survives.
        /// </summary>
        [Fact]
        public void Render_TransposeWithNonSquareBitmap_Throws_DocumentsKnownDefect()
        {
            Assert.Throws<IndexOutOfRangeException>(() =>
                TextureGraphEvaluator.Render(Single(Constant(2048)), 8, 4, null, transpose: true));
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
                    Assert.Equal(255, px[x, y].A);
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

            // Alpha zero zeroes all three channels, but the bitmap alpha stays opaque.
            Assert.Equal(0, px[0, 0].R);
            Assert.Equal(0, px[0, 0].G);
            Assert.Equal(0, px[0, 0].B);
            Assert.Equal(255, px[0, 0].A);
        }
    }
}
