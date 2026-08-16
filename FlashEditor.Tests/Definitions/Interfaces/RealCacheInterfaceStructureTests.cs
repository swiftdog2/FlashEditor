using System;
using System.Collections.Generic;
using System.IO;
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
    ///     Creating, deleting and reordering interface components, applied to a real interface.
    /// </summary>
    /// <remarks>
    ///     <b>The components are the cache's and the cache written to is not.</b> A real interface
    ///     is copied into a temporary store and every edit lands there, because the real cache is
    ///     opened read-only and stays that way. Synthetic components would prove much less: index
    ///     3 is not an opcode format, its records vary in length by type, and a group of hand-built
    ///     three-byte payloads would exercise none of the byte layout a renumbering has to leave
    ///     alone.
    ///     <para>
    ///     <b>The copy is stored uncompressed on purpose.</b> Index 3 is GZip in both caches and a
    ///     GZip re-encode is never byte-identical, so an edit and its inverse could only ever be
    ///     compared there as decompressed payloads. Stored uncompressed, the container is
    ///     deterministic and the round trip is asserted on the stored bytes themselves.
    ///     </para>
    /// </remarks>
    [Collection("RealCache")]
    public sealed class RealCacheInterfaceStructureTests : IClassFixture<RealCacheFixture>, IDisposable {
        private const int Index = RSConstants.INTERFACE_DEFINITIONS_INDEX;

        private readonly RealCacheFixture _fixture;
        private readonly ITestOutputHelper _output;
        private readonly string _dir;
        private readonly List<RSFileStore> _stores = new List<RSFileStore>();

        /// <summary>Binds the shared open cache and the per-test output sink.</summary>
        public RealCacheInterfaceStructureTests(RealCacheFixture fixture, ITestOutputHelper output) {
            _fixture = fixture;
            _output = output;
            _dir = Path.Combine(Path.GetTempPath(), "fe-iface-" + Guid.NewGuid().ToString("N"));
        }

        /// <summary>Releases the temporary stores, each of which holds its dat2 exclusively.</summary>
        public void Dispose() {
            foreach (RSFileStore store in _stores)
                store.Dispose();
            if (Directory.Exists(_dir))
                Directory.Delete(_dir, true);
        }

        // ===================================================================
        //  What index 3's numbering is
        // ===================================================================

        /// <summary>
        ///     Every interface the table declares numbers its components 0 to n-1 with no hole.
        /// </summary>
        /// <remarks>
        ///     <b>Two documents in this tree disagreed about this, and the renumbering every
        ///     structural edit performs exists only if this one is right.</b>
        ///     <c>InterfaceComponentEdits</c> states that file ids are dense and the client depends
        ///     on it; <c>InterfaceEditorPanel.InterfaceListing.IdRange</c> said the opposite in a
        ///     comment, that the count and the highest id disagree. Measured rather than picked.
        ///     <para>
        ///     What makes it compulsory rather than tidy: the client derives a group's file count
        ///     as <c>maxFileId + 1</c> and throws the explicit id list away whenever that agrees
        ///     with the declared count (<c>VersionTable.java:183,185</c>), so a group left with a
        ///     hole is read with a file count that does not match its contents and every component
        ///     after the hole is addressed as a different one.
        ///     </para>
        ///     <para>
        ///     Read from the reference table, never decoded, so the totals are printed rather than
        ///     asserted - index 3 is one of the eleven the two supported caches disagree on.
        ///     </para>
        /// </remarks>
        [RealCacheFact]
        public void EveryInterface_NumbersItsComponentsDenselyFromZero() {
            RSReferenceTable table = _fixture.Table(Index);

            var sparse = new List<string>();
            int groups = 0;
            int files = 0;

            foreach (int groupId in _fixture.OpenCache().EnumerateGroups(Index)) {
                RSArchiveEntry entry = table.GetArchiveEntry(groupId);
                if (entry == null)
                    continue;

                int[] ids = entry.GetValidFileIds();
                groups++;
                files += ids.Length;

                for (int i = 0; i < ids.Length; i++) {
                    if (ids[i] == i)
                        continue;

                    sparse.Add("group " + groupId + " declares " + ids.Length + " files, highest id " +
                        ids[ids.Length - 1] + ", first hole at position " + i + " (id " + ids[i] + ")");
                    break;
                }
            }

            _output.WriteLine("index 3: " + groups + " groups, " + files + " files declared");
            _output.WriteLine(sparse.Count == 0
                ? "every group is dense 0..n-1"
                : sparse.Count + " groups are not dense:");

            foreach (string line in sparse.Take(20))
                _output.WriteLine("  " + line);

            Assert.Empty(sparse);
        }

        // ===================================================================
        //  A structural edit that changes nothing
        // ===================================================================

        /// <summary>
        ///     A plan that moves nothing stages nothing, and is refused before the group is read.
        /// </summary>
        /// <remarks>
        ///     The invariant this whole feature is most likely to break silently. Re-encoding a
        ///     group rewrites its stored bytes and therefore its archive CRC, which rewrites the
        ///     entry carrying it, which rewrites the reference-table container every other
        ///     interface in the index shares - so a no-op noticed after the write has already cost
        ///     every one of them their bytes.
        /// </remarks>
        [RealCacheFact]
        public void AReorderToTheSamePosition_StagesNothing() {
            Scratch scratch = Copy();
            InterfaceComponentTree tree = TreeOf(scratch.Cache, scratch.GroupId);

            int subject = tree.Roots[0];
            InterfaceStructureEdit plan = InterfaceComponentEdits.PlanReorder(tree, subject, 0);
            Assert.True(plan.IsEmpty);

            Snapshot before = Snapshot.Of(scratch);
            Assert.False(InterfaceStructureWriter.Apply(scratch.Cache, scratch.GroupId, plan));
            before.AssertNothingChanged(scratch);
        }

        // ===================================================================
        //  Set and unset
        // ===================================================================

        /// <summary>
        ///     Creating a component and deleting it lands on the bytes that were there to begin
        ///     with, and on the reference-table entry that described them.
        /// </summary>
        /// <remarks>
        ///     The set-and-unset check in its unconditional form. Appending takes the next free id
        ///     and moves nothing, and deleting the highest id closes a numbering that has no hole
        ///     after it - so these two are exact inverses for any interface, which is what makes
        ///     this the pair worth asserting byte identity on.
        ///     <para>
        ///     The archive VERSION is expected to have advanced by two, and that is not a defect
        ///     being tolerated: it is a counter the JS5 update protocol compares against what a
        ///     client already holds, and this group really was written twice.
        ///     </para>
        /// </remarks>
        [RealCacheFact]
        public void CreatingAComponentAndDeletingIt_LandsOnTheOriginalStoredBytes() {
            Scratch scratch = Copy();
            InterfaceComponentTree tree = TreeOf(scratch.Cache, scratch.GroupId);

            byte[] storedBefore = scratch.Cache.LoadContainer(Index, scratch.GroupId).ToArray();
            RSArchiveEntry entryBefore = scratch.Cache.GetReferenceTable(Index).GetArchiveEntry(scratch.GroupId);
            int versionBefore = entryBefore.GetVersion();
            int crcBefore = entryBefore.GetCrc();
            int[] idsBefore = entryBefore.GetValidFileIds();
            int[] namesBefore = idsBefore.Select(id => entryBefore.GetFileEntry(id).GetIdentifier()).ToArray();

            int parent = tree.Roots[0];
            InterfaceStructureEdit create = InterfaceComponentEdits.PlanInsert(tree, parent, -1);
            InterfaceComponentDefinition created =
                InterfaceComponentEdits.NewComponent(scratch.GroupId, create.Inserted, parent);

            Assert.True(InterfaceStructureWriter.Apply(scratch.Cache, scratch.GroupId, create, created));
            Assert.NotEqual(storedBefore, scratch.Cache.LoadContainer(Index, scratch.GroupId).ToArray());

            InterfaceComponentTree grown = TreeOf(scratch.Cache, scratch.GroupId);
            InterfaceStructureEdit remove = InterfaceComponentEdits.PlanDelete(grown, create.Inserted);
            Assert.Empty(remove.Renumbering);
            Assert.True(InterfaceStructureWriter.Apply(scratch.Cache, scratch.GroupId, remove));

            RSCache reopened = SaveAndReopen(scratch.Cache);
            RSArchiveEntry entryAfter = reopened.GetReferenceTable(Index).GetArchiveEntry(scratch.GroupId);

            Assert.Equal(storedBefore, reopened.LoadContainer(Index, scratch.GroupId).ToArray());
            Assert.Equal(idsBefore, entryAfter.GetValidFileIds());
            Assert.Equal(namesBefore,
                idsBefore.Select(id => entryAfter.GetFileEntry(id).GetIdentifier()).ToArray());
            Assert.Equal(crcBefore, entryAfter.GetCrc());
            Assert.Equal(versionBefore + 2, entryAfter.GetVersion());
        }

        /// <summary>
        ///     Deleting an interior leaf and putting it back lands on the original stored bytes,
        ///     renumbering and un-renumbering the whole group on the way.
        /// </summary>
        /// <remarks>
        ///     The hard half of set-and-unset. A delete of id <i>k</i> moves every id above it down
        ///     by one and repoints every parent reference into that range; an insert that takes id
        ///     <i>k</i> moves them all back up. So they cancel exactly - <b>and only when the insert
        ///     really does take id k</b>, which is a real condition rather than a formality.
        ///     <c>PlanInsert</c> is stated in sibling positions, and the id a position yields is
        ///     whatever the sibling standing there currently holds; a parent whose remaining
        ///     children are not contiguous with the deleted one therefore cannot express the
        ///     inverse at all, and a parent left with no children at all appends to the end
        ///     instead. That is a property of the planner worth knowing before an undo stack is
        ///     built on it, so the subject here is chosen to satisfy the condition and
        ///     <c>insert.Inserted</c> is asserted rather than assumed.
        ///     <para>
        ///     <b>The restored component's parent is its ORIGINAL id, not the one it had between
        ///     the two edits.</b> The inserted component's bytes are stored exactly as handed over -
        ///     the plan's renumbering is applied to the components already in the group and never
        ///     to the newcomer, because a component that does not exist yet cannot have been moved.
        ///     Since the delete and the insert cancel, the parent's id after both is the id it had
        ///     before either.
        ///     </para>
        ///     <para>
        ///     The identifier is carried back explicitly rather than defaulted. A component created
        ///     in the editor has no name, but a component being <i>restored</i> had one, and
        ///     <c>RSGroupFile</c> exists to let the caller that knows the mapping move the hashes
        ///     with the files - nothing in the bytes says which id a name followed.
        ///     </para>
        /// </remarks>
        [RealCacheFact]
        public void DeletingAnInteriorLeafAndPuttingItBack_LandsOnTheOriginalStoredBytes() {
            Scratch scratch = Copy();
            InterfaceComponentTree tree = TreeOf(scratch.Cache, scratch.GroupId);

            (int leaf, int parent, int position) = PickRestorableLeaf(tree);

            byte[] storedBefore = scratch.Cache.LoadContainer(Index, scratch.GroupId).ToArray();
            RSArchiveEntry entryBefore = scratch.Cache.GetReferenceTable(Index).GetArchiveEntry(scratch.GroupId);
            int versionBefore = entryBefore.GetVersion();
            int crcBefore = entryBefore.GetCrc();
            int[] idsBefore = entryBefore.GetValidFileIds();
            int[] namesBefore = idsBefore.Select(id => entryBefore.GetFileEntry(id).GetIdentifier()).ToArray();
            int nameOfLeaf = namesBefore[Array.IndexOf(idsBefore, leaf)];

            //Kept before the delete, because after it there is nothing to read it from.
            byte[] leafBytes = scratch.Cache.ReadGroup(Index, scratch.GroupId)[leaf].ToArray();
            var restored = new InterfaceComponentDefinition(scratch.GroupId, leaf)
                .Decode(new JagStream(leafBytes));

            _output.WriteLine("interface " + scratch.SourceGroupId + " (copied to slot " + scratch.GroupId +
                "): deleting leaf " + leaf + " of " + tree.Components.Count + ", child " + position +
                " of component " + parent);

            InterfaceStructureEdit delete = InterfaceComponentEdits.PlanDelete(tree, leaf);
            Assert.NotEmpty(delete.Renumbering);
            Assert.True(InterfaceStructureWriter.Apply(scratch.Cache, scratch.GroupId, delete));
            Assert.NotEqual(storedBefore, scratch.Cache.LoadContainer(Index, scratch.GroupId).ToArray());

            //The plan is built against the tree as it is NOW, so the parent has to be named in the
            //numbering the delete left behind.
            InterfaceComponentTree after = TreeOf(scratch.Cache, scratch.GroupId);
            int movedParent = delete.Renumbering.TryGetValue(parent, out int p) ? p : parent;

            InterfaceStructureEdit insert = InterfaceComponentEdits.PlanInsert(after, movedParent, position);
            Assert.Equal(leaf, insert.Inserted);
            Assert.True(InterfaceStructureWriter.Apply(scratch.Cache, scratch.GroupId, insert, restored,
                nameOfLeaf));

            RSCache reopened = SaveAndReopen(scratch.Cache);
            RSArchiveEntry entryAfter = reopened.GetReferenceTable(Index).GetArchiveEntry(scratch.GroupId);

            Assert.Equal(storedBefore, reopened.LoadContainer(Index, scratch.GroupId).ToArray());
            Assert.Equal(idsBefore, entryAfter.GetValidFileIds());
            Assert.Equal(namesBefore,
                idsBefore.Select(id => entryAfter.GetFileEntry(id).GetIdentifier()).ToArray());
            Assert.Equal(crcBefore, entryAfter.GetCrc());
            Assert.Equal(versionBefore + 2, entryAfter.GetVersion());
        }

        /// <summary>
        ///     A leaf whose deletion can be undone through <c>PlanInsert</c>.
        /// </summary>
        /// <remarks>
        ///     The condition is that the sibling standing immediately after it holds the very next
        ///     file id, so that once the delete has shifted everything down, the same sibling
        ///     position yields the id that was vacated. See the caller's remarks for why that is a
        ///     condition at all rather than something any leaf satisfies.
        /// </remarks>
        /// <param name="tree">The interface's structure.</param>
        /// <returns>The leaf, its parent, and its position among its siblings.</returns>
        private static (int Leaf, int Parent, int Position) PickRestorableLeaf(InterfaceComponentTree tree) {
            if (TryPickRestorableLeaf(tree, out (int, int, int) found))
                return found;

            throw new InvalidOperationException(
                "No interface component in this group is a leaf followed immediately by the sibling " +
                "holding the next file id, which is what makes a delete undoable through PlanInsert.");
        }

        private static bool TryPickRestorableLeaf(InterfaceComponentTree tree,
            out (int Leaf, int Parent, int Position) found) {
            foreach (int parent in tree.Components.Keys.OrderBy(id => id)) {
                IReadOnlyList<int> children = tree.ChildrenOf(parent);

                for (int i = 0; i + 1 < children.Count; i++) {
                    if (tree.ChildrenOf(children[i]).Count != 0 || children[i + 1] != children[i] + 1)
                        continue;

                    found = (children[i], parent, i);
                    return true;
                }
            }

            found = default;
            return false;
        }

        /// <summary>
        ///     And once it is back, restating the interface is a no-op again - so the baseline the
        ///     unchanged path measures against followed the writes rather than being fixed at the
        ///     bytes the session opened with.
        /// </summary>
        [RealCacheFact]
        public void AfterAnEditAndItsInverse_AReorderToTheSamePositionStillStagesNothing() {
            Scratch scratch = Copy();
            InterfaceComponentTree tree = TreeOf(scratch.Cache, scratch.GroupId);

            int subject = tree.Roots[0];
            int last = tree.Components.Keys.Max();

            //Append and delete, which is the shortest pair that really rewrites the group.
            var extra = new InterfaceComponentDefinition(scratch.GroupId, last + 1) {
                RawParentId = subject
            };

            InterfaceStructureEdit append = InterfaceComponentEdits.PlanInsert(tree, subject, -1);
            Assert.True(InterfaceStructureWriter.Apply(scratch.Cache, scratch.GroupId, append, extra));

            InterfaceComponentTree grown = TreeOf(scratch.Cache, scratch.GroupId);
            InterfaceStructureEdit remove = InterfaceComponentEdits.PlanDelete(grown, append.Inserted);
            Assert.True(InterfaceStructureWriter.Apply(scratch.Cache, scratch.GroupId, remove));

            InterfaceComponentTree back = TreeOf(scratch.Cache, scratch.GroupId);
            Snapshot before = Snapshot.Of(scratch);

            Assert.False(InterfaceStructureWriter.Apply(scratch.Cache, scratch.GroupId,
                InterfaceComponentEdits.PlanReorder(back, back.Roots[0], 0)));

            before.AssertNothingChanged(scratch);
        }

        // ===================================================================
        //  What each operation leaves behind
        // ===================================================================

        /// <summary>
        ///     A delete takes the subtree with it, closes the numbering, and leaves every surviving
        ///     component naming the parent it had.
        /// </summary>
        /// <remarks>
        ///     The half of a structural edit that is impossible to see afterwards: the components
        ///     move, the tree looks right, and every parent field still names the id its component
        ///     used to have. Read back through a reopened store, because a read through the cache
        ///     that wrote it returns the staged bytes whether or not they were committed.
        /// </remarks>
        [RealCacheFact]
        public void DeletingASubtree_ClosesTheNumberingAndRepointsEverySurvivingParent() {
            Scratch scratch = Copy();
            InterfaceComponentTree tree = TreeOf(scratch.Cache, scratch.GroupId);

            //A component with children, so the delete cascades rather than removing one record.
            int subject = tree.Components.Keys
                .Where(id => tree.ChildrenOf(id).Count > 0 && tree.ParentageOf(id) == InterfaceParentage.Child)
                .OrderBy(id => id)
                .First();

            InterfaceStructureEdit plan = InterfaceComponentEdits.PlanDelete(tree, subject);
            _output.WriteLine("interface " + scratch.SourceGroupId + ": deleting " + subject + " takes " +
                plan.Removed.Count + " of " + tree.Components.Count + " components and renumbers " +
                plan.Renumbering.Count);

            //What each surviving component's parent should be afterwards, worked out from the tree
            //BEFORE the edit, so the assertion does not read the answer off the thing it is testing.
            var expected = new Dictionary<int, int>();
            foreach (KeyValuePair<int, InterfaceComponentDefinition> entry in tree.Components) {
                if (plan.Removed.Contains(entry.Key))
                    continue;

                int newId = plan.Renumbering.TryGetValue(entry.Key, out int moved) ? moved : entry.Key;
                int parent = entry.Value.RawParentId;

                expected[newId] = parent == InterfaceComponentDefinition.NoParent
                    ? InterfaceComponentDefinition.NoParent
                    : plan.Renumbering.TryGetValue(parent, out int movedParent) ? movedParent : parent;
            }

            Assert.True(InterfaceStructureWriter.Apply(scratch.Cache, scratch.GroupId, plan));

            RSCache reopened = SaveAndReopen(scratch.Cache);
            RSArchiveEntry entry2 = reopened.GetReferenceTable(Index).GetArchiveEntry(scratch.GroupId);

            Assert.Equal(Enumerable.Range(0, expected.Count).ToArray(), entry2.GetValidFileIds());

            IReadOnlyDictionary<int, JagStream> stored = reopened.ReadGroup(Index, scratch.GroupId);
            foreach (KeyValuePair<int, int> want in expected) {
                var component = new InterfaceComponentDefinition(scratch.GroupId, want.Key)
                    .Decode(stored[want.Key]);
                Assert.Equal(want.Value, component.RawParentId);
            }
        }

        /// <summary>
        ///     Appending renumbers nothing, so every component that was already there keeps the
        ///     exact bytes it had.
        /// </summary>
        /// <remarks>
        ///     The cheap operation, and worth pinning as cheap: appending at the end is the only
        ///     insertion that moves no id, which is why the planner prefers it and why the UI
        ///     offers it as the default.
        ///     <para>
        ///     <b>What this does not discriminate, and should not be read as doing:</b> whether the
        ///     writer copied those bytes or re-derived them. Every component in both supported
        ///     caches re-encodes to what it was read from, so a writer that decoded and re-encoded
        ///     all fifty records would pass this unchanged. The reason
        ///     <c>InterfaceStructureWriter</c> hands the stored bytes straight back is that it does
        ///     not want a structural edit to depend on that claim at all; this test says the result
        ///     is right, and the byte-identity sweeps are what say the other route would have been.
        ///     </para>
        /// </remarks>
        [RealCacheFact]
        public void AppendingAComponent_LeavesEveryOtherComponentsBytesUntouched() {
            Scratch scratch = Copy();
            InterfaceComponentTree tree = TreeOf(scratch.Cache, scratch.GroupId);

            Dictionary<int, byte[]> before = scratch.Cache.ReadGroup(Index, scratch.GroupId)
                .ToDictionary(pair => pair.Key, pair => pair.Value.ToArray());

            int parent = tree.Roots[0];
            InterfaceStructureEdit plan = InterfaceComponentEdits.PlanInsert(tree, parent, -1);

            Assert.Empty(plan.Renumbering);
            Assert.Empty(plan.Warnings);
            Assert.Equal(tree.Components.Count, plan.Inserted);

            InterfaceComponentDefinition created =
                InterfaceComponentEdits.NewComponent(scratch.GroupId, plan.Inserted, parent);

            Assert.True(InterfaceStructureWriter.Apply(scratch.Cache, scratch.GroupId, plan, created));

            RSCache reopened = SaveAndReopen(scratch.Cache);
            IReadOnlyDictionary<int, JagStream> after = reopened.ReadGroup(Index, scratch.GroupId);

            Assert.Equal(before.Count + 1, after.Count);
            foreach (KeyValuePair<int, byte[]> original in before)
                Assert.Equal(original.Value, after[original.Key].ToArray());

            /* The created component reads back as what the editor made it, which is a claim about
               the codec on a record no cache has ever contained. Every byte-identity sweep in this
               suite compares a decode against bytes the original encoder wrote, so none of them
               says anything about a record this project composed from nothing - and the one field
               that would fail silently is the type, because the type byte chooses which block is
               written and which is read. */
            var read = new InterfaceComponentDefinition(scratch.GroupId, plan.Inserted)
                .Decode(after[plan.Inserted]);

            Assert.Equal(created.ComponentType, read.ComponentType);
            Assert.Equal(parent, read.RawParentId);
            Assert.Equal(created.BaseWidth, read.BaseWidth);
            Assert.Equal(created.BaseHeight, read.BaseHeight);
            Assert.Equal(created.Colour, read.Colour);
            Assert.True(read.RectangleFilled);

            //The created component is unnamed, because a hash the editor invented would match no
            //name forever and index 3's names are recovered by re-hashing candidates.
            RSArchiveEntry entry = reopened.GetReferenceTable(Index).GetArchiveEntry(scratch.GroupId);
            Assert.Equal(RSGroupFile.Unnamed, entry.GetFileEntry(plan.Inserted).GetIdentifier());
        }

        /// <summary>
        ///     Reordering a component among its siblings and putting it back lands on the original
        ///     stored bytes.
        /// </summary>
        /// <remarks>
        ///     Draw order is file-id order within a parent and is not a stored field, so a reorder
        ///     is purely a renumbering - the payloads that move carry no trace of their own ids and
        ///     only the parent references into the moved range change. That makes this the case a
        ///     payload-only comparison gets wrong in both directions at once, and the reason the
        ///     archive layer compares the declared id list separately.
        /// </remarks>
        [RealCacheFact]
        public void ReorderingAComponentAndPuttingItBack_LandsOnTheOriginalStoredBytes() {
            Scratch scratch = Copy();
            InterfaceComponentTree tree = TreeOf(scratch.Cache, scratch.GroupId);

            //A parent with at least three children, so a move has somewhere to go.
            int parent = tree.Components.Keys
                .Where(id => tree.ChildrenOf(id).Count >= 3)
                .OrderBy(id => id)
                .First();

            IReadOnlyList<int> siblings = tree.ChildrenOf(parent);
            int subject = siblings[siblings.Count - 1];

            byte[] storedBefore = scratch.Cache.LoadContainer(Index, scratch.GroupId).ToArray();
            RSArchiveEntry entryBefore = scratch.Cache.GetReferenceTable(Index).GetArchiveEntry(scratch.GroupId);
            int[] namesBefore = entryBefore.GetValidFileIds()
                .Select(id => entryBefore.GetFileEntry(id).GetIdentifier()).ToArray();

            _output.WriteLine("interface " + scratch.SourceGroupId + ": moving " + subject + " to the front of " +
                siblings.Count + " children of " + parent);

            InterfaceStructureEdit toFront = InterfaceComponentEdits.PlanReorder(tree, subject, 0);
            Assert.NotEmpty(toFront.Renumbering);
            Assert.True(InterfaceStructureWriter.Apply(scratch.Cache, scratch.GroupId, toFront));
            Assert.NotEqual(storedBefore, scratch.Cache.LoadContainer(Index, scratch.GroupId).ToArray());

            InterfaceComponentTree moved = TreeOf(scratch.Cache, scratch.GroupId);
            int nowAt = toFront.Renumbering[subject];

            InterfaceStructureEdit back = InterfaceComponentEdits.PlanReorder(moved, nowAt, siblings.Count - 1);
            Assert.True(InterfaceStructureWriter.Apply(scratch.Cache, scratch.GroupId, back));

            RSCache reopened = SaveAndReopen(scratch.Cache);
            RSArchiveEntry entryAfter = reopened.GetReferenceTable(Index).GetArchiveEntry(scratch.GroupId);

            Assert.Equal(storedBefore, reopened.LoadContainer(Index, scratch.GroupId).ToArray());
            Assert.Equal(namesBefore, entryAfter.GetValidFileIds()
                .Select(id => entryAfter.GetFileEntry(id).GetIdentifier()).ToArray());
        }

        /// <summary>
        ///     Any renumbering says out loud that it repoints references nothing here can find.
        /// </summary>
        /// <remarks>
        ///     A component is addressed from outside its interface as
        ///     <c>(interface &lt;&lt; 16) | component</c>, by CS2 scripts in index 12 and by hook
        ///     arguments in other interfaces. Renumbering silently re-points every one of them at a
        ///     different component, and finding the CS2 ones means scanning every compiled script
        ///     for a constant, which is separate work. So the warning has to be attached to the
        ///     plan rather than left to whoever writes the next surface.
        /// </remarks>
        [RealCacheFact]
        public void AnyRenumbering_CarriesTheWarningAboutReferencesItCannotSee() {
            Scratch scratch = Copy();
            InterfaceComponentTree tree = TreeOf(scratch.Cache, scratch.GroupId);

            int subject = tree.Components.Keys
                .Where(id => tree.ChildrenOf(id).Count > 0 && tree.ParentageOf(id) == InterfaceParentage.Child)
                .OrderBy(id => id)
                .First();

            InterfaceStructureEdit plan = InterfaceComponentEdits.PlanDelete(tree, subject);

            Assert.NotEmpty(plan.Renumbering);
            Assert.Contains(plan.Warnings, warning => warning.Contains("CS2 scripts in index 12"));
        }

        // ===================================================================
        //  What it refuses
        // ===================================================================

        /// <summary>
        ///     A plan that would leave a hole in the numbering is refused rather than closed up.
        /// </summary>
        /// <remarks>
        ///     <b>Not reachable through the planner, which is the point.</b> Every operation in
        ///     <c>InterfaceComponentEdits</c> closes the numbering, so this hands the writer a plan
        ///     that removes a component and renumbers nothing - which is what a fifth operation
        ///     added later without that step would produce. Closing the hole silently would perform
        ///     a renumbering the user was never shown and never warned about, which is exactly what
        ///     the warnings exist to prevent.
        /// </remarks>
        [RealCacheFact]
        public void APlanThatWouldLeaveAHole_IsRefusedRatherThanClosedUp() {
            Scratch scratch = Copy();
            InterfaceComponentTree tree = TreeOf(scratch.Cache, scratch.GroupId);

            int leaf = tree.Components.Keys
                .Where(id => tree.ChildrenOf(id).Count == 0 && id < tree.Components.Count - 1)
                .OrderBy(id => id)
                .First();

            var holed = new InterfaceStructureEditBuilder()
                .Removing(leaf)
                .Build();

            Snapshot before = Snapshot.Of(scratch);

            InvalidOperationException refusal = Assert.Throws<InvalidOperationException>(() =>
                InterfaceStructureWriter.Apply(scratch.Cache, scratch.GroupId, holed));

            Assert.Contains("hole", refusal.Message);
            before.AssertNothingChanged(scratch);
        }

        /// <summary>
        ///     Deleting every component is refused: a group with no payload cannot be stored, and
        ///     deleting an interface is a different operation from editing one.
        /// </summary>
        [RealCacheFact]
        public void DeletingEveryComponent_IsRefused() {
            Scratch scratch = Copy();
            InterfaceComponentTree tree = TreeOf(scratch.Cache, scratch.GroupId);

            var everything = new InterfaceStructureEditBuilder();
            foreach (int id in tree.Components.Keys)
                everything.Removing(id);

            Snapshot before = Snapshot.Of(scratch);

            Assert.Throws<InvalidOperationException>(() =>
                InterfaceStructureWriter.Apply(scratch.Cache, scratch.GroupId, everything.Build()));

            before.AssertNothingChanged(scratch);
        }

        /// <summary>
        ///     A plan that inserts and a caller that supplies nothing to insert is a mistake rather
        ///     than an append.
        /// </summary>
        [RealCacheFact]
        public void APlanThatInsertsWithNoComponentSupplied_IsRefused() {
            Scratch scratch = Copy();
            InterfaceComponentTree tree = TreeOf(scratch.Cache, scratch.GroupId);

            InterfaceStructureEdit plan = InterfaceComponentEdits.PlanInsert(tree, tree.Roots[0], -1);

            Snapshot before = Snapshot.Of(scratch);
            Assert.Throws<ArgumentException>(() =>
                InterfaceStructureWriter.Apply(scratch.Cache, scratch.GroupId, plan));
            before.AssertNothingChanged(scratch);
        }

        // ===================================================================
        //  A real interface, copied somewhere writable
        // ===================================================================

        /// <summary>One real interface in a temporary cache of its own.</summary>
        private sealed class Scratch {
            internal Scratch(RSCache cache, int groupId, int sourceGroupId) {
                Cache = cache;
                GroupId = groupId;
                SourceGroupId = sourceGroupId;
            }

            internal RSCache Cache { get; }

            /// <summary>Where the copy sits in the temporary cache, which is always slot 0.</summary>
            internal int GroupId { get; }

            /// <summary>Which real interface it was copied from, for the output only.</summary>
            internal int SourceGroupId { get; }
        }

        /// <summary>
        ///     Copies one real interface into a temporary cache that can be written to.
        /// </summary>
        /// <remarks>
        ///     <b>The copy lands in slot 0 whatever interface it came from</b>, because
        ///     <c>RSFileStore.Write</c> requires archive ids to be contiguous and would otherwise
        ///     demand seventeen placeholder groups before it to reach interface 18. The component
        ///     bytes do not change with the group id - a component's payload carries no group of
        ///     its own, and the folded <c>(interface &lt;&lt; 16) | component</c> values in its hook
        ///     arguments are stored as raw integers - so every byte assertion here is unaffected.
        ///     The one consequence: an intra-interface hook reference in the copy no longer names
        ///     the group it sits in, so the planner's detectable-reference warning cannot fire on
        ///     it, and <see cref="AnyRenumbering_CarriesTheWarningAboutReferencesItCannotSee"/>
        ///     asserts the unconditional CS2 warning instead - the one that matters, because it is
        ///     the one about references nothing can find.
        ///     <para>
        ///     Stored with no compression, so an edit and its inverse can be compared as stored
        ///     bytes. The CRC is computed over what is actually written rather than copied from the
        ///     real table, or a no-op could not be told from a recompute.
        ///     </para>
        /// </remarks>
        /// <returns>The temporary cache and the interface inside it.</returns>
        private Scratch Copy() {
            RSCache real = _fixture.OpenCache();
            int sourceGroupId = PickInterface(real);
            const int groupId = 0;

            byte[] payload = real.GetContainer(Index, sourceGroupId).GetStream().ToArray();
            RSArchiveEntry source = real.GetReferenceTable(Index).GetArchiveEntry(sourceGroupId);

            string dir = Path.Combine(_dir, "in-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(dir);

            //Sector 0 is burned: allocation derives the next free sector from the data length, and
            //sector id 0 is the chain terminator.
            File.WriteAllBytes(Path.Combine(dir, "main_file_cache.dat2"), new byte[520]);
            File.WriteAllBytes(Path.Combine(dir, "main_file_cache.idx" + Index), Array.Empty<byte>());

            /* idx255 is padded to reach slot 3, because RSFileStore.Write requires archive ids to
               be contiguous and index 3's reference table is archive 3 of the meta index. A padded
               record reads back as size 0 at sector 0, which every reader here treats as absent -
               RSCache.LoadContainer and RSCache's own liveness test both reject sector 0, which is
               the end-of-chain marker rather than a location. */
            File.WriteAllBytes(Path.Combine(dir, "main_file_cache.idx" + RSConstants.META_INDEX),
                new byte[Index * RSIndex.SIZE]);

            var store = new RSFileStore(dir);
            _stores.Add(store);

            JagStream stored = new RSContainer(Index, groupId, RSConstants.NO_COMPRESSION,
                new JagStream(payload), source.GetVersion()).Encode();
            byte[] storedBytes = stored.ToArray();

            store.Write(Index, groupId, new JagStream(storedBytes));
            store.Write(RSConstants.META_INDEX, Index, EncodeTable(groupId, source, storedBytes));

            return new Scratch(new RSCache(store), groupId, sourceGroupId);
        }

        /// <summary>
        ///     A reference table declaring the one copied interface, with an honest CRC.
        /// </summary>
        /// <remarks>
        ///     The identifiers flag is copied from the real table rather than assumed: index 3 sets
        ///     it in both supported caches, and a fixture that hardcoded it would stop testing the
        ///     identifier half the moment that stopped being true.
        /// </remarks>
        /// <param name="groupId">The interface id.</param>
        /// <param name="source">The real table's entry for it.</param>
        /// <param name="storedBytes">The container as it was just written.</param>
        /// <returns>The encoded table container.</returns>
        private JagStream EncodeTable(int groupId, RSArchiveEntry source, byte[] storedBytes) {
            RSReferenceTable real = _fixture.Table(Index);
            var table = new RSReferenceTable {
                format = real.format,
                version = real.version,
                flags = real.flags
            };

            var entry = new RSArchiveEntry(groupId);
            entry.SetIdentifier(source.GetIdentifier());
            entry.SetVersion(source.GetVersion());
            entry.SetCrc(unchecked((int) FlashEditor.Cache.Util.CRC32Helper.ComputeCrc32(
                storedBytes.AsSpan(0, storedBytes.Length - 2))));

            int[] ids = source.GetValidFileIds();
            entry.SetValidFileIds(ids);

            var fileEntries = new SortedDictionary<int, RSFileEntry>();
            foreach (int id in ids) {
                var child = new RSFileEntry(id);
                child.SetIdentifier(source.GetFileEntry(id).GetIdentifier());
                fileEntries[id] = child;
            }

            entry.SetFileEntries(fileEntries);
            table.PutArchiveEntry(groupId, entry);

            return new RSContainer(RSConstants.META_INDEX, Index, RSConstants.GZIP_COMPRESSION,
                ReferenceTableCodec.Encode(table), -1).Encode();
        }

        /// <summary>
        ///     The interface these tests operate on, derived rather than named.
        /// </summary>
        /// <remarks>
        ///     Derived because the two supported caches disagree on index 3 - the repack holds
        ///     eleven more interfaces - so a hardcoded id would be a different interface in each.
        ///     The requirements come from what the tests need to exercise: a subtree deep enough
        ///     that a delete cascades, a parent with three children so a reorder has somewhere to
        ///     go, and few enough components that a failure prints something a human can read.
        /// </remarks>
        /// <param name="cache">The real cache.</param>
        /// <returns>The chosen interface id.</returns>
        private int PickInterface(RSCache cache) {
            RSReferenceTable table = cache.GetReferenceTable(Index);

            foreach (int groupId in cache.EnumerateGroups(Index)) {
                RSArchiveEntry entry = table.GetArchiveEntry(groupId);
                if (entry == null || entry.GetValidFileIds().Length is < 6 or > 60)
                    continue;

                InterfaceComponentTree tree = TreeOf(cache, groupId);

                bool hasGrandchild = tree.Components.Keys.Any(id =>
                    tree.ParentageOf(id) == InterfaceParentage.Child
                    && tree.ChildrenOf(id).Count > 0);
                bool hasThreeSiblings = tree.Components.Keys.Any(id => tree.ChildrenOf(id).Count >= 3);
                if (hasGrandchild && hasThreeSiblings && TryPickRestorableLeaf(tree, out _))
                    return groupId;
            }

            throw new InvalidOperationException(
                "No interface in this cache has a nested subtree, a parent with three children and " +
                "a leaf whose deletion can be undone through PlanInsert, which is what every " +
                "structural test here needs between them.");
        }

        private static InterfaceComponentTree TreeOf(RSCache cache, int groupId) {
            var components = new List<InterfaceComponentDefinition>();
            foreach (KeyValuePair<int, JagStream> file in cache.ReadGroup(Index, groupId)) {
                file.Value.Seek0();
                components.Add(new InterfaceComponentDefinition(groupId, file.Key).Decode(file.Value));
            }

            return InterfaceComponentTree.Build(groupId, components);
        }

        /// <summary>
        ///     Commits the cache to a fresh directory and reopens it, so assertions run against
        ///     bytes that made a full round trip through the file store.
        /// </summary>
        /// <param name="cache">The cache to commit.</param>
        /// <returns>A cache over the committed bytes.</returns>
        private RSCache SaveAndReopen(RSCache cache) {
            string outDir = Path.Combine(_dir, "out-" + Guid.NewGuid().ToString("N"));
            cache.WriteCache(outDir);

            var reopened = new RSFileStore(outDir);
            _stores.Add(reopened);
            return new RSCache(reopened);
        }

        /// <summary>
        ///     Everything a write to this index touches.
        /// </summary>
        /// <remarks>
        ///     Compared rather than <c>HasUnsavedChanges</c>, which is already true from seeding.
        ///     It is the stronger statement anyway, because it catches a rewrite that produced the
        ///     same container bytes at a different sector - which still moves the dat2 under
        ///     everything after it.
        /// </remarks>
        private sealed class Snapshot {
            private byte[] _archive = Array.Empty<byte>();
            private byte[] _table = Array.Empty<byte>();
            private byte[] _indexRecords = Array.Empty<byte>();
            private byte[] _metaRecords = Array.Empty<byte>();
            private long _dataLength;

            internal static Snapshot Of(Scratch scratch) {
                return new Snapshot {
                    _archive = scratch.Cache.LoadContainer(Index, scratch.GroupId).ToArray(),
                    _table = scratch.Cache.LoadContainer(RSConstants.META_INDEX, Index).ToArray(),
                    _indexRecords = scratch.Cache.GetStore().GetIndexEntry(Index).GetStream().ToArray(),
                    _metaRecords = scratch.Cache.GetStore().GetIndexEntry(RSConstants.META_INDEX)
                        .GetStream().ToArray(),
                    _dataLength = scratch.Cache.GetStore().dataChannel.Length
                };
            }

            internal void AssertNothingChanged(Scratch scratch) {
                Snapshot after = Of(scratch);
                Assert.Equal(_archive, after._archive);
                Assert.Equal(_table, after._table);
                Assert.Equal(_indexRecords, after._indexRecords);
                Assert.Equal(_metaRecords, after._metaRecords);
                Assert.Equal(_dataLength, after._dataLength);
            }
        }

        /// <summary>
        ///     Builds a plan the planner would never produce.
        /// </summary>
        /// <remarks>
        ///     The writer's refusals have to be testable, and every operation
        ///     <c>InterfaceComponentEdits</c> offers closes the numbering - so the only way to reach
        ///     the density check is to hand the writer a plan built by hand. That is not a
        ///     contrivance: it is exactly the shape a sixth operation added later without the
        ///     closing step would take, and the check exists so that it fails loudly instead of
        ///     writing a group the client reads short.
        /// </remarks>
        private sealed class InterfaceStructureEditBuilder {
            private readonly List<int> _removed = new List<int>();

            internal InterfaceStructureEditBuilder Removing(int fileId) {
                _removed.Add(fileId);
                return this;
            }

            internal InterfaceStructureEdit Build() {
                _removed.Sort();

                //The constructor is internal rather than public for exactly this reason: a plan is
                //something the planner produces, and the one place that builds one by hand is a
                //test proving the writer does not trust it.
                return new InterfaceStructureEdit(new Dictionary<int, int>(), _removed, -1,
                    Array.Empty<string>());
            }
        }
    }
}
