using System;
using System.Collections.Generic;
using System.Linq;
using FlashEditor.Cache;
using FlashEditor.Definitions.Editing;
using FlashEditor.IO;

namespace FlashEditor.Definitions.Entities {
    /// <summary>
    ///     Sets an existing item's inventory view (opcodes 4 to 8 and 95) and params (opcode 249)
    ///     from <c>name=value</c> assignments, and stages the result.
    /// </summary>
    /// <remarks>
    ///     Each view name writes through the Items grid's own column (<see cref="ItemListDescriptor"/>),
    ///     so a value the grid refuses is refused here with the same range. <c>param.&lt;key&gt;</c>
    ///     sets or replaces that one key and keeps the rest, where the grid's Params cell replaces
    ///     the whole block; its value is an int or a quoted string as
    ///     <see cref="ItemFieldText.ParseParams"/> reads it. Nothing is staged until every assignment
    ///     has been accepted.
    /// </remarks>
    public static class ItemViewEdit {
        private const string ParamPrefix = "param.";

        //Assignment name to Items grid column header, in the order Describe prints them.
        private static readonly (string Name, string Header)[] ViewFields = {
            ("zoom", "Zoom"), ("xan2d", "Rotate1"), ("yan2d", "Rotate2"),
            ("zan2d", "Zan2d"), ("xoff2d", "OffsetX"), ("yoff2d", "OffsetY")
        };

        private static readonly ItemListDescriptor Items = new ItemListDescriptor();

        /// <summary>The view field names an assignment can set, besides <c>param.&lt;key&gt;</c>.</summary>
        public static IReadOnlyList<string> FieldNames { get; } = ViewFields.Select(f => f.Name).ToArray();

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

            ItemDefinition item = Load(cache, itemId);
            string before = Describe(item);
            foreach (string assignment in assignments)
                Assign(item, assignment);

            bool staged = DefinitionWriter.Save(cache, Items.IndexId, itemId, Items.Encode(item).ToArray());
            return new ItemEditOutcome(before, Describe(item), staged);
        }

        /// <summary>One line naming every field <see cref="Apply"/> can set, as the cache holds them now.</summary>
        /// <param name="cache">The open cache.</param>
        /// <param name="itemId">The item.</param>
        /// <returns>The line, in the form <see cref="ItemEditOutcome.Before"/> takes.</returns>
        public static string Describe(RSCache cache, int itemId) {
            if (cache == null)
                throw new ArgumentNullException(nameof(cache));
            return Describe(Load(cache, itemId));
        }

        private static ItemDefinition Load(RSCache cache, int itemId) {
            CacheAddressing addressing = CacheAddressing.For(Items.IndexId);
            var address = new DefinitionAddress(addressing.GroupOf(itemId), addressing.FileOf(itemId), itemId);
            return Items.Decode(cache, address,
                new JagStream(cache.ReadFileBytes(Items.IndexId, address.GroupId, address.FileId)));
        }

        private static string Describe(ItemDefinition item) {
            IEnumerable<string> view = ViewFields.Select(f => f.Name + " " + Column(f.Header).Read(item));
            string parameters = item.itemParams == null || item.itemParams.Count == 0
                ? "none"
                : ItemFieldText.FormatParams(item.itemParams);
            return string.Join(" ", view) + " params " + parameters;
        }

        private static void Assign(ItemDefinition item, string assignment) {
            int equals = (assignment ?? string.Empty).IndexOf('=');
            if (equals <= 0)
                throw new FormatException("\"" + assignment + "\" is not an assignment. Write name=value, for example zoom=1850.");

            string name = assignment!.Substring(0, equals).Trim();
            string value = assignment.Substring(equals + 1).Trim();

            if (name.StartsWith(ParamPrefix, StringComparison.Ordinal)) {
                SortedDictionary<int, object>? parsed = ItemFieldText.ParseParams(name.Substring(ParamPrefix.Length) + "=" + value);
                item.itemParams ??= new SortedDictionary<int, object>();
                foreach (KeyValuePair<int, object> parameter in parsed!)
                    item.itemParams[parameter.Key] = parameter.Value;
                return;
            }

            string? header = ViewFields.FirstOrDefault(f => f.Name == name).Header;
            if (header == null)
                throw new FormatException("\"" + name + "\" is not an item view field. The fields are " +
                                          string.Join(", ", FieldNames) + " and param.<key>.");

            string? refusal = Column(header).TryWrite(item, value);
            if (refusal != null)
                throw new FormatException(name + ": " + refusal);
        }

        private static DefinitionColumn Column(string header) => Items.Columns.First(c => c.Header == header);
    }

    /// <summary>What <see cref="ItemViewEdit.Apply"/> did.</summary>
    /// <param name="Before">The item's view and params before the edit, as <see cref="ItemViewEdit.Describe(RSCache, int)"/> writes them.</param>
    /// <param name="After">The same after the edit.</param>
    /// <param name="Staged">Whether the edit changed the item's bytes and so was staged.</param>
    public sealed record ItemEditOutcome(string Before, string After, bool Staged);
}
