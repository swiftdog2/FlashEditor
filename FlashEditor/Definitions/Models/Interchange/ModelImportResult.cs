using System;
using System.Collections.Generic;

namespace FlashEditor.Definitions.Models.Interchange {
    /// <summary>What an import did with one part of a model.</summary>
    public enum ModelImportDisposition {
        /// <summary>Taken from the OBJ, overwriting what the model held.</summary>
        Replaced,

        /// <summary>Carried over from the model unchanged, because OBJ cannot express it.</summary>
        Preserved,

        /// <summary>Present in the OBJ and deliberately not used.</summary>
        Ignored,

        /// <summary>The reason the import was refused.</summary>
        Refused
    }

    /// <summary>
    ///     One row of the account an import gives of itself.
    /// </summary>
    /// <remarks>
    ///     Built as rows rather than as prose so the editor can show them in a grid. The whole
    ///     point of an import that preserves most of a file is that the user can see which parts
    ///     those were, and a viewport that cannot be captured on this machine is not where that
    ///     gets confirmed.
    /// </remarks>
    public sealed class ModelImportEntry {
        /// <summary>What the row is about, in the user's terms.</summary>
        public string Field { get; }

        /// <summary>What happened to it.</summary>
        public ModelImportDisposition Disposition { get; }

        /// <summary>The counts or the reason behind it.</summary>
        public string Detail { get; }

        /// <summary>Binds one row.</summary>
        /// <param name="field">What the row is about.</param>
        /// <param name="disposition">What happened to it.</param>
        /// <param name="detail">The counts or the reason.</param>
        public ModelImportEntry(string field, ModelImportDisposition disposition, string detail) {
            Field = field ?? throw new ArgumentNullException(nameof(field));
            Disposition = disposition;
            Detail = detail ?? throw new ArgumentNullException(nameof(detail));
        }

        /// <summary>Renders the row as one line, for a log or a message box.</summary>
        /// <returns>The line.</returns>
        public override string ToString() => $"{Disposition}: {Field} - {Detail}";
    }

    /// <summary>
    ///     The outcome of writing an OBJ back over a model.
    /// </summary>
    /// <remarks>
    ///     A refusal is a normal outcome rather than an exception, because most of them are the
    ///     user's mesh disagreeing with the model rather than a defect. <see cref="Model"/> is null
    ///     exactly when <see cref="Succeeded"/> is false, and the original model is never modified
    ///     either way.
    /// </remarks>
    public sealed class ModelImportResult {
        /// <summary>Whether the mesh could be written back.</summary>
        public bool Succeeded { get; }

        /// <summary>
        ///     Whether the mesh differed from the model's own geometry.
        /// </summary>
        /// <remarks>
        ///     False means <see cref="Model"/> is the model that went in, untouched. That matters:
        ///     re-deriving the strip opcodes and smart widths from unchanged geometry would still
        ///     change the stored bytes, because the format has more than one legal encoding of the
        ///     same mesh and the shipped encoder did not always pick the shortest. An import that
        ///     changed nothing must write nothing.
        /// </remarks>
        public bool GeometryChanged { get; }

        /// <summary>
        ///     The model to save, or null when the import was refused.
        /// </summary>
        /// <remarks>
        ///     <c>Model.Encode()</c> gives the bytes to write back into index 7. To see the result
        ///     in the viewer, decode those bytes into a fresh <see cref="ModelDefinition"/> - the
        ///     projection carries normals and UVs that are computed at decode and cannot be patched
        ///     in place.
        /// </remarks>
        public ModelFile? Model { get; }

        /// <summary>One line saying what happened, or why it did not.</summary>
        public string Message { get; }

        /// <summary>Every part of the model, and what became of it.</summary>
        public IReadOnlyList<ModelImportEntry> Entries { get; }

        /// <summary>Binds an outcome.</summary>
        /// <param name="succeeded">Whether the mesh could be written back.</param>
        /// <param name="geometryChanged">Whether the mesh differed from the model's own.</param>
        /// <param name="model">The model to save, or null.</param>
        /// <param name="message">One line saying what happened.</param>
        /// <param name="entries">Every part of the model, and what became of it.</param>
        public ModelImportResult(bool succeeded, bool geometryChanged, ModelFile? model,
            string message, IReadOnlyList<ModelImportEntry> entries) {
            Succeeded = succeeded;
            GeometryChanged = geometryChanged;
            Model = model;
            Message = message ?? throw new ArgumentNullException(nameof(message));
            Entries = entries ?? throw new ArgumentNullException(nameof(entries));
        }
    }
}
