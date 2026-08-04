using System;
using System.Collections.Generic;
using System.Linq;
using FlashEditor.cache;
using FlashEditor.Definitions.SpotAnims;
using FlashEditor.Tests.Cache.RealCache;
using Xunit;
using Xunit.Abstractions;

namespace FlashEditor.Tests.Definitions
{
    /// <summary>
    ///     Decodes every spot animation the index-21 reference table declares, requires exact buffer
    ///     consumption, and requires each one to re-encode to the bytes it came from.
    /// </summary>
    /// <remarks>
    ///     A cheap index to sweep - twelve GZip groups holding under 32 KB of payload between them -
    ///     so nothing here samples. What it cannot cover is the effect opcode group: eight opcodes
    ///     set the same two fields and not one of them occurs in either cache, so they are pinned by
    ///     synthetic cases in <see cref="GraphicDefinitionCodecTests"/> instead. The measurement
    ///     below reports the population so the day one appears is visible rather than silent.
    /// </remarks>
    [Collection("RealCache")]
    public sealed class RealCacheGraphicTests : IClassFixture<RealCacheFixture>
    {
        private readonly RealCacheFixture _fixture;
        private readonly ITestOutputHelper _output;

        /// <summary>Binds the shared open cache and the per-test output sink.</summary>
        public RealCacheGraphicTests(RealCacheFixture fixture, ITestOutputHelper output)
        {
            _fixture = fixture;
            _output = output;
        }

        /// <summary>Groups the index-21 reference table declares.</summary>
        private int GroupsDeclared => _fixture.DeclaredGroups(RSConstants.GRAPHICS_INDEX);

        /// <summary>Files the index-21 reference table declares across every group.</summary>
        private int GraphicsDeclared => _fixture.DeclaredFiles(RSConstants.GRAPHICS_INDEX);

        /// <summary>The spot-animation index bound to the production codec.</summary>
        /// <returns>A sweep over every declared graphic.</returns>
        private DefinitionSweep<GraphicDefinition> Sweep()
        {
            return new DefinitionSweep<GraphicDefinition>(_fixture, _output, RSConstants.GRAPHICS_INDEX,
                new DefinitionCodec<GraphicDefinition>("graphic",
                    (id, stream) => new GraphicDefinition { Id = id }.Decode(stream),
                    definition => definition.Encode(),
                    definition => definition.Opcodes.Select(record => record.Opcode)))
                .AcrossEveryGroup();
        }

        /// <summary>Every declared graphic decodes and finishes on the last byte of its file.</summary>
        [RealCacheFact]
        public void EveryGraphic_DecodesAndConsumesItsBufferExactly()
        {
            DefinitionSweepResult swept = Sweep().AssertExactConsumption();

            Assert.True(GraphicsDeclared > 0, "index 21 declares no files, so nothing was checked");
            Assert.Equal(GroupsDeclared, swept.Groups);
            Assert.Equal(GraphicsDeclared, swept.Records);
            Assert.Equal(GraphicsDeclared, swept.Passed);
        }

        /// <summary>Every declared graphic re-encodes to the bytes it was decoded from.</summary>
        [RealCacheFact]
        public void EveryGraphic_ReEncodesToTheCapturedBytes()
        {
            DefinitionSweepResult swept = Sweep().AssertReEncodesToCapturedBytes();

            Assert.True(GraphicsDeclared > 0, "index 21 declares no files, so nothing was checked");
            Assert.Equal(GraphicsDeclared, swept.Records);
            Assert.Equal(GraphicsDeclared, swept.Passed);
            Assert.Equal(0, swept.Reordered);
        }

        /// <summary>The encoder's own output decodes back to something that encodes identically.</summary>
        [RealCacheFact]
        public void EveryGraphic_EncodeIsAFixedPointOfDecode()
        {
            Sweep().AssertEncodeIsAFixedPointOfDecode();
        }

        /// <summary>
        ///     The stored opcode order is not canonical, and the first record of the index proves it.
        /// </summary>
        /// <remarks>
        ///     Graphic 0 stores the animation before the model. That one record is enough to
        ///     falsify an ascending-order encoder, which is worth naming separately from the
        ///     population figure: a count can shrink to zero on some other cache while the claim
        ///     stays true of this one.
        /// </remarks>
        [RealCacheFact]
        public void TheStoredOpcodeOrderIsNotAscending()
        {
            int outOfOrder = 0;
            int repeating = 0;
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
            });

            _output.WriteLine($"{outOfOrder} of {swept.Records} graphics are not in ascending opcode " +
                              $"order, {repeating} repeat an opcode, across {sequences.Count} distinct " +
                              "sequences");
            _output.WriteLine("leading opcode: " +
                              string.Join(", ", leading.Select(entry => entry.Key + "=" + entry.Value)));

            Assert.Equal(GraphicsDeclared, swept.Records);
            Assert.True(outOfOrder > 0,
                "no graphic stores its opcodes out of ascending order, so this cache cannot show " +
                "that the recorded order is needed");

            _fixture.Profile.AssertCensus(_output, "graphic.recordsOutOfAscendingOpcodeOrder", outOfOrder);
            _fixture.Profile.AssertCensus(_output, "graphic.recordsRepeatingAnOpcode", repeating);
            _fixture.Profile.AssertCensus(_output, "graphic.distinctOpcodeSequences", sequences.Count);
        }

        /// <summary>
        ///     Every model and animation a graphic names is declared by the index that holds it.
        /// </summary>
        /// <remarks>
        ///     A self-proving join in both directions: opcode 1 is an index-7 <em>group</em> id
        ///     (Node_Sub6.java:59-66 fetches it as <c>getChildFromFolder(modelId, 0)</c>) and opcode
        ///     2 is a folded index-20 animation id. Reading either at the wrong width or through the
        ///     wrong split would leave most of them naming nothing.
        /// </remarks>
        [RealCacheFact]
        public void EveryModelAndAnimationAGraphicNamesExists()
        {
            var models = new HashSet<int>(_fixture.Table(RSConstants.MODELS_INDEX).GetArchiveEntries().Keys);
            HashSet<int> animations = DeclaredAnimationIds();

            int modelsResolved = 0;
            int animationsResolved = 0;
            int withoutAnimation = 0;
            var missing = new List<string>();

            DefinitionSweepResult swept = Sweep().ForEachDecoded((record, definition) =>
            {
                if (models.Contains(definition.ModelId))
                    modelsResolved++;
                else if (missing.Count < 10)
                    missing.Add($"graphic {definition.Id} names model {definition.ModelId}, " +
                                "which index 7 does not declare");

                if (definition.AnimationId < 0)
                {
                    withoutAnimation++;
                    return;
                }

                if (animations.Contains(definition.AnimationId))
                    animationsResolved++;
                else if (missing.Count < 10)
                    missing.Add($"graphic {definition.Id} names animation {definition.AnimationId}, " +
                                "which index 20 does not declare");
            });

            _output.WriteLine($"{modelsResolved} of {swept.Records} graphics name a declared model; " +
                              $"{animationsResolved} name a declared animation and {withoutAnimation} " +
                              "name none");

            Assert.True(modelsResolved > 0, "no graphic named a model, so opcode 1 was never exercised");
            Assert.True(animationsResolved > 0,
                "no graphic named an animation, so opcode 2 was never exercised");
            Assert.True(missing.Count == 0,
                "graphics name records their index does not declare:" + Environment.NewLine +
                string.Join(Environment.NewLine, missing));
            Assert.Equal(swept.Records, modelsResolved);
            Assert.Equal(swept.Records, animationsResolved + withoutAnimation);
        }

        /// <summary>
        ///     Which of the optional blocks this cache actually exercises.
        /// </summary>
        /// <remarks>
        ///     The effect opcodes and the retexture table occur nowhere, which is the point of
        ///     recording it: the byte-identity sweep is silent about a branch no record reaches, so
        ///     a reader who sees it pass must not read that as coverage of those opcodes.
        /// </remarks>
        [RealCacheFact]
        public void TheEffectAndRetextureBranchesAreUnreachedByThisCache()
        {
            int withEffect = 0;
            int withRecolours = 0;
            int withRetextures = 0;
            int respectingMovement = 0;
            int rotationsIgnored = 0;
            int scalesAtTheDefault = 0;

            DefinitionSweepResult swept = Sweep().ForEachDecoded((record, definition) =>
            {
                if (definition.EffectOpcode != GraphicDefinition.NoEffectOpcode)
                    withEffect++;
                if (definition.RecolourFrom.Length > 0)
                    withRecolours++;
                if (definition.RetextureFrom.Length > 0)
                    withRetextures++;
                if (definition.RespectsMovementInterrupt)
                    respectingMovement++;
                if (definition.Rotation != 0 && !definition.RotationIsApplied)
                    rotationsIgnored++;

                //A record that stores a field at exactly the value an absent opcode would give is
                //the absent-versus-default case, and it has to keep the opcode.
                if ((definition.Opcodes.Has(4) && definition.ScaleXZ == GraphicDefinition.DefaultScale) ||
                    (definition.Opcodes.Has(5) && definition.ScaleY == GraphicDefinition.DefaultScale) ||
                    (definition.Opcodes.Has(7) && definition.Ambient == 0) ||
                    (definition.Opcodes.Has(8) && definition.Contrast == 0))
                    scalesAtTheDefault++;
            });

            _output.WriteLine($"of {swept.Records} graphics: {withEffect} carry an effect opcode, " +
                              $"{withRecolours} recolour, {withRetextures} retexture, " +
                              $"{respectingMovement} respect movement, {rotationsIgnored} store a " +
                              $"rotation the client ignores, {scalesAtTheDefault} store a field at " +
                              "its own default");

            Assert.Equal(GraphicsDeclared, swept.Records);
            Assert.True(scalesAtTheDefault > 0,
                "no graphic stores a field at its own default, so the absent-versus-default branch " +
                "is untested by this cache");

            _fixture.Profile.AssertCensus(_output, "graphic.recordsWithAnEffectOpcode", withEffect);
            _fixture.Profile.AssertCensus(_output, "graphic.recordsWithRecolours", withRecolours);
            _fixture.Profile.AssertCensus(_output, "graphic.recordsRespectingMovement", respectingMovement);
        }

        /// <summary>Every animation id index 20 declares, folded the way a graphic stores it.</summary>
        /// <returns>The declared animation ids.</returns>
        private HashSet<int> DeclaredAnimationIds()
        {
            var ids = new HashSet<int>();
            CacheAddressing addressing = CacheAddressing.For(RSConstants.ANIMATIONS_INDEX);
            RSReferenceTable animations = _fixture.Table(RSConstants.ANIMATIONS_INDEX);

            foreach (KeyValuePair<int, RSArchiveEntry> entry in animations.GetArchiveEntries())
                foreach (int fileId in entry.Value.GetValidFileIds())
                    ids.Add(addressing.DefinitionId(entry.Key, fileId));

            return ids;
        }
    }
}
