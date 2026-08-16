using System;
using System.Text;

namespace FlashEditor.Definitions.Shaders {
    /// <summary>How a stored text file terminates its lines.</summary>
    public enum ShaderLineEnding {
        /// <summary>The file holds no line break at all.</summary>
        None,

        /// <summary>Bare <c>0A</c> throughout, and no <c>0D</c>.</summary>
        Lf,

        /// <summary>Every <c>0A</c> preceded by a <c>0D</c>.</summary>
        CrLf,

        /// <summary>Bare <c>0D</c> throughout, and no <c>0A</c>.</summary>
        Cr,

        /// <summary>More than one convention in one file.</summary>
        Mixed
    }

    /// <summary>
    ///     A stored text payload that can be shown in a text box and written back byte for byte.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///     <b>Line endings are the trap this whole type exists for.</b> Measured across index 31's
    ///     <c>gl</c> group: four of the ARB programs use bare LF and carry no CRLF at all,
    ///     <c>transparent_water</c> uses CRLF, both GLSL files use CRLF, and only one of the seven
    ///     ends with a newline. A <see cref="System.Windows.Forms.TextBox"/> shows and returns CRLF
    ///     whatever it was given, and <c>File.WriteAllText</c> writes the platform's own convention -
    ///     so the obvious implementation rewrites four files the moment they are displayed and saved,
    ///     produces something that still compiles and still reads correctly, and no longer matches
    ///     the bytes nobody edited.
    ///     </para>
    ///     <para>
    ///     <b>So the decode records the convention and the encode replays it</b>, and the claim is
    ///     checked rather than assumed: <see cref="RoundTripsExactly"/> is computed at decode by
    ///     encoding <see cref="DisplayText"/> back and comparing it to the stored bytes. A file whose
    ///     conventions are mixed cannot be reproduced from one terminator, so it fails that check and
    ///     editing is refused with the reason stated - it can still be replaced wholesale from a
    ///     file, which is a byte copy and cannot rewrite anything.
    ///     </para>
    ///     <para>
    ///     Latin-1 is the transfer encoding because it is the one that maps every byte 00-FF onto a
    ///     distinct character and back. UTF-8 would replace anything invalid with U+FFFD on the way
    ///     in and emit three bytes for it on the way out, which is a silent corruption of exactly the
    ///     kind this type is here to prevent.
    ///     </para>
    /// </remarks>
    public sealed class ShaderTextDocument {
        private readonly byte[] original;

        private ShaderTextDocument(byte[] original, string displayText, ShaderLineEnding ending,
            bool endsWithNewline, bool isText) {
            this.original = original;
            DisplayText = displayText;
            Ending = ending;
            EndsWithNewline = endsWithNewline;
            IsText = isText;

            //Checked, not assumed. This is the assertion the whole type rests on and it costs one
            //encode of a file that is already in memory.
            RoundTripsExactly = isText && Encode(displayText).AsSpan().SequenceEqual(original);
        }

        /// <summary>The stored bytes this was decoded from.</summary>
        public byte[] Original => original;

        /// <summary>
        ///     The text with every line break normalised to CRLF, which is what a text box can hold.
        /// </summary>
        /// <remarks>
        ///     Normalised deliberately rather than handed over raw. A multiline
        ///     <see cref="System.Windows.Forms.TextBox"/> given bare LF renders the whole file as one
        ///     line, so showing the stored bytes verbatim is not an option - the answer is to
        ///     normalise for display and replay the recorded convention on the way back, never to
        ///     leave the file in whatever the control produced.
        /// </remarks>
        public string DisplayText { get; }

        /// <summary>Which convention the stored bytes use.</summary>
        public ShaderLineEnding Ending { get; }

        /// <summary>
        ///     Whether the stored bytes end with a line break.
        /// </summary>
        /// <remarks>
        ///     Reported because it is the other half of the trap - one of the seven <c>gl</c> files
        ///     ends with <c>0A</c> and six do not, and an editor that appends a trailing newline
        ///     "to be tidy" rewrites six files. Nothing needs it to encode: splitting keeps the
        ///     trailing empty line and the join puts the terminator back.
        /// </remarks>
        public bool EndsWithNewline { get; }

        /// <summary>Whether every byte is printable ASCII or ordinary whitespace.</summary>
        public bool IsText { get; }

        /// <summary>
        ///     Whether encoding <see cref="DisplayText"/> reproduces the stored bytes exactly.
        /// </summary>
        /// <remarks>
        ///     False for a binary payload and for a text file mixing conventions. It gates editing:
        ///     a document that cannot reproduce what it was given must not be allowed to write.
        /// </remarks>
        public bool RoundTripsExactly { get; }

        /// <summary>
        ///     Why this payload cannot be edited as text, or <c>null</c> when it can.
        /// </summary>
        public string? EditRefusal {
            get {
                if (!IsText)
                    return "This file is not text - it holds bytes outside printable ASCII, so there is nothing" +
                           " to edit. It can still be replaced from a file.";

                if (Ending == ShaderLineEnding.Mixed)
                    return "This file mixes line-ending conventions, so no single terminator reproduces it and" +
                           " any edit would silently rewrite the lines nobody touched. Editing is off; it can" +
                           " still be replaced from a file, which copies bytes.";

                if (!RoundTripsExactly)
                    return "This file does not survive a text round trip byte for byte, so editing it here would" +
                           " rewrite bytes nobody edited. It can still be replaced from a file.";

                return null;
            }
        }

        /// <summary>The line-ending convention in words, for a column.</summary>
        public string EndingText => Ending switch {
            ShaderLineEnding.None => "no line breaks",
            ShaderLineEnding.Lf => "LF",
            ShaderLineEnding.CrLf => "CRLF",
            ShaderLineEnding.Cr => "CR",
            _ => "MIXED"
        };

        /// <summary>Decodes a stored payload for display.</summary>
        /// <param name="stored">The stored bytes.</param>
        /// <returns>The document.</returns>
        public static ShaderTextDocument Decode(byte[] stored) {
            if (stored == null)
                throw new ArgumentNullException(nameof(stored));

            bool isText = LooksLikeText(stored);
            ShaderLineEnding ending = EndingOf(stored);
            bool endsWithNewline = stored.Length > 0 &&
                                   (stored[^1] == (byte) '\n' || stored[^1] == (byte) '\r');

            //Decoded even for a binary payload, so the hex view and the text view share one type and
            //a caller cannot forget to ask which it has. Editing is what is gated, not decoding.
            string text = Encoding.Latin1.GetString(stored);
            string display = string.Join("\r\n", SplitLines(text));

            return new ShaderTextDocument(stored, display, ending, endsWithNewline, isText);
        }

        /// <summary>
        ///     Writes edited text back in the convention the stored file used.
        /// </summary>
        /// <remarks>
        ///     Whatever the caller hands over is normalised first, because a text box returns CRLF
        ///     for a key press and pasted text can carry anything. The terminator then goes back on
        ///     from <see cref="Ending"/>, so a file that arrived as bare LF leaves as bare LF however
        ///     it was displayed.
        /// </remarks>
        /// <param name="editedDisplayText">The edited text, in any convention.</param>
        /// <returns>The bytes to store.</returns>
        public byte[] Encode(string editedDisplayText) {
            if (editedDisplayText == null)
                throw new ArgumentNullException(nameof(editedDisplayText));

            string terminator = Ending switch {
                ShaderLineEnding.Lf => "\n",
                ShaderLineEnding.Cr => "\r",
                //CRLF for a file that had no line break to observe. It cannot matter - a single line
                //has no terminator to write - and picking one keeps the switch total.
                _ => "\r\n"
            };

            return Encoding.Latin1.GetBytes(string.Join(terminator, SplitLines(editedDisplayText)));
        }

        /// <summary>
        ///     Splits on every convention, so the join can put one back.
        /// </summary>
        /// <remarks>
        ///     CRLF is collapsed before the bare CR pass, or every CRLF would produce a spurious
        ///     empty line.
        /// </remarks>
        private static string[] SplitLines(string text) {
            return text.Replace("\r\n", "\n").Replace('\r', '\n').Split('\n');
        }

        /// <summary>
        ///     Which convention a payload uses, counted over the bytes rather than inferred.
        /// </summary>
        private static ShaderLineEnding EndingOf(ReadOnlySpan<byte> bytes) {
            int crlf = 0;
            int bareLf = 0;
            int bareCr = 0;

            for (int i = 0; i < bytes.Length; i++) {
                if (bytes[i] == (byte) '\n') {
                    if (i > 0 && bytes[i - 1] == (byte) '\r')
                        crlf++;
                    else
                        bareLf++;
                }
                else if (bytes[i] == (byte) '\r' && (i + 1 >= bytes.Length || bytes[i + 1] != (byte) '\n')) {
                    bareCr++;
                }
            }

            int conventions = (crlf > 0 ? 1 : 0) + (bareLf > 0 ? 1 : 0) + (bareCr > 0 ? 1 : 0);
            if (conventions > 1)
                return ShaderLineEnding.Mixed;

            if (crlf > 0)
                return ShaderLineEnding.CrLf;
            if (bareLf > 0)
                return ShaderLineEnding.Lf;
            if (bareCr > 0)
                return ShaderLineEnding.Cr;

            return ShaderLineEnding.None;
        }

        /// <summary>
        ///     Whether every byte is printable ASCII, tab, CR or LF.
        /// </summary>
        /// <remarks>
        ///     The discriminator is the payload, never the group id. Index 31's two groups happen to
        ///     split cleanly into one plaintext and one compiled, but a tab that decided from the
        ///     name would be asserting the split rather than measuring it - the same mistake the
        ///     loading-sprites tab exists to correct on index 32.
        /// </remarks>
        private static bool LooksLikeText(ReadOnlySpan<byte> bytes) {
            if (bytes.Length == 0)
                return false;

            foreach (byte value in bytes) {
                if (value == 0x09 || value == 0x0A || value == 0x0D)
                    continue;
                if (value < 0x20 || value > 0x7E)
                    return false;
            }

            return true;
        }
    }
}
