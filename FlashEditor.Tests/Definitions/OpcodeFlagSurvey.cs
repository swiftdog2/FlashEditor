using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using FlashEditor.cache;
using FlashEditor.Definitions;
using FlashEditor.Definitions.Editing;

namespace FlashEditor.Tests.Definitions {
    /// <summary>
    ///     Where every opcode of one definition family actually occurs in the loaded cache.
    /// </summary>
    /// <remarks>
    ///     Built by one pass over the whole index rather than by sampling the first few groups.
    ///     The sampled search this replaced could not find a record for a rare opcode and fell back
    ///     to a synthetic one, which is the weaker test and gave no way to tell "this cache has
    ///     none" from "the sample missed it". Measured over index 16, both caches agreeing to the
    ///     record: opcode 97 is carried by 9 objects of 56,199 and opcode 169 by 21, so a
    ///     1,500-record sample finds neither more often than it finds either.
    ///     <para>
    ///     One pass serves every flag and every test method, so the cost is paid once: the survey
    ///     is memoised per descriptor for the lifetime of the test host. It decodes and never
    ///     re-encodes - proving a record re-encodes to its stored bytes is the caller's job,
    ///     because a caller that needs a baseline needs to know which candidate failed.
    ///     </para>
    /// </remarks>
    internal sealed class OpcodeFlagSurvey {
        /// <summary>
        ///     How many addresses are kept per opcode per shape.
        /// </summary>
        /// <remarks>
        ///     More than one, because a candidate is discarded when it does not re-encode to its
        ///     stored bytes and a single candidate would then turn a bad pick into a failure that
        ///     reads like a codec regression.
        /// </remarks>
        private const int CandidatesKept = 4;

        private static readonly ConcurrentDictionary<string, OpcodeFlagSurvey> Surveys =
            new ConcurrentDictionary<string, OpcodeFlagSurvey>();

        private readonly List<DefinitionAddress>[] _carrying = NewLists();
        private readonly List<DefinitionAddress>[] _lacking = NewLists();
        private readonly List<DefinitionAddress>[] _repeating = NewLists();
        private readonly int[] _carriedBy = new int[256];
        private readonly int[] _repeatedBy = new int[256];
        private readonly bool[] _sawPayload = new bool[256];

        private OpcodeFlagSurvey() {
        }

        /// <summary>How many records of the family were examined.</summary>
        internal int RecordsExamined { get; private set; }

        /// <summary>The family this survey walked, for a failure message.</summary>
        internal string RowNoun { get; private set; } = "record";

        /// <summary>
        ///     Surveys an index once, or returns the survey already taken of it.
        /// </summary>
        /// <param name="cache">The open cache.</param>
        /// <param name="descriptor">The family to walk.</param>
        /// <returns>Where each opcode occurs in that family.</returns>
        internal static OpcodeFlagSurvey For(RSCache cache, IDefinitionListDescriptor descriptor) {
            return Surveys.GetOrAdd(descriptor.GetType().FullName ?? descriptor.RowNoun,
                _ => Build(cache, descriptor));
        }

        /// <summary>Addresses of records that carry <paramref name="opcode"/> exactly once.</summary>
        /// <param name="opcode">The opcode to look for.</param>
        /// <returns>Up to <see cref="CandidatesKept"/> addresses, in id order.</returns>
        internal IReadOnlyList<DefinitionAddress> Carrying(int opcode) => _carrying[opcode];

        /// <summary>Addresses of records that do not carry <paramref name="opcode"/> at all.</summary>
        /// <param name="opcode">The opcode to look for.</param>
        /// <returns>Up to <see cref="CandidatesKept"/> addresses, in id order.</returns>
        internal IReadOnlyList<DefinitionAddress> Lacking(int opcode) => _lacking[opcode];

        /// <summary>
        ///     Addresses of records that carry <paramref name="opcode"/> more than once.
        /// </summary>
        /// <remarks>
        ///     A separate shape because a bare flag turned off has to drop <b>every</b> occurrence
        ///     or the flag is still set, so such a record changes length by more than one byte and
        ///     the single-byte assertions do not apply to it.
        /// </remarks>
        /// <param name="opcode">The opcode to look for.</param>
        /// <returns>Up to <see cref="CandidatesKept"/> addresses, in id order.</returns>
        internal IReadOnlyList<DefinitionAddress> Repeating(int opcode) => _repeating[opcode];

        /// <summary>How many records in this cache carry <paramref name="opcode"/>.</summary>
        /// <param name="opcode">The opcode to count.</param>
        /// <returns>The population, which belongs to the loaded cache rather than to build 639.</returns>
        internal int CarriedBy(int opcode) => _carriedBy[opcode];

        /// <summary>How many records in this cache carry <paramref name="opcode"/> more than once.</summary>
        /// <param name="opcode">The opcode to count.</param>
        /// <returns>The population, which belongs to the loaded cache.</returns>
        internal int RepeatedBy(int opcode) => _repeatedBy[opcode];

        /// <summary>
        ///     Whether every occurrence of <paramref name="opcode"/> in this index consumed no payload.
        /// </summary>
        /// <remarks>
        ///     Read off the recorded bytes rather than off a list in the test, so "which opcodes are
        ///     bare flags" is answered by the cache. An opcode carried by nothing reports true and
        ///     is useless on its own, which is why callers pair this with
        ///     <see cref="CarriedBy"/>.
        /// </remarks>
        /// <param name="opcode">The opcode to test.</param>
        /// <returns>Whether it never carried a payload here.</returns>
        internal bool IsAlwaysBare(int opcode) => !_sawPayload[opcode];

        private static List<DefinitionAddress>[] NewLists() {
            var lists = new List<DefinitionAddress>[256];
            for (int i = 0; i < lists.Length; i++)
                lists[i] = new List<DefinitionAddress>();
            return lists;
        }

        private static OpcodeFlagSurvey Build(RSCache cache, IDefinitionListDescriptor descriptor) {
            var survey = new OpcodeFlagSurvey { RowNoun = descriptor.RowNoun };
            var occurrences = new int[256];

            //Grouped, so each archive is inflated once. Walking addresses in file order instead
            //re-decodes the whole group per file - 56,199 group decodes against 224 on index 16.
            foreach (IGrouping<int, DefinitionAddress> group in
                     descriptor.Enumerate(cache).GroupBy(address => address.GroupId)) {
                IReadOnlyDictionary<int, JagStream> files = cache.ReadGroup(descriptor.IndexId, group.Key);

                foreach (DefinitionAddress address in group) {
                    if (!files.TryGetValue(address.FileId, out JagStream payload))
                        continue;

                    object row = descriptor.Decode(cache, address, payload);
                    if (row is not OpcodeStreamDefinition definition)
                        throw new InvalidOperationException(
                            descriptor.RowNoun + " rows are not opcode streams, so they cannot be surveyed here");

                    survey.RecordsExamined++;
                    Array.Clear(occurrences);

                    OpcodeStream stream = definition.Opcodes;
                    for (int i = 0; i < stream.Count; i++) {
                        occurrences[stream[i].Opcode]++;
                        if (!stream[i].IsBareFlag)
                            survey._sawPayload[stream[i].Opcode] = true;
                    }

                    for (int opcode = 0; opcode < occurrences.Length; opcode++) {
                        if (occurrences[opcode] == 0) {
                            Keep(survey._lacking[opcode], address);
                            continue;
                        }

                        survey._carriedBy[opcode]++;
                        if (occurrences[opcode] == 1) {
                            Keep(survey._carrying[opcode], address);
                        }
                        else {
                            survey._repeatedBy[opcode]++;
                            Keep(survey._repeating[opcode], address);
                        }
                    }
                }
            }

            return survey;
        }

        private static void Keep(List<DefinitionAddress> addresses, DefinitionAddress address) {
            if (addresses.Count < CandidatesKept)
                addresses.Add(address);
        }
    }
}
