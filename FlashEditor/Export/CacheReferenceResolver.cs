using System;
using System.Collections.Generic;
using System.IO;
using FlashEditor.Cache;
using FlashEditor.Definitions.Config;
using FlashEditor.IO;
using static FlashEditor.Utils.DebugUtil;

namespace FlashEditor.Export {
    /// <summary>
    ///     Turns a stored id into what it addresses, for the joins this project has measured.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///     <b>The measured list is the ceiling.</b> Every join this resolves is one the work list
    ///     records as measured and resolving in this cache; nothing here invents a relation. That
    ///     restraint is not caution for its own sake - the world map icon join was first "confirmed"
    ///     by two self-proving rows and a shift sweep too narrow to falsify itself, and it was wrong.
    ///     Coverage is not correctness, and a plausible mapping is the easiest thing in this cache to
    ///     confirm by accident.
    ///     </para>
    ///     <para>
    ///     Existence is answered from the reference table rather than by reading the target, because
    ///     the client resolves every read through the table: a group the table does not declare is
    ///     unreachable in game whatever bytes sit in the idx file. It is also the only answer cheap
    ///     enough to give for every id in the cache.
    ///     </para>
    ///     <para>
    ///     A detail line is only produced for the handful of small index 2 groups that are themselves
    ///     join targets. Those are read once and cached; everything else resolves to existence and an
    ///     address, because decoding a target per reference would decode the same group thousands of
    ///     times.
    ///     </para>
    /// </remarks>
    public sealed class CacheReferenceResolver {
        /// <summary>
        ///     Index 2 groups whose records are decoded so a reference into them can say what it hit.
        /// </summary>
        /// <remarks>
        ///     All six are join targets and all six are small. Adding a large group here would cost a
        ///     full group decode on the first reference into it and nothing else - the cost is in the
        ///     size of the group, not in how often it is resolved.
        /// </remarks>
        private static readonly int[] DetailedConfigGroups = {
            ConfigGroup.FloorUnderlay, ConfigGroup.FloorOverlay, ConfigGroup.ParameterType,
            ConfigGroup.Quest, ConfigGroup.MapSceneIcon, ConfigGroup.MapElement
        };

        private readonly RSCache cache;

        /// <summary>Declared file ids per group, per index, read once from each reference table.</summary>
        private readonly Dictionary<int, Dictionary<int, int[]>> declared =
            new Dictionary<int, Dictionary<int, int[]>>();

        /// <summary>Group name hashes per index, for the indexes whose table carries identifiers.</summary>
        private readonly Dictionary<int, Dictionary<int, int>> identifiers =
            new Dictionary<int, Dictionary<int, int>>();

        /// <summary>One-line descriptions of the records in <see cref="DetailedConfigGroups"/>.</summary>
        private readonly Dictionary<int, Dictionary<int, string>> configDetails =
            new Dictionary<int, Dictionary<int, string>>();

        /// <summary>Builds a resolver over an open cache.</summary>
        /// <param name="cache">The open cache.</param>
        public CacheReferenceResolver(RSCache cache) {
            this.cache = cache ?? throw new ArgumentNullException(nameof(cache));
        }

        /// <summary>
        ///     Resolves an id that addresses a whole group of an index.
        /// </summary>
        /// <param name="field">The field on the record that holds the id.</param>
        /// <param name="join">The join this comes from, named as the work list names it.</param>
        /// <param name="targetIndex">The index the id addresses.</param>
        /// <param name="id">The id as stored.</param>
        /// <returns>The resolution, or null when the id stores "nothing" rather than an id.</returns>
        public ExportedReference? Group(string field, string join, int targetIndex, int id) {
            if (id < 0)
                return null;

            return Build(field, join, id, targetIndex, id, FirstFileOf(targetIndex, id));
        }

        /// <summary>
        ///     Resolves a definition id on a paged index, splitting it into its group and file.
        /// </summary>
        /// <remarks>
        ///     The split comes from <see cref="CacheAddressing"/>, which throws rather than guessing
        ///     for an index whose shape is unrecorded. An unrecorded index falls back to treating the
        ///     id as a group, which is what every one-file-per-group index does anyway.
        /// </remarks>
        /// <param name="field">The field on the record that holds the id.</param>
        /// <param name="join">The join this comes from.</param>
        /// <param name="targetIndex">The index the id addresses.</param>
        /// <param name="id">The definition id as stored.</param>
        /// <returns>The resolution, or null when the id stores "nothing" rather than an id.</returns>
        public ExportedReference? Definition(string field, string join, int targetIndex, int id) {
            if (id < 0)
                return null;

            if (!CacheAddressing.TryGetFor(targetIndex, out CacheAddressing addressing)
                || addressing.Shape == CacheIdShape.NameHashed)
                return Group(field, join, targetIndex, id);

            return Build(field, join, id, targetIndex, addressing.GroupOf(id), addressing.FileOf(id));
        }

        /// <summary>
        ///     Resolves an id that addresses a file of one index 2 config group.
        /// </summary>
        /// <remarks>
        ///     Index 2 has no id arithmetic of its own - it is thirty-five unrelated families sharing
        ///     one index - so the group is stated by the caller and the id is the file within it.
        /// </remarks>
        /// <param name="field">The field on the record that holds the id.</param>
        /// <param name="join">The join this comes from.</param>
        /// <param name="configGroup">The group within index 2.</param>
        /// <param name="id">The file id within that group.</param>
        /// <returns>The resolution, or null when the id stores "nothing" rather than an id.</returns>
        public ExportedReference? Config(string field, string join, int configGroup, int id) {
            if (id < 0)
                return null;

            return Build(field, join, id, RSConstants.CONFIG, configGroup, id);
        }

        /// <summary>Assembles one resolution, answering existence and detail from the cheap sources.</summary>
        /// <param name="field">The field on the record that holds the id.</param>
        /// <param name="join">The join this comes from.</param>
        /// <param name="id">The id as stored.</param>
        /// <param name="targetIndex">The index it addresses.</param>
        /// <param name="group">The group within that index.</param>
        /// <param name="file">The file within that group.</param>
        /// <returns>The resolution.</returns>
        private ExportedReference Build(string field, string join, int id, int targetIndex, int group, int file) {
            Dictionary<int, int[]> index = DeclaredIn(targetIndex);
            bool exists = index.TryGetValue(group, out int[]? files) && Array.BinarySearch(files, file) >= 0;

            string? detail = targetIndex == RSConstants.CONFIG ? ConfigDetail(group, file) : null;

            int? identifier = null;
            if (IdentifiersIn(targetIndex).TryGetValue(group, out int hash) && hash != -1)
                identifier = hash;

            return new ExportedReference(field, join, id, targetIndex, group, file, exists, detail, identifier);
        }

        /// <summary>
        ///     The lowest file id a group declares, for an index whose ids address whole groups.
        /// </summary>
        /// <remarks>
        ///     Zero for every index in this cache bar the sparse ones, and taken from the table rather
        ///     than assumed - a group whose only file is not file 0 would otherwise be reported as
        ///     absent by an existence check against file 0.
        /// </remarks>
        /// <param name="indexId">The index.</param>
        /// <param name="groupId">The group.</param>
        /// <returns>The lowest declared file id, or 0 when the group declares none.</returns>
        private int FirstFileOf(int indexId, int groupId) {
            return DeclaredIn(indexId).TryGetValue(groupId, out int[]? files) && files.Length > 0 ? files[0] : 0;
        }

        /// <summary>The declared file ids of every group of an index, read once.</summary>
        /// <param name="indexId">The index.</param>
        /// <returns>Group id to its ascending declared file ids, empty when the index has no table.</returns>
        private Dictionary<int, int[]> DeclaredIn(int indexId) {
            if (declared.TryGetValue(indexId, out Dictionary<int, int[]>? cached))
                return cached;

            var map = new Dictionary<int, int[]>();
            var hashes = new Dictionary<int, int>();

            RSReferenceTable? table = TableOf(indexId);
            if (table != null) {
                foreach (KeyValuePair<int, RSArchiveEntry> entry in table.GetArchiveEntries()) {
                    try {
                        map[entry.Key] = entry.Value.GetValidFileIds();
                    } catch (InvalidOperationException) {
                        //A group whose file ids were never set declares nothing this can check
                        //against. Recording it as empty keeps existence answerable rather than
                        //throwing part way through an export.
                        map[entry.Key] = Array.Empty<int>();
                    }

                    if (table.hasIdentifiers)
                        hashes[entry.Key] = entry.Value.GetIdentifier();
                }
            }

            declared[indexId] = map;
            identifiers[indexId] = hashes;
            return map;
        }

        /// <summary>The group name hashes of an index, empty when its table carries none.</summary>
        /// <param name="indexId">The index.</param>
        /// <returns>Group id to name hash.</returns>
        private Dictionary<int, int> IdentifiersIn(int indexId) {
            DeclaredIn(indexId);
            return identifiers[indexId];
        }

        /// <summary>An index's reference table, or null when it has none.</summary>
        /// <param name="indexId">The index.</param>
        /// <returns>The table, or null.</returns>
        private RSReferenceTable? TableOf(int indexId) {
            try {
                return cache.GetReferenceTable(indexId);
            } catch (FileNotFoundException) {
                //Indexes 34 and 35 have no idx255 record at all here, and an id outside the store
                //looks the same. Both mean "nothing to resolve against".
                return null;
            }
        }

        /// <summary>
        ///     What one record of a detailed index 2 group holds, in a line.
        /// </summary>
        /// <param name="groupId">The group within index 2.</param>
        /// <param name="fileId">The file within that group.</param>
        /// <returns>The description, or null when the group is not one this resolver reads.</returns>
        private string? ConfigDetail(int groupId, int fileId) {
            if (Array.IndexOf(DetailedConfigGroups, groupId) < 0)
                return null;

            if (!configDetails.TryGetValue(groupId, out Dictionary<int, string>? group)) {
                group = ReadConfigGroup(groupId);
                configDetails[groupId] = group;
            }

            return group.TryGetValue(fileId, out string? detail) ? detail : null;
        }

        /// <summary>
        ///     Decodes one small index 2 group into its per-record summaries.
        /// </summary>
        /// <remarks>
        ///     Through <see cref="RSCache.ReadGroup"/>, which decodes the group once. Reading the same
        ///     records file by file would re-inflate and re-decode the whole group per file.
        /// </remarks>
        /// <param name="groupId">The group within index 2.</param>
        /// <returns>File id to summary.</returns>
        private Dictionary<int, string> ReadConfigGroup(int groupId) {
            var summaries = new Dictionary<int, string>();

            ConfigFamily family = ConfigFamily.For(groupId);

            IReadOnlyDictionary<int, JagStream> files;
            try {
                files = cache.ReadGroup(RSConstants.CONFIG, groupId);
            } catch (Exception ex) {
                //A group this cache does not carry costs the detail column and nothing else. The
                //reference still resolves, with existence taken from the table.
                Debug("Could not read config group " + groupId + " for reference detail: " + ex.Message,
                    LOG_DETAIL.ADVANCED);
                return summaries;
            }

            foreach (KeyValuePair<int, JagStream> file in files) {
                try {
                    summaries[file.Key] = family.Read(file.Key, file.Value).Summary;
                } catch (Exception ex) {
                    //One record that will not decode must not cost the other two hundred.
                    Debug("Could not summarise config " + groupId + "/" + file.Key + ": " + ex.Message,
                        LOG_DETAIL.ADVANCED);
                }
            }

            return summaries;
        }
    }
}
