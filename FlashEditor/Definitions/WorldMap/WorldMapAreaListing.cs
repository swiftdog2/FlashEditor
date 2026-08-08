using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using FlashEditor.cache;
using FlashEditor.Definitions.Editing;

namespace FlashEditor.Definitions.WorldMap {
    /// <summary>
    ///     One world-map area as a list row: its details record, and where the rest of it lives.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///     <b>The row does not carry the raster.</b> Index 23's rasters total just over 6 MB and the
    ///     largest is 4.7 MB on its own, so a list that decoded all 39 to fill its columns would
    ///     hold the whole index in memory to show a table of names. The raster is read when an area
    ///     is selected, on the tab's own worker.
    ///     </para>
    ///     <para>
    ///     <b>The addresses are carried, not computed.</b> Index 23 is name-hashed at both levels:
    ///     the details group is id 1 rather than 0, the group ids run 0-44 then 64-94, and the
    ///     <c>area</c> file is id 4 in 32 groups and id 0 in the other seven. Every one of those is
    ///     resolved once here through <see cref="WorldMapNaming"/> and then kept, because nothing on
    ///     this index can be reached a second time by arithmetic.
    ///     </para>
    /// </remarks>
    public sealed class WorldMapAreaListing : IDetailRow {
        private WorldMapAreaListing(DefinitionAddress address, WorldMapAreaDefinition area,
            int rasterGroupId, int rasterFileId, int elementGroupId, int elementCount, int storedLength) {
            Address = address;
            Area = area;
            RasterGroupId = rasterGroupId;
            RasterFileId = rasterFileId;
            ElementGroupId = elementGroupId;
            ElementCount = elementCount;
            StoredLength = storedLength;
        }

        /// <summary>Where the details record lives.</summary>
        public DefinitionAddress Address { get; }

        /// <summary>The decoded details record.</summary>
        public WorldMapAreaDefinition Area { get; }

        /// <summary>The group holding this area's raster, or -1 when no group hashes to its name.</summary>
        public int RasterGroupId { get; }

        /// <summary>
        ///     The file within that group holding the raster, or -1.
        /// </summary>
        /// <remarks>
        ///     Shown as a column rather than kept private, because this is the index's own trap: it
        ///     is 4 in 32 of the 39 areas and 0 in the other seven, so a reader that assumed one
        ///     works on most of the list and fails on the rest. A column makes that visible instead
        ///     of surprising.
        /// </remarks>
        public int RasterFileId { get; }

        /// <summary>The <c>&lt;name&gt;_staticelements</c> group, or -1 when the area has none.</summary>
        public int ElementGroupId { get; }

        /// <summary>How many icons the area places.</summary>
        public int ElementCount { get; }

        /// <summary>How many bytes the details record is stored as.</summary>
        public int StoredLength { get; }

        /// <summary>The area id, which is its file id within the details group.</summary>
        public int Id => Area.Id;

        /// <summary>The internal name, which is also the name of the area's other two groups.</summary>
        public string InternalName => Area.InternalName;

        /// <summary>The name shown to the player.</summary>
        public string DisplayName => Area.DisplayName;

        /// <summary>The canvas the area's zones describe.</summary>
        public WorldMapCanvas Canvas => WorldMapCanvas.For(Area);

        /// <summary>The canvas size as one string, for the grid.</summary>
        public string CanvasSize {
            get {
                WorldMapCanvas canvas = Canvas;
                return canvas.IsEmpty ? "none" : canvas.Width + "x" + canvas.Height;
            }
        }

        /// <summary>Whether the area has a static-element group at all.</summary>
        /// <remarks>Three areas do not, and the client falls back to an empty list for them.</remarks>
        public bool HasElements => ElementGroupId >= 0;

        /// <inheritdoc/>
        public string Summary =>
            "Area " + Id + " \"" + InternalName + "\" - " + DisplayName + ", " +
            CanvasSize + " tiles from " + Area.Zones.Count + " zone(s), " + ElementCount + " icon(s)";

        /// <inheritdoc/>
        public IReadOnlyList<DetailField> Fields => BuildFields();

        /// <summary>
        ///     Builds a row from a details file, resolving where the area's other two groups are.
        /// </summary>
        /// <remarks>
        ///     Runs on the list panel's worker. The three reference-table lookups per area are what
        ///     make the raster and icon columns truthful rather than assumed, and 39 areas is few
        ///     enough that resolving them eagerly costs nothing worth deferring.
        /// </remarks>
        /// <param name="cache">The open cache.</param>
        /// <param name="address">Where the details record came from.</param>
        /// <param name="payload">The stored details file.</param>
        /// <returns>The row.</returns>
        public static WorldMapAreaListing Build(RSCache cache, DefinitionAddress address, JagStream payload) {
            if (cache == null) throw new ArgumentNullException(nameof(cache));
            if (payload == null) throw new ArgumentNullException(nameof(payload));

            payload.Seek0();
            int stored = (int) payload.Length;

            var area = new WorldMapAreaDefinition { Id = address.FileId };
            area.Decode(payload);

            int rasterGroup = WorldMapNaming.GroupIdFor(cache, area.InternalName);
            int rasterFile = rasterGroup < 0
                ? -1
                : WorldMapNaming.FileIdFor(cache, rasterGroup, WorldMapNaming.RasterFile);

            int elementGroup = WorldMapNaming.GroupIdFor(cache,
                WorldMapNaming.StaticElementGroupFor(area.InternalName));
            int elementCount = elementGroup < 0
                ? 0
                : cache.GetFileIds(RSConstants.WORLD_MAP, elementGroup).Length;

            return new WorldMapAreaListing(address, area, rasterGroup, rasterFile,
                elementGroup, elementCount, stored);
        }

        private IReadOnlyList<DetailField> BuildFields() {
            WorldMapCanvas canvas = Canvas;

            var fields = new List<DetailField> {
                new DetailField("Area id", Id.ToString(CultureInfo.InvariantCulture) +
                                           " (the file id inside the 'details' group)"),
                new DetailField("Internal name", InternalName +
                                                 " (hashed lower-cased to reach the other two groups)"),
                new DetailField("Display name", DisplayName),
                new DetailField("Opens at", Area.OriginX + ", " + Area.OriginY + " (packed 0x" +
                                            Area.PackedOrigin.ToString("X8", CultureInfo.InvariantCulture) + ")"),
                new DetailField("Background tint", Area.TintColour == -1
                    ? "none, so empty tiles take the client's blue checkerboard"
                    : "#" + (Area.TintColour & 0xFFFFFF).ToString("X6", CultureInfo.InvariantCulture)),
                new DetailField("Enabled", Area.Enabled
                    ? "yes"
                    : "no (stored " + Area.StoredEnabled + "; the client tests for exactly 1)"),
                new DetailField("Zoom", Area.StoredZoom == WorldMapAreaDefinition.ZoomStoredAsZero
                    ? "0, stored as 255 - the client folds the two together"
                    : Area.StoredZoom.ToString(CultureInfo.InvariantCulture)),
                new DetailField("Unread byte", Area.UnreadByte.ToString(CultureInfo.InvariantCulture) +
                                               " (read by the client and stored nowhere)"),
                new DetailField("Canvas", canvas.IsEmpty
                    ? "none, because the area declares no zone"
                    : canvas.Width + "x" + canvas.Height + " tiles at map origin " +
                      canvas.OriginX + ", " + canvas.OriginY),
                new DetailField("Raster", RasterGroupId < 0
                    ? "no group hashes to this area's name"
                    : "group " + RasterGroupId + ", file " + RasterFileId + " (found by name, " +
                      "because the file id is not fixed across areas)"),
                new DetailField("Static elements", HasElements
                    ? "group " + ElementGroupId + ", " + ElementCount + " file(s)"
                    : "none - this area has no _staticelements group, which the client tolerates"),
                new DetailField("Details record", StoredLength.ToString(CultureInfo.InvariantCulture) +
                                                  " bytes stored"),
                new DetailField("Zones", Area.Zones.Count.ToString(CultureInfo.InvariantCulture))
            };

            for (int i = 0; i < Area.Zones.Count; i++) {
                WorldMapZone zone = Area.Zones[i];
                fields.Add(new DetailField("  zone " + i,
                    "plane " + zone.Plane + ": world " +
                    zone.SourceMinX + "," + zone.SourceMinY + " to " +
                    zone.SourceMaxX + "," + zone.SourceMaxY + "  ->  map " +
                    zone.DestinationMinX + "," + zone.DestinationMinY + " to " +
                    zone.DestinationMaxX + "," + zone.DestinationMaxY));
            }

            return fields;
        }
    }

    /// <summary>
    ///     Index 23's <c>details</c> group as a definition list: one row per world-map area.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///     <b>The enumeration is overridden and that is the whole point.</b> The base walks every
    ///     file of the index, which here would produce 39 areas, 39 rasters and 39 static-element
    ///     groups interleaved in one list - three unrelated record families that share nothing but
    ///     an index. The area is the level a user navigates by, so the list is the areas and the
    ///     other two families hang off the selected row.
    ///     </para>
    ///     <para>
    ///     <b>Read only.</b> The details record round-trips byte for byte, so an encoder is not the
    ///     obstacle; what is missing is a safe edit. Every field on it is load bearing for something
    ///     the editor cannot regenerate - the internal name is the hash that reaches the other two
    ///     groups, and the zones are the only statement of the area's canvas, which the raster's
    ///     block positions are already laid out against. Retyping either in a grid cell would
    ///     silently unhook an area from its own raster.
    ///     </para>
    /// </remarks>
    public sealed class WorldMapAreaListDescriptor : DefinitionListDescriptor<WorldMapAreaListing> {
        /// <inheritdoc/>
        public override int IndexId => RSConstants.WORLD_MAP;

        /// <inheritdoc/>
        public override string RowNoun => "world map area";

        /// <inheritdoc/>
        public override IReadOnlyList<DefinitionColumn> Columns { get; } = new[] {
            DefinitionColumn.Number<WorldMapAreaListing>("Area", row => row.Id, width: 55),
            DefinitionColumn.Text<WorldMapAreaListing>("Name", row => row.DisplayName, width: 200),
            DefinitionColumn.Text<WorldMapAreaListing>("Internal", row => row.InternalName, width: 200),
            DefinitionColumn.Text<WorldMapAreaListing>("Canvas", row => row.CanvasSize, width: 90),
            DefinitionColumn.Number<WorldMapAreaListing>("Zones", row => row.Area.Zones.Count, width: 55),
            DefinitionColumn.Number<WorldMapAreaListing>("Icons", row => row.ElementCount, width: 55),
            /* The raster file id earns a column because it is this index's trap: 4 in 32 areas and 0
               in the other seven, so a reader that assumed one would work on most of this list. */
            DefinitionColumn.Text<WorldMapAreaListing>("Raster",
                row => row.RasterGroupId < 0 ? "missing" : row.RasterGroupId + ":" + row.RasterFileId,
                width: 80),
            DefinitionColumn.Text<WorldMapAreaListing>("Enabled", row => row.Area.Enabled ? "yes" : "no",
                width: 65),
            DefinitionColumn.Number<WorldMapAreaListing>("Zoom", row => row.Area.Zoom, width: 55)
        };

        /// <summary>
        ///     Every area the <c>details</c> group declares.
        /// </summary>
        /// <remarks>
        ///     The group is reached by name. It is id <b>1</b> in both caches and the client still
        ///     asks for it by name (<c>Class278.java:171</c>), so nothing here writes the id down.
        /// </remarks>
        /// <param name="cache">The open cache.</param>
        /// <returns>One address per area.</returns>
        public override IEnumerable<DefinitionAddress> Enumerate(RSCache cache) {
            if (cache == null)
                throw new ArgumentNullException(nameof(cache));

            int details = WorldMapNaming.GroupIdFor(cache, WorldMapNaming.DetailsGroup);
            if (details < 0)
                throw new FileNotFoundException(
                    "Index " + RSConstants.WORLD_MAP + " has no group named '" +
                    WorldMapNaming.DetailsGroup + "', so no area can be addressed.");

            foreach (int fileId in cache.GetFileIds(RSConstants.WORLD_MAP, details))
                yield return new DefinitionAddress(details, fileId);
        }

        /// <inheritdoc/>
        public override WorldMapAreaListing Decode(RSCache cache, DefinitionAddress address, JagStream payload) {
            return WorldMapAreaListing.Build(cache, address, payload);
        }

        /// <inheritdoc/>
        public override DefinitionAddress AddressOf(WorldMapAreaListing row) {
            if (row == null)
                throw new ArgumentNullException(nameof(row));

            return row.Address;
        }
    }
}
