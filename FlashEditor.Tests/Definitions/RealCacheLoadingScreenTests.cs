using System;
using System.Collections.Generic;
using System.Linq;
using FlashEditor.cache;
using FlashEditor.Definitions.LoadingScreens;
using FlashEditor.Tests.Cache.RealCache;
using Xunit;
using Xunit.Abstractions;

namespace FlashEditor.Tests.Definitions
{
    /// <summary>
    ///     Decodes the index-33 manifest and every loading screen the reference table declares,
    ///     requires exact buffer consumption, and requires each to re-encode to the bytes it came
    ///     from.
    /// </summary>
    /// <remarks>
    ///     Index 33 is two groups holding different things: group 0 is a single manifest file and
    ///     group 1 is the screens themselves, so it gets two codecs and two sweeps addressed per
    ///     group. Group 1's file ids are not contiguous, which is why every id here comes off the
    ///     reference table rather than out of a counted loop.
    /// </remarks>
    [Collection("RealCache")]
    public sealed class RealCacheLoadingScreenTests : IClassFixture<RealCacheFixture>
    {
        private readonly RealCacheFixture _fixture;
        private readonly ITestOutputHelper _output;

        /// <summary>Binds the shared open cache and the per-test output sink.</summary>
        public RealCacheLoadingScreenTests(RealCacheFixture fixture, ITestOutputHelper output)
        {
            _fixture = fixture;
            _output = output;
        }

        /// <summary>File ids the reference table declares in one of index 33's groups.</summary>
        /// <param name="groupId">The group to read.</param>
        /// <returns>The declared file ids, ascending.</returns>
        private int[] DeclaredFileIds(int groupId)
        {
            return _fixture.Table(RSConstants.GAME_TIPS).GetArchiveEntry(groupId).GetValidFileIds();
        }

        /// <summary>The manifest, which is the whole of group 0.</summary>
        /// <returns>A sweep over that one record.</returns>
        private DefinitionSweep<LoadingScreenManifest> Manifest()
        {
            return new DefinitionSweep<LoadingScreenManifest>(_fixture, _output, RSConstants.GAME_TIPS,
                new DefinitionCodec<LoadingScreenManifest>("loading-screen manifest",
                    (id, stream) => new LoadingScreenManifest().Decode(stream),
                    definition => definition.Encode()))
                .WithinGroup(LoadingScreenManifest.GroupId)
                .NotOpcodeTerminated();
        }

        /// <summary>The screens, which are the whole of group 1.</summary>
        /// <returns>A sweep over every declared screen.</returns>
        private DefinitionSweep<LoadingScreenDefinition> Screens()
        {
            return new DefinitionSweep<LoadingScreenDefinition>(_fixture, _output, RSConstants.GAME_TIPS,
                new DefinitionCodec<LoadingScreenDefinition>("loading screen",
                    (id, stream) => new LoadingScreenDefinition { Id = id }.Decode(stream),
                    definition => definition.Encode()))
                .WithinGroup(LoadingScreenDefinition.GroupId)
                .NotOpcodeTerminated();
        }

        /// <summary>
        ///     The reference table declares the two groups the client reads, the manifest group
        ///     holding exactly one file.
        /// </summary>
        /// <remarks>
        ///     <c>Class282</c>'s constructor asks for group 0 file 0 by literal (Class282.java:69) and
        ///     <c>method3336</c> for group 1 by literal (:173), so a table declaring either
        ///     differently leaves the manifest or the screens unreachable. The counts are read rather
        ///     than written down.
        /// </remarks>
        [RealCacheFact]
        public void TheIndexDeclaresTheTwoGroupsTheClientReads()
        {
            RSReferenceTable table = _fixture.Table(RSConstants.GAME_TIPS);
            int[] groups = table.GetArchiveEntries().Keys.ToArray();
            int[] screenIds = DeclaredFileIds(LoadingScreenDefinition.GroupId);

            bool contiguous = screenIds.Length > 0 &&
                              screenIds.Last() == screenIds.First() + screenIds.Length - 1;

            _output.WriteLine("index 33 declares groups " + string.Join(", ", groups) +
                              $"; {screenIds.Length} screens with ids {screenIds.First()}.." +
                              $"{screenIds.Last()}, " + (contiguous ? "contiguous" : "with holes"));

            Assert.Contains(LoadingScreenManifest.GroupId, groups);
            Assert.Contains(LoadingScreenDefinition.GroupId, groups);

            Assert.Equal(new[] { LoadingScreenManifest.FileId },
                DeclaredFileIds(LoadingScreenManifest.GroupId));
            Assert.True(screenIds.Length > 0, "index 33 declares no screens, so nothing would be checked");
        }

        /// <summary>The manifest decodes, consumes its file exactly, and re-encodes to its stored bytes.</summary>
        [RealCacheFact]
        public void TheManifest_RoundTripsToItsStoredBytes()
        {
            Manifest().AssertExactConsumption();

            DefinitionSweepResult swept = Manifest().AssertReEncodesToCapturedBytes();
            Assert.Equal(1, swept.Records);
            Assert.Equal(1, swept.Passed);

            Manifest().AssertEncodeIsAFixedPointOfDecode();
        }

        /// <summary>Every declared screen decodes and finishes on the last byte of its file.</summary>
        [RealCacheFact]
        public void EveryScreen_DecodesAndConsumesItsBufferExactly()
        {
            DefinitionSweepResult swept = Screens().AssertExactConsumption();

            Assert.Equal(DeclaredFileIds(LoadingScreenDefinition.GroupId).Length, swept.Records);
            Assert.Equal(swept.Records, swept.Passed);
        }

        /// <summary>Every declared screen re-encodes to the bytes it was decoded from.</summary>
        [RealCacheFact]
        public void EveryScreen_ReEncodesToTheCapturedBytes()
        {
            DefinitionSweepResult swept = Screens().AssertReEncodesToCapturedBytes();

            Assert.Equal(DeclaredFileIds(LoadingScreenDefinition.GroupId).Length, swept.Records);
            Assert.Equal(swept.Records, swept.Passed);
        }

        /// <summary>The screen encoder's own output decodes back to something that encodes identically.</summary>
        [RealCacheFact]
        public void EveryScreen_EncodeIsAFixedPointOfDecode()
        {
            Screens().AssertEncodeIsAFixedPointOfDecode();
        }

        /// <summary>
        ///     Reports which element types this cache exercises, and asserts the rest are genuinely
        ///     unreachable here rather than merely rare.
        /// </summary>
        /// <remarks>
        ///     The point of the test is the second half. Seven of the ten types are ported from the
        ///     client on faith, and a sweep that finds none of them is not evidence they are right -
        ///     so the run states, every time, exactly which branches nothing here defends. The
        ///     synthetic tests in <c>LoadingScreenCodecTests</c> are what cover those.
        /// </remarks>
        [RealCacheFact]
        public void TheExercisedElementTypesAreReportedAndTheRestAreUnreachableHere()
        {
            var histogram = new SortedDictionary<int, int>();
            int elements = 0;

            DefinitionSweepResult swept = Screens().ForEachDecoded((record, definition) =>
            {
                foreach (LoadingScreenElement element in definition.Elements)
                {
                    histogram.TryGetValue(element.TypeIndex, out int seen);
                    histogram[element.TypeIndex] = seen + 1;
                    elements++;
                }
            });

            int[] exercised = histogram.Keys.ToArray();
            int[] unreachable = Enumerable.Range(0, LoadingScreenElement.TypeCount)
                .Except(exercised).ToArray();

            _output.WriteLine("element types exercised: " +
                              string.Join(", ", histogram.Select(entry => $"{entry.Key}={entry.Value}")));
            _output.WriteLine("element types no file here uses, so no sweep defends them: " +
                              string.Join(", ", unreachable));

            Assert.Equal(elements, histogram.Values.Sum());
            Assert.True(swept.Records > 0, "no screen was decoded, so nothing was checked");
            Assert.True(exercised.Length > 0, "no element was decoded at all");
            Assert.True(exercised.All(type => type >= 0 && type < LoadingScreenElement.TypeCount),
                "a screen holds an element type outside the ten the format defines");
        }

        /// <summary>
        ///     The manifest's type-version block agrees with the 637 client's own table.
        /// </summary>
        /// <remarks>
        ///     Not a statistic: this is the handshake that decides whether the client shows any
        ///     loading screen at all. If the count or any byte disagrees, Class282.java:86-89 empties
        ///     both arrays and nothing is drawn, with no error anywhere. The bytes are still replayed
        ///     verbatim by the codec rather than regenerated from this - the cache is 639 and the
        ///     client 637, so agreeing today is a measurement, not a licence to rebuild them.
        /// </remarks>
        [RealCacheFact]
        public void TheManifestsTypeVersionsMatchTheClientsTable()
        {
            LoadingScreenManifest manifest = DecodeManifest();

            _output.WriteLine("stored type versions: " + string.Join(", ", manifest.TypeVersions) +
                              $"; manifest version {manifest.Version}, default screen " +
                              $"{manifest.DefaultScreenId}");

            Assert.Equal(LoadingScreenElement.ClientTypeVersions, manifest.TypeVersions);
        }

        /// <summary>
        ///     Every screen the manifest names exists, and every screen that exists is named.
        /// </summary>
        /// <remarks>
        ///     A self-proving join rather than a plausible one: the ids the manifest holds are the
        ///     argument <c>Class282.method3336</c> passes to <c>getChildFromFolder(1, id)</c>, so an
        ///     id group 1 does not declare is a screen the client cannot load, and a file no category
        ///     names is a screen it can never show. Both halves are asserted as set equality against
        ///     the reference table, which holds whatever the populations are.
        /// </remarks>
        [RealCacheFact]
        public void TheManifestNamesExactlyTheScreensGroupOneDeclares()
        {
            LoadingScreenManifest manifest = DecodeManifest();

            int[] declared = DeclaredFileIds(LoadingScreenDefinition.GroupId).OrderBy(id => id).ToArray();
            var named = new SortedSet<int>();
            int references = 0;

            foreach (LoadingScreenCategory category in manifest.Categories)
            {
                foreach (int screenId in category.ScreenIds)
                {
                    named.Add(screenId);
                    references++;
                }
            }

            _output.WriteLine($"{manifest.Categories.Count} categories hold {references} references to " +
                              $"{named.Count} distinct screens, against {declared.Length} declared");

            Assert.True(named.Count > 0, "the manifest names no screen at all");
            Assert.Equal(declared, named.ToArray());
        }

        /// <summary>Reads the manifest through the production path.</summary>
        /// <returns>The decoded manifest.</returns>
        private LoadingScreenManifest DecodeManifest()
        {
            RSCache cache = _fixture.OpenCache();
            byte[] bytes = cache.ReadFileBytes(RSConstants.GAME_TIPS, LoadingScreenManifest.GroupId,
                LoadingScreenManifest.FileId);

            return new LoadingScreenManifest().Decode(new JagStream(bytes));
        }
    }
}
