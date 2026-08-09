# Index 12 - CLIENT_SCRIPTS (CS2 bytecode)

> **Capability grading corrected 2026-08-09.** "Capability: none" is false. Index 12 has a codec,
> a whole-index byte-identity sweep, a Client Scripts tab and a disassembler that resolves jump
> targets and marks labels.
>
> Two things this document still gets wrong are already logged in `reference/DOC-CONFLICTS.md`:
> the four footer count fields are named in the wrong pairs, and the sweep is driven from the
> reference table rather than from idx12.
>
> What remains open is naming the `if_set*`/`cc_set*` opcode family (`TODO.md` item 26f) and
> structured control flow (a Backlog item). The format sections below are sound.

**Format:** fully-understood  
**Capability:** none  
**Effort:** large

## What it is

Compiled CS2 client-script bytecode - one script per group, one file per group, file id 0.

CLIENT AUTHORITY. `InterfaceSettings.java:169` opens index 12 as `Class52.clientScriptArchive = openFileStore(-120, false, 1, 12)`. Two readers consume it, and both take file 0 of a group and hand the bytes to `Class22.unpack`, which returns a `CS2Script`:
- `Node_Sub46_Sub13_Sub2.getScript(int i)` at `:18` - `clientScriptArchive.getChildFromFolder(i, 0)`, i.e. script by raw GROUP ID.
- `Class213.method2779` at `:58,:86,:112` - `method2733(method2763(..., key), ...)`, i.e. script by reference-table IDENTIFIER, where `method2763` (`JS5Archive.java:1112`) looks the key up in `aVersionTable_1571.aClass122_2666`, the identifier->group map built at `VersionTable.java:152` from the table's identifier array. `method2733` (`:591-609`) then returns `getChildFromFolder(group, 0)` because the group holds one file.
The interpreter is `Class247.runScript` (`:7758`): opcodes <100 are an if/else chain in-line (`:7781-7988`), 100..4999 dispatch to `method3148`, 5000..9999 to `method3156` (`:7994-8001`).

RECORD FORMAT, read straight off `Class22.unpack` (`Class22.java:11-78`), offsets relative to the decompressed file:
- `[0]` optional NUL-terminated name/trigger string via `RSBuffer.method1222(-1)` (`RSBuffer.java:427`): a leading `0x00` means absent and consumes one byte.
- instruction stream to `footer`, each entry `u16 opcode` then: opcode 3 -> NUL-terminated string; opcode <100 and not 21, 38, 39 -> `int32`; otherwise -> `u8`.
- `footer = len - 2 - trailerLen - 12`, holding `int32 instructionCount`, `u16 integerArgCount`, `u16 stringArgCount`, `u16 localIntCount`, `u16 localStringCount`.
- then `u8 switchBlockCount`, and per block `u16 caseCount` followed by `caseCount` pairs of `int32 key, int32 jumpDelta`.
- final `u16 trailerLen` = the byte length of the switch section.

MEASURED IN THIS CACHE (my own decode of dat2/idx12/idx255, read-only):
- idx12 is 24,906 bytes = 4151 slots, and all 4151 are populated - no empty slots.
- idx255 group 12 is the index-12 reference table: GZip, no version trailer, format 6, table version 1378, flags 0x01 (identifiers only, no whirlpool, no sizes), 4149 groups declared, max group id 4150, consumed to the byte.
- Every declared group holds exactly one file (file-count histogram is 1 x 4149; 4149 files total).
- Groups 699 and 700 exist in idx12 and in dat2 but are ABSENT from the reference table.
- All 4151 stored containers are GZip (type 2) with a 2-byte version trailer. None is XTEA encrypted.
- All 4151 decompressed payloads parse cleanly under the 637 rule: the instruction stream ends exactly at `footer`, the switch section ends exactly at `len-2`, and the decoded instruction count equals the `instructionCount` in the footer, for every one.
- 0 of 4151 carry the leading name string - the first byte is `0x00` in every script.
- 485 of 4151 carry at least one switch block. 335,279 instructions total; largest script 7084 instructions. 582 distinct opcode values, maximum 7314; 48,205 instructions use an opcode >= 100 and therefore a one-byte operand.

## Current capability

Nothing beyond generic container/archive plumbing. Index 12 has no decoder, no encoder, no viewer, no test.

- `FlashEditor/Cache/RSConstants.cs:27` declares `CLIENT_SCRIPTS_INDEX = 12`. A whole-repo grep finds no other use of that constant anywhere in `FlashEditor/` or `FlashEditor.Tests/`. Per CLAUDE.md's own rule (the production project has zero bare index literals), an unreferenced index constant means no feature exists for that index.
- `FlashEditor/Cache/RSConstants.cs:77` puts the display string `"CLIENT_SCRIPTS"` in `indexNames`, which is only used by `GetIndexName` (`:111`).
- `FlashEditor/Editor.cs:64-76` is the `editorTypes` array that drives which indexes the editor loads and which tab shows them. It lists 19, 8, 18, 16, 3, 7, 9, 5, 6 and the meta index. **12 is not in it**, and `Editor.cs:526-851` has no `case` for it. There is no generic raw-index browser either.
- `FlashEditor/Definitions/` contains Item, NPC, Object, Model, FloorUnderlay, FloorOverlay, MapSceneIcon, Sprites and Tracks. There is no script definition class.
- The only test that names index 12 is `FlashEditor.Tests/Cache/RealCacheReferenceTableShapeTests.cs:107`, which asserts index 12 is one of the tables that sets the identifiers flag. That is a reference-table property, not a content codec.
- `STATE_OF_THE_EDITOR.md:124` already says it plainly: "no CS2 script (idx 12) decoder", and `:624` lists CS2/idx12 under P3 as future work.

So index 12 can be read as raw bytes through `RSCache.ReadFile` like any index, and nothing in the project interprets those bytes.

## Gaps

- A `ClientScriptDefinition` class in `FlashEditor/Definitions/` implementing `IDefinition` with `Decode(JagStream)` and `Encode()`, mirroring `Class22.unpack` exactly: optional leading NUL-terminated name (absent when the first byte is 0), instruction stream of `u16 opcode` plus a per-opcode operand, then the 12-byte footer, then the switch-block section, then the `u16` switch-section length. The decoder must retain the raw opcode/operand pairs rather than lowering them to a model, or the re-encode cannot be byte-identical.
- A codec test against captured bytes, in the style of `FlashEditor.Tests/Cache/ObjectDefinitionCodecTests.cs` - a fixture file under `FlashEditor.Tests/Fixtures/RealCache/` holding one real index-12 payload, asserting the decoded field values AND the exact re-encoded bytes. Round-tripping our encoder against our decoder proves nothing (CLAUDE.md); the fixture has to come from the cache.
- A full-index byte-identity sweep over all 4151 groups, in the style of the item/NPC/object sweeps. Drive it off idx12 (4151 groups), NOT off the reference table (4149), or groups 699 and 700 are silently skipped. Assert three things per group, all of which I verified hold today: the instruction stream ends exactly at the computed footer offset, the switch section ends exactly at `len-2`, and the footer's instruction count equals the number of instructions decoded. Then assert the re-encoded bytes equal the decompressed input bytes.
- An `Encode()` that is provably canonical. `instructionCount`, the switch-section length and the footer offset are all derivable from the content, so nothing here needs a 'which encoding did I see' record - but that has to be proven by the sweep, not assumed.
- A GUI tab following the `Editor.Designer.cs` pattern (a `TabPage` field declared alongside `TextureViewerTab` / `MapEditorTab` / `TrackEditorTab` at `:143-148`), plus an entry in `Editor.cs:64-76` `editorTypes` and a `case RSConstants.CLIENT_SCRIPTS_INDEX` in the loader switch at `Editor.cs:526`. Editing raw `u16` opcodes is not usable, so the tab needs a disassembler: an opcode -> mnemonic/signature table covering the 582 distinct opcodes this cache actually uses, derived from `Class247.runScript` (`:7781-7988`), `Class247.method3148` (opcodes 100-4999) and `Class247.method3156` (opcodes 5000-9999).
- A write path decision: `RSCache.WriteFile(12, groupId, 0, data)` already works structurally, since every group is a single file. Nothing else is needed for persistence.

## Notes and traps

TRAPS, in the order they will bite.

1. **The operand-width rule is the whole ballgame, and the exceptions are the trap.** `Class22.java:64-68`: an operand is a 4-byte `int32` when `opcode < 100 && opcode != 21 && opcode != 38 && opcode != 39`, otherwise a single unsigned byte, except opcode 3 which is a NUL-terminated string. The three carve-outs sit inside the `<100` range and a reader that misses them desyncs the instruction stream and never recovers. The obfuscated source spells them as `(type ^ 0xffffffff) > -101`, `type != 21`, `(type ^ 0xffffffff) != -39`, `type != 39`, which decode to `type < 100`, `!= 21`, `!= 38`, `!= 39`.

2. **The 637 rule is correct for the 639 data, and I proved it rather than assuming it.** All 4151 groups parse with the stream terminating exactly at the footer, the switch section terminating exactly at `len-2`, and the footer's instruction count matching the decoded count. A wrong operand-width table would have to land on all three coincidences 4151 times. This is the one place where the usual 637-vs-639 anxiety can be retired.

3. **Groups 699 and 700 are orphans.** They hold real, parseable CS2 payloads in dat2 and idx12, but the reference table declares only 4149 groups and does not list them. `JS5Archive.method2758` gates every client read on the table, so the client can never load them - they are dead weight from a repack. Consequence for us: a sweep or a tab built off `GetReferenceTable(12)` sees 4149 and reports success while never touching two groups. Enumerate idx12.

4. **The reference-table identifier is not a plain name hash, and part of it is a packed interface hook.** Index 12 sets the identifiers flag (`0x01`), and the client feeds `class105.anInt3416 | componentKey << 10` into the identifier map at `Class213.java:51,77,105` - `Class105` is an event-type token constructed with small ids (10, 11, 12, 13, 14, 15, 16, 17, 18, 73, 76 across `Class142`, `Node_Sub46_Sub2`, `Class60`, `Class308`, `Class90`, `Class331`, `Node_Sub10_Sub26`, `Class288`, `Class336`, `Class152`, `Class206`). The data agrees: the low 10 bits of the 4149 identifiers are uniform at about 4 per bucket over all 1024 values, with two enormous spikes - 177 at 10 and 136 at 17 - and exactly one identifier equals `0x3FFFC10`, which is the literal `0x3fffc00` at `Class213.java:105` OR-ed with event type 16, the global-default hook. A further 87 sit in the `(interfaceId + 65536) << 10` window from `Class213.java:77` with consecutive interface ids (948-955, 1156-1178, ...). **What the remaining ~3800 identifiers are is UNKNOWN.** They are uniformly distributed 32-bit values (1929 negative) with no structure I could prove, and I could not recover a single script name to test the `String.hashCode` hypothesis against. Do not label the column "name hash" - that would be exactly the plausible-mapping-confirmed-by-accident failure CLAUDE.md warns about. Label it "identifier" and note the two proven sub-populations.

5. **No non-canonical encoding found, which is unusual for this cache and should still be proven by a sweep.** Every derived field is genuinely derived: `instructionCount` matched the decoded count in all 4151, the switch-section length matched in all 4151, and the optional leading name string is absent in all 4151 (first byte `0x00`). The `method1222` reader cannot produce an empty string - a `0x00` first byte always returns null - so there is no absent-versus-empty ambiguity. But the whole index is GZip, so per AGENTS.md never compare the stored container; compare the decompressed payload.

6. **No XTEA, no BZip2, no LZMA, no multi-file groups, no multi-chunk groups.** All 4151 are GZip with a 2-byte version trailer and a single file. Group version values run 1..82.

7. **No decode-time dependency on any other index.** Scripts reference enums (17), interfaces (3), items (19) and so on by numeric operand, but nothing is needed to decode or re-encode a script. A *disassembler* that resolves those operands to names would depend on those indexes; the codec does not.

8. **The effort is lopsided.** Decode + encode + the byte-identity sweep is small-to-medium and fully specified by `Class22.java` - I effectively wrote it in PowerShell to produce the numbers above. The "GUI editing" half of `complete` is where the large effort lives: 582 distinct opcodes are in use across three separate client dispatchers, and an opcode table is the only thing that makes a script tab more useful than a hex editor. If the goal is to stop index 12 being a blind spot cheaply, ship decode + encode + the sweep first and grade it `read-write-no-tests` -> then `read-only`+sweep, rather than waiting on the disassembler.

## External reference

**RuneStar** (GitHub) carries clientscript opcode definitions and decompilation work for the
RuneScape client script format. The codec here is small and fully specified from the 637 client;
what RuneStar is worth consulting for is the part that makes a *tab* useful rather than a hex
dump, namely naming the opcodes and reconstructing control flow. Check its coverage for build
639 specifically before relying on it - the project is oriented at later revisions, and an
opcode table from the wrong build is the kind of plausible-looking mapping this cache confirms
by accident.
