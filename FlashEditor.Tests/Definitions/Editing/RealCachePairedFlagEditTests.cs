using System;
using System.Collections.Generic;
using System.Linq;
using FlashEditor.Cache;
using FlashEditor.Definitions;
using FlashEditor.Definitions.Editing;
using FlashEditor.Definitions.Entities;
using FlashEditor.Tests.Cache.RealCache;
using Xunit;
using Xunit.Abstractions;
using FlashEditor.IO;

namespace FlashEditor.Tests.Definitions.Editing {
    /// <summary>
    ///     Editing the two properties that more than one opcode spells.
    /// </summary>
    /// <remarks>
    ///     <c>ObjectDefinition.walkable</c> is opcode 17 <b>or</b> 18 and
    ///     <c>NPCDefinition.mainOptionIndex</c> is 158 <b>or</b> 159. Neither is a single opcode's
    ///     presence, so neither fits the three checks in <see cref="RealCacheBareFlagEditTests"/>,
    ///     and both have already produced a defect: a stored 17 used to come back as an 18, which
    ///     is the same length and a different statement.
    ///     <para>
    ///     Two things make this family harder than a lone flag. The value is a function of
    ///     <i>which</i> opcode is present, so restoring the wrong one is a silent rewrite rather
    ///     than a length change; and where both are present the value depends on their <b>order</b>,
    ///     so the getter has to read the recorded stream by index. Suppressing an opcode rather than
    ///     removing it changes what "still in the stream" means for exactly that kind of getter - it
    ///     can then report a value the encoder does not write, which misrepresents the record rather
    ///     than merely rewriting it.
    ///     </para>
    ///     <para>
    ///     Populations, measured over the whole of index 16 and index 18 and identical in both
    ///     caches on index 16: 4,407 objects carry 17 alone, 8,492 carry 18 alone, <b>7</b> carry
    ///     both (ids 48011-48017, 17 first) and 43,293 carry neither. On index 18 <b>no NPC in
    ///     either cache carries opcode 158 at all</b> - 2,195 carry 159 in the vanilla capture and
    ///     2,198 in the repack - so every case involving 158 is built from a real record and says
    ///     so. An absent input is precisely where this project's sweeps have been proven blind.
    ///     </para>
    /// </remarks>
    public sealed class RealCachePairedFlagEditTests : IClassFixture<RealCacheFixture> {
        private readonly RealCacheFixture _cache;
        private readonly ITestOutputHelper _output;

        public RealCachePairedFlagEditTests(RealCacheFixture cache, ITestOutputHelper output) {
            _cache = cache;
            _output = output;
        }

        /// <summary>
        ///     Turning walkable off and back on restores the opcode the file carried, not the other one.
        /// </summary>
        /// <remarks>
        ///     The assertion that caught the first defect here. Opcode 17 also resets the clip type
        ///     and 18 does not, so substituting one for the other produces a record of the right
        ///     length that says something else. All four shapes are covered - 17 alone, 18 alone,
        ///     both, and neither - because the both case is the only one where turning the flag off
        ///     changes the length by two.
        /// </remarks>
        [RealCacheFact]
        public void WalkableRestoresWhicheverBlockingOpcodeTheFileCarried() {
            RSCache cache = _cache.OpenCache();
            var descriptor = new ObjectListDescriptor();

            foreach (WalkShape shape in WalkShapes()) {
                (byte[] stored, ObjectDefinition row, string how) = FindObject(cache, descriptor, shape);

                int carried = Occurrences(row, 17) + Occurrences(row, 18);
                Assert.Equal(shape.Expected17 + shape.Expected18, carried);
                Assert.Equal(carried == 0, row.walkable);

                //Off: every blocking opcode goes, or the tile is still blocked.
                row.walkable = true;
                byte[] cleared = descriptor.Encode(row).ToArray();
                Assert.Equal(stored.Length - carried, cleared.Length);
                ObjectDefinition afterClear = Decode(cache, descriptor, cleared);
                Assert.True(afterClear.walkable);
                Assert.Equal(0, Occurrences(afterClear, 17) + Occurrences(afterClear, 18));

                //On again: the same opcodes, in the same places.
                row.walkable = false;
                byte[] restored = descriptor.Encode(row).ToArray();

                if (carried > 0) {
                    Assert.Equal(stored, restored);
                }
                else {
                    //A record that carried neither has no stored answer, so one is invented. 18 is
                    //the narrower claim: it blocks the tile without also resetting the clip type.
                    Assert.Equal(stored.Length + 1, restored.Length);
                    ObjectDefinition invented = Decode(cache, descriptor, restored);
                    Assert.Equal(0, Occurrences(invented, 17));
                    Assert.Equal(1, Occurrences(invented, 18));
                }

                ObjectDefinition afterRestore = Decode(cache, descriptor, restored);
                Assert.False(afterRestore.walkable);
                Assert.Equal(restored, descriptor.Encode(afterRestore).ToArray());

                _output.WriteLine($"walkable on a record carrying {shape.Name}: {stored.Length} -> " +
                                  $"{cleared.Length} -> {restored.Length} bytes ({how})");
            }
        }

        /// <summary>
        ///     mainOptionIndex reports the value the encoder actually writes, after every edit.
        /// </summary>
        /// <remarks>
        ///     Its getter is the only one in either codec that consults the recorded stream <b>by
        ///     index</b>, so it is the only one that suppression can put out of step with the
        ///     encoder. A getter that disagrees with the bytes misrepresents the record: the grid
        ///     shows a value the file does not hold, and the next edit is made against a reading
        ///     that was already wrong. Checked by decoding what the encoder produced and asking the
        ///     decoded record - never by asking the same object twice.
        /// </remarks>
        [RealCacheFact]
        public void MainOptionIndexAgreesWithTheBytesTheEncoderWrites() {
            RSCache cache = _cache.OpenCache();
            var descriptor = new NPCListDescriptor();

            foreach (OptionShape shape in OptionShapes()) {
                foreach (byte target in new byte[] { 0, 1 }) {
                    (byte[] stored, NPCDefinition row, string how) = FindNpc(cache, descriptor, shape);

                    row.mainOptionIndex = target;
                    byte[] edited = descriptor.Encode(row).ToArray();
                    NPCDefinition decoded = Decode(cache, descriptor, edited);

                    Assert.Equal(target, row.mainOptionIndex);
                    Assert.Equal(target, decoded.mainOptionIndex);

                    //The value has to be what the client would read, which is decided by whichever
                    //of the two opcodes comes last, not by which of them is present.
                    Assert.Equal(target, LastWins(decoded));

                    _output.WriteLine($"mainOptionIndex := {target} on a record carrying {shape.Name}: " +
                                      $"{stored.Length} -> {edited.Length} bytes, reads back {decoded.mainOptionIndex} ({how})");
                }
            }
        }

        /// <summary>
        ///     mainOptionIndex set and set back lands on the original stored bytes.
        /// </summary>
        /// <remarks>
        ///     The assertion an asymmetric setter cannot pass, applied to a pair rather than a lone
        ///     flag. Both directions are exercised on every shape the format allows, so a setter
        ///     that only knows how to restore the opcode it dropped fails on the record that
        ///     carried the other one.
        /// </remarks>
        [RealCacheFact]
        public void MainOptionIndexSetAndSetBackLandsOnTheOriginalStoredBytes() {
            RSCache cache = _cache.OpenCache();
            var descriptor = new NPCListDescriptor();

            foreach (OptionShape shape in OptionShapes()) {
                (byte[] stored, NPCDefinition row, string how) = FindNpc(cache, descriptor, shape);

                byte original = row.mainOptionIndex;
                byte other = original == 0 ? (byte) 1 : (byte) 0;

                row.mainOptionIndex = other;
                byte[] moved = descriptor.Encode(row).ToArray();
                Assert.NotEqual(stored, moved);

                row.mainOptionIndex = original;
                byte[] returned = descriptor.Encode(row).ToArray();

                Assert.Equal(stored, returned);
                _output.WriteLine($"mainOptionIndex round trip {original} -> {other} -> {original} on a record " +
                                  $"carrying {shape.Name}: {returned.Length} identical bytes ({how})");
            }
        }

        /// <summary>
        ///     Which of 158 and 159 the client would obey, worked out without the property.
        /// </summary>
        /// <remarks>
        ///     A second reading of the same question, so a getter and this cannot both be wrong in
        ///     the same direction by construction. The client applies each opcode as it reads it, so
        ///     the last one in the stream decides.
        /// </remarks>
        private static byte LastWins(NPCDefinition definition) {
            byte value = 0;
            OpcodeStream stream = definition.Opcodes;
            for (int i = 0; i < stream.Count; i++) {
                if (stream[i].Opcode == 158) value = 1;
                if (stream[i].Opcode == 159) value = 0;
            }
            return value;
        }

        /// <summary>The four ways a record can spell walkability.</summary>
        private static IEnumerable<WalkShape> WalkShapes() {
            yield return new WalkShape("17 alone", 1, 0);
            yield return new WalkShape("18 alone", 0, 1);
            yield return new WalkShape("both 17 and 18", 1, 1);
            yield return new WalkShape("neither", 0, 0);
        }

        /// <summary>The four ways a record can spell the main option index.</summary>
        /// <remarks>
        ///     Two of them exist in neither cache, since no NPC carries 158 at all, and are built.
        ///     The both-present shape is built with 159 last, which is the order that makes the
        ///     stored value 0 while a naive setter would still see a 158 in the stream.
        /// </remarks>
        private static IEnumerable<OptionShape> OptionShapes() {
            yield return new OptionShape("159 alone", false, true);
            yield return new OptionShape("neither", false, false);
            yield return new OptionShape("158 alone", true, false);
            yield return new OptionShape("both 158 and 159", true, true);
        }

        private (byte[] Stored, ObjectDefinition Row, string How) FindObject(
            RSCache cache, ObjectListDescriptor descriptor, WalkShape shape) {
            OpcodeFlagSurvey survey = OpcodeFlagSurvey.For(cache, descriptor);

            //Candidates come from whichever opcode the shape needs; the other is then checked on
            //the decoded record, because the survey indexes one opcode at a time.
            IReadOnlyList<DefinitionAddress> candidates =
                shape.Expected17 == 1 ? survey.Carrying(17)
                : shape.Expected18 == 1 ? survey.Carrying(18)
                : survey.Lacking(17);

            foreach (DefinitionAddress address in Widen(cache, descriptor, candidates, shape)) {
                byte[] stored = cache.ReadFileBytes(descriptor.IndexId, address.GroupId, address.FileId);
                ObjectDefinition row = descriptor.Decode(cache, address, new JagStream(stored));

                if (Occurrences(row, 17) != shape.Expected17 || Occurrences(row, 18) != shape.Expected18)
                    continue;
                if (!descriptor.Encode(row).ToArray().SequenceEqual(stored))
                    continue;

                return (stored, descriptor.Decode(cache, address, new JagStream(stored)), "found at " + address);
            }

            Assert.Fail($"No object in this cache carries {shape.Name} and re-encodes to its stored bytes.");
            return default;
        }

        /// <summary>
        ///     Candidate addresses for a walkability shape, widened by a scan when the shape is rare.
        /// </summary>
        /// <remarks>
        ///     Only 7 objects of 56,199 carry both 17 and 18 and they sit together at ids
        ///     48011-48017, so the survey's first few carriers of either opcode never include one.
        ///     Rather than name those ids - which would be a figure about one cache written into a
        ///     test - the index is scanned for the shape when the cheap candidates do not hold it.
        /// </remarks>
        private static IEnumerable<DefinitionAddress> Widen(RSCache cache, ObjectListDescriptor descriptor,
            IReadOnlyList<DefinitionAddress> candidates, WalkShape shape) {
            foreach (DefinitionAddress address in candidates)
                yield return address;

            foreach (IGrouping<int, DefinitionAddress> group in
                     descriptor.Enumerate(cache).GroupBy(address => address.GroupId)) {
                IReadOnlyDictionary<int, JagStream> files = cache.ReadGroup(descriptor.IndexId, group.Key);
                foreach (DefinitionAddress address in group) {
                    if (!files.TryGetValue(address.FileId, out JagStream payload))
                        continue;

                    ObjectDefinition row = descriptor.Decode(cache, address, payload);
                    if (Occurrences(row, 17) == shape.Expected17 && Occurrences(row, 18) == shape.Expected18)
                        yield return address;
                }
            }
        }

        private (byte[] Stored, NPCDefinition Row, string How) FindNpc(
            RSCache cache, NPCListDescriptor descriptor, OptionShape shape) {
            OpcodeFlagSurvey survey = OpcodeFlagSurvey.For(cache, descriptor);

            IReadOnlyList<DefinitionAddress> candidates = shape.Has159 ? survey.Carrying(159) : survey.Lacking(159);
            DefinitionAddress seed = default;
            bool haveSeed = false;

            foreach (DefinitionAddress address in candidates) {
                byte[] stored = cache.ReadFileBytes(descriptor.IndexId, address.GroupId, address.FileId);
                NPCDefinition row = descriptor.Decode(cache, address, new JagStream(stored));

                if (!descriptor.Encode(row).ToArray().SequenceEqual(stored))
                    continue;

                if (!haveSeed) {
                    seed = address;
                    haveSeed = true;
                }

                if (Occurrences(row, 158) == (shape.Has158 ? 1 : 0) && Occurrences(row, 159) == (shape.Has159 ? 1 : 0))
                    return (stored, descriptor.Decode(cache, address, new JagStream(stored)), "found at " + address);
            }

            Assert.True(haveSeed, "No NPC in this cache re-encoded to its stored bytes.");

            /* Built rather than skipped. No NPC in either cache carries opcode 158, so the two
               shapes that need it exist only if this test makes them - and an untested shape is
               where a codec change goes unnoticed. The bytes are assembled by hand rather than
               through the setter under test, because a setter cannot be used to build the input
               that proves it right. */
            byte[] seedBytes = cache.ReadFileBytes(descriptor.IndexId, seed.GroupId, seed.FileId);
            NPCDefinition seedRow = descriptor.Decode(cache, seed, new JagStream(seedBytes));
            byte[] built = WithoutOptionFlags(descriptor.Encode(seedRow).ToArray(), shape);

            NPCDefinition rebuilt = Decode(cache, descriptor, built);
            Assert.Equal(shape.Has158 ? 1 : 0, Occurrences(rebuilt, 158));
            Assert.Equal(shape.Has159 ? 1 : 0, Occurrences(rebuilt, 159));
            Assert.Equal(built, descriptor.Encode(rebuilt).ToArray());

            return (built, Decode(cache, descriptor, built),
                "built from " + seed + " - no NPC in this cache carries opcode 158");
        }

        /// <summary>
        ///     Rewrites a record's bytes to carry exactly the option flags a shape names.
        /// </summary>
        /// <remarks>
        ///     Byte surgery on the encoded record rather than a call to the property, so the input
        ///     to the tests above owes nothing to the setter they are testing. Both opcodes are bare,
        ///     so removing one is deleting its byte and adding one is inserting a byte before the
        ///     terminator; 159 is written after 158 so that the client's reading of a record
        ///     carrying both is index 0, which is the order that tells a correct setter from one
        ///     that merely drops whichever opcode it was asked about.
        /// </remarks>
        private static byte[] WithoutOptionFlags(byte[] encoded, OptionShape shape) {
            var output = new List<byte>(encoded.Length + 2);
            OpcodeStream stream = new NPCDefinition(new JagStream(encoded)).Opcodes;

            /* Laid back down from the recorded occurrences, which hold each payload verbatim, so
               nothing here has to know any opcode's width. */
            for (int i = 0; i < stream.Count; i++) {
                //The record's own option flags go wholesale; the shape's are appended below, so
                //their order is stated here rather than inherited.
                if (stream[i].Opcode == 158 || stream[i].Opcode == 159)
                    continue;

                output.Add((byte) stream[i].Opcode);
                output.AddRange(stream[i].Payload);
            }

            if (shape.Has158) output.Add(158);
            if (shape.Has159) output.Add(159);
            output.Add(0);
            return output.ToArray();
        }

        private static ObjectDefinition Decode(RSCache cache, ObjectListDescriptor descriptor, byte[] bytes) {
            DefinitionAddress address = descriptor.Enumerate(cache).First();
            return descriptor.Decode(cache, address, new JagStream(bytes));
        }

        private static NPCDefinition Decode(RSCache cache, NPCListDescriptor descriptor, byte[] bytes) {
            DefinitionAddress address = descriptor.Enumerate(cache).First();
            return descriptor.Decode(cache, address, new JagStream(bytes));
        }

        private static int Occurrences(OpcodeStreamDefinition definition, int opcode) {
            OpcodeStream stream = definition.Opcodes;
            int count = 0;
            for (int i = 0; i < stream.Count; i++)
                if (stream[i].Opcode == opcode)
                    count++;
            return count;
        }

        /// <summary>One combination of the two walk-blocking opcodes.</summary>
        private sealed class WalkShape {
            internal WalkShape(string name, int expected17, int expected18) {
                Name = name;
                Expected17 = expected17;
                Expected18 = expected18;
            }

            /// <summary>The shape in words, for the failure message.</summary>
            internal string Name { get; }

            /// <summary>How many times the record carries opcode 17.</summary>
            internal int Expected17 { get; }

            /// <summary>How many times the record carries opcode 18.</summary>
            internal int Expected18 { get; }
        }

        /// <summary>One combination of the two main-option opcodes.</summary>
        private sealed class OptionShape {
            internal OptionShape(string name, bool has158, bool has159) {
                Name = name;
                Has158 = has158;
                Has159 = has159;
            }

            /// <summary>The shape in words, for the failure message.</summary>
            internal string Name { get; }

            /// <summary>Whether the record carries opcode 158.</summary>
            internal bool Has158 { get; }

            /// <summary>Whether the record carries opcode 159.</summary>
            internal bool Has159 { get; }
        }
    }
}
