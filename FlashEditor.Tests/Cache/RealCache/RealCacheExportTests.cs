using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using FlashEditor.Cache;
using FlashEditor.Definitions.Billboards;
using FlashEditor.Definitions.SpotAnims;
using FlashEditor.Export;
using FlashEditor.IO;
using Xunit;
using Xunit.Abstractions;

namespace FlashEditor.Tests.Cache.RealCache
{
    /// <summary>
    ///     Reads the structured export back and checks a sample of it against a fresh decode.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///     The export is written by walking record types reflectively, which is what stops a field
    ///     added to a decoder from silently vanishing from the export - and which also means nothing
    ///     about the output is stated twice, so a test that only checked the export against itself
    ///     would pass whatever the writer did. Every assertion here therefore compares the file on
    ///     disk against bytes decoded again from the cache.
    ///     </para>
    ///     <para>
    ///     Three small indexes rather than a whole-cache export. Index 29 is one group, 21 is a dozen,
    ///     and both carry opcode streams and outbound references, which is everything the export
    ///     claims to preserve. Sweeping index 16 here would prove the same thing at two hundred times
    ///     the cost, and the byte-identity sweeps already walk those records.
    ///     </para>
    /// </remarks>
    public sealed class RealCacheExportTests : IClassFixture<RealCacheFixture>
    {
        /// <summary>The indexes exported for these tests.</summary>
        private static readonly int[] ExportedIndexes =
        {
            RSConstants.CONFIG_BILLBOARD, RSConstants.GRAPHICS_INDEX
        };

        private readonly RealCacheFixture _fixture;
        private readonly ITestOutputHelper _output;

        /// <summary>Takes the shared cache.</summary>
        /// <param name="fixture">The opened cache.</param>
        /// <param name="output">Where the export's location is reported.</param>
        public RealCacheExportTests(RealCacheFixture fixture, ITestOutputHelper output)
        {
            _fixture = fixture;
            _output = output;
        }

        /// <summary>
        ///     The export names the cache it came from, and the fingerprint it recorded is the one
        ///     the reference tables actually declare.
        /// </summary>
        /// <remarks>
        ///     A provenance stamp nobody can check is decoration. The header carries the six counts
        ///     the recognition was taken from, so this re-reads them off the tables and requires them
        ///     to agree - which is what makes the stamp evidence rather than a label.
        /// </remarks>
        [RealCacheFact]
        public void TheHeaderStampsTheLoadedCacheAndItsFingerprintAgreesWithTheTables()
        {
            string destination = Export();

            using JsonDocument header = Read(Path.Combine(destination, CacheExporter.HeaderFileName));
            JsonElement cache = header.RootElement.GetProperty("cache");

            Assert.False(string.IsNullOrWhiteSpace(cache.GetProperty("name").GetString()),
                "The export must name the cache it came from.");

            JsonElement indexes = cache.GetProperty("fingerprint").GetProperty("indexes");
            JsonElement counts = cache.GetProperty("fingerprint").GetProperty("counts");

            Assert.Equal(indexes.GetArrayLength() * 2, counts.GetArrayLength());

            for (int i = 0; i < indexes.GetArrayLength(); i++)
            {
                int indexId = indexes[i].GetInt32();
                Assert.Equal(_fixture.DeclaredGroups(indexId), counts[i * 2].GetInt32());
                Assert.Equal(_fixture.DeclaredFiles(indexId), counts[i * 2 + 1].GetInt32());
            }

            //An unrecognised cache is a legitimate outcome, but the run should say so out loud
            //rather than leaving the reader to infer it from a name.
            _output.WriteLine("Export stamped: " + cache.GetProperty("kind").GetString() +
                " - " + cache.GetProperty("name").GetString());
        }

        /// <summary>
        ///     Every billboard the reference table declares is in the export, and its opcode stream
        ///     is byte for byte what a fresh decode produces.
        /// </summary>
        /// <remarks>
        ///     Asserted without an <c>or</c>. A count that allowed failures to make up the shortfall
        ///     would score a record that would not decode exactly like one that did, and a cache
        ///     whose billboards had all stopped decoding would pass it unchanged.
        /// </remarks>
        [RealCacheFact]
        public void EveryDeclaredBillboardIsExportedWithTheOpcodeStreamAFreshDecodeProduces()
        {
            string destination = Export();

            using JsonDocument manifest = Read(Path.Combine(
                IndexDirectory(destination, RSConstants.CONFIG_BILLBOARD), "manifest.json"));

            int declared = _fixture.DeclaredFiles(RSConstants.CONFIG_BILLBOARD);

            Assert.Equal(0, manifest.RootElement.GetProperty("recordsThatWouldNotDecode").GetInt32());
            Assert.Equal(declared, manifest.RootElement.GetProperty("recordsWritten").GetInt32());

            var groups = new GroupReader(_fixture.OpenCache(), RSConstants.CONFIG_BILLBOARD);
            int compared = 0;

            foreach (JsonElement entry in Records(IndexDirectory(destination, RSConstants.CONFIG_BILLBOARD)))
            {
                JsonElement address = entry.GetProperty("address");
                int group = address.GetProperty("group").GetInt32();
                int file = address.GetProperty("file").GetInt32();

                JagStream payload = groups.File(group, file);
                BillboardDefinition fresh = new BillboardDefinition { Id = file }.Decode(payload);

                AssertOpcodesMatch(fresh.Opcodes, Opcodes(entry.GetProperty("record")), group, file);
                compared++;
            }

            Assert.Equal(declared, compared);
        }

        /// <summary>
        ///     Every spot animation's exported model reference names the id its record stores, and
        ///     says truthfully whether index 7 declares it.
        /// </summary>
        /// <remarks>
        ///     The reference rows are the export's own interpretation, so they are the part most
        ///     worth checking against the record they were derived from. Existence is checked against
        ///     the reference table rather than against a decode, which is the same definition the
        ///     export uses and the same one the client applies.
        /// </remarks>
        [RealCacheFact]
        public void EverySpotAnimationModelReferenceNamesTheIdItsRecordStores()
        {
            string destination = Export();

            RSCache cache = _fixture.OpenCache();
            var modelGroups = new HashSet<int>(cache.EnumerateGroups(RSConstants.MODELS_INDEX));
            var groups = new GroupReader(cache, RSConstants.GRAPHICS_INDEX);
            int checkedRows = 0;

            foreach (JsonElement entry in Records(IndexDirectory(destination, RSConstants.GRAPHICS_INDEX)))
            {
                JsonElement address = entry.GetProperty("address");
                int group = address.GetProperty("group").GetInt32();
                int file = address.GetProperty("file").GetInt32();

                GraphicDefinition fresh = new GraphicDefinition().Decode(groups.File(group, file));

                JsonElement? model = ReferenceNamed(entry, "modelId");
                if (fresh.ModelId < 0)
                {
                    Assert.False(model.HasValue,
                        "Spot animation " + group + "/" + file + " stores no model, so the export" +
                        " must not claim a model reference for it.");
                    continue;
                }

                Assert.True(model.HasValue,
                    "Spot animation " + group + "/" + file + " stores model " + fresh.ModelId +
                    " and the export resolved no reference for it.");

                Assert.Equal(fresh.ModelId, model!.Value.GetProperty("id").GetInt32());
                Assert.Equal(RSConstants.MODELS_INDEX, model.Value.GetProperty("targetIndex").GetInt32());
                Assert.Equal(modelGroups.Contains(fresh.ModelId),
                    model.Value.GetProperty("exists").GetBoolean());

                checkedRows++;
            }

            Assert.True(checkedRows > 0, "No spot animation named a model, so nothing was checked.");
        }

        /// <summary>
        ///     The export writes nothing into the cache directory.
        /// </summary>
        /// <remarks>
        ///     The cache is read only and an export is the operation most likely to forget it, since
        ///     it is the only whole-cache walk that also writes. Compared by name and last-write
        ///     time, which catches a file added and a file rewritten in place.
        /// </remarks>
        [RealCacheFact]
        public void TheExportLeavesTheCacheDirectoryUntouched()
        {
            Dictionary<string, DateTime> before = Snapshot(RealCacheLocator.Directory);

            Export();

            Dictionary<string, DateTime> after = Snapshot(RealCacheLocator.Directory);

            Assert.Equal(before.Count, after.Count);
            foreach (KeyValuePair<string, DateTime> file in before)
            {
                Assert.True(after.ContainsKey(file.Key), "The export removed " + file.Key + ".");
                Assert.Equal(file.Value, after[file.Key]);
            }
        }

        /// <summary>The last-write time of every file in a directory, by name.</summary>
        /// <param name="directory">The directory.</param>
        /// <returns>File name to last-write time.</returns>
        private static Dictionary<string, DateTime> Snapshot(string directory)
        {
            var files = new Dictionary<string, DateTime>(StringComparer.OrdinalIgnoreCase);

            foreach (string path in Directory.GetFiles(directory))
                files[Path.GetFileName(path)] = File.GetLastWriteTimeUtc(path);

            return files;
        }

        /// <summary>
        ///     Exports the sample indexes once for the whole class.
        /// </summary>
        /// <remarks>
        ///     Once rather than per test, because four exports of the same indexes would decode the
        ///     same groups four times to prove four different things about one output. The
        ///     destination is a fresh temporary directory and is left in place, so a failing run can
        ///     be inspected.
        /// </remarks>
        /// <returns>The export root.</returns>
        private string Export()
        {
            lock (ExportGate)
            {
                if (_destination != null)
                    return _destination;

                string destination = Path.Combine(Path.GetTempPath(),
                    "flasheditor-export-" + Guid.NewGuid().ToString("N"));

                var options = new CacheExportOptions
                {
                    Destination = destination,
                    Indexes = ExportedIndexes,
                    IncludeModelReferences = false
                };

                new CacheExporter(_fixture.OpenCache(), RealCacheLocator.Directory, options).Run();

                _output.WriteLine("Export written to " + destination);
                _destination = destination;
                return destination;
            }
        }

        /// <summary>Guards the one-time export against xunit running two classes at once.</summary>
        private static readonly object ExportGate = new object();

        /// <summary>Where the export was written, or null before the first test ran.</summary>
        private static string _destination;

        /// <summary>The directory an index was exported into.</summary>
        /// <remarks>
        ///     Found by its numeric prefix rather than by rebuilding the name, so a change to how the
        ///     exporter names a directory does not silently make this test look at nothing.
        /// </remarks>
        /// <param name="destination">The export root.</param>
        /// <param name="indexId">The index.</param>
        /// <returns>The directory.</returns>
        private static string IndexDirectory(string destination, int indexId)
        {
            string prefix = "index-" + indexId.ToString("D2");

            foreach (string directory in Directory.GetDirectories(destination))
                if (Path.GetFileName(directory).StartsWith(prefix, StringComparison.Ordinal))
                    return directory;

            throw new DirectoryNotFoundException(
                "The export holds no directory for index " + indexId + " under " + destination);
        }

        /// <summary>Every record entry in every part file under an index directory.</summary>
        /// <remarks>
        ///     The whole entry - address, record and references - because the references sit beside
        ///     the record rather than inside it. Cloned, because the document that owns the element
        ///     is disposed before the caller sees it.
        /// </remarks>
        /// <param name="indexDirectory">The index's directory.</param>
        /// <returns>Each entry.</returns>
        private static IEnumerable<JsonElement> Records(string indexDirectory)
        {
            foreach (string part in Directory.GetFiles(indexDirectory, "part-*.json",
                SearchOption.AllDirectories))
            {
                //Parsed whole rather than streamed: a part is bounded by design.
                using JsonDocument document = Read(part);

                foreach (JsonElement record in document.RootElement.GetProperty("records").EnumerateArray())
                    yield return record.Clone();
            }
        }

        /// <summary>
        ///     Hands out the files of one index a group at a time, keeping the last group decoded.
        /// </summary>
        /// <remarks>
        ///     A test that called <see cref="RSCache.ReadGroup"/> per record would re-inflate and
        ///     re-decode a group once for every record in it, which is the same cost the export
        ///     exists to avoid paying. Records arrive in group order, so one group of memory is
        ///     enough.
        /// </remarks>
        private sealed class GroupReader
        {
            private readonly RSCache _cache;
            private readonly int _indexId;
            private IReadOnlyDictionary<int, JagStream> _files = new Dictionary<int, JagStream>();
            private int _groupId = -1;

            /// <summary>Reads one index.</summary>
            /// <param name="cache">The open cache.</param>
            /// <param name="indexId">The index.</param>
            public GroupReader(RSCache cache, int indexId)
            {
                _cache = cache;
                _indexId = indexId;
            }

            /// <summary>One file, positioned at its start.</summary>
            /// <param name="groupId">The group.</param>
            /// <param name="fileId">The file within it.</param>
            /// <returns>The stored payload.</returns>
            public JagStream File(int groupId, int fileId)
            {
                if (groupId != _groupId)
                {
                    _files = _cache.ReadGroup(_indexId, groupId);
                    _groupId = groupId;
                }

                JagStream payload = _files[fileId];
                payload.Seek0();
                return payload;
            }
        }

        /// <summary>
        ///     The opcode array of an exported record, following the listing wrapper when there is one.
        /// </summary>
        /// <remarks>
        ///     Some descriptors produce the decoded definition directly and others wrap it in a
        ///     listing that exposes it as <c>record</c>. Descending until the opcodes appear keeps
        ///     this test from having to know which is which.
        /// </remarks>
        /// <param name="record">The exported record body.</param>
        /// <returns>The opcode array.</returns>
        private static JsonElement Opcodes(JsonElement record)
        {
            JsonElement current = record;

            for (int depth = 0; depth < 4; depth++)
            {
                if (current.TryGetProperty("opcodes", out JsonElement opcodes))
                    return opcodes;

                if (!current.TryGetProperty("record", out JsonElement inner))
                    break;

                current = inner;
            }

            throw new InvalidOperationException("The exported record carries no opcode stream.");
        }

        /// <summary>The reference row for a named field, or null when the export resolved none.</summary>
        /// <param name="entry">The exported entry, which holds the references beside the record.</param>
        /// <param name="field">The field name.</param>
        /// <returns>The row, or null.</returns>
        private static JsonElement? ReferenceNamed(JsonElement entry, string field)
        {
            if (!entry.TryGetProperty("references", out JsonElement references))
                return null;

            foreach (JsonElement reference in references.EnumerateArray())
                if (reference.GetProperty("field").GetString() == field)
                    return reference;

            return null;
        }

        /// <summary>Requires an exported opcode stream to match a freshly decoded one exactly.</summary>
        /// <param name="fresh">The stream a fresh decode recorded.</param>
        /// <param name="exported">The array the export wrote.</param>
        /// <param name="group">The group, for the failure message.</param>
        /// <param name="file">The file, for the failure message.</param>
        private static void AssertOpcodesMatch(FlashEditor.Definitions.OpcodeStream fresh,
            JsonElement exported, int group, int file)
        {
            string where = group + "/" + file;

            Assert.True(fresh.Count == exported.GetArrayLength(),
                "Record " + where + " decoded " + fresh.Count + " opcodes and the export wrote " +
                exported.GetArrayLength() + ".");

            for (int i = 0; i < fresh.Count; i++)
            {
                Assert.Equal(fresh[i].Opcode, exported[i].GetProperty("opcode").GetInt32());
                Assert.Equal(Convert.ToHexString(fresh[i].Payload).ToLowerInvariant(),
                    exported[i].GetProperty("payload").GetString());
            }
        }

        /// <summary>Parses a JSON file.</summary>
        /// <param name="path">The file.</param>
        /// <returns>The document, which the caller disposes.</returns>
        private static JsonDocument Read(string path)
        {
            return JsonDocument.Parse(File.ReadAllText(path));
        }
    }
}
