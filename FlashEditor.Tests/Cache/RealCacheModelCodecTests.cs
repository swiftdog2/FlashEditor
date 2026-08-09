using FlashEditor.Cache;
using FlashEditor.Definitions;
using FlashEditor.Tests.Cache.RealCache;
using System;
using System.Collections.Generic;
using System.Linq;
using Xunit;
using Xunit.Abstractions;
using FlashEditor.IO;

namespace FlashEditor.Tests.Cache
{
    /// <summary>
    ///     Re-encodes every model index 7 declares and requires the stored bytes back.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///     This is index 7's primary regression detector, and it is deliberately one pass rather
    ///     than the usual family of sweeps. The index inflates to roughly 260 MB across some 63,600
    ///     groups, so reading it is the expensive half by a wide margin and reading it four times to
    ///     ask four questions would cost four times as much for no more information. One model is
    ///     held at a time and dropped before the next is read, so the peak is one archive rather
    ///     than the index.
    ///     </para>
    ///     <para>
    ///     Nothing here is sampled. <c>FLASHEDITOR_TEST_CACHE_FULL</c> gates
    ///     <see cref="RealCacheFixture.ArchivesToExamine"/>, which this does not call: the claim
    ///     being made is about every model the reference table declares, and a sampled run cannot
    ///     make it.
    ///     </para>
    ///     <para>
    ///     What it cannot defend is anything whose triggering input is absent from both caches - a
    ///     widened smart, a gap before the footer, the new-protocol smart skin blocks. Those are
    ///     pinned synthetically by <c>ModelCodecTests</c>, and the counts this test reports are what
    ///     say they are absent rather than merely untested.
    ///     </para>
    /// </remarks>
    public class RealCacheModelCodecTests : IClassFixture<RealCacheFixture>
    {
        /// <summary>Failing models listed before the report is truncated.</summary>
        private const int MaxReportedFailures = 20;

        private readonly RealCacheFixture _cache;
        private readonly ITestOutputHelper _output;

        /// <summary>Binds the shared open cache and the per-test output sink.</summary>
        /// <param name="cache">The shared open cache.</param>
        /// <param name="output">Where the coverage lines go.</param>
        public RealCacheModelCodecTests(RealCacheFixture cache, ITestOutputHelper output)
        {
            _cache = cache;
            _output = output;
        }

        /// <summary>
        ///     Every model the reference table declares must re-encode to the bytes it was read from.
        /// </summary>
        /// <remarks>
        ///     The comparison is against the unpacked file rather than the stored container, because
        ///     no GZip container in either cache re-encodes byte-identically and every group on this
        ///     index is GZip.
        /// </remarks>
        [RealCacheFact]
        public void EveryModel_ReEncodesToItsStoredBytesAndReportsWhatItCovered()
        {
            RSReferenceTable table = _cache.Table(RSConstants.MODELS_INDEX);
            CacheAddressing addressing = CacheAddressing.For(RSConstants.MODELS_INDEX);
            var declaredGroups = new List<int>(table.GetArchiveEntries().Keys);

            Assert.True(declaredGroups.Count > 0,
                "index 7 declares no groups, so the sweep would pass without checking anything");

            var failures = new List<string>();
            var census = new SortedDictionary<string, long>();
            int compared = 0;
            int identical = 0;
            long payloadBytes = 0;

            foreach (int groupId in declaredGroups)
            {
                int[] fileIds = table.GetArchiveEntry(groupId)?.GetValidFileIds();
                if (fileIds == null || fileIds.Length == 0)
                {
                    Add(failures, $"model {groupId}: the reference table declares no file for it");
                    continue;
                }

                if (fileIds.Length != 1)
                {
                    Add(failures, $"model {groupId}: declares {fileIds.Length} files, but a model " +
                                  "group holds exactly one and the client fetches it by group id alone");
                    continue;
                }

                byte[] stored = _cache.RawContainer(RSConstants.MODELS_INDEX, groupId);
                if (stored == null)
                {
                    Add(failures, $"model {groupId}: declared by the reference table but its index " +
                                  "record is empty");
                    continue;
                }

                byte[] bytes;
                try
                {
                    RSContainer container = _cache.TryDecodeContainer(RSConstants.MODELS_INDEX, groupId, stored);
                    if (container == null)
                    {
                        Add(failures, $"model {groupId}: container would not decode");
                        continue;
                    }

                    RSArchive archive = RSArchive.Decode(container.GetStream(), fileIds);
                    bytes = archive.GetFile(fileIds[0])?.ToArray();
                }
                catch (Exception ex)
                {
                    Add(failures, $"model {groupId}: could not be read - {ex.GetType().Name}: {ex.Message}");
                    continue;
                }

                if (bytes == null || bytes.Length == 0)
                {
                    Add(failures, $"model {groupId}: unpacked to no bytes at all");
                    continue;
                }

                int modelId = addressing.DefinitionId(groupId, fileIds[0]);

                ModelFile file;
                byte[] again;
                try
                {
                    file = ModelCodec.Decode(bytes, modelId);
                    again = ModelCodec.Encode(file).ToArray();
                }
                catch (Exception ex)
                {
                    Add(failures, $"model {modelId}: codec threw {ex.GetType().Name}: {ex.Message}");
                    continue;
                }

                compared++;
                payloadBytes += bytes.Length;

                if (again.AsSpan().SequenceEqual(bytes))
                    identical++;
                else
                    Add(failures, Describe(modelId, bytes, again, file));

                Tally(census, file);
            }

            _output.WriteLine($"{identical} of {compared} models re-encoded to their stored bytes, " +
                              $"{payloadBytes} bytes of model payload across {declaredGroups.Count} " +
                              "declared groups");
            foreach (KeyValuePair<string, long> entry in census)
                _output.WriteLine($"  {entry.Key} = {entry.Value}");

            AssertNoFailures(failures);
            Assert.Equal(declaredGroups.Count, compared);
            Assert.Equal(compared, identical);

            AssertStructure(census, compared);
            AssertCensus(census);
        }

        /// <summary>
        ///     Relationships that hold whatever cache is loaded, so an unrecognised one is still
        ///     defended.
        /// </summary>
        /// <remarks>
        ///     The two histograms have to account for every model, and no textured face may carry a
        ///     type the client has no branch for - it reads types 0 to 3 and silently ignores
        ///     anything else, which would leave a block partly unread and every offset after it
        ///     wrong.
        /// </remarks>
        /// <param name="census">The measured figures.</param>
        /// <param name="compared">How many models the sweep covered.</param>
        private static void AssertStructure(IReadOnlyDictionary<string, long> census, int compared)
        {
            long encodings = Sum(census, "model.encoding.");
            Assert.True(encodings == compared,
                $"the encoding histogram accounts for {encodings} models, not {compared}");

            long formats = Sum(census, "model.formatType.");
            Assert.True(formats == compared,
                $"the format-type histogram accounts for {formats} models, not {compared}");

            census.TryGetValue("model.texturedFaces.other", out long strayTypes);
            Assert.True(strayTypes == 0,
                $"{strayTypes} textured faces carry a type outside 0-3, which the client reads no " +
                "block for at all");
        }

        /// <summary>
        ///     Figures that belong to one cache rather than to build 639.
        /// </summary>
        /// <remarks>
        ///     Each is a population, so each moves with the content. Three of them are the ones that
        ///     matter most and are recorded as zero on both caches: a smart stored wider than it
        ///     needed, a gap between the data and the footer, and a textured face of type 1-3 sitting
        ///     after a type-0 one. The last is what the client itself depends on - its projection
        ///     arrays are sized by the type 1-3 count and indexed by the textured-face index
        ///     (Model.java:503-508 against :674), so a model that mixed the two orders would put the
        ///     client out of bounds. 19,802 models in the vanilla capture carry both kinds and every
        ///     one of them sorts types 1-3 first.
        /// </remarks>
        /// <param name="census">The measured figures.</param>
        private void AssertCensus(IReadOnlyDictionary<string, long> census)
        {
            foreach (string key in CensusKeys)
            {
                census.TryGetValue(key, out long measured);
                _cache.Profile.AssertCensus(_output, key, measured);
            }
        }

        /// <summary>
        ///     Every figure the sweep reports, listed rather than derived from what was measured.
        /// </summary>
        /// <remarks>
        ///     A key that measured zero is absent from the census dictionary, and a figure that
        ///     silently stopped being reported is exactly the kind of coverage loss this is meant to
        ///     catch - so the list is fixed and a missing key is asserted as zero.
        /// </remarks>
        private static readonly string[] CensusKeys =
        {
            "model.encoding.legacy",
            "model.encoding.newer",
            "model.encoding.newProtocol",
            "model.formatType.12",
            "model.formatType.14",
            "model.formatType.15",
            "model.formatType.16",
            "model.withEmbeddedFormatType",
            "model.embeddedFormatTypeIs12",
            "model.withParticleTail",
            "model.emitters",
            "model.effectors",
            "model.withBondTail",
            "model.bonds",
            "model.texturedFaces.type0",
            "model.texturedFaces.type1",
            "model.texturedFaces.type2",
            "model.texturedFaces.type3",
            "model.withType1To3Faces",
            "model.withType13FaceAfterType0",
            "model.withFaceSkins",
            "model.faceSkinsAbove127",
            "model.withVertexSkins",
            "model.vertexSkinsAbove127",
            "model.withScaleBlockSlack",
            "model.scaleBlockSlackBytes",
            "model.widenedSmarts",
            "model.withGapBeforeFooter"
        };

        private static void Tally(SortedDictionary<string, long> census, ModelFile file)
        {
            Bump(census, "model.encoding." + Name(file.Encoding));
            Bump(census, "model.formatType." + file.FormatType);

            if (file.HasEmbeddedFormatType)
            {
                Bump(census, "model.withEmbeddedFormatType");
                if (file.FormatType == 12)
                    Bump(census, "model.embeddedFormatTypeIs12");
            }

            if (file.Emitters != null)
            {
                Bump(census, "model.withParticleTail");
                Bump(census, "model.emitters", file.Emitters.Length);
                Bump(census, "model.effectors", file.Effectors!.Length);
            }

            if (file.Bonds != null)
            {
                Bump(census, "model.withBondTail");
                Bump(census, "model.bonds", file.Bonds.Length);
            }

            TallyTexturedFaces(census, file);

            if (file.FaceSkins != null)
            {
                Bump(census, "model.withFaceSkins");
                if (AnyAbove127(file.FaceSkins))
                    Bump(census, "model.faceSkinsAbove127");
            }

            if (file.VertexSkins != null)
            {
                Bump(census, "model.withVertexSkins");
                if (AnyAbove127(file.VertexSkins))
                    Bump(census, "model.vertexSkinsAbove127");
            }

            if (file.SlackTextureScale != null && file.SlackTextureScale.Length > 0)
            {
                Bump(census, "model.withScaleBlockSlack");
                Bump(census, "model.scaleBlockSlackBytes", file.SlackTextureScale.Length);
            }

            if (file.Gap != null && file.Gap.Length > 0)
                Bump(census, "model.withGapBeforeFooter");

            long widened = Widened(file.VertexDeltasX) + Widened(file.VertexDeltasY) +
                           Widened(file.VertexDeltasZ) + Widened(file.FaceIndexDeltas);
            if (widened > 0)
                Bump(census, "model.widenedSmarts", widened);
        }

        private static void TallyTexturedFaces(SortedDictionary<string, long> census, ModelFile file)
        {
            int textured = file.TexturedFaceCount;
            if (textured == 0)
                return;

            int type1To3 = file.Type1To3FaceCount;
            bool outOfOrder = false;

            for (int i = 0; i < textured; i++)
            {
                //The legacy encoding has no type block at all and the client fills it with zeroes.
                int type = file.TextureTypes == null ? 0 : file.TextureTypes[i];
                if (type >= 0 && type <= 3)
                    Bump(census, "model.texturedFaces.type" + type);
                else
                    Bump(census, "model.texturedFaces.other");

                if (type >= 1 && type <= 3 && i >= type1To3)
                    outOfOrder = true;
            }

            if (type1To3 > 0)
                Bump(census, "model.withType1To3Faces");
            if (outOfOrder)
                Bump(census, "model.withType13FaceAfterType0");
        }

        private static bool AnyAbove127(StoredSmart[] values)
        {
            foreach (StoredSmart value in values)
            {
                if (value.Value > 127)
                    return true;
            }
            return false;
        }

        private static long Widened(StoredSmart[] values)
        {
            if (values == null)
                return 0;

            long widened = 0;
            foreach (StoredSmart value in values)
            {
                if (value.Width == JagStream.SmartWidth.TwoByte && value.Value >= -64 && value.Value <= 63)
                    widened++;
            }
            return widened;
        }

        private static string Name(ModelEncoding encoding)
        {
            switch (encoding)
            {
                case ModelEncoding.Legacy:
                    return "legacy";
                case ModelEncoding.Newer:
                    return "newer";
                default:
                    return "newProtocol";
            }
        }

        private static void Bump(SortedDictionary<string, long> census, string key, long by = 1)
        {
            census.TryGetValue(key, out long seen);
            census[key] = seen + by;
        }

        private static long Sum(IReadOnlyDictionary<string, long> census, string prefix)
        {
            return census.Where(entry => entry.Key.StartsWith(prefix, StringComparison.Ordinal))
                         .Sum(entry => entry.Value);
        }

        /// <summary>
        ///     Describes one mismatch, without a per-byte re-decode.
        /// </summary>
        /// <remarks>
        ///     The usual opcode-boundary trace costs a decode per byte of the record. On an index
        ///     whose largest model is 91 KB that turns a failing run into a hang, so the report is a
        ///     first-difference offset and the shape of the model instead.
        /// </remarks>
        /// <param name="modelId">The model that failed.</param>
        /// <param name="stored">The bytes the cache holds.</param>
        /// <param name="reencoded">The bytes the encoder produced.</param>
        /// <param name="file">The decoded model, for its shape.</param>
        /// <returns>The failure line.</returns>
        private static string Describe(int modelId, byte[] stored, byte[] reencoded, ModelFile file)
        {
            int shared = Math.Min(stored.Length, reencoded.Length);
            int at = shared;
            for (int i = 0; i < shared; i++)
            {
                if (stored[i] != reencoded[i])
                {
                    at = i;
                    break;
                }
            }

            return $"model {modelId} ({file.Encoding}, format {file.FormatType}, " +
                   $"{file.VertexCount} vertices, {file.FaceCount} faces, " +
                   $"{file.TexturedFaceCount} textured): re-encoded {reencoded.Length} bytes from a " +
                   $"stored {stored.Length}, first difference at {at} " +
                   $"({ByteAt(stored, at)} became {ByteAt(reencoded, at)})";
        }

        private static string ByteAt(byte[] bytes, int offset)
        {
            return offset < bytes.Length ? $"0x{bytes[offset]:X2}" : "end of buffer";
        }

        /// <summary>
        ///     Records a failure, keeping only the first few so a wholesale mismatch does not build a
        ///     63,000-line string before the assertion runs.
        /// </summary>
        /// <param name="failures">The collected failures.</param>
        /// <param name="detail">What went wrong.</param>
        private static void Add(List<string> failures, string detail)
        {
            if (failures.Count < MaxReportedFailures)
                failures.Add(detail);
            else if (failures.Count == MaxReportedFailures)
                failures.Add("... and more, truncated");
        }

        private static void AssertNoFailures(List<string> failures)
        {
            if (failures.Count == 0)
                return;

            Assert.Fail("models did not survive the codec:" + Environment.NewLine +
                        string.Join(Environment.NewLine, failures));
        }
    }
}
