using System;
using System.Collections.Generic;
using System.Linq;
using FlashEditor.Cache;
using FlashEditor.Definitions.Animation;
using FlashEditor.Tests.Cache.RealCache;
using Xunit;
using Xunit.Abstractions;

namespace FlashEditor.Tests.Definitions.Animation
{
    /// <summary>
    ///     Decodes every animation the index-20 reference table declares, requires exact buffer
    ///     consumption, and requires each one to re-encode to the bytes it came from.
    /// </summary>
    /// <remarks>
    ///     Index 20 is the index where opcode order matters most: over half its records store their
    ///     opcodes out of ascending order, ten different opcodes lead somewhere, and five scalar
    ///     opcodes repeat within a record. It is also the index that makes indexes 0 and 1 reachable
    ///     - index 0 carries no name hashes, so an animation's packed frame ids are the only
    ///     statement anywhere of which frame set is which. That join is asserted here rather than
    ///     assumed, because getting the two halves of the packing the wrong way round produces frame
    ///     ids that look plausible and name nothing.
    /// </remarks>
    [Collection("RealCache")]
    public sealed class RealCacheAnimationTests : IClassFixture<RealCacheFixture>
    {
        private readonly RealCacheFixture _fixture;
        private readonly ITestOutputHelper _output;

        /// <summary>Binds the shared open cache and the per-test output sink.</summary>
        public RealCacheAnimationTests(RealCacheFixture fixture, ITestOutputHelper output)
        {
            _fixture = fixture;
            _output = output;
        }

        /// <summary>Groups the index-20 reference table declares.</summary>
        private int GroupsDeclared => _fixture.DeclaredGroups(RSConstants.ANIMATIONS_INDEX);

        /// <summary>Files the index-20 reference table declares across every group.</summary>
        private int AnimationsDeclared => _fixture.DeclaredFiles(RSConstants.ANIMATIONS_INDEX);

        /// <summary>The animation index bound to the production codec.</summary>
        /// <returns>A sweep over every declared animation.</returns>
        private DefinitionSweep<AnimationDefinition> Sweep()
        {
            return new DefinitionSweep<AnimationDefinition>(_fixture, _output, RSConstants.ANIMATIONS_INDEX,
                new DefinitionCodec<AnimationDefinition>("animation",
                    (id, stream) => new AnimationDefinition { Id = id }.Decode(stream),
                    definition => definition.Encode(),
                    definition => definition.Opcodes.Select(record => record.Opcode)))
                .AcrossEveryGroup();
        }

        /// <summary>Every declared animation decodes and finishes on the last byte of its file.</summary>
        [RealCacheFact]
        public void EveryAnimation_DecodesAndConsumesItsBufferExactly()
        {
            DefinitionSweepResult swept = Sweep().AssertExactConsumption();

            Assert.True(AnimationsDeclared > 0, "index 20 declares no files, so nothing was checked");
            Assert.Equal(GroupsDeclared, swept.Groups);
            Assert.Equal(AnimationsDeclared, swept.Records);
            Assert.Equal(AnimationsDeclared, swept.Passed);
        }

        /// <summary>Every declared animation re-encodes to the bytes it was decoded from.</summary>
        [RealCacheFact]
        public void EveryAnimation_ReEncodesToTheCapturedBytes()
        {
            DefinitionSweepResult swept = Sweep().AssertReEncodesToCapturedBytes();

            Assert.True(AnimationsDeclared > 0, "index 20 declares no files, so nothing was checked");
            Assert.Equal(AnimationsDeclared, swept.Records);
            Assert.Equal(AnimationsDeclared, swept.Passed);
            Assert.Equal(0, swept.Reordered);
        }

        /// <summary>The encoder's own output decodes back to something that encodes identically.</summary>
        [RealCacheFact]
        public void EveryAnimation_EncodeIsAFixedPointOfDecode()
        {
            Sweep().AssertEncodeIsAFixedPointOfDecode();
        }

        /// <summary>
        ///     The stored opcode order is not canonical, and neither is opcode repetition.
        /// </summary>
        /// <remarks>
        ///     The byte-identity sweep already fails when either is dropped, but it fails as "1,400
        ///     definitions differ" rather than as a statement of why. This says what the encoder is
        ///     defending against, and would go green on a cache where it no longer had to - which is
        ///     the honest failure mode for a claim about content.
        /// </remarks>
        [RealCacheFact]
        public void TheStoredOpcodeOrderIsNeitherAscendingNorFreeOfRepeats()
        {
            int outOfOrder = 0;
            int repeating = 0;
            int withOpcode16 = 0;
            var sequences = new HashSet<string>();
            var leading = new SortedDictionary<int, int>();

            DefinitionSweepResult swept = Sweep().ForEachDecoded((record, definition) =>
            {
                int[] opcodes = definition.Opcodes.Select(entry => entry.Opcode).ToArray();

                sequences.Add(string.Join(",", opcodes));
                if (opcodes.Length > 0)
                {
                    leading.TryGetValue(opcodes[0], out int seen);
                    leading[opcodes[0]] = seen + 1;
                }
                if (!opcodes.SequenceEqual(opcodes.OrderBy(opcode => opcode)))
                    outOfOrder++;
                if (opcodes.Length != opcodes.Distinct().Count())
                    repeating++;
                if (definition.TweensAcrossCachedFrames)
                    withOpcode16++;
            });

            _output.WriteLine($"{outOfOrder} of {swept.Records} animations are not in ascending opcode " +
                              $"order, {repeating} repeat an opcode, across {sequences.Count} distinct " +
                              "sequences");
            _output.WriteLine("leading opcode: " +
                              string.Join(", ", leading.Select(entry => entry.Key + "=" + entry.Value)));

            Assert.Equal(AnimationsDeclared, swept.Records);
            Assert.True(outOfOrder > 0,
                "no animation stores its opcodes out of ascending order, so this cache cannot show " +
                "that the recorded order is needed");
            Assert.True(repeating > 0,
                "no animation repeats an opcode, so this cache cannot show that the earlier " +
                "occurrence has to be replayed from its own bytes");
            Assert.True(leading.Count > 1, "every animation leads with the same opcode");

            _fixture.Profile.AssertCensus(_output, "animation.recordsOutOfAscendingOpcodeOrder", outOfOrder);
            _fixture.Profile.AssertCensus(_output, "animation.recordsRepeatingAnOpcode", repeating);
            _fixture.Profile.AssertCensus(_output, "animation.distinctOpcodeSequences", sequences.Count);
            _fixture.Profile.AssertCensus(_output, "animation.recordsWithOpcode16", withOpcode16);
        }

        /// <summary>
        ///     Every frame an animation names resolves to a file index 0 actually declares.
        /// </summary>
        /// <remarks>
        ///     This is the self-proving half of the index-0 join, and the reason it is worth more
        ///     than any coverage figure: the packed id is <c>(frameSetGroup &lt;&lt; 16) |
        ///     frameIndex</c>, so reading the two halves the wrong way round yields group ids in the
        ///     tens of thousands and frame indexes that are really group ids. Either mistake misses
        ///     almost every lookup, and getting it right cannot happen by accident across hundreds of
        ///     thousands of references.
        ///     <para>
        ///     Opcode 12's secondary table is measured rather than asserted: a handful of its
        ///     references name frames index 0 does not declare, and the client bounds-checks that
        ///     path (Class97.java:309, 311) rather than relying on it.
        ///     </para>
        /// </remarks>
        [RealCacheFact]
        public void EveryFrameReferenceNamesAFileIndexZeroDeclares()
        {
            Dictionary<int, HashSet<int>> frameFiles = DeclaredFrameFiles();

            int resolved = 0;
            int unresolved = 0;
            int secondaryResolved = 0;
            int secondaryUnresolved = 0;
            var frameSets = new HashSet<int>();
            var missing = new List<string>();

            DefinitionSweepResult swept = Sweep().ForEachDecoded((record, definition) =>
            {
                foreach (int packed in definition.FrameIds)
                {
                    int group = AnimationDefinition.FrameGroupOf(packed);
                    frameSets.Add(group);

                    if (Declares(frameFiles, packed))
                    {
                        resolved++;
                        continue;
                    }

                    unresolved++;
                    if (missing.Count < 10)
                    {
                        missing.Add($"animation {definition.Id} names frame set {group} frame " +
                                    $"{AnimationDefinition.FrameIndexOf(packed)}, which index 0 does not declare");
                    }
                }

                foreach (int packed in definition.SecondaryFrameIds)
                {
                    if (Declares(frameFiles, packed))
                        secondaryResolved++;
                    else
                        secondaryUnresolved++;
                }
            });

            _output.WriteLine($"{resolved} frame references across {swept.Records} animations resolve to " +
                              $"a declared index-0 file, naming {frameSets.Count} frame sets");
            _output.WriteLine($"opcode 12's secondary table: {secondaryResolved} resolve, " +
                              $"{secondaryUnresolved} name a frame index 0 does not declare");

            Assert.True(resolved > 0, "no animation named a frame, so the packing was never exercised");
            Assert.True(secondaryResolved > 0,
                "no secondary frame reference resolved, so opcode 12's packing was never exercised");
            Assert.True(unresolved == 0,
                $"{unresolved} frame references name nothing in index 0:{Environment.NewLine}" +
                string.Join(Environment.NewLine, missing));

            _fixture.Profile.AssertCensus(_output, "animation.frameSetsNamed", frameSets.Count);
            _fixture.Profile.AssertCensus(_output, "animation.secondaryFrameReferencesNotDeclared",
                secondaryUnresolved);
        }

        /// <summary>
        ///     The two interrupt fields are stored as they were read, not as the client derives them.
        /// </summary>
        /// <remarks>
        ///     <c>Class97.method938</c> rewrites both from -1 after every load. Folding that into the
        ///     decoded fields would make the encoder write opcodes 9 and 10 into records that never
        ///     carried them, which the byte-identity sweep catches - but only while records without
        ///     those opcodes exist. This states the requirement directly and reports how much of the
        ///     index depends on it.
        /// </remarks>
        [RealCacheFact]
        public void TheDerivedInterruptFieldsNeverReachTheStoredOnes()
        {
            int unstated = 0;
            int derivedTwo = 0;
            int stated = 0;

            DefinitionSweepResult swept = Sweep().ForEachDecoded((record, definition) =>
            {
                //The client's rule, restated here so a decoder that quietly applied it at decode
                //time - and would therefore write the opcode back - fails on the stored field.
                int fallback = definition.Opcodes.Has(3)
                    ? AnimationDefinition.BlendedInterruptBehaviour
                    : 0;

                Assert.Equal(definition.MovingInterrupt < 0 ? fallback : definition.MovingInterrupt,
                    definition.EffectiveMovingInterrupt);
                Assert.Equal(definition.StationaryInterrupt < 0 ? fallback : definition.StationaryInterrupt,
                    definition.EffectiveStationaryInterrupt);

                if (definition.MovingInterrupt < 0)
                {
                    unstated++;
                    if (fallback == AnimationDefinition.BlendedInterruptBehaviour)
                        derivedTwo++;
                }
                else
                {
                    stated++;
                }
            });

            _output.WriteLine($"{stated} animations state opcode 9, {unstated} leave it to the client's " +
                              $"post-decode pass, {derivedTwo} of which derive a 2 from opcode 3");

            Assert.Equal(AnimationsDeclared, swept.Records);
            Assert.True(stated > 0, "no animation stores opcode 9, so the stored branch is untested");
            Assert.True(unstated > 0, "every animation stores opcode 9, so the derived branch is untested");
            Assert.True(derivedTwo > 0,
                "no animation derives a non-zero interrupt, so only one arm of the rule is covered");
        }

        /// <summary>Every file id in index 0, grouped by the group that holds it.</summary>
        /// <returns>Declared file ids per index-0 group.</returns>
        private Dictionary<int, HashSet<int>> DeclaredFrameFiles()
        {
            var declared = new Dictionary<int, HashSet<int>>();
            RSReferenceTable frames = _fixture.Table(RSConstants.FRAMES_INDEX);

            foreach (KeyValuePair<int, RSArchiveEntry> entry in frames.GetArchiveEntries())
                declared[entry.Key] = new HashSet<int>(entry.Value.GetValidFileIds());

            return declared;
        }

        /// <summary>Whether index 0 declares the file a packed frame id names.</summary>
        /// <param name="declared">Declared file ids per index-0 group.</param>
        /// <param name="packedFrameId">The packed id stored by the animation.</param>
        /// <returns>Whether the frame exists.</returns>
        private static bool Declares(Dictionary<int, HashSet<int>> declared, int packedFrameId)
        {
            return declared.TryGetValue(AnimationDefinition.FrameGroupOf(packedFrameId), out HashSet<int> files)
                   && files.Contains(AnimationDefinition.FrameIndexOf(packedFrameId));
        }
    }
}
