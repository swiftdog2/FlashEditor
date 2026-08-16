using System;
using System.Collections.Generic;
using System.IO;
using FlashEditor.IO;

namespace FlashEditor.Definitions.Config {
    /// <summary>
    ///     One opcode exactly as it appeared in a config record, with the bytes it carried.
    /// </summary>
    /// <remarks>
    ///     The payload is kept raw rather than as a decoded value because index 2's opcodes are not
    ///     all one integer wide: opcode 9 of a map element is four fields, opcode 15 is four arrays,
    ///     and opcode 249 is a list. A per-occurrence <c>int</c> could not describe any of them, and
    ///     the one thing an earlier occurrence of a repeated opcode has to do is come back unchanged.
    /// </remarks>
    public readonly struct ConfigOpcode {
        /// <summary>The opcode byte.</summary>
        public int Opcode { get; }

        /// <summary>The payload bytes this occurrence consumed, empty for a flag opcode.</summary>
        public byte[] Payload { get; }

        /// <summary>Records one opcode occurrence and the bytes it read.</summary>
        /// <param name="opcode">The opcode byte.</param>
        /// <param name="payload">The payload bytes it consumed.</param>
        public ConfigOpcode(int opcode, byte[] payload) {
            Opcode = opcode;
            Payload = payload ?? Array.Empty<byte>();
        }
    }

    /// <summary>
    ///     The opcode loop every JS5 index 2 config record shares, with the stored opcode order and
    ///     repetition captured so a record nobody edited re-encodes to the bytes it was read from.
    /// </summary>
    /// <remarks>
    ///     The format is not canonical and index 2 is the worst of it measured anywhere in this
    ///     cache. Group 36 stores <b>none</b> of its 1,051 files in ascending opcode order, across 16
    ///     distinct orders; group 46 stores none of its 28; group 31 none of its 4. An encoder that
    ///     walked opcodes 1..n would reproduce almost nothing, so the decoded order is the model and
    ///     the encoder replays it.
    ///     <para>
    ///     Repetition is captured the same way. Group 36 files 779 and 780 each emit opcode 22 twice
    ///     with different values, which is the same shape as floor overlay 94's doubled opcode 11: a
    ///     decoder that keeps only the winning value writes a file of the right length and the wrong
    ///     contents, and the archive CRC covers those bytes, so it is a silent corruption rather than
    ///     an error.
    ///     </para>
    ///     <para>
    ///     The <b>last</b> occurrence of an opcode is re-encoded from the definition's live fields,
    ///     which is what a byte-identity sweep actually tests and what carries an edit back out.
    ///     Earlier occurrences replay their captured bytes, because the fields only remember the
    ///     value that won. Every opcode present in the cache is somebody's last occurrence, so the
    ///     encoder is exercised on all of them.
    ///     </para>
    /// </remarks>
    public abstract class ConfigDefinition {
        private readonly List<ConfigOpcode> _opcodes = new List<ConfigOpcode>();

        /* Opcodes an edit has turned off. A set rather than a removal from the list above, and the
           difference is the whole of a defect this project has already paid for once on the object
           and NPC codecs: removing an opcode forgets WHERE it was, so turning the flag back on
           re-emits it at the end of the record. That is a record of the right length with a byte
           moved, which the commit path then stages as a real change - and an archive CRC covers the
           stored bytes, so it drags in the reference-table entry of every archive packed alongside
           it, for an edit that netted nothing. Index 2 cannot afford that at all: not one of group
           36's 1,051 files is in ascending opcode order, so position is the record. */
        private readonly HashSet<int> _suppressed = new HashSet<int>();

        /// <summary>Definition id, which is the file id within the family's group.</summary>
        public int Id { get; set; } = -1;

        /// <summary>The opcodes this record carried, in the order the file stores them.</summary>
        /// <remarks>
        ///     This is the hit map as well as the order. Presence must be read from here and never
        ///     inferred by comparing a field against its default: several defaults in these record
        ///     classes are legal stored values, so "did this record carry opcode N" is not a question
        ///     the decoded value can answer.
        ///     <para>
        ///     Unfiltered: an opcode an edit has suppressed is still listed here, because this is
        ///     what the <i>file</i> carried. <see cref="Has"/> is what an encoder asks.
        ///     </para>
        /// </remarks>
        public IReadOnlyList<ConfigOpcode> DecodedOpcodes => _opcodes;

        /// <summary>Whether the record will emit an opcode.</summary>
        /// <remarks>
        ///     False for an opcode the file carried and an edit has since turned off, which is what
        ///     keeps <see cref="AddedOpcodes"/> from appending a copy of one that is only suppressed.
        /// </remarks>
        /// <param name="opcode">The opcode to look for.</param>
        /// <returns>Whether it occurs in the stored stream and has not been suppressed.</returns>
        public bool Has(int opcode) {
            return Stored(opcode) && !_suppressed.Contains(opcode);
        }

        /// <summary>Whether the stored file carried an opcode, whatever an edit has done since.</summary>
        /// <param name="opcode">The opcode to look for.</param>
        /// <returns>Whether it occurs in the decoded stream.</returns>
        public bool Stored(int opcode) {
            for (int i = 0; i < _opcodes.Count; i++)
                if (_opcodes[i].Opcode == opcode)
                    return true;
            return false;
        }

        /// <summary>
        ///     Turns a payload-free opcode on or off, keeping the position the file stored it at.
        /// </summary>
        /// <remarks>
        ///     <b>A bare opcode's whole meaning is whether it is in the stream</b>, so a property
        ///     spelled by one cannot be written back by re-deriving a payload - there is no payload.
        ///     Every boolean on an index 2 record class therefore has to call this from its setter as
        ///     well as assigning the field, or turning the flag off changes the field and nothing
        ///     else and the file re-encodes identically: an edit that vanishes with no error
        ///     anywhere.
        ///     <para>
        ///     The field still has to be assigned, and to its constructor default when the opcode
        ///     goes off. <see cref="AddedOpcodes"/> compares against that default and would otherwise
        ///     append the opcode straight back.
        ///     </para>
        /// </remarks>
        /// <param name="opcode">The payload-free opcode.</param>
        /// <param name="present">Whether the record should emit it.</param>
        protected void SetBareOpcode(int opcode, bool present) {
            if (present)
                _suppressed.Remove(opcode);
            else if (Stored(opcode))
                _suppressed.Add(opcode);
        }

        /// <summary>Decodes one config record from its stored bytes.</summary>
        /// <param name="stream">The definition file, positioned at its first opcode.</param>
        public void Decode(JagStream stream) {
            _opcodes.Clear();
            _suppressed.Clear();

            while (true) {
                int opcode = stream.ReadUnsignedByte();
                if (opcode == 0)
                    break;

                int start = stream.Position;
                ReadPayload(opcode, stream);
                int end = stream.Position;

                //Capture what the payload reader consumed rather than what it decoded to. That is
                //the only description that covers every opcode shape here, and it is what an
                //earlier occurrence of a repeated opcode is re-encoded from.
                stream.Position = start;
                _opcodes.Add(new ConfigOpcode(opcode, stream.ReadBytes(end - start)));
            }
        }

        /// <summary>Encodes this record back to its file representation.</summary>
        /// <returns>The encoded bytes, positioned at 0.</returns>
        public JagStream Encode() {
            JagStream stream = new JagStream();

            for (int i = 0; i < _opcodes.Count; i++) {
                int opcode = _opcodes[i].Opcode;

                //A suppressed opcode is skipped where it stood rather than deleted from the list,
                //so restoring it puts it back in the same place.
                if (_suppressed.Contains(opcode))
                    continue;

                stream.WriteByte(opcode);

                if (IsLastOccurrence(i))
                    WritePayload(opcode, stream);
                else
                    stream.Write(_opcodes[i].Payload);
            }

            foreach (int opcode in AddedOpcodes()) {
                stream.WriteByte(opcode);
                WritePayload(opcode, stream);
            }

            stream.WriteByte(0);
            return stream.Flip();
        }

        /// <summary>Reads one opcode's payload into this record's fields.</summary>
        /// <remarks>
        ///     Must consume exactly the bytes the 637 client's dispatcher consumes for that opcode,
        ///     and must throw for an opcode it does not handle. Silently ignoring an unknown opcode
        ///     is what the client does and it desynchronises everything after it.
        /// </remarks>
        /// <param name="opcode">The opcode byte just read.</param>
        /// <param name="stream">The definition file, positioned at the payload.</param>
        protected abstract void ReadPayload(int opcode, JagStream stream);

        /// <summary>Writes one opcode's payload from this record's fields.</summary>
        /// <param name="opcode">The opcode being emitted.</param>
        /// <param name="stream">The stream to write to.</param>
        protected abstract void WritePayload(int opcode, JagStream stream);

        /// <summary>
        ///     Opcodes an edit has made necessary that the decoded file did not carry.
        /// </summary>
        /// <remarks>
        ///     Compared against the record class's constructor default, which is the only signal
        ///     available. An edit that sets a field to exactly its default is therefore not written,
        ///     which is correct for the file (the client reads the same value either way) but means
        ///     "clear this field" cannot be expressed by an edit on a record that never carried the
        ///     opcode.
        /// </remarks>
        /// <returns>The opcodes to append, in the order they should be written.</returns>
        protected virtual IEnumerable<int> AddedOpcodes() => Array.Empty<int>();

        /// <summary>Refuses an opcode the record type does not define.</summary>
        /// <param name="opcode">The unhandled opcode.</param>
        /// <returns>Never returns; always throws.</returns>
        protected InvalidDataException Unknown(int opcode) {
            return new InvalidDataException("Unknown " + GetType().Name + " opcode " + opcode +
                                            " in definition " + Id);
        }

        /// <summary>Whether the occurrence at an index is the last of its opcode.</summary>
        /// <param name="index">The occurrence index within the decoded stream.</param>
        /// <returns>Whether any later occurrence carries the same opcode.</returns>
        private bool IsLastOccurrence(int index) {
            int opcode = _opcodes[index].Opcode;
            for (int i = index + 1; i < _opcodes.Count; i++)
                if (_opcodes[i].Opcode == opcode)
                    return false;
            return true;
        }
    }
}
