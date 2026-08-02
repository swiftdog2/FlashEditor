# NPC Definition Opcodes - 637 Client vs FlashEditor Codec

## What this document is

A per-opcode de-obfuscation of the NPC definition decoder in the Hydra 637 Java client,
set beside FlashEditor's own NPC decoder, with every client claim carrying a line citation.

Two different authorities are in play and they are **not** interchangeable:

- **The 639 cache is the authority on payload sizes.** It is the data this editor actually reads
  and writes. If the cache says an opcode carries four bytes, it carries four bytes.
- **The 637 client is the authority on meaning and signedness.** Only the client tells you that
  a byte is read signed, or that a value is multiplied by 5, or that a field ends up as a
  shadow alpha rather than a sound id. The cache can never reveal any of that.

The client and the cache are a mismatched pair (see `AGENTS.md`): the cache is build 639, the
bundled client writes `637` in its handshake and login blocks. Where they disagree, **both
readings are recorded here and neither is presented as the one to "fix" the other with.**

## Sources

| Side | File | Entry point |
| --- | --- | --- |
| Client | `C:\Users\Cristian.Rosu\source\repos\Personal\HydraScape\client\src\Class141.java` | `method2297` (loop, lines 675-695) dispatching to `method2293` (lines 283-607) |
| Ours | `FlashEditor/Definitions/NPCDefinition.cs` | `Decode(JagStream, int[])` lines 166-177 dispatching to `Decode(JagStream, int)` lines 184-529 |
| Buffer | `C:\Users\Cristian.Rosu\source\repos\Personal\HydraScape\client\src\RSBuffer.java` | `readSignedByte` 853, `readString` 878, `readUnsignedByte` 896, `readUnsignedShort` 901, `method1186` (3-byte big-endian medium) 131 |
| Buffer | `FlashEditor/IO/JagStream.cs` | `ReadByte` 217, `ReadSignedByte` 301, `ReadUnsignedByte` 315, `ReadUnsignedShort` 345, `ReadShort` 355, `ReadMedium` 377, `ReadInt` 399, `ReadJagexString` 580 |

### Reading the obfuscation

`method2293` is one deeply nested if/else chain, and the decompiler rewrote most equality tests
into ones-complement form. `(op ^ 0xffffffff) == -N` is `~op == -N`, which is `op == N - 1`.
So `(i_0_ ^ 0xffffffff) == -102` is `op == 101`. Ranges follow the same trick:
`((op ^ 0xffffffff) <= -151) && (op < 155)` at line 466 is `150 <= op <= 154`.

Because the chain is nested rather than flat, the *body* of a case is frequently hundreds of
lines below or above its *test*. Both line numbers are cited where they differ meaningfully.

### One critical primitive difference

`JagStream.ReadByte()` (line 217) returns **0-255**, not -128..127. It is an *unsigned* byte read
that happens to be named like Java's signed one. Every `stream.ReadByte()` in our decoder is
therefore equivalent to the client's `readUnsignedByte()`, never to `readSignedByte()`.
`JagStream.ReadShort()` (line 355), by contrast, genuinely is signed.

---

## Opcode table

Column meanings:

- **Client read** - the literal expression from `Class141.java`, or a short description for loops.
- **Client ref** - `Class141.java:LINE`. Every claim about the client cites one.
- **Our field** - the C# field, with the `NPCDefinition.cs` line in parentheses.
- **Verdict** - `AGREE`, `SIZE-DIFFERS`, `SIGNEDNESS-DIFFERS`, `SEMANTICS-DIFFER`,
  `CODEC-ONLY` (we handle it, the 637 client does not), `CLIENT-ONLY` (the reverse).

| Opcode | Client read (exact expression) | Client field | Client ref | Our read | Our field | Verdict | Notes |
| --- | --- | --- | --- | --- | --- | --- | --- |
| 1 | `n = readUnsignedByte()`, then n x `readUnsignedShort()` with `65535 -> -1` | `anIntArray1107` | Class141.java:290-301 | `n = ReadByte()`, then n x `ReadUnsignedShort()` with `65535 -> -1` | `modelIds` (186-194) | AGREE | Primary render model list. Consumed as the model array in `method2301` (Class141.java:1144). |
| 2 | `readString()` | `aString1144` | Class141.java:601 | `ReadJagexString()` | `name` (196-198) | AGREE | Client default is the literal `"null"` (Class141.java:258); ours matches (line 116). |
| 12 | `readUnsignedByte()` | `anInt1112` | Class141.java:304 | `ReadByte()` | `size` (200-201) | AGREE | Tile footprint. Prior lead flagged this as a signedness mismatch: **refuted**, the client reads it unsigned. |
| 30-34 | `readString()` into `[op - 30]` | `aStringArray1148` | Class141.java:598 | `ReadJagexString()`, then `null` if the text is exactly `"Hidden"` | `options[op - 30]` (205-215) | SEMANTICS-DIFFER | Wire-identical; the 637 client has **no** `"Hidden"` special case in this range. Also note the client's array is 5 wide (Class141.java:272) while ours is 6 with `"Examine"` pre-seeded at index 5 (NPCDefinition.cs:118). |
| 40 | `n = readUnsignedByte()`, then n x (`(short) readUnsignedShort()`, `(short) readUnsignedShort()`) | `aShortArray1108` (src), `aShortArray1105` (dst) | Class141.java:307-315 | `n = ReadByte()`, then n x (`ReadShort()`, `ReadShort()`) | `recolorSrc`, `recolorDst` (217-226) | AGREE | Read order src-then-dst confirmed by the recolour apply loop `renderable.ia(aShortArray1108[i], is_91_[i])` where `is_91_` defaults to `aShortArray1105` (Class141.java:1241, 1249). The client reads unsigned then narrows to Java `short`, so the stored value is signed 16-bit exactly as ours is. |
| 41 | `n = readUnsignedByte()`, then n x (`(short) readUnsignedShort()`, `(short) readUnsignedShort()`) | `aShortArray1155` (src), `aShortArray1137` (dst) | Class141.java:317-325 | `n = ReadByte()`, then n x (`ReadShort()`, `ReadShort()`) | `retextureSrc`, `retextureDst` (228-237) | AGREE | Same narrow-to-`short` argument as opcode 40. Apply loop at Class141.java:1264. |
| 42 | `n = readUnsignedByte()`, then n x `readSignedByte()` | `aByteArray1136` | Class141.java:327-333 | `n = ReadByte()`, then n x `(byte) ReadByte()` | `recolorDstPalette` (239-245) | AGREE | Bit-identical: we store the raw byte, and the client itself re-widens with `& 0xff` at the use site (`Class265.aShortArray1977[aByteArray1136[i] & 0xff]`, Class141.java:1247). Palette-index override for the opcode 40 recolour destination. |
| 44 | *not handled* | - | Class141.java:283-607 (no case) | `ReadShort()` (2 bytes) | `op44` (247-249) | CODEC-ONLY | The 637 chain has no test for 44; unmatched opcodes fall out of the innermost `if(i_0_ == 249)` at Class141.java:488, which has no `else`, and are silently ignored. |
| 45 | *not handled* | - | Class141.java:283-607 (no case) | `ReadShort()` (2 bytes) | `op45` (251-253) | CODEC-ONLY | As opcode 44. |
| 60 | `n = readUnsignedByte()`, then n x `readUnsignedShort()` | `anIntArray1117` | Class141.java:335-341 | `n = ReadByte()`, then n x `ReadUnsignedShort()` | `dialogueModels` (255-261) | AGREE | Separate model list used by `method2299` (Class141.java:769), the chathead/dialogue render path, distinct from the opcode 1 list. |
| 93 | *no payload* | `aBoolean1140 = false` | Class141.java:595 (test at 342) | *no payload* | `drawMinimapDot = false` (263-265) | AGREE | Gates the minimap dot in `Class201.java:113`. |
| 95 | `readUnsignedShort()` | `anInt1115` | Class141.java:344 | `ReadShort()` | `level` (267-269) | SIGNEDNESS-DIFFERS | Combat level. Wire size identical, so round-trip is unaffected. Max observed in the 639 cache is 1001, so the divergence is unobservable here. |
| 97 | `readUnsignedShort()` | `anInt1121` | Class141.java:592 (test at 345) | `ReadShort()` | `scaleXY` (271-273) | SIGNEDNESS-DIFFERS | Horizontal model scale; client default 128 (Class141.java:253), applied via `renderable_95_.O(anInt1121, anInt1142, anInt1121)` (Class141.java:1369). Max observed 512. |
| 98 | `readUnsignedShort()` | `anInt1142` | Class141.java:347 | `ReadShort()` | `scaleZ` (275-277) | SIGNEDNESS-DIFFERS | Vertical scale; client default 128 (Class141.java:257). Max observed 300. |
| 99 | *no payload* | `aBoolean1106 = true` | Class141.java:589 (test at 348) | *no payload* | `hasRenderPriority = true` (279-281) | AGREE | |
| 100 | `readSignedByte()` | `anInt1104` | Class141.java:586 (test at 349) | `ReadByte()` (unsigned 0-255) | `ambient` (283-285) | SIGNEDNESS-DIFFERS | Confirmed as ambient: consumed as `64 + anInt1104` in the renderable build (Class141.java:1232). A stored `0xF0` means -16 to the client and 240 to us. Prior lead **confirmed**. |
| 101 | `readSignedByte() * 5` | `anInt1131` | Class141.java:351 | `ReadByte()`, no multiply | `contrast` (287-289) | SEMANTICS-DIFFER | Confirmed as contrast: consumed as `anInt1131 + 850` (Class141.java:1233). Two independent divergences - the sign, and the `* 5` scale factor we do not apply. The field name `contrast` is correct; the stored value is a fifth of the client's working value. Prior lead **confirmed**. |
| 102 | `readUnsignedShort()` | `anInt1113` | Class141.java:353 | `ReadShort()` | `headIcon` (291-293) | SIGNEDNESS-DIFFERS | Max observed 24. |
| 103 | `readUnsignedShort()` | `anInt1091` | Class141.java:583 (test at 354) | `ReadShort()` | `rotation` (295-297) | SIGNEDNESS-DIFFERS | Client default 32 (Class141.java:267), used as `anInt1091 << 3` for the facing angle (Class21_Sub2.java:271, Class341.java:70). Max observed 5000. |
| 106 | `varbit = readUnsignedShort()` (`65535 -> -1`); `varp = readUnsignedShort()` (`65535 -> -1`); `c = readUnsignedByte()`; array sized `c + 2`; `c + 1` x `readUnsignedShort()` (`65535 -> -1`); `[c + 1] = -1` | `anInt1146`, `anInt1151`, `anIntArray1109` | Class141.java:355-390 | identical | `varbit`, `varp`, `morphs` (299-321) | AGREE | Morph variant table. The trailing slot is the fallback id, read only by opcode 118. |
| 107 | *no payload* | `aBoolean1116 = false` | Class141.java:392 | *no payload* | `clickable = false` (323-325) | AGREE | |
| 109 | *no payload* | `aBoolean1126 = false` | Class141.java:580 (test at 393) | *no payload* | `slowWalk = false` (327-329) | AGREE | Read back in `Class333.java:150`. |
| 111 | *no payload* | `aBoolean1130 = false` | Class141.java:577 (test at 394) | *no payload* | `animateIdle = false` (331-333) | AGREE | Gates the shadow render in `Particle_Sub3_Sub4_Sub2_Sub1.java:78`. |
| 112 | *not handled* | - | Class141.java:283-607 (no case) | `(sbyte) ReadByte()` (1 byte) | `anInt1104` (335-337) | CODEC-ONLY | The chain jumps straight from the 111 test (line 394) to the 113 test (line 395). Note our field is confusingly named `anInt1104`, which in the client is the **opcode 100** field, not anything to do with 112. |
| 113 | `(short) readUnsignedShort()`; `(short) readUnsignedShort()` | `aShort1094`, `aShort1135` | Class141.java:573-574 (test at 395) | `(short) ReadShort()`; `(short) ReadShort()` | `primaryShadowColour`, `secondaryShadowColour` (339-342) | AGREE | Our names are accurate. Both are packed HSL colours handed to the procedural shadow-disc builder `Class102.method1703` (call site Particle_Sub3_Sub4_Sub2_Sub1.java:87, 89); the builder blends the first toward the second across three concentric rings (Class102.java:96-99). Client narrows to `short` and re-widens with `& 0xffff`, matching our bit pattern. Max observed 14896. |
| 114 | `readSignedByte()`; `readSignedByte()` | `aByte1122`, `aByte1134` | Class141.java:397-398 | `(sbyte) ReadByte()`; `(sbyte) ReadByte()` | `primaryShadowModifier`, `secondaryShadowModifier` (344-347) | AGREE | The two alpha stops of the same shadow disc, used `& 0xff` (Particle_Sub3_Sub4_Sub2_Sub1.java:85-86). Client defaults -96 and -16 (Class141.java:236, 264); ours -33 and -113 (NPCDefinition.cs:10, 12), which differ, but defaults are not opcode behaviour. |
| 118 | as opcode 106, plus a third `readUnsignedShort()` (`65535 -> -1`) stored into the last array slot | `anInt1146`, `anInt1151`, `anIntArray1109` | Class141.java:355-390 (extra read at 370-376) | identical | `varbit`, `varp`, `morphs`, `last` (299-321) | AGREE | Shares the 106 body; the extra read is gated on `i_0_ == 118` at Class141.java:370. |
| 119 | `readSignedByte()` | `aByte1103` | Class141.java:400 | `(sbyte) ReadByte()` | `walkMask` (349-351) | AGREE | Read back at `client.java:2038`. |
| 121 | allocate `new int[anIntArray1107.length][]`; `n = readUnsignedByte()`; per record `idx = readUnsignedByte()` then 3 x `readSignedByte()` | `anIntArrayArray1124` | Class141.java:402-413 | allocate `new int[modelIds.Length][]`; `n = ReadByte()`; per record `idx = ReadByte()` then 3 x `ReadSignedByte()` | `translations` (354-365) | AGREE | Sparse per-model translation records; unlisted slots stay null on both sides. Both implementations depend on opcode 1 having already been seen. |
| 122 | `readUnsignedShort()` | `anInt1127` | Class141.java:415 | `ReadShort()` | `hitbarSprite` (367-369) | SIGNEDNESS-DIFFERS | Read back at `IntegerNode.java:69`. Max observed 3092. |
| 123 | `readUnsignedShort()` | `anInt1092` | Class141.java:417 | `ReadShort()` | `height` (371-373) | SIGNEDNESS-DIFFERS | Client default -1 (Class141.java:233); read back at `Particle_Sub3_Sub4_Sub2_Sub1.java:327-328`. Max observed 1600. |
| 125 | `readSignedByte()` | `aByte1141` | Class141.java:419 | `(byte) ReadByte()` | `respawnDirection` (375-377) | SIGNEDNESS-DIFFERS | Used as `(aByte1141 + 4 << 11) & 0x3fda` (Particle_Sub3_Sub2.java:270), so the sign genuinely participates. Client default 4 (Class141.java:260); ours 7 (NPCDefinition.cs:11). |
| 127 | `readUnsignedShort()` | `anInt1145` | Class141.java:421 | `ReadShort()` | `renderTypeID` (379-381) | SIGNEDNESS-DIFFERS | Resolves to a `Class294` animation/translation set (Class141.java:1133-1134). Max observed 1972. |
| 128 | `readUnsignedByte()` - **result discarded** | *(none)* | Class141.java:423 | `ReadByte()` | `movementType` (383-385) | SEMANTICS-DIFFER | Same one-byte payload, but the 637 client reads and throws the value away: the statement is a bare call with no assignment. Prior lead flagged this as a signedness mismatch: **refuted** on signedness (the client read is unsigned), but there is a real and larger divergence - the client attaches no meaning to it at all, so our `movementType` name is unverifiable from this client. |
| 134 | 4 x `readUnsignedShort()` each with `65535 -> -1`, then `readUnsignedByte()` | `anInt1120`, `anInt1132`, `anInt1102`, `anInt1147`, `anInt1128` | Class141.java:425-449 (test at 424) | 4 x `ReadShort()` each with a `== 65535` guard, then `ReadByte()` | `idleSound`, `crawlSound`, `walkSound`, `runSound`, `soundDistance` (387-394) | SIGNEDNESS-DIFFERS | Nine bytes on both sides. Our `== 65535` guards are dead code, because `ReadShort()` has already turned `0xFFFF` into -1, so the net result still matches the client. In this cache no sound field ever reaches the sentinel anyway (max observed 9305). The presence of any of these three ids is what `method2302` tests for (Class141.java:1396). |
| 135 | `readUnsignedByte()`; `readUnsignedShort()` | `anInt1143`, `anInt1154` | Class141.java:451-452 | `ReadByte()`; `ReadShort()` | `primaryCursorOp`, `primaryCursor` (396-399) | SIGNEDNESS-DIFFERS | The **byte** agrees - the client reads it unsigned. Prior lead claimed a signed-byte mismatch here: **refuted**. The residual divergence is the 16-bit half only. Max observed 169. |
| 136 | `readUnsignedByte()`; `readUnsignedShort()` | `anInt1114`, `anInt1110` | Class141.java:454-455 | `ReadByte()`; `ReadShort()` | `secondaryCursorOp`, `secondaryCursor` (401-404) | SIGNEDNESS-DIFFERS | As opcode 135, including the refuted signed-byte lead. Max observed 60. |
| 137 | `readUnsignedShort()` | `anInt1099` | Class141.java:570 (test at 456) | `ReadShort()` | `attackOpCursor` (406-408) | SIGNEDNESS-DIFFERS | Max observed 42. |
| 138 | `readUnsignedShort()` | `anInt1095` | Class141.java:458 | `ReadShort()` | `armyIcon` (410-412) | SIGNEDNESS-DIFFERS | Max observed 2015. |
| 139 | `readUnsignedShort()` | `anInt1100` | Class141.java:567 (test at 459) | `ReadShort()` | `spriteId` (414-416) | SIGNEDNESS-DIFFERS | Read back at `IntegerNode.java:154`. Does not occur in the 639 cache. |
| 140 | `readUnsignedByte()` | `anInt1156` | Class141.java:564 (test at 460) | `ReadByte()` | `ambientSoundVolume` (418-420) | AGREE | Client default 255 (Class141.java:280), matching ours (NPCDefinition.cs:50). Prior lead flagged a signed-byte mismatch: **refuted**. |
| 141 | *no payload* | `aBoolean1153 = true` | Class141.java:462 | *no payload* | `visiblePriority = true` (422-424) | AGREE | Read back at `client.java:603, 609`. |
| 142 | `readUnsignedShort()` | `anInt1118` | Class141.java:464 | `ReadShort()` | `mapIcon` (426-428) | SIGNEDNESS-DIFFERS | Read back at `Class201.java:117, 121`. Does not occur in the 639 cache. |
| 143 | *no payload* | `aBoolean1149 = true` | Class141.java:561 (test at 465) | *no payload* | `invisiblePriority = true` (430-432) | AGREE | Read back at `client.java:619`. |
| 150-154 | `readString()` into `[op - 150]`, then **discarded** (set to `null`) unless `aClass301_1133.aBoolean2503` | `aStringArray1148` | Class141.java:466-472 | `ReadJagexString()`, then `null` if the text is exactly `"Hidden"` | `options[op - 150]` (434-444) | SEMANTICS-DIFFER | Wire-identical, meanings differ twice over. These are conditional options: the client keeps them only when the session flag `Class301.aBoolean2503` is set, which is pushed from a login-response byte (`Class332_Sub1.java:351, 354`); it does not special-case `"Hidden"`. Ours keeps them unconditionally and instead nulls the literal `"Hidden"`. |
| 155 | `readSignedByte()` x4 | `aByte1111`, `aByte1139`, `aByte1119`, `aByte1138` | Class141.java:555-558 (test at 473) | `ReadByte()` x4 (unsigned) | `hue`, `saturation`, `lightness`, `opacity` (446-451) | SIGNEDNESS-DIFFERS | Four bytes on both sides, and **our field names are essentially right**. The values go to `renderable.method2337(aByte1111, aByte1139, aByte1119, aByte1138 & 0xff)` (Class141.java:855, 1269), which blends every vertex colour toward a target HSL by weight/128 (`Renderable_Sub1.java:1321-1339`); the packing there is 6-bit hue, 3-bit saturation, 7-bit lightness. So the fourth byte is a **blend weight**, not an opacity in the alpha sense, and it is explicitly used unsigned. The first three are read signed precisely so that -1 can mean "leave this channel alone" (`if(i != -1)` at Renderable_Sub1.java:1330, 1333, 1336); read unsigned, that sentinel becomes 255. Prior lead described this as "four signed shadow/alpha bytes": **partially refuted** - shadow bytes are opcodes 113/114, not 155. The real finding is the sign and the -1 sentinel. |
| 158 | *no payload* | `aByte1129 = (byte) 1` | Class141.java:475 (test at 474) | *no payload* | `mainOptionIndex = 1` (453-455) | AGREE | Client's field defaults to -1 and is resolved lazily in `method2295` (Class141.java:619-628) when neither 158 nor 159 was present; ours has no such lazy path and defaults to 0. |
| 159 | *no payload* | `aByte1129 = (byte) 0` | Class141.java:552 (test at 476) | *no payload* | `mainOptionIndex = 0` (457-459) | AGREE | See opcode 158 on the default. |
| 160 | `n = readUnsignedByte()`, then n x `readUnsignedShort()` | `anIntArray1152` | Class141.java:540-549 (test at 477) | `n = ReadByte()`, then n x `ReadShort()` | `campaigns` (461-467) | SIGNEDNESS-DIFFERS | Read back at `Class96.java:237`. Max observed 185. |
| 162 | ***no payload at all*** | `aBoolean1093 = true` | Class141.java:537 (test at 478) | `ReadShort()`; `ReadShort()` (4 bytes) | `anInt1101`, `anInt1090` (469-472) | SEMANTICS-DIFFER | The sharpest divergence in the file. To the 637 client this is a bare flag with no payload, propagated to `Class169.java:69, 84`. We consume four bytes. Prior lead said "the client has no such case": **corrected** - the client *does* have a case for 162, it simply reads nothing. Note also that our field names `anInt1101`/`anInt1090` are the client's **opcode 164** fields, so the naming is doubly misleading. |
| 163 | `readUnsignedByte()` | `anInt1096` | Class141.java:533-534 (test at 479) | `ReadByte()` | `anInt864` (474-476) | AGREE | Read back at `Particle_Sub3_Sub4_Sub2_Sub1.java:188-189`, where it selects between two size-dependent behaviours. Client default -1 (Class141.java:262). |
| 164 | `readUnsignedShort()`; `readUnsignedShort()` | `anInt1101`, `anInt1090` | Class141.java:482-485 (test at 480) | `ReadShort()`; `ReadShort()` | `anInt848`, `anInt837` (478-481) | SIGNEDNESS-DIFFERS | Both client fields default to 256 (Class141.java:238, 263); `anInt1090` is read back at `Node_Sub31_Sub4.java:77` and `Node_Sub42.java:183`. Does not occur in the 639 cache. |
| 165 | `readUnsignedByte()` | `anInt1123` | Class141.java:529-530 (test at 486) | `ReadByte()` | `anInt847` (483-485) | AGREE | Read back at `Particle_Sub3_Sub4_Sub2_Sub1.java:187, 194, 197, 247`. Client default 0 (Class141.java:245). |
| 168 | `readUnsignedByte()` | `anInt1125` | Class141.java:525-526 (test at 487) | `ReadByte()` | `anInt828` (487-489) | AGREE | Used as `anInt1125 << 9` (Node_Sub31_Sub4.java:76, Node_Sub42.java:185). Does not occur in the 639 cache. |
| 170-175 | *not handled* | - | Class141.java:283-607 (no case) | `ReadShort()` (2 bytes each) | `unknownOptions[op - 170]` (491-498) | CODEC-ONLY | Six opcodes, none of which the 637 chain tests for. |
| 179 | *not handled* | - | Class141.java:283-607 (no case) | `ReadByte()` x6 (6 bytes) | `unknownByte1..6` (500-508) | CODEC-ONLY | |
| 249 | `n = readUnsignedByte()`; per entry `isString = readUnsignedByte() == 1`, `key = method1186()` (3-byte big-endian), then `readInt()` when not a string else `readString()` | `aRSArray_1098` | Class141.java:489-522 (test at 488) | `n = ReadByte()`; per entry `isString = ReadByte() == 1`, `key = ReadMedium()`, then `ReadJagexString()` or `ReadInt()` | `config` (510-523) | AGREE | The client's `method1186` (RSBuffer.java:131-135) is a 3-byte big-endian unsigned medium, matching `JagStream.ReadMedium` (JagStream.cs:377-383). Client stores into an `RSArray` hash, we use a `SortedDictionary`; the wire format is the same. |

### Decode-loop framing

| Aspect | Client | Ours | Verdict |
| --- | --- | --- | --- |
| Opcode read | `readUnsignedByte()` (Class141.java:683) | `ReadByte()` (NPCDefinition.cs:169) | AGREE |
| Terminator | breaks on `0` only (Class141.java:685-687) | breaks on `opcode <= 0 \|\| opcode == 255` (NPCDefinition.cs:171-172) | SEMANTICS-DIFFER |
| Unknown opcode | silently ignored, because the innermost `if(i_0_ == 249)` has no `else` (Class141.java:488) | silently ignored via `default:` (NPCDefinition.cs:525-527) | AGREE |

The `255` break is a real divergence: if a definition ever carried opcode 255, the client would
fall through to the unknown-opcode path and keep looping, while we would stop decoding. Our extra
`<= 0` guard also covers `ReadByte()` returning -1 at end of stream, which the client has no
equivalent for. Prior lead **confirmed**.

---

## 1. Disagreements, and whether they are observable in the 639 cache

### Method

Every one of the **13,359** NPC definitions in the cache at
`C:\Users\Cristian.Rosu\source\repos\Personal\FlashEditor\cache` was walked with a throwaway
scanner built against the production `RSCache`/`RSFileStore`/`JagStream` types, using our
decoder's payload sizes. Result:

```
definitions=13359  cleanToExactEnd=13359  unknownOpcode=0
desync=0  readFailed=0  terminator255=0  ranOffEnd=0
```

Every definition consumed its payload and landed exactly on its terminator with zero bytes of
slack, no opcode outside our table appeared, and no definition used a 255 terminator. That is a
strong empirical confirmation that **our size table is the right one for this cache** - a wrong
size anywhere would have desynchronised the walk and shown up as slack or a bogus opcode.

The scanner is scratch tooling and was not committed. No production code or test was modified.

### Opcodes actually present in the 639 cache

`1, 2, 12, 30, 31, 32, 33, 34, 40, 41, 60, 93, 95, 97, 98, 99, 100, 101, 102, 103, 106, 107,
109, 111, 113, 114, 118, 119, 121, 122, 123, 125, 127, 128, 134, 135, 136, 137, 138, 140, 141,
143, 150, 152, 153, 155, 159, 160, 163, 165, 249`

Absent: `42, 44, 45, 112, 139, 142, 151, 154, 158, 162, 164, 168, 170, 171, 172, 173, 174, 175, 179`.

This reproduces the prior sweep's occurrence list exactly. Opcodes 95 and 119 appear in all
13,359 definitions; opcode 165 appears in exactly one.

### The disagreements, ranked

**Divergences on opcodes that DO occur in the 639 cache** (these matter for anyone reading a
decoded definition and believing the field name):

| Opcode | Occurrences | Nature of the disagreement |
| --- | --- | --- |
| 101 | 2245 | We store `byte`; the client stores `signedByte * 5`. Our `contrast` is a fifth of the client's working value and wrong in sign for stored values above 127. |
| 155 | 44 | We read four unsigned bytes; the client reads the first three signed, where -1 is an explicit "skip this channel" sentinel. Our field names are right in substance; the fourth value is a blend weight rather than an opacity. |
| 100 | 3800 | Client reads signed, we read unsigned. Stored values above 127 mean opposite things. |
| 128 | 218 | Same size, but the client reads and discards the byte, so nothing in the 637 client supports our `movementType` name. |
| 125 | 596 | Client reads signed and the sign participates in the arithmetic; we read unsigned. |
| 30-34, 150-154 | 5538 / 4974 / 2216 / 345 / 139, and 21 / 117 / 20 | Wire-identical, but our `"Hidden"` nulling exists in neither range in the 637 client, and the client's own conditional-option gate on 150-154 (a session flag) has no counterpart in ours. |
| 95, 97, 98, 102, 103, 113, 122, 123, 127, 134, 135, 136, 137, 138, 160 | various | 16-bit signedness only. **Not observable in this cache**: the scanner recorded the maximum raw unsigned value for every 16-bit field of every one of these opcodes and none reached `0x8000`, so signed and unsigned reads produce identical numbers throughout. |

**Divergences on opcodes that do NOT occur in the 639 cache** (unobservable here, but recorded
because a modified or future cache could contain them):

| Opcode | Nature |
| --- | --- |
| 162 | We consume 4 bytes; the client consumes 0. Reading a real 162 with our decoder would swallow four bytes of the following opcodes and desynchronise the whole definition. This is the highest-severity latent divergence in the file. |
| 44, 45 | We consume 2 bytes each; no client case. |
| 112 | We consume 1 byte; no client case. |
| 170-175 | We consume 2 bytes each; no client case. |
| 179 | We consume 6 bytes; no client case. |
| 139, 142, 164 | 16-bit signedness only. |
| 42, 168, 158, 151, 154 | Agree; listed only for completeness of the absent set. |

**Decode-loop divergence**: our break on opcode 255 has no client equivalent, and no definition
in this cache uses it, so it is unobservable here.

There are **no `CLIENT-ONLY` opcodes**. Every opcode the 637 client handles, we also handle.
There are **no `SIZE-DIFFERS` verdicts** either: where both sides read an opcode, they always
read the same number of bytes. Every disagreement is about sign, meaning, or existence.

### Prior leads: confirmed, refuted, corrected

| Lead | Outcome |
| --- | --- |
| We and the client disagree on 44, 45, 112, 170-175, 179 (we read a payload, the client has no case) | **Confirmed** - Class141.java:283-607 has no test for any of them. |
| Opcode 162 is the sharpest: we read 4 bytes, the client reads none | **Confirmed on substance, corrected on wording** - the client *has* a case for 162 (test Class141.java:478, body 537), it just sets `aBoolean1093 = true` and reads nothing. |
| None of those opcodes occurs in any of the 13,359 definitions | **Confirmed** by independent re-sweep. |
| Occurrence list for opcodes that DO appear | **Confirmed** exactly. |
| Opcode 155 is `hue/saturation/lightness/opacity` for us but four signed shadow/alpha bytes in the client | **Partially refuted.** It is four signed bytes, but it is not shadow - it is an HSL vertex-colour blend (Renderable_Sub1.java:1321-1339), so our names are substantially correct. The shadow bytes are opcodes 113 and 114. |
| Opcode 101 is `contrast` for us but `signedByte * 5` in the client | **Confirmed on the read**, and the name `contrast` is **vindicated** by `anInt1131 + 850` at Class141.java:1233. |
| Opcodes 100/101/112 may map to different client fields than our names suggest | **Mixed.** 100 (`ambient`) and 101 (`contrast`) are correctly named, confirmed by their use sites. 112 has no client field at all - and our C# field for it, `anInt1104`, collides with the client's opcode-100 field name, which is a genuine naming hazard. |
| Signedness mismatches on 100, 101, 128, 140, 12, 135, 136 | **Split.** Confirmed for 100 and 101. **Refuted for 12, 128, 135, 136 and 140** - the client reads all of those unsigned (Class141.java:304, 423, 451, 454, 564), same as us. 128 has a different and larger problem (discarded value) and 135/136 have a residual mismatch on their 16-bit half only. |
| Our `Decode` breaks on 255, the client does not | **Confirmed** - Class141.java:685-687 breaks on 0 only. |

Two signedness divergences the prior leads did **not** list, and that are worth knowing about:

- Opcode **125** (`respawnDirection`). The client reads it signed (Class141.java:419) and the sign
  participates in the arithmetic at Particle_Sub3_Sub2.java:270; we read it unsigned.
- The whole family of 16-bit fields where we use signed `ReadShort()` against the client's
  `readUnsignedShort()`. Harmless in this cache, but it is a systematic pattern rather than an
  isolated slip.

---

## 2. What could not be determined

Stated plainly, so nobody mistakes silence for agreement:

1. **Whether the 639 cache's producer shares the 637 client's semantics.** Everything in the
   Verdict column compares us to a client that is two builds off. Where the two disagree, this
   document records both readings; it does not and cannot rule that either is what the 639
   content authors intended.

2. **The meaning of opcodes 44, 45, 112, 170-175 and 179.** They exist only in our decoder. The
   637 client offers zero evidence about them, and they never occur in the cache, so their sizes
   in our decoder are unverified by *either* authority. They came from somewhere else and that
   provenance was not traced.

3. **The correct payload size for opcode 162 in a 639 cache.** The client says zero bytes, our
   decoder says four, and the cache is silent because no definition uses it. This cannot be
   settled from the material available.

4. **What `Class301.aBoolean2503` actually represents** (the gate on opcodes 150-154). It is set
   from a byte in the login response alongside a block of other world flags
   (Class332_Sub1.java:342-354) and stored as `Class79.aBoolean602`. Its precise meaning - most
   likely a members-world flag, by analogy with other builds - was **not** confirmed, so this
   document calls it a session flag and no more.

5. **Semantic names for `anInt1096` (163), `anInt1123` (165), `anInt1125` (168), `anInt1101`/
   `anInt1090` (164) and `aBoolean1093` (162).** Their read expressions and use sites are cited,
   but no confident human-readable name was established for any of them. Our C# names
   (`anInt864`, `anInt847`, `anInt828`, `anInt848`, `anInt837`) are equally opaque and, worse, do
   not match the client's numbering, so the two sets cannot be cross-referenced by name.

6. **Whether the 637 client's field defaults match ours for every field.** Spot differences were
   noted where seen (opcodes 114, 125, 158/159), but no exhaustive default-by-default comparison
   was performed. Defaults are not opcode behaviour and were out of scope.

7. **The encode side.** `NPCDefinition.Encode` (NPCDefinition.cs:539-773) was read but not audited
   against this table. It unconditionally emits several opcodes the cache never contains
   (44, 45, 112, 162, 179) and writes opcode 155 as unsigned bytes. Whether that round-trips
   byte-for-byte against real cache input was not tested here.
