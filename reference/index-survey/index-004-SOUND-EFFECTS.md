# Index 4 - SOUND_EFFECTS

**Format:** fully-understood  
**Capability:** none  
**Effort:** medium

## What it is

Procedural sound-effect synthesiser definitions - not sampled audio. Each group is one sound effect id and holds exactly one file (file id 0), so group == file == one record; the client fetches it as `JS5Archive.getChildFromFolder(id, 0)` at `Class37.java:9`, called with a literal 0 at `Class280.java:192-193`, `:258-259` and `Particle_Sub3_Sub2.java:24`. Index 4 is opened at `InterfaceSettings.java:162` into `Class76_Sub2.aJS5Archive_3733`.

A record (`Class37.java:26-39`) is: 10 optional tone slots, then u16 loopStart and u16 loopEnd (milliseconds; `Class37.java:70` converts with `22050 * x / 1000`). A slot is present iff its next byte is non-zero: the client reads the byte, and on non-zero rewinds one and parses a tone, so the marker IS the tone's first field. Output is 8-bit signed mono PCM at 22050 Hz (`Class37.java:67-71`, clamp at `:94-96`).

A tone (`Class344.method3820`, `:78-124`) is: pitch envelope, volume envelope, three optional modulator envelope PAIRS (each gated by the same non-zero-byte marker), up to 10 harmonics (unsigned smart amplitude - break on 0 - then signed smart semitone offset, then unsigned smart delay), unsigned smart delay time, unsigned smart delay feedback, u16 duration, u16 offset, then a filter block.

An envelope (`Class209.method2771/2772`, `:54-71`) is: u8 waveform form (1=square, 2=sine, 3=saw, 4=noise per `Class344.method3821` `:126-144`), i32 start, i32 end, u8 segment count, then per segment u16 duration + u16 value.

The filter (`Class182.method2612`, `:35-67`) is: u8 packing two nibbles (pole counts, high=set 0 / low=set 1); if non-zero then u16+u16 gains, a u8 interpolation mask, then u16+u16 per pole per set, then the same again only for poles whose mask bit `1 << (set*4 + pole)` is set, and finally an extra envelope shape only when `mask != 0 || gain1 != gain0`.

Index 4 has a second consumer: it is handed to the MIDI voice path alongside index 15 (patch bank) and index 14 (Vorbis samples) at `Particle_Sub3_Sub5_Sub2.java:99-100`.

MEASURED IN THIS CACHE (by walking dat2/idx4 directly, read-only): idx4 is 61428 bytes = 10238 records, all non-empty. The reference table (idx255 group 4) is format 6, version 330, flags 0x00, and declares 10237 groups with ids 0..10237 - id 4787 is ABSENT from the table but still has a live 156-byte container in idx4. Every declared group has exactly 1 file, file id 0. The table consumes exactly 143326 of 143326 bytes (no trailing tail). Containers: 10143 GZip, 95 uncompressed, 0 BZip2, every one carrying a 2-byte version trailer; stored 21..1169 bytes (median 186), payload 14..4412 bytes (median 255). No XTEA anywhere - the client opens index 4 with the non-encrypted flag (`InterfaceSettings.java:162` passes `false`, versus `true` for index 5).

## Current capability

Nothing index-4 specific exists. The only two references to it anywhere in the production project are the constant declaration `FlashEditor\Cache\RSConstants.cs:19` (`SOUND_EFFECTS = 4`) and its display string at `FlashEditor\Cache\RSConstants.cs:69`. A repo-wide grep for `SOUND_EFFECTS` returns exactly those two lines plus the index-map table in `AGENTS.md:278`. There is no `SoundEffectDefinition` type, no Decode/Encode, no accessor on `RSCache` (the typed getters run out at `RSCache.cs:760` `GetTrack` / `:837` `GetModelDefinition`), and no test in `FlashEditor.Tests` mentions index 4 or sound.

No GUI. `FlashEditor\Editor.cs:64-76` lists the nine tab indexes (items, sprites, NPCs, objects, interfaces, models, textures, maps, music) and index 4 is not among them; `Editor.Designer.cs:64-148` has no sound tab.

What does work is the generic container layer, which is index-agnostic: `RSCache.ReadFileBytes` (`RSCache.cs:783`) and `RSCache.WriteFile` (`RSCache.cs:102`) will read and stage a raw index-4 group like any other, and `RealCacheConformanceTests` sweeps every index in the meta table (`RealCacheConformanceTests.cs:126,175,229,305,375`), so index 4's containers, CRCs, file split and idx records are proven at the byte level - as OPAQUE bytes. Nothing understands or can edit their contents.

## Gaps

- A `SoundEffectDefinition` type under `FlashEditor\Definitions\` with `Decode(JagStream)`/`Encode()`, plus nested `Tone`, `Envelope` and `Filter` types. Port from `Class37.java:26-39`, `Class344.java:78-124`, `Class209.java:54-71`, `Class182.java:35-67`. The two smart readers are `RSBuffer.readSmart` (`RSBuffer.java:857-868`: <128 -> u8, else u16-32768) and `RSBuffer.method1239` (`:606-612`: <128 -> u8-64, else u16-49152).
- A `RSCache.GetSoundEffect(int id)` accessor beside `GetTrack` (`RSCache.cs:760`), reading file 0 of the group.
- A codec test against captured bytes in `FlashEditor.Tests\Fixtures\RealCache\` - a round trip through our own encoder proves nothing (CLAUDE.md).
- A full-index byte-identity sweep over all 10238 groups, comparing the DECOMPRESSED payload (a GZip re-encode is never byte-identical). I have already established the target: a client-faithful reader consumes 10238 of 10238 payloads to the exact byte, so an exact-consumption plus byte-identity sweep is achievable at 100%.
- A `SoundEffectEditorTab` following the `TrackEditorTab` pattern (`Editor.Designer.cs:148`, `Editor.cs:64-76` `editorTypes`, `Definitions\Tracks\TrackEditorPanel.cs`), with the id list, per-tone field editing and save-back.
- Optional but the only way to audition an edit: a port of the synthesiser `Class344.method3822` (`:146-315`) and the mixdown `Class37.method345` (`:73-102`) to produce 22050 Hz 8-bit PCM. It is ~190 lines of exact fixed-point integer DSP including a biquad cascade, and it is the single most expensive piece of the job.

## Notes and traps

TRAPS, all measured or cited:

1. ORPHAN GROUP. idx4 holds 10238 live records; the reference table declares 10237 and omits id 4787, whose 156-byte container is still present and parses cleanly. A sweep enumerated from the table sees 10237, one enumerated from idx4 sees 10238. Pick one and say which - the mismatch will otherwise read as a decoder bug. (AGENTS.md:278 records 10237/10237, which is the table-side reading.)

2. THE FORMAT IS CANONICAL - a rarity in this cache. I audited every smart in every group: 125,592 unsigned smarts and 31,311 signed smarts, and ZERO use the wide 2-byte form for a value that fits the 1-byte form. So `readSmart` can be re-encoded by value with no "record which encoding you saw" machinery. Do not import the non-canonical apparatus from the map/floor path here; it is not needed.

3. ZERO-BYTE PRESENCE MARKERS. Slot presence is signalled by peeking a byte: zero means absent and is consumed, non-zero means present and is rewound to become the envelope's form byte (`Class37.java:30-35`, `Class344.java:84-107`). An encoder that ever writes a form byte of 0 for a present envelope makes the record unreadable. Two effects in this cache have zero tones (14-byte payloads: ten zero bytes plus the two u16s), so a decoder that assumes at least one tone breaks on real data.

4. CONDITION-COUPLED OPTIONAL FIELD. The filter's trailing envelope shape is written only when `mask != 0 || gain1 != gain0` (`Class182.java:61-63`). Editing the gains to be equal while the mask is zero silently drops it, changing the length.

5. CLIENT BUG, LATENT. `Class344.java:71-74` allocates the three harmonic arrays at length 5, while the decode loop at `:108-116` writes up to 10 slots - a tone with 6+ harmonics throws ArrayIndexOutOfBounds in the 637 client. The synthesiser only reads the first 5 anyway (`:173`, `:198`). Measured maximum in this cache: 5. So it never fires today, and the editor must not let anyone write a 6th. Same shape in `Class182.java:25-27`, whose `[2][2][4]` arrays cannot hold the up-to-15 poles the nibbles can express; measured maximum is 4 in both sets.

6. NO NAMES. Table flags are 0x00 - no identifiers, no whirlpool, no sizes, no entry hashes. Sound effects are addressable by id only, exactly like index 2. No name-recovery feature is possible from this cache.

7. NO XTEA, NO BZIP2. 10143 GZip, 95 uncompressed, and all 10238 carry a 2-byte version trailer. Compare decompressed payloads, never containers.

8. INBOUND DEPENDENCIES. `ObjectDefinition.ambientSoundId` (`Definitions\ObjectDefinition.cs:294,633,639`, written back at `:993,999`) and the NPC sound opcodes hold index-4 ids. Any renumbering breaks them, and nothing currently validates that an id resolves.

9. NO 637-TO-639 DRIFT ON THIS INDEX. A reader ported straight from the 637 client consumes 10238 of 10238 payloads with zero bytes left over and zero overruns, so the format did not move between the builds. Distribution for sanity-checking an implementation: tones per effect 0:2, 1:4967, 2:2789, 3:1213, 4:583, 5:270, 6:161, 7:71, 8:61, 9:37, 10:84 (20,990 tones total, 13,884 of them carrying a filter block); 1009 effects have loopStart < loopEnd, which is the client's condition for looping at `Class37.java:49,60`.
