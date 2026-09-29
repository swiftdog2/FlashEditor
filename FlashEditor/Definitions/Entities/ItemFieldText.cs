using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace FlashEditor.Definitions.Entities {
    /// <summary>
    ///     The item fields a grid cell shows as text - menu options, recolours, params and wear
    ///     offsets - and the parse of an edit back into them.
    /// </summary>
    /// <remarks>
    ///     Every parse refuses malformed text with a <see cref="FormatException"/> naming what is
    ///     wrong, rather than keeping the part it understood: a cell holds several values, so a
    ///     partial parse would drop some of an edit while the rest of it took. Each format parses back
    ///     to exactly the value it shows for every item in both caches, which
    ///     <c>RealCacheNewItemTests.EveryItem_TextCellsParseBackToTheValuesTheyShow</c> pins.
    /// </remarks>
    public static class ItemFieldText {
        private const char OptionSeparator = '|';

        /// <summary>Shows five menu slots separated by <c>|</c>, an empty slot being no option.</summary>
        /// <param name="options">The five slots; a null slot has no option.</param>
        /// <returns>The cell text.</returns>
        public static string FormatOptions(string[] options) {
            if (options == null)
                throw new ArgumentNullException(nameof(options));
            return string.Join(OptionSeparator, options);
        }

        /// <summary>Parses five <c>|</c>-separated menu slots, trimming each; an empty slot is null.</summary>
        /// <param name="text">The cell text.</param>
        /// <returns>The five slots.</returns>
        /// <exception cref="FormatException">The text does not hold exactly five slots.</exception>
        public static string[] ParseOptions(string text) {
            string[] parts = (text ?? string.Empty).Split(OptionSeparator);
            if (parts.Length != 5)
                throw new FormatException(
                    "An item has 5 menu slots, written separated by |, with an empty slot for none - " +
                    "for example |Wield|||drop. This has " + parts.Length + ".");

            var options = new string[5];
            for (int i = 0; i < parts.Length; i++) {
                string option = parts[i].Trim();
                options[i] = option.Length == 0 ? null : option;
            }
            return options;
        }

        /// <summary>
        ///     Shows each recolour pair as <c>from&gt;to</c>, comma separated, as unsigned colour words.
        /// </summary>
        /// <param name="from">The colours the model carries, or null for no recolour.</param>
        /// <param name="to">What each is drawn as instead.</param>
        /// <returns>The cell text, empty for no recolour.</returns>
        public static string FormatRecolours(short[] from, short[] to) {
            if (from == null)
                return string.Empty;

            var text = new StringBuilder();
            for (int i = 0; i < from.Length; i++) {
                if (i > 0)
                    text.Append(", ");
                text.Append((ushort) from[i]).Append('>').Append((ushort) to[i]);
            }
            return text.ToString();
        }

        /// <summary>Parses comma-separated <c>from&gt;to</c> colour-word pairs, each 0 to 65535.</summary>
        /// <param name="text">The cell text.</param>
        /// <returns>The pairs, or null when the text is blank.</returns>
        /// <exception cref="FormatException">A pair is malformed or out of range, or there are over 255.</exception>
        public static (short[] From, short[] To)? ParseRecolours(string text) {
            if (string.IsNullOrWhiteSpace(text))
                return null;

            string[] pairs = text.Split(',');
            RequireByteCount(pairs.Length, "recolour pairs");

            var from = new short[pairs.Length];
            var to = new short[pairs.Length];
            for (int i = 0; i < pairs.Length; i++) {
                string[] sides = pairs[i].Split('>');
                if (sides.Length != 2)
                    throw new FormatException(
                        "\"" + pairs[i].Trim() + "\" is not a recolour. Write each as from>to, for example 6798>5231.");
                from[i] = (short) ParseColourWord(sides[0]);
                to[i] = (short) ParseColourWord(sides[1]);
            }
            return (from, to);
        }

        /// <summary>
        ///     Shows each param as <c>key=value</c>, comma separated, with a string value quoted and its
        ///     quotes and backslashes escaped.
        /// </summary>
        /// <param name="parameters">The params, or null for none.</param>
        /// <returns>The cell text, empty for none.</returns>
        public static string FormatParams(IReadOnlyDictionary<int, object> parameters) {
            if (parameters == null)
                return string.Empty;

            var text = new StringBuilder();
            foreach (KeyValuePair<int, object> parameter in parameters) {
                if (text.Length > 0)
                    text.Append(", ");
                text.Append(parameter.Key.ToString(CultureInfo.InvariantCulture)).Append('=');
                if (parameter.Value is string value)
                    text.Append('"').Append(value.Replace("\\", "\\\\").Replace("\"", "\\\"")).Append('"');
                else
                    text.Append(Convert.ToInt32(parameter.Value, CultureInfo.InvariantCulture)
                        .ToString(CultureInfo.InvariantCulture));
            }
            return text.ToString();
        }

        /// <summary>
        ///     Parses comma-separated <c>key=value</c> params: a key 0 to 16777215, a value an int or a
        ///     quoted string.
        /// </summary>
        /// <param name="text">The cell text.</param>
        /// <returns>The params, or null when the text is blank.</returns>
        /// <exception cref="FormatException">
        ///     A param is malformed, a key is out of range or repeated, or there are over 255.
        /// </exception>
        public static SortedDictionary<int, object> ParseParams(string text) {
            if (string.IsNullOrWhiteSpace(text))
                return null;

            var parameters = new SortedDictionary<int, object>();
            int at = 0;
            while (true) {
                int equals = text.IndexOf('=', at);
                if (equals < 0)
                    throw new FormatException(
                        "\"" + text.Substring(at).Trim() + "\" is not a param. Write each as key=value, for " +
                        "example 644=1580 or 900=\"text\".");

                string keyText = text.Substring(at, equals - at).Trim();
                if (!int.TryParse(keyText, NumberStyles.None, CultureInfo.InvariantCulture, out int key) || key > 0xFFFFFF)
                    throw new FormatException("\"" + keyText + "\" is not a param key: a key is a whole number from 0 to 16777215.");
                if (parameters.ContainsKey(key))
                    throw new FormatException("Param " + key + " is given twice.");

                at = SkipSpaces(text, equals + 1);
                object value;
                if (at < text.Length && text[at] == '"') {
                    value = ReadQuoted(text, ref at);
                }
                else {
                    int end = text.IndexOf(',', at);
                    if (end < 0)
                        end = text.Length;
                    string valueText = text.Substring(at, end - at).Trim();
                    if (!int.TryParse(valueText, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out int number))
                        throw new FormatException(
                            "Param " + key + "'s value \"" + valueText + "\" is neither a whole number nor a quoted string.");
                    value = number;
                    at = end;
                }

                parameters.Add(key, value);
                RequireByteCount(parameters.Count, "params");

                at = SkipSpaces(text, at);
                if (at >= text.Length)
                    return parameters;
                if (text[at] != ',')
                    throw new FormatException("Param " + key + " is followed by \"" + text.Substring(at) +
                                              "\" where a comma or the end should be.");
                at++;
            }
        }

        /// <summary>Shows a wear offset as <c>x, y, z</c> in the decoded units the client adds to the model.</summary>
        /// <param name="x">The X offset.</param>
        /// <param name="y">The Y offset.</param>
        /// <param name="z">The Z offset.</param>
        /// <returns>The cell text.</returns>
        public static string FormatOffset(int x, int y, int z) {
            return string.Format(CultureInfo.InvariantCulture, "{0}, {1}, {2}", x, y, z);
        }

        /// <summary>
        ///     Parses a wear offset <c>x, y, z</c>, each a multiple of 4 from -512 to 508.
        /// </summary>
        /// <remarks>
        ///     Opcodes 125 and 126 store each axis as a signed byte the client shifts left by two
        ///     (<c>ItemDefinition.java:971-980</c>), so no other value can be written.
        /// </remarks>
        /// <param name="text">The cell text.</param>
        /// <returns>The offset.</returns>
        /// <exception cref="FormatException">The text is not three storable offsets.</exception>
        public static (int X, int Y, int Z) ParseOffset(string text) {
            string[] parts = (text ?? string.Empty).Split(',');
            if (parts.Length != 3)
                throw new FormatException("A wear offset is three numbers, x, y, z - for example 12, -44, 24.");

            var axes = new int[3];
            for (int i = 0; i < 3; i++) {
                string part = parts[i].Trim();
                if (!int.TryParse(part, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out int value) ||
                    value < -512 || value > 508 || value % 4 != 0)
                    throw new FormatException(
                        "\"" + part + "\" cannot be stored as a wear offset: each axis is a multiple of 4 from " +
                        "-512 to 508, because the item stores it as a signed byte times four.");
                axes[i] = value;
            }
            return (axes[0], axes[1], axes[2]);
        }

        private static int ParseColourWord(string text) {
            string trimmed = text.Trim();
            if (!int.TryParse(trimmed, NumberStyles.None, CultureInfo.InvariantCulture, out int word) || word > 0xFFFF)
                throw new FormatException("\"" + trimmed + "\" is not a colour word: a colour is a whole number from 0 to 65535.");
            return word;
        }

        private static void RequireByteCount(int count, string what) {
            if (count > 255)
                throw new FormatException("An item holds at most 255 " + what + ", because the count is stored in one byte.");
        }

        private static int SkipSpaces(string text, int at) {
            while (at < text.Length && char.IsWhiteSpace(text[at]))
                at++;
            return at;
        }

        private static string ReadQuoted(string text, ref int at) {
            var value = new StringBuilder();
            for (int i = at + 1; i < text.Length; i++) {
                char c = text[i];
                if (c == '\\' && i + 1 < text.Length) {
                    value.Append(text[++i]);
                }
                else if (c == '"') {
                    at = i + 1;
                    return value.ToString();
                }
                else {
                    value.Append(c);
                }
            }
            throw new FormatException("A quoted param value is not closed: " + text.Substring(at));
        }
    }
}
