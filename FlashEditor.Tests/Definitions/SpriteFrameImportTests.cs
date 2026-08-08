using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using FlashEditor.cache.sprites;
using FlashEditor.cache.util;
using FlashEditor.Definitions.Sprites;
using Xunit;
using static FlashEditor.Tests.Definitions.SpritePictures;

namespace FlashEditor.Tests.Definitions
{
    /// <summary>
    ///     Pins writing one picture into one frame of an existing sprite set.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///     Nothing in the cache defends this. A byte-identity sweep compares a shipped file against a
    ///     re-encode of itself, and every byte a per-frame import writes is new, so a sweep over all
    ///     4,593 sets would pass with this path broken in any way at all. The claims below are
    ///     therefore made against hand-built sets whose expected bytes follow from the format.
    ///     </para>
    ///     <para>
    ///     <b>The claim that matters is byte identity of the frames nobody edited</b>, and it is
    ///     asserted on the encoded file rather than on the decoded objects: each frame's stored span
    ///     is located from the frame metadata - the planes run from offset 0 in frame order, one
    ///     flags byte each - and compared before and against after. Comparing decoded fields instead
    ///     would let a defect in the traversal write cancel against the matching defect in the read,
    ///     which is the exact shape two real defects on this index already took.
    ///     </para>
    ///     <para>
    ///     The fixture set is built to make the aliased fields live: it holds a palette entry spelled
    ///     0x000000 rather than 0x000001, a frame stored column-major, a frame carrying an alpha plane
    ///     that leaves every pixel opaque, a frame whose flags byte sets a bit the client never reads,
    ///     and pixels addressing the transparent entry. Every one of those is something a re-encode
    ///     driven by pixels would lose.
    ///     </para>
    /// </remarks>
    public class SpriteFrameImportTests
    {
        //The fixture palette. Entry 2 is spelled as a stored black on purpose: the client promotes it
        //to 0x000001 on read, so it draws the same as a stored 1 and a new black pixel has to match it
        //rather than claim an entry of its own.
        private const int Blue = 0x102030;
        private const int StoredBlack = 0x000000;
        private const int Grey = 0x405060;
        private const int White = 0xFFFFFF;

        private const int OpaqueBlue = unchecked((int) 0xFF102030);
        private const int OpaqueGrey = unchecked((int) 0xFF405060);
        private const int OpaqueRed = unchecked((int) 0xFFFF0000);

        // ===================================================================
        //  The frames nobody edited
        // ===================================================================

        /// <summary>
        ///     Replacing one frame leaves every other frame's stored bytes exactly as they were.
        /// </summary>
        /// <remarks>
        ///     The whole point of the feature. The previous import replaced the set, so this is the
        ///     claim that separates the two, and it is asserted on the encoded spans of the frames
        ///     rather than on their decoded fields.
        /// </remarks>
        [Fact]
        public void ReplacingOneFrame_LeavesEveryOtherFrameByteIdentical()
        {
            SpriteDefinition before = FixtureSet();
            byte[] was = before.Encode().ToArray();

            using Bitmap picture = Picture(5, 2,
                OpaqueBlue, OpaqueGrey, OpaqueBlue, OpaqueGrey, OpaqueBlue,
                OpaqueGrey, OpaqueBlue, OpaqueGrey, OpaqueBlue, OpaqueGrey);

            SpriteFrameImport imported = SpriteImageImporter.ReplaceFrame(before, 1, picture);
            byte[] now = imported.Set.Encode().ToArray();

            Assert.Equal(4, imported.Set.GetFrameCount());
            foreach (int untouched in new[] { 0, 2, 3 })
            {
                Assert.Equal(FrameBytes(was, before, untouched), FrameBytes(now, imported.Set, untouched));
            }

            //And the set that was handed in is not the one that changed. The editor keeps the
            //selected row and re-decodes it from the staged bytes, so a mutation here would show up
            //as the grid disagreeing with the cache.
            Assert.Equal(was, before.Encode().ToArray());
        }

        /// <summary>
        ///     A replacement that needs an alpha plane still leaves the frames after it untouched.
        /// </summary>
        /// <remarks>
        ///     The case the simplest possible implementation gets wrong. A plane doubles the frame's
        ///     stored length, so every frame after it moves in the file; "byte-identical" therefore
        ///     has to mean the same bytes at a different offset, not the same bytes at the same one.
        /// </remarks>
        [Fact]
        public void AReplacementThatGrowsTheFrame_MovesTheLaterFramesWithoutChangingThem()
        {
            SpriteDefinition before = FixtureSet();
            byte[] was = before.Encode().ToArray();

            //One partly transparent pixel, which is all it takes to force a plane over the frame.
            using Bitmap picture = Picture(5, 2,
                OpaqueBlue, OpaqueBlue, OpaqueBlue, OpaqueBlue, OpaqueBlue,
                unchecked((int) 0x80102030), OpaqueBlue, OpaqueBlue, OpaqueBlue, OpaqueBlue);

            SpriteFrameImport imported = SpriteImageImporter.ReplaceFrame(before, 1, picture);
            SpriteDefinition after = imported.Set;
            byte[] now = after.Encode().ToArray();

            Assert.True(imported.CarriesAnAlphaPlane);
            Assert.Equal(before.Frames[1].StoredLength + 10, after.Frames[1].StoredLength);
            Assert.Equal(was.Length + 10, now.Length);

            foreach (int untouched in new[] { 0, 2, 3 })
            {
                Assert.Equal(FrameBytes(was, before, untouched), FrameBytes(now, after, untouched));
            }
        }

        /// <summary>
        ///     The stored traversal flag of an untouched frame survives, planes included.
        /// </summary>
        /// <remarks>
        ///     2,767 frames in the shipped data cannot state their order in their own bytes and every
        ///     one of them stores the bit clear, so an encoder that recomputed the flag sweeps both
        ///     caches clean and corrupts the first set edited. That danger is sharper here than for a
        ///     whole-set import, because the frames carried across were read from a file and their
        ///     flags are the file's rather than this code's. Asserted on both the flags byte and the
        ///     order the plane is written in, since only the second would catch a writer that kept the
        ///     flag and ignored it.
        /// </remarks>
        [Theory]
        [InlineData(SpriteSetPalettePolicy.KeepExistingFrames)]
        [InlineData(SpriteSetPalettePolicy.RequantiseWholeSet)]
        public void TheStoredTraversalFlagOfAnUntouchedFrame_Survives(SpriteSetPalettePolicy policy)
        {
            SpriteDefinition before = FixtureSet();
            using Bitmap picture = Flat(5, 2, OpaqueRed);

            SpriteDefinition after = SpriteImageImporter
                .ReplaceFrame(before, 1, picture, SpriteFrameAnchor.KeepOffset, policy).Set;

            //Frame 3 is one pixel wide, so its order cannot be recovered from its bytes at all, and
            //it sets a flag bit the client does not read.
            Assert.True(after.Frames[3].OrderIsUnrecoverable);
            Assert.Equal(before.Frames[3].Flags, after.Frames[3].Flags);
            Assert.True(after.Frames[3].IsColumnMajor);

            //Read back off the file: the flags byte of a frame is the first byte of its span.
            SpriteDefinition read = RoundTrip(after);
            Assert.Equal(before.Frames[3].Flags, read.Frames[3].Flags);
            Assert.Equal(after.Frames[3].PaletteIndices, read.Frames[3].PaletteIndices);
        }

        /// <summary>Every untouched frame keeps its offset and its sub-rectangle.</summary>
        [Theory]
        [InlineData(SpriteSetPalettePolicy.KeepExistingFrames)]
        [InlineData(SpriteSetPalettePolicy.RequantiseWholeSet)]
        public void TheGeometryOfAnUntouchedFrame_Survives(SpriteSetPalettePolicy policy)
        {
            SpriteDefinition before = FixtureSet();
            using Bitmap picture = Flat(5, 2, OpaqueRed);

            SpriteDefinition after = SpriteImageImporter
                .ReplaceFrame(before, 1, picture, SpriteFrameAnchor.KeepOffset, policy).Set;

            Assert.Equal(before.width, after.width);
            Assert.Equal(before.height, after.height);

            foreach (int untouched in new[] { 0, 2, 3 })
            {
                Assert.Equal(before.Frames[untouched].OffsetX, after.Frames[untouched].OffsetX);
                Assert.Equal(before.Frames[untouched].OffsetY, after.Frames[untouched].OffsetY);
                Assert.Equal(before.Frames[untouched].SubWidth, after.Frames[untouched].SubWidth);
                Assert.Equal(before.Frames[untouched].SubHeight, after.Frames[untouched].SubHeight);
            }
        }

        // ===================================================================
        //  The shared palette
        // ===================================================================

        /// <summary>
        ///     A picture whose colours the set already holds moves nothing and approximates nothing.
        /// </summary>
        [Fact]
        public void AReplacementThatFitsTheExistingPalette_ChangesNoPaletteEntryAtAll()
        {
            SpriteDefinition before = FixtureSet();
            using Bitmap picture = Picture(5, 2,
                OpaqueBlue, OpaqueGrey, OpaqueBlue, OpaqueGrey, OpaqueBlue,
                OpaqueGrey, OpaqueBlue, OpaqueGrey, OpaqueBlue, OpaqueGrey);

            SpriteFrameImport imported = SpriteImageImporter.ReplaceFrame(before, 1, picture);

            Assert.Equal(2, imported.SourceColours);
            Assert.Equal(2, imported.PaletteEntriesReused);
            Assert.Equal(0, imported.PaletteEntriesAdded);
            Assert.Equal(0, imported.PaletteEntriesApproximated);
            Assert.Equal(0, imported.WorstChannelError);
            Assert.False(imported.Requantised);
            Assert.Equal(0, imported.FramesRewritten);

            //The palette block is the same bytes, entry for entry, spelling included.
            Assert.Equal(before.PaletteStored, imported.Set.PaletteStored);
        }

        /// <summary>
        ///     A new colour is appended, and no entry that already existed moves.
        /// </summary>
        /// <remarks>
        ///     Appending rather than inserting in colour order is what makes the promise about the
        ///     untouched frames keepable: a plane is a list of entry numbers, so an entry that keeps
        ///     its number leaves every frame referencing it spelling back unchanged. A palette sorted
        ///     after every import would look tidier on screen and rewrite every frame in the set.
        /// </remarks>
        [Fact]
        public void ANewColour_IsAppendedAndMovesNoExistingEntry()
        {
            SpriteDefinition before = FixtureSet();
            byte[] was = before.Encode().ToArray();
            using Bitmap picture = Flat(5, 2, OpaqueRed);

            SpriteFrameImport imported = SpriteImageImporter.ReplaceFrame(before, 1, picture);
            SpriteDefinition after = imported.Set;

            Assert.Equal(1, imported.PaletteEntriesAdded);
            Assert.Equal(0, imported.PaletteEntriesReused);
            Assert.Equal(0, imported.PaletteEntriesApproximated);
            Assert.Equal(before.PaletteStored.Length + 1, after.PaletteStored.Length);

            //Red is 0xFF0000, which sorts below Grey and White. An insert in colour order would put
            //it at entry 2 or 3 and renumber everything after it.
            for (int entry = 0; entry < before.PaletteStored.Length; entry++)
                Assert.Equal(before.PaletteStored[entry], after.PaletteStored[entry]);
            Assert.Equal(0xFF0000, after.PaletteStored[^1]);

            byte[] now = after.Encode().ToArray();
            foreach (int untouched in new[] { 0, 2, 3 })
                Assert.Equal(FrameBytes(was, before, untouched), FrameBytes(now, after, untouched));
        }

        /// <summary>
        ///     A palette with no room left approximates the picture rather than refusing it, and still
        ///     leaves the other frames alone.
        /// </summary>
        /// <remarks>
        ///     This is the outcome the policy buys and the one a user has to be told about: the frame
        ///     being imported takes the whole cost. Refusing instead would make the feature useless on
        ///     exactly the sets that need it most, since a set with a full palette is a set with real
        ///     artwork in it.
        /// </remarks>
        [Fact]
        public void AFullPalette_ApproximatesThePictureAndLeavesTheOtherFramesAlone()
        {
            SpriteDefinition before = SaturatedSet();
            byte[] was = before.Encode().ToArray();

            Assert.Equal(256, before.PaletteStored.Length);

            //White is far outside the lattice the fixture palette is drawn from, so the nearest entry
            //is measurably wrong rather than accidentally right.
            using Bitmap picture = Flat(2, 2, unchecked((int) 0xFFFFFFFF));

            SpriteFrameImport imported = SpriteImageImporter.ReplaceFrame(before, 1, picture);
            SpriteDefinition after = imported.Set;

            Assert.Equal(0, imported.PaletteEntriesAdded);
            Assert.Equal(0, imported.PaletteEntriesReused);
            Assert.Equal(1, imported.PaletteEntriesApproximated);
            Assert.True(imported.WorstChannelError > 0,
                "an approximated colour that reports no error is claiming a lossless mapping");
            Assert.Equal(before.PaletteStored, after.PaletteStored);

            //The reported error bounds the real one, or the report is worse than none.
            int landed = after.PaletteStored[after.Frames[1].PaletteIndices[0]];
            for (int shift = 0; shift <= 16; shift += 8)
            {
                int gap = Math.Abs(0xFF - ((landed >> shift) & 0xFF));
                Assert.True(gap <= imported.WorstChannelError,
                    $"the colour moved {gap} on one channel but the import reported {imported.WorstChannelError}");
            }

            byte[] now = after.Encode().ToArray();
            Assert.Equal(FrameBytes(was, before, 0), FrameBytes(now, after, 0));
        }

        /// <summary>
        ///     Requantising rebuilds the palette across the whole set and re-indexes the frames it has
        ///     to, which is a change to frames nobody edited.
        /// </summary>
        /// <remarks>
        ///     The expected figures are worked out from the fixture rather than read off the result.
        ///     The colours surviving the edit are the near-black drawn by entry 2, the blue of entry 1
        ///     and the white of entry 4; entry 3's grey is referenced only by the frame being
        ///     replaced, so it is dropped. Sorted, the new palette is 0x000001, 0x102030, 0xFF0000,
        ///     0xFFFFFF, so blue moves from entry 1 to 2, the near-black from 2 to 1, and white stays
        ///     at 4. Two of the three untouched frames therefore change and one does not, which is
        ///     also why "requantised" cannot be reported as "every frame rewritten".
        /// </remarks>
        [Fact]
        public void RequantisingTheWholeSet_ReIndexesTheFramesItHasTo_AndSaysHowMany()
        {
            SpriteDefinition before = FixtureSet();
            byte[] was = before.Encode().ToArray();
            using Bitmap picture = Flat(5, 2, OpaqueRed);

            SpriteFrameImport imported = SpriteImageImporter.ReplaceFrame(before, 1, picture,
                SpriteFrameAnchor.KeepOffset, SpriteSetPalettePolicy.RequantiseWholeSet);
            SpriteDefinition after = imported.Set;

            Assert.True(imported.Requantised);
            Assert.Equal(2, imported.FramesRewritten);
            Assert.Equal(new[] { 0, 0x000001, Blue, 0xFF0000, White }, after.PaletteStored);

            byte[] now = after.Encode().ToArray();

            //Frame 2 addresses white, which kept its entry number, so its bytes are unchanged. Frames
            //0 and 3 address entries that moved, so theirs are not - and that difference is the cost
            //the confirmation exists to report.
            Assert.Equal(FrameBytes(was, before, 2), FrameBytes(now, after, 2));
            Assert.NotEqual(FrameBytes(was, before, 0), FrameBytes(now, after, 0));
            Assert.NotEqual(FrameBytes(was, before, 3), FrameBytes(now, after, 3));

            //What has to hold whatever the entry numbers did: every untouched pixel still draws the
            //colour it drew before, transparency included.
            foreach (int untouched in new[] { 0, 2, 3 })
                AssertDrawnColoursUnchanged(before, after, untouched);
        }

        /// <summary>Transparent pixels stay on entry 0 through a requantise.</summary>
        /// <remarks>
        ///     Entry 0 holds no colour, so it cannot be re-indexed onto a nearest match the way a
        ///     colour entry is. A remap that treated it as an ordinary entry would fill every hole in
        ///     the artwork with whatever colour sorted first.
        /// </remarks>
        [Fact]
        public void ARequantiseLeavesEveryTransparentPixelOnEntryZero()
        {
            SpriteDefinition before = FixtureSet();
            using Bitmap picture = Flat(5, 2, OpaqueRed);

            SpriteDefinition after = SpriteImageImporter.ReplaceFrame(before, 1, picture,
                SpriteFrameAnchor.KeepOffset, SpriteSetPalettePolicy.RequantiseWholeSet).Set;

            byte[] wasPlane = before.Frames[0].PaletteIndices;
            byte[] nowPlane = after.Frames[0].PaletteIndices;
            Assert.Contains<byte>(0, wasPlane);
            for (int at = 0; at < wasPlane.Length; at++)
                Assert.Equal(wasPlane[at] == 0, nowPlane[at] == 0);
        }

        /// <summary>
        ///     More colours than the palette has room for are cut down rather than taken in order.
        /// </summary>
        /// <remarks>
        ///     The obvious implementation appends colours until the palette is full, and it is at its
        ///     worst on exactly the picture this feature invites: something with more colours than the
        ///     set has room for. Sorted ascending, the entries would all come from the low end of the
        ///     range - here every colour with a red of 201 would be dropped and approximated against a
        ///     red of 161, a per-channel error of 40 - so the check is that the appended entries reach
        ///     the top of the picture's range and that the reported error is well under that.
        /// </remarks>
        [Fact]
        public void MoreColoursThanTheRoomLeft_AreMedianCutRatherThanTakenInOrder()
        {
            SpriteDefinition before = SmallPaletteSet();
            int[] colours = DistinctColours(336);
            using Bitmap picture = Picture(24, 14, colours);

            SpriteFrameImport imported = SpriteImageImporter.ReplaceFrame(before, 1, picture);
            SpriteDefinition after = imported.Set;

            //Four entries were there, so 252 of the 255 are free and 336 colours do not fit.
            Assert.Equal(336, imported.SourceColours);
            Assert.Equal(256, after.PaletteStored.Length);
            Assert.True(imported.PaletteEntriesApproximated > 0,
                "336 colours cannot fit 252 free entries, so something has to be approximated");

            int highest = 0;
            for (int entry = before.PaletteStored.Length; entry < after.PaletteStored.Length; entry++)
                highest = Math.Max(highest, (after.PaletteStored[entry] >> 16) & 0xFF);

            //Taking the colours in order stops at a red of 161. The lattice reaches 201.
            Assert.True(highest >= 200,
                $"the appended entries reach a red of only {highest}, so they were taken in order");
            Assert.True(imported.WorstChannelError < 40,
                $"a worst channel error of {imported.WorstChannelError} is what taking them in order costs");

            //And the promise about the other frame holds whatever the palette did.
            byte[] was = before.Encode().ToArray();
            byte[] now = after.Encode().ToArray();
            Assert.Equal(FrameBytes(was, before, 0), FrameBytes(now, after, 0));
        }

        // ===================================================================
        //  The black rule
        // ===================================================================

        /// <summary>
        ///     Black matches a palette entry spelled 0x000000, because that is what the entry draws.
        /// </summary>
        /// <remarks>
        ///     A stored zero is promoted to one by the client (<c>Class324.java:76-79</c>), so it
        ///     draws the same near-black a stored 0x000001 does. Matching on the stored spelling
        ///     instead would spend a second entry on a colour the set already has, and on a set with a
        ///     full palette it would approximate black against something that is not black while a
        ///     perfect match sat unused.
        /// </remarks>
        [Fact]
        public void PureBlack_MatchesAnEntrySpelledAsAStoredBlack()
        {
            SpriteDefinition before = FixtureSet();
            using Bitmap picture = Flat(5, 2, unchecked((int) 0xFF000000));

            SpriteFrameImport imported = SpriteImageImporter.ReplaceFrame(before, 1, picture);

            Assert.Equal(1, imported.PaletteEntriesReused);
            Assert.Equal(0, imported.PaletteEntriesAdded);
            Assert.Equal(10, imported.BlackPixels);
            Assert.Equal(before.PaletteStored, imported.Set.PaletteStored);
            Assert.All(imported.Set.Frames[1].PaletteIndices, index => Assert.Equal(2, index));
        }

        /// <summary>An opaque pixel never addresses the transparent entry.</summary>
        /// <remarks>
        ///     Entry 0 is the transparent slot and stores no colour, so an opaque pixel pointed at it
        ///     vanishes where there is no alpha plane and draws black where there is. That is a
        ///     separate rule from the black one: a black pixel gets an entry of its own and draws
        ///     black, and only a fully transparent pixel takes entry 0.
        /// </remarks>
        [Fact]
        public void AnOpaquePixel_NeverAddressesEntryZero()
        {
            SpriteDefinition before = FixtureSet();
            using Bitmap picture = Picture(5, 2,
                unchecked((int) 0xFF000000), OpaqueRed, 0x00000000, OpaqueBlue, unchecked((int) 0xFF000000),
                OpaqueRed, 0x00FFFFFF, OpaqueBlue, unchecked((int) 0xFF000000), OpaqueRed);

            SpriteFrameImport imported = SpriteImageImporter.ReplaceFrame(before, 1, picture);
            byte[] plane = imported.Set.Frames[1].PaletteIndices;

            Assert.Equal(2, imported.TransparentPixels);
            for (int at = 0; at < plane.Length; at++)
            {
                bool clear = at == 2 || at == 6;
                Assert.Equal(clear, plane[at] == 0);
            }
        }

        // ===================================================================
        //  The replaced frame's own stored state
        // ===================================================================

        /// <summary>
        ///     The replacement keeps the displaced frame's traversal order and its unread flag bits.
        /// </summary>
        /// <remarks>
        ///     A picture can state whether it needs an alpha plane and nothing else about the flags
        ///     byte. Rebuilding the byte from the picture would clear the traversal bit of a frame the
        ///     packer stored column-major and drop any bit the client does not read, neither of which
        ///     the user asked for by choosing a PNG.
        /// </remarks>
        [Fact]
        public void TheReplacedFrame_KeepsTheDisplacedFramesTraversalOrderAndUnknownBits()
        {
            SpriteDefinition before = FixtureSet();
            using Bitmap picture = Flat(1, 4, OpaqueBlue);

            //Frame 3 is column-major and sets bit 6, which the client never reads.
            SpriteDefinition after = SpriteImageImporter.ReplaceFrame(before, 3, picture).Set;

            Assert.Equal(before.Frames[3].Flags, after.Frames[3].Flags);
            Assert.True(after.Frames[3].IsColumnMajor);
            Assert.Equal(0x40, after.Frames[3].Flags & 0x40);

            //And the plane really is written in that order, which is the half a flags comparison
            //cannot see. Frame 3 is one pixel wide, so a second, wider case follows below.
            Assert.Equal(after.Frames[3].PaletteIndices, RoundTrip(after).Frames[3].PaletteIndices);
        }

        /// <summary>
        ///     A column-major replacement is written column by column and reads back the same.
        /// </summary>
        /// <remarks>
        ///     Frame 1 of the fixture is 5x2 and column-major, which is wide enough that a plane
        ///     written row by row would read back transposed rather than identical. The picture is
        ///     asymmetric for the same reason.
        /// </remarks>
        [Fact]
        public void AColumnMajorReplacement_IsWrittenInThatOrder()
        {
            SpriteDefinition before = FixtureSet();
            int[] pixels = DistinctColours(10);
            using Bitmap picture = Picture(5, 2, pixels);

            SpriteDefinition after = SpriteImageImporter.ReplaceFrame(before, 1, picture).Set;
            SpriteFrame frame = after.Frames[1];

            Assert.True(frame.IsColumnMajor);

            //Pixel (x, y) sits at x + y * 5 of the canonical plane whichever order the file uses, so
            //this states the picture survived the traversal rather than restating the traversal.
            for (int y = 0; y < 2; y++)
                for (int x = 0; x < 5; x++)
                    Assert.Equal(pixels[x + y * 5] & 0xFFFFFF,
                        after.PaletteStored[frame.PaletteIndices[x + y * 5]]);

            Assert.Equal(frame.PaletteIndices, RoundTrip(after).Frames[1].PaletteIndices);
        }

        /// <summary>
        ///     The alpha bit is the one flag the picture decides, in both directions.
        /// </summary>
        /// <remarks>
        ///     Frame 2 of the fixture carries a plane that leaves every pixel opaque, which is a real
        ///     shape in both caches. Replacing it with a picture that needs no plane must clear the
        ///     bit and drop the plane, or the file states a plane it does not carry and the decoder
        ///     reads the next frame's bytes as this one's alpha.
        /// </remarks>
        [Fact]
        public void TheAlphaBit_FollowsThePictureWhileTheRestOfTheByteDoesNot()
        {
            SpriteDefinition before = FixtureSet();
            Assert.True(before.Frames[2].HasAlphaPlane);

            using Bitmap opaque = Flat(3, 3, OpaqueBlue);
            SpriteDefinition dropped = SpriteImageImporter.ReplaceFrame(before, 2, opaque).Set;

            Assert.False(dropped.Frames[2].HasAlphaPlane);
            Assert.Null(dropped.Frames[2].Alpha);
            Assert.Equal(before.Frames[2].Flags & ~SpriteFrame.FlagAlpha, dropped.Frames[2].Flags);
            Assert.Equal(dropped.Encode().ToArray(), RoundTrip(dropped).Encode().ToArray());

            //And the other way: a frame with no plane gains one when the picture needs it.
            using Bitmap soft = Picture(4, 3,
                OpaqueBlue, OpaqueBlue, OpaqueBlue, OpaqueBlue,
                OpaqueBlue, unchecked((int) 0x40102030), OpaqueBlue, OpaqueBlue,
                OpaqueBlue, OpaqueBlue, OpaqueBlue, OpaqueBlue);
            SpriteDefinition gained = SpriteImageImporter.ReplaceFrame(before, 0, soft).Set;

            Assert.False(before.Frames[0].HasAlphaPlane);
            Assert.True(gained.Frames[0].HasAlphaPlane);
            Assert.Equal(0x40, gained.Frames[0].Alpha![5]);
        }

        // ===================================================================
        //  Placement
        // ===================================================================

        /// <summary>The replacement takes the offset the frame it displaces was stored at.</summary>
        /// <remarks>
        ///     A frame is a sub-rectangle placed within the canvas and routinely does not reach its
        ///     edge, so 0,0 is not a safe default - it would move artwork every time a frame not at
        ///     the origin was replaced.
        /// </remarks>
        [Fact]
        public void KeepOffset_PlacesTheReplacementWhereTheOldFrameWas()
        {
            SpriteDefinition before = FixtureSet();
            using Bitmap picture = Flat(3, 3, OpaqueBlue);

            SpriteFrameImport imported = SpriteImageImporter.ReplaceFrame(before, 2, picture,
                SpriteFrameAnchor.KeepOffset);

            Assert.Equal(new Rectangle(8, 1, 3, 3), imported.Placement);
            Assert.Equal(8, imported.Set.Frames[2].OffsetX);
            Assert.Equal(1, imported.Set.Frames[2].OffsetY);
        }

        /// <summary>The two placements that ignore the old frame put it where they say.</summary>
        [Theory]
        [InlineData(SpriteFrameAnchor.TopLeft, 0, 0)]
        [InlineData(SpriteFrameAnchor.Centre, 6, 4)]
        public void TheStatedPlacements_PutTheFrameWhereTheyClaim(SpriteFrameAnchor anchor, int x, int y)
        {
            //A 16x12 canvas and a 4x4 picture, so centring is (16-4)/2, (12-4)/2.
            SpriteDefinition before = FixtureSet();
            using Bitmap picture = Flat(4, 4, OpaqueBlue);

            SpriteFrameImport imported = SpriteImageImporter.ReplaceFrame(before, 2, picture, anchor);

            Assert.Equal(new Rectangle(x, y, 4, 4), imported.Placement);
            Assert.Equal(x, imported.Set.Frames[2].OffsetX);
            Assert.Equal(y, imported.Set.Frames[2].OffsetY);
        }

        /// <summary>
        ///     A picture that would reach outside the canvas is refused rather than clipped or moved.
        /// </summary>
        /// <remarks>
        ///     The client allocates exactly the canvas and writes at offset plus pixel, so a frame
        ///     past the edge throws in the game. Clipping it would drop artwork silently and moving it
        ///     would relocate artwork silently, so the import stops and names both sizes.
        /// </remarks>
        [Fact]
        public void APictureThatWouldReachOutsideTheCanvas_IsRefused()
        {
            SpriteDefinition before = FixtureSet();
            using Bitmap picture = Flat(15, 2, OpaqueBlue);

            //Frame 1 sits at 2,4 on a 16 wide canvas, so 15 wide overflows by one pixel.
            InvalidOperationException refused = Assert.Throws<InvalidOperationException>(() =>
                SpriteImageImporter.ReplaceFrame(before, 1, picture));

            Assert.Contains("16x12", refused.Message);
            Assert.Contains("15x2", refused.Message);

            //And the same picture is accepted once it is placed somewhere it fits, which is what
            //makes this a refusal to guess rather than a size limit.
            SpriteFrameImport fitted = SpriteImageImporter.ReplaceFrame(before, 1, picture,
                SpriteFrameAnchor.TopLeft);
            Assert.Equal(new Rectangle(0, 0, 15, 2), fitted.Placement);
        }

        /// <summary>A frame the set does not hold is refused before anything is read.</summary>
        [Fact]
        public void ReplaceFrame_RefusesAFrameIdTheSetDoesNotHold()
        {
            SpriteDefinition before = FixtureSet();
            using Bitmap picture = Flat(2, 2, OpaqueBlue);

            Assert.Throws<ArgumentOutOfRangeException>(() =>
                SpriteImageImporter.ReplaceFrame(before, 4, picture));
            Assert.Throws<ArgumentOutOfRangeException>(() =>
                SpriteImageImporter.ReplaceFrame(before, -1, picture));
        }

        // ===================================================================
        //  The rest of the stored form
        // ===================================================================

        /// <summary>
        ///     The unread gap a packer left between the planes and the palette survives the edit.
        /// </summary>
        /// <remarks>
        ///     Thirteen groups in the repack carry three such bytes. Nothing reads them, so dropping
        ///     them would go unnoticed until a set that had one came back three bytes shorter for a
        ///     reason unrelated to the frame that was edited.
        /// </remarks>
        [Fact]
        public void ThePixelPlaneTrailer_SurvivesAPerFrameImport()
        {
            SpriteDefinition before = FixtureSet(new byte[] { 0, 0, 0 });
            using Bitmap picture = Flat(5, 2, OpaqueBlue);

            SpriteDefinition after = SpriteImageImporter.ReplaceFrame(before, 1, picture).Set;

            Assert.Equal(3, after.PixelPlaneTrailer.Length);
            SpriteDefinition read = RoundTrip(after);
            Assert.Equal(3, read.PixelPlaneTrailer.Length);
            Assert.Equal(after.Encode().ToArray(), read.Encode().ToArray());
        }

        /// <summary>Whatever the import writes, the decoder reads back and writes out again unchanged.</summary>
        [Theory]
        [InlineData(SpriteSetPalettePolicy.KeepExistingFrames)]
        [InlineData(SpriteSetPalettePolicy.RequantiseWholeSet)]
        public void APerFrameImport_IsAFixedPointOfTheCodec(SpriteSetPalettePolicy policy)
        {
            SpriteDefinition before = FixtureSet();
            using Bitmap picture = Picture(5, 2, DistinctColours(10));

            SpriteDefinition after = SpriteImageImporter
                .ReplaceFrame(before, 1, picture, SpriteFrameAnchor.KeepOffset, policy).Set;

            byte[] first = after.Encode().ToArray();
            byte[] second = RoundTrip(after).Encode().ToArray();
            Assert.Equal(first, second);
        }

        /// <summary>The same picture into the same set produces the same bytes every time.</summary>
        /// <remarks>
        ///     A palette that depended on a dictionary's enumeration order would turn "did this import
        ///     change anything" into a coin toss, and re-importing an identical file would rewrite the
        ///     group CRC and the reference-table entry of everything packed beside it.
        /// </remarks>
        [Fact]
        public void APerFrameImport_IsDeterministic()
        {
            using Bitmap picture = Picture(5, 2, DistinctColours(10));

            byte[] first = SpriteImageImporter.ReplaceFrame(FixtureSet(), 1, picture).Set.Encode().ToArray();
            byte[] second = SpriteImageImporter.ReplaceFrame(FixtureSet(), 1, picture).Set.Encode().ToArray();

            Assert.Equal(first, second);
        }

        // ===================================================================
        //  Assembling a set from several pictures
        // ===================================================================

        /// <summary>
        ///     Several pictures become one set sharing one palette, each keeping its own geometry.
        /// </summary>
        /// <remarks>
        ///     The other half of what a multi-frame set needs. The canvas is the largest picture in
        ///     each direction so nothing is scaled, and the palette is chosen over all of them at once
        ///     because the format stores exactly one for the set.
        /// </remarks>
        [Fact]
        public void SeveralPictures_BecomeOneSetSharingOnePalette()
        {
            using Bitmap first = Flat(4, 2, OpaqueBlue);
            using Bitmap second = Flat(2, 2, OpaqueRed);
            using Bitmap third = Picture(6, 3, DistinctColours(18));

            SpriteFrameImport imported = SpriteImageImporter.FromImages(new Image[] { first, second, third });
            SpriteDefinition set = imported.Set;

            Assert.Null(imported.ReplacedFrame);
            Assert.Equal(3, set.GetFrameCount());
            Assert.Equal(6, set.width);
            Assert.Equal(3, set.height);

            Assert.Equal(new[] { 4, 2, 6 }, set.Frames.Select(frame => frame.SubWidth).ToArray());
            Assert.Equal(new[] { 2, 2, 3 }, set.Frames.Select(frame => frame.SubHeight).ToArray());
            Assert.All(set.Frames, frame => Assert.Equal(0, frame.OffsetX + frame.OffsetY));

            //Twenty distinct colours across the three pictures, one palette holding all of them.
            Assert.Equal(20, imported.SourceColours);
            Assert.Equal(20, imported.PaletteColours);
            Assert.Equal(Blue, set.PaletteStored[set.Frames[0].PaletteIndices[0]]);
            Assert.Equal(0xFF0000, set.PaletteStored[set.Frames[1].PaletteIndices[0]]);

            Assert.Equal(set.Encode().ToArray(), RoundTrip(set).Encode().ToArray());
        }

        /// <summary>A picture smaller than the shared canvas can be centred on it.</summary>
        [Fact]
        public void SeveralPictures_CanBeCentredOnTheSharedCanvas()
        {
            using Bitmap wide = Flat(6, 4, OpaqueBlue);
            using Bitmap small = Flat(2, 2, OpaqueRed);

            SpriteDefinition set = SpriteImageImporter
                .FromImages(new Image[] { wide, small }, SpriteFrameAnchor.Centre).Set;

            Assert.Equal(0, set.Frames[0].OffsetX);
            Assert.Equal(2, set.Frames[1].OffsetX);
            Assert.Equal(1, set.Frames[1].OffsetY);
        }

        /// <summary>
        ///     Keeping an offset means nothing when there is no frame to keep it from, so it is refused.
        /// </summary>
        [Fact]
        public void FromImages_RefusesToKeepAnOffsetThereIsNoFrameFor()
        {
            using Bitmap picture = Flat(2, 2, OpaqueBlue);

            Assert.Throws<ArgumentException>(() =>
                SpriteImageImporter.FromImages(new Image[] { picture }, SpriteFrameAnchor.KeepOffset));
            Assert.Throws<ArgumentException>(() =>
                SpriteImageImporter.FromImages(Array.Empty<Image>()));
        }

        // ===================================================================
        //  Builders
        // ===================================================================

        /// <summary>
        ///     A four frame set exercising every stored field a re-encode could lose.
        /// </summary>
        /// <remarks>
        ///     Frame 0 is row-major with transparent pixels in it, frame 1 is column-major, frame 2
        ///     carries an alpha plane that leaves everything opaque, and frame 3 is one pixel wide -
        ///     so its traversal order cannot be recovered from its bytes - and sets a flag bit the
        ///     client never reads. Palette entry 2 is spelled as a stored black.
        /// </remarks>
        /// <param name="trailer">The unread gap to store between the planes and the palette.</param>
        /// <returns>The set.</returns>
        private static SpriteDefinition FixtureSet(byte[] trailer = null)
        {
            var frames = new List<SpriteFrame>
            {
                new SpriteFrame
                {
                    OffsetX = 0, OffsetY = 0, SubWidth = 4, SubHeight = 3, Flags = 0,
                    PaletteIndices = new byte[] { 1, 1, 0, 1, 1, 0, 1, 1, 1, 1, 1, 0 }
                },
                new SpriteFrame
                {
                    OffsetX = 2, OffsetY = 4, SubWidth = 5, SubHeight = 2,
                    Flags = SpriteFrame.FlagVertical,
                    PaletteIndices = new byte[] { 3, 3, 3, 3, 3, 3, 3, 3, 3, 3 }
                },
                new SpriteFrame
                {
                    OffsetX = 8, OffsetY = 1, SubWidth = 3, SubHeight = 3,
                    Flags = SpriteFrame.FlagAlpha,
                    PaletteIndices = new byte[] { 4, 4, 4, 4, 4, 4, 4, 4, 4 },
                    Alpha = Enumerable.Repeat((byte) 0xFF, 9).ToArray()
                },
                new SpriteFrame
                {
                    OffsetX = 12, OffsetY = 6, SubWidth = 1, SubHeight = 4,
                    Flags = SpriteFrame.FlagVertical | 0x40,
                    PaletteIndices = new byte[] { 2, 2, 2, 2 }
                }
            };

            return SpriteDefinition.FromFrames(16, 12,
                new[] { 0, Blue, StoredBlack, Grey, White }, frames, trailer);
        }

        /// <summary>
        ///     A two frame set with room in its palette and a frame big enough to hold a picture with
        ///     more colours than that room.
        /// </summary>
        /// <returns>The set.</returns>
        private static SpriteDefinition SmallPaletteSet()
        {
            var frames = new List<SpriteFrame>
            {
                new SpriteFrame
                {
                    OffsetX = 0, OffsetY = 0, SubWidth = 2, SubHeight = 2, Flags = 0,
                    PaletteIndices = new byte[] { 1, 2, 3, 1 }
                },
                new SpriteFrame
                {
                    OffsetX = 0, OffsetY = 0, SubWidth = 24, SubHeight = 14,
                    Flags = SpriteFrame.FlagVertical,
                    PaletteIndices = Enumerable.Repeat((byte) 2, 24 * 14).ToArray()
                }
            };

            return SpriteDefinition.FromFrames(24, 14, new[] { 0, Blue, Grey, White }, frames);
        }

        /// <summary>A two frame set whose palette is full, so nothing can be appended to it.</summary>
        /// <returns>The set.</returns>
        private static SpriteDefinition SaturatedSet()
        {
            int[] palette = new int[256];
            int[] colours = DistinctColours(255);
            for (int entry = 1; entry < 256; entry++)
                palette[entry] = colours[entry - 1] & 0xFFFFFF;

            var frames = new List<SpriteFrame>
            {
                new SpriteFrame
                {
                    OffsetX = 0, OffsetY = 0, SubWidth = 2, SubHeight = 2, Flags = 0,
                    PaletteIndices = new byte[] { 1, 2, 3, 4 }
                },
                new SpriteFrame
                {
                    OffsetX = 4, OffsetY = 4, SubWidth = 2, SubHeight = 2,
                    Flags = SpriteFrame.FlagVertical,
                    PaletteIndices = new byte[] { 5, 6, 7, 8 }
                }
            };

            return SpriteDefinition.FromFrames(8, 8, palette, frames);
        }

        /// <summary>
        ///     The bytes one frame occupies in an encoded set, flags byte and planes included.
        /// </summary>
        /// <remarks>
        ///     The planes run from offset 0 in frame order with no length field of their own, so a
        ///     frame's span is the sum of the lengths before it. Located from the frame metadata
        ///     rather than from the decoder's own recorded position, so the two remain independent
        ///     readings of the same file.
        /// </remarks>
        /// <param name="file">The encoded set.</param>
        /// <param name="set">The set those bytes came from.</param>
        /// <param name="frameId">Which frame to cut out.</param>
        /// <returns>The frame's stored bytes.</returns>
        private static byte[] FrameBytes(byte[] file, SpriteDefinition set, int frameId)
        {
            int at = 0;
            for (int id = 0; id < frameId; id++)
                at += set.Frames[id].StoredLength;

            return file.AsSpan(at, set.Frames[frameId].StoredLength).ToArray();
        }

        /// <summary>
        ///     Asserts a frame draws the same colours after the edit as before it.
        /// </summary>
        /// <remarks>
        ///     The claim that survives a requantise, where the entry numbers move but the picture must
        ///     not. Compared through each set's own palette, promoted the way the client promotes it,
        ///     so a stored black and the 0x000001 it draws as count as agreeing.
        /// </remarks>
        /// <param name="before">The set as it was.</param>
        /// <param name="after">The set as it is.</param>
        /// <param name="frameId">The frame to compare.</param>
        private static void AssertDrawnColoursUnchanged(SpriteDefinition before, SpriteDefinition after, int frameId)
        {
            byte[] was = before.Frames[frameId].PaletteIndices;
            byte[] now = after.Frames[frameId].PaletteIndices;
            Assert.Equal(was.Length, now.Length);

            for (int at = 0; at < was.Length; at++)
            {
                Assert.Equal(before.RenderPalette[was[at]], after.RenderPalette[now[at]]);
            }
        }

        /// <summary>Encodes a set and decodes the result, which is what the editor stages and re-reads.</summary>
        /// <param name="set">The set to write out.</param>
        /// <returns>The set read back off those bytes.</returns>
        private static SpriteDefinition RoundTrip(SpriteDefinition set)
        {
            return SpriteDefinition.DecodeFromStream(new JagStream(set.Encode().ToArray()));
        }
    }
}
