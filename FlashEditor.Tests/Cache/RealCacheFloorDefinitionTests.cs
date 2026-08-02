using System;
using System.Collections.Generic;
using System.Text;
using FlashEditor.cache;
using FlashEditor.Definitions;
using FlashEditor.Tests.Cache.RealCache;
using Xunit;

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

        public RealCacheFloorDefinitionTests(RealCacheFixture fixture)
        {
            _fixture = fixture;
        }

        [RealCacheFact]
        public void EveryUnderlayDecodesAndRoundTrips()
        {
            RSCache cache = _fixture.OpenCache();
            int[] ids = cache.GetConfigFileIds(RSConstants.FLOOR_UNDERLAY_GROUP);

            var failures = new List<string>();
            int withTexture = 0;

            foreach (int id in ids)
            {
                try
                {
                    byte[] original = cache.ReadFileBytes(RSConstants.CONFIG, RSConstants.FLOOR_UNDERLAY_GROUP, id);
                    var def = new FloorUnderlayDefinition { Id = id };
                    var stream = new JagStream(original);
                    def.Decode(stream);

                    if (stream.Remaining() != 0)
                        failures.Add($"underlay {id}: {stream.Remaining()} bytes left over");

                    byte[] reencoded = def.Encode().ToArray();
                    if (!ByteEqual(original, reencoded))
                        failures.Add($"underlay {id}: round trip differs ({original.Length} -> {reencoded.Length} bytes)");

                    if (def.TextureId != -1)
                        withTexture++;
                }
                catch (Exception ex)
                {
                    failures.Add($"underlay {id}: {ex.Message}");
                }
            }

            AssertNoFailures(failures);
            Assert.Equal(159, ids.Length);
        }

        [RealCacheFact]
        public void EveryOverlayDecodesAndRoundTrips()
        {
            RSCache cache = _fixture.OpenCache();
            int[] ids = cache.GetConfigFileIds(RSConstants.FLOOR_OVERLAY_GROUP);

            var failures = new List<string>();
            int worldMapBackgrounds = 0;
            int transparent = 0;

            foreach (int id in ids)
            {
                try
                {
                    byte[] original = cache.ReadFileBytes(RSConstants.CONFIG, RSConstants.FLOOR_OVERLAY_GROUP, id);
                    var def = new FloorOverlayDefinition { Id = id };
                    var stream = new JagStream(original);
                    def.Decode(stream);

                    if (stream.Remaining() != 0)
                        failures.Add($"overlay {id}: {stream.Remaining()} bytes left over");

                    byte[] reencoded = def.Encode().ToArray();
                    if (!ByteEqual(original, reencoded))
                        failures.Add($"overlay {id}: round trip differs ({original.Length} -> {reencoded.Length} bytes)");

                    if (def.IsWorldMapBackground)
                        worldMapBackgrounds++;
                    if (def.PrimaryRgb == FloorOverlayDefinition.TransparentRgb)
                        transparent++;
                }
                catch (Exception ex)
                {
                    failures.Add($"overlay {id}: {ex.Message}");
                }
            }

            AssertNoFailures(failures);
            Assert.Equal(235, ids.Length);

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

        private static bool ByteEqual(byte[] a, byte[] b)
        {
            if (a.Length != b.Length)
                return false;
            for (int i = 0; i < a.Length; i++)
                if (a[i] != b[i])
                    return false;
            return true;
        }

        private static void AssertNoFailures(List<string> failures)
        {
            if (failures.Count == 0)
                return;

            var sb = new StringBuilder();
            sb.Append(failures.Count).Append(" floor definitions failed:");
            for (int i = 0; i < failures.Count && i < 20; i++)
                sb.AppendLine().Append("  ").Append(failures[i]);
            if (failures.Count > 20)
                sb.AppendLine().Append("  ... and ").Append(failures.Count - 20).Append(" more");

            Assert.Fail(sb.ToString());
        }
    }
}
