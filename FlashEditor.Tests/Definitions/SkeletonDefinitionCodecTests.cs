using System;
using System.IO;
using FlashEditor.Definitions.Animation;
using Xunit;

namespace FlashEditor.Tests.Definitions
{
    /// <summary>
    ///     Pins the index-1 skeleton codec against bytes it did not produce.
    /// </summary>
    /// <remarks>
    ///     Round-tripping this encoder against this decoder proves nothing, so the two sources here
    ///     are the cache and the client. <see cref="CapturedSkeleton"/> is group 2905 verbatim, and
    ///     <c>RealCacheSkeletonTests</c> asserts it still is; the synthetic records are laid out by
    ///     hand to the read order in <c>Node_Sub1.java:87-117</c> and exercise the two values the
    ///     client normalises on load, neither of which occurs in this cache.
    /// </remarks>
    public class SkeletonDefinitionCodecTests
    {
        /// <summary>
        ///     Index 1 group 2905 exactly as the cache stores it: three bones, types 0/1/3, one flag
        ///     set, and a bone with no labels at all.
        /// </summary>
        /// <remarks>
        ///     Chosen because it is 18 bytes and still exercises every block, including the two the
        ///     cache never varies: the mask is <c>0xFFFF</c> on all 173,749 bones and the flag byte is
        ///     only ever 0 or 1.
        /// </remarks>
        private static readonly byte[] CapturedSkeleton =
        {
            0x03,                                //3 bones
            0x00, 0x01, 0x03,                    //transform types
            0x00, 0x00, 0x01,                    //flags
            0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF,  //masks
            0x01, 0x01, 0x00,                    //label counts
            0x00, 0x01                           //labels, all bones concatenated
        };

        /// <summary>The group the captured bytes were read from.</summary>
        public const int CapturedSkeletonId = 2905;

        /// <summary>Those bytes, so the cache-backed test can compare without a second copy.</summary>
        /// <returns>A fresh copy of the captured record.</returns>
        public static byte[] CapturedSkeletonBytes() => (byte[])CapturedSkeleton.Clone();

        /// <summary>
        ///     A real record decodes into the column-major layout the client reads.
        /// </summary>
        /// <remarks>
        ///     This is what settles the read order. Every block is a different width, so a decoder
        ///     that read a bone as one contiguous record - type, flag, mask, count, labels - would
        ///     still consume 18 bytes on this file and produce entirely different bones.
        /// </remarks>
        [Fact]
        public void ACapturedSkeleton_DecodesIntoTheClientsColumnOrder()
        {
            var stream = new JagStream(CapturedSkeletonBytes());
            var skeleton = new SkeletonDefinition { Id = CapturedSkeletonId }.Decode(stream);

            Assert.Equal(CapturedSkeleton.Length, stream.Position);
            Assert.Equal(3, skeleton.BoneCount);

            Assert.Equal(new[] { 0, 1, 3 }, new[]
            {
                skeleton.Bones[0].TransformType, skeleton.Bones[1].TransformType, skeleton.Bones[2].TransformType
            });
            Assert.Equal(new[] { 0, 0, 1 }, new[]
            {
                skeleton.Bones[0].Flag, skeleton.Bones[1].Flag, skeleton.Bones[2].Flag
            });
            Assert.All(skeleton.Bones, bone => Assert.Equal(0xFFFF, bone.Mask));

            Assert.Equal(new[] { 0 }, skeleton.Bones[0].Labels.ToArray());
            Assert.Equal(new[] { 1 }, skeleton.Bones[1].Labels.ToArray());
            Assert.Empty(skeleton.Bones[2].Labels);

            Assert.False(skeleton.Bones[0].IsFlagSet);
            Assert.True(skeleton.Bones[2].IsFlagSet);
            Assert.Equal(2, skeleton.TotalLabelCount);
        }

        /// <summary>A real record re-encodes to the bytes it came from.</summary>
        [Fact]
        public void ACapturedSkeleton_ReEncodesToItsStoredBytes()
        {
            var skeleton = new SkeletonDefinition { Id = CapturedSkeletonId }
                .Decode(new JagStream(CapturedSkeletonBytes()));

            Assert.Equal(CapturedSkeletonBytes(), skeleton.Encode().ToArray());
        }

        /// <summary>
        ///     Every label count is read before any label byte.
        /// </summary>
        /// <remarks>
        ///     The client sizes all the label arrays in one loop (<c>Node_Sub1.java:109-111</c>) and
        ///     fills them in a second (:113-117). A decoder that read one bone's count then its
        ///     labels would take this record's second count as bone 0's second label and desynchronise
        ///     for the rest of the file. Interleaving the two passes reads identically whenever no
        ///     bone carries more than one label, which is true of 586 of the 3106 shipped skeletons,
        ///     so the smallest record that tells the two orders apart is worth stating outright rather
        ///     than leaving to the sweep to report as a byte mismatch.
        /// </remarks>
        [Fact]
        public void AllLabelCountsAreReadBeforeAnyLabel()
        {
            byte[] record =
            {
                0x02,
                0x01, 0x02,              //transform types
                0x00, 0x00,              //flags
                0xFF, 0xFF, 0xFF, 0xFF,  //masks
                0x02, 0x01,              //label counts: two then one
                0x0A, 0x0B, 0x0C         //labels
            };

            var stream = new JagStream((byte[])record.Clone());
            var skeleton = new SkeletonDefinition { Id = 0 }.Decode(stream);

            Assert.Equal(record.Length, stream.Position);
            Assert.Equal(new[] { 10, 11 }, skeleton.Bones[0].Labels.ToArray());
            Assert.Equal(new[] { 12 }, skeleton.Bones[1].Labels.ToArray());
            Assert.Equal(record, skeleton.Encode().ToArray());
        }

        /// <summary>
        ///     A stored transform type 6 keeps its byte and reports the client's remap separately.
        /// </summary>
        /// <remarks>
        ///     <c>Node_Sub1.java:96-98</c> rewrites a stored 6 to a 2 on load, and nothing writes the
        ///     array back, so the client has no use for the distinction. A cache editor does: folding
        ///     the remap into the field would re-encode the 6 as a 2 and rewrite a file nobody edited,
        ///     changing the archive CRC with it. Type 6 occurs zero times in the 173,749 bones of this
        ///     cache, so this synthetic record is the only thing standing between that defect and the
        ///     first repack that introduces one.
        /// </remarks>
        [Fact]
        public void AStoredTransformType6_KeepsItsByteAndRemapsOnlyOnRead()
        {
            byte[] record =
            {
                0x02,
                0x06, 0x02,              //an aliased type beside the type it aliases
                0x00, 0x00,
                0xFF, 0xFF, 0xFF, 0xFF,
                0x00, 0x00
            };

            var skeleton = new SkeletonDefinition { Id = 0 }.Decode(new JagStream((byte[])record.Clone()));

            Assert.Equal(6, skeleton.Bones[0].TransformType);
            Assert.Equal(2, skeleton.Bones[0].EffectiveTransformType);
            Assert.Equal(2, skeleton.Bones[1].TransformType);
            Assert.Equal(2, skeleton.Bones[1].EffectiveTransformType);

            //Both bones drive the same transform, and only one of them re-encodes as a 6.
            Assert.Equal(new[] { 2, 2 }, skeleton.GetEffectiveTransformTypes());
            Assert.Equal(record, skeleton.Encode().ToArray());
        }

        /// <summary>
        ///     A flag byte that is neither 0 nor 1 keeps its value and reads as unset.
        /// </summary>
        /// <remarks>
        ///     The client's test is <c>== 1</c> (<c>Node_Sub1.java:102</c>), so a <c>bool</c> field
        ///     would decode a stored 2 as false and write back 0. Only 0 and 1 occur in this cache -
        ///     173,153 and 596 - so this is the same latent hazard as the type-6 remap and is removed
        ///     the same way, by keeping the byte.
        /// </remarks>
        [Fact]
        public void AFlagByteAboveOne_KeepsItsValueAndReadsAsUnset()
        {
            byte[] record =
            {
                0x01,
                0x00,
                0x02,        //neither 0 nor 1
                0xFF, 0xFF,
                0x00
            };

            var skeleton = new SkeletonDefinition { Id = 0 }.Decode(new JagStream((byte[])record.Clone()));

            Assert.Equal(2, skeleton.Bones[0].Flag);
            Assert.False(skeleton.Bones[0].IsFlagSet);
            Assert.Equal(record, skeleton.Encode().ToArray());
        }

        /// <summary>
        ///     A skeleton with no bones is a single zero byte, and survives it.
        /// </summary>
        /// <remarks>
        ///     Groups 3046 and 3092 are exactly this. A decoder that assumed at least one bone reads
        ///     off the end of a one-byte file.
        /// </remarks>
        [Fact]
        public void AZeroBoneSkeleton_DecodesAndReEncodes()
        {
            var stream = new JagStream(new byte[] { 0x00 });
            var skeleton = new SkeletonDefinition { Id = 3046 }.Decode(stream);

            Assert.Equal(1, stream.Position);
            Assert.Empty(skeleton.Bones);
            Assert.Equal(0, skeleton.TotalLabelCount);
            Assert.Empty(skeleton.GetEffectiveTransformTypes());
            Assert.Equal(new byte[] { 0x00 }, skeleton.Encode().ToArray());
        }

        /// <summary>A record shorter than its bone count declares is refused, not padded.</summary>
        [Fact]
        public void ATruncatedRecord_Throws()
        {
            //Declares two bones and then stops inside the mask block.
            byte[] truncated = { 0x02, 0x00, 0x01, 0x00, 0x00, 0xFF };

            Assert.Throws<EndOfStreamException>(
                () => new SkeletonDefinition { Id = 0 }.Decode(new JagStream(truncated)));
        }

        /// <summary>
        ///     An edit the format cannot store is refused rather than truncated.
        /// </summary>
        /// <remarks>
        ///     Every field bar the mask is a single byte, so a masked-off 256 writes a 0 - which for a
        ///     label id silently moves the bone onto a different label group and for a label count
        ///     silently empties the bone. Neither shows up in any sweep, because no unedited record
        ///     can produce it.
        /// </remarks>
        [Fact]
        public void Encode_RefusesAValueTooWideForItsField()
        {
            var skeleton = new SkeletonDefinition { Id = 7 };
            skeleton.Bones.Add(new SkeletonBone());

            skeleton.Bones[0].TransformType = 256;
            Assert.Throws<InvalidOperationException>(() => skeleton.Encode());
            skeleton.Bones[0].TransformType = 0;

            skeleton.Bones[0].Mask = 0x10000;
            Assert.Throws<InvalidOperationException>(() => skeleton.Encode());
            skeleton.Bones[0].Mask = 0xFFFF;

            skeleton.Bones[0].Labels.Add(256);
            Assert.Throws<InvalidOperationException>(() => skeleton.Encode());
            skeleton.Bones[0].Labels.Clear();

            for (int i = 0; i < SkeletonDefinition.MaxBones; i++)
                skeleton.Bones.Add(new SkeletonBone());
            Assert.Equal(SkeletonDefinition.MaxBones + 1, skeleton.BoneCount);
            Assert.Throws<InvalidOperationException>(() => skeleton.Encode());
        }

        /// <summary>
        ///     A skeleton at the format's ceilings encodes and reads back unchanged.
        /// </summary>
        /// <remarks>
        ///     255 bones and 254 labels on a bone both occur in this cache, so the ceiling is not
        ///     theoretical. 255 labels does not occur, and this is the only thing that covers it.
        /// </remarks>
        [Fact]
        public void ASkeletonAtTheFormatsCeilings_RoundTrips()
        {
            var skeleton = new SkeletonDefinition { Id = 0 };
            for (int i = 0; i < SkeletonDefinition.MaxBones; i++)
            {
                var bone = new SkeletonBone { TransformType = i % 11, Flag = i % 2, Mask = 0xFFFF };
                for (int label = 0; label < 255; label++)
                    bone.Labels.Add(label);
                skeleton.Bones.Add(bone);
            }

            byte[] encoded = skeleton.Encode().ToArray();
            var stream = new JagStream(encoded);
            var reread = new SkeletonDefinition { Id = 0 }.Decode(stream);

            Assert.Equal(encoded.Length, stream.Position);
            Assert.Equal(255, reread.BoneCount);
            Assert.Equal(255 * 255, reread.TotalLabelCount);
            Assert.Equal(254, reread.Bones[254].Labels[254]);
            Assert.Equal(encoded, reread.Encode().ToArray());
        }
    }
}
