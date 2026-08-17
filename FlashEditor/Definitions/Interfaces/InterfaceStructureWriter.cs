using System;
using System.Collections.Generic;
using FlashEditor.Cache;
using FlashEditor.IO;

namespace FlashEditor.Definitions.Interfaces {
    /// <summary>
    ///     Applies an <see cref="InterfaceStructureEdit"/> to the cache.
    /// </summary>
    /// <remarks>
    ///     <b>The planner and this are deliberately separate, and the split is not tidiness.</b>
    ///     <see cref="InterfaceComponentEdits"/> works out the renumbering, the subtree a delete
    ///     takes with it, and the references the edit breaks that nothing here can repair - all of
    ///     which a user has to be shown <i>before</i> anything is written. This is the half that
    ///     cannot be undone by looking at it again.
    ///     <para>
    ///     <b>Only the components whose parent field actually moves are re-encoded.</b> Everything
    ///     else is written back with the exact bytes it was read with, which is a stronger
    ///     guarantee than relying on the codec: the byte-identity sweeps say every component in
    ///     both supported caches re-encodes to what it was read from, but a structural edit has no
    ///     business testing that claim on 700 records in order to move one. A component whose id
    ///     changes but whose parent does not carries no trace of its own id in its bytes, so it
    ///     moves verbatim.
    ///     </para>
    ///     <para>
    ///     <b>Dense numbering is enforced here rather than in <c>RSCache.WriteGroup</c>.</b> Sparse
    ///     groups are legal in this format and common on other indexes, so the archive layer does
    ///     not impose density on every index. Index 3 is measured dense in every declared group
    ///     (<c>RealCacheInterfaceStructureTests.EveryInterface_NumbersItsComponentsDenselyFromZero</c>)
    ///     and the client depends on it: it derives a group's file count as <c>maxFileId + 1</c>
    ///     and discards the explicit id list whenever the two agree
    ///     (<c>VersionTable.java:183,185</c>), so a hole left in an interface is read with a file
    ///     count that does not match its contents. This is the caller that knows, so this is where
    ///     the check belongs.
    ///     </para>
    ///     <para>
    ///     Staged like every other write. Nothing reaches the filesystem until
    ///     <c>RSCache.WriteCache</c>.
    ///     </para>
    /// </remarks>
    public static class InterfaceStructureWriter {
        /// <summary>
        ///     Carries out a planned structural edit, or reports that it changes nothing.
        /// </summary>
        /// <remarks>
        ///     <b>An edit that changes nothing is refused before the group is read, let alone
        ///     rewritten.</b> Re-encoding a group rewrites its stored bytes and therefore its
        ///     archive CRC, which rewrites the reference-table entry that carries the CRC, which
        ///     rewrites the table container every other interface in the index shares - so a no-op
        ///     detected afterwards is a no-op that has already cost 1,067 entries their bytes.
        ///     <see cref="InterfaceStructureEdit.IsEmpty"/> is the first test here, and
        ///     <c>RSCache.WriteGroup</c> applies a second one against the stored payload for the
        ///     case where a plan is not empty and still lands on the same bytes.
        ///     <para>
        ///     Read through the cache rather than taken from the caller's decoded rows on purpose:
        ///     a cell edit is staged the moment it is committed, so the cache is where the current
        ///     bytes are, and taking them from a grid would silently write whatever that grid last
        ///     managed to decode.
        ///     </para>
        /// </remarks>
        /// <param name="cache">The open cache.</param>
        /// <param name="groupId">The interface being edited.</param>
        /// <param name="plan">What <see cref="InterfaceComponentEdits"/> worked out.</param>
        /// <param name="inserted">
        ///     The component to create, required when and only when the plan states an insertion.
        /// </param>
        /// <param name="insertedIdentifier">
        ///     The name hash to record for the created component. Defaults to
        ///     <see cref="RSGroupFile.Unnamed"/>, which is what a component created in the editor
        ///     has - a name it cannot be looked up by is worse than none, because index 3's names
        ///     are recovered by re-hashing candidates and an invented hash matches nothing forever.
        ///     Stated rather than assumed so that re-inserting a component that was just deleted
        ///     can put its name back with it.
        /// </param>
        /// <returns>Whether anything was staged.</returns>
        /// <exception cref="ArgumentNullException">The cache or the plan is null.</exception>
        /// <exception cref="ArgumentException">
        ///     The plan states an insertion and no component was supplied, or supplies one it did
        ///     not ask for.
        /// </exception>
        /// <exception cref="InvalidOperationException">
        ///     The edit would leave the group empty, or numbered with a hole.
        /// </exception>
        public static bool Apply(RSCache cache, int groupId, InterfaceStructureEdit plan,
            InterfaceComponentDefinition? inserted = null,
            int insertedIdentifier = RSGroupFile.Unnamed) {
            if (cache == null)
                throw new ArgumentNullException(nameof(cache));
            if (plan == null)
                throw new ArgumentNullException(nameof(plan));

            if (plan.Inserted >= 0 && inserted == null)
                throw new ArgumentException("The plan inserts a component at file id " + plan.Inserted +
                    " and none was supplied.", nameof(inserted));
            if (plan.Inserted < 0 && inserted != null)
                throw new ArgumentException("A component was supplied for a plan that inserts nothing.",
                    nameof(inserted));

            //Before the group is read. See the remarks: there is no cheap way back from a rewrite.
            if (plan.IsEmpty)
                return false;

            const int index = RSConstants.INTERFACE_DEFINITIONS_INDEX;

            RSArchiveEntry entry = cache.GetReferenceTable(index).GetArchiveEntry(groupId)
                ?? throw new InvalidOperationException(
                    "Interface " + groupId + " is not declared by index 3's reference table, so it" +
                    " cannot be edited - the client resolves every group through the table and could" +
                    " never load one it omits.");

            IReadOnlyDictionary<int, JagStream> stored = cache.ReadGroup(index, groupId);

            var removed = new HashSet<int>(plan.Removed);
            var files = new List<RSGroupFile>(stored.Count + 1);

            foreach (KeyValuePair<int, JagStream> file in stored) {
                int oldId = file.Key;
                if (removed.Contains(oldId))
                    continue;

                int newId = plan.Renumbering.TryGetValue(oldId, out int moved) ? moved : oldId;
                int identifier = entry.GetFileEntry(oldId)?.GetIdentifier() ?? RSGroupFile.Unnamed;

                files.Add(new RSGroupFile(newId, Repointed(groupId, oldId, file.Value, plan.Renumbering),
                    identifier));
            }

            if (inserted != null)
                files.Add(new RSGroupFile(plan.Inserted, inserted.Encode(), insertedIdentifier));

            //Ascending, because the reference table delta-encodes the id list and RSCache.WriteGroup
            //refuses anything else. Sorted here rather than assumed: a renumbering redistributes ids
            //among the components that keep them, so the enumeration order above is the OLD order.
            files.Sort((left, right) => left.FileId.CompareTo(right.FileId));

            RequireDenseNumbering(groupId, files);

            return cache.WriteGroup(index, groupId, files);
        }

        /// <summary>
        ///     One component's bytes, with its parent reference moved if the renumbering moved it.
        /// </summary>
        /// <remarks>
        ///     <b>The stored bytes are handed straight back where the parent did not move</b>, so a
        ///     component that merely changes id is written with the bytes it was read with rather
        ///     than with the codec's opinion of them. A file id appears nowhere in a component's
        ///     payload - it is the reference table that says which id a payload sits at - so
        ///     renumbering on its own genuinely changes no byte.
        ///     <para>
        ///     Only a changed parent forces a decode, and then the record really is different, so
        ///     the codec is being asked to do the thing it is proven to do rather than to reproduce
        ///     bytes nobody edited.
        ///     </para>
        /// </remarks>
        /// <param name="groupId">The interface, so a decoded component knows where it lives.</param>
        /// <param name="fileId">The component's current file id.</param>
        /// <param name="payload">Its stored bytes.</param>
        /// <param name="renumbering">Old file id to new file id.</param>
        /// <returns>The bytes to store.</returns>
        private static JagStream Repointed(int groupId, int fileId, JagStream payload,
            IReadOnlyDictionary<int, int> renumbering) {
            //Rewound because ReadGroup hands back the archive's own streams, and anything that has
            //already read one of them has left it at the end.
            payload.Seek0();

            var component = new InterfaceComponentDefinition(groupId, fileId).Decode(payload);
            payload.Seek0();

            if (component.RawParentId == InterfaceComponentDefinition.NoParent
                || !renumbering.TryGetValue(component.RawParentId, out int moved)) {
                return payload;
            }

            component.RawParentId = moved;
            return component.Encode();
        }

        /// <summary>
        ///     Refuses a file set that is not numbered 0 to n-1.
        /// </summary>
        /// <remarks>
        ///     The one rule index 3 imposes that the archive layer deliberately does not. See the
        ///     type's remarks: the client reads a group's file count as <c>maxFileId + 1</c> and
        ///     drops the explicit id list when that agrees with the declared count, so a hole is
        ///     read as a component that is not there and shifts every component after it.
        ///     <para>
        ///     A check rather than a fix. Closing a hole here would silently perform a renumbering
        ///     the user was never shown and never warned about, which is precisely what the
        ///     planner's warnings exist to prevent.
        ///     </para>
        /// </remarks>
        /// <param name="groupId">The interface, for the message.</param>
        /// <param name="files">The proposed contents, ascending.</param>
        private static void RequireDenseNumbering(int groupId, IReadOnlyList<RSGroupFile> files) {
            if (files.Count == 0)
                throw new InvalidOperationException(
                    "Interface " + groupId + " would be left with no components. A group with no" +
                    " payload cannot be stored, so deleting the last component means deleting the" +
                    " interface, which is a different operation.");

            for (int i = 0; i < files.Count; i++) {
                if (files[i].FileId == i)
                    continue;

                throw new InvalidOperationException(
                    "Interface " + groupId + " would be numbered with a hole: position " + i +
                    " holds component " + files[i].FileId + ". The client reads a group's file count" +
                    " as the highest id plus one, so a hole shifts every component after it onto a" +
                    " different id.");
            }
        }
    }
}
