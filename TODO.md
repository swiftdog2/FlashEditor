# FlashEditor TODO

The running work list. `reference/index-survey/00-WORKLIST.md` is the per-index plan and stays
the authority on cache formats; this file is the wider product backlog, including everything
that is not a codec.

**Update this at milestones, not every commit.** A finished index, a shipped feature, a
direction change. If it is updated on every turn it becomes noise and stops being read.

**Do not put volatile numbers here.** Counts of our own code go stale by the next commit and get
read as targets. Counts *of the cache* are fine, because the cache does not change.

**Every claim below was checked against the code on 2026-08-05, not against the prose.** Seven were
wrong and are corrected in place: the sprite import button, the index-2 families, the index-9
sweep, the renderer's test coverage, the font editor, the environment data, and index 15 being
treated as empty when it holds 176 groups. A claim here that is not carrying a `file:line` is a
claim nobody has re-checked since.

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

Nothing.

---

## Next

Each item carries the prompt that resumes it, and **the prompt is the whole brief** - paste it
and go. Most items are independent. The four that are not say so in their heading, and those are
the only ordering constraints in this file.

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

### 1. Index 26 materials - record the census

Smallest open item, and the *only* unrecorded census in the suite: `materials.declaredTextures`
and `materials.presentRecords` are the sole `AssertCensus` keys with no entry in either profile
(`RealCacheProfile.cs:65` defines the prefix, `:540` prints it, `RealCacheMaterialTests.cs:134-135`
calls it).

```
Read CLAUDE.md and AGENTS.md first; they carry the standing rules.

RealCacheMaterialTests.cs:134-135 calls AssertCensus for materials.declaredTextures and
materials.presentRecords, and neither key exists in RealCacheProfile's Vanilla() or Repack()
dictionary. AssertCensus on a missing key only calls output.WriteLine, so those figures are
asserted by nothing and a change in the population would pass silently. Every other census key
in the suite has an entry in both dictionaries; these two are the only exceptions.

Measured independently by walking the dat2 sector chain outside our decoder: 915 declared and
present in the vanilla b639 capture, 1,408 in the repack. Re-measure rather than trusting those.

Record them in FlashEditor.Tests/Cache/RealCache/RealCacheProfile.cs, the sanctioned home for a
measurement that genuinely differs between the two caches, and make the sweep assert them.

Note that RealCacheMaterialTests.cs:138 already asserts the RELATIONSHIP the counts must satisfy
(2 + declared + present * BytesPerRecord == stored length). Adding the absolute figures pins the
population; do not weaken the relationship assertion to make room for them.

Run the suite against both caches. Commit.
```

---

### 2. Sprite tab - lay it out for images rather than for rows

The tab is the original item-list layout and has not changed since it was written. It renders
every sprite into a 20 pixel row (`Editor.Designer.cs:970`), which is the whole problem: sprites
run from 2x2 to over 400x200 and a single row height cannot serve both.

```
Read CLAUDE.md and AGENTS.md first, including the UI conventions section.

The Sprites tab (index 8) is a five column grid - ID, Frames, Width, Height, Image
(Editor.Designer.cs:956-960) - with RowHeight 20 (:970), a fixed Consolas 11.25 font (:965) and
absolute positioning (:968) rather than docking. Sprites vary from 2x2 to over 400x200, so one
row height cannot present them. Rebuild the tab around the images.

USE THE TEXTURES TAB AS THE REFERENCE, not the surrounding sprite code. Textures
(Editor.cs:1388-1457) does the three things this tab does not: it seeds every row before the
worker starts (SeedTextureGrid at :1455) so the grid is never empty, it replaces tiles in place
as batches arrive (ApplyTextureTiles at :1411) rather than rebuilding the list, and it handles
cancel and fault by clearing loadedTabs (:1430-1441) so a failed load can be retried. The sprite
worker calls SpriteListView.SetObjects from inside DoWork (Editor.cs:1254), which is the
cross-thread access CLAUDE.md's UI conventions forbid; populate in RunWorkerCompleted.

SCALING. Letterbox each sprite into a fixed tile, preserving aspect, never stretching:
 - Upscale small sprites by an INTEGER factor with nearest-neighbour sampling. A 2x2 sprite
   smoothed by the default interpolation is four grey blobs. Pixels must stay square and hard.
 - Downscale large sprites to fit the tile, and say somewhere that the tile is not full size.
 - Give the selected sprite a detail pane at 1:1 with a zoom control, because a thumbnail of a
   400x200 sprite is not something you can judge an edit against.
 - A checkerboard behind the image, not a solid colour. Index 0 is the transparent slot and a
   sprite that is entirely transparent must be distinguishable from one that failed to render.

FRAMES. A sprite set is a CANVAS plus N frames, which is what makes this more than a picture
list. SpriteDefinition.width/height (:70,:73) are the canvas; each frame is a sub-rectangle of
SubWidth x SubHeight at OffsetX,OffsetY (:216-217) that routinely does not reach the canvas edge.
GetFrames() (:456) rasterises each frame onto a canvas-sized image, and Rasterise (:482) grows
that canvas with Math.Max when a frame overflows it. So show BOTH: the frame's own pixels and
where it sits within the canvas, because an offset is invisible if you crop to the sub-rectangle.
The tree already models this - CanExpandGetter expands sets of more than one frame
(Editor.cs:1256-1261) and ChildrenGetter returns the frames (:1263-1266), which is legal because
RSBufferedImage derives from SpriteDefinition (Cache/Util/RSBufferedImage.cs:12). Keep that
relationship; replace only the presentation.

Measured in the vanilla b639 capture, from RealCacheProfile: 11,177 frames, of which only 44 sets
carry more than one frame, so multi-frame is the rare case and must not dominate the layout.
2,377 frames have ZERO AREA - they must read as empty rather than as a tile that failed to draw,
the same distinction the empty index-2 config groups need. 180 frames carry an alpha plane. In
the repack 11 frames overflow their declared canvas and in the vanilla capture none do, so the
overflow path is real and is exercised by only one of the two caches.

Screenshot the tab, read the PNG at native resolution, fix what is clipped. Run the suite against
both caches. Commit.
```

---

### 3. Index 14 SFX2 - a tab

Cheapest tab on this list: the descriptor is already written. `Sfx2ListDescriptor.cs` is a real
`DefinitionListDescriptor<Sfx2Listing>` and is currently referenced only by its own tests, so this
is largely a `Register` call and a panel.

```
Read CLAUDE.md and AGENTS.md first, including the UI conventions section.

Index 14 has a codec, a whole-index sweep (RealCacheSfx2Tests.cs:121-190) and a written
DefinitionListDescriptor<Sfx2Listing> in FlashEditor/Definitions/Audio/Sfx2/Sfx2ListDescriptor.cs
that nothing outside the tests references. It has no tab. Wire the descriptor up rather than
writing a second one: 3,657 groups, one Vorbis setup header plus 3,656 samples, 431,558 packets.

A sample is a header - sample rate, PCM byte count, loop start, loop end - plus a packet list.
Show those, the packet count and total packet bytes. Group 0 is structurally different from the
rest; present it as what it is rather than as a broken sample.

Playback is out of scope HERE: the setup header has no magic, no channel count and no framing
bit, so no off-the-shelf decoder takes it, and a hand-written Vorbis decoder is a far larger job
than this tab. Say so in the UI rather than leaving a dead play button. Item 16 is where that
decoder gets settled, because track playback cannot happen without it - so do not design this tab
in a way that makes adding playback later mean rebuilding it.

Screenshot the tab, read the PNG, fix what is clipped. Run the suite against both caches. Commit.
```

---

### 4. Index 12 client scripts - a tab

Codec and sweep are done. A raw grid of numeric opcodes is barely better than hex, so be honest
about how far this goes.

```
Read CLAUDE.md and AGENTS.md first, including the UI conventions section.

Index 12 has a decoder, an encoder and a whole-index byte-identity sweep
(RealCacheClientScriptTests.cs:70-109), and no tab. Give it one via DefinitionListPanel and a
descriptor.

Size it for the shape: 4,149 scripts, 335,158 instructions, largest script 7,084 instructions
and 106 KB decompressed. Decode on selection, not on load - a grid that materialises every
instruction of every script on tab load builds over a third of a million rows.

The identifier column must NOT be called a name hash. Index 12 sets the identifiers flag, but
the identifier is partly a packed interface hook and roughly 3,800 of the 4,149 are unexplained
32-bit values. Label it "identifier".

A disassembler is OUT of scope here - it is its own item. Ship the raw instruction view and say
in the UI that opcodes are unnamed. ClientScriptInstruction.cs:8-12 already states why the
opcodes are raw; the tab should say the same thing to the user.

Screenshot the tab, read the PNG, fix what is clipped. Run the suite against both caches. Commit.
```

---

### 5. Index 32 loading sprites - a tab

```
Read CLAUDE.md and AGENTS.md first, including the UI conventions section.

Index 32 has a codec and a whole-index sweep and no tab. It is MIXED: of its 26 groups, 21 are
JPEG and 5 are 256-frame Jagex glyph sheets. The tab has to present both and say which a group
is. LoadingSpriteDefinition.LooksLikeJpeg (:69-71) is the discriminator the decoder uses.

The JPEGs are 4-component, non-JFIF, with no Adobe marker. Every standard decoder renders them
as CMYK and produces a plausible, wrong image. The codec already carries a proven colour path in
JagexJpeg.cs - use it, and do not substitute a library decoder because it looks easier.

Byte identity on the JPEG half is by construction: Encode returns the stored bytes verbatim
(LoadingSpriteDefinition.cs:114-125). That means an import replaces the file rather than
transcoding it, and the UI should say so.

Screenshot the tab, read the PNG, fix what is clipped. Run the suite against both caches. Commit.
```

---

### 6. Index 23 world map - a tab

```
Read CLAUDE.md and AGENTS.md first, including the UI conventions section.

Index 23 has a codec (FlashEditor/Definitions/WorldMap/) and a whole-index sweep
(RealCacheWorldMapTests.cs:112-215) and no tab; nothing outside the tests references the reader.
Three record families: area details, the area raster tile stream, and fixed-size static elements,
addressed by name hash at both group and file level.

Name the tab so it cannot be confused with the existing Map tab. That one is index 5 terrain;
this is the world map the client draws over it.

The interesting view is the raster: render it rather than tabulate it. Its icons resolve through
index 2 group 36, which is implemented (MapElementDefinition.cs:225) - prove that join resolves
rather than assuming it, the way object opcode 107 into group 36 was proven by decoding every
object. A near-total aggregate match is not evidence; find the rows that are checkable alone.

Note the addressing trap already recorded: the area file is id 4 in most groups and id 0 in the
rest, so a reader assuming a fixed file id fails on a subset.

Screenshot the tab, read the PNG, fix what is clipped. Run the suite against both caches. Commit.
```

---

### 7. Index 9 textures - an encoder and a byte-identity sweep

**The only index with a decoder that does not re-encode.** This file previously claimed every
index was covered, which was wrong: `Texture.cs` has no `Encode` method at all, and
`TextureGraphConformanceTests` proves consumption, not identity. Index 15 is a different and worse
case, having no decoder either, and is item 16.

```
Read CLAUDE.md and AGENTS.md first, plus reference/index-survey/index-009-TEXTURES.md, which
already scopes this work in detail and is the authority on it.

Index 9 has a decoder, an evaluator and a gallery, and NO encoder: there is no Texture.Encode, no
TextureGraph serialiser, and no WriteFile call for index 9 anywhere. TextureGraphConformanceTests
sweeps consumption only, so index 9 is the sole content index in the cache with no byte-identity
sweep behind it. Close that.

Decode must first record what it currently throws away, or byte identity is unreachable.
Discarded today, per the survey: the per-node version byte (Texture.cs:325), the output-size byte
(:329), the opcode order and which opcodes were present, the type-29 shape payloads (skipped
blind at :629-632), and 10 trailing bytes that are never read. The survey's recommended design is
to capture each opcode's raw byte span at decode and have the encoder replay it, editing only the
spans the user touched - the same non-canonical problem every other index in this cache has.

Two traps the survey records. Type 12 swallows four opcodes - (12,2), (12,4), (12,5), (12,6),
Texture.cs:518-526 - which consume no bytes, so consumption still balances while an encoder built
from decoded state alone drops them and shortens those files. And compression is mixed, 507 of
946 groups stored uncompressed, so the sweep must compare DECOMPRESSED payloads.

Then the sweep, in the shape of the existing definition sweeps, through DefinitionSweep.

Run the suite against both caches. Commit.
```

---

### 8. Wire the renderer into a tab

The engines exist and are documented. **No production code constructs any of them**, so none of
it is reachable. Highest-value item here.

Two things about this layer that will mislead you if you meet them cold:

- **Commit `bddca21` reads alarming and is fine.** It is titled *"restore the layer from decompiled
  output to real source"*. All 19 files under `FlashEditor/Rendering/` were written across several
  waves, left untracked, and destroyed by an `rm -rf` during mutation testing. They were rebuilt
  against the surviving tests and checked at IL level against the pre-deletion build. What is on
  disk is real source with real XML docs, not a decompiler dump. The rules added afterwards to
  `CLAUDE.md` and `AGENTS.md` in `0be0fb7` are the fix and they bind you.
- **`/analyse-csharp` reports the whole directory as an unreferenced cluster, and that is expected
  rather than a finding.** It is also the cheapest proof this item landed: when the cluster goes
  live, the item is done.

```
Read CLAUDE.md and AGENTS.md first, including the UI conventions section.

FlashEditor/Rendering/ holds skeletal animation, a playback loop, ray-triangle picking with a
face and vertex index overlay, and a bounded particle simulation. Verified 2026-08-05: no
production code anywhere constructs AnimationPlayer, SkeletalAnimator, PickMesh, ParticleSystem
or ModelAttachments - every construction site is inside Rendering/ or in a test.

Two things make this smaller than it looks. The cache bridges are already written but never
instantiated: CacheAnimationDataSource (IAnimationDataSource.cs:43) and CacheParticleDataSource
(IParticleDataSource.cs:37). And the seam into the renderer already exists:
ModelRenderer.ApplyPose (ModelRenderer.cs:318) takes IReadOnlyList<PosedMesh> and calls
PosedNormals - Editor.cs simply never calls it. So the work is wiring, not construction.

THE MAP OF THE LAYER. 19 files, specified by 6 test files in FlashEditor.Tests/Rendering/.
Signatures are greppable; what is where is not:
 - Animation. SkeletalAnimator is the object the UI holds, and it owns an AnimationPlayer as
   .Player. Data arrives through IAnimationDataSource, which has a cache-backed and an in-memory
   implementation. That pairing is how the tests run with no cache present. Keep it.
 - Particles. ParticleSystem, with the same two-implementation split behind IParticleDataSource.
 - Picking and overlay. PickMesh does the ray-triangle picking, ViewportMath builds the ray and
   projects labels back to pixels, IndexLabelPainter draws them with GDI over the control rather
   than in GL, and ModelAttachments answers which emitters sit on a face and which effectors sit
   on a vertex.
 - ViewportOverlayRenderer is the only GL-touching piece and it is INTERNAL. Confirm the editor can
   reach it from wherever you intend to call it before designing around it.

WHERE IT PLUGS IN. Editor.Designer.cs:144 declares the single GLControl and :1432 hosts it in
splitContainer1.Panel1. Editor.cs:34 holds the ModelRenderer field, :321-330 is the whole of the GL
event wiring, and Gl_Paint is at :565. Before adding a vertex transform, read FrameModel
(Editor.cs:527) - its loop at :535-537 already does the /128 flip and is commented "Same transform
as AppendVertex / ModelRenderer", so there are at least three copies of it already and the overlay
should reuse rather than add a fourth.

THERE IS ONE GL CONTEXT IN THE APPLICATION. Do not reparent the control or add a second GLControl
for a preview pane without first proving the context survives it. Item 9 depends on this holding.

SETTLED, do not relitigate: render rate and animation rate are different things; emitters anchor to
a face while effectors anchor to a vertex, which is why the overlay shows both index kinds at once;
skeletal transforms stay CPU-side, matching the client's Renderable_Sub2.method2344.

STILL UNDECIDED, and yours to settle: where the animation selector and the numeric readouts live
inside the viewer, and whether the DefinitionListPanel convention fits a list that is filtered by
the selected model rather than enumerating an index.

Wire it into the Models editor:
 - an animation selector, playing at a fixed render rate while advancing frames on the
   animation's own stored durations - those are different things and conflating them makes every
   animation play at the wrong speed. AnimationPlaybackTests already pins that separation.
 - the wireframe and index overlay on hover, showing BOTH the face index and the vertex indices,
   because emitters anchor to a face and effectors to a vertex
 - particles for models carrying emitters or effectors
 - readable numbers beside the viewport: current frame, frame id, elapsed time, live particle
   count, active emitters

Fix the render timer while you are here rather than leaving it as a separate item. Editor.cs:329
sets _fpsTimer to 1000/30 ms and :330 invalidates glControl unconditionally on every tick, from
the constructor until OnFormClosed (:2455), on every page, with nothing animating. Gate it on the
viewport being visible and on something actually needing a frame. The 30 Hz figure already
matches AnimationPlayer.RenderFramesPerSecond (AnimationPlayer.cs:60); wire the two together
rather than leaving them coincidentally equal.

Do not trust "all tested" as a description of this folder. The pure-logic classes are heavily
covered, but IndexLabelPainter, ViewportOverlayRenderer, PosedNormals and both Cache*DataSource
classes have ZERO test coverage, and the first two say so in their own doc comments. The classes
you are about to make reachable are the least covered ones in the folder.

THE VIEWPORT CANNOT BE VERIFIED BY SCREENSHOT. No BitBlt capture on this machine sees the GL
surface - a control clearing to magenta captures blank. Everything AROUND it verifies normally:
the tab renders, the selector populates, the readouts update, the suite stays green.

For the picture, produce a checklist instead - specific model ids, what correct looks like, and
what a plausible wrong result looks like - so it can be confirmed by eye in one pass. THAT
CHECKLIST DOES NOT EXIST YET and the candidate ids have not been chosen. CLAUDE.md names model
15748 as a fast load, but that is a render-type case, not a skin or particle case, so it is a
starting point and not the answer. Find skinned candidates through models carrying vertex labels
and particle candidates through the spotanim definitions that reference emitters. Do not claim the
rendering works.

Run the suite against both caches. Commit.
```

---

### 9. Entities page. **Needs item 8 first.**

```
Read CLAUDE.md and AGENTS.md first, including the UI conventions section.

Seeing an item's model currently means opening Models first, then Items, then Models again.
Replace that with one Entities page: a type selector for Items, NPCs, Objects and Models, the
grid for the selected type, and a single persistent 3D viewport beside it.

ONE GL CONTEXT, NEVER REPARENTED. Moving a GLControl between parents destroys its window handle
and its context with it. The viewport stays put and the grid swaps.

For NPCs, list the animations the definition names and let them be cycled - that is the feature
this page exists for, and it depends on the renderer wiring item being done first.

Items, NPCs, Objects and Models all predate DefinitionListPanel: each is a bespoke BackgroundWorker
arm inside LoadEditorTab (Editor.cs:1147, 1272, 1336, 1459) re-implementing the worker, the
progress reporting and the edit commit. Migrating those four is part of this item; do not leave
four implementations behind a new shell. Textures (:1388) and the Console (:1106) are bespoke too
but are not entity grids and are out of scope here.

Screenshot it, read the PNG, fix what is clipped. Run the suite against both caches. Commit.
```

---

### 10. Sprite import from image formats. **Needs item 2 first.**

Scope corrected: the import button is **not** dead. `ImportSpriteBtn_Click` exists and works
(`Editor.cs:1595-1645`), but it only accepts the cache's own `.dat` sprite-set container. What is
missing is PNG, JPG and BMP.

```
Read CLAUDE.md and AGENTS.md first, including the UI conventions section.

ImportSpriteBtn already has a handler (Editor.Designer.cs:952 wires it, Editor.cs:1595-1645
implements it) and it is a good one: it decodes into a throwaway SpriteDefinition to validate
before touching anything (:1617), no-ops when the bytes match what is stored (:1621-1624), writes
through cache.WriteFile (:1626) and re-decodes the row in place (:1633-1636). Earlier notes here
called it a dead button; that was wrong. Keep this path and its validate-then-write shape.

What is missing is that its file filter (:1605) accepts only the cache's own .dat sprite-set
container. Add PNG, JPG and BMP, converting to the stored form.

Three real problems, each measured in shipped data:
 - Palette quantisation. Sprites are indexed, at most 255 colours, index 0 reserved for
   transparency. Decide whether to quantise or refuse an image with too many colours.
 - The black trap. A stored 0x000000 decodes as 0x000001 because index 0 is the transparent slot,
   and BOTH spellings occur in shipped palettes. Pure black must be written as 0x000001 or it
   disappears.
 - The alpha plane is optional, and frames exist carrying a fully opaque one. Choose deliberately
   whether to emit one, and say why.

Traversal order is the trap no sweep catches: 2,767 frames are ambiguous between row-major and
column-major, and not one sets the column-major flag, so an encoder recomputing that flag would
sweep clean on both caches and corrupt the first sprite edited. Keep the stored flag.

Screenshot it, read the PNG, fix what is clipped. Run the suite against both caches. Commit.
```

---

### 11. Client background import. **Needs item 5 first.**

```
Read CLAUDE.md and AGENTS.md first, including the UI conventions section.

The client background lives in index 32, which is mostly JPEG rather than sprite format, so this
is a different path from sprite import and should not be folded into it. There is no write path
to index 32 today: the only WriteFile call sites in the editor are sprites and items.

A JPEG re-encode is no more reproducible than a GZip one, so import means STORING THE SUPPLIED
FILE, not transcoding it - which is what the encoder already does
(LoadingSpriteDefinition.cs:114-125 returns the stored bytes). Validate that what the user
supplies is a JPEG the client can read - 4-component, non-JFIF - and refuse with a clear message
rather than storing something that renders as CMYK garbage in game. Those properties are recorded
as observations about existing data at JagexJpeg.cs:65-68 and are enforced nowhere; this item is
where they become a check.

Confirm from the client which group is actually the background before writing to one.

Screenshot it, read the PNG, fix what is clipped. Run the suite against both caches. Commit.
```

---

### 12. Interface name recovery

Absorbs what was a separate "name recovery beyond the known list" backlog section, which
described the same greps.

```
Read CLAUDE.md and AGENTS.md first.

Index 3's tab shows raw 32-bit identifiers where names would be far more useful. What exists
today is in FlashEditor/Definitions/Interfaces/InterfaceNames.cs: a curated dictionary of 27
group names (:49-77), each of which is only displayed if re-hashing it matches the stored
identifier (:91-100), plus a com_<fileId> rule for components (:112-118) that resolves 9,219 of
9,219 candidates. Both mechanisms are self-proving. Keep that property.

A verified list of 467 group names exists at
C:\Users\CJ\Desktop\HydraScape\docs\cache-format\Leanbow Interface Names.txt, keyed by group id.
It is in the sibling repo, not this one, and nothing here references it.

The hash is djb2 - h = h * 31 + c, no lowercasing, no offset - implemented at
Cache/Util/NameHasher.cs. Confirmed against this cache; the h * 61 + (c - 32) variant matched
nothing. Verify that before relying on it.

Every entry must re-hash and match the stored identifier before it is displayed, so a wrong row
reads as unnamed rather than as a false name. Ship no unverified name.

Then extend coverage the cheap way, in this order:
 - grep the 637 client for every string literal, hash each, keep the matches. The client asks
   index 3 for names directly, so there will be more.
 - do the same against the HydraScape server sources.
 - token recombination over the vocabulary of the known names, which are structured
   <system>_<thing>_<variant> in snake_case.
 - check the OpenRS2 archive for other 637 and 639 caches whose reference tables carry names this
   one does not. The OpenRS2 repository itself ships no name dictionary; that was checked.

A generated table is acceptable if it stays modest on disk and CPU. Do NOT brute force. djb2 is
32 bits and real names are twenty characters or more, so the number of strings hashing to any
given value is effectively unbounded and a cracked candidate is a guess wearing a name. This
project already has a scar from exactly that failure.

Run the suite against both caches. Commit.
```

---

### 13. The editor half of the JS5 handshake

Without this the live-reload loop cannot be proven at all. The server half is written, compiles,
and has never run. Nothing in `FlashEditor/` mentions the protocol and no setting exists for it.

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

### 14. Index 12 disassembler. **Needs item 4 first.**

Deliberately separate from the tab. The codec is small and done; this is what makes a script tab
worth opening. Nothing exists today: no opcode table, no mnemonics, no disassembler anywhere in
the repo.

```
Read CLAUDE.md and AGENTS.md first.

Index 12's instructions are raw u16 opcodes - ClientScriptInstruction.cs:56 stores the number and
:8-12 explains that naming it needs an opcode table spanning the three dispatchers in Class247,
which is this item. A grid reading "opcode 2426, byte 0" is barely better than hex.

The bill is measured: 582 distinct opcodes across three client dispatchers, but 32 of them carry
86 percent of all instructions - so a first pass covering the common ones is tractable and
immediately useful.

RuneStar on GitHub carries clientscript opcode definitions and decompilation work and is worth
consulting. CHECK ITS BUILD COVERAGE FIRST: it is oriented at later revisions, and an opcode
table from the wrong build is exactly the kind of plausible mapping this cache confirms by
accident. Anything taken from it must be verified against the 637 client before it ships.

Control flow reconstruction - the switch blocks and jump deltas the codec already decodes, 831
blocks and 11,962 cases - turns a linear listing into something readable. That is the second half
and can be split off.

Run the suite against both caches. Commit.
```

---

### 15. The defects the suite already pins

Seven `*_DocumentsKnownDefect` tests describe live, reproducible defects, and not one of them was
on this list. They are cheap to find and each is a real behaviour someone will hit.

```
Read CLAUDE.md and AGENTS.md first, including the *_DocumentsKnownDefect convention at
FlashEditor.Tests/Cache/RSFileStoreTests.cs:11-19.

Seven tests currently pin behaviour known to be wrong. Fixing any one of them is a deliberate,
visible change to its test - that is the point of the convention. Triage them, fix what is worth
fixing, and say why for anything left pinned.

Store defects, RSFileStoreTests.cs:
 - :241 GetIndexCount returns the highest non-meta index id rather than a count, so a
   for (i < GetIndexCount()) loop skips the highest-id index.
 - :252 a cache holding only index 0 reports GetIndexCount() == 0, so RSCache allocates a
   zero-length table array and loads nothing.
 - :373 allocation is append-only with no free list, so shrinking an archive leaves the surplus
   sectors zero-filled but still chained and permanently orphaned.
 - :446 against a zero-length dat2 the allocator hands out sector 0, but both readers treat
   sector 0 as end-of-chain, so the first write to a fresh cache fails its own verification.

Texture evaluator defects, TextureGraphEvaluatorTests.cs:
 - :351 node type 24 (merge-RGB) is missing from the colour-node classification, so it dispatches
   to the mono evaluator, which has no case for it, and always renders flat mid-grey. EvalMergeRGB
   is dead code.
 - :375 types 21 and 33 are classified as colour nodes with no colour implementation, so they
   silently pass the child's colour through instead of applying their operation.
 - :402 the transpose branch indexes with x * width + y instead of x * height + y, so a
   non-square transposed render walks off the pixel buffer and throws.

The four store defects are the ones with teeth: two of them affect writing a cache, which is what
this application is for. Take those first.

Run the suite against both caches. Commit.
```

---

### 16. Play a track the way the client does

The largest item in this section and the only one gated on an unsolved research question. The
Tracks tab can export MIDI (`TrackEditorPanel.cs:331-352`) and nothing in the editor can play it -
there is no `NAudio`, `winmm`, `SoundPlayer` or `WaveOut` reference anywhere in `FlashEditor/`.
Playing the exported file in any Windows player uses the GM synth, which is right for the stock
programs and wrong for Jagex's own bank, so the cheap version of this feature teaches the user
that tracks sound different from the game. The export path already knows this: `:315` notes a byte
is written unconditionally "so the file plays outside the client". This item builds the real one.

```
Read CLAUDE.md and AGENTS.md first, including the UI conventions section.

Give the Tracks tab playback that uses the cache's own instruments rather than a General MIDI
synth. Index 6 and index 11 both decode already and Track.Midi (Track.cs:222, built by BuildMidi
at :420) is a standard SMF; what is missing is everything under it. There is no audio output of
any kind in the project today - no NAudio, winmm, SoundPlayer or WaveOut reference exists in
FlashEditor/ - so the output path is being built from nothing as well.

INDEX 14 IS THE GATE, and it should be settled before anything else is built. Its Vorbis setup
header has no magic, no channel count and no framing bit, so no off-the-shelf decoder accepts it,
and item 3 above declared playback out of scope for exactly that reason. Either a hand-written
decoder or a proven way to feed the stored packets to an existing one is the largest part of this
item. Decide and prove that first; the rest is tractable and this is not. If it cannot be settled,
stop and report that rather than shipping a GM fallback, because a GM fallback is the thing this
item exists to replace.

Then index 15, which has NO decoder, no definition class, no test and no tab - the only occurrence
of its name in the whole solution is its own constant declaration. Build it the way every other
index was built: decoder, encoder, whole-index byte-identity sweep over both caches, through
DefinitionSweep. Measured by walking the dat2 outside our decoder: 176 declared and present
groups, one file each, ids 0-127 (GM melodic), then 128, 129, 136, 144, 152, 153, 168, 176, 178
and 184 (bank 1, the GM drum kits at canonical offsets), then 255 and 256-292 (bank 2, Jagex's
custom instruments). Re-measure rather than trusting those figures.

RSConstants.SFX3_INDEX (RSConstants.cs:32) is a wrong name for index 15 - it is the MIDI patch
bank, not a third sound-effect bank. The comment two lines above it already says so and cites
Particle_Sub3_Sub5_Sub2.java:99-100, so the constant currently contradicts its own neighbour. It
has zero adoption sites anywhere in the solution, so renaming it is free and is part of this item.

Only then the synth: bank and program derivation, note-to-sample mapping, mixing, and an output
device.

NOTHING IN THE SUITE CAN HEAR ANYTHING. Audio output is the same class of problem as the OpenGL
viewport: a synth that decodes every byte correctly and mixes them wrongly passes every sweep.
Test what is testable - the index 15 codec, the sample lookup, the bank and program derivation,
the note-to-sample mapping landing in range for both banks - and for the sound itself produce a
checklist naming specific track ids, which bank each leans on, and what wrong sounds like. Do not
claim it sounds right on the strength of a green suite.

Run the suite against both caches. Commit.
```

---

### 17. Font editing - the glyphs, not just the metrics

The tab exists and is not the editor the goal implies. It shows nine columns of scalars, of which
five are editable, and renders no glyph at all. The data it does not surface is most of the format.

```
Read CLAUDE.md and AGENTS.md first, including the UI conventions section.

Index 13 has a tab (Editor.cs:890, FontPanel is a raw DefinitionListPanel bound to
FontListDescriptor) and a byte-identity sweep (RealCacheFontTests.cs:73-111). Its columns are Font,
Name, Kerned, Line height, Ascent, Descent, Space adv, Byte 259 and Byte 260
(FontListDescriptor.cs:83-99), of which five are editable numbers. Nothing renders a glyph, there
is no preview, and the per-character data is not reachable at all. Build the real editor.

WHAT EXISTS AND IS NOT SURFACED, all on FontDefinition: AdvanceWidths, one byte per character for
256 characters (:137) - the single most useful editable field in a font and currently invisible;
GlyphRows (:215) and GlyphTops (:224); LeftEdgeProfiles (:235) and RightEdgeProfiles (:245), the
per-character edge insets; and KerningMatrix() (:373). Surface these.

THE PIXELS ARE NOT IN INDEX 13. Index 13 is metrics only. The glyph sheet is an index-8 sprite set
addressed by the SAME id - Editor.cs:886-889 records the relationship, and DOC-CONFLICTS states
that all 25 index-13 ids exist in index 8 with byte-identical name hashes, so the pairing is by id.
Nothing under FlashEditor/Definitions/Fonts/ references SPRITES_INDEX, so that join does not exist
yet. Building it is the first step and it is what makes every view below possible. Prove the join
row by row rather than on aggregate coverage - this project has a scar from a plausible join that
matched almost everything and was wrong.

Then build:
 - A glyph grid: every character as its rendered sprite, with its advance width editable in place.
   That is the view the old backlog entry asked for and the reason this item exists.
 - A live text preview - type a string, see it laid out using this font's own metrics, with kerning
   applied for kerned records. A preview is the only way to judge an advance-width edit.
 - The kerning matrix for kerned records, as a grid rather than as a number.

TWO LAYOUTS, NOT ONE. A kerned record and an unkerned one are different shapes: the unkerned
payload is 2 + 256 + 5 bytes (:59), while a kerned record carries the edge profiles and stores no
line-height byte at all, which is why SetLineHeight silently swallows an edit on one
(FontListDescriptor.cs:145-152). Every view has to handle both, and the UI should say which layout
a font is rather than showing an empty kerning grid that reads as broken.

TTF or OTF import is the natural end of this item and is the expensive half: it means rasterising
glyphs into the index-8 sprite format as well as writing index-13 metrics, so it inherits every
constraint of item 10 - the palette limit, the black trap, the stored traversal flag. Treat it as a
separate wave and do not start it until the grid and the preview work.

Index 13's sweep must stay green throughout. Edits go through the existing descriptor Encode
(FontListDescriptor.cs:134), which round-trips today.

Screenshot the tab, read the PNG at native resolution, fix what is clipped. Run the suite against
both caches. Commit.
```

---

## Backlog

### An unpacked working tree, packed on deploy - PARKED

**Deliberately not scheduled. Revisit when there is appetite for an architectural change, not
before.** The reasoning below is kept because it is durable and because the prerequisite is
already paid for, so picking it up later costs nothing to rediscover.

Why parked rather than dropped: it is the only idea on this page that is better architecture
rather than a missing feature, but it is also a cutover that touches how every index is stored,
landing on a codebase that has just reached the point where nearly every index sweeps green. The
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
only safe if repacking reproduces what was dumped, and this project has already proved exactly
that for every index that holds content bar indexes 9 and 15: decode, re-encode, compare against
the stored bytes, over both caches. Building this on top of an unproven codec would silently
corrupt a cache; on top of these sweeps it is mostly plumbing. The sweeps ARE the prerequisite,
and items 7 and 16 are the last two of them.

Nothing exists yet: the only export paths in the editor are selection-scoped
(`Editor.cs:1502`, `:1536`, `:1817`), and there is no bulk dump or repack anywhere.

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
these are presentation and workflow ideas. Judged rather than collected.

| Idea | Verdict |
|---|---|
| Divergence panel, rebuild badges | **Promoted out of this list.** Both are now standing UI conventions in `CLAUDE.md` ("say what the editor cannot do", "mark what an edit will cost"), so they bind every UI item above rather than waiting here as work |
| Font editor | **Now item 17.** Two corrections in a row here: this entry once said index 13 "has no editor at all", which was wrong because a tab exists; then it said "done" on the strength of `IsEditable => true`, which was also wrong. The tab edits five scalar metrics and surfaces neither the 256 advance widths nor a single glyph |
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

This is the largest single item in this file and should be broken down before it is started.
The codec already round-trips every component byte-identically, which is the prerequisite.

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
  `ConfigOpcode` struct and its own decode/encode loop, and 14 subclasses on each side. It was
  deliberately not migrated when the shared one landed, because its `Encode` is shaped around
  `WritePayload`/`AddedOpcodes` rather than a pre-built record list.
- `AnalyseCache` (`Editor.cs:1960`, not the 1064 previously recorded here) is a stub: it assigns
  `cacheOut` and never reads it, loads `inputCache` inside a `try` and never uses it, and
  unconditionally returns 0, so `AnalyseCaches` (`:1942-1958`) always reports no differences.
- `MemoryUtils` (`Utils/MemoryUtils.cs:9`) is dead - the only occurrence of the name in the whole
  solution is its own declaration. `RSArchive.Decode` hand-rolls the same idea instead
  (`RSArchive.cs:136-151`: one reused 4 KB buffer, `new byte[chunkSize]` above that). Adopting the
  pool there is the highest-value site, but `ArrayPool.Rent` over-serves and `Return` does not
  clear, so it needs `try`/`finally` and a slice at every use, not a swap.
- Migrate the Textures tab and the Console off their bespoke `LoadEditorTab` arms
  (`Editor.cs:1388`, `:1106`). The four entity grids are covered by item 9; the Track panel has
  its own worker too (`TrackEditorPanel.cs:197`), and Map and Huffman are deliberately not
  `DefinitionListPanel` tabs.

---

## Done

Kept short. Detail lives in the git history, in `reference/index-survey/00-WORKLIST.md` and in
`reference/DOC-CONFLICTS.md`.

- **Nearly every cache index that holds content has a decoder, an encoder and a whole-index
  byte-identity sweep.** This section previously claimed the coverage was total; it is not, and
  there are two exceptions, both scheduled above. **Index 9** has a decoder, an evaluator and a
  gallery but no encoder at all, so its sweep proves consumption rather than identity - item 7.
  **Index 15** has nothing: no decoder, no definition class, no test and no tab, despite holding
  176 declared and present groups, which is why it was mistaken for an empty index - item 16.
  Indexes 34, 35 and 36 really are empty in this cache and are struck off permanently.
- The suite runs against the vanilla b639 capture by default and the private-server repack as a
  second gate, and asserts relationships rather than counts, so it holds on either.
- Shared foundations: `DefinitionSweep`, `DefinitionListPanel`, `CacheAddressing`, `OpcodeStream`,
  table-driven enumeration, the signed-smart writer, `RSCache.ReadGroup`.
- **Index 2's config families are all reachable.** `ConfigEditorPanel.Bind` enumerates the open
  cache's own index-2 reference table and resolves every declared group through `ConfigFamily.For`
  (`ConfigFamily.cs:413-419`), falling back to a raw reader for anything unmodelled, so no decoded
  family can be missing from the selector. This file previously listed identity kits, structs,
  light curves, render animations and quests as unsurfaced; all five are registered
  (`ConfigFamily.cs:196, 280, 289, 302, 330`). Only the light-curve *preview* remains, in the
  ideas table above.
- Whole-world map viewer with hover feedback and a vertex affordance for height edits,
  categorised navigation, and the form's autoscaling corrected at source.
- Textures load off the UI thread and fill progressively without rebuilding the list.
- Sprite import from the cache's own `.dat` container, validating before it writes
  (`Editor.cs:1595-1645`). Image-format import is item 10.
- Three live defects fixed: the map save path writing underwater terrain over the surface square,
  a malformed archive able to kill the process uncatchably, and index 26 discarding every field
  edit in silence.
- The JS5 update server recomputes its master index instead of freezing it at boot, and can
  release its file handles so a cache can be replaced underneath it.
