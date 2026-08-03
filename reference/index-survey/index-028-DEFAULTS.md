# Index 28 - DEFAULTS

**Format:** fully-understood  
**Capability:** none  
**Effort:** small

## What it is

Two tiny opcode-terminated config blobs, not a record table. The .idx28 file is 24 bytes = 4 slots, but the reference table (idx255 group 28, 36-byte payload, decoded by hand: `06 00 0002 0001 0002 ...`) declares format 6, flags 0x00, groupCount 2, group-id deltas 1 and 2 -> **group ids 1 and 3**, each with fileCount 1 and file id 0. Slots 0 and 2 are dead (slot 0 carries a garbage length 0xFF0000 with sector 0). So "a group" = one config record, "a file" = the whole payload (single-file archive: no size table, no chunk byte), "one record" = the whole group. Both containers are compression type 0 (uncompressed) with a 2-byte version trailer of 3, and no name hashes (flags 0x00), so groups are addressable by id only.

The client hardcodes exactly those two ids: `InterfaceSettings.java:234` reads group 1, `:235` reads group 3, both off `Class253.aJS5Archive_1932` = `openFileStore(-80, true, 1, 28)` (`InterfaceSettings.java:184`), via `JS5Archive.method2733` -> `getChildFromFolder(id, 0)` (`JS5Archive.java:591-616`).

GROUP 1 (18 bytes, verbatim `01 02AA 02AB 02AC 02AD 02AE 02AF 04 01 0445 00`), decoded by `Class276.method3284` (`Class276.java:7-51`):
- opcode 1: six unsigned shorts = 682,683,684,685,686,687 -> `Class50.anIntArray417`. These are TEXTURE ids: `Class159.method2508:19-23` feeds them through `Class13.method217` (`Class13.java:13-32`) to `RenderType_Sub1.method1803:2387-2393` -> `Class48_Sub1_Sub1`, whose `method456` resolves each of the 6 via `d.method11` - the texture provider interface implemented by `Class260` (`d.java:11`, `Class260.java:5,233`) - and builds a `Class42_Sub2`, which is `glTarget 34067 = GL_TEXTURE_CUBE_MAP` (`Class42_Sub2.java:140,157,174`). So: the six faces of the default environment cube map, attached to every scene tile at `Class28.java:47,124`.
- opcode 4: count byte 1, then one unsigned short 1093 (0xFFFF -> -1) -> `Class272.anIntArray2036`. ENUM ids (index 17), indexed by the player's rank byte: `Player.java:479-490` picks this array, then `getEnum(is[rank])` and formats the title from `titleInformation & 0xff`.
- opcode 5: same shape -> `Class35.anIntArray333`, the female variant (`Player.java:479`, gender == 1). ABSENT in this cache.
- opcode 0 terminates.

GROUP 3 (31 bytes, verbatim `03 06 | 01 0000 FFEC 0000 0000 0000 0014 0000 0028 0000 003C 0000 0050 | 02 B798 | 00`), decoded by `Class155.method2495` (`Class155.java:20-70`):
- opcode 3: unsigned byte 6 -> `Class362.anInt3090`, and it ALLOCATES both arrays, so it must precede opcode 1.
- opcode 1: 6 pairs of SIGNED shorts -> `Class57.anIntArray457` = [0,0,0,0,0,0] and `Class235.anIntArray1764` = [-20,0,20,40,60,80]. These are per-slot pixel offsets added to the on-screen overhead draw position at `IntegerNode.java:375-377`, inside the loop `for(i < Class362.anInt3090)` at `:333` that walks an entity's HITSPLAT slots (`Particle_Sub3_Sub4_Sub2.anIntArray6375/6430/6386/6397`, sized from `anInt3090` at `Particle_Sub3_Sub4_Sub2.java:206-241`; the ids resolve to `Class86` hitmark defs via `Class121`, which reads config group 46 of index 2 - `Class121.java:97-109` with `client.BIT_CONFIG` at `InterfaceSettings.java:265`). So: max simultaneous hitsplats (6 here, client default 4) and where each is drawn.
- opcode 2: unsigned short 47000 -> `Class64_Sub10.anInt3666`, a MODEL id (index 7). Preloaded at `InterfaceSettings.java:239-240` and rendered 500x by `Class66.method683:38-78` as the hardware-renderer FPS benchmark.
- opcode 0 terminates.

## Current capability

Nothing index-28-specific exists. The only mention of the index anywhere in the production project is the constant declaration `DEFAULTS = 28, //fonts?` (`FlashEditor/Cache/RSConstants.cs:43`) and its display name at `:93`. A repo-wide grep for `DEFAULTS` over `FlashEditor/` returns exactly those two lines; a grep over `FlashEditor.Tests/` returns nothing (the three hits are unrelated uses of the English word "defaults"). There is no definition class, no decoder, no encoder, no export, no test that names the index. The comment `//fonts?` is wrong - fonts are index 13.

No GUI tab. `Editor.cs:64-76` lists `editorTypes` and it holds only META, 19, 8, 18, 16, 3, 7, 9, 5, 6; index 28 is not in it, and `LoadEditorTab` (`Editor.cs:471-565`) dispatches on that array, so there is no code path that opens index 28's data. The META tab does surface index 28's reference table row, because `RSCache.LoadReferenceTables` (`RSCache.cs:542-552`) decodes all 37 meta groups and `Editor.cs:526-557` binds them to `RefTableListView` - that is metadata (group count, CRC, version), not content.

What DOES already hold, and is worth knowing before anyone starts: the generic container/archive layer round-trips both groups byte-identically today, and it is proven on every run rather than only under FULL=1. `RealCacheFixture.ArchivesToExamine` (`FlashEditor.Tests/Cache/RealCache/RealCacheFixture.cs:122-134`) returns all archive ids when the index has <= 250 of them, and index 28 has 2 - so the sampled run and the full run are identical here. Index 28's two groups are therefore covered on every run by `RealCacheConformanceTests.ReferenceTables_ReEncodeToTheCapturedBytes` (`:59`), `ArchiveCrcs_MatchTheCapturedContainerBytes` (`:119`), `Containers_PreserveTheirPayloadAndHeaderAcrossReEncode` (`:169`), `Archives_ReEncodeToTheCapturedPayloadBytes` (`:218`) and `UnchangedArchives_SurviveTheEditPathWithTheirPayloadIntact` (`:295`), plus `RealCacheReferenceTableShapeTests` which sweeps all 35 tables. So the bytes go in and out intact - nobody has taught the editor what they MEAN.

## Gaps

- A `SceneDefaultsDefinition` class for group 1 with Decode/Encode over opcodes 1, 4, 5, 0 - six cube-map texture ids, and two count-prefixed unsigned-short arrays of enum ids with the 0xFFFF -> -1 sentinel round-tripped as 0xFFFF.
- A `HitsplatLayoutDefinition` class for group 3 with Decode/Encode over opcodes 1, 2, 3, 0 - a slot count, that many pairs of SIGNED shorts, and an unsigned-short model id.
- Opcode-order and presence recording on both, in the pattern CLAUDE.md already mandates: group 3 ships opcode 3 before opcode 1 and a re-encode must keep that order; group 1 ships opcode 4 and omits opcode 5, and the omission must survive.
- A codec test against the captured bytes, which are short enough to embed as literals: group 1 = `01 02 AA 02 AB 02 AC 02 AD 02 AE 02 AF 04 01 04 45 00` (18 bytes), group 3 = `03 06 01 00 00 FF EC 00 00 00 00 00 00 00 14 00 00 00 28 00 00 00 3C 00 00 00 50 02 B7 98 00` (31 bytes). This is the strongest available check, because CLAUDE.md warns that round-tripping this encoder against this decoder proves nothing.
- A byte-identity sweep over the index - here that is exactly 2 groups, enumerated from the reference table's group ids rather than from `idx28.Length / 6`.
- A GUI tab following the `editorTypes` / `LoadEditorTab` pattern (`Editor.cs:64-76`, `:471`). Two records with about eight editable fields between them; the six texture ids and the model id are the only things worth a picker, and both could reuse the existing texture and model tabs.

## Notes and traps

TRAPS, in the order they will bite:

1. **The index has 2 groups, not 4.** `idx28.Length / 6 = 4` is a count of SLOTS. The reference table declares ids 1 and 3 only, and idx28 slot 0 holds a nonsense length of 0xFF0000 with sector 0. Anything that iterates `0..fileSize/6` instead of the table's group-id list will try to read two groups that do not exist. AGENTS.md:302 already says 2 groups / 2 files and is right on the count.

2. **AGENTS.md:302 describes the contents wrongly.** It says "Default sprite ids and colours". There are no sprite ids and no colours. The six ids are TEXTURE ids consumed by the `Class260` texture provider and bound as a GL_TEXTURE_CUBE_MAP (`Class42_Sub2.java:140`); the other array holds ENUM ids for player titles; and group 3 is hitsplat layout plus a benchmark model id. `RSConstants.cs:43`'s `//fonts?` is wrong too. Both are claims, not evidence - exactly the case CLAUDE.md warns about. Fix them when you touch this.

3. **Signedness differs between the two groups, and no sweep can catch getting it wrong.** Group 1 opcode 1/4/5 read UNSIGNED shorts (`Class276.java:21-26,31,40`). Group 3 opcode 1 reads SIGNED shorts (`Class155.java:39-40`, `RSBuffer.readShort`). The very first value in group 3 that matters is `FFEC` = -20; read unsigned it becomes 65516 and the file still round-trips byte-identically. Only the client settles this.

4. **Opcode order is load-bearing here, not just non-canonical.** Group 3 stores opcode 3 (the count) before opcode 1 (that many pairs), because opcode 3 ALLOCATES the arrays (`Class155.java:46-48`) and opcode 1 loops over `Class57.anIntArray457.length` (`:38`). Emit 1 before 3 and the client reads 4 pairs into arrays it is about to throw away, then resizes to 6 and loses the data. Record the order at decode as the terrain-tile decoder already does.

5. **Absent versus default.** Group 1 has opcode 4 and no opcode 5. If a decoder materialises a default female-title array and the encoder writes it, the file grows and the client's `Class35.anIntArray333 != null` branch at `Player.java:479` flips, changing behaviour for female characters. Group 3's opcode 3 has the same shape: absent means 4, and the client synthesises `anIntArray1764[i] = 20*i` (`Class155.java:54-62`). Present-with-value-4 and absent are different bytes for the same decoded state.

6. **0xFFFF is a sentinel, not a value**, on group 1 opcodes 4 and 5 (`Class276.java:32-33,41-42`). Store -1 internally and you must write 0xFFFF back. No live case in this cache - the only entry is 1093 - so a bug here passes the byte-identity sweep and breaks on the first cache that uses it.

7. **Cross-index dependencies, all four of them.** Group 1 opcode 1 -> texture ids 682-687 resolved through `Class260`, built from indexes 26, 9 and 8 (`InterfaceSettings.java:244-245`). Group 1 opcode 4 -> enum id 1093 in index 17 (`Node_Sub10_Sub24.enumFileStore`, `InterfaceSettings.java:173`). Group 3 opcode 2 -> model id 47000 in index 7. Group 3's slot count caps hitmark defs that live in index 2 group 46 (`Class121.java:102`). A GUI tab that validates ids has to reach into four other indexes.

8. **No XTEA, no compression complication.** Both containers are compression type 0, so there is no GZip non-determinism to work around and the encrypted-region rule never applies. Both carry a version trailer of 3 and the reference table records version 3 for both - consistent, and the trailer length is 2 here rather than assumed.

9. **The 637 client only ever reads groups 1 and 3, by literal.** `method2733(1, 14)` and `method2733(3, 82)` at `InterfaceSettings.java:234-235`. Adding a group 0 or 2 in the editor would produce a cache the client silently ignores. Note also that `method2733` throws `RuntimeException` if a group's file count is not 1 (`JS5Archive.java:612`), so an editor that turns either group into a multi-file archive crashes the client at load rather than degrading.

10. **A cosmetic client bug to be aware of, not to copy.** `Player.java:489` indexes the title array by the player's rank byte with no bounds check, and this cache's array has length 1. Any rank above 0 throws. Do not "fix" it in the data by padding the array - that changes the bytes; just do not be surprised by a length-1 array.
