# Index 3 - INTERFACE_DEFINITIONS: codec architecture

**Status:** format settled end to end. Nothing is left guessed on the read path.

This note is written so the codec can be built from it without reopening the client. Every
claim is either a `file:line` in the bundled 637 client
(`C:\Users\CJ\Desktop\RSPS\Hydra\Client\src`) or a measurement over the 639 reference cache.
Where the two are both quoted, they were established independently.

Measurements were taken with a Python transcription of `RSInterface.unpackConfig` reading the
real dat2, not with this project's C# (which decodes nothing here yet), so nothing below is
this decoder agreeing with itself. Scripts:
`<session scratchpad>\if3sweep.py`, `if3stats.py`, `if3phantom.py`, `if3phantom2.py`, `if3names.py`,
`if3names2.py`, `if3crack.py`.

---

## 1. The falsification, run first

**42,256 of 42,256 component files consume exactly.**

The 637 `unpackConfig` read order, transcribed field for field with no tolerance and no
trailing slack, lands on the last byte of every declared component file in the 639 cache.
Zero short reads, zero overruns, zero exceptions.

The three groups the reference table does not declare (section 3) add **89 more, all exact**,
for 42,345 of 42,345.

So the 637 order is the 639 order. Nothing downstream needs re-planning, and an
exact-consumption assertion is a legitimate test rather than an aspiration.

Corroborating shape, same sweep:

| | |
|---|---|
| Component files, declared groups | 42,256 in 1,078 groups |
| Smallest / median / largest component | 60 / 74 / 1,864 bytes |
| Total decoded payload | 3,550,506 bytes |
| Groups holding exactly one file | 83 |
| Largest group | group 947, 771 files |
| Files starting with a byte other than `0xFF` | 0 |

---

## 2. Addressing

A group is one interface. A file is one component. The client folds the pair into a single
component id: `ID_TAG = (parent << 16) + childIndex` (`EntityEnumType.java:46`), and takes it
apart again the same way in the CS2 dispatcher, `child = stack >> 16; sub_child = stack &
0xFFFF` (`Class247.java:412-413`).

That is exactly `CacheAddressing.Paged(16)`, the same shape index 0 already uses, and index 3
has **no row in `CacheAddressing.TryGetFor`** today. Add one, citing `EntityEnumType.java:46`.
It fits: the largest group holds 771 files, far inside the 65,536-id page.

Enumeration rules:

- **Group ids are sparse.** 1,078 groups over ids 0..1,080. Enumerate from the reference
  table, never `for (g = 0; g <= 1080; g++)`.
- **File ids are dense, 0..n-1, in every group.** That is what makes the client's
  `childIndex == fileId` assumption safe, and the client relies on it: `VersionTable.java:183`
  stores `maxFileId + 1` as the group's file count and `:185` throws the explicit id list away
  whenever the two agree. Do not depend on it in the encoder anyway - read the ids from the
  table.
- Every group is **single chunk**, so the chunk-major layout trap does not arise here.
  Compression is mixed: 953 GZip groups, 125 BZip2, 0 uncompressed.
- **No XTEA anywhere.** `getChildFromFolder` passes null keys
  (`JS5Archive.java:190-192` -> `getDecryptedFile(null, 5, fileId, groupId)`). The `false` in
  `openFileStore(-126, false, 1, 3)` at `InterfaceSettings.java:161` is **not** an encryption
  flag - it is `aBoolean1570`, a discard-unpacked caching switch (`JS5Archive.java:195, 365,
  917`). Index 6 is opened with `true` and is not encrypted either. Do not cite that argument
  as evidence about XTEA.

---

## 3. The three groups the reference table does not declare

`idx3` holds 1,081 live records; the table declares 1,078. Ids **772, 825 and 891** have a
real length and sector in `idx3`, and their sector chains decode cleanly.

| Group | Container | Stored | Payload | Files | Component sizes |
|---|---|---|---|---|---|
| 772 | GZip | 323 B | 1,221 B | 14 | 117, 74 x 6, 86 x 3, 82, 115, 81, 67 |
| 825 | GZip | 402 B | 2,262 B | 32 | 61 x 10, 72, 83, 60 x 3, 74 x 4, ... |
| 891 | BZip2 | 1,052 B | 4,112 B | 43 | 60, 71, 89, 85, 170, 60, 60, 96, ... |

The file counts are not in any table, so they were recovered by brute force: for each
candidate count, require that the size trailer parses, that the deltas sum to the body length,
and that **every** resulting file consumes exactly under `unpackConfig`. Exactly one count
satisfies that for each group - 14, 32 and 43 - and all 89 components decode exactly. They are
intact interfaces, not garbage.

**Do they matter?** Three separate answers, and they differ:

1. **To the client, no.** `VersionTable.java:135` sizes its arrays to `maxGroupId + 1` and
   leaves `anIntArray2671[g] == 0` for any id the table does not declare;
   `JS5Archive.method2758:1035` rejects exactly that case, so `loadInterfaceIfExists` returns false
   and the client can never load them. They are dead weight in the running game.
2. **To the current write path, no.** `RSFileStore` mutates the `idx3` stream that was read
   from disk and writes it back whole (`RSFileStore.SaveTo`), so the three records survive a
   save; and sector allocation only ever appends
   (`nextFreeSector = dataChannel.Length / RSSector.SIZE`, `RSFileStore.cs:172`), so their
   sectors cannot be reused. Nothing today endangers them.
3. **To an implementer, yes - as a trap.** Any enumeration that walks `idx3` slots rather than
   the table will surface three groups whose file count is unknown, and a lookup keyed on the
   table will throw on them. Enumerate from the table (`RSCache.EnumerateGroups`), and if a
   diagnostic wants to show them, recover the file count the way above rather than guessing.

They are almost certainly the residue of a repack: this cache is a 639 base with local
modifications, and removing a group from the table without zeroing its `idx` record leaves
precisely this.

---

## 4. The wire format, in read order

Source: `RSInterface.unpackConfig`, `RSInterface.java:1032-1343`. `unpackConfig` is called as
`unpackConfig(new RSBuffer(is), -947)` (`EntityEnumType.java:54`); every arithmetic use of that
second argument inside the method evaluates to 0 (`i + 947`, `i ^ i`, `i ^ ~0x3b2` with
`~0x3b2 == -947`). It is obfuscation noise. Ignore it.

`i_2_` below is the version byte. Read section 5 before implementing any branch that mentions
it.

### 4.1 Header

| # | Reader | Client field | Meaning, and how it was settled | Measured |
|---|---|---|---|---|
| 1 | u8 | `i_2_` | Version. `255` maps to `-1` (`:1035-1039`). | `0xFF` on all 42,256 |
| 2 | u8 | `contentType` | **The component type.** Bit `0x80` means a name string follows; the branch masks it off and reads a NUL string (`:1043-1046`). | see 4.3; `0x80` set on 0 files |
| 2a | NUL string | `aString2231` | Only when `0x80` was set. CS2 6702 takes the substring before `':'` from it (`Class247.java:7051-7056`), so it is a colon-delimited authoring name. | never present |
| 3 | **u16** | `x` | **`contentType` in Jagex's sense** - an enum-like id the client compares against a table of constants and against 0 (`client.java:897,916,948`, `Node_Sub10_Sub24.java:114,213,250`). It is *not* a coordinate. | 0 on 42,235; 21 files carry 328, 1337-1339 or 1400-1407 |
| 4 | **i16** | `y` | **basePositionX.** CS2 `if_setposition` (opcode 1000) writes `y` from its first argument (`Class247.java:420-423`). | -25,945 .. 13,926 |
| 5 | **i16** | `width` | **basePositionY.** Same opcode, second argument (`Class247.java:423`). | -22,165 .. 13,107 |
| 6 | **u16** | `height` | **baseWidth.** CS2 `if_setsize` (opcode 1001) writes `height` from its first argument (`Class247.java:453-455`), and `Class253.method3180:319-329` resolves the drawn width from it. | 0 .. 64,836 |
| 7 | **u16** | `anInt2242` | **baseHeight.** Same opcode, second argument (`Class247.java:456`); `Class253.method3180:333-343` resolves the drawn height from it. | 0 .. 64,732 |
| 8 | i8 | `aByte2243` | **widthMode.** Opcode 1001 arg 3, clamped 0..4 (`Class247.java:461-476`); consumed at `Class253.java:319`. | 0 (37,072), 1 (2,933), 2 (2,251) |
| 9 | i8 | `aByte2207` | **heightMode.** Opcode 1001 arg 4, clamped 0..4 (`Class247.java:461-477`); consumed at `Class253.java:333`. | 0 (38,045), 1 (2,185), 2 (2,026) |
| 10 | i8 | `aByte2240` | **xMode.** Opcode 1000 arg 3, clamped 0..**5** (`Class247.java:425-441`); consumed at `KeyStroke.java:32-38`. | 0..5 |
| 11 | i8 | `aByte2245` | **yMode.** Opcode 1000 arg 4, clamped 0..5 (`Class247.java:426-442`); consumed at `KeyStroke.java:13-19`. | 0..5 |
| 12 | u16 | `parentID` | Parent component. `65535` -> `-1`; otherwise `parentID + (ID_TAG & ~0xffff)`, i.e. the group id is folded into the high half (`:1057-1063`). | 8,413 roots, 33,843 children, **0 out of range** |
| 13 | u8 | `settingsFlags` | bit 0 = `isHidden` (`:1071`). Bit 1 is read **only** when `i_2_ >= 0` (`:1067-1069`). Bits 2-7 are read by nothing. | only 0 (39,512) and 1 (2,744) |

**The field names in the decompiled client are shifted by one position relative to the read
order.** `x` is the content type, `y` is X, `width` is Y, `height` is width, `anInt2242` is
height. This is the "settle it from what the client does, never from the identifier" rule
paying out: naming from the identifiers gives you an unsigned X with a signed Y and a signed
width with an unsigned height, which is incoherent. Under the corrected mapping the
signedness is exactly right, and the data proves it independently - the two signed reads are
the two fields that go negative in this cache, and the two unsigned reads are the two that
exceed 32,767.

### 4.2 Type-specific block

Read only for the six types below; every other type value reads nothing here.

**Type 0 - layer/container** (`:1073-1080`)

| Reader | Field | Measured |
|---|---|---|
| u16 | `scrollMaxH` | 0..455 |
| u16 | `scrollMaxV` | 0..6,000 |
| u8, `== 1` | `aBoolean2286` | only when `i_2_ < 0`, i.e. **always here**. Values 0 (6,335) and 1 (238) |

**Type 5 - sprite** (`:1082-1096`)

| Reader | Field | Meaning | Measured |
|---|---|---|---|
| i32 | `imageID` | Sprite id in index 8. CS2 1105. | -1 .. 4,584 (index 8 holds 4,593 groups) |
| u16 | `anInt2255` | Sprite transform parameter; used with a 4096 scale at `Node_Sub10_Sub24.java:603-639`. CS2 1106. | 0 .. 49,152 |
| u8 | flags | bit 0 -> `aBoolean2288` (CS2 1107, gates the transformed draw at `Node_Sub10_Sub24.java:598`), bit 1 -> `aBoolean2279` (CS2 1122). Bits 2-7 unread. | 0,1,2,3 only |
| u8 | `anInt2285` | **Transparency.** The renderer builds the pixel as `((255 - (a & 0xff)) << 24) \| (colour & 0xffffff)` (`Node_Sub10_Sub24.java:443-449`), so 0 is opaque. | 0..255 |
| u8 | `anInt2304` | Outline thickness: `class324.method3688(anInt2304)` (`RSInterface.java:500`). CS2 1116. | 0..2 |
| i32 | `anInt2355` | Outline/shadow colour, 0 meaning none (`RSInterface.java:487-495`). CS2 1117. | 0 .. 6,579,300 |
| u8, `== 1` | `aBoolean2327` | First image transform, `method3682` (`RSInterface.java:479`). CS2 1118. | 0 (13,101), 1 (361) |
| u8, `== 1` | `aBoolean2281` | Second image transform, `method3691` (`:483`). CS2 1119. | 0 (12,821), 1 (641) |
| i32 | `foregroundColor` | Recolour tint. | -1 .. 16,777,215 |

**Type 6 - model** (`:1098-1146`)

`anInt2233 = 1` is assigned at `:1099` before any read. It is the model-source kind, not a wire
field: `Class247.java:1478` returns `mediaID` only when the kind is still 1, and CS2 opcodes
set it to 2, 3, 5, 6, 8 and 9 for npc/item/player sources. So `mediaID` is **a model id in
index 7** as decoded.

| Reader | Field | Notes | Measured |
|---|---|---|---|
| u16 | `mediaID` | `65535` -> `-1`. | 819..65535; 1,001 are 65535 |
| u8 | settings | bit 0 -> takes the 6-field block; bit 1 (`hiddenSomething`) -> takes the 7-field block; bit 2 -> `aBoolean2265`; bit 3 -> `aBoolean2325`. Bits 4-7 unread. | 1 (6,765), 9 (123), 5 (108), 13 (8), 0 (5) |
| block A, when bit 0 | i16 `modelOffsetX`, i16 `modelOffsetY`, u16 `rotateX`, u16 `rotateY`, u16 `rotateZ`, u16 `modelZoom` | | 7,004 records; rot 0..2047, zoom 0..5000 |
| block B, when bit 1 and not bit 0 | i16, i16, i16 `anInt2352`, u16, u16, u16, **i16** `modelZoom` | Note the zoom is **signed** here and unsigned in block A. | **0 records. Bit 1 is never set in this cache.** |
| neither | - | | 5 records |
| u16 | `animationID` | `65535` -> `-1`. | 6,466 are 65535 |
| u16, when `aByte2243 != 0` | `anInt2232` | Gated on widthMode. | 338 records |
| u16, when `aByte2207 != 0` | `anInt2226` | Gated on heightMode. | 320 records |

**Type 4 - text** (`:1148-1166`)

| Reader | Field | Notes | Measured |
|---|---|---|---|
| u16 | `anInt2264` | Font id in index 13. `65535` -> `-1`. | 305..4,040. **The sentinel never occurs.** |
| NUL string | `message` | | non-empty on 8,920 of 10,317 |
| u8 | `anInt2244` | Line height. | 0..42 |
| u8 | `anInt2341` | Horizontal alignment. | 0..2 |
| u8 | `anInt2296` | Vertical alignment. | 0..3 |
| u8, `== 1` | `aBoolean2315` | Shadow. | 0 (2,713), 1 (7,604) |
| i32 | `foregroundColor` | | |
| u8 | `anInt2285` | Transparency, as type 5. | **always 0** |
| u8 | `anInt2350` | **Only when `i_2_ >= 0`.** | never read |

**Type 3 - rectangle** (`:1168-1172`): i32 `foregroundColor`, u8 `aBoolean2263` (`== 1`;
selects between two rectangle draws at `Node_Sub10_Sub24.java:441-455` - 0 on 1,102 and 1 on
3,426), u8 `anInt2285` transparency (0..255).

**Type 9 - line** (`:1174-1178`): u8 `anInt2293` line width (1..2), i32 `foregroundColor`,
u8 `aBoolean2256` (`== 1`; flips which diagonal is drawn, `Node_Sub10_Sub24.java:885-897` -
0 on 308, 1 on 59).

### 4.3 Component type dispatch

Settled from what the renderer and the layout pass **do**, never from a field identifier. The
type is byte 1 with `0x80` masked off.

| Type | What the client does with it | Files |
|---|---|---|
| 0 | Sets a clip rectangle, applies `scrollPosition`, and recurses into its children (`Node_Sub10_Sub24.java:405-412`). Layer/container. | 6,573 |
| 1 | Nothing reads it. | 0 |
| 2 | Skipped by the bounds clamp in `client.java:722-726`, so it is drawn without being clipped to its own rectangle. Nothing draws it. | 0 |
| 3 | Fills a rectangle with `foregroundColor` (`Node_Sub10_Sub24.java:439-455`). | 4,528 |
| 4 | Resolves an `RSFont` and draws `message` (`Node_Sub10_Sub24.java:471-...`). | 10,317 |
| 5 | Draws an `ImageArchive` from index 8, or a player-appearance model when `anInt2267 >= 0` (`Node_Sub10_Sub24.java:563-...`). | 13,462 |
| 6 | Draws a model from index 7, or from an item/npc/player source (`Node_Sub10_Sub24.java:675-...`). | 7,009 |
| 7, 8 | Nothing reads them. | 0 |
| 9 | Draws a line between two corners of its rectangle (`Node_Sub10_Sub24.java:881-897`). | 367 |

Types 10..127 are expressible and read no type block. Nothing rejects them.

### 4.4 Common tail

| # | Reader | Field | Notes | Measured |
|---|---|---|---|---|
| 1 | **u24** | `i_6_` | Hook/access mask. `RSBuffer.method1186()`, `:1180`. See section 6. | 50 distinct values; 0 on 25,509 |
| 2 | u8 + loop | slot table | See below. | present on 43 files |
| 3 | NUL string | `aString2224` | Option base text. CS2 1101. | non-empty on 1,292 |
| 4 | u8 | action byte | Low nibble = number of context-menu option strings; high nibble gates the two blocks below (`:1214-1242`). | low 0..10; **high only 0 (42,117) or 1 (139)** |
| 5 | NUL string x low nibble | `contextMenuActions` | | |
| 6 | u8 index, u16 value, when high nibble `> 0` | into `anIntArray2326` | Array is sized `index + 1` and pre-filled with -1. | index 0..1, value 42..62 |
| 7 | u8 index, u16 value, when high nibble `> 1` | into the same array | **Never reached in this cache.** | |
| 8 | NUL string | `aString2333` | `""` becomes null at `:1246-1248`. | empty on 42,163 |
| 9 | u8 | `anInt2308` | Drag deadzone in pixels (`Class111_Sub3.java:87-95`). | 0..5 |
| 10 | u8 | `anInt2353` | Drag delay in ticks, compared against `Class105.anInt3417` (`Class111_Sub3.java:83`). | 0..5 |
| 11 | u8 | `anInt2289` | Hint-icon slot (`Node_Sub10_Sub24.java:137`). CS2 at `Class247.java:1080`. | 0..1 |
| 12 | NUL string | `aString2214` | Tooltip; `Class170.java:16-22` returns it when non-blank. | non-empty on 154 |
| 13 | u16 x 3, gated | `i_18_`, `anInt2309`, `anInt2318` | Gate is `(i_6_ & 0x3F800) != 0`, see section 6. Each has the `65535 -> -1` sentinel. | fires on 140 files; **all three are 65535 in all 140** |
| 14 | u16, when `i_2_ >= 0` | `anInt2317` | `65535 -> -1`. | never read |
| 15 | param block, when `i_2_ >= 0` | | See section 5. | never read |
| 16 | 10 x `loadCS2Bytecode` | hooks 0-9 | | |
| 17 | 1 x `loadCS2Bytecode`, when `i_2_ >= 0` | `anObjectArray2253` | | never read |
| 18 | 10 x `loadCS2Bytecode` | hooks 10-19 | | |
| 19 | 5 x `method3473` | int arrays 0-4 | | |

**The slot table** (`:1181-1210`). Read a u8. While it is non-zero: `slot = (byte >> 4) - 1`;
read another u8 and form `value = ((byte & 0xf) << 8) | next`, with `4095` mapping to `-1`;
read two i8; then read the next u8 and repeat. Four bytes per entry, terminated by a zero
byte. The zero terminator is the byte read at `:1181` when the table is absent, so an absent
table costs exactly one byte and no terminator is written separately.

The three parallel arrays are size 11, so a high nibble above 11 would throw in the client.
Measured: the table is present on 43 files (39 with one entry, 4 with four), no slot index
above 10 occurs, and the `4095` sentinel never occurs.

### 4.5 The hook arrays

`loadCS2Bytecode` (`:400-430`): read u8 `n`; if `n == 0` return **null**; otherwise read `n`
elements, each a u8 type byte followed by an i32 when the type is 0, a NUL string when the type
is 1, and **nothing at all for any other type**, leaving that element null.

`method3473` (`:981-1001`): read u8 `n`; if `n == 0` return **null**; otherwise `n` x i32.

Decode order maps to the client's own CS2 setter opcodes at `Class247.java:1254-1316`, which is
what settles each slot's identity. The five hook/trigger pairings are hard facts: opcodes 1407,
1414, 1415, 1428 and 1429 each assign a hook **and** its int array in one statement.

| Slot | Field | CS2 setter | What fires it | Present |
|---|---|---|---|---|
| 0 | `anObjectArray2332` | none | Every component, immediately after its interface loads (`Class247.method3155`, `:4120-4136`). | 2,038 |
| 1 | `anObjectArray2227` | 1403 | Entering the `aBoolean2322` state (`client.java:1176-1187`). | 4,206 |
| 2 | `anObjectArray2272` | 1404 | Leaving `aBoolean2322` (`client.java:1201-1215`). | 4,942 |
| 3 | `anObjectArray2324` | 1406 | Target selection cancelled (`Node_Sub10_Sub32.java:90-95`). | 14 |
| 4 | `anObjectArray2257` | 1416 | The component becomes the target selector (`Node_Sub5_Sub2.java:84-95`). | 9 |
| 5 | `anObjectArray2269` + int 0 | 1407 | Varp/varbit change. Channel written at `Class185.java:377` from `VarpBit.getBitConfig`. | 702 / 702 |
| 6 | `anObjectArray2252` + int 1 | 1414 | Inventory change. Channel written at `PacketParser.java:1926, 2019, 2171` from the interface index. | 175 / 175 |
| 7 | `anObjectArray2278` + int 2 | 1415 | Stat change. Channel written at `PacketParser.java:2300` from a skill id. | 90 / 86 |
| 8 | `anObjectArray2270` | 1408 | Unconditionally, every tick (`client.java:1216-1223`). | 57 |
| 9 | `some_interface_script` | 1409 | With an op index and a string (`Class303.java:41-49`). | 1,218 |
| 10 | `anObjectArray2314` | 1412 | Every tick while in `aBoolean2322` (`client.java:1190-1199`). | 915 |
| 11 | `anObjectArray2291` | 1400 | Entering the `aBoolean2241` state (`client.java:1122-1135`). | 546 |
| 12 | `anObjectArray2335` | 1411 | Every tick while in `aBoolean2241` (`client.java:1137-1147`). | 7 |
| 13 | `anObjectArray2356` | 1402 | Leaving `aBoolean2241` (`client.java:1150-1163`). | 68 |
| 14 | `anObjectArray2230` | 1401 | Every tick while the tick flag is set (`client.java:1164-1174`). | 20 |
| 15 | `anObjectArray2316` | 1405 | A drag that has passed `anInt2308` (`Class111_Sub3.java:101-109`). | 1 |
| 16 | `anObjectArray2313` | 1410 | The drop, which also sends the switch-items packet (`Class111_Sub3.java:66-79`). | 91 |
| 17 | `anObjectArray2277` | 1417 | With `Class319.anInt2699` (`client.java:897-906`). | 13 |
| 18 | `anObjectArray2212` + int 3 | 1428 | Client **int** var change. Channel written at `Class185.java:389`, alongside `anIntArray3744[id] = miscSettingsHash`. | 326 / 325 |
| 19 | `anObjectArray2320` + int 4 | 1429 | Client **string** var change. Channel written at `Class185.java:593`, alongside `aStringArray4967[id] = overrideString`. | 103 / 103 |

Three of the five pairs have identical present-counts and the other two differ by 4 and 1,
which is what you expect if a hook can be set without triggers but not the reverse. That is
independent corroboration of the pairing.

CS2 opcodes **1418 to 1427** set ten further hook arrays (`anObjectArray2239`, `2274`, `2215`,
`2292`, `2340`, `2330`, `2319`, `2294`, `2220`, `2266`). None of them is in the wire format.
Do not go looking for them in the bytes.

Measured totals: 15,541 of 845,120 hook slots are present (829,579 null); 1,391 of 211,280 int
slots are present. Element type bytes: 46,033 ints and 1,505 strings, **and nothing else**.

---

## 5. The version byte, and the branches this cache cannot reach

`i_2_` is `-1` on **all 42,256 files** (byte 0 is `0xFF` on every one, and 255 is the only
value mapped to -1). `EntityEnumType.java:50-52` throws `IllegalStateException("if1")` on
anything else, so the client agrees: this cache is all if3.

That makes five branches unreachable here and one always taken:

| `RSInterface.java` | Branch | Reachable in this cache |
|---|---|---|
| `:1067-1069` | `settingsFlags` bit 1 -> `aBoolean2286` | **no** |
| `:1077-1079` | The extra byte on type-0 components | **always taken** |
| `:1163-1165` | The extra u8 on type-4 components | **no** |
| `:1279-1285` | `anInt2317` (u16, `65535 -> -1`) | **no** |
| `:1289-1307` | **The param table** - u8 count, then that many `(u24 key, i32 value)`; then u8 count, then that many `(u24 key, versioned string)` | **no** |
| `:1320-1322` | The 21st `loadCS2Bytecode` array, `anObjectArray2253` | **no** |

Two consequences, and they pull in opposite directions:

**Implement all six anyway.** This is the same rule the project already carries for reference
tables (`AGENTS.md`: nothing sets `sizes 0x04`, no table is format 7, keep both branches). The
first file that sets a version is mis-parsed from that field onward, and no sweep in this
repository would catch it, because no shipped file exercises the branch.

**And do not let anyone defend them with a sweep.** A passing byte-identity sweep over index 3
says nothing about any of the six. Write that in the code comment next to each, so a future
reader does not mistake green tests for evidence. A decoder written from a modern RS3 if3
reference will read the param block unconditionally and desync every record in this cache.

The `0x80` name-string branch on the type byte (`:1043-1046`) is in the same category: set on
**0 of 42,256** files, must still be implemented, will never be tested by this cache.

---

## 6. The 24-bit reader

**Yes, `RSBuffer.method1186()` is unsigned, and big-endian** (`RSBuffer.java:131-135`):

```java
this.caret += 3;
return (((this.buffer[-1 + this.caret]) & 0xff)
      + ((0xff & (this.buffer[-2 + this.caret])) << 8)
      + (((this.buffer[this.caret + -3]) & 0xff) << 16));
```

Every byte is masked with `0xff` before it is combined, so no sign extension occurs and the
result is 0..16,777,215. Nothing subtracts a bias. It is used in three places: the hook mask
at `:1180`, and the two param-key reads at `:1293` and `:1302`.

**There is a second 24-bit reader in the same class and it is not interchangeable.**
`RSBuffer.method1192` (`:169-178`) is little-endian:
`buf[p] + (buf[p+1] << 8) + ((buf[p+2] << 16) & 0xff0000)`. Picking that one puts the mask
bytes in the wrong order, which silently changes which of the gated branches fires. Read the
bytes in the order `method1186` does.

**The gate on the mask.** `:1259` reads
`aa_Sub3.method157(i_6_, (byte) 64) != 0`, and `method157` is
`return (0x3ff26 & i) >> 11;` (`aa_Sub3.java:27-37`). After the shift, only bits 11..17
survive, so the condition is exactly `(mask & 0x3F800) != 0`. Fires on **140** files here, and
in every one of them all three following shorts are `65535`.

Bit 23 of the mask is never set in this cache. 50 distinct mask values occur; the four
commonest are `0x000000` (25,509), `0x000400` (9,748), `0x000002` (4,802) and `0x000402`
(1,039).

---

## 7. Non-canonical encodings

The project's standing rule is that a decoder has to record which encoding it saw. Here is the
full list for index 3, split by whether it is a real ambiguity or not, because **the survey's
list is wrong on one item** and getting that wrong costs an implementer a wasted field.

### 7.1 Genuine ambiguities - the stored byte cannot be recomputed

| # | Where | Why two encodings decode the same | Occurs here |
|---|---|---|---|
| A | The action byte's **high nibble** (`:1226-1242`) | Values 2..15 all read exactly two index/value pairs. The nibble is never stored anywhere. | **No** - only 0 and 1 occur. Latent. |
| B | `loadCS2Bytecode` **element type byte** (`:412-420`) | Type 0 reads an int, type 1 reads a string, and **every other value reads nothing** and leaves the element null. So 2..255 are all aliases of each other. | **No** - only 0 and 1 occur. Latent. |
| C | Every `readUnsignedByte() == 1` boolean | Any value other than 1 decodes to false, so 0 and 2..255 alias. Affects the type-0 extra byte, type-3 `aBoolean2263`, type-4 `aBoolean2315`, type-5 `aBoolean2327` / `aBoolean2281`, type-9 `aBoolean2256`. | **No** - all six are 0 or 1 throughout. Latent. |
| D | `settingsFlags` (`:1065-1071`) | Bit 0 is `isHidden`; bit 1 is read only when the version gate opens; bits 2-7 are read by nothing. Rebuilding the byte from `isHidden` alone drops six bits. Same shape as the reference table's `groupFlags` rule in `AGENTS.md`. | Values 0 and 1 only, so no bit is currently lost. Keep the byte. |
| E | The type-5 flags byte (`:1086-1089`) | Bits 0 and 1 are read; bits 2-7 are not. | Values 0..3 only. |
| F | The type-6 settings byte (`:1106-1112`) | Bits 0-3 are read; bits 4-7 are not. | Values 0, 1, 5, 9, 13. |

**Store the raw bytes for A through F**, not the decoded interpretation. Four of the six cost
one byte each on the record.

### 7.2 Transforms that must be inverted, not ambiguities

These are bijective, so nothing is lost, but each is a place where a decoder that keeps only
the runtime value writes back the wrong bytes:

- **`parentID`** (`:1057-1063`). Raw `65535` becomes `-1`; anything else becomes
  `raw + (groupId << 16)`. The encoder must write `parent < 0 ? 65535 : (parent & 0xFFFF)`.
  Measured: every non-sentinel parent is a valid sibling file id inside the same group, 33,843
  of them, with zero out of range - so the fold really does mean "component `raw` of this same
  interface".
- **The `65535 -> -1` sentinels** on `mediaID`, `animationID`, the type-4 font id, the three
  hook-mask shorts, and `anInt2317`. Each is one representation of one value.
- **The version byte**: 255 means -1, 0..254 mean themselves.
- **The slot value 4095 -> -1** (`:1194-1196`). 12 bits, so `-1` has exactly one encoding.
- **`aString2333`**: `""` is turned into null at `:1246-1248`. Write null back as a single
  zero byte and the bytes match.

### 7.3 Not a non-canonical case, contrary to the survey

The survey lists "null vs empty for every `loadCS2Bytecode` / `method3473` array" as a
non-canonical case. **It is not.** Both helpers return null on a count of 0, and there is no
other encoding of an empty array - a present-but-empty array is unrepresentable. Null and
count-0 are the same thing. Do not add a "was this null or empty" flag; it can only ever be
null.

The real hazard in that neighbourhood is B above, the element type byte.

---

## 8. Names

Both levels carry name hashes. The table is format 6, version 1131, flags `0x01`, and consumes
to the byte (270,792 of 270,792).

**The sentinel is `-1`, not 0.** `VersionTable.java:145-147` pre-fills the identifier array
with -1 and then overwrites it for declared groups, so a stored -1 is the client's own way of
saying "unnamed". Measured: **0** identifiers are zero; **11 groups** (1,070 to 1,080) and
**1,721 components** are -1. `RawFileListDescriptor.cs` already documents this correctly; the
index-3 survey's "all identifiers are non-zero, so both levels are name-hashed" is true and
misleading.

So 1,067 groups and 40,535 components carry a real hash. Hashing is Java `String.hashCode`
over the lowercased name, as `NameHasher.GetNameHash` already implements.

**Component names: 9,219 recovered, self-provingly.** The five commonest component hashes are
consecutive integers (`94843122`..`94843126`, on 366/342/327/272/254 files), which forces the
names to differ only in a trailing digit. A meet-in-the-middle crack over `[a-z0-9_]` returns
`com_0`, `com_1`, `com_2`, ... (the only other candidate, `e1m_N`, is not a word). Extending
`com_<N>` to N in 0..3,999 matches **9,219** component hashes, and in **9,219 of 9,219** the N
equals that component's own file id. Zero exceptions. That is a join that proves itself on
every single row, which is the standard `CLAUDE.md` demands after the track-name failure - not
an aggregate that merely looks good.

Breakdown of the 42,256 components: 1,721 unnamed, 9,219 named `com_<fileId>`, 31,316 carrying
a bespoke name across 21,244 distinct hashes.

**Group names: recoverable against a candidate list, not by brute force.** Hashing plain
English guesses lands them directly and verifiably:

| Name | Hash | Group |
|---|---|---|
| `loginscreen` | -2,079,671,019 | 744 |
| `lobbyscreen` | 225,502,146 | 906 |
| `inventory` | -2,020,599,460 | 149 |
| `toplevel` | -954,439,857 | 548 |
| `worldmap` | 36,243,594 | 755 |
| `options` | -1,249,474,914 | 261 |
| `logout` | -1,097,329,270 | 182 |
| `magic` | 103,655,853 | 192 |
| `prayer` | -980,211,737 | 271 |

The first two are corroborated independently: they are the only two names the client asks index
3 for by name, at `InterfaceSettings.java:356-358`.

Harvesting every printable run out of index 3's own payloads (7,239 candidates) names 14 more
groups outright (34 `Notes`, 139 `Gnomeball`, 320 `Stats`, 464 `Emotes`, 952 `Bind`, ...) and
1,870 components.

Exhaustive cracking is where it stops being reliable. A meet-in-the-middle over all
`[a-z0-9_]` strings of six characters or fewer returns at least one candidate for **189 of the
1,067** named groups, and most of those are ambiguous - a 32-bit hash over a 37-character
alphabet collides about once per target at that length. Structure disambiguates where it
exists (groups 64-67 crack to `chat1`..`chat4`, 228/230/232/234 to `multi2`..`multi5`), but a
lone six-character candidate is a guess. Ship the hash in the UI and a curated name list
alongside it; never let a cracked string overwrite the stored hash.

**Ten of the group ids share a hash only because they share `-1`.** There are 1,068 distinct
group identifier values over 1,078 groups, and the single duplicate is the -1 sentinel on the
eleven groups 1,070-1,080. `RSReferenceTable.GetArchiveId(name)` is therefore unambiguous for
every real name.

---

## 9. Client-side modifications - do not port

The survey warns of three in the load path. All three are real, and all three are in
`EntityEnumType.loadInterfaceIfExists`:

1. **`:40`** - `getChildFromFolder(parent == 61 ? 259 : parent, childIndex)`. Asking for group
   61 silently serves group 259's components while still tagging them `61`. A codec that
   copied this would read the wrong archive.
2. **`:55-59`** - for group 408 only, if `mediaID` is in 15,285..15,289, overwrite `modelZoom`
   with 3,700 and add 5 to `modelOffsetY`. This mutates decoded fields after `unpackConfig`
   returns; port it and a byte-identity re-encode of group 408 fails.
3. **`:61-63`** - any component with `mediaID == 27167` is forced hidden. Same problem: it
   changes `isHidden` after decode.

Two more that are not in the load path but belong on the same list:

4. **`RSInterface.java:1252`** - a live `System.err.println(anInt2308 + "|" + anInt2353)` left
   inside `unpackConfig`. Debug spew, not format.
5. **`Class111_Sub3.java:86-90`** - in the drag handler, `if (id == 149 && id2 == 0)` forces
   `anInt2353 = 14` and `anInt2308 = 5`. Group 149 is `inventory` (section 8), so this is a
   hardcoded fix to the inventory container's drag parameters. It never touches the bytes, so
   it cannot corrupt a re-encode, but it will make a viewer disagree with the client about
   those two fields on one component.

None of the five is the format. All five are the reason the "match the client" default has to
be applied with judgement.

---

## 10. Building it

### 10.1 Where the editor already is

The survey's "Current capability" section is stale. `Editor.cs:558-559` **already** registers
the interface tab through the descriptor mechanism:

```
Register(InterfaceEditorTab, RSConstants.INTERFACE_DEFINITIONS_INDEX,
    openCache => InterfaceListPanel.Bind(openCache, InterfaceListing));
```

with `InterfaceListing` being a `RawFileListDescriptor` (`Editor.cs:118-119`) and
`InterfaceListPanel` a `DefinitionListPanel` (`Editor.Designer.cs:1255, 1647`). There is no
switch arm to add and none to write. The work is to replace the raw descriptor with a real
`DefinitionListDescriptor<TRow>` that implements `Enumerate`, `Decode`, `AddressOf`, `Encode`
and sets `IsEditable`.

### 10.2 Suggested pass split

**Pass 1 - decode plus exact consumption, no encoder.**
`InterfaceComponentDefinition` with a `Decode(JagStream, int groupId, int fileId)` following
section 4 exactly, storing the raw bytes for the six cases in 7.1 and the unfolded raw
`parentID`. A `[RealCacheFact]` sweep over `RSCache.EnumerateFiles(3)` asserting the stream
lands exactly at the end for every file. That reproduces the result in section 1 inside this
repository, where it can regress. Add `CacheAddressing` row `Paged(16)` for index 3.

**Pass 2 - encoder and the byte-identity sweep.** `Encode` writing the same order, then the
full-index sweep in the shape of `RealCacheItemDefinitionTests`: all 1,078 groups, all 42,256
files must re-encode to the bytes they were read from. This is the primary regression
detector, and per `CLAUDE.md` it must not be relaxed if it fails.

Before the encoder, capture a handful of components as fixtures under
`FlashEditor.Tests\Fixtures\RealCache\` and assert against those bytes. Round-tripping this
encoder against this decoder proves nothing; two real defects have already survived exactly
that.

**Pass 3 - the descriptor and the tree.** Columns: group, file, type, position, size, parent,
hook count, name hash. Name the group and component from a curated list plus the
`com_<fileId>` rule, and show the hash when neither resolves.

**Pass 4 (optional, much larger) - a layout preview.** It needs index 8 (sprites), 7 (models)
and 13 (fonts), which the client states directly at `InterfaceSettings.java:294-295`. The codec
needs none of them.

### 10.3 Assertions worth writing, and one to avoid

Write, because each fails loudly if a branch is mis-ported:

- Exact consumption, all 42,256.
- Byte identity, all 42,256.
- Every non-sentinel `parentID` resolves to a file id that exists in the same group (currently
  33,843 of 33,843).
- Type histogram equals `{0: 6573, 3: 4528, 4: 10317, 5: 13462, 6: 7009, 9: 367}`.
- Every component's version byte is `0xFF`, so the fact that six branches are untested is
  itself pinned rather than assumed.

Avoid: any sweep whose assertion contains an `or` over "decoded or skipped". Per `CLAUDE.md`, a
sweep that tolerates a failure does not test for it.

---

## 11. What is still open

- **The 21 components with a non-zero `contentType` (field `x`).** Values 328, 1,337-1,339 and
  1,400-1,407. The client compares this field against a table of constants scattered across
  `client.java` and `Node_Sub10_Sub24.java`; resolving those constants would name the
  behaviours. Nothing about the codec depends on it.
- **`anInt2255`** (type 5, 0..49,152). Used with a 4096 scale at
  `Node_Sub10_Sub24.java:603-639`. Whether it is an angle, a scale, or a packed pair is not
  settled here.
- **Which of `aBoolean2327` / `aBoolean2281` is the horizontal flip.** Settling it means
  reading `Class324.method3682` and `method3691`. The codec does not care; a preview would.
- **The remaining 31,316 component names.** A curated name list is the only path; brute force
  is ambiguous past five characters.
- **The provenance of groups 772, 825 and 891.** Their contents decode cleanly; why they were
  dropped from the table is not recoverable from the cache.
