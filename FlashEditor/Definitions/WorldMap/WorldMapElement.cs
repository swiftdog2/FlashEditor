using System;
using FlashEditor.IO;

namespace FlashEditor.Definitions.WorldMap {
    /// <summary>
    ///     One fixed-position icon on an area's overview map, from a
    ///     <c>&lt;area&gt;_staticelements</c> group.
    /// </summary>
    /// <remarks>
    ///     Seven bytes, no opcodes: a packed position, a map-element id and a members-only flag.
    ///     Read order is <c>Class52.method491</c> (Class52.java:89-91).
    ///     <para>
    ///     <b>The id is a map element, not an object.</b> <c>Class278.method3302</c> hands it
    ///     straight to <c>Node_Sub47</c>, which is the same slot the tile stream fills from an
    ///     object's opcode 107 - so this index's two element references point at different things
    ///     and only this one resolves directly into config group
    ///     <see cref="FlashEditor.Cache.RSConstants.MAP_ELEMENT_GROUP"/>. Measured over the whole index: every
    ///     one of the 869 distinct ids stored here is a declared file of that group, and 38 of them
    ///     are not object ids at all, so the join cannot be the other one.
    ///     </para>
    ///     <para>
    ///     <b>It is a file id, not a position in the group's id list, and the client says so
    ///     outright.</b> The value reaches <c>Class341.method3807</c> unmodified - stored at
    ///     Class278.java:476, carried as <c>Node_Sub47.anInt4268</c> (Node_Sub47.java:57-61), passed
    ///     by every consumer (Class86.java:36, Class202.java:228, Particle_Sub3.java:19,
    ///     Class256_Sub1.java:54, Particle_Sub4.java:66-67) - and that method resolves it as
    ///     <c>getChildFromFolder(36, i_0_)</c> (Class341.java:185) against index 2
    ///     (Class341.java:140, InterfaceSettings.java:160,273-274), which is a plain
    ///     <c>(group, file)</c> accessor (JS5Archive.java:203-205). The client splits an id into
    ///     group and file elsewhere - <c>i &gt;&gt;&gt; 8</c> and <c>i &amp; 0xff</c> for locations
    ///     at Class302.java:96 - and does none of it here. This matters because group 36's ids are
    ///     dense from zero in this cache, so id and list position are the same number and no
    ///     measurement of the data can tell the two readings apart.
    ///     </para>
    /// </remarks>
    public sealed class WorldMapElement {
        /// <summary>The value of <see cref="MembersOnly"/> the client drops on a free world.</summary>
        /// <remarks><c>Class52.java:92</c> spells it <c>(x ^ 0xffffffff) == -2</c>, which is <c>== 1</c>.</remarks>
        public const int MembersOnlyValue = 1;

        private const int PlaneShift = 28;
        private const int PlaneMask = 0x3;
        private const int CoordinateBits = 14;
        private const int CoordinateMask = 0x3FFF;

        /// <summary>The element's file id within its group.</summary>
        /// <remarks>
        ///     File ids are sparse on this index - one staticelements group holds 0, 1, 3, 4, 5 and
        ///     another holds 0 and 2 - so this is read off the reference table rather than counted.
        ///     It carries no meaning of its own: the client walks whatever ids the table declares.
        /// </remarks>
        public int Id { get; set; } = -1;

        /// <summary>
        ///     Where the icon sits, packed as <c>plane &lt;&lt; 28 | x &lt;&lt; 14 | y</c>.
        /// </summary>
        /// <remarks>
        ///     Kept packed because that is what the record stores. The split is
        ///     <c>Class278.java:473-474</c>, and <b>which half is x cannot be read off that line</b>:
        ///     it passes <c>packed &amp; 0x3fff</c> and <c>packed &gt;&gt; 14 &amp; 0x3fff</c> into an
        ///     obfuscated parameter list. The data settles it instead. Every area's details record
        ///     declares the world rectangles it copies tiles from, so taking the high half as x puts
        ///     all 965 placements inside their own area on plane, x and y, while swapping the halves
        ///     puts 486 of them inside - measured by
        ///     <c>RealCacheWorldMapIconJoinTests.EveryPlacementSitsInsideItsOwnAreasSourceRectangle</c>.
        /// </remarks>
        public int PackedPosition { get; set; }

        /// <summary>The plane the icon sits on.</summary>
        public int Plane => (PackedPosition >> PlaneShift) & PlaneMask;

        /// <summary>The icon's world x, in tiles.</summary>
        public int X => (PackedPosition >> CoordinateBits) & CoordinateMask;

        /// <summary>The icon's world y, in tiles.</summary>
        public int Y => PackedPosition & CoordinateMask;

        /// <summary>The map element drawn here: a file id in config group 36.</summary>
        public int MapElementId { get; set; }

        /// <summary>The members-only byte exactly as stored.</summary>
        public byte MembersOnly { get; set; }

        /// <summary>Whether a free world hides this icon.</summary>
        public bool HiddenOnFreeWorlds => MembersOnly == MembersOnlyValue;

        /// <summary>Reads one static-element record.</summary>
        /// <param name="stream">The file, positioned at its first byte.</param>
        /// <returns>This definition.</returns>
        public WorldMapElement Decode(JagStream stream) {
            if (stream == null)
                throw new ArgumentNullException(nameof(stream));

            PackedPosition = stream.ReadInt();
            MapElementId = stream.ReadUnsignedShort();
            MembersOnly = (byte) stream.ReadUnsignedByte();
            return this;
        }

        /// <summary>Writes this record back to the file representation.</summary>
        /// <returns>The encoded file, positioned at 0.</returns>
        public JagStream Encode() {
            var stream = new JagStream(7);
            stream.WriteInteger(PackedPosition);
            stream.WriteShort(MapElementId);
            stream.WriteByte(MembersOnly);
            return stream.Flip();
        }
    }
}
