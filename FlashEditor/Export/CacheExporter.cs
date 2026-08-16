using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using System.Text.Json;
using System.Threading;
using FlashEditor.Cache;
using FlashEditor.Cache.Region;
using FlashEditor.Cache.Util;
using FlashEditor.Cache.Util.Crypto;
using FlashEditor.Definitions.Audio;
using FlashEditor.Definitions.Editing;
using FlashEditor.Definitions.Models;
using FlashEditor.Definitions.Sprites;
using FlashEditor.IO;
using static FlashEditor.Utils.DebugUtil;

namespace FlashEditor.Export {
    /// <summary>What one index contributed to an export.</summary>
    public sealed class CacheExportIndexSummary {
        /// <summary>Records what an index produced.</summary>
        /// <param name="indexId">The index.</param>
        /// <param name="coverage">How it was written out.</param>
        /// <param name="declaredGroups">How many groups its reference table declares.</param>
        /// <param name="declaredFiles">How many files its reference table declares.</param>
        /// <param name="records">How many records were written for it.</param>
        /// <param name="failures">How many records would not decode.</param>
        public CacheExportIndexSummary(int indexId, ExportCoverage coverage, int declaredGroups,
            int declaredFiles, int records, int failures) {
            IndexId = indexId;
            Coverage = coverage;
            DeclaredGroups = declaredGroups;
            DeclaredFiles = declaredFiles;
            Records = records;
            Failures = failures;
        }

        /// <summary>The index.</summary>
        public int IndexId { get; }

        /// <summary>How it was written out.</summary>
        public ExportCoverage Coverage { get; }

        /// <summary>How many groups its reference table declares.</summary>
        public int DeclaredGroups { get; }

        /// <summary>How many files its reference table declares.</summary>
        public int DeclaredFiles { get; }

        /// <summary>How many records were written.</summary>
        public int Records { get; }

        /// <summary>
        ///     How many records would not decode.
        /// </summary>
        /// <remarks>
        ///     Counted and reported rather than allowed to end the export. A record that will not
        ///     decode is a finding, and losing the other fifty thousand to it would hide the finding
        ///     rather than surface it.
        /// </remarks>
        public int Failures { get; }
    }

    /// <summary>What an export produced.</summary>
    public sealed class CacheExportResult {
        /// <summary>Records the outcome of an export.</summary>
        /// <param name="destination">Where it was written.</param>
        /// <param name="provenance">Which cache it came from.</param>
        /// <param name="indexes">What each index contributed.</param>
        /// <param name="records">How many records were written in total.</param>
        /// <param name="failures">How many records would not decode.</param>
        public CacheExportResult(string destination, CacheProvenance provenance,
            IReadOnlyList<CacheExportIndexSummary> indexes, int records, int failures) {
            Destination = destination;
            Provenance = provenance;
            Indexes = indexes;
            Records = records;
            Failures = failures;
        }

        /// <summary>Where the export was written.</summary>
        public string Destination { get; }

        /// <summary>Which cache it came from.</summary>
        public CacheProvenance Provenance { get; }

        /// <summary>What each index contributed.</summary>
        public IReadOnlyList<CacheExportIndexSummary> Indexes { get; }

        /// <summary>How many records were written in total.</summary>
        public int Records { get; }

        /// <summary>How many records would not decode.</summary>
        public int Failures { get; }
    }

    /// <summary>
    ///     Writes the whole cache out as structured JSON, so it can be queried outside the editor.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///     <b>Read only, and never a round trip.</b> Nothing here is an interchange format and the
    ///     export cannot be packed back into a cache. The obstacle is that none of these formats is
    ///     canonical: opcode order is free, opcodes repeat, values alias, an absent field is not the
    ///     same as one storing the default, integers are stored at more than one width, and index 9
    ///     keeps raw per-opcode payload spans. A record read to its values and written back from them
    ///     differs from bytes nobody edited, which rewrites the archive, its CRC, and the
    ///     reference-table entry of every archive packed alongside it. Every file this writes says so
    ///     in its own header.
    ///     </para>
    ///     <para>
    ///     Three things decide how it reads the cache. It enumerates from the reference table, never
    ///     from the idx file, because the client gates every read on the table. It reads a group at a
    ///     time through <see cref="RSCache.ReadGroup"/>, because reading a group file by file
    ///     re-inflates and re-decodes that group once per file. And it holds one group of records at a
    ///     time and streams them out, because decode buffers are not pooled here and a whole-cache
    ///     walk that accumulated would sit on the large object heap for the length of the run.
    ///     </para>
    /// </remarks>
    public sealed class CacheExporter {
        /// <summary>The name of the machine-readable header at the root of an export.</summary>
        public const string HeaderFileName = "export.json";

        /// <summary>The name of the prose header beside it.</summary>
        public const string ReadmeFileName = "README.md";

        /// <summary>How many records go into one part file before it rolls over.</summary>
        /// <remarks>
        ///     A bound on both file size and file count. One file per group would be 63,607 files on
        ///     index 7 alone; one file per index would be a single unreadable blob. A group is never
        ///     split across two parts, so a part is a whole number of groups and can be read on its
        ///     own.
        /// </remarks>
        private const int RecordsPerPart = 2000;

        private readonly RSCache cache;
        private readonly string? cacheDirectory;
        private readonly CacheExportOptions options;
        private readonly CacheProvenance provenance;
        private readonly CacheReferenceResolver resolver;
        private readonly List<CacheExportIndexSummary> summaries = new List<CacheExportIndexSummary>();

        /// <summary>Map square names by their hash, built only when index 5 is written.</summary>
        private Dictionary<int, string>? mapSquareNames;

        /// <summary>Prepares an export of an open cache.</summary>
        /// <param name="cache">The open cache. It is only read.</param>
        /// <param name="cacheDirectory">Where the cache was opened from, for the provenance stamp.</param>
        /// <param name="options">What to write and where, or null for the defaults.</param>
        public CacheExporter(RSCache cache, string? cacheDirectory, CacheExportOptions? options = null) {
            this.cache = cache ?? throw new ArgumentNullException(nameof(cache));
            this.cacheDirectory = cacheDirectory;
            this.options = options ?? new CacheExportOptions();

            provenance = CacheProvenance.Identify(cache);
            resolver = new CacheReferenceResolver(cache);
        }

        /// <summary>Which cache this export will be stamped with.</summary>
        public CacheProvenance Provenance => provenance;

        /// <summary>
        ///     Writes the export.
        /// </summary>
        /// <param name="progress">Told the name of each index as it starts, or null.</param>
        /// <param name="token">Cancels the run between groups.</param>
        /// <returns>What was written.</returns>
        public CacheExportResult Run(IProgress<string>? progress = null, CancellationToken token = default) {
            string destination = options.ResolveDestination(cacheDirectory);
            Directory.CreateDirectory(destination);

            summaries.Clear();

            int records = 0;
            int failures = 0;

            foreach (int indexId in IndexesToExport()) {
                token.ThrowIfCancellationRequested();
                progress?.Report("index " + indexId + " (" + IndexName(indexId) + ")");

                CacheExportIndexSummary summary = ExportIndex(destination, indexId, token);
                summaries.Add(summary);
                records += summary.Records;
                failures += summary.Failures;
            }

            WriteReferenceTables(destination);
            WriteHeader(destination, records, failures);
            WriteReadme(destination);

            return new CacheExportResult(destination, provenance, summaries, records, failures);
        }

        /// <summary>
        ///     The indexes to walk.
        /// </summary>
        /// <remarks>
        ///     The store's own list of present indexes, so an index the cache does not carry is not
        ///     walked at all. The caller may narrow it; it cannot widen it past what is on disk.
        /// </remarks>
        /// <returns>The index ids, ascending.</returns>
        private IEnumerable<int> IndexesToExport() {
            var present = new List<int>(cache.GetStore().ContentIndexIds);
            present.Sort();

            if (options.Indexes == null)
                return present;

            var narrowed = new List<int>();
            foreach (int indexId in present)
                if (Contains(options.Indexes, indexId))
                    narrowed.Add(indexId);

            return narrowed;
        }

        /// <summary>Whether a list holds a value.</summary>
        /// <param name="values">The list.</param>
        /// <param name="value">The value.</param>
        /// <returns>Whether it is present.</returns>
        private static bool Contains(IReadOnlyList<int> values, int value) {
            for (int i = 0; i < values.Count; i++)
                if (values[i] == value)
                    return true;
            return false;
        }

        /// <summary>Writes one index: its manifest, and its records where it has any.</summary>
        /// <param name="destination">The export root.</param>
        /// <param name="indexId">The index.</param>
        /// <param name="token">Cancels the run between groups.</param>
        /// <returns>What the index contributed.</returns>
        private CacheExportIndexSummary ExportIndex(string destination, int indexId, CancellationToken token) {
            string directory = Path.Combine(destination, IndexDirectoryName(indexId));
            Directory.CreateDirectory(directory);

            RSReferenceTable? table = TableOf(indexId);
            IReadOnlyList<IDefinitionListDescriptor> descriptors = CacheExportPlan.DescriptorsFor(indexId);

            ExportCoverage coverage =
                table == null ? ExportCoverage.Absent
                : descriptors.Count > 0 || HasCustomSection(indexId) ? ExportCoverage.Structured
                : ExportCoverage.Manifest;

            int records = 0;
            int failures = 0;

            if (coverage == ExportCoverage.Structured) {
                var used = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

                foreach (IDefinitionListDescriptor descriptor in descriptors) {
                    (int written, int failed) = ExportDescriptor(directory, indexId, descriptor, used, token);
                    records += written;
                    failures += failed;
                }

                (int customWritten, int customFailed) = ExportCustomSection(directory, indexId, used, token);
                records += customWritten;
                failures += customFailed;
            }

            Dictionary<int, List<string>>? payloads =
                coverage == ExportCoverage.Manifest && options.WriteBinaryPayloads
                    ? WritePayloads(directory, indexId, token)
                    : null;

            WriteIndexManifest(directory, indexId, table, coverage, descriptors, records, failures, payloads);

            return new CacheExportIndexSummary(indexId, coverage, table?.GetArchiveCount() ?? 0,
                DeclaredFileCount(table), records, failures);
        }

        /// <summary>Writes every record one descriptor produces, a group at a time.</summary>
        /// <param name="directory">The index's directory.</param>
        /// <param name="indexId">The index.</param>
        /// <param name="descriptor">The descriptor.</param>
        /// <param name="usedSectionNames">Section directory names already taken on this index.</param>
        /// <param name="token">Cancels the run between groups.</param>
        /// <returns>How many records were written and how many failed.</returns>
        private (int Written, int Failed) ExportDescriptor(string directory, int indexId,
            IDefinitionListDescriptor descriptor, HashSet<string> usedSectionNames, CancellationToken token) {
            string section = UniqueSection(usedSectionNames, descriptor.RowNoun);
            string sectionDirectory = Path.Combine(directory, section);

            var byGroup = new SortedDictionary<int, List<DefinitionAddress>>();
            foreach (DefinitionAddress address in descriptor.Enumerate(cache)) {
                if (!byGroup.TryGetValue(address.GroupId, out List<DefinitionAddress>? addresses)) {
                    addresses = new List<DefinitionAddress>();
                    byGroup[address.GroupId] = addresses;
                }

                addresses.Add(address);
            }

            if (byGroup.Count == 0)
                return (0, 0);

            Directory.CreateDirectory(sectionDirectory);

            using var parts = new PartWriter(sectionDirectory, indexId, section,
                descriptor.GetType().Name, provenance);

            int failed = 0;

            foreach (KeyValuePair<int, List<DefinitionAddress>> group in byGroup) {
                token.ThrowIfCancellationRequested();

                IReadOnlyDictionary<int, JagStream> files;

                if (descriptor.ReadsPayload) {
                    try {
                        //One decode for the whole group. ReadFile releases the container as soon as
                        //it has handed back one file, so a per-file walk here would re-inflate and
                        //re-decode this group once per record.
                        files = cache.ReadGroup(indexId, group.Key);
                    } catch (Exception ex) {
                        Debug("Export could not read " + indexId + "/" + group.Key + ": " + ex.Message,
                            LOG_DETAIL.BASIC);
                        failed += group.Value.Count;
                        continue;
                    }
                } else {
                    files = EmptyFiles;
                }

                parts.BeginGroup(group.Key);

                foreach (DefinitionAddress address in group.Value) {
                    JagStream payload;

                    if (!descriptor.ReadsPayload)
                        payload = new JagStream();
                    else if (files.TryGetValue(address.FileId, out JagStream? stored))
                        payload = stored;
                    else {
                        //Declared by the table and absent from the payload. ReadGroup omits it
                        //rather than returning an empty stream, so this is the table and the archive
                        //disagreeing and is worth counting.
                        failed++;
                        continue;
                    }

                    object record;
                    try {
                        payload.Seek0();
                        record = descriptor.Decode(cache, address, payload);
                    } catch (Exception ex) {
                        Debug("Export could not decode " + indexId + "/" + address + ": " + ex.Message,
                            LOG_DETAIL.BASIC);
                        failed++;
                        continue;
                    }

                    parts.WriteRecord(address, record, resolver);
                }
            }

            return (parts.RecordsWritten, failed);
        }

        /// <summary>An empty file map, for a descriptor that reads no payload.</summary>
        private static readonly IReadOnlyDictionary<int, JagStream> EmptyFiles =
            new Dictionary<int, JagStream>();

        /// <summary>Whether an index has a section this exporter writes itself.</summary>
        /// <param name="indexId">The index.</param>
        /// <returns>Whether a custom section exists for it.</returns>
        private bool HasCustomSection(int indexId) {
            switch (indexId) {
                case RSConstants.MODELS_INDEX:
                    return options.IncludeModelReferences;
                case RSConstants.TEXTURES:
                case RSConstants.MIDI_PATCH_INDEX:
                case RSConstants.MATERIALS:
                    return true;
                default:
                    return false;
            }
        }

        /// <summary>Writes the sections no definition-list descriptor covers.</summary>
        /// <param name="directory">The index's directory.</param>
        /// <param name="indexId">The index.</param>
        /// <param name="usedSectionNames">Section directory names already taken on this index.</param>
        /// <param name="token">Cancels the run between groups.</param>
        /// <returns>How many records were written and how many failed.</returns>
        private (int Written, int Failed) ExportCustomSection(string directory, int indexId,
            HashSet<string> usedSectionNames, CancellationToken token) {
            switch (indexId) {
                case RSConstants.MODELS_INDEX when options.IncludeModelReferences:
                    return ExportGroupRecords(directory, indexId, usedSectionNames, "model reference",
                        "ModelCodec", token,
                        (group, file, payload) => new ModelReferenceRecord(group, file,
                            ModelCodec.Decode(payload, group)));
                case RSConstants.TEXTURES:
                    return ExportGroupRecords(directory, indexId, usedSectionNames, "texture graph",
                        "Texture", token,
                        (group, file, payload) => Texture.Decode(payload));
                case RSConstants.MIDI_PATCH_INDEX:
                    return ExportGroupRecords(directory, indexId, usedSectionNames, "midi patch",
                        "MidiPatchDefinition", token,
                        (group, file, payload) => new MidiPatchRecord(group, file,
                            new MidiPatchDefinition { Id = group }.Decode(payload)));
                case RSConstants.MATERIALS:
                    return ExportMaterials(directory, usedSectionNames);
                default:
                    return (0, 0);
            }
        }

        /// <summary>
        ///     Writes one record per declared file of an index, decoded by a supplied reader.
        /// </summary>
        /// <remarks>
        ///     The same group-at-a-time shape the descriptor path uses, for the indexes whose decoder
        ///     is not wired to a definition list. The reader is handed the payload positioned at its
        ///     start and is expected to leave nothing behind it.
        /// </remarks>
        /// <param name="directory">The index's directory.</param>
        /// <param name="indexId">The index.</param>
        /// <param name="usedSectionNames">Section directory names already taken on this index.</param>
        /// <param name="rowNoun">What one record is called.</param>
        /// <param name="decoderName">The decoder, for the part file's header.</param>
        /// <param name="token">Cancels the run between groups.</param>
        /// <param name="read">Decodes one file.</param>
        /// <returns>How many records were written and how many failed.</returns>
        private (int Written, int Failed) ExportGroupRecords(string directory, int indexId,
            HashSet<string> usedSectionNames, string rowNoun, string decoderName, CancellationToken token,
            Func<int, int, JagStream, object> read) {
            string section = UniqueSection(usedSectionNames, rowNoun);
            string sectionDirectory = Path.Combine(directory, section);
            Directory.CreateDirectory(sectionDirectory);

            using var parts = new PartWriter(sectionDirectory, indexId, section, decoderName, provenance);

            int failed = 0;

            foreach (int groupId in cache.EnumerateGroups(indexId)) {
                token.ThrowIfCancellationRequested();

                IReadOnlyDictionary<int, JagStream> files;
                try {
                    files = cache.ReadGroup(indexId, groupId);
                } catch (Exception ex) {
                    Debug("Export could not read " + indexId + "/" + groupId + ": " + ex.Message,
                        LOG_DETAIL.BASIC);
                    failed++;
                    continue;
                }

                parts.BeginGroup(groupId);

                foreach (KeyValuePair<int, JagStream> file in files) {
                    object record;
                    try {
                        file.Value.Seek0();
                        record = read(groupId, file.Key, file.Value);
                    } catch (Exception ex) {
                        Debug("Export could not decode " + indexId + "/" + groupId + "/" + file.Key +
                            ": " + ex.Message, LOG_DETAIL.BASIC);
                        failed++;
                        continue;
                    }

                    parts.WriteRecord(new DefinitionAddress(groupId, file.Key), record, resolver);
                }
            }

            return (parts.RecordsWritten, failed);
        }

        /// <summary>
        ///     Writes index 26, which is one columnar table rather than a group of records.
        /// </summary>
        /// <remarks>
        ///     The index holds a single file whose rows are stored column major, so there is no
        ///     per-record file to walk. <see cref="MaterialTable.Load"/> is the only decoder that
        ///     knows the layout, and the slots it produces are what a reader wants.
        /// </remarks>
        /// <param name="directory">The index's directory.</param>
        /// <param name="usedSectionNames">Section directory names already taken on this index.</param>
        /// <returns>How many records were written and how many failed.</returns>
        private (int Written, int Failed) ExportMaterials(string directory, HashSet<string> usedSectionNames) {
            string section = UniqueSection(usedSectionNames, "material");
            string sectionDirectory = Path.Combine(directory, section);
            Directory.CreateDirectory(sectionDirectory);

            MaterialTable table;
            try {
                table = MaterialTable.Load(cache);
            } catch (Exception ex) {
                Debug("Export could not read the material table: " + ex.Message, LOG_DETAIL.BASIC);
                return (0, 1);
            }

            using var parts = new PartWriter(sectionDirectory, RSConstants.MATERIALS, section,
                "MaterialTable", provenance);

            parts.BeginGroup(0);

            for (int slot = 0; slot < table.Slots.Count; slot++)
                parts.WriteRecord(new DefinitionAddress(0, slot, slot), table.Slots[slot], resolver);

            return (parts.RecordsWritten, 0);
        }

        /// <summary>
        ///     Writes an index's manifest: what its reference table declares, group by group.
        /// </summary>
        /// <remarks>
        ///     Every figure here is read from the table and the idx record rather than by decoding
        ///     anything, which is what makes the manifest cheap enough to write for an index whose
        ///     payload is never touched. The group CRC is the table's own, so it covers the stored
        ///     container - including the ciphertext for an encrypted archive.
        /// </remarks>
        /// <param name="directory">The index's directory.</param>
        /// <param name="indexId">The index.</param>
        /// <param name="table">Its reference table, or null when it has none.</param>
        /// <param name="coverage">How the index was written.</param>
        /// <param name="descriptors">The descriptors that decoded it.</param>
        /// <param name="records">How many records were written.</param>
        /// <param name="failures">How many records would not decode.</param>
        /// <param name="payloads">The files written per group, or null when none were.</param>
        private void WriteIndexManifest(string directory, int indexId, RSReferenceTable? table,
            ExportCoverage coverage, IReadOnlyList<IDefinitionListDescriptor> descriptors,
            int records, int failures, Dictionary<int, List<string>>? payloads) {
            using FileStream stream = File.Create(Path.Combine(directory, "manifest.json"));
            using var writer = new Utf8JsonWriter(stream, new JsonWriterOptions { Indented = true });

            writer.WriteStartObject();
            WriteExportKind(writer);
            writer.WriteNumber("index", indexId);
            writer.WriteString("name", IndexName(indexId));
            writer.WriteString("coverage", coverage.ToString());

            string? reason = CacheExportPlan.ManifestReason(indexId);
            if (reason != null)
                writer.WriteString("manifestReason", reason);

            writer.WriteNumber("recordsWritten", records);
            writer.WriteNumber("recordsThatWouldNotDecode", failures);

            writer.WriteStartArray("sections");
            foreach (IDefinitionListDescriptor descriptor in descriptors)
                writer.WriteStringValue(descriptor.RowNoun);
            writer.WriteEndArray();

            if (table == null) {
                writer.WriteString("table", "this index has no reference table in this cache");
                writer.WriteEndObject();
                return;
            }

            writer.WriteStartObject("table");
            writer.WriteNumber("format", table.format);
            writer.WriteNumber("version", table.version);
            writer.WriteNumber("flags", table.flags);
            writer.WriteBoolean("hasIdentifiers", table.hasIdentifiers);
            writer.WriteBoolean("usesWhirlpool", table.usesWhirlpool);
            writer.WriteBoolean("hasSizes", table.sizes);
            writer.WriteBoolean("hasEntryHashes", table.entryHashes);
            writer.WriteNumber("declaredGroups", table.GetArchiveCount());
            writer.WriteNumber("declaredFiles", DeclaredFileCount(table));
            writer.WriteEndObject();

            WriteOrphans(writer, indexId);
            WriteGroups(writer, indexId, table, payloads);

            writer.WriteEndObject();
        }

        /// <summary>
        ///     Writes the decompressed payload of every declared file of a manifest-only index.
        /// </summary>
        /// <remarks>
        ///     Off unless asked for. The decompressed file is the artefact rather than the stored
        ///     container, because the container is a compression envelope nothing outside this editor
        ///     reads - and because a re-encoded GZip container is never byte-identical anyway, so a
        ///     stored container written here could not be compared with the one it came from.
        /// </remarks>
        /// <param name="directory">The index's directory.</param>
        /// <param name="indexId">The index.</param>
        /// <param name="token">Cancels the run between groups.</param>
        /// <returns>The relative paths written, by group.</returns>
        private Dictionary<int, List<string>> WritePayloads(string directory, int indexId,
            CancellationToken token) {
            var written = new Dictionary<int, List<string>>();

            string payloadDirectory = Path.Combine(directory, "payloads");
            Directory.CreateDirectory(payloadDirectory);

            foreach (int groupId in cache.EnumerateGroups(indexId)) {
                token.ThrowIfCancellationRequested();

                IReadOnlyDictionary<int, JagStream> files;
                try {
                    files = cache.ReadGroup(indexId, groupId);
                } catch (Exception ex) {
                    Debug("Export could not read " + indexId + "/" + groupId + " for its payload: " +
                        ex.Message, LOG_DETAIL.BASIC);
                    continue;
                }

                var paths = new List<string>();

                foreach (KeyValuePair<int, JagStream> file in files) {
                    string name = "g" + groupId.ToString(CultureInfo.InvariantCulture) +
                        "-f" + file.Key.ToString(CultureInfo.InvariantCulture) + ".bin";

                    File.WriteAllBytes(Path.Combine(payloadDirectory, name), file.Value.ToArray());
                    paths.Add("payloads/" + name);
                }

                written[groupId] = paths;
            }

            return written;
        }

        /// <summary>Lists the groups an idx file holds that its reference table does not declare.</summary>
        /// <remarks>
        ///     Reported rather than folded in. The client resolves every read through the table, so an
        ///     undeclared group is unreachable in game whatever its bytes say - but it is still on
        ///     disk, and an export that silently dropped it would be a worse record of the cache than
        ///     one that names it.
        /// </remarks>
        /// <param name="writer">The manifest writer.</param>
        /// <param name="indexId">The index.</param>
        private void WriteOrphans(Utf8JsonWriter writer, int indexId) {
            IReadOnlyList<int> orphans;
            try {
                orphans = cache.EnumerateOrphanGroups(indexId);
            } catch (Exception ex) {
                Debug("Export could not scan index " + indexId + " for orphans: " + ex.Message,
                    LOG_DETAIL.ADVANCED);
                return;
            }

            writer.WriteStartArray("undeclaredGroups");
            foreach (int orphan in orphans)
                writer.WriteNumberValue(orphan);
            writer.WriteEndArray();

            writer.WriteBoolean("undeclaredGroupsExported", options.IncludeOrphanGroups && orphans.Count > 0);
        }

        /// <summary>Writes one manifest row per declared group.</summary>
        /// <param name="writer">The manifest writer.</param>
        /// <param name="indexId">The index.</param>
        /// <param name="table">Its reference table.</param>
        /// <param name="payloads">The files written per group, or null when none were.</param>
        private void WriteGroups(Utf8JsonWriter writer, int indexId, RSReferenceTable table,
            Dictionary<int, List<string>>? payloads) {
            RSIndex? idx = null;
            int slots = 0;
            try {
                idx = cache.GetStore().GetIndexEntry(indexId);
                slots = cache.GetStore().GetFileCount(indexId);
            } catch (Exception ex) {
                //No idx file for this index. The table's own figures are still worth writing.
                Debug("Export could not open idx" + indexId + ": " + ex.Message, LOG_DETAIL.ADVANCED);
            }

            XTEAKeyTable? keys = indexId == RSConstants.MAPS_INDEX ? cache.GetXTEAKeyTable() : null;

            writer.WriteStartArray("groups");

            foreach (KeyValuePair<int, RSArchiveEntry> entry in table.GetArchiveEntries()) {
                writer.WriteStartObject();
                writer.WriteNumber("id", entry.Key);
                writer.WriteNumber("crc", entry.Value.GetCrc());
                writer.WriteNumber("version", entry.Value.GetVersion());

                if (table.hasIdentifiers) {
                    writer.WriteNumber("identifier", entry.Value.GetIdentifier());

                    string? name = NameOf(indexId, entry.Value.GetIdentifier());
                    if (name != null)
                        writer.WriteString("name", name);
                }

                int[] files;
                try {
                    files = entry.Value.GetValidFileIds();
                } catch (InvalidOperationException) {
                    files = Array.Empty<int>();
                }

                writer.WriteNumber("declaredFiles", files.Length);

                if (idx != null && entry.Key < slots) {
                    idx.ReadContainerHeader(entry.Key);
                    writer.WriteNumber("storedLength", idx.GetSize());
                    writer.WriteNumber("firstSector", idx.GetSectorID());
                }

                if (keys != null)
                    writer.WriteBoolean("hasXteaKey", keys.GetKey(indexId, entry.Key) != null);

                //A path is written only where a file actually was. A path field naming a file that
                //was never created reads as an assurance and is a lie.
                if (payloads != null && payloads.TryGetValue(entry.Key, out List<string>? paths)) {
                    writer.WriteStartArray("payloadPaths");
                    foreach (string path in paths)
                        writer.WriteStringValue(path);
                    writer.WriteEndArray();
                }

                writer.WriteEndObject();
            }

            writer.WriteEndArray();
        }

        /// <summary>
        ///     A group's name, where it can be proved by re-hashing a candidate.
        /// </summary>
        /// <remarks>
        ///     Only index 5, and only because its names are generated rather than guessed: every map
        ///     square is <c>m</c>, <c>l</c>, <c>um</c>, <c>ul</c> or <c>n</c> followed by its two
        ///     coordinates, so the whole name space can be enumerated and hashed. A name that comes
        ///     back is one whose hash equals the identifier the table holds, which is proof rather
        ///     than a plausible match. Nothing else here names a group, because a name recovered any
        ///     other way would be a claim this export cannot support.
        /// </remarks>
        /// <param name="indexId">The index.</param>
        /// <param name="identifier">The stored name hash.</param>
        /// <returns>The name, or null.</returns>
        private string? NameOf(int indexId, int identifier) {
            if (indexId != RSConstants.MAPS_INDEX)
                return null;

            mapSquareNames ??= BuildMapSquareNames();
            return mapSquareNames.TryGetValue(identifier, out string? name) ? name : null;
        }

        /// <summary>Every map square name the coordinate space can produce, by hash.</summary>
        /// <returns>Name hash to name.</returns>
        private static Dictionary<int, string> BuildMapSquareNames() {
            var names = new Dictionary<int, string>();

            for (int x = 0; x < 256; x++) {
                for (int y = 0; y < 256; y++) {
                    Record(names, MapSquareNames.Terrain(x, y));
                    Record(names, MapSquareNames.Locations(x, y));
                    Record(names, MapSquareNames.UnderwaterTerrain(x, y));
                    Record(names, MapSquareNames.UnderwaterLocations(x, y));
                    Record(names, MapSquareNames.NpcSpawns(x, y));
                }
            }

            return names;
        }

        /// <summary>Records one candidate name under its hash, first writer winning.</summary>
        /// <param name="names">The map being built.</param>
        /// <param name="name">The candidate.</param>
        private static void Record(Dictionary<int, string> names, string name) {
            //A collision would make the second name unprovable, so the first is kept and the clash is
            //simply not reported. None occurs in this name space; the guard is here because a silent
            //overwrite would be the failure that looks like a correct answer.
            int hash = NameHasher.GetNameHash(name);
            if (!names.ContainsKey(hash))
                names[hash] = name;
        }

        /// <summary>Writes the shape of every reference table in the meta index.</summary>
        /// <remarks>
        ///     Index 255 has no records of its own to export - it holds the tables the rest of this
        ///     export is enumerated from - so it is written as one file describing all of them. Half
        ///     the format is unexercised in this cache and that is worth stating rather than leaving
        ///     to be rediscovered.
        /// </remarks>
        /// <param name="destination">The export root.</param>
        private void WriteReferenceTables(string destination) {
            using FileStream stream = File.Create(Path.Combine(destination, "reference-tables.json"));
            using var writer = new Utf8JsonWriter(stream, new JsonWriterOptions { Indented = true });

            writer.WriteStartObject();
            WriteExportKind(writer);
            writer.WriteString("source", "index 255, the meta index");
            writer.WriteStartArray("tables");

            foreach (int indexId in cache.GetStore().ContentIndexIds) {
                RSReferenceTable? table = TableOf(indexId);
                if (table == null)
                    continue;

                writer.WriteStartObject();
                writer.WriteNumber("index", indexId);
                writer.WriteString("name", IndexName(indexId));
                writer.WriteNumber("format", table.format);
                writer.WriteNumber("version", table.version);
                writer.WriteNumber("flags", table.flags);
                writer.WriteBoolean("hasIdentifiers", table.hasIdentifiers);
                writer.WriteBoolean("usesWhirlpool", table.usesWhirlpool);
                writer.WriteBoolean("hasSizes", table.sizes);
                writer.WriteBoolean("hasEntryHashes", table.entryHashes);
                writer.WriteNumber("declaredGroups", table.GetArchiveCount());
                writer.WriteNumber("declaredFiles", DeclaredFileCount(table));
                writer.WriteEndObject();
            }

            writer.WriteEndArray();
            writer.WriteEndObject();
        }

        /// <summary>Writes the machine-readable header at the root of the export.</summary>
        /// <param name="destination">The export root.</param>
        /// <param name="records">How many records were written.</param>
        /// <param name="failures">How many would not decode.</param>
        private void WriteHeader(string destination, int records, int failures) {
            using FileStream stream = File.Create(Path.Combine(destination, HeaderFileName));
            using var writer = new Utf8JsonWriter(stream, new JsonWriterOptions { Indented = true });

            writer.WriteStartObject();
            WriteExportKind(writer);

            writer.WriteStartObject("cache");
            writer.WriteString("kind", provenance.Kind.ToString());
            writer.WriteString("name", provenance.Name);
            if (cacheDirectory != null)
                writer.WriteString("directory", cacheDirectory);

            writer.WriteStartObject("fingerprint");
            writer.WriteString("method",
                "declared group and file counts on indexes 3, 9 and 19, in that order. Never a" +
                " directory name, and never a reference-table version: index 3 carries version 1131" +
                " in both supported caches while holding 11 more groups and 1373 more files in the" +
                " repack, so a matching version is not evidence that an index is untouched.");
            writer.WriteStartArray("indexes");
            foreach (int indexId in CacheProvenance.FingerprintedIndexes)
                writer.WriteNumberValue(indexId);
            writer.WriteEndArray();
            writer.WriteStartArray("counts");
            foreach (int count in provenance.Fingerprint)
                writer.WriteNumberValue(count);
            writer.WriteEndArray();
            writer.WriteEndObject();
            writer.WriteEndObject();

            writer.WriteStartObject("options");
            writer.WriteBoolean("includeOrphanGroups", options.IncludeOrphanGroups);
            writer.WriteBoolean("writeBinaryPayloads", options.WriteBinaryPayloads);
            writer.WriteBoolean("includeModelReferences", options.IncludeModelReferences);
            writer.WriteEndObject();

            writer.WriteString("enumeration",
                "table driven. Every record here is one an index's reference table declares, because" +
                " the client resolves every read through the table and cannot reach a group the table" +
                " omits. Groups an idx file holds and no table declares are listed per index under" +
                " undeclaredGroups and are " +
                (options.IncludeOrphanGroups ? "exported." : "not exported."));

            writer.WriteStartArray("joinsResolved");
            foreach (string join in CacheExportJoins.Resolved)
                writer.WriteStringValue(join);
            writer.WriteEndArray();

            writer.WriteStartArray("joinsNotResolved");
            foreach (string join in CacheExportJoins.NotResolved)
                writer.WriteStringValue(join);
            writer.WriteEndArray();

            writer.WriteNumber("recordsWritten", records);
            writer.WriteNumber("recordsThatWouldNotDecode", failures);

            writer.WriteStartArray("indexes");
            foreach (CacheExportIndexSummary summary in summaries) {
                writer.WriteStartObject();
                writer.WriteNumber("index", summary.IndexId);
                writer.WriteString("name", IndexName(summary.IndexId));
                writer.WriteString("coverage", summary.Coverage.ToString());
                writer.WriteNumber("declaredGroups", summary.DeclaredGroups);
                writer.WriteNumber("declaredFiles", summary.DeclaredFiles);
                writer.WriteNumber("recordsWritten", summary.Records);
                writer.WriteNumber("recordsThatWouldNotDecode", summary.Failures);
                writer.WriteEndObject();
            }

            writer.WriteEndArray();
            writer.WriteEndObject();
        }

        /// <summary>The two lines every file of an export carries, so no file can be read out of context.</summary>
        /// <param name="writer">The file's writer, inside its root object.</param>
        private void WriteExportKind(Utf8JsonWriter writer) {
            writer.WriteString("export", "FlashEditor read-only structured cache export");
            writer.WriteString("cacheName", provenance.Name);
            writer.WriteString("warning", NotARoundTrip);
        }

        /// <summary>The single sentence that has to survive being copied out of context.</summary>
        internal const string NotARoundTrip =
            "READ ONLY. This is not a round trip and cannot be packed back into a cache. These" +
            " formats are not canonical - opcode order is free, opcodes repeat, values alias, an" +
            " absent field differs from one storing the default, integers occur at more than one" +
            " width, and index 9 keeps raw per-opcode payload spans - so a record rebuilt from these" +
            " fields would differ from bytes nobody edited.";

        /// <summary>Writes the prose header beside the machine-readable one.</summary>
        /// <param name="destination">The export root.</param>
        private void WriteReadme(string destination) {
            var text = new StringBuilder();

            text.AppendLine("# FlashEditor cache export");
            text.AppendLine();
            text.AppendLine("## Read only. This is not a round trip.");
            text.AppendLine();
            text.AppendLine("Nothing here can be packed back into a cache, and no tool should try.");
            text.AppendLine("None of these formats is canonical, so a record rebuilt from the fields in");
            text.AppendLine("this export would differ from the bytes it came from even where nobody");
            text.AppendLine("edited it. Every case found so far is one of these:");
            text.AppendLine();
            text.AppendLine("- opcode order within a record, which the decoder's loop does not fix;");
            text.AppendLine("- opcode repetition, where only the last occurrence reaches the fields;");
            text.AppendLine("- aliased values, where two stored bytes decode to the same value;");
            text.AppendLine("- absent versus default, which a decoded value cannot tell apart;");
            text.AppendLine("- variable-width integers stored wider than they needed to be;");
            text.AppendLine("- index 9's raw per-opcode payload spans.");
            text.AppendLine();
            text.AppendLine("A save that changed nothing would still rewrite the archive, its CRC, and the");
            text.AppendLine("reference-table entry of every archive packed alongside it. The recorded opcode");
            text.AppendLine("stream is exported beside each record precisely because that is the part a");
            text.AppendLine("value-only reading throws away.");
            text.AppendLine();
            text.AppendLine("## Which cache this is");
            text.AppendLine();
            text.AppendLine("**" + provenance.Name + "**");
            text.AppendLine();
            text.AppendLine("Recognised from the group and file counts indexes 3, 9 and 19 declare, never");
            text.AppendLine("from a directory name and never from a reference-table version. Two 639 caches");
            text.AppendLine("are supported and they disagree on eleven indexes, six of them in their");
            text.AppendLine("declared counts, so a figure taken out of this export means nothing until it is");
            text.AppendLine("paired with the cache above. `export.json` carries the fingerprint itself.");
            text.AppendLine();
            text.AppendLine("## What is in here");
            text.AppendLine();
            text.AppendLine("One directory per cache index. Each holds `manifest.json`, which is what that");
            text.AppendLine("index's reference table and idx file state, and one subdirectory per record");
            text.AppendLine("type holding the decoded records in numbered parts. A part never splits a");
            text.AppendLine("group.");
            text.AppendLine();
            text.AppendLine("Indexes whose payload is an asset are written as a manifest and nothing else,");
            text.AppendLine("because the bytes are the content and no JSON around them makes it queryable:");
            text.AppendLine();

            var manifestIndexes = new List<int>(CacheExportPlan.ManifestIndexes);
            manifestIndexes.Sort();
            foreach (int indexId in manifestIndexes)
                text.AppendLine("- index " + indexId + " (" + IndexName(indexId) + "): " +
                    CacheExportPlan.ManifestReason(indexId));

            text.AppendLine();
            text.AppendLine("## References");
            text.AppendLine();
            text.AppendLine("Each record carries a `references` array resolving the ids it stores. Only the");
            text.AppendLine("relations this project has measured are resolved, and the raw id is always kept");
            text.AppendLine("beside the resolution:");
            text.AppendLine();
            foreach (string join in CacheExportJoins.Resolved)
                text.AppendLine("- " + join);

            text.AppendLine();
            text.AppendLine("`exists` on a reference means the target index's reference table declares that");
            text.AppendLine("file. It is not a claim that the target decodes.");
            text.AppendLine();

            File.WriteAllText(Path.Combine(destination, ReadmeFileName), text.ToString());
        }

        /// <summary>An index's reference table, or null when it has none.</summary>
        /// <param name="indexId">The index.</param>
        /// <returns>The table, or null.</returns>
        private RSReferenceTable? TableOf(int indexId) {
            try {
                return cache.GetReferenceTable(indexId);
            } catch (FileNotFoundException) {
                return null;
            }
        }

        /// <summary>How many files a table declares in total.</summary>
        /// <param name="table">The table, or null.</param>
        /// <returns>The count, or 0.</returns>
        private static int DeclaredFileCount(RSReferenceTable? table) {
            if (table == null)
                return 0;

            int total = 0;
            foreach (KeyValuePair<int, RSArchiveEntry> entry in table.GetArchiveEntries()) {
                try {
                    total += entry.Value.GetValidFileIds().Length;
                } catch (InvalidOperationException) {
                    //A group whose file ids were never set declares none this can count.
                }
            }

            return total;
        }

        /// <summary>The directory one index is written into.</summary>
        /// <param name="indexId">The index.</param>
        /// <returns>A name that sorts in index order.</returns>
        private static string IndexDirectoryName(int indexId) {
            return "index-" + indexId.ToString("D2", CultureInfo.InvariantCulture) + "-" +
                Slug(IndexName(indexId));
        }

        /// <summary>The name this project gives an index.</summary>
        /// <param name="indexId">The index.</param>
        /// <returns>The constant's name, or the id when there is none.</returns>
        private static string IndexName(int indexId) {
            return indexId >= 0 && indexId < RSConstants.indexNames.Length
                ? RSConstants.indexNames[indexId]
                : indexId.ToString(CultureInfo.InvariantCulture);
        }

        /// <summary>A section directory name that is not already taken on this index.</summary>
        /// <remarks>
        ///     Index 2's nineteen provider-less groups all call a record a "record", so their slugs
        ///     collide. Numbering the duplicates keeps each family's records in their own directory
        ///     rather than appended to another family's file.
        /// </remarks>
        /// <param name="used">The names already taken.</param>
        /// <param name="rowNoun">What the section calls one record.</param>
        /// <returns>The directory name.</returns>
        private static string UniqueSection(HashSet<string> used, string rowNoun) {
            string baseName = Slug(rowNoun);
            string name = baseName;

            for (int suffix = 2; !used.Add(name); suffix++)
                name = baseName + "-" + suffix.ToString(CultureInfo.InvariantCulture);

            return name;
        }

        /// <summary>A string reduced to something safe in a path.</summary>
        /// <param name="text">The text.</param>
        /// <returns>Lowercase, with anything but letters and digits collapsed to a hyphen.</returns>
        private static string Slug(string text) {
            var slug = new StringBuilder(text.Length);
            bool hyphen = false;

            foreach (char character in text) {
                if (char.IsLetterOrDigit(character)) {
                    slug.Append(char.ToLowerInvariant(character));
                    hyphen = false;
                } else if (!hyphen && slug.Length > 0) {
                    slug.Append('-');
                    hyphen = true;
                }
            }

            string result = slug.ToString().TrimEnd('-');
            return result.Length == 0 ? "section" : result;
        }

        /// <summary>
        ///     Streams a section's records into numbered part files, rolling over on a record count.
        /// </summary>
        /// <remarks>
        ///     Rolling on a count rather than writing one file per group or one per index. One file
        ///     per group is 63,607 files on index 7 alone, and one file per index is a blob no editor
        ///     will open. A part is closed only between groups, so every part is a whole number of
        ///     groups and can be read on its own.
        /// </remarks>
        private sealed class PartWriter : IDisposable {
            private readonly string directory;
            private readonly int indexId;
            private readonly string section;
            private readonly string decoder;
            private readonly CacheProvenance provenance;

            private FileStream? stream;
            private Utf8JsonWriter? writer;
            private RecordJsonWriter? records;
            private int partNumber;
            private int recordsInPart;
            private int firstGroupInPart;
            private int lastGroupInPart;

            /// <summary>Opens a section for writing.</summary>
            /// <param name="directory">The section's directory.</param>
            /// <param name="indexId">The index.</param>
            /// <param name="section">The section's name.</param>
            /// <param name="decoder">The decoder that produced the records.</param>
            /// <param name="provenance">Which cache this is.</param>
            public PartWriter(string directory, int indexId, string section, string decoder,
                CacheProvenance provenance) {
                this.directory = directory;
                this.indexId = indexId;
                this.section = section;
                this.decoder = decoder;
                this.provenance = provenance;
            }

            /// <summary>How many records this section has written.</summary>
            public int RecordsWritten { get; private set; }

            /// <summary>
            ///     Marks the start of a group, which is the only place a part may roll over.
            /// </summary>
            /// <param name="groupId">The group about to be written.</param>
            public void BeginGroup(int groupId) {
                if (writer != null && recordsInPart >= RecordsPerPart)
                    CloseParts();

                if (writer == null)
                    OpenPart(groupId);

                lastGroupInPart = groupId;
            }

            /// <summary>Writes one record with its address and its resolved references.</summary>
            /// <param name="address">Where the record came from.</param>
            /// <param name="record">The decoded record.</param>
            /// <param name="resolver">Resolves the ids it stores.</param>
            public void WriteRecord(DefinitionAddress address, object record, CacheReferenceResolver resolver) {
                if (writer == null || records == null)
                    OpenPart(address.GroupId);

                writer!.WriteStartObject();

                writer.WriteStartObject("address");
                writer.WriteNumber("group", address.GroupId);
                writer.WriteNumber("file", address.FileId);
                if (address.HasDefinitionId)
                    writer.WriteNumber("definitionId", address.DefinitionId);
                writer.WriteEndObject();

                writer.WritePropertyName("record");
                records!.WriteRecord(record);

                writer.WriteStartArray("references");
                foreach (ExportedReference reference in CacheExportJoins.Extract(record, resolver)) {
                    writer.WriteStartObject();
                    writer.WriteString("field", reference.Field);
                    writer.WriteString("join", reference.Join);
                    writer.WriteNumber("id", reference.Id);
                    writer.WriteNumber("targetIndex", reference.TargetIndex);
                    writer.WriteNumber("targetGroup", reference.TargetGroup);
                    writer.WriteNumber("targetFile", reference.TargetFile);
                    writer.WriteBoolean("exists", reference.Exists);
                    if (reference.Detail != null)
                        writer.WriteString("detail", reference.Detail);
                    if (reference.Identifier.HasValue)
                        writer.WriteNumber("targetIdentifier", reference.Identifier.Value);
                    writer.WriteEndObject();
                }

                writer.WriteEndArray();
                writer.WriteEndObject();

                recordsInPart++;
                RecordsWritten++;
            }

            /// <summary>Opens the next part file and writes its header.</summary>
            /// <param name="groupId">The first group it will hold.</param>
            private void OpenPart(int groupId) {
                string name = "part-" + partNumber.ToString("D5", CultureInfo.InvariantCulture) + ".json";
                stream = File.Create(Path.Combine(directory, name));
                writer = new Utf8JsonWriter(stream, new JsonWriterOptions { Indented = true });
                records = new RecordJsonWriter(writer);

                firstGroupInPart = groupId;
                lastGroupInPart = groupId;
                recordsInPart = 0;

                writer.WriteStartObject();
                writer.WriteString("export", "FlashEditor read-only structured cache export");
                writer.WriteString("cacheName", provenance.Name);
                writer.WriteString("warning", NotARoundTrip);
                writer.WriteNumber("index", indexId);
                writer.WriteString("section", section);
                writer.WriteString("decoder", decoder);
                writer.WriteStartArray("records");
            }

            /// <summary>Closes the open part, stamping the group range it turned out to hold.</summary>
            private void CloseParts() {
                if (writer == null)
                    return;

                writer.WriteEndArray();
                writer.WriteNumber("firstGroup", firstGroupInPart);
                writer.WriteNumber("lastGroup", lastGroupInPart);
                writer.WriteNumber("records", recordsInPart);
                writer.WriteEndObject();
                writer.Flush();
                writer.Dispose();
                stream?.Dispose();

                writer = null;
                stream = null;
                records = null;
                partNumber++;
            }

            /// <summary>Closes the last part.</summary>
            public void Dispose() {
                CloseParts();
            }
        }
    }
}
