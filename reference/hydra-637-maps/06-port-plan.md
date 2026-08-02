# Porting this into FlashEditor

What the existing C# gets wrong, what is missing entirely, and a phased plan.

## 1. The existing decoder

`FlashEditor/Cache/Region/` already contains `Region.cs`, `Location.cs`, `Position.cs` and
`HeightCalc.cs`. Its **opcode structure is correct** - the `0 / 1 / 2-49 / 50-81 / 82-255` split
matches the client exactly. The arithmetic around it is not.

### Blockers

| File:line | Defect | Fix |
|---|---|---|
| `Region.cs:57` | Op-0 plane-0 height is `Calculate(...) << 3` - positive and 4x too small. Client (`Class305.java:1943-1945`) negates and scales by 32. | `-HeightCalc.Calculate(baseX, baseY, x, y) * 32` |
| `Region.cs:59` | Inter-plane drop is 240. Client (`Class305.java:1940-1941`) uses 960. | `tileHeights[z-1,x,y] - 960` |
| `Region.cs:68` | Op-1 plane-0 height is `-height << 3`, i.e. `h*8`. Client (`Class305.java:1970`) is `h*32`. | `-h * 32` |
| `Region.cs:72` | Same 4x shortfall on planes 1-3. | `tileHeights[z-1,x,y] - (h * 32)` |
| `Region.cs:91` | `LoadTerrain` returns after the grid and discards the extras tail. **MEASURED**: 1324 of 1684 `m` files carry one. | Parse the tail per `02-terrain-m.md` section 5, then assert `Remaining == 0` |
| `Region.cs:103` | Loc id delta uses a plain `ReadUnsignedSmart()`. The stream uses an extended smart. **MEASURED**: corrupts 281 groups. | Add `ReadExtendedUnsignedSmart()` and use it here **only** - line 109 is already correct |
| `HeightCalc.cs:38` | `Precalculate()` has **no caller anywhere in the solution**, so `SIN`/`COS` are all zero and `Interpolate` returns a constant 32768. The procedural terrain is a fixed 50/50 blend today. | Make it a static constructor |
| `HeightCalc.cs:57` | `(baseX >> 3) + 932638 + x`. The client passes absolute tile coords with no shift and uses `+932731` / `+556238`. The `>> 3` collapses eight regions onto one. | `baseX + x + 932731`, `baseY + y + 556238` |
| `HeightCalc.cs:23` | `COS` is 2048 entries at amplitude 65536 indexed `COS[1024*x/y]`. Client is 16384 entries at amplitude 16384 indexed `COS[8192*x/y]`. Blend weight must land in `[24576, 40960]`; as written it spans `[0, 65536]`. | Rebuild the table, or keep 2048@65536 and change `Interpolate` to `f = (65536 - COS[1024*x/y] / 4) >> 1` |
| `Position.cs:33` | `this.size = mapSize` assigns the wrong field. `mapSize` is never assigned and stays 0; `size` is never read. Every method consulting `mapSize` is therefore wrong. | Assign `this.mapSize = mapSize` and delete the dead `size` field |
| `Cache/Util/NameHasher.cs:14` | `Encoding.GetEncoding(1252)` in a static initialiser. On .NET 9 code page 1252 needs `System.Text.Encoding.CodePages`, which is not referenced, and the provider is never registered. | Map names are ASCII - use `(sbyte)c` directly and drop the dependency |

### Major

| File:line | Defect |
|---|---|
| `Region.cs:20` | `tileHeights` is `[4,104,104]`. Heights are a **vertex** grid: for one region, `[4,65,65]`. 104 is a scene width, not a region width. |
| `Region.cs:21-25` | The five byte grids are also `[4,104,104]`; they should be `[4,64,64]`. The oversize silently returns 0 for indices 64-103 instead of throwing. |
| `Region.cs:41` | `LoadTerrain` hardcodes 4 planes. **MEASURED**: all 900 `um` groups need exactly 1 plane and every one fails with 2 or more. Make the plane count a parameter. |
| `Region.cs:99` | `LoadLocations` appends without clearing, and `LoadTerrain` does not reset the grids. A second load duplicates every loc. The client zeroes the flag byte per tile (`Class305.java:1931`). |
| `Region.cs:116` | `buf.ReadByte() & 0xFF` - `JagStream.ReadByte` returns -1 at EOF, and `-1 & 0xFF` is 255, so a truncated stream yields `shape=63` and keeps going. Use the throwing reader, as `LoadTerrain:48` already does. |
| `Region.cs:120` | Locs are stored as absolute `Position`, discarding local x/y and plane. Combined with `Location` having no setters (`Location.cs:14-24`), **a decoded region cannot be edited or re-encoded at all**. |
| `IO/JagStream.cs:579` | `WriteUnsignedSmart` rejects values above 32767, so the write path cannot emit the extended form that 323 shipped groups use. There is no reader counterpart either. |
| `Cache/RSCache.cs:244` | `SetVersion(1337)` hardcodes the reference-table version of every archive it touches, destroying the JS5 update signal. |
| `Cache/RSCache.cs:275` | The whole reference table is re-encoded and rewritten on every `WriteFile`. Index 5's table is 114,474 bytes. A 40-square edit rewrites it 40 times. |

### Minor

- `Region.cs:114` masks the plane with `& 0x3`. **MEASURED** max position is 16383, so the mask
  never fires on good data - it can only hide a desync. Drop it, add a tripwire.
- `Region.cs:161` `IsLinkedBelow` samples bit `0x2` at the caller's plane; the client samples it at
  a fixed plane index and only shifts when `plane > 0`.
- `Definitions/ObjectDefinition.cs:288,304,307` - `mapSceneId`, `mapAreaId` and `mapIconId` have no
  public accessor, so a map renderer cannot reach the icons.
- `Cache/RSIdentifiers.cs:20` - `getFile`'s probe loop spins forever if the table ever fills.

### One thing the existing code already gets right

`Definitions/ObjectDefinition.cs:450-451` assigns opcode 14 to `sizeX` and 15 to `sizeY`. That is
the **correct axis naming** - the opposite of the client's `Class352` field spelling, and therefore
right. Do **not** "fix" it to match the client. See `03-locs-l.md` section 5.

## 2. Missing entirely

Nothing in FlashEditor decodes any of these today:

| Piece | Where it lives | Notes |
|---|---|---|
| `FloorUnderlayDefinition` | index 2, group 1, 159 files | Needed for any tile colour at all |
| `FloorOverlayDefinition` | index 2, group 4, 235 files | Plus the unconditional `method2691` priority composite |
| `MapSceneDefinition` | index 2, group 34, 100 files | Bank / altar / staircase icons |
| HSL16 to RGB palette | `Class93_Sub1.method904` | 65536 entries, gamma pinned at 0.7 |
| RGB24 to HSL16 | `Class38.method348` and `FloorUnderlay.method718` | Two distinct variants, neither textbook |
| The underlay blender | `Class305.java:222-350` | The reason terrain looks right |
| Region lookup by name hash | - | `NameHasher` and `RSIdentifiers.getFile` exist but are not wired together |
| XTEA on the map read path | - | `Cache/Util/Crypto/XTEA.cs` and `XTEAKeyTable.cs` exist and are wired to nothing that reads a region |

On XTEA specifically: 659 of 1684 `l` groups are ciphertext, and **61 of those have no key in
`FlashEditor/xteas/xteas.json`**. Missing keys must yield an empty loc list, not an exception.
Detect encryption on the gzip magic at `stored[9..12] == 1F 8B 08`, never on "does it inflate" -
20 encrypted groups inflate successfully over their own ciphertext and produce 6 bytes of garbage.

## 3. Things to deliberately not port

| Thing | Why |
|---|---|
| The `n`-file gate (`Class61.java:59-64`) | Real, but only on a dead client-side rebuild path. Porting it blanks 88% of the world. |
| `MapIndex.java` / `map_index.dat` | Dead code, and wrong for this cache: 496 of 1673 regions mismatch. |
| The minimap rasteriser | It re-rasterises the 3D mesh. A 2D editor should draw from tile data directly. |
| The client's XTEA end offset | It over-enciphers one block. Use `[5, storedLength - 2)`. |
| Per-session randomisation | Gamma, world-map hue, minimap tint. Determinism matters more for an editor. |
| Water fields as colour | Ops 13/14/16 feed the underwater shader, not any map view. Decode and expose; do not colour with them. |
| Terrain geometry / normals / lighting | A 2D view needs a polygon per (shape, rotation) - a small hand-authored table, not a mesh. |

## 4. Phased plan

Each phase is independently shippable and has a test that proves it.

**Phase 1 - read a region. DONE.** Name-hash lookup wired to index 5
(`RSReferenceTable.GetArchiveId`, `MapSquareNames`, `MapSquareLoader`), XTEA on the map read path,
`m` and `l` decoded with the corrected arithmetic, extras tail parsed and validated.
`JagStream.ReadExtendedUnsignedSmart` added. `HeightCalc`, `Position` and `NameHasher` blockers
fixed.

*Test:* `FlashEditor.Tests/Cache/RealCacheMapDecodeTests.cs`. Measured on the shipped cache:

| Assertion | Result |
|---|---|
| Index-5 names decompose into the five families with nothing left over | 1684 / 1684 / 900 / 900 / 35 |
| Every `m` file decodes and its extras tail walks to the last byte | 1684, 0 failures |
| Squares carrying an extras tail | 1324, matching the independent measurement |
| Every `um` file decodes as a single plane with no tail | 900, 0 failures |
| Every `l` file decodes or reports a missing key | 1623 decoded, 61 missing key |
| Squares needing the extended smart | 63 |
| Procedural heights vary and stay in 10..60 | passes |

Sanity check on decoded values: Lumbridge `m50_50` resolves to base (3200, 3200), every plane-0
height is a multiple of 32, the plane-0 to plane-1 delta is exactly -960, and the shape histogram
over 3,544,326 decoded locations is dominated by shape 10 (game object, 1.80M) and shape 22 (ground
decoration, 1.04M), which is the expected distribution.

**Phase 2 - floor definitions.** Decode index 2 groups 1 and 4.
*Test:* all 159 underlays and 235 overlays consume exactly, with no unknown opcodes.

**Phase 3 - colour.** The HSL palette, both RGB-to-HSL variants, the underlay blender.
*Test:* golden-image comparison of one known square against a reference render, plus a unit test
that `PackHsl` reproduces the desaturation ladder at each of its four thresholds.

**Phase 4 - the 2D view.** A `MapRasteriser` over `DirectBitmap` (`Cache/Util/DirectBitmap.cs:34-57`
is already `Format32bppPArgb` over a pinned `int[]` and has tests). Layer flags for underlay,
overlay, walls, ground decoration, game objects, mapscenes, tile flags, grid. Pass order: underlay
fill, overlay shape mask, tile-flag wash, locs by group, icons, grid.
*Test:* render a square with a known landmark and eyeball it, then pin it as a golden image.

**Phase 5 - editing.** Mutable tile and loc models, a command/undo stack, hit-testing from screen
pixel to `(regionX, regionY, localX, localY, plane)`.
*Test:* apply an edit, undo it, assert the model is byte-identical to the load.

**Phase 6 - the write path.** This is the dangerous one, and `STATE_OF_THE_EDITOR.md` already rates
the existing cache write path unsafe and mostly wrong. Do not build on it without reading that
assessment first.
*Test:* **round-trip every region unedited and require byte-identical output** before allowing a
single edited write. A region that does not round-trip cannot be safely saved.

### Write-path hazards

- The loc stream must re-emit the **extended** smart where the original used it, or 323 groups
  change length. `JagStream.WriteUnsignedSmart` currently cannot.
- An encrypted region must be written back **encrypted**, with the same key. Writing plaintext
  destroys it silently, because a format-6 table has no flag recording the change and the client
  will decipher it regardless.
- The archive CRC covers the **stored** bytes, so for an encrypted group it covers ciphertext.
  Computing it means encoding the container with its key.
- The extras tail must be preserved. It cannot be re-derived from anything the editor models.
- Do not stamp a fixed reference-table version, and do not rewrite the whole table per file.

## 5. Residual risks

Carried forward from the adjudication, so they are not rediscovered later:

1. **131 encrypted `l` groups have no key anywhere.** Their encryption is inferred from header
   structure plus the `server/mapdata` correlation, not proven by a decrypt. They will render
   objectless whatever you do.
2. **699 of 1684 `l` groups were never exercised** by any measurement - the encrypted ones absent
   from both `server/mapdata` and the no-key subset. Nothing suggests they differ.
3. **Extras opcode 128 and shadow kinds 2 and -1 have zero occurrences** in this cache. Their
   lengths come from source only.
4. **The water depth unit is unresolved** - two entry points into the same field disagree by 8x.
5. **The underlay blend window is 10 or 11 wide.** Pin it before implementing.
6. **`um`/`ul` were not exercised** for the extras tail or for loc decoding beyond the smart-reader
   measurement.
7. The 637-client / 639-cache split means a client-derived claim can be right about *meaning* and
   still wrong about this cache's *layout*. Where they could disagree, the cache wins on layout.
