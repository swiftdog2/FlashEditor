using System;
using System.Collections.Generic;
using System.IO;
using FlashEditor.IO;

namespace FlashEditor.Definitions.Animation {
    /// <summary>
    ///     One signed smart exactly as it sat on the wire: the value, and which of the two widths
    ///     carried it.
    /// </summary>
    /// <remarks>
    ///     The width is carried because the encoding is not canonical - -64 to 63 is expressible in
    ///     both widths, so the decoded number alone does not determine the bytes. Replaying the width
    ///     that was read makes the encoder byte-identical by construction rather than by measurement,
    ///     which matters here more than anywhere else in the cache: index 0 holds 20,142,030 signed
    ///     smarts and a single re-widened one moves every byte after it.
    /// </remarks>
    public readonly struct FrameValue {
        /// <summary>A value the frame does not store, which the client fills from the transform type.</summary>
        /// <remarks>
        ///     Never written. A missing axis costs no bytes, because the flag bit that would have
        ///     announced it is clear.
        /// </remarks>
        public static readonly FrameValue Absent = default;

        /// <summary>The decoded number, -16384 to 16383.</summary>
        public int Value { get; }

        /// <summary>The width the cache stored it in, or <c>Shortest</c> for a value never read.</summary>
        public JagStream.SmartWidth Width { get; }

        /// <summary>Binds a value to the width it was stored in.</summary>
        /// <param name="value">The decoded number.</param>
        /// <param name="width">The width to write it back in.</param>
        public FrameValue(int value, JagStream.SmartWidth width = JagStream.SmartWidth.Shortest) {
            Value = value;
            Width = width;
        }

        /// <summary>Describes the value and its width, for a failure line.</summary>
        /// <returns>The value with its stored width.</returns>
        public override string ToString() => Value + " (" + Width + ")";
    }

    /// <summary>
    ///     One slot of a frame: which axes it stores, the two-bit field beside them, and the values.
    /// </summary>
    /// <remarks>
    ///     A slot's position <b>is</b> its identity - it indexes the bone table of the skeleton the
    ///     frame names, and nothing in the file states which bone it means
    ///     (<c>Class7.java:60-61</c> uses the loop counter to read <c>anIntArray3812</c>). Inserting or
    ///     removing a slot re-points every slot after it.
    ///     <para>
    ///     A slot whose flag byte is zero is skipped entirely by the client (<c>Class7.java:66</c>) and
    ///     still occupies a flag byte, so it is kept here rather than dropped - dropping it would
    ///     shorten the flag block and renumber every following slot.
    ///     </para>
    /// </remarks>
    public sealed class FrameTransform {
        /// <summary>Flag bit announcing a stored x value (<c>Class7.java:75</c>).</summary>
        public const int XPresent = 0x1;

        /// <summary>Flag bit announcing a stored y value (<c>Class7.java:80</c>).</summary>
        public const int YPresent = 0x2;

        /// <summary>Flag bit announcing a stored z value (<c>Class7.java:85</c>).</summary>
        public const int ZPresent = 0x4;

        /// <summary>Bit position of the two-bit field the client keeps beside the axis bits.</summary>
        /// <remarks><c>Class7.java:90</c>: <c>aByteArray97[n] = (byte) (i_6_ &gt;&gt;&gt; 3 &amp; 0x3)</c>.</remarks>
        public const int SubTypeShift = 3;

        /// <summary>Width of that field, once shifted down.</summary>
        public const int SubTypeMask = 0x3;

        /// <summary>The largest value the flag byte can carry.</summary>
        public const int MaxFlag = 255;

        /// <summary>
        ///     The flag byte, exactly as the file stores it.
        /// </summary>
        /// <remarks>
        ///     Raw rather than rebuilt from the axis bits, because the client reads two separate
        ///     fields out of it and only masks one of them: bits 0-2 are the axis bits and bits 3-4
        ///     are a further two-bit value kept per slot. Bits 5-7 are read by nothing at all, so
        ///     recomputing the byte from what we understand would drop whatever they hold.
        /// </remarks>
        public int Flag { get; set; }

        /// <summary>Whether the frame stores an x value for this slot.</summary>
        public bool HasX => (Flag & XPresent) != 0;

        /// <summary>Whether the frame stores a y value for this slot.</summary>
        public bool HasY => (Flag & YPresent) != 0;

        /// <summary>Whether the frame stores a z value for this slot.</summary>
        public bool HasZ => (Flag & ZPresent) != 0;

        /// <summary>
        ///     The two-bit field at bits 3-4 of the flag byte.
        /// </summary>
        /// <remarks>
        ///     Named no further than its position, because the client only ever stores it
        ///     (<c>Class7.java:90</c> into <c>aByteArray97</c>, copied to <c>aByteArray99</c> at :128)
        ///     and this decompile shows no read of it. It changes no byte count: the value stream is
        ///     sized by bits 0-2 alone.
        /// </remarks>
        public int SubType => (Flag >> SubTypeShift) & SubTypeMask;

        /// <summary>
        ///     Whether the client would pass over this slot without recording a pose for it.
        /// </summary>
        /// <remarks>
        ///     <c>Class7.java:66</c> gates the whole body on <c>i_6_ &gt; 0</c>, so a zero flag byte
        ///     contributes nothing but its own byte. Note this is not the same as "stores no values":
        ///     a flag of 8 sets only the two-bit field, is <b>not</b> skipped, and still reads no
        ///     values.
        /// </remarks>
        public bool IsSkipped => Flag == 0;

        /// <summary>How many signed smarts this slot takes out of the value stream.</summary>
        /// <remarks>
        ///     The whole of the frame's length arithmetic, and it depends on nothing but the flag
        ///     byte - which is why a frame re-encodes byte for byte without its skeleton in hand.
        /// </remarks>
        public int StoredValueCount => (HasX ? 1 : 0) + (HasY ? 1 : 0) + (HasZ ? 1 : 0);

        /// <summary>The stored x value, meaningful only when <see cref="HasX"/>.</summary>
        public FrameValue X { get; set; }

        /// <summary>The stored y value, meaningful only when <see cref="HasY"/>.</summary>
        public FrameValue Y { get; set; }

        /// <summary>The stored z value, meaningful only when <see cref="HasZ"/>.</summary>
        public FrameValue Z { get; set; }
    }

    /// <summary>
    ///     One slot of a frame after the skeleton has been applied: the numbers the client would
    ///     actually transform with.
    /// </summary>
    /// <remarks>
    ///     Distinct from <see cref="FrameTransform"/> on purpose. The stored value and the acted-on
    ///     value differ for four of the eleven transform types - 3 and 10 default a missing axis to
    ///     128 instead of 0, and 2 and 9 rescale into a 14-bit angle - so a single field holding both
    ///     would either lose the stored byte or hand the caller the wrong number.
    /// </remarks>
    public readonly struct FramePose {
        /// <summary>The slot, which is the index of the bone in the skeleton's table.</summary>
        public int Slot { get; }

        /// <summary>The x value, defaulted and rescaled by the transform type.</summary>
        public int X { get; }

        /// <summary>The y value, defaulted and rescaled by the transform type.</summary>
        public int Y { get; }

        /// <summary>The z value, defaulted and rescaled by the transform type.</summary>
        public int Z { get; }

        /// <summary>The two-bit field carried from bits 3-4 of the slot's flag byte.</summary>
        public int SubType { get; }

        /// <summary>
        ///     The slot holding the pivot this one turns about, or <c>-1</c> for none.
        /// </summary>
        /// <remarks>
        ///     Only types 1, 2 and 3 ever take one, and only from the most recent type-0 slot that no
        ///     earlier pose has already claimed (<c>Class7.java:97-101</c>). That "already claimed"
        ///     rule is why a pivot cannot be resolved by scanning backwards after the fact: it is
        ///     consumed by the first pose that uses it.
        /// </remarks>
        public int PivotSlot { get; }

        /// <summary>Binds one resolved pose.</summary>
        /// <param name="slot">The slot, and so the bone.</param>
        /// <param name="x">The resolved x value.</param>
        /// <param name="y">The resolved y value.</param>
        /// <param name="z">The resolved z value.</param>
        /// <param name="subType">The two-bit field from the flag byte.</param>
        /// <param name="pivotSlot">The pivot slot, or -1.</param>
        public FramePose(int slot, int x, int y, int z, int subType, int pivotSlot) {
            Slot = slot;
            X = x;
            Y = y;
            Z = z;
            SubType = subType;
            PivotSlot = pivotSlot;
        }
    }

    /// <summary>
    ///     A frame read against its skeleton: the poses the client would build, and the model flags
    ///     the transform types present imply.
    /// </summary>
    /// <remarks>
    ///     This is <c>Class7</c>'s constructor output - <c>aShortArray108/94/105/106/107</c> and
    ///     <c>aByteArray99</c> as the six columns of <see cref="Poses"/>, and the three booleans
    ///     <c>aBoolean102/104/95</c> folded into <see cref="ModelBuildFlags"/>.
    /// </remarks>
    public sealed class ResolvedFrame {
        /// <summary>Model-build bit a type 7 slot sets.</summary>
        /// <remarks>
        ///     <c>Class7.java:104-105</c> sets <c>aBoolean104</c>, which
        ///     <c>Node_Sub46_Sub16.method1619</c> returns and <c>Class97.java:143-145</c> turns into
        ///     this bit. Named after the bit rather than after a guess at what it switches on.
        /// </remarks>
        public const int ModelFlagFromType7 = 0x80;

        /// <summary>Model-build bit a type 5 slot sets.</summary>
        /// <remarks><c>Class7.java:102-103</c> to <c>method1617</c> to <c>Class97.java:146-148</c>.</remarks>
        public const int ModelFlagFromType5 = 0x100;

        /// <summary>Model-build bit a type 8, 9 or 10 slot sets.</summary>
        /// <remarks><c>Class7.java:106-107</c> to <c>method1615</c> to <c>Class97.java:149-151</c>.</remarks>
        public const int ModelFlagFromWideTypes = 0x400;

        /// <summary>The poses, in slot order, with the skipped slots left out.</summary>
        public IReadOnlyList<FramePose> Poses { get; }

        /// <summary>
        ///     The bits this frame contributes to the model-build flags.
        /// </summary>
        /// <remarks>
        ///     Zero, or an OR of the three <c>ModelFlagFrom*</c> constants. The client ORs them into
        ///     the flags it hands the renderable, so a frame set that carries none of those types
        ///     builds a cheaper model.
        /// </remarks>
        public int ModelBuildFlags { get; }

        /// <summary>Binds one resolved frame.</summary>
        /// <param name="poses">The poses, in slot order.</param>
        /// <param name="modelBuildFlags">The model-build bits the transform types imply.</param>
        public ResolvedFrame(IReadOnlyList<FramePose> poses, int modelBuildFlags) {
            Poses = poses ?? throw new ArgumentNullException(nameof(poses));
            ModelBuildFlags = modelBuildFlags;
        }
    }

    /// <summary>
    ///     One animation keyframe: a per-bone delta pose played against a skeleton.
    /// </summary>
    /// <remarks>
    ///     JS5 index 0 (<c>RSConstants.FRAMES_INDEX</c>). A group is one animation's complete frame
    ///     set and a file is one frame, its file id being the frame's ordinal - so 3526 groups hold
    ///     359,931 frames in the shipped 639 cache. The reference table sets no flags, so a group has
    ///     no name; the only route in is index 20, which stores the packed <c>(group &lt;&lt; 16) |
    ///     file</c> id that <c>Class97.java:130-131</c> splits.
    ///     <para>
    ///     Layout, <c>Class7.java:53-111</c>:
    ///     <c>u8</c> read and discarded (:53), <c>u16</c> skeleton group id (:54, read for real at
    ///     <c>Node_Sub46_Sub16.java:126-128</c>), <c>u8</c> slot count (:55), one flag byte per slot
    ///     (:65), then the value stream - one signed smart per set axis bit, in slot order then x, y,
    ///     z (:75-89). The client reads the flags and the values through two cursors over the same
    ///     array, which is why the two blocks are contiguous and why <c>:112-114</c> can throw unless
    ///     the value cursor lands exactly on the end of the file.
    ///     </para>
    ///     <para>
    ///     <b>The skeleton changes what a value means and never how many bytes it takes.</b> The byte
    ///     count follows from the axis bits alone, so this decodes and re-encodes byte for byte with
    ///     no skeleton in hand. Reading the numbers as poses does need one: call
    ///     <see cref="Resolve"/> with <c>SkeletonDefinition.GetEffectiveTransformTypes()</c>, which is
    ///     the post-remap array <c>Class7.java:61</c> reads.
    ///     </para>
    ///     <para>
    ///     Empty frames are normal - 1,568 of the shipped files declare zero slots and are four bytes
    ///     long.
    ///     </para>
    /// </remarks>
    public sealed class FrameDefinition {
        /// <summary>The most slots a frame can hold, because the count is a single byte.</summary>
        /// <remarks>Reached in this cache: the largest frame declares exactly 255 slots.</remarks>
        public const int MaxTransforms = 255;

        /// <summary>The largest skeleton id the two-byte field can carry.</summary>
        public const int MaxSkeletonId = 0xFFFF;

        /// <summary>What <see cref="LeadingByte"/> holds in every file of this cache.</summary>
        /// <remarks>
        ///     359,931 of 359,931. The client reads it and drops it (<c>Class7.java:53</c>), so
        ///     nothing here can say what it is for - only that it is written and that it is one.
        /// </remarks>
        public const int LeadingByteInThisCache = 1;

        /// <summary>Transform type whose slot is a pivot later slots refer back to.</summary>
        /// <remarks><c>Class7.java:62-64, 96-101</c>.</remarks>
        private const int PivotType = 0;

        /// <summary>Transform types that take a pivot from the most recent unclaimed type-0 slot.</summary>
        private const int TranslateType = 1, RotateType = 2, ScaleType = 3;

        /// <summary>Transform types whose values are rescaled into a 14-bit angle.</summary>
        /// <remarks><c>Class7.java:91-95</c>: <c>value &lt;&lt; 2 &amp; 0x3fff</c>.</remarks>
        private const int RescaledTypeA = 2, RescaledTypeB = 9;

        /// <summary>Transform types whose missing axes default to 128 rather than 0.</summary>
        /// <remarks><c>Class7.java:72-74</c>.</remarks>
        private const int Defaults128TypeA = 3, Defaults128TypeB = 10;

        /// <summary>The default a type that is not <see cref="Defaults128TypeA"/> or B uses.</summary>
        private const int ZeroDefault = 0;

        /// <summary>The default those two types use instead.</summary>
        private const int OneTwentyEightDefault = 128;

        /// <summary>How far the rescale shifts, and the mask it lands in.</summary>
        private const int AngleShift = 2, AngleMask = 0x3FFF;

        /// <summary>The frame id: the packed <c>(group &lt;&lt; 16) | file</c> index 20 stores.</summary>
        /// <remarks>
        ///     Not stored in the file. It is the address, carried so a failure can be reported in the
        ///     terms the client uses.
        /// </remarks>
        public int Id { get; set; } = -1;

        /// <summary>
        ///     The first byte, which the client reads and throws away.
        /// </summary>
        /// <remarks>
        ///     Kept because it is written: it is 1 in every one of this cache's 359,931 frames, but
        ///     recomputing it as a constant would be an assumption the format does not support, and
        ///     the archive CRC covers whatever it really holds.
        /// </remarks>
        public int LeadingByte { get; set; } = LeadingByteInThisCache;

        /// <summary>
        ///     The index-1 group id of the skeleton this frame is played against.
        /// </summary>
        /// <remarks>
        ///     Every file of a group names the same skeleton in this cache, all 3526 of them, but the
        ///     field is per file and is stored per file.
        /// </remarks>
        public int SkeletonId { get; set; }

        /// <summary>The slots, in the order the file stores them, including the skipped ones.</summary>
        public List<FrameTransform> Transforms { get; } = new List<FrameTransform>();

        /// <summary>How many slots the frame declares, skipped ones included.</summary>
        public int TransformCount => Transforms.Count;

        /// <summary>How many signed smarts the value stream holds.</summary>
        public int StoredValueCount {
            get {
                int total = 0;
                foreach (FrameTransform transform in Transforms)
                    total += transform.StoredValueCount;
                return total;
            }
        }

        /// <summary>Decodes one frame.</summary>
        /// <remarks>
        ///     Reads the flag block and then the value stream, which is what the client's two cursors
        ///     amount to over a single pass. Nothing here consults the stream length, so a buffer with
        ///     bytes past the end of the record over-runs visibly instead of stopping on it.
        /// </remarks>
        /// <param name="stream">The stored file, positioned at its start.</param>
        /// <returns>This definition.</returns>
        /// <exception cref="ArgumentNullException"><paramref name="stream"/> is null.</exception>
        /// <exception cref="EndOfStreamException">The record ends inside a field.</exception>
        public FrameDefinition Decode(JagStream stream) {
            if (stream == null)
                throw new ArgumentNullException(nameof(stream));

            Transforms.Clear();

            LeadingByte = stream.ReadUnsignedByte();
            SkeletonId = stream.ReadUnsignedShort();

            int transformCount = stream.ReadUnsignedByte();
            for (int slot = 0; slot < transformCount; slot++)
                Transforms.Add(new FrameTransform { Flag = stream.ReadUnsignedByte() });

            /* The value stream starts where the flag block ends, which is what Class7.java:59
               (RSBuffer_0_.caret = RSBuffer.caret + i) says, so one cursor reading the flags to
               completion and then the values is the same read. */
            for (int slot = 0; slot < transformCount; slot++) {
                FrameTransform transform = Transforms[slot];
                if (transform.HasX)
                    transform.X = ReadValue(stream, slot, "x");
                if (transform.HasY)
                    transform.Y = ReadValue(stream, slot, "y");
                if (transform.HasZ)
                    transform.Z = ReadValue(stream, slot, "z");
            }

            return this;
        }

        /// <summary>Encodes this frame back to its stored representation.</summary>
        /// <remarks>
        ///     Byte-identical for an unedited frame. Every field is written from the stored value,
        ///     including the leading byte, the whole flag byte and the width each smart was read in -
        ///     so nothing is recomputed and nothing has to be argued about.
        /// </remarks>
        /// <returns>The encoded file, positioned at 0.</returns>
        /// <exception cref="InvalidOperationException">
        ///     A field has been edited past what its width can store. Reported rather than truncated:
        ///     a masked-off slot count writes a shorter flag block and silently reassigns every value
        ///     in the stream to a different bone.
        /// </exception>
        public JagStream Encode() {
            if (Transforms.Count > MaxTransforms)
                throw new InvalidOperationException(
                    "Frame " + Id + " declares " + Transforms.Count + " transforms, and the count is " +
                    "stored as a single byte, so at most " + MaxTransforms + " fit.");

            JagStream stream = new JagStream();
            stream.WriteByte(Checked(LeadingByte, byte.MaxValue, "leading byte"));
            stream.WriteShort(Checked(SkeletonId, MaxSkeletonId, "skeleton id"));
            stream.WriteByte(Transforms.Count);

            for (int slot = 0; slot < Transforms.Count; slot++)
                stream.WriteByte(Checked(Transforms[slot].Flag, FrameTransform.MaxFlag, "flag", slot));

            for (int slot = 0; slot < Transforms.Count; slot++) {
                FrameTransform transform = Transforms[slot];
                if (transform.HasX)
                    WriteValue(stream, transform.X, slot, "x");
                if (transform.HasY)
                    WriteValue(stream, transform.Y, slot, "y");
                if (transform.HasZ)
                    WriteValue(stream, transform.Z, slot, "z");
            }

            return stream.Flip();
        }

        /// <summary>
        ///     Reads this frame's numbers as the poses the client would transform with.
        /// </summary>
        /// <remarks>
        ///     A port of <c>Class7.java:56-115</c>. Three things the raw record cannot express happen
        ///     here and nowhere else: a missing axis takes 128 rather than 0 for types 3 and 10, every
        ///     axis of a type 2 or 9 is rescaled into a 14-bit angle, and types 1, 2 and 3 claim the
        ///     most recent type-0 slot as their pivot - once, so the second claimant gets none.
        ///     <para>
        ///     Take the types from <see cref="SkeletonDefinition.GetEffectiveTransformTypes"/> and
        ///     resolve them once per frame set: <c>Class7.java:61</c> reads the post-remap array, so a
        ///     frame resolved against the raw bytes would be wrong wherever a stored 6 occurred. None
        ///     occur in this cache, which is a fact about the data and not about the format.
        ///     </para>
        /// </remarks>
        /// <param name="effectiveTransformTypes">
        ///     The skeleton's transform types, one per bone, with the client's 6 to 2 remap applied.
        /// </param>
        /// <returns>The poses and the model-build bits they imply.</returns>
        /// <exception cref="ArgumentNullException"><paramref name="effectiveTransformTypes"/> is null.</exception>
        /// <exception cref="ArgumentException">
        ///     The skeleton has fewer bones than the frame has slots, so a slot names a bone that does
        ///     not exist. The client walks off the end of the array here and its own catch block turns
        ///     the frame into an empty one (<c>Class7.java:130-134</c>), which is a silent wrong
        ///     answer rather than a rule of the format.
        /// </exception>
        public ResolvedFrame Resolve(int[] effectiveTransformTypes) {
            if (effectiveTransformTypes == null)
                throw new ArgumentNullException(nameof(effectiveTransformTypes));
            if (effectiveTransformTypes.Length < Transforms.Count)
                throw new ArgumentException(
                    "Frame " + Id + " declares " + Transforms.Count + " transforms but skeleton " +
                    SkeletonId + " has only " + effectiveTransformTypes.Length + " bones, so slot " +
                    effectiveTransformTypes.Length + " names no bone.", nameof(effectiveTransformTypes));

            var poses = new List<FramePose>(Transforms.Count);
            int modelBuildFlags = 0;

            //The most recent type-0 slot, and the highest one already claimed as a pivot.
            int pivotCandidate = -1;
            int pivotClaimed = -1;

            for (int slot = 0; slot < Transforms.Count; slot++) {
                int type = effectiveTransformTypes[slot];
                if (type == PivotType)
                    pivotCandidate = slot;

                FrameTransform transform = Transforms[slot];
                if (transform.IsSkipped)
                    continue;

                if (type == PivotType)
                    pivotClaimed = slot;

                int fallback = type == Defaults128TypeA || type == Defaults128TypeB
                    ? OneTwentyEightDefault
                    : ZeroDefault;

                int x = transform.HasX ? transform.X.Value : fallback;
                int y = transform.HasY ? transform.Y.Value : fallback;
                int z = transform.HasZ ? transform.Z.Value : fallback;

                if (type == RescaledTypeA || type == RescaledTypeB) {
                    x = (x << AngleShift) & AngleMask;
                    y = (y << AngleShift) & AngleMask;
                    z = (z << AngleShift) & AngleMask;
                }

                /* Class7.java:96-108. The three arms are mutually exclusive in the client, so a type
                   that takes a pivot never sets a model flag and the other way round. */
                int pivotSlot = -1;
                if (type == TranslateType || type == RotateType || type == ScaleType) {
                    if (pivotCandidate > pivotClaimed) {
                        pivotSlot = pivotCandidate;
                        pivotClaimed = pivotCandidate;
                    }
                } else if (type == 5) {
                    modelBuildFlags |= ResolvedFrame.ModelFlagFromType5;
                } else if (type == 7) {
                    modelBuildFlags |= ResolvedFrame.ModelFlagFromType7;
                } else if (type == 8 || type == 9 || type == 10) {
                    modelBuildFlags |= ResolvedFrame.ModelFlagFromWideTypes;
                }

                poses.Add(new FramePose(slot, x, y, z, transform.SubType, pivotSlot));
            }

            return new ResolvedFrame(poses, modelBuildFlags);
        }

        /// <summary>Reads one signed smart, keeping the width it arrived in.</summary>
        /// <remarks>
        ///     The bounds check is here rather than left to <c>JagStream.ReadSmart</c> so a truncated
        ///     value stream names the slot and axis it died on. That reader peeks before it reads, so
        ///     without this a record ending exactly on a value boundary would surface as an index
        ///     exception with no context.
        /// </remarks>
        /// <param name="stream">The stream to read from.</param>
        /// <param name="slot">The slot being read, for the message.</param>
        /// <param name="axis">The axis being read, for the message.</param>
        /// <returns>The value and its stored width.</returns>
        private FrameValue ReadValue(JagStream stream, int slot, string axis) {
            if (stream.Position >= stream.Length)
                throw new EndOfStreamException(
                    "Frame " + Id + " ends before transform " + slot + "'s " + axis + " value, which its " +
                    "flag byte announces.");

            int value = stream.ReadSmart(out JagStream.SmartWidth width);
            return new FrameValue(value, width);
        }

        /// <summary>Writes one signed smart back in the width it was read in.</summary>
        /// <param name="stream">The stream to write to.</param>
        /// <param name="value">The value and its width.</param>
        /// <param name="slot">The slot being written, for the message.</param>
        /// <param name="axis">The axis being written, for the message.</param>
        /// <exception cref="InvalidOperationException">The value no longer fits its recorded width.</exception>
        private void WriteValue(JagStream stream, FrameValue value, int slot, string axis) {
            try {
                stream.WriteSmart(value.Value, value.Width);
            } catch (ArgumentOutOfRangeException ex) {
                throw new InvalidOperationException(
                    "Frame " + Id + " transform " + slot + ": " + axis + " is " + value.Value +
                    ", which does not fit the " + value.Width + " signed smart it was read as. Clear the " +
                    "recorded width to let the encoder pick one.", ex);
            }
        }

        /// <summary>Rejects a field an edit has pushed outside what the format can store.</summary>
        /// <param name="value">The value to write.</param>
        /// <param name="max">The largest value its field can hold.</param>
        /// <param name="what">The field, for the message.</param>
        /// <returns>The value, when it fits.</returns>
        private int Checked(int value, int max, string what) {
            if (value < 0 || value > max)
                throw new InvalidOperationException(
                    "Frame " + Id + ": " + what + " is " + value + ", which does not fit the 0.." + max +
                    " the format stores it in.");
            return value;
        }

        /// <summary>Rejects a per-slot field an edit has pushed outside what the format can store.</summary>
        /// <param name="value">The value to write.</param>
        /// <param name="max">The largest value its field can hold.</param>
        /// <param name="what">The field, for the message.</param>
        /// <param name="slot">The slot it belongs to, for the message.</param>
        /// <returns>The value, when it fits.</returns>
        private int Checked(int value, int max, string what, int slot) {
            if (value < 0 || value > max)
                throw new InvalidOperationException(
                    "Frame " + Id + " transform " + slot + ": " + what + " is " + value +
                    ", which does not fit the 0.." + max + " the format stores it in.");
            return value;
        }
    }
}
