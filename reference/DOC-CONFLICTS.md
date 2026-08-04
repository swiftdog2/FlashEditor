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
| `STATE_OF_THE_EDITOR.md` | Assessment header dated 2026-07-31; sections 1, 2 and 7 describe a build and suite that no longer exist | Already flagged in `CLAUDE.md`, still unresolved. Sections 7a-7f are worth keeping; the rest is a historical write-up presented as current fact. |
| `Texture.cs:522-526` | Comment says two type-12 opcodes are swallowed | There are four: `(12,2)`, `(12,4)`, `(12,5)`, `(12,6)`. Both type-12 nodes in the cache are affected. |
| `TextureGraphEvaluator.cs:1660` | `EvalBlur`, headed "TYPE 17: Blur" | Dead code, and the same file's live type-17 arm is headed "NOT blur". The client settles it as an HSL adjust. Three further mono arms (types 10, 20, 33) are unreachable by the same mono-flag rule. Type 21 is a three-input lerp with no numeric parameter, not an emboss, and both files say emboss. |
| `Texture.cs` decoder comments | Type labels for 9, 22, 30 and 35 | Wrong; 9 and 22 are labelled as each other's job. The evaluator has all four right, so the two files disagree with each other. This is the trap `CLAUDE.md` already records - settle a type from the dispatch and the client, never from a method's own header. |
| `index-survey/index-009-TEXTURES.md` | 946 groups, table version 443, 3784 trailing bytes, 507 uncompressed, index 26 declaring 1408 textures | All repack residue. Build 639 is 915, 440, 0, 476, and index 26 declaring 915 - index 26 and index 9 are 1:1 in the vanilla capture. |
| `TextureGraphConformanceTests.EveryTexture_ProducesAThumbnail` | Iterates textures declared by index 26 but carrying no graph in index 9 | The repack has 462 of those; the vanilla capture has none, so on the default cache that loop body never runs. The test is not silently green - it reports the population - but the branch needs either a synthetic case or an explicit statement that it only exercises on the repack. |

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
| `CLAUDE.md` invariants | Four indexes carry four zero bytes per file past the end of their reference table | Repack residue. The vanilla b639 capture has no trailing bytes on any of its 35 tables | Parsing all 35 tables in both caches and comparing each against a field-by-field length. |
| `CLAUDE.md` invariants | Four indexes hold groups their reference table does not declare, with the ids listed | Repack residue. The vanilla capture has no orphan groups on any index | Same parser, comparing live idx slots against declared ids. Written into CLAUDE.md by this session, so it lasted about a day. |
| This log, first revision | The repack adds 55 item groups, roughly 14,000 items | Both caches declare 80 item groups. The repack's idx19 has 135 *slots*, 55 of them dead records pointing at sector zero. The real delta is 43 files | Dividing an idx file's size by 6 counts allocated slots, not declared groups. It also missed indexes 27 and 29, whose file counts move inside an unchanged group count. Six indexes carry content deltas, not four. |
| `AGENTS.md` revision section | Reference-table versions identify which indexes a server customised | Index 3 carries the same version in both caches while holding 1,373 more files in the repack, and index 18 has identical group and file counts with a different version and 115 more payload bytes. A version match is not evidence an index is untouched | Comparing versions and payloads table by table across both caches. |

## Reference material worth pulling in

| Topic | Source | Why |
|---|---|---|
| Index 12 CLIENT_SCRIPTS | **RuneStar** (GitHub) | Carries clientscript opcode definitions and decompilation work. Index 12's codec is small, but the worklist notes a *useful* tab needs a disassembler over roughly 580 opcodes across three client dispatchers, and that is the part worth not reinventing. Check what it covers for build 639 specifically before relying on it - it is oriented at later revisions. |
