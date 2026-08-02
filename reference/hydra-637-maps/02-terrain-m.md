# The terrain file: `m<x>_<y>`

Unencrypted, header-less, purely sequential. A tile-opcode grid followed by a variable-length
extras tail in the same buffer.

Decoder: `Class305.method3574` (`Class305.java:759-767`) for the grid, `Class305_Sub1.method3582`
(`Class305_Sub1.java:100`) for the tail, both fed the same `RSBuffer` from `Class42.java:30-34`.

**MEASURED**: all 1684 `m` groups in the shipped cache decode with this spec to **exact** buffer
consumption, zero failures. Re-run independently against the plaintext copies in
`server/mapdata`: 1684 of 1684, also exact.

## 1. Iteration order

**CONFIRMED**, `Class305.java:759-767`:

```java
for (int plane = 0; plane < planeCount; plane++)
  for (int x = 0; x < 64; x++)
    for (int y = 0; y < 64; y++)
      decodeTile(...);
```

Plane-major, then X, then Y. Getting this wrong transposes the world.

- `planeCount` is **4** for the game scene (`Class181.java:216`).
- `um` files are **single plane**. **MEASURED**: all 900 `um` groups consume exactly with 1 plane
  and every one fails with 2 or more. They also carry no extras tail.
- The tile grid is always 64x64 per region.

## 2. The per-tile opcode loop

Read unsigned bytes until an opcode terminates the tile.

| Opcode | Payload | Effect | Terminates tile |
|---:|---|---|---|
| 0 | none | Height from the procedural generator | **yes** |
| 1 | 1 x u8 height | Explicit height | **yes** |
| 2..49 | 1 x u8 overlay id | `shape = (op - 2) / 4`, `rotation = (op - 2) & 3` | no |
| 50..81 | none | `tileFlags = op - 49` | no |
| 82..255 | none | `underlayId = op - 81` | no |

**CONFIRMED**. Overlay and underlay ids are **single unsigned bytes**, never shorts, never smarts.
Sentinel is 0 meaning "none"; `id - 1` indexes the config. Do not introduce -1.

> The rotation addend. `Class305.method3581` adds a rotation term to the overlay rotation, but on
> the **static** region-load path that term is a literal 0. It is non-zero only on the dynamic /
> instanced-region path, where a genuine chunk rotation applies. A port that adds it
> unconditionally rotates every overlay in the world. Keep `(op - 2) & 3` for static loads.

## 3. Height arithmetic

**CONFIRMED**, `Class305.java:1937-1971`, normalised:

```java
// opcode 0
if (plane != 0) heights[plane][x][y] = heights[plane - 1][x][y] - 960;
else            heights[0][x][y]     = -(noise(absY + 556238, absX + 932731)) * 32;

// opcode 1
int h = readUnsignedByte();
if (h == 1) h = 0;                       // sentinel
if (plane != 0) heights[plane][x][y] = heights[plane - 1][x][y] - (h * 32);
else            heights[0][x][y]     = -h * 32;
```

**This client is a 4x rescale of RS2.** Tile size is **512**, not 128 (`Class305.java:1036,1382,1895`).
Every vertical quantity follows:

| Quantity | This client | RS2 / 317 |
|---|---:|---:|
| World units per height byte | **32** | 8 |
| Inter-plane drop | **960** | 240 |
| Tile size | **512** | 128 |

Heights are negative because Y-up is negative in this coordinate system.

The `h == 1 -> 0` sentinel is real and hot: **MEASURED** 1,665,326 occurrences out of 10,768,926
opcode-1 tiles (15.5%).

**The height array is a vertex grid, one larger on each axis.** `Class305.java:127` allocates
`[planes][sceneWidth + 1][sceneHeight + 1]`. For a single region that is `[4][65][65]`. The five
byte grids (underlay, overlay id, overlay shape, overlay rotation, tile flags) are true tile grids
at `[4][64][64]` (`Class305.java:128-132`).

> Minimap-only footnote, ignore for a region decoder: when the scene is the 1-plane minimap scene
> (`aBoolean2544 == true`), opcode 0 writes `0` and opcode 1 writes `+h * 32` with no sentinel
> (`Class305.java:1948,1973`).

## 4. The procedural height fallback

Used by opcode 0 on plane 0. Generator is `Projectile.method3082` (`Projectile.java:46-77`),
normalised:

```
n =  (inoise(X + 45365, Y + 91923, freq 4) - 128)
  + ((inoise(X + 10294, Y + 37821, freq 2) - 128) >> 1)
  + ((inoise(X,         Y,         freq 1) - 128) >> 2);

n = (int)(n * 0.3) + 35;
clamp n to [10, 60];
```

Three octaves, frequencies 4 / 2 / 1, amplitudes 1 / 1/2 / 1/4, clamp `[10, 60]`.

`X` and `Y` are **absolute world tile coordinates** with no shift: `Class42.java:22-23` supplies
`64 * regionX` and `64 * regionY`, and `Class305.java:1944` adds the `+932731` / `+556238` offsets.

The two inner primitives:

```
// Class242.method2934 - integer hash noise
n = x + 57 * y;
n ^= n << 13;
return ((n * (n * n * 15731 + 789221) + 1376312589) & 0x7fffffff) >> 19 & 255;

// Class156_Sub1.method2499 - smoothing
smoothed = centre / 4 + sides / 8 + corners / 16;
```

### The interpolation table

The client's cosine table (`Class284_Sub2_Sub2.java:16-22`) has **16384 entries at amplitude
16384**, indexed `COS[8192 * frac / freq]` (`ClientScript.java:142`), with the result taken `>> 16`.

That combination yields a blend weight in **[24576, 40960]**, i.e. 0.375 to 0.625, not the
[0, 1] of a true cosine interpolation. **This is genuinely what the client does** - the noise
interpolation is damped. It is unambiguous in the source, but flagged here because it looks like a
bug and a porter will be tempted to "fix" it. The other 20-odd users of that table all take `>> 14`,
consistent with amplitude 16384; only the noise interpolator uses `>> 16`.

## 5. The extras tail

**CONFIRMED and MEASURED**: 1324 of 1684 `m` files carry a tail; 360 end exactly at the grid.
`um` files never do.

The client loops `while (caret < buffer.length)` and throws `IllegalStateException` on any
unrecognised opcode (`Class305_Sub1.java:277`). That strictness is useful: parsing the tail and
asserting the buffer is fully consumed catches every arithmetic slip in the grid decoder
immediately.

| Opcode | Payload |
|---:|---|
| 0 | Environment. `u8 mask`, then `+4` if `0x01`, `+2` if `0x02`, `+2` if `0x04`, `+2` if `0x08`, `+6` if `0x10`, `+4` if `0x20`, `+2` if `0x40`, `+12` if `0x80` (`Class28.java:59-137`) |
| 1 | Point lights. `u8 count`, then per light: `u8 flags/plane`, `3 x u16` (x, z, y), `u8 n`, `(2n+1) x u16`, `u16 colour`, `u8 type`; **plus one extra `u16` if `(type & 0x1f) == 31`** (`Class1.java:126-181`, `Class305_Sub1.java:293-297`) |
| 2 | `3 x u8` (`Class28.java:139-152`) |
| 128 | Exactly 10 bytes: `u16, i16, i16, i16, u16` (`Class305_Sub1.java:280-287`) |
| 129 | Shadow map. Four planes, each a **signed** byte `kind`; `kind == 1` is followed by 256 signed bytes (16x16, each covering a 4x4 tile block); kinds 0, 2 and -1 are followed by nothing |
| other | throws |

**MEASURED** occurrence in the shipped cache:

```
extras opcodes : {0: 1269, 1: 933, 2: 51, 129: 20}
shadow kinds   : {0: 60, 1: 20}
light type&0x1f: {..., 16: 18690, 31: 376}
```

Opcode 128 is **UNTESTED** - zero occurrences here, so its 10-byte length comes from source only.
Shadow kinds 2 and -1 are likewise **UNTESTED**. The `type & 0x1f == 31` conditional fires 376
times, so it is required for correct skipping.

## 6. Tile flags

Set by opcodes 50..81 as `flag = op - 49`. **CONFIRMED** bit meanings, each traced to its consumer:

| Bit | Meaning |
|---:|---|
| `0x1` | Tile is fully blocked / clipped |
| `0x2` | Bridge bit. Read only from plane 1 |
| `0x4` | Roofed interior, participates in the roof-removal flood fill |
| `0x8` | Force render at plane 0 |
| `0x10` | Hide locs on this plane |

The bridge bit does three separate things (`Class206.method2723`, `Node_Sub31_Sub4.method1390`):

1. Shifts the collision plane down by one when marking blocked tiles and when adding locs.
2. Physically slides the whole tile column down one plane after the scene is built, stashing the
   displaced plane-0 tile.
3. Makes every height query read `plane + 1`.

> The existing C# `Region.IsLinkedBelow` (`Region.cs:161`) samples `0x2` at the caller's plane. The
> client samples it at a **fixed** plane index in every call site except one, and only applies the
> shift when `plane > 0`. `IsVisibleBelow` picks the right bit but has the same plane problem.

## 7. C# skeleton

```csharp
// Region is 64x64. tileHeights is a 65x65 VERTEX grid.
for (int z = 0; z < planeCount; z++)
for (int x = 0; x < 64; x++)
for (int y = 0; y < 64; y++) {
    while (true) {
        int op = buf.ReadUnsignedByte();          // must throw at EOF, not return -1
        if (op == 0) {
            tileHeights[z, x, y] = z == 0
                ? -HeightCalc.Calculate(baseX, baseY, x, y) * 32
                : tileHeights[z - 1, x, y] - 960;
            break;
        }
        if (op == 1) {
            int h = buf.ReadUnsignedByte();
            if (h == 1) h = 0;
            tileHeights[z, x, y] = z == 0
                ? -h * 32
                : tileHeights[z - 1, x, y] - (h * 32);
            break;
        }
        if (op <= 49) {
            overlayIds[z, x, y]       = buf.ReadUnsignedByte();   // int, not byte
            overlayShapes[z, x, y]    = (byte)((op - 2) / 4);
            overlayRotations[z, x, y] = (byte)((op - 2) & 3);     // no rotation addend on the static path
        } else if (op <= 81) {
            tileFlags[z, x, y] = (byte)(op - 49);
        } else {
            underlayIds[z, x, y] = op - 81;                       // int, not byte
        }
    }
}

ParseExtrasTail(buf);
Debug.Assert(buf.Remaining == 0);   // holds on 1684/1684 real files
```
