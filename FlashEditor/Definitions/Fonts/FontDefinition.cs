using System;
using System.IO;
using FlashEditor.Cache;
using FlashEditor.IO;

namespace FlashEditor.Definitions.Fonts {
    /// <summary>
    ///     One font's metrics: index 13's whole record, as the 637 client's <c>Class197(byte[])</c>
    ///     constructor reads it.
    /// </summary>
    /// <remarks>
    ///     <b>Index 13 is not sprite format.</b> It holds no pixels at all. The glyphs live in index
    ///     8 at the <i>same group id</i>: <c>Class114.java:82,89</c> passes one id <c>i</c> to both
    ///     archives, <c>Class324.method3684(spritesArchive, i)</c> for the 256-glyph sheet and
    ///     <c>Class119_Sub1.method2182(fontsArchive, i)</c> for this record, and
    ///     <c>InterfaceSettings.java:76,157</c> is where the two archives are opened. Every index-13
    ///     group in both supported caches exists in index 8 under the identical name hash, so the
    ///     join is provable rather than plausible.
    ///     <para>
    ///     <b>Two record shapes, chosen by the second byte.</b> <c>Class197.java:28</c> reads a flag
    ///     and <c>:33-84</c> takes a wholly different path when it is 1: three more 256-entry tables
    ///     and two variable-length edge profiles per character, from which the client derives a
    ///     256x256 kerning matrix. <b>Nothing in either supported cache sets that flag</b>, so no
    ///     byte-identity sweep defends the kerned path and a passing sweep is not evidence it is
    ///     right. It is implemented anyway and pinned by synthetic tests instead, for the reason
    ///     <c>AGENTS.md</c> gives for the format-7 reference-table branches: a decoder that drops an
    ///     unreachable branch mis-parses the first record that uses it, from that field onward, and
    ///     nothing would catch it.
    ///     </para>
    ///     <para>
    ///     <b>Stored and derived are kept apart.</b> <see cref="Encode"/> replays what
    ///     <see cref="Decode"/> read and never recomputes anything: the kerning flag is held as its
    ///     stored byte rather than as the boolean the client folds it to, and
    ///     <see cref="LineHeight"/> is a stored byte only when the record is unkerned - the kerned
    ///     branch computes the same field from the space character's profile (<c>:84</c>) and reads
    ///     no byte for it, so the record length changes with the flag.
    ///     </para>
    /// </remarks>
    public sealed class FontDefinition : IDefinition {
        /// <summary>
        ///     Character codes a font covers, one per possible byte value.
        /// </summary>
        /// <remarks>
        ///     A format constant, not a measurement. <c>Class197.java:193</c> indexes the advance
        ///     table with <c>0xff &amp;</c> an incoming character, and the kerning matrix at
        ///     <c>:71</c> is allocated 256 by 256, so anything narrower puts the client out of
        ///     bounds rather than merely losing a glyph.
        /// </remarks>
        public const int CharacterCount = 256;

        /// <summary>
        ///     Byte length of an unkerned record.
        /// </summary>
        /// <remarks>
        ///     Every group in both supported caches is this size, which is the whole of the
        ///     unkerned layout: version, kerning flag, 256 advance widths, line height, the two
        ///     bytes the client discards, ascent, descent. Stated here so a test can assert the
        ///     size the format implies rather than a size copied off the data.
        /// </remarks>
        public const int UnkernedLength = 2 + CharacterCount + 5;

        /// <summary>
        ///     The character whose profile supplies the line height on a kerned record.
        /// </summary>
        /// <remarks>
        ///     Space. <c>Class197.java:84</c> takes <c>is_28_[32] + is_29_[32]</c>, and the two
        ///     characters the kerning matrix skips entirely at <c>:74,76</c> are 32 and 160 - space
        ///     and no-break space - so the space glyph's box is doing double duty as the line box.
        /// </remarks>
        public const int SpaceCharacter = 32;

        /// <summary>The other character the kerning matrix leaves at zero.</summary>
        /// <remarks>No-break space, skipped alongside <see cref="SpaceCharacter"/> at <c>Class197.java:74,76</c>.</remarks>
        public const int NoBreakSpaceCharacter = 160;

        /// <summary>
        ///     The only version byte the client accepts.
        /// </summary>
        /// <remarks>
        ///     <c>Class197.java:22-26</c> throws for anything else, so an editor that let this be
        ///     changed would produce a cache that crashes at font load rather than one that renders
        ///     badly.
        /// </remarks>
        public const int SupportedVersion = 0;

        /// <summary>The stored kerning-flag byte that turns the kerned layout on.</summary>
        /// <remarks><c>Class197.java:28</c> compares for equality with 1, not for non-zero.</remarks>
        public const int KerningFlagSet = 1;

        private byte[] advanceWidths = new byte[CharacterCount];
        private byte[] glyphRows = new byte[CharacterCount];
        private byte[] glyphTops = new byte[CharacterCount];
        private byte[][] leftEdgeProfiles = EmptyProfiles();
        private byte[][] rightEdgeProfiles = EmptyProfiles();
        private sbyte[,]? kerning;

        /// <summary>The font id, which on index 13 is the group id.</summary>
        /// <remarks>
        ///     Not stored in the record. Carried so a failure can be reported against the font the
        ///     editor names, and so the index-8 glyph sheet can be found - the two share an id.
        /// </remarks>
        public int Id { get; set; } = -1;

        /// <summary>
        ///     The stored version byte.
        /// </summary>
        /// <remarks>
        ///     Kept rather than assumed so <see cref="Encode"/> is a pure replay. Only
        ///     <see cref="SupportedVersion"/> ever reaches it, because <see cref="Decode"/> refuses
        ///     anything the client would throw on.
        /// </remarks>
        public byte Version { get; private set; }

        /// <summary>
        ///     The kerning flag exactly as stored.
        /// </summary>
        /// <remarks>
        ///     The stored byte rather than the boolean, because the fold is many-to-one: the client
        ///     tests <c>== 1</c>, so 0 and 2 both mean unkerned and re-encoding from a boolean would
        ///     turn a stored 2 into a 0. Every record in both caches stores 0, so no sweep here
        ///     could ever catch that - which is exactly why the byte is kept.
        /// </remarks>
        public byte KerningFlag { get; private set; }

        /// <summary>Whether this record carries the kerning tables.</summary>
        /// <remarks>Derived from <see cref="KerningFlag"/> the way <c>Class197.java:28</c> derives it.</remarks>
        public bool IsKerned => KerningFlag == KerningFlagSet;

        /// <summary>
        ///     Per-character advance width, indexed by character code.
        /// </summary>
        /// <remarks>
        ///     Held as raw bytes because the client reads them back unsigned
        ///     (<c>Class197.java:193</c>, <c>0xff &amp;</c>) while the kerning walk at
        ///     <c>Class378.method4003:55-57</c> compares them unsigned too. A signed model would
        ///     re-encode a width above 127 as a negative number.
        /// </remarks>
        public byte[] AdvanceWidths => advanceWidths;

        /// <summary>
        ///     Stored line height, or the value the kerned branch derives instead.
        /// </summary>
        /// <remarks>
        ///     <b>Not the space advance.</b> It is the line step: <c>Class197.java:171-178</c>
        ///     defaults the caller's step to it and then sizes a block of text as
        ///     <c>(lines - 1) * step + ascent + descent</c>, and <c>RSFont.java:382-383</c> defaults
        ///     the same way. The four verdana records store 35 against a space advance of 4, so the
        ///     two are not the same field and cannot be derived from each other.
        ///     <para>
        ///     Stored on an unkerned record (<c>:86</c>) and computed as
        ///     <c>glyphRows[32] + glyphTops[32]</c> on a kerned one (<c>:84</c>), which is why the
        ///     setter refuses on a kerned record: there is no byte to write it into.
        ///     </para>
        /// </remarks>
        public int LineHeight {
            get => IsKerned
                ? (glyphRows[SpaceCharacter] & 0xFF) + (glyphTops[SpaceCharacter] & 0xFF)
                : StoredLineHeight;
            set {
                if (IsKerned)
                    throw new InvalidOperationException(
                        "A kerned font derives its line height from the space glyph's profile " +
                        "(Class197.java:84) and stores no byte for it, so it cannot be set directly.");
                if (value < 0 || value > 255)
                    throw new ArgumentOutOfRangeException(nameof(value), value,
                        "The line height is a single stored byte.");
                StoredLineHeight = (byte) value;
            }
        }

        /// <summary>The line-height byte as stored, meaningful only on an unkerned record.</summary>
        /// <remarks>
        ///     Separate from <see cref="LineHeight"/> so the encoder writes what it read rather than
        ///     what the getter would answer, which on a kerned record is a derivation.
        /// </remarks>
        public byte StoredLineHeight { get; private set; }

        /// <summary>
        ///     The first byte the client reads and throws away, kept verbatim.
        /// </summary>
        /// <remarks>
        ///     <c>Class197.java:89</c>. It varies from font to font - 9 for group 305, 43 for group
        ///     648 - so a decoder that modelled the record as "widths, line height, ascent, descent"
        ///     would lose it and re-encode differently from bytes nobody edited. Its meaning is
        ///     unknown and the name deliberately does not guess at one.
        /// </remarks>
        public byte UnusedByte259 { get; set; }

        /// <summary>The second discarded byte, kept for the same reason.</summary>
        /// <remarks><c>Class197.java:90</c>.</remarks>
        public byte UnusedByte260 { get; set; }

        /// <summary>
        ///     Rows the baseline sits above, <c>anInt1517</c>.
        /// </summary>
        /// <remarks>
        ///     Settled by use rather than by name: <c>IntegerNode.java:680,686</c> puts the glyph box
        ///     at <c>baseline - anInt1517</c> to <c>baseline + anInt1514</c>, and
        ///     <c>RSFont.java:942</c> writes <c>anInt1514 + anInt1517</c> as the box height.
        /// </remarks>
        public byte Ascent { get; set; }

        /// <summary>Rows the baseline sits below, <c>anInt1514</c>.</summary>
        /// <remarks>The other half of the box <see cref="Ascent"/> cites.</remarks>
        public byte Descent { get; set; }

        /// <summary>
        ///     Rows in each character's edge profile, <c>is_28_</c>. Kerned records only.
        /// </summary>
        /// <remarks>
        ///     It sizes <b>both</b> profile arrays - <c>Class197.java:48</c> and <c>:61</c> allocate
        ///     from the same table - so it is the record's only statement of how long the two
        ///     variable-length blocks are, and a decoder that sized the second from its own table
        ///     would desynchronise on the first font that used the branch.
        /// </remarks>
        public byte[] GlyphRows => glyphRows;

        /// <summary>
        ///     The row each character's profile starts at, <c>is_29_</c>. Kerned records only.
        /// </summary>
        /// <remarks>
        ///     <c>Class378.method4003:43-54</c> uses it as the profile's y origin, intersecting the
        ///     two characters' <c>[top, top + rows)</c> spans to find the rows where they overlap.
        /// </remarks>
        public byte[] GlyphTops => glyphTops;

        /// <summary>
        ///     Per-character left-edge inset, one entry per row. Kerned records only.
        /// </summary>
        /// <remarks>
        ///     <c>is_32_</c>, read first (<c>Class197.java:45-56</c>). It is consulted for the
        ///     <b>right</b>-hand character of a pair: <c>method4003</c> takes <c>is[i]</c> where
        ///     <c>i</c> is the second character (<c>Class197.java:77</c> passes <c>i_41_</c> first).
        ///     Stored delta-encoded as signed bytes down the rows, wrapping in eight bits.
        /// </remarks>
        public byte[][] LeftEdgeProfiles => leftEdgeProfiles;

        /// <summary>
        ///     Per-character right-edge inset, one entry per row. Kerned records only.
        /// </summary>
        /// <remarks>
        ///     <c>is_36_</c>, read second (<c>Class197.java:58-69</c>), and consulted for the
        ///     <b>left</b>-hand character of a pair (<c>is_3_[i_1_]</c>). Same delta encoding, same
        ///     row counts.
        /// </remarks>
        public byte[][] RightEdgeProfiles => rightEdgeProfiles;

        /// <summary>
        ///     Reads a font record, choosing the layout by the flag the record itself carries.
        /// </summary>
        /// <remarks>
        ///     Refuses a version the client refuses (<c>Class197.java:22-26</c>) rather than
        ///     carrying on, because every offset after that byte belongs to a format this decoder
        ///     has never seen.
        /// </remarks>
        /// <param name="stream">The stored file, positioned at its start.</param>
        /// <param name="xteaKey">Unused. Index 13 carries no encrypted group in either cache.</param>
        /// <exception cref="InvalidDataException">The version byte is one the client would throw on.</exception>
        public void Decode(JagStream stream, int[]? xteaKey = null) {
            if (stream == null)
                throw new ArgumentNullException(nameof(stream));

            Version = (byte) stream.ReadUnsignedByte();
            if (Version != SupportedVersion)
                throw new InvalidDataException(
                    "Font " + Id + " declares version " + Version + "; Class197.java:22-26 throws for " +
                    "anything but " + SupportedVersion + ", so the rest of the record is a layout " +
                    "this decoder has never seen.");

            KerningFlag = (byte) stream.ReadUnsignedByte();
            advanceWidths = stream.ReadBytes(CharacterCount);

            if (IsKerned) {
                glyphRows = stream.ReadBytes(CharacterCount);
                glyphTops = stream.ReadBytes(CharacterCount);
                leftEdgeProfiles = ReadProfiles(stream, glyphRows);
                rightEdgeProfiles = ReadProfiles(stream, glyphRows);
                StoredLineHeight = 0;
            } else {
                glyphRows = new byte[CharacterCount];
                glyphTops = new byte[CharacterCount];
                leftEdgeProfiles = EmptyProfiles();
                rightEdgeProfiles = EmptyProfiles();
                StoredLineHeight = (byte) stream.ReadUnsignedByte();
            }

            UnusedByte259 = (byte) stream.ReadUnsignedByte();
            UnusedByte260 = (byte) stream.ReadUnsignedByte();
            Ascent = (byte) stream.ReadUnsignedByte();
            Descent = (byte) stream.ReadUnsignedByte();

            //Derived from the tables just read, so it cannot survive a change to them.
            kerning = null;
        }

        /// <summary>
        ///     Writes the record back in the shape it was read in.
        /// </summary>
        /// <remarks>
        ///     Nothing is recomputed. The kerning flag goes out as its stored byte, the line height
        ///     is written only on the layout that has a slot for it, and the profile deltas are
        ///     re-derived from the decoded values - which is exact, because for a given predecessor
        ///     exactly one signed byte reaches a given successor in eight-bit arithmetic.
        /// </remarks>
        /// <returns>The bytes to store, ready to read.</returns>
        public JagStream Encode() {
            var stream = new JagStream(IsKerned ? UnkernedLength * 4 : UnkernedLength);

            stream.WriteByte(Version);
            stream.WriteByte(KerningFlag);
            stream.Write(advanceWidths, 0, advanceWidths.Length);

            if (IsKerned) {
                stream.Write(glyphRows, 0, glyphRows.Length);
                stream.Write(glyphTops, 0, glyphTops.Length);
                WriteProfiles(stream, leftEdgeProfiles);
                WriteProfiles(stream, rightEdgeProfiles);
            } else {
                stream.WriteByte(StoredLineHeight);
            }

            stream.WriteByte(UnusedByte259);
            stream.WriteByte(UnusedByte260);
            stream.WriteByte(Ascent);
            stream.WriteByte(Descent);

            return stream.Flip();
        }

        /// <summary>
        ///     Reads a font's metrics out of an open cache.
        /// </summary>
        /// <remarks>
        ///     By group id, which is the font id: <c>Class119_Sub1.java:42</c> fetches the record
        ///     with the single-file accessor <c>JS5Archive.method2733</c>, so the file id is
        ///     whatever the reference table declares for that group rather than an assumed 0.
        /// </remarks>
        /// <param name="cache">The open cache.</param>
        /// <param name="fontId">The font id, which is the index-13 group id.</param>
        /// <returns>The decoded metrics.</returns>
        /// <exception cref="FileNotFoundException">The index declares no such font.</exception>
        public static FontDefinition Load(RSCache cache, int fontId) {
            if (cache == null)
                throw new ArgumentNullException(nameof(cache));

            RSArchiveEntry entry = cache.GetReferenceTable(RSConstants.FONTS_INDEX).GetArchiveEntry(fontId);
            if (entry == null)
                throw new FileNotFoundException(
                    "Index " + RSConstants.FONTS_INDEX + " declares no font " + fontId + ".");

            int[] fileIds = entry.GetValidFileIds();
            if (fileIds.Length != 1)
                throw new FileNotFoundException(
                    "Index " + RSConstants.FONTS_INDEX + " group " + fontId + " declares " +
                    fileIds.Length + " files; Class119_Sub1.java:42 reads it through the single-file " +
                    "accessor, which throws for anything else.");

            var font = new FontDefinition { Id = fontId };
            font.Decode(new JagStream(cache.ReadFileBytes(RSConstants.FONTS_INDEX, fontId, fileIds[0])));
            return font;
        }

        /// <summary>
        ///     The kerning matrix the client derives, <c>[left, right]</c>.
        /// </summary>
        /// <remarks>
        ///     Derived state, rebuilt on demand and never encoded - the record stores the edge
        ///     profiles it is computed from, not the matrix. An unkerned font has no matrix at all
        ///     (<c>aByteArrayArray1516</c> stays null and every reader checks it, e.g.
        ///     <c>Class197.java:151,249</c>), so this answers null rather than a zero-filled table
        ///     that would read as "kerns to nothing".
        /// </remarks>
        /// <returns>The 256x256 matrix, or <c>null</c> when the font is unkerned.</returns>
        public sbyte[,]? KerningMatrix() {
            if (!IsKerned)
                return null;
            return kerning ??= BuildKerningMatrix();
        }

        /// <summary>
        ///     The advance width of a character, read the way the client reads it.
        /// </summary>
        /// <param name="character">The character code, 0..255.</param>
        /// <returns>The advance in pixels.</returns>
        public int AdvanceOf(int character) {
            if (character < 0 || character >= CharacterCount)
                throw new ArgumentOutOfRangeException(nameof(character), character,
                    "A font covers character codes 0.." + (CharacterCount - 1) + ".");
            return advanceWidths[character] & 0xFF;
        }

        /// <summary>
        ///     Sets a character's advance width.
        /// </summary>
        /// <param name="character">The character code, 0..255.</param>
        /// <param name="advance">The advance in pixels, 0..255.</param>
        public void SetAdvance(int character, int advance) {
            if (character < 0 || character >= CharacterCount)
                throw new ArgumentOutOfRangeException(nameof(character), character,
                    "A font covers character codes 0.." + (CharacterCount - 1) + ".");
            if (advance < 0 || advance > 255)
                throw new ArgumentOutOfRangeException(nameof(advance), advance,
                    "An advance width is a single stored byte.");

            advanceWidths[character] = (byte) advance;
            //The matrix is capped by the two advances (Class378.method4003:55-57), so it moves.
            kerning = null;
        }

        /// <summary>
        ///     Ports <c>Class378.method4003</c> over every pair, exactly as <c>Class197.java:71-82</c>
        ///     drives it.
        /// </summary>
        /// <remarks>
        ///     A literal port rather than a tidier equivalent, for the reason the Huffman codeword
        ///     assignment is one: a substitution that looked right would still agree with itself and
        ///     disagree with the client on screen, and nothing in the cache could tell the two apart.
        ///     Space and no-break space are skipped on both axes (<c>:74,76</c>) and stay at zero.
        /// </remarks>
        /// <returns>The matrix, indexed <c>[left, right]</c>.</returns>
        private sbyte[,] BuildKerningMatrix() {
            var matrix = new sbyte[CharacterCount, CharacterCount];

            for (int left = 0; left < CharacterCount; left++) {
                if (left == SpaceCharacter || left == NoBreakSpaceCharacter)
                    continue;

                for (int right = 0; right < CharacterCount; right++) {
                    if (right == SpaceCharacter || right == NoBreakSpaceCharacter)
                        continue;
                    matrix[left, right] = unchecked((sbyte) KernOf(left, right));
                }
            }

            return matrix;
        }

        /// <summary>
        ///     How far a pair may close up, as <c>Class378.method4003:43-69</c> computes it.
        /// </summary>
        /// <remarks>
        ///     The clearance between the two glyphs is the sum of the left character's right-edge
        ///     inset and the right character's left-edge inset, taken row by row over the rows their
        ///     boxes share, and capped by the smaller of the two advance widths. The kern is its
        ///     negation, so a positive clearance pulls the pair together.
        ///     <para>
        ///     When the boxes do not overlap at all the loop never runs and the cap stands, which is
        ///     what makes an empty overlap kern by a whole advance width. That is the client's
        ///     behaviour, not an oversight here.
        ///     </para>
        /// </remarks>
        /// <param name="left">The left character code.</param>
        /// <param name="right">The right character code.</param>
        /// <returns>The kerning adjustment before the byte truncation the client applies.</returns>
        private int KernOf(int left, int right) {
            int leftTop = glyphTops[left] & 0xFF;
            int leftBottom = leftTop + (glyphRows[left] & 0xFF);
            int rightTop = glyphTops[right] & 0xFF;
            int rightBottom = rightTop + (glyphRows[right] & 0xFF);

            int from = Math.Max(leftTop, rightTop);
            int to = Math.Min(leftBottom, rightBottom);

            int clearance = Math.Min(advanceWidths[left] & 0xFF, advanceWidths[right] & 0xFF);

            byte[] leftEdge = rightEdgeProfiles[left];
            byte[] rightEdge = leftEdgeProfiles[right];

            for (int row = from; row < to; row++) {
                int gap = unchecked((sbyte) rightEdge[row - rightTop]) +
                          unchecked((sbyte) leftEdge[row - leftTop]);
                if (gap < clearance)
                    clearance = gap;
            }

            return -clearance;
        }

        /// <summary>
        ///     Reads one variable-length profile block: per character, <c>rows</c> delta-encoded
        ///     signed bytes.
        /// </summary>
        /// <remarks>
        ///     The accumulator is a signed byte and wraps in eight bits
        ///     (<c>Class197.java:50-55</c> declares <c>byte i_34_</c>), so a run of deltas that
        ///     overflows is legal and reproducible rather than an error.
        /// </remarks>
        /// <param name="stream">The record, positioned at the block.</param>
        /// <param name="rows">Rows per character.</param>
        /// <returns>The decoded profiles, indexed by character.</returns>
        private static byte[][] ReadProfiles(JagStream stream, byte[] rows) {
            var profiles = new byte[CharacterCount][];

            for (int character = 0; character < CharacterCount; character++) {
                var profile = new byte[rows[character] & 0xFF];
                sbyte running = 0;

                for (int row = 0; row < profile.Length; row++) {
                    running = unchecked((sbyte) (running + stream.ReadSignedByte()));
                    profile[row] = unchecked((byte) running);
                }

                profiles[character] = profile;
            }

            return profiles;
        }

        /// <summary>
        ///     Writes a profile block back as the deltas it was read as.
        /// </summary>
        /// <remarks>
        ///     Exact rather than approximate: for a given predecessor exactly one signed byte
        ///     reaches a given successor in eight-bit arithmetic, so the deltas are recovered
        ///     uniquely and the block re-encodes byte for byte.
        /// </remarks>
        /// <param name="stream">The stream to append to.</param>
        /// <param name="profiles">The decoded profiles, indexed by character.</param>
        private static void WriteProfiles(JagStream stream, byte[][] profiles) {
            foreach (byte[] profile in profiles) {
                sbyte running = 0;

                foreach (byte value in profile) {
                    sbyte target = unchecked((sbyte) value);
                    stream.WriteSignedByte(unchecked((sbyte) (target - running)));
                    running = target;
                }
            }
        }

        /// <summary>An empty profile per character, which is what an unkerned record has.</summary>
        /// <returns>256 zero-length arrays.</returns>
        private static byte[][] EmptyProfiles() {
            var profiles = new byte[CharacterCount][];
            for (int character = 0; character < CharacterCount; character++)
                profiles[character] = Array.Empty<byte>();
            return profiles;
        }
    }
}
