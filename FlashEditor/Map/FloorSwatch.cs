using System;
using System.Text;
using FlashEditor.Definitions.Config;

namespace FlashEditor.Map {
    /// <summary>How a floor palette swatch is filled.</summary>
    public enum FloorSwatchFill {
        /// <summary>The map draws <see cref="FloorSwatch.MapRgb"/>.</summary>
        Colour,

        /// <summary>The map draws nothing and hides the underlay too; see <see cref="FloorColour.IsHole"/>.</summary>
        Hole,

        /// <summary>The overlay draws no colour of its own and the underlay beneath shows.</summary>
        UnderlayShows
    }

    /// <summary>
    ///     One floor record as the palette shows it: the colour the top-down map draws, and a caption
    ///     naming every colour the record carries.
    /// </summary>
    /// <remarks>
    ///     The caption lists the primary colour separately because it is what the in-game 3D ground
    ///     uses, while the map draws the secondary first - an overlay can be green in game and brown
    ///     on the map, and only the caption says so.
    /// </remarks>
    /// <param name="Kind">Which floor table the record is in.</param>
    /// <param name="Id">The definition id.</param>
    /// <param name="Fill">How the swatch is filled.</param>
    /// <param name="MapRgb">The 24-bit colour the map draws; meaningful only for <see cref="FloorSwatchFill.Colour"/>.</param>
    /// <param name="TextureId">The texture the record names, or -1.</param>
    /// <param name="Caption">The hover caption.</param>
    public sealed record FloorSwatch(FloorKind Kind, int Id, FloorSwatchFill Fill, int MapRgb, int TextureId, string Caption) {
        private const string Gap = "   ";

        /// <summary>Builds the swatch for an overlay record.</summary>
        /// <param name="overlay">The record, with its <see cref="FloorOverlayDefinition.Id"/> set.</param>
        /// <param name="textureHsl">
        ///     Resolves a texture to packed HSL; <see cref="FloorColour.TextureHsl"/> in the editor.
        /// </param>
        /// <returns>The swatch.</returns>
        public static FloorSwatch Overlay(FloorOverlayDefinition overlay, Func<int, int> textureHsl) {
            int hsl = FloorColour.OverlayHsl(overlay, textureHsl);
            FloorSwatchFill fill = hsl != MapPalette.NoColour ? FloorSwatchFill.Colour
                : FloorColour.IsHole(overlay, hsl) ? FloorSwatchFill.Hole
                : FloorSwatchFill.UnderlayShows;
            int mapRgb = MapPalette.ToRgb(hsl);

            var caption = new StringBuilder(FloorBrush.Describe(new FloorPick(FloorKind.Overlay, overlay.Id)));
            caption.Append(Gap).Append(fill switch {
                FloorSwatchFill.Hole => "map hole (nothing drawn)",
                FloorSwatchFill.UnderlayShows => "map none (underlay shows)",
                _ => "map " + Hex(mapRgb)
            });

            caption.Append(Gap).Append("primary ").Append(
                !overlay.HasPrimaryRgb ? "none"
                : overlay.PrimaryRgb == FloorOverlayDefinition.TransparentRgb ? "transparent (underlay shows)"
                : Hex(overlay.PrimaryRgb));

            if (overlay.SecondaryRgb != -1)
                caption.Append(Gap).Append("secondary ").Append(Hex(overlay.SecondaryRgb));

            AppendTexture(caption, overlay.TextureId);

            return new FloorSwatch(FloorKind.Overlay, overlay.Id, fill, mapRgb, overlay.TextureId, caption.ToString());
        }

        /// <summary>Builds the swatch for an underlay record.</summary>
        /// <param name="underlay">The record, with its <see cref="FloorUnderlayDefinition.Id"/> set.</param>
        /// <returns>The swatch.</returns>
        public static FloorSwatch Underlay(FloorUnderlayDefinition underlay) {
            int mapRgb = MapPalette.ToRgb(FloorColour.UnderlayHsl(UnderlayColour.FromRgb(underlay.Rgb)));

            var caption = new StringBuilder(FloorBrush.Describe(new FloorPick(FloorKind.Underlay, underlay.Id)));
            caption.Append(Gap).Append("map ").Append(Hex(mapRgb));
            caption.Append(Gap).Append("colour ").Append(Hex(underlay.Rgb));
            AppendTexture(caption, underlay.TextureId);

            return new FloorSwatch(FloorKind.Underlay, underlay.Id, FloorSwatchFill.Colour, mapRgb,
                underlay.TextureId, caption.ToString());
        }

        private static void AppendTexture(StringBuilder caption, int textureId) {
            if (textureId >= 0)
                caption.Append(Gap).Append("texture ").Append(textureId);
        }

        private static string Hex(int rgb) => "#" + (rgb & 0xFFFFFF).ToString("X6");
    }
}
