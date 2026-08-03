# Index 16 - OBJECTS_DEFINITIONS (loc / world-object definitions)

**Format:** fully-understood  
**Capability:** complete  
**Effort:** trivial

## What it is

Object ("loc") definitions - the config records for every world object the client can place on a tile: model group lists, name, tile footprint, clip/walk flags, recolour/retexture tables, morph (varbit/varp) tables, ambient sounds, cursors, map-scene and map-element icon ids, menu options and a parameter map.

Client authority: `InterfaceSettings.java:172` opens index 16 as `Class375.aJS5Archive_3167 = openFileStore(-87, false, 1, 16)`; `InterfaceSettings.java:368` then sets `fileType = 2` on it, the same treatment given to the other definition indexes (2, 17, 18, 19, 20, 21). That archive is handed to the loc provider at `InterfaceSettings.java:271-272`: `Class130.aClass302_1028 = new Class302(..., Class375.aJS5Archive_3167, MODEL_FILE_SYSTEM)` - index 16 plus index 7 (models). `Class302.method3546` is the accessor ("// OBJECT DEFS" comment at `Class302.java:83`) and it addresses the record as `getChildFromFolder(za.method1674(-1035933944, i), Class151.method2444(i, -119))`, where `za.java:19` returns `i >>> 8` and `Class151.java:27` returns `i & 0xff`. So:
  - GROUP  = objectId >> 8 (a 256-slot page of definitions)
  - FILE   = objectId & 0xFF (one definition)
  - RECORD = an opcode stream: unsigned-byte opcode, opcode-implied payload, repeat until opcode 0 (`Class352.method3850`, lines 306-326, dispatching to `method3863` at :1036-1486).

Measured from this cache (read-only parse of idx255 group 16 and idx16, done for this report): reference table is format 6, version 1121, flags 0x00 - **no identifier hashes, no whirlpool, no sizes** - 224 groups with contiguous ids 0..223 and 56,199 files total. 160 groups hold a full 256 files; 64 hold fewer, and 63 of those have *gaps in the middle* of the file-id range (first id 0, last id 255, holes between). Group 223 is the only short-and-contiguous one, holding ids 0..176, so the highest object id present is 57,264. Container compression: 223 GZip, 1 BZip2 (group 174); all 224 carry a 2-byte version trailer; chunk count is 1 (index 0 is the only multi-chunk index).

## Current capability

Decode, encode, a whole-index byte-identity sweep, and a GUI editing tab all exist.

DECODER - `FlashEditor/Definitions/ObjectDefinition.cs:409` (`Decode(JagStream, int[])`) drives the opcode loop; `:447` (`Decode(JagStream, int)`) is the per-opcode handler. Every opcode the 637 client handles is handled here, plus 12 the client does not (44, 45, 68, 90, 96, 189, 190-195), none of which occur in this cache. `reference/hydra-637-definitions/object-opcodes.md:53-58` records the three-pass exact-consumption result: 56,199 / 56,199 clean under the client's widths, under our widths, and under the production decoder, with zero throws.

ENCODER - `ObjectDefinition.cs:809` (`Encode()`), assembled by `WriteRecordsInStreamOrder` (`:1155`). It is explicitly non-canonical-safe: `_streamRecords` (`:383`) keeps every opcode *in the order it was read* paired with *the exact payload bytes*, so opcode order and repeated opcodes reproduce verbatim; only the last occurrence of an opcode takes a freshly-encoded payload (`:1179`). `DropOpcode` (`:1116`) removes an opcode from both the hit map and the recorded stream so a flag turned off in the GUI actually disappears.

BYTE-IDENTITY SWEEP - `FlashEditor.Tests/Cache/RealCacheObjectDefinitionTests.cs:191` `AllObjectDefinitions_ReEncodeToTheirCapturedBytes`, backed by `:80` (decodes without throwing) and `:123` (consumes its buffer exactly, with a trailing guard byte so an overrun cannot masquerade as a clean stop). **It covers the whole index on every run**: `LoadDefinitions` (`:638`) enumerates `_cache.ArchivesToExamine(table)`, and `RealCacheFixture.cs:125` returns all archives when `all.Count <= SampleArchivesPerIndex`, which is 250 (`RealCacheFixture.cs:24`) against this index's 224. `FLASHEDITOR_TEST_CACHE_FULL=1` changes nothing here, and `ReportSampling` (`:751`) says so in the test output.

CODEC TEST AGAINST CONSTRUCTED BYTES - `FlashEditor.Tests/Cache/ObjectDefinitionCodecTests.cs:14`, a hand-built stream exercising ~40 opcodes with a decode/encode/decode/encode four-phase byte compare. Per CLAUDE.md this proves little on its own; the real-cache sweep is what carries the weight.

BEHAVIOURAL TESTS - bare-flag add/remove/re-encode (`RealCacheObjectDefinitionTests.cs:372, 401, 431, 454, 481`), the two payload widths that once desynced (`:506` opcode 75 = 1 byte, `:521` opcode 72 = signed short), opcode-order replay (`:552`), repeated opcodes (`:572`), and both ambient-sound opcodes together (`:587`).

GUI - `ObjectEditorTab` holds `GameObjectListView`, a `FastObjectListView` with 8 columns (`Editor.Designer.cs:1129-1136, 1160-1206`): id, name, sizeX, sizeY, walkable, isClipped, ambientSoundId, morphVarbit. Loading is on a `BackgroundWorker` (`Editor.cs:750-793`). Cell edits go through `ObjectListView_CellEditStarting` (`Editor.cs:1156`, clones the pre-edit state) and `ObjectListView_CellEditFinished` (`Editor.cs:949-968`), which skips the write when the re-encoded bytes are unchanged and otherwise calls `cache.WriteFile(RSConstants.OBJECTS_DEFINITIONS_INDEX, ...)`. Selecting a row loads its models into the OpenGL viewer (`Editor.cs:1439-1464`).

READ API - `RSCache.GetObjectDefinition(archiveId, fileId)` at `RSCache.cs:805`, id folded as `archiveId * 256 + fileId` (`:808`), matching the client's split. Consumed by the map path at `Map/MapRasteriser.cs:863` as `GetObjectDefinition(objectId >> 8, objectId & 0xFF)`.

## Gaps

- Nothing is missing to reach 'complete' - decoder, encoder, whole-index byte-identity sweep and a GUI editing tab are all present and cited above. Everything below is polish beyond that bar.
- Opcodes 78 and 79 are conflated into one `ambientSoundId` field (`ObjectDefinition.cs:294, 631-641`). The client keeps them apart - `anInt2996` for op 78 versus `anInt2949` for op 79 - and forwards both to the sound emitter as separate slots (Node_Sub31_Sub4.java:108,118). 81 definitions in this cache carry both opcodes; on those the editor shows one number where the client has two. The bytes still round-trip, because `Encode` (`:977-1003`) re-emits only the later opcode and replays the other verbatim from `_streamRecords`, so this is an editing and display defect, not a corruption one. Fixing it means splitting the field and adding a column.
- Only 8 of roughly 90 decoded fields are reachable from the GUI. Models, recolour/retexture, morph tables, actions/menu ops, offsets, scales, lighting, cursors and the parameter map are all decoded and encoded but have no editor surface.
- No test proves an index-16 write reaches disk. `RSCacheWriteFileTests` builds a synthetic store on index 0 and 255; the captured-bytes fixtures in `CapturedCacheBytesTests` are reference tables, archives and index-5 XTEA. CLAUDE.md's rule applies - verify persistence by reopening the store, not by reading back through the cache that wrote it.
- `Editor.cs:761` and `:961` derive `filesPerArchive` from the *first* archive's valid file count. That is 256 here only because group 0 happens to be full; 64 of the 224 groups are not. Nothing corrects for it, so the id arithmetic is right by luck rather than by construction, and the load loop requests 1,145 file ids that do not exist (see traps).

## Notes and traps

TRAPS AND THINGS ALREADY PAID FOR

1. NON-CANONICAL ENCODING IS REAL HERE AND ALREADY HANDLED. Opcode order within a record is arbitrary (the client's reader is a bare loop, `Class352.java:312-321`) and some definitions repeat an opcode with a different value each time. `_streamRecords` and `WriteRecordsInStreamOrder` exist for exactly this. Do not "tidy" `Encode` into a fixed ascending emission order - it will fail the sweep on thousands of definitions.

2. THE SPARSE-GROUP SHAPE IS THE MAIN LANDMINE. 64 of 224 groups hold fewer than 256 files, and 63 of those have holes in the middle of the id range. `RSCache.ReadFile` (`RSCache.cs:604-605`) throws `FileNotFoundException` for an absent file id, so `Editor.cs:766-782`'s `for (file = 0; file < 256; file++)` loop throws and swallows 1,145 exceptions on every object-tab load. Anything new that enumerates this index must take its file ids from `RSArchiveEntry.GetValidFileIds()` (as `RealCacheObjectDefinitionTests.cs:651` correctly does), never from a count.

3. NO NAME HASHES. Index 16's reference table flags are 0x00 - measured, not assumed. A group or file is addressable only by id, and no object can ever be looked up by name from this cache. Do not build a name-lookup feature on this index.

4. NO XTEA ANYWHERE ON THIS INDEX. Index 5 is the only encrypted family. But 223 of the 224 containers are GZip, so per AGENTS.md a re-encode is never byte-identical - the "a save that changes nothing must write nothing" invariant is load-bearing here, and it is honoured in two places: `Editor.cs:952-956` compares re-encoded definition bytes before calling `WriteFile`, and `RSCache.WriteFile` (`:141-148`) compares against the stored payload. Group 174 is the lone BZip2 container; anything that assumes GZip on this index is wrong for one group in 224.

5. THE REFERENCE DOC IS PARTLY STALE - IN OUR FAVOUR. `reference/hydra-637-definitions/object-opcodes.md:91-92, 192-198` records opcodes 28/29/39 as mislabelled, with `modelBrightness` fed from opcode 28 and `modelContrast` from opcode 29. **That is already fixed.** `ObjectDefinition.cs:214` is now `modelBrightness => ambientLighting` (opcode 29, the client's `64 + anInt2931` ambient term) and `:222` is `modelContrast => contrastLighting` (opcode 39, the client's `850 + anInt2980`). Do not "fix" it again. The doc's 78/79 finding, by contrast, is still live.

6. TWO PAYLOAD WIDTHS THAT ALREADY COST REAL WORK. Opcode 72 is a *signed short* shifted left 2 (`Class352.java:1113`, `ObjectDefinition.cs` case 72, encoder at `:766`), not a byte; opcode 75 is a *one-byte value*, not a bare flag (`Class352.java:1116`, client default -1 at `:254`). Both were wrong once and desynced thousands of definitions, and opcode 72 re-synchronised by accident often enough to slip past an exact-consumption check. They are pinned by `RealCacheObjectDefinitionTests.cs:506` and `:521`.

7. OPCODES 14/15 (sizeX/sizeY) ARE TRANSPOSED RELATIVE TO THE CLIENT'S DEOBFUSCATED NAMES, and the GUI exposes both as editable columns. Both are one unsigned byte so nothing desyncs and the sweep cannot see it, but a user swapping a 1x2 object's footprint in the grid may be editing the other axis. The client's names come from a deobfuscator pass and are not independently corroborated - CLAUDE.md's "a semantic name in reference/ is a claim" applies to the client's names too. Unresolved; do not silently pick a side.

8. OPCODE 24 KEEPS 65535 WHERE THE CLIENT MAPS IT TO -1 (`Class352.java:1455-1457`). 5,423 definitions carry it. Round-trips fine (`ObjectDefinition.cs:880` emits the stored value back), but any consumer reading `animationId` sees 65535 rather than "no animation".

9. DEPENDENCIES ON OTHER INDEXES. Index 7 (models) - the client constructs the loc provider with both (`InterfaceSettings.java:271-272`), and the GUI's model preview follows `modelIds[0]`. Index 2 (config) - opcode 102 (`mapSceneIcon`, `ObjectDefinition.cs:239`) resolves into config group 34 via `RSCache.GetMapSceneIcon` (`RSCache.cs:740`), and opcode 107 (`mapElementId`, `:258`) into config group 36; both are settled by client usage, not by name - `anInt2990` goes to `Class335.method3766` (Class180.java:42, Class277.java:121) and `anInt2958` to the minimap draw (Class201.java:77). Index 5 (maps) - `MapRasteriser.cs:863` resolves every located object through this index, so a regression here shows up as wrong map icons before it shows up anywhere else.

10. NO 637-VERSUS-639 FORMAT DIVERGENCE ON THIS INDEX. Unusually for this cache, every payload width our decoder uses matches the 637 client's exactly, proven three ways over all 56,199 definitions (`object-opcodes.md:53-58`). The 12 opcodes we handle that the client does not are all absent from the data, so they are inert rather than wrong - leave them.
