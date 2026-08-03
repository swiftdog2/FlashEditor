using System;
using System.Collections.Generic;
using System.Linq;
using FlashEditor.cache;
using FlashEditor.Definitions.Animation;
using static FlashEditor.Utils.DebugUtil;

namespace FlashEditor.Definitions.Editing {
    /// <summary>
    ///     One index-0 group as a list row: an animation's whole frame set, and the index-1 skeleton
    ///     every frame in it is played against.
    /// </summary>
    /// <remarks>
    ///     A row is a <b>group</b>, not a file. Index 0 holds 359,931 files in 3526 groups, and a
    ///     single file on its own says almost nothing - the group is what one animation is
    ///     (<c>Node_Sub46_Sub16.java:113-123</c> loads every file of one group and indexes it by file
    ///     id). Listing files instead would produce a hundred rows per animation and no way to tell
    ///     where one ends.
    ///     <para>
    ///     The skeleton is carried here rather than looked up on selection because the frame-set
    ///     columns that make the list worth sorting - bone count, label count, the transform types in
    ///     play - all come from index 1, and because <see cref="EffectiveTransformTypes"/> has to be
    ///     resolved once per set rather than once per frame.
    ///     </para>
    /// </remarks>
    public sealed class FrameSetListing {
        private int[]? effectiveTransformTypes;

        /// <summary>Binds one frame set to the skeleton it names.</summary>
        /// <param name="address">The group, and the first file in it.</param>
        /// <param name="frameCount">How many files the reference table declares for the group.</param>
        /// <param name="firstFrame">The group's first frame, decoded - the only one read to build the row.</param>
        /// <param name="skeleton">The skeleton it names, or null when index 1 does not hold it.</param>
        public FrameSetListing(DefinitionAddress address, int frameCount, FrameDefinition firstFrame,
            SkeletonDefinition? skeleton) {
            Address = address;
            FrameCount = frameCount;
            FirstFrame = firstFrame ?? throw new ArgumentNullException(nameof(firstFrame));
            Skeleton = skeleton;
        }

        /// <summary>Where the frame the row was built from lives.</summary>
        public DefinitionAddress Address { get; }

        /// <summary>The frame set id, which is the group id.</summary>
        public int SetId => Address.GroupId;

        /// <summary>How many frames the set holds.</summary>
        /// <remarks>
        ///     From the reference table, so it counts the files that exist rather than the highest id
        ///     plus one. Frame arrays are legitimately sparse - the client sizes them by capacity
        ///     (<c>JS5Archive.java:207-221</c>) and leaves holes null.
        /// </remarks>
        public int FrameCount { get; }

        /// <summary>The group's first frame, kept so the tab can show the header it was read from.</summary>
        public FrameDefinition FirstFrame { get; }

        /// <summary>The skeleton the set names, or null when it could not be read.</summary>
        public SkeletonDefinition? Skeleton { get; }

        /// <summary>The index-1 group id the first frame names.</summary>
        /// <remarks>
        ///     Read off one frame rather than all of them. The field is per file, but all 3526 groups
        ///     in this cache name a single skeleton across every file they hold, and reading the group
        ///     to prove it per row would decode the whole index twice.
        /// </remarks>
        public int SkeletonId => FirstFrame.SkeletonId;

        /// <summary>The skeleton's bone count, or null when the skeleton is missing.</summary>
        /// <remarks>Null rather than 0 or -1, so a missing skeleton reads as an empty cell instead of a real count.</remarks>
        public object? BoneCount => Skeleton?.BoneCount;

        /// <summary>The skeleton's total label count, or null when the skeleton is missing.</summary>
        public object? LabelCount => Skeleton?.TotalLabelCount;

        /// <summary>The distinct transform types the skeleton uses, ascending.</summary>
        /// <remarks>
        ///     Effective rather than stored, because that is what decides how this set's frames read:
        ///     types 3 and 10 default a missing axis to 128, and 2 and 9 rescale into a 14-bit angle.
        /// </remarks>
        public string TransformTypes => Skeleton == null
            ? string.Empty
            : string.Join(", ", Skeleton.Bones.Select(bone => bone.EffectiveTransformType).Distinct().OrderBy(type => type));

        /// <summary>
        ///     The skeleton's transform types, one per bone, or null when the skeleton is missing.
        /// </summary>
        /// <remarks>
        ///     Built once and kept. <see cref="SkeletonDefinition.GetEffectiveTransformTypes"/>
        ///     allocates on every call and a set can hold 2792 frames, each of which needs the same
        ///     array to resolve.
        /// </remarks>
        public int[]? EffectiveTransformTypes =>
            Skeleton == null ? null : effectiveTransformTypes ??= Skeleton.GetEffectiveTransformTypes();
    }

    /// <summary>
    ///     Presents index 0 as one row per animation, with index 1 joined on.
    /// </summary>
    /// <remarks>
    ///     Read only. <c>FrameDefinition</c> encodes byte for byte, but nothing in this list is a
    ///     frame field - every column is either an address, a count taken from the reference table or
    ///     a property of the skeleton - so there is nothing here an edit could be written back from.
    ///     Frame editing belongs on the transform grid beside the list, and it must not offer to
    ///     change a slot count: a slot's position is its bone, so inserting one re-points every slot
    ///     after it.
    /// </remarks>
    public sealed class FrameSetListDescriptor : DefinitionListDescriptor<FrameSetListing> {
        private static readonly IReadOnlyList<DefinitionColumn> FrameSetColumns = new[] {
            DefinitionColumn.ReadOnly<FrameSetListing>("Set", row => row.SetId, 70),
            DefinitionColumn.ReadOnly<FrameSetListing>("Frames", row => row.FrameCount, 80),
            DefinitionColumn.ReadOnly<FrameSetListing>("Skeleton", row => row.SkeletonId, 90),
            DefinitionColumn.ReadOnly<FrameSetListing>("Bones", row => row.BoneCount, 70),
            DefinitionColumn.ReadOnly<FrameSetListing>("Labels", row => row.LabelCount, 80),
            DefinitionColumn.ReadOnly<FrameSetListing>("Transform types", row => row.TransformTypes, 170)
        };

        /* One decoded skeleton per id, so a sweep of 3526 sets costs at most the 3106 groups index 1
           holds rather than one read each. Keyed to the cache it was read from and dropped wholesale
           when that changes, because a reopened cache is a different set of bytes under the same ids. */
        private readonly object skeletonGate = new object();
        private readonly Dictionary<int, SkeletonDefinition?> skeletonsById = new Dictionary<int, SkeletonDefinition?>();
        private RSCache? skeletonSource;

        /// <inheritdoc/>
        public override int IndexId => RSConstants.FRAMES_INDEX;

        /// <inheritdoc/>
        public override string RowNoun => "frame set";

        /// <inheritdoc/>
        public override IReadOnlyList<DefinitionColumn> Columns => FrameSetColumns;

        /// <summary>
        ///     One address per group: the group's first file.
        /// </summary>
        /// <remarks>
        ///     Deliberately not the base <c>Enumerate</c>, which yields every file. That is right for
        ///     an index whose file is its record and wrong here, where the group is the record - it
        ///     would produce 359,931 rows for 3526 animations.
        ///     <para>
        ///     The first file id is read off the reference table rather than assumed to be 0. Frame
        ///     arrays are sparse by design, so the lowest id a group declares is not always zero.
        ///     </para>
        /// </remarks>
        /// <param name="cache">The open cache.</param>
        /// <returns>One address per non-empty group.</returns>
        public override IEnumerable<DefinitionAddress> Enumerate(RSCache cache) {
            if (cache == null)
                throw new ArgumentNullException(nameof(cache));

            foreach (int group in cache.EnumerateGroups(IndexId)) {
                int[] files = cache.GetFileIds(IndexId, group);
                if (files.Length > 0)
                    yield return Address(group, files[0]);
            }
        }

        /// <inheritdoc/>
        public override FrameSetListing Decode(RSCache cache, DefinitionAddress address, JagStream payload) {
            if (cache == null)
                throw new ArgumentNullException(nameof(cache));
            if (payload == null)
                throw new ArgumentNullException(nameof(payload));

            var frame = new FrameDefinition { Id = address.DefinitionId };
            frame.Decode(payload);

            return new FrameSetListing(address, cache.GetFileIds(IndexId, address.GroupId).Length, frame,
                ResolveSkeleton(cache, frame.SkeletonId));
        }

        /// <inheritdoc/>
        public override DefinitionAddress AddressOf(FrameSetListing row) {
            if (row == null)
                throw new ArgumentNullException(nameof(row));
            return row.Address;
        }

        /// <summary>
        ///     Reads a skeleton once and remembers it.
        /// </summary>
        /// <remarks>
        ///     A missing or unreadable skeleton is remembered as null rather than retried, so one bad
        ///     id costs one read and not one per frame set that names it. The lock is there because a
        ///     rebind cancels the previous load cooperatively, which leaves a window where two
        ///     workers are calling this.
        /// </remarks>
        /// <param name="cache">The open cache.</param>
        /// <param name="skeletonId">The index-1 group id.</param>
        /// <returns>The skeleton, or null when index 1 does not hold it.</returns>
        private SkeletonDefinition? ResolveSkeleton(RSCache cache, int skeletonId) {
            lock (skeletonGate) {
                if (!ReferenceEquals(cache, skeletonSource)) {
                    skeletonsById.Clear();
                    skeletonSource = cache;
                }

                if (skeletonsById.TryGetValue(skeletonId, out SkeletonDefinition? known))
                    return known;

                SkeletonDefinition? skeleton = null;
                try {
                    //The file id comes off the reference table. Index 1 is GroupPerId, whose file id
                    //is declared rather than derived - the client's own accessor takes file 0
                    //(JS5Archive.java:591-611) but only after asserting the group holds exactly one.
                    int[] files = cache.GetFileIds(RSConstants.SKINS, skeletonId);
                    if (files.Length > 0) {
                        byte[] stored = cache.ReadFileBytes(RSConstants.SKINS, skeletonId, files[0]);
                        skeleton = new SkeletonDefinition { Id = skeletonId }.Decode(new JagStream(stored));
                    }
                } catch (Exception ex) {
                    Debug("Skeleton " + skeletonId + " could not be read: " + ex.Message);
                }

                skeletonsById[skeletonId] = skeleton;
                return skeleton;
            }
        }
    }
}
