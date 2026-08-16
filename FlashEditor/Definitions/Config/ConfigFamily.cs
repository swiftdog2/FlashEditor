using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using FlashEditor.IO;

namespace FlashEditor.Definitions.Config {
    /// <summary>
    ///     What kind of editor a config field wants, when it can be edited at all.
    /// </summary>
    /// <remarks>
    ///     Stated by the field rather than inferred from its rendered text, because the text is
    ///     ambiguous in both directions: a colour and a texture id are both integers, and
    ///     <c>0x3C1E0A</c> parsed as decimal would store a different colour and report nothing. The
    ///     four id kinds exist so the panel can offer the item-18 picker rather than a text box - the
    ///     whole point of that dialog is that "which sprite is the crosshair" is not a question a
    ///     number can answer.
    /// </remarks>
    public enum ConfigFieldEditor {
        /// <summary>Not editable. The default, and what every field of an unmodelled family is.</summary>
        None,

        /// <summary>A signed decimal integer, typed.</summary>
        Integer,

        /// <summary>Free text.</summary>
        Text,

        /// <summary>A packed <c>0xRRGGBB</c>, picked from a colour dialog or typed as hex.</summary>
        Colour,

        /// <summary>A flag, typed as true or false.</summary>
        Flag,

        /// <summary>An index-8 sprite group id, picked by looking at sprites.</summary>
        Sprite,

        /// <summary>An index-9 texture id, picked by looking at textures.</summary>
        Texture,

        /// <summary>An index-20 animation id, picked from the animation list.</summary>
        Animation,

        /// <summary>An index-13 font id, picked from the font list.</summary>
        Font
    }

    /// <summary>
    ///     One decoded field of a config record, as the editor shows it, and how to write it back.
    /// </summary>
    /// <remarks>
    ///     <b>A class rather than a struct, and the reason is the edit.</b> Two boxed structs with the
    ///     same name and value compare equal, and <see cref="BrightIdeasSoftware.FastObjectListView"/>
    ///     keys its model-to-row map on the object it is handed - so a record carrying the same
    ///     name/value pair twice would lose a row, and an edit committed against one would be
    ///     indistinguishable from an edit against the other.
    ///     <para>
    ///     <see cref="Write"/> closes over the decoded record the field was read from, so applying an
    ///     edit needs no reflection and no second copy of the field table. It throws for input it
    ///     cannot parse; the panel reports that on its status line rather than letting it out of a
    ///     cell editor, where an exception takes the form down.
    ///     </para>
    /// </remarks>
    public sealed class ConfigField {
        /// <summary>Names one field and the value it holds, read only.</summary>
        /// <param name="name">The field's name.</param>
        /// <param name="value">The value, already rendered.</param>
        public ConfigField(string name, string value)
            : this(name, value, ConfigFieldEditor.None, null) {
        }

        /// <summary>Names one field, the value it holds, and how an edit is written back.</summary>
        /// <param name="name">The field's name.</param>
        /// <param name="value">The value, already rendered.</param>
        /// <param name="editor">What kind of editor the value wants.</param>
        /// <param name="write">Applies an edited value to the decoded record, or null for read only.</param>
        public ConfigField(string name, string value, ConfigFieldEditor editor, Action<string>? write) {
            Name = name ?? string.Empty;
            Value = value ?? string.Empty;
            Editor = write == null ? ConfigFieldEditor.None : editor;
            Write = write;
        }

        /// <summary>The field's name.</summary>
        public string Name { get; }

        /// <summary>The value, rendered.</summary>
        public string Value { get; }

        /// <summary>What kind of editor the value wants, or <see cref="ConfigFieldEditor.None"/>.</summary>
        public ConfigFieldEditor Editor { get; }

        /// <summary>Applies an edited value to the decoded record, or null when the field is read only.</summary>
        public Action<string>? Write { get; }

        /// <summary>Whether an edit to this field can be written back.</summary>
        public bool IsEditable => Write != null;
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
        private readonly Func<object, ConfigRecord>? describe;
        private readonly Func<object, JagStream>? encode;

        private ConfigFamily(int groupId, string name, string rowNoun, string notes, bool modelled,
            Func<int, JagStream, ConfigRecord> read,
            Func<object, int?>? colour = null, Func<object, int?>? texture = null,
            Func<object, int?>? sprite = null,
            Func<object, ConfigRecord>? describe = null,
            Func<object, JagStream>? encode = null) {
            GroupId = groupId;
            Name = name;
            RowNoun = rowNoun;
            Notes = notes;
            IsModelled = modelled;
            this.read = read;
            Colour = colour;
            Texture = texture;
            Sprite = sprite;
            this.describe = describe;
            this.encode = encode;
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

        /// <summary>
        ///     Reads the one index-8 sprite group a record is recognised by, or null for a family
        ///     that names none.
        /// </summary>
        /// <remarks>
        ///     Four families name a sprite and each of them is otherwise a row of numbers - a cursor,
        ///     a minimap stamp, a world map marker and a hit splat are all things a user identifies by
        ///     looking at them, and "which record is the crosshair" is not a question a column of ids
        ///     can answer.
        ///     <para>
        ///     <b>One sprite, deliberately, even where a record names several.</b> Which one each
        ///     family gives is decided at its registration in <see cref="WithProviders"/> and argued
        ///     there; the rest stay in the detail pane, which is where a record's whole sprite set
        ///     belongs.
        ///     </para>
        ///     <para>
        ///     <b>Returning a negative id is the family saying "this record stores no sprite", and it
        ///     is a different answer from a sprite that would not read.</b> A negative reaches the
        ///     grid as <see cref="Editing.DefinitionCellArt.None"/>, so the cell shows the stored id
        ///     with no tile beside it; a real id that will not decode reaches
        ///     <c>SpriteThumbnailRenderer</c> and comes back as a marked tile, and one still being
        ///     read draws as an empty outline. Three states, three appearances. Do not fold "none"
        ///     into null - null means the accessor could not read the record at all.
        ///     </para>
        /// </remarks>
        public Func<object, int?>? Sprite { get; }

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
        ///     Whether a record of this family can be written back to the cache.
        /// </summary>
        /// <remarks>
        ///     False for <see cref="Unmodelled"/> alone. Every family with a codec re-encodes by
        ///     replaying the opcode stream it decoded, which is what makes an unedited record come
        ///     back byte for byte on an index where <b>none</b> of group 36's 1,051 files is in
        ///     ascending opcode order.
        /// </remarks>
        public bool CanEncode => encode != null;

        /// <summary>
        ///     Re-encodes one decoded record of this family.
        /// </summary>
        /// <remarks>
        ///     Straight to the record class's own <c>Encode</c>, which replays the recorded opcode
        ///     stream and re-derives only the last occurrence of each opcode from the live fields.
        ///     Nothing here rebuilds a stream from the values, because that would silently normalise
        ///     the order and the repetition this index is full of.
        /// </remarks>
        /// <param name="definition">The decoded record.</param>
        /// <returns>The encoded file, positioned at 0.</returns>
        /// <exception cref="NotSupportedException">This family has no codec.</exception>
        public JagStream Encode(object definition) {
            if (definition == null)
                throw new ArgumentNullException(nameof(definition));
            if (encode == null)
                throw new NotSupportedException(
                    "Config group " + GroupId + " has no codec here, so its records cannot be written back.");

            return encode(definition);
        }

        /// <summary>
        ///     Rebuilds the summary, the field list and the opcode list of a record that has been
        ///     edited.
        /// </summary>
        /// <remarks>
        ///     Needed because an edit changes the decoded record in place while the row still holds
        ///     the description built when it was read - and a pane that went on showing the old value
        ///     beside a grid cell showing the new one reads as an edit that half took.
        /// </remarks>
        /// <param name="definition">The decoded record.</param>
        /// <returns>The rebuilt description.</returns>
        /// <exception cref="NotSupportedException">This family has no codec.</exception>
        public ConfigRecord Describe(object definition) {
            if (definition == null)
                throw new ArgumentNullException(nameof(definition));
            if (describe == null)
                throw new NotSupportedException(
                    "Config group " + GroupId + " has no codec here, so its records cannot be re-described.");

            return describe(definition);
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
                    Swatch("Colour", definition.Rgb, value => definition.Rgb = value),
                    Asset("Texture", definition.TextureId, ConfigFieldEditor.Texture,
                        value => definition.TextureId = value),
                    Number("Texture scale", definition.TextureScale, value => definition.TextureScale = value),
                    Switch("Casts shadow", definition.CastsShadow, value => definition.CastsShadow = value),
                    Switch("Occludes", definition.Occludes, value => definition.Occludes = value)
                },
                definition => definition.Encode(),
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
                    /* Editing the colour of an overlay that stores none also sets HasPrimaryRgb,
                       because the codec's AddedOpcodes reads that flag to decide whether to append
                       opcode 1. Setting the value alone would look like an edit and write nothing.
                       Clearing it back to absent is deliberately not offered: the two states are
                       different bytes, and a text box cannot spell "absent" apart from black. */
                    Swatch("Primary colour", definition.PrimaryRgb,
                        value => {
                            definition.PrimaryRgb = value;
                            definition.HasPrimaryRgb = true;
                        }),
                    new ConfigField("Primary colour stored",
                        definition.HasPrimaryRgb ? "yes, opcode 1" : "no - opcode 1 absent"),
                    Swatch("Secondary colour", definition.SecondaryRgb,
                        value => definition.SecondaryRgb = value, optional: true),
                    Asset("Texture", definition.TextureId, ConfigFieldEditor.Texture,
                        value => definition.TextureId = value),
                    new ConfigField("Texture width",
                        definition.TextureIdIsShortForm ? "opcode 3, a short" : "opcode 2, a byte"),
                    Number("Texture scale", definition.TextureScale, value => definition.TextureScale = value),
                    Number("Priority", definition.Priority, value => definition.Priority = value),
                    new ConfigField("Priority composite", Hex(definition.ApplyPriorityComposite(), 4)),
                    Switch("Blends with neighbours", definition.BlendWithNeighbours,
                        value => definition.BlendWithNeighbours = value),
                    Switch("Flat ground occluder", definition.FlatGroundOccluder,
                        value => definition.FlatGroundOccluder = value),
                    Switch("Casts shadow", definition.CastsShadow, value => definition.CastsShadow = value),
                    Switch("World map background", definition.IsWorldMapBackground,
                        value => definition.IsWorldMapBackground = value),
                    Swatch("Water tint", definition.WaterTintRgb, value => definition.WaterTintRgb = value),
                    Number("Water depth", definition.WaterDepth, value => definition.WaterDepth = value),
                    Number("Water alpha", definition.WaterAlpha, value => definition.WaterAlpha = value)
                },
                definition => definition.Encode(),
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
                definition => new[] {
                    Number("Capacity", definition.Capacity, value => definition.Capacity = value)
                }),

            Of<ParameterTypeDefinition>(ConfigGroup.ParameterType, "Parameter types", "parameter type",
                "What an opcode 249 parameter key means and what its default is." +
                " Class365.java:102 names the group.",
                definition => "type '" + definition.TypeLetter + "'" +
                              (definition.IsString
                                  ? ", default \"" + (definition.DefaultString ?? "") + "\""
                                  : ", default " + definition.DefaultInt),
                definition => new[] {
                    /* The raw byte, not the character. The client remaps 0x80-0x9F through cp1252
                       on the way to a char, and one record in this cache stores 0x80 - so the
                       character is a rendering of the byte and the byte is what re-encodes. */
                    Number("Type letter byte", definition.TypeLetterByte,
                        value => definition.TypeLetterByte = value),
                    new ConfigField("Type letter", "'" + definition.TypeLetter + "' (byte " +
                        Hex(definition.TypeLetterByte, 2) + ")"),
                    new ConfigField("Holds a string", definition.IsString.ToString()),
                    Number("Default integer", definition.DefaultInt, value => definition.DefaultInt = value),
                    Words("Default string", definition.DefaultString,
                        value => definition.DefaultString = value),
                    Switch("Opcode 4 flag", definition.Unknown4, value => definition.Unknown4 = value)
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
                    Number("Persistence scope", definition.PersistenceScope,
                        value => definition.PersistenceScope = value),
                    new ConfigField("Reset on logout", definition.ResetOnLogout.ToString())
                }),

            Of<ClientVariableDefinition>(ConfigGroup.ClientVariable, "Client variables", "client variable",
                "The type letter of one client-side variable slot, and whether the server may write it." +
                " Class132.java:117 names the group.",
                definition => "type '" + definition.TypeLetter + "'" +
                              (definition.ServerWritable ? ", server writable" : ""),
                definition => new[] {
                    Number("Type letter byte", definition.TypeLetterByte,
                        value => definition.TypeLetterByte = value),
                    new ConfigField("Type letter", "'" + definition.TypeLetter + "' (byte " +
                        Hex(definition.TypeLetterByte, 2) + ")"),
                    Switch("Server writable", definition.ServerWritable,
                        value => definition.ServerWritable = value)
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
                    Number("Waveform", definition.Waveform, value => definition.Waveform = value),
                    Number("Rate", definition.Rate, value => definition.Rate = value),
                    Number("Amplitude", definition.Amplitude, value => definition.Amplitude = value),
                    Number("Offset", definition.Offset, value => definition.Offset = value)
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
                    /* Through SetSpriteGroupId rather than the property, because "no icon" has two
                       encodings here - the opcode absent, and opcode 4 present - and they are not
                       interchangeable on re-encode. Assigning the property alone on a record that
                       carries opcode 4 changes the field and nothing else, so the file comes back
                       identical and the edit silently does nothing. */
                    Asset("Sprite group", definition.SpriteGroupId, ConfigFieldEditor.Sprite,
                        value => definition.SetSpriteGroupId(value)),
                    new ConfigField("No-icon encoding", definition.DescribeAbsentIconEncoding()),
                    Swatch("Tint", definition.TintRgb, value => definition.TintRgb = value),
                    Switch("Stretch to footprint", definition.StretchToFootprint,
                        value => definition.StretchToFootprint = value)
                },
                definition => definition.Encode(),
                /* One sprite and no choice to make: opcode 1 is the icon and opcode 4 is the
                   explicit "none", which the client honours by drawing nothing at all
                   (Class122.java:93 gates the whole draw on anInt114 != -1). The tile is the sprite
                   untinted, because opcode 2's tint is applied by the client at draw time
                   (Class122.java:118-120) rather than being part of the stored picture. */
                sprite: definition => definition.SpriteGroupId),

            Of<CursorDefinition>(ConfigGroup.Cursor, "Cursors", "cursor",
                "The sprite drawn as the mouse pointer and the pixel within it that is the hotspot." +
                " Class11.java:33 names the group.",
                definition => "sprite " + definition.SpriteId + " at " + definition.HotspotX + "," +
                              definition.HotspotY,
                definition => new[] {
                    Asset("Sprite", definition.SpriteId, ConfigFieldEditor.Sprite,
                        value => definition.SpriteId = value),
                    Number("Hotspot x", definition.HotspotX, value => definition.HotspotX = value),
                    Number("Hotspot y", definition.HotspotY, value => definition.HotspotY = value)
                },
                /* The only sprite a cursor names, and the only one of these four families with no
                   "none" to represent: the field has no -1 encoding - Class231.anInt1735 starts at
                   Java's 0, which is a real index-8 group - and all 175 records carry opcode 1
                   anyway. The tile is the pointer image itself, which is what
                   Class231.java:127 hands the platform through RSFont.java:82-95. */
                sprite: definition => definition.SpriteId),

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
                definition => MapElementFields(definition),
                /* Opcode 1 of the three sprite opcodes, because it is the only one the client will
                   draw on its own. Node_Sub40.java:116-119 gates the whole sprite on
                   anInt245 != -1 and only then swaps in opcode 2's anInt225, and only while the
                   marker is hovered, so opcode 2 is a second state of this picture rather than a
                   record of its own. Opcode 18's anInt231 occurs in no file of either cache and
                   what selects it is not settled, so a tile drawn from it would be a guess. Both of
                   the others stay in the detail pane's Sprite field. */
                sprite: definition => definition.SpriteId),

            Of<DamageMarkDefinition>(ConfigGroup.DamageMark, "Damage marks", "damage mark",
                "The sprites, font, colour and lifetime of one hit splat drawn over a mobile." +
                " Class121.java:102 names the group.",
                definition => "font " + definition.FontId + ", " + definition.LifetimeMillis + " ms",
                definition => new[] {
                    Asset("Font", definition.FontId, ConfigFieldEditor.Font,
                        value => definition.FontId = value),
                    Swatch("Text colour", definition.TextRgb, value => definition.TextRgb = value),
                    //The one string field in the index whose record class cannot hold a null. Its
                    //constructor default is the empty string, so an empty edit restores the default
                    //and AddedOpcodes leaves the opcode off, which is the same outcome.
                    Words("Number template", definition.NumberTemplate,
                        value => definition.NumberTemplate = value ?? ""),
                    Asset("Sprite layer 1", definition.SpriteLayer1Id, ConfigFieldEditor.Sprite,
                        value => definition.SpriteLayer1Id = value),
                    Asset("Sprite layer 2", definition.SpriteLayer2Id, ConfigFieldEditor.Sprite,
                        value => definition.SpriteLayer2Id = value),
                    Asset("Sprite layer 3", definition.SpriteLayer3Id, ConfigFieldEditor.Sprite,
                        value => definition.SpriteLayer3Id = value),
                    Asset("Preloaded sprite", definition.PreloadedSpriteId, ConfigFieldEditor.Sprite,
                        value => definition.PreloadedSpriteId = value),
                    Number("Drift x", definition.DriftX, value => definition.DriftX = value),
                    Number("Drift y", definition.DriftY, value => definition.DriftY = value),
                    Number("Offset y", definition.OffsetY, value => definition.OffsetY = value),
                    Number("Lifetime (ms)", definition.LifetimeMillis,
                        value => definition.LifetimeMillis = value),
                    Number("Fade start (ms)", definition.FadeStartMillis,
                        value => definition.FadeStartMillis = value),
                    /* The same shape as group 34's "no icon": two opcodes write this field, opcode
                       11 spelling a zero with no payload and opcode 14 storing a short, so which
                       was stored is only recoverable from the opcode list. A record carrying opcode
                       11 replays it whatever the field says, so an edit to that record's fade start
                       cannot take - which is worth saying rather than leaving as a silent no-op.
                       Opcode 11 occurs in no file of either cache; opcode 14 occurs in all 28. */
                    new ConfigField("Fade start encoding",
                        definition.Has(11)
                            ? "opcode 11, a bare zero - editing this field cannot change the bytes"
                            : definition.Has(14) ? "opcode 14, a stored short" : "absent"),
                    Number("Opcode 12 field", definition.Unknown12, value => definition.Unknown12 = value)
                },
                sprite: definition => LeadingDamageMarkSprite(definition))
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
        /// <param name="sprite">Reads the record's index-8 sprite group, for a family that names one.</param>
        /// <returns>The family.</returns>
        private static ConfigFamily Of<T>(int groupId, string name, string rowNoun, string notes,
            Func<T, string> summary, Func<T, IEnumerable<ConfigField>> fields,
            Func<T, int?>? sprite = null)
            where T : ConfigDefinition, new() {
            ConfigRecord Describe(T definition) {
                return new ConfigRecord(definition, summary(definition), OpcodesOf(definition),
                    fields(definition).ToArray());
            }

            return new ConfigFamily(groupId, name, rowNoun, notes, true, (id, payload) => {
                var definition = new T { Id = id };
                definition.Decode(payload);
                return Describe(definition);
            }, sprite: Widen(sprite),
                describe: record => Describe((T) record),
                encode: record => ((T) record).Encode());
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
        /// <param name="sprite">Reads the record's index-8 sprite group, for a family that names one.</param>
        private static ConfigFamily Legacy<T>(int groupId, string name, string rowNoun, string notes,
            Func<int, JagStream, T> decode, Func<T, List<DecodedOpcode>> opcodes,
            Func<T, string> summary, Func<T, IEnumerable<ConfigField>> fields,
            Func<T, JagStream> encode,
            Func<T, int?>? colour = null, Func<T, int?>? texture = null,
            Func<T, int?>? sprite = null) where T : class {
            ConfigRecord Describe(T definition) {
                ConfigOpcodeRow[] rows = opcodes(definition)
                    .Select(entry => new ConfigOpcodeRow(entry.Opcode,
                        entry.Value.ToString(CultureInfo.InvariantCulture)))
                    .ToArray();
                return new ConfigRecord(definition, summary(definition), rows, fields(definition).ToArray());
            }

            return new ConfigFamily(groupId, name, rowNoun, notes, true,
                (id, payload) => Describe(decode(id, payload)),
                Widen(colour), Widen(texture), Widen(sprite),
                record => Describe((T) record),
                record => encode((T) record));
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

        /// <summary>An integer field the user can retype.</summary>
        /// <param name="name">The field's name.</param>
        /// <param name="value">Its current value.</param>
        /// <param name="set">Writes an edited value onto the decoded record.</param>
        /// <returns>The field.</returns>
        private static ConfigField Number(string name, int value, Action<int> set) {
            return new ConfigField(name, value.ToString(CultureInfo.InvariantCulture),
                ConfigFieldEditor.Integer, text => set(ParseInt(text)));
        }

        /// <summary>
        ///     A text field the user can retype.
        /// </summary>
        /// <remarks>
        ///     A null renders as an empty cell rather than as the word "none", which would otherwise
        ///     be storable as a literal label - and an empty edit writes the null back rather than a
        ///     zero-length string. That is the difference between an edit that undoes itself and one
        ///     that does not: a record carrying no string decodes to null, and a setter that turned
        ///     it into <c>""</c> would leave the field differing from its constructor default, which
        ///     is the only signal <c>AddedOpcodes</c> has - so clearing an already-empty field would
        ///     append an opcode the file never carried.
        /// </remarks>
        /// <param name="name">The field's name.</param>
        /// <param name="value">Its current value, which may be null.</param>
        /// <param name="set">Writes an edited value onto the decoded record.</param>
        /// <returns>The field.</returns>
        private static ConfigField Words(string name, string? value, Action<string?> set) {
            return new ConfigField(name, value ?? string.Empty, ConfigFieldEditor.Text,
                text => set(string.IsNullOrEmpty(text) ? null : text));
        }

        /// <summary>A flag field the user can retype as true or false.</summary>
        /// <param name="name">The field's name.</param>
        /// <param name="value">Its current value.</param>
        /// <param name="set">Writes an edited value onto the decoded record.</param>
        /// <returns>The field.</returns>
        private static ConfigField Switch(string name, bool value, Action<bool> set) {
            return new ConfigField(name, value ? "true" : "false", ConfigFieldEditor.Flag,
                text => set(ParseBool(text)));
        }

        /// <summary>
        ///     A packed <c>0xRRGGBB</c> the user can pick from a colour dialog.
        /// </summary>
        /// <param name="name">The field's name.</param>
        /// <param name="rgb">The packed colour, or -1 on an optional field that stores none.</param>
        /// <param name="set">Writes an edited colour onto the decoded record.</param>
        /// <param name="optional">Whether -1 is a legal value meaning the record stores no colour.</param>
        /// <returns>The field.</returns>
        private static ConfigField Swatch(string name, int rgb, Action<int> set, bool optional = false) {
            string rendered = optional && rgb == -1 ? "none" : Hex(rgb, 6);
            return new ConfigField(name, rendered, ConfigFieldEditor.Colour,
                text => set(ParseColour(text, optional)));
        }

        /// <summary>An id naming a picture or a record in another index, picked rather than typed.</summary>
        /// <param name="name">The field's name.</param>
        /// <param name="id">Its current value.</param>
        /// <param name="editor">Which index the id addresses.</param>
        /// <param name="set">Writes an edited id onto the decoded record.</param>
        /// <returns>The field.</returns>
        private static ConfigField Asset(string name, int id, ConfigFieldEditor editor, Action<int> set) {
            return new ConfigField(name, id.ToString(CultureInfo.InvariantCulture), editor,
                text => set(ParseInt(text)));
        }

        /// <summary>An index-20 animation id, picked rather than typed.</summary>
        /// <param name="name">The field's name.</param>
        /// <param name="id">Its current value.</param>
        /// <param name="set">Writes an edited id onto the decoded record.</param>
        /// <returns>The field.</returns>
        private static ConfigField Animation(string name, int id, Action<int> set) {
            return Asset(name, id, ConfigFieldEditor.Animation, set);
        }

        /// <summary>
        ///     An edited integer, or a refusal naming what could not be read.
        /// </summary>
        /// <remarks>
        ///     Thrown rather than defaulted to zero. An unparseable edit that quietly stored 0 would
        ///     be a silent write of a legal value the user never asked for, and on this index a
        ///     stored 0 is frequently a real setting rather than an empty one.
        /// </remarks>
        /// <param name="text">What the editor produced.</param>
        /// <returns>The value.</returns>
        private static int ParseInt(string? text) {
            string trimmed = (text ?? string.Empty).Trim();

            if (int.TryParse(trimmed, NumberStyles.Integer, CultureInfo.InvariantCulture, out int value))
                return value;

            throw new FormatException("\"" + trimmed + "\" is not a whole number.");
        }

        /// <summary>An edited flag, or a refusal naming what could not be read.</summary>
        /// <param name="text">What the editor produced.</param>
        /// <returns>The value.</returns>
        private static bool ParseBool(string? text) {
            string trimmed = (text ?? string.Empty).Trim();

            if (bool.TryParse(trimmed, out bool value))
                return value;
            if (trimmed == "1")
                return true;
            if (trimmed == "0")
                return false;

            throw new FormatException("\"" + trimmed + "\" is not true or false.");
        }

        /// <summary>
        ///     An edited colour, read as hexadecimal.
        /// </summary>
        /// <remarks>
        ///     Hexadecimal always, never <c>Convert.ToInt32</c>: a bare "3C1E0A" read as decimal
        ///     stores a different colour and reports nothing, which is worse than refusing because
        ///     the swatch then shows a value the user did not choose.
        /// </remarks>
        /// <param name="text">What the editor produced.</param>
        /// <param name="optional">Whether the word "none" is legal and means -1.</param>
        /// <returns>The packed colour.</returns>
        private static int ParseColour(string? text, bool optional) {
            string trimmed = (text ?? string.Empty).Trim();

            if (optional && (trimmed.Length == 0 ||
                             string.Equals(trimmed, "none", StringComparison.OrdinalIgnoreCase)))
                return -1;

            if (trimmed.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
                trimmed = trimmed.Substring(2);
            if (trimmed.StartsWith("#", StringComparison.Ordinal))
                trimmed = trimmed.Substring(1);

            if (int.TryParse(trimmed, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out int value))
                return value;

            throw new FormatException("\"" + trimmed + "\" is not a hexadecimal colour.");
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
            /* The opcode-1 byte is read and thrown away by the client (Class152.java:257) and takes
               values 0..13 across all 652 records, so it is not constant and cannot be recomputed -
               it is kept verbatim and is worth being able to set. */
            yield return Number("Opcode 1 byte", definition.Unknown1, value => definition.Unknown1 = value);
            yield return new ConfigField("Body models", Join(definition.ModelIds));
            yield return new ConfigField("Head models", Join(definition.HeadModelIds));
            yield return new ConfigField("Recolours", Pairs(definition.RecolourFrom, definition.RecolourTo));
            yield return new ConfigField("Retextures", Pairs(definition.RetextureFrom, definition.RetextureTo));
            yield return Switch("Opcode 3 flag", definition.Unknown3, value => definition.Unknown3 = value);
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
        /// <summary>
        ///     Lists a render animation's fields, one animation id per row.
        /// </summary>
        /// <remarks>
        ///     One row per id rather than a quadrant set on a line, because every one of these is an
        ///     index-20 animation and the picker can only be offered to a field that holds exactly
        ///     one. Four ids on one line reads more compactly and cannot be edited or previewed at
        ///     all, which is the trade the whole item makes the other way.
        /// </remarks>
        /// <param name="definition">The decoded record.</param>
        /// <returns>The fields.</returns>
        private static IEnumerable<ConfigField> RenderAnimationFields(RenderAnimationDefinition definition) {
            yield return Animation("Idle", definition.IdleAnimationId,
                value => definition.IdleAnimationId = value);
            yield return new ConfigField("Idle pool",
                definition.IdlePoolAnimationIds == null
                    ? "none"
                    : Join(definition.IdlePoolAnimationIds) + " weighted " +
                      Join(definition.IdlePoolWeights) + ", total " + definition.IdlePoolWeightTotal);

            yield return Animation("Move 0 deg", definition.MoveForwardAnimationId,
                value => definition.MoveForwardAnimationId = value);
            yield return Animation("Move 90", definition.MoveAt90AnimationId,
                value => definition.MoveAt90AnimationId = value);
            yield return Animation("Move 180", definition.MoveAt180AnimationId,
                value => definition.MoveAt180AnimationId = value);
            yield return Animation("Move 270", definition.MoveAt270AnimationId,
                value => definition.MoveAt270AnimationId = value);

            yield return Animation("Walk 0 deg", definition.WalkForwardAnimationId,
                value => definition.WalkForwardAnimationId = value);
            yield return Animation("Walk 90", definition.WalkAt90AnimationId,
                value => definition.WalkAt90AnimationId = value);
            yield return Animation("Walk 180", definition.WalkAt180AnimationId,
                value => definition.WalkAt180AnimationId = value);
            yield return Animation("Walk 270", definition.WalkAt270AnimationId,
                value => definition.WalkAt270AnimationId = value);

            yield return Animation("Run 0 deg", definition.RunForwardAnimationId,
                value => definition.RunForwardAnimationId = value);
            yield return Animation("Run 90", definition.RunAt90AnimationId,
                value => definition.RunAt90AnimationId = value);
            yield return Animation("Run 180", definition.RunAt180AnimationId,
                value => definition.RunAt180AnimationId = value);
            yield return Animation("Run 270", definition.RunAt270AnimationId,
                value => definition.RunAt270AnimationId = value);

            yield return Animation("Turn on spot -", definition.TurnOnSpotNegativeAnimationId,
                value => definition.TurnOnSpotNegativeAnimationId = value);
            yield return Animation("Turn on spot +", definition.TurnOnSpotPositiveAnimationId,
                value => definition.TurnOnSpotPositiveAnimationId = value);
            yield return Animation("Move turn -", definition.MoveTurnNegativeAnimationId,
                value => definition.MoveTurnNegativeAnimationId = value);
            yield return Animation("Move turn +", definition.MoveTurnPositiveAnimationId,
                value => definition.MoveTurnPositiveAnimationId = value);
            yield return Animation("Walk turn -", definition.WalkTurnNegativeAnimationId,
                value => definition.WalkTurnNegativeAnimationId = value);
            yield return Animation("Walk turn +", definition.WalkTurnPositiveAnimationId,
                value => definition.WalkTurnPositiveAnimationId = value);
            yield return Animation("Run turn -", definition.RunTurnNegativeAnimationId,
                value => definition.RunTurnNegativeAnimationId = value);
            yield return Animation("Run turn +", definition.RunTurnPositiveAnimationId,
                value => definition.RunTurnPositiveAnimationId = value);

            yield return new ConfigField("Equipment slot order", Join(definition.EquipmentSlotOrder));
            yield return Number("Opcode 26 byte a", definition.Unknown26A,
                value => definition.Unknown26A = value);
            yield return Number("Opcode 26 byte b", definition.Unknown26B,
                value => definition.Unknown26B = value);
            yield return Number("Opcode 54 byte a", definition.Unknown54A,
                value => definition.Unknown54A = value);
            yield return Number("Opcode 54 byte b", definition.Unknown54B,
                value => definition.Unknown54B = value);

            if (definition.ModelSlotTransforms.Count > 0)
                yield return new ConfigField("Model slot transforms",
                    definition.ModelSlotTransforms.Count.ToString());
        }

        /// <summary>Lists a quest's fields.</summary>
        /// <param name="definition">The decoded record.</param>
        /// <returns>The fields.</returns>
        private static IEnumerable<ConfigField> QuestFields(QuestDefinition definition) {
            yield return Words("Name", definition.Name, value => definition.Name = value);
            yield return Words("Alternate name", definition.AlternateName,
                value => definition.AlternateName = value);
            yield return Asset("Chat icon sprite", definition.IconSpriteId, ConfigFieldEditor.Sprite,
                value => definition.IconSpriteId = value);
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

        /// <summary>
        ///     The leftmost sprite a damage mark actually carries, which is the one tile that stands
        ///     for the record.
        /// </summary>
        /// <remarks>
        ///     <b>A hit splat is four sprites and a number laid out left to right, not stacked</b>, so
        ///     no single one of them is the record. IntegerNode.java:596-624 walks one x cursor
        ///     <c>i_85_</c> through opcode 3, opcode 4, opcode 5 repeated to the width of the number,
        ///     the number itself and then opcode 6, and :719-740 draws each at the offset it was
        ///     given.
        ///     <para>
        ///     Taking the leftmost piece <i>present</i> rather than opcode 3 outright, because opcode
        ///     3 is optional and the group exercises that: in the vanilla capture nine of the first
        ///     twenty-five records store opcodes 4, 5 and 6 with no 3. Fixing the tile to opcode 3
        ///     drew "-1, no picture" over a third of the grid for records that carry three sprites
        ///     each, which is the column asserting something false rather than declining to answer.
        ///     A record naming none of the four still reports -1, and that is then true.
        ///     </para>
        ///     <para>
        ///     The cost is that the cell does not say which opcode supplied the tile, so two rows'
        ///     tiles are not always the same piece of the splat. The detail pane's Sprite layers
        ///     field lists all four ids, and the Order column shows which opcodes the record stored,
        ///     so the row itself carries the answer.
        ///     </para>
        /// </remarks>
        /// <param name="definition">The decoded record.</param>
        /// <returns>The sprite group, or -1 when the record names none.</returns>
        private static int LeadingDamageMarkSprite(DamageMarkDefinition definition) {
            if (definition.SpriteLayer1Id >= 0) return definition.SpriteLayer1Id;
            if (definition.SpriteLayer2Id >= 0) return definition.SpriteLayer2Id;
            if (definition.PreloadedSpriteId >= 0) return definition.PreloadedSpriteId;
            return definition.SpriteLayer3Id;
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
            yield return Words("Label", definition.Label, value => definition.Label = value);
            yield return Swatch("Label colour", definition.LabelRgb, value => definition.LabelRgb = value);
            yield return Asset("Font", definition.FontId, ConfigFieldEditor.Font,
                value => definition.FontId = value);
            yield return Asset("Sprite", definition.SpriteId, ConfigFieldEditor.Sprite,
                value => definition.SpriteId = value);
            yield return Asset("Highlighted sprite", definition.HighlightedSpriteId,
                ConfigFieldEditor.Sprite, value => definition.HighlightedSpriteId = value);
            yield return new ConfigField("Flags byte", Hex(definition.Flags, 2));
            yield return new ConfigField("Drawn on minimap",
                definition.MinimapVisibleByte + (definition.DrawnOnMinimap ? " yes" : " no"));
            yield return Switch("Rendered", definition.Rendered, value => definition.Rendered = value);
            yield return Number("Category", definition.CategoryId, value => definition.CategoryId = value);
            yield return Words("Menu target", definition.MenuTarget, value => definition.MenuTarget = value);
            yield return new ConfigField("Menu options",
                string.Join(" | ", definition.MenuActions.Select(action => action ?? "")));
            yield return new ConfigField("Visibility gate 1", "varbit " + definition.VisibleVarbitId +
                ", varp " + definition.VisibleVarpId + ", " + definition.VisibleMin + ".." +
                definition.VisibleMax);
            yield return new ConfigField("Visibility gate 2", "varbit " + definition.SecondVisibleVarbitId +
                ", varp " + definition.SecondVisibleVarpId + ", " + definition.SecondVisibleMin + ".." +
                definition.SecondVisibleMax);
            /* Argb rather than Rgb, and signed: op 21 and op 22 are read with readInt and every
               measured value is negative, so a colour dialog would have to throw the alpha away.
               Editable as the integer the file stores instead. */
            yield return Number("Fill (signed ARGB)", definition.FillArgb, value => definition.FillArgb = value);
            yield return Number("Outline (signed ARGB)", definition.OutlineArgb,
                value => definition.OutlineArgb = value);

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
