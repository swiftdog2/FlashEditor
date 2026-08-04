namespace FlashEditor.Cache.Region {
    /// <summary>
    ///     One entry of a map square's NPC spawn table, the index-5 <c>n</c> family.
    /// </summary>
    /// <remarks>
    ///     Four bytes on the wire: an unsigned short packing the plane and the square-local tile,
    ///     then an unsigned short NPC id. The reader is
    ///     <c>Particle_Sub3_Sub2.method3005</c> (<c>:233-246</c>), which loops until the buffer is
    ///     exhausted rather than reading a count, and unpacks
    ///     <c>plane = p &gt;&gt; 14</c>, <c>localX = (p &gt;&gt; 7) &amp; 0x3f</c>,
    ///     <c>localY = p &amp; 0x3f</c>. The client spells the middle field
    ///     <c>(p &amp; 0x1fd6) &gt;&gt; 7</c>; the low bits of that constant are shifted straight
    ///     out, so it is the same six-bit field.
    ///
    ///     <para>
    ///     The client also stops after 511 records and after 1023 spawns across the whole scene.
    ///     Those are limits of its scene arrays, not of the format, so nothing here reproduces them
    ///     - an editor that silently dropped the 512th record would write a shorter file back.
    ///     </para>
    /// </remarks>
    public sealed class NpcSpawn {
        /// <summary>Creates a spawn.</summary>
        /// <param name="npcId">The NPC definition id.</param>
        /// <param name="plane">Plane, 0..3.</param>
        /// <param name="localX">Tile X within the map square, 0..63.</param>
        /// <param name="localY">Tile Y within the map square, 0..63.</param>
        public NpcSpawn(int npcId, int plane, int localX, int localY) {
            NpcId = npcId;
            Plane = plane;
            LocalX = localX;
            LocalY = localY;
        }

        /// <summary>The NPC definition id.</summary>
        public int NpcId { get; }

        /// <summary>Plane, 0..3.</summary>
        public int Plane { get; }

        /// <summary>Tile X within the map square, 0..63.</summary>
        public int LocalX { get; }

        /// <summary>Tile Y within the map square, 0..63.</summary>
        public int LocalY { get; }

        /// <summary>
        ///     The packed position word this spawn encodes to.
        /// </summary>
        /// <remarks>
        ///     Bits 6 and 13 belong to no field. Nothing here keeps them, so if a record ever set
        ///     one it would be lost on save - which is why the byte-identity sweep over every
        ///     shipped table is the thing that pins them clear, rather than a comment saying so.
        ///     Contrast the terrain height byte, which genuinely cannot be recomputed and is kept
        ///     verbatim.
        /// </remarks>
        public int PackedPosition => (Plane << 14) | (LocalX << 7) | LocalY;
    }
}
