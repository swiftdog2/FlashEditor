using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using FlashEditor.IO;

namespace FlashEditor.Definitions.Config {
    /// <summary>One decoded field of a config record, as the editor shows it.</summary>
    public readonly struct ConfigField {
        /// <summary>Names one field and the value it holds.</summary>
        /// <param name="name">The field's name.</param>
        /// <param name="value">The value, already rendered.</param>
        public ConfigField(string name, string value) {
            Name = name;
            Value = value;
        }

        /// <summary>The field's name.</summary>
        public string Name { get; }

        /// <summary>The value, rendered.</summary>
        public string Value { get; }
    }

    /// <summary>
    ///     One opcode occurrence of a config record, as the editor shows it.
    /// </summary>
    /// <remarks>
    ///     <see cref="Detail"/> is not the same thing for every family and deliberately so. A
    ///     <see cref="ConfigDefinition"/> keeps the payload <b>bytes</b> each occurrence consumed, so
    ///     that is what is shown; the three older families keep only the decoded <b>value</b>
    ///     (<see cref="DecodedOpcode"/>), so no byte string exists to show for them. Rendering a
    ///     recomputed byte string for the second kind would put invented bytes beside real ones.
    /// </remarks>
    public readonly struct ConfigOpcodeRow {
        /// <summary>Records one opcode occurrence.</summary>
        /// <param name="opcode">The opcode byte.</param>
        /// <param name="detail">The stored payload in hex, or the decoded value where no bytes were kept.</param>
        public ConfigOpcodeRow(int opcode, string detail) {
            Opcode = opcode;
            Detail = detail;
        }

        /// <summary>The opcode byte.</summary>
        public int Opcode { get; }

        /// <summary>The stored payload in hex, or the decoded value. See the type's remarks.</summary>
        public string Detail { get; }
    }

    /// <summary>
    ///     One config record described without naming its type, so a single list can present any
    ///     index 2 family.
    /// </summary>
    public sealed class ConfigRecord {
        /// <summary>Binds a decoded record to the description the editor shows for it.</summary>
        /// <param name="definition">The decoded record, or null when the family has no codec here.</param>
        /// <param name="summary">A one-line description of what the record holds.</param>
        /// <param name="opcodes">The opcodes it carried, in stored order.</param>
        /// <param name="fields">Its decoded fields.</param>
        public ConfigRecord(object? definition, string summary, IReadOnlyList<ConfigOpcodeRow> opcodes,
            IReadOnlyList<ConfigField> fields) {
            Definition = definition;
            Summary = summary ?? string.Empty;
            Opcodes = opcodes ?? Array.Empty<ConfigOpcodeRow>();
            Fields = fields ?? Array.Empty<ConfigField>();
        }

        /// <summary>The decoded record, or null when no codec exists for its family.</summary>
        public object? Definition { get; }

        /// <summary>What the record holds, in one line.</summary>
        public string Summary { get; }

        /// <summary>The opcodes it carried, in the order the file stores them.</summary>
        public IReadOnlyList<ConfigOpcodeRow> Opcodes { get; }

        /// <summary>The decoded fields.</summary>
        public IReadOnlyList<ConfigField> Fields { get; }

        /// <summary>
        ///     The opcode sequence exactly as stored.
        /// </summary>
        /// <remarks>
        ///     Worth a column of its own because index 2 is the least canonical part of this cache:
        ///     not one of group 36's 1,051 files is in ascending opcode order, and a record can carry
        ///     the same opcode twice. Sorting the list on this column is how a repacked file stands
        ///     out from a shipped one.
        /// </remarks>
        public string Order => string.Join(",", Opcodes.Select(entry => entry.Opcode));
    }

    /// <summary>
    ///     One index 2 config family: what its group holds, and how to read a record of it.
    /// </summary>
    /// <remarks>
    ///     Index 2 is thirty-five unrelated families sharing one index, so the config tab is one
    ///     grid driven by whichever family the group selector names. This type is the table that
    ///     drives it, which is the same reasoning <c>IDefinitionListDescriptor</c> applies one level
    ///     up: a family is a row here rather than an arm of a switch in the panel.
    ///     <para>
    ///     A group with no entry is <b>not</b> an error. It gets <see cref="Unmodelled"/>, which
    ///     reads nothing and classifies each record from its own bytes, so an unlisted group shows
    ///     its id space and its record lengths rather than a blank grid.
    ///     </para>
    /// </remarks>
    public sealed class ConfigFamily {
        private readonly Func<int, JagStream, ConfigRecord> read;

        private ConfigFamily(int groupId, string name, string rowNoun, string notes, bool modelled,
            Func<int, JagStream, ConfigRecord> read,
            Func<object, int?>? colour = null, Func<object, int?>? texture = null) {
            GroupId = groupId;
            Name = name;
            RowNoun = rowNoun;
            Notes = notes;
            IsModelled = modelled;
            this.read = read;
            Colour = colour;
            Texture = texture;
        }

        /// <summary>The group within index 2 that holds the family.</summary>
        public int GroupId { get; }

        /// <summary>What the family is called, for the group selector.</summary>
        public string Name { get; }

        /// <summary>What one record is called, singular, for the status line.</summary>
        public string RowNoun { get; }

        /// <summary>What is known about the family, including the client site that names its group.</summary>
        public string Notes { get; }

        /// <summary>Whether this editor has a codec for the family.</summary>
        public bool IsModelled { get; }

        /// <summary>
        ///     Reads the packed <c>0xRRGGBB</c> a record stores, or null for a family that stores none.
        /// </summary>
        /// <remarks>
        ///     Optional per family rather than a column on every one. Index 2 is thirty-five
        ///     unrelated record types and only a handful of them store a colour at all; a swatch
        ///     column on the other thirty would be an empty column with a heading that lies.
        ///     <para>
        ///     Returning null <i>per record</i> is also meaningful and is not the same as the family
        ///     having no colour: a floor overlay distinguishes an absent colour from black, because
        ///     they are different bytes and re-encode differently.
        ///     </para>
        /// </remarks>
        public Func<object, int?>? Colour { get; }

        /// <summary>Reads the index-9 texture a record names, or null for a family that names none.</summary>
        public Func<object, int?>? Texture { get; }

        /// <summary>Decodes one record of this family.</summary>
        /// <param name="fileId">The file id, which is the definition id within the group.</param>
        /// <param name="payload">The stored file, positioned at its start.</param>
        /// <returns>The record.</returns>
        public ConfigRecord Read(int fileId, JagStream payload) {
            if (payload == null)
                throw new ArgumentNullException(nameof(payload));
            return read(fileId, payload);
        }

        /// <summary>
        ///     A family this editor has no codec for.
        /// </summary>
        /// <remarks>
        ///     Reads no opcodes at all rather than guessing at a table. What it can say is true of any
        ///     record whatever its format: how long it is, and whether it terminates immediately -
        ///     8,694 of index 2's 16,981 files are a single <c>0x00</c>, so "empty" is the honest and
        ///     useful answer for about half the index.
        /// </remarks>
        /// <param name="groupId">The group id.</param>
        /// <returns>The family.</returns>
        public static ConfigFamily Unmodelled(int groupId) {
            return new ConfigFamily(groupId, "no codec here", "record",
                "No codec has been written for this group, so nothing here decodes its opcodes." +
                " Each record is classified from its own bytes instead: an empty record is a single" +
                " 0x00 terminator.",
                false,
                (id, payload) => {
                    byte[] bytes = Bytes(payload);
                    return new ConfigRecord(null, DescribeRawRecord(bytes),
                        Array.Empty<ConfigOpcodeRow>(),
                        new[] { new ConfigField("Stored bytes", Hex(bytes)) });
                });
        }

        /// <summary>
        ///     The sixteen groups a class in the 637 client opens, in group order.
        /// </summary>
        /// <remarks>
        ///     Eighteen classes are constructed with <c>client.BIT_CONFIG</c>
        ///     (InterfaceSettings.java:247-293) and each names its group with
        ///     <c>getChildsInFolder(0, n)</c>, but two of them name groups 29 and 30, which this cache
        ///     does not contain. Most rows derive from <see cref="ConfigDefinition"/> and three
        ///     predate it; the two shapes differ only in what they keep per opcode, which
        ///     <see cref="ConfigOpcodeRow.Detail"/> covers.
        /// </remarks>
        private static readonly ConfigFamily[] WithProviders = {
            Legacy<FloorUnderlayDefinition>(ConfigGroup.FloorUnderlay, "Floor underlays", "floor underlay",
                "The base colour and texture of a ground tile, blended with its neighbours before any" +
                " overlay is drawn.",
                (id, payload) => new FloorUnderlayDefinition { Id = id }.Decode(payload),
                definition => definition.DecodedOpcodes,
                definition => "rgb " + Hex(definition.Rgb, 6) +
                              (definition.TextureId == -1 ? "" : ", texture " + definition.TextureId),
                definition => new[] {
                    new ConfigField("Colour", Hex(definition.Rgb, 6)),
                    new ConfigField("Texture", definition.TextureId.ToString()),
                    new ConfigField("Texture scale", definition.TextureScale.ToString()),
                    new ConfigField("Casts shadow", definition.CastsShadow.ToString()),
                    new ConfigField("Occludes", definition.Occludes.ToString())
                },
                //An underlay always carries a colour: opcode 1 absent leaves Rgb at 0, and black is
                //a real underlay colour here rather than a stand-in for "none".
                definition => definition.Rgb,
                definition => definition.TextureId),

            Of<IdentityKitDefinition>(ConfigGroup.IdentityKit, "Identity kits", "identity kit",
                "The models one body part of a player is built from, with the recolour and retexture" +
                " tables applied to them. Class83.java:158 names the group; PlayerAppearance is the" +
                " only consumer.",
                definition => DescribeIdentityKit(definition),
                definition => IdentityKitFields(definition)),

            Legacy<FloorOverlayDefinition>(ConfigGroup.FloorOverlay, "Floor overlays", "floor overlay",
                "The shape drawn over an underlay - paths, water and the rest - plus the priority that" +
                " decides which of two overlapping overlays wins.",
                (id, payload) => new FloorOverlayDefinition { Id = id }.Decode(payload),
                definition => definition.DecodedOpcodes,
                definition => (definition.HasPrimaryRgb ? "rgb " + Hex(definition.PrimaryRgb, 6) : "no colour") +
                              (definition.TextureId == -1 ? "" : ", texture " + definition.TextureId) +
                              ", priority " + definition.Priority,
                definition => new[] {
                    new ConfigField("Primary colour",
                        definition.HasPrimaryRgb ? Hex(definition.PrimaryRgb, 6) : "absent"),
                    new ConfigField("Secondary colour",
                        definition.SecondaryRgb == -1 ? "none" : Hex(definition.SecondaryRgb, 6)),
                    new ConfigField("Texture", definition.TextureId +
                        (definition.TextureIdIsShortForm ? " (stored as a short)" : "")),
                    new ConfigField("Texture scale", definition.TextureScale.ToString()),
                    new ConfigField("Priority", definition.Priority + ", composite " +
                        Hex(definition.ApplyPriorityComposite(), 4)),
                    new ConfigField("Blends with neighbours", definition.BlendWithNeighbours.ToString()),
                    new ConfigField("Flat ground occluder", definition.FlatGroundOccluder.ToString()),
                    new ConfigField("Casts shadow", definition.CastsShadow.ToString()),
                    new ConfigField("World map background", definition.IsWorldMapBackground.ToString()),
                    new ConfigField("Water", "tint " + Hex(definition.WaterTintRgb, 6) + ", depth " +
                        definition.WaterDepth + ", alpha " + definition.WaterAlpha)
                },
                /* Null where the record stores no colour, which is NOT black. HasPrimaryRgb exists
                   because absent and 0x000000 are different bytes that re-encode differently, and
                   a swatch drawn for an absent colour would assert a value the file does not
                   carry. */
                definition => definition.HasPrimaryRgb ? definition.PrimaryRgb : null,
                definition => definition.TextureId),

            Of<ContainerDefinition>(ConfigGroup.Container, "Item containers", "container",
                "How many slots one of the game's inventories, banks or shop stocks holds." +
                " Class8.java:163 names the group.",
                definition => definition.Capacity + " slots",
                definition => new[] { new ConfigField("Capacity", definition.Capacity.ToString()) }),

            Of<ParameterTypeDefinition>(ConfigGroup.ParameterType, "Parameter types", "parameter type",
                "What an opcode 249 parameter key means and what its default is." +
                " Class365.java:102 names the group.",
                definition => "type '" + definition.TypeLetter + "'" +
                              (definition.IsString
                                  ? ", default \"" + (definition.DefaultString ?? "") + "\""
                                  : ", default " + definition.DefaultInt),
                definition => new[] {
                    new ConfigField("Type letter", "'" + definition.TypeLetter + "' (byte " +
                        Hex(definition.TypeLetterByte, 2) + ")"),
                    new ConfigField("Holds a string", definition.IsString.ToString()),
                    new ConfigField("Default integer", definition.DefaultInt.ToString()),
                    new ConfigField("Default string", definition.DefaultString ?? "none"),
                    new ConfigField("Opcode 4 flag", definition.Unknown4.ToString())
                }),

            Of<EmptyConfigDefinition>(ConfigGroup.ClientString, "Client strings", "client string",
                "Every record in this cache is a bare terminator, so the group is an id space and" +
                " nothing else. Class239 stores the child count and never reads a file;" +
                " InterfaceSettings.java:343 uses that count as an array length.",
                definition => "empty record",
                definition => Array.Empty<ConfigField>()),

            Of<VarPlayerDefinition>(ConfigGroup.VarPlayer, "Player variables", "varplayer",
                "One slot of the per-player variable array, and whether it survives a logout." +
                " Class139.java:19 names the group; the file count is what sizes the client's array.",
                definition => definition.Has(5)
                    ? "persistence " + definition.PersistenceScope
                    : "empty record",
                definition => new[] {
                    new ConfigField("Persistence scope", definition.PersistenceScope.ToString()),
                    new ConfigField("Reset on logout", definition.ResetOnLogout.ToString())
                }),

            Of<ClientVariableDefinition>(ConfigGroup.ClientVariable, "Client variables", "client variable",
                "The type letter of one client-side variable slot, and whether the server may write it." +
                " Class132.java:117 names the group.",
                definition => "type '" + definition.TypeLetter + "'" +
                              (definition.ServerWritable ? ", server writable" : ""),
                definition => new[] {
                    new ConfigField("Type letter", "'" + definition.TypeLetter + "' (byte " +
                        Hex(definition.TypeLetterByte, 2) + ")"),
                    new ConfigField("Server writable", definition.ServerWritable.ToString())
                }),

            Of<StructDefinition>(ConfigGroup.Struct, "Structs", "struct",
                "A bag of parameters and nothing else, addressed by id. CS2 opcode 4500 reads one" +
                " parameter out of it and takes the key's type and default from the parameter type" +
                " table in group 11. Class264.java:67 names the group.",
                definition => definition.Parameters.Count == 0
                    ? "empty record"
                    : definition.Parameters.Count + " parameters",
                definition => ParameterFields(definition.Parameters)),

            Of<LightIntensityDefinition>(ConfigGroup.LightIntensity, "Light intensity curves", "light curve",
                "The waveform, rate, amplitude and offset that make a light pulse. Class269.java:161" +
                " names the group; an effect stream that meets a marker of 31 feeds all four fields" +
                " to Class1, which writes the result into a light's intensity every tick.",
                definition => "waveform " + definition.Waveform + ", rate " + definition.Rate +
                              ", amplitude " + definition.Amplitude + ", offset " + definition.Offset,
                definition => new[] {
                    new ConfigField("Waveform", definition.Waveform.ToString()),
                    new ConfigField("Rate", definition.Rate.ToString()),
                    new ConfigField("Amplitude", definition.Amplitude.ToString()),
                    new ConfigField("Offset", definition.Offset.ToString())
                }),

            Of<RenderAnimationDefinition>(ConfigGroup.RenderAnimation, "Render animations", "render animation",
                "Which animation a player or NPC plays for every combination of movement and facing." +
                " Class257.java:82 names the group; Class284_Sub1_Sub2.method3370 is the selector that" +
                " settles which opcode covers which quadrant.",
                definition => DescribeRenderAnimation(definition),
                definition => RenderAnimationFields(definition)),

            Legacy<MapSceneIconDefinition>(ConfigGroup.MapSceneIcon, "Map scene icons", "map scene icon",
                "The sprite the minimap stamps over a tile for a staircase, a door and the like.",
                (id, payload) => new MapSceneIconDefinition { Id = id }.Decode(payload),
                definition => definition.DecodedOpcodes,
                definition => "sprite " + definition.SpriteGroupId,
                definition => new[] {
                    new ConfigField("Sprite group", definition.SpriteGroupId.ToString()),
                    new ConfigField("Tint", Hex(definition.TintRgb, 6)),
                    new ConfigField("Stretch to footprint", definition.StretchToFootprint.ToString())
                }),

            Of<CursorDefinition>(ConfigGroup.Cursor, "Cursors", "cursor",
                "The sprite drawn as the mouse pointer and the pixel within it that is the hotspot." +
                " Class11.java:33 names the group.",
                definition => "sprite " + definition.SpriteId + " at " + definition.HotspotX + "," +
                              definition.HotspotY,
                definition => new[] {
                    new ConfigField("Sprite", definition.SpriteId.ToString()),
                    new ConfigField("Hotspot", definition.HotspotX + ", " + definition.HotspotY)
                }),

            Of<QuestDefinition>(ConfigGroup.Quest, "Quests", "quest",
                "The quest name, its requirement lists and the sprite a chat line draws beside a name" +
                " for it. Class13.java:123 names the group; item definition opcode 132 is a list of" +
                " file ids in it.",
                definition => string.IsNullOrEmpty(definition.Name)
                    ? "unnamed quest"
                    : "\"" + definition.Name + "\"",
                definition => QuestFields(definition)),

            Of<MapElementDefinition>(ConfigGroup.MapElement, "World map elements", "map element",
                "The sprite, label, polygon and right-click menu the world map draws for one point of" +
                " interest. Class341.java:141 names the group; object definition opcode 107 is a file" +
                " id in it.",
                definition => DescribeMapElement(definition),
                definition => MapElementFields(definition)),

            Of<DamageMarkDefinition>(ConfigGroup.DamageMark, "Damage marks", "damage mark",
                "The sprites, font, colour and lifetime of one hit splat drawn over a mobile." +
                " Class121.java:102 names the group.",
                definition => "font " + definition.FontId + ", " + definition.LifetimeMillis + " ms",
                definition => new[] {
                    new ConfigField("Font", definition.FontId.ToString()),
                    new ConfigField("Text colour", Hex(definition.TextRgb, 6)),
                    new ConfigField("Number template", definition.NumberTemplate),
                    new ConfigField("Sprite layers", definition.SpriteLayer1Id + ", " +
                        definition.SpriteLayer2Id + ", " + definition.SpriteLayer3Id),
                    new ConfigField("Preloaded sprite", definition.PreloadedSpriteId.ToString()),
                    new ConfigField("Drift", definition.DriftX + ", " + definition.DriftY),
                    new ConfigField("Offset y", definition.OffsetY.ToString()),
                    new ConfigField("Lifetime", definition.LifetimeMillis + " ms"),
                    new ConfigField("Fade start", definition.FadeStartMillis + " ms"),
                    new ConfigField("Opcode 12 field", definition.Unknown12.ToString())
                })
        };

        /// <summary>
        ///     Every family this editor models, in group order.
        /// </summary>
        /// <remarks>
        ///     All thirty-five of index 2's groups: <see cref="WithProviders"/> plus the nineteen
        ///     groups no client class opens. Those nineteen are appended from
        ///     <see cref="ConfigGroup.EmptyProviderless"/> rather than written out one row each -
        ///     they share a codec, a summary and a set of notes, and nothing distinguishes one from
        ///     another beyond its id and its file count.
        /// </remarks>
        public static readonly IReadOnlyList<ConfigFamily> Modelled = BuildModelled();

        /// <summary>Joins the provider families to the empty ones.</summary>
        /// <returns>Every family, in the order the selector shows them.</returns>
        private static IReadOnlyList<ConfigFamily> BuildModelled() {
            var families = new List<ConfigFamily>(WithProviders);
            foreach (int group in ConfigGroup.EmptyProviderless)
                families.Add(NoProvider(group));
            return families;
        }

        /// <summary>
        ///     One of the nineteen groups no class in the 637 client opens.
        /// </summary>
        /// <remarks>
        ///     This is a real assertion rather than a placeholder, and the difference from
        ///     <see cref="Unmodelled"/> is the point: <see cref="EmptyConfigDefinition"/> runs the
        ///     opcode loop and refuses every opcode it meets, so a cache that starts filling one of
        ///     these groups fails loudly here instead of being read against a guessed table. Measured
        ///     over both caches, every file of every one of them is a single <c>0x00</c>.
        /// </remarks>
        /// <param name="groupId">The group id.</param>
        /// <returns>The family.</returns>
        private static ConfigFamily NoProvider(int groupId) {
            return Of<EmptyConfigDefinition>(groupId, "Group " + groupId + " (no provider)", "record",
                "No class in the 637 client opens this group, and every record in both caches is a" +
                " bare terminator, so its opcode set cannot be recovered from 639 data at all and no" +
                " field in it can be named. The codec refuses any opcode rather than guessing, which" +
                " is what makes a byte-identity sweep over the group mean \"it is still empty\".",
                definition => "empty record",
                definition => Array.Empty<ConfigField>());
        }

        /// <summary>
        ///     The family that holds a group, modelled or not.
        /// </summary>
        /// <param name="groupId">The group id within index 2.</param>
        /// <returns>The family, or an <see cref="Unmodelled"/> one when this editor has no codec.</returns>
        public static ConfigFamily For(int groupId) {
            foreach (ConfigFamily family in Modelled)
                if (family.GroupId == groupId)
                    return family;

            return Unmodelled(groupId);
        }

        /// <summary>Registers a family whose codec derives from <see cref="ConfigDefinition"/>.</summary>
        /// <typeparam name="T">The record type.</typeparam>
        /// <param name="groupId">The group id.</param>
        /// <param name="name">The family's display name.</param>
        /// <param name="rowNoun">What one record is called, singular.</param>
        /// <param name="notes">What is known about the family.</param>
        /// <param name="summary">Describes one decoded record in a line.</param>
        /// <param name="fields">Lists one decoded record's fields.</param>
        /// <returns>The family.</returns>
        private static ConfigFamily Of<T>(int groupId, string name, string rowNoun, string notes,
            Func<T, string> summary, Func<T, IEnumerable<ConfigField>> fields)
            where T : ConfigDefinition, new() {
            return new ConfigFamily(groupId, name, rowNoun, notes, true, (id, payload) => {
                var definition = new T { Id = id };
                definition.Decode(payload);
                return new ConfigRecord(definition, summary(definition), OpcodesOf(definition),
                    fields(definition).ToArray());
            });
        }

        /// <summary>
        ///     Registers a family whose codec predates <see cref="ConfigDefinition"/>.
        /// </summary>
        /// <remarks>
        ///     The three floor and map-scene decoders keep a <see cref="DecodedOpcode"/> per
        ///     occurrence, which holds the decoded value rather than the bytes it came from, so their
        ///     opcode rows show the value. They are listed here rather than left out because they are
        ///     index 2 families like any other and the tab would otherwise report three groups as
        ///     unmodelled that this editor has round-tripped byte for byte for months.
        /// </remarks>
        /// <typeparam name="T">The record type.</typeparam>
        /// <param name="groupId">The group id.</param>
        /// <param name="name">The family's display name.</param>
        /// <param name="rowNoun">What one record is called, singular.</param>
        /// <param name="notes">What is known about the family.</param>
        /// <param name="decode">Decodes one record.</param>
        /// <param name="opcodes">The record's stored opcode list.</param>
        /// <param name="summary">Describes one decoded record in a line.</param>
        /// <param name="fields">Lists one decoded record's fields.</param>
        /// <returns>The family.</returns>
        /// <param name="colour">Reads the record's packed colour, for a family that stores one.</param>
        /// <param name="texture">Reads the record's texture id, for a family that names one.</param>
        private static ConfigFamily Legacy<T>(int groupId, string name, string rowNoun, string notes,
            Func<int, JagStream, T> decode, Func<T, List<DecodedOpcode>> opcodes,
            Func<T, string> summary, Func<T, IEnumerable<ConfigField>> fields,
            Func<T, int?>? colour = null, Func<T, int?>? texture = null) where T : class {
            return new ConfigFamily(groupId, name, rowNoun, notes, true, (id, payload) => {
                T definition = decode(id, payload);
                ConfigOpcodeRow[] rows = opcodes(definition)
                    .Select(entry => new ConfigOpcodeRow(entry.Opcode,
                        entry.Value.ToString(CultureInfo.InvariantCulture)))
                    .ToArray();
                return new ConfigRecord(definition, summary(definition), rows, fields(definition).ToArray());
            }, Widen(colour), Widen(texture));
        }

        /// <summary>
        ///     A typed record accessor as one taking the untyped decoded record.
        /// </summary>
        /// <remarks>
        ///     A record of the wrong type yields null rather than throwing. The accessor is reached
        ///     from a grid cell renderer during a scroll, and the only way a mismatch can arise is
        ///     the family table pairing an accessor with the wrong decoder - which shows as an empty
        ///     column immediately rather than as an exception out of a paint handler that takes the
        ///     form down.
        /// </remarks>
        private static Func<object, int?>? Widen<T>(Func<T, int?>? accessor) where T : class {
            if (accessor == null)
                return null;

            return record => record is T typed ? accessor(typed) : null;
        }

        /// <summary>The opcode rows of a <see cref="ConfigDefinition"/>, payload bytes and all.</summary>
        /// <param name="definition">The decoded record.</param>
        /// <returns>The rows, in stored order.</returns>
        private static ConfigOpcodeRow[] OpcodesOf(ConfigDefinition definition) {
            return definition.DecodedOpcodes
                .Select(entry => new ConfigOpcodeRow(entry.Opcode, Hex(entry.Payload)))
                .ToArray();
        }

        /// <summary>What a record of an unmodelled family can be said to be, from its bytes alone.</summary>
        /// <param name="bytes">The stored record.</param>
        /// <returns>The description.</returns>
        private static string DescribeRawRecord(byte[] bytes) {
            if (bytes.Length == 0)
                return "no bytes at all";
            if (bytes.Length == 1 && bytes[0] == 0)
                return "empty record";
            return bytes.Length + " bytes, first opcode " + bytes[0];
        }

        /// <summary>
        ///     The record's bytes, leaving the stream where it was found.
        /// </summary>
        /// <remarks>
        ///     Rewound afterwards because the caller owns the stream and a later reader would
        ///     otherwise start at the end of it.
        /// </remarks>
        /// <param name="payload">The stored file.</param>
        /// <returns>The bytes.</returns>
        private static byte[] Bytes(JagStream payload) {
            int start = payload.Position;
            byte[] bytes = payload.ReadBytes(payload.Length - start);
            payload.Position = start;
            return bytes;
        }

        /// <summary>A byte string in hex, truncated so one cell cannot carry a whole record.</summary>
        /// <param name="bytes">The bytes.</param>
        /// <returns>The hex, with a length note when it was cut short.</returns>
        private static string Hex(byte[] bytes) {
            if (bytes == null || bytes.Length == 0)
                return "";

            const int shown = 24;
            string hex = BitConverter.ToString(bytes, 0, Math.Min(shown, bytes.Length)).Replace('-', ' ');
            return bytes.Length <= shown ? hex : hex + " ... (" + bytes.Length + " bytes)";
        }

        /// <summary>A value in hex, zero padded to the width the format gives it.</summary>
        /// <param name="value">The value.</param>
        /// <param name="digits">How many hex digits the stored field holds.</param>
        /// <returns>The value in hex.</returns>
        private static string Hex(int value, int digits) {
            return "0x" + value.ToString("X" + digits.ToString(CultureInfo.InvariantCulture));
        }

        /// <summary>Describes an identity kit in one line.</summary>
        /// <param name="definition">The decoded record.</param>
        /// <returns>The description.</returns>
        private static string DescribeIdentityKit(IdentityKitDefinition definition) {
            var parts = new List<string>(3);

            if (definition.ModelIds != null && definition.ModelIds.Length > 0)
                parts.Add(definition.ModelIds.Length + " body model" +
                          (definition.ModelIds.Length == 1 ? "" : "s"));

            int heads = definition.HeadModelIds.Count(model => model != -1);
            if (heads > 0)
                parts.Add(heads + " head model" + (heads == 1 ? "" : "s"));

            if (definition.RecolourFrom != null && definition.RecolourFrom.Length > 0)
                parts.Add(definition.RecolourFrom.Length + " recolours");

            return parts.Count == 0 ? "empty record" : string.Join(", ", parts);
        }

        /// <summary>Lists an identity kit's fields.</summary>
        /// <param name="definition">The decoded record.</param>
        /// <returns>The fields.</returns>
        private static IEnumerable<ConfigField> IdentityKitFields(IdentityKitDefinition definition) {
            yield return new ConfigField("Opcode 1 byte",
                definition.Has(1) ? definition.Unknown1.ToString() : "absent");
            yield return new ConfigField("Body models", Join(definition.ModelIds));
            yield return new ConfigField("Head models", Join(definition.HeadModelIds));
            yield return new ConfigField("Recolours", Pairs(definition.RecolourFrom, definition.RecolourTo));
            yield return new ConfigField("Retextures", Pairs(definition.RetextureFrom, definition.RetextureTo));
            yield return new ConfigField("Opcode 3 flag", definition.Unknown3.ToString());
        }

        /// <summary>Describes a render animation in one line.</summary>
        /// <param name="definition">The decoded record.</param>
        /// <returns>The description.</returns>
        private static string DescribeRenderAnimation(RenderAnimationDefinition definition) {
            var parts = new List<string>(3);

            if (definition.IdleAnimationId != -1)
                parts.Add("idle " + definition.IdleAnimationId);
            else if (definition.IdlePoolAnimationIds != null)
                parts.Add("idle pool of " + definition.IdlePoolAnimationIds.Length);

            if (definition.WalkForwardAnimationId != -1)
                parts.Add("walk " + definition.WalkForwardAnimationId);
            if (definition.RunForwardAnimationId != -1)
                parts.Add("run " + definition.RunForwardAnimationId);

            return parts.Count == 0 ? "no animation set" : string.Join(", ", parts);
        }

        /// <summary>Lists a render animation's fields.</summary>
        /// <param name="definition">The decoded record.</param>
        /// <returns>The fields.</returns>
        private static IEnumerable<ConfigField> RenderAnimationFields(RenderAnimationDefinition definition) {
            yield return new ConfigField("Idle", definition.IdleAnimationId.ToString());
            yield return new ConfigField("Idle pool",
                definition.IdlePoolAnimationIds == null
                    ? "none"
                    : Join(definition.IdlePoolAnimationIds) + " weighted " +
                      Join(definition.IdlePoolWeights) + ", total " + definition.IdlePoolWeightTotal);
            yield return new ConfigField("Move set", Quadrants(definition.MoveForwardAnimationId,
                definition.MoveAt90AnimationId, definition.MoveAt180AnimationId,
                definition.MoveAt270AnimationId));
            yield return new ConfigField("Walk set", Quadrants(definition.WalkForwardAnimationId,
                definition.WalkAt90AnimationId, definition.WalkAt180AnimationId,
                definition.WalkAt270AnimationId));
            yield return new ConfigField("Run set", Quadrants(definition.RunForwardAnimationId,
                definition.RunAt90AnimationId, definition.RunAt180AnimationId,
                definition.RunAt270AnimationId));
            yield return new ConfigField("Turn on the spot",
                definition.TurnOnSpotNegativeAnimationId + " / " + definition.TurnOnSpotPositiveAnimationId);
            yield return new ConfigField("Turn while moving",
                "move " + definition.MoveTurnNegativeAnimationId + "/" + definition.MoveTurnPositiveAnimationId +
                ", walk " + definition.WalkTurnNegativeAnimationId + "/" + definition.WalkTurnPositiveAnimationId +
                ", run " + definition.RunTurnNegativeAnimationId + "/" + definition.RunTurnPositiveAnimationId);
            yield return new ConfigField("Equipment slot order", Join(definition.EquipmentSlotOrder));
            yield return new ConfigField("Opcode 26 bytes",
                definition.Unknown26A + ", " + definition.Unknown26B);
            yield return new ConfigField("Opcode 54 bytes",
                definition.Unknown54A + ", " + definition.Unknown54B);

            if (definition.ModelSlotTransforms.Count > 0)
                yield return new ConfigField("Model slot transforms",
                    definition.ModelSlotTransforms.Count.ToString());
        }

        /// <summary>Lists a quest's fields.</summary>
        /// <param name="definition">The decoded record.</param>
        /// <returns>The fields.</returns>
        private static IEnumerable<ConfigField> QuestFields(QuestDefinition definition) {
            yield return new ConfigField("Name", definition.Name ?? "none");
            yield return new ConfigField("Alternate name", definition.AlternateName ?? "none");
            yield return new ConfigField("Chat icon sprite", definition.IconSpriteId.ToString());
            yield return new ConfigField("Opcode 3 entries", definition.Conditions3.Count.ToString());
            yield return new ConfigField("Opcode 4 entries", definition.Conditions4.Count.ToString());
            yield return new ConfigField("Discarded bytes",
                "op5 " + Present(definition, 5, definition.Unknown5) +
                ", op6 " + Present(definition, 6, definition.Unknown6) +
                ", op7 " + Present(definition, 7, definition.Unknown7) +
                ", op9 " + Present(definition, 9, definition.Unknown9));
            yield return new ConfigField("Opcode 8 flag", definition.Unknown8.ToString());

            if (definition.Parameters.Count > 0)
                yield return new ConfigField("Parameters", definition.Parameters.Count.ToString());
        }

        /// <summary>Lists a parameter block as one field per entry.</summary>
        /// <param name="parameters">The entries, in stored order.</param>
        /// <returns>The fields.</returns>
        private static IEnumerable<ConfigField> ParameterFields(List<ConfigParameter> parameters) {
            //Numbered by position rather than keyed by id: six struct records in this cache carry
            //the same key twice, so a key alone would name two rows.
            for (int i = 0; i < parameters.Count; i++) {
                ConfigParameter parameter = parameters[i];
                yield return new ConfigField("[" + i + "] key " + parameter.Key,
                    parameter.IsString ? "\"" + (parameter.StringValue ?? "") + "\"" : parameter.IntValue.ToString());
            }
        }

        /// <summary>Renders an opcode's stored value, or says it was absent.</summary>
        /// <param name="definition">The decoded record.</param>
        /// <param name="opcode">The opcode to report.</param>
        /// <param name="value">The field the opcode filled.</param>
        /// <returns>The value, or "absent".</returns>
        private static string Present(ConfigDefinition definition, int opcode, int value) {
            return definition.Has(opcode) ? value.ToString(CultureInfo.InvariantCulture) : "absent";
        }

        /// <summary>Renders one facing quadrant set.</summary>
        /// <param name="forward">The animation for facing along the heading.</param>
        /// <param name="at90">The animation 90 degrees off it.</param>
        /// <param name="at180">The animation 180 degrees off it.</param>
        /// <param name="at270">The animation 270 degrees off it.</param>
        /// <returns>The four ids.</returns>
        private static string Quadrants(int forward, int at90, int at180, int at270) {
            return "0 deg " + forward + ", 90 " + at90 + ", 180 " + at180 + ", 270 " + at270;
        }

        /// <summary>Renders an integer list, or says it is absent.</summary>
        /// <param name="values">The values, or null.</param>
        /// <returns>The list.</returns>
        private static string Join(IReadOnlyList<int>? values) {
            if (values == null || values.Count == 0)
                return "none";
            return string.Join(", ", values);
        }

        /// <summary>Renders a pair of parallel short arrays as arrow pairs.</summary>
        /// <param name="from">The source values, or null.</param>
        /// <param name="to">The replacement values, or null.</param>
        /// <returns>The pairs.</returns>
        private static string Pairs(short[]? from, short[]? to) {
            if (from == null || to == null || from.Length == 0)
                return "none";

            int shared = Math.Min(from.Length, to.Length);
            var pairs = new List<string>(shared);
            for (int i = 0; i < shared; i++)
                pairs.Add(from[i] + " -> " + to[i]);
            return string.Join(", ", pairs);
        }

        private static string DescribeMapElement(MapElementDefinition definition) {
            var parts = new List<string>(4);

            if (!string.IsNullOrEmpty(definition.Label))
                parts.Add("\"" + definition.Label + "\"");
            if (definition.SpriteId != -1)
                parts.Add("sprite " + definition.SpriteId);
            if (definition.PolygonVertices != null)
                parts.Add(definition.PolygonVertices.Length / 2 + "-vertex polygon");
            if (definition.CategoryId != -1)
                parts.Add("category " + definition.CategoryId);

            return parts.Count == 0 ? "empty record" : string.Join(", ", parts);
        }

        private static IEnumerable<ConfigField> MapElementFields(MapElementDefinition definition) {
            yield return new ConfigField("Label", definition.Label ?? "none");
            yield return new ConfigField("Label colour", Hex(definition.LabelRgb, 6));
            yield return new ConfigField("Font", definition.FontId.ToString());
            yield return new ConfigField("Sprite", definition.SpriteId + ", highlighted " +
                definition.HighlightedSpriteId);
            yield return new ConfigField("Flags byte", Hex(definition.Flags, 2));
            yield return new ConfigField("Drawn on minimap",
                definition.MinimapVisibleByte + (definition.DrawnOnMinimap ? " yes" : " no"));
            yield return new ConfigField("Rendered", definition.Rendered.ToString());
            yield return new ConfigField("Category", definition.CategoryId.ToString());
            yield return new ConfigField("Menu target", definition.MenuTarget ?? "none");
            yield return new ConfigField("Menu options",
                string.Join(" | ", definition.MenuActions.Select(action => action ?? "")));
            yield return new ConfigField("Visibility gate 1", "varbit " + definition.VisibleVarbitId +
                ", varp " + definition.VisibleVarpId + ", " + definition.VisibleMin + ".." +
                definition.VisibleMax);
            yield return new ConfigField("Visibility gate 2", "varbit " + definition.SecondVisibleVarbitId +
                ", varp " + definition.SecondVisibleVarpId + ", " + definition.SecondVisibleMin + ".." +
                definition.SecondVisibleMax);
            yield return new ConfigField("Fill", Hex(definition.FillArgb, 8));
            yield return new ConfigField("Outline", Hex(definition.OutlineArgb, 8));

            if (definition.PolygonVertices != null)
                yield return new ConfigField("Polygon",
                    definition.PolygonVertices.Length / 2 + " vertices, fill " +
                    Hex(definition.PolygonFillArgb, 8) + ", " +
                    (definition.PolygonEdgeArgb?.Length ?? 0) + " edge colours");

            if (definition.Parameters.Count > 0)
                yield return new ConfigField("Parameters", definition.Parameters.Count.ToString());
        }
    }
}
