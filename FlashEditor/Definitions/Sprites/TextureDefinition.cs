using System;
using System.Drawing;

namespace FlashEditor.Definitions.Sprites {
    /// <summary>
    /// Texture definition metadata from the materials index (index 26).
    /// Fields correspond to Class238 in the Hydra client.
    /// The material index stores ALL texture definitions in a single file
    /// using a columnar (pass-based) binary format - decoded and encoded by
    /// <see cref="MaterialTable"/>.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///     <b>Eighteen of the nineteen columns are named for what the client does with them, and
    ///     every one of those names cites the line that settles it.</b> The evidence is
    ///     <c>reference/hydra-637-definitions/material-columns.md</c>, read against the 637 tree at
    ///     <c>HydraScape/client/src</c>; the measured companion is
    ///     <c>reference/index-survey/index-026-MATERIALS-column-census.md</c>. The citation is the
    ///     point rather than decoration - a name with nothing behind it is how the model dump ended
    ///     up labelling five face arrays wrongly for months.
    ///     </para>
    ///     <para>
    ///     <see cref="field1827"/> is the exception and keeps its obfuscated name, because no Java
    ///     code in the 637 tree reads it at all.
    ///     </para>
    ///     <para>
    ///     The nineteen material fields are properties rather than fields so that assigning one
    ///     records <em>which</em> column changed. Without that the write path cannot tell an edited
    ///     record from an untouched one and has to choose between two wrong answers: replay the
    ///     stored bytes and discard every edit in silence, which is what it used to do, or re-encode
    ///     everything from fields and rewrite bytes nobody touched.
    ///     </para>
    ///     <para>
    ///     Assigning the value a field already holds is not an edit, and neither is an edit that puts
    ///     a field back where it started. That matters more here than it looks: a property grid writes
    ///     every cell back on commit, and treating those as edits would rewrite the whole table - and
    ///     its archive CRC - for a dialog somebody only opened.
    ///     </para>
    ///     <para>
    ///     <see cref="spriteFileIds"/>, <see cref="graph"/> and <see cref="thumb"/> are deliberately
    ///     plain fields: they come from index 9 and index 8, not from index 26, and merging them in
    ///     at load time must not mark the material table as edited.
    ///     </para>
    /// </remarks>
    public class TextureDefinition : IDisposable {
        /// <summary>The texture id, which is this record's slot in the material table.</summary>
        public int id;

        /// <summary>
        ///     Which material columns have been assigned a value different from the one decoded.
        /// </summary>
        /// <remarks>
        ///     A bit per <see cref="MaterialColumn"/>. Per column rather than per record because the
        ///     format is not canonical in three of its columns: a boolean column decodes many-to-one,
        ///     so re-encoding a column from its bool cannot reproduce a stored byte outside {0,1}.
        ///     Neither supported cache holds one, which is exactly why the granularity has to be
        ///     designed in rather than discovered - no sweep over this cache can catch it.
        /// </remarks>
        private int _dirtyColumns;

        private bool _suppressTexture;
        private bool _force64x64;
        private bool _excludeFromDrawList;
        private int _colourGain;
        private int _greyBlendWeight;
        private sbyte _effectProgram;
        private sbyte _effectParams;
        private int _representativeHsl;
        private sbyte _scrollU;
        private sbyte _scrollV;
        private bool _field1827;
        private bool _transposePixels;
        private sbyte _mipmap;
        private bool _repeatU;
        private bool _repeatV;
        private bool _halfFloatUpload;
        private int _combineMode;
        private int _waterParams;
        private int _alphaMode;

        // --- Class238 fields (columnar read order) ---

        /// <summary>
        ///     Draw <see cref="representativeHsl"/> instead of this texture (<c>aBoolean1825</c>).
        /// </summary>
        /// <remarks>
        ///     Stored inverted: true when the byte is 0 (<c>Class260.java:116</c>). Six independent
        ///     subsystems react to true by throwing the texture id away and falling back to a flat
        ///     colour, and <c>Node_Sub16.java:78-80</c> names that colour as <c>aShort1831</c>. It is
        ///     not "is textured": in models the suppression is conditional on the caller's
        ///     <c>0x40</c> flag (<c>Renderable_Sub1.java:185-191</c>), and in terrain, floor overlays
        ///     and the map path it is unconditional (<c>s_Sub3.java:310</c>,
        ///     <c>Class278.java:714</c>).
        /// </remarks>
        public bool suppressTexture {
            get => _suppressTexture;
            set => Set(ref _suppressTexture, value, MaterialColumn.SuppressTexture);
        }

        /// <summary>
        ///     Rasterise this texture at 64x64 rather than at the configured detail size
        ///     (<c>aBoolean1822</c>).
        /// </summary>
        /// <remarks>
        ///     True when the byte is 1 (<c>Class260.java:121</c>). <c>Class364.java:96</c> picks
        ///     <c>64</c> against the user texture-detail setting and uses it as both width and
        ///     height; <c>RenderType_Sub2.java:2379</c> and <c>Node_Sub10_Sub25.java:170</c> agree.
        ///     A resolution cap, not a quality hint.
        /// </remarks>
        public bool force64x64 {
            get => _force64x64;
            set => Set(ref _force64x64, value, MaterialColumn.Force64x64);
        }

        /// <summary>
        ///     Drop any face carrying this texture from the hardware draw list
        ///     (<c>aBoolean1833</c>).
        /// </summary>
        /// <remarks>
        ///     True when the byte is 1 (<c>Class260.java:126</c>). Both hardware model builders
        ///     <c>continue</c> past the face (<c>Renderable_Sub2.java:401-403</c>,
        ///     <c>Renderable_Sub3.java:175-178</c>), which is the same statement that excludes
        ///     render-type-2 faces one line earlier. <b>The software builder never reads it</b>, so
        ///     the same model draws the face under the software rasteriser and drops it under both
        ///     hardware renderers. Why content would ask for that is not visible from the client.
        /// </remarks>
        public bool excludeFromDrawList {
            get => _excludeFromDrawList;
            set => Set(ref _excludeFromDrawList, value, MaterialColumn.ExcludeFromDrawList);
        }

        /// <summary>
        ///     Saturating gain applied to the lit vertex colour before the texture is
        ///     applied, 0 to 255 (<c>aByte1829</c>).
        /// </summary>
        /// <remarks>
        ///     The multiplier is <c>(256 + v) / 256</c>, so 0 leaves the colour alone and 255 is
        ///     roughly a doubling, clamped per channel at 65535 before the <c>&gt;&gt; 8</c> -
        ///     <c>Renderable_Sub1.java:2440-2445</c>, matched at <c>Renderable_Sub2.java:3927-3945</c>,
        ///     <c>Node_Sub20.java:150-166</c> and <c>Node_Sub30.java:492</c>. A multiply, not a
        ///     colour and not an offset.
        ///     <para>
        ///     <b>Surfaced as 0..255 although <c>Class238</c> declares a Java <c>byte</c>.</b> Every
        ///     client consumption masks <c>&amp; 0xff</c> first, so the meaningful high end is 255,
        ///     and an <c>sbyte</c> surface showed that as <c>-1</c>. The stored byte is unchanged
        ///     either way - one byte in, one byte out - so this is a presentation fix and not a
        ///     format change.
        ///     </para>
        /// </remarks>
        public int colourGain {
            get => _colourGain;
            set => Set(ref _colourGain, value, MaterialColumn.ColourGain);
        }

        /// <summary>
        ///     Weight of the blend from the surface's palette colour toward a neutral grey of the
        ///     same brightness, 0 to 255 (<c>aByte1830</c>).
        /// </summary>
        /// <remarks>
        ///     A desaturation weight rather than a brightness: <c>Renderable_Sub1.java:2428-2438</c>
        ///     lerps toward <c>131586 * shade</c>, and <c>131586</c> is <c>0x020202</c>, the
        ///     monochrome equivalent of the lighting value. Matched at
        ///     <c>Renderable_Sub2.java:3901-3923</c>, <c>Node_Sub20.java:129-146</c> and
        ///     <c>Node_Sub30.java:468</c>, and gated on <c>effectProgram != 4</c> in the last two.
        ///     <para>
        ///     A stored 255 is a 255/256 lerp and never an exact swap. Two renderers special-case
        ///     <c>256</c> for full replacement (<c>Renderable_Sub1.java:2430</c>,
        ///     <c>Node_Sub20.java:141</c>) after masking with <c>0xff</c>, so that branch is dead.
        ///     </para>
        ///     <para>
        ///     Surfaced as 0..255 for the same reason as <see cref="colourGain"/>, and this is the
        ///     column where it bites hardest: 255 is the single most common non-zero value in both
        ///     caches.
        ///     </para>
        /// </remarks>
        public int greyBlendWeight {
            get => _greyBlendWeight;
            set => Set(ref _greyBlendWeight, value, MaterialColumn.GreyBlendWeight);
        }

        /// <summary>
        ///     Which of the renderer's ten texture-effect programs runs for this texture
        ///     (<c>aByte1820</c>).
        /// </summary>
        /// <remarks>
        ///     Used directly as an array index into a ten-wide table - <c>Class55.java:119-121</c>
        ///     and <c>RenderType_Sub3.java:4085</c>, populated at <c>Class55.java:53-62</c>. Slot 0
        ///     is deliberately null and means "no effect"; 4, 8 and 9 are the water shaders and
        ///     degrade to 2 when the hardware cannot run them (<c>Class55.java:95-99</c>). An
        ///     enumerated id, not a flag and not a scalar, and a value outside 0..9 indexes past the
        ///     table.
        /// </remarks>
        public sbyte effectProgram {
            get => _effectProgram;
            set => Set(ref _effectProgram, value, MaterialColumn.EffectProgram);
        }

        /// <summary>
        ///     A packed parameter byte whose layout belongs to whichever program
        ///     <see cref="effectProgram"/> names (<c>aByte1816</c>).
        /// </summary>
        /// <remarks>
        ///     <b>Deliberately not named for any one effect's reading of it.</b> Effects 8 and 9 take
        ///     bits 0-1 as an animation-speed exponent and bits 3-5 as a scale exponent, feeding the
        ///     <c>"time"</c> and <c>"scale"</c> uniforms the client names in a string literal
        ///     (<c>Class151_Sub2.java:150-165</c>, <c>Class151_Sub4.java:135-145</c>). Effect 1 reads
        ///     the whole byte as a 1-based frame-set index (<c>Class151_Sub6.java:197</c>); effect 2
        ///     tests <c>0x80</c>, <c>0x40</c> and <c>0x3</c> (<c>Class151_Sub3.java:299-335</c>);
        ///     effect 5 uses a fourth layout again (<c>Class151_Sub7.java:122-129</c>). There is no
        ///     single scalar meaning, so reading it as a number is meaningless.
        /// </remarks>
        public sbyte effectParams {
            get => _effectParams;
            set => Set(ref _effectParams, value, MaterialColumn.EffectParams);
        }

        /// <summary>
        /// The texture's representative colour, packed as a raw 16-bit RS HSL
        /// (<c>aShort1831</c>).
        /// </summary>
        /// <remarks>
        /// Not a speed or timing value, whatever the field tables say. The client feeds it to
        /// <c>Class345.method3825</c>, whose body is the standard HSL light-shade
        /// (<c>(hsl &amp; 0xff80) + clamped lightness</c>), and then to the palette lookup - see
        /// <c>Node_Sub16.java:79</c> and <c>Class278.java:730-732</c>, whose
        /// <c>Class111_Sub2.method2117</c> unpacks hue at <c>&gt;&gt; 10</c>, saturation at
        /// <c>(x &gt;&gt; 3) &amp; 0x70</c> and luminance at <c>x &amp; 0x7f</c> before indexing a
        /// 65536-entry palette. It is what the client draws wherever a texture cannot be generated,
        /// and specifically the colour <see cref="suppressTexture"/> substitutes.
        /// <para>
        /// Held unsigned while <c>Class260.java:151</c> casts the same two bytes to a signed
        /// <c>short</c>, so records the client reads as negative read as positive here. The stored
        /// bytes are identical either way - the encoder writes the low sixteen bits - but an editor
        /// must not "correct" a value above 32767 by storing a signed one.
        /// </para>
        /// </remarks>
        public int representativeHsl {
            get => _representativeHsl;
            set => Set(ref _representativeHsl, value, MaterialColumn.RepresentativeHsl);
        }

        /// <summary>
        ///     Horizontal texture scroll speed, signed, in texels per 50 ms at the rasterised
        ///     resolution (<c>aByte1823</c>).
        /// </summary>
        /// <remarks>
        ///     Two independent paths agree on the axis: <c>Node_Sub2.java:116-137</c> adds it to the
        ///     column index of the software span, and <c>RenderType_Sub1.java:4427-4432</c> puts it
        ///     in the <c>x</c> slot of a <c>glTranslatef</c> on the texture matrix (mode 5890). The
        ///     sign is the direction. The period is <c>50 * textureSize</c>, which is where the unit
        ///     comes from.
        ///     <para>
        ///     Non-zero on either axis marks the model or terrain tile as animated so it is
        ///     re-uploaded each frame (<c>Renderable_Sub1.java:453</c>, <c>s_Sub3.java:241</c>).
        ///     </para>
        /// </remarks>
        public sbyte scrollU {
            get => _scrollU;
            set => Set(ref _scrollU, value, MaterialColumn.ScrollU);
        }

        /// <summary>
        ///     Vertical texture scroll speed, the partner of <see cref="scrollU"/> and read at the
        ///     identical sites (<c>aByte1837</c>).
        /// </summary>
        /// <remarks>
        ///     <c>RenderType_Sub2.java:1888</c> reaches <c>Node_Sub2.java:121-130</c>, where it
        ///     becomes a whole-row stride added to the row base, and
        ///     <c>RenderType_Sub1.java:4432</c> lands it in the <c>y</c> argument of the texture
        ///     matrix translate. Same units and same signedness as <see cref="scrollU"/>.
        ///     <para>
        ///     <b>The one caveat worth stating.</b> <c>RenderType_Sub3.java:4074-4076</c> passes the
        ///     pair in the opposite argument order to <c>RenderType_Sub1</c> and scatters them
        ///     through a 3x3 matrix nobody has chased. The U/V assignment rests on the software path
        ///     and on RT1's <c>glTranslatef</c>, which agree with each other.
        ///     </para>
        /// </remarks>
        public sbyte scrollV {
            get => _scrollV;
            set => Set(ref _scrollV, value, MaterialColumn.ScrollV);
        }

        /// <summary>
        ///     True when the byte is 1 (<c>Class260.java:166</c>). <b>Keeps its obfuscated name:
        ///     nothing in the 637 client reads it.</b>
        /// </summary>
        /// <remarks>
        ///     It is assigned at <c>Class260.java:166</c> and then appears only in the argument lists
        ///     of two <c>native</c> methods, <c>oa.java:160</c> and <c>oa.java:880</c>, declared at
        ///     <c>oa.java:132-135</c> and <c>oa.java:894-898</c>. No Java code branches on it, copies
        ///     it or derives anything from it, and <c>oa</c> hands all nineteen columns across the
        ///     JNI boundary in one call, so being present there says only that the column exists.
        ///     <para>
        ///     Its eighteen siblings were named from what the client does with them. Any name here
        ///     would be invented, and an invented name in this codebase reads as a settled one - the
        ///     model dump's five mislabelled face arrays are the standing example. It stays
        ///     <c>field1827</c> until something reads it.
        ///     </para>
        /// </remarks>
        public bool field1827 {
            get => _field1827;
            set => Set(ref _field1827, value, MaterialColumn.Field1827);
        }

        /// <summary>
        ///     Transpose the generated image, so pixel (row, col) lands at
        ///     <c>row + col * width</c> (<c>aBoolean1824</c>).
        /// </summary>
        /// <remarks>
        ///     True when the byte is 1 (<c>Class260.java:171</c>). It is the only column
        ///     <c>Class260</c> itself passes on, into the index-9 graph evaluator
        ///     (<c>Class260.java:225</c>, <c>:270</c>, <c>:319</c>), where it starts the write cursor
        ///     at the row index and advances it by the full width per pixel
        ///     (<c>Node_Sub46_Sub19.java:243-244</c> and <c>:288-291</c>).
        /// </remarks>
        public bool transposePixels {
            get => _transposePixels;
            set => Set(ref _transposePixels, value, MaterialColumn.TransposePixels);
        }

        /// <summary>
        ///     Build and use a mipmap chain for this texture rather than a single level-0 upload
        ///     (<c>aByte1832</c>).
        /// </summary>
        /// <remarks>
        ///     Selects the upload path at <c>Class364.java:109</c> into
        ///     <c>Class42_Sub1.java:151-181</c>, whose true side loops halving the image
        ///     (<c>Node_Sub46_Sub16.java:13-45</c>) and sets the "has a mipmap chain" flag that
        ///     drives the min filter (<c>Class42.java:223-231</c>).
        ///     <para>
        ///     <b>Signed, and the client is inconsistent about it.</b>
        ///     <c>Class319.java:100</c> and <c>Class364.java:109</c> test <c>!= 0</c> while
        ///     <c>Class48_Sub1_Sub1.java:168</c> and <c>Class48_Sub2_Sub1.java:289</c> test
        ///     <c>&gt; 0</c>, so a stored byte of 0x80..0xFF would mipmap on models and not on
        ///     skyboxes. The signed reading matches the field's declared Java type; neither cache
        ///     holds such a byte, so nothing on disk tells the two apart. It is a <c>byte</c> rather
        ///     than a <c>boolean</c> in <c>Class238</c> and nothing reads a magnitude out of it.
        ///     </para>
        /// </remarks>
        public sbyte mipmap {
            get => _mipmap;
            set => Set(ref _mipmap, value, MaterialColumn.Mipmap);
        }

        /// <summary>
        ///     Horizontal wrap mode: true is <c>GL_REPEAT</c>, false is <c>GL_CLAMP_TO_EDGE</c>
        ///     (<c>aBoolean1826</c>).
        /// </summary>
        /// <remarks>
        ///     True when the byte is 1 (<c>Class260.java:181</c>). Two independently written
        ///     renderers assign it to <c>GL_TEXTURE_WRAP_S</c> (<c>10242</c>) -
        ///     <c>Class42_Sub1.java:350-367</c> and <c>Class21_Sub1.java:244-251</c> - which is what
        ///     removes the risk of having this and <see cref="repeatV"/> the wrong way round. The
        ///     software rasteriser reduces the pair to "wraps at all"
        ///     (<c>RenderType_Sub2.java:2770-2772</c>).
        /// </remarks>
        public bool repeatU {
            get => _repeatU;
            set => Set(ref _repeatU, value, MaterialColumn.RepeatU);
        }

        /// <summary>
        ///     Vertical wrap mode, same encoding as <see cref="repeatU"/> (<c>aBoolean1819</c>).
        /// </summary>
        /// <remarks>
        ///     True when the byte is 1 (<c>Class260.java:186</c>), and assigned to
        ///     <c>GL_TEXTURE_WRAP_T</c> (<c>10243</c>) at <c>Class42_Sub1.java:362</c> and
        ///     <c>Class21_Sub1.java:250</c>.
        /// </remarks>
        public bool repeatV {
            get => _repeatV;
            set => Set(ref _repeatV, value, MaterialColumn.RepeatV);
        }

        /// <summary>
        ///     Upload as a 16-bit floating point surface, <c>GL_RGBA16F_ARB</c>, where the hardware
        ///     supports it (<c>aBoolean1817</c>).
        /// </summary>
        /// <remarks>
        ///     True when the byte is 1 (<c>Class260.java:191</c>). <c>Class364.java:98-102</c> gates
        ///     it on the renderer's capability probe and then hands over a <c>float[]</c> from the
        ///     float evaluator (<c>Node_Sub46_Sub19.java:148-216</c>) instead of the packed
        ///     <c>int[]</c> the normal path uses; <c>Class319.java:97-101</c> is the RT3 equivalent.
        ///     A precision request, not a transparency or filtering flag.
        /// </remarks>
        public bool halfFloatUpload {
            get => _halfFloatUpload;
            set => Set(ref _halfFloatUpload, value, MaterialColumn.HalfFloatUpload);
        }

        /// <summary>
        ///     Which of five fixed-function texture combine modes this texture draws with, 0 to 4
        ///     (<c>anInt1821</c>).
        /// </summary>
        /// <remarks>
        ///     Unsigned byte, read at <c>Class260.java:196</c> and dispatched five ways at
        ///     <c>RenderType_Sub1.java:4379-4408</c>, which sets <c>GL_COMBINE_RGB_ARB</c> and
        ///     <c>GL_COMBINE_ALPHA_ARB</c> through <c>method1899</c> (<c>:4495-4520</c>):
        ///     0 modulate/modulate, 1 replace/replace, 2 interpolate/replace, 3 add/modulate,
        ///     4 subtract/subtract. <c>RenderType_Sub3.java:3665-3688</c> is the same dispatch onto
        ///     state objects. <b>A value above 4 falls through every branch of both dispatchers and
        ///     silently leaves the previous combiner in place</b>, so it is worth refusing at the
        ///     editor rather than storing.
        /// </remarks>
        public int combineMode {
            get => _combineMode;
            set => Set(ref _combineMode, value, MaterialColumn.CombineMode);
        }

        /// <summary>
        ///     Water-shader parameters packed into one four-byte word, read at
        ///     <c>Class260.java:201</c> (<c>anInt1835</c>).
        /// </summary>
        /// <remarks>
        ///     <b>Not a tint.</b> It was taken for one once and multiplied into the generated pixels,
        ///     which scaled every texture in the editor towards black. The client names the fields
        ///     itself, in <c>glUniform</c> calls with string literals:
        ///     bits 0-15 <c>"breakWaterDepth"</c>, bits 16-17 <c>"breakWaterOffset"</c> over 8.0,
        ///     bits 19-22 <c>"waveIntensity"</c>.y and bits 23-26 its .x, each over 16.0, and bits
        ///     27-30 <c>"waveExponent"</c> - <c>Class151_Sub2.java:150-166</c> for effect 9,
        ///     <c>Class151_Sub4.java:135-146</c> for the reduced effect-8 form, and the unobfuscated
        ///     <c>Class76_Sub9.java:191-200</c>, which is what pins the widths the JODE masks cannot.
        ///     The two <c>waveIntensity</c> components are <b>reversed</b> relative to the order the
        ///     source lines appear in, so do not re-derive the axes from line order. The fragment
        ///     shader is itself a string literal in the client (<c>Class151_Sub2.java:72-82</c>) and
        ///     declares no colour uniform this could reach.
        ///     <para>
        ///     <b>Read by effect programs 2, 8 and 9</b>, not by the water shaders alone -
        ///     <c>Class151_Sub3.java:300</c> sits at slot 2 (<c>Class55.java:53-62</c>) and reads bit
        ///     0. It is <b>zero in every record of both caches</b> while programs 2 and 8 are both in
        ///     use, so why it is zero is unknown; "the effect is unused" is refuted, because the one
        ///     slot in either cache with <see cref="effectProgram"/> 8 - slot 701 - stores zero here
        ///     too. Nothing on disk can therefore test the layout, and only the file-length identity
        ///     holds the column at four bytes.
        ///     </para>
        /// </remarks>
        public int waterParams {
            get => _waterParams;
            set => Set(ref _waterParams, value, MaterialColumn.WaterParams);
        }

        /// <summary>
        ///     Where a textured span takes its alpha from: 2 the texel's own bits 24-31, 1 a binary
        ///     colour key on black, anything else the interpolated face alpha (<c>anInt1818</c>).
        /// </summary>
        /// <remarks>
        ///     Unsigned byte, read at <c>Class260.java:206</c>. The textured-span inner loop admits
        ///     only one reading of the three values (<c>SoftwareRasterizer.java:583-588</c>), and
        ///     everything else falls out of it: the <c>== 2</c> decoder switch at
        ///     <c>oa.java:872</c> and <c>Class364.java:104</c> picks the only decoder that writes an
        ///     alpha byte, and the <c>!= 1</c> test at <c>RenderType_Sub2.java:2383</c> suppresses a
        ///     3x3 box blur that would smear a colour key across its boundary.
        ///     <b>It is not a boolean</b>, and reading it as one loses the distinction between "no
        ///     transparency" and "colour-keyed".
        ///     <para>
        ///     One client inconsistency, recorded rather than smoothed over: the translucent-bucket
        ///     decision tests <c>== 2</c> in the software model builder
        ///     (<c>Renderable_Sub1.java:194</c>) and <c>!= 0</c> in both hardware ones
        ///     (<c>Renderable_Sub2.java:482</c>, <c>Renderable_Sub3.java:236</c>), so a mode-1
        ///     texture sorts differently between them.
        ///     </para>
        /// </remarks>
        public int alphaMode {
            get => _alphaMode;
            set => Set(ref _alphaMode, value, MaterialColumn.AlphaMode);
        }

        /// <summary>Sprite file IDs decoded from the TEXTURES index (9).</summary>
        public int[] spriteFileIds;

        /// <summary>Parsed procedural texture graph for lazy rendering.</summary>
        public TextureGraph graph;

        /// <summary>
        ///     The index 9 file exactly as it was stored, which is the only thing a save can write
        ///     back.
        /// </summary>
        /// <remarks>
        ///     <see cref="graph"/> cannot stand in for it. Decoding runs the client's post-decode
        ///     hooks, which overwrite decoded parameters with derived ones, and the format is
        ///     non-canonical in five separate ways - so re-serialising the graph would rewrite
        ///     files nobody edited. Like <see cref="graph"/> this comes from index 9 rather than
        ///     from the material table, so setting it must not mark this record dirty.
        /// </remarks>
        public TextureGraphRecord graphRecord;

        /// <summary>Thumbnail for GUI display.</summary>
        public Bitmap? thumb;

        /// <summary>
        ///     The 23 bytes this record was decoded from, or null when it was never decoded.
        /// </summary>
        /// <remarks>
        ///     Held so a column nobody edited re-encodes to what it came from rather than to what
        ///     its field would produce. Set by <see cref="MaterialTable"/> only - a record with no
        ///     stored bytes is encoded entirely from its fields.
        /// </remarks>
        internal byte[]? StoredRecord { get; set; }

        /// <summary>Whether any material column now differs from the bytes it was decoded from.</summary>
        public bool IsDirty => _dirtyColumns != 0;

        /// <summary>Whether one material column now says something its stored bytes do not.</summary>
        /// <param name="column">The column to test.</param>
        /// <returns>Whether that column must be re-encoded from its field.</returns>
        internal bool IsColumnDirty(MaterialColumn column) => (_dirtyColumns & (1 << (int) column)) != 0;

        /// <summary>
        ///     Declares the stored bytes and the fields to agree again.
        /// </summary>
        /// <remarks>
        ///     Called after a decode, where they agree by construction, and after a save, where the
        ///     bytes just written have been adopted as the stored ones. Calling it at any other
        ///     point loses an edit.
        /// </remarks>
        internal void MarkClean() => _dirtyColumns = 0;

        /// <summary>
        ///     Assigns a field, and records its column as edited only while the field and the stored
        ///     bytes disagree.
        /// </summary>
        /// <remarks>
        ///     <b>The bit is cleared again when a field is put back, which is a separate claim from
        ///     "an untouched record re-encodes to what it was read from".</b> A byte-identity sweep
        ///     only ever proves the second one, and an edit that nets nothing still has to write
        ///     nothing - the re-encode rewrites the archive CRC and drags in the reference-table entry
        ///     of everything packed beside it.
        ///     <para>
        ///     Latching the bit on the first assignment would be wrong for exactly the columns this
        ///     codec is careful about: a boolean column decodes many-to-one, so a stored byte of 2
        ///     reads as false, and a record edited to true and back to false would then re-encode that
        ///     column from its field and store a 0. The test is therefore against the stored bytes
        ///     rather than against the value the field held a moment ago.
        ///     </para>
        /// </remarks>
        /// <typeparam name="T">The field's type.</typeparam>
        /// <param name="slot">The backing field.</param>
        /// <param name="value">The new value.</param>
        /// <param name="column">The column the field is stored in.</param>
        private void Set<T>(ref T slot, T value, MaterialColumn column) where T : struct, IEquatable<T> {
            if (slot.Equals(value))
                return;

            slot = value;

            //A record with no stored bytes was built rather than decoded, so there is nothing to
            //agree with and every column of it is written from its field.
            if (StoredRecord != null && MaterialTable.ColumnMatchesStored(this, column))
                _dirtyColumns &= ~(1 << (int) column);
            else
                _dirtyColumns |= 1 << (int) column;
        }

        /// <summary>Releases the thumbnail and drops the parsed graph.</summary>
        public void Dispose() {
            thumb?.Dispose();
            thumb = null;
            graph = null;
            graphRecord = null;
        }
    }
}
