using System;
using System.Collections.Generic;

namespace FlashEditor.Map {
    /// <summary>Which floor id the wand matches on.</summary>
    /// <remarks>
    ///     Two fields rather than one "the floor", because a tile has both and they answer different
    ///     questions. Selecting by underlay picks out a whole terrain type through the paths and
    ///     roads drawn over it; selecting by overlay picks out the path and leaves the terrain. A
    ///     wand that guessed which one the user meant would be right about half the time.
    /// </remarks>
    public enum MapWandField {
        /// <summary>Match on the underlay id, ignoring anything drawn over it.</summary>
        Underlay,

        /// <summary>Match on the overlay id, where 0 means the tile has none.</summary>
        Overlay
    }

    /// <summary>What a wand click selected, and what stopped it going further.</summary>
    /// <remarks>
    ///     The two limits are reported separately because they mean opposite things to the user. A
    ///     flood that ran into the edge of the loaded neighbourhood is <em>incomplete</em> and would
    ///     grow if it could see further; one that hit the tile cap is complete in shape and simply
    ///     too big to act on. Collapsing them into "truncated" would leave a user re-clicking a wand
    ///     that can never finish.
    /// </remarks>
    /// <param name="Tiles">The selected tiles, in world coordinates.</param>
    /// <param name="MatchedValue">The id every selected tile carries.</param>
    /// <param name="ReachedSceneEdge">Whether the flood stopped at the edge of the loaded squares.</param>
    /// <param name="ReachedTileLimit">Whether the flood stopped at <paramref name="Tiles"/>'s cap.</param>
    public readonly record struct MapWandResult(
        IReadOnlyList<(int WorldX, int WorldY)> Tiles,
        int MatchedValue,
        bool ReachedSceneEdge,
        bool ReachedTileLimit);

    /// <summary>
    ///     Selects the run of similar tiles connected to one clicked tile.
    /// </summary>
    /// <remarks>
    ///     <b>Bounded by the loaded neighbourhood, deliberately.</b> The flood reads through a
    ///     <see cref="MapScene"/>, which is the 3x3 block of squares the editor already holds around
    ///     the click. Letting it walk off that would mean decoding squares from inside a mouse
    ///     handler, one JS5 read at a time, with no upper bound - a wand over open grass would
    ///     decode a continent. Stopping at the edge and <em>saying so</em> is the honest version, and
    ///     it is why <see cref="MapWandResult.ReachedSceneEdge"/> exists.
    ///     <para>
    ///     <b>Four-connected, not eight.</b> Diagonally touching tiles do not share an edge, and a
    ///     wand that crossed corners leaks through the single-tile diagonal gaps that paths and
    ///     coastlines are full of, which turns "this beach" into "every beach on the continent".
    ///     </para>
    ///     <para>
    ///     <b>Tolerance is a distance in ids, not in colour.</b> Neighbouring floor ids are not
    ///     neighbouring colours - the tables are in no visual order at all - so a tolerance above
    ///     zero is only useful for the handful of families that were authored as runs. It defaults
    ///     to zero for that reason and the option bar says so.
    ///     </para>
    /// </remarks>
    public static class MapWand {
        /// <summary>
        ///     The most tiles one wand click will select.
        /// </summary>
        /// <remarks>
        ///     Four squares' worth. The scene the flood runs over is nine squares, so this stops
        ///     short of it on purpose: a wand that routinely returned every tile it could see would
        ///     be indistinguishable from "select everything loaded", and the rectangle tool already
        ///     does that deliberately.
        /// </remarks>
        public const int DefaultTileLimit = 4 * 64 * 64;

        /// <summary>The largest tolerance the option bar offers.</summary>
        public const int MaximumTolerance = 64;

        /// <summary>
        ///     Floods out from a tile, taking every connected tile whose id is within tolerance.
        /// </summary>
        /// <param name="scene">The loaded neighbourhood the flood may read.</param>
        /// <param name="plane">The plane to read.</param>
        /// <param name="worldX">World X of the clicked tile.</param>
        /// <param name="worldY">World Y of the clicked tile.</param>
        /// <param name="field">Which id to match on.</param>
        /// <param name="tolerance">How far from the clicked tile's id still counts as a match.</param>
        /// <param name="tileLimit">The most tiles to return.</param>
        /// <returns>The result, whose tile list is empty when the click was outside the scene.</returns>
        public static MapWandResult Flood(MapScene scene, int plane, int worldX, int worldY,
            MapWandField field, int tolerance, int tileLimit = DefaultTileLimit) {
            if (scene == null) throw new ArgumentNullException(nameof(scene));

            int limit = Math.Max(1, tileLimit);
            int slack = Math.Clamp(tolerance, 0, MaximumTolerance);

            int startX = worldX - scene.BaseX;
            int startY = worldY - scene.BaseY;

            var selected = new List<(int WorldX, int WorldY)>();
            if (!Inside(scene, startX, startY))
                return new MapWandResult(selected, -1, false, false);

            int target = Read(scene, plane, startX, startY, field);

            var seen = new HashSet<int>();
            var frontier = new Queue<(int SceneX, int SceneY)>();

            seen.Add(startY * scene.WidthTiles + startX);
            frontier.Enqueue((startX, startY));

            bool reachedEdge = false;
            bool reachedLimit = false;

            //Iterative rather than recursive: a flood over four squares is 16,384 tiles deep in the
            //worst case, which is a stack overflow rather than a slow method.
            while (frontier.Count > 0) {
                (int sceneX, int sceneY) = frontier.Dequeue();
                selected.Add((sceneX + scene.BaseX, sceneY + scene.BaseY));

                if (selected.Count >= limit) {
                    reachedLimit = frontier.Count > 0;
                    break;
                }

                for (int direction = 0; direction < 4; direction++) {
                    int nextX = sceneX + (direction == 0 ? 1 : direction == 1 ? -1 : 0);
                    int nextY = sceneY + (direction == 2 ? 1 : direction == 3 ? -1 : 0);

                    if (!Inside(scene, nextX, nextY)) {
                        reachedEdge = true;
                        continue;
                    }

                    int key = nextY * scene.WidthTiles + nextX;
                    if (seen.Contains(key))
                        continue;

                    if (Math.Abs(Read(scene, plane, nextX, nextY, field) - target) > slack)
                        continue;

                    //Marked on enqueue rather than on dequeue. Marked on dequeue, a tile with two
                    //already-queued neighbours is enqueued twice and appears twice in the output.
                    seen.Add(key);
                    frontier.Enqueue((nextX, nextY));
                }
            }

            return new MapWandResult(selected, target, reachedEdge, reachedLimit);
        }

        /// <summary>
        ///     Whether a scene tile is one the flood may read.
        /// </summary>
        /// <remarks>
        ///     The square has to exist, not merely be in range. <see cref="MapScene.UnderlayId"/>
        ///     answers 0 for an absent square, which is also a legitimate stored id, so a flood that
        ///     only bounds-checked would pour out through the missing corner of a neighbourhood and
        ///     select open water as though it were terrain.
        /// </remarks>
        private static bool Inside(MapScene scene, int sceneX, int sceneY) =>
            sceneX >= 0 && sceneY >= 0 && sceneX < scene.WidthTiles && sceneY < scene.HeightTiles
            && scene.SquareAt(sceneX, sceneY) != null;

        private static int Read(MapScene scene, int plane, int sceneX, int sceneY, MapWandField field) =>
            field == MapWandField.Overlay
                ? scene.OverlayId(plane, sceneX, sceneY)
                : scene.UnderlayId(plane, sceneX, sceneY);
    }
}
