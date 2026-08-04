using System;
using System.Collections;
using System.Collections.Generic;

namespace FlashEditor.Definitions {
    /// <summary>
    ///     One opcode as it occurred in a definition file, paired with the exact bytes its payload
    ///     occupied.
    /// </summary>
    /// <remarks>
    ///     The payload is kept verbatim rather than as a decoded value because a payload replayed
    ///     byte for byte cannot be mis-encoded. That is what lets an occurrence the decoder threw
    ///     away survive: several indexes store the same opcode twice with different values and the
    ///     client keeps only the last, so the earlier occurrence exists nowhere but in these bytes.
    ///     It also removes any need for the encoder to agree with the decoder about which of
    ///     several valid encodings of the same value the packer happened to choose.
    /// </remarks>
    public readonly struct OpcodeRecord {
        /// <summary>The opcode byte.</summary>
        public int Opcode { get; }

        /// <summary>
        ///     The payload bytes exactly as they were read, empty for a bare flag.
        /// </summary>
        /// <remarks>
        ///     Never null, so a caller can write it without a guard. Treated as immutable by
        ///     everything that holds it; the array is shared with any clone of the stream.
        /// </remarks>
        public byte[] Payload { get; }

        /// <summary>Records one opcode occurrence.</summary>
        /// <param name="opcode">The opcode byte.</param>
        /// <param name="payload">The bytes the opcode's payload occupied, or null for none.</param>
        public OpcodeRecord(int opcode, byte[] payload) {
            Opcode = opcode;
            Payload = payload ?? Array.Empty<byte>();
        }

        /// <summary>Whether the opcode carried no payload, so its presence is its whole meaning.</summary>
        public bool IsBareFlag => Payload.Length == 0;
    }

    /// <summary>
    ///     Every opcode a definition was decoded from, in the order it appeared, with the bytes
    ///     each one carried.
    /// </summary>
    /// <remarks>
    ///     None of the opcode-stream formats in this cache is canonical: nothing fixes an opcode
    ///     order, opcodes repeat, and an encoder that emitted its own fixed ascending order would
    ///     rewrite definitions the user merely opened - which changes the archive, its CRC, and the
    ///     reference-table entry of every archive packed alongside it. Recording the stream and
    ///     replaying it is what makes an untouched definition re-encode to the bytes it came from.
    /// </remarks>
    public sealed class OpcodeStream : IReadOnlyList<OpcodeRecord> {
        private readonly List<OpcodeRecord> _records;

        /// <summary>Creates an empty stream, as a definition built from nothing has.</summary>
        public OpcodeStream() {
            _records = new List<OpcodeRecord>();
        }

        private OpcodeStream(List<OpcodeRecord> records) {
            _records = records;
        }

        /// <summary>How many opcode occurrences were recorded.</summary>
        public int Count => _records.Count;

        /// <summary>The occurrence at <paramref name="index"/>, in decode order.</summary>
        public OpcodeRecord this[int index] => _records[index];

        /// <inheritdoc/>
        public IEnumerator<OpcodeRecord> GetEnumerator() => _records.GetEnumerator();

        IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

        /// <summary>Appends one occurrence to the end of the recorded stream.</summary>
        /// <param name="opcode">The opcode byte.</param>
        /// <param name="payload">The bytes its payload occupied, or null for none.</param>
        public void Add(int opcode, byte[] payload) => _records.Add(new OpcodeRecord(opcode, payload));

        /// <summary>Forgets every recorded occurrence.</summary>
        public void Clear() => _records.Clear();

        /// <summary>Whether the definition carried <paramref name="opcode"/> at all.</summary>
        public bool Has(int opcode) {
            for (int i = 0; i < _records.Count; i++)
                if (_records[i].Opcode == opcode)
                    return true;
            return false;
        }

        /// <summary>Where <paramref name="opcode"/> last appeared, or -1 when it never did.</summary>
        /// <remarks>
        ///     The last occurrence is the one whose value reached the fields, so it is the only one
        ///     an edit can have changed and the only one worth re-encoding from a field.
        /// </remarks>
        public int LastIndexOf(int opcode) {
            for (int i = _records.Count - 1; i >= 0; i--)
                if (_records[i].Opcode == opcode)
                    return i;
            return -1;
        }

        /// <summary>Whether the occurrence at <paramref name="index"/> is the last of its opcode.</summary>
        public bool IsLastOccurrence(int index) => LastIndexOf(_records[index].Opcode) == index;

        /// <summary>
        ///     Drops every occurrence of <paramref name="opcode"/>, so the next encode cannot put
        ///     it back.
        /// </summary>
        /// <remarks>
        ///     An opcode still listed here is replayed from the bytes it was read from, which is
        ///     what keeps a repeated opcode byte-exact but would also resurrect a flag the user had
        ///     just turned off - the row in the grid changes, the save reports success, and the
        ///     definition in the cache is unaltered.
        /// </remarks>
        /// <param name="opcode">The opcode to remove.</param>
        /// <returns>How many occurrences were removed.</returns>
        public int Remove(int opcode) => _records.RemoveAll(record => record.Opcode == opcode);

        /// <summary>Takes a copy no edit through this instance can reach.</summary>
        /// <remarks>
        ///     The editor clones a definition to hold what it looked like before an edit, and the
        ///     two cannot share the recorded stream: dropping an opcode would write through to both
        ///     and the snapshot would then agree with the edit it exists to remember. The payload
        ///     arrays themselves are shared, which is safe because nothing mutates one in place.
        /// </remarks>
        /// <returns>An independent stream holding the same occurrences.</returns>
        public OpcodeStream Clone() => new OpcodeStream(new List<OpcodeRecord>(_records));

        /// <summary>
        ///     Writes the definition back out in the order it was decoded in, taking freshly
        ///     encoded bytes only where an edit could have changed them.
        /// </summary>
        /// <remarks>
        ///     Only the last occurrence of an opcode takes its freshly encoded payload, because
        ///     that is the occurrence whose value the decoder let reach the fields. Every earlier
        ///     occurrence, and any opcode the caller declined to re-encode at all, is replayed from
        ///     the bytes it was read from.
        ///     <para>
        ///     A fresh payload with no place in the recorded stream is one the field values asked
        ///     for but the original did not carry - a value set on a definition that arrived
        ///     without that opcode, or the whole of a definition the editor created from nothing.
        ///     Appending it is what keeps such an edit rather than dropping it.
        ///     </para>
        /// </remarks>
        /// <param name="freshPayloads">Each opcode the field values ask for, with its encoded payload.</param>
        /// <param name="appendInAscendingOrder">
        ///     Whether opcodes the recorded stream never carried are appended in ascending opcode
        ///     order. Set it when the caller does not already build them in a deterministic order,
        ///     so a definition built from nothing still encodes predictably.
        /// </param>
        /// <returns>The complete definition stream, terminator included, ready to read.</returns>
        public JagStream Replay(List<KeyValuePair<int, byte[]>> freshPayloads, bool appendInAscendingOrder = false) {
            JagStream output = new JagStream();
            Dictionary<int, byte[]> encoded = new Dictionary<int, byte[]>(freshPayloads.Count);
            foreach (KeyValuePair<int, byte[]> payload in freshPayloads)
                encoded[payload.Key] = payload.Value;

            Dictionary<int, int> lastOccurrence = new Dictionary<int, int>();
            for (int i = 0; i < _records.Count; i++)
                lastOccurrence[_records[i].Opcode] = i;

            HashSet<int> replaced = new HashSet<int>();

            void Put(int opcode, byte[] payload) {
                output.WriteByte((byte) opcode);
                if (payload != null && payload.Length > 0)
                    output.Write(payload, 0, payload.Length);
            }

            for (int i = 0; i < _records.Count; i++) {
                int opcode = _records[i].Opcode;

                if (lastOccurrence[opcode] == i && encoded.TryGetValue(opcode, out byte[]? fresh)) {
                    Put(opcode, fresh);
                    replaced.Add(opcode);
                }
                else {
                    Put(opcode, _records[i].Payload);
                }
            }

            List<KeyValuePair<int, byte[]>> appended = freshPayloads;
            if (appendInAscendingOrder) {
                //A copy, so replaying does not reorder a list the caller still holds.
                appended = new List<KeyValuePair<int, byte[]>>(freshPayloads);
                appended.Sort((left, right) => left.Key.CompareTo(right.Key));
            }

            foreach (KeyValuePair<int, byte[]> payload in appended)
                if (!replaced.Contains(payload.Key))
                    Put(payload.Key, payload.Value);

            output.WriteByte(0);
            return output.Flip();
        }
    }
}
