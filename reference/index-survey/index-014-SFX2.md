# Index 14 - SFX2 (sound-effect sample bank, Vorbis)

**Format:** fully-understood  
**Capability:** none  
**Effort:** large

## What it is

Index 14 is the sound-effect sample bank, stored as stripped Vorbis. The client opens it at `InterfaceSettings.java:170` (`Class196.aJS5Archive_1512 = openFileStore(-91, false, 1, 14)`) and only `Node_Sub13` reads it.

STRUCTURE. 3657 groups, one file per group (the client's `method2733` special-cases fileCount==1 at `JS5Archive.java:608-609`). A group id IS a sound-effect id: `Class280.java:206` calls `Node_Sub13.method1137(Class196.aJS5Archive_1512, class98_sub42.anInt4210)` with the ambient-sound config's raw id.

GROUP 0 IS NOT A SAMPLE. `Node_Sub13.method1133:32` fetches `getChildFromFolder(0, 0)` once and hands it to `method1143`, which parses it as a Vorbis setup header: two 4-bit blocksize exponents, then `read(8)+1` codebooks (`Class71`), `read(6)+1` time-domain transforms (read and discarded, `:184-186`), floors (`Class56`), residues (`Class311`), mappings (`Class371`), and modes (blockflag bit, two 16-bit fields, 8-bit mapping index, `:205-210`). `Class71:44` opens by skipping `read(24)` — the codebook sync pattern — and `Node_Sub13.method1139` is verbatim Vorbis `float32_unpack` (21-bit mantissa, exponent bias 788). Measured in this cache: group 0 inflates to 2593 bytes, its first bytes are `aa 16 42 43 56 ...` — `42 43 56` is 0x564342, the Vorbis codebook sync pattern, self-proving. 23 codebooks; blocksize0 = blocksize1 = 1024.

GROUPS 1..3656 ARE SAMPLE RECORDS, decoded by `Node_Sub13.method1142:494-518`. A record is a 20-byte header of five big-endian int32s, then a packet list:
  int32 sampleRate        (:497)
  int32 pcmByteCount      (:498, sizes the output buffer at :250)
  int32 loopStart         (:499)
  int32 loopEnd           (:500, sign bit = loop flag: `if (<0) { loopEnd = ~loopEnd; looping = true }` :501-504)
  int32 packetCount       (:505)
  packetCount x { varint length, then that many raw Vorbis audio packet bytes }  (:507-517)
The length varint is `do { b = readUnsignedByte(); len += b } while (b >= 255)` (:510-513) — a canonical base-255 encoding, so 255 is written `255,00`.

One record = one mono sound effect. Output is 8-bit signed PCM (`method1132:266-270` maps the float to `(int)(128 + f*128)`, clamps, writes `byte(i-128)`), wrapped into `Node_Sub24_Sub1(sampleRate, pcm, loopStart, loopEnd, looping)` at `:281`.

MEASURED OVER THE WHOLE INDEX (my own extraction from cache/main_file_cache.dat2, all 3656 sample groups): the 20-byte header + varint packet walk consumes every payload to the exact byte, 3655 of 3655 checked (group 1971 is BZip2 and my script skipped it). Sample rates: 22050 x3626, 44100 x19 (groups 1972-1990), 8000 x3, 10000 x2, and one each of 4000, 12000, 19000, 20000, 22000. 11 looping samples: groups 287, 340, 341, 342, 356, 359, 411, 418, 482, 508, 568. Container compression: 3131 uncompressed, 524 GZip, 1 BZip2 (group 1971); all carry a 2-byte version trailer. Decompressed payloads: min 58 B, median 6328 B, max 156,960 B, 38,214,335 B total.

## Current capability

Nothing index-specific. The only reference to index 14 anywhere in the repo is its constant declaration:

  `FlashEditor/Cache/RSConstants.cs:29` — `SFX2_INDEX = 14, //VORBIS/midi instruments`
  `FlashEditor/Cache/RSConstants.cs:79` — the display string `"SFX2"`

A repo-wide grep for `SFX2_INDEX` returns exactly one hit: that declaration. It has zero adoption sites, which per CLAUDE.md's rule ("RSConstants is already fully adopted; the production project has zero bare integer index literals") means the editor has no feature for this index, not that someone used a magic number.

- No decoder. No `Definitions/` type for samples; `FlashEditor/Definitions/` holds item, NPC, object, model, sprite, texture only. A grep for Sound/Sfx/Sample/Audio/Ogg/Wav across `FlashEditor/` hits 7 files, all unrelated (texture sampling, object/NPC sound-id fields, designer resources).
- No encoder.
- No GUI tab. `Editor.cs:64-76` (`editorTypes`) enumerates the nine indexes that get tabs — items, sprites, NPCs, objects, interfaces, models, textures, maps, music. 14 is absent, so the tab dispatch `switch` at `Editor.cs:526-851` has no arm for it.
- No test. Nothing under `FlashEditor.Tests/` mentions index 14 or SFX2.
- `STATE_OF_THE_EDITOR.md:108` states the position directly: "10-15 | Huffman, music2, cs2, fonts, sfx | - | - | - | -" — nothing decoded, no viewer, not editable, no write-back.

What DOES work is index-agnostic transport, and it should not be mistaken for index-14 capability. `RSCache.GetContainer`/`ReadFile` will hand back the raw bytes of any index-14 group, and `RealCacheConformanceTests.cs:126,175,226,302` sweeps `_cache.TableIndexes` (which includes 14) asserting container round-trip, trailer length and chunk layout. That is the sector/container/archive wrapper only. The 20-byte-header-plus-packet-list payload is completely unparsed.

## Gaps

- A record definition class with Decode/Encode - e.g. FlashEditor/Definitions/Audio/SfxSample.cs - reading the five int32 header fields plus the base-255-varint-prefixed packet blobs per Node_Sub13.method1142:494-518. This is a small, well-bounded codec; the format is fully pinned and I proved exact consumption over all 3655 non-BZip2 groups.
- A separate codebook/setup-header type for group 0 (VorbisSetup) - group 0 is structurally unlike 1..3656 and must not go through the sample decoder. Decode/Encode of it needs the LSB-first bit reader from Node_Sub13.method1134/1138 plus the Class71/Class56/Class311/Class371 sub-decoders.
- A codec test against captured bytes, in the style of FlashEditor.Tests/Cache/CapturedCacheBytesTests.cs - not a round-trip of our encoder against our decoder, which CLAUDE.md warns proves nothing.
- A full-index byte-identity sweep, [RealCacheFact] + RealCacheFixture, asserting all 3657 groups re-encode to the bytes read. Compare DECOMPRESSED payloads, never the stored container (524 of these groups are GZip and a GZip re-encode is never byte-identical). This is the piece that would make the claim defensible; index 14 currently has none.
- An actual Vorbis decoder to make a GUI tab meaningful - port Node_Sub13.method1135 (the IMDCT, :284-492), Class71 (codebooks), Class56 (floors), Class311 (residues), Class371 (mappings). This is the bulk of the work and cannot be shortcut with an off-the-shelf library, because group 0 is not a well-formed Vorbis setup packet (see notes).
- A GUI tab following the Editor.Designer.cs pattern: add RSConstants.SFX2_INDEX to Editor.cs:64-76 editorTypes in tab order, add a case arm to the dispatch switch at Editor.cs:526+, and a load path alongside the maps/music special cases at Editor.cs:485-499. Minimum useful content: id, sample rate, PCM length, loop start/end/flag, packet count; ideally playback and WAV import/export.
- A write path - nothing calls RSCache.WriteFile for index 14, and there is no encode-side re-slice for a single-file group.

## Notes and traps

TRAPS.

1. GROUP 0 IS A CATEGORY ERROR AS A SAMPLE. It has no 20-byte header. Guard on it explicitly. The self-proving check is bytes 2-4 == `42 43 56` (Vorbis codebook sync 0x564342, skipped by the `read(24)` at Class71:44).

2. GROUP 0 IS NOT FEEDABLE TO LIBVORBIS/STB_VORBIS. It is a hybrid: the two blocksize nibbles from the Vorbis IDENTIFICATION header prepended to a Vorbis SETUP header, with no `\x01vorbis`/`\x05vorbis` magic, no channel count, no sample rate, and no framing bit. Channels are implicitly mono (method1132 emits one byte per sample). Any port must be hand-written against Node_Sub13/Class71, not delegated.

3. blocksize0 == blocksize1 == 1024 in this cache. A decoder ported from a reference Vorbis implementation that assumes bs0 < bs1 will assert or mis-window. The client handles it fine because method1143:141-177 builds both tables independently.

4. THE LOOP FLAG IS THE SIGN BIT OF loopEnd. `Node_Sub13.java:501-503` does `if (anInt3900 < 0) { anInt3900 = anInt3900 ^ 0xffffffff; aBoolean3890 = true }` — that is `~`, not negation. On re-encode a looping sample must write `~loopEnd`. Verified on group 287: stored loopEnd is -21174, ~(-21174) = 21173 = its pcmByteCount. Only 11 groups loop (287, 340-342, 356, 359, 411, 418, 482, 508, 568), so a sampled test misses this; a full-index byte-identity sweep catches it.

5. NON-LOOPING SAMPLES STILL CARRY MEANINGFUL loopStart/loopEnd. Group 1 is non-looping with loopStart=42292, loopEnd=62110. Do not zero them when the flag is clear or 3645 groups stop round-tripping.

6. THE PACKET LENGTH VARINT IS BASE-255 AND CONTINUES ON `>= 255`, NOT `> 255`. So 255 is written `FF 00` (two bytes) and 510 is `FF FF 00`. The encoding is forced and unique — good news, this is one of the few things in this cache that IS canonical — but `while (n >= 255) { put 255; n -= 255 } put n` is the only correct encoder. An encoder capping at 254 silently produces the wrong length.

7. COMPRESSION IS MOSTLY NONE, WHICH IS UNUSUAL AND HELPFUL. 3131 of 3656 sample groups are stored uncompressed, 524 GZip, 1 BZip2 (group 1971). Index 14 alone holds most of the cache's 4,480 uncompressed containers (AGENTS.md:137). The 3131 uncompressed ones will round-trip byte-exactly at the container level; the 524 GZip ones never will (AGENTS.md: 0 of 96,183). A byte-identity sweep must compare decompressed payloads, and must preserve each group's stored compression type rather than picking one.

8. NO NAMES. Index 14's reference table does not set the identifiers flag — AGENTS.md:86, measured by RealCacheReferenceTableShapeTests, lists identifiers on 3, 5, 6, 8, 10, 12, 13, 23, 30, 31, 32, 33 and 14 is not among them. So a GUI tab can only list numeric ids. Same limitation as index 11 jingles. Do not attempt a name join; CLAUDE.md's track-name warning applies directly.

9. DEPENDENCY ON INDEX 15 AND 4. `Particle_Sub3_Sub5_Sub2.java:99-100` hands index 15 (the MIDI patch bank), index 14 and index 4 to the synth together, and per AGENTS.md:321-323 `Class355.method3875` maps a MIDI program to samples drawn from 14 and 4. Object and NPC definitions also carry sound-effect ids. So group ids in index 14 are external references — renumbering or inserting groups breaks index 15 and the definition indexes silently.

10. `method1132(new int[]{22050})` at Class280.java:211 IS NOT A SAMPLE RATE. It is a mutable PCM-BYTE BUDGET for one incremental decode call (`is[0] -= i - anInt3913`, :273). Reading it as a rate would be an easy and wrong inference; the real rate is the record's first int32, and 29 of 3655 groups are not 22050.

11. NO XTEA anywhere on this index (no keys, table is format 6, no per-archive flags byte exists on disk in this cache at all).

12. NO CLIENT BUG FOUND on this path. Node_Sub13 reads what is on disk correctly, and 637-vs-639 shows no divergence: every 639 record parses to exact consumption under the 637 reader.

13. All 3657 slots are populated — no absent groups, so there is no absent-versus-default hazard here.
