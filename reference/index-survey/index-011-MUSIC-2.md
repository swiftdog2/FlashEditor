# Index 11 - MUSIC_2 (jingles)

**Format:** fully-understood  
**Capability:** codec complete, no GUI write path  
**Effort:** large

## What it is

Jingles: short, non-looping music stings the server or a clientscript fires (level-up, quest complete). Same packed-MIDI format as index 6, different bank.

Client chain, all verified by reading it: `InterfaceSettings.java:168` opens index 11 as `Class61.aJS5Archive_481 = Class42_Sub3.openFileStore(-100, false, 1, 11)` (fileType 1, no keys). It is consumed at `Class228.java:23`, reached from the CS2 opcodes at `Class247.java:2013,2036` and from a server packet at `PacketParser.java:2073` that reads a volume byte and a 16-bit id, with `0xFFFF` meaning "stop". `Class228:23` calls `s_Sub1.method3434(archive, false, volume, id, 0, ...)` -> `StringNode.method1144` -> `Class64_Sub13.method604` which parks the archive in `Class269.aJS5Archive_2025` and the id/file in `Node_Sub18.anInt3951` / `Class76_Sub8.anInt3770` -> `ClientScript.java:55` `Node_Sub7.method985(archive, group, file)` -> `JS5Archive.getChildFromFolder(groupId, fileId)` (`JS5Archive.java:203`, which passes `null` XTEA keys). Tracing the argument shuffle: groupId = the jingle id, fileId = 0.

So: a GROUP is one jingle, addressed by id; it holds exactly ONE FILE, id 0; that file is one packed MIDI song. Decoded by `Node_Sub7`'s private constructor (`Node_Sub7.java:20-302`) into a standard MIDI file, then handed to the synthesiser with the sample banks from indexes 14/15/4.

Measured directly from the cache bytes (idx255 group 11, inflated 6182 bytes, consumed to the byte): format 6, version 48, flags 0x00, 441 groups, ids 0..440 contiguous, fileCount 1 for every one, 441 files total, no identifiers block. All 441 groups are GZip (compression 2) with a 2-byte version trailer. Payloads: min 21, median 1655, max 102768 bytes, 1,907,985 total. Every one of the 441 payloads starts with byte 0x17 (opcode 23 = set tempo). The three-byte header at the END of each file gives trackCount 2..20 and division 360 (x1), 480 (x173) or 960 (x267).

## Current capability

Decode and encode, with a GUI tab that is still export-only.

- Codec: `FlashEditor/Definitions/Tracks/Track.cs` - `Track.Decode(JagStream)` is a full port of `Node_Sub7`, verified arm for arm against `Node_Sub7.java:183-300` including the pitch-wheel low/high cursor order (vs `Node_Sub7.java:271-273`) and the signed `sbyte` reads. It differs from the client in retaining the packed spans rather than cursors into a buffer it discards, so `Track.Encode()` can reproduce the file and `Track.Project()` rebuilds the MIDI as derived output. `TrackRun.cs` states the twenty-one runs in their on-disk order.
- Sweep: `FlashEditor.Tests/Definitions/RealCacheTrackCodecTests.cs` re-encodes every declared group of index 11 and index 6 to its stored bytes through `DefinitionSweep`, and separately requires the stored length, the field-by-field sum of the retained spans and the encoder's output to agree with nothing left in front of the trailer.
- Cache entry point: `FlashEditor/Cache/RSCache.cs:760` `GetTrack(indexId, groupId)` - takes the first valid file id from the reference-table entry, so it works on index 11's one-file groups.
- Index constant: `FlashEditor/Cache/RSConstants.cs:26` `MUSIC_2 = 11`.
- GUI: `FlashEditor/Definitions/Tracks/TrackEditorPanel.cs:38` lists index 11 as "Jingles" next to index 6; tab wired at `Editor.Designer.cs:1362-1370` and bound at `Editor.cs:493-497`. It is a `BackgroundWorker` sweep of both indexes with a list, a stats pane and `Export MIDI...` (`TrackEditorPanel.cs:310`). Export only - `File.WriteAllBytes` to disk, nothing writes back to the cache.
- Tests: `FlashEditor.Tests/Definitions/RealCacheTrackTests.cs:43` `EveryTrackDecodesToAStructurallyValidMidi` sweeps every group of index 6 and index 11 (`:32` `JingleIndex = RSConstants.MUSIC_2`) and checks two things that a decoder with wrong run boundaries cannot satisfy: emitted length reconciles with the packed file's own predicted length (`:78`), and the output is structurally valid MIDI - MThd, MTrk chunks that tile exactly, FF 2F 00 closing each (`:165-217`). `TrackNamesJoinOnTheArchiveNameHash:155-156` asserts every index-11 group identifier is -1.
- Offline codec test: `FlashEditor.Tests/Definitions/TrackCodecTests.cs` builds packed files by hand from `Node_Sub7.java` and asserts their expected MIDI literally, so the pair is not proven only by running this encoder against this decoder. It pins the aliases directly: a signed run delta, bit 7 of a controller-number delta, a wide variable-length delta time, and the dropped meta status byte going into the projection but never into the packed form.

Grade: the codec round-trips; the GUI does not write. `RSCache.WriteFile` could take `Track.Encode()`'s bytes, and no GUI path reaches it.

## Gaps

- GUI editing: TrackEditorPanel is export-only. It needs an Import MIDI / Replace action feeding RSCache.WriteFile(11, groupId, 0, bytes), plus the usual staged-save behaviour. The tab pattern itself is already right - it is code-built like MapEditorPanel and bound from Editor.cs:493, so no Editor.Designer.cs surgery is needed.
- Mutating the stored form: `Track` exposes the runs read-only (`Track.Run`), which reproduces an unedited jingle and cannot change one. Any mutation API has to keep the opcode stream, the delta times and the twenty-one runs consistent, since nothing in the file states a length.
- A MIDI import path, which is strictly harder than round-tripping: it must synthesise run splits, channel-delta nibbles and running-status decisions from scratch.

## Notes and traps

TRAPS, in the order they will bite.

1. THE ACCUMULATORS ARE NOT RESET PER TRACK, AND THEIR UNMASKED STATE IS LOST. Note, velocities, pitch wheel, channel pressure, key pressure, channel, controller number and the 128 per-controller values are all initialised once before the track loop (`Node_Sub7.java:174-182`, mirrored in `Track`'s projection) and carry across every MTrk in the file. Each is a running sum of SIGNED byte deltas, and the output is `accumulator & 127`. So the accumulator routinely holds values outside 0..127 and the emitted MIDI cannot tell you what it was. This is the whole reason `Track` keeps the raw runs and `Encode` recomputes nothing; an encoder working back from the MIDI would produce a valid file with different bytes.

2. CLIENT BUG, and it hits every jingle. `Node_Sub7.java:196-199` gates the 0xFF meta status byte on the running-status test used for channel messages. Opcodes 7 (end of track) and 23 (set tempo) both mask to nibble 7, so an end-of-track directly after a tempo change loses its 0xFF and the chunk closes with a bare `2F 00`, which the MIDI spec forbids. Measured by walking pass 1 over the raw bytes of all 441 index-11 groups: 441 dropped status bytes, one in every one of the 441 groups. So the 637 client emits non-conformant MIDI for 100% of jingles. `Track` writes the byte unconditionally and counts it in `RepairedMetaStatusBytes`, and `RealCacheTrackTests` adds it back before comparing lengths. It cannot reach the packed file: `Encode` replays the stored opcode stream, which has no representation of that byte, so the packed file's own length prediction (`Node_Sub7.java:166`) not allowing for it costs nothing.

3. THE HEADER IS THE LAST THREE BYTES, NOT THE FIRST (`Node_Sub7.java:22`). The opcode stream must start at offset 0 because the re-interleave indexes the raw buffer from 0. This is also why the shared sweep harness's padded `AssertExactConsumption` is unusable here - the padding moves the header - and why `RealCacheTrackCodecTests` asserts exact consumption by making three independently-derived lengths agree instead.

4. INDEX 11 DOES NOT EXERCISE THE WHOLE FORMAT. Opcode census over all 447,357 opcode bytes in index 11: low nibbles 0, 1, 2, 3, 6, 7 only. Nibble 4 (channel pressure) and nibble 5 (polyphonic key pressure) NEVER occur here. High nibbles 0-15 all occur, so the channel XOR-delta uses the full range. A sweep that passes on index 11 alone has not touched two arms of the decoder; index 6 must be swept too.

5. NO NAMES, EVER. The reference table flags byte is 0x00 (measured), so there is no identifiers block, no whirlpool, no sizes, no entry hash. `TrackNames` (index 17 enum 1345) is an index-6 mechanism only; every jingle arrives with NameHash -1 and stays unnamed (`TrackEditorPanel.cs:246`, asserted at `RealCacheTrackTests.cs:155-156`). Export file names fall back to `track_11_<id>.mid` (`TrackEditorPanel.cs:366`) - and the index must stay in the name because group ids restart at 0 in both 6 and 11.

6. NO XTEA, NO SURPRISES IN THE CONTAINER. `getChildFromFolder` passes null keys (`JS5Archive.java:203-205`); no index-11 group is in any key dump. All 441 containers are GZip with a 2-byte version trailer, so the standard "a GZip re-encode is never byte-identical" rule applies: any byte-identity sweep must compare the DECOMPRESSED payload, never the stored container.

7. THE GROUP HAS ONE FILE, SO THE ARCHIVE PAYLOAD HAS NO TRAILER AT ALL. fileCount is 1 for all 441 groups, and the client special-cases that: writing a size table or a chunk-count byte would be handed back as file data and grow the file on every save.

8. `Class247.java:2013,2036` and `PacketParser.java:2073` pass the jingle id as a raw 16-bit value with 0xFFFF reserved. Ids are contiguous 0..440 here, so any renumbering on repack breaks every server and every script that references one. Treat group ids as fixed.

9. 637 vs 639: no divergence found. Every opcode byte in this cache is handled by the 637 decoder - the census hit no value that reaches the client's `throw` (`Node_Sub7.java:62-68`, mirrored in `Track.CountOpcodes`). The format is unchanged between the pair.

10. `RealCacheTrackTests` is NOT the byte-identity sweep and must not be read as one; that is `RealCacheTrackCodecTests`. The two do not overlap and both are needed. The encoder replays the stored runs, so it would reproduce the cache byte for byte even if the MIDI projection were nonsense - which means the byte-identity sweep says nothing at all about the export, and `RealCacheTrackTests`' length prediction and structural-validity checks are the only thing that does.
