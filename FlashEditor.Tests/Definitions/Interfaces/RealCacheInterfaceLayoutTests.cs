using System;
using System.Collections.Generic;
using System.Linq;
using FlashEditor.Cache;
using FlashEditor.Definitions.Interfaces;
using FlashEditor.Definitions.Interfaces.Layout;
using FlashEditor.IO;
using FlashEditor.Tests.Cache.RealCache;
using Xunit;
using Xunit.Abstractions;

namespace FlashEditor.Tests.Definitions.Interfaces {
    /// <summary>
    ///     Resolves every component in the cache and asserts the properties that must hold whatever
    ///     the mode bytes are.
    /// </summary>
    /// <remarks>
    ///     <b>Why this exists in this shape.</b> The layout resolver reads bytes and writes
    ///     rectangles, so no byte-identity sweep can see it: the encoder is not involved and a
    ///     resolver that put every component in the wrong place would leave the primary regression
    ///     detector completely green. What can be asserted instead is a set of relationships that
    ///     hold for any correct resolver, over every component the reference table declares.
    ///     <para>
    ///     <b>No count is written down here.</b> Index 3 is one of the eleven indexes the two
    ///     supported caches disagree on, so every total is read from the reference table and every
    ///     census is printed rather than asserted. What is asserted is the relationship - every
    ///     declared component produced exactly one node - which is true of both caches and would
    ///     stay true of a third.
    ///     </para>
    /// </remarks>
    [Collection("RealCache")]
    public sealed class RealCacheInterfaceLayoutTests : IClassFixture<RealCacheFixture> {
        private readonly RealCacheFixture _fixture;
        private readonly ITestOutputHelper _output;

        /// <summary>Binds the shared open cache and the per-test output sink.</summary>
        public RealCacheInterfaceLayoutTests(RealCacheFixture fixture, ITestOutputHelper output) {
            _fixture = fixture;
            _output = output;
        }

        /// <summary>
        ///     Every declared component resolves to exactly one node, and no group throws.
        /// </summary>
        /// <remarks>
        ///     The termination half of this is not incidental. A parent chain in this index can be
        ///     770 levels deep in the largest group, and at least one component in both caches is its
        ///     own parent, so a resolver that recursed or that walked a parent chain would either
        ///     overflow the stack or hang. That this test finishes at all is the assertion.
        /// </remarks>
        [RealCacheFact]
        public void EveryComponentResolvesExactlyOnce() {
            int declared = _fixture.DeclaredFiles(RSConstants.INTERFACE_DEFINITIONS_INDEX);
            int groups = 0;
            int resolvedTotal = 0;
            int drawn = 0;

            foreach ((int groupId, List<InterfaceComponentDefinition> components) in EveryInterface()) {
                InterfaceComponentTree tree = InterfaceComponentTree.Build(groupId, components);
                IReadOnlyDictionary<int, InterfaceLayoutNode> resolved =
                    InterfaceLayoutResolver.ResolveGroup(tree, InterfaceRect.FixedModeCanvas);

                Assert.Equal(components.Count, resolved.Count);

                groups++;
                resolvedTotal += resolved.Count;
                drawn += resolved.Values.Count(node => node.IsDrawn);
            }

            _output.WriteLine($"cache: {_fixture.Profile.Name}");
            _output.WriteLine($"interfaces {groups}, components {resolvedTotal}, of which the client " +
                              $"would lay out {drawn} and would not lay out {resolvedTotal - drawn}");

            Assert.Equal(_fixture.DeclaredGroups(RSConstants.INTERFACE_DEFINITIONS_INDEX), groups);
            Assert.Equal(declared, resolvedTotal);
        }

        /// <summary>
        ///     A drawn child of a layer resolves inside its parent's clip rectangle wherever its
        ///     modes place it inside the parent's box.
        /// </summary>
        /// <remarks>
        ///     Deliberately not "every child is inside its parent". That is false and the format
        ///     allows it to be false: mode 1 subtracts the base from the parent and can go negative,
        ///     mode 2 permits a base above 16384 meaning "wider than the parent", and both occur.
        ///     What must hold is the weaker and actually meaningful claim - that the clip a child
        ///     inherits never grows. A clip that grew would mean a child could paint outside every
        ///     ancestor that was supposed to contain it.
        /// </remarks>
        [RealCacheFact]
        public void AChildsClipNeverGrowsBeyondItsParents() {
            int checkedChildren = 0;
            int clippedAway = 0;

            foreach ((int groupId, List<InterfaceComponentDefinition> components) in EveryInterface()) {
                InterfaceComponentTree tree = InterfaceComponentTree.Build(groupId, components);
                IReadOnlyDictionary<int, InterfaceLayoutNode> resolved =
                    InterfaceLayoutResolver.ResolveGroup(tree, InterfaceRect.FixedModeCanvas);

                foreach (int fileId in tree.InDrawOrder()) {
                    InterfaceLayoutNode node = resolved[fileId];

                    foreach (int childId in tree.ChildrenOf(fileId)) {
                        InterfaceLayoutNode child = resolved[childId];
                        checkedChildren++;

                        if (child.Clip.IsEmpty) {
                            clippedAway++;
                            continue;
                        }

                        /* A type-2 component inherits its parent's clip verbatim, so it is equal
                           rather than contained, and IsInside covers that. Nothing else may exceed
                           it on any edge. */
                        Assert.True(child.Clip.IsInside(node.Clip),
                            $"group {groupId} file {childId} (type {child.Component.ComponentType}) " +
                            $"clips to {child.Clip}, outside its parent's {node.Clip}");
                    }
                }
            }

            _output.WriteLine($"cache: {_fixture.Profile.Name}");
            _output.WriteLine($"parented components checked {checkedChildren}, " +
                              $"of which {clippedAway} clip away to nothing");

            Assert.True(checkedChildren > 0, "No parented component was reached at all.");
        }

        /// <summary>
        ///     Prints which layout mode values the cache actually uses, and asserts the ones it does
        ///     not.
        /// </summary>
        /// <remarks>
        ///     A branch no data reaches is defended by nothing, and the next person to touch the
        ///     resolver needs to know which those are. Two findings this pins:
        ///     <list type="bullet">
        ///     <item>
        ///     <b>Sizing modes 3 and 4 occur zero times on either axis, in both caches.</b> So the
        ///     mode-3 fall-through and both aspect-ratio cross-links are reachable only through CS2
        ///     opcode 1001 and are defended solely by the hand-written unit tests.
        ///     </item>
        ///     <item>
        ///     The positioning modes are the ones with real coverage, which is why the catch-all arm
        ///     matters.
        ///     </item>
        ///     </list>
        ///     The assertion is deliberately one-directional: it fails if a sizing mode above 2 ever
        ///     appears, because that would mean a branch this suite treats as dead has become live
        ///     and the note above has gone stale. It does not assert the counts, which differ between
        ///     the two caches.
        /// </remarks>
        [RealCacheFact]
        public void LayoutModeCoverage_IsPrintedAndTheDeadSizingBranchesAreStillDead() {
            var widthModes = new SortedDictionary<int, int>();
            var heightModes = new SortedDictionary<int, int>();
            var xModes = new SortedDictionary<int, int>();
            var yModes = new SortedDictionary<int, int>();

            foreach ((int _, List<InterfaceComponentDefinition> components) in EveryInterface()) {
                foreach (InterfaceComponentDefinition component in components) {
                    Count(widthModes, component.WidthMode);
                    Count(heightModes, component.HeightMode);
                    Count(xModes, component.XMode);
                    Count(yModes, component.YMode);
                }
            }

            _output.WriteLine($"cache: {_fixture.Profile.Name}");
            _output.WriteLine("widthMode  " + Describe(widthModes));
            _output.WriteLine("heightMode " + Describe(heightModes));
            _output.WriteLine("xMode      " + Describe(xModes));
            _output.WriteLine("yMode      " + Describe(yModes));

            Assert.All(widthModes.Keys, mode => Assert.InRange(mode, 0, 2));
            Assert.All(heightModes.Keys, mode => Assert.InRange(mode, 0, 2));
        }

        /// <summary>
        ///     At least one component in this cache is its own parent, and the tree survives it.
        /// </summary>
        /// <remarks>
        ///     <b>This test exists because the specification claimed the opposite.</b> The design
        ///     document for the resolver asserted that no parent cycle exists in either cache and
        ///     proposed a test asserting exactly that; a reviewer found group 468 file 1 storing its
        ///     own file id as its parent, byte-identically in both. Had the document been believed,
        ///     the resolver would have been written to assume acyclicity and the assertion would have
        ///     failed on its first run - or worse, been "fixed" by relaxing it.
        ///     <para>
        ///     The group is not named in the assertion. What is asserted is that cycles exist, are
        ///     classified, and do not prevent the group resolving; which group carries one is a
        ///     property of a particular cache and is printed instead.
        ///     </para>
        /// </remarks>
        [RealCacheFact]
        public void ParentCyclesExistInThisCacheAndAreSurvived() {
            var cyclic = new List<string>();
            var dangling = new List<string>();

            foreach ((int groupId, List<InterfaceComponentDefinition> components) in EveryInterface()) {
                InterfaceComponentTree tree = InterfaceComponentTree.Build(groupId, components);

                foreach (InterfaceComponentDefinition component in components) {
                    switch (tree.ParentageOf(component.FileId)) {
                        case InterfaceParentage.Cyclic:
                            cyclic.Add($"{groupId}:{component.FileId}" +
                                       $" (parent {component.RawParentId})");
                            break;
                        case InterfaceParentage.Dangling:
                            dangling.Add($"{groupId}:{component.FileId}" +
                                         $" (parent {component.RawParentId})");
                            break;
                    }
                }
            }

            _output.WriteLine($"cache: {_fixture.Profile.Name}");
            _output.WriteLine($"components in a parent cycle: {cyclic.Count}" +
                              (cyclic.Count == 0 ? "" : " - " + string.Join(", ", cyclic.Take(20))));
            _output.WriteLine($"components with a dangling parent: {dangling.Count}" +
                              (dangling.Count == 0 ? "" : " - " + string.Join(", ", dangling.Take(20))));

            Assert.NotEmpty(cyclic);
        }

        private static void Count(IDictionary<int, int> census, int mode) {
            census[mode] = census.TryGetValue(mode, out int seen) ? seen + 1 : 1;
        }

        private static string Describe(SortedDictionary<int, int> census) {
            return string.Join("  ", census.Select(entry => $"{entry.Key}:{entry.Value}"));
        }

        /// <summary>
        ///     Every interface in the cache, decoded a group at a time.
        /// </summary>
        /// <remarks>
        ///     <c>ReadGroup</c> rather than a per-file walk. <c>RSCache.ReadFile</c> releases the
        ///     container the moment it has handed back one file, so reading index 3 file by file
        ///     re-inflates each group once per component it holds - tens of thousands of group
        ///     decodes for the same bytes this does in one per group.
        /// </remarks>
        private IEnumerable<(int GroupId, List<InterfaceComponentDefinition> Components)> EveryInterface() {
            RSCache cache = _fixture.OpenCache();

            foreach (int groupId in cache.EnumerateGroups(RSConstants.INTERFACE_DEFINITIONS_INDEX)) {
                IReadOnlyDictionary<int, JagStream> files;

                try {
                    files = cache.ReadGroup(RSConstants.INTERFACE_DEFINITIONS_INDEX, groupId);
                }
                catch (Exception ex) {
                    throw new Xunit.Sdk.XunitException(
                        $"Interface group {groupId} could not be read: {ex.Message}");
                }

                var components = new List<InterfaceComponentDefinition>(files.Count);

                foreach (KeyValuePair<int, JagStream> file in files.OrderBy(entry => entry.Key)) {
                    var component = new InterfaceComponentDefinition(groupId, file.Key);
                    component.Decode(file.Value);
                    components.Add(component);
                }

                yield return (groupId, components);
            }
        }
    }
}
