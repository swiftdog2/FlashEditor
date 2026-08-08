using System;
using System.Collections.Generic;
using System.Linq;
using FlashEditor;
using FlashEditor.cache;
using FlashEditor.cache.sprites;
using FlashEditor.Definitions.Fonts;
using FlashEditor.Tests.Cache.RealCache;
using Xunit;
using Xunit.Abstractions;

namespace FlashEditor.Tests.Definitions
{
    /// <summary>Scratch measurement of the index-13 to index-8 relationship. Not a permanent test.</summary>
    [Collection("RealCache")]
    public sealed class FontGlyphJoinProbe : IClassFixture<RealCacheFixture>
    {
        private readonly RealCacheFixture _fixture;
        private readonly ITestOutputHelper _output;

        public FontGlyphJoinProbe(RealCacheFixture fixture, ITestOutputHelper output)
        {
            _fixture = fixture;
            _output = output;
        }

        private static bool FrameCountOk(SpriteDefinition set) => set.GetFrameCount() == 256;

        private static bool AdvanceBound(FontDefinition font, SpriteDefinition set)
        {
            for (int c = 0; c < 256; c++)
                if (font.AdvanceOf(c) < set.Frames[c].SubWidth) return false;
            return true;
        }

        /// <summary>The ink, at its own left bearing, fits inside the advance box.</summary>
        private static bool InkFitsTheAdvance(FontDefinition font, SpriteDefinition set)
        {
            for (int c = 0; c < 256; c++)
            {
                SpriteFrame f = set.Frames[c];
                if (f.OffsetX + f.SubWidth > font.AdvanceOf(c)) return false;
            }
            return true;
        }

        private static bool CanvasWidthIsMaxAdvance(FontDefinition font, SpriteDefinition set)
        {
            int max = 0;
            for (int c = 0; c < 256; c++) max = Math.Max(max, font.AdvanceOf(c));
            return max == set.width;
        }

        private static bool LineHeightRule(FontDefinition font, SpriteDefinition set)
            => font.LineHeight + font.Descent == set.height;

        private static bool AscentRule(FontDefinition font, SpriteDefinition set)
        {
            int floor = set.height - (font.Ascent + font.Descent);
            for (int c = 0; c < 256; c++)
            {
                SpriteFrame f = set.Frames[c];
                if (f.SubWidth == 0 || f.SubHeight == 0) continue;
                if (f.OffsetY < floor) return false;
            }
            return true;
        }

        private static bool CanvasWidthRule(FontDefinition font, SpriteDefinition set)
        {
            for (int c = 0; c < 256; c++)
                if (set.Frames[c].OffsetX + set.Frames[c].SubWidth > set.width) return false;
            return true;
        }

        [RealCacheFact]
        public void CrossPairing()
        {
            _output.WriteLine("cache: " + RealCacheLocator.Directory);
            RSCache cache = _fixture.OpenCache();
            RSReferenceTable fonts = _fixture.Table(RSConstants.FONTS_INDEX);
            RSReferenceTable sprites = _fixture.Table(RSConstants.SPRITES_INDEX);

            var metrics = new SortedDictionary<int, FontDefinition>();
            var sheets = new SortedDictionary<int, SpriteDefinition>();

            foreach (int fontId in fonts.GetArchiveEntries().Keys)
            {
                metrics[fontId] = FontDefinition.Load(cache, fontId);
                int[] fileIds = sprites.GetArchiveEntry(fontId).GetValidFileIds();
                var set = new SpriteDefinition();
                set.Decode(new JagStream(cache.ReadFileBytes(RSConstants.SPRITES_INDEX, fontId, fileIds[0])));
                sheets[fontId] = set;
            }

            var palettes = new SortedDictionary<string, int>();
            var flagValues = new SortedDictionary<int, int>();
            foreach (int fontId in metrics.Keys)
            {
                SpriteDefinition set = sheets[fontId];
                string p = string.Join("/", set.PaletteStored.Select(v => "0x" + v.ToString("X6")));
                palettes.TryGetValue(p, out int seen);
                palettes[p] = seen + 1;
                foreach (SpriteFrame f in set.Frames)
                {
                    flagValues.TryGetValue(f.Flags, out int fs);
                    flagValues[f.Flags] = fs + 1;
                }
            }
            _output.WriteLine("palettes: " + string.Join(" | ", palettes.Select(e => $"{e.Key} x{e.Value}")));
            _output.WriteLine("frame flags: " + string.Join(", ", flagValues.Select(e => $"{e.Key}={e.Value}")));

            int exactTouch = 0, slackOne = 0;
            foreach (int fontId in metrics.Keys)
            {
                FontDefinition font = metrics[fontId];
                SpriteDefinition set = sheets[fontId];
                int minTop = int.MaxValue;
                for (int c = 0; c < 256; c++)
                {
                    SpriteFrame f = set.Frames[c];
                    if (f.SubWidth > 0 && f.SubHeight > 0) minTop = Math.Min(minTop, f.OffsetY);
                }
                int slack = minTop - (set.height - font.Ascent - font.Descent);
                if (slack == 0) exactTouch++; else if (slack == 1) slackOne++;
                _output.WriteLine($"font {fontId}: lh={font.LineHeight} desc={font.Descent} H={set.height} " +
                                  $"lh+desc-H={font.LineHeight + font.Descent - set.height} ascentSlack={slack}");
            }
            _output.WriteLine($"ascent band exactly touched by {exactTouch} fonts, one row of slack on {slackOne}");

            var rules = new (string Name, Func<FontDefinition, SpriteDefinition, bool> Test)[] {
                ("256 frames", (f, s) => FrameCountOk(s)),
                ("advance >= subWidth", AdvanceBound),
                ("offsetX + subWidth <= advance", InkFitsTheAdvance),
                ("lineHeight + descent == canvasH", LineHeightRule),
                ("ink within the ascent band", AscentRule),
                ("canvasW == max advance", CanvasWidthIsMaxAdvance)
            };

            foreach ((string name, var test) in rules)
            {
                int id = 0, cross = 0;
                foreach (int fontId in metrics.Keys)
                    foreach (int sheetId in sheets.Keys)
                    {
                        bool ok = test(metrics[fontId], sheets[sheetId]);
                        if (fontId == sheetId) { if (ok) id++; }
                        else if (ok) cross++;
                    }
                _output.WriteLine($"{name}: identity {id}/25, cross {cross}/600");
            }

            int allId = 0, allCross = 0;
            var survivors = new List<string>();
            foreach (int fontId in metrics.Keys)
                foreach (int sheetId in sheets.Keys)
                {
                    bool ok = rules.All(r => r.Test(metrics[fontId], sheets[sheetId]));
                    if (fontId == sheetId) { if (ok) allId++; else _output.WriteLine("IDENTITY FAILED " + fontId); }
                    else if (ok) { allCross++; survivors.Add($"{fontId}<-{sheetId}"); }
                }
            _output.WriteLine($"ALL FOUR: identity {allId}/25, cross {allCross}/600");
            _output.WriteLine("cross survivors: " + string.Join(", ", survivors));
        }
    }
}
