# Index 24 - QUICK_CHAT_MESSAGES (misnamed - it is a complete quick-chat bank, menus AND messages)

**Format:** fully-understood  
**Capability:** none  
**Effort:** small

## What it is

Index 24 is one of TWO identically-shaped quick-chat banks (24 and 25). The name is wrong: menu-vs-message is group 0 vs group 1 WITHIN each index, not a split across the two indexes (AGENTS.md:298-299 already flags this).

CLIENT PROOF OF IDENTITY. Index 24 is opened at InterfaceSettings.java:180 (`openFileStore(-81, false, 1, 24)` -> `Class81.aJS5Archive_622`); index 25 at :181 -> `Class322.aJS5Archive_2714`. Both archives are handed to TWO loaders together, at InterfaceSettings.java:297-300:
  - `Class212` (Class212.java:34-54) reads folder 0 only - `getChildFromFolder(0, id)` at Class212.java:66,71 - producing `Node_Sub46_Sub1`.
  - `Class280` (Class280.java:338-363) reads folder 1 only - `getChildFromFolder(1, id)` at Class280.java:378,383 - producing `Node_Sub46_Sub11`.
An id >= 0x8000 selects index 25 with `0x7fff & id` (Class212.java:65-66, Class280.java:377-378), and a record loaded from 25 has 0x8000 OR'd back into its stored ids (Node_Sub46_Sub1.method1531, Node_Sub46_Sub11.method1575). So 24 and 25 are two id NAMESPACES of one format.

MEASURED FROM THIS CACHE (I parsed idx255 group 24, walked the dat2 sector chain, decompressed and split both groups). Reference table: format 6, version 90, flags 0x00 -> NO name hashes, so groups and files are addressable by id only. 2 groups, contiguous file ids:
  - group 0 = 211 files, version 64, BZip2 container, stored 4501 B, 2-byte version trailer, payload 8374 B, chunks=1
  - group 1 = 1088 files, version 77, BZip2 container, stored 11608 B, 2-byte trailer, payload 39501 B, chunks=1
  Total 1299 files, matching AGENTS.md:298.

So: a GROUP is a bank half (0 = menu tree, 1 = message templates); a FILE is one record; a RECORD is one opcode-terminated definition. It is many small records, not a few blobs.

MENU RECORD (group 0) - Node_Sub46_Sub1.method1532 (:131-147) loops `op = u8`, 0 terminates, dispatching to method1527 (:34-67):
  op 1 -> cp1252 NUL-terminated string = the category caption (:61; RSBuffer.readString at RSBuffer.java:878-894)
  op 2 -> u8 count, then count x { u16 id, i8 shortcut char } -> SUBMENU links (:41-49)
  op 3 -> u8 count, then count x { u16 id, i8 shortcut char } -> MESSAGE links (:51-58)
  any other opcode -> no payload read at all (silent, and it desyncs the stream)
Measured over all 211: opcodes 1x211, 2x64, 3x173; opcode orders [1,2]x38, [1,2,3]x26, [1,3]x147; 211 submenu links, 1090 message links; every record consumed to the exact byte, 0 failures.
WHICH LIST IS WHICH IS PROVEN, not inferred: op2 ids span exactly 0..210 (= the 211 group-0 menus) and op3 ids span exactly 0..1087 (= the 1088 group-1 messages). Also every one of the 211 op2 entries carries a NONZERO hotkey letter and every one of the 1090 op3 entries has hotkey byte 0. The tree resolves correctly: the only three menus never referenced as anyone's submenu are 85 "Quick Chat" (subs: General/Trade-Items/Skills/Group events/Clans/Inter-game, hotkeys g,t,s,e,c,m), 117 "The Great Orb Project", 151 "Fish Flingers".

MESSAGE RECORD (group 1) - Node_Sub46_Sub11.method1584 (:275-294) loops `op = u8`, 0 terminates, dispatching to method1578 (:129-173):
  op 1 -> cp1252 NUL-terminated template string, split on '<' into segments (:132). Each '<' is a substitution slot, e.g. "My Agility level is <."
  op 2 -> u8 count, then count x u16 -> related/response message ids (:157-163)
  op 3 -> u8 count, then per entry { u16 slotTypeId; then N x u16 } where N is the type's `anInt2915` (:135-152)
  op 4 -> NO payload, clears a boolean (:153-155)
Measured over all 1088: opcodes 1x1088, 2x413, 3x257 (opcode 4 NEVER occurs); orders [1]x517, [1,2]x314, [1,2,3]x99, [1,3]x158; every record consumed to the exact byte, 0 failures. Slot type ids seen: 0x163 1x8 4x70 6x1 7x1 8x4 9x8 10x17 11x50 13x1 14x4 15x1 (12 distinct, 0 unknown).
Sample decode: msg 0 "What is your level in Agility?", msg 1 "My Agility level is <." (1 slot), msg 2 "Let's go to Agility course: <?".

## Current capability

NOTHING index-specific. The only reference to index 24 anywhere in the repo is the constant itself:
  - FlashEditor/Cache/RSConstants.cs:39  `QUICK_CHAT_MESSAGES = 24`
  - FlashEditor/Cache/RSConstants.cs:89  the display-name string
A grep for `QUICK_CHAT` across the whole tree matches only those two files plus AGENTS.md:298-299. There is no `RSConstants.QUICK_CHAT_MESSAGES` call site, no `24` used as an index id, no definition class (FlashEditor/Definitions/ holds Item, NPC, Object, Model, FloorUnderlay, FloorOverlay, MapSceneIcon, Sprites/, Tracks/ - nothing quick-chat), and no test file touching it (FlashEditor.Tests has no quick-chat test).

No GUI. The editor's tab-to-index map is `Editor.cs:64-76` and lists only indexes 19, 8, 18, 16, 3, 7, 9, 5, 6. Index 24 has no tab and is never loaded.

What DOES work today is the generic container/archive layer beneath it, and it covers index 24 completely. `RealCacheConformanceTests` iterates `_cache.TableIndexes` in every sweep:
  - ReferenceTables_ReEncodeToTheCapturedBytes (RealCacheConformanceTests.cs:59)
  - ArchiveCrcs_MatchTheCapturedContainerBytes (:119)
  - Containers_PreserveTheirPayloadAndHeaderAcrossReEncode (:169)
  - Archives_ReEncodeToTheCapturedPayloadBytes (:218)
  - IndexRecords_ReEncodeToTheCapturedBytes (:479)
and with only 2 archives, index 24 is examined in FULL even in sampled mode (RealCacheFixture.cs:122-134, `SampleArchivesPerIndex = 250` at :24). So the editor can already read idx24's two groups and split them into the 1299 files byte-identically - it simply cannot interpret a single record. That is infrastructure, not index-24 support.

Grade: none.

## Gaps

- QuickChatMenuDefinition class with Decode/Encode, in FlashEditor/Definitions/, implementing IDefinition: opcode 1 = cp1252 NUL-terminated caption, opcode 2 = u8 count then count x {u16 id, i8 hotkey} submenu links, opcode 3 = same shape for message links, opcode 0 terminator. Reads group 0 of index 24 (211 records).
- QuickChatMessageDefinition class with Decode/Encode: opcode 1 = cp1252 NUL-terminated template split on '<', opcode 2 = u8 count then count x u16 related ids, opcode 3 = u8 count then per entry {u16 slotTypeId, then N x u16 where N comes from the client's Class348 table}, opcode 4 = flag with no payload, opcode 0 terminator. Reads group 1 of index 24 (1088 records).
- A hardcoded 14-entry slot-type table ported from the client (Class93_Sub1.java:236-240 plus the 14 `new Class348(...)` sites), mapping type id -> extra u16 word count: 0->1, 1->0, 2->0, 4->1, 6->2, 7->1, 8->1, 9->1, 10->0, 11->2, 12->0, 13->0, 14->1, 15->0. Ids 3 and 5 do not exist. Without this the opcode-3 payload length is not computable and the parser desyncs.
- A codec test against captured bytes (the pattern of ObjectDefinitionCodecTests.cs / NPCDefinitionCodecTests.cs), not a self-round-trip - CLAUDE.md is explicit that round-tripping this encoder against this decoder proves nothing.
- A full-index byte-identity sweep in the RealCache* style: decode and re-encode all 211 menu records and all 1088 message records and assert each is byte-identical to the file it came from, plus an exact-consumption assertion. 1299 records total, and I have already confirmed all 1299 decode with exact consumption and zero failures, so this sweep will go green as soon as the encoders are written.
- A Quick Chat editor tab following the Editor.Designer.cs pattern, added to the `editorTypes` array at Editor.cs:64-76 (that array's order must match the tab layout). Natural UI: a tree built from the menu links (root 85 'Quick Chat'), with the message templates and their slot specs editable in a detail pane.
- Index 25 support alongside it - it is the same format and the 0x8000 half of the same id space, so a menu/message editor that ignores it is only half a quick-chat editor.

## Notes and traps

TRAPS FOR THE IMPLEMENTER

1. The slot-type word count lives in the CLIENT, not the cache. Opcode 3 of a message record is variable-length and its length is decided by a 14-entry table hardcoded in the 637 client (Class93_Sub1.method906, Class93_Sub1.java:236-240). There is nothing in index 24 or any other index that tells you how many u16 words follow a slot type id. Port the table verbatim. Critically, the client's behaviour on an UNKNOWN type id (Node_Sub46_Sub11.java:144 `if(class348 != null)`) is to read NO extra words and drop the slot - so an unknown type is not an error, it is a defined length of zero, and a decoder that guesses otherwise desyncs. In this cache the question is moot: 12 distinct type ids occur (0,1,4,6,7,8,9,10,11,13,14,15), all 12 are in the client's table, zero unknown references. Types 3 and 5 exist in neither.

2. Both group containers are BZip2, not GZip - and I verified they DO re-compress byte-identically through SharpZipLib's `BZip2OutputStream(ms, 1)`, which is exactly what CompressionUtils.Bzip2 (Utils/Compression.cs:105-113) uses. group 0: 4490 -> 4490 identical; group 1: 11597 -> 11597 identical. So index 24 is one of the friendly cases where a true end-to-end byte-identical save (including the stored container and therefore the CRC) is achievable, unlike anything GZip. Do not let the general "a re-encode is never byte-identical" note (AGENTS.md:131-149) talk you out of asserting container identity here - that note is about GZip. But note AGENTS.md records 19 of 1743 BZip2 containers cache-wide that do NOT round-trip; these two are not among them.

3. No name hashes. The table's flags byte is 0x00, so neither the two groups nor the 1299 files carry identifiers. Everything is addressed by id, and there is no name to recover. Do not build a name-based lookup.

4. Non-canonical encoding risk is LOW here, and I measured it rather than assuming. Across all 1299 records: opcode order is always strictly ascending, no opcode is ever repeated in a record, and every record consumes to the exact byte. Orders observed are only [1,2] [1,2,3] [1,3] for menus and [1] [1,2] [1,2,3] [1,3] for messages. So a fixed-ascending-order encoder should reproduce the bytes. Still record the order as decoded - CLAUDE.md's rule is that you assume non-canonical until a byte-identity sweep says otherwise, and the sweep does not exist yet.

5. Opcode 4 of the message record is dead in this cache (0 of 1088). Implement it anyway (it takes no payload), and do not delete it because no sweep exercises it - that is the exact shape CLAUDE.md warns about with the reference-table flags.

6. Index 25 is a hard dependency of the FORMAT but not of THIS cache's data. Any id with bit 0x8000 set belongs to index 25 (Class212.java:65-66, Class280.java:377-378). I checked: no idx24 record references it - op2 ids max out at 210 and op3 ids at 1087, both strictly inside index 24's own ranges. So an index-24-only decoder is complete for this cache's data, but an editor that lets a user type an id must handle the 0x8000 namespace or it will silently point at the wrong bank.

7. Editing index 24 changes what the client sends at login. Node_Sub9.java:58 writes `Class81.aJS5Archive_622.method2735(...)` into the login/handshake block, and method2735 (JS5Archive.java:638-653) returns the reference table's CRC (VersionTable.anInt2677, computed at VersionTable.java:86). A server that validates that CRC will reject a client whose index 24 you have edited. Same is true of index 25 at Node_Sub9.java:59.

8. Strings are cp1252, NUL-terminated (RSBuffer.java:878-894 scans to a zero byte; the decode is Node_Sub46_Sub6.method1546). Not length-prefixed, not UTF-8. On .NET you must call `Encoding.RegisterProvider(CodePagesEncodingProvider.Instance)` before `Encoding.GetEncoding(1252)` or it throws.

9. The '<' in a message template is a SLOT marker, not literal text (Node_Sub46_Sub11.java:132 splits on it into N+1 segments for N slots). A GUI that shows the raw string is fine; a GUI that lets a user add or remove a '<' without adding or removing the matching opcode-3 entry will produce a record the client renders wrong or crashes on (method1576 at :101-127 indexes the slot arrays by segment position).

10. The reference table for index 24 consumes to the byte with zero trailing bytes, so it is NOT one of the four tail-carrying indexes (9, 26, 27, 29). Nothing special needed there.

VERIFICATION METHOD. Everything measured above came from reading the cache read-only via a throwaway script in the scratchpad (idx255 entry 24 -> dat2 sector chain -> BZip2 -> archive split -> per-record opcode walk). No repo file was touched, no dotnet build or test was run, and nothing was written to the cache. A PowerShell gotcha worth passing on: `-shl` on a `[byte]` operates at byte width and silently truncates, so `($b[$o] -shl 16)` returns garbage - cast to `[int]` first. That cost one wrong reference-table parse.
