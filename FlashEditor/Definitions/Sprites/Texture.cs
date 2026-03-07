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

        /// <summary>Sprite file IDs referenced by this texture.</summary>
        public int[] FileIds => _spriteIds;

        /// <summary>Number of sprite references.</summary>
        public int Count => _spriteIds.Length;

        /// <summary>The parsed texture graph for evaluation.</summary>
        public TextureGraph Graph => _graph;

        /// <summary>Returns the sprite file ID at the specified index.</summary>
        public int GetFileId(int index) => _spriteIds[index];

        // Node type → child input count (from super() calls in client)
        private static readonly int[] ChildCounts = {
        //  0  1  2  3  4  5  6  7  8  9 10 11 12 13 14 15 16 17 18 19
            0, 0, 0, 0, 0, 1, 1, 2, 1, 1, 1, 1, 0, 0, 0, 0, 0, 1, 0, 3,
        // 20 21 22 23 24 25 26 27 28 29 30 31 32 33 34 35 36 37 38 39
            1, 3, 1, 1, 1, 1, 1, 0, 0, 0, 1, 0, 1, 1, 0, 1, 0, 0, 0, 0,
        };

        /// <summary>
        /// Decodes a texture graph, building the full node graph and extracting sprite IDs.
        /// </summary>
        public static Texture Decode(JagStream buffer)
        {
            var tex = new Texture();
            var spriteIds = new List<int>();

            int nodeCount = buffer.ReadUnsignedByte();
            var nodes = new TextureNode[nodeCount];

            for (int n = 0; n < nodeCount; n++)
            {
                var node = new TextureNode();
                nodes[n] = node;

                // Node header (Node_Sub46_Sub11.method1581)
                buffer.ReadUnsignedByte(); // version byte (discarded)
                int nodeType = buffer.ReadUnsignedByte();
                node.Type = nodeType;
                buffer.ReadUnsignedByte(); // output size (anInt3860)
                int opcodeCount = buffer.ReadUnsignedByte();

                // Decode each opcode into config fields
                bool bail = false;
                for (int op = 0; op < opcodeCount; op++)
                {
                    int opcode = buffer.ReadUnsignedByte();

                    if (!DecodeNodeOpcode(node, nodeType, opcode, buffer))
                    {
                        Debug($"Texture graph: unknown opcode {opcode} for node type {nodeType}, bailing", LOG_DETAIL.ADVANCED);
                        bail = true;
                        break;
                    }
                }

                if (bail)
                {
                    // Collect sprite IDs from whatever we parsed so far
                    foreach (var nd in nodes)
                        if (nd != null && nd.SpriteId >= 0)
                            spriteIds.Add(nd.SpriteId);
                    tex._spriteIds = spriteIds.ToArray();
                    tex._graph = null;
                    return tex;
                }

                // Read child connection indices
                int childCount = nodeType < ChildCounts.Length ? ChildCounts[nodeType] : 0;
                node.ChildIndices = new int[childCount];
                for (int c = 0; c < childCount; c++)
                    node.ChildIndices[c] = buffer.ReadUnsignedByte();

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
            }

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
                    if (opcode == 0) { node.IntParam0 = buf.ReadUnsignedByte() << 4; return true; }
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

                case 5: // Brightness (Sub24)
                    if (opcode == 0) { node.IntParam0 = buf.ReadUnsignedByte() << 4; return true; }
                    if (opcode == 1) { node.IntParam1 = buf.ReadUnsignedByte(); return true; }
                    if (opcode == 2) { node.IntParam2 = buf.ReadUnsignedByte(); return true; }
                    return false;

                case 6: // Blend mono (Sub15)
                    if (opcode == 0) { node.IntParam0 = buf.ReadUnsignedShort(); return true; }
                    if (opcode == 1) { node.IntParam1 = buf.ReadUnsignedShort(); return true; }
                    if (opcode == 2) { node.BlendMode = buf.ReadUnsignedByte(); return true; }
                    return false;

                case 7: // ColourBlend (Sub7)
                    if (opcode == 0) { node.IntParam0 = buf.ReadUnsignedByte() << 4; return true; }
                    if (opcode == 1) { node.BlendMode = buf.ReadUnsignedByte(); return true; }
                    return false;

                case 8: // ColourRamp (Sub9) — variable opcode 0
                    if (opcode == 0)
                    {
                        node.GradientPreset = buf.ReadUnsignedByte();
                        int count = buf.ReadUnsignedByte();
                        node.GradientCount = count;
                        node.GradientData = new int[count][];
                        for (int i = 0; i < count; i++)
                        {
                            node.GradientData[i] = new int[4];
                            node.GradientData[i][0] = buf.ReadUnsignedShort(); // position
                            int packed = buf.ReadUnsignedShort(); // packed RGB565 or similar
                            // The second short is the colour packed as RGB
                            node.GradientData[i][1] = (packed >> 10) & 0x1F;  // R 5-bit
                            node.GradientData[i][2] = (packed >> 5) & 0x1F;   // G 5-bit
                            node.GradientData[i][3] = packed & 0x1F;          // B 5-bit
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
                    return false;

                case 14: // SineWave (Sub17)
                    if (opcode == 0) { node.IntParam0 = buf.ReadUnsignedShort(); return true; }
                    return false;

                case 15: // PerlinNoise (Sub26)
                    if (opcode == 0) { node.IntParam0 = buf.ReadUnsignedByte(); return true; } // octaves
                    if (opcode == 1) { node.IntParam1 = buf.ReadUnsignedByte(); return true; } // freq
                    if (opcode == 2) { node.IntParam2 = buf.ReadUnsignedShort(); return true; } // seed
                    if (opcode == 3) { node.IntParam3 = buf.ReadUnsignedByte(); return true; }
                    if (opcode == 4) { node.IntParam4 = buf.ReadUnsignedByte(); return true; }
                    if (opcode == 5) { node.IntParam5 = buf.ReadUnsignedByte(); return true; }
                    if (opcode == 6) { node.IntParam6 = buf.ReadUnsignedByte(); return true; }
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

                case 19: // Weave (Sub2)
                    if (opcode == 0) { node.IntParam0 = buf.ReadUnsignedShort(); return true; }
                    if (opcode == 1) { node.IntParam1 = buf.ReadUnsignedByte(); return true; }
                    return false;

                case 20: // Clamp (Sub29)
                    if (opcode == 0) { node.IntParam0 = buf.ReadUnsignedByte() << 4; return true; }
                    if (opcode == 1) { node.IntParam1 = buf.ReadUnsignedByte() << 4; return true; }
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

                case 25: // CurveRemap (Sub14)
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

                case 29: // Factory (Sub36) — cannot parse opcode 0
                    if (opcode == 1) { node.IntParam0 = buf.ReadUnsignedByte(); return true; }
                    if (opcode == 0) return false;
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

                case 33: // Offset/Scroll (Sub20)
                    if (opcode == 0) { node.IntParam0 = buf.ReadUnsignedShort(); return true; }
                    if (opcode == 1) { node.IntParam1 = buf.ReadUnsignedByte(); return true; }
                    return false;

                case 34: // CurveRemap2 (Sub35) — opcode 2 is variable/unparseable
                    if (opcode == 0) { node.IntParam0 = buf.ReadUnsignedByte(); return true; }
                    if (opcode == 1) { node.IntParam1 = buf.ReadUnsignedByte(); return true; }
                    if (opcode == 2) return false; // cannot reliably parse
                    if (opcode >= 3 && opcode <= 6) { SetIntParam(node, opcode - 1, buf.ReadUnsignedByte()); return true; }
                    return false;

                case 35: // Scale (Sub1)
                    if (opcode == 0) { node.IntParam0 = buf.ReadUnsignedShort(); return true; }
                    return false;

                case 36: // Checkerboard (Sub25)
                    if (opcode == 0) { node.IntParam0 = buf.ReadUnsignedShort(); return true; }
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
