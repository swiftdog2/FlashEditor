using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Linq;
using FlashEditor.cache;
using FlashEditor.Definitions.Sprites;
using FlashEditor.Tests.Cache.RealCache;
using Xunit;
using Xunit.Abstractions;

namespace FlashEditor.Tests.Rendering
{
    /// <summary>Temporary: dump a few textures both ways so the change can be judged by eye.</summary>
    [Collection("RealCache")]
    public sealed class MonoBlendDump : IClassFixture<RealCacheFixture>
    {
        private readonly RealCacheFixture _fixture;
        private readonly ITestOutputHelper _output;

        public MonoBlendDump(RealCacheFixture fixture, ITestOutputHelper output)
        {
            _fixture = fixture;
            _output = output;
        }

        [RealCacheFact]
        public void Dump()
        {
            string dir = Environment.GetEnvironmentVariable("MONO_DUMP_DIR");
            Assert.False(string.IsNullOrEmpty(dir));
            Directory.CreateDirectory(dir);

            RSCache cache = _fixture.OpenCache();
            new TextureManager(cache).Load();

            int[] ids = { 668, 598, 863, 376, 530, 128, 911, 812 };

            foreach (bool legacy in new[] { true, false })
            {
                TextureGraphEvaluator.MeasureLegacyMonoBlend = legacy;
                TextureGraphEvaluator.ClearCaches();

                foreach (int id in ids)
                {
                    if (!TextureManager.Textures.TryGetValue(id, out TextureDefinition def) || def.graph == null)
                        continue;

                    int[] px = TextureGraphEvaluator.RenderArgb(def.graph, 128, 128, cache, def.field1824, id);
                    if (px == null) { _output.WriteLine($"{id} {(legacy ? "before" : "after")}: null"); continue; }

                    using var bmp = new Bitmap(128, 128, PixelFormat.Format32bppArgb);
                    var data = bmp.LockBits(new Rectangle(0, 0, 128, 128), ImageLockMode.WriteOnly, PixelFormat.Format32bppArgb);
                    System.Runtime.InteropServices.Marshal.Copy(px, 0, data.Scan0, px.Length);
                    bmp.UnlockBits(data);
                    bmp.Save(Path.Combine(dir, $"tex{id}-{(legacy ? "before" : "after")}.png"), ImageFormat.Png);

                    int rgb = TextureManager.RepresentativeRgb(def);
                    _output.WriteLine($"{id} {(legacy ? "before" : "after")}: declared=({(rgb >> 16) & 0xFF},{(rgb >> 8) & 0xFF},{rgb & 0xFF}) "
                        + $"mean=({px.Average(p => (p >> 16) & 0xFF):F0},{px.Average(p => (p >> 8) & 0xFF):F0},{px.Average(p => p & 0xFF):F0})");
                }
            }

            TextureGraphEvaluator.MeasureLegacyMonoBlend = false;
            TextureGraphEvaluator.ClearCaches();
        }
    }
}
