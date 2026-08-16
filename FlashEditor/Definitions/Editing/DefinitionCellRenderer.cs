using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using BrightIdeasSoftware;
using FlashEditor.UI;

namespace FlashEditor.Definitions.Editing {
    /// <summary>
    ///     Draws a cell that means more than its text: a colour, a picture, or a reference.
    /// </summary>
    /// <remarks>
    ///     <b>The number always stays on screen.</b> A swatch replaces nothing - it is drawn beside
    ///     the hex, and a thumbnail beside its id. Someone who wants the number must still get it,
    ///     because the number is what they will type into a tool, cite in a bug report, or search
    ///     for. It also happens to be what keeps sorting, filtering and cell editing working: all
    ///     three read the column's aspect, and none of them know a renderer exists.
    ///     <para>
    ///     <b>The row is never cast here.</b> Both the text and the visual are read through the
    ///     column's own delegates, which are <c>Cast</c>-guarded, because
    ///     <c>ObjectListView</c> hands a null model to a renderer for rows being recycled during a
    ///     scroll and for cells measured before a model is attached. A renderer that cast
    ///     <c>RowObject</c> itself would throw a raw <c>NullReferenceException</c> inside a paint
    ///     handler, where nothing catches it and the form goes down.
    ///     </para>
    ///     <para>
    ///     <b>A row of the wrong type still throws</b>, from inside the column's <c>Cast</c>, and
    ///     that is deliberate. It can only mean a descriptor wired its columns to a different row
    ///     type than it produces, and that exact fault has already been caught once by that
    ///     exception. Catching it here would turn a real diagnostic into a grid of blank cells.
    ///     </para>
    /// </remarks>
    internal sealed class DefinitionCellRenderer : BaseRenderer {
        /// <summary>The side of a swatch or a thumbnail tile, in pixels.</summary>
        internal const int ArtSide = 14;

        /// <summary>The gap between the art and the text beside it.</summary>
        private const int ArtGap = 5;

        private readonly DefinitionColumn column;
        private readonly Func<IDefinitionThumbnailSource?> thumbnails;

        /* Held rather than created per call. Render runs once per visible cell per paint, and the
           precedent for getting this wrong is in this repository: a font created per row cost
           4,593 GDI objects on the sprite page. */
        private readonly Pen edgePen;
        private readonly Pen placeholderPen;

        internal DefinitionCellRenderer(DefinitionColumn column,
            Func<IDefinitionThumbnailSource?> thumbnails) {
            this.column = column ?? throw new ArgumentNullException(nameof(column));
            this.thumbnails = thumbnails ?? throw new ArgumentNullException(nameof(thumbnails));

            edgePen = new Pen(Color.FromArgb(0x60, 0x00, 0x00, 0x00));
            placeholderPen = new Pen(Color.FromArgb(0x40, 0x80, 0x80, 0x80));
        }

        /// <summary>
        ///     The descriptor's column, so an activated cell can say which field it belongs to.
        /// </summary>
        /// <remarks>
        ///     Not <c>Column</c>: <see cref="BaseRenderer"/> already has one, holding the
        ///     <see cref="OLVColumn"/> this draws into, and shadowing it would leave two properties
        ///     of the same name meaning two different things on one object.
        /// </remarks>
        internal DefinitionColumn DescribedColumn => column;

        /// <summary>The visual for a row, for a host deciding what an activated cell named.</summary>
        /// <param name="model">The row.</param>
        /// <returns>The visual.</returns>
        internal DefinitionCellVisual VisualFor(object? model) {
            return column.Visual == null || model == null
                ? DefinitionCellVisual.None
                : column.Visual(model);
        }

        /// <inheritdoc/>
        public override void Render(Graphics g, Rectangle r) {
            DrawBackground(g, r);

            Rectangle cell = r;
            DefinitionCellVisual visual = VisualFor(RowObject);

            EditorSurface surface = EditorTheme.SurfaceOf(ListView);

            switch (visual.Art) {
                case DefinitionCellArt.Swatch:
                    DrawSwatch(g, ref cell, visual);
                    break;

                case DefinitionCellArt.Thumbnail:
                    DrawTile(g, ref cell, visual, surface);
                    break;

                case DefinitionCellArt.Link:
                    DrawLinkMark(g, ref cell, surface);
                    break;
            }

            /* Art == None falls through to here with the cell untouched, and so does a null row -
               whose Aspect is also null, so this draws an empty cell. One path covers both, which
               is why None is the struct's default rather than a case that has to be handled. */
            DrawText(g, cell, Aspect?.ToString() ?? string.Empty);
        }

        /// <inheritdoc/>
        /// <remarks>
        ///     Measured without consulting the row, so an auto-size pass over an empty grid cannot
        ///     throw. The art is a fixed side by construction, so only the text needs the model and
        ///     a null one contributes nothing.
        /// </remarks>
        protected override Size CalculateContentSize(Graphics g, Rectangle r) {
            Size text = base.CalculateContentSize(g, r);
            return new Size(text.Width + ArtSide + ArtGap, Math.Max(text.Height, ArtSide));
        }

        /// <inheritdoc/>
        protected override void Dispose(bool disposing) {
            if (disposing) {
                edgePen.Dispose();
                placeholderPen.Dispose();
            }

            base.Dispose(disposing);
        }

        /// <summary>
        ///     A filled square in the stored colour, outlined.
        /// </summary>
        /// <remarks>
        ///     The outline is not decoration. Floor colours run to both ends of the range, and a
        ///     near-white swatch on a white grid row or a near-black one on a shaded row would
        ///     otherwise have no edge at all and read as an empty cell.
        /// </remarks>
        private void DrawSwatch(Graphics g, ref Rectangle cell, DefinitionCellVisual visual) {
            Rectangle box = TakeArtBox(ref cell);

            using var brush = new SolidBrush(visual.SwatchColour);
            g.FillRectangle(brush, box);
            g.DrawRectangle(edgePen, box.X, box.Y, box.Width - 1, box.Height - 1);
        }

        /// <summary>
        ///     The picture for an id, or a placeholder while it is being read.
        /// </summary>
        /// <remarks>
        ///     A panel with no thumbnail source draws the placeholder and nothing else, which is
        ///     what makes a thumbnail column safe in a tab that has not opted in - the id is still
        ///     in the text beside it, so the cell degrades to what it was before.
        /// </remarks>
        private void DrawTile(Graphics g, ref Rectangle cell, DefinitionCellVisual visual,
            EditorSurface surface) {
            Rectangle box = TakeArtBox(ref cell);

            Bitmap? tile = thumbnails()?.TryGet(visual.IndexId, visual.TargetId, ArtSide);
            if (tile != null) {
                g.InterpolationMode = InterpolationMode.NearestNeighbor;
                g.PixelOffsetMode = PixelOffsetMode.Half;
                g.DrawImage(tile, box);
                return;
            }

            //An outline rather than nothing, so a row that is still loading reads as pending
            //rather than as a record with no picture.
            g.DrawRectangle(placeholderPen, box.X, box.Y, box.Width - 1, box.Height - 1);
        }

        /// <summary>The mark that says a number can be followed.</summary>
        private void DrawLinkMark(Graphics g, ref Rectangle cell, EditorSurface surface) {
            Rectangle box = TakeArtBox(ref cell);
            EditorIcons.Draw(g, EditorIcon.Link, box, EditorTheme.Accent(surface));
        }

        /// <summary>
        ///     Reserves the art box at the left of a cell and hands back the rest for the text.
        /// </summary>
        /// <remarks>
        ///     By reference, so the caller cannot forget to shrink the cell and paint the text over
        ///     the art it just drew.
        /// </remarks>
        private static Rectangle TakeArtBox(ref Rectangle cell) {
            int top = cell.Y + Math.Max(0, (cell.Height - ArtSide) / 2);
            var box = new Rectangle(cell.X + 2, top, ArtSide, ArtSide);

            int taken = ArtSide + ArtGap;
            cell = new Rectangle(cell.X + taken, cell.Y, Math.Max(0, cell.Width - taken), cell.Height);

            return box;
        }
    }
}
