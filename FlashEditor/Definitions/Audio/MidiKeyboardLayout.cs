using System;
using System.Drawing;

namespace FlashEditor.Definitions.Audio {
    /// <summary>
    ///     Where each of a patch's 128 keys sits on a drawn piano keyboard, and which key a point
    ///     falls on.
    /// </summary>
    /// <remarks>
    ///     Separated from the control that paints it so that the geometry can be tested without a
    ///     window. That is not a formality: a piano keyboard is the one layout where "looks about
    ///     right" and "the click lands on the key under the pointer" are different statements, because
    ///     the black keys overlap two white ones and a hit test that checks the white keys first is
    ///     wrong on every black key while looking perfect.
    ///     <para>
    ///     <b>White key edges are computed from the key index rather than from a width.</b> Dividing
    ///     the client area by 75 and multiplying back leaves a remainder, so the last key would stop
    ///     short of the right edge by up to 74 pixels of accumulated rounding, and the gaps between
    ///     keys would be uneven. Taking each edge as <c>i * width / count</c> tiles the whole area
    ///     exactly and spreads the rounding one pixel at a time.
    ///     </para>
    /// </remarks>
    public sealed class MidiKeyboardLayout {
        /// <summary>How tall a black key is as a fraction of the keyboard, as a numerator over 5.</summary>
        /// <remarks>
        ///     Three fifths, which is roughly a real keyboard's proportion. It also has to leave room
        ///     below for the sample band the control draws across the bottom of every sounding key,
        ///     which is the whole reason the keyboard is drawn rather than gridded.
        /// </remarks>
        private const int BlackKeyHeightNumerator = 3;

        private const int BlackKeyHeightDenominator = 5;

        /// <summary>The white key each key is, counted from the left, or -1 for a black key.</summary>
        private static readonly int[] WhiteIndex = BuildWhiteIndex();

        /// <summary>How many white keys the 128 a patch describes contain.</summary>
        /// <remarks>
        ///     Derived rather than written down, because it follows from the pitch classes and a
        ///     literal here would be a second statement of the same fact that could drift from it.
        /// </remarks>
        public static int WhiteKeyCount { get; } = CountWhiteKeys();

        private readonly int width;
        private readonly int height;

        /// <summary>Measures a keyboard into a rectangle.</summary>
        /// <param name="width">The drawable width in pixels.</param>
        /// <param name="height">The drawable height in pixels.</param>
        public MidiKeyboardLayout(int width, int height) {
            this.width = Math.Max(0, width);
            this.height = Math.Max(0, height);
        }

        /// <summary>Whether there is enough room to draw anything at all.</summary>
        /// <remarks>
        ///     A control is laid out at its designer size before the form's first layout pass, and a
        ///     keyboard narrower than its white keys would give every key a zero-width rectangle. The
        ///     caller draws nothing rather than a column of slivers.
        /// </remarks>
        public bool IsDrawable => width >= WhiteKeyCount && height >= BlackKeyHeightDenominator;

        /// <summary>How tall a black key is drawn.</summary>
        public int BlackKeyHeight => height * BlackKeyHeightNumerator / BlackKeyHeightDenominator;

        /// <summary>
        ///     Where a key is drawn.
        /// </summary>
        /// <remarks>
        ///     A black key straddles the boundary between the white key below it and the one above,
        ///     centred on that boundary and about three fifths of a white key wide, which is what
        ///     makes the five-and-two grouping read as a keyboard rather than as stripes.
        /// </remarks>
        /// <param name="key">The key, 0..127.</param>
        /// <returns>The key's rectangle.</returns>
        /// <exception cref="ArgumentOutOfRangeException">The key is outside the keyboard.</exception>
        public Rectangle KeyBounds(int key) {
            Check(key);

            if (WhiteIndex[key] >= 0) {
                int left = WhiteEdge(WhiteIndex[key]);
                return new Rectangle(left, 0, WhiteEdge(WhiteIndex[key] + 1) - left, height);
            }

            //The white key below a black one is always key - 1: no two black keys are adjacent.
            int boundary = WhiteEdge(WhiteIndex[key - 1] + 1);
            int span = WhiteEdge(1) - WhiteEdge(0);
            int blackWidth = Math.Max(1, span * 3 / 5);

            return new Rectangle(boundary - blackWidth / 2, 0, blackWidth, BlackKeyHeight);
        }

        /// <summary>
        ///     Which key a point falls on, or -1 for none.
        /// </summary>
        /// <remarks>
        ///     Black keys are tested first and only within their own height. They are drawn over the
        ///     white keys they straddle, so a white-key-first test hands back the white key underneath
        ///     every black one, and the top two thirds of the keyboard then plays the wrong note.
        /// </remarks>
        /// <param name="x">The point's x, in the keyboard's own coordinates.</param>
        /// <param name="y">The point's y.</param>
        /// <returns>The key, or -1.</returns>
        public int KeyAt(int x, int y) {
            if (!IsDrawable || x < 0 || y < 0 || x >= width || y >= height)
                return -1;

            if (y < BlackKeyHeight)
                for (int key = 0; key < MidiPatchDefinition.Keys; key++)
                    if (WhiteIndex[key] < 0 && KeyBounds(key).Contains(x, y))
                        return key;

            for (int key = 0; key < MidiPatchDefinition.Keys; key++)
                if (WhiteIndex[key] >= 0 && KeyBounds(key).Contains(x, y))
                    return key;

            return -1;
        }

        /// <summary>Whether a key is drawn as a white key.</summary>
        /// <param name="key">The key, 0..127.</param>
        /// <returns>Whether it is white.</returns>
        public static bool IsWhite(int key) {
            Check(key);
            return WhiteIndex[key] >= 0;
        }

        /// <summary>Where the left edge of a white key sits.</summary>
        /// <param name="whiteIndex">The white key's index from the left.</param>
        /// <returns>The x coordinate.</returns>
        private int WhiteEdge(int whiteIndex) {
            return (int) ((long) whiteIndex * width / WhiteKeyCount);
        }

        /// <summary>Numbers the white keys and leaves the black ones at -1.</summary>
        /// <returns>The per-key white index.</returns>
        private static int[] BuildWhiteIndex() {
            var indices = new int[MidiPatchDefinition.Keys];
            int white = 0;

            for (int key = 0; key < MidiPatchDefinition.Keys; key++)
                indices[key] = GeneralMidi.IsWhiteKey(key) ? white++ : -1;

            return indices;
        }

        /// <summary>Counts the white keys from the table that was just built.</summary>
        /// <remarks>
        ///     Runs after <see cref="WhiteIndex"/>, which static field initialisers guarantee by
        ///     running in declaration order. Counted rather than taken from the last entry, so it
        ///     stays right if the keyboard ever ends on a black key.
        /// </remarks>
        /// <returns>The count.</returns>
        private static int CountWhiteKeys() {
            int white = 0;
            foreach (int index in WhiteIndex)
                if (index >= 0)
                    white++;

            return white;
        }

        private static void Check(int key) {
            if (key < 0 || key >= MidiPatchDefinition.Keys)
                throw new ArgumentOutOfRangeException(nameof(key), key, "A patch describes keys 0..127.");
        }
    }
}
