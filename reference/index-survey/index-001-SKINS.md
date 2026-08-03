# Index 1 - SKINS - animation skeletons (frame bases / "framemaps")

**Format:** fully-understood  
**Capability:** complete  
**Effort:** small

## What it is

Index 1 holds animation **skeletons** - the bone table an animation frame is played against. The constant name SKINS is misleading in the modelling sense: these are not vertex weights, they are the per-bone transform descriptors.

Client trace (authoritative): `InterfaceSettings.java:159` opens index 1 as `Class323.aJS5Archive_2716`, which is handed to `new Class183(...)` at `InterfaceSettings.java:283` alongside index 20 (animation defs) and index 0 (frames). `Class183.<init>` forwards it to `Projectile.method3079` (`Projectile.java:32-38`), which parks index 1 in `Class64_Sub2.aJS5Archive_3644` and index 0 in `Class64_Sub15.aJS5Archive_3679`. The only consumer is `Node_Sub46_Sub16.method1614` (`Node_Sub46_Sub16.java:105-166`), the frame-set loader: for each frame file it skips byte 0, reads a u16 (`RSBuffer.caret = 1; readUnsignedShort()`, :126-128), and uses that u16 as the **index-1 group id**, fetching it with `Class64_Sub2.aJS5Archive_3644.method2733(i_27_, -118)` (:161) and decoding it with `new Node_Sub1(id, bytes)`.

Structure in THIS cache: 3106 groups, each with exactly one file, so **one group = one file = one skeleton record**. Confirmed both ways - `JS5Archive.method2733` (`JS5Archive.java:591-611`) returns `getChildFromFolder(i, 0)` only when the group's file count is 1 and throws otherwise, and my own sweep of all 3106 groups found every one a single-file group. The reference table for index 1 is format 6, version 392, flags `0x00` (measured from idx255 group 1), so **no name hashes** - a skeleton is addressable by id only.

Record format (`Node_Sub1.java:82-118`), all fields big-endian:
  u8   boneCount
  u8[boneCount]  transformType   (client remaps a stored 6 to 2 at :96-97)
  u8[boneCount]  flag            (client stores it as `byte == 1`, :102)
  u16[boneCount] mask            (:106; ANDed with a caller mask before the transform runs)
  u8[boneCount]  labelCount      (:110)
  u8[...]        labels          (:115, labelCount entries per bone, concatenated)

Meaning, from what the client DOES with the fields (not from any name): `Renderable_Sub2.method2344` (`Renderable_Sub2.java:2788-3120`) dispatches on transformType - 0 = compute pivot/origin as the centroid of the labelled vertices (:2792-2828), 1 = translate (:2829-2848), 2 = rotate (:2849-2913), 3 = scale, `>>7` (:3014-3035), 5 = alpha (:3036+), plus arms for 7, 8, 10. `labels` are indices into the model's label tables. `mask` is gated by `Renderable.java:320,325` (`i_29_ & class98_sub1.anIntArray3815[...]`); `flag` gates a separate skeletal path at `Renderable.java:721`.

Measured over all 3106 groups (my read-only sweep): 173,749 bones and 936,887 label entries total; bone count 0..255 (two groups have a 1-byte payload, i.e. zero bones); payload 1..5918 bytes; compression 2584 GZip / 509 none / 13 BZip2, every container with a 2-byte version trailer, none XTEA-encrypted.

## Current capability

Nothing index-specific. FlashEditor has **no** skeleton decoder, no encoder, no test and no GUI tab.

The only trace of index 1 in the whole repository is the constant itself: `FlashEditor/Cache/RSConstants.cs:16` (`SKINS = 1`) and its display string at `FlashEditor/Cache/RSConstants.cs:66`. A repo-wide grep for `SKINS` returns those two lines and nothing else. Per CLAUDE.md ("RSConstants is already fully adopted... an unreferenced index constant means the editor has no feature for that index yet"), that is conclusive rather than suggestive. Grepping for `Skin`/`Skeleton`/`FrameMap`/`Node_Sub1` in `FlashEditor/` and `FlashEditor.Tests/` returns only `ModelDefinition.cs` (`VertSkins`/`FaceSkin`, which are the model's own per-vertex/per-face label arrays - the *target* of index-1 labels, not the skeleton).

What does cover index 1, generically and at the wrong layer:
- `FlashEditor/Cache/RSCache.cs:542-547` loads every reference table at startup, so index 1's table is decoded and shown in the "Reference Tables" tab (`FlashEditor/Editor.Designer.cs:291-298`). That is group count / CRC / version metadata only.
- `FlashEditor.Tests/Cache/RealCacheConformanceTests.cs:126,175-196,226-257,302-335` iterate `_cache.TableIndexes`, which includes index 1, and sweep it at the container/archive layer - stored-CRC agreement, container decode/re-encode preserving compression, version and payload, and the file split. So we can already read an index-1 group as an opaque blob and write one back safely.
- `RealCacheReferenceTableShapeTests` covers index 1's reference table shape.

Neither of those knows a bone from a byte. There is no `SkinDefinition` (or equivalently named) class, so `RSCache.WriteFile(1, id, 0, bytes)` is the only "edit" available and it requires the caller to hand-build the record.

## Gaps

- A definition class - e.g. FlashEditor/Definitions/SkeletonDefinition.cs - with Decode(JagStream)/Encode() implementing Node_Sub1.java:82-118 verbatim: u8 boneCount, u8[n] transformType, u8[n] flag, u16[n] mask, u8[n] labelCount, u8[...] labels. It must store the transform type RAW rather than applying the client's 6 -> 2 remap, and store the flag as the raw byte rather than a bool, or the encoder cannot be byte-identical in general.
- A codec test against captured bytes, in the style of FlashEditor.Tests/Cache/ObjectDefinitionCodecTests.cs and CapturedCacheBytesTests.cs - CLAUDE.md is explicit that round-tripping this encoder against this decoder proves nothing. Pin at least one real group (group 0 is 251 bones / 4950 bytes and exercises transform types 0,1,2,3,5 and label lists of 0 and 167) plus a zero-bone group.
- A full-index byte-identity sweep - RealCacheSkeletonTests, modelled on RealCacheObjectDefinitionTests - decoding all 3106 groups, asserting exact stream consumption and asserting Encode() reproduces the decompressed payload byte for byte. I have already proven offline that this passes: all 3106 groups consume exactly, 0 short and 0 long. Compare the DECOMPRESSED payload, never the stored container (2584 of the 3106 are GZip and a GZip re-encode is never byte-identical).
- A GUI tab following the Editor.Designer.cs pattern (a TabPage plus a TreeListView, as ItemEditorTab/ObjectEditorTab do, loaded through the existing per-tab BackgroundWorker in Editor.cs around :500-565): a list of 3106 skeleton ids with a per-bone grid of type / flag / mask / labels. There are no names on this index, so the list is id-only.

## Notes and traps

TRAPS, in the order they will bite:

1. **Index 1 is not independently editable, and this is the one that will cost real work.** A frame in index 0 stores per-bone deltas positionally, keyed to the skeleton it names at bytes [1..3] of the frame file (`Node_Sub46_Sub16.java:126-128`). Adding, removing or reordering a bone silently invalidates every frame that references that skeleton. My sweep over all 3526 index-0 groups found the first frame of every one naming an in-range index-1 group (3095 distinct skeletons, 0 out of range), so the coupling is dense and real. A read-only viewer is safe; an editor that changes boneCount is not, unless it rewrites the referencing frames too.

2. **Label ids point at two different tables depending on the transform type.** Types 0/1/2/3 index the model's *vertex* label groups (`anIntArrayArray4888`, `Renderable_Sub2.java:2806,2837,2853,3018`), built from `ModelDefinition.VertSkins`. Type 5 (alpha) indexes the model's *face* label groups (`anIntArrayArray4870`, `Renderable_Sub2.java:3041`), built from `ModelDefinition.FaceSkin`. A UI that resolves a label to a vertex group unconditionally will mislabel every alpha bone. That is a dependency on index 7, not on index 1.

3. **The client's 6 -> 2 remap is a decode-side normalisation, and must not reach the stored byte** (`Node_Sub1.java:96-97`). Type 6 occurs **0 times** in the 173,749 bones of this cache, so it costs nothing today - but this is exactly the "aliased values" hazard CLAUDE.md lists, and folding it in makes the encoder wrong the moment a repack introduces one.

4. **The flag byte is read as `byte == 1`, so a stored value > 1 would decode true and re-encode as 0.** Only 0 (173,153) and 1 (596) occur here, so a `bool` field round-trips this cache - but storing the raw byte costs one field and removes the hazard.

5. **The u16 mask is 0xFFFF on all 173,749 bones.** Its meaning cannot be established from this cache and should not be guessed - CLAUDE.md's warning about plausible mappings applies squarely. Store it, display it, do not fold it away and do not name it beyond what `Renderable.java:320,325` proves (it is ANDed with a caller-supplied mask before the transform is applied).

6. **Transform type 4 occurs 4 times in this cache and no renderer in the 637 client has an arm for it.** `Renderable_Sub2.method2344` handles 0,1,2,3,5,7,8,10; `Renderable_Sub1` adds 9; `Renderable_Sub3` handles 0,1,2,3,5,7,8,9,10. Nothing handles 4 anywhere. Type 9 (141 occurrences) is handled by Sub1 and Sub3 but not Sub2. Irrelevant to a codec - the type is an opaque u8 - but it will confuse anyone building a viewer, and it is a "data vetoes" case in reverse: the value exists and the client ignores it.

7. **Every group is single-file**, so AGENTS.md's single-file rule applies exactly: the whole decompressed payload is the record, with no size table and no chunk-count byte. Writing one back through `RSCache.WriteFile(RSConstants.SKINS, id, 0, ...)` must not add either.

8. **Two groups have a 1-byte payload (boneCount 0).** A decoder that assumes at least one bone crashes on them. Bone count, labelCount and label id are all u8, so 255 bones and 255 labels-per-bone are hard ceilings the editor must enforce (max observed: 255 bones, 254 labels-per-bone, label id 254).

9. **No XTEA anywhere on index 1**, and no name hashes (reference table flags `0x00`), so no name-hash join is possible - unlike index 6, where the enum join went wrong. Skeletons are id-addressable only.

Evidence for the format claims is a read-only Python sweep I ran against `cache/main_file_cache.dat2` + `idx1` (scratchpad only, nothing written to the repo or the cache): all 3106 groups parse under the `Node_Sub1` layout with **zero** bytes left over and zero parse failures. That is the strongest possible statement that the format did not change between 637 and 639, and it means the byte-identity sweep in the gap list is a formality rather than a risk - which is why the effort is small.
