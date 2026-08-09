using System;
using System.Drawing;

namespace FlashEditor.Definitions.Editing {
    /// <summary>
    ///     What a cell draws over and above its text.
    /// </summary>
    public enum DefinitionCellArt {
        /// <summary>Text only, which is what every column produced before this existed.</summary>
        None,

        /// <summary>A packed <c>0xRRGGBB</c> colour, drawn as a swatch with its hex kept beside it.</summary>
        Swatch,

        /// <summary>An id naming a picture in another index, drawn as a tile with the id beside it.</summary>
        Thumbnail,

        /// <summary>An id naming a record in another index, drawn as something the user can follow.</summary>
        Link
    }

    /// <summary>
    ///     What one cell should draw, stated by the descriptor and resolved by the panel.
    /// </summary>
    /// <remarks>
    ///     A description of the <i>value</i> rather than a renderer instance, for the same reason
    ///     <see cref="DefinitionColumn"/> carries a delegate pair rather than a property name: a
    ///     descriptor states that a number is an index-8 id, and knows nothing about GDI, the
    ///     theme, where the pixels come from, or what following a link should do. Those differ per
    ///     host, and a descriptor that decided them would have to know about every tab - which is
    ///     the coupling descriptors exist to avoid.
    ///     <para>
    ///     <c>default(DefinitionCellVisual)</c> is <see cref="DefinitionCellArt.None"/>, so "this
    ///     cell has no visual" costs no allocation and needs no null check anywhere.
    ///     </para>
    /// </remarks>
    public readonly struct DefinitionCellVisual : IEquatable<DefinitionCellVisual> {
        private DefinitionCellVisual(DefinitionCellArt art, int packedRgb, int indexId, int targetId) {
            Art = art;
            PackedRgb = packedRgb;
            IndexId = indexId;
            TargetId = targetId;
        }

        /// <summary>A cell that draws nothing but its text.</summary>
        public static DefinitionCellVisual None => default;

        /// <summary>A colour swatch.</summary>
        /// <param name="packedRgb">The colour, packed as <c>0xRRGGBB</c>.</param>
        /// <returns>The visual.</returns>
        public static DefinitionCellVisual Swatch(int packedRgb) {
            return new DefinitionCellVisual(DefinitionCellArt.Swatch, packedRgb, -1, -1);
        }

        /// <summary>A picture resolved from an index and an id.</summary>
        /// <param name="indexId">The index the id addresses.</param>
        /// <param name="id">The id.</param>
        /// <returns>The visual.</returns>
        public static DefinitionCellVisual Thumbnail(int indexId, int id) {
            return new DefinitionCellVisual(DefinitionCellArt.Thumbnail, 0, indexId, id);
        }

        /// <summary>A reference to a record in another index.</summary>
        /// <param name="indexId">The index the id addresses.</param>
        /// <param name="id">The id.</param>
        /// <returns>The visual.</returns>
        public static DefinitionCellVisual Link(int indexId, int id) {
            return new DefinitionCellVisual(DefinitionCellArt.Link, 0, indexId, id);
        }

        /// <summary>What this cell draws.</summary>
        public DefinitionCellArt Art { get; }

        /// <summary>The colour, packed as <c>0xRRGGBB</c>, when <see cref="Art"/> is a swatch.</summary>
        public int PackedRgb { get; }

        /// <summary>The index the id addresses, for a thumbnail or a link.</summary>
        public int IndexId { get; }

        /// <summary>The id, for a thumbnail or a link.</summary>
        public int TargetId { get; }

        /// <summary>The swatch colour, opaque.</summary>
        /// <remarks>
        ///     The alpha is forced rather than taken from the packed value. Every colour the cache
        ///     stores in this form is three bytes, so the top byte is zero and
        ///     <see cref="Color.FromArgb(int)"/> would read every swatch as fully transparent and
        ///     draw nothing at all.
        /// </remarks>
        public Color SwatchColour => Color.FromArgb(
            0xFF, (PackedRgb >> 16) & 0xFF, (PackedRgb >> 8) & 0xFF, PackedRgb & 0xFF);

        /// <inheritdoc/>
        public bool Equals(DefinitionCellVisual other) {
            return Art == other.Art && PackedRgb == other.PackedRgb
                && IndexId == other.IndexId && TargetId == other.TargetId;
        }

        /// <inheritdoc/>
        public override bool Equals(object? obj) {
            return obj is DefinitionCellVisual other && Equals(other);
        }

        /// <inheritdoc/>
        public override int GetHashCode() {
            return HashCode.Combine((int) Art, PackedRgb, IndexId, TargetId);
        }
    }

    /// <summary>
    ///     Resolves an index and an id to a grid tile, without ever blocking a paint.
    /// </summary>
    /// <remarks>
    ///     A panel with no source draws a thumbnail column as plain text, so a tab that has not
    ///     opted in is not broken by a descriptor that asks for pictures.
    /// </remarks>
    public interface IDefinitionThumbnailSource {
        /// <summary>
        ///     The tile for an id, or null when it is not resolved yet.
        /// </summary>
        /// <remarks>
        ///     <b>Never blocks and never decodes on the calling thread.</b> A miss queues the work
        ///     and returns null so the renderer draws a placeholder and the grid stays scrollable.
        ///     Called from the paint, which is what makes "recently used" mean "recently on
        ///     screen".
        /// </remarks>
        /// <param name="indexId">The index the id addresses.</param>
        /// <param name="id">The id.</param>
        /// <param name="side">The tile side in pixels.</param>
        /// <returns>The tile, or null.</returns>
        Bitmap? TryGet(int indexId, int id, int side);

        /// <summary>Raised, coalesced, when at least one queued tile has landed.</summary>
        event EventHandler? TilesReady;
    }

    /// <summary>
    ///     Which row, and what the cell the user activated named.
    /// </summary>
    /// <remarks>
    ///     Carries the row as well as the visual because a handler usually wants both: the visual
    ///     says where to go, and the row says where the user came from, which is what a back stack
    ///     needs to be able to return.
    /// </remarks>
    public sealed class DefinitionCellActivatedEventArgs : EventArgs {
        /// <summary>Creates the event data.</summary>
        /// <param name="row">The row whose cell was activated.</param>
        /// <param name="visual">What that cell named.</param>
        public DefinitionCellActivatedEventArgs(object row, DefinitionCellVisual visual) {
            Row = row ?? throw new ArgumentNullException(nameof(row));
            Visual = visual;
        }

        /// <summary>The row whose cell was activated.</summary>
        public object Row { get; }

        /// <summary>What the cell named: the index and the id.</summary>
        public DefinitionCellVisual Visual { get; }
    }
}
