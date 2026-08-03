# Index 29 - CONFIG_BILLBOARD

**Format:** fully-understood  
**Capability:** none  
**Effort:** small

## What it is

Billboard definitions - the constant name is CORRECT, unlike five others in the index map. One group (id 0) holding 182 files with dense ids 0..181; a file is one record, 11-16 bytes, opcode-terminated by 0. There is no per-record header and no name hashing (table flags 0x00), so a record is addressable only by its file id.

The client opens index 29 at InterfaceSettings.java:185 into Class111_Sub3.aJS5Archive_4715, hands it to RenderType_Sub3.aJS5Archive_4528 via Class253.java:394-396, and reads it at Class177.java:21 as getChildFromFolder(0, id) - literally group 0, file = billboard id. Decode loop is Class177.method2586 (:153-171), payload switch is Class177.method2583 (:110-150).

Opcode table (Class177.java:110-150), with measured occurrence over all 182 files in this cache:
  0     terminator
  1     u16 material id, 65535 -> -1. Indexes index 26's material table (Class260.method11, Class260.java:233). Present 182/182.
  2     u16 width-1, u16 height-1 (both defaulting to 64). Present 182/182.
  3     i8, READ AND DISCARDED by the 637 client - no assignment at Class177.java:126. Present 113/182; values 0 (x98), 5, 6, 10, 20, 25, 30, 32, 50, 100.
  4     u8, default 2. Raster/blend mode. Present 182/182, always 1 here.
  5     u8, default 1. Colour-combine mode. Present 170/182; values 0 (x165), 1 (x2), 3 (x3).
  6     flag, default false. Present 0/182.
  7     flag, default false. Present 171/182.

What a record means, settled by what the client does with it, not by names. Renderable_Sub1.java:211-224 pairs one Class177 with the per-model attachment array Class106[] (decoded in Model.java:789-800 and 1324-1341 as billboardId u16, faceId u16, distance u8/smart, depthBias i8) to build a Class170. Renderable_Sub1.java:2044-2070 then places it at the CENTROID of the referenced face's three vertices and sizes it in screen space as halfW = scale * width * fov / (z * 128), halfH likewise from height - so opcode 2 is a screen-space quad size in 1/128 units, not a texture dimension. Renderable_Sub1.java:3264-3273 draws it through RenderType_Sub2.method1923 (:2775-2800) -> Class332_Sub3_Sub2.method3757, whose raster loops branch on the opcode-4 value (0 = opaque copy, 1 = colour-key, skip texel 0; 2 = saturating additive) and on the opcode-5 value (0 = modulate by face colour, 1 = copy texel unmodified, 2 = alpha-scale, 3 = additive tint). Opcode 7 (aBoolean1377 -> Class170.aBoolean1314) suppresses the SOURCE FACE: Renderable_Sub1.java:3259 rasterises the triangle only when it is false, and Renderable_Sub2.java:449-453 drops the face from the draw list entirely. Opcode 6 reaches only the hardware path (Class249.aBoolean1904, Renderable_Sub2.java:4036) where it suppresses the billboard when the shader renderer is active.

In short: index 29 says "a quad of this size, this material, blended this way, replacing this face"; the model says "which billboard, on which face".

## Current capability

Nothing beyond generic cache plumbing. The ONLY two references to this index anywhere in FlashEditor are the constant declaration at FlashEditor/Cache/RSConstants.cs:44 and its display name at :94. No decoder, no definition class, no encoder, no GUI tab, no payload test.

What exists incidentally:
- FlashEditor/Cache/RSCache.cs:73 calls LoadReferenceTables (:541-553), which decodes index 29's reference table like every other, so the Meta tab (Editor.cs:526-557, ContainerListView.SetObjects at :557) lists it as a row: format 6, version 40, 1 group, 182 files, CRC 0xEBE2182F. That is metadata, not content.
- FlashEditor.Tests/Cache/RealCacheReferenceTableShapeTests.cs:180 and :248 pin index 29 as one of the four tables carrying trailing bytes (728 zero bytes, 4 per file over 182 files in 1 group). Again the table, never the payload.
- FlashEditor/Cache/RSCache.cs:102 WriteFile is index-agnostic, so the write plumbing would work the moment a definition class existed - but nothing calls it for 29.
- STATE_OF_THE_EDITOR.md:115 already records 27-35 as all dashes, which is accurate.

The 182 records themselves have never been decoded by this project. I verified they can be, with a read-only script over cache/main_file_cache.dat2: all 182 parse against the Class177 opcode set with exact consumption and zero failures, 0 unknown opcodes, all 46 distinct material ids in 735..1318 valid against index 26's 1408-entry table (Class260.java:105-107).

## Gaps

- A BillboardDefinition class in FlashEditor/Definitions/ with Decode(JagStream)/Encode(), modelled on FloorUnderlayDefinition.cs - same shape: seven opcodes, terminator 0, defaults material=-1, width=64, height=64, op4=2, op5=1, flags false. It must carry a DecodedOpcodes list (FlashEditor/Definitions/DecodedOpcode.cs) because the on-disk opcode ORDER is not canonical here.
- DecodedOpcode currently holds one int Value. Opcode 2 carries two u16s (width-1, height-1), so either widen the struct, record it as two entries, or emit opcode 2 from the fields at replay time as ObjectDefinition.cs:885-889 does with its Emit helper. Decide before writing the encoder, not after the sweep fails.
- A group-level codec: read index 29 group 0 as a 182-file archive and write it back. The RSArchive layer already handles the chunk-major split (chunks = 1 here), so this is a loop, not new format work.
- A codec test against captured bytes - the project rule is that round-tripping our encoder against our decoder proves nothing (CLAUDE.md). Use bytes lifted from the real group 0, or hand-assemble against the Class177 opcode table.
- A byte-identity sweep over all 182 files, in the pattern of RealCacheFloorDefinitionTests.cs:34-70 (EveryUnderlayDecodesAndRoundTrips), asserting 182 decoded and 182 re-encoding to the exact input bytes. Assert the count, not 'loaded + skipped == 182' - an or in the assertion is a hole.
- A Billboards tab following the Editor.Designer.cs TabPage pattern (declarations at :40-148, e.g. TrackEditorTab at :148) plus a case in the LoadCache switch at Editor.cs:524. A useful editor needs a material preview, which means resolving the opcode-1 id through index 26 and index 9 - the Textures tab work already in flight is the natural place to borrow that from.
- For the tab to show a billboard in context, ModelDefinition needs the per-model attachment array. It is not decoded today: ParticleEffectId and ParticleAnchorVert at ModelDefinition.cs:77-80 are declared and NEVER assigned anywhere in the file, and neither DecodeRS2 (:206-470) nor DecodeOld (:477) nor DecodeRS3 (:786) reads the Class106[] tail the client reads at Model.java:789-800 / 1324-1341. Strictly separable from index 29 itself - do the definition first.

## Notes and traps

TRAPS.

1. Opcode order is non-canonical, and badly so. Eight distinct orderings occur across 182 files and NONE is ascending - opcode 1 is written LAST in every single record. The orderings and counts: (2,3,4,5,7,1) x110, (2,4,5,7,1) x43, (2,4,7,5,1) x10, (2,4,7,1) x6, (2,4,1) x5, (2,4,5,1) x5, (2,3,5,4,7,1) x2, (2,3,4,1) x1. Note 4-then-5 versus 5-then-4 and 5-then-7 versus 7-then-5 both occur, so the order is not even derivable from a rule. An encoder that emits ascending opcodes reproduces 0 of 182 files. Record the sequence at decode and replay it, exactly as FloorUnderlayDefinition does.

2. No opcode repeats within any record, so the last-occurrence-wins machinery in DecodedOpcodeExtensions is not exercised here. Keep it anyway; do not conclude from a passing sweep that repetition is impossible.

3. Absent-versus-default is live. Opcode 5 is explicitly stored as 1 in two records, which is exactly its default, and opcode 3 is explicitly stored as 0 in 98 records. "Did this record store the field" cannot be inferred by comparing the value to the default.

4. Opcode 3 is a real field the 637 client THROWS AWAY - Class177.java:126 is a bare RSBuffer.readSignedByte() with no assignment. 113 of 182 records carry it, with values 0/5/6/10/20/25/30/32/50/100. Its meaning is unknown. It must be decoded into a field and re-emitted verbatim or 113 files stop round-tripping. Do not guess a name for it.

5. Opcode 1 aliases: a stored 65535 decodes to -1, and no opcode 1 at all also gives -1. That alias is NOT exercised in this cache (0 of 182 store 65535, all 182 store a real id), so keep the raw value rather than recomputing 65535 from -1, but there is no byte-identity evidence available to defend that branch here.

6. Opcode 6 is dead in this cache - 0 occurrences - yet it is a real opcode with a real consumer (Renderable_Sub2.java:4036). Same category as the reference-table flags CLAUDE.md warns about: implement it, and accept that no sweep defends it.

7. CLIENT BUG, do not copy: Class260.method11 (Class260.java:233-240) clamps with `if (i > length) i = length - 1`, which should be `>=`. A material id exactly equal to the table length falls through and throws. Harmless with this data (max id 1318 against 1408 materials) but do not port the off-by-one.

8. Hydra debug noise, not format: Class177.java:24-29 has two System.out.println calls, one for a null file and one hardcoded to `id == 129`. Those are private-server additions, not Jagex behaviour, and say nothing about record 129.

9. No XTEA anywhere near this index. Table flags are 0x00 - no identifiers, no whirlpool, no sizes - so there are no name hashes to recover and no group naming is possible. Container facts measured from disk: group 0 is GZip, 851 stored bytes, 3468-byte payload, 2-byte version trailer 0x0028 (= 40, matching the reference-table version), chunks = 1, 182 file slices of 11-16 bytes summing to 2739 plus a 729-byte size table. Standard, no surprises - but per AGENTS.md a GZip re-encode is never byte-identical, so the sweep must compare the decompressed payload, never the container.

10. No evidence of any 637-to-639 format change on this index: every one of the 182 files parses to exact consumption against the 637 opcode set with no unknown opcodes, so the data raises no veto against the client here.

11. FlashEditor/Cache/RSConstants.cs:154 says "Archive 29: Skyboxes". That comment block is about ARCHIVES WITHIN INDEX 2, not indexes. It is not about this index and must not be read as one.
