# Live edit protocol

The wire format that carries a map or floor-definition edit from FlashEditor to the HydraScape
server (leg A) and from the server to the 637 client (leg B). This document is the specification.
The C# codec (`FlashEditor/LiveEdit/LiveEditFrameCodec.cs`) and both Java codecs cite it, and a
change to either leg changes this document, every codec and the version byte together.

Nothing on this path writes the cache. A pushed file lives for the server session only; Save plus
the JS5 reload handshake remains the only persistence.

All integers are big-endian and unsigned.

## Kinds

Both legs share one kind byte.

| Kind | Meaning | id field |
|---|---|---|
| 0 | `m` terrain | square id `(x << 8) \| y` |
| 1 | `l` locations | square id |
| 2 | `um` underwater terrain | square id |
| 3 | `ul` underwater locations | square id |
| 4 | floor underlay definition (index 2, group 1) | definition id = file id |
| 5 | floor overlay definition (index 2, group 4) | definition id = file id |
| 6 | rebuild (server to client only; no payload) | 0 |
| 7 | batch end (editor to server only; no payload) | 0 |

**Map payloads (kinds 0 to 3)** are the decompressed file bytes exactly as the cache group's file
0 holds them after container decode and decryption - what `RegionCodec.EncodeTerrain` and
`RegionCodec.EncodeLocations` return. Never a container, never ciphertext.

**Definition payloads (kinds 4 and 5)** are the encoded definition bytes as stored in the index-2
file.

A frame of kinds 0 to 5 is a **full replacement** of that file for the session. No frame ever
carries a delta.

## Leg A - editor to server

TCP to `127.0.0.1` only. One editor connection at a time is enough; the server tolerates more.

```
frame := magic:u16 = 0x464C ("FL")  version:u8 = 1  kind:u8  id:u16  length:u32  payload[length]
ack   := status:u8
```

| Offset | Size | Field |
|---|---|---|
| 0 | 2 | magic `0x46 0x4C` |
| 2 | 1 | version `0x01` |
| 3 | 1 | kind, 0 to 5 or 7 |
| 4 | 2 | id |
| 6 | 4 | payload length |
| 10 | length | payload |

Worked example: square 50,50 (`0x3232`) terrain with payload `01 02 03` is

```
46 4C 01 00 32 32 00 00 00 03 01 02 03
```

### Batch end

- Every editor flush is sent as its frames followed by one kind-7 frame, `id = 0`, `length = 0`.
  A flush with no frames sends nothing, not even the batch end.
- The server holds a connection's frames until kind 7 arrives, then hands the whole batch to the
  engine thread at once, so one flush is applied in one drain: one collision rebuild per square,
  never from a new `m` beside an old `l`, and one kind-6 rebuild per player.
- Frames held when the connection closes are **discarded**. A sender therefore counts no frame of
  a batch as delivered until the batch end is acknowledged, and resends the whole batch after a
  connection failure, including frames that were individually acknowledged.

### Acknowledgement

- The server replies with **exactly one ack byte per frame**, the batch end included, in frame
  order.
  - `0` - well formed and accepted for the engine. The server acks on its reader thread and the
    engine applies the batch later, so a `0` never means the frame has been applied.
  - `1` - rejected: a kind outside 0 to 5 and 7, or a length over the limit. Every 16-bit id is a
    valid square or definition id, so the id is never a reason.
- A frame with a bad kind is rejected only after its payload has been read, so the stream stays
  in sync and the connection stays open.
- A bad magic or version **closes the connection without an ack**. The sender cannot tell a
  mismatched peer from a dead one by waiting, so it treats the missing ack as a failed push.
- The sender may pipeline, but must read one ack per frame it wrote.

### Limit

- Maximum payload length: **262144** bytes. A frame declaring more is rejected with status `1`
  and the connection is then closed, because the declared payload is never read and the stream
  cannot be resynchronised past it. A sender must not rely on the server skipping it.
- Every map file must fit one frame under this limit: a frame never carries a delta or a part of
  a file, so a file over it cannot be pushed at all.

## Leg B - server to client

New server-to-client opcode **127**, variable-short (size code `-2`), one-byte opcode (ISAAC is
disabled in this client).

```
body := version:u8 = 1  kind:u8  id:u16  total:u32  offset:u32  chunk[packetSize - 12]
```

| Offset | Size | Field |
|---|---|---|
| 0 | 1 | version `0x01` |
| 1 | 1 | kind, 0 to 6 |
| 2 | 2 | id |
| 4 | 4 | total payload length |
| 8 | 4 | offset of this chunk within the payload |
| 12 | packetSize - 12 | chunk |

### Fragmentation

- A payload is split into chunks of at most **6000** bytes, one packet each, in ascending offset
  order.
- The limit is set by the client, not the server: the client's game socket is opened with a
  7500-byte ring buffer (`HydraScape/client/src/Class332_Sub1.java:93-94`), and a packet of 7501
  bytes or more drops the connection. 6000 leaves the header and framing well inside it.
- The server's encoder separately refuses any packet over 50000 bytes
  (`HydraScape/server/src/net/tazogaming/hydra/net/netty/codec/GameProtocolEncoder.java:42`), so
  an unfragmented map file could not be sent even if the client could hold it.

### Reassembly

The client reassembles per `(kind, id)`:

- `offset == 0` starts a new buffer of `total` bytes, discarding any partial one for that key.
- A chunk whose `offset` is not the end of the previous chunk discards the partial buffer.
- A chunk whose `total` differs from the partial buffer's, or which would run past `total`, also
  discards the partial buffer.
- The payload is applied when `offset + chunk.length == total`.
- A body shorter than 12 bytes, or with a version other than 1, a kind over 6, or a `total` over
  262144, is ignored.

### Rebuild

Kind 6 has `id = 0`, `total = 0`, `offset = 0` and no chunk. It asks the client to rebuild the
scene if it is in a built state. The client reads only its version and kind, but still ignores
a body shorter than 12 bytes, so the header is sent in full. The server sends one after each
applied batch, and after the overrides it resends on every region send, so a batch costs one
rebuild rather than one per file.

## Lifetime

- The server keeps the latest payload per `(kind, id)` until it restarts. After every region
  packet it sends a player, it resends every map payload inside the new area, every floor
  definition, then one rebuild.
- The client keeps every completed payload until it returns to the login screen, which clears
  them.
- A map payload for a square with no group in the client's cache is kept but never read, because
  the client's map loader skips such a square.
