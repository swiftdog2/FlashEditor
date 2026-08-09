using System;
using System.Linq;
using FlashEditor.Cache;
using FlashEditor.Definitions;
using FlashEditor.Tests.Cache.RealCache;
using Xunit;
using Xunit.Abstractions;
using FlashEditor.IO;

namespace FlashEditor.Tests.Cache
{
    /// <summary>
    ///     Decodes every floor underlay and overlay in the real cache, requires exact buffer
    ///     consumption, and requires each one to re-encode to the bytes it came from.
    /// </summary>
    /// <remarks>
    ///     Both formats are opcode streams with no length prefix, so a wrong field width or an
    ///     unhandled opcode desynchronises the parse. Exact consumption catches that. The round trip
    ///     catches the separate problem of a decoder that reads correctly but cannot write back -
    ///     which matters because the archive CRC is taken over the stored bytes, so an encoder that
    ///     reorders opcodes or normalises a value silently invalidates every definition it touches.
    ///
    ///     See <c>reference/hydra-637-maps/04-floor-definitions.md</c>.
    /// </remarks>
    [Collection("RealCache")]
    public sealed class RealCacheFloorDefinitionTests : IClassFixture<RealCacheFixture>
    {
        private readonly RealCacheFixture _fixture;
        private readonly ITestOutputHelper _output;

        /// <summary>Binds the shared open cache and the per-test output sink.</summary>
        public RealCacheFloorDefinitionTests(RealCacheFixture fixture, ITestOutputHelper output)
        {
            _fixture = fixture;
            _output = output;
        }

        /// <summary>
        ///     The underlay family, which is group 1 of the config index.
        /// </summary>
        /// <remarks>
        ///     Both floor families live in index 2, which holds thirty-five unrelated config
        ///     families and so has no index-wide id split: the definition id is the file id within
        ///     the family's group. That is <see cref="CacheAddressing.SingleGroup"/> applied to one
        ///     group of a shared index, which is what <c>WithinGroup</c> asks the sweep for.
        /// </remarks>
        /// <returns>A sweep over every floor underlay.</returns>
        private DefinitionSweep<FloorUnderlayDefinition> Underlays()
        {
            return new DefinitionSweep<FloorUnderlayDefinition>(_fixture, _output, RSConstants.CONFIG,
                new DefinitionCodec<FloorUnderlayDefinition>("underlay",
                    (id, stream) =>
                    {
                        var definition = new FloorUnderlayDefinition { Id = id };
                        definition.Decode(stream);
                        return definition;
                    },
                    definition => definition.Encode(),
                    definition => definition.DecodedOpcodes.Select(entry => entry.Opcode)))
                .WithinGroup(RSConstants.FLOOR_UNDERLAY_GROUP);
        }

        /// <summary>The overlay family, which is group 4 of the config index.</summary>
        /// <returns>A sweep over every floor overlay.</returns>
        private DefinitionSweep<FloorOverlayDefinition> Overlays()
        {
            return new DefinitionSweep<FloorOverlayDefinition>(_fixture, _output, RSConstants.CONFIG,
                new DefinitionCodec<FloorOverlayDefinition>("overlay",
                    (id, stream) =>
                    {
                        var definition = new FloorOverlayDefinition { Id = id };
                        definition.Decode(stream);
                        return definition;
                    },
                    definition => definition.Encode(),
                    definition => definition.DecodedOpcodes.Select(entry => entry.Opcode)))
                .WithinGroup(RSConstants.FLOOR_OVERLAY_GROUP);
        }

        [RealCacheFact]
        public void EveryUnderlayDecodesAndRoundTrips()
        {
            DefinitionSweep<FloorUnderlayDefinition> sweep = Underlays();

            sweep.AssertExactConsumption();
            DefinitionSweepResult swept = sweep.AssertReEncodesToCapturedBytes();

            Assert.Equal(159, swept.Records);
        }

        [RealCacheFact]
        public void EveryOverlayDecodesAndRoundTrips()
        {
            DefinitionSweep<FloorOverlayDefinition> sweep = Overlays();

            sweep.AssertExactConsumption();
            DefinitionSweepResult swept = sweep.AssertReEncodesToCapturedBytes();

            Assert.Equal(235, swept.Records);

            int worldMapBackgrounds = 0;
            int transparent = 0;
            sweep.ForEachDecoded((record, def) =>
            {
                if (def.IsWorldMapBackground)
                    worldMapBackgrounds++;
                if (def.PrimaryRgb == FloorOverlayDefinition.TransparentRgb)
                    transparent++;
            });

            //Exactly one definition claims the world-map background slot. More than one would mean
            //the last decoded wins, which would change what the world map paints.
            Assert.Equal(1, worldMapBackgrounds);
            Assert.True(transparent > 0, "no overlay uses the 0xFF00FF show-the-underlay sentinel");
        }

        /// <summary>
        ///     Overlay 94 emits opcode 11 twice, which pins the decoder as a last-write-wins loop.
        /// </summary>
        /// <remarks>
        ///     A one-shot switch, or a decoder that rejected a repeated opcode, would read the
        ///     second value as an opcode and desynchronise. This is the only definition in the cache
        ///     that exercises it.
        /// </remarks>
        [RealCacheFact]
        public void RepeatedOpcodeTakesTheLastValue()
        {
            RSCache cache = _fixture.OpenCache();
            FloorOverlayDefinition def = cache.GetFloorOverlay(94);

            int elevenCount = 0;
            foreach (DecodedOpcode entry in def.DecodedOpcodes)
                if (entry.Opcode == 11)
                    elevenCount++;

            Assert.Equal(2, elevenCount);
            Assert.Equal(127, def.Priority);
        }

        /// <summary>
        ///     The priority composite folds the definition id into the low byte.
        /// </summary>
        [RealCacheFact]
        public void PriorityCompositeFoldsInTheDefinitionId()
        {
            RSCache cache = _fixture.OpenCache();
            FloorOverlayDefinition def = cache.GetFloorOverlay(94);

            Assert.Equal((127 << 8) | 94, def.ApplyPriorityComposite());

            //It must not be applied during decode, or Encode would write the composite back.
            Assert.Equal(127, def.Priority);
        }

        /// <summary>
        ///     Defaults survive a definition that sets nothing.
        /// </summary>
        [Fact]
        public void EmptyDefinitionsKeepTheirDefaults()
        {
            var underlay = new FloorUnderlayDefinition { Id = 0 };
            underlay.Decode(new JagStream(new byte[] { 0 }));
            Assert.Equal(-1, underlay.TextureId);
            Assert.Equal(512, underlay.TextureScale);
            Assert.True(underlay.CastsShadow);
            Assert.True(underlay.Occludes);

            var overlay = new FloorOverlayDefinition { Id = 0 };
            overlay.Decode(new JagStream(new byte[] { 0 }));
            Assert.Equal(0, overlay.PrimaryRgb);
            Assert.False(overlay.HasPrimaryRgb);
            Assert.Equal(-1, overlay.SecondaryRgb);
            Assert.Equal(-1, overlay.TextureId);
            Assert.Equal(512, overlay.TextureScale);
            Assert.Equal(8, overlay.Priority);
            Assert.Equal(0x122F3D, overlay.WaterTintRgb);
            Assert.Equal(64, overlay.WaterDepth);
            Assert.Equal(127, overlay.WaterAlpha);
            Assert.True(overlay.CastsShadow);
            Assert.False(overlay.BlendWithNeighbours);
            Assert.True(overlay.FlatGroundOccluder);
        }

        /// <summary>
        ///     An opcode the client does not handle is refused rather than silently desynchronising.
        /// </summary>
        [Fact]
        public void UnknownOpcodesAreRejected()
        {
            //4, 6 and 15 fall through in the client, consuming nothing, and corrupt everything after.
            foreach (byte opcode in new byte[] { 4, 6, 15 })
            {
                var overlay = new FloorOverlayDefinition { Id = 0 };
                Assert.ThrowsAny<Exception>(() => overlay.Decode(new JagStream(new byte[] { opcode, 0 })));
            }
        }

        /// <summary>An edit that adds a field emits its opcode.</summary>
        [Fact]
        public void EditedFieldsAreEmitted()
        {
            var overlay = new FloorOverlayDefinition { Id = 0 };
            overlay.Decode(new JagStream(new byte[] { 0 }));

            overlay.PrimaryRgb = 0x336699;
            overlay.HasPrimaryRgb = true;
            overlay.BlendWithNeighbours = true;

            var round = new FloorOverlayDefinition { Id = 0 };
            round.Decode(new JagStream(overlay.Encode().ToArray()));

            Assert.Equal(0x336699, round.PrimaryRgb);
            Assert.True(round.BlendWithNeighbours);
        }
    }
}
