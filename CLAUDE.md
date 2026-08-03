# FlashEditor

A C# WinForms editor for the RuneScape JS5 cache, **revision 639**, targeting .NET 9.

The Java client bundled alongside this cache (in the sibling `HydraScape` repository) is
**build 637**, not 639. They are a mismatched pair, which matters constantly.

## The client leads, the data vetoes

The 637 client is the reference for how anything is implemented - algorithms, field meanings,
signedness, read order. Where it and this project disagree, the client is right.

**Unless the 639 data says otherwise.** The cache is two builds later and can legitimately
contain what the client never saw. Item opcode 131 occurs in this cache and the 637 client has
no handler for it at all; matching the client there would break decoding of real data.

**And unless the client is simply broken.** There is a third case the first two do not cover:
the client does something wrong, the data has no opinion, and copying it faithfully would
reproduce a defect. Two on the map path alone:

- The XTEA keys are wired to the wrong file family. `Class181.java:44` passes `null` keys for
  `l` (locations), which is the only encrypted family, while `:76-77` passes the real keys to
  `n`, which is never encrypted. In the running client this means 659 map squares render with
  no objects at all.
- The XTEA end offset is taken as `buffer.length`, which includes the two-byte version trailer,
  so one block past the container is enciphered. It is silent only because the extra block lands
  in the gzip trailer and the client's raw inflater never checks it.

"Match the client" is the right default and the wrong reflex. Where the client is wrong, diverge
and say so in a comment citing the line, as `reference/hydra-637-maps/01-cache-access.md` does
under its **CLIENT BUG** headings.

Full rules and the opcode tables: `reference/hydra-637-definitions/` for definitions,
`reference/hydra-637-maps/` for the map path.

## Build and test

```
dotnet build FlashEditor.sln
dotnet test FlashEditor.Tests/FlashEditor.Tests.csproj
```

Most of the suite needs a real 639 cache, which is **gitignored** at `cache/`. Point tests at
it explicitly:

```
FLASHEDITOR_TEST_CACHE=C:\Users\Cristian.Rosu\source\repos\Personal\FlashEditor\cache
FLASHEDITOR_TEST_CACHE_FULL=1     # sweep every archive rather than a per-index sample
```

**`FULL=1` is a merge gate, not an inner-loop gate.** A full sweep saturates every core: it decodes
and re-encodes every archive, and xunit parallelises collections across all of them by default.
Iterate against the sampled suite, and run the full sweep once, at the point the work is accepted.
Running it per change buys the same information repeatedly. **Do not record how long it takes** -
that is a property of the machine it ran on, not of the suite, and a stale figure gets read as a
target. One already did damage: a run measurably faster than the number written here was treated as
evidence that `FULL=1` had failed to apply, when the switch was working correctly. Measure on the
machine in hand.

**`FULL=1` does not widen the map tests, because they were never narrow.** It gates
`RealCacheFixture.ArchivesToExamine`, and only the conformance and definition suites call it. The
map suites enumerate through their own `EverySquare` helpers
(`RealCacheMapDecodeTests.cs:306`, `RealCacheRegionCodecTests.cs:256`), which walk all 256x256
region coordinates and yield every group the table holds, with no reference to the switch. So every
map square is swept on **every** run, and the sampled and full runs differ by less than the name
suggests. Do not reach for `FULL=1` to make a map assertion cover more ground - it already covers
all of it, and a sampled run that passes the map tests has earned the same claim a full one has.

**Never run more than one cache-backed suite at a time, and never run the full sweep in parallel
agents.** Each worktree carries its own checkout, `obj/`, `bin/`, test host and MSBuild nodes, and
every one of them memory-maps the same `main_file_cache.dat2`. Four concurrent full sweeps once
made the development machine unusable and had to be killed. Decode buffers are not pooled either -
`MemoryUtils` (`Rent`/`Return`, `LargeObjectThreshold`) is dead code, so every archive decode
allocates fresh and the large ones land on the LOH. **Parallelise the editing, serialise the
sweeping**: fan work out across worktrees, then run one sweep against the merged tree.

One thing you cannot infer: `RealCacheLocator` walks **up** the directory tree looking for
`cache/`, so **unsetting the variable does not test the no-cache path** - point it at a directory
that is not a cache.

A cache-backed test uses `[RealCacheFact]` rather than `[Fact]`, which skips with a named reason
when no cache is present, and takes `RealCacheFixture` for a shared opened cache.

## Invariants

- **A sweep that tolerates a failure does not test for it.**
  `RealCacheMapDecodeTests.EveryLocationFileDecodesOrReportsAMissingKey` walks all 1684 `l` groups
  and asserts `loaded + missingKey == 1684`, which scores a square that failed to decrypt exactly
  like one that succeeded - a cache whose keys had all stopped working passes it unchanged. XTEA
  decryption is pinned instead by `RealCacheXteaCoverageTests`, which asserts the claim that cannot
  be met by giving up: a group the key table has a key for, and which does not open without it,
  **must** open with it. Squares with no published key are excluded rather than counted, so they
  cannot absorb a real regression. Measured: every keyed group decrypts, 598 of 598 in the
  reference cache and 1587 of 1587 in the OpenRS2 b639 archive. Apply the same reading to any
  future sweep - an `or` in the assertion is usually a hole.
- **The byte-identity sweeps are the primary regression detector.** Every item, NPC and object
  definition, every floor underlay and overlay, and every map square must re-encode to the bytes
  it was read from - 20,470 items, 13,359 NPCs, 56,199 objects, 159 underlays, 235 overlays and
  1684 map squares. **If a sweep fails, you broke something - do not adjust the sweep.** Add one
  for any content type you teach the editor to write, through
  `FlashEditor.Tests/Cache/RealCache/DefinitionSweep.cs` rather than by writing a fifth copy of the
  enumerate-decode-re-encode-compare loop. It enumerates from the table's declared id list, pads
  the decode buffer with sentinels so an over-read cannot look like a clean stop, compares
  decompressed payloads, and asserts without an `or`.
- **The real cache is read-only.** It is opened read-only and no test writes to it. Keep it so.
- **Do not change a decode payload size** without evidence from the 639 data. The sizes are
  proven by exact-consumption sweeps over every definition.
- **A save that changes nothing must write nothing.** Re-encoding rewrites the stored bytes and
  therefore the archive CRC, which drags in the reference-table entry of every archive packed
  alongside it.
- **The format is not canonical, so a decoder has to record which encoding it saw.** Several
  fields have more than one valid representation of the same decoded value, and the original
  encoder did not pick consistently. Decoding to the value alone throws away the choice, and the
  re-encode then differs from bytes nobody edited. Every case found so far:
  - Opcode **order** within a record. The decoder is a loop, so any order reads back the same. A
    terrain tile is written overlay, flags, underlay, height; underlay-first reproduced only 91
    of 1684 files.
  - Opcode **repetition**. Floor overlay 94 emits opcode 11 twice, `255` then `127`. Keeping only
    the winning value re-encodes both as `127`, giving a file of the right length and the wrong
    contents.
  - **Aliased values.** Terrain height bytes `0` and `1` both decode to height zero, because the
    decoder maps a stored `1` to `0`, and the shipped files use both. The stored byte cannot be
    recomputed and is kept verbatim.
  - **Absent versus default.** Some tiles store a height that happens to equal what the procedural
    fallback would produce, so "did this tile store a height" cannot be inferred by comparing the
    two. It is recorded at decode.
  - **Unstable ordering.** `l50_50` places object 85 at position 3969 twice with different
    attributes. `List.Sort` is unstable and swapped them; sort with `OrderBy`.

  The pattern generalises to any index not yet done. Assume non-canonical until a byte-identity
  sweep says otherwise.
- **Half the reference-table format is dead in this cache, and the decoder still has to carry it.**
  Measured over all 35 tables in idx255 by
  `FlashEditor.Tests/Cache/RealCacheReferenceTableShapeTests.cs`: sizes `0x04`
  (`ReferenceTableCodec.cs:98`) and entry hash `0x08` (`:72`) are set on **no table at all**;
  identifiers `0x01` on indexes 3, 5, 6, 8, 10, 12, 13, 23, 30, 31, 32 and 33; whirlpool `0x02` on
  index 30 alone; no table sets a fifth bit. Index 2 has no name hashes, so a config group is
  addressable only by id - the name lookup the map path uses on index 5 has nothing to read there.
  Every table is format 6 bar index 36, a four-byte format-5 stub declaring zero groups, so the
  format-7 per-archive flags byte (`:112`) - the only in-table statement that an archive is XTEA
  encrypted - **exists nowhere on disk here**. That is why the read path infers encryption and the
  write path refuses to guess it. Keep all four branches implemented anyway: the first table that
  does set one is mis-parsed from that field onward, and no sweep would catch it, because no
  shipped table exercises the branch. idx255 declares 37 records; slots 34 and 35 hold nothing.
- **Four indexes hold groups their reference table does not declare.** Index 3 has 772, 825 and
  891; index 4 has 4787; index 12 has 699 and 700; index 32 has 498 and 1407. The client gates
  every read on the table, so an undeclared group is unreachable in game whatever its bytes say -
  which is why `RSCache.EnumerateGroups` is table-driven and `EnumerateOrphanGroups` reports the
  difference rather than dropping it. Pinned by `RealCacheEnumerationTests`. A table-driven sweep
  silently skips these, so an index-driven parser and a table-driven one disagree on exactly these
  four and nowhere else. The first survey of this said only indexes 4 and 12, which missed two.
- **Four indexes carry trailing bytes past the end of the table: four zero bytes per file.** Index
  9 has 3784 over 946 files in 946 groups, 26 has 4 over 1 file in 1 group, 27 has 1684 over 421
  files in only **2** groups, and 29 has 728 over 182 files in **1** group. Per *file* - 27 and 29
  are the only two that tell the readings apart, and a per-group parser sized from them would skip
  8 and 4 bytes and leave 1676 and 724 in the stream. This note read "per child", which is exact by
  `AGENTS.md:57` and reads as "per group" to everyone else; say file. The block sits where the
  per-file identifier block would (`ReferenceTableCodec.cs:141`) with the identifiers flag clear,
  so nothing reads it; that is the shape, not proven provenance. The other 31 tables consume to the
  byte, indexes 2 and 5 among them, so a parser must tolerate the tail rather than assert exact
  consumption. Pinned by
  `RealCacheReferenceTableShapeTests.ReferenceTableTrailingBytes_AreFourZeroBytesPerFileOnFourIndexes`,
  which requires three figures to agree on the offset: a field-by-field length from the format,
  where `Decode` leaves the stream, and what `Encode` writes.
- **Saving stages; it does not touch the disk.** `RSCache.WriteFile` and everything above it write
  into an in-memory overlay. Nothing reaches the filesystem until `RSCache.WriteCache`, which
  promotes the dat2 and every index file together so a cache is never half-updated. Both it and
  `RSFileStore.SaveTo` are `internal`. A read through the same `RSCache` after a write returns the
  new bytes whether or not it was committed, so **verify persistence by reopening the store**, not
  by reading back through the cache that wrote it.
- **A tab states its own cache index; its position states nothing.** `Editor.RegisterEditorTabs`
  maps each `TabPage` to an index and, for the self-contained tabs, to the delegate that binds their
  panel. It replaced a `static int[] editorTypes` read as `editorTypes[SelectedIndex]`, where
  inserting a page anywhere but the end silently pointed every later tab at the wrong index. The
  constructor **throws** for a page in the strip with no registration, so forgetting one is caught on
  the next launch rather than by a tab quietly showing another index's contents. Add the line, do
  not reintroduce a parallel array.
- **A new index editor should be a `DefinitionListPanel` descriptor, not another arm in
  `LoadEditorTab`.** `FlashEditor/Definitions/Editing/` holds the reusable list: the panel owns the
  worker, the percent-boundary progress, the UI-thread population and the edit commit, and one
  `DefinitionListDescriptor<TRow>` states the index, the enumeration, the decode, the columns and the
  re-encode. Items, sprites, NPCs and objects still predate it and each re-implement all of that;
  leave them until they are migrated deliberately. The Interfaces tab (index 3) is the worked
  example, and it is a `RawFileListDescriptor` - a raw group/file/size/name-hash listing - because
  index 3's record format is not reverse engineered and a listing is what can be shown honestly.

## Traps that have already cost real work

- **Never record a count of our own tests here, and distrust any you find.** It is stale by the
  next commit and it invites a reader to treat a number as a target. Measure instead. Counts *of
  the cache* are the opposite and worth writing down, because the cache does not change: 20,470
  item definitions, 1684 map squares, 659 of them with encrypted locations, and 42,256 index-3
  files across 1078 groups. The line is whether the number describes the data or describes us.
- **A GZip re-encode is never byte-identical** (0 of 96,183 in the reference cache). Never
  compare compressed containers to decide whether something changed; compare the decompressed
  payload.
- **Reading a group file by file re-decodes the group once per file.** `RSCache.ReadFile` calls
  `ReleaseData` the moment it has handed back the one file it was asked for, so the next call
  re-reads the sector chain, re-inflates and re-decodes the same archive. Over index 3 that is 42,256
  group decodes where `RSCache.ReadGroup` does 1078 for the same bytes - a count, not a timing, so it
  holds on any machine. `ReadGroup` returns byte-for-byte what the per-file path does, pinned by
  `RealCacheReadGroupTests`. The tab loaders that walk files individually are still paying it.
- **Round-tripping this encoder against this decoder proves nothing.** Two real defects survived
  exactly that way, and in both cases a hand-built test asserted the bug rather than catching
  it. Check against captured bytes or the client.
- **`.claude/worktrees/` holds stale full copies of the tree.** Scope renames to `FlashEditor/`
  and `FlashEditor.Tests/`, and verify a worktree's base commit before trusting a report written
  from it - a committed document once asserted already-fixed bugs as live for this reason.
- **A test named `*_DocumentsKnownDefect` pins behaviour that is known to be wrong**, so a fix
  shows up as a deliberate, visible test change. Convention declared at
  `FlashEditor.Tests/Cache/RSFileStoreTests.cs:11-19`.
- **A semantic name in `reference/` is a claim, not evidence.** The model dump's field-name
  table had five face arrays shuffled - the render type labelled as alpha, alpha as the render
  type, priority as skin - and this project's decoder was right while the doc was wrong for all
  five. What settles an obfuscated array's meaning is what the client *does* with it
  (`aByteArray1414` gates the draw list on `!= 2`, so it is the render type), never its position
  and never the name someone attached to it. Corrected 2026-08-02; the rows now cite that usage.
- **`Region` is ambiguous in any file that touches WinForms.** `System.Drawing.Region` arrives
  through the implicit usings and collides with `Cache.Region.Region`. Alias it
  (`using MapRegion = FlashEditor.Cache.Region.Region;`) rather than fully qualifying it
  everywhere. Cost three separate build breaks before it was worth a note.
- **A public property on a `Control` subclass needs `[Browsable(false)]` and
  `[DesignerSerializationVisibility(Hidden)]`** if it is runtime-only, or analyzer `WFO1000`
  fails the build. Attach them to the declaration, not to a doc comment above a same-named
  property elsewhere in the file.
- **Nothing in the suite covers the renderer or WinForms.** `ModelRenderer` and the shaders are
  OpenGL, so a render-path defect passes every test in the suite. Faces the client refuses to draw
  were being drawn for as long as the viewer has existed and the sweeps never saw it. Check render
  changes by eye; model 15748 carries a render-type-2 face and is a fast case to load.
  `tools/Capture-EditorTab.ps1` launches the app, selects a tab through UI Automation and writes a
  PNG, which is the only automated check that a tab draws at all. Use it on any tab you touch.
- **Every literal pixel size in `Editor.Designer.cs` is scaled at runtime.** It sets
  `AutoScaleMode.Font` with `AutoScaleDimensions(9F, 20F)`, so each hardcoded `Width`/`Height` and
  each `SizeType.Absolute` row is multiplied by the font ratio, about two thirds on the development
  machine. Widths mostly survive it; heights do not, because a `ComboBox` or `NumericUpDown` keeps
  the height its font needs while the row shrinks around it. That is how the map tab's 110px tool
  row drew at 76 and sliced its own button row in half, and how a 60px combo rendered as "Pl".
  Prefer `AutoSize` rows and docking. Re-tuning the number is treating the symptom.
- **A downscaled screenshot is not evidence about a checkbox.** An unticked box reads as ticked
  once the image is scaled below about 0.8. Crop at native resolution before claiming a control's
  state, or settle it from the code instead, which is stronger anyway.
- **A near-total aggregate match is not evidence that a join is correct.** The track-name join keyed
  the index-17 enum by index-6 group id, and every aggregate agreed: 958 of 970 keys landed on a
  real group, 958 of 963 groups got a name. It was wrong - the enum is in alphabetical order, so
  its key is the music player's list position. The one row checkable on its own falsified it: group
  0's identifier is `hash("scape main")`, and the enum holds "Scape Main" at key 150. Prefer a
  self-proving join at lower coverage; hashing the display names instead names 598 of 963 and every
  name it yields is verifiable. **Coverage is not correctness, and a plausible mapping is the
  easiest thing in this cache to confirm by accident.**
- **An orphaned method's own comment about which opcode it handles is unreliable.** Two evaluators
  in `TextureGraphEvaluator` were headed "TYPE 15: Perlin Noise" and "TYPE 34"; type 15 is cellular
  noise, already correctly dispatched to `EvalWorley`, and 34 is fractal noise, already on
  `EvalFractalNoise`. Both comments would have sent you to fix the wrong arm. Settle a dead
  method's identity from the dispatch and the client, never from its own header.
- **A dead-code verdict can name a transitively-dead cluster whose members are not independently
  deletable.** Four of the five `MapSquareNames` members flagged unreferenced have live callers in
  `Region.cs`; they read as dead only because those callers are. Deleting them does not compile.
  Two related shapes to expect: xunit test-class constructors always read as dead, because
  `IClassFixture` builds them by reflection; and deleting a method routinely orphans a private
  field into a fresh CS0414, which forces the edit wider than it was scoped.
- **Warning counts here need their method stated or they mean nothing.** `dotnet build -v:n` prints
  every warning twice, once inline and once in the summary, so a naive count doubles it. And
  `CS8618` fires once per uninitialised field while reporting the *constructor's* `file:line:col`
  every time - `NPCDefinition.cs(325,16)` alone emits it 15 times - so deduplicating by
  file+line+code **under**counts: 214 unique locations against 261 real diagnostics. Quote the
  summary block, and say which build produced it.

## Stubs and dead ends that look like work

Each of these looks like a gap worth closing and is something else. Knowing which saves a wasted
investigation.

- **`RSConstants` is already fully adopted.** The production project has **zero** bare integer
  index literals - every index-position argument already names a constant. So an unreferenced index
  constant does **not** mean someone used a magic number; it means the editor has no feature for
  that index yet. 27 of them have no adoption site anywhere. They are documentation of the index
  map and should stay as such. The only two literals worth swapping are in the test project:
  `RealCacheLocator.cs:27` hardcodes `"main_file_cache.idx255"`, and `RSCacheXteaWriteFileTests.cs:93`
  hardcodes `"main_file_cache.idx6"` where it means `MAPS_INDEX + 1`.
- **`MemoryUtils` is dead, and adopting it is not a mechanical edit.** `RSArchive.Decode` is where
  pooling belongs and is the highest-value site in the codebase: it already reuses one 4 KB buffer
  and falls back to `new byte[chunkSize]` for anything larger, which fires once per chunk per file -
  roughly 96,000 LOH allocations in a full sweep. Two things make a naive swap dangerous.
  `ArrayPool.Rent` **over-serves**, so every site must slice `AsSpan(0, length)` or it writes the
  wrong byte count and corrupts the archive; and `Return` does **not** clear, so a short read leaks
  the previous file's bytes into the next. Both need `try`/`finally`. Worth doing with a before and
  after allocation measurement, not as a tidy-up.
- **`AnalyseCache` (`Editor.cs:1064`) is a stub.** It loads the input cache into a local, never
  reads the output path at all, and unconditionally returns 0 - so "no differences found" is what
  it always reports, whatever the two caches hold.
- **The three `C:/Users/CJ/` paths are three different directories, not one repeated.**
  `CACHE_DIRECTORY` is the cache being read, `CACHE_OUTPUT_DIRECTORY` is where edits and item
  exports are written, `CACHE_ORIGINAL_COPY` is a pristine copy to revert to. Compare-to-output
  needs the first two to differ to mean anything, and the two reload buttons
  (`Editor.cs:1102,1107`) select between the last two. Repointing them at one path breaks the
  compare feature rather than fixing anything.

## Claims not yet verified in this repo

Reported during the map reverse engineering and plausible, but **not** reproduced by anything in
the suite. Treat each as a lead, and promote it into the sections above only once a test or a
measurement here confirms it.

- **Encryption cannot be detected by "does it inflate".** Reportedly 20 encrypted `l` groups
  inflate successfully over their own ciphertext into a few bytes of garbage. If true, detect on
  the gzip magic at `stored[9..12] == 1F 8B 08` instead. This is the inverse of the failure
  `AGENTS.md` already covers, where a key that does not fit means "not encrypted".

## Where to look

| | |
|---|---|
| `AGENTS.md` | The wire format: containers, groups, sectors, reference tables, XTEA, index map |
| `reference/hydra-637-definitions/` | De-obfuscated 637 opcode tables, every claim citing a `file:line` |
| `reference/hydra-637-maps/` | The map path end to end: index-5 addressing and XTEA, the `m` and `l` byte formats, floor definitions, the colour model, and how to read the obfuscated client |
| `reference/hydra-model-decoding/` | The three model decoders, the face field-name map, and the render types |
| `reference/index-survey/` | Per-index capability and format survey. `00-WORKLIST.md` is the ordered plan for the indexes that still need an editor, and its section 4 lists the shared abstractions to build before writing more of them |
| `STATE_OF_THE_EDITOR.md` | What has been found and fixed, plus the roadmap |
| `HydraScape/client/src` | The 637 client itself, for implementation questions |

`STATE_OF_THE_EDITOR.md` is a findings record with an assessment header dated 2026-07-31, and
most of it above the roadmap is a historical write-up rather than current fact - sections 1, 2
and 7 in particular describe a build and a test suite that no longer exist. **The live queue is
`P2` onward**, plus the one unfinished `P1` item (the three hardcoded `C:/Users/CJ/...`
constants still in `RSConstants.cs`). Sections `7a` to `7f` are where the design rationale for
the container, trailer and XTEA rules lives, and are worth reading before touching any of them.

## Conventions

- No em dashes anywhere - in code, comments, docs or commit messages. Use `-`.
- XML doc comments on public members, explaining **why** rather than restating the code.
- Commit messages explain the failure mode, not just the change.
