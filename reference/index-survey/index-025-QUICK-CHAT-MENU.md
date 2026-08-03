# Index 25 - QUICK_CHAT_MENU (misnamed - it is the second complete quick-chat bank, the 0x8000 one)

**Format:** fully-understood  
**Capability:** none  
**Effort:** small

## What it is

Index 25 is one of TWO complete quick-chat banks (24 is the other). It is NOT "the menus while 24 holds the messages" - each index carries both halves, split by GROUP: group 0 = menu-tree nodes, group 1 = message templates. Client proof: InterfaceSettings.java:181 opens index 25 into Class322.aJS5Archive_2714, and passes it as the SECOND archive to both loaders alongside index 24 (:180) - Class212 the menu loader (constructed :297-298, reads folder 0 at Class212.java:41,46,66,71) and Class280 the message loader (constructed :299-300, reads folder 1 at Class280.java:349,354,378,383). Which index serves a request is decided by the id: id >= 32768 goes to index 25 with the file id masked to id & 0x7fff (Class212.java:65-66, Class280.java:377-378). Ids stored INSIDE a record loaded from 25 get 0x8000 OR'd back on (Node_Sub46_Sub1.java:115,122 and Node_Sub46_Sub11.java:84, both calling Class41.method366 which is `i | i_0_` at Class41.java:28), so index 25's id graph is self-contained.

MEASURED from this cache (my own read-only parse of idx255/idx25/dat2, not inferred): idx255 group 25 is format 6, version 6, flags 0x00, 2 groups, group versions [6,5], file counts [17, 69], file ids contiguous 0..16 and 0..68, and the table consumes exactly 204 of 204 bytes (no trailing block). Group 0: stored 375 bytes, GZip, uncompressed 528, 17 files, chunks=1, version trailer 0x0006. Group 1: stored 807 bytes, BZip2, uncompressed 1630, 69 files, chunks=1, version trailer 0x0005. So it is 86 SMALL RECORDS of 6-38 bytes each, not a few large blobs. A group is a bank half; a file is one record; one record is either one menu node (name + shortcut-keyed child list) or one message template.

Content, decoded and cross-verified: this is the FunOrb / lobby / cross-game bank. Menu 1 is the root "Quick Chat", children 2 "General" ('g') and 12 "Inter-game" ('m'); leaves "Responses", "Hello", "Goodbye", "Mood", "Smileys", "Banter", "Basics", "Taunts", "Ripostes", "FunOrb", "Games", "RuneScape", "Activities". Messages are "Hello!", "Hi.", ":-)", "I won!", "Fear my leet skills.", "Let's play Arcanists.", "Let's play Stealing Creation." Index 24 for contrast holds 211 menus + 1088 messages: the main in-game RuneScape bank.

## Current capability

Nothing index-specific. `RSConstants.QUICK_CHAT_MENU = 25` (FlashEditor/Cache/RSConstants.cs:40) and its display name (:90) are the ONLY two references to index 25 anywhere in FlashEditor/ - a grep for QUICK_CHAT across the whole repo returns exactly those two plus the AGENTS.md rows. There is no definition class, no Decode/Encode, no exporter, no record-level test.

The GUI cannot even open it: Editor.cs:64-76 hardcodes `editorTypes` to nine indexes (items, sprites, NPCs, objects, interfaces, models, textures, maps, music) and 25 is not among them, so no tab ever calls LoadIndex on it.

What DOES cover index 25, and it is worth being precise rather than claiming zero: the generic cache-layer sweeps in FlashEditor.Tests/Cache/RealCacheConformanceTests.cs iterate `_cache.TableIndexes`, which is every meta group - ReferenceTables_ReEncodeToTheCapturedBytes (:66), ArchiveCrcs_MatchTheCapturedContainerBytes (:126), Containers_PreserveTheirPayloadAndHeaderAcrossReEncode (:175) and Archives_ReEncodeToTheCapturedPayloadBytes (:226). Index 25 has 2 archives and RealCacheFixture.ArchivesToExamine (Cache/RealCache/RealCacheFixture.cs:122) returns all of them when the count is at or below the sample size, so BOTH groups of index 25 are byte-identity swept on every run, sampled or full. That pins the container header, the XTEA-absent path, the 17-file and 69-file archive splits and the reference table - it does not touch a single record byte. That is generic infrastructure, not index-25 capability.

## Gaps

- A QuickChatMenuDefinition class with Decode/Encode. Opcodes, from Node_Sub46_Sub1.method1527/method1532: 0 terminator; 1 = cp1252 NUL-terminated name (:61); 2 = u8 count then count x (u16 submenu id, i8 shortcut char) (:41-49); 3 = u8 count then count x (u16 message id, i8 shortcut char) (:51-58); 4 = zero-payload flag that the 637 client does not handle at all. Char byte 0 means no shortcut, otherwise cp1252 via Class64_Sub7.method576.
- A QuickChatMessageDefinition class with Decode/Encode. Opcodes, from Node_Sub46_Sub11.method1578/method1584: 0 terminator; 1 = cp1252 string, the template, which the client splits on '<' at :132 (store it raw); 2 = u8 count then count x u16 suggested-response message ids (:157-163); 3 = u8 count then count x (u16 paramTypeId + N x u16) where N comes from a client-hardcoded table; 4 = zero-payload flag clearing a bool (:154). Opcodes 3 and 4 do NOT occur anywhere in index 25 (measured histogram for group 1 is {1:69, 2:11}), but both occur in index 24, so a shared codec needs them.
- The Class348 parameter table ported as a constant, needed only if the codec is shared with index 24. It is hardcoded in the client, not in the cache: Class93_Sub1.method906:236-240 returns 14 instances, constructed at Class348.java:72-80. Mapping paramTypeId -> trailing u16 count (anInt2915): 0->1, 1->0, 2->0, 4->1, 6->2, 7->1, 8->1, 9->1, 10->0, 11->2, 12->0, 13->0, 14->1, 15->0. Ids 3 and 5 do not exist. Index 24 uses ids {0,1,4,6,7,8,9,10,11,13,14,15} and every one of its 1088 message files consumes exactly, so the 639 data confirms the 637 table.
- Opcode-order and repetition capture in the DecodedOpcode style (FlashEditor/Definitions/DecodedOpcode.cs) - required, not optional, see traps.
- A codec test against captured bytes for a handful of records, in the shape of ObjectDefinitionCodecTests.cs / NPCDefinitionCodecTests.cs.
- A full-index byte-identity sweep - all 17 menu records and all 69 message records must re-encode to the exact bytes read, in the shape of RealCacheFloorDefinitionTests.cs. Cheap: 86 records, 1989 bytes of payload total.
- A GUI tab in the Editor.Designer.cs pattern, plus adding RSConstants.QUICK_CHAT_MENU (and 24) to the editorTypes array at Editor.cs:64-76 - without that entry no tab can load the index at all. Natural shape is a tree view of group 0 with the message list of the selected node from group 1.

## Notes and traps

TRAPS, in the order they will bite.

1. MENU OPCODE 4 IS IN THE 639 DATA AND THE 637 CLIENT HAS NO HANDLER. Group 0 file 1 is `04 01 "Quick Chat" 00 02 02 00 02 'g' 00 0c 'm' 00`. Node_Sub46_Sub1.method1527 tests only 1, 2 and 3; anything else falls through reading nothing, so 637 treats it as a no-op and the record still parses to the end. This is exactly the item-opcode-131 case CLAUDE.md describes: keep a zero-payload handler, do not "match the client" by deleting it. Its meaning is unknown; file 1 is the root menu, but one occurrence is not evidence.

2. NON-CANONICAL OPCODE ORDER, ALREADY PRESENT. File 1's order is [4, 1, 2]; all 16 other menu records start with 1. A decoder that re-emits a fixed order reproduces 16 of 17 files and corrupts the root menu. Record the sequence (DecodedOpcode.cs exists for precisely this).

3. THE MESSAGE OPCODE 3 PAYLOAD LENGTH IS NOT IN THE FILE. It is `Class348.anInt2915`, hardcoded in the client. An unknown type id makes the record unparseable - and note what the client does at Node_Sub46_Sub11.java:144, where a null lookup skips the inner u16 loop entirely rather than skipping the right number of bytes, so an unrecognised type silently desyncs the rest of the record. Not reachable from index 25 (zero occurrences), fatal for index 24.

4. COMPRESSION DIFFERS BETWEEN THE TWO GROUPS: group 0 is GZip, group 1 is BZip2. Never compare stored containers to decide whether anything changed (GZip re-encodes 0 of 96,183 identical). Worse, both groups sit in one reference table, so rewriting either changes that table and therefore the entry of the other - a menu edit dirties the message group's table row.

5. A DANGLING ID SHIPS IN THE CACHE. Menu 16 "Activities" lists message ids 59, 60, 61, 63, 64 and 69, but group 1 holds ids 0..68 only. Id 69 resolves to nothing (the client gets null from getChildFromFolder and builds an empty record). Any validator that asserts every referenced id resolves fails on the untouched cache. Ids 62, 67 and 68 are likewise present in group 1 but referenced by no menu.

6. THE 0x8000 CONVENTION IS AN EDITOR HAZARD. Ids inside index 25 records are stored WITHOUT the high bit and the client ORs it back at load. If the UI shows global quick-chat ids it must strip 0x8000 before writing, and it must never write a value >= 0x8000 into an index-25 record.

7. NO NAMES EXIST. Table flags are 0x00, so neither groups nor files carry identifier hashes; records are addressable by id alone and there is nothing to hash-recover. The menu's own opcode-1 string is the only human label.

8. DECOMPILER ARTEFACT - READ IT AS AN ELSE. `if(!client.aBoolean3553) break;` inside the do/while(false) at Class212.java:67 and Class280.java:379 is the obfuscator's rendering of the else branch; client.java:2842 sets the field true. The real semantics are if(id >= 32768) read index 25 with id & 0x7fff else read index 24 with id. Transcribing the decompiled control flow literally makes every lookup fall through to index 24.

9. STRINGS ARE cp1252 AND NUL-TERMINATED. Message templates carry '<' placeholders that the client splits on (Class112.method2142 at :132); keep the raw string and let the viewer do the splitting, or a rejoin has to reproduce the separator exactly.

WHY THE JOIN IS SAFE TO TRUST (CLAUDE.md's coverage-is-not-correctness rule): this is not an aggregate match, it is self-proving row by row. Menu 2 "General" opcode 2 lists (3,'r') (4,'h') (5,'g') (6,'m') (7,'s') (8,'b') and menu records 3..8 are literally "Responses", "Hello", "Goodbye", "Mood", "Smileys", "Banter" - the shortcut letter matches the first letter of the target name in all six. Menu 4 "Hello" opcode 3 lists 1..5 and message records 1..5 are "Hello!", "Hi.", "Good day.", "Nice to meet you.", "How are you?". Message 5 "How are you?" opcode 2 lists 18,19,20,21 = "I'm great!", "I'm good.", "I'm okay.", "Meh." So opcode 2 on a menu is submenus, opcode 3 on a menu is messages, and opcode 2 on a message is the suggested replies. All 17 menu records and all 69 message records consume to the byte under this reading.

BUILD IT FOR BOTH INDEXES AT ONCE. 24 and 25 are the same two formats; doing 25 alone leaves the far larger bank (211 + 1088 records) one shared codec away, and index 24 is the one that exercises message opcodes 3 and 4.

Scripts I used are in the scratchpad and are read-only against the cache: C:\Users\CJ\AppData\Local\Temp\claude\C--Users-CJ-Desktop-FlashEditor\f188415d-792b-47d7-bdca-e00fd5387036\scratchpad\idx25.py and decode25.py.
