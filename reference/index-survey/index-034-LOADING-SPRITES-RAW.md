# Index 34 - LOADING_SPRITES_RAW

**Format:** empty-in-this-cache  
**Capability:** not-applicable  
**Effort:** trivial

## What it is

Nothing. Index 34 is genuinely, verifiably empty in this cache, at both levels that could hold it: `cache/main_file_cache.idx34` is a single byte `0xff`, so `RSFileStore.GetFileCount` (RSFileStore.cs:73, length/RSIndex.SIZE) yields 1/6 = 0 groups; and idx255 record 34 (bytes 204..209 of the 222-byte file) is `00 00 00 00 00 00` - length 0, sector 0 - so index 34 has no reference table either. Contrast record 36 at offset 216, `00 00 09 | 01 69 6e`, a real 9-byte stored container. `RealCacheFixture.cs:55-64` drops 34 and 35 for exactly this reason and `RealCacheReferenceTableShapeTests.cs:152` pins the survivors at 35 tables.

What it WOULD hold, settled from what the 637 client does rather than from the constant's name: index 34 is the drop-in alternative to index 32, chosen when the JVM cannot decode images. `InterfaceSettings.java:72-74` sets `Node_Sub5_Sub2.aBoolean5535 = !Class116.method2162(false)` and then opens `(!aBoolean5535 ? 32 : 34)` into `Class1.aJS5Archive_67`. `Class116.method2162` (Class116.java:60-73) gunzips a hardcoded blob (`Class74.aByteArray546` = `{31,-117,8,...}`, gzip magic 1F 8B 08) through `Class263.method3220` (Class263.java:202) and hands it to `Class271.method3277` (Class271.java:29), which is `Toolkit.getDefaultToolkit().createImage()` + MediaTracker + PixelGrabber; any exception returns false. So the flag means "AWT image decoding is broken here", and that is the case that selects 34.

The decoder choice is what settles the byte format. `Class237_Sub1.method2915` (Class237_Sub1.java:13-32) branches on the same flag: `!aBoolean5535` decodes the file bytes through `Class271.method3277` (AWT/JPEG); otherwise through `Class324.method3683` (Class324.java:16-23) into `Class324.method3690` (Class324.java:44+), which is the Jagex-native sprite container - u16 frame count at `length-2`, seek to `length - 7 - n*8` for width/height/paletteSize, four u16 arrays (offsetX, offsetY, subWidth, subHeight), 3-byte palette entries read backwards from there, then per-frame a flags byte (bit0 vertical, bit1 alpha) and palette-index pixels. So index 32 stores AWT-decodable image bytes and index 34 stores the same loading-screen media as Jagex sprite sets. The name LOADING_SPRITES_RAW happens to be right, but only the client's decoder choice proves it.

Addressing, had it any content: a GROUP is one sprite set, reachable by id or by name hash - `Class84.method834` resolves `"p11_full"`, `"p12_full"` and `"b12_full"` by name against this same archive handle, and CLAUDE.md records the identifiers flag `0x01` set on index 32. A FILE is file 0 of the group; `Class324.method3683` reads via `JS5Archive.method2733`, the single-file accessor, so the whole group payload is the sprite set. A RECORD is one frame within that set.

## Current capability

Nothing beyond naming it, and there is nothing to do. Every reference to index 34 in the entire solution is two lines of constants:
- `FlashEditor/Cache/RSConstants.cs:49` - `LOADING_SPRITES_RAW = 34, //in jagex format`
- `FlashEditor/Cache/RSConstants.cs:99` - `"LOADING_SPRITES_RAW"` in `indexNames`, consumed only by `GetIndexName` (RSConstants.cs:111-120) for GUI labels.

No decoder, no encoder, no codec test, no sweep, no tab:
- `FlashEditor/Editor.cs:64-76` (`editorTypes`) enumerates the nine indexes that own a tab - 19, 8, 18, 16, 3, 7, 9, 5, 6 - and 34 is not among them. The `-1` entry is the main menu and maps to META_INDEX (Editor.cs:472-476), so 34 does not surface there either.
- Grepping `FlashEditor.Tests/` for `LOADING_SPRITES_RAW` returns nothing. The only test-side acknowledgement of the index existing at all is a comment, `RealCacheReferenceTableShapeTests.cs:149` ("34 and 35 hold nothing at all"), plus the 35-table assertion on :152 that the exclusion produces.

The read path does open the index file and survive it: `RSFileStore.cs:33-36` adds idx34 to `indexChannels` because the file exists, `GetFileCount(34)` returns 0, and `RSCache.LoadReferenceTables` (RSCache.cs:542-555) loops 0..35, hits `GetReferenceTable(34)` -> `GetContainer(META_INDEX, 34)` -> null container -> `FileNotFoundException` (RSCache.cs:571-576, :425-426), which the loop catches and logs. Graceful, and correct - there is nothing there.

## Gaps

- Nothing. The index declares zero groups and has no reference table, so there is no decoder, encoder, sweep or tab that could be written against data that does not exist. Any work here would be speculative code with no test that could fail.
- If a populated index 34 ever arrived (a different cache, or a repack): the codec is already written and would not need authoring. `SpriteDefinition` (FlashEditor/Definitions/Sprites/SpriteDefinition.cs:63 Decode, :249 Encode) is field-for-field `Class324.method3690` - same u16 count at length-2, same `length - size*8 - 7` seek, same four u16 arrays, same 3-byte palette with the 0->1 substitution, same flags byte. The work would be a rebind, not a new format.
- The rebind itself: `RSCache.GetSprite` hardcodes `RSConstants.SPRITES_INDEX` (RSCache.cs:691). It would need an index parameter to serve 34.
- A byte-identity sweep for the sprite codec - which does NOT exist today for index 8 either. No source file under FlashEditor.Tests/ mentions `SpriteDefinition` or `GetSprite`; the only hits are the compiled FlashEditor.dll in the tests' bin folder. So `SpriteDefinition.Encode` is unproven, and anyone reusing it for index 34 would inherit an unproven encoder. Per CLAUDE.md, a byte-identity sweep over index 8 is the prerequisite, not an extra.

## Notes and traps

Traps, for anyone who reopens this:

1. DO NOT read the emptiness as a defect to fix. Index 34 is the fallback rendition, only ever opened when `Class116.method2162` fails, i.e. on a JVM whose AWT cannot decode an image. A cache shipped for a normal JVM populates 32 and leaves 34 empty. This one does. AGENTS.md's index table already carries `-` for 34's group and file counts.

2. The two "empty" states are different and both must survive. Index 34 and 35 have NO meta record (length 0, sector 0). Index 36 HAS one: a 9-byte container holding a 4-byte format-5 stub declaring zero groups, which CLAUDE.md notes once took the whole decode down through `Max()` on an empty sequence. Do not conflate them, and do not "tidy" the 34/35 path into the 36 path.

3. `RSCache.LoadReferenceTables` loops `indexId < store.GetIndexCount()` and `GetIndexCount` (RSFileStore.cs:68-80) returns the MAXIMUM non-meta index, which is 36 here. So the loop covers 0..35 and never loads index 36's table at all. That is a separate off-by-one from index 34's story, but anyone touching this loop to "handle 34" will trip over it. Out of scope for this report; flagged so it is not discovered the hard way.

4. `LOADING_SPRITES_RAW` is one of the ~27 `RSConstants` index constants with no adoption site. CLAUDE.md is explicit that this does NOT mean someone used a magic number - the production project has zero bare index literals - it means there is no feature for the index. Leave the constant as documentation; do not delete it as dead code.

5. Client-reading trap already resolved, so nobody re-treads it: `Class237_Sub1.java:24` reads `if(!client.aBoolean3553) break;` inside the AWT branch, which looks like the AWT result is sometimes overwritten by the `Class324` decode of the same bytes - i.e. like both formats live in one index. It is not. `client.aBoolean3553` is declared at client.java:25 and assigned in exactly one place, client.java:2841-2843, inside the teardown method and gated on `Applet_Sub1.anInt4 != 0`. It is the shutdown flag, false in normal operation, so the break always fires and the two decode paths are mutually exclusive. Standard JODE opaque-predicate noise.

6. No XTEA, no dependency on another index, no 637-vs-639 divergence to worry about - none of it is reachable, because there are no bytes. If index 34 is ever populated, expect it to mirror index 32's shape: name hashes present (identifiers `0x01` is set on 32 per CLAUDE.md's measured flag table, and `Class84.method834` looks groups up by the names "p11_full"/"p12_full"/"b12_full"), one sprite set per group, single file per group.
