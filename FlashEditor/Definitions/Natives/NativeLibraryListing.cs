using System;
using System.Collections.Generic;
using System.Globalization;
using FlashEditor.Cache;
using FlashEditor.Definitions.Editing;
using FlashEditor.IO;

namespace FlashEditor.Definitions.Natives {
    /// <summary>
    ///     One native library: where it lives, what the cache calls it, and what its own bytes say
    ///     it is.
    /// </summary>
    /// <remarks>
    ///     There is no decode. A record here <i>is</i> the binary - the client writes the returned
    ///     bytes straight to disk and loads them (<c>Signlink.java:554-561</c>) - so the work this
    ///     type does is entirely classification: split the recovered name, and read the payload's own
    ///     header. Those two are kept apart on purpose, because the only thing worth knowing about
    ///     this index is where they disagree.
    /// </remarks>
    public sealed class NativeLibraryListing : IDetailRow {
        /// <summary>Describes one group.</summary>
        /// <param name="address">Where the file lives.</param>
        /// <param name="nameHash">The identifier the reference table stores.</param>
        /// <param name="name">The recovered name, split.</param>
        /// <param name="shape">What the payload's leading bytes say it is.</param>
        /// <param name="stored">The stored bytes.</param>
        /// <param name="anomaly">Why this group's name disagrees with its siblings', or null.</param>
        public NativeLibraryListing(DefinitionAddress address, int nameHash, NativeLibraryName name,
            NativeBinaryShape shape, byte[] stored, string? anomaly) {
            Address = address;
            NameHash = nameHash;
            Name = name;
            Shape = shape;
            Stored = stored ?? throw new ArgumentNullException(nameof(stored));
            Anomaly = anomaly;
        }

        /// <summary>Where the file lives in the cache.</summary>
        public DefinitionAddress Address { get; }

        /// <summary>The group id.</summary>
        public int GroupId => Address.GroupId;

        /// <summary>The identifier the reference table stores for the group.</summary>
        public int NameHash { get; }

        /// <summary>The recovered name, split into os, architecture and library.</summary>
        public NativeLibraryName Name { get; }

        /// <summary>What the payload's leading bytes say it is.</summary>
        public NativeBinaryShape Shape { get; }

        /// <summary>The stored bytes, for export.</summary>
        public byte[] Stored { get; }

        /// <summary>Why this group's name disagrees with its siblings', or null.</summary>
        public string? Anomaly { get; }

        /// <summary>The stored payload length.</summary>
        public int SizeBytes => Stored.Length;

        /// <summary>The name as stored, or a stand-in saying it was never recovered.</summary>
        public string PathOrHash => Name.Path.Length > 0
            ? Name.Path
            : "(unrecovered, hash " + NameHash + ")";

        /// <summary>
        ///     Whether the architecture the name claims and the one the header states agree.
        /// </summary>
        /// <remarks>
        ///     Blank rather than "yes" or "no" where either side has nothing to say - a universal
        ///     binary carries several architectures and an unrecovered name carries none, and
        ///     scoring either as a disagreement would put a warning on a file that is fine.
        /// </remarks>
        public string NameMatchesHeader {
            get {
                if (!Name.IsWellFormed || Shape.Bits == 0)
                    return string.Empty;

                bool claims64 = Name.Architecture.Contains("64", StringComparison.Ordinal);
                return claims64 == (Shape.Bits == 64) ? "yes" : "NO";
            }
        }

        /// <inheritdoc/>
        public string Summary =>
            "Group " + GroupId + " - " + PathOrHash + ", " + Shape.Format + " " + Shape.Architecture +
            ", " + SizeBytes.ToString("N0", CultureInfo.InvariantCulture) + " bytes";

        /// <inheritdoc/>
        public IReadOnlyList<DetailField> Fields {
            get {
                var fields = new List<DetailField> {
                    new DetailField("Group", GroupId.ToString(CultureInfo.InvariantCulture)),
                    new DetailField("Name", PathOrHash),
                    new DetailField("Name hash", NameHash.ToString(CultureInfo.InvariantCulture)),
                    new DetailField("Operating system", Name.OperatingSystem),
                    new DetailField("Architecture (name)", Name.Architecture),
                    new DetailField("Library", Name.Library),
                    new DetailField("File name", Name.FileName),
                    //The file inside the group is named "" and not the library filename. A lookup
                    //that hashes the filename for the file slot finds nothing; the client passes the
                    //empty string explicitly at Class35.java:102.
                    new DetailField("File id", Address.FileId + " (named \"\")"),
                    new DetailField("Format", Shape.Format),
                    new DetailField("Architecture (header)", Shape.Architecture),
                    new DetailField("Word width", Shape.BitsText),
                    new DetailField("Name agrees with header", NameMatchesHeader),
                    new DetailField("Stored payload", SizeBytes.ToString("N0", CultureInfo.InvariantCulture) + " bytes")
                };

                if (Anomaly != null)
                    fields.Add(new DetailField("Anomaly", Anomaly));

                return fields;
            }
        }
    }

    /// <summary>
    ///     Index 30 as a list: one row per group, one library per row.
    /// </summary>
    /// <remarks>
    ///     Read only, and it must stay that way. There is nothing to re-encode - a record is a
    ///     compiled binary - so the only edit this index supports is substituting one file for
    ///     another, which goes through <see cref="CachePayloadTransfer"/> rather than through a cell
    ///     editor. Leaving <see cref="DefinitionListDescriptor{TRow}.IsEditable"/> false is what
    ///     switches cell editing off entirely.
    /// </remarks>
    public sealed class NativeLibraryListDescriptor : DefinitionListDescriptor<NativeLibraryListing> {
        private readonly IReadOnlyList<DefinitionColumn> columns = new[] {
            DefinitionColumn.ReadOnly<NativeLibraryListing>("Group", row => row.GroupId, 60),
            DefinitionColumn.ReadOnly<NativeLibraryListing>("Name", row => row.PathOrHash, 260),
            DefinitionColumn.ReadOnly<NativeLibraryListing>("OS", row => row.Name.OperatingSystem, 80),
            DefinitionColumn.ReadOnly<NativeLibraryListing>("Arch", row => row.Name.Architecture, 90),
            DefinitionColumn.ReadOnly<NativeLibraryListing>("Library", row => row.Name.Library, 90),
            DefinitionColumn.ReadOnly<NativeLibraryListing>("Format", row => row.Shape.Format, 130),
            DefinitionColumn.ReadOnly<NativeLibraryListing>("Header arch", row => row.Shape.Architecture, 150),
            DefinitionColumn.ReadOnly<NativeLibraryListing>("Width", row => row.Shape.BitsText, 70),
            DefinitionColumn.ReadOnly<NativeLibraryListing>("Agrees", row => row.NameMatchesHeader, 70),
            DefinitionColumn.ReadOnly<NativeLibraryListing>("Size", row => row.SizeBytes, 100),
            DefinitionColumn.ReadOnly<NativeLibraryListing>("Anomaly",
                row => row.Anomaly == null ? string.Empty : "see detail", 90)
        };

        /* Surveyed once per bind rather than per row. The anomaly is a statement about the whole name
           set - one operating system spelling one architecture two ways - so it cannot be decided
           from a single group, and re-deriving it 36 times would read the reference table 36 times. */
        private NativeLibraryCensus? census;

        /// <inheritdoc/>
        public override int IndexId => RSConstants.NATIVE_LIBRARIES;

        /// <inheritdoc/>
        public override string RowNoun => "library";

        /// <inheritdoc/>
        public override IReadOnlyList<DefinitionColumn> Columns => columns;

        /// <summary>The survey this descriptor built, or null before the first row was decoded.</summary>
        public NativeLibraryCensus? Census => census;

        /// <inheritdoc/>
        public override NativeLibraryListing Decode(RSCache cache, DefinitionAddress address, JagStream payload) {
            if (cache == null)
                throw new ArgumentNullException(nameof(cache));
            if (payload == null)
                throw new ArgumentNullException(nameof(payload));

            census ??= NativeLibraryCensus.Build(cache);

            RSArchiveEntry? entry = cache.GetReferenceTable(IndexId).GetArchiveEntry(address.GroupId);
            int hash = entry?.GetIdentifier() ?? CacheNameIndex.Unnamed;
            NativeLibraryName name = NativeLibraryNames.TryGetName(hash, out string? recovered)
                ? NativeLibraryName.Parse(recovered)
                : NativeLibraryName.None;

            byte[] stored = payload.ToArray();

            return new NativeLibraryListing(address, hash, name,
                NativeBinaryShape.Of(stored), stored, census.AnomalyFor(address.GroupId));
        }

        /// <inheritdoc/>
        public override DefinitionAddress AddressOf(NativeLibraryListing row) {
            if (row == null)
                throw new ArgumentNullException(nameof(row));
            return row.Address;
        }
    }
}
