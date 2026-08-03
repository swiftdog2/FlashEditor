# Index 2 - CONFIG

**Format:** partially-understood  
**Capability:** partial-read  
**Effort:** very-large

## What it is

Index 2 is the client's general config store - client.BIT_CONFIG, opened at InterfaceSettings.java:160 as openFileStore(-116, false, 1, 2). It is the only index whose GROUPS are types rather than instances.

GROUP = one config TYPE; the group id is hard-coded in the client, passed as arg 2 to JS5Archive.getChildsInFolder(0, <group>) in each type provider's constructor. FILE = one RECORD; the definition id IS the file id (getChildFromFolder(groupId,fileId) -> getDecryptedFile(null,5,fileId,groupId), JS5Archive.java:203-205). RECORD = an opcode stream terminated by a zero byte, no length prefix and no count header. Every index-2 decoder in the 637 client is the same loop - read unsigned byte, break on 0, dispatch: Class167.java:104-114 ((op ^ 0xffffffff) == -1 is op == 0), Class9.java:214-225, Class294.java:176-187, Class220.java:56-65, InterfaceConfig.java:82-89, Class90.java:95-100, Class231.java:160-165, Class86.java:312-317, Node_Sub46_Sub18.java:31-37.

MEASURED (I parsed idx255 group 2 directly): idx2 is 294 bytes = 49 slots, but the table declares only 35 groups holding 16,981 files. Slot 0 holds length 0xFF0000 / sector 0 (dead); 13 more slots are all-zero. Format 6, version 785, flags 0x00 - no name hashes, zero trailing bytes. Containers: 23 GZip, 8 BZip2, 4 stored, all with a 2-byte version trailer.

Group:files - 1:159, 2:394, 3:652, 4:235, 5:609, 7:337, 11:1330, 15:345, 16:2002, 18:1198, 19:1445, 20:103, 21:9, 22:1227, 23:269, 24:11, 25:377, 26:1730, 31:4, 32:1972, 33:175, 34:100, 35:187, 36:1051, 37:13, 38:1, 39:1, 40:2, 41:323, 42:95, 43:185, 44:398, 45:13, 46:28, 48:1.

Client providers located (getChildsInFolder site -> record class): 1 Class153.java:201 -> FloorUnderlay; 3 Class83.java:158 -> Class152; 4 Class32.java:79 -> FloorOverlayConfig; 5 Class8.java:163 -> Node_Sub46_Sub18; 11 Class365.java:102 -> Class149; 15 Class239.java:75; 16 Class139.java:19 -> Class167; 19 Class132.java:117 -> Class90; 26 Class264.java:67 -> InterfaceConfig; 29 Class59.java:64; 30 Class115.java:53; 31 Class269.java:161 -> Class379; 32 Class257.java:82 -> Class294; 33 Class11.java:33 -> Class231; 34 Class335.java:61 -> Class9; 35 Class13.java:123 -> Class220; 36 Class341.java:141 -> Class24; 46 Class121.java:102 -> Class86. Groups 29 and 30 have providers but do not exist in this cache. 17 groups in the cache have no client provider at all (2, 7, 18, 20, 21, 22, 23, 24, 25, 37-45, 48) - server-side config types the client never opens.

## Current capability

Three of the 35 groups have a full decoder + encoder + byte-identity sweep. The other 32 have nothing above the raw container layer.

DONE (record level):
- Group 1, floor underlay - FlashEditor/Definitions/FloorUnderlayDefinition.cs, reached via RSCache.GetFloorUnderlay (Cache/RSCache.cs:711-714).
- Group 4, floor overlay - FlashEditor/Definitions/FloorOverlayDefinition.cs (Decode :92, Encode :194), via RSCache.cs:725-728.
- Group 34, map scene icon - FlashEditor/Definitions/MapSceneIconDefinition.cs (Decode :50, Encode :94), via RSCache.cs:740-743.
Sweeps: FlashEditor.Tests/Cache/RealCacheFloorDefinitionTests.cs:35 and :73 assert exact buffer consumption plus byte-identical re-encode over all 159 underlays and all 235 overlays; FlashEditor.Tests/Map/RealCacheMapIconTests.cs:30 does the same for all 100 map scene icons. That is 494 of 16,981 files, 2.9%.

Group enumeration helper: RSCache.GetConfigFileIds (RSCache.cs:793-797) returns the real file ids from the reference table.

CONTAINER/ARCHIVE LEVEL (index-agnostic, but it does cover index 2 completely): RealCacheConformanceTests.Archives_ReEncodeToTheCapturedPayloadBytes:218, Containers_PreserveTheirPayloadAndHeaderAcrossReEncode:169, ArchiveCrcs_MatchTheCapturedContainerBytes:119, UnchangedArchives_SurviveTheEditPathWithTheirPayloadIntact:295 and IndexRecords_ReEncodeToTheCapturedBytes:479 all iterate _cache.TableIndexes. Index 2 has 35 groups and RealCacheFixture.SampleArchivesPerIndex is 250 (RealCacheFixture.cs:24,125), so all 35 groups are swept on every run, FULL=1 or not. So we can read and rewrite any index-2 file as opaque bytes today; we just cannot interpret 32 of the 35 record formats.

GUI: none. No config tab exists in Editor.Designer.cs (tabs are Item, Sprite, NPC, Object, Interface, ModelViewer, TextureViewer, MapEditor, TrackEditor, plus generic Reference Tables / Containers). Editor.cs only ever calls WriteFile for indexes 19, 16 and 18 (Editor.cs:944,966,986). The only consumer of index 2 in the app is the map renderer, read-only: MapRasteriser.cs:740 (underlay), :758 (overlay), :900 (map scene icon). RSConstants.MAP_ELEMENT_GROUP (=36) is declared at RSConstants.cs:63 and referenced nowhere.

## Gaps

- Record decoders/encoders for the other 32 groups - 16,487 of 16,981 files. The 18 with a client provider can be ported from the classes named above; the 17 with no provider (groups 2, 7, 18, 20, 21, 22, 23, 24, 25, 37-45, 48) have no 637 reference at all and must be reverse-engineered from the bytes.
- A definition class per group following the FloorOverlayDefinition pattern: typed properties, a DecodedOpcodes list, Decode, Encode replaying decoded opcodes in order with IsLastOccurrence, and an AddedOpcodes enumerator for edited fields.
- A byte-identity sweep per group in the shape of RealCacheFloorDefinitionTests - exact buffer consumption plus re-encode equality over every file id from GetConfigFileIds, asserting the measured count.
- A codec test against captured bytes for each new type. The existing floor tests compare against cache bytes, which satisfies CLAUDE.md's rule that a round trip against our own encoder proves nothing.
- RSCache accessors (GetX(int id)) per type, mirroring GetFloorUnderlay/GetFloorOverlay/GetMapSceneIcon.
- A Config editor tab in Editor.Designer.cs following the ItemEditorTab/ObjectEditorTab pattern: group selector, ObjectListView of records, property grid, and a save that calls RSCache.WriteFile(RSConstants.CONFIG, group, fileId, ...). Nothing writes index 2 today.
- Named constants for the remaining group ids in RSConstants.cs; only 1, 4, 34 and 36 exist.

## Notes and traps

TRAPS

1. 49 idx slots, 35 real groups. Iterate the reference table, never 0..48. Slot 0's length is 0xFF0000 with sector 0 - reading it blind asks for a 16 MB sector chain. RSConstants.MAX_VALID_ARCHIVE_LENGTH (=1,000,000) exists but is referenced nowhere in production.

2. File ids are NOT contiguous in 8 of 35 groups: 11, 16, 19, 22, 32, 34, 41, 43 (e.g. group 16 holds 2002 files with max id 2050; group 34 holds 100 with max id 100). Loop over GetConfigFileIds, never over 0..count-1. The existing tests do this correctly.

3. Non-canonical encodings are already proven here, so assume more. Floor overlay 94 emits opcode 11 twice, 255 then 127 - pinned by RealCacheFloorDefinitionTests.RepeatedOpcodeTakesTheLastValue:127. Opcode order within a record varies. FloorOverlayDefinition solves both by replaying DecodedOpcodes in order and only letting the last occurrence pick up an edit (FloorOverlayDefinition.cs:200-204). Copy that structure; do not decode to values alone.

4. Absent versus default. FloorOverlayDefinition carries HasPrimaryRgb because an absent opcode 1 is not the same as an explicit black, and TextureIdIsShortForm because opcodes 2 and 3 write the same field in different widths (opcode 2 has zero occurrences in this cache, so the byte form is untested by the sweep).

5. CLIENT BUG, deliberate divergence: the client's opcode dispatchers silently ignore an unknown opcode without consuming its payload, which desynchronises everything after it. Floor overlay opcodes 4, 6 and 15 fall through this way. Our decoders throw instead (FloorOverlayDefinition.cs:147-151, MapSceneIconDefinition.cs:71-75) and RealCacheFloorDefinitionTests.UnknownOpcodesAreRejected:189 pins it. Keep that convention for new types.

6. Post-decode transforms are not part of the format. FloorOverlayConfig.method2691 folds the definition id into the priority; ApplyPriorityComposite (FloorOverlayDefinition.cs:190) is deliberately NOT called from Decode, or Encode would write the composite back. FloorUnderlay decomposes its RGB into four HSL accumulators inside opcode 1 (FloorUnderlay.java:112-134); we keep raw RGB so the round trip survives and leave the conversion to the renderer.

7. Compression: 23 of the 35 containers are GZip, so a container re-encode is never byte-identical (AGENTS.md, 0 of 96,183). Compare decompressed payloads. Nothing in index 2 is XTEA encrypted - only index 5 'l' groups are.

8. No name hashes (flags 0x00), pinned by RealCacheReferenceTableShapeTests.cs:112, and zero trailing bytes (:263). Groups are addressable by numeric id only, so the name-recovery trick that works on indexes 3/5/6/etc is unavailable here.

9. RSConstants.cs:135-187 carries a commented archive listing that is NOT 639. It describes a later revision - it lists archives up to 80 (max here is 48) and calls archive 16 "Empty (Pre 745: Var Player)" while this cache has 2002 files there. Treat every line of it as a claim. Where I could check it against the client it was roughly right about which types exist, but the "Empty" annotations are wrong for 639.

10. Semantic names in the deob are claims. FloorUnderlay, FloorOverlayConfig and InterfaceConfig are the only index-2 record classes someone has named; the other 15 located providers return ClassNNN. Group 16's record (Class167.java:127-133) has exactly one opcode, 5, reading an unsigned short - a single-field record, consistent with VarPlayer but not proven by the client's usage. Settle each from what the client does with the field, not from the class name.

11. Cross-index dependencies. Group 4 overlays reference index 9 texture ids (TextureManager resolves the colour, RealCacheMapIconTests.cs:123). Group 34 icons reference index 8 sprite GROUP ids, drawing file 0 of that group (Class324.java:34-36). Object definitions in index 16 point at group 34 via opcode 102, not 68 - RealCacheMapIconTests.ObjectsWithoutTheOpcodeReportNoIcon:76 exists because that field once defaulted to 0 and put an icon on all 21,665 locations in a scene.

12. Group 36 is world-map data (Class341 -> Class24, whose getter method3807 carries an in-source "// Map Loading" comment). CLAUDE.md warns that a method's own comment is unreliable; I have not confirmed group 36's contents from usage, so treat MAP_ELEMENT_GROUP as unverified.
