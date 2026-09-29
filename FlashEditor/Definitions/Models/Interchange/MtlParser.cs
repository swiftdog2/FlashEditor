using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;

namespace FlashEditor.Definitions.Models.Interchange {
    /// <summary>
    ///     Reads the diffuse colour (<c>Kd</c>) of each material in an MTL, and nothing else.
    /// </summary>
    /// <remarks>
    ///     A model face stores one colour, so of everything a material can state only <c>Kd</c> has
    ///     somewhere to go. The other statements are skipped rather than rejected. A malformed
    ///     <c>Kd</c> is rejected, because reading past it would give faces the wrong colour silently.
    /// </remarks>
    public static class MtlParser {
        /// <summary>Reads an MTL from text.</summary>
        /// <param name="text">The whole file.</param>
        /// <returns>
        ///     Each material that states a <c>Kd</c>, mapped to it as 24-bit RGB: each channel
        ///     clamped to 0..1 and rounded to the nearest of 0..255. A material with no <c>Kd</c>
        ///     is absent.
        /// </returns>
        /// <exception cref="ModelImportException">A <c>Kd</c> line is malformed or belongs to no material.</exception>
        public static IReadOnlyDictionary<string, int> Parse(string text) {
            if (text == null)
                throw new ArgumentNullException(nameof(text));

            var colours = new Dictionary<string, int>(StringComparer.Ordinal);
            string? material = null;
            int lineNumber = 0;

            using var reader = new StringReader(text);
            string? line;
            while ((line = reader.ReadLine()) != null) {
                lineNumber++;

                int comment = line.IndexOf('#');
                if (comment >= 0)
                    line = line.Substring(0, comment);

                string[] parts = line.Split(Whitespace, StringSplitOptions.RemoveEmptyEntries);
                if (parts.Length == 0)
                    continue;

                if (parts[0] == "newmtl") {
                    material = parts.Length >= 2 ? parts[1] : null;
                    continue;
                }

                if (parts[0] != "Kd")
                    continue;

                if (material == null) {
                    throw new ModelImportException(
                        $"Line {lineNumber} of the MTL states a Kd before any newmtl names a material for it.");
                }

                if (parts.Length < 4) {
                    throw new ModelImportException(
                        $"Line {lineNumber} of the MTL: a Kd needs three numbers, and this one has {parts.Length - 1}.");
                }

                colours[material] = (Channel(parts[1], lineNumber) << 16) |
                                    (Channel(parts[2], lineNumber) << 8) |
                                    Channel(parts[3], lineNumber);
            }

            return colours;
        }

        /// <summary>Reads an MTL from a file on disk.</summary>
        /// <param name="path">The file to read.</param>
        /// <returns>See <see cref="Parse"/>.</returns>
        /// <exception cref="ModelImportException">A <c>Kd</c> line is malformed or belongs to no material.</exception>
        public static IReadOnlyDictionary<string, int> ParseFile(string path) {
            if (path == null)
                throw new ArgumentNullException(nameof(path));
            return Parse(File.ReadAllText(path));
        }

        private static int Channel(string field, int lineNumber) {
            if (!double.TryParse(field, NumberStyles.Float, CultureInfo.InvariantCulture, out double value)
                || double.IsNaN(value) || double.IsInfinity(value)) {
                throw new ModelImportException(
                    $"Line {lineNumber} of the MTL has \"{field}\" where a Kd channel belongs.");
            }

            return (int) Math.Round(Math.Clamp(value, 0.0, 1.0) * 255.0, MidpointRounding.AwayFromZero);
        }

        private static readonly char[] Whitespace = { ' ', '\t', '\r' };
    }
}
