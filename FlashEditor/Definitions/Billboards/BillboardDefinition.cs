using System;
using System.Collections.Generic;
using FlashEditor.IO;

namespace FlashEditor.Definitions.Billboards {
    /// <summary>
    ///     One billboard from JS5 index 29: a screen-space quad that replaces a model face.
    /// </summary>
    /// <remarks>
    ///     The whole index is one group, so a billboard id is a file id within group 0 -
    ///     <c>getChildFromFolder(0, id)</c> at Class177.java:21. Opcode table from
    ///     <c>Class177.method2583</c> (Class177.java:110-150), driven by the loop in
    ///     <c>method2586</c> (:153-171).
    ///     <para>
    ///     What a record means was settled from what the client does with it.
    ///     Renderable_Sub1.java:2044-2070 places the quad at the centroid of the referenced face's
    ///     three vertices and sizes it as <c>scale * width * fov / (z * 128)</c>, so opcode 2 is a
    ///     screen-space size in 1/128 units rather than a texture dimension. The per-model
    ///     attachment array decides which billboard lands on which face; this index says nothing
    ///     about that.
    ///     </para>
    /// </remarks>
    public sealed class BillboardDefinition : OpcodeStreamDefinition {
        /// <summary>The one group the whole index holds.</summary>
        public const int GroupId = 0;

        /// <summary>The stored value on opcode 1 that means "no material".</summary>
        /// <remarks>
        ///     Class177.java:137-140 maps it to -1, which is also what an absent opcode 1 leaves
        ///     behind - so the two are aliases and the raw value has to be kept rather than
        ///     recomputed. Neither supported cache stores it, so nothing here defends that branch.
        /// </remarks>
        public const int NoMaterial = 0xFFFF;

        /// <summary>Width and height the client assumes when opcode 2 is absent.</summary>
        public const int DefaultExtent = 64;

        /// <summary>Raster mode the client assumes when opcode 4 is absent.</summary>
        public const int DefaultRasterMode = 2;

        /// <summary>Colour-combine mode the client assumes when opcode 5 is absent.</summary>
        public const int DefaultCombineMode = 1;

        /// <summary>The billboard id, which is its file id within group 0.</summary>
        public int Id { get; set; } = -1;

        /// <summary>Opcode 1. Material id in index 26, or -1 for none.</summary>
        public int MaterialId { get; set; } = -1;

        /// <summary>Opcode 2, first field. Quad width in 1/128 screen units.</summary>
        /// <remarks>Stored as width minus one, so the default 64 is a stored 63.</remarks>
        public int Width { get; set; } = DefaultExtent;

        /// <summary>Opcode 2, second field. Quad height in 1/128 screen units.</summary>
        public int Height { get; set; } = DefaultExtent;

        /// <summary>
        ///     Opcode 3. A signed byte the 637 client reads and discards.
        /// </summary>
        /// <remarks>
        ///     Class177.java:126 is a bare <c>readSignedByte()</c> with no assignment, so its meaning
        ///     is unknown and deliberately not guessed at in the name. It is a real field of the
        ///     format - a majority of the records in the repack carry it, with values spread from 0
        ///     to 100 - so it has to be decoded and written back verbatim or those records stop
        ///     round-tripping.
        /// </remarks>
        public sbyte UnusedByte3 { get; set; }

        /// <summary>
        ///     Opcode 4. Which raster loop draws the quad.
        /// </summary>
        /// <remarks>
        ///     Class332_Sub3_Sub2.method3757 branches on it: 0 opaque copy, 1 colour-key skipping
        ///     texel 0, 2 saturating additive.
        /// </remarks>
        public int RasterMode { get; set; } = DefaultRasterMode;

        /// <summary>
        ///     Opcode 5. How the texel is combined with the face colour.
        /// </summary>
        /// <remarks>
        ///     Same rasteriser: 0 modulate by face colour, 1 copy unmodified, 2 alpha-scale,
        ///     3 additive tint.
        /// </remarks>
        public int CombineMode { get; set; } = DefaultCombineMode;

        /// <summary>
        ///     Opcode 6. Suppresses the billboard while the shader renderer is active.
        /// </summary>
        /// <remarks>
        ///     A view over the recorded opcode stream rather than a stored bool, so clearing it
        ///     drops the opcode instead of leaving one behind for the replay to put back - which
        ///     would change the row in the editor, report the save as successful, and leave the flag
        ///     set in the cache. Reaches only the hardware path, Renderable_Sub2.java:4036.
        ///     <para>Neither supported cache sets it, so no sweep defends it.</para>
        /// </remarks>
        public bool HiddenOnShaderRenderer {
            get => Opcodes.Has(6);
            set => SetFlag(6, value);
        }

        /// <summary>
        ///     Opcode 7. Suppresses the face the billboard replaces.
        /// </summary>
        /// <remarks>
        ///     Renderable_Sub1.java:3259 rasterises the source triangle only when this is false, and
        ///     Renderable_Sub2.java:449-453 drops the face from the draw list outright. Same
        ///     stream-backed shape as <see cref="HiddenOnShaderRenderer"/>.
        /// </remarks>
        public bool HidesSourceFace {
            get => Opcodes.Has(7);
            set => SetFlag(7, value);
        }

        /// <summary>Reads one billboard record from its file.</summary>
        /// <param name="stream">The file, positioned at its first opcode.</param>
        /// <returns>This definition.</returns>
        public BillboardDefinition Decode(JagStream stream) {
            Opcodes.Clear();
            DecodeOpcodeStream(stream);
            return this;
        }

        /// <inheritdoc/>
        protected override bool DecodeOpcode(JagStream stream, int opcode) {
            switch (opcode) {
                case 1: {
                        int material = stream.ReadUnsignedShort();
                        MaterialId = material == NoMaterial ? -1 : material;
                        return true;
                    }

                case 2:
                    Width = stream.ReadUnsignedShort() + 1;
                    Height = stream.ReadUnsignedShort() + 1;
                    return true;

                case 3:
                    UnusedByte3 = stream.ReadSignedByte();
                    return true;

                case 4:
                    RasterMode = stream.ReadUnsignedByte();
                    return true;

                case 5:
                    CombineMode = stream.ReadUnsignedByte();
                    return true;

                //6 and 7 are bare flags: their presence is their whole payload.
                case 6:
                case 7:
                    return true;

                default:
                    return false;
            }
        }

        /// <summary>Writes this billboard back to the file representation.</summary>
        /// <remarks>
        ///     Order capture is mandatory here rather than defensive. Eight distinct orderings occur
        ///     and not one of them is ascending - opcode 1 is written last in every record - so an
        ///     encoder emitting ascending opcodes reproduces none of them. The order is not even
        ///     derivable from a rule: both 4-then-5 and 5-then-4 occur, as do 5-then-7 and 7-then-5.
        /// </remarks>
        /// <returns>The encoded file, positioned at 0.</returns>
        public JagStream Encode() {
            var records = new List<KeyValuePair<int, byte[]>>();

            /* Each block emits when the record carried the opcode OR when the field has moved off
               the client's default. The first arm is what keeps an opcode stored at its own default
               - two records store combine mode 1 and most that carry opcode 3 store 0 - instead of
               dropping it and shortening a file nobody edited. */
            if (Opcodes.Has(1) || MaterialId != -1)
                records.Add(Payload(1, buffer => buffer.WriteShort(MaterialId == -1 ? NoMaterial : MaterialId)));

            if (Opcodes.Has(2) || Width != DefaultExtent || Height != DefaultExtent) {
                records.Add(Payload(2, buffer => {
                    buffer.WriteShort(Width - 1);
                    buffer.WriteShort(Height - 1);
                }));
            }

            if (Opcodes.Has(3) || UnusedByte3 != 0)
                records.Add(Payload(3, buffer => buffer.WriteSignedByte(UnusedByte3)));
            if (Opcodes.Has(4) || RasterMode != DefaultRasterMode)
                records.Add(Payload(4, buffer => buffer.WriteByte((byte) RasterMode)));
            if (Opcodes.Has(5) || CombineMode != DefaultCombineMode)
                records.Add(Payload(5, buffer => buffer.WriteByte((byte) CombineMode)));

            /* 6 and 7 are not listed. They carry no payload, so there is nothing to re-encode, and
               the recorded stream is the only statement of whether they are set - which is exactly
               what their properties read and write. */
            return Opcodes.Replay(records, appendInAscendingOrder: true);
        }

        /// <summary>Adds or drops a bare flag opcode.</summary>
        /// <remarks>
        ///     An added flag lands at the end of the stream, which is safe on this index: every
        ///     opcode is independent, unlike index 28's count-then-offsets pair.
        /// </remarks>
        /// <param name="opcode">The flag opcode.</param>
        /// <param name="set">Whether the flag should be present.</param>
        private void SetFlag(int opcode, bool set) {
            if (set == Opcodes.Has(opcode))
                return;

            if (set)
                Opcodes.Add(opcode, Array.Empty<byte>());
            else
                Opcodes.Remove(opcode);
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
