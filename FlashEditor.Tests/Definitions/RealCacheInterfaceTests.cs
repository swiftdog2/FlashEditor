using System;
using System.Collections.Generic;
using System.Linq;
using FlashEditor.Cache;
using FlashEditor.Definitions.Interfaces;
using FlashEditor.Tests.Cache.RealCache;
using Xunit;
using Xunit.Abstractions;
using FlashEditor.IO;

namespace FlashEditor.Tests.Definitions
{
    /// <summary>
    ///     Decodes every interface component in the real revision-639 cache, requires exact buffer
    ///     consumption, and requires each one to re-encode to the bytes it came from.
    /// </summary>
    /// <remarks>
    ///     Index 3 is not an opcode stream. Nothing in a component is self-delimiting and there is no
    ///     terminator: the layout is a fixed header, a block chosen by the type byte, and a tail whose
    ///     shape is decided by an action nibble, a 24-bit mask and a version byte. So a field read one
    ///     byte too wide shifts everything after it and the record cannot land on its own last byte,
    ///     which makes exact consumption across every declared file the whole statement about the
    ///     layout. <c>NotOpcodeTerminated</c> drops the two assertions that only mean something for an
    ///     opcode format.
    ///     <para>
    ///     Every group, not the 250-group sample: "every component in the cache re-encodes to its
    ///     stored bytes" is not a claim a run over 250 of a thousand-odd groups can make.
    ///     </para>
    ///     <para>
    ///     Index 3 is one of the eleven the two supported caches disagree on - the repack holds
    ///     42,256 components across 1078 interfaces and the vanilla b639 capture 40,883 across 1067
    ///     - so the totals are read from the reference table and the content census is scoped to
    ///     the cache it was measured on. What is asserted unconditionally is every relationship
    ///     between those figures and every branch the data does or does not reach, which is what
    ///     the census was really defending.
    ///     </para>
    /// </remarks>
    [Collection("RealCache")]
    public sealed class RealCacheInterfaceTests : IClassFixture<RealCacheFixture>
    {
        private readonly RealCacheFixture _fixture;
        private readonly ITestOutputHelper _output;

        /// <summary>Binds the shared open cache and the per-test output sink.</summary>
        public RealCacheInterfaceTests(RealCacheFixture fixture, ITestOutputHelper output)
        {
            _fixture = fixture;
            _output = output;
        }

        /// <summary>Groups index 3's reference table declares, one interface each.</summary>
        private int InterfacesInCache => _fixture.DeclaredGroups(RSConstants.INTERFACE_DEFINITIONS_INDEX);

        /// <summary>Component files the table declares across every one of those groups.</summary>
        private int ComponentsInCache => _fixture.DeclaredFiles(RSConstants.INTERFACE_DEFINITIONS_INDEX);

        private DefinitionSweep<InterfaceComponentDefinition> Sweep()
        {
            return new DefinitionSweep<InterfaceComponentDefinition>(_fixture, _output,
                RSConstants.INTERFACE_DEFINITIONS_INDEX,
                new DefinitionCodec<InterfaceComponentDefinition>("interface component",
                    (id, stream) => InterfaceComponentDefinition.FromComponentId(id).Decode(stream),
                    component => component.Encode()))
                .AcrossEveryGroup()
                .NotOpcodeTerminated();
        }

        /// <summary>
        ///     Every component decodes and finishes on the last byte of its file.
        /// </summary>
        /// <remarks>
        ///     The harness decodes a padded copy as well as the genuine bytes, which is what makes
        ///     this sharp on a format with no terminator: the twenty hook arrays and five trigger
        ///     arrays that close every record are all length-prefixed, so a decoder that read one
        ///     array too few would stop short and one too many would run into the padding.
        /// </remarks>
        [RealCacheFact]
        public void EveryComponent_DecodesAndConsumesItsBufferExactly()
        {
            DefinitionSweepResult swept = Sweep().AssertExactConsumption();

            Assert.Equal(ComponentsInCache, swept.Records);
            Assert.Equal(ComponentsInCache, swept.Passed);
            Assert.Equal(InterfacesInCache, swept.Groups);
        }

        /// <summary>Every component re-encodes to the bytes it was decoded from.</summary>
        /// <remarks>
        ///     The primary regression detector for this index. The editor rewrites a component through
        ///     its encoder on every save, and the archive CRC covers the stored bytes, so anything the
        ///     encoder normalises rewrites files nobody edited and drags in the reference-table entry
        ///     of every group packed alongside them.
        /// </remarks>
        [RealCacheFact]
        public void EveryComponent_ReEncodesToTheCapturedBytes()
        {
            DefinitionSweepResult swept = Sweep().AssertReEncodesToCapturedBytes();

            Assert.Equal(ComponentsInCache, swept.Records);
            Assert.Equal(ComponentsInCache, swept.Passed);
            Assert.Equal(0, swept.Reordered);
        }

        /// <summary>The encoder's own output decodes back to something that encodes identically.</summary>
        /// <remarks>
        ///     Independent of byte identity against the cache: this one fails on a field the encoder
        ///     writes in a shape its own decoder reads differently, which is the property the save
        ///     path depends on once a component has actually been edited.
        /// </remarks>
        [RealCacheFact]
        public void EveryComponent_EncodeIsAFixedPointOfDecode()
        {
            Sweep().AssertEncodeIsAFixedPointOfDecode();
        }

        /// <summary>
        ///     What index 3 actually contains, so the codec's coverage is stated rather than assumed.
        /// </summary>
        /// <remarks>
        ///     <para>
        ///     Most of this exists to record which branches the sweeps above <b>cannot</b> defend:
        ///     the version byte is 255 on every file, so six branches never fire; the 0x80 name
        ///     flag, the extended model transform, an action high nibble above 1, an operand type
        ///     byte other than 0 or 1, a slot value of 4095 and a boolean byte other than 0 or 1
        ///     all occur zero times. Each is implemented, and <c>InterfaceComponentCodecTests</c>
        ///     is the only thing that tests it. If a cache ever introduces one, the key-set and
        ///     zero-occurrence assertions here are what say so, and they hold whichever cache is
        ///     loaded.
        ///     </para>
        ///     <para>
        ///     The populations behind them are counts of one cache's content, so they are asserted
        ///     through the profile. Every relationship between them is asserted outright instead -
        ///     each histogram sums to the record count, the parent fold accounts for every
        ///     component, the mask gate agrees with itself - which is what stops a decoder that
        ///     mis-classified records from passing on a cache whose census has not been recorded.
        ///     </para>
        /// </remarks>
        [RealCacheFact]
        public void TheInterfaceIndex_HoldsWhatTheCodecClaimsItDoes()
        {
            RSReferenceTable table = _fixture.Table(RSConstants.INTERFACE_DEFINITIONS_INDEX);
            var filesPerGroup = new Dictionary<int, HashSet<int>>();
            foreach (KeyValuePair<int, RSArchiveEntry> entry in table.GetArchiveEntries())
                filesPerGroup[entry.Key] = new HashSet<int>(entry.Value.GetValidFileIds());

            var types = new SortedDictionary<int, int>();
            var settings = new SortedDictionary<int, int>();
            var spriteFlags = new SortedDictionary<int, int>();
            var modelSettings = new SortedDictionary<int, int>();
            var actionHighNibbles = new SortedDictionary<int, int>();
            var operandTypes = new SortedDictionary<int, int>();
            var booleanBytes = new SortedDictionary<int, int>();

            long payloadBytes = 0;
            int nonIf3 = 0;
            int authoringNames = 0;
            int rootComponents = 0;
            int parentedComponents = 0;
            int danglingParents = 0;
            int nonZeroContentTypes = 0;
            int extendedModelTransforms = 0;
            int slotTables = 0;
            int slotEntries = 0;
            int slotSentinels = 0;
            int highestSlot = -1;
            int targetGated = 0;
            int targetShortsAtSentinel = 0;
            int hookArrays = 0;
            int triggerArrays = 0;
            int modelSentinels = 0;
            int animationSentinels = 0;
            int fontSentinels = 0;
            int emptySelectedActions = 0;

            DefinitionSweepResult swept = Sweep().ForEachDecoded((record, component) =>
            {
                payloadBytes += record.Bytes.Length;
                Count(types, component.ComponentType);
                Count(settings, component.SettingsFlags);
                Count(actionHighNibbles, component.ActionHighNibble);

                if (component.RawVersion != InterfaceComponentDefinition.If3Version)
                    nonIf3++;
                if (component.AuthoringName != null)
                    authoringNames++;
                if (component.ContentType != 0)
                    nonZeroContentTypes++;
                if (component.SelectedAction.IsEmpty)
                    emptySelectedActions++;

                if (component.RawParentId == InterfaceComponentDefinition.NoParent)
                {
                    rootComponents++;
                }
                else
                {
                    parentedComponents++;
                    //A stored parent is a sixteen-bit sibling index, so it has to name a file the
                    //same group actually holds. That is what settles the fold as "component raw of
                    //this same interface" rather than as a global id.
                    if (!filesPerGroup[record.GroupId].Contains(component.RawParentId))
                        danglingParents++;
                }

                if (component.ComponentType == 0)
                    Count(booleanBytes, component.LayerFlagByte);

                if (component.ComponentType == 3)
                    Count(booleanBytes, component.RectangleFilledByte);

                if (component.ComponentType == 4)
                {
                    Count(booleanBytes, component.ShadowByte);
                    if (component.RawFontId == InterfaceComponentDefinition.NoReference)
                        fontSentinels++;
                }

                if (component.ComponentType == 5)
                {
                    Count(spriteFlags, component.SpriteFlags);
                    Count(booleanBytes, component.SpriteTransform1Byte);
                    Count(booleanBytes, component.SpriteTransform2Byte);
                }

                if (component.ComponentType == 6)
                {
                    Count(modelSettings, component.ModelSettings);
                    if (component.HasExtendedModelTransform)
                        extendedModelTransforms++;
                    if (component.RawModelId == InterfaceComponentDefinition.NoReference)
                        modelSentinels++;
                    if (component.RawAnimationId == InterfaceComponentDefinition.NoReference)
                        animationSentinels++;
                }

                if (component.ComponentType == 9)
                    Count(booleanBytes, component.LineFlippedByte);

                if (component.Slots.Count > 0)
                {
                    slotTables++;
                    slotEntries += component.Slots.Count;
                    foreach (InterfaceSlotEntry slot in component.Slots)
                    {
                        highestSlot = Math.Max(highestSlot, slot.Slot);
                        if (slot.RawValue == InterfaceSlotEntry.NoValue)
                            slotSentinels++;
                    }
                }

                if (component.HasTargetShorts)
                {
                    targetGated++;
                    if (component.RawTargetVerb == InterfaceComponentDefinition.NoReference &&
                        component.RawTargetCursor == InterfaceComponentDefinition.NoReference &&
                        component.RawTargetOperand == InterfaceComponentDefinition.NoReference)
                        targetShortsAtSentinel++;
                }

                hookArrays += component.HookArrayCount;
                triggerArrays += component.TriggerArrayCount;

                foreach (InterfaceScriptOperand[] hook in component.Hooks)
                    foreach (InterfaceScriptOperand operand in hook)
                        Count(operandTypes, operand.TypeByte);
            });

            _output.WriteLine("component types: " + Histogram(types));
            _output.WriteLine("settings bytes: " + Histogram(settings));
            _output.WriteLine("sprite flag bytes: " + Histogram(spriteFlags));
            _output.WriteLine("model settings bytes: " + Histogram(modelSettings));
            _output.WriteLine("action high nibbles: " + Histogram(actionHighNibbles));
            _output.WriteLine("hook operand type bytes: " + Histogram(operandTypes));
            _output.WriteLine("boolean bytes across every == 1 field: " + Histogram(booleanBytes));

            Assert.Equal(ComponentsInCache, swept.Records);
            Assert.Equal(InterfacesInCache, swept.Groups);
            Assert.True(swept.Records > 0, "index 3 declared nothing, so nothing was counted");

            //The component type dispatch. Types 1, 2, 7 and 8 are expressible and unused, and
            //10..127 read no type block at all, so six values account for the whole index - and
            //they account for all of it, which is the half a per-type population cannot state.
            Assert.Equal(new[] { 0, 3, 4, 5, 6, 9 }, types.Keys.ToArray());
            Assert.Equal(swept.Records, types.Values.Sum());

            //Every file is if3, which is what makes the six version-gated branches untestable here.
            Assert.Equal(0, nonIf3);
            Assert.Equal(0, authoringNames);

            //The parent fold, checked on every row rather than in aggregate. Every component is
            //either a root or names a sibling its own group holds; nothing falls between.
            Assert.Equal(0, danglingParents);
            Assert.Equal(swept.Records, rootComponents + parentedComponents);

            Assert.Equal(0, fontSentinels);

            //The slot table, and the sentinel that never occurs.
            Assert.Equal(0, slotSentinels);
            Assert.True(slotEntries >= slotTables,
                "a component with a slot table holds at least one entry, so entries cannot trail tables");
            Assert.True(highestSlot <= InterfaceSlotEntry.MaxSlot,
                "Slot " + highestSlot + " is past the eleven the client's parallel arrays hold.");

            //The mask gate. All three shorts are the sentinel in every gated file, which is why the
            //gate is derived from the mask rather than recorded - and that is the assertion, not
            //how many files happen to be gated.
            Assert.Equal(targetGated, targetShortsAtSentinel);

            //The latent non-canonical cases, each pinned at zero occurrences so a cache that
            //introduces one is visible rather than silently exercising an untested branch.
            Assert.Equal(0, extendedModelTransforms);
            Assert.Equal(new[] { 0, 1 }, actionHighNibbles.Keys.ToArray());
            Assert.Equal(swept.Records, actionHighNibbles.Values.Sum());
            Assert.Equal(new[] { InterfaceScriptOperand.IntegerType, InterfaceScriptOperand.StringType },
                operandTypes.Keys.ToArray());
            Assert.Equal(new[] { 0, 1 }, booleanBytes.Keys.ToArray());
            Assert.Equal(new[] { 0, 1 }, settings.Keys.ToArray());
            Assert.Equal(new[] { 0, 1, 2, 3 }, spriteFlags.Keys.ToArray());
            Assert.Equal(new[] { 0, 1, 5, 9, 13 }, modelSettings.Keys.ToArray());

            //The populations themselves, which belong to whichever cache produced them.
            RealCacheProfile profile = _fixture.Profile;
            profile.AssertCensus(_output, "interface.payloadBytes", payloadBytes);
            foreach (KeyValuePair<int, int> type in types)
                profile.AssertCensus(_output, "interface.type." + type.Key, type.Value);
            profile.AssertCensus(_output, "interface.rootComponents", rootComponents);
            profile.AssertCensus(_output, "interface.parentedComponents", parentedComponents);
            profile.AssertCensus(_output, "interface.nonZeroContentTypes", nonZeroContentTypes);
            profile.AssertCensus(_output, "interface.emptySelectedActions", emptySelectedActions);
            profile.AssertCensus(_output, "interface.modelSentinels", modelSentinels);
            profile.AssertCensus(_output, "interface.animationSentinels", animationSentinels);
            profile.AssertCensus(_output, "interface.slotTables", slotTables);
            profile.AssertCensus(_output, "interface.slotEntries", slotEntries);
            profile.AssertCensus(_output, "interface.targetGated", targetGated);
            profile.AssertCensus(_output, "interface.hookArrays", hookArrays);
            profile.AssertCensus(_output, "interface.triggerArrays", triggerArrays);
            profile.AssertCensus(_output, "interface.actionHighNibble.0", actionHighNibbles[0]);
            profile.AssertCensus(_output, "interface.actionHighNibble.1", actionHighNibbles[1]);
            profile.AssertCensus(_output, "interface.operand.integer",
                operandTypes[InterfaceScriptOperand.IntegerType]);
            profile.AssertCensus(_output, "interface.operand.string",
                operandTypes[InterfaceScriptOperand.StringType]);
        }

        /// <summary>
        ///     Groups the reference table does not declare are intact interfaces, not garbage.
        /// </summary>
        /// <remarks>
        ///     <para>
        ///     Their file counts are stated nowhere, so each is recovered by requiring that the size
        ///     trailer parses, that the deltas sum to the body length, and that <b>every</b> resulting
        ///     file consumes exactly under the codec. Exactly one count satisfies all three per group.
        ///     That is a self-proving recovery rather than a guess, and it doubles as an independent
        ///     check on the decoder: a codec with a mis-sized field would find no working count at all.
        ///     </para>
        ///     <para>
        ///     The client cannot load them - <c>VersionTable.java:135</c> sizes its arrays to
        ///     <c>maxGroupId + 1</c> and leaves the file count at 0 for an undeclared id, which
        ///     <c>JS5Archive.method2758:1035</c> rejects - so they are dead weight in the running
        ///     game and nothing in the editor reads them. This test exists so that stays a decision
        ///     rather than an accident, and so a future enumeration written over idx3's slots meets
        ///     a documented case instead of an exception.
        ///     </para>
        ///     <para>
        ///     Which groups those are is a fact about one cache: the repack holds 772, 825 and 891
        ///     at 14, 32 and 43 files, and the vanilla b639 capture holds none at all on index 3 or
        ///     anywhere else. The subject list therefore comes from the profile, and the assertion
        ///     that the cache holds exactly that list is what keeps the test meaningful where the
        ///     list is empty.
        ///     </para>
        /// </remarks>
        [RealCacheFact]
        public void TheUndeclaredGroups_AreIntactInterfacesAtExactlyOneFileCount()
        {
            RSReferenceTable table = _fixture.Table(RSConstants.INTERFACE_DEFINITIONS_INDEX);
            IReadOnlyList<int> undeclared =
                _fixture.OpenCache().EnumerateOrphanGroups(RSConstants.INTERFACE_DEFINITIONS_INDEX);

            _output.WriteLine($"{_fixture.Profile.Name}: index 3 holds {undeclared.Count} group(s) its " +
                              $"table does not declare [{string.Join(", ", undeclared)}]");

            if (_fixture.Profile.OrphanGroups != null)
            {
                _fixture.Profile.OrphanGroups.TryGetValue(RSConstants.INTERFACE_DEFINITIONS_INDEX,
                    out int[] expected);
                Assert.Equal(expected ?? Array.Empty<int>(), undeclared.ToArray());
            }

            foreach (int groupId in undeclared)
            {
                Assert.Null(table.GetArchiveEntry(groupId));

                byte[] stored = _fixture.RawContainer(RSConstants.INTERFACE_DEFINITIONS_INDEX, groupId);
                Assert.NotNull(stored);

                RSContainer container =
                    _fixture.TryDecodeContainer(RSConstants.INTERFACE_DEFINITIONS_INDEX, groupId, stored);
                Assert.NotNull(container);

                byte[] payload = container.GetStream().ToArray();
                List<int> workingCounts = FileCountsThatFullyDecode(groupId, payload);

                _output.WriteLine($"group {groupId}: {payload.Length} bytes of payload, " +
                                  $"file counts that decode exactly: {string.Join(", ", workingCounts)}");

                //Exactly one count works, whatever it is - that is the self-proving half, and it
                //holds without knowing the answer in advance.
                Assert.Single(workingCounts);
                _fixture.Profile.AssertCensus(_output,
                    $"interface.undeclared.{groupId}.files", workingCounts[0]);
            }
        }

        /// <summary>
        ///     The bytes <c>InterfaceComponentCodecTests</c> asserts against are still what the cache
        ///     holds.
        /// </summary>
        /// <remarks>
        ///     Without this the offline tests pin the codec to literals nobody can check, which is the
        ///     shape a hand-built test takes when it asserts a bug rather than catching one.
        /// </remarks>
        [RealCacheFact]
        public void TheCapturedFixtures_AreStillWhatTheCacheStores()
        {
            RSCache cache = _fixture.OpenCache();

            foreach ((int groupId, int fileId, byte[] expected) in InterfaceComponentCodecTests.CapturedComponents())
            {
                byte[] stored = cache.ReadFileBytes(RSConstants.INTERFACE_DEFINITIONS_INDEX, groupId, fileId);
                Assert.Equal(expected, stored);
            }
        }

        /// <summary>
        ///     Every file count under which the whole group unpacks and every component consumes its
        ///     buffer exactly.
        /// </summary>
        /// <param name="groupId">The group being probed, for the component addresses.</param>
        /// <param name="payload">The decompressed group payload.</param>
        /// <returns>The counts that work, ascending.</returns>
        private static List<int> FileCountsThatFullyDecode(int groupId, byte[] payload)
        {
            var working = new List<int>();

            //An upper bound rather than an exhaustive search: the smallest component in this cache is
            //60 bytes, so no group can hold more files than its payload has room for.
            int mostFiles = Math.Max(1, payload.Length / 60);

            for (int count = 1; count <= mostFiles; count++)
            {
                int[] fileIds = Enumerable.Range(0, count).ToArray();

                RSArchive archive;
                try
                {
                    archive = RSArchive.Decode(new JagStream((byte[])payload.Clone()), fileIds);
                }
                catch (Exception)
                {
                    //A wrong count reads the size trailer at the wrong offset, so it fails in
                    //whatever way the unpacker happens to notice first.
                    continue;
                }

                if (DecodesExactly(groupId, fileIds, archive))
                    working.Add(count);
            }

            return working;
        }

        private static bool DecodesExactly(int groupId, int[] fileIds, RSArchive archive)
        {
            foreach (int fileId in fileIds)
            {
                if (!archive.HasFile(fileId))
                    return false;

                byte[] bytes = archive.GetFile(fileId).ToArray();
                if (bytes.Length == 0)
                    return false;

                var stream = new JagStream(bytes);
                try
                {
                    new InterfaceComponentDefinition(groupId, fileId).Decode(stream);
                }
                catch (Exception)
                {
                    return false;
                }

                if (stream.Position != bytes.Length)
                    return false;
            }

            return true;
        }

        private static void Count(SortedDictionary<int, int> counts, int value)
        {
            counts.TryGetValue(value, out int seen);
            counts[value] = seen + 1;
        }

        private static string Histogram(SortedDictionary<int, int> counts)
        {
            return string.Join(", ", counts.Select(entry => entry.Key + "=" + entry.Value));
        }
    }
}
