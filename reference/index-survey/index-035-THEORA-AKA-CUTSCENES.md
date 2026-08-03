# Index 35 - THEORA_AKA_CUTSCENES (name wrong; index unused)

**Format:** empty-in-this-cache  
**Capability:** not-applicable  
**Effort:** trivial

## What it is

Nothing. Index 35 is empty in this 639 cache and referenced nowhere by the 637 client: no group, no file, no record, no format. CACHE: cache/main_file_cache.idx35 is exactly 1 byte, value 0xFF - an entry is 6 bytes, so 0 groups, and it does not hold even one whole entry (idx34 and idx36 are the identical 1-byte 0xFF placeholder). Decoding all 37 six-byte records of cache/main_file_cache.idx255 by hand, record 35 is length=0 sector=0: no reference table at all, not even the four-byte format-5 zero-group stub index 36 has (record 34 is also 0/0; record 36 is length 9 at sector 92526). CLIENT: Class100.java:8 sizes aFileStoreArray844 at 37, so a slot exists. Class42_Sub3.java:34-52 openFileStore is the sole writer of that array (:43); an exhaustive grep of its call sites gives InterfaceSettings.java:73-74 (32 or 34), :75 (33), :76 (13), :157-188 (8,0,1,2,3,4,5,6,7,9,10,11,12,14-31,36) and a recursive self-call with 8 at Class42_Sub3.java:47 - that is 0-34 and 36. 35 is passed nowhere, so aFileStoreArray844[35] is permanently null; the null check at InterfaceSettings.java:195 is what keeps the progress loop alive. Node_Sub10_Sub10.java:8-9, the 37-entry loading-weight table, gives slot 35 weight 0. The name is misattributed: Ogg/Theora is index 36, the only index opened with fileType=2 (InterfaceSettings.java:188), also empty here. Matches AGENTS.md:309-310 and :327-331.

## Current capability

Nothing beyond generic plumbing, and nothing is warranted. The only occurrences of the index in the whole repo are FlashEditor/Cache/RSConstants.cs:50 (the constant) and :100 (the display string in indexNames), consumed only by RSConstants.GetIndexName (:111-120) for debug logging - grep for THEORA_AKA_CUTSCENES across the tree returns those two plus AGENTS.md:309. No decoder, no encoder, no codec test, no sweep, no GUI tab (the tab list is FlashEditor/Editor.Designer.cs:248-257: Console, Item, Sprite, NPC, Object, Interface, ModelViewer, TextureViewer, MapEditor, TrackEditor). FlashEditor/Cache/RSFileStore.cs:33-37 does open idx35 because the file exists, and GetFileCount (:49-54) returns 1/6 = 0 groups for it. The absence of a table is pinned indirectly by FlashEditor.Tests/Cache/RealCache/RealCacheFixture.cs:55-64, which walks all 37 meta records and skips the ones whose raw container is null, and by RealCacheReferenceTableShapeTests.cs:152 asserting exactly 35 tables - the two missing ones are 34 and 35, as its comment at :147-149 says.

## Gaps

- Nothing. There are zero bytes to decode, so decoder, encoder, byte-identity sweep and GUI tab are all vacuous. Any of them would be untestable against this cache and would assert a format nobody has seen.

## Notes and traps

Traps for anyone touching this tail of the index map. (1) The name is a lie in two directions - 35 is not Theora, 36 is; do not build a video decoder here. Renaming the constant is the only defensible edit, and it is cosmetic. (2) RSFileStore.GetIndexCount (FlashEditor/Cache/RSFileStore.cs:64-79) returns the MAX non-meta index present, not a count, and RSCache uses it as a count: RSCache.cs:544 sizes referenceTables[36] and :547 loops indexId < 36, so it does attempt index 35 and never reaches index 36. For 35 that is harmless - GetContainer(255,35) hits the zero-length record, DecodeContainer yields null, and RSCache.cs:426 throws FileNotFoundException which LoadReferenceTables swallows at :551-553 - but the same off-by-one silently locks index 36 out of GetReferenceTable via the bound at :565. Fix that only with a test, since correcting it makes 36's four-byte stub reachable for the first time. (3) The 1-byte 0xFF idx35 file is not a valid 6-byte-aligned index; any parser that assumes filesize % 6 == 0 will be wrong about it, and the current integer divide happens to give the right answer by luck. (4) Whether some other 639-era cache populates index 35 is unknown - do not infer from this one. No XTEA, no compression question, no cross-index dependency: there is no data.
