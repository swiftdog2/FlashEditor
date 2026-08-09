using System;
using System.IO;
using System.Linq;
using FlashEditor.Definitions.Animation;
using Xunit;
using FlashEditor.IO;

namespace FlashEditor.Tests.Definitions
{
    /// <summary>
    ///     Pins the index-0 frame codec against bytes it did not produce.
    /// </summary>
    /// <remarks>
    ///     Round-tripping this encoder against this decoder proves nothing, so the two sources here are
    ///     the cache and the client. <see cref="CapturedFrame"/> is index 0 group 2435 verbatim - one of
    ///     the nine single-file groups, so the whole group payload is the frame - and
    ///     <c>RealCacheFrameTests</c> asserts it still is. Everything else is laid out by hand to the
    ///     read order in <c>Class7.java:53-111</c>, and covers three shapes the shipped cache never
    ///     produces: a two-byte smart holding a value the one-byte form could carry, a slot whose flag
    ///     sets only the two-bit field, and a skeleton shorter than the frame that names it.
    /// </remarks>
    public class FrameDefinitionCodecTests
    {
        /// <summary>
        ///     Index 0 group 2435 exactly as the cache stores it: skeleton 2174, 29 slots, 22 values.
        /// </summary>
        /// <remarks>
        ///     Worth pinning rather than a smaller synthetic record because it is the shape the format
        ///     actually takes here: 15 of its 29 slots carry a flag byte of zero and read nothing at
        ///     all, and every one of its 22 values is a two-byte smart. A decoder that dropped the
        ///     zero-flag slots would produce a frame of 14 transforms that re-encodes 15 bytes short
        ///     and re-points every remaining slot onto the wrong bone.
        /// </remarks>
        private static readonly byte[] CapturedFrame =
        {
            0x01,                                //leading byte, read and discarded by the client
            0x08, 0x7E,                          //skeleton group 2174 in index 1
            0x1D,                                //29 transform slots

            //One flag byte per slot. Bits 0/1/2 are x/y/z present; 0x00 is a slot the client skips.
            0x00, 0x02, 0x00, 0x03, 0x00, 0x01, 0x00, 0x03,
            0x00, 0x02, 0x00, 0x03, 0x00, 0x01, 0x00, 0x03,
            0x00, 0x00, 0x06, 0x00, 0x04, 0x00, 0x06, 0x00,
            0x06, 0x00, 0x04, 0x00, 0x06,

            //22 signed smarts, one per set axis bit, in slot order and then x, y, z.
            0xBF, 0x10, 0xC0, 0x50, 0xBF, 0xB0, 0xC0, 0x78,
            0xC0, 0x50, 0xC0, 0x50, 0xC0, 0xF0, 0xBF, 0xB0,
            0xC0, 0x50, 0xBF, 0x88, 0xBF, 0xB0, 0xBF, 0xB0,
            0xBF, 0xB0, 0xC0, 0x50, 0xC0, 0x78, 0xC0, 0x50,
            0xC0, 0x50, 0xC0, 0x50, 0xBF, 0xB0, 0xBF, 0x88,
            0xBF, 0xB0, 0xBF, 0xB0
        };

        /// <summary>The group the captured bytes were read from.</summary>
        public const int CapturedFrameGroupId = 2435;

        /// <summary>The file within that group, which holds one file only.</summary>
        public const int CapturedFrameFileId = 0;

        /// <summary>The skeleton the captured frame names.</summary>
        public const int CapturedFrameSkeletonId = 2174;

        /// <summary>Those bytes, so the cache-backed test can compare without a second copy.</summary>
        /// <returns>A fresh copy of the captured record.</returns>
        public static byte[] CapturedFrameBytes() => (byte[])CapturedFrame.Clone();

        // ===================================================================
        //  The captured record
        // ===================================================================

        /// <summary>
        ///     A real frame decodes into the header, the flag block and the value stream the client
        ///     reads.
        /// </summary>
        /// <remarks>
        ///     This is what settles the two-block layout. The client reads the flags and the values
        ///     through two cursors over the same array (<c>Class7.java:51-59</c>), so every flag byte
        ///     precedes every value byte; a decoder that read a slot's flag and then its values would
        ///     consume the same 77 bytes on a record whose slots each carry one value, and entirely
        ///     different numbers on this one.
        /// </remarks>
        [Fact]
        public void ACapturedFrame_DecodesIntoTheClientsTwoBlockLayout()
        {
            var stream = new JagStream(CapturedFrameBytes());
            var frame = new FrameDefinition { Id = 7 }.Decode(stream);

            Assert.Equal(CapturedFrame.Length, stream.Position);
            Assert.Equal(FrameDefinition.LeadingByteInThisCache, frame.LeadingByte);
            Assert.Equal(CapturedFrameSkeletonId, frame.SkeletonId);
            Assert.Equal(29, frame.TransformCount);
            Assert.Equal(22, frame.StoredValueCount);

            //Fifteen slots are pure padding in the flag block and are kept as slots.
            Assert.Equal(15, frame.Transforms.Count(transform => transform.IsSkipped));

            Assert.False(frame.Transforms[0].HasX);
            Assert.True(frame.Transforms[1].HasY);
            Assert.Equal(-240, frame.Transforms[1].Y.Value);

            Assert.True(frame.Transforms[3].HasX);
            Assert.True(frame.Transforms[3].HasY);
            Assert.False(frame.Transforms[3].HasZ);
            Assert.Equal(80, frame.Transforms[3].X.Value);
            Assert.Equal(-80, frame.Transforms[3].Y.Value);

            //Flag 6 is y and z with no x, which is the case an "x first, always present" reading
            //of the axis bits gets wrong.
            Assert.False(frame.Transforms[18].HasX);
            Assert.Equal(-80, frame.Transforms[18].Y.Value);
            Assert.Equal(80, frame.Transforms[18].Z.Value);

            Assert.Equal(-80, frame.Transforms[28].Y.Value);
            Assert.Equal(-80, frame.Transforms[28].Z.Value);

            //Every value in this record is two-byte, and no two-byte value in the whole index
            //encodes a number the one-byte form could hold.
            Assert.All(frame.Transforms.Where(transform => transform.HasX),
                transform => Assert.Equal(JagStream.SmartWidth.TwoByte, transform.X.Width));
            Assert.DoesNotContain(frame.Transforms.Where(transform => transform.HasX),
                transform => transform.X.Value >= -64 && transform.X.Value <= 63);
        }

        /// <summary>A real frame re-encodes to the bytes it came from.</summary>
        [Fact]
        public void ACapturedFrame_ReEncodesToItsStoredBytes()
        {
            var frame = new FrameDefinition { Id = 7 }.Decode(new JagStream(CapturedFrameBytes()));

            Assert.Equal(CapturedFrameBytes(), frame.Encode().ToArray());
        }

        // ===================================================================
        //  The layout
        // ===================================================================

        /// <summary>
        ///     Every flag byte is read before any value byte.
        /// </summary>
        /// <remarks>
        ///     <c>Class7.java:59</c> parks the value cursor at <c>flags start + slot count</c>, so the
        ///     two blocks are contiguous and separate. Interleaving them reads identically whenever
        ///     every slot carries exactly one value, so the smallest record that tells the two orders
        ///     apart has slots of different widths and is worth stating outright.
        /// </remarks>
        [Fact]
        public void EveryFlagIsReadBeforeAnyValue()
        {
            byte[] record =
            {
                0x01, 0x00, 0x05, 0x02,  //skeleton 5, two slots
                0x01, 0x03,              //flags: x only, then x and y
                0x41, 0x42, 0x43         //values 1, 2, 3 as one-byte smarts
            };

            var stream = new JagStream((byte[])record.Clone());
            var frame = new FrameDefinition { Id = 0 }.Decode(stream);

            Assert.Equal(record.Length, stream.Position);
            Assert.Equal(5, frame.SkeletonId);
            Assert.Equal(1, frame.Transforms[0].X.Value);
            Assert.Equal(2, frame.Transforms[1].X.Value);
            Assert.Equal(3, frame.Transforms[1].Y.Value);
            Assert.Equal(record, frame.Encode().ToArray());
        }

        /// <summary>
        ///     A frame with no slots is four bytes, and survives being one.
        /// </summary>
        /// <remarks>
        ///     1,568 of the shipped 359,931 files are exactly this. A decoder that assumed at least one
        ///     slot, or that sized the value stream from the remaining length, reads off the end of
        ///     them.
        /// </remarks>
        [Fact]
        public void AnEmptyFrame_IsFourBytesAndRoundTrips()
        {
            byte[] record = { 0x01, 0x0C, 0x21, 0x00 };

            var stream = new JagStream((byte[])record.Clone());
            var frame = new FrameDefinition { Id = 0 }.Decode(stream);

            Assert.Equal(4, stream.Position);
            Assert.Equal(0x0C21, frame.SkeletonId);
            Assert.Empty(frame.Transforms);
            Assert.Equal(0, frame.StoredValueCount);
            Assert.Equal(record, frame.Encode().ToArray());
        }

        /// <summary>
        ///     A flag byte that sets only the two-bit field reads no values and is not a skipped slot.
        /// </summary>
        /// <remarks>
        ///     <c>Class7.java:66</c> gates on <c>flag &gt; 0</c>, not on the axis bits, so a flag of 8
        ///     records a pose with all three axes defaulted while consuming nothing from the value
        ///     stream. Treating "reads no values" and "is skipped" as the same thing gets that slot's
        ///     pose wrong without changing a single byte, so no byte-identity sweep can see it.
        /// </remarks>
        [Fact]
        public void ASlotWithOnlyTheTwoBitFieldSet_ReadsNoValuesAndIsNotSkipped()
        {
            byte[] record =
            {
                0x01, 0x00, 0x00, 0x02,
                0x18, 0x00,              //bits 3 and 4 set, then a genuinely empty slot
            };

            var stream = new JagStream((byte[])record.Clone());
            var frame = new FrameDefinition { Id = 0 }.Decode(stream);

            Assert.Equal(record.Length, stream.Position);
            Assert.Equal(0, frame.StoredValueCount);

            Assert.False(frame.Transforms[0].IsSkipped);
            Assert.Equal(3, frame.Transforms[0].SubType);
            Assert.False(frame.Transforms[0].HasX);

            Assert.True(frame.Transforms[1].IsSkipped);
            Assert.Equal(0, frame.Transforms[1].SubType);

            //The skipped slot still gets a pose-less flag byte back.
            Assert.Equal(record, frame.Encode().ToArray());

            //And only the non-skipped one becomes a pose.
            ResolvedFrame resolved = frame.Resolve(new[] { 1, 1 });
            Assert.Single(resolved.Poses);
            Assert.Equal(0, resolved.Poses[0].Slot);
            Assert.Equal(3, resolved.Poses[0].SubType);
        }

        /// <summary>
        ///     Bits the client reads nothing out of survive a round trip.
        /// </summary>
        /// <remarks>
        ///     Only bits 0-4 of the flag byte are read anywhere in <c>Class7</c>. An encoder that
        ///     rebuilt the byte from the axis bits and the two-bit field would silently drop bits 5-7,
        ///     which is a rewrite of a file nobody edited and so a changed archive CRC.
        /// </remarks>
        [Fact]
        public void UnreadFlagBits_SurviveTheRoundTrip()
        {
            byte[] record =
            {
                0x01, 0x00, 0x00, 0x01,
                0xE1,        //bits 5, 6 and 7 set on top of an x value
                0x41
            };

            var frame = new FrameDefinition { Id = 0 }.Decode(new JagStream((byte[])record.Clone()));

            Assert.Equal(0xE1, frame.Transforms[0].Flag);
            Assert.True(frame.Transforms[0].HasX);
            Assert.Equal(0, frame.Transforms[0].SubType);
            Assert.Equal(record, frame.Encode().ToArray());
        }

        // ===================================================================
        //  The non-canonical smart
        // ===================================================================

        /// <summary>
        ///     A value stored in the wider of its two legal forms is written back in that form.
        /// </summary>
        /// <remarks>
        ///     The signed smart can carry -64 to 63 in either width, so the decoded number does not
        ///     determine the bytes. This cache happens to be canonical - of its 11,871,643 two-byte
        ///     values not one holds a number the one-byte form could have - so a shortest-form encoder
        ///     reproduces it and no sweep over shipped data can tell the two encoders apart. That makes
        ///     this synthetic record the only thing standing between the codec and a repack that widens
        ///     a value, which would come back a byte shorter and shift every value after it.
        /// </remarks>
        [Fact]
        public void AWidenedValue_IsWrittenBackInTheWidthItWasReadIn()
        {
            //0xC000 is zero written in the two-byte form; 0x40 is zero written in the one-byte form.
            byte[] widened = { 0x01, 0x00, 0x00, 0x01, 0x01, 0xC0, 0x00 };

            var stream = new JagStream((byte[])widened.Clone());
            var frame = new FrameDefinition { Id = 0 }.Decode(stream);

            Assert.Equal(widened.Length, stream.Position);
            Assert.Equal(0, frame.Transforms[0].X.Value);
            Assert.Equal(JagStream.SmartWidth.TwoByte, frame.Transforms[0].X.Width);
            Assert.Equal(widened, frame.Encode().ToArray());

            //Dropping the recorded width is what an edit does, and then the encoder picks.
            frame.Transforms[0].X = new FrameValue(0);
            Assert.Equal(new byte[] { 0x01, 0x00, 0x00, 0x01, 0x01, 0x40 }, frame.Encode().ToArray());
        }

        /// <summary>
        ///     The one-byte form is read as itself, so the two forms are distinguishable both ways.
        /// </summary>
        [Fact]
        public void ANarrowValue_KeepsTheOneByteForm()
        {
            byte[] narrow = { 0x01, 0x00, 0x00, 0x01, 0x01, 0x00 };

            var frame = new FrameDefinition { Id = 0 }.Decode(new JagStream((byte[])narrow.Clone()));

            Assert.Equal(-64, frame.Transforms[0].X.Value);
            Assert.Equal(JagStream.SmartWidth.OneByte, frame.Transforms[0].X.Width);
            Assert.Equal(narrow, frame.Encode().ToArray());
        }

        // ===================================================================
        //  Resolving against a skeleton
        // ===================================================================

        /// <summary>
        ///     Types 3 and 10 fill a missing axis with 128, and every other type with 0.
        /// </summary>
        /// <remarks>
        ///     <c>Class7.java:72-74</c>. The stored record is identical either way - a missing axis
        ///     costs no bytes - so this is invisible to the encoder and decides what the animation
        ///     looks like.
        /// </remarks>
        [Fact]
        public void Resolve_DefaultsAMissingAxisFromTheTransformType()
        {
            //Four slots, each storing only x, against types 3, 10, 1 and 0.
            var frame = FrameOf(0, new[] { 0x01, 0x01, 0x01, 0x01 }, new[] { 7, 7, 7, 7 });

            ResolvedFrame resolved = frame.Resolve(new[] { 3, 10, 1, 0 });

            Assert.Equal(4, resolved.Poses.Count);
            Assert.Equal(new[] { 7, 128, 128 }, Axes(resolved.Poses[0]));
            Assert.Equal(new[] { 7, 128, 128 }, Axes(resolved.Poses[1]));
            Assert.Equal(new[] { 7, 0, 0 }, Axes(resolved.Poses[2]));
            Assert.Equal(new[] { 7, 0, 0 }, Axes(resolved.Poses[3]));
        }

        /// <summary>
        ///     Types 2 and 9 rescale every axis into a 14-bit angle, defaults included.
        /// </summary>
        /// <remarks>
        ///     <c>Class7.java:91-95</c> is <c>value &lt;&lt; 2 &amp; 0x3fff</c> and runs after the
        ///     defaults, so a missing axis on a type 2 resolves to 0 by way of <c>0 &lt;&lt; 2</c>, and
        ///     a stored -1 wraps to 16380 rather than staying negative.
        /// </remarks>
        [Fact]
        public void Resolve_RescalesTypes2And9IntoAFourteenBitAngle()
        {
            var frame = FrameOf(0, new[] { 0x01, 0x01 }, new[] { -1, 100 });

            ResolvedFrame resolved = frame.Resolve(new[] { 2, 9 });

            Assert.Equal(new[] { 0x3FFC, 0, 0 }, Axes(resolved.Poses[0]));
            Assert.Equal(new[] { 400, 0, 0 }, Axes(resolved.Poses[1]));

            //Type 9 is also one of the three that widen the model-build flags; type 2 is not.
            Assert.Equal(ResolvedFrame.ModelFlagFromWideTypes, resolved.ModelBuildFlags);
        }

        /// <summary>
        ///     A pivot is claimed by the first type 1, 2 or 3 slot after it, and by that one only.
        /// </summary>
        /// <remarks>
        ///     <c>Class7.java:96-101</c> compares the most recent type-0 slot against the highest one
        ///     already claimed and moves the second forward when it takes one, so the second consumer
        ///     of the same pivot gets -1. That cannot be reconstructed by scanning backwards after the
        ///     fact, which is why the resolution is a single forward pass.
        /// </remarks>
        [Fact]
        public void Resolve_ClaimsEachPivotOnce()
        {
            //Slot 0 and slot 3 are pivots with no flag of their own, so neither consumes itself.
            var frame = FrameOf(0, new[] { 0x00, 0x01, 0x01, 0x00, 0x01 }, new[] { 1, 2, 3 });

            ResolvedFrame resolved = frame.Resolve(new[] { 0, 1, 1, 0, 1 });

            Assert.Equal(new[] { 1, 2, 4 }, resolved.Poses.Select(pose => pose.Slot).ToArray());
            Assert.Equal(0, resolved.Poses[0].PivotSlot);

            //The second claimant of the same pivot gets none.
            Assert.Equal(-1, resolved.Poses[1].PivotSlot);

            //A later type-0 slot re-arms it.
            Assert.Equal(3, resolved.Poses[2].PivotSlot);
        }

        /// <summary>
        ///     A type-0 slot that carries a flag of its own consumes its own pivot.
        /// </summary>
        /// <remarks>
        ///     <c>Class7.java:67-69</c> sets the claimed marker to the slot itself, so the following
        ///     type 1 finds nothing unclaimed. The distinction between "is a pivot" and "has been
        ///     claimed" is the whole of this arm.
        /// </remarks>
        [Fact]
        public void Resolve_LetsAFlaggedPivotConsumeItself()
        {
            var frame = FrameOf(0, new[] { 0x01, 0x01 }, new[] { 5, 6 });

            ResolvedFrame resolved = frame.Resolve(new[] { 0, 1 });

            Assert.Equal(-1, resolved.Poses[0].PivotSlot);
            Assert.Equal(-1, resolved.Poses[1].PivotSlot);
        }

        /// <summary>
        ///     The three model-build bits come from the transform types the frame actually touches.
        /// </summary>
        /// <remarks>
        ///     <c>Class7.java:102-108</c> sets three booleans, which
        ///     <c>Node_Sub46_Sub16.method1615/1617/1619</c> hand back and <c>Class97.java:143-151</c>
        ///     turns into 0x400, 0x100 and 0x80. A skipped slot sets none of them, because the whole
        ///     body is inside the <c>flag &gt; 0</c> gate.
        /// </remarks>
        [Fact]
        public void Resolve_CollectsTheModelBuildFlagsFromTheTypesItTouches()
        {
            var frame = FrameOf(0, new[] { 0x01, 0x01, 0x01, 0x00 }, new[] { 1, 2, 3 });

            ResolvedFrame resolved = frame.Resolve(new[] { 5, 7, 8, 5 });

            Assert.Equal(
                ResolvedFrame.ModelFlagFromType5 | ResolvedFrame.ModelFlagFromType7 |
                ResolvedFrame.ModelFlagFromWideTypes,
                resolved.ModelBuildFlags);

            //Slot 3 is a type 5 the client never reaches, and contributes nothing - a frame whose
            //only type-5 slot is skipped sets no bit at all.
            Assert.Equal(3, resolved.Poses.Count);

            ResolvedFrame allSkipped = FrameOf(0, new[] { 0x00 }, Array.Empty<int>()).Resolve(new[] { 5 });
            Assert.Empty(allSkipped.Poses);
            Assert.Equal(0, allSkipped.ModelBuildFlags);
        }

        /// <summary>
        ///     A frame with more slots than its skeleton has bones is refused rather than emptied.
        /// </summary>
        /// <remarks>
        ///     The client indexes the bone array with the loop counter and has no bound check
        ///     (<c>Class7.java:61</c>); the resulting exception is swallowed by its own catch block
        ///     (<c>:130-134</c>), which leaves a frame of zero poses that renders as no animation at
        ///     all. Reproducing that silence would hide a mismatched skeleton behind a still model.
        /// </remarks>
        [Fact]
        public void Resolve_RefusesASkeletonShorterThanTheFrame()
        {
            var frame = FrameOf(0, new[] { 0x01, 0x01 }, new[] { 1, 2 });

            Assert.Throws<ArgumentException>(() => frame.Resolve(new[] { 1 }));

            //A longer skeleton is fine - a frame need not touch every bone.
            Assert.Equal(2, frame.Resolve(new[] { 1, 1, 1, 1 }).Poses.Count);
        }

        // ===================================================================
        //  Refusals
        // ===================================================================

        /// <summary>A record that ends inside a field is refused, not padded.</summary>
        /// <remarks>
        ///     Three places it can end, and all three have to fail: inside the flag block, exactly on
        ///     the boundary of a value the flags promised, and inside the second byte of a two-byte
        ///     smart. The middle one is the one a length-driven reader gets wrong, because the buffer
        ///     ends where a well-formed record also would.
        /// </remarks>
        [Theory]
        [InlineData(new byte[] { 0x01, 0x00, 0x00, 0x02, 0x01 })]              //one flag of two
        [InlineData(new byte[] { 0x01, 0x00, 0x00, 0x01, 0x01 })]              //no value at all
        [InlineData(new byte[] { 0x01, 0x00, 0x00, 0x01, 0x01, 0xC0 })]        //half a two-byte smart
        [InlineData(new byte[] { 0x01, 0x00, 0x00 })]                          //no slot count
        public void ATruncatedRecord_Throws(byte[] truncated)
        {
            Assert.Throws<EndOfStreamException>(
                () => new FrameDefinition { Id = 0 }.Decode(new JagStream(truncated)));
        }

        /// <summary>
        ///     An edit the format cannot store is refused rather than truncated.
        /// </summary>
        /// <remarks>
        ///     Each of these writes a well-formed file holding the wrong thing if it is masked instead.
        ///     A slot count of 256 masks to 0, which turns the entire flag block and value stream into
        ///     bytes past the end of a four-byte frame; a flag byte of 256 masks to 0, which silently
        ///     unbinds three values from their slot and desynchronises the value stream for the rest of
        ///     the record.
        /// </remarks>
        [Fact]
        public void Encode_RefusesAValueTooWideForItsField()
        {
            var frame = new FrameDefinition { Id = 11 };
            frame.Transforms.Add(new FrameTransform { Flag = 0x01, X = new FrameValue(1) });

            frame.LeadingByte = 256;
            Assert.Throws<InvalidOperationException>(() => frame.Encode());
            frame.LeadingByte = FrameDefinition.LeadingByteInThisCache;

            frame.SkeletonId = FrameDefinition.MaxSkeletonId + 1;
            Assert.Throws<InvalidOperationException>(() => frame.Encode());
            frame.SkeletonId = 0;

            frame.Transforms[0].Flag = FrameTransform.MaxFlag + 1;
            Assert.Throws<InvalidOperationException>(() => frame.Encode());
            frame.Transforms[0].Flag = 0x01;

            while (frame.Transforms.Count <= FrameDefinition.MaxTransforms)
                frame.Transforms.Add(new FrameTransform());
            Assert.Throws<InvalidOperationException>(() => frame.Encode());
        }

        /// <summary>
        ///     A value edited past the width it was read in is refused, not quietly widened.
        /// </summary>
        /// <remarks>
        ///     Widening it would be the friendlier answer and the wrong one: the recorded width is
        ///     replayed precisely so an untouched frame is byte-identical, and a field that changes
        ///     length shifts every value after it. Clearing the width is the caller's way of saying the
        ///     value has genuinely been edited.
        /// </remarks>
        [Fact]
        public void Encode_RefusesAValueThatNoLongerFitsItsRecordedWidth()
        {
            var frame = new FrameDefinition { Id = 11, SkeletonId = 1 };
            frame.Transforms.Add(new FrameTransform
            {
                Flag = 0x01,
                X = new FrameValue(4000, JagStream.SmartWidth.OneByte)
            });

            Assert.Throws<InvalidOperationException>(() => frame.Encode());

            //4 header bytes, one flag byte and the two-byte form the value needs.
            frame.Transforms[0].X = new FrameValue(4000);
            Assert.Equal(7, frame.Encode().ToArray().Length);
        }

        /// <summary>A frame at the format's ceilings encodes and reads back unchanged.</summary>
        /// <remarks>
        ///     255 slots occurs in this cache. 255 slots each carrying all three axes does not, and
        ///     this is the only thing that covers the longest record the format can express.
        /// </remarks>
        [Fact]
        public void AFrameAtTheFormatsCeilings_RoundTrips()
        {
            var frame = new FrameDefinition { Id = 0, SkeletonId = FrameDefinition.MaxSkeletonId };
            for (int slot = 0; slot < FrameDefinition.MaxTransforms; slot++)
            {
                frame.Transforms.Add(new FrameTransform
                {
                    Flag = FrameTransform.MaxFlag,
                    X = new FrameValue(-16384),
                    Y = new FrameValue(16383),
                    Z = new FrameValue(0)
                });
            }

            byte[] encoded = frame.Encode().ToArray();
            var stream = new JagStream(encoded);
            var reread = new FrameDefinition { Id = 0 }.Decode(stream);

            //4 header bytes, 255 flag bytes, then 5 value bytes per slot: the two extremes need the
            //wide form and the zero takes the narrow one.
            Assert.Equal(encoded.Length, stream.Position);
            Assert.Equal(4 + 255 + (255 * 5), encoded.Length);
            Assert.Equal(FrameDefinition.MaxTransforms, reread.TransformCount);
            Assert.Equal(FrameDefinition.MaxSkeletonId, reread.SkeletonId);
            Assert.Equal(-16384, reread.Transforms[254].X.Value);
            Assert.Equal(16383, reread.Transforms[254].Y.Value);
            Assert.Equal(encoded, reread.Encode().ToArray());
        }

        // ===================================================================
        //  Helpers
        // ===================================================================

        /// <summary>
        ///     Builds a frame from a flag list and the values those flags call for.
        /// </summary>
        /// <param name="skeletonId">The skeleton the frame names.</param>
        /// <param name="flags">One flag byte per slot.</param>
        /// <param name="values">The value stream, in slot order and then x, y, z.</param>
        /// <returns>The frame.</returns>
        private static FrameDefinition FrameOf(int skeletonId, int[] flags, int[] values)
        {
            var frame = new FrameDefinition { Id = 0, SkeletonId = skeletonId };
            int next = 0;

            foreach (int flag in flags)
            {
                var transform = new FrameTransform { Flag = flag };
                if (transform.HasX)
                    transform.X = new FrameValue(values[next++]);
                if (transform.HasY)
                    transform.Y = new FrameValue(values[next++]);
                if (transform.HasZ)
                    transform.Z = new FrameValue(values[next++]);
                frame.Transforms.Add(transform);
            }

            Assert.Equal(values.Length, next);
            return frame;
        }

        /// <summary>The three resolved axes of a pose, for a compact assertion.</summary>
        /// <param name="pose">The pose.</param>
        /// <returns>x, y and z.</returns>
        private static int[] Axes(FramePose pose) => new[] { pose.X, pose.Y, pose.Z };
    }
}
