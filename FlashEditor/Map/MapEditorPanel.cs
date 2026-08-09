using System;
using System.Collections.Generic;
using System.Drawing;
using System.Text;
using System.Windows.Forms;
using FlashEditor.Cache;
using FlashEditor.Cache.Region;
using FlashEditor.Definitions;
using FlashEditor.UI;

using MapRegion = FlashEditor.Cache.Region.Region;
using FlashEditor.Definitions.Entities;

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

        /// <summary>
        ///     Width of the left control column, in real pixels.
        /// </summary>
        /// <remarks>
        ///     The navigator is square - <c>WorldNavigatorControl.ThumbnailArea</c> takes the smaller
        ///     of the two sides - so the column's width, not its height, is what caps how big the
        ///     world thumbnail can get. Giving it the stretchy row is only half of making it legible;
        ///     the other half is this.
        /// </remarks>
        private const int LeftColumnWidth = 250;

        private readonly WorldMapViewControl view = new WorldMapViewControl { Dock = DockStyle.Fill };
        private readonly ComboBox planeBox = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList };
        private readonly Button fitButton = new Button {
            Text = "Fit world", AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink
        };
        private readonly Label zoomLabel = new Label { AutoSize = true };
        private readonly CheckedListBox layerList = new CheckedListBox { Dock = DockStyle.Fill, CheckOnClick = true };
        private readonly TextBox inspector = new TextBox { Dock = DockStyle.Fill, Multiline = true, ReadOnly = true, Font = new Font("Consolas", 9F) };
        private readonly Label status = new Label { Dock = DockStyle.Bottom, Height = 22, TextAlign = ContentAlignment.MiddleLeft };

        private readonly ComboBox toolBox = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList };

        /* Every floor material in the cache, as swatches. The tool box and its unlabelled number
           stay for now - this is what makes them usable rather than what replaces them, and
           replacing them is the rest of item 20. */
        private readonly FloorMaterialPalette materials = new FloorMaterialPalette();
        private readonly NumericUpDown toolValue = new NumericUpDown { Minimum = 0, Maximum = 255, Value = 1 };
        private readonly Button undoButton = new Button {
            Text = "Undo", Enabled = false, AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink
        };
        private readonly Button redoButton = new Button {
            Text = "Redo", Enabled = false, AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink
        };
        private readonly Button saveButton = new Button {
            Text = "Save cache", Enabled = false, AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink
        };
        private readonly WorldNavigatorControl navigator = new WorldNavigatorControl { Dock = DockStyle.Fill };
        private readonly TrackBar reliefBar = new TrackBar {
            Minimum = 0, Maximum = 100, Value = 65, TickStyle = TickStyle.None
        };

        //Held because ApplyMeasuredSizes has to reach back into them once the font is final.
        private GroupBox worldGroup;
        private TableLayoutPanel layersBody;

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

        /// <summary>
        ///     Keeps the status line's render counters honest while the sweep runs.
        /// </summary>
        /// <remarks>
        ///     Nothing else moved them. <see cref="UpdateStatus"/> only ran on a pan, a zoom, a hover
        ///     or an edit, so the figures the user read were whatever the last mouse event happened to
        ///     leave behind - which is how a screenshot taken well into a session showed "0 of 1684
        ///     rendered, 1683 queued" over a fully drawn world: one hover, seconds after the tab
        ///     opened, froze the line at the state the sweep was in then.
        ///
        ///     Polled rather than driven from <c>MapTileRenderService.TilesReady</c>, which fires once
        ///     per tile on the render thread. Marshalling 1684 of those to the UI thread costs more
        ///     than the renders do, which is the same reason the view polls <c>ReadyCount</c>.
        /// </remarks>
        private readonly System.Windows.Forms.Timer statusTimer =
            new System.Windows.Forms.Timer { Interval = 400 };

        private readonly MapEditHistory history = new MapEditHistory();

        /// <summary>
        ///     Whether the status line currently holds the live readout rather than a one-off message.
        /// </summary>
        /// <remarks>
        ///     The poll must not wipe "Saved 3 square(s)" a third of a second after the save. A
        ///     message clears this; the next hover, pan or zoom puts the live line back.
        /// </remarks>
        private bool statusShowsProgress;

        //What the last live readout reported, so the poll can skip rebuilding an identical string.
        private int lastRenderedSquares = -1;
        private int lastQueuedTiles = -1;

        private TileHit? lastHit;

        /// <summary>
        ///     What the most recent edit did, in numbers, for the foot of the inspector.
        /// </summary>
        /// <remarks>
        ///     Survives the flash on purpose. The flash is under a second and says where; this says
        ///     what, and is still there when the user looks down from the map to work out whether
        ///     the click did anything.
        /// </remarks>
        private string? lastEditNote;

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
            PlaceLocation,
            RotateTopLocation,
            CycleTopLocationShape,
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
            ("Place location", MapTool.PlaceLocation),
            ("Rotate top location", MapTool.RotateTopLocation),
            ("Cycle top location shape", MapTool.CycleTopLocationShape),
            ("Delete top location", MapTool.DeleteTopLocation)
        };

        /// <summary>
        ///     The upper bound the Value box takes for each tool, and why.
        /// </summary>
        /// <remarks>
        ///     Not cosmetic. An underlay id is written back as <c>id + 81</c> in a single byte
        ///     (<c>RegionCodec.EncodeTile</c>), so 175 and above wraps and the tile silently decodes
        ///     as something else entirely - the box allowed 255 before this existed. An overlay id is
        ///     written as a bare byte and so reaches 255, and an object id is a smart delta with no
        ///     byte to overflow, which is what lets the place tool address the whole of index 16.
        /// </remarks>
        private static int MaximumValueFor(MapTool tool) {
            switch (tool) {
                case MapTool.PaintUnderlay:
                    return 174;
                case MapTool.PaintOverlay:
                    return 255;
                case MapTool.PlaceLocation:
                    return 65535;
                default:
                    return 255;
            }
        }

        /// <summary>
        ///     The shape a freshly placed location takes.
        /// </summary>
        /// <remarks>
        ///     10 is the ordinary standing game object. Shapes 0-3 are walls, 4-8 wall decorations
        ///     and 22 is ground decoration (Class64_Sub17.anIntArray3685), all of which need a wall
        ///     or a floor to make sense of; 10 is the one that renders anywhere. Cycle it afterwards
        ///     with the shape tool.
        /// </remarks>
        private const int PlacedLocationShape = 10;

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

            //BuildLayout has already selected the first tool, so the bound is applied once here as
            //well as on every later change - otherwise the box would keep whatever maximum it was
            //constructed with until the user touched the combo.
            toolBox.SelectedIndexChanged += (_, _) => ApplyToolSelection();
            ApplyToolSelection();

            materials.Picked += (_, pick) => LoadBrush(pick);

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

            undoButton.Click += (_, _) => StepHistory(history.Undo, "undone", reversed: true);
            redoButton.Click += (_, _) => StepHistory(history.Redo, "redone", reversed: false);
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

            statusTimer.Tick += (_, _) => {
                if (store == null || service == null || !statusShowsProgress)
                    return;

                //Rebuilt only when a figure has actually moved. The sweep publishes a square every
                //few milliseconds at the start and then goes quiet for seconds at a time.
                if (service.RenderedSquareCount == lastRenderedSquares && service.PendingCount == lastQueuedTiles)
                    return;

                UpdateStatus();
            };

            navigator.RegionPicked += (_, region) => {
                //Deliberately moves even to a square the cache has nothing for. In a whole-world
                //view open water is a legitimate place to look; refusing only made sense when
                //picking a square meant loading a 3x3 scene of it.
                view.CentreOnRegion(region.X, region.Y);
                navigator.SetCurrent(region.X, region.Y);

                ShowMessage(store != null && store.Exists(region.X, region.Y)
                    ? $"m{region.X}_{region.Y}"
                    : $"m{region.X}_{region.Y} does not exist in this cache");
            };
        }

        /// <inheritdoc/>
        protected override void OnHandleCreated(EventArgs e) {
            base.OnHandleCreated(e);

            //The earliest point at which the form's font auto-scaling has certainly run, which is
            //what every measurement in here depends on.
            ApplyMeasuredSizes();
            statusTimer.Start();
        }

        /// <inheritdoc/>
        protected override void OnHandleDestroyed(EventArgs e) {
            statusTimer.Stop();
            base.OnHandleDestroyed(e);
        }

        /// <inheritdoc/>
        protected override void OnFontChanged(EventArgs e) {
            base.OnFontChanged(e);

            //Fires during the form's auto-scaling and again on a DPI change, both of which move
            //every number ApplyMeasuredSizes derives.
            if (layersBody != null)
                ApplyMeasuredSizes();
        }

        /// <summary>
        ///     Arms the paint tool for a material the user picked out of the palette.
        /// </summary>
        /// <remarks>
        ///     Selects the tool as well as the value, because picking a colour and then finding the
        ///     brush still set to "Toggle blocked flag" is the kind of surprise that makes a palette
        ///     feel unreliable.
        ///     <para>
        ///     <b>The underlay cap is honoured here rather than routed around.</b> A tile stores an
        ///     underlay as id + 81 in a single byte, so 174 is the highest that survives the
        ///     encoder - and the palette shows every record the table declares, which on the vanilla
        ///     capture runs past that. A swatch above the cap is refused out loud rather than
        ///     silently clamped to something the user did not pick.
        ///     </para>
        /// </remarks>
        /// <param name="pick">What the user chose.</param>
        private void LoadBrush(FloorPick pick) {
            MapTool wanted = pick.Kind == FloorKind.Underlay
                ? MapTool.PaintUnderlay
                : MapTool.PaintOverlay;

            for (int i = 0; i < ToolRows.Length; i++) {
                if (ToolRows[i].Tool != wanted)
                    continue;

                toolBox.SelectedIndex = i;
                break;
            }

            if (pick.Id > toolValue.Maximum) {
                ShowMessage(pick.Kind + " " + pick.Id + " is past the " + toolValue.Maximum +
                    " a tile can store for it, so the brush was left where it was.");
                return;
            }

            toolValue.Value = pick.Id;
            ShowMessage("Brush set to " + pick.Kind.ToString().ToLowerInvariant() + " " + pick.Id);
        }

        private MapTool SelectedTool =>
            toolBox.SelectedIndex >= 0 ? ToolRows[toolBox.SelectedIndex].Tool : MapTool.Inspect;

        /// <summary>
        ///     Applies everything that depends on which tool is selected.
        /// </summary>
        /// <remarks>
        ///     Two things: the Value box narrows to the range the tool can encode, and the canvas is
        ///     told whether to draw the height-vertex affordance. The affordance is on only for the
        ///     height tools, because it is an explanation of what they do rather than a general
        ///     grid, and drawn under every tool it would be permanent clutter.
        /// </remarks>
        private void ApplyToolSelection() {
            toolValue.Maximum = MaximumValueFor(SelectedTool);
            view.ShowVertexAffordance =
                SelectedTool == MapTool.RaiseHeight || SelectedTool == MapTool.LowerHeight;
        }

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
                ShowMessage($"Zoom in to at least {WorldMapViewControl.MinimumEditingPixelsPerTile:0} px/tile to edit");
                return;
            }

            //Loads rather than reading what happens to be resident. A square that was drawn can
            //still have been evicted behind the sweep, and an edit that silently does nothing
            //because of that is the worst possible failure here.
            MapScene scene = store.SceneAround(hit.RegionX, hit.RegionY, loadMissing: true);

            MapRegion square = scene.SquareAt(hit.WorldX - scene.BaseX, hit.WorldY - scene.BaseY);
            if (square == null)
                return;

            IMapEdit? edit = BuildEdit(SelectedTool, square, hit);
            if (edit == null) {
                //A refusal used to be completely silent - the tool fired, declined, and neither the
                //map nor the status line said anything. That is the worst case of "it isn't clear
                //what edits are happening", because nothing distinguishes it from a working edit
                //whose result is invisible.
                view.Flash(hit.WorldX, hit.WorldY, 1, 1, hit.Plane, MapFlashKind.Rejected, "no change");
                ShowMessage(RefusalReason(SelectedTool));
                return;
            }

            //Under the store's lock, which is what LocationSnapshot copies under. An add or a
            //remove that grows the live list between the snapshot's sizing and its CopyTo throws
            //on the render thread, and the blanket catch there turns that into a square that never
            //renders and stays a placeholder rectangle.
            UnderStoreLock(() => { history.Apply(edit); return edit; });

            //Pinned before anything else can evict it. The undo history holds this instance, and a
            //reloaded copy would silently orphan every edit already recorded against it.
            store.PinEdited(edit.Target);

            lastEditNote = EditNote(edit, "last edit");
            InvalidateFor(edit);
            ShowMessage(edit.Description + HeightVisibilityWarning(edit));
        }

        /// <summary>
        ///     Why a tool declined to change anything, in the user's terms.
        /// </summary>
        /// <remarks>
        ///     Every case here is a tool that needs something already on the tile to act on. Saying
        ///     which is the difference between a refusal and an apparent malfunction.
        /// </remarks>
        /// <param name="tool">The tool that declined.</param>
        /// <returns>The message to show.</returns>
        private static string RefusalReason(MapTool tool) {
            switch (tool) {
                case MapTool.CycleOverlayShape:
                case MapTool.CycleOverlayRotation:
                    return "No overlay on this tile - paint one first, then cycle it";
                case MapTool.RotateTopLocation:
                case MapTool.CycleTopLocationShape:
                case MapTool.DeleteTopLocation:
                    return "No object on this tile";
                default:
                    return "Nothing to change on this tile";
            }
        }

        /// <summary>
        ///     Warns when a height edit has been written but cannot be seen.
        /// </summary>
        /// <remarks>
        ///     A flat top-down view has exactly one way to show terrain elevation, and that is
        ///     relief shading. With the layer unticked or the slider near zero a height edit is
        ///     applied correctly, saves correctly, and changes not one pixel - which reads as the
        ///     tool being broken. The edit flash still fires, so the warning explains the gap
        ///     between "something happened here" and "the terrain looks identical".
        /// </remarks>
        /// <param name="edit">The edit just applied.</param>
        /// <returns>The clause to append, or an empty string.</returns>
        private string HeightVisibilityWarning(IMapEdit edit) {
            if (edit is not SetHeightEdit)
                return "";

            if ((CheckedLayers() & MapLayers.Hillshade) == 0)
                return "  -  tick Relief shading to see it: a height change is only visible as shading";

            //Below about a sixth of the range a single 32-unit step shades by under two levels of
            //grey, which on textured terrain is indistinguishable from nothing.
            return reliefBar.Value < 15
                ? "  -  raise the Relief slider to see it: one step barely shades at this strength"
                : "";
        }

        /// <summary>
        ///     The "last edit" block for the inspector, which outlives the flash.
        /// </summary>
        /// <remarks>
        ///     The flash says <em>where</em> and lasts under a second. This says <em>what</em>, in
        ///     numbers, and stays until the next edit - which is what makes a height change
        ///     readable at all, since the terrain itself only moves by one shading step.
        /// </remarks>
        /// <param name="edit">The edit applied, undone or redone.</param>
        /// <param name="heading">
        ///     What to call it - "last edit", "undone", "redone". Continuation lines are indented to
        ///     match, so the heading can be any length.
        /// </param>
        /// <param name="reversed"><c>true</c> when the edit was undone rather than applied.</param>
        /// <returns>The block, or <c>null</c> when the edit cannot describe itself.</returns>
        private static string? EditNote(IMapEdit edit, string heading, bool reversed = false) {
            if (edit is not IMapEditArea area)
                return null;

            string[] lines = DescribeEdit(edit, area, reversed);
            if (lines.Length == 0)
                return null;

            string indent = new string(' ', heading.Length + 2);
            var sb = new StringBuilder();

            sb.AppendLine(heading + "  " + lines[0]);
            for (int i = 1; i < lines.Length; i++)
                sb.AppendLine(indent + lines[i]);

            return sb.ToString().TrimEnd();
        }

        /// <summary>
        ///     What an edit did, one array entry per line.
        /// </summary>
        /// <remarks>
        ///     A height edit gets three lines and everything else gets one, which is the whole
        ///     asymmetry this feature is about: the other tools change something the map draws
        ///     directly, while a height change is only ever visible as a shading difference and has
        ///     to be spelled out in numbers to be checkable at all.
        /// </remarks>
        /// <param name="edit">The edit.</param>
        /// <param name="area">Its tile area.</param>
        /// <param name="reversed">
        ///     <c>true</c> when the edit was undone, which swaps every before and after. A note that
        ///     read "-960 -> -992" after an undo would name the height the tile no longer has.
        /// </param>
        /// <returns>The lines.</returns>
        private static string[] DescribeEdit(IMapEdit edit, IMapEditArea area, bool reversed) {
            string where = $"at {area.LocalX},{area.LocalY}";

            switch (edit) {
                case SetHeightEdit height: {
                    int steps = reversed ? -height.StepDelta : height.StepDelta;
                    int from = reversed ? height.NewHeight : height.OldHeight;
                    int to = reversed ? height.OldHeight : height.NewHeight;
                    string verb = steps > 0 ? "raised" : steps < 0 ? "lowered" : "left";

                    return new[] {
                        $"{verb} the SW vertex of tile {area.LocalX},{area.LocalY}",
                        $"{from} -> {to} units"
                        + $"  ({steps:+0;-0;0} step of {MapRegion.HEIGHT_UNITS_PER_STEP}, negative is up)",
                        "4 tiles share that vertex, so the surface bends - the tile does not lift"
                    };
                }

                case SetUnderlayEdit underlay:
                    return new[] {
                        reversed
                            ? $"underlay {underlay.NewId} -> {underlay.OldId} {where}"
                            : $"underlay {underlay.OldId} -> {underlay.NewId} {where}"
                    };

                case SetOverlayEdit overlay:
                    return new[] {
                        reversed
                            ? $"overlay {overlay.NewId} -> {overlay.OldId} {where}"
                            : $"overlay {overlay.OldId} -> {overlay.NewId} {where}"
                              + $"  shape {overlay.NewShape}  rot {overlay.NewRotation}"
                    };

                case SetTileFlagsEdit flags:
                    return new[] {
                        reversed
                            ? $"flags 0x{flags.NewFlags:X2} -> 0x{flags.OldFlags:X2} {where}"
                            : $"flags 0x{flags.OldFlags:X2} -> 0x{flags.NewFlags:X2} {where}"
                    };

                case AddLocationEdit add:
                    return new[] {
                        reversed
                            ? $"removed object {add.Location.Id} {where}"
                            : $"placed object {add.Location.Id} {where}"
                              + $"  footprint {area.TilesWide}x{area.TilesHigh}"
                    };

                case RemoveLocationEdit remove:
                    return new[] {
                        reversed
                            ? $"restored object {remove.Location.Id} {where}"
                            : $"deleted object {remove.Location.Id} {where}"
                    };

                case ReplaceLocationEdit replace: {
                    Location shown = reversed ? replace.Original : replace.Replacement;
                    return new[] {
                        $"object {shown.Id} {where}  shape {shown.Shape}  rot {shown.Orientation}"
                    };
                }

                default:
                    return new[] { edit.Description };
            }
        }

        /// <summary>
        ///     Undoes or redoes a step, and reports it the same way an edit is reported.
        /// </summary>
        /// <remarks>
        ///     Undo used to be the quietest thing in the panel: the button ran, the map changed
        ///     somewhere, and the status line still held whatever the last edit had put there. It
        ///     now flashes the tiles it touched and names the step, which matters most when the
        ///     reverted edit is off screen.
        /// </remarks>
        /// <param name="operation">The history operation.</param>
        /// <param name="heading">What to call it in the inspector.</param>
        /// <param name="reversed">
        ///     <c>true</c> for undo, which runs the edit backwards. Redo re-applies it forwards and
        ///     so reports exactly as the original edit did.
        /// </param>
        private void StepHistory(Func<IMapEdit> operation, string heading, bool reversed) {
            IMapEdit edit = UnderStoreLock(operation);
            if (edit == null)
                return;

            lastEditNote = EditNote(edit, heading, reversed);
            InvalidateFor(edit, reversed);
            ShowMessage($"{heading}: {edit.Description}");
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

        /// <summary>
        ///     Turns a click into the edit the selected tool would apply, if any.
        /// </summary>
        /// <remarks>
        ///     Nullable because most tools decline on some tiles - cycling a shape needs an overlay
        ///     to cycle and the location tools need a location to act on - and a decline has to be
        ///     distinguishable from an edit so the caller does not push an empty entry onto the
        ///     undo stack.
        /// </remarks>
        /// <param name="tool">The selected tool.</param>
        /// <param name="square">The square the click landed on.</param>
        /// <param name="hit">The tile the click landed on.</param>
        /// <returns>The edit, or <c>null</c> when the tool has nothing to do on this tile.</returns>
        private IMapEdit? BuildEdit(MapTool tool, MapRegion square, TileHit hit) {
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

                case MapTool.PlaceLocation: {
                    (int wide, int high) = Footprint(value, 0);
                    return new AddLocationEdit(square,
                        NewLocation(square, value, PlacedLocationShape, 0, p, x, y), wide, high);
                }

                case MapTool.RotateTopLocation: {
                    Location? target = TopLocation(square, p, x, y);
                    if (target == null)
                        return null;

                    int rotated = (target.Orientation + 1) & 3;
                    (int wide, int high) = Footprint(target.Id, rotated);
                    return new ReplaceLocationEdit(square, target,
                        NewLocation(square, target.Id, target.Shape, rotated, p, x, y), wide, high);
                }

                case MapTool.CycleTopLocationShape: {
                    Location? target = TopLocation(square, p, x, y);
                    if (target == null)
                        return null;

                    (int wide, int high) = Footprint(target.Id, target.Orientation);
                    return new ReplaceLocationEdit(square, target,
                        NewLocation(square, target.Id, (target.Shape + 1) % LocationShapeCount,
                            target.Orientation, p, x, y), wide, high);
                }

                case MapTool.DeleteTopLocation: {
                    Location? target = TopLocation(square, p, x, y);
                    if (target == null)
                        return null;

                    (int wide, int high) = Footprint(target.Id, target.Orientation);
                    return new RemoveLocationEdit(square, target, wide, high);
                }

                default:
                    return null;
            }
        }

        /// <summary>
        ///     An object's tile footprint after rotation, for the edit highlight.
        /// </summary>
        /// <remarks>
        ///     Rotation swaps the axes on an odd orientation, matching what the rasteriser already
        ///     does (<c>MapRasteriser</c> line 467). Without it a 3x1 object rotated onto its side
        ///     would be highlighted across the wrong three tiles, which is worse than not
        ///     highlighting it - a wrong footprint teaches a wrong mental model of what was placed.
        ///
        ///     Eight shipped loc files reference ids that index 16 does not carry, so a missing
        ///     definition is expected data rather than an error; one tile is the safe read.
        /// </remarks>
        /// <param name="objectId">The object definition id.</param>
        /// <param name="orientation">The rotation, 0..3.</param>
        /// <returns>Tiles east and north.</returns>
        private (int Wide, int High) Footprint(int objectId, int orientation) {
            int sizeX = 1, sizeY = 1;

            try {
                ObjectDefinition? definition = cache?.GetObjectDefinition(objectId >> 8, objectId & 0xFF);
                if (definition != null) {
                    //The size fields are bytes, so widen before Math.Max or the overload is ambiguous.
                    sizeX = Math.Max(1, (int) definition.sizeX);
                    sizeY = Math.Max(1, (int) definition.sizeY);
                }
            }
            catch (Exception) {
                //Left at 1x1.
            }

            return (orientation & 1) == 0 ? (sizeX, sizeY) : (sizeY, sizeX);
        }

        /// <summary>
        ///     Shape codes a location can take, 0..22 inclusive.
        /// </summary>
        /// <remarks>
        ///     The decoder rejects anything above 22 as a desynchronised stream
        ///     (<c>Region.LoadLocations</c>), so a cycle that ran past it would produce a square the
        ///     editor could write and then refuse to read back.
        /// </remarks>
        private const int LocationShapeCount = 23;

        /// <summary>
        ///     The last location decoded on a tile, which is the one drawn on top of the others.
        /// </summary>
        /// <param name="square">The square.</param>
        /// <param name="plane">The plane.</param>
        /// <param name="x">Tile X within the square.</param>
        /// <param name="y">Tile Y within the square.</param>
        /// <returns>The location, or <c>null</c> when the tile holds none.</returns>
        private static Location? TopLocation(MapRegion square, int plane, int x, int y) {
            Location? target = null;
            foreach (Location loc in square.GetLocations())
                if (loc.Plane == plane && loc.LocalX == x && loc.LocalY == y)
                    target = loc;
            return target;
        }

        /// <summary>
        ///     Builds a location whose absolute position agrees with its square-local one.
        /// </summary>
        /// <remarks>
        ///     Both are stored on a <see cref="Location"/> and only the local pair is encoded, so a
        ///     mismatch between them would never reach the file - it would show up as an object the
        ///     inspector puts in one place and the renderer in another.
        /// </remarks>
        /// <param name="square">The square the location belongs to.</param>
        /// <param name="id">The object definition id.</param>
        /// <param name="shape">The shape code.</param>
        /// <param name="orientation">The rotation, 0..3.</param>
        /// <param name="plane">The plane.</param>
        /// <param name="x">Tile X within the square.</param>
        /// <param name="y">Tile Y within the square.</param>
        /// <returns>The location.</returns>
        private static Location NewLocation(MapRegion square, int id, int shape, int orientation,
            int plane, int x, int y) {
            return new Location(id, shape, orientation, x, y, plane,
                new Position(square.GetBaseX() + x, square.GetBaseY() + y, plane));
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
        /// <param name="reversed">
        ///     <c>true</c> when the edit was undone rather than applied, so the feedback reports the
        ///     direction the terrain actually moved.
        /// </param>
        private void InvalidateFor(IMapEdit edit, bool reversed = false) {
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

            //Raised here rather than at the click, so that undo and redo flash exactly as an edit
            //does. An undo that silently reverts something is the same "did anything happen"
            //problem running backwards.
            FlashFor(edit, reversed);

            InvalidateInspectorScene();
            UpdateInspector(lastHit, loadMissing: true);
            UpdateStatus();
        }

        /// <summary>
        ///     Marks on the canvas what an edit changed.
        /// </summary>
        /// <remarks>
        ///     Purely an overlay - it does not touch the tile cache, which
        ///     <see cref="InvalidateFor"/> has already dealt with separately.
        ///
        ///     A height edit raises two marks. The amber one is the tile that was clicked; the cyan
        ///     one is the two-by-two block of tiles that share the vertex the click moved, which is
        ///     the part nobody can guess from the tool's name. Everything else is one mark over its
        ///     own footprint.
        /// </remarks>
        /// <param name="edit">The edit applied, undone or redone.</param>
        /// <param name="reversed"><c>true</c> when the edit was undone.</param>
        private void FlashFor(IMapEdit edit, bool reversed) {
            if (edit is not IMapEditArea area || edit.Target == null)
                return;

            int id = edit.Target.GetRegionID();
            int worldX = MapSquareNames.RegionX(id) * MapRegion.WIDTH + area.LocalX;
            int worldY = MapSquareNames.RegionY(id) * MapRegion.HEIGHT + area.LocalY;

            if (edit is SetHeightEdit height) {
                int steps = reversed ? -height.StepDelta : height.StepDelta;
                int landed = reversed ? height.OldHeight : height.NewHeight;

                //The block whose surface bends: the clicked tile is its north-east quarter.
                view.Flash(worldX - 1, worldY - 1, 2, 2, area.Plane, MapFlashKind.Vertex);
                view.Flash(worldX, worldY, 1, 1, area.Plane, MapFlashKind.Edit,
                    $"{steps:+0;-0;0} step   h {landed}");
                return;
            }

            view.Flash(worldX, worldY, area.TilesWide, area.TilesHigh, area.Plane,
                FlashKindOf(edit, reversed), FlashLabel(edit, reversed));
        }

        /// <summary>
        ///     Whether a flash should read as a write or as a deletion.
        /// </summary>
        /// <remarks>
        ///     Undo swaps the two for the location tools: undoing a placement removes an object, and
        ///     undoing a deletion puts one back. Colouring by the edit's own type would show a red
        ///     "removed" mark at the exact moment an object reappeared.
        /// </remarks>
        private static MapFlashKind FlashKindOf(IMapEdit edit, bool reversed) {
            switch (edit) {
                case RemoveLocationEdit:
                    return reversed ? MapFlashKind.Edit : MapFlashKind.Removal;
                case AddLocationEdit:
                    return reversed ? MapFlashKind.Removal : MapFlashKind.Edit;
                default:
                    return MapFlashKind.Edit;
            }
        }

        /// <summary>
        ///     The caption drawn over a flash.
        /// </summary>
        /// <remarks>
        ///     Deliberately not <c>edit.Description</c>, which is written for an undo menu and reads
        ///     as "Underlay 40 to 12 at 33,17" - the coordinates are the one thing the mark on the
        ///     map has already said, and the length pushes the plate wider than the tile it points
        ///     at.
        ///
        ///     Every case reports the value that is now on the tile, which on an undo is the edit's
        ///     <em>old</em> one. A label naming the value that has just been thrown away would be
        ///     worse than no label.
        /// </remarks>
        /// <param name="edit">The edit.</param>
        /// <param name="reversed"><c>true</c> when the edit was undone.</param>
        /// <returns>The caption, or <c>null</c> for none.</returns>
        private static string? FlashLabel(IMapEdit edit, bool reversed) {
            switch (edit) {
                case SetUnderlayEdit underlay:
                    return $"underlay {(reversed ? underlay.OldId : underlay.NewId)}";
                case SetOverlayEdit overlay:
                    return $"overlay {(reversed ? overlay.OldId : overlay.NewId)}";
                case SetTileFlagsEdit flags:
                    return $"flags 0x{(reversed ? flags.OldFlags : flags.NewFlags):X2}";
                case AddLocationEdit add:
                    return reversed ? $"removed {add.Location.Id}" : $"object {add.Location.Id}";
                case RemoveLocationEdit remove:
                    return reversed ? $"object {remove.Location.Id}" : $"deleted {remove.Location.Id}";
                case ReplaceLocationEdit replace: {
                    Location shown = reversed ? replace.Original : replace.Replacement;
                    return $"object {shown.Id}  shape {shown.Shape}  rot {shown.Orientation}";
                }
                default:
                    return null;
            }
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
                ShowMessage("Nothing to save");
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
                //Staging and writing both inside the gate, and the handshake's wait deliberately
                //outside it. The gate is taken on the UI thread by the tile inspector and by the
                //render thread, so holding it across a wait that can last the whole timeout would
                //freeze the window from behind the progress dialog. With the handshake off this is
                //the same single exclusive block it always was, run on the calling thread.
                JS5ReloadProgressDialog.Save(FindForm(), cacheDirectory, () => store.RunExclusive(() => {
                    foreach ((MapRegion Square, int RegionX, int RegionY) entry in dirty)
                        loader.Save(entry.Square, entry.RegionX, entry.RegionY);

                    cache.WriteCache(cacheDirectory);
                }));

                history.Clear();
                ShowMessage($"Saved {dirty.Count} square(s) to {cacheDirectory}");
            }
            catch (OperationCanceledException) {
                //The user's own choice, and recoverable: the request has been withdrawn, the
                //squares are still dirty and still in memory, and saving again retries.
                ShowMessage("Save cancelled while waiting for the JS5 update server");
            }
            catch (Exception ex) {
                //A failed save leaves the staged edits in memory, so the user can retry.
                ShowMessage("Save failed: " + ex.Message);
                MessageBox.Show(ex.Message, "Save failed", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        /// <summary>
        ///     Builds the two-pane layout.
        /// </summary>
        /// <remarks>
        ///     <b>No control in the left column carries a hardcoded pixel size any more.</b> The form
        ///     declares <c>AutoScaleMode.Font</c> against design metrics of 9 by 20
        ///     (<c>Editor.Designer.cs</c>), so on a machine whose font measures smaller than that,
        ///     every literal width, height and <c>SizeType.Absolute</c> row is multiplied by the
        ///     ratio. Measured off a screenshot of this panel it was around two thirds: the 210-pixel
        ///     World row drew 145 tall and the 150-pixel tool combo drew 101 wide.
        ///
        ///     Widths shrink with it and mostly survive, but a ComboBox, a NumericUpDown and a Label
        ///     keep whatever height their font needs, so the rows shrank out from under their
        ///     contents. The Tool group's button row was sliced in half by the window edge, "Fit
        ///     world" was a sliver with only the tops of its glyphs showing, and a 60-pixel Plane
        ///     combo rendered "Pl".
        ///
        ///     Every row therefore measures its own content (<c>SizeType.AutoSize</c>) or is computed
        ///     from live font metrics in <see cref="ApplyMeasuredSizes"/>, and the stretchy row goes to
        ///     the world navigator rather than to the layer list, which needs exactly nine rows and
        ///     was holding an empty half-column of slack.
        /// </remarks>
        private void BuildLayout() {
            var split = new SplitContainer {
                Dock = DockStyle.Fill,
                Orientation = Orientation.Vertical,
                SplitterDistance = LeftColumnWidth,
                FixedPanel = FixedPanel.Panel1
            };

            //Right: canvas above, inspector below.
            var right = new SplitContainer {
                Dock = DockStyle.Fill,
                Orientation = Orientation.Horizontal,
                FixedPanel = FixedPanel.Panel2
            };
            right.Panel1.Controls.Add(view);

            /* THE PALETTE LIVES HERE, NOT IN THE LEFT COLUMN, AND THAT WAS THE SECOND ATTEMPT.
               394 swatches need WIDTH: the left column is 250 pixels of a window already holding
               five stacked groups, and putting the palette there either collapsed it to nothing or
               clipped the layer list. Along the bottom it gets the whole window's width, which is
               where a row of swatches wants to run anyway. */
            var bottom = new SplitContainer {
                Dock = DockStyle.Fill,
                Orientation = Orientation.Vertical
            };

            var materialsGroup = new GroupBox { Text = "Floor materials", Dock = DockStyle.Fill };
            materialsGroup.Controls.Add(materials);

            bottom.Panel1.Controls.Add(inspector);
            bottom.Panel2.Controls.Add(materialsGroup);

            //Placed once the container has a real size, for the reason every other splitter here is:
            //a distance assigned to a 150-pixel-wide default is silently clamped.
            bottom.HandleCreated += (_, _) => bottom.SplitterDistance = Math.Max(120, bottom.Width / 3);

            right.Panel2.Controls.Add(bottom);

            split.Panel1.Controls.Add(BuildLeftColumn());
            split.Panel2.Controls.Add(right);

            Controls.Add(split);
            Controls.Add(status);

            for (int p = 0; p < 4; p++)
                planeBox.Items.Add("Plane " + p);
            planeBox.SelectedIndex = 0;

            //Seeded from MapLayers.Default, which is also what WorldMapViewControl.layers starts at,
            //so the tick boxes and the picture cannot disagree about what is on. There are only those
            //two statements of the default, and neither may be spelled out again here: a third copy
            //is how they would drift. On: underlay, overlay, walls, map scene icons, relief shading
            //and grid. Off: ground decoration, game objects and tile flags.
            foreach ((string name, MapLayers layer) in LayerRows)
                layerList.Items.Add(name, (MapLayers.Default & layer) != 0);

            foreach ((string label, MapTool _) in ToolRows)
                toolBox.Items.Add(label);
            toolBox.SelectedIndex = 0;

            //SplitterDistance has to be set after the control has a size, or it is silently clamped.
            split.HandleCreated += (_, _) => split.SplitterDistance = LeftColumnWidth;
            right.HandleCreated += (_, _) => right.SplitterDistance = Math.Max(100, right.Height - 150);

            status.Text = "No cache loaded";
        }

        /// <summary>
        ///     Builds the left control column.
        /// </summary>
        /// <remarks>
        ///     One Percent row and three AutoSize rows. The Percent row is the world navigator, which
        ///     is now the primary way to move around the map and so should be the biggest thing here;
        ///     the three below it take exactly the height their contents measure, which is what stops
        ///     the Tool group being pushed off the bottom of the window. That is the guarantee - the
        ///     rows can no longer be wrong about how tall their contents are, because they no longer
        ///     hold an opinion about it.
        ///
        ///     <c>AutoScroll</c> is a backstop for a window so short that even the three measured rows
        ///     do not fit, at which point <see cref="TableLayoutPanel"/> starts shrinking them again.
        ///     It costs nothing when it does not engage. It is only safe to ask for because nothing in
        ///     the column derives its height from its width any more - there is not a wrapping
        ///     <see cref="FlowLayoutPanel"/> left below this point - so a scrollbar appearing cannot
        ///     change the height that decided whether to show it.
        /// </remarks>
        /// <returns>The column.</returns>
        private TableLayoutPanel BuildLeftColumn() {
            var column = new TableLayoutPanel {
                Dock = DockStyle.Fill,
                ColumnCount = 1,
                RowCount = 4,
                AutoScroll = true
            };

            column.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));

            column.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            column.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            column.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            column.RowStyles.Add(new RowStyle(SizeType.AutoSize));

            worldGroup = new GroupBox { Text = "World", Dock = DockStyle.Fill };
            worldGroup.Controls.Add(navigator);

            column.Controls.Add(worldGroup, 0, 0);
            column.Controls.Add(BuildViewRow(), 0, 1);
            column.Controls.Add(BuildLayersGroup(), 0, 2);
            column.Controls.Add(BuildToolGroup(), 0, 3);

            return column;
        }

        /// <summary>
        ///     The plane selector, "Fit world" and the zoom readout.
        /// </summary>
        /// <remarks>
        ///     The combo sits in the one Percent column and is anchored to both its edges, so it is
        ///     whatever is left after the label and the button rather than a fixed 60 pixels that font
        ///     scaling cut to 40 and rendered as "Pl". The button measures its own text for the same
        ///     reason. The zoom readout is on its own row because those three already fill the width.
        /// </remarks>
        /// <returns>The row.</returns>
        private TableLayoutPanel BuildViewRow() {
            var row = new TableLayoutPanel {
                Dock = DockStyle.Fill,
                AutoSize = true,
                AutoSizeMode = AutoSizeMode.GrowAndShrink,
                ColumnCount = 3,
                RowCount = 2
            };

            row.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            row.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            row.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            row.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            row.RowStyles.Add(new RowStyle(SizeType.AutoSize));

            //An anchor with no Top and no Bottom is what centres a control vertically in its cell,
            //which is why none of the labels here needs the six-pixel top padding the old flow layout
            //used to line them up against the combo by hand.
            planeBox.Anchor = AnchorStyles.Left | AnchorStyles.Right;
            fitButton.Anchor = AnchorStyles.Left;
            zoomLabel.Anchor = AnchorStyles.Left;

            row.Controls.Add(new Label { Text = "Plane", AutoSize = true, Anchor = AnchorStyles.Left }, 0, 0);
            row.Controls.Add(planeBox, 1, 0);
            row.Controls.Add(fitButton, 2, 0);
            row.Controls.Add(zoomLabel, 0, 1);
            row.SetColumnSpan(zoomLabel, 3);

            return row;
        }

        /// <summary>
        ///     The layer tick boxes and the relief slider.
        /// </summary>
        /// <remarks>
        ///     The list's row is the one <c>Absolute</c> height left in the panel, and
        ///     <see cref="ApplyMeasuredSizes"/> computes it from the list's own item height rather than
        ///     a literal. Nine layers never becomes ten by accident, and sizing it to its content is
        ///     what frees the gap that used to sit between "Grid" and the relief slider.
        /// </remarks>
        /// <returns>The group box.</returns>
        private GroupBox BuildLayersGroup() {
            var group = new GroupBox {
                Text = "Layers",
                Dock = DockStyle.Fill,
                AutoSize = true,
                AutoSizeMode = AutoSizeMode.GrowAndShrink
            };

            layersBody = new TableLayoutPanel {
                Dock = DockStyle.Fill,
                AutoSize = true,
                AutoSizeMode = AutoSizeMode.GrowAndShrink,
                ColumnCount = 1,
                RowCount = 2
            };

            layersBody.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));

            //Filled in by ApplyMeasuredSizes; zero here so a missed call shows as a collapsed list
            //rather than as a plausible-looking wrong height.
            layersBody.RowStyles.Add(new RowStyle(SizeType.Absolute, 0));
            layersBody.RowStyles.Add(new RowStyle(SizeType.AutoSize));

            var reliefRow = new TableLayoutPanel {
                Dock = DockStyle.Fill,
                AutoSize = true,
                AutoSizeMode = AutoSizeMode.GrowAndShrink,
                ColumnCount = 2,
                RowCount = 1,
                Margin = Padding.Empty
            };

            reliefRow.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            reliefRow.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            reliefRow.RowStyles.Add(new RowStyle(SizeType.AutoSize));

            //A horizontal TrackBar keeps its own height whatever is asked of it, so only the width is
            //stretched here and the row measures the height the control insists on.
            reliefBar.Anchor = AnchorStyles.Left | AnchorStyles.Right;

            reliefRow.Controls.Add(new Label { Text = "Relief", AutoSize = true, Anchor = AnchorStyles.Left }, 0, 0);
            reliefRow.Controls.Add(reliefBar, 1, 0);

            layersBody.Controls.Add(layerList, 0, 0);
            layersBody.Controls.Add(reliefRow, 0, 1);
            group.Controls.Add(layersBody);

            return group;
        }

        /// <summary>
        ///     The tool selector, its value, and undo, redo and save.
        /// </summary>
        /// <remarks>
        ///     A grid rather than a wrapping flow. The flow put all six controls on one line and
        ///     relied on the group being 110 pixels tall for the wrap to fit, which font scaling cut
        ///     to about 76 and the window edge then sliced the button row in half. Two half-width
        ///     columns give undo and redo a row of their own with no wrapping to get wrong, and the
        ///     row heights come from the controls.
        /// </remarks>
        /// <returns>The group box.</returns>
        private GroupBox BuildToolGroup() {
            var group = new GroupBox {
                Text = "Tool",
                Dock = DockStyle.Fill,
                AutoSize = true,
                AutoSizeMode = AutoSizeMode.GrowAndShrink
            };

            var body = new TableLayoutPanel {
                Dock = DockStyle.Fill,
                AutoSize = true,
                AutoSizeMode = AutoSizeMode.GrowAndShrink,
                ColumnCount = 2,
                RowCount = 4
            };

            body.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
            body.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
            for (int row = 0; row < body.RowCount; row++)
                body.RowStyles.Add(new RowStyle(SizeType.AutoSize));

            toolBox.Anchor = AnchorStyles.Left | AnchorStyles.Right;
            toolValue.Anchor = AnchorStyles.Left | AnchorStyles.Right;

            //Left-anchored rather than stretched: they measure their own text, and stretching an
            //AutoSize control across a cell is the one combination TableLayoutPanel resolves by
            //guessing.
            undoButton.Anchor = AnchorStyles.Left;
            redoButton.Anchor = AnchorStyles.Left;
            saveButton.Anchor = AnchorStyles.Left;

            body.Controls.Add(toolBox, 0, 0);
            body.SetColumnSpan(toolBox, 2);

            body.Controls.Add(new Label { Text = "Value", AutoSize = true, Anchor = AnchorStyles.Left }, 0, 1);
            body.Controls.Add(toolValue, 1, 1);

            body.Controls.Add(undoButton, 0, 2);
            body.Controls.Add(redoButton, 1, 2);

            body.Controls.Add(saveButton, 0, 3);
            body.SetColumnSpan(saveButton, 2);

            group.Controls.Add(body);

            return group;
        }

        /// <summary>
        ///     Sets the sizes that only the running machine's font can decide.
        /// </summary>
        /// <remarks>
        ///     Called on handle creation and on any later font change. Handle creation is the first
        ///     point at which the form's font auto-scaling has certainly run and
        ///     <see cref="Control.Font"/> is final; measuring earlier - in the constructor, where the
        ///     old literals were - gives design-time numbers that are then scaled again and end up
        ///     about a third too small.
        /// </remarks>
        private void ApplyMeasuredSizes() {
            //Off, so the list holds the exact height set rather than snapping to a whole item count
            //and leaving the arithmetic below unable to predict what it will do.
            layerList.IntegralHeight = false;

            //Four terms, and dropping any one of them puts a scrollbar over nine visible items: the
            //rows at the list's own item height, the 3D border on the top and bottom edges, the
            //margin the cell takes off a docked child before it gets any of the row at all, and two
            //pixels of slack. The margin and the border are read live rather than assumed, because
            //font scaling moves both.
            layersBody.RowStyles[0].Height =
                layerList.ItemHeight * LayerRows.Length
                + 2 * SystemInformation.Border3DSize.Height
                + layerList.Margin.Vertical
                + 2;

            //Docked to the bottom with a literal height, which scaling cut below the text it carries -
            //the descenders on "plane" and "px/tile" were being clipped by the window edge.
            status.Height = Font.Height + 8;

            //A floor for the navigator, which otherwise has a Percent row and nothing to stop it
            //collapsing on a short window. Nine text lines is roughly the thumbnail's old size, so
            //this is the point below which the column starts scrolling instead of shrinking it.
            worldGroup.MinimumSize = new Size(0, Font.Height * 9);
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

            //Read straight from the cache rather than from the map store, because the floor tables
            //are config records and have nothing to do with whether a map square has loaded.
            materials.Bind(newCache);

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

            //Names a tile in a cache that is no longer the one on screen.
            lastEditNote = null;

            if (cache == null) {
                loader = null;
                navigator.Build(null);
                ShowMessage("No cache loaded");
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

            ShowMessage(store.Exists(rx, ry)
                ? $"m{rx}_{ry}"
                : $"m{rx}_{ry} does not exist in this cache");
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

            var sb = new StringBuilder();
            sb.AppendLine($"world {hit.WorldX}, {hit.WorldY}   plane {hit.Plane}");
            sb.AppendLine($"square m{hit.RegionX}_{hit.RegionY}");
            sb.AppendLine($"zoom to {WorldMapViewControl.MinimumEditingPixelsPerTile:0} px/tile"
                          + " or closer for tile detail");

            //Carried at this zoom too: zooming out is the obvious thing to do after an edit to see
            //what moved, and dropping the record exactly then would be perverse.
            AppendLastEdit(sb);

            inspector.Text = sb.ToString();
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
                AppendLastEdit(sb);
                inspector.Text = sb.ToString();
                return;
            }

            AppendHeight(sb, scene, square, hit, sceneX, sceneY);

            sb.AppendLine($"underlay  {scene.UnderlayId(hit.Plane, sceneX, sceneY)}");
            sb.AppendLine($"overlay   {scene.OverlayId(hit.Plane, sceneX, sceneY)}" +
                          $"  shape {scene.OverlayShape(hit.Plane, sceneX, sceneY)}" +
                          $"  rot {scene.OverlayRotation(hit.Plane, sceneX, sceneY)}");
            sb.AppendLine($"flags     0x{scene.TileFlags(hit.Plane, sceneX, sceneY):X2}");

            int shown = 0;
            foreach (Location loc in square.GetLocations()) {
                if (loc.Plane != hit.Plane || loc.LocalX != hit.LocalX || loc.LocalY != hit.LocalY)
                    continue;
                if (shown++ == 0)
                    sb.AppendLine("locs:");
                sb.AppendLine($"  id {loc.Id}  shape {loc.Shape} ({LocGroups.Of(loc.Shape)})  rot {loc.Orientation}");
            }

            AppendLastEdit(sb);
            inspector.Text = sb.ToString();
        }

        /// <summary>
        ///     Writes the height block: the stored value, what it means, and the four corners.
        /// </summary>
        /// <remarks>
        ///     The old block was two bare numbers - <c>height -960</c> and a row of corner values -
        ///     and neither said the two things a reader has to know to interpret them. Heights are
        ///     stored <b>negative-up</b> in steps of <c>Region.HEIGHT_UNITS_PER_STEP</c>, so -960
        ///     is thirty steps of ground <em>above</em> the plane's base and not a depth. And the
        ///     stored value belongs to the tile's <b>south-west corner vertex</b>, not to the tile:
        ///     that vertex is the sw entry of the corner row, and the other three belong to
        ///     neighbouring tiles. That is why a raise appears to move the neighbours too, and it
        ///     is the confusion the whole feature exists to clear up, so it is stated here in
        ///     writing as well as drawn on the canvas.
        ///
        ///     On a square boundary the corners come from the neighbouring square, so this doubles
        ///     as the quickest way to see the shared-vertex resolution working.
        /// </remarks>
        private static void AppendHeight(StringBuilder sb, MapScene scene, MapRegion square, TileHit hit,
            int sceneX, int sceneY) {
            //The 900 shipped underwater squares decode a single plane, and Region.GetTileHeight
            //indexes its array unguarded - so hovering plane 1 over one of them threw out of a
            //mouse handler, which takes the form down rather than showing a blank line.
            if (hit.Plane < 0 || hit.Plane >= square.PlaneCount) {
                sb.AppendLine($"height    (this square carries {square.PlaneCount} plane(s), not plane {hit.Plane})");
                return;
            }

            int height = square.GetTileHeight(hit.Plane, hit.LocalX, hit.LocalY);

            //Plane 0 measures from sea level; every plane above measures from the one below it,
            //which is what the encoder writes and therefore what a step count has to agree with.
            int reference = hit.Plane == 0 ? 0 : square.GetTileHeight(hit.Plane - 1, hit.LocalX, hit.LocalY);
            int steps = (reference - height) / MapRegion.HEIGHT_UNITS_PER_STEP;

            sb.AppendLine($"height    {height} units = {steps} step(s) up"
                          + $"  ({MapRegion.HEIGHT_UNITS_PER_STEP} units a step, negative is up)");
            sb.AppendLine("          this is the SW corner VERTEX of the tile, shared by 4 tiles");

            sb.AppendLine($"vertices  sw {scene.VertexHeight(hit.Plane, sceneX, sceneY)}" +
                          $"  se {scene.VertexHeight(hit.Plane, sceneX + 1, sceneY)}" +
                          $"  nw {scene.VertexHeight(hit.Plane, sceneX, sceneY + 1)}" +
                          $"  ne {scene.VertexHeight(hit.Plane, sceneX + 1, sceneY + 1)}");
            sb.AppendLine("          a height edit here moves sw only; se, nw and ne belong to"
                          + " the neighbouring tiles");
        }

        /// <summary>Appends the standing record of the most recent edit, if there is one.</summary>
        private void AppendLastEdit(StringBuilder sb) {
            if (lastEditNote == null)
                return;

            sb.AppendLine();
            sb.AppendLine(lastEditNote);
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
        ///     sixty times a second while it does. The two render counters take the service's own
        ///     gate and the tile cache's, which are short and touch no I/O; that is what makes them
        ///     safe for <see cref="statusTimer"/> to poll as well.
        /// </remarks>
        private void UpdateStatus() {
            zoomLabel.Text = $"{view.Camera.PixelsPerTile:0.###} px/tile";

            if (store == null || service == null) {
                status.Text = cache == null ? "No cache loaded" : "No map index";
                statusShowsProgress = true;
                lastRenderedSquares = -1;
                lastQueuedTiles = -1;
                return;
            }

            string where = lastHit == null ? "-" : $"m{lastHit.RegionX}_{lastHit.RegionY}";
            int missing = store.MissingKeyCount;

            string keys = missing == 0
                ? "keys ok"
                : $"{missing} square(s) missing XTEA keys - objects hidden";

            //Read once and remembered, so the poll can compare against exactly what is on screen.
            lastRenderedSquares = service.RenderedSquareCount;
            lastQueuedTiles = service.PendingCount;

            status.Text = $"plane {view.Plane}   {view.Camera.PixelsPerTile:0.###} px/tile   {where}   " +
                          SweepProgress() + "   " +
                          (view.EditingEnabled ? "" : "zoom in to edit   ") + keys;

            statusShowsProgress = true;
        }

        /// <summary>
        ///     What the render sweep has finished, phrased so it cannot contradict the picture.
        /// </summary>
        /// <remarks>
        ///     The old wording was "N of 1684 rendered", which reads as "how much of the map is drawn"
        ///     and was seen saying 0 over a fully drawn world. Two separate things were wrong with it.
        ///
        ///     It was stale - nothing refreshed the line between mouse events, which
        ///     <see cref="statusTimer"/> now fixes. And it never counted what it appeared to:
        ///     <c>MapTileRenderService.RenderedSquareCount</c> is how many squares hold an
        ///     <em>overview</em> tile, the permanent level-0-and-below band, while everything drawn
        ///     from two pixels per tile upward comes from detail tiles that are rendered on demand and
        ///     counted nowhere. So the noun is now "overview", the queue is counted in tiles rather
        ///     than squares because at a detail zoom that is what is in it, and once the sweep is done
        ///     the line says where the rest of the picture comes from instead of repeating a total
        ///     that will never move again.
        /// </remarks>
        /// <returns>The progress clause.</returns>
        private string SweepProgress() {
            int total = store.SquareCount;

            if (lastRenderedSquares >= total && lastQueuedTiles == 0)
                return view.Camera.Level >= MapTileCache.FirstDetailLevel
                    ? $"overview complete, all {total} squares - tile detail is drawn as you pan"
                    : $"overview complete, all {total} squares";

            return lastQueuedTiles > 0
                ? $"overview {lastRenderedSquares} of {total} squares, {lastQueuedTiles} tile(s) queued"
                : $"overview {lastRenderedSquares} of {total} squares";
        }

        /// <summary>
        ///     Puts a one-off message on the status line, and stops the poll overwriting it.
        /// </summary>
        /// <remarks>
        ///     The poll refreshes the counters several times a second while the sweep runs, which
        ///     would wipe "Saved 3 square(s)" before anyone could read it. The next hover, pan, zoom
        ///     or edit restores the live line.
        /// </remarks>
        /// <param name="text">What to say.</param>
        private void ShowMessage(string text) {
            status.Text = text;
            statusShowsProgress = false;
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

                statusTimer.Stop();
                statusTimer.Dispose();

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
