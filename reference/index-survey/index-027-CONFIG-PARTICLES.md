# Index 27 - CONFIG_PARTICLES

**Format:** fully-understood  
**Capability:** none  
**Effort:** medium

## What it is

Two groups of opcode-encoded particle definitions, one record per file. Group 0 = 403 particle EMITTER definitions (ids 0..402, contiguous); group 1 = 18 particle EFFECTOR (force-field) definitions (ids 0..17, contiguous). 421 files total, matching FlashEditor.Tests/Cache/RealCacheReferenceTableShapeTests.cs:257.

Client authority: InterfaceSettings.java:183 opens index 27 (`openFileStore(-96, false, 1, 27)`) into Class245.aJS5Archive_1864; InterfaceSettings.java:307 hands it to Class373_Sub1_Sub1.method3970 (Class373_Sub1_Sub1.java:9-28), which stashes the same archive in two statics - Class242.java:40-42 -> Class42_Sub1_Sub1.aJS5Archive_6206 and Class89.java:23-33 -> Class64_Sub3.aJS5Archive_3648. Those two statics are read in exactly one place each: ParticleType.java:11 `getChildFromFolder(0, id)` and Class21.java:51 `getChildFromFolder(1, i)`. JS5Archive.java:203-204 fixes the argument order as (groupId, fileId), so group 0 -> ParticleType, group 1 -> Class66.

A record is a classic opcode stream terminated by 0. Emitter opcodes 1..34 are decoded by ParticleType.method895 (ParticleType.java:519-664); effector opcodes 1..10 by Class66.method686 (Class66.java:284-325). Class66 is provably a force field, not a sprite: Particle_Sub4_Sub2_Sub1.java:129-307 uses its opcode-3 vector (anInt506/511/505), its opcode-4 falloff mode + strength (anInt518/anInt512) and its cone threshold (anInt514, derived at Class66.java:245 from the opcode-1 angle) to accumulate per-particle acceleration. Class21.java:58-61 registers effectors whose opcode-6 mode == 2 into a 16-slot global array (Class336.java:22), so mode 2 = ambient/global, otherwise scene-local.

Emitters name effectors by id: emitter opcode 9 (anIntArray728) and opcode 10 (anIntArray772) are u8-count + u16 id lists resolved against group 1 (Particle_Sub4_Sub2_Sub1.java:139-140, 287-295). I measured the union of those lists across all 403 emitters: exactly {0..17}, i.e. every effector in group 1 is referenced.

Measured on-disk shape of this cache (my own read-only parse of idx255/idx27/dat2, not from any doc):
- reference table: format 6, version 87, flags 0x00 (NO name hashes - addressable by id only), 2 groups, group versions 83 and 11, 874 bytes of real table plus 1684 trailing zero bytes.
- group 0 container: GZip, 9682 stored bytes, 2-byte version trailer, 34,376-byte payload, chunks = 1, 403 files, 32,763-byte body, per-file 67..90 bytes (avg 81.3), no zero-length files.
- group 1 container: GZip, 233 stored bytes, 2-byte version trailer, 462-byte payload, chunks = 1, 18 files, 389-byte body, per-file 16..26 bytes.
- no XTEA anywhere: both containers inflate directly.

## Current capability

Nothing. No decoder, no encoder, no test, no GUI.

The complete set of references to index 27 in the whole repo:
- FlashEditor/Cache/RSConstants.cs:42 - `CONFIG_PARTICLES = 27, //map effects`. Declaration only; grep for CONFIG_PARTICLES over FlashEditor/ and FlashEditor.Tests/ returns this line and nothing else. It is one of the 27 unadopted index constants CLAUDE.md describes.
- FlashEditor/Cache/RSConstants.cs:92 - the string "CONFIG_PARTICLES" in `indexNames`, consumed by `GetIndexName` (RSConstants.cs:111) for display text.
- FlashEditor.Tests/Cache/RealCacheReferenceTableShapeTests.cs:179, 248, 250, 257-259 - index 27 appears only as reference-table *shape* data (2 groups, 421 files, 1684 trailing zero bytes). Nothing there opens a group or looks at a record.

The generic layers do work on it, because they are index-agnostic: `RSCache.GetContainer`/`ReadFile` will hand back the raw bytes of group 0 file N today, and the Meta tab (Editor.Designer.cs:275, 297, 385) lists index 27's reference table row alongside the other 34. That is metadata and raw bytes, not decoding.

Near miss worth naming so nobody counts it as support: FlashEditor/Definitions/ModelDefinition.cs:78 and :80 declare `ParticleEffectId` and `ParticleAnchorVert`, and Editor.cs:268-269 prints "Particle effect: ...". Both fields are declared and **never assigned** - grep finds only the declarations - so `ParticleEffectId` is permanently 0xFFFF and that Editor line is unreachable. The model decoder does not read the particle footer at all. The real footer is in the client at reference/hydra-model-decoding/Model.java:754-782: a u8 count then (emitterId u16, vertexIndex u16) pairs, followed by a u8 count then (effectorId u16, vertexIndex u16) pairs, the latter resolved to index 27 group 1 via Class35.java:196-206.

Grade: none.

## Gaps

- ParticleEmitterDefinition with Decode/Encode over the 34 opcodes at ParticleType.java:519-664. Widths, all verified by exact consumption over the real files: 1 = 4x u16 (each <<3 at decode, so the encoder must >>3); 2 = u8 discarded; 3 = 2x i32; 4 = u8 + s8; 5 = u16; 6 = 2x i32 (start and end ARGB); 7 = 2x u16; 8 = 2x u16; 9 = u8 count + count x u16 (effector ids); 10 = u8 count + count x u16 (global effector ids); 11 = NO payload and no handler at all - a silent no-op, see traps; 12 = s8; 13 = s8; 14 = u16; 15 = u16; 16 = u8 + u16 + u16 + u8; 17 = u16; 18 = i32; 19 = u8; 20 = u8; 21 = u8; 22 = i32; 23 = u8; 24 = flag, no payload; 25 = u8 count + count x u16; 26 = flag; 27 = u16; 28 = u8; 29 = s16 discarded; 30 = flag; 31 = 2x u16; 32 = flag; 33 = flag; 34 = flag.
- ParticleEffectorDefinition with Decode/Encode over the 10 opcodes at Class66.java:284-325: 1 = u16 (cone angle index); 2 = u8 discarded; 3 = 3x i32 (direction/offset x,y,z); 4 = u8 falloff mode + i32 strength; 5 = no handler, no payload; 6 = u8 mode (2 = global); 7 = no handler, no payload; 8 = flag, no payload; 9 = flag, no payload; 10 = flag, no payload. Note the client reads the strength pair on opcode 4, not 5 - `(i ^ 0xffffffff) == -5` is i == 4. Getting that wrong costs 9 of the 18 records; it cost me a pass.
- Opcode-order and repetition recording on both classes. This index is non-canonical in the way CLAUDE.md warns about, and I measured it: ALL 403 emitters and ALL 18 effectors store their opcodes in a non-ascending order (91 distinct emitter sequences, 7 distinct effector sequences), e.g. emitter 0 is 1,3,31,27,28,7,8,10,12,14,15,16,20,24,24,30,26,21,22,23,6,18. A decoder that re-emits in ascending order re-encodes 0 of 421 records correctly.
- A codec test against captured bytes from this cache (not a self-round-trip - CLAUDE.md: round-tripping this encoder against this decoder proves nothing).
- A full-index byte-identity sweep over all 421 files, comparing decompressed file bytes rather than containers (GZip re-encode is never byte-identical).
- A GUI tab in the Editor.Designer.cs pattern (see MapEditorTab / TrackEditorTab at Editor.Designer.cs:147-148 and the per-tab load switch at Editor.cs:525). Two lists, emitters and effectors, keyed by id - there are no name hashes on this index (table flags 0x00), so ids are the only handle.

## Notes and traps

TRAPS, most damaging first.

1. Opcode 24 is emitted TWICE in 42 of the 403 emitters, and it carries no payload (`aBoolean759 = false`, ParticleType.java:583-584) - so the bytes are literally `18 18`. This is the floor-overlay-94 shape from CLAUDE.md, with one mercy: opcode 24 is the ONLY repeated opcode anywhere in index 27, and it has no value, so a plain ordered `List<int>` of opcodes is sufficient. The existing `DecodedOpcode` struct (FlashEditor/Definitions/DecodedOpcode.cs:15-33) carries a single `int Value` and cannot represent index 27's multi-field payloads (opcode 1 is four u16s, opcode 16 is four fields) - do not force it. Record the order, encode the values from the fields.

2. Opcodes 5 and 31 are aliases for the same pair of fields. Opcode 5 sets anInt780 = anInt788 from ONE u16 (ParticleType.java:541); opcode 31 sets each from its own u16 (:593-596). 16 records use 5, 387 use 31, 16+387 = 403, so exactly one per record. Decoding to the field values alone throws away which encoding was on disk and breaks byte-identity on the 16 that use opcode 5.

3. Emitter opcode 11 has no handler and reads NO payload. It falls through every branch of ParticleType.method895 and the loop simply reads the next byte as the next opcode. Do not "fix" this by giving it a width - it does not occur in this cache, and a width would desync the stream if it ever did. Same shape for effector opcodes 5 and 7.

4. CLIENT BUG / private-server hack, ParticleType.java:4-6 and :17-27. `list(int id)` starts with `if(id == 306) id = 100;`, and then for id 100 it hardcodes seven colour fields over whatever was decoded (anInt741=255, anInt730=-51, anInt757=255, anInt734=-51, anInt771=255, anInt737=-51, anInt775=0xffffff). Emitter 306 exists on disk (group 0 holds 0..402) and is unreachable in this client, and emitter 100's decoded colours are discarded. Editing either file will look like it did nothing. This is a client-side override, not a format fact - the editor should decode both faithfully and, at most, warn.

5. Cross-index dependency, and the field the editor already pretends to have. Models (index 7) reference emitters and effectors by id in the footer at reference/hydra-model-decoding/Model.java:754-782. `ModelDefinition.ParticleEffectId`/`ParticleAnchorVert` (ModelDefinition.cs:78-80) are declared and never assigned, so Editor.cs:268-269 prints a line that can never fire. Anyone wiring index 27 into the model viewer must implement that footer first; do not trust the existing fields.

6. Reference table: flags 0x00, so NO name hashes on this index - like index 2, a record is addressable only by id. And index 27 is one of the four tables carrying trailing zero bytes (1684 of them, four per file). ReferenceTableCodec already tolerates that; a hand-rolled parser will not.

7. No XTEA, GZip on both groups, 2-byte version trailer on both. Compare decompressed file bytes in any sweep, never containers.

8. Opcode coverage in this cache, so you know which branches no sweep will defend. Emitters: opcodes 2, 11, 17, 25, 29 occur ZERO times; every other opcode 1-34 occurs. Effectors: opcodes 2, 5, 7 occur zero times. Implement them anyway - same reasoning as the dead reference-table flags in CLAUDE.md - but do not read a passing sweep as evidence they are right.

9. Verification I actually ran (read-only, no build, no test host): I parsed idx255 group 27, both index 27 containers and all 421 records straight out of dat2 in PowerShell, using the opcode widths taken from the client. 403 of 403 emitters and 18 of 18 effectors consume their file EXACTLY, with no unknown opcode and no overrun. That is the exact-consumption proof CLAUDE.md asks for before trusting payload sizes, and it says the 637 client's opcode table is complete for this 639 data - unlike items, index 27 has no 639-only opcode.

One PowerShell gotcha if you reproduce this: `-shl` on a [byte] operand wraps within byte width, so `$b[0] -shl 8` is 0. Cast to [int] first. It silently produced a plausible-looking wrong offset table for me before I caught it.
