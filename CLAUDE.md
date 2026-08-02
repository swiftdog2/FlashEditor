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

One thing you cannot infer: `RealCacheLocator` walks **up** the directory tree looking for
`cache/`, so **unsetting the variable does not test the no-cache path** - point it at a directory
that is not a cache.

A cache-backed test uses `[RealCacheFact]` rather than `[Fact]`, which skips with a named reason
when no cache is present, and takes `RealCacheFixture` for a shared opened cache.

## Invariants

- **The byte-identity sweeps are the primary regression detector.** Every item, NPC and object
  definition, every floor underlay and overlay, and every map square must re-encode to the bytes
  it was read from - 20,470 items, 13,359 NPCs, 56,199 objects, 159 underlays, 235 overlays and
  1684 map squares. **If a sweep fails, you broke something - do not adjust the sweep.** Add one
  for any content type you teach the editor to write.
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
- **Saving stages; it does not touch the disk.** `RSCache.WriteFile` and everything above it write
  into an in-memory overlay. Nothing reaches the filesystem until `RSCache.WriteCache`, which
  promotes the dat2 and every index file together so a cache is never half-updated. Both it and
  `RSFileStore.SaveTo` are `internal`. A read through the same `RSCache` after a write returns the
  new bytes whether or not it was committed, so **verify persistence by reopening the store**, not
  by reading back through the cache that wrote it.

## Traps that have already cost real work

- **Never record a count of our own tests here, and distrust any you find.** It is stale by the
  next commit and it invites a reader to treat a number as a target. Measure instead. Counts *of
  the cache* are the opposite and worth writing down, because the cache does not change: 20,470
  item definitions, 1684 map squares, 659 of them with encrypted locations. The line is whether
  the number describes the data or describes us.
- **A GZip re-encode is never byte-identical** (0 of 96,183 in the reference cache). Never
  compare compressed containers to decide whether something changed; compare the decompressed
  payload.
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
- **Nothing in the suite covers the renderer.** `ModelRenderer` and the shaders are OpenGL, so a
  render-path defect passes every test in the suite. Faces the client refuses to draw were being
  drawn for as long as the viewer has existed and the sweeps never saw it. Check render changes
  by eye; model 15748 carries a render-type-2 face and is a fast case to load.

## Claims not yet verified in this repo

Reported during the map reverse engineering and plausible, but **not** reproduced by anything in
the suite. Treat each as a lead, and promote it into the sections above only once a test or a
measurement here confirms it.

- **Encryption cannot be detected by "does it inflate".** Reportedly 20 encrypted `l` groups
  inflate successfully over their own ciphertext into a few bytes of garbage. If true, detect on
  the gzip magic at `stored[9..12] == 1F 8B 08` instead. This is the inverse of the failure
  `AGENTS.md` already covers, where a key that does not fit means "not encrypted".
- **Reference-table flag bit 2 (sizes) is set nowhere in this cache**, and index 2 carries no name
  hashes at all. `AGENTS.md` documents the sizes block in operational detail as though it were
  live, so if this holds, that section describes a shape the 639 data never takes.
- **Indexes 9, 26, 27 and 29 carry an extra all-zero `i32` per child past the documented end of
  the table**, so a generic parser must tolerate trailing bytes rather than assert exact
  consumption. Index 5 and index 2 do consume exactly.

## Where to look

| | |
|---|---|
| `AGENTS.md` | The wire format: containers, groups, sectors, reference tables, XTEA, index map |
| `reference/hydra-637-definitions/` | De-obfuscated 637 opcode tables, every claim citing a `file:line` |
| `reference/hydra-637-maps/` | The map path end to end: index-5 addressing and XTEA, the `m` and `l` byte formats, floor definitions, the colour model, and how to read the obfuscated client |
| `reference/hydra-model-decoding/` | The three model decoders, the face field-name map, and the render types |
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
