using System;
using System.Collections.Generic;
using System.Linq;
using FlashEditor;
using FlashEditor.Definitions.LoadingScreens;
using Xunit;
using Xunit.Abstractions;
using FlashEditor.IO;

namespace FlashEditor.Tests.Definitions
{
    /// <summary>
    ///     Index-33 codec tests over hand-laid bytes, for the branches the cache cannot speak for.
    /// </summary>
    /// <remarks>
    ///     Seven of the ten element types occur in neither supported cache, so the real-cache sweep
    ///     passes whatever this codec does with them - and the first file that uses one would be
    ///     mis-parsed from that element onward with nothing to catch it. These records are therefore
    ///     written out field by field in the order the client's decoder reads them, and the width of
    ///     each is asserted against the width that decoder consumes.
    /// </remarks>
    public sealed class LoadingScreenCodecTests
    {
        private readonly ITestOutputHelper _output;

        /// <summary>Binds the per-test output sink.</summary>
        public LoadingScreenCodecTests(ITestOutputHelper output)
        {
            _output = output;
        }

        /// <summary>Bytes a screen file's own header occupies before the first element.</summary>
        private const int HeaderSize = 6;

        /// <summary>
        ///     Each element type consumes exactly the bytes its client decoder reads.
        /// </summary>
        /// <remarks>
        ///     The theory carries each decoder's <em>read sequence</em> rather than the total it comes
        ///     to, because the total is the part that gets mis-transcribed: type 2 shipped as 26 when
        ///     <c>Class64_Sub27.method663</c> reads 30, and a wrong total is indistinguishable from a
        ///     wrong decoder. A sequence can be checked call by call against the client, and
        ///     <see cref="ClientReadWidth"/> does the arithmetic that a reader otherwise does by eye.
        ///     Type 7 is absent because it is the one variable-width record - its string has no length
        ///     prefix - and is covered on its own below.
        /// </remarks>
        /// <param name="typeIndex">The element type.</param>
        /// <param name="clientReads">
        ///     The client decoder's reads in order, as <see cref="ClientReadWidth"/> spells them.
        /// </param>
        [Theory]
        [InlineData(0, "i")]        //Class298.method3503:200-207
        [InlineData(1, "Pii")]      //Particle_Sub10.method3141:1-14
        [InlineData(2, "Piis")]     //Class64_Sub27.method663:10-26
        [InlineData(3, "T")]        //Class338.method3781:44-62
        [InlineData(4, "bbbssssiiib")] //Node_Sub40.method1469:1-19
        [InlineData(5, "R")]        //RenderType.method1796:1-17
        [InlineData(6, "Rm")]       //Class138.method2277:1-15
        [InlineData(8, "s")]        //Node_Sub46_Sub19.method1634:1-12
        [InlineData(9, "Ts")]       //Class362.method3924:1-18
        public void EachFixedWidthElementConsumesTheBytesItsClientDecoderReads(int typeIndex, string clientReads)
        {
            int recordSize = ClientReadWidth(clientReads);

            byte[] file = Screen(1000, 500, writer =>
            {
                writer.WriteByte((byte) typeIndex);
                for (int i = 0; i < recordSize; i++)
                    writer.WriteByte((byte) (i + 1));
            });

            var stream = new JagStream(file);
            var screen = new LoadingScreenDefinition().Decode(stream);

            Assert.Equal(file.Length, stream.Position);
            Assert.Single(screen.Elements);
            Assert.Equal(typeIndex, screen.Elements[0].TypeIndex);
            Assert.Equal(file, screen.Encode().ToArray());
            Assert.Equal(HeaderSize + 1 + recordSize, file.Length);
        }

        /// <summary>
        ///     Bytes a transcribed client read sequence consumes.
        /// </summary>
        /// <remarks>
        ///     <c>b</c> is <c>readUnsignedByte</c>, <c>s</c> either short read, <c>m</c> a 24-bit read
        ///     and <c>i</c> <c>readInt</c>. The three composites are the decoders the client itself
        ///     reuses: <c>P</c> is the shared placement <c>Class105.method1716</c>, <c>R</c> is
        ///     <c>RenderType.method1796</c> and <c>T</c> is <c>Class338.method3781</c>, so a nested
        ///     record is written the way the client writes it rather than expanded by hand.
        /// </remarks>
        /// <param name="reads">The read sequence.</param>
        /// <returns>Bytes the sequence consumes.</returns>
        private static int ClientReadWidth(string reads)
        {
            int width = 0;

            foreach (char read in reads)
            {
                switch (read)
                {
                    case 'b': width += 1; break;
                    case 's': width += 2; break;
                    case 'm': width += 3; break;
                    case 'i': width += 4; break;
                    case 'P': width += ClientReadWidth("bbsssssii"); break;
                    case 'R': width += ClientReadWidth("sbbss"); break;
                    case 'T': width += ClientReadWidth("Pssssss"); break;
                    default: throw new ArgumentException("unknown read '" + read + "'", nameof(reads));
                }
            }

            return width;
        }

        /// <summary>
        ///     The shared placement's transcribed width is the size the codec encodes it at.
        /// </summary>
        /// <remarks>
        ///     Ties <see cref="ClientReadWidth"/> back to the production constant, so the two cannot
        ///     drift and leave the theory above measuring itself.
        /// </remarks>
        [Fact]
        public void TheTranscribedPlacementWidthIsThePlacementTheCodecEncodes()
        {
            Assert.Equal(LoadingScreenPlacement.EncodedSize, ClientReadWidth("P"));
        }

        /// <summary>
        ///     A screen holding one of every element type round-trips to the bytes it was laid out as.
        /// </summary>
        /// <remarks>
        ///     The one test that exercises the dispatch itself. Every type's record is distinct, so an
        ///     element sized wrongly shifts the type byte of the next and the decode fails outright
        ///     rather than quietly reading the wrong fields.
        /// </remarks>
        [Fact]
        public void AScreenHoldingOneOfEveryElementTypeRoundTrips()
        {
            byte[] file = Screen(9000, 1000, writer =>
            {
                WriteIntegerElement(writer, 0x11223344);
                WritePlacedElement(writer, 1, inner => { inner.WriteInteger(101); inner.WriteInteger(102); });
                WritePlacedElement(writer, 2, inner =>
                {
                    inner.WriteInteger(201);
                    inner.WriteInteger(202);
                    inner.WriteShort(203);
                });
                WritePlacedElement(writer, 3, inner => WriteShorts(inner, 301, 302, 303, 304, 305, 306));
                WriteType4Element(writer);
                WriteSpriteElement(writer, 5, 3763, null);
                WriteSpriteElement(writer, 6, 3764, inner => inner.WriteMedium(-1));
                WriteTextElement(writer, "A crossbow isn't just for killing");
                WriteShortElement(writer, 8, 808);
                WritePlacedElement(writer, 9, inner =>
                {
                    WriteShorts(inner, 901, 902, 903, 904, 905, 906);
                    inner.WriteShort(-907);
                });
            });

            var stream = new JagStream(file);
            var screen = new LoadingScreenDefinition().Decode(stream);

            Assert.Equal(file.Length, stream.Position);
            Assert.Equal(9000, screen.DisplayDurationMs);
            Assert.Equal(1000, screen.SecondTiming);
            Assert.Equal(Enumerable.Range(0, LoadingScreenElement.TypeCount),
                screen.Elements.Select(element => element.TypeIndex));

            //Spot the fields that would move if a neighbouring record were sized wrongly.
            Assert.Equal(0x11223344, ((LoadingScreenIntegerElement) screen.Elements[0]).Value);
            Assert.Equal(102, ((LoadingScreenType1Element) screen.Elements[1]).Value2);
            Assert.Equal(203, ((LoadingScreenType2Element) screen.Elements[2]).Value3);
            Assert.Equal(new[] { 301, 302, 303, 304, 305, 306 },
                ((LoadingScreenType3Element) screen.Elements[3]).Values);
            Assert.Equal(3763, ((LoadingScreenSpriteElement) screen.Elements[5]).SpriteId);
            Assert.Equal(-1, ((LoadingScreenType6Element) screen.Elements[6]).SignedMedium);
            Assert.Equal("A crossbow isn't just for killing",
                ((LoadingScreenTextElement) screen.Elements[7]).Text);
            Assert.Equal(808, ((LoadingScreenType8Element) screen.Elements[8]).Value);
            Assert.Equal(-907, ((LoadingScreenType9Element) screen.Elements[9]).TrailingValue);

            Assert.Equal(file, screen.Encode().ToArray());
        }

        /// <summary>
        ///     The shared twenty-byte placement reads its fields in the order the client reads them.
        /// </summary>
        /// <remarks>
        ///     Every value distinct, so a transposed pair fails rather than passing on two equal
        ///     numbers. Signedness is the part no sweep can defend: <c>Class105.method1716</c> reads
        ///     s16, s16, u16, u16, s16 in a row and all five re-encode to the same bytes whichever way
        ///     round they are read.
        /// </remarks>
        [Fact]
        public void TheSharedPlacementReadsItsFieldsInTheClientsOrder()
        {
            byte[] file = Screen(1, 2, writer =>
            {
                writer.WriteByte(1);
                writer.WriteByte(2);            //horizontal anchor
                writer.WriteByte(1);            //vertical anchor
                writer.WriteShort(-30);         //offset x, signed
                writer.WriteShort(-40);         //offset y, signed
                writer.WriteShort(50);          //width, unsigned
                writer.WriteShort(60);          //height, unsigned
                writer.WriteShort(-70);         //content offset y, signed
                writer.WriteInteger(80);        //font id
                writer.WriteInteger(0x00FF8040);//colour
                writer.WriteInteger(90);
                writer.WriteInteger(100);
            });

            var screen = new LoadingScreenDefinition().Decode(new JagStream(file));
            LoadingScreenPlacement placement = ((LoadingScreenType1Element) screen.Elements[0]).Placement;

            Assert.Equal(2, placement.HorizontalAnchor);
            Assert.Equal(1, placement.VerticalAnchor);
            Assert.Equal(-30, placement.OffsetX);
            Assert.Equal(-40, placement.OffsetY);
            Assert.Equal(50, placement.Width);
            Assert.Equal(60, placement.Height);
            Assert.Equal(-70, placement.ContentOffsetY);
            Assert.Equal(80, placement.FontId);
            Assert.Equal(0x00FF8040, placement.Colour);
            Assert.Equal(LoadingScreenPlacement.EncodedSize + 8, file.Length - HeaderSize - 1);
            Assert.Equal(file, screen.Encode().ToArray());
        }

        /// <summary>
        ///     Type 6's trailing 24-bit value is signed, unlike the file header's.
        /// </summary>
        /// <remarks>
        ///     <c>Class138.method2277</c> reads it through <c>RSBuffer.method1227</c>, which
        ///     sign-extends (RSBuffer.java:482-497), while the header uses <c>method1186</c>, which
        ///     does not (:131-135). The two re-encode identically, so only a value read back can tell
        ///     them apart.
        /// </remarks>
        [Fact]
        public void Type6ReadsASignedMediumWhileTheHeaderReadsAnUnsignedOne()
        {
            byte[] file = Screen(0xFFFFFF, 0, writer =>
                WriteSpriteElement(writer, 6, 1, inner => inner.WriteMedium(-2)));

            var screen = new LoadingScreenDefinition().Decode(new JagStream(file));

            Assert.Equal(0xFFFFFF, screen.DisplayDurationMs);
            Assert.Equal(-2, ((LoadingScreenType6Element) screen.Elements[0]).SignedMedium);
            Assert.Equal(file, screen.Encode().ToArray());
        }

        /// <summary>
        ///     A text element keeps the bytes it was stored as, not the string they decode to.
        /// </summary>
        /// <remarks>
        ///     Decoding is lossy at the edges: <c>Node_Sub46_Sub6.method1546</c> substitutes '?' for
        ///     the five unassigned slots in the 0x80-0x9F band, so those bytes cannot be recovered
        ///     from the text. No string in either supported cache holds a byte above 0x7F - which is
        ///     exactly why nothing but this test defends the choice.
        /// </remarks>
        [Fact]
        public void ATextElementKeepsBytesADecodeToStringWouldLose()
        {
            byte[] raw = { 0x81, 0x8D, 0x41, 0x90, 0x9D, 0x42 };

            byte[] file = Screen(1, 1, writer =>
            {
                writer.WriteByte(7);
                writer.Write(raw, 0, raw.Length);
                writer.WriteByte(0);
                WriteTextTrailer(writer);
            });

            var screen = new LoadingScreenDefinition().Decode(new JagStream(file));
            var text = (LoadingScreenTextElement) screen.Elements[0];

            Assert.Equal(raw, text.TextBytes);

            //Every unassigned slot decodes to the same '?', so the text alone cannot distinguish them.
            Assert.Equal("??A??B", text.Text);
            Assert.Equal(file, screen.Encode().ToArray());
        }

        /// <summary>An empty string is a bare terminator, and stays one.</summary>
        [Fact]
        public void AnEmptyTextElementIsJustTheTerminator()
        {
            byte[] file = Screen(1, 1, writer =>
            {
                writer.WriteByte(7);
                writer.WriteByte(0);
                WriteTextTrailer(writer);
            });

            var screen = new LoadingScreenDefinition().Decode(new JagStream(file));
            var text = (LoadingScreenTextElement) screen.Elements[0];

            Assert.Empty(text.TextBytes);
            Assert.Equal(string.Empty, text.Text);
            Assert.Equal(file, screen.Encode().ToArray());
        }

        /// <summary>Setting the text writes the client's encoding of it back.</summary>
        [Fact]
        public void SettingTheTextReEncodesThroughTheClientsCharacterSet()
        {
            var element = new LoadingScreenTextElement { Text = "fee’s" };

            Assert.Equal(new byte[] { (byte) 'f', (byte) 'e', (byte) 'e', 0x92, (byte) 's' },
                element.TextBytes);
            Assert.Equal("fee’s", element.Text);
        }

        /// <summary>An element type outside the ten the format defines is reported, not skipped.</summary>
        /// <remarks>
        ///     There is no length prefix anywhere in a screen file, so an unrecognised type cannot be
        ///     stepped over - the next byte read would be payload taken for a type index.
        /// </remarks>
        [Fact]
        public void AnUnknownElementTypeThrowsRatherThanDesynchronising()
        {
            /* Laid out here rather than through the Screen helper, which counts the elements by
               walking them with the production decoder and would therefore throw first. */
            var stream = new JagStream();
            stream.WriteMedium(1);
            stream.WriteShort(1);
            stream.WriteByte(1);
            stream.WriteByte(LoadingScreenElement.TypeCount);
            stream.WriteInteger(0);
            byte[] file = stream.Flip().ToArray();

            Assert.Throws<InvalidOperationException>(
                () => new LoadingScreenDefinition().Decode(new JagStream(file)));
        }

        /// <summary>A cloned screen shares no element with the screen it came from.</summary>
        [Fact]
        public void CloningAScreenCopiesItsElements()
        {
            byte[] file = Screen(1, 1, writer => WriteTextElement(writer, "before"));

            var original = new LoadingScreenDefinition().Decode(new JagStream(file));
            LoadingScreenDefinition copy = original.Clone();
            ((LoadingScreenTextElement) copy.Elements[0]).Text = "after";

            Assert.Equal("before", ((LoadingScreenTextElement) original.Elements[0]).Text);
            Assert.Equal("after", ((LoadingScreenTextElement) copy.Elements[0]).Text);
        }

        // ===================================================================
        //  The manifest
        // ===================================================================

        /// <summary>The manifest reads its header and category table in the client's order.</summary>
        [Fact]
        public void TheManifestReadsItsHeaderAndCategoryTableInTheClientsOrder()
        {
            byte[] file = Manifest(3, LoadingScreenElement.ClientTypeVersions, maxCategoryIndex: 5,
                defaultScreenId: -1,
                rows: new[]
                {
                    new LoadingScreenCategory { Index = 4, ShuffleStored = 1, ScreenIds = new[] { 326, 327 } },
                    new LoadingScreenCategory { Index = 0, ShuffleStored = 0, ScreenIds = new[] { 666 } }
                });

            var stream = new JagStream(file);
            var manifest = new LoadingScreenManifest().Decode(stream);

            Assert.Equal(file.Length, stream.Position);
            Assert.Equal(3, manifest.Version);
            Assert.Equal(LoadingScreenElement.ClientTypeVersions, manifest.TypeVersions);
            Assert.Equal(5, manifest.MaxCategoryIndex);
            Assert.Equal(-1, manifest.DefaultScreenId);
            Assert.Equal(2, manifest.Categories.Count);
            Assert.Equal(4, manifest.Categories[0].Index);
            Assert.True(manifest.Categories[0].Shuffles);
            Assert.Equal(new[] { 326, 327 }, manifest.Categories[0].ScreenIds);
            Assert.Equal(new[] { 666 }, manifest.Categories[1].ScreenIds);
            Assert.Equal(file, manifest.Encode().ToArray());
        }

        /// <summary>
        ///     Below version 3 the default-screen field is not in the file at all.
        /// </summary>
        /// <remarks>
        ///     Class282.java:93-97 reads it only when the version is above 2, so a decoder that always
        ///     read it would take a category row's first two bytes as the field and mis-parse every
        ///     row after it. Both supported caches store version 3, so nothing else covers this.
        /// </remarks>
        [Fact]
        public void BelowVersionThreeTheDefaultScreenFieldIsAbsent()
        {
            byte[] file = Manifest(2, LoadingScreenElement.ClientTypeVersions, maxCategoryIndex: 1,
                defaultScreenId: null,
                rows: new[]
                {
                    new LoadingScreenCategory { Index = 1, ShuffleStored = 0, ScreenIds = new[] { 7, 8 } }
                });

            var stream = new JagStream(file);
            var manifest = new LoadingScreenManifest().Decode(stream);

            Assert.Equal(file.Length, stream.Position);
            Assert.Equal(2, manifest.Version);
            Assert.Equal(-1, manifest.DefaultScreenId);
            Assert.Equal(new[] { 7, 8 }, manifest.Categories[0].ScreenIds);
            Assert.Equal(file, manifest.Encode().ToArray());
        }

        /// <summary>
        ///     A default screen id other than -1 is carried, because it changes what the rows mean.
        /// </summary>
        /// <remarks>
        ///     When it is set, Class282.java:110-113 prepends it to every category list and :153 makes
        ///     the shuffle skip slot 0 - so the same category bytes describe different lists. It is -1
        ///     in both supported caches.
        /// </remarks>
        [Fact]
        public void ADefaultScreenIdOtherThanMinusOneSurvives()
        {
            byte[] file = Manifest(3, LoadingScreenElement.ClientTypeVersions, maxCategoryIndex: 0,
                defaultScreenId: 42,
                rows: new[]
                {
                    new LoadingScreenCategory { Index = 0, ShuffleStored = 1, ScreenIds = new[] { 9 } }
                });

            var manifest = new LoadingScreenManifest().Decode(new JagStream(file));

            Assert.Equal(42, manifest.DefaultScreenId);
            Assert.Equal(file, manifest.Encode().ToArray());
        }

        /// <summary>
        ///     The type-version bytes are replayed as stored rather than regenerated.
        /// </summary>
        /// <remarks>
        ///     They are a handshake that fails silently: if the count or any byte disagrees with the
        ///     client's own table, Class282.java:86-89 empties both arrays and no loading screen is
        ///     shown at all. A decoder that rebuilt them from
        ///     <see cref="LoadingScreenElement.ClientTypeVersions"/> would quietly repair a file for
        ///     one build while breaking it for the one it belongs to - and this cache is 639 against a
        ///     637 client, so the two are not guaranteed to agree.
        /// </remarks>
        [Fact]
        public void TypeVersionBytesAreReplayedRatherThanRegenerated()
        {
            int[] disagreeing = { 9, 9, 9 };

            byte[] file = Manifest(3, disagreeing, maxCategoryIndex: 0, defaultScreenId: -1,
                rows: Array.Empty<LoadingScreenCategory>());

            var manifest = new LoadingScreenManifest().Decode(new JagStream(file));

            Assert.Equal(disagreeing, manifest.TypeVersions);
            Assert.NotEqual(LoadingScreenElement.ClientTypeVersions.Length, manifest.TypeVersions.Length);
            Assert.Equal(file, manifest.Encode().ToArray());
        }

        /// <summary>A shuffle byte the client does not recognise keeps its stored value.</summary>
        /// <remarks>
        ///     Class282.java:102 tests it with <c>== 1</c>, so a stored 2 means false; recomputing the
        ///     byte from the bool would write a 0 into a file nobody edited.
        /// </remarks>
        [Fact]
        public void AnUnrecognisedShuffleByteKeepsItsStoredValue()
        {
            byte[] file = Manifest(3, LoadingScreenElement.ClientTypeVersions, maxCategoryIndex: 0,
                defaultScreenId: -1,
                rows: new[]
                {
                    new LoadingScreenCategory { Index = 0, ShuffleStored = 2, ScreenIds = new[] { 1 } }
                });

            var manifest = new LoadingScreenManifest().Decode(new JagStream(file));

            Assert.Equal(2, manifest.Categories[0].ShuffleStored);
            Assert.False(manifest.Categories[0].Shuffles);
            Assert.Equal(file, manifest.Encode().ToArray());
        }

        /// <summary>
        ///     A manifest version the client refuses to parse is carried through a save unchanged.
        /// </summary>
        /// <remarks>
        ///     Class282.java:71 abandons the file above version 3 without reading another byte, so the
        ///     rest has no known shape. Dropping it would silently truncate a file this editor cannot
        ///     read; keeping it is the same defence the reference-table codec makes for the branches
        ///     no shipped table exercises.
        /// </remarks>
        [Fact]
        public void AManifestVersionTheClientRefusesToParseIsCarriedVerbatim()
        {
            byte[] tail = { 1, 2, 3, 4, 5 };
            byte[] file = new byte[tail.Length + 1];
            file[0] = LoadingScreenManifest.MaxParsedVersion + 1;
            Array.Copy(tail, 0, file, 1, tail.Length);

            var manifest = new LoadingScreenManifest().Decode(new JagStream(file));

            Assert.Equal(LoadingScreenManifest.MaxParsedVersion + 1, manifest.Version);
            Assert.Equal(tail, manifest.UnparsedTail);
            Assert.Empty(manifest.Categories);
            Assert.Equal(file, manifest.Encode().ToArray());
        }

        /// <summary>A cloned manifest shares no category row with the manifest it came from.</summary>
        [Fact]
        public void CloningAManifestCopiesItsCategories()
        {
            byte[] file = Manifest(3, LoadingScreenElement.ClientTypeVersions, maxCategoryIndex: 0,
                defaultScreenId: -1,
                rows: new[]
                {
                    new LoadingScreenCategory { Index = 0, ShuffleStored = 0, ScreenIds = new[] { 5 } }
                });

            var original = new LoadingScreenManifest().Decode(new JagStream(file));
            LoadingScreenManifest copy = original.Clone();
            copy.Categories[0].ScreenIds = new[] { 6 };

            Assert.Equal(new[] { 5 }, original.Categories[0].ScreenIds);
            Assert.Equal(new[] { 6 }, copy.Categories[0].ScreenIds);
        }

        // ===================================================================
        //  Laying out bytes the way the client reads them
        // ===================================================================

        /// <summary>Lays out a screen file: unsigned 24-bit duration, unsigned short, element count.</summary>
        /// <param name="durationMs">The 24-bit duration field.</param>
        /// <param name="secondTiming">The unsigned short after it.</param>
        /// <param name="writeElements">Writes each element's type byte and record.</param>
        /// <returns>The file bytes.</returns>
        private byte[] Screen(int durationMs, int secondTiming, Action<JagStream> writeElements)
        {
            var body = new JagStream();
            writeElements(body);
            byte[] elements = body.Flip().ToArray();

            var stream = new JagStream();
            stream.WriteMedium(durationMs);
            stream.WriteShort(secondTiming);
            stream.WriteByte((byte) CountElements(elements));
            stream.Write(elements, 0, elements.Length);

            byte[] file = stream.Flip().ToArray();
            _output.WriteLine($"laid out a {file.Length} byte screen");
            return file;
        }

        /// <summary>
        ///     Counts the elements in a laid-out body by walking it with the production decoder.
        /// </summary>
        /// <remarks>
        ///     The count byte sits before the records, so it cannot be written until they exist. This
        ///     re-reads them rather than making each helper report a count, which would put the
        ///     element widths in the test twice and let the two drift apart.
        /// </remarks>
        /// <param name="elements">The laid-out element records.</param>
        /// <returns>How many elements the body holds.</returns>
        private static int CountElements(byte[] elements)
        {
            var stream = new JagStream(elements);
            int count = 0;

            while (stream.Position < stream.Length)
            {
                LoadingScreenElement element = LoadingScreenElement.Create(stream.ReadUnsignedByte());
                element.Decode(stream);
                count++;
            }

            return count;
        }

        private static void WriteIntegerElement(JagStream writer, int value)
        {
            writer.WriteByte(0);
            writer.WriteInteger(value);
        }

        private static void WriteShortElement(JagStream writer, int typeIndex, int value)
        {
            writer.WriteByte((byte) typeIndex);
            writer.WriteShort(value);
        }

        /// <summary>Writes a type byte, the shared twenty-byte placement, and a type-specific tail.</summary>
        private static void WritePlacedElement(JagStream writer, int typeIndex, Action<JagStream> tail)
        {
            writer.WriteByte((byte) typeIndex);
            writer.WriteByte(1);
            writer.WriteByte(2);
            writer.WriteShort(-3);
            writer.WriteShort(-4);
            writer.WriteShort(5);
            writer.WriteShort(6);
            writer.WriteShort(-7);
            writer.WriteInteger(8);
            writer.WriteInteger(9);
            tail(writer);
        }

        /// <summary>Writes the type-4 record, whose layout is its own rather than the shared one.</summary>
        private static void WriteType4Element(JagStream writer)
        {
            writer.WriteByte(4);
            writer.WriteByte(11);
            writer.WriteByte(1);
            writer.WriteByte(2);
            writer.WriteShort(-13);
            writer.WriteShort(-14);
            writer.WriteShort(15);
            writer.WriteShort(16);
            writer.WriteInteger(17);
            writer.WriteInteger(18);
            writer.WriteInteger(19);
            writer.WriteByte(1);
        }

        /// <summary>Writes a type-5 sprite record, optionally with a type-6 tail after it.</summary>
        private static void WriteSpriteElement(JagStream writer, int typeIndex, int spriteId,
            Action<JagStream> tail)
        {
            writer.WriteByte((byte) typeIndex);
            writer.WriteShort(spriteId);
            writer.WriteByte(0);
            writer.WriteByte(1);
            writer.WriteShort(-20);
            writer.WriteShort(-21);
            tail?.Invoke(writer);
        }

        /// <summary>Writes a type-7 text record.</summary>
        private static void WriteTextElement(JagStream writer, string text)
        {
            writer.WriteByte(7);
            writer.WriteJagexString(text);
            WriteTextTrailer(writer);
        }

        /// <summary>Writes the twenty-five bytes that follow a type-7 element's string.</summary>
        private static void WriteTextTrailer(JagStream writer)
        {
            writer.WriteByte(2);
            writer.WriteByte(1);
            writer.WriteShort(-22);
            writer.WriteShort(-23);
            writer.WriteByte(24);
            writer.WriteByte(25);
            writer.WriteByte(26);
            writer.WriteShort(27);
            writer.WriteShort(28);
            writer.WriteInteger(29);
            writer.WriteInteger(30);
            writer.WriteInteger(31);
        }

        private static void WriteShorts(JagStream writer, params int[] values)
        {
            foreach (int value in values)
                writer.WriteShort(value);
        }

        /// <summary>Lays out a manifest file in the order the client reads it.</summary>
        /// <param name="version">The version byte.</param>
        /// <param name="typeVersions">The per-type version bytes, count included.</param>
        /// <param name="maxCategoryIndex">The highest category slot the client sizes arrays for.</param>
        /// <param name="defaultScreenId">The signed default, or null to omit the field entirely.</param>
        /// <param name="rows">The category rows.</param>
        /// <returns>The file bytes.</returns>
        private static byte[] Manifest(int version, IReadOnlyList<int> typeVersions, int maxCategoryIndex,
            int? defaultScreenId, IReadOnlyList<LoadingScreenCategory> rows)
        {
            var stream = new JagStream();
            stream.WriteByte((byte) version);
            stream.WriteByte((byte) typeVersions.Count);
            foreach (int typeVersion in typeVersions)
                stream.WriteByte((byte) typeVersion);

            stream.WriteByte((byte) rows.Count);
            stream.WriteByte((byte) maxCategoryIndex);

            if (defaultScreenId.HasValue)
                stream.WriteShort(defaultScreenId.Value);

            foreach (LoadingScreenCategory row in rows)
            {
                stream.WriteByte((byte) row.Index);
                stream.WriteByte((byte) row.ShuffleStored);
                stream.WriteShort(row.ScreenIds.Length);
                foreach (int screenId in row.ScreenIds)
                    stream.WriteShort(screenId);
            }

            return stream.Flip().ToArray();
        }
    }
}
