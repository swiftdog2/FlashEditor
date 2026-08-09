using System;
using System.Drawing;
using System.Windows.Forms;
using FlashEditor.UI;
using Xunit;

namespace FlashEditor.Tests.UI {
    /// <summary>
    ///     What can honestly be asserted about the info affordance without a screen.
    /// </summary>
    /// <remarks>
    ///     <b>These tests do not open a popover and do not create a window handle.</b> Nothing in
    ///     this suite covers WinForms, and the capture tool photographs the main window handle so a
    ///     <c>ToolStripDropDown</c> is out of frame by construction - a test that showed one would
    ///     be asserting against a thing no automated check on this machine can see, on a thread
    ///     whose apartment state xunit does not promise.
    ///     <para>
    ///     So the split is deliberate: everything that decides <i>what</i> the control says - the
    ///     kind-to-glyph map, the kind-to-ink map, the wrap width, the break normalisation, the
    ///     accessible text - is pure and is pinned here, and a human is left to judge only whether
    ///     it reads well. The two failures these catch are the ones a human eye cannot: two kinds
    ///     that render identically, so a cost warning reads as ordinary help, and a body that
    ///     measures to a different height than it draws, which clips its last line.
    ///     </para>
    /// </remarks>
    public class InfoAffordanceTests {
        /// <summary>
        ///     One of the CLAUDE.md obligations is about the editor and the other is about a pending
        ///     action, so the two must not arrive on screen as the same mark.
        /// </summary>
        [Theory]
        [InlineData(EditorSurface.Page)]
        [InlineData(EditorSurface.Canvas)]
        public void EveryKindIsDistinguishableFromEveryOther(EditorSurface surface) {
            var kinds = new[] { InfoKind.Help, InfoKind.Limitation, InfoKind.Cost };

            for (int i = 0; i < kinds.Length; i++) {
                for (int j = i + 1; j < kinds.Length; j++) {
                    (EditorIcon Glyph, Color Ink) left =
                        (InfoAffordance.GlyphFor(kinds[i]), InfoAffordance.InkFor(kinds[i], surface));
                    (EditorIcon Glyph, Color Ink) right =
                        (InfoAffordance.GlyphFor(kinds[j]), InfoAffordance.InkFor(kinds[j], surface));

                    Assert.True(left != right,
                        $"{kinds[i]} and {kinds[j]} draw the same glyph in the same ink on {surface}.");
                }
            }
        }

        /// <summary>A cost is the only kind that is about an action, so it is the only warning mark.</summary>
        [Fact]
        public void OnlyACostCarriesTheWarningGlyph() {
            Assert.Equal(EditorIcon.Warning, InfoAffordance.GlyphFor(InfoKind.Cost));
            Assert.Equal(EditorIcon.Info, InfoAffordance.GlyphFor(InfoKind.Help));
            Assert.Equal(EditorIcon.Info, InfoAffordance.GlyphFor(InfoKind.Limitation));
        }

        /// <summary>
        ///     The ink comes out of the theme, so a glyph on a dark canvas is not drawn in the ink
        ///     chosen for a near-white page.
        /// </summary>
        [Theory]
        [InlineData(InfoKind.Help)]
        [InlineData(InfoKind.Limitation)]
        [InlineData(InfoKind.Cost)]
        public void EveryKindIsInkedPerSurface(InfoKind kind) {
            Assert.NotEqual(InfoAffordance.InkFor(kind, EditorSurface.Page),
                InfoAffordance.InkFor(kind, EditorSurface.Canvas));
        }

        /// <summary>Every kind states its own heading, so an unlabelled note is never headless.</summary>
        [Theory]
        [InlineData(InfoKind.Help)]
        [InlineData(InfoKind.Limitation)]
        [InlineData(InfoKind.Cost)]
        public void EveryKindHasACaptionOfItsOwn(InfoKind kind) {
            Assert.False(string.IsNullOrWhiteSpace(InfoAffordance.DefaultCaptionFor(kind)));
        }

        /// <summary>
        ///     The wrap width is measured from the font, so a body wraps rather than running off the
        ///     side the way a tooltip would.
        /// </summary>
        [Fact]
        public void TheColumnIsMeasuredFromTheFontWhenThereIsRoom() {
            using var font = new Font("Consolas", 9F);

            int narrow = InfoAffordance.MeasureColumn(font, 20, int.MaxValue / 4);
            int wide = InfoAffordance.MeasureColumn(font, 92, int.MaxValue / 4);

            Assert.True(narrow > 0);
            Assert.True(wide > narrow,
                $"92 columns measured {wide} and 20 columns measured {narrow}; the count is being ignored.");
        }

        /// <summary>
        ///     A narrow monitor clamps the column rather than opening a popover wider than the
        ///     screen, which is the failure mode a fifteen-line note would hit first.
        /// </summary>
        [Fact]
        public void TheColumnIsClampedToTheAvailableWidth() {
            using var font = new Font("Consolas", 9F);

            const int Available = 400;
            int column = InfoAffordance.MeasureColumn(font, 92, Available);

            Assert.True(column <= Available * 3 / 4,
                $"A 92 column measure produced {column} against {Available} available.");
            Assert.True(column > 0);
        }

        /// <summary>
        ///     The three break spellings the existing notes use all reach the measurer as one, so
        ///     what is measured is what is drawn.
        /// </summary>
        [Fact]
        public void EveryBreakSpellingNormalisesToOne() {
            string mixed = "one\ntwo\r\nthree\rfour";
            string normalised = InfoAffordance.NormaliseBreaks(mixed);

            Assert.Equal(
                "one" + Environment.NewLine + "two" + Environment.NewLine +
                "three" + Environment.NewLine + "four",
                normalised);
        }

        /// <summary>A blank line survives normalisation, because that is how a paragraph breaks.</summary>
        [Fact]
        public void ABlankLineSurvivesNormalisation() {
            string normalised = InfoAffordance.NormaliseBreaks("first\n\nsecond");

            Assert.Equal("first" + Environment.NewLine + Environment.NewLine + "second", normalised);
        }

        /// <summary>The factory wires the three things a caller would otherwise set by hand.</summary>
        [Fact]
        public void TheFactoryWiresTheNoteToItsControl() {
            using var button = new Button { Text = "Import" };
            using InfoAffordance note = InfoAffordance.For(button, InfoKind.Cost, "It restages the record.");

            Assert.Same(button, note.Describes);
            Assert.Equal(InfoKind.Cost, note.Kind);
            Assert.Equal("It restages the record.", note.Body);
            Assert.False(note.IsOpen);
        }

        /// <summary>
        ///     A screen reader announcing "what this edit costs" with no subject says nothing, so the
        ///     described control's caption goes in front of it.
        /// </summary>
        [Fact]
        public void TheAccessibleNameNamesTheControlItDescribes() {
            using var button = new Button { Text = "Import" };
            using InfoAffordance note = InfoAffordance.For(button, InfoKind.Cost, "Every import rewrites the CRC.");

            Assert.Equal("Import: " + InfoAffordance.DefaultCaptionFor(InfoKind.Cost), note.AccessibleName);
            Assert.Equal("Every import rewrites the CRC.", note.AccessibleDescription);
        }

        /// <summary>
        ///     The paragraph is reachable from the accessible tree whether or not the popover is
        ///     ever opened, which is the only route a keyboard-and-reader user has to it.
        /// </summary>
        [Fact]
        public void TheBodyIsCarriedIntoTheAccessibleDescription() {
            using var note = new InfoAffordance();
            Assert.True(string.IsNullOrEmpty(note.AccessibleDescription));

            note.Body = "Playback diverges from the client on purpose.";
            Assert.Equal("Playback diverges from the client on purpose.", note.AccessibleDescription);
        }

        /// <summary>A stated caption beats the kind's own wording in the accessible name too.</summary>
        [Fact]
        public void AStatedCaptionReplacesTheKindsOwn() {
            using var note = new InfoAffordance { Kind = InfoKind.Limitation, Caption = "Not the Map tab" };

            Assert.Equal("Not the Map tab", note.AccessibleName);
        }

        /// <summary>
        ///     A glyph-only note is square and no smaller than an icon, so it lines up with a row of
        ///     text without being clipped.
        /// </summary>
        [Fact]
        public void AGlyphOnlyNoteIsASquareBigEnoughForItsIcon() {
            using var note = new InfoAffordance();
            Size preferred = note.GetPreferredSize(Size.Empty);

            Assert.Equal(preferred.Width, preferred.Height);
            Assert.True(preferred.Width >= EditorTheme.IconSide,
                $"A {preferred.Width}px box cannot hold a {EditorTheme.IconSide}px icon.");
        }

        /// <summary>
        ///     A summary widens the note and never narrows the glyph box, so setting one cannot clip
        ///     the icon it sits beside.
        /// </summary>
        [Fact]
        public void ASummaryWidensTheNoteWithoutShrinkingTheGlyph() {
            using var note = new InfoAffordance();
            Size bare = note.GetPreferredSize(Size.Empty);

            note.Summary = "Read only - not the Map tab";
            Size withSummary = note.GetPreferredSize(Size.Empty);

            Assert.True(withSummary.Width > bare.Width);
            Assert.True(withSummary.Height >= bare.Height);
        }

        /// <summary>
        ///     Nothing states a pixel: the box is derived from the font, so a larger font grows it.
        /// </summary>
        [Fact]
        public void TheGlyphBoxIsDerivedFromTheFont() {
            using var large = new Font("Consolas", 24F);
            using var note = new InfoAffordance { Font = large };

            Assert.True(note.GetPreferredSize(Size.Empty).Height > EditorTheme.IconSide,
                "A 24pt note is still sized as though the font were the default.");
        }

        /// <summary>
        ///     A note with nothing to say does not claim to be open, because an empty popover reads
        ///     as one that failed to load.
        /// </summary>
        [Fact]
        public void ANoteWithNoBodyDoesNotOpen() {
            using var note = new InfoAffordance();

            note.Open();

            Assert.False(note.IsOpen);
        }
    }
}
