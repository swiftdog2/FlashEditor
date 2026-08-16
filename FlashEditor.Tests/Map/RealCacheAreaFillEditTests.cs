using System;
using System.Collections.Generic;
using System.Linq;
using FlashEditor.Cache;
using FlashEditor.Cache.Region;
using FlashEditor.Map;
using FlashEditor.Tests.Cache.RealCache;
using Xunit;
using Xunit.Abstractions;

using MapRegion = FlashEditor.Cache.Region.Region;

namespace FlashEditor.Tests.Map
{
    /// <summary>
    ///     Paints an area, paints it back, and requires the original stored bytes.
    /// </summary>
    /// <remarks>
    ///     <b>This is a different claim from the byte-identity sweep, and the gap between the two
    ///     has held four real defects.</b> <c>RealCacheRegionCodecTests</c> proves an
    ///     <em>unedited</em> square re-encodes to what it was read from, which is a statement about
    ///     the encoder alone: the square never went near an edit path, and its
    ///     <see cref="MapRegion.Dirty"/> flag is false so the verbatim shortcut answers before the
    ///     encoder is even reached. What is asserted here is that an edit that nets out to nothing
    ///     <em>writes</em> nothing - the square is dirty, the whole encoder runs, and it has to land
    ///     on the same bytes.
    ///     <para>
    ///     <b>The non-canonical hazards on this path in particular.</b> A terrain tile is written
    ///     overlay, flags, underlay, height, and underlay-first reproduced only 91 of 1684 files, so
    ///     a restore that wrote the fields in a different order would fail here and nowhere else. An
    ///     overlay carries three fields and restoring only its id would leave a shape behind. Height
    ///     bytes 0 and 1 both decode to zero and the stored byte is kept verbatim, which is why the
    ///     height case below is a documented defect rather than a passing test.
    ///     </para>
    ///     <para>
    ///     Every fill goes through <see cref="MapAreaEdits"/> rather than through hand-built edits,
    ///     because the thing under test is the path the Map tab's fill actually takes.
    ///     </para>
    /// </remarks>
    [Collection("RealCache")]
    public sealed class RealCacheAreaFillEditTests : IClassFixture<RealCacheFixture>
    {
        private const int RegionX = 50, RegionY = 50;

        private readonly RealCacheFixture _fixture;
        private readonly ITestOutputHelper _output;

        public RealCacheAreaFillEditTests(RealCacheFixture fixture, ITestOutputHelper output)
        {
            _fixture = fixture;
            _output = output;
        }

        /// <summary>
        ///     Filling a selection with an underlay and filling it back lands on the original bytes.
        /// </summary>
        /// <remarks>
        ///     The middle assertion is the one that makes the outer pair mean anything: the bytes
        ///     have to <em>differ</em> after the first fill. Without it a fill that silently did
        ///     nothing would pass, which is the shape of failure this whole test exists to catch.
        /// </remarks>
        [RealCacheFact]
        public void FillingAnUnderlayAndFillingItBackLandsOnTheOriginalBytes()
        {
            MapRegion square = Load();
            byte[] original = (byte[]) square.RawTerrain.Clone();
            Assert.False(square.Dirty);

            List<(int WorldX, int WorldY)> tiles = Block(12, 12, 8, 8);
            Dictionary<(int, int), int> before = tiles.ToDictionary(t => t,
                t => square.GetUnderlayId(0, Local(t.WorldX), Local(t.WorldY)));

            //A value no covered tile already holds, so the fill genuinely writes every one of them.
            int painted = FreeUnderlayId(before.Values);

            MapAreaEditResult fill = Fill(square, tiles, MapAreaTool.Underlay,
                new MapAreaOptions { Value = painted });

            Assert.False(fill.WasRefused);
            Assert.NotNull(fill.Edit);
            Assert.Equal(tiles.Count, fill.Changed);

            fill.Edit.Apply();
            Assert.True(square.Dirty);

            byte[] afterPaint = RegionCodec.EncodeTerrain(square);
            Assert.False(Same(original, afterPaint),
                "the fill wrote nothing, so painting it back proves nothing");

            //Painted back tile by tile with what each one held, which is what a user does with the
            //eyedropper and the brush rather than with Undo.
            var restore = new List<IMapEdit>();
            foreach ((int worldX, int worldY) in tiles)
                restore.Add(new SetUnderlayEdit(square, 0, Local(worldX), Local(worldY),
                    before[(worldX, worldY)]));

            new CompositeEdit("restore", restore).Apply();

            Assert.True(square.Dirty, "the square must still be dirty, or the encoder is not being tested");
            Assert.True(Same(original, RegionCodec.EncodeTerrain(square)),
                "painting an area back to what it held did not reproduce the stored bytes");
        }

        /// <summary>
        ///     An overlay fill and its reverse land on the original bytes, all three fields included.
        /// </summary>
        /// <remarks>
        ///     An overlay is an id, a shape and a rotation packed into one opcode
        ///     (<c>2 + shape * 4 + rotation</c>) followed by the id byte, so restoring the id alone
        ///     would leave the shape of whatever the brush was set to and produce a file of exactly
        ///     the right length with the wrong contents.
        /// </remarks>
        [RealCacheFact]
        public void FillingAnOverlayAndFillingItBackRestoresShapeAndRotationToo()
        {
            MapRegion square = Load();
            byte[] original = (byte[]) square.RawTerrain.Clone();

            List<(int WorldX, int WorldY)> tiles = Block(20, 20, 6, 6);

            var before = new Dictionary<(int, int), (int Id, byte Shape, byte Rotation)>();
            foreach ((int worldX, int worldY) in tiles) {
                int x = Local(worldX), y = Local(worldY);
                before[(worldX, worldY)] = (square.GetOverlayId(0, x, y),
                    square.GetOverlayShape(0, x, y), square.GetOverlayRotation(0, x, y));
            }

            //A shape and rotation that are not 0, so a restore that only put the id back is caught.
            MapAreaEditResult fill = Fill(square, tiles, MapAreaTool.Overlay,
                new MapAreaOptions { Value = 3, OverlayShape = 5, OverlayRotation = 2 });

            Assert.False(fill.WasRefused);
            Assert.NotNull(fill.Edit);
            fill.Edit.Apply();

            Assert.False(Same(original, RegionCodec.EncodeTerrain(square)),
                "the overlay fill wrote nothing");

            var restore = new List<IMapEdit>();
            foreach ((int worldX, int worldY) in tiles) {
                (int id, byte shape, byte rotation) = before[(worldX, worldY)];
                restore.Add(new SetOverlayEdit(square, 0, Local(worldX), Local(worldY),
                    id, shape, rotation));
            }

            new CompositeEdit("restore", restore).Apply();

            Assert.True(Same(original, RegionCodec.EncodeTerrain(square)),
                "restoring an overlay area did not reproduce the stored bytes");
        }

        /// <summary>Setting the blocked flag across an area and clearing it lands on the original bytes.</summary>
        /// <remarks>
        ///     The flag byte is written as <c>flags + 49</c> and omitted entirely when it is zero, so
        ///     this is the one field where a set-and-unset changes the <em>length</em> of a tile's
        ///     opcode run in both directions.
        /// </remarks>
        [RealCacheFact]
        public void SettingAndClearingTheBlockedFlagAcrossAnAreaLandsOnTheOriginalBytes()
        {
            MapRegion square = Load();
            byte[] original = (byte[]) square.RawTerrain.Clone();

            List<(int WorldX, int WorldY)> tiles = Block(30, 30, 5, 5);
            Dictionary<(int, int), byte> before = tiles.ToDictionary(t => t,
                t => square.GetRenderRule(0, Local(t.WorldX), Local(t.WorldY)));

            MapAreaEditResult fill = Fill(square, tiles, MapAreaTool.BlockedFlag,
                new MapAreaOptions { Blocked = true });

            Assert.False(fill.WasRefused);
            Assert.NotNull(fill.Edit);
            fill.Edit.Apply();

            Assert.False(Same(original, RegionCodec.EncodeTerrain(square)),
                "the flag fill wrote nothing, so no covered tile was unblocked to begin with");

            var restore = new List<IMapEdit>();
            foreach ((int worldX, int worldY) in tiles)
                restore.Add(new SetTileFlagsEdit(square, 0, Local(worldX), Local(worldY),
                    before[(worldX, worldY)]));

            new CompositeEdit("restore", restore).Apply();

            Assert.True(Same(original, RegionCodec.EncodeTerrain(square)),
                "restoring the flag byte did not reproduce the stored bytes");
        }

        /// <summary>
        ///     A height raised and put straight back does <b>not</b> reproduce the original bytes,
        ///     and this pins exactly when.
        /// </summary>
        /// <remarks>
        ///     <b>A known defect, named as one so a fix shows up as a deliberate test change.</b>
        ///     <c>Region.SetTileHeight</c> latches <c>heightExplicit</c> and <c>heightEdited</c> and
        ///     nothing ever clears them, so once a tile's height has been written the encoder stops
        ///     replaying its stored byte and recomputes a step from the value. That is lossy in two
        ///     ways, both of which are properties of the format rather than of this editor:
        ///     <list type="bullet">
        ///         <item><description>
        ///             A tile that stored <b>no</b> height wrote opcode 0, one byte. After a
        ///             set-and-unset it writes opcode 1 and a step, two bytes.
        ///         </description></item>
        ///         <item><description>
        ///             Stored bytes <b>0 and 1 both decode to height zero</b>, and the shipped files
        ///             use both, so a tile whose byte was 1 comes back as 0.
        ///         </description></item>
        ///     </list>
        ///     The assertion is therefore the relationship rather than a bare inequality: the bytes
        ///     survive if and only if every touched tile stored an explicit height whose byte was
        ///     not the alias. Written the other way round - "restoring a height always differs" - it
        ///     would fail on a block that happened to be all explicit, which is the sort of test
        ///     that gets relaxed rather than understood.
        /// </remarks>
        [RealCacheFact]
        public void RaisingAndRestoringAHeightAreaLosesTheStoredByte_DocumentsKnownDefect()
        {
            MapRegion square = Load();
            byte[] original = (byte[]) square.RawTerrain.Clone();

            List<(int WorldX, int WorldY)> tiles = Block(40, 40, 6, 6);

            int implicitHeights = 0, aliasedBytes = 0;
            var before = new Dictionary<(int, int), int>();

            foreach ((int worldX, int worldY) in tiles) {
                int x = Local(worldX), y = Local(worldY);

                before[(worldX, worldY)] = square.GetTileHeight(0, x, y);

                if (!square.HasExplicitHeight(0, x, y))
                    implicitHeights++;
                else if (square.GetRawHeightByte(0, x, y) == 1)
                    aliasedBytes++;
            }

            MapAreaEditResult fill = Fill(square, tiles, MapAreaTool.RaiseHeight, new MapAreaOptions());
            Assert.NotNull(fill.Edit);
            fill.Edit.Apply();

            var restore = new List<IMapEdit>();
            foreach ((int worldX, int worldY) in tiles)
                restore.Add(new SetHeightEdit(square, 0, Local(worldX), Local(worldY),
                    before[(worldX, worldY)]));

            new CompositeEdit("restore", restore).Apply();

            //Every decoded height is back where it started. It is only the *encoding* that is lost.
            foreach ((int worldX, int worldY) in tiles)
                Assert.Equal(before[(worldX, worldY)],
                    square.GetTileHeight(0, Local(worldX), Local(worldY)));

            bool lossless = implicitHeights == 0 && aliasedBytes == 0;
            bool same = Same(original, RegionCodec.EncodeTerrain(square));

            _output.WriteLine($"m{RegionX}_{RegionY} tiles 40..45: {implicitHeights} stored no height, " +
                              $"{aliasedBytes} stored the aliased byte 1, bytes reproduced: {same}");

            Assert.Equal(lossless, same);
        }

        /// <summary>
        ///     The zero-change fill writes nothing at all, so a square never becomes dirty for free.
        /// </summary>
        /// <remarks>
        ///     The cheapest form of the same claim, and the one a user hits by accident: filling a
        ///     selection with the floor it already holds. A fill that produced an edit here would
        ///     mark the square dirty and rewrite the archive, changing its CRC and dragging in the
        ///     reference-table entry of every archive packed beside it, for no change whatever.
        /// </remarks>
        [RealCacheFact]
        public void FillingAnAreaWithWhatItAlreadyHoldsProducesNoEdit()
        {
            MapRegion square = Load();

            //A block that is uniform in the shipped data, found rather than assumed.
            (int x, int y, int underlay)? uniform = FindUniformBlock(square, 4);
            Assert.True(uniform.HasValue,
                $"no uniform 4x4 underlay block in m{RegionX}_{RegionY}, so this test cannot run");

            List<(int WorldX, int WorldY)> tiles = Block(uniform.Value.x, uniform.Value.y, 4, 4);

            MapAreaEditResult result = Fill(square, tiles, MapAreaTool.Underlay,
                new MapAreaOptions { Value = uniform.Value.underlay });

            Assert.False(result.WasRefused);
            Assert.Null(result.Edit);
            Assert.Equal(0, result.Changed);
            Assert.Equal(tiles.Count, result.Skipped);
            Assert.False(square.Dirty);
        }

        /// <summary>
        ///     An area fill past the underlay cap refuses out loud and touches nothing.
        /// </summary>
        /// <remarks>
        ///     The single-tile path is protected by the option bar's own maximum, which an area
        ///     fill does not go through - so this is the check that stops the fill being the route
        ///     around it. Against the real cache rather than a synthetic square, because what is
        ///     being asserted is that a real square comes out unmarked.
        /// </remarks>
        [RealCacheFact]
        public void AnAreaFillPastTheUnderlayCapRefusesAndLeavesTheSquareClean()
        {
            MapRegion square = Load();

            MapAreaEditResult result = Fill(square, Block(50, 50, 3, 3), MapAreaTool.Underlay,
                new MapAreaOptions { Value = MapToolLimits.MaximumUnderlayId + 1 });

            Assert.True(result.WasRefused);
            Assert.Null(result.Edit);
            Assert.False(square.Dirty);
            Assert.True(Same(square.RawTerrain, RegionCodec.EncodeTerrain(square)));
        }

        private MapRegion Load()
        {
            RSCache cache = _fixture.OpenCache();
            var loader = new MapSquareLoader(cache);
            return loader.Load(RegionX, RegionY, out _);
        }

        private static MapAreaEditResult Fill(MapRegion square,
            IEnumerable<(int WorldX, int WorldY)> tiles, MapAreaTool tool, MapAreaOptions options)
        {
            return MapAreaEdits.Build(tiles, 0, tool, options, (_, _) => square);
        }

        /// <summary>A block of world tiles inside the square, given its south-west local corner.</summary>
        private static List<(int WorldX, int WorldY)> Block(int localX, int localY, int wide, int high)
        {
            return MapSelection.RectangleTiles(
                RegionX * MapRegion.WIDTH + localX, RegionY * MapRegion.HEIGHT + localY,
                RegionX * MapRegion.WIDTH + localX + wide - 1,
                RegionY * MapRegion.HEIGHT + localY + high - 1).ToList();
        }

        private static int Local(int world) => world % MapRegion.WIDTH;

        /// <summary>An underlay id none of the covered tiles already holds, and within the cap.</summary>
        private static int FreeUnderlayId(IEnumerable<int> held)
        {
            var taken = new HashSet<int>(held);
            for (int id = 1; id <= MapToolLimits.MaximumUnderlayId; id++)
                if (!taken.Contains(id))
                    return id;

            throw new InvalidOperationException("every storable underlay id is already on these tiles");
        }

        private static (int x, int y, int underlay)? FindUniformBlock(MapRegion square, int side)
        {
            for (int x = 0; x + side <= MapRegion.WIDTH; x++) {
                for (int y = 0; y + side <= MapRegion.HEIGHT; y++) {
                    int first = square.GetUnderlayId(0, x, y);
                    bool uniform = true;

                    for (int dx = 0; dx < side && uniform; dx++)
                        for (int dy = 0; dy < side && uniform; dy++)
                            uniform = square.GetUnderlayId(0, x + dx, y + dy) == first;

                    if (uniform)
                        return (x, y, first);
                }
            }

            return null;
        }

        private static bool Same(byte[] left, byte[] right)
        {
            if (left == null || right == null || left.Length != right.Length)
                return false;

            for (int i = 0; i < left.Length; i++)
                if (left[i] != right[i])
                    return false;

            return true;
        }
    }
}
