# Index 9 - TEXTURES: codec architecture

**Status:** the wire format is settled end to end, including the ten bytes the 637 client never
reads. The minimum capture set for a byte-identical encoder is proven by construction (section 2).
Two of the ten trailer positions have no established meaning and one bit of a third does not
either; those are named in section 9.

Every claim below is either a `file:line` in the bundled 637 client
(`C:\Users\CJ\Desktop\RSPS\Hydra\Client\src`) or a measurement over both 639 caches on disk.

Measurements were taken with a **Python transcription of the client's own readers**
(`Node_Sub46_Sub11.method1581` plus each `Node_Sub10_Sub*.method991`), reading the real dat2. It
does not call this project's C# at any point, so nothing below is our decoder agreeing with
itself. Scripts: `<session scratchpad>\tex9.py`, `tex9sweep.py`, on top of the existing
`cache.py`.

**Which cache each figure belongs to.** The default and source of truth is the vanilla b639
capture, `OpenRS2/cache-runescape-live-en-b639-2011-02-23-00-00-00-openrs2#1194/cache`. The repack
at `cache/` is a 639 base with local modifications and every figure taken from it is labelled as
such. Section 8 separates them explicitly. In short: **915 groups is the build-639 number, 946 is
repack residue**, and the survey note `index-survey/index-009-TEXTURES.md` states the repack's
figures throughout without saying so.

---

## 1. The falsification, run first

**915 of 915 and 946 of 946 payloads parse under the 637 read order and land exactly ten bytes
from the end of the file.** Zero short reads, zero overruns, zero exceptions, on a reader
transcribed from the client rather than from our C#.

So the 637 opcode widths are the 639 widths, and the ten trailing bytes are a fixed-width block
rather than a ragged tail.

Corroborating shape, same sweep, **vanilla** unless marked:

| | |
|---|---|
| Groups, one file each (file id 0) | 915 (repack 946) |
| Reference table | format 6, version 440, flags `0x00`, ids contiguous 0..914 |
| Repack reference table | format 6, version **443**, ids 0..945, plus **3784 trailing zero bytes** (4 per file) |
| Container compression | 439 GZip, 476 uncompressed (repack 439 GZip, 507 uncompressed) |
| Container version trailer | 2 bytes on all 915 (and all 946) |
| XTEA | none anywhere |
| Payload min / median / max | 24 / 93 / 994 bytes, 124,468 total (repack 24 / 99 / 994, 127,537) |
| Nodes | 9,329 in 915 graphs (repack 9,546 in 946) |
| Nodes per graph, min / max | 2 / 56 |
| Opcodes per node, max | 8 |
| Distinct `(node type, opcode)` pairs occurring | **98**, identical set in both caches |
| Total opcode payload bytes | 54,620 (repack 56,046) |
| Node types occurring | 38 of 40. **Types 18 and 31 occur zero times in either cache.** |

---

## 2. The minimum capture set, proven

An encoder that re-emits, per graph:

```
u8   nodeCount
per node:
  u8   nodeIndex          <- RECOMPUTED, see below. Not captured.
  u8   nodeType           <- captured
  u8   outputSizeByte     <- captured verbatim
  u8   opcodeCount        <- derived from the captured opcode list length
  per opcode, in the captured order:
    u8    opcode          <- captured
    bytes rawPayload      <- captured verbatim, span taken at decode
  u8 x childCount         <- captured verbatim (childCount is fixed per type)
u8 x3  the three output node indices   <- captured verbatim
u8 x10 the trailer                     <- captured verbatim
```

reproduces **915 of 915** vanilla payloads and **946 of 946** repack payloads byte for byte.
That is the whole contract. Nothing else needs recording, and nothing in the list can be dropped.

### 2.1 The "version byte" is the node's own index, and must not be captured

`Node_Sub46_Sub11.method1581:21` reads a byte and throws it away; `Texture.cs:325` copies that and
calls it "version byte (discarded)". Both descriptions are wrong about what it holds.

**Measured: `byte == nodeIndex` for 9,329 of 9,329 nodes in the vanilla capture and 9,546 of 9,546
in the repack.** It is the packer's loop counter `i`. Recompute it as the node's position in the
node array; do not add a field for it. The histogram is the giveaway - value 0 appears exactly
once per graph (915), value 1 exactly 915 times, value 2 914 times, decaying to a single node
carrying 55, which is the node-count distribution read sideways.

This is the one item on the survey's "must capture" list that is free.

### 2.2 The output-size byte is not derivable and must be captured

`anInt3860` (`Node_Sub10.java:196`, written at `Node_Sub46_Sub11.java:30`) has exactly one use in
the whole client, `Node_Sub10.method998:378`:

```java
int i_16_ = ((i_15_ == (this.anInt3860 ^ 0xffffffff)) ? i : this.anInt3860);
```

All three callers pass `i_15_ = -256` (`Node_Sub46_Sub19.java:154, 224`, and `:314` where
`i_39_ ^ ~0xb0` evaluates to -256 for the `i_39_ = 79` the method is called with), and
`~anInt3860 == -256`
exactly when `anInt3860 == 255`. So it is the height of the node's row cache, with **255 as a
sentinel meaning "cache the full image"**. It is a packer-computed memory hint.

Values occurring, vanilla: `1` x8317, `3` x531, `255` x370, `2` x53, `9` x28, `5` x22, `7` x8.
Repack: `1` x8472, `3` x593, rest identical.

Two things were tested as derivations and both fail: it is **not** the node's in-degree (3,756 of
9,329 disagree) and **not** in-degree plus output references (1,233 disagree). Capture it.

### 2.3 Opcode order and repetition do not vary, but presence does

- **Order.** The decoder is a loop (`Node_Sub46_Sub11.java:34-38`), so any order reads back the
  same state. Measured: **0 of 9,329 nodes carry their opcodes in anything but strictly ascending
  order**, in both caches.
- **Repetition.** Expressible, and **0 of 9,329 nodes repeat an opcode**, in both caches.
- **Presence.** Live and unavoidable. Capturing the ordered opcode list captures all three at once
  for the price of one, which is why section 2's contract does not treat them separately.

The survey lists order and repetition alongside presence as things that "would be lost". They
would, but nothing in either cache exercises them. Worth knowing before someone designs a data
structure to preserve an ordering that never varies.

---

## 3. The ten-byte trailer

`Node_Sub46_Sub19.java:111-114` reads the three output-node bytes and the constructor ends. The
client never touches what follows. These are 639-era bytes it never saw.

### 3.1 Eight of the ten positions are index-26 material fields, verbatim

Compared position by position against the columnar material record for the same texture id
(index 26, group 0, decoded with the 637 `Class260` column order), across **all 915 vanilla
textures**:

| Pos | Equals | Mismatches, vanilla | Distinct values | Histogram (vanilla) |
|---|---|---|---|---|
| 0 | `field1827` | **0 of 915** | 2 | `0` x869, `1` x46 |
| 1 | `field1824` | **0 of 915** | 2 | `0` x900, `1` x15 |
| 2 | `field1826` | **0 of 915** | 2 | `1` x905, `0` x10 |
| 3 | `field1819` | **0 of 915** | 2 | `1` x906, `0` x9 |
| 4 | `field1821` | **0 of 915** | 2 | `0` x913, `3` x2 |
| 5 | `field1823` | **0 of 915** | 7 | `0` x907, then `255`, `254`, `253`, `5`, `3`, `1` |
| 6 | `field1837` | **0 of 915** | 7 | `0` x899, then `254` x5, `250` x3, `2` x3, `1` x2, `255` x2, `3` |
| 7 | low 5 bits == `field1832`; bit `0x20` unexplained | **0 of 915** on the low bits | 4 | `34` x908, `0` x5, `2` x1, `32` x1 |
| 8 | **nothing** | n/a | 11 | `0` x713, `1` x127, `2` x46, `3` x13, `5` x6, `4` x4, `6` x2, and one each of `7`, `10`, `11`, `13` |
| 9 | `field1817` | **0 of 915** | 2 | `0` x859, `1` x56 |

`field1823` and `field1837` are decoded as **signed** bytes by `Class260`, which is why positions
5 and 6 read as small negatives (`255` = -1, `250` = -6). `field1821` is unsigned.

That is not a coincidence at 915-for-915 on eight independent columns. The trailer is a
**per-texture material record**, the same information index 26 carries, moved into the graph file
by build 639. It is not a copy of index 26's serialisation: the eight matched columns appear in
the trailer in a different order from the order `Class260` reads them
(`TextureManager.DecodeColumnar` passes 11, 12, 14, 15, 17, 9, 10, 13, ?, 16), and ten of index
26's twenty fields (`1825`, `1822`, `1833`, `1829`, `1830`, `1820`, `1816`, `1831`, `1835`,
`1818`) have no trailer position at all. It is its own structure.

### 3.2 Position 7 is two fields sharing a byte

`pos7 & 0x1F == field1832` for **915 of 915** vanilla and **946 of 946** repack. `field1832` is
`2` in 909 textures and `0` in 6.

Bit `0x20` is set in 909 of 915 and clear in exactly six: **groups 14, 34, 40, 45, 306 and 678**
(the same six in both caches). No index-26 column has a 909/6 split, so the bit is not a copy of
one. Its meaning is unknown; see section 9.

### 3.3 The trailer is not synthesisable, and the repack proves it

The obvious shortcut - drop the trailer at decode and rebuild it from index 26 at encode - is
wrong, and the repack is the counterexample. Rerunning the same comparison against the repack:

| Position | Mismatches, repack | Which groups |
|---|---|---|
| 0, 1, 4, 5 | 0 | - |
| 2 (`field1826`) | 24 | 915-938, all repacker-added |
| 3 (`field1819`) | 24 | 915-938, all repacker-added |
| 6 (`field1837`) | 1 | **655**, a vanilla-range texture |
| 9 (`field1817`) | 25 | **89**, plus 915-938 |
| 7 low bits | 0 | - |

Groups 0..914 have **byte-identical payloads in both caches** (915 of 915), so groups 89 and 655
did not change in index 9. The repack edited **index 26** and left index 9 alone. The two records
have already drifted apart once in the wild. Copy the ten bytes verbatim; never regenerate them,
and never assert that they agree with index 26.

---

## 4. Per-node-type opcode table

Widths read off each `Node_Sub10_Sub*.method991` in the 637 client, not off this project's C#.
`u8` = 1 byte, `u16` = 2, `s16` = signed 2, `med` = 3-byte big-endian (`RSBuffer.method1186`).
An opcode with **no arm reads nothing at all** - the base `Node_Sub10.method991` is empty
(`Node_Sub10.java:265`) - so the opcode byte is consumed and the parse continues.

The type-to-class map is `PlayerAppearance.method3630`, returns in order at
`PlayerAppearance.java:386-503`.

`kids` is the fixed child-index byte count and `mono` the default channel mode, both read off the
class's `super(inputCount, isMonochrome)` call. **All 40 rows of `Texture.ChildCounts` and
`Texture.MonoDefaults` were checked against those calls and all 40 agree.**

| Type | Class (`method991` line) | kids | mono | Opcodes and widths | Nodes (vanilla) |
|---|---|---|---|---|---|
| 0 | Sub13 (:185) | 0 | yes | `0` u8, decoded `(b<<12)/255` | 1932 |
| 1 | Sub22 (:578) | 0 | no | `0` med | 62 |
| 2 | Sub18 (none) | 0 | yes | **no reader: every opcode reads nothing** | 66 |
| 3 | Sub3 (none) | 0 | yes | **no reader** | 241 |
| 4 | Sub38 (:205) | 0 | yes | `0` u8, `1` u8, `2`-`7` u16 | 26 |
| 5 | Sub24 (:1009) | 1 | no | `0` u8, `1` u8, `2` u8 = **mono flag** | 110 |
| 6 | Sub15 (:149) | 1 | no | `0` u16, `1` u16, `2` u8 = **mono flag** | 207 |
| 7 | Sub7 (:234) | 2 | no | `0` u8 blend mode, `1` u8 = **mono flag** | 1377 |
| 8 | Sub9 (:252) | 1 | yes | `0` = u8 interp + u8 count + count x (u16, u16) | 1622 |
| 9 | Sub11 (:52) | 1 | no | `0` u8 bool, `1` u8 bool, `2` u8 = **mono flag** | 59 |
| 10 | Sub33 (:408) | 1 | no | `0` = u8 preset; **if preset == 0**, u8 count + count x (u16, u8, u8, u8) | 525 |
| 11 | Sub4 (:33) | 1 | no | `0`-`2` u16 | 6 |
| 12 | Sub30 (:150) | 0 | yes | `0` u8, `1` u8, `3` u8. **2, 4, 5 and 6 have no arm** | 2 |
| 13 | Sub8 (none) | 0 | yes | **no reader** | 152 |
| 14 | Sub17 (:82) | 0 | yes | `0` u16 | 2 |
| 15 | Sub26 (:243) | 0 | yes | `0` u8 **sets both cell counts**, `1` u8, `2` u16, `3` u8, `4` u8, `5` u8, `6` u8 | 346 |
| 16 | Sub32 (:175) | 0 | yes | `0` u8, `1` u8, `2` u16 | 1 |
| 17 | Sub6 (:195) | 1 | no | `0` **s16**, `1` s8, `2` s8 (client scales both by `<<12 / 100`) | 45 |
| 18 | Sub5_Sub1 (none) | 0 | no | inherits Sub5: `0` u16 | **0** |
| 19 | Sub2 (:146) | 3 | no | `0` u16, `1` u8 = **mono flag** | 232 |
| 20 | Sub29 (:150) | 1 | no | `0` u8, `1` u8 | 69 |
| 21 | Sub12 (:45) | 3 | no | `0` u8 = **mono flag**, and nothing else | 128 |
| 22 | Sub39 (:104) | 1 | no | `0` u8 = **mono flag**, and nothing else | 98 |
| 23 | Sub27 (:109) | 1 | no | `0` u8 = **mono flag**, and nothing else | 15 |
| 24 | Sub16 (none) | 1 | yes | **no reader** | 1 |
| 25 | Sub14 (:108) | 1 | no | `0`-`3` u16, `4` med | 1 |
| 26 | Sub31 (:56) | 1 | yes | `0` u16, `1` u16 | 15 |
| 27 | Sub23 (:143) | 0 | yes | `0` u8, `1` u16, `2` u8 | 47 |
| 28 | Sub28 (:337) | 0 | yes | `0` u8, `1`-`5` u16, `6` u8, `7` u16, `8` u16 | 12 |
| 29 | Sub36 (:105) | 0 | yes | `0` = shape list (see 4.1), `1` u8 = **mono flag** | 29 |
| 30 | Sub10 (:173) | 1 | no | `0` u16, `1` u16, `2` u8 = **mono flag** | 263 |
| 31 | Sub34 (:128) | 0 | yes | `0`-`3` u16 | **0** |
| 32 | Sub37 (:149) | 1 | yes | `0`-`2` u16 | 352 |
| 33 | Sub20 (:312) | 1 | no | `0` u16, `1` u8 bool | 1 |
| 34 | Sub35 (:242) | 0 | yes | `0` u8, `1` u8 octaves, `2` = s16 **plus, when negative, `octaves` x s16**, `3` u8 **sets both scales**, `4` u8, `5` u8, `6` u8 | 788 |
| 35 | Sub1 (:200) | 1 | yes | `0` u16 | 4 |
| 36 | Sub25 (:92) | 0 | no | `0` u16 nested texture id | 358 |
| 37 | Sub21 (:169) | 0 | yes | `0`-`6` u16 | 3 |
| 38 | Sub19 (:181) | 0 | yes | `0` u8, `1` u16, `2` u8, `3` u16, `4` u16 | 44 |
| 39 | Sub5 (:338) | 0 | no | `0` u16 sprite id | 88 |

The two variable-length readers are the only places a byte-for-byte encoder can go wrong on
length:

- **Type 34 opcode 2's array length lives in a sibling opcode.** It is `anInt5733`, which opcode 1
  sets and which **initialises to 4** (`Node_Sub10_Sub35.java:21`). A node that omits opcode 1 and
  carries a negative opcode 2 still reads four shorts. Measured: 240 nodes take the negative
  branch, 498 of the 788 type-34 nodes omit opcode 1 entirely.
- **Type 29 opcode 0** is a count byte then, per entry, a shape-id byte selecting a fixed-size
  record.

### 4.1 Type 29 shape records

`Node_Sub10_Sub36.method991:107-138`. Sizes read off the constructor argument lists, which Java
evaluates left to right:

| Shape id | Reader | Fields | Bytes | Occurrences (both caches) |
|---|---|---|---|---|
| 0 | `Class255.method3192` (`Class255.java:39`) | 4 x s16, med, u8 | **12** | 28 |
| 1 | `Node_Sub10_Sub14.method1046` (`:48`) | 8 x s16, med, u8 | **20** | 42 |
| 2 | `Class258.method3203` (`Class258.java:13`) | 4 x s16, 2 x med, u8 | **15** | 91 |
| 3 | `Class300.method3533` (`Class300.java:28`) | 4 x s16, 2 x med, u8 | **15** | 35 |
| other | none | reads nothing, leaves a null slot | 0 | 0 |

29 type-29 nodes hold 196 shape records totalling **3,066 payload bytes**, identical in both
caches. `Texture.cs:629-632` skips every one of them (`buf.Skip(12/20/15/15)`), so all 3,066 bytes
are discarded today. No unknown shape id occurs, but keep the zero-width fallback: an unknown id
consumes nothing and the parse continues.

---

## 5. What decode discards, per node type

`TextureNode` (`TextureGraphEvaluator.cs:86-150`) has no field for any of these.

**Discarded for every one of the 40 types**, by `Texture.Decode`:

| Item | Site | Verdict |
|---|---|---|
| The node-header first byte | `Texture.cs:325` | **Recompute** as the node index (section 2.1). Do not capture. |
| The output-size byte `anInt3860` | `Texture.cs:329` | **Capture verbatim** (section 2.2). Not derivable. |
| Which opcodes were present | the `for` loop, `Texture.cs:333` | **Capture** the ordered opcode list. |
| Opcode order and repetition | same loop | Comes free with the list. Never varies in either cache. |
| The ten-byte trailer | never read at all | **Capture verbatim** (section 3). |

**Discarded for particular types:**

| Type | What is lost | Scale |
|---|---|---|
| 29 | the whole shape-record payload, skipped blind | 3,066 bytes over 196 records in 29 nodes |
| 5, 6, 7, 9, 19, 21, 22, 23, 29, 30 | the mono-flag opcode's **raw byte**: `Texture.cs:344` stores `byte == 1` as a `bool?`, so bytes 2..255 all collapse to `false` | 1,390 mono-flag opcodes in vanilla. Measured values are only `0` (x273) and `1` (x1117), so the collapse is **not exercised**, but the flattening is real |
| 17 | opcode 0 is read `ReadUnsignedShort` (`Texture.cs:551`) where the client reads `readShort` (`Node_Sub10_Sub6.java:209`) | Byte count identical, so byte identity is safe; the **value** is wrong above 0x7FFF. 4 nodes carry opcode 0 |
| 34 | `IntParam1` is overwritten by the octave trim, section 6 | 788 nodes at risk, 0 actually affected |
| 8 | `GradientData` is replaced by an identity ramp when absent, section 6 | 1622 nodes at risk, 0 actually affected |
| 21 | `case 21` in `DecodeNodeOpcode` (`Texture.cs:572`) is **dead code**: opcode 0 is the mono flag and is consumed at `:344` before the switch. Same for types 22 and 23 | harmless (same 1-byte width), but the field it claims to set is never populated |

**Absent versus default.** `InitNodeDefaults` (`Texture.cs:87-130`) seeds real values for types 0,
6, 7, 15, 25, 30 and 34, so a node whose decoded value equals its default may or may not have
carried the opcode. Measured over both caches, for every defaulted opcode: **there is no case
where a node stores a value equal to the default and another node of the same type omits the same
opcode.** The nearest miss is type 6 opcode 1, where 160 of 207 nodes store the default 4096 but
**none** omit the opcode. Type 0 opcode 0 is the one people expect to bite - 1,028 nodes omit it
and 904 carry it - and **no node stores byte 255**, the only byte that decodes to the 4096
default.

So the hazard is not live in either cache. It becomes live the moment the editor creates a node at
its default value, which is why the presence list still has to be recorded rather than inferred
from the value.

---

## 6. The two destructive post-decode hooks

Both exist, both overwrite an as-read value with a computed one, and **neither fires on any file
in either cache.**

### 6.1 Fractal-noise octave trim - `Texture.cs:302-305`

```csharp
while (octaves > 1 && Math.Abs(node.Amplitudes[octaves - 1]) <= 8)
    octaves--;
node.IntParam1 = octaves;
```

`IntParam1` is opcode 1's value. After this line the stored octave count is gone.

This is a faithful port, not our invention: the client does the same thing to its own field in
`Node_Sub10_Sub35.method1001:37-46`, decrementing `anInt5733` past every trailing amplitude in
`(-8, 8)`. The client can afford it because it never writes the file back. We cannot.

Emulated against the real data: **788 of 788 type-34 nodes reach the trim loop in both caches, and
0 have their octave count changed.** The smallest `|last amplitude|` anywhere is **32**, four times
the threshold. Octave counts occurring are 1, 2, 3, 5, 6, 7 plus 498 nodes taking the default 4;
no node stores a count below 1, so the `if (octaves < 1) octaves = 1` clamp above it is also
never taken.

The 240 nodes with negative persistence take the explicit-amplitude branch, where a length
mismatch causes an early `return` **before** the trim, so `IntParam1` survives there by accident
rather than by design.

**Verdict: real defect, zero blast radius today.** Keep the as-read value beside the derived one -
it costs one field and removes the whole class of failure.

### 6.2 Curve identity-ramp substitution - `Texture.cs:164-166`

```csharp
if (markers == null || markers.Length == 0)
    markers = new[] { new[] { 0, 0 }, new[] { 4096, 4096 } };
```

The client's original **is** destructive - `Node_Sub10_Sub9.method1001` assigns the substitute
straight into `anIntArrayArray5587`, the field opcode 0 filled. Our port is not: it substitutes
into a local and writes only `node.CurveLut`, leaving `node.GradientData` null. So unlike 6.1 this
one is already safe, and the survey's description of it as an overwrite is one level stronger than
the code. Keep it that way; do not "fix" it into matching the client.

Either way it never runs: **all 1,622 vanilla and all 1,715 repack type-8 nodes carry opcode 0,
and every one declares at least 2 markers.** Marker counts run 2 to 13, most commonly 4 (535),
3 (489) and 2 (405). Zero nodes lack opcode 0; zero declare a count of 0.

---

## 7. The aliased and swallowed opcode sets, walked exhaustively

Every node of every texture in both caches was walked and every `(type, opcode)` pair recorded.
98 distinct pairs occur, the **same 98 in both caches**.

### 7.1 Swallowed opcodes: four, not two

An opcode is swallowed when it occurs in the data and the client class has no arm for it, so it
consumes nothing and leaves no trace in decoded state.

**The complete set, in both caches, is `(12, 2)`, `(12, 4)`, `(12, 5)` and `(12, 6)`.**
`Node_Sub10_Sub30.method991:150` handles only 0, 1 and 3.

There are exactly **two type-12 nodes in the whole cache** - group **275** node 16 and group
**742** node 3 - and each carries all seven opcodes 0..6, so each contributes four swallowed
opcode bytes:

```
group 275 node 16: ops 0='00' 1='02' 2='' 3='04' 4='' 5='' 6=''
group 742 node  3: ops 0='00' 1='02' 2='' 3='02' 4='' 5='' 6=''
```

`Texture.cs:522-526`'s comment says "two graphs in this cache carry opcodes 2 and 4". Two graphs
is right; **two opcodes is wrong - it is four**, and an encoder rebuilt from decoded state alone
would shorten each of those two files by 4 bytes, not 2.

The other candidates were checked and are clean. Types 2, 3, 13 and 24 have **no `method991` at
all**, so `Texture.cs:434`'s `case 2/3/13/24: return true;` would swallow anything - but no opcode
occurs on any of the 460 nodes of those four types in either cache. Types 18 and 31 have zero
nodes.

### 7.2 The aliased pairs exist and are not exercised

| Alias | Client site | Nodes using the combined form | Nodes using the split form |
|---|---|---|---|
| Type 15 opcode `0` sets both cell counts; `5` and `6` set them separately | `Node_Sub10_Sub26.java:279` | **0 of 346** | 269 use `{5,6}`, 26 use `{6}`, 19 use `{5}`, 32 use neither |
| Type 34 opcode `3` sets both scales; `5` and `6` set them separately | `Node_Sub10_Sub35.java:294` | **0 of 788** | 575 use `{5,6}`, 35 use `{6}`, 28 use `{5}`, 150 use neither |

Identical counts in both caches. **Neither combined opcode occurs anywhere.** The aliasing is a
property of the format, not of the shipped data, so an encoder that always emits the split form is
byte-exact on both caches - but only because the capture-the-opcode-list design in section 2
already makes the question moot.

These two are the **only** multi-field assignments from a single read in the entire client texture
decoder: a grep for `x = y = RSBuffer.read` across all 40 `Node_Sub10_Sub*.java` returns exactly
those two lines. The set is complete.

### 7.3 Opcodes with a reader that never occur

Live arms with no data behind them, so no sweep defends them: type 4 opcode 5, type 5 opcode 2,
type 15 opcode 0, type 23 opcode 0, type 33 opcodes 0 and 1, type 34 opcode 3. Keep them; a
hand-edited graph can reach them and an encoder must be able to write them back.

---

## 8. Which figures belong to which cache

| | Vanilla b639 | Repack | Verdict |
|---|---|---|---|
| Index 9 groups | **915** | 946 | 915 is the build-639 figure |
| Index 9 reference-table version | 440 | 443 | repack bumped it |
| Trailing bytes past the table | **0** | 3784 (4 per file) | repack residue |
| Index 26 declared textures | **915** | 1408 | repack inflated it |
| Textures declared in 26 with no graph in 9 | **0** | 462 | **repack residue** |
| Compression split | 439 GZip / 476 raw | 439 GZip / 507 raw | the 31 added groups are all uncompressed |
| Nodes | 9,329 | 9,546 | |
| Trailer positions matching index 26 | **8 of 10, 915/915 each** | 4 of 10 exact | the repack broke the correspondence |

**Groups 0..914 are byte-identical in both caches, all 915.** The repack's entire index-9 delta is
31 appended groups, ids 915..945, all 99 bytes, all uncompressed, all carrying the same trailer
`00 00 00 00 00 00 00 22 00 00`. They are 24 distinct payloads of one 7-node template that differ
only in the type-39 node's sprite id (`0x05C6`..`0x05DF`, ids 1478..1503, with 1478 used eight
times). A private-server operator wiring 31 custom sprites through one copied graph.

Two consequences:

1. **The index-26 correspondence in section 3.1 is a build-639 property.** It holds 915 for 915 on
   eight columns in the vanilla capture, and the repack breaks it in 26 places - 24 of them its own
   new groups, plus vanilla-range groups **89** and **655** where it edited index 26 and not index 9.
2. **`index-survey/index-009-TEXTURES.md` states repack figures as general facts throughout**: 946
   groups, table version 443, 3784 trailing bytes, 507 uncompressed, 1408 declared in index 26, and
   the whole per-position trailer histogram in its trap 1. All of those are the repack's. The trailer
   histogram in section 3.1 above is the vanilla one and differs at positions 0, 2, 3, 4, 5, 6, 7 and 9.

---

## 9. Could not settle

Listed so nobody reports them as settled later on weaker evidence than was used to fail here.

- **Trailer position 8.** 11 distinct values, `0` in 713 of 915. It equals **no** index-26 column
  (best case 202 mismatches, and that is just "both are mostly zero"). It equals no graph-derived
  quantity tried: node count, per-type node counts for all 40 types, sprite-node count, distinct
  sprite ids, nested-texture count, output-size max/sum/distinct, opcode total, leaf count, the
  three output indices, payload length mod 256. Its value distribution reads like a small count or
  a lookup index. **Unknown.**
- **Trailer position 7, bit `0x20`.** Set in 909 textures, clear in exactly six (14, 34, 40, 45,
  306, 678). No index-26 column has that split. The low five bits are `field1832` beyond doubt;
  this bit is not. **Unknown.**
- **What `field1832` and the other seven matched columns mean.** Section 3.1 proves the trailer
  bytes *equal* named index-26 fields; it does not decode either. `Class260`'s field names are
  obfuscated and this document deliberately does not guess at semantics for them, per the
  reference-naming rule in `CLAUDE.md`.
- **Whether the output-size byte is computable.** Two derivations were tested and rejected
  (section 2.2). A third - the maximum vertical reach of a node's consumers, which is what a row
  cache would need - is plausible and was not tested, because every consumer's reach would have to
  be modelled first. Capturing the byte costs nothing and closes the question.
- **Whether the trailer is fixed at ten bytes by the format or by this build.** Every file in both
  caches has exactly ten. No client code reads it, so there is no length field to point at. Treat
  ten as measured, not as declared, and have the decoder record the actual remaining length rather
  than assume it.
- **Node type 23's meaning** (`Node_Sub10_Sub27`). Its only opcode is the mono flag and its
  evaluator indexes through `method1087` and `Node_Sub10_Sub23.anInt5661`, a coordinate remap that
  was not chased. `Texture.cs` and `TextureGraphEvaluator` both label it "FlipV"; that label is
  unverified either way.

---

## 10. Method identity: settled from the dispatch and the client

Per `CLAUDE.md`'s rule that an orphaned method's own header is unreliable here.

**The two headers `CLAUDE.md` names are now correct.** `TextureGraphEvaluator.cs:2213` reads
"TYPE 15: Worley (cellular) noise" and `:2294` "TYPE 34: Fractal Perlin noise", matching the
dispatch at `:824` and `:843`.

**There are no unreferenced `Eval*` methods left.** Every one of them has exactly two occurrences
in the file, its declaration and a single dispatch site. The dead type-36 checkerboard is gone; a
comment at `:2205-2210` marks where it was.

Four findings the dispatch and the client settle, none of which affects the codec:

1. **Type 17 is an HSL adjust, not a blur, and the file says both.** `:1509` heads
   `EvalHSLAdjust17` "TYPE 17: HSL Adjust (Hydra Sub6 - NOT blur)" and `:1660` heads `EvalBlur`
   "TYPE 17: Blur". The client settles it: `Node_Sub10_Sub6.method997:227-244` converts RGB to
   HSL, adds `anInt5563` to hue and the two `/100`-scaled bytes to saturation and lightness, wraps
   hue at 4096 and clamps the other two. `EvalHSLAdjust17` is right. **`EvalBlur` is dead code that
   reads as live**: it is dispatched at `EvalMono:826`, and `EvalMono` is only reached when
   `IsMonochrome(node)` (`:714, 724`), which for type 17 is `MonoOverride ?? false`. Type 17 has no
   mono-override opcode, so the flag can never become true. The client has the same rule -
   `Node_Sub10.method1000` (`:215-224`) and `method994` (`:316-324`) dispatch on the child's own
   `aBoolean3861` - and `Sub6` declares no `method990` at all, so asking a type-17 node for a mono
   row throws in the client (`Node_Sub10.java:249`). `Texture.cs:550` also labels type 17 "Blur".
2. **The same reachability argument makes three more mono arms dead**: types 10, 20 and 33
   (`EvalGradientRemap`, `EvalTileMono`, `EvalOffset`). All three are `super(1, false)` with no
   mono-override opcode. Type 33's colour arm is `goto default`, and the default branch copies
   child 0, so **type-33 nodes currently render as a passthrough** rather than as an offset.
3. **Type 21 is a three-way blend, not an emboss - in both files.** `Node_Sub10_Sub12` is
   `super(3, false)` (`:11`) and both its outputs compute
   `out = (b*(4096-t) + t*a) >> 12` over children 0, 1 and 2 (`:15-38`, `:64ff`). Its only opcode
   is the mono flag (`:45-57`), so it has **no numeric parameter at all**. `EvalEmboss`
   (`TextureGraphEvaluator.cs:1840`) invents a strength from `node.IntParam0`, which decode never
   populates because the mono-override path consumes opcode 0 first, so the strength is always the
   `Math.Max(1, 0)` fallback. And `EvalEmboss` is itself unreachable by argument 2, so **128
   type-21 nodes render today as a passthrough of child 0**. The codec side is correct: child count
   3 and a 1-byte opcode 0 both match the client.
4. **`Texture.cs`'s decoder comments mislabel four node types where the evaluator has them right.**
   The evaluator is the one to believe here, and the client agrees with it:

   | Type | `Texture.cs` says | Client does | Evidence |
   |---|---|---|---|
   | 9 | "Invert (Sub11)" | mirror / flip: opcode 1 flips the row, opcode 0 reverses within it | `Node_Sub10_Sub11.java:29-50` |
   | 22 | "FlipH (Sub39)" | invert: `out = 4096 - in` | `Node_Sub10_Sub39.java:85-100` |
   | 30 | "EdgeDetect (Sub10)" | range remap: `out = lo + (in * span >> 12)` | `Node_Sub10_Sub10.java:154-170` |
   | 35 | "Scale (Sub1)" | normal / bump map: neighbour gradients into `4096/sqrt(...)` | `Node_Sub10_Sub1.java:165-195` |

   Types 9 and 22 are labelled as each other's job. None of this changes a single byte the decoder
   reads; it changes which method someone opens to fix a render defect.

Also worth recording: `EvalFactory` (`:2079`, headed "TYPE 29: Factory (BAIL - too complex to
port)") is not a factory. Type 29 is `Node_Sub10_Sub36`, a shape-list rasteriser over the four
record types in section 4.1. "Factory" is an invented name with no client basis.

---

## 11. Build order for the implementer

1. **Record the capture set from section 2** on `TextureNode` and `TextureGraph`: node type, the
   output-size byte, an ordered `(opcode, byte[] rawSpan)` list, the child bytes, the three output
   indices, and the ten-byte trailer. Take the spans by bracketing the stream position around each
   opcode's read in the existing `DecodeNodeOpcode` call, not by re-parsing.
2. **Write `Texture.Encode` as a replay** of that list, recomputing only the node index byte, the
   node count and the per-node opcode count. Nothing else is synthesised.
3. **Add the byte-identity sweep** over every group of whichever cache is pointed at, comparing
   **decompressed payloads** - 439 of 915 containers are GZip and per `AGENTS.md` no GZip container
   re-encodes byte-identically. The sweep should assert 915 of 915 on the vanilla capture and 946 of
   946 on the repack, and must read its expected count from the reference table rather than
   hardcoding either.
4. **Then** keep the as-read octave count beside `IntParam1` (section 6.1). It changes nothing in
   either cache and removes the whole failure mode before the editor can create a node that triggers
   it.
5. **Then** the node inspector. The two fields worth exposing first are the sprite id on type
   18/39 nodes and the nested-texture id on type 36, because both are cross-index references that
   `TextureGraphConformanceTests` already pins.

An edit to any field inside an opcode payload rewrites only that opcode's span, so the surrounding
bytes - including every byte in section 9's unknown column - stay exactly as they were read.
