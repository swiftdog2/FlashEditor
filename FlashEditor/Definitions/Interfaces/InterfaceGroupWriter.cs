using System;
using System.Collections.Generic;
using FlashEditor.Cache;
using FlashEditor.Cache.Util;
using FlashEditor.IO;

namespace FlashEditor.Definitions.Interfaces {
    /// <summary>
    ///     Makes and copies whole interfaces, as opposed to editing the components inside one.
    /// </summary>
    /// <remarks>
    ///     The split against <see cref="InterfaceStructureWriter"/> is the group boundary: that
    ///     changes which components a group holds, this changes which groups exist. They share
    ///     <see cref="InterfaceGroupRules"/>, because a group being made has to satisfy the same
    ///     numbering rule as one being edited.
    ///     <para>
    ///     <b>Creating an interface is the one index-3 structural operation that is safe by
    ///     construction.</b> Every other one renumbers components, and a renumber silently
    ///     re-points the <c>(interface &lt;&lt; 16) | component</c> references held by CS2 scripts
    ///     in index 12, which nothing in this project can scan for. Nothing anywhere points at a
    ///     group that did not exist a moment ago.
    ///     </para>
    ///     <para>
    ///     Staged like every other write. Nothing reaches the filesystem until
    ///     <c>RSCache.WriteCache</c>.
    ///     </para>
    /// </remarks>
    public static class InterfaceGroupWriter {
        private const int Index = RSConstants.INTERFACE_DEFINITIONS_INDEX;

        /// <summary>
        ///     Creates a new interface holding the components given.
        /// </summary>
        /// <remarks>
        ///     <b>A name is required here, unlike at the archive layer.</b> Unnamed groups are legal
        ///     in the format and index 3 carries eleven of them, but an interface the editor makes
        ///     with no name can never be addressed by one afterwards: names are stored as hashes and
        ///     recovered by re-hashing candidates, so a name not chosen now is not a name that can
        ///     be chosen later without rewriting the group.
        ///     <para>
        ///     <b>The components cannot be empty and must be numbered from zero.</b> A group with no
        ///     payload cannot be stored at all, and the client reads a group's file count as the
        ///     highest file id plus one.
        ///     </para>
        /// </remarks>
        /// <param name="cache">The open cache.</param>
        /// <param name="name">The interface's name. Hashed, and refused if already taken.</param>
        /// <param name="components">
        ///     Its components, numbered 0 to n-1. Each is encoded as it stands.
        /// </param>
        /// <returns>The group id the new interface took.</returns>
        /// <exception cref="ArgumentNullException">The cache or the component list is null.</exception>
        /// <exception cref="ArgumentException">The name is blank.</exception>
        /// <exception cref="InvalidOperationException">
        ///     The components are empty or not dense, or the name is already taken.
        /// </exception>
        public static int Create(RSCache cache, string name,
            IReadOnlyList<InterfaceComponentDefinition> components) {
            if (cache == null)
                throw new ArgumentNullException(nameof(cache));
            if (components == null)
                throw new ArgumentNullException(nameof(components));

            int identifier = RequireName(cache, name);
            int groupId = cache.NextCreatableGroupId(Index);

            var files = new List<RSGroupFile>(components.Count);
            for (int i = 0; i < components.Count; i++) {
                InterfaceComponentDefinition component = components[i];

                /* The component is told which group it is in, because a parent reference is stored
                   as a bare sibling index and folded against the group id on read. A component
                   built against another group would encode a parent that resolves elsewhere. */
                if (component.GroupId != groupId)
                    throw new InvalidOperationException(
                        "Component at position " + i + " was built for interface " + component.GroupId +
                        " and this creates interface " + groupId + ". A parent reference is folded" +
                        " against the group id on read, so it has to be built for the group it lands in.");

                files.Add(new RSGroupFile(component.FileId, component.Encode(),
                    NameHasher.GetNameHash("com_" + component.FileId)));
            }

            files.Sort((left, right) => left.FileId.CompareTo(right.FileId));
            InterfaceGroupRules.RequireDenseNumbering(groupId, files);

            cache.CreateGroup(Index, groupId, files, identifier);
            InterfaceNames.RememberCreatedName(groupId, name);

            return groupId;
        }

        /// <summary>
        ///     Hashes a proposed interface name, refusing a blank or an already-taken one.
        /// </summary>
        /// <remarks>
        ///     The collision test is on the hash rather than the name, because the hash is all the
        ///     cache stores and all the client compares. Two groups answering to one name leave one
        ///     of them unreachable by name, and which one depends on the order the client's hash
        ///     table happens to reach them.
        /// </remarks>
        /// <param name="cache">The open cache.</param>
        /// <param name="name">The proposed name.</param>
        /// <returns>Its hash.</returns>
        private static int RequireName(RSCache cache, string name) {
            if (string.IsNullOrWhiteSpace(name))
                throw new ArgumentException(
                    "An interface created here has to be named. Index 3 stores a name as a hash and" +
                    " the editor recovers it by re-hashing candidates, so a group left unnamed now" +
                    " cannot be given a name later without rewriting it.", nameof(name));

            int identifier = NameHasher.GetNameHash(name);
            int taken = cache.GetReferenceTable(Index).Names.GroupIdOfHash(identifier);

            if (taken >= 0)
                throw new InvalidOperationException(
                    "Interface " + taken + " is already called \"" + name + "\". Two interfaces" +
                    " sharing a name leave one of them unreachable by name, because the client stops" +
                    " at the first its hash table reaches.");

            return identifier;
        }
    }
}
