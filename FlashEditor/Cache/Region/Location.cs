namespace FlashEditor.Cache.Region {
    /// <summary>
    ///     One static object placement decoded from a location file.
    /// </summary>
    /// <remarks>
    ///     Both the square-local coordinates and the absolute <see cref="Position"/> are kept. The
    ///     local ones are what the file actually encodes, and without them a decoded square cannot
    ///     be written back - the packed position word cannot be rebuilt from an absolute
    ///     coordinate without also knowing which square it came from.
    ///
    ///     See <c>reference/hydra-637-maps/03-locs-l.md</c>.
    /// </remarks>
    public class Location {
        /// <summary>The object definition id.</summary>
        public int Id { get; }

        /// <summary>
        ///     The shape, 0..22, selecting both the model and where it sits on the tile.
        /// </summary>
        /// <remarks>
        ///     0-3 wall, 4-8 wall decoration, 9-21 game object, 22 ground decoration
        ///     (Class64_Sub17.anIntArray3685).
        /// </remarks>
        public int Shape { get; }

        /// <summary>The rotation, 0..3.</summary>
        public int Orientation { get; }

        /// <summary>Tile X within the map square, 0..63.</summary>
        public int LocalX { get; }

        /// <summary>Tile Y within the map square, 0..63.</summary>
        public int LocalY { get; }

        /// <summary>Plane, 0..3.</summary>
        public int Plane { get; }

        /// <summary>Absolute world position.</summary>
        public Position Position { get; }

        /// <summary>Creates a location.</summary>
        /// <param name="id">Object definition id.</param>
        /// <param name="shape">Shape code, 0..22.</param>
        /// <param name="orientation">Rotation, 0..3.</param>
        /// <param name="localX">Tile X within the square.</param>
        /// <param name="localY">Tile Y within the square.</param>
        /// <param name="plane">Plane.</param>
        /// <param name="position">Absolute world position.</param>
        public Location(int id, int shape, int orientation, int localX, int localY, int plane, Position position) {
            Id = id;
            Shape = shape;
            Orientation = orientation;
            LocalX = localX;
            LocalY = localY;
            Plane = plane;
            Position = position;
        }

        /// <summary>
        ///     The packed position word this location encodes to.
        /// </summary>
        /// <remarks>The inverse of the decode at Class305_Sub1.java:432.</remarks>
        public int PackedPosition => (Plane << 12) | (LocalX << 6) | LocalY;

        /// <summary>The attribute byte this location encodes to.</summary>
        public int PackedAttributes => (Shape << 2) | Orientation;

        /// <summary>Gets the object id for this location.</summary>
        public int GetId() => Id;

        /// <summary>Gets the shape code stored in the map.</summary>
        public int GetLocationType() => Shape;

        /// <summary>Gets the orientation (rotation) of this location.</summary>
        public int GetOrientation() => Orientation;

        /// <summary>Gets the absolute position of this location.</summary>
        public Position GetPosition() => Position;
    }
}
