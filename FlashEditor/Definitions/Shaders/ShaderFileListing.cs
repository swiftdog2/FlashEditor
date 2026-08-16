using System;
using System.Collections.Generic;
using System.Globalization;
using FlashEditor.Cache;
using FlashEditor.Definitions.Editing;
using FlashEditor.IO;

namespace FlashEditor.Definitions.Shaders {
    /// <summary>
    ///     One shader program: which backend holds it, what the client calls it, and what its bytes
    ///     are.
    /// </summary>
    /// <remarks>
    ///     A file here is an opaque blob with no internal opcode structure, so there is no decode
    ///     beyond classification. What the row carries besides the bytes is the line-ending profile,
    ///     which is the only thing standing between a text editor and a silently rewritten file.
    /// </remarks>
    public sealed class ShaderFileListing : IDetailRow {
        /// <summary>Describes one shader file.</summary>
        /// <param name="address">Where the file lives.</param>
        /// <param name="backend">The recovered group name, or null.</param>
        /// <param name="shader">The recovered file name, or null.</param>
        /// <param name="groupNameHash">The group's stored identifier.</param>
        /// <param name="fileNameHash">The file's stored identifier.</param>
        /// <param name="document">The payload decoded for display.</param>
        /// <param name="shape">What the payload's leading bytes say it is.</param>
        public ShaderFileListing(DefinitionAddress address, string? backend, string? shader,
            int groupNameHash, int fileNameHash, ShaderTextDocument document, ShaderProgramShape shape) {
            Address = address;
            Backend = backend;
            Shader = shader;
            GroupNameHash = groupNameHash;
            FileNameHash = fileNameHash;
            Document = document ?? throw new ArgumentNullException(nameof(document));
            Shape = shape;
        }

        /// <summary>Where the file lives in the cache.</summary>
        public DefinitionAddress Address { get; }

        /// <summary>The rendering backend, or null when the group name was not recovered.</summary>
        public string? Backend { get; }

        /// <summary>The shader program name, or null when the file name was not recovered.</summary>
        public string? Shader { get; }

        /// <summary>The group's stored identifier.</summary>
        public int GroupNameHash { get; }

        /// <summary>The file's stored identifier.</summary>
        public int FileNameHash { get; }

        /// <summary>The payload decoded for display, and the only thing allowed to re-encode it.</summary>
        public ShaderTextDocument Document { get; }

        /// <summary>What the payload's leading bytes say it is.</summary>
        public ShaderProgramShape Shape { get; }

        /// <summary>The stored bytes.</summary>
        public byte[] Stored => Document.Original;

        /// <summary>The stored payload length.</summary>
        public int SizeBytes => Stored.Length;

        /// <summary>The backend as a cell value, falling back to the hash.</summary>
        public string BackendOrHash => Backend ?? "(hash " + GroupNameHash + ")";

        /// <summary>The shader name as a cell value, falling back to the hash.</summary>
        public string ShaderOrHash => Shader ?? "(hash " + FileNameHash + ")";

        /// <summary>
        ///     How the client addresses this file, or an empty string when either half is unnamed.
        /// </summary>
        /// <remarks>
        ///     Shown because it is the whole point of the per-file name index: this pair is what
        ///     <c>JS5Archive.method2739</c> is handed, and until that index existed the second half
        ///     of it could not be resolved at all.
        /// </remarks>
        public string ClientAddress => Backend == null || Shader == null
            ? string.Empty
            : "\"" + Backend + "\"/\"" + Shader + "\"";

        /// <summary>Whether this file can be edited as text here.</summary>
        public bool IsEditableText => Document.EditRefusal == null;

        /// <inheritdoc/>
        public string Summary =>
            BackendOrHash + " / " + ShaderOrHash + " - " + Shape.Description + ", " +
            SizeBytes.ToString("N0", CultureInfo.InvariantCulture) + " bytes, " + Document.EndingText;

        /// <inheritdoc/>
        public IReadOnlyList<DetailField> Fields {
            get {
                var fields = new List<DetailField> {
                    new DetailField("Group", Address.GroupId.ToString(CultureInfo.InvariantCulture)),
                    new DetailField("Backend", BackendOrHash),
                    new DetailField("Group name hash", GroupNameHash.ToString(CultureInfo.InvariantCulture)),
                    new DetailField("File", Address.FileId.ToString(CultureInfo.InvariantCulture)),
                    new DetailField("Shader", ShaderOrHash),
                    new DetailField("File name hash", FileNameHash.ToString(CultureInfo.InvariantCulture)),
                    new DetailField("Client address", ClientAddress),
                    new DetailField("Kind", Shape.Description),
                    new DetailField("Stored payload", SizeBytes.ToString("N0", CultureInfo.InvariantCulture) + " bytes"),
                    new DetailField("Line endings", Document.EndingText),
                    new DetailField("Ends with a newline", Document.EndsWithNewline ? "yes" : "no"),
                    new DetailField("Survives a text round trip", Document.RoundTripsExactly ? "yes" : "no")
                };

                if (Document.EditRefusal != null)
                    fields.Add(new DetailField("Editing", Document.EditRefusal));

                return fields;
            }
        }
    }

    /// <summary>
    ///     Index 31 as a list: one row per shader program, fourteen in this cache.
    /// </summary>
    /// <remarks>
    ///     Read only as a grid. Editing a shader is editing its whole text, which no cell editor can
    ///     do, so <c>ShaderEditorPanel</c> owns the write and takes it through
    ///     <see cref="CachePayloadTransfer.Stage"/> - the same unchanged-payload check a file import
    ///     goes through.
    /// </remarks>
    public sealed class ShaderListDescriptor : DefinitionListDescriptor<ShaderFileListing> {
        private readonly IReadOnlyList<DefinitionColumn> columns = new[] {
            DefinitionColumn.ReadOnly<ShaderFileListing>("Backend", row => row.BackendOrHash, 90),
            DefinitionColumn.ReadOnly<ShaderFileListing>("Shader", row => row.ShaderOrHash, 230),
            DefinitionColumn.ReadOnly<ShaderFileListing>("Group", row => row.Address.GroupId, 60),
            DefinitionColumn.ReadOnly<ShaderFileListing>("File", row => row.Address.FileId, 55),
            DefinitionColumn.ReadOnly<ShaderFileListing>("Kind", row => row.Shape.Description, 150),
            DefinitionColumn.ReadOnly<ShaderFileListing>("Size", row => row.SizeBytes, 80),
            DefinitionColumn.ReadOnly<ShaderFileListing>("Line endings", row => row.Document.EndingText, 110),
            DefinitionColumn.ReadOnly<ShaderFileListing>("Trailing newline",
                row => row.Document.EndsWithNewline ? "yes" : "no", 130),
            DefinitionColumn.ReadOnly<ShaderFileListing>("Editable",
                row => row.IsEditableText ? "yes" : "no", 80)
        };

        /// <inheritdoc/>
        public override int IndexId => RSConstants.GRAPHICS_SHADERS;

        /// <inheritdoc/>
        public override string RowNoun => "shader";

        /// <inheritdoc/>
        public override IReadOnlyList<DefinitionColumn> Columns => columns;

        /// <inheritdoc/>
        public override ShaderFileListing Decode(RSCache cache, DefinitionAddress address, JagStream payload) {
            if (cache == null)
                throw new ArgumentNullException(nameof(cache));
            if (payload == null)
                throw new ArgumentNullException(nameof(payload));

            RSReferenceTable table = cache.GetReferenceTable(IndexId);
            RSArchiveEntry? group = table.GetArchiveEntry(address.GroupId);
            RSFileEntry? file = group?.GetFileEntry(address.FileId);

            int groupHash = group?.GetIdentifier() ?? CacheNameIndex.Unnamed;
            int fileHash = file?.GetIdentifier() ?? CacheNameIndex.Unnamed;

            ShaderTextDocument document = ShaderTextDocument.Decode(payload.ToArray());

            return new ShaderFileListing(address,
                ShaderNames.GroupName(groupHash), ShaderNames.FileName(fileHash),
                groupHash, fileHash, document,
                ShaderProgramShape.Of(document.Original, document.IsText));
        }

        /// <inheritdoc/>
        public override DefinitionAddress AddressOf(ShaderFileListing row) {
            if (row == null)
                throw new ArgumentNullException(nameof(row));
            return row.Address;
        }
    }
}
