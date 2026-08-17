# FlashEditor TODO

The product backlog: everything that is not a codec.

**Update this at milestones, not every commit.** A finished index, a shipped feature, a direction
change. Updated every turn it becomes noise and stops being read.

**Do not put volatile numbers here.** Counts of our own code go stale by the next commit and get
read as targets. Counts *of the cache* are fine, because the cache does not change - but a count
that differs between the two caches must name which one it belongs to.

**Nothing that is finished belongs in this file.** Completed work lives in the git history. The
exception is a gap a finished item left behind, which belongs in Backlog as work, not in a
changelog as a footnote.

> **The premise this queue rests on.** Every index that holds content decodes, encodes and
> re-encodes byte-identically. The correctness work is done. What is not done is that the editor
> still presents the cache as integers, and someone who does not already know this format cannot
> build anything with it. Items 18 to 26 are that gap.

Checked against the code on 2026-08-10, part way through the item 18 and 26 work.

**Item 27 is done**, so the four stale survey documents now carry banners saying so. Read
`reference/DOC-CONFLICTS.md` before trusting a figure from `reference/`.

---

## Constraints that shape everything below

Standing rules live in `CLAUDE.md` and `AGENTS.md` and are not repeated here. These are the ones
with a worked case behind them, or that bind a specific item below.

- **Nothing in the test suite covers WinForms or the renderer.** A layout or render defect passes
  every test. Every item in the queue below is UI, so every one of them is verified by eye.
- **A DWM-composited capture DOES see the OpenGL surface, confirmed 2026-08-09**, which retires a
  standing constraint. `tools/Capture-EditorTab.ps1` still cannot - it uses `CopyFromScreen` and
  `PrintWindow`, both of which return whatever GDI last blitted into that rectangle. The working
  route is at `reference/viewer-eyeball-checklist.md:10`.
- **Toggling a flag is an edit the byte-identity sweeps cannot see.** They prove an *unedited*
  record re-encodes to what it was read from, which is a different claim from "an edit that nets
  nothing writes nothing". Four real defects lived in that gap. Coverage is 27 of 27 bare-flag
  properties in `RealCacheBareFlagEditTests`, with `EveryBareOpcodeInTheCacheIsCoveredOrExempt`
  (`:189`) failing when a payload-free opcode the cache carries is neither tested nor exempted.
  **Add the same third check to any new edit path: set it, set it back, land on the original bytes.**
  Binds items 20, 21, 22 and 26 - all four open new edit paths.
- **A byte-identity sweep cannot see a normalisation whose triggering input is absent from the
  cache**, and **proves only what its encoder re-derives**. Index 14's packet-length rule and index
  9's replay encoder are the worked examples. Where an encoder replays stored bytes, name what the
  sweep is then evidence *of*, and name the other test covering the rest. Binds items 22 and 23.
- **Evidence quality is measured by what a relation rejects, not by what it accepts.** The font join
  scored every relation on how many of 600 *wrong* pairings it admits; one that scored perfectly on
  the correct pairings let 325 wrong ones through. The world map icon join is the cautionary case: a
  shift sweep over -8..+8 confirmed one offset, and the same sweep over -16..+16 confirmed eleven.
  **When a sweep is your discriminator, widen it until it breaks.** Binds item 19.
- **A green filtered run is not a green suite.** `ObjectDefinition.Decode`'s static `StringBuilder`
  (`ObjectDefinition.cs:405-417`) survived every narrow run and failed only under a full sweep,
  because xunit parallelises collections. A filtered run is a development aid and never a merge
  gate.
- **Run the merge sweep with a logger that names failures**, because `-v:q` reports only counts and
  a transient does not come back on request. Use `--logger "console;verbosity=normal"`, which prints
  `Test Run Successful` rather than `Passed!`. **And gate the push on the test command itself** -
  `tail <log> && git push` pushes whenever `tail` succeeds, which is always.
- **Serialise cache-backed test runs.** Parallelise the editing, serialise the sweeping.

---

## In flight

**Item 18 is done. Item 26 is done bar 26h's write path. Item 19 is done bar two call sites
named in its own section. Items 20 and 27 are under way.** What landed, and what each left behind:

| | State | What is left |
|---|---|---|
| **18.1** icons and toolbar | Done. `EditorTheme`, `EditorSurface`, `EditorIcon`, `EditorIcons` (33 GDI-drawn icons), `EditorToolStrip` | Nothing. Icons are judged on a contact sheet; four shipped broken in the first pass and the sheet is the only reason they did not stay broken |
| **18.2** column renderers | Done. `DefinitionCellVisual`, `DefinitionCellRenderer`, three factories on `DefinitionColumn`, proved on the Config tab's floor families | Adopt them on the other pages. Zero descriptors changed, so every adoption is additive |
| **18.3** asset picker | Done. `AssetPickerDialog` over sprites, models, textures, fonts and animations, virtualised to the visible rows | **Has its first caller**: item 20's place-location tool, which also added an `Object` kind. That kind is the one whose ids are not group ids - index 16 holds definitions as files and a location names `group << 8 \| file` - so it enumerates files. 21 and 26e are the remaining consumers |
| **18.4** info affordance | Done. `InfoAffordance`, the two interface toolbars, the floor palette, and every docked paragraph migrated | Nothing but the eyeball pass, which needs a capture of the Sprites, Fonts, Tracks, SFX2, Client Scripts, Loading Sprites, World Map, Particles and Entities pages. The prompt's site list is wrong in **four** places, not three - see `reference/DOC-CONFLICTS.md`. Two wrap-on-resize helpers went with the paragraphs and the rest lost entries |
| **26a** layout resolver | Done, with 23 unit tests and 4 cache-backed property sweeps, green on both caches | Nothing |
| **26b** component tree | Done, tree view wired into the tab with two-way selection | Nothing |
| **26c** canvas | Done for types 0, 3, 4, 5, 6 and 9, clipped as the client clips, text in the cache's own glyphs | Models are marked, not drawn - the only route to model pixels is OpenGL. Text breaks only on `
`; the client's wrap rule is unsettled |
| **26f** opcode naming | Done. 126 newly named, 71 -> 197 | ~70 deliberately left numbered because the dispatcher does not settle them |

**Corrections to this file's own item 18, found by doing it.** All three would have mis-scoped the
work, and they are logged in `reference/DOC-CONFLICTS.md`:

- "exactly **two** `ToolTip` instances" - there is **one**, attached to two controls, one of which
  the list missed. The "menu item" is a `ToolTipText` property, a different mechanism. It matters
  because that instance carries a 30-second `AutoPopDelay`, right for a paragraph and wrong for a
  toolbar, so inheriting it is a trap.
- "no icon resources" - `Flash.ico` exists, as `<ApplicationIcon>` and in `Editor.resx`. It is the
  app icon, so the spirit holds, but the resx path is exercised and a reader would conclude it is not.
- **`MapEditorPanel.cs:1613` is not a docked label and must be struck from 18.4.** It is
  `sb.AppendLine` feeding the read-only inspector `TextBox`, rewritten on every mouse move. Moving
  it behind an (i) would delete a feature. Four other real labels are missing from the list, so the
  count survives by coincidence: `ParticlePreviewPanel.notice`, `FontEditorPanel.glyphNote` and
  `.previewNote`, and `Editor.Designer.cs`'s `ViewerLimitsLabel`. A fifth citation is short rather
  than absent - `ClientScriptEditorPanel` docks **two** where the list names one line.
- **The test that separates the two families is whether anything reassigns the text.** A paragraph
  no code path rewrites is a candidate; one rewritten on selection is a status line wearing a
  paragraph's clothes, and moving it behind an (i) would delete a feature the same way `:1613`
  would. Every `header`, every `status`, `ConfigEditorPanel.notes`, `FontEditorPanel.kerningNote`,
  `EntityBrowserPanel.noticeLabel` and both `previewNote`s in the LoadingSprites and WorldMap tabs
  fail it and stay labels.

**And one about the whole application, which changes what any of this can assume:** the process is
pinned **DPI-unaware** (`FlashEditorForm.cs:46`, an OpenTK crash fix), so `AutoScaleMode.Dpi`
computes exactly 1.0 everywhere and nothing scales. 16 logical pixels is 16 physical pixels. Drawing
an icon as a vector buys recolourability, **not** sharpness - Windows bitmap-stretches the whole
window after paint, so a vector and a raster blur identically.

---

## Next

Each item carries the prompt that resumes it, and **the prompt is the whole brief** - paste it and
go. Prompts deliberately do not repeat the standing rules; every prompt opens by requiring them.

**Numbers are not reused. Items 1 to 17 are complete and their numbers stay retired**, because other
documents cite items by number and a renumber breaks a cross-reference silently.

**Items 18, 19 and 20 are ordered and the order matters.** 18 builds the shared visual machinery, 19
and 20 are its first two consumers, and 21 to 26 all assume it exists. Doing 20 before 18 means
building a tool palette nothing else can reuse. Items 21 to 25 have no ordering constraint against
each other. **Item 26 is the largest item in this file** and is scheduled last on purpose, but its
first sub-item depends on nothing and can start at any time.

---

### 18. A visual language: icons, value renderers, tooltips

**The foundation, and the highest-leverage change available.** Twenty of twenty-five pages route
their grid through `DefinitionListPanel`, so a column-renderer extension added there lands on almost
the whole application at once. Nothing exists to build on: a repo-wide search finds no `ToolStrip`,
no icon resources, one `ImageList` (texture thumbnail data, not icons), and exactly **two**
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

**Two of the fifteen joins are left, and both are call sites rather than machinery.** The routing,
the preview, the counts and the picker all landed; the prompt below is kept whole because its
join list is still the ceiling, and because the two rules under it bind anything added later.

What is on disk now, so none of it is rebuilt:

- **Resolution is the export's, not a second copy.** `CacheReferenceResolver` answers what an id
  addresses and whether the target's reference table declares it, and `CacheExportJoins.Extract` is
  the single statement of which relations are measured. `CacheReferencePreview` (`FlashEditor/UI/`)
  wraps both for the UI and adapts the two row types the export builds rather than decodes - a
  model's footer and a MIDI patch's key census.
- **A place in the cache is an index, a record and sometimes a group.** Indexes 2 and 27 are
  collections of unrelated families with no id arithmetic, so an id there is not a place: quest 12
  and map scene icon 12 are different records, and so are emitter 40 and effector 40. That is why
  `EditorLocation` and `DefinitionCellVisual` carry a group, why `DefinitionColumn.ConfigLink`
  exists beside `Link`, and why `ConfigEditorPanel.Show` and `ParticleEditorPanel.Show` take one.
  `CacheReferencePreview.GroupIsPartOfTheAddress` names the two indexes once.
- **`Editor.TabFor` resolves an index to a tab in two passes**, primary registration first and the
  indexes a tab merely lists second. Indexes 7, 16 and 18 route nowhere on their own - the Entities
  page is registered against 19 - so `EntityBrowserPanel.Show` picks the family once the page is
  reached.
- **The hover preview, the one-to-many count and the reference menu are all on
  `DefinitionListPanel`**, so every tab that uses the shared grid has them without wiring. The count
  is measured over the loaded rows through the column's own visual delegate; nothing writes one
  down. The menu carries the references no cell can - a quest array, a parameter dictionary, a
  model footer, eight arrays of hook operands.
- **`AssetPickerDialog` has its caller**: an editable link into a sprite, model, texture, font or
  animation opens it instead of a text box. An editable link follows on Ctrl+click rather than a
  plain one, because the first click of the double click that starts an edit would otherwise
  navigate away before the second landed.

What is left:

- **The map tile underlay and overlay join has no call site.** `Editor.GoToCacheRecord(indexId,
  recordId, groupId)` is the entry point for it and the Map tab is the only caller it needs, but
  the Map tab is a bespoke control with no cell to click, so somebody has to decide where on it a
  floor id becomes clickable. One line, once that is decided.
- **The reverse of the billboard join - which models attach this billboard - is not wired**, and
  should not be wired the obvious way. It needs every model footer in index 7 decoded, which is
  exactly what `ModelListDescriptor.ReadsPayload = false` exists to avoid; the forward direction
  (a model's row lists its bonds) is on the reference menu and costs one model decode. If the
  reverse is wanted, it wants a built-once index with a progress report, not a hover.
- **Nothing here has been seen on screen.** It was written alongside two other agents, so no tab
  was captured and no cache-backed test was run. `RealCacheLinkColumnTests` is written and unrun.

**One join already works** and is the model for a one-to-many join in code rather than in the UI:
`NpcAnimationSet.For` (`Definitions/Entities/NpcAnimationSet.cs:73-99`) resolves an NPC's
opcode-127 render type into config group 32 and lists the idle, walk, run and turn animations.

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
and it was wrong. If you want a join that is not listed, prove it by what the relation REJECTS, and
say so.

Verified by eye. Capture every tab you touch. Commit.
```

**A link column is a second surface for a join, and that is where an unmeasured one gets in.** It
looks like a display change and is a claim about the format. `RealCacheLinkColumnTests` is the
check: over every declared record of indexes 16, 21 and 29 it asserts that each link a column draws
is a triple `CacheExportJoins` already produces for the same record. Add a case to it for any index
that grows one. It deliberately does not assert that every link resolves - some ids in this cache do
dangle, and "resolved or dangling" is an `or` that a cache whose links had all stopped resolving
would pass unchanged.

---

### 20. The map tab as a paint program

**All five sub-items are built.** What is left is the eyeball pass and one merged-tree run of the
suite against both caches - `RealCacheAreaFillEditTests` in particular has never been run, having
been written in a worktree while other agents held the same `main_file_cache.dat2`.

What the second half added, and where it went:

- **The tool palette is an `EditorToolStrip` along the top of the canvas, not in the left column.**
  The palette alone would have fitted the column, being a swap for the combo; the option bar is
  genuinely new and would not. Both want width, which the canvas edge has.
- **`MapToolOptionsBar` gives each id its own labelled box** - "Underlay id", "Overlay id", "Object
  id" - as separate controls rather than one relabelled one, so switching tools cannot carry an
  overlay id into an object field. Overlay shape and rotation are brush settings now; the cycle
  tools stay for adjusting what is already on a tile.
- **`MapSelection`, `MapBrush`, `MapWand` and `MapAreaEdits` are free of WinForms and of the cache**,
  because everything they decide ends up written to a map square and nothing in the suite covers a
  window. The underlay cap is stated once in `MapToolLimits` and enforced at both the option bar and
  the fill.
- **Selecting is zoom-gated exactly as editing is**, and says so in the selection tools' own note
  rather than only refusing: below 2 px/tile a tile is sub-pixel, and every square a selection
  touches is decoded and pinned for as long as the edit is undoable.
- **Two silent-warning defects found on the way.** `HeightVisibilityWarning` tested the edit for
  `SetHeightEdit`, which a fill of ten thousand height edits is not; and `FlashFor` bails on
  anything that is not an `IMapEditArea`, which a `CompositeEdit` can never be, so undoing a fill
  would have reverted ten thousand tiles in silence. Both handle a group now.
- **The height path is one-way and is pinned as a known defect.**
  `Region.SetTileHeight` latches `heightExplicit` and `heightEdited` and nothing clears them, so a
  raise-then-restore loses a tile that stored no height (one byte becomes two) and loses the alias,
  since stored bytes 0 and 1 both decode to zero and the shipped files use both. Underlay, overlay
  and flag fills do land on the original bytes.

Three things the first half established that the rest did not have to rediscover:

- **The left column cannot take another group.** The palette went there first and `AutoScroll` made
  `TableLayoutPanel` squeeze the Percent rows to nothing so it never appeared; a minimum height
  fixed that and clipped the layer list instead. It lives along the bottom now, beside the
  inspector. **A tool palette replacing the combo is a swap, not an addition, so it is safe** - but
  anything genuinely new needs a home, not a row.
- **The underlay cap belongs at the point of picking.** A tile stores an underlay as `id + 81` in
  one byte, so 174 is the highest that survives, and the palette shows every record the table
  declares. Picking past the cap refuses out loud. An area fill must do the same rather than
  clamping, for the same reason.
- **A tool that reads is not a tool that writes.** The eyedropper runs before `BuildEdit` and never
  reaches it, and `MapFlashKind.Sampled` exists so a read does not flash the colour that means "the
  square was written to". Any inspect-like tool added later wants the same treatment.

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
what it was, and land on the original stored bytes.

Verified by eye plus tools/Capture-EditorTab.ps1. Run the suite against both caches. Commit.
```

**What the prompt above no longer covers.** Everything in it is built; the two items outstanding
are a capture of the Map tab and one merged-tree suite run against both caches. The map suites
sweep every square on every run, so `FLASHEDITOR_TEST_CACHE_FULL=1` buys nothing extra here.

---

### 21. Make index 2 editable, and make it legible - DONE

**Built.** The write path is the descriptor's: `ConfigListDescriptor.IsEditable` follows the
family's own `CanEncode` and `Encode` goes to the record class's `Encode`, which replays the stored
opcode order and repetition. Editing is on `ConfigEditorPanel`'s field pane, one field per line,
because every grid column summarises several opcodes at once. `ConfigPreviewPanel` draws the eight
families that need a picture, reusing `FloorMaterialPalette`, `SpriteThumbnailRenderer` and
`NpcAnimationSet` rather than drawing any of them a second time. The nineteen provider-less groups
stay listed and stay labelled.

Two things it found rather than added:

- **A payload-free opcode cannot be written back by re-deriving a payload.** Thirteen boolean
  fields across ten codecs looked editable and silently did nothing the moment the pane became
  editable. They now suppress the opcode rather than removing it, so turning one back on puts it
  where the file had it - the same rule `SuppressedOpcodes` states for the three codecs that
  predate `ConfigDefinition`.
- **Group 34's "no icon" has two live encodings** and assigning the property alone changed the
  field and nothing else. `MapSceneIconDefinition.SetSpriteGroupId` swaps the opcode in place.

What is left, as work rather than as a footnote:

- **The array fields are read only.** A parameter block, a polygon, a recolour table and a quest's
  requirement lists have no single value a text box can hold, so they are shown and not edited.
  Group 26 is nothing but a parameter block, so that family is effectively still read only.
- **"Make this field absent" is not expressible** for a field with a payload. An opcode the record
  does not carry is appended only when the value differs from the record class's constructor
  default, and on this index absent and default are frequently different bytes. The one field where
  that bites in shipped data is a floor overlay's primary colour, which two of the 235 records do
  not store; it is named and excluded in `RealCacheConfigFieldEditTests.IsExempt`.
- **The light curve refuses waveform 3.** It indexes a 2,048-entry noise table the client builds at
  startup from a seeded fractal generator (`Class358.java:17`), and one of the four records uses it.
  Porting `Node_Sub10_Sub35` would draw it.

The prompt below is kept because it is the statement of what the tab is for.

```
Read CLAUDE.md, AGENTS.md and the UI conventions section first. Items 18 and 19 must be done.

Index 2 holds 35 record types and the editor cannot write one byte of it. Fix that, and make the
sixteen families that mean something readable by someone who has never seen this cache.

EDITING goes on the field pane, not the grid. ConfigListDescriptor.cs:50-56 already states why:
every grid column summarises several opcodes at once.

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

 - EIGHTEEN OF THE NINETEEN COLUMNS ARE SETTLED, each from what the 637 client does with it and
   each citing the line that settles it - reference/hydra-637-definitions/material-columns.md, with
   reference/index-survey/index-026-MATERIALS-column-census.md as the measured companion. The names
   live on TextureDefinition and MaterialColumn, and every grid heading carries the client field in
   brackets so a name can be checked without leaving the tab. TWO of the eighteen get a real
   presentation rather than a number: representativeHsl (aShort1831) is the raw 16-bit RS HSL and
   gets a swatch, and transposePixels (aBoolean1824) is the flag the evaluator is driven by and
   gets a checkbox.

   FIELD1827 KEEPS ITS OBFUSCATED NAME and no name may be invented for it. Class260.java:166
   assigns it and only oa.java:160 and oa.java:880 read it, both native method argument lists, so
   there is nothing to name it after. A plausible name here would be read as settled, and one
   already was: waterParams (anInt1835) was taken for a tint and multiplied into the generated
   pixels, which scaled every texture toward black. It is packed water-shader parameters, read by
   effect programs 2, 8 and 9, and zero in every record of both caches for reasons nobody knows -
   "the water effects are unused" is refuted, because the one slot in either cache on program 8
   stores zero too.

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

### 23. The MIDI patch tab - DONE, bar a listening pass

**Built.** `MidiPatchEditorPanel` is registered against index 15 under Media: a
`DefinitionListPanel` of the 176 patches over `MidiKeyboardControl`, which draws the selected patch
as 128 keys and plays one through `TrackPlayback`. The prompt below is kept because it is the
statement of what the tab is for.

**What is not done, and what it would take.** Nobody has heard it. The audio path is asserted only
as far as "the right patch is selected and the right sample named" - `MidiKeyPreviewTests` reads the
patch id back off the production synthesiser, and `RealCacheMidiPatchTabTests` checks that over
every declared patch - and no test in this suite renders a sample to a device. The remaining work is
one pass of `reference/track-player-listening-checklist.md` against a handful of keys, by ear.

**One correction to the prompt below.** It says the ten drum-kit ids sit "at their canonical
offsets", which is nearly true and is written up in `reference/DOC-CONFLICTS.md`: GS would put a
Jazz kit at id 160, this cache has no 160, and it does have a 178 that no published kit table names.
`GeneralMidi` labels that one by its bank and program rather than inventing a name for it.

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
   dropping the notes quietly. Porting the index-4 synth is a separate Backlog item.

The semantic accessors are the risk, not the codec. Encode writes the run-length planes back
verbatim, so the byte-identity sweep proves the planes survive and says nothing about how they
expand. The per-key walks are pinned against hand-built plane bytes in RealCacheMidiPatchTests;
read those before trusting an accessor, and add one if the tab reaches a path they do not cover.

Verified by ear against reference/track-player-listening-checklist.md and by eye. Commit.
```

---

### 24. A generic extract and import surface, and the two indexes that were nothing else - DONE

Landed 2026-08-16. `CachePayloadTransfer` and `CachePayloadTransferStrip`
(`FlashEditor/Definitions/Editing/`) do raw bytes in both directions for any index;
`CacheNameIndex` (`FlashEditor/Cache/CacheNameIndex.cs`) resolves group **and file** names off one
reference table; indexes 30 and 31 have tabs.

**What it left behind, as work rather than as a changelog footnote:**

- **Five indexes have not adopted the name index.** 3, 5, 23, 32 and 33 all carry identifiers and
  still address by id. Index 23 is the one that cannot be read correctly without it - its `area`
  file is id 4 in 32 groups and id 0 in the other 7 - and it is also the cheapest available proof
  that the hash is over the **lowercased** name, which nothing pins yet.
- **Four indexes have not adopted the transfer surface.** 6, 11, 14 and 32 all want export, and
  three of them already have a bespoke path that writes a *rendering* rather than the stored bytes.
- **The two write suites each copy the whole cache.** `RealCacheWhirlpoolWriteTests` and
  `RealCacheShaderEditTests` follow `MapSaveRoundTripTests` in copying every `main_file_cache.*`
  to a temp directory per test, which is now four whole-cache copies in a full run. The lighter
  pattern exists - `NpcDefinitionWritePathTests` seeds real bytes into a synthetic cache - but it
  does not exercise the real reference table, which is the point on index 30.

---

### 25. A read-only structured export of the whole cache - DONE

`FlashEditor/Export/`, reachable from **Cache - Export to JSON**. Still distinct from the parked
working-tree item in Backlog: that one reads its output back, this one never does, and the
paragraph parking it is the reason this one is read only.

**The three decisions worth knowing before changing it:**

- **It reuses the editor's own `IDefinitionListDescriptor`s** rather than restating each index's
  addressing and decode. `CacheExportPlan.DescriptorsFor` is the table. An index the editor can
  show is an index this can export, and the two cannot drift; an index gains an export by gaining
  a descriptor. Index 7's descriptor reads no payload, so models are decoded by the exporter
  itself and reduced to their footer references - geometry is never written.
- **`CacheExportJoins.Resolved` is a ceiling, not a starting point.** It is item 19's measured
  list and nothing else. `NotResolved` states, in the export's own header, which measured joins it
  does not do and why, so an absence reads as a decision rather than an oversight.
- **Provenance comes from `CacheProvenance`**, which fingerprints the same way `RealCacheProfile`
  does - declared counts on indexes 3, 9 and 19, never a directory name and never a table version.
  It is a separate type because the test profile also carries every figure the suite asserts, and
  because the test project references the production project rather than the reverse.

**What is written as a manifest and not decoded**, each with its reason in the file itself:
indexes 5, 6, 8, 10, 11, 14, 30 and 31. Every one bar 5 is an asset - audio, pixels, native code,
compiled shaders - where JSON around the bytes answers no query. Index 5 is the one judgement
call: the group list, its map-square names recovered by hashing the whole coordinate name space,
and its per-group key status are written, but the 64x64 terrain tiles and the location placements
are not, because that is millions of records serving a view the map tab already gives.

**Left to do, in the order it is worth doing:**

- **Index 5's locations.** "Which map squares place object X" is a real question and the export
  cannot answer it. It needs `MapSquareLoader`, and it needs a decision about the terrain side,
  which is 16,384 tiles a square. Doing terrain would also bring the one measured join the export
  currently declines - map tile underlay and overlay into config groups 1 and 4.
- **Index 10's Huffman table and index 8's sprite geometry** are both decoded already and are
  cheap; they are manifests only because a code table and a pixel plane were judged not worth a
  record each. Reconsider if either turns out to be queried.
- **`ConfigFamily.Sprite`** is a decoded relation on both sides that item 19 does not list, so the
  export does not resolve it. Promote it into item 19 first if it is wanted.

**Verified by** `FlashEditor.Tests/Cache/RealCache/RealCacheExportTests.cs`, which re-reads the
written JSON and compares it against a fresh decode from the cache rather than against itself -
necessary, because the record writer is reflective and states nothing twice. Plus
`FlashEditor.Tests/Export/CacheExportUnitTests.cs`, which needs no cache.

---

### 26. A real interface editor

**The largest single item in this file**, and now the one with the clearest plan. The prerequisites
are paid: all 42,256 components across 1,078 interfaces re-encode byte-identically, six
non-canonical cases are captured, and most rows carry a name verified by re-hashing it against the
loaded cache rather than a bare hash.

**What the tab is today**: an interface list, a component grid, and a read-only field pane. Four
cells edit - X, Y, Width, Height (`InterfaceComponentListDescriptor.cs:157-164`). There is no
canvas, no rendering of any kind, no tree, no colour picker, no creation or deletion, and no route
from a hook to the script it names.

**Three findings that decide the design**, each worth reading before starting:

- **The format carries no per-state appearance.** A component stores one colour, one sprite id, one
  font. Hover, pressed and selected are produced entirely at runtime by CS2 scripts fired from
  twenty hook slots. A canvas rendering the stored record alone shows a bank window with nothing
  selected, no item icons and no counts - and that is what the format is, not a defect.
- **Z-order is not a field.** Draw order is file-id order within a parent. "Send to back" is a
  renumber, which changes every sibling's parent references.
- **No layout resolver exists.** The four sizing and positioning mode bytes decode and nothing turns
  them into a pixel rectangle. Until that exists there is no canvas, no hit testing and no drag.

Eight sub-items with a hard dependency order. **26a and 26b depend on nothing.** 26f gates the
behaviour work; 26h is the only one that touches the archive layer.

| | Sub-item | Depends on | State |
|---|---|---|---|
| **26a** | The layout resolver: four mode bytes to a pixel rectangle, ported from the client | nothing | **done** |
| **26b** | The component tree from the parent field, in file-id draw order | nothing | **done** |
| **26c** | Static rendering of types 0, 3, 4, 5, 6 and 9 onto a canvas | 26a, item 18 | **done** |
| **26d** | Direct manipulation: select, move, resize, marquee, snap, nudge | 26c | **done.** The marquee rule is containment, not intersection, because almost every interface here is one root layer covering the canvas and an intersection rule would collapse every band to "the whole interface". `InterfaceHitTest` and `InterfaceSnap` hold the parts that can be tested without WinForms |
| **26e** | In-place text editing and colour pickers on every colour field | 26c, item 18 | **done.** Every colour the format carries is swatched: the shared one on types 3, 4, 5 and 9, and the type-5 outline. Adding the second one forced `DefinitionCellActivatedEventArgs` to carry its column - with two swatches on a row the old handler wrote the wrong field |
| **26f** | Naming the `if_set*` / `cc_set*` opcode family in the disassembler | nothing | **done** |
| **26g** | The behaviour panel: twenty named hook slots, each resolving to its script, with the call-time sentinels decoded | 26f, item 19 | **done.** `InterfaceHookPanel` over `InterfaceHookRow`, sharing the bottom-right pane with the field grid, which no longer lists hooks at all. Its Script column is a `Link` and it raises the same `DefinitionCellActivatedEventArgs` a definition list raises, so the form's existing `OnCellActivated` takes it and there is one call site - `Editor.cs`, beside the Go menu. What that link *does* is still item 19's |
| **26h** | Component creation, deletion and reordering, with reference fix-up | 26b, 26d | **model done, not wired.** `InterfaceComponentEdits` plans the renumbering and the parent fix-up, tested on the invariant. **Applying one needs a group-level write the cache does not have** - see below |

**Three findings from 26a that bind everything after it.** Each was caught by review before any
code was written, and each would otherwise have become a test asserting a defect:

- **`(-25945 * 765) >> 14` is -1212, not -1211.** `-1211` is what a truncating `/ 16384` produces.
  The design document demanded -1211, so a test written from it would have passed against the wrong
  implementation and pushed whoever fixed it to replace the shift with a division. 117 components in
  this cache have a negative base position on a shift-mode axis, so this is live arithmetic.
- **A component can be its own parent.** Group 468 file 1 is, byte-identically in both caches.
  Anything walking the parent structure must be cycle-proof by construction; there is deliberately
  no depth cap, because the format permits a 770-level chain in the 771-file group this cache holds.
- **Sizing modes 3 and 4 occur zero times in either cache**, on both axes, while positioning mode 5
  occurs 57 times on x and 56 on y. So the aspect-ratio cross-links are defended only by hand-written
  unit tests, and the positioning catch-all arm is load-bearing rather than defensive.

**And one about the clip rule, which no test that does not draw can see:** a type-9 line extends its
clip one pixel right and down, because its endpoint is inclusive. Both caches hold 367 lines and the
omission clips the last pixel off every one.

**26g's hard half is already done, and whoever picks it up should start from it rather than the
client.** 26f produced the full stored-slot mapping in `ClientScriptOpcodes.AddComponentHooks`: each
of the twenty wire slots against the CS2 opcode that writes it and the client field it lands in,
checkable against `RSInterface.unpackConfig`'s read order at `RSInterface.java:1308-1340`. Three
things it settled that the panel has to respect:

- **Slot 0 has no setter at all.** It is the hook the client fires itself over every component as an
  interface opens (`Class247.java:4130-4136`). A panel listing "the opcode that sets this" must say
  "none, the client fires it" rather than leaving the cell blank.
- **Slots 5, 6, 7, 18 and 19 pair with the five trigger arrays**, and their CS2 setters assign the
  hook and its triggers in one statement. Showing a hook without its triggers shows half the record.
- **Ten CS2 opcodes, 1418 to 1427, set hook arrays that are not in the wire format at all.** Do not
  go looking for them in the bytes, and do not give them a row.

**Do not name the twenty slots after events.** 26f deliberately refused to: which event fires which
array is decided outside the dispatcher, so `on-click` would be invented rather than derived. Name
the storage and the setter, which are both checkable.

**Anything that edits geometry must invert the mode, not add a delta**, and must use
`InterfaceLayoutResolver.ParentExtentsFor` to get the extents to invert against. Both are already
built and both have a test that fails loudly if they are bypassed.

**26h needs one thing this project does not have: a way to change which files a group contains.**
`RSCache` exposes `WriteFile` and nothing else - no delete, no group rewrite - so a renumbering
cannot be committed however correct the plan is. That is the whole remaining cost of 26h and it is
archive-layer work, not interface work:

- Writing a group whose **file set** differs from the stored one, which changes the reference
  table's per-file id list and file count, not just a payload.
- The client reads a group's file count as `maxFileId + 1` and throws the explicit id list away
  when the two agree (`VersionTable.java:183,185`), so the numbering must stay dense - which is
  what forces the renumbering in the first place.
- **The invariant that a save changing nothing writes nothing has to survive it.** Re-encoding
  rewrites the archive CRC and drags in the reference-table entry of every archive packed
  alongside, so a no-op structural edit must be detected before the group is rewritten, not after.

**And one thing no amount of care inside this project can fix:** a component is addressed from
outside its interface as `(interface << 16) | file`, by CS2 scripts in index 12 and by hook
arguments in other interfaces. Renumbering repoints all of those at a different component. The
planner reports the references it can see and says plainly that the rest exist; finding the CS2
ones means scanning 4,149 compiled scripts for a constant, which is its own item and would make
26h materially safer.

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

### 27. Correct the stale reference documents - DONE

**All six rows are done.** Kept rather than deleted because the table records what each document
claimed and what was true, which is the evidence for `reference/DOC-CONFLICTS.md`'s entries.

**No prompt, because it was a paragraph of work per file - but it had already cost one wasted
investigation.** Each was written before
the index it describes was built, and each now claims nothing exists for an index that is complete
with a tab. A survey document is prose: written once, never re-measured.

**Four of the six rows below are done.** The two that remain are the last two.

| File | What it claims | What is true |
|---|---|---|
| ~~`index-survey/00-WORKLIST.md` §1~~ | ~~Per-index capability~~ | **Done.** Section 1 banner-marked historical; 0, 2, 4 and 5 declared still live |
| ~~`index-survey/index-002-CONFIG.md`~~ | ~~3 of 35 groups decode~~ | **Done.** Banner-marked superseded by `index-architect-02.md` |
| ~~`index-survey/index-003-INTERFACE-DEFINITIONS.md`~~ | ~~"nothing exists"~~ | **Done.** Banner-marked superseded by `index-architect-03.md` |
| ~~`index-survey/index-012-CLIENT-SCRIPTS.md`~~ | ~~"nothing exists"~~ | **Done.** Capability grading corrected |
| ~~`CLAUDE.md` UI conventions~~ | ~~A `TabPage` strip~~ | **Done**, and the count was wrong in this row too: the deck holds **27** pages and the tree exposes **25**. A page count is two numbers and neither document said which it meant |
| ~~`Sfx2EditorPanel.cs:55-60`~~ | ~~The tab cannot play audio~~ | **Done, by acting on it rather than restating it.** The tab plays effects now. The note says what playback does and does not do - chiefly that looping is not applied, so an effect sounds shorter here than in game. **Not yet verified by ear**: `reference/track-player-listening-checklist.md` is the shape that check should take |

`STATE_OF_THE_EDITOR.md` remains historical above its roadmap, which `CLAUDE.md` already says.
Each correction belongs in `reference/DOC-CONFLICTS.md`, which exists for exactly this.

---

## Backlog

Not scheduled. Each is real work with a real reason it is not in the queue.

### Tabs that want a visual representation rather than a grid

Every one is a grid of integers describing something inherently visual. Ordered by how much the
picture beats the numbers.

| Index | What it should be | Note |
|---|---|---|
| 9 textures | A node graph canvas | The format is literally a DAG and drawing it as one is the honest representation. Nodes with live thumbnails of their own output, wires for the child edges. The most visually rewarding index in the cache and currently a two-column grid |
| 27 particles | Gradient pickers for start-to-end colour, sliders for start-to-end size | The live preview viewport is **done** - `ParticlePreviewPanel` runs the selected emitter on the tab's own GL context. The pickers are not, and every colour and size bound is still an integer. Two things the preview deliberately does not do: no material texture, because the graphs are rasterised into the Entities context and a handle does not cross contexts, and no scene, so opcodes 12, 13 and 33 destroy nothing |
| 20 animations | A timeline strip, each frame a cell whose width is its duration, scrubbing the viewport | Priority and re-trigger behaviour want named dropdowns, not 0/1/2 |
| 21 spot anims | A live preview of the model running its animation, recolour pairs as two swatches and an arrow | Editable already; the recolour pairs are the most editable thing in the index and they are four hex numbers |
| 33 loading screens | A visual preview | Needs index 32 for sprites and 13 for fonts; both decode |
| 0 and 1 | A per-bone timeline, with the affected triangles highlighted in the viewport | Read-only is correct for these: a frame slot's POSITION is its bone identity, so inserting one re-points every slot after it |
| 24/25 quick chat | A menu tree as a player sees it, and templates with slots as filled placeholder chips | `My Agility level is [skill level]` rather than `My Agility level is <` |

### Missing capability

- **Port the index-4 sound synthesiser.** 10,237 procedural effects the editor cannot play, and the
  reason 14 of the MIDI patch bank's 21,491 keys are silent in the music player. The client's
  renderer is about 190 lines of fixed-point DSP with a biquad cascade. The format is the one
  canonical format in the cache, so there is no encoding-choice work. Blocks a play button on the
  index-4 tab and completes item 23.
- **Re-bake the world map from index 5.** Index 23 is pre-baked and the client draws it without
  reading index 5 at all, so the two can legitimately disagree - and after any terrain edit they
  will. A genuine content pipeline rather than a viewer, and the honest answer to "I painted Varrock
  and the world map still shows grass".
- **Music import.** Indexes 6 and 11 export MIDI and play. Replacing a track needs the column-major
  packed form rebuilt from an SMF, which is the inverse of a decoder that is lossy by construction -
  many distinct stored delta streams produce byte-identical MIDI. The tab replaces raw bytes today.
- **Per-frame sprite import.** A picture describes one frame, so importing into one of the 44
  multi-frame sets keeps one and discards the rest behind a dialog that says so. The honest fix is
  choosing which frame a picture replaces, and letting a set be assembled from several.
- **Structured control flow in the client-script tab.** The disassembler resolves jump targets and
  marks labels, and says in its own header that it is a listing, not a CFG. Basic blocks, loops and
  if/else would make a script readable rather than merely decoded. Item 26f is the naming half and
  is scheduled; this is the structural half and is not.

### An unpacked working tree, packed on deploy - PARKED

**Deliberately not scheduled. Revisit when there is appetite for an architectural change.**

Today we edit the packed cache in place, so every save rewrites the dat2 and the reference table of
every archive packed alongside, and version control sees one enormous binary change. The
alternative is to dump every index to readable files, treat those as the source of truth, and pack
only on deploy.

**The prerequisite is paid**: a dump-and-repack pipeline is only safe if repacking reproduces what
was dumped, and that is proven for every index that holds content, over both caches. The hazard is
entirely in the non-canonical encodings - opcode order, opcode repetition, aliased values,
absent-versus-default, variable-width integer widths, index 9's raw per-opcode payload spans. Miss
one and an untouched record repacks differently. Every case is already documented per index. **This
is also the paragraph item 25 points at for why its export is read-only.**

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
  record list. Mechanical, but a separate change with its own sweep to clear.
- **Three bespoke `LoadEditorTab` arms are left and no more**: Meta (`Editor.cs:1363`), Sprites
  (`:1404`) and Textures (`:1493`). Tracks, Map and Huffman have their own panels deliberately and
  are not migration candidates.
- **Run the remaining viewer checklist cases.** B, C, E, G and I have never been run, and D is open
  - amber and blue marks appear near the shape, but whether they read as `face N` and `vN` with
  numbers in range is unsettled. Cheaper than it was, now that a DWM-composited capture sees the GL
  surface.
- **Index 32's colour model is settled against a period JVM; only the deployment question is open.**
  Under JDK 8 all 21 payloads decode and match our reading on 97.72% of pixels, never differing by
  more than 3 levels, while the CMYK reading a marker-less four-component file invites sits 53.84
  levels away and matches 4 pixels of 1,176,093. Under JDK 11 `Toolkit` refuses every payload **and
  the client's own capability probe**, so a client there would fall back to index 34. What remains
  open is which JVM a given deployment runs.
