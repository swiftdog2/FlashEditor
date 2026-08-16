using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Text.Json;
using FlashEditor.Cache;
using FlashEditor.Definitions;
using FlashEditor.Export;
using Xunit;

namespace FlashEditor.Tests.Export
{
    /// <summary>
    ///     The parts of the structured export that can be pinned without a cache.
    /// </summary>
    /// <remarks>
    ///     Everything here is about the export's own contract rather than about any index: that it
    ///     refuses to write into the cache it is reading, that it recognises a cache from counts
    ///     rather than from a name, and that the record writer keeps the opcode stream exactly as it
    ///     was recorded. The whole-cache behaviour is pinned by
    ///     <c>Cache.RealCache.RealCacheExportTests</c>, which needs a real cache.
    /// </remarks>
    public sealed class CacheExportUnitTests
    {
        /// <summary>
        ///     The cache is read only, so the export must not be able to land inside it whatever the
        ///     caller asks for.
        /// </summary>
        [Fact]
        public void ResolveDestination_RefusesADestinationInsideTheCacheBeingRead()
        {
            string cacheDirectory = Path.Combine(Path.GetTempPath(), "flasheditor-export-cache-test");
            var options = new CacheExportOptions { Destination = Path.Combine(cacheDirectory, "export") };

            Assert.Throws<InvalidOperationException>(() => options.ResolveDestination(cacheDirectory));
        }

        /// <summary>The cache directory itself is inside itself, and is refused for the same reason.</summary>
        [Fact]
        public void ResolveDestination_RefusesTheCacheDirectoryItself()
        {
            string cacheDirectory = Path.Combine(Path.GetTempPath(), "flasheditor-export-cache-test");
            var options = new CacheExportOptions { Destination = cacheDirectory };

            Assert.Throws<InvalidOperationException>(() => options.ResolveDestination(cacheDirectory));
        }

        /// <summary>A destination elsewhere is returned, made absolute.</summary>
        [Fact]
        public void ResolveDestination_AcceptsADestinationOutsideTheCache()
        {
            string cacheDirectory = Path.Combine(Path.GetTempPath(), "flasheditor-export-cache-test");
            string destination = Path.Combine(Path.GetTempPath(), "flasheditor-export-output-test");
            var options = new CacheExportOptions { Destination = destination };

            Assert.Equal(Path.GetFullPath(destination), options.ResolveDestination(cacheDirectory));
        }

        /// <summary>
        ///     The two supported caches are recognised from their declared counts alone.
        /// </summary>
        /// <remarks>
        ///     Synthetic tables carrying only the six figures the fingerprint reads. That is the
        ///     point of the test: if recognition ever started depending on anything else - a
        ///     directory name, a table version, a payload - these tables would stop being enough and
        ///     this would fail.
        /// </remarks>
        [Theory]
        [InlineData(1067, 40883, 915, 915, 80, 20427, CacheKind.VanillaB639)]
        [InlineData(1078, 42256, 946, 946, 80, 20470, CacheKind.Repack)]
        [InlineData(1067, 40883, 915, 915, 80, 20428, CacheKind.Unrecognised)]
        public void Identify_RecognisesACacheFromItsDeclaredCounts(int interfaceGroups, int interfaceFiles,
            int textureGroups, int textureFiles, int itemGroups, int itemFiles, CacheKind expected)
        {
            var tables = new Dictionary<int, RSReferenceTable>
            {
                [RSConstants.INTERFACE_DEFINITIONS_INDEX] = Table(interfaceGroups, interfaceFiles),
                [RSConstants.TEXTURES] = Table(textureGroups, textureFiles),
                [RSConstants.ITEM_DEFINITIONS_INDEX] = Table(itemGroups, itemFiles)
            };

            CacheProvenance provenance = CacheProvenance.Identify(indexId => tables[indexId]);

            Assert.Equal(expected, provenance.Kind);
        }

        /// <summary>
        ///     A cache whose fingerprint indexes cannot be read is unrecognised rather than fatal.
        /// </summary>
        /// <remarks>
        ///     Failing to recognise a cache must never stop it being exported: an export of a cache
        ///     nobody has measured is exactly the case where the fingerprint in the header is worth
        ///     the most.
        /// </remarks>
        [Fact]
        public void Identify_ReportsUnrecognisedWhenAFingerprintIndexIsMissing()
        {
            CacheProvenance provenance = CacheProvenance.Identify(
                indexId => throw new FileNotFoundException("no table for index " + indexId));

            Assert.Equal(CacheKind.Unrecognised, provenance.Kind);
            Assert.Empty(provenance.Fingerprint);
        }

        /// <summary>
        ///     A recorded opcode stream survives the export in order, repetition included.
        /// </summary>
        /// <remarks>
        ///     The single most important thing the export writes. Every format here is
        ///     non-canonical, so the order a record was decoded in and the fact that an opcode
        ///     occurred twice exist nowhere but in this stream once the fields have been read. A
        ///     writer that sorted, deduplicated or summarised it would produce a file that looks
        ///     complete and has thrown the evidence away.
        /// </remarks>
        [Fact]
        public void WriteRecord_KeepsTheOpcodeStreamInOrderWithItsRepetitions()
        {
            var record = new RecordedStreamStub();
            record.Opcodes.Add(11, new byte[] { 0xFF });
            record.Opcodes.Add(5, Array.Empty<byte>());
            record.Opcodes.Add(11, new byte[] { 0x7F });

            using JsonDocument document = Write(record);
            JsonElement opcodes = document.RootElement.GetProperty("opcodes");

            Assert.Equal(3, opcodes.GetArrayLength());
            Assert.Equal(11, opcodes[0].GetProperty("opcode").GetInt32());
            Assert.Equal("ff", opcodes[0].GetProperty("payload").GetString());
            Assert.Equal(5, opcodes[1].GetProperty("opcode").GetInt32());
            Assert.Equal("", opcodes[1].GetProperty("payload").GetString());
            Assert.Equal(11, opcodes[2].GetProperty("opcode").GetInt32());
            Assert.Equal("7f", opcodes[2].GetProperty("payload").GetString());
        }

        /// <summary>
        ///     A record that refers back to itself is written once and marked, rather than
        ///     recursing.
        /// </summary>
        [Fact]
        public void WriteRecord_StopsAtACycleRatherThanRecursing()
        {
            var record = new CyclicStub();
            record.Self = record;

            using JsonDocument document = Write(record);

            Assert.Equal("(cycle)", document.RootElement.GetProperty("self").GetString());
        }

        /// <summary>
        ///     A long blob is summarised rather than written inline, and says that it was.
        /// </summary>
        /// <remarks>
        ///     The summary carries the length and the head, so a reader can tell a truncated value
        ///     from a short one. A silent truncation would read as the whole payload.
        /// </remarks>
        [Fact]
        public void WriteRecord_SummarisesALongBlobRatherThanTruncatingItSilently()
        {
            var record = new BlobStub { Payload = new byte[4096] };

            using JsonDocument document = Write(record);
            JsonElement payload = document.RootElement.GetProperty("payload");

            Assert.Equal(4096, payload.GetProperty("length").GetInt32());
            Assert.True(payload.TryGetProperty("elided", out _));
        }

        /// <summary>
        ///     A flag array is written as its length and the positions that are set.
        /// </summary>
        /// <remarks>
        ///     Lossless, and it is what keeps the 256-entry opcode hit map every definition carries
        ///     from being two kilobytes of <c>false</c> per record. A reader has to be able to
        ///     recover the array, so the length is written beside the positions rather than left to
        ///     be inferred from the highest one.
        /// </remarks>
        [Fact]
        public void WriteRecord_WritesAFlagArrayAsItsSetPositions()
        {
            var record = new FlagArrayStub { Decoded = new bool[256] };
            record.Decoded[1] = true;
            record.Decoded[249] = true;

            using JsonDocument document = Write(record);
            JsonElement decoded = document.RootElement.GetProperty("decoded");

            Assert.Equal(256, decoded.GetProperty("length").GetInt32());

            JsonElement set = decoded.GetProperty("setIndices");
            Assert.Equal(2, set.GetArrayLength());
            Assert.Equal(1, set[0].GetInt32());
            Assert.Equal(249, set[1].GetInt32());
        }

        /// <summary>Every join the export resolves is named in its own header.</summary>
        /// <remarks>
        ///     So a reader can see the ceiling without reading the source, and so a join added to the
        ///     extractor without being declared shows up as an inconsistency rather than as an
        ///     unexplained reference row.
        /// </remarks>
        [Fact]
        public void ResolvedJoins_AreDeclaredAndNonEmpty()
        {
            Assert.NotEmpty(CacheExportJoins.Resolved);

            foreach (string join in CacheExportJoins.Resolved)
                Assert.Contains("->", join, StringComparison.Ordinal);
        }

        /// <summary>Writes one record and parses what came out.</summary>
        /// <param name="record">The record.</param>
        /// <returns>The parsed JSON, which the caller disposes.</returns>
        private static JsonDocument Write(object record)
        {
            using var buffer = new MemoryStream();
            using (var writer = new Utf8JsonWriter(buffer))
            {
                new RecordJsonWriter(writer).WriteRecord(record);
            }

            return JsonDocument.Parse(Encoding.UTF8.GetString(buffer.ToArray()));
        }

        /// <summary>A reference table declaring a group and file count and nothing else.</summary>
        /// <param name="groups">How many groups to declare.</param>
        /// <param name="files">How many files to spread across them.</param>
        /// <returns>The table.</returns>
        private static RSReferenceTable Table(int groups, int files)
        {
            var table = new RSReferenceTable();

            for (int groupId = 0; groupId < groups; groupId++)
            {
                //Spread the files so the last group takes the remainder. Only the total is read,
                //but an even split would make a wrong total impossible to construct.
                int share = files / groups + (groupId < files % groups ? 1 : 0);
                var fileIds = new int[share];
                for (int i = 0; i < share; i++)
                    fileIds[i] = i;

                var entry = new RSArchiveEntry(groupId);
                entry.SetValidFileIds(fileIds);
                table.PutArchiveEntry(groupId, entry);
            }

            return table;
        }

        /// <summary>A record whose only content is a recorded opcode stream.</summary>
        private sealed class RecordedStreamStub
        {
            /// <summary>The recorded stream.</summary>
            public OpcodeStream Opcodes { get; } = new OpcodeStream();
        }

        /// <summary>A record that holds a reference to itself.</summary>
        private sealed class CyclicStub
        {
            /// <summary>The self reference the writer has to refuse to follow.</summary>
            public CyclicStub Self { get; set; }
        }

        /// <summary>A record holding a blob longer than the writer writes inline.</summary>
        private sealed class BlobStub
        {
            /// <summary>The blob.</summary>
            public byte[] Payload { get; set; } = Array.Empty<byte>();
        }

        /// <summary>A record carrying an opcode hit map, as the definition decoders do.</summary>
        private sealed class FlagArrayStub
        {
            /// <summary>The hit map.</summary>
            public bool[] Decoded { get; set; } = Array.Empty<bool>();
        }
    }
}
