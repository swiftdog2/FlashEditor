# Index 255 - META (reference tables / "CRCTABLE")

**Format:** fully-understood  
**Capability:** complete  
**Effort:** small

## What it is

Index 255 holds the reference table for every other index: group N of idx255 is the metadata for index N. Established from the client, not inferred: `FileStore.method3784` (FileStore.java:129, :152, :178) fetches its table with `submitRequest(255, (byte) 0, containerID, true)` where `containerID` is the index id set at `FileStore.java:72`, and the on-disk half of that request is served from `new Class17(255, dat2, idx255, 500000)` (client.java:3146-3147), which `Class42_Sub3.openFileStore:43` hands to every one of the 37 `openFileStore` calls at InterfaceSettings.java:157-188. The payload is parsed by `VersionTable.method3622` (VersionTable.java:105-215).

A group is one whole reference table. There is no archive/file split inside it: `VersionTable` is handed the stored container bytes and calls the container decoder directly (`Node_Sub46_Sub10.method1571`, VersionTable.java:109), so a group holds exactly one file and idx255 has no reference table of its own describing file ids. One record therefore = one index's complete metadata: format byte, table version (format >= 6 only), a flags byte, group count, delta-encoded group ids, then per group a name hash (flag 0x01), a CRC32, a 64-byte whirlpool digest (flag 0x02), a version, a file count, delta-encoded file ids, and finally per-file name hashes (0x01 again).

Two structural facts about THIS cache. (1) idx255 is 222 bytes = 37 records, and 35 of them hold a table; slots 34 and 35 are empty, pinned by `RealCacheReferenceTableShapeTests.cs:152`. (2) Group 255 - the JS5 master index / checksum table that carries the CRC, version and whirlpool the client validates each table against (`Class109.isAvailable:129-135` reads a count byte then 72 bytes per index; `Class109.method1738:222-231` seeks `72*i + 6` and reads CRC int, version int, 64-byte digest) - does NOT exist on disk. It is network-served only, which is why idx255 stops at 37 records.

## Current capability

Decoder: `ReferenceTableCodec.Decode` (FlashEditor/Cache/ReferenceTableCodec.cs:19-155). Field order matches `VersionTable.method3622` block for block - format, version, flags, count, delta ids, identifiers, CRCs, whirlpool, versions, file counts, delta file ids, per-file identifiers.

Encoder: `ReferenceTableCodec.Encode` (:160-313), and it is live on the write path - `RSCache.WriteReferenceTable` (Cache/RSCache.cs:315-322) bumps `table.version`, re-encodes, wraps in a GZip container and writes it to `store.Write(META_INDEX, indexId, ...)` after every edit, or once per batch (`RSCache.cs:293-296`, `BeginBatch` at :337).

Full-index byte-identity sweep: `RealCacheConformanceTests.ReferenceTables_ReEncodeToTheCapturedBytes` (FlashEditor.Tests/Cache/RealCacheConformanceTests.cs:58-104) walks all 35 tables in the real cache and requires each to re-encode to the captured payload byte for byte, tolerating only trailing zero padding.

Independent shape proof: `RealCacheReferenceTableShapeTests` (Tests/Cache/RealCacheReferenceTableShapeTests.cs:67, :131, :185) requires three figures to agree per table - a field-by-field length computed from the format (`DocumentedTableLength`, :285), where `Decode` leaves the stream, and what `Encode` writes.

Captured-bytes fixture (not this codec's own output): `CapturedCacheBytesTests.ReferenceTable_FromCapturedBytes_ReEncodesToTheSameBytes` (:75-87) plus the container shape at :103-116.

Write-path tests against a synthetic cache: `RSCacheWriteFileTests` builds a table into idx255 (:100) and asserts the entry, file-id list and FLAG_SIZES pair survive save-and-reopen (:157-422).

GUI: the META tab is the main tab - `Editor.cs:471-565` maps the main menu to `META_INDEX` and populates `RefTableListView` with every decoded table (format, version, archive count, and the four flag bits as checkboxes: Designer.cs:300-375) and `ContainerListView` with the containers loaded so far. Display only - no `CellEditActivation` is set on either (only `ItemListView`, Designer.cs:593), and `RSCache.WriteFile` throws outright for META_INDEX (RSCache.cs:103-104). The table is maintained automatically by the write path instead, which is the correct design: a hand-edited reference table desynchronises from the archives it describes.

## Gaps

- Trailing padding is dropped on save. Encode does not reproduce the four-zero-bytes-per-file block that indexes 9, 26, 27 and 29 carry, so writing any of those four shortens the table by 3784, 4, 1684 and 728 bytes. Harmless (identifiers flag is clear, nothing reads it, the 637 client never looks) but it means those four indexes are byte-identical only under the sweep's zero-padding tolerance, not absolutely. Closing it needs the surplus captured at decode onto RSReferenceTable and re-emitted.
- No checksum table (JS5 master index, 255/255). Nothing in the project generates the 6 + 72*N byte structure the client reads at Class109.java:129-135 and :222-231. Not needed to read or edit this cache because it is not on disk, but a JS5 server serving an edited cache has to regenerate it, and the editor cannot produce one. `FlashEditor.Cache.CheckSum` is a namespace with no checksum code in it (only RSIdentifiers.cs:7 declares it).
- No GUI mutation of a table, by design. `RSCache.WriteFile` refuses META_INDEX (RSCache.cs:103-104) and the META tab is a read-only TreeListView. Deliberate, not an omission - but there is also no read-only inspector for a single table's per-group rows (CRC, version, file ids, name hashes); the tab shows one row per table and nothing below it. The commented-out `ChildrenGetter` at Editor.cs:543-552 is the half-built version of that.
- `typeCol.AspectName = "type"` (Designer.cs:325) binds to a property `RSReferenceTable` does not have - the field is `indexId` (RSReferenceTable.cs:57). The leftmost column of the META tab renders empty, so the tab does not say which index each row belongs to.

## Notes and traps

TRAPS, in order of how much they will cost:

1. THE 637 CLIENT REJECTS FORMAT 7. `VersionTable.method3622` opens with `if(i_3_ < 5 || i_3_ > 6) throw new RuntimeException();` (VersionTable.java:112-114). Our codec implements a format-7 branch (the per-archive flags byte, ReferenceTableCodec.cs:112-117 and :267-272) that this client would refuse to parse at all. Keep it - the branch is correct for later builds and a decoder that drops it mis-parses from that field onward - but never *emit* format 7 for this client, and note that the format-7 XTEA flag bit is doubly dead here: no table on disk is format 7 (RealCacheReferenceTableShapeTests.cs:159-160) and the client could not read it if one were.

2. THE SIZES (0x04) AND ENTRY-HASH (0x08) BLOCKS ARE UNPROVEN, NOT JUST DEAD. The 637 client reads only bits 0x01 and 0x02 (VersionTable.java:118, :121). It has no sizes block and no entry-hash block anywhere. No table in this cache sets either flag. So the *position* our decoder gives them - entry hash between the CRCs and the whirlpool (ReferenceTableCodec.cs:72-74), sizes between whirlpool and versions (:98-105) - is corroborated by neither the client nor the data. It is a claim carried over from other revisions. Do not treat a passing sweep as evidence for it, and do not "fix" the ordering without a table that actually sets the bit.

3. THE CRC AND WHIRLPOOL A TABLE IS CHECKED AGAINST LIVE SOMEWHERE ELSE. `new VersionTable(is, anInt5309, aByteArray5319)` (FileStore.java:143) validates the table's CRC against `anInt5309` and its digest against `aByteArray5319`, both of which come from the master index at Class109.java:224-229, i.e. from the network. A re-encoded table has a different CRC (and, for indexes 9/26/27/29, a different length) than any published master index says, so an edited cache served over JS5 needs its master index regenerated. Locally irrelevant; over JS5 it is the whole ballgame.

4. FIELD ORDER IS NON-CANONICAL IN ONE PLACE THE CODEC ALREADY HANDLES. `Encode` writes the group count from `archiveEntries.Count` and file counts from `fileEntries.Count`, not from the stored `validArchivesCount`/count fields, and rebuilds deltas from the actual ids. That is right, and it is why the encoder must never renumber - the comment at ReferenceTableCodec.cs:287-288 records a fixed defect where an ordinal counter renumbered every file to 0..n-1. The client's reader nulls its own id array when count == maxId+1 (VersionTable.java:183-185), so a dense group is indistinguishable to it; a sparse one is not.

5. DUPLICATE GROUP IDS WOULD THROW. Deltas are unsigned shorts, so a delta of 0 is legal on the wire and yields a repeated id. The client tolerates it (it just overwrites). Our decoder does `GetArchiveEntries().Add(...)` (ReferenceTableCodec.cs:46), which throws on a duplicate key. Does not occur in this cache; would be a hard failure on a cache that had one.

6. NO XTEA, EVER. `RSCache.ResolveXTEAKey` returns null for META_INDEX unconditionally (RSCache.cs:952-954), and the client never passes keys on the 255 path. Reference-table containers also carry no version trailer, unlike every archive container - both shapes are pinned at CapturedCacheBytesTests.cs:103-134, and the trailer length must be derived, never assumed to be 2.

7. INDEX 36's TABLE IS A FOUR-BYTE FORMAT-5 STUB declaring zero groups (RealCacheReferenceTableShapeTests.cs:162-164). It once took the whole decode down through `Max()` on an empty sequence; the guard is at ReferenceTableCodec.cs:53-55. Any rewrite of the decoder has to keep it. Format 5 also means no table-version int (VersionTable.java:115-119 mirrors our :27-28).

8. ORDERING ON SAVE. `RSFileStore.SaveTo` promotes idx255 last (RSFileStore.cs:304-309) because it is the pointer to every table; an interrupted save must leave unreferenced sectors rather than records pointing at absent data. Do not reorder that loop.

DEPENDENCIES: every other index depends on this one - `RSCache.GetReferenceTable` (RSCache.cs:564-584) is the entry point for reading anything, and index 5's map path can only address groups through the name hashes this table carries (`RSReferenceTable.GetArchiveId`, :71-76). Index 2 carries no name hashes at all (RealCacheReferenceTableShapeTests.cs:112-113), so nothing may fall back to a name lookup there.

GRADING NOTE: graded complete on decoder + encoder + a whole-index byte-identity sweep (RealCacheConformanceTests.cs:58) + a GUI tab. The fourth leg is a read-only tab plus automatic maintenance on write, not hand-editing, and hand-editing is refused on purpose (RSCache.cs:103-104). If the rubric's "GUI editing" is read strictly as user-mutable fields, downgrade this to read-write-with-tests; the sweep exists either way and can be pointed at.
