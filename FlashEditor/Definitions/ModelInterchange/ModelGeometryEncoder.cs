using System;
using System.Collections.Generic;

namespace FlashEditor.Definitions.ModelInterchange {
    /// <summary>
    ///     Writes a new mesh into a model, replacing the vertex and face blocks and keeping every
    ///     other array the file already held.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///     This is the half of the import that touches the stored form, and it is deliberately
    ///     narrow: four blocks are rebuilt - the vertex flag masks, the three delta streams, the
    ///     strip opcodes and the face-index deltas - along with the five footer fields that declare
    ///     their lengths. Every other array is carried over <b>by reference</b>, so "preserved"
    ///     means the same object, not a value that happened to match.
    ///     </para>
    ///     <para>
    ///     Every refusal lives here rather than in the OBJ layer, so the encoder is safe to call on
    ///     its own. They exist because the arrays being preserved are indexed positionally: a
    ///     per-face array is addressed by face number and a per-vertex array by vertex number, so
    ///     moving either count silently re-points the other's entries at different geometry.
    ///     </para>
    /// </remarks>
    public static class ModelGeometryEncoder {
        /// <summary>
        ///     The largest count the footer can declare, since all three layouts store the vertex
        ///     and face counts as unsigned shorts.
        /// </summary>
        public const int MaxCount = 0xFFFF;

        /// <summary>
        ///     The largest byte length a declared block field can carry, for the same reason.
        /// </summary>
        public const int MaxBlockLength = 0xFFFF;

        /// <summary>Lowest value a signed smart can carry.</summary>
        private const int MinSmart = -16384;

        /// <summary>Highest value a signed smart can carry.</summary>
        private const int MaxSmart = 16383;

        /// <summary>
        ///     Produces a copy of <paramref name="original"/> carrying <paramref name="geometry"/>
        ///     instead of its own vertices and faces.
        /// </summary>
        /// <remarks>
        ///     Always rebuilds, even when the mesh is identical. Callers that want an unedited round
        ///     trip to leave the bytes alone must check <see cref="ModelGeometry.Matches"/> first -
        ///     <see cref="ModelObjImporter"/> does - because a rebuild normalises the strip opcodes
        ///     and the smart widths, and the shipped encoder did not always take the shortest form.
        /// </remarks>
        /// <param name="original">The model whose non-geometry arrays are kept.</param>
        /// <param name="geometry">The mesh to write in.</param>
        /// <returns>A new model file. The original is not modified.</returns>
        /// <exception cref="ModelImportException">
        ///     The mesh cannot be mapped onto the arrays being preserved, or a delta or block length
        ///     does not fit the field that has to hold it.
        /// </exception>
        public static ModelFile Rebuild(ModelFile original, ModelGeometry geometry) {
            if (original == null)
                throw new ArgumentNullException(nameof(original));
            if (geometry == null)
                throw new ArgumentNullException(nameof(geometry));

            RefuseUnmappableCounts(original, geometry);
            RefuseOutOfRangeIndices(geometry);
            RefuseUnrepresentableOpcodeFlags(original);

            EncodeVertices(geometry, out byte[] vertexFlags,
                out StoredSmart[] deltasX, out StoredSmart[] deltasY, out StoredSmart[] deltasZ,
                out int lengthX, out int lengthY, out int lengthZ);
            EncodeFaces(geometry, out byte[] opcodes, out StoredSmart[] faceDeltas, out int faceLength);

            var rebuilt = CopyPreserving(original);
            rebuilt.VertexCount = geometry.VertexCount;
            rebuilt.VertexFlags = vertexFlags;
            rebuilt.VertexDeltasX = deltasX;
            rebuilt.VertexDeltasY = deltasY;
            rebuilt.VertexDeltasZ = deltasZ;
            rebuilt.VertexXLength = lengthX;
            rebuilt.VertexYLength = lengthY;
            rebuilt.VertexZLength = lengthZ;
            rebuilt.SlackVertexX = Array.Empty<byte>();
            rebuilt.SlackVertexY = Array.Empty<byte>();
            rebuilt.SlackVertexZ = Array.Empty<byte>();

            rebuilt.FaceOpcodes = opcodes;
            rebuilt.FaceIndexDeltas = faceDeltas;
            rebuilt.FaceIndexLength = faceLength;
            rebuilt.SlackFaceIndex = Array.Empty<byte>();

            return rebuilt;
        }

        // ===================================================================
        //  Refusals
        // ===================================================================

        /// <summary>
        ///     Refuses a mesh whose counts would leave a preserved array indexed by something that
        ///     moved.
        /// </summary>
        /// <remarks>
        ///     The face count is absolute: <see cref="ModelFile.FaceColours"/> is not optional, so
        ///     there is no model at all whose per-face arrays survive a face-count change, and
        ///     inventing a colour for a new face would be a guess written into the cache.
        ///     <para>
        ///     The vertex count is allowed to move, but only when nothing else addresses a vertex.
        ///     Three things do: the per-vertex skin groups, the reference vertices of every textured
        ///     triangle, and a particle effector's anchor (Renderable_Sub1.java:1461-1472). Emitters
        ///     and bonds ride on faces instead, so they are unaffected.
        ///     </para>
        /// </remarks>
        /// <param name="original">The model being written over.</param>
        /// <param name="geometry">The incoming mesh.</param>
        private static void RefuseUnmappableCounts(ModelFile original, ModelGeometry geometry) {
            if (geometry.FaceCount != original.FaceCount) {
                throw new ModelImportException(
                    $"The mesh has {geometry.FaceCount} faces and model {original.ModelId} has " +
                    $"{original.FaceCount}. Every per-face array is addressed by face number - " +
                    "colours, render types, priorities, alphas, skin groups, texture ids and " +
                    "texture-coordinate indices - and none of them can be extended or trimmed " +
                    "without inventing values. Change the geometry, not the face count.");
            }

            if (geometry.VertexCount > MaxCount) {
                throw new ModelImportException(
                    $"The mesh has {geometry.VertexCount} vertices and the footer stores the count " +
                    $"in two bytes, so {MaxCount} is the most a model can hold.");
            }

            if (geometry.VertexCount == original.VertexCount)
                return;

            var blockers = new List<string>();
            if (original.VertexSkins != null)
                blockers.Add($"{original.VertexSkins.Length} per-vertex skin groups");
            if (original.TexturedFaceCount > 0)
                blockers.Add($"{original.TexturedFaceCount} textured triangles, each naming three " +
                             "reference vertices by index");
            if (original.Effectors != null && original.Effectors.Length > 0)
                blockers.Add($"{original.Effectors.Length} particle effectors, each anchored to a vertex");

            if (blockers.Count == 0)
                return;

            throw new ModelImportException(
                $"The mesh has {geometry.VertexCount} vertices and model {original.ModelId} has " +
                $"{original.VertexCount}. That is allowed only when nothing else addresses a vertex " +
                "by index, and this model carries " + string.Join(", ", blockers) +
                ". Keep the vertex count, or edit those arrays in the editor first.");
        }

        /// <summary>Refuses a mesh whose faces name vertices it does not contain.</summary>
        /// <param name="geometry">The incoming mesh.</param>
        private static void RefuseOutOfRangeIndices(ModelGeometry geometry) {
            for (int i = 0; i < geometry.FaceCount; i++) {
                Check(geometry, i, geometry.FaceA[i]);
                Check(geometry, i, geometry.FaceB[i]);
                Check(geometry, i, geometry.FaceC[i]);
            }
        }

        private static void Check(ModelGeometry geometry, int face, int vertex) {
            if ((uint) vertex < (uint) geometry.VertexCount)
                return;

            throw new ModelImportException(
                $"Face {face} names vertex {vertex}, but the mesh has {geometry.VertexCount} " +
                "vertices. A face index outside the vertex array reads whatever bytes follow it " +
                "in the client.");
        }

        /// <summary>
        ///     Refuses a new-protocol model whose strip opcodes carry bits the rebuild would drop.
        /// </summary>
        /// <remarks>
        ///     Only three bits of the byte are the opcode there (Model.java:1071); the rest is a
        ///     per-face flag belonging to the trailing block that flags bit 7 declares. No model in
        ///     either cache sets any of it - <see cref="ModelCodec"/> refuses bit 7 outright for the
        ///     same reason - so rather than reproduce bits nothing can check, a model that has them
        ///     is refused.
        /// </remarks>
        /// <param name="original">The model being written over.</param>
        private static void RefuseUnrepresentableOpcodeFlags(ModelFile original) {
            if (original.Encoding != ModelEncoding.NewProtocol)
                return;

            for (int i = 0; i < original.FaceOpcodes.Length; i++) {
                if ((original.FaceOpcodes[i] & ~0x7) == 0)
                    continue;

                throw new ModelImportException(
                    $"Face {i} of new-protocol model {original.ModelId} carries opcode byte 0x" +
                    original.FaceOpcodes[i].ToString("X2") + ", whose bits above the low three are " +
                    "a per-face flag for the trailing block. A rebuild cannot reproduce them, so " +
                    "this model's geometry cannot be replaced.");
            }
        }

        // ===================================================================
        //  Block encoding
        // ===================================================================

        /// <summary>
        ///     Turns absolute coordinates back into the mask-and-delta form the file stores.
        /// </summary>
        /// <remarks>
        ///     The decoder only adds a delta when the vertex's mask bit is set, so an axis that did
        ///     not move between two vertices needs neither a bit nor a byte. Clearing the bit on a
        ///     zero delta is therefore the canonical choice and the smallest one; a stored zero
        ///     delta with the bit set decodes the same and is what the shipped encoder sometimes
        ///     wrote, which is why an unchanged mesh keeps its original bytes rather than coming
        ///     through here.
        /// </remarks>
        private static void EncodeVertices(ModelGeometry geometry, out byte[] vertexFlags,
            out StoredSmart[] deltasX, out StoredSmart[] deltasY, out StoredSmart[] deltasZ,
            out int lengthX, out int lengthY, out int lengthZ) {
            int count = geometry.VertexCount;
            vertexFlags = new byte[count];
            var x = new List<StoredSmart>(count);
            var y = new List<StoredSmart>(count);
            var z = new List<StoredSmart>(count);
            lengthX = lengthY = lengthZ = 0;

            int px = 0, py = 0, pz = 0;
            for (int i = 0; i < count; i++) {
                int mask = 0;

                int dx = geometry.X[i] - px;
                if (dx != 0) {
                    mask |= 1;
                    lengthX += Emit(x, dx, i, "X");
                    px = geometry.X[i];
                }

                int dy = geometry.Y[i] - py;
                if (dy != 0) {
                    mask |= 2;
                    lengthY += Emit(y, dy, i, "Y");
                    py = geometry.Y[i];
                }

                int dz = geometry.Z[i] - pz;
                if (dz != 0) {
                    mask |= 4;
                    lengthZ += Emit(z, dz, i, "Z");
                    pz = geometry.Z[i];
                }

                vertexFlags[i] = (byte) mask;
            }

            RefuseOversizedBlock(lengthX, "vertex X delta");
            RefuseOversizedBlock(lengthY, "vertex Y delta");
            RefuseOversizedBlock(lengthZ, "vertex Z delta");

            deltasX = x.ToArray();
            deltasY = y.ToArray();
            deltasZ = z.ToArray();
        }

        /// <summary>
        ///     Chooses each face's strip opcode against the face before it, and emits the deltas it
        ///     consumes.
        /// </summary>
        /// <remarks>
        ///     The three rolling opcodes are tried before the restart, so a run of adjacent
        ///     triangles costs one delta each rather than three. Which of them applies follows from
        ///     what the client's decoder leaves in <c>a</c> and <c>b</c>: opcode 2 keeps
        ///     <c>(a, c)</c>, opcode 3 keeps <c>(c, b)</c> and opcode 4 keeps <c>(b, a)</c>
        ///     (Model.java, the four <c>if</c> arms the projection replays).
        /// </remarks>
        private static void EncodeFaces(ModelGeometry geometry, out byte[] opcodes,
            out StoredSmart[] faceDeltas, out int length) {
            int count = geometry.FaceCount;
            opcodes = new byte[count];
            var deltas = new List<StoredSmart>(count);
            length = 0;

            int a = 0, b = 0, c = 0, offset = 0;
            for (int i = 0; i < count; i++) {
                int wantA = geometry.FaceA[i];
                int wantB = geometry.FaceB[i];
                int wantC = geometry.FaceC[i];

                if (wantA == a && wantB == c)
                    opcodes[i] = 2;
                else if (wantA == c && wantB == b)
                    opcodes[i] = 3;
                else if (wantA == b && wantB == a)
                    opcodes[i] = 4;
                else
                    opcodes[i] = 1;

                if (opcodes[i] == 1) {
                    length += Emit(deltas, wantA - offset, i, "face index");
                    length += Emit(deltas, wantB - wantA, i, "face index");
                    length += Emit(deltas, wantC - wantB, i, "face index");
                }
                else {
                    length += Emit(deltas, wantC - offset, i, "face index");
                }

                a = wantA;
                b = wantB;
                c = wantC;
                offset = wantC;
            }

            RefuseOversizedBlock(length, "face index");
            faceDeltas = deltas.ToArray();
        }

        /// <summary>Appends one signed smart in the narrowest width that holds it.</summary>
        /// <param name="into">The stream being built.</param>
        /// <param name="value">The delta.</param>
        /// <param name="index">The vertex or face the delta belongs to, for the failure message.</param>
        /// <param name="what">Which stream it is, for the failure message.</param>
        /// <returns>How many bytes it occupies.</returns>
        private static int Emit(List<StoredSmart> into, int value, int index, string what) {
            if (value < MinSmart || value > MaxSmart) {
                throw new ModelImportException(
                    $"The {what} delta at {index} is {value}, and a signed smart carries " +
                    $"{MinSmart} to {MaxSmart}. Two vertices that far apart cannot be stored one " +
                    "after the other; reorder or rescale the mesh.");
            }

            bool oneByte = value >= -64 && value <= 63;
            into.Add(new StoredSmart(value,
                oneByte ? JagStream.SmartWidth.OneByte : JagStream.SmartWidth.TwoByte));
            return oneByte ? 1 : 2;
        }

        private static void RefuseOversizedBlock(int length, string what) {
            if (length <= MaxBlockLength)
                return;

            throw new ModelImportException(
                $"The {what} block came out at {length} bytes and the footer declares its length " +
                $"in two bytes, so {MaxBlockLength} is the most it can state.");
        }

        // ===================================================================
        //  Preservation
        // ===================================================================

        /// <summary>
        ///     Copies every field a mesh cannot express, sharing the arrays rather than cloning them.
        /// </summary>
        /// <remarks>
        ///     Sharing is deliberate. It makes "preserved" checkable by reference equality, which is
        ///     what <c>ModelObjInterchangeTests</c> asserts field by field over every public property, so
        ///     a field added to <see cref="ModelFile"/> and forgotten here fails a test rather than
        ///     silently defaulting to null in every imported model. The geometry fields are the only
        ///     ones the caller overwrites afterwards.
        /// </remarks>
        /// <param name="original">The model being written over.</param>
        /// <returns>A copy carrying everything except the four rebuilt blocks.</returns>
        private static ModelFile CopyPreserving(ModelFile original) {
            return new ModelFile {
                Encoding = original.Encoding,
                ModelId = original.ModelId,
                VertexCount = original.VertexCount,
                FaceCount = original.FaceCount,
                TexturedFaceCount = original.TexturedFaceCount,
                FormatType = original.FormatType,
                Header = original.Header,
                Flags = original.Flags,
                LegacyFaceMaskFlag = original.LegacyFaceMaskFlag,
                PriorityFlag = original.PriorityFlag,
                AlphaFlag = original.AlphaFlag,
                FaceSkinFlag = original.FaceSkinFlag,
                FaceTextureFlag = original.FaceTextureFlag,
                VertexSkinFlag = original.VertexSkinFlag,
                VertexXLength = original.VertexXLength,
                VertexYLength = original.VertexYLength,
                VertexZLength = original.VertexZLength,
                FaceIndexLength = original.FaceIndexLength,
                TextureCoordLength = original.TextureCoordLength,
                StoredVertexSkinLength = original.StoredVertexSkinLength,
                StoredFaceSkinLength = original.StoredFaceSkinLength,
                Sentinel = original.Sentinel,
                TextureTypes = original.TextureTypes,
                VertexFlags = original.VertexFlags,
                FaceTypeBytes = original.FaceTypeBytes,
                FaceOpcodes = original.FaceOpcodes,
                FacePriorities = original.FacePriorities,
                FaceAlphas = original.FaceAlphas,
                FaceSkins = original.FaceSkins,
                VertexSkins = original.VertexSkins,
                FaceTextureIds = original.FaceTextureIds,
                FaceColours = original.FaceColours,
                VertexDeltasX = original.VertexDeltasX,
                VertexDeltasY = original.VertexDeltasY,
                VertexDeltasZ = original.VertexDeltasZ,
                FaceIndexDeltas = original.FaceIndexDeltas,
                TextureCoords = original.TextureCoords,
                SlackVertexX = original.SlackVertexX,
                SlackVertexY = original.SlackVertexY,
                SlackVertexZ = original.SlackVertexZ,
                SlackFaceIndex = original.SlackFaceIndex,
                SlackTextureCoord = original.SlackTextureCoord,
                SlackTextureScale = original.SlackTextureScale,
                SlackVertexSkin = original.SlackVertexSkin,
                SlackFaceSkin = original.SlackFaceSkin,
                Gap = original.Gap,
                TextureVertexA = original.TextureVertexA,
                TextureVertexB = original.TextureVertexB,
                TextureVertexC = original.TextureVertexC,
                TextureScaleP = original.TextureScaleP,
                TextureScaleQ = original.TextureScaleQ,
                TextureScaleR = original.TextureScaleR,
                TextureFieldA = original.TextureFieldA,
                TextureFieldB = original.TextureFieldB,
                TextureFieldC = original.TextureFieldC,
                TextureType2FieldA = original.TextureType2FieldA,
                TextureType2FieldB = original.TextureType2FieldB,
                Emitters = original.Emitters,
                Effectors = original.Effectors,
                Bonds = original.Bonds
            };
        }
    }
}
