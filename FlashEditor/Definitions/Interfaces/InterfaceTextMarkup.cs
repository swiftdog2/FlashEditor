using System;
using System.Text;

namespace FlashEditor.Definitions.Interfaces {
    /// <summary>
    ///     Turns the markup interface text carries into the characters a font actually draws.
    /// </summary>
    /// <remarks>
    ///     <b>Interface text is not a plain string, and treating it as one is visibly wrong.</b>
    ///     The client scans for <c>&lt;</c>..<c>&gt;</c> and interprets what is between them
    ///     (<c>RSFont.java:203-266</c> for the scan, <c>:975-1020</c> for the tag table). Rendered
    ///     raw, a component reads "<c>...scenarios:&lt;br&gt;the amount of reward credits...</c>"
    ///     with the tag on screen as text, and every tag also counts toward the measured width - so
    ///     the line is far wider than the client would draw and overruns its box.
    ///     <para>
    ///     <b>What each tag does, from the client:</b>
    ///     </para>
    ///     <list type="bullet">
    ///     <item><c>br</c> - a line break (<c>:996-997</c>).</item>
    ///     <item>
    ///     <c>lt</c>, <c>gt</c>, <c>nbsp</c>, <c>shy</c>, <c>times</c>, <c>euro</c>, <c>copy</c>,
    ///     <c>reg</c> - one literal character each (<c>:214-227</c>). These are the reason a naive
    ///     "strip anything in angle brackets" is wrong: it would delete real text.
    ///     </item>
    ///     <item>
    ///     <c>col=</c>, <c>argb=</c>, <c>str</c>, <c>u=</c>, <c>shad=</c> and their closers - draw
    ///     state, consumed and producing no character.
    ///     </item>
    ///     <item>
    ///     <c>img=N</c> - draws an inline sprite and advances the pen by its width. Consumed here
    ///     and <b>not</b> drawn: the painter has no sprite source, and leaving the tag visible is
    ///     worse than leaving a gap. <see cref="InlineImages"/> reports how many were dropped so a
    ///     surface can say so rather than quietly losing them.
    ///     </item>
    ///     </list>
    ///     <para>
    ///     <b>An unterminated <c>&lt;</c> is literal.</b> The client only treats one as a tag when a
    ///     <c>&gt;</c> follows it, so a stray angle bracket in a sentence stays a character rather
    ///     than swallowing the rest of the line.
    ///     </para>
    /// </remarks>
    public sealed class InterfaceTextMarkup {
        private InterfaceTextMarkup(string text, int lines, int inlineImages) {
            Text = text;
            Lines = lines;
            InlineImages = inlineImages;
        }

        /// <summary>The characters to lay out, with <c>\n</c> where the markup broke a line.</summary>
        public string Text { get; }

        /// <summary>How many lines the text occupies.</summary>
        public int Lines { get; }

        /// <summary>How many inline sprites were consumed and not drawn.</summary>
        public int InlineImages { get; }

        /// <summary>Whether anything was interpreted rather than passed through.</summary>
        public bool HasMarkup { get; private init; }

        /// <summary>
        ///     Reads a stored string into the characters a font draws.
        /// </summary>
        /// <param name="stored">The string as the record holds it.</param>
        /// <returns>The parsed text.</returns>
        public static InterfaceTextMarkup Parse(string? stored) {
            if (string.IsNullOrEmpty(stored))
                return new InterfaceTextMarkup(string.Empty, 1, 0);

            var text = new StringBuilder(stored.Length);
            int lines = 1;
            int images = 0;
            bool sawMarkup = false;

            for (int i = 0; i < stored.Length; i++) {
                char c = stored[i];

                if (c != '<') {
                    if (c == '\n')
                        lines++;

                    text.Append(c);
                    continue;
                }

                int close = stored.IndexOf('>', i + 1);
                if (close < 0) {
                    //No closing bracket, so this is a literal character rather than a tag. The
                    //client does the same: it only acts on '>' while a '<' is open.
                    text.Append(c);
                    continue;
                }

                string tag = stored.Substring(i + 1, close - i - 1);
                i = close;
                sawMarkup = true;

                switch (tag) {
                    case "br":
                        text.Append('\n');
                        lines++;
                        continue;
                    case "lt":
                        text.Append('<');
                        continue;
                    case "gt":
                        text.Append('>');
                        continue;
                    case "nbsp":
                        text.Append(' ');
                        continue;
                    case "shy":
                        text.Append('­');
                        continue;
                    case "times":
                        text.Append('×');
                        continue;
                    case "euro":
                        text.Append('€');
                        continue;
                    case "copy":
                        text.Append('©');
                        continue;
                    case "reg":
                        text.Append('®');
                        continue;
                }

                if (tag.StartsWith("img=", StringComparison.Ordinal)) {
                    images++;
                    continue;
                }

                //Everything else is draw state - colour, underline, shadow, strikethrough - which
                //changes how the following characters look and contributes none of its own.
            }

            return new InterfaceTextMarkup(text.ToString(), lines, images) { HasMarkup = sawMarkup };
        }
    }
}
