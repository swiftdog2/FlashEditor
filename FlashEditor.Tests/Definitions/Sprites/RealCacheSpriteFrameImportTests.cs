using System;
using System.Collections.Generic;
using System.Drawing;
using FlashEditor.Cache;
using FlashEditor.Definitions.Sprites;
using FlashEditor.Cache.Util;
using FlashEditor.Tests.Cache.RealCache;
using Xunit;
using Xunit.Abstractions;
using static FlashEditor.Tests.Definitions.Sprites.SpritePictures;
using FlashEditor.IO;

namespace FlashEditor.Tests.Definitions.Sprites
{
    /// <summary>
    ///     Replaces one frame of every multi-frame set the cache holds, and requires the rest of the
    ///     file to come back byte for byte.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///     <see cref="SpriteFrameImportTests"/> makes the same claim against hand-built sets, which is
    ///     where the exact expected bytes can be worked out from the format. This makes it against the
    ///     real ones, and the two are not the same test: a fixture holds the stored oddities somebody
    ///     thought to write down, while the shipped sets hold whichever ones the packer actually
    ///     emitted - column-major planes, alpha planes that leave everything opaque, palette entries
    ///     spelled as black, frames placed away from the origin, and the three unread bytes thirteen
    ///     repack groups keep between their last plane and their palette.
    ///     </para>
    ///     <para>
    ///     The assertion is deliberately the whole file rather than a frame list. Everything before the
    ///     replaced frame's span and everything after it must be identical, which covers the other
    ///     frames, the palette, the per-frame metadata arrays and the trailer in one statement, and
    ///     cannot be satisfied by a decoder and an encoder agreeing with each other about something
    ///     wrong.
    ///     </para>
    ///     <para>
    ///     The cache is only read. Everything here is done to bytes in memory.
    ///     </para>
    /// </remarks>
    [Collection("RealCache")]
    public sealed class RealCacheSpriteFrameImportTests : IClassFixture<RealCacheFixture>
    {
        private readonly RealCacheFixture _fixture;
        private readonly ITestOutputHelper _output;

        /// <summary>Binds the shared open cache and the per-test output sink.</summary>
        public RealCacheSpriteFrameImportTests(RealCacheFixture fixture, ITestOutputHelper output)
        {
            _fixture = fixture;
            _output = output;
        }

        /// <summary>
        ///     Replacing a frame of a shipped set changes that frame's bytes and nothing else.
        /// </summary>
        /// <remarks>
        ///     A colour the set's own palette already holds is used, so nothing can be attributed to
        ///     quantisation: under
        ///     <see cref="SpriteSetPalettePolicy.KeepExistingFrames"/> the palette must come back
        ///     entry for entry and no other frame may move. The replaced frame keeps its geometry too,
        ///     so a file whose replaced frame carried no alpha plane comes back exactly as long as it
        ///     went in.
        /// </remarks>
        [RealCacheFact]
        public void ReplacingAFrameOfEveryMultiFrameSet_LeavesTheRestOfTheFileIdentical()
        {
            var failures = new List<string>();
            int multiFrameSets = 0;
            int exercised = 0;
            int notReplaceable = 0;

            DefinitionSweepResult swept = Sweep().ForEachDecoded((record, sprite) =>
            {
                if (sprite.Frames.Count <= 1)
                    return;

                multiFrameSets++;

                int frameId = ReplaceableFrame(sprite);
                if (frameId < 0)
                {
                    //A set whose every frame is empty or reaches outside its own canvas. Eleven such
                    //frames exist, all in one repack group, and the importer refuses them by design.
                    notReplaceable++;
                    return;
                }

                SpriteFrame displaced = sprite.Frames[frameId];
                int colour = sprite.PaletteStored.Length > 1
                    ? unchecked((int) 0xFF000000) | sprite.RenderPalette[1]
                    : 0;

                try
                {
                    using Bitmap picture = Flat(displaced.SubWidth, displaced.SubHeight, colour);
                    SpriteFrameImport imported = SpriteImageImporter.ReplaceFrame(sprite, frameId, picture);

                    if (imported.PaletteEntriesAdded != 0)
                    {
                        failures.Add($"set {record.Id}: a colour taken from the set's own palette added " +
                                     $"{imported.PaletteEntriesAdded} entries");
                    }

                    byte[] now = imported.Set.Encode().ToArray();
                    string? difference = Difference(record.Bytes, now, sprite, imported.Set, frameId);
                    if (difference != null)
                        failures.Add($"set {record.Id} frame {frameId}: {difference}");

                    exercised++;
                }
                catch (Exception ex)
                {
                    failures.Add($"set {record.Id} frame {frameId}: {ex.GetType().Name}: {ex.Message}");
                }
            });

            _output.WriteLine($"{exercised} of {multiFrameSets} multi-frame sets had a frame replaced; " +
                              $"{notReplaceable} hold no frame that can be replaced");

            Assert.Empty(failures);
            Assert.Equal(DeclaredSets, swept.Records);
            _fixture.Profile.AssertCensus(_output, "sprite.multiFrameSets", multiFrameSets);
            Assert.True(exercised > 0, "no multi-frame set was exercised, so nothing above checked anything");
        }

        /// <summary>
        ///     Requantising a shipped set keeps every untouched frame's flags, geometry and picture,
        ///     while its entry numbers move.
        /// </summary>
        /// <remarks>
        ///     The policy that does rewrite frames nobody edited, held to what it must not change. The
        ///     drawn colour of every pixel of every untouched frame has to survive, transparency
        ///     included, or a policy sold as "better colour" is losing artwork; and the stored flags
        ///     have to survive, or the traversal order of a shipped column-major frame is being
        ///     recomputed by the back door.
        /// </remarks>
        [RealCacheFact]
        public void RequantisingAShippedSet_KeepsEveryUntouchedFramesPictureAndFlags()
        {
            var failures = new List<string>();
            int exercised = 0;
            long framesCompared = 0;

            Sweep().ForEachDecoded((record, sprite) =>
            {
                if (sprite.Frames.Count <= 1)
                    return;

                int frameId = ReplaceableFrame(sprite);
                if (frameId < 0)
                    return;

                SpriteFrame displaced = sprite.Frames[frameId];
                int colour = sprite.PaletteStored.Length > 1
                    ? unchecked((int) 0xFF000000) | sprite.RenderPalette[1]
                    : 0;

                try
                {
                    using Bitmap picture = Flat(displaced.SubWidth, displaced.SubHeight, colour);
                    SpriteDefinition after = SpriteImageImporter.ReplaceFrame(sprite, frameId, picture,
                        SpriteFrameAnchor.KeepOffset, SpriteSetPalettePolicy.RequantiseWholeSet).Set;

                    for (int id = 0; id < sprite.Frames.Count; id++)
                    {
                        if (id == frameId)
                            continue;

                        SpriteFrame was = sprite.Frames[id];
                        SpriteFrame now = after.Frames[id];
                        framesCompared++;

                        if (was.Flags != now.Flags || was.OffsetX != now.OffsetX || was.OffsetY != now.OffsetY ||
                            was.SubWidth != now.SubWidth || was.SubHeight != now.SubHeight)
                        {
                            failures.Add($"set {record.Id} frame {id}: flags or geometry changed");
                            continue;
                        }

                        for (int at = 0; at < was.PaletteIndices.Length; at++)
                        {
                            int drewBefore = sprite.RenderPalette[was.PaletteIndices[at]];
                            int drawsNow = after.RenderPalette[now.PaletteIndices[at]];
                            if (drewBefore == drawsNow)
                                continue;

                            failures.Add($"set {record.Id} frame {id} pixel {at}: drew {drewBefore:X6}, " +
                                         $"now draws {drawsNow:X6}");
                            break;
                        }
                    }

                    exercised++;
                }
                catch (Exception ex)
                {
                    failures.Add($"set {record.Id} frame {frameId}: {ex.GetType().Name}: {ex.Message}");
                }
            });

            _output.WriteLine($"{exercised} sets requantised, {framesCompared} untouched frames compared pixel " +
                              "for pixel");

            Assert.Empty(failures);
            Assert.True(exercised > 0, "no multi-frame set was exercised, so nothing above checked anything");
        }

        /// <summary>Sprite sets the reference table declares, one per group.</summary>
        private int DeclaredSets => _fixture.DeclaredGroups(RSConstants.SPRITES_INDEX);

        /// <summary>The sprite index bound to the production codec, across every declared group.</summary>
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
        ///     A frame of the set that a picture can actually replace, preferring one in the middle.
        /// </summary>
        /// <remarks>
        ///     A frame of no area cannot be described by a picture, and a frame already reaching
        ///     outside its own canvas cannot be placed back where it is - the importer refuses that
        ///     rather than writing something the client would throw on. The middle is preferred so the
        ///     assertion has frames on both sides of the one that changed, which is what catches a
        ///     writer that keeps the frames before the edit and loses the ones after it.
        /// </remarks>
        /// <param name="sprite">The set.</param>
        /// <returns>The frame id, or -1 when the set holds none.</returns>
        private static int ReplaceableFrame(SpriteDefinition sprite)
        {
            int middle = sprite.Frames.Count / 2;
            for (int step = 0; step < sprite.Frames.Count; step++)
            {
                int id = (middle + step) % sprite.Frames.Count;
                SpriteFrame frame = sprite.Frames[id];
                if (frame.Area > 0 && !sprite.Overflows(frame))
                    return id;
            }

            return -1;
        }

        /// <summary>
        ///     Compares two encoded sets outside the span of one frame, and says where they part.
        /// </summary>
        /// <remarks>
        ///     The planes run from offset 0 in frame order with no length field of their own, so one
        ///     frame's span is the sum of the stored lengths before it. Everything either side of that
        ///     span - every other frame, the palette, the four metadata arrays, the frame count and
        ///     any unread gap - has to be identical, and comparing it as two runs of bytes rather than
        ///     as decoded fields is what keeps the check independent of the decoder.
        /// </remarks>
        /// <param name="was">The stored bytes.</param>
        /// <param name="now">The bytes the import would store.</param>
        /// <param name="before">The set as decoded.</param>
        /// <param name="after">The set as rebuilt.</param>
        /// <param name="frameId">The frame that was replaced.</param>
        /// <returns>What differs, or null when only the frame's own span did.</returns>
        private static string? Difference(byte[] was, byte[] now, SpriteDefinition before, SpriteDefinition after,
                                          int frameId)
        {
            int start = 0;
            for (int id = 0; id < frameId; id++)
            {
                if (before.Frames[id].StoredLength != after.Frames[id].StoredLength)
                    return $"frame {id} changed length, {before.Frames[id].StoredLength} to {after.Frames[id].StoredLength}";
                start += before.Frames[id].StoredLength;
            }

            int wasLength = before.Frames[frameId].StoredLength;
            int nowLength = after.Frames[frameId].StoredLength;

            if (was.Length - wasLength != now.Length - nowLength)
            {
                return $"the file changed by {now.Length - was.Length} bytes while the frame changed by " +
                       $"{nowLength - wasLength}";
            }

            if (!was.AsSpan(0, start).SequenceEqual(now.AsSpan(0, start)))
                return $"the {start} bytes of frames before it differ";

            int wasTail = start + wasLength;
            int nowTail = start + nowLength;
            return was.AsSpan(wasTail).SequenceEqual(now.AsSpan(nowTail))
                ? null
                : $"the {was.Length - wasTail} bytes after it differ";
        }
    }
}
