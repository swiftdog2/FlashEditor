using FlashEditor.Definitions.Interfaces;
using Xunit;

namespace FlashEditor.Tests.Definitions.Interfaces {
    /// <summary>
    ///     The tag grammar interface text is written in, and the four ways of reading it wrong.
    /// </summary>
    /// <remarks>
    ///     Every expectation here is read off <c>RSFont.java</c> in the 637 client - the scanner at
    ///     <c>:203-266</c> and the tag table at <c>:975-1020</c> - rather than off this parser.
    ///     Round-tripping a parser against itself proves nothing, and the two cases that were
    ///     genuinely wrong before these tests existed
    ///     (<see cref="AnUnterminatedAngleBracketSwallowsTheRestOfTheString"/> and
    ///     <see cref="ASecondAngleBracketRestartsTheTag"/>) both looked entirely reasonable.
    /// </remarks>
    public sealed class InterfaceTextMarkupTests {
        /// <summary>
        ///     Plain text with no tags in it comes back untouched.
        /// </summary>
        [Fact]
        public void TextWithNoTagsIsUnchanged() {
            InterfaceTextMarkup parsed = InterfaceTextMarkup.Parse("Dragontooth Island");

            Assert.Equal("Dragontooth Island", parsed.Text);
            Assert.False(parsed.HasMarkup);
            Assert.Equal(1, parsed.Lines);
            Assert.Equal(0, parsed.InlineImages);
        }

        /// <summary>
        ///     A <c>br</c> tag breaks the line and leaves no characters behind.
        /// </summary>
        /// <remarks>
        ///     This is the whole of the "interface 35 shows a literal &lt;br&gt;" report. Drawn raw
        ///     the four characters appear on screen; measured raw they also widen the line by four
        ///     characters, so the text overran its box for a second, independent reason.
        /// </remarks>
        [Fact]
        public void BrBreaksTheLineAndDrawsNothing() {
            InterfaceTextMarkup parsed = InterfaceTextMarkup.Parse("first<br>second");

            Assert.Equal("first\nsecond", parsed.Text);
            Assert.Equal(2, parsed.Lines);
            Assert.True(parsed.HasMarkup);
        }

        /// <summary>
        ///     The eight substitution tags each produce exactly one character.
        /// </summary>
        /// <remarks>
        ///     <b>The reason "delete anything between angle brackets" is wrong.</b> These carry real
        ///     text, so stripping them silently removes characters the game draws - and the two that
        ///     produce <c>&lt;</c> and <c>&gt;</c> are the ones a stripper would mangle worst,
        ///     because their output is the delimiter itself.
        /// </remarks>
        [Theory]
        [InlineData("<lt>", "<")]
        [InlineData("<gt>", ">")]
        [InlineData("<nbsp>", "\u00a0")]
        [InlineData("<shy>", "\u00ad")]
        [InlineData("<times>", "\u00d7")]
        [InlineData("<euro>", "\u20ac")]
        [InlineData("<copy>", "\u00a9")]
        [InlineData("<reg>", "\u00ae")]
        public void ASubstitutionTagProducesOneCharacter(string stored, string expected) {
            Assert.Equal(expected, InterfaceTextMarkup.Parse(stored).Text);
        }

        /// <summary>
        ///     <c>nbsp</c> is U+00A0 and not a plain space, because the wrapper must not break there.
        /// </summary>
        /// <remarks>
        ///     Separated from the table above because it is the one substitution whose exact code
        ///     point changes layout rather than just glyph choice. Emitting <c>' '</c> would look
        ///     identical on screen until a line happened to need breaking at that point, and would
        ///     then break at the one place the markup exists to forbid.
        /// </remarks>
        [Fact]
        public void NonBreakingSpaceIsNotAPlainSpace() {
            string text = InterfaceTextMarkup.Parse("a<nbsp>b").Text;

            Assert.Equal("a\u00a0b", text);
            Assert.DoesNotContain("\u0020", text);
        }

        /// <summary>
        ///     Styling tags are consumed and contribute no characters.
        /// </summary>
        [Theory]
        [InlineData("<col=ff0000>red</col>", "red")]
        [InlineData("<argb=80ff0000>x</argb>", "x")]
        [InlineData("<u=ffffff>x</u>", "x")]
        [InlineData("<str>x</str>", "x")]
        [InlineData("<shad=000000>x</shad>", "x")]
        [InlineData("<shad=-1>x", "x")]
        public void AStylingTagDrawsNothingOfItsOwn(string stored, string expected) {
            Assert.Equal(expected, InterfaceTextMarkup.Parse(stored).Text);
        }

        /// <summary>
        ///     An inline image is consumed, counted, and reported rather than lost quietly.
        /// </summary>
        /// <remarks>
        ///     The client draws the sprite and advances the pen by its width
        ///     (<c>RSFont.java:258-259</c>). This painter has no sprite source at the point it lays
        ///     text out, so the gap is wrong by the sprite's width - which is why the count is
        ///     surfaced instead of the tag being dropped in silence.
        /// </remarks>
        [Fact]
        public void AnInlineImageIsCountedAndConsumed() {
            InterfaceTextMarkup parsed = InterfaceTextMarkup.Parse("hp<img=3> left<img=4>");

            Assert.Equal("hp left", parsed.Text);
            Assert.Equal(2, parsed.InlineImages);
        }

        /// <summary>
        ///     An unterminated <c>&lt;</c> swallows everything after it, as the client's does.
        /// </summary>
        /// <remarks>
        ///     <b>The friendlier reading is the wrong one.</b> Treating a stray <c>&lt;</c> as a
        ///     literal character is what a reader expects and what the first draft of this parser
        ///     did. The client sets a tag-open position at <c>:205-207</c> and emits a character only
        ///     while none is open (<c>:278</c>), so with no <c>&gt;</c> to close it the scanner never
        ///     emits again. A preview that showed the swallowed text would be showing text the game
        ///     does not draw.
        /// </remarks>
        [Fact]
        public void AnUnterminatedAngleBracketSwallowsTheRestOfTheString() {
            Assert.Equal("keep ", InterfaceTextMarkup.Parse("keep <lose this entirely").Text);
        }

        /// <summary>
        ///     A second <c>&lt;</c> inside an open tag restarts the tag.
        /// </summary>
        /// <remarks>
        ///     The natural implementation - find the next <c>&gt;</c> and take everything between -
        ///     reads <c>&lt;a&lt;br&gt;</c> as the unknown tag <c>a&lt;br</c> and drops it. The
        ///     client's scanner overwrites its tag-open position at every <c>&lt;</c>
        ///     (<c>:206</c>), so the tag is <c>br</c> and the line breaks. Both readings produce no
        ///     visible characters, so only the line count tells them apart.
        /// </remarks>
        [Fact]
        public void ASecondAngleBracketRestartsTheTag() {
            InterfaceTextMarkup parsed = InterfaceTextMarkup.Parse("a<x<br>b");

            Assert.Equal("a\nb", parsed.Text);
            Assert.Equal(2, parsed.Lines);
        }

        /// <summary>
        ///     An unknown tag is consumed rather than drawn.
        /// </summary>
        /// <remarks>
        ///     The client hands anything it does not recognise to <c>parseColor</c> and then
        ///     <c>continue</c>s (<c>RSFont.java:229-264</c>), so an unrecognised tag is silently
        ///     eaten rather than shown. Matching that matters for a preview: showing the tag would
        ///     make an editor look broken on a file the game renders cleanly.
        /// </remarks>
        [Fact]
        public void AnUnknownTagIsConsumed() {
            Assert.Equal("ab", InterfaceTextMarkup.Parse("a<nosuchtag>b").Text);
        }

        /// <summary>
        ///     A null or empty string parses to one empty line rather than throwing.
        /// </summary>
        [Theory]
        [InlineData(null)]
        [InlineData("")]
        public void NothingParsesToOneEmptyLine(string? stored) {
            InterfaceTextMarkup parsed = InterfaceTextMarkup.Parse(stored);

            Assert.Equal(string.Empty, parsed.Text);
            Assert.Equal(1, parsed.Lines);
            Assert.False(parsed.HasMarkup);
        }

        /// <summary>
        ///     A tag that produces a character still counts as markup having been seen.
        /// </summary>
        /// <remarks>
        ///     <see cref="InterfaceTextMarkup.HasMarkup"/> decides whether a surface offers to show
        ///     the stored form beside the rendered one, so it has to mean "this string was
        ///     interpreted" rather than "characters were removed". A caption of <c>10<gt>5</gt></c>
        ///     is interpreted and comes out the same length.
        /// </remarks>
        [Fact]
        public void ASubstitutionCountsAsMarkupEvenThoughNothingIsLost() {
            InterfaceTextMarkup parsed = InterfaceTextMarkup.Parse("10<gt>5");

            Assert.Equal("10>5", parsed.Text);
            Assert.True(parsed.HasMarkup);
        }
    }
}
