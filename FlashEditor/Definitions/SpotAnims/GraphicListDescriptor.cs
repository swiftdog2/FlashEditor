using System;
using System.Collections.Generic;
using FlashEditor.Cache;
using FlashEditor.Definitions.Editing;
using FlashEditor.IO;

namespace FlashEditor.Definitions.SpotAnims {
    /// <summary>
    ///     One spot animation from index 21 as a list row.
    /// </summary>
    /// <remarks>
    ///     The table carries no name hashes, so a graphic is addressable by id alone and every
    ///     column here is either the id or something the record states about itself.
    /// </remarks>
    public sealed class GraphicListing {
        /// <summary>Binds one decoded graphic to where it came from.</summary>
        /// <param name="address">The group and file, and the graphic id they carry.</param>
        /// <param name="record">The decoded record.</param>
        public GraphicListing(DefinitionAddress address, GraphicDefinition record) {
            Address = address;
            Record = record ?? throw new ArgumentNullException(nameof(record));
        }

        /// <summary>Where the record lives in the cache.</summary>
        public DefinitionAddress Address { get; }

        /// <summary>The decoded record.</summary>
        public GraphicDefinition Record { get; }

        /// <summary>The graphic id.</summary>
        public int GraphicId => Record.Id;

        /// <summary>The index-7 model group drawn for this effect.</summary>
        public int ModelId => Record.ModelId;

        /// <summary>The index-20 animation played on it, or nothing.</summary>
        /// <remarks>Null rather than -1 so "no animation" reads as an empty cell.</remarks>
        public object? AnimationId => Record.AnimationId < 0 ? null : Record.AnimationId;

        /// <summary>Horizontal scale in 128ths.</summary>
        public int ScaleXZ => Record.ScaleXZ;

        /// <summary>Vertical scale in 128ths.</summary>
        public int ScaleY => Record.ScaleY;

        /// <summary>
        ///     The stored rotation, flagged when the client will ignore it.
        /// </summary>
        /// <remarks>
        ///     Only 90, 180 and 270 are acted on; anything else leaves the model unrotated. Showing
        ///     the raw value with a marker states what the file says without correcting it.
        /// </remarks>
        public string Rotation =>
            Record.Rotation == 0 ? "0"
                : Record.RotationIsApplied ? Record.Rotation.ToString()
                : Record.Rotation + " (ignored)";

        /// <summary>Ambient light, stored value and the value the model is built with.</summary>
        public string Ambient => Record.Ambient + " (" + Record.EffectiveAmbient + ")";

        /// <summary>Contrast, stored value and the value the model is built with.</summary>
        public string Contrast => Record.Contrast + " (" + Record.EffectiveContrast + ")";

        /// <summary>How many colours the graphic replaces on its model.</summary>
        public int Recolours => Record.RecolourFrom.Length;

        /// <summary>How many materials it replaces.</summary>
        public int Retextures => Record.RetextureFrom.Length;

        /// <summary>Whether the entity's movement can cancel or defer the animation.</summary>
        public string RespectsMovement => Record.RespectsMovementInterrupt ? "yes" : "no";

        /// <summary>
        ///     The effect the record states, named by the opcode that stated it.
        /// </summary>
        /// <remarks>
        ///     The opcode is shown rather than only the kind because three opcodes produce the same
        ///     kind and differ only in how the parameter is written.
        /// </remarks>
        public string Effect {
            get {
                if (Record.EffectOpcode == GraphicDefinition.NoEffectOpcode)
                    return "";
                return "op" + Record.EffectOpcode + " kind " + Record.EffectKind +
                       " param " + Record.EffectParameter;
            }
        }

        /// <summary>The opcodes the record stored, in the order it stored them.</summary>
        /// <remarks>
        ///     One record in seven stores them out of ascending order, graphic 0 included, so the
        ///     order is a property of the file rather than a rendering choice.
        /// </remarks>
        public string OpcodeOrder {
            get {
                var parts = new List<string>(Record.Opcodes.Count);
                for (int i = 0; i < Record.Opcodes.Count; i++)
                    parts.Add(Record.Opcodes[i].Opcode.ToString());
                return string.Join(",", parts);
            }
        }
    }

    /// <summary>
    ///     Index 21 as a definition list: one flat row per spot animation.
    /// </summary>
    /// <remarks>
    ///     Editable in the plain numeric fields. Every one of them is an independent opcode carrying
    ///     a single value, so changing one rewrites that opcode's payload in place and leaves the
    ///     recorded order alone. The recolour and retexture tables and the effect opcodes are not
    ///     offered here - they are count-prefixed runs and a mutually exclusive opcode group, neither
    ///     of which a single cell can express.
    /// </remarks>
    public sealed class GraphicListDescriptor : DefinitionListDescriptor<GraphicListing> {
        private readonly IReadOnlyList<DefinitionColumn> columns;

        /// <summary>Lists every spot animation the index declares.</summary>
        public GraphicListDescriptor() {
            columns = new[] {
                DefinitionColumn.ReadOnly<GraphicListing>("Graphic", row => row.GraphicId, 90),
                DefinitionColumn.Number<GraphicListing>("Model", row => row.ModelId,
                    (row, value) => row.Record.ModelId = value, 90),
                DefinitionColumn.Number<GraphicListing>("Animation", row => row.AnimationId,
                    (row, value) => row.Record.AnimationId = value, 90),
                DefinitionColumn.Number<GraphicListing>("Scale XZ", row => row.ScaleXZ,
                    (row, value) => row.Record.ScaleXZ = value, 80),
                DefinitionColumn.Number<GraphicListing>("Scale Y", row => row.ScaleY,
                    (row, value) => row.Record.ScaleY = value, 80),
                DefinitionColumn.ReadOnly<GraphicListing>("Rotation", row => row.Rotation, 100),
                DefinitionColumn.ReadOnly<GraphicListing>("Ambient", row => row.Ambient, 90),
                DefinitionColumn.ReadOnly<GraphicListing>("Contrast", row => row.Contrast, 100),
                DefinitionColumn.ReadOnly<GraphicListing>("Recolours", row => row.Recolours, 80),
                DefinitionColumn.ReadOnly<GraphicListing>("Retextures", row => row.Retextures, 80),
                DefinitionColumn.ReadOnly<GraphicListing>("Movement", row => row.RespectsMovement, 80),
                DefinitionColumn.ReadOnly<GraphicListing>("Effect", row => row.Effect, 150),
                DefinitionColumn.ReadOnly<GraphicListing>("Opcodes", row => row.OpcodeOrder, 130)
            };
        }

        /// <inheritdoc/>
        public override int IndexId => RSConstants.GRAPHICS_INDEX;

        /// <inheritdoc/>
        public override string RowNoun => "spot animation";

        /// <inheritdoc/>
        public override IReadOnlyList<DefinitionColumn> Columns => columns;

        /// <inheritdoc/>
        public override bool IsEditable => true;

        /// <inheritdoc/>
        public override GraphicListing Decode(RSCache cache, DefinitionAddress address, JagStream payload) {
            var record = new GraphicDefinition { Id = address.DefinitionId };
            record.Decode(payload);
            return new GraphicListing(address, record);
        }

        /// <inheritdoc/>
        public override DefinitionAddress AddressOf(GraphicListing row) {
            if (row == null)
                throw new ArgumentNullException(nameof(row));
            return row.Address;
        }

        /// <inheritdoc/>
        public override JagStream Encode(GraphicListing row) {
            if (row == null)
                throw new ArgumentNullException(nameof(row));
            return row.Record.Encode();
        }
    }
}
