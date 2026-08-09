using System;
using System.Collections.Generic;
using System.Linq;
using FlashEditor.cache;
using FlashEditor.Definitions.Sprites;
using FlashEditor.Tests.Cache.RealCache;
using Xunit;
using Xunit.Abstractions;

namespace FlashEditor.Tests.Rendering
{
    /// <summary>Temporary: cache-wide fidelity of the mono type 7 arm against the old default.</summary>
    [Collection("RealCache")]
    public sealed class MonoBlendMeasurement : IClassFixture<RealCacheFixture>
    {
        private readonly RealCacheFixture _fixture;
        private readonly ITestOutputHelper _output;

        public MonoBlendMeasurement(RealCacheFixture fixture, ITestOutputHelper output)
        {
            _fixture = fixture;
            _output = output;
        }

        [RealCacheFact]
        public void Measure()
        {
            RSCache cache = _fixture.OpenCache();
            new TextureManager(cache).Load();

            var hosts = TextureManager.Textures.Values
                .Where(d => d.graph?.Nodes != null && d.graph.Nodes.Any(n => n != null && n.Type == 7 && n.MonoOverride == true))
                .OrderBy(d => d.id)
                .ToList();

            _output.WriteLine($"{_fixture.Profile}: {hosts.Count} textures carry a mono type 7");

            var before = new Dictionary<int, double>();
            var after = new Dictionary<int, double>();

            foreach (bool legacy in new[] { true, false })
            {
                TextureGraphEvaluator.MeasureLegacyMonoBlend = legacy;
                TextureGraphEvaluator.ClearCaches();

                foreach (TextureDefinition def in hosts)
                {
                    int[] px = TextureGraphEvaluator.RenderArgb(def.graph, 32, 32, cache, def.field1824, def.id);
                    if (px == null) continue;

                    long r = 0, g = 0, b = 0;
                    foreach (int p in px) { r += (p >> 16) & 0xFF; g += (p >> 8) & 0xFF; b += p & 0xFF; }
                    int mr = (int)(r / px.Length), mg = (int)(g / px.Length), mb = (int)(b / px.Length);

                    int rgb = TextureManager.RepresentativeRgb(def);
                    double err = (Math.Abs(mr - ((rgb >> 16) & 0xFF)) + Math.Abs(mg - ((rgb >> 8) & 0xFF))
                        + Math.Abs(mb - (rgb & 0xFF))) / 3.0;

                    (legacy ? before : after)[def.id] = err;
                }
            }

            TextureGraphEvaluator.MeasureLegacyMonoBlend = false;
            TextureGraphEvaluator.ClearCaches();

            var common = before.Keys.Intersect(after.Keys).ToList();
            double meanBefore = common.Average(id => before[id]);
            double meanAfter = common.Average(id => after[id]);
            int improved = common.Count(id => after[id] < before[id] - 0.5);
            int worsened = common.Count(id => after[id] > before[id] + 0.5);
            int unchanged = common.Count - improved - worsened;

            _output.WriteLine($"compared {common.Count} textures");
            _output.WriteLine($"mean channel error against the declared colour: before {meanBefore:F2}, after {meanAfter:F2}");
            _output.WriteLine($"improved {improved}, worsened {worsened}, unchanged {unchanged}");
            _output.WriteLine($"911: before {before.GetValueOrDefault(911, -1):F1}, after {after.GetValueOrDefault(911, -1):F1}");

            foreach (int id in common.OrderByDescending(id => after[id] - before[id]).Take(10))
                _output.WriteLine($"  worst regression {id}: {before[id]:F1} -> {after[id]:F1}");
            foreach (int id in common.OrderBy(id => after[id] - before[id]).Take(10))
                _output.WriteLine($"  best improvement {id}: {before[id]:F1} -> {after[id]:F1}");
        }
    }
}
