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

        /// <summary>
        ///     The tool palette, which replaced a drop-down list of thirteen tools.
        /// </summary>
        /// <remarks>
        ///     A combo is the wrong control for a paint program's tool set for two reasons that
        ///     compound: only the armed tool is visible, so the set is not discoverable at all
        ///     without opening it, and switching tools is a click, a scan and a second click where a
        ///     palette is one click or one key. The tools are grouped by what they operate on -
        ///     inspect, selection, floors, terrain, flags, objects - with a separator between each
        ///     group, because "which of these thirteen changes an object" was previously answerable
        ///     only by reading all thirteen labels.
        /// </remarks>
        private readonly EditorToolStrip toolStrip = new EditorToolStrip { Dock = DockStyle.Fill };

        /// <summary>What each tool's option bar shows, and the note it carries.</summary>
        private readonly MapToolOptionsBar options = new MapToolOptionsBar();

        /// <summary>The tiles an area operation acts on, empty until a selection tool is used.</summary>
        private readonly MapSelection selection = new MapSelection();

        private readonly Dictionary<MapTool, EditorToolButton> toolButtons = new Dictionary<MapTool, EditorToolButton>();

        private MapTool currentTool = MapTool.Inspect;

        //Held so the fill action can be greyed out when there is nothing to fill. Nullable because
        //the selection's Changed handler is wired before the palette is built.
        private EditorToolButton? fillButton;

        //Where the current selection drag started, and how it combines with what was selected.
        private TileHit? dragAnchor;
        private MapSelectionMode dragMode = MapSelectionMode.Replace;

        /* Every floor material in the cache, as swatches. */
        private readonly FloorMaterialPalette materials = new FloorMaterialPalette();
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

        /// <summary>What a click or a drag on the canvas does.</summary>
        private enum MapTool {
            Inspect,
            Eyedropper,
            SelectRectangle,
            SelectFreehand,
            SelectSimilar,
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

        /// <summary>
        ///     Everything a tool states about itself: its icon, its key, its options and its note.
        /// </summary>
        /// <remarks>
        ///     One table rather than a switch per property. The old arrangement had the tool list in
        ///     one array and its value bound in a separate <c>switch</c>, which is how a tool ends up
        ///     with the wrong cap: nothing in the language connects the two, and adding a row to the
        ///     array silently gives the new tool whatever the <c>default</c> arm returns.
        /// </remarks>
        /// <param name="Tool">Which tool.</param>
        /// <param name="Icon">Its glyph.</param>
        /// <param name="Tooltip">What it does, in a few words.</param>
        /// <param name="Shortcut">The key that arms it.</param>
        /// <param name="Options">Which option groups it reads.</param>
        /// <param name="NoteKind">Which obligation its note discharges.</param>
        /// <param name="NoteCaption">The heading over its note.</param>
        /// <param name="NoteBody">The note, or empty for none.</param>
        private readonly record struct ToolSpec(MapTool Tool, EditorIcon Icon, string Tooltip,
            Keys Shortcut, MapToolOptions Options, InfoKind NoteKind, string NoteCaption, string NoteBody);

        /// <summary>Where a separator falls in <see cref="ToolSpecs"/>, by the tool that follows it.</summary>
        private static readonly MapTool[] GroupStarts = {
            MapTool.SelectRectangle, MapTool.PaintUnderlay, MapTool.RaiseHeight,
            MapTool.ToggleBlockedFlag, MapTool.PlaceLocation
        };

        private static readonly ToolSpec[] ToolSpecs = {
            new ToolSpec(MapTool.Inspect, EditorIcon.Pointer, "Inspect a tile", Keys.I,
                MapToolOptions.None, InfoKind.Help, "Inspect",
                "Reads the tile under the pointer into the inspector below the map and changes " +
                "nothing. This is the only tool that is safe to click with anywhere."),

            new ToolSpec(MapTool.Eyedropper, EditorIcon.Eyedropper,
                "Pick up a tile's floor into the brush", Keys.E,
                MapToolOptions.None, InfoKind.Help, "Eyedropper",
                "Takes the clicked tile's overlay, or its underlay where it has no overlay, into " +
                "the brush - together with the overlay's shape and rotation.\n\n" +
                "It reads and never writes, so it flashes GREEN rather than the amber that means " +
                "a square was written to, and it leaves nothing on the undo stack."),

            new ToolSpec(MapTool.SelectRectangle, EditorIcon.SelectRectangle,
                "Select a rectangle of tiles - drag", Keys.M,
                MapToolOptions.None, InfoKind.Limitation, "What a selection can cover",
                SelectionNote),

            new ToolSpec(MapTool.SelectFreehand, EditorIcon.SelectFreehand,
                "Select tiles freehand - drag", Keys.L,
                MapToolOptions.None, InfoKind.Limitation, "What a selection can cover",
                SelectionNote),

            new ToolSpec(MapTool.SelectSimilar, EditorIcon.SelectSimilar,
                "Select everything adjacent that matches", Keys.W,
                MapToolOptions.Wand, InfoKind.Limitation, "How far the wand reaches",
                "The flood stops at the edge of the nine squares loaded around the click, and " +
                "says so when it does. Letting it walk further would mean decoding squares from " +
                "inside a mouse handler with no upper bound, which over open grass is a " +
                "continent.\n\n" +
                "Tolerance is a distance in ids, not in colour. Neighbouring floor ids are not " +
                "neighbouring colours - the tables are in no visual order - so anything above zero " +
                "only helps for the few families that were authored as a run."),

            new ToolSpec(MapTool.PaintUnderlay, EditorIcon.Underlay, "Paint underlay", Keys.U,
                MapToolOptions.UnderlayId | MapToolOptions.Brush, InfoKind.Cost,
                "What painting costs", PaintCostNote),

            new ToolSpec(MapTool.PaintOverlay, EditorIcon.Overlay, "Paint overlay", Keys.O,
                MapToolOptions.OverlayId | MapToolOptions.Brush | MapToolOptions.OverlayForm,
                InfoKind.Cost, "What painting costs", PaintCostNote),

            new ToolSpec(MapTool.CycleOverlayShape, EditorIcon.Shape,
                "Step the overlay already on this tile to its next shape", Keys.S,
                MapToolOptions.None, InfoKind.Help, "Cycle overlay shape",
                "Adjusts what is already on the tile. To lay a shaped overlay in the first place, " +
                "set Shape on the overlay brush instead - that is what it is there for."),

            new ToolSpec(MapTool.CycleOverlayRotation, EditorIcon.Rotate,
                "Turn the overlay already on this tile a quarter turn", Keys.T,
                MapToolOptions.None, InfoKind.Help, "Cycle overlay rotation",
                "Adjusts what is already on the tile. To lay a rotated overlay in the first place, " +
                "set Rotation on the overlay brush."),

            new ToolSpec(MapTool.RaiseHeight, EditorIcon.Raise, "Raise terrain", Keys.R,
                MapToolOptions.Brush, InfoKind.Cost, "What a height change costs", HeightCostNote),

            new ToolSpec(MapTool.LowerHeight, EditorIcon.Lower, "Lower terrain", Keys.F,
                MapToolOptions.Brush, InfoKind.Cost, "What a height change costs", HeightCostNote),

            new ToolSpec(MapTool.ToggleBlockedFlag, EditorIcon.Block,
                "Toggle the blocked flag", Keys.B,
                MapToolOptions.Brush, InfoKind.Help, "The blocked flag",
                "Bit 0 of the tile's flag byte, which is what stops anything walking onto it. The " +
                "other seven bits are left exactly as they were.\n\n" +
                "Over a selection this SETS the bit rather than toggling it, because a toggle " +
                "across ten thousand tiles produces a checkerboard of whatever was there before " +
                "and is not an operation anybody means to ask for. Clear it again with the same " +
                "tool held with Ctrl."),

            new ToolSpec(MapTool.PlaceLocation, EditorIcon.Add, "Place an object", Keys.P,
                MapToolOptions.ObjectId, InfoKind.Help, "Placing an object",
                "The object is placed as shape 10, the ordinary standing game object, which is the " +
                "one shape that renders anywhere. Walls and decorations need a wall or a floor to " +
                "make sense of, so they are reached by cycling the shape afterwards.\n\n" +
                "Browse lists every object definition in the cache. They cannot be drawn here - the " +
                "only route to model pixels in this editor is OpenGL - so each shows as a " +
                "placeholder carrying its id."),

            new ToolSpec(MapTool.RotateTopLocation, EditorIcon.Rotate,
                "Turn the topmost object a quarter turn", Keys.K,
                MapToolOptions.None, InfoKind.Help, "Which object",
                "The LAST object decoded on the tile, which is the one drawn on top of the others. " +
                "A tile can carry several and the inspector lists all of them."),

            new ToolSpec(MapTool.CycleTopLocationShape, EditorIcon.Shape,
                "Step the topmost object to its next shape", Keys.J,
                MapToolOptions.None, InfoKind.Limitation, "Why the shape stops at 22",
                "The decoder rejects a shape above 22 as a desynchronised stream, so a cycle that " +
                "ran past it would produce a square this editor could write and then refuse to " +
                "read back."),

            new ToolSpec(MapTool.DeleteTopLocation, EditorIcon.Remove,
                "Delete the topmost object", Keys.X,
                MapToolOptions.None, InfoKind.Help, "Which object",
                "The LAST object decoded on the tile, which is the one drawn on top of the others.")
        };

        /// <summary>
        ///     What every selection tool has to say, which is the same thing.
        /// </summary>
        /// <remarks>
        ///     Shared rather than written three times, so the zoom answer cannot drift between the
        ///     three tools that give it.
        /// </remarks>
        private const string SelectionNote =
            "SELECTING IS ZOOM-GATED, exactly as editing is, and for a reason rather than for " +
            "consistency: below two pixels per tile a tile is sub-pixel, so you cannot see which " +
            "tiles you are taking, and every square a selection touches has to be decoded and " +
            "pinned for as long as the edit is undoable. At the fully zoomed-out view that would " +
            "be all 1684 of them.\n\n" +
            "Shift adds to the selection, Ctrl takes away, and a plain drag replaces it.\n\n" +
            "Middle-drag or hold space and drag to pan while a selection tool is armed - a plain " +
            "left drag now draws.";

        /// <summary>What every paint tool has to say about the price of a fill.</summary>
        private const string PaintCostNote =
            "A paint changes pixels, not the shape of the world, so it is a repaint rather than a " +
            "rebuild and costs nothing on screen.\n\n" +
            "WHAT IT COSTS IS THE SAVE. Every map square a stroke or a fill touches is re-encoded " +
            "and written back, which changes that archive's CRC and drags in the reference-table " +
            "entry of every archive packed alongside it. A brush laid across a square corner " +
            "dirties four archives; a selection spanning a 3x3 block of squares dirties nine. The " +
            "status line reports the square count for exactly this reason.";

        /// <summary>What the height tools have to say, which is the longest note on the bar.</summary>
        private const string HeightCostNote =
            "A HEIGHT CHANGE REBUILDS THE RELIEF SHADING for every tile of the square it touches " +
            "and of the eight squares around it, because the shading of a tile is computed from " +
            "its neighbours' heights and the blend reaches across a square boundary. That is a " +
            "rebuild rather than a repaint, and over a large selection it is the slowest thing " +
            "this tab does.\n\n" +
            "IT ALSO WRITES A VERTEX, NOT A TILE. The value stored against a tile is the elevation " +
            "of its SOUTH-WEST CORNER, which four tiles share, so raising one tile bends a " +
            "two-by-two block of the surface. Over an area each selected tile's own south-west " +
            "vertex moves, so the surface bends outward one tile past the edge of the selection on " +
            "the west and south sides.\n\n" +
            "And it is only ever visible as shading. With the Relief shading layer unticked, or " +
            "the slider near zero, a height change applies correctly, saves correctly and changes " +
            "not one pixel.";

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

                //A tile coordinate means a different tile on another plane, so a selection carried
                //across a plane step would apply a fill to terrain nobody looked at.
                selection.SetPlane(view.Plane);

                InvalidateInspectorScene();
                UpdateInspector(lastHit);
                UpdateStatus();
            };

            layerList.ItemCheck += OnLayerToggled;

            toolStrip.ToolArmed += (_, button) => {
                foreach (KeyValuePair<MapTool, EditorToolButton> entry in toolButtons)
                    if (ReferenceEquals(entry.Value, button))
                        currentTool = entry.Key;

                ApplyToolSelection();
            };

            //BuildLayout has already armed the first tool, so this runs once here as well as on
            //every later change - otherwise the option bar would show nothing at all until the user
            //touched the palette.
            ApplyToolSelection();

            selection.Changed += (_, _) => {
                view.RefreshSelection();
                if (fillButton != null)
                    fillButton.Enabled = !selection.IsEmpty;
                UpdateStatus();
            };

            options.Changed += (_, _) => UpdateStatus();
            options.PickObjectRequested += (_, _) => PickObjectId();

            view.Selection = selection;
            view.DragStarted += OnDragStarted;
            view.DragMoved += OnDragMoved;
            view.DragFinished += (_, _) => OnDragFinished();

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
            bool underlay = pick.Kind == FloorKind.Underlay;
            int cap = underlay ? MapToolLimits.MaximumUnderlayId : MapToolLimits.MaximumOverlayId;

            ArmTool(underlay ? MapTool.PaintUnderlay : MapTool.PaintOverlay);

            if (pick.Id > cap) {
                ShowMessage(pick.Kind + " " + pick.Id + " is past the " + cap +
                    " a tile can store for it, so the brush was left where it was.");
                return;
            }

            if (underlay)
                options.UnderlayId = pick.Id;
            else
                options.OverlayId = pick.Id;

            ShowMessage("Brush set to " + pick.Kind.ToString().ToLowerInvariant() + " " + pick.Id);
        }

        /// <summary>
        ///     Arms a tool from code, exactly as a click on its palette button would.
        /// </summary>
        /// <remarks>
        ///     Goes through the button's own <c>Arm</c> so the palette's checked state and this
        ///     panel's idea of the armed tool cannot disagree - which is the failure the old combo
        ///     made impossible by construction and a palette makes easy.
        /// </remarks>
        /// <param name="tool">The tool to arm.</param>
        private void ArmTool(MapTool tool) {
            if (!toolButtons.TryGetValue(tool, out EditorToolButton? button))
                return;

            button.Arm();
            currentTool = tool;
            ApplyToolSelection();
        }

        /// <summary>
        ///     Takes a tile's floor into the brush.
        /// </summary>
        /// <remarks>
        ///     <b>The tool that turns the hardest question in this tab into an easy one.</b> Every
        ///     paint tool needs a number, and nothing about a number says what it draws; but a user
        ///     looking at the map already knows which patch of ground they want to copy. Pointing at
        ///     it is the answer.
        ///     <para>
        ///     An overlay is preferred over the underlay beneath it when the tile has one, because
        ///     that is what the tile visibly is - the overlay is drawn on top.
        ///     </para>
        ///     <para>
        ///     <b>The shape and rotation are now taken as well as reported.</b> They used to be
        ///     reported only, because the brush had nowhere to put them and always painted shape 0 -
        ///     so an eyedropper over a shaped overlay armed a brush that drew something visibly
        ///     different from the tile it had just copied. The option bar gives them a home, and
        ///     "pick up this floor" finally means all of it.
        ///     </para>
        /// </remarks>
        /// <param name="square">The map square under the pointer.</param>
        /// <param name="hit">Which tile.</param>
        private void PickUpFloor(MapRegion square, TileHit hit) {
            int p = hit.Plane, x = hit.LocalX, y = hit.LocalY;

            int overlay = square.GetOverlayId(p, x, y);
            if (overlay != 0) {
                LoadBrush(new FloorPick(FloorKind.Overlay, overlay));

                byte shape = square.GetOverlayShape(p, x, y);
                byte rotation = square.GetOverlayRotation(p, x, y);

                options.OverlayShape = shape;
                options.OverlayRotation = rotation;

                ShowMessage("Picked up overlay " + overlay + ", shape " + shape +
                    ", rotation " + rotation + " - the brush now paints all three");
                view.Flash(hit.WorldX, hit.WorldY, 1, 1, p, MapFlashKind.Sampled, "overlay " + overlay);
                return;
            }

            int underlay = square.GetUnderlayId(p, x, y);
            if (underlay == 0) {
                view.Flash(hit.WorldX, hit.WorldY, 1, 1, p, MapFlashKind.Rejected, "no floor");
                ShowMessage("That tile stores neither an overlay nor an underlay, so there is " +
                    "nothing to pick up.");
                return;
            }

            LoadBrush(new FloorPick(FloorKind.Underlay, underlay));
            view.Flash(hit.WorldX, hit.WorldY, 1, 1, p, MapFlashKind.Sampled, "underlay " + underlay);
        }

        private MapTool SelectedTool => currentTool;

        /// <summary>Whether a tool draws a selection rather than editing.</summary>
        private static bool IsSelectionTool(MapTool tool) =>
            tool == MapTool.SelectRectangle || tool == MapTool.SelectFreehand
            || tool == MapTool.SelectSimilar;

        /// <summary>
        ///     Applies everything that depends on which tool is armed.
        /// </summary>
        /// <remarks>
        ///     Three things now. The option bar shows the groups this tool reads and the note that
        ///     belongs to it; the canvas is told whether a left drag draws a selection or pans; and
        ///     the height-vertex affordance is turned on for the two height tools only, because it
        ///     is an explanation of what they do rather than a general grid and would be permanent
        ///     clutter under everything else.
        ///     <para>
        ///     The value cap no longer lives here. It was a <c>switch</c> that had to be kept in
        ///     step with a separate tool array by hand; the caps are now stated once in
        ///     <see cref="MapToolLimits"/> and each id box on the option bar is constructed with its
        ///     own, so the two cannot disagree.
        ///     </para>
        /// </remarks>
        private void ApplyToolSelection() {
            ToolSpec spec = SpecFor(currentTool);

            options.ShowFor(spec.Options, spec.NoteKind, spec.NoteCaption, spec.NoteBody);

            view.DragSelects = IsSelectionTool(currentTool)
                               && currentTool != MapTool.SelectSimilar;

            view.ShowVertexAffordance =
                currentTool == MapTool.RaiseHeight || currentTool == MapTool.LowerHeight;

            UpdateStatus();
        }

        private static ToolSpec SpecFor(MapTool tool) {
            foreach (ToolSpec spec in ToolSpecs)
                if (spec.Tool == tool)
                    return spec;

            return ToolSpecs[0];
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
                ShowMessage(ZoomRefusal(SelectedTool));
                return;
            }

            //Loads rather than reading what happens to be resident. A square that was drawn can
            //still have been evicted behind the sweep, and an edit that silently does nothing
            //because of that is the worst possible failure here.
            MapScene scene = store.SceneAround(hit.RegionX, hit.RegionY, loadMissing: true);

            MapRegion square = scene.SquareAt(hit.WorldX - scene.BaseX, hit.WorldY - scene.BaseY);
            if (square == null)
                return;

            //Before BuildEdit, because both of these READ rather than change - there is no edit to
            //build, nothing to undo, and both must work on a square nobody intends to modify. The
            //eyedropper flashes green for the same reason.
            if (SelectedTool == MapTool.Eyedropper) {
                PickUpFloor(square, hit);
                return;
            }

            if (SelectedTool == MapTool.SelectSimilar) {
                RunWand(scene, hit);
                return;
            }

            //A brush wider than one tile, or a live selection, turns a click into an area operation.
            //Below that this stays exactly the single-tile path it always was.
            if (AreaToolFor(SelectedTool) != null && (options.BrushSize > 1 || !selection.IsEmpty)) {
                ApplyBrushArea(hit);
                return;
            }

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
        ///     Why a tool will not run at this zoom, phrased for what it was about to do.
        /// </summary>
        /// <remarks>
        ///     <b>Selecting is gated as hard as editing, deliberately, and this is where it is said
        ///     out loud.</b> The reasons are the same two that gate editing and neither is weaker
        ///     for a selection: below two pixels per tile a tile is sub-pixel, so a user cannot see
        ///     which tiles they are taking; and every square a selection touches has to be decoded
        ///     and pinned for as long as the edit is undoable, which at the fully zoomed-out view
        ///     would be all of them. The alternative - letting a selection be drawn and refusing to
        ///     fill it - would be worse, because it puts the refusal at the point the user has
        ///     already done the work.
        /// </remarks>
        /// <param name="tool">The tool that was clicked with.</param>
        /// <returns>The message to show.</returns>
        private static string ZoomRefusal(MapTool tool) {
            string verb = IsSelectionTool(tool) ? "select" : "edit";
            return $"Zoom in to at least {WorldMapViewControl.MinimumEditingPixelsPerTile:0} px/tile to {verb}" +
                   "  -  below that a tile is smaller than a pixel";
        }

        /// <summary>
        ///     Which area operation a tool maps onto, or <c>null</c> when it has none.
        /// </summary>
        /// <remarks>
        ///     Only the tools whose effect is defined per tile appear here. Cycling the shape of
        ///     whatever object happens to be on top of each of ten thousand tiles is not an
        ///     operation anybody means to ask for, and neither is placing ten thousand copies of one
        ///     object - so those tools stay single-tile and say nothing about the selection.
        /// </remarks>
        /// <param name="tool">The armed tool.</param>
        /// <returns>The area operation, or <c>null</c>.</returns>
        private static MapAreaTool? AreaToolFor(MapTool tool) {
            switch (tool) {
                case MapTool.PaintUnderlay:
                    return MapAreaTool.Underlay;
                case MapTool.PaintOverlay:
                    return MapAreaTool.Overlay;
                case MapTool.ToggleBlockedFlag:
                    return MapAreaTool.BlockedFlag;
                case MapTool.RaiseHeight:
                    return MapAreaTool.RaiseHeight;
                case MapTool.LowerHeight:
                    return MapAreaTool.LowerHeight;
                default:
                    return null;
            }
        }

        /// <summary>The values the option bar currently holds, as the area builder reads them.</summary>
        /// <remarks>
        ///     Blocked is read off Ctrl rather than from a checkbox. Over an area the flag tool sets
        ///     the bit rather than toggling it - a toggle across ten thousand tiles produces a
        ///     checkerboard of whatever happened to be there, which is not an operation anybody
        ///     means to ask for - so there has to be some way to spell "clear it", and the modifier
        ///     is the one that needs no permanent control on the bar.
        /// </remarks>
        private MapAreaOptions AreaOptions(MapAreaTool tool) {
            return new MapAreaOptions {
                Value = tool == MapAreaTool.Overlay ? options.OverlayId : options.UnderlayId,
                OverlayShape = options.OverlayShape,
                OverlayRotation = options.OverlayRotation,
                Blocked = (ModifierKeys & Keys.Control) == 0
            };
        }

        /// <summary>
        ///     Applies the armed tool over the brush footprint, clipped to the selection.
        /// </summary>
        /// <remarks>
        ///     Clipped rather than ignored. Every paint program in existence treats a selection as a
        ///     stencil, and a brush that painted straight through one would make the selection tools
        ///     actively dangerous - the user's mental model would be "this is protected" and the
        ///     editor's would be "this is decoration".
        /// </remarks>
        /// <param name="hit">The tile clicked.</param>
        private void ApplyBrushArea(TileHit hit) {
            var covered = new List<(int WorldX, int WorldY)>();

            foreach ((int worldX, int worldY) in MapBrush.Footprint(hit.WorldX, hit.WorldY,
                         options.BrushSize, options.BrushShape)) {
                if (selection.IsEmpty || selection.Contains(worldX, worldY))
                    covered.Add((worldX, worldY));
            }

            if (covered.Count == 0) {
                view.Flash(hit.WorldX, hit.WorldY, 1, 1, hit.Plane, MapFlashKind.Rejected, "outside");
                ShowMessage("The brush landed entirely outside the selection, so nothing was painted. " +
                    "Clear the selection to paint anywhere.");
                return;
            }

            ApplyArea(covered, hit.Plane, "brush");
        }

        /// <summary>
        ///     Applies the armed tool to every selected tile, as one undo step.
        /// </summary>
        /// <remarks>
        ///     The action the selection tools exist for. Separate from the brush so that filling ten
        ///     thousand tiles is a deliberate act with its own button rather than something a stray
        ///     click can do.
        /// </remarks>
        private void FillSelection() {
            if (store == null)
                return;

            if (selection.IsEmpty) {
                ShowMessage("Nothing is selected. Draw an area with the rectangle, freehand or wand " +
                    "tool first.");
                return;
            }

            if (AreaToolFor(SelectedTool) == null) {
                ShowMessage(SpecFor(SelectedTool).Tooltip + " has no area form - it acts on whatever " +
                    "is already on one tile. Arm a paint, height or flag tool to fill.");
                return;
            }

            if (!view.EditingEnabled) {
                ShowMessage(ZoomRefusal(SelectedTool));
                return;
            }

            ApplyArea(new List<(int WorldX, int WorldY)>(selection.Tiles), selection.Plane, "selection");
        }

        /// <summary>
        ///     Builds one undo step over a set of tiles, applies it, and reports what it cost.
        /// </summary>
        /// <remarks>
        ///     <b>One <see cref="CompositeEdit"/>, never one entry per tile.</b> A fill of ten
        ///     thousand tiles that pushed ten thousand entries onto the history would need ten
        ///     thousand clicks of Undo to reverse, and the history's own <c>Changed</c> event would
        ///     fire that many times on the way in.
        ///     <para>
        ///     <b>Every square is pinned before the edit is recorded.</b> The undo history holds the
        ///     square instances the edits were built against, so a square evicted behind the render
        ///     sweep and reloaded would silently orphan every edit recorded against it.
        ///     </para>
        ///     <para>
        ///     The refusal path is the underlay cap, and it comes out of
        ///     <see cref="MapAreaEdits"/> rather than being re-tested here - one statement of the
        ///     bound, checked wherever a value can reach a tile.
        ///     </para>
        /// </remarks>
        /// <param name="tiles">The tiles to cover.</param>
        /// <param name="plane">The plane to write on.</param>
        /// <param name="what">What to call the operation in the status line.</param>
        private void ApplyArea(IReadOnlyCollection<(int WorldX, int WorldY)> tiles, int plane, string what) {
            MapAreaTool? area = AreaToolFor(SelectedTool);
            if (area == null || store == null)
                return;

            MapAreaEditResult result = MapAreaEdits.Build(tiles, plane, area.Value,
                AreaOptions(area.Value), SquareAtWorld);

            if (result.WasRefused) {
                ShowMessage(result.Refusal!);
                return;
            }

            if (result.Edit == null) {
                ShowMessage($"Every one of those {tiles.Count:N0} tile(s) already holds that, so " +
                    "nothing was written.");
                return;
            }

            UnderStoreLock(() => { history.Apply(result.Edit); return result.Edit; });

            foreach (MapRegion square in result.Edit.Targets)
                store.PinEdited(square);

            lastEditNote = $"last {what}  {result.Changed:N0} tile(s) in {result.Squares} square(s)"
                           + (result.Skipped > 0 ? $", {result.Skipped:N0} already matched" : "");

            //Which flashes the group's own bounds, the same way undoing it will.
            InvalidateFor(result.Edit);

            ShowMessage($"{result.Edit.Description}  -  all {result.Squares} are rewritten on save"
                        + HeightVisibilityWarning(result.Edit));
        }

        /// <summary>
        ///     Resolves a world tile to its square, decoding it when it is not resident.
        /// </summary>
        /// <remarks>
        ///     Decodes rather than reading what happens to be resident, for the same reason the
        ///     single-tile path does: a square that was drawn can still have been evicted behind the
        ///     render sweep, and a fill that silently skipped it would leave a hole in the middle of
        ///     the area with nothing on screen saying why.
        /// </remarks>
        /// <param name="worldX">World X.</param>
        /// <param name="worldY">World Y.</param>
        /// <returns>The square, or <c>null</c> where the cache has none.</returns>
        private MapRegion? SquareAtWorld(int worldX, int worldY) {
            if (store == null)
                return null;

            int regionX = worldX / MapRegion.WIDTH;
            int regionY = worldY / MapRegion.HEIGHT;

            return store.Exists(regionX, regionY) ? store.GetOrLoad(regionX, regionY) : null;
        }

        /// <summary>
        ///     Runs the wand from a click and reports what stopped it.
        /// </summary>
        /// <remarks>
        ///     A read, so it flashes green and leaves nothing on the undo stack, exactly as the
        ///     eyedropper does. The two limits are reported separately because they mean opposite
        ///     things: running out of loaded squares means the shape is incomplete and would grow;
        ///     running out of tile budget means the shape is complete and too big to act on.
        /// </remarks>
        /// <param name="scene">The loaded neighbourhood around the click.</param>
        /// <param name="hit">The tile clicked.</param>
        private void RunWand(MapScene scene, TileHit hit) {
            MapWandResult wand = MapWand.Flood(scene, hit.Plane, hit.WorldX, hit.WorldY,
                options.WandField, options.WandTolerance);

            if (wand.Tiles.Count == 0) {
                view.Flash(hit.WorldX, hit.WorldY, 1, 1, hit.Plane, MapFlashKind.Rejected, "no square");
                ShowMessage("There is no square loaded under that click, so the wand had nothing to " +
                    "match against.");
                return;
            }

            MapSelectionResult applied = selection.Apply(wand.Tiles, ModeFromModifiers());
            if (applied.WasRefused) {
                ShowMessage(applied.Refusal!);
                return;
            }

            view.Flash(hit.WorldX, hit.WorldY, 1, 1, hit.Plane, MapFlashKind.Sampled,
                options.WandField + " " + wand.MatchedValue);

            string limit = wand.ReachedTileLimit
                ? $"  -  stopped at the {MapWand.DefaultTileLimit:N0} tile limit, so the run continues past this"
                : wand.ReachedSceneEdge
                    ? "  -  reached the edge of the loaded squares, so the run may continue past this"
                    : "";

            ShowMessage($"Wand took {wand.Tiles.Count:N0} tile(s) matching {options.WandField.ToString().ToLowerInvariant()} " +
                        $"{wand.MatchedValue}{limit}. {SelectionClause()}");
        }

        /// <summary>
        ///     Whether the drag replaces, adds to or subtracts from the selection.
        /// </summary>
        /// <remarks>
        ///     Shift and Ctrl, which is what every drawing tool on this machine uses, so nothing has
        ///     to be learned. Read at the moment the gesture is committed rather than when it
        ///     started: a user who begins a rectangle and then decides to add holds Shift part way
        ///     through, which is exactly what they expect to work.
        /// </remarks>
        private static MapSelectionMode ModeFromModifiers() {
            if ((ModifierKeys & Keys.Shift) != 0)
                return MapSelectionMode.Add;
            return (ModifierKeys & Keys.Control) != 0
                ? MapSelectionMode.Subtract
                : MapSelectionMode.Replace;
        }

        private void OnDragStarted(object? sender, TileHit hit) {
            if (!view.EditingEnabled) {
                dragAnchor = null;
                ShowMessage(ZoomRefusal(SelectedTool));
                return;
            }

            dragAnchor = hit;
            dragMode = ModeFromModifiers();

            //Committed on the first move rather than here, so a click that turns out to be a pan
            //gesture on another tool has not already thrown the selection away.
            if (SelectedTool == MapTool.SelectFreehand)
                selection.Apply(new[] { (hit.WorldX, hit.WorldY) }, dragMode);
        }

        /// <summary>
        ///     Extends the selection to the tile a drag has reached.
        /// </summary>
        /// <remarks>
        ///     The rectangle recomputes from its anchor on every move rather than accumulating,
        ///     because a rectangle dragged out and then back in has to shrink. Freehand accumulates,
        ///     because that is what freehand means - but it accumulates only the tiles the pointer
        ///     actually crossed, so a fast drag across the screen leaves a dotted line rather than a
        ///     stroke. That is the same behaviour every raster editor has and is why the rectangle
        ///     tool exists.
        /// </remarks>
        private void OnDragMoved(object? sender, TileHit hit) {
            if (dragAnchor == null)
                return;

            MapSelectionResult result;

            if (SelectedTool == MapTool.SelectFreehand) {
                result = selection.Apply(new[] { (hit.WorldX, hit.WorldY) }, dragMode);
            }
            else {
                /* Counted before it is built. The cap inside Apply is the authority, but reaching
                   it by materialising the tiles first means a drag across a 1280-tile-wide viewport
                   walks a million tuples into a hash set on every mouse move before being told no -
                   which makes its own refusal unusable. */
                long wanted = MapSelection.RectangleTileCount(dragAnchor.WorldX, dragAnchor.WorldY,
                    hit.WorldX, hit.WorldY);

                if (wanted > MapSelection.MaximumTiles) {
                    ShowMessage($"That rectangle is {wanted:N0} tiles, past the " +
                        $"{MapSelection.MaximumTiles:N0} an area operation will take. Every square " +
                        "it touches has to be decoded, pinned and rewritten on save.");
                    return;
                }

                result = selection.Apply(
                    MapSelection.RectangleTiles(dragAnchor.WorldX, dragAnchor.WorldY, hit.WorldX, hit.WorldY),
                    dragMode);
            }

            if (result.WasRefused)
                ShowMessage(result.Refusal!);

            /* Nothing else to do: the selection's own Changed event has already run UpdateStatus,
               which carries the tile and square counts. Putting a second message here as well
               replaced the live line - plane, zoom, sweep progress and all - with a shorter one, on
               every mouse move of the drag. */
        }

        private void OnDragFinished() {
            if (dragAnchor == null)
                return;

            dragAnchor = null;
            UpdateStatus();
        }

        /// <summary>Empties the selection and says so.</summary>
        private void ClearSelection() {
            if (selection.IsEmpty) {
                ShowMessage("Nothing was selected.");
                return;
            }

            selection.Clear();
            ShowMessage("Selection cleared - paint tools act on single tiles again.");
        }

        /// <summary>
        ///     Chooses an object id by looking at the list rather than typing a number.
        /// </summary>
        /// <remarks>
        ///     The first consumer of the asset picker. Objects cannot be drawn in this editor, so
        ///     every tile in it is a placeholder carrying its id - which is still strictly more than
        ///     a bare spin box offers, because the picker states how many definitions the cache
        ///     holds and filters by id prefix.
        /// </remarks>
        private void PickObjectId() {
            if (cache == null) {
                ShowMessage("No cache is loaded, so there is nothing to pick from.");
                return;
            }

            int? picked = AssetPickerDialog.Pick(FindForm(), cache, AssetKind.Object, options.ObjectId);
            if (picked == null)
                return;

            options.ObjectId = picked.Value;
            ShowMessage("Place tool set to object " + picked.Value);
        }

        /// <summary>
        ///     What is selected, in the two units that matter.
        /// </summary>
        /// <remarks>
        ///     The square count is the one the user needs and would never think to ask for: every
        ///     square a selection touches is re-encoded and rewritten when the cache is saved, so a
        ///     selection laid across a 3x3 block of squares dirties nine archives whether it covers
        ///     nine tiles of them or nine thousand.
        /// </remarks>
        private string SelectionClause() {
            if (selection.IsEmpty)
                return "no selection";

            int squares = selection.SquareCount;
            return $"{selection.Count:N0} tile(s) selected across {squares} square(s)"
                   + (squares > 1 ? " - all of them are rewritten on save" : "");
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
            //A group counts too. This tested for SetHeightEdit alone, and an area fill of ten
            //thousand height edits is a CompositeEdit - so the warning would have gone silent at
            //exactly the point the user had changed the most terrain and could see the least.
            if (!ChangesHeight(edit))
                return "";

            if ((CheckedLayers() & MapLayers.Hillshade) == 0)
                return "  -  tick Relief shading to see it: a height change is only visible as shading";

            //Below about a sixth of the range a single 32-unit step shades by under two levels of
            //grey, which on textured terrain is indistinguishable from nothing.
            return reliefBar.Value < 15
                ? "  -  raise the Relief slider to see it: one step barely shades at this strength"
                : "";
        }

        /// <summary>Whether an edit, or any edit inside a group, moved terrain.</summary>
        /// <param name="edit">The edit.</param>
        /// <returns>Whether a height was written.</returns>
        private static bool ChangesHeight(IMapEdit edit) {
            if (edit is SetHeightEdit)
                return true;

            if (edit is not CompositeEdit composite)
                return false;

            foreach (IMapEdit member in composite.Edits)
                if (member is SetHeightEdit)
                    return true;

            return false;
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
            //A group has no single area and its own description already carries the tile and square
            //counts, which is what a reader wants from a fill. Falling through to the null below
            //would wipe the record exactly when an area fill was undone.
            if (edit is CompositeEdit group)
                return heading + "  " + group.Description;

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

            switch (tool) {
                case MapTool.PaintUnderlay:
                    return new SetUnderlayEdit(square, p, x, y, options.UnderlayId);

                case MapTool.PaintOverlay:
                    //Shape and rotation come off the brush now. They used to be hardcoded to 0,
                    //which meant laying a shaped overlay was paint-then-cycle-then-cycle with every
                    //intermediate state written to the square.
                    return new SetOverlayEdit(square, p, x, y, options.OverlayId,
                        options.OverlayShape, options.OverlayRotation);

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
                    return new SetHeightEdit(square, p, x, y, MapAreaEdits.StepHeight(square, p, x, y, +1));

                case MapTool.LowerHeight:
                    return new SetHeightEdit(square, p, x, y, MapAreaEdits.StepHeight(square, p, x, y, -1));

                case MapTool.ToggleBlockedFlag:
                    return new SetTileFlagsEdit(square, p, x, y,
                        (byte) (square.GetRenderRule(p, x, y) ^ 0x1));

                case MapTool.PlaceLocation: {
                    int objectId = options.ObjectId;
                    (int wide, int high) = Footprint(objectId, 0);
                    return new AddLocationEdit(square,
                        NewLocation(square, objectId, PlacedLocationShape, 0, p, x, y), wide, high);
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
            /* A group is not an IMapEditArea and never can be - it straddles squares - so without
               this an area fill of ten thousand tiles undone from the button would revert in
               complete silence, which is the exact "did anything happen" problem the flash exists
               for, running backwards and at scale. */
            if (edit is CompositeEdit group) {
                FlashGroup(group, reversed);
                return;
            }

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
        ///     Marks the whole area a group of edits covered, as one rectangle.
        /// </summary>
        /// <remarks>
        ///     The bounding box rather than one flash per member. Ten thousand overlapping flashes
        ///     is ten thousand fades running on the repaint timer and a solid block of amber while
        ///     they do, which says less than one outline does.
        ///     <para>
        ///     Each member's world position is rebuilt from its own square, because a group can
        ///     straddle squares and the local coordinates on the members are square-local.
        ///     </para>
        /// </remarks>
        /// <param name="group">The group.</param>
        /// <param name="reversed"><c>true</c> when it was undone.</param>
        private void FlashGroup(CompositeEdit group, bool reversed) {
            int minX = int.MaxValue, minY = int.MaxValue, maxX = int.MinValue, maxY = int.MinValue;
            int plane = 0, counted = 0;

            foreach (IMapEdit member in group.Edits) {
                if (member is not IMapEditArea area || member.Target == null)
                    continue;

                int id = member.Target.GetRegionID();
                int worldX = MapSquareNames.RegionX(id) * MapRegion.WIDTH + area.LocalX;
                int worldY = MapSquareNames.RegionY(id) * MapRegion.HEIGHT + area.LocalY;

                if (worldX < minX) minX = worldX;
                if (worldX + area.TilesWide - 1 > maxX) maxX = worldX + area.TilesWide - 1;
                if (worldY < minY) minY = worldY;
                if (worldY + area.TilesHigh - 1 > maxY) maxY = worldY + area.TilesHigh - 1;

                plane = area.Plane;
                counted++;
            }

            if (counted == 0)
                return;

            view.Flash(minX, minY, maxX - minX + 1, maxY - minY + 1, plane,
                reversed ? MapFlashKind.Removal : MapFlashKind.Edit,
                $"{counted:N0} tiles");
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
            /* THE TOOL PALETTE AND ITS OPTION BAR GO ALONG THE TOP OF THE CANVAS, NOT IN THE LEFT
               COLUMN. The palette itself is a swap for the combo that was in the Tool group and
               would have fitted; the option bar is genuinely new and would not. The column is 250
               pixels of a window already holding a stack of groups, and the last thing put in it
               either collapsed to nothing under AutoScroll or clipped the layer list. A row of
               tools and a row of labelled options both want WIDTH, which is what the canvas edge
               has and the column does not - and it is where a paint program's tools live anyway.

               A TableLayoutPanel rather than three docked controls: docking resolves in reverse
               z-order, which is the kind of implicit ordering that puts the toolbar under the map
               the first time somebody reorders two lines. */
            var canvasHost = new TableLayoutPanel {
                Dock = DockStyle.Fill,
                ColumnCount = 1,
                RowCount = 3
            };

            canvasHost.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            canvasHost.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            canvasHost.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            canvasHost.RowStyles.Add(new RowStyle(SizeType.Percent, 100));

            canvasHost.Controls.Add(BuildToolPalette(), 0, 0);
            canvasHost.Controls.Add(options, 0, 1);
            canvasHost.Controls.Add(view, 0, 2);

            right.Panel1.Controls.Add(canvasHost);

            /* THE PALETTE LIVES HERE, NOT IN THE LEFT COLUMN, AND THAT WAS THE SECOND ATTEMPT.
               394 swatches need WIDTH: the left column is 250 pixels of a window already holding a
               stack of groups, and putting the palette there either collapsed it to nothing or
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
        ///     the Edits group being pushed off the bottom of the window. That is the guarantee - the
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
            column.Controls.Add(BuildEditsGroup(), 0, 3);

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
        ///     Builds the tool palette from <see cref="ToolSpecs"/>, plus the two selection actions.
        /// </summary>
        /// <remarks>
        ///     <b>Every tool is in one radio group.</b> The strip supports several keyed by an
        ///     arbitrary object, and it is tempting to give the selection tools their own so a
        ///     selection mode and a paint tool could be armed together - but a click can only do one
        ///     thing, and two lit buttons would say otherwise. Selecting and painting are alternated
        ///     rather than combined, and the selection outlives the tool that made it.
        ///     <para>
        ///     <b>The two actions are actions, not tools.</b> Fill and Clear do something once and
        ///     hold no state, so they must not join the radio group - a Fill button that stayed lit
        ///     would read as a mode.
        ///     </para>
        /// </remarks>
        /// <returns>The strip.</returns>
        private EditorToolStrip BuildToolPalette() {
            object group = new object();

            foreach (ToolSpec spec in ToolSpecs) {
                if (Array.IndexOf(GroupStarts, spec.Tool) >= 0)
                    toolStrip.AddSeparator();

                toolButtons[spec.Tool] = toolStrip.AddTool(group, spec.Icon, spec.Tooltip, spec.Shortcut);
            }

            toolStrip.AddSeparator();

            fillButton = toolStrip.AddAction(EditorIcon.FillArea,
                "Apply the armed tool to every selected tile, as one undo step", Keys.G,
                (_, _) => FillSelection());
            fillButton.Enabled = false;

            toolStrip.AddAction(EditorIcon.Remove, "Clear the selection", Keys.D,
                (_, _) => ClearSelection());

            toolStrip.AddSeparator();
            toolStrip.AddAction(EditorIcon.Undo, "Undo the last edit", Keys.Control | Keys.Z,
                (_, _) => StepHistory(history.Undo, "undone", reversed: true));
            toolStrip.AddAction(EditorIcon.Redo, "Redo the last undone edit", Keys.Control | Keys.Y,
                (_, _) => StepHistory(history.Redo, "redone", reversed: false));

            toolButtons[MapTool.Inspect].Arm();
            currentTool = MapTool.Inspect;

            return toolStrip;
        }

        /// <summary>
        ///     Undo, redo and save, in the left column.
        /// </summary>
        /// <remarks>
        ///     What is left of the Tool group once the tool list and its unlabelled value box moved
        ///     to the canvas edge. Renamed with them: a group called "Tool" holding neither a tool
        ///     nor a tool's options would send the next reader looking in the wrong place.
        ///     <para>
        ///     Undo and redo are also on the palette, as actions with their usual Ctrl+Z and Ctrl+Y.
        ///     Deliberately in both places - the buttons here are the only thing that reports
        ///     whether there is anything to undo, since a tool strip button that greys itself out is
        ///     far less legible at 24 pixels than a disabled button with a word on it.
        ///     </para>
        /// </remarks>
        /// <returns>The group box.</returns>
        private GroupBox BuildEditsGroup() {
            var group = new GroupBox {
                Text = "Edits",
                Dock = DockStyle.Fill,
                AutoSize = true,
                AutoSizeMode = AutoSizeMode.GrowAndShrink
            };

            var body = new TableLayoutPanel {
                Dock = DockStyle.Fill,
                AutoSize = true,
                AutoSizeMode = AutoSizeMode.GrowAndShrink,
                ColumnCount = 2,
                RowCount = 2
            };

            body.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
            body.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
            for (int row = 0; row < body.RowCount; row++)
                body.RowStyles.Add(new RowStyle(SizeType.AutoSize));

            //Left-anchored rather than stretched: they measure their own text, and stretching an
            //AutoSize control across a cell is the one combination TableLayoutPanel resolves by
            //guessing.
            undoButton.Anchor = AnchorStyles.Left;
            redoButton.Anchor = AnchorStyles.Left;
            saveButton.Anchor = AnchorStyles.Left;

            body.Controls.Add(undoButton, 0, 0);
            body.Controls.Add(redoButton, 1, 0);

            body.Controls.Add(saveButton, 0, 1);
            body.SetColumnSpan(saveButton, 2);

            group.Controls.Add(body);

            return group;
        }

        /// <summary>
        ///     Routes a tool's shortcut, unless something on screen is being typed into.
        /// </summary>
        /// <remarks>
        ///     <b>The strip cannot catch its own keys.</b> A <c>ToolStripButton</c> has no
        ///     <c>ShortcutKeys</c>, and <c>ProcessCmdKey</c> only reaches a control's own ancestors -
        ///     the palette is a sibling of the canvas the user is working in, never its parent, so
        ///     a strip handling its own keys would work only while the strip itself had focus, which
        ///     is never.
        ///     <para>
        ///     <b>The guard is not optional.</b> <c>ProcessCmdKey</c> runs before the focused control
        ///     sees the key, so without it every letter typed into a spin box on the option bar
        ///     would arm a tool instead. Editable controls are excluded by type rather than by name.
        ///     </para>
        /// </remarks>
        /// <param name="msg">The window message.</param>
        /// <param name="keyData">The key combination.</param>
        /// <returns>Whether it was consumed.</returns>
        protected override bool ProcessCmdKey(ref Message msg, Keys keyData) {
            if (!IsTyping() && toolStrip.HandleShortcut(keyData))
                return true;

            return base.ProcessCmdKey(ref msg, keyData);
        }

        /// <summary>
        ///     Whether the focus is somewhere a letter means a letter.
        /// </summary>
        /// <remarks>
        ///     <c>Form.ActiveControl</c> is the active control of the <em>form's</em> container, not
        ///     the innermost focused one, so reading it alone answers "the split container" for a
        ///     spin box six levels down and every keystroke typed into that box would arm a tool.
        ///     The chain is walked to the bottom instead. A <see cref="NumericUpDown"/> is itself a
        ///     <see cref="ContainerControl"/> whose active child is its own text box, so the walk
        ///     lands on a <see cref="TextBoxBase"/> and the first test catches it.
        /// </remarks>
        /// <returns>Whether a shortcut should be left alone.</returns>
        private bool IsTyping() {
            Control? active = FindForm();

            while (active is ContainerControl container && container.ActiveControl != null)
                active = container.ActiveControl;

            return active is TextBoxBase or NumericUpDown or ComboBox
                   || active?.Parent is NumericUpDown;
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

            //Names world tiles in a cache that is no longer the one on screen, and a fill against
            //it would write to squares the user has never looked at.
            selection.Clear();

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

            //The selection is second, ahead of the sweep counters. It is the figure that decides
            //whether the next click writes one tile or ten thousand, and it names the square count
            //because that is what a save rewrites.
            string selected = selection.IsEmpty ? "" : SelectionClause() + "   ";

            status.Text = $"plane {view.Plane}   {view.Camera.PixelsPerTile:0.###} px/tile   {where}   " +
                          selected + SweepProgress() + "   " +
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
