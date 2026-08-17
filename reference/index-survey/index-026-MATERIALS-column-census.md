# Index 26 column census

A measurement of what the nineteen index-26 columns hold in each of the two revision-639 caches on
disk. Nothing here names or interprets a field. Every figure is read from the **stored bytes** of
each 23-byte record, unsigned and big-endian, so a decoder choice (signedness, inversion, the
many-to-one boolean collapse) cannot colour the number.

Measured 2026-08-16 by `MaterialColumns_AreCensusedAgainstTheLoadedCache` in
`FlashEditor.Tests/Definitions/Sprites/MaterialColumnCensusTests.cs`, run filtered, once per cache.
No sweep, no `FLASHEDITOR_TEST_CACHE_FULL`. Both caches were opened read-only and neither was
written to. **That test was written on its own branch and may not be in the tree you are reading
this from**; the figures below stand on their own, and re-deriving them needs the test back or an
equivalent scratchpad read.

The semantic companion to this document is
`reference/hydra-637-definitions/material-columns.md`, which says what the 637 client does with each
column. The two were produced blind to each other. Where a name in that document and a distribution
here disagree, this one is the measurement and that one is the claim.

| | vanilla b639 capture (OpenRS2 1194) | the repack |
|---|---|---|
| path | `OpenRS2\cache-runescape-live-en-b639-2011-02-23-00-00-00-openrs2#1194\cache` | `C:\Users\CJ\Desktop\FlashEditor\cache` |
| declared slots | 915 | 1408 |
| present records | 915 | 1408 |
| file length | 21,962 bytes | 33,794 bytes |
| index-9 groups declared | 915 | 946 |
| present slots that are an index-9 group | 915 | 946 |
| present slots with no index-9 group | **0** | **462** |

Every declared slot is present in both caches, so "slot" and "record" are the same population here
and every column has one value per slot.

**The vanilla capture cannot answer the graph-correlation question at all.** Index 26 and index 9
are 1:1 at 915 there, so there is no graphless partition to compare against. That cross-cut is a
repack-only measurement, on 946 graph-bearing against 462 graphless slots.

---

## 1. Summary, both caches

Raw stored value. `only {0,1}` is the boolean corroboration test; `constant` means the column
carries no information in that cache at all.

### vanilla b639 capture, 915 slots

| column | offset | width | distinct | min | max | zero slots | constant | only {0,1} |
|---|---|---|---|---|---|---|---|---|
| field1825 | 0 | 1 | 2 | 0 | 1 | 797 | no | **yes** |
| field1822 | 1 | 1 | 2 | 0 | 1 | 385 | no | **yes** |
| field1833 | 2 | 1 | 2 | 0 | 1 | 914 | no | **yes** |
| field1829 | 3 | 1 | 6 | 0 | 255 | 898 | no | no |
| field1830 | 4 | 1 | 11 | 0 | 255 | 693 | no | no |
| field1820 | 5 | 1 | 8 | 0 | 8 | 711 | no | no |
| field1816 | 6 | 1 | 12 | 0 | 194 | 743 | no | no |
| field1831 | 7 | 2 | 393 | 3 | 64919 | 0 | no | no |
| field1823 | 9 | 1 | 7 | 0 | 255 | 907 | no | no |
| field1837 | 10 | 1 | 7 | 0 | 255 | 899 | no | no |
| field1827 | 11 | 1 | 2 | 0 | 1 | 869 | no | **yes** |
| field1824 | 12 | 1 | 2 | 0 | 1 | 900 | no | **yes** |
| field1832 | 13 | 1 | 2 | 0 | 2 | 6 | no | no |
| field1826 | 14 | 1 | 2 | 0 | 1 | 10 | no | **yes** |
| field1819 | 15 | 1 | 2 | 0 | 1 | 9 | no | **yes** |
| field1817 | 16 | 1 | 2 | 0 | 1 | 859 | no | **yes** |
| field1821 | 17 | 1 | 2 | 0 | 3 | 913 | no | no |
| field1835 | 18 | 4 | 1 | 0 | 0 | 915 | **yes** | yes (all zero) |
| field1818 | 22 | 1 | 3 | 0 | 2 | 787 | no | no |

### the repack, 1408 slots

| column | offset | width | distinct | min | max | zero slots | constant | only {0,1} |
|---|---|---|---|---|---|---|---|---|
| field1825 | 0 | 1 | 2 | 0 | 1 | 987 | no | **yes** |
| field1822 | 1 | 1 | 2 | 0 | 1 | 808 | no | **yes** |
| field1833 | 2 | 1 | 2 | 0 | 1 | 1407 | no | **yes** |
| field1829 | 3 | 1 | 12 | 0 | 255 | 1333 | no | no |
| field1830 | 4 | 1 | 16 | 0 | 255 | 1063 | no | no |
| field1820 | 5 | 1 | 8 | 0 | 8 | 1087 | no | no |
| field1816 | 6 | 1 | 12 | 0 | 194 | 1123 | no | no |
| field1831 | 7 | 2 | 513 | 3 | 65087 | 0 | no | no |
| field1823 | 9 | 1 | 16 | 0 | 255 | 1363 | no | no |
| field1837 | 10 | 1 | 16 | 0 | 255 | 1369 | no | no |
| field1827 | 11 | 1 | 2 | 0 | 1 | 1361 | no | **yes** |
| field1824 | 12 | 1 | 2 | 0 | 1 | 1390 | no | **yes** |
| field1832 | 13 | 1 | 2 | 0 | 2 | 6 | no | no |
| field1826 | 14 | 1 | 2 | 0 | 1 | 226 | no | **yes** |
| field1819 | 15 | 1 | 2 | 0 | 1 | 250 | no | **yes** |
| field1817 | 16 | 1 | 2 | 0 | 1 | 1283 | no | **yes** |
| field1821 | 17 | 1 | 2 | 0 | 3 | 1406 | no | no |
| field1835 | 18 | 4 | 1 | 0 | 0 | 1408 | **yes** | yes (all zero) |
| field1818 | 22 | 1 | 3 | 0 | 2 | 951 | no | no |

**The two caches agree on the shape of every column.** Same offsets, same widths, same
constant/boolean verdicts, same maxima on field1816 (194), field1820 (8), field1832 (2),
field1821 (3), field1818 (2). Only the multiplicities and the distinct counts move.

### The eight boolean-shaped columns

Strictly `{0,1}` in **both** caches: **field1825, field1822, field1833, field1827, field1824,
field1826, field1819, field1817**. A boolean reading of these eight is corroborated by the data
and refuted by nothing in it.

### The columns a boolean reading is refuted for

**field1832** takes only two values in both caches - but they are **0 and 2**, never 1. A decoder
that tests `== 1` reads *every* slot as false in both caches. **field1821** takes only 0 and 3.
**field1818** takes 0, 1 and 2. **field1820** takes 0..8. These four are two-valued or
few-valued, which looks boolean at a glance, and none of them is.

### The one column that carries no information

**field1835** is zero in every slot of both caches: 0 of 915 and 0 of 1408 non-zero. See section 5.
No other column is constant in either cache. The closest are **field1833** (1 slot set out of 915,
1 out of 1408) and **field1821** (2 slots set out of 915, 2 out of 1408).

---

## 2. Distributions, capped at the ten most common

Value counts are of the raw stored byte(s). `+N further values` is the count of distinct values not
listed, followed by the slots they account for between them.

### field1825 - offset 0, width 1

| value | vanilla | repack |
|---|---|---|
| 0 | 797 | 987 |
| 1 | 118 | 421 |

### field1822 - offset 1, width 1

| value | vanilla | repack |
|---|---|---|
| 0 | 385 | 808 |
| 1 | 530 | 600 |

### field1833 - offset 2, width 1

| value | vanilla | repack |
|---|---|---|
| 0 | 914 | 1407 |
| 1 | 1 | 1 |

### field1829 - offset 3, width 1

vanilla (6 distinct): `0:898, 255:8, 50:5, 128:2, 102:1, 160:1`

repack (12 distinct): `0:1333, 255:38, 100:20, 50:6, 128:4, 72:1, 80:1, 102:1, 135:1, 150:1`, +2
further values over 2 slots

### field1830 - offset 4, width 1

vanilla (11 distinct): `0:693, 255:188, 25:9, 220:7, 200:6, 51:5, 50:2, 128:2, 100:1, 180:1`, +1
further value over 1 slot

repack (16 distinct): `0:1063, 255:301, 25:9, 200:8, 220:7, 51:5, 128:5, 50:2, 69:1, 72:1`, +6
further values over 6 slots

### field1820 - offset 5, width 1

Fully enumerated in both; 8 distinct, values 0 through 8 with 3 absent.

| value | vanilla | repack |
|---|---|---|
| 0 | 711 | 1087 |
| 1 | 93 | 165 |
| 2 | 21 | 22 |
| 4 | 9 | 9 |
| 5 | 4 | 4 |
| 6 | 46 | 86 |
| 7 | 30 | 34 |
| 8 | 1 | 1 |

**Value 3 occurs in neither cache.** The value set is identical between the caches.

### field1816 - offset 6, width 1

vanilla (12 distinct): `0:743, 1:89, 2:57, 3:15, 130:3, 129:2, 9:1, 24:1, 57:1, 137:1`, +2 further
values over 2 slots

repack (12 distinct): `0:1123, 1:174, 2:72, 3:27, 130:4, 129:2, 9:1, 24:1, 57:1, 137:1`, +2 further
values over 2 slots

Both caches reach max 194, and both have exactly 12 distinct values.

### field1831 - offset 7, width 2

The only wide column with any variation, and by far the highest-entropy column in the table.

vanilla: 393 distinct, min 3, max 64919, **zero in 0 of 915 slots**, at or above 32768 in 109 slots.
Top ten: `127:23, 20:18, 88:18, 89:17, 91:15, 83:14, 79:13, 85:13, 97:13, 80:12`; the remaining 383
values cover 759 slots.

repack: 513 distinct, min 3, max 65087, **zero in 0 of 1408 slots**, at or above 32768 in 133 slots.
Top ten: `127:72, 89:23, 20:21, 88:21, 91:20, 68:18, 76:18, 80:18, 67:17, 79:17`; the remaining 503
values cover 1163 slots.

Never zero in either cache, which is the only column that can say that.

### field1823 - offset 9, width 1

vanilla (7 distinct, fully enumerated): `0:907, 254:2, 255:2, 1:1, 3:1, 5:1, 253:1`

repack (16 distinct): `0:1363, 255:12, 253:6, 254:6, 226:5, 2:3, 250:3, 6:2, 1:1, 3:1`, +6 further
values over 6 slots

### field1837 - offset 10, width 1

vanilla (7 distinct, fully enumerated): `0:899, 254:5, 2:3, 250:3, 1:2, 255:2, 3:1`

repack (16 distinct): `0:1369, 3:7, 255:7, 254:6, 250:4, 1:3, 2:2, 216:2, 5:1, 10:1`, +6 further
values over 6 slots

### field1827 - offset 11, width 1

| value | vanilla | repack |
|---|---|---|
| 0 | 869 | 1361 |
| 1 | 46 | 47 |

### field1824 - offset 12, width 1

| value | vanilla | repack |
|---|---|---|
| 0 | 900 | 1390 |
| 1 | 15 | 18 |

### field1832 - offset 13, width 1

| value | vanilla | repack |
|---|---|---|
| 0 | 6 | 6 |
| 2 | 909 | 1402 |

**The value 1 never occurs in either cache.** Exactly six slots hold 0 in both.

### field1826 - offset 14, width 1

| value | vanilla | repack |
|---|---|---|
| 0 | 10 | 226 |
| 1 | 905 | 1182 |

### field1819 - offset 15, width 1

| value | vanilla | repack |
|---|---|---|
| 0 | 9 | 250 |
| 1 | 906 | 1158 |

### field1817 - offset 16, width 1

| value | vanilla | repack |
|---|---|---|
| 0 | 859 | 1283 |
| 1 | 56 | 125 |

### field1821 - offset 17, width 1

| value | vanilla | repack |
|---|---|---|
| 0 | 913 | 1406 |
| 3 | 2 | 2 |

**Values 1 and 2 never occur.** Exactly two slots hold 3 in both caches.

### field1835 - offset 18, width 4

| value | vanilla | repack |
|---|---|---|
| 0 | 915 | 1408 |

### field1818 - offset 22, width 1

| value | vanilla | repack |
|---|---|---|
| 0 | 787 | 951 |
| 1 | 45 | 54 |
| 2 | 83 | 403 |

---

## 3. field1825 - raw stored bytes against the decoded bool

Reported as stored bytes on purpose: the decoded value would hide a mistake in the inversion, since
"stored 0, decoded true" reads identically whichever way round the encoder has the sense.

### vanilla b639 capture

| stored byte | slots | decoded true | decoded false |
|---|---|---|---|
| 0 | 797 | 797 | 0 |
| 1 | 118 | 0 | 118 |

### the repack

| stored byte | slots | decoded true | decoded false |
|---|---|---|---|
| 0 | 987 | 987 | 0 |
| 1 | 421 | 0 | 421 |

The stored column is strictly `{0,1}` in both caches - no byte outside that set exists, so the
many-to-one collapse the codec is careful about is **not exercised by either cache**. The decode is
exactly `stored == 0`, consistently, in every one of the 2323 records across both caches. The
inversion is therefore internally consistent; whether the sense is the *right* way round is a claim
about the client, not something this data can settle, because the two caches only ever store 0 or 1
and both readings partition the slots identically (just with the labels swapped).

---

## 4. The signed-byte columns - is a signed reading exercised at all?

The codec reads seven columns back as `sbyte`. A stored byte at or below 127 decodes the same under
either reading, so a signed reading is only *falsifiable* where the high bit is set.

### vanilla b639 capture

| column | slots with the high bit set | signed min | signed max | signed reading exercised |
|---|---|---|---|---|
| field1829 | 11 of 915 | -128 | 102 | **yes** |
| field1830 | 205 of 915 | -128 | 100 | **yes** |
| field1820 | **0 of 915** | 0 | 8 | **no** |
| field1816 | 8 of 915 | -127 | 57 | **yes** |
| field1823 | 5 of 915 | -3 | 5 | **yes** |
| field1837 | 10 of 915 | -6 | 3 | **yes** |
| field1832 | **0 of 915** | 0 | 2 | **no** |

### the repack

| column | slots with the high bit set | signed min | signed max | signed reading exercised |
|---|---|---|---|---|
| field1829 | 46 of 1408 | -128 | 102 | **yes** |
| field1830 | 325 of 1408 | -128 | 125 | **yes** |
| field1820 | **0 of 1408** | 0 | 8 | **no** |
| field1816 | 9 of 1408 | -127 | 57 | **yes** |
| field1823 | 35 of 1408 | -40 | 40 | **yes** |
| field1837 | 23 of 1408 | -60 | 50 | **yes** |
| field1832 | **0 of 1408** | 0 | 2 | **no** |

**Where the signed reading is unfalsified by this cache:** `field1820` and `field1832` hold no byte
with the high bit set in either cache. Both could equally be unsigned and nothing in either cache
would read differently. `field1820`'s maximum is 8 and `field1832`'s is 2, so neither comes close.

**Where it is exercised:** `field1829`, `field1830`, `field1816`, `field1823` and `field1837` all
carry high-bit bytes in both caches. `field1830` most heavily - 205 of 915 and 325 of 1408 slots -
and the values cluster hard on 255 (188 and 301 slots respectively), which is -1 signed and 255
unsigned. `field1816`'s high-bit values are 129, 130, 137 and 194, which read as -127, -126, -119
and -62 signed; those are not clustered near 255 the way `field1823`/`field1837`/`field1830`'s are.

The distinction is worth stating carefully: "the high bit is set somewhere" does **not** prove the
signed reading is correct. It only means the two readings disagree on those slots, so the data
*could* falsify one of them if something else told us what the value ought to be. Nothing in index
26 alone does.

---

## 5. field1835 - the four-byte column

**Confirmed zero in every record of both caches.**

| | slots | non-zero slots | min | max | distinct |
|---|---|---|---|---|---|
| vanilla b639 capture | 915 | **0** | 0 | 0 | 1 |
| the repack | 1408 | **0** | 0 | 0 | 1 |

All four bytes are zero in all 2323 records across both caches. This is the only column that is
constant, and it is constant at zero. It carries no information in either cache, so **nothing about
its meaning can be tested against this data at all** - any claim about it rests entirely on the
client. It also means the column's *width* is unfalsifiable from content: four zero bytes are
indistinguishable from any other partition of four zero bytes. Only the exact-consumption sweep
(file length `2 + count + present * 23`) holds it at four.

---

## 6. Correlation with index-9 procedural graph presence

**Repack only.** The vanilla capture is 1:1 at 915/915 with no graphless slot, so it contributes
nothing to this cross-cut. In the repack, 946 present slots have an index-9 group declared and 462
do not.

Read as: values on the 946 graph-bearing slots, then values on the 462 graphless slots.

| column | graph-bearing (946) | graphless (462) | relationship |
|---|---|---|---|
| field1825 | `0:797, 1:149` | `1:272, 0:190` | same value set |
| field1822 | `1:550, 0:396` | `0:412, 1:50` | same value set |
| field1833 | `0:945, 1:1` | `0:462` | subset (graphless all 0) |
| field1829 | `0:929, 255:8, 50:5, 128:2, 102:1, 160:1` | `0:404, 255:30, 100:20, 128:2, 50:1, 72:1, 80:1, 135:1, 150:1, 200:1` | overlapping |
| field1830 | `0:695, 255:217, 25:9, 220:7, 200:6, 51:5, 50:2, 128:2, 100:1, 180:1` (+1 more) | `0:368, 255:84, 128:3, 200:2, 69:1, 72:1, 125:1, 150:1, 188:1` | overlapping |
| field1820 | `0:744, 1:91, 6:46, 7:30, 2:21, 4:9, 5:4, 8:1` | `0:343, 1:74, 6:40, 7:4, 2:1` | overlapping |
| field1816 | `0:776, 1:90, 2:54, 3:15, 130:3, 129:2, 9:1, 24:1, 57:1, 137:1` (+2 more) | `0:347, 1:84, 2:18, 3:12, 130:1` | overlapping |
| field1831 | `127:47, 20:18, 88:18, 89:17, 83:15, 79:14, 91:14, 85:13, 97:13, 80:12` (+388 more) | `127:25, 76:11, 67:10, 36:9, 68:9, 23:7, 29:7, 40:6, 58:6, 80:6` (+190 more) | overlapping |
| field1823 | `0:938, 254:2, 255:2, 1:1, 3:1, 5:1, 253:1` | `0:425, 255:10, 226:5, 253:5, 254:4, 2:3, 250:3, 6:2, 4:1, 40:1` (+3 more) | overlapping |
| field1837 | `0:930, 254:5, 250:4, 1:2, 2:2, 255:2, 3:1` | `0:439, 3:6, 255:5, 216:2, 1:1, 5:1, 10:1, 20:1, 50:1, 196:1` (+4 more) | overlapping |
| field1827 | `0:900, 1:46` | `0:461, 1:1` | same value set |
| field1824 | `0:931, 1:15` | `0:459, 1:3` | same value set |
| field1832 | `2:940, 0:6` | `2:462` | subset (graphless all 2) |
| field1826 | `1:929, 0:17` | `1:253, 0:209` | same value set |
| field1819 | `1:930, 0:16` | `0:234, 1:228` | same value set |
| field1817 | `0:865, 1:81` | `0:418, 1:44` | same value set |
| field1821 | `0:944, 3:2` | `0:462` | subset (graphless all 0) |
| field1835 | `0:946` | `0:462` | both constant zero |
| field1818 | `0:788, 2:114, 1:44` | `2:289, 0:163, 1:10` | same value set |

**No column is disjoint across the partition.** Not one of the nineteen takes one value on
graph-bearing slots and a different value on graphless ones. So no column in this cache is a clean
marker of "has a procedural graph", and any naming claim that predicts such a split is refuted.

Three columns are **constant on the graphless side** while varying on the graph-bearing side:
field1833 (all 0), field1832 (all 2), field1821 (all 0). All three are near-constant on the
graph-bearing side too (1, 6 and 2 exceptional slots respectively), so this is weak: the exceptions
are so few that landing entirely in the larger partition is unremarkable.

Two columns shift **proportion** sharply across the partition, which is the strongest signal here:

| column | set on graph-bearing | set on graphless | ratio shift |
|---|---|---|---|
| field1819 = 0 | 16 of 946 (1.7%) | 234 of 462 (50.6%) | ~30x |
| field1826 = 0 | 17 of 946 (1.8%) | 209 of 462 (45.2%) | ~25x |
| field1825 = 1 | 149 of 946 (15.7%) | 272 of 462 (58.9%) | ~3.7x |
| field1822 = 1 | 550 of 946 (58.1%) | 50 of 462 (10.8%) | ~0.19x |
| field1818 = 2 | 114 of 946 (12.1%) | 289 of 462 (62.6%) | ~5.2x |

and one shifts the other way:

| column | set on graph-bearing | set on graphless |
|---|---|---|
| field1827 = 1 | 46 of 946 (4.9%) | 1 of 462 (0.2%) |

These are proportions, not partitions. A column at 1.7% against 50.6% still holds the same two
values in both halves, so it cannot *decide* which half a slot is in - but the shift is real and far
too large to be noise at these populations.

**A caution on all of the above.** The graphless slots are repack additions, so a proportion shift
across the partition is equally consistent with "this column means something about graphs" and
"whoever added slots 946..1407 set these columns differently from Jagex". The repack is the only
cache that can be asked the question and it is also the cache whose extra rows were written by
somebody else. Treat every figure in this section as a lead, not a finding.

---

## 7. Pairwise identical columns

**No two of the nineteen columns hold the same value in every slot** - not in the vanilla capture
and not in the repack. All 171 pairs were compared, value against value, over every present record.

So no column is indistinguishable from another by data alone, and no naming of any pair rests
*entirely* on the client for want of a distinguishing record. Every one of the nineteen is
separated from every other by at least one slot in both caches.

The pairs whose *distributions* come closest, and so would be easiest to confuse if only the
summary table were read: field1826 and field1819 (`{0,1}`, 10 and 9 zeros in the vanilla capture,
226 and 250 in the repack), and field1833 and field1821 (one slot set against two, in both caches).
Distribution similarity is not identity, and the census tests identity per slot rather than by
comparing the tallies.

---

## 8. What could not be measured

- **The graph correlation is a repack-only measurement.** The vanilla capture has no graphless slot,
  so its 915 rows contribute nothing to section 6. Anything section 6 says is a fact about a cache
  whose extra 462 rows were authored by the repacker.
- **field1835 is untestable.** Constant zero in both caches, so its width, its signedness and its
  meaning are all unfalsifiable from content. Only the file-length identity holds it at four bytes.
- **The many-to-one boolean collapse is never exercised.** No boolean-shaped column stores a byte
  outside `{0,1}` in either cache, so the codec's care about replaying an aliased boolean byte is
  correct-by-design and confirmed by nothing on disk. Same for the existence column: every declared
  slot is present in both caches, so the "existence byte other than 1" branch has no instance.
- **The inversion's *sense* is not settled by this data.** field1825 stores only 0 and 1, so both
  readings partition the slots identically and only the labels differ. The client settles it; the
  cache cannot.
- **The absent-value gaps are observations, not proofs of a range.** field1820 never stores 3,
  field1832 never stores 1, field1821 never stores 1 or 2. That the value is unused in two caches
  does not mean the format forbids it.
- **Nothing here tests the renderer.** Every figure is about bytes in index 26. What the client does
  with any of them is outside what a census can reach.
