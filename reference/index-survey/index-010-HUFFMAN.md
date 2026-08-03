# Index 10 - HUFFMAN

**Format:** fully-understood  
**Capability:** none  
**Effort:** small

## What it is

A single 256-byte Huffman code-length table used to compress and decompress chat text. It is one group, not two.

Measured from the cache directly (read-only hex dumps, no test run):
- `main_file_cache.idx10` is 12 bytes = 2 slots, but slot 0 is a dead record: `ff 00 00 | 00 00 00` (length 0xFF0000, sector 0). Slot 1 is the only live one: length 121, sector 9350.
- The idx255 record for index 10 is at offset 60: length 35, sector 1157. That container decodes (compression 0, compressedSize 30, no version trailer) to a format-6 reference table: format 6, version 5, flags `0x01` (identifiers), groupCount 1, group id delta `0x0001` -> **group id 1**, identifier `0x4afc73ad`, CRC `0x78117e4e`, version 2, fileCount 1, file id 0, file identifier `0x00000000`. Consumes to the byte (30/30) - index 10 is not one of the four trailing-bytes indexes.
- `0x4afc73ad` is exactly Java `String.hashCode("huffman")` (computed: `0x4afc73ad`). That is a self-proving join, not a plausible one.
- The group container at sector 9350 has sector header archiveId 1, chunk 0, next 0, indexId 10. Container: compression `02` (GZip), compressedSize `0x6e` = 110, **uncompressedSize `0x00000100` = 256**, gzip ISIZE trailer also 256, then a 2-byte version trailer `00 02` matching the table's version 2. 5+110+4+2 = 121 = the idx10 length.

So: **group = the whole `"huffman"` blob (id 1). File = the same bytes (single-file archive, id 0, no trailer, no name). One record = one byte of the file: the Huffman code bit-length for data value `i`, for i in 0..255.** A length of 0 means that byte value has no codeword.

Client authority for the read: `InterfaceSettings.java:167` opens index 10 into `Node_Sub40.aJS5Archive_4198`; `InterfaceSettings.java:310` does `new Class213(Node_Sub40.aJS5Archive_4198.method2739("huffman", "", -32734))` - fetched **by name**, not by id. `Class213(byte[] is)` (`Class213.java:182-276`) treats every byte as a bit length: `int i_27_ = is[i_26_]` (`:195`), skips zeros, and builds a codeword table `anIntArray1608` plus a decode tree `anIntArray1606`. `Class213.method2780` (`:278`) compresses, `Class213.method2782` (`:344`) decompresses. `aByteArray1605[i_10_]` is indexed by a raw byte value 0..255 at `:294`, which is why the file must be 256 long.

What it is used for: `Class284_Sub1_Sub1.method3368` (`:35-45`) writes a smart length then Huffman-compresses an outbound chat string into the packet buffer; `Node_Sub10_Sub26.method1084` (`:24-40`) reads a smart length and Huffman-decompresses an inbound chat string, returning the literal `"Cabbage"` on any exception. It is the chat text codec and nothing else.

## Current capability

Nothing index-specific. The only mention of this index anywhere in the production project is the constant declaration itself:

- `FlashEditor/Cache/RSConstants.cs:25` - `HUFFMAN_INDEX = 10,`
- `FlashEditor/Cache/RSConstants.cs:75` - the string `"HUFFMAN"` in `indexNames`

`grep -rn "HUFFMAN_INDEX" --include=*.cs .` returns that declaration and nothing else. Zero adoption sites, which per CLAUDE.md means "no feature for that index yet", not a magic number in hiding. There is no Huffman definition class, no decode/encode of code lengths, no code-tree builder, no chat compress/decompress, no test, and no GUI tab (`Editor.Designer.cs:64-148` lists Item, Sprite, NPC, Object, Interface, ModelViewer, TextureViewer, MapEditor, TrackEditor - no Huffman). `STATE_OF_THE_EDITOR.md:108` already records the row `| 10-15 | Huffman, music2, cs2, fonts, sfx | - | - | - | - |`.

What does work is only the generic cache layer, which applies to every index equally:
- `RSCache.LoadReferenceTables()` (`FlashEditor/Cache/RSCache.cs:544`, called from the constructor at `:72`) decodes index 10's reference table on every cache open, so it appears in the Meta tab's Reference Tables list (`Editor.cs:556`, `Editor.Designer.cs:291,297`).
- `RSCache.GetContainer(10, 1)` (`RSCache.cs:390`) and `RSCache.ReadFileBytes(10, 1, 0)` (`RSCache.cs:783`) return the 256 raw bytes; `RSCache.WriteFile` would stage a replacement. Neither knows what the bytes mean.
- The container and reference-table layers are byte-identity swept over every index including 10, by `FlashEditor.Tests/Cache/RealCacheConformanceTests.cs:58` (`ReferenceTables_ReEncodeToTheCapturedBytes`), `:169`, `:218` and `:365` (`SingleFileArchives_CarryNoTrailerInTheCapturedBytes`, which is the sweep index 10's group actually lands in, being single-file). That proves the wrapper, not the content.

## Gaps

- A `HuffmanTable` definition class with `Decode(JagStream)` / `Encode()`. Decode is: read exactly 256 bytes as signed-or-unsigned bit lengths (see traps - the client reads them signed). Encode writes the 256 stored bytes back verbatim. The derived structures (codewords, decode tree) must be rebuilt from the lengths, never stored, exactly as `Class213.java:182-276` does.
- A port of the codeword assignment in `Class213.java:191-272` and the tree walk in `:344-497`, so the editor can actually compress and decompress a string. Without it the tab is a hex viewer with prettier labels. `Class41.method366(a,b)` used at `:216` and `:308` is just `a | b` (`Class41.java:25-32`).
- A codec test against captured bytes: take the real 256-byte payload, decode, re-encode, assert byte identity. Also assert the shape - length is exactly 256, and at least one length is 0 (an unencodable byte value) if the shipped table has any.
- A round-trip *semantic* test that is not encoder-against-decoder: compress a known string with our port and decompress it with our port is worthless on its own (CLAUDE.md). Pin it instead against the client's algorithm by asserting specific codewords for specific byte values, derived by hand from `Class213`'s constructor.
- A full-index byte-identity sweep. It sweeps exactly one group, so it is three lines, but it must be there and must assert on the *decompressed* 256-byte payload, never the GZip container.
- A GUI tab following the `Editor.Designer.cs` pattern (a `TabPage` field declared around `:143-148`, added to `EditorTabControl`, with an entry in `editorTypes` and the `LoadEditorTab` switch in `Editor.cs:500-560`). Useful content: 256 rows of (byte value, ASCII char, bit length, derived codeword in binary), an editable bit-length column, plus a live encode/decode text box so an edit can be seen to work.

## Notes and traps

Traps, all evidence-backed:

1. **The group id is 1, not 0.** `ReadFile(RSConstants.HUFFMAN_INDEX, 0, 0)` gets nothing. The reference table declares exactly one group and its delta-decoded id is 1 (raw table bytes `00 01` after the group count). idx10 slot 0 is a dead record - `ff 00 00 | 00 00 00`, a length of 0xFF0000 pointing at sector 0. The "2 groups" from filesize/6 is a hole, not a second blob. Do not build anything that assumes group 0, and do not "repair" slot 0.

2. **Resolve it by name hash, not by id.** The client itself never uses the id: `InterfaceSettings.java:310` calls `method2739("huffman", "", ...)`. Index 10 sets the identifiers flag (`0x01`), the group identifier is `0x4afc73ad` = Java `hashCode("huffman")`, and the file's identifier is `0x00000000` (unnamed). Keying on the hash survives a repack that renumbers the group; keying on 1 does not.

3. **Byte identity must be asserted on the 256-byte payload, never the container.** The group is GZip, and AGENTS.md measures 0 of 96,183 GZip containers as byte-identical after a re-encode. A sweep comparing stored bytes here fails for a reason that has nothing to do with Huffman.

4. **The code-length table is the canonical form, but the codeword assignment is not plain canonical Huffman.** `Class213.java:191-235` maintains a 33-entry `is_24_` working array and backtracks through shorter lengths when a prefix collides, which is not the textbook "sort by length, increment" assignment. If you port only the lengths and generate codewords with a standard canonical-Huffman routine, you may get a table that decodes its own output fine and disagrees with the client on the wire. Port `Class213`'s constructor literally.

5. **A bit length of 0 means "no codeword", and the client throws on it.** `Class213.java:296-298`: `if((i_12_ ^ 0xffffffff) == -1) throw new RuntimeException("No codeword for data value " + i_10_)`. An editor that lets a user zero a length is quietly making that character unsendable. Validate on edit.

6. **The lengths are read as a signed Java byte** - `int i_27_ = is[i_26_]` at `:195` with `is` a `byte[]`. Then `1 << -i_27_ + 32` at `:198` and shift counts derived from it. Any length above 127 would go negative and corrupt the tree. The shipped table almost certainly has none (max useful Huffman length here is well under 32, and the working array `is_24_` is sized 33), but a C# port using `byte` where the client uses `sbyte` will silently diverge if an edited table ever exceeds 127. Mirror the client's signedness; only the client can settle this, the data cannot.

7. **No dependencies and no encryption.** Index 10 is opened at `InterfaceSettings.java:167` with the same flags as the ordinary indexes, it carries no XTEA keys, and nothing else in the cache references it. Its container is a plain single-file GZip archive - no size table, no chunk-count byte (the payload simply is the file, pinned by `RealCacheConformanceTests.cs:365`). This is the cleanest index in the cache to implement; the whole cost is the codeword/tree port and the tab.

8. **637 vs 639**: no evidence of any format change. The container is a raw 256-byte length table with no version or opcode structure that could have drifted, and the 637 client's reader accepts it as-is. Nothing here needs the "data vetoes" rule.

9. Corroborating doc, treat as a claim not evidence: `HYDRA_CACHE_SPEC.md:467` labels index 10 "Binary data (Huffman, etc.) ... Miscellaneous binary" and puts 2 in the weight column. The "etc." and "miscellaneous" are wrong for this cache - there is exactly one group and it is the huffman table. The 2 is the JS5 priority weight, not a group count; do not read it as confirmation of two groups.
