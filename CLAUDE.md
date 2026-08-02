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

Full rules and the opcode tables: `reference/hydra-637-definitions/`. Read it before changing
how any definition opcode is read.

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

Two things you cannot infer:

- `RealCacheLocator` walks **up** the directory tree looking for `cache/`, so **unsetting the
  variable does not test the no-cache path** - point it at a directory that is not a cache.
- Current counts, measured 2026-08-02: **552 pass with the cache** (full sweep), **533 pass and
  19 skip without one.** Treat these as already stale and measure before relying on them.

## Invariants

- **The three byte-identity sweeps are the primary regression detector.** All 20,470 item,
  13,359 NPC and 56,199 object definitions must re-encode to the bytes they were read from. **If
  a sweep fails, you broke something - do not adjust the sweep.**
- **The real cache is read-only.** It is opened read-only and no test writes to it. Keep it so.
- **Do not change a decode payload size** without evidence from the 639 data. The sizes are
  proven by exact-consumption sweeps over every definition.
- **A save that changes nothing must write nothing.** Re-encoding rewrites the stored bytes and
  therefore the archive CRC, which drags in the reference-table entry of every archive packed
  alongside it.

## Traps that have already cost real work

- **Any quoted test baseline is stale.** Measure it. This has caught out briefs written in this
  repo more than once, including the numbers above at the moment they were written.
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
- **Nothing in the suite covers the renderer.** `ModelRenderer` and the shaders are OpenGL, so a
  render-path defect passes every test in the suite. Faces the client refuses to draw were being
  drawn for as long as the viewer has existed and the sweeps never saw it. Check render changes
  by eye; model 15748 carries a render-type-2 face and is a fast case to load.

## Where to look

| | |
|---|---|
| `AGENTS.md` | The wire format: containers, groups, sectors, reference tables, XTEA, index map |
| `reference/hydra-637-definitions/` | De-obfuscated 637 opcode tables, every claim citing a `file:line` |
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
