using System.Collections.Generic;
using System.Drawing.Drawing2D;
using System.Drawing;
using System;

namespace FlashEditor.Rendering
{
    public static class IndexLabelPainter
    {
        public const int BackdropPadding = 3;

        public static Color FaceColour => Color.FromArgb(255, 255, 184, 38);

        public static Color VertexColour => Color.FromArgb(255, 150, 190, 255);

        public static Color BackdropColour => Color.FromArgb(190, 0, 0, 0);

        public static void Paint(Graphics graphics, IReadOnlyList<IndexLabel> labels, Font font)
        {
            if (graphics == null)
            {
                throw new ArgumentNullException("graphics");
            }
            if (labels == null)
            {
                throw new ArgumentNullException("labels");
            }
            if (font == null)
            {
                throw new ArgumentNullException("font");
            }
            if (labels.Count == 0)
            {
                return;
            }
            SmoothingMode smoothingMode = graphics.SmoothingMode;
            graphics.SmoothingMode = SmoothingMode.AntiAlias;
            using SolidBrush brush = new SolidBrush(BackdropColour);
            using SolidBrush solidBrush = new SolidBrush(FaceColour);
            using SolidBrush solidBrush2 = new SolidBrush(VertexColour);
            foreach (IndexLabel label in labels)
            {
                SizeF sizeF = graphics.MeasureString(label.Text, font);
                float num = label.Pixel.X - sizeF.Width / 2f;
                float num2 = label.Pixel.Y - sizeF.Height / 2f;
                graphics.FillRectangle(brush, num - 3f, num2 - 3f, sizeF.Width + 6f, sizeF.Height + 6f);
                graphics.DrawString(label.Text, font, (label.Kind == IndexLabelKind.Face) ? solidBrush : solidBrush2, num, num2);
            }
            graphics.SmoothingMode = smoothingMode;
        }
    }
}
