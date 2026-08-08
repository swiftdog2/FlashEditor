# Index 9 - TEXTURES

**Format:** partially-understood  
**Capability:** read-only  
**Effort:** large

## What it is

946 procedural texture graphs, one per group, group id == texture id. Verified directly against the cache: idx255 record 9 decodes to format 6, table version 443, flags 0x00, 946 groups with contiguous ids 0..945, and every single one holds exactly 1 file (file id 0), plus 3784 trailing zero bytes (4 per file). So a "group" is one whole texture and its only "file" is the whole graph blob; there is no sub-file structure to speak of. Compression is mixed: 507 groups stored uncompressed, 439 GZip; all 946 carry a 2-byte version trailer; none are XTEA encrypted.

The client settles the addressing. Index 9 is opened at InterfaceSettings.java:166 (`RSBuffer.aJS5Archive_3995 = openFileStore(-121, false, 1, 9)`) and handed to Class260 as the middle of three archives at InterfaceSettings.java:244 - `new Class260(idx26, idx9, idx8)`. Class260.method3206 (Class260.java:285) reads it with `aJS5Archive_3258.method2733(i, -5)`, and JS5Archive.method2733 (JS5Archive.java:591-612) resolves that as: if the table declares one group, treat i as a file in group 0; otherwise i is the group id and it must declare exactly 1 file, then read file 0. This cache is the second case.

One record is a Node_Sub46_Sub19 - a DAG of operation nodes. Node_Sub46_Sub19.java:69-116 is the whole format: a node-count byte; then per node a 4-byte header (a discarded version byte, the node type, an output-size byte `anInt3860`, and an opcode count) via Node_Sub46_Sub11.method1581:19-42, then that many type-specific opcode records, then exactly childCount child-index bytes where childCount is fixed per node type; then three trailing bytes naming the colour, alpha and brightness output nodes. There are exactly 40 node types, 0..39 - counted from the 40 distinct `new Node_Sub10_Sub*` constructions in the factory PlayerAppearance.method3630 (PlayerAppearance.java:113ff).

Index 9 is NOT the texture metadata. The 20-field columnar material table (Class260's constructor, Class260.java:106-211) lives in index 26 and declares 1,408 textures, of which only these 946 have graphs.

## Current capability

Decode and display, at high fidelity. No encode, no write path, no editing.

DECODER: `Texture.Decode` (FlashEditor/Definitions/Sprites/Texture.cs:311-410) mirrors Node_Sub46_Sub19's constructor exactly - node count, 4-byte node header, opcode loop, child-index bytes, three output indices. All 40 node types have opcode arms (Texture.cs:416-718). It carries the client's per-type child counts (Texture.cs:43-48), monochrome defaults (:53-62) and monochrome-override opcodes (:67-76), the node field initialisers the client's classes declare (`InitNodeDefaults`, :87-130), and the client's `method1001` post-decode hooks (`PostInitNode`, :140-148, building the type 8 transfer curve, type 15 Worley tables and type 34 fractal ladder).

PROOF IT DECODES CORRECTLY: `TextureGraphConformanceTests.EveryTextureGraph_ConsumesItsFileExactlyBarTheTrailer` (FlashEditor.Tests/Definitions/TextureGraphConformanceTests.cs:96-112) sweeps every group in the index and asserts the read head lands exactly 10 bytes from the end of every file. `NoTextureGraph_ContainsAnUnhandledOpcode` (:124-138) asserts the opcode tables have no gaps across the whole index. Both are real full-index sweeps, not samples.

LOADER: `TextureManager.LoadFromTextureIndex` (Definitions/Sprites/TextureManager.cs:77-139) walks the reference table, and picks textureId = archiveId for the single-file case at :111 - which is what the client does.

EVALUATOR: `TextureGraphEvaluator` (2150 lines) renders the graphs - a mono dispatch covering ~33 types (TextureGraphEvaluator.cs:530-564) and a colour dispatch covering ~20 (:580-601). Corroborated against something independent by `DeclaredTextureColour_MatchesWhatTheGraphRenders` (TextureGraphConformanceTests.cs:221-270), which scores every rendered graph's mean colour against the index-26 declared colour with a shuffled control.

GUI: a read-only gallery. `TextureViewerTab` (Editor.Designer.cs:1299-1312) holds `TextureListView` with exactly two columns, `TextureImage` and `TextureID` (:1374), plus a progress bar and a status label. Loaded async on a BackgroundWorker at Editor.cs:796-849. Its only context-menu item is a literal placeholder - `new ToolStripMenuItem("Dummy Action")` wired to `DummyMethod()` at Editor.cs:109-111.

ENCODER: none. There is no `Texture.Encode`, no `TextureGraph` serialiser, and no `RSCache.WriteFile` call for index 9 anywhere - the only WriteFile call sites in the whole project are items, objects, NPCs (Editor.cs:944, 966, 986) and maps (Cache/Region/MapSquareLoader.cs:176, 184). `TextureManager.EncodeColumnar` (TextureManager.cs:260) exists but encodes index 26, not index 9, and has no production caller at all - only tests.

## Gaps

- An encoder. Nothing exists: no `Texture.Encode`, no TextureGraph serialiser. It must re-emit node count, per node the version byte, type, output-size byte, opcode count, every opcode record, then childCount child bytes, then the three output indices, then the 10-byte trailer verbatim.
- Decode must first start recording what it currently throws away, or byte identity is unreachable. Discarded today: the per-node version byte (Texture.cs:325), the output-size byte `anInt3860` (:329), the opcode order and the set of opcodes actually present, the type 29 shape-record payloads (skipped blind at :629-632), and the 10 trailing bytes (never read at all). The cheapest correct design is to capture each opcode's raw byte span at decode and have the encoder replay it, editing only the spans the user touched.
- Undo the destructive post-decode hooks before encoding. `InitFractalNoise` overwrites `node.IntParam1` with the trimmed octave count (Texture.cs:305) - the value opcode 1 wrote is gone, so encoding IntParam1 back writes a different file. `InitCurveTransfer` substitutes an identity ramp when there are no markers (:165-166). Both need the as-read value kept alongside the derived one.
- A full-index byte-identity sweep over all 946 groups, in the shape of the existing definition sweeps. `TextureGraphConformanceTests` proves consumption, not identity; nothing in the suite re-encodes an index 9 file.
- A GUI editing surface. The Textures tab is image + id only (Editor.Designer.cs:1374) and its context menu is a `Dummy Action` stub (Editor.cs:109-111). A graph editor needs a node inspector, and at minimum the sprite-id field on type 18/39 nodes and the nested-texture id on type 36 nodes.
- A write path: an `RSCache.WriteFile(RSConstants.TEXTURES, textureId, 0, ...)` call plus the reference-table CRC and version update that comes with it. No code currently writes to index 9.

## Notes and traps

TRAPS, in the order they will bite.

1. THE 10-BYTE TRAILER IS DATA, NOT PADDING, AND IT IS UNDECODED. The 637 client stops reading immediately after the three output-node bytes (Node_Sub46_Sub19.java:111-114), so these are 639-era bytes it never saw - the usual cache-ahead-of-client situation. They are not constant. I measured a per-position histogram across all 946 payloads: pos0 = 0 x900 / 1 x46; pos1 = 0 x931 / 1 x15; pos2 = 1 x905 / 0 x41; pos3 = 1 x906 / 0 x40; pos4 = 0 x944 / 3 x2; pos5 mostly 0 with 255/254/253 outliers (reads like a signed byte); pos6 mostly 0 with 254/250/2/1/255; pos7 = 34 (0x22) x939, then 0 x5, 2 x1, 32 x1; pos8 = 0 x744 / 1 x127 / 2 x46 / 3 x13 / 5 x6 / 4 x4; pos9 = 0 x890 / 1 x56. Field meanings are UNKNOWN - the client cannot answer and I will not guess. Copy them verbatim; do not synthesise them.

2. NON-CANONICAL ENCODING IS EVERYWHERE HERE, exactly as CLAUDE.md warns. Aliased opcodes: type 15 opcode 0 sets IntParam0 AND IntParam1 together while opcodes 5 and 6 set them separately (Texture.cs:535, 540-541); type 34 opcode 3 sets IntParam3 and IntParam4 together while 5 and 6 set them separately (:687, 689-690). Two different byte encodings, one decoded state. Opcode ORDER within a node is free - the decoder is a `for` loop (:333). Opcode REPETITION is expressible and would be lost. And ABSENT-VERSUS-DEFAULT is acute, because `InitNodeDefaults` (:87-130) seeds real values (type 0 IntParam0 = 4096, type 7 BlendMode = 6, type 34's five defaults, type 30's 1024/3072, type 6's 4096, type 25's 409/4096x3, type 15's 5/5/-/2048/2/1) - a node whose decoded value equals its default may or may not have carried the opcode, and nothing records which.

3. TYPE 12'S UNHANDLED OPCODES ARE DELIBERATELY SWALLOWED. Texture.cs returns true for every opcode on node type 12, because the client's own Node_Sub10_Sub30 has arms for 0, 1 and 3 only. They consume nothing, so consumption still balances, but nothing records that they were present - an encoder built from decoded state alone drops them and shortens exactly those files.

   CORRECTED 2026-08-08: this line previously said the swallowed opcodes are "2 and 4", and that was wrong. Measured by the opcode census in RealCacheTextureGraphTests, identically in the vanilla b639 capture and in the repack, the set is 2, 4, 5 AND 6, two graphs each. The wrong figure had also been copied into the decoder's own comment. Do not restate the set from this document: the census prints it, and a count that is not measured is the failure this file has now produced three times.

4. DEPENDENCIES ON THREE OTHER INDEXES. Index 26 supplies the texture roster and the `field1824` transpose flag the renderer needs; index 8 supplies the sprites that type 18 and 39 nodes name; and index 9 references ITSELF through type 36 nodes, whose target must exist (pinned by `EveryComposedTextureReference_ResolvesToALoadedTexture`, TextureGraphConformanceTests.cs:143-174). Editing a graph can therefore break another texture. Composed graphs are also shared mutable state during evaluation - `ComposedTextures_RenderIdenticallyUnderConcurrency` (:355-388) exists because the editor renders on 20 threads.

5. COMPRESSION IS MIXED, SO COMPARE PAYLOADS NOT CONTAINERS. 507 of the 946 groups are stored uncompressed and 439 are GZip. AGENTS.md's measurement stands: 0 of 96,183 GZip containers re-encode byte-identically. A byte-identity sweep here must compare the decompressed graph bytes.

6. NO XTEA, NO NAME HASHES. Index 9's table flags byte is 0x00 (verified), so no identifiers, no whirlpool, no sizes - a texture is addressable by id only, and there is no name to recover. The table also carries 3784 trailing zero bytes past its last field, so any parser must tolerate a tail rather than assert exact consumption (pinned at FlashEditor.Tests/Cache/RealCacheReferenceTableShapeTests.cs:186, :259).

7. THE RENDERER IS UNTESTED BY CONSTRUCTION, and one part of it is openly approximate: the gradient preset tables at TextureGraphEvaluator.cs:1103-1160 are hand-transcribed from Node_Sub10_Sub33.method1100 with editorial labels ("Warm earth tones", "Full spectrum rainbow"). That is a render-fidelity concern only - it cannot affect a codec round trip - but do not read those labels as evidence of anything, per CLAUDE.md's rule that a semantic name in reference/ is a claim.

8. `TextureLoader.cs` is an 8-line empty placeholder class. Do not mistake it for a gap; the loading lives in TextureManager.

9. The `TEXTURES` constant is one of the few that names its index correctly - the misnamed ones are 15, 17, 24, 25 and 36. RSConstants.cs:24.
