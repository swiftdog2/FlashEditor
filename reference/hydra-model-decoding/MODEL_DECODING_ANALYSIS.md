# Hydra Client - Model Decoding: Exhaustive Analysis

## Table of Contents
1. [Overview & Loading Chain](#1-overview--loading-chain)
2. [Format Detection Logic](#2-format-detection-logic)
3. [RSBuffer - Binary Stream Reader](#3-rsbuffer---binary-stream-reader)
4. [Field Name Mapping (Obfuscated → Semantic)](#4-field-name-mapping)
5. [Legacy Decoder: method2587()](#5-legacy-decoder-method2587)
6. [Newer Decoder: decoder_newer_format()](#6-newer-decoder-decoder_newer_format)
7. [Newest Decoder: decoder_newest_format()](#7-newest-decoder-decoder_newest_format)
8. [Triangle Strip Compression](#8-triangle-strip-compression)
9. [Smart Encoding (Variable-Length Integers)](#9-smart-encoding)
10. [Textured Triangle Types](#10-textured-triangle-types)
11. [Model Particles & Bonds](#11-model-particles--bonds)
12. [Data Layout Diagrams](#12-data-layout-diagrams)
13. [Comparison of All Three Formats](#13-comparison-of-all-three-formats)
14. [Supporting Classes](#14-supporting-classes)

---

## 1. Overview & Loading Chain

### Entry Points

**JS5Archive.getChildFromFolder()** (JS5Archive.java:203)
```
getChildFromFolder(groupId, fileId) → getDecryptedFile(null, 5, fileId, groupId)
```
- Fetches raw compressed bytes from the JS5 cache store
- Decompresses via `Node_Sub46_Sub10.method1571()`
- Splits multi-file groups using a footer-based chunk table
- Returns raw `byte[]` of the model data

**Node_Sub6.method981()** (Node_Sub6.java:59-66)
```java
static final Model method981(JS5Archive archive, int fileId, int groupId, int particleId, int toOverride) {
    byte[] data = archive.getChildFromFolder(groupId, fileId);
    if (data == null) return null;
    return new Model(data, groupId, particleId, toOverride);
}
```
- `groupId` = the model ID (which group in archive 7)
- `fileId` = specific file within the group
- `particleId` / `toOverride` = particle effect substitution

**Model Constructor** (Model.java:81-106)
```java
Model(byte[] is, int id, int particleId, int toOverride) {
    this.modelID = id;

    // Special IDs use newest protocol
    if (modelID >= 63607 && modelID <= 63613) {
        newProtocol = true;
        decoder_newest_format(is);
        return;
    }

    // Check last 2 bytes for format marker
    if (is[is.length - 1] == -1 && is[is.length - 2] == -1) {
        decoder_newer_format(is, 1);  // 0xFFFF marker = newer format
    } else {
        method2587(is, -1);           // no marker = legacy format
    }
}
```

---

## 2. Format Detection Logic

```
┌─────────────────────────────────┐
│       Model byte[] received     │
└──────────────┬──────────────────┘
               │
     ┌─────────▼──────────┐
     │ modelID in range   │   YES
     │ [63607, 63613] ?   ├──────────► decoder_newest_format()
     └─────────┬──────────┘             (newProtocol = true)
               │ NO
     ┌─────────▼──────────┐
     │ Last 2 bytes ==    │   YES
     │ 0xFF 0xFF ?        ├──────────► decoder_newer_format()
     └─────────┬──────────┘
               │ NO
               │
               ▼
         method2587()                   (Legacy format)
```

**Key difference**: The `newProtocol` flag is ONLY set for model IDs 63607-63613. The `decoder_newest_format` method handles BOTH `newProtocol=true` AND the regular newer format (it has internal branching). The `decoder_newer_format` is used when the 0xFFFF end marker is present but the model ID is NOT in the special range.

---

## 3. RSBuffer - Binary Stream Reader

RSBuffer wraps a `byte[]` with a movable `caret` (position cursor). All model decoders create multiple RSBuffer instances pointing to the SAME byte array but with different caret positions to read different sections in parallel.

### Key Read Methods Used in Model Decoding

| Method | Signature | Bytes | Returns | Description |
|--------|-----------|-------|---------|-------------|
| `readUnsignedByte()` | `:896` | 1 | 0..255 | `buffer[caret++] & 0xFF` |
| `readSignedByte()` | `:853` | 1 | -128..127 | `buffer[caret++]` (raw signed) |
| `readUnsignedShort()` | `:901` | 2 | 0..65535 | Big-endian: `(buf[c] << 8) \| buf[c+1]` |
| `readShort()` | `:820` | 2 | -32768..32767 | Same as above but sign-extends >32767 |
| `readInt()` | `:753` | 4 | full int | Big-endian 32-bit |
| `method1186()` | `:131` | 3 | 0..16777215 | 24-bit medium: `(buf[c]<<16) \| (buf[c+1]<<8) \| buf[c+2]` |
| `method1239()` | `:606` | 1-2 | -64..16383 | **Smart vertex delta** (see Section 9) |
| `readSmart(int)` | `:857` | 1-2 | 0..32767 | Peek: <128 → 1 byte; >=128 → 2 bytes - 32768 |
| `readSmart2()` | `:870` | 1-2 | -1..32766 | Peek: <128 → byte-1; >=128 → short-32769 |

---

## 4. Field Name Mapping

The decompiled code uses obfuscated field names. Here's the semantic mapping:

### Geometry Arrays

| Obfuscated Name | Type | Size | Semantic Name | Description |
|-----------------|------|------|---------------|-------------|
| `vertices` | int | 1 | vertexCount | Number of vertices |
| `triangles` | int | 1 | faceCount | Number of triangles/faces |
| `texturedTriangles` | int | 1 | textureFaceCount | Number of textured face mappings |
| `anIntArray1407[]` | int[] | vertices | vertexX | X coordinates |
| `anIntArray1408[]` | int[] | vertices | vertexY | Y coordinates |
| `anIntArray1409[]` | int[] | vertices | vertexZ | Z coordinates |
| `aShortArray1393[]` | short[] | triangles | faceVertexA | Face vertex index 1 |
| `aShortArray1410[]` | short[] | triangles | faceVertexB | Face vertex index 2 |
| `aShortArray1392[]` | short[] | triangles | faceVertexC | Face vertex index 3 |
| `anInt1406` | int | 1 | maxVertexIndex | Highest vertex index used +1 |

### Face Properties

| Obfuscated Name | Type | Size | Semantic Name | Description |
|-----------------|------|------|---------------|-------------|
| `aShortArray1415[]` | short[] | triangles | faceColor | HSL color ID per face |
| `aByteArray1402[]` | byte[] | triangles | faceAlpha | Per-face transparency (if i5==255) |
| `aByte1422` | byte | 1 | globalAlpha | Single alpha for all faces (if i5!=255) |
| `aByteArray1414[]` | byte[] | triangles | faceRenderFlags | Transparency/priority flags |
| `aByteArray1411[]` | byte[] | triangles | faceRenderType | Render type per face |
| `anIntArray1395[]` | int[] | triangles | facePriority | Render order priority |
| `aShortArray1409[]` | short[] | triangles | faceTexture | Texture ID per face (-1 = none) |
| `aByteArray1420[]` | byte[] | triangles | textureMapping | Texture coord mapping index |

### Vertex Properties

| Obfuscated Name | Type | Size | Semantic Name | Description |
|-----------------|------|------|---------------|-------------|
| `anIntArray1411[]` | int[] | vertices | vertexBoneWeight | Bone/skin group for animation |

### Texture Face Data

| Obfuscated Name | Type | Size | Semantic Name | Description |
|-----------------|------|------|---------------|-------------|
| `aByteArray1388[]` | byte[] | texFaces | textureFaceType | Type of texture mapping (0-3) |
| `aShortArray1403[]` | short[] | texFaces | texFaceVertA | Texture face vertex index 1 |
| `aShortArray1421[]` | short[] | texFaces | texFaceVertB | Texture face vertex index 2 |
| `aShortArray1385[]` | short[] | texFaces | texFaceVertC | Texture face vertex index 3 |
| `anIntArray1389[]` | int[] | texFaces | texCoordU | Texture U coordinate |
| `anIntArray1404[]` | int[] | texFaces | texCoordV | Texture V coordinate |
| `anIntArray1390[]` | int[] | texFaces | texCoordW | Texture W coordinate |
| `aByteArray1423[]` | byte[] | texFaces | texAlpha | Texture alpha value |
| `aByteArray1399[]` | byte[] | texFaces | texColor | Texture color modifier |
| `anIntArray1412[]` | int[] | texFaces | texLayerIndex | Texture layer |
| `anIntArray1397[]` | int[] | texFaces | texScaleU | Texture scale U (type 2 only) |
| `anIntArray1386[]` | int[] | texFaces | texScaleV | Texture scale V (type 2 only) |

### Other

| Obfuscated Name | Type | Description |
|-----------------|------|-------------|
| `formatType` | int | Model format version (12-16), affects texture coord sizes |
| `modelParticles[]` | ModelParticle[] | Particle effects attached to faces |
| `aClass35Array1398[]` | Class35[] | Emitter attachment points |
| `aClass106Array1419[]` | Class106[] | Model bonds/animation constraints |

---

## 5. Legacy Decoder: method2587()

**Location**: Model.java:1363-1630
**Trigger**: Last 2 bytes of data are NOT 0xFF 0xFF
**Footer size**: 18 bytes

### 5.1 Footer Layout (read from `length - 18`)

```
Offset  Size   Field        Description
──────  ────   ─────        ───────────
0       2      vertices     Vertex count (unsigned short)
2       2      triangles    Face count (unsigned short)
4       1      texFaces     Textured triangle count (unsigned byte)
5       1      i_5_         Render info flag (1 = has faceRenderFlags, faceTexture, textureMapping)
6       1      i_6_         Alpha: 255 = per-face array; else = global alpha value
7       1      i_7_         Render type flag (1 = has faceRenderType array)
8       1      i_8_         Priority flag (1 = has facePriority array)
9       1      i_9_         Bone weight flag (1 = has vertexBoneWeight array)
10      2      i_10_        X-delta data size in bytes
12      2      i_11_        Y-delta data size in bytes
14      2      i_12_        Z-delta data size in bytes
16      2      i_13_        Face index data size in bytes
```

### 5.2 Data Section Layout (sequential from byte 0)

```
Offset Calculation                 Section                    Contents
──────────────────────────────     ───────                    ────────
0                                  vertexFlags[vertices]      1 byte per vertex (which axes have deltas)
+vertices                          faceStripType[triangles]   1 byte per face (triangle strip opcode)
+triangles                         faceAlpha[triangles]       (only if i_6_==255) signed byte per face
+triangles (cond)                  facePriority[triangles]    (only if i_8_==1) unsigned byte per face
+triangles (cond)                  faceRenderFlags[triangles] (only if i_5_==1) unsigned byte per face
+vertices (cond)                   vertexBones[vertices]      (only if i_9_==1) unsigned byte per vertex
+triangles (cond)                  faceRenderType[triangles]  (only if i_7_==1) signed byte per face
i_22_ = above                      faceIndices[i_13_ bytes]  Smart-encoded triangle vertex indices
i_23_ = i_22_ + i_13_              faceColor[triangles*2]    unsigned short per face (color HSL)
i_24_ = i_23_ + tri*2              texFaceVerts[texFaces*6]  3x unsigned short per textured face
i_25_ = i_24_ + texFaces*6         xDeltas[i_10_ bytes]      Smart-encoded X deltas
i_26_ = i_25_ + i_10_              yDeltas[i_11_ bytes]      Smart-encoded Y deltas
i_27_ = i_26_ + i_11_              zDeltas[i_12_ bytes]      Smart-encoded Z deltas
```

### 5.3 Vertex Decoding (lines 1464-1487)

Vertices are delta-encoded. Each vertex has a flag byte indicating which axes have changed:

```
For each vertex v (0..vertices-1):
    flag = readUnsignedByte()       // from vertexFlags section

    dx = 0
    if (flag & 0x1):                // bit 0 = X present
        dx = method1239()           // smart vertex delta from xDeltas section

    dy = 0
    if (flag & 0x2):                // bit 1 = Y present
        dy = method1239()           // from yDeltas section

    dz = 0
    if (flag & 0x4):                // bit 2 = Z present
        dz = method1239()           // from zDeltas section

    vertexX[v] = prevX + dx
    vertexY[v] = prevY + dy
    vertexZ[v] = prevZ + dz
    prevX, prevY, prevZ = vertexX[v], vertexY[v], vertexZ[v]
```

### 5.4 Face Property Decoding (lines 1493-1524)

```
For each face f (0..triangles-1):
    faceColor[f] = readUnsignedShort()    // from faceColor section

    if (i_5_ == 1):  // has render info
        renderInfo = readUnsignedByte()

        // Bit 0: face render flag
        if (renderInfo & 1 != 1):
            faceRenderFlags[f] = 0
        else:
            faceRenderFlags[f] = 1    // face has special transparency

        // Bit 1: texture mapping
        if (renderInfo & 2 != 2):
            textureMapping[f] = -1
            faceTexture[f] = -1
        else:
            textureMapping[f] = renderInfo >> 2    // upper 6 bits = tex map index
            faceTexture[f] = faceColor[f]          // color IS the texture ID
            faceColor[f] = 127                      // reset color to neutral

    if (i_6_ == 255):
        faceAlpha[f] = readSignedByte()

    if (i_7_ == 1):
        faceRenderType[f] = readSignedByte()

    if (i_8_ == 1):
        facePriority[f] = readUnsignedByte()
```

**Important legacy difference**: In the legacy format, texture information is packed INTO the render info flag byte. `bit 1` indicates a textured face, and the texture ID comes from the face color value (which is then replaced with 127). This is very different from newer formats where textures have their own dedicated array.

### 5.5 Face Index Decoding (lines 1532-1589)

Uses triangle strip compression with 4 opcodes (see Section 8). Strip types use values 1, 2, 3, 4 (NOT -2, -3, -4, -5 like newer formats).

### 5.6 Textured Face Decoding (lines 1592-1597)

Very simple - just 3 unsigned shorts per textured face:
```
For each texFace (0..texturedTriangles-1):
    textureFaceType[t] = 0                       // always type 0
    texFaceVertA[t] = readUnsignedShort()
    texFaceVertB[t] = readUnsignedShort()
    texFaceVertC[t] = readUnsignedShort()
```

### 5.7 Post-Processing (lines 1598-1623)

After decoding, the legacy format runs texture mapping optimization:
- For each face with a texture mapping, check if the textured face vertices match the face vertices
- If they match exactly, clear the mapping (set to -1) since the texture maps directly
- If no face actually needs texture remapping, discard the entire `textureMapping` array
- Similarly, if no face had the render flag bit set, discard `faceRenderFlags`

---

## 6. Newer Decoder: decoder_newer_format()

**Location**: Model.java:381-807
**Trigger**: Last 2 bytes == 0xFF 0xFF (and model ID not in special range)
**Footer size**: 23 bytes (read from `length - 23`)

### 6.1 Footer Layout

```
Offset  Size   Field        Description
──────  ────   ─────        ───────────
0       2      vertices     Vertex count
2       2      triangles    Face count
4       1      texFaces     Textured triangle count
5       1      flags        8-bit flags:
                               bit 0 (0x01): j - has faceRenderFlags
                               bit 1 (0x02): k - has model particles
                               bit 2 (0x04): m - has model bonds
                               bit 3 (0x08): n - has formatType embedded
                               (bits 4-7: unused in this decoder)
6       1      i_62_        Alpha: 255 = per-face; else = global
7       1      i_63_        Render type flag (1 = has faceRenderType)
8       1      i_64_        Priority flag (1 = has facePriority)
9       1      i_65_        Texture flag (1 = has faceTexture/textureMapping)
10      1      i_66_        Bone weight flag (1 = has vertexBoneWeight)
11      2      i_67_        X-delta data size
13      2      i_68_        Y-delta data size
15      2      i_69_        Z-delta data size
17      2      i_70_        Face index data size
19      2      i_71_        Extra data size (texture layer alpha)
```

**formatType handling** (line 401-405): If flag bit 3 is set, the `formatType` is embedded 7 bytes before the current footer position. The decoder backs up, reads 1 byte, then skips forward 6 bytes to resume.

### 6.2 Data Section Layout

This format uses 7 parallel RSBuffer readers all pointing to the same byte array. Each reader's caret is positioned at a calculated offset:

```
Section                      Reader   Start Offset    Contents
───────                      ──────   ────────────    ────────
textureFaceType[texFaces]    RSBuf1   0               1 signed byte per textured face
vertexFlags[vertices]        RSBuf1   texFaces        1 byte per vertex
faceRenderFlags[tri]         RSBuf2   (conditional)   1 byte per face (if j flag)
faceStripType[tri]           RSBuf2   (after above)   1 byte per face
faceAlpha[tri]               RSBuf3   (conditional)   1 byte per face (if i_62_==255)
facePriority[tri]            RSBuf5   (conditional)   1 byte per face (if i_64_==1)
vertexBones[vertices]        RSBuf5   (conditional)   1 byte per vertex (if i_66_==1)
faceRenderType[tri]          RSBuf4   (conditional)   1 byte per face (if i_63_==1)
faceIndices[i_70_ bytes]     RSBuf1   (calculated)    Smart-encoded vertex indices
faceTexture[tri*2]           RSBuf6   (conditional)   2 bytes per face (if i_65_==1)
texLayerAlpha[i_71_ bytes]   RSBuf7   (conditional)   1 byte per face (if textures)
faceColor[tri*2]             RSBuf1   (calculated)    2 bytes per face
xDeltas[i_67_ bytes]         RSBuf2   (calculated)    Smart-encoded X deltas
yDeltas[i_68_ bytes]         RSBuf3   (calculated)    Smart-encoded Y deltas
zDeltas[i_69_ bytes]         RSBuf4   (calculated)    Smart-encoded Z deltas
texType0Verts[i_72_*6]       RSBuf1   (calculated)    3x ushort per type-0 tex face
texType1-3Verts[i_73_*6]     RSBuf2   (calculated)    3x ushort per type 1/2/3 tex face
texCoords[i_73_*varies]      RSBuf3   (calculated)    UV coords (size depends on formatType)
texAlphas[i_73_]             RSBuf4   (calculated)    1 byte per type 1/2/3 tex face
texColors[i_73_]             RSBuf5   (calculated)    1 byte per type 1/2/3 tex face
texScales[i_73_+i_74_*2]     RSBuf6   (calculated)    1-3 bytes per (layer index + scale)
```

### 6.3 Texture Face Type Counting (lines 419-434)

Before laying out data, the decoder counts how many textured faces of each type exist:
```
i_72_ = count of type 0 textured faces
i_73_ = count of type 1, 2, or 3 textured faces
i_74_ = count of type 2 textured faces (subset of i_73_)
```

This determines the exact byte sizes of the texture data sections.

### 6.4 Vertex Decoding

Identical to legacy format - same delta encoding with `method1239()`.

### 6.5 Face Property Decoding (lines 583-607)

Each property read from its own separate RSBuffer stream:
```
For each face f:
    faceColor[f]      = RSBuf1.readUnsignedShort()
    if j:  faceRenderFlags[f] = RSBuf2.readSignedByte()
    if i_62_==255: faceAlpha[f] = RSBuf3.readSignedByte()
    if i_63_==1:   faceRenderType[f] = RSBuf4.readSignedByte()
    if i_64_==1:   facePriority[f] = RSBuf5.readUnsignedByte()
    if i_65_==1:   faceTexture[f] = RSBuf6.readUnsignedShort() - 1   // -1 = no texture
    if texMapping exists:
        if faceTexture[f] == -1:
            textureMapping[f] = -1
        else:
            textureMapping[f] = RSBuf7.readUnsignedByte() - 1
```

**Key difference from legacy**: Textures are in their OWN array rather than packed into the render info flag. The face texture ID is separate from the face color.

### 6.6 Face Index Decoding (lines 615-672)

Uses triangle strip with opcodes -2, -3, -4, -5 (= 1, 2, 3, 4 in unsigned). See Section 8.

### 6.7 Textured Face Decoding (lines 680-752)

Complex type-based system with 4 textured face types (0-3). See Section 10.

### 6.8 Particles & Bonds (lines 754-800)

If flag bit 1 (`k`): Read model particles.
If flag bit 2 (`m`): Read model bonds.
See Section 11.

---

## 7. Newest Decoder: decoder_newest_format()

**Location**: Model.java:809-1344
**Trigger**: model ID in [63607, 63613] (sets `newProtocol=true`)
**Header size**: 3 bytes (when `newProtocol`)
**Footer size**: 26 bytes (when `newProtocol`), otherwise 23 bytes

### 7.1 Header (newProtocol only)

```
Offset  Size   Field
──────  ────   ─────
0       1      type marker (must be 1, else RuntimeException)
1       1      reserved (unused)
2       1      formatType (12-16)
```

### 7.2 Footer Layout (from `length - 26` when newProtocol)

```
Offset  Size   Field        Description
──────  ────   ─────        ───────────
0       2      vertices     Vertex count
2       2      triangles    Face count
4       2      texFaces     Textured face count (UNSIGNED SHORT in newProtocol vs BYTE in newer)
6       1      flags        8-bit flags:
                               bit 0 (0x01): j - has faceRenderFlags
                               bit 1 (0x02): k - has particles
                               bit 2 (0x04): m - has bonds
                               bit 3 (0x08): n - has embedded formatType
                               bit 4 (0x10): i1 - extended vertex bone format
                               bit 5 (0x20): i2 - extended face priority format
                               bit 6 (0x40): i3 - extended bond format
                               bit 7 (0x80): i4 - has extra bone/animation data
7       1      i5           Alpha: 255 = per-face; else = global
8       1      i6           Render type flag (1 = has faceRenderType)
9       1      i7           Priority flag (1 = has facePriority)
10      1      i8           Texture flag (1 = has faceTexture/textureMapping)
11      1      i9           Bone weight flag (1 = has vertexBoneWeight)
12      2      i10          X-delta data size
14      2      i11          Y-delta data size
16      2      i12          Z-delta data size
18      2      i13          Face index data size
20      2      i14          Extra data size
```

**When newProtocol, additional footer fields**:
```
22      2      i15          Vertex color/bone count
24      2      i16          Triangle priority/type count
```

**When NOT newProtocol** (lines 878-891):
```
22      2      i15          (only if bit 4 set, else derived from i9)
24      2      i16          (only if bit 5 set, else derived from i7)
```

### 7.3 Key Differences from Newer Format

| Feature | Newer Format | Newest Format (newProtocol) |
|---------|-------------|---------------------------|
| Header | None | 3 bytes (type, reserved, formatType) |
| Footer | 23 bytes | 26 bytes |
| texFaces in footer | UnsignedByte (max 255) | UnsignedShort (max 65535) |
| Data starts at | Offset 0 | Offset 3 |
| Flags bits 4-7 | Unused | i1, i2, i3, i4 active |
| Vertex bone read | `readUnsignedByte()` | `readSmart2()` (if i1 set) |
| Face priority read | `readUnsignedByte()` | `readSmart2()` (if i2 set) |
| Bond data read | `readUnsignedByte()` | `readSmart2()` (if i3 set) |
| Strip type masking | Raw byte value | `byte & 0x7` (lower 3 bits only) |
| Extra bone data (i4) | N/A | Has `anInt1413` + bone block at end |
| Tex layer alpha | `readUnsignedByte() - 1` | `readSmart() - 1` if formatType>=16 |

### 7.4 Extended Bone Data (i4 flag, bit 7)

When the i4 flag (0x80) is set (lines 976-988):
```
// Read from just before the 26-byte footer
localOCI8.caret = length - 26
localOCI8.caret -= buffer[caret - 1]    // dynamic offset stored as last byte before footer
anInt1413 = readUnsignedShort()          // extra bone count
i48 = readUnsignedShort()               // bone data block 1 size
i49 = readUnsignedShort()               // bone data block 2 size

// Data sections after main data:
i43 = mainDataEnd + i48                  // extra bone block 1
i44 = i43 + i49                          // extra bone block 2
i45 = i44 + vertices                     // per-vertex bone index
i46 = i45 + anInt1413 * 2               // bone pair table
```

### 7.5 Face Index Decoding with Extra Bones

When `anInt1413 > 0` and the strip type byte has bit 3 set (`i55 & 0x8`), three extra bytes are read per face from the bone stream (lines 1193-1203). These appear to be per-face bone attachment data (partially commented out in the source).

---

## 8. Triangle Strip Compression

All three decoders use triangle strip compression to encode face vertex indices compactly. Instead of storing 3 vertex indices per face, most faces reuse vertices from the previous face.

### Encoding

Each face has a strip opcode byte. The vertex indices are stored as **smart-encoded deltas** from a running "last used" index counter.

### Opcodes

**Legacy format** uses positive values 1, 2, 3, 4:

| Opcode | Name | Action |
|--------|------|--------|
| 1 | New Triangle | A = last + smart(), B = A + smart(), C = B + smart() |
| 2 | Continue Strip | B = C (prev), C = last + smart(). A unchanged. |
| 3 | Reorder | A = C (prev), C = last + smart(). B unchanged. |
| 4 | Swap & Extend | swap(A, B), C = last + smart() |

**Newer/Newest formats** use values 1, 2, 3, 4 stored as unsigned bytes, checked as `(val ^ 0xFFFFFFFF) == -2` etc., which means they're comparing against 1, 2, 3, 4:

| Check | Value | Action |
|-------|-------|--------|
| `== -2` (XOR) | 1 | New Triangle: all 3 vertices read fresh |
| `== -3` (XOR) | 2 | Continue: B=C(prev), C=new |
| `== -4` (XOR) | 3 | Reorder: A=C(prev), C=new |
| `== -5` (XOR) | 4 | Swap+Extend: swap(A,B), C=new |

**Newest format with newProtocol**: The opcode byte is masked with `& 0x7` (lower 3 bits only), because bit 3 is used for extra bone data.

### Example

```
Face 0: opcode=1, deltas=[0, 3, 2]
    A = 0+0 = 0
    B = 0+3 = 3
    C = 3+2 = 5
    → Triangle(0, 3, 5)   lastIndex=5

Face 1: opcode=2, deltas=[1]
    B = C(prev) = 5
    C = 5+1 = 6
    A unchanged = 0
    → Triangle(0, 5, 6)   lastIndex=6

Face 2: opcode=3, deltas=[-2]
    A = C(prev) = 6
    C = 6+(-2) = 4
    B unchanged = 5
    → Triangle(6, 5, 4)   lastIndex=4
```

The `anInt1406` (maxVertexIndex) tracks the highest vertex index encountered, then is incremented by 1 at the end.

---

## 9. Smart Encoding

### method1239() - Vertex Delta Smart

Used to encode vertex coordinate deltas. Provides compact 1-byte encoding for small deltas, 2-byte for larger:

```java
int peek = buffer[caret] & 0xFF;
if (peek < 128) {
    return readUnsignedByte() - 64;      // 1 byte: range [-64, 63]
} else {
    return readUnsignedShort() - 49152;  // 2 bytes: range [-49152, 16383]
}
```

**Range**: -64 to 63 (1 byte) or -49152 to 16383 (2 bytes)
**Bias**: 64 for single byte, 49152 (0xC000) for double byte

### readSmart() - General Smart

```java
int peek = buffer[caret] & 0xFF;
if (peek < 128) {
    return readUnsignedByte();           // 1 byte: range [0, 127]
} else {
    return readUnsignedShort() - 32768;  // 2 bytes: range [0, 32767]
}
```

### readSmart2() - Nullable Smart

Like readSmart but offset by -1 to allow -1 as a sentinel value:
```java
int peek = buffer[caret] & 0xFF;
if (peek < 128) {
    return readUnsignedByte() - 1;       // 1 byte: range [-1, 126]
} else {
    return readUnsignedShort() - 32769;  // 2 bytes: range [-1, 32766]
}
```

---

## 10. Textured Triangle Types

The newer and newest formats support 4 types of texture face mappings. The type is stored in `aByteArray1388[]`.

### Type 0 (Simple Projection)

Just 3 vertex indices - the texture is projected from the triangle defined by these vertices:
```
texFaceVertA = readUnsignedShort()
texFaceVertB = readUnsignedShort()
texFaceVertC = readUnsignedShort()
```

### Type 1 (UV Mapped)

Full UV-mapped texture face:
```
texFaceVertA = readUnsignedShort()
texFaceVertB = readUnsignedShort()
texFaceVertC = readUnsignedShort()

if formatType < 15:
    texCoordU = readUnsignedShort()               // 16-bit U
    if formatType >= 14:
        texCoordV = method1186()                   // 24-bit V
    else:
        texCoordV = readUnsignedShort()            // 16-bit V
    texCoordW = readUnsignedShort()                // 16-bit W
else:  // formatType >= 15
    texCoordU = method1186()                       // 24-bit U
    texCoordV = method1186()                       // 24-bit V
    texCoordW = method1186()                       // 24-bit W

texAlpha = readSignedByte()
texColor = readSignedByte()
texLayerIndex = readSignedByte()
```

### Type 2 (Multi-layer UV)

Same as Type 1 but with additional scale data:
```
(same vertex and UV reads as Type 1)
texAlpha = readSignedByte()
texColor = readSignedByte()
texLayerIndex = readSignedByte()
texScaleU = readSignedByte()                       // extra scale for type 2
texScaleV = readSignedByte()                       // extra scale for type 2
```

### Type 3 (Alternative UV)

Same as Type 1 (no extra scale data):
```
(same vertex and UV reads as Type 1)
texAlpha = readSignedByte()
texColor = readSignedByte()
texLayerIndex = readSignedByte()
```

### UV Coordinate Size Rules

| formatType | U size | V size | W size |
|-----------|--------|--------|--------|
| < 14 | 16-bit | 16-bit | 16-bit |
| 14 | 16-bit | 24-bit | 16-bit |
| >= 15 | 24-bit | 24-bit | 24-bit |

---

## 11. Model Particles & Bonds

### ModelParticle (Class87)

Attached to specific faces of a model. Decoded when flag bit 1 (`k`) is set.

**Decoding** (lines 754-783 / 1286-1316):
```
particleCount = readUnsignedByte()
for each particle:
    particleId = readUnsignedShort()        // particle effect ID
    if particleId == toOverrideParticleId:
        particleId = customParticleId       // substitution
    faceIndex = readUnsignedShort()         // which face this particle is attached to
    alpha = (globalAlpha if not per-face) else faceAlpha[faceIndex]

    modelParticle = new ModelParticle(
        particleId,
        faceVertexA[faceIndex],
        faceVertexB[faceIndex],
        faceVertexC[faceIndex],
        alpha
    )

emitterCount = readUnsignedByte()
for each emitter:
    emitterId = readUnsignedShort()
    vertexIndex = readUnsignedShort()
    attachPoint = new Class35(emitterId, vertexIndex)
```

### Class106 - Model Bonds

Decoded when flag bit 2 (`m`) is set.

```
bondCount = readUnsignedByte()
for each bond:
    field1 = readUnsignedShort()           // bond ID (anInt905)
    field2 = readUnsignedShort()           // face index (anInt906)

    // In newest format with i3 flag:
    field3 = readSmart2()                  // bone group (anInt908)
    // Otherwise:
    field3 = readUnsignedByte()            // (255 → -1)

    field4 = readSignedByte()              // modifier (anInt907)

    bond = new Class106(field1, field2, field3, field4)
```

---

## 12. Data Layout Diagrams

### Legacy Format
```
┌──────────────────────────────────────────────────────────┐
│                    MODEL DATA BLOB                       │
├──────────┬───────────┬──────────┬───────────┬────────────┤
│ Vertex   │ Face      │ Alpha    │ Priority  │ Render     │
│ Flags    │ StripType │ (opt)    │ (opt)     │ Flags(opt) │
│ [V]      │ [T]       │ [T]      │ [T]       │ [T]        │
├──────────┼───────────┼──────────┼───────────┼────────────┤
│ Vertex   │ Render    │ Face     │ Face      │ Tex Face   │
│ Bones    │ Type(opt) │ Indices  │ Colors    │ Verts      │
│ [V](opt) │ [T]       │ [smart]  │ [T*2]     │ [tex*6]    │
├──────────┼───────────┼──────────┴───────────┴────────────┤
│ X Deltas │ Y Deltas  │ Z Deltas                          │
│ [smart]  │ [smart]   │ [smart]                           │
├──────────┴───────────┴───────────────────────────────────┤
│                    FOOTER (18 bytes)                     │
│ V(2) T(2) tex(1) flags(5x1) sizes(4x2)                 │
└──────────────────────────────────────────────────────────┘
```

### Newer Format
```
┌──────────────────────────────────────────────────────────┐
│                    MODEL DATA BLOB                       │
├──────────┬───────────┬──────────┬───────────┬────────────┤
│ TexFace  │ Vertex    │ Render   │ StripType │ Alpha      │
│ Types    │ Flags     │ Flags    │           │ (opt)      │
│ [tex]    │ [V]       │ [T](opt) │ [T]       │ [T]        │
├──────────┼───────────┼──────────┼───────────┼────────────┤
│ Priority │ Vertex    │ Render   │ Face      │ Texture    │
│ (opt)    │ Bones     │ Type     │ Indices   │ IDs        │
│ [T]      │ [V](opt)  │ [T](opt) │ [smart]   │ [T*2](opt) │
├──────────┼───────────┼──────────┼───────────┼────────────┤
│ TexLayer │ Face      │ X Deltas │ Y Deltas  │ Z Deltas   │
│ Alpha    │ Colors    │          │           │            │
│ [T](opt) │ [T*2]     │ [smart]  │ [smart]   │ [smart]    │
├──────────┼───────────┼──────────┼───────────┼────────────┤
│ Type0 Tex│ Type1-3   │ Tex UV   │ Tex       │ Tex Color  │
│ Verts    │ Tex Verts │ Coords   │ Alpha     │            │
│ [t0*6]   │ [t123*6]  │ [varies] │ [t123]    │ [t123]     │
├──────────┼───────────┼──────────┴───────────┴────────────┤
│ Tex Scale│ (Particles + Bonds data appended)             │
│ [varies] │                                               │
├──────────┴───────────────────────────────────────────────┤
│                    FOOTER (23 bytes)                     │
│ V(2) T(2) tex(1) flags(1) config(5x1) sizes(5x2) 0xFFFF│
└──────────────────────────────────────────────────────────┘
```

### Newest Format (newProtocol)
```
┌──────────────────────────────────────────────────────────┐
│ HEADER (3 bytes): type(1) reserved(1) formatType(1)     │
├──────────────────────────────────────────────────────────┤
│                    MODEL DATA BLOB                       │
│                (Same as Newer format,                    │
│                 but offsets start at 3                    │
│                 and tex count is UShort)                  │
├──────────────────────────────────────────────────────────┤
│ (Extended bone data block, if i4 flag set)               │
├──────────────────────────────────────────────────────────┤
│                    FOOTER (26 bytes)                     │
│ V(2) T(2) tex(2!) flags(1) config(5x1)                  │
│ sizes(5x2) vertColors(2) triPriorities(2)               │
└──────────────────────────────────────────────────────────┘
```

---

## 13. Comparison of All Three Formats

| Feature | Legacy (method2587) | Newer (decoder_newer_format) | Newest (decoder_newest_format) |
|---------|-------------------|---------------------------|------------------------------|
| **Detection** | No 0xFFFF marker | Last 2 bytes = 0xFFFF | Model ID 63607-63613 |
| **Footer size** | 18 bytes | 23 bytes | 26 bytes |
| **Header** | None | None | 3 bytes |
| **texFaces storage** | UByte (max 255) | UByte (max 255) | UShort (max 65535) |
| **Flag bits used** | 0-3 only | 0-3 | 0-7 (all 8 bits) |
| **Texture storage** | Packed in render info | Dedicated arrays | Dedicated arrays |
| **Tex face types** | Type 0 only | Types 0-3 | Types 0-3 |
| **UV precision** | N/A | 16/24-bit (formatType) | 16/24-bit (formatType) |
| **Bone encoding** | UByte only | UByte only | UByte or readSmart2() |
| **Priority encoding** | UByte only | UByte only | UByte or readSmart2() |
| **Strip opcodes** | 1, 2, 3, 4 | 1, 2, 3, 4 | (byte & 0x7): 1-4, bit 3=bone flag |
| **Extra bone data** | No | No | Yes (i4 flag) |
| **formatType** | Always 12 | Optional (flag bit 3) | In header or flag bit 3 |
| **Post-processing** | Tex mapping optimization | None | None |
| **Particle support** | No | Yes (flag bit 1) | Yes (flag bit 1) |
| **Bond support** | No | Yes (flag bit 2) | Yes (flag bit 2) |

---

## 14. Supporting Classes

### Class35 - Emitter Attachment Point
```java
// Fields:
int anInt327;    // emitter effect ID
int anInt329;    // vertex index attachment point
```

### Class106 - Model Bond
```java
// Fields:
int anInt905;    // bond ID
int anInt906;    // face/triangle index
int anInt908;    // bone group (-1 = none)
int anInt907;    // modifier byte
```

### ModelParticle - Particle Effect
```java
// Key fields:
int anInt666;    // particle vertex A
int anInt661;    // particle vertex B
int anInt674;    // particle vertex C
byte aByte658;   // alpha
int anInt659;    // particle effect ID
```

---

## Files in This Dump

| File | Original Path | Description |
|------|--------------|-------------|
| `Model.java` | `src/Model.java` | Main model class with all 3 decoders |
| `RSBuffer.java` | `src/RSBuffer.java` | Binary stream reader |
| `Node_Sub6.java` | `src/Node_Sub6.java` | Model loader (method981) |
| `JS5Archive.java` | `src/JS5Archive.java` | Cache archive interface |
| `ModelParticle.java` | `src/ModelParticle.java` | Particle effect data |
| `Class106.java` | `src/Class106.java` | Model bond data |
| `Class35.java` | `src/Class35.java` | Emitter attachment data |
