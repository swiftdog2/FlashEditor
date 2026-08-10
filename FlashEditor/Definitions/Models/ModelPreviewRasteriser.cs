using System;
using System.Drawing;
using System.Drawing.Imaging;

namespace FlashEditor.Definitions.Models {
    /// <summary>
    ///     Draws a model to a bitmap on the CPU, for the places that cannot use OpenGL.
    /// </summary>
    /// <remarks>
    ///     <b>Why this exists when the editor already has a renderer.</b> The 3D viewer is OpenGL on
    ///     the one UI-thread context, so everything else that wants to show a model - the interface
    ///     canvas, the asset picker, a grid thumbnail - had to draw a labelled box instead. That is
    ///     an honest placeholder and a poor answer to "which model is 4608". This is small, slow and
    ///     entirely CPU-side, which is exactly what those callers need: a still picture, off the UI
    ///     thread, with no context to share.
    ///     <para>
    ///     <b>It is deliberately not the client's renderer and must not be mistaken for it.</b> Flat
    ///     shading per face from the stored colour, a painter's algorithm by depth, no textures, no
    ///     lighting model, no priorities, no alpha. The client does all of those. What this gets
    ///     right is the silhouette, the colours and the orientation, which is what identifies a
    ///     model at a glance - and any surface using it should say so, the way
    ///     <c>CLAUDE.md</c> requires a view that diverges from the client to say it does.
    ///     </para>
    ///     <para>
    ///     <b>Faces with render type 2 are skipped, because the client refuses to draw them.</b>
    ///     That is settled from what the client does with the array rather than from its name, and
    ///     it is a defect this project has had before: faces the client will not draw were drawn by
    ///     the OpenGL viewer for as long as it existed and no test ever saw it.
    ///     </para>
    /// </remarks>
    public static class ModelPreviewRasteriser {
        /// <summary>
        ///     Renders a model at a fixed three-quarter view.
        /// </summary>
        /// <remarks>
        ///     One angle rather than a caller-chosen one: every consumer so far wants "show me what
        ///     this is", and a shared angle makes a grid of models comparable. The camera frames the
        ///     model's own bounds, so a large model and a small one both fill the tile.
        /// </remarks>
        /// <param name="model">The decoded model.</param>
        /// <param name="width">The bitmap width.</param>
        /// <param name="height">The bitmap height.</param>
        /// <returns>The picture, or null when the model has nothing to draw.</returns>
        public static Bitmap? Render(ModelDefinition model, int width, int height) {
            if (model == null)
                throw new ArgumentNullException(nameof(model));
            if (width <= 0 || height <= 0)
                return null;
            if (model.VertexCount <= 0 || model.TriangleCount <= 0)
                return null;

            //Yaw a little off square and pitch down, which is roughly how the game presents a
            //character or an object and so the angle a reader recognises.
            const double Yaw = 0.6;
            const double Pitch = 0.45;

            double cosYaw = Math.Cos(Yaw), sinYaw = Math.Sin(Yaw);
            double cosPitch = Math.Cos(Pitch), sinPitch = Math.Sin(Pitch);

            var px = new double[model.VertexCount];
            var py = new double[model.VertexCount];
            var pz = new double[model.VertexCount];

            double minX = double.MaxValue, maxX = double.MinValue;
            double minY = double.MaxValue, maxY = double.MinValue;

            for (int i = 0; i < model.VertexCount; i++) {
                double x = model.VertX[i];
                double y = model.VertY[i];
                double z = model.VertZ[i];

                double rx = x * cosYaw - z * sinYaw;
                double rz = x * sinYaw + z * cosYaw;
                double ry = y * cosPitch - rz * sinPitch;

                pz[i] = y * sinPitch + rz * cosPitch;
                px[i] = rx;
                py[i] = ry;

                if (rx < minX) minX = rx;
                if (rx > maxX) maxX = rx;
                if (ry < minY) minY = ry;
                if (ry > maxY) maxY = ry;
            }

            double spanX = Math.Max(1.0, maxX - minX);
            double spanY = Math.Max(1.0, maxY - minY);

            //A margin, so a silhouette that touches its bounds is not flush against the tile edge.
            double scale = Math.Min((width - 2) / spanX, (height - 2) / spanY);
            double offsetX = (width - spanX * scale) / 2.0 - minX * scale;
            double offsetY = (height - spanY * scale) / 2.0 - minY * scale;

            var bitmap = new Bitmap(width, height, PixelFormat.Format32bppArgb);
            var depth = new double[width * height];
            for (int i = 0; i < depth.Length; i++)
                depth[i] = double.MaxValue;

            BitmapData bits = bitmap.LockBits(new Rectangle(0, 0, width, height),
                ImageLockMode.WriteOnly, PixelFormat.Format32bppArgb);

            try {
                var pixels = new int[width * height];

                for (int face = 0; face < model.TriangleCount; face++) {
                    //Render type 2 is the one the client gates its draw list on. Drawing it here
                    //would show geometry the game never puts on screen.
                    if (model.FaceRenderType != null && face < model.FaceRenderType.Length
                        && model.FaceRenderType[face] == 2) {
                        continue;
                    }

                    int a = model.faceIndices1[face];
                    int b = model.faceIndices2[face];
                    int c = model.faceIndices3[face];

                    if (a >= model.VertexCount || b >= model.VertexCount || c >= model.VertexCount)
                        continue;

                    int rgb = ModelDefinition.RawHslToRgb(model.FaceColour[face]);

                    /* A cheap directional shade from the face normal's z, so curvature reads at all.
                       Flat colour alone makes a model a silhouette-coloured blob, which defeats the
                       point of drawing it. */
                    double nx = (py[b] - py[a]) * (pz[c] - pz[a]) - (pz[b] - pz[a]) * (py[c] - py[a]);
                    double ny = (pz[b] - pz[a]) * (px[c] - px[a]) - (px[b] - px[a]) * (pz[c] - pz[a]);
                    double nz = (px[b] - px[a]) * (py[c] - py[a]) - (py[b] - py[a]) * (px[c] - px[a]);
                    double length = Math.Sqrt(nx * nx + ny * ny + nz * nz);

                    double shade = length <= 0 ? 1.0 : 0.55 + 0.45 * Math.Abs(nz / length);
                    int shaded = Shade(rgb, shade);

                    FillTriangle(pixels, depth, width, height,
                        px[a] * scale + offsetX, py[a] * scale + offsetY, pz[a],
                        px[b] * scale + offsetX, py[b] * scale + offsetY, pz[b],
                        px[c] * scale + offsetX, py[c] * scale + offsetY, pz[c],
                        shaded);
                }

                System.Runtime.InteropServices.Marshal.Copy(pixels, 0, bits.Scan0, pixels.Length);
            }
            finally {
                bitmap.UnlockBits(bits);
            }

            return bitmap;
        }

        /// <summary>
        ///     Fills one triangle, nearest depth wins.
        /// </summary>
        /// <remarks>
        ///     A depth buffer rather than sorting faces back to front. Sorting by centroid is the
        ///     usual shortcut and it fails exactly where a model is interesting - two faces that
        ///     interpenetrate, or a long face crossing a short one - which shows as flickering
        ///     surfaces that look like decode corruption rather than like a rendering choice.
        /// </remarks>
        private static void FillTriangle(int[] pixels, double[] depth, int width, int height,
            double ax, double ay, double az, double bx, double by, double bz,
            double cx, double cy, double cz, int argb) {
            int minX = Math.Max(0, (int) Math.Floor(Math.Min(ax, Math.Min(bx, cx))));
            int maxX = Math.Min(width - 1, (int) Math.Ceiling(Math.Max(ax, Math.Max(bx, cx))));
            int minY = Math.Max(0, (int) Math.Floor(Math.Min(ay, Math.Min(by, cy))));
            int maxY = Math.Min(height - 1, (int) Math.Ceiling(Math.Max(ay, Math.Max(by, cy))));

            double area = (bx - ax) * (cy - ay) - (by - ay) * (cx - ax);
            if (Math.Abs(area) < 1e-9)
                return;

            for (int y = minY; y <= maxY; y++) {
                for (int x = minX; x <= maxX; x++) {
                    double sx = x + 0.5, sy = y + 0.5;

                    double w0 = ((bx - sx) * (cy - sy) - (by - sy) * (cx - sx)) / area;
                    double w1 = ((cx - sx) * (ay - sy) - (cy - sy) * (ax - sx)) / area;
                    double w2 = 1.0 - w0 - w1;

                    if (w0 < 0 || w1 < 0 || w2 < 0)
                        continue;

                    double z = w0 * az + w1 * bz + w2 * cz;
                    int at = y * width + x;

                    if (z >= depth[at])
                        continue;

                    depth[at] = z;
                    pixels[at] = argb;
                }
            }
        }

        private static int Shade(int rgb, double factor) {
            int r = (int) Math.Clamp(((rgb >> 16) & 0xFF) * factor, 0, 255);
            int g = (int) Math.Clamp(((rgb >> 8) & 0xFF) * factor, 0, 255);
            int b = (int) Math.Clamp((rgb & 0xFF) * factor, 0, 255);

            return unchecked((int) 0xFF000000) | (r << 16) | (g << 8) | b;
        }
    }
}
