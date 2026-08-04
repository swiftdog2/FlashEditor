using System;
using System.Collections.Generic;

namespace FlashEditor.Definitions.WorldMap {
    /// <summary>
    ///     One world-map area, read from a file of index 23's <c>details</c> group.
    /// </summary>
    /// <remarks>
    ///     Fixed shape, no opcode chain: two NUL-terminated strings, two 32-bit ints, three bytes,
    ///     then a counted list of 17-byte zones. Read order is <c>Class48_Sub1.method457</c>
    ///     (Class48_Sub1.java:52-62); the constructor it feeds is
    ///     <c>Node_Sub46_Sub10.java:470-494</c>. The file id is the area id -
    ///     <c>Class278.java:176</c> keys the area cache on it.
    ///     <para>
    ///     Two fields cannot be recomputed from what the client keeps, and both are stored raw here:
    ///     the byte at <see cref="UnreadByte"/>, which the constructor is handed and never stores,
    ///     and <see cref="StoredZoom"/>, where a stored 255 is folded to 0 on the way in
    ///     (Node_Sub46_Sub10.java:483-485) so the two spellings of "zero" are indistinguishable
    ///     afterwards.
    ///     </para>
    /// </remarks>
    public sealed class WorldMapAreaDefinition {
        /// <summary>The stored zoom the client folds to zero.</summary>
        public const int ZoomStoredAsZero = 255;

        /// <summary>The value of <see cref="StoredEnabled"/> the client reads as enabled.</summary>
        /// <remarks>
        ///     An equality test rather than a non-zero test: <c>Class48_Sub1.java:54</c> spells it
        ///     <c>(readUnsignedByte() ^ 0xffffffff) == -2</c>, which is <c>== 1</c>. Anything else is
        ///     disabled, so the raw byte is kept and <see cref="Enabled"/> is a view over it.
        /// </remarks>
        public const int EnabledValue = 1;

        /// <summary>Bit width of each half of <see cref="PackedOrigin"/>.</summary>
        private const int CoordinateBits = 14;

        /// <summary>Mask isolating one coordinate of <see cref="PackedOrigin"/>.</summary>
        private const int CoordinateMask = 0x3FFF;

        /// <summary>The area id, which is this record's file id within the details group.</summary>
        public int Id { get; set; } = -1;

        /// <summary>
        ///     The area's internal name, which is also the name of its raster and element groups.
        /// </summary>
        /// <remarks>
        ///     Kept exactly as stored. One area is spelled <c>ft3_zanaris_HQ</c> here while its
        ///     group's identifier is the hash of the lower-cased form, so folding the case on decode
        ///     would round-trip wrongly and folding it on lookup is what actually resolves the group.
        /// </remarks>
        public string InternalName { get; set; } = string.Empty;

        /// <summary>The name shown to the player, for example "RuneScape Surface".</summary>
        public string DisplayName { get; set; } = string.Empty;

        /// <summary>
        ///     The world position the overview map opens at, packed as <c>x &lt;&lt; 14 | y</c>.
        /// </summary>
        /// <remarks>
        ///     Kept packed because that is what the record stores. The split is
        ///     <c>Class247.java:4856-4857</c>, which pushes <c>&gt;&gt; 14 &amp; 0x3fff</c> and
        ///     <c>&amp; 0x3fff</c> onto the script stack in that order.
        /// </remarks>
        public int PackedOrigin { get; set; }

        /// <summary>The origin's world x, in tiles.</summary>
        public int OriginX => (PackedOrigin >> CoordinateBits) & CoordinateMask;

        /// <summary>The origin's world y, in tiles.</summary>
        public int OriginY => PackedOrigin & CoordinateMask;

        /// <summary>
        ///     A flat RGB tint painted over the whole area, or -1 for none.
        /// </summary>
        /// <remarks>
        ///     Settled from use: <c>Class278.java:300-301</c> turns it into an opaque colour with
        ///     <c>~0xffffff | value</c> and only when it is not -1, so -1 is the absent marker and 0
        ///     is a real, black tint that the same code would draw. Both occur here.
        /// </remarks>
        public int TintColour { get; set; }

        /// <summary>The enabled byte exactly as stored.</summary>
        public byte StoredEnabled { get; set; }

        /// <summary>Whether the client treats this area as selectable.</summary>
        public bool Enabled => StoredEnabled == EnabledValue;

        /// <summary>The zoom byte exactly as stored, 255 included.</summary>
        public byte StoredZoom { get; set; }

        /// <summary>
        ///     The zoom the client ends up with, which is a preset compared against 37, 50, 75, 100
        ///     and 200 at <c>Class339.java:70-75</c>.
        /// </summary>
        public int Zoom => StoredZoom == ZoomStoredAsZero ? 0 : StoredZoom;

        /// <summary>
        ///     The byte the 637 client reads and discards.
        /// </summary>
        /// <remarks>
        ///     <c>Class48_Sub1.java:52-55</c> passes eight values to a constructor that stores seven;
        ///     the eighth has no field. It is 0 in every record of both caches, so a decoder that
        ///     dropped it would round-trip cleanly here and corrupt the first record that ever
        ///     carried something else. Its meaning is unknown and the name deliberately does not
        ///     guess at one.
        /// </remarks>
        public byte UnreadByte { get; set; }

        /// <summary>The rectangles of the world this area's overview map is assembled from.</summary>
        public List<WorldMapZone> Zones { get; } = new List<WorldMapZone>();

        /// <summary>Reads one details record.</summary>
        /// <param name="stream">The file, positioned at its first byte.</param>
        /// <returns>This definition.</returns>
        public WorldMapAreaDefinition Decode(JagStream stream) {
            if (stream == null)
                throw new ArgumentNullException(nameof(stream));

            InternalName = stream.ReadJagexString();
            DisplayName = stream.ReadJagexString();
            PackedOrigin = stream.ReadInt();
            TintColour = stream.ReadInt();
            StoredEnabled = (byte) stream.ReadUnsignedByte();
            StoredZoom = (byte) stream.ReadUnsignedByte();
            UnreadByte = (byte) stream.ReadUnsignedByte();

            int zoneCount = stream.ReadUnsignedByte();
            Zones.Clear();
            for (int i = 0 ; i < zoneCount ; i++)
                Zones.Add(WorldMapZone.Decode(stream));

            return this;
        }

        /// <summary>Writes this record back to the file representation.</summary>
        /// <returns>The encoded file, positioned at 0.</returns>
        /// <exception cref="InvalidOperationException">The zone list cannot be expressed by the format.</exception>
        public JagStream Encode() {
            if (Zones.Count > byte.MaxValue)
                throw new InvalidOperationException(
                    "A world-map area stores its zone count in one byte, so it cannot hold " +
                    Zones.Count + " zones.");

            var stream = new JagStream();
            stream.WriteJagexString(InternalName);
            stream.WriteJagexString(DisplayName);
            stream.WriteInteger(PackedOrigin);
            stream.WriteInteger(TintColour);
            stream.WriteByte(StoredEnabled);
            stream.WriteByte(StoredZoom);
            stream.WriteByte(UnreadByte);
            stream.WriteByte((byte) Zones.Count);

            foreach (WorldMapZone zone in Zones)
                zone.Encode(stream);

            return stream.Flip();
        }
    }
}
