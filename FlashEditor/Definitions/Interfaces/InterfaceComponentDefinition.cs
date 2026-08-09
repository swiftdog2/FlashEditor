using System;
using System.Collections.Generic;
using FlashEditor.Cache;
using FlashEditor.IO;

namespace FlashEditor.Definitions.Interfaces {
    /// <summary>
    ///     One interface component - a file of index 3, where a group is one interface.
    /// </summary>
    /// <remarks>
    ///     Ported from <c>RSInterface.unpackConfig</c> (<c>RSInterface.java:1032-1343</c>) and its two
    ///     helpers <c>loadCS2Bytecode</c> (<c>:398-427</c>) and <c>method3473</c> (<c>:981-1001</c>).
    ///     The method's second argument is obfuscation noise: it is passed as -947
    ///     (<c>EntityEnumType.java:54</c>) and every arithmetic use of it inside - <c>i + 947</c>,
    ///     <c>i ^ i</c>, <c>i ^ ~0x3b2</c> - evaluates to 0, so nothing here takes one.
    ///     <para>
    ///     <b>The decompiled field names are shifted by one against the read order.</b> The client's
    ///     <c>x</c> is the enum-like content type, <c>y</c> is the base X, <c>width</c> is the base Y,
    ///     <c>height</c> is the base width and <c>anInt2242</c> is the base height. Settled from what
    ///     the CS2 setters do with them - <c>if_setposition</c> writes <c>y</c> and <c>width</c>
    ///     (<c>Class247.java:420-423</c>) and <c>if_setsize</c> writes <c>height</c> and
    ///     <c>anInt2242</c> (<c>:453-456</c>) - never from the identifiers, which would pair an
    ///     unsigned X with a signed Y. The 639 data agrees independently: the two signed reads are
    ///     the two fields that go negative and the two unsigned reads are the two that exceed 32,767.
    ///     </para>
    ///     <para>
    ///     <b>Three modifications in the client's own load path are deliberately not ported</b>,
    ///     because they are that client's local hacks rather than the format, and all three mutate
    ///     decoded fields after <c>unpackConfig</c> returns - so copying any of them breaks a
    ///     byte-identity re-encode. They are <c>EntityEnumType.java:40</c> (asking for group 61 serves
    ///     group 259), <c>:55-59</c> (group 408 with a media id in 15,285..15,289 has its zoom forced
    ///     to 3,700 and its Y offset raised by 5) and <c>:61-63</c> (media id 27,167 is forced
    ///     hidden). Two more of the same kind sit outside the load path: the live
    ///     <c>System.err.println</c> at <c>RSInterface.java:1252</c>, and
    ///     <c>Class111_Sub3.java:86-90</c> forcing the drag parameters of the inventory container.
    ///     </para>
    ///     <para>
    ///     <b>What is stored raw, and why.</b> Several fields have more than one byte sequence that
    ///     decodes to the same value, so a decoder that kept only the decoded value could not put the
    ///     file back. Each is marked on its property. None of them is exercised by this cache, which
    ///     is exactly why they are kept: a byte-identity sweep over index 3 is evidence about none of
    ///     them.
    ///     </para>
    /// </remarks>
    public sealed class InterfaceComponentDefinition {
        /// <summary>Hook arrays in the wire format, excluding the version-gated twenty-first.</summary>
        public const int HookCount = 20;

        /// <summary>Trigger integer arrays in the wire format.</summary>
        public const int TriggerCount = 5;

        /// <summary>The version byte value that means "if3", the only one this cache uses.</summary>
        public const int If3Version = 255;

        /// <summary>The stored parent id that means "no parent".</summary>
        public const int NoParent = 65535;

        /// <summary>The stored short that means "none" on every 16-bit reference in the format.</summary>
        public const int NoReference = 65535;

        /// <summary>
        ///     Which bits of the access mask open the three trailing shorts.
        /// </summary>
        /// <remarks>
        ///     <c>RSInterface.java:1259</c> calls <c>aa_Sub3.method157(mask, 64)</c>, which is
        ///     <c>(0x3ff26 &amp; i) &gt;&gt; 11</c> (<c>aa_Sub3.java:27-37</c>). The shift discards
        ///     bits 0..10, so the surviving mask is exactly this.
        /// </remarks>
        public const int TargetGateMask = 0x3F800;

        /// <summary>Binds a component to the address it is stored at.</summary>
        /// <param name="groupId">The interface, which is the group id.</param>
        /// <param name="fileId">The component, which is the file id.</param>
        public InterfaceComponentDefinition(int groupId, int fileId) {
            if (groupId < 0)
                throw new ArgumentOutOfRangeException(nameof(groupId), groupId, "Group ids are non-negative.");
            if (fileId < 0)
                throw new ArgumentOutOfRangeException(nameof(fileId), fileId, "File ids are non-negative.");

            GroupId = groupId;
            FileId = fileId;
        }

        /// <summary>
        ///     Binds a component to a folded component id.
        /// </summary>
        /// <remarks>
        ///     A named factory rather than a one-argument constructor, because the alternative sits
        ///     beside <c>(groupId, fileId)</c> and one dropped argument would silently read a folded
        ///     id as a group. Split through <see cref="CacheAddressing"/> rather than by an
        ///     open-coded shift, so the page size is stated once. The client folds the same pair the
        ///     same way - <c>ID_TAG = (parent &lt;&lt; 16) + childIndex</c> at
        ///     <c>EntityEnumType.java:46</c>.
        /// </remarks>
        /// <param name="componentId">The folded id.</param>
        /// <returns>An empty definition bound to that address.</returns>
        public static InterfaceComponentDefinition FromComponentId(int componentId) {
            CacheAddressing addressing = CacheAddressing.For(RSConstants.INTERFACE_DEFINITIONS_INDEX);
            return new InterfaceComponentDefinition(addressing.GroupOf(componentId),
                addressing.FileOf(componentId));
        }

        // ===================================================================
        //  Address
        // ===================================================================

        /// <summary>The interface this component belongs to.</summary>
        public int GroupId { get; }

        /// <summary>The component's index within its interface.</summary>
        public int FileId { get; }

        /// <summary>The folded component id the client and CS2 both address it by.</summary>
        public int ComponentId => (GroupId << 16) | FileId;

        // ===================================================================
        //  Header
        // ===================================================================

        /// <summary>
        ///     The version byte exactly as stored.
        /// </summary>
        /// <remarks>
        ///     255 is the if3 marker and decodes to -1. Every one of the 42,256 files in this cache
        ///     stores 255, which is why six branches below can never fire here.
        /// </remarks>
        public int RawVersion { get; set; } = If3Version;

        /// <summary>The version the client sees, with 255 read as -1.</summary>
        public int Version => RawVersion == If3Version ? -1 : RawVersion;

        /// <summary>
        ///     The component type: 0 layer, 3 rectangle, 4 text, 5 sprite, 6 model, 9 line.
        /// </summary>
        /// <remarks>
        ///     Settled from what the renderer does with each value rather than from any field name.
        ///     Types 1, 2, 7 and 8 are expressible and nothing in the client reads them; 10..127 read
        ///     no type block at all and nothing rejects them.
        /// </remarks>
        public int ComponentType { get; set; }

        /// <summary>
        ///     The authoring name, or null when the type byte's 0x80 bit is clear.
        /// </summary>
        /// <remarks>
        ///     Colon-delimited: CS2 6702 takes the substring before the first ':'
        ///     (<c>Class247.java:7051-7056</c>). <b>Set on 0 of the 42,256 files here</b>, so the
        ///     branch is implemented and untestable against this cache.
        /// </remarks>
        public InterfaceText? AuthoringName { get; set; }

        /// <summary>
        ///     The enum-like content type the client compares against a table of constants.
        /// </summary>
        /// <remarks>
        ///     The client's <c>x</c>. Not a coordinate. 0 on 42,235 files; the other 21 carry 328,
        ///     1337-1339 or 1400-1407.
        /// </remarks>
        public int ContentType { get; set; }

        /// <summary>The component's X, relative to its parent. Signed.</summary>
        public int BasePositionX { get; set; }

        /// <summary>The component's Y, relative to its parent. Signed.</summary>
        public int BasePositionY { get; set; }

        /// <summary>The component's width before <see cref="WidthMode"/> is applied. Unsigned.</summary>
        public int BaseWidth { get; set; }

        /// <summary>The component's height before <see cref="HeightMode"/> is applied. Unsigned.</summary>
        public int BaseHeight { get; set; }

        /// <summary>How the width is resolved against the parent (<c>Class253.java:319</c>).</summary>
        public sbyte WidthMode { get; set; }

        /// <summary>How the height is resolved against the parent (<c>Class253.java:333</c>).</summary>
        public sbyte HeightMode { get; set; }

        /// <summary>How the X is resolved against the parent (<c>KeyStroke.java:32-38</c>).</summary>
        public sbyte XMode { get; set; }

        /// <summary>How the Y is resolved against the parent (<c>KeyStroke.java:13-19</c>).</summary>
        public sbyte YMode { get; set; }

        /// <summary>
        ///     The parent component id as stored, 0..65535.
        /// </summary>
        /// <remarks>
        ///     <b>Non-canonical case.</b> The client turns 65535 into -1 and anything else into
        ///     <c>raw + (ID_TAG &amp; ~0xffff)</c>, folding this component's own group into the high
        ///     half (<c>:1057-1063</c>). An encoder that wrote the decoded value straight back would
        ///     write a 32-bit component id into a 16-bit field. The stored short is kept and
        ///     <see cref="ParentComponentId"/> derives the client's view from it.
        ///     <para>
        ///     8,413 files store the 65535 sentinel and 33,843 store a real parent; every one of the
        ///     33,843 names a file id that exists in the same group.
        ///     </para>
        /// </remarks>
        public int RawParentId { get; set; } = NoParent;

        /// <summary>The parent as a folded component id, or -1 when this component is a root.</summary>
        public int ParentComponentId =>
            RawParentId == NoParent ? -1 : (GroupId << 16) | RawParentId;

        /// <summary>
        ///     The settings byte exactly as stored.
        /// </summary>
        /// <remarks>
        ///     <b>Non-canonical case.</b> Bit 0 is <see cref="IsHidden"/> and bit 1 is read only when
        ///     the version byte is non-negative; bits 2-7 are read by nothing at all
        ///     (<c>:1065-1071</c>). Rebuilding this byte from <c>IsHidden</c> alone would drop six
        ///     bits that no reader would miss and every byte comparison would. The same shape as the
        ///     reference table's <c>groupFlags</c> rule in <c>AGENTS.md</c>. Only 0 and 1 occur here,
        ///     so nothing is currently lost - which is not the same as nothing being at risk.
        /// </remarks>
        public int SettingsFlags { get; set; }

        /// <summary>Whether the component starts hidden - bit 0 of <see cref="SettingsFlags"/>.</summary>
        public bool IsHidden => (SettingsFlags & 0x1) != 0;

        // ===================================================================
        //  Type 0 - layer
        // ===================================================================

        /// <summary>Horizontal scroll extent of a layer.</summary>
        public int ScrollMaxHorizontal { get; set; }

        /// <summary>Vertical scroll extent of a layer.</summary>
        public int ScrollMaxVertical { get; set; }

        /// <summary>
        ///     The layer's trailing flag byte, read only when the version byte is negative.
        /// </summary>
        /// <remarks>
        ///     <b>Non-canonical case.</b> The client tests it for equality with 1
        ///     (<c>:1077-1079</c>), so 0 and 2..255 all decode to false and alias each other. Stored
        ///     raw. Values 0 (6,335 files) and 1 (238) are the only ones that occur.
        /// </remarks>
        public int LayerFlagByte { get; set; }

        // ===================================================================
        //  Type 5 - sprite
        // ===================================================================

        /// <summary>The sprite id in index 8, or -1.</summary>
        public int SpriteId { get; set; }

        /// <summary>The sprite transform parameter, used with a 4096 scale at <c>Node_Sub10_Sub24.java:603-639</c>.</summary>
        public int SpriteTransform { get; set; }

        /// <summary>
        ///     The sprite flags byte exactly as stored.
        /// </summary>
        /// <remarks>
        ///     <b>Non-canonical case.</b> Bits 0 and 1 are read (<c>:1086-1089</c>); bits 2-7 are not.
        ///     Values 0..3 are the only ones this cache uses.
        /// </remarks>
        public int SpriteFlags { get; set; }

        /// <summary>Whether the transformed draw is used - bit 0 of <see cref="SpriteFlags"/>.</summary>
        public bool SpriteTransformed => (SpriteFlags & 0x1) != 0;

        /// <summary>The client's <c>aBoolean2279</c>, CS2 1122 - bit 1 of <see cref="SpriteFlags"/>.</summary>
        public bool SpriteTiled => (SpriteFlags & 0x2) != 0;

        /// <summary>Outline thickness, <c>class324.method3688</c> at <c>RSInterface.java:500</c>.</summary>
        public int OutlineThickness { get; set; }

        /// <summary>Outline or shadow colour, 0 meaning none (<c>RSInterface.java:487-495</c>).</summary>
        public int OutlineColour { get; set; }

        /// <summary>
        ///     The first image-transform flag byte as stored.
        /// </summary>
        /// <remarks><b>Non-canonical case</b>: an <c>== 1</c> test, so 0 and 2..255 alias.</remarks>
        public int SpriteTransform1Byte { get; set; }

        /// <summary>
        ///     The second image-transform flag byte as stored.
        /// </summary>
        /// <remarks><b>Non-canonical case</b>: an <c>== 1</c> test, so 0 and 2..255 alias.</remarks>
        public int SpriteTransform2Byte { get; set; }

        // ===================================================================
        //  Type 6 - model
        // ===================================================================

        /// <summary>
        ///     The model id as stored, with 65535 meaning none.
        /// </summary>
        /// <remarks>
        ///     A model id in index 7 as decoded. The client assigns <c>anInt2233 = 1</c> before any
        ///     read (<c>:1099</c>), which is the model-source kind rather than a wire field, and
        ///     <c>Class247.java:1478</c> returns the id only while that kind is still 1. CS2 opcodes
        ///     set it to 2, 3, 5, 6, 8 and 9 for npc, item and player sources at runtime.
        /// </remarks>
        public int RawModelId { get; set; } = NoReference;

        /// <summary>The model id, or -1 when the sentinel is stored.</summary>
        public int ModelId => RawModelId == NoReference ? -1 : RawModelId;

        /// <summary>
        ///     The model settings byte exactly as stored.
        /// </summary>
        /// <remarks>
        ///     <b>Non-canonical case.</b> Bits 0-3 are read (<c>:1106-1112</c>); bits 4-7 are not.
        ///     Bit 0 takes the six-field transform block and bit 1 the seven-field one; the two are
        ///     mutually exclusive and bit 0 wins. Values 0, 1, 5, 9 and 13 occur here.
        /// </remarks>
        public int ModelSettings { get; set; }

        /// <summary>Whether the six-field transform block is present - bit 0 of <see cref="ModelSettings"/>.</summary>
        public bool HasModelTransform => (ModelSettings & 0x1) != 0;

        /// <summary>
        ///     Whether the seven-field transform block is present.
        /// </summary>
        /// <remarks>
        ///     Bit 1, and only when bit 0 is clear. <b>Set on 0 of the 42,256 files here</b>, so the
        ///     block and its signed zoom are implemented and untestable against this cache.
        /// </remarks>
        public bool HasExtendedModelTransform => (ModelSettings & 0x1) == 0 && (ModelSettings & 0x2) != 0;

        /// <summary>The client's <c>aBoolean2265</c> - bit 2 of <see cref="ModelSettings"/>.</summary>
        public bool ModelFlag2 => (ModelSettings & 0x4) != 0;

        /// <summary>The client's <c>aBoolean2325</c> - bit 3 of <see cref="ModelSettings"/>.</summary>
        public bool ModelFlag3 => (ModelSettings & 0x8) != 0;

        /// <summary>Model X offset, signed.</summary>
        public int ModelOffsetX { get; set; }

        /// <summary>Model Y offset, signed.</summary>
        public int ModelOffsetY { get; set; }

        /// <summary>The extended block's extra signed short, the client's <c>anInt2352</c>.</summary>
        public int ModelExtendedOffset { get; set; }

        /// <summary>Model rotation about X, 0..2047.</summary>
        public int ModelRotateX { get; set; }

        /// <summary>Model rotation about Y, 0..2047.</summary>
        public int ModelRotateY { get; set; }

        /// <summary>Model rotation about Z, 0..2047.</summary>
        public int ModelRotateZ { get; set; }

        /// <summary>
        ///     Model zoom.
        /// </summary>
        /// <remarks>
        ///     Read unsigned in the six-field block and <b>signed</b> in the seven-field one. That
        ///     asymmetry is the client's, not a transcription slip.
        /// </remarks>
        public int ModelZoom { get; set; }

        /// <summary>The animation id as stored, with 65535 meaning none.</summary>
        public int RawAnimationId { get; set; } = NoReference;

        /// <summary>The animation id, or -1 when the sentinel is stored.</summary>
        public int AnimationId => RawAnimationId == NoReference ? -1 : RawAnimationId;

        /// <summary>An extra short present only when <see cref="WidthMode"/> is non-zero.</summary>
        public int ModelWidthExtra { get; set; }

        /// <summary>An extra short present only when <see cref="HeightMode"/> is non-zero.</summary>
        public int ModelHeightExtra { get; set; }

        // ===================================================================
        //  Type 4 - text
        // ===================================================================

        /// <summary>The font id as stored, with 65535 meaning none. The sentinel never occurs here.</summary>
        public int RawFontId { get; set; } = NoReference;

        /// <summary>The font id in index 13, or -1 when the sentinel is stored.</summary>
        public int FontId => RawFontId == NoReference ? -1 : RawFontId;

        /// <summary>The text drawn.</summary>
        public InterfaceText Message { get; set; } = InterfaceText.EmptyText;

        /// <summary>Line height.</summary>
        public int LineHeight { get; set; }

        /// <summary>Horizontal alignment, 0..2.</summary>
        public int HorizontalAlignment { get; set; }

        /// <summary>Vertical alignment, 0..3.</summary>
        public int VerticalAlignment { get; set; }

        /// <summary>
        ///     The text shadow flag byte as stored.
        /// </summary>
        /// <remarks><b>Non-canonical case</b>: an <c>== 1</c> test, so 0 and 2..255 alias.</remarks>
        public int ShadowByte { get; set; }

        /// <summary>Whether the text is drawn with a shadow.</summary>
        public bool HasShadow => ShadowByte == 1;

        /// <summary>
        ///     A trailing text byte read only when the version byte is non-negative
        ///     (<c>:1163-1165</c>). <b>Never read in this cache.</b>
        /// </summary>
        public int TextVersionedByte { get; set; }

        // ===================================================================
        //  Type 3 - rectangle
        // ===================================================================

        /// <summary>
        ///     The rectangle fill flag byte as stored.
        /// </summary>
        /// <remarks>
        ///     Selects between two rectangle draws at <c>Node_Sub10_Sub24.java:441-455</c>.
        ///     <b>Non-canonical case</b>: an <c>== 1</c> test, so 0 and 2..255 alias.
        /// </remarks>
        public int RectangleFilledByte { get; set; }

        /// <summary>Whether the rectangle is drawn filled.</summary>
        public bool RectangleFilled => RectangleFilledByte == 1;

        // ===================================================================
        //  Type 9 - line
        // ===================================================================

        /// <summary>Line width in pixels.</summary>
        public int LineWidth { get; set; }

        /// <summary>
        ///     The line direction flag byte as stored.
        /// </summary>
        /// <remarks>
        ///     Flips which diagonal is drawn (<c>Node_Sub10_Sub24.java:885-897</c>).
        ///     <b>Non-canonical case</b>: an <c>== 1</c> test, so 0 and 2..255 alias.
        /// </remarks>
        public int LineFlippedByte { get; set; }

        /// <summary>Whether the line runs the other way across its rectangle.</summary>
        public bool LineFlipped => LineFlippedByte == 1;

        // ===================================================================
        //  Shared by types 3, 4, 5 and 9
        // ===================================================================

        /// <summary>
        ///     The component's colour, which types 3, 4, 5 and 9 all read into one field.
        /// </summary>
        /// <remarks>
        ///     Shared here because it is shared in the client, and only one type block ever runs for
        ///     a given component. For a sprite it is the recolour tint rather than a fill.
        /// </remarks>
        public int Colour { get; set; }

        /// <summary>
        ///     Transparency, read by types 3, 4 and 5.
        /// </summary>
        /// <remarks>
        ///     <b>0 is opaque.</b> The renderer builds the pixel as
        ///     <c>((255 - (a &amp; 0xff)) &lt;&lt; 24) | (colour &amp; 0xffffff)</c>
        ///     (<c>Node_Sub10_Sub24.java:443-449</c>), so this is an inverted alpha.
        /// </remarks>
        public int Transparency { get; set; }

        // ===================================================================
        //  Common tail
        // ===================================================================

        /// <summary>
        ///     The 24-bit access mask.
        /// </summary>
        /// <remarks>
        ///     Read big-endian and unsigned by <c>RSBuffer.method1186</c>
        ///     (<c>RSBuffer.java:131-135</c>), every byte masked with 0xff before it is combined.
        ///     <c>RSBuffer.method1192</c> in the same class is little-endian and is <b>not</b>
        ///     interchangeable - picking it silently changes which of the gated branches fires.
        /// </remarks>
        public int AccessMask { get; set; }

        /// <summary>
        ///     The slot table, in stream order.
        /// </summary>
        /// <remarks>
        ///     Empty means the table is absent, which costs exactly one zero byte on the wire - the
        ///     byte read at <c>:1181</c> serves as both the first header and the terminator, so an
        ///     absent table writes no separate terminator. Present on 43 files: 39 with one entry and
        ///     4 with four.
        /// </remarks>
        public List<InterfaceSlotEntry> Slots { get; } = new List<InterfaceSlotEntry>();

        /// <summary>The option base text, CS2 1101.</summary>
        public InterfaceText OptionBase { get; set; } = InterfaceText.EmptyText;

        /// <summary>
        ///     The high nibble of the action byte, exactly as stored.
        /// </summary>
        /// <remarks>
        ///     <b>Non-canonical case.</b> The two blocks it gates are taken for <c>&gt; 0</c> and
        ///     <c>&gt; 1</c> (<c>:1226-1242</c>), so 2..15 all read exactly two index/value pairs and
        ///     alias each other, and the nibble is never stored anywhere the client can read back.
        ///     Only 0 (42,117 files) and 1 (139) occur here, so the alias is latent.
        ///     <para>
        ///     The low nibble is <i>not</i> stored: it is exactly <c>ContextOptions.Count</c>, with no
        ///     second representation, so it is recomputed on encode.
        ///     </para>
        /// </remarks>
        public int ActionHighNibble { get; set; }

        /// <summary>The context-menu option strings. The count is the action byte's low nibble.</summary>
        public List<InterfaceText> ContextOptions { get; } = new List<InterfaceText>();

        /// <summary>The index written by the first action block, present when <see cref="ActionHighNibble"/> is above 0.</summary>
        public int OptionArrayIndex1 { get; set; }

        /// <summary>The value written by the first action block.</summary>
        public int OptionArrayValue1 { get; set; }

        /// <summary>The index written by the second action block, present when <see cref="ActionHighNibble"/> is above 1.</summary>
        public int OptionArrayIndex2 { get; set; }

        /// <summary>The value written by the second action block. <b>Never reached in this cache.</b></summary>
        public int OptionArrayValue2 { get; set; }

        /// <summary>
        ///     The client's <c>aString2333</c>, which it turns into null when empty (<c>:1246-1248</c>).
        /// </summary>
        /// <remarks>
        ///     Not an ambiguity: an empty string and a null one are both one zero byte, so writing
        ///     the empty form back reproduces the file. Empty on 42,163 of the 42,256 files.
        /// </remarks>
        public InterfaceText SelectedAction { get; set; } = InterfaceText.EmptyText;

        /// <summary>Drag deadzone in pixels (<c>Class111_Sub3.java:87-95</c>).</summary>
        public int DragDeadzone { get; set; }

        /// <summary>Drag delay in ticks (<c>Class111_Sub3.java:83</c>).</summary>
        public int DragDelay { get; set; }

        /// <summary>Hint-icon slot (<c>Node_Sub10_Sub24.java:137</c>), CS2 at <c>Class247.java:1080</c>.</summary>
        public int HintSlot { get; set; }

        /// <summary>Tooltip, returned by <c>Class170.java:16-22</c> when non-blank.</summary>
        public InterfaceText Tooltip { get; set; } = InterfaceText.EmptyText;

        /// <summary>
        ///     Whether the three trailing target shorts are present.
        /// </summary>
        /// <remarks>
        ///     Derived from the mask rather than stored, because the client derives it the same way
        ///     and the mask is kept whole. Fires on 140 files, in every one of which all three shorts
        ///     are the 65535 sentinel.
        /// </remarks>
        public bool HasTargetShorts => (AccessMask & TargetGateMask) != 0;

        /// <summary>The first target short as stored, with 65535 meaning none.</summary>
        public int RawTargetVerb { get; set; } = NoReference;

        /// <summary>The second target short as stored, with 65535 meaning none.</summary>
        public int RawTargetCursor { get; set; } = NoReference;

        /// <summary>The third target short as stored, with 65535 meaning none.</summary>
        public int RawTargetOperand { get; set; } = NoReference;

        /// <summary>
        ///     The version-gated short, the client's <c>anInt2317</c> (<c>:1279-1285</c>).
        ///     <b>Never read in this cache.</b>
        /// </summary>
        public int RawVersionedShort { get; set; } = NoReference;

        /// <summary>
        ///     Integer parameters, read only when the version byte is non-negative.
        ///     <b>Never read in this cache.</b>
        /// </summary>
        public List<InterfaceParameter> IntegerParameters { get; } = new List<InterfaceParameter>();

        /// <summary>
        ///     String parameters, read only when the version byte is non-negative.
        ///     <b>Never read in this cache.</b>
        /// </summary>
        public List<InterfaceParameter> StringParameters { get; } = new List<InterfaceParameter>();

        /// <summary>
        ///     The twenty CS2 hook arrays, indexed as the wire format orders them.
        /// </summary>
        /// <remarks>
        ///     <b>An empty entry is an absent hook, and that is not a lost distinction.</b> It looks
        ///     like the classic null-versus-empty trap and is not one: <c>loadCS2Bytecode</c> returns
        ///     null on a count of 0 (<c>:401-405</c>), and a present-but-empty array has no encoding
        ///     at all - a count of 0 <i>is</i> absence. So there is nothing to record, and a "was this
        ///     null or empty" flag could only ever hold one value. The real hazard in this
        ///     neighbourhood is the operand type byte, which
        ///     <see cref="InterfaceScriptOperand.TypeByte"/> keeps.
        ///     <para>
        ///     Slots 5, 6, 7, 18 and 19 pair with the five <see cref="Triggers"/> arrays; the client's
        ///     CS2 setters 1407, 1414, 1415, 1428 and 1429 each assign a hook and its triggers in one
        ///     statement (<c>Class247.java:1254-1316</c>). CS2 opcodes 1418 to 1427 set ten further
        ///     hook arrays that are not in the wire format at all - do not go looking for them in the
        ///     bytes.
        ///     </para>
        /// </remarks>
        public InterfaceScriptOperand[][] Hooks { get; } = EmptyTable<InterfaceScriptOperand>(HookCount);

        /// <summary>
        ///     A twenty-first hook array read between slots 9 and 10 when the version byte is
        ///     non-negative (<c>:1320-1322</c>). <b>Never read in this cache.</b>
        /// </summary>
        public InterfaceScriptOperand[] VersionedHook { get; set; } =
            Array.Empty<InterfaceScriptOperand>();

        /// <summary>
        ///     The five trigger integer arrays. An empty entry is an absent array, for the reason
        ///     <see cref="Hooks"/> gives.
        /// </summary>
        public int[][] Triggers { get; } = EmptyTable<int>(TriggerCount);

        private static T[][] EmptyTable<T>(int length) {
            var table = new T[length][];
            for (int i = 0; i < length; i++)
                table[i] = Array.Empty<T>();
            return table;
        }

        // ===================================================================
        //  Decode
        // ===================================================================

        /// <summary>
        ///     Reads one component, in the order <c>unpackConfig</c> reads it.
        /// </summary>
        /// <param name="stream">The component's bytes, positioned at the version byte.</param>
        /// <returns>This definition, populated.</returns>
        public InterfaceComponentDefinition Decode(JagStream stream) {
            if (stream == null)
                throw new ArgumentNullException(nameof(stream));

            RawVersion = stream.ReadUnsignedByte();

            int typeByte = stream.ReadUnsignedByte();
            if ((typeByte & 0x80) != 0) {
                //Unreachable in this cache - 0 of 42,256 files set the bit. A passing byte-identity
                //sweep is not evidence about this branch.
                ComponentType = typeByte & 0x7F;
                AuthoringName = InterfaceText.Read(stream);
            }
            else {
                ComponentType = typeByte;
                AuthoringName = null;
            }

            ContentType = stream.ReadUnsignedShort();
            BasePositionX = stream.ReadShort();
            BasePositionY = stream.ReadShort();
            BaseWidth = stream.ReadUnsignedShort();
            BaseHeight = stream.ReadUnsignedShort();
            WidthMode = stream.ReadSignedByte();
            HeightMode = stream.ReadSignedByte();
            XMode = stream.ReadSignedByte();
            YMode = stream.ReadSignedByte();
            RawParentId = stream.ReadUnsignedShort();
            SettingsFlags = stream.ReadUnsignedByte();

            if (ComponentType == 0) {
                ScrollMaxHorizontal = stream.ReadUnsignedShort();
                ScrollMaxVertical = stream.ReadUnsignedShort();

                //The one version-gated branch that is always taken here rather than never: the byte
                //exists precisely because an if3 file has no settings bit 1 to carry the flag.
                if (Version < 0)
                    LayerFlagByte = stream.ReadUnsignedByte();
            }

            if (ComponentType == 5) {
                SpriteId = stream.ReadInt();
                SpriteTransform = stream.ReadUnsignedShort();
                SpriteFlags = stream.ReadUnsignedByte();
                Transparency = stream.ReadUnsignedByte();
                OutlineThickness = stream.ReadUnsignedByte();
                OutlineColour = stream.ReadInt();
                SpriteTransform1Byte = stream.ReadUnsignedByte();
                SpriteTransform2Byte = stream.ReadUnsignedByte();
                Colour = stream.ReadInt();
            }

            if (ComponentType == 6) {
                RawModelId = stream.ReadUnsignedShort();
                ModelSettings = stream.ReadUnsignedByte();

                if (HasModelTransform) {
                    ModelOffsetX = stream.ReadShort();
                    ModelOffsetY = stream.ReadShort();
                    ModelRotateX = stream.ReadUnsignedShort();
                    ModelRotateY = stream.ReadUnsignedShort();
                    ModelRotateZ = stream.ReadUnsignedShort();
                    ModelZoom = stream.ReadUnsignedShort();
                }
                else if (HasExtendedModelTransform) {
                    //Unreachable in this cache - bit 1 is set on 0 of 42,256 files. Note the zoom is
                    //signed in this block and unsigned in the one above; that is the client's own
                    //asymmetry, and no sweep here can defend it.
                    ModelOffsetX = stream.ReadShort();
                    ModelOffsetY = stream.ReadShort();
                    ModelExtendedOffset = stream.ReadShort();
                    ModelRotateX = stream.ReadUnsignedShort();
                    ModelRotateY = stream.ReadUnsignedShort();
                    ModelRotateZ = stream.ReadUnsignedShort();
                    ModelZoom = stream.ReadShort();
                }

                RawAnimationId = stream.ReadUnsignedShort();

                if (WidthMode != 0)
                    ModelWidthExtra = stream.ReadUnsignedShort();
                if (HeightMode != 0)
                    ModelHeightExtra = stream.ReadUnsignedShort();
            }

            if (ComponentType == 4) {
                RawFontId = stream.ReadUnsignedShort();
                Message = InterfaceText.Read(stream);
                LineHeight = stream.ReadUnsignedByte();
                HorizontalAlignment = stream.ReadUnsignedByte();
                VerticalAlignment = stream.ReadUnsignedByte();
                ShadowByte = stream.ReadUnsignedByte();
                Colour = stream.ReadInt();
                Transparency = stream.ReadUnsignedByte();

                //Unreachable in this cache; implemented so the first file that sets a version is not
                //mis-parsed from here on.
                if (Version >= 0)
                    TextVersionedByte = stream.ReadUnsignedByte();
            }

            if (ComponentType == 3) {
                Colour = stream.ReadInt();
                RectangleFilledByte = stream.ReadUnsignedByte();
                Transparency = stream.ReadUnsignedByte();
            }

            if (ComponentType == 9) {
                LineWidth = stream.ReadUnsignedByte();
                Colour = stream.ReadInt();
                LineFlippedByte = stream.ReadUnsignedByte();
            }

            AccessMask = stream.ReadMedium();
            DecodeSlots(stream);

            OptionBase = InterfaceText.Read(stream);

            int actionByte = stream.ReadUnsignedByte();
            ActionHighNibble = actionByte >> 4;
            ContextOptions.Clear();
            for (int i = 0; i < (actionByte & 0xF); i++)
                ContextOptions.Add(InterfaceText.Read(stream));

            if (ActionHighNibble > 0) {
                OptionArrayIndex1 = stream.ReadUnsignedByte();
                OptionArrayValue1 = stream.ReadUnsignedShort();
            }

            if (ActionHighNibble > 1) {
                //Unreachable in this cache - the high nibble is only ever 0 or 1.
                OptionArrayIndex2 = stream.ReadUnsignedByte();
                OptionArrayValue2 = stream.ReadUnsignedShort();
            }

            SelectedAction = InterfaceText.Read(stream);
            DragDeadzone = stream.ReadUnsignedByte();
            DragDelay = stream.ReadUnsignedByte();
            HintSlot = stream.ReadUnsignedByte();
            Tooltip = InterfaceText.Read(stream);

            if (HasTargetShorts) {
                RawTargetVerb = stream.ReadUnsignedShort();
                RawTargetCursor = stream.ReadUnsignedShort();
                RawTargetOperand = stream.ReadUnsignedShort();
            }

            if (Version >= 0) {
                //Unreachable in this cache. A decoder written from a modern RS3 if3 reference reads
                //the parameter table unconditionally and desynchronises every record here.
                RawVersionedShort = stream.ReadUnsignedShort();
                DecodeParameters(stream);
            }

            for (int i = 0; i < 10; i++)
                Hooks[i] = DecodeScript(stream);

            //Unreachable in this cache, and it sits between hook 9 and hook 10 rather than at the end,
            //so getting it wrong shifts the last ten hooks and all five trigger arrays.
            VersionedHook = Version >= 0 ? DecodeScript(stream) : Array.Empty<InterfaceScriptOperand>();

            for (int i = 10; i < HookCount; i++)
                Hooks[i] = DecodeScript(stream);

            for (int i = 0; i < TriggerCount; i++)
                Triggers[i] = DecodeTriggers(stream);

            return this;
        }

        private void DecodeSlots(JagStream stream) {
            Slots.Clear();

            int header = stream.ReadUnsignedByte();
            while (header != 0) {
                int slot = (header >> 4) - 1;
                int value = ((header << 8) | stream.ReadUnsignedByte()) & 0xFFF;
                sbyte first = stream.ReadSignedByte();
                sbyte second = stream.ReadSignedByte();

                Slots.Add(new InterfaceSlotEntry(slot, value, first, second));
                header = stream.ReadUnsignedByte();
            }
        }

        private void DecodeParameters(JagStream stream) {
            IntegerParameters.Clear();
            StringParameters.Clear();

            int integers = stream.ReadUnsignedByte();
            for (int i = 0; i < integers; i++)
                IntegerParameters.Add(new InterfaceParameter(stream.ReadMedium(), stream.ReadInt(), null));

            int strings = stream.ReadUnsignedByte();
            for (int i = 0; i < strings; i++)
                StringParameters.Add(new InterfaceParameter(stream.ReadMedium(), 0,
                    InterfaceText.ReadVersioned(stream)));
        }

        private static InterfaceScriptOperand[] DecodeScript(JagStream stream) {
            int count = stream.ReadUnsignedByte();
            if (count == 0)
                return Array.Empty<InterfaceScriptOperand>();

            var operands = new InterfaceScriptOperand[count];
            for (int i = 0; i < count; i++)
                operands[i] = InterfaceScriptOperand.Read(stream);
            return operands;
        }

        private static int[] DecodeTriggers(JagStream stream) {
            int count = stream.ReadUnsignedByte();
            if (count == 0)
                return Array.Empty<int>();

            var values = new int[count];
            for (int i = 0; i < count; i++)
                values[i] = stream.ReadInt();
            return values;
        }

        // ===================================================================
        //  Encode
        // ===================================================================

        /// <summary>
        ///     Writes the component back in the order it was read.
        /// </summary>
        /// <returns>The encoded file, rewound.</returns>
        public JagStream Encode() {
            var stream = new JagStream();

            stream.WriteByte(RawVersion);

            if (ComponentType < 0 || ComponentType > 0x7F)
                throw new InvalidOperationException(
                    "Component type " + ComponentType + " does not fit the seven bits the type byte " +
                    "leaves for it once the name flag is masked off.");

            stream.WriteByte(AuthoringName == null ? ComponentType : ComponentType | 0x80);
            AuthoringName?.Write(stream);

            stream.WriteShort(ContentType);
            stream.WriteShort(BasePositionX);
            stream.WriteShort(BasePositionY);
            stream.WriteShort(BaseWidth);
            stream.WriteShort(BaseHeight);
            stream.WriteSignedByte(WidthMode);
            stream.WriteSignedByte(HeightMode);
            stream.WriteSignedByte(XMode);
            stream.WriteSignedByte(YMode);
            stream.WriteShort(RawParentId);
            stream.WriteByte(SettingsFlags);

            if (ComponentType == 0) {
                stream.WriteShort(ScrollMaxHorizontal);
                stream.WriteShort(ScrollMaxVertical);
                if (Version < 0)
                    stream.WriteByte(LayerFlagByte);
            }

            if (ComponentType == 5) {
                stream.WriteInteger(SpriteId);
                stream.WriteShort(SpriteTransform);
                stream.WriteByte(SpriteFlags);
                stream.WriteByte(Transparency);
                stream.WriteByte(OutlineThickness);
                stream.WriteInteger(OutlineColour);
                stream.WriteByte(SpriteTransform1Byte);
                stream.WriteByte(SpriteTransform2Byte);
                stream.WriteInteger(Colour);
            }

            if (ComponentType == 6) {
                stream.WriteShort(RawModelId);
                stream.WriteByte(ModelSettings);

                if (HasModelTransform) {
                    stream.WriteShort(ModelOffsetX);
                    stream.WriteShort(ModelOffsetY);
                    stream.WriteShort(ModelRotateX);
                    stream.WriteShort(ModelRotateY);
                    stream.WriteShort(ModelRotateZ);
                    stream.WriteShort(ModelZoom);
                }
                else if (HasExtendedModelTransform) {
                    stream.WriteShort(ModelOffsetX);
                    stream.WriteShort(ModelOffsetY);
                    stream.WriteShort(ModelExtendedOffset);
                    stream.WriteShort(ModelRotateX);
                    stream.WriteShort(ModelRotateY);
                    stream.WriteShort(ModelRotateZ);
                    stream.WriteShort(ModelZoom);
                }

                stream.WriteShort(RawAnimationId);

                if (WidthMode != 0)
                    stream.WriteShort(ModelWidthExtra);
                if (HeightMode != 0)
                    stream.WriteShort(ModelHeightExtra);
            }

            if (ComponentType == 4) {
                stream.WriteShort(RawFontId);
                Message.Write(stream);
                stream.WriteByte(LineHeight);
                stream.WriteByte(HorizontalAlignment);
                stream.WriteByte(VerticalAlignment);
                stream.WriteByte(ShadowByte);
                stream.WriteInteger(Colour);
                stream.WriteByte(Transparency);
                if (Version >= 0)
                    stream.WriteByte(TextVersionedByte);
            }

            if (ComponentType == 3) {
                stream.WriteInteger(Colour);
                stream.WriteByte(RectangleFilledByte);
                stream.WriteByte(Transparency);
            }

            if (ComponentType == 9) {
                stream.WriteByte(LineWidth);
                stream.WriteInteger(Colour);
                stream.WriteByte(LineFlippedByte);
            }

            stream.WriteMedium(AccessMask);
            EncodeSlots(stream);

            OptionBase.Write(stream);

            if (ContextOptions.Count > 0xF)
                throw new InvalidOperationException(
                    "A component carries at most 15 context-menu options, because the count is the " +
                    "action byte's low nibble; this one has " + ContextOptions.Count + ".");
            if (ActionHighNibble < 0 || ActionHighNibble > 0xF)
                throw new InvalidOperationException(
                    "The action byte's high nibble is four bits; " + ActionHighNibble + " does not fit.");

            stream.WriteByte((ActionHighNibble << 4) | ContextOptions.Count);
            foreach (InterfaceText option in ContextOptions)
                option.Write(stream);

            if (ActionHighNibble > 0) {
                stream.WriteByte(OptionArrayIndex1);
                stream.WriteShort(OptionArrayValue1);
            }

            if (ActionHighNibble > 1) {
                stream.WriteByte(OptionArrayIndex2);
                stream.WriteShort(OptionArrayValue2);
            }

            SelectedAction.Write(stream);
            stream.WriteByte(DragDeadzone);
            stream.WriteByte(DragDelay);
            stream.WriteByte(HintSlot);
            Tooltip.Write(stream);

            if (HasTargetShorts) {
                stream.WriteShort(RawTargetVerb);
                stream.WriteShort(RawTargetCursor);
                stream.WriteShort(RawTargetOperand);
            }

            if (Version >= 0) {
                stream.WriteShort(RawVersionedShort);
                EncodeParameters(stream);
            }

            for (int i = 0; i < 10; i++)
                EncodeScript(stream, Hooks[i]);

            if (Version >= 0)
                EncodeScript(stream, VersionedHook);

            for (int i = 10; i < HookCount; i++)
                EncodeScript(stream, Hooks[i]);

            for (int i = 0; i < TriggerCount; i++)
                EncodeTriggers(stream, Triggers[i]);

            return stream.Flip();
        }

        private void EncodeSlots(JagStream stream) {
            foreach (InterfaceSlotEntry entry in Slots) {
                //The bound checked here is what the byte can carry, not what the client can load. A
                //slot above MaxSlot would throw in the client's own reader, and a slot of -1 is what
                //its reader produces from a header with a zero high nibble - but refusing either
                //would mean the encoder rejects records the decoder accepted, and this codec's
                //contract is that a file it read comes back the file it read.
                if (entry.Slot < -1 || entry.Slot > 14)
                    throw new InvalidOperationException(
                        "Slot " + entry.Slot + " cannot be stored: the header byte carries slot + 1 in " +
                        "four bits.");
                if (entry.RawValue < 0 || entry.RawValue > InterfaceSlotEntry.NoValue)
                    throw new InvalidOperationException(
                        "Slot value " + entry.RawValue + " does not fit the twelve bits the format gives it.");

                //The header packs the slot index above the value's top four bits, which is why the
                //value is kept raw: rebuilding it from a signed -1 puts 0xFF in the low nibble.
                int header = ((entry.Slot + 1) << 4) | ((entry.RawValue >> 8) & 0xF);
                if (header == 0)
                    throw new InvalidOperationException(
                        "A slot entry whose header byte is zero would terminate the table it is in, " +
                        "handing the rest of the component back as the option base string.");

                stream.WriteByte(header);
                stream.WriteByte(entry.RawValue & 0xFF);
                stream.WriteSignedByte(entry.First);
                stream.WriteSignedByte(entry.Second);
            }

            //Also the whole table when there are no entries: the reader's first byte doubles as the
            //terminator, so an absent table is one zero byte and not two.
            stream.WriteByte(0);
        }

        private void EncodeParameters(JagStream stream) {
            stream.WriteByte(IntegerParameters.Count);
            foreach (InterfaceParameter parameter in IntegerParameters) {
                stream.WriteMedium(parameter.Key);
                stream.WriteInteger(parameter.Integer);
            }

            stream.WriteByte(StringParameters.Count);
            foreach (InterfaceParameter parameter in StringParameters) {
                stream.WriteMedium(parameter.Key);
                (parameter.Text ?? InterfaceText.EmptyText).WriteVersioned(stream);
            }
        }

        private static void EncodeScript(JagStream stream, InterfaceScriptOperand[] operands) {
            //An empty array is written as absent because the format cannot express anything else:
            //a count of 0 is what absence is, so there is no encoding to choose between.
            if (operands == null || operands.Length == 0) {
                stream.WriteByte(0);
                return;
            }

            if (operands.Length > 255)
                throw new InvalidOperationException(
                    "A hook array holds at most 255 operands; this one has " + operands.Length + ".");

            stream.WriteByte(operands.Length);
            foreach (InterfaceScriptOperand operand in operands)
                operand.Write(stream);
        }

        private static void EncodeTriggers(JagStream stream, int[] values) {
            if (values == null || values.Length == 0) {
                stream.WriteByte(0);
                return;
            }

            if (values.Length > 255)
                throw new InvalidOperationException(
                    "A trigger array holds at most 255 entries; this one has " + values.Length + ".");

            stream.WriteByte(values.Length);
            foreach (int value in values)
                stream.WriteInteger(value);
        }

        // ===================================================================
        //  Editing helpers
        // ===================================================================

        /// <summary>
        ///     Points the component at a parent, or at nothing.
        /// </summary>
        /// <remarks>
        ///     Takes a folded component id and stores the low half, which is the inverse of the fold
        ///     the client applies on read. Writing <see cref="RawParentId"/> from a decoded id
        ///     directly is the mistake this exists to prevent.
        /// </remarks>
        /// <param name="componentId">The parent's folded component id, or any negative value for none.</param>
        public void SetParent(int componentId) {
            if (componentId < 0) {
                RawParentId = NoParent;
                return;
            }

            if ((componentId >> 16) != GroupId)
                throw new ArgumentOutOfRangeException(nameof(componentId), componentId,
                    "A component's parent is stored as a sixteen-bit sibling index, so it has to live " +
                    "in the same interface - group " + GroupId + ".");

            RawParentId = componentId & 0xFFFF;
        }

        /// <summary>How many of the twenty hook arrays this component carries.</summary>
        public int HookArrayCount {
            get {
                int present = 0;
                foreach (InterfaceScriptOperand[] hook in Hooks)
                    if (hook.Length > 0)
                        present++;
                return present;
            }
        }

        /// <summary>How many of the five trigger arrays this component carries.</summary>
        public int TriggerArrayCount {
            get {
                int present = 0;
                foreach (int[] trigger in Triggers)
                    if (trigger.Length > 0)
                        present++;
                return present;
            }
        }

        /// <summary>The component in words, for logs and error messages.</summary>
        /// <returns>Its address and type.</returns>
        public override string ToString() {
            return "interface " + GroupId + " component " + FileId + " (type " + ComponentType + ")";
        }
    }
}
