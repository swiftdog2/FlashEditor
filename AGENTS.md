# FlashEditor Contribution Guide

This repository contains a C# RuneScape cache editor targeting **Revision 639**. It provides a WinForms-based GUI to load, view, edit, and update JS5 cache archives. The application targets **.NET 9** and includes xUnit-based unit tests.

## Build Requirements
- Visual Studio or `dotnet` with .NET 9 SDK.
- All packages are restored via NuGet. The `packages.config` file lists dependencies such as IKVM, Newtonsoft.Json, and OpenTK.

## Test Suite
- Tests reside under the `FlashEditor.Tests` project and target .NET 9.
- xUnit (`2.5.0`) and Moq are the primary test dependencies.
- Run `dotnet test` or use Visual Studio Test Explorer to execute the suite.

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

### Index Entry (.idx#)
- 6 bytes per group
- Bytes `0–2` → compressed length
- Bytes `3–5` → first sector offset

### Sector Header (dat2)
`|2 bytes idxId|2 bytes groupId|3 bytes nextSector|1 byte chunk#|512 data|`

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
- `chunks` is commonly **3**, not 1 - roughly two thirds of the multi-file archives in a real
  639 cache use three. Laying the files out end to end instead of chunk-major produces a
  payload of exactly the same length with the bytes in the wrong order, which a round trip
  through this codec cannot detect. Encoding must either reproduce the split it decoded or
  drop to a single chunk, which is a shape the client also reads.

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
  carry `name_hash`, `name` (`l<x>_<y>`) and `mapsquare`. Build 639 is cache id 1194.

### Minimal Cache-Editor API
```csharp
class CacheEditor {
    CacheEditor(Path folder);
    Container read(int indexId, int groupId);
    void write(int indexId, int groupId, Container c, int[] xteaKeyOrNull);

    ReferenceTable getTable(int indexId);
    void saveTable(int indexId, ReferenceTable t);

    byte[] readChild(int index, int group, int child, int[] key);
    void writeChild(int index, int group, int child, byte[] data,
                    Compression cmp, int[] key);
}

class Container {
    byte   compressionType;
    byte[] dataUncompressed;
    byte[][] childSlices;   // null if single-file
}
```

### Write Algorithm (high-level)
1. Build container (compress → XTEA → add length table)
2. Allocate/extend sector chain, write chunks, update `.idx#`
3. Update reference table (CRC32, version, flags) and re-CRC it
4. Write updated reference table back into index 255
5. Optionally bump idx255’s own CRC/version

Index Map for Revision 639
   | ID  | Contents                      |
   |----|--------------------------------|
   | 0  | Animation frames               |
   | 1  | Animation skins                |
   | 2  | Configs (items, objects, NPCs) |
   | 3  | Interfaces                     |
   | 4  | Unused                         |
   | 5  | Maps (XTEA encrypted)          |
   | 6  | Unused                         |
   | 7  | 3-D models (.m meshes)         |
   | 8  | Sprites                        |
   | 9  | Unused                         |
   |10  | Huffman chat table             |
   |12  | Client scripts (CS2)           |
   |13  | Font metrics                   |
   |14  | Sound effects                  |
   |16  | MIDI instrument bank           |
   |17  | MIDI tracks                    |
   |18  | Textures                       |
   |19  | Enums                          |
   |20  | Legacy loader sprites          |
   |21  | Spot-animation definitions     |
   |22  | World-map composites           |
   |23  | Quick-chat phrases             |
   |24  | Material/lighting configs      |
   |25  | Particle configs               |
   |26  | Default chest/key definitions  |
   |27  | Cut-scene scripts              |
   |28  | Billboard/UV data              |
   |29  | Shader programs                |
   |30  | Client preferences             |
   |31  | GE/database tables             |
   |32  | Clan-citadel configs           |
   |33  | Instanced region templates     |
   |34  | Item morph tables              |
   |35  | Struct definitions             |
   |36  | Extended enums                 |
   |255 | Reference table                |

## Coding Guidelines
- Include C# XML documentation comments on public classes and members whenever the intent isn't obvious.
