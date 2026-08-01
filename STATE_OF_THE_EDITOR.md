# FlashEditor - State of the Cache Editor

Assessment date: 2026-07-31. Commit assessed: `45bc52b` (working tree clean).

Benchmark used: a *full comprehensive JS5 cache editor* - every index enumerable and
editable, encode and decode symmetric and lossless for every content type, safe
transactional writes, and accurate documentation.

---

## 1. Verdict

> **Update (2026-07-31, after this report was first written):** the build has since been
> repaired - see section 2. The solution now builds with 0 errors and all 45 tests pass.
> Everything below section 2 still stands.

| Dimension | State | Score vs benchmark |
|---|---|---|
| Compiles | **Yes** (was broken; fixed 2026-07-31) | 100% |
| Cache read (containers, sectors, ref tables) | Solid, a few off-by-ones | ~80% |
| Cache write (round trip to disk) | Present but **unsafe and mostly wrong** | ~20% |
| Index coverage (viewer) | 7 of 37 indexes | ~19% |
| Index coverage (editor) | 1 of 37 indexes (items), partially wired | ~3% |
| Encode/decode symmetry | 2 of ~9 content types round trip | ~20% |
| Test coverage | ~52 cases, none covering the write path | ~15% |
| Documentation | Best doc is 9 months stale, README factually wrong | ~30% |

**Overall: this is a partially working cache *viewer* with an unsafe, incomplete
write path bolted on. It is not an editor yet, and at HEAD it does not build.**

Roughly **15-20%** of the way to the benchmark. The rendering/texture work is the
strongest part of the codebase and is where all recent effort has gone; the cache
mutation layer - the thing a cache *editor* exists to do - is the weakest.

---

## 2. Build status: was broken, now fixed

The build failed for two independent reasons. Both are resolved.

### 2a. NuGet restore failed (NU1605)

`FlashEditor.csproj` - a `WinExe` - carried the test packages `xunit 2.9.3`,
`xunit.runner.visualstudio 3.1.5`, `Microsoft.NET.Test.Sdk 18.8.1` and `Moq`. Those flow
transitively through the project reference into `FlashEditor.Tests`, which pinned lower
versions (`xunit 2.4.2`, `Test.Sdk 17.14.1`, `System.Resources.Extensions 9.0.6`),
producing three `NU1605` package-downgrade errors. Restore aborted before the compiler
ever ran, which is why the missing type below was not the first thing reported.

Fix: removed the test packages from the app project; aligned the test project to the same
versions; dropped `BouncyCastle.NetCore 2.2.1`, `SharpZipLib`, `Moq` and
`Microsoft.NETFramework.ReferenceAssemblies.net472` from the test project (verified unused
by grep - and the BouncyCastle entry was a second, conflicting copy of the one the app
gets from `BouncyCastle.Cryptography`). Also corrected the test project's `LangVersion`
comment, which claimed it targeted .NET Framework.

### 2b. `XTEAKeyTable` did not exist

`RSCache.cs` referenced a type that was never committed - 4 usages, 0 definitions
(`:57`, `:603`, `:611`, `:619`). `git log --all` confirms the file was **never added to
git in any branch**; the consuming code arrived in `cc81561` without it. So the last five
commits were pushed without a successful build.

Fix: implemented `FlashEditor/Cache/Util/Crypto/XTEAKeyTable.cs` to the contract the call
sites require (`LoadFromFile`, `FindKeyFile`, `GetKey(indexId, archiveId)`, `Count`). It
parses the common JSON key-dump shapes, treats an all-zero key as "not encrypted", and
degrades to an empty table on a malformed file rather than blocking cache load. **This is
a reconstruction from the call sites, not a recovery of the original** - the key-file
format it accepts is an assumption and needs checking against a real key dump.

### Current state

```
Build succeeded.  0 Error(s), ~201 Warning(s)
Passed! - Failed: 0, Passed: 45, Skipped: 0, Total: 45
```

Warnings are overwhelmingly nullable-annotation noise (`CS8618` uninitialised
non-nullable field, `CS8625`, `CS8602`) from `<Nullable>enable</Nullable>` on a codebase
not written for it. Three are substantive and corroborate findings elsewhere in this
report:

- `Region.cs(44,44)` **unreachable code** - the `break`-inside-`switch` infinite loop (s.4)
- `Track.cs(235,48)` **unreachable code** - the `goto label361` control-flow bug (s.4)
- `JagStream.cs(278,20)` `CS0675` bitwise-or on a sign-extended operand - a likely
  read bug in the stream primitives, not previously flagged
- `ReferenceTableCodec.cs(79,40)` `CA2014` `stackalloc` inside a loop - stack overflow risk

---

## 3. Index coverage

37 indexes are named in `RSConstants.cs:54-90`. Reference-table *metadata* is loaded for
almost all of them at cache open (`RSCache.cs:65,350-363`). Actual content handling:

| Idx | Name | Content decoded | Viewer | Editable in UI | Writes back |
|---|---|---|---|---|---|
| 0 | Frames | - | - | - | - |
| 1 | Skins | - | - | - | - |
| 2 | Config | - | - | - | - |
| 3 | Interfaces | - | **empty tab** | - | - |
| 4 | Sound effects | - | - | - | - |
| 5 | Maps | decoder exists, unreachable | - | - | - |
| 6 | Music | decoder exists, unreachable | - | - | - |
| 7 | Models | yes | **yes (OpenGL)** | - | encode throws |
| 8 | Sprites | yes | **yes** | - | encode throws |
| 9 | Textures | yes | **yes** | - | encoder has no caller |
| 10-15 | Huffman, music2, cs2, fonts, sfx | - | - | - | - |
| 16 | Objects | yes | **yes** | handler exists, never enabled | unreachable |
| 17 | CS2 settings | - | - | - | - |
| 18 | NPCs | yes | **yes** | handler exists, never wired | unreachable |
| 19 | Items | yes | **yes** | **yes** | **yes (see s.5)** |
| 20-25 | Anims, gfx, worldmap, quickchat | - | - | - | - |
| 26 | Materials | yes (feeds textures) | via textures tab | - | - |
| 27-35 | Particles, defaults, billboards, shaders, tips, cutscenes | - | - | - | - |
| 36 | Vorbis | **unreachable** (off-by-one, below) | - | - | - |
| 255 | Meta | yes | **yes** | read-only by design | explicitly blocked |

**Off-by-one:** `RSFileStore.GetIndexCount()` returns the *highest index id*, not a count
(`RSFileStore.cs:54-69`, its own doc comment admits this). `RSCache.LoadReferenceTables`
loops `indexId < GetIndexCount()`, so the highest index present on disk is never loaded
and `GetReferenceTable(36)` throws.

**Missing entirely:** no interface (idx 3) decoder, no CS2 script (idx 12) decoder, no
animation/frame (idx 0/1/20) decoder, no sound/MIDI wiring, no world map, no enums,
no structs, no quick chat, no particles. For a comprehensive editor these are ~30
missing content codecs.

---

## 4. Encode / decode symmetry

| Type | Decode | Encode | Round-trip verdict |
|---|---|---|---|
| `ObjectDefinition` | complete-ish | yes, driven by `decoded[]` hit map | **Best in repo.** Byte-exact for decoded defs. UI-editable `walkable` is never encoded (`ObjectDefinition.cs:610-611`) |
| `TextureDefinition` | yes (columnar) | yes | **Broken by short-circuit** - `TextureManager.cs:260-270` returns `RawIndexData` verbatim whenever present, so field edits are silently discarded |
| `ItemDefinition` | yes, throws on unknown opcode (`:338`) | yes | Lossy - opcodes 1/4/5/6/12 emitted unconditionally; `equipSlotId`/`equipId` are grid-editable but never encoded |
| `NPCDefinition` | yes, **silently ignores unknown opcodes without skipping payload** (`:525`) | yes | **Effectively broken.** ~7 unconditional `.Length` reads on nullable arrays (`:544,564,572,580,590,614,708`) throw NRE for the common NPC. ~25 opcodes emitted unconditionally |
| `ModelDefinition` | 3 formats (Old / RS2 / "newest") | **none** - `:748` throws | No write path. Textured triangle types 1-3 not decoded (`:467`); Animaya/particle blocks never parsed |
| `SpriteDefinition` | yes | **none** - `:246` `NotImplementedException` | Read-only |
| `Texture` (procedural graph) | node types 0-39, 2 types bail the whole graph | **none** | Read-only. Alpha output hard-disabled (`TextureGraphEvaluator.cs:145`); 5 colour node types fall through to passthrough |
| `Track` (MIDI) | partial, buggy | none | Dead code, never referenced. Control-flow bug at `Track.cs:259` restarts the loop |
| `Region` / map | partial | none | Dead code, never referenced. `LoadTerrain` has a Java-port `break`-in-`switch` bug (`Region.cs:45-67`) making it an infinite loop |

**Reference table codec** (`ReferenceTableCodec.cs`) - was the most consequential
asymmetry. **Fixed 2026-08-02**, each fix pinned by a round-trip test in `CodecTests`:

- ~~Decodes formats 5/6/7; **encodes 5/6 only**. The per-archive flags byte read at
  `:99-107` for format 7 is never written back.~~ Fixed - `Encode` now emits the flags
  byte for format 7+, between the version block and the file counts where `Decode`
  expects it. Omitting it shifted every following field, and `RSCache.WriteFile`
  re-encodes the table on **every edit**.
- ~~File IDs are flattened to `0..n-1` on encode.~~ Fixed - the delta now runs over
  `GetFileEntries().Keys`, so sparse file IDs survive.
- ~~Entry hashes always encode as 0 (`CalculateHash()` over a stream that is never
  populated).~~ Fixed - `Encode` writes back the decoded `GetHash()` value.
- Also found and fixed while in the same seam: with `FLAG_IDENTIFIERS` set, `Decode` read
  the per-file name hash into `SetHash` while `Encode` wrote it from `GetIdentifier`, so
  every file name was lost on the first save. Both sides now use the identifier field,
  matching how the archive-level identifier is already handled.
- Still open: `FLAG_SIZES` values are never recomputed after an edit - they go stale
  immediately. That belongs to `RSCache.WriteFile`, not the codec; tracked as s.9 item 7c.
- ~~Still open: the sparse-file-ID fix above is defeated downstream by `RSCache.WriteFile`.~~
  Fixed 2026-08-02 - `WriteFile` now reconciles the archive and its entry over actual file
  ids and rehydrates the archive before editing it, so sparse ids survive a save and reopen.
  See s.9 item 7a.
- Still open: the format-7 flags byte round trips bit 0 only. Tracked as s.9 item 7b.

**Archive codec:** ~~`RSArchive.Decode` special-cases a 1-file archive by taking the whole
buffer including the trailing chunk byte (`:41-53`), while `Encode` always writes that
byte (`:191`). Every save cycle grows a single-file archive by one byte - which affects
models, sprites and textures, i.e. most of the cache.~~ **Fixed 2026-08-02.** `Decode` is
the correct side: the client's own unpacker special-cases a file count of 1 and takes the
payload verbatim, so a single-file archive has no trailer at all. `Encode` now suppresses
both the size table and the chunk-count byte for that case. `Decode` also no longer reads
the last *payload* byte as a chunk count in the single-file path - that left a bogus count
behind which corrupted the size table if a second file was later added to the archive.

**Compression:** none/BZip2/GZip supported both ways. **LZMA (type 3) is unsupported** -
`RSContainer.cs:98` throws. **XTEA** is a real, correct implementation wired into both
container paths, but the key source is the missing `XTEAKeyTable`, so no key can ever
load, and `RSCache.WriteFile:166` calls `container.Encode()` with no key - editing an
encrypted archive would write it back in cleartext.

---

## 5. The write path is unsafe

This is the most serious functional problem after the build failure.

A single item cell edit fires immediately, with no confirmation:

```
ItemListView CellEditFinished        Editor.cs:831
  -> cache.WriteFile(...)            Editor.cs:861
  -> RSFileStore.Write               RSFileStore.cs:100
     -> idx record                   RSFileStore.cs:133-134  (in-memory only)
     -> dataChannel.WriteBytes       RSFileStore.cs:189      (hits disk immediately)
```

`dataChannel` is a `MemoryMappedFile` opened read-write over **the loaded cache's own
`main_file_cache.dat2`** (`RSFileStore.cs:21`, `MappedDataChannel.cs:20-37`). So:

1. **The user's source cache is mutated in place** - no backup, no prompt, no undo.
2. **Only the dat2 is written; the `.idx` files stay in RAM.** The on-disk cache is left
   internally inconsistent the moment you edit anything.
3. The explicit `Cache -> Save All` that would flush the idx files writes to
   `RSConstants.CACHE_OUTPUT_DIRECTORY` = `"C:/Users/CJ/Desktop/RSPS/Hydra/cache2/"`
   (`RSConstants.cs:118`), hardcoded to the original author's machine, ignoring the
   directory the user picked. It creates no directory and has no try/catch
   (`Editor.cs:962-965`), so on any other machine it throws an unhandled
   `DirectoryNotFoundException` and crashes before writing anything.

All three hardcoded paths (`CACHE_DIRECTORY`, `CACHE_OUTPUT_DIRECTORY`,
`CACHE_ORIGINAL_COPY`, `RSConstants.cs:117-119`) also drive Export-to-.dat, Compare-to-
Output, and the Reload buttons.

Other write-path defects:

- Sector allocation is **append-only with no free list** (`RSFileStore.cs:152-157`).
  Shrinking an archive leaves orphan sectors chained to it forever - permanent bloat.
- New-archive idx writes happen at whatever the stream `Position` happens to be - the
  `Seek` only exists on the `!newArchive` branch (`RSFileStore.cs:117-134`). Appending an
  archive can overwrite another archive's index record.
- Sector 0 is allocated on an empty dat2 (`RSFileStore.cs:114`) but the reader rejects
  `sectorID <= 0` (`RSCache.cs:296`) - anything written to a fresh cache is unreadable.
- Zero-length payload throws (`RSFileStore.cs:194`).
- Version and compression are hardcoded to `1337` / GZIP (`RSCache.cs:139,176,188`).
- No master checksum table (index 255 group 255) handling anywhere - step 5 of the
  documented write algorithm is simply absent.
- `Compare to Output` (`AnalyseCache`, `Editor.cs:989-1002`) loads a stream into an
  unused local, never opens the second cache, and unconditionally `return 0;` - so it
  **always** reports "no differences found".

---

## 6. UI state

- **Every "Import ..." button in the application has no Click handler**: Import Item
  (`Editor.Designer.cs:562`), Import Sprite (:796), Import NPC (:928), Import Object
  (:1115). Plus two Export-.dat buttons (:918, :1105).
- **Interfaces tab is an empty TabPage** (`Editor.Designer.cs:1199-1206`) with no `case`
  in `LoadEditorTab`.
- **Textures tab has no selection handler**; its only context action is
  `DummyMethod()` -> `MessageBox.Show("Dummy action executed.")` (`Editor.cs:1418-1420`).
- Sprite .dat export is `MessageBox.Show("Sorry doesn't work")` (`Editor.cs:825-828`).
- NPC edit handlers exist (`Editor.cs:888,1021`) but are never wired; Object edit
  handlers are wired but `CellEditActivation` is never set, so they can't fire.
- **The app has no user-visible error reporting.** All logging goes through
  `DebugUtil` -> `Console.WriteLine` (`Debugging.cs:29-34`) in a `WinExe` with no
  console and no log panel. A failed cache load surfaces as an empty grid
  (`Editor.cs:447-450`). 15 of 22 catch blocks are log-and-continue into that void.
- Cross-thread violations: `SetObjects` is called from inside `BackgroundWorker.DoWork`
  in five places (`Editor.cs:522,581,637,707,752`).
- The single `GLControl` lives on the Models tab; item/NPC/object previews render into
  it and bail on `!glControl.IsHandleCreated`, so **previews silently do nothing until
  the user visits the Models tab once** (`Editor.cs:1256,1289,1326`).

**Renderer** is the healthiest subsystem: correct face priority, two-pass translucency,
per-vertex RS colours, recolour/retexture pipeline. Missing: animation playback (UV
offset is hardcoded to zero, `ModelRenderer.cs:312-317`; no frame/sequence loader
exists at all), skinning (parsed but never uploaded; shader has no bone attributes),
and real lighting (single camera-locked headlight, `texture.vert:19-23`).

---

## 7. Tests

~52 executable cases across 12 files, ~1,137 lines.

**Strong:** `ObjectDefinitionCodecTests` (~35 opcodes, 4-phase byte-exact round trip -
genuinely good work), `TextureDefinitionTests` (6 cases), crypto known-answer vectors.

**Critical gaps:**

- **No cache-write round-trip test.** `RSFileStoreWriteTests.cs` contains **zero
  assertions** - verified by reading it. It calls `store.Write` three times and passes
  if nothing throws. Nothing reads bytes back, checks the idx entry, or walks the chain.
- **No encrypted-container test** despite `RSContainer` taking an `int[] xteaKey`.
- **Reference table: 1 test**, pinned to `format=6, flags=0`, one archive, one file -
  precisely the shape that dodges every bug listed in section 4.
- **4 of 6 definition types have no tests at all** - including `ModelDefinition` (5
  bug-fix commits, most-churned decoder in the repo) and `NPCDefinition`.
- **`TextureGraphEvaluator` (1,824 lines, the entire focus of the last week of work) has
  zero tests.**
- `RSCache` (23 public members), `RSSector`, `RSIndex`, `MappedDataChannel`: zero.
- No CI anywhere (`no .github/`, no pipeline file).

Tests are frozen: the last commit touching `FlashEditor.Tests` is `f0f83de` (2026-03-05)
and it *added no tests*, it repaired existing broken ones. Five commits and ~3,300 lines
have shipped since with no test changes.

All 45 tests pass (`dotnet test`, 2026-07-31). That number is unchanged by the build fix -
no tests were added or removed, only the packaging that prevented them running.

**Build hygiene:** the test-package leak and BouncyCastle split are fixed (s.2a). Still
outstanding: the solution declares a `FlashEditor.runsettings` that does not exist, and
both projects retain stale .NET Framework 4.7.2 / ClickOnce bootstrapper baggage on a
`net9.0-windows` target.

---

## 8. Documentation

- **`README.md` is factually wrong and is the newest commit in the repo.** It says
  "A C# WinForms desktop application for editing animations and sprites". This is a
  RuneScape 639 JS5 cache editor. It never mentions RuneScape, JS5, cache, or 639, and
  omits the .NET 9 SDK prerequisite.
- **`AGENTS.md` is the best document here and is ~9 months stale** (last touched
  2025-06-20). It describes a `packages.config` / IKVM build that no longer exists; cites
  xUnit 2.5.0 against an actual 2.4.2; documents a `CacheEditor` API that was never
  built; uses INDEX/GROUP/CHILD terminology the code abandoned in commit `5ce0069`
  (now Index/Archive/File); and its sector-header diagram is in the wrong field order
  versus `RSSector.cs:42-63`.
- **`SETUP_TESTS.md`** instructs you to create a test project that already exists, lists
  packages that don't match the csproj, has a broken code fence, and describes CI that
  doesn't exist.
- **`tests.txt`** is a committed 66-byte UTF-16 console dump (`VSTest version 17.14.0`).
- **`reference/hydra-model-decoding/MODEL_DECODING_ANALYSIS.md`** (883 lines) is
  excellent and load-bearing. There is no equivalent for textures.
- Missing entirely: architecture doc, XTEA key file format doc, `TextureGraphEvaluator`
  doc, CONTRIBUTING, LICENSE, CHANGELOG.
- XML doc coverage is roughly **61%** in both the cache layer and the definitions layer,
  but skewed: leaf utilities are near 100%, while `RSArchiveEntry` is 0% (36 members),
  `RSContainer` 29%, `RSReferenceTable` 24%, `TextureGraphEvaluator` ~10%,
  `Track` 0%. Several existing docs are actively wrong (`ModelReference.cs:17-19`,
  `RSArchive.cs:133-136`, `RSSector.cs:47-52`, `NameHasher.cs:12-13`), and
  `RSArchive.cs:141` contains a leaked AI citation artifact
  (`:contentReference[oaicite:0]{index=0}`).
- Unexplained committed artifacts: `FlashEditor.zip` (20.5 MB) at the repo root, and a
  second copy inside `FlashEditor/`. Plus `SpriteDefinition.txt`.

---

## 9. What to do, in order

**P0 - unblock (DONE 2026-07-31)**
1. ~~Write or restore `XTEAKeyTable`.~~ Done - reconstructed from the call sites.
2. ~~Get `dotnet build` and `dotnet test` green.~~ Done - 0 errors, 45/45 passing.
3. **Validate the reconstructed `XTEAKeyTable` against a real key dump.** Its accepted
   JSON format is an assumption; until a genuine encrypted map archive decrypts through
   it, XTEA support is unproven.

**P1 - stop the write path destroying caches**
3. ~~Make `MappedDataChannel` open the source dat2 **read-only**; stage all writes and
   flush both dat2 and idx together on an explicit Save.~~ Done 2026-08-02 in `99f4f25` -
   it became `StagedDataChannel`, and `RSFileStore.SaveTo` promotes dat2 before idx.
4. **Partly done.** Save All now targets the directory that was actually opened and is
   wrapped so a failure reports instead of killing the app (`99f4f25`). Still outstanding:
   all three hardcoded `C:/Users/CJ/...` constants remain in `RSConstants.cs:117-119` and
   still drive Export-to-.dat (`Editor.cs:941,947`), Compare-to-Output (`:1056-1057`) and
   both Reload buttons (`:1093,1098`).
5. ~~Add the end-to-end write round-trip test that doesn't exist: write -> reopen ->
   read -> byte-compare, including grow, shrink and new-archive cases.~~ Done 2026-08-02
   in `99f4f25` - grow, shrink and new-archive Write cases plus SaveTo reopen-and-compare.
6. ~~Fix reference-table format-7 encode, sparse file-ID encode, and entry-hash encode.~~
   Done 2026-08-02 - plus the per-file identifier asymmetry found alongside them (s.4).
7. ~~Fix the `RSArchive` single-file chunk-byte asymmetry (silent 1-byte growth per
   save).~~ Done 2026-08-02 (s.4).
7a. ~~Fix the dummy-file-entry loop in `RSCache.WriteFile:139-145`, which defeats the
   sparse-file-ID fix in item 6.~~ **Done 2026-08-02.** The loop walked `id` from `0` to
   `archive.FileCount()` and called `archive.GetFile(id)`, a bare `files[fileId]` indexer
   with no containment check, so any sparse archive threw `KeyNotFoundException` on the
   first gap. It now reconciles the archive against its reference-table entry over actual
   file ids, in both directions, using the new `RSArchive.HasFile`/`GetFileIds`, and runs
   *before* the archive is re-encoded so backfilled entries reach the payload.
   Two defects had to be fixed alongside it for item 6 to be observable end to end:
   - `WriteFile` started from an **empty** archive whenever `container.GetArchive()` was
     null, which is the normal case: `ReadFile` calls `ReleaseData()` and drops the decoded
     archive as soon as it has handed the caller its file. Every edit therefore replaced the
     whole group with the single file being written. It now rehydrates the archive from the
     container payload, decoded against the file ids the entry held *before* the edit.
   - `validFileIds` was never updated when a file entry was added, so a reloaded container
     decoded against a stale id list. It is now refreshed from the file entries.
   Pinned by `RSCacheWriteFileTests`, which drives `WriteFile` against a synthetic on-disk
   cache and asserts after a save-and-reopen cycle.
7b. Make the format-7 archive-flags byte lossless. Decode keeps only bit 0 (the XTEA
   marker, `ReferenceTableCodec.cs:115`) and encode writes only bit 0, so any other bit
   set in a real table is silently zeroed on re-encode. No longer a field shift, but not
   yet byte-exact - it needs the raw byte retained alongside `UsesXtea`.
7c. Recompute `FLAG_SIZES` values on edit in `RSCache.WriteFile` (moved here from s.4,
   where it was listed as a codec defect but is really the write path's job).
7d. Pin the codec against a real 639 cache. Every codec test round-trips this encoder
   against this decoder, so a shared misreading of the wire format would pass. The
   single-file no-trailer rule in particular is argued from the client's unpacker and
   from AGENTS.md, not demonstrated against captured bytes.

**P2 - make it an editor**
8. Route all logging to a visible log panel; stop swallowing into a nonexistent console.
9. Wire the NPC and Object edit paths that already exist; fix the `NPCDefinition.Encode`
   null-array crashes and the unconditional-opcode emission in Item/NPC encoders.
10. Implement `SpriteDefinition.Encode`; give `TextureDefinition` a dirty flag so edits
    survive `EncodeColumnar`.
11. Implement or remove the six dead Import buttons and the empty Interfaces tab.

**P3 - toward comprehensive**
12. Add codecs for the ~30 untouched indexes, starting with the ones that have partial
    work already (maps/idx5, music/idx6) or high value (interfaces/idx3, CS2/idx12,
    animations/idx0+1+20).
13. Add `ModelDefinition.Encode` and texture-graph encode - currently the two largest
    read-only islands.
14. LZMA container support.
15. Rewrite `README.md` to describe the actual project; refresh `AGENTS.md` to current
    terminology, build system and API; delete `tests.txt`; explain or remove the
    committed zips.
