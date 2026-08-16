using System.Drawing;
using FlashEditor.Definitions.Audio;
using Xunit;

namespace FlashEditor.Tests.Definitions.Audio
{
    /// <summary>
    ///     The geometry the MIDI patch tab's keyboard is drawn and clicked against.
    /// </summary>
    /// <remarks>
    ///     <b>A keyboard is the one layout where looking right and behaving right are different
    ///     claims.</b> Black keys are drawn over the two white keys they straddle, so a hit test that
    ///     checks the white keys first hands back the white key underneath every black one - and the
    ///     drawing is unaffected, so the top two thirds of the control plays the wrong note while
    ///     looking perfect. Nothing in the suite covers WinForms, and no capture on this machine can
    ///     see what a click did, so this is the only place that claim is checked at all.
    /// </remarks>
    public sealed class MidiKeyboardLayoutTests
    {
        /// <summary>A width and height a real tab gives the control, near enough.</summary>
        private const int Width = 900;

        private const int Height = 140;

        /// <summary>The 128 keys hold 75 white ones, counted rather than stated by the layout.</summary>
        /// <remarks>
        ///     Written out here because it is a property of the pitch classes rather than of the code
        ///     under test: 128 keys is ten octaves and eight semitones, so 10 * 7 white keys plus the
        ///     five white ones in C to G#.
        /// </remarks>
        [Fact]
        public void TheKeyboard_Holds75WhiteKeys()
        {
            Assert.Equal(75, MidiKeyboardLayout.WhiteKeyCount);
        }

        /// <summary>
        ///     The white keys tile the whole width with no gap and no overlap.
        /// </summary>
        /// <remarks>
        ///     The reason edges are taken as <c>i * width / count</c> rather than by multiplying one
        ///     key width back out. 900 divided by 75 happens to be exact; 901 is not, and a layout
        ///     that multiplied would leave the last key 74 pixels short of the right edge on it. Both
        ///     are checked for that reason.
        /// </remarks>
        [Theory]
        [InlineData(900)]
        [InlineData(901)]
        [InlineData(1279)]
        public void WhiteKeys_TileTheFullWidth(int width)
        {
            var layout = new MidiKeyboardLayout(width, Height);
            int expectedLeft = 0;
            int seen = 0;

            for (int key = 0; key < MidiPatchDefinition.Keys; key++)
            {
                if (!MidiKeyboardLayout.IsWhite(key))
                    continue;

                Rectangle bounds = layout.KeyBounds(key);
                Assert.Equal(expectedLeft, bounds.Left);
                Assert.True(bounds.Width > 0, "White key " + key + " has no width at " + width + ".");
                Assert.Equal(Height, bounds.Height);

                expectedLeft = bounds.Right;
                seen++;
            }

            Assert.Equal(MidiKeyboardLayout.WhiteKeyCount, seen);
            Assert.Equal(width, expectedLeft);
        }

        /// <summary>Black keys are shorter than white ones and sit inside the keyboard.</summary>
        [Fact]
        public void BlackKeys_AreShorterAndStayInsideTheKeyboard()
        {
            var layout = new MidiKeyboardLayout(Width, Height);

            for (int key = 0; key < MidiPatchDefinition.Keys; key++)
            {
                if (MidiKeyboardLayout.IsWhite(key))
                    continue;

                Rectangle bounds = layout.KeyBounds(key);
                Assert.Equal(layout.BlackKeyHeight, bounds.Height);
                Assert.True(bounds.Height < Height, "Black key " + key + " is full height.");
                Assert.True(bounds.Left >= 0, "Black key " + key + " starts left of the control.");
                Assert.True(bounds.Right <= Width, "Black key " + key + " runs past the control.");
            }
        }

        /// <summary>
        ///     Every key can be clicked, and clicking it selects that key and no other.
        /// </summary>
        /// <remarks>
        ///     The assertion the whole class exists for. A white key is probed low, below the black
        ///     keys that overlap it, because that is where its own surface is; a black key is probed
        ///     at its own centre. A hit test that checked white keys first passes the first half and
        ///     fails all 53 of the second.
        /// </remarks>
        [Fact]
        public void EveryKey_IsHitAtItsOwnCentre()
        {
            var layout = new MidiKeyboardLayout(Width, Height);

            for (int key = 0; key < MidiPatchDefinition.Keys; key++)
            {
                Rectangle bounds = layout.KeyBounds(key);
                int x = bounds.Left + (bounds.Width / 2);

                //A white key's own surface is the strip below the black keys; its upper half belongs
                //to whichever black keys straddle it.
                int y = MidiKeyboardLayout.IsWhite(key)
                    ? layout.BlackKeyHeight + ((Height - layout.BlackKeyHeight) / 2)
                    : bounds.Top + (bounds.Height / 2);

                Assert.Equal(key, layout.KeyAt(x, y));
            }
        }

        /// <summary>A point on a white key's upper half belongs to the black key drawn over it.</summary>
        /// <remarks>
        ///     Stated separately from the round trip because it is the inverse claim: not only does
        ///     every black key answer at its own centre, the white key underneath does not answer
        ///     there. C#4 is drawn over the boundary between C4 and D4, so a point at its centre and
        ///     near the top must come back as 61 rather than 60 or 62.
        /// </remarks>
        [Fact]
        public void ABlackKey_WinsOverTheWhiteKeysItStraddles()
        {
            var layout = new MidiKeyboardLayout(Width, Height);
            Rectangle sharp = layout.KeyBounds(61);

            int x = sharp.Left + (sharp.Width / 2);
            Assert.Equal(61, layout.KeyAt(x, 1));
            Assert.Equal(61, layout.KeyAt(x, layout.BlackKeyHeight - 1));

            //At the first row below it a white key takes over again, which is the half of the claim
            //a white-key-first hit test would satisfy on its own.
            int below = layout.KeyAt(x, layout.BlackKeyHeight);
            Assert.True(below >= 0, "Nothing was hit just below the black key.");
            Assert.True(GeneralMidi.IsWhiteKey(below), "Key " + below + " below C#4 is not a white key.");
        }

        /// <summary>A point outside the control belongs to no key.</summary>
        [Fact]
        public void APointOutsideTheKeyboard_HitsNothing()
        {
            var layout = new MidiKeyboardLayout(Width, Height);

            Assert.Equal(-1, layout.KeyAt(-1, 10));
            Assert.Equal(-1, layout.KeyAt(10, -1));
            Assert.Equal(-1, layout.KeyAt(Width, 10));
            Assert.Equal(-1, layout.KeyAt(10, Height));
        }

        /// <summary>
        ///     A control too small to draw says so rather than handing back slivers.
        /// </summary>
        /// <remarks>
        ///     A panel is laid out at its designer size before the form's first layout pass, so this
        ///     state occurs on every launch rather than only on a resized window.
        /// </remarks>
        [Fact]
        public void ATooNarrowKeyboard_IsNotDrawable()
        {
            Assert.False(new MidiKeyboardLayout(0, 0).IsDrawable);
            Assert.False(new MidiKeyboardLayout(MidiKeyboardLayout.WhiteKeyCount - 1, Height).IsDrawable);
            Assert.True(new MidiKeyboardLayout(MidiKeyboardLayout.WhiteKeyCount, Height).IsDrawable);
        }
    }
}
