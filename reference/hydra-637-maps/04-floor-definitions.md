# Floor underlay and overlay definitions

Both live in **JS5 index 2** (`client.BIT_CONFIG`, `InterfaceSettings.java:160`). The prior spec
puts them in indexes 3 and 4, which is wrong.

| Type | Index | Group | Count in shipped cache | Loader |
|---|---:|---:|---:|---|
| Underlay | 2 | **1** | **159** (ids 0-158) | `Class153.java:201,241` |
| Overlay | 2 | **4** | **235** (ids 0-234) | `Class32.java:79,129` |

Definition id is used directly as the file id inside the group.

**MEASURED**: every one of the 159 + 235 files decodes to exact buffer consumption with the tables
below, with zero unknown opcodes. Index 2 reference table is protocol 6, version 785, flags `0x00`
(not name-hashed, so look these up by numeric group id).

Decode as a sequential opcode loop with last-write-wins, not a one-shot switch: **MEASURED**,
overlay 94 genuinely emits opcode 11 twice (255, then 127).

## 1. FloorUnderlay

`FloorUnderlay.java:21-48`. Five opcodes.

| Op | Field | Read | Notes |
|---:|---|---|---|
| 0 | - | - | Terminator |
| 1 | colour | **u24 big-endian** | Immediately decomposed by `method718`, see below |
| 2 | textureId | **u16**, `0xFFFF` to -1 | Not a `u8`. Unlike the overlay's opcode 2, which is a byte |
| 3 | textureScale | **u16 shifted left 2** | Default 512 |
| 4 | castsShadow = false | none | The prior spec calls this "unknown flag" |
| 5 | occludes = false | none | The prior spec calls this "unknown flag" |

> Opcodes 2 and 3 were wrong in the first draft of this file, which had them as a `u8` and a plain
> `u16`. Corrected against `FloorUnderlay.java:23-31` and confirmed by round-tripping all 159 shipped
> definitions byte-exactly. The overlay table below was right first time.

> The colour read is **not** a smart. `RSBuffer.method1186` (`RSBuffer.java:131-135`) is a plain
> 3-byte big-endian unsigned read. The prior spec says "smart" in three places; there is no smart
> read anywhere in either floor decoder.

### The underlay stores four values, not a packed HSL

**CONFIRMED**, `FloorUnderlay.method718` (`FloorUnderlay.java:112-134`), called **during** decode
from inside opcode 1, not post-decode.

An underlay does **not** carry a packed HSL short at all. It carries four separate accumulator
components, precisely so the terrain blender can area-average them before packing:

| Field | Meaning |
|---|---|
| `anInt538` | hue **pre-multiplied** by a chroma-derived weight |
| `anInt540` | the hue multiplier / weight itself |
| `anInt541` | saturation |
| `anInt542` | lightness |

The blender sums all four over a neighbourhood, then divides hue by the summed weight and sat/light
by the plain count. That weighting is why grey and near-grey tiles do not drag the average hue
around. See `05-colour-and-rendering.md`.

> `FloorUnderlay.method718:75` computes the green channel as `(i & 0xff10) >> 8` where `0xff00` is
> clearly intended. **Verified harmless**: `0x10 >> 8 == 0`, so the result is identical. It is a
> JODE artifact. Flagged so nobody "fixes" it into something that changes behaviour.

## 2. FloorOverlayConfig

`FloorOverlayConfig.java:110-179`. Thirteen handled opcodes.

| Op | Field | Read | Notes |
|---:|---|---|---|
| 0 | - | - | Terminator |
| 1 | primary colour | u24 BE, then RGB to HSL | `0xFF00FF` maps to -1, meaning "show the underlay through" |
| 2 | textureId | u8 | **UNTESTED**, zero occurrences in the shipped cache |
| 3 | textureId | u16 | `0xFFFF` maps to -1 |
| 5 | `aBoolean1527 = false` | none | Disables the tile's participation in flat-ground occluders |
| 7 | secondary colour | u24 BE, then RGB to HSL | Second colour channel; also the flat-colour override for the low-detail renderer and the world map |
| 8 | mark as world-map background | none | Writes this definition's id into `Class32.anInt312`. Exactly one overlay (id 5) uses it |
| 9 | textureScale | `u16 << 2` | |
| 10 | `castsShadow = false` | none | **UNTESTED**, zero occurrences |
| 11 | priority | u8 | |
| 12 | blendWithNeighbours = true | none | |
| 13 | **water tint RGB** | u24 BE, raw RGB | Not HSL |
| 14 | **water depth** | `u8 << 2` | |
| 16 | **water alpha** | u8 | |

Opcodes **4, 6 and 15 do not exist**. They consume nothing and would desync the stream. The prior
spec lists 4 and 6 as real fields.

### Defaults

The prior spec omits these entirely and they matter (`FloorOverlayConfig.java:78-79, 92-103`):

| Field | Default |
|---|---|
| primary hsl | `0` (black, **not** transparent) |
| secondary hsl | `-1` |
| textureId | `-1` |
| textureScale | `512` |
| priority | `8` |
| waterTintRgb | `0x122F3D` |
| waterDepth | `64` |
| waterAlpha | `127` |
| occludes, castsShadow | `true` |
| blendWithNeighbours | `false` |

### The mandatory post-decode step

**CONFIRMED**: `Class32.java:137` **unconditionally** calls `FloorOverlayConfig.method2691`
(`FloorOverlayConfig.java:169-179`), including for a null file, which rewrites:

```
priority = (priority << 8) | definitionId
```

Anything comparing overlay priorities must compare the composite. Skipping this gets the
neighbour-blend tie-breaking wrong.

## 3. Opcodes 13, 14 and 16 are water, not minimap

This was disputed between agents and settled by tracing every consumer.

**VERDICT: water.** These three feed only the underwater / water rendering path. The ARB vertex
programs that read them literally declare `PARAM waterPlane` and `PARAM fogParams`. **No minimap or
world-map rasteriser reads any of them** - the map colour is opcode 1, with opcode 7 and the
opcode-2/3 texture as overrides, exactly as `Class278.method3311` and `Node_Sub16.method1149` do.

The submersion model to reproduce:

```
factor = clamp(-distanceBelowWaterPlane / (waterDepth * 8) + waterAlpha / 255, 0, 1)
blend the fragment toward waterTintRgb by factor
```

The `* 8` is applied at the call site, not in the decoder, so the effective default depth is 512
world units. The software renderer (`Renderable_Sub1.java:3525-3541`) drops `waterAlpha` entirely
and interpolates purely on vertex Y between the surface and the depth threshold. Preserve that
asymmetry if you port both renderers.

Entities inherit the triple from the tiles they occupy (`LoginRequest.java:133-153`), and a tile
with no water config inherits from a neighbour in its footprint. Port that fill-in loop or entities
standing half in water will not tint.

> **Open risk.** The physical unit of the depth is inferred, not proven. The GL path uses
> `1 / (depth * 8)` as a reciprocal scale on a plane distance; the software path compares
> `depth * 8` directly against vertex Y; but the hardcoded fallback at `Class60.java:188` passes 40
> raw with no `* 8`. Two entry points into the same field on different scales. Unresolved, and a
> port that unifies them naively could get the global underwater tint depth wrong by 8x.

## 4. Related definitions the renderer needs

| Type | Index | Group | Count | Decoder |
|---|---:|---:|---:|---|
| MapScene (bank / altar / staircase icons) | 2 | 34 | 100 | `Class9.method193` (`Class9.java:161-204`) |
| MapElement / world-map area | 2 | 36 | - | `Class341.java:141,185` |

MapScene opcodes: `1` = u16 sprite id into index 8, `2` = u24 rgb, `3` = flat flag, `4` = sprite
id -1.

Neither has any decoder in FlashEditor today.
