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

        /// <summary>
        /// A copy that can be evaluated independently of this one.
        /// </summary>
        /// <remarks>
        /// See <see cref="TextureNode.CloneConfiguration"/>: evaluation is destructive to the
        /// nodes, so a graph reached through composition has to be cloned before it is rendered.
        /// </remarks>
        internal TextureGraph CloneForComposition() {
            if (Nodes == null)
                return null;

            var copies = new TextureNode[Nodes.Length];
            for (int i = 0; i < Nodes.Length; i++)
                copies[i] = Nodes[i]?.CloneConfiguration();

            //Rewire children against the copies. A decoded graph carries child indices; a graph
            //assembled in code may only have the references, so those are matched by identity
            //against the source array.
            for (int i = 0; i < copies.Length; i++) {
                TextureNode copy = copies[i];
                TextureNode original = Nodes[i];
                if (copy == null)
                    continue;

                if (copy.ChildIndices != null) {
                    copy.Children = new TextureNode[copy.ChildIndices.Length];
                    for (int c = 0; c < copy.ChildIndices.Length; c++) {
                        int idx = copy.ChildIndices[c];
                        if (idx >= 0 && idx < copies.Length)
                            copy.Children[c] = copies[idx];
                    }
                } else if (original.Children != null) {
                    copy.Children = new TextureNode[original.Children.Length];
                    for (int c = 0; c < original.Children.Length; c++)
                        copy.Children[c] = MatchByIdentity(original.Children[c], copies);
                }
            }

            return new TextureGraph {
                Nodes = copies,
                ColourOutputIndex = ColourOutputIndex,
                AlphaOutputIndex = AlphaOutputIndex,
                BrightnessOutputIndex = BrightnessOutputIndex,
            };
        }

        /// <summary>
        /// The copy standing in for <paramref name="child"/>, or the child itself when it is not
        /// one of this graph's nodes.
        /// </summary>
        private TextureNode MatchByIdentity(TextureNode child, TextureNode[] copies) {
            if (child == null)
                return null;
            for (int i = 0; i < Nodes.Length; i++)
                if (ReferenceEquals(Nodes[i], child))
                    return copies[i];
            return child;
        }
    }

    /// <summary>
    /// A single node in the texture graph.
    /// </summary>
    public class TextureNode {
        public int Type;

        /// <summary>
        /// Set only when the graph data overrides whether this node emits one channel or three.
        /// </summary>
        /// <remarks>
        /// Ten node types carry an opcode that overwrites the client's <c>aBoolean3861</c>, so
        /// the channel count is a property of the node rather than of its type. Left null the
        /// node uses its type's default, which is what a hand-built node wants.
        /// </remarks>
        public bool? MonoOverride;

        /// <summary>Shape ids from a type 29 shape list.</summary>
        public int[] ShapeIds;

        /// <summary>
        /// Texture id a type 36 node composes, or -1. These nodes are not generators at all -
        /// they render another whole texture and sample it, which is how textures in this cache
        /// are built out of one another.
        /// </summary>
        public int NestedTextureId = -1;

        /// <summary>Signed shorts trailing a type 34 opcode 2 - explicit per-octave amplitudes.</summary>
        public int[] ShortData;

        /// <summary>Seeded permutation table for the noise generators (types 15 and 34).</summary>
        internal byte[] Permutation;

        /// <summary>Per-octave amplitudes for type 34, after <c>method1108</c>.</summary>
        internal int[] Amplitudes;

        /// <summary>Per-octave frequencies for type 34, after <c>method1108</c>.</summary>
        internal int[] Frequencies;

        /// <summary>Feature-point jitter offsets for type 15, after <c>method1083</c>.</summary>
        internal int[] Jitter;

        /// <summary>The 257-entry transfer curve for type 8, after <c>method1031</c>.</summary>
        internal int[] CurveLut;
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

        // Cached gradient colour LUT for type 10 (built on first use)
        internal int[] GradientColourLUT;

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

        /// <summary>
        /// Copies the decoded configuration, leaving the evaluation scratch behind.
        /// </summary>
        /// <remarks>
        /// Evaluation writes its row caches into the node, so a graph cannot be evaluated by two
        /// threads at once. Composition makes that reachable - the editor renders textures on 20
        /// threads and any two of them may reference the same texture - so a composed graph is
        /// cloned rather than shared. The read-only tables are aliased, not copied.
        /// </remarks>
        internal TextureNode CloneConfiguration() => new TextureNode {
            Type = Type,
            MonoOverride = MonoOverride,
            ChildIndices = ChildIndices,
            IntParam0 = IntParam0, IntParam1 = IntParam1, IntParam2 = IntParam2,
            IntParam3 = IntParam3, IntParam4 = IntParam4, IntParam5 = IntParam5,
            IntParam6 = IntParam6, IntParam7 = IntParam7, IntParam8 = IntParam8,
            BlendMode = BlendMode,
            CurveData = CurveData,
            GradientData = GradientData,
            GradientPreset = GradientPreset,
            GradientCount = GradientCount,
            SpriteId = SpriteId,
            //Normally null at clone time, since Render loads sprites after allocating. Carried
            //across so a node that was handed its pixels directly keeps them.
            SpritePixels = SpritePixels,
            SpriteWidth = SpriteWidth,
            SpriteHeight = SpriteHeight,
            ShapeIds = ShapeIds,
            ShortData = ShortData,
            NestedTextureId = NestedTextureId,
            Permutation = Permutation,
            Amplitudes = Amplitudes,
            Frequencies = Frequencies,
            Jitter = Jitter,
            CurveLut = CurveLut,
        };
    }

    /// <summary>
    /// Evaluates a parsed procedural texture graph to produce an ARGB bitmap.
    /// Matches the client's method1631 pipeline using 12-bit fixed-point arithmetic.
    /// </summary>
    public static class TextureGraphEvaluator {
        private const int FP_ONE = 4096;
        private const int FP_MAX = 4080; // 255/256 * 4096

        /// <summary>
        /// Texture ids currently being rendered on this thread. A type 36 node can reference a
        /// texture that references it back, so composition has to refuse to re-enter.
        /// </summary>
        [ThreadStatic]
        private static HashSet<int> _renderStack;

        public static Bitmap Render(TextureGraph graph, int width, int height, RSCache cache, bool transpose = false, int textureDefId = -1) {
            int[] pixels = RenderArgb(graph, width, height, cache, transpose, textureDefId);
            if (pixels == null)
                return null;

            var bmp = new Bitmap(width, height, PixelFormat.Format32bppArgb);
            var data = bmp.LockBits(new Rectangle(0, 0, width, height),
                ImageLockMode.WriteOnly, PixelFormat.Format32bppArgb);
            Marshal.Copy(pixels, 0, data.Scan0, pixels.Length);
            bmp.UnlockBits(data);
            Debug($"[GraphEval] tex {textureDefId}: COMPLETE — returning {bmp.Width}x{bmp.Height} bitmap", LOG_DETAIL.ADVANCED);
            return bmp;
        }

        /// <summary>
        /// Evaluates a graph to a packed ARGB buffer. Type 36 composition needs pixels rather
        /// than a <see cref="Bitmap"/>, so the bitmap wrapper sits on top of this rather than
        /// the other way round.
        /// </summary>
        internal static int[] RenderArgb(TextureGraph source, int width, int height, RSCache cache, bool transpose = false, int textureDefId = -1) {
            if (source == null || source.Nodes == null || source.Nodes.Length == 0) {
                Debug($"[GraphEval] tex {textureDefId}: null/empty graph", LOG_DETAIL.ADVANCED);
                return null;
            }

            //Evaluation caches each row into the nodes, so it works on a copy. The editor
            //renders on 20 threads and composition lets two of them reach the same graph, and
            //the caller has no way to know that happened - so the safety belongs here rather
            //than in a rule about who may call this.
            TextureGraph graph = source.CloneForComposition();

            Debug($"[GraphEval] tex {textureDefId}: BEGIN — {graph.Nodes.Length} nodes, {width}x{height}, " +
                  $"colourOut={graph.ColourOutputIndex}, alphaOut={graph.AlphaOutputIndex}, transpose={transpose}", LOG_DETAIL.ADVANCED);

            // Build gamma LUT: pow(i/255.0, 0.7) * 255
            byte[] gammaLUT = new byte[256];
            for (int i = 0; i < 256; i++)
                gammaLUT[i] = (byte)(Math.Pow(i / 255.0, 0.7) * 255.0 + 0.5);

            // Build coordinate LUTs
            int[] xCoord = new int[width];
            int[] yCoord = new int[height];
            for (int i = 0; i < width; i++)
                xCoord[i] = (i << 12) / width;
            for (int i = 0; i < height; i++)
                yCoord[i] = (i << 12) / height;

            // Allocate node buffers and load sprites
            int spriteNodesLoaded = 0, spriteNodesFailed = 0;
            for (int ni = 0; ni < graph.Nodes.Length; ni++) {
                var node = graph.Nodes[ni];
                if (node == null) continue;
                node.Allocate(width, height, xCoord, yCoord);
                if (node.Type == 36 && node.NestedTextureId >= 0) {
                    LoadNestedTextureForNode(node, cache, textureDefId);
                    continue;
                }
                if ((node.Type == 18 || node.Type == 39) && node.SpriteId >= 0) {
                    Debug($"[GraphEval] tex {textureDefId}: node[{ni}] type={node.Type} loading sprite {node.SpriteId}", LOG_DETAIL.ADVANCED);
                    LoadSpriteForNode(node, cache, textureDefId);
                    if (node.SpritePixels != null) {
                        spriteNodesLoaded++;
                        Debug($"[GraphEval] tex {textureDefId}: node[{ni}] sprite loaded OK — {node.SpriteWidth}x{node.SpriteHeight}, {node.SpritePixels.Length} pixels", LOG_DETAIL.ADVANCED);
                    } else {
                        spriteNodesFailed++;
                        Debug($"[GraphEval] tex {textureDefId}: node[{ni}] sprite {node.SpriteId} FAILED to load — SpritePixels is null", LOG_DETAIL.BASIC);
                    }
                }
            }
            if (spriteNodesLoaded + spriteNodesFailed > 0)
                Debug($"[GraphEval] tex {textureDefId}: sprites loaded={spriteNodesLoaded}, failed={spriteNodesFailed}", LOG_DETAIL.ADVANCED);

            // Evaluate row-by-row
            int colourIdx = graph.ColourOutputIndex;
            if (colourIdx < 0 || colourIdx >= graph.Nodes.Length || graph.Nodes[colourIdx] == null) {
                Debug($"[GraphEval] tex {textureDefId}: invalid colourOutputIndex={colourIdx} (nodes={graph.Nodes.Length}) — returning null", LOG_DETAIL.BASIC);
                return null;
            }

            var pixels = new int[width * height];
            var colourNode = graph.Nodes[colourIdx];
            bool outputIsMono = IsMonochrome(colourNode);

            //Deliberately not sampling the alpha output node. This mirrors the client's
            //Node_Sub46_Sub19.method1631, the path behind method9 - the one a composed texture
            //is rendered through - which derives alpha from the colour alone. Sampling the
            //alpha node is method1633, a separate entry point for GL uploads.
            TextureNode alphaNode = null;

            for (int y = 0; y < height; y++) {
                int[] alphaMono = alphaNode != null ? GetMono(alphaNode, y) : null;

                if (outputIsMono) {
                    int[] mono = GetMono(colourNode, y);
                    for (int x = 0; x < width; x++) {
                        int v = gammaLUT[Clamp12(mono[x]) >> 4];
                        //method1631 leaves a pure-black pixel fully transparent and makes every
                        //other pixel opaque.
                        int alpha = alphaMono != null
                            ? (v == 0 ? 0 : Math.Clamp(alphaMono[x] >> 4, 0, 255))
                            : (v == 0 ? 0 : 0xFF);
                        int idx = transpose ? x * width + y : y * width + x;
                        pixels[idx] = (alpha << 24) | (v << 16) | (v << 8) | v;
                    }
                } else {
                    int[][] rgb = GetColour(colourNode, y);
                    for (int x = 0; x < width; x++) {
                        int r = gammaLUT[Clamp12(rgb[0][x]) >> 4];
                        int g = gammaLUT[Clamp12(rgb[1][x]) >> 4];
                        int b = gammaLUT[Clamp12(rgb[2][x]) >> 4];
                        bool black = r == 0 && g == 0 && b == 0;
                        int alpha = black ? 0
                            : alphaMono != null ? Math.Clamp(alphaMono[x] >> 4, 0, 255) : 0xFF;
                        int idx = transpose ? x * width + y : y * width + x;
                        pixels[idx] = (alpha << 24) | (r << 16) | (g << 8) | b;
                    }
                }
            }

            // Release node buffers
            foreach (var node in graph.Nodes)
                node?.Release();

            // Diagnostic: check if image is all-black or all-transparent
            int nonBlack = 0, nonTransparent = 0;
            for (int i = 0; i < pixels.Length && i < 1000; i++) {
                if ((pixels[i] & 0x00FFFFFF) != 0) nonBlack++;
                if (((pixels[i] >> 24) & 0xFF) != 0) nonTransparent++;
            }
            int sampled = Math.Min(pixels.Length, 1000);
            Debug($"[GraphEval] tex {textureDefId}: pixel sample ({sampled}px): {nonBlack} non-black, {nonTransparent} non-transparent", LOG_DETAIL.ADVANCED);
            if (nonBlack == 0)
                Debug($"[GraphEval] tex {textureDefId}: WARNING — all sampled pixels are black!", LOG_DETAIL.BASIC);

            return pixels;
        }

        /// <summary>
        /// Renders the texture a type 36 node composes and hands the pixels to the node, which
        /// then samples them exactly as a sprite node samples a sprite.
        /// </summary>
        /// <remarks>
        /// The client picks the nested render size off the referenced texture's own mipmap flag
        /// (<c>Node_Sub10_Sub25.method998</c>), not off the size of the texture being built.
        /// </remarks>
        private static void LoadNestedTextureForNode(TextureNode node, RSCache cache, int textureDefId) {
            int nestedId = node.NestedTextureId;

            _renderStack ??= new HashSet<int>();
            if (_renderStack.Count >= 6 || !_renderStack.Add(nestedId)) {
                Debug($"[GraphEval] tex {textureDefId}: node composes texture {nestedId} - refusing to recurse", LOG_DETAIL.ADVANCED);
                return;
            }

            try {
                if (!TextureManager.Textures.TryGetValue(nestedId, out TextureDefinition nested) || nested?.graph == null) {
                    Debug($"[GraphEval] tex {textureDefId}: composed texture {nestedId} has no graph", LOG_DETAIL.ADVANCED);
                    return;
                }

                int size = nested.field1822 ? 64 : 128;
                int[] argb = RenderArgb(nested.graph, size, size, cache, nested.field1824, nestedId);
                if (argb == null)
                    return;

                node.SpritePixels = argb;
                node.SpriteWidth = size;
                node.SpriteHeight = size;
                Debug($"[GraphEval] tex {textureDefId}: composed texture {nestedId} at {size}x{size}", LOG_DETAIL.ADVANCED);
            } catch (Exception ex) {
                Debug($"[GraphEval] tex {textureDefId}: composing texture {nestedId} FAILED — {ex.GetType().Name}: {ex.Message}", LOG_DETAIL.BASIC);
            } finally {
                _renderStack.Remove(nestedId);
            }
        }

        // Sprite ID override table for texture IDs 939-945
        private static readonly Dictionary<int, int> _spriteOverrides = new() {
            { 939, 523 }, { 940, 524 }, { 941, 525 }, { 942, 526 },
            { 943, 527 }, { 944, 528 }, { 945, 1069 }
        };

        private static void LoadSpriteForNode(TextureNode node, RSCache cache, int textureDefId = -1) {
            if (cache == null) {
                Debug($"[SpriteLoad] tex {textureDefId}: cache is null", LOG_DETAIL.BASIC);
                return;
            }
            if (node.SpriteId < 0) {
                Debug($"[SpriteLoad] tex {textureDefId}: spriteId={node.SpriteId} (negative, skipping)", LOG_DETAIL.ADVANCED);
                return;
            }
            int origSpriteId = node.SpriteId;
            int spriteId = origSpriteId;
            if (textureDefId >= 0 && _spriteOverrides.TryGetValue(textureDefId, out int overrideId)) {
                spriteId = overrideId;
                Debug($"[SpriteLoad] tex {textureDefId}: sprite override {origSpriteId} → {spriteId}", LOG_DETAIL.ADVANCED);
            }
            try {
                Debug($"[SpriteLoad] tex {textureDefId}: GetSprite({spriteId}) ...", LOG_DETAIL.INSANE);
                SpriteDefinition sprite = cache.GetSprite(spriteId);
                if (sprite == null) {
                    Debug($"[SpriteLoad] tex {textureDefId}: sprite {spriteId} — GetSprite returned null", LOG_DETAIL.ADVANCED);
                    return;
                }
                int frameCount = sprite.GetFrameCount();
                if (frameCount == 0) {
                    Debug($"[SpriteLoad] tex {textureDefId}: sprite {spriteId} — 0 frames", LOG_DETAIL.ADVANCED);
                    return;
                }
                var frame = sprite.GetFrame(0);
                if (frame == null) {
                    Debug($"[SpriteLoad] tex {textureDefId}: sprite {spriteId} — frame 0 null", LOG_DETAIL.ADVANCED);
                    return;
                }
                node.SpritePixels = frame.GetPixels();
                node.SpriteWidth = frame.GetWidth();
                node.SpriteHeight = frame.GetHeight();
                if (node.SpritePixels == null || node.SpritePixels.Length == 0) {
                    Debug($"[SpriteLoad] tex {textureDefId}: sprite {spriteId} — GetPixels returned null/empty (w={node.SpriteWidth}, h={node.SpriteHeight})", LOG_DETAIL.BASIC);
                    node.SpritePixels = null;
                } else {
                    Debug($"[SpriteLoad] tex {textureDefId}: sprite {spriteId} — OK {node.SpriteWidth}x{node.SpriteHeight}, {node.SpritePixels.Length} pixels", LOG_DETAIL.ADVANCED);
                }
            } catch (Exception ex) {
                Debug($"[SpriteLoad] tex {textureDefId}: sprite {spriteId} FAILED — {ex.GetType().Name}: {ex.Message}", LOG_DETAIL.BASIC);
                Debug($"[SpriteLoad] tex {textureDefId}: stack: {ex.StackTrace?.Split('\n').FirstOrDefault()?.Trim()}", LOG_DETAIL.ADVANCED);
            }
        }

        /// <summary>
        /// Whether a node emits a single channel. This is a per-node property, not a property
        /// of the node type: types 5, 6, 7, 9, 19, 21, 22, 23, 29 and 30 all carry an opcode
        /// that overwrites it, so reading it back off a type table renders those nodes on the
        /// wrong number of channels.
        /// </summary>
        private static bool IsMonochrome(TextureNode node) =>
            node.MonoOverride ?? Texture.DefaultIsMonochrome(node.Type);

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
            if (node.ColourCache == null) {
                // Node was never allocated — return a safe fallback
                return _fallbackColour ??= new int[][] { new int[1], new int[1], new int[1] };
            }

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

        [ThreadStatic]
        private static int[][]? _fallbackColour;

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
                case 5: EvalBoxBlurMono(node, output, w, row); break;
                case 6: EvalClampNodeMono(node, output, w, row); break;
                case 8: EvalCurveTransfer(node, output, w, row); break;
                case 9: EvalMirrorFlipMono(node, output, w, row); break;
                case 10: EvalGradientRemap(node, output, w, row); break;
                case 12: EvalNoise(node, output, w, row); break;
                case 13: EvalVoronoi(node, output, w, row); break;
                case 14: EvalSineWave(node, output, w, row); break;
                case 15: EvalWorley(node, output, w, row); break;
                case 16: EvalThreshold(node, output, w, row); break;
                case 17: EvalBlur(node, output, w, row); break;
                case 19: EvalPolarDistortionMono(node, output, w, row); break;
                case 20: EvalTileMono(node, output, w, row); break;
                case 21: EvalEmboss(node, output, w, row); break;
                case 22: EvalInvertMono(node, output, w, row); break;
                case 23: EvalFlipV(node, output, w, row); break;
                //Type 25 has no arm here on purpose: Node_Sub10_Sub14 is constructed
                //super(1, false) and never overrides Node_Sub10.method990, so asking it for a
                //monochrome row throws in the client. See EvalColourKeyScale.
                case 26: EvalTurbulence(node, output, w, row); break;
                case 27: EvalLines(node, output, w, row); break;
                case 28: EvalMandelbrot(node, output, w, row); break;
                case 29: EvalFactory(node, output, w); break;
                case 30: EvalRangeRemapMono(node, output, w, row); break;
                case 31: EvalSquare(node, output, w, row); break;
                case 32: EvalPolarWarp(node, output, w, row); break;
                case 33: EvalOffset(node, output, w, row); break;
                case 34: EvalFractalNoise(node, output, w, row); break;
                case 35: EvalBumpMap(node, output, w, row); break;
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
                case 5: EvalBoxBlurColour(node, output, w, row); break;
                case 6: EvalClampNodeColour(node, output, w, row); break;
                case 7: EvalColourBlend(node, output, w, row); break;
                case 9: EvalMirrorFlipColour(node, output, w, row); break;
                case 11: EvalHSLAdjust(node, output, w, row); break;
                case 10: EvalGradientRemapColour(node, output, w, row); break;
                case 17: EvalHSLAdjust17(node, output, w, row); break;
                case 18: // falls through to 39
                case 39: EvalSpriteSource(node, output, w, row); break;
                //A composed texture is sampled the same way a sprite is; only the source of
                //the pixels differs.
                case 36: EvalSpriteSource(node, output, w, row); break;
                case 19: EvalPolarDistortionColour(node, output, w, row); break;
                case 20: EvalTileColour(node, output, w, row); break;
                case 21: goto default; // Emboss — no colour variant
                case 22: EvalInvertColour(node, output, w, row); break;
                case 23: EvalFlipVColour(node, output, w, row); break;
                case 24: EvalMergeRGB(node, output, w, row); break;
                case 25: EvalColourKeyScale(node, output, w, row); break;
                case 30: EvalRangeRemapColour(node, output, w, row); break;
                case 33: goto default; // Offset — no colour variant
                default:
                    // Colour-capable node without dedicated colour eval —
                    // pass through child colour if available, else promote from mono
                    if (node.Children != null && node.Children.Length >= 1 && node.Children[0] != null) {
                        int[][] childC = GetColour(node.Children[0], row);
                        Array.Copy(childC[0], output[0], w);
                        Array.Copy(childC[1], output[1], w);
                        Array.Copy(childC[2], output[2], w);
                    } else {
                        EvalMono(node, row);
                        Array.Copy(node.MonoCache, output[0], w);
                        Array.Copy(node.MonoCache, output[1], w);
                        Array.Copy(node.MonoCache, output[2], w);
                    }
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
        //  TYPE 5: Box Blur (separable)
        // ===================================================================
        private static void EvalBoxBlurMono(TextureNode node, int[] output, int w, int row) {
            if (node.Children == null || node.Children.Length < 1 || node.Children[0] == null) {
                Array.Fill(output, 2040, 0, w);
                return;
            }
            int radiusH = node.IntParam0;
            int radiusV = node.IntParam1;

            // Vertical pass: average rows [row-radiusV..row+radiusV]
            int[] vSum = new int[w];
            int vCount = 0;
            for (int dy = -radiusV; dy <= radiusV; dy++) {
                int sy = ((row + dy) % node.Height + node.Height) % node.Height;
                int[] childRow = GetMono(node.Children[0], sy);
                vCount++;
                for (int x = 0; x < w; x++)
                    vSum[x] += childRow[x];
            }
            if (vCount > 0)
                for (int x = 0; x < w; x++)
                    vSum[x] /= vCount;

            // Horizontal pass: sliding window average
            for (int x = 0; x < w; x++) {
                int s = 0, c = 0;
                for (int dx = -radiusH; dx <= radiusH; dx++) {
                    int sx = ((x + dx) % w + w) % w;
                    s += vSum[sx];
                    c++;
                }
                output[x] = s / c;
            }
        }

        private static void EvalBoxBlurColour(TextureNode node, int[][] output, int w, int row) {
            if (node.Children == null || node.Children.Length < 1 || node.Children[0] == null) {
                Array.Fill(output[0], 2040, 0, w);
                Array.Fill(output[1], 2040, 0, w);
                Array.Fill(output[2], 2040, 0, w);
                return;
            }
            int radiusH = node.IntParam0;
            int radiusV = node.IntParam1;

            int[][] vSum = { new int[w], new int[w], new int[w] };
            int vCount = 0;
            for (int dy = -radiusV; dy <= radiusV; dy++) {
                int sy = ((row + dy) % node.Height + node.Height) % node.Height;
                int[][] childRow = GetColour(node.Children[0], sy);
                vCount++;
                for (int ch = 0; ch < 3; ch++)
                    for (int x = 0; x < w; x++)
                        vSum[ch][x] += childRow[ch][x];
            }
            if (vCount > 0)
                for (int ch = 0; ch < 3; ch++)
                    for (int x = 0; x < w; x++)
                        vSum[ch][x] /= vCount;

            for (int x = 0; x < w; x++) {
                int[] s = { 0, 0, 0 };
                int c = 0;
                for (int dx = -radiusH; dx <= radiusH; dx++) {
                    int sx = ((x + dx) % w + w) % w;
                    for (int ch = 0; ch < 3; ch++)
                        s[ch] += vSum[ch][sx];
                    c++;
                }
                for (int ch = 0; ch < 3; ch++)
                    output[ch][x] = s[ch] / c;
            }
        }

        // ===================================================================
        //  TYPE 6: Clamp Node (mono path)
        // ===================================================================
        private static void EvalClampNodeMono(TextureNode node, int[] output, int w, int row) {
            if (node.Children == null || node.Children.Length < 1 || node.Children[0] == null) {
                Array.Fill(output, 2040, 0, w);
                return;
            }
            int[] child = GetMono(node.Children[0], row);
            int lo = node.IntParam0; // default 0
            //The 4096 default is seeded at decode time, so an explicit upper bound of zero
            //clamps to zero rather than being mistaken for "unset".
            int hi = node.IntParam1;
            for (int x = 0; x < w; x++) {
                int v = child[x];
                output[x] = v < lo ? lo : v > hi ? hi : v;
            }
        }

        private static void EvalClampNodeColour(TextureNode node, int[][] output, int w, int row) {
            if (node.Children == null || node.Children.Length < 1 || node.Children[0] == null) {
                Array.Fill(output[0], 2040, 0, w);
                Array.Fill(output[1], 2040, 0, w);
                Array.Fill(output[2], 2040, 0, w);
                return;
            }
            int[][] child = GetColour(node.Children[0], row);
            int lo = node.IntParam0;
            int hi = node.IntParam1;
            for (int ch = 0; ch < 3; ch++)
                for (int x = 0; x < w; x++) {
                    int v = child[ch][x];
                    output[ch][x] = v < lo ? lo : v > hi ? hi : v;
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
            int mode = node.BlendMode;

            //The client writes the blend straight out. There is no blend amount to interpolate
            //against - the value that used to be read as one was the mode itself.
            for (int ch = 0; ch < 3; ch++)
                for (int x = 0; x < w; x++)
                    output[ch][x] = BlendOp(a[ch][x], b[ch][x], mode);
        }

        /// <summary>
        /// The twelve blend operations of Node_Sub10_Sub7, in the client's numbering. Modes 6
        /// through 12 previously mapped to entirely different operations, and mode 6 is the one
        /// a blend node with no mode opcode falls back to.
        /// </summary>
        private static int BlendOp(int a, int b, int mode) {
            switch (mode) {
                case 1: return a + b;                                     // add
                case 2: return a - b;                                     // subtract
                case 3: return (b * a) >> 12;                             // multiply
                case 4: return b != 0 ? (a << 12) / b : FP_ONE;           // divide
                case 5: return FP_ONE - (((FP_ONE - a) * (FP_ONE - b)) >> 12); // screen
                case 6:                                                   // hard light
                    return b >= 2048
                        ? FP_ONE - (((FP_ONE - a) * (FP_ONE - b)) >> 11)
                        : (b * a) >> 11;
                case 7:                                                   // colour dodge
                    return a == FP_ONE ? FP_ONE : (b << 12) / (FP_ONE - a);
                case 8:                                                   // colour burn
                    return a == 0 ? 0 : FP_ONE - (((FP_ONE - b) << 12) / a);
                case 9: return Math.Min(a, b);                            // darken
                case 10: return Math.Max(a, b);                           // lighten
                case 11: return Math.Abs(a - b);                          // difference
                case 12: return a + b - ((a * b) >> 11);                  // vivid add
                default: return b;
            }
        }

        // ===================================================================
        //  TYPE 8: Curve/Spline Transfer (mono)
        // ===================================================================
        private static void EvalCurveTransfer(TextureNode node, int[] output, int w, int row) {
            if (node.Children == null || node.Children.Length < 1 || node.Children[0] == null) {
                Array.Fill(output, 2040, 0, w);
                return;
            }

            int[] child = GetMono(node.Children[0], row);
            int[] lut = node.CurveLut;

            //A curve with fewer than two markers is malformed; the client throws on it. Passing
            //the input through keeps the rest of the texture renderable.
            if (lut == null) {
                Array.Copy(child, output, w);
                return;
            }

            for (int x = 0; x < w; x++)
                output[x] = lut[Math.Clamp(child[x] >> 4, 0, 256)];
        }

        // ===================================================================
        //  TYPE 9: Mirror/Flip
        // ===================================================================
        private static void EvalMirrorFlipMono(TextureNode node, int[] output, int w, int row) {
            if (node.Children == null || node.Children.Length < 1 || node.Children[0] == null) {
                Array.Fill(output, 2040, 0, w);
                return;
            }
            int srcRow = (node.IntParam1 != 0) ? (node.Height - 1 - row) : row;
            int[] child = GetMono(node.Children[0], srcRow);
            if (node.IntParam0 != 0) {
                for (int x = 0; x < w; x++)
                    output[x] = child[w - 1 - x];
            } else {
                Array.Copy(child, output, w);
            }
        }

        private static void EvalMirrorFlipColour(TextureNode node, int[][] output, int w, int row) {
            if (node.Children == null || node.Children.Length < 1 || node.Children[0] == null) {
                Array.Fill(output[0], 2040, 0, w);
                Array.Fill(output[1], 2040, 0, w);
                Array.Fill(output[2], 2040, 0, w);
                return;
            }
            int srcRow = (node.IntParam1 != 0) ? (node.Height - 1 - row) : row;
            int[][] child = GetColour(node.Children[0], srcRow);
            if (node.IntParam0 != 0) {
                for (int ch = 0; ch < 3; ch++)
                    for (int x = 0; x < w; x++)
                        output[ch][x] = child[ch][w - 1 - x];
            } else {
                for (int ch = 0; ch < 3; ch++)
                    Array.Copy(child[ch], output[ch], w);
            }
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
                case 6: { // smoothstep
                    double t = val / (double)FP_ONE;
                    t = t * t * (3.0 - 2.0 * t);
                    return (int)(t * FP_ONE);
                }
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
        //  TYPE 10: Gradient Remap — Colour Output (Hydra Sub33)
        //  Maps mono child through a 257-entry packed RGB LUT built from
        //  gradient stops (preset or custom), extracting R/G/B channels.
        // ===================================================================
        private static void EvalGradientRemapColour(TextureNode node, int[][] output, int w, int row) {
            if (node.Children == null || node.Children.Length < 1 || node.Children[0] == null) {
                Array.Fill(output[0], 2040, 0, w);
                Array.Fill(output[1], 2040, 0, w);
                Array.Fill(output[2], 2040, 0, w);
                return;
            }
            int[] child = GetMono(node.Children[0], row);
            int[] lut = GetOrBuildGradientColourLUT(node);

            for (int x = 0; x < w; x++) {
                int idx = child[x] >> 4; // 12-bit → 8-bit index (0-256)
                if (idx < 0) idx = 0;
                if (idx > 256) idx = 256;
                int packed = lut[idx];
                output[0][x] = (packed >> 12) & 0xFF0; // R in 12-bit
                output[1][x] = (packed >> 4) & 0xFF0;  // G in 12-bit
                output[2][x] = (packed << 4) & 0xFF0;  // B in 12-bit
            }
        }

        private static int[] GetOrBuildGradientColourLUT(TextureNode node) {
            if (node.GradientColourLUT != null) return node.GradientColourLUT;
            node.GradientColourLUT = BuildGradientColourLUT(node);
            return node.GradientColourLUT;
        }

        private static int[] BuildGradientColourLUT(TextureNode node) {
            int[][] stops;

            if (node.GradientPreset != 0) {
                stops = GetPresetGradientData(node.GradientPreset);
            } else if (node.GradientData != null && node.GradientData.Length > 0) {
                // Custom gradient data: FlashEditor stores R/G/B as raw 8-bit,
                // but Hydra expects 12-bit. Convert to 12-bit for interpolation.
                stops = new int[node.GradientData.Length][];
                for (int i = 0; i < node.GradientData.Length; i++) {
                    stops[i] = new int[4];
                    stops[i][0] = node.GradientData[i][0];      // position (already 0-4096)
                    stops[i][1] = node.GradientData[i][1] << 4; // R: 8-bit → 12-bit
                    stops[i][2] = node.GradientData[i][2] << 4; // G: 8-bit → 12-bit
                    stops[i][3] = node.GradientData[i][3] << 4; // B: 8-bit → 12-bit
                }
            } else {
                // No gradient data — identity grayscale ramp
                int[] identity = new int[257];
                for (int i = 0; i <= 256; i++) {
                    int v = Math.Clamp(i, 0, 255);
                    identity[i] = (v << 16) | (v << 8) | v;
                }
                return identity;
            }

            int numStops = stops.Length;
            int[] lut = new int[257];

            for (int i = 0; i <= 256; i++) {
                int pos = i << 4; // map 0-256 → 0-4096

                // Find how many stops have position <= pos
                int seg = 0;
                for (int s = 0; s < numStops; s++) {
                    if (pos < stops[s][0]) break;
                    seg++;
                }

                int r12, g12, b12;

                if (seg > 0 && seg < numStops) {
                    // Between two stops — interpolate
                    int[] prev = stops[seg - 1];
                    int[] next = stops[seg];
                    int range = next[0] - prev[0];
                    if (range <= 0) {
                        r12 = next[1]; g12 = next[2]; b12 = next[3];
                    } else {
                        int t = ((pos - prev[0]) << 12) / range;
                        int invT = FP_ONE - t;
                        r12 = (next[1] * t + prev[1] * invT) >> 12;
                        g12 = (next[2] * t + prev[2] * invT) >> 12;
                        b12 = (next[3] * t + prev[3] * invT) >> 12;
                    }
                } else if (seg == 0) {
                    // Before first stop
                    r12 = stops[0][1]; g12 = stops[0][2]; b12 = stops[0][3];
                } else {
                    // Past last stop
                    int last = numStops - 1;
                    r12 = stops[last][1]; g12 = stops[last][2]; b12 = stops[last][3];
                }

                // Convert 12-bit to 8-bit, clamp, and pack as (R<<16)|(G<<8)|B
                int r8 = Math.Clamp(r12 >> 4, 0, 255);
                int g8 = Math.Clamp(g12 >> 4, 0, 255);
                int b8 = Math.Clamp(b12 >> 4, 0, 255);

                lut[i] = (r8 << 16) | (g8 << 8) | b8;
            }

            return lut;
        }

        /// <summary>
        /// Returns preset gradient stop data in 12-bit format.
        /// Format: int[n][4] = { position, R_12bit, G_12bit, B_12bit }.
        /// Matches Hydra Node_Sub10_Sub33.method1100 presets 1-6.
        /// </summary>
        private static int[][] GetPresetGradientData(int preset) {
            switch (preset) {
                case 1: return new[] { // Black → White
                    new[] { 0, 0, 0, 0 },
                    new[] { 4096, 4096, 4096, 4096 }
                };
                case 2: return new[] { // Warm earth tones
                    new[] { 0, 2650, 2602, 2361 },
                    new[] { 2867, 2313, 1799, 1558 },
                    new[] { 3072, 2618, 1734, 1413 },
                    new[] { 3276, 2296, 1220, 947 },
                    new[] { 3481, 2072, 963, 722 },
                    new[] { 3686, 2730, 2152, 1766 },
                    new[] { 3891, 2232, 1060, 915 },
                    new[] { 4096, 1686, 1413, 1140 }
                };
                case 3: return new[] { // Full spectrum rainbow
                    new[] { 0, 0, 0, 4096 },
                    new[] { 663, 0, 4096, 4096 },
                    new[] { 1363, 0, 4096, 0 },
                    new[] { 2048, 4096, 4096, 0 },
                    new[] { 2727, 4096, 0, 0 },
                    new[] { 3411, 4096, 0, 4096 },
                    new[] { 4096, 0, 0, 4096 }
                };
                case 6: return new[] { // Green-yellow-red (client preset 6)
                    new[] { 2048, 0, 4096, 0 },
                    new[] { 2867, 4096, 4096, 0 },
                    new[] { 3276, 4096, 4096, 0 },
                    new[] { 4096, 4096, 0, 0 }
                };
                case 4: return new[] { // Black-blue-cyan-white (client preset 4)
                    new[] { 0, 0, 0, 0 },
                    new[] { 1843, 0, 0, 1493 },
                    new[] { 2457, 0, 0, 2939 },
                    new[] { 2781, 0, 1124, 3565 },
                    new[] { 3481, 546, 3084, 4031 },
                    new[] { 4096, 4096, 4096, 4096 }
                };
                case 5: return new[] { // Earth tones, 16 stops (client preset 5)
                    new[] { 0, 80, 192, 321 },
                    new[] { 155, 321, 449, 562 },
                    new[] { 389, 578, 690, 803 },
                    new[] { 671, 947, 995, 1140 },
                    new[] { 897, 1285, 1397, 1509 },
                    new[] { 1175, 1525, 1429, 1413 },
                    new[] { 1368, 1734, 1461, 1333 },
                    new[] { 1507, 1413, 1525, 1702 },
                    new[] { 1736, 1108, 1590, 2056 },
                    new[] { 2088, 1766, 2056, 2666 },
                    new[] { 2355, 2409, 2586, 3276 },
                    new[] { 2691, 3116, 3148, 3228 },
                    new[] { 3031, 3806, 3710, 3196 },
                    new[] { 3522, 3437, 3421, 3019 },
                    new[] { 3727, 3116, 3148, 3228 },
                    new[] { 4096, 2377, 2505, 2746 }
                };
                default: return new[] { // Unknown preset — linear grayscale
                    new[] { 0, 0, 0, 0 },
                    new[] { 4096, 4096, 4096, 4096 }
                };
            }
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
        //  TYPE 17: HSL Adjust (Hydra Sub6 — NOT blur)
        //  Adjusts hue/saturation/lightness of a colour child input.
        //  IntParam0 = hue shift (signed short), IntParam1/2 = S/L adjust
        //  (signed bytes, scaled by << 12 / 100 per Hydra).
        // ===================================================================
        private static void EvalHSLAdjust17(TextureNode node, int[][] output, int w, int row) {
            if (node.Children == null || node.Children.Length < 1 || node.Children[0] == null) {
                Array.Fill(output[0], 2040, 0, w);
                Array.Fill(output[1], 2040, 0, w);
                Array.Fill(output[2], 2040, 0, w);
                return;
            }
            int[][] child = GetColour(node.Children[0], row);

            // Hydra: hShift = readShort() (signed), sAdj/lAdj = (readSignedByte() << 12) / 100
            int hShift = (short)node.IntParam0;
            int sAdj = (node.IntParam1 << 12) / 100;
            int lAdj = (node.IntParam2 << 12) / 100;

            for (int x = 0; x < w; x++) {
                int r = child[0][x], g = child[1][x], b = child[2][x];
                RGBtoHSL(r, g, b, out int h, out int s, out int l);

                h += hShift;
                s += sAdj;
                l += lAdj;

                // Wrap hue to [0, 4096)
                while (h < 0) h += FP_ONE;
                while (h > FP_ONE) h -= FP_ONE;

                // Clamp saturation and lightness to [0, 4096]
                if (s < 0) s = 0;
                if (s > FP_ONE) s = FP_ONE;
                if (l < 0) l = 0;
                if (l > FP_ONE) l = FP_ONE;

                HSLtoRGB(h, s, l, out int or_, out int og, out int ob);
                output[0][x] = or_;
                output[1][x] = og;
                output[2][x] = ob;
            }
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
        //  Perlin gradient noise - support for type 26 only
        // ===================================================================
        //A fractal Perlin generator used to stand in for type 15 here. Type 15 is
        //Node_Sub10_Sub26, cellular noise, and it is now ported as EvalWorley; the Perlin
        //generator went with it. What survives is the single-octave sampler below, which
        //EvalTurbulence uses to displace its coordinates.

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
        //  TYPE 16: Threshold (uses child[0] mono input)
        // ===================================================================
        private static void EvalThreshold(TextureNode node, int[] output, int w, int row) {
            int thresh = node.IntParam0;
            int below = node.IntParam1;
            int above = node.IntParam2;
            if (node.Children != null && node.Children.Length >= 1 && node.Children[0] != null) {
                int[] child = GetMono(node.Children[0], row);
                for (int x = 0; x < w; x++)
                    output[x] = child[x] < thresh ? below : above;
            } else {
                for (int x = 0; x < w; x++)
                    output[x] = node.XCoord[x] < thresh ? below : above;
            }
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
        //  TYPE 19: Polar Distortion
        // ===================================================================
        private static readonly int[] _sinLUT = BuildSinCosLUT(false);
        private static readonly int[] _cosLUT = BuildSinCosLUT(true);

        private static int[] BuildSinCosLUT(bool isCos) {
            int[] lut = new int[256];
            for (int i = 0; i < 256; i++) {
                double angle = i * 2.0 * Math.PI / 256.0;
                lut[i] = (int)((isCos ? Math.Cos(angle) : Math.Sin(angle)) * 4096);
            }
            return lut;
        }

        private static void EvalPolarDistortionMono(TextureNode node, int[] output, int w, int row) {
            if (node.Children == null || node.Children.Length < 3) {
                Array.Fill(output, 2040, 0, w);
                return;
            }
            int scale = node.IntParam0 == 0 ? 32768 : node.IntParam0;
            int[] source = GetMono(node.Children[0], row);
            int[] angleField = GetMono(node.Children[1], row);
            int[] magField = GetMono(node.Children[2], row);

            for (int x = 0; x < w; x++) {
                int angle = (angleField[x] >> 4) & 0xFF;
                int mag = (scale * magField[x]) >> 12;
                int dx = (_sinLUT[angle] * mag) >> 12;
                int dy = (_cosLUT[angle] * mag) >> 12;
                int sx = ((x + (dx >> 12)) % w + w) % w;
                int sy = ((row + (dy >> 12)) % node.Height + node.Height) % node.Height;
                int[] srcRow = GetMono(node.Children[0], sy);
                output[x] = srcRow[sx];
            }
        }

        private static void EvalPolarDistortionColour(TextureNode node, int[][] output, int w, int row) {
            if (node.Children == null || node.Children.Length < 3 ||
                node.Children[0] == null || node.Children[1] == null || node.Children[2] == null) {
                Array.Fill(output[0], 2040, 0, w);
                Array.Fill(output[1], 2040, 0, w);
                Array.Fill(output[2], 2040, 0, w);
                return;
            }
            int scale = node.IntParam0 == 0 ? 32768 : node.IntParam0;
            int[] angleField = GetMono(node.Children[1], row);
            int[] magField = GetMono(node.Children[2], row);

            for (int x = 0; x < w; x++) {
                int angle = (angleField[x] >> 4) & 0xFF;
                int mag = (scale * magField[x]) >> 12;
                int dx = (_sinLUT[angle] * mag) >> 12;
                int dy = (_cosLUT[angle] * mag) >> 12;
                int sx = ((x + (dx >> 12)) % w + w) % w;
                int sy = ((row + (dy >> 12)) % node.Height + node.Height) % node.Height;
                int[][] srcRow = GetColour(node.Children[0], sy);
                for (int ch = 0; ch < 3; ch++)
                    output[ch][x] = srcRow[ch][sx];
            }
        }

        // ===================================================================
        //  TYPE 20: Tile/Scale (Hydra Sub29 — divides image into tiles)
        // ===================================================================
        private static void EvalTileMono(TextureNode node, int[] output, int w, int row) {
            if (node.Children == null || node.Children.Length < 1 || node.Children[0] == null) {
                Array.Fill(output, 2040, 0, w);
                return;
            }
            int xDiv = Math.Max(1, node.IntParam0);
            int yDiv = Math.Max(1, node.IntParam1);
            int tileW = w / xDiv;
            int tileH = node.Height / yDiv;
            int srcRow = tileH > 0 ? (row % tileH) * node.Height / tileH : 0;
            int[] childRow = GetMono(node.Children[0], srcRow);
            for (int x = 0; x < w; x++) {
                int srcX = tileW > 0 ? (x % tileW) * w / tileW : 0;
                if (srcX >= w) srcX = w - 1;
                output[x] = childRow[srcX];
            }
        }

        private static void EvalTileColour(TextureNode node, int[][] output, int w, int row) {
            if (node.Children == null || node.Children.Length < 1 || node.Children[0] == null) {
                Array.Fill(output[0], 2040, 0, w);
                Array.Fill(output[1], 2040, 0, w);
                Array.Fill(output[2], 2040, 0, w);
                return;
            }
            int xDiv = Math.Max(1, node.IntParam0);
            int yDiv = Math.Max(1, node.IntParam1);
            int tileW = w / xDiv;
            int tileH = node.Height / yDiv;
            int srcRow = tileH > 0 ? (row % tileH) * node.Height / tileH : 0;
            int[][] childRow = GetColour(node.Children[0], srcRow);
            for (int x = 0; x < w; x++) {
                int srcX = tileW > 0 ? (x % tileW) * w / tileW : 0;
                if (srcX >= w) srcX = w - 1;
                for (int ch = 0; ch < 3; ch++)
                    output[ch][x] = childRow[ch][srcX];
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
        //  TYPE 22: Invert (Hydra Sub39 — 4096 - value)
        // ===================================================================
        private static void EvalInvertMono(TextureNode node, int[] output, int w, int row) {
            if (node.Children == null || node.Children.Length < 1 || node.Children[0] == null) {
                Array.Fill(output, 2040, 0, w);
                return;
            }
            int[] child = GetMono(node.Children[0], row);
            for (int x = 0; x < w; x++)
                output[x] = FP_ONE - child[x];
        }

        private static void EvalInvertColour(TextureNode node, int[][] output, int w, int row) {
            if (node.Children == null || node.Children.Length < 1 || node.Children[0] == null) {
                Array.Fill(output[0], 2040, 0, w);
                Array.Fill(output[1], 2040, 0, w);
                Array.Fill(output[2], 2040, 0, w);
                return;
            }
            int[][] child = GetColour(node.Children[0], row);
            for (int ch = 0; ch < 3; ch++)
                for (int x = 0; x < w; x++)
                    output[ch][x] = FP_ONE - child[ch][x];
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

        private static void EvalFlipVColour(TextureNode node, int[][] output, int w, int row) {
            if (node.Children == null || node.Children.Length < 1 || node.Children[0] == null) {
                Array.Fill(output[0], 2040, 0, w);
                Array.Fill(output[1], 2040, 0, w);
                Array.Fill(output[2], 2040, 0, w);
                return;
            }
            int mirrorRow = node.Height - 1 - row;
            int[][] child = GetColour(node.Children[0], mirrorRow);
            for (int ch = 0; ch < 3; ch++)
                Array.Copy(child[ch], output[ch], w);
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
        //  TYPE 25: Colour key scale
        // ===================================================================
        /// <summary>
        /// Scales the three channels of any pixel that matches a key colour, and passes every
        /// other pixel through untouched.
        /// </summary>
        /// <remarks>
        /// <c>Node_Sub10_Sub14.method997</c>. The match is per channel against
        /// <c>anIntArray5609</c> with a single shared tolerance <c>anInt5604</c>; a pixel only
        /// counts as keyed when all three channels are inside it, and the moment one channel
        /// falls outside the client copies the remaining channels through verbatim.
        ///
        /// This node was implemented as a 256-entry curve remap, which is a different operation
        /// and one the decoder never fed - nothing populates <c>CurveData</c> for a type 25 node -
        /// so it degenerated into a pass-through. It was also not dispatched on the colour path
        /// at all, and the client has no monochrome variant of this node
        /// (<c>Node_Sub10_Sub14</c> declares <c>super(1, false)</c> and leaves
        /// <c>Node_Sub10.method990</c> to throw), so the colour path is the only one that ever
        /// runs. The single type 25 node in this cache lives in texture 911 and scales a grey
        /// input by 2867/1638/409 over 4096, which is what makes that texture brown.
        /// </remarks>
        private static void EvalColourKeyScale(TextureNode node, int[][] output, int w, int row) {
            if (node.Children == null || node.Children.Length < 1 || node.Children[0] == null) {
                Array.Fill(output[0], 2040, 0, w);
                Array.Fill(output[1], 2040, 0, w);
                Array.Fill(output[2], 2040, 0, w);
                return;
            }

            int[][] child = GetColour(node.Children[0], row);
            int tolerance = node.IntParam0;
            int scaleB = node.IntParam1, scaleG = node.IntParam2, scaleR = node.IntParam3;

            //Opcode 4 packs the key colour as a 24-bit medium, unpacked into 12-bit channels the
            //same way Node_Sub10_Sub33:454-456 unpacks a gradient marker.
            int key = node.IntParam4;
            int keyR = (key >> 12) & 4080, keyG = (key >> 4) & 4080, keyB = (key << 4) & 4080;

            int[] inR = child[0], inG = child[1], inB = child[2];
            int[] outR = output[0], outG = output[1], outB = output[2];

            for (int x = 0; x < w; x++) {
                int r = inR[x];
                if (Math.Abs(r - keyR) > tolerance) {
                    outR[x] = r; outG[x] = inG[x]; outB[x] = inB[x];
                    continue;
                }
                int g = inG[x];
                if (Math.Abs(g - keyG) > tolerance) {
                    outR[x] = r; outG[x] = g; outB[x] = inB[x];
                    continue;
                }
                int b = inB[x];
                if (Math.Abs(b - keyB) > tolerance) {
                    outR[x] = r; outG[x] = g; outB[x] = b;
                    continue;
                }
                outR[x] = (r * scaleR) >> 12;
                outG[x] = (g * scaleG) >> 12;
                outB[x] = (b * scaleB) >> 12;
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
        //  TYPE 30: Range remap (levels)
        // ===================================================================
        /// <summary>
        /// Rescales its input into the band [<c>IntParam0</c>, <c>IntParam1</c>].
        /// </summary>
        /// <remarks>
        /// This was implemented as a Sobel edge detector, which is not what
        /// <c>Node_Sub10_Sub10</c> does - it is the single expression
        /// <c>low + (input * (high - low) &gt;&gt; 12)</c>. The bounds default to 1024 and 3072,
        /// so a node that carries neither opcode still narrows its input rather than zeroing it.
        /// </remarks>
        private static void EvalRangeRemapMono(TextureNode node, int[] output, int w, int row) {
            if (node.Children == null || node.Children.Length < 1 || node.Children[0] == null) {
                Array.Fill(output, node.IntParam0, 0, w);
                return;
            }
            int low = node.IntParam0;
            int span = node.IntParam1 - low;
            int[] child = GetMono(node.Children[0], row);
            for (int x = 0; x < w; x++)
                output[x] = low + ((child[x] * span) >> 12);
        }

        private static void EvalRangeRemapColour(TextureNode node, int[][] output, int w, int row) {
            if (node.Children == null || node.Children.Length < 1 || node.Children[0] == null) {
                for (int ch = 0; ch < 3; ch++)
                    Array.Fill(output[ch], node.IntParam0, 0, w);
                return;
            }
            int low = node.IntParam0;
            int span = node.IntParam1 - low;
            int[][] child = GetColour(node.Children[0], row);
            for (int ch = 0; ch < 3; ch++)
                for (int x = 0; x < w; x++)
                    output[ch][x] = low + ((child[ch][x] * span) >> 12);
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
        //  TYPE 35: Normal/Bump Map
        // ===================================================================
        private static void EvalBumpMap(TextureNode node, int[] output, int w, int row) {
            if (node.Children == null || node.Children.Length < 1 || node.Children[0] == null) {
                Array.Fill(output, 2040, 0, w);
                return;
            }
            int intensity = Math.Max(1, node.IntParam0);
            int prevRow = ((row - 1) % node.Height + node.Height) % node.Height;
            int nextRow = ((row + 1) % node.Height + node.Height) % node.Height;
            int[] cur = GetMono(node.Children[0], row);
            int[] above = GetMono(node.Children[0], prevRow);
            int[] below = GetMono(node.Children[0], nextRow);

            for (int x = 0; x < w; x++) {
                int xl = ((x - 1) + w) % w;
                int xr = (x + 1) % w;
                long dX = (long)intensity * (cur[xr] - cur[xl]);
                long dY = (long)intensity * (below[x] - above[x]);
                long len = (long)Math.Sqrt((double)(dX * dX + dY * dY + (long)FP_ONE * FP_ONE));
                if (len == 0) len = 1;
                output[x] = (int)(FP_ONE - (long)FP_ONE * FP_ONE / len);
            }
        }

        // ===================================================================
        //  TYPE 36: Checkerboard
        // ===================================================================
        //Type 36 used to be evaluated here as a checkerboard generator. It is a nested texture
        //reference - see LoadNestedTextureForNode - and the invented checkerboard was what most
        //of the textures tab was actually showing.

        // ===================================================================
        //  TYPE 15: Worley (cellular) noise
        // ===================================================================
        /// <summary>
        /// Cellular noise: the distance from each pixel to the nearest jittered feature points
        /// on a coarse grid.
        /// </summary>
        /// <remarks>
        /// This node was implemented as Perlin gradient noise, which is a different algorithm
        /// producing a different picture. <c>Node_Sub10_Sub26</c> scatters one feature point per
        /// cell, keeps the four smallest distances over the 3x3 neighbourhood, and by default
        /// outputs the second minus the first - the classic cell-border look.
        /// </remarks>
        private static void EvalWorley(TextureNode node, int[] output, int w, int row) {
            byte[] perm = node.Permutation;
            int[] jitter = node.Jitter;
            if (perm == null || jitter == null) {
                Array.Fill(output, 0, 0, w);
                return;
            }

            int freqX = Math.Max(1, node.IntParam0);
            int freqY = Math.Max(1, node.IntParam1);
            int mode = node.IntParam4;
            int metric = node.IntParam5;

            int sy = 2048 + freqY * node.YCoord[row];
            int cyBase = sy >> 12;

            for (int x = 0; x < w; x++) {
                int d1 = int.MaxValue, d2 = int.MaxValue, d3 = int.MaxValue, d4 = int.MaxValue;
                int sx = 2048 + freqX * node.XCoord[x];
                int cxBase = sx >> 12;

                for (int cy = cyBase - 1; cy <= cyBase + 1; cy++) {
                    int wrappedY = cy >= freqY ? cy - freqY : cy;
                    int py = perm[0xff & wrappedY] & 0xff;

                    for (int cx = cxBase - 1; cx <= cxBase + 1; cx++) {
                        int wrappedX = cx >= freqX ? cx - freqX : cx;
                        int k = 2 * (perm[0xff & (wrappedX + py)] & 0xff);
                        int dx = sx - (cx << 12) - jitter[k];
                        int dy = sy - (cy << 12) - jitter[k + 1];
                        int dist = WorleyDistance(dx, dy, metric);

                        //Keep the four smallest distances, in order.
                        if (dist < d1) { d4 = d3; d3 = d2; d2 = d1; d1 = dist; }
                        else if (dist < d2) { d4 = d3; d3 = d2; d2 = dist; }
                        else if (dist >= d3) { if (dist < d4) d4 = dist; }
                        else { d4 = d3; d3 = dist; }
                    }
                }

                switch (mode) {
                    case 0: output[x] = d1; break;
                    case 1: output[x] = d2; break;
                    case 2: output[x] = d2 - d1; break;
                    case 3: output[x] = d3; break;
                    case 4: output[x] = d4; break;
                    default: break; // client leaves the row buffer untouched
                }
            }
        }

        private static int WorleyDistance(int dx, int dy, int metric) {
            switch (metric) {
                case 1: return (dx * dx + dy * dy) >> 12;                        // squared euclidean
                case 2: return Math.Abs(dx) + Math.Abs(dy);                      // manhattan
                case 3: return Math.Max(Math.Abs(dx), Math.Abs(dy));             // chebyshev
                case 4: {                                                        // minkowski p=1/2
                    double t = 4096.0 * Math.Sqrt(Math.Abs(dx) / 4096.0)
                             + 4096.0 * Math.Sqrt(Math.Abs(dy) / 4096.0);
                    return (int)(t * t) >> 12;
                }
                case 5:
                    return (int)(4096.0 * Math.Pow((dx * (double)dx + dy * (double)dy) / 16777216.0, 0.25));
                default:
                    return (int)(4096.0 * Math.Sqrt((dx * (double)dx + dy * (double)dy) / 16777216.0));
            }
        }

        // ===================================================================
        //  TYPE 34: Fractal Perlin noise
        // ===================================================================
        /// <summary>
        /// Sums octaves of 2D Perlin gradient noise, optionally recentring the signed result on
        /// mid-grey.
        /// </summary>
        /// <remarks>
        /// This is the node that used to emit a plain left-to-right ramp, because it was written
        /// against a <c>CurveData</c> field nothing ever assigns. It has no inputs and is one of
        /// the most common leaves in the cache, so almost every graph built on it was flat.
        /// </remarks>
        private static void EvalFractalNoise(TextureNode node, int[] output, int w, int row) {
            byte[] perm = node.Permutation;
            int[] amp = node.Amplitudes;
            int[] freq = node.Frequencies;
            if (perm == null || amp == null || freq == null) {
                Array.Fill(output, 2048, 0, w);
                return;
            }

            bool recentre = node.IntParam0 == 1;
            int octaves = Math.Min(node.IntParam1, Math.Min(amp.Length, freq.Length));
            int scaleX = node.IntParam3;
            int scaleY = node.IntParam4;
            int yFixed = scaleY * node.YCoord[row];

            bool wrote = false;
            for (int oct = 0; oct < octaves; oct++) {
                int amplitude = amp[oct];
                if (amplitude <= 8 && amplitude >= -8)
                    continue;

                int f = freq[oct] << 12;
                int xLimit = (f * scaleX) >> 12;
                int yLimit = (f * scaleY) >> 12;
                int yf = (f * yFixed) >> 12;
                int y0 = yf >> 12;
                int y1 = y0 + 1;
                if (y1 >= yLimit) y1 = 0;
                yf &= 0xfff;

                int permY0 = perm[y0 & 0xff] & 0xff;
                int permY1 = perm[y1 & 0xff] & 0xff;
                int smoothY = TextureNoise.Smooth[yf];
                bool last = recentre && oct == octaves - 1;

                for (int x = 0; x < w; x++) {
                    int xf = ((node.XCoord[x] * scaleX) * f) >> 12;
                    int v = PerlinCell(perm, smoothY, xLimit, xf, permY1, permY0, yf);
                    v = (v * amplitude) >> 12;
                    if (!wrote) output[x] = v;
                    else output[x] += v;
                    if (last) output[x] = 2048 + (output[x] >> 1);
                }
                wrote = true;
            }

            if (!wrote)
                Array.Fill(output, recentre ? 2048 : 0, 0, w);
        }

        /// <summary>
        /// One octave of 2D Perlin gradient noise - four corner gradients bilinearly blended
        /// through the smootherstep curve.
        /// </summary>
        private static int PerlinCell(byte[] perm, int smoothY, int xLimit, int xf, int permY1, int permY0, int yFrac) {
            int x0 = xf >> 12;
            int x1 = x0 + 1;
            if (x1 >= xLimit) x1 = 0;
            x0 &= 0xff;
            x1 &= 0xff;
            xf &= 0xfff;

            int xf1 = xf - 4096;
            int yf1 = yFrac - 4096;
            int smoothX = TextureNoise.Smooth[xf];

            int v00 = Gradient(perm[x0 + permY0] & 3, xf, yFrac);
            int v10 = Gradient(perm[x1 + permY0] & 3, xf1, yFrac);
            int lower = v00 + ((smoothX * (v10 - v00)) >> 12);

            int v01 = Gradient(perm[x0 + permY1] & 3, xf, yf1);
            int v11 = Gradient(perm[x1 + permY1] & 3, xf1, yf1);
            int upper = v01 + (((v11 - v01) * smoothX) >> 12);

            return lower + ((smoothY * (upper - lower)) >> 12);
        }

        /// <summary>The four diagonal gradient directions the client selects between.</summary>
        private static int Gradient(int selector, int dx, int dy) {
            switch (selector) {
                case 0: return dx + dy;
                case 1: return dy - dx;
                case 2: return dx - dy;
                default: return -dy - dx;
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
