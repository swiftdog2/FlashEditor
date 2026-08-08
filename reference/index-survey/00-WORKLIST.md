# FlashEditor Implementation Plan

Derived from the 38 per-index surveys. Order is by index number, per the binding direction. "Full editor" throughout means: decoder, encoder, byte-identity sweep over the whole index, GUI tab.

---

## 0. Skipped, and why (do not re-investigate)

| Index | Reason |
|---|---|
| 16 OBJECTS_DEFINITIONS | Already complete: decoder, encoder, whole-index byte-identity sweep, editable tab. Residual polish only (opcodes 78/79 conflated into one field; 8 of ~90 fields exposed in the grid). |
| 19 ITEM_DEFINITIONS | Already complete on all four legs. Residual polish only. |
| 255 META | Complete: codec, whole-index sweep, tab. Tab is read-only by design (`RSCache.WriteFile` refuses META_INDEX). Two residuals are cross-cutting, see section 4. |
| 34 LOADING_SPRITES_RAW | **Empty in this cache.** idx34 is a 1-byte `0xFF` placeholder (0 groups) and idx255 record 34 is length 0 / sector 0, so there is no reference table either. It is the fallback for index 32, selected only when the JVM cannot decode images. Nothing to build. |
| 35 THEORA_AKA_CUTSCENES | **Empty in this cache**, same shape as 34. Also opened by nothing: the client never passes 35 to `openFileStore`, and its loading weight is 0. The name is misattributed; Ogg/Theora is index 36. Nothing to build. |
| 36 VORBIS | **Empty in this cache.** Unlike 34/35 it *has* a meta record: a 9-byte container holding a four-byte format-5 stub declaring zero groups. Keep the stub-tolerant decode path; there is no content to edit. |

Three indexes below the "empty" line are still worth one line of code: nothing may treat 34/35 (no record) and 36 (zero-group stub) as the same case.

---

## 1. Ordered worklist

Effort is the survey's own grading. "Dep" is hard unless marked soft.

| # | Index | Capability now | Build | Effort | Dep |
|---|---|---|---|---|---|
| 0 | FRAMES | none (constant declared, zero adoption) | `FrameDefinition` decode/encode: header (marker byte, base id u16, transform count u8), per-transform flag byte incl. the 2-bit field at bits 3-4, signed-smart value stream. Decode takes the base's type array as an argument. Full sweep over all 3526 groups / 359,931 files. Frame-set browser tab. | medium | **1** (types decide payload); 20 for naming/grouping (soft) |
| 1 | SKINS (skeletons) | none | `SkeletonDefinition` decode/encode: u8 boneCount, u8[n] type (store raw, do not apply the client's 6→2 remap), u8[n] flag (raw byte, not bool), u16[n] mask, u8[n] labelCount, u8[] labels. Sweep all 3106 groups. Bone-grid tab. | small | none |
| 2 | CONFIG | partial-read (groups 1, 4, 34 done) | 32 remaining record types. Split into per-group work items; do not attempt as one pass. 18 have a 637 provider to port; 17 have none. | very-large | research (§2) |
| 3 | INTERFACE_DEFINITIONS | none (empty TabPage, no switch arm) | if3 component codec ported from `unpackConfig` + its two helpers. Non-canonical capture for parentID 65535/-1, slot 4095/-1, null-vs-empty arrays. Exact-consumption sweep first, then byte identity over all 1078 groups / 42,256 files. Group/component tree tab (both levels carry name hashes). | large | research (§2) |
| 4 | SOUND_EFFECTS | none | `SoundEffectDefinition` + nested Tone/Envelope/Filter. Sweep all groups; the format is canonical here so no encoding-choice capture is needed. Field-grid tab. Synthesiser port is optional and separable. | medium | none |
| 5 | MAPS | read-write-no-tests (m + l families complete and swept) | Only the remaining three families: `um` (900) and `ul` (900) encoders and sweeps, and `n` (35) decoder/encoder/sweep from scratch. Teach `MapSquareLoader.Save` which family a region came from. Underwater layer and NPC-spawn tool in the map tab. Wire the already-written `AddLocationEdit`/`ReplaceLocationEdit`. | medium | none |
| 6 | MUSIC | read-only (decoder, tab, export) | Rewrite `Track.Decode` to retain the raw packed runs, then `Track.Encode`. Byte-identity sweep over all 963 groups. Import/replace in the tab. | large | none |
| 7 | MODELS | read-only (3 decoders, OpenGL viewer) | Three encoders (newer 63,605 / legacy 2 / newProtocol 7). Decode types 1-3 textured faces (currently dropped). Read particles and bonds. Stop baking the vertex shift into decode. Fix the newProtocol smart variant and the `FaceSkin` sbyte truncation. Full sweep over 63,614 groups. Mesh-editing surface. | very-large | none |
| 8 | SPRITES | read-only, lossy | Rewrite `Decode` to keep palette, per-frame flags, offsets and sub-dimensions; implement `Encode` (currently throws). Sweep all 4593 groups across all four flag combinations. Wire the dead `ImportSpriteBtn`, implement `ExportSpriteDatBtn`, fix PNG export to emit every frame. Surface names. | medium | none |
| 9 | TEXTURES | read-only (decoder, evaluator, gallery) | Capture what decode discards (version byte, output-size byte, opcode order/presence, type-29 payloads, the 10-byte trailer). Encoder replaying raw opcode spans. Sweep all 946 groups. Node inspector in the tab. | large | research (§2) |
| 10 | HUFFMAN | none | `HuffmanTable` decode/encode over the 256 bit-lengths, plus a literal port of the codeword/tree construction. Sweep (one group). Table tab with a live encode/decode box. | small | none |
| 11 | MUSIC_2 (jingles) | read-only | Same format as 6. Do in the same change; sweep all 441 groups. | large | 6 (shared codec) |
| 12 | CLIENT_SCRIPTS | none | `ClientScriptDefinition` decode/encode. Sweep driven off idx12 (4151), not the reference table (4149). Tab requires a disassembler over the 582 opcodes in use. | large | none for the codec |
| 13 | FONTS | none | `FontDefinition` over the fixed 263-byte record, including the kerning branch (dead here, must still round-trip). Sweep all 25 groups. Fonts tab. Name recovery for the 14 uncracked hashes. | small | 8 for glyph preview (soft) |
| 14 | SFX2 | none | `SfxSample` (20-byte header + base-255 varint packet list) and a separate `VorbisSetup` for group 0. Sweep all 3657 groups. A meaningful tab needs a hand-written Vorbis decoder. | large | none |
| 15 | MIDI_PATCH (patch bank) | read-write-no-tab (codec and sweep complete) | GUI only: a `DefinitionListPanel` descriptor with a patch list and a 128-key grid showing sample reference, bank, tuning, volume, pan and envelope. `MidiPatchDefinition` already models the raw streams. | small | 4 and 14 for audition (soft) |
| 17 | CLIENTSCRIPT_SETTINGS (enums) | partial-read (one enum, lossy) | `EnumDefinition` decode/encode with opcode-order capture and empty-file handling. Sweep all 14 groups / 3558 files. Enum grid tab. Keep `TrackNames` hashing values, not keys. | small | none |
| 18 | NPC_DEFINITIONS | read-write-no-tests (codec and sweep complete) | GUI wiring only: set `CellEditActivation` and subscribe the two existing handlers on `NPCListView`, mark the id column non-editable, add a persistence test. | small | none |
| 20 | ANIMATIONS | none | `AnimationDefinition` over the 18 opcodes, with opcode order/repeat capture and derived-state separation from `method938`. Sweep all 15,260 records. Tab. | medium | 0 and 1 for playback (soft) |
| 21 | GRAPHICS (spot anims) | none | `GraphicDefinition` over 16 opcodes with order capture. Sweep all 2956 records. Tab with model preview. | small | 7 for preview (soft) |
| 22 | SCRIPT_CONFIGS (varbits) | none | `VarBitDefinition`: one opcode, four fields, plus absent-vs-default capture for the 1-byte files. Sweep all 8785 files. Varbit tab with a varp reverse index. | small | none |
| 23 | WORLD_MAP | none | Three record types: area details, the area raster tile stream, and the 7-byte static elements. Name-hash addressing at both group and file level. Sweep all 1043 files. World-map tab. | large | index-2 group 36 for icons (soft) |
| 24 | QUICK_CHAT_MESSAGES | none | Menu and message codecs, plus the 14-entry slot-type word-count table ported from the client. Sweep all 1299 files. Quick-chat tree tab. | small | none |
| 25 | QUICK_CHAT_MENU | none | Same two formats, second id namespace. Sweep all 86 files. Build with 24 in one change. | small | 24 (shared codec) |
| 26 | MATERIALS | read-only (decoder live, encoder dead) | Byte-identity sweep against the real 33,794-byte blob. Dirty flag so `EncodeColumnar` stops returning the raw bytes verbatim and silently discarding edits. Write path. 19-column materials grid. | small | none |
| 27 | CONFIG_PARTICLES | none | Emitter (34 opcodes) and effector (10 opcodes) codecs with opcode-order capture and the 5/31 alias recorded. Sweep all 421 files. Two-list tab. | medium | 7 if the model particle footer is wired (soft) |
| 28 | DEFAULTS | none | Two codecs, group 1 and group 3, with opcode order load-bearing and the type-version handshake bytes round-tripped verbatim. Sweep (two groups). Small tab. | small | none |
| 29 | CONFIG_BILLBOARD | none | `BillboardDefinition` over 7 opcodes; opcode 1 is written last in every record, so order capture is mandatory. Sweep all 182 files. Tab. | small | 26 for material preview (soft) |
| 30 | NATIVE_LIBRARIES | read-only (generic path) | Metadata classifier (os/arch/library from the group name, format from the payload magic). Committed name table for all 36 groups. Extract/import tab. Sweep that exercises the whirlpool recompute. | small | generic blob tab (§4) |
| 31 | GRAPHICS_SHADERS | read-write-no-tests (generic path, fully swept) | File-by-name lookup, a shader-aware type, a name-join test, a content-shape test, and a tab (text editor for `gl`, hex for `dx`). | small | per-file name index (§4) |
| 32 | LOADING_SPRITES | none | Payload-shape dispatcher (21 JPEG, 5 Jagex sprite sets). A 4-component non-JFIF YCbCr JPEG reader. Reuse the sprite codec for the font groups. Sweep all 26 groups. Tab. | medium | **8** (sprite encoder, index-parameterised `GetSprite`) |
| 33 | GAME_TIPS (loading screens) | none | Manifest codec (group 0) and screen codec (group 1) with all ten element records implemented, three of which are exercised here. Sweep all 343 files. Text/tree tab first; visual preview later. | medium | 32 and 13 for preview (soft) |

---

## 2. Needs format research first

Three indexes have `formatKnown: partially-understood`. Each becomes a `reference/index-architect-NN.md` research task, in the shape of the existing `reference/hydra-637-definitions/` docs: every claim citing a client `file:line` or a measurement over the 639 data.

### `reference/index-architect-02.md` (CONFIG)
Establish, per config group, what one record is.
- For the 18 groups with a located client provider: the full opcode table with payload widths, each row citing the provider's decoder `file:line`, plus an exact-consumption sweep over every file in that group proving the 637 widths hold in 639.
- For the 17 groups with **no** client provider (2, 7, 18, 20-25, 37-45, 48): reverse-engineer from the bytes. Establish at minimum the opcode set, whether the record is opcode-terminated at all, and exact consumption. Say explicitly where a field's meaning cannot be settled rather than naming it.
- Settle group 36 (`MAP_ELEMENT_GROUP`), which index 23 and object opcode 107 both depend on. Its contents are currently unverified.
- Record which groups' file ids are non-contiguous, and confirm the group id list from the reference table rather than 0..48.
- Deliverable is per-group, so index 2 can be implemented group by group instead of as one very-large pass.

### `reference/index-architect-03.md` (INTERFACE_DEFINITIONS)
Establish that the 637 `unpackConfig` read order consumes the 639 data exactly.
- Run an exact-consumption sweep over all 42,256 component files before any encoder is written. This has never been done and is the cheapest possible falsification.
- Pin the version byte's five gated branches. All files here start `0xFF` (version -1), so the param block and four other branches never fire and the `< 0` branch always does. Document which branches are unreachable in this cache so nobody defends them with a sweep.
- Settle the component-type dispatch (byte 1, low 7 bits) from what the client *does* per type, not from the field's identifier.
- Enumerate the non-canonical cases: parentID 65535 vs -1, slot 4095 vs -1, null vs empty for every bytecode/int array.
- Confirm the 24-bit reader used for the hook mask and param keys is unsigned.

### `reference/index-architect-09.md` (TEXTURES)
Establish the two things the decoder currently cannot round-trip.
- The 10-byte trailer after the three output-node indices. The 637 client stops reading before it, so it is 639-era data. It is not constant. Establish per-position field boundaries and widths from the data, and say plainly which positions have no determinable meaning. Copy verbatim regardless.
- A per-node-type inventory of what decode discards: the version byte, the output-size byte, opcode order, opcode presence and repetition, type-29 shape payloads, and the two destructive post-decode hooks (fractal-noise octave trim, curve identity-ramp substitution).
- Confirm the aliased opcode pairs (type 15 opcode 0 vs 5/6; type 34 opcode 3 vs 5/6) and the two swallowed type-12 opcodes are the complete set.

Index 12's reference-table identifier semantics are a **separate, optional** research note. Two sub-populations are proven (interface-hook packing, one global-default token); the rest are unexplained. The codec does not need it; a name column in the tab would. Do not label that column "name hash".

---

## 3. Straight to implementation

Format is fully understood and cited to client `file:line`; no research pass needed.

**0, 1, 4, 5, 6, 7, 8, 10, 11, 12, 13, 14, 15, 17, 18, 20, 21, 22, 23, 24, 25, 26, 27, 28, 29, 30, 31, 32, 33.**

Two qualifiers, neither a research task:
- **7** is fully understood but has three live decoder defects and one undecoded field family (textured face types 1-3, particles, bonds). That is implementation, not research.
- **23** is fully understood as a format, but its icon ids resolve into index-2 group 36, which is inside the index-2 research task. The terrain half can ship without it.

---

## 4. Cross-cutting work to do first

This is what stops the remaining indexes becoming copies of each other. Each item names what exists today.

### 4.1 A reusable definition-list tab
Two patterns exist and only one scales. The old one is a designer-declared `TabPage` plus a hand-written arm in the `LoadEditorTab` switch (`Editor.cs:471-568` and onward), used by items, sprites, NPCs, objects. The new one is a self-contained `UserControl` with a `Bind(cache, ...)` method that owns its own worker, used by maps and tracks (`Editor.cs:492-504`). Twenty-five more indexes through the switch is untenable.

Build a `DefinitionListPanel` UserControl: `Bind(cache, indexId)`, a column descriptor list, id enumeration from the reference table, a background loader, edit commit routed through `RSCache.WriteFile`, and the "re-encode unchanged, so write nothing" check. A new index should then be one designer control plus one registration, no switch arm.

Fix the registration mechanism at the same time. `editorTypes` (`Editor.cs:63-76`) is a positional array whose order must match the tab layout, with a comment saying so. Replace it with a per-tab index property so adding a tab in the wrong position cannot silently load the wrong index.

### 4.2 A shared opcode-stream record
`DecodedOpcode` (`Definitions/DecodedOpcode.cs`) carries one `int Value` per occurrence, plus `IsLastOccurrence` and `Has`. That is enough for floor overlays and too weak for most of the worklist: index 21 opcode 2 is two u16s, index 27 opcode 1 is four u16s, index 29 opcode 2 is two u16s, index 24 opcode 3 is a variable-length list.

**Done.** `Definitions/OpcodeStream.cs` holds `OpcodeRecord` (one occurrence: opcode plus its verbatim payload bytes) and `OpcodeStream` (the recorded sequence, with `Has`, `LastIndexOf`, `IsLastOccurrence`, `Remove`, `Clone` and `Replay`). `Definitions/OpcodeStreamDefinition.cs` drives the terminator loop, captures each payload verbatim, and **throws** on an unknown opcode - the per-opcode reader returns `false` and the base reports where the parse stopped. Throwing is this project's deliberate divergence from the client, which silently consumes nothing and desyncs. `ObjectDefinition`, `NPCDefinition` and `ItemDefinition` are migrated onto it; a new decoder inherits all three behaviours by deriving from `OpcodeStreamDefinition` and implementing `DecodeOpcode`.

Two things a new codec has to decide for itself. `IsTerminator` defaults to `opcode <= 0`; override it only for a format with its own sentinel, as the NPC decoder does for 255. And `Replay(records, appendInAscendingOrder)` decides where an opcode the decoded file never carried is written - pass `true` when the caller does not already build its fresh payloads in a deterministic order.

`ConfigDefinition` (`Definitions/Config/`) is a fifth implementation of the same pattern, with its own `ConfigOpcode` struct, and was left alone: its `Encode` is shaped around `WritePayload`/`AddedOpcodes` rather than a pre-built record list, so it uses none of `Replay`. Folding it in is mechanical - `ReadPayload` keeps throwing `Unknown(opcode)` so its 14 subclasses do not move - but it is a separate change with its own sweep to clear.

Measured need, so this is not speculative: index 29 writes opcode 1 last in every record, index 27 has all 421 records non-ascending, index 21 has 442 non-ascending, index 17 always writes the default after the table. A fixed-ascending encoder reproduces almost nothing in this cache.

### 4.3 A shared byte-identity sweep harness
`RealCacheFixture` (`FlashEditor.Tests/Cache/RealCache/RealCacheFixture.cs`) supplies the opened cache, `RawContainer`, `Table`, `TablePayload`, `ArchivesToExamine` and `RealCacheFact`. What it does not supply is the sweep itself: `RealCacheItemDefinitionTests`, `RealCacheNpcDefinitionTests`, `RealCacheObjectDefinitionTests` and `RealCacheFloorDefinitionTests` each re-implement enumerate → decode → assert exact consumption → re-encode → compare.

Build `DefinitionSweep` with one entry point taking the fixture, an index id and a factory. It must, by construction:
- enumerate file ids from the reference table's valid-id list, never `0..count-1` (sparse groups occur in indexes 2, 16, 19, 20, 21, 23, 32, 33 and more);
- carry the sentinel-padding trick from the NPC exact-consumption test so an over-read cannot masquerade as a clean stop;
- compare **decompressed payloads**, never stored containers;
- assert the count with no `or` in the assertion.

Also add an idx-driven enumeration variant. Four indexes hold groups present in their idx file and absent from their reference table, measured by `RealCacheEnumerationTests`: index 3 has 772, 825 and 891; index 4 has 4787; index 12 has 699 and 700; index 32 has 498 and 1407. A table-driven sweep skips them. This first read "index 12 has two and index 4 has one", which missed indexes 3 and 32 - both on this worklist, so an implementer working from the short version would size index 3's group count wrongly.

### 4.4 Settle the `IDefinition` contract
`IDefinition` (`Definitions/IDefinition.cs`) is `internal`, and its `Decode(JagStream stream, int[] xteaKey = null)` carries an XTEA parameter only the map path ever uses. Definitions do not share a uniform construction path, so a generic sweep harness cannot instantiate them. Settle on `Decode(JagStream)` / `JagStream Encode()` plus a factory delegate, and move the key handling to where it belongs.

### 4.5 Paged-id addressing helpers
Five different group/file splits are in play: `>>8 / &0xFF` (16, 19, 21, 17), `>>7 / &0x7F` (18, 20), `>>10 / &0x3FF` (22), name-hash only (5, 23, 30, 31), and single-group (26, 10). These are currently open-coded, and one place derives the page size from the first archive's file count rather than from a constant. Add named split/join helpers so no new index hardcodes 256.

### 4.6 Enumerate-without-throwing
Both the item and object tab loaders iterate `0..255` per archive and rely on `RSCache.ReadFile` throwing `FileNotFoundException` for the holes, catching and discarding. That is thousands of exceptions per tab load and it will multiply across 25 tabs. Add `RSCache.EnumerateFiles(indexId)` yielding real (group, file) pairs from the reference table.

### 4.7 Per-file name lookup
`NameHasher.GetNameHash` and `RSReferenceTable.GetArchiveId(name)` resolve **group** names. `ReferenceTableCodec` decodes and re-encodes per-**file** identifiers, but nothing indexes them, so a file cannot be found by name. Indexes 3, 5, 23, 30, 31, 32 and 33 all need it, and two of them cannot be read correctly without it: index 23's `area` file is id 4 in 32 groups and id 0 in the other 7, and index 30's file is named the empty string while the group carries the path.

Build the per-group identifier index once. While there: index 23 is the cheapest available proof that the hash is over the **lowercased** name, because one group's own record spells its name with capitals and only the lowercased form hashes to the stored identifier.

### 4.8 `JagStream` gaps
- **No signed-smart writer.** `ReadSmart` exists (bias -64 / -0xC000) and `ReadShortSmart` aliases it; the only writer is `WriteUnsignedSmart`, which is the 0/32768-bias variant and is the wrong function. Indexes 0, 4, 7 and 15 all need the signed writer.
- **`ReadVarInt`/`WriteVarInt`** exist and now have callers on the track path; a stale comment in the IO tests says they have none.
- **cp1252 strings.** `ReadJagexString`/`WriteJagexString` are the correct pair and match the client's remap including the `\0 → ?` fallback; the plain `ReadString` beside them skips the remap and is wrong for indexes 17, 23, 24, 25 and 33. Register `CodePagesEncodingProvider` once at startup rather than per call site.

### 4.9 A generic binary-blob extract/import tab
Nothing in the editor writes a payload to disk or reads one back: there is no `SaveFileDialog`, `OpenFileDialog` or `File.WriteAllBytes` anywhere in `Editor.cs`. Index 30 is nothing but extract/import, index 31's `dx` half can only be replaced, and 6, 11, 14 and 32 all want export. Build it once.

### 4.10 Sprite codec reuse
`RSCache.GetSprite` hardcodes `RSConstants.SPRITES_INDEX`, and `SpriteDefinition.Encode` throws. Both block indexes 8, 13 and 32. Index-parameterise the accessor as part of the index-8 work, not afterwards.

### 4.11 Reference-table residuals (from index 255)
- `Encode` does not reproduce the trailing four-zero-bytes-per-file block that indexes 9, 26, 27 and 29 carry, so writing any of those four shortens their table. Three of the four are on this worklist. Capture the surplus at decode and re-emit it.
- The META tab's leftmost column binds to a property the reference table does not have, so it renders empty and the tab does not say which index each row is.
- No JS5 master index (checksum table) generator exists. Not needed to edit this cache; needed by anyone serving an edited cache over JS5.

---

## 5. Risk list

Indexes unlikely to survive a single implementation pass, and why.

**2 CONFIG (highest risk).** Thirty-two undecoded record types, seventeen of which have no 637 client reference at all. This is not one task and must not be scheduled as one. Split it per group after the research pass; expect several groups to end with "shape established, field meanings unknown", which is an acceptable outcome and should be written down as such.

**7 MODELS.** Three encoders, plus roughly 80 percent of textured faces currently undecoded, plus three live decoder defects (vertex scale baked into decode on 39,043 models, wrong smart variant on the newProtocol path, face-skin truncation above 127), plus a dead `FF FD` branch that matches nothing in the client. The non-canonical hazards are the worst in the cache: strip opcodes are freely re-expressible, smart widths overlap, and the block lengths are declared rather than derived. It is also the heaviest index to sweep, 260 MB inflated across every archive, on a decode path that allocates per chunk.

**6 and 11 MUSIC.** The decoder is lossy *by construction*: it accumulates signed byte deltas into running values and emits only the masked MIDI. Many distinct stored streams produce byte-identical output, so the deltas cannot be recomputed. The encoder therefore requires rewriting the decoder to retain the raw runs, not adding a method beside it. Add the deliberate client-bug divergence (a meta status byte our decoder inserts and the packed file's own length prediction does not allow for) and the round trip has to strip it again.

**9 TEXTURES.** Ten trailer bytes of unknown meaning that vary per file. Two post-decode hooks that overwrite as-read values. Two swallowed opcodes on node type 12 that consume nothing and are recorded nowhere. Non-canonicality on all four axes at once (aliased opcodes, free order, expressible repetition, seeded defaults). Every one of those must be captured before the first encoder line.

**3 INTERFACE_DEFINITIONS.** About 35 common fields, six type-specific blocks, 21 hook arrays and five int arrays, with no reference document in the repo and no exact-consumption proof that the 637 read order fits the 639 data. Also carries three client-side mods in the load path that must not be ported.

**12 CLIENT_SCRIPTS.** The codec is small and fully specified. The "full editor" bar is what breaks it: raw u16 opcodes are not editable, so the tab needs a disassembler covering 582 distinct opcodes spread across three client dispatchers. Recommend shipping codec plus sweep as one unit and the disassembler as a second, explicitly separate unit.

**14 SFX2.** Same shape. The record codec is small; a tab worth having needs a hand-written Vorbis decoder, because group 0 is a hybrid setup header with no magic, no channel count and no framing bit, and cannot be handed to any off-the-shelf library.

**23 WORLD_MAP.** Six megabytes of tile stream whose flag byte is very likely non-canonical (a palette index can be carried inline or as an escape plus literal), so the flag must be preserved rather than recomputed. Its icon half is blocked on index-2 group 36. Its compression mix is BZip2-dominant, which is where the cache's known non-round-tripping BZip2 containers may live.

**8 SPRITES.** Looks like "write the missing encoder"; is actually "rewrite the decoder first". Decode currently rasterises straight to a bitmap and discards the palette, the per-frame flags byte, the offsets and the sub-dimensions. Three separate aliasing cases (black remapped to avoid the transparent index, unrecoverable row/column order on 1-pixel frames, optional alpha plane on fully-opaque frames) mean nothing can be rebuilt from pixels. It also gates index 32.

**0 FRAMES.** Cannot be decoded to meaningful values without index 1, and cannot be navigated without index 20. Sequencing risk more than format risk. The saving grace is that the format self-checks: the value stream must land exactly on the end of the file, and all 359,931 files do.

**32 LOADING_SPRITES.** Two unrelated payload shapes in one index, and a JPEG re-encode is no more reproducible than a GZip one. Byte identity has to be defended by keeping the original bytes and writing nothing when nothing changed, which is a design decision to take before the sweep is written, not after it fails. Also needs a 4-component, non-JFIF, no-Adobe-marker JPEG reader; every standard decoder renders these as CMYK and produces a plausible, wrong image.

**5 MAPS, remaining families.** Contains a live latent corruption: `MapSquareLoader.Save` resolves the surface `m`/`l` names unconditionally, so saving a region returned by the underwater loader writes single-plane underwater terrain over the four-plane surface square. Fix that before adding any underwater editing.

**26 MATERIALS.** Small, but ordered: `EncodeColumnar` returns the raw stored bytes whenever they are present, so every field edit is currently discarded in silence. The dirty flag must land before the write path, or the first sweep will pass while the editor does nothing.

**Cross-cutting risk: GUI scale.** Twenty-five new tabs into one switch statement and a positional index array is the single most likely source of a wrong-index-loaded bug. Section 4.1 exists to remove that risk before it materialises, and should be done before the second new tab, not the tenth.

**Cross-cutting risk: sweep serialisation.** Per CLAUDE.md, never run more than one cache-backed suite at a time and never run full sweeps in parallel agents. As each new index adds a whole-index sweep, the merge-gate run gets heavier, and index 7's is the heaviest by a wide margin. Parallelise the editing across worktrees; serialise the sweeping against the merged tree.
---

## Progress log

Appended as indexes land, so the ordered worklist above stays the plan and this stays the record.

| Index | State | Proof |
|---|---|---|
| 0 FRAMES | complete | 359,931 of 359,931 frame files re-encode byte-identically across all 3526 groups; exact consumption on every one. Animation tab resolves each pose through its skeleton. |
| 1 SKINS | complete | 3106 of 3106 skeletons re-encode byte-identically. Census pinned: 173,749 bones, 936,887 labels, no transform type 6, flags only {0,1}, masks only 0xFFFF. |
| 2 CONFIG | 11 of 35 families | Eight new families swept whole - map elements 1051, parameter types 1330, containers 609, varplayers 2002, client variables 1445, cursors 175, damage marks 28, client strings 345 - on top of the three already done. 19 of the remaining groups are empty in every file, so the real remainder is small. |
| 3 INTERFACE_DEFINITIONS | complete | 42,256 of 42,256 components across all 1078 interfaces. Six non-canonical cases captured. The three undeclared groups recovered at their unique parsing file counts. |
| 4 SOUND_EFFECTS | complete | 10,237 of 10,237 table-declared effects. Group 4787 is in idx4 but not the table, so a table-driven sweep correctly never sees it. |

### Corrections to this document, found by building against it

- The index-0 empty-frame count was 1568. It is **1573**, settled by a sweep that decodes no
  frames at all. Corrected in `index-000-FRAMES.md`.
- "Single-file group" reads as "file 0" and is wrong once: group 757's sole file is id **40**.
  Frame arrays are sized by capacity and indexed by id, so holes are legal.
- Section 4.3's note that only indexes 4 and 12 hold groups absent from their reference table
  missed two. It is 3, 4, 12 and 32, pinned by `RealCacheEnumerationTests`.
- Transform type 6 exists nowhere in this cache, so index 1's remap trap is latent rather than
  live and no sweep can see it. It is pinned by a synthetic test instead. Expect more of this
  shape: a normalisation the client performs on load is invisible to a byte-identity sweep
  whenever the input that triggers it does not occur.
