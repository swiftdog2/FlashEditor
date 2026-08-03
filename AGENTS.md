# FlashEditor Contribution Guide

This repository contains a C# RuneScape cache editor targeting **Revision 639**. It provides a WinForms-based GUI to load, view, edit, and update JS5 cache archives. The application targets **.NET 9** and includes xUnit-based unit tests.

### Which revision, and how that was established

The editor decodes the **cache**, so 639 is the number that matters here. Two 639 caches are on
disk and the suite runs against both:

- **The vanilla b639 capture**, `OpenRS2/cache-runescape-live-en-b639-2011-02-23-00-00-00-openrs2#1194/cache`,
  a live-server capture from the OpenRS2 archive with its key dump beside it as `keys.json`.
  This is the default and the source of truth.
- **The repack**, `cache/`, a 639 base with local modifications. Still supported, still has to
  pass, and every figure measured against it belongs to it.

It was determined to be 639 from the data rather than from any comment:

- Reference-table versions are per-build and monotonic. The cache matches build 639's exactly on
  indexes 2, 3, 12, 16 and 20; it is never *below* 639 on any index, and it is below build 640 on
  every one of those five. So the base build is 639.
- The repack carries local modifications on at least **eleven** indexes, not the four first
  reported. Measured against the vanilla capture, its reference-table version is higher on 5, 7,
  8, 9, 17, 18, 19, 26, 27 and 29, and its index-3 table differs in content at the **same**
  version, 1131 in both. So a bumped version is evidence that an index was touched and is never
  evidence that one was not. The eleven tables whose payloads differ are 3, 5, 7, 8, 9, 17, 18,
  19, 26, 27 and 29; the other 24 are byte-identical.
- XTEA keys do **not** identify a build. The same key dump opens every keyed archive of both
  caches - 1587 of 1587 in the vanilla capture, 598 of 598 in the repack, the difference being
  that the repack has already decrypted the rest in place. A successful decrypt says the key dump
  is compatible, not that it matches a build. 62 of the vanilla capture's encrypted map squares
  have no key in the published dump; the repack has 61.

The **client** source bundled alongside this cache (in the sibling HydraScape repository) is
a different revision: **637**. It writes `637` as the revision field in the JS5 handshake and
in both the lobby and game login blocks, and renders the literal string `"Build: 637"`; the
number 639 appears nowhere in its 854 source files. Client and cache are a mismatched pair,
which matters if you use that client as a reference for decoder behaviour - notably
`reference/hydra-model-decoding/`, which was taken from it.

## Build Requirements
- Visual Studio or `dotnet` with the .NET 9 SDK.
- Both projects target `net9.0-windows` and set `EnableWindowsTargeting`, so the solution and
  its tests are Windows-only.
- Packages come from `PackageReference` in each `.csproj`; there is no `packages.config`. The
  app depends on SharpZipLib, BouncyCastle.Cryptography, Newtonsoft.Json, OpenTK and
  ObjectListView.
- `FlashEditor.csproj` declares `InternalsVisibleTo` for `FlashEditor.Tests`, which is why the
  tests can reach `RSFileStore`, `RSIndex` and other internal types.

## Test Suite
- Tests reside under `FlashEditor.Tests` and target `net9.0-windows`.
- xUnit 2.9.3 with `xunit.runner.visualstudio`. There is no mocking framework: the suite works
  against real bytes and synthetic caches in temp directories instead.
- `dotnet test` builds first, so no separate build step is needed.
- Most of the suite needs a real 639 cache. Two are on disk, both gitignored, and the vanilla
  OpenRS2 capture is the default; `FLASHEDITOR_TEST_CACHE` selects a particular one. See
  `CLAUDE.md` for the layout and for which counts belong to which cache.
- A count the reference table states is read from the table. A count of decoded content that
  differs between the two caches lives in
  `FlashEditor.Tests/Cache/RealCache/RealCacheProfile.cs`, which recognises the cache from its own
  declared counts on indexes 3, 9 and 19 - never from a directory name, and never from a
  reference-table version.

## Cache Editing Overview
The editor loads, displays, and modifies the RuneScape JS5 cache for revision 639. Important details for understanding the cache structure:

## Updated Cache Blueprint (rev \u2248 639)

### Physical Files
- `main_file_cache.dat2` – payload sectors (520-byte blocks)
- `main_file_cache.idx0` … `idxN` – lookup per index (0–30)
- `main_file_cache.idx255` (META) – reference-table index
- `dat2` stores every group as a chain of 520-byte sectors.

### Logical Hierarchy
- `INDEX` (0–30) → `GROUP` ("container") → `CHILD[0…n]`

### Reference Table
- Format byte `6` or `7`
- `hasNames` flag
- `groupCount` (u16)
- Delta-encoded `groupIds` array
- `groupCrc`, `groupVersion`, and `groupFlags` arrays (format 7+ only for `groupFlags`;
  bit0 = XTEA, the rest unidentified - encode the byte back whole rather than rebuilding
  it from bit0, or the unknown bits are lost on the first save)
- Optional `groupSizes` pair per group when the `sizes` flag (`0x04`) is set, written
  between the whirlpool digests and `groupVersion`. Both describe the group as stored and
  must be recomputed whenever it is rewritten:
  - compressed = the whole stored container **minus its version trailer**, the same span
    the group CRC covers. The trailer is only present when the container carries a
    version, so read its length off the container rather than assuming 2.
  - uncompressed = the group payload before compression, i.e. the container's own
    uncompressed-length header field.
- For each group:
  - `fileCount` (u16)
  - Delta-encoded `fileIds` array

#### Nothing in either cache sets the sizes flag, and nothing in either is format 7

Measured over all 35 reference tables in idx255 by
`FlashEditor.Tests/Cache/RealCacheReferenceTableShapeTests.cs`. The flags byte is identical
index for index in both caches:

| Flag or field | Where it is set |
|---|---|
| identifiers `0x01` | indexes 3, 5, 6, 8, 10, 12, 13, 23, 30, 31, 32, 33 |
| whirlpool `0x02` | index 30 alone |
| **sizes `0x04`** | **nowhere** |
| entry hash `0x08` | nowhere |
| format `7` | nowhere - every table is format 6 bar index 36, a four byte format-5 stub |

The `groupSizes` rules above and the `groupFlags` byte with them are therefore correct and
unreachable: no shipped table puts either to the test, so no byte-identity sweep defends them and
a passing sweep is not evidence they are right. Keep both implemented - a decoder that drops
either mis-parses the first table that does set them, from that field onward. The operational
consequence is that the XTEA bit is stated nowhere on disk in this cache, which is what forces the
read path to infer encryption and the write path to refuse to guess.

Index 2 carries no name hashes either, so a config group is addressable only by id.

The **repack** carries a tail the format does not account for on four tables: index 9 has 3784
trailing zero bytes, 26 has 4, 27 has 1684 and 29 has 728 - four bytes per **file** in every case,
not per group (27's 421 files sit in 2 groups, 29's 182 in 1). It sits where the per-file
identifier block would, with the identifiers flag clear, so nothing reads it. Its other 31 tables
consume to the byte, and **every one of the vanilla b639 capture's 35 tables does**. So the tail
is repacker residue rather than a feature of the format: a parser must tolerate trailing bytes
rather than assert exact consumption, and nothing may assert that a tail is present either.

### Index Entry (.idx#)
- 6 bytes per group
- Bytes `0–2` → compressed length
- Bytes `3–5` → first sector offset

### Sector Header (dat2)
`|2 bytes archiveId|2 bytes chunk#|3 bytes nextSector|1 byte indexId|512 data|`

Read the field order off `RSSector.Decode`/`Encode` rather than from memory. This document had
the first and last pairs the wrong way round until 2026-08-02, which is the kind of error that
produces a cache that looks structurally fine and is unreadable.

### Container Wrapper
- Byte `compressionType` (`0`=none, `1`=BZip2, `2`=GZip)
- `compressedSize` (4 bytes)
- `uncompressedSize` (4 bytes when compressed)
- Payload (optionally XTEA-encrypted)
- Optional 2-byte version trailer. **Present or absent depending on the container** - in a
  real 639 cache every archive container carries one and every reference-table container in
  the meta index does not. Derive its length by subtracting the header and `compressedSize`
  from the stored length; never assume 2. The archive CRC and the reference table's
  `compressed` size are both taken over the stored container *minus* this trailer, so a
  wrong trailer length puts both out of step with the client.

#### A re-encode is never byte-identical for GZip

Compression in the **repack**: **96,244 GZip, 4,480 uncompressed, 1,743 BZip2.** These are
whole-cache totals, so they belong to that cache; the ratio does not move much in the vanilla
capture but the denominators have not been re-measured. Decoding and re-encoding every one of them
gives:

| Compression | Byte-identical after a round trip |
|---|---|
| none | 4,480 / 4,480 |
| BZip2 | 1,724 / 1,743 |
| **GZip** | **0 / 96,183** |

The claim that survives without a denominator, and the only one worth relying on: **no GZip
container in either cache re-encodes byte-identically.**

Deflate is not canonical: Jagex used Java's `Deflater`, this project uses SharpZipLib, and both
emit valid GZip of the same payload with different encodings. Our output is within 0.06% on
size, so this is not a defect to fix - cloning their compressor is the wrong instinct.

Two consequences that matter. **Never compare compressed containers** to decide whether
something changed; compare the decompressed payload. And because the archive CRC covers the
*stored* bytes, re-encoding changes the CRC even when the content is identical - which is why
a save that changes nothing now writes nothing at all rather than re-encoding.

Jagex writes `MTIME = 0` in every GZip header (715 of 715 sampled), so a compressor that stamps
the current time both diverges from the format and makes its own output unreproducible.

### Group (archive) payload
What sits inside the container once decompressed. The file count comes from the reference
table, not from the payload.

- **One file** - no trailer at all. The whole payload is the file. The client special-cases
  a file count of 1, so writing a size table or a chunk-count byte here hands those bytes
  back as file data and grows the file on every save.
- **More than one file** - payload first, then the trailer:
  - `chunks x fileCount` big-endian `int32` sizes
  - a final unsigned byte holding `chunks`
- The payload is stored **chunk-major**: chunk 0 of every file, then chunk 1 of every file,
  and so on. Files are in ascending id order within each chunk.
- The size table is delta-encoded **across the files within a chunk**, and the running total
  restarts on each chunk. So for chunk `c`, `size[c][0]` is the first delta and
  `size[c][i] = size[c][i-1] + delta`.
- `chunks` is commonly **3**, not 1. Laying the files out end to end instead of chunk-major
  produces a payload of exactly the same length with the bytes in the wrong order, which a
  round trip through this codec cannot detect.

#### The split is arbitrary, and nothing may assume otherwise

Measured across the whole **repack**. Every row bar the first is index 0 only, and index 0's
reference table and every group CRC in it are byte-identical in both caches, so only the first row
is a repack-scale figure:

| | |
|---|---|
| Chunk counts occurring anywhere | only **1** or **3** (1,638 and 3,517 groups) |
| Where the multi-chunk groups live | **all 3,517 are in index 0** (animation frames) |
| Chunk 0's length, all 359,922 files | exactly **4 bytes** |
| Files with no monotonic order across their chunks | **64.3%** |
| Non-proportional groups | 3,461 of 3,517 |
| Files with a zero-length middle or last chunk | about 3,200 |
| Negative deltas used in size tables | **222,317** |

So: **any split the size table describes is legal.** Nothing may assume proportional, equal,
or monotonically ordered chunks, and the `int32` deltas are genuinely signed. Zero-length
slices are normal and load-bearing.

The client's own reader settles it - `JS5Archive.method2729`, multi-file branch at lines
383-440. It reads every one of the `chunks x fileCount` entries twice, once to size each
file's array and once to copy, and slices exactly per entry. Nothing is derived, halved or
assumed even. A copy of that file is already in this repository at
`reference/hydra-model-decoding/JS5Archive.java`, so the unpacker does not need hunting again.

The constraints its reader actually imposes, and therefore the only ones encoding must respect:
every per-file total must be non-negative, the table entries must sum to the body length
exactly, and `chunks` must fit in an unsigned byte. Within that, a file may be re-sliced freely
- which is why an edit re-slices only the file it touched instead of collapsing the group.

Revision 639 (and later) expects the header layout above. Some earlier
revisions swapped the two length fields, so both encode and decode must
use this ordering to remain compatible with the in-game client.

### XTEA Layer
- Applied after compression and before sectorisation
- 32-round standard XTEA over 8-byte blocks; a trailing partial block is left in the clear
- Key of four 32-bit integers (`0,0,0,0` indicates no encryption)
- **The encrypted region is `[5, 5 + compressedSize + (compressed ? 4 : 0))`.** It starts
  after the compression type and the compressed size, and for a compressed container it
  therefore *includes the 4-byte uncompressed-size field*. That field cannot be read until
  the region has been deciphered, and deciphering only the bytes after it offsets every
  8-byte block by four so nothing decrypts at all.
- A format 6 table has no per-archive encryption flag, so the only available signal that an
  archive is encrypted is that a key exists for it. That signal is not reliable - a repacked
  cache may have had archives decrypted in place while a build-wide key dump still lists
  them - so a key that fails to fit is treated as "not encrypted", not as an error.
- **An archive is written back in the state it was read in, or not at all.** The same
  missing flag that makes the read path guess makes the write path unable to guess safely:
  writing a decrypted archive back as plaintext destroys it silently, because the client
  deciphers it regardless and nothing on disk records the change. The state is therefore
  recorded on the container at decode time (`RSContainer.StoredEncrypted`) and re-used on
  encode; where no key is available to honour it, the save fails rather than proceeding.
- **An archive CRC covers the stored bytes, so for an encrypted archive it covers the
  ciphertext.** Anything computing one has to encode the container with its key.
- Keys for a given build can be had from the OpenRS2 archive
  (`https://archive.openrs2.org/caches.json`, then `/caches/runescape/<id>/keys.json`).
  In that export `archive` is the **index** and `group` is the **archive id**; entries also
  carry `name_hash`, `name` (`l<x>_<y>`) and `mapsquare`. Build 639 is cache id 1194, which is
  the capture on disk at
  `OpenRS2/cache-runescape-live-en-b639-2011-02-23-00-00-00-openrs2#1194/`, key dump included as
  `keys.json` beside its `cache/` directory. Both caches use that same dump.

### The actual types

This section used to sketch a `CacheEditor` class that does not exist and never has. The real
layering, innermost first:

| Type | File | Responsibility |
|---|---|---|
| `JagStream` | `IO/JagStream.cs` | The buffer everything reads and writes through |
| `RSSector` | `Cache/RSSector.cs` | One 520-byte sector: header plus 512 data bytes |
| `RSFileStore` | `Cache/RSFileStore.cs` | dat2 and the idx files; sector-chain allocation; staged saves |
| `RSContainer` | `Cache/RSContainer.cs` | The stored wrapper: compression, XTEA, version trailer |
| `RSArchive` | `Cache/RSArchive.cs` | A group's payload: the file split and its chunk layout |
| `RSReferenceTable` / `ReferenceTableCodec` | `Cache/` | idx255 metadata: CRCs, versions, file ids |
| `RSCache` | `Cache/RSCache.cs` | Ties them together. `GetContainer`, `ReadFile`, `WriteFile` |

`RSCache.WriteFile(indexId, archiveId, fileId, data)` is the single entry point for an edit,
and the place the interesting rules live: it re-encodes the archive, decides whether anything
actually changed, resolves the XTEA key, and updates the CRC and reference-table entry.

### Write Algorithm (high-level)
1. Build container (compress → XTEA → add length table)
2. Allocate/extend sector chain, write chunks, update `.idx#`
3. Update reference table (CRC32, version, flags) and re-CRC it
4. Write updated reference table back into index 255
5. Optionally bump idx255’s own CRC/version

### Index Map

**The cache and the client are the source of truth here, not `RSConstants.cs`.** This table used
to say the opposite. It was written when the only known failure was a table listing a different
revision's layout (index 16 as "MIDI instrument bank", 18 as "Textures", 19 as "Enums"), so
"believe `RSConstants`" was the fix. That advice is now wrong: a sweep of every index against the
637 client found **five constant names that misdescribe what the index holds**, flagged in the
Contents column below. Believe what the client does with an index and what the index actually
contains; treat the constant name as a claim.

The group and file counts below are measured from the **vanilla b639 capture**, and corroborate
the naming where it is right: index 19 holds 80 groups of ~20,427 item definitions, index 18 holds
13,359 NPCs and index 16 holds 56,199 objects, which is the right order of magnitude for this
revision.

**Six rows the repack inflates**, with its figures in brackets: index 3 (1078 groups / 42,256
files), 7 (63,614), 9 (946), 19 (20,470 files), 27 (421 files) and 29 (182 files). Every other row
is identical in both caches. Never write one of the six into a test - read it from the reference
table.

   | ID  | RSConstants name       | Contents                        | Groups | Files   |
   |-----|------------------------|---------------------------------|--------|---------|
   | 0   | FRAMES                 | Animation frames                | 3526   | 359931  |
   | 1   | SKINS                  | Animation skins                 | 3106   | 3106    |
   | 2   | CONFIG                 | Configs                         | 35     | 16981   |
   | 3   | INTERFACE_DEFINITIONS  | Interfaces                      | 1067   | 40883   |
   | 4   | SOUND_EFFECTS          | Sound effects                   | 10237  | 10237   |
   | 5   | MAPS                   | Maps (partly XTEA encrypted)    | 5203   | 5203    |
   | 6   | MUSIC                  | Music                           | 963    | 963     |
   | 7   | MODELS                 | 3-D models                      | 63607  | 63607   |
   | 8   | SPRITES                | Sprites                         | 4593   | 4593    |
   | 9   | TEXTURES               | Textures                        | 915    | 915     |
   | 10  | HUFFMAN                | Huffman chat table              | 1      | 1       |
   | 11  | MUSIC_2                | Music, second bank              | 441    | 441     |
   | 12  | CLIENT_SCRIPTS         | Client scripts (CS2)            | 4149   | 4149    |
   | 13  | FONTS                  | Font metrics                    | 25     | 25      |
   | 14  | SFX2                   | Vorbis samples, g0 = codebooks  | 3657   | 3657    |
   | 15  | SFX3                   | **MIDI patch bank, NAME WRONG** | 176    | 176     |
   | 16  | OBJECTS_DEFINITIONS    | Object definitions              | 224    | 56199   |
   | 17  | CLIENTSCRIPT_SETTINGS  | **Enum table, NAME WRONG**      | 14     | 3558    |
   | 18  | NPC_DEFINITIONS        | NPC definitions                 | 106    | 13359   |
   | 19  | ITEM_DEFINITIONS       | Item definitions                | 80     | 20427   |
   | 20  | ANIMATIONS             | Animation definitions           | 120    | 15260   |
   | 21  | GRAPHICS               | Spot-animation definitions      | 12     | 2956    |
   | 22  | SCRIPT_CONFIGS         | Varbits                         | 9      | 8785    |
   | 23  | WORLD_MAP              | World map                       | 76     | 1043    |
   | 24  | QUICK_CHAT_MESSAGES    | **Quick-chat bank, NAME WRONG** | 2      | 1299    |
   | 25  | QUICK_CHAT_MENU        | **Quick-chat bank, NAME WRONG** | 2      | 86      |
   | 26  | MATERIALS              | Material / lighting configs     | 1      | 1       |
   | 27  | CONFIG_PARTICLES       | Particle emitters and effectors | 2      | 285     |
   | 28  | DEFAULTS               | Default sprite ids and colours  | 2      | 2       |
   | 29  | CONFIG_BILLBOARD       | Billboard configs               | 1      | 90      |
   | 30  | NATIVE_LIBRARIES       | Native libraries                | 36     | 36      |
   | 31  | GRAPHICS_SHADERS       | Shader programs                 | 2      | 14      |
   | 32  | LOADING_SPRITES        | Loading sprites (JPEG)          | 26     | 26      |
   | 33  | GAME_TIPS              | Loading-screen tips             | 2      | 343     |
   | 34  | LOADING_SPRITES_RAW    | Loading sprites (Jagex format)  | -      | -       |
   | 35  | THEORA_AKA_CUTSCENES   | **Nothing, NAME WRONG**         | -      | -       |
   | 36  | VORBIS                 | **Theora video, NAME WRONG**    | 0      | 0       |
   | 255 | META                   | Reference tables                | 37     | -       |

Indexes 34 and 35 have no reference table at all in either cache; index 36 has one that declares
zero groups - a four byte format-5 stub, which is a real shape the table codec has to survive.
The repack additionally leaves one-byte `main_file_cache.idx34` and `idx35` files on disk, which
the vanilla capture does not have at all; `RSFileStore.cs:34-36` skips a missing idx file, so both
open cleanly.

#### The five wrong names, and the audio family in particular

Established by sweeping every index in the reference cache and matching it against what the 637
client does with that index.

- **15 is the MIDI instrument/patch bank**, not a third sound bank: 176 groups of ~290 bytes, each
  a sparse 256-entry table. `Particle_Sub3_Sub5_Sub2.java:99-100` passes it alongside 14 and 4, and
  `Class355.method3875` maps a MIDI program to samples drawn from those two.
- **17 is the enum table.** The client's own field is `enumFileStore` (`Node_Sub10_Sub24.java:9`).
  14 groups x 256 files. **Group 5 is the music track name list**, group 2 the skill names.
- **35 is referenced nowhere by the client** - absent from the `openFileStore` sequence at
  `InterfaceSettings.java:157-188`, which covers 0-34 and 36 and skips it, leaving
  `aFileStoreArray844[35]` permanently null.
- **36 is Ogg/Theora video**, the only index opened with `fileType = 2`; `Class237` is an Ogg
  container. Empty here. **So 14 and 36 are effectively swapped relative to their names.**
- **24 and 25 are both complete quick-chat banks.** Menu versus message is **group 0 versus group 1
  within each**, not a split across the two indexes; the client picks 25 over 24 on `id >= 0x8000`.
  Adopting the current names would encode a false model of the format.

Two things about the audio family that are easy to get wrong:

- **6 is music and 11 is jingles**, same packed format, both starting `0x17` and both decoded by
  `Node_Sub7.method985`. 6 is 963 groups at ~40 KB median and **carries name hashes**; 11 is 441
  groups at ~1.6 KB median and **carries none at all** (table flags `0x00`), so no jingle can ever
  be named from the cache.
- **On index 14, group 0 is the Vorbis setup header and codebooks**, structurally unlike groups
  1..3656, which each open with a 17-byte header (sample rate 22050, sample count, loop start, loop
  end, flag). Decoding group 0 as a sample is a category error; the client reads it separately at
  `Node_Sub13.method1133`.

#### Group and file names hash with Java `String.hashCode`

`h * 31 + c` over the lowercased name. Proven independently of any decoder: index 31 holds exactly
two groups whose identifiers are 3301 and 3220, which are `"gl"` and `"dx"` - the OpenGL and Direct3D
shader sets, corroborated by their payloads starting `!!ARBvp1.0` and a D3D `CTAB` block. That one
fact is what makes name recovery possible on every index that sets the identifiers flag.

## Definition opcodes: read the reference before changing a decoder

`reference/hydra-637-definitions/` holds de-obfuscated opcode tables for the item, NPC and
object decoders in the bundled 637 client, cross-referenced against this project's codecs.
Every client claim cites a `file:line`, so any row can be checked in seconds.

Consult it before altering how any definition opcode is read.

### The client leads, the data vetoes

**The 637 client is the reference for how anything is implemented** - algorithms, field
meanings, signedness, read order. Where it and this project disagree, the client is right
unless the 639 data says otherwise.

**The 639 cache overrides it only where the data proves something the client cannot know.**
The cache is two builds later, so it can legitimately contain what the client never saw. That
veto is not hypothetical: item opcode **131 occurs in this cache and the 637 client has no
handler for it at all** - the chain tests 129, 130, 132, 134 and 249 and 131 falls straight
through. Removing our handler to match the client would break decoding of real data.

The two rules in practice:

| Situation | What to do |
|---|---|
| Client reads it differently to us | Follow the client. |
| Client has no handler, but the opcode occurs in 639 data | Keep ours. The data vetoes. |
| Client has no handler and the opcode never occurs | Leave it. Record it. Change nothing. |
| Only the cache can answer (a payload size) | The data decides, proven by an exact-consumption sweep. |
| Only the client can answer (signedness, meaning) | The client decides. The cache cannot reveal either. |

A disagreement that the data cannot arbitrate is usually to be recorded rather than resolved -
all three codec sweeps found opcodes this project handles that the 637 client does not, and
none of those occur in the 639 cache.

The rows worth attention are `SIGNEDNESS-DIFFERS` and `SEMANTICS-DIFFER`: no test in the
suite can detect either, and both surface as wrong values in the editor and wrong data on
save.

## Models: the face render type decides what is drawn

Every model face carries a render type. Only three values occur anywhere, measured over the
**repack's** index 7 - it holds 63,614 models to the vanilla capture's 63,607, so every absolute
figure in this section is at repack scale and has not been re-measured against the capture:

| Value | Meaning | Faces |
|---|---|---|
| 0 | Gouraud shaded | 10,190,480 |
| 1 | Flat shaded | 1,337,038 |
| **2** | **Not drawn** | **34,528** |

A further 10,613,853 faces belong to models that carry no render-type array at all, which
is equivalent to type 0.

**Type 2 is a visibility flag, not a shading mode.** Both of the client's renderers gate
their draw list on it before anything else touches the face - `Renderable_Sub2.java:397`
and `Renderable_Sub3.java:172`, the latter spelling `!= 2` as `(x ^ 0xffffffff) != -3`.
Those faces are stray geometry: they carry face colour HSL 0, which the palette maps to
near-black, and they are frequently unattached to the mesh. Model 15748's face 433 spans
34% of the model's bounding box on three vertices no other face references.

They are spread across **12,621 of the 63,604 models**, so anything that walks faces and
ignores the type is wrong on one model in five. `ModelRenderer` did exactly that until
2026-08-02 and put black slivers on the viewer.

Only the newer and newest formats can express type 2. The legacy decoder derives the type
from bit 0 of a packed flags byte, so it only ever produces 0 or 1 - which matches the
data, where every model carrying a type-2 face is in the newer format.

Field names for the rest of the face arrays are in `reference/hydra-model-decoding/`. Five
of them were shuffled in that table until 2026-08-02, so read the correction note there
before trusting a name in it.

## Coding Guidelines
- Include C# XML documentation comments on public classes and members whenever the intent isn't obvious.
