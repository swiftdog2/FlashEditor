# Index 26 material columns: what the 637 client does with each field

Reference tree: `C:\Users\CJ\Desktop\HydraScape\client\src`. All `file:line` citations are from that
tree. The record type is `Class238`; the loader is `Class260`'s constructor
(`Class260.java:101-215`), which runs nineteen sequential passes over the whole texture range, one
per column, in exactly the order our decoder reads them.

Every one of the nineteen fields is read somewhere. **One field (`field1827`) is read only by a
native method and never by any Java logic**, which is the closest thing to an UNREAD result here.

Two consumption sites carry more weight than all the others and are worth reading first:

- `SoftwareRasterizer.java:583-588` - the textured-span inner loop, which settles `field1818`.
- `Class151_Sub2.java:146-165` and `Class151_Sub4.java:131-145` - the only places in the client
  where a **GLSL uniform is named in a string literal**. They settle `field1816` and `field1835`.

The measured companion to this document is
`reference/index-survey/index-026-MATERIALS-column-census.md`, which says what each column actually
holds in the two caches on disk. The two were produced blind to each other.

---

## The names this project adopted

`TextureDefinition` and `MaterialColumn` carry these names, and each member's XML doc cites both its
client field and the section below that settles it. **`field1827` keeps its obfuscated name on
purpose** - see section 11.

| client field | `TextureDefinition` | `MaterialColumn` | settled by |
|---|---|---|---|
| `aBoolean1825` | `suppressTexture` | `SuppressTexture` | section 1 |
| `aBoolean1822` | `force64x64` | `Force64x64` | section 2 |
| `aBoolean1833` | `excludeFromDrawList` | `ExcludeFromDrawList` | section 3 |
| `aByte1829` | `colourGain` | `ColourGain` | section 4 |
| `aByte1830` | `greyBlendWeight` | `GreyBlendWeight` | section 5 |
| `aByte1820` | `effectProgram` | `EffectProgram` | section 6 |
| `aByte1816` | `effectParams` | `EffectParams` | section 7 |
| `aShort1831` | `representativeHsl` | `RepresentativeHsl` | section 8 |
| `aByte1823` | `scrollU` | `ScrollU` | section 9 |
| `aByte1837` | `scrollV` | `ScrollV` | section 10 |
| `aBoolean1827` | `field1827` | `Field1827` | **nothing - unread** |
| `aBoolean1824` | `transposePixels` | `TransposePixels` | section 12 |
| `aByte1832` | `mipmap` | `Mipmap` | section 13 |
| `aBoolean1826` | `repeatU` | `RepeatU` | section 14 |
| `aBoolean1819` | `repeatV` | `RepeatV` | section 15 |
| `aBoolean1817` | `halfFloatUpload` | `HalfFloatUpload` | section 16 |
| `anInt1821` | `combineMode` | `CombineMode` | section 17 |
| `anInt1835` | `waterParams` | `WaterParams` | section 18 |
| `anInt1818` | `alphaMode` | `AlphaMode` | section 19 |

`effectParams` is named for what it **is** rather than for any one effect's reading of it: section 7
shows five different bit layouts across five effect programs, so a name taken from the water
shaders' `"time"` and `"scale"` would be wrong on the other four.

---

## The nineteen fields

### 1. `field1825` - `Class238.aBoolean1825`

Loaded inverted: `Class260.java:116` sets it from `(readUnsignedByte() ^ 0xffffffff) == -1`, which
is `byte == 0`. Our encoder writing 0 for true is correct.

| Read site | What the code does |
|---|---|
| `Node_Sub16.java:78-80` | `if(!class238.aBoolean1825) { return class238.aShort1831; }` - the floor-overlay colour resolver returns the texture's representative HSL instead of using the texture |
| `Class278.java:714` | `if(i_163_ >= 0 && var_d.method11(i_163_,...).aBoolean1825) { i_163_ = -1; }` - the map-colour path discards the texture id outright |
| `s_Sub3.java:239` | `if(!class238.aBoolean1825) { bool_446_ = true; ... }` - terrain keeps per-vertex texture arrays only when the flag is clear |
| `s_Sub3.java:310` | `if(class238 != null && (class327.aByte2740 & 0x2) == 0 && !class238.aBoolean1825)` - textured tile branch, else the flat-colour branch |
| `s_Sub1.java:830-833` | `if(... && aBoolean1825) { i_215_ = -1; i_216_ = 128; }` - texture id dropped, light forced to neutral |
| `s_Sub2.java:772`, `s_Sub3.java:278, 2227, 2454, 2477-2519` | the same drop, in the other terrain builders |
| `Renderable_Sub1.java:185-191`, `:244-247`, `:447-449` | `if((i_787_ & 0x40) == 0 \|\| !aBoolean1825) { use texture } else { i_798_ = -1; }` |
| `Renderable_Sub2.java:466-471`, `:676` and `Renderable_Sub3.java:225`, `:393` | same pattern in the two hardware model builders |
| `RenderType_Sub2.java:2778` | `if((i_363_ != 65535) && !(...aBoolean1825))` gates the whole textured draw |

**Proves:** the flag suppresses the texture. Every consumer, in six independent subsystems, reacts
to `true` by throwing the texture id away and falling back to a flat colour, and `Node_Sub16.java:79`
names that colour as `aShort1831`. It is not "is textured", it is "**do not use this as a texture;
substitute its representative colour**". In models the suppression is conditional on a caller flag
`0x40`; in terrain, floor overlays and the map path it is unconditional.

**Confidence: SETTLED.**

---

### 2. `field1822` - `Class238.aBoolean1822`

Loaded at `Class260.java:121` as `byte == 1`.

| Read site | What the code does |
|---|---|
| `Class364.java:96` | `int i_1_ = (!class238.aBoolean1822 ? aHa_Sub1_3105.anInt4309 : 64);` and `i_1_` is then the width **and** height passed to the `Class42_Sub1` texture upload |
| `Class319.java:95` | same, against `anInt4607` |
| `RenderType_Sub2.java:2379`, `:2742` | `int i_144_ = (class238.aBoolean1822 \|\| aBoolean4491 ? 64 : this.anInt4482);` |
| `RenderType_Sub2.java:2868` | `method1925` exposes it as `aBoolean4491 \|\| ...aBoolean1822` |
| `RenderType_Sub1.java:4428`, `RenderType_Sub3.java:4069` | `int i_289_ = !class238.aBoolean1822 ? 128 : 64;` then `i_290_ = 50 * i_289_`, the UV-scroll period |
| `Class48_Sub1_Sub1.java:167`, `Class48_Sub2_Sub1.java:285` | the skybox picks the **smallest** size across its six faces |
| `Node_Sub10_Sub25.java:170` | `int i_5_ = (...aBoolean1822 ? 64 : 128);` |

`anInt4309`, `anInt4482` and `anInt4607` are all initialised to 128 (`RenderType_Sub1.java:130`,
`RenderType_Sub2.java:167`, `RenderType_Sub3.java:480`) and are the user texture-detail setting.

**Proves:** the flag forces this texture to be rasterised at 64x64 instead of the configured size.
It is a per-texture resolution cap, not a quality or detail hint.

**Confidence: SETTLED.**

---

### 3. `field1833` - `Class238.aBoolean1833`

Loaded at `Class260.java:126` as `byte == 1`.

| Read site | What the code does |
|---|---|
| `Renderable_Sub2.java:401-403` | `if(((anInt4837 & 0x40) == 0 \|\| !class238.aBoolean1825) && class238.aBoolean1833) { continue; }` inside the loop that builds the model's face list |
| `Renderable_Sub3.java:175-178` | identical |
| `oa.java:160`, `:879` | forwarded to the native renderer |

The `continue` is the same statement that the loop uses one line earlier to exclude render-type-2
faces (`model.aByteArray1414[i] != 2`), so the face is never added to `is[]` and never counted into
`anIntArray4851` / `anIntArray4932`.

**Proves:** a face whose texture carries this flag is **excluded from the draw list entirely**. The
guard means it only bites when the texture was actually going to be used - a texture that is already
being suppressed by `field1825` is not additionally skipped.

Worth recording: `Renderable_Sub1` (the software model builder) never reads it, so the same model
draws its faces under the software rasteriser and drops them under both hardware renderers.

**Confidence: SETTLED** for "excludes the face from the hardware draw list". Why content would want
that is not visible from the client.

---

### 4. `field1829` - `Class238.aByte1829`

Loaded signed at `Class260.java:131`, and masked `& 0xff` at every consumption.

| Read site | What the code does |
|---|---|
| `Renderable_Sub1.java:2440-2445` | `int i_436_ = class238.aByte1829 & 0xff; if(i_436_ != 0) { i_436_ += 256; ...each channel * i_436_, clamped to 65535, then >> 8 }` |
| `Renderable_Sub2.java:3927-3945` | identical |
| `Node_Sub20.java:150-166`, `Node_Sub30.java:492` | identical |
| `Renderable_Sub2.java:1090`, `:1099`; `Renderable_Sub3.java:809`, `:816` | captured into the same pair of locals as `aByte1830` |
| `RenderType_Sub3.java:537` | `aNativeInterface4526.initTextureMetrics(i_81_, class238.aByte1830, class238.aByte1829)` (`jagex3/graphics2/hw/NativeInterface.java:48`) |

**Proves:** a saturating multiplicative **gain on the vertex/lighting colour** before the texture is
applied. The multiplier is `(256 + v) / 256`, so 0 means "leave the colour alone" and 255 means
roughly double it, with per-channel clamping at 65535 before the `>> 8`. It is not a colour, an
alpha, or an offset - the operation is a multiply.

**Confidence: SETTLED.**

---

### 5. `field1830` - `Class238.aByte1830`

Loaded signed at `Class260.java:136`, masked `& 0xff` at every consumption.

| Read site | What the code does |
|---|---|
| `Renderable_Sub1.java:2428-2438` | `int i_432_ = class238.aByte1830 & 0xff; if(i_432_ != 0) { i_433_ = 131586 * shade; ... lerp the palette colour toward i_433_ by i_432_/256 }` |
| `Renderable_Sub2.java:3901-3923` | identical, with the shade clamped to 0..127 first |
| `Node_Sub20.java:129-146`, `Node_Sub30.java:468` | identical |
| `RenderType_Sub3.java:537` | passed to `initTextureMetrics` alongside `aByte1829` |

`131586` is `0x020202`, so `131586 * shade` is a pure grey whose channels are twice the shade level:
the monochrome equivalent of the lighting value.

**Proves:** a 0..255 **lerp weight from the surface's palette colour toward a neutral grey of the
same brightness**. 0 keeps the tint, high values discard it and leave plain lighting for the texture
to modulate. A desaturation weight, not a brightness.

Client quirk worth knowing: both `Renderable_Sub1.java:2430` and `Node_Sub20.java:141` special-case
`i_432_ == 256` for full replacement, but `i_432_` was just masked with `0xff` and can never reach
256. That branch is dead in every renderer, so a stored 255 is a 255/256 lerp and not an exact swap.

**Confidence: SETTLED.**

---

### 6. `field1820` - `Class238.aByte1820`

Loaded signed at `Class260.java:141`.

| Read site | What the code does |
|---|---|
| `RenderType_Sub1.java:4440` then `Class55.java:119-121` | `aClass151Array437[i_4_ & 0x7fffffff].method2440(...)` - used **directly as an array index** |
| `RenderType_Sub3.java:4085` then `RenderType_Sub3.java:4197-4245` | `aClass76Array4613[0x7fffffff & i_217_]` - same, on the shader-object array |
| `Class55.java:95-99` | `if(!bool_1_ && (i_4_ == 4 \|\| i_4_ == 8 \|\| i_4_ == 9)) { if(i_4_ == 4) i = i_2_; i_4_ = 2; }` - fallback when a hardware capability is missing |
| `Class319.java:104`, `Class364.java:104` | `!Node_Sub10_Sub7.method1023(1, class238.aByte1820)`, and `method1023` (`Node_Sub10_Sub7.java:22-27`) is `i != 1 && i != 7` |
| `s_Sub3.java:1029-1041` (`method3441`) | returns true for 4, 8 and 9 |
| `Node_Sub20.java:130`, `Node_Sub30.java:470` | `class238.aByte1820 != 4` gates the `aByte1830` grey blend |
| `Renderable_Sub1.java:186`, `Renderable_Sub2.java:467`, `Renderable_Sub3.java:226` | packed into the face sort key at `<< 8` so faces sharing a value batch together |

The arrays are ten wide and populated slot by slot at `Class55.java:53-62` and
`RenderType_Sub3.java:2655-2663`. Slot 0 is deliberately left null and means "no effect".

**Proves:** an **effect/material program id**, 0 to 9, indexing the renderer's table of texture
effects. Slot 4/8/9 are the water shaders (see `field1816` and `field1835`), slot 2 is the fallback
they degrade to. It is an enumerated id, not a flag and not a scalar.

**Confidence: SETTLED.**

---

### 7. `field1816` - `Class238.aByte1816`

Loaded signed at `Class260.java:146`.

| Read site | What the code does |
|---|---|
| `Class151_Sub2.java:150-165` (effect 9) | `int i_6_ = 1 << (0x3 & i);` -> `glUniform1fARB(..., "time", i_6_ * clock % 40000 / 40000.0F)`; `float f = (1 << (0x7 & i >> 3)) / 32.0F;` -> `glUniform1fARB(..., "scale", f)` |
| `Class151_Sub4.java:135-145` (effect 8) | the same two extractions feeding the same two uniforms, `"time"` and `"scale"` |
| `Class151_Sub1.java:175` (effect 4) | `if((0x1 & i ...) == 1)` selects an animated water frame set against a static one |
| `Class151_Sub3.java:299-335` (effect 2) | tests `i & 0x80`, `i & 0x40` and `i & 0x3` to choose texture bindings and `glProgramLocalParameter4fARB` values |
| `Class151_Sub7.java:122-129` (effect 5) | `float f = ((0x3 & i) + 1) * -5.0E-4F; float f_2_ = (1 + ((0x1d & i) >> 3)) * 5.0E-4F; f_3_ = (i & 0x40) != 0 ? 9.765625E-4F : 4.8828125E-4F;` and `i & 0x80` picks the scroll axis |
| `Class151_Sub6.java:197` (effect 1) | `aClass42_Sub2Array4995[i - 1]` - a **1-based index** into a three-element array (`Class151_Sub6.java:131-134`) |
| `Renderable_Sub1.java:187`, `Renderable_Sub2.java:468`, `Renderable_Sub3.java:227` | `i_791_ += i_794_ & 0xff;` into the face sort key |
| `RenderType_Sub1.java:4442`, `RenderType_Sub3.java:4084` | passed to `Class55.method508` / `method2045` and on to `method2441` / `method746` |

**Proves:** a **packed parameter byte whose interpretation belongs to the effect named by
`field1820`**. For the water shaders (effects 8 and 9) it decomposes into bits 0-1 = animation-speed
exponent (`"time"`) and bits 3-5 = scale exponent (`"scale"`); for effect 1 it is a 1-based frame-set
index; for effects 2 and 5 it is a different bit layout again. There is no single scalar meaning, and
reading it as a signed number is meaningless - it is a bit field.

**Confidence: SETTLED** that it is a per-effect packed bit field, and SETTLED for the `"time"` and
`"scale"` decomposition specifically, because the client names those uniforms itself.

---

### 8. `field1831` - `Class238.aShort1831` (already settled as 16-bit RS HSL)

Loaded at `Class260.java:151` as `(short) readUnsignedShort()`.

| Read site | What the code does |
|---|---|
| `Node_Sub16.java:79` | returned as the floor-overlay colour, into the same slot as `FloorOverlayConfig.anInt1540` and `anInt1537`, which are HSL |
| `Class278.java:730-732` | `Class221.anIntArray1665[Class111_Sub2.method2117(Class345.method3825(96, ...aShort1831, ...), 92) & 0xffff]` |
| `RenderType_Sub2.java:2872` | `return (this.aD938.method11(i, -28755).aShort1831 & 0xffff);` |
| `s_Sub3.java:2506`, `:2512`, `:2519` | same palette lookup as `Class278` |
| `oa.java:158`, `:877` | forwarded to the native renderer as a `short` (`oa.java:132`, `:895`) |

`Class221.anIntArray1665` is a 65536-entry table (`Class122.java:12-13`), so the index is a 16-bit
value. `Class111_Sub2.method2117` (`Class111_Sub2.java:8-37`) unpacks it as hue `>> 10`, saturation
`(x >> 3) & 0x70`, luminance `x & 0x7f`, rebalances saturation against luminance and repacks the
same way.

**Proves: the client agrees with our settled claim.** It is a raw 16-bit RS HSL colour, hue in bits
10-15, saturation in bits 7-9, luminance in bits 0-6, and it is the flat colour substituted whenever
`field1825` suppresses the texture.

One reading note: the client stores it in a Java `short`, so values above 0x7FFF are negative there
and every consumer masks `& 0xffff`. Our decoder holding 0..65535 is compatible and re-encodes
identically.

**Confidence: SETTLED.**

---

### 9. `field1823` - `Class238.aByte1823`

Loaded signed at `Class260.java:156`.

| Read site | What the code does |
|---|---|
| `RenderType_Sub2.java:1887` | `class98_sub2.method949((class238.aByte1823 * i_614_ * 50 / 1000), (class238.aByte1837 * ...))` |
| `Node_Sub2.java:116-137` (`method949`) | the first argument becomes `i_2_`, used as `(i_8_ + i_2_) & i_3_` where `i_8_` is the column and `i_3_` is `width - 1` |
| `RenderType_Sub1.java:4427-4432` | `method1857((clock % i_290_ * aByte1823) / i_290_, 0.0F, (byte) 44, (aByte1837 * (clock % i_290_)) / i_290_)`, and `method1857` (`RenderType_Sub1.java:3496-3510`) does `glMatrixMode(5890); glTranslatef(f, f_127_, f_126_)` on the **texture** matrix |
| `RenderType_Sub3.java:4068-4076` | the RT3 equivalent, through `Class111_Sub3.method2119` |
| `Renderable_Sub1.java:453`, `:507`; `Renderable_Sub2.java:470`, `:501`, `:1102`; `Renderable_Sub3.java:251`, `:818` | `if(aByte1823 != 0 \|\| aByte1837 != 0)` marks the model as animated so it is re-uploaded each frame |
| `s_Sub3.java:241`, `:317` | the same test sets `aByte2747 \|= 0x4` / `aByte2740 \|= 0x4` on a terrain tile |

**Proves:** the **U (horizontal) scroll speed** of the texture, signed so the sign is the direction.
Two independent paths agree: `Node_Sub2.method949` adds it to the column index, and
`RenderType_Sub1` puts it in the `x` slot of a `glTranslatef` on the texture matrix.

The period is `50 * textureSize`, so the unit is texels per 50 ms at the rasterised resolution.

**Confidence: SETTLED.**

---

### 10. `field1837` - `Class238.aByte1837`

Loaded signed at `Class260.java:161`. Read at the identical sites as `field1823`, always as its
partner.

| Read site | What the code does |
|---|---|
| `RenderType_Sub2.java:1888` -> `Node_Sub2.java:121-130` | the second argument becomes `i_4_ = anInt3822 * i_0_`, a whole-**row** stride added to the row base |
| `RenderType_Sub1.java:4432` | lands in the `y` argument of `glTranslatef` on the texture matrix |
| `RenderType_Sub3.java:4074` | the RT3 equivalent |

**Proves:** the **V (vertical) scroll speed**, signed, same units as `field1823`.

Caveat stated rather than hidden: `RenderType_Sub3.java:4074-4076` passes the two in the opposite
argument order to `RenderType_Sub1`, and `Class111_Sub3.method2119` scatters them into a 3x3 matrix
whose layout I did not chase. The U/V assignment above rests on the software path and on RT1's
`glTranslatef`, which agree with each other.

**Confidence: SETTLED.**

---

### 11. `field1827` - `Class238.aBoolean1827`

Loaded at `Class260.java:166` as `byte == 1`.

| Read site | What the code does |
|---|---|
| `oa.java:160` | passed as `bool_34_` to `AA(...)`, declared `private final native void AA(...)` at `oa.java:132-135` |
| `oa.java:880` | passed to `CA(...)`, likewise native, `oa.java:894-898` |

**There is no other read anywhere in the client.** No Java code branches on it, stores it, or
derives anything from it.

**Proves:** nothing about its meaning. `oa` is the native (non-OpenGL) renderer bridge and it hands
all nineteen columns across the JNI boundary in one call, so being present there says only that the
column exists. Any name attached to this field would be invented.

**Confidence: UNREAD** (assigned at `Class260.java:166`, consumed by no Java code; forwarded blind
to a native method whose body is not in this tree).

---

### 12. `field1824` - `Class238.aBoolean1824` (already settled as pixel transposition)

Loaded at `Class260.java:171` as `byte == 1`.

| Read site | What the code does |
|---|---|
| `Class260.java:225`, `:270`, `:319` | the only argument `Class260` itself passes on, into `Node_Sub46_Sub19.method1630` / `method1633` / `method1631` |
| `Node_Sub46_Sub19.java:243-244`, `:288-291` | in `method1631`: `if(bool_18_) { i_25_ = i_26_; }` before the row loop, then `is[i_25_++] = i_36_; if(bool_18_) { i_25_ += i_19_ - 1; }` |
| `Node_Sub46_Sub19.java:335-336`, `:381-383` | the same two statements in `method1633` |
| `oa.java:162`, `:881` | forwarded to the native renderer |

With the flag set, the write cursor starts at the **row** index and advances by the full width per
pixel, so pixel `(row, col)` lands at `row + col * width`. With it clear the cursor is sequential.

**Proves: the client agrees with our settled claim.** It is a transpose of the generated image, and
it is applied by the index-9 graph evaluator on output, exactly as our note says.

**Confidence: SETTLED.**

---

### 13. `field1832` - `Class238.aByte1832`

Loaded signed at `Class260.java:176`.

| Read site | What the code does |
|---|---|
| `Class364.java:109` | `new Class42_Sub1(..., class238.aByte1832 != 0, is, 0, 0, false)`, position 6 |
| `Class42_Sub1.java:151-181` | that argument is `bool`: `if(anInt3226 == 34037 \|\| !bool \|\| ...) { glTexImage2Di(...); method373(true, false); } else { Class336.method3773(...); method373(true, true); }` |
| `Class364.java:101` -> `Class42_Sub1.java:121-146` | the float-texture variant, same branch shape, `Class2.method168` on the true side |
| `Class42_Sub1.java:83-118` -> `Node_Sub46_Sub16.java:13-45` | the byte variant; `method1613` loops halving the image and calling `glTexImage2Dub` at increasing levels |
| `Class319.java:100`, `:109` | the RT3 path, `(aByte1832 ^ 0xffffffff) != -1` and `aByte1832 != 0` |
| `Class48_Sub1_Sub1.java:168` | `if(class238.aByte1832 > 0) { i_0_ = 1; }`, then `new Class42_Sub2(..., i_0_ != 0, ...)` for the skybox |
| `Class48_Sub2_Sub1.java:289` | `if((class238.aByte1832 ^ 0xffffffff) < -1)`, that is `> 0`, feeding `method1934(8, bool, ...)` |

`Class42.method373(true, true)` sets `aBoolean3225` (`Class42.java:223-231`), the "this texture has a
mipmap chain" flag that drives the min filter.

**Proves:** it selects the **mipmap-building upload path**. Non-zero means build and use a mipmap
chain; zero means a single `glTexImage2D` at level 0.

Signedness is load-bearing and the client is inconsistent about it: `Class319`/`Class364` test
`!= 0` while `Class48_Sub1_Sub1`/`Class48_Sub2_Sub1` test `> 0`, so a stored byte of 0x80..0xFF would
mipmap on models and not on skyboxes. Our signed reading matches the field's declared Java type.

**Confidence: SETTLED** for "mipmap on/off". Whether the client intended more than a flag - it is a
`byte`, not a `boolean`, and nothing reads a magnitude out of it - is not decidable from here.

---

### 14. `field1826` - `Class238.aBoolean1826`

Loaded at `Class260.java:181` from `(readUnsignedByte() ^ 0xffffffff) == -2`, that is `byte == 1`.

| Read site | What the code does |
|---|---|
| `Class364.java:112` -> `Class42_Sub1.java:350-367` | `method383(aBoolean1819, 10242, aBoolean1826)`, and the body does `glTexParameteri(target, 10242, !bool_63_ ? 33071 : 10497)` where `bool_63_` is `aBoolean1826` |
| `Class319.java:111` -> `Class21_Sub1.java:244-251` | `method46(aBoolean1826, aBoolean1819, -97)`, body `glTexParameteri(target, 10242, !bool ? 33071 : 10497)` where `bool` is `aBoolean1826` |
| `RenderType_Sub2.java:2770-2772` | `method1922` returns `aBoolean1826 \|\| aBoolean1819`, the software test for "wraps at all" |
| `oa.java:161`, `:880` | forwarded to the native renderer |

`10242` is `GL_TEXTURE_WRAP_S`, `10497` is `GL_REPEAT`, `33071` is `GL_CLAMP_TO_EDGE`.

**Proves:** the **horizontal (S/U) wrap mode**. True means `GL_REPEAT`, false means
`GL_CLAMP_TO_EDGE`. Two independently written renderers assign it to `GL_TEXTURE_WRAP_S`, which is
what removes the risk of having the pair the wrong way round.

**Confidence: SETTLED.**

---

### 15. `field1819` - `Class238.aBoolean1819`

Loaded at `Class260.java:186` as `byte == 1`.

| Read site | What the code does |
|---|---|
| `Class364.java:112` -> `Class42_Sub1.java:362` | `glTexParameteri(target, 10243, !bool ? 33071 : 10497)` where `bool` is `aBoolean1819` |
| `Class319.java:111` -> `Class21_Sub1.java:250` | `glTexParameteri(target, 10243, bool_7_ ? 10497 : 33071)` where `bool_7_` is `aBoolean1819` |
| `RenderType_Sub2.java:2770-2772` | the same `method1922` disjunction |
| `oa.java:161`, `:880` | forwarded to the native renderer |

`10243` is `GL_TEXTURE_WRAP_T`.

**Proves:** the **vertical (T/V) wrap mode**, same encoding.

**Confidence: SETTLED.**

---

### 16. `field1817` - `Class238.aBoolean1817`

Loaded at `Class260.java:191` as `byte == 1`.

| Read site | What the code does |
|---|---|
| `Class364.java:98-102` | `if(class238.aBoolean1817 && aHa_Sub1_3105.method1768()) { float[] fs = aD3101.method10(...); class42_sub1 = new Class42_Sub1(aHa_Sub1_3105, 3553, 34842, i_1_, i_1_, ..., fs, 6408); }` else the integer path |
| `Class319.java:97-101` | the RT3 equivalent, `method2066(Class62.aClass164_486, ..., fs, false, i_1_, i_1_)` |
| `oa.java:161`, `:881` | forwarded to the native renderer |

`3553` is `GL_TEXTURE_2D`, `34842` is `GL_RGBA16F_ARB`, `6408` is `GL_RGBA`, and the data handed over
is a `float[]` produced by `Node_Sub46_Sub19.method1630` (`Node_Sub46_Sub19.java:148-216`) rather
than the `int[]` the normal path uses. `method1768` is the renderer's capability probe and returns a
flat `false` on the software rasteriser (`RenderType_Sub2.java`).

**Proves:** the texture is to be uploaded as a **16-bit floating point (HDR) surface** when the
hardware supports it, decoded through the float evaluator instead of the packed-integer one. It is a
precision request, not a transparency or filtering flag.

**Confidence: SETTLED.**

---

### 17. `field1821` - `Class238.anInt1821`

Loaded at `Class260.java:196` as `readUnsignedByte()`.

| Read site | What the code does |
|---|---|
| `RenderType_Sub1.java:4437` | `i_285_ = class238.anInt1821;` then `method509(class42_sub1, false, i_285_)` and `method1896(260, i_285_)` |
| `RenderType_Sub1.java:4379-4408` (`method1896`) | dispatches on the value: `1 -> method1899(7681, 8960, 7681)`, `0 -> (8448, 8960, 8448)`, `2 -> (7681, i+8700, 34165)`, `3 -> (8448, 8960, 260)`, `4 -> (34023, 8960, 34023)` |
| `RenderType_Sub1.java:4495-4520` (`method1899`) | `glTexEnvi(8960, 34161, i_292_); glTexEnvi(8960, 34162, i);` |
| `RenderType_Sub3.java:4081` -> `method2015` (`RenderType_Sub3.java:3665-3688`) | the same five-way dispatch, onto pairs of `Class128` state objects |
| `Class151_Sub5.java:82-88`, `Class151_Sub6.java`, `Class151_Sub8.java`, `Class151_Sub9.java` | each effect forwards it through `method2442` into `method1896` |
| `oa.java:162`, `:881` | forwarded to the native renderer |

`8960` is `GL_TEXTURE_ENV`, `34161` is `GL_COMBINE_RGB_ARB`, `34162` is `GL_COMBINE_ALPHA_ARB`.
`7681` is `GL_REPLACE`, `8448` is `GL_MODULATE`, `260` is `GL_ADD`, `34165` is `GL_INTERPOLATE_ARB`,
`34023` is `GL_SUBTRACT_ARB`.

**Proves:** the **texture combine mode**, an enum 0 to 4:

| value | RGB combiner | alpha combiner |
|---|---|---|
| 0 | `GL_MODULATE` | `GL_MODULATE` |
| 1 | `GL_REPLACE` | `GL_REPLACE` |
| 2 | `GL_INTERPOLATE` | `GL_REPLACE` |
| 3 | `GL_ADD` | `GL_MODULATE` |
| 4 | `GL_SUBTRACT` | `GL_SUBTRACT` |

A value above 4 falls through every branch of both dispatchers and silently leaves the previous
combiner in place.

**Confidence: SETTLED.**

---

### 18. `field1835` - `Class238.anInt1835`

Loaded at `Class260.java:201` as `readInt()`, four bytes.

| Read site | What the code does |
|---|---|
| `Class151_Sub2.java:152-166` (effect 9) | `int i_7_ = i_4_ & 0xffff;` -> `glUniform1fARB(..., "breakWaterDepth", i_7_)`; `float f_8_ = ((i_4_ >> 16) & ...) / 8.0F;` -> `"breakWaterOffset"`; `(i_4_ >> 19) & 0xf` and `(i_4_ >> 23) & 0xf`, each `/ 16.0F` -> `glUniform2fARB(..., "waveIntensity", ...)`; `i_4_ >> 27` -> `"waveExponent"` |
| `Class151_Sub4.java:135-146` (effect 8) | the reduced form: `0xffff & i_1_` -> `"breakWaterDepth"`, `(i_1_ >> 16) / 8.0F` -> `"breakWaterOffset"` |
| `Class151_Sub3.java:300` (effect 2) | reads **bit 0** of the word |
| `Class76_Sub9.java:191-200` | the unobfuscated sibling, which is what pins the bit layout |
| `RenderType_Sub1.java:4441` -> `Class55.java:109`, `:121` | reaches `Class151.method2441` as the second parameter |
| `RenderType_Sub3.java:4086` -> `method2045` -> `Class76.method746` | the RT3 route to the same place |
| `oa.java:158`, `:878` | forwarded to the native renderer |

**Proves:** it is a **packed water-shader parameter word**, not a colour and not a tint. The layout,
from `Class151_Sub2.java:150-166`, `Class151_Sub4.java:135-146` and the unobfuscated
`Class76_Sub9.java:191-200`:

| bits | uniform | scaling |
|---|---|---|
| 0-15 | `breakWaterDepth` | raw |
| 16-17 | `breakWaterOffset` | `/ 8.0` |
| 19-22 | `waveIntensity.y` | `/ 16.0` |
| 23-26 | `waveIntensity.x` | `/ 16.0` |
| 27-30 | `waveExponent` | raw |

Two corrections to an earlier reading of this row, both worth stating because both were plausible:
**bit 18 is not part of `breakWaterOffset` and bit 31 is not part of `waveExponent`**, and the
`waveIntensity` components are **reversed** relative to the order the source lines appear in, so the
axes must not be inferred from line order.

**It is not read only by the water shaders.** `Class151_Sub3` sits at effect slot 2
(`Class55.java:53-62` builds the ten-slot array; slot 8 is `Class151_Sub4` and slot 9 is
`Class151_Sub2`) and reads bit 0 of this word at `Class151_Sub3.java:300`. Effect 2 is in real use:
21 of 915 vanilla slots and 22 of 1408 repack slots carry `field1820 == 2`. So programs 2, 8 and 9
all read it.

**Why it is zero in every 639 record is unknown, and "the effect is unused" is refuted.** Both caches
hold exactly one slot with `field1820 == 8` - slot 701 - and its `field1835` is zero, byte-identically
in both. Effect programs 2 and 8 are both in use and both read the word, and it is still zero
everywhere. Do not repeat the earlier explanation that no shipped texture drives a water shader.

*Inference, not fact, recorded because it is the only lead here:* nothing guards on the word before
dispatch and the fragment shader computes `waterDepth/breakWaterDepth`, so a stored zero is a division
by zero in GLSL. That reads more like the packer never populating the field in build 639 than an
intended value - but nobody has run the shader and the data has no opinion, so it stays labelled.

**Nothing here reads it as a colour**, which independently confirms that treating it as a tint was
wrong.

The bit masks in the decompiled source (`0x37eb0`, `0x36757`, `0x7f9b2e24`) are JODE obfuscation
noise layered over the real masks; the shifts and divisors are the reliable part, and
`Class76_Sub9.java:191-200` is unobfuscated and settles the widths the masks cannot.

The fragment shader itself is a string literal in the client, so the uniforms can be read in use as
well as by name. `Class151_Sub2.java:72-82` declares `uniform vec2 waveIntensity; uniform float
waveExponent; uniform float breakWaterDepth; uniform float breakWaterOffset;` and then computes
`clamp(waterDepth/breakWaterDepth - breakWaterOffset*wnNormal.w, 0.0, 1.0)` as a shore factor,
`pow(1.0-shoreFactor, waveExponent)` as a wave factor, and mixes toward
`(waveIntensity.x*wnNormal.wwww)+waveIntensity.y`. `Class151_Sub4.java:61-69` carries the reduced
variant with only the two break-water uniforms. There is no colour uniform anywhere in either
shader that this field could reach.

**Confidence: SETTLED** as "packed water-shader parameters, uniform names taken from the client's own
string literals", and SETTLED for the bit layout now that `Class76_Sub9.java:191-200` has been read
against it. Corrected 2026-08-17: the first pass of this row had bits 16-18 and 27-31, and claimed
the word was read only by effects 8 and 9.

---

### 19. `field1818` - `Class238.anInt1818`

Loaded at `Class260.java:206` as `readUnsignedByte()`.

| Read site | What the code does |
|---|---|
| `SoftwareRasterizer.java:583-588` | `if(anInt147 == 2) { i_59_ = i_58_ >> 24 & 0xff; } else if(anInt147 == 1) { i_59_ = (i_58_ == 0) ? 0 : 255; } else { i_59_ = (int) f_37_; }` where `i_58_` is the texel and `i_59_` the blend alpha |
| `SoftwareRasterizer.java:1673`, `:3250` | `anInt147 = aHa_Sub2_131.method1912(i_151_);` and `RenderType_Sub2.java:2302-2303` is `return ...anInt1818;` |
| `oa.java:872` | `if(class238.anInt1818 != 2) { method9(...) } else { method13(...) }` - selects the RGB decoder against the ARGB decoder |
| `Class319.java:103`, `Class364.java:104`, `Class346.java:255`, `:260`, `:264` | the same `== 2` decoder selection, plus a background fill and a draw-mode switch in `Class346` |
| `Node_Sub46_Sub19.java:284-286` vs `:381` | `method1631` (the `!= 2` path) forces `i_36_ \|= 0xff000000` unless the pixel is black; `method1633` (the `== 2` path) takes a real alpha from `is_49_` |
| `RenderType_Sub2.java:2383`, `:2746` | `new Node_Sub2(i, i_144_, ..., class238.anInt1818 != 1)`, and `Node_Sub2.java:21-105` applies a 3x3 box blur over all four channels when that flag is set |
| `Renderable_Sub1.java:170`, `:194`, `:450` | `anInt1818 == 2` marks the model as needing the translucent pass |
| `Renderable_Sub2.java:443`, `:482`; `Renderable_Sub3.java:208`, `:236` | `anInt1818 != 0` for the same decision |

**Proves:** an **alpha-source mode**, three valued, and `SoftwareRasterizer.java:583-588` admits only
one reading of the three values:

| value | meaning |
|---|---|
| 0 | the texture carries no transparency; the span uses the interpolated face alpha |
| 1 | binary colour-key: a texel of exactly `0x000000` is fully transparent, everything else opaque |
| 2 | the texture carries an 8-bit alpha channel, taken from bits 24-31 of the texel |

Everything else falls out of that. The `== 2` decoder switch is why mode 2 needs `method1633`, which
is the only decoder that writes an alpha byte. The `!= 1` blur test is coherent because blurring a
colour-keyed texture would smear the key across the boundary. It is **not** a boolean and reading it
as one loses the distinction between "no transparency" and "colour-keyed".

One client inconsistency worth recording rather than smoothing over: for the "does this face go in
the translucent bucket" decision, the software model builder tests `== 2` (`Renderable_Sub1.java:194`)
while both hardware builders test `!= 0` (`Renderable_Sub2.java:482`, `Renderable_Sub3.java:236`).
A mode-1 texture therefore sorts differently between the software and hardware renderers.

**Confidence: SETTLED.**

---

## Summary sections

### Fields the client never reads

**One: `field1827` / `aBoolean1827`.** It is assigned at `Class260.java:166` and then appears only
at `oa.java:160` and `oa.java:880`, both of which are argument lists for `native` methods
(`oa.java:132-135`, `oa.java:894-898`). No Java code in the tree branches on it, copies it, or
derives anything from it. Its purpose is not recoverable from this decompile and should not be
guessed at.

Two near-misses that are **not** in this category, stated so nobody re-derives them as unread:

- `field1833` is read only by `Renderable_Sub2` and `Renderable_Sub3`, and not by the software model
  builder. It is genuinely consumed.
- `field1835` is read by effect programs 2, 8 and 9. It is zero in every record of both caches while
  programs 2 and 8 are both in use, so it does not take effect anywhere in either cache - and **why
  it is zero is unknown**, not explained by the programs being unused. See section 18.

### Contradictions with the widths and signedness our decoder uses

**No width contradiction exists.** All nineteen widths in `MaterialTable.ColumnWidths` match
`Class260.java:114-208` exactly: seventeen single bytes, one `readUnsignedShort` at pass 8 and one
`readInt` at pass 18.

Signedness is a different matter. Nothing below breaks the byte-identity round trip, because
`MaterialTable` replays stored bytes per column, but three of them make the editor's surface show a
value the client would never compute:

1. **`field1829` and `field1830` should be presented as 0..255, not as `sbyte`.** The client reads
   them signed into a `byte` field, but **every** consumption masks `& 0xff` first
   (`Renderable_Sub1.java:2428`, `:2440`; `Renderable_Sub2.java:3901`, `:3927`; `Node_Sub20.java:129`,
   `:150`; `Node_Sub30.java:468`, `:492`). Their meaningful range runs to 255 - a near-total grey
   blend and roughly a doubling of brightness - and our `sbyte` surface renders those as -1. This is
   the most likely of the three to mislead a user editing the Materials tab.

2. **`field1816` is a bit field, not a number.** It is consumed as `& 0x3`, `>> 3 & 0x7`, `& 0x40`,
   `& 0x80`, `& 0x1d`, and at `Class151_Sub6.java:197` as a 1-based array index. Presenting it as a
   signed scalar is not wrong on the wire but is unhelpful; the decomposition depends on `field1820`.

3. **`field1835` is a bit field too**, five packed sub-values, and our `int` reading matches Java's
   `readInt` including the arithmetic `>> 27`. The previous "tint" reading was not a width or
   signedness error, it was a semantic one, and the evidence against it is that no consumer anywhere
   feeds it to a palette or a colour blend.

Two things that look like bugs and are not:

- `field1832` as `sbyte` is correct - it is a `byte` in `Class238`, and `Class48_Sub1_Sub1.java:168`
  genuinely tests `> 0` while `Class364.java:109` tests `!= 0`. That asymmetry belongs to the client.
- `field1831` as an unsigned 0..65535 `int` is correct and safer than the client's `short`, since
  every client consumer masks `& 0xffff` anyway.

One validation opportunity rather than a bug: `field1820` is used directly as an index into a
ten-element array (`Class55.java:53-62`, `RenderType_Sub3.java:2655-2663`), and `field1821` selects
among exactly five combine modes. Values outside 0..9 and 0..4 respectively either crash the client
or silently do nothing, so they are worth rejecting at the editor.

### The two already-settled claims

**`field1831` - the client agrees.** `Node_Sub16.java:79` returns it into a slot otherwise filled by
HSL floor-overlay colours; `Class278.java:730-732` and `s_Sub3.java:2506` push it through
`Class111_Sub2.method2117`, which unpacks hue at `>> 10`, saturation at `(x >> 3) & 0x70` and
luminance at `x & 0x7f`, and then index a 65536-entry palette. It is raw 16-bit RS HSL, exactly as
recorded, and it is specifically the colour that substitutes for the texture when `field1825` is set.

**`field1824` - the client agrees.** `Class260.java:225`, `:270` and `:319` pass it into
`Node_Sub46_Sub19`, and at `Node_Sub46_Sub19.java:243-244` and `:288-291` (and again at `:335-336` and
`:381-383`) it changes the output write cursor from sequential to `row + col * width`. That is a
transpose of the generated image, applied by the index-9 evaluator, which is what our note says.

Neither settled claim needs revising.

### 637 against 639

Nothing in index 26 required a 639-only handler. All nineteen columns are read by the 637 loader,
and eighteen of them are consumed by 637 code. The single gap - `field1827` - is a gap in this
decompile's visibility (a native call), not a build mismatch, so there is no case here for the
"the client lacks a handler the data needs" exception.
