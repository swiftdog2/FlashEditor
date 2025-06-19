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
- `groupCrc`, `groupVersion`, and `groupFlags` arrays (bit0 = XTEA)
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
- Optional 3-byte `cumulativeLengths[]` table when the container has multiple files

Revision 639 (and later) expects the header layout above. Some earlier
revisions swapped the two length fields, so both encode and decode must
use this ordering to remain compatible with the in-game client.

### XTEA Layer
- Applied after compression and before sectorisation
- 32-round standard XTEA over 8-byte blocks
- Key of four 32-bit integers (`0,0,0,0` indicates no encryption)

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
