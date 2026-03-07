using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using FlashEditor.cache;
using FlashEditor.cache.sprites;
using FlashEditor.cache.util;
using static FlashEditor.Utils.DebugUtil;

namespace FlashEditor.Definitions.Sprites {

    /// <summary>
    /// Parsed texture graph ready for evaluation.
    /// </summary>
    public class TextureGraph {
        public TextureNode[] Nodes;
        public int ColourOutputIndex;
        public int AlphaOutputIndex;
        public int BrightnessOutputIndex;
    }

    /// <summary>
    /// A single node in the texture graph.
    /// </summary>
    public class TextureNode {
        public int Type;
        public bool IsMonochrome;
        public int[] ChildIndices;
        public TextureNode[] Children;

        // Per-node config fields (populated during decode based on type)
        public int IntParam0, IntParam1, IntParam2, IntParam3, IntParam4, IntParam5;
        public int IntParam6, IntParam7, IntParam8;
        public int BlendMode;
        public int[] CurveData;
        public int[][] GradientData; // [count][4] = {position, r, g, b}
        public int GradientPreset;
        public int GradientCount;
        public int SpriteId = -1;

        // Runtime buffers
        internal int[] MonoCache;
        internal int[][] ColourCache; // [3][width] for RGB
        internal int CachedRow = -1;
        internal bool CachedIsMono;

        // Dimensions set during allocation
        internal int Width, Height;
        internal int[] XCoord, YCoord;

        // Sprite pixel data for types 18 & 39
        internal int[] SpritePixels;
        internal int SpriteWidth, SpriteHeight;

        public void Allocate(int w, int h, int[] xCoord, int[] yCoord) {
            Width = w;
            Height = h;
            XCoord = xCoord;
            YCoord = yCoord;
            MonoCache = new int[w];
            ColourCache = new int[3][];
            ColourCache[0] = new int[w];
            ColourCache[1] = new int[w];
            ColourCache[2] = new int[w];
            CachedRow = -1;
        }

        public void Release() {
            MonoCache = null;
            ColourCache = null;
            CachedRow = -1;
        }
    }

    /// <summary>
    /// Evaluates a parsed procedural texture graph to produce an ARGB bitmap.
    /// Matches the client's method1631 pipeline using 12-bit fixed-point arithmetic.
    /// </summary>
    public static class TextureGraphEvaluator {
        private const int FP_ONE = 4096;
        private const int FP_MAX = 4080; // 255/256 * 4096

        public static Bitmap Render(TextureGraph graph, int width, int height, RSCache cache) {
            if (graph == null || graph.Nodes == null || graph.Nodes.Length == 0)
                return null;

            // Build coordinate LUTs
            int[] xCoord = new int[width];
            int[] yCoord = new int[height];
            for (int i = 0; i < width; i++)
                xCoord[i] = (i << 12) / width;
            for (int i = 0; i < height; i++)
                yCoord[i] = (i << 12) / height;

            // Allocate node buffers and load sprites
            foreach (var node in graph.Nodes) {
                if (node == null) continue;
                node.Allocate(width, height, xCoord, yCoord);
                if ((node.Type == 18 || node.Type == 39) && node.SpriteId >= 0)
                    LoadSpriteForNode(node, cache);
            }

            // Evaluate row-by-row
            int colourIdx = graph.ColourOutputIndex;
            if (colourIdx < 0 || colourIdx >= graph.Nodes.Length || graph.Nodes[colourIdx] == null)
                return null;

            var pixels = new int[width * height];
            var colourNode = graph.Nodes[colourIdx];
            bool outputIsMono = IsMonochrome(colourNode);

            for (int y = 0; y < height; y++) {
                if (outputIsMono) {
                    int[] mono = GetMono(colourNode, y);
                    for (int x = 0; x < width; x++) {
                        int v = Clamp12(mono[x]) >> 4;
                        pixels[y * width + x] = unchecked((int)0xFF000000) | (v << 16) | (v << 8) | v;
                    }
                } else {
                    int[][] rgb = GetColour(colourNode, y);
                    for (int x = 0; x < width; x++) {
                        int r = Clamp12(rgb[0][x]) >> 4;
                        int g = Clamp12(rgb[1][x]) >> 4;
                        int b = Clamp12(rgb[2][x]) >> 4;
                        pixels[y * width + x] = unchecked((int)0xFF000000) | (r << 16) | (g << 8) | b;
                    }
                }
            }

            // Release node buffers
            foreach (var node in graph.Nodes)
                node?.Release();

            // Build bitmap using LockBits for safe pixel copy
            var bmp = new Bitmap(width, height, PixelFormat.Format32bppArgb);
            var data = bmp.LockBits(new Rectangle(0, 0, width, height),
                ImageLockMode.WriteOnly, PixelFormat.Format32bppArgb);
            Marshal.Copy(pixels, 0, data.Scan0, pixels.Length);
            bmp.UnlockBits(data);
            return bmp;
        }

        private static void LoadSpriteForNode(TextureNode node, RSCache cache) {
            if (cache == null || node.SpriteId < 0) return;
            try {
                SpriteDefinition sprite = cache.GetSprite(node.SpriteId);
                if (sprite != null && sprite.GetFrameCount() > 0) {
                    var frame = sprite.GetFrame(0);
                    if (frame != null) {
                        node.SpritePixels = frame.GetPixels();
                        node.SpriteWidth = frame.GetWidth();
                        node.SpriteHeight = frame.GetHeight();
                    }
                }
            } catch (Exception ex) {
                Debug($"TextureGraphEvaluator: failed to load sprite {node.SpriteId}: {ex.Message}", LOG_DETAIL.ADVANCED);
            }
        }

        private static bool IsMonochrome(TextureNode node) {
            switch (node.Type) {
                case 1: case 7: case 8: case 11: case 18: case 24: case 39:
                    return false;
                default:
                    return true;
            }
        }

        // Get mono output from a node, with auto-conversion from colour if needed
        private static int[] GetMono(TextureNode node, int row) {
            if (node.CachedRow == row && node.CachedIsMono)
                return node.MonoCache;

            if (!IsMonochrome(node)) {
                // Colour node → take red channel as mono
                int[][] rgb = GetColour(node, row);
                Array.Copy(rgb[0], node.MonoCache, node.Width);
                node.CachedRow = row;
                node.CachedIsMono = true;
                return node.MonoCache;
            }

            EvalMono(node, row);
            node.CachedRow = row;
            node.CachedIsMono = true;
            return node.MonoCache;
        }

        // Get colour output from a node, with auto-promotion from mono if needed
        private static int[][] GetColour(TextureNode node, int row) {
            if (node.CachedRow == row && !node.CachedIsMono)
                return node.ColourCache;

            if (IsMonochrome(node)) {
                // Mono node → duplicate to all 3 channels
                int[] mono = GetMono(node, row);
                Array.Copy(mono, node.ColourCache[0], node.Width);
                Array.Copy(mono, node.ColourCache[1], node.Width);
                Array.Copy(mono, node.ColourCache[2], node.Width);
                node.CachedRow = row;
                node.CachedIsMono = false;
                return node.ColourCache;
            }

            EvalColour(node, row);
            node.CachedRow = row;
            node.CachedIsMono = false;
            return node.ColourCache;
        }

        private static int Clamp12(int v) {
            if (v < 0) return 0;
            if (v > 4080) return 4080;
            return v;
        }

        private static int Mul12(int a, int b) => (a * b) >> 12;

        // ===================================================================
        //  MONO NODE EVALUATION
        // ===================================================================
        private static void EvalMono(TextureNode node, int row) {
            int w = node.Width;
            int[] output = node.MonoCache;

            switch (node.Type) {
                case 0: EvalConstant(node, output, w); break;
                case 2: EvalHorizontalGrad(node, output, w); break;
                case 3: EvalVerticalGrad(node, output, w, row); break;
                case 4: EvalBrick(node, output, w, row); break;
                case 5: EvalBrightness(node, output, w, row); break;
                case 6: EvalBlend(node, output, w, row); break;
                case 9: EvalInvert(node, output, w, row); break;
                case 10: EvalGradientRemap(node, output, w, row); break;
                case 12: EvalNoise(node, output, w, row); break;
                case 13: EvalVoronoi(node, output, w, row); break;
                case 14: EvalSineWave(node, output, w, row); break;
                case 15: EvalPerlin(node, output, w, row); break;
                case 16: EvalThreshold(node, output, w, row); break;
                case 17: EvalBlur(node, output, w, row); break;
                case 19: EvalWeave(node, output, w, row); break;
                case 20: EvalClamp(node, output, w, row); break;
                case 21: EvalEmboss(node, output, w, row); break;
                case 22: EvalFlipH(node, output, w, row); break;
                case 23: EvalFlipV(node, output, w, row); break;
                case 25: EvalCurveRemap(node, output, w, row); break;
                case 26: EvalTurbulence(node, output, w, row); break;
                case 27: EvalLines(node, output, w, row); break;
                case 28: EvalMandelbrot(node, output, w, row); break;
                case 29: EvalFactory(node, output, w); break;
                case 30: EvalEdgeDetect(node, output, w, row); break;
                case 31: EvalSquare(node, output, w, row); break;
                case 32: EvalPolarWarp(node, output, w, row); break;
                case 33: EvalOffset(node, output, w, row); break;
                case 34: EvalCurveRemap2(node, output, w, row); break;
                case 35: EvalScale(node, output, w, row); break;
                case 36: EvalCheckerboard(node, output, w, row); break;
                case 37: EvalAbsMirror(node, output, w, row); break;
                case 38: EvalTileWrap(node, output, w, row); break;
                default:
                    // Unknown mono node — mid-grey
                    Array.Fill(output, 2040, 0, w);
                    break;
            }
        }

        // ===================================================================
        //  COLOUR NODE EVALUATION
        // ===================================================================
        private static void EvalColour(TextureNode node, int row) {
            int w = node.Width;
            int[][] output = node.ColourCache;

            switch (node.Type) {
                case 1: EvalConstantColour(node, output, w); break;
                case 7: EvalColourBlend(node, output, w, row); break;
                case 8: EvalColourRamp(node, output, w, row); break;
                case 11: EvalHSLAdjust(node, output, w, row); break;
                case 18: // falls through to 39
                case 39: EvalSpriteSource(node, output, w, row); break;
                case 24: EvalMergeRGB(node, output, w, row); break;
                default:
                    Array.Fill(output[0], 2040, 0, w);
                    Array.Fill(output[1], 2040, 0, w);
                    Array.Fill(output[2], 2040, 0, w);
                    break;
            }
        }

        // ===================================================================
        //  TYPE 0: Constant
        // ===================================================================
        private static void EvalConstant(TextureNode node, int[] output, int w) {
            int val = node.IntParam0; // already 12-bit range [0..4080]
            Array.Fill(output, val, 0, w);
        }

        // ===================================================================
        //  TYPE 1: Constant Colour
        // ===================================================================
        private static void EvalConstantColour(TextureNode node, int[][] output, int w) {
            // IntParam0 = packed RGB from readMedium
            int rgb = node.IntParam0;
            int r = ((rgb >> 16) & 0xFF) << 4; // expand 8-bit to 12-bit
            int g = ((rgb >> 8) & 0xFF) << 4;
            int b = (rgb & 0xFF) << 4;
            Array.Fill(output[0], r, 0, w);
            Array.Fill(output[1], g, 0, w);
            Array.Fill(output[2], b, 0, w);
        }

        // ===================================================================
        //  TYPE 2: Horizontal Gradient
        // ===================================================================
        private static void EvalHorizontalGrad(TextureNode node, int[] output, int w) {
            for (int x = 0; x < w; x++)
                output[x] = node.XCoord[x];
        }

        // ===================================================================
        //  TYPE 3: Vertical Gradient
        // ===================================================================
        private static void EvalVerticalGrad(TextureNode node, int[] output, int w, int row) {
            int val = node.YCoord[row];
            Array.Fill(output, val, 0, w);
        }

        // ===================================================================
        //  TYPE 4: Brick Pattern
        // ===================================================================
        private static void EvalBrick(TextureNode node, int[] output, int w, int row) {
            int brickW = Math.Max(1, node.IntParam2);
            int brickH = Math.Max(1, node.IntParam3);
            int mortarW = node.IntParam4;
            int mortarH = node.IntParam5;
            int totalW = brickW + mortarW;
            int totalH = brickH + mortarH;

            int y = (row * node.Height) >> 0; // use raw row
            int yInTile = ((y % totalH) + totalH) % totalH;
            bool yInMortar = yInTile >= brickH;

            // Offset every other row
            int rowIndex = y / totalH;
            int xOffset = (rowIndex & 1) == 1 ? (totalW >> 1) : 0;

            for (int x = 0; x < w; x++) {
                if (yInMortar) {
                    output[x] = node.IntParam1 == 0 ? 0 : FP_MAX;
                } else {
                    int xp = ((x + xOffset) % totalW + totalW) % totalW;
                    bool xInMortar = xp >= brickW;
                    output[x] = xInMortar ? (node.IntParam1 == 0 ? 0 : FP_MAX) :
                                           (node.IntParam0 == 0 ? 0 : FP_MAX);
                }
            }
        }

        // ===================================================================
        //  TYPE 5: Brightness
        // ===================================================================
        private static void EvalBrightness(TextureNode node, int[] output, int w, int row) {
            if (node.Children == null || node.Children.Length < 1 || node.Children[0] == null) {
                Array.Fill(output, 2040, 0, w);
                return;
            }
            int[] child = GetMono(node.Children[0], row);
            int factor = node.IntParam0; // 0-255 byte mapped to 12-bit
            for (int x = 0; x < w; x++)
                output[x] = Mul12(child[x], factor);
        }

        // ===================================================================
        //  TYPE 6: Blend (mono)
        // ===================================================================
        private static void EvalBlend(TextureNode node, int[] output, int w, int row) {
            if (node.Children == null || node.Children.Length < 2) {
                Array.Fill(output, 2040, 0, w);
                return;
            }
            int[] a = GetMono(node.Children[0], row);
            int[] b = GetMono(node.Children[1], row);
            int factor = node.IntParam0;  // blend factor 0-4096
            int mode = node.BlendMode;

            for (int x = 0; x < w; x++) {
                int va = a[x], vb = b[x];
                int blended = BlendOp(va, vb, mode);
                // lerp between a and blended by factor
                output[x] = va + (Mul12(blended - va, factor));
            }
        }

        private static int BlendOp(int a, int b, int mode) {
            switch (mode) {
                case 0: return b; // normal
                case 1: return a + b; // add
                case 2: return a - b; // subtract
                case 3: return Mul12(a, b); // multiply
                case 4: return b == 0 ? FP_MAX : (a << 12) / b; // divide
                case 5: return a + b - Mul12(a, b); // screen
                case 6: return Math.Min(a, b); // min/darken
                case 7: return Math.Max(a, b); // max/lighten
                case 8: return Math.Abs(a - b); // difference
                case 9: // overlay
                    return a < 2048 ? Mul12(2 * a, b) : FP_MAX - Mul12(2 * (FP_MAX - a), FP_MAX - b);
                case 10: // hard light
                    return b < 2048 ? Mul12(2 * a, b) : FP_MAX - Mul12(2 * (FP_MAX - a), FP_MAX - b);
                case 11: // soft light
                    int t = Mul12(a, b);
                    return t + Mul12(a, FP_MAX - Mul12(FP_MAX - a, FP_MAX - b) - t);
                default: return b;
            }
        }

        // ===================================================================
        //  TYPE 7: Colour Blend
        // ===================================================================
        private static void EvalColourBlend(TextureNode node, int[][] output, int w, int row) {
            if (node.Children == null || node.Children.Length < 2) {
                Array.Fill(output[0], 2040, 0, w);
                Array.Fill(output[1], 2040, 0, w);
                Array.Fill(output[2], 2040, 0, w);
                return;
            }
            int[][] a = GetColour(node.Children[0], row);
            int[][] b = GetColour(node.Children[1], row);
            int factor = node.IntParam0;
            int mode = node.BlendMode;

            for (int ch = 0; ch < 3; ch++) {
                for (int x = 0; x < w; x++) {
                    int va = a[ch][x], vb = b[ch][x];
                    int blended = BlendOp(va, vb, mode);
                    output[ch][x] = va + Mul12(blended - va, factor);
                }
            }
        }

        // ===================================================================
        //  TYPE 8: Colour Ramp
        // ===================================================================
        private static void EvalColourRamp(TextureNode node, int[][] output, int w, int row) {
            if (node.Children == null || node.Children.Length < 1 || node.Children[0] == null) {
                Array.Fill(output[0], 2040, 0, w);
                Array.Fill(output[1], 2040, 0, w);
                Array.Fill(output[2], 2040, 0, w);
                return;
            }
            int[] child = GetMono(node.Children[0], row);
            int[] rampR, rampG, rampB;
            BuildColourRampLUT(node, out rampR, out rampG, out rampB);

            for (int x = 0; x < w; x++) {
                int idx = Clamp12(child[x]) >> 4; // 0-255
                output[0][x] = rampR[idx];
                output[1][x] = rampG[idx];
                output[2][x] = rampB[idx];
            }
        }

        private static void BuildColourRampLUT(TextureNode node, out int[] rampR, out int[] rampG, out int[] rampB) {
            rampR = new int[256];
            rampG = new int[256];
            rampB = new int[256];

            if (node.GradientData == null || node.GradientData.Length == 0) {
                // Default: greyscale ramp
                for (int i = 0; i < 256; i++) {
                    rampR[i] = i << 4;
                    rampG[i] = i << 4;
                    rampB[i] = i << 4;
                }
                return;
            }

            // Sort gradient stops by position
            var stops = node.GradientData;
            // Interpolate between stops
            int prevPos = 0, prevR = 0, prevG = 0, prevB = 0;
            int stopIdx = 0;
            int nextPos = stops[0][0] >> 4; // convert from 12-bit to 8-bit
            int nextR = stops[0][1] << 4, nextG = stops[0][2] << 4, nextB = stops[0][3] << 4;

            for (int i = 0; i < 256; i++) {
                while (stopIdx < stops.Length - 1 && i >= nextPos) {
                    prevPos = nextPos;
                    prevR = nextR;
                    prevG = nextG;
                    prevB = nextB;
                    stopIdx++;
                    nextPos = stops[stopIdx][0] >> 4;
                    nextR = stops[stopIdx][1] << 4;
                    nextG = stops[stopIdx][2] << 4;
                    nextB = stops[stopIdx][3] << 4;
                }
                int range = nextPos - prevPos;
                if (range <= 0) {
                    rampR[i] = nextR;
                    rampG[i] = nextG;
                    rampB[i] = nextB;
                } else {
                    int t = ((i - prevPos) << 12) / range;
                    rampR[i] = prevR + Mul12(nextR - prevR, t);
                    rampG[i] = prevG + Mul12(nextG - prevG, t);
                    rampB[i] = prevB + Mul12(nextB - prevB, t);
                }
            }
        }

        // ===================================================================
        //  TYPE 9: Invert
        // ===================================================================
        private static void EvalInvert(TextureNode node, int[] output, int w, int row) {
            if (node.Children == null || node.Children.Length < 1 || node.Children[0] == null) {
                Array.Fill(output, 2040, 0, w);
                return;
            }
            int[] child = GetMono(node.Children[0], row);
            for (int x = 0; x < w; x++)
                output[x] = FP_MAX - child[x];
        }

        // ===================================================================
        //  TYPE 10: Gradient/Transfer Curve Remap
        // ===================================================================
        private static void EvalGradientRemap(TextureNode node, int[] output, int w, int row) {
            if (node.Children == null || node.Children.Length < 1 || node.Children[0] == null) {
                Array.Fill(output, 2040, 0, w);
                return;
            }
            int[] child = GetMono(node.Children[0], row);

            if (node.GradientPreset != 0) {
                // Preset gradient curves
                for (int x = 0; x < w; x++)
                    output[x] = ApplyPresetCurve(child[x], node.GradientPreset);
            } else if (node.GradientData != null) {
                // Custom gradient points
                int[] lut = BuildGradientLUT(node);
                for (int x = 0; x < w; x++) {
                    int idx = Clamp12(child[x]) >> 4;
                    output[x] = lut[idx];
                }
            } else {
                Array.Copy(child, output, w);
            }
        }

        private static int ApplyPresetCurve(int val, int preset) {
            switch (preset) {
                case 1: // linear (identity)
                    return val;
                case 2: // square
                    return Mul12(val, val);
                case 3: // sqrt
                    return (int)(Math.Sqrt(val / (double)FP_ONE) * FP_ONE);
                case 4: // sine
                    return (int)(Math.Sin(val * Math.PI / (2.0 * FP_ONE)) * FP_ONE);
                case 5: // cosine
                    return FP_ONE - (int)(Math.Cos(val * Math.PI / (2.0 * FP_ONE)) * FP_ONE);
                default: return val;
            }
        }

        private static int[] BuildGradientLUT(TextureNode node) {
            int[] lut = new int[256];
            if (node.GradientData == null || node.GradientData.Length == 0) {
                for (int i = 0; i < 256; i++)
                    lut[i] = i << 4;
                return lut;
            }

            var stops = node.GradientData;
            int prevPos = 0, prevVal = 0;
            int stopIdx = 0;
            int nextPos = stops[0][0] >> 4;
            // For mono gradient, use channel 1 (R) as value
            int nextVal = stops[0][1] << 4;

            for (int i = 0; i < 256; i++) {
                while (stopIdx < stops.Length - 1 && i >= nextPos) {
                    prevPos = nextPos;
                    prevVal = nextVal;
                    stopIdx++;
                    nextPos = stops[stopIdx][0] >> 4;
                    nextVal = stops[stopIdx][1] << 4;
                }
                int range = nextPos - prevPos;
                if (range <= 0)
                    lut[i] = nextVal;
                else {
                    int t = ((i - prevPos) << 12) / range;
                    lut[i] = prevVal + Mul12(nextVal - prevVal, t);
                }
            }
            return lut;
        }

        // ===================================================================
        //  TYPE 11: HSL Adjust
        // ===================================================================
        private static void EvalHSLAdjust(TextureNode node, int[][] output, int w, int row) {
            if (node.Children == null || node.Children.Length < 1 || node.Children[0] == null) {
                Array.Fill(output[0], 2040, 0, w);
                Array.Fill(output[1], 2040, 0, w);
                Array.Fill(output[2], 2040, 0, w);
                return;
            }
            int[][] child = GetColour(node.Children[0], row);
            int hShift = node.IntParam0;
            int sShift = node.IntParam1;
            int lShift = node.IntParam2;

            for (int x = 0; x < w; x++) {
                int r = child[0][x], g = child[1][x], b = child[2][x];
                RGBtoHSL(r, g, b, out int h, out int s, out int l);
                h = (h + hShift) & 0xFFF;
                s = Clamp12(s + sShift);
                l = Clamp12(l + lShift);
                HSLtoRGB(h, s, l, out int or, out int og, out int ob);
                output[0][x] = or;
                output[1][x] = og;
                output[2][x] = ob;
            }
        }

        private static void RGBtoHSL(int r, int g, int b, out int h, out int s, out int l) {
            int max = Math.Max(r, Math.Max(g, b));
            int min = Math.Min(r, Math.Min(g, b));
            l = (max + min) >> 1;
            if (max == min) { h = 0; s = 0; return; }
            int d = max - min;
            s = l > 2048 ? (d << 12) / (2 * FP_ONE - max - min) : (d << 12) / (max + min);
            if (max == r) h = ((g - b) << 12) / d + (g < b ? 6 * FP_ONE : 0);
            else if (max == g) h = ((b - r) << 12) / d + 2 * FP_ONE;
            else h = ((r - g) << 12) / d + 4 * FP_ONE;
            h /= 6;
        }

        private static void HSLtoRGB(int h, int s, int l, out int r, out int g, out int b) {
            if (s == 0) { r = g = b = l; return; }
            int q = l < 2048 ? Mul12(l, FP_ONE + s) : l + s - Mul12(l, s);
            int p = 2 * l - q;
            r = HueToRGB(p, q, h + FP_ONE / 3);
            g = HueToRGB(p, q, h);
            b = HueToRGB(p, q, h - FP_ONE / 3);
        }

        private static int HueToRGB(int p, int q, int t) {
            if (t < 0) t += FP_ONE;
            if (t > FP_ONE) t -= FP_ONE;
            if (t < FP_ONE / 6) return p + Mul12(q - p, 6 * t);
            if (t < FP_ONE / 2) return q;
            if (t < FP_ONE * 2 / 3) return p + Mul12(q - p, (FP_ONE * 2 / 3 - t) * 6);
            return p;
        }

        // ===================================================================
        //  TYPE 12: Noise
        // ===================================================================
        private static void EvalNoise(TextureNode node, int[] output, int w, int row) {
            int seed = node.IntParam0;
            for (int x = 0; x < w; x++) {
                int hash = HashNoise(x, row, seed);
                output[x] = ((hash & 0xFFF) * FP_MAX) >> 12;
            }
        }

        private static int HashNoise(int x, int y, int seed) {
            int n = x + y * 57 + seed * 131;
            n = (n << 13) ^ n;
            return (n * (n * n * 15731 + 789221) + 1376312589) & 0x7FFFFFFF;
        }

        // ===================================================================
        //  TYPE 13: Voronoi
        // ===================================================================
        private static void EvalVoronoi(TextureNode node, int[] output, int w, int row) {
            int seed = node.IntParam0;
            int cellSize = Math.Max(1, w / 8);
            int fy = (node.YCoord[row] * 8) >> 12;
            for (int x = 0; x < w; x++) {
                int fx = (node.XCoord[x] * 8) >> 12;
                int minDist = int.MaxValue;
                for (int dy = -1; dy <= 1; dy++) {
                    for (int dx = -1; dx <= 1; dx++) {
                        int cx = fx + dx, cy = fy + dy;
                        int h = HashNoise(cx, cy, seed);
                        int px = cx * cellSize + (h % cellSize);
                        int py = cy * cellSize + ((h >> 8) % cellSize);
                        int wx = x - px, wy = row - py;
                        int dist = wx * wx + wy * wy;
                        if (dist < minDist) minDist = dist;
                    }
                }
                int v = (int)(Math.Sqrt(minDist) * FP_ONE / cellSize);
                output[x] = Clamp12(v);
            }
        }

        // ===================================================================
        //  TYPE 14: Sine Wave
        // ===================================================================
        private static void EvalSineWave(TextureNode node, int[] output, int w, int row) {
            int freq = Math.Max(1, node.IntParam0);
            for (int x = 0; x < w; x++) {
                double t = node.XCoord[x] * freq / (double)FP_ONE;
                output[x] = (int)((Math.Sin(t * 2.0 * Math.PI) * 0.5 + 0.5) * FP_MAX);
            }
        }

        // ===================================================================
        //  TYPE 15: Perlin Noise
        // ===================================================================
        private static void EvalPerlin(TextureNode node, int[] output, int w, int row) {
            int octaves = Math.Max(1, Math.Min(8, node.IntParam0));
            int freq = Math.Max(1, node.IntParam1);
            int seed = node.IntParam2;

            for (int x = 0; x < w; x++) {
                double nx = node.XCoord[x] * freq / (double)FP_ONE;
                double ny = node.YCoord[row] * freq / (double)FP_ONE;
                double val = 0, amp = 1.0, maxAmp = 0;
                double f = 1.0;
                for (int o = 0; o < octaves; o++) {
                    val += PerlinSample(nx * f + seed, ny * f + seed) * amp;
                    maxAmp += amp;
                    amp *= 0.5;
                    f *= 2.0;
                }
                val = val / maxAmp * 0.5 + 0.5;
                output[x] = Clamp12((int)(val * FP_MAX));
            }
        }

        private static double PerlinSample(double x, double y) {
            int xi = (int)Math.Floor(x), yi = (int)Math.Floor(y);
            double xf = x - xi, yf = y - yi;
            double u = Fade(xf), v = Fade(yf);
            int aa = PerlinHash(xi, yi), ab = PerlinHash(xi, yi + 1);
            int ba = PerlinHash(xi + 1, yi), bb = PerlinHash(xi + 1, yi + 1);
            double x1 = Lerp(PerlinGrad(aa, xf, yf), PerlinGrad(ba, xf - 1, yf), u);
            double x2 = Lerp(PerlinGrad(ab, xf, yf - 1), PerlinGrad(bb, xf - 1, yf - 1), u);
            return Lerp(x1, x2, v);
        }

        private static double Fade(double t) => t * t * t * (t * (t * 6 - 15) + 10);
        private static double Lerp(double a, double b, double t) => a + t * (b - a);
        private static int PerlinHash(int x, int y) {
            int n = x + y * 57;
            n = (n << 13) ^ n;
            return (n * (n * n * 15731 + 789221) + 1376312589) & 0xFF;
        }
        private static double PerlinGrad(int hash, double x, double y) {
            switch (hash & 3) {
                case 0: return x + y;
                case 1: return -x + y;
                case 2: return x - y;
                default: return -x - y;
            }
        }

        // ===================================================================
        //  TYPE 16: Threshold
        // ===================================================================
        private static void EvalThreshold(TextureNode node, int[] output, int w, int row) {
            int thresh = node.IntParam0;
            int below = node.IntParam1;
            int above = node.IntParam2;
            for (int x = 0; x < w; x++)
                output[x] = node.XCoord[x] < thresh ? below : above;
        }

        // ===================================================================
        //  TYPE 17: Blur
        // ===================================================================
        private static void EvalBlur(TextureNode node, int[] output, int w, int row) {
            if (node.Children == null || node.Children.Length < 1 || node.Children[0] == null) {
                Array.Fill(output, 2040, 0, w);
                return;
            }
            int radius = Math.Max(0, node.IntParam0);
            if (radius == 0) {
                int[] child = GetMono(node.Children[0], row);
                Array.Copy(child, output, w);
                return;
            }

            // Vertical blur: average rows [row-radius..row+radius]
            int count = 0;
            int[] sum = new int[w];
            for (int dy = -radius; dy <= radius; dy++) {
                int sy = row + dy;
                if (sy < 0) sy = 0;
                if (sy >= node.Height) sy = node.Height - 1;
                int[] childRow = GetMono(node.Children[0], sy);
                count++;
                for (int x = 0; x < w; x++)
                    sum[x] += childRow[x];
            }

            // Horizontal blur
            for (int x = 0; x < w; x++) {
                int s = 0, c = 0;
                for (int dx = -radius; dx <= radius; dx++) {
                    int sx = x + dx;
                    if (sx < 0) sx = 0;
                    if (sx >= w) sx = w - 1;
                    s += sum[sx];
                    c++;
                }
                output[x] = s / (c * count);
            }
        }

        // ===================================================================
        //  TYPE 18 & 39: Sprite Source
        // ===================================================================
        private static void EvalSpriteSource(TextureNode node, int[][] output, int w, int row) {
            if (node.SpritePixels == null || node.SpriteWidth <= 0 || node.SpriteHeight <= 0) {
                // No sprite loaded — magenta to make it obvious
                Array.Fill(output[0], FP_MAX, 0, w);
                Array.Fill(output[1], 0, 0, w);
                Array.Fill(output[2], FP_MAX, 0, w);
                return;
            }

            int sy = (row * node.SpriteHeight) / node.Height;
            if (sy >= node.SpriteHeight) sy = node.SpriteHeight - 1;

            for (int x = 0; x < w; x++) {
                int sx = (x * node.SpriteWidth) / w;
                if (sx >= node.SpriteWidth) sx = node.SpriteWidth - 1;
                int argb = node.SpritePixels[sy * node.SpriteWidth + sx];
                int a = (argb >> 24) & 0xFF;
                if (a == 0) {
                    output[0][x] = 0;
                    output[1][x] = 0;
                    output[2][x] = 0;
                } else {
                    output[0][x] = ((argb >> 16) & 0xFF) << 4;
                    output[1][x] = ((argb >> 8) & 0xFF) << 4;
                    output[2][x] = (argb & 0xFF) << 4;
                }
            }
        }

        // ===================================================================
        //  TYPE 19: Weave
        // ===================================================================
        private static void EvalWeave(TextureNode node, int[] output, int w, int row) {
            if (node.Children == null || node.Children.Length < 3) {
                Array.Fill(output, 2040, 0, w);
                return;
            }
            int[] warp = GetMono(node.Children[0], row);
            int[] weft = GetMono(node.Children[1], row);
            int[] mask = GetMono(node.Children[2], row);

            int freq = Math.Max(1, node.IntParam0);
            for (int x = 0; x < w; x++) {
                int xp = (node.XCoord[x] * freq) >> 12;
                int yp = (node.YCoord[row] * freq) >> 12;
                output[x] = ((xp + yp) & 1) == 0 ? Mul12(warp[x], mask[x]) >> 12 * 0 + warp[x]
                    : Mul12(weft[x], mask[x]) >> 12 * 0 + weft[x];
                // Simplified: alternate warp/weft based on position
                output[x] = ((xp + yp) & 1) == 0 ? warp[x] : weft[x];
            }
        }

        // ===================================================================
        //  TYPE 20: Clamp
        // ===================================================================
        private static void EvalClamp(TextureNode node, int[] output, int w, int row) {
            if (node.Children == null || node.Children.Length < 1 || node.Children[0] == null) {
                Array.Fill(output, 2040, 0, w);
                return;
            }
            int[] child = GetMono(node.Children[0], row);
            int lo = node.IntParam0, hi = node.IntParam1;
            if (hi < lo) { int t = lo; lo = hi; hi = t; }
            for (int x = 0; x < w; x++) {
                int v = child[x];
                output[x] = v < lo ? lo : v > hi ? hi : v;
            }
        }

        // ===================================================================
        //  TYPE 21: Emboss
        // ===================================================================
        private static void EvalEmboss(TextureNode node, int[] output, int w, int row) {
            if (node.Children == null || node.Children.Length < 3) {
                Array.Fill(output, 2040, 0, w);
                return;
            }
            int[] height0 = GetMono(node.Children[0], row);
            int[] heightUp = GetMono(node.Children[0], Math.Max(0, row - 1));
            int[] light = GetMono(node.Children[1], row);
            int[] ambient = GetMono(node.Children[2], row);

            int strength = Math.Max(1, node.IntParam0);
            for (int x = 0; x < w; x++) {
                int dx = x + 1 < w ? height0[x + 1] - height0[x] : 0;
                int dy = heightUp[x] - height0[x];
                int n = (dx + dy) * strength >> 12;
                int lit = 2048 + n;
                output[x] = Clamp12(Mul12(light[x], lit) + ambient[x]);
            }
        }

        // ===================================================================
        //  TYPE 22: Flip Horizontal
        // ===================================================================
        private static void EvalFlipH(TextureNode node, int[] output, int w, int row) {
            if (node.Children == null || node.Children.Length < 1 || node.Children[0] == null) {
                Array.Fill(output, 2040, 0, w);
                return;
            }
            int[] child = GetMono(node.Children[0], row);
            for (int x = 0; x < w; x++)
                output[x] = child[w - 1 - x];
        }

        // ===================================================================
        //  TYPE 23: Flip Vertical
        // ===================================================================
        private static void EvalFlipV(TextureNode node, int[] output, int w, int row) {
            if (node.Children == null || node.Children.Length < 1 || node.Children[0] == null) {
                Array.Fill(output, 2040, 0, w);
                return;
            }
            int mirrorRow = node.Height - 1 - row;
            int[] child = GetMono(node.Children[0], mirrorRow);
            Array.Copy(child, output, w);
        }

        // ===================================================================
        //  TYPE 24: Merge RGB (mono → colour)
        // ===================================================================
        private static void EvalMergeRGB(TextureNode node, int[][] output, int w, int row) {
            if (node.Children == null || node.Children.Length < 1 || node.Children[0] == null) {
                Array.Fill(output[0], 2040, 0, w);
                Array.Fill(output[1], 2040, 0, w);
                Array.Fill(output[2], 2040, 0, w);
                return;
            }
            // Takes single mono child and copies to all channels
            int[] child = GetMono(node.Children[0], row);
            Array.Copy(child, output[0], w);
            Array.Copy(child, output[1], w);
            Array.Copy(child, output[2], w);
        }

        // ===================================================================
        //  TYPE 25: Curve Remap
        // ===================================================================
        private static void EvalCurveRemap(TextureNode node, int[] output, int w, int row) {
            if (node.Children == null || node.Children.Length < 1 || node.Children[0] == null) {
                Array.Fill(output, 2040, 0, w);
                return;
            }
            int[] child = GetMono(node.Children[0], row);
            if (node.CurveData != null && node.CurveData.Length == 256) {
                for (int x = 0; x < w; x++) {
                    int idx = Clamp12(child[x]) >> 4;
                    output[x] = node.CurveData[idx];
                }
            } else {
                Array.Copy(child, output, w);
            }
        }

        // ===================================================================
        //  TYPE 26: Turbulence
        // ===================================================================
        private static void EvalTurbulence(TextureNode node, int[] output, int w, int row) {
            if (node.Children == null || node.Children.Length < 1 || node.Children[0] == null) {
                Array.Fill(output, 2040, 0, w);
                return;
            }
            int distort = Math.Max(1, node.IntParam0);
            int seed = node.IntParam1;

            for (int x = 0; x < w; x++) {
                double nx = node.XCoord[x] / (double)FP_ONE;
                double ny = node.YCoord[row] / (double)FP_ONE;
                double dx = PerlinSample(nx * distort + seed, ny * distort + seed) * distort / (double)FP_ONE;
                double dy = PerlinSample(nx * distort + seed + 17.0, ny * distort + seed + 31.0) * distort / (double)FP_ONE;
                int sx = (int)((nx + dx) * w) % w;
                int sy = (int)((ny + dy) * node.Height) % node.Height;
                if (sx < 0) sx += w;
                if (sy < 0) sy += node.Height;
                int[] childRow = GetMono(node.Children[0], sy);
                output[x] = childRow[sx % w];
            }
        }

        // ===================================================================
        //  TYPE 27: Lines/Scratch
        // ===================================================================
        private static void EvalLines(TextureNode node, int[] output, int w, int row) {
            int count = Math.Max(1, node.IntParam0);
            int seed = node.IntParam1;
            int thickness = Math.Max(1, node.IntParam2);
            Array.Fill(output, 0, 0, w);
            for (int i = 0; i < count; i++) {
                int h = HashNoise(i, seed, 0);
                int lx = (h & 0xFF) * w >> 8;
                int ly = ((h >> 8) & 0xFF) * node.Height >> 8;
                int lx2 = ((h >> 16) & 0xFF) * w >> 8;
                int ly2 = ((h >> 24) & 0x7F) * node.Height >> 7;
                // Simple line rasterization
                if (ly <= row && ly2 >= row || ly2 <= row && ly >= row) {
                    int range = Math.Abs(ly2 - ly);
                    int t = range == 0 ? 0 : (row - Math.Min(ly, ly2)) * w / Math.Max(1, range);
                    int lxAtRow = lx + (lx2 - lx) * (row - ly) / Math.Max(1, ly2 - ly);
                    for (int dx = -thickness; dx <= thickness; dx++) {
                        int px = (lxAtRow + dx + w) % w;
                        if (px >= 0 && px < w)
                            output[px] = FP_MAX;
                    }
                }
            }
        }

        // ===================================================================
        //  TYPE 28: Mandelbrot
        // ===================================================================
        private static void EvalMandelbrot(TextureNode node, int[] output, int w, int row) {
            int maxIter = Math.Max(8, node.IntParam0);
            int cx0 = node.IntParam1, cy0 = node.IntParam2;
            int cx1 = node.IntParam3, cy1 = node.IntParam4;
            int scale = node.IntParam5;
            if (cx0 == 0 && cx1 == 0) { cx0 = -8192; cx1 = 4096; cy0 = -4096; cy1 = 4096; }

            double xMin = cx0 / (double)FP_ONE * 2.0 - 1.5;
            double xMax = cx1 / (double)FP_ONE * 2.0 + 0.5;
            double yMin = cy0 / (double)FP_ONE * 2.0 - 1.0;
            double yMax = cy1 / (double)FP_ONE * 2.0 + 1.0;

            double ci = yMin + (yMax - yMin) * row / node.Height;
            for (int x = 0; x < w; x++) {
                double cr = xMin + (xMax - xMin) * x / w;
                double zr = 0, zi = 0;
                int iter = 0;
                while (zr * zr + zi * zi <= 4.0 && iter < maxIter) {
                    double t = zr * zr - zi * zi + cr;
                    zi = 2.0 * zr * zi + ci;
                    zr = t;
                    iter++;
                }
                output[x] = iter >= maxIter ? 0 : (iter * FP_MAX / maxIter);
            }
        }

        // ===================================================================
        //  TYPE 29: Factory (BAIL — too complex to port)
        // ===================================================================
        private static void EvalFactory(TextureNode node, int[] output, int w) {
            Array.Fill(output, 2040, 0, w); // mid-grey fallback
        }

        // ===================================================================
        //  TYPE 30: Edge Detect (Sobel)
        // ===================================================================
        private static void EvalEdgeDetect(TextureNode node, int[] output, int w, int row) {
            if (node.Children == null || node.Children.Length < 1 || node.Children[0] == null) {
                Array.Fill(output, 0, 0, w);
                return;
            }
            int[] cur = GetMono(node.Children[0], row);
            int[] above = GetMono(node.Children[0], Math.Max(0, row - 1));
            int[] below = GetMono(node.Children[0], Math.Min(node.Height - 1, row + 1));

            int strength = Math.Max(1, node.IntParam0);
            for (int x = 0; x < w; x++) {
                int xl = Math.Max(0, x - 1), xr = Math.Min(w - 1, x + 1);
                int gx = -above[xl] + above[xr] - 2 * cur[xl] + 2 * cur[xr] - below[xl] + below[xr];
                int gy = -above[xl] - 2 * above[x] - above[xr] + below[xl] + 2 * below[x] + below[xr];
                int mag = (int)Math.Sqrt(gx * gx + gy * gy) * strength >> 12;
                output[x] = Clamp12(mag);
            }
        }

        // ===================================================================
        //  TYPE 31: Square
        // ===================================================================
        private static void EvalSquare(TextureNode node, int[] output, int w, int row) {
            int sx = node.IntParam0, sy = node.IntParam1;
            int sw = node.IntParam2, sh = node.IntParam3;
            if (sw == 0 && sh == 0) { sw = 2048; sh = 2048; sx = 1024; sy = 1024; }
            int yCoord = node.YCoord[row];
            bool yIn = yCoord >= sy && yCoord < sy + sh;
            for (int x = 0; x < w; x++) {
                int xCoord = node.XCoord[x];
                output[x] = (yIn && xCoord >= sx && xCoord < sx + sw) ? FP_MAX : 0;
            }
        }

        // ===================================================================
        //  TYPE 32: Polar Warp
        // ===================================================================
        private static void EvalPolarWarp(TextureNode node, int[] output, int w, int row) {
            if (node.Children == null || node.Children.Length < 1 || node.Children[0] == null) {
                Array.Fill(output, 2040, 0, w);
                return;
            }
            int centerX = node.IntParam0, centerY = node.IntParam1;
            if (centerX == 0 && centerY == 0) { centerX = 2048; centerY = 2048; }

            for (int x = 0; x < w; x++) {
                double dx = (node.XCoord[x] - centerX) / (double)FP_ONE;
                double dy = (node.YCoord[row] - centerY) / (double)FP_ONE;
                double angle = Math.Atan2(dy, dx) / (2.0 * Math.PI) + 0.5;
                double radius = Math.Sqrt(dx * dx + dy * dy) * 2.0;
                int sx = (int)(angle * w) % w;
                int sy = (int)(radius * node.Height) % node.Height;
                if (sx < 0) sx += w;
                if (sy < 0) sy += node.Height;
                int[] childRow = GetMono(node.Children[0], sy);
                output[x] = childRow[sx % w];
            }
        }

        // ===================================================================
        //  TYPE 33: Offset/Scroll
        // ===================================================================
        private static void EvalOffset(TextureNode node, int[] output, int w, int row) {
            if (node.Children == null || node.Children.Length < 1 || node.Children[0] == null) {
                Array.Fill(output, 2040, 0, w);
                return;
            }
            int offX = (node.IntParam0 * w) >> 12;
            int offY = (node.IntParam1 * node.Height) >> 12;
            int srcRow = ((row - offY) % node.Height + node.Height) % node.Height;
            int[] child = GetMono(node.Children[0], srcRow);
            for (int x = 0; x < w; x++) {
                int sx = ((x - offX) % w + w) % w;
                output[x] = child[sx];
            }
        }

        // ===================================================================
        //  TYPE 34: Curve Remap 2
        // ===================================================================
        private static void EvalCurveRemap2(TextureNode node, int[] output, int w, int row) {
            // Uses CurveData if available, otherwise identity
            if (node.CurveData != null && node.CurveData.Length > 0) {
                int len = node.CurveData.Length;
                for (int x = 0; x < w; x++) {
                    int idx = (node.XCoord[x] * len) >> 12;
                    if (idx < 0) idx = 0;
                    if (idx >= len) idx = len - 1;
                    output[x] = node.CurveData[idx];
                }
            } else {
                for (int x = 0; x < w; x++)
                    output[x] = node.XCoord[x];
            }
        }

        // ===================================================================
        //  TYPE 35: Scale
        // ===================================================================
        private static void EvalScale(TextureNode node, int[] output, int w, int row) {
            if (node.Children == null || node.Children.Length < 1 || node.Children[0] == null) {
                Array.Fill(output, 2040, 0, w);
                return;
            }
            int scaleX = Math.Max(1, node.IntParam0);
            int srcRow = (row * FP_ONE / scaleX) % node.Height;
            if (srcRow < 0) srcRow += node.Height;
            int[] child = GetMono(node.Children[0], srcRow);
            for (int x = 0; x < w; x++) {
                int sx = (x * FP_ONE / scaleX) % w;
                if (sx < 0) sx += w;
                output[x] = child[sx];
            }
        }

        // ===================================================================
        //  TYPE 36: Checkerboard
        // ===================================================================
        private static void EvalCheckerboard(TextureNode node, int[] output, int w, int row) {
            int freq = Math.Max(1, node.IntParam0);
            int yp = (node.YCoord[row] * freq) >> 12;
            for (int x = 0; x < w; x++) {
                int xp = (node.XCoord[x] * freq) >> 12;
                output[x] = ((xp + yp) & 1) == 0 ? FP_MAX : 0;
            }
        }

        // ===================================================================
        //  TYPE 37: Abs/Mirror
        // ===================================================================
        private static void EvalAbsMirror(TextureNode node, int[] output, int w, int row) {
            int mode = node.IntParam0;
            for (int x = 0; x < w; x++) {
                int xc = node.XCoord[x];
                int yc = node.YCoord[row];
                int val;
                switch (mode) {
                    case 0: val = Math.Abs(xc - 2048) * 2; break;
                    case 1: val = Math.Abs(yc - 2048) * 2; break;
                    case 2: val = Math.Max(Math.Abs(xc - 2048), Math.Abs(yc - 2048)) * 2; break;
                    case 3: val = (Math.Abs(xc - 2048) + Math.Abs(yc - 2048)); break;
                    case 4: {
                        double dx = (xc - 2048) / 2048.0;
                        double dy = (yc - 2048) / 2048.0;
                        val = (int)(Math.Sqrt(dx * dx + dy * dy) * FP_ONE);
                        break;
                    }
                    default: val = xc; break;
                }
                output[x] = Clamp12(val);
            }
        }

        // ===================================================================
        //  TYPE 38: Tile/Wrap
        // ===================================================================
        private static void EvalTileWrap(TextureNode node, int[] output, int w, int row) {
            int freqX = Math.Max(1, node.IntParam0);
            int freqY = Math.Max(1, node.IntParam1);
            int yp = (node.YCoord[row] * freqY) & 0xFFF;
            for (int x = 0; x < w; x++) {
                int xp = (node.XCoord[x] * freqX) & 0xFFF;
                // Simple diagonal pattern based on tiled coordinates
                output[x] = (xp + yp) & 0xFFF;
            }
        }
    }
}
