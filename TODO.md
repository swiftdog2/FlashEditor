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

Ordered. The first three are small; the rest are features.

1. **Tabs for the six newest indexes.** 7, 14, 23, 32, the remaining index-2 families and 26
   all landed as codecs with no GUI, by design, so none is reachable from the application.
   Index 7 wants the Entities page below rather than a list of its own.
2. **Record index 26's census.** Both runs print `UNRECORDED` for its declared and present
   texture counts, so a change in that population passes silently. Measured independently:
   915 in the vanilla capture, 1,408 in the repack. Small, and it closes a real hole.
3. **The editor half of the JS5 handshake.** The server half is written and pushed; without
   this the loop cannot be proven at all. See the JS5 section below.
4. **Interface name recovery.** Apply `HydraScape/docs/cache-format/Leanbow Interface Names.txt`
   (467 entries, keyed by group id). The hash is **djb2** (`h * 31 + c`), confirmed against the
   real cache; the `h * 61 + (c - 32)` variant is wrong and matched nothing. Every entry must
   re-hash and match before it is shown, so a bad row reads as unnamed rather than as a false
   name.
5. **Entities page.** One page hosting Items, NPCs, Objects and Models with a type selector and a
   single persistent viewport. Fixes the current tab dance, where seeing an item's model means
   visiting Models first. One GL context, never reparented - reparenting a `GLControl` destroys
   its handle and its context.
6. **Skinning and animation playback.** The renderer has none: `VertSkins` and `FaceSkin` are
   parsed but never uploaded, and the shader has no bone attributes. Port the client's CPU-side
   transform application rather than inventing GPU skinning. Viewport runs at 30fps; the
   animation advances on its own stored frame durations, which is a different thing.
7. **Model export and import via OBJ.** Import must be **geometry replacement, not file
   replacement**: OBJ cannot express face render types, priority and alpha arrays, textured
   triangle types, vertex skins or particle attachment points, so everything except vertices and
   faces is preserved from the original and an unmappable face count is refused.
8. **Wireframe and vertex index overlay.** Hover a face, see its edges and its vertex indices, so
   particle attachment points can be chosen. CPU ray-triangle pick, then draw the indices as 2D
   text over the viewport rather than rendering text in GL.
9. **Particle rendering.** Decode lands with index 7; the simulation and draw path are new work.
   Index 27 supplies the emitters and effectors.
10. **Sprite import.** PNG, JPG and BMP into the sprite editor. Three real problems: palette
   quantisation to 255 colours with index 0 reserved transparent, the black remap (a stored
   `0x000000` decodes as `0x000001`, so pure black must be written as the latter or it
   disappears), and whether to emit an alpha plane for a fully opaque frame. Both spellings and
   both alpha choices occur in shipped data.
11. **Client background import.** Probably a separate path: the background lives in index 32,
   which is mostly JPEG rather than sprite format. Storing the supplied file is likely correct;
   transcoding is not.

---

## Backlog

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
