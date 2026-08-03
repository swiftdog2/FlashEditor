# Index 5 - MAPS

**Format:** fully-understood  
**Capability:** read-write-no-tests  
**Effort:** medium

## What it is

Index 5 is the world map. It is the only index in this cache with **no numeric addressing at all**: there is no region-to-group table, so every lookup hashes a group *name*. The client proves it - `Class61.java:48-57` calls `Class234.mapContainer.requestFile("m"+rx+"_"+ry)` and the same for `l`, `n`, `um`, `ul`, with **no underscore after the prefix** (`m50_50`, not `m_50_50`). The store is opened at `InterfaceSettings.java:163`, `openFileStore(-54, true, 1, 5)` - the `true` is the discard flag, so index 5 caches nothing client-side.

A **group** is one file family for one 64x64 map square. A **file** within it is always child 0 - every one of the 5203 groups holds exactly one file. A **record** depends on the family:

| Prefix | Groups | What one group is | Encrypted |
|---|---:|---|---|
| `m` | 1684 | 4 planes x 64 x 64 tiles, plane-major then X then Y, each tile an opcode run terminated by 0 (derive height) or 1 (explicit height byte); then a variable-length extras tail (environment / point lights / shadow map) | No |
| `l` | 1684 | delta-encoded object placements: extended-smart id delta, then per-object smart position deltas + one attribute byte (`shape = b>>2`, `orientation = b&3`) | **659 of 1684** |
| `um` | 900 | same terrain format, **one plane only**, no extras tail | No |
| `ul` | 900 | same loc format, underwater | No |
| `n` | 35 | NPC spawn table: repeating 4-byte records, `u16 packed` then `u16 npcId`, where `plane = p>>14`, `localX = (p>>7)&0x3f`, `localY = p&0x3f` | No |

Zero unmatched name hashes across the table, so no sixth family exists here (`RealCacheMapDecodeTests.cs:44-77` asserts `matched == table.GetArchiveEntries().Count`).

Terrain opcodes confirmed against `Class305.method3581` (`Class305.java:1907-1996`): op 0 -> derived height (`:1940-1945`, `-960` per plane drop, procedural on plane 0); op 1 -> explicit byte with **a stored 1 remapped to 0** (`:1960-1962`); 2..49 -> overlay id + shape `(op-2)/4` + rotation `(op-2)&3` (`:1985-1988`); 50..81 -> tile flags `op-49` (`:1992`); 82..255 -> underlay `op-81` (`:1995`). Loc format confirmed against the static reader `Class305_Sub1.method3591` (`:1503-1535`): plane is a **bare** `i_155_ >> 12` with no mask.

The `n` format is NOT dead as the reference doc implies - `Particle_Sub3_Sub2.method3005` (`:230-247`) does read it. It is unreachable only because both live region-load paths null the id array first (`Node_Sub36.java:141`, `Node_Sub41.java:49`).

## Current capability

Index 5 is the single most advanced index in the editor. Everything below is real, working code, not a stub.

**Name addressing** - `MapSquareNames.cs:16-40` builds all five prefixes; `MapSquareLoader.ResolveGroup` (`MapSquareLoader.cs:59-62`) resolves through the reference table's identifier map.

**Decoder** - `Region.LoadTerrain` (`Region.cs:140-167`) with the per-tile loop at `:251-299` and the extras-tail walker at `:185-233`; `Region.LoadLocations` (`Region.cs:311-356`). `MapSquareLoader.Load` (`:77-102`) and `LoadUnderwater` (`:114-130`).

**Encoder** - `RegionCodec.EncodeTerrain` (`RegionCodec.cs:25-44`) and `EncodeLocations` (`:126-165`). Both return the original bytes verbatim for an unedited square and only re-encode when dirty or `force: true`.

**Write path** - `MapSquareLoader.Save` (`:167-190`) stages both files inside one `cache.BeginBatch()` so the 114 KB index-5 reference table is encoded once. XTEA is handled end to end: the read path infers encryption by trying the key and falling back (`RSCache.DecodeContainer`, `:891-906`), the container enciphers the correct range `[5, 5+compressedSize+4)` = `[5, storedLength-2)` (`RSContainer.cs:121-127`), and the write path **refuses to guess** - `RSCache.ResolveWriteKey` (`:931-943`) throws rather than write an encrypted square back as plaintext.

**Byte-identity sweeps** - `RealCacheRegionCodecTests.EveryTerrainFileSurvivesAForcedReEncode` (`FlashEditor.Tests/Map/RealCacheRegionCodecTests.cs:69-109`) force-re-encodes **all 1684** `m` files and asserts `Assert.Equal(total, byteIdentical)` with `total == 1684`. `EveryLocationFileSurvivesAForcedReEncode` (`:115-155`) does the same for every readable `l` file, also asserting `total == byteIdentical`. These are true full-family sweeps - `EverySquare` (`:256-262`) walks all 256x256 coordinates and ignores `FULL=1`.

**Supporting sweeps** - exact-consumption decode of all 1684 `m` (`RealCacheMapDecodeTests.cs:83-115`, also pinning 1324 squares with an extras tail), all 1684 `l` (`:122-168`), all 900 `um` as single-plane with empty tail (`:209-238`); XTEA proven by `RealCacheXteaCoverageTests.EveryEncryptedLocationGroupWithAKeyDecrypts` (`:59+`), which excludes unkeyed squares rather than counting them.

**Persistence** - `MapSaveRoundTripTests.AnEditedSquareSurvivesASaveAndReload` (`:58-118`) edits, saves, commits and **reopens the store**, and asserts an untouched square writes nothing. `EditingLocationsDoesNotDisturbNeighbours` (`:122-156`).

**GUI** - a real Map tab. `Editor.cs:73` puts `RSConstants.MAPS_INDEX` in `editorTypes`; `Editor.cs:485-489` binds `MapEditorPanel`; `Editor.Designer.cs:1352-1360` is the tab, `:1576` the control. `MapEditorPanel.cs` gives paint underlay, paint overlay, cycle overlay shape, cycle overlay rotation, raise/lower height, toggle blocked flag and delete top location (`:49-71`, `BuildEdit` at `:148-197`), with undo/redo (`:96-102`) and a "Save cache" button that commits via `cache.WriteCache` (`:234-274`). Rendering is `MapRasteriser.cs`, `MapScene.cs`, `Hillshade.cs`, `WorldNavigatorControl.cs`, with real floor colours resolved from index 2.

**Where it stops.** `um` decodes but has no encode sweep and no save path. `ul` is decoded only as a side effect inside `LoadUnderwater` (`:122-127`), asserted nowhere, encoded nowhere. `n` has **nothing** but the name helper `MapSquareNames.NpcSpawns` (`:40`), which its own doc comment admits nothing calls. That is 1835 of 5203 groups with no encoder and no sweep.

## Gaps

- READ THIS FIRST - the grade is a bucket, not the whole truth. The `m` family (1684 groups) and the `l` family (1684 groups) ARE complete by the strict definition: decoder, encoder, full byte-identity sweep and GUI editing all exist and are cited above. The index falls short of 'complete' only because the other three families - `um` (900), `ul` (900) and `n` (35), 1835 groups in total - have no encoder and no sweep. Do not commission work on the m/l codec; it is done and proven.
- `n` (35 groups) has no decoder, no definition class, no encoder, no test, no GUI. The format is now known and is trivial: repeating 4-byte records, `u16 packed` then `u16 npcId`, `plane = p>>14`, `localX = (p>>7)&0x3f`, `localY = p&0x3f`, read until the buffer is exhausted (`Particle_Sub3_Sub2.java:230-247`). Needs an `NpcSpawn` record type plus `DecodeNpcSpawns`/`EncodeNpcSpawns` on `RegionCodec`.
- `ul` (900 groups) is decoded incidentally by `MapSquareLoader.LoadUnderwater` (`MapSquareLoader.cs:122-127`) and asserted by nothing. No test reads a single underwater location back. `06-port-plan.md:172` flags this as an unexercised residual risk.
- No byte-identity sweep for `um` or `ul`. `RealCacheRegionCodecTests.EverySquare` is only ever called with `"m"` and `"l"` (`:47`, `:123`). `EncodeTerrain` already honours `region.PlaneCount` so a `um` sweep is a handful of lines; `EncodeLocations` needs nothing at all for `ul`.
- `MapSquareLoader.Save` cannot save `um`/`ul` and will silently corrupt if handed one. It resolves `MapSquareNames.Terrain`/`Locations` unconditionally (`:174`, `:182`), so saving a region returned by `LoadUnderwater` writes single-plane underwater terrain over the four-plane surface `m` group. Save needs to know which family the region came from.
- No GUI surface for `um`, `ul` or `n`. `MapTool` (`MapEditorPanel.cs:49-59`) has no underwater layer and no NPC-spawn tool.
- `AddLocationEdit` and `ReplaceLocationEdit` exist (`MapEdits.cs:152`, `:205`) but are wired to no tool - `BuildEdit` (`MapEditorPanel.cs:148-197`) only ever emits `RemoveLocationEdit`. You can delete objects in the GUI, not place or edit them.
- `RealCacheRegionCodecTests.UntouchedSquaresAreWrittenBackVerbatim` breaks at 200 squares (`:57-58`). Low value - it only exercises the clone path - but it is the one map assertion that samples rather than sweeps.

## Notes and traps

TRAPS, in the order they will bite you:

1. **Non-canonical encoding, four separate cases, all already solved - do not "simplify" any of them.** (a) Opcode *order* within a tile is free, but the shipped files use overlay -> flags -> underlay -> terminator; underlay-first reproduced only 91 of 1684 files. Pinned at `RegionCodec.cs:49-74`. (b) Stored height bytes `0` and `1` both decode to 0 (`Class305.java:1960-1962`), so the byte is kept verbatim in `Region.rawHeightByte` and cannot be recomputed. `RegionCodec.EncodeHeight:110` therefore rejects a one-step edited height as unencodable. (c) "Absent versus default": some tiles store a height equal to what the procedural fallback would produce, so `heightExplicit` is recorded at decode (`Region.cs:60`) rather than inferred. (d) **`List.Sort` is unstable and will silently reorder** - `l50_50` places object 85 at position 3969 twice with different attributes; `RegionCodec.cs:150-151` uses `OrderBy` for exactly this reason.

2. **Two client bugs you must NOT copy.** The XTEA end offset is `buffer.length` (`JS5Archive.java:348`), which includes the 2-byte version trailer and over-enciphers one block on 22.7% of encrypted groups; use `[5, storedLength-2)`, which `RSContainer.cs:121-127` already does. And the keys are wired to the wrong family - `Class181.java:44` passes `null` to `getDecryptedFile` for the `l` file, which is the only encrypted one, while `:76-77` passes the real keys to `n`, which is never encrypted. All 659 encrypted squares therefore render objectless in the running client. FlashEditor deliberately diverges.

3. **The encrypted-group count is a property of THIS cache, not of the format.** 659 of 1684 `l` groups here, 61 with no published key; the same sweep over the OpenRS2 b639 archive finds 1649 encrypted and 62 unkeyed, because that copy stores as ciphertext what this one stores plain (`MapSquareLoader.cs:22-33`). Never hardcode 659.

4. **A missing key must yield an empty loc list, never an exception** - that is the client's observable behaviour. But a square that decoded to an empty list must never be written back, or it erases every object. `MapSquareLoader.Save:183` guards on `region.RawLocations.Length > 0`. Keep that guard.

5. **Encryption detection.** `RSCache.DecodeContainer` (`:891-906`) tries the key and falls back to plaintext when it fails, rather than the gzip-magic check at `stored[9..12] == 1F 8B 08` that `01-cache-access.md:151-155` recommends. It works today because the container length check (`RSContainer.cs:155`) catches the garbage, but the "20 encrypted groups inflate over their own ciphertext" claim is listed in CLAUDE.md as **not yet verified in this repo**. Verify before relying on either behaviour.

6. **`Region` collides with `System.Drawing.Region`** through the implicit usings in any file touching WinForms. Alias it: `using MapRegion = FlashEditor.Cache.Region.Region;`. This has broken the build three times.

7. **Cross-index dependencies for rendering, not for the codec.** Colours come from index 2: `FLOOR_UNDERLAY_GROUP = 1`, `FLOOR_OVERLAY_GROUP = 4`, `MAP_SCENE_GROUP = 34` (`RSConstants.cs:60-62`, resolved at `RSCache.cs:710-727`). Object footprints and map-scene icons come from index 16 object definitions. Index 2 has **no name hashes**, so those are addressable only by numeric group id.

8. **No 637-to-639 format drift found on this index.** Every opcode our decoder handles has a client handler, and no unhandled opcode occurs in the data - the exact-consumption sweeps over all 1684 `m` and all 900 `um` files would have caught either.

9. **Overlay and underlay ids are unsigned.** The client stores them into `byte[][][]` via `readSignedByte` (`Class305.java:1985`) but every read site masks `& 0xff` (`:249`, `:265`, `:922-923`). Our `int` + `ReadUnsignedByte` is correct; do not "fix" it to signed.

10. **`FULL=1` does not widen these tests.** `EverySquare` in both map suites enumerates all 256x256 coordinates directly (`RealCacheMapDecodeTests.cs:306`, `RealCacheRegionCodecTests.cs:256`), so a sampled run already covers every map square. And per CLAUDE.md, never run two cache-backed suites concurrently.
