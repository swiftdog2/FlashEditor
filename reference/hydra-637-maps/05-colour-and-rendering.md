# Colour, blending and the two map renderers

What turns decoded tile data into pixels. This is the part that makes terrain look like RuneScape
rather than like flat swatches.

## 1. Packed HSL

The client's colour currency is a 16-bit packed HSL short:

```
hsl16 = (hue << 10) | (sat << 7) | light
        hue   6 bits
        sat   3 bits
        light 7 bits
```

Packing is not a plain bit shuffle - it desaturates as lightness rises first
(`Class79.method801`, `Class79.java:115-135`):

```csharp
public static int PackHsl(int hue, int sat, int light) {
    if      (light > 243) sat >>= 4;
    else if (light > 217) sat >>= 3;
    else if (light > 192) sat >>= 2;
    else if (light > 179) sat >>= 1;
    return ((hue >> 2 & 0x3F) << 10) | ((sat >> 5) << 7) | (light >> 1);
}
```

Converted to RGB through a **65536-entry palette** built once at startup
(`Class93_Sub1.method904`, `Class93_Sub1.java:137-215`; table lives at
`Class221.anIntArray1665`, built by `Class122.method2199`).

> The client **randomises the palette gamma per session**, and separately randomises the world-map
> hue and lightness and the minimap wall colours, and adds a random additive tint on every minimap
> regeneration. For an editor, pin the gamma at **0.7** and drop all the randomisation. You want a
> deterministic render.

## 2. RGB to HSL, two different variants

Neither is textbook HSL. Both use `/256.0` rather than `/255.0` normalisation, integer truncation,
and a lightness-dependent saturation right-shift ladder before packing. Transcribe the arithmetic,
do not substitute a standard library conversion.

| Variant | Used by | Produces |
|---|---|---|
| `Class38.method348` (`Class38.java:6-77`) | Overlays | A packed `hsl16` |
| `FloorUnderlay.method718` (`FloorUnderlay.java:112-134`) | Underlays | Four separate accumulator components |

The overlay path additionally maps the magic RGB `0xFF00FF` to `-1`, meaning "no colour, show the
underlay through" (`Class64_Sub24.method652`).

The underlay path deliberately does **not** produce a packed short, because its output has to be
area-averaged first. See `04-floor-definitions.md` section 1.

## 3. The underlay blend

`Class305.method3568` (`Class305.java:222-350`). This is the single most important routine for
visual fidelity, and it is why **a region cannot be coloured in isolation** - the window reaches
several tiles into the neighbouring map squares.

The algorithm is a sliding-window running-sum box blur over the underlay components, with hue
weighted and saturation and lightness unweighted:

```
for each output tile:
    sumWeightedHue += underlay.anInt538     // hue already multiplied by its chroma weight
    sumHueWeight   += underlay.anInt540
    sumSat         += underlay.anInt541
    sumLight       += underlay.anInt542
    count          += 1

    hue   = sumWeightedHue * 256 / sumHueWeight   // guard sumHueWeight == 0
    sat   = sumSat   / count                      // guard count == 0
    light = sumLight / count
    hsl16 = PackHsl(hue, sat, light)
```

Only tiles with a **non-zero underlay id** contribute. Both guards must be reproduced or the edges
of the world divide by zero.

Implemented as a running sum: as the window advances one column, add the entering column and
subtract the leaving one, rather than re-summing.

The hue weighting is the subtle part. Dividing hue by the **summed chroma weight** rather than by
the tile count stops grey and near-grey tiles from dragging the averaged hue toward zero. Getting
this wrong produces terrain that is recognisably the right shape but the wrong colour.

### The window is 10 wide and asymmetric - SETTLED

Both earlier passes were wrong, in different directions. Read directly from
`Class305.java:243-318` and confirmed independently:

Both loops run `for (i = -5; i < size; i++)`, and each step **adds** column `i + 5` (when in range)
and **subtracts** column `i - 5` (when in range) before writing output column `i`. By the time
output `x` is written, the resident set is:

```
x - 4  ..  x + 5        10 columns, not 11, and not centred
```

So the window reaches **4 tiles back and 5 forward**. The two are not interchangeable: a tile is
self-contained only from index 4 to index 59 of a 64-tile square, and swapping the reaches
misclassifies a one-tile band on the north and east edges. Clipped at the scene edge, never wrapped.

Consequence for the apron: a square needs at least 5 tiles of neighbour on the north and east and 4
on the south and west, so a one-square apron is ample.

### Consequence for the editor

Because the window reaches 5 tiles past each edge, **a map square cannot be coloured in isolation**.
Any scene must hold at least a one-square apron around whatever is displayed. A repository that
loads only the visible square will produce visibly wrong seams.

### Related rules in the same area

- **The hole rule** (`Class305.java:939-943`). An overlay with `primaryHsl == -1` **and**
  `secondaryHsl == -1` suppresses the tile entirely - nothing is drawn, and the underlay does
  **not** show through. This is what makes cave mouths and dungeon voids read correctly.
- **The default shape** (`Class305.java:936-937`). When `shape == 0` and there is no overlay, the
  client substitutes shape 12, the full underlay square.
- **Overlay colour resolution order** (`Node_Sub16.method1149`, `Node_Sub16.java:63-89`):
  1. `secondaryHsl` (op 7) if not -1
  2. otherwise the op-2/3 texture's **average colour**, if the texture permits it
  3. otherwise `primaryHsl` (op 1)

Two build paths consume the result:

| Path | When | Behaviour |
|---|---|---|
| `Class305.method3576` (`Class305.java:911-1112`) | graphics preference off | One flat blended HSL per tile, base shape tables only |
| `Class305.method3578` | graphics preference on | Per-corner HSL with bilinear interior interpolation, neighbour-overlay edge propagation, extended shape tables |

For a 2D editor view, the flat path is the right reference. It is simpler and produces a clean,
readable tile grid.

## 4. Tile shapes

Terrain geometry comes from **15 tile shapes** built from a 13-entry vertex coordinate table plus
three parallel triangle-index tables (`Class305.java:118` and `:103`):

```
SHAPE_VERTEX_X[13] = {0, 256, 512, 512, 512, 256, 0, 0, 128, 256, 128, 384, 256}
SHAPE_VERTEX_Y[13] = {0,   0,   0, 256, 512, 512, 512, 256, 256, 384, 128, 128, 256}
```

Coordinates are in 512-unit tile space, matching the 4x rescale. Rotation is a fixed 4-way
permutation of the vertex indices. A second, larger table set is used when a neighbouring overlay
has to be blended across the tile edge.

For a 2D top-down editor, these tables give you the **shape mask** to fill the overlay colour into:
shape 0 is the whole tile, the rest are the familiar half / corner / diagonal splits.

## 5. Lighting

Per-vertex central-difference normals. The GPU paths (`s_Sub1`, `s_Sub2`) build float normals
`(dx, -2S, dz)` normalised, dotted against a normalised light direction defaulting to
`(-200, -240, -200)`. The software path (`s_Sub3`) does the same in fixed point but with a
demonstrably wrong length constant.

A 2D editor does not need this. A flat unlit render is clearer for editing, and the client's own
lighting is not something you want baked into data you are about to write back.

## 6. The minimap is not what you want to copy

**CONFIRMED**: the minimap is **not** a per-tile colour lookup. It orthographically re-rasterises
the already-built 3D ground mesh at exactly 4 screen pixels per tile into a 512x512 ARGB sprite,
once per plane change, reusing the same per-tile triangle data and the same baked per-vertex HSL
lighting as the 3D scene. Walls and doors are then stamped on as 4px lines and 1px dots, and loc
mapscene sprites are blitted over the top.

Reproducing that in C# means building the 3D mesh first, which is a large detour for a 2D editor.
**Do not port it.** Build the 2D view directly from tile data as described in section 3.

What *is* worth taking from the minimap code:

- The 4 pixels per tile convention.
- The wall stamping in `Class277.java:143-203`. Note: **ground decoration draws only a mapscene
  sprite and nothing at all if it has none**. The 1x1 dots come from wall **shape 3** (corner
  posts), not from ground decoration - a common misreading.

## 7. The world map is a separate format entirely

**CONFIRMED**: the world map (`Class278`) never reads index 5. It has its own archive, **JS5 index
23** (`InterfaceSettings.java:179`), name-addressed, containing:

| Group | Content |
|---|---|
| `details` | World-map definitions |
| `<mapname>` | A single file named `area`, holding a compact per-tile underlay / overlay / loc stream |
| `<mapname>_staticelements` | Icon placements |

It colours each tile as a flat rect (blended underlay RGB) plus a shape-masked overlay rect, at
1.5 / 2 / 3 / 4 / 8 screen pixels per tile depending on the map's zoom preset. Underlay blending is
computed identically to the scene path (`Class278.method3310` mirrors `Class305.method3568`).

The prior spec has **no entry at all for index 23**.

This format is a much closer match to what a 2D editor wants to draw than the minimap is, and it is
worth reading `Class278.java:502-586` for that reason - but it is a *derived* artifact. Edits must
go to index 5, not here. If FlashEditor ever edits maps, index 23 becomes **stale data that also
needs regenerating**, which is worth knowing before a user files a bug about it.
