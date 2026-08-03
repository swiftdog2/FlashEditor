# Index 6 - MUSIC

**Format:** fully-understood  
**Capability:** read-only  
**Effort:** large

## What it is

Index 6 holds full-length music tracks: 963 groups (idx6 is 5778 bytes / 6 = 963), one file per group, each file a column-major re-packing of a standard MIDI file. A group == one track; the single file inside it == the whole packed track; one "record" is the packed byte stream that expands to one complete SMF (MThd + N x MTrk).

Client authority, followed end to end:
- Opened at `InterfaceSettings.java:164`: `Node_Sub10_Sub1.aJS5Archive_5544 = Class42_Sub3.openFileStore(-115, true, 1, 6)`. Index 11 (jingles) is opened at `:168` and is the same format.
- Addressed by name: `InterfaceSettings.java:216` calls `aJS5Archive_5544.requestFile((byte) -109, "scape main")`, so index 6 is name-addressable (its reference table sets the identifiers flag 0x01; AGENTS.md:86).
- Playback path: `Class61.java:219-221` -> `Class226.method2854(..., aJS5Archive_5544, 0, 2, groupId)` stashes fileId=0 and groupId (`Class226.java:32-36`), and `ClientScript.java:55` calls `Node_Sub7.method985(archive, groupId, fileId)`, which does `getChildFromFolder(groupId, fileId)` (`Node_Sub7.java:9`). So: group id = track id, file id = 0, always.
- The decoder is the whole of `Node_Sub7.java:20-305`. It reads a 3-byte header from the LAST three bytes of the file (`:22-24`: trackCount u8, division u16), then walks an opcode stream from offset 0, then reads ~24 contiguous per-field runs (note numbers, note-on velocities, note-off velocities, pitch-wheel high/low, per-controller value runs, tempo triplets, delta times) and re-interleaves them into a real MIDI file. Most runs are delta-encoded against the previous value in the same run; the opcode's high nibble is a channel delta XOR-ed into a running channel (`:214`).
- The second `openFileStore` argument (`true` for index 6) is not a naming flag: it is `aBoolean1570`, which discards the raw packed group after unpacking (`JS5Archive.java:365`) and forces an on-demand reload (`:917`). Relevant only to client memory behaviour, not to the on-disk format.

## Current capability

Decode, display and export - no write path.

- Decoder: `FlashEditor/Definitions/Tracks/Track.cs:141` `Track.Decode(JagStream)`, a full four-pass port of `Node_Sub7.method985`. It reproduces the header-at-the-end read (`Track.cs:150`), the opcode counting pass (`:172-206`), the delta-time pass (`:213-221`), the controller-number replay (`:226-275`), all 21 run cursors (`:280-303`) and the re-interleave (`:330-465`), and it back-patches every MTrk length (`:461-464`).
- Cache plumbing: `FlashEditor/Cache/RSCache.cs:760` `GetTrack(indexId, groupId)` - resolves the group's real file id from the reference table (`:765-769`) rather than assuming 0, and carries the reference-table name hash onto the track (`:773`).
- Names: `FlashEditor/Definitions/Tracks/TrackNames.cs:53` `Load(cache)` reads enum 1345 (index 17, group 5, file 65) and keys it by `NameHasher.GetNameHash(name)`, so a name attaches to a group only when it reproduces that group's stored identifier. 598 of 963 groups get a name (`TrackNames.cs:33-36`).
- GUI: `FlashEditor/Definitions/Tracks/TrackEditorPanel.cs:27`, a real tab wired into the designer at `Editor.Designer.cs:1362-1370` ("Tracks", `TrackEditorTab`), instantiated at `Editor.Designer.cs:1578`, bound from `Editor.cs:493-497` keyed on `RSConstants.MUSIC_INDEX`. It lists both index 6 and 11 (`TrackEditorPanel.cs:36-38`), shows per-track MIDI statistics, and exports MIDI to disk (`:64`, `:310-344`). It is explicitly read only (`:24-26`).
- Sweep: `FlashEditor.Tests/Definitions/RealCacheTrackTests.cs:44` `EveryTrackDecodesToAStructurallyValidMidi` walks every group of index 6 and index 11 from the reference table (`:52-56`) and checks two things that cannot be satisfied by a self-consistent wrong decoder: the emitted length must equal the packed file's own predicted length (`:78-81`), and the output must be structurally valid MIDI - MThd, tiling MTrk chunks, `FF 2F 00` terminator on every chunk, chunk count == declared track count (`:165-217`). `:114` `TrackNamesJoinOnTheArchiveNameHash` round-trips every recovered name back through `RSReferenceTable.GetArchiveId` and pins that group 0 is "Scape Main", and that index 11 carries no identifiers.

There is NO encoder. `grep Encode` over `FlashEditor/Definitions/Tracks/` returns nothing, and `Track.cs:26-27` says so outright. Nothing in the editor writes to index 6.

## Gaps

- An encoder. `Track` has no `Encode`, so nothing can write index 6 back. This is the whole gap between read-only and complete.
- The decoder is lossy against the stored bytes and must be changed before an encoder is possible. It accumulates the delta runs into running values (`Track.cs:379-449`: `note`, `noteOnVelocity`, `noteOffVelocity`, `pitchWheel`, `channelPressure`, `keyPressure`, `controllerValues[]`) and keeps only the MIDI output. The raw signed-byte deltas, the raw opcode stream and the raw delta-time VLQs are all discarded. `Track` must retain the packed runs verbatim (the same 'record which encoding it saw' rule CLAUDE.md applies to terrain) before any re-encode can be byte-identical.
- A byte-identity sweep over all 963 index-6 groups (and the 441 index-11 groups, same format), in the shape of the existing definition sweeps. There is currently no test that re-encodes anything on this index, because there is nothing to re-encode.
- A codec test against captured bytes - one small packed track and its known-good MIDI, checked in - so the pair is not proven only by running this encoder against this decoder (CLAUDE.md: round-tripping this encoder against this decoder proves nothing).
- GUI editing. The Tracks tab is a list plus an 'Export MIDI...' button (`TrackEditorPanel.cs:64,310`). There is no import, no edit, no save, and no call into `RSCache.WriteFile` anywhere on this path.
- A MIDI import path if user-authored tracks are ever wanted, which is a strictly harder problem than round-tripping: it must synthesise run splits, channel-delta nibbles and running-status decisions from scratch.

## Notes and traps

Traps for whoever builds the encoder:

1. THE FORMAT IS NON-CANONICAL IN THE DIRECTION THAT MATTERS. Every accumulated value is written masked (`value & 127`, `Track.cs:381,390,423,434-435,441,449`) but accumulated as a full `int` from signed byte deltas. Many distinct stored delta streams therefore produce byte-identical MIDI, so the deltas cannot be recomputed from the decoded output. Keep the stored bytes; do not recompute them. Same reasoning as the terrain height `0`/`1` alias in CLAUDE.md.

2. RUNNING STATE IS NOT RESET BETWEEN MTrk CHUNKS. `channel`, `note`, the three velocity accumulators, `pitchWheel` and `controllerValues[128]` are declared outside the track loop (`Track.cs:316-328`), matching the client (`Node_Sub7.java:174-182`). An encoder that resets per track desynchronises every track after the first, and the structural MIDI test would still pass.

3. CONFIRMED CLIENT BUG, already diverged from and documented at `Track.cs:122-137`. The client gates the `0xFF` meta status byte on the same running-status test it uses for channel messages (`Node_Sub7.java:196-199`); opcodes 7 (end of track) and 23 (set tempo) both mask to nibble 7, so an end-of-track that follows a tempo change loses its status byte and the client emits a bare `2F 00`. Our decoder writes it unconditionally and counts the additions in `Track.RepairedMetaStatusBytes`. Consequence: our MIDI is deliberately NOT byte-identical to the client's output, and the length check has to add the repair count back (`RealCacheTrackTests.cs:78`). An encoder must strip those bytes again or the packed re-encode will not match.

4. THE HEADER IS AT THE END OF THE FILE, not the start (`Node_Sub7.java:22`, `Track.cs:150`). The opcode stream must start at offset 0 because pass four indexes the raw buffer from 0 (`Track.cs:345`) while pass one reads through the cursor.

5. SIGNEDNESS. Every run byte is read as `sbyte`. The pitch-wheel low half in particular must be signed because it feeds bit 7 upward through `>> 7` and is not masked away like the others (`Track.cs:429-433`); reading it unsigned silently changes the second output byte. The client cannot be inferred here - Java bytes are signed by default, which is why `Node_Sub7.java:272-273` looks like it does nothing special.

6. NO XTEA, NO UNUSUAL COMPRESSION. Index 6 is not in the encrypted family (only index 5 `l` groups are), and its containers are ordinary JS5 containers. But CLAUDE.md's GZip rule applies at full force: index-6 groups are ~40 KB median, so a re-encode of the container will never be byte-identical. Any sweep must compare the DECOMPRESSED group payload, never the stored container.

7. CROSS-INDEX DEPENDENCY. Track names come from index 17 (enum 1345 = group 5, file 65), not from index 6 (`TrackNames.cs:39-59`). Index 6 itself stores only a one-way name hash in its reference table. Do not "improve" the join by keying the enum on group id - that mapping covers 958 of 963 groups, looks like confirmation, and is wrong; the enum key is the music player's alphabetical list position. `RealCacheTrackTests.cs:99-111` exists specifically to stop that regression.

8. INDEX 11 IS THE SAME FORMAT AND SHOULD BE DONE IN THE SAME CHANGE (441 groups, idx11 = 2646/6). It is decoded by the same `Node_Sub7.method985` and carries no identifiers at all, so no jingle can ever be named.

9. 637 vs 639: no evidence of any format change. Every index-6 group in this cache decodes through the 637 algorithm with its own length prediction reconciling, which is the strongest available statement that the packing is unchanged.

10. Minor doc staleness worth fixing while nearby: `FlashEditor.Tests/IO/JagStreamTests.cs:1190` still says "ReadVarInt and WriteVarInt have no callers anywhere in the repository". `Track.cs:218` and `:343` are now callers - the varint pair is the delta-time codec for this index.
