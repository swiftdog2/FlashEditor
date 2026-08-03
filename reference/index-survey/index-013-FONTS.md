# Index 13 - FONTS

**Format:** fully-understood  
**Capability:** none  
**Effort:** small

## What it is

Font metrics - one fixed-layout 263-byte record per font, and nothing else. The 4041 figure is slot count, not group count: `main_file_cache.idx13` is 24246 bytes = 4041 six-byte slots, but only 26 are non-zero and one of those (slot 0, raw bytes `ff0000000000`) is a not-present marker also found on idx2/10/28/31/32. The reference table declares 25 groups, ids 305, 307, 468, 473, 494-497, 584, 591, 645-648, 764, 776, 819, 1591, 2244, 2710, 3237, 3793-3795, 4040, every one with exactly one file (id 0). So group = font, file = the whole record, record = the metrics for one font.

Every group decompresses to exactly 263 bytes and consumes all 263. Layout, taken from the 637 client's `Class197(byte[])` constructor (`Class197.java:18-92`):
  off 0   u8   version - the client throws unless it is 0 (`:22-26`); all 25 are 0
  off 1   u8   kerning flag (`:28`); 0 in all 25 groups here, so the kerning branch `:33-84` is dead in this cache
  off 2   256  per-character advance width, indexed by byte code, read back unsigned (`:30-31`, `0xff &` at `:196`)
  off 258 u8   default line height (`:86`; used as the line step at `Class197.java:172` and `RSFont.java:383`)
  off 259 u8   read and discarded (`:89`)
  off 260 u8   read and discarded (`:90`)
  off 261 u8   ascent - `anInt1517` (`:91`)
  off 262 u8   descent - `anInt1514` (`:92`)
Ascent/descent are settled by use, not by name: `IntegerNode.java:680,686` puts the glyph box at `baseline - anInt1517` to `baseline + anInt1514`, and `RSFont.java:942` writes `anInt1514 + anInt1517` as the box height.

Index 13 holds no pixels. The glyphs live in index 8 at the SAME group id: `Class114.method2151` loads `Class324.method3684(spritesArchive, i)` and `Class119_Sub1.method2182(fontsArchive, i)` with one id `i` (`Class114.java:82,89`), and `InterfaceSettings.java:210-211` proves which archive is which by passing `HintIcon.aJS5Archive_348` (opened as index 13 at `InterfaceSettings.java:76`) and `Class332_Sub2.aJS5Archive_5423` (index 8, `:157`) into `Class77.method775` -> `Class64_Sub17.aJS5Archive_3687` / `Class64_Sub16.aJS5Archive_3683` (`Class77.java:20-21`). Verified in the data: all 25 index-13 group ids exist in index 8 with the identical name hash, and each of those index-8 groups holds exactly 256 sprites - one per byte code.

Names are recoverable (index 13 sets the identifiers flag). Java `String.hashCode` brute force names 11 of 25: 494 p11_full, 495 p12_full, 496 b12_full, 497 q8_full, 647 lunar_alphabet, 648 lunar_alphabet_lrg, 764 barbassault_font, 819 surok_font, 3793 verdana_11pt_regular, 3795 verdana_15pt_regular, 4040 verdana_13pt_regular. The other 14 did not fall to the wordlists tried.

## Current capability

No font support whatsoever. `RSConstants.FONTS_INDEX = 13` is declared at `FlashEditor/Cache/RSConstants.cs:28` and referenced nowhere else in the solution - a repo-wide grep for `FONTS_INDEX` returns that one declaration line and nothing more. There is no `FontDefinition`, no `RSCache.GetFont`, no font decode or encode anywhere (`FlashEditor/Definitions/` holds Item, NPC, Object, Model, FloorUnderlay, FloorOverlay, MapSceneIcon, Sprites/, Tracks/ and nothing font-shaped). Index 13 is absent from `Editor.editorTypes` (`FlashEditor/Editor.cs:62-75`), so there is no tab and `LoadEditorTab`'s switch (`Editor.cs:524`) has no case for it. No test in `FlashEditor.Tests` names index 13. Every `Font` hit in `Editor.cs`, `Editor.Designer.cs` and `FlashEditor/Map/*.cs` is `System.Drawing.Font`.

What DOES exist is index-agnostic plumbing that already covers index 13 byte-for-byte, which is the substrate an implementer starts from:
- `RSCache.LoadReferenceTables` (`FlashEditor/Cache/RSCache.cs:542-551`) decodes index 13's table at startup, so it shows up as a row in the CRC-table tab (`Editor.cs:526-560`) - metadata only, no payload view.
- `RSCache.ReadFile(13, groupId, 0)` and `RSCache.WriteFile` work generically; the payload is opaque bytes.
- `RealCacheFixture.SampleArchivesPerIndex = 250` (`FlashEditor.Tests/Cache/RealCache/RealCacheFixture.cs:24`) and index 13 has 25 archives, so `ArchivesToExamine` returns ALL of them (`:125-126`) in sampled and full runs alike. That means `RealCacheConformanceTests.ArchiveCrcs_MatchTheCapturedContainerBytes` (`:119`), `Containers_PreserveTheirPayloadAndHeaderAcrossReEncode` (`:169`), `Archives_ReEncodeToTheCapturedPayloadBytes` (`:218`), `UnchangedArchives_SurviveTheEditPathWithTheirPayloadIntact` (`:295`) and `SingleFileArchives_CarryNoTrailerInTheCapturedBytes` (`:365`) already sweep all 25 font groups.
- `IndexRecords_ReEncodeToTheCapturedBytes` (`RealCacheConformanceTests.cs:479-498`) walks every slot 0..RecordCount, so all 4041 idx13 records including the `ff0000000000` slot 0 re-encode byte-identically.
- `ReferenceTables_ReEncodeToTheCapturedBytes` (`:59`) and `RealCacheReferenceTableShapeTests` cover index 13's table.
None of that decodes a font. It proves the container is safe to carry, not that anything understands it.

The glyph half is nearer: the Sprites tab enumerates every index-8 group via `cache.GetSprite` (`Editor.cs:651-655`, `RSCache.cs:693`), which includes all 25 font sheets, and `SpriteDefinition` has both `DecodeFromStream` and `Encode` (`Definitions/Sprites/SpriteDefinition.cs:249`). But that loop swallows per-group exceptions (`Editor.cs:665-667`) and no test pins sprite decode, so I cannot claim the font sheets actually render - only that they are in scope.

## Gaps

- A `FontDefinition` class in `FlashEditor/Definitions/` implementing `IDefinition` with `Decode(JagStream)`/`Encode()` over the 263-byte record: version byte, kerning flag, `byte[256]` advance widths, line height, the two discarded bytes kept verbatim, ascent, descent. Plus the kerning branch (`Class197.java:33-84`): two `byte[256]` tables then two delta-encoded per-character arrays sized by the first table, then the 256x256 kern matrix derived via `Class378.method4003`. Dead in this cache but must be implemented and must round-trip, same reasoning as the format-7 reference-table branches in AGENTS.md.
- `RSCache.GetFont(int groupId)` alongside `GetSprite`, reading `ReadFile(RSConstants.FONTS_INDEX, groupId, 0)`.
- A codec test against captured bytes - not a round trip. Capture one of the 25 payloads into `FlashEditor.Tests/Fixtures/RealCache/` the way the existing archive fixtures are, and assert the decoded field values against the client's read order.
- A byte-identity sweep over all 25 groups, comparing the DECOMPRESSED 263-byte payload (never the container - one of the 25 is BZip2 and the other 24 are GZip, which never re-encodes identically). Assert exact 263-byte consumption and 263-byte re-emission, no `or` in the assertion.
- A Fonts tab: add `RSConstants.FONTS_INDEX` to `Editor.editorTypes` (`Editor.cs:62-75`), a `case` in the `LoadEditorTab` switch following the `SPRITES_INDEX` pattern (`Editor.cs:624`), a designer tab plus loading label and progress bar matching `Editor.Designer.cs`, and a list view keyed on the 25 groups.
- Name recovery for the 14 groups whose hash has not been cracked, so the tab lists fonts by name rather than by id.
- A glyph preview that joins index 13 metrics to the index-8 sheet at the same group id - the only way to see whether an edited width is right. This is the bulk of the work; everything above it is mechanical.

## Notes and traps

Traps, in the order they will bite:

1. **4041 is a slot count, not a group count.** Only 25 groups exist, at sparse ids up to 4040, and slot 0 carries the not-present marker `ff 00 00 00 00 00` - a length of 16,711,680 pointing at sector 0. Enumerate from `referenceTable.GetArchiveEntries()`, never from `store.GetFileCount(13)`. `GetFileCount` (`RSFileStore.cs:49`) returns filesize/6, which is 4041 here.

2. **Bytes 259 and 260 are read and thrown away by the client** (`Class197.java:89-90`) and they vary per font - `(9,6)` for group 305, `(43,58)` for 648, `(1,1)` for 776. A decoder that models the record as "widths, line height, ascent, descent" loses them and the re-encode differs from bytes nobody edited. Keep them verbatim. This is the same class of defect CLAUDE.md lists under non-canonical encodings, arriving by a different route: not two encodings of one value, but a value with no meaning that still has to survive.

3. **Byte 258 is stored only when the kerning flag is clear.** With the flag set the client derives the same field as `left[32] + width[32]` (`Class197.java:84`) and reads no byte at all, so the record length changes and the field becomes computed rather than stored. All 25 groups here have the flag clear, so the kerned encoder can never be defended by a sweep against this cache - write it, and say in a comment that nothing tests it.

4. **Byte 258 is not the space advance.** Measured across all 25: the four verdana fonts all store 35 while `width[32]` is 4, and group 305 stores 12 against a `width[32]` of 3. It is the default line height, used as the line step at `Class197.java:172` and `RSFont.java:383`.

5. **The version byte must be 0 or the client throws** (`Class197.java:22-26`). An editor that lets it be set to anything else produces a cache that crashes at font load.

6. **Compression is mixed and one group is the odd one out.** 24 of the 25 stored containers are GZip; group 494 (`p11_full`) is BZip2. Per AGENTS.md a GZip re-encode is never byte-identical, so any font sweep must compare the decompressed payload. `RSContainer` already preserves the type across re-encode and the conformance sweep proves it for these 25.

7. **Hard dependency on index 8, by group id.** Metrics without glyphs are unviewable and glyphs without metrics are unlayoutable. The join is id-for-id and is independently confirmed twice over: the client passes one id to both archives (`Class114.java:82,89`), and in the data all 25 ids carry identical name hashes in both tables with 256 sprites each on the index-8 side. This is the rare case where the join is self-proving rather than merely plausible - contrast the track-name join CLAUDE.md warns about.

8. **No XTEA, no multi-file groups, no chunking.** Every group is one file, single chunk, no trailer beyond the 2-byte container version. Nothing here needs the key table.

9. Marginal client bug, noted for completeness rather than as a blocker: `Class197.method2675` calls `method2673` with 8364 and 215 for the `&euro;` and `&times;` entities (`Class197.java:283,272`), indexing a `byte[256]`. The 8364 path throws. It does not affect the on-disk format.
