using System;
using FlashEditor.Definitions.Sprites;
using FlashEditor.Definitions.Config;

namespace FlashEditor.Map {
    /// <summary>
    ///     The colour the top-down map draws for a floor record, as packed map HSL.
    /// </summary>
    /// <remarks>
    ///     The one place the choice is made. <see cref="MapRasteriser"/> draws through it and
    ///     <see cref="FloorMaterialPalette"/> builds its swatches through it, so a swatch cannot
    ///     show one colour while the map paints another.
    /// </remarks>
    public static class FloorColour {
        /// <summary>
        ///     Picks an overlay's map colour the way the client does.
        /// </summary>
        /// <remarks>
        ///     Secondary colour, then the texture's representative colour, then primary
        ///     (Node_Sub16.method1149). The primary is what the in-game 3D ground uses, so an overlay
        ///     with a secondary colour looks different on the map than in game.
        /// </remarks>
        /// <param name="overlay">The overlay record.</param>
        /// <param name="textureHsl">Resolves a texture id to packed HSL, or <see cref="MapPalette.NoColour"/>.</param>
        /// <returns>Packed map HSL, or <see cref="MapPalette.NoColour"/> when the map draws no overlay colour.</returns>
        public static int OverlayHsl(FloorOverlayDefinition overlay, Func<int, int> textureHsl) {
            if (overlay.SecondaryRgb != -1)
                return MapPalette.FromRgb(overlay.SecondaryRgb);

            int textured = overlay.TextureId < 0 ? MapPalette.NoColour : textureHsl(overlay.TextureId);
            if (textured != MapPalette.NoColour)
                return textured;

            if (!overlay.HasPrimaryRgb)
                return MapPalette.NoColour;

            return MapPalette.FromRgb(overlay.PrimaryRgb);
        }

        /// <summary>
        ///     Whether the overlay suppresses its tile completely, underlay included.
        /// </summary>
        /// <remarks>
        ///     An overlay with neither colour, whose primary is the transparent sentinel, draws
        ///     nothing and the underlay does NOT show through. That is what makes cave mouths and
        ///     dungeon voids read as voids instead of solid ground. An overlay with no stored primary
        ///     at all is not a hole: it lets the underlay show.
        /// </remarks>
        /// <param name="overlay">The overlay record.</param>
        /// <param name="overlayHsl">What <see cref="OverlayHsl"/> returned for it.</param>
        /// <returns><c>true</c> when the map draws the tile as a hole.</returns>
        public static bool IsHole(FloorOverlayDefinition overlay, int overlayHsl) =>
            overlayHsl == MapPalette.NoColour && overlay.SecondaryRgb == -1
            && overlay.PrimaryRgb == FloorOverlayDefinition.TransparentRgb;

        /// <summary>
        ///     A texture's declared colour, as packed map HSL, from the loaded texture table.
        /// </summary>
        /// <remarks>
        ///     <c>representativeHsl</c> is already a packed HSL in the same space as an overlay's primary
        ///     and secondary colours, and the client returns it verbatim
        ///     (Node_Sub16.java:75-80, <c>return class238.aShort1831</c>). It must not be routed
        ///     through <c>TextureManager.RepresentativeRgb</c>, which converts it to RGB through the
        ///     <em>model</em> palette for the renderer's benefit; doing that and folding the result
        ///     back through the map palette turns the whole world flat green and drains the colour
        ///     out of water.
        ///
        ///     <c>suppressTexture</c> is the client's <c>aBoolean1825</c> gate: when set, the texture
        ///     declines to stand in for a colour and the tile falls through to its primary.
        ///
        ///     Texture metadata is loaded by <c>GLTextureCache</c> at cache-open. Called before
        ///     that, or in a headless test, the table is empty and every texture resolves to
        ///     <see cref="MapPalette.NoColour"/>, so a textured overlay falls back to its primary.
        /// </remarks>
        /// <param name="textureId">A texture id, or -1 for none.</param>
        /// <returns>Packed map HSL, or <see cref="MapPalette.NoColour"/>.</returns>
        public static int TextureHsl(int textureId) {
            if (textureId < 0
                || !TextureManager.Textures.TryGetValue(textureId, out TextureDefinition def)
                || def == null || def.suppressTexture)
                return MapPalette.NoColour;

            //Both consumers clamp the lightness before the palette lookup (Class345.method3825,
            //reached from Class278.java:731 and from the scene path). A texture whose declared
            //lightness sits at either extreme would otherwise come out pure black or white.
            int hsl = def.representativeHsl & 0xFFFF;
            int lightness = Math.Clamp(hsl & 0x7F, 2, 126);
            return (hsl & 0xFF80) | lightness;
        }
    }
}
