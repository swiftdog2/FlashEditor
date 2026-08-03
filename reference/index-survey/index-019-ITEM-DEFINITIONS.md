# Index 19 - ITEM_DEFINITIONS

**Format:** fully-understood  
**Capability:** complete  
**Effort:** trivial

## What it is

Item ("obj") definitions - one self-delimiting opcode stream per item.

CLIENT AUTHORITY: InterfaceSettings.java:175 opens index 19 as Class208.aJS5Archive_1581 = Class42_Sub3.openFileStore(-62, false, 1, 19). That handle is passed to new Class205(...) at InterfaceSettings.java:279-280, and Class205.getItemByID(int itemID) (Class205.java:202) fetches the record with itemDefArchive.getChildFromFolder(Class150.method2437(itemID), Class119_Sub3.method2187(itemID)) at Class205.java:216-217, where Class150.java:31 is itemID >>> 8 and Class119_Sub3.java:75 is itemID & 0xff. So GROUP = itemId >> 8, FILE = itemId & 0xff, one file = one item record. Class205.java:186-187 derives the item count as getChildsInFolder(0, lastGroup) + 256 * lastGroup, confirming 256 items per group. The bytes go to ItemDefinition.unpackItemFile(RSBuffer) (ItemDefinition.java:1199-1211), a while ((opcode = readUnsignedByte()) != 0) loop dispatching to readValues (ItemDefinition.java:886).

MEASURED IN THIS CACHE, parsed straight out of idx255/dat2 with an independent Python parser rather than through our C# code:
- Reference table: format 6, version 692, flags 0x00, 80 groups (ids 0..79 contiguous), 20,470 files, consumes to the byte with no trailing tail.
- flags 0x00 means no name hashes, so an item group is addressable only by id.
- File counts per group: 256 for most; group 57 has 254 (file ids 102 and 103 absent), group 78 has 255 (83 absent), group 79 has 249. Highest item id is 20472 although only 20,470 records exist.
- Containers: 69 groups GZip, 11 BZip2 (groups 31, 32, 36, 53, 61, 62, 63, 65, 66, 67, 70). All carry a 2-byte version trailer. Chunk count is 1 for all 80. No XTEA anywhere in this index.
- main_file_cache.idx19 is 810 bytes = 135 slots, but slots 80..134 are all zero. The group count is 80, not 135 - do not infer it from the idx file size here.
- Independently decoding all 20,470 records with the FlashEditor payload sizes: every one consumes its buffer exactly, none is empty. Opcodes occurring: 1, 2, 4, 5, 6, 7, 8, 11, 12, 16, 23, 24, 25, 26, 30, 32, 33, 34, 35, 36, 37, 38, 39, 40, 41, 42, 65, 78, 79, 90, 91, 92, 93, 95, 96, 97, 98, 100-108, 110, 111, 112, 113, 114, 115, 121, 122, 125, 126, 127, 128, 129, 130, 132, 249. Every one is handled by ItemDefinition.cs.

## Current capability

Everything. Decode, encode, a full-index byte-identity sweep, and a GUI editing tab.

DECODER: FlashEditor/Definitions/ItemDefinition.cs:200 Decode(JagStream), dispatching at :233 DecodeOpcode over 69 opcodes. It records opcodeOrder (:34) and opcodePayloads (:44) so a non-canonical layout can be reproduced.

ENCODER: ItemDefinition.cs:445 Encode(), per-opcode emission at :512 EmitOpcode, opcode-249 block at :705 WriteParams. Replays the recorded opcode order first, then appends any field moved off its default that the record never carried.

BYTE-IDENTITY SWEEP: FlashEditor.Tests/Cache/RealCacheItemDefinitionTests.cs:149 AllItemDefinitions_ReEncodeToTheCapturedBytes asserts every record re-encodes to the bytes it was read from. It is a FULL sweep on every run, not only under FULL=1: it enumerates via _cache.ArchivesToExamine(table) (:548), and RealCacheFixture.cs:125 returns all archives when the count is <= SampleArchivesPerIndex, which is 250 (RealCacheFixture.cs:24) against this index's 80 archives. Backed by :66 AllItemDefinitions_DecodeAndConsumeTheirBufferExactly (exact-consumption plus a terminator check at :114 that rules out a truncated record landing on the end) and :215 AllItemDefinitions_EncodeIsAFixedPointOfDecode. Six cache-free regression tests pin the non-canonical cases: :278, :297, :342, :394, :420, :445, :465, :492.

GUI: the ItemEditorTab / ItemListView in Editor.Designer.cs:506, 574-700 - 14 columns, CellEditActivation = DoubleClick (:593), wired at :612-614. Load path Editor.cs:567-623; edit commit Editor.cs:914-947, which skips the write when the re-encode is unchanged (:931) and otherwise calls cache.WriteFile(RSConstants.ITEM_DEFINITIONS_INDEX, id/256, id%256, ...) (:944). Read helper RSCache.cs:676 GetItemDefinition, addressing archiveId*256+fileId (:679), matching the client. Also has a per-item .dat export (Editor.cs:991-1037) and a 3-D preview that applies opcode-40 recolour and opcode-41 retexture to the inventory model (Editor.cs:1344-1362, 1406-1434).

The opcode semantics are separately cross-checked against the 637 client in reference/hydra-637-definitions/item-opcodes.md, 69 rows, each citing a client file:line.

## Gaps

- Nothing is missing for 'complete' - decoder, encoder, whole-index byte-identity sweep and GUI editing all exist. What follows is residual polish, not a gap in capability.

## Notes and traps

TRAP 1 - THE DOCS ARE WRONG ABOUT OPCODE 131, AND IT IS THE EXAMPLE THEY LEAN ON. CLAUDE.md, AGENTS.md:371 and reference/hydra-637-definitions/item-opcodes.md all assert "item opcode 131 occurs in this cache and the 637 client has no handler for it", and use it as the flagship case for "the data vetoes the client". I decoded all 20,470 records in C:/Users/CJ/Desktop/FlashEditor/cache with an independent parser using our own payload sizes: opcode 131 occurs at an opcode boundary ZERO times. Byte 0x83 appears 762 times, all as payload data. The same sweep reproduces every other absence the doc claims (no 18, no 31, no 109, no 134, no 139, no 140) and lands exactly on the end of all 20,470 buffers, so the parser is sound. Do NOT delete the 131 handler on the strength of this (harmless, and a later build may carry it), but the claim needs restating or re-measuring on the maintainer's copy before it is cited again.

TRAP 2 - NON-CANONICAL ENCODINGS, all already handled, all easy to break. Measured here: 15,433 of 20,470 records store their opcodes in non-ascending order, and 267 records repeat an opcode. Plus explicitly-stored defaults (opcode 12 = 1, opcode 4 = 2000), the seeded "take"/"drop" menu entries the decoder plants (ItemDefinition.cs:54-64, compared against DefaultGroundOptions in EmitOpcode :547 rather than against null), and repeated opcode-249 keys the SortedDictionary collapses (kept in itemParamEntries :173). Bare flags 11/16/65 are deliberately rebuilt from the field rather than replayed, so clearing one removes every occurrence (ItemDefinition.cs:469-480, pinned at RealCacheItemDefinitionTests.cs:492).

TRAP 3 - LOSSY FIELD SCALING. Opcode 114 decodes as ReadSignedByte()*5 (:340) and encodes as /5 (:633); opcodes 125/126 decode <<2 (:351-359) and encode >>2 (:644-655). Round-trip-safe only while the value stays a multiple of the scale. Neither is a GUI column today, but any new editor field for contrast or wear offsets must snap to the scale or it silently truncates.

TRAP 4 - TWO GUI COLUMNS ARE NOT IN THE FORMAT. equipSlotColumn and equipIdColumn (Editor.Designer.cs:686, 691) bind equipSlotId / equipId, declared at ItemDefinition.cs:97-100 as "UI binding, not in rev 639 cache". No opcode reads or writes them, so editing those cells changes the row, produces identical bytes, and Editor.cs:931 correctly writes nothing. It looks like a silently failing save. Only 13 of ~70 decodable fields are exposed at all.

TRAP 5 - NAMING, NOT BYTES. Opcodes 90-93 are mis-paired in our field names: the client builds chatheads {90,92} and {91,93} (method3486:141-147), so maleHeadModel2 is really female head 1 and femaleHeadModel1 is really male head 2. Also texturePriorities (opcode 42) is a palette-index override, not a priority table, and unnoted (opcode 65) is the GE-search flag. All naming only; the wire reads are correct.

TRAP 6 - BZIP2 GROUPS. 11 of the 80 item groups are BZip2. AGENTS.md records BZip2 re-encoding as 1,724 of 1,743 byte-identical, so a save that touches one of those groups has a small chance of a container that does not reproduce. Item byte-identity is asserted on the decompressed file payload, so the sweep cannot see this.

TRAP 7 - THE 0..255 LOAD LOOP. Editor.cs:593 iterates file 0..255 for every archive regardless of the reference table and relies on RSCache.ReadFile throwing FileNotFoundException (RSCache.cs:604-605), caught at Editor.cs:600. Correct but noisy: 10 throws per load (80*256 - 20,470), from the gaps in groups 57, 78 and 79.

MINOR: ItemDefinition.Decode has a safety bail at :219 after 256 opcodes, which would silently truncate a longer record; the real maximum here is far below that. The class comment at ItemDefinition.cs:9 credits a "rev 640 client" while the verified reference is the bundled 637 client.

The 80-group / 20,470-file figures match AGENTS.md:293 exactly, so this cache directory is the reference cache those counts came from.
