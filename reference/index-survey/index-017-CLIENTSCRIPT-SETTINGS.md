# Index 17 - CLIENTSCRIPT_SETTINGS (name is wrong - it is the ENUM table)

**Format:** fully-understood  
**Capability:** partial-read  
**Effort:** small

## What it is

Index 17 is the client's **enum table**, not client-script settings. The 637 client opens it as `Node_Sub10_Sub24.enumFileStore = Class42_Sub3.openFileStore(-49, false, 1, 17)` (`InterfaceSettings.java:173`, field declared `Node_Sub10_Sub24.java:9`) and hands it to `new Class29(..., enumFileStore)` (`InterfaceSettings.java:252-253`), whose only two methods are `getEnum` and `getEnumData` (`Class29.java:226`, `:269`).

ADDRESSING. An enum is identified by a single id, split `group = id >>> 8`, `file = id & 0xFF` (`Class29.java:237-238`, calling `Class153.method2490` at `Class153.java:181-191` which returns `i >>> 8`, and `Node_Sub10_Sub9.method1032` at `Node_Sub10_Sub9.java:15-25` which returns `i & 0xff`). So a **group is a bank of 256 consecutive enum ids**, a **file is one enum**, and one **record inside a file is one key/value pair**.

MEASURED IN THIS CACHE (I parsed idx255 group 17 and all 14 groups out of dat2 directly, read-only):
- Reference table: format 6, version 587, **flags 0x00** (no group or file name hashes), 14 groups, ids 0..13 contiguous, file counts 256 x 13 + 230 = **3558 files**, table consumed to the byte with 0 trailing bytes.
- Group versions: 73, 54, 109, 111, 55, 208, 155, 40, 109, 35, 9, 32, 26, 22.
- Containers: group 1 is GZip, the other 13 are **BZip2**; all 14 carry a 2-byte version trailer; all 14 are single-chunk. No XTEA.
- **2586 of the 3558 files are a bare `0x00` terminator** - unallocated enum slots. Only **972 enums are populated**. Populated per group: g0=18, g1=7, g2=52, g3=89, g4=73, g5=99, g6=88, g7=51, g8=97, g9=72, g10=6, g11=111, g12=184, g13=25.
- Largest file 17,373 bytes.

WIRE FORMAT (`GameConfig.loadEnum`, `GameConfig.java:148-174`, dispatching to `extractEnumData`, `:78-123`; type constants at `:6-7`): opcode loop terminated by 0.
- `1` = key type, one **signed** byte mapped to a char by `Class64_Sub7.method576`
- `2` = value type, same
- `3` = default string (0-terminated cp1252)
- `4` = default int32
- `5` = table of int32 key -> string value; `u16` count then count x (int32, string)
- `6` = table of int32 key -> int32 value; `u16` count then count x (int32, int32)

The key is **always an int32 on the wire** regardless of the type char (`GameConfig.java:95`); the type chars are semantic labels. The value's wire type is decided by which opcode was used (5 vs 6), not by the type char.

Observed key-type chars: i(787) o(120) I(32) n(12) K(8) O(8) S(3) v(1) J(1). Value-type chars span 22 distinct bytes including a non-ASCII `0xAB` on 10 files.

CONTENT. Enum 1345 = group 5 file 65 is the music player's track-name list: key type `i`, value type `s`, 970 entries, in alphabetical order (0 "Adventure", 1 "Al Kharid", ... 974 "Cage Against the Machine"). Enum 680 (group 2 file 168) is the skill-name list: `(0,"Attack") (1,"Defence") (2,"Strength") (3,"Constitution") (4,"Ranged") (5,"Prayer") (6,"Magic") (16,"Agility") ...`. This confirms the two AGENTS.md claims about groups 5 and 2.

## Current capability

One enum, partially, read-only, as a support detail of the music tab.

- `FlashEditor/Cache/RSConstants.cs:32` declares `CLIENTSCRIPT_SETTINGS = 17`; `:82` puts the (wrong) name in `indexNames`, which is consumed only by `GetIndexName` (`RSConstants.cs:111`) for a debug log line at `RSCache.cs:607`. No index browser hangs off it.
- `FlashEditor/Definitions/Tracks/TrackNames.cs:53-73` (`Load`) is the **only** code in the project that reads index 17. It reads exactly one file - `cache.ReadFile(RSConstants.CLIENTSCRIPT_SETTINGS, 1345 >>> 8, 1345 & 0xFF)` at `:58-59`.
- `TrackNames.ReadStringValues` (`:85-126`) is a partial decoder. It walks the full opcode set 1-6 correctly, but **discards the keys, the key/value type chars, the defaults and all int-valued tables**, returning only the strings from opcode 5. Its own doc comment at `:79-84` states it is deliberately not a general enum decoder.
- Consumer: `FlashEditor/Definitions/Tracks/TrackEditorPanel.cs:219`, which turns the strings into display names for index-6 tracks by hashing them.
- Test: `FlashEditor.Tests/Definitions/RealCacheTrackTests.cs:114` `TrackNamesJoinOnTheArchiveNameHash`. It asserts the *music-name join*, not the enum byte format - it never checks that any index-17 file decodes correctly, only that names hash back to index-6 group identifiers.

There is **no** enum definition class, **no** `Encode`, **no** codec test against captured bytes, **no** byte-identity sweep, and **no** GUI tab. Coverage is 1 of 972 populated enums, and lossy on that one.

## Gaps

- An `EnumDefinition` class under `FlashEditor/Definitions/` with `Decode(JagStream)`/`Encode()`, modelled on `FloorOverlayDefinition`. It must carry: key type byte, value type byte, default string, default int, and an ordered list of (int32 key, string|int32 value) pairs - plus the recorded opcode order and the presence/absence flags needed for byte identity (see traps).
- Support for the empty enum. 2586 of 3558 files are a single `0x00` byte. `Decode` must produce a present-but-empty definition and `Encode` must emit exactly one zero byte, or the sweep loses 73% of the index.
- A codec test against captured bytes - hand-picked files covering each of the four opcode orders that occur, the `0x00`-only file, and the `0xAB` value-type file.
- A full-index byte-identity sweep: walk all 14 groups x every file id from the index-17 reference table, decode and re-encode each of the 3558 files, assert `SequenceEqual` on the original bytes. This is cheap - the whole index is ~470 KB decompressed and every file already consumes exactly, so a passing sweep is achievable on the first correct attempt. Use `[RealCacheFact]` + `RealCacheFixture`, no `or` in the assertion.
- A write path: `RSCache.WriteFile(RSConstants.CLIENTSCRIPT_SETTINGS, id >>> 8, id & 0xFF, bytes)`. Nothing index-17-specific is needed - no XTEA, single-chunk groups - but the 256-file group means the multi-file archive trailer applies, so an edit re-slices the group.
- A GUI tab following the `Editor.Designer.cs` pattern: enum id list (ids are the only handle - the table sets flags 0x00, so there are no names to show), key/value type, defaults, and an editable key/value grid. Reuse the `TrackEditorPanel` layout since it is the newest example.
- Rename or at minimum document `RSConstants.CLIENTSCRIPT_SETTINGS`. `TrackNames.cs:20-23` already flags the misnomer and explicitly defers the rename.

## Notes and traps

TRAPS, in the order they will bite.

1. **Opcode order is non-canonical in exactly one respect, and the natural order is the wrong one.** Only four opcode sequences occur across all 972 populated enums: `(1,2,6,4)` x712, `(1,2,5,3)` x253, `(1,2,4)` x6, `(1,2,3)` x1. **The default (3 or 4) is written AFTER the table (5 or 6), never before.** An encoder that emits opcodes in ascending numeric order - the obvious choice, and what every other definition encoder in this project effectively does - reproduces 0 of the 965 enums that have both a table and a default. This is CLAUDE.md's "opcode order within a record" rule hitting again. Since only two orders occur with a table, either record the order at decode or hard-code default-after-table; recording it is safer and costs one field.

2. **2586 files are a bare `0x00`.** An "enum is absent, skip it" shortcut passes the decode sweep and fails byte identity on three quarters of the index.

3. **Type chars are signed bytes; keep the raw byte.** The client does `readSignedByte()` then `Class64_Sub7.method576` (`Class64_Sub7.java:9-31`), which remaps only 0x80-0x9F through cp1252 and passes everything else straight through. One value type in this cache is `0xAB` (10 files), and 22 distinct value-type bytes occur. Store the byte, expose the char for display. Round-tripping through `char` is an avoidable way to lose one.

4. **Strings use the Jagex cp1252 remap, and `JagStream` has two readers.** Use `ReadJagexString`/`WriteJagexString` (`FlashEditor/IO/JagStream.cs:736` and `:752`) - I checked them against `RSBuffer.readString` (`RSBuffer.java:878-894`) plus `method576` and they match, including the `'\0' -> '?'` fallback. The plain `ReadString` just above at `:725` skips the remap and is wrong here.

5. **Do not copy the client's opcode loop.** `GameConfig.loadEnum` (`GameConfig.java:148-160`) has **no default arm**: an unrecognised opcode falls through `extractEnumData` consuming nothing, and the loop reads the next byte as an opcode. It cannot detect desync. Throw instead. Moot for correctness in this cache - I verified only opcodes 1-6 occur and **all 3558 files consume to the byte with zero remainder** - but it will silently eat a bug you introduce.

6. **Enum 2252 is special-cased in the client and its cached bytes are ignored.** `Class29.getEnum` (`Class29.java:243-245`) substitutes `GameConfig.questEnum()`, a 100-entry table built in code (`GameConfig.java:44-75`) reading "The knights tale N". Editing 2252 in the cache changes nothing in the 637 client. Whether 639 still does this is **unknown**.

7. **No names, ever.** The index-17 table sets flags `0x00` - no group and no file identifiers - so an enum is addressable only by id. There is no equivalent of the index-6 name-hash trick here.

8. **Do not regress the TrackNames join while generalising it.** `TrackNames` keys by *hash of the value string*, not by the enum key, and `TrackNames.cs:25-36` plus `RealCacheTrackTests.cs:100-112` document at length why keying by the enum key looks right (958/970) and is wrong. If a general enum decoder replaces `ReadStringValues`, the music tab must keep hashing values. CLAUDE.md's "coverage is not correctness" trap was written about precisely this join.

9. **Container-level round trips are not byte-identical.** Group 1 is GZip; per AGENTS.md a GZip re-encode is 0/96,183 identical. Compare decompressed payloads, and put byte identity at the *definition* level, not the container level. The other 13 groups are BZip2, where AGENTS.md records 1724/1743 - so 19 BZip2 groups somewhere in the cache do not round-trip, and whether any of them is in index 17 is unverified.

10. No XTEA, no dependency on any other index, and nothing suggests the format changed between 637 and 639 - every byte in the 639 data is accounted for by the 637 decoder.

My analysis scripts are at `C:\Users\CJ\AppData\Local\Temp\claude\C--Users-CJ-Desktop-FlashEditor\f188415d-792b-47d7-bdca-e00fd5387036\scratchpad\idx17.py` and `idx17b.py`; they read the cache only and wrote nothing.
