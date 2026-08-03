# Index 15 - SFX3 (misnamed: MIDI instrument/patch bank)

**Format:** fully-understood  
**Capability:** none  
**Effort:** medium

## What it is

Index 15 is the MIDI instrument/patch bank - the sample-mapping layer that turns a program change in an index-6/11 track into playable samples. It is NOT a third sound-effect bank; `RSConstants.SFX3_INDEX` is a wrong name (AGENTS.md:289 already flags it).

CLIENT PATH, end to end:
- `InterfaceSettings.java:171` opens index 15 into `Class119_Sub2.aJS5Archive_4726`.
- `Particle_Sub3_Sub5_Sub2.java:99-100` hands it to `aa_Sub3.method159` alongside index 14 and index 4; `aa_Sub3.java:62-66` parks them as `Class64_Sub1.aJS5Archive_3641` (15), `IntegerNode.aJS5Archive_4127` (14), `Class94.aJS5Archive_793` (4).
- `ClientScript.java:62,68-69` calls `Node_Sub31_Sub2.method1352(song, 22050, new Class308(idx4, idx14), idx15, false)`.
- `Node_Sub31_Sub2.java:1141-1156`: for every distinct patch id the song touches, `Class355.method3875(id, idx15)`.
- `Class355.java:16-19`: `JS5Archive.method2733(id)` then `new Node_Sub44(bytes)`.
- `JS5Archive.java:591-612`: `method2733` returns the whole group as one file and THROWS unless the group holds exactly one file.

GROUP = one instrument patch. FILE = the single file in that group; group payload IS the record. RECORD = a `Node_Sub44` (`Node_Sub44.java:103-451`): seven parallel 128-entry arrays, one entry per MIDI key 0..127 (`:106-112`), plus a shared envelope table.
- `anIntArray4246[note]` - sample reference. `method1517` (`:465-496`) does `v--`; bit 0 selects the bank (`Class308.method3613` -> index 14 Vorbis, else `method3611` -> index 4 sound effects; ctor mapping at `Class308.java:60-63`), and `v>>2` is the sample id. 0 = note unused.
- `aShortArray4248[note]` pitch, `aByteArray4247[note]` filter/cutoff, `aByteArray4252[note]` volume, `aByteArray4250[note]` a per-note byte, `aClass89Array4251[note]` -> a shared `Class89` envelope, `anInt4249` a trailing count.

GROUP ID = the MIDI bank+program id, computed by `Node_Sub7.java:342-357`: CC0 contributes `<<14`, CC32 contributes `<<7`, program change adds 0..127. Channel 9 defaults to 128 (`Node_Sub7.java:318`), i.e. bank LSB 1 = drum kits.

MEASURED IN THIS CACHE (my own read of idx15 + dat2): the idx file has 293 six-byte slots but only 176 are occupied, and the reference table declares 176. The occupied ids are exactly 0-127 (the 128 General MIDI melodic programs), then 128, 129, 136, 144, 152, 153, 168, 176, 178, 184 (bank 1 = the GM drum kits: Standard/Room/Power/Electronic/TR-808/Brush/Orchestra/SFX at the canonical offsets), then 255 and 256-292 (bank 2, Jagex's custom instruments). One file per group, all 176 GZip with a 2-byte version trailer, uncompressed payload min/median/max 273/289/1018 bytes.

I verified the format rather than asserting it: I ported the `Node_Sub44` constructor to Python and ran it over all 176 groups. Every one consumes to the exact final byte - 176/176, no slack, no overrun. Sample references land in range for both banks (max id 3716 against index 4's 10238 groups, 2883 against index 14's 3657).

## Current capability

Nothing format-specific. Index 15 has no decoder, no encoder, no definition class, no test and no GUI tab.

- `FlashEditor/Cache/RSConstants.cs:30` declares `SFX3_INDEX = 15`. A grep for `SFX3_INDEX` across every `.cs` in `FlashEditor/` and `FlashEditor.Tests/` returns exactly that one line - the declaration itself. Zero adoption sites, which per CLAUDE.md means "no feature for that index yet", not "someone used a magic number". The literal `15` is used as an index nowhere either (the only hit is `Editor.Designer.cs:1267`, a `TabIndex`).
- `FlashEditor/Definitions/` holds ItemDefinition, NPCDefinition, ObjectDefinition, ModelDefinition, FloorUnderlay/Overlay, MapSceneIcon, Sprites/, Tracks/. There is no instrument, patch or `Node_Sub44` equivalent.
- GUI: the tab set is Item, Sprite, NPC, Object, Interface, ModelViewer, TextureViewer, MapEditor, TrackEditor and the Meta console (`Editor.Designer.cs:1469-1577`). No index-15 tab, and `Editor.cs:524` dispatches on a switch keyed to specific index constants which has no case 15.
- The only place index 15 surfaces at all is metadata: `RSCache.LoadReferenceTables` (`RSCache.cs:542-547`) walks every index, so index 15's reference table is decoded at startup and its row appears in Meta > Reference Tables (`Editor.cs:556`). `RSConstants.cs:80` supplies the (wrong) label "SFX3" via `GetIndexName` (`:111`). No group payload is ever loaded or shown.

What DOES cover index 15 is the generic byte-level plumbing, and it should not be mistaken for capability: `RealCacheConformanceTests` iterates `_cache.TableIndexes`, which `RealCacheFixture.cs:57-63` builds from every table in idx255, so index 15's containers, archive payloads, single-file no-trailer rule and idx records are all swept (`RealCacheConformanceTests.cs:169, 218, 365, 479`). That proves we can move index 15's bytes around without corrupting them. It says nothing about understanding a patch record - the same sweep covers index 34, which is empty.

## Gaps

- A definition class - FlashEditor/Definitions/Audio/InstrumentDefinition.cs (or PatchDefinition) - with Decode/Encode, modelled on the RAW STREAMS rather than on the decoded 128-entry arrays. Storing decoded values cannot round-trip (see traps 1, 3, 4, 5, 6). The record is a single positional blob with no opcode loop: three NUL-terminated run-length streams each followed by a same-length value region read by a separate cursor, an envelope back-reference table, an envelope descriptor table, two optional curve blocks, two 128-byte delta-coded pitch streams, 128 base-128 varint sample references, and a tail of per-envelope bytes (Node_Sub44.java:113-447).
- An exact-consumption sweep over all 176 groups. I have already established this passes - my Python port of Node_Sub44 consumed every one of the 176 payloads to the final byte with zero slack - so the C# decoder has a known-correct target to hit.
- A byte-identity sweep over all 176 groups, comparing the DECOMPRESSED payload (every group is GZip, and AGENTS.md's measurement is 0 of 96,183 GZip containers re-encode identically, so comparing containers is meaningless).
- A codec test against captured bytes for the awkward minority: the 6 groups carrying the velocity curve (is_21), the 10 carrying the filter curve (is_22), and the 12 groups with more than one envelope (envelope counts measured across the index: 164 groups have 1, two have 2, one has 5, one has 16, seven have 17, one has 18). A sweep that only ever sees the 164 single-envelope groups proves almost nothing about the back-reference table.
- A GUI tab following the Editor.Designer.cs pattern - a TabPage plus a TreeListView, bound from the switch in Editor.cs:524 with its own BackgroundWorker, the way ItemEditorTab and TrackEditorTab are. Natural shape: patch list on the left (labelled from the General MIDI program names keyed on group id, since the index carries no name hashes), 128-row note grid on the right showing sample reference, source index (4 or 14), pitch, volume and envelope per note.
- Optional but this is what makes the tab worth having: a sample resolver for index 4 and index 14 so a patch can be auditioned. Neither index has a decoder in FlashEditor today, so this is a dependency, not a detail.

## Notes and traps

TRAPS, in the order they will bite.

1. NON-CANONICAL - the discarded bit. `method1517` (Node_Sub44.java:481-484) computes the sample id as `(v-1)>>2` but branches on `(v-1)&1`. Bit 1 is read and thrown away. Measured over all 176 groups: of 1067 distinct sample references, `(v-1)&3` is 3 in 565 cases, 1 in 488, 0 in 13 and 2 in 1 - so bit 1 is SET in 566 of them and carries real data the client ignores. Decoding to (sampleId, bank) and re-encoding as `(id<<2)|bank|1` corrupts more than half the references. Keep the raw varint.

2. THREE CURSORS, NOT ONE. The record is not read front to back. `Node_Sub44.java:124` and `:136` capture `i_5_` and `i_9_`, skip a region, and come back to read it byte by byte much later (`:227`, `:244`) while the main cursor is elsewhere. Each region is exactly `strlen+1` bytes where strlen is the preceding NUL-terminated run stream. The regions are consumed only as far as there are runs, so the trailing byte of each is typically never read - its value is unconstrained and must be preserved verbatim.

3. NON-CANONICAL - RLE partitioning. Every one of the run streams drives `if(run == 0) fetch next run byte` with `run = -1` meaning "run forever" once the stream is exhausted (e.g. `:210-213`, `:228-231`). A given 128-entry array has many valid encodings: one long run, several short ones, or an early exhaustion. Rebuilding the runs from the decoded arrays will produce a file of plausible length and different bytes. This is the same class of defect CLAUDE.md documents for terrain opcode order.

4. NON-CANONICAL - the envelope alias table. `Node_Sub44.java:147-167` decodes a back-reference code where 0 means "new envelope" and non-zero names an earlier one, with an off-by-one skip (`if(i_15_ <= i_17_) i_17_--`). Two different codes can select the same envelope set. Do not regenerate it by de-duplicating the envelope list.

5. LOSSY, NOT JUST NON-CANONICAL - the curve blocks. `is_21` (6 groups) and `is_22` (10 groups) are applied destructively inside the constructor: `:344-364` rescales `aByteArray4252` in place with integer division via `Class64_Sub26.method658`, and `:375-415` does the same to `aByteArray4247` with clamping to [0,128]. Neither is invertible. Keep the pre-curve arrays and the curve as separate fields and apply the transform only for display or playback.

6. LOSSY DECODE OFFSETS. `aByteArray4247[k] = ((signed byte) + 16) << 2` narrowed back to a byte (`:244`, `:252`) wraps for most inputs - the stored byte cannot be recovered from the decoded value. Same shape, milder, for `aByteArray4250 = signed - 1` (`:227`), `aByteArray4252 = u8 + 1` (`:283`), `anInt4249 = u8 + 1` (`:289`). Store the raw byte.

7. ASYMMETRIC READ. At `:275-285` the run counter advances on every one of the 128 notes but the value byte is read ONLY when `anIntArray4246[note] != 0`. Get that backwards and the stream desyncs silently on any patch with unused notes - which is most of the drum kits (group 128 uses 61 of 128 notes).

8. DO NOT SIZE THE INDEX FROM THE IDX FILE. idx15 is 1758 bytes = 293 slots, but only 176 are occupied and the reference table declares 176. Enumerating 0..292 hits 117 empty slots. The task brief's "293 groups" is the slot count, not the group count.

9. DEPENDENCIES. Index 4 (`Class37`/`Node_Sub24_Sub1`) and index 14 (`Node_Sub13`, whose group 0 is the Vorbis setup header per AGENTS.md) are both required to resolve a patch into audio, and neither has a decoder in FlashEditor. Index 6/11 already decode to MIDI (`Definitions/Tracks/Track.cs`), so a patch browser is useful on its own; a player is not, until 4 and 14 land.

10. NO NAMES. Index 15 does not set the identifiers flag (AGENTS.md's measured table lists 3, 5, 6, 8, 10, 12, 13, 23, 30, 31, 32, 33), so nothing in the cache names an instrument. Any label in a GUI has to come from the General MIDI program table keyed on group id - which the measured id layout makes safe: 0-127 are GM programs in order, 128+ are GM drum kits at their canonical offsets.

11. NOT ENCRYPTED, and no 637/639 format drift. No XTEA on index 15. And the 637 decoder parses the 639 data exactly: my port consumed 176 of 176 payloads to the final byte, so there is no "the data vetoes" case here.

12. AGENTS.md:289 says "176 groups of ~290 bytes, each a sparse 256-entry table". The size is right (median 289 uncompressed). "256-entry" is wrong - the arrays are 128 wide, one per MIDI key (`Node_Sub44.java:106-112`), across seven parallel arrays. Worth correcting when this work lands.
