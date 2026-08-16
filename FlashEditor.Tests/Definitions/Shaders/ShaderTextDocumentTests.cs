using System.Text;
using FlashEditor.Definitions.Shaders;
using Xunit;

namespace FlashEditor.Tests.Definitions.Shaders
{
    /// <summary>
    ///     The text codec that stops a shader editor rewriting the lines nobody touched.
    /// </summary>
    /// <remarks>
    ///     Synthetic bytes rather than the cache, because the cases that matter are the ones the
    ///     cache does not hold: a file mixing conventions, a CR-only file, a file with no line break
    ///     at all. The cache's own seven files are swept separately by
    ///     <c>RealCacheShaderTests</c>.
    ///     <para>
    ///     <b>Every test here asserts the same property in a different shape: what comes out is what
    ///     went in.</b> That is deliberate. Round-tripping this encoder against this decoder proves
    ///     nothing on its own, so the assertion is always against the literal bytes the case states,
    ///     never against a re-decode.
    ///     </para>
    /// </remarks>
    public class ShaderTextDocumentTests
    {
        private static byte[] Bytes(string text)
        {
            return Encoding.Latin1.GetBytes(text);
        }

        [Fact]
        public void ABareLfFileIsRecognisedAndWrittenBackAsBareLf()
        {
            byte[] stored = Bytes("!!ARBvp1.0\nPARAM x = 1;\nEND");
            ShaderTextDocument document = ShaderTextDocument.Decode(stored);

            Assert.Equal(ShaderLineEnding.Lf, document.Ending);
            Assert.False(document.EndsWithNewline);
            Assert.True(document.RoundTripsExactly);
            Assert.Null(document.EditRefusal);

            //The display text is CRLF because a text box cannot show anything else, and the encode
            //has to undo exactly that.
            Assert.Equal("!!ARBvp1.0\r\nPARAM x = 1;\r\nEND", document.DisplayText);
            Assert.Equal(stored, document.Encode(document.DisplayText));
        }

        [Fact]
        public void ACrLfFileIsWrittenBackAsCrLf()
        {
            byte[] stored = Bytes("uniform float time;\r\nvoid main() {}\r\n");
            ShaderTextDocument document = ShaderTextDocument.Decode(stored);

            Assert.Equal(ShaderLineEnding.CrLf, document.Ending);
            Assert.True(document.EndsWithNewline);
            Assert.Equal(stored, document.Encode(document.DisplayText));
        }

        /// <summary>
        ///     The trailing newline is part of the file and is neither added nor removed.
        /// </summary>
        /// <remarks>
        ///     Only one of the seven shaders in the cache ends with one, so an editor that appended a
        ///     newline "to be tidy" would rewrite six files, and one that stripped it would rewrite
        ///     the seventh.
        /// </remarks>
        [Theory]
        [InlineData("!!ARBvp1.0\nEND", false)]
        [InlineData("!!ARBvp1.0\nEND\n", true)]
        [InlineData("!!ARBvp1.0\r\nEND", false)]
        [InlineData("!!ARBvp1.0\r\nEND\r\n", true)]
        public void TheTrailingNewlineSurvivesExactly(string text, bool expectedTrailing)
        {
            byte[] stored = Bytes(text);
            ShaderTextDocument document = ShaderTextDocument.Decode(stored);

            Assert.Equal(expectedTrailing, document.EndsWithNewline);
            Assert.True(document.RoundTripsExactly);
            Assert.Equal(stored, document.Encode(document.DisplayText));
        }

        [Fact]
        public void ASingleLineFileWithNoLineBreakRoundTrips()
        {
            byte[] stored = Bytes("!!ARBvp1.0 END");
            ShaderTextDocument document = ShaderTextDocument.Decode(stored);

            Assert.Equal(ShaderLineEnding.None, document.Ending);
            Assert.False(document.EndsWithNewline);
            Assert.True(document.RoundTripsExactly);
            Assert.Equal(stored, document.Encode(document.DisplayText));
        }

        /// <summary>
        ///     A file mixing conventions cannot be reproduced from one terminator, so editing is off.
        /// </summary>
        /// <remarks>
        ///     Refusing is the whole point. The alternative - picking the majority convention and
        ///     writing that - produces a file that compiles, reads correctly and differs from the one
        ///     on disk in lines nobody edited, which is precisely the failure this type exists to
        ///     prevent and is invisible in every view the editor has.
        /// </remarks>
        [Fact]
        public void AFileMixingConventionsIsShownButRefusesToBeEdited()
        {
            byte[] stored = Bytes("one\r\ntwo\nthree");
            ShaderTextDocument document = ShaderTextDocument.Decode(stored);

            Assert.Equal(ShaderLineEnding.Mixed, document.Ending);
            Assert.False(document.RoundTripsExactly);
            Assert.NotNull(document.EditRefusal);
            Assert.Contains("mixes line-ending conventions", document.EditRefusal);
        }

        [Fact]
        public void ACrOnlyFileIsWrittenBackAsCrOnly()
        {
            byte[] stored = Bytes("one\rtwo\rthree");
            ShaderTextDocument document = ShaderTextDocument.Decode(stored);

            Assert.Equal(ShaderLineEnding.Cr, document.Ending);
            Assert.True(document.RoundTripsExactly);
            Assert.Equal(stored, document.Encode(document.DisplayText));
        }

        /// <summary>
        ///     A compiled payload is decoded for display and refuses to be edited.
        /// </summary>
        /// <remarks>
        ///     The dx group is compiled Direct3D bytecode, and the byte that makes it binary is a
        ///     zero rather than anything exotic. Nothing here transcodes it; it is shown as hex and
        ///     can only be replaced.
        /// </remarks>
        [Fact]
        public void ABinaryPayloadRefusesToBeEditedAsText()
        {
            byte[] stored = { 0x01, 0x01, 0xFE, 0xFF, 0x00, 0x43, 0x54, 0x41, 0x42 };
            ShaderTextDocument document = ShaderTextDocument.Decode(stored);

            Assert.False(document.IsText);
            Assert.False(document.RoundTripsExactly);
            Assert.NotNull(document.EditRefusal);
            Assert.Contains("not text", document.EditRefusal);
        }

        /// <summary>
        ///     An edit and its exact reversal land back on the original bytes.
        /// </summary>
        /// <remarks>
        ///     The set-and-unset check every new edit path in this project has to pass. A byte
        ///     identity sweep proves an <i>unedited</i> record re-encodes to what it was read from,
        ///     which is a different claim from "an edit that nets nothing writes nothing" - four real
        ///     defects have lived in that gap.
        /// </remarks>
        [Fact]
        public void AnEditAndItsReversalLandOnTheOriginalBytes()
        {
            byte[] stored = Bytes("!!ARBvp1.0\nMOV result.color, fragment.color;\nEND");
            ShaderTextDocument document = ShaderTextDocument.Decode(stored);

            string edited = document.DisplayText.Replace("MOV", "ABS");
            byte[] afterEdit = document.Encode(edited);

            Assert.NotEqual(stored, afterEdit);
            //Still bare LF: the edit changed the text and not the convention.
            Assert.Equal(ShaderLineEnding.Lf, ShaderTextDocument.Decode(afterEdit).Ending);

            byte[] afterReversal = document.Encode(edited.Replace("ABS", "MOV"));
            Assert.Equal(stored, afterReversal);
        }

        /// <summary>
        ///     Text pasted in another convention is normalised to the file's own, not stored raw.
        /// </summary>
        /// <remarks>
        ///     A text box hands back CRLF for a key press whatever it was shown, and a paste can
        ///     carry anything, so the encode has to normalise its input rather than trust it. Without
        ///     this an editor turns a pure-LF file into a mixed one the moment anything is pasted
        ///     into it - and a mixed file is one this type then refuses to edit at all.
        /// </remarks>
        [Fact]
        public void PastedTextInAnotherConventionIsWrittenInTheFilesOwn()
        {
            byte[] stored = Bytes("one\ntwo");
            ShaderTextDocument document = ShaderTextDocument.Decode(stored);

            byte[] written = document.Encode("one\r\ntwo\rthree\nfour");

            Assert.Equal(Bytes("one\ntwo\nthree\nfour"), written);
            Assert.Equal(ShaderLineEnding.Lf, ShaderTextDocument.Decode(written).Ending);
        }

        /// <summary>
        ///     Every byte value survives the string the editor holds.
        /// </summary>
        /// <remarks>
        ///     Latin-1 is the transfer encoding because it is the one that maps 00-FF onto distinct
        ///     characters and back. UTF-8 would replace anything invalid with U+FFFD on the way in
        ///     and emit three bytes for it on the way out, which is a silent corruption that no view
        ///     in the editor would show.
        /// </remarks>
        [Fact]
        public void EveryByteValueSurvivesTheDisplayEncoding()
        {
            //Every value except the two line breaks, which the display normalisation is entitled to
            //rewrite and which the tests above cover on their own terms. What is left is the claim
            //that matters here: the string the editor holds is a lossless carrier of arbitrary
            //bytes, so nothing outside the edited region can be changed by being displayed.
            var stored = new byte[254];
            int at = 0;
            for (int value = 0; value < 256; value++)
            {
                if (value == 0x0A || value == 0x0D)
                    continue;
                stored[at++] = (byte) value;
            }

            ShaderTextDocument document = ShaderTextDocument.Decode(stored);

            Assert.False(document.IsText);
            Assert.Equal(stored, document.Original);
            Assert.Equal(stored, document.Encode(document.DisplayText));
        }
    }
}
