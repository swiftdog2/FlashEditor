# Object (loc) definition opcodes - 637 client versus our codec

## What this document is

A per-opcode comparison of three independent sources of truth for the object/loc definition
format:

1. **The 637 Java client**, `HydraScape/client/src/Class352.java`, method `method3863`
   (lines 1036-1486). This is the only source that says what a payload *means* and whether it
   is read signed or unsigned. Every claim about the client below cites a line number in that
   file.
2. **Our decoder**, `FlashEditor/Definitions/ObjectDefinition.cs`, method
   `Decode(JagStream, int)` (lines 212-538).
3. **The 639 cache itself**, which proves payload *sizes* empirically and nothing else.

Client and cache are a mismatched pair (client 637, cache 639) - see `AGENTS.md`. Where they
disagree the cache wins on sizes and the client wins on meaning. They did not in fact disagree
on a single size; see "Empirical basis" below.

## How the client was read

`method3863` is a single deeply nested if/else chain on the opcode `i`, obfuscated so that
equality tests appear as `(i ^ 0xffffffff) == -N`, which is `i == N - 1`. Roughly half the
opcodes are handled in a *deferred else*: the chain tests `i != N`, descends into the body, and
the handler for `N` sits in the matching `} else {` far below, at lines 1275-1471, in **reverse**
order of the tests. There are exactly 31 such deferred tests and exactly 31 tail else-blocks, and
the two lists reconcile one-for-one, which is what makes the mapping below checkable rather than
guessed.

Buffer primitive semantics, from `HydraScape/client/src/RSBuffer.java`:

| Client call | Bytes | Meaning |
| --- | --- | --- |
| `readUnsignedByte()` (RSBuffer.java:896) | 1 | `b & 0xff` |
| `readSignedByte()` (RSBuffer.java:853) | 1 | raw signed byte |
| `readUnsignedShort()` (RSBuffer.java:901) | 2 | big-endian, unsigned |
| `readShort()` (RSBuffer.java:820) | 2 | big-endian, sign-corrected at 32767 |
| `readInt()` (RSBuffer.java:753) | 4 | big-endian |
| `readSmart(int)` (RSBuffer.java:857) | 1 or 2 | peek < 128 -> unsigned byte, else unsigned short - 32768 |
| `method1186()` (RSBuffer.java:131) | 3 | big-endian unsigned medium |
| `readString()` (RSBuffer.java:878) | var | NUL-terminated |

Our `JagStream.ReadUnsignedSmart()` (JagStream.cs:464) is byte-for-byte the same rule as
`readSmart`, and `JagStream.ReadMedium()` (JagStream.cs:377) is the same as `method1186`.

## Empirical basis

Every one of the 56,199 object definitions in the 639 cache was walked twice with a
size-only table: once using the payload widths the 637 client implies, once using the widths our
decoder uses. A walk counts as clean only when the `0` terminator lands exactly on the last byte
of the buffer.

| Size table | Clean | Desynced |
| --- | --- | --- |
| 637 client | 56,199 | 0 |
| our codec | 54,302 | 1,897 |

The client's widths are therefore correct for the 639 cache on every single definition, with no
637-versus-639 divergence anywhere in this format. Our 1,897 failures are attributable to exactly
two opcodes: 75 (first bad opcode in 1,408 definitions) and 72 (31 definitions); the remaining
458 failures are downstream garbage from an already-desynced cursor. The "Cache" column in the
table below is the count of definitions carrying that opcode, from the clean client-table walk.

## Opcode table

`Client ref` is `Class352.java:LINE` unless stated. `Cache` is the number of the 56,199 639
definitions carrying the opcode.

| Opcode | Client read (exact expression) | Client field | Client ref | Our read | Our field | Cache | Verdict | Notes |
| --- | --- | --- | --- | --- | --- | --- | --- | --- |
| 1 | `ubyte n`; per group `signedByte` + `ubyte m` + m x `readUnsignedShort()` | `aByteArray2994[]`, `anIntArrayArray2951[][]` | 1045-1060 | same | `modelTypes[]`, `modelIds[][]` | 53375 | AGREE | |
| 2 | `readString()` | `name` | 1066 | `ReadJagexString()` | `name` | 25640 | AGREE | Both NUL-terminated; ours additionally maps 128-159 through a cp1252 table. |
| 5 | as opcode 1, plus a second model-group block skipped by `method3854` | as opcode 1 | 1040-1064, 545-562 | same, extra block captured raw into `_op5ExtraRaw` | as opcode 1 | 324 | AGREE | The client reads the extra block **before** the main block when `aClass302_2963.aBoolean2513` is set and **after** when it is not (1041-1043 vs 1062-1064). That flag defaults to `false` (Class302.java:51) and all 324 op-5 definitions in the cache walk clean with the block last, so "after" is correct here. |
| 14 | `readUnsignedByte()` | `sizeY` | 1067-1068 | `ReadByte()` | `sizeX` | 8713 | SEMANTICS-DIFFER | Field names are transposed relative to ours. Both are one unsigned byte so there is no parse impact. The client field names come from a deobfuscator and are not independently corroborated here; recorded, not resolved. |
| 15 | `readUnsignedByte()` | `sizeX` | 1069, 1470 | `ReadByte()` | `sizeY` | 8290 | SEMANTICS-DIFFER | See opcode 14. |
| 17 | flag, 0 bytes | `walkable = false`, `actionCount = 0` | 1070, 1465-1467 | flag, 0 bytes | `clipType = 0`, `projectileClipped = false`, `walkable = false` | 4414 | AGREE | The client's `actionCount` is the interact/clip type (op 27 sets it to 1), matching our `clipType`; its `walkable` is the projectile-blocking flag, matching our `projectileClipped`. Names differ, behaviour matches. |
| 18 | flag, 0 bytes | `walkable = false` | 1071, 1462-1463 | flag, 0 bytes | `projectileClipped = false`, `walkable = false` | 8499 | AGREE | 17 and 18 both clear the same field in the client, as they do in ours. 7 definitions in the cache carry both. |
| 19 | `readUnsignedByte()` | `anInt2998` | 1072-1073 | `ReadByte()` | `category` | 34958 | AGREE | |
| 21 | flag, 0 bytes | `aByte2971 = 1` | 1074, 1459-1460 | flag, 0 bytes | `contourGroundType = 1` | 18059 | AGREE | |
| 22 | flag, 0 bytes | `aBoolean3007 = true` | 1075-1076 | flag, 0 bytes | `isClipped = true` | 12910 | AGREE | |
| 23 | flag, 0 bytes | `anInt2956 = 1` | 1077-1078 | flag, 0 bytes | `obstructsGround = 1` | 46741 | AGREE | Paired with opcode 103, which sets the same field to 0. |
| 24 | `readUnsignedShort()`, then 65535 -> -1 | `anInt2941` | 1452-1457 | `ReadUnsignedShort()`, 65535 kept | `animationId` | 5423 | SEMANTICS-DIFFER | Two bytes either way, so no parse impact. Our `Encode` writes opcode 24 whenever `animationId != -1`, so a stored 65535 round-trips unchanged; the difference is only visible to code reading the field. |
| 27 | flag, 0 bytes | `actionCount = 1` | 1080-1081 | flag, 0 bytes | `clipType = 1` | 10887 | AGREE | |
| 28 | `readUnsignedByte() << 2` | `anInt2966` | 1082, 1449-1450 | `ReadByte() << 2` | `decorDisplacement`, and also `modelBrightness` | 950 | SEMANTICS-DIFFER | Size agrees. `anInt2966` defaults to 64 (line 245) and is consumed by the decor/wall-displacement path in Class305_Sub1.java:953-954, 1065-1066, 1146-1147, 1175, 1205, 1257. It is *not* a lighting value, so our extra `modelBrightness = decorDisplacement` assignment is wrong. The client's brightness is opcode 29, not 28. |
| 29 | `readSignedByte()` | `anInt2931` | 1083-1084 | `ReadSignedByte()` | `ambientLighting`, and also `modelContrast` | 36722 | SEMANTICS-DIFFER | Size agrees. `anInt2931` is the **ambient/brightness** term: line 568 computes `64 + anInt2931` and passes it as the ambient argument at line 684. Our `modelContrast = ambientLighting` mislabels it; the contrast term is opcode 39. |
| 30 | `readString()` | `aStringArray2939[0]` | 1086, 1443-1444 | `ReadJagexString()` | `actions[0]` | 11380 | AGREE | Client guard is `i < 30 \|\| i >= 35`, so the else covers 30-34. |
| 31 | `readString()` | `aStringArray2939[1]` | 1086, 1443-1444 | `ReadJagexString()` | `actions[1]` | 2969 | AGREE | |
| 32 | `readString()` | `aStringArray2939[2]` | 1086, 1443-1444 | `ReadJagexString()` | `actions[2]` | 367 | AGREE | |
| 33 | `readString()` | `aStringArray2939[3]` | 1086, 1443-1444 | `ReadJagexString()` | `actions[3]` | 1388 | AGREE | |
| 34 | `readString()` | `aStringArray2939[4]` | 1086, 1443-1444 | `ReadJagexString()` | `actions[4]` | 1179 | AGREE | |
| 39 | `readSignedByte() * 5` | `anInt2980` | 1085, 1446-1447 | `ReadSignedByte() * 5` | `contrastLighting` | 27847 | AGREE | This is the **contrast** term: line 569 computes `850 + anInt2980` and passes it as the contrast argument at line 684. |
| 40 | `ubyte n`; n x (`readUnsignedShort()`, `readUnsignedShort()`) | `aShortArray3003[]` then `aShortArray2965[]` | 1087-1097 | same | `recolSrc[]` then `recolDst[]` | 18788 | AGREE | |
| 41 | `ubyte n`; n x (`readUnsignedShort()`, `readUnsignedShort()`) | `aShortArray2995[]` then `aShortArray2974[]` | 1431-1441 | same | `retexSrc[]` then `retexDst[]` | 3065 | AGREE | |
| 42 | `ubyte n`; n x `readSignedByte()` | `aByteArray2955[]` | 1421-1429 | same | `texturePriorities[]` | 0 | AGREE | Handled by both, present in no 639 definition. |
| 44 | not handled | - | absent from 1036-1486 | `ReadUnsignedShort()` expanded to a byte array | `unknownArray3` | 0 | CODEC-ONLY | No branch for 44 anywhere in the chain. Absent from the cache too, so it is unreachable dead code either way. |
| 45 | not handled | - | absent from 1036-1486 | `ReadUnsignedShort()` expanded to a byte array | `unknownArray4` | 0 | CODEC-ONLY | As opcode 44. |
| 62 | flag, 0 bytes | `aBoolean2961 = true` | 1100-1101 | flag, 0 bytes | `flipped = true` | 4428 | AGREE | |
| 64 | flag, 0 bytes | `aBoolean2947 = false` | 1102, 1418-1419 | flag, 0 bytes | `castsShadow = false` | 2722 | AGREE | Client default is `true` (line 232), matching ours. |
| 65 | `readUnsignedShort()` | `anInt2938` | 1103-1104 | `ReadUnsignedShort()` | `scaleX` | 1312 | AGREE | |
| 66 | `readUnsignedShort()` | `anInt2954` | 1105, 1415-1416 | `ReadUnsignedShort()` | `scaleY` | 2405 | AGREE | |
| 67 | `readUnsignedShort()` | `anInt2929` | 1106-1107 | `ReadUnsignedShort()` | `scaleZ` | 1281 | AGREE | |
| 68 | not handled | - | absent; chain steps 67 (1106) -> 69 (1108) | `ReadUnsignedShort()` | `mapSceneId` | 0 | CODEC-ONLY | The chain tests 67 then immediately `i != 69`; nothing catches 68. Absent from the cache too. |
| 69 | `readUnsignedByte()` | `anInt2948` | 1108, 1412-1413 | `ReadByte()` | `minimapForceClip` | 2537 | AGREE | |
| 70 | `readShort() << 2` | `anInt2973` | 1109-1110 | `ReadShort() << 2` | `offsetX` | 434 | AGREE | Signed short, shifted. |
| 71 | `readShort() << 2` | `anInt2997` | 1111-1112 | `ReadShort() << 2` | `offsetY` | 710 | AGREE | Signed short, shifted. |
| 72 | `readShort() << 2` | `anInt2946` | 1113, 1409-1410 | `ReadUnsignedByte()` | `offsetZ` | 371 | SIZE-DIFFERS | **Our decoder is wrong and desyncs.** The client reads two bytes, exactly like 70 and 71, and `anInt2946` is used interchangeably with `anInt2973`/`anInt2997` at lines 582 and 774. The cache agrees: the two-byte reading walks all 371 definitions clean, the one-byte reading desyncs on 31 of them before other opcodes mask the rest. `Encode` (ObjectDefinition.cs:691) writes one byte back, so the error is symmetric on save. |
| 73 | flag, 0 bytes | `aBoolean2969 = true` | 1114, 1406-1407 | flag, 0 bytes | `obstructsWheelchair = true` | 6300 | AGREE | |
| 74 | flag, 0 bytes | `clippingFlag = true` | 1115, 1403-1404 | flag, 0 bytes | `isSolid = true` | 652 | AGREE | |
| 75 | `readUnsignedByte()` | `anInt2975` | 1116, 1399-1401 | nothing, 0 bytes | none | 1591 | SIZE-DIFFERS | **Our decoder is wrong and desyncs.** The client reads one unsigned byte into `anInt2975`, whose default is -1 (line 254), so it is a value, not a bare flag. This is the single largest defect found: it is the first bad opcode in 1,408 of the 56,199 definitions. `Encode` (ObjectDefinition.cs:695) emits a bare `75` with no payload, so the byte is also lost on save. |
| 77 | `readUnsignedShort()` (65535 -> -1) x2; `ubyte n`; (n+1) x `readUnsignedShort()` (65535 -> -1) | `anInt2983`, `anInt2968`, `anIntArray2928[]` | 1117, 1354-1397 | `SmartOrMinus1` x2; `ReadByte` n; (n+1) x `SmartOrMinus1` | `morphVarbit`, `morphVarp`, `morphIds[]` | 2317 | AGREE | Array is allocated at n+2 in both, with the last slot holding the opcode-92 default (-1 for opcode 77). |
| 78 | `readUnsignedShort()`; `readUnsignedByte()` | `anInt2996`; `anInt2981` | 1119-1123 | `ReadUnsignedShort()`; `ReadByte()` | `ambientSoundId`; `ambientSoundLoops` | 1697 | SEMANTICS-DIFFER | Sizes agree. The client's first field here, `anInt2996`, is a **different** field from opcode 79's first field - see opcode 79. |
| 79 | `readUnsignedShort()`; `readUnsignedShort()`; `readUnsignedByte()`; `ubyte n`; n x `readUnsignedShort()` | `anInt2949`; `anInt2972`; `anInt2981`; `anIntArray2926[]` | 1124, 1334-1352 | `ReadUnsignedShort()`; `ReadUnsignedShort()`; `ReadUnsignedByte()`; `ReadByte` n; n x `ReadUnsignedShort()` | `ambientSoundId`; `ambientSoundExtra`; `ambientSoundLoops`; `extraSounds[]` | 218 | SEMANTICS-DIFFER | Sizes agree exactly. Our codec conflates opcode 78's and opcode 79's first field into one `ambientSoundId`; the client keeps them apart and copies both into the sound emitter as separate slots - `anInt4210 <- anInt2996` and `anInt4219 <- anInt2949` (Node_Sub31_Sub4.java:108,118 and Node_Sub42.java:140,144). 81 definitions in the cache carry both opcodes, and for those our decoder's second read clobbers the first. The third field, `anInt2981`, genuinely *is* shared with opcode 78 in the client, so our shared `ambientSoundLoops` is correct. |
| 81 | `256 * readUnsignedByte()` | `aByte2971 = 2`, `anInt2985` | 1125, 1328-1332 | `256 * ReadByte()` | `contourGroundType = 2`, `contourGroundParam` | 514 | AGREE | |
| 82 | flag, 0 bytes | `aBoolean2982 = true` | 1126-1127 | flag, 0 bytes | `mergeNormals = true` | 92 | AGREE | |
| 88 | flag, 0 bytes | `aBoolean2935 = false` | 1128-1129 | flag, 0 bytes | `noShadow = true` | 4073 | AGREE | Ours inverts the sense and renames; same zero-payload behaviour. |
| 89 | flag, 0 bytes | `aBoolean2925 = false` | 1130-1132 | flag, 0 bytes | `noDecor = true` | 605 | AGREE | Ours inverts the sense and renames. |
| 90 | not handled | - | absent; chain steps 89 (1130) -> 91 (1133) | flag, 0 bytes | `unknownFlag90` | 0 | CODEC-ONLY | Nothing catches 90. Absent from the cache too; harmless either way because it is a zero-byte opcode. |
| 91 | flag, 0 bytes | `aBoolean2927 = true` | 1133-1134 | flag, 0 bytes | `unknownFlag91` | 334 | AGREE | |
| 92 | as opcode 77, plus one extra `readUnsignedShort()` (65535 -> -1) after the two varbit/varp reads | `anInt2983`, `anInt2968`, `i_75_`, `anIntArray2928[]` | 1117, 1354-1397 (extra read at 1372-1379) | same | `morphVarbit`, `morphVarp`, default id, `morphIds[]` | 183 | AGREE | The extra id lands in `anIntArray2928[n+1]` (line 1396), which is what ours does. No cache definition carries both 77 and 92. |
| 93 | `readUnsignedShort()` | `aByte2971 = 3`, `anInt2985` | 1135, 1323-1326 | `ReadUnsignedShort()` | `contourGroundType = 3`, `contourGroundParam` | 65 | AGREE | |
| 94 | flag, 0 bytes | `aByte2971 = 4` | 1136-1137 | flag, 0 bytes | `contourGroundType = 4` | 145 | AGREE | |
| 95 | `readShort()` | `aByte2971 = 5`, `anInt2985` | 1138-1142 | `ReadShort()` | `contourGroundType = 5`, `contourGroundParam` | 383 | AGREE | Signed, unlike opcode 93. |
| 96 | not handled | - | absent; chain steps 95 (1138) -> 97 (1143) | flag, 0 bytes | `unknownFlag96` | 0 | CODEC-ONLY | Nothing catches 96. Absent from the cache too; harmless because it is zero-byte. |
| 97 | flag, 0 bytes | `aBoolean3004 = true` | 1143-1145 | flag, 0 bytes | `unknownFlag97` | 9 | AGREE | |
| 98 | flag, 0 bytes | `aBoolean3005 = true` | 1146-1147 | flag, 0 bytes | `unknownFlag98` | 22 | AGREE | |
| 99 | `readUnsignedByte()`; `readUnsignedShort()` | `anInt3002`; `anInt3008` | 1148, 1317-1321 | `ReadByte()`; `ReadUnsignedShort()` | `cursorType1`; `cursorSprite1` | 7311 | AGREE | |
| 100 | `readUnsignedByte()`; `readUnsignedShort()` | `anInt2933`; `anInt2977` | 1150-1154 | `ReadByte()`; `ReadUnsignedShort()` | `cursorType2`; `cursorSprite2` | 265 | AGREE | |
| 101 | `readUnsignedByte()` | `anInt2962` | 1155-1157 | `ReadByte()` | `ambientVolume` | 0 | AGREE | Handled by both, present in no 639 definition. |
| 102 | `readUnsignedShort()` | `anInt2990` | 1158-1160 | `ReadUnsignedShort()` | `mapAreaId` | 3267 | AGREE | |
| 103 | flag, 0 bytes | `anInt2956 = 0` | 1161, 1314-1315 | flag, 0 bytes | `obstructsGround = 0` | 1232 | AGREE | |
| 104 | `readUnsignedByte()` | `anInt2987` | 1163-1165 | `ReadByte()` | `soundVolume` | 365 | AGREE | Client default is 255 (line 281). |
| 105 | flag, 0 bytes | `aBoolean2976 = true` | 1166, 1311-1312 | flag, 0 bytes | `unknownFlag105` | 0 | AGREE | Present in the client, contrary to an earlier note; absent from the 639 cache. |
| 106 | `ubyte n`; n x (`readUnsignedShort()`, `readUnsignedByte()`) | `anIntArray2979[]`, `anIntArray2937[]`, running sum into `anInt2964` | 1167-1185 | same | `animationIds[]`, `animationWeights[]` | 3 | AGREE | The client also accumulates the weights into `anInt2964`; ours does not, which is a derived value only. |
| 107 | `readUnsignedShort()` | `anInt2958` | 1186, 1307-1308 | `ReadUnsignedShort()` | `mapIconId` | 170 | AGREE | |
| 150 | `readString()`, nulled again when `aClass302_2963.aBoolean2516` is clear | `aStringArray2939[0]` | 1187, 1297-1305 | `ReadJagexString()` | `menuOps[0]` | 442 | AGREE | Client guard is `i < 150 \|\| i >= 155`. It writes into the *same* array as opcodes 30-34, at index `i - 150`, so 150-154 overwrite the 30-34 entries; ours keeps them in a separate `menuOps` array. That is a client display quirk, not a wire-format difference. |
| 151 | as 150 | `aStringArray2939[1]` | 1297-1305 | `ReadJagexString()` | `menuOps[1]` | 220 | AGREE | |
| 152 | as 150 | `aStringArray2939[2]` | 1297-1305 | `ReadJagexString()` | `menuOps[2]` | 136 | AGREE | |
| 153 | as 150 | `aStringArray2939[3]` | 1297-1305 | `ReadJagexString()` | `menuOps[3]` | 1 | AGREE | |
| 154 | as 150 | `aStringArray2939[4]` | 1297-1305 | `ReadJagexString()` | `menuOps[4]` | 188 | AGREE | |
| 160 | `ubyte n`; n x `readUnsignedShort()` | `anIntArray2934[]` | 1189-1200 | same | `minimapIcons[]` | 57 | AGREE | |
| 162 | `readInt()` | `aByte2971 = 3`, `anInt2985` | 1201-1204 | `ReadInt()` | `contourGroundType = 3`, `unknownInt162` | 0 | AGREE | Present in the client, contrary to an earlier note; absent from the 639 cache. Same contour type as opcode 93 but a 4-byte parameter. |
| 163 | 4 x `readSignedByte()` | `aByte2930`, `aByte2942`, `aByte2967`, `aByte2932` | 1205-1213 | 4 x `ReadSignedByte()` | `unknownByte163a`..`d` | 14 | AGREE | `aByte2932` is tested at line 601 as a render-flag trigger. |
| 164 | `readShort()` | `anInt2940` | 1214-1216 | `ReadShort()` | `unknownShort164` | 0 | AGREE | Present in the client, contrary to an earlier note; absent from the 639 cache. |
| 165 | `readShort()` | `anInt2988` | 1217, 1293-1295 | `ReadShort()` | `unknownShort165` | 0 | AGREE | Present in the client, contrary to an earlier note; absent from the 639 cache. |
| 166 | `readShort()` | `anInt2989` | 1219, 1289-1291 | `ReadShort()` | `unknownShort166` | 0 | AGREE | Present in the client, contrary to an earlier note; absent from the 639 cache. |
| 167 | `readUnsignedShort()` | `anInt2945` | 1220, 1285-1287 | `ReadUnsignedShort()` | `unknownShort167` | 228 | AGREE | Unsigned, unlike 164-166. |
| 168 | flag, 0 bytes | `aBoolean2992 = true` | 1221-1222 | flag, 0 bytes | `unknownFlag168` | 324 | AGREE | |
| 169 | flag, 0 bytes | `aBoolean2957 = true` | 1223-1224 | flag, 0 bytes | `unknownFlag169` | 21 | AGREE | |
| 170 | `readSmart(...)` | `anInt2986` | 1225, 1280-1283 | `ReadUnsignedSmart()` | `unknownSmart170` | 3434 | AGREE | Same 1-or-2-byte rule and same 32768 bias. |
| 171 | `readSmart(...)` | `anInt2953` | 1227, 1275-1278 | `ReadUnsignedSmart()` | `unknownSmart171` | 145 | AGREE | |
| 173 | `readUnsignedShort()` x2 | `anInt3006`, `anInt2950` | 1228-1232 | `ReadUnsignedShort()` x2 | `unknownShort173a`, `b` | 0 | AGREE | Present in the client, contrary to an earlier note; absent from the 639 cache. |
| 177 | flag, 0 bytes | `aBoolean2984 = true` | 1233-1234 | flag, 0 bytes | `unknownFlag177` | 0 | AGREE | Present in the client, contrary to an earlier note; absent from the 639 cache. |
| 178 | `readUnsignedByte()` | `anInt2970` | 1235-1238 | `ReadByte()` | `unknownByte178` | 41 | AGREE | |
| 189 | not handled | - | absent from 1036-1486 | flag, 0 bytes | `unknownFlag189` | 0 | CODEC-ONLY | |
| 190 | not handled | - | absent from 1036-1486 | `ReadUnsignedShort()` | `extraOpcodeArray[0]` | 0 | CODEC-ONLY | |
| 191 | not handled | - | absent from 1036-1486 | `ReadUnsignedShort()` | `extraOpcodeArray[1]` | 0 | CODEC-ONLY | |
| 192 | not handled | - | absent from 1036-1486 | `ReadUnsignedShort()` | `extraOpcodeArray[2]` | 0 | CODEC-ONLY | |
| 193 | not handled | - | absent from 1036-1486 | `ReadUnsignedShort()` | `extraOpcodeArray[3]` | 0 | CODEC-ONLY | |
| 194 | not handled | - | absent from 1036-1486 | `ReadUnsignedShort()` | `extraOpcodeArray[4]` | 0 | CODEC-ONLY | |
| 195 | not handled | - | absent from 1036-1486 | `ReadUnsignedShort()` | `extraOpcodeArray[5]` | 0 | CODEC-ONLY | |
| 249 | `ubyte n`; per entry `readUnsignedByte()` type, `method1186()` key, then `readInt()` or `readString()` | `aRSArray_2944` | 1239-1273 | `ReadByte()`, `ReadMedium()`, `ReadInt()`/`ReadJagexString()` | `parameters` | 143 | AGREE | `method1186()` is a 3-byte big-endian unsigned medium (RSBuffer.java:131), matching our `ReadMedium()`. |

There is no CLIENT-ONLY row: every opcode the 637 client handles is also handled by our decoder.

## Disagreements

Two of these break the parse. The rest are labelling only.

### Parse-breaking

- **Opcode 75.** The client reads one unsigned byte (Class352.java:1400); we read nothing. It
  occurs in **1,591** of the 56,199 639 definitions and is the first bad opcode in 1,408 of the
  1,897 definitions our size table cannot walk. `Encode` also drops the byte.
- **Opcode 72.** The client reads a signed short and shifts it left two (Class352.java:1410),
  identically to opcodes 70 and 71; we read one unsigned byte. It occurs in **371** definitions
  and is the first bad opcode in 31 of the desynced ones (the other 340 are already desynced by
  opcode 75 before reaching it). `Encode` writes one byte back.

A prior note claimed both had already been fixed. They have not been, in this working tree:
`ObjectDefinition.cs:368` still reads `buf.ReadUnsignedByte()` for 72 and `ObjectDefinition.cs:372`
is still a comment-only no-op for 75. This document deliberately does not change the code.

### Labelling only, no byte impact

- **Opcodes 14 and 15.** The client assigns 14 to `sizeY` and 15 to `sizeX`; we do the reverse.
  Both occur in the cache in bulk (8,713 and 8,290 definitions) but both are one unsigned byte,
  so nothing desyncs. The client's field names come from a deobfuscator and could themselves be
  transposed; this is recorded, not resolved.
- **Opcodes 78 and 79.** The client's first field differs between the two opcodes (`anInt2996`
  versus `anInt2949`) and both are forwarded to the sound emitter as separate slots
  (Node_Sub31_Sub4.java:108,118). We store both in one `ambientSoundId`. **81** definitions in
  the cache carry both opcodes, and in those our second read overwrites the first. Both opcodes
  are read at the correct widths, so this never desyncs; it only loses a value. The earlier note
  put this count at 44, which does not match the cache.
- **Opcode 24.** The client maps a read value of 65535 to -1 (Class352.java:1455-1457); we keep
  65535. 5,423 definitions carry the opcode. Two bytes either way, and our encoder writes the
  stored value straight back, so it round-trips.
- **Opcodes 28, 29 and 39.** Widths all agree. The client's brightness/ambient term is opcode 29
  (`64 + anInt2931`, line 568) and its contrast term is opcode 39 (`850 + anInt2980`, line 569),
  both consumed by the model-lighting call at line 684. Our decoder sets `modelBrightness` from
  opcode 28 and `modelContrast` from opcode 29, so both public lighting fields carry the wrong
  value. Opcode 28's client field is a decor displacement (Class305_Sub1.java:953-954), which is
  what our own `decorDisplacement` name already says.

### Opcodes we handle that the 637 client does not

44, 45, 68, 90, 96, 189, 190, 191, 192, 193, 194 and 195. **None of them occur in any of the
56,199 639 object definitions**, so they are unreachable on this cache and the extra handlers are
inert. Note that 68, 90 and 96 were not on the earlier list; conversely 105, 162, 164, 165, 166,
173 and 177 were on it but *are* handled by the 637 client - they are simply unused by the 639
data.

### Opcodes both sides handle that the 639 cache never uses

42, 101, 105, 162, 164, 165, 166, 173 and 177. Nothing to fix; noted so nobody mistakes an absence
of cache evidence for an absence of the opcode.

## What could not be determined

- **Whether the client's `sizeX`/`sizeY` naming for opcodes 14 and 15 is correct.** The names come
  from a deobfuscator pass, not from the bytecode, and nothing in `Class352.java` distinguishes the
  two fields' roles well enough to arbitrate. Only the disagreement is recorded.
- **The meaning of opcode 75's byte.** The client stores it in `anInt2975`, default -1
  (Class352.java:254). No read of `anInt2975` was traced beyond the decoder, so its purpose is
  unknown; only its width is established, and that is established twice over.
- **The meaning of the remaining `unknownFlag*` / `unknownShort*` fields** (91, 97, 98, 163, 167,
  168, 169, 170, 171, 178). Their widths are confirmed against the client and the cache; their
  semantics were not traced.
- **Whether opcodes 44, 45, 68, 90, 96, 189 and 190-195 mean anything in any revision.** They are
  absent from the 637 client and from the 639 cache, so neither source can say. Their presence in
  our codec is unexplained by anything in this repository's two reference points.
- **Whether the field-name transposition on 78/79 matters to any consumer.** The editor's own use
  of `ambientSoundId` was not audited.
