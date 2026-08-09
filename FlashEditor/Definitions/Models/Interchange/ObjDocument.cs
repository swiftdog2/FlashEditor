using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace FlashEditor.Definitions.Models.Interchange {
    /// <summary>
    ///     The text an export produced: the OBJ, and the material library it names.
    /// </summary>
    /// <remarks>
    ///     Kept as strings rather than written straight to disk so the exporter is a pure function
    ///     and can be asserted on without a filesystem. <see cref="Save"/> is the thin part.
    /// </remarks>
    public sealed class ObjDocument {
        /// <summary>The OBJ text, newline-separated.</summary>
        public string ObjText { get; }

        /// <summary>The MTL text, or null when the model needed no materials.</summary>
        public string? MaterialText { get; }

        /// <summary>
        ///     The file name the OBJ's <c>mtllib</c> line names, or null when there is no library.
        /// </summary>
        /// <remarks>A bare name rather than a path, so the pair stays portable between folders.</remarks>
        public string? MaterialFileName { get; }

        /// <summary>
        ///     One line per fact worth showing the user next to the export.
        /// </summary>
        /// <remarks>
        ///     The viewport cannot be screenshotted on this machine, so anything the export
        ///     computed has to be legible outside it. These are the same figures the OBJ header
        ///     carries as comments.
        /// </remarks>
        public IReadOnlyList<string> Summary { get; }

        /// <summary>Binds an exported pair.</summary>
        /// <param name="objText">The OBJ text.</param>
        /// <param name="materialText">The MTL text, or null.</param>
        /// <param name="materialFileName">The MTL file name, or null.</param>
        /// <param name="summary">Lines worth showing the user.</param>
        public ObjDocument(string objText, string? materialText, string? materialFileName,
            IReadOnlyList<string> summary) {
            ObjText = objText ?? throw new ArgumentNullException(nameof(objText));
            MaterialText = materialText;
            MaterialFileName = materialFileName;
            Summary = summary ?? throw new ArgumentNullException(nameof(summary));
        }

        /// <summary>
        ///     Writes the OBJ, and the material library beside it under the name the OBJ states.
        /// </summary>
        /// <remarks>
        ///     UTF-8 without a byte order mark. A BOM is legal in a text file and several OBJ
        ///     readers treat the first token of the first line as <c>﻿v</c> and skip the vertex.
        /// </remarks>
        /// <param name="objPath">Where the OBJ goes. The MTL lands in the same directory.</param>
        /// <returns>The paths written, OBJ first.</returns>
        public IReadOnlyList<string> Save(string objPath) {
            if (objPath == null)
                throw new ArgumentNullException(nameof(objPath));

            var encoding = new UTF8Encoding(false);
            var written = new List<string> { objPath };
            File.WriteAllText(objPath, ObjText, encoding);

            if (MaterialText == null || MaterialFileName == null)
                return written;

            string directory = Path.GetDirectoryName(Path.GetFullPath(objPath)) ?? string.Empty;
            string materialPath = Path.Combine(directory, MaterialFileName);
            File.WriteAllText(materialPath, MaterialText, encoding);
            written.Add(materialPath);
            return written;
        }
    }
}
