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
        ///     Copies a whole interface into a new one.
        /// </summary>
        /// <remarks>
        ///     <b>The stored bytes are copied verbatim, never re-encoded.</b> The byte-identity
        ///     sweeps say every component in both supported caches re-encodes to what it was read
        ///     from, but a copy has no business testing that claim on 700 records in order to change
        ///     none of them.
        ///     <para>
        ///     The chunk split is not preserved, because <c>RSArchive.PutFile</c> produces a
        ///     single-chunk archive. That is safe here and only here: <c>AGENTS.md</c> measures
        ///     every multi-chunk group in this cache as living in index 0, so index 3 is
        ///     single-chunk throughout and the copy payload comes out identical to the source.
        ///     The same technique on index 0 would silently reshape the file layout.
        ///     </para>
        ///     <para>
        ///     <b>Per-file names are copied verbatim and that is correct here specifically.</b>
        ///     <see cref="InterfaceNames.MovedIdentifier"/> exists because a generated
        ///     <c>com_&lt;fileId&gt;</c> hash is a statement about the id it sits at, but a copy
        ///     preserves the numbering, so every generated name is still true of the file it lands
        ///     on. Any future copy that <i>renumbers</i> must route through it or it un-names every
        ///     generated-name component it moves.
        ///     </para>
        ///     <para>
        ///     <b>The group name is never copied</b>, which is why a new one is required: two
        ///     interfaces answering to one name leave one of them unreachable by name.
        ///     </para>
        /// </remarks>
        /// <param name="cache">The open cache.</param>
        /// <param name="sourceGroupId">The interface to copy.</param>
        /// <param name="name">The name for the copy.</param>
        /// <returns>The new group id, and what the copy carries over that it cannot fix.</returns>
        /// <exception cref="ArgumentNullException">The cache is null.</exception>
        /// <exception cref="ArgumentException">The name is blank.</exception>
        /// <exception cref="InvalidOperationException">
        ///     The source is not declared, or the name is already taken.
        /// </exception>
        public static DuplicateResult Duplicate(RSCache cache, int sourceGroupId, string name) {
            if (cache == null)
                throw new ArgumentNullException(nameof(cache));

            int identifier = RequireName(cache, name);

            RSArchiveEntry source = cache.GetReferenceTable(Index).GetArchiveEntry(sourceGroupId)
                ?? throw new InvalidOperationException(
                    "Interface " + sourceGroupId + " is not declared by index 3 reference table," +
                    " so it cannot be copied - the client resolves every group through the table" +
                    " and could never load it either.");

            IReadOnlyDictionary<int, JagStream> stored = cache.ReadGroup(Index, sourceGroupId);
            var files = new List<RSGroupFile>(stored.Count);

            foreach (KeyValuePair<int, JagStream> file in stored) {
                //Rewound and copied out, because ReadGroup hands back the archive own streams and
                //the source archive is released once this returns.
                file.Value.Seek0();

                files.Add(new RSGroupFile(file.Key, new JagStream(file.Value.ToArray()),
                    source.GetFileEntry(file.Key)?.GetIdentifier() ?? RSGroupFile.Unnamed));
            }

            files.Sort((left, right) => left.FileId.CompareTo(right.FileId));

            int groupId = cache.NextCreatableGroupId(Index);
            InterfaceGroupRules.RequireDenseNumbering(groupId, files);

            IReadOnlyList<string> warnings = CopiedReferenceWarnings(cache, sourceGroupId);

            cache.CreateGroup(Index, groupId, files, identifier);
            InterfaceNames.RememberCreatedName(groupId, name);

            return new DuplicateResult(groupId, warnings);
        }

        /// <summary>
        ///     What a copy of an interface carries over that it cannot repair.
        /// </summary>
        /// <remarks>
        ///     Exposed separately so a caller can show these before the copy is written rather than
        ///     after, which is the order <see cref="InterfaceComponentEdits"/> plans a structural
        ///     edit in.
        ///     <para>
        ///     <b>Reported and never acted on.</b> Rewriting the high half of every integer operand
        ///     that happens to equal the source group id is exactly the heuristic
        ///     <c>InterfaceComponentEdits.ExternalReferenceWarnings</c> deliberately only warns
        ///     about: an operand whose high half matches may be an ordinary constant, and rewriting
        ///     one corrupts it silently.
        ///     </para>
        /// </remarks>
        /// <param name="cache">The open cache.</param>
        /// <param name="sourceGroupId">The interface that would be copied.</param>
        /// <returns>The warnings, in the order they should be shown.</returns>
        public static IReadOnlyList<string> CopiedReferenceWarnings(RSCache cache, int sourceGroupId) {
            if (cache == null)
                throw new ArgumentNullException(nameof(cache));

            var referencing = new SortedSet<int>();

            foreach (KeyValuePair<int, JagStream> file in cache.ReadGroup(Index, sourceGroupId)) {
                file.Value.Seek0();
                InterfaceComponentDefinition component =
                    new InterfaceComponentDefinition(sourceGroupId, file.Key).Decode(file.Value);
                file.Value.Seek0();

                foreach (InterfaceScriptOperand[] hook in component.Hooks) {
                    foreach (InterfaceScriptOperand operand in hook) {
                        if (operand.TypeByte != InterfaceScriptOperand.IntegerType)
                            continue;

                        //A folded component id: the high half is a group and the low half a file.
                        if (operand.Integer >> 16 == sourceGroupId)
                            referencing.Add(file.Key);
                    }
                }
            }

            var warnings = new List<string>();

            if (referencing.Count > 0) {
                warnings.Add("Components " + string.Join(", ", referencing) + " carry hook arguments" +
                    " that address interface " + sourceGroupId + " by id. The copy keeps pointing at" +
                    " the original, so those hooks act on it rather than on the copy.");
            }

            warnings.Add("Nothing outside the copy names it. CS2 scripts in index 12 and hooks in" +
                " other interfaces address components as (interface << 16) | component, so" +
                " everything that drives interface " + sourceGroupId + " will go on driving it and" +
                " the copy will not be driven at all. Those references cannot be found or repaired" +
                " from here.");

            return warnings;
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

        /// <summary>
        ///     What <see cref="Duplicate"/> made, and what it could not fix.
        /// </summary>
        /// <param name="GroupId">The group id the copy took.</param>
        /// <param name="Warnings">
        ///     The references the copy carries over pointing at the original. Always non-empty: a
        ///     copy is never wired to anything, and a caller that showed nothing would be saying it
        ///     was.
        /// </param>
        public sealed record DuplicateResult(int GroupId, IReadOnlyList<string> Warnings);
    }
}
