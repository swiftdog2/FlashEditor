using System;
using System.Collections.Generic;
using FlashEditor.Cache.Util;

namespace FlashEditor.Cache {
    /// <summary>
    ///     Resolves the names a reference table carries back to group and file ids, at both levels.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///     <b>The file level did not exist before this.</b>
    ///     <see cref="ReferenceTableCodec"/> has always decoded and re-encoded the per-file
    ///     identifier block, and <see cref="RSFileEntry"/> has always carried the hash, but nothing
    ///     ever indexed it - so <c>"gl"/"transparent_water"</c>, which is exactly how the client
    ///     addresses an index-31 shader (<c>JS5Archive.method2739</c> lower-cases and hashes both
    ///     halves), could not be resolved at all. Only the group level was reachable, through
    ///     <see cref="RSReferenceTable.GetArchiveId"/>.
    ///     </para>
    ///     <para>
    ///     <b>It refuses rather than pretends when the table carries no names.</b> Index 2 sets no
    ///     identifiers flag, so a config group there is addressable only by id. Every lookup here
    ///     returns -1 for such a table, and <see cref="NameLookupRefusal"/> says why in words a
    ///     caller can put on screen - the alternative, an empty map that answers -1 without
    ///     explanation, reads identically to a name that simply is not present and would send
    ///     someone hunting for a group that was never named.
    ///     </para>
    ///     <para>
    ///     A <see cref="Dictionary{TKey,TValue}"/> rather than the client's own open-addressed
    ///     <see cref="CheckSum.RSIdentifiers"/> table. That one is indexed by array position, so
    ///     using it at the file level would mean building an array sized to the highest file id and
    ///     leaving every undeclared slot holding 0 - and 0 is <c>hash("")</c>, a name index 30
    ///     genuinely uses for every one of its files. A lookup of the empty string would then land
    ///     on padding. Keying the declared entries directly cannot do that.
    ///     </para>
    /// </remarks>
    public sealed class CacheNameIndex {
        /// <summary>
        ///     The identifier value the format spells "this entry has no name".
        /// </summary>
        /// <remarks>
        ///     It is also the value <see cref="RSArchiveEntry.identifier"/> initialises to, so an
        ///     entry from a table with no identifiers block is indistinguishable from one the block
        ///     marks unnamed - which is correct, because neither can be addressed by name. Index 3
        ///     carries identifiers and still leaves entries at -1, so the two cases really do both
        ///     occur.
        /// </remarks>
        public const int Unnamed = -1;

        private readonly Dictionary<int, int> groupsByHash;
        private readonly Dictionary<int, Dictionary<int, int>> filesByHash;

        private CacheNameIndex(int indexId, bool carriesNames, string? refusal,
            Dictionary<int, int> groupsByHash, Dictionary<int, Dictionary<int, int>> filesByHash) {
            IndexId = indexId;
            CarriesNames = carriesNames;
            NameLookupRefusal = refusal;
            this.groupsByHash = groupsByHash;
            this.filesByHash = filesByHash;
        }

        /// <summary>The index this was built for, for messages that have to name it.</summary>
        public int IndexId { get; }

        /// <summary>Whether the table carries name hashes at all.</summary>
        public bool CarriesNames { get; }

        /// <summary>
        ///     Why a name lookup on this index can never succeed, or <c>null</c> when it can.
        /// </summary>
        /// <remarks>
        ///     Non-null only when the table sets no identifiers flag. A table that does set it but
        ///     simply has no entry of the requested name returns -1 with no refusal, because that is
        ///     a lookup that failed rather than a lookup that was never possible.
        /// </remarks>
        public string? NameLookupRefusal { get; }

        /// <summary>Builds the lookup for one decoded table.</summary>
        /// <param name="table">The decoded reference table.</param>
        /// <returns>The lookup.</returns>
        public static CacheNameIndex Build(RSReferenceTable table) {
            if (table == null)
                throw new ArgumentNullException(nameof(table));

            var groups = new Dictionary<int, int>();
            var files = new Dictionary<int, Dictionary<int, int>>();

            if (!table.hasIdentifiers) {
                return new CacheNameIndex(table.indexId, false,
                    "The reference table for index " + table.indexId + " sets no identifiers flag, so it" +
                    " carries no name hashes at all - its groups and files are addressable only by id.",
                    groups, files);
            }

            foreach (KeyValuePair<int, RSArchiveEntry> group in table.GetArchiveEntries()) {
                //First id wins on a collision, and later ones are dropped rather than overwriting.
                //Two entries hashing alike is the client's own ambiguity - its open-addressed table
                //probes to a free slot and returns whichever it reaches first - so there is no
                //correct answer to prefer, and silently taking the last would make the resolved id
                //depend on iteration order.
                if (group.Value.GetIdentifier() != Unnamed)
                    groups.TryAdd(group.Value.GetIdentifier(), group.Key);

                Dictionary<int, int>? named = null;
                foreach (KeyValuePair<int, RSFileEntry> file in group.Value.GetFileEntries()) {
                    if (file.Value.GetIdentifier() == Unnamed)
                        continue;

                    named ??= new Dictionary<int, int>();
                    named.TryAdd(file.Value.GetIdentifier(), file.Key);
                }

                //Only for a group that actually named something, so the common case of a table whose
                //file identifiers are all -1 costs one dictionary rather than one per group.
                if (named != null)
                    files[group.Key] = named;
            }

            return new CacheNameIndex(table.indexId, true, null, groups, files);
        }

        /// <summary>
        ///     The group of a given name, or -1.
        /// </summary>
        /// <param name="name">The group name, case-insensitive as the client hashes it.</param>
        /// <returns>The group id, or -1 when nothing carries that name.</returns>
        public int GroupId(string name) {
            if (name == null)
                throw new ArgumentNullException(nameof(name));
            return GroupIdOfHash(NameHasher.GetNameHash(name));
        }

        /// <summary>The group whose stored identifier is <paramref name="nameHash"/>, or -1.</summary>
        /// <param name="nameHash">The stored 32-bit name hash.</param>
        /// <returns>The group id, or -1.</returns>
        public int GroupIdOfHash(int nameHash) {
            return groupsByHash.TryGetValue(nameHash, out int id) ? id : -1;
        }

        /// <summary>
        ///     The file of a given name inside a group, or -1.
        /// </summary>
        /// <remarks>
        ///     The empty string is a real name here rather than a missing one: every index-30 group
        ///     holds a single file called <c>""</c>, and the client asks for it that way
        ///     (<c>Class35.java:102</c> passes <c>""</c> explicitly). So this must not treat an empty
        ///     name as "no name given".
        /// </remarks>
        /// <param name="groupId">The group to look inside.</param>
        /// <param name="name">The file name, case-insensitive.</param>
        /// <returns>The file id, or -1.</returns>
        public int FileId(int groupId, string name) {
            if (name == null)
                throw new ArgumentNullException(nameof(name));
            return FileIdOfHash(groupId, NameHasher.GetNameHash(name));
        }

        /// <summary>The file in a group whose stored identifier is <paramref name="nameHash"/>, or -1.</summary>
        /// <param name="groupId">The group to look inside.</param>
        /// <param name="nameHash">The stored 32-bit name hash.</param>
        /// <returns>The file id, or -1.</returns>
        public int FileIdOfHash(int groupId, int nameHash) {
            return filesByHash.TryGetValue(groupId, out Dictionary<int, int>? named) &&
                   named.TryGetValue(nameHash, out int id)
                ? id
                : -1;
        }

        /// <summary>
        ///     Resolves the two-level address the client uses, <c>group/file</c>.
        /// </summary>
        /// <param name="groupName">The group name.</param>
        /// <param name="fileName">The file name, which is the empty string for a single-file group like index 30's.</param>
        /// <param name="groupId">The resolved group id, or -1.</param>
        /// <param name="fileId">The resolved file id, or -1.</param>
        /// <returns>Whether both halves resolved.</returns>
        public bool TryResolve(string groupName, string fileName, out int groupId, out int fileId) {
            groupId = GroupId(groupName);
            fileId = groupId < 0 ? -1 : FileId(groupId, fileName);
            return groupId >= 0 && fileId >= 0;
        }

        /// <summary>How many groups this index can address by name.</summary>
        public int NamedGroupCount => groupsByHash.Count;

        /// <summary>
        ///     How many files this index can address by name, over every group.
        /// </summary>
        /// <remarks>
        ///     Counted rather than stated. Both index-31 groups carry the same seven file hashes, so
        ///     this is 14 there and not 7 - the name is unique within a group and not across the
        ///     index, which is why the file lookup is nested under a group id rather than flat.
        /// </remarks>
        public int NamedFileCount {
            get {
                int total = 0;
                foreach (KeyValuePair<int, Dictionary<int, int>> group in filesByHash)
                    total += group.Value.Count;
                return total;
            }
        }
    }
}
