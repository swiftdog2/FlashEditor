using System;
using System.Collections.Generic;
using System.Drawing;
using System.Text;
using System.Windows.Forms;
using FlashEditor.cache;
using FlashEditor.Cache.Region;

using MapRegion = FlashEditor.Cache.Region.Region;

namespace FlashEditor.Map {
    /// <summary>
    ///     The Map tab: navigation, layer toggles, a canvas and a tile inspector.
    /// </summary>
    /// <remarks>
    ///     Built in code rather than through the designer, so it can be dropped into a tab page
    ///     with one line and does not add to the shared <c>Editor.Designer.cs</c>.
    /// </remarks>
    public sealed class MapEditorPanel : UserControl {
        private RSCache cache;
        private string cacheDirectory;
        private MapSquareLoader loader;
        private MapRasteriser rasteriser;

        private readonly MapViewerControl viewer = new MapViewerControl { Dock = DockStyle.Fill };
        private readonly NumericUpDown regionX = new NumericUpDown { Minimum = 0, Maximum = 255, Value = 50, Width = 60 };
        private readonly NumericUpDown regionY = new NumericUpDown { Minimum = 0, Maximum = 255, Value = 50, Width = 60 };
        private readonly ComboBox planeBox = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = 60 };
        private readonly CheckedListBox layerList = new CheckedListBox { Dock = DockStyle.Fill, CheckOnClick = true };
        private readonly TextBox inspector = new TextBox { Dock = DockStyle.Fill, Multiline = true, ReadOnly = true, Font = new Font("Consolas", 9F) };
        private readonly Label status = new Label { Dock = DockStyle.Bottom, Height = 22, TextAlign = ContentAlignment.MiddleLeft };
        private readonly Button goButton = new Button { Text = "Go", Width = 48 };

        private readonly ComboBox toolBox = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = 150 };
        private readonly NumericUpDown toolValue = new NumericUpDown { Minimum = 0, Maximum = 255, Value = 1, Width = 60 };
        private readonly Button undoButton = new Button { Text = "Undo", Width = 60, Enabled = false };
        private readonly Button redoButton = new Button { Text = "Redo", Width = 60, Enabled = false };
        private readonly Button saveButton = new Button { Text = "Save cache", Width = 90, Enabled = false };
        private readonly WorldNavigatorControl navigator = new WorldNavigatorControl { Dock = DockStyle.Fill };

        private readonly MapEditHistory history = new MapEditHistory();

        private TileHit lastHit;

        /// <summary>What a click on the canvas does.</summary>
        private enum MapTool {
            Inspect,
            PaintUnderlay,
            PaintOverlay,
            CycleOverlayShape,
            CycleOverlayRotation,
            RaiseHeight,
            LowerHeight,
            ToggleBlockedFlag,
            DeleteTopLocation
        }

        private static readonly (string Label, MapTool Tool)[] ToolRows = {
            ("Inspect", MapTool.Inspect),
            ("Paint underlay", MapTool.PaintUnderlay),
            ("Paint overlay", MapTool.PaintOverlay),
            ("Cycle overlay shape", MapTool.CycleOverlayShape),
            ("Cycle overlay rotation", MapTool.CycleOverlayRotation),
            ("Raise height", MapTool.RaiseHeight),
            ("Lower height", MapTool.LowerHeight),
            ("Toggle blocked flag", MapTool.ToggleBlockedFlag),
            ("Delete top location", MapTool.DeleteTopLocation)
        };

        private static readonly (string Name, MapLayers Layer)[] LayerRows = {
            ("Underlay", MapLayers.Underlay),
            ("Overlay", MapLayers.Overlay),
            ("Walls", MapLayers.Walls),
            ("Ground decoration", MapLayers.GroundDecoration),
            ("Map scene icons", MapLayers.MapSceneIcons),
            ("Game objects", MapLayers.GameObjects),
            ("Tile flags", MapLayers.TileFlags),
            ("Grid", MapLayers.Grid)
        };

        /// <summary>Creates the panel.</summary>
        public MapEditorPanel() {
            Dock = DockStyle.Fill;
            BuildLayout();

            goButton.Click += (_, _) => LoadRegion((int) regionX.Value, (int) regionY.Value);
            planeBox.SelectedIndexChanged += (_, _) => viewer.Plane = planeBox.SelectedIndex;
            layerList.ItemCheck += OnLayerToggled;
            viewer.TileHovered += (_, hit) => { lastHit = hit; UpdateInspector(hit); };
            viewer.TileClicked += OnTileClicked;

            undoButton.Click += (_, _) => { history.Undo(); AfterEdit(); };
            redoButton.Click += (_, _) => { history.Redo(); AfterEdit(); };
            history.Changed += (_, _) => {
                undoButton.Enabled = history.CanUndo;
                redoButton.Enabled = history.CanRedo;
                saveButton.Enabled = cache != null && history.Count > 0;
            };

            saveButton.Click += (_, _) => SaveEdits();

            navigator.RegionPicked += (_, region) => {
                if (!navigator.Exists(region.X, region.Y)) {
                    status.Text = $"m{region.X}_{region.Y} does not exist in this cache";
                    return;
                }
                regionX.Value = region.X;
                regionY.Value = region.Y;
                LoadRegion(region.X, region.Y);
            };
        }

        private MapTool SelectedTool =>
            toolBox.SelectedIndex >= 0 ? ToolRows[toolBox.SelectedIndex].Tool : MapTool.Inspect;

        private void OnTileClicked(object sender, TileHit hit) {
            lastHit = hit;
            UpdateInspector(hit);

            MapScene scene = viewer.Scene;
            if (scene == null || SelectedTool == MapTool.Inspect)
                return;

            MapRegion square = scene.SquareAt(hit.SceneX, hit.SceneY);
            if (square == null)
                return;

            IMapEdit edit = BuildEdit(SelectedTool, square, hit);
            if (edit == null)
                return;

            history.Apply(edit);
            AfterEdit();
            status.Text = edit.Description;
        }

        private IMapEdit BuildEdit(MapTool tool, MapRegion square, TileHit hit) {
            int p = hit.Plane, x = hit.LocalX, y = hit.LocalY;
            int value = (int) toolValue.Value;

            switch (tool) {
                case MapTool.PaintUnderlay:
                    return new SetUnderlayEdit(square, p, x, y, value);

                case MapTool.PaintOverlay:
                    //A freshly painted overlay takes shape 0, the full tile, which is what 85% of
                    //the overlays in the shipped cache use.
                    return new SetOverlayEdit(square, p, x, y, value, 0, 0);

                case MapTool.CycleOverlayShape: {
                    if (square.GetOverlayId(p, x, y) == 0)
                        return null;
                    byte shape = (byte) ((square.GetOverlayShape(p, x, y) + 1) % TileShapes.FileShapeCount);
                    return new SetOverlayEdit(square, p, x, y, square.GetOverlayId(p, x, y), shape,
                        square.GetOverlayRotation(p, x, y));
                }

                case MapTool.CycleOverlayRotation: {
                    if (square.GetOverlayId(p, x, y) == 0)
                        return null;
                    byte rotation = (byte) ((square.GetOverlayRotation(p, x, y) + 1) & 3);
                    return new SetOverlayEdit(square, p, x, y, square.GetOverlayId(p, x, y),
                        square.GetOverlayShape(p, x, y), rotation);
                }

                case MapTool.RaiseHeight:
                    return new SetHeightEdit(square, p, x, y, StepHeight(square, p, x, y, +1));

                case MapTool.LowerHeight:
                    return new SetHeightEdit(square, p, x, y, StepHeight(square, p, x, y, -1));

                case MapTool.ToggleBlockedFlag:
                    return new SetTileFlagsEdit(square, p, x, y,
                        (byte) (square.GetRenderRule(p, x, y) ^ 0x1));

                case MapTool.DeleteTopLocation: {
                    Location target = null;
                    foreach (Location loc in square.GetLocations())
                        if (loc.Plane == p && loc.LocalX == x && loc.LocalY == y)
                            target = loc;
                    return target == null ? null : new RemoveLocationEdit(square, target);
                }

                default:
                    return null;
            }
        }

        /// <summary>
        ///     Moves a tile's height by whole storable steps.
        /// </summary>
        /// <remarks>
        ///     One step is 32 world units, not the 8 of RS2. Step 1 is skipped because the decoder
        ///     maps a stored 1 to 0, so a height of exactly one step below the reference has no
        ///     encoding and would be rejected on save.
        /// </remarks>
        /// <param name="square">The square.</param>
        /// <param name="plane">The plane.</param>
        /// <param name="x">Tile X within the square.</param>
        /// <param name="y">Tile Y within the square.</param>
        /// <param name="direction">+1 to raise, -1 to lower.</param>
        /// <returns>The new height in world units.</returns>
        private static int StepHeight(MapRegion square, int plane, int x, int y, int direction) {
            int reference = plane == 0 ? 0 : square.GetTileHeight(plane - 1, x, y);
            int steps = (reference - square.GetTileHeight(plane, x, y)) / MapRegion.HEIGHT_UNITS_PER_STEP;

            steps += direction;
            if (steps == 1)
                steps += direction;

            steps = Math.Clamp(steps, 0, 255);
            return reference - steps * MapRegion.HEIGHT_UNITS_PER_STEP;
        }

        /// <summary>
        ///     Stages every edited square and commits the cache to disk.
        /// </summary>
        /// <remarks>
        ///     Confirms first, because this rewrites the cache the user opened. Editing stages
        ///     nothing to disk until this runs, so up to here everything is still reversible by
        ///     simply not saving.
        /// </remarks>
        private void SaveEdits() {
            MapScene scene = viewer.Scene;
            if (cache == null || scene == null || cacheDirectory == null)
                return;

            var dirty = new List<(MapRegion Square, int X, int Y)>();
            for (int dx = 0; dx < scene.SquaresX; dx++) {
                for (int dy = 0; dy < scene.SquaresY; dy++) {
                    MapRegion square = scene.Square(dx, dy);
                    if (square != null && square.Dirty)
                        dirty.Add((square, scene.OriginRegionX + dx, scene.OriginRegionY + dy));
                }
            }

            if (dirty.Count == 0) {
                status.Text = "Nothing to save";
                return;
            }

            string names = string.Join(", ", dirty.ConvertAll(d => $"m{d.X}_{d.Y}"));
            string prompt = $"Write {dirty.Count} edited square(s) back to the cache?"
                + Environment.NewLine + Environment.NewLine + names
                + Environment.NewLine + Environment.NewLine + cacheDirectory;

            if (MessageBox.Show(prompt, "Save map edits",
                    MessageBoxButtons.OKCancel, MessageBoxIcon.Warning) != DialogResult.OK)
                return;

            try {
                foreach ((MapRegion square, int x, int y) in dirty)
                    loader.Save(square, x, y);

                cache.WriteCache(cacheDirectory);

                history.Clear();
                status.Text = $"Saved {dirty.Count} square(s) to {cacheDirectory}";
            }
            catch (Exception ex) {
                //A failed save leaves the staged edits in memory, so the user can retry.
                status.Text = "Save failed: " + ex.Message;
                MessageBox.Show(ex.Message, "Save failed", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        private void AfterEdit() {
            viewer.Rerender();
            UpdateInspector(lastHit);
        }

        private void BuildLayout() {
            var split = new SplitContainer {
                Dock = DockStyle.Fill,
                Orientation = Orientation.Vertical,
                SplitterDistance = 220,
                FixedPanel = FixedPanel.Panel1
            };

            //Left: navigation and layers.
            var left = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 4 };
            left.RowStyles.Add(new RowStyle(SizeType.Absolute, 210));
            left.RowStyles.Add(new RowStyle(SizeType.Absolute, 96));
            left.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            left.RowStyles.Add(new RowStyle(SizeType.Absolute, 110));

            var nav = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.LeftToRight, WrapContents = true };
            nav.Controls.Add(new Label { Text = "Region", AutoSize = true, Padding = new Padding(0, 6, 4, 0) });
            nav.Controls.Add(regionX);
            nav.Controls.Add(regionY);
            nav.Controls.Add(goButton);
            nav.Controls.Add(new Label { Text = "Plane", AutoSize = true, Padding = new Padding(0, 6, 4, 0) });
            nav.Controls.Add(planeBox);

            var layersGroup = new GroupBox { Text = "Layers", Dock = DockStyle.Fill };
            layersGroup.Controls.Add(layerList);

            var tools = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.LeftToRight, WrapContents = true };
            tools.Controls.Add(toolBox);
            tools.Controls.Add(new Label { Text = "Value", AutoSize = true, Padding = new Padding(0, 6, 4, 0) });
            tools.Controls.Add(toolValue);
            tools.Controls.Add(undoButton);
            tools.Controls.Add(redoButton);
            tools.Controls.Add(saveButton);

            var toolsGroup = new GroupBox { Text = "Tool", Dock = DockStyle.Fill };
            toolsGroup.Controls.Add(tools);

            var worldGroup = new GroupBox { Text = "World", Dock = DockStyle.Fill };
            worldGroup.Controls.Add(navigator);

            left.Controls.Add(worldGroup, 0, 0);
            left.Controls.Add(nav, 0, 1);
            left.Controls.Add(layersGroup, 0, 2);
            left.Controls.Add(toolsGroup, 0, 3);

            //Right: canvas above, inspector below.
            var right = new SplitContainer {
                Dock = DockStyle.Fill,
                Orientation = Orientation.Horizontal,
                FixedPanel = FixedPanel.Panel2
            };
            right.Panel1.Controls.Add(viewer);
            right.Panel2.Controls.Add(inspector);

            split.Panel1.Controls.Add(left);
            split.Panel2.Controls.Add(right);

            Controls.Add(split);
            Controls.Add(status);

            for (int p = 0; p < 4; p++)
                planeBox.Items.Add("Plane " + p);
            planeBox.SelectedIndex = 0;

            foreach ((string name, MapLayers layer) in LayerRows)
                layerList.Items.Add(name, (MapLayers.Default & layer) != 0);

            foreach ((string label, MapTool _) in ToolRows)
                toolBox.Items.Add(label);
            toolBox.SelectedIndex = 0;

            //SplitterDistance has to be set after the control has a size, or it is silently clamped.
            split.HandleCreated += (_, _) => split.SplitterDistance = 220;
            right.HandleCreated += (_, _) => right.SplitterDistance = Math.Max(100, right.Height - 150);

            status.Text = "No cache loaded";
        }

        /// <summary>
        ///     Binds the panel to a cache and shows the default region.
        /// </summary>
        /// <param name="newCache">The open cache, or <c>null</c> to unbind.</param>
        /// <param name="directory">Where a save should commit to. Null disables saving.</param>
        public void Bind(RSCache newCache, string directory = null) {
            cache = newCache;
            cacheDirectory = directory;

            if (cache == null) {
                loader = null;
                rasteriser = null;
                viewer.Show(null, null);
                navigator.Build(null);
                status.Text = "No cache loaded";
                saveButton.Enabled = false;
                return;
            }

            loader = new MapSquareLoader(cache);
            rasteriser = new MapRasteriser(cache);

            navigator.Build(loader);
            LoadRegion((int) regionX.Value, (int) regionY.Value);
        }

        /// <summary>Loads a square and its apron into the viewer.</summary>
        /// <param name="rx">Region X.</param>
        /// <param name="ry">Region Y.</param>
        public void LoadRegion(int rx, int ry) {
            if (loader == null)
                return;

            if (!loader.Exists(rx, ry)) {
                status.Text = $"m{rx}_{ry} does not exist in this cache";
                return;
            }

            var sw = System.Diagnostics.Stopwatch.StartNew();
            MapScene scene = MapScene.Load(loader, rx, ry);
            long loadMs = sw.ElapsedMilliseconds;

            sw.Restart();
            viewer.Show(scene, rasteriser);
            long renderMs = sw.ElapsedMilliseconds;

            //A square whose locations could not be decrypted renders its terrain and no objects.
            //Saying so is the difference between "this area is empty" and "we cannot read it".
            navigator.SetCurrent(rx, ry);

            string keys = scene.SquaresMissingKeys.Count == 0
                ? "keys ok"
                : $"{scene.SquaresMissingKeys.Count} square(s) missing XTEA keys - objects hidden";

            status.Text = $"m{rx}_{ry}   scene {scene.WidthTiles}x{scene.HeightTiles}   " +
                          $"load {loadMs}ms, render {renderMs}ms   {keys}";
        }

        private void OnLayerToggled(object sender, ItemCheckEventArgs e) {
            //ItemCheck fires before the item's state changes, so read the incoming value.
            MapLayers result = MapLayers.None;
            for (int i = 0; i < LayerRows.Length; i++) {
                bool on = i == e.Index ? e.NewValue == CheckState.Checked : layerList.GetItemChecked(i);
                if (on)
                    result |= LayerRows[i].Layer;
            }

            BeginInvoke(new Action(() => viewer.Layers = result));
        }

        private void UpdateInspector(TileHit hit) {
            MapScene scene = viewer.Scene;
            if (scene == null || hit == null)
                return;

            var sb = new StringBuilder();
            sb.AppendLine($"world {hit.WorldX}, {hit.WorldY}   plane {hit.Plane}");
            sb.AppendLine($"square m{hit.RegionX}_{hit.RegionY}   local {hit.LocalX}, {hit.LocalY}");

            MapRegion square = scene.SquareAt(hit.SceneX, hit.SceneY);
            if (square == null) {
                sb.AppendLine("(no square here)");
                inspector.Text = sb.ToString();
                return;
            }

            sb.AppendLine($"height    {square.GetTileHeight(hit.Plane, hit.LocalX, hit.LocalY)}");
            sb.AppendLine($"underlay  {scene.UnderlayId(hit.Plane, hit.SceneX, hit.SceneY)}");
            sb.AppendLine($"overlay   {scene.OverlayId(hit.Plane, hit.SceneX, hit.SceneY)}" +
                          $"  shape {scene.OverlayShape(hit.Plane, hit.SceneX, hit.SceneY)}" +
                          $"  rot {scene.OverlayRotation(hit.Plane, hit.SceneX, hit.SceneY)}");
            sb.AppendLine($"flags     0x{scene.TileFlags(hit.Plane, hit.SceneX, hit.SceneY):X2}");

            int shown = 0;
            foreach (Location loc in square.GetLocations()) {
                if (loc.Plane != hit.Plane || loc.LocalX != hit.LocalX || loc.LocalY != hit.LocalY)
                    continue;
                if (shown++ == 0)
                    sb.AppendLine("locs:");
                sb.AppendLine($"  id {loc.Id}  shape {loc.Shape} ({LocGroups.Of(loc.Shape)})  rot {loc.Orientation}");
            }

            inspector.Text = sb.ToString();
        }
    }
}
