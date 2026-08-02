# Map decoding reference, build 637 client / build 639 cache

A byte-level blueprint of how the bundled HydraScape client decodes map data, written so that
map viewing and editing can be implemented in FlashEditor without re-doing the reverse
engineering.

This is the sibling of `reference/hydra-637-definitions/` and follows the same rules: every
claim about the client cites `<ClassName>.java:LINE`, every claim about the data was measured
against the shipped cache, and anything neither read nor measured is labelled as such.

## Contents

| File | Covers |
|---|---|
| `01-cache-access.md` | Index 5 addressing, the JS5 container, XTEA, the reference table |
| `02-terrain-m.md` | The `m<x>_<y>` tile grid, heights, the procedural fallback, the extras tail |
| `03-locs-l.md` | The `l<x>_<y>` loc stream, shapes, rotation, footprints, clipping |
| `04-floor-definitions.md` | `FloorUnderlay` and `FloorOverlayConfig` opcode tables and colour maths |
| `05-colour-and-rendering.md` | RGB to HSL, the HSL palette, the underlay blend, minimap and world map |
| `06-port-plan.md` | What the existing C# gets wrong, and a phased plan to build the editor |

## Which source is authoritative, and for what

Same split as the definitions reference. The cache is build **639**. The client is build **637**
(`client.java:1750` writes `637` into the JS5 handshake). They are a mismatched pair.

| Question | Authority | Why |
|---|---|---|
| How many bytes does this field occupy? | **The 639 cache** | Provable. A wrong width desynchronises a self-delimiting stream, so decoding all 1684 map squares and requiring exact buffer consumption is decisive. |
| Is this byte signed or unsigned? | **The 637 client** | The cache cannot reveal it. |
| What does this field *mean*? | **The 637 client** | The cache cannot reveal it. |
| What are the world-space units? | **The 637 client** | An editor that round-trips raw bytes never needs them; a renderer does. |

One nuance specific to maps: the client contains **live bugs and dead code** on this path. Where
the client does something wrong, this reference says so and tells you to do the right thing
instead, because the goal is an editor that produces correct data, not a bit-exact client clone.
Those cases are marked **CLIENT BUG** and each one explains why the client gets away with it.

## Verdict labels

| Label | Meaning |
|---|---|
| **CONFIRMED** | Read in the client source, cited, and where testable measured against the cache |
| **MEASURED** | Established by decoding the shipped cache; the number quoted is the observed count |
| **INFERRED** | Reasoned from surrounding code; no direct citation or measurement proves it |
| **UNTESTED** | Present in the client but with zero occurrences in the shipped 639 data |

## How this was produced

Nineteen agents in two passes over the 854-file client tree. The first pass deobfuscated eleven
components independently; the second pass took every point on which those agents disagreed and
required it to be settled by parsing the real cache rather than by re-reading code. Six conflicts
were adjudicated that way. The measurements quoted throughout are from that second pass.

## Reading the obfuscated client

The client is a JODE decompile of a re-obfuscated jar. Before reasoning about any conditional,
normalise these. Getting this wrong is how the existing `HYDRA_CACHE_SPEC.md` ended up with
inverted compression constants.

| Obfuscated form | Means |
|---|---|
| `(a ^ 0xffffffff) == -k` | `a == k - 1` |
| `(a ^ 0xffffffff) != -k` | `a != k - 1` |
| `(a ^ 0xffffffff) < (b ^ 0xffffffff)` | `a > b` |
| `(a ^ 0xffffffff) != 0` | `a != -1` |
| `~a`, `a ^ -1` | the same thing |

Additionally:

- Every method carries one or more **garbage parameters** used only in dead branches guarded by a
  sentinel constant. Find the call sites to learn the real signature.
- Every body is wrapped in `try/catch` rethrowing through `Class64_Sub27.method667(ex, "xx.A(...)")`.
  Ignore the wrapper, but **the string literal leaks the original Jagex class and method name**,
  which is the fastest way to group related methods across the tree.
- `do { ... } while (false)` plus labelled `break` is a `goto`. Flatten it.
- Fields are hoisted onto unrelated classes. A field belonging to the map loader may live on
  `Player`, `client`, or an arbitrary `ClassNNN`. Follow reads and writes, not class names.

## Headline corrections to the existing HYDRA_CACHE_SPEC.md

`HydraScape/client/HYDRA_CACHE_SPEC.md` is the prior art. Its map sections are thin and several of
its claims are actively wrong. Do not port from it.

| Spec claim | Reality |
|---|---|
| Compression `0xFF`=none, `0xFE`=GZIP, `0xFD`=BZIP2 (S3.2) | `0`=none, `1`=BZIP2, else GZIP. The spec mis-normalised the `^0xffffffff` idiom. |
| `RSBuffer.method1235` decrypts, `method1215` encrypts (S5.2) | Exactly reversed. `method1215` is decrypt and is the only one on the cache path. |
| Container supports LZMA (S3.1) | No LZMA anywhere in the tree. Only two branches exist. |
| `Class117` is the XTEA cipher (S1) | `Class117` is ISAAC, and it is disabled: both entry points `return 0`. |
| Regions resolve through `map_index.dat` / `MapIndex.java` (S16.1) | Dead code, zero call sites, and factually wrong for this cache. Index 5 is name-hash addressed. |
| Region ids over 3535 are invalid (S16.1) | That constant lives only in the dead class. The shipped index 5 has 5203 groups. |
| Two map file families, `m_X_Y` and `l_X_Y` (S16.2) | Five families, and the names have no underscore after the prefix: `m50_50`, not `m_50_50`. |
| `l_X_Y` is XTEA encrypted, `m_X_Y` is not (S16.2/16.3) | Right conclusion, wrong reason, and the client cannot act on it. See `01-cache-access.md`. |
| Index 5 is cached in memory (S16.4) | The opposite. Both the unpacked child and the packed container are dropped after every read. |
| Floor Overlay is index 4, Floor Underlay index 3 (S9.1) | Both are JS5 index **2**: overlays are group 4, underlays are group 1. |
| Floor Overlay opcode table (S17.4) | Off by one from opcode 3 onward. Would desync on the first definition. |
| Reference table field order (S6.1) | Wrong order, and omits the protocol-6 revision int and the group name-hash block. |
| Last 2 bytes of a group are a CRC footer (S6.3) | They are the version. The CRC32 is a separate reference-table field over `stored[0 .. len-2)`. |
