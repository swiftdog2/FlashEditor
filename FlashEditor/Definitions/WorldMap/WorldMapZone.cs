namespace FlashEditor.Definitions.WorldMap {
    /// <summary>
    ///     One rectangle of the world copied onto an area's overview map.
    /// </summary>
    /// <remarks>
    ///     Seventeen bytes: a plane and eight unsigned shorts. What each short means was settled
    ///     from what the client compares it against, never from its position - the decompiled
    ///     constructor stores the nine arguments into fields in a scrambled order
    ///     (<c>Node_Sub6.java:170-180</c>), so reading the layout off the field names would pair
    ///     the wrong bounds together.
    ///     <list type="bullet">
    ///     <item><c>method976</c> gates on <c>x &gt;= a2 &amp;&amp; x &lt;= a4</c> and
    ///     <c>y &gt;= a3 &amp;&amp; y &lt;= a5</c>, so arguments two to five are the <b>source</b>
    ///     rectangle in world coordinates.</item>
    ///     <item><c>method977</c> gates on <c>a6 &lt;= x &lt;= a8</c> and <c>a7 &lt;= y &lt;= a9</c>,
    ///     so arguments six to nine are the <b>destination</b> rectangle on the overview map.</item>
    ///     <item><c>method982</c> translates between them as
    ///     <c>mapX = worldX - srcMinX + dstMinX</c>, which is what makes the pairing load bearing:
    ///     swapping a source bound for a destination bound still decodes and still round-trips, and
    ///     puts every icon in the wrong place.</item>
    ///     </list>
    /// </remarks>
    public sealed class WorldMapZone {
        /// <summary>The plane this rectangle is taken from, 0-3 in this cache.</summary>
        public int Plane { get; set; }

        /// <summary>West edge of the source rectangle, in world tiles.</summary>
        public int SourceMinX { get; set; }

        /// <summary>South edge of the source rectangle, in world tiles.</summary>
        public int SourceMinY { get; set; }

        /// <summary>East edge of the source rectangle, inclusive.</summary>
        public int SourceMaxX { get; set; }

        /// <summary>North edge of the source rectangle, inclusive.</summary>
        public int SourceMaxY { get; set; }

        /// <summary>West edge of where the rectangle lands on the overview map.</summary>
        public int DestinationMinX { get; set; }

        /// <summary>South edge of where the rectangle lands on the overview map.</summary>
        public int DestinationMinY { get; set; }

        /// <summary>East edge of the destination rectangle, inclusive.</summary>
        public int DestinationMaxX { get; set; }

        /// <summary>North edge of the destination rectangle, inclusive.</summary>
        public int DestinationMaxY { get; set; }

        /// <summary>Reads one zone record.</summary>
        /// <param name="stream">Positioned at the zone's first byte.</param>
        /// <returns>The decoded zone.</returns>
        public static WorldMapZone Decode(JagStream stream) {
            var zone = new WorldMapZone();
            zone.Plane = stream.ReadUnsignedByte();
            zone.SourceMinX = stream.ReadUnsignedShort();
            zone.SourceMinY = stream.ReadUnsignedShort();
            zone.SourceMaxX = stream.ReadUnsignedShort();
            zone.SourceMaxY = stream.ReadUnsignedShort();
            zone.DestinationMinX = stream.ReadUnsignedShort();
            zone.DestinationMinY = stream.ReadUnsignedShort();
            zone.DestinationMaxX = stream.ReadUnsignedShort();
            zone.DestinationMaxY = stream.ReadUnsignedShort();
            return zone;
        }

        /// <summary>Writes this zone back in the client's read order.</summary>
        /// <param name="stream">The stream to append to.</param>
        public void Encode(JagStream stream) {
            stream.WriteByte((byte) Plane);
            stream.WriteShort(SourceMinX);
            stream.WriteShort(SourceMinY);
            stream.WriteShort(SourceMaxX);
            stream.WriteShort(SourceMaxY);
            stream.WriteShort(DestinationMinX);
            stream.WriteShort(DestinationMinY);
            stream.WriteShort(DestinationMaxX);
            stream.WriteShort(DestinationMaxY);
        }
    }
}
