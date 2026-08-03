# Index 20 - ANIMATIONS (animation / sequence definitions)

**Format:** fully-understood  
**Capability:** none  
**Effort:** medium

## What it is

Animation ("sequence") definitions - the records that say which frames an animation plays, in what order, for how long, and with what priority. NOT the frame data itself (that is index 0) nor the skeletons (index 1).

Addressing, from the 637 client: index 20 is opened at `InterfaceSettings.java:176` into `Particle_Sub3.aJS5Archive_5087`, and that archive is handed to `Class183`'s constructor at `InterfaceSettings.java:282-283` alongside index 0 (`Class94.aJS5Archive_796`) and index 1 (`Class323.aJS5Archive_2716`). `Class183.method2623(id, 16383)` is the definition getter: it splits the animation id into group `id >>> 7` (`Class299_Sub2.java:132`) and file `id & 0x7f` (`Node_Sub10_Sub32.java:18`), pulls the file at `Class183.java:243-244`, and decodes it with `Class97.method933` (`Class97.java:264-285`), an opcode loop terminated by a zero byte, dispatching to `Class97.method939` (`Class97.java:416-539`).

So: a GROUP is a bank of up to 128 consecutive animation ids; a FILE is one animation definition; ONE RECORD is an opcode stream. Measured in this cache by decoding it: 120 groups, ids 0..119 contiguous, 15,260 definitions (matching AGENTS.md:294), animation ids 0..15,274 with 15 ids absent. 112 groups hold the full 128 files; groups 3, 4, 7, 40 hold 127, group 22 holds 126, group 34 holds 122, group 89 holds 125, group 119 holds 43.

The opcode table, each cited to `Class97.java` and confirmed present in the 639 data (occurrence counts over all 15,260 records):
- 1 (:419-431, 15260x): u16 n; n x u16 -> per-frame duration in client cycles (`Class340.java:26-33` advances the frame when a per-cycle counter exceeds it); n x u16 then n x (u16 << 16) -> packed frame reference, high 16 = index-0 frame group, low 16 = frame index (`Class97.java:130-131` does `>> 16` then `&= 0xffff`). Payload 1 + 6n bytes.
- 2 (:433, 3911x) u16. 3 (:526-530, 809x) u8 count then count x u8 into a boolean[256]. 5 (:436, 2726x) u8 = priority, default 5; a queued animation is only replaced when the new priority >= the old (`Class266.java:52-53`). 6 (:438, 2033x) u16. 7 (:523, 2193x) u16. 8 (:520, 388x) u8, default 99. 9 (:442, 424x) u8. 10 (:444, 727x) u8. 11 (:517, 426x) u8 = re-trigger behaviour, default 2; 0 cancels, 1 restarts at frame 0, 2 resets the sub-counter (`Class266.java:56-70`). 12 (:506-514, 178x) u8 count then count x u16 and count x (u16 << 16), same packed frame reference as opcode 1. 13 (:491-503, 2311x) u16 n, then n rows of u8 count c, and when c>0 a 24-bit value plus (c-1) x u16; c==0 consumes only the count byte. 14 (:488, 192x), 15 (:450, 4629x), 16 (:485, 2x), 18 (:482, 371x) are flags with no payload. 19 (:478-479, 189x) u8 index + u8 value. 20 (:466-468, 813x) u8 index + u16 + u16.

Opcodes 4 and 17 have no handler in the 637 client and occur zero times in the 639 data, so there is no data veto to reconcile.

## Current capability

Nothing index-20-specific exists. `RSConstants.ANIMATIONS_INDEX = 20` (`FlashEditor/Cache/RSConstants.cs:35`) and the display name at `:85` are the only two references to this index in the entire repository - a project-wide grep for `ANIMATIONS`, `ANIMATIONS_INDEX`, `AnimationDefinition` and `SequenceDefinition` across `FlashEditor/` and `FlashEditor.Tests/` returns those two lines and nothing else. Per CLAUDE.md's own note, an unadopted `RSConstants` entry means the editor has no feature for that index, not that someone used a magic number.

- No definition class. `FlashEditor/Definitions/` holds Item, NPC, Object, FloorUnderlay, FloorOverlay, MapSceneIcon and Model only.
- No GUI. `Editor.editorTypes` (`FlashEditor/Editor.cs:64-76`) lists the nine indexes the editor loads; 20 is not among them, and `LoadEditorTab`'s switch (`Editor.cs:525`) has no case for it. `Editor.Designer.cs` has no animation tab.
- No test. Nothing in `FlashEditor.Tests/Cache/` mentions it.

What does reach index 20 is index-agnostic infrastructure, and it is worth being precise about it rather than counting it as capability:
- `RSCache.LoadReferenceTables` (`FlashEditor/Cache/RSCache.cs:542-555`) decodes every meta group on open, so index 20's reference table is parsed and shows up in the META/console tab's table list (`Editor.cs:526-539`). Metadata only - group count, version, CRCs.
- `RealCacheFixture.TableIndexes` (`FlashEditor.Tests/Cache/RealCache/RealCacheFixture.cs:55-64`) enumerates every index with a meta container, so `RealCacheConformanceTests` does sweep index 20's containers and archive file-splits (`RealCacheConformanceTests.cs:126-134, 175-196, 226-257`). That proves the wrapper and the file split for this index, not one byte of the record format.
- `RSCache.ReadFile(20, group, file)` already returns the correct raw bytes; there is simply no decoder to hand them to.

## Gaps

- A definition class, e.g. FlashEditor/Definitions/AnimationDefinition.cs, with Decode(JagStream)/Encode() covering the 18 opcodes above. It must record the opcode order and repeats via the existing List<DecodedOpcode> mechanism (FlashEditor/Definitions/DecodedOpcode.cs) - 7,940 of the 15,260 records are not in ascending opcode order and 15 records repeat a scalar opcode, so a value-only decoder cannot re-encode this index.
- A codec test against captured bytes, in the shape of FlashEditor.Tests/Cache/ObjectDefinitionCodecTests.cs. Two records already earn a hand-written case: anim 5857 (opcode sequence 15,16,1,2) and anim 6495 (14,15,16,1,2) are the only two occurrences of opcode 16 in the cache, and both are short enough to pin literally.
- An exact-consumption sweep over all 15,260 records, modelled on RealCacheNpcDefinitionTests.AllNpcDefinitions_Decode_AndConsumeTheirBufferExactly, including its SentinelPadding trick (RealCacheNpcDefinitionTests.cs:38-51) so an over-read is visible.
- A byte-identity sweep asserting all 15,260 records re-encode to the bytes they were read from, in the shape of the existing item/NPC/object sweeps. This is the gate CLAUDE.md names as the primary regression detector, and without it the index cannot be graded above read-write-no-tests.
- A GUI tab following the Editor.Designer.cs pattern: add RSConstants.ANIMATIONS_INDEX to Editor.editorTypes (Editor.cs:64-76) in the same position as the new TabPage, add a case to the LoadEditorTab switch (Editor.cs:525) populating from cache.GetReferenceTable(20), and a save path mirroring Editor.cs:986 (cache.WriteFile(...)).
- Optional and separable: frame playback. That needs index 0 (frame) and index 1 (skin/base) decoders, neither of which exists - STATE_OF_THE_EDITOR.md:263 records that no frame or sequence loader exists at all. Editing the definitions does not depend on it.

## Notes and traps

Evidence for the format claims: I decoded all 120 groups of the real cache at C:\\Users\\CJ\\Desktop\\FlashEditor\\cache with a standalone Python implementation of the sector/container/archive layers plus the 637 client's opcode chain. All 15,260 records decoded and consumed their buffer exactly - zero failures, zero unknown opcodes, zero leftover bytes. That is as strong as the NPC/item evidence CLAUDE.md accepts, so the 637 opcode table is complete for this 639 index and the implementer should port `Class97.method939` directly rather than re-deriving it.

TRAPS, in the order they will bite:

1. NON-CANONICAL OPCODE ORDER. 7,940 of 15,260 records are not in ascending opcode order (anim 5: [6,7,11,15,9,1,3]; anim 25: [5,1,13,3]). Opcode 1 leads in only 7,330 records; 15 leads in 3,500, 5 in 2,063, and seven other opcodes lead somewhere. A fixed-order encoder reproduces a minority of this index. Record the order.

2. NON-CANONICAL REPEATS OF SCALAR OPCODES. Opcode 5 appears twice in 4 records (anim 7317: [5,5,1,3]), opcode 9 in 4 (anim 864: [5,6,7,9,10,11,15,9,10,1,3]), opcode 10 in 4, opcode 15 in 2 (anim 11724: [15,15,1,2]), opcode 7 in 1 (anim 1771: [7,6,7,1]). These are exactly the floor-overlay-94 shape CLAUDE.md documents: keeping only the winning value gives a file of the wrong length or the wrong contents. Opcodes 19 and 20 also repeat (45 and 185 records) but there it is legitimate - each occurrence writes a different array slot.

3. POST-DECODE MUTATION. `Class97.method938` (`Class97.java:385-413`) runs after the opcode loop and rewrites `anInt821` and `anInt816` from -1 to 0 or 2 depending on whether opcode 3 was present. `Class183.java:260` calls it on every load. An encoder that emits from post-processed state writes opcodes 9 and 10 into files that never had them. Keep the decoded state separate from the derived state.

4. ABSENT VERSUS DEFAULT. The constructor (`Class97.java:107-119`) presets anInt807=99, anInt821=-1, anInt819=2, anInt820=-1, anInt816=-1, anInt829=5, anInt828=-1. A record that stores the default value is indistinguishable from one that omits the opcode unless presence is recorded at decode - the same rule CLAUDE.md states for terrain heights.

5. LAZY ARRAY SIZING. Opcodes 19 and 20 allocate from `anIntArrayArray822.length`, which opcode 13 sets. I checked: in this cache opcode 13 always precedes 19/20, and no record carries 19 or 20 without 13, so the client never faults - but that invariant is a property of the data, not the format. Preserving the recorded order preserves it for free; reordering breaks it.

6. OPCODE 16 IS NEARLY DEAD BUT REAL. Exactly 2 occurrences (anims 5857 and 6495). Easy to drop by accident, and no sampled sweep would notice.

7. SPARSE FILE IDS. Eight groups hold fewer than 128 files and 15 animation ids are missing from the 0..15,274 range. Address records through the reference table's file id list, never by position within the group.

8. CONTAINER COMPARISON WILL FAIL. Index 20's 120 groups are 78 BZip2 and 42 GZip, every one carrying a 2-byte version trailer. Per AGENTS.md, GZip never re-encodes byte-identically, so the sweep must compare the decompressed payload, as the existing sweeps do.

9. NO NAMES, NO XTEA. The index 20 reference table is format 6, version 2436, flags 0x00 - no identifiers, no whirlpool, no sizes - and consumes to the byte (31,968 of 31,968), so it is not one of the four trailing-byte indexes. Animations are addressable by id only; there is nothing to hash names against. Nothing on index 20 is encrypted.

Scratchpad (not in the repo): C:\\Users\\CJ\\AppData\\Local\\Temp\\claude\\C--Users-CJ-Desktop-FlashEditor\\f188415d-792b-47d7-bdca-e00fd5387036\\scratchpad\\idx20.py reproduces every count above.
