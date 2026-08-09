using System;
using System.Collections.Generic;
using System.Drawing;
using FlashEditor.Definitions.Sprites;

namespace FlashEditor.Definitions.Sprites {
    /// <summary>
    ///     Where a picture is placed within the canvas of the set it is being written into.
    /// </summary>
    /// <remarks>
    ///     A frame is a sub-rectangle at an offset and it routinely does not reach the canvas edge,
    ///     so the offset is a stored field that an import has to decide. Nothing here defaults to
    ///     0,0 silently: <see cref="TopLeft"/> is that choice spelled out.
    /// </remarks>
    public enum SpriteFrameAnchor {
        /// <summary>The offset the frame being replaced already had, which moves no artwork.</summary>
        KeepOffset,

        /// <summary>The canvas origin.</summary>
        TopLeft,

        /// <summary>Centred within the canvas, rounding left and up.</summary>
        Centre
    }

    /// <summary>
    ///     What a per-frame import is allowed to do to the palette the whole set shares.
    /// </summary>
    /// <remarks>
    ///     A sprite set stores one palette for every frame in it, so a picture whose colours the
    ///     palette does not hold cannot be written without one of two things happening, and they
    ///     have different consequences for the frames the user did not touch. This is the choice
    ///     between them, made by the caller rather than inferred, because only one of them rewrites
    ///     bytes nobody asked to change.
    /// </remarks>
    public enum SpriteSetPalettePolicy {
        /// <summary>
        ///     Keep every existing entry where it is; append new colours while the palette has room
        ///     and approximate anything that still does not fit.
        /// </summary>
        /// <remarks>
        ///     The frames that were not replaced come out byte for byte as they went in, because an
        ///     entry never moves and a plane is a list of entry numbers. The cost is borne entirely
        ///     by the picture being imported: past 255 colours in the set as a whole, its colours are
        ///     mapped to the nearest entry that already exists.
        /// </remarks>
        KeepExistingFrames,

        /// <summary>
        ///     Rebuild one palette across every frame's pixels and the new picture's together, and
        ///     re-index every frame onto it.
        /// </summary>
        /// <remarks>
        ///     The picture keeps its colours as well as the format allows, and the price is that
        ///     every other frame in the set is rewritten - their pixels point at different entry
        ///     numbers afterwards, and any entry no pixel referenced is gone. Correct output, and
        ///     still a change to files the user did not edit, which is why it has to be asked for.
        /// </remarks>
        RequantiseWholeSet
    }

    /// <summary>
    ///     What writing one or more pictures into a sprite set cost, alongside the rebuilt set.
    /// </summary>
    /// <remarks>
    ///     The whole-set conversion reports through <see cref="SpriteImageImport"/>; this reports the
    ///     two things that only exist once a set is being edited around its existing frames - what
    ///     happened to the shared palette, and how many frames nobody edited were rewritten anyway.
    ///     Both are invisible in the result and both change bytes.
    /// </remarks>
    public sealed class SpriteFrameImport {
        /// <summary>The rebuilt set, in the stored form the encoder reads.</summary>
        public required SpriteDefinition Set { get; init; }

        /// <summary>
        ///     Which frame the picture replaced, or <c>null</c> when the set was assembled from
        ///     pictures rather than edited.
        /// </summary>
        public required int? ReplacedFrame { get; init; }

        /// <summary>Where the new frame sits within the canvas, and how big it is.</summary>
        /// <remarks>
        ///     Reported because the offset is a stored field the picture itself says nothing about,
        ///     so a user has no other way to see which of the placements they got.
        /// </remarks>
        public required Rectangle Placement { get; init; }

        /// <summary>Distinct storable colours the picture held, counting black and 0x000001 as one.</summary>
        public required int SourceColours { get; init; }

        /// <summary>Colours in the resulting palette, excluding the reserved entry 0.</summary>
        public required int PaletteColours { get; init; }

        /// <summary>Source colours that an entry already in the palette matched exactly.</summary>
        public required int PaletteEntriesReused { get; init; }

        /// <summary>Entries appended to the palette, which move no existing entry.</summary>
        public required int PaletteEntriesAdded { get; init; }

        /// <summary>Source colours mapped to an entry that is not the colour they asked for.</summary>
        public required int PaletteEntriesApproximated { get; init; }

        /// <summary>
        ///     The largest single-channel difference between a source colour and the entry it was
        ///     mapped to, out of 255.
        /// </summary>
        public required int WorstChannelError { get; init; }

        /// <summary>Whether the palette was rebuilt across the whole set rather than extended.</summary>
        public required bool Requantised { get; init; }

        /// <summary>
        ///     How many frames other than the replaced one came out with different stored bytes.
        /// </summary>
        /// <remarks>
        ///     The number the confirmation exists for. Under
        ///     <see cref="SpriteSetPalettePolicy.KeepExistingFrames"/> it is zero by construction and
        ///     the tests assert it byte for byte; under
        ///     <see cref="SpriteSetPalettePolicy.RequantiseWholeSet"/> it is however many frames the
        ///     re-indexing touched, and the user is told before anything is written.
        /// </remarks>
        public required int FramesRewritten { get; init; }

        /// <summary>Whether the new frame carries an alpha plane.</summary>
        public required bool CarriesAnAlphaPlane { get; init; }

        /// <summary>Pixels holding the black the format spells 0x000001.</summary>
        public required int BlackPixels { get; init; }

        /// <summary>Pixels left transparent, which are the ones addressing palette entry 0.</summary>
        public required int TransparentPixels { get; init; }

        /// <summary>A one-line summary for the status strip.</summary>
        /// <returns>The summary.</returns>
        public string Describe() {
            string where = ReplacedFrame == null
                ? $"{Set.GetFrameCount()} frames on a {Set.width}x{Set.height} canvas"
                : $"frame {ReplacedFrame} of {Set.GetFrameCount()}, {Placement.Width}x{Placement.Height} at " +
                  $"{Placement.X},{Placement.Y}";

            string palette = Requantised
                ? $"palette rebuilt to {PaletteColours} colours, {FramesRewritten} untouched frame(s) re-indexed"
                : $"{PaletteEntriesReused} colour(s) reused, {PaletteEntriesAdded} added, palette now {PaletteColours}";

            string error = PaletteEntriesApproximated > 0 || Requantised
                ? $", worst channel error {WorstChannelError}/255"
                : string.Empty;

            return $"{where}, {palette}{error}, " + (CarriesAnAlphaPlane ? "alpha plane written" : "no alpha plane");
        }
    }

    /// <summary>
    ///     The half of the picture import that writes into a set rather than over one.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///     <b>Why this exists.</b> Importing a picture used to replace the whole set, so a six frame
    ///     animated interface sprite came back as one frame and the other five were gone. Both
    ///     caches hold 44 sets of more than one frame and several of them are animated UI, so
    ///     "replace frame 3 of 6" is the ordinary case rather than the exotic one. Replacing a whole
    ///     set is still supported - it is what <see cref="SpriteImageImporter.FromImage"/> does - but
    ///     it is now a choice.
    ///     </para>
    ///     <para>
    ///     <b>The shared palette is the whole difficulty.</b> One palette serves every frame, so a
    ///     picture whose colours it does not hold forces a decision that reaches frames the user
    ///     never selected. <see cref="SpriteSetPalettePolicy"/> states the two answers. The default
    ///     everywhere in this code and in the editor is
    ///     <see cref="SpriteSetPalettePolicy.KeepExistingFrames"/>, because it is the only one that
    ///     can promise the untouched frames re-encode to the bytes they were read from, and a promise
    ///     like that is worth more than the colour accuracy it costs - the alternative silently
    ///     rewrites artwork, and an archive CRC covers the stored bytes, so a rewritten frame drags
    ///     in the reference-table entry of everything packed beside it.
    ///     </para>
    ///     <para>
    ///     <b>Stored flags are carried, not recomputed.</b> An untouched frame keeps its own flags
    ///     byte whole, which matters more here than in the whole-set case: 2,767 frames in the shipped
    ///     data are too thin for their bytes to state a traversal order and every one of them stores
    ///     the bit clear, so an encoder that recomputed the flag would sweep both caches clean and
    ///     corrupt the first set edited. The <b>replaced</b> frame keeps the stored flags of the frame
    ///     it displaces too, bar the alpha bit, which is the one bit the new picture genuinely
    ///     decides. A frame stored column-major therefore stays column-major, and any bit the client
    ///     does not read survives the edit.
    ///     </para>
    ///     <para>
    ///     <b>The canvas is not grown.</b> The client allocates exactly canvas width by canvas height
    ///     and writes at offset plus pixel, so a frame reaching past the edge is something it would
    ///     throw on. A picture that will not fit where it is being placed is refused with both sizes
    ///     named, rather than moved somewhere it does fit or quietly clipped.
    ///     </para>
    /// </remarks>
    public static partial class SpriteImageImporter {
        /// <summary>Entries a palette holds in total, the reserved entry 0 included.</summary>
        /// <remarks><c>paletteSize - 1</c> is one unsigned byte (<c>Class324.java:55</c>).</remarks>
        public const int MaxPaletteEntries = MaxColours + 1;

        /// <summary>
        ///     Replaces one frame of an existing set with a picture, rebuilding the set around it.
        /// </summary>
        /// <param name="set">The decoded set to edit. Not modified; the result is a new set.</param>
        /// <param name="frameId">Which frame the picture replaces.</param>
        /// <param name="picture">The decoded picture. Not disposed here.</param>
        /// <param name="anchor">Where the replacement sits within the canvas.</param>
        /// <param name="policy">What the import may do to the palette the frames share.</param>
        /// <returns>The rebuilt set and what the edit cost.</returns>
        /// <exception cref="ArgumentNullException">A required argument is null.</exception>
        /// <exception cref="ArgumentOutOfRangeException">The set holds no such frame.</exception>
        /// <exception cref="InvalidOperationException">The picture cannot be placed or stored.</exception>
        public static SpriteFrameImport ReplaceFrame(SpriteDefinition set, int frameId, Image picture,
                                                     SpriteFrameAnchor anchor = SpriteFrameAnchor.KeepOffset,
                                                     SpriteSetPalettePolicy policy = SpriteSetPalettePolicy.KeepExistingFrames) {
            if (set == null)
                throw new ArgumentNullException(nameof(set));
            if (picture == null)
                throw new ArgumentNullException(nameof(picture));
            if (set.Frames == null || set.Frames.Count == 0)
                throw new InvalidOperationException(
                    "That sprite set holds no decoded frames, so there is no frame to replace.");
            if (frameId < 0 || frameId >= set.Frames.Count)
                throw new ArgumentOutOfRangeException(nameof(frameId),
                    $"Frame {frameId} of a set that holds {set.Frames.Count}.");

            int[] pixels = ReadStraightArgb(picture, out int width, out int height);
            if (width <= 0 || height <= 0)
                throw new InvalidOperationException("That picture has no pixels.");

            SpriteFrame displaced = set.Frames[frameId];
            (int offsetX, int offsetY) = Place(anchor, displaced, set.width, set.height, width, height);

            //Refused rather than clipped or moved. The client sizes its raster at exactly the canvas
            //(Class324.method3686) and writes at offset plus pixel, so a frame past the edge is an
            //exception in the game; and a frame silently moved somewhere it fits is artwork the
            //editor relocated without saying so.
            if (offsetX + width > set.width || offsetY + height > set.height)
                throw new InvalidOperationException(
                    $"A {width}x{height} picture placed at {offsetX},{offsetY} reaches outside this set's " +
                    $"{set.width}x{set.height} canvas, and the client allocates exactly the canvas. Resize the " +
                    "picture, place it elsewhere, or replace the whole set to change the canvas.");

            Dictionary<int, long> wanted = Harvest(pixels, out bool needsAlphaPlane, out int transparentPixels);

            //The alpha bit is the only one the picture decides. Everything else in the byte - the
            //traversal order, and any bit the client never reads - belongs to the frame that was
            //there and is carried across, because nothing in a PNG can state either.
            int flags = (displaced.Flags & ~SpriteFrame.FlagAlpha) | (needsAlphaPlane ? SpriteFrame.FlagAlpha : 0);

            return policy == SpriteSetPalettePolicy.RequantiseWholeSet
                ? RequantiseAround(set, frameId, pixels, wanted, width, height, offsetX, offsetY, flags,
                                   needsAlphaPlane, transparentPixels)
                : ExtendAround(set, frameId, pixels, wanted, width, height, offsetX, offsetY, flags,
                               needsAlphaPlane, transparentPixels);
        }

        /// <summary>
        ///     Assembles a whole set from several pictures sharing one palette.
        /// </summary>
        /// <remarks>
        ///     The other half of what a multi-frame set needs: <see cref="ReplaceFrame"/> edits one
        ///     that exists and this builds one that does not. The canvas is the largest picture in
        ///     both directions, so every frame fits without any of them being scaled, and the palette
        ///     is chosen over all of the pictures at once rather than per picture - a per-picture
        ///     palette cannot be stored, since the format holds exactly one for the set.
        /// </remarks>
        /// <param name="pictures">The frames, in the order they are to be stored. Not disposed here.</param>
        /// <param name="anchor">Where each picture sits on the shared canvas.</param>
        /// <returns>The set and what the conversion cost.</returns>
        /// <exception cref="ArgumentNullException">A required argument is null.</exception>
        /// <exception cref="ArgumentException">No picture was given, or the anchor has nothing to keep.</exception>
        /// <exception cref="InvalidOperationException">The pictures cannot be expressed by the format.</exception>
        public static SpriteFrameImport FromImages(IReadOnlyList<Image> pictures,
                                                   SpriteFrameAnchor anchor = SpriteFrameAnchor.TopLeft) {
            if (pictures == null)
                throw new ArgumentNullException(nameof(pictures));
            if (pictures.Count == 0)
                throw new ArgumentException("A sprite set holds at least one frame.", nameof(pictures));
            if (anchor == SpriteFrameAnchor.KeepOffset)
                throw new ArgumentException(
                    "There is no existing frame to keep an offset from when a set is being built from pictures.",
                    nameof(anchor));

            var planes = new List<int[]>(pictures.Count);
            var sizes = new List<(int Width, int Height)>(pictures.Count);
            int canvasWidth = 0, canvasHeight = 0;

            foreach (Image picture in pictures) {
                if (picture == null)
                    throw new ArgumentException("A set cannot hold a null picture.", nameof(pictures));

                int[] pixels = ReadStraightArgb(picture, out int width, out int height);
                if (width <= 0 || height <= 0)
                    throw new InvalidOperationException("One of those pictures has no pixels.");
                if (width > MaxDimension || height > MaxDimension)
                    throw new InvalidOperationException(
                        $"A sprite frame states its size in unsigned shorts, so {width}x{height} cannot be stored.");

                planes.Add(pixels);
                sizes.Add((width, height));
                canvasWidth = Math.Max(canvasWidth, width);
                canvasHeight = Math.Max(canvasHeight, height);
            }

            //One harvest over every picture, because one palette has to serve all of them. Harvesting
            //per picture and merging the palettes afterwards would spend entries on the same colour
            //once per frame that holds it.
            var counts = new Dictionary<int, long>();
            var needsPlane = new bool[planes.Count];
            bool needsAlphaPlane = false;
            int transparentPixels = 0;

            for (int id = 0; id < planes.Count; id++) {
                Dictionary<int, long> one = Harvest(planes[id], out needsPlane[id], out int clear);
                needsAlphaPlane |= needsPlane[id];
                transparentPixels += clear;
                foreach (KeyValuePair<int, long> colour in one) {
                    counts.TryGetValue(colour.Key, out long seen);
                    counts[colour.Key] = seen + colour.Value;
                }
            }

            int sourceColours = counts.Count;
            int[] palette = BuildPalette(counts, out int worstChannelError);
            int[] stored = WithReservedEntry(palette);

            var frames = new List<SpriteFrame>(planes.Count);
            int blackPixels = 0;
            for (int id = 0; id < planes.Count; id++) {
                (int width, int height) = sizes[id];
                (int offsetX, int offsetY) = Place(anchor, null, canvasWidth, canvasHeight, width, height);

                //Per picture, so a set of pictures where only one has soft edges does not pay for a
                //plane on the frames that do not need one.
                bool framePlane = needsPlane[id];

                frames.Add(new SpriteFrame {
                    OffsetX = offsetX,
                    OffsetY = offsetY,
                    SubWidth = width,
                    SubHeight = height,
                    //Row-major with the flag clear: a picture has no stored order to preserve.
                    Flags = framePlane ? SpriteFrame.FlagAlpha : 0,
                    PaletteIndices = MapOntoStored(planes[id], stored, out int black, out _, out _),
                    Alpha = framePlane ? AlphaPlane(planes[id]) : null
                });
                blackPixels += black;
            }

            return new SpriteFrameImport {
                Set = SpriteDefinition.FromFrames(canvasWidth, canvasHeight, stored, frames),
                ReplacedFrame = null,
                Placement = new Rectangle(0, 0, canvasWidth, canvasHeight),
                SourceColours = sourceColours,
                PaletteColours = palette.Length,
                PaletteEntriesReused = 0,
                PaletteEntriesAdded = palette.Length,
                PaletteEntriesApproximated = Math.Max(0, sourceColours - palette.Length),
                WorstChannelError = worstChannelError,
                Requantised = false,
                FramesRewritten = 0,
                CarriesAnAlphaPlane = needsAlphaPlane,
                BlackPixels = blackPixels,
                TransparentPixels = transparentPixels
            };
        }

        /// <summary>
        ///     Writes the new frame against the palette the set already has, extending it where there
        ///     is room and approximating where there is not.
        /// </summary>
        /// <remarks>
        ///     Existing entries never move, which is the whole point: a plane is a list of entry
        ///     numbers, so an entry that keeps its number leaves every frame that references it
        ///     spelling back exactly as it was. New colours can therefore only be appended, never
        ///     inserted in colour order, and the palette a set ends up with after several imports is
        ///     in the order the colours arrived rather than sorted.
        /// </remarks>
        private static SpriteFrameImport ExtendAround(SpriteDefinition set, int frameId, int[] pixels,
                                                      Dictionary<int, long> wanted, int width, int height,
                                                      int offsetX, int offsetY, int flags, bool needsAlphaPlane,
                                                      int transparentPixels) {
            var palette = new List<int>(set.PaletteStored);
            if (palette.Count == 0)
                palette.Add(0); //The reserved slot, which a set decoded from a file always has

            Dictionary<int, int> byColour = IndexByDrawnColour(palette);

            int reused = 0;
            var missing = new List<int>();
            foreach (int colour in SortedColours(wanted)) {
                if (byColour.ContainsKey(colour))
                    reused++;
                else
                    missing.Add(colour);
            }

            foreach (int colour in RoomFor(missing, wanted, MaxPaletteEntries - palette.Count))
                palette.Add(colour);

            int added = palette.Count - Math.Max(1, set.PaletteStored.Length);
            int[] stored = palette.ToArray();
            byte[] indices = MapOntoStored(pixels, stored, out int blackPixels, out int worstChannelError,
                                           out int approximated);

            var frames = new List<SpriteFrame>(set.Frames.Count);
            for (int id = 0; id < set.Frames.Count; id++) {
                frames.Add(id == frameId
                    ? NewFrame(offsetX, offsetY, width, height, flags, indices,
                               needsAlphaPlane ? AlphaPlane(pixels) : null)
                    : Copy(set.Frames[id]));
            }

            return new SpriteFrameImport {
                Set = SpriteDefinition.FromFrames(set.width, set.height, stored, frames, set.PixelPlaneTrailer),
                ReplacedFrame = frameId,
                Placement = new Rectangle(offsetX, offsetY, width, height),
                SourceColours = wanted.Count,
                PaletteColours = stored.Length - 1,
                PaletteEntriesReused = reused,
                PaletteEntriesAdded = added,
                //Counted by the mapping rather than as what was left over, because a median cut adds
                //representatives rather than source colours: a colour can be missing from the palette
                //before the cut and land on an exact match after it.
                PaletteEntriesApproximated = approximated,
                WorstChannelError = worstChannelError,
                Requantised = false,
                //Zero by construction rather than by measurement: no existing entry moved and no
                //other frame's bytes were touched. The tests hold that claim against the encoded
                //bytes rather than against this number.
                FramesRewritten = 0,
                CarriesAnAlphaPlane = needsAlphaPlane,
                BlackPixels = blackPixels,
                TransparentPixels = transparentPixels
            };
        }

        /// <summary>
        ///     Rebuilds one palette over every frame and the new picture, and re-indexes the lot.
        /// </summary>
        /// <remarks>
        ///     The colours of the untouched frames are read back through the palette they were stored
        ///     against and weighted by how many pixels hold them, so a frame covering most of the set
        ///     keeps most of the entries. Two things follow that the caller has to be told before this
        ///     runs: an entry no pixel references is dropped, and every frame's plane is rewritten
        ///     even where its drawn colours did not change, because the entry numbers moved.
        /// </remarks>
        private static SpriteFrameImport RequantiseAround(SpriteDefinition set, int frameId, int[] pixels,
                                                          Dictionary<int, long> wanted, int width, int height,
                                                          int offsetX, int offsetY, int flags, bool needsAlphaPlane,
                                                          int transparentPixels) {
            var counts = new Dictionary<int, long>(wanted);
            for (int id = 0; id < set.Frames.Count; id++) {
                if (id == frameId)
                    continue;

                foreach (byte index in set.Frames[id].PaletteIndices) {
                    if (index == 0 || index >= set.PaletteStored.Length)
                        continue;
                    int colour = DrawnColour(set.PaletteStored[index]);
                    counts.TryGetValue(colour, out long seen);
                    counts[colour] = seen + 1;
                }
            }

            int[] palette = BuildPalette(counts, out int worstChannelError);
            int[] stored = WithReservedEntry(palette);

            //One lookup per old entry rather than per pixel: a set has at most 255 of them and
            //millions of pixels.
            var remap = new byte[Math.Max(1, set.PaletteStored.Length)];
            for (int entry = 1; entry < set.PaletteStored.Length; entry++) {
                remap[entry] = palette.Length == 0
                    ? (byte) 0
                    : (byte) (NearestIndex(DrawnColour(set.PaletteStored[entry]), palette) + 1);
            }

            byte[] indices = MapOntoStored(pixels, stored, out int blackPixels, out int replacedError, out _);
            worstChannelError = Math.Max(worstChannelError, replacedError);

            var frames = new List<SpriteFrame>(set.Frames.Count);
            int rewritten = 0;
            for (int id = 0; id < set.Frames.Count; id++) {
                if (id == frameId) {
                    frames.Add(NewFrame(offsetX, offsetY, width, height, flags, indices,
                                        needsAlphaPlane ? AlphaPlane(pixels) : null));
                    continue;
                }

                SpriteFrame source = set.Frames[id];
                byte[] moved = new byte[source.PaletteIndices.Length];
                bool changed = false;
                for (int at = 0; at < moved.Length; at++) {
                    byte was = source.PaletteIndices[at];
                    moved[at] = was == 0 || was >= remap.Length ? (byte) 0 : remap[was];
                    changed |= moved[at] != was;
                }

                if (changed)
                    rewritten++;

                //Flags, geometry and alpha plane are the frame's own and survive untouched; only the
                //entry numbers move, which is the entire cost of this policy.
                frames.Add(NewFrame(source.OffsetX, source.OffsetY, source.SubWidth, source.SubHeight,
                                    source.Flags, moved, source.Alpha == null ? null : (byte[]) source.Alpha.Clone()));
            }

            return new SpriteFrameImport {
                Set = SpriteDefinition.FromFrames(set.width, set.height, stored, frames, set.PixelPlaneTrailer),
                ReplacedFrame = frameId,
                Placement = new Rectangle(offsetX, offsetY, width, height),
                SourceColours = wanted.Count,
                PaletteColours = palette.Length,
                PaletteEntriesReused = 0,
                PaletteEntriesAdded = palette.Length,
                PaletteEntriesApproximated = Math.Max(0, counts.Count - palette.Length),
                WorstChannelError = worstChannelError,
                Requantised = true,
                FramesRewritten = rewritten,
                CarriesAnAlphaPlane = needsAlphaPlane,
                BlackPixels = blackPixels,
                TransparentPixels = transparentPixels
            };
        }

        /// <summary>Builds the replacement frame, so both policies state its shape once.</summary>
        /// <param name="offsetX">Left edge within the canvas.</param>
        /// <param name="offsetY">Top edge within the canvas.</param>
        /// <param name="width">Stored plane width.</param>
        /// <param name="height">Stored plane height.</param>
        /// <param name="flags">The flags byte, carried from the frame being displaced.</param>
        /// <param name="indices">The palette-index plane.</param>
        /// <param name="alpha">The alpha plane, or null.</param>
        /// <returns>The frame.</returns>
        private static SpriteFrame NewFrame(int offsetX, int offsetY, int width, int height, int flags,
                                            byte[] indices, byte[]? alpha) {
            return new SpriteFrame {
                OffsetX = offsetX,
                OffsetY = offsetY,
                SubWidth = width,
                SubHeight = height,
                Flags = flags,
                PaletteIndices = indices,
                Alpha = alpha
            };
        }

        /// <summary>
        ///     Copies a frame, planes included, so the result shares no array with the set it came from.
        /// </summary>
        /// <remarks>
        ///     The arrays are cloned rather than referenced. The caller keeps the set it passed in -
        ///     the editor re-decodes the selected row from the staged bytes rather than swapping the
        ///     object - and an aliased plane would let an edit to one appear in the other.
        /// </remarks>
        /// <param name="frame">The frame to copy.</param>
        /// <returns>The copy.</returns>
        private static SpriteFrame Copy(SpriteFrame frame) {
            return new SpriteFrame {
                OffsetX = frame.OffsetX,
                OffsetY = frame.OffsetY,
                SubWidth = frame.SubWidth,
                SubHeight = frame.SubHeight,
                //Whole, so the traversal bit and anything the client does not read both survive.
                Flags = frame.Flags,
                PaletteIndices = (byte[]) frame.PaletteIndices.Clone(),
                Alpha = frame.Alpha == null ? null : (byte[]) frame.Alpha.Clone()
            };
        }

        /// <summary>
        ///     The distinct storable colours a picture holds, with how many pixels hold each.
        /// </summary>
        /// <remarks>
        ///     Harvested after the black promotion, so 0x000000 and 0x000001 are one colour rather
        ///     than two that draw identically and cost an entry apiece. A fully transparent pixel
        ///     contributes no colour at all, whatever the file recorded under the transparency.
        /// </remarks>
        /// <param name="pixels">Straight ARGB, row-major.</param>
        /// <param name="needsAlphaPlane">Receives whether any pixel is partly transparent.</param>
        /// <param name="transparentPixels">Receives how many pixels are fully transparent.</param>
        /// <returns>The colours and their weights.</returns>
        private static Dictionary<int, long> Harvest(int[] pixels, out bool needsAlphaPlane, out int transparentPixels) {
            var counts = new Dictionary<int, long>();
            needsAlphaPlane = false;
            transparentPixels = 0;

            foreach (int argb in pixels) {
                int alpha = (argb >> 24) & 0xFF;
                if (alpha == 0) {
                    transparentPixels++;
                    continue;
                }

                if (alpha != 0xFF)
                    needsAlphaPlane = true;

                int colour = StorableColour(argb & 0xFFFFFF);
                counts.TryGetValue(colour, out long seen);
                counts[colour] = seen + 1;
            }

            return counts;
        }

        /// <summary>
        ///     As many of the missing colours as the palette has room for, cut down when it has fewer.
        /// </summary>
        /// <remarks>
        ///     The obvious version of this appends colours in order until the palette is full, and it
        ///     is badly wrong for the case it exists to handle. A photograph dropped onto a frame of a
        ///     set with a small palette has thousands of colours, and taking the first 255 in ascending
        ///     order takes the 255 darkest blues and approximates the whole picture against them. A
        ///     median cut over the colours that did not match spends the room on the colours the
        ///     picture is actually made of, which is what the whole-set import already does.
        /// </remarks>
        /// <param name="missing">The colours no entry matched, in ascending order.</param>
        /// <param name="weights">How many pixels hold each colour.</param>
        /// <param name="room">Entries the palette has left.</param>
        /// <returns>The colours to append.</returns>
        private static int[] RoomFor(List<int> missing, Dictionary<int, long> weights, int room) {
            if (room <= 0 || missing.Count == 0)
                return Array.Empty<int>();
            if (missing.Count <= room)
                return missing.ToArray();

            //Already ascending, which is the order MedianCut's boxes are measured over.
            var weighted = new ColourCount[missing.Count];
            for (int at = 0; at < missing.Count; at++) {
                weights.TryGetValue(missing[at], out long pixels);
                weighted[at] = new ColourCount(missing[at], pixels);
            }

            return MedianCut(weighted, room);
        }

        /// <summary>The colours of a harvest in ascending order, so a palette never depends on hashing.</summary>
        /// <param name="counts">The harvest.</param>
        /// <returns>The colours, sorted.</returns>
        private static int[] SortedColours(Dictionary<int, long> counts) {
            var colours = new int[counts.Count];
            counts.Keys.CopyTo(colours, 0);
            Array.Sort(colours);
            return colours;
        }

        /// <summary>Prefixes a palette with the reserved transparent entry the format never stores.</summary>
        /// <param name="palette">The colours.</param>
        /// <returns>The palette as stored.</returns>
        private static int[] WithReservedEntry(int[] palette) {
            int[] stored = new int[palette.Length + 1];
            Array.Copy(palette, 0, stored, 1, palette.Length);
            return stored;
        }

        /// <summary>The colour an entry draws as, which is the client's promotion of a stored black.</summary>
        /// <remarks>
        ///     <c>Class324.java:76-79</c>. Matching a new colour against the palette has to be done on
        ///     this rather than on the stored spelling, or a picture holding black adds a second entry
        ///     beside one that already draws black.
        /// </remarks>
        /// <param name="stored">The stored entry.</param>
        /// <returns>The drawn colour.</returns>
        private static int DrawnColour(int stored) {
            return stored == 0 ? 1 : stored;
        }

        /// <summary>Maps each drawn colour of a palette to the lowest entry that produces it.</summary>
        /// <param name="palette">The palette as stored, entry 0 reserved.</param>
        /// <returns>Drawn colour to entry number.</returns>
        private static Dictionary<int, int> IndexByDrawnColour(List<int> palette) {
            var byColour = new Dictionary<int, int>(palette.Count);
            //Downwards, so a colour spelled twice in a shipped palette resolves to the lower entry
            //and the mapping is a function of the palette rather than of iteration order.
            for (int entry = palette.Count - 1; entry >= 1; entry--)
                byColour[DrawnColour(palette[entry])] = entry;
            return byColour;
        }

        /// <summary>
        ///     Turns a picture into palette indices against a palette that already exists.
        /// </summary>
        /// <remarks>
        ///     Entry 0 is the transparent slot and holds no colour, so only a fully transparent pixel
        ///     ever takes it. An opaque pixel pointed at entry 0 vanishes where there is no alpha
        ///     plane and draws black where there is, which is the failure this rule exists to stop -
        ///     and it is not the same thing as a black pixel, which stores 0x000001 at an entry of its
        ///     own and draws exactly as the client draws it.
        /// </remarks>
        /// <param name="pixels">Straight ARGB, row-major.</param>
        /// <param name="paletteStored">The palette as stored, entry 0 reserved.</param>
        /// <param name="blackPixels">Receives how many pixels held the promoted black.</param>
        /// <param name="worstChannelError">Receives the largest per-channel approximation error.</param>
        /// <param name="approximated">Receives how many distinct colours took an inexact entry.</param>
        /// <returns>One index per pixel.</returns>
        private static byte[] MapOntoStored(int[] pixels, int[] paletteStored, out int blackPixels,
                                            out int worstChannelError, out int approximated) {
            blackPixels = 0;
            worstChannelError = 0;
            approximated = 0;

            byte[] indices = new byte[pixels.Length];
            var resolved = new Dictionary<int, byte>();

            //The drawn colours, so the nearest search and the exact match both compare like with like.
            int[] drawn = new int[Math.Max(0, paletteStored.Length - 1)];
            for (int entry = 1; entry < paletteStored.Length; entry++)
                drawn[entry - 1] = DrawnColour(paletteStored[entry]);

            var byColour = new Dictionary<int, byte>(drawn.Length);
            for (int entry = drawn.Length; entry >= 1; entry--)
                byColour[drawn[entry - 1]] = (byte) entry;

            for (int at = 0; at < pixels.Length; at++) {
                if (((pixels[at] >> 24) & 0xFF) == 0)
                    continue;

                int colour = StorableColour(pixels[at] & 0xFFFFFF);
                if (!resolved.TryGetValue(colour, out byte index)) {
                    if (byColour.TryGetValue(colour, out byte exact)) {
                        index = exact;
                    } else if (drawn.Length == 0) {
                        //Nothing to point at. FromFrames would refuse an index past the palette, so
                        //this can only happen for a picture whose colours were all dropped, and the
                        //honest answer is the transparent slot rather than a wrong colour.
                        index = 0;
                        approximated++;
                    } else {
                        approximated++;
                        index = (byte) (NearestIndex(colour, drawn) + 1);
                        int nearest = drawn[index - 1];
                        for (int shift = 0; shift <= 16; shift += 8) {
                            int gap = Math.Abs(((colour >> shift) & 0xFF) - ((nearest >> shift) & 0xFF));
                            if (gap > worstChannelError)
                                worstChannelError = gap;
                        }
                    }

                    resolved[colour] = index;
                }

                indices[at] = index;
                if (colour == 1)
                    blackPixels++;
            }

            return indices;
        }

        /// <summary>Decides where a picture sits on the canvas.</summary>
        /// <param name="anchor">The placement asked for.</param>
        /// <param name="existing">The frame being displaced, or null when there is none.</param>
        /// <param name="canvasWidth">The canvas width.</param>
        /// <param name="canvasHeight">The canvas height.</param>
        /// <param name="width">The picture's width.</param>
        /// <param name="height">The picture's height.</param>
        /// <returns>The offset to store.</returns>
        private static (int X, int Y) Place(SpriteFrameAnchor anchor, SpriteFrame? existing,
                                            int canvasWidth, int canvasHeight, int width, int height) {
            switch (anchor) {
                case SpriteFrameAnchor.KeepOffset:
                    if (existing == null)
                        throw new ArgumentException("There is no existing frame to keep an offset from.",
                                                    nameof(anchor));
                    return (existing.OffsetX, existing.OffsetY);

                case SpriteFrameAnchor.TopLeft:
                    return (0, 0);

                case SpriteFrameAnchor.Centre:
                    //Rounded down, so a one pixel remainder goes to the right and the bottom. Either
                    //choice is arbitrary; being consistent is what makes an import reproducible.
                    return (Math.Max(0, (canvasWidth - width) / 2), Math.Max(0, (canvasHeight - height) / 2));

                default:
                    throw new ArgumentOutOfRangeException(nameof(anchor), anchor, "Unknown placement.");
            }
        }
    }
}
