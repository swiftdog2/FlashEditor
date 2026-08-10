using System.Drawing.Imaging;
using FlashEditor.Cache;
using FlashEditor.Definitions.Editing;
using FlashEditor.Definitions.Fonts;
using FlashEditor.Definitions.Interfaces;
using FlashEditor.Definitions.Interfaces.Layout;
using FlashEditor.Definitions.Sprites;
using FlashEditor.IO;

namespace FlashEditor.Tools.RenderInterface {
    /// <summary>
    ///     Draws one interface to a PNG, offscreen, at the size the interface needs.
    /// </summary>
    /// <remarks>
    ///     <b>This exists because a window screenshot cannot answer a rendering question.</b>
    ///     <c>Capture-EditorTab.ps1</c> photographs the whole editor, where the canvas sits inside a
    ///     splitter a few hundred pixels across; an interface laid out on the 765x503 fixed-mode
    ///     sheet arrives scrolled and clipped, so "is this drawn correctly" becomes "which part of
    ///     it was on screen". Here the control is the same control, sized to the sheet, and the PNG
    ///     is the whole of it.
    ///     <para>
    ///     <b>It waits for the thumbnail cache rather than drawing immediately.</b> Sprites and
    ///     models are produced on a background thread and <c>TryGet</c> returns null until one
    ///     arrives, so a draw taken at once shows placeholders for every picture on the page and
    ///     looks exactly like a renderer that cannot read them.
    ///     </para>
    /// </remarks>
    internal static class Program {
        [STAThread]
        private static int Main(string[] args) {
            if (args.Length < 2) {
                Console.Error.WriteLine(
                    "usage: RenderInterface <cache-directory> <out.png> [interface-id ...]");
                return 2;
            }

            string cacheDirectory = args[0];
            string outPath = args[1];

            int[] ids = args.Length > 2
                ? args.Skip(2).Select(int.Parse).ToArray()
                : new[] { 8 };

            var store = new RSFileStore(cacheDirectory);
            var cache = new RSCache(store);

            using var tiles = new DefinitionThumbnailCache(new IDefinitionThumbnailRenderer[] {
                new SpriteThumbnailRenderer(cache, composited: false),
                new ModelThumbnailRenderer(cache)
            });

            using var painter = new InterfaceTextPainter(cache);

            foreach (int id in ids) {
                string path = ids.Length == 1
                    ? outPath
                    : Path.Combine(Path.GetDirectoryName(outPath) ?? ".",
                        Path.GetFileNameWithoutExtension(outPath) + "-" + id + ".png");

                try {
                    RenderOne(cache, tiles, painter, id, path);
                    Console.WriteLine("wrote " + path);
                }
                catch (Exception error) {
                    Console.Error.WriteLine("interface " + id + ": " + error.Message);
                }
            }

            return 0;
        }

        private static void RenderOne(RSCache cache, DefinitionThumbnailCache tiles,
            InterfaceTextPainter painter, int groupId, string outPath) {
            /* One group read, not one read per file. RSCache.ReadFile releases the group as soon as
               it has handed back the file it was asked for, so walking a 40-file interface file by
               file re-inflates and re-decodes the same archive 40 times. */
            IReadOnlyDictionary<int, JagStream> files =
                cache.ReadGroup(RSConstants.INTERFACE_DEFINITIONS_INDEX, groupId);

            var components = new List<InterfaceComponentDefinition>(files.Count);
            foreach (KeyValuePair<int, JagStream> file in files) {
                if (file.Value == null)
                    continue;

                components.Add(new InterfaceComponentDefinition(groupId, file.Key).Decode(file.Value));
            }

            InterfaceComponentTree tree = InterfaceComponentTree.Build(groupId, components);

            if (Environment.GetEnvironmentVariable("RENDERINTERFACE_DUMP") == "1")
                Dump(tree);

            if (Environment.GetEnvironmentVariable("RENDERINTERFACE_FONTS") == "1")
                DumpFonts(cache, components);

            using var canvas = new InterfaceCanvas {
                Size = new Size(InterfaceRect.FixedModeCanvas.Width + 40,
                    InterfaceRect.FixedModeCanvas.Height + 40),
                Thumbnails = tiles,
                TextPainter = painter
            };

            canvas.Show(tree);

            /* Every picture on the page is produced off-thread, so the first paint is what asks for
               them and a draw taken before they land shows placeholders throughout. Pump the
               message loop rather than sleeping: the cache raises TilesReady on the UI thread, and
               a thread with no loop running never receives it. */
            var area = new Rectangle(0, 0, canvas.Width, canvas.Height);

            /* A priming draw, whose only job is to ask for every picture on the page.
               Refresh() cannot do it: this control has never been shown, so it has no window
               handle, so there is no WM_PAINT to invalidate and OnPaint never runs. DrawToBitmap
               creates the handle and paints synchronously, which is the only thing here that
               reaches the draw path at all - without it the settle loop below waits for work that
               was never queued and every sprite and model comes out as its placeholder. */
            using (var priming = new Bitmap(canvas.Width, canvas.Height))
                canvas.DrawToBitmap(priming, area);

            //Then wait for the producer. Models are rasterised on the CPU one at a time, so a page
            //of them takes noticeably longer than a page of sprites.
            for (int settle = 0; settle < 400 && tiles.PendingCount > 0; settle++)
                Thread.Sleep(25);

            Console.WriteLine("  interface " + groupId + ": " + components.Count + " components, " +
                tiles.Count + " tile entries, " + tiles.PendingCount + " pending, " +
                tiles.Bytes + " bytes");

            using var picture = new Bitmap(canvas.Width, canvas.Height);
            canvas.DrawToBitmap(picture, area);
            picture.Save(outPath, ImageFormat.Png);
        }

        /// <summary>
        ///     Prints the metrics of every font this interface uses, beside where its ink actually
        ///     lands.
        /// </summary>
        /// <remarks>
        ///     The alignment maths and the glyph placement are stated in two different coordinate
        ///     systems - one from the client's baseline, one from this project's line top - and a
        ///     mismatch between them shows up as text drifting up or down inside its box. The only
        ///     way to tell which is out is to print both.
        /// </remarks>
        private static void DumpFonts(RSCache cache, List<InterfaceComponentDefinition> components) {
            var seen = new HashSet<int>();

            foreach (InterfaceComponentDefinition c in components) {
                if (c.ComponentType != 4 || c.FontId < 0 || !seen.Add(c.FontId))
                    continue;

                FontGlyphSheet sheet;
                try {
                    sheet = FontGlyphSheet.Load(cache, c.FontId);
                }
                catch (Exception error) {
                    Console.WriteLine("    font " + c.FontId + ": " + error.Message);
                    continue;
                }

                FontTextLayout.Layout layout = FontTextLayout.Measure(sheet.Metrics, c.Message.Text);

                int inkTop = int.MaxValue, inkBottom = int.MinValue;
                foreach (FontTextLayout.PlacedGlyph glyph in layout.Glyphs) {
                    SpriteFrame? frame = sheet.FrameFor(glyph.Character);
                    if (frame == null)
                        continue;

                    inkTop = Math.Min(inkTop, glyph.LineTop + frame.OffsetY);
                    inkBottom = Math.Max(inkBottom, glyph.LineTop + frame.OffsetY + frame.SubHeight);
                }

                Console.WriteLine("    font " + c.FontId +
                    ": ascent=" + sheet.Metrics.Ascent +
                    " descent=" + sheet.Metrics.Descent +
                    " lineHeight=" + sheet.Metrics.LineHeight +
                    " canvas=" + sheet.CanvasWidth + "x" + sheet.CanvasHeight +
                    " baseline=" + sheet.Baseline +
                    " | \"" + c.Message.Text + "\"" +
                    " layout=" + layout.Width + "x" + layout.Height +
                    " lines=" + layout.Lines +
                    " ink=" + (inkTop == int.MaxValue ? "none" : inkTop + ".." + inkBottom));
            }
        }

        /// <summary>
        ///     Prints every component's resolved rectangle, so a rendering question can be answered
        ///     with numbers instead of by looking at a picture.
        /// </summary>
        private static void Dump(InterfaceComponentTree tree) {
            IReadOnlyDictionary<int, InterfaceLayoutNode> nodes =
                InterfaceLayoutResolver.ResolveGroup(tree, InterfaceRect.FixedModeCanvas);

            foreach (int fileId in tree.InDrawOrder()) {
                if (!nodes.TryGetValue(fileId, out InterfaceLayoutNode? node))
                    continue;

                InterfaceComponentDefinition c = node.Component;
                string detail = c.ComponentType switch {
                    4 => " text=\"" + c.Message.Text + "\" font=" + c.FontId +
                         " h=" + c.HorizontalAlignment + " v=" + c.VerticalAlignment +
                         " lineHeight=" + c.LineHeight,
                    5 => " sprite=" + c.SpriteId + (c.SpriteTiles ? " tiled" : ""),
                    6 => " model=" + c.RawModelId,
                    _ => ""
                };

                Console.WriteLine("    " + fileId.ToString().PadLeft(3) +
                    " d" + node.Depth + " type" + c.ComponentType +
                    " abs=" + node.Absolute.X + "," + node.Absolute.Y +
                    " " + node.Absolute.Width + "x" + node.Absolute.Height +
                    " clip=" + node.Clip.X + "," + node.Clip.Y +
                    " " + node.Clip.Width + "x" + node.Clip.Height +
                    (node.IsDrawn ? "" : " NOTDRAWN") +
                    (c.IsHidden ? " HIDDEN" : "") + detail);
            }
        }
    }
}
