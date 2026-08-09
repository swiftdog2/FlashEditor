using System;
using FlashEditor.IO;

namespace FlashEditor.Definitions.LoadingScreens {
    /// <summary>
    ///     One drawable on a loading screen, tagged by the type byte that precedes it.
    /// </summary>
    /// <remarks>
    ///     A screen file is a header followed by <c>count</c> records, each a type byte indexing the
    ///     ten-entry table <c>Class48_Sub2_Sub1.method476</c> returns (Class48_Sub2_Sub1.java:223-233)
    ///     and then a record of a width the type alone fixes. There is no length prefix anywhere, so
    ///     mis-sizing one record loses the whole rest of the file.
    ///     <para>
    ///     <b>All ten are implemented and only three occur in either supported cache.</b> The other
    ///     seven are ported from their client decoders on faith, for the reason
    ///     <c>CLAUDE.md</c> already records for the reference-table flags: the first file that uses
    ///     one is mis-parsed from that element onward, and no sweep over this cache would catch it
    ///     because no file here exercises the branch. A passing sweep is therefore not evidence that
    ///     the unexercised seven are right.
    ///     </para>
    /// </remarks>
    public abstract class LoadingScreenElement {
        /// <summary>How many element types the format defines.</summary>
        public const int TypeCount = 10;

        /// <summary>
        ///     The per-type version byte the manifest must agree with, in type order.
        /// </summary>
        /// <remarks>
        ///     Each is the <c>Class113.anInt955</c> of the corresponding entry in
        ///     <c>Class48_Sub2_Sub1.method476</c>'s array: Class100.java:7, Class47.java:3,
        ///     Class137.java:3, Node_Sub44.java:7, Class365.java:11, Class280.java:21,
        ///     Node_Sub10_Sub3.java:7, Class308.java:10, Class4.java:17, Class18.java:7.
        ///     <para>
        ///     It is a compatibility handshake that fails silently: if the manifest's copy disagrees
        ///     on the count or on any byte, Class282.java:86-89 empties both of its arrays and the
        ///     client shows no loading screen at all, with no error. The manifest's bytes are
        ///     therefore replayed verbatim rather than regenerated from this - see
        ///     <see cref="LoadingScreenManifest.TypeVersions"/>.
        ///     </para>
        /// </remarks>
        public static readonly int[] ClientTypeVersions = { 1, 2, 2, 2, 1, 1, 1, 2, 1, 2 };

        /// <summary>The element's index into the ten-entry type table.</summary>
        public abstract int TypeIndex { get; }

        /// <summary>Reads this element's record, leaving the stream on the byte after it.</summary>
        /// <param name="stream">The screen file, positioned just after the type byte.</param>
        public abstract void Decode(JagStream stream);

        /// <summary>Writes this element's record, type byte excluded.</summary>
        /// <param name="stream">The buffer being assembled.</param>
        public abstract void Encode(JagStream stream);

        /// <summary>Takes a copy no edit through this instance can reach.</summary>
        /// <returns>An independent element holding the same values.</returns>
        public virtual LoadingScreenElement Clone() => (LoadingScreenElement) MemberwiseClone();

        /// <summary>
        ///     Builds the element type a stored type byte names.
        /// </summary>
        /// <remarks>
        ///     Throws rather than returning null for an out-of-range type. The client would index its
        ///     ten-entry array and throw too (Class124.java:104), and a decoder that skipped the
        ///     element instead would have no way to know how many bytes to skip.
        /// </remarks>
        /// <param name="typeIndex">The stored type byte.</param>
        /// <returns>An empty element of that type.</returns>
        public static LoadingScreenElement Create(int typeIndex) {
            switch (typeIndex) {
                case 0: return new LoadingScreenIntegerElement();
                case 1: return new LoadingScreenType1Element();
                case 2: return new LoadingScreenType2Element();
                case 3: return new LoadingScreenType3Element();
                case 4: return new LoadingScreenType4Element();
                case 5: return new LoadingScreenSpriteElement();
                case 6: return new LoadingScreenType6Element();
                case 7: return new LoadingScreenTextElement();
                case 8: return new LoadingScreenType8Element();
                case 9: return new LoadingScreenType9Element();
                default:
                    throw new InvalidOperationException(
                        "Loading-screen element type " + typeIndex + " is outside the " + TypeCount +
                        " the format defines, so the rest of the file cannot be sized.");
            }
        }
    }

    /// <summary>
    ///     Where an element sits on the screen and how it is drawn, as the four types that share
    ///     <c>Class105.method1716</c> store it.
    /// </summary>
    /// <remarks>
    ///     Twenty bytes, read at Class105.java:42-62 and used by element types 1, 2, 3 and 9. Every
    ///     field's meaning comes from Class373.java:208-228, which is the only place in the 637
    ///     client that reads one of these back: it anchors the box, offsets it, then draws inside it.
    ///     <para>
    ///     <b>Signedness here is untestable by any sweep.</b> The reader takes s16, s16, u16, u16,
    ///     s16 in a row and all five re-encode to the same bytes whichever way round they are read.
    ///     Only the client settles them.
    ///     </para>
    /// </remarks>
    public sealed class LoadingScreenPlacement {
        /// <summary>Bytes this placement occupies.</summary>
        public const int EncodedSize = 20;

        /// <summary>Index into the three-entry horizontal anchor table.</summary>
        /// <remarks>
        ///     <c>OnDemandRequest.method1595</c> (OnDemandRequest.java:82-92) returns left, centre and
        ///     right in that order; Class373.java:208 resolves the box's x through it.
        /// </remarks>
        public int HorizontalAnchor { get; set; }

        /// <summary>Index into the three-entry vertical anchor table.</summary>
        /// <remarks>
        ///     <c>Class331.method3723</c> (Class331.java:7-17), whose <c>method2088</c>
        ///     (Class110.java:42-58) returns 0 for the first entry, <c>(screen - size) / 2</c> for the
        ///     second and <c>screen - size</c> for the third - top, centre, bottom.
        /// </remarks>
        public int VerticalAnchor { get; set; }

        /// <summary>Horizontal offset from the anchor, signed.</summary>
        public int OffsetX { get; set; }

        /// <summary>Vertical offset from the anchor, signed.</summary>
        public int OffsetY { get; set; }

        /// <summary>Width of the box the anchor is resolved against.</summary>
        public int Width { get; set; }

        /// <summary>Height of the box the anchor is resolved against.</summary>
        public int Height { get; set; }

        /// <summary>Extra vertical offset applied to the content inside the box, signed.</summary>
        /// <remarks>Class373.java:228 adds it after centring the content vertically.</remarks>
        public int ContentOffsetY { get; set; }

        /// <summary>Font id in index 13, drawn through <c>Class119_Sub1.method2182</c>.</summary>
        public int FontId { get; set; }

        /// <summary>Colour the content is drawn in, as the client hands it to the font renderer.</summary>
        public int Colour { get; set; }

        /// <summary>Reads the twenty-byte placement.</summary>
        /// <param name="stream">The file, positioned at the placement.</param>
        public void Decode(JagStream stream) {
            HorizontalAnchor = stream.ReadUnsignedByte();
            VerticalAnchor = stream.ReadUnsignedByte();
            OffsetX = stream.ReadShort();
            OffsetY = stream.ReadShort();
            Width = stream.ReadUnsignedShort();
            Height = stream.ReadUnsignedShort();
            ContentOffsetY = stream.ReadShort();
            FontId = stream.ReadInt();
            Colour = stream.ReadInt();
        }

        /// <summary>Writes the twenty-byte placement.</summary>
        /// <param name="stream">The buffer being assembled.</param>
        public void Encode(JagStream stream) {
            stream.WriteByte((byte) HorizontalAnchor);
            stream.WriteByte((byte) VerticalAnchor);
            stream.WriteShort(OffsetX);
            stream.WriteShort(OffsetY);
            stream.WriteShort(Width);
            stream.WriteShort(Height);
            stream.WriteShort(ContentOffsetY);
            stream.WriteInteger(FontId);
            stream.WriteInteger(Colour);
        }

        /// <summary>Takes an independent copy.</summary>
        /// <returns>A placement holding the same values.</returns>
        public LoadingScreenPlacement Clone() => (LoadingScreenPlacement) MemberwiseClone();
    }
}
