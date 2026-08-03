# Index 2 (CONFIG) - build plan, group by group

Research pass. No code was written. Every claim below is either a `file:line` in the 637 client at
`C:\Users\CJ\Desktop\RSPS\Hydra\Client\src`, or a measurement over the 639 cache at
`C:\Users\CJ\Desktop\FlashEditor\cache`. Where it is a measurement the number is given and the
sentence says "measured".

**How the measurements were taken.** A standalone Python reader was written in the session
scratchpad (not in the repo) that walks `main_file_cache.idx255` to the index-2 reference table,
then `main_file_cache.idx2` and `main_file_cache.dat2` for each group, unwraps the container,
splits the archive chunk-major per `AGENTS.md`, and runs a per-group opcode walker built from the
client's dispatchers. It reproduces the project's own numbers where they overlap (56,199 object
definitions consume exactly, 235 floor overlays, 159 underlays, 100 map scene icons), which is the
cross-check that the reader itself is right. It is not a substitute for a `DefinitionSweep` in the
test project; it is what told us what to build one against.

---

## 0. What the reference table actually declares

Measured, by parsing idx255 group 2 directly.

| | |
|---|---|
| Format / version / flags | 6 / 785 / `0x00` |
| Declared groups | **35** |
| Total files | **16,981** |
| Bytes consumed by the table | 34,390 of 34,390 - **no trailing bytes** |
| Name hashes | none, on groups or files (flags `0x00`) |
| `main_file_cache.idx2` size | 294 bytes = **49 slots**, 14 of which the table never names |

The group ids, confirmed from the table and **not** assumed to be `0..48`:

```
1 2 3 4 5 7 11 15 16 18 19 20 21 22 23 24 25 26 31 32 33 34 35 36 37 38 39 40 41 42 43 44 45 46 48
```

Slot 0 carries length `0xFF0000` and sector 0 and is not in the table. Reading it blind asks the
store for a 16 MB sector chain. **Iterate the reference table.**

Per-group container facts (measured). Every container carries a 2-byte version trailer.

| Group | Files | Compression | Stored | Payload | Table version |
|---|---|---|---|---|---|
| 1 | 159 | gzip | 1,049 | 2,133 | 27 |
| 2 | 394 | gzip | 52 | 1,971 | 44 |
| 3 | 652 | bzip2 | 2,067 | 8,036 | 14 |
| 4 | 235 | gzip | 1,797 | 3,707 | 48 |
| 5 | 609 | bzip2 | 443 | 4,873 | 74 |
| 7 | 337 | gzip | 50 | 1,686 | 30 |
| 11 | 1,330 | bzip2 | 1,170 | 9,901 | 130 |
| 15 | 345 | gzip | 50 | 1,726 | 41 |
| 16 | 2,002 | bzip2 | 110 | 10,038 | 170 |
| 18 | 1,198 | gzip | 59 | 5,991 | 151 |
| 19 | 1,445 | bzip2 | 313 | 10,135 | 152 |
| 20 | 103 | gzip | 43 | 516 | 18 |
| 21 | 9 | gzip | 39 | 46 | 2 |
| 22 | 1,227 | gzip | 58 | 6,136 | 49 |
| 23 | 269 | gzip | 48 | 1,346 | 7 |
| 24 | 11 | gzip | 39 | 56 | 1 |
| 25 | 377 | gzip | 51 | 1,886 | 41 |
| 26 | 1,730 | gzip | 66,396 | 240,728 | 207 |
| 31 | 4 | gzip | 73 | 65 | 2 |
| 32 | 1,972 | bzip2 | 13,006 | 26,411 | 224 |
| 33 | 175 | gzip | 415 | 1,926 | 15 |
| 34 | 100 | gzip | 232 | 789 | 6 |
| 35 | 187 | bzip2 | 3,393 | 7,692 | 45 |
| 36 | 1,051 | bzip2 | 8,732 | 41,931 | 65 |
| 37 | 13 | gzip | 39 | 66 | 3 |
| 38 | 1 | **none** | 8 | 1 | 1 |
| 39 | 1 | **none** | 8 | 1 | 1 |
| 40 | 2 | **none** | 18 | 11 | 1 |
| 41 | 323 | gzip | 50 | 1,616 | 13 |
| 42 | 95 | gzip | 43 | 476 | 19 |
| 43 | 185 | gzip | 45 | 926 | 18 |
| 44 | 398 | gzip | 52 | 1,991 | 11 |
| 45 | 13 | gzip | 39 | 66 | 4 |
| 46 | 28 | gzip | 253 | 898 | 2 |
| 48 | 1 | **none** | 8 | 1 | 1 |

Chunk count is 1 for every multi-file group in index 2; groups 38, 39 and 48 are single-file, so
they carry no size table at all (payload length 1 = the whole file).

### Groups with non-contiguous file ids

Measured. Eight groups. Loop the reference table's declared ids, never `0..count-1`.

| Group | Files | Max id | Missing ids |
|---|---|---|---|
| 11 | 1,330 | 1,330 | 371 |
| 16 | 2,002 | 2,050 | 49 ids: 1115, 1116, 1310, 1643, 1708-1715, ... |
| 19 | 1,445 | 1,452 | 740, 745, 780, 781, 782, 789, 1409, 1410 |
| 22 | 1,227 | 1,262 | 36 ids: 297-300, 302-304, 306-308, 364, 365, ... |
| 32 | 1,972 | 1,972 | 502 |
| 34 | 100 | 100 | 98 |
| 41 | 323 | 325 | 89, 91, 92 |
| 43 | 185 | 187 | 183, 184, 185 |

---

## 1. The headline finding: half of index 2 is empty records

**8,694 of the 16,981 files are a single byte, `0x00`.** Measured over every file in every group.

Nineteen groups are **100% empty** - every file in them is that one byte:

```
2  7  15  18  20  21  22  23  24  25  37  38  39  40  41  42  43  44  45  48
```

(That is 20 group ids; group 15 is listed here for its record contents but has a client provider,
see §3.) The 19 provider-less groups hold 5,302 files between them and **not one of them carries a
single opcode.**

Four more groups are mostly empty: group 16 (1,993 of 2,002 empty), group 11 (851 of 1,330), group
26 (548 of 1,730), group 15 (345 of 345).

The consequences are large and they cut both ways:

- **There is nothing to reverse engineer in those 19 groups.** The task brief asks for an opcode
  set per provider-less group; the answer, from the bytes, is that the opcode set is empty in this
  cache and no opcode table can be recovered from 639 data at all. A record is opcode-terminated
  and terminates immediately. Anything else about their format is unknowable here. **Do not invent
  field names for them.** These are almost certainly server-side config types whose payloads Jagex
  strips from the client cache while keeping the id space - the id space is the only information
  they still carry.
- **They are trivially codec-complete.** A decoder that reads the opcode loop and throws on any
  unknown opcode decodes all 5,302 of them exactly, and an encoder that replays the recorded
  opcode stream writes back the same byte. They can be shipped in one pass with a shared codec and
  a byte-identity sweep that is meaningful (it pins "the group is still empty"), at near-zero cost.
- **The empty-record count is the regression detector for them.** If a future cache puts real bytes
  in group 22, the shared codec throws on the first unknown opcode rather than silently mis-reading.

---

## 2. Client providers - the corrected list

Index 2 is `client.BIT_CONFIG`, opened at `InterfaceSettings.java:160` as
`openFileStore(-116, false, 1, 2)`. Eighteen classes are constructed with it
(`InterfaceSettings.java:247-293`) and each names its group in its constructor with
`getChildsInFolder(0, <group>)`.

**Correction to `reference/index-survey/index-002-CONFIG.md`.** That survey says "18 groups have a
locatable provider and 17 do not". Eighteen providers exist, but **two of them (groups 29 and 30,
`Class59.java:64` and `Class115.java:53`) name groups that this cache does not contain.** So the
split over the 35 groups that do exist is **16 with a provider and 19 without** - and the survey's
own list of provider-less groups has 19 entries (`2, 7, 18, 20, 21, 22, 23, 24, 25, 37-45, 48`),
so the prose figure contradicts its own list. Use 16 / 19.

| Group | Provider (`getChildsInFolder` site) | File read at | Record class | Record decode loop |
|---|---|---|---|---|
| 1 | `Class153.java:201` | `:241` | `FloorUnderlay` | `method717` `:52-61` |
| 3 | `Class83.java:158` | `:179` | `Class152` | `method2480` `:265-286` |
| 4 | `Class32.java:79` | `:129` | `FloorOverlayConfig` | `method2688` `:105-120` |
| 5 | `Class8.java:163` | `:189` | `Node_Sub46_Sub18` | `method1628` `:31-47` |
| 11 | `Class365.java:102` | `:136` | `Class149` | `unpackConfig` `:128-145` |
| 15 | `Class239.java:75` | **never** | none | none |
| 16 | `Class139.java:19` | `:56` | `Class167` | `method2527` `:104-125` |
| 19 | `Class132.java:117` | `:139` | `Class90` | `method885` `:95-111` |
| 26 | `Class264.java:67` | `:90` | `InterfaceConfig` | `method1588` `:82-101` |
| 31 | `Class269.java:161` | `:197` | `Class379` | `method4008` `:39-56` |
| 32 | `Class257.java:82` | `:126` | `Class294` | `method3475` `:176-194` |
| 33 | `Class11.java:33` | `:91` | `Class231` | `method2880` `:160-176` |
| 34 | `Class335.java:61` | `:83` | `Class9` | `method192` `:214-231` |
| 35 | `Class13.java:123` | `:161` | `Class220` | `method2816` `:56-77` |
| 36 | `Class341.java:141` | `:185` | `Class24` | `method290` `:399-425` |
| 46 | `Class121.java:102` | `:158` | `Class86` | `method851` `:312-333` |
| ~~29~~ | `Class59.java:64` | `:98` | - | group absent from this cache |
| ~~30~~ | `Class115.java:53` | `:107` | - | group absent from this cache |

**Group 15 has a provider that never reads a file.** `Class239` (`Class239.java:70-79`) stores the
archive and the child count and stops; the class has no getter. Its count is used once, at
`InterfaceSettings.java:343`, to size `Class151_Sub1.aStringArray4967`. So group 15 exists purely
as a length. Consistent with all 345 of its files being empty.

### Reader primitives used below

All from `RSBuffer.java`. Widths are exact and were confirmed by the consumption sweep.

| Name here | Client | Line | Bytes |
|---|---|---|---|
| `u8` | `readUnsignedByte` | `:896` | 1 |
| `s8` | `readSignedByte` | `:853` | 1 |
| `u16` | `readUnsignedShort` | `:901` | 2 |
| `s16` | `readShort` | `:820` | 2, sign-corrected |
| `u24` | `method1186` | `:131` | 3 |
| `i32` | `readInt` | `:753` | 4 |
| `str` | `readString` | `:878` | null-terminated |
| `gjstr2` | `method1223` | `:440` | leading version byte that **must be 0**, then null-terminated |

---

## 3. Exact-consumption result for every modelled group

Every group with a provider was decoded with the client's widths and the position compared to the
file length. **Measured: 11,679 files, 11,679 exact. Zero unknown opcodes, zero over-reads, zero
trailing bytes.** No width in the 637 client is wrong for 639 anywhere in index 2.

| Group | Files | Exact | Opcodes seen (count) | Distinct opcode orders | Ascending-order files |
|---|---|---|---|---|---|
| 1 | 159 | 159 | 1:159 2:159 3:72 4:3 5:5 | 5 | 158 / 159 |
| 3 | 652 | 652 | 1:652 2:652 3:13 40:12 60:248 | 5 | 382 / 652 |
| 4 | 235 | 235 | 1:233 3:194 5:58 7:38 8:1 9:46 11:234 12:118 13:12 14:12 16:5 | 44 | 116 / 235 |
| 5 | 609 | 609 | 2:609 | 1 | 609 / 609 |
| 11 | 1,330 | 1,330 | 1:442 2:383 4:81 5:59 | 6 | 1,286 / 1,330 |
| 16 | 2,002 | 2,002 | 5:9 | 2 | 2,002 / 2,002 |
| 19 | 1,445 | 1,445 | 1:1445 2:19 | 2 | 1,445 / 1,445 |
| 26 | 1,730 | 1,730 | 249:1182 | 2 | 1,730 / 1,730 |
| 31 | 4 | 4 | 1:4 2:4 3:4 4:4 | 1 | **0 / 4** |
| 32 | 1,972 | 1,972 | see §7 | 58 | 1,393 / 1,972 |
| 33 | 175 | 175 | 1:175 2:175 | 1 | 175 / 175 |
| 34 | 100 | 100 | 1:93 3:2 4:7 | 3 | 100 / 100 |
| 35 | 187 | 187 | see §8 | 15 | **3 / 187** |
| 36 | 1,051 | 1,051 | see §5 | 16 | **0 / 1,051** |
| 46 | 28 | 28 | 1:28 3:19 4:26 5:26 6:26 8:27 9:27 10:28 14:28 | 6 | **0 / 28** |

"Ascending-order files" is the count that could be re-encoded by walking opcodes 1..n. Everything
short of the file count in that column **requires an order-preserving encoder** of the
`FloorOverlayDefinition` shape (`FlashEditor/Definitions/FloorOverlayDefinition.cs:200-204`).

### A decompiler artefact you will meet, and the measurement that settles it

Three of these dispatchers contain `if(!client.aBoolean3553) break;` in the middle of an opcode
body (`Class231.java:147`, `Class90.java:75`, and inside `Class24`'s callers). `aBoolean3553` is
assigned `true` at exactly one site, `client.java:2842`, inside a shutdown path. It is JODE's
tail-merge join: two opcode bodies that shared a tail in bytecode were merged and guarded with an
opaque predicate. **Read the field as false during decode.**

For group 33 that decision is worth two bytes per record. `Class231.method2879:141-152` merged so
that opcode 2 appears to read `u8, u8` and then fall through into opcode 1's `u16`. It does not:
opcode 2 is `u8, u8`, and the sweep proves it - all 175 files consume exactly under that reading,
and every one of them carries both opcodes, so the alternative reading would over-read 350 bytes.

---

## 4. Build order

Cheapest and best-understood first. Each numbered item is one pass: definition class + `RSCache`
accessor + `DefinitionSweep` byte-identity test.

| # | Group(s) | Files | Why here |
|---|---|---|---|
| **1** | 2, 7, 18, 20, 21, 22, 23, 24, 25, 37-45, 48 | 5,302 | All empty. One shared "empty record" codec covers 19 groups in one pass. |
| **2** | 5 | 609 | One opcode, `u16`, no ordering, no repetition, no aliasing. |
| **3** | 16 | 2,002 | One opcode, `u16`, 9 non-empty files. |
| **4** | 19 | 1,445 | Two opcodes, both fixed, always ascending. |
| **5** | 15 | 345 | Empty, but has a provider - fold into pass 1's codec, note the provider. |
| **6** | 11 | 1,330 | Four fixed opcodes, one string. **Do this before 26 and 36** - it is the param type table they both key off. |
| **7** | 26 | 1,730 | One opcode (249), the param block. Reuses the object codec's 249 handling. |
| **8** | 33 | 175 | Two opcodes, fixed order. Watch the `aBoolean3553` trap. |
| **9** | 31 | 4 | Four opcodes, but **non-ascending in 4 of 4** - the first group that forces the order-preserving encoder. |
| **10** | 46 | 28 | 14 opcodes, small, includes `gjstr2`. Non-ascending in 28 of 28. |
| **11** | 3 | 652 | Two variable-length blocks, one discarded byte. |
| **12** | 36 | 1,051 | Fully settled in §5. Blocks nothing, but object opcode 107 and the world map want it. |
| **13** | 35 | 187 | 18 opcodes, six of which the client reads and discards. |
| **14** | 32 | 1,972 | 38 opcodes, 58 distinct orders, two repeaters. Largest single job. |
| - | 1, 4, 34 | 494 | Already done. Leave alone. |

Constants: `RSConstants.cs:60-63` declares `FLOOR_UNDERLAY_GROUP=1`, `FLOOR_OVERLAY_GROUP=4`,
`MAP_SCENE_GROUP=34`, `MAP_ELEMENT_GROUP=36`. Add one per group you model, in the same block.

Addressing: index 2 has no row in `CacheAddressing.TryGetFor`, and it should not get one - a config
group is a **type**, not a page of ids, so there is no arithmetic relating a definition id to a
group. Use `DefinitionAddress(groupId, fileId, definitionId: fileId)` and treat the group as the
type selector, exactly as `DefinitionListDescriptor` already allows.

---

## 5. Group 36 - MAP_ELEMENT. Settled.

**Decoder:** `Class24.method290` (`Class24.java:399-425`), dispatching to `method288`
(`:209-373`). Provider `Class341.method3807` (`Class341.java:169-205`), which also calls
`method291` (`:427-462`) afterwards - **that is a post-decode transform, not part of the format**
(it derives a bounding box from opcode 15's polygon into `anInt244/247/248/262`). Do not call it
from `Decode`, for the same reason `ApplyPriorityComposite` is not called from
`FloorOverlayDefinition.Decode`.

**Consumption: 1,051 files, 1,051 exact.** Measured.

### Opcode table

| Op | Payload | Client field | Line | Occurrences | Measured values |
|---|---|---|---|---|---|
| 1 | `u16` | `anInt245` | `:366` | 553 | 510..1784, 101 distinct |
| 2 | `u16` | `anInt225` | `:363` | 174 | 1774, 1775 only |
| 3 | `str` | `aString263` | `:360` | 605 | "Exit", "East Graveyard", "Soul Obelisk" |
| 4 | `u24` | `anInt257` | `:357` | 605 | 5 distinct, max 16777215 |
| 5 | `u24` | `anInt238` | `:354` | 0 | never occurs |
| 6 | `u8` | `anInt264` | `:351` | 605 | 0, 1, 2 |
| 7 | `u8` bitfield | bit 1 -> `aBoolean230=true`; bit 0 clear -> `aBoolean258=false` | `:340-348` | 360 | 2, 3 |
| 8 | `u8` | `aBoolean261 = (v==1)` | `:221` | 834 | 0, 1 |
| 9 | `u16,u16,i32,i32` | `anInt259, anInt260, anInt251, anInt227`; both shorts map 65535 -> -1 | `:222-236` | 312 | e.g. (5819, -1, 0, 0) |
| 10-14 | `str` each | `aStringArray237[op-10]` | `:237-238` | 177, 1, 0, 0, 0 | "Open", "Follow" |
| 15 | see below | polygon | `:311-337` | 69 | 4..11 vertices |
| 16 | none | `aBoolean241 = false` | `:241` | 0 | never occurs |
| 17 | `str` | `aString232` | `:309` | 177 | "Map" |
| 18 | `u16` | `anInt231` | `:244` | 0 | never occurs |
| 19 | `u16` | `anInt246` | `:306` | 968 | 948..2098, 88 distinct |
| 20 | `u16,u16,i32,i32` | `anInt240, anInt243, anInt254, anInt236`; both shorts map 65535 -> -1 | `:247-260` | 245 | e.g. (5332, -1, 0, 12) |
| 21 | `i32` | `anInt239` | `:303` | 176 | one value, -5276401 |
| 22 | `i32` | `anInt226` | `:300` | 178 | two values, -56 and 546687231 |
| 23 | `u8,u8,u8` | `anInt250, anInt253, anInt224` | `:295-297` | 0 | never occurs |
| 24 | `s16,s16` | `anInt235, anInt252` | `:265-266` | 0 | never occurs |
| 249 | param block | `aRSArray_256` | `:267-292` | **0** | never occurs |

Opcode 15's payload, in read order (`:311-337`):

```
n     = u8
pts   = s16 x (2n)      // x,y pairs -> anIntArray265
fill  = i32             // anInt249
m     = u8
edges = i32 x m         // anIntArray234
idx   = s8 x n          // aByteArray229
```

Measured over the 69 records that carry it: 4 to 11 vertices, coordinates -128..384, and the
remaining fields are constant across all 69 - `m` = 1, fill colour 867106815, the single edge
colour -16711681, and every one of the 344 edge indices is 0. So the edge-colour table is
exercised only in its degenerate one-entry form here.

### What the fields mean, settled from what the client does with them

- **Ops 1 and 2 are sprite ids in index 8.** `Class24.method287` (`:175-207`) picks
  `bool ? anInt225 : anInt245` and hands it to `Class324.method3685(aClass341_233.aJS5Archive_2852, ...)`;
  `aJS5Archive_2852` is the second archive `Class341` was constructed with, which
  `InterfaceSettings.java:273-274` gives as `Class332_Sub2.aJS5Archive_5423` = index 8. Corroborated:
  measured maximum is 1784 and index 8 declares 4,593 groups. The `bool` is
  `Node_Sub47.aBoolean4275`, i.e. op 2 is the highlighted/hovered variant (`Node_Sub40.java:116-119`).
- **Op 3 is the world-map label.** `Node_Sub40.java:154-158` breaks it into lines and draws it.
- **Op 6 selects the font for that label**: `Class105.method1718(anInt264, 5466)` at
  `Node_Sub40.java:155`. Values 0, 1, 2.
- **Op 4 is the label colour** (`Node_Sub40.java` / `Class103.java:88` / `Class126.java:47`), **op 5
  the colour used when a flag on the placement is set** (`Class126.java:48-49`). Op 5 never occurs.
- **Ops 9 and 20 are visibility gates.** `Class24.method284` (`:78-119`) evaluates them through
  `Interface6`. Its only implementation is `Class140`: `method7` (`:192-208`) resolves a **varbit**
  through `Class198` (index 22), `method6` (`:183-190`) reads a **varp** directly. So the first
  short is a varbit id, the second a varp id, and the two ints are an inclusive `[min, max]` the
  resolved value must fall in. Op 9 gates the world-map draw, op 20 the second condition.
- **Op 15 is a closed polygon in world-tile coordinates.** `Class278.method3314` (`:787-843`)
  offsets each `(x, y)` pair by the placement's world position, fills with `anInt249`, and draws
  each edge in `anIntArray234[aByteArray229[i]]`, wrapping the last vertex to the first.
- **Op 21 is a filled-rectangle colour** (`RenderType.method1781` at `Class103.java:81`), **op 22 an
  outline colour** (`method1760` at `Class103.java:77`). Both are signed 32-bit ARGB - the measured
  values are negative, so a `u32` field would round-trip but read wrong.
- **Op 23 is line width plus two line parameters** (`Class164.java:79`, `Class278.java:818,838`);
  when `anInt250 > 0` the polygon edges are drawn as thick lines instead of hairlines.
- **Op 24 is a label offset**, scaled into screen space at `Node_Sub40.java:159-161`.
- **Ops 10-14 are the right-click menu options**, walked 4 down to 0 by `Particle_Sub4.java:75-88`;
  **op 17 is the menu target**, passed alongside op 19 at `Particle_Sub4.java:78`.
- **CS2 reads five of these directly**: opcode 6800 returns op 3, 6801 returns op 1, 6802 returns
  op 6, 6803 returns op 19, and 6804 reads the op-249 param block
  (`Class247.java:7265-7318`).

### The two joins that depend on group 36

**Object opcode 107.** `Class352.java:1307-1308` reads it as `readUnsignedShort` into `anInt2958`.
`Class278.method3295` (`:55-100`) walks the world-map tile grid, resolves each tile's object id
through index 16, applies the object's varbit/varp morph first
(`class352.method3852(anInterface6_2060, ...)`), takes the resulting `anInt2958`, and if it is not
-1 constructs a `Node_Sub47` whose id **is the group-36 file id**. `Class202.java:228` and
`Class256_Sub1.java:54` then fetch the `Class24` back by it. The minimap does the same at
`Node_Sub10_Sub5.java:196-198`, gating on `aBoolean261` (op 8).

**Measured, by decoding all 56,199 index-16 object definitions:** opcode 107 occurs on **170**
objects with **144 distinct values** in the range **225..1028**, and **every one of them is a live
group-36 file id.** Zero dangling references. The same run confirms opcode 102 -> group 34: 3,267
objects, 82 distinct values, 0..100, all live group-34 file ids, and **opcode 68 occurs zero
times**, which independently corroborates the existing note that 102 is the map-scene opcode in
639.

**Index 23.** `Class278.method3298` (`:160-180`) is handed the index-23 archive *and* the group-36
provider together. It reads index 23's `"details"` group by name hash and decodes each file with
`Class48_Sub1.method457` (`:48-71`) into a `Node_Sub46_Sub10`. That record does **not** reference
group 36; the link is indirect, through the object ids the world-map tile data carries. So index
23 depends on group 36 only via index 16 opcode 107, and modelling group 36 unblocks the world map
without needing anything else from index 23.

### Group 36 hazards

- **Order: 0 of 1,051 files are in ascending opcode order.** 16 distinct orders. The most common,
  415 files, is `3, 6, 4, 8, 19`. An ascending encoder reproduces nothing.
- **Repetition: files 779 and 780 emit opcode 22 twice.** Keeping only the winning value gives a
  file of the right length and the wrong contents - the same shape as floor overlay 94's opcode 11.
- **Aliasing: ops 9 and 20 map stored 65535 to -1** in both short fields. -1 has exactly one
  encoding here, so this is safe *provided* the encoder writes 65535 back rather than -1 truncated.
- **Absent versus default:** `Class24`'s constructor (`:55-76`) sets non-zero defaults on 19 fields
  (`anInt245 = -1`, `anInt244 = 2147483647`, `aBoolean261 = true`, ...). Several of those defaults
  equal legal stored values, so "did this record carry op N" cannot be inferred from the value.
  Record the opcode hit map at decode.
- **Six opcodes never occur** (5, 16, 18, 23, 24, 249). Implement them from the client anyway - a
  sweep that passes says nothing about them - but do not expect them to be exercised.

---

## 6. The simple groups

### Group 5 - 609 files, 1 opcode

`Node_Sub46_Sub18.method1627` (`:14-29`).

| Op | Payload | Field |
|---|---|---|
| 2 | `u16` | `anInt6055` |

**609 of 609 exact.** Every file carries exactly opcode 2. Measured values 1..516.

**Settled by usage:** `Class156_Sub1.java:32,43` uses this number as the slot count of an item
container, comparing it against `Node_Sub3.itemIDS.length`; CS2 reads it at `Class247.java:2115`.
So group 5 is the container/inventory table and its one field is the capacity. No hazards: single
opcode, single order, no repetition.

### Group 16 - 2,002 files, 1 opcode

`Class167.method2530` (`:127-144`).

| Op | Payload | Field |
|---|---|---|
| 5 | `u16` | `anInt1283` |

**2,002 of 2,002 exact.** Only **9** files carry the opcode; measured `(file, value)` pairs:
`(166,1) (167,2) (168,3) (169,4) (170,5) (171,6) (173,7) (304,9) (872,10)`.

**Settled by usage:** `Class140`'s player-variable array is sized by this group's file count
(`Class140.java:49-50`, via `Class134.aClass139_3465.anInt1086`), and `Class140.method2288`
(`:120-135`) resets variable *i* on logout when `Class167.anInt1283 == 0`. So group 16 is the
varplayer table, one file per varp, and the single field is a non-reset marker. The client only
ever tests it against 0, so the specific values 1..10 are not settled by the client.

### Group 19 - 1,445 files, 2 opcodes

`Class90.method884` (`:68-93`).

| Op | Payload | Field | Line |
|---|---|---|---|
| 1 | `s8` -> char | `aChar720` via `Class64_Sub7.method576` (cp1252 byte -> char) | `:74` |
| 2 | none | `anInt718 = 0` | `:80` |

**1,445 of 1,445 exact**, all in ascending order. Opcode 1 on all 1,445, opcode 2 on 19.

Measured type letters: `i` 1287, `1` 58, `c` 41, `o` 28, `J` 8, `K` 7, `m` 5, `I` 2, `O` 2, `e` 2,
`g` 2, `d` 1, `n` 1, `0x80` 1.

**Settled by usage:** the file count sizes the client-variable stores at
`InterfaceSettings.java:342,344`; `:346-350` marks variable *i* as settable when `anInt718 == 0`;
`Class31.java:26-32` refuses a server update unless that mark is set, and additionally clamps a
value to -1..1 when the type letter is `'1'` (49). So group 19 is the client-variable table, op 1
is its type letter and op 2 its "server may not set this" flag. Note the type letter is stored as
one raw byte and passed through a cp1252 remap for bytes 0x80-0x9F - **keep the raw byte**, one
record in this cache stores 0x80.

### Group 33 - 175 files, 2 opcodes

`Class231.method2879` (`:137-158`).

| Op | Payload | Field | Line |
|---|---|---|---|
| 1 | `u16` | `anInt1735` | `:151` |
| 2 | `u8, u8` | `anInt1738, anInt1736` | `:145-146` |

**175 of 175 exact**, single opcode order `1, 2` in every file.

**Settled by usage:** `RSFont.java:82-95` takes `anInt1735` as a sprite id in index 8, loads it,
and passes it with `new Point(anInt1738, anInt1736)` to `Signlink.method872` - the platform
custom-cursor call. So group 33 is the cursor table: op 1 the sprite, op 2 the hotspot.
Measured sprite ids 168..4027 (172 distinct); hotspots are `(5,0)` on 136 records, `(0,0)` on 37,
`(6,0)` and `(11,4)` on one each.

**Hazard:** the `aBoolean3553` tail merge described in §3. Read op 2 as two bytes.

### Group 11 - 1,330 files, 4 opcodes. Do this before 26 and 36.

`Class149.method2434` (`:104-126`).

| Op | Payload | Field | Line |
|---|---|---|---|
| 1 | `s8` -> char | `aChar1201` via `Class64_Sub7.method576` | `:109` |
| 2 | `i32` | `anInt1202` | `:111` |
| 4 | none | `aBoolean1204 = false` | `:113` |
| 5 | `str` | `aString1203` | `:118` |

**1,330 of 1,330 exact.** 851 are empty. Order is non-ascending in 44 files.

Measured type letters: `i` 228, `s` 59, `S` 27, `d` 19, `o` 18, `J` 15, `c` 15, `g` 12, `A` 8,
`I` 8, `K` 8, `m` 6, `1` 5, `n` 3, `O` 3, `l` 2, and one each of `@ P t v y 0x80`.

**This is the parameter type table, and the join proves itself.** `Class149.isString`
(`:92-102`) returns `aChar1201 == 115`, i.e. `'s'`. CS2 opcode 6804 (`Class247.java:7304-7320`)
looks a group-11 record up by the same integer key the op-249 param block stores, and uses
`isString()` to decide whether to pull a string or an int out of that block, falling back to op 5
or op 2 as the default.

**Measured cross-check, and it is exact:** group 26's 1,730 records carry **12,269** op-249 param
entries using **232 distinct keys**. Every one of those 232 keys is a live group-11 file id (zero
outside), and the per-entry string/int flag agrees with the keyed record's type letter (`'s'`
versus anything else) on **all 12,269 entries**. That is a self-proving join at 100% coverage, not
an aggregate that merely looks plausible.

### Group 26 - 1,730 files, 1 opcode

`InterfaceConfig.method1589` (`:103-136`).

| Op | Payload |
|---|---|
| 249 | `n = u8`, then n x (`u8` isString, `u24` key, then `str` if isString else `i32`) |

**1,730 of 1,730 exact.** 548 are empty; 1,182 carry the block.

This is byte-for-byte the same param block the object decoder already handles
(`ObjectDefinition.cs:758-772`), so reuse that code path. Read by CS2 at
`Class247.java:3788-3795` via `getString`/`getInteger`.

**Hazard:** the client's `insertElement` keeps the *first* occurrence of a duplicate key
(`InterfaceConfig.java:125`), and the project's object codec keeps the first too
(`if (!parameters.ContainsKey(key))`). Preserve the raw entry list in order rather than a
dictionary, or a duplicate key is dropped on re-encode.

### Group 31 - 4 files, 4 opcodes

`Class379.method4009` (`:58-81`).

| Op | Payload | Field | Line |
|---|---|---|---|
| 1 | `u8` | `anInt3195` | `:65` |
| 2 | `u16` | `anInt3197` | `:75` |
| 3 | `u16` | `anInt3194` | `:72` |
| 4 | `s16` | `anInt3193` | `:69` |

**4 of 4 exact.** All four records carry all four opcodes, and **the order is `3, 2, 4, 1` in every
one** - non-ascending in 4 of 4. This is the cheapest possible test case for the order-preserving
encoder; do it before group 32.

Consumers pass all four to `Class1.method166` from a particle/effect stream when a marker equals 31
(`Class305_Sub1.java:296-299` and `:510-513`). Four records is not enough usage to name the fields
and I am not naming them.

### Group 3 - 652 files

`Class152.method2479` (`:220-263`).

| Op | Payload | Field | Line |
|---|---|---|---|
| 1 | `u8` | **read and discarded** | `:257` |
| 2 | `n = u8`, then n x `u16` | `anIntArray1218` | `:250-254` |
| 3 | none | - | `:226` |
| 40 | `n = u8`, then n x (`u16`, `u16`) | `aShortArray1219 / aShortArray1217` (recolour) | `:227-234` |
| 41 | `n = u8`, then n x (`u16`, `u16`) | `aShortArray1224 / aShortArray1223` (retexture) | `:239-246` |
| 60-69 | `u16` | `anIntArray1222[op-60]` | `:236-238` |

**652 of 652 exact.** Measured: op 1 on all 652 (values 0..13), op 2 on all 652 (1 model in 647
records, 2 in 5), op 3 on 13, op 40 on 12, **op 60 on 248 and ops 61-69 on none**.

**Settled by usage:** the only consumer is `PlayerAppearance` (`:811, 827, 1167, 1195, 1360`), which
builds a player body model from `anIntArray1218` (`method2473`) or from the five `anIntArray1222`
slots (`method2476`), applying the recolour and retexture tables. So group 3 is the player
body-part model table.

**Two hazards.**
1. **Opcode 1's byte is read and thrown away.** The decoder must keep it verbatim or the re-encode
   loses it. Measured values 0..13, so it is not constant.
2. **`anIntArray1222` is `int[5]`** (`Class152.java:87`) but the dispatcher accepts opcodes 60..69
   (`(i^-1) <= -61 && (i^-1) > -71`). Opcodes 65-69 would throw `ArrayIndexOutOfBounds` in the
   client. They never occur here - only op 60 does - so this is a latent client defect, not a live
   one. **Do not "fix" it by widening the array silently; record it.**

---

## 7. Group 32 - 1,972 files, 38 opcodes. The largest job.

`Class294.method3476` (`Class294.java:196-418`).

| Op | Payload | Field | Line |
|---|---|---|---|
| 1 | `u16, u16` | `anInt2396, anInt2399`, each 65535 -> -1 | `:396-408` |
| 2 | `u16` | `anInt2368` | `:393` |
| 3 | `u16` | `anInt2394` | `:390` |
| 4 | `u16` | `anInt2377` | `:387` |
| 5 | `u16` | `anInt2403` | `:384` |
| 6 | `u16` | `anInt2389` | `:207` |
| 7 | `u16` | `anInt2361` | `:209` |
| 8 | `u16` | `anInt2357` | `:381` |
| 9 | `u16` | `anInt2402` | `:378` |
| 26 | `u8, u8` | `anInt2362 = v*4`, `anInt2382 = v*4` | `:213-214` |
| 27 | `u8` index, then 6 x `s16` | `anIntArrayArray2366[idx]` | `:215-226` |
| 28 | 12 x `u8` | `anIntArray2379`, 255 -> -1 | `:227-236` |
| 29 | `u8` | `anInt2398` | `:238` |
| 30 | `u16` | `anInt2383` | `:375` |
| 31 | `u8` | `anInt2390` | `:241` |
| 32 | `u16` | `anInt2392` | `:372` |
| 33 | `s16` | `anInt2393` | `:244` |
| 34 | `u8` | `anInt2375` | `:369` |
| 35 | `u16` | `anInt2380` | `:366` |
| 36 | `s16` | `anInt2363` | `:363` |
| 37 | `u8` | `anInt2401` | `:249` |
| 38 | `u16` | `anInt2376` | `:251` |
| 39 | `u16` | `anInt2388` | `:360` |
| 40 | `u16` | `anInt2365` | `:356` |
| 41 | `u16` | `anInt2359` | `:352` |
| 42 | `u16` | `anInt2372` | `:348` |
| 43 | `u16` | `anInt2381` | `:344` |
| 44 | `u16` | `anInt2374` | `:258` |
| 45 | `u16` | `anInt2385` | `:262` |
| 46 | `u16` | `anInt2405` | `:340` |
| 47 | `u16` | `anInt2404` | `:336` |
| 48 | `u16` | `anInt2384` | `:268` |
| 49 | `u16` | `anInt2370` | `:272` |
| 50 | `u16` | `anInt2378` | `:275` |
| 51 | `u16` | `anInt2369` | `:332` |
| 52 | `n = u8`, then n x (`u16`, `u8`) | `anIntArray2395` ids, `anIntArray2386` weights | `:314-330` |
| 53 | none | `aBoolean2400 = false` | `:280` |
| 54 | `u8, u8` | `anInt2360 = v<<6`, `anInt2391 = v<<6` | `:283-286` |
| 55 | `u8` index, `u16` | `anIntArray2373[idx]` | `:293-297` |
| 56 | `u8` index, then 3 x `s16` | `anIntArrayArray2364[idx]` | `:303-312` |

**1,972 of 1,972 exact.** Measured occurrences: 1:1972, 2:23, 3:2, 4:2, 5:2, 6:161, 7:16, 8:17,
9:17, 26:153, 38:248, 39:248, 40:345, 41:366, 42:361, 46:1, 47:1, 48:1, 49:1, 50:44, 51:44, 52:54,
54:16. **Opcodes 27, 28, 29, 30, 31, 32, 33, 34, 35, 36, 37, 43, 44, 45, 53, 55 and 56 never
occur** - implement them from the client, expect no sweep coverage.

**What it is, as far as the client settles it:** every player and NPC resolves one of these through
`Particle_Sub3_Sub4_Sub2.method3039` (`:828-844`) -> `Class257.method3199`. It is the per-mobile
render configuration. `Class294.method3478` (`:420-444`) returns `anInt2396` (opcode 1's first
short) if set, else picks from opcode 52's id list weighted by its byte weights;
`Class284_Sub1_Sub2.java:38-39` and `Player.java:438` store the result on the mobile and check
membership with `method3480`. That much is settled; the other 36 fields are not, and I am not
naming them.

**Hazards.**
- **579 of 1,972 files are in non-ascending opcode order**, across 58 distinct orders.
- **Two files repeat opcodes.** File 1205: `38, 39, 40, 41, 42, 6, 6, 8, 9, 1` (opcode 6 twice).
  File 1799: `38, 39, 38, 39, 40, 41, 42, 6, 1` (opcodes 38 and 39 each twice, interleaved).
  Decoding to values alone loses both.
- **Ops 26 and 54 store a scaled value** (`*4`, `<<6`). Both are injective over a byte, so keeping
  the decoded value is recoverable - but keeping the raw byte is cheaper and cannot be wrong.
- **Ops 27, 55 and 56 are indexed writes into fixed 12-slot arrays.** Two records writing the same
  index would silently collapse if you store the array rather than the record list. Neither opcode
  occurs here, so a sweep cannot catch it.

---

## 8. Group 35 - 187 files, 18 opcodes

`Class220.method2818` (`Class220.java:79-208`).

| Op | Payload | Field | Line |
|---|---|---|---|
| 1 | `gjstr2` | `aString1663` | `:201` |
| 2 | `gjstr2` | `aString1654` | `:198` |
| 3 | `n = u8`, then n x (`u16`, `i32`, `i32`) | `anIntArrayArray1659` | `:189-195` |
| 4 | `n = u8`, then n x (`u16`, `i32`, `i32`) | `anIntArrayArray1648` | `:86-92` |
| 5 | `u16` | **read and discarded** | `:186` |
| 6 | `u8` | **read and discarded** | `:183` |
| 7 | `u8` | **read and discarded** | `:180` |
| 8 | none | - | `:96` |
| 9 | `u8` | **read and discarded** | `:176` |
| 10 | `n = u8`, then n x `i32` | `anIntArray1652` | `:99-104` |
| 12 | `i32` | **read and discarded** | `:173` |
| 13 | `n = u8`, then n x `u16` | `anIntArray1651` | `:107-112` |
| 14 | `n = u8`, then n x (`u8`, `u8`) | `anIntArrayArray1658` | `:114-119` |
| 15 | `u16` | **read and discarded** | `:170` |
| 17 | `u16` | `anInt1649` | `:122` |
| 18 | `n = u8`, then n x (`i32`, `i32`, `i32`, `str`) | `anIntArray1647/1653/1661`, `aStringArray1662` | `:124-135` |
| 19 | `n = u8`, then n x (`i32`, `i32`, `i32`, `str`) | `anIntArray1646/1655/1660`, `aStringArray1656` | `:156-167` |
| 249 | param block | `aRSArray_1650` | `:137-154` |

Note **opcode 11 and 16 are absent from the dispatcher entirely** - they fall through with no
payload consumed, the same client defect floor overlay opcodes 4, 6 and 15 have. Our decoders
throw on an unknown opcode (`FloorOverlayDefinition.cs:147-151`); keep that convention.

**187 of 187 exact.** Measured occurrences: 1:187, 2:4, 3:73, 4:113, 5:9, 6:5, 7:5, 8:3, 9:183,
10:1, 13:1, 14:2, 17:21, 249:1. Opcodes 12, 15, 18, 19 never occur.

**Partly settled by usage:** the only field the client reads back is `anInt1649` (op 17), which
`Class64_Sub25.method653` (`:9-38`) treats as a sprite id in index 8 and turns into an inline
`<img=n>` tag in a chat string. The rest of the record is decoded and never read by this client.
**Do not name the other fields.**

**Hazards.**
- **184 of 187 files are in non-ascending order**, 15 distinct orders. The most common is `9, 1, 4`.
- **Six opcodes are read and discarded** (5, 6, 7, 9, 12, 15). Their bytes must be kept verbatim -
  there is no field to reconstruct them from. Opcodes 5, 6, 7 and 9 all occur in real records.
- Ops 1 and 2 are `gjstr2`, so the leading zero version byte is part of the payload and must be
  written back.

---

## 9. Group 46 - 28 files, 14 opcodes

`Class86.method841` (`Class86.java:143-192`).

| Op | Payload | Field | Line |
|---|---|---|---|
| 1 | `u16` | `anInt655` | `:148` |
| 2 | `u24` | `anInt648` | `:150` |
| 3 | `u16` | `anInt641` | `:152` |
| 4 | `u16` | `anInt643` | `:154` |
| 5 | `u16` | `anInt652` | `:156` |
| 6 | `u16` | `anInt647` | `:180` |
| 7 | `s16` | `anInt653` | `:177` |
| 8 | `gjstr2` | `aString654` | `:174` |
| 9 | `u16` | `anInt651` | `:161` |
| 10 | `s16` | `anInt650` | `:163` |
| 11 | none | `anInt645 = 0` | `:165` |
| 12 | `u8` | `anInt642` | `:167` |
| 13 | `s16` | `anInt646` | `:169` |
| 14 | `u16` | `anInt645` | `:171` |

**28 of 28 exact.** Measured occurrences: 1:28, 3:19, 4:26, 5:26, 6:26, 8:27, 9:27, 10:28, 14:28.
Opcodes 2, 7, 11, 12, 13 never occur.

**Settled by usage:** ops 3, 4, 5 and 6 are sprite ids loaded through
`Class86.method847` (`:215-252`) from `aClass121_644.aJS5Archive_1005`, which
`InterfaceSettings.java:265-266` gives as `Class332_Sub2.aJS5Archive_5423` = index 8.
`Class86.method848` (`:254-273`) substitutes a formatted number into every `"%1"` in op 8's string,
and **op 8's stored value is literally `"%1"` in all 27 records that carry it** (measured). The
records are fetched per hit on a mobile at `IntegerNode.java:344,355`, where `anInt651` (op 9) is
subtracted from a deadline timestamp. So group 46 is the damage-mark table: four sprite variants,
a number template, and a lifetime.

**Hazards.** Non-ascending in **28 of 28** files, 6 distinct orders. Op 8 is `gjstr2` - keep the
version byte. Ops 7, 10 and 13 are signed.

---

## 10. Groups 1, 4 and 34 - already modelled, recorded for completeness

These three have a decoder, an encoder and a byte-identity sweep already
(`FloorUnderlayDefinition.cs`, `FloorOverlayDefinition.cs`, `MapSceneIconDefinition.cs`). The
tables are here so index 2 is documented end to end; **do not rebuild them.**

### Group 1 - floor underlay, 159 files

`FloorUnderlay.method716` (`FloorUnderlay.java:21-50`).

| Op | Payload | Field | Line | Occurrences |
|---|---|---|---|---|
| 1 | `u24` | `anInt539`, decomposed to HSL by `method718` | `:39-42` | 159 |
| 2 | `u16`, 65535 -> -1 | `anInt537` | `:25-29` | 159 |
| 3 | `u16 << 2` | `anInt536` | `:30-31` | 72 |
| 4 | none | `aBoolean544 = false` | `:36-38` | 3 |
| 5 | none | `aBoolean543 = false` | `:33-35` | 5 |

**159 of 159 exact.** One file out of ascending order; no repetition. The HSL decomposition at
`FloorUnderlay.java:112-134` is a post-decode transform - the project keeps raw RGB, correctly.

### Group 4 - floor overlay, 235 files

`FloorOverlayConfig.method2689` (`FloorOverlayConfig.java:122-168`).

| Op | Payload | Field | Line | Occurrences |
|---|---|---|---|---|
| 1 | `u24` | `anInt1537` | `:158-159` | 233 |
| 2 | `u8` | `anInt1542` | `:126-127` | **0** |
| 3 | `u16`, 65535 -> -1 | `anInt1542` | `:128-132` | 194 |
| 5 | none | `aBoolean1527 = false` | `:155-156` | 58 |
| 7 | `u24` | `anInt1540` | `:134-135` | 38 |
| 8 | none | copies `anInt1536` into the provider | `:136-137` | 1 |
| 9 | `u16 << 2` | `anInt1529` | `:138-139` | 46 |
| 10 | none | `aBoolean1538 = false` | `:140-141` | **0** |
| 11 | `u8` | `anInt1535` | `:142-143` | 234 |
| 12 | none | `aBoolean1526 = true` | `:144-145` | 118 |
| 13 | `u24` | `anInt1532` | `:146-147` | 12 |
| 14 | `u8 << 2` | `anInt1530` | `:152-153` | 12 |
| 16 | `u8` | `anInt1534` | `:149-151` | 5 |

**235 of 235 exact.** Opcodes 4, 6 and 15 fall through the client's dispatcher without consuming a
payload - the known client defect the project deliberately diverges from. **Opcodes 2 and 10 occur
zero times**, so the aliasing between opcodes 2 and 3 (both writing the texture id, at different
widths) is real in the format and untested by the data. 119 files are out of ascending order; file
94 repeats opcode 11.

### Group 34 - map scene icon, 100 files

`Class9.method193` (`Class9.java:233-258`).

| Op | Payload | Field | Line | Occurrences |
|---|---|---|---|---|
| 1 | `u16` | `anInt114` - sprite group id in index 8 | `:240-241` | 93 |
| 2 | `u24` | `anInt115` | `:242-243` | **0** |
| 3 | none | `aBoolean116 = true` | `:244-245` | 2 |
| 4 | none | `anInt114 = -1` | `:247-250` | 7 |

**100 of 100 exact**, all ascending, no repetition. Opcode 1's value is a sprite **group** id whose
file 0 is drawn (`Class324.method3685(aClass335_117.aJS5Archive_2814, anInt114, i)` at
`Class9.java:183`); `aJS5Archive_2814` is the 4th constructor argument of `Class335`
(`Class335.java:60`), which `InterfaceSettings.java:275-276` gives as index 8.

**Aliasing hazard:** opcode 4 sets `anInt114` to -1, the same value the constructor leaves it at,
and 7 records carry it. "No icon" therefore has two encodings - absent, and opcode 4 - and they are
not interchangeable on re-encode.

---

## 11. The 19 provider-less groups

```
2  7  18  20  21  22  23  24  25  37  38  39  40  41  42  43  44  45  48
```

5,302 files. **Every one is `0x00`.** Measured: minimum length 1, maximum length 1, last byte 0,
first byte 0, in all 5,302.

What that establishes, and nothing further:

- The record **is** opcode-terminated - a bare terminator is a valid empty record under the same
  loop every other index-2 type uses.
- Consumption is exact for all 5,302 under that loop.
- **The opcode set cannot be recovered from this cache.** There is no byte to reverse engineer.
  Any opcode table for these groups would have to come from a different build's client or a
  different cache, and would be unverifiable here.
- **No field in any of them can be named.** Do not name one.

Three of them (38, 39, 48) hold exactly one file; group 40 holds two. Groups 38, 39, 40 and 48 are
the only index-2 containers stored uncompressed.

Build them as one shared codec: decode the loop, throw on any opcode, record the (empty) opcode
stream, encode it back. One `DefinitionSweep` per group, or one parameterised over the 19, each
asserting its own file count and byte identity. The assertion that earns its keep is "this group is
still empty" - stated as byte identity, it survives a cache that starts filling them in, because
the decoder throws rather than guessing.

---

## 12. Non-canonical hazards, collected

The project has been bitten by all four categories. Per group:

| Hazard | Groups affected |
|---|---|
| **Opcode order** varies within a group | 1 (1 file), 3 (270), 4 (119), 11 (44), 31 (**4 of 4**), 32 (579), 35 (184), 36 (**1,051 of 1,051**), 46 (**28 of 28**) |
| **Opcode repetition** | 4 (file 94, op 11 twice - known), 32 (files 1205 and 1799), 36 (files 779 and 780, op 22 twice) |
| **Aliased values** | 4 (ops 2 and 3 both write the texture id at different widths; op 2 never occurs, so the byte form is untested), 1 op 2 / 4 op 3 / 32 op 1 / 36 ops 9 and 20 (65535 -> -1), 32 ops 26 and 54 (scaled on read) |
| **Absent versus default** | Every group whose record class sets non-zero constructor defaults: 4, 32 (`Class294:132-174`, 38 fields), 36 (`Class24:55-76`, 19 fields), 46 (`Class86:129-141`, 10 fields), 31, 35. Record the opcode hit map; never infer presence from the value. |
| **Read-and-discarded bytes** | 3 op 1 (occurs on all 652 files), 35 ops 5, 6, 7, 9, 12, 15 (four of the six occur) |
| **`gjstr2` version byte** | 35 ops 1, 2; 46 op 8 |

Two structural notes that apply everywhere in index 2:

- **Nothing in index 2 is XTEA encrypted.** Only index 5's `l` groups are.
- **23 of 35 containers are GZip.** Compare decompressed payloads, never stored containers.

---

## 13. Corrections to `reference/index-survey/index-002-CONFIG.md`

- "**18 groups have a locatable provider and 17 do not**" is wrong on both halves. 18 providers
  exist but two of them (29, 30) name groups absent from this cache; over the 35 groups that do
  exist it is **16 with a provider, 19 without**. The survey's own enumeration of provider-less
  groups already lists 19.
- The survey does **not** record that 19 of those groups are entirely empty, which is the single
  most consequential fact about them. It frames them as "must be reverse-engineered from the
  bytes"; there are no bytes.
- The survey marks group 36 as unverified and warns that `Class341.method3807`'s "// Map Loading"
  comment is unreliable. The comment happens to be roughly right, but §5 settles the group from
  usage rather than from the comment, and the warning was correct to issue.
- Group 15 is listed as a provider group; the provider reads no file and the group is empty.
- Group 16's record is described as "consistent with VarPlayer but not proven by the client's
  usage". It is now proven by usage: `Class140.java:49-50,120-135`.
- The survey's group:file list matches the reference table exactly. Confirmed independently.

## 14. Where the numbers came from

| Claim | Method |
|---|---|
| 35 groups, 16,981 files, group id list, file id gaps | Parsed idx255 group 2 |
| Container compression, stored and payload sizes | Unwrapped each of the 35 containers |
| 8,694 single-byte records; 19 all-empty groups | Walked all 16,981 files |
| 11,679 of 11,679 exact consumption | Decoded each modelled group with the client's widths |
| Opcode occurrence counts, order counts, repeats | Same walk |
| Field value ranges (group 36, 5, 16, 19, 33, 3, 46) | Same walk, capturing values |
| 12,269 param entries, 232 keys, 0 outside group 11, 0 type disagreements | Cross-joined group 26's op-249 blocks against group 11's type letters |
| Object opcode 107: 170 objects, 144 values, 225..1028, 0 dangling | Decoded all 56,199 index-16 files; all 56,199 consumed exactly |
| Object opcode 102: 3,267 objects, all live group-34 ids; opcode 68: 0 | Same run |
| Index 8 has 4,593 groups | Parsed idx255 group 8 |
