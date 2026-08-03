# Index 11 - MUSIC_2 (jingles)

**Format:** fully-understood  
**Capability:** read-only  
**Effort:** large

## What it is

Jingles: short, non-looping music stings the server or a clientscript fires (level-up, quest complete). Same packed-MIDI format as index 6, different bank.

Client chain, all verified by reading it: `InterfaceSettings.java:168` opens index 11 as `Class61.aJS5Archive_481 = Class42_Sub3.openFileStore(-100, false, 1, 11)` (fileType 1, no keys). It is consumed at `Class228.java:23`, reached from the CS2 opcodes at `Class247.java:2013,2036` and from a server packet at `PacketParser.java:2073` that reads a volume byte and a 16-bit id, with `0xFFFF` meaning "stop". `Class228:23` calls `s_Sub1.method3434(archive, false, volume, id, 0, ...)` -> `StringNode.method1144` -> `Class64_Sub13.method604` which parks the archive in `Class269.aJS5Archive_2025` and the id/file in `Node_Sub18.anInt3951` / `Class76_Sub8.anInt3770` -> `ClientScript.java:55` `Node_Sub7.method985(archive, group, file)` -> `JS5Archive.getChildFromFolder(groupId, fileId)` (`JS5Archive.java:203`, which passes `null` XTEA keys). Tracing the argument shuffle: groupId = the jingle id, fileId = 0.

So: a GROUP is one jingle, addressed by id; it holds exactly ONE FILE, id 0; that file is one packed MIDI song. Decoded by `Node_Sub7`'s private constructor (`Node_Sub7.java:20-302`) into a standard MIDI file, then handed to the synthesiser with the sample banks from indexes 14/15/4.

Measured directly from the cache bytes (idx255 group 11, inflated 6182 bytes, consumed to the byte): format 6, version 48, flags 0x00, 441 groups, ids 0..440 contiguous, fileCount 1 for every one, 441 files total, no identifiers block. All 441 groups are GZip (compression 2) with a 2-byte version trailer. Payloads: min 21, median 1655, max 102768 bytes, 1,907,985 total. Every one of the 441 payloads starts with byte 0x17 (opcode 23 = set tempo). The three-byte header at the END of each file gives trackCount 2..20 and division 360 (x1), 480 (x173) or 960 (x267).

## Current capability

READ ONLY, with a GUI tab, and no encoder anywhere.

- Decoder: `FlashEditor/Definitions/Tracks/Track.cs:141` `Track.Decode(JagStream)` - a full port of `Node_Sub7`, four passes (opcode census, delta-time walk, controller-number replay + run-boundary cursors, re-interleave). Verified arm-for-arm against `Node_Sub7.java:183-300`, including the pitch-wheel low/high cursor order (`Track.cs:432-433` vs `Node_Sub7.java:271-273`) and the signed `sbyte` reads.
- Cache entry point: `FlashEditor/Cache/RSCache.cs:760` `GetTrack(indexId, groupId)` - takes the first valid file id from the reference-table entry, so it works on index 11's one-file groups.
- Index constant: `FlashEditor/Cache/RSConstants.cs:26` `MUSIC_2 = 11`.
- GUI: `FlashEditor/Definitions/Tracks/TrackEditorPanel.cs:38` lists index 11 as "Jingles" next to index 6; tab wired at `Editor.Designer.cs:1362-1370` and bound at `Editor.cs:493-497`. It is a `BackgroundWorker` sweep of both indexes with a list, a stats pane and `Export MIDI...` (`TrackEditorPanel.cs:310`). Export only - `File.WriteAllBytes` to disk, nothing writes back to the cache.
- Tests: `FlashEditor.Tests/Definitions/RealCacheTrackTests.cs:43` `EveryTrackDecodesToAStructurallyValidMidi` sweeps every group of index 6 and index 11 (`:32` `JingleIndex = RSConstants.MUSIC_2`) and checks two things that a decoder with wrong run boundaries cannot satisfy: emitted length reconciles with the packed file's own predicted length (`:78`), and the output is structurally valid MIDI - MThd, MTrk chunks that tile exactly, FF 2F 00 closing each (`:165-217`). `TrackNamesJoinOnTheArchiveNameHash:155-156` asserts every index-11 group identifier is -1.
- There is no `Encode` anywhere under `FlashEditor/Definitions/Tracks/` (grep: zero hits). No repack, no byte-identity sweep, no way to import a MIDI.

Grade: read-only. The generic `RSCache.WriteFile` could replace an index-11 file with arbitrary bytes, but nothing produces those bytes and no GUI path reaches it.

## Gaps

- Track.Encode: rebuild the packed file from a MIDI. This is the whole job. It needs the decoder to retain the raw run bytes (note/velocity/controller/pitch-wheel/tempo runs) as read, because the decode is lossy in the direction that matters - see the accumulator trap in notes. Realistically: Decode records the runs verbatim, Encode re-emits them, and only edited events get their deltas recomputed.
- A codec test against captured bytes: take a handful of real index-11 files (group 0 is 21 bytes at the small end, the 102,768-byte group at the large end), keep them as fixtures, and assert decode->encode reproduces them. CLAUDE.md is explicit that round-tripping this encoder against this decoder proves nothing.
- A full-index byte-identity sweep: all 441 index-11 groups (and the 963 in index 6) must re-encode to the bytes they were read from, in the style of the item/NPC/object/map sweeps. Nothing of the kind exists for either index today.
- GUI editing: TrackEditorPanel is export-only. It needs an Import MIDI / Replace action feeding RSCache.WriteFile(11, groupId, 0, bytes), plus the usual staged-save behaviour. The tab pattern itself is already right - it is code-built like MapEditorPanel and bound from Editor.cs:493, so no Editor.Designer.cs surgery is needed.

## Notes and traps

TRAPS, in the order they will bite.

1. THE ACCUMULATORS ARE NOT RESET PER TRACK, AND THEIR UNMASKED STATE IS LOST. Note, velocities, pitch wheel, channel pressure, key pressure, channel, controller number and the 128 per-controller values are all initialised once before the track loop (`Node_Sub7.java:174-182`, mirrored at `Track.cs:316-328`) and carry across every MTrk in the file. Each is a running sum of SIGNED byte deltas, and the output is `accumulator & 127`. So the accumulator routinely holds values outside 0..127 and the emitted MIDI cannot tell you what it was. An encoder that recomputes deltas from the decoded MIDI will produce a valid file with different bytes. This is the single reason a byte-identity encoder must keep the raw runs.

2. CLIENT BUG, and it hits every jingle. `Node_Sub7.java:196-199` gates the 0xFF meta status byte on the running-status test used for channel messages. Opcodes 7 (end of track) and 23 (set tempo) both mask to nibble 7, so an end-of-track directly after a tempo change loses its 0xFF and the chunk closes with a bare `2F 00`, which the MIDI spec forbids. Measured by walking pass 1 over the raw bytes of all 441 index-11 groups: 441 dropped status bytes, one in every one of the 441 groups. So the 637 client emits non-conformant MIDI for 100% of jingles. `Track.cs:354` writes the byte unconditionally and counts it in `RepairedMetaStatusBytes`; `RealCacheTrackTests.cs:78` adds it back before comparing lengths. An encoder must drop it again to hit byte identity - the packed file's own length prediction (`Node_Sub7.java:166`) does NOT allow for it.

3. THE HEADER IS THE LAST THREE BYTES, NOT THE FIRST (`Node_Sub7.java:22`, `Track.cs:150`). The opcode stream must start at offset 0 because pass 4 indexes the raw buffer from 0.

4. INDEX 11 DOES NOT EXERCISE THE WHOLE FORMAT. Opcode census over all 447,357 opcode bytes in index 11: low nibbles 0, 1, 2, 3, 6, 7 only. Nibble 4 (channel pressure) and nibble 5 (polyphonic key pressure) NEVER occur here. High nibbles 0-15 all occur, so the channel XOR-delta uses the full range. A sweep that passes on index 11 alone has not touched two arms of the decoder; index 6 must be swept too.

5. NO NAMES, EVER. The reference table flags byte is 0x00 (measured), so there is no identifiers block, no whirlpool, no sizes, no entry hash. `TrackNames` (index 17 enum 1345) is an index-6 mechanism only; every jingle arrives with NameHash -1 and stays unnamed (`TrackEditorPanel.cs:246`, asserted at `RealCacheTrackTests.cs:155-156`). Export file names fall back to `track_11_<id>.mid` (`TrackEditorPanel.cs:366`) - and the index must stay in the name because group ids restart at 0 in both 6 and 11.

6. NO XTEA, NO SURPRISES IN THE CONTAINER. `getChildFromFolder` passes null keys (`JS5Archive.java:203-205`); no index-11 group is in any key dump. All 441 containers are GZip with a 2-byte version trailer, so the standard "a GZip re-encode is never byte-identical" rule applies: any byte-identity sweep must compare the DECOMPRESSED payload, never the stored container.

7. THE GROUP HAS ONE FILE, SO THE ARCHIVE PAYLOAD HAS NO TRAILER AT ALL. fileCount is 1 for all 441 groups, and the client special-cases that: writing a size table or a chunk-count byte would be handed back as file data and grow the file on every save.

8. `Class247.java:2013,2036` and `PacketParser.java:2073` pass the jingle id as a raw 16-bit value with 0xFFFF reserved. Ids are contiguous 0..440 here, so any renumbering on repack breaks every server and every script that references one. Treat group ids as fixed.

9. 637 vs 639: no divergence found. Every opcode byte in this cache is handled by the 637 decoder - the census hit no value that reaches the client's `throw` (`Node_Sub7.java:62-68`, mirrored at `Track.cs:204`). The format is unchanged between the pair.

10. `RealCacheTrackTests` is the right shape but it is NOT a byte-identity sweep and must not be read as one. Its two checks are strong (the format's own length prediction, and MIDI structural validity) precisely because there is nothing to compare against; do not let them stand in for the sweep once an encoder exists.
