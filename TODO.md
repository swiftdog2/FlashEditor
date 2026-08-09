# FlashEditor TODO

The running work list. `reference/index-survey/00-WORKLIST.md` was the per-index plan and is now
**largely historical** - see the warning below. This file is the wider product backlog, including
everything that is not a codec.

**Update this at milestones, not every commit.** A finished index, a shipped feature, a
direction change. If it is updated on every turn it becomes noise and stops being read.

**Do not put volatile numbers here.** Counts of our own code go stale by the next commit and get
read as targets. Counts *of the cache* are fine, because the cache does not change - but a count
that differs between the two caches must name which one it belongs to.

**Every claim below was checked against the code on 2026-08-09, at commit `9d883bc`.** The previous
pass was 62 commits earlier at `64005a6`, and **both items that pass left in the queue have since
shipped**: item 13 (the JS5 editor handshake) and item 16 (playing a track on the cache's own
instruments). The queue below is therefore entirely new work, driven by a single observation:

> **Every index that holds content now decodes, encodes and re-encodes byte-identically. The
> correctness work is done. What is not done is that the editor still presents the cache as
> numbers, and a user who does not already know the format cannot build anything with it.**

That is the direction change this revision records. The remaining work is overwhelmingly at the
surface, and the surface has almost nothing to build on: **no icons, no toolbars, and two tooltips
across twenty-five pages.**

---

## Documents that are now stale, and must not be trusted

Found while surveying for this revision. Each was written before the index it describes was built.

- **`reference/index-survey/00-WORKLIST.md` section 1** lists capability per index as of its
  writing - "3 INTERFACE_DEFINITIONS | none (empty TabPage)", "2 CONFIG | partial-read (groups 1,
  4, 34 done)". Every one of those is now false. Its **progress log** at the foot is also stale, and
  stops at five indexes. Sections 4 (cross-cutting work) and 5 (risk list) are still worth reading;
  section 1 is not.
- **`reference/index-survey/index-002-CONFIG.md`** claims 3 of 35 groups decode and "GUI: none".
  It is 35 of 35 with a tab. `reference/index-architect-02.md` supersedes it.
- **`reference/index-survey/index-003-INTERFACE-DEFINITIONS.md`** and
  **`index-012-CLIENT-SCRIPTS.md`** both say "nothing exists". Both indexes are complete with tabs.
  `reference/index-architect-03.md` supersedes the first.
- **`CLAUDE.md` UI conventions** still describes a `TabPage` strip and "three of the tabs predate
  this". The strip is gone - navigation is a category `TreeView` over a `TabControl` that swallows
  `TCM_ADJUSTRECT` (`Editor.cs:353-371`), across 25 pages in six categories. The three-bespoke-arm
  count is still correct: Meta (`Editor.cs:1363`), Sprites (`:1404`), Textures (`:1493`).
- **`STATE_OF_THE_EDITOR.md`** remains historical above its roadmap, as `CLAUDE.md` already says.

**Fixing these is item 27.** They are listed here rather than silently corrected because a reader
who finds a survey document tonight needs to know before they act on it.

---

## Constraints that shape everything below

- **A DWM-composited capture DOES see the OpenGL surface, confirmed 2026-08-09.** This retires a
  standing constraint. `tools/Capture-EditorTab.ps1` still cannot - it uses `CopyFromScreen` and
  `PrintWindow`, both of which return whatever GDI last blitted into that rectangle - but the
  viewport is no longer unobservable. `reference/viewer-eyeball-checklist.md:10` records the working
  route. **Five of nine checklist cases are now confirmed on a monitor** (A, F, H, F3, F4); D is
  open and B, C, E, G, I have not been run.
- **Nothing in the test suite covers WinForms or the renderer.** A layout or render defect passes
  every test. This does not change and it binds every item in the queue below, all of which are UI.
- **Serialise cache-backed test runs.** Parallelise the editing, serialise the sweeping.
- **A green filtered run is not a green suite, and a concurrency defect is invisible to anything
  narrower than the full sweep.** The worked case is `ObjectDefinition.Decode`'s static
  `StringBuilder` (`ObjectDefinition.cs:405-417`), which survived every narrow run and failed only
  under a full sweep. A filtered run is a development aid and never a merge gate; static mutable
  state in a decode path is a defect whether or not anything calls it on two threads today.
- **Run the merge sweep with a logger that names failures**, because `-v:q` reports only counts and
  a transient does not come back on request. Use `--logger "console;verbosity=normal"`, and note it
  prints `Test Run Successful` rather than `Passed!`. **And gate the push on the test command
  itself** - `tail <log> && git push` pushes whenever `tail` succeeds, which is always.
- **Toggling a flag is an edit the byte-identity sweeps cannot see.** They prove an *unedited*
  record re-encodes to what it was read from, which is a different claim from "an edit that nets
  nothing writes nothing". Four real defects lived in that gap. Coverage is now 27 of 27 bare-flag
  properties in `RealCacheBareFlagEditTests`, with `EveryBareOpcodeInTheCacheIsCoveredOrExempt`
  (`:189`) failing when a payload-free opcode the cache carries is neither tested nor exempted.
  **Add the same third check to any new edit path: set it, set it back, land on the original bytes.**
  This binds items 20, 21, 22 and 26 directly - all four open new edit paths.
- **Evidence quality is measured by what a relation rejects, not by what it accepts.** The font join
  scored every relation on how many of 600 *wrong* pairings it admits; the ascent relation lets 325
  through while scoring perfectly on the correct ones. The world map icon join is the cautionary
  case, where a shift sweep over -8..+8 confirmed offset 0 and a sweep over -16..+16 confirmed
  eleven offsets. **When a sweep is your discriminator, widen it until it breaks.**
- **A code comment is prose wherever it states a count, and so is a survey document.** Both are
  written once and never re-measured. The corrections log is `reference/DOC-CONFLICTS.md`; read it
  before trusting a figure from `reference/`.
- **A byte-identity sweep cannot see a normalisation whose triggering input is absent from the
  cache**, and **proves only what its encoder re-derives**. Index 14's packet-length rule and index
  9's replay encoder are the two worked examples. Where an encoder replays stored bytes, name what
  the sweep is then evidence *of*, and name the other test covering the rest.
- **A warning count is only comparable against a build of the same scope.** An incremental build
  does not re-emit the test project's warnings.

---

## In flight

Nothing.

---

## Next

Each item carries the prompt that resumes it, and **the prompt is the whole brief** - paste it and
go. Prompts deliberately do not repeat the standing rules; those live in `CLAUDE.md` and
`AGENTS.md`, and every prompt opens by requiring them.

**Numbers are not reused.** Items 1 to 17 are done and their numbers stay retired, because other
documents cite items by number and a renumber breaks a cross-reference silently.

**Items 18, 19 and 20 are ordered and the order matters.** 18 builds the shared visual machinery,
19 and 20 are its first two consumers, and items 21 to 26 all assume it exists. Doing 20 before 18
means building a tool palette that nothing else can reuse. Items 21 to 25 have no ordering
constraint against each other. **Item 26 is the largest item in this file** and is scheduled last on
purpose, but its first sub-item can start any time because it depends on nothing.

---

### 18. A visual language: icons, value renderers, tooltips

**The foundation, and the highest-leverage change available.** Twenty of twenty-five pages route
their grid through `DefinitionListPanel`, so a column-renderer extension added there lands on almost
the whole application at once. Nothing exists to build on: a repo-wide search finds no `ToolStrip`,
no icon resources, one `ImageList` (which is texture thumbnail data, not icons), and exactly **two**
`ToolTip` instances - one on the GL control, one on a menu item.

```
Read CLAUDE.md, AGENTS.md and the UI conventions section first.

The editor presents this cache as integers. A colour is the hex string "FF3300"; a sprite id, a
model id, a texture id and a font id are all bare numbers; a flag is the word True. There is no
colour swatch renderer anywhere in the project, and pictures are drawn in only four grids in the
whole application. Build the shared machinery that fixes this everywhere at once.

1. AN ICON SET AND A SHARED TOOLBAR CONTROL. Monochrome line icons tinted from the theme, legible
   at 16px, working on both the light and dark backgrounds the app uses. A reusable toolbar/tool
   palette control that takes (icon, tooltip, shortcut, checked-state) and nothing else. Every tool
   palette in the queue below depends on this existing first.

2. COLUMN RENDERERS ON DefinitionListPanel. At minimum: a colour swatch, an image thumbnail
   resolved from an index and an id, and a clickable id. The numeric value stays visible, small and
   secondary - a user who wants the number must still get it. Respect the existing rule that
   ObjectListView hands a null row to an aspect getter during scroll recycling: render an empty
   cell, but keep throwing for a row of the WRONG type, because that can only mean a descriptor
   wired its columns to a different row type than it produces.

3. AN ASSET PICKER DIALOG, reused by every field naming a sprite, model, texture, font or
   animation. Thumbnails, search, and the id shown. This is the component that removes "type a
   number and hope" from the whole application, and it is the prerequisite for items 20 and 21
   being usable rather than merely possible.

4. A TOOLTIP AND INFO-POPOVER LAYER. CLAUDE.md requires the editor to say what it cannot do and to
   mark what an edit will cost. That rule is right and stays. What changes is delivery: today it is
   roughly a dozen permanent paragraph labels docked into pages - Editor.Designer.cs:623 and :794
   (a fifteen-line essay), WorldMapEditorPanel.cs:74, LoadingSpriteEditorPanel.cs:85 and :165,
   ClientScriptEditorPanel.cs:136, Sfx2EditorPanel.cs:55, TrackEditorPanel.cs:122,
   MapEditorPanel.cs:1613. Move that content behind a small (i) affordance beside the control it
   describes. The rule is satisfied and the surface clears.

Do not migrate any tab in this change beyond what is needed to prove each piece works on one real
page. The migrations are items 19 to 26.

Nothing in the suite covers WinForms, so this is verified by eye and by
tools/Capture-EditorTab.ps1 on every tab you touch. Capture before and after. Commit.
```

---

### 19. Cross-navigation: make every id that points somewhere clickable

**There is not one "go to" link anywhere in the application.** A repo-wide search for `GoTo`,
`LinkLabel` or `Hyperlink` finds nothing outside client-script jump arithmetic. The cache is almost
entirely made of ids pointing at other ids, and every one of them is currently a dead end.

**One join already works** and is the model for the rest: `NpcAnimationSet.For`
(`Definitions/Entities/NpcAnimationSet.cs:73-99`) resolves an NPC's opcode-127 render type into
config group 32 and lists the idle, walk, run and turn animations. Copy its shape.

```
Read CLAUDE.md, AGENTS.md and the UI conventions section first. Item 18 must be done.

Every id column in this editor is a dead end. Build navigation on the joins that already decode.
The list, all of them measured and all resolving in this cache:

  map tile underlay/overlay  -> config groups 1 and 4
  floor definition texture   -> index 9
  item opcode 132            -> config group 35 (quests) - decoded both sides, wired to nothing
  object opcode 102          -> config group 34 (map scene icons), 3,267 objects
  object opcode 107          -> config group 36 (world map elements), 170 objects, zero dangling
  object ambientSoundId      -> index 4
  object morph varbit        -> index 22
  any opcode-249 param key   -> config group 11; 12,269 entries over 232 keys, every key live
  interface hook element 0   -> index 12 (see item 26g - this one is the biggest)
  interface sprite/font/model-> indexes 8, 13, 7
  spot anim model/animation  -> indexes 7 and 20
  billboard material         -> index 26, and the reverse: which models attach this billboard
  model particle footer      -> index 27 emitters and effectors
  midi patch key             -> index 14 or index 4, selected by bit 0 of the reference
  loading screen element     -> indexes 32 and 13

Two requirements that decide whether this is useful or annoying:

 - A HOVER PREVIEW comes first. Following a link should be the last resort, not the only way to
   find out what an id is. A thumbnail or a one-line summary on hover answers most questions
   without navigating at all.
 - NAVIGATION MUST BE REVERSIBLE. A back stack, because a user who follows four links to
   understand one record has to get home.

Where a join is one-to-many, show the count before navigating - "used by 3,267 objects" is the
answer to a question the user was about to ask.

Do NOT invent a join that is not in the list above. The world map icon join is the standing lesson:
its first evidence rested on two self-proving rows and a shift sweep too narrow to falsify itself,
and it was wrong. If you want a join that is not listed, prove it the way item 12 and the font join
were proven - by what the relation REJECTS - and say so.

Verified by eye. Capture every tab you touch. Commit.
```

---

### 20. The map tab as a paint program

**The worked example that started this revision.** Asked how to paint all of Varrock with the
TzHaar lava floor, the honest answer is that four of the five steps have no support at all: there is
no way to see what a floor material looks like, no selection of any kind, no brush size, and no fill.
Twelve tools are selected from a **drop-down list**, and beneath it sits **one shared unlabelled
`NumericUpDown`** whose meaning changes between "underlay id", "overlay id" and "object definition
id" depending on the combo above it, with nothing on screen saying which
(`MapEditorPanel.cs:57`, `:58`, `:1299-1303`).

None of this is format work. Every one of those steps writes through edit types that already exist,
already capture what they replaced, and already group into a single undo step.

```
Read CLAUDE.md, AGENTS.md and the UI conventions section first. Item 18 must be done.

Turn the Map tab into a paint program. The tools already work; the way they are reached does not.

1. TOOL PALETTE. Replace the tool ComboBox (MapEditorPanel.cs:57, ToolRows at :167-180) with an
   icon palette from item 18, with tooltips and keyboard shortcuts. Group tools by what they
   operate on: terrain paint, height, flags, objects, inspect.

2. CONTEXT OPTION BAR. Replace the single shared Value box with a per-tool option strip. The brush
   gets a size and a shape; the overlay brush also gets shape and rotation, which today require two
   separate tools to cycle after the fact; the wand gets a tolerance; place-location gets an id
   with the item-18 asset picker behind it.

3. A MATERIALS PALETTE, and this is the piece that makes the tab learnable. A dockable panel
   showing every one of the 159 underlays and 235 overlays as an actual swatch - the definition's
   RGB, or its texture where it has one - with the id small and secondary. Click to load the brush.
   Both counts hold in both caches so they are properties of build 639, but read them from the
   reference table anyway.

4. AN EYEDROPPER. Picks up a tile's underlay, overlay, shape and rotation into the current brush.
   This converts the hardest question in the tab - which number do I want - into one the user
   answers by pointing at a part of the game world they already know. It is the single highest
   value tool in this item.

5. SELECTION AND AREA APPLICATION. Rectangle, freehand, and contiguous-similar (a magic wand over
   the underlay or overlay id). Then make every paint tool apply across the selection. Report in
   the status line how many tiles are selected AND how many map squares that spans, because every
   touched square is rewritten on save and the user has to know they are about to dirty nine of
   them.

Constraints that already exist and must survive:
 - The underlay Value box caps at 174 because a tile encodes it as id + 81 in one byte
   (MaximumValueFor, :192-203). An area fill must not route around that check.
 - Editing is gated on zoom (:392-395). Decide deliberately whether an area operation is too, and
   say which on screen.
 - A height edit writes a VERTEX shared by four tiles. An area height operation must state that,
   and the existing HeightVisibilityWarning (:467-479) must still fire.
 - Undo is per-stroke via CompositeEdit. An area fill is one undo step, not ten thousand.

Then add the edit-path check the constraints section requires: paint a selection, paint it back to
what it was, and land on the original stored bytes. A byte-identity sweep cannot see this and four
real defects have already lived in exactly that gap.

Verified by eye plus tools/Capture-EditorTab.ps1. Run the suite against both caches. Commit.
```

---

### 21. Make index 2 editable, and make it legible

**Index 2 is entirely read-only** - every list column is `DefinitionColumn.ReadOnly`
(`ConfigListDescriptor.cs:59-65`) and both detail grids set `IsEditable = false`
(`ConfigEditorPanel.cs:265`). Nothing in the application writes it. This is the largest functional
gap in the editor, because index 2 owns floors, cursors, hit splats, quests, map icons, identity
kits and every NPC's walk cycle. All sixteen codecs already support edits through `AddedOpcodes`.

```
Read CLAUDE.md, AGENTS.md and the UI conventions section first. Items 18 and 19 must be done.

Index 2 holds 35 record types and the editor cannot write one byte of it. Fix that, and make the
sixteen families that mean something readable by someone who has never seen this cache.

EDITING goes on the field pane, not the grid. ConfigListDescriptor.cs:50-56 already states why:
every grid column summarises several opcodes at once. Work through the field grid.

THE FAMILIES THAT NEED A VISUAL REPRESENTATION, in value order:
 - group 1 and 4, floors. Colour as a swatch and a picker; texture id through the item-18 picker.
   These two feed item 20's materials palette, so do them first and share the renderer.
 - group 33, cursors. A sprite id and a hotspot: draw the sprite with the hotspot marked. It is
   two bytes and completely opaque as numbers.
 - group 34, map scene icons. Sprite thumbnail. Note "no icon" has TWO encodings - the opcode
   absent, and opcode 4 present - and they are not interchangeable on re-encode.
 - group 46, damage marks. Three sprite layers drawn in order, a font, a drift vector and a fade.
   Compose the preview rather than listing nine numbers.
 - group 36, world map elements. Sprite, hover sprite, label, label colour, and a polygon with a
   fill and an edge colour table. Draw the polygon.
 - group 35, quests. Already the most readable family because the summary column shows the name.
   Add the icon and the item-19 link back from item opcode 132.
 - group 32, render animations. Every id in it is an index-20 animation. Preview through the same
   route NpcAnimationSet already uses.
 - group 31, light intensity. Four numbers describing a waveform. An animated preview driven by
   the client's own formula shows in one second what four integers cannot. All four records store
   their opcodes in the order 3, 2, 4, 1.

THE NINETEEN EMPTY FAMILIES stay visible and stay honest. Groups 2, 7, 18, 20-25, 37-45 and 48 are
5,302 files, every one a single 0x00 byte, in both caches, with no client provider. The tab already
labels them "(no provider)" and reports the measured empty count. Keep that. A tab that hides them
is lying about the shape of the index; EmptyConfigDefinition refusing any opcode rather than
guessing is the right posture and the surface should match it.

Opcode order is load-bearing across this index - group 36 has no record in ascending order, group
46 has none, group 35 has 184 of 187 non-ascending, group 32 has 579 records across 58 distinct
orders. The codecs already replay the recorded stream. Do not let an edit path bypass that.

Then the set-and-unset check from the constraints section, on every newly editable field.

Run the suite against both caches. Commit.
```

---

### 22. The index 26 materials editor, and its silent write bug

**Small, self-contained, and currently a data-loss hazard.** Index 26 is decoded on every cache open
and drives the renderer, but **not one of its nineteen columns is displayed or editable anywhere**,
and `MaterialTable.Save` (`MaterialTable.cs:356`) **has no caller in the solution**.

```
Read CLAUDE.md and AGENTS.md first, including the UI conventions section.

Index 26 is the roster of texture slots plus nineteen columns of per-slot render state. It is the
table that says a texture exists, how the renderer treats it, and what flat colour to draw when the
pixels cannot be generated. It is live in the renderer and invisible in the editor.

Build the tab: a nineteen-column grid beside the index-9 thumbnail for the same id.

Three things this has to get right:

 - THE TWO COLUMNS WITH ESTABLISHED MEANINGS get a real presentation. field1831 is the
   representative colour in raw 16-bit RS HSL (TextureDefinition.cs:111-127) - a swatch. field1824
   is the pixel transposition flag the evaluator is driven by (:150-157) - a checkbox. The other
   SEVENTEEN have no established meaning and are named after obfuscated client fields on purpose.
   Do not invent names for them. field1835 is a 4-byte int that is zero in every record and was
   once mistaken for a tint, which scaled every texture toward black (:189-198).

 - SHOW THE SLOTS WITH NO GRAPH. Index 26 and index 9 are 1:1 in the vanilla capture at 915 each,
   which invites the wrong inference that they are the same population. In the repack it is 1408
   against 946, and ids 946..1407 have no procedural content at all - those are exactly the slots
   where the fallback colour is the entire thing the player ever sees. A grid that only shows rows
   with a graph hides the rows that matter most.

 - THE WRITE PATH IS THE POINT. EncodeColumnar returns the raw stored bytes whenever they are
   present, so a field edit is currently discarded in silence. The dirty flag has to land before
   the write path or the first sweep passes while the editor does nothing. Then the set-and-unset
   check from the constraints section.

Run the suite against both caches. Commit.
```

---

### 23. The MIDI patch tab

**The largest "codec finished, no interface" gap in the cache.** Index 15 decodes, re-encodes
byte-identically over all 176 patches in both caches, and is live in the music player through
`MidiSoundBank` - and there is no tab and no panel.

```
Read CLAUDE.md and AGENTS.md first, including the UI conventions section.

Index 15 is the MIDI patch bank: the layer that turns "program 40, key 60" into a sound. Group ids
follow General MIDI - 0..127 are the melodic programs in order, and 128, 129, 136, 144, 152, 153,
168, 176, 178, 184 are the drum kits at their canonical offsets, with 255 and 256..292 as Jagex's
own instruments. The index carries no name hashes, so every label has to come from the General MIDI
program table keyed on group id, which the measured id layout makes safe.

Build it as a 128-KEY PIANO KEYBOARD, not a grid. Click a key: hear it, and see which sample it
maps to, which bank that sample lives in, its tuning, pan, volume, envelope and mute group. The
mute group is worth surfacing plainly - it is how a drum kit chokes an open hi-hat, and it is
invisible as an integer.

Two things the tab must say out loud:

 - Bit 0 of a sample reference selects the bank: index 14 (recorded Vorbis) or index 4 (procedural
   synth). Bit 1 is the sustain flag. The id is v >> 2.
 - INDEX 4 HAS NO RENDERER IN THIS PROJECT, so keys pointing there are silent in the player.
   Measured at 14 of the 21,491 keys, across 10 patches and 6 samples.
   MidiSoundBank.UnrenderedEffectKeys (:18-24) already counts them. Show that count rather than
   dropping the notes quietly. Porting the index-4 synth is a separate item in the backlog.

The semantic accessors are the risk, not the codec. Encode writes the run-length planes back
verbatim, so the byte-identity sweep proves the planes survive and says nothing about how they
expand. The per-key walks were pinned against hand-built plane bytes in item 16; read those tests
before trusting an accessor, and add one if the tab reaches a path they do not cover.

Verified by ear against reference/track-player-listening-checklist.md and by eye. Commit.
```

---

### 24. A generic extract and import tab, and the two indexes that are nothing else

**Nothing in the editor writes an arbitrary payload to disk or reads one back.** Four bespoke paths
exist - sprites, models as OBJ, tracks as MIDI, loading sprites - and no general one. Two whole
indexes have **no UI at all** because of it.

```
Read CLAUDE.md and AGENTS.md first, including the UI conventions section.

Build one generic extract/import surface, then use it for the two indexes that need nothing else.

INDEX 30, NATIVE LIBRARIES. 36 groups in both caches: real compiled DLLs, .so and .dylib files
that the client extracts to disk and loads, which is what makes OpenGL mode work. Six library
families crossed with three operating systems and their architectures. The group name is the whole
structure - "windows/x86/jaggl.dll" - and the file inside is named the empty string. All 36 names
were recovered by brute force and are listed in index-030-NATIVE-LIBRARIES.md:30; commit that table
rather than re-deriving it. Classify each group by OS, architecture and library from the name, and
by format from the payload magic (MZ, Mach-O, ELF).

Two things specific to index 30:
 - It is the ONLY table in the cache that sets the whirlpool flag, so it is the sole real-world
   exercise of that branch of ReferenceTableCodec, and that branch is currently proven by nothing.
   A sweep here that exercises the whirlpool recompute is worth more than the tab.
 - Group 11 is named windows/x64/jagmisc.dll while every other 64-bit Windows library is under
   windows/x86_64/, and the client only ever asks for the latter. The cache wins. Do NOT "fix" the
   name; surface it as the anomaly it is.

INDEX 31, GRAPHICS SHADERS. 2 groups, 14 files, both caches. GPU shader programs, all of them about
water - transparent water, reflections, and the underwater view. Group "gl" is plaintext: five ARB
assembly files and two GLSL. Group "dx" is compiled Direct3D 9 bytecode and can only be replaced.
Give "gl" a text editor and "dx" a hex view.

LINE ENDINGS ARE THE TRAP HERE. Four ARB files use bare LF and no CRLF, transparent_water uses
CRLF, both GLSL files use CRLF, and only one file ends with a newline. Any text control or
File.WriteAllText round trip silently rewrites the file. Prove a no-op edit writes nothing before
you prove an edit writes something.

Both indexes are name-addressed and PER-FILE NAME LOOKUP DOES NOT EXIST. ReferenceTableCodec
decodes and re-encodes per-file identifiers but nothing indexes them, so "gl"/"transparent_water" -
exactly how the client addresses it - cannot be resolved today. Build the per-group identifier
index once; indexes 3, 5, 23, 30, 31, 32 and 33 all want it.

Run the suite against both caches. Commit.
```

---

### 25. A read-only structured export of the whole cache

**Cheap, safe, and it unblocks understanding rather than editing.** Distinct from the parked
working-tree item in the backlog, which is an architecture change - this one writes and never reads.

```
Read CLAUDE.md and AGENTS.md first.

Export every index to structured JSON, one file per record or per group as suits the index, with
every id-to-id reference RESOLVED alongside the raw id. The point is to make the cache queryable
outside the editor: "which floor overlays are red", "which interface components run script 4271",
"which models attach billboard 17", "which objects reference a varbit".

This is READ-ONLY and must never be presented as a round trip. The round-trippable version is a
separate, parked, much larger item, because the dumped form would have to carry every non-canonical
encoding choice the decoders record - opcode order, opcode repetition, aliased values,
absent-versus-default, variable-width integer widths, index 9's raw per-opcode payload spans - and
missing one means an untouched record repacks differently. Say so in the export's own header so
nobody mistakes it for a source of truth.

What to include per record: the decoded fields, the recorded opcode order, and the resolved
references. What to leave binary and reference by path: models, sprites, audio, JPEG payloads,
native libraries, shader bytecode.

Scope the output to the loaded cache and STAMP WHICH CACHE IT WAS, because six indexes differ
between the two and an export with no provenance is a set of numbers nobody can check.

Verified by re-reading the export and comparing a sample of records against a fresh decode. Commit.
```

---

### 26. A real interface editor

**The largest single item in this file**, and now the one with the clearest plan. The prerequisites
are paid: all 42,256 components across 1,078 interfaces re-encode byte-identically, six
non-canonical cases are captured, and most rows carry a verified name rather than a bare hash.

**What the tab is today**: an interface list, a component grid, and a read-only field pane. Four
cells edit - X, Y, Width, Height (`InterfaceComponentListDescriptor.cs:157-164`). There is no
canvas, no rendering of any kind, no tree, no colour picker, no creation or deletion, and no route
from a hook to the script it names.

**Three findings that decide the design**, and each is worth reading before starting:

- **The format carries no per-state appearance.** A component stores one colour, one sprite id, one
  font. Hover, pressed and selected are produced entirely at runtime by CS2 scripts fired from
  twenty hook slots. A canvas rendering the stored record alone shows a bank window with nothing
  selected, no item icons and no counts - and that is what the format is, not a defect.
- **Z-order is not a field.** Draw order is file-id order within a parent. "Send to back" is a
  renumber, which changes every sibling's parent references.
- **No layout resolver exists.** The four sizing and positioning mode bytes decode and nothing turns
  them into a pixel rectangle. Until that exists there is no canvas, no hit testing and no drag.

Broken into eight sub-items with a hard dependency order. **26a depends on nothing and can start at
any time.** 26f is the gate for the behaviour work; 26h is the only one that touches the archive
layer.

| | Sub-item | Depends on |
|---|---|---|
| **26a** | The layout resolver: four mode bytes to a pixel rectangle, ported from the client | nothing |
| **26b** | The component tree from the parent field, in file-id draw order | nothing |
| **26c** | Static rendering of types 0, 3, 4, 5, 6 and 9 onto a canvas | 26a, item 18 |
| **26d** | Direct manipulation: select, move, resize, marquee, snap, nudge | 26c |
| **26e** | In-place text editing and colour pickers on every colour field | 26c, item 18 |
| **26f** | Naming the `if_set*` / `cc_set*` opcode family in the disassembler | nothing |
| **26g** | The behaviour panel: twenty named hook slots, each resolving to its script, with the call-time sentinels decoded | 26f, item 19 |
| **26h** | Component creation, deletion and reordering, with reference fix-up | 26b, 26d |

```
Read CLAUDE.md, AGENTS.md, the UI conventions section, and reference/index-architect-03.md first.
That last document is the authority on index 3; the index-survey document for index 3 is stale and
says nothing exists.

Build sub-item 26a: THE LAYOUT RESOLVER. It is the gate for the whole interface editor and it
depends on nothing else.

Every component carries four mode bytes - WidthMode and HeightMode, XMode and YMode - that decide
how its stored position and size resolve against its parent. They decode today
(InterfaceComponentDefinition.cs:173-183) and nothing computes a rectangle from them. The client's
resolvers are Class253.java:319,333 for sizing and KeyStroke.java:32-38,13-19 for positioning; CS2
clamps sizing modes to 0..4 and positioning modes to 0..5.

Deliver a resolver that takes a component, its parent's resolved rectangle, and returns the
component's rectangle in pixels. Port it from what the client DOES, citing file:line per branch, in
the shape reference/hydra-637-definitions/ uses.

Then defend it with something a byte-identity sweep cannot give you, because this code touches no
bytes. Resolve every one of the 42,256 components against its parent chain and assert the
properties that must hold whatever the modes are: a root's parent chain terminates, no chain is
cyclic, every resolved rectangle is finite, and a child of a type-0 layer resolves inside its
parent's clip rectangle where its modes say it should. Print the distribution of mode combinations
actually used - if a mode value never occurs, say so, because the branch handling it is then
defended by nothing and the next reader needs to know.

Do not build the canvas in this change. 26a lands on its own, with tests, and 26c consumes it.

Run the suite against both caches. Commit.
```

---

## Backlog

Not scheduled. Each is real work with a real reason it is not in the queue.

### Tabs that want a visual representation rather than a grid

Every one of these is a grid of integers describing something inherently visual. Ordered by how
much the picture beats the numbers.

| Index | What it should be | Note |
|---|---|---|
| 9 textures | A node graph canvas | The format is literally a DAG and drawing it as one is the honest representation. Nodes with live thumbnails of their own output, wires for the child edges. The most visually rewarding index in the cache and currently a two-column grid |
| 27 particles | A live preview viewport, gradient pickers for start-to-end colour, sliders for start-to-end size | Every field is a curve or a colour and every one is an integer today |
| 20 animations | A timeline strip, each frame a cell whose width is its duration, scrubbing the viewport | Priority and re-trigger behaviour want named dropdowns, not 0/1/2 |
| 21 spot anims | A live preview of the model running its animation, recolour pairs as two swatches and an arrow | Editable already; the recolour pairs are the most editable thing in the index and they are four hex numbers |
| 33 loading screens | A visual preview | Needs index 32 for sprites and 13 for fonts; both decode |
| 0 and 1 | A per-bone timeline, with the affected triangles highlighted in the viewport | Read-only is correct for these: a frame slot's POSITION is its bone identity, so inserting one re-points every slot after it |
| 24/25 quick chat | A menu tree as a player sees it, and templates with slots as filled placeholder chips | `My Agility level is [skill level]` rather than `My Agility level is <` |

### Missing capability

- **Port the index-4 sound synthesiser.** 10,237 procedural effects that the editor cannot play,
  and the reason 14 keys in the MIDI patch bank go silent. The client's renderer is about 190 lines
  of fixed-point DSP with a biquad cascade. The format is the one canonical format in the cache, so
  there is no encoding-choice work. Blocks a play button on the index-4 tab and completes item 23.
- **Re-bake the world map from index 5.** Index 23 is pre-baked and the client draws it without
  reading index 5 at all, so the two can legitimately disagree - and after any terrain edit they
  will. A re-bake is a genuine content pipeline rather than a viewer, and it is the honest answer to
  "I painted Varrock and the world map still shows grass".
- **Music import.** Index 6 and 11 export MIDI and play. Replacing a track needs the column-major
  packed form rebuilt from an SMF, which is the inverse of a decoder that is lossy by construction -
  many distinct stored delta streams produce byte-identical MIDI. The tab replaces raw bytes today.
- **Per-frame sprite import.** A picture describes one frame, so importing into one of the 44
  multi-frame sets keeps one and discards the rest behind a dialog that says so. The honest fix is
  choosing which frame a picture replaces, and letting a set be assembled from several.
- **Structured control flow in the client-script tab.** The disassembler resolves jump targets and
  marks labels and says in its own header that it is a listing, not a CFG. Basic blocks, loops and
  if/else would make a script readable rather than merely decoded. Item 26f is the naming half and
  is scheduled; this is the structural half and is not.

### An unpacked working tree, packed on deploy - PARKED

**Deliberately not scheduled. Revisit when there is appetite for an architectural change.** Distinct
from item 25, which is a read-only export.

Today we edit the packed cache in place, so every save rewrites the dat2 and the reference table of
every archive packed alongside, and version control sees one enormous binary change. The
alternative is to dump every index to readable files, treat those as the source of truth, and pack
only on deploy.

**The prerequisite is now paid**: a dump-and-repack pipeline is only safe if repacking reproduces
what was dumped, and that is proven for every index that holds content, over both caches. The
hazard is entirely in the non-canonical encodings - opcode order, repetition, aliased values,
absent-versus-default, smart widths, index 9's raw payload spans. Miss one and an untouched record
repacks differently. Every case is already documented per index.

Still to settle: what is readable per index, whether the packed cache is a build artefact or
committed alongside, and how it composes with the JS5 reload handshake.

### Smaller items

- **`AnalyseCache` (`Editor.cs:1969`) is a stub.** It assigns `cacheOut` and never reads it, loads
  `inputCache` inside a `try` and never uses it, and unconditionally returns 0 - so `AnalyseCaches`
  (`:1951`), reachable from the Meta tab's Compare to Output button, always reports no differences
  whatever the two caches hold.
- **`MemoryUtils` (`Utils/MemoryUtils.cs:9`) is dead** - the only occurrence of the name in the
  solution is its own declaration. `RSArchive.Decode` hand-rolls the same idea instead
  (`RSArchive.cs:136-147`: one reused 4 KB buffer, `new byte[chunkSize]` above that), which fires
  roughly 96,000 times in a full sweep and lands the large ones on the LOH. Adopting the pool there
  is the highest-value site, but `ArrayPool.Rent` over-serves and `Return` does not clear, so it
  needs `try`/`finally` and a slice at every use, not a swap. Worth a before-and-after allocation
  measurement, not a tidy-up.
- **`ConfigDefinition` (`Definitions/Config/ConfigDefinition.cs:56`) is a second, hand-rolled
  implementation of the opcode-replay pattern** alongside `OpcodeStreamDefinition`, with its own
  `ConfigOpcode` struct and 14 subclasses. Deliberately not migrated when the shared one landed,
  because its `Encode` is shaped around `WritePayload` and `AddedOpcodes` rather than a pre-built
  record list. Mechanical, but it is a separate change with its own sweep to clear.
- **Three bespoke `LoadEditorTab` arms are left and no more**: Meta (`Editor.cs:1363`), Sprites
  (`:1404`) and Textures (`:1493`). Tracks, Map and Huffman have their own panels deliberately and
  are not migration candidates.
- **Settle what the client's JVM draws for an index-32 image, or record that it cannot be settled.**
  Mostly closed: under JDK 8 all 21 payloads decode and match our reading on 97.72% of pixels,
  never differing by more than 3 levels, while the CMYK reading a marker-less four-component file
  invites sits 53.84 levels away and matches 4 pixels of 1,176,093. Under JDK 11 `Toolkit`
  refuses every payload **and the client's own capability probe**, so the client would fall back to
  index 34. What remains open is only which JVM a given deployment runs.
- **Run the remaining viewer checklist cases.** B, C, E, G and I have never been run, and D is open
  - amber and blue marks appear near the shape but whether they read as `face N` and `vN` with
  numbers in range is unsettled. Now cheaper than it was, because a DWM-composited capture sees the
  GL surface.

---

## 27. Correct the stale documents

Not a queue item with a prompt, because it is a paragraph of work in each file, but it is real and
it has already cost one wasted investigation.

Correct, in this order of harm:

1. `reference/index-survey/00-WORKLIST.md` - mark section 1 historical, or delete it and keep
   sections 4 and 5. Bring the progress log to what the code actually holds.
2. `reference/index-survey/index-002-CONFIG.md`, `index-003-INTERFACE-DEFINITIONS.md` and
   `index-012-CLIENT-SCRIPTS.md` - each carries a "Current capability" section that is now false in
   the strongest way, claiming nothing exists for an index that is complete with a tab. Point each
   at its architect document.
3. `CLAUDE.md` UI conventions - the tab strip is gone, replaced by a category tree over a
   `TabControl` that swallows `TCM_ADJUSTRECT`. Update the description; the three-bespoke-arm count
   is still right.
4. `Sfx2EditorPanel.cs:55-60` says the tab does not play audio because no off-the-shelf decoder
   takes these bytes. A hand-written Vorbis decoder now exists and drives the music player. The
   label may still be a correct scoping decision, but it is no longer justified by capability -
   restate it or act on it.

Each of these is a claim that a reader would act on. `reference/DOC-CONFLICTS.md` exists for exactly
this and each correction belongs in it.

---

## Done

Kept short. Detail lives in the git history, in `reference/index-survey/00-WORKLIST.md` and in
`reference/DOC-CONFLICTS.md`. **Where an item shipped with a gap, the gap is named in the same
bullet** and the actionable ones are also listed under Backlog above.

- **The editor half of the JS5 handshake** (item 13). `Cache/JS5ReloadHandshake.cs` writes
  `reload.request`, waits for `reload.released` with a timeout and a named failure, saves, then
  deletes the request - in that order, because the server holds read handles without
  `FILE_SHARE_DELETE` and the save fails on Windows while it runs. Behind a setting that is off by
  default, driven through `UI/JS5ReloadProgressDialog.cs:63` so the wait does not freeze the window,
  and covered by `JS5ReloadHandshakeTests` and `JS5LiveReloadEndToEndTests`, both gated on a
  declared server.
- **Playing a track the way the client does** (item 16). The index-14 gate is closed with a
  hand-written Vorbis decoder for a setup header carrying no magic, no channel count and no framing
  bit; `MidiSoundBank` wires indexes 15, 14 and 4 the way the client does; `MidiSynthesiser` and
  `WaveOutDevice` play it; the Tracks tab has transport and states what the player does not
  reproduce. Verified by ear against `reference/track-player-listening-checklist.md`.
  *Gap:* index 4 is procedural and has no renderer here, so 14 of the bank's 21,491 keys are
  silent. `MidiSoundBank.UnrenderedEffectKeys` counts them rather than dropping them quietly.
- **The 3D viewer became observable, and five of nine checklist cases passed on a monitor.** A
  DWM-composited capture sees the GL surface; `CopyFromScreen` and `PrintWindow` still do not. Three
  real render defects were found by eye, turned into numbers, fixed against the numbers, and
  confirmed by eye again: a multi-part entity coming apart at the joints, particles driven in
  milliseconds rather than client cycles, and a monochrome type-7 texture blend that made every
  particle an opaque square.
- **Models export and import as Wavefront OBJ** from the Entities page, which is the pragmatic
  answer to mesh editing - edit in Blender.
- **Every cache index that holds content has a decoder, an encoder and a whole-index byte-identity
  sweep.** Indexes 9 and 15 were the last two and both are closed. Indexes 34, 35 and 36 are empty
  in this cache and struck off permanently - and they are three *different* empty states, which
  nothing may conflate.
- **Twenty-five pages across six categories**, reached from a category tree rather than a tab strip,
  with `RegisterEditorTabs` throwing for any page with no registration. Three bespoke
  `LoadEditorTab` arms are left and no more.
- **Interface name recovery** (item 12): 434 verified group names plus 75 bespoke component names,
  every one re-hashed against the loaded cache before it is shown, plus a `com_<fileId>` rule
  resolving 9,249 components in the vanilla capture.
- **Index 12 disassembler** (item 14): 68 opcodes named, every one cited to the client line that
  proves it, nothing taken from RuneStar. *Split off:* basic blocks and structured control flow.
- **Font glyph editor** (item 17), over an index-13 to index-8 join proven by falsification rather
  than by coverage - which is where the rejection-column lesson in Constraints came from.
- **Entities page** (item 9): items, NPCs, objects and models on one page beside one persistent
  viewport. Putting editable flag columns on real records is what exposed the four bare-flag
  defects recorded in Constraints.
- **Sprite tab rebuilt around images** (item 2) and **sprite import from PNG, JPEG and BMP**
  (item 10), warning before it stages whenever the conversion loses something the result cannot
  show. *Gap:* a picture describes one frame, so importing into one of the 44 multi-frame sets
  keeps one.
- **World Map Overview tab** (item 6), **index 32 replacement validation** (item 11), **three new
  tabs for indexes 14, 12 and 32** (items 3, 4, 5), **the index-26 materials census asserted rather
  than printed** (item 1), and **all seven `*_DocumentsKnownDefect` tests fixed** (item 15).
- **The renderer wired into a tab** (item 8), with the render timer gated on the viewport being
  visible and on something needing a frame.
- Shared foundations: `DefinitionSweep`, `DefinitionListPanel`, `CacheAddressing`, `OpcodeStream`,
  table-driven enumeration, the signed-smart writer, `RSCache.ReadGroup`.
- The suite runs against the vanilla b639 capture by default and the repack as a second gate, and
  asserts relationships rather than counts, so it holds on either.
- Whole-world map viewer with hover feedback and a vertex affordance for height edits, categorised
  navigation, and the form's autoscaling corrected at source.
- Three live defects fixed: the map save path writing underwater terrain over the surface square, a
  malformed archive able to kill the process uncatchably, and index 26 discarding every field edit
  in silence.
- The JS5 update server recomputes its master index instead of freezing it at boot, and can release
  its file handles so a cache can be replaced underneath it.
