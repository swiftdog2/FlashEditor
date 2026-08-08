# FlashEditor TODO

The running work list. `reference/index-survey/00-WORKLIST.md` is the per-index plan and stays
the authority on cache formats; this file is the wider product backlog, including everything
that is not a codec.

**Update this at milestones, not every commit.** A finished index, a shipped feature, a
direction change. If it is updated on every turn it becomes noise and stops being read.

**Do not put volatile numbers here.** Counts of our own code go stale by the next commit and get
read as targets. Counts *of the cache* are fine, because the cache does not change - but a count
that differs between the two caches must name which one it belongs to.

**Every claim below was checked against the code on 2026-08-08, at commit `b6e5dfb`, not against
the prose.** Three figures inherited from the previous pass were repack-scoped and are corrected
here with both caches named: index 9's group count and its uncompressed share, and index 3's
component-name coverage. One structural claim was struck as void rather than deferred (route 4 of
item 12), and one hand-off note about type 12's swallowed opcodes named four where the code and the
survey both say two. Every surviving `file:line` was re-resolved at this commit, which moved most
of them - `Editor.cs` alone gained roughly twenty lines per new tab - and one named the wrong
directory outright (`MapElementDefinition.cs` is under `Definitions/Config/`, not `WorldMap/`).
A claim here that is not carrying a `file:line` is a claim nobody has re-checked since.

---

## Constraints that shape everything below

- **No BitBlt capture on this machine can see the OpenGL surface.** `tools/Capture-EditorTab.ps1`
  verifies every WinForms panel and is *no evidence at all* about the 3D viewer. Anything in the
  renderer is verified by a human looking at the screen. Budget for that.
- **Nothing in the test suite covers WinForms or the renderer.** A layout or render defect passes
  every test.
- **Serialise cache-backed test runs.** Parallelise the editing, serialise the sweeping.
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
- **A warning count is only comparable against a build of the same scope.** An incremental build
  recompiles only the changed project, so the test project's warnings are not re-emitted and the
  total drops - 319 against 330 for the same tree in this repo. `CLAUDE.md` already requires the
  method behind a warning count to be stated; this is the form it takes here, and it is why two
  passes that "agree the warnings went down" can both be measuring nothing.

---

## In flight

Three items are being worked in parallel worktrees right now. Their results are not visible from
here, so they are listed rather than judged. **Confirm each against the tree before picking one
up** - it may already be done, and its `file:line` citations below were verified at `b6e5dfb`,
which is the base those worktrees branched from.

### 2. Sprite tab - lay it out for images rather than for rows

The tab is the original item-list layout. It renders every sprite into a 20 pixel row
(`Editor.Designer.cs:976`), which is the whole problem: sprites run from 2x2 to over 400x200 and a
single row height cannot serve both.

```
Read CLAUDE.md and AGENTS.md first, including the UI conventions section.

The Sprites tab (index 8) is a five column grid - ID, Frames, Width, Height, Image
(Editor.Designer.cs:962-966) - with RowHeight 20 (:976), a fixed Consolas 11.25 font (:971) and
absolute positioning (:974) rather than docking. Sprites vary from 2x2 to over 400x200, so one
row height cannot present them. Rebuild the tab around the images.

USE THE TEXTURES TAB AS THE REFERENCE, not the surrounding sprite code. Textures
(Editor.cs:1408-1477) does the three things this tab does not: it seeds every row before the
worker starts (SeedTextureGrid at :2562, called at :1475) so the grid is never empty, it replaces
tiles in place as batches arrive (ApplyTextureTiles at :2631, called at :1431) rather than
rebuilding the list, and it handles cancel and fault by clearing loadedTabs (:1450-1460) so a
failed load can be retried. The sprite worker calls SpriteListView.SetObjects from inside DoWork
(Editor.cs:1274), which is the cross-thread access CLAUDE.md's UI conventions forbid; populate in
RunWorkerCompleted.

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
SubWidth x SubHeight at OffsetX,OffsetY (:216-219) that routinely does not reach the canvas edge.
GetFrames() (:456) rasterises each frame onto a canvas-sized image, and Rasterise (:479) grows
that canvas with Math.Max when a frame overflows it. So show BOTH: the frame's own pixels and
where it sits within the canvas, because an offset is invisible if you crop to the sub-rectangle.
The tree already models this - CanExpandGetter expands sets of more than one frame
(Editor.cs:1276-1281) and ChildrenGetter returns the frames (:1283-1286), which is legal because
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

### 6. Index 23 world map - a tab

```
Read CLAUDE.md and AGENTS.md first, including the UI conventions section.

Index 23 has a codec (FlashEditor/Definitions/WorldMap/) and a whole-index sweep
(RealCacheWorldMapTests.cs:113) and no tab; nothing outside the tests references the reader, and
RegisterEditorTabs (Editor.cs:801-924) names no page against RSConstants.WORLD_MAP
(RSConstants.cs:48). Three record families: area details, the area raster tile stream, and
fixed-size static elements, addressed by name hash at both group and file level.

Name the tab so it cannot be confused with the existing Map tab. That one is index 5 terrain;
this is the world map the client draws over it.

The interesting view is the raster: render it rather than tabulate it. Its icons resolve through
index 2 group 36, which is implemented - and note the file is Definitions/Config/
MapElementDefinition.cs, not under WorldMap/, because it is an index-2 config record that the
world map happens to be the main consumer of; its sprite ids are read at :227-228 - prove that
join resolves
rather than assuming it, the way object opcode 107 into group 36 was proven by decoding every
object. A near-total aggregate match is not evidence; find the rows that are checkable alone.
RealCacheWorldMapTests already draws that line twice, at :409 and :468, where a static element
must name a map element and a tile element must name an object; reuse those rather than inventing
a third reading.

Note the addressing trap the suite already pins: the area file is id 4 in most groups and id 0 in
the rest (RealCacheWorldMapTests.TheRasterFileIdIsNotFixedAcrossAreas, :237), so a reader assuming
a fixed file id fails on a subset.

Screenshot the tab, read the PNG, fix what is clipped. Run the suite against both caches. Commit.
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
face and vertex index overlay, and a bounded particle simulation. Verified again at b6e5dfb: no
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

WHERE IT PLUGS IN. Editor.Designer.cs:144 declares the single GLControl and :1438 hosts it in
splitContainer1.Panel1. Editor.cs:34 holds the ModelRenderer field, :322-331 is the whole of the GL
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
the constructor until OnFormClosed (:2483), on every page, with nothing animating. Gate it on the
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

## Next

Each item carries the prompt that resumes it, and **the prompt is the whole brief** - paste it
and go. Most items are independent. The two that are not say so in their heading, and those are
the only ordering constraints in this file.

**Numbers are not reused.** Items 1, 3, 4, 5, 7, 12 and 15 are done and their numbers stay
retired, because other items and other documents cite items by number and a renumber breaks a
cross-reference silently.

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
arm inside LoadEditorTab (Editor.cs:1167, 1292, 1356, 1479) re-implementing the worker, the
progress reporting and the edit commit. Migrating those four is part of this item; do not leave
four implementations behind a new shell. Sprites (:1227), Textures (:1408) and the Console (:1126)
are bespoke too; sprites is item 2's and the other two are in the smaller-items list, so all three
are out of scope here.

Screenshot it, read the PNG, fix what is clipped. Run the suite against both caches. Commit.
```

---

### 10. Sprite import from image formats. **Needs item 2 first.**

Scope corrected in an earlier pass and re-checked here: the import button is **not** dead.
`ImportSpriteBtn_Click` exists and works (`Editor.cs:1615-1665`), but it only accepts the cache's
own `.dat` sprite-set container. What is missing is PNG, JPG and BMP.

```
Read CLAUDE.md and AGENTS.md first, including the UI conventions section.

ImportSpriteBtn already has a handler (Editor.Designer.cs:958 wires it, Editor.cs:1615-1665
implements it) and it is a good one: it decodes into a throwaway SpriteDefinition to validate
before touching anything (:1637), no-ops when the bytes match what is stored (:1641), writes
through cache.WriteFile (:1646) and re-decodes the row in place (:1654). Earlier notes here
called it a dead button; that was wrong. Keep this path and its validate-then-write shape.

The index-32 Replace path (LoadingSpriteEditorPanel.cs:444-495) is the same shape done more
recently and with its refusals written out; read it before adding a second dialect of the same
idea.

What is missing is that its file filter (:1625) accepts only the cache's own .dat sprite-set
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
sweep clean on both caches and corrupt the first sprite edited. Keep the stored flag. That is the
same class of blind spot the constraints section above now has a worked example of.

Screenshot it, read the PNG, fix what is clipped. Run the suite against both caches. Commit.
```

---

### 11. Client background import - the two checks the index-32 tab did not make

**Scope narrowed, because most of this landed with the index-32 tab.** A validating Replace path
now exists (`LoadingSpriteEditorPanel.ReplaceStored`, `:444-495`): it stores the supplied file
verbatim rather than transcoding, refuses a file that does not open `FF D8` (`:459`), refuses one
byte-identical to what is stored so a no-op save writes nothing (`:465`), and refuses one whose
preview will not build - which covers both "will not parse" and "the Jagex colour path cannot
render it" (`:475`). What is left is two questions it deliberately did not answer.

```
Read CLAUDE.md and AGENTS.md first, including the UI conventions section.

Two open questions about index 32's import path. Both are settled from the client, not from us.

FIRST: a three-component JFIF JPEG is currently accepted, and nobody has established whether it
should be. JpegRaster.ToArgb (:130) admits a component count of 1, 3 or 4 and applies the same
YCbCr transform to 3 and 4, so an ordinary camera or Photoshop JPEG imports and previews
correctly in the editor. Every image index 32 actually holds is four-component, baseline, no JFIF
APP0 and no Adobe APP14, sampled 2x2, 1x1, 1x1, 2x2 - recorded with its evidence at
JagexJpeg.cs:55-72, which ties the shape to the client's own 1x1 capability probe
(Class74.aByteArray546, decoded by Class116.method2162, Class116.java:60-77) rather than to an
inference about the data.

DO NOT ASSUME THE ANSWER IS "REFUSE". Class271.method3277 (Class271.java:29-65) hands the stored
bytes straight to Toolkit.getDefaultToolkit().createImage and grabs the result with a
PixelGrabber, with no colour transform and no component handling anywhere - so on the face of it
the JVM would decode a standard JFIF perfectly well and a refusal would be the editor being
stricter than the client. Settle it from what that path does, and whichever way it goes, say so
in the UI and pin it with a synthetic three-component file. The preview must never show a picture
the client would draw differently.

SECOND: which group is the background. LoadingSpriteNames names only the four glyph sheets -
p11_full, p12_full and b12_full resolve by name out of Class84.java:20-31, and the fourth was
recovered against its stored hash - and states outright that THE TWENTY-ONE JPEG GROUPS ARE NOT
NAMED. So the background cannot be identified by name today, and no write should be aimed at a
particular group until the client says which one it loads. Find the call site, not a plausible id.

Screenshot it, read the PNG, fix what is clipped. Run the suite against both caches. Commit.
```

---

### 13. The editor half of the JS5 handshake

Without this the live-reload loop cannot be proven at all. The server half is written, compiles,
and has never run. Nothing in `FlashEditor/` mentions the protocol - no occurrence of
`reload.request` or `reload.released` anywhere in the project - and no setting exists for it.

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

### 14. Index 12 disassembler

Deliberately separate from the tab, which now exists. The codec is small and done; this is what
makes the script tab worth opening. Nothing exists today: no opcode table, no mnemonics, no
disassembler anywhere in the repo.

```
Read CLAUDE.md and AGENTS.md first.

Index 12's instructions are raw u16 opcodes - ClientScriptInstruction.cs:56 stores the number and
:7-13 explains that naming it needs an opcode table spanning the three dispatchers in Class247:
the in-line chain below 100 (Class247.java:7781-7988), method3148 for 100..4999 and method3156
for 5000..9999. That table is this item. The tab (ClientScriptEditorPanel) shows the raw numbers
and says in the UI that they are unnamed; replacing that message is how this item finishes.

The bill is measured, and every figure here is identical in both caches, so it is a property of
the format rather than of one capture (RealCacheProfile.cs:308-318 and :492-502): 335,158
instructions, 582 distinct opcodes, highest opcode 7314, 831 switch blocks and 11,962 cases
across 485 scripts. 32 opcodes carry 86 percent of all instructions, so a first pass covering the
common ones is tractable and immediately useful.

RuneStar on GitHub carries clientscript opcode definitions and decompilation work and is worth
consulting. CHECK ITS BUILD COVERAGE FIRST: it is oriented at later revisions, and an opcode
table from the wrong build is exactly the kind of plausible mapping this cache confirms by
accident. Anything taken from it must be verified against the 637 client before it ships.

Control flow reconstruction - the switch blocks and jump deltas the codec already decodes - turns
a linear listing into something readable. That is the second half and can be split off.

Run the suite against both caches. Commit.
```

---

### 16. Play a track the way the client does

**Partly landed.** The index-15 codec that gated the synth is built: `MidiPatchDefinition` and
`MidiPatchEnvelope` decode and re-encode every declared patch byte-identically in both caches
through `DefinitionSweep`, and `RSConstants.SFX3_INDEX` is now `MIDI_PATCH_INDEX`
(`RSConstants.cs:37`, first adoption site `CacheAddressing.cs:343`). What remains is the hard
half: the Vorbis question, the semantic gap the codec's own sweep cannot close, the synth, and
an output path.

```
Read CLAUDE.md and AGENTS.md first, including the UI conventions section.

Give the Tracks tab playback that uses the cache's own instruments rather than a General MIDI
synth. Index 6 and index 11 both decode already and Track.Midi (Track.cs:222, built by BuildMidi
at :420) is a standard SMF; the export path is at TrackEditorPanel.cs:330-355 and its comment at
:315 notes a byte written unconditionally "so the file plays outside the client", which is the
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
the planes survive a round trip and says nothing at all about how they expand. WalkPans (:692),
WalkEnvelopes (:720), WalkVolumes (:746) and WalkMuteGroups are what the synth will actually
read, through PanOf (:336), EnvelopeOf (:341), VolumeOf (:346), BankOf (:259), HeldOf (:283) and
MuteGroupOf (:323). Today PanOf, EnvelopeOf and VolumeOf are called by nothing outside the class,
and BankOf, HeldOf and MuteGroupOf are exercised only as aggregate tallies by
RealCacheMidiPatchTests.TheMidiPatchBank_HoldsWhatTheCodecClaimsItDoes (:152-215). A run list
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

### 17. Font editing - the glyphs, not just the metrics

The tab exists and is not the editor the goal implies. It shows nine columns of scalars, of which
five are editable, and renders no glyph at all. The data it does not surface is most of the format.

```
Read CLAUDE.md and AGENTS.md first, including the UI conventions section.

Index 13 has a tab (registered at Editor.cs:903; FontPanel is a raw DefinitionListPanel bound to
FontListDescriptor) and a byte-identity sweep (RealCacheFontTests.cs:109). Its columns are Font,
Name, Kerned, Line height, Ascent, Descent, Space adv, Byte 259 and Byte 260
(FontListDescriptor.cs:83-98), of which five are editable numbers. Nothing renders a glyph, there
is no preview, and the per-character data is not reachable at all. Build the real editor.

WHAT EXISTS AND IS NOT SURFACED, all on FontDefinition: AdvanceWidths, one byte per character for
256 characters (:137) - the single most useful editable field in a font and currently invisible;
GlyphRows (:215) and GlyphTops (:224); LeftEdgeProfiles (:235) and RightEdgeProfiles (:245), the
per-character edge insets; and KerningMatrix() (:373). Surface these.

THE PIXELS ARE NOT IN INDEX 13. Index 13 is metrics only. The glyph sheet is an index-8 sprite set
addressed by the SAME id, and the join is already proven row by row in the suite -
RealCacheFontTests.EveryFont_HasAGlyphSheetAtTheSameIdInIndexEight (:241) - but it exists nowhere
in production: nothing under FlashEditor/Definitions/Fonts/ references SPRITES_INDEX. Building
that join in the panel is the first step and it is what makes every view below possible. Carry the
test's per-row standard across; do not settle for aggregate coverage.

Then build:
 - A glyph grid: every character as its rendered sprite, with its advance width editable in place.
   That is the view the old backlog entry asked for and the reason this item exists.
 - A live text preview - type a string, see it laid out using this font's own metrics, with kerning
   applied for kerned records. A preview is the only way to judge an advance-width edit.
 - The kerning matrix for kerned records, as a grid rather than as a number.

TWO LAYOUTS, NOT ONE, AND ONLY ONE OF THEM SHIPS IN THIS CACHE. The unkerned payload is
2 + 256 + 5 bytes (FontDefinition.UnkernedLength, :59), while a kerned record carries the edge
profiles and stores no line-height byte at all, which is why SetLineHeight silently swallows an
edit on one (FontListDescriptor.cs:148-152). NO FONT IN EITHER CACHE SETS THE KERNING FLAG -
asserted, not printed, by RealCacheFontTests.NoFontInThisCache_SetsTheKerningFlag (:167). So the
kerned half is reachable only through synthetic records, the kerning grid will be empty for every
real font, and the UI has to say which layout a font is rather than leaving an empty grid that
reads as broken. If that test ever fails, the sweep has started covering ground the synthetic
records were standing in for, and this item's kerned views become real.

TTF or OTF import is the natural end of this item and is the expensive half: it means rasterising
glyphs into the index-8 sprite format as well as writing index-13 metrics, so it inherits every
constraint of item 10 - the palette limit, the black trap, the stored traversal flag. Treat it as a
separate wave and do not start it until the grid and the preview work.

Index 13's sweep must stay green throughout. Edits go through the existing descriptor Encode
(FontListDescriptor.cs:131), which round-trips today.

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
- `AnalyseCache` (`Editor.cs:1980`) is a stub: it assigns `cacheOut` and never reads it, loads
  `inputCache` inside a `try` and never uses it, and unconditionally returns 0, so `AnalyseCaches`
  (`:1962-1978`) always reports no differences.
- `MemoryUtils` (`Utils/MemoryUtils.cs:9`) is dead - the only occurrence of the name in the whole
  solution is its own declaration. `RSArchive.Decode` hand-rolls the same idea instead
  (`RSArchive.cs:136-147`: one reused 4 KB buffer, `new byte[chunkSize]` above that). Adopting the
  pool there is the highest-value site, but `ArrayPool.Rent` over-serves and `Return` does not
  clear, so it needs `try`/`finally` and a slice at every use, not a swap.
- Migrate the Textures tab and the Console off their bespoke `LoadEditorTab` arms
  (`Editor.cs:1408`, `:1126`). The four entity grids are covered by item 9 and sprites by item 2;
  the Track panel has its own worker too (`TrackEditorPanel.cs:197`), and Map and Huffman are
  deliberately not `DefinitionListPanel` tabs.

---

## Done

Kept short. Detail lives in the git history, in `reference/index-survey/00-WORKLIST.md` and in
`reference/DOC-CONFLICTS.md`.

- **Every cache index that holds content now has a decoder, an encoder and a whole-index
  byte-identity sweep.** The two exceptions this section used to carry are closed. **Index 9**
  gained `Texture.Encode` and `TextureGraphRecord`, which keeps the per-node version byte, the
  output-size byte, each opcode's raw payload span in stream order, the child run and the 10-byte
  trailer, so a graph re-encodes without re-deriving anything - 915 of 915 groups in the vanilla
  b639 capture and 946 of 946 in the repack (item 7). **Index 15** gained
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
  `RegisterEditorTabs`**: index 14 SFX2 (item 3, `Editor.cs:844`), index 12 client scripts
  (item 4, `:910`) and index 32 loading sprites (item 5, `:897`). The index-32 tab also shipped a
  validating Replace path, which is why item 11 above is down to two questions.
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
  (`Editor.cs:1615-1665`). Image-format import is item 10.
- Three live defects fixed: the map save path writing underwater terrain over the surface square,
  a malformed archive able to kill the process uncatchably, and index 26 discarding every field
  edit in silence.
- The JS5 update server recomputes its master index instead of freezing it at boot, and can
  release its file handles so a cache can be replaced underneath it.
