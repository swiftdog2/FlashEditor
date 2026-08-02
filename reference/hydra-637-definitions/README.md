# Definition opcode reference, build 637 client

De-obfuscated opcode tables for the item, NPC and object definition decoders in the Java
client bundled with this cache, cross-referenced against this project's C# codecs.

This exists because the knowledge is expensive to produce and easy to lose. The client is
heavily obfuscated - a deeply nested if/else chain on the opcode, with fields named
`anInt2975` - so establishing what a single opcode reads is slow, careful work. Doing it
once and writing it down is the point.

## Which source is authoritative, and for what

The cache is build **639**. The client is build **637**. They are a mismatched pair (see
`AGENTS.md`), so neither is authoritative on its own.

| Question | Authority | Why |
|---|---|---|
| How many bytes does this opcode's payload occupy? | **The 639 cache** | Proven empirically. A wrong size desynchronises a self-delimiting opcode stream, so sweeping every definition and requiring each to consume its buffer exactly is a decisive test. It is also the revision we actually target. |
| Is this byte signed or unsigned? | **The 637 client** | The cache cannot reveal it. A stream parses identically either way; only the value differs, and only above 127. |
| What does this field *mean*? | **The 637 client** | The cache cannot reveal it. A field parses identically whether you call it `contrast` or `shadow`. |
| Should we change our decoder to match the client? | **Neither, alone** | See below. |

The last row is the one that matters. **Do not "fix" a decoder to match the client where the
639 data disagrees.** The two builds genuinely differ, and the sweeps found opcodes our codec
handles that the 637 client does not - none of which occur in the 639 cache. Changing those
to match 637 would break nothing today and would be wrong tomorrow.

The right response to a disagreement is to record it, note whether the opcode actually occurs
in the 639 cache, and leave the code alone unless the data says otherwise.

## What this reference cannot tell you, and what already proved it

Sizes were settled by sweeping the whole cache through the production codecs. That work found
the decoders correct for items (20,470 definitions) and NPCs (13,359), and genuinely wrong for
objects (56,199) on two opcodes:

- **Opcode 75** was read as a bare flag; it carries one unsigned byte
  (`Class352.java:1400`). 1,591 definitions carry it. 194 threw outright and the rest silently
  produced garbage from that offset onward.
- **Opcode 72** was read as one unsigned byte; it carries a signed short shifted left two
  (`Class352.java:1410`), like the offsets at 70 and 71.

Opcode 72 is the cautionary tale for this whole document. The leftover low byte was read as
the next opcode, and because it is often a bare flag the parse **re-synchronised by accident**:
359 of the 371 affected definitions passed an exact-consumption check by luck. This project's
own hand-built test stream encoded the wrong layout with a comment insisting `"NOT Short<<2!"`,
so the test suite was pinning the bug. Neither the data alone nor a round trip of our encoder
against our decoder could have caught it. The client did.

That is the argument for keeping these tables current.

## How to read the tables

Every row that makes a claim about the client cites `<ClassName>.java:LINE`, so any claim here
can be checked against the source in seconds. A row without a citation is explicitly marked
unverified rather than quietly asserted.

Verdicts:

| Verdict | Meaning |
|---|---|
| `AGREE` | Same size, same signedness, same meaning. |
| `SIZE-DIFFERS` | Different payload width. Check which one the 639 data supports before touching anything. |
| `SIGNEDNESS-DIFFERS` | Same width, different sign. Invisible to every existing test; shows up as wrong values above 127. |
| `SEMANTICS-DIFFER` | Same bytes, different meaning or field name. Invisible to every existing test. |
| `CODEC-ONLY` | We handle it, the 637 client does not. |
| `CLIENT-ONLY` | The client handles it, we do not. |

`SIGNEDNESS-DIFFERS` and `SEMANTICS-DIFFER` are the rows worth your attention: no test in the
suite can detect either, and both surface as wrong numbers in the editor and wrong data written
back on save.

## Provenance

Client source: `HydraScape/client/src`, build 637, established from the JS5 handshake, both
login blocks and an on-screen `"Build: 637"` string. The number 639 appears in none of its 854
files. See `AGENTS.md` for that determination in full.

## Files

- `item-opcodes.md`
- `npc-opcodes.md`
- `object-opcodes.md`
