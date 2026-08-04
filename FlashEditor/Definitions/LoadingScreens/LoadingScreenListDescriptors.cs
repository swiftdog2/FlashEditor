using System;
using System.Collections.Generic;
using FlashEditor.cache;
using FlashEditor.Definitions.Editing;

namespace FlashEditor.Definitions.LoadingScreens {
    /// <summary>
    ///     What the Loading Screens tab needs from a row whichever of index 33's two groups it holds.
    /// </summary>
    /// <remarks>
    ///     The manifest and the screens are two formats with two codecs, not two record types of one
    ///     format: group 0 holds a single versioned file listing which screens belong to which
    ///     category, group 1 holds the screens themselves. So the tab selects the group and this is
    ///     the smallest surface a shared detail pane can be written against.
    /// </remarks>
    public interface ILoadingScreenListing : IDetailRow {
        /// <summary>Where the record lives in the cache.</summary>
        DefinitionAddress Address { get; }
    }

    /// <summary>The single manifest file of group 0 as a list row.</summary>
    /// <remarks>
    ///     A one-row list rather than a bare detail pane, so the group reads the same way as the other
    ///     one and the file's address stays on screen.
    /// </remarks>
    public sealed class LoadingScreenManifestListing : ILoadingScreenListing {
        /// <summary>Binds the decoded manifest to where it came from.</summary>
        /// <param name="address">The group and file.</param>
        /// <param name="record">The decoded record.</param>
        public LoadingScreenManifestListing(DefinitionAddress address, LoadingScreenManifest record) {
            Address = address;
            Record = record ?? throw new ArgumentNullException(nameof(record));
        }

        /// <inheritdoc/>
        public DefinitionAddress Address { get; }

        /// <summary>The decoded record.</summary>
        public LoadingScreenManifest Record { get; }

        /// <summary>The file id within group 0.</summary>
        public int FileId => Address.FileId;

        /// <summary>
        ///     The format version, flagged when the 637 client would abandon the file.
        /// </summary>
        /// <remarks>
        ///     Above version 3 the client reads the version byte and stops
        ///     (<c>Class282.java:127-131</c>), so a higher version is not a newer manifest it reads
        ///     differently - it is one it does not read at all.
        /// </remarks>
        public string Version =>
            Record.Version > LoadingScreenManifest.MaxParsedVersion
                ? Record.Version + " (past what the 637 client parses)"
                : Record.Version.ToString();

        /// <summary>How many per-type version bytes the file carries.</summary>
        public int TypeVersions => Record.TypeVersions.Length;

        /// <summary>How many category rows the file carries.</summary>
        public int Categories => Record.Categories.Count;

        /// <summary>
        ///     The highest category index the client allocates for, beside the row count.
        /// </summary>
        /// <remarks>
        ///     Stored separately and deliberately not derived: the client sizes its arrays from this
        ///     and fills every slot no row named, so a manifest may legitimately declare more slots
        ///     than it has rows.
        /// </remarks>
        public string Slots {
            get {
                int slots = Record.MaxCategoryIndex + 1;
                return slots == Record.Categories.Count ? slots.ToString() : slots + " for " + Record.Categories.Count + " row(s)";
            }
        }

        /// <summary>The screen prepended to every category, or nothing.</summary>
        public string DefaultScreen => DetailText.OrAbsent(Record.DefaultScreenId);

        /// <summary>How many screen ids the categories name in total.</summary>
        public int ScreenIds {
            get {
                int total = 0;
                foreach (LoadingScreenCategory category in Record.Categories)
                    total += category.ScreenIds.Length;
                return total;
            }
        }

        /// <inheritdoc/>
        public string Summary =>
            "Manifest - version " + Version + " - " + Categories + " category row(s), " +
            ScreenIds + " screen id(s)";

        /// <inheritdoc/>
        public IReadOnlyList<DetailField> Fields {
            get {
                var fields = new List<DetailField> {
                    new DetailField("Format version", Version),
                    new DetailField("Per-type versions", DetailText.Ids(Record.TypeVersions)),
                    new DetailField("Category slots allocated", Slots),
                    new DetailField("Default screen id", DefaultScreen)
                };

                foreach (LoadingScreenCategory category in Record.Categories) {
                    fields.Add(new DetailField("Category " + category.Index,
                        "shuffle byte " + category.ShuffleStored + " (" + (category.Shuffles ? "shuffles" : "in order") +
                        "), screens " + DetailText.Ids(category.ScreenIds)));
                }

                if (Record.UnparsedTail.Length > 0)
                    fields.Add(new DetailField("Unparsed tail", Record.UnparsedTail.Length + " byte(s) kept verbatim"));

                return fields;
            }
        }
    }

    /// <summary>One loading screen from group 1 as a list row.</summary>
    public sealed class LoadingScreenListing : ILoadingScreenListing {
        /// <summary>Binds one decoded screen to where it came from.</summary>
        /// <param name="address">The group and file.</param>
        /// <param name="record">The decoded record.</param>
        public LoadingScreenListing(DefinitionAddress address, LoadingScreenDefinition record) {
            Address = address;
            Record = record ?? throw new ArgumentNullException(nameof(record));
        }

        /// <inheritdoc/>
        public DefinitionAddress Address { get; }

        /// <summary>The decoded record.</summary>
        public LoadingScreenDefinition Record { get; }

        /// <summary>The screen id, which is its file id in group 1.</summary>
        public int ScreenId => Record.Id;

        /// <summary>How long the screen stays up, in milliseconds.</summary>
        public int DurationMs => Record.DisplayDurationMs;

        /// <summary>The second timing field, whose role the 637 client does not settle.</summary>
        public int SecondTiming => Record.SecondTiming;

        /// <summary>How many drawables the screen carries.</summary>
        public int Elements => Record.Elements.Count;

        /// <summary>The element type indexes in draw order.</summary>
        /// <remarks>
        ///     In order rather than sorted or counted: the stored order is the z-order, so it is part
        ///     of what the screen is rather than a presentation choice.
        /// </remarks>
        public string ElementTypes {
            get {
                var parts = new List<string>(Record.Elements.Count);
                foreach (LoadingScreenElement element in Record.Elements)
                    parts.Add(element.TypeIndex.ToString());
                return string.Join(",", parts);
            }
        }

        /// <summary>The text any text element on this screen carries, joined.</summary>
        /// <remarks>
        ///     The one field of a screen that says what it is. Everything else is geometry, and index
        ///     33 carries no name hashes, so without this a screen is a number.
        /// </remarks>
        public string Text {
            get {
                var parts = new List<string>();
                foreach (LoadingScreenElement element in Record.Elements)
                    if (element is LoadingScreenTextElement text && text.Text.Length > 0)
                        parts.Add(text.Text);
                return string.Join(" | ", parts);
            }
        }

        /// <inheritdoc/>
        public string Summary =>
            "Screen " + ScreenId + " - " + DurationMs + " ms - " + Elements + " element(s)" +
            (Text.Length == 0 ? string.Empty : " - \"" + Text + "\"");

        /// <inheritdoc/>
        public IReadOnlyList<DetailField> Fields {
            get {
                var fields = new List<DetailField> {
                    new DetailField("Display duration", DurationMs + " ms"),
                    new DetailField("Second timing field", SecondTiming.ToString())
                };

                for (int i = 0; i < Record.Elements.Count; i++) {
                    LoadingScreenElement element = Record.Elements[i];
                    fields.Add(new DetailField("Element " + i + " - type " + element.TypeIndex,
                        LoadingScreenText.Describe(element)));
                }

                return fields;
            }
        }
    }

    /// <summary>How one loading-screen element is written into the detail pane.</summary>
    /// <remarks>
    ///     Only the elements the 637 client gives a meaning to are described in words. The rest are
    ///     rendered as the values they store, because naming them would be a guess - the client
    ///     decodes several of them into fields nothing reads.
    /// </remarks>
    internal static class LoadingScreenText {
        /// <summary>Describes one element.</summary>
        /// <param name="element">The element.</param>
        /// <returns>The description.</returns>
        internal static string Describe(LoadingScreenElement element) {
            switch (element) {
                case LoadingScreenTextElement text:
                    return "text \"" + text.Text + "\", anchors " + text.HorizontalAnchor + "/" + text.VerticalAnchor;

                //Type 6 first: it derives from the sprite element, so the general arm would swallow it
                //and drop the trailing signed medium only it carries.
                case LoadingScreenType6Element extended:
                    return "sprite " + extended.SpriteId + ", anchors " + extended.HorizontalAnchor + "/" +
                           extended.VerticalAnchor + ", offset " + extended.OffsetX + "," + extended.OffsetY +
                           ", trailing " + extended.SignedMedium;

                case LoadingScreenSpriteElement sprite:
                    return "sprite " + sprite.SpriteId + ", anchors " + sprite.HorizontalAnchor + "/" +
                           sprite.VerticalAnchor + ", offset " + sprite.OffsetX + "," + sprite.OffsetY;

                case LoadingScreenIntegerElement number:
                    return "value " + number.Value;

                default:
                    return "stored as " + element.GetType().Name;
            }
        }
    }

    /// <summary>
    ///     Group 0 of index 33 as a definition list: the manifest, on its own.
    /// </summary>
    /// <remarks>
    ///     Scoped to the group rather than the index, because group 1 beside it is a different format
    ///     with a different codec and the base <c>Enumerate</c> would feed screens to this decoder.
    ///     <para>
    ///     Read only. The category rows are count-prefixed runs of ids and the slot count is stored
    ///     separately from the row count, so nothing here is a single independent cell.
    ///     </para>
    /// </remarks>
    public sealed class LoadingScreenManifestListDescriptor : DefinitionListDescriptor<LoadingScreenManifestListing> {
        private readonly IReadOnlyList<DefinitionColumn> columns;

        /// <summary>Lists the manifest group.</summary>
        public LoadingScreenManifestListDescriptor() {
            columns = new[] {
                DefinitionColumn.ReadOnly<LoadingScreenManifestListing>("File", row => row.FileId, 70),
                DefinitionColumn.ReadOnly<LoadingScreenManifestListing>("Version", row => row.Version, 220),
                DefinitionColumn.ReadOnly<LoadingScreenManifestListing>("Type versions", row => row.TypeVersions, 110),
                DefinitionColumn.ReadOnly<LoadingScreenManifestListing>("Categories", row => row.Categories, 100),
                DefinitionColumn.ReadOnly<LoadingScreenManifestListing>("Slots", row => row.Slots, 130),
                DefinitionColumn.ReadOnly<LoadingScreenManifestListing>("Default screen", row => row.DefaultScreen, 120),
                DefinitionColumn.ReadOnly<LoadingScreenManifestListing>("Screen ids", row => row.ScreenIds, 100)
            };
        }

        /// <inheritdoc/>
        public override int IndexId => RSConstants.GAME_TIPS;

        /// <inheritdoc/>
        public override string RowNoun => "manifest";

        /// <inheritdoc/>
        public override IReadOnlyList<DefinitionColumn> Columns => columns;

        /// <inheritdoc/>
        public override IEnumerable<DefinitionAddress> Enumerate(RSCache cache) {
            return LoadingScreenEnumeration.Group(cache, IndexId, LoadingScreenManifest.GroupId, Address);
        }

        /// <inheritdoc/>
        public override LoadingScreenManifestListing Decode(RSCache cache, DefinitionAddress address, JagStream payload) {
            var record = new LoadingScreenManifest();
            record.Decode(payload);
            return new LoadingScreenManifestListing(address, record);
        }

        /// <inheritdoc/>
        public override DefinitionAddress AddressOf(LoadingScreenManifestListing row) {
            if (row == null)
                throw new ArgumentNullException(nameof(row));
            return row.Address;
        }
    }

    /// <summary>
    ///     Group 1 of index 33 as a definition list: one flat row per loading screen.
    /// </summary>
    /// <remarks>
    ///     Group scoped for the same reason as <see cref="LoadingScreenManifestListDescriptor"/>, and
    ///     enumerated from the reference table because this group's file ids are <b>not</b>
    ///     contiguous - they are 0 and then a run starting well above it, so a counted walk reads
    ///     files that do not exist and misses the ones that do.
    ///     <para>
    ///     Read only. A screen is a count-prefixed list of ten different element formats, and the
    ///     stored order is the z-order, so nothing about it is a single independent cell.
    ///     </para>
    /// </remarks>
    public sealed class LoadingScreenListDescriptor : DefinitionListDescriptor<LoadingScreenListing> {
        private readonly IReadOnlyList<DefinitionColumn> columns;

        /// <summary>Lists every loading screen the index declares.</summary>
        public LoadingScreenListDescriptor() {
            columns = new[] {
                DefinitionColumn.ReadOnly<LoadingScreenListing>("Screen", row => row.ScreenId, 80),
                DefinitionColumn.ReadOnly<LoadingScreenListing>("Duration ms", row => row.DurationMs, 110),
                DefinitionColumn.ReadOnly<LoadingScreenListing>("Timing 2", row => row.SecondTiming, 90),
                DefinitionColumn.ReadOnly<LoadingScreenListing>("Elements", row => row.Elements, 90),
                DefinitionColumn.ReadOnly<LoadingScreenListing>("Types", row => row.ElementTypes, 140),
                DefinitionColumn.ReadOnly<LoadingScreenListing>("Text", row => row.Text, 460)
            };
        }

        /// <inheritdoc/>
        public override int IndexId => RSConstants.GAME_TIPS;

        /// <inheritdoc/>
        public override string RowNoun => "screen";

        /// <inheritdoc/>
        public override IReadOnlyList<DefinitionColumn> Columns => columns;

        /// <inheritdoc/>
        public override IEnumerable<DefinitionAddress> Enumerate(RSCache cache) {
            return LoadingScreenEnumeration.Group(cache, IndexId, LoadingScreenDefinition.GroupId, Address);
        }

        /// <inheritdoc/>
        public override LoadingScreenListing Decode(RSCache cache, DefinitionAddress address, JagStream payload) {
            var record = new LoadingScreenDefinition { Id = address.FileId };
            record.Decode(payload);
            return new LoadingScreenListing(address, record);
        }

        /// <inheritdoc/>
        public override DefinitionAddress AddressOf(LoadingScreenListing row) {
            if (row == null)
                throw new ArgumentNullException(nameof(row));
            return row.Address;
        }
    }

    /// <summary>Shared group-scoped enumeration for the two index-33 descriptors.</summary>
    internal static class LoadingScreenEnumeration {
        /// <summary>Every file one group of index 33 declares.</summary>
        /// <param name="cache">The open cache.</param>
        /// <param name="indexId">The index id.</param>
        /// <param name="groupId">The group within it.</param>
        /// <param name="address">Builds the address, so the descriptor's own id rules apply.</param>
        /// <returns>The addresses to load.</returns>
        internal static IEnumerable<DefinitionAddress> Group(RSCache cache, int indexId, int groupId,
            Func<int, int, DefinitionAddress> address) {
            if (cache == null)
                throw new ArgumentNullException(nameof(cache));

            foreach (int file in cache.GetFileIds(indexId, groupId))
                yield return address(groupId, file);
        }
    }
}
