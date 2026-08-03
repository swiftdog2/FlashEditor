using System;

namespace FlashEditor.cache {
    /// <summary>
    ///     How an index relates a definition id to the (group, file) pair that stores it.
    /// </summary>
    /// <remarks>
    ///     Four shapes occur in this cache and they are not interchangeable. Keeping them as
    ///     distinct cases rather than folding the non-paged ones into a page size of 1 is
    ///     deliberate: a caller that asks index 5 for its page size has asked a question with no
    ///     answer, and any number handed back would be acted on.
    /// </remarks>
    public enum CacheIdShape {
        /// <summary>
        ///     The definition id splits into a group and a file by a power-of-two page size,
        ///     <c>group = id &gt;&gt; FileBits</c> and <c>file = id &amp; FileMask</c>.
        /// </summary>
        Paged,

        /// <summary>
        ///     Every group holds exactly one file, and the definition id is the group id. The file
        ///     id is declared by the reference table rather than derived, so it cannot be computed.
        /// </summary>
        GroupPerId,

        /// <summary>
        ///     The whole index is a single group and the definition id is the file id within it.
        /// </summary>
        SingleGroup,

        /// <summary>
        ///     Groups are found by hashing a name, and no arithmetic relates an id to a group.
        /// </summary>
        NameHashed
    }

    /// <summary>
    ///     The group/file addressing scheme for one cache index, and the split and join that go
    ///     with it.
    /// </summary>
    /// <remarks>
    ///     This type exists so an index's split is stated once. Before it, <c>&gt;&gt;8 / &amp;0xFF</c>,
    ///     <c>&gt;&gt;7 / &amp;0x7F</c> and <c>&gt;&gt;10 / &amp;0x3FF</c> were open-coded at every call
    ///     site, and one loader derived the page size from the first group's file count instead -
    ///     which is right only while group 0 happens to be full, and 64 of index 16's 224 groups
    ///     are not.
    ///     <para>
    ///     Every index whose shape is settled by the 637 client or by a measurement over the 639
    ///     data has a row in <see cref="TryGetFor"/>, each citing its evidence. An index with no
    ///     row is unknown rather than assumed, and <see cref="For"/> throws for it, so a new index
    ///     editor has to establish its addressing and record it here instead of inheriting a guess.
    ///     </para>
    /// </remarks>
    public readonly struct CacheAddressing {
        /// <summary>The addressing shape this index uses.</summary>
        public CacheIdShape Shape { get; }

        private readonly int _fileBits;
        private readonly int _singleGroupId;

        private CacheAddressing(CacheIdShape shape, int fileBits, int singleGroupId) {
            Shape = shape;
            _fileBits = fileBits;
            _singleGroupId = singleGroupId;
        }

        /// <summary>Whether the definition id splits arithmetically into a group and a file.</summary>
        public bool IsPaged => Shape == CacheIdShape.Paged;

        /// <summary>
        ///     How many low bits of a definition id are the file id.
        /// </summary>
        /// <exception cref="InvalidOperationException">The index is not paged.</exception>
        public int FileBits => IsPaged ? _fileBits : throw NotPaged(nameof(FileBits));

        /// <summary>
        ///     The id page size: how many consecutive definition ids one group can address.
        /// </summary>
        /// <remarks>
        ///     This is an addressing span, <b>not</b> a file count. Groups are routinely short and
        ///     routinely have holes in the middle of their id range, so a
        ///     <c>for (file = 0; file &lt; FilesPerGroup; file++)</c> loop asks for files that do
        ///     not exist. Enumerate with <c>RSCache.EnumerateFiles</c> instead.
        /// </remarks>
        /// <exception cref="InvalidOperationException">The index is not paged.</exception>
        public int FilesPerGroup => IsPaged ? 1 << _fileBits : throw NotPaged(nameof(FilesPerGroup));

        /// <summary>
        ///     The mask that isolates the file id from a definition id.
        /// </summary>
        /// <exception cref="InvalidOperationException">The index is not paged.</exception>
        public int FileMask => IsPaged ? (1 << _fileBits) - 1 : throw NotPaged(nameof(FileMask));

        /// <summary>
        ///     The id of the one group the index holds.
        /// </summary>
        /// <remarks>
        ///     Not always zero. Index 10's single group is id <b>1</b>, and its idx slot 0 is a
        ///     dead record, so <c>ReadFile(HUFFMAN_INDEX, 0, 0)</c> finds nothing.
        /// </remarks>
        /// <exception cref="InvalidOperationException">The index is not a single-group index.</exception>
        public int SingleGroupId => Shape == CacheIdShape.SingleGroup
            ? _singleGroupId
            : throw new InvalidOperationException(
                "SingleGroupId is only defined for a single-group index; this one is " + Shape + ".");

        /// <summary>
        ///     A paged index whose low <paramref name="fileBits"/> bits of a definition id are the
        ///     file id.
        /// </summary>
        /// <param name="fileBits">Bit width of the file id, 1..30.</param>
        /// <returns>The addressing scheme.</returns>
        public static CacheAddressing Paged(int fileBits) {
            if (fileBits < 1 || fileBits > 30)
                throw new ArgumentOutOfRangeException(nameof(fileBits), fileBits,
                    "A page size has to be expressible as a positive 32-bit shift.");
            return new CacheAddressing(CacheIdShape.Paged, fileBits, 0);
        }

        /// <summary>An index whose groups hold one file each, the definition id being the group id.</summary>
        public static readonly CacheAddressing GroupPerId =
            new CacheAddressing(CacheIdShape.GroupPerId, 0, 0);

        /// <summary>
        ///     An index that is one group, the definition id being the file id within it.
        /// </summary>
        /// <param name="groupId">The id of that group, which is not necessarily zero.</param>
        /// <returns>The addressing scheme.</returns>
        public static CacheAddressing SingleGroup(int groupId) {
            if (groupId < 0)
                throw new ArgumentOutOfRangeException(nameof(groupId), groupId, "Group ids are non-negative.");
            return new CacheAddressing(CacheIdShape.SingleGroup, 0, groupId);
        }

        /// <summary>An index addressed by hashing group names, with no id arithmetic at all.</summary>
        public static readonly CacheAddressing NameHashed =
            new CacheAddressing(CacheIdShape.NameHashed, 0, 0);

        /// <summary>
        ///     The group that holds a definition.
        /// </summary>
        /// <param name="definitionId">The definition id.</param>
        /// <returns>The group id.</returns>
        /// <exception cref="InvalidOperationException">The index is name-hashed, so no group follows from an id.</exception>
        public int GroupOf(int definitionId) {
            if (definitionId < 0)
                throw new ArgumentOutOfRangeException(nameof(definitionId), definitionId, "Definition ids are non-negative.");

            return Shape switch {
                CacheIdShape.Paged => definitionId >> _fileBits,
                CacheIdShape.GroupPerId => definitionId,
                CacheIdShape.SingleGroup => _singleGroupId,
                _ => throw NotDerivable(nameof(GroupOf))
            };
        }

        /// <summary>
        ///     The file within its group that holds a definition.
        /// </summary>
        /// <remarks>
        ///     Undefined for <see cref="CacheIdShape.GroupPerId"/>: the client fetches those with
        ///     whatever single file id the reference table declares, and it is not always 0 - index
        ///     23's <c>area</c> file is id 4 in 32 groups and id 0 in the other seven. Read it off
        ///     the entry rather than assuming it.
        /// </remarks>
        /// <param name="definitionId">The definition id.</param>
        /// <returns>The file id.</returns>
        /// <exception cref="InvalidOperationException">The file id is not derivable from the id on this index.</exception>
        public int FileOf(int definitionId) {
            if (definitionId < 0)
                throw new ArgumentOutOfRangeException(nameof(definitionId), definitionId, "Definition ids are non-negative.");

            return Shape switch {
                CacheIdShape.Paged => definitionId & ((1 << _fileBits) - 1),
                CacheIdShape.SingleGroup => definitionId,
                _ => throw NotDerivable(nameof(FileOf))
            };
        }

        /// <summary>
        ///     The definition id a (group, file) pair carries - the inverse of
        ///     <see cref="GroupOf"/> and <see cref="FileOf"/>, which every write path needs.
        /// </summary>
        /// <param name="groupId">The group id.</param>
        /// <param name="fileId">The file id within that group.</param>
        /// <returns>The definition id.</returns>
        /// <exception cref="InvalidOperationException">The index is name-hashed, so no id follows from a pair.</exception>
        public int DefinitionId(int groupId, int fileId) {
            if (groupId < 0)
                throw new ArgumentOutOfRangeException(nameof(groupId), groupId, "Group ids are non-negative.");
            if (fileId < 0)
                throw new ArgumentOutOfRangeException(nameof(fileId), fileId, "File ids are non-negative.");

            switch (Shape) {
                case CacheIdShape.Paged:
                    if (fileId > (1 << _fileBits) - 1)
                        throw new ArgumentOutOfRangeException(nameof(fileId), fileId,
                            "File id does not fit a " + (1 << _fileBits) + "-slot page, so the join would " +
                            "carry into the group id and name a different definition.");
                    return (groupId << _fileBits) | fileId;

                case CacheIdShape.GroupPerId:
                    return groupId;

                case CacheIdShape.SingleGroup:
                    if (groupId != _singleGroupId)
                        throw new ArgumentOutOfRangeException(nameof(groupId), groupId,
                            "This index holds a single group, id " + _singleGroupId + ".");
                    return fileId;

                default:
                    throw NotDerivable(nameof(DefinitionId));
            }
        }

        /// <summary>
        ///     The addressing scheme for an index, or false when this cache has not established one.
        /// </summary>
        /// <remarks>
        ///     Every row below is settled by what the 637 client does with the index, or by a
        ///     measurement over the 639 data where the client cannot answer. An index missing from
        ///     the switch is genuinely unestablished, not accidentally omitted - adding a row is
        ///     part of building that index's editor.
        /// </remarks>
        /// <param name="indexId">The cache index id.</param>
        /// <param name="addressing">The scheme, when one is known.</param>
        /// <returns>Whether the index's addressing is established.</returns>
        public static bool TryGetFor(int indexId, out CacheAddressing addressing) {
            switch (indexId) {
                //256 ids to a group. Objects: Class302.method3546 splits with za.java:19 (i >>> 8)
                //and Class151.java:27 (i & 0xff). Items: Class205.java:216-217 with Class150.java:31
                //and Class119_Sub3.java:75. Spot anims: Class304.java:118-146 with Class329.java:39
                //and Class314.java:11. Enums: Class29.java:237-238 with Class153.java:181 and
                //Node_Sub10_Sub9.java:15.
                case RSConstants.OBJECTS_DEFINITIONS_INDEX:
                case RSConstants.CLIENTSCRIPT_SETTINGS:
                case RSConstants.ITEM_DEFINITIONS_INDEX:
                case RSConstants.GRAPHICS_INDEX:
                    addressing = Paged(8);
                    return true;

                //128 ids to a group. NPCs: Class301.java:207-208 shifts by 7 and masks with
                //Class163.java:143 (i & 0x7f). Animations use the same split.
                case RSConstants.NPC_DEFINITIONS_INDEX:
                case RSConstants.ANIMATIONS_INDEX:
                    addressing = Paged(7);
                    return true;

                //65,536 frames to a group. Index 20 stores a packed frame id and Class97.java:130-131
                //splits it: method2624(2, i_1_ >> 16) picks the frame set and i_1_ &= 0xffff the frame
                //within it. Index 0 has no name hashes, so that packed id is the only way in.
                //
                //Index 3 folds the same way and the client states both halves: EntityEnumType.java:46
                //builds ID_TAG = (parent << 16) + childIndex, and Class247.java:412-413 takes it apart
                //again as child = stack >> 16 with sub_child = stack & 0xFFFF. The fold is load bearing
                //rather than cosmetic - RSInterface.unpackConfig:1057-1063 reconstructs a component's
                //parent as parentID + (ID_TAG & ~0xffff), so the page size is what says a stored
                //parent id names a sibling of the same interface.
                case RSConstants.FRAMES_INDEX:
                case RSConstants.INTERFACE_DEFINITIONS_INDEX:
                    addressing = Paged(16);
                    return true;

                //1024 varbits to a group: Class198.java:92-93 fetches with Class234.java:31
                //(id >>> 10) and Class32.java:61 (id & 0x3ff).
                case RSConstants.SCRIPT_CONFIGS:
                    addressing = Paged(10);
                    return true;

                //Name-hashed. Index 5 is reached by hashing "m50_50"/"l50_50" - the shipped
                //map_index.dat mechanism is dead code and disagrees with this cache. Index 23's
                //details group is id 1 rather than 0 and must be resolved by hash. Index 30 builds
                //"<os>/<arch>/<lib><ext>" and hashes it. Index 31's two groups are "gl" (3301) and
                //"dx" (3220), at ids 1 and 3.
                case RSConstants.MAPS_INDEX:
                case RSConstants.WORLD_MAP:
                case RSConstants.NATIVE_LIBRARIES:
                case RSConstants.GRAPHICS_SHADERS:
                    addressing = NameHashed;
                    return true;

                //One group holding the whole index. Class260.java:106 reads index 26 as
                //getChildFromFolder(0, 0).
                case RSConstants.MATERIALS:
                    addressing = SingleGroup(0);
                    return true;

                //Index 10's single group is id 1, not 0: the table's delta-decoded group id is 1
                //and idx10 slot 0 is a dead record (length 0xFF0000, sector 0). The client does not
                //use the id at all - InterfaceSettings.java:310 asks for "huffman" - so treat the 1
                //as this cache's layout rather than as a stable address.
                case RSConstants.HUFFMAN_INDEX:
                    addressing = SingleGroup(1);
                    return true;

                //One file per group, the group id being the definition id. Measured in this cache:
                //index 4 and index 12 declare exactly one file per group, and index 6/11 groups
                //hold exactly one file each (the id of which is read off the reference table, not
                //assumed to be 0 - see FileOf).
                //
                //Index 1 is the case the client states outright: JS5Archive.method2733
                //(JS5Archive.java:591-611) returns getChildFromFolder(id, 0) when the group holds
                //exactly one file and throws otherwise, and Node_Sub46_Sub16.java:161 is the only
                //caller. All 3106 groups in this cache are single-file.
                case RSConstants.SKINS:
                case RSConstants.SOUND_EFFECTS:
                case RSConstants.MUSIC_INDEX:
                case RSConstants.MUSIC_2:
                case RSConstants.CLIENT_SCRIPTS_INDEX:
                    addressing = GroupPerId;
                    return true;

                default:
                    addressing = default;
                    return false;
            }
        }

        /// <summary>
        ///     The addressing scheme for an index.
        /// </summary>
        /// <param name="indexId">The cache index id.</param>
        /// <returns>The scheme.</returns>
        /// <exception cref="ArgumentOutOfRangeException">
        ///     The index's addressing has not been established here. Establish it from the client
        ///     or from the data and add a row to <see cref="TryGetFor"/> rather than guessing at
        ///     the call site.
        /// </exception>
        public static CacheAddressing For(int indexId) {
            if (TryGetFor(indexId, out CacheAddressing addressing))
                return addressing;

            throw new ArgumentOutOfRangeException(nameof(indexId), indexId,
                "No addressing scheme is recorded for index " + indexId +
                ". Settle its group/file split against the 637 client or the 639 data and add a row" +
                " to CacheAddressing.TryGetFor.");
        }

        /// <summary>A short description of the scheme, for logs and error messages.</summary>
        /// <returns>The scheme in words.</returns>
        public override string ToString() {
            return Shape switch {
                CacheIdShape.Paged => "Paged(" + (1 << _fileBits) + " ids per group)",
                CacheIdShape.GroupPerId => "GroupPerId",
                CacheIdShape.SingleGroup => "SingleGroup(" + _singleGroupId + ")",
                _ => "NameHashed"
            };
        }

        private InvalidOperationException NotPaged(string member) {
            return new InvalidOperationException(
                member + " is only defined for a paged index; this one is " + Shape +
                ". It throws rather than answering 0, because a page size of 0 turns a bounded loop" +
                " into a silent no-op.");
        }

        private InvalidOperationException NotDerivable(string member) {
            return new InvalidOperationException(
                member + " is not derivable on a " + Shape + " index. Resolve the group by name hash" +
                " (RSReferenceTable.GetArchiveId) or read the file id off the reference table entry.");
        }
    }
}
