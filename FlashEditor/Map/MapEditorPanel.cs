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
    ///     The Map tab: a whole-world canvas, navigation, layer toggles and a tile inspector.
    /// </summary>
    /// <remarks>
    ///     Built in code rather than through the designer, so it can be dropped into a tab page
    ///     with one line and does not add to the shared <c>Editor.Designer.cs</c>.
    ///
    ///     The canvas is a virtualised world view rather than one rendered scene, so there is no
    ///     "load this region" step any more and no Region X / Region Y / Go controls: every square
    ///     the cache holds is reachable by panning, and the world navigator jumps to any of them.
    ///     What is left in this class is the editing surface - tools, undo, save and the inspector -
    ///     plus the wiring that keeps the background renderer's settings in step with the toggles.
    /// </remarks>
    public sealed class MapEditorPanel : UserControl {
        private RSCache? cache;
        private string? cacheDirectory;
        private MapSquareLoader loader;
        private MapRasteriser rasteriser;
        private MapSquareStore store;
        private MapTileRenderService service;

        private readonly WorldMapViewControl view = new WorldMapViewControl { Dock = DockStyle.Fill };
        private readonly ComboBox planeBox = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = 60 };
        private readonly Button fitButton = new Button { Text = "Fit world", Width = 76 };
        private readonly Label zoomLabel = new Label { AutoSize = true, Padding = new Padding(6, 6, 0, 0) };
        private readonly CheckedListBox layerList = new CheckedListBox { Dock = DockStyle.Fill, CheckOnClick = true };
        private readonly TextBox inspector = new TextBox { Dock = DockStyle.Fill, Multiline = true, ReadOnly = true, Font = new Font("Consolas", 9F) };
        private readonly Label status = new Label { Dock = DockStyle.Bottom, Height = 22, TextAlign = ContentAlignment.MiddleLeft };

        private readonly ComboBox toolBox = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = 150 };
        private readonly NumericUpDown toolValue = new NumericUpDown { Minimum = 0, Maximum = 255, Value = 1, Width = 60 };
        private readonly Button undoButton = new Button { Text = "Undo", Width = 60, Enabled = false };
        private readonly Button redoButton = new Button { Text = "Redo", Width = 60, Enabled = false };
        private readonly Button saveButton = new Button { Text = "Save cache", Width = 90, Enabled = false };
        private readonly WorldNavigatorControl navigator = new WorldNavigatorControl { Dock = DockStyle.Fill };
        private readonly TrackBar reliefBar = new TrackBar {
            Minimum = 0, Maximum = 100, Value = 65, TickStyle = TickStyle.None, Width = 190
        };

        /// <summary>
        ///     Delays the relief slider's effect until the user stops moving it.
        /// </summary>
        /// <remarks>
        ///     Relief is part of the render signature, so every value change throws away every
        ///     rendered tile - 35 MiB of overview among them - and restarts the sweep. Wired live,
        ///     one drag across the slider would do that a hundred times.
        /// </remarks>
        private readonly System.Windows.Forms.Timer reliefDebounce =
            new System.Windows.Forms.Timer { Interval = 150 };

        private readonly MapEditHistory history = new MapEditHistory();

        private TileHit? lastHit;

        /// <summary>Guards the two-way plane binding between the combo and the canvas.</summary>
        /// <remarks>
        ///     Ctrl+wheel changes the plane on the canvas, which updates the combo, which would
        ///     otherwise push the same value back into the canvas. Both setters already ignore an
        ///     unchanged value, so this is belt as well as braces - but the braces are what stop a
        ///     future clamp or coercion turning that into a loop.
        /// </remarks>
        private bool syncingPlane;

        //A one-entry memo, because hovering inside one square would otherwise rebuild the same 3x3
        //scene on every mouse move.
        private MapScene inspectorScene;
        private int inspectorSceneRegionX = -1;
        private int inspectorSceneRegionY = -1;

        //Whether the memoised scene was built with decoding allowed. A resident-only scene is
        //still worth keeping, but it cannot stand in for one a click asked to have decoded.
        private bool inspectorSceneLoaded;

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
            ("Relief shading", MapLayers.Hillshade),
            ("Game objects", MapLayers.GameObjects),
            ("Tile flags", MapLayers.TileFlags),
            ("Grid", MapLayers.Grid)
        };

        /// <summary>Creates the panel.</summary>
        public MapEditorPanel() {
            Dock = DockStyle.Fill;
            BuildLayout();

            planeBox.SelectedIndexChanged += (_, _) => {
                if (syncingPlane)
                    return;
                view.Plane = planeBox.SelectedIndex;
                UpdateStatus();
            };

            view.PlaneChanged += (_, _) => {
                syncingPlane = true;
                try {
                    planeBox.SelectedIndex = view.Plane;
                }
                finally {
                    syncingPlane = false;
                }

                InvalidateInspectorScene();
                UpdateInspector(lastHit);
                UpdateStatus();
            };

            layerList.ItemCheck += OnLayerToggled;

            view.TileHovered += (_, hit) => {
                lastHit = hit;

                //Below the editing zoom a tile is sub-pixel, and one mouse sweep across a
                //fit-world view crosses hundreds of squares. A per-tile readout is meaningless
                //there and the neighbourhood lookup behind it is not free, so it is skipped.
                if (view.EditingEnabled)
                    UpdateInspector(hit);
                else
                    ShowCoarseInspector(hit);

                UpdateStatus();
            };

            view.TileClicked += OnTileClicked;
            view.ViewChanged += (_, _) => OnViewChanged();

            fitButton.Click += (_, _) => view.FitWorld();

            undoButton.Click += (_, _) => InvalidateFor(UnderStoreLock(history.Undo));
            redoButton.Click += (_, _) => InvalidateFor(UnderStoreLock(history.Redo));
            history.Changed += (_, _) => {
                undoButton.Enabled = history.CanUndo;
                redoButton.Enabled = history.CanRedo;
                saveButton.Enabled = cache != null && history.Count > 0;
            };

            saveButton.Click += (_, _) => SaveEdits();

            reliefBar.Scroll += (_, _) => {
                reliefDebounce.Stop();
                reliefDebounce.Start();
            };

            reliefDebounce.Tick += (_, _) => {
                reliefDebounce.Stop();
                view.ReliefStrength = reliefBar.Value / 100f;
                UpdateStatus();
            };

            navigator.RegionPicked += (_, region) => {
                //Deliberately moves even to a square the cache has nothing for. In a whole-world
                //view open water is a legitimate place to look; refusing only made sense when
                //picking a square meant loading a 3x3 scene of it.
                view.CentreOnRegion(region.X, region.Y);
                navigator.SetCurrent(region.X, region.Y);

                status.Text = store != null && store.Exists(region.X, region.Y)
                    ? $"m{region.X}_{region.Y}"
                    : $"m{region.X}_{region.Y} does not exist in this cache";
            };
        }

        private MapTool SelectedTool =>
            toolBox.SelectedIndex >= 0 ? ToolRows[toolBox.SelectedIndex].Tool : MapTool.Inspect;

        private void OnViewChanged() {
            Rectangle regions = view.Camera.VisibleRegionBounds();
            navigator.SetViewport(new RectangleF(regions.X, regions.Y, regions.Width, regions.Height));

            int rx = Math.Clamp((int) (view.Camera.CentreWorldX / MapRegion.WIDTH), 0, WorldNavigatorControl.WorldSquares - 1);
            int ry = Math.Clamp((int) (view.Camera.CentreWorldY / MapRegion.HEIGHT), 0, WorldNavigatorControl.WorldSquares - 1);
            navigator.SetCurrent(rx, ry);

            UpdateStatus();
        }

        private void OnTileClicked(object sender, TileHit hit) {
            lastHit = hit;

            //A click is a deliberate, one-at-a-time action, so this is the one inspector path
            //allowed to decode. Hovering is not.
            UpdateInspector(hit, loadMissing: true);

            if (SelectedTool == MapTool.Inspect || store == null)
                return;

            if (!view.EditingEnabled) {
                status.Text = $"Zoom in to at least {WorldMapViewControl.MinimumEditingPixelsPerTile:0} px/tile to edit";
                return;
            }

            //Loads rather than reading what happens to be resident. A square that was drawn can
            //still have been evicted behind the sweep, and an edit that silently does nothing
            //because of that is the worst possible failure here.
            MapScene scene = store.SceneAround(hit.RegionX, hit.RegionY, loadMissing: true);

            MapRegion square = scene.SquareAt(hit.WorldX - scene.BaseX, hit.WorldY - scene.BaseY);
            if (square == null)
                return;

            IMapEdit edit = BuildEdit(SelectedTool, square, hit);
            if (edit == null)
                return;

            //Under the store's lock, which is what LocationSnapshot copies under. An add or a
            //remove that grows the live list between the snapshot's sizing and its CopyTo throws
            //on the render thread, and the blanket catch there turns that into a square that never
            //renders and stays a placeholder rectangle.
            UnderStoreLock(() => { history.Apply(edit); return edit; });

            //Pinned before anything else can evict it. The undo history holds this instance, and a
            //reloaded copy would silently orphan every edit already recorded against it.
            store.PinEdited(edit.Target);

            InvalidateFor(edit);
            status.Text = edit.Description;
        }

        /// <summary>
        ///     Runs a history operation holding the square store's lock.
        /// </summary>
        /// <remarks>
        ///     Applying, undoing and redoing all mutate a square's grids and its location list, and
        ///     the render thread reads both. The store's lock is what the render thread's
        ///     <c>LocationSnapshot</c> already takes, so taking it here makes the reader and the
        ///     writer agree on one lock rather than only half the pair being guarded.
        /// </remarks>
        /// <param name="operation">The history operation.</param>
        /// <returns>Whatever the operation returned, so the caller can invalidate what it touched.</returns>
        private IMapEdit UnderStoreLock(Func<IMapEdit> operation) {
            if (store == null)
                return operation();

            IMapEdit result = null!;
            store.RunExclusive(() => result = operation());
            return result;
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
        ///     Redraws whatever an edit touched, and nothing else.
        /// </summary>
        /// <remarks>
        ///     Takes the edit rather than blanket-rerendering, which is why
        ///     <c>MapEditHistory.Undo</c> and <c>Redo</c> return one. A composite edit can straddle
        ///     squares, so every target is invalidated; the tile cache widens each to a 3x3 itself,
        ///     because the blend and the relief both reach across a boundary.
        /// </remarks>
        /// <param name="edit">The edit applied, undone or redone, or <c>null</c> for none.</param>
        private void InvalidateFor(IMapEdit edit) {
            if (edit == null)
                return;

            IEnumerable<MapRegion> targets = edit is CompositeEdit composite
                ? composite.Targets
                : new[] { edit.Target };

            foreach (MapRegion target in targets) {
                if (target == null)
                    continue;

                int id = target.GetRegionID();
                view.InvalidateSquare(MapSquareNames.RegionX(id), MapSquareNames.RegionY(id));
            }

            InvalidateInspectorScene();
            UpdateInspector(lastHit, loadMissing: true);
            UpdateStatus();
        }

        /// <summary>
        ///     Stages every edited square and commits the cache to disk.
        /// </summary>
        /// <remarks>
        ///     Confirms first, because this rewrites the cache the user opened. Editing stages
        ///     nothing to disk until this runs, so up to here everything is still reversible by
        ///     simply not saving.
        ///
        ///     The dirty list comes from the square store rather than from the undo history: a
        ///     square whose every edit has been undone still reports dirty, because
        ///     <c>Region.Dirty</c> is never cleared by an undo, and the store is the only thing that
        ///     knows about every square that was ever touched.
        ///
        ///     The whole write runs under the store's lock, which is the same lock the render thread
        ///     decodes under. Two threads inside the JS5 path at once is not something the cache
        ///     survives.
        /// </remarks>
        private void SaveEdits() {
            if (cache == null || store == null || loader == null || cacheDirectory == null)
                return;

            IReadOnlyList<(MapRegion Square, int RegionX, int RegionY)> dirty = store.DirtySquares();

            if (dirty.Count == 0) {
                status.Text = "Nothing to save";
                return;
            }

            var names = new List<string>();
            foreach ((MapRegion Square, int RegionX, int RegionY) entry in dirty)
                names.Add($"m{entry.RegionX}_{entry.RegionY}");

            string prompt = $"Write {dirty.Count} edited square(s) back to the cache?"
                + Environment.NewLine + Environment.NewLine + string.Join(", ", names)
                + Environment.NewLine + Environment.NewLine + cacheDirectory;

            if (MessageBox.Show(prompt, "Save map edits",
                    MessageBoxButtons.OKCancel, MessageBoxIcon.Warning) != DialogResult.OK)
                return;

            try {
                store.RunExclusive(() => {
                    foreach ((MapRegion Square, int RegionX, int RegionY) entry in dirty)
                        loader.Save(entry.Square, entry.RegionX, entry.RegionY);

                    cache.WriteCache(cacheDirectory);
                });

                history.Clear();
                status.Text = $"Saved {dirty.Count} square(s) to {cacheDirectory}";
            }
            catch (Exception ex) {
                //A failed save leaves the staged edits in memory, so the user can retry.
                status.Text = "Save failed: " + ex.Message;
                MessageBox.Show(ex.Message, "Save failed", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
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
            nav.Controls.Add(new Label { Text = "Plane", AutoSize = true, Padding = new Padding(0, 6, 4, 0) });
            nav.Controls.Add(planeBox);
            nav.Controls.Add(fitButton);
            nav.Controls.Add(zoomLabel);

            var layersGroup = new GroupBox { Text = "Layers", Dock = DockStyle.Fill };

            var layersBody = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 2 };
            layersBody.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            layersBody.RowStyles.Add(new RowStyle(SizeType.Absolute, 52));

            var reliefRow = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.LeftToRight, WrapContents = true };
            reliefRow.Controls.Add(new Label { Text = "Relief", AutoSize = true, Padding = new Padding(0, 6, 4, 0) });
            reliefRow.Controls.Add(reliefBar);

            layersBody.Controls.Add(layerList, 0, 0);
            layersBody.Controls.Add(reliefRow, 0, 1);
            layersGroup.Controls.Add(layersBody);

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
            right.Panel1.Controls.Add(view);
            right.Panel2.Controls.Add(inspector);

            split.Panel1.Controls.Add(left);
            split.Panel2.Controls.Add(right);

            Controls.Add(split);
            Controls.Add(status);

            for (int p = 0; p < 4; p++)
                planeBox.Items.Add("Plane " + p);
            planeBox.SelectedIndex = 0;

            //Seeded from the same constant the renderer defaults to, so the boxes cannot disagree
            //with what is actually drawn. Ground decoration, game objects and tile flags are off.
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
        ///     Binds the panel to a cache and opens the world view on it.
        /// </summary>
        /// <remarks>
        ///     Opens centred on region 50,50 at one pixel per tile, which puts roughly 25 by 16
        ///     squares on screen, and immediately queues a whole-world overview sweep so that every
        ///     region becomes viewable without another click. "Fit world" and Home jump straight to
        ///     the fully zoomed-out view.
        /// </remarks>
        /// <param name="newCache">The open cache, or <c>null</c> to unbind.</param>
        /// <param name="directory">Where a save should commit to. Null disables saving.</param>
        public void Bind(RSCache? newCache, string? directory = null) {
            //Idempotent, because Editor.LoadEditorTab calls this on every visit to the Map tab and
            //not just the first. Without the guard, switching to another tab and back tore down
            //the store - dropping the pinned dictionary that holds every edited square, which the
            //undo history still points at - cleared the history, freed the whole rendered overview
            //band and restarted the 1684-square sweep. The edits went with no prompt and no
            //message, and the save button greyed itself out on the way. Identity is the right test
            //because opening a cache builds a new RSCache, which is exactly when a rebind is due.
            if (ReferenceEquals(newCache, cache) && store != null && directory == cacheDirectory)
                return;

            cache = newCache;
            cacheDirectory = directory;

            //Torn down in dependency order: the service owns the render thread and that thread is
            //the only other user of the rasteriser and the store.
            service?.Dispose();
            service = null;
            store?.Dispose();
            store = null;
            rasteriser?.Dispose();
            rasteriser = null;

            view.Bind(null, null);
            InvalidateInspectorScene();
            history.Clear();

            if (cache == null) {
                loader = null;
                navigator.Build(null);
                status.Text = "No cache loaded";
                saveButton.Enabled = false;
                return;
            }

            loader = new MapSquareLoader(cache);
            rasteriser = new MapRasteriser(cache);
            store = new MapSquareStore(loader);
            service = new MapTileRenderService(store, rasteriser);

            navigator.Build(store.PresenceMap, store.SquareCount);

            view.Layers = CheckedLayers();
            view.ReliefStrength = reliefBar.Value / 100f;

            //Camera first, then bind. Binding queues the whole-world sweep, and the sweep starts
            //at the camera's own world row so that the area being looked at fills in first. Bound
            //the other way round the camera was still at its constructed default of row 128, so
            //the opening view at row 50 was reached 178 rows into the sweep.
            view.CentreOnRegion(WorldMapViewControl.InitialRegionX, WorldMapViewControl.InitialRegionY);
            view.Bind(store, service);

            UpdateStatus();
        }

        /// <summary>
        ///     Centres the world view on a square.
        /// </summary>
        /// <remarks>
        ///     Kept for callers that used to load a region: there is nothing to load any more, so
        ///     this is a pan. Safe before a cache is bound.
        /// </remarks>
        /// <param name="rx">Region X.</param>
        /// <param name="ry">Region Y.</param>
        public void LoadRegion(int rx, int ry) {
            view.CentreOnRegion(rx, ry);
            navigator.SetCurrent(rx, ry);

            if (store == null)
                return;

            status.Text = store.Exists(rx, ry)
                ? $"m{rx}_{ry}"
                : $"m{rx}_{ry} does not exist in this cache";
        }

        private MapLayers CheckedLayers() {
            MapLayers result = MapLayers.None;
            for (int i = 0; i < LayerRows.Length; i++)
                if (layerList.GetItemChecked(i))
                    result |= LayerRows[i].Layer;
            return result;
        }

        private void OnLayerToggled(object sender, ItemCheckEventArgs e) {
            //ItemCheck fires before the item's state changes, so read the incoming value.
            MapLayers result = MapLayers.None;
            for (int i = 0; i < LayerRows.Length; i++) {
                bool on = i == e.Index ? e.NewValue == CheckState.Checked : layerList.GetItemChecked(i);
                if (on)
                    result |= LayerRows[i].Layer;
            }

            BeginInvoke(new Action(() => {
                view.Layers = result;
                UpdateStatus();
            }));
        }

        /// <summary>
        ///     The 3x3 scene around a square, memoised for one square at a time.
        /// </summary>
        /// <remarks>
        ///     The memo bounds the work to once per square the cursor crosses into rather than once
        ///     per mouse move, but it is not enough on its own. A memo miss that decodes costs nine
        ///     <c>GetOrLoad</c> calls on the UI thread, each of which can be a full JS5 read behind
        ///     the store's lock, and at fit-world zoom a square is four screen pixels wide - one
        ///     mouse sweep crosses hundreds of them. So hovering reads only what is already
        ///     resident, and only a click is allowed to decode.
        ///
        ///     A resident-only scene is memoised too, and is upgraded rather than reused when a
        ///     later caller does want the decode.
        /// </remarks>
        /// <param name="regionX">Region X of the centre square.</param>
        /// <param name="regionY">Region Y of the centre square.</param>
        /// <param name="loadMissing"><c>true</c> to decode absent squares, which blocks.</param>
        /// <returns>The scene, or <c>null</c> when nothing is bound.</returns>
        private MapScene SceneFor(int regionX, int regionY, bool loadMissing) {
            if (store == null)
                return null;

            if (inspectorScene != null && inspectorSceneRegionX == regionX && inspectorSceneRegionY == regionY
                && (inspectorSceneLoaded || !loadMissing))
                return inspectorScene;

            inspectorScene = store.SceneAround(regionX, regionY, loadMissing);
            inspectorSceneRegionX = regionX;
            inspectorSceneRegionY = regionY;
            inspectorSceneLoaded = loadMissing;
            return inspectorScene;
        }

        private void InvalidateInspectorScene() {
            inspectorScene = null;
            inspectorSceneRegionX = -1;
            inspectorSceneRegionY = -1;
            inspectorSceneLoaded = false;
        }

        /// <summary>
        ///     The two lines worth showing when the zoom is too coarse for a per-tile readout.
        /// </summary>
        /// <remarks>
        ///     Cheap on purpose: it touches neither the store nor the cache, which is what makes it
        ///     safe to run on every hover at a zoom where a mouse sweep crosses hundreds of squares.
        /// </remarks>
        /// <param name="hit">The tile under the cursor.</param>
        private void ShowCoarseInspector(TileHit? hit) {
            if (hit == null)
                return;

            inspector.Text =
                $"world {hit.WorldX}, {hit.WorldY}   plane {hit.Plane}" + Environment.NewLine +
                $"square m{hit.RegionX}_{hit.RegionY}" + Environment.NewLine +
                $"zoom to {WorldMapViewControl.MinimumEditingPixelsPerTile:0} px/tile or closer for tile detail";
        }

        private void UpdateInspector(TileHit? hit, bool loadMissing = false) {
            if (hit == null)
                return;

            MapScene scene = SceneFor(hit.RegionX, hit.RegionY, loadMissing);
            if (scene == null)
                return;

            int sceneX = hit.WorldX - scene.BaseX;
            int sceneY = hit.WorldY - scene.BaseY;

            var sb = new StringBuilder();
            sb.AppendLine($"world {hit.WorldX}, {hit.WorldY}   plane {hit.Plane}");
            sb.AppendLine($"square m{hit.RegionX}_{hit.RegionY}   local {hit.LocalX}, {hit.LocalY}");

            MapRegion square = scene.SquareAt(sceneX, sceneY);
            if (square == null) {
                //A square the cache carries but that is not resident is still being decoded behind
                //the sweep. Saying "no square here" for it would report empty water where there is
                //terrain the user is about to see appear.
                sb.AppendLine(store.Exists(hit.RegionX, hit.RegionY) ? "(decoding...)" : "(no square here)");
                inspector.Text = sb.ToString();
                return;
            }

            sb.AppendLine($"height    {square.GetTileHeight(hit.Plane, hit.LocalX, hit.LocalY)}");
            sb.AppendLine($"underlay  {scene.UnderlayId(hit.Plane, sceneX, sceneY)}");
            sb.AppendLine($"overlay   {scene.OverlayId(hit.Plane, sceneX, sceneY)}" +
                          $"  shape {scene.OverlayShape(hit.Plane, sceneX, sceneY)}" +
                          $"  rot {scene.OverlayRotation(hit.Plane, sceneX, sceneY)}");
            sb.AppendLine($"flags     0x{scene.TileFlags(hit.Plane, sceneX, sceneY):X2}");

            //The tile's four corner vertices. On a square boundary these come from the neighbouring
            //square, so they are also the quickest way to see the vertex resolution working.
            sb.AppendLine($"corners   sw {scene.VertexHeight(hit.Plane, sceneX, sceneY)}" +
                          $"  se {scene.VertexHeight(hit.Plane, sceneX + 1, sceneY)}" +
                          $"  nw {scene.VertexHeight(hit.Plane, sceneX, sceneY + 1)}" +
                          $"  ne {scene.VertexHeight(hit.Plane, sceneX + 1, sceneY + 1)}");

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

        /// <summary>
        ///     Rewrites the status line.
        /// </summary>
        /// <remarks>
        ///     The missing-key figure is now a property of the whole cache rather than of one loaded
        ///     scene, and it grows as squares are decoded - which is strictly more than the old
        ///     per-scene count could say, and keeps "this area is unreadable" distinguishable from
        ///     "this area is empty".
        ///
        ///     Every figure here has to be lock-free and allocation-free. This runs on every pan,
        ///     zoom and hover, so anything that takes the store's lock stalls the drag behind
        ///     whatever square the render thread is decoding, and steals the lock back from it
        ///     sixty times a second while it does.
        /// </remarks>
        private void UpdateStatus() {
            zoomLabel.Text = $"{view.Camera.PixelsPerTile:0.###} px/tile";

            if (store == null || service == null) {
                status.Text = cache == null ? "No cache loaded" : "No map index";
                return;
            }

            string where = lastHit == null ? "-" : $"m{lastHit.RegionX}_{lastHit.RegionY}";
            int missing = store.MissingKeyCount;

            string keys = missing == 0
                ? "keys ok"
                : $"{missing} square(s) missing XTEA keys - objects hidden";

            status.Text = $"plane {view.Plane}   {view.Camera.PixelsPerTile:0.###} px/tile   {where}   " +
                          $"{service.RenderedSquareCount} of {store.SquareCount} rendered, {service.PendingCount} queued   " +
                          (view.EditingEnabled ? "" : "zoom in to edit   ") + keys;
        }

        /// <summary>
        ///     Runs a whole-cache operation with the map's render thread held off.
        /// </summary>
        /// <remarks>
        ///     For the editor's File menu. Saving replaces the dat2 and every index file on disk
        ///     while the map's render thread may be part way through a JS5 read, and the store's
        ///     lock is the one that thread decodes under. <c>SaveEdits</c> already does this for
        ///     its own save; the menu path has to go through the same gate or the render thread
        ///     decodes across the file replacement and caches whatever it got as real terrain.
        ///
        ///     Safe before a cache is bound, in which case it simply runs the action.
        /// </remarks>
        /// <param name="action">What to run.</param>
        public void RunExclusive(Action action) {
            if (action == null) throw new ArgumentNullException(nameof(action));

            if (store == null)
                action();
            else
                store.RunExclusive(action);
        }

        /// <inheritdoc/>
        protected override void Dispose(bool disposing) {
            if (disposing) {
                reliefDebounce.Stop();
                reliefDebounce.Dispose();

                //Order matters: the render thread is the only other user of the store and the
                //rasteriser, so it has to be joined before either is torn down.
                service?.Dispose();
                store?.Dispose();
                rasteriser?.Dispose();
            }

            base.Dispose(disposing);
        }
    }
}
