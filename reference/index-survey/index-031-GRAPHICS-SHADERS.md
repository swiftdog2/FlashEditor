# Index 31 - GRAPHICS_SHADERS

**Format:** fully-understood  
**Capability:** read-write with tests, and a tab  
**Effort:** small

> **Built 2026-08-16.** The Gaps section below is now history and is kept for its evidence. What
> landed: `ShaderNames` commits the two backend names and the seven program names, all of them
> taken from the client rather than from a wordlist; `ShaderProgramShape` classifies a payload from
> its own bytes; `ShaderTextDocument` is the line-ending-exact text codec; and the Graphics Shaders
> tab edits `gl` as text and shows `dx` as hex, over `CachePayloadTransfer`.
>
> **Trap 1 below is the whole design and it is now enforced rather than merely warned about.**
> `ShaderTextDocument` records the convention at decode and replays it at encode, and checks that
> claim rather than asserting it: `RoundTripsExactly` is computed at decode by encoding the display
> text back and comparing it to the stored bytes, and a file that fails - a binary payload, or one
> mixing conventions - is shown but not editable. `RealCacheShaderTests` then asserts what a
> decoder hardcoding either convention cannot satisfy: both LF and CRLF occur, both
> trailing-newline states occur, no file mixes them, and every plaintext file re-encodes to its
> stored bytes.
>
> **The per-file name lookup gap is closed generally, not here.** `CacheNameIndex` resolves both
> levels for any table that carries identifiers, so `"gl"/"transparent_water"` now resolves through
> `RSCache.ReadFileBytes(31, "gl", "transparent_water")` and the same machinery is waiting for
> indexes 3, 5, 23, 32 and 33.

## What it is

Two GPU shader sets, one per rendering backend, addressed by name rather than by id. Measured directly out of C:\Users\CJ\Desktop\FlashEditor\cache: main_file_cache.idx31 is 24 bytes (4 slots), but only slots 1 and 3 hold data - slot 0 is `FF 00 00 00 00 00` and slot 2 is all zero, both with sector 0. idx255 group 31 decodes to format 6, version 3, flags 0x01 (identifiers only), 2 groups: id 1 with identifier 3301 and id 3 with identifier 3220. Java `String.hashCode` of "gl" is 3301 and of "dx" is 3220 (computed; matches AGENTS.md:349-352). Each group holds 7 files, ids 0-6, and BOTH groups carry the identical 7 file-name hashes. All 7 resolve exactly against the 7 shader names the 637 client asks for by name, with nothing left over: 530103708="uw_ground_lit", 2453192682="transparent_water", 3924670244="uw_model_lit", 635274923="uw_model_unlit", 2631978787="uw_ground_unlit", 2194791646="environment_mapped_water_f", 2194791662="environment_mapped_water_v". So: a GROUP is a renderer backend; a FILE is one named shader program; a RECORD is the whole file - an opaque blob with no internal opcode structure. Group 1 ("gl") is 14,452 bytes of payload: files 0-4 are plaintext ARB vertex programs starting `!!ARBvp1.0`, files 5-6 are plaintext GLSL (`varying vec3 wvVertex;` / `uniform float time;`). Group 3 ("dx") is 6,629 bytes: seven compiled Direct3D 9 shader blobs, each opening with a version token then a `CTAB` constant-table block and ending `FF FF 00 00` - six are `01 01 FE FF` (vs_1_1) and file 5, environment_mapped_water_f, is `00 02 FF FF` (ps_2_0). Both groups are GZip, single-chunk, 7 files, 2-byte version trailer, unencrypted.

## Current capability

Read and write both work today through the generic path; there is nothing index-31-specific anywhere, and no GUI. Evidence. (1) The only mentions of the constant in the whole repo are its declaration and its name string: FlashEditor\Cache\RSConstants.cs:46 and :96. Zero adoption sites - `RSConstants.GRAPHICS_SHADERS` is never passed to anything. (2) The three .cs files matching /shader/ are FlashEditor\Editor.cs, Definitions\ModelDefinition.cs and Cache\RSConstants.cs; Editor.cs:118-189 is the editor's OWN OpenGL viewer compiling `Shaders/texture.vert` off disk, unrelated to index 31. (3) The editor never opens the index: Editor.cs:64-76 `editorTypes` lists only 19, 8, 18, 16, 3, 7, 9, 5, 6. The only GUI surface is the generic "Reference Tables" tab (Editor.Designer.cs:289-298), which shows index 31's table as one metadata row because RSCache.LoadReferenceTables (RSCache.cs:542-555) loads every index - format, version, flags, archive count, no content. (4) Reading works: `RSCache.ReadFileBytes(31, 1, 6)` (RSCache.cs:783) returns the GLSL vertex shader verbatim. Naming a group works: `RSReferenceTable.GetArchiveId("gl")` (RSReferenceTable.cs:71-76 via NameHasher.GetNameHash) returns 1, because ReferenceTableCodec.cs:53-65 indexes `identifiersTmp` by archive key, not by ordinal. Naming a FILE does not work - per-file identifiers are read and stored (ReferenceTableCodec.cs:141-152) but no per-group RSIdentifiers is ever built from them. (5) Writing works: `RSCache.WriteFile` (RSCache.cs:102) gates only META_INDEX and is otherwise index-agnostic. (6) Byte identity IS proven over 100% of this index on every run. RealCacheFixture.ArchivesToExamine (RealCacheFixture.cs:122-134) returns all archives when the count is <= 250, and index 31 has 2, so sampled and full runs are identical here. All six sweeps in RealCacheConformanceTests.cs iterate `_cache.TableIndexes`, which includes 31: ReferenceTables_ReEncodeToTheCapturedBytes (:59), ArchiveCrcs_MatchTheCapturedContainerBytes (:119), Containers_PreserveTheirPayloadAndHeaderAcrossReEncode (:169), Archives_ReEncodeToTheCapturedPayloadBytes (:218), UnchangedArchives_SurviveTheEditPathWithTheirPayloadIntact (:295, which requires >= 2 files - both shader groups have 7), and IndexRecords_ReEncodeToTheCapturedBytes (:479, all 4 idx31 slots). Nothing in reference\ mentions index 31, and the bundled Hydra spec calls it "Unknown" (HYDRA_CACHE_SPEC.md:488).

## Gaps

- A shader-aware type. There is no `ShaderProgram`/`ShaderSet` class anywhere. Because a file is an opaque blob, Decode/Encode are byte pass-through plus classification: backend from the group identifier (gl/dx), name from the file identifier, and kind from the leading bytes (`!!ARBvp1.0` = ARB assembly, neither of the two = GLSL, `01 01 FE FF`/`00 02 FF FF` = D3D9 vs_1_1/ps_2_0).
- A file-name lookup. `RSIdentifiers` is only ever built for GROUP identifiers (ReferenceTableCodec.cs:65). Per-file name hashes are stored on `RSFileEntry` (ReferenceTableCodec.cs:150) but never indexed, so `"gl"/"environment_mapped_water_v"` - which is exactly how the client addresses it - cannot be resolved today. Needs either a per-group RSIdentifiers or a small `NameHasher`-keyed dictionary of the 7 known names.
- A codec test against captured bytes. Nothing asserts the name join. The test that would prove it: all 14 stored file identifiers resolve against the 7 client-sourced names, in both groups, with zero unmatched on either side. That join is self-proving in the sense CLAUDE.md demands - 7 of 7 exact hash equality, not coverage.
- A content-level conformance test. Assert the gl group's 7 files are ASCII, 5 start `!!ARBvp1.0`, 2 are GLSL; assert the dx group's 7 all carry a valid D3D9 version token, a `CTAB` block and the `FF FF` end token. That pins the format claim against the bytes rather than against the client alone.
- A GUI tab. Nothing exists. Following the existing pattern it needs a `TabPage` in Editor.Designer.cs alongside TextureViewerTab/MapEditorTab (:143-148), an entry in `editorTypes` (Editor.cs:64-76), and a bound panel like `TrackEditorPanel` - a text editor for the gl side, a hex/annotated view for the dx side, saving through `RSCache.WriteFile(31, group, file, ...)`.
- An index-31-specific byte-identity sweep. Arguably redundant - the generic sweeps already cover both groups fully on every run - but a named one would fail loudly if a shader tab ever normalises the payload rather than passing it through.

## Notes and traps

Client trail, followed end to end. InterfaceSettings.java:187 `Class212.aJS5Archive_1603 = Class42_Sub3.openFileStore(-108, true, 1, 31)` - fileType 1, the ordinary JS5 path (only index 36 uses fileType 2). That archive reaches exactly one place: `RenderType.getRenderTypeProvider` (RenderType.java:17-34) passes it to `Class214.getDXRenderType` on renderTypeId 3 and `Class60.getOpenGLRenderType` on renderTypeId 5, and to nothing on the software paths (cases 0/1/2 drop it). It is stored as `RenderType_Sub3.aJS5Archive_4535` (:517) and consumed by the water/underwater material classes, which fetch by name: Class76_Sub9.java:102,105 and Class76_Sub2.java:122 and Class76_Sub8.java:62-68 ask for group "gl"; Class76_Sub1.java:34,36 and Class76_Sub3.java:25 and Class76_Sub6.java:34-40 ask for group "dx". `JS5Archive.method2739(group, file, -32734)` (JS5Archive.java:709-737) lower-cases both, hashes each with `Class305.method3580`, resolves group then file through the identifier tables and returns `getChildFromFolder` - the raw bytes, unparsed. Class76_Sub1.java:33-36 hands those bytes straight to `IDirect3DDevice.a()`/`.b()`, i.e. CreateVertexShader/CreatePixelShader, which settles that the dx files are compiled D3D bytecode and not source.

TRAPS.

1. LINE ENDINGS ARE NOT UNIFORM, and this is the trap that will break a byte-identity sweep. Measured across the gl group: uw_ground_lit, uw_model_lit, uw_model_unlit and uw_ground_unlit use 68/67/52/53 BARE LF and zero CRLF; transparent_water uses 36 CRLF and zero bare LF; both GLSL files use CRLF only (46 and 23). Only transparent_water ends with a newline (last byte 0A); the four ARB files end on `D` of "END" and the GLSL files on `}`. Any text control or `File.WriteAllText` round trip that normalises newlines or appends a trailing newline silently rewrites the file. Treat shader text as bytes; decode to string for display only, and never write back a re-serialised string.

2. Group ids are 1 and 3, NOT 0 and 1. Anything that enumerates `0..GetArchiveCount()` reads the wrong groups. The 4-versus-2 discrepancy between the task's cache facts and AGENTS.md:305 is exactly this: the idx file must be long enough to hold slot 3, so it declares 4 slots for 2 real groups.

3. idx31 slot 0 is `FF 00 00 00 00 00` - a declared length of 16,711,680 with sector 0. It is not a group (the reference table does not list it) and RSIndex round-trips it verbatim (RSIndex.cs:29-43), but a reader that trusts the length field before checking the sector will try to allocate 16 MB. Slot 2 is clean zeros, so this is one bad slot, not a pattern.

4. The dx files are only editable in the sense that they can be replaced. Producing new D3D9 bytecode needs an external HLSL compiler; there is no in-tree path and none should be invented. The gl files are genuinely editable text.

5. No XTEA, no unusual compression, no dependency on another index. Both containers are GZip with a 2-byte version trailer and no key. The reference table sets identifiers (0x01) only - no whirlpool, no sizes, no format 7 - consistent with AGENTS.md:86-90.

6. Non-canonicality has not been ruled out at the archive level, but there is nothing for it to bite: both groups are chunks=1 with a 7-entry size table, and `Archives_ReEncodeToTheCapturedPayloadBytes` already passes over them, so the split is reproduced exactly. The risk lives entirely in item 1 above.

7. 637-versus-639: no divergence found. The 7 names the 637 client requests are precisely the 7 file identifiers present in the 639 cache, in both groups. The client asks for nothing this cache lacks and the cache holds nothing the client does not ask for.
