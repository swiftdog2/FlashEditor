# The location file: `l<x>_<y>`

A flat, header-less, delta-and-smart-encoded stream of static object placements for one 64x64 map
square. No header, no count, no index.

Decoder: `Class305_Sub1.method3591` (`Class305_Sub1.java:1503-1574`), reached from
`Class181.method2607` via `ParticleType.method898`. The dynamic-region variant is
`Class305_Sub1.method3584`.

**MEASURED**: 1885 files in `server/mapdata` decode byte-exactly to EOF, 2,281,028 locs total, with
shapes strictly in 0..22, planes strictly in 0..3, x and y strictly in 0..63, and 643 empty regions
that are exactly one byte `0x00`.

Remember from `01-cache-access.md`: **659 of the 1684 `l` groups in the cache are XTEA-encrypted**
and must be decrypted before any of this applies.

## 1. The grammar

```
id = -1
while ((idDelta = readExtendedSmart()) != 0) {
    id += idDelta

    position = 0
    while ((posDelta = readSmart()) != 0) {
        position += posDelta - 1

        plane  =  position >> 12
        localX = (position >> 6) & 0x3F
        localY =  position       & 0x3F

        attributes = readUnsignedByte()
        shape      = attributes >> 2
        rotation   = attributes & 3
    }
}
```

Nothing else follows per loc in this revision.

A minimal valid empty region is exactly one byte: `0x00`.

## 2. The two smart readers are different

This is the single most common porting error on this format.

| Field | Reader | Source |
|---|---|---|
| Object id delta | **extended** smart | `RSBuffer.method1208` (`RSBuffer.java:288-304`) |
| Position delta | plain smart | `RSBuffer.method1206` |

The extended smart loops while the value reads 32767, accumulating as it goes:

```csharp
public int ReadExtendedUnsignedSmart() {
    int total = 0, v;
    while ((v = ReadUnsignedSmart()) == 32767)
        total += 32767;
    return total + v;
}
```

**MEASURED**: 63 `l` groups and 260 `ul` groups in the shipped cache contain a continuation. Using
a plain smart for the id delta corrupts the stream on **281** of them. The existing C# at
`Region.cs:103` uses a plain smart here and is wrong; `Region.cs:109`, the position delta, is
already correct and must not be changed.

There is a third variant, `readSmart2` (`RSBuffer.java:870-876`), which biases by -1 / -32769. The
loc stream does **not** use it - the `- 1` is applied manually at `Class305_Sub1.java:1530`.

## 3. The position word

**CONFIRMED**: `Class305_Sub1.java:432` is `plane = position >> 12` with **no mask**.

**MEASURED**: the maximum position across every readable `l` and `ul` group is exactly 16383, so
`position >> 12` is already 0..3 on good data. The existing C# masks it with `& 0x3`
(`Region.cs:114`). That mask can therefore only ever fire on a desynced stream, where it silently
converts a garbage plane into a plausible one and hides the failure.

Drop the mask and add a tripwire instead:

```csharp
if (position > 16383 || shape > 22) throw new InvalidDataException("loc stream desync");
```

Those two conditions are the cheapest available detector for a wrong-reader desync.

## 4. Shapes

23 shapes, routed to four scene slots by `Class64_Sub17.anIntArray3685`:

```
{0,0,0,0, 1,1,1,1,1, 2,2,2,2,2,2,2,2,2,2,2,2,2, 3}
```

| Shapes | Group | Meaning |
|---|---|---|
| 0-3 | 0 | Wall |
| 4-8 | 1 | Wall decoration |
| 9-21 | 2 | Game object |
| 22 | 3 | Ground decoration |

Bounds-check the index at 23. The master dispatcher is `Class305_Sub1.method3588`
(`Class305_Sub1.java:699-1307`).

> Citation note: `method3588` does **not** read `anIntArray3685` - it duplicates the same partition
> as a hardcoded if/else chain. The array's actual readers are `Node_Sub10_Sub22.java:52,157,385,470`
> and `PacketParser.java:1542`. The partition is identical either way; this is only relevant if you
> go looking for the table in the dispatcher and cannot find it.

Two genuine divergences between the static and dynamic paths, **CONFIRMED**:

- Shape 6 uses axis-aligned offset vectors on the static path but diagonal vectors on the dynamic path.
- Shape 9 gets a 1x1 bounding box on the static path but its full footprint on the dynamic path.

## 5. Footprints, and the size-field swap

> **The field names in the client are backwards.** `Class352` opcode 14 is decoded into the field
> spelled `sizeY` but is the true **X** extent at rotation 0. Opcode 15 is decoded into `sizeX` but
> is the true **Y** extent.
>
> **CONFIRMED** by three independent consumers: `Class305_Sub1.java:713-736` (assigns
> `xExtent = def.sizeY` for rotations 0 and 2), `Class243.method2951`, and
> `Node_Sub31_Sub4.java:98-113`.

Port by axis, not by Jagex's spelling. In C#, decode **opcode 14 into `SizeX`** and
**opcode 15 into `SizeY`**, which is the opposite naming to the client's fields, then:

| Rotation | X extent | Y extent |
|---:|---|---|
| 0, 2 | `SizeX` (op 14) | `SizeY` (op 15) |
| 1, 3 | `SizeY` (op 15) | `SizeX` (op 14) |

Concretely, an object with op14=3 and op15=1 occupies tiles `(x .. x+2, y)` at rotations 0 and 2,
and `(x, y .. y+2)` at rotations 1 and 3. Copying the client's field spellings into C# types called
`SizeX`/`SizeY` transposes every multi-tile object in the world.

## 6. Clipping

Written into `Class243`, one per plane, as an `int[N+6][N+6]` with origin offset -1. Three parallel
8-direction bit planes plus occupancy bits:

| Layer | Shift | Gate |
|---|---|---|
| Reach / interaction | base | always |
| Projectile | `<< 9` | gated on `blocksProjectile` |
| Walk | `<< 22` | gated on `!removeClipping` (i.e. "not hollow") |

Plus `0x100` / `0x20000` / `0x40000000` occupancy, `0x200000` "no floor", `0x40000` "blocked ground
decoration".

> Naming trap: the `Class352` field that reads like `walkable` is actually **`blocksProjectile`**.
> It is set by opcodes 17 and 18, not 21.

## 7. ObjectDefinition opcode corrections

`Class352.method3863`. The prior `HYDRA_CACHE_SPEC.md` section 17.2 is shifted by one across most
of the table. Corrections, all **CONFIRMED**:

| Op | Reality | Spec said |
|---:|---|---|
| 14 | X extent at rotation 0 (field spelled `sizeY`) | "sizeY / Y dimension" |
| 15 | Y extent at rotation 0 (field spelled `sizeX`) | "sizeX / X dimension" |
| 17, 18 | `blocksProjectile = false` | - |
| 19 | Clickable / has-interaction flag, also derived post-decode (`Class352.java:1522-1536`) | "Unknown" |
| 21 | Contoured-ground type = 1 | "walkable = false" |
| 22 | Delayed / non-flat shading, no payload | "Offset field" |
| 23 | Occlude, no payload | "Boolean flag" |
| 24 | u16 animation id, 65535 to -1 | "anInt2956 = 1" |
| 28 | Decor displacement, `u8 << 2` | lumped 28-30 |
| 29 | Ambient, signed byte | lumped 28-30 |
| 30-34 | Action strings | - |
| 40 | Recolour: `u8 count`, then count x (`u16 from`, `u16 to`) | "Action name / string" |
| 41 | Retexture: same shape (`Class352.java:1432-1441`) | "colorSources" |
| 42 | Per-entry palette-index byte array | "colorBytes" |
| 43 | **does not exist** | "textureMaps" |
| 64 | `castsShadow = false`, no payload | "scaleY" |
| 65, 66, 67 | Model scale X / height / Y, u16, default 128 | listed as 64 / 66 / 68 |
| 68 | **does not exist** | "scaleZ" |
| 70, 71, 72 | **Translations**, not rotations: signed `i16 << 2` for X / height / Y | "rotateX/Y/Z" |
| 77, 92 | Varbit/varp transform block; 92 has an extra u16 default | - |
| 78 | Ambient sound: `u16 soundId`, `u8 radius` | "model+anim" |
| 93 | `u16` into `anInt2985`, contoured-ground type 3 | "altInteract (ushort x3 + array)" |
| 94 | Contoured-ground type 4, no payload | "animType = 4" |
| 95 | Contoured-ground type 5 plus `i16` | listed as 96 |
| 150-154 | Action strings, second bank | - |
| 170, 171 | **Placement-critical**: occluder height (default 960) and lateral offset (default 0), both `readSmart` | absent |
| 107, 160, 162-169, 173, 177, 178, 249 | Handled by the decoder | absent |

## 8. Locs referencing missing definitions

**MEASURED**: 8 `l` files reference object ids that do not exist in index 16 - `l47_51` (732 locs,
91 distinct ids), `l47_53` (611), `l48_53` (199), `l46_52` (39), `l44_157` (26), `l43_158` (11),
`l45_157` (2), `l50_55` (1). Ids run to 61871 against a maximum valid id of 57264.

Almost certainly HydraScape custom content plus some cache/definition-table skew. A port must
**not** treat "object id not in index 16" as a decode error.
