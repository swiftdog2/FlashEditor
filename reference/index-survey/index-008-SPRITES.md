# Index 8 - SPRITES

**Format:** fully-understood  
**Capability:** read-only  
**Effort:** medium

## What it is

Index 8 holds indexed-colour sprite *sets*. Structure: one group = one file (id 0) = one sprite set of N frames. Confirmed both ways - `AGENTS.md:282` measures 4593 groups / 4593 files, and the client's own accessor `JS5Archive.method2733` (`JS5Archive.java:591-612`) falls back to `getChildFromFolder(i, 0)` only when `anIntArray2671[i] == 1`, i.e. exactly one child per group, otherwise it throws.

The client opens it at `InterfaceSettings.java:157`: `Class332_Sub2.aJS5Archive_5423 = Class42_Sub3.openFileStore(-119, false, 1, 8)` - index 8, fileType 1, not the XTEA-flagged form used for 5/6/23/26/28/30/31. The decoder is `Class324.method3690` (`Class324.java:43-133`). Layout, read from the END of the file backwards:
- `[len-2]` u16 frameCount N
- `[len-7-8N]` u16 canvasWidth, u16 canvasHeight, u8 (paletteSize-1)
- then 4 arrays of N u16: offsetX (`anInt2725`), offsetY (`anInt2721`), subWidth (`anInt2722`), subHeight (`anInt2720`). Right/bottom padding (`anInt2719`/`anInt2724`) are DERIVED at `:70-71`, not stored.
- `[len-7-8N-3*(paletteSize-1)]` palette, (paletteSize-1) x 24-bit RGB, entry 0 reserved as transparent and never stored.
- `[0..]` pixel planes, one per frame: a flags byte, then subWidth*subHeight palette-index bytes, then (if flagged) subWidth*subHeight alpha bytes.
Flags: `0x01` = column-major/"vertical" (`Class324.java:91,96-100`), `0x02` = alpha plane present (`:90`).

Groups on index 8 carry NAME HASHES (identifiers flag `0x01` set - `AGENTS.md:86`), and the client uses them: `Class1.method165` (`Class1.java:4-27`) resolves 16 sprite groups by name - "hitmarks", "headicons_pk", "mapflag", "compass", "scrollbar", "cross", "mapdots" etc - via `JS5Archive.requestFile` (`JS5Archive.java:~620`), which lowercases and Java-hashes the string. `ImageArchive` is NOT a second on-disk format; it is the runtime GPU-uploaded form built from a `Class324` by `RenderType.method1758` (`Class141.java:38`).

## Current capability

READ ONLY, and untested.

Decoder: `FlashEditor\Definitions\Sprites\SpriteDefinition.cs:63-179` (`Decode`). I checked it field by field against `Class324.method3690` and it is faithful: same backwards seeks (`:66` vs client `:46`, `:71` vs `:52`, `:92` vs `:73`), same 4-array order offsetX/offsetY/subWidth/subHeight (`:86-89` vs `:57-67`), same palette 0->1 coercion (`:95-96` vs `:77-79`), same flag bits (`:15-18` vs `:90-91`), same two-plane alpha layout (indices fully, then alpha fully - `:122-130` then `:153-167`). `JagStream.ReadByte` (`IO/JagStream.cs:271`) returns 0-255, so the paletteSize byte and index bytes are unsigned as the client requires.

Entry point: `RSCache.GetSprite(int containerId)` at `Cache\RSCache.cs:688-699`. Note it hands the WHOLE group container payload to `Decode` and never goes through `RSArchive`'s file split - correct only because every index-8 group holds exactly one file.

GUI: a real Sprites tab exists. `Editor.Designer.cs:702-712` (`SpriteEditorTab`, text "Sprites"), wired as tab position 2 via `Editor.cs:67`. Loader is the background worker at `Editor.cs:624-688`, which sweeps every reference-table entry and populates a `TreeListView` with sprite sets expandable to frames (`:673-683`).

Write path: NONE. `SpriteDefinition.Encode()` is `throw new NotImplementedException()` at `SpriteDefinition.cs:249-251`. `ExportSpriteDatBtn_Click` (`Editor.cs:908-909`) is the literal comment "//Nothing yet bro". `ImportSpriteBtn` is declared (`Editor.Designer.cs:93,798-805`) with NO Click handler assigned at all - a dead button on the form.

Export: PNG only, and only frame 0 - `ExportSpriteBmpBtn_Click` (`Editor.cs:889-896`) saves `sprite.thumb`, which `SpriteDefinition.cs:173-174` sets to frame 0 exclusively. Multi-frame sets lose every frame but the first.

Tests: ZERO. I grepped `FlashEditor.Tests` for `GetSprite`, `SPRITES_INDEX`, `SpriteDefinition` and `prite` - every hit is either the `FlashEditor.Definitions.Sprites` namespace used for TEXTURE graph tests, `NPCDefinition.hitbarSprite`, or `MapSceneIconDefinition.SpriteGroupId`. No test decodes a single real index-8 group. Worse, both production callers swallow failure: `MapRasteriser.cs:908-911` wraps `GetSprite` in a bare `catch (Exception)` and `TextureGraphEvaluator.cs:448` does the same, so a totally broken sprite decoder would not fail any existing map or texture sweep either.

## Gaps

- Decode is LOSSY - it rasterises straight to a Bitmap and throws away everything needed to re-encode. `SpriteDefinition.Decode` keeps only canvas width/height, frame count and `RSBufferedImage` frames (`SpriteDefinition.cs:80-83,176`); `RSBufferedImage` (`Cache/Util/RSBufferedImage.cs`) stores only a `DirectBitmap`. The palette, the per-frame flags byte, offsetX/offsetY, subWidth/subHeight and the alpha-plane-present decision are all discarded. Fixing this is the prerequisite for everything else: add per-frame `Flags`, `OffsetX`, `OffsetY`, `SubWidth`, `SubHeight` and a set-level `int[] Palette` recorded verbatim at decode.
- `SpriteDefinition.Encode()` (`SpriteDefinition.cs:249-251`) must be implemented: write pixel planes forward from 0 in stored flag order, then palette (paletteSize-1) x 3 bytes, then canvas w/h and the (paletteSize-1) byte, then the four u16 arrays, then the u16 frame count. It must emit the RECORDED palette and flags, not ones recomputed from the bitmaps.
- A codec test against captured bytes - decode a handful of real index-8 groups covering all four flag combinations (plain, vertical, alpha, vertical+alpha) and one multi-frame set, and assert exact re-encode. Not a synthetic round trip: CLAUDE.md is explicit that round-tripping this encoder against this decoder proves nothing.
- A full byte-identity sweep over all 4593 groups, in the shape of `RealCacheMapIconTests.EveryMapSceneIconDecodesAndRoundTrips` (`FlashEditor.Tests/Map/RealCacheMapIconTests.cs:29-65`): read raw bytes, decode, assert exact re-encode, and assert exact consumption. No `or` in the assertion and no tolerated failure count.
- GUI write: wire `ImportSpriteBtn.Click` (currently unhandled, `Editor.Designer.cs:798-805`) to a PNG/frame import that quantises to <=255 palette entries and routes through `RSCache.WriteFile(RSConstants.SPRITES_INDEX, groupId, 0, bytes)`; implement `ExportSpriteDatBtn_Click` (`Editor.cs:908`); and fix PNG export to emit every frame rather than only `thumb` (`Editor.cs:889-896`).
- Surface sprite NAMES in the tab. Index 8 sets the identifiers flag, `ReferenceTableCodec.cs:56-65` already decodes them and `RSReferenceTable.cs:75` already does the reverse lookup through `NameHasher`, but `Editor.cs:624-688` shows bare numeric ids. The client's own 16 known names (`Class1.java:4-27`) are a free, self-proving seed list for a hash->name dictionary.

## Notes and traps

TRAPS, in the order they will bite:

1. NON-CANONICAL: palette entry aliasing. A stored colour of 0x000000 is rewritten to 0x000001 by both the client (`Class324.java:77-79`) and us (`SpriteDefinition.cs:95-96`), because 0 means transparent. The stored byte cannot be recomputed from the decoded value. Keep the palette verbatim. This is exactly CLAUDE.md's "aliased values" case.

2. NON-CANONICAL: the flags byte is not recoverable from pixels. For a 1-pixel-wide or 1-pixel-tall frame, row-major and column-major produce identical bytes, so bit 0x01 is a free choice the original encoder made. Record it.

3. NON-CANONICAL: alpha-plane presence. The client nulls the alpha array when every byte is 0xFF (`Class324.java:127-129`), so a fully-opaque frame may legally be stored with OR without an alpha plane, decoding to identical pixels. Bit 0x02 must be recorded, not inferred.

4. NON-CANONICAL: the palette may hold colours no pixel references, and may hold the same colour twice at different indices. Rebuilding a palette by scanning pixels is guaranteed not to reproduce the bytes.

5. Canvas dimensions are stored, padding is derived. `anInt2719`/`anInt2724` at `Class324.java:70-71` are computed, so do not write them. But canvas w/h are NOT necessarily `max(offsetX+subWidth)`. Our `SpriteDefinition.cs:112` allocates `Math.Max(width, subWidth)` x `Math.Max(height, subHeight)`, which silently papers over any frame that overflows the canvas - replace with the stored canvas size and let a real overflow fail loudly.

6. There is no length check between the end of the pixel planes and the start of the metadata block (which is located by seeking backwards from EOF). Any unread gap there would be invisible to decode and lost on encode. The full sweep must assert exact consumption, or it will not catch this.

7. `RSCache.GetSprite` (`RSCache.cs:691-696`) feeds the raw GROUP container into `Decode`, bypassing `RSArchive`. Safe today - one file per group - but if a future edit adds a second file the multi-file trailer (`chunks x fileCount` int32 sizes plus a chunk-count byte, `AGENTS.md:154-171`) lands where the sprite metadata is read from and the decode silently produces garbage. Any writer must preserve one-file groups.

8. NOT a trap: no XTEA (index 8 is opened with the non-encrypted form at `InterfaceSettings.java:157`; only 5, 6, 23, 26, 28, 30, 31, 36 pass `true`), no unusual compression, and no 637-vs-639 format drift found - the client decoder matches our decoder field for field.

9. DEPENDENTS - breaking sprite decode breaks two features silently. `MapRasteriser.cs:904` pulls map scene icons from index 8, and `TextureGraphEvaluator.cs:425` / `TextureManager.cs:483` pull sprite sources for index 9 texture graphs (node types 18 and 39). Both catch and swallow every exception (`MapRasteriser.cs:908`, `TextureGraphEvaluator.cs:448`), so a regression shows up as a missing icon or a blank texture, never as a failing test.

10. `RSBufferedImage` extends `SpriteDefinition` purely so frames can be reused as tree children (`Editor.cs:680-683`). It overrides `GetWidth`/`GetHeight` (`RSBufferedImage.cs:56-67`) because a frame never populates the inherited fields. Adding per-frame metadata to the base class means deciding which of the two roles each new field belongs to - put the per-frame fields on `RSBufferedImage`, not on `SpriteDefinition`.
