# FlashEditor TODO

The running work list. `reference/index-survey/00-WORKLIST.md` is the per-index plan and stays
the authority on cache formats; this file is the wider product backlog, including everything
that is not a codec.

**Update this at milestones, not every commit.** A finished index, a shipped feature, a
direction change. If it is updated on every turn it becomes noise and stops being read.

**Do not put volatile numbers here.** Counts of our own code go stale by the next commit and get
read as targets. Counts *of the cache* are fine, because the cache does not change - but a count
that differs between the two caches must name which one it belongs to.

**Every claim below was checked against the code on 2026-08-08, at commit `64005a6`, not against
the prose.** Eight items that this file still listed as in flight or next had landed and are now in
Done; every `file:line` that survived was re-resolved at this commit, and `Editor.cs` moved by
several hundred lines because four bespoke tab loaders left it. **A claim here that is not carrying
a `file:line`, a named test, or the cache it belongs to, is a claim nobody has re-checked since.**

One claim in this pass was **reported as landed and is not**: the eyeball checklist for the 3D
viewer. It exists in no commit message, no source file and no document - `grep -i checklist` over
the tree finds only this file. The renderer is still unverified by any automated *or* manual means
and that is recorded as open, in Constraints and again under Smaller items.

---

## Constraints that shape everything below

- **No BitBlt capture on this machine can see the OpenGL surface.** `tools/Capture-EditorTab.ps1`
  verifies every WinForms panel and is *no evidence at all* about the 3D viewer. Anything in the
  renderer is verified by a human looking at the screen. Budget for that.
  **The renderer is now reachable from the running editor and is still verified by nothing.** The
  whole `FlashEditor/Rendering/` cluster is constructed from production code (`Editor.ModelAnimation.cs`),
  so a render defect now ships rather than sitting in dead code, and no test, no capture and no
  written checklist stands between it and a user. The candidate model ids have never been chosen.
- **Nothing in the test suite covers WinForms or the renderer.** A layout or render defect passes
  every test.
- **Serialise cache-backed test runs.** Parallelise the editing, serialise the sweeping.
- **A green filtered run is not a green suite, and a concurrency defect is invisible to anything
  narrower than the full sweep.** `ObjectDefinition.Decode` built a diagnostic string on every
  record into a **static** `StringBuilder` and handed it to a log level that discards it - the work
  unavoidable, the output unreachable. Two threads decoding index 16 at once clear and append to the
  same instance and `ToString` throws `ArgumentOutOfRangeException` on a chunk length that changed
  underneath it. It survived every narrow filtered run and failed only under a full sweep, because
  xunit parallelises collections across all cores and one class surveys index 16 while another
  decodes it. The fix and that explanation live at `ObjectDefinition.cs:405-417`. Two consequences
  worth carrying: a filtered run is a development aid and never a merge gate, and static mutable
  state in a decode path is a defect whether or not anything today calls it on two threads.
- **Toggling a flag is an edit the byte-identity sweeps cannot see.** They prove an **unedited**
  record re-encodes to what it was read from, which is a different claim from "an edit that nets
  nothing writes nothing". Four real defects lived in that gap, all found the week the Entities page
  put editable flag columns on real records: `DropOpcode` removed an opcode from the recorded stream
  and so threw its **position** away, making a set-then-unset re-emit it at the end - a record of the
  right length with a byte moved, which `DefinitionListPanel.CommitEdit` stages as a real change and
  which drags in the reference-table entry of every archive packed alongside; `mainOptionIndex`
  misreported its own value once its opcode was suppressed rather than removed; an NPC storing
  opcode 159 alone (2,195 of 13,359 in the vanilla capture, 2,198 in the repack) lost it over a round
  trip; and `ObjectDefinition.Encode` wrote the walk flags as either/or, so the 7 objects carrying
  both 17 and 18 would have lost one. Coverage is now **27 of 27** bare-flag properties - 2 item, 7
  NPC, 18 object - in `RealCacheBareFlagEditTests`, plus the two paired-opcode properties in
  `RealCachePairedFlagEditTests`, with
  `EveryBareOpcodeInTheCacheIsCoveredOrExempt` (`:189`) failing when a payload-free opcode the cache
  carries is neither tested nor listed as not-a-flag. **Add the same third check to any new edit
  path: set it, set it back, land on the original stored bytes.**
- **Evidence quality is measured by what a relation rejects, not by what it accepts.** The index-13
  to index-8 font join paired all 25 fonts against all 25 sheets and scored each relation on how
  many of the 600 **wrong** pairings it admits (`RealCacheFontGlyphSheetTests.cs:216-217`). Every
  relation scored a perfect 25 of 25 on the correct pairings and one of them - the ascent relation -
  lets **325 of 600** wrong pairings through, so a join built on it would have looked completely
  convincing. Only the rejection column separates proof from coincidence. Three more of this shape
  landed in the same run and each is worth more than its coverage figure: the index-12 branch
  arithmetic was settled by **one** script out of 4,149 (`RealCacheClientScriptTests.cs:327-331` -
  all 42,884 branches are in range under `position + 1 + delta`, exactly one is not under
  `position + delta`, and the 11,962 switch arms are consistent with both readings so they settle
  nothing); the world map icon join rests on **2** self-proving rows out of 965 placements
  (`RealCacheWorldMapIconJoinTests.cs:126`); and a whole-index sweep over 431,558 packets passed
  clean against a deliberately broken packet-length rule.
- **A code comment is prose wherever it states a count, and so is a survey document.** Both are
  written once and never re-measured, so two of them agreeing is one unmeasured sentence copied
  twice. The worked case and its correction are recorded in `reference/DOC-CONFLICTS.md`, under the
  index-9 node type 12 row - read it there rather than restating the set here, because a set
  restated in two places is exactly how that entry came to exist. **Reading the code is not
  measuring the data.** Where this file states a count, it names the test that printed it or the
  table it was read from.
- **A byte-identity sweep cannot see a normalisation whose triggering input is absent from the
  cache. This is now demonstrated rather than asserted, and index 14 is the worked example.** A
  whole-index sweep over every declared index-14 group re-encoded byte-identically against a
  deliberately broken packet-length rule, `(length - 1) / 255 + 1`. It passed clean because the
  longest packet in either cache is 147 bytes (`Sfx2Sample.cs:345`), so nothing shipped reaches
  the base-255 continuation boundary at all and every wrong prefix width agrees with the right one
  below it (`Sfx2ListDescriptor.cs:158-161` says the same thing about the detail pane's arithmetic).
  Only hand-built records caught it, at exactly 255, with 254 and 256 passing either way -
  `Sfx2CodecTests.cs:169-171`, and the boundary is asserted in its own right by
  `RealCacheSfx2Tests.ThePacketLengthContinuationByteIsUnreachableInThisCache` (`:371`), which
  measures the longest packet so the premise fails loudly if a cache ever ships a longer one. When
  a rule has a threshold, find the threshold and build a record that crosses it; the sweep will not
  do it for you.
- **A byte-identity sweep proves only what its encoder re-derives.** Index 9 is the standing
  example and says so in its own header (`RealCacheTextureGraphTests.cs:25-35`): the encoder replays
  each opcode's **stored payload span** rather than rebuilding it from the decoded node, because the
  format is non-canonical in five ways and re-deriving would rewrite untouched files. So that sweep
  is sharp about structure - node count, per-node opcode count, child run width, output indices,
  trailer - and says **nothing** about payload widths, which are pinned instead by the
  exact-consumption sweeps against a sentinel-padded buffer. Index 15 has the same shape and the
  same consequence, recorded against item 16. Whenever an encoder replays stored bytes, name what
  the sweep is then evidence *of*, and name the other test that covers the rest.
- **A warning count is only comparable against a build of the same scope.** An incremental build
  recompiles only the changed project, so the test project's warnings are not re-emitted and the
  total drops - 319 against 330 for the same tree in this repo. `CLAUDE.md` already requires the
  method behind a warning count to be stated; this is the form it takes here, and it is why two
  passes that "agree the warnings went down" can both be measuring nothing.

---

## In flight

Nothing.

---

## Next

Each item carries the prompt that resumes it, and **the prompt is the whole brief** - paste it
and go. **Two items remain and neither depends on the other**, so there are no ordering
constraints left in this file.

**Numbers are not reused.** Items 1 to 12, 14, 15 and 17 are done and their numbers stay retired,
because other items and other documents cite items by number and a renumber breaks a
cross-reference silently. The gaps in the numbering below are that, not an omission.

Prompts deliberately do not repeat the standing rules. Those live in `CLAUDE.md` and `AGENTS.md`,
every prompt opens by requiring them, and duplicating them here would let the two drift apart. A
prompt carries only what is specific to its item, plus the closing verify-and-commit line, which
is repeated on purpose: it is the hand-off ritual rather than a rule.

The rules a prompt relies on, so you can see they are covered: commit before anything
deliberately breaks the tree; test against both caches; assert relationships rather than counts;
settle behaviour from what the client does; separate stored from derived state; capture
non-canonical encodings; follow the **UI conventions** section of `CLAUDE.md` for anything with a
surface; and remember no capture on this machine can see the OpenGL viewport.

---

### 13. The editor half of the JS5 handshake

**Untouched. Re-checked at this commit and unchanged.** Without this the live-reload loop cannot be
proven at all. The server half is written, compiles, and has never run. Nothing in `FlashEditor/`
mentions the protocol - `grep` finds no occurrence of `reload.request` or `reload.released` anywhere
in the project - and no setting exists for it. It is the smaller of the two items left.

```
Read CLAUDE.md, AGENTS.md and the JS5 section of this file first.

HydraScape's update server can now rebuild its master index when the cache changes and release
its file handles so the cache can be replaced underneath it. The editor half does not exist -
there is no reload.request, reload.released or handshake logic anywhere in FlashEditor/, and no
setting in Properties/Settings - so the loop has never run.

In the save path, behind a setting that is off by default because it must only fire when pointed
at a live server's cache:
 1. write reload.request into the cache directory
 2. wait for reload.released to appear, with a timeout and a clear failure message
 3. save the cache
 4. delete reload.request

The ordering is the whole point. The server holds read handles without FILE_SHARE_DELETE, so on
Windows the save FAILS while it runs - the release has to happen before the write, not after.

Then prove it end to end: start the server with test_mode and load_js5 true and cache_path
pointed at the cache being edited, log in with any credentials (test mode grants rights 11 with
no database), edit something visible, reconnect, and confirm the change is in game. Report what
actually happened rather than what should have.

While you are in the save path: decide point 3 of the JS5 backlog section below, whether the
sentinel is worth keeping or should become a localhost admin socket. That decision is cheapest to
make with this code in front of you.
```

---

### 16. Play a track the way the client does

**Partly landed, and the half that landed is the easy one.** The index-15 codec that gated the synth
is built: `MidiPatchDefinition` and `MidiPatchEnvelope` decode and re-encode every declared patch
byte-identically in both caches through `DefinitionSweep`, and `RSConstants.SFX3_INDEX` is now
`MIDI_PATCH_INDEX` (`RSConstants.cs:37`, first adoption site `CacheAddressing.cs:343`). **This is the
only large item left in the queue** - the interface editor in the backlog is larger still, and is
deliberately not scheduled. What remains here is the Vorbis question, the semantic gap the codec's
own sweep cannot close, the synth, and an output path - and the last of those still does not exist
at all: no `NAudio`, `winmm`, `SoundPlayer` or `WaveOut` reference occurs anywhere under
`FlashEditor/`, re-checked at this commit.

```
Read CLAUDE.md and AGENTS.md first, including the UI conventions section.

Give the Tracks tab playback that uses the cache's own instruments rather than a General MIDI
synth. Index 6 and index 11 both decode already and Track.Midi (Track.cs:222, built by BuildMidi
at :397) is a standard SMF; the export path is TrackEditorPanel.ExportSelected (:330) and its
comment at :315 notes a byte written unconditionally "so the file plays outside the client", which is the
cheap version of this feature admitting what it is. There is still no audio output of any kind in
the project - no NAudio, winmm, SoundPlayer or WaveOut reference exists anywhere in FlashEditor/ -
so the output path is being built from nothing as well.

INDEX 14 IS THE GATE, and it should be settled before anything else is built. Its Vorbis setup
header has no magic, no channel count and no framing bit, so no off-the-shelf decoder accepts it,
and the index-14 tab says so on screen for exactly that reason. Either a hand-written decoder or
a proven way to feed the stored packets to an existing one is the largest part of this item.
Decide and prove that first; the rest is tractable and this is not. If it cannot be settled, stop
and report that rather than shipping a GM fallback, because a GM fallback is the thing this item
exists to replace.

INDEX 15'S SEMANTIC ACCESSORS ARE UNDEFENDED, and closing that comes before the synth is built on
top of them. The codec stores each per-key attribute as a run-length plane and Encode
(MidiPatchDefinition.cs:525) writes those planes back verbatim, so the byte-identity sweep proves
the planes survive a round trip and says nothing at all about how they expand. That is the same
shape as index 9's replay sweep in the constraints above, and it is STILL TRUE at this commit.
WalkPans (:692), WalkEnvelopes (:720), WalkVolumes (:746) and WalkMuteGroups (:665) are what the
synth will actually read, through PanOf (:336), EnvelopeOf (:341), VolumeOf (:346), BankOf (:259),
HeldOf (:283) and MuteGroupOf (:323). Today PanOf, EnvelopeOf and VolumeOf are called by nothing
outside the class, and BankOf, HeldOf and MuteGroupOf are exercised only as aggregate tallies by
RealCacheMidiPatchTests.TheMidiPatchBank_HoldsWhatTheCodecClaimsItDoes (:153). A run list
walked with an off-by-one would keep every tally plausible, re-encode byte-identically, and hand
the synth the wrong instrument on a key boundary. Pin the walks against hand-built plane bytes
where the expected per-key array is written out by hand - the run boundary is the case, the same
way the index-14 packet-length boundary was.

Only then the synth: bank and program derivation, note-to-sample mapping, mixing, and an output
device.

NOTHING IN THE SUITE CAN HEAR ANYTHING. Audio output is the same class of problem as the OpenGL
viewport: a synth that decodes every byte correctly and mixes them wrongly passes every sweep.
Test what is testable - the sample lookup, the bank and program derivation, the per-key walks
above, the note-to-sample mapping landing in range for both banks - and for the sound itself
produce a checklist naming specific track ids, which bank each leans on, and what wrong sounds
like. Do not claim it sounds right on the strength of a green suite.

Run the suite against both caches. Commit.
```

---

## Backlog

### An unpacked working tree, packed on deploy - PARKED

**Deliberately not scheduled. Revisit when there is appetite for an architectural change, not
before.** The reasoning below is kept because it is durable and because the prerequisite is
already paid for, so picking it up later costs nothing to rediscover.

Why parked rather than dropped: it is the only idea on this page that is better architecture
rather than a missing feature, but it is also a cutover that touches how every index is stored,
landing on a codebase that has just reached the point where every index sweeps green. The
value is real and the timing is bad.

Today we edit the packed cache in place. Every save rewrites the dat2 and the reference table of
every archive packed alongside, so version control sees one enormous binary change and cannot say
what actually altered. Git LFS makes that storable, not reviewable.

The alternative: dump every index to readable files on disk, treat THAT as the source of truth
under version control, and pack to a cache only when deploying. Changing one item definition then
shows up as a diff on one file. Models, sprites and audio stay in their native formats because
there is nothing more readable to convert them to, but their boundaries are still per-file rather
than per-cache.

**We are unusually well placed to do this, and should say why.** A dump-and-repack pipeline is
only safe if repacking reproduces what was dumped, and this project has now proved exactly that
for **every** index that holds content: decode, re-encode, compare against the stored bytes, over
both caches. Indexes 9 and 15 were the last two exceptions and both are closed. Building this on
top of an unproven codec would silently corrupt a cache; on top of these sweeps it is mostly
plumbing. **The sweeps were the prerequisite and it is now paid**, which is the one thing that
changed about this entry.

Nothing exists yet: the only export paths in the editor are selection-scoped, and there is no bulk
dump or repack anywhere.

Design notes to settle before starting:
- What is readable per index. Definitions want a text format; models, sprites, audio and JPEG
  payloads stay binary.
- Non-canonical encodings are the hazard. The dumped form has to carry every encoding choice the
  decoder records - opcode order, repetition, aliased values, absent-versus-default, smart widths,
  and index 9's raw per-opcode payload spans - or a repack produces different bytes for an
  untouched record. Every one of those cases is already documented per index.
- Whether the packed cache is a build artefact (gitignored) or committed alongside.
- How this composes with the JS5 reload handshake: pack, then signal, then reload.

### Ideas worth taking from other editors

Seen in a 727-targeted editor. The formats will not transfer - that is a different build - but
these are presentation and workflow ideas. Judged rather than collected.

| Idea | Verdict |
|---|---|
| Divergence panel, rebuild badges | **Promoted out of this list.** Both are now standing UI conventions in `CLAUDE.md` ("say what the editor cannot do", "mark what an edit will cost"), so they bind every UI item in this file rather than waiting here as work |
| Font editor | **Done, as item 17.** Three verdicts were written here before one was right: "no editor at all" (wrong, a tab existed), "done" on the strength of `IsEditable => true` (wrong, it edited five scalar metrics), then "the glyphs are missing" (right). The glyph grid, the metrics preview and the kerning grid now exist in `FontEditorPanel.cs`, over the index-13 to index-8 join |
| Environment editor | **Needs a decoder before it needs a panel.** The earlier verdict "real data we decode and do not surface" was wrong: `Region.ParseExtrasTail` (`Region.cs:198-246`) walks the environment block by opcode length and skips it, preserving the bytes raw in `ExtrasTail` so they round-trip. No sun colour, ambient, backlight, fog or bloom field is decoded anywhere. The only named thing that exists is the six cube-map texture ids in index 28 (`SceneDefaultsDefinition.cs:51`), which are already on the Defaults tab |
| Light-curve preview | The one survivor of what used to be a whole index-2 item. Group 31 decodes to waveform, rate, amplitude and offset (`LightIntensityDefinition.cs:66`) and the Config tab shows them as four numbers in a grid. An animated preview driven by the client's own formula shows in a second what four integers cannot. You would open it rarely; it is still the only view that makes the family mean anything |
| Composite preview | Not a new idea - it is what the interface editor below has to be anyway. Index 33 already has a tab and it is read-only (`LoadingScreenListDescriptors.cs:311-316`), so the rehearsal is an addition to it rather than a build |
| Loading-screen simulator | **Decline.** A live crossfading playback engine for content tuned once. Impressive, poor value |
| First-person walk mode | **Decline for now.** Genuinely good, but it is a camera sitting on top of a 3D region renderer we have not built. The easy part of a hard job, and it reads as higher value than it is |

### A real interface editor

The Interfaces tab lists interfaces, then a component grid, then a read-only field pane. The
component grid does edit X, Y, Width and Height as cells (`InterfaceComponentListDescriptor.cs:156-163`),
so it is not purely a viewer - but everything else is read-only and there is no canvas. The goal
is to build interfaces, not inspect them:

- Canvas with direct manipulation: move, resize, select, marquee select, snap
- Z-order control, including send forward and send backward from a context menu
- Component creation and deletion for every type the format supports
- Sprite assignment, including per-state sprites so a button can show a different sprite when
  highlighted
- Embedded content: models, sprites and items placed inside a component
- Live preview that renders the interface as the client would draw it
- Anything the format can represent should be editable

This is the largest single item in this file - larger than anything in the queue above - and it
sits in the backlog because it should be broken down before it is started.
The codec already round-trips every component byte-identically, which is the prerequisite, and
most rows now carry a verified name rather than a bare 32-bit hash (item 12), which is what makes
a canvas navigable.

### JS5 and live reloading

**Evaluated. The server side is done; the editor side is item 13 above.**

What the evaluation settled, so nobody re-derives it:

- **The standalone update server serves JS5**, on port 43594. The game server binds 5915 only and
  refuses a JS5 handshake outright. Settled by the ports, not by structure.
- **There are three copies of the cache code.** `server/src/net/tazogaming/hydra/cacheserver/` is
  the real one and is where to work. `js5/` at the HydraScape root is a flattened extract that
  does not compile - packages stripped, imports not, and six classes in the default package,
  which cannot be imported at all. `server/src/.../io/js5/` is the game server's read-only
  reader and serves nothing.
- **Every group and every reference table is read from disk per request.** Only the master index
  was held in memory, built once at boot, which is why an edited cache was fully readable and
  completely invisible: the client compared against a frozen checksum table and never asked for
  a group.
- **The game server does not read the cache for most item data.** Weight, shop and exchange
  price, examine text, tradeable and wilderness rules live in `config/item/item_definitions.cfg`;
  map clipping lives in loose files under `server/config/mapdata/`. Editing an item in the cache
  changes its appearance, name and stack behaviour, not its price or weight. Both have to be
  edited to change both.

Done on the server, compiling but **not yet run against a live client**:

- The master index rebuilds when the cache files change, checked on the 255/255 request.
- `FileStore.close()`, and `load()` reopens rather than reusing a stale handle.
- `CacheWatcher`, a sentinel handshake, because a plain file watcher cannot work here: the stores
  hold read handles without FILE_SHARE_DELETE, so on Windows the editor's write fails while the
  server runs, the files never change, and a watcher waiting for a change waits forever. The
  release has to come first, so the editor has to ask. Protocol is `reload.request` from the
  editor, `reload.released` back once handles are shut, request deleted after the write, then
  reload.
- The cache path takes `-Dcache.path=`, since the hardcoded `data/cache/` does not exist.

Still open, beyond item 13:

- **Decide whether the sentinel is worth keeping** or should become a localhost admin socket. The
  handshake is deliberately crude and shuts the server for the duration. Item 13 asks for this
  decision, since it is cheapest to make with that code open.
- A master index generator is **not** needed. The serving component computes it, and now
  recomputes it.

Out of reach without client changes: true live reload of content already loaded in a running
client. Definitions are memoised after first decode and scenes are baked at region load, so the
realistic target stays edit, reconnect, see the change.

### Smaller items

- `ConfigDefinition` (`Definitions/Config/ConfigDefinition.cs:56`) is a second, hand-rolled
  implementation of the opcode-replay pattern alongside `OpcodeStreamDefinition`, with its own
  `ConfigOpcode` struct (`:15`) and its own decode/encode loop, and 14 subclasses on each side. It
  was deliberately not migrated when the shared one landed, because its `Encode` is shaped around
  `WritePayload` (`:140`) and `AddedOpcodes` (`:118`) rather than a pre-built record list.
- `AnalyseCache` (`Editor.cs:1969`) is a stub: it assigns `cacheOut` and never reads it, loads
  `inputCache` inside a `try` and never uses it, and unconditionally returns 0, so `AnalyseCaches`
  (`:1951`) always reports no differences.
- `MemoryUtils` (`Utils/MemoryUtils.cs:9`) is dead - the only occurrence of the name in the whole
  solution is its own declaration. `RSArchive.Decode` hand-rolls the same idea instead
  (`RSArchive.cs:136-147`: one reused 4 KB buffer, `new byte[chunkSize]` above that). Adopting the
  pool there is the highest-value site, but `ArrayPool.Rent` over-serves and `Return` does not
  clear, so it needs `try`/`finally` and a slice at every use, not a swap.
- Migrate the Console, the Sprites tab and the Textures tab off their bespoke `LoadEditorTab` arms.
  **Three arms are left in that switch and no more**: `META_INDEX` (`Editor.cs:1356`),
  `SPRITES_INDEX` (`:1397`) and `TEXTURES` (`:1486`) - items, NPCs, objects and models all left with
  the Entities page. The Track panel has its own worker too (`TrackEditorPanel.cs:197`), and Tracks,
  Map and Huffman are deliberately not `DefinitionListPanel` tabs. **The UI conventions section of
  `CLAUDE.md` still names seven predating tabs with their old line numbers and is stale on this
  point; correct it when that file is next edited.**
- **Sprite import into a multi-frame set discards every frame but one.** A picture describes one
  frame, so `ImportSpriteFromPicture` (`Editor.cs:1766`) warns and asks before it stages
  (`:1781-1783`), which makes it honest rather than complete. 44 sets carry more than one frame in
  both caches (`RealCacheProfile` `sprite.multiFrameSets`, the same figure in each). The honest fix
  is a per-frame import: choose which frame a picture replaces, and let a set be assembled from
  several. Inherits every constraint the whole-set import already carries - the 255 entry palette,
  the black trap, the stored traversal flag.
- **Choose the 3D viewer's eyeball checklist and write it down.** The renderer is reachable from
  production code now and is verified by nothing at all; the checklist that was to stand in for a
  test does not exist in any commit message, source file or document. It needs specific model ids,
  what correct looks like and what a plausible wrong result looks like. `CLAUDE.md` names model
  15748 as a fast load, but that is a render-type case rather than a skinned or particle one, so it
  is a starting point and not the answer. Find skinned candidates through models carrying vertex
  labels and particle candidates through the spotanim definitions that reference emitters.
- **Settle what the client's JVM actually draws for an index-32 image, or record that it cannot be
  settled from this side.** The stored files are four-component with no `JFIF APP0` and no
  `Adobe APP14`, which is the combination libjpeg resolves as CMYK, and the client reaches the bytes
  through `Toolkit.createImage` with no colour handling of its own
  (`Class271.method3277`, `Class271.java:29-65`). This editor draws them as luma, Cb, Cr and a
  discarded fourth plane, inferred from the files' own quantisation tables and sampling factors and
  stated nowhere - `JpegRaster.cs:107-113` and `LoadingSpriteJpegPolicy.cs:11-25` both say so in
  their own headers. **So the reading may be right about the data and still not be what the client
  paints.** Genuinely open, and the reason the import path refuses anything but the shape the
  client's own probe demonstrated rather than trying to be clever about colour.

---

## Done

Kept short. Detail lives in the git history, in `reference/index-survey/00-WORKLIST.md` and in
`reference/DOC-CONFLICTS.md`. **Where an item shipped with a gap, the gap is named in the same
bullet rather than dropped**, and the actionable ones are also listed under Smaller items above.

- **Sprite tab rebuilt around images** (item 2). `SpriteTileGeometry`, `SpritePainter` and
  `SpriteCanvas` under `Definitions/Sprites/`: letterboxed on a checkerboard, integer
  nearest-neighbour magnification so a 2x2 sprite stays four hard pixels, a 1:1 detail pane with a
  zoom control, and the frame's own rectangle shown against the canvas it sits in. Both caches
  declare the same 4,593 groups; the drawable-versus-empty split is a **printed** census from
  `RealCacheSpriteTileTests.cs:137`, which reported 4,566 sets drawing a picture and 27 drawing the
  empty marker, and which asserts only that the two add up to what the table declares.
  *Gap:* a picture describes one frame, so importing into one of the 44 multi-frame sets keeps one
  and discards the rest behind a dialog.
- **World Map Overview tab** (item 6), index 23, registered at `Editor.cs:1056` and named at
  `Editor.Designer.cs:1353` so it cannot be read as the index-5 Map tab. It renders the raster
  rather than tabulating it, over the 39 areas the index holds. The icon join was settled before
  anything was drawn through it and rests on 2 self-proving rows out of 965 placements, not on
  coverage.
- **The renderer is wired into a tab** (item 8), through `Editor.ModelAnimation.cs`: animation
  selector and transport, hover picking with face and vertex indices, particles, and numeric
  readouts. The whole `FlashEditor/Rendering/` cluster is now constructed from production code, so
  `/analyse-csharp` no longer reports it as an unreferenced cluster - which was the cheapest proof
  the item had landed. The render timer no longer repaints a hidden GL surface: it is gated on the
  viewport being visible **and** on something needing a frame (`SyncViewportTimer`), and its
  interval is read from `AnimationPlayer.RenderFramesPerSecond` rather than restated as 1000/30.
  *Gap:* nothing verifies the picture. See Constraints and Smaller items.
- **Entities page** (item 9): items, NPCs, objects and models on one page beside one persistent
  viewport, registered once at `Editor.cs:946`. All four left their bespoke `LoadEditorTab` arms for
  `DefinitionListDescriptor` implementations under `Definitions/Entities/`. Putting editable flag
  columns on real records is what exposed the four bare-flag defects recorded in Constraints.
- **Sprite import from PNG, JPEG and BMP** (item 10), converting to the stored indexed form with
  median-cut quantisation, and warning before it stages whenever the conversion loses something the
  result cannot show - colours merged, frames dropped, canvas resized (`Editor.cs:1766`).
- **Index 32 replacement validation** (item 11). `LoadingSpriteJpegPolicy` refuses anything that is
  not the shape the client's own capability probe demonstrated, rather than anything that is not a
  JPEG - so an ordinary three-component JFIF, which previews here perfectly well, is refused with
  the reason stated on screen. *Gap:* what the client's JVM would actually paint is still open, and
  is recorded under Smaller items.
- **Index 12 disassembler** (item 14). `ClientScriptOpcodes` names 68 opcodes, every one cited to
  the line of `Class247.java` that proves it, and nothing was taken from RuneStar - its table is
  OSRS 194-199 and declares `SWITCH = 60`, where this cache holds 831 opcode-51 switches. The tab
  shows mnemonics and resolves jump targets. Naming coverage is **printed, never asserted against a
  floor**, by `RealCacheClientScriptTests.TheDisassembler_NamesEveryInLineOpcodeAndReportsWhatThatCovers`;
  the figure carried into this pass, 89.64% of instructions over 63 of the distinct opcodes in use,
  comes from that printed census and not from anything in the tree. What **is** asserted is that
  every opcode below 100 the cache uses carries a mnemonic, and that nothing reaches 10,000.
  *Split off, not done:* basic blocks, loops and if/else are not reconstructed - this is a linear
  listing and the tab says so (`ClientScriptEditorPanel.cs:141`).
- **Font glyph editor** (item 17). The index-13 to index-8 join is built in `FontGlyphSheet` and
  surfaced by `FontEditorPanel`: a glyph grid, a live metrics preview that states it is not the
  client's text renderer, and a kerning grid. The join is proven by falsification rather than by
  coverage, which is where the rejection-column lesson in Constraints came from.

- **Every cache index that holds content now has a decoder, an encoder and a whole-index
  byte-identity sweep.** The two exceptions this section used to carry are closed. **Index 9**
  gained `Texture.Encode` and `TextureGraphRecord`, which keeps the per-node version byte, the
  output-size byte, each opcode's raw payload span in stream order, the child run and the 10-byte
  trailer, so a graph re-encodes without re-deriving anything - 915 of 915 groups in the vanilla
  b639 capture and 946 of 946 in the repack (item 7). Both of those sweeps replay stored bytes, so
  read the constraint above on what a replay sweep is evidence of before quoting either. **Index 15** gained
  `MidiPatchDefinition`/`MidiPatchEnvelope`, 176 patches byte-identical in both caches, and
  `SFX3_INDEX` became `MIDI_PATCH_INDEX` (item 16a). Indexes 34, 35 and 36 really are empty in
  this cache and are struck off permanently.
- The suite runs against the vanilla b639 capture by default and the private-server repack as a
  second gate, and asserts relationships rather than counts, so it holds on either.
- Shared foundations: `DefinitionSweep`, `DefinitionListPanel`, `CacheAddressing`, `OpcodeStream`,
  table-driven enumeration, the signed-smart writer, `RSCache.ReadGroup`.
- **The index-26 materials census is asserted rather than printed** (item 1).
  `materials.declaredTextures` and `materials.presentRecords` were the only `AssertCensus` keys
  with no entry in either profile; both now sit in both dictionaries, at 915 in the vanilla
  capture (`RealCacheProfile.cs:271-272`) and 1408 in the repack (`:460-461`), alongside the
  relationship assertion that was already there.
- **All seven `*_DocumentsKnownDefect` tests are fixed, and the convention is now carried by its
  own doc comments rather than by any live test** (item 15). Four store defects: `GetIndexCount`
  replaced by `HighestContentIndexId` and `ContentIndexIds`, chain termination on shrink plus an
  in-memory free list, and sector 0 no longer handed out on a fresh dat2. Three texture-evaluator
  defects: types 21 and 33, the transpose stride, and type 24 - which the old item text called
  merge-RGB and which is actually `Node_Sub10_Sub16`, `super(1, true)`, a **monochrome** node, so
  the premise recorded here was itself wrong and the fix ran the other way.
- **Three new tabs, each a `DefinitionListPanel` descriptor plus a panel registered in
  `RegisterEditorTabs`**: index 14 SFX2 (item 3, `Editor.cs:979`), index 12 client scripts
  (item 4, `:1047`) and index 32 loading sprites (item 5, `:1032`). The index-32 tab also shipped
  the validating Replace path that item 11 later narrowed to a shape policy.
- **Interface name recovery** (item 12). 27 curated group names became 434 verified group names
  (`InterfaceNameTable.cs:22`) plus 75 bespoke component names (`:469`), every one of which is
  re-hashed against the identifier the loaded cache holds before it is shown, so a name that is
  wrong or right for another build reads as unnamed. The generated routes ship only against a
  stated corroboration bar, measured against a foreign-identifier null rather than against decoys.
  The `com_<fileId>` rule resolves 9,249 components in the vanilla capture and 9,219 in the
  repack - the single figure this file used to carry was the repack's.
  **Route 4 of the old prompt, "check other OpenRS2 caches for names this one lacks", is struck
  permanently as structurally void**: index 3's identifier is a 32-bit int and never a string, so
  another cache can only ever supply more hashes to crack, which is the one thing this work
  refuses to do.
- Whole-world map viewer with hover feedback and a vertex affordance for height edits,
  categorised navigation, and the form's autoscaling corrected at source.
- Textures load off the UI thread and fill progressively without rebuilding the list.
- Sprite import from the cache's own `.dat` container, validating before it writes
  (`ImportSpriteBtn_Click`, `Editor.cs:1722`). Picture formats came later, as item 10.
- Three live defects fixed: the map save path writing underwater terrain over the surface square,
  a malformed archive able to kill the process uncatchably, and index 26 discarding every field
  edit in silence.
- The JS5 update server recomputes its master index instead of freezing it at boot, and can
  release its file handles so a cache can be replaced underneath it.
