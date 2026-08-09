using System;
using System.Collections.Generic;
using FlashEditor.IO;

namespace FlashEditor.Definitions.Defaults {
    /// <summary>
    ///     Group 1 of JS5 index 28: the default environment cube map and the player-title enum
    ///     tables.
    /// </summary>
    /// <remarks>
    ///     Index 28 is not a record table. It holds two unrelated config blobs, one per group, and
    ///     the client reads both by literal id - <c>method2733(1, 14)</c> and <c>method2733(3, 82)</c>
    ///     at InterfaceSettings.java:234-235. Each group is a single-file archive, so a group is one
    ///     record. <c>JS5Archive.method2733</c> throws when a group's file count is not 1
    ///     (JS5Archive.java:612), so turning either into a multi-file archive crashes the client at
    ///     load rather than degrading.
    ///     <para>
    ///     Decoded by <c>Class276.method3284</c> (Class276.java:7-51). What the fields are was
    ///     settled from what the client does with them, because the index constant and
    ///     <c>AGENTS.md</c>'s "default sprite ids and colours" are both wrong: opcode 1's six ids go
    ///     through the <c>Class260</c> texture provider into a <c>Class42_Sub2</c> whose GL target is
    ///     34067, <c>GL_TEXTURE_CUBE_MAP</c> (Class42_Sub2.java:140), so they are texture ids and the
    ///     six faces of one cube map. Opcode 4's ids are enum ids indexed by the player's rank byte
    ///     (Player.java:479-490), and opcode 5 is the same table for the other gender.
    ///     </para>
    /// </remarks>
    public sealed class SceneDefaultsDefinition : OpcodeStreamDefinition {
        /// <summary>The group id the client reads this record from.</summary>
        public const int GroupId = 1;

        /// <summary>Faces in the cube map, and so how many ids opcode 1 carries.</summary>
        public const int CubeMapFaces = 6;

        /// <summary>The stored value that means "no id", on opcodes 4 and 5.</summary>
        /// <remarks>
        ///     Class276.java:32-33 and :41-42 map it to -1. Kept as a named constant because the
        ///     encoder has to put the sentinel back rather than write -1, and no entry in either
        ///     supported cache uses it - so a mistake here passes every byte-identity sweep and
        ///     breaks on the first cache that does.
        /// </remarks>
        public const int NoId = 0xFFFF;

        /// <summary>
        ///     Opcode 1. The six texture ids forming the default environment cube map, or null when
        ///     the record did not carry it.
        /// </summary>
        /// <remarks>
        ///     Null rather than an array of -1: the client leaves <c>Class50.anIntArray417</c> null
        ///     when the opcode is absent, and every scene tile reads that field
        ///     (Class28.java:47,124).
        /// </remarks>
        public int[]? CubeMapTextureIds { get; set; }

        /// <summary>
        ///     Opcode 4. Enum ids holding the male player titles, indexed by rank, or null when
        ///     absent.
        /// </summary>
        public int[]? MaleTitleEnumIds { get; set; }

        /// <summary>
        ///     Opcode 5. The same table for female players, or null when absent.
        /// </summary>
        /// <remarks>
        ///     Absent in both supported caches, and the absence is load bearing: <c>Player.java:479</c>
        ///     branches on <c>Class35.anIntArray333 != null</c>, so materialising an empty array here
        ///     and writing it back changes what the client does for female characters.
        /// </remarks>
        public int[]? FemaleTitleEnumIds { get; set; }

        /// <summary>Reads the record.</summary>
        /// <param name="stream">The group's single file, positioned at its first opcode.</param>
        /// <returns>This definition.</returns>
        public SceneDefaultsDefinition Decode(JagStream stream) {
            Opcodes.Clear();
            DecodeOpcodeStream(stream);
            return this;
        }

        /// <inheritdoc/>
        protected override bool DecodeOpcode(JagStream stream, int opcode) {
            switch (opcode) {
                case 1: {
                        var ids = new int[CubeMapFaces];
                        for (int i = 0; i < ids.Length; i++)
                            ids[i] = stream.ReadUnsignedShort();
                        CubeMapTextureIds = ids;
                        return true;
                    }

                case 4:
                    MaleTitleEnumIds = ReadIdTable(stream);
                    return true;

                case 5:
                    FemaleTitleEnumIds = ReadIdTable(stream);
                    return true;

                default:
                    return false;
            }
        }

        /// <summary>Writes the record back.</summary>
        /// <returns>The encoded file, positioned at 0.</returns>
        public JagStream Encode() {
            var records = new List<KeyValuePair<int, byte[]>>();

            /* Pattern-matched into a non-nullable local rather than tested in place: a lambda does
               not carry the enclosing method's null-state, so the closure would read the property
               as possibly null however it was guarded. */
            if (CubeMapTextureIds is int[] cubeMap) {
                records.Add(Payload(1, buffer => {
                    foreach (int id in cubeMap)
                        buffer.WriteShort(id);
                }));
            }

            if (MaleTitleEnumIds is int[] maleTitles)
                records.Add(Payload(4, buffer => WriteIdTable(buffer, maleTitles)));
            if (FemaleTitleEnumIds is int[] femaleTitles)
                records.Add(Payload(5, buffer => WriteIdTable(buffer, femaleTitles)));

            //The blocks above are already in ascending opcode order, so an opcode this record did
            //not carry lands in a predictable place without a further sort.
            return Opcodes.Replay(records);
        }

        /// <summary>Reads a count-prefixed table of ids, mapping the sentinel to -1.</summary>
        /// <param name="stream">The stream, positioned at the count byte.</param>
        /// <returns>The ids.</returns>
        private static int[] ReadIdTable(JagStream stream) {
            int count = stream.ReadUnsignedByte();
            var ids = new int[count];
            for (int i = 0; i < count; i++) {
                int id = stream.ReadUnsignedShort();
                ids[i] = id == NoId ? -1 : id;
            }
            return ids;
        }

        /// <summary>Writes a count-prefixed table of ids, putting the sentinel back.</summary>
        /// <param name="buffer">The payload buffer.</param>
        /// <param name="ids">The ids to write.</param>
        private static void WriteIdTable(JagStream buffer, int[] ids) {
            buffer.WriteByte((byte) ids.Length);
            foreach (int id in ids)
                buffer.WriteShort(id == -1 ? NoId : id);
        }

        /// <summary>Builds one opcode's payload into its own buffer.</summary>
        /// <param name="opcode">The opcode the payload belongs to.</param>
        /// <param name="write">Writes the payload.</param>
        /// <returns>The opcode paired with its bytes.</returns>
        private static KeyValuePair<int, byte[]> Payload(int opcode, Action<JagStream> write) {
            var buffer = new JagStream();
            write(buffer);
            return new KeyValuePair<int, byte[]>(opcode, buffer.Flip().ToArray());
        }
    }
}
