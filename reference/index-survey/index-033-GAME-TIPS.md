# Index 33 - GAME_TIPS (loading screens)

**Format:** fully-understood  
**Capability:** none  
**Effort:** medium

## What it is

Loading-screen definitions plus the manifest that selects between them. NOT a flat list of tip strings - the tips are one element type inside a screen layout.

Client entry point: `InterfaceSettings.java:75` opens it (`openFileStore(-50, false, 1, 33)` -> `Class224_Sub1.aJS5Archive_5035`) and `:97-116` hands the whole archive to `new Class282(...)`, then builds one `Class210` (`implements LoadingScreen`, `Class210.java:5,30-42`) per selected id.

Two groups, and they are different things - this is a "few large blobs" index, not a record table:

GROUP 0 = the manifest, exactly 1 file (file 0). Read by the `Class282` constructor (`Class282.java:64-136`) via `getChildFromFolder(0, 0)` (`:69`). Measured payload 772 bytes, consumed to the byte by my parse:
  u8 version = 3
  u8 typeCount = 10, then 10 u8 type-version bytes = [1,2,2,2,1,1,1,2,1,2]
  u8 categoryEntries = 18, u8 maxCategoryIndex = 17
  s16 defaultScreenId = -1 (only read when version >= 3, `:93-97`)
  18 x { u8 categoryIndex, u8 shuffleFlag, u16 count, count x u16 screenFileId }
All 18 categories hold exactly 19 ids and all have shuffle=0; the 342 ids are precisely group 1's 342 file ids.

GROUP 1 = the screens themselves, 342 files, name hash = Java `hashCode("screens")` = 1926385031 (0x72D24D87), verified. One file = one loading screen, read by `Class282.method3336` -> `getChildFromFolder(1, id)` (`:173`) and decoded by `Class124.method2215` (`Class124.java:96-111`):
  u24 displayDurationMs (`RSBuffer.method1186`, `RSBuffer.java:131-135`; proven a millisecond duration by `Class210.java:147`, which compares `anInt1012 + l` against `currentTimeMillis`)
  u16 secondTiming (`Class210.method25` returns it, role not pinned)
  u8 elementCount, then per element: u8 type index into the 10-entry `Class113[]` (`Class48_Sub2_Sub1.java:223-233`) followed by a fixed-shape record.
Element record sizes, each read straight off its decoder:
  0 `Class298.method3503` i32 -> 4 | 1 `Particle_Sub10.method3141` 28 | 2 `Class64_Sub27.method663` 26 | 3 `Class338.method3781` 32 | 4 `Node_Sub40.method1469` 24 | 5 `RenderType.method1796` 8 | 6 `Class138.method2277` 11 | 7 `MobEntity.method3024` NUL-terminated string + 25 | 8 `Node_Sub46_Sub19.method1634` 2 | 9 `Class362.method3924` 34. The shared 20-byte prefix used by 1/2/3/9 is `Class105.method1716` (`Class105.java:42-62`).

Measured over all 342 files in this cache (my own read-only parse, exact consumption 342/342, 0 failures): only element types 5, 7 and 9 occur - 5400 / 1620 / 342. 18 files are title cards (13 elements, no text, u24=0, u16=500) - one per category. The other 324 are tip screens (22 elements: 16 type-5, 1 type-9, then 5 type-7, u24=9000ms, u16=1000). The five type-7 elements in a screen all carry the SAME string, differing only in their 25 trailing bytes (consistent with an outline/shadow draw pass, not proven). 1620 text occurrences resolve to 18 distinct tips, each used 90 times, all combat-triangle copy ("A crossbow isn't just for killing..."), longest 198 chars, all pure ASCII in this cache.

Which category is shown is a client preference byte: `InterfaceSettings.java:95` reads `Node_Sub9.aClass98_Sub27_3856.aClass64_Sub4_4053.method568(...)`, i.e. `Class64_Sub4.anInt494` on the `Node_Sub27` preferences record (`Node_Sub27.java:319`, written back at `:537`), also exposed to CS2 at `Class247.java:6639`. The user-facing name of that option is not determinable from the obfuscated source.

## Current capability

Nothing index-specific. The only two references to 33 anywhere in `FlashEditor\` are the constant itself:
- `FlashEditor\Cache\RSConstants.cs:48` - `GAME_TIPS = 33, //loading screens`
- `FlashEditor\Cache\RSConstants.cs:98` - the display string in `indexNames`
A repo-wide grep for `GAME_TIPS` finds no other production or test use. This is one of the 27 constants CLAUDE.md notes have no adoption site.

What exists is generic plumbing that happens to cover it:
- `FlashEditor\Cache\RSCache.cs:576-590` `LoadReferenceTables` walks every index, so index 33's table is decoded on load. `GetContainer(33, g)` / `ReadFile(33, g, f)` will hand you raw file bytes today - but that is index-agnostic framing, not knowledge of the format.
- `FlashEditor\Editor.cs:64-76` `editorTypes` is the tab-to-index map and contains 9 indexes; 33 is not one of them, so `LoadEditorTab` (`Editor.cs:471`) has no branch for it. The only place it surfaces in the GUI is the main-menu META tab (`Editor.cs:526-565`), which dumps every reference table into `RefTableListView` - index 33 appears there as one metadata row (group count, version, CRC) with no way to open it.
- `FlashEditor.Tests\Cache\RealCacheConformanceTests.cs` sweeps `_cache.TableIndexes`, which includes 33, at four levels: reference-table re-encode byte-identity (`:66-100`), archive CRC (`:126-154`), container round-trip (`:175-196`), archive payload re-encode against captured bytes (`:226-269`), no-op edit through `PutFile` (`:302-344`) and idx-record re-encode (`:484-493`). With only 2 groups, `RealCacheFixture.ArchivesToExamine` (`RealCache\RealCacheFixture.cs:122-134`) returns both regardless of `FULL=1`, so index 33's framing is always fully swept.
- `FlashEditor.Tests\Cache\RealCacheReferenceTableShapeTests.cs:107` pins index 33 as one of the tables that sets the identifiers flag.

So: the container, the group file-split and the reference table for index 33 are already proven byte-identical. The 363 KB of content inside them is opaque bytes to this editor - no decoder, no model class, no display, no encoder, no content test.

## Gaps

- A `LoadingScreenIndex` definition class with Decode/Encode for group 0 file 0: u8 version, u8 typeCount + N type-version bytes, u8 categoryEntries, u8 maxCategoryIndex, s16 default (version>=3 only), then the 18 x {u8 category, u8 shuffle, u16 count, count x u16 id} table. Must round-trip the type-version bytes verbatim rather than regenerating them.
- A `LoadingScreenDefinition` class with Decode/Encode for a group 1 file: u24 duration, u16 timing, u8 elementCount, then the typed element list.
- Ten element record codecs, one per `Class113`. Three (5, 7, 9) are exercised by this cache; the other seven have to be ported from the client decoders on faith - `Class298.method3503`, `Particle_Sub10.method3141`, `Class64_Sub27.method663`, `Class338.method3781`, `Node_Sub40.method1469`, `Class138.method2277`, `Node_Sub46_Sub19.method1634`. Keep them implemented anyway; the first file that uses one is mis-parsed from that element onward and no sweep here would catch it.
- A cp1252 codec for the type-7 string. `Node_Sub46_Sub6.method1546:11-34` remaps bytes 0x80-0x9F through `Class65.aCharArray497` and substitutes '?' for anything unmapped, so decoding is lossy at the edges. Nothing in this cache exercises it (0 bytes >= 0x80 across all 1620 strings) - but a user typing a curly quote into a tip editor immediately does.
- A codec test against captured bytes, in the shape of `FlashEditor.Tests\Cache\CapturedCacheBytesTests.cs`. Round-tripping this encoder against this decoder proves nothing (CLAUDE.md).
- A full-index byte-identity sweep. This is cheap here and there is no excuse for sampling it: 343 files, 364 KB total, so a `[RealCacheFact]` that decodes and re-encodes every file in both groups and asserts byte equality is the whole index. Model it on `RealCacheItemDefinitionTests`.
- A GUI tab. Text/tree editing of the 18 tips and the 18 category lists is cheap and fits the `editorTypes` + `LoadEditorTab` pattern directly (`Editor.cs:64-76`, `:471`). A visual preview is not cheap - see the cross-index dependency in Notes.

## Notes and traps

Traps, in the order they will bite:

1. **Group 1's file ids are non-contiguous.** They are {0} then {326..666} - 342 ids, max 666. A loop over `0..fileCount-1` reads the wrong files. Take them from `GetValidFileIds()`.

2. **The manifest's type-version bytes are a compatibility handshake, and getting them wrong fails silently.** Group 0 stores `[1,2,2,2,1,1,1,2,1,2]`, which is exactly `Class113.anInt955` for the ten types in `Class48_Sub2_Sub1.method476` order (`Class100.java:7`=1, `Class47.java:3`=2, `Class137.java:3`=2, `Node_Sub44.java:7`=2, `Class365.java:11`=1, `Class280.java:21`=1, `Node_Sub10_Sub3.java:7`=1, `Class308.java:10`=2, `Class4.java:17`=1, `Class18.java:7`=2). If the count or any byte disagrees, `Class282.java:86-89` sets `anInt2124 = -1` and empties both arrays - the client shows **no loading screens at all**, with no error. Re-encode these bytes verbatim.

3. **`defaultScreenId` is a signed short and -1 is load-bearing.** It is -1 in this cache. If it is ever set to something else, `Class282.java:110-113` **prepends** it to every category list and `:153` makes the shuffle skip slot 0. So the same category bytes mean different things depending on one field elsewhere in the file.

4. **Signedness is mixed and untestable.** `Class105.method1716` reads s16, s16, u16, u16, s16 in a row; `RenderType.method1796` reads u16 then two s16; `Class138.method2277` uses `RSBuffer.method1227` (`RSBuffer.java:482-497`), a **signed** 24-bit read, while the file header uses `method1186` (`:131-135`), an **unsigned** 24-bit read. A byte-identity sweep cannot detect getting these backwards - they re-encode to the same bytes and show the wrong number in the editor. This is CLAUDE.md's SIGNEDNESS-DIFFERS case.

5. **Cross-index dependency, and it is a hard one for any preview.** `InterfaceSettings.java:106` builds `new Class362(Class1.aJS5Archive_67, HintIcon.aJS5Archive_348)` - sprites from **index 32 or 34** and fonts from **index 13**. Index 32/34 is chosen at `:73-74` by `Node_Sub5_Sub2.aBoolean5535`; **index 34 is empty in this cache**, so index 32 is the only usable source. The type-5 element's leading u16 is a sprite id in that archive: file 0's first element is 3763, and index 32 has 3795 groups, which corroborates the reading. Rendering a screen therefore needs the sprite tab's decoder and a font decoder this editor does not have. Ship the text/tree editor first.

6. **The 342 per-file name hashes must survive a save.** All 342 are distinct and none crack against any naming pattern I tried; `ReferenceTableCodec.cs:141-152` already round-trips them, so just do not rebuild the table from scratch.

7. **No XTEA anywhere on this index**, and both groups are GZip with a 2-byte version trailer. Per AGENTS.md, a GZip re-encode is never byte-identical, so compare decompressed payloads, never containers - and note that any save to index 33 rewrites 364 KB and moves its reference-table entry.

8. **Group 0's name hash is unknown.** 530115961 (0x1F98ED79). I ruled out every a-z string up to 6 characters exhaustively and ~200 targeted words and two-word joins up to 12 characters. Group 1's is confirmed as "screens". Do not guess group 0's - CLAUDE.md's warning about plausible mappings applies exactly here.

9. **Semantic naming beyond what the client does is guesswork.** I can prove u24 is a millisecond duration (`Class210.java:147`) and that type 7 carries text. I cannot prove what u16 is, what the 25 bytes after each string are, or what the 18 categories represent - only that the category is picked by a saved preference byte. Name the fields after their usage or leave them numbered.

Measured facts about the cache worth recording (these do not change): idx33 is 12 bytes = 2 groups. Its reference table is format 6, version 13, flags 0x01, group versions 9 and 9, CRCs 653C04DA and 35CE55E0, file counts 1 and 342, and it consumes exactly 2098 of 2098 bytes with no trailing tail. Group 0 stores 616 bytes -> 772 payload. Group 1 stores 11,683 -> 363,961 payload, 1 chunk, body 362,592, file sizes 149 to 1310. 18 distinct tip strings across 342 screens.
