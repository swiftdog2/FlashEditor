using System;
using System.Collections.Generic;
using System.Linq;
using FlashEditor;
using FlashEditor.Cache;
using FlashEditor.Cache.Util;
using FlashEditor.Definitions.Editing;
using FlashEditor.Definitions.Fonts;
using FlashEditor.Tests.Cache.RealCache;
using Xunit;
using Xunit.Abstractions;
using FlashEditor.IO;

namespace FlashEditor.Tests.Definitions.Fonts
{
    /// <summary>
    ///     Sweeps index 13 - the font metrics - over every group its reference table declares.
    /// </summary>
    /// <remarks>
    ///     <b>Index 13 is not sprite format.</b> The worklist filed it as sprite-adjacent and put
    ///     index 8 down as a dependency for glyph preview; the dependency is real and the format
    ///     claim is not. Index 13 holds <c>Class197</c> metrics read by
    ///     <c>Class119_Sub1.method2182</c>, and no group here parses as a sprite set. The pixels are
    ///     in index 8 at the same id, which
    ///     <see cref="EveryFont_HasAGlyphSheetAtTheSameIdInIndexEight"/> checks.
    ///     <para>
    ///     Everything asserted is a relationship against what the reference table declares, so
    ///     nothing here depends on which of the two supported caches is loaded. The two agree on this
    ///     index in every respect measured - same groups, same identifiers, same payloads - but that
    ///     is a fact about them rather than about the format, so it is not written into an assertion.
    ///     </para>
    ///     <para>
    ///     <b>These tests cannot reach the kerned branch.</b> Nothing in either cache sets the flag,
    ///     which <see cref="NoFontInThisCache_SetsTheKerningFlag"/> states outright. The branch is
    ///     pinned by <c>FontDefinitionCodecTests</c> against synthetic bytes instead, because a
    ///     branch defended only by a sweep that never enters it is not defended at all.
    ///     </para>
    /// </remarks>
    [Collection("RealCache")]
    public sealed class RealCacheFontTests : IClassFixture<RealCacheFixture>
    {
        private readonly RealCacheFixture _fixture;
        private readonly ITestOutputHelper _output;

        /// <summary>Binds the shared open cache and the per-test output sink.</summary>
        public RealCacheFontTests(RealCacheFixture fixture, ITestOutputHelper output)
        {
            _fixture = fixture;
            _output = output;
        }

        /// <summary>Groups the index-13 reference table declares.</summary>
        /// <remarks>Read from the table. A count the cache states is never written down here.</remarks>
        private int GroupsDeclared => _fixture.DeclaredGroups(RSConstants.FONTS_INDEX);

        /// <summary>Files the index-13 reference table declares across every group.</summary>
        private int FilesDeclared => _fixture.DeclaredFiles(RSConstants.FONTS_INDEX);

        /// <summary>Index 13 bound to the production codec.</summary>
        /// <remarks>
        ///     <c>NotOpcodeTerminated</c> because the record is not an opcode stream - it is a fixed
        ///     table plus an optional kerning block, and its last byte is the descent. Exact
        ///     consumption still applies and is the whole statement about the layout: the file
        ///     carries no length of its own, so a decoder that read to the end of the buffer would
        ///     accept any size, and the padded decode is what refuses that.
        ///     <para>
        ///     <c>AcrossEveryGroup</c> because the index is tiny and "every font in the cache
        ///     re-encodes to its stored bytes" is not a claim a sampled run could make. The sample
        ///     cap would cover them all anyway; saying so explicitly means it still does if the cap
        ///     ever moves.
        ///     </para>
        /// </remarks>
        /// <returns>A sweep over every declared font.</returns>
        private DefinitionSweep<FontDefinition> Sweep()
        {
            return new DefinitionSweep<FontDefinition>(_fixture, _output, RSConstants.FONTS_INDEX,
                new DefinitionCodec<FontDefinition>("font", DecodeFont, font => font.Encode()))
                .AcrossEveryGroup()
                .NotOpcodeTerminated();
        }

        private static FontDefinition DecodeFont(int fontId, JagStream stream)
        {
            var font = new FontDefinition { Id = fontId };
            font.Decode(stream);
            return font;
        }

        /// <summary>Every declared font decodes and finishes on the last byte of its file.</summary>
        [RealCacheFact]
        public void EveryFont_DecodesAndConsumesItsBufferExactly()
        {
            DefinitionSweepResult swept = Sweep().AssertExactConsumption();

            Assert.True(FilesDeclared > 0, "index 13 declares no files, so nothing was checked");
            Assert.Equal(GroupsDeclared, swept.Groups);
            Assert.Equal(FilesDeclared, swept.Records);
            Assert.Equal(FilesDeclared, swept.Passed);
        }

        /// <summary>Every declared font re-encodes to the bytes it was decoded from.</summary>
        /// <remarks>
        ///     The property the editor depends on. Two fields would break it if they were modelled as
        ///     values rather than as stored bytes: the two bytes at offsets 259 and 260, which the
        ///     client reads and discards, and the kerning flag, which the client folds with
        ///     <c>== 1</c>. Only the first of the two is defended by this sweep, since every record
        ///     here stores the flag as 0 - the alias is pinned synthetically instead.
        /// </remarks>
        [RealCacheFact]
        public void EveryFont_ReEncodesToItsStoredBytes()
        {
            DefinitionSweepResult swept = Sweep().AssertReEncodesToCapturedBytes();

            Assert.True(FilesDeclared > 0, "index 13 declares no files, so nothing was checked");
            Assert.Equal(FilesDeclared, swept.Records);
            Assert.Equal(FilesDeclared, swept.Passed);
            Assert.Equal(0, swept.Reordered);
        }

        /// <summary>
        ///     Every record is exactly the length its own kerning flag implies.
        /// </summary>
        /// <remarks>
        ///     The arithmetic, not a measured number: an unkerned record is
        ///     <c>2 + 256 + 5</c> bytes, and a kerned one is <c>2 + 3 * 256 + 2 * sum(rows) + 4</c>,
        ///     because <c>Class197.java:48,61</c> size both profile blocks from the same row table.
        ///     Written to cover both arms so that a cache which did ship a kerned font would be
        ///     checked rather than skipped.
        /// </remarks>
        [RealCacheFact]
        public void EveryFont_IsTheLengthItsKerningFlagImplies()
        {
            var wrong = new List<string>();
            var lengths = new SortedDictionary<int, int>();

            DefinitionSweepResult swept = Sweep().ForEachDecoded((record, font) =>
            {
                int expected = ExpectedLength(font);
                lengths.TryGetValue(record.Bytes.Length, out int seen);
                lengths[record.Bytes.Length] = seen + 1;

                if (record.Bytes.Length != expected)
                {
                    wrong.Add($"font {record.Id}: stored {record.Bytes.Length} bytes, but a " +
                              $"{(font.IsKerned ? "kerned" : "unkerned")} record of these row counts " +
                              $"is {expected}");
                }
            });

            _output.WriteLine("record lengths: " +
                              string.Join(", ", lengths.Select(entry => $"{entry.Key}={entry.Value}")));

            Assert.Equal(FilesDeclared, swept.Records);
            Assert.Empty(wrong);
        }

        /// <summary>
        ///     No font in this cache sets the kerning flag, which is why the kerned branch is pinned
        ///     synthetically.
        /// </summary>
        /// <remarks>
        ///     A statement about the data, deliberately asserted rather than merely printed. It is
        ///     the premise the synthetic tests rest on: if a cache ever ships a kerned font, this
        ///     fails, and the failure is the notice that the sweep now covers ground the synthetic
        ///     record was standing in for.
        /// </remarks>
        [RealCacheFact]
        public void NoFontInThisCache_SetsTheKerningFlag()
        {
            var flags = new SortedDictionary<int, int>();
            var kerned = new List<int>();

            DefinitionSweepResult swept = Sweep().ForEachDecoded((record, font) =>
            {
                flags.TryGetValue(font.KerningFlag, out int seen);
                flags[font.KerningFlag] = seen + 1;
                if (font.IsKerned)
                    kerned.Add(record.Id);
            });

            _output.WriteLine("kerning flag values: " +
                              string.Join(", ", flags.Select(entry => $"{entry.Key}={entry.Value}")));

            Assert.Equal(FilesDeclared, swept.Records);
            Assert.Empty(kerned);
        }

        /// <summary>
        ///     Every font has a positive glyph box and a positive line step.
        /// </summary>
        /// <remarks>
        ///     <b>Deliberately weak, and here for what the byte-identity sweep cannot say.</b> That
        ///     sweep cannot tell a field apart from a byte it happens to copy, so a decoder whose
        ///     offsets were wholesale shifted would re-encode perfectly and hand the editor
        ///     nonsense. The two quantities the client cannot lay text out without are the glyph box
        ///     <c>ascent + descent</c> (<c>RSFont.java:942</c>) and the line step
        ///     (<c>Class197.java:171-178</c>), and a shift far enough to land either on the advance
        ///     table hits a zero entry on most fonts.
        ///     <para>
        ///     Its limits are worth stating so nobody reads more into a pass. It does not catch a
        ///     one-byte shift reliably, because the last advance entry is non-zero on most fonts,
        ///     and it does not catch ascent and descent swapped with each other at all - several
        ///     fonts store a descent of 0, so the box is unchanged by the swap, and no measurement
        ///     over this cache could separate them. That pair is settled from the client's use alone
        ///     (<c>IntegerNode.java:680,686</c>).
        ///     </para>
        /// </remarks>
        [RealCacheFact]
        public void EveryFont_HasAPositiveGlyphBoxAndLineHeight()
        {
            var degenerate = new List<string>();

            DefinitionSweepResult swept = Sweep().ForEachDecoded((record, font) =>
            {
                int box = font.Ascent + font.Descent;
                if (box <= 0 || font.LineHeight <= 0)
                    degenerate.Add($"font {record.Id}: ascent {font.Ascent}, descent {font.Descent}, " +
                                   $"line height {font.LineHeight}");
            });

            Assert.Equal(FilesDeclared, swept.Records);
            Assert.Empty(degenerate);
        }

        /// <summary>
        ///     Every font group has a glyph sheet at the same id in index 8, under the same name hash.
        /// </summary>
        /// <remarks>
        ///     This is the join the client makes: <c>Class114.java:82,89</c> passes one id <c>i</c> to
        ///     <c>Class324.method3684(spritesArchive, i)</c> and
        ///     <c>Class119_Sub1.method2182(fontsArchive, i)</c>, and
        ///     <c>InterfaceSettings.java:76,157</c> is where those two archives are opened.
        ///     <para>
        ///     Self-proving rather than merely plausible, which is the distinction <c>CLAUDE.md</c>
        ///     draws over the track-name join: the identifiers have to match, not merely the ids, so
        ///     a coincidental overlap of id ranges would not pass. Both halves are asserted, because
        ///     the id agreeing while the name does not would mean the two indexes are not the same
        ///     id space at all.
        ///     </para>
        /// </remarks>
        [RealCacheFact]
        public void EveryFont_HasAGlyphSheetAtTheSameIdInIndexEight()
        {
            RSReferenceTable fonts = _fixture.Table(RSConstants.FONTS_INDEX);
            RSReferenceTable sprites = _fixture.Table(RSConstants.SPRITES_INDEX);
            var unmatched = new List<string>();
            int joined = 0;

            foreach (KeyValuePair<int, RSArchiveEntry> font in fonts.GetArchiveEntries())
            {
                RSArchiveEntry sheet = sprites.GetArchiveEntry(font.Key);
                if (sheet == null)
                {
                    unmatched.Add($"font {font.Key} has no index-8 group");
                    continue;
                }

                if (sheet.GetIdentifier() != font.Value.GetIdentifier())
                {
                    unmatched.Add($"font {font.Key} identifier 0x{font.Value.GetIdentifier():X8} but " +
                                  $"index-8 group {font.Key} identifier 0x{sheet.GetIdentifier():X8}");
                    continue;
                }

                joined++;
            }

            _output.WriteLine($"{joined} of {GroupsDeclared} font ids carry the identical name hash in " +
                              "index 8");
            Assert.Equal(GroupsDeclared, joined);
            Assert.Empty(unmatched);
        }

        /// <summary>
        ///     Every name this project claims to have recovered hashes to a group the index declares.
        /// </summary>
        /// <remarks>
        ///     The only kind of name recovery worth shipping on this index: the cache stores
        ///     <c>hash(name)</c> and not the name, so a candidate either hashes to a declared
        ///     identifier or it is a guess. Nothing is asserted about how many groups are named -
        ///     most are not - because coverage is not correctness and a wordlist that named all
        ///     twenty-five by luck would be worse than one that names eleven provably.
        /// </remarks>
        [RealCacheFact]
        public void EveryRecoveredFontName_HashesToADeclaredGroup()
        {
            RSReferenceTable fonts = _fixture.Table(RSConstants.FONTS_INDEX);

            //Built by hand rather than with ToDictionary: two groups sharing an identifier is a
            //possible cache, and it should fail as an unrecovered name rather than as a duplicate
            //key thrown out of the test's own setup.
            var identifiers = new Dictionary<int, int>();
            foreach (KeyValuePair<int, RSArchiveEntry> entry in fonts.GetArchiveEntries())
                identifiers[entry.Value.GetIdentifier()] = entry.Key;

            var unmatched = new List<string>();
            var named = new SortedDictionary<int, string>();

            foreach (string name in FontNames.KnownNames)
            {
                int hash = NameHasher.GetNameHash(name);
                if (identifiers.TryGetValue(hash, out int groupId))
                    named[groupId] = name;
                else
                    unmatched.Add($"'{name}' hashes to 0x{hash:X8}, which no index-13 group carries");
            }

            _output.WriteLine($"{named.Count} of {GroupsDeclared} fonts are named: " +
                              string.Join(", ", named.Select(entry => $"{entry.Key}={entry.Value}")));
            Assert.Empty(unmatched);
        }

        /// <summary>
        ///     A recovered name is only ever reported for the group whose identifier it hashes to.
        /// </summary>
        /// <remarks>
        ///     The lookup goes through the identifier rather than through a written-down id, so this
        ///     also proves that a cache which renumbered its font groups would still be named
        ///     correctly. The unnamed groups answer null rather than a nearest guess.
        /// </remarks>
        [RealCacheFact]
        public void FontNames_ReportsANameOnlyWhereTheIdentifierAgrees()
        {
            RSCache cache = _fixture.OpenCache();
            RSReferenceTable fonts = _fixture.Table(RSConstants.FONTS_INDEX);
            var wrong = new List<string>();
            int named = 0;

            foreach (int fontId in fonts.GetArchiveEntries().Keys)
            {
                string name = FontNames.NameOf(cache, fontId);
                if (name == null)
                    continue;

                named++;
                int identifier = fonts.GetArchiveEntry(fontId).GetIdentifier();
                if (NameHasher.GetNameHash(name) != identifier)
                    wrong.Add($"font {fontId} named '{name}', which hashes to " +
                              $"0x{NameHasher.GetNameHash(name):X8} and not to its stored 0x{identifier:X8}");
            }

            _output.WriteLine($"{named} fonts were named; the remaining " +
                              $"{GroupsDeclared - named} carry an identifier no candidate hashes to");
            Assert.True(named > 0, "no font was named, so nothing was checked");
            Assert.Empty(wrong);
        }

        /// <summary>
        ///     <see cref="FontDefinition.Load"/> reaches every declared font through the reference
        ///     table's own file id.
        /// </summary>
        /// <remarks>
        ///     Not a duplicate of the sweep, which reads the archive directly. This is the path the
        ///     editor uses, and it is the one that would break on a group whose single file is not id
        ///     0 - the client's single-file accessor takes whatever id the table declares
        ///     (<c>JS5Archive.java:591-611</c>), which is not always 0 elsewhere in the cache.
        /// </remarks>
        [RealCacheFact]
        public void EveryFont_LoadsThroughTheCacheApi()
        {
            RSCache cache = _fixture.OpenCache();
            RSReferenceTable fonts = _fixture.Table(RSConstants.FONTS_INDEX);
            var failures = new List<string>();
            int loaded = 0;

            foreach (int fontId in fonts.GetArchiveEntries().Keys)
            {
                try
                {
                    FontDefinition font = FontDefinition.Load(cache, fontId);
                    if (font.Id != fontId)
                        failures.Add($"font {fontId} loaded with id {font.Id}");
                    else
                        loaded++;
                }
                catch (Exception ex)
                {
                    failures.Add($"font {fontId}: {ex.GetType().Name}: {ex.Message}");
                }
            }

            Assert.Equal(GroupsDeclared, loaded);
            Assert.Empty(failures);
        }

        /// <summary>
        ///     The list descriptor enumerates the whole index and each of its rows re-encodes to the
        ///     stored bytes.
        /// </summary>
        /// <remarks>
        ///     The descriptor is what a Fonts tab is driven by, and it reaches the cache by a
        ///     different route than the sweep does - <c>RSCache.EnumerateFiles</c> and
        ///     <c>ReadFileBytes</c> rather than the archive directly. A tab that listed a different
        ///     population from the one the sweep defends would be the failure this catches.
        /// </remarks>
        [RealCacheFact]
        public void TheFontListDescriptor_ListsEveryFontAndReEncodesEachRow()
        {
            RSCache cache = _fixture.OpenCache();
            var descriptor = new FontListDescriptor();
            var failures = new List<string>();
            int rows = 0;

            Assert.Equal(RSConstants.FONTS_INDEX, descriptor.IndexId);

            foreach (DefinitionAddress address in descriptor.Enumerate(cache))
            {
                byte[] stored = cache.ReadFileBytes(RSConstants.FONTS_INDEX, address.GroupId, address.FileId);
                FontListing row = descriptor.Decode(cache, address, new JagStream(stored));

                if (row.FontId != address.GroupId)
                    failures.Add($"{address} listed as font {row.FontId}");

                byte[] reencoded = descriptor.Encode(row).ToArray();
                if (!reencoded.AsSpan().SequenceEqual(stored))
                    failures.Add($"{address} re-encoded {reencoded.Length} bytes from {stored.Length}");

                rows++;
            }

            Assert.Equal(FilesDeclared, rows);
            Assert.Empty(failures);
        }

        /// <summary>
        ///     The byte length a record of these fields must occupy, per the client's read order.
        /// </summary>
        /// <param name="font">The decoded record.</param>
        /// <returns>The implied length.</returns>
        private static int ExpectedLength(FontDefinition font)
        {
            if (!font.IsKerned)
                return FontDefinition.UnkernedLength;

            int rows = 0;
            foreach (byte count in font.GlyphRows)
                rows += count;

            //Version, flag, advances, rows, tops, two profile blocks, and the four-byte tail.
            return 2 + 3 * FontDefinition.CharacterCount + 2 * rows + 4;
        }
    }
}
