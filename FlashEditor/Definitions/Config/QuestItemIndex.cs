using System;
using System.Collections.Generic;
using FlashEditor.Cache;
using FlashEditor.Definitions.Editing;
using FlashEditor.Definitions.Entities;
using FlashEditor.IO;
using static FlashEditor.Utils.DebugUtil;

namespace FlashEditor.Definitions.Config {
    /// <summary>
    ///     Which item definitions name each quest, read the only way it can be: by asking every item.
    /// </summary>
    /// <remarks>
    ///     <b>The join runs one way on disk.</b> Item opcode 132 stores a list of index-2 group-35
    ///     file ids (<c>ItemDefinition.quests</c>), and a quest record stores nothing pointing back -
    ///     so "which items require this quest" cannot be answered from the quest. It is the forward
    ///     relation inverted, and inverting it means decoding index 19.
    ///     <para>
    ///     <b>Built once and by group, not by file.</b> <c>RSCache.ReadFile</c> releases the
    ///     container as soon as it has handed back one file, so a per-file walk re-inflates each
    ///     group once per item it holds - 20,427 group decodes against 80 for the same bytes in the
    ///     vanilla capture. The counts here are read from the reference table rather than written
    ///     down, because the two caches disagree on index 19.
    ///     </para>
    ///     <para>
    ///     <b>The relation is the export's, not a second one.</b> It is
    ///     <c>"item opcode 132 -> config group 35"</c>, the same string
    ///     <see cref="Export.CacheExportJoins"/> attributes the forward direction to, so a reader can
    ///     check both against the same measured claim. Nothing here re-derives an address: an item's
    ///     id folds back to a group through <see cref="CacheAddressing"/>, exactly as the item
    ///     descriptor does it.
    ///     </para>
    /// </remarks>
    public sealed class QuestItemIndex {
        /// <summary>The measured relation this inverts, named as the export names it.</summary>
        public const string Join = "item opcode 132 -> config group 35";

        private readonly Dictionary<int, List<int>> byQuest;

        private QuestItemIndex(Dictionary<int, List<int>> byQuest, int itemsRead, int itemsFailed) {
            this.byQuest = byQuest;
            ItemsRead = itemsRead;
            ItemsFailed = itemsFailed;
        }

        /// <summary>How many item definitions were decoded to build this.</summary>
        public int ItemsRead { get; }

        /// <summary>
        ///     How many item definitions would not decode.
        /// </summary>
        /// <remarks>
        ///     Reported rather than folded into the total, because a non-zero count here means the
        ///     answer below is a floor rather than an answer - an item that did not decode may well
        ///     have named the quest being asked about.
        /// </remarks>
        public int ItemsFailed { get; }

        /// <summary>
        ///     Reads every item definition and records which quests each names.
        /// </summary>
        /// <remarks>
        ///     An item that will not decode costs itself and nothing else, which is what
        ///     <see cref="ItemsFailed"/> reports. This is expensive enough to belong on a worker;
        ///     nothing here touches a control, so it is safe to call from one.
        /// </remarks>
        /// <param name="cache">The open cache.</param>
        /// <returns>The inverted relation.</returns>
        public static QuestItemIndex Build(RSCache cache) {
            if (cache == null)
                throw new ArgumentNullException(nameof(cache));

            var byQuest = new Dictionary<int, List<int>>();
            int read = 0;
            int failed = 0;

            CacheAddressing addressing = CacheAddressing.For(RSConstants.ITEM_DEFINITIONS_INDEX);

            foreach (int group in cache.EnumerateGroups(RSConstants.ITEM_DEFINITIONS_INDEX)) {
                IReadOnlyDictionary<int, JagStream> files;

                try {
                    files = cache.ReadGroup(RSConstants.ITEM_DEFINITIONS_INDEX, group);
                }
                catch (Exception ex) {
                    Debug("Quest item index could not read item group " + group + ": " + ex.Message);
                    continue;
                }

                foreach (KeyValuePair<int, JagStream> file in files) {
                    int itemId = addressing.DefinitionId(group, file.Key);

                    try {
                        ItemDefinition item = ItemDefinition.DecodeFromStream(file.Value);
                        read++;

                        if (item.quests == null)
                            continue;

                        foreach (int quest in item.quests) {
                            if (!byQuest.TryGetValue(quest, out List<int>? items))
                                byQuest[quest] = items = new List<int>();

                            items.Add(itemId);
                        }
                    }
                    catch (Exception ex) {
                        failed++;
                        Debug("Quest item index could not decode item " + itemId + ": " + ex.Message,
                            LOG_DETAIL.ADVANCED);
                    }
                }
            }

            return new QuestItemIndex(byQuest, read, failed);
        }

        /// <summary>
        ///     Which items name one quest, in ascending item id.
        /// </summary>
        /// <remarks>
        ///     An item naming the same quest twice appears twice, deliberately: opcode 132 stores a
        ///     list and the client does not deduplicate it, so a repeat is a fact about the record
        ///     rather than noise to hide.
        /// </remarks>
        /// <param name="questId">The group-35 file id.</param>
        /// <returns>The item ids, possibly none.</returns>
        public IReadOnlyList<int> ItemsNaming(int questId) {
            if (!byQuest.TryGetValue(questId, out List<int>? items))
                return Array.Empty<int>();

            items.Sort();
            return items;
        }
    }
}
