using System;
using System.Collections.Generic;
using System.Linq;
using FlashEditor.Cache;
using FlashEditor.Definitions.Sprites;
using FlashEditor.Cache.Util;
using FlashEditor.Tests.Cache.RealCache;
using Xunit;
using Xunit.Abstractions;
using FlashEditor.IO;

namespace FlashEditor.Tests.Definitions.Sprites
{
    /// <summary>
    ///     Decodes every sprite set in index 8 and requires each one to re-encode to the bytes it
    ///     came from.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///     Index 8 is read backwards from the end of the file - the frame count is the last two
    ///     bytes and everything else is positioned relative to it - so the harness's padded-decode
    ///     check cannot be used here: appending a sentinel moves the end of the file and the decode
    ///     then reads the padding as a frame count. Consumption is asserted instead by
    ///     <see cref="EverySpriteSet_AccountsForEveryStoredByte"/>, which holds the offset the
    ///     decoder actually stopped at against the length the frame metadata implies. That is the
    ///     same claim from two independent directions, which is what the padding buys elsewhere.
    ///     </para>
    ///     <para>
    ///     Byte identity is the sharp instrument on this index because the format is not canonical
    ///     in four separate places: a colour stored as black, an alpha plane that leaves everything
    ///     opaque, the traversal flag on a frame too thin to have one, and a packer's unread bytes
    ///     between the planes and the palette. Every one of them decodes to a picture that spells
    ///     back differently, so a decoder that kept only the pixels would rewrite files nobody
    ///     touched.
    ///     </para>
    /// </remarks>
    [Collection("RealCache")]
    public sealed class RealCacheSpriteTests : IClassFixture<RealCacheFixture>
    {
        private readonly RealCacheFixture _fixture;
        private readonly ITestOutputHelper _output;

        /// <summary>Binds the shared open cache and the per-test output sink.</summary>
        public RealCacheSpriteTests(RealCacheFixture fixture, ITestOutputHelper output)
        {
            _fixture = fixture;
            _output = output;
        }

        /// <summary>Sprite sets the reference table declares, one per group.</summary>
        private int DeclaredSets => _fixture.DeclaredGroups(RSConstants.SPRITES_INDEX);

        /// <summary>
        ///     The sprite index bound to the production codec, across every declared group.
        /// </summary>
        /// <remarks>
        ///     Every group rather than the 250-group sample: the assertions below are statements
        ///     about the whole index and a sample cannot make them. The whole payload is under
        ///     fifteen megabytes decompressed, and nothing is rasterised during a decode, so the
        ///     cost is the inflate.
        /// </remarks>
        /// <returns>A sweep over every sprite set the cache declares.</returns>
        private DefinitionSweep<SpriteDefinition> Sweep()
        {
            return new DefinitionSweep<SpriteDefinition>(_fixture, _output, RSConstants.SPRITES_INDEX,
                new DefinitionCodec<SpriteDefinition>("sprite set", DecodeSet, sprite => sprite.Encode()))
                .AcrossEveryGroup()
                .NotOpcodeTerminated();
        }

        /// <summary>Decodes one set, carrying the group id onto it the way the editor does.</summary>
        /// <param name="definitionId">The group id, which is the sprite id on this index.</param>
        /// <param name="stream">The stored bytes.</param>
        /// <returns>The decoded set.</returns>
        private static SpriteDefinition DecodeSet(int definitionId, JagStream stream)
        {
            var sprite = new SpriteDefinition();
            sprite.Decode(stream);
            sprite.SetIndex(definitionId);
            return sprite;
        }

        /// <summary>
        ///     Index 8 addresses one file per group, so a sprite id is a group id.
        /// </summary>
        /// <remarks>
        ///     The whole read path depends on it: <c>RSCache.GetSprite</c> hands the raw group
        ///     container to the decoder without unpacking it, which is right only while a group
        ///     holds exactly one file. A second file would put the multi-file size table where the
        ///     sprite metadata is read from, and the decode would produce nonsense rather than fail.
        /// </remarks>
        [RealCacheFact]
        public void TheSpriteIndex_HoldsExactlyOneFilePerGroup()
        {
            Assert.Equal(CacheIdShape.GroupPerId, CacheAddressing.For(RSConstants.SPRITES_INDEX).Shape);

            RSReferenceTable table = _fixture.Table(RSConstants.SPRITES_INDEX);
            var wrong = new List<string>();

            foreach (KeyValuePair<int, RSArchiveEntry> group in table.GetArchiveEntries())
            {
                int[] files = group.Value.GetValidFileIds();
                if (files.Length != 1 || files[0] != 0)
                    wrong.Add($"group {group.Key} declares files [{string.Join(" ", files)}]");
            }

            Assert.Empty(wrong);
            Assert.Equal(DeclaredSets, _fixture.DeclaredFiles(RSConstants.SPRITES_INDEX));
            Assert.True(DeclaredSets > 0, "index 8 declares no groups, so nothing below checked anything");
        }

        /// <summary>Every sprite set re-encodes to the bytes it was decoded from.</summary>
        /// <remarks>
        ///     The editor rewrites a set through the encoder on every save and the archive CRC
        ///     covers the stored bytes, so anything the encoder normalises changes the reference
        ///     table entry of every archive packed alongside it.
        /// </remarks>
        [RealCacheFact]
        public void EverySpriteSet_ReEncodesToTheCapturedBytes()
        {
            DefinitionSweepResult swept = Sweep().AssertReEncodesToCapturedBytes();

            Assert.Equal(DeclaredSets, swept.Records);
            Assert.Equal(DeclaredSets, swept.Groups);
            Assert.Equal(DeclaredSets, swept.Passed);
        }

        /// <summary>
        ///     Every byte of every sprite set is accounted for by exactly one decoded field.
        /// </summary>
        /// <remarks>
        ///     The format states no length for the pixel planes: they run forwards from offset 0
        ///     while the palette is located by seeking back from the end of the file, and nothing
        ///     joins the two. So the offset the decoder stops at is compared against the length the
        ///     frame metadata implies - two figures produced by different code that have to agree -
        ///     and any bytes left between them are counted rather than passed over. Thirteen groups
        ///     in the repack really do leave three, which is why the decoder captures them and why
        ///     the figure is scoped to the cache instead of asserted at zero.
        /// </remarks>
        [RealCacheFact]
        public void EverySpriteSet_AccountsForEveryStoredByte()
        {
            var failures = new List<string>();
            int setsWithATrailer = 0;
            long trailerBytes = 0;

            DefinitionSweepResult swept = Sweep().ForEachDecoded((record, sprite) =>
            {
                long fromMetadata = sprite.Frames.Sum(frame => (long) frame.StoredLength);
                if (sprite.PixelPlaneEnd != fromMetadata)
                {
                    failures.Add($"set {record.Id}: the decoder stopped at {sprite.PixelPlaneEnd} but its " +
                                 $"{sprite.Frames.Count} frames describe {fromMetadata} bytes of planes");
                }

                if (sprite.StoredLength != record.Bytes.Length)
                    failures.Add($"set {record.Id}: decoded from {record.Bytes.Length} bytes, recorded {sprite.StoredLength}");

                long expected = sprite.PixelPlaneEnd + sprite.PixelPlaneTrailer.Length
                                + (long) (sprite.PaletteStored.Length - 1) * 3 + 7 + 8L * sprite.Frames.Count;
                if (expected != record.Bytes.Length)
                {
                    failures.Add($"set {record.Id}: the fields account for {expected} bytes of a stored " +
                                 record.Bytes.Length);
                }

                if (sprite.PixelPlaneTrailer.Length > 0)
                {
                    setsWithATrailer++;
                    trailerBytes += sprite.PixelPlaneTrailer.Length;
                }
            });

            _output.WriteLine($"{setsWithATrailer} sets leave {trailerBytes} bytes unread between the last " +
                              "pixel plane and the palette");

            Assert.Empty(failures);
            Assert.Equal(DeclaredSets, swept.Records);
            _fixture.Profile.AssertCensus(_output, "sprite.setsWithAPixelPlaneTrailer", setsWithATrailer);
            _fixture.Profile.AssertCensus(_output, "sprite.pixelPlaneTrailerBytes", trailerBytes);
        }

        /// <summary>
        ///     Whatever the encoder writes, its own decoder reads back and writes out again
        ///     unchanged.
        /// </summary>
        /// <remarks>
        ///     Independent of byte identity against the cache, and the property the save path leans
        ///     on once a set has actually been edited: a plane written in one traversal and read
        ///     back in the other would survive a comparison against the cache and still corrupt the
        ///     first edited sprite.
        /// </remarks>
        [RealCacheFact]
        public void EverySpriteSet_EncodeIsAFixedPointOfDecode()
        {
            var failures = new List<string>();

            DefinitionSweepResult swept = Sweep().ForEachDecoded((record, sprite) =>
            {
                try
                {
                    byte[] first = sprite.Encode().ToArray();
                    byte[] second = DecodeSet(record.Id, new JagStream(first)).Encode().ToArray();

                    if (!first.AsSpan().SequenceEqual(second))
                    {
                        failures.Add($"set {record.Id}: {first.Length} encoded bytes re-encoded to " +
                                     $"{second.Length} differently");
                    }
                }
                catch (Exception ex)
                {
                    failures.Add($"set {record.Id}: the encoder's own output would not decode - " +
                                 $"{ex.GetType().Name}: {ex.Message}");
                }
            });

            Assert.Empty(failures);
            Assert.Equal(DeclaredSets, swept.Records);
        }

        /// <summary>
        ///     What index 8 actually contains, so the codec's coverage is stated rather than assumed.
        /// </summary>
        /// <remarks>
        ///     Three of these decide whether the non-canonical branches are defended by the sweep at
        ///     all, and they do not agree with each other:
        ///     <list type="bullet">
        ///     <item>A palette entry stored as black is <b>live</b> in both caches, and so is the
        ///     0x000001 it decodes to, so the sweep would catch an encoder that recomputed either.</item>
        ///     <item>A redundant alpha plane is <b>live</b> in both caches, so dropping one the way
        ///     the client does would shorten real files and fail the byte-identity sweep.</item>
        ///     <item>A frame whose traversal order cannot be recovered from its bytes is common, but
        ///     <b>every one of them stores the flag clear</b>. So the divergent case is latent: an
        ///     encoder that assumed row-major on an ambiguous frame sweeps both caches clean.
        ///     <c>SpriteDefinitionCodecTests</c> is the only thing that catches it.</item>
        ///     </list>
        /// </remarks>
        [RealCacheFact]
        public void TheSpriteIndex_HoldsWhatTheCodecClaimsItDoes()
        {
            var flagBytes = new SortedDictionary<int, int>();
            int frames = 0;
            int multiFrameSets = 0;
            int framesWithUnknownFlagBits = 0;
            int framesWithZeroArea = 0;
            int framesWithAnAlphaPlane = 0;
            int framesWithARedundantAlphaPlane = 0;
            int framesWhoseOrderIsUnrecoverable = 0;
            int framesStoredColumnMajorWithAnUnrecoverableOrder = 0;
            int paletteEntriesStoredAsBlack = 0;
            int paletteEntriesStoredAsOne = 0;
            int setsWithAPaletteEntryStoredAsBlack = 0;
            int setsWithAnUnreferencedPaletteEntry = 0;
            int paletteIndicesOutOfRange = 0;

            DefinitionSweepResult swept = Sweep().ForEachDecoded((record, sprite) =>
            {
                frames += sprite.Frames.Count;
                if (sprite.Frames.Count > 1)
                    multiFrameSets++;

                bool storedBlack = false;
                for (int entry = 1; entry < sprite.PaletteStored.Length; entry++)
                {
                    if (sprite.PaletteStored[entry] == 0)
                    {
                        paletteEntriesStoredAsBlack++;
                        storedBlack = true;
                    }
                    else if (sprite.PaletteStored[entry] == 1)
                    {
                        paletteEntriesStoredAsOne++;
                    }
                }
                if (storedBlack)
                    setsWithAPaletteEntryStoredAsBlack++;

                var referenced = new HashSet<int>();
                foreach (SpriteFrame frame in sprite.Frames)
                {
                    flagBytes.TryGetValue(frame.Flags, out int seen);
                    flagBytes[frame.Flags] = seen + 1;

                    if ((frame.Flags & ~(SpriteFrame.FlagVertical | SpriteFrame.FlagAlpha)) != 0)
                        framesWithUnknownFlagBits++;
                    if (frame.Area == 0)
                        framesWithZeroArea++;
                    if (frame.HasAlphaPlane)
                        framesWithAnAlphaPlane++;
                    if (frame.AlphaPlaneIsRedundant)
                        framesWithARedundantAlphaPlane++;
                    if (frame.OrderIsUnrecoverable)
                    {
                        framesWhoseOrderIsUnrecoverable++;
                        if (frame.IsColumnMajor)
                            framesStoredColumnMajorWithAnUnrecoverableOrder++;
                    }

                    foreach (byte index in frame.PaletteIndices)
                    {
                        referenced.Add(index);
                        if (index >= sprite.PaletteStored.Length)
                            paletteIndicesOutOfRange++;
                    }
                }

                for (int entry = 1; entry < sprite.PaletteStored.Length; entry++)
                {
                    if (!referenced.Contains(entry))
                    {
                        setsWithAnUnreferencedPaletteEntry++;
                        break;
                    }
                }
            });

            _output.WriteLine("flag bytes: " + string.Join(", ", flagBytes.Select(f => $"{f.Key}={f.Value}")));
            _output.WriteLine($"{framesWithAnAlphaPlane} frames carry an alpha plane, " +
                              $"{framesWithARedundantAlphaPlane} of them fully opaque");
            _output.WriteLine($"{framesWhoseOrderIsUnrecoverable} frames are one pixel wide, one pixel tall or " +
                              $"empty, of which {framesStoredColumnMajorWithAnUnrecoverableOrder} store the " +
                              "column-major flag");

            //Relationships, true of any cache. The histogram has to account for every frame, and
            //every frame has to belong to a set the table declared.
            Assert.Equal(DeclaredSets, swept.Records);
            Assert.Equal(frames, flagBytes.Values.Sum());
            Assert.True(frames > 0, "no frame was decoded, so nothing below checked anything");

            //A palette index the palette cannot hold would throw in the client, which indexes its
            //palette array with the raw byte. Nothing may tolerate one here either.
            Assert.Equal(0, paletteIndicesOutOfRange);

            _fixture.Profile.AssertCensus(_output, "sprite.frames", frames);
            _fixture.Profile.AssertCensus(_output, "sprite.multiFrameSets", multiFrameSets);
            _fixture.Profile.AssertCensus(_output, "sprite.framesWithZeroArea", framesWithZeroArea);
            _fixture.Profile.AssertCensus(_output, "sprite.framesWithUnknownFlagBits", framesWithUnknownFlagBits);
            _fixture.Profile.AssertCensus(_output, "sprite.flagByte.0", flagBytes.GetValueOrDefault(0));
            _fixture.Profile.AssertCensus(_output, "sprite.flagByte.1", flagBytes.GetValueOrDefault(1));
            _fixture.Profile.AssertCensus(_output, "sprite.flagByte.2", flagBytes.GetValueOrDefault(2));
            _fixture.Profile.AssertCensus(_output, "sprite.flagByte.3", flagBytes.GetValueOrDefault(3));
            _fixture.Profile.AssertCensus(_output, "sprite.paletteEntriesStoredAsBlack", paletteEntriesStoredAsBlack);
            _fixture.Profile.AssertCensus(_output, "sprite.setsWithAPaletteEntryStoredAsBlack",
                setsWithAPaletteEntryStoredAsBlack);
            _fixture.Profile.AssertCensus(_output, "sprite.paletteEntriesStoredAsOne", paletteEntriesStoredAsOne);
            _fixture.Profile.AssertCensus(_output, "sprite.setsWithAnUnreferencedPaletteEntry",
                setsWithAnUnreferencedPaletteEntry);
            _fixture.Profile.AssertCensus(_output, "sprite.framesWithAnAlphaPlane", framesWithAnAlphaPlane);
            _fixture.Profile.AssertCensus(_output, "sprite.framesWithARedundantAlphaPlane",
                framesWithARedundantAlphaPlane);
            _fixture.Profile.AssertCensus(_output, "sprite.framesWhoseOrderIsUnrecoverable",
                framesWhoseOrderIsUnrecoverable);
            _fixture.Profile.AssertCensus(_output, "sprite.framesStoredColumnMajorWithAnUnrecoverableOrder",
                framesStoredColumnMajorWithAnUnrecoverableOrder);
        }

        /// <summary>
        ///     Every frame in the index rasterises onto a bitmap of the size its geometry demands.
        /// </summary>
        /// <remarks>
        ///     The only coverage the render path gets. Both production callers - the map scene icons
        ///     and the texture graph's sprite sources - wrap it in a bare <c>catch</c>, so a broken
        ///     rasteriser shows up as a missing icon and fails nothing.
        ///     <para>
        ///     The overflow count is why the raster is grown to fit rather than clipped to the
        ///     stored canvas: eleven frames of one repack group reach outside the canvas their own
        ///     file declares, which the client itself would throw on. Clipping them would be a
        ///     silent edit and throwing would take the sprite list down with it.
        ///     </para>
        /// </remarks>
        [RealCacheFact]
        public void EveryFrame_RasterisesToTheSizeItsGeometryDemands()
        {
            var failures = new List<string>();
            int rasterised = 0;
            int framesOverflowingTheCanvas = 0;

            DefinitionSweepResult swept = Sweep().ForEachDecoded((record, sprite) =>
            {
                try
                {
                    List<RSBufferedImage> images = sprite.GetFrames();
                    for (int id = 0; id < sprite.Frames.Count; id++)
                    {
                        SpriteFrame frame = sprite.Frames[id];
                        if (sprite.Overflows(frame))
                            framesOverflowingTheCanvas++;

                        int wanted = Math.Max(sprite.width, frame.OffsetX + frame.SubWidth);
                        int tall = Math.Max(sprite.height, frame.OffsetY + frame.SubHeight);

                        if (images[id].GetWidth() != wanted || images[id].GetHeight() != tall)
                        {
                            failures.Add($"set {record.Id} frame {id}: rasterised " +
                                         $"{images[id].GetWidth()}x{images[id].GetHeight()}, wanted {wanted}x{tall}");
                        }
                        else
                        {
                            rasterised++;
                        }
                    }
                }
                catch (Exception ex)
                {
                    failures.Add($"set {record.Id}: rasterising threw {ex.GetType().Name}: {ex.Message}");
                }
                finally
                {
                    //A frame is a pinned pixel buffer and a GDI bitmap, and this sweep builds one
                    //for every frame in the index.
                    sprite.Dispose();
                }
            });

            _output.WriteLine($"{rasterised} frames rasterised, {framesOverflowingTheCanvas} of them reaching " +
                              "outside the canvas their file declares");

            Assert.Empty(failures);
            Assert.Equal(DeclaredSets, swept.Records);
            Assert.True(rasterised > 0, "nothing was rasterised, so the render path was not exercised");
            _fixture.Profile.AssertCensus(_output, "sprite.framesOverflowingTheCanvas", framesOverflowingTheCanvas);
        }

        /// <summary>
        ///     The bytes <c>SpriteDefinitionCodecTests</c> asserts against are still what the cache
        ///     holds.
        /// </summary>
        /// <remarks>
        ///     Without this the offline tests pin the codec to literals nobody can check, which is
        ///     the shape a hand-built test takes when it asserts a bug rather than catching one.
        ///     All five groups are byte-identical in both supported caches, so this holds whichever
        ///     one the suite is pointed at.
        /// </remarks>
        [RealCacheFact]
        public void TheCapturedFixtures_AreStillWhatTheCacheStores()
        {
            RSCache cache = _fixture.OpenCache();
            byte[][] captured = SpriteDefinitionCodecTests.CapturedGroupBytes();

            for (int i = 0; i < captured.Length; i++)
            {
                int groupId = SpriteDefinitionCodecTests.CapturedGroupIds[i];
                byte[] stored = cache.ReadFileBytes(RSConstants.SPRITES_INDEX, groupId, 0);

                Assert.Equal(captured[i], stored);
            }
        }
    }
}
