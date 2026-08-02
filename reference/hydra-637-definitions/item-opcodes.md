# Item definition opcodes - build 637 client vs the FlashEditor codec

Cross-reference between the item-definition decoder in the bundled HydraScape client
(build **637**) and our C# decoder in `FlashEditor/Definitions/ItemDefinition.cs`
(targeting the build **639** cache).

The two revisions are a mismatched pair (see `AGENTS.md`). The cache is authoritative on
**payload sizes**; a prior sweep of all 20,470 item definitions in the 639 cache showed the
C# decoder consumes every definition's buffer exactly, with no unknown opcode. The client is
authoritative on **what a payload means** and **whether it is read signed or unsigned**. This
document is therefore about semantics and signedness, not sizes.

---

## The client class

**`ItemDefinition.java`** in `HydraScape/client/src`.

The class survived obfuscation with its real name and a handful of hand-recovered field names
(`name`, `value`, `teamID`, `invModelID`, ...) - it appears a previous author partially
de-obfuscated it, which is why a name-based search for `Class<N>` siblings missed it.

Evidence that this is the item-definition decoder, not a look-alike:

| Claim | Evidence |
| --- | --- |
| It is a per-opcode definition decoder | `readValues(int opcode, RSBuffer, int)` at `ItemDefinition.java:886`, driven by the read-until-zero loop `unpackItemFile(RSBuffer, byte)` at `ItemDefinition.java:1199-1211` - identical shape to `Class141.readValues` (NPC) and `Class352` (loc). |
| It decodes **items** specifically | Opcode 12 reads a 4-byte int into a field the client later multiplies by stack size to price an inventory slot (`Class48_Sub1.java:86-97`). Opcode 11 sets the flag the CS2 "is stackable" instruction reads (`Class247.java:3587`). |
| Note / lend template merging | `Class205.java:235-242` - when opcode 98's field is set the client merges the definition at opcode 97's field; when opcode 122's field is set it merges the definition at opcode 121's field. That is the classic noted/lent item construction and exists on no other definition type. |
| Ground / inventory option arrays | `Class205.java:224-227` seeds `aStringArray2446` with the "Take" phrase at index 2 and `aStringArray2473` with the "Drop" phrase at index 4 - the defaults our `groundOptions` / `inventoryOptions` carry. |
| It is the class the rest of the client asks for by item id | `Class205.getItemByID(...)` returns `ItemDefinition`; called from `Class247` (CS2), `Class39` (menus), `Class96`, `Class277` (Grand Exchange search), `Particle_Sub3_Sub2_Sub1` (ground items). |

`Class204.java` was checked and is menu code, as flagged. It holds the literal option strings
but never touches an `RSBuffer`.

### Buffer primitives (build 637)

| Client call | Definition | C# equivalent |
| --- | --- | --- |
| `readUnsignedByte()` | `RSBuffer.java:896-899`, `buffer[caret++] & 0xff` | `JagStream.ReadByte()` (`JagStream.cs:217-220`, backing store is `byte[]`, so 0-255) |
| `readSignedByte()` | `RSBuffer.java:853-855`, raw Java `byte` | `JagStream.ReadSignedByte()` (`JagStream.cs:301-305`) |
| `readUnsignedShort()` | `RSBuffer.java:901-904`, big-endian, unsigned | `JagStream.ReadUnsignedShort()` (`JagStream.cs:345-350`) |
| `readInt()` | big-endian signed 32-bit | `JagStream.ReadInt()` (`JagStream.cs:399-404`) |
| `method1186()` | `RSBuffer.java:131-135`, big-endian **unsigned 24-bit medium** | `JagStream.ReadMedium()` (`JagStream.cs:377-383`) |
| `readString()` | `RSBuffer.java:878-894`, NUL-terminated, no version byte | `JagStream.ReadJagexString()` (`JagStream.cs:580-594`) |

Both sides loop `while (opcode = readUnsignedByte()) != 0`
(`ItemDefinition.java:1203-1211` vs `ItemDefinition.cs:139-144`).

---

## Opcode table

`Client ref` is `ItemDefinition.java:LINE` unless another file is named. The client's decompiled
if/else chain is inverted (`if (opcode != N) { ... } else { <the read> }`), so the guard line and
the read line are usually far apart; where they differ, both are given as `guard/read`.

| Opcode | Client read (exact expression) | Client field | Client ref | Our read | Our field | Verdict | Notes |
| ---: | --- | --- | --- | --- | --- | --- | --- |
| 1 | `invModelID = RSBuffer.readUnsignedShort();` | `invModelID` | 890/1182 | `buf.ReadUnsignedShort()` | `inventoryModelId` | AGREE | Inventory model id. |
| 2 | `this.name = RSBuffer.readString();` | `name` | 891-892 | `buf.ReadJagexString()` | `name` | AGREE | NUL-terminated, no version byte. |
| 4 | `this.anInt2465 = RSBuffer.readUnsignedShort();` | `anInt2465` | 893/1179 | `buf.ReadUnsignedShort()` | `modelZoom` | AGREE | Zoom2d. Default 2000 both sides (client ctor line 102). Used as `anInt2465 << 2` at 297-301. |
| 5 | `this.anInt2416 = RSBuffer.readUnsignedShort();` | `anInt2416` | 894-895 | `buf.ReadUnsignedShort()` | `modelRotation1` | AGREE | Rotation, applied as `method2105(anInt2416 << 3)` (322). Axis letter (xan2d) is community convention, not proven from the obfuscated matrix code. |
| 6 | `this.anInt2476 = RSBuffer.readUnsignedShort();` | `anInt2476` | 896-897 | `buf.ReadUnsignedShort()` | `modelRotation2` | AGREE | Rotation, applied as `method2097(anInt2476 << 3)` (316). Axis letter unproven, as above. |
| 7 | `this.anInt2437 = RSBuffer.readUnsignedShort(); if (this.anInt2437 > 32767) this.anInt2437 -= 65536;` | `anInt2437` | 898/1172-1176 | `buf.ReadUnsignedShort()` then `if (> 32767) -= 65536` | `modelOffsetX` | AGREE | Read unsigned then folded to signed - identical on both sides, character for character. |
| 8 | `this.anInt2447 = RSBuffer.readUnsignedShort(); if (this.anInt2447 > 32767) this.anInt2447 -= 65536;` | `anInt2447` | 899/1165-1169 | `buf.ReadUnsignedShort()` then `if (> 32767) -= 65536` | `modelOffsetY` | AGREE | As opcode 7. |
| 11 | `this.anInt2469 = 1;` | `anInt2469` | 900/1162 | `stackable = 1` | `stackable` | AGREE | No payload. Client tests `== 1` for stackability (`Class48_Sub1.java:87`, `Class247.java:3587`). |
| 12 | `this.value = RSBuffer.readInt();` | `value` | 901-902 | `buf.ReadInt()` | `value` | AGREE | Signed 4-byte int. |
| 16 | `this.aBoolean2420 = true;` | `aBoolean2420` | 903-904 | `membersOnly = true` | `membersOnly` | AGREE | No payload. Members flag (`Class205.java:245`, `Class247.java:3626`). |
| 18 | `this.anInt2418 = RSBuffer.readUnsignedShort();` | `anInt2418` | 905/1159 | `buf.ReadUnsignedShort()` | `multiStackSize` | AGREE | Both default to -1. The client's only use is pushing it raw onto the CS2 stack (`Class247.java:3707`), so its meaning is not recoverable from the client; our name `multiStackSize` is unproven. **Not present in the 639 cache.** |
| 23 | `this.anInt2458 = RSBuffer.readUnsignedShort();` | `anInt2458` | 906/1156 | `buf.ReadUnsignedShort()` | `maleWearModel1` | AGREE | Grouped with 24 and 78 in the non-`bool` branch of `method3500` (676-684). |
| 24 | `femaleModel1 = (RSBuffer.readUnsignedShort());` | `femaleModel1` | 907-908 | `buf.ReadUnsignedShort()` | `maleWearModel2` | AGREE | **The client field name is wrong.** `method3500:676-684` groups {23, 24, 78} against {25, 26, 79}; ours is the correct grouping. |
| 25 | `this.maleWornModelId2 = (RSBuffer.readUnsignedShort());` | `maleWornModelId2` | 909-910 | `buf.ReadUnsignedShort()` | `femaleWearModel1` | AGREE | Client field name wrong, as opcode 24. |
| 26 | `femaleModelID2 = (RSBuffer.readUnsignedShort());` | `femaleModelID2` | 911/1153 | `buf.ReadUnsignedShort()` | `femaleWearModel2` | AGREE | |
| 30 | `this.aStringArray2446[opcode - 30] = (RSBuffer.readString());` | `aStringArray2446[0]` | 912/1150 | `buf.ReadJagexString()` | `groundOptions[0]` | AGREE | Ground option slot 0. |
| 31 | as opcode 30 | `aStringArray2446[1]` | 912/1150 | `buf.ReadJagexString()` | `groundOptions[1]` | AGREE | **Not present in the 639 cache.** |
| 32 | as opcode 30 | `aStringArray2446[2]` | 912/1150 | `buf.ReadJagexString()` | `groundOptions[2]` | AGREE | Default is the "Take" phrase (`Class205.java:224-225`). |
| 33 | as opcode 30 | `aStringArray2446[3]` | 912/1150 | `buf.ReadJagexString()` | `groundOptions[3]` | AGREE | |
| 34 | as opcode 30 | `aStringArray2446[4]` | 912/1150 | `buf.ReadJagexString()` | `groundOptions[4]` | AGREE | |
| 35 | `this.aStringArray2473[-35 + opcode] = (RSBuffer.readString());` | `aStringArray2473[0]` | 913/1147 | `buf.ReadJagexString()` | `inventoryOptions[0]` | AGREE | Inventory option slot 0. |
| 36 | as opcode 35 | `aStringArray2473[1]` | 913/1147 | `buf.ReadJagexString()` | `inventoryOptions[1]` | AGREE | |
| 37 | as opcode 35 | `aStringArray2473[2]` | 913/1147 | `buf.ReadJagexString()` | `inventoryOptions[2]` | AGREE | |
| 38 | as opcode 35 | `aStringArray2473[3]` | 913/1147 | `buf.ReadJagexString()` | `inventoryOptions[3]` | AGREE | |
| 39 | as opcode 35 | `aStringArray2473[4]` | 913/1147 | `buf.ReadJagexString()` | `inventoryOptions[4]` | AGREE | Default is the "Drop"/discard phrase (`Class205.java:226-227`, `ItemDefinition.java:636`). |
| 40 | `int n = readUnsignedByte(); for (i < n) { newColors[i] = (short) readUnsignedShort(); originalColors[i] = (short) readUnsignedShort(); }` | `newColors`, `originalColors` | 914/1134-1144 | `n = ReadByte()`, then `originalModelColors[i] = (short) ReadUnsignedShort()`, `modifiedModelColors[i] = (short) ReadUnsignedShort()` | `originalModelColors`, `modifiedModelColors` | AGREE | Byte-identical read order. **The client's field names are inverted**: `Model.recolor(start, find, replace)` (`Model.java:1955-1961`) is called as `recolor(0, newColors[i], originalColors[i])` (173), so the *first* short read is the colour to find and the *second* is the replacement. Our naming is the correct one. |
| 41 | `int n = readUnsignedByte(); for (i < n) { aShortArray2460[i] = (short) readUnsignedShort(); aShortArray2456[i] = (short) readUnsignedShort(); }` | `aShortArray2460`, `aShortArray2456` | 915-926 | `n = ReadByte()`, then `textureColour1[i]`, `textureColour2[i]` | `textureColour1`, `textureColour2` | AGREE | Same shape as 40. `Model.method2590(replace, flag, find)` (`Model.java:1632-1646`) is called as `method2590(aShortArray2456[i], flag, aShortArray2460[i])` (179), so the *first* short read is the texture id to find, the *second* is the replacement. `textureColour1` = original texture, `textureColour2` = replacement. |
| 42 | `int n = readUnsignedByte(); for (i < n) aByteArray2457[i] = RSBuffer.readSignedByte();` | `aByteArray2457` | 927/1125-1131 | `n = ReadByte()`, then `ReadSignedByte()` | `texturePriorities` | AGREE | Signed on both sides. Semantically **not** a priority table: each entry indexes `Class338.aShortArray2833` to override the *replacement* colour of the matching opcode-40 pair (233, 838). Our field name is misleading; the read is correct. |
| 65 | `this.aBoolean2461 = true;` | `aBoolean2461` | 928-929 | `unnoted = true` | `unnoted` | AGREE | No payload. Gates whether the item appears in the Grand Exchange search (`Class277.java:66`); cleared for members items on f2p worlds (`Class205.java:248`). Our field name `unnoted` is misleading; the XML doc comment ("tradeable on the GE") is correct. |
| 78 | `anInt2424 = (RSBuffer.readUnsignedShort());` | `anInt2424` | 930-931 | `buf.ReadUnsignedShort()` | `maleWearModel3` | AGREE | Third model of the {23, 24, 78} set (`method3500:678`). |
| 79 | `anInt2432 = (RSBuffer.readUnsignedShort());` | `anInt2432` | 932/1122 | `buf.ReadUnsignedShort()` | `femaleWearModel3` | AGREE | Third model of the {25, 26, 79} set (`method3500:682`). |
| 90 | `anInt2417 = RSBuffer.readUnsignedShort();` | `anInt2417` | 933/1119 | `buf.ReadUnsignedShort()` | `maleHeadModel1` | SEMANTICS-DIFFER | Wire read identical. `method3486:141-147` pairs **90 with 92** and **91 with 93**; our field names pair 90+91 as male and 92+93 as female, which mis-labels 91 and 92. Correct labelling: 90 = male head 1, 91 = female head 1, 92 = male head 2, 93 = female head 2. |
| 91 | `anInt2453 = RSBuffer.readUnsignedShort();` | `anInt2453` | 934/1116 | `buf.ReadUnsignedShort()` | `maleHeadModel2` | SEMANTICS-DIFFER | Should be *female* head 1 - see opcode 90. Naming only; bytes are identical. |
| 92 | `anInt2449 = RSBuffer.readUnsignedShort();` | `anInt2449` | 935-937 | `buf.ReadUnsignedShort()` | `femaleHeadModel1` | SEMANTICS-DIFFER | Should be *male* head 2 - see opcode 90. Naming only. |
| 93 | `anInt2455 = RSBuffer.readUnsignedShort();` | `anInt2455` | 938/1112-1113 | `buf.ReadUnsignedShort()` | `femaleHeadModel2` | AGREE | Female head 2 - our label happens to be right. |
| 95 | `this.anInt2441 = RSBuffer.readUnsignedShort();` | `anInt2441` | 939-941 | `buf.ReadUnsignedShort()` | `zan2d` | AGREE | Applied as `method2104(-anInt2441 << 3)` (315) - the absolute matrix reset, applied before opcodes 5 and 6. |
| 96 | `this.anInt2464 = RSBuffer.readUnsignedByte();` | `anInt2464` | 942/1108-1109 | `buf.ReadByte()` | `dummyItem` | AGREE | Unsigned byte both sides. Non-zero excludes the item from the Grand Exchange search (`Class277.java:68`), i.e. a dummy/placeholder flag. Name is right. |
| 97 | `this.anInt2433 = RSBuffer.readUnsignedShort();` | `anInt2433` | 943/1104-1105 | `buf.ReadUnsignedShort()` | `notedId` | AGREE | The item this note stands for. `Class205.java:236` passes it as the source of `name`, `value` and the members flag in `method3487:197-206`. |
| 98 | `this.anInt2414 = RSBuffer.readUnsignedShort();` | `anInt2414` | 944-947 | `buf.ReadUnsignedShort()` | `notedTemplateId` | AGREE | The note template. `Class205.java:235-237` triggers the merge on this field and takes the models/colours from it. |
| 100 | `anIntArray2428[opcode-100] = readUnsignedShort(); anIntArray2454[opcode-100] = readUnsignedShort();` | `anIntArray2428[0]`, `anIntArray2454[0]` | 948-950/1091-1101 | `ReadUnsignedShort()` twice | `stackIds[0]`, `stackAmounts[0]` | AGREE | Pair is (variant item id, minimum stack count). `method3493:529-533` picks the last entry whose count is non-zero and `<=` the held amount. Both sides lazily allocate the two 10-element arrays. |
| 101 | as opcode 100 | `[1]` | 948-950/1091-1101 | as opcode 100 | `stackIds[1]`, `stackAmounts[1]` | AGREE | |
| 102 | as opcode 100 | `[2]` | 948-950/1091-1101 | as opcode 100 | `stackIds[2]`, `stackAmounts[2]` | AGREE | |
| 103 | as opcode 100 | `[3]` | 948-950/1091-1101 | as opcode 100 | `stackIds[3]`, `stackAmounts[3]` | AGREE | |
| 104 | as opcode 100 | `[4]` | 948-950/1091-1101 | as opcode 100 | `stackIds[4]`, `stackAmounts[4]` | AGREE | |
| 105 | as opcode 100 | `[5]` | 948-950/1091-1101 | as opcode 100 | `stackIds[5]`, `stackAmounts[5]` | AGREE | |
| 106 | as opcode 100 | `[6]` | 948-950/1091-1101 | as opcode 100 | `stackIds[6]`, `stackAmounts[6]` | AGREE | |
| 107 | as opcode 100 | `[7]` | 948-950/1091-1101 | as opcode 100 | `stackIds[7]`, `stackAmounts[7]` | AGREE | |
| 108 | as opcode 100 | `[8]` | 948-950/1091-1101 | as opcode 100 | `stackIds[8]`, `stackAmounts[8]` | AGREE | |
| 109 | as opcode 100 | `[9]` | 948-950/1091-1101 | as opcode 100 | `stackIds[9]`, `stackAmounts[9]` | AGREE | **Not present in the 639 cache.** |
| 110 | `anInt2451 = RSBuffer.readUnsignedShort();` | `anInt2451` | 951/1087-1088 | `buf.ReadUnsignedShort()` | `resizeX` | AGREE | Default 128 both sides. Passed first to `renderable.O(anInt2451, anInt2429, anInt2415)` (273, 829). |
| 111 | `anInt2429 = RSBuffer.readUnsignedShort();` | `anInt2429` | 952-955 | `buf.ReadUnsignedShort()` | `resizeY` | AGREE | Default 128 both sides. |
| 112 | `anInt2415 = RSBuffer.readUnsignedShort();` | `anInt2415` | 956-958 | `buf.ReadUnsignedShort()` | `resizeZ` | AGREE | Default 128 both sides. |
| 113 | `anInt2452 = RSBuffer.readSignedByte();` | `anInt2452` | 959-961 | `buf.ReadSignedByte()` | `ambient` | AGREE | Signed both sides. Added to the ambient base 64 at the render call (266, 825). |
| 114 | `anInt2422 = 5 * RSBuffer.readSignedByte();` | `anInt2422` | 962-963/1082-1084 | `buf.ReadSignedByte() * 5` | `contrast` | AGREE | Signed, and the x5 scale matches. Added to the contrast base (768 at 266, 850 at 825). Our `Encode` divides by 5 to restore the wire byte (`ItemDefinition.cs:452`). |
| 115 | `this.teamID = RSBuffer.readUnsignedByte();` | `teamID` | 964-967 | `buf.ReadByte()` | `teamId` | AGREE | Unsigned byte both sides. |
| 121 | `this.anInt2472 = RSBuffer.readUnsignedShort();` | `anInt2472` | 968/1078-1079 | `buf.ReadUnsignedShort()` | `lendId` | AGREE | The item this lent copy stands for. `Class205.java:241` passes it as the first argument to `method3498`, the source of name/models/colours. |
| 122 | `this.anInt2459 = RSBuffer.readUnsignedShort();` | `anInt2459` | 969-970/1074-1075 | `buf.ReadUnsignedShort()` | `lendTemplateId` | AGREE | The lend template. `Class205.java:240` triggers the merge on this field. |
| 125 | `anInt2448 = readSignedByte() << 2; anInt2426 = readSignedByte() << 2; anInt2425 = readSignedByte() << 2;` | `anInt2448`, `anInt2426`, `anInt2425` | 971-978 | `ReadSignedByte() << 2` three times | `manWearXOffset`, `manWearYOffset`, `manWearZOffset` | AGREE | Read order is X, Y, Z. Proven: `method3500:725` calls `Model.method2597(anInt2425, anInt2448, flag, anInt2426)`, and `Model.java:1807-1811` adds argument 2 to the X vertex array, argument 4 to Y and argument 1 to Z. Applied to the non-`bool` (male) model set. |
| 126 | `anInt2474 = readSignedByte() << 2; anInt2427 = readSignedByte() << 2; anInt2467 = readSignedByte() << 2;` | `anInt2474`, `anInt2427`, `anInt2467` | 979-980/1066-1071 | `ReadSignedByte() << 2` three times | `womanWearXOffset`, `womanWearYOffset`, `womanWearZOffset` | AGREE | Same X, Y, Z order (`method3500:730`). Applied to the `bool` (female) model set. |
| 127 | `this.anInt2438 = RSBuffer.readUnsignedByte(); this.anInt2439 = RSBuffer.readUnsignedShort();` | `anInt2438`, `anInt2439` | 981-985 | `ReadByte()` then `ReadUnsignedShort()` | `cursor1Op`, `cursor1Id` | AGREE | Pair is (option index, cursor sprite id). `Class39.java:349-352` substitutes `anInt2439` for the default cursor when the ground-option index matches `anInt2438`. |
| 128 | `this.anInt2421 = RSBuffer.readUnsignedByte(); this.anInt2471 = RSBuffer.readUnsignedShort();` | `anInt2421`, `anInt2471` | 986-987/1060-1063 | `ReadByte()` then `ReadUnsignedShort()` | `cursor2Op`, `cursor2Id` | AGREE | Second (option index, cursor sprite) pair - `Class39.java:356-359`. |
| 129 | `this.anInt2463 = RSBuffer.readUnsignedByte(); this.anInt2440 = RSBuffer.readUnsignedShort();` | `anInt2463`, `anInt2440` | 988-989/1054-1057 | `ReadByte()` then `ReadUnsignedShort()` | `cursor3Op`, `cursor3Id` | AGREE | Third pair, read back by CS2 at `Class247.java:3658-3659`. |
| 130 | `this.anInt2434 = RSBuffer.readUnsignedByte(); this.anInt2462 = RSBuffer.readUnsignedShort();` | `anInt2434`, `anInt2462` | 990-991/1048-1051 | `ReadByte()` then `ReadUnsignedShort()` | `cursor4Op`, `cursor4Id` | AGREE | Fourth pair, read back by CS2 at `Class247.java:3661-3662`. |
| 131 | *no handler* | - | 990-996 (falls through the 130 / 132 / 134 / 249 tests with no `else`) | `ReadByte()` then `ReadUnsignedShort()` | `cursor5Op`, `cursor5Id` | CODEC-ONLY | **Present in the 639 cache.** The 637 client silently ignores it and consumes no payload, so it would desync on any 639 definition carrying opcode 131. Consistent with a fifth (option index, cursor) pair added after 637; our 3-byte read is what the cache proves. The client cannot confirm the semantics. |
| 132 | `int n = readUnsignedByte(); for (i < n) anIntArray2436[i] = readUnsignedShort();` | `anIntArray2436` | 992/1037-1045 | `n = ReadByte()`, then `ReadUnsignedShort()` | `quests` | AGREE | The array is appended to the menu tooltip via `Class64_Sub25.method653` (`Class96.java:196-243`), the same slot the loc decoder fills from *its* opcode 160 (`Class352.java:1189-1200`) and the NPC decoder from `anIntArray1152`. Quest/campaign id list. |
| 134 | `this.anInt2445 = RSBuffer.readUnsignedByte();` | `anInt2445` | 993-995 | `buf.ReadByte()` | `pickSizeShift` | AGREE | Unsigned byte both sides. Passed as the pick/hit-test size to `Renderable.method2333`/`method2339` for ground items (`Particle_Sub3_Sub2_Sub1.java:308-310, 371-383`). **Not present in the 639 cache.** |
| 139 | *no handler* | - | 993-996 (falls through the 134 / 249 tests) | `buf.ReadUnsignedShort()` | `bindId` | CODEC-ONLY | **Not present in the 639 cache.** No client evidence for the semantics either way. |
| 140 | *no handler* | - | 993-996 (falls through the 134 / 249 tests) | `buf.ReadUnsignedShort()` | `bindTemplateId` | CODEC-ONLY | **Not present in the 639 cache.** As opcode 139. |
| 249 | `int n = readUnsignedByte(); for (i < n) { boolean isStr = readUnsignedByte() == 1; int key = RSBuffer.method1186(); node = isStr ? new StringNode(readString()) : new IntegerNode(readInt()); }` | `aRSArray_2443` | 996-1034 | `n = ReadByte()`; per entry `ReadByte() == 1`, `ReadMedium()`, then `ReadJagexString()` or `ReadInt()` | `itemParams` | AGREE | Identical layout: count byte, then per entry a type byte (1 = string), a 24-bit unsigned key, and either a NUL-terminated string or a 4-byte int. Behavioural nit only: on a duplicate key the client inserts into its `RSArray` unconditionally (1029-1033) while we keep the first occurrence (`ItemDefinition.cs:332`). Bytes consumed are the same. |

**Row count:** 69 opcodes. 66 rows cite a concrete client read expression and line. The 3 rows that
make a negative claim (131, 139, 140) cite the fall-through range that proves the absence. No row
is unverified.

---

## Disagreements, and whether they occur in the 639 cache

### Opcode 131 - the only disagreement that touches real data

The 639 cache contains opcode 131. The 637 client has no handler for it: at
`ItemDefinition.java:990-996` the chain tests 130, then 132, then 134, then 249, and 131 falls off
the end of the chain with no `else`, consuming nothing. Feeding a 639 item definition with opcode
131 to the 637 client would desync its buffer from that point on and misparse everything after.

This is exactly the mismatch `AGENTS.md` warns about, and it is the sharpest single piece of
evidence for it in the item decoder. It also means the client can say nothing about what 131
means. Our 3-byte `(byte, unsigned short)` read is validated by the cache sweep - every definition
consumes its buffer exactly - and by the fact that 127-130 are four identical (option index, cursor
sprite id) pairs, which makes a fifth pair the obvious reading. That inference is not proof, and it
is not made by any client code we have.

Do not change `ItemDefinition.cs` on the strength of this. The cache is the authority on the size
and the size is right.

### Opcodes 90-93 - our field names mis-pair the chathead models

`method3486:141-147` builds the chathead by taking a base model and an optional second model:

- non-`bool` branch: base = opcode 90's field, second = opcode **92**'s field
- `bool` branch: base = opcode 91's field, second = opcode **93**'s field

So the pairs are {90, 92} and {91, 93}. Our fields group them {90, 91} as male and {92, 93} as
female, so `maleHeadModel2` (91) is really *female head 1* and `femaleHeadModel1` (92) is really
*male head 2*. Purely a naming defect - all four are unsigned shorts read in the same order, all
four are present in the 639 cache, and encode/decode round-trips are unaffected. Worth fixing in a
future rename pass; it is not a decoder bug.

The equivalent grouping for the worn models is `method3500:676-684`, which pairs {23, 24, 78}
against {25, 26, 79}. Our field names match that grouping correctly.

### Field names that are misleading but harmless

- **Opcode 42** (`texturePriorities`): the client uses each signed byte as an index into
  `Class338.aShortArray2833` to override the *replacement* colour of the corresponding opcode-40
  pair (`ItemDefinition.java:233, 838`). It is a palette-index override table, not a priority table.
- **Opcode 65** (`unnoted`): the flag gates Grand Exchange search visibility
  (`Class277.java:66`), and is cleared for members items on free worlds (`Class205.java:248`).
  Nothing to do with notes. The XML doc comment on the field already says the right thing.
- **Opcodes 40/41**: the *client's* names are the inverted ones, not ours. `Model.recolor` and
  `Model.method2590` both find the first-read value and substitute the second-read value, which
  matches our `originalModelColors`/`modifiedModelColors` ordering.

### Opcodes present on one side only, none of which occur together in 639 data

| Opcode | Side | In 639 cache | Note |
| ---: | --- | --- | --- |
| 18 | both | no | Handled identically; the client only pushes it to CS2, so the meaning is unrecoverable. |
| 31 | both | no | Ground option slot 1. |
| 109 | both | no | Tenth stack variant. |
| 131 | ours only | **yes** | See above. |
| 134 | both | no | Handled identically. |
| 139, 140 | ours only | no | No client handler and no cache data; both the size and the meaning are unverified. |

## What could not be determined

- **The axis letters for opcodes 5, 6 and 95.** The client applies them through
  `Class111.method2105`, `method2097` and `method2104`, whose implementations
  (`Class111_Sub1.java:166, 268, 282`) manipulate an obfuscated float matrix. Which of the three is
  xan2d / yan2d / zan2d cannot be read off that code without reconstructing the matrix layout. The
  labels in our source follow community convention. What *is* proven is the application order:
  opcode 95 first, via the absolute matrix reset `method2104`, then 6, then the 7/8 translate, then
  5 (`ItemDefinition.java:315-322`).
- **The meaning of opcode 18.** Client-side it is read and then only ever pushed raw onto the CS2
  stack (`Class247.java:3707`). Our name `multiStackSize` is a guess; the default of -1 matches the
  client's (`ItemDefinition.java:86`), which is the only thing that can be confirmed.
- **The semantics of opcodes 131, 139 and 140.** No client handler exists for any of them. 131 is in
  the 639 cache so its size is proven; 139 and 140 have neither client evidence nor cache data, so
  both their sizes and their meanings rest on the codec author's assumption alone.
- **Opcode 3.** Neither side handles it. In older revisions this was the item description; it is
  absent from both the 637 client chain and the 639 cache.
