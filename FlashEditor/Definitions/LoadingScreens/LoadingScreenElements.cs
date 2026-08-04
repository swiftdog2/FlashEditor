using System;
using System.IO;

namespace FlashEditor.Definitions.LoadingScreens {
    /// <summary>
    ///     Type 0. A bare 32-bit value, <c>Class298.method3503</c> (Class298.java:12-24).
    /// </summary>
    /// <remarks>
    ///     Wrapped in a <c>Class163</c> whose only field is that value; nothing in the 637 client
    ///     reads it back, so its meaning cannot be settled here. Unreachable in both supported caches.
    /// </remarks>
    public sealed class LoadingScreenIntegerElement : LoadingScreenElement {
        /// <inheritdoc/>
        public override int TypeIndex => 0;

        /// <summary>The stored 32-bit value.</summary>
        public int Value { get; set; }

        /// <inheritdoc/>
        public override void Decode(JagStream stream) => Value = stream.ReadInt();

        /// <inheritdoc/>
        public override void Encode(JagStream stream) => stream.WriteInteger(Value);
    }

    /// <summary>
    ///     Type 1. A placement followed by two 32-bit values, <c>Particle_Sub10.method3141</c>
    ///     (Particle_Sub10.java:20-33).
    /// </summary>
    /// <remarks>Unreachable in both supported caches, so nothing here defends the field widths but the client.</remarks>
    public sealed class LoadingScreenType1Element : LoadingScreenElement {
        /// <inheritdoc/>
        public override int TypeIndex => 1;

        /// <summary>Where the element is drawn.</summary>
        public LoadingScreenPlacement Placement { get; set; } = new LoadingScreenPlacement();

        /// <summary>First trailing 32-bit value, stored on <c>Class93_Sub3</c>.</summary>
        public int Value1 { get; set; }

        /// <summary>Second trailing 32-bit value.</summary>
        public int Value2 { get; set; }

        /// <inheritdoc/>
        public override void Decode(JagStream stream) {
            Placement.Decode(stream);
            Value1 = stream.ReadInt();
            Value2 = stream.ReadInt();
        }

        /// <inheritdoc/>
        public override void Encode(JagStream stream) {
            Placement.Encode(stream);
            stream.WriteInteger(Value1);
            stream.WriteInteger(Value2);
        }

        /// <inheritdoc/>
        public override LoadingScreenElement Clone() {
            var copy = (LoadingScreenType1Element) base.Clone();
            copy.Placement = Placement.Clone();
            return copy;
        }
    }

    /// <summary>
    ///     Type 2. A placement, two 32-bit values and an unsigned short,
    ///     <c>Class64_Sub27.method663</c> (Class64_Sub27.java:12-26).
    /// </summary>
    /// <remarks>Unreachable in both supported caches.</remarks>
    public sealed class LoadingScreenType2Element : LoadingScreenElement {
        /// <inheritdoc/>
        public override int TypeIndex => 2;

        /// <summary>Where the element is drawn.</summary>
        public LoadingScreenPlacement Placement { get; set; } = new LoadingScreenPlacement();

        /// <summary>First trailing 32-bit value.</summary>
        public int Value1 { get; set; }

        /// <summary>Second trailing 32-bit value.</summary>
        public int Value2 { get; set; }

        /// <summary>Trailing unsigned short.</summary>
        public int Value3 { get; set; }

        /// <inheritdoc/>
        public override void Decode(JagStream stream) {
            Placement.Decode(stream);
            Value1 = stream.ReadInt();
            Value2 = stream.ReadInt();
            Value3 = stream.ReadUnsignedShort();
        }

        /// <inheritdoc/>
        public override void Encode(JagStream stream) {
            Placement.Encode(stream);
            stream.WriteInteger(Value1);
            stream.WriteInteger(Value2);
            stream.WriteShort(Value3);
        }

        /// <inheritdoc/>
        public override LoadingScreenElement Clone() {
            var copy = (LoadingScreenType2Element) base.Clone();
            copy.Placement = Placement.Clone();
            return copy;
        }
    }

    /// <summary>
    ///     Type 3. A placement followed by six unsigned shorts, <c>Class338.method3781</c>
    ///     (Class338.java:44-62).
    /// </summary>
    /// <remarks>
    ///     Type 9 is this record plus one signed short, which is why it decodes through this one.
    ///     Unreachable in both supported caches.
    /// </remarks>
    public class LoadingScreenType3Element : LoadingScreenElement {
        /// <inheritdoc/>
        public override int TypeIndex => 3;

        /// <summary>Where the element is drawn.</summary>
        public LoadingScreenPlacement Placement { get; set; } = new LoadingScreenPlacement();

        /// <summary>The six trailing unsigned shorts, in stored order.</summary>
        /// <remarks>Kept as an array because none of the six is read back anywhere in the 637 client.</remarks>
        public int[] Values { get; set; } = new int[TrailingShorts];

        /// <summary>How many unsigned shorts follow the placement.</summary>
        protected const int TrailingShorts = 6;

        /// <inheritdoc/>
        public override void Decode(JagStream stream) {
            Placement.Decode(stream);
            Values = new int[TrailingShorts];
            for (int i = 0; i < TrailingShorts; i++)
                Values[i] = stream.ReadUnsignedShort();
        }

        /// <inheritdoc/>
        public override void Encode(JagStream stream) {
            Placement.Encode(stream);
            for (int i = 0; i < TrailingShorts; i++)
                stream.WriteShort(i < Values.Length ? Values[i] : 0);
        }

        /// <inheritdoc/>
        public override LoadingScreenElement Clone() {
            var copy = (LoadingScreenType3Element) base.Clone();
            copy.Placement = Placement.Clone();
            copy.Values = (int[]) Values.Clone();
            return copy;
        }
    }

    /// <summary>
    ///     Type 4. Its own placement layout plus three 32-bit values and a flag,
    ///     <c>Node_Sub40.method1469</c> (Node_Sub40.java:6-22).
    /// </summary>
    /// <remarks>
    ///     Deliberately not built on <see cref="LoadingScreenPlacement"/>: this type reads a leading
    ///     byte the shared prefix does not have, and drops the shared prefix's signed short. The two
    ///     layouts are the same length by coincidence, and treating them as one would shift every
    ///     field. Unreachable in both supported caches.
    /// </remarks>
    public sealed class LoadingScreenType4Element : LoadingScreenElement {
        /// <inheritdoc/>
        public override int TypeIndex => 4;

        /// <summary>Leading byte, read before the anchors.</summary>
        public int Value0 { get; set; }

        /// <summary>Index into the three-entry horizontal anchor table.</summary>
        public int HorizontalAnchor { get; set; }

        /// <summary>Index into the three-entry vertical anchor table.</summary>
        public int VerticalAnchor { get; set; }

        /// <summary>Horizontal offset from the anchor, signed.</summary>
        public int OffsetX { get; set; }

        /// <summary>Vertical offset from the anchor, signed.</summary>
        public int OffsetY { get; set; }

        /// <summary>Width of the box the anchor is resolved against.</summary>
        public int Width { get; set; }

        /// <summary>Height of the box the anchor is resolved against.</summary>
        public int Height { get; set; }

        /// <summary>First trailing 32-bit value.</summary>
        public int Value1 { get; set; }

        /// <summary>Second trailing 32-bit value.</summary>
        public int Value2 { get; set; }

        /// <summary>Third trailing 32-bit value.</summary>
        public int Value3 { get; set; }

        /// <summary>
        ///     The stored trailing byte, which the client turns into a bool by comparing it to 1.
        /// </summary>
        /// <remarks>
        ///     Node_Sub40.java:18 spells it <c>(readUnsignedByte() ^ 0xffffffff) == i</c> with
        ///     <c>i == -2</c>, so the test is against 1 and every other value means false. Kept as the
        ///     byte, because recomputing it from the bool would rewrite any stored value but 0 and 1.
        /// </remarks>
        public int FlagStored { get; set; }

        /// <summary>What the client makes of <see cref="FlagStored"/>.</summary>
        public bool Flag => FlagStored == 1;

        /// <inheritdoc/>
        public override void Decode(JagStream stream) {
            Value0 = stream.ReadUnsignedByte();
            HorizontalAnchor = stream.ReadUnsignedByte();
            VerticalAnchor = stream.ReadUnsignedByte();
            OffsetX = stream.ReadShort();
            OffsetY = stream.ReadShort();
            Width = stream.ReadUnsignedShort();
            Height = stream.ReadUnsignedShort();
            Value1 = stream.ReadInt();
            Value2 = stream.ReadInt();
            Value3 = stream.ReadInt();
            FlagStored = stream.ReadUnsignedByte();
        }

        /// <inheritdoc/>
        public override void Encode(JagStream stream) {
            stream.WriteByte((byte) Value0);
            stream.WriteByte((byte) HorizontalAnchor);
            stream.WriteByte((byte) VerticalAnchor);
            stream.WriteShort(OffsetX);
            stream.WriteShort(OffsetY);
            stream.WriteShort(Width);
            stream.WriteShort(Height);
            stream.WriteInteger(Value1);
            stream.WriteInteger(Value2);
            stream.WriteInteger(Value3);
            stream.WriteByte((byte) FlagStored);
        }
    }

    /// <summary>
    ///     Type 5. A sprite drawn at an anchored offset, <c>RenderType.method1796</c>
    ///     (RenderType.java:1796 block).
    /// </summary>
    /// <remarks>
    ///     The most common element in this cache. Its leading unsigned short is a sprite id in the
    ///     loading-sprite archive - Class134.java:93 and :106 fetch it out of that archive, and
    ///     InterfaceSettings.java:73-74 picks index 32 or 34 for the role. Index 34 is empty in both
    ///     supported caches, so index 32 is the only source that resolves.
    ///     <para>
    ///     Type 6 is this record plus a signed 24-bit value, which is why it decodes through this one.
    ///     </para>
    /// </remarks>
    public class LoadingScreenSpriteElement : LoadingScreenElement {
        /// <inheritdoc/>
        public override int TypeIndex => 5;

        /// <summary>Sprite id in the loading-sprite archive.</summary>
        public int SpriteId { get; set; }

        /// <summary>Index into the three-entry horizontal anchor table.</summary>
        public int HorizontalAnchor { get; set; }

        /// <summary>Index into the three-entry vertical anchor table.</summary>
        public int VerticalAnchor { get; set; }

        /// <summary>Horizontal offset from the anchor, signed.</summary>
        /// <remarks>Class134.java:121-122 adds it to the resolved anchor position.</remarks>
        public int OffsetX { get; set; }

        /// <summary>Vertical offset from the anchor, signed.</summary>
        public int OffsetY { get; set; }

        /// <inheritdoc/>
        public override void Decode(JagStream stream) {
            SpriteId = stream.ReadUnsignedShort();
            HorizontalAnchor = stream.ReadUnsignedByte();
            VerticalAnchor = stream.ReadUnsignedByte();
            OffsetX = stream.ReadShort();
            OffsetY = stream.ReadShort();
        }

        /// <inheritdoc/>
        public override void Encode(JagStream stream) {
            stream.WriteShort(SpriteId);
            stream.WriteByte((byte) HorizontalAnchor);
            stream.WriteByte((byte) VerticalAnchor);
            stream.WriteShort(OffsetX);
            stream.WriteShort(OffsetY);
        }
    }

    /// <summary>
    ///     Type 6. A type-5 sprite record followed by a <b>signed</b> 24-bit value,
    ///     <c>Class138.method2277</c> (Class138.java:18-30).
    /// </summary>
    /// <remarks>
    ///     The signedness is the trap here and no byte-identity sweep can catch it: the file header's
    ///     own 24-bit field uses <c>RSBuffer.method1186</c> (RSBuffer.java:131-135), which is
    ///     unsigned, while this one uses <c>method1227</c> (:482-497), which sign-extends. Both
    ///     re-encode to the same three bytes and only one shows the right number in an editor.
    ///     Unreachable in both supported caches.
    /// </remarks>
    public sealed class LoadingScreenType6Element : LoadingScreenSpriteElement {
        /// <summary>Largest value the signed 24-bit field can hold.</summary>
        private const int SignedMediumMax = 0x7FFFFF;

        /// <summary>How much a negative signed 24-bit value is below its unsigned reading.</summary>
        private const int SignedMediumSpan = 0x1000000;

        /// <inheritdoc/>
        public override int TypeIndex => 6;

        /// <summary>The trailing signed 24-bit value.</summary>
        public int SignedMedium { get; set; }

        /// <inheritdoc/>
        public override void Decode(JagStream stream) {
            base.Decode(stream);
            int value = stream.ReadMedium();
            SignedMedium = value > SignedMediumMax ? value - SignedMediumSpan : value;
        }

        /// <inheritdoc/>
        public override void Encode(JagStream stream) {
            base.Encode(stream);
            stream.WriteMedium(SignedMedium);
        }
    }

    /// <summary>
    ///     Type 7. A string followed by an anchored placement and eight further fields,
    ///     <c>MobEntity.method3024</c> (MobEntity.java:3024 block).
    /// </summary>
    /// <remarks>
    ///     This is the element that carries the tip text. The string is NUL terminated and has no
    ///     length prefix, so it is the only element whose record is not a fixed width.
    /// </remarks>
    public sealed class LoadingScreenTextElement : LoadingScreenElement {
        private byte[] textBytes = Array.Empty<byte>();

        /// <inheritdoc/>
        public override int TypeIndex => 7;

        /// <summary>
        ///     The string exactly as the file stores it, terminator excluded.
        /// </summary>
        /// <remarks>
        ///     Kept as bytes rather than as a decoded string because decoding is lossy at the edges:
        ///     <c>Node_Sub46_Sub6.method1546</c> (:11-34) remaps 0x80-0x9F through a table with five
        ///     unassigned slots and substitutes '?' for each, so those five bytes cannot be recovered
        ///     from the text. No string in either supported cache holds a byte above 0x7F, so a sweep
        ///     here proves nothing about that - which is exactly why the bytes are kept.
        /// </remarks>
        public byte[] TextBytes {
            get => textBytes;
            set => textBytes = value ?? Array.Empty<byte>();
        }

        /// <summary>The string as the client would display it.</summary>
        /// <remarks>
        ///     Reading is always safe. Writing goes through the client's own encoding and is
        ///     therefore lossy in the two places <see cref="JagStream.WriteJagexString"/> documents,
        ///     which is why it is a separate operation from holding the bytes.
        /// </remarks>
        public string Text {
            get {
                byte[] terminated = new byte[textBytes.Length + 1];
                Array.Copy(textBytes, terminated, textBytes.Length);
                return new JagStream(terminated).ReadJagexString();
            }
            set {
                var buffer = new JagStream();
                buffer.WriteJagexString(value ?? string.Empty);
                byte[] encoded = buffer.Flip().ToArray();

                //WriteJagexString appends the terminator; the stored form here excludes it.
                textBytes = new byte[encoded.Length - 1];
                Array.Copy(encoded, textBytes, textBytes.Length);
            }
        }

        /// <summary>Index into the three-entry horizontal anchor table.</summary>
        public int HorizontalAnchor { get; set; }

        /// <summary>Index into the three-entry vertical anchor table.</summary>
        public int VerticalAnchor { get; set; }

        /// <summary>Horizontal offset from the anchor, signed.</summary>
        public int OffsetX { get; set; }

        /// <summary>Vertical offset from the anchor, signed.</summary>
        public int OffsetY { get; set; }

        /// <summary>First of three trailing bytes.</summary>
        public int Byte1 { get; set; }

        /// <summary>Second of three trailing bytes.</summary>
        public int Byte2 { get; set; }

        /// <summary>Third of three trailing bytes.</summary>
        public int Byte3 { get; set; }

        /// <summary>First trailing unsigned short.</summary>
        public int Short1 { get; set; }

        /// <summary>Second trailing unsigned short.</summary>
        public int Short2 { get; set; }

        /// <summary>First trailing 32-bit value.</summary>
        public int Value1 { get; set; }

        /// <summary>Second trailing 32-bit value.</summary>
        public int Value2 { get; set; }

        /// <summary>Third trailing 32-bit value.</summary>
        public int Value3 { get; set; }

        /// <inheritdoc/>
        public override void Decode(JagStream stream) {
            int start = stream.Position;
            while (true) {
                int read = stream.ReadByte();
                if (read < 0)
                    throw new EndOfStreamException(
                        "A loading-screen text element ran off the end of the file looking for its " +
                        "string terminator.");
                if (read == 0)
                    break;
            }

            int length = stream.Position - start - 1;
            stream.Position = start;
            textBytes = stream.ReadBytes(length);
            stream.Position += 1;

            HorizontalAnchor = stream.ReadUnsignedByte();
            VerticalAnchor = stream.ReadUnsignedByte();
            OffsetX = stream.ReadShort();
            OffsetY = stream.ReadShort();
            Byte1 = stream.ReadUnsignedByte();
            Byte2 = stream.ReadUnsignedByte();
            Byte3 = stream.ReadUnsignedByte();
            Short1 = stream.ReadUnsignedShort();
            Short2 = stream.ReadUnsignedShort();
            Value1 = stream.ReadInt();
            Value2 = stream.ReadInt();
            Value3 = stream.ReadInt();
        }

        /// <inheritdoc/>
        public override void Encode(JagStream stream) {
            stream.Write(textBytes, 0, textBytes.Length);
            stream.WriteByte(0);
            stream.WriteByte((byte) HorizontalAnchor);
            stream.WriteByte((byte) VerticalAnchor);
            stream.WriteShort(OffsetX);
            stream.WriteShort(OffsetY);
            stream.WriteByte((byte) Byte1);
            stream.WriteByte((byte) Byte2);
            stream.WriteByte((byte) Byte3);
            stream.WriteShort(Short1);
            stream.WriteShort(Short2);
            stream.WriteInteger(Value1);
            stream.WriteInteger(Value2);
            stream.WriteInteger(Value3);
        }

        /// <inheritdoc/>
        public override LoadingScreenElement Clone() {
            var copy = (LoadingScreenTextElement) base.Clone();
            copy.textBytes = (byte[]) textBytes.Clone();
            return copy;
        }
    }

    /// <summary>
    ///     Type 8. A bare unsigned short, <c>Node_Sub46_Sub19.method1634</c>
    ///     (Node_Sub46_Sub19.java:1634 block).
    /// </summary>
    /// <remarks>Unreachable in both supported caches.</remarks>
    public sealed class LoadingScreenType8Element : LoadingScreenElement {
        /// <inheritdoc/>
        public override int TypeIndex => 8;

        /// <summary>The stored unsigned short.</summary>
        public int Value { get; set; }

        /// <inheritdoc/>
        public override void Decode(JagStream stream) => Value = stream.ReadUnsignedShort();

        /// <inheritdoc/>
        public override void Encode(JagStream stream) => stream.WriteShort(Value);
    }

    /// <summary>
    ///     Type 9. A type-3 record followed by a signed short, <c>Class362.method3924</c>
    ///     (Class362.java:6-22).
    /// </summary>
    /// <remarks>
    ///     Occurs in this cache - exactly once per screen file - so its width is pinned by the
    ///     byte-identity sweep, unlike the type 3 it is built on.
    /// </remarks>
    public sealed class LoadingScreenType9Element : LoadingScreenType3Element {
        /// <inheritdoc/>
        public override int TypeIndex => 9;

        /// <summary>The trailing signed short.</summary>
        public int TrailingValue { get; set; }

        /// <inheritdoc/>
        public override void Decode(JagStream stream) {
            base.Decode(stream);
            TrailingValue = stream.ReadShort();
        }

        /// <inheritdoc/>
        public override void Encode(JagStream stream) {
            base.Encode(stream);
            stream.WriteShort(TrailingValue);
        }
    }
}
