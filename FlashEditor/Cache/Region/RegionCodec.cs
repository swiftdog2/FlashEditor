using System;
using System.Collections.Generic;

namespace FlashEditor.Cache.Region {
    /// <summary>
    ///     Encodes a decoded map square back into its terrain and location file formats.
    /// </summary>
    /// <remarks>
    ///     An unedited square is written back as the exact bytes it was decoded from. Only a dirty
    ///     square is re-encoded, and the canonical encoding is chosen to reproduce the shipped files
    ///     byte-for-byte wherever the original encoder was itself canonical.
    ///
    ///     See <c>reference/hydra-637-maps/02-terrain-m.md</c> and <c>03-locs-l.md</c>.
    /// </remarks>
    public static class RegionCodec {
        /// <summary>
        ///     Encodes a square's terrain file.
        /// </summary>
        /// <param name="region">The square to encode.</param>
        /// <param name="force">
        ///     Re-encode even when the square is unedited. For testing the encoder against the
        ///     original bytes; production callers should leave this false.
        /// </param>
        /// <returns>The encoded terrain file.</returns>
        public static byte[] EncodeTerrain(Region region, bool force = false) {
            if (region == null) throw new ArgumentNullException(nameof(region));

            if (!force && !region.Dirty && region.RawTerrain.Length > 0)
                return (byte[]) region.RawTerrain.Clone();

            var stream = new JagStream();

            for (int z = 0; z < region.PlaneCount; z++)
                for (int x = 0; x < Region.WIDTH; x++)
                    for (int y = 0; y < Region.HEIGHT; y++)
                        EncodeTile(stream, region, z, x, y);

            //The environment, lighting and shadow section cannot be re-derived, so it goes back
            //exactly as it came in.
            if (region.ExtrasTail.Length > 0)
                stream.Write(region.ExtrasTail, 0, region.ExtrasTail.Length);

            return stream.Flip().ToArray();
        }

        /// <summary>
        ///     Writes one tile's opcode run.
        /// </summary>
        /// <remarks>
        ///     The non-terminating opcodes may appear in any order - the decoder is a loop, not a
        ///     grammar - but this is the order the shipped files use, so an untouched tile
        ///     re-encodes to the same bytes: overlay, then flags, then underlay, then the
        ///     terminator that carries the height. Determined by diffing a re-encode against the
        ///     original bytes of m50_50, where a tile reads 7 171 53 129 1 30.
        /// </remarks>
        private static void EncodeTile(JagStream stream, Region region, int z, int x, int y) {
            int overlay = region.GetOverlayId(z, x, y);
            if (overlay != 0) {
                int shape = region.GetOverlayShape(z, x, y);
                int rotation = region.GetOverlayRotation(z, x, y);
                stream.WriteByte((byte) (2 + shape * 4 + rotation));
                stream.WriteByte((byte) overlay);
            }

            byte flags = region.GetRenderRule(z, x, y);
            if (flags != 0)
                stream.WriteByte((byte) (flags + 49));

            int underlay = region.GetUnderlayId(z, x, y);
            if (underlay != 0)
                stream.WriteByte((byte) (underlay + 81));

            EncodeHeight(stream, region, z, x, y);
        }

        /// <summary>
        ///     Writes the terminating opcode, which is where a tile's height lives.
        /// </summary>
        /// <remarks>
        ///     Opcode 0 means "no stored height", and the decoder then derives one - procedurally on
        ///     plane 0, or from the plane below elsewhere. Opcode 1 carries an explicit byte.
        ///
        ///     Which form a tile used is read back off the square rather than inferred by comparing
        ///     the height to the derived value. Tiles exist whose stored height happens to equal
        ///     what the derivation produces, and collapsing those to opcode 0 rewrites bytes nobody
        ///     asked to change.
        /// </remarks>
        private static void EncodeHeight(JagStream stream, Region region, int z, int x, int y) {
            if (!region.HasExplicitHeight(z, x, y)) {
                stream.WriteByte(0);
                return;
            }

            //An untouched tile writes the byte it was decoded from. Bytes 0 and 1 both decode to
            //height 0, and the shipped files use both, so the choice cannot be reconstructed.
            if (!region.HasEditedHeight(z, x, y)) {
                stream.WriteByte(1);
                stream.WriteByte(region.GetRawHeightByte(z, x, y));
                return;
            }

            int height = region.GetTileHeight(z, x, y);

            int step = z == 0
                ? -height / Region.HEIGHT_UNITS_PER_STEP
                : (region.GetTileHeight(z - 1, x, y) - height) / Region.HEIGHT_UNITS_PER_STEP;

            //The decoder maps a stored 1 to 0, so 1 cannot be written as itself. A height of one
            //step therefore has no opcode-1 encoding and has to fall back to the derived form.
            if (step < 0 || step > 255 || step == 1)
                throw new InvalidOperationException(
                    $"Tile {x},{y} plane {z} has height {height}, which is not encodable " +
                    $"(step {step}); heights must be a multiple of {Region.HEIGHT_UNITS_PER_STEP} " +
                    "and within 0..255 steps of the reference, and one step is a reserved value");

            stream.WriteByte(1);
            stream.WriteByte((byte) step);
        }

        /// <summary>
        ///     Encodes a square's location file.
        /// </summary>
        /// <param name="region">The square to encode.</param>
        /// <param name="force">Re-encode even when the square is unedited.</param>
        /// <returns>The encoded location file.</returns>
        public static byte[] EncodeLocations(Region region, bool force = false) {
            if (region == null) throw new ArgumentNullException(nameof(region));

            if (!force && !region.Dirty && region.RawLocations.Length > 0)
                return (byte[]) region.RawLocations.Clone();

            var stream = new JagStream();

            //The stream is delta-encoded on both axes, so it has to be sorted the way it was
            //decoded: ascending object id, and within an object, ascending packed position.
            var byId = new SortedDictionary<int, List<Location>>();
            foreach (Location loc in region.GetLocations()) {
                if (!byId.TryGetValue(loc.Id, out List<Location> group))
                    byId[loc.Id] = group = new List<Location>();
                group.Add(loc);
            }

            int lastId = -1;
            foreach (KeyValuePair<int, List<Location>> entry in byId) {
                stream.WriteExtendedUnsignedSmart(entry.Key - lastId);
                lastId = entry.Key;

                //OrderBy is stable, unlike List.Sort. l50_50 places object 85 at position
                //3969 twice with different attributes, and an unstable sort swaps them.
                List<Location> ordered = new List<Location>(
                    System.Linq.Enumerable.OrderBy(entry.Value, loc => loc.PackedPosition));

                int lastPosition = 0;
                foreach (Location loc in ordered) {
                    stream.WriteUnsignedSmart(loc.PackedPosition - lastPosition + 1);
                    lastPosition = loc.PackedPosition;
                    stream.WriteByte((byte) loc.PackedAttributes);
                }

                stream.WriteByte(0);
            }

            stream.WriteByte(0);
            return stream.Flip().ToArray();
        }

        /// <summary>
        ///     Decodes a square's NPC spawn table, the <c>n</c> family.
        /// </summary>
        /// <remarks>
        ///     Fixed four-byte records with no count and no terminator, read until the buffer runs
        ///     out - the client's loop condition is <c>caret &lt; length</c>
        ///     (<c>Particle_Sub3_Sub2.java:233-246</c>). An empty file is legal and occurs in both
        ///     supported caches, so an empty list is a real decode rather than a failure.
        ///
        ///     A length that is not a whole number of records means the stream is not what it was
        ///     taken for, and is rejected rather than truncated. The client would read past the end
        ///     of its own buffer there; every shipped table is a clean multiple of four.
        /// </remarks>
        /// <param name="buf">The decompressed spawn file.</param>
        /// <returns>The decoded spawns, in file order.</returns>
        /// <exception cref="System.IO.InvalidDataException">The file is not a whole number of records.</exception>
        public static List<NpcSpawn> DecodeNpcSpawns(JagStream buf) {
            if (buf == null) throw new ArgumentNullException(nameof(buf));

            var spawns = new List<NpcSpawn>();

            while (buf.Remaining() > 0) {
                if (buf.Remaining() < NpcSpawnRecordBytes)
                    throw new System.IO.InvalidDataException(
                        "NPC spawn file has " + buf.Remaining() + " bytes left over, which is less " +
                        "than one " + NpcSpawnRecordBytes + " byte record");

                int packed = buf.ReadUnsignedShort();
                int npcId = buf.ReadUnsignedShort();

                spawns.Add(new NpcSpawn(npcId, packed >> 14, (packed >> 7) & 0x3F, packed & 0x3F));
            }

            return spawns;
        }

        /// <summary>
        ///     Encodes a square's NPC spawn table.
        /// </summary>
        /// <remarks>
        ///     No header and no terminator: an empty table encodes to zero bytes, which is what the
        ///     shipped empty tables hold.
        /// </remarks>
        /// <param name="spawns">The spawns, in the order they should be written.</param>
        /// <returns>The encoded spawn file.</returns>
        public static byte[] EncodeNpcSpawns(IReadOnlyList<NpcSpawn> spawns) {
            if (spawns == null) throw new ArgumentNullException(nameof(spawns));

            var stream = new JagStream();

            foreach (NpcSpawn spawn in spawns) {
                stream.WriteShort(spawn.PackedPosition);
                stream.WriteShort(spawn.NpcId);
            }

            return stream.Flip().ToArray();
        }

        /// <summary>Bytes one NPC spawn record occupies: two unsigned shorts.</summary>
        public const int NpcSpawnRecordBytes = 4;
    }
}
