# FlashEditor

A C# WinForms editor for the RuneScape JS5 cache, **revision 639**, targeting .NET 9.

It decodes, displays and re-encodes the cache's contents: item, NPC and object definitions,
map squares and their terrain and locations, models, animations and skeletons, sprites and
procedural textures, interfaces, fonts, client scripts, quick chat, the world map, sound
effects and music. Models and map squares also render in a 3D viewport via OpenGL.

The governing rule of the project is byte identity: anything decoded must re-encode to the
bytes it was read from, because the format is not canonical and several fields have more
than one valid representation of the same value. The test suite sweeps every declared
record of every supported content type and asserts exactly that.

## Build and test

```
dotnet build FlashEditor.sln
dotnet test FlashEditor.Tests/FlashEditor.Tests.csproj
```

Building needs nothing beyond the .NET 9 SDK with the Windows desktop workload. NuGet
restores every dependency.

## Running the tests needs a cache, and it is not in this repository

Most of the suite is cache-backed. A revision 639 cache is several hundred megabytes and a
single `main_file_cache.dat2` exceeds GitHub's 100 MB file limit, so no cache is committed
here. Tests that need one use `[RealCacheFact]`, which **skips with a named reason** rather
than failing when none is found, so a fresh clone builds and runs green with a much smaller
suite than the full one.

`RealCacheLocator` walks up from the test binaries looking for a cache. To point it at a
specific one:

```
FLASHEDITOR_TEST_CACHE=<path to a directory containing main_file_cache.dat2>
FLASHEDITOR_TEST_CACHE_FULL=1     # sweep every archive rather than a per-index sample
```

`FULL=1` is a merge gate, not an inner-loop gate: it saturates every core. Iterate against
the sampled suite and run the full sweep once, at the point the work is accepted. Note that
it does not widen the map tests, which enumerate every map square on every run regardless.

XTEA keys for encrypted map squares are read from a key dump beside the cache.
`XTEAKeyTable.FindKeyFile` probes the cache directory and its parent for `xteas.json`,
`xtea.json`, `keys.json` and `xteakeys.json`, so the common layouts resolve without moving
anything. `xteas/xteas.json` in this repository is the dump for one such cache.

## Layout

| | |
|---|---|
| `FlashEditor/` | The application. `Cache/` is the wire format, `Definitions/` is one folder per content family, `Map/` is the map editor and rasteriser, `Rendering/` is the 3D viewport |
| `FlashEditor.Tests/` | xunit suite, mirroring the application's folders |
| `reference/` | Reverse-engineering notes: per-index surveys, de-obfuscated client opcode tables, the map path, model decoding |
| `tools/` | Ancillary scripts, including UI capture for tab verification |
| `xteas/` | An XTEA key dump for a revision 639 cache |

## Where the rules are written down

This project carries more written context than most, because the format is undocumented and
several of its rules were learned by getting them wrong first.

| | |
|---|---|
| `CLAUDE.md` | The project's working rules: what the client is authoritative for, the invariants, the traps, the UI conventions |
| `AGENTS.md` | The wire format: containers, groups, sectors, reference tables, XTEA, the index map |
| `TODO.md` | The running work list |
| `STATE_OF_THE_EDITOR.md` | Findings record and roadmap. Much of it above the roadmap is historical rather than current |
| `reference/DOC-CONFLICTS.md` | Claims in this project's own documents that turned out wrong, and how each was settled. Worth reading before trusting a figure from `reference/` |

## A note on the reference client

The Java client used as the implementation reference is **build 637**, while this cache is
**639**. They are a mismatched pair. The client is the reference for how anything is
implemented, unless the 639 data says otherwise, or unless the client is simply broken -
and it is, in at least two places on the map path. `CLAUDE.md` sets out how to tell those
cases apart.
