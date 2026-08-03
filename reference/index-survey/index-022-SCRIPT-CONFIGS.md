# Index 22 - SCRIPT_CONFIGS (varbit definitions)

**Format:** fully-understood  
**Capability:** none  
**Effort:** small

## What it is

Index 22 holds **VarBit definitions** - the table that maps a varbit id onto a bit range inside a player variable (varp). The 637 client opens it at `InterfaceSettings.java:178` (`openFileStore(-124, false, 1, 22)`), stores the handle in `Node_Sub46_Sub19.aJS5Archive_6067`, and hands that handle to `new Class198(...)` at `InterfaceSettings.java:291-292`. `Class198` is a `VarBit` cache: `Class198.method2680` (`Class198.java:77-106`) looks a varbit up by id, and on a miss fetches `aJS5Archive_1522.getChildFromFolder(Class234.method2886(id), Class32.method318(id))` (`:92-93`), which are `id >>> 10` (`Class234.java:31`) and `id & 0x3ff` (`Class32.java:61`).

So: **group = varbitId >> 10, file = varbitId & 0x3FF, one file = one varbit record.** 1024 varbits per group.

Record format, from `VarBit.method3945`/`method3946` (`VarBit.java:47-80`): a bare opcode loop terminated by 0. **Only opcode 1 exists**, and it reads `anInt3115 = readUnsignedShort()` (the varp index), `fromBit = readUnsignedByte()`, `toBit = readUnsignedByte()`. The semantics are settled by what the client does with them, not by the names: `Class140.method2289` (`Class140.java:140-149`) uses `anInt3115` to index `bitConfigArray`/`anIntArray3244` (the varp array), masks with `anIntArray6070[toBit - fromBit]` and shifts left by `fromBit`. `anIntArray6070[n] == 2^(n+1) - 1` (`Node_Sub46_Sub20.java:7,14`, 32 entries), so `fromBit` is the LSB, `toBit` the MSB, and `toBit - fromBit` must be 0..31.

Measured directly out of this 639 cache (I walked idx22 -> dat2 sectors -> BZip2 container -> archive split -> every record; script at C:\Users\CJ\AppData\Local\Temp\claude\C--Users-CJ-Desktop-FlashEditor\f188415d-792b-47d7-bdca-e00fd5387036\scratchpad\idx22c.ps1):
- Reference table idx255 group 22: format 6, table version 280, flags **0x00** (no names, no whirlpool, no sizes), 9 groups, per-group versions 18,20,11,11,53,69,81,62,39. Consumes 17686 of 17686 payload bytes - no trailing tail.
- File counts per group: 1014,1023,1023,1024,1023,1024,1024,1024,606 = **8785 files**, matching AGENTS.md:296. Group 8's ids run 0..605; groups 0-7 top out at file id 1023, so the highest varbit id in this cache is 8*1024+605 = 8797 and 431 of the 9216 addressable slots have no file at all.
- Every group is **BZip2** (container type 1), 1 chunk, ~2 KB stored / ~9 KB payload, with a 2-byte version trailer.
- Every file is either **6 bytes** (opcode 1, u16 varp, u8 from, u8 to, terminator 0) - 7366 of them - or **1 byte** (a bare terminator) - 1419 of them. **Opcode histogram: opcode 1 x7366, nothing else.** Every file consumes exactly to its declared size. Max varp id seen: 2050. No record has fromBit > toBit.

## Current capability

**Nothing interprets index 22.** There is no varbit decoder, no encoder, no GUI tab, and no test that names the index.

- `RSConstants.SCRIPT_CONFIGS = 22` (`FlashEditor/Cache/RSConstants.cs:37`) and the display name at `:87` are the only two references to index 22 in the entire repository. A repo-wide grep for `SCRIPT_CONFIGS` returns exactly those two lines; the constant has **zero adoption sites**, which per CLAUDE.md means "the editor has no feature for that index yet".
- `FlashEditor/Definitions/` contains no VarBit class (only Item, NPC, Object, Model, FloorOverlay, FloorUnderlay, MapSceneIcon, Sprites, Tracks). Every `varbit` hit in our source is the **morph** varbit field on `ObjectDefinition.cs:286` / `NPCDefinition.cs:215`, which is a *consumer* of a varbit id, not a decoder of index 22.
- No test in `FlashEditor.Tests` mentions index 22 or varbits.

What does cover it, generically and for free:
- `RealCacheConformanceTests.Archives_ReEncodeToTheCapturedPayloadBytes` (`FlashEditor.Tests/Cache/RealCacheConformanceTests.cs:217-269`) sweeps `_cache.TableIndexes`, so it re-encodes all 9 index-22 archive payloads to their captured bytes. Because `RealCacheFixture.ArchivesToExamine` (`RealCache/RealCacheFixture.cs:122-134`) returns everything when the group count is under `SampleArchivesPerIndex = 250` (`:24`), **all 9 groups are swept on every run**, FULL=1 or not. The multi-file no-op-edit sweep (`:294-349`) and the container round-trip (`:168-204`) hit them too, and `ReferenceTables_ReEncodeToTheirCapturedBytes` (`:66-101`) pins the index 22 table itself.
- The META tab (`Editor.cs:526-558`, tabs "Reference Tables"/"Containers" at `Editor.Designer.cs:297,385`) lists index 22's reference table row like every other.

So the storage layer under index 22 is proven byte-identical; **the varbit record format is not read by one line of our code.**

## Gaps

- A `VarBitDefinition` class in `FlashEditor/Definitions/` implementing `IDefinition` with `Decode(JagStream)` / `Encode()`: opcode loop, opcode 1 = { u16 varp, u8 fromBit, u8 toBit }, terminator 0. Four fields total. Model it on `FloorUnderlayDefinition.cs`, which is the closest single-opcode-family analogue.
- Non-canonical-encoding capture, per CLAUDE.md: record whether the file was 6 bytes (opcode 1 present) or 1 byte (bare terminator), and record opcode order/repetition through the existing `DecodedOpcode.cs` mechanism, so a default-valued varbit re-encodes to the 1 byte it was read from rather than to 6.
- A codec test against captured bytes (pattern: `FlashEditor.Tests/Cache/ObjectDefinitionCodecTests.cs`) covering the 6-byte record, the 1-byte bare-terminator file, and the bit-range extraction `(varp >> fromBit) & (2^(toBit-fromBit+1) - 1)`.
- A full-index byte-identity sweep - all 9 groups, all 8785 files, decode then re-encode to the exact stored bytes - in the shape of `RealCacheFloorDefinitionTests`. There is no `or` to hide behind here: every file must match, and the assertion should also pin the counts (7366 six-byte records, 1419 one-byte files) so a decoder that silently normalises the empties is caught.
- An exact-consumption assertion: each file's decoder must land exactly on its declared size. It already does for all 8785 files in this cache, so a regression is detectable.
- A `VarBitEditorTab` in `Editor.Designer.cs` following the `ItemEditorTab`/`ObjectEditorTab` pattern (TabPage + ObjectListView + OLVColumns + a `loaded[editorIndex]` BackgroundWorker branch in the `switch (type)` at `Editor.cs:525`). Columns: varbit id, varp, fromBit, toBit, and a derived mask/width column.
- Optional but high value: a reverse index from varp -> varbits, since `ObjectDefinition.morphVarbit` and `NPCDefinition.varbit` already reference these ids and the editor currently shows a bare number with nothing behind it.

## Notes and traps

Traps, in the order they will bite:

1. **Absent versus default is live here, and it is the whole difficulty.** Three distinct on-disk states collapse to the same decoded VarBit: a file id that does not exist (431 of the 9216 slots), a 1-byte file holding only the terminator (1419 files), and - hypothetically - a 6-byte record whose varp/bits are all zero. A decoder that returns "default VarBit" for all three, and an encoder that writes 6 bytes for a default, breaks 1419 files on the first save. Record which state was seen at decode, exactly as CLAUDE.md's "absent versus default" rule demands. And a "write every varbit" pass must not materialise the 431 missing ids - that changes the reference table's file-id list for the group.

2. **Sparse, non-uniform groups.** Do not assume 1024 files per group. Group 0 has 1014 files whose ids still run 0..1023; group 8 has 606. Always drive off `GetValidFileIds()`.

3. **BZip2, not GZip.** All 9 groups are compression type 1. This matters twice: BZip2 round-trips 1724/1743 in this cache (AGENTS.md:139), i.e. 19 containers in the cache do *not* come back byte-identical, so never decide "did this change" by comparing stored bytes - compare the decompressed payload. And `CompressionUtils.Bunzip2`/`Bzip2` strip and re-prepend the `BZh1` header (`FlashEditor/Utils/Compression.cs:76-117`); anything reading these bytes outside `RSContainer` must do the same.

4. **The index has no name hashes.** Table flags are 0x00, so a varbit is addressable only by id - there is no name to recover, unlike index 6.

5. **`RSConstants.cs:135-187` will send you to the wrong index.** That comment block lists "Archive 14: Empty (Pre 488: Var Bit)" and "Archive 69: Var Bit" as *config index 2* archives. Archive 69 is the 745+ layout. In 639 the client reads varbits from index 22 and nothing else; do not go hunting in index 2. (The `//aka varbits` comment on `RSConstants.cs:37` is, unusually, correct.)

6. **No 637/639 divergence to manage.** The client handles exactly one opcode and the 639 data contains exactly one opcode, 7366 times. There is no "data vetoes the client" case here - which makes this one of the few indexes where matching the client is unambiguously right.

7. **Cross-index dependency, one way only.** The varp id (0..2050 in this cache) indexes the client's player-variable array; there is no varp *definition* in this index and nothing to join against. Conversely `ObjectDefinition.morphVarbit` (opcodes 77/92) and `NPCDefinition.varbit` (opcodes 106/118) hold varbit ids that resolve *into* index 22, so a decoder here immediately improves two existing editor tabs. Resist inventing any other join - CLAUDE.md's warning that a plausible mapping is the easiest thing in this cache to confirm by accident applies.

8. **`toBit - fromBit` is bounded at 31** by `anIntArray6070`'s 32 entries (`Node_Sub46_Sub20.java:7`). No record in this cache violates it, but a GUI that lets you type a bit range must, or the client array-index-out-of-bounds on load.
