# Index 7 - MODELS

**Format:** fully-understood  
**Capability:** read-only  
**Effort:** very-large

## What it is

Index 7 is the client's `MODEL_FILE_SYSTEM`: 3-D meshes, one mesh per group, one file per group.

Client authority. `InterfaceSettings.java:165` opens it - `Class76_Sub9.MODEL_FILE_SYSTEM = Class42_Sub3.openFileStore(-92, false, 1, 7)`. Every load goes through `Node_Sub6.method981(JS5Archive, int i, int i_16_, particleId, toOverride)` (`Node_Sub6.java:59-66`), whose body is `getChildFromFolder(i_16_, i)` then `new Model(is, i_16_, ...)`. Every call site passes fileId `0` and the model id as the group id - `Class66.java:38`, `Node_Sub10_Sub16.java:29`. So **group id == model id, file id is always 0**, and 63,614 groups == 63,614 models. Reference-table file counts agree (AGENTS.md index map: 63614 groups / 63614 files).

One record is a complete mesh whose header sits at the END of the buffer: vertex count, face count, textured-face count, a flags byte, five per-array presence flags, and the byte lengths of the delta/index/extra blocks. Vertices are delta-encoded per axis with a per-vertex mask byte; faces are triangle-strip encoded with a 4-opcode stream; face colour, render type, priority, alpha, skin and texture id each live in their own parallel block read by its own cursor.

Measured over this cache with a throwaway sector/container walker (all 63,614 groups read, none missing):
- compression: **63,614 / 63,614 GZip**, every one carrying a **2-byte version trailer**. No XTEA anywhere - every group inflated with a plain gzip read, no key.
- 260,673,459 bytes of decompressed model data; smallest 24 bytes, median 2,331, largest 91,578. This is by far the heaviest index in the cache.
- Format split: **63,605 end `FF FF`** -> `decoder_newer_format` (`Model.java:381`). **2 (groups 6700 and 6701)** end otherwise -> legacy `method2587` (`Model.java:1363`). **7 (groups 63607-63613)** are the newProtocol newest format, selected by id at `Model.java:90-93`; their 3-byte header reads `01 01 10` = version 1, unused, **formatType 16**.
- **Zero models end `FF FD`.**
- formatType across the 63,605 newer models: **12 -> 39,043; 14 -> 1,934; 15 -> 22,628**.
- Textured faces: 55,759 models carry at least one. Type counts: **type 0 = 130,067, type 1 = 54,989, type 2 = 285,915, type 3 = 1,098**. **50,742 models carry at least one type 1/2/3 face.**
- 213 models carry particles (flags bit 1), 250 carry bonds (flags bit 2).
- Per-face skin present in 21,439 models, per-vertex skin in 26,832; both reach 255.

## Current capability

**Decoder - yes, three formats.** `FlashEditor/Definitions/ModelDefinition.cs`: entry `Decode` (`:113`), format selection `GetModelFormat` (`:164`), newer `DecodeRS2` (`:206`), legacy `DecodeOld` (`:477`), newest `DecodeRS3` (`:786`). Derived data for the viewer: `ComputeNormals` (`:1109`), `ComputeTextureUVCoordinates` (`:1191`), `ComputeAnimationTables` (`:1305`), `ComputeVertexColours` (`:1385`), HSL palette `BuildHslLut` (`:1562`) / `RepackHsl` (`:1610`).

**Encoder - no.** `ModelDefinition.Encode()` is `=> throw new NotSupportedException("Model re-encoding is out of scope for viewer.")` at `ModelDefinition.cs:748`. Nothing anywhere calls `WriteFile(RSConstants.MODELS_INDEX, ...)`; the only `WriteFile` sites are items, objects, NPCs (`Editor.cs:944, 966, 986`) and maps (`Cache/Region/MapSquareLoader.cs:176, 184`).

**Cache plumbing - yes.** `RSCache.GetModelDefinition(archiveId, fileId)` (`Cache/RSCache.cs:837`), `RSCache.EnumerateModelReferences()` (`:824`), `Cache/ModelReference.cs` (`ModelID => ArchiveId`, `:19`).

**GUI - view only.** `ModelViewerTab` with `splitContainer1` = OpenGL `glControl` + `ModelListView` (`Editor.Designer.cs:1225-1297`), registered as an editor tab at `Editor.cs:71`, populated at `Editor.cs:851-863`, selection handler `ModelListView_SelectedIndexChanged` (`Editor.cs:1200-1276`), rendering in `FlashEditor/ModelRenderer.cs` (render type 2 correctly dropped at `:68`). Models are also loaded read-only by the NPC, item and object tabs with recolour transforms applied to a clone (`Editor.cs:1284-1471`, `CloneForRendering` at `ModelDefinition.cs:1370`). There is no control anywhere that edits a mesh and no save path.

**Tests - one sanity sweep, no byte identity.** `FlashEditor.Tests/Cache/RealCacheModelTests.cs:51` `AllModels_DecodeToGeometryThatIndexesItsOwnVertices` decodes models and asserts every face index falls inside the model's own vertex array (`Validate`, `:136`). It walks `_cache.ArchivesToExamine(table)`, so it is **sampled unless `FLASHEDITOR_TEST_CACHE_FULL=1`** (`:117-121`) - unlike the map suites, this one really is narrowed by the switch. `FlashEditor.Tests/Definitions/HslPaletteTests.cs:79,127` pins `ModelDefinition.RawHslToRgb`. Nothing re-encodes a model, and nothing can, because there is no encoder.

## Gaps

- ModelDefinition.Encode() (ModelDefinition.cs:748) throws. Three encoders are needed - newer (63,605 models), legacy (2 models: groups 6700 and 6701), newProtocol newest (7 models: 63607-63613) - each reproducing the footer, the parallel block order, and the exact byte-lengths the original declared.
- A codec test against captured bytes. Round-tripping our encoder against our decoder proves nothing (CLAUDE.md); pin at least one model of each of the three formats against bytes lifted from the cache, plus one against a hand-checked field table.
- A full-index byte-identity sweep over all 63,614 groups. It does not exist. Nothing today proves a model re-encodes to what it was read from, and the existing sweep (RealCacheModelTests.cs:51) only checks that face indices are in range and is sampled by default.
- Decoder completeness for texture faces. DecodeRS2 (ModelDefinition.cs:458-469) and DecodeRS3 (:1091-1103) decode ONLY type-0 textured faces. Types 1-3 are skipped entirely: no UVs (anIntArray1389/1404/1390), no texture alpha, colour, layer index or the type-2 scale pair. That is 342,002 of 472,069 textured faces, across 50,742 of the 63,605 newer-format models - roughly 80% of the index.
- Particles and bonds are never read. The client reads them from offset i_99_ (Model.java:753-800): 213 models carry particles, 250 carry bonds. ModelDefinition has ParticleEffectId/ParticleAnchorVert fields (:78-80) that nothing ever assigns.
- A GUI editing surface. ModelViewerTab is a list plus an OpenGL preview; there is no property grid, no mesh editor, and no save button on the models path. Reaching 'complete' means a tab that fits the Editor.Designer.cs pattern plus a write path through RSCache.WriteFile(RSConstants.MODELS_INDEX, ...).

## Notes and traps

EVIDENCE THE FORMAT IS FULLY UNDERSTOOD. I ported the client's `decoder_newer_format` block layout (`Model.java:381-495`, offsets i_77_ through i_99_, plus the particle/bond tail at `:753-800`) into a throwaway Python sweep and computed the expected end-of-data for every model. **All 63,605 newer-format models consume to the byte, 0 mismatches.** Both legacy models check out by hand as well: group 6700 is 180 vertices / 347 faces and its sections sum to 2,972 + 18-byte footer = 2,990 = its exact length; 6701 sums to 1,673 + 18 = 1,691. So an encoder has a complete, verified target. Not independently verified: the 7 newProtocol models' exact consumption (their header `01 01 10` was confirmed).

THE ONE-BYTE TRAP THAT WILL BITE FIRST. When flags bit 3 is set, the embedded `formatType` byte lives at `footerStart - 1`, i.e. immediately BEFORE the 23-byte footer, not inside it. The client reaches it with `caret -= 7; read; caret += 6` (`Model.java:401-405`). My first sweep was off by exactly -1 on all 24,562 bit-3 models until I accounted for it. `ModelDefinition.cs:233-238` ports the seek correctly, but an encoder that sizes the tail as 23 bytes will corrupt every one of them.

LIVE DEFECT 1 - VERTEX SCALE IS BAKED IN AND THE CLIENT DOES NOT DO IT THERE. `decoder_newer_format` never shifts vertices. The client's *callers* do it after loading: `model.method2592(13746, 2)` when `formatType < 13` - `Class107.java:175-176`, `Class152.java:114-116`, `Node_Sub10_Sub16.java:33-34`, `ItemDefinition.java:155`. `method2592` (`Model.java:1682-1700`) shifts vertices **and** the UV arrays left by 2. Our decoder shifts inside `Decode`: `ModelDefinition.cs:380-386` (conditional), `:1004-1010` (conditional), and `:616-620` (**unconditional** in the legacy path). This affects **39,043 of 63,605 models**. It is fine for viewing but it means `VertX/Y/Z` are not the on-disk values, so any encoder must reverse the shift exactly - or, better, stop baking it in and let the viewer apply it.

LIVE DEFECT 2 - WRONG SMART VARIANT ON THE NEWPROTOCOL PATH. `ModelDefinition.cs:1050` reads the texture-mapping index for `FormatType >= 16` with `st7.ReadShortSmart()`, which is `ReadSmart` (`JagStream.cs:533`, bias -64 / -0xC000). The client uses `readSmart(454)` (`RSBuffer.java:857`, bias 0 / -32768) at `Model.java:1115`. One-byte values come out **64 too low**. All 7 newProtocol models are formatType 16, and 63607, 63608, 63610, 63612 have `tfc > 0` with `i8 == 1`, so this fires on 4 real models.

LIVE DEFECT 3 - FACE SKIN TRUNCATION. `FaceSkin` is `sbyte[]` (`ModelDefinition.cs:61`) assigned `(sbyte) st5.ReadUnsignedByte()` (`:410`). The client keeps it as `int[] anIntArray1395` from `readUnsignedByte()` (`Model.java:596`). Measured: of the 21,439 models with per-face skins, **8,639 contain a value above 127**, which our decoder turns negative.

DEAD BRANCH - DO NOT INVEST IN IT. `GetModelFormat` (`ModelDefinition.cs:186`) routes an `FF FD` tail to the Newest decoder. The client has no such branch - `Model.java:96-101` tests only `FF FF` and otherwise falls to legacy - and **no model in this cache ends `FF FD`**. So `DecodeRS3`'s non-newProtocol arm is unreachable, which is also why its `FormatType = 13` default (`:833`) has never mattered even though the client's default is 12 (`Model.java:30`). Fixing bugs in that arm buys nothing; deleting the sentinel would match the client.

NON-CANONICAL HAZARDS FOR THE ENCODER (assume all of these until a byte-identity sweep says otherwise):
- **Strip opcodes.** Any face can legally be written as opcode 1 (three fresh smart deltas) instead of 2/3/4. Re-deriving the opcode stream from the decoded triangles will produce a different, equally valid file. Preserve the opcode bytes.
- **Smart width.** `method1239` (our `ReadSmart`) encodes -64..63 in one byte and -16384..16383 in two, and the ranges overlap - the same delta has two encodings. Record which width was used. (Corrected: this row said -49152..16383, which is unreachable. The two-byte branch is only taken when the leading byte has bit 7 set, so the biased `u16` is confined to `0x8000..0xFFFF`. `JagStream.WriteSmart` rejects below -16384 for that reason.)
- **The block lengths are declared, not derived.** i_67_..i_71_ / i_10_..i_14_ come from the footer. Recomputing them from re-derived data rather than carrying them forward will change the file even when nothing was edited.
- **The formatType flag bit is independent of the value.** Bit 3 clear implies formatType 12, but a model may set bit 3 and store 12. Preserve the bit; never recompute it from `FormatType == 12`.
- **Two separate textured-face vertex blocks.** Type-0 verts sit at i_92_, types 1-3 at i_93_ (`Model.java:674-679`), with UVs, alpha, colour and layer/scale in four further blocks. Both block order and the `aByteArray1388` type order must survive a round trip. (Our type-0 read at `ModelDefinition.cs:458-469` is correct even for mixed models, because the client also reads type 0 sequentially from its own block - that is not the bug here; the bug is that types 1-3 are dropped.)

OTHER THINGS WORTH KNOWING:
- **No XTEA on this index**, established from the data: all 63,614 groups inflate as plain gzip with no key.
- **Dependencies.** `FaceTextures` ids resolve against index 9/26. Item, NPC and object definitions recolour models (`Editor.cs:1284-1349`), so a model edit is visible from three other tabs.
- **Cost.** A full decode sweep of this index inflates 260 MB across 63,614 archives, every one GZip. CLAUDE.md's note about `MemoryUtils` being dead and `RSArchive.Decode` allocating per chunk lands hardest here - this is the index that makes a full sweep expensive.
- **Nothing in the suite covers the renderer** (CLAUDE.md), and the model viewer is the renderer. `ModelRenderer.cs:68` gates on render type 2 correctly today; a regression there passes every test.
