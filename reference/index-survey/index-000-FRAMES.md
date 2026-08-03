# Index 0 - FRAMES

**Format:** fully-understood  
**Capability:** complete  
**Effort:** medium

## What it is

Skeletal animation keyframes. The 637 client opens index 0 at `InterfaceSettings.java:158` (`Class94.aJS5Archive_796 = openFileStore(-114, false, 1, 0)`), hands it to `Class183`'s constructor alongside index 20 (animation defs) and index 1 (frame bases) at `InterfaceSettings.java:282-283`, and `Class183.java:139-150` parks it in `Class64_Sub15.aJS5Archive_3679` via `Projectile.java:32-38`. The only consumer is `Node_Sub46_Sub16`, which is constructed with a group id (`:99-105`), loads EVERY file of that group (`:113-123`), and builds `new Class7[getChildsInFolder(0, group)]` indexed by file id (`:141-165`).

GROUP = one animation's complete frame set. FILE = one keyframe; its file id is the frame's ordinal within the animation, and the array is sized by capacity so gaps are legal. RECORD = one frame: a per-transform-group delta pose.

Frame ids are packed `(group << 16) | fileId` - proven by `Class97.java:130-131`, `method2624(2, i_1_ >> 16); i_1_ &= 0xffff`. Index 20 stores those packed ids, so index 0 is addressed only through index 20.

Byte format, from `Class7.java:45-135`:
  [0]   u8  - read and discarded by the client. Constant 1 in this cache (measured: 359,931 of 359,931).
  [1-2] u16 - the frame BASE group id in index 1 (also read standalone at `Node_Sub46_Sub16.java:128-130`).
  [3]   u8  - transformCount, the number of transform groups this frame touches.
  [4 .. 4+tc)  - one flag byte per transform group. Bits 0/1/2 = "x/y/z present"; bits 3-4 (`i_6_ >>> 3 & 0x3`, `Class7.java:90`) are a 2-bit field kept per transform.
  [4+tc .. end] - the value stream, one signed smart per set bit, read by `RSBuffer.method1239` (`RSBuffer.java:606-612`): byte < 128 -> b-64, else u16-49152.
`Class7.java:112-114` throws unless the value stream is consumed EXACTLY.

Decoding is meaningless without index 1. `Class7.java:61` reads the transform TYPE from the base (`aClass98_Sub1_93.anIntArray3812[i_4_]`), and the type changes the payload: types 3 and 10 default to 128 rather than 0 (`:72-74`), types 2 and 9 are rescaled `<< 2 & 0x3fff` into 14-bit angles (`:91-95`), type 0 marks the pivot that following 1/2/3 entries refer back to (`:62-64, 96-101`), and types 5, 7 and 8/9/10 set three per-frame booleans (`:102-108`) that widen the model-build flags at `Class97.java:145-152`.

## Current capability

Nothing frame-aware. `RSConstants.FRAMES_INDEX = 0` (`FlashEditor/Cache/RSConstants.cs:15`) is declared and has ZERO consumers - a grep for `FRAMES_INDEX` across `FlashEditor/` and `FlashEditor.Tests/` returns that one declaration and nothing else. There is no frame or animation class anywhere in `FlashEditor/Definitions/`, no tab (index 0 is absent from `editorTypes`, `FlashEditor/Editor.cs:64-76`, and there is no case for it in `LoadEditorTab`, `:525`), and no test of the frame format. `STATE_OF_THE_EDITOR.md:98` scores index 0 as "-" in all four columns and `:125` lists idx 0/1/20 under "Missing entirely".

What DOES work is index-agnostic plumbing that happens to reach index 0. `RSCache.ReadFileBytes` (public, `FlashEditor/Cache/RSCache.cs:783-786` over `ReadFile`, `:593-623`) returns any frame file's raw bytes, and `RSCache.WriteFile` (`:102`) writes them back. Index 0 is in fact the load-bearing case for that plumbing: it holds all 3517 three-chunk archives in the cache, `RSArchive.Decode/Encode` (`FlashEditor/Cache/RSArchive.cs:60-158`, `:196-230`) is exercised by `RealCacheConformanceTests.Archives_ReEncodeToTheCapturedPayloadBytes` (`:218`) and `UnchangedArchives_SurviveTheEditPathWithTheirPayloadIntact` (`:295`) which sweep index 0 among the rest (250 sampled archives per index, `RealCacheFixture.cs:24,122-134`, all 3526 under `FULL=1`), and both captured-byte fixtures in `CapturedCacheBytesTests.cs:141,160` are index-0 archives (99 and 2435).

That is byte identity of the CONTAINER and the group PAYLOAD. Not one byte inside a frame is interpreted by this codebase.

## Gaps

- A `FrameDefinition` class with `Decode`/`Encode` in `FlashEditor/Definitions/` - header (b0, baseId u16, transformCount u8), the per-transform flag byte including its 2-bit field at bits 3-4, and the signed-smart value stream. It must take the base's type array as an argument, because types 2/3/9/10 change how a value decodes (`Class7.java:72-95`).
- A `FrameBaseDefinition` for index 1 (`Node_Sub1.java:80-122`). Index 0 cannot be decoded to values without it, so index 1 is a hard prerequisite, not a follow-up. Note its `type 6 -> 2` aliasing at `:96-98`, which is lossy and must be recorded to re-encode index 1 byte-identically.
- A signed-smart WRITER on `JagStream`. `ReadSmart()` (`FlashEditor/IO/JagStream.cs:533-538`) already is `RSBuffer.method1239` exactly (-64 / -0xC000 bias), but there is no matching writer - `WriteUnsignedSmart` (`:623`) is the 0/32768-bias variant and is the wrong function.
- A codec test against captured bytes. Index 0 archives 99 (three-chunk, two files) and 2435 (single file, 77 bytes, base 2174, 29 transforms) are already fixtures in `FlashEditor.Tests`, so the frame-level fixture can be cut from bytes already in the repo.
- A full-index byte-identity sweep: decode and re-encode all 359,931 files in all 3526 groups and assert equality, in the shape of `RealCacheItemDefinitionTests`. This is the missing proof, and it is cheap to make exact because the format self-checks - the value stream must land precisely on the end of the file.
- A GUI tab. It has to follow the pattern: a `TabPage` field in `Editor.Designer.cs` (`:135-148`), an entry appended to `editorTypes` in the SAME position as the tab (`Editor.cs:64-76`), and either a `case RSConstants.FRAMES_INDEX` in the `LoadEditorTab` switch (`:525`) or a `Bind(cache)` panel like `MapEditorPanel`/`TrackEditorPanel` (`:485-497`). A frame browser needs index 20 to name and group the frame sets, since index 0 has no name hashes.
- Anything visual beyond a field grid needs the renderer: `ModelDefinition` parses `VertSkins`/`FaceSkin` (`:42-61, 375-376, 409-410`) but `STATE_OF_THE_EDITOR.md:262-264` records that skinning is never uploaded and the shader has no bone attributes.

## Notes and traps

MEASURED THIS SESSION over every group in the cache, by walking the sector chains, the containers and the archive trailers directly (scratchpad script, read-only, cache untouched):

- Reference table (idx255 group 0): format 6, version 699, flags 0, 3526 groups, ids 0..3525, 359,931 files, consumed 762,182 of 762,182 bytes - no trailing tail, no name hashes, so a group is addressable by id ONLY.
- 3517 multi-file groups, every one of them exactly 3 chunks. 9 single-file groups (22, 605, 757, 1836, 2374, 2435, 2633, 3047, 3290). **Group 757's one file is id 40, not 0** - the other eight are id 0. That is trap 7 below in its cheapest form, and it is worth stating on this line because "single file" reads as "file 0" and a reader who assumes it gets a `FileNotFoundException` on exactly one of the nine.
- Chunk 0 is exactly 4 bytes for all 359,922 files in multi-file groups - it IS the frame header. Chunk 1's length equals transformCount for all 359,922, zero exceptions - it IS the flag block. Chunk 2 is the value stream.
- Header byte 0 is 1 for all 359,931 files, single-file groups included.
- All 3526 groups reference exactly ONE base id across all their files. Max base id 3105 = index 1's highest group (3106 groups).
- All 359,931 files parse with exact consumption: 358,358 with transforms land precisely on the end of the value stream, 1,573 are empty frames (transformCount 0, four bytes total). Zero overruns, zero leftovers. The format is completely accounted for.
  - CORRECTED 2026-08-04. This line read 358,363 / 1,568. It was wrong by five, and the wrong figure went straight into `RealCacheFrameTests` as an assertion and failed there. Two independent measurements give 1,573: the production codec over every file, and a sweep that decodes no frame at all and counts files whose archive size table totals four bytes. Both numbers describe the same population, because a frame with no transforms has no flag block and no value stream.
- 20,142,030 signed-smart values read: 8,270,387 one-byte and 11,871,643 two-byte.

TRAPS:

1. THE SIGNED SMART IS NON-CANONICAL BY CONSTRUCTION BUT CANONICAL IN THIS DATA. `method1239` accepts values -64..63 in either the one-byte form (v+64) or the two-byte form (v+49152, first byte 0x80..0xFF, so the reachable range is -16384..16383), so the same decoded int has two legal encodings - exactly the trap CLAUDE.md documents. Measured: of 11,871,643 two-byte values, ZERO fall in -64..63. Jagex always picked the narrow form. So an encoder that writes one byte whenever -64 <= v <= 63 and two otherwise reproduces the cache exactly, and the width does NOT need recording. Verify this with the sweep rather than trusting it; it is the one place a frame codec can silently diverge.

2. CHUNK BOUNDARIES ALIGN WITH THE FORMAT BUT ARE NOT PART OF IT. The 4/masks/values split is a packer convention: `JS5Archive.getChildFromFolder` (`:203-205`) hands the client the concatenation, and `Class7` reads one flat array. Do not read fields per chunk. Conversely `RSArchive.TryResliceFile` (`FlashEditor/Cache/RSArchive.cs:297-328`) keeps each original slice and dumps any growth into the last chunk, so editing a frame whose length changes produces a legal payload with a different split - correct for the client, but no longer byte-identical, and the archive-level sweep will see it.

3. INDEX 1 IS A HARD DEPENDENCY, INDEX 20 IS THE ONLY WAY IN. A frame decoded without its base yields raw ints with no meaning - the type decides whether 128 or 0 is the default and whether the value is a 14-bit angle. And nothing names an index-0 group; the only route to "which animation is this" is index 20's packed `(group << 16) | file` ids (`Class97.java:130-131`), and index 20 has no decoder here either.

4. INDEX 0 IS THE CACHE'S BZIP2 RESERVOIR. Compression across its 3526 groups: 2310 GZip, 1196 BZip2, 20 uncompressed. AGENTS.md records 1743 BZip2 containers cache-wide of which 19 do not round-trip byte-identically, so 1196 of the 1743 sit here - if a frame write ever needs container identity, this is where the BZip2 re-encode risk concentrates. GZip never round-trips identically at all (0 of 96,183), which is why an unchanged frame must not be re-encoded.

5. NO XTEA ANYWHERE IN INDEX 0. It is not in the key dumps and the table is format 6 with no flags byte. Nothing to infer.

6. transformCount is a single byte, so 255 is the ceiling, and the cache reaches it. `Class7`'s static scratch arrays are sized 500 (`:7-18`), which is headroom, not a limit to copy.

7. `getChildsInFolder` returns the ARRAY CAPACITY (`JS5Archive.java:207-221`, `anIntArray2671[group]`), while `method2743` (`:807-830`) returns the actual file ids. `Node_Sub46_Sub16.java:141-165` sizes by the former and indexes by the latter, so frame arrays are legitimately sparse with null holes. A decoder that assumes file id == position will be wrong on sparse groups.

8. Max files in one group is 2792, and 508 distinct file counts occur. Nothing may assume a uniform animation length.

Grade justification: `none`, not `read-write-no-tests`. The raw byte read/write is index-agnostic plumbing available to all 37 indexes; counting it as index-0 support would claim a capability that does not exist, since no line of this codebase knows what a frame is. Effort is `medium` for decoder + encoder + full sweep + a field-grid tab; playback in the viewer is `large` and belongs to the renderer, not to this index.
