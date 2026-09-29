using System;
using System.Collections.Generic;
using System.Globalization;
using FlashEditor.Cache;
using FlashEditor.Definitions.Editing;
using FlashEditor.IO;

namespace FlashEditor.Definitions.Entities {
    /// <summary>
    ///     Sets an existing item's inventory view (opcodes 4 to 8 and 95) and params (opcode 249)
    ///     from <c>name=value</c> assignments, and stages the result.
    /// </summary>
    /// <remarks>
    ///     The names are <c>zoom</c>, <c>xan2d</c>, <c>yan2d</c>, <c>zan2d</c>, <c>xoff2d</c>,
    ///     <c>yoff2d</c> and <c>param.&lt;key&gt;</c>, whose value is an int or a quoted string as
    ///     <see cref="ItemFieldText.ParseParams"/> reads it. Every assignment is parsed before any is
    ///     applied, so a refused one leaves the item as it was.
    /// </remarks>
    public static class ItemViewEdit {
        private const string ParamPrefix = "param.";

        /// <summary>Applies <paramref name="assignments"/> to item <paramref name="itemId"/> and stages it.</summary>
        /// <remarks>
        ///     Staged through <see cref="DefinitionWriter.Save"/>, so an edit that leaves the encoding
        ///     unchanged stages nothing.
        /// </remarks>
        /// <param name="cache">The open cache to stage into.</param>
        /// <param name="itemId">The item to edit.</param>
        /// <param name="assignments">The <c>name=value</c> assignments, applied in order.</param>
        /// <returns>The view before and after, and whether anything was staged.</returns>
        /// <exception cref="FormatException">An assignment names no field or holds a value the field cannot store.</exception>
        public static ItemEditOutcome Apply(RSCache cache, int itemId, IEnumerable<string> assignments) {
            if (cache == null)
                throw new ArgumentNullException(nameof(cache));
            if (assignments == null)
                throw new ArgumentNullException(nameof(assignments));

            var edits = new List<Action<ItemDefinition>>();
            foreach (string assignment in assignments)
                edits.Add(Parse(assignment));

            CacheAddressing addressing = CacheAddressing.For(RSConstants.ITEM_DEFINITIONS_INDEX);
            ItemDefinition item = ItemDefinition.DecodeFromStream(new JagStream(cache.ReadFileBytes(
                RSConstants.ITEM_DEFINITIONS_INDEX, addressing.GroupOf(itemId), addressing.FileOf(itemId))));
            item.id = itemId;

            string before = Describe(item);
            foreach (Action<ItemDefinition> edit in edits)
                edit(item);

            bool staged = DefinitionWriter.Save(cache, RSConstants.ITEM_DEFINITIONS_INDEX, itemId, item.Encode().ToArray());
            return new ItemEditOutcome(before, Describe(item), staged);
        }

        /// <summary>One line naming every field <see cref="Apply"/> can set, as it can be read back.</summary>
        /// <param name="item">The item.</param>
        /// <returns>The line.</returns>
        public static string Describe(ItemDefinition item) {
            if (item == null)
                throw new ArgumentNullException(nameof(item));

            string parameters = item.itemParams == null || item.itemParams.Count == 0
                ? "none"
                : ItemFieldText.FormatParams(item.itemParams);
            return string.Format(CultureInfo.InvariantCulture,
                "zoom {0} xan2d {1} yan2d {2} zan2d {3} xoff2d {4} yoff2d {5} params {6}",
                item.modelZoom, item.modelRotation1, item.modelRotation2, item.zan2d,
                item.modelOffsetX, item.modelOffsetY, parameters);
        }

        private static Action<ItemDefinition> Parse(string assignment) {
            int equals = (assignment ?? string.Empty).IndexOf('=');
            if (equals <= 0)
                throw new FormatException("\"" + assignment + "\" is not an assignment. Write name=value, for example zoom=1850.");

            string name = assignment!.Substring(0, equals).Trim();
            string value = assignment.Substring(equals + 1);

            if (name.StartsWith(ParamPrefix, StringComparison.Ordinal)) {
                SortedDictionary<int, object>? parsed = ItemFieldText.ParseParams(name.Substring(ParamPrefix.Length) + "=" + value);
                if (parsed == null)
                    throw new FormatException("\"" + assignment + "\" names no param key.");
                return item => {
                    item.itemParams ??= new SortedDictionary<int, object>();
                    foreach (KeyValuePair<int, object> parameter in parsed)
                        item.itemParams[parameter.Key] = parameter.Value;
                };
            }

            switch (name) {
                case "zoom": { int v = Unsigned(name, value); return item => item.modelZoom = v; }
                case "xan2d": { int v = Unsigned(name, value); return item => item.modelRotation1 = v; }
                case "yan2d": { int v = Unsigned(name, value); return item => item.modelRotation2 = v; }
                case "zan2d": { int v = Unsigned(name, value); return item => item.zan2d = v; }
                case "xoff2d": { int v = Signed(name, value); return item => item.modelOffsetX = v; }
                case "yoff2d": { int v = Signed(name, value); return item => item.modelOffsetY = v; }
                default:
                    throw new FormatException("\"" + name + "\" is not an item view field. The fields are zoom, xan2d, " +
                                              "yan2d, zan2d, xoff2d, yoff2d and param.<key>.");
            }
        }

        //Opcodes 4, 5, 6 and 95 are read unsigned; 7 and 8 are read unsigned and folded to signed
        //(ItemDefinition.java:893-899, 939-941), so each is a short of that signedness.
        private static int Unsigned(string name, string text) => Number(name, text, 0, ushort.MaxValue);

        private static int Signed(string name, string text) => Number(name, text, short.MinValue, short.MaxValue);

        private static int Number(string name, string text, int min, int max) {
            string trimmed = text.Trim();
            if (!int.TryParse(trimmed, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out int value) ||
                value < min || value > max)
                throw new FormatException("\"" + trimmed + "\" cannot be stored as " + name + ": it is a whole number from " +
                                          min + " to " + max + ".");
            return value;
        }
    }

    /// <summary>What <see cref="ItemViewEdit.Apply"/> did.</summary>
    /// <param name="Before">The item's view and params before the edit, as <see cref="ItemViewEdit.Describe"/> writes them.</param>
    /// <param name="After">The same after the edit.</param>
    /// <param name="Staged">Whether the edit changed the item's bytes and so was staged.</param>
    public sealed record ItemEditOutcome(string Before, string After, bool Staged);
}
