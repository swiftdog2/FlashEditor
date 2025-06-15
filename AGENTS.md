# FlashEditor Contribution Guide

This repository contains a C# RuneScape cache editor targeting **Revision 639**. It provides a WinForms-based GUI to load, view, edit, and update JS5 cache archives. The application targets **.NET Framework 4.7.2** and includes xUnit-based unit tests.

## Build Requirements
- Visual Studio or `msbuild` with .NET Framework 4.7.2 SDK.
- All packages are restored via NuGet. The `packages.config` file lists dependencies such as IKVM, Newtonsoft.Json, and OpenTK.

## Test Suite
- Tests reside under the `FlashEditor.Tests` project and target .NET Framework 4.7.2.
- xUnit (`2.5.0`) and Moq are the primary test dependencies.
- Run `dotnet test` or use Visual Studio Test Explorer to execute the suite.

## Cache Editing Overview
The editor loads, displays, and modifies the RuneScape JS5 cache for revision 639. Important details for understanding the cache structure:

1. **Archive Storage**
   - `main_file_cache.dat2` holds all archive data sequentially.
   - `main_file_cache.idx0` to `idx36` map archive IDs to offsets inside `dat2`.
   - `main_file_cache.idx255` stores CRC and Whirlpool hashes for each other index so the client can detect updates.
   - Each archive inside `dat2` follows the format:
     `[compression-type 1B][compressed-len 3B][uncompressed-len 3B][payload…][optional version 2B]` where compression type `0=GZIP`, `1=BZIP2`, `2=none`.

2. **Index Map for Revision 639**
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

3. **Editing Tips**
   - Choose index → group → file by ID.
   - Decode using the correct opcode table for revision 639; opcodes may differ between revisions.
   - After modifications, recompress the payload (GZIP or BZIP2) and update the corresponding index entry and reference table.
   - Map archives (index 5) require the correct 128-bit XTEA key.
   - Instanced regions (index 33) and particle configs (index 25) appeared in this revision—older clients may ignore them.

The editor also supports features such as MIDI music playback and the modern index layout (0–36 plus 255). When porting assets to other revisions, keep in mind that opcode mappings and file formats may differ significantly after revision 669.

## Performance Guidelines
- Avoid using heavy LINQ chains like `Where().Select().ToList()` inside performance-critical loops. Use a single-pass `for` loop to filter and transform in one step.
