# FlashEditor TODO

The running work list. `reference/index-survey/00-WORKLIST.md` is the per-index plan and stays
the authority on cache formats; this file is the wider product backlog, including everything
that is not a codec.

**Update this at milestones, not every commit.** A finished index, a shipped feature, a
direction change. If it is updated on every turn it becomes noise and stops being read.

**Do not put volatile numbers here.** Counts of our own code go stale by the next commit and get
read as targets. Counts *of the cache* are fine, because the cache does not change.

---

## Constraints that shape everything below

- **No BitBlt capture on this machine can see the OpenGL surface.** `tools/Capture-EditorTab.ps1`
  verifies every WinForms panel and is *no evidence at all* about the 3D viewer. Anything in the
  renderer is verified by a human looking at the screen. Budget for that.
- **Nothing in the test suite covers WinForms or the renderer.** A layout or render defect passes
  every test.
- **Serialise cache-backed test runs.** Parallelise the editing, serialise the sweeping.
- **A byte-identity sweep cannot see a normalisation whose triggering input is absent from the
  cache.** Those need synthetic tests. This has come up repeatedly and will keep coming up.

---

## In flight

Nothing. Every cache index with content now has a decoder, an encoder and a
whole-index byte-identity sweep, verified against both caches, sampled and full.

---

## Next

Each item is independently resumable. **The prompt is the whole brief** - paste it and go.

Prompts deliberately do not repeat the standing rules. Those live in `CLAUDE.md` and `AGENTS.md`,
every prompt opens by requiring them, and duplicating them here would let the two drift apart.
A prompt carries only what is specific to its item.

The rules a prompt relies on, so you can see they are covered: commit before anything
deliberately breaks the tree; test against both caches; assert relationships rather than counts;
settle behaviour from what the client does; separate stored from derived state; capture
non-canonical encodings; follow the **UI conventions** section of `CLAUDE.md` for anything with a
surface; and remember no capture on this machine can see the OpenGL viewport.

---

### 1. Index 26 materials - record the census

Smallest open item. Both runs print `UNRECORDED` for index 26's texture counts, so a change in
that population passes silently.

```
Read CLAUDE.md and AGENTS.md first; they carry the standing rules.

Index 26's sweep prints UNRECORDED for materials.declaredTextures and
materials.presentRecords, so those figures are asserted by nothing and a change in the
population would pass silently. Measured independently by walking the dat2 sector chain
outside our decoder: 915 declared and present in the vanilla b639 capture, 1,408 in the
repack.

Record them in FlashEditor.Tests/Cache/RealCache/RealCacheProfile.cs, the sanctioned home for
a measurement that genuinely differs between the two caches, and make the sweep assert them.
Re-measure rather than trusting the numbers above.

Run the suite against both caches. Commit.
```

---

### 2. Index 12 client scripts - a tab

Codec and sweep are done. A raw grid of numeric opcodes is barely better than hex, so be honest
about how far this goes.

```
Read CLAUDE.md and AGENTS.md first, including the UI conventions section.

Index 12 has a decoder, an encoder and a whole-index byte-identity sweep, and no tab. Give it
one via DefinitionListPanel and a descriptor.

Size it for the shape: 4,149 scripts, 335,158 instructions, largest script 7,084 instructions
and 106 KB decompressed. Decode on selection, not on load - a grid that materialises every
instruction of every script on tab load builds over a third of a million rows.

The identifier column must NOT be called a name hash. Index 12 sets the identifiers flag, but
the identifier is partly a packed interface hook and roughly 3,800 of the 4,149 are
unexplained 32-bit values. Label it "identifier".

A disassembler is OUT of scope here - it is its own item. Ship the raw instruction view and
say in the UI that opcodes are unnamed.

Screenshot the tab, read the PNG, fix what is clipped. Run the suite against both caches.
Commit.
```

---

### 3. Index 14 SFX2 - a tab

```
Read CLAUDE.md and AGENTS.md first, including the UI conventions section.

Index 14 has a codec and a whole-index sweep and no tab. Give it one via DefinitionListPanel:
3,657 groups, one Vorbis setup header plus 3,656 samples, 431,558 packets in total.

A sample is a header - sample rate, PCM byte count, loop start, loop end - plus a packet list.
Show those, the packet count and total packet bytes. Group 0 is structurally different from
the rest; present it as what it is rather than as a broken sample.

Playback is out of scope: the setup header has no magic, no channel count and no framing bit,
so no off-the-shelf decoder takes it, and a hand-written Vorbis decoder is a far larger job
than this tab. Say so in the UI rather than leaving a dead play button.

Screenshot the tab, read the PNG, fix what is clipped. Run the suite against both caches.
Commit.
```

---

### 4. Index 23 world map - a tab

```
Read CLAUDE.md and AGENTS.md first, including the UI conventions section.

Index 23 has a codec and a whole-index sweep and no tab. Three record families: area details,
the area raster tile stream, and fixed-size static elements, addressed by name hash at both
group and file level.

The interesting view is the raster: it is the world map the client draws, so render it rather
than tabulate it. Its icons resolve through index 2 group 36, which is implemented - prove
that join resolves rather than assuming it, the way object opcode 107 into group 36 was proven
by decoding every object.

Note the addressing trap already recorded: the area file is id 4 in most groups and id 0 in
the rest, so a reader assuming a fixed file id fails on a subset.

Screenshot the tab, read the PNG, fix what is clipped. Run the suite against both caches.
Commit.
```

---

### 5. Index 32 loading sprites - a tab

```
Read CLAUDE.md and AGENTS.md first, including the UI conventions section.

Index 32 has a codec and a whole-index sweep and no tab. It is MIXED: of its 26 groups, 21 are
JPEG and 5 are 256-frame Jagex glyph sheets. The tab has to present both and say which a group
is.

The JPEGs are 4-component, non-JFIF, with no Adobe marker. Every standard decoder renders them
as CMYK and produces a plausible, wrong image. The codec already carries a proven colour path
- use it, and do not substitute a library decoder because it looks easier.

Byte identity on the JPEG half is by construction: the encoder returns stored bytes. That
means an import replaces the file rather than transcoding it, and the UI should say so.

Screenshot the tab, read the PNG, fix what is clipped. Run the suite against both caches.
Commit.
```

---

### 6. Index 2 - surface the remaining families

```
Read CLAUDE.md and AGENTS.md first, including the UI conventions section.

The Config tab presents some of index 2's families. Five more landed with codecs and sweeps
and are not in it: identity kits, structs, light curves, render animations and quests. Add
them to the existing group selector rather than building a second tab.

Nineteen further groups are empty across every one of their files - a single 0x00 byte. They
must read as empty rather than as a blank grid that looks broken; that is roughly half of
index 2 by file count and it is a fact about the cache, not a gap.

The light curves deserve better than a field grid: they define how a point light flickers, and
an animated preview driven by the client's own formula shows in a second what four integers
cannot. Optional, but the highest-value view in this item.

Screenshot the tab, read the PNG, fix what is clipped. Run the suite against both caches.
Commit.
```

---

### 7. Wire the renderer into a tab

The engines exist, are mutation-tested and documented. **Nothing references them**, so none of it
is reachable. Highest-value item here.

```
Read CLAUDE.md and AGENTS.md first, including the UI conventions section.

FlashEditor/Rendering/ holds skeletal animation, a playback loop, ray-triangle picking with a
face and vertex index overlay, and a bounded particle simulation. All tested, all documented.
Editor.cs references none of it, so none of it is reachable from the application.

Wire it into the Models editor:
 - an animation selector, playing at a fixed render rate while advancing frames on the
   animation's own stored durations - those are different things and conflating them makes
   every animation play at the wrong speed
 - the wireframe and index overlay on hover, showing BOTH the face index and the vertex
   indices, because emitters anchor to a face and effectors to a vertex
 - particles for models carrying emitters or effectors
 - readable numbers beside the viewport: current frame, frame id, elapsed time, live particle
   count, active emitters

THE VIEWPORT CANNOT BE VERIFIED BY SCREENSHOT. No BitBlt capture on this machine sees the GL
surface - a control clearing to magenta captures blank. Verify everything AROUND it normally,
and for the picture produce a checklist naming specific model ids that carry skins and
particles, what correct looks like, and what a plausible wrong result looks like. Do not claim
the rendering works.

Run the suite against both caches. Commit.
```

---

### 8. Entities page

```
Read CLAUDE.md and AGENTS.md first, including the UI conventions section.

Seeing an item's model currently means opening Models first, then Items, then Models again.
Replace that with one Entities page: a type selector for Items, NPCs, Objects and Models, the
grid for the selected type, and a single persistent 3D viewport beside it.

ONE GL CONTEXT, NEVER REPARENTED. Moving a GLControl between parents destroys its window
handle and its context with it. The viewport stays put and the grid swaps.

For NPCs, list the animations the definition names and let them be cycled - that is the
feature this page exists for, and it depends on the renderer wiring item being done first.

Items, NPCs and Objects predate DefinitionListPanel and each re-implement the worker, progress
and edit commit. Migrating them is part of this item; do not leave three implementations
behind a new shell.

Screenshot it, read the PNG, fix what is clipped. Run the suite against both caches. Commit.
```

---

### 9. Interface name recovery

```
Read CLAUDE.md and AGENTS.md first.

Index 3's tab shows raw 32-bit identifiers where names would be far more useful. 27 group
names are recovered today; a verified list of 467 exists at
HydraScape/docs/cache-format/Leanbow Interface Names.txt, keyed by group id.

The hash is djb2 - h = h * 31 + c, no lowercasing, no offset. Confirmed against this cache;
the h * 61 + (c - 32) variant matched nothing. Verify that before relying on it.

Every entry must re-hash and match the stored identifier before it is displayed, so a wrong
row reads as unnamed rather than as a false name. Ship no unverified name.

Then extend coverage the cheap way: grep the 637 client and the HydraScape server for every
string literal, hash each, keep the matches. The client already asks index 3 for names
directly, so there will be more.

Do NOT brute force. djb2 is 32 bits and real names are twenty characters or more, so the
number of strings hashing to any given value is effectively unbounded and a cracked candidate
is a guess wearing a name.

Run the suite against both caches. Commit.
```

---

### 10. The editor half of the JS5 handshake

Without this the live-reload loop cannot be proven at all. The server half is written, compiles,
and has never run.

```
Read CLAUDE.md, AGENTS.md and the JS5 section of this file first.

HydraScape's update server can now rebuild its master index when the cache changes and release
its file handles so the cache can be replaced underneath it. The editor half does not exist,
so the loop has never run.

In the save path, behind a setting that is off by default because it must only fire when
pointed at a live server's cache:
 1. write reload.request into the cache directory
 2. wait for reload.released to appear, with a timeout and a clear failure message
 3. save the cache
 4. delete reload.request

The ordering is the whole point. The server holds read handles without FILE_SHARE_DELETE, so
on Windows the save FAILS while it runs - the release has to happen before the write, not
after.

Then prove it end to end: start the server with test_mode and load_js5 true and cache_path
pointed at the cache being edited, log in with any credentials (test mode grants rights 11
with no database), edit something visible, reconnect, and confirm the change is in game.
Report what actually happened rather than what should have.
```

---

### 11. Sprite import

```
Read CLAUDE.md and AGENTS.md first, including the UI conventions section.

Index 8 decodes to its stored form and encodes back, so import is now possible. Accept PNG,
JPG and BMP into the sprite editor. The existing ImportSpriteBtn has no handler.

Three real problems, each measured in shipped data:
 - Palette quantisation. Sprites are indexed, at most 255 colours, index 0 reserved for
   transparency. Decide whether to quantise or refuse an image with too many colours.
 - The black trap. A stored 0x000000 decodes as 0x000001 because index 0 is the transparent
   slot, and BOTH spellings occur in shipped palettes. Pure black must be written as 0x000001
   or it disappears.
 - The alpha plane is optional, and frames exist carrying a fully opaque one. Choose
   deliberately whether to emit one, and say why.

Traversal order is the trap no sweep catches: 2,767 frames are ambiguous between row-major and
column-major, and not one sets the column-major flag, so an encoder recomputing that flag
would sweep clean on both caches and corrupt the first sprite edited. Keep the stored flag.

Screenshot it, read the PNG, fix what is clipped. Run the suite against both caches. Commit.
```

---

### 12. Client background import

```
Read CLAUDE.md and AGENTS.md first, including the UI conventions section.

The client background lives in index 32, which is mostly JPEG rather than sprite format, so
this is a different path from sprite import and should not be folded into it.

A JPEG re-encode is no more reproducible than a GZip one, so import means STORING THE SUPPLIED
FILE, not transcoding it. Validate that what the user supplies is a JPEG the client can read -
4-component, non-JFIF - and refuse with a clear message rather than storing something that
renders as CMYK garbage in game.

Confirm from the client which group is actually the background before writing to one.

Screenshot it, read the PNG, fix what is clipped. Run the suite against both caches. Commit.
```

---

### 13. Index 12 disassembler

Deliberately separate from item 2. The codec is small and done; this is what makes a script tab
worth opening.

```
Read CLAUDE.md and AGENTS.md first.

Index 12's instructions are raw u16 opcodes. A grid reading "opcode 2426, byte 0" is barely
better than hex. This item gives them names and structure.

The bill is measured: 582 distinct opcodes across three client dispatchers, but 32 of them
carry 86 percent of all instructions - so a first pass covering the common ones is tractable
and immediately useful.

RuneStar on GitHub carries clientscript opcode definitions and decompilation work and is worth
consulting. CHECK ITS BUILD COVERAGE FIRST: it is oriented at later revisions, and an opcode
table from the wrong build is exactly the kind of plausible mapping this cache confirms by
accident. Anything taken from it must be verified against the 637 client before it ships.

Control flow reconstruction - the switch blocks and jump deltas the codec already decodes -
turns a linear listing into something readable. That is the second half and can be split off.

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
landing on a codebase that has just reached the point where every index sweeps green. The value
is real and the timing is bad.

Today we edit the packed cache in place. Every save rewrites the dat2 and the reference table of
every archive packed alongside, so version control sees one enormous binary change and cannot say
what actually altered. Git LFS makes that storable, not reviewable.

The alternative: dump every index to readable files on disk, treat THAT as the source of truth
under version control, and pack to a cache only when deploying. Changing one item definition then
shows up as a diff on one file. Models, sprites and audio stay in their native formats because
there is nothing more readable to convert them to, but their boundaries are still per-file rather
than per-cache.

**We are unusually well placed to do this, and should say why.** A dump-and-repack pipeline is
only safe if repacking reproduces what was dumped, and this project has already proved exactly
that for every index that holds content: decode, re-encode, compare against the stored bytes,
over both caches. Building this on top of an unproven codec would silently corrupt a cache; on
top of these sweeps it is mostly plumbing. The sweeps ARE the prerequisite and they are done.

Design notes to settle before starting:
- What is readable per index. Definitions want a text format; models, sprites, audio and JPEG
  payloads stay binary.
- Non-canonical encodings are the hazard. The dumped form has to carry every encoding choice the
  decoder records - opcode order, repetition, aliased values, absent-versus-default, smart widths
  - or a repack produces different bytes for an untouched record. Every one of those cases is
  already documented per index.
- Whether the packed cache is a build artefact (gitignored) or committed alongside.
- How this composes with the JS5 reload handshake: pack, then signal, then reload.

### Ideas worth taking from other editors

Seen in a 727-targeted editor. The formats will not transfer - that is a different build - but
these are presentation and workflow ideas, and several land on indexes we already decode.

**Judged rather than collected, because most of this is ordinary feature work and two items are
worth declining.** The unpacked working tree above is the only idea here that is better
*architecture* than what we have, and it is parked; everything below is either a gap we have not
filled or polish.

| Idea | Verdict |
|---|---|
| Divergence panel | **Take it.** Cheap, and we have a worse version of the problem with no answer: the map rasteriser deliberately is not the client's minimap, and nothing says so, so a user comparing them cannot tell a bug from a documented choice |
| Rebuild badges | **Take it.** Effectively free, and it generalises - several edits here are expensive for reasons invisible to the user |
| Environment editor | Real data we decode and do not surface. A gap, not a better design |
| Composite preview | Not a new idea - it is what the interface editor has to be anyway. The useful part is the sequencing: build the index-33 version first as a rehearsal for the harder index-3 one |
| Font, light-curve editors | Fill eventually. You would open them roughly never |
| Loading-screen simulator | **Decline.** A live crossfading playback engine for content tuned once. Impressive, poor value |
| First-person walk mode | **Decline for now.** Genuinely good, but it is a camera sitting on top of a 3D region renderer we have not built. The easy part of a hard job, and it reads as higher value than it is |

- **A first-person walk mode.** WASD and mouse look through the region, with ctrl-scroll changing
  plane. For a map editor this is the difference between arranging tiles and seeing what a player
  sees. Sits on the same scene the 3D view would use.
- **A region environment editor** - sun colour, ambient, light, backlight and direction, fog
  colour and depth, the six cube-map texture faces, bloom, skybox. Two things worth copying
  exactly: it marks which fields force a scene REBUILD because the sun's direction and ambient
  are baked into vertex colours rather than applied at draw, and it states that an untouched
  region repacks to identical bytes.
- **A client graphics settings mirror that is honest about divergence.** A panel listing each of
  the client's graphics options, what we do about it, and a badge reading applied, partial or not
  applicable - with a note saying what is not built. An editor that quietly renders differently
  from the client teaches the user wrong things; one that says "shadows: static grid only, no
  projected shadows" does not.
- **A font editor** over index 13: the glyph grid with each glyph's advance width editable
  in place, a live text preview, and import from a TTF or OTF. We decode index 13 already and it
  has no editor at all.
- **A light intensity editor** over index 2's light curves: an animated waveform preview showing
  how the brightness flickers, driven by the client's own formula, alongside the raw duration,
  amplitude and base fields. We decode these already.
- **A loading screen simulator** over index 33: play the real timings, crossfade between tips as
  the client does, and show the master rotation as thumbnails. Editing a timing restarts the
  simulation. We decode index 33 already.
- **Composite previews with per-component anchors.** The tip editor shows the composed screen at
  the true client size with a component list underneath - sprite id, anchor, offset, reorder
  arrows. That is the shape the interface editor below should take, and it is worth building the
  simpler index-33 version first as a rehearsal for the harder index-3 one.

### A real interface editor

The current Interfaces tab shows decoded fields. That is a viewer, not an editor. The goal is to
build interfaces, not inspect them:

- Canvas with direct manipulation: move, resize, select, marquee select, snap
- Z-order control, including send forward and send backward from a context menu
- Component creation and deletion for every type the format supports
- Sprite assignment, including per-state sprites so a button can show a different sprite when
  highlighted
- Embedded content: models, sprites and items placed inside a component
- Live preview that renders the interface as the client would draw it
- Anything the format can represent should be editable

This is the largest single item in this file and should be broken down before it is started.
The codec already round-trips every component byte-identically, which is the prerequisite.

### Name recovery beyond the known list

- Grep the 637 client for every string literal, hash each, and match against unmatched
  identifiers. The client already asks index 3 for names directly, so there will be more.
- Do the same against the HydraScape server sources.
- Token recombination over the vocabulary of the known names, which are structured
  `<system>_<thing>_<variant>` in snake_case.
- A generated table is acceptable if it stays modest on disk and CPU. **Blind brute force is
  not**: djb2 is 32 bits, real names are twenty characters or more, and the number of strings
  hashing to any given value at that length is effectively unbounded. A cracked candidate would
  be a guess wearing a name, which is the failure this project already has a scar from.
- Check the OpenRS2 archive for other 637 and 639 caches whose reference tables carry names this
  cache does not. The OpenRS2 *repository* ships no name dictionary; that was checked.

### JS5 and live reloading

**Evaluated. The server side is done; the editor side is not.**

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

Still to do:

1. **The editor half of the handshake.** In the save path: write `reload.request`, wait for
   `reload.released`, save, delete the request. Behind a setting, since it must only fire when
   pointed at a live server's cache.
2. **Prove the loop end to end** with a running server and client. Nothing above has been tested
   against either.
3. **Decide whether the sentinel is worth keeping** or should become a localhost admin socket.
   The handshake is deliberately crude and shuts the server for the duration.
4. A master index generator is **not** needed. The serving component computes it, and now
   recomputes it.

Out of reach without client changes: true live reload of content already loaded in a running
client. Definitions are memoised after first decode and scenes are baked at region load, so the
realistic target stays edit, reconnect, see the change.

### Smaller items

- Index 12 disassembler. The codec is done. 582 distinct opcodes, but 32 of them carry most
  instructions, so a first pass is tractable.
- Migrate the Items, Sprites, NPCs and Objects tabs onto `DefinitionListPanel`. They predate it
  and each re-implement the worker, progress and edit commit.
- `ConfigDefinition` is a fifth copy of the opcode-replay pattern and was deliberately not
  migrated when the shared one landed.
- The FPS timer invalidates the GL control 30 times a second for the life of the form, on every
  page, whether or not anything animates.
- `AnalyseCache` is a stub that always reports no differences.
- `MemoryUtils` is dead. Adopting it in `RSArchive.Decode` is the highest-value pooling site, but
  `ArrayPool.Rent` over-serves and `Return` does not clear, so it needs care rather than a swap.

---

## Done

Kept short. Detail lives in the git history, in `reference/index-survey/00-WORKLIST.md` and in
`reference/DOC-CONFLICTS.md`.

- **Every cache index that holds content now has a decoder, an encoder and a whole-index
  byte-identity sweep.** Indexes 34, 35 and 36 are empty in this cache and are struck off
  permanently. Index 12 ships without a tab by design; the six most recent codecs have no tab yet.
- The suite runs against the vanilla b639 capture by default and the private-server repack as a
  second gate, and asserts relationships rather than counts, so it holds on either.
- Shared foundations: `DefinitionSweep`, `DefinitionListPanel`, `CacheAddressing`, `OpcodeStream`,
  table-driven enumeration, the signed-smart writer, `RSCache.ReadGroup`.
- Whole-world map viewer with hover feedback and a vertex affordance for height edits,
  categorised navigation, and the form's autoscaling corrected at source.
- Textures load off the UI thread and fill progressively without rebuilding the list.
- Three live defects fixed: the map save path writing underwater terrain over the surface square,
  a malformed archive able to kill the process uncatchably, and index 26 discarding every field
  edit in silence.
- The JS5 update server recomputes its master index instead of freezing it at boot, and can
  release its file handles so a cache can be replaced underneath it.
