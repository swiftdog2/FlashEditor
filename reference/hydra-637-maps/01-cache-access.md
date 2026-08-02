# Getting map bytes out of the cache

Everything in this file is upstream of any map format. If this layer is wrong, nothing below it
can be right.

## 1. Index 5 is name-hash addressed

**CONFIRMED.** There is no region-to-file table. Every map lookup goes through the JS5 group name.

```java
// Class61.java:49-57 - all five families, per region
mapContainer.requestFile("m"  + rx + "_" + ry);
mapContainer.requestFile("l"  + rx + "_" + ry);
mapContainer.requestFile("um" + rx + "_" + ry);
mapContainer.requestFile("ul" + rx + "_" + ry);
mapContainer.requestFile("n"  + rx + "_" + ry);
```

Note there is **no underscore after the prefix**. The name is `m50_50`, not `m_50_50`. The server
at `HydraScape/server/src/net/tazogaming/hydra/map/MapFetcher.java:127` builds `"l_" + x + "_" + y`,
which hashes to nothing in this cache (**MEASURED**: 0 hits for the `l_` form, 1684 for `l`). Do not
copy that string.

The hash is the classic Jagex one, over the lowercased name:

```csharp
static int Hash(string name) {
    unchecked {
        int h = 0;
        foreach (char c in name.ToLowerInvariant())
            h = h * 31 + Cp1252(c);
        return h;
    }
}
```

`Cp1252(c)` is `(sbyte)c` for `0 < c < 128`. Map names are pure ASCII so the sign never matters
here, but the lowercasing does: the client calls `toLowerCase()` at every call site.

Lookup is an open-addressed table (`Class122`): size is the smallest power of two at least
`n + (n >> 1)`, stored as an interleaved `int[2 * size]` of key/value, probe at `hash & (size - 1)`
then linear, miss returns -1.

### `MapIndex.java` is dead code

**CONFIRMED.** `MapIndex.java` and `map_index.dat` have **zero call sites** anywhere in the client.
`getMapIndex` is private with no in-class caller and `loadIndicies` is never called. The server
loads `map_index.dat` at `MapFetcher.java:42-63` and then immediately overwrites both ids from the
cache at `MapFetcher.java:81-84`, so it is vestigial in both halves of the codebase.

Do not port it. **MEASURED**: against the shipped cache it is wrong for 496 of 1673 regions and
would clamp 505 of 1673 ids to -1.

### The five families

**MEASURED** by brute-forcing every name hash in the index-5 reference table:

| Prefix | Content | Groups | Encrypted |
|---|---|---:|---|
| `m` | Terrain: heights, underlay, overlay, tile flags | 1684 | No |
| `l` | Locations: static object placements | 1684 | **659 of 1684** |
| `um` | Underwater terrain, single plane | 900 | No |
| `ul` | Underwater locations | 900 | No |
| `n` | NPC spawn table | 35 | No |
| | **Total** | **5203** | |

Zero unmatched hashes, so no sixth family exists in this cache. `um`/`ul` cover an identical
900-region subset of the 1684. Treat a -1 lookup as "this family does not exist for this region",
not as an error.

Every group holds exactly one file, child id 0.

## 2. The JS5 container

**CONFIRMED**, `Node_Sub46_Sub10.method1571` (`Node_Sub46_Sub10.java:393-436`).

```
[u8  compressionType]
[i32 compressedLength]
[i32 decompressedLength]   <- present only when compressionType != 0
[    payload             ]
[u16 version             ]  <- trailer, outside the container proper
```

| compressionType | Meaning |
|---:|---|
| 0 | Uncompressed |
| 1 | BZIP2 |
| anything else | GZIP (conventionally 2) |

The prior spec's `0xFF`/`0xFE`/`0xFD` values are a mis-normalisation of
`if ((type ^ 0xffffffff) == -1)`, which reads `type == 0`.

Two decoding details a port will trip over:

- **BZIP2**: the 4-byte `BZh1` magic is **not stored**. Blocksize is hardcoded to 1
  (`Class330.java:92`). Prepend `42 5A 68 31` before handing the payload to a decompressor.
- **GZIP**: the client uses a raw `Inflater(nowrap)` over `buffer[caret+10 .. length-18]`
  (`Class263.java:184-186`), skipping the 10-byte gzip header and ignoring the 8-byte trailer.
  Because `buffer.length` includes the 2-byte version trailer, it actually feeds the inflater two
  bytes too many. Harmless in raw mode; a stricter C# inflater will notice.

**MEASURED**: every one of the 5203 index-5 groups is compressionType 2 (GZIP).

## 3. The version trailer and the CRC

**MEASURED across all 5203 index-5 groups**: the reference-table CRC32 equals
`crc32(stored[0 .. storedLength - 2))` for every single group. The final two bytes are the
big-endian group **version**, not a CRC.

So:

```
storedLength = 9 + compressedLength + 2
CRC32 covers [0, storedLength - 2)
version      = big-endian u16 at [storedLength - 2, storedLength)
```

Worked example, group 4240 = `l1_30`:

```
stored bytes    : 3537
reftable CRC32  : 0x39535705   crc32(stored[:-2]) : 0x39535705   match
reftable version: 4            trailer bytes      : 00 04
header (clear)  : 02 00 00 0d c6   -> comp=2, compressedLength=3526
```

## 4. XTEA

### Where it applies

**MEASURED**: exactly **659 of the 1684 `l` groups are XTEA-encrypted**. Every `m`, `um`, `ul` and
`n` group is plaintext. There is no per-group encryption flag in a format-6 table, so the only
signal is that the payload does not parse.

Three independent lines of evidence:

1. All 659 have structurally perfect clear headers (`compressionType` 2, and
   `9 + compressedLength + 2 == storedLength` exactly) with garbage payloads. That is the signature
   of encryption starting at offset 5, not of corruption.
2. **528 of the 659 decrypt cleanly** with keys pooled from `HydraScape/data/xteas`, each producing
   a valid gzip stream whose CRC32 and ISIZE trailer both match.
3. All 659 are **absent from `HydraScape/server/mapdata`**, and not one of the 4525 plaintext groups
   is. The tool that produced that directory hit exactly the same wall. 100% correlation.

The remaining 131 have no key in any shipped dump. They are **INFERRED** to be encrypted rather
than corrupt, on the header evidence plus the mapdata correlation.

### Detecting encryption

Do **not** detect by "does it inflate". **MEASURED**: 20 encrypted `l` groups inflate successfully
as raw deflate over their own ciphertext and yield 6 bytes of garbage. Detect on the gzip magic at
`stored[9..12] == 1F 8B 08`.

### The range

The correct range is `[5, storedLength - 2)`. Block count is `(storedLength - 7) / 8`, whole
8-byte blocks only, trailing partial block left in the clear.

> **CLIENT BUG.** `JS5Archive.java:348` passes `buffer.length` as the end offset, and that buffer
> includes the 2-byte version trailer. The range it enciphers is therefore `[5, storedLength)`.
> **MEASURED**: on 120 of 528 recoverable groups (22.7%) this covers one extra block. The extra
> block provably always starts inside the 8-byte gzip trailer and never in the deflate stream, so
> the decoded map data is byte-identical either way (`same_payload 528/528`) and the client's raw
> `Inflater` never checks the trailer it just corrupted.
>
> A C# port using `GZipStream` will throw on ~23% of encrypted regions. Use `[5, storedLength - 2)`.

Worked example of the difference, group 2502 = `l55_150`:

```
stored 45 bytes, comp=2, compressedLength=34, version trailer 00 01

client range [5,45): 5 blocks
    trailer CRC32=0xEFB13A74 ISIZE=913266872   actual CRC32=0xB4D83A74 len=52   CORRUPT
correct range [5,43): 4 blocks
    trailer CRC32=0xB4D83A74 ISIZE=52          actual CRC32=0xB4D83A74 len=52   OK
payload identical in both cases
```

### The cipher

Textbook 32-round XTEA. Delta `0x9E3779B9`, big-endian words, key is four `int32`.
`RSBuffer.method1215` (`RSBuffer.java:346-375`) is **decrypt** - its sum starts at
`-957401312` (delta * 32) and counts down. `RSBuffer.method1235` (`RSBuffer.java:551-584`) is
**encrypt** and is used only on outgoing login blocks, never on the cache. The prior spec has these
two reversed.

An all-zero key means "not encrypted" and skips decryption entirely (`JS5Archive.java:342`).

### The client cannot decrypt its own loc files

> **CLIENT BUG, and the reason the map looks empty in places.** The keys are wired to the wrong
> family.
>
> ```java
> // Class181.java:44 - the loc file, which IS encrypted, gets a null key
> Class255.landscapeDatas[i] = mapContainer.getDecryptedFile(null, 5, 0, LANDSCAPE_FILE_IDS[i]);
>
> // Class181.java:76-77 - the n file, which is never encrypted, gets the keys
> Class105.aByteArrayArray3414[i] = mapContainer.getDecryptedFile(MAP_XTEA_KEYS[i], 5, 0, nIds[i]);
> ```
>
> All 659 encrypted regions therefore fall into the container decoder's lenient failure path
> (`Node_Sub46_Sub10.java:412-423`), which returns a 100-byte buffer of zeros rather than throwing.
> A loc stream of zeros terminates immediately, so those 659 regions render with **no objects at
> all**. The `n` path that does get keys is itself dead: both live network paths null the `n` id
> array (`Node_Sub36.java:134`, `Node_Sub41.java:58`).
>
> FlashEditor should pass keys for `l`. A missing key must yield an empty loc list, not an
> exception, matching the client's observable behaviour for the 131 unrecoverable regions.

### Key sources

`HydraScape/data/xteas` pools to **4423 regions / 10425 distinct keys** across three shapes:

| File | Layout |
|---|---|
| `xteas.zip` | `xteas/<revision>/<regionId>.txt`, four lines of signed decimal int32, 37 revisions (508 to 742), 58717 entries |
| `718xteas.zip` | flat `<regionId>.txt`, same four-line format |
| `XTEAS.txt` | human log: `Region <id>` then `Found xtea=[a, b, c, d] in revision=<r>` |

Region id is `(x << 8) | y`, parsed from the `l<x>_<y>` group name.

`HydraScape/server/config/mapxtea.bin` is **missing**, which is why the server sends all-zero keys.

## 5. The reference table

**CONFIRMED** (`VersionTable.method3622`), and **MEASURED** by consuming the real index-5 table
exactly (114474 of 114474 bytes).

```
u8   protocol                  (5 or 6; anything else throws)
i32  revision                  only if protocol >= 6
u8   flags
u16  validGroupCount
u16  groupIdDelta   x N
i32  groupNameHash  x N        only if flags & 0x01
i32  groupCrc32     x N
64B  groupWhirlpool x N        only if flags & 0x02
i32  groupVersion   x N
u16  childCount     x N
u16  childIdDelta   x ...
i32  childNameHash  x ...      only if flags & 0x01
```

The prior spec places CRC before names and whirlpool before version, and omits both the protocol-6
revision int and the group name-hash block entirely.

**Only bits 0 and 1 are read.** The rest of the flags byte is discarded
(`VersionTable.java:123,125`).

**MEASURED** flags bytes in the shipped cache: index 5 is `0x01` (named, no whirlpool), index 2 is
`0x00` (not named at all - config lookups must be by numeric group id). **Bit 2 (`0x04`, the sizes
block documented in `AGENTS.md`) is set nowhere in the entire 639 cache**, and this client has no
code that could read it. Gate any sizes support on `(flags & 4) != 0` so it stays inert here.

Index 5: protocol 6, revision 1233, 5203 groups.

**Tolerate trailing bytes.** **MEASURED**: four of the 35 shipped tables (indices 9, 26, 27, 29)
carry an extra all-zero `i32` per child past the documented end. Index 5 and index 2 consume
exactly, but a generic parser must not assert `Remaining == 0`.

## 6. Caching

**CONFIRMED.** Index 5 is opened with `openFileStore(-54, true, 1, 5)`
(`InterfaceSettings.java:163`). Because every index-5 group holds exactly one child,
`JS5Archive.java:246-264` nulls both the unpacked child and its parent slot on **every** read, and
`JS5Archive.java:365-367` nulls the packed container too. Index 5 caches nothing.

This directly contradicts the prior spec's "map data is kept in memory to avoid repeated
decompression". If a port wants a cache, make it an explicit opt-in layer above the archive, and
note the discard flag is per-archive (indices 5, 6 and 23 set it; the rest do not).
