using System;
using System.Collections.Generic;
using System.Linq;
using FlashEditor.Cache;
using FlashEditor.Definitions.Editing;
using FlashEditor.IO;

namespace FlashEditor.Definitions.Animation {
    /// <summary>
    ///     One skeleton as a list row, bound to the address it was read from.
    /// </summary>
    /// <remarks>
    ///     The address is carried rather than derived. Index 1 is
    ///     <see cref="CacheIdShape.GroupPerId"/>, which by design cannot answer
    ///     <see cref="CacheAddressing.FileOf"/> - the file id is whatever the reference table
    ///     declares, and assuming 0 is exactly the guess that puts index 23 on the wrong file.
    /// </remarks>
    public sealed class SkeletonListing {
        /// <summary>Binds a decoded skeleton to where it came from.</summary>
        /// <param name="address">The group and file the record was read from.</param>
        /// <param name="skeleton">The decoded skeleton.</param>
        public SkeletonListing(DefinitionAddress address, SkeletonDefinition skeleton) {
            Address = address;
            Skeleton = skeleton ?? throw new ArgumentNullException(nameof(skeleton));
        }

        /// <summary>Where the record lives in the cache.</summary>
        public DefinitionAddress Address { get; }

        /// <summary>The decoded skeleton.</summary>
        public SkeletonDefinition Skeleton { get; }

        /// <summary>The skeleton id, which is the group id.</summary>
        public int Id => Skeleton.Id;

        /// <summary>How many bones it holds.</summary>
        public int BoneCount => Skeleton.BoneCount;

        /// <summary>How many label entries it holds across all bones.</summary>
        public int LabelCount => Skeleton.TotalLabelCount;

        /// <summary>How many bones have their flag byte set to the value the client tests for.</summary>
        public int FlagsSet => Skeleton.Bones.Count(bone => bone.IsFlagSet);

        /// <summary>
        ///     The distinct transform types present, ascending, as stored.
        /// </summary>
        /// <remarks>
        ///     Stored rather than remapped, so a skeleton carrying the aliased type 6 is visible as
        ///     one instead of reading as another type 2.
        /// </remarks>
        public string TransformTypes =>
            string.Join(", ", Skeleton.Bones.Select(bone => bone.TransformType).Distinct().OrderBy(type => type));
    }

    /// <summary>
    ///     Presents index 1's skeletons in a <c>DefinitionListPanel</c>.
    /// </summary>
    /// <remarks>
    ///     Read only, deliberately, even though <see cref="Encode"/> is implemented and round-trips
    ///     byte for byte. A skeleton's editable state is per bone - five values and a label list each,
    ///     up to 255 bones - and none of that fits a grid with one row per skeleton, so every column
    ///     here is a summary. Turning <see cref="IsEditable"/> on would offer to write summaries back,
    ///     which is meaningless. A bone-level editor needs a second grid bound to the selected row,
    ///     and it must not offer to change the bone count: a frame addresses a bone by position, so
    ///     inserting one silently re-points every index-0 frame that names this skeleton.
    /// </remarks>
    public sealed class SkeletonListDescriptor : DefinitionListDescriptor<SkeletonListing> {
        private static readonly IReadOnlyList<DefinitionColumn> SkeletonColumns = new[] {
            DefinitionColumn.ReadOnly<SkeletonListing>("Id", row => row.Id, 70),
            DefinitionColumn.ReadOnly<SkeletonListing>("Bones", row => row.BoneCount, 70),
            DefinitionColumn.ReadOnly<SkeletonListing>("Labels", row => row.LabelCount, 70),
            DefinitionColumn.ReadOnly<SkeletonListing>("Flags set", row => row.FlagsSet, 80),
            DefinitionColumn.ReadOnly<SkeletonListing>("Transform types", row => row.TransformTypes, 200)
        };

        /// <inheritdoc/>
        public override int IndexId => RSConstants.SKINS;

        /// <inheritdoc/>
        public override string RowNoun => "skeleton";

        /// <inheritdoc/>
        public override IReadOnlyList<DefinitionColumn> Columns => SkeletonColumns;

        /// <inheritdoc/>
        public override SkeletonListing Decode(RSCache cache, DefinitionAddress address, JagStream payload) {
            if (payload == null)
                throw new ArgumentNullException(nameof(payload));

            var skeleton = new SkeletonDefinition { Id = address.DefinitionId };
            skeleton.Decode(payload);
            return new SkeletonListing(address, skeleton);
        }

        /// <inheritdoc/>
        public override DefinitionAddress AddressOf(SkeletonListing row) {
            if (row == null)
                throw new ArgumentNullException(nameof(row));
            return row.Address;
        }

        /// <inheritdoc/>
        public override JagStream Encode(SkeletonListing row) {
            if (row == null)
                throw new ArgumentNullException(nameof(row));
            return row.Skeleton.Encode();
        }
    }
}
