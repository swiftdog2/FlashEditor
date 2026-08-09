using System;
using System.Collections.Generic;
using System.IO;
using FlashEditor.IO;

namespace FlashEditor.Definitions.Config {
    /// <summary>
    ///     A world map element: the sprite, label, polygon and right-click menu the world map and the
    ///     minimap draw for one point of interest.
    /// </summary>
    /// <remarks>
    ///     JS5 index 2, group <see cref="ConfigGroup.MapElement"/>. 1,051 files in the shipped 639
    ///     cache. Decoded by <c>Class24.method290</c> (Class24.java:399-425) dispatching to
    ///     <c>method288</c> (:209-373); the provider is <c>Class341</c>, which names the group at
    ///     Class341.java:141.
    ///     <para>
    ///     <b>How an object reaches one.</b> Object definition opcode <b>107</b> is a file id in this
    ///     group. <c>Class278.method3295</c> (:55-100) walks the world map's tile grid, resolves each
    ///     tile's object through index 16, applies the object's varbit/varp morph <i>first</i>, and
    ///     then takes the resulting id. Measured over all 56,199 object definitions: opcode 107
    ///     occurs on 170 objects with 144 distinct values in 225..1028, and every one of them is a
    ///     live file id here.
    ///     </para>
    ///     <para>
    ///     <c>Class341.method291</c> (:427-462) runs after every decode and derives a bounding box
    ///     from opcode 15's polygon. That is a post-decode transform rather than part of the format,
    ///     so it is not done here for the same reason
    ///     <see cref="FloorOverlayDefinition.ApplyPriorityComposite"/> is not done in that decoder:
    ///     applying it at decode would make the encoder write the derived values back.
    ///     </para>
    ///     <para>
    ///     Six opcodes - 5, 16, 18, 23, 24 and 249 - occur in no file of this cache. They are
    ///     implemented from the client anyway, and a passing byte-identity sweep says nothing at all
    ///     about them.
    ///     </para>
    /// </remarks>
    public sealed class MapElementDefinition : ConfigDefinition {
        /// <summary>Opcode 1. Sprite group in JS5 index 8, or -1 for none.</summary>
        /// <remarks>
        ///     <c>Class24.method287</c> (:175-207) hands it to
        ///     <c>Class324.method3685(aClass341_233.aJS5Archive_2852, ...)</c>, and that archive is
        ///     the one InterfaceSettings.java:273-274 constructs <c>Class341</c> with, which is index
        ///     8. The measured maximum here is 1784 against index 8's 4,593 groups.
        /// </remarks>
        public int SpriteId { get; set; } = -1;

        /// <summary>Opcode 2. Sprite drawn instead of <see cref="SpriteId"/> while hovered.</summary>
        /// <remarks>The selector is <c>Node_Sub47.aBoolean4275</c> (Node_Sub40.java:116-119).</remarks>
        public int HighlightedSpriteId { get; set; } = -1;

        /// <summary>Opcode 3. The label drawn beside the element on the world map.</summary>
        /// <remarks>Broken into lines and drawn at Node_Sub40.java:154-158.</remarks>
        public string? Label { get; set; }

        /// <summary>Opcode 4. Label colour, 24-bit RGB.</summary>
        public int LabelRgb { get; set; }

        /// <summary>Opcode 5. Label colour used when the placement carries a flag, 24-bit RGB.</summary>
        /// <remarks>Selected at Class126.java:48-49. Occurs in no file of this cache.</remarks>
        public int FlaggedLabelRgb { get; set; } = -1;

        /// <summary>Opcode 6. Font the label is drawn in.</summary>
        /// <remarks><c>Class105.method1718(anInt264, 5466)</c> at Node_Sub40.java:155. Values 0, 1, 2.</remarks>
        public int FontId { get; set; }

        /// <summary>Opcode 7. A bitfield, kept raw.</summary>
        /// <remarks>
        ///     Class24.java:340-348 reads two bits out of it and ignores the rest: bit 1 set means
        ///     <c>aBoolean230 = true</c>, bit 0 <b>clear</b> means <c>aBoolean258 = false</c>. Kept as
        ///     the stored byte because the unread bits have no field to survive in, and because two
        ///     different bytes can produce the same pair of booleans. Measured values 2 and 3.
        ///     <para>
        ///     The default of 1 is the byte that would reproduce the constructor's state - bit 0 set
        ///     so <c>aBoolean258</c> stays true, bit 1 clear so <c>aBoolean230</c> stays false - which
        ///     is what makes "absent" and "stored as 1" indistinguishable to the client and why
        ///     presence is read off the opcode list instead.
        ///     </para>
        /// </remarks>
        public int Flags { get; set; } = 1;

        /// <summary>
        ///     Opcode 8. Whether the element is drawn on the minimap, as the stored byte.
        /// </summary>
        /// <remarks>
        ///     The client tests <c>readUnsignedByte() == 1</c> (Class24.java:221) and the minimap
        ///     gates on the result (Node_Sub10_Sub5.java:196-198), so every byte other than 1 means
        ///     the same thing. Kept raw for that reason - the decoded boolean cannot say which of
        ///     them was stored. Default 1, matching <c>aBoolean261 = true</c>.
        /// </remarks>
        public int MinimapVisibleByte { get; set; } = 1;

        /// <summary>Whether the minimap draws this element, as the client reads opcode 8.</summary>
        public bool DrawnOnMinimap => MinimapVisibleByte == 1;

        /// <summary>Opcode 9, first field. Varbit whose value gates the world map draw, or -1.</summary>
        /// <remarks>
        ///     <c>Class24.method284</c> (:78-119) resolves it through <c>Interface6</c>, whose only
        ///     implementation is <c>Class140</c>: <c>method7</c> (:192-208) resolves a varbit through
        ///     index 22 and <c>method6</c> (:183-190) reads a varp directly. The varp wins when it is
        ///     set. Stored 65535 means -1.
        /// </remarks>
        public int VisibleVarbitId { get; set; } = -1;

        /// <summary>Opcode 9, second field. Varp read instead of the varbit, or -1.</summary>
        public int VisibleVarpId { get; set; } = -1;

        /// <summary>Opcode 9, third field. Lowest value of the gate that still draws.</summary>
        public int VisibleMin { get; set; }

        /// <summary>Opcode 9, fourth field. Highest value of the gate that still draws.</summary>
        public int VisibleMax { get; set; }

        /// <summary>Opcodes 10 to 14. The right-click menu options, index 0 drawn last.</summary>
        /// <remarks>Walked 4 down to 0 by Particle_Sub4.java:75-88.</remarks>
        public string?[] MenuActions { get; } = new string?[5];

        /// <summary>Opcode 15. Polygon vertices in world tiles, x and y interleaved.</summary>
        /// <remarks>
        ///     <c>Class278.method3314</c> (:787-843) offsets each pair by the placement's world
        ///     position, fills the shape with <see cref="PolygonFillArgb"/> and draws each edge in
        ///     the colour <see cref="PolygonEdgeColourIndices"/> selects, wrapping the last vertex
        ///     back to the first. Measured over the 69 records that carry it: 4 to 11 vertices,
        ///     coordinates -128..384.
        /// </remarks>
        public int[]? PolygonVertices { get; set; }

        /// <summary>Opcode 15. Fill colour of the polygon, signed 32-bit ARGB.</summary>
        public int PolygonFillArgb { get; set; }

        /// <summary>Opcode 15. The edge colour table, signed 32-bit ARGB each.</summary>
        /// <remarks>
        ///     Every record in this cache carries exactly one entry, so the table is exercised only
        ///     in its degenerate form here.
        /// </remarks>
        public int[]? PolygonEdgeArgb { get; set; }

        /// <summary>
        ///     Opcode 15. Which entry of <see cref="PolygonEdgeArgb"/> colours each edge.
        /// </summary>
        /// <remarks>One per vertex, signed. Every one of the 344 stored here is 0.</remarks>
        public sbyte[]? PolygonEdgeColourIndices { get; set; }

        /// <summary>Opcode 16. Whether the element is drawn at all. Defaults to true.</summary>
        /// <remarks>
        ///     <c>aBoolean241</c>, checked before the visibility gate on both draw paths
        ///     (Class256_Sub1.java:58, Particle_Sub3.java:20). Occurs in no file of this cache.
        /// </remarks>
        public bool Rendered { get; set; } = true;

        /// <summary>Opcode 17. The menu target the options act on.</summary>
        /// <remarks>Passed alongside <see cref="CategoryId"/> at Particle_Sub4.java:78.</remarks>
        public string? MenuTarget { get; set; }

        /// <summary>Opcode 18. A third sprite group in JS5 index 8, or -1.</summary>
        /// <remarks>
        ///     <c>anInt231</c>, loaded by <c>Class24.method286</c> (:147-172) out of the same archive
        ///     as opcodes 1 and 2. What selects it over those two is not settled here, and it occurs
        ///     in no file of this cache.
        /// </remarks>
        public int AlternateSpriteId { get; set; } = -1;

        /// <summary>Opcode 19. The category the world map filters and highlights on.</summary>
        /// <remarks>
        ///     Class202.java:237 draws the highlight marker on every element whose value equals the
        ///     one active category, and Particle_Sub4.java:78 passes it as the menu action's id. CS2
        ///     opcode 6803 returns it (Class247.java:7300).
        /// </remarks>
        public int CategoryId { get; set; } = -1;

        /// <summary>Opcode 20, first field. Second gate's varbit, or -1.</summary>
        /// <remarks>Evaluated exactly as opcode 9's, as a second condition.</remarks>
        public int SecondVisibleVarbitId { get; set; } = -1;

        /// <summary>Opcode 20, second field. Second gate's varp, or -1.</summary>
        public int SecondVisibleVarpId { get; set; } = -1;

        /// <summary>Opcode 20, third field. Second gate's lowest passing value.</summary>
        public int SecondVisibleMin { get; set; }

        /// <summary>Opcode 20, fourth field. Second gate's highest passing value.</summary>
        public int SecondVisibleMax { get; set; }

        /// <summary>Opcode 21. Colour of the filled rectangle drawn for the element, signed ARGB.</summary>
        /// <remarks>
        ///     <c>RenderType.method1781</c> at Class103.java:81. Signed: the one value stored in this
        ///     cache is -5276401, which an unsigned field would round-trip and read wrong.
        /// </remarks>
        public int FillArgb { get; set; }

        /// <summary>Opcode 22. Colour of the element's outline, signed ARGB.</summary>
        /// <remarks><c>method1760</c> at Class103.java:77. Files 779 and 780 store this opcode twice.</remarks>
        public int OutlineArgb { get; set; }

        /// <summary>Opcode 23, first field. Edge line width; hairlines when below 1.</summary>
        /// <remarks>Class164.java:79 and Class278.java:818,838. Occurs in no file of this cache.</remarks>
        public int LineWidth { get; set; } = -1;

        /// <summary>Opcode 23, second field. A line parameter that is not settled here.</summary>
        /// <remarks><c>anInt253</c>. Occurs in no file of this cache.</remarks>
        public int LineParameterA { get; set; } = -1;

        /// <summary>Opcode 23, third field. A line parameter that is not settled here.</summary>
        /// <remarks><c>anInt224</c>. Occurs in no file of this cache.</remarks>
        public int LineParameterB { get; set; } = -1;

        /// <summary>Opcode 24, first field. Label offset along x, signed.</summary>
        /// <remarks>Scaled into screen space at Node_Sub40.java:159-161. Occurs in no file here.</remarks>
        public int LabelOffsetX { get; set; }

        /// <summary>Opcode 24, second field. Label offset along y, signed.</summary>
        public int LabelOffsetY { get; set; }

        /// <summary>Opcode 249. The parameter block, in stored order.</summary>
        /// <remarks>Read by CS2 opcode 6804. Occurs in no file of this cache.</remarks>
        public List<ConfigParameter> Parameters { get; } = new List<ConfigParameter>();

        /// <summary>Decodes one map element definition.</summary>
        /// <param name="stream">The definition file.</param>
        /// <returns>This definition.</returns>
        public MapElementDefinition DecodeFrom(JagStream stream) {
            Decode(stream);
            return this;
        }

        /// <inheritdoc/>
        protected override void ReadPayload(int opcode, JagStream stream) {
            switch (opcode) {
                case 1: SpriteId = stream.ReadUnsignedShort(); break;
                case 2: HighlightedSpriteId = stream.ReadUnsignedShort(); break;
                case 3: Label = stream.ReadJagexString(); break;
                case 4: LabelRgb = stream.ReadMedium(); break;
                case 5: FlaggedLabelRgb = stream.ReadMedium(); break;
                case 6: FontId = stream.ReadUnsignedByte(); break;
                case 7: Flags = stream.ReadUnsignedByte(); break;
                case 8: MinimapVisibleByte = stream.ReadUnsignedByte(); break;

                case 9:
                    VisibleVarbitId = ShortOrMinusOne(stream);
                    VisibleVarpId = ShortOrMinusOne(stream);
                    VisibleMin = stream.ReadInt();
                    VisibleMax = stream.ReadInt();
                    break;

                case 10:
                case 11:
                case 12:
                case 13:
                case 14:
                    MenuActions[opcode - 10] = stream.ReadJagexString();
                    break;

                case 15: ReadPolygon(stream); break;
                case 16: Rendered = false; break;
                case 17: MenuTarget = stream.ReadJagexString(); break;
                case 18: AlternateSpriteId = stream.ReadUnsignedShort(); break;
                case 19: CategoryId = stream.ReadUnsignedShort(); break;

                case 20:
                    SecondVisibleVarbitId = ShortOrMinusOne(stream);
                    SecondVisibleVarpId = ShortOrMinusOne(stream);
                    SecondVisibleMin = stream.ReadInt();
                    SecondVisibleMax = stream.ReadInt();
                    break;

                case 21: FillArgb = stream.ReadInt(); break;
                case 22: OutlineArgb = stream.ReadInt(); break;

                case 23:
                    LineWidth = stream.ReadUnsignedByte();
                    LineParameterA = stream.ReadUnsignedByte();
                    LineParameterB = stream.ReadUnsignedByte();
                    break;

                case 24:
                    LabelOffsetX = stream.ReadShort();
                    LabelOffsetY = stream.ReadShort();
                    break;

                case 249: ConfigParameters.Read(stream, Parameters); break;

                default:
                    //The client's dispatcher is a chain of equality tests with no final else, so an
                    //opcode it does not name consumes nothing and desynchronises the rest of the
                    //record. Refusing is strictly better.
                    throw Unknown(opcode);
            }
        }

        /// <inheritdoc/>
        protected override void WritePayload(int opcode, JagStream stream) {
            switch (opcode) {
                case 1: stream.WriteShort(SpriteId); break;
                case 2: stream.WriteShort(HighlightedSpriteId); break;
                case 3: stream.WriteJagexString(Label ?? ""); break;
                case 4: stream.WriteMedium(LabelRgb); break;
                case 5: stream.WriteMedium(FlaggedLabelRgb); break;
                case 6: stream.WriteByte(FontId); break;
                case 7: stream.WriteByte(Flags); break;
                case 8: stream.WriteByte(MinimapVisibleByte); break;

                case 9:
                    WriteShortOrMinusOne(stream, VisibleVarbitId);
                    WriteShortOrMinusOne(stream, VisibleVarpId);
                    stream.WriteInteger(VisibleMin);
                    stream.WriteInteger(VisibleMax);
                    break;

                case 10:
                case 11:
                case 12:
                case 13:
                case 14:
                    stream.WriteJagexString(MenuActions[opcode - 10] ?? "");
                    break;

                case 15: WritePolygon(stream); break;
                case 16: break;
                case 17: stream.WriteJagexString(MenuTarget ?? ""); break;
                case 18: stream.WriteShort(AlternateSpriteId); break;
                case 19: stream.WriteShort(CategoryId); break;

                case 20:
                    WriteShortOrMinusOne(stream, SecondVisibleVarbitId);
                    WriteShortOrMinusOne(stream, SecondVisibleVarpId);
                    stream.WriteInteger(SecondVisibleMin);
                    stream.WriteInteger(SecondVisibleMax);
                    break;

                case 21: stream.WriteInteger(FillArgb); break;
                case 22: stream.WriteInteger(OutlineArgb); break;

                case 23:
                    stream.WriteByte(LineWidth);
                    stream.WriteByte(LineParameterA);
                    stream.WriteByte(LineParameterB);
                    break;

                case 24:
                    stream.WriteShort(LabelOffsetX);
                    stream.WriteShort(LabelOffsetY);
                    break;

                case 249: ConfigParameters.Write(stream, Parameters); break;

                default: throw Unknown(opcode);
            }
        }

        /// <inheritdoc/>
        protected override IEnumerable<int> AddedOpcodes() {
            if (!Has(1) && SpriteId != -1) yield return 1;
            if (!Has(2) && HighlightedSpriteId != -1) yield return 2;
            if (!Has(3) && Label != null) yield return 3;
            if (!Has(4) && LabelRgb != 0) yield return 4;
            if (!Has(5) && FlaggedLabelRgb != -1) yield return 5;
            if (!Has(6) && FontId != 0) yield return 6;
            if (!Has(7) && Flags != 1) yield return 7;
            if (!Has(8) && MinimapVisibleByte != 1) yield return 8;
            if (!Has(9) && (VisibleVarbitId != -1 || VisibleVarpId != -1)) yield return 9;

            for (int action = 0; action < MenuActions.Length; action++)
                if (!Has(10 + action) && MenuActions[action] != null)
                    yield return 10 + action;

            if (!Has(15) && PolygonVertices != null) yield return 15;
            if (!Has(16) && !Rendered) yield return 16;
            if (!Has(17) && MenuTarget != null) yield return 17;
            if (!Has(18) && AlternateSpriteId != -1) yield return 18;
            if (!Has(19) && CategoryId != -1) yield return 19;
            if (!Has(20) && (SecondVisibleVarbitId != -1 || SecondVisibleVarpId != -1)) yield return 20;
            if (!Has(21) && FillArgb != 0) yield return 21;
            if (!Has(22) && OutlineArgb != 0) yield return 22;
            if (!Has(23) && (LineWidth != -1 || LineParameterA != -1 || LineParameterB != -1)) yield return 23;
            if (!Has(24) && (LabelOffsetX != 0 || LabelOffsetY != 0)) yield return 24;
            if (!Has(249) && Parameters.Count > 0) yield return 249;
        }

        /// <summary>Reads opcode 15's four blocks in the order Class24.java:311-337 reads them.</summary>
        /// <param name="stream">The definition file, positioned at the vertex count.</param>
        private void ReadPolygon(JagStream stream) {
            int vertexCount = stream.ReadUnsignedByte();

            int[] coordinates = new int[vertexCount * 2];
            for (int i = 0; i < coordinates.Length; i++)
                coordinates[i] = stream.ReadShort();

            int fill = stream.ReadInt();

            int[] edgeColours = new int[stream.ReadUnsignedByte()];
            for (int i = 0; i < edgeColours.Length; i++)
                edgeColours[i] = stream.ReadInt();

            sbyte[] indices = new sbyte[vertexCount];
            for (int i = 0; i < indices.Length; i++)
                indices[i] = stream.ReadSignedByte();

            PolygonVertices = coordinates;
            PolygonFillArgb = fill;
            PolygonEdgeArgb = edgeColours;
            PolygonEdgeColourIndices = indices;
        }

        /// <summary>Writes opcode 15's four blocks.</summary>
        /// <remarks>
        ///     The vertex count is written once and sizes two arrays, so an edit that resizes one
        ///     without the other cannot be encoded at all. It is refused rather than padded: the
        ///     record would otherwise be readable and describe a shape nobody asked for.
        /// </remarks>
        /// <param name="stream">The stream to write to.</param>
        private void WritePolygon(JagStream stream) {
            int[] vertices = PolygonVertices ?? Array.Empty<int>();
            int[] edgeColours = PolygonEdgeArgb ?? Array.Empty<int>();
            sbyte[] indices = PolygonEdgeColourIndices ?? Array.Empty<sbyte>();

            if ((vertices.Length & 1) != 0)
                throw new InvalidDataException("Map element " + Id +
                    " has an odd number of polygon coordinates; they are x,y pairs.");

            int count = vertices.Length / 2;
            if (indices.Length != count)
                throw new InvalidDataException("Map element " + Id + " has " + count +
                    " polygon vertices but " + indices.Length + " edge colour indices; opcode 15 " +
                    "stores one count for both.");

            stream.WriteByte(count);
            foreach (int coordinate in vertices)
                stream.WriteShort(coordinate);

            stream.WriteInteger(PolygonFillArgb);

            stream.WriteByte(edgeColours.Length);
            foreach (int colour in edgeColours)
                stream.WriteInteger(colour);

            foreach (sbyte index in indices)
                stream.WriteSignedByte(index);
        }

        /// <summary>Reads a varbit or varp id, mapping the stored 65535 to -1 as the client does.</summary>
        /// <param name="stream">The definition file.</param>
        /// <returns>The id, or -1.</returns>
        private static int ShortOrMinusOne(JagStream stream) {
            int value = stream.ReadUnsignedShort();
            return value == 0xFFFF ? -1 : value;
        }

        /// <summary>Writes back the 65535 the client reads as -1.</summary>
        /// <remarks>
        ///     -1 has exactly one encoding in this field, so the alias is safe in one direction only:
        ///     writing a truncated -1 would emit 0xFFFF by accident and writing it as anything else
        ///     would change the meaning.
        /// </remarks>
        /// <param name="stream">The stream to write to.</param>
        /// <param name="value">The id, or -1.</param>
        private static void WriteShortOrMinusOne(JagStream stream, int value) {
            stream.WriteShort(value == -1 ? 0xFFFF : value);
        }
    }
}
