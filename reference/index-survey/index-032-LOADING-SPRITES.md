# Index 32 - LOADING_SPRITES

**Format:** fully-understood  
**Capability:** none  
**Effort:** medium

## What it is

The pre-login art store: everything the loading screen draws before the main cache is open. idx32 is 22,770 bytes = 3795 slots, but the reference table (idx255[32], 296-byte GZip container -> 580-byte table, format 6, version 7, flags 0x01) declares only **26 groups**, at sparse ids 494, 495, 496, 3762-3765, 3769-3777, 3779-3786, 3793, 3794. Every group holds exactly **one file** (file id 0), so a group IS a record and the group payload IS the file - no size table, no chunk byte. Compression is 21 GZip and 5 BZip2; every container carries a 2-byte version trailer; none is XTEA encrypted.

TWO payload shapes, measured by decompressing all 26:
- **21 JPEGs** (groups 3762-3786), magic FF D8 FF DB ... FF D9. Eleven are 384x254 or 384x253 backdrops; the rest are small furniture (16x12, 32x47, 36x47 x2, 5x18 x2, 187x4 x2, 93x13).
- **5 Jagex sprite sets** (groups 494, 495, 496, 3793, 3794), each 256 frames with a 2-colour palette - i.e. font glyph sets. Canvases 11x12, 13x16, 14x16, 18x38, 16x39.

Names recovered by Java String.hashCode over the group identifiers: **494 = p11_full, 495 = p12_full, 496 = b12_full** - all three appear verbatim as string literals in the client at Class84.java:23-26, so the join is self-proving, not coverage-based. 3793 = verdana_11pt_regular by the same hash. The other 22 identifiers are non-zero but unrecovered.

Client authority (Hydra 637):
- InterfaceSettings.java:72-74 opens index 32 (or 34 when JPEG decoding is unavailable): `aBoolean5535 = !Class116.method2162(false); openFileStore(-79, false, 1, !aBoolean5535 ? 32 : 34)`. Class116.java:60-77 probes a bundled test image through Class263/Class271.method3277, so 32 is the JPEG variant and 34 the raw fallback.
- Class237_Sub1.java:13-32 is the only reader of an index-32 payload as an image: `Class271.method3277(JS5Archive.method2733(i, 58), 1)`, and Class271.java:29-65 hands the bytes straight to `Toolkit.getDefaultToolkit().createImage(byte[])` + PixelGrabber - a plain AWT image decode.
- JS5Archive.java:591-616 (`method2733`) throws unless the group holds exactly one file, which is why every index-32 group has fileCount 1.
- Class84.java:20-31, called at InterfaceSettings.java:93, does `requestFile("p11_full"/"p12_full"/"b12_full")` against index 32 - a by-name lookup, which is why this table sets the identifiers flag.
- Class373.java:164-178 is the font path: metrics come from index **13** via Class119_Sub1.method2182, glyphs from index **32** via Class324.method3684, at the same group id. Class324.java:25-32 -> :43-47 sets `caret = len - 2; readUnsignedShort()`, which is exactly the Jagex sprite-set decoder in SpriteDefinition.cs:66-67.
- InterfaceSettings.java:97-115 builds the screens from index 33's definitions and `new Class362(index32, index13)` (Class362.java:94-134), so index 32 supplies pixels and index 33 says where they go.

## Current capability

Nothing index-specific. `RSConstants.LOADING_SPRITES = 32` at FlashEditor/Cache/RSConstants.cs:47 and the display name at :97 are the ONLY two references to index 32 anywhere in FlashEditor/ or FlashEditor.Tests/ - a repo-wide grep for LOADING_SPRITES returns those two lines plus the AGENTS.md table row, and no test file mentions the index at all. No decoder, no viewer, no export, no tab. Editor.cs:63-75 (`editorTypes`) does not list it and Editor.Designer.cs:40-148 has no matching TabPage. This is exactly the case CLAUDE.md describes: an unadopted index constant means the editor has no feature for that index, not that someone used a magic number.

What DOES already work, generically, is the byte layer - and it is byte-identity proven for all 26 groups:
- ReferenceTableCodec.cs:19-155 / :160-313 handles index 32's exact table shape, including the per-file identifier block at :141-152 / :297-309 that this table's 0x01 flag turns on.
- RealCacheFixture.cs:24 caps sampling at 250 archives per index and :122-134 returns every archive when the count is under the cap, so index 32's 26 groups are ALL swept on every run, sampled or FULL.
- RealCacheConformanceTests.cs:59 (table re-encodes to captured bytes), :119 (archive CRCs match the stored container), :169 (container payload+header survive re-encode), :218 (archive payload re-encodes byte-identically), :365 (single-file archives carry no trailer), :479 (index records re-encode).
So the editor can read and rewrite an index-32 group as an opaque blob without corrupting it. It cannot turn a single one of them into a picture.

Two existing pieces are near-misses rather than support: SpriteDefinition.Decode (Definitions/Sprites/SpriteDefinition.cs:63-179) parses the 5 font groups correctly - I verified exact consumption on all five, sum(subWidth*subHeight) + 256 flag bytes lands exactly on the palette start - but RSCache.GetSprite (Cache/RSCache.cs:735-751) hardcodes `RSConstants.SPRITES_INDEX`, so it can never be pointed at index 32; and SpriteDefinition.Encode (:249) is `throw new NotImplementedException()`.

## Gaps

- A payload-shape dispatcher: index 32 is NOT uniformly JPEG. Sniff the first two bytes - FF D8 means JPEG, anything else is a Jagex sprite set read from the tail. Dispatching on the index id instead breaks 5 of the 26 groups.
- A 4-component JPEG reader. These files have no JFIF APP0 and no Adobe APP14, so every standard decoder guesses CMYK and renders wrong colours. Components 0-2 are Y/Cb/Cr and component 3 is all zero in all 21 files - decode as YCbCr and discard component 3.
- Reuse of the Jagex sprite decoder for the 5 font groups: add an index parameter to RSCache.GetSprite (RSCache.cs:743 hardcodes SPRITES_INDEX), or a LoadingSpriteDefinition that calls SpriteDefinition.Decode directly.
- SpriteDefinition.Encode (SpriteDefinition.cs:249) is NotImplementedException. Without it the 5 font groups can be read but never written, so the write half of the index cannot be completed at all.
- A JPEG writer, plus the design decision that goes with it: a JPEG re-encode is no more reproducible than a GZip one, so the sweep has to be defended at the stored-payload level (keep the original bytes, write nothing when nothing changed) rather than by round-tripping pixels.
- A codec test against captured bytes for both shapes - one JPEG group and one 256-frame font group, asserted against bytes checked into FlashEditor.Tests, not against our own encoder.
- A full-index byte-identity sweep over all 26 groups: decode every group's content, re-encode, and assert the stored payload is unchanged. Nothing in the suite today decodes index-32 CONTENT; the six conformance tests only prove the container and archive wrappers.
- A GUI tab: a TabPage in Editor.Designer.cs following the SpriteEditorTab pattern (Editor.Designer.cs:85), an entry in editorTypes (Editor.cs:63-75), and a case in the background-worker switch alongside RSConstants.SPRITES_INDEX (Editor.cs:624).

## Notes and traps

TRAPS, most expensive first.

1. **The constant's own comment lies.** RSConstants.cs:47 says "in jpg format" and AGENTS.md:306 says "Loading sprites (JPEG)". Five of the 26 groups are not JPEG at all - they are 256-frame Jagex sprite sets holding font glyphs (494 p11_full, 495 p12_full, 496 b12_full, 3793 verdana_11pt_regular, 3794 unnamed). A JPEG-only decoder throws on 19% of the index. Dispatch on the payload magic.

2. **The JPEGs decode to the WRONG COLOURS through every standard library, silently.** Verified, not theorised: SOF0 of group 3769 is `ffc0 0014 08 00fe 0180 04 012200 021101 031101 042200` - 4 components, no APP0 and no APP14 anywhere in the file. `System.Drawing.Image.FromFile` opens it happily as PixelFormat 8207 (Format32bppCMYK) and PIL reports mode CMYK; I rendered both and the result is a recognisable but washed-out teal/pink image. Merging components 0-2 as YCbCr and dropping component 3 produces the correct artwork. Component 3 is min 0 max 0 across all 21 files, and shares Y's 2x2 sampling and quant table 0 - a dummy slot, not alpha. This is the single defect most likely to ship unnoticed, because the image looks like an image.

3. **Do not mistake the 104 trailing bytes for the known zero-tail.** CLAUDE.md records four indexes (9, 26, 27, 29) carrying four zero bytes per file with the identifiers flag CLEAR. Index 32's table also ends in 4 zero bytes per file (26 files x 4 = 104), but here the identifiers flag IS set, so it is a legitimate per-file name-hash block that happens to be all zero, and ReferenceTableCodec.cs:141-152 consumes it exactly - 580 of 580 bytes, nothing left over. Index 32 is not a fifth member of that family.

4. **idx32 has 3795 slots, 29 look populated, and the table declares 26.** RSCache.GetFileCount(32) returns 3795 (RSFileStore.cs:49-54, idx bytes / 6). Three populated slots are not in the reference table: slot 0 is malformed (length field 0xFF0000, sector 0), while slots 498 and 1407 are REAL orphan containers with valid sector headers (idx=32, matching ids) holding a 14,309-byte GZip and a 59,233-byte BZip2 - leftovers from a repack. Enumerate `referenceTable.GetArchiveEntries()` the way Editor.cs:642 already does for sprites; anything that walks idx slots will hit all three.

5. **Group ids are sparse and far larger than the group count** (26 groups spread over 494..3794). Any array sized by group count and indexed by group id is an out-of-range crash.

6. **Five BZip2 groups.** AGENTS.md records that 19 of the cache's 1,743 BZip2 containers do not round-trip byte-identically. Whether any of index 32's five are among them is unverified - and the conformance sweep would not tell you, because it compares decompressed payloads, not compressed bytes.

7. **Index 32 is not self-describing.** The loading-screen layout that names which group to draw lives in index 33 (InterfaceSettings.java:97-115, Class362.java:94-134), and the fonts pair a glyph set here with metrics of the SAME group id in index 13 (Class373.java:171-174). A viewer can show the pictures without either; anything claiming to say what a picture is FOR needs index 33.

8. **Index 34 is the raw twin and is empty here** (1-byte idx file). The client picks 34 over 32 when JPEG decoding is unavailable (InterfaceSettings.java:72-74), so that whole branch has no data in this cache to test against.

9. **The 3 recovered font names are proven; the other 22 are not.** 494/495/496 hash-match string literals that exist in the client (Class84.java:23-26) - self-proving. verdana_11pt_regular for 3793 came from a hand-typed candidate list, a 1-in-2^32 hash hit and structurally consistent (3793 is one of the five font-shaped groups), but it is not corroborated by the client. I could not recover the 21 JPEG names from either the client's literals or a ~4,000-candidate brute force. Do not invent them.
