using System;
using System.Collections.Generic;
using System.IO;
using FlashEditor;
using FlashEditor.Definitions.Fonts;
using Xunit;

namespace FlashEditor.Tests.Definitions
{
    /// <summary>
    ///     Pins the index-13 font codec against records built byte by byte from the client's read
    ///     order.
    /// </summary>
    /// <remarks>
    ///     <b>The kerned branch is why this file exists.</b> No group in either supported cache sets
    ///     the kerning flag, so the byte-identity sweep over index 13 never once enters
    ///     <c>Class197.java:33-84</c> - it could be deleted outright and every real-cache test would
    ///     still pass. That is exactly the trap <c>CLAUDE.md</c> describes for the format-7
    ///     reference-table branches: the first record that used it would be mis-parsed from that
    ///     field onward, and no sweep could catch it. So the kerned record here is synthetic, its
    ///     bytes are stated literally rather than produced by the encoder, and its kerning matrix is
    ///     worked out by hand from <c>Class378.method4003:43-69</c>.
    ///     <para>
    ///     Each profile is given twice on purpose: once as the delta bytes the record stores and once
    ///     as the values they decode to. Neither is computed from the other by production code, so a
    ///     decoder and an encoder that agreed with each other about the wrong delta scheme would
    ///     still fail here.
    ///     </para>
    /// </remarks>
    public class FontDefinitionCodecTests
    {
        private const int Space = FontDefinition.SpaceCharacter;
        private const int NoBreakSpace = FontDefinition.NoBreakSpaceCharacter;
        private const int A = 65;
        private const int B = 66;
        private const int C = 67;
        private const int D = 68;
        private const int F = 70;
        private const int G = 71;

        // ===================================================================
        //  The unkerned record - the only shape either supported cache holds
        // ===================================================================

        /// <summary>
        ///     The unkerned layout is version, flag, 256 advances, line height, two discarded bytes,
        ///     ascent, descent - and nothing else.
        /// </summary>
        /// <remarks>
        ///     Every offset is asserted against a distinct value, so a decoder that read the tail in
        ///     the wrong order still fails. The order is <c>Class197.java:86-92</c>: the line height
        ///     first, then two <c>readUnsignedByte</c> calls whose results are dropped, then ascent
        ///     and descent.
        /// </remarks>
        [Fact]
        public void AnUnkernedRecord_ReadsEveryFieldFromTheOffsetTheClientReadsItFrom()
        {
            byte[] stored = UnkernedRecord(kerningFlag: 0, lineHeight: 35, unused259: 9,
                unused260: 6, ascent: 12, descent: 3);

            var font = new FontDefinition { Id = 3793 };
            var stream = new JagStream(stored);
            font.Decode(stream);

            Assert.Equal(stored.Length, stream.Position);
            Assert.Equal(0, font.Version);
            Assert.False(font.IsKerned);
            Assert.Equal(35, font.LineHeight);
            Assert.Equal(9, font.UnusedByte259);
            Assert.Equal(6, font.UnusedByte260);
            Assert.Equal(12, font.Ascent);
            Assert.Equal(3, font.Descent);

            //The advance table is written as its own character code, so a shifted read is visible.
            Assert.Equal(A, font.AdvanceOf(A));
            Assert.Equal(255, font.AdvanceOf(255));
        }

        /// <summary>An unkerned record is exactly the length the format implies.</summary>
        /// <remarks>
        ///     Stated against the constant the format derives rather than against 263 written down,
        ///     and asserted here rather than only over the cache: the cache can only say that its own
        ///     records are 263 bytes, not that 263 is what this layout produces.
        /// </remarks>
        [Fact]
        public void AnUnkernedRecord_IsTheLengthTheLayoutImplies()
        {
            Assert.Equal(2 + FontDefinition.CharacterCount + 5, FontDefinition.UnkernedLength);
            Assert.Equal(FontDefinition.UnkernedLength, UnkernedRecord().Length);
        }

        /// <summary>An unkerned record re-encodes to the bytes it was read from.</summary>
        [Fact]
        public void AnUnkernedRecord_ReEncodesToItsStoredBytes()
        {
            byte[] stored = UnkernedRecord(kerningFlag: 0, lineHeight: 35, unused259: 9,
                unused260: 6, ascent: 12, descent: 3);

            var font = new FontDefinition();
            font.Decode(new JagStream(stored));

            Assert.Equal(stored, font.Encode().ToArray());
        }

        /// <summary>
        ///     The two bytes the client reads and throws away survive a re-encode.
        /// </summary>
        /// <remarks>
        ///     They carry no meaning the client acts on (<c>Class197.java:89-90</c> discards both)
        ///     and they differ from font to font, so a decoder modelled as "widths, line height,
        ///     ascent, descent" loses them and rewrites bytes nobody edited. Asserted against
        ///     distinct values so a decoder that read one byte twice fails.
        /// </remarks>
        [Fact]
        public void TheDiscardedBytes_SurviveARoundTrip()
        {
            byte[] stored = UnkernedRecord(unused259: 43, unused260: 58);

            var font = new FontDefinition();
            font.Decode(new JagStream(stored));

            Assert.Equal(43, font.UnusedByte259);
            Assert.Equal(58, font.UnusedByte260);
            Assert.Equal(stored, font.Encode().ToArray());
        }

        /// <summary>
        ///     A kerning-flag byte that is neither 0 nor 1 means unkerned and re-encodes as itself.
        /// </summary>
        /// <remarks>
        ///     <b>The normalisation no sweep here could catch.</b> <c>Class197.java:28</c> folds the
        ///     byte with <c>== 1</c>, so 0 and 2 both select the unkerned layout and a decoder that
        ///     kept only the boolean would write a stored 2 back as 0. Every record in both supported
        ///     caches stores 0, so the data cannot tell the two rules apart and only a synthetic case
        ///     can. This is the same shape as the aliased terrain height bytes <c>CLAUDE.md</c>
        ///     records.
        /// </remarks>
        [Fact]
        public void ANonBinaryKerningFlag_MeansUnkernedAndIsKeptVerbatim()
        {
            byte[] stored = UnkernedRecord(kerningFlag: 2);

            var font = new FontDefinition();
            font.Decode(new JagStream(stored));

            Assert.False(font.IsKerned);
            Assert.Equal(2, font.KerningFlag);
            Assert.Equal(stored, font.Encode().ToArray());
        }

        /// <summary>
        ///     A version byte the client would throw on is refused rather than parsed.
        /// </summary>
        /// <remarks>
        ///     <c>Class197.java:22-26</c> throws for anything but 0. Carrying on would decode a
        ///     layout this project has never seen and hand the editor plausible-looking metrics for
        ///     it.
        /// </remarks>
        [Fact]
        public void AnUnsupportedVersion_IsRefused()
        {
            byte[] stored = UnkernedRecord();
            stored[0] = 1;

            var font = new FontDefinition { Id = 305 };

            Assert.Throws<InvalidDataException>(() => font.Decode(new JagStream(stored)));
        }

        /// <summary>
        ///     An unkerned font has no kerning matrix, rather than an all-zero one.
        /// </summary>
        /// <remarks>
        ///     The client keeps <c>aByteArrayArray1516</c> null and every reader checks it
        ///     (<c>Class197.java:151,249,502</c>). A zero-filled table would read as "this pair kerns
        ///     to nothing", which is a different statement from "this font does not kern".
        /// </remarks>
        [Fact]
        public void AnUnkernedFont_HasNoKerningMatrix()
        {
            var font = new FontDefinition();
            font.Decode(new JagStream(UnkernedRecord()));

            Assert.Null(font.KerningMatrix());
        }

        /// <summary>The line height cannot be edited on a record with no slot for it.</summary>
        /// <remarks>
        ///     A kerned record derives it from the space glyph's profile (<c>Class197.java:84</c>).
        ///     Accepting the write would drop the value silently on the next save.
        /// </remarks>
        [Fact]
        public void TheLineHeight_CannotBeSetOnAKernedRecord()
        {
            FontDefinition font = DecodedKernedRecord();

            //A block body, not an expression body: an assignment lambda is convertible to both
            //Action and the Func<object> overload xUnit keeps to catch that very mistake.
            Assert.Throws<InvalidOperationException>(() => { font.LineHeight = 20; });
        }

        // ===================================================================
        //  The kerned record - unreachable in both caches, so pinned here
        // ===================================================================

        /// <summary>
        ///     The kerned layout reads three 256-entry tables and two variable-length profile blocks
        ///     before the tail, and consumes the record exactly.
        /// </summary>
        /// <remarks>
        ///     The block sizes are the whole risk. <c>Class197.java:48</c> and <c>:61</c> both
        ///     allocate from <c>is_28_</c>, so the second profile block is sized by the <i>first</i>
        ///     table and not by one of its own - a decoder that invented a second length table would
        ///     desynchronise here and land somewhere plausible in the tail.
        /// </remarks>
        [Fact]
        public void AKernedRecord_ConsumesEveryBlockTheClientReads()
        {
            byte[] stored = KernedRecord();
            var stream = new JagStream(stored);

            var font = new FontDefinition { Id = 1 };
            font.Decode(stream);

            Assert.Equal(stored.Length, stream.Position);
            Assert.True(font.IsKerned);
            Assert.Equal(1, font.KerningFlag);
        }

        /// <summary>
        ///     A kerned record's length is <c>774 + 2 * sum(rows)</c>, which is what makes it
        ///     unmistakable on disk.
        /// </summary>
        /// <remarks>
        ///     Worth asserting on the synthetic record because the real-cache sweep can only ever
        ///     confirm the unkerned length. If a future cache ships a kerned font, this is the
        ///     arithmetic the sweep will be checking it against.
        /// </remarks>
        [Fact]
        public void AKernedRecord_IsTheLengthItsRowCountsImply()
        {
            int rows = 0;
            foreach (KeyValuePair<int, byte[]> profile in LeftEdgeDeltas)
                rows += profile.Value.Length;

            int expected = 2 + 3 * FontDefinition.CharacterCount + 2 * rows + 4;

            Assert.Equal(expected, KernedRecord().Length);
        }

        /// <summary>
        ///     Every profile decodes to the values its deltas describe, wrapping in eight bits.
        /// </summary>
        /// <remarks>
        ///     <c>Class197.java:50</c> declares the accumulator as a Java <c>byte</c>, so a run that
        ///     overflows is legal rather than an error, and a decoder that accumulated in a wider
        ///     type would diverge on the first font that used one. Character 71's single delta of
        ///     <c>0xF0</c> is the case: it decodes to -16, which is what makes its pair with 70 kern
        ///     apart rather than together.
        /// </remarks>
        [Fact]
        public void EveryProfile_DecodesToTheValuesItsDeltasDescribe()
        {
            FontDefinition font = DecodedKernedRecord();

            foreach (KeyValuePair<int, byte[]> expected in LeftEdgeValues)
                Assert.Equal(expected.Value, font.LeftEdgeProfiles[expected.Key]);

            foreach (KeyValuePair<int, byte[]> expected in RightEdgeValues)
                Assert.Equal(expected.Value, font.RightEdgeProfiles[expected.Key]);

            //Every character the record does not name has no profile at all, rather than a
            //one-entry one - a decoder that read a byte per character regardless would desync.
            Assert.Empty(font.LeftEdgeProfiles[1]);
            Assert.Empty(font.RightEdgeProfiles[1]);
        }

        /// <summary>A kerned record re-encodes to the bytes it was read from.</summary>
        /// <remarks>
        ///     The delta re-derivation is the part at risk: for a given predecessor exactly one
        ///     signed byte reaches a given successor in eight-bit arithmetic, so an encoder that got
        ///     it right is byte-exact and one that clamped instead of wrapping is not.
        /// </remarks>
        [Fact]
        public void AKernedRecord_ReEncodesToItsStoredBytes()
        {
            byte[] stored = KernedRecord();

            var font = new FontDefinition();
            font.Decode(new JagStream(stored));

            Assert.Equal(stored, font.Encode().ToArray());
        }

        /// <summary>
        ///     A kerned record's line height is the space glyph's box, not a stored byte.
        /// </summary>
        /// <remarks>
        ///     <c>Class197.java:84</c>: <c>is_28_[32] + is_29_[32]</c>. The synthetic record gives
        ///     space 5 rows starting at row 7, so the line height is 12 - a number that appears
        ///     nowhere in the record's bytes, which is the point.
        /// </remarks>
        [Fact]
        public void AKernedRecord_DerivesItsLineHeightFromTheSpaceGlyph()
        {
            FontDefinition font = DecodedKernedRecord();

            Assert.Equal(12, font.LineHeight);
        }

        /// <summary>
        ///     The kerning matrix agrees with <c>Class378.method4003</c> worked through by hand.
        /// </summary>
        /// <remarks>
        ///     Four cases, each covering something the others do not:
        ///     <list type="bullet">
        ///     <item>A and B overlap on rows 2 and 3, and the smaller clearance wins: the profiles
        ///     sum to 5 on both rows, well under the 10-pixel advance cap, so the pair closes by
        ///     5.</item>
        ///     <item>B before A reads the <i>other</i> profile of each character and gives 8, so a
        ///     matrix that had the two blocks the wrong way round would fail even though both pairs
        ///     exist.</item>
        ///     <item>A and C do not overlap at all - C's box starts at row 200 - so the loop never
        ///     runs and the advance cap stands, kerning by a whole 3 pixels. Faithful to the client
        ///     and easy to "fix" into a zero.</item>
        ///     <item>F and G sum to a negative clearance, because G's profile byte is <c>0xF0</c>.
        ///     The pair kerns <i>apart</i>, which only happens if the profile bytes are read
        ///     signed.</item>
        ///     </list>
        /// </remarks>
        [Fact]
        public void TheKerningMatrix_AgreesWithTheClientWorkedThroughByHand()
        {
            sbyte[,] kerning = DecodedKernedRecord().KerningMatrix();

            Assert.NotNull(kerning);
            Assert.Equal(-5, kerning[A, B]);
            Assert.Equal(-8, kerning[B, A]);
            Assert.Equal(-3, kerning[A, C]);
            Assert.Equal(-3, kerning[C, A]);
            Assert.Equal(-10, kerning[D, A]);
            Assert.Equal(11, kerning[F, G]);
        }

        /// <summary>
        ///     Space and no-break space are skipped on both axes and stay at zero.
        /// </summary>
        /// <remarks>
        ///     <c>Class197.java:74,76</c>. Space carries a real five-row profile in this record, so a
        ///     matrix that failed to skip it would produce a non-zero entry here rather than
        ///     coincidentally agreeing.
        /// </remarks>
        [Fact]
        public void TheKerningMatrix_LeavesTheTwoSpaceCharactersAtZero()
        {
            sbyte[,] kerning = DecodedKernedRecord().KerningMatrix();

            Assert.Equal(0, kerning[Space, A]);
            Assert.Equal(0, kerning[A, Space]);
            Assert.Equal(0, kerning[NoBreakSpace, A]);
            Assert.Equal(0, kerning[A, NoBreakSpace]);
        }

        /// <summary>
        ///     The matrix is derived, so an advance-width edit moves it.
        /// </summary>
        /// <remarks>
        ///     The advance is the clearance cap (<c>Class378.method4003:55-57</c>), so narrowing A
        ///     below the profile-derived gap makes it the binding constraint. A cached matrix that
        ///     was not invalidated would keep answering -5.
        /// </remarks>
        [Fact]
        public void TheKerningMatrix_FollowsAnAdvanceWidthEdit()
        {
            FontDefinition font = DecodedKernedRecord();
            Assert.Equal(-5, font.KerningMatrix()[A, B]);

            font.SetAdvance(A, 2);

            Assert.Equal(-2, font.KerningMatrix()[A, B]);
        }

        /// <summary>An advance width above 127 is read unsigned.</summary>
        /// <remarks>
        ///     Character 68 stores 200. The client reads the table with <c>0xff &amp;</c>
        ///     (<c>Class197.java:193</c>) and compares it unsigned in the kerning walk, so a signed
        ///     model would make it -56 and cap every pair it appears in at a negative clearance.
        /// </remarks>
        [Fact]
        public void AnAdvanceWidthAbove127_IsReadUnsigned()
        {
            FontDefinition font = DecodedKernedRecord();

            Assert.Equal(200, font.AdvanceOf(D));
        }

        // ===================================================================
        //  Record builders - layout stated literally, never encoded
        // ===================================================================

        /// <summary>
        ///     Lays out an unkerned record by hand, per <c>Class197.java:22-31,86-92</c>.
        /// </summary>
        /// <remarks>
        ///     Each advance width is its own character code, so a decoder that read the table at the
        ///     wrong offset produces visibly shifted values rather than a plausible font.
        /// </remarks>
        /// <param name="kerningFlag">The stored flag byte.</param>
        /// <param name="lineHeight">The stored line height.</param>
        /// <param name="unused259">The first byte the client discards.</param>
        /// <param name="unused260">The second byte the client discards.</param>
        /// <param name="ascent">Rows above the baseline.</param>
        /// <param name="descent">Rows below the baseline.</param>
        /// <returns>The record bytes.</returns>
        private static byte[] UnkernedRecord(byte kerningFlag = 0, byte lineHeight = 12,
            byte unused259 = 9, byte unused260 = 6, byte ascent = 12, byte descent = 3)
        {
            var record = new List<byte> { 0, kerningFlag };

            for (int character = 0; character < FontDefinition.CharacterCount; character++)
                record.Add((byte) character);

            record.Add(lineHeight);
            record.Add(unused259);
            record.Add(unused260);
            record.Add(ascent);
            record.Add(descent);

            return record.ToArray();
        }

        /// <summary>Advance widths the synthetic kerned record stores, by character code.</summary>
        /// <remarks>68 stores 200 so the unsigned read is exercised; every other character is 0.</remarks>
        private static readonly Dictionary<int, byte> Advances = new Dictionary<int, byte>
        {
            [Space] = 4, [A] = 10, [B] = 12, [C] = 3, [D] = 200, [F] = 20, [G] = 20
        };

        /// <summary>Rows in each character's edge profile, <c>is_28_</c>.</summary>
        private static readonly Dictionary<int, byte> Rows = new Dictionary<int, byte>
        {
            [Space] = 5, [A] = 3, [B] = 2, [C] = 1, [F] = 1, [G] = 1
        };

        /// <summary>The row each character's profile starts at, <c>is_29_</c>.</summary>
        /// <remarks>C starts at row 200, far below A, so their boxes cannot overlap.</remarks>
        private static readonly Dictionary<int, byte> Tops = new Dictionary<int, byte>
        {
            [Space] = 7, [A] = 1, [B] = 2, [C] = 200, [F] = 0, [G] = 0
        };

        /// <summary>The left-edge block as stored: signed deltas down the rows.</summary>
        private static readonly Dictionary<int, byte[]> LeftEdgeDeltas = new Dictionary<int, byte[]>
        {
            [Space] = new byte[] { 0, 0, 0, 0, 0 },
            [A] = new byte[] { 0x04, 0xFD, 0x05 },
            [B] = new byte[] { 0x02, 0x03 },
            [C] = new byte[] { 0x09 },
            [F] = new byte[] { 0x00 },
            [G] = new byte[] { 0xF0 }
        };

        /// <summary>What those deltas decode to.</summary>
        private static readonly Dictionary<int, byte[]> LeftEdgeValues = new Dictionary<int, byte[]>
        {
            [Space] = new byte[] { 0, 0, 0, 0, 0 },
            [A] = new byte[] { 4, 1, 6 },
            [B] = new byte[] { 2, 5 },
            [C] = new byte[] { 9 },
            [F] = new byte[] { 0 },
            [G] = new byte[] { 0xF0 }
        };

        /// <summary>The right-edge block as stored.</summary>
        private static readonly Dictionary<int, byte[]> RightEdgeDeltas = new Dictionary<int, byte[]>
        {
            [Space] = new byte[] { 0, 0, 0, 0, 0 },
            [A] = new byte[] { 0x01, 0x02, 0xFD },
            [B] = new byte[] { 0x07, 0xFB },
            [C] = new byte[] { 0x09 },
            [F] = new byte[] { 0x05 },
            [G] = new byte[] { 0x00 }
        };

        /// <summary>What those deltas decode to.</summary>
        private static readonly Dictionary<int, byte[]> RightEdgeValues = new Dictionary<int, byte[]>
        {
            [Space] = new byte[] { 0, 0, 0, 0, 0 },
            [A] = new byte[] { 1, 3, 0 },
            [B] = new byte[] { 7, 2 },
            [C] = new byte[] { 9 },
            [F] = new byte[] { 5 },
            [G] = new byte[] { 0 }
        };

        /// <summary>
        ///     Lays out a kerned record by hand, per <c>Class197.java:33-69</c> and the tail at
        ///     <c>:89-92</c>.
        /// </summary>
        /// <remarks>
        ///     Pure concatenation: every byte comes from the tables above rather than from the
        ///     encoder, so this cannot agree with a defect the encoder has.
        /// </remarks>
        /// <returns>The record bytes.</returns>
        private static byte[] KernedRecord()
        {
            var record = new List<byte> { 0, 1 };

            AppendTable(record, Advances);
            AppendTable(record, Rows);
            AppendTable(record, Tops);
            AppendProfiles(record, LeftEdgeDeltas);
            AppendProfiles(record, RightEdgeDeltas);

            record.Add(43);
            record.Add(58);
            record.Add(51);
            record.Add(2);

            return record.ToArray();
        }

        /// <summary>Decodes the synthetic kerned record.</summary>
        /// <returns>The decoded font.</returns>
        private static FontDefinition DecodedKernedRecord()
        {
            var font = new FontDefinition { Id = 1 };
            font.Decode(new JagStream(KernedRecord()));
            return font;
        }

        /// <summary>Writes a 256-entry table, zero for every character the map omits.</summary>
        /// <param name="record">The record being built.</param>
        /// <param name="values">The characters that carry a non-zero entry.</param>
        private static void AppendTable(List<byte> record, IReadOnlyDictionary<int, byte> values)
        {
            for (int character = 0; character < FontDefinition.CharacterCount; character++)
                record.Add(values.TryGetValue(character, out byte value) ? value : (byte) 0);
        }

        /// <summary>Writes a profile block in character order, omitting the zero-row characters.</summary>
        /// <param name="record">The record being built.</param>
        /// <param name="profiles">The characters that carry a profile.</param>
        private static void AppendProfiles(List<byte> record, IReadOnlyDictionary<int, byte[]> profiles)
        {
            for (int character = 0; character < FontDefinition.CharacterCount; character++)
                if (profiles.TryGetValue(character, out byte[] profile))
                    record.AddRange(profile);
        }
    }
}
