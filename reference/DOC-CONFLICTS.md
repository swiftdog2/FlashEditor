# Documentation conflicts and corrections

A running log of claims in `CLAUDE.md`, `AGENTS.md`, `STATE_OF_THE_EDITOR.md` and `reference/`
that turned out to be wrong, ambiguous, or true only of one particular cache.

Two rules this log exists to enforce:

1. **Record architecture, not statistics.** How a container is laid out, how a group is
   addressed, which field decides a payload width - these do not change. How many item
   definitions exist changes with the cache in front of you, and a number written down is read
   later as a target.
2. **A claim in a document is a claim, not evidence.** Where a document and the data disagree,
   the data wins, and the correction gets written back here so the next reader does not pay for
   it again.

## Open

| Where | Claim | Problem |
|---|---|---|
| `CLAUDE.md` byte-identity invariant | Lists exact record counts per content type | These are counts of the *repack*. Several differ in the vanilla OpenRS2 b639 cache, which is now the preferred source of truth. The invariant should state "every record the reference table declares", which is true of any cache. |
| `CLAUDE.md` XTEA invariant | "598 of 598 in the reference cache and 1587 of 1587 in the OpenRS2 b639 archive" | The 598 figure is a repack property. The claim worth keeping is the relationship: a group the key table has a key for, and which does not open without it, must open with it. |
| `AGENTS.md` revision section | Names MAPS, MODELS, NPC_DEFINITIONS and ITEM_DEFINITIONS as the indexes sitting above 639 | Measured group-count deltas between the repack and vanilla b639 are on indexes 3, 7, 9 and 19. Group count is not the same measurement as reference-table version, so both may be true, but the document should say which measurement it means. |
| `STATE_OF_THE_EDITOR.md` | Assessment header dated 2026-07-31; sections 1, 2 and 7 describe a build and suite that no longer exist | Already flagged in `CLAUDE.md`, still unresolved. Sections 7a-7f are worth keeping; the rest is a historical write-up presented as current fact. |

## Corrected

| Where | Was | Is | How it was settled |
|---|---|---|---|
| `hydra-model-decoding/MODEL_DECODING_ANALYSIS.md`, `index-survey/index-007-MODELS.md` | Signed smart's two-byte range is -49152..16383 | -16384..16383 | The reader only takes that branch when the leading byte has bit 7 set, so the biased u16 cannot fall below 0x8000. Read off `JagStream.ReadSmart`. |
| `index-survey/index-000-FRAMES.md` | Two-byte smart's first byte is 0xBF..0xC0 | 0x80..0xFF | Same branch condition. |
| `index-survey/index-000-FRAMES.md` | 1568 empty frames | 1573 | A sector-chain sweep that decodes no frames at all. |
| `index-survey/00-WORKLIST.md` §4.3 | Indexes 4 and 12 hold groups absent from their reference table | Indexes 3, 4, 12 and 32 do | `RealCacheEnumerationTests`, which failed on its first run against the documented claim. |
| `index-architect-02.md` | 27 damage-mark records carry the bare `%1` substitution | 26 do: one record has no template opcode, one stores the empty string | A raw opcode walk transcribed from the client, going through neither the document nor our decoder. |
| `index-survey/index-002-CONFIG.md` | 18 config groups have a client provider, 17 do not | 16 do, 19 do not - two of the 18 providers name groups absent from this cache | Cross-referencing each provider against the reference table's group list. |
| `CLAUDE.md` reusable-tab section | The Interfaces tab is a raw listing because index 3's format is not reverse engineered | Both halves were true when written and neither is now | Index 3 was implemented. |

## Reference material worth pulling in

| Topic | Source | Why |
|---|---|---|
| Index 12 CLIENT_SCRIPTS | **RuneStar** (GitHub) | Carries clientscript opcode definitions and decompilation work. Index 12's codec is small, but the worklist notes a *useful* tab needs a disassembler over roughly 580 opcodes across three client dispatchers, and that is the part worth not reinventing. Check what it covers for build 639 specifically before relying on it - it is oriented at later revisions. |
