using System;
using System.Collections.Generic;
using FlashEditor.Definitions.Config;
using FlashEditor.IO;
using Xunit;

namespace FlashEditor.Tests.Definitions.Config {
    /// <summary>
    ///     Every payload-free flag on an index 2 record, turned off, turned on, and landed back on
    ///     the bytes it started from.
    /// </summary>
    /// <remarks>
    ///     <b>A bare flag is the one field a byte-identity sweep can never see.</b> Its whole meaning
    ///     is whether its opcode is in the stream, so setting one adds or drops a byte and changes
    ///     the record's length - and a sweep proves only that an <i>unedited</i> record comes back
    ///     unchanged. Four real defects in this repository have lived in that gap, and two of them
    ///     were exactly this shape on the object and NPC codecs: a setter that removed the opcode
    ///     forgot where it was, so turning the flag back on re-emitted it at the end of the record.
    ///     <para>
    ///     Three checks per flag, and the third is the one an asymmetric setter cannot pass: off,
    ///     on, and back to the original bytes. The bytes are hand-built rather than taken from the
    ///     cache, because a round trip of this encoder against this decoder proves nothing - the
    ///     inputs here are written as the format defines them and the assertions are against those
    ///     bytes.
    ///     </para>
    ///     <para>
    ///     Every case puts the flag's opcode in the <b>middle</b> of the record. Position is the
    ///     whole point: on this index not one of group 36's 1,051 files is in ascending opcode order,
    ///     so a flag restored at the end of the record is a different file, and the archive CRC
    ///     covers those bytes.
    ///     </para>
    /// </remarks>
    public sealed class ConfigBareFlagEditTests {
        /// <summary>
        ///     One boolean spelled by a payload-free opcode, and the record that carries it.
        /// </summary>
        /// <param name="Label">What the flag is, for the failure line.</param>
        /// <param name="Stored">A record whose stream carries the opcode, with it in the middle.</param>
        /// <param name="Decode">Decodes the record.</param>
        /// <param name="Read">Reads the flag off it.</param>
        /// <param name="Write">Writes the flag onto it.</param>
        /// <param name="Encode">Re-encodes it.</param>
        public sealed record Flag(
            string Label,
            byte[] Stored,
            Func<byte[], object> Decode,
            Func<object, bool> Read,
            Action<object, bool> Write,
            Func<object, byte[]> Encode);

        /// <summary>
        ///     Every payload-free flag the config field pane offers as editable.
        /// </summary>
        /// <remarks>
        ///     Written out one row each rather than discovered by reflection. A flag added to a codec
        ///     later does not appear here on its own, which is deliberate: the row is where the
        ///     opcode's polarity is stated, and polarity is the thing that cannot be inferred from
        ///     the property - opcode 4 of a floor underlay means <c>CastsShadow = false</c> while
        ///     opcode 3 of a map scene icon means <c>StretchToFootprint = true</c>.
        /// </remarks>
        /// <returns>The flags.</returns>
        public static IEnumerable<object[]> Flags() {
            foreach (Flag flag in All())
                yield return new object[] { flag };
        }

        private static IEnumerable<Flag> All() {
            //Underlay opcode 1 is a three-byte colour, so the flag sits between two real opcodes.
            yield return Legacy<FloorUnderlayDefinition>("floor underlay casts shadow (opcode 4)",
                new byte[] { 1, 0x3C, 0x1E, 0x0A, 4, 2, 0x00, 0x29, 0 },
                stored => new FloorUnderlayDefinition { Id = 0 }.Decode(new JagStream(stored)),
                record => record.CastsShadow, (record, value) => record.CastsShadow = value,
                record => record.Encode().ToArray());

            yield return Legacy<FloorUnderlayDefinition>("floor underlay occludes (opcode 5)",
                new byte[] { 1, 0x3C, 0x1E, 0x0A, 5, 2, 0x00, 0x29, 0 },
                stored => new FloorUnderlayDefinition { Id = 0 }.Decode(new JagStream(stored)),
                record => record.Occludes, (record, value) => record.Occludes = value,
                record => record.Encode().ToArray());

            yield return Legacy<FloorOverlayDefinition>("floor overlay flat ground occluder (opcode 5)",
                new byte[] { 1, 0x3C, 0x1E, 0x0A, 5, 11, 0x08, 0 },
                stored => new FloorOverlayDefinition { Id = 0 }.Decode(new JagStream(stored)),
                record => record.FlatGroundOccluder,
                (record, value) => record.FlatGroundOccluder = value,
                record => record.Encode().ToArray());

            yield return Legacy<FloorOverlayDefinition>("floor overlay world map background (opcode 8)",
                new byte[] { 1, 0x3C, 0x1E, 0x0A, 8, 11, 0x08, 0 },
                stored => new FloorOverlayDefinition { Id = 0 }.Decode(new JagStream(stored)),
                record => record.IsWorldMapBackground,
                (record, value) => record.IsWorldMapBackground = value,
                record => record.Encode().ToArray());

            yield return Legacy<FloorOverlayDefinition>("floor overlay casts shadow (opcode 10)",
                new byte[] { 1, 0x3C, 0x1E, 0x0A, 10, 11, 0x08, 0 },
                stored => new FloorOverlayDefinition { Id = 0 }.Decode(new JagStream(stored)),
                record => record.CastsShadow, (record, value) => record.CastsShadow = value,
                record => record.Encode().ToArray());

            yield return Legacy<FloorOverlayDefinition>("floor overlay blends with neighbours (opcode 12)",
                new byte[] { 1, 0x3C, 0x1E, 0x0A, 12, 11, 0x08, 0 },
                stored => new FloorOverlayDefinition { Id = 0 }.Decode(new JagStream(stored)),
                record => record.BlendWithNeighbours,
                (record, value) => record.BlendWithNeighbours = value,
                record => record.Encode().ToArray());

            yield return Legacy<MapSceneIconDefinition>("map scene icon stretch to footprint (opcode 3)",
                new byte[] { 1, 0x00, 0x5D, 3, 2, 0x11, 0x22, 0x33, 0 },
                stored => new MapSceneIconDefinition { Id = 0 }.Decode(new JagStream(stored)),
                record => record.StretchToFootprint,
                (record, value) => record.StretchToFootprint = value,
                record => record.Encode().ToArray());

            yield return Config<ParameterTypeDefinition>("parameter type opcode 4 flag",
                new byte[] { 1, (byte) 'i', 4, 2, 0x00, 0x00, 0x00, 0x07, 0 },
                record => record.Unknown4, (record, value) => record.Unknown4 = value);

            yield return Config<ClientVariableDefinition>("client variable server writable (opcode 2)",
                new byte[] { 2, 1, (byte) 'i', 0 },
                record => record.ServerWritable, (record, value) => record.ServerWritable = value);

            yield return Config<IdentityKitDefinition>("identity kit opcode 3 flag",
                new byte[] { 3, 1, 0x05, 2, 1, 0x00, 0x2A, 0 },
                record => record.Unknown3, (record, value) => record.Unknown3 = value);

            yield return Config<MapElementDefinition>("map element rendered (opcode 16)",
                new byte[] { 6, 0x01, 16, 19, 0x03, 0xB4, 0 },
                record => record.Rendered, (record, value) => record.Rendered = value);

            yield return Config<RenderAnimationDefinition>("render animation opcode 53 flag",
                new byte[] { 53, 2, 0x00, 0x0B, 0 },
                record => record.Unknown53, (record, value) => record.Unknown53 = value);

            yield return Config<QuestDefinition>("quest opcode 8 flag",
                new byte[] { 8, 17, 0x00, 0x2A, 0 },
                record => record.Unknown8, (record, value) => record.Unknown8 = value);
        }

        /// <summary>Turning a flag off drops exactly its opcode and leaves every other byte alone.</summary>
        /// <param name="flag">The flag under test.</param>
        [Theory]
        [MemberData(nameof(Flags))]
        public void TurningAFlagOffDropsExactlyItsOpcode(Flag flag) {
            object record = flag.Decode(flag.Stored);
            bool stored = flag.Read(record);

            flag.Write(record, !stored);
            byte[] off = flag.Encode(record);

            Assert.Equal(flag.Stored.Length - 1, off.Length);
            Assert.Equal(Without(flag.Stored, OpcodeOf(flag)), off);
        }

        /// <summary>
        ///     A flag turned off and back on lands on the original stored bytes.
        /// </summary>
        /// <remarks>
        ///     The assertion an asymmetric setter cannot pass, and the reason the codecs suppress an
        ///     opcode rather than removing it. Compared against the bytes the record was built from
        ///     rather than against a re-encode taken before the edit, because a re-encode compared
        ///     with itself would agree with a setter that moved the opcode both times.
        /// </remarks>
        /// <param name="flag">The flag under test.</param>
        [Theory]
        [MemberData(nameof(Flags))]
        public void AFlagTurnedOffAndBackOnLandsOnTheOriginalStoredBytes(Flag flag) {
            object record = flag.Decode(flag.Stored);
            bool stored = flag.Read(record);

            flag.Write(record, !stored);
            flag.Write(record, stored);

            Assert.Equal(flag.Stored, flag.Encode(record));
            Assert.Equal(stored, flag.Read(record));
        }

        /// <summary>
        ///     A record that never carried the flag gains its opcode when the flag is set, and loses
        ///     it again when it is cleared.
        /// </summary>
        /// <remarks>
        ///     The other direction, and the one the cache supplies unevenly - several of these
        ///     opcodes occur in no file at all, so a test that only exercised records carrying them
        ///     would leave the added-opcode rule untested for exactly the flags nothing else covers.
        /// </remarks>
        /// <param name="flag">The flag under test.</param>
        [Theory]
        [MemberData(nameof(Flags))]
        public void ARecordWithoutTheFlagGainsAndLosesTheOpcode(Flag flag) {
            byte[] without = Without(flag.Stored, OpcodeOf(flag));

            object record = flag.Decode(without);
            bool absent = flag.Read(record);

            flag.Write(record, !absent);
            byte[] added = flag.Encode(record);
            Assert.Equal(without.Length + 1, added.Length);

            flag.Write(record, absent);
            Assert.Equal(without, flag.Encode(record));
        }

        /// <summary>The opcode a flag's record carries, which is the one byte the two forms differ by.</summary>
        /// <param name="flag">The flag.</param>
        /// <returns>The opcode.</returns>
        private static byte OpcodeOf(Flag flag) {
            object record = flag.Decode(flag.Stored);
            bool stored = flag.Read(record);

            flag.Write(record, !stored);
            byte[] off = flag.Encode(record);

            for (int i = 0; i < off.Length; i++)
                if (off[i] != flag.Stored[i])
                    return flag.Stored[i];

            throw new InvalidOperationException(
                flag.Label + " re-encoded identically with the flag turned off, so the setter" +
                " changed the field and nothing else - which is an edit that vanishes silently.");
        }

        /// <summary>The same record with one byte removed.</summary>
        /// <param name="stored">The record.</param>
        /// <param name="opcode">The byte to drop, at its first occurrence.</param>
        /// <returns>The shortened record.</returns>
        private static byte[] Without(byte[] stored, byte opcode) {
            var kept = new List<byte>(stored.Length - 1);
            bool dropped = false;

            foreach (byte value in stored) {
                if (!dropped && value == opcode) {
                    dropped = true;
                    continue;
                }

                kept.Add(value);
            }

            return kept.ToArray();
        }

        private static Flag Legacy<T>(string label, byte[] stored, Func<byte[], T> decode,
            Func<T, bool> read, Action<T, bool> write, Func<T, byte[]> encode) where T : class {
            return new Flag(label, stored,
                bytes => decode(bytes),
                record => read((T) record),
                (record, value) => write((T) record, value),
                record => encode((T) record));
        }

        private static Flag Config<T>(string label, byte[] stored, Func<T, bool> read,
            Action<T, bool> write) where T : ConfigDefinition, new() {
            return new Flag(label, stored,
                bytes => {
                    var record = new T { Id = 0 };
                    record.Decode(new JagStream(bytes));
                    return record;
                },
                record => read((T) record),
                (record, value) => write((T) record, value),
                record => ((T) record).Encode().ToArray());
        }
    }
}
