using BrightIdeasSoftware;
using FlashEditor.cache;
using FlashEditor.cache.sprites;
//RSBufferedImage lives here and derives from SpriteDefinition, so the sprite tab has to be able to
//tell a rendered frame apart from a set before it writes anything back.
using FlashEditor.cache.util;
using FlashEditor.Definitions;
using FlashEditor.Definitions.Billboards;
using FlashEditor.Definitions.Editing;
using FlashEditor.Definitions.Entities;
using FlashEditor.Definitions.Fonts;
using FlashEditor.Definitions.SpotAnims;
using FlashEditor.Definitions.Sprites;
using FlashEditor.Rendering;
using OpenTK.GLControl;
using OpenTK.Graphics.OpenGL;
using OpenTK.Mathematics;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Diagnostics;
using System;
using System.IO;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Runtime.InteropServices;
using System.Windows.Forms;
using FlashEditor.Utils;
using static FlashEditor.Utils.DebugUtil;
using Timer = System.Windows.Forms.Timer;


namespace FlashEditor {
    public partial class Editor : Form {
        internal RSCache cache;
        private readonly ModelRenderer _modelRenderer = new ModelRenderer();
        private GLTextureCache? _textureCache;
        private readonly Dictionary<int, System.Threading.Tasks.Task<ModelDefinition>> _modelTasks = new();

        private readonly ImageList _textureImageList = new ImageList();
        private readonly ContextMenuStrip _textureContextMenu = new ContextMenuStrip();

        /// <summary>
        ///     Where each texture id's tile lives in <see cref="_textureImageList"/>.
        /// </summary>
        /// <remarks>
        ///     Every slot is claimed up front by <see cref="SeedTextureGrid"/> so a finished render
        ///     can be written over the one already there. That is the whole reason the incremental
        ///     load is affordable: measured on this machine, replacing an entry costs 0.28ms while
        ///     <em>adding</em> one to an ImageList that a populated list view is bound to costs 24ms
        ///     and climbs with the row count. The slot is cached rather than found with
        ///     <c>Images.IndexOfKey</c>, which is a linear scan of all 1408 keys.
        /// </remarks>
        private readonly Dictionary<int, int> _textureTileSlots = new();

        /// <summary>
        ///     The representative-colour tile seeded for each texture id, until its render lands.
        /// </summary>
        /// <remarks>
        ///     Held so the placeholder can be released the moment the real tile displaces it, and
        ///     only then. Disposing the whole set on completion would free the one tile still on
        ///     screen for any texture whose thumbnail failed to build.
        /// </remarks>
        private readonly Dictionary<int, Bitmap> _texturePlaceholders = new();

        /// <summary>
        ///     How many finished tiles the render worker collects before handing them to the UI.
        /// </summary>
        /// <remarks>
        ///     Chosen from measurement rather than taste. A publish costs 0.28ms per tile with no
        ///     meaningful fixed overhead - the invalidate that follows is 0.05ms - so batch size
        ///     trades stall length against repaint count linearly and nothing else. 32 puts each
        ///     publish at about 9ms, well inside a frame, and produces roughly 44 repaints across
        ///     the whole sweep instead of 1408.
        /// </remarks>
        private const int TextureTileBatchSize = 32;

        /// <summary>
        ///     The longest a finished tile waits for its batch to fill before being published.
        /// </summary>
        /// <remarks>
        ///     Without it the tail of a slow index, or any run of textures that render faster than
        ///     they group, would sit invisible. The grid should keep moving even when the batch does
        ///     not fill.
        /// </remarks>
        private const int TextureTileBatchIntervalMs = 250;

        /// <summary>
        ///     The tile drawn in the sprite grid for each sprite set, keyed by its group id.
        /// </summary>
        /// <remarks>
        ///     Held rather than rebuilt per paint because the aspect getter is called for every
        ///     visible row on every scroll. A row with no entry here draws
        ///     <see cref="_spritePendingTile"/>, so the grid is complete and scrollable before the
        ///     first group has been read and a finished tile replaces a placeholder without touching
        ///     the row - the same reason the texture grid seeds its slots.
        ///     There is no <see cref="ImageList"/> behind this one: ObjectListView draws an
        ///     <see cref="Image"/> returned from an image getter directly, and an ImageList would
        ///     force every tile through one more resize before the letterboxing had a chance to say
        ///     anything.
        /// </remarks>
        private readonly Dictionary<int, Bitmap> _spriteTiles = new();

        /// <summary>
        ///     The one tile every row that has not been read yet is drawn with.
        /// </summary>
        /// <remarks>
        ///     Shared rather than one per row: 4,593 identical placeholders would be 60MB of grey.
        ///     Flat rather than a checkerboard, because a checkerboard says "these pixels are
        ///     transparent" and a row that has not been read has no pixels to be transparent.
        /// </remarks>
        private Bitmap? _spritePendingTile;

        /// <summary>Whether the sprite grid's columns and tree getters have been wired.</summary>
        /// <remarks>
        ///     Once per form rather than once per load: they are bound to the control, not to the
        ///     cache, and rebinding them on every cache open would leak a font per open.
        /// </remarks>
        private bool _spriteColumnsBound;

        /// <summary>Whether the user has moved the sprite page's splitter themselves.</summary>
        /// <remarks>
        ///     Until they do, the splitter follows the width the grid's columns need, re-measured on
        ///     every resize. Placing it once was not enough: the page is first loaded at the size the
        ///     designer laid it out for and the form is usually maximised afterwards, so a distance
        ///     computed on the first visit left the grid two thirds of the width it wanted for the
        ///     rest of the session.
        /// </remarks>
        private bool _spriteSplitMovedByHand;

        /// <summary>What the sprite grid needs to show every one of its columns in full.</summary>
        private int _spriteGridWidth;

        /// <summary>What each sprite row is showing, keyed by group id.</summary>
        /// <remarks>
        ///     Kept beside the rows rather than on them. A row is a <c>SpriteDefinition</c>, and the
        ///     three states that are not "a picture" - not read yet, no pixels stored, would not
        ///     decode - are facts about this tab's load rather than about the file, so putting them
        ///     on the definition would be putting presentation state into the codec's model.
        /// </remarks>
        private readonly Dictionary<int, SpriteRowStatus> _spriteRowStatus = new();

        /// <summary>The seeded row for each sprite group id.</summary>
        private readonly Dictionary<int, SpriteDefinition> _spriteRows = new();

        /// <summary>Which set and frame index each expanded frame row came from.</summary>
        /// <remarks>
        ///     A rendered frame is an <c>RSBufferedImage</c>, which derives from
        ///     <c>SpriteDefinition</c> and carries the frame's position in its set as <c>index</c> -
        ///     but nothing that says which set, and nothing about the stored offset the frame sits at
        ///     within the canvas. Both are needed to describe the row, so they are recorded when the
        ///     children are handed to the tree, which is the only way a frame row can appear.
        /// </remarks>
        private readonly Dictionary<RSBufferedImage, (SpriteDefinition Set, int Frame)> _spriteFrameOwners = new();

        /// <summary>Tiles for expanded frame rows, keyed by set id and frame index.</summary>
        /// <remarks>
        ///     Built on demand rather than during the load. Only 44 of the vanilla capture's sets
        ///     hold more than one frame, so rendering every frame's tile up front would be 11,177
        ///     tiles to show 4,593 rows.
        /// </remarks>
        private readonly Dictionary<(int Set, int Frame), Bitmap> _spriteFrameTiles = new();

        /// <summary>The bitmap currently on the sprite detail pane, owned here.</summary>
        private Bitmap? _spriteDetailPicture;

        /// <summary>The font the empty and failed markers inside a sprite tile are drawn in.</summary>
        /// <remarks>
        ///     One font for the whole tab rather than one per tile: a tile is built on the load
        ///     worker, and creating a font there per sprite would be 4,593 GDI objects. Sized from
        ///     the tile so the marker still fits when the grid's font, and so the tile, is larger.
        /// </remarks>
        private Font? _spriteMarkerFont;

        /// <summary>The side of one sprite tile in pixels.</summary>
        /// <remarks>
        ///     Measured from the grid's font rather than written down. A list view's row height is a
        ///     pixel count the form's DPI scaling does not touch, so a literal is correct only at the
        ///     DPI it was chosen at - which is the defect that shrank every row on this form while
        ///     the fonts inside them stayed the size they were.
        /// </remarks>
        private int _spriteTileSide;

        /// <summary>How many decoded sprite sets the loader hands to the grid at a time.</summary>
        /// <remarks>
        ///     Same trade as the texture grid's batch: a publish costs a dictionary write and a
        ///     decode per set and the invalidate that follows is one repaint, so the batch size
        ///     trades stall length against repaint count. 48 keeps a publish inside a frame while
        ///     turning 4,593 repaints into about 96.
        /// </remarks>
        private const int SpriteBatchSize = 48;

        /// <summary>The longest a decoded set waits for its batch to fill before being published.</summary>
        private const int SpriteBatchIntervalMs = 250;

        private readonly Timer _fpsTimer = new();
        private readonly ToolTip _modelTooltip = new() { InitialDelay = 300, AutoPopDelay = 30000, ReshowDelay = 100 };
        private int _program;
        private int _testTexture;
        private int _uModel, _uView, _uProj, _uTexture, _uTexOffset, _uLightDir;
        private readonly Stopwatch _animStopwatch = new();
        private Matrix4 _model = Matrix4.Identity;
        private Matrix4 _view;
        private Matrix4 _proj;

        // camera state
        private double _yaw = 0.0, _pitch = 0.0, _distance = 5.0, _fov = 45.0;
        private Vector3 _target = Vector3.Zero;
        private readonly Vector3 _up = Vector3.UnitY;
        private MouseButtons _activeButton = MouseButtons.None;
        private Point _lastMousePos;
        private const float OrbitSpeed = 0.01f;
        private const float PanSpeed = 0.005f;

        private const int LVM_FIRST = 0x1000;
        private const int LVM_SETICONSPACING = LVM_FIRST + 53;

        [DllImport("user32.dll", CharSet = CharSet.Auto)]
        private static extern IntPtr SendMessage(IntPtr hWnd, int msg, int wParam, int lParam);

        private static void SetIconSpacing(ListView lv, int spacing) {
            int param = (spacing << 16) | spacing;
            SendMessage(lv.Handle, LVM_SETICONSPACING, 0, param);
        }
        /// <summary>
        ///     What one editor tab is: the page, the cache index it edits, and how to hand it a
        ///     cache when the tab owns its own panel.
        /// </summary>
        /// <remarks>
        ///     A record per tab rather than a positional array. The array this replaced had to be
        ///     kept in the same order as the pages - it was read as
        ///     <c>editorTypes[EditorTabControl.SelectedIndex]</c> - so inserting a page anywhere but
        ///     the end silently pointed every tab after it at the wrong index, and the only thing
        ///     standing between the editor and that was a comment. Keyed by the page object, a tab
        ///     can be moved, reordered or hidden and still name its own index. That is what made it
        ///     safe to reorder the pages into navigation order when the tab strip was replaced.
        /// </remarks>
        private sealed class EditorTabBinding {
            internal EditorTabBinding(int indexId, EditorCategory category, Action<RSCache>? bind, int[] alsoIndexes) {
                IndexId = indexId;
                Category = category;
                Bind = bind;

                //The routing index first, then whatever else the tab happens to show. Only the
                //first one addresses anything; the rest are here to be read.
                IndexLabel = string.Join(", ", new[] { indexId }.Concat(alsoIndexes));
            }

            /// <summary>The cache index this tab edits.</summary>
            internal int IndexId { get; }

            /// <summary>Which navigation group the tab is filed under.</summary>
            internal EditorCategory Category { get; }

            /// <summary>
            ///     Every index the tab puts on screen, for the navigation entry to show.
            /// </summary>
            /// <remarks>
            ///     Display only, and deliberately separate from <see cref="IndexId"/>: three tabs
            ///     present two indexes each because neither half reads without the other, but
            ///     exactly one of the two is what the tab is routed to. Folding them into one field
            ///     would put the routing decision back into a list, which is the failure the
            ///     positional array made.
            /// </remarks>
            internal string IndexLabel { get; }

            /// <summary>
            ///     Hands the tab's own panel the open cache, for a tab built as a self-contained
            ///     <see cref="UserControl"/> rather than driven from the loader below.
            /// </summary>
            /// <remarks>
            ///     Null for the older tabs, whose loading still lives in <see cref="LoadEditorTab"/>.
            ///     Every new index editor should supply one: that is the whole difference between
            ///     adding a tab and adding another arm to a method that already knows about every
            ///     index before it.
            /// </remarks>
            internal Action<RSCache>? Bind { get; }
        }

        /// <summary>
        ///     The navigation groups, in the order they appear down the left.
        /// </summary>
        /// <remarks>
        ///     Grouped by what someone is doing rather than by index number, because the index
        ///     order is an accident of how the cache grew: models are 7, the frames that animate
        ///     them are 0 and 1, and the animations that sequence those frames are 20. Naming a
        ///     group is required of every tab, so a new editor cannot arrive without a home.
        /// </remarks>
        private enum EditorCategory {
            /// <summary>The cache as a whole - reference tables and containers rather than content.</summary>
            Cache,

            /// <summary>The three definition families a player interacts with directly.</summary>
            Entities,

            /// <summary>Terrain and the effects placed on it.</summary>
            World,

            /// <summary>Geometry and everything that moves it.</summary>
            ModelsAndAnimation,

            /// <summary>Authored assets - pictures, sound and the screens built from them.</summary>
            Media,

            /// <summary>Tables the client reads to configure itself, and the codecs they need.</summary>
            ConfigAndScripts
        }

        /// <summary>
        ///     The heading each <see cref="EditorCategory"/> is drawn with, in navigation order.
        /// </summary>
        /// <remarks>
        ///     Order lives here rather than in the enum's numeric values so that regrouping is one
        ///     edit. Every constant must appear exactly once - checked when the tree is built, for
        ///     the same reason an unregistered page is refused.
        /// </remarks>
        private static readonly (EditorCategory Category, string Caption)[] navCategories = {
            (EditorCategory.Cache, "Cache"),
            (EditorCategory.Entities, "Entities"),
            (EditorCategory.World, "World"),
            (EditorCategory.ModelsAndAnimation, "Models and animation"),
            (EditorCategory.Media, "Media"),
            (EditorCategory.ConfigAndScripts, "Config and scripts")
        };

        /// <summary>
        ///     Holds the editor pages and shows one of them, with no tab strip of its own.
        /// </summary>
        /// <remarks>
        ///     Two dozen editors overflowed a single strip into scroll arrows, so navigation moved
        ///     to a tree down the left. The pages stay <see cref="TabPage"/>s inside a
        ///     <see cref="TabControl"/> anyway: that keeps the registration guard enumerating the
        ///     one collection every editor is in, and keeps lazy loading hanging off
        ///     SelectedIndexChanged, so this change is navigation only and touches no load path.
        ///     <para>
        ///     TCM_ADJUSTRECT is how the control asks itself to reserve room for the strip. Leaving
        ///     it unanswered hands the page the entire client area, which covers the buttons. The
        ///     usual <c>ItemSize</c> and <c>Appearance</c> alternative leaves a sliver whose height
        ///     depends on the active visual style, so it looks right on one machine only.
        ///     </para>
        ///     <para>
        ///     Multiline is what removes the scroll arrows, and it is not cosmetic. A single-line
        ///     tab control whose tabs are wider than itself creates a real <c>msctls_updown32</c>
        ///     child to scroll them, and TCM_ADJUSTRECT does not touch it: that only reshapes the
        ///     display rectangle, so the spinner kept drawing over the top-right of every page and
        ///     could not be clicked, because navigation no longer went through the strip. A
        ///     multiline tab control wraps instead of scrolling and never creates one. The rows it
        ///     wraps onto cost nothing here, since the strip's area is already reclaimed above.
        ///     </para>
        /// </remarks>
        private sealed class PageDeck : TabControl {
            private const int TCM_ADJUSTRECT = 0x1328;

            /// <summary>Creates the deck with the strip suppressed.</summary>
            public PageDeck() {
                //Set here rather than in the designer so a regenerated form cannot drop it and
                //bring the scroll arrows back.
                Multiline = true;
            }

            protected override void WndProc(ref Message m) {
                if (m.Msg == TCM_ADJUSTRECT && !DesignMode) {
                    m.Result = (IntPtr) 1;
                    return;
                }

                base.WndProc(ref m);
            }
        }

        /// <summary>Every editor tab, keyed by the page itself so its position cannot matter.</summary>
        private readonly Dictionary<TabPage, EditorTabBinding> editorTabs = new Dictionary<TabPage, EditorTabBinding>();

        /// <summary>
        ///     What the billboards tab shows, held because the panel treats a different descriptor
        ///     instance as a different thing to show.
        /// </summary>
        /// <remarks>
        ///     Index 29 is the one new tab with no wrapper panel of its own - it is a flat list - so
        ///     its descriptor has nowhere else to live. Building one per bind would reload the index
        ///     on every visit to the tab and throw away the sort and the selection with it.
        /// </remarks>
        private readonly IDefinitionListDescriptor billboards = new BillboardListDescriptor();

        /// <summary>
        ///     What the spot-animations tab shows, held for the reason <see cref="billboards"/> is.
        /// </summary>
        /// <remarks>
        ///     Index 21 is the other new tab that is a flat list with no wrapper panel of its own, so
        ///     its descriptor has nowhere else to live. Building one per bind would reload the index on
        ///     every visit to the tab and throw away the sort and the selection with it.
        /// </remarks>
        private readonly IDefinitionListDescriptor spotAnims = new GraphicListDescriptor();

        /// <summary>
        ///     What the fonts tab shows, held for the reason <see cref="billboards"/> is.
        /// </summary>
        /// <remarks>
        ///     Index 13 is the third flat list with no wrapper panel of its own, so its descriptor has
        ///     nowhere else to live. Building one per bind would reload the index on every visit to the
        ///     tab and throw away the sort and the selection with it.
        /// </remarks>
        private readonly IDefinitionListDescriptor fonts = new FontListDescriptor();

        /// <summary>The tabs already populated for the cache currently open.</summary>
        private readonly HashSet<TabPage> loadedTabs = new HashSet<TabPage>();

        /// <summary>Which navigation node shows which page, so the two can be kept in step.</summary>
        private readonly Dictionary<TabPage, TreeNode> navNodes = new Dictionary<TabPage, TreeNode>();

        /// <summary>
        ///     Set while the tree and the page deck are being pushed into agreement.
        /// </summary>
        /// <remarks>
        ///     Each drives the other - picking a node selects a page, and selecting a page moves the
        ///     highlight - so without this the two bounce a selection back and forth.
        /// </remarks>
        private bool navSyncing;

        List<BackgroundWorker> workers = new List<BackgroundWorker>();
        public Editor() {
            InitializeComponent();
            RegisterEditorTabs();
            BuildNavigationTree();

            //Added here rather than in the designer so the generated file stays untouched
            ToolStripMenuItem saveAsItem = new ToolStripMenuItem("Save As...");
            saveAsItem.Click += saveAsToolStripMenuItem_Click;
            openToolStripMenuItem.DropDownItems.Insert(1, saveAsItem);

            /* Wired once, here, rather than per bind. The entity page is a UserControl that outlives
               every cache the form opens, so subscribing inside the bind delegate would add one more
               handler on every cache open and load the model four times over on the fourth. */
            EntityPanel.EntitySelected += EntityPanel_EntitySelected;
            EntityPanel.AnimationChosen += EntityPanel_AnimationChosen;

            glControl.Load += Gl_Load;
            glControl.Paint += Gl_Paint;
            glControl.Resize += Editor_Resize;
            glControl.MouseDown += Gl_MouseDown;
            glControl.MouseUp += Gl_MouseUp;
            glControl.MouseMove += Gl_MouseMove;
            glControl.MouseWheel += Gl_MouseWheel;

            //The redraw rate, taken from the rendering layer rather than restated as 1000/30. The two
            //were coincidentally equal and independently written down, so a change to one silently
            //left the other behind. Nothing in the playback arithmetic reads it - an animation
            //advances on its own stored durations against elapsed wall-clock time, and this only
            //decides how often that is sampled.
            _fpsTimer.Interval = 1000 / AnimationPlayer.RenderFramesPerSecond;

            //Not started here, and gated on both halves inside the tick. It used to run from the
            //constructor until OnFormClosed and invalidate unconditionally, so a session spent on any
            //other page still repainted a hidden GL surface thirty times a second with nothing
            //animating on it.
            _fpsTimer.Tick += (_, _) => ViewportTick();

            UpdateView();
            UpdateProjection();

            _textureImageList.ColorDepth = ColorDepth.Depth32Bit;
            _textureImageList.ImageSize = new Size(100, 100);
            TextureListView.LargeImageList = _textureImageList;
            SetIconSpacing(TextureListView, 110);

            var dummyItem = new ToolStripMenuItem("Dummy Action");
            dummyItem.Click += (_, _) => DummyMethod();
            _textureContextMenu.Items.Add(dummyItem);
            TextureListView.ContextMenuStrip = _textureContextMenu;
        }

        private void Gl_Load(object sender, EventArgs e) {
            glControl.MakeCurrent();

            int vert = CompileShader(ShaderType.VertexShader, LoadShader("texture.vert"));
            int frag = CompileShader(ShaderType.FragmentShader, LoadShader("texture.frag"));

            _program = GL.CreateProgram();
            GL.AttachShader(_program, vert);
            GL.AttachShader(_program, frag);
            GL.LinkProgram(_program);
            GL.GetProgram(_program, GetProgramParameterName.LinkStatus, out int ok);
            if (ok == 0)
                Debug($"Program link error: {GL.GetProgramInfoLog(_program)}");
            GL.DeleteShader(vert);
            GL.DeleteShader(frag);

            _uModel = GL.GetUniformLocation(_program, "uModel");
            _uView = GL.GetUniformLocation(_program, "uView");
            _uProj = GL.GetUniformLocation(_program, "uProj");
            _uTexture = GL.GetUniformLocation(_program, "uTexture");
            _uTexOffset = GL.GetUniformLocation(_program, "uTexOffset");
            _uLightDir = GL.GetUniformLocation(_program, "uLightDir");
            GL.UseProgram(_program);
            GL.Uniform1(_uTexture, 0);
            GL.Uniform2(_uTexOffset, 0f, 0f);
            GL.Uniform3(_uLightDir, 0f, 0f, 1f);
            GL.UseProgram(0);

            GL.ClearColor(0.2f, 0.2f, 0.2f, 1.0f);
            GL.Enable(EnableCap.DepthTest);
            GL.Enable(EnableCap.Blend);
            GL.BlendFunc(BlendingFactor.SrcAlpha, BlendingFactor.OneMinusSrcAlpha);
            GL.Disable(EnableCap.CullFace);

            UpdateView();
            UpdateProjection();

            _animStopwatch.Start();

            _testTexture = CreateSolidTexture(Color.FromArgb(255, 255, 204, 77));
            float[] verts = {
                // pos(3), normal(3), uv(2), alpha(1), colour(3) = 12 floats per vertex
                -0.5f, -0.5f, 0f, 0f, 0f, 1f, 0f, 0f, 1f, 1f, 0.8f, 0.3f,
                 0.5f, -0.5f, 0f, 0f, 0f, 1f, 1f, 0f, 1f, 1f, 0.8f, 0.3f,
                 0.0f,  0.5f, 0f, 0f, 0f, 1f, 0.5f, 1f, 1f, 1f, 0.8f, 0.3f
            };
            uint[] idx = { 0, 1, 2 };
            _modelRenderer.LoadSimple(verts, idx, _testTexture);
        }

        /// <summary>
        /// Grabs the latest GL error and logs it if non‐zero.
        /// If you see INVALID_OPERATION on a fixed‐pipeline call,
        /// you’re likely in a core‐profile context.
        /// </summary>
        private void CheckGLError(string location) {
            var err = GL.GetError();
            if (err != ErrorCode.NoError) {
                Debug($"GL Error @ {location}: {err}", LOG_DETAIL.ADVANCED);
            }
        }

        private static string LoadShader(string name) {
            var path = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Shaders", name);
            return File.ReadAllText(path);
        }

        private static int CompileShader(ShaderType type, string src) {
            int shader = GL.CreateShader(type);
            GL.ShaderSource(shader, src);
            GL.CompileShader(shader);
            GL.GetShader(shader, ShaderParameter.CompileStatus, out int ok);
            if (ok == 0)
                Debug($"{type} compile error: {GL.GetShaderInfoLog(shader)}");
            return shader;
        }

        private static int CreateSolidTexture(Color color)
        {
            int tex = GL.GenTexture();
            GL.BindTexture(TextureTarget.Texture2D, tex);
            GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMinFilter, (int)TextureMinFilter.Linear);
            GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMagFilter, (int)TextureMagFilter.Linear);
            byte[] pixel = { color.R, color.G, color.B, color.A };
            GL.TexImage2D(TextureTarget.Texture2D, 0, PixelInternalFormat.Rgba, 1, 1, 0, OpenTK.Graphics.OpenGL.PixelFormat.Rgba, PixelType.UnsignedByte, pixel);
            GL.BindTexture(TextureTarget.Texture2D, 0);
            return tex;
        }

        private void UpdateProjection() {
            _proj = Matrix4.CreatePerspectiveFieldOfView(
                MathHelper.DegreesToRadians((float) _fov),
                glControl.Width / (float) glControl.Height,
                1f,
                10000f);
        }

        private Vector3 CameraPosition() {
            return new Vector3(
                _target.X + (float) (_distance * Math.Cos(_pitch) * Math.Sin(_yaw)),
                _target.Y + (float) (_distance * Math.Sin(_pitch)),
                _target.Z + (float) (_distance * Math.Cos(_pitch) * Math.Cos(_yaw))
            );
        }

        private void UpdateView() {
            _view = Matrix4.LookAt(CameraPosition(), _target, _up);
        }

        /// <summary>
        /// Builds and sets the GL viewport tooltip with model statistics.
        /// </summary>
        private void UpdateModelTooltip(string source, IList<int> modelIds, IList<ModelDefinition> defs) {
            if (defs == null || defs.Count == 0) {
                _modelTooltip.SetToolTip(glControl, null);
                return;
            }

            var sb = new System.Text.StringBuilder();
            sb.AppendLine(source);
            sb.AppendLine(new string('-', 32));

            int totalVerts = 0, totalTris = 0, totalTexTris = 0;
            var allTextureIds = new HashSet<int>();

            for (int m = 0; m < defs.Count; m++) {
                var def = defs[m];
                int id = m < modelIds.Count ? modelIds[m] : -1;

                if (defs.Count > 1)
                    sb.AppendLine($"  Model {(id >= 0 ? id.ToString() : "?")}:");

                sb.AppendLine($"  Vertices: {def.VertexCount}");
                sb.AppendLine($"  Triangles: {def.TriangleCount}");
                if (def.TexturedTriangleCount > 0)
                    sb.AppendLine($"  Textured tris: {def.TexturedTriangleCount}");
                sb.AppendLine($"  Format: {def.FormatType}");

                // Collect unique texture IDs
                if (def.FaceTextures != null) {
                    foreach (short tex in def.FaceTextures) {
                        if (tex >= 0) allTextureIds.Add(tex);
                    }
                }

                if (def.FaceAlpha != null) {
                    int transCount = 0;
                    for (int i = 0; i < def.TriangleCount; i++)
                        if ((def.FaceAlpha[i] & 0xFF) > 0) transCount++;
                    if (transCount > 0)
                        sb.AppendLine($"  Translucent faces: {transCount}");
                }

                if (def.ParticleEffectId != 0xFFFF)
                    sb.AppendLine($"  Particle effect: {def.ParticleEffectId}");

                totalVerts += def.VertexCount;
                totalTris += def.TriangleCount;
                totalTexTris += def.TexturedTriangleCount;

                if (defs.Count > 1) sb.AppendLine();
            }

            if (defs.Count > 1) {
                sb.AppendLine(new string('-', 32));
                sb.AppendLine($"Total: {totalVerts} verts, {totalTris} tris");
            }

            if (allTextureIds.Count > 0) {
                var sorted = allTextureIds.OrderBy(x => x);
                sb.AppendLine($"Textures: {string.Join(", ", sorted)}");
            }

            _modelTooltip.SetToolTip(glControl, sb.ToString().TrimEnd());
        }

        /// <summary>
        /// Resets the camera to frame the given model definitions so the full
        /// model is visible with a default viewing angle.
        /// </summary>
        private void FrameModel(IList<ModelDefinition> defs) {
            if (defs == null || defs.Count == 0) return;

            float minX = float.MaxValue, minY = float.MaxValue, minZ = float.MaxValue;
            float maxX = float.MinValue, maxY = float.MinValue, maxZ = float.MinValue;
            int totalVerts = 0;

            foreach (var def in defs) {
                for (int i = 0; i < def.VertexCount; i++) {
                    // Same transform as AppendVertex / ModelRenderer
                    float x = def.VertX[i] / 128f;
                    float y = -def.VertY[i] / 128f;
                    float z = -def.VertZ[i] / 128f;
                    if (x < minX) minX = x;
                    if (y < minY) minY = y;
                    if (z < minZ) minZ = z;
                    if (x > maxX) maxX = x;
                    if (y > maxY) maxY = y;
                    if (z > maxZ) maxZ = z;
                    totalVerts++;
                }
            }

            if (totalVerts == 0) return;

            _target = new Vector3((minX + maxX) / 2f, (minY + maxY) / 2f, (minZ + maxZ) / 2f);

            float dx = maxX - minX, dy = maxY - minY, dz = maxZ - minZ;
            float radius = MathF.Sqrt(dx * dx + dy * dy + dz * dz) / 2f;

            float halfFovRad = MathHelper.DegreesToRadians((float)_fov) / 2f;
            _distance = Math.Max(radius / Math.Sin(halfFovRad), 0.5);

            _yaw = 0.0;
            _pitch = 0.0;
        }


        private void Gl_Paint(object sender, PaintEventArgs e) {
            glControl.MakeCurrent();
            GL.Viewport(0, 0, glControl.Width, glControl.Height);

            GL.Clear(ClearBufferMask.ColorBufferBit | ClearBufferMask.DepthBufferBit);

            UpdateView();
            UpdateProjection();

            GL.UseProgram(_program);
            GL.UniformMatrix4(_uModel, false, ref _model);
            GL.UniformMatrix4(_uView, false, ref _view);
            GL.UniformMatrix4(_uProj, false, ref _proj);
            // Light follows the camera so shading updates as you orbit
            Vector3 lightDir = Vector3.Normalize(CameraPosition() - _target);
            GL.Uniform3(_uLightDir, lightDir.X, lightDir.Y, lightDir.Z);
            float elapsed = (float)_animStopwatch.Elapsed.TotalSeconds;
            _modelRenderer.Draw(elapsed, _uTexOffset);

            //After the model and still inside the program, because all three overlays share the model
            //shader and its twelve-float vertex layout - one attribute binding serves them and no
            //program switch happens between them.
            DrawViewportOverlays();

            GL.UseProgram(0);
            glControl.SwapBuffers();
            CheckGLError("After SwapBuffers");

            //GDI on top of the swapped surface. The index labels are four short strings and the
            //control already has a Graphics; putting text through the GL pipeline would mean a glyph
            //atlas and a second shader for them.
            PaintIndexLabels(e.Graphics);
        }

        public bool IsCacheDirSet() {
            if (string.Equals(Properties.Settings.Default.cacheDir, string.Empty, StringComparison.Ordinal))
                return false;
            return true;
        }

        public void SetCacheDir() {
            if (folderBrowserDialog1.ShowDialog() == DialogResult.OK)
                SetCacheDir(folderBrowserDialog1.SelectedPath);
        }

        public void SetCacheDir(string directory) {
            Properties.Settings.Default.cacheDir = directory;
            Properties.Settings.Default.Save();
            Properties.Settings.Default.Reload();
        }

        /// <summary>
        ///     The cache directory to open, asking for one only when nothing else can supply it.
        /// </summary>
        /// <remarks>
        ///     The persisted setting is the source of truth and is never overridden: whatever the
        ///     user last opened is what opens again. <see cref="CachePaths.Input"/> is consulted only
        ///     when there is no setting at all, and only when what it names really is a cache - it
        ///     falls back to a literal path that may not exist on this machine, and seeding the
        ///     setting with a directory holding no cache would leave the folder picker appearing
        ///     with no explanation.
        /// </remarks>
        /// <returns>The directory the cache will be read from.</returns>
        public string GetCacheDir() {
            if (!IsCacheDirSet()) {
                string discovered = CachePaths.Input;
                if (CachePaths.IsCacheDirectory(discovered)) {
                    Debug("No cache directory set, defaulting to " + discovered);
                    SetCacheDir(discovered);
                }
            }

            while (!IsCacheDirSet())
                SetCacheDir();

            return Properties.Settings.Default.cacheDir;
        }

        private void Editor_Load(object sender, EventArgs e) {
            //Here rather than in the constructor: the form's font scaling runs during layout, before
            //Load, and it would multiply anything set earlier by the same ratio that shrank the
            //designer's literals in the first place.
            SizeViewerControls();

            //Seeded rather than prompted for. A first run with no setting used to open nothing at
            //all and say nothing about it; asking for a folder here would be worse, because the
            //application can usually see a cache from where it is running.
            if (!IsCacheDirSet() && CachePaths.IsCacheDirectory(CachePaths.Input))
                SetCacheDir(CachePaths.Input);

            if (IsCacheDirSet())
                LoadCache(Properties.Settings.Default.cacheDir);
            SpriteListView.AlwaysGroupByColumn = SpriteIdColumn;

            //ObjectListView invokes this on the UI thread while it builds and paints rows, so it
            //is the safety net for any row the texture worker did not supply a bitmap for, not
            //the normal path.
            TextureImage.ImageGetter = rowObject => {
                //The texture id is the ImageList key the worker writes under
                int id = ((TextureDefinition) rowObject).id;
                string key = id.ToString();

                //Answered from the slot map first. SeedTextureGrid claims a slot for every texture
                //before the rows are bound, so during a load this is the only branch that runs -
                //and it has to be O(1), because Images.ContainsKey is a linear scan of all 1408
                //keys and this getter is called once per row for every one of them.
                if (_textureTileSlots.ContainsKey(id))
                    return key;

                if (!TextureListView.LargeImageList!.Images.ContainsKey(key)) { //Assigned _textureImageList in the constructor and never reassigned
                    //Routed through CreateThumbnail rather than new Bitmap(raw, size) so this
                    //produces the same image the worker does. The two disagreed: the worker
                    //composites onto black at HighQualityBicubic while this composited onto a
                    //transparent 32bppArgb surface at the default interpolation, and since the
                    //graph evaluator writes alpha 0 for black pixels the backgrounds differed
                    //visibly on exactly the rows that came through here.
                    Image raw = TextureManager.GetThumbnailForTexture(key);
                    TextureListView.LargeImageList.Images.Add(key, CreateThumbnail(raw));
                }

                //Returning the key lets ObjectListView pull the bitmap out of LargeImageList
                return key;
            };
        }

        private void LoadCache() {
            workers.ForEach(w => w.CancelAsync());
            loadedTabs.Clear();
            LoadCache(GetCacheDir());
        }

        private void LoadCache(string directory) {
            Stopwatch sw = new Stopwatch();
            sw.Start();

            foreach (BackgroundWorker worker in workers) {
                if (worker.IsBusy) {
                    Debug("Cannot interrupt background worker at this time.");
                    return;
                }
            }

            //Opening another cache discards anything staged, so offer to save first
            if(!ConfirmDiscardOrSave())
                return;

            //Clear off the previous crap
            workers.Clear();

            SpriteListView.ClearObjects();
            SpriteListView.Refresh();

            try {
                //Dispose old resources before loading new cache
                DisposeOldResources();

                //Load the cache and the reference tables
                RSFileStore store = new RSFileStore(directory);
                cache = new RSCache(store);
                LoadXTEAKeys(directory);
                _textureCache = new GLTextureCache(cache);

                //The viewport's frame, skeleton and particle sources, on the same terms as every
                //panel bind below: they read through the file store, so one left pointing at the
                //previous cache would decode out of a store that has just been disposed.
                BindViewerAnimation(cache);
                sw.Stop();

                Debug("Loaded cache in " + sw.ElapsedMilliseconds + "ms");

                //Refresh the loaded pages
                loadedTabs.Clear();

                //Go back to the main panel
                LoadEditorTab(EditorTabControl.SelectedTab);
            }
            catch (Exception ex) {
                Debug("Cache failed to load: " + ex.Message);
                Debug(ex.StackTrace ?? string.Empty);
            }
        }

        /// <summary>
        ///     Resolves and loads the XTEA keys for the cache just opened.
        /// </summary>
        /// <remarks>
        ///     Split out of <see cref="LoadCache(string)"/> because the outcome has to be reported
        ///     either way. The map index is the only encrypted content in a 639 cache, and with no
        ///     key table every encrypted square reports as unkeyed and draws with no objects on it -
        ///     which looks like a decoder fault rather than a missing file, and is exactly how this
        ///     went unnoticed while the same bytes decrypted in the test suite.
        ///     <para>
        ///     <c>RSCache.TryAutoLoadXTEAKeys</c> is not used: it probes beside the cache and stops,
        ///     which is right when a key dump sits next to the cache and silent when one does not.
        ///     <c>CachePaths.FindKeyFile</c> starts there and then widens to the application's own
        ///     directory tree, and says where it looked when it finds nothing.
        ///     </para>
        /// </remarks>
        /// <param name="directory">The directory the cache was opened from.</param>
        private void LoadXTEAKeys(string directory) {
            string? keyFile = CachePaths.FindKeyFile(directory, out IReadOnlyList<string> probed);

            if (keyFile != null) {
                cache.LoadXTEAKeys(keyFile);
                Debug("XTEA keys for " + directory + " resolved to " + keyFile);
                return;
            }

            Debug("No XTEA key file found for " + directory +
                  " - every encrypted map square will read as unkeyed. Put xteas.json beside the" +
                  " cache, or in an xteas/ or keys/ directory next to it. Searched: " +
                  string.Join("; ", probed));
        }

        /// <summary>
        ///     Gives the viewport's animation selector a width its own font can fill.
        /// </summary>
        /// <remarks>
        ///     A <see cref="ComboBox"/> cannot auto-size its width, so it is the one control on that
        ///     strip whose size has to be stated at all - everything beside it is <c>AutoSize</c>.
        ///     Measured from the font against the widest id the index can hold rather than written
        ///     into the designer, for the reason <c>DefinitionListPanel</c> derives its progress bar's
        ///     height from its own font: a literal is only right at the DPI it was drawn at, and this
        ///     form scales by <c>AutoScaleMode.Dpi</c>.
        /// </remarks>
        private void SizeViewerControls() {
            //Room for a five-digit id plus the drop-down arrow. Index 20 declares 15,260 records, so
            //five digits is the width that never truncates rather than a guess.
            Size widest = TextRenderer.MeasureText("000000", AnimationSelector.Font);
            AnimationSelector.Width = widest.Width + SystemInformation.VerticalScrollBarWidth;
        }

        /// <summary>
        ///     States which cache index each tab edits, where it is filed in the navigation tree,
        ///     and how the self-contained ones are bound.
        /// </summary>
        /// <remarks>
        ///     Called once, from the constructor, after <c>InitializeComponent</c> has created the
        ///     pages. Adding an editor means adding a line here; getting that wrong is caught on the
        ///     next launch rather than by a tab quietly showing another index's contents.
        /// </remarks>
        private void RegisterEditorTabs() {
            //The console describes the cache as a whole rather than one index, so it takes the
            //meta index - which is also what the loader reads to rebuild it on every visit.
            Register(Console, RSConstants.META_INDEX, EditorCategory.Cache);

            Register(SpriteEditorTab, RSConstants.SPRITES_INDEX, EditorCategory.Media);
            Register(TextureViewerTab, RSConstants.TEXTURES, EditorCategory.Media);

            //The self-contained tabs. Each owns its worker and its layout, so all the form does is
            //hand it the cache.
            /* Indexes 19, 18, 16 and 7 in one page, because the three definition families and the
               models they name are only useful together: seeing an item's model used to mean opening
               Models, then Items, then Models again. Registered against index 19, the way the Tracks
               tab is registered against 6 while listing 11 beside it - the routing index picks the
               page's home in the tree and nothing else, since the page selects its own family. */
            Register(EntityEditorTab, RSConstants.ITEM_DEFINITIONS_INDEX, EditorCategory.Entities,
                openCache => BindEntityPage(openCache),
                RSConstants.NPC_DEFINITIONS_INDEX, RSConstants.OBJECTS_DEFINITIONS_INDEX,
                RSConstants.MODELS_INDEX);
            /* Index 3 has two real levels - a group is one interface and a file is one component -
               so the tab lists interfaces and loads a single interface's components on selection.
               A flat listing of all 42,256 files hid the level that matters. */
            Register(InterfaceEditorTab, RSConstants.INTERFACE_DEFINITIONS_INDEX, EditorCategory.ConfigAndScripts,
                openCache => InterfacePanel.Bind(openCache));
            /* Index 2 is thirty-five unrelated config families sharing one index, so the tab is one
               grid with a group selector: the group is the record type, not a page of ids. */
            Register(ConfigEditorTab, RSConstants.CONFIG, EditorCategory.ConfigAndScripts,
                openCache => ConfigPanel.Bind(openCache));
            Register(MapEditorTab, RSConstants.MAPS_INDEX, EditorCategory.World,
                openCache => MapEditorPanel.Bind(openCache, GetCacheDir()));
            //The tracks tab lists index 11 alongside index 6; 6 is what identifies the tab
            Register(TrackEditorTab, RSConstants.MUSIC_INDEX, EditorCategory.Media,
                openCache => TrackEditorPanel.Bind(openCache), RSConstants.MUSIC_2);
            /* Indexes 0 and 1 share one tab because neither can be read without the other: a frame
               addresses its bones positionally and the bone's transform type is what decides how the
               frame's numbers are read. Index 0 is what identifies the tab, since a row is a frame
               set and index 1 is joined onto it. */
            Register(AnimationEditorTab, RSConstants.FRAMES_INDEX, EditorCategory.ModelsAndAnimation,
                openCache => AnimationPanel.Bind(openCache), RSConstants.SKINS);
            /* Index 4 is one file per group and the group id is the effect id, so the tab is a list
               of records rather than of files. The panel nests three grids under that list because a
               record does: ten tone slots, each with its envelopes and a filter cascade. */
            Register(SoundEffectEditorTab, RSConstants.SOUND_EFFECTS, EditorCategory.Media,
                openCache => SoundEffectPanel.Bind(openCache));
            /* Index 14 is one file per group like index 4, but it is not one list: group 0 is the
               Vorbis setup header every other group is decoded against, so the panel carries a
               detail pane that can describe both shapes and states that the tab does not play
               audio - which is a documented choice rather than a defect, and invisible otherwise. */
            Register(Sfx2EditorTab, RSConstants.SFX2_INDEX, EditorCategory.Media,
                openCache => Sfx2Panel.Bind(openCache));
            /* Index 10 is one group holding one file, so there is nothing to list: the tab shows the
               256 records inside that file and runs text through them, which is the only place in
               the editor where a codec can be watched working rather than trusted. Filed next to
               Quick Chat because quick-chat text is what this table compresses. */
            Register(HuffmanEditorTab, RSConstants.HUFFMAN_INDEX, EditorCategory.ConfigAndScripts,
                openCache => HuffmanPanel.Bind(openCache));
            /* Index 17 is the enum table whatever its constant is called. An enum is a keyed table,
               so the tab lists the enums and shows the selected one's pairs beside them. */
            Register(EnumEditorTab, RSConstants.CLIENTSCRIPT_SETTINGS, EditorCategory.ConfigAndScripts,
                openCache => EnumPanel.Bind(openCache));
            /* Index 22 is one bit range of one varplayer per file. The second level is the varp: a
               range on its own is three numbers, and against its siblings it is one field of a
               packed variable. */
            Register(VarBitEditorTab, RSConstants.SCRIPT_CONFIGS, EditorCategory.ConfigAndScripts,
                openCache => VarBitPanel.Bind(openCache));
            /* Index 28 is two unrelated config blobs sharing an index, so the tab selects between
               them by group in the same shape the Config tab uses on index 2. */
            Register(DefaultsEditorTab, RSConstants.DEFAULTS, EditorCategory.ConfigAndScripts,
                openCache => DefaultsPanel.Bind(openCache));
            /* Index 29 is a single group of records addressed by file id, so there is no second
               level and the shared list panel is the whole tab. */
            Register(BillboardEditorTab, RSConstants.CONFIG_BILLBOARD, EditorCategory.World,
                openCache => BillboardPanel.Bind(openCache, billboards));
            /* Index 20, joined to index 0. An animation names its frames as a packed
               (frameSet << 16) | frame id, and index 0 has no name hashes, so that id is the only
               route from an animation to the frames the Animation tab already presents. */
            Register(AnimationDefinitionsTab, RSConstants.ANIMATIONS_INDEX, EditorCategory.ModelsAndAnimation,
                openCache => AnimationDefinitionPanel.Bind(openCache));
            /* Index 21 is a flat paged index whose editable opcodes each carry one value, so like
               index 29 the shared list panel is the whole tab. */
            Register(SpotAnimEditorTab, RSConstants.GRAPHICS_INDEX, EditorCategory.ModelsAndAnimation,
                openCache => SpotAnimPanel.Bind(openCache, spotAnims));
            /* Indexes 24 and 25 share this tab: each is a complete quick-chat bank holding both
               menus and messages, split by group rather than by index, so the panel selects the bank
               and the family. Registered against 24 the way the Tracks tab is registered against 6
               while listing 11 beside it. */
            Register(QuickChatEditorTab, RSConstants.QUICK_CHAT_MESSAGES, EditorCategory.ConfigAndScripts,
                openCache => QuickChatPanel.Bind(openCache), RSConstants.QUICK_CHAT_MENU);
            /* Index 27 holds emitters in group 0 and the effectors they name in group 1, two formats
               with no opcode in common, so the tab selects the family. */
            Register(ParticleEditorTab, RSConstants.CONFIG_PARTICLES, EditorCategory.World,
                openCache => ParticlePanel.Bind(openCache));
            /* Index 33's two groups are two formats with two codecs - a versioned manifest and the
               screens it categorises - so the tab selects the group. */
            Register(LoadingScreenEditorTab, RSConstants.GAME_TIPS, EditorCategory.Media,
                openCache => LoadingScreenPanel.Bind(openCache));
            /* Index 32 is one file per group, so a list would ordinarily be the whole tab - but the
               index is mixed, holding JPEG images and Jagex glyph sheets with nothing on disk to tell
               them apart, so the panel dispatches on the payload's own FF D8 magic and draws whichever
               picture the row's shape asks for. Filed under Media beside Loading Screens: index 33
               says which pre-login screens exist and this holds the art they are made of. */
            Register(LoadingSpriteEditorTab, RSConstants.LOADING_SPRITES, EditorCategory.Media,
                openCache => LoadingSpritePanel.Bind(openCache));
            /* Index 13 is one file per group with the group id as the font id, so like indexes 21 and
               29 the shared list panel is the whole tab. Filed under Media beside Sprites because a
               font's glyphs are an index-8 sprite set addressed by this same id - the metrics here
               and the pixels there are one asset split across two indexes. */
            Register(FontEditorTab, RSConstants.FONTS_INDEX, EditorCategory.Media,
                openCache => FontPanel.Bind(openCache, fonts));
            /* Index 12 is one compiled CS2 script per group and one file per group, so a script id is
               a group id. Two levels, because a script is an instruction stream: the list is the
               scripts and the panes beside it hold the selected script's instructions and switch
               tables. A flat grid of every instruction in the index would be a third of a million
               rows with nothing to say where one script ends. */
            Register(ClientScriptEditorTab, RSConstants.CLIENT_SCRIPTS_INDEX, EditorCategory.ConfigAndScripts,
                openCache => ClientScriptPanel.Bind(openCache));
            /* Index 23, filed under World beside Map and named so it cannot be mistaken for it. Map
               edits index 5, the terrain the world is built from; this is the pre-rendered overview
               the client draws in its map window, which never reads index 5 at all
               (InterfaceSettings.java:179 hands Class278 index 23 and nothing else). Three unrelated
               families share the index - one details record per area, one tile raster per area and
               one icon group per area - and only the areas are a list, so the tab lists those and
               draws the selected area's raster beside them. */
            Register(WorldMapOverviewTab, RSConstants.WORLD_MAP, EditorCategory.World,
                openCache => WorldMapPanel.Bind(openCache));

            /* Every page in the deck has to have named its index. An unregistered page is the
               failure the positional array made silent - it used to read whatever index happened to
               sit at its position, or run off the end of the array - so it is refused loudly here,
               at construction, rather than left to surface as an editor showing the wrong contents.
               Hiding the tab strip makes this more load-bearing, not less: a page nothing routes to
               is now a page with no way to reach it at all. */
            foreach (TabPage page in EditorTabControl.TabPages)
                if (!editorTabs.ContainsKey(page))
                    throw new InvalidOperationException(
                        "Editor tab '" + page.Name + "' is in the page deck but names no cache index." +
                        " Add a Register call for it in RegisterEditorTabs.");
        }

        /// <summary>
        ///     Records what one editor edits and where it is reached from.
        /// </summary>
        /// <param name="page">The page, which is the key - so its position in the deck is free to change.</param>
        /// <param name="indexId">The cache index the editor is routed to.</param>
        /// <param name="category">Which navigation group it appears under.</param>
        /// <param name="bind">
        ///     Hands the tab's own panel the open cache, for a tab that owns its loading. Null for a
        ///     tab still driven by the loader in <see cref="LoadEditorTab"/>.
        /// </param>
        /// <param name="alsoIndexes">
        ///     Any further indexes the editor puts on screen. Shown in the navigation entry and
        ///     nowhere else - <paramref name="indexId"/> alone decides what is loaded.
        /// </param>
        private void Register(TabPage page, int indexId, EditorCategory category,
            Action<RSCache>? bind = null, params int[] alsoIndexes) {
            if (page == null)
                throw new ArgumentNullException(nameof(page));

            if (editorTabs.ContainsKey(page))
                throw new InvalidOperationException("Editor tab '" + page.Name + "' is registered twice.");

            editorTabs.Add(page, new EditorTabBinding(indexId, category, bind, alsoIndexes));
        }

        /// <summary>
        ///     Fills the left-hand navigation tree from the registrations.
        /// </summary>
        /// <remarks>
        ///     Built from <see cref="editorTabs"/> rather than written out a second time, so an
        ///     editor cannot be registered and then be unreachable, or appear twice, or drift out of
        ///     step with the index it is routed to. The counts are checked at the end for the same
        ///     reason <see cref="RegisterEditorTabs"/> checks the deck: a navigation tree missing an
        ///     entry hides a whole editor and nothing else would say so.
        ///     <para>
        ///     Each entry carries its cache index because this is a cache editor and the index is how
        ///     content is addressed - it is what a user types into every other tool. The two columns
        ///     are padded rather than owner-drawn: the tree is Consolas, so padding is what lines the
        ///     numbers up, and the widths come from the longest entry so a new one cannot break it.
        ///     </para>
        /// </remarks>
        private void BuildNavigationTree() {
            /* Checked for duplicates as well as for count: listing one constant twice and omitting
               another keeps the length right, and would otherwise surface as a duplicate-key throw
               naming a page rather than the table that is actually wrong. */
            EditorCategory[] declared = navCategories.Select(entry => entry.Category).ToArray();
            if (declared.Length != declared.Distinct().Count() ||
                declared.Length != Enum.GetValues<EditorCategory>().Length)
                throw new InvalidOperationException(
                    "Every EditorCategory needs a heading in navCategories, and each exactly once.");

            //Sized from the entries themselves, so a longer caption widens the column rather than
            //pushing the index out of alignment
            int nameWidth = editorTabs.Keys.Max(page => page.Text.Length) + 2;
            int indexWidth = editorTabs.Values.Max(binding => binding.IndexLabel.Length);

            //One font shared by every heading rather than one each, in the designer's own style of
            //letting control fonts live as long as the form
            Font headingFont = new Font(EditorNavTree.Font, FontStyle.Bold);

            EditorNavTree.BeginUpdate();
            EditorNavTree.Nodes.Clear();
            navNodes.Clear();

            foreach ((EditorCategory category, string caption) in navCategories) {
                TreeNode group = EditorNavTree.Nodes.Add(caption);
                group.NodeFont = headingFont;

                /* Deck order, not dictionary order: the deck is what the designer states, so the
                   arrow keys inside it walk the editors in the same order the tree lists them. */
                foreach (TabPage page in EditorTabControl.TabPages) {
                    EditorTabBinding binding = editorTabs[page];
                    if (binding.Category != category)
                        continue;

                    TreeNode node = group.Nodes.Add(
                        page.Text.PadRight(nameWidth) + binding.IndexLabel.PadLeft(indexWidth));
                    node.Tag = page;
                    node.Name = page.Name;
                    navNodes.Add(page, node);
                }

                if (group.Nodes.Count == 0)
                    throw new InvalidOperationException(
                        "Navigation group '" + caption + "' has no editors in it. Remove the group or file one under it.");
            }

            EditorNavTree.ExpandAll();
            EditorNavTree.EndUpdate();

            if (navNodes.Count != editorTabs.Count)
                throw new InvalidOperationException(
                    "The navigation tree lists " + navNodes.Count + " editors but " + editorTabs.Count +
                    " are registered, so at least one cannot be reached.");

            SyncNavigationToDeck();
        }

        /// <summary>
        ///     Shows the page a navigation entry names.
        /// </summary>
        /// <remarks>
        ///     Assigning <c>SelectedTab</c> rather than loading anything here: that raises
        ///     SelectedIndexChanged, which stays the one route into <see cref="LoadEditorTab"/> and
        ///     so into the already-loaded guard. Lazy loading is therefore unchanged by the move to
        ///     a tree.
        ///     <para>
        ///     A group heading carries no page and deliberately does not expand or collapse on
        ///     selection: arrowing down the tree passes over every heading, and collapsing one under
        ///     the cursor would fold away the entries the user is arrowing towards. The glyph,
        ///     double-click and the left and right arrows already toggle it.
        ///     </para>
        /// </remarks>
        private void EditorNavTree_AfterSelect(object sender, TreeViewEventArgs e) {
            if (navSyncing || e.Node?.Tag is not TabPage page)
                return;

            EditorTabControl.SelectedTab = page;
        }

        /// <summary>Moves the navigation highlight onto whatever page the deck is showing.</summary>
        private void SyncNavigationToDeck() {
            TabPage? page = EditorTabControl.SelectedTab;
            if (page == null || !navNodes.TryGetValue(page, out TreeNode? node))
                return;

            navSyncing = true;
            try {
                EditorNavTree.SelectedNode = node;
            }
            finally {
                navSyncing = false;
            }
        }

        /// <summary>
        ///     Populates a tab, once per open cache.
        /// </summary>
        /// <remarks>
        ///     Takes the page rather than its position. The position was the bug: it was used to
        ///     index a parallel array of cache index ids, so the two could disagree and nothing
        ///     would say so.
        /// </remarks>
        /// <param name="page">The tab to populate, typically <c>EditorTabControl.SelectedTab</c>.</param>
        public void LoadEditorTab(TabPage? page) {
            //SelectedTab is null while the tab control has no selection, which happens during
            //construction and after the last page is removed
            if (page == null)
                return;

            if (!editorTabs.TryGetValue(page, out EditorTabBinding? binding)) {
                Debug("Editor tab '" + page.Name + "' names no cache index, so there is nothing to load");
                return;
            }

            int type = binding.IndexId;

            if (cache == null) {
                Debug("Cache failed to load");
                return;
            }

            //Already loaded, no need to reload. The console is the exception: it describes the whole
            //cache rather than one index, so it is rebuilt on every visit.
            if (loadedTabs.Contains(page) && type != RSConstants.META_INDEX)
                return;

            /* A tab that owns its own panel just gets the cache. Below the loaded guard like every
               other tab: rebinding the map discards the undo history and every unsaved map edit with
               it, so a tab revisit has to be a no-op. The binds are idempotent as well, because
               loadedTabs is cleared wholesale whenever a cache is opened. */
            if (binding.Bind != null) {
                loadedTabs.Add(page);
                binding.Bind(cache);
                return;
            }

            /*
             * Once we've loaded the tab, there's no need to reload it every time
             * so lock the editor index from being re-loaded (and hence limit access
             * to a single background worker, otherwise you run into race conditions)
             */

            loadedTabs.Add(page);

            //Creates a new background worker
            BackgroundWorker bgw = new BackgroundWorker {
                WorkerReportsProgress = true,
                WorkerSupportsCancellation = true
            };
            //This enables us to load multiple tabs at once
            workers.Add(bgw);

            /* The sprite arm was the last reader of a reference table fetched here for every tab.
               It enumerated the table's archive entries directly; it now goes through
               RSCache.EnumerateFiles, which snapshots under the cache lock and is the same route
               every other loader takes. Fetching the table up front also meant an index with no
               table threw out of this method before the switch, for tabs that never wanted one. */

            switch (type) {
                case RSConstants.META_INDEX:
                    /* Only the gathering runs on the worker. Populating the two list views used to
                       run here as well, and touching a WinForms control off the UI thread paints it
                       outside any invalidation the form knows about: the reference-table grid stayed
                       drawn over whichever editor was opened next, most visibly on the Models tab,
                       which leaves most of its page uncovered. Hand the results back through
                       RunWorkerCompleted, which is raised on the thread that started the worker. */
                    bgw.DoWork += delegate (object? _, DoWorkEventArgs args) {
                        List<RSReferenceTable> refTables = new List<RSReferenceTable>();
                        for (int k = 0 ; k < cache.referenceTables.Length ; k++)
                            if (cache.referenceTables[k] != null)
                                refTables.Add(cache.referenceTables[k]);

                        List<RSContainer> containers = new List<RSContainer>();
                        foreach (KeyValuePair<int, SortedDictionary<int, RSContainer>> types in cache.containers)
                            foreach (KeyValuePair<int, RSContainer> container in types.Value)
                                containers.Add(container.Value);

                        args.Result = (refTables, containers);
                    };

                    bgw.RunWorkerCompleted += delegate (object? _, RunWorkerCompletedEventArgs args) {
                        if (args.Error != null || args.Cancelled || args.Result == null)
                            return;

                        (List<RSReferenceTable> refTables, List<RSContainer> containers) =
                            ((List<RSReferenceTable>, List<RSContainer>)) args.Result;

                        CompressCol.AspectGetter = (x) => ((RSContainer) x).GetCompressionString();

                        RefTableListView.SetObjects(refTables);
                        ContainerListView.SetObjects(containers);
                    };

                    bgw.Disposed += delegate {
                        workers.Remove(bgw);
                    };

                    bgw.RunWorkerAsync();
                    break;

                case RSConstants.SPRITES_INDEX: {
                        Debug(@" _                     _ _                _____            _ _           ");
                        Debug(@"| |                   | (_)              / ____|          (_| |          ");
                        Debug(@"| |     ___   __ _  __| |_ _ __   __ _  | (___  _ __  _ __ _| |_ ___ ___ ");
                        Debug(@"| |    / _ \ / _` |/ _` | | '_ \ / _` |  \___ \| '_ \| '__| | __/ _ / __|");
                        Debug(@"| |___| (_) | (_| | (_| | | | | | (_| |  ____) | |_) | |  | | ||  __\__ \");
                        Debug(@"|______\___/ \__,_|\__,_|_|_| |_|\__, | |_____/| .__/|_|  |_|\__\___|___/");
                        Debug(@"                                  __/ |        | |                       ");
                        Debug(@"                                 |___/         |_|                       ");
                        Debug(@"Loading Sprites");

                        /* One address per group. Index 8 declares exactly one file per group in both
                           caches, and a sprite set is a group rather than a file, so the row is the
                           group and the file id is read off the table rather than assumed to be 0 -
                           the same rule SpriteFileId states for the import and export paths. */
                        List<(int Group, int File)> addresses = cache.EnumerateFiles(RSConstants.SPRITES_INDEX)
                            .GroupBy(address => address.Group)
                            .Select(group => (Group: group.Key, File: group.First().File))
                            .ToList();

                        BindSpriteColumns();
                        SeedSpriteGrid(addresses);
                        PlaceSpriteSplitter();

                        int tileSide = _spriteTileSide;
                        Font markerFont = _spriteMarkerFont!; //Assigned by BindSpriteColumns, which ran above
                        RSCache open = cache;

                        bgw.ProgressChanged += new ProgressChangedEventHandler((sender, e) => {
                            /* Two payloads share this worker, a batch of decoded sets and a status
                               line, and they are told apart by type rather than by the percentage -
                               so a burst of batches cannot make the progress bar jump about. */
                            if (e.UserState is SpriteSetBatch batch) {
                                ApplySpriteSets(batch);
                                return;
                            }

                            SpriteProgressBar.Value = Math.Clamp(e.ProgressPercentage, 0, 100);
                            SpriteLoadingLabel.Text = e.UserState!.ToString(); //Every other ReportProgress call in this worker passes a status string
                        });

                        bgw.DoWork += (object? s, DoWorkEventArgs args) => {
                            args.Result = ReadSpriteSets(bgw, open, addresses, tileSide, markerFont, args);
                        };

                        bgw.RunWorkerCompleted += (_, e) => {
                            /* A hidden TabPage is not resized until it is shown, and this page is
                               shown by the same selection that starts this load - so the width the
                               splitter was measured against on the way in can be the designer's
                               rather than the window's. Re-placed here, by which time the page has
                               certainly been laid out. */
                            PlaceSpriteSplitter();

                            //And the detail pane, which may be showing "not read yet" for a row this
                            //load has since filled in.
                            ShowSelectedSprite();

                            /* Reading e.Result throws when the worker cancelled or faulted, so both
                               are checked first. LoadEditorTab marks the tab loaded before any work
                               starts, so a fault has to clear that flag or the tab keeps its seeded
                               placeholders for the rest of the session with no way to retry. The
                               rows survive either way, because they were bound before the worker
                               started. */
                            if (e.Cancelled) {
                                loadedTabs.Remove(page);
                                SpriteLoadingLabel.Text = "Sprite load cancelled";
                                return;
                            }

                            if (e.Error != null) {
                                loadedTabs.Remove(page);
                                SpriteLoadingLabel.Text = "Sprite load failed";
                                Debug($"Loading sprites failed: {e.Error.GetType().Name}: {e.Error.Message}", LOG_DETAIL.BASIC);
                                return;
                            }

                            var outcome = (SpriteLoadOutcome) e.Result!; //DoWork assigns Result on every path that is not cancelled or faulted
                            SpriteProgressBar.Value = 100;
                            SpriteLoadingLabel.Text = outcome.Describe();
                            Debug("Sprites loaded: " + outcome.Describe(), LOG_DETAIL.BASIC);
                        };

                        bgw.Disposed += delegate {
                            workers.Remove(bgw);
                        };

                        bgw.RunWorkerAsync();
                        break;
                    }
                case RSConstants.TEXTURES: {
                        //This case used to call LoadTextures inline and leave the worker created
                        //above orphaned, so the whole 1408-definition render sweep ran on the UI
                        //thread before the message loop could pump - which is what made the tab
                        //look frozen. Snapshot on the UI thread, then do everything on the worker.
                        //TextureManager.Textures is a static dictionary shared with GLTextureCache
                        //and the map path, so it is copied rather than enumerated off-thread.
                        List<TextureDefinition> snapshot = TextureManager.Textures.Values.ToList();

                        //Dropping the previous cache's rows first. The list view now stays live and
                        //paintable for the whole load, and its ImageGetter would otherwise be asked
                        //for tiles for textures that no longer exist.
                        TextureListView.ClearObjects();

                        TextureProgressBar.Value = 0;
                        TextureLoadingLabel.Text = $"Preparing {snapshot.Count} tiles";

                        bgw.ProgressChanged += new ProgressChangedEventHandler((sender, e) => {
                            //Two kinds of report share this worker: a batch of finished tiles, and a
                            //status line. Separating them by the payload type rather than by the
                            //percentage keeps the progress bar driven only by the percent-boundary
                            //reports, so a burst of batches cannot make it jump about.
                            if (e.UserState is TextureTileBatch batch) {
                                ApplyTextureTiles(batch);
                                return;
                            }

                            TextureProgressBar.Value = Math.Clamp(e.ProgressPercentage, 0, 100);
                            TextureLoadingLabel.Text = e.UserState!.ToString(); //Every other ReportProgress call in this worker passes a status string
                        });

                        bgw.DoWork += (object? s, DoWorkEventArgs args) => {
                            args.Result = RenderTextureThumbnails(bgw, snapshot, args);
                        };

                        bgw.RunWorkerCompleted += (_, e) => {
                            //Reading e.Result throws when the worker cancelled or faulted, so both
                            //are checked first. LoadEditorTab marks the tab loaded before any work
                            //starts, so a fault has to clear that flag or the tab stays empty for
                            //the rest of the session with no way to retry. The rows survive either
                            //way, because they were bound before the worker started - a cancelled
                            //load leaves a grid of representative colours rather than an empty tab.
                            if (e.Cancelled) {
                                loadedTabs.Remove(page);
                                TextureLoadingLabel.Text = "Texture load cancelled";
                                return;
                            }

                            if (e.Error != null) {
                                loadedTabs.Remove(page);
                                TextureLoadingLabel.Text = "Texture load failed";
                                Debug($"LoadTextures failed: {e.Error.GetType().Name}: {e.Error.Message}", LOG_DETAIL.BASIC);
                                return;
                            }

                            TextureProgressBar.Value = 100;
                            TextureLoadingLabel.Text = $"Textures loaded ({(int) e.Result!})";
                            TextureManager.PrintDiagnostics();
                        };

                        bgw.Disposed += delegate {
                            workers.Remove(bgw);
                        };

                        //Started only once every row is bound and every tile slot exists. A batch
                        //that arrived before its slot did would have nowhere to write, and seeding
                        //costs about 1.7s against a sweep that runs for two minutes.
                        SeedTextureGrid(snapshot, () => bgw.RunWorkerAsync());
                        break;
                    }

            }
        }

        /// <summary>
        ///     When you flick to a different editor page.
        /// </summary>
        /// <remarks>
        ///     Still the only place a page load is triggered from, whether the page was reached from
        ///     the navigation tree or by the deck's own arrow keys, so the highlight is moved from
        ///     here rather than from the tree's handler.
        /// </remarks>
        private void EditorTabControl_SelectedIndexChanged(object sender, EventArgs e) {
            SyncNavigationToDeck();
            LoadEditorTab(EditorTabControl.SelectedTab);

            //Half the render timer's gate. Leaving the model page stops the clock rather than leaving
            //it repainting a surface nobody is looking at, and returning to it starts it again only
            //if something on the viewport is actually moving.
            SyncViewportTimer();
        }

        /// <summary>The cache index the selected tab edits, or -1 when it names none.</summary>
        public int GetEditorType() {
            TabPage? page = EditorTabControl.SelectedTab;
            return page != null && editorTabs.TryGetValue(page, out EditorTabBinding? binding)
                ? binding.IndexId
                : -1;
        }

        /// <summary>
        ///     Writes the selected rows out as PNG files.
        /// </summary>
        /// <remarks>
        ///     Through <see cref="SpritePainter.ToDisplayBitmap"/> rather than by saving the
        ///     rasteriser's own bitmap. That bitmap is declared premultiplied and holds straight
        ///     ARGB, so saving it directly encodes every frame carrying an alpha plane wrongly - the
        ///     same mislabelling the tab's own drawing has to work around, and one that only shows on
        ///     the 180 frames of the vanilla capture that store a plane at all.
        ///     <para>
        ///     A frame row is named for the set it came from as well as its own position, because a
        ///     frame's <c>index</c> is its place in the set and would otherwise collide with the file
        ///     written for the sprite set of the same number.
        ///     </para>
        /// </remarks>
        /// <param name="sender">The button.</param>
        /// <param name="e">The event data.</param>
        private void ExportSpriteBmpBtn_Click(object sender, EventArgs e) {
            string dir = GetCacheDir() + "\\sprites";
            Directory.CreateDirectory(dir);

            int written = 0;
            foreach (object row in SpriteListView.SelectedObjects) {
                if (row is not SpriteDefinition sprite)
                    continue;

                RSBufferedImage? frame;
                string name;
                if (row is RSBufferedImage frameRow) {
                    SpriteDefinition? owner = SpriteSetBehind(frameRow);
                    frame = frameRow;
                    name = (owner == null ? "frame" : owner.index.ToString()) + "_" + frameRow.index;
                } else {
                    if (!SpritePainter.CanRasterise(sprite))
                        continue;
                    frame = sprite.GetFrame(0);
                    name = sprite.index.ToString();
                }

                using Bitmap? picture = SpritePainter.ToDisplayBitmap(frame);
                if (picture == null)
                    continue;

                picture.Save(Path.Combine(dir, name + ".png"), System.Drawing.Imaging.ImageFormat.Png);
                written++;
            }

            SpriteLoadingLabel.Text = "Exported " + written + " picture(s)";
        }

        private void SetDirectoryToolStripMenuItem_Click(object sender, EventArgs e) {
            SetCacheDir();
            LoadCache();
        }

        private void OpenDirectoryToolStripMenuItem_Click(object sender, EventArgs e) {
            if (IsCacheDirSet())
                Process.Start(GetCacheDir());
        }

        /// <summary>
        ///     Writes the stored bytes of every selected sprite set out as a <c>.dat</c> file.
        /// </summary>
        /// <remarks>
        ///     The <b>stored</b> bytes, read straight back out of the cache, rather than a re-encode
        ///     of the decoded set. The two agree for every group in both caches, but an export is what
        ///     an import is later checked against, so it has to be the file rather than our opinion of
        ///     the file - a codec defect would otherwise be exported and imported without ever being
        ///     visible.
        ///     <para>
        ///     Frame rows are skipped. The tree's children are <see cref="RSBufferedImage"/> instances
        ///     wrapped as sprite sets for display; a frame is not separately addressable in the cache
        ///     and has no bytes of its own to write.
        ///     </para>
        /// </remarks>
        private void ExportSpriteDatBtn_Click(object sender, EventArgs e) {
            if (cache == null)
                return;

            List<SpriteDefinition> sets = SelectedSpriteSets();
            if (sets.Count == 0) {
                SpriteLoadingLabel.Text = "Select a sprite set to export";
                return;
            }

            string directory = Path.Combine(RSConstants.CACHE_OUTPUT_DIRECTORY, "sprites");

            try {
                Directory.CreateDirectory(directory);

                int written = 0;
                foreach (SpriteDefinition set in sets) {
                    byte[] stored = cache.ReadFileBytes(RSConstants.SPRITES_INDEX, set.index, SpriteFileId(set.index));
                    File.WriteAllBytes(Path.Combine(directory, set.index + ".dat"), stored);
                    written++;
                }

                SpriteLoadingLabel.Text = "Exported " + written + " sprite set(s)";
                Debug("Exported " + written + " sprite sets to " + directory);
            }
            catch (Exception ex) {
                //Reported rather than thrown: a failed export must cost the export and nothing else
                SpriteLoadingLabel.Text = "Export failed";
                Debug("Sprite export failed: " + ex);
                MessageBox.Show(this,
                    "Could not export to:" + Environment.NewLine + directory +
                    Environment.NewLine + Environment.NewLine + ex.Message,
                    "Export sprites", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        /// <summary>
        ///     Replaces the selected sprite set with the bytes of a sprite file on disk.
        /// </summary>
        /// <remarks>
        ///     The button existed with no handler attached at all, so index 8 was read-only in the
        ///     editor whatever the codec beneath it could do - the same shape index 18's write path
        ///     was in.
        ///     <para>
        ///     The file is decoded before anything is staged. A sprite set is located from the end of
        ///     the file backwards, so a wrong length is not a truncated set but a set whose palette and
        ///     frame metadata are read out of the pixel planes; <c>SpriteDefinition.Decode</c> refuses
        ///     that rather than producing a plausible picture, which is what makes the check worth
        ///     something. The file's own bytes are then what gets stored, not a re-encode of what was
        ///     decoded, so the import does not depend on our encoder agreeing with whatever wrote the
        ///     file.
        ///     </para>
        ///     <para>
        ///     Nothing is written when the cache already holds those bytes. The comparison is against
        ///     the <b>decompressed</b> file - a GZip re-encode is never byte-identical in this cache,
        ///     so comparing containers would report a difference every time and rewrite the group, its
        ///     CRC, and the reference-table entry of every group packed beside it.
        ///     </para>
        /// </remarks>
        private void ImportSpriteBtn_Click(object sender, EventArgs e) {
            if (cache == null || SpriteListView.SelectedObject is not SpriteDefinition target ||
                target is RSBufferedImage) {
                MessageBox.Show(this, "Select the sprite set to overwrite first.", "Import sprite",
                                MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            using OpenFileDialog picker = new OpenFileDialog {
                Title = "Import sprite set " + target.index,
                Filter = "Sprite set (*.dat)|*.dat|All files (*.*)|*.*"
            };

            if (picker.ShowDialog(this) != DialogResult.OK)
                return;

            try {
                byte[] imported = File.ReadAllBytes(picker.FileName);

                //Decoded into a throwaway first, so a file that will not parse costs nothing at all.
                //The selected row is only touched once the file is known to be readable and the
                //write has been staged.
                SpriteDefinition validated = SpriteDefinition.DecodeFromStream(new JagStream(imported));

                int fileId = SpriteFileId(target.index);

                if (cache.ReadFileBytes(RSConstants.SPRITES_INDEX, target.index, fileId).AsSpan().SequenceEqual(imported)) {
                    SpriteLoadingLabel.Text = "Sprite " + target.index + " already holds those bytes";
                    return;
                }

                cache.WriteFile(RSConstants.SPRITES_INDEX, target.index, fileId, new JagStream(imported));

                /* The selected instance is re-decoded in place rather than swapped for another. It is
                   a node of a TreeListView whose children are its own rasterised frames, so replacing
                   the object means rebuilding that branch; disposing it drops the frames and the
                   thumbnail, which describe the bytes that were there before this, and the next paint
                   rasterises the new ones lazily. */
                target.Dispose();
                target.Decode(new JagStream(imported));

                //The grid draws from a tile built when the set was loaded, so the row would otherwise
                //keep showing the picture the file held before the import.
                RedrawSpriteRow(target);

                SpriteListView.RefreshObject(target);
                SpriteLoadingLabel.Text = "Imported sprite " + target.index + " (" + validated.GetFrameCount() + " frames)";
            }
            catch (Exception ex) {
                //Reported rather than thrown: a malformed file must cost the import and nothing else
                Debug("Sprite import failed: " + ex);
                MessageBox.Show(this,
                    "Could not import that file as a sprite set:" + Environment.NewLine + ex.Message,
                    "Import sprite", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        /// <summary>The selected rows that are sprite sets rather than rendered frames.</summary>
        /// <returns>The selected sets, which may be empty.</returns>
        private List<SpriteDefinition> SelectedSpriteSets() {
            var sets = new List<SpriteDefinition>();
            foreach (object row in SpriteListView.SelectedObjects)
                if (row is SpriteDefinition set && row is not RSBufferedImage)
                    sets.Add(set);
            return sets;
        }

        /// <summary>
        ///     The file id a sprite group holds, read off the reference table rather than assumed.
        /// </summary>
        /// <remarks>
        ///     Every group in both caches holds exactly one file and its id is 0, but the id is
        ///     declared rather than derived - <c>CacheAddressing.FileOf</c> refuses to answer for a
        ///     <c>GroupPerId</c> index for exactly this reason, and index 23 is the case that proves a
        ///     single-file group's id is not always 0.
        /// </remarks>
        /// <param name="groupId">The sprite set's group id in index 8.</param>
        /// <returns>The file id within that group.</returns>
        /// <exception cref="InvalidOperationException">The group declares no file.</exception>
        private int SpriteFileId(int groupId) {
            int[] fileIds = cache.GetFileIds(RSConstants.SPRITES_INDEX, groupId);
            if (fileIds.Length == 0)
                throw new InvalidOperationException(
                    "Index " + RSConstants.SPRITES_INDEX + " group " + groupId + " declares no file.");
            return fileIds[0];
        }

        /// <summary>
        /// This is where the magic gets done.
        /// And I really mean magic, because if this works then I am a literal god.
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private void saveAllToolStripMenuItem_Click_1(object sender, EventArgs e) {
            SaveCache(GetCacheDir());
        }

        /// <summary>Prompts for a directory and writes a complete copy of the cache there.</summary>
        private void saveAsToolStripMenuItem_Click(object sender, EventArgs e) {
            if(folderBrowserDialog1.ShowDialog() == DialogResult.OK)
                SaveCache(folderBrowserDialog1.SelectedPath);
        }

        /// <summary>
        ///     Commits the staged cache to <paramref name="directory"/>. Returns false when the
        ///     save failed, so a caller guarding a close can keep the window open.
        /// </summary>
        private bool SaveCache(string directory) {
            if(cache == null)
                return true;

            try {
                //Through the map panel's store lock, which is the lock its render thread decodes
                //under. Writing replaces the dat2 and every index file on disk, and the render
                //thread runs for the whole life of the Map tab - a decode overlapping the
                //replacement used to read a closed memory map. MapEditorPanel.SaveEdits already
                //takes this gate for its own save; this is the same operation from the File menu.
                MapEditorPanel.RunExclusive(() => cache.WriteCache(directory));
                Debug("Saved cache to " + directory);
                return true;
            }
            catch(Exception ex) {
                Debug("Save failed: " + ex.Message);
                MessageBox.Show(this,
                    "Could not save the cache to:" + Environment.NewLine + directory +
                    Environment.NewLine + Environment.NewLine + ex.Message,
                    "Save failed", MessageBoxButtons.OK, MessageBoxIcon.Error);
                return false;
            }
        }

        /// <summary>
        ///     Offers to save when edits are staged. Returns false to abort whatever the caller
        ///     was about to do, whether that is closing the window or opening another cache.
        /// </summary>
        private bool ConfirmDiscardOrSave() {
            if(cache == null || !cache.HasUnsavedChanges)
                return true;

            DialogResult choice = MessageBox.Show(this,
                "Save changes to the cache before continuing?",
                "Unsaved changes", MessageBoxButtons.YesNoCancel, MessageBoxIcon.Warning);

            if(choice == DialogResult.Cancel)
                return false;
            if(choice == DialogResult.No)
                return true;

            return SaveCache(GetCacheDir());
        }

        protected override void OnFormClosing(FormClosingEventArgs e) {
            //OnFormClosed runs after the window is gone and cannot cancel, so the guard is here
            if(!ConfirmDiscardOrSave())
                e.Cancel = true;

            base.OnFormClosing(e);
        }

        private void button4_Click(object sender, EventArgs e) {
            AnalyseCaches();
        }

        public void AnalyseCaches() {
            Debug(@"                      _           _             ");
            Debug(@"    /\               | |         (_)            ");
            Debug(@"   /  \   _ __   __ _| |_   _ ___ _ _ __   __ _ ");
            Debug(@"  / /\ \ | '_ \ / _` | | | | / __| | '_ \ / _` |");
            Debug(@" / ____ \| | | | (_| | | |_| \__ \ | | | | (_| |");
            Debug(@"/_/    \_\_| |_|\__,_|_|\__, |___/_|_| |_|\__, |");
            Debug(@"                         __/ |             __/ |");
            Debug(@"                        |___/             |___/ ");
            Debug(@"Analysing");

            int diff = AnalyseCache("dat2");
            foreach (KeyValuePair<int, RSIndex> index in cache.GetStore().indexChannels)
                diff += AnalyseCache("idx" + index.Key);

            Debug("Analysis complete, " + (diff > 0 ? diff + " differences found" : "no differences found"));
        }

        public int AnalyseCache(string file) {
            string cacheIn = RSConstants.CACHE_DIRECTORY + "/main_file_cache.";
            string cacheOut = RSConstants.CACHE_OUTPUT_DIRECTORY + "/main_file_cache.";

            try {
                //Load the two caches into a stream
                JagStream inputCache = JagStream.LoadStream(cacheIn + file);
            }
            catch (Exception ex) {
                Debug(ex.Message);
            }

            return 0;
        }

        /// <summary>Reopens the pristine copy, discarding whatever the working cache holds.</summary>
        private void button5_Click(object sender, EventArgs e) {
            ReopenAt(CachePaths.Pristine, "pristine copy", CachePaths.PristineVariable);
        }

        /// <summary>Reopens the directory edits are written to, so a save can be inspected.</summary>
        private void button6_Click(object sender, EventArgs e) {
            ReopenAt(CachePaths.Output, "edited copy", CachePaths.OutputVariable);
        }

        /// <summary>
        ///     Points the editor at one of the two secondary cache directories.
        /// </summary>
        /// <remarks>
        ///     Checked before the setting is written. Both directories are resolved rather than
        ///     hardcoded now, and the pristine copy in particular is one the user takes rather than
        ///     one the editor makes, so it legitimately may not exist - and pointing the persisted
        ///     setting at a directory holding no cache would leave the editor unable to open
        ///     anything on the next launch either.
        /// </remarks>
        /// <param name="directory">The directory to reopen from.</param>
        /// <param name="what">What that directory is, for the message when it holds no cache.</param>
        /// <param name="variable">The environment variable that points it somewhere else.</param>
        private void ReopenAt(string directory, string what, string variable) {
            if (!CachePaths.IsCacheDirectory(directory)) {
                Debug("No cache at the " + what + " directory: " + directory);
                MessageBox.Show(this,
                    "There is no cache in the " + what + " directory:" + Environment.NewLine + directory +
                    Environment.NewLine + Environment.NewLine +
                    "Set " + variable + " to point somewhere else, or put a copy of the cache there.",
                    "Reload cache", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            SetCacheDir(directory);
            LoadCache(GetCacheDir());
        }

        //Set the alternating row back color
        private void alternateRowsToolStripMenuItem_Click(object sender, EventArgs e) {
            TreeListView[] tlvs = { RefTableListView, ContainerListView, SpriteListView };
            DialogResult result = colorDialog1.ShowDialog();

            foreach (TreeListView tlv in tlvs) {
                tlv.UseAlternatingBackColors = result == DialogResult.OK;
                tlv.AlternateRowBackColor = colorDialog1.Color;
                tlv.Refresh();
            }

            /* The three grids this used to reach into by name are one DefinitionListPanel now, which
               owns its list rather than exposing it. Asking the panel is what keeps the menu working
               across a family switch: the shading has to survive the grid being rebound, and a
               handle taken here would be to whichever list happened to be showing. */
            EntityPanel.SetAlternatingRows(result == DialogResult.OK, colorDialog1.Color);
        }

        /// <summary>Redraws the sprite detail pane at the magnification the user asked for.</summary>
        /// <param name="sender">The zoom control.</param>
        /// <param name="e">The event data.</param>
        private void SpriteZoom_ValueChanged(object sender, EventArgs e) {
            SpritePreview.Zoom = (int) SpriteZoom.Value;
        }

        /// <summary>Shows or hides the outline marking the frame's own pixels within the canvas.</summary>
        /// <param name="sender">The check box.</param>
        /// <param name="e">The event data.</param>
        private void SpriteFrameOutline_CheckedChanged(object sender, EventArgs e) {
            SpritePreview.OutlineFrame = SpriteFrameOutline.Checked;
        }


        /// <summary>
        /// Applies NPC recolouring, retexturing, and per-model translation
        /// offsets to a loaded model, matching the RS client merge pipeline.
        /// </summary>
        private static void ApplyNpcTransforms(ModelDefinition def, NPCDefinition npc, int modelIndex) {
            // Recolour: replace face HSL colours matching src → dst
            if (npc.recolorSrc != null && npc.recolorDst != null) {
                for (int f = 0; f < def.TriangleCount; f++) {
                    for (int c = 0; c < npc.recolorSrc.Length; c++) {
                        if (def.FaceColour[f] == (short)npc.recolorSrc[c]) {
                            // Opcode 42 palette plumbing
                            if (npc.recolorDstPalette != null && c < npc.recolorDstPalette.Length) {
                                int idx = npc.recolorDstPalette[c] & 0xFF;
                                if (idx < ColourPalette.Entries.Length && ColourPalette.Entries[idx] != 0)
                                    def.FaceColour[f] = ColourPalette.Entries[idx];
                                else
                                    def.FaceColour[f] = (short)npc.recolorDst[c];
                            } else {
                                def.FaceColour[f] = (short)npc.recolorDst[c];
                            }
                            break;
                        }
                    }
                }
            }

            // Retexture: replace texture IDs matching src → dst
            if (npc.retextureSrc != null && npc.retextureDst != null && def.FaceTextures != null) {
                for (int f = 0; f < def.TriangleCount; f++) {
                    for (int t = 0; t < npc.retextureSrc.Length; t++) {
                        if (def.FaceTextures[f] == (short)npc.retextureSrc[t]) {
                            def.FaceTextures[f] = (short)npc.retextureDst[t];
                            break;
                        }
                    }
                }
            }

            // Per-model translation offset (opcode 121)
            // Hydra client (Class141:1175) applies raw signed byte values directly
            // after the conditional <<2 upscale — no additional shift on translations.
            if (npc.translations != null && modelIndex < npc.translations.Length
                && npc.translations[modelIndex] != null) {
                int[] t = npc.translations[modelIndex];
                for (int v = 0; v < def.VertexCount; v++) {
                    def.VertX[v] += t[0];
                    def.VertY[v] += t[1];
                    def.VertZ[v] += t[2];
                }
            }

            // NPC scale application (opcodes 97/98)
            if (npc.scaleXY != 128 || npc.scaleZ != 128) {
                for (int v = 0; v < def.VertexCount; v++) {
                    def.VertX[v] = def.VertX[v] * npc.scaleXY / 128;
                    def.VertY[v] = def.VertY[v] * npc.scaleZ / 128;
                    def.VertZ[v] = def.VertZ[v] * npc.scaleXY / 128;
                }
            }
        }

        /// <summary>
        /// Applies item recolouring and retexturing to a loaded model.
        /// </summary>
        private static void ApplyItemTransforms(ModelDefinition def, ItemDefinition item) {
            if (item.originalModelColors != null && item.modifiedModelColors != null) {
                for (int f = 0; f < def.TriangleCount; f++) {
                    for (int c = 0; c < item.originalModelColors.Length; c++) {
                        if (def.FaceColour[f] == item.originalModelColors[c]) {
                            def.FaceColour[f] = item.modifiedModelColors[c];
                            break;
                        }
                    }
                }
            }

            if (item.textureColour1 != null && item.textureColour2 != null && def.FaceTextures != null) {
                for (int f = 0; f < def.TriangleCount; f++) {
                    for (int t = 0; t < item.textureColour1.Length; t++) {
                        if (def.FaceTextures[f] == item.textureColour1[t]) {
                            def.FaceTextures[f] = item.textureColour2[t];
                            break;
                        }
                    }
                }
            }
        }

        /// <summary>
        ///     Points the entity page at a cache and refills the viewport's animation selector.
        /// </summary>
        /// <remarks>
        ///     The selector holds every id index 20 declares, which is what lets a model be posed by
        ///     an animation nothing in the cache says belongs to it. It used to ride along on the
        ///     Models tab's enumeration worker; that tab is gone, so it gets a worker of its own -
        ///     off the UI thread for the reason it always was, that a table walk over 120 groups is
        ///     not free and the page it stalls is the one being opened.
        /// </remarks>
        /// <param name="openCache">The open cache.</param>
        private void BindEntityPage(RSCache openCache) {
            EntityPanel.Bind(openCache);

            BackgroundWorker animationIds = new BackgroundWorker { WorkerSupportsCancellation = true };
            workers.Add(animationIds);

            animationIds.DoWork += (_, args) => args.Result = EnumerateAnimationIds(openCache);

            animationIds.RunWorkerCompleted += (_, args) => {
                //Reading Result throws when the worker cancelled or faulted, so both are checked
                //first. A failure costs the selector and nothing else - the grid beside it is
                //already loading through its own worker.
                if (args.Error != null || args.Cancelled || args.Result == null) {
                    Debug("Animation ids could not be listed: " + args.Error?.Message);
                    return;
                }

                PopulateAnimationSelector((List<int>) args.Result);
            };

            animationIds.Disposed += delegate {
                workers.Remove(animationIds);
            };

            animationIds.RunWorkerAsync();
        }

        /// <summary>
        ///     Loads whatever the entity page has selected into the viewport.
        /// </summary>
        /// <remarks>
        ///     One handler where there were four, and the four were identical apart from which
        ///     definition they read model ids out of. The row type is what selects the arm rather
        ///     than which grid raised the event, because there is only one grid now.
        ///     <para>
        ///     A null row is the family selector having moved rather than a defect: the page reports
        ///     that its selection is gone before the new family's grid has loaded. The viewport keeps
        ///     the models it has, because unloading them would leave an empty rectangle that reads as
        ///     a broken tab while the grid beside it fills.
        ///     </para>
        /// </remarks>
        /// <param name="sender">The entity page.</param>
        /// <param name="e">What is selected.</param>
        private void EntityPanel_EntitySelected(object? sender, EntitySelectionEventArgs e) {
            if (e.Kind != EntityKind.Npc)
                EntityPanel.ClearAnimations("Animations are listed for NPCs, which name a render animation set.");

            switch (e.Row) {
                case ItemDefinition item:
                    ShowItemModel(item);
                    break;
                case NPCDefinition npc:
                    ShowNpcAnimations(npc);
                    ShowNpcModels(npc);
                    break;
                case ObjectDefinition definition:
                    ShowObjectModels(definition);
                    break;
                case ModelListing listing:
                    ShowModel(listing);
                    break;
            }
        }

        /// <summary>Plays the animation the entity page's selector names.</summary>
        /// <remarks>
        ///     The viewport's own id box is moved to the same id, so the two selectors cannot end up
        ///     showing different animations while one of them is playing. Setting <c>Text</c> rather
        ///     than <c>SelectedItem</c>: the box lists every id index 20 declares and searching
        ///     fifteen thousand items for one is not worth it to move a caption.
        /// </remarks>
        /// <param name="sender">The entity page.</param>
        /// <param name="animationId">The index-20 id.</param>
        private void EntityPanel_AnimationChosen(object? sender, int animationId) {
            AnimationSelector.Text = animationId.ToString();
            LoadViewerAnimation(animationId);
        }

        /// <summary>Lists the animations an NPC's render animation set names.</summary>
        /// <param name="npc">The selected NPC.</param>
        private void ShowNpcAnimations(NPCDefinition npc) {
            if (cache == null)
                return;

            IReadOnlyList<NpcAnimation> animations = NpcAnimationSet.For(cache, npc, out string reason);
            EntityPanel.ShowAnimations(animations, reason);
        }

        /// <summary>
        ///     Uploads one model from index 7 to the viewport.
        /// </summary>
        /// <remarks>
        ///     The decode is kept off the UI thread and the upload is kept on it, because the GL
        ///     context is current only on the thread that owns the control. The decoded definition is
        ///     memoised in <c>cache.models</c>, which is what makes stepping back and forth through
        ///     the grid cheap.
        /// </remarks>
        /// <param name="listing">The row.</param>
        private void ShowModel(ModelListing listing) {
            if (cache == null || _textureCache == null)
                return;

            int id = listing.ModelId;

            if (cache.models.TryGetValue(id, out ModelDefinition? cached)) {
                UploadModels(new[] { cached },
                    "Model " + id + " (group " + id + ", file " + listing.FileId + ")", new[] { id });
                return;
            }

            //One task per id, shared: the selection can pass over the same row twice before the
            //first decode has finished, and a second task for it would decode the same bytes again.
            if (!_modelTasks.TryGetValue(id, out Task<ModelDefinition>? task)) {
                task = Task.Run(() => cache.GetModelDefinition(listing.Address.GroupId, listing.FileId));
                _modelTasks[id] = task;
            }

            task.ContinueWith(finished => {
                _modelTasks.Remove(id);

                if (finished.Status != TaskStatus.RanToCompletion) {
                    Debug("Model " + id + " failed to load: " + finished.Exception?.Flatten().InnerException);
                    return;
                }

                cache.models[id] = finished.Result;
                UploadModels(new[] { finished.Result },
                    "Model " + id + " (group " + id + ", file " + listing.FileId + ")", new[] { id });
            }, TaskScheduler.FromCurrentSynchronizationContext());
        }

        /// <summary>Uploads an item's inventory model, recoloured and retextured as the item asks.</summary>
        /// <param name="item">The selected item.</param>
        private void ShowItemModel(ItemDefinition item) {
            if (cache == null || _textureCache == null)
                return;

            int modelId = item.inventoryModelId;
            if (modelId <= 0)
                return;

            LoadModelsAsync(new[] { modelId },
                (definition, id) => ApplyItemTransforms(definition, item),
                "Item " + item.id + " '" + item.name + "' (model " + modelId + ")");
        }

        /// <summary>Uploads an NPC's models, with its recolours, retextures and per-model offsets.</summary>
        /// <param name="npc">The selected NPC.</param>
        private void ShowNpcModels(NPCDefinition npc) {
            if (cache == null || _textureCache == null || npc.modelIds == null)
                return;

            int[] ids = npc.modelIds.Where(id => id >= 0).ToArray();
            if (ids.Length == 0)
                return;

            LoadModelsAsync(ids,
                //The index into the NPC's own array, not into the filtered list: opcode 121's
                //translations are positional, so a model whose siblings included a -1 would take the
                //offset belonging to a different slot.
                (definition, id) => ApplyNpcTransforms(definition, npc, Array.IndexOf(npc.modelIds!, id)),
                "NPC " + npc.id + " '" + npc.name + "'");
        }

        /// <summary>Uploads an object's first render group, which is its default orientation.</summary>
        /// <param name="definition">The selected object.</param>
        private void ShowObjectModels(ObjectDefinition definition) {
            if (cache == null || _textureCache == null)
                return;

            if (definition.modelIds == null || definition.modelIds.Length == 0 || definition.modelIds[0] == null)
                return;

            int[] ids = definition.modelIds[0].Where(id => id > 0).Select(id => (int) id).ToArray();
            if (ids.Length == 0)
                return;

            LoadModelsAsync(ids, null, "Object " + definition.id + " '" + definition.name + "'");
        }

        /// <summary>
        ///     Decodes a set of models off the UI thread and uploads them on it.
        /// </summary>
        /// <remarks>
        ///     Shared by the item, NPC and object arms, which had three copies of this between them.
        ///     The models are cloned for rendering before any transform touches them: recolouring
        ///     writes into the face colour array, and the definitions are memoised, so transforming
        ///     one in place would leave the next entity that names the same model wearing the last
        ///     one's colours.
        /// </remarks>
        /// <param name="modelIds">The models to load, in the order they should be uploaded.</param>
        /// <param name="transform">Applied to each decoded model, with its id. Null for none.</param>
        /// <param name="source">What the viewport is showing, for the tooltip.</param>
        private void LoadModelsAsync(int[] modelIds, Action<ModelDefinition, int>? transform, string source) {
            RSCache open = cache;

            Task.Run(() => {
                List<ModelDefinition> loaded = new List<ModelDefinition>(modelIds.Length);

                foreach (int id in modelIds) {
                    try {
                        ModelDefinition definition = open.GetModelDefinition(id, 0).CloneForRendering();
                        transform?.Invoke(definition, id);
                        loaded.Add(definition);
                    }
                    catch (Exception failure) {
                        //One model that will not decode costs itself, not the whole entity.
                        Debug("Model " + id + " failed to load: " + failure.Message);
                    }
                }

                return loaded;
            }).ContinueWith(finished => {
                if (finished.Status != TaskStatus.RanToCompletion || finished.Result.Count == 0)
                    return;

                UploadModels(finished.Result, source, modelIds);
            }, TaskScheduler.FromCurrentSynchronizationContext());
        }

        /// <summary>
        ///     Hands decoded models to the renderer, the picker, the animator and the particle system.
        /// </summary>
        /// <remarks>
        ///     The one place any of that happens, and it runs on the UI thread only: every call below
        ///     touches GL, and the context is current on the thread that owns the control. The order
        ///     matters as well - <c>SetViewerModels</c> indexes by the same model position the
        ///     renderer was given, so a set built from a different list would highlight one face and
        ///     pose another.
        /// </remarks>
        /// <param name="definitions">The models, in upload order.</param>
        /// <param name="source">What the viewport is showing, for the tooltip.</param>
        /// <param name="modelIds">The ids those models came from, for the tooltip.</param>
        private void UploadModels(IList<ModelDefinition> definitions, string source, IList<int> modelIds) {
            if (!glControl.IsHandleCreated || _textureCache == null || definitions.Count == 0)
                return;

            glControl.MakeCurrent();

            if (_testTexture != 0) {
                GL.DeleteTexture(_testTexture);
                _testTexture = 0;
            }

            _modelRenderer.LoadMultiple(definitions, _textureCache);
            SetViewerModels(new List<ModelDefinition>(definitions));
            FrameModel(definitions);
            UpdateModelTooltip(source, modelIds, definitions);
            glControl.Invalidate();
        }

        private void Editor_Resize(object sender, EventArgs e) {
            // The GLControl may not have created its underlying window yet
            // when the form is resized before the Models tab is opened.
            // Avoid touching OpenGL until the control has a handle.
            if (!glControl.IsHandleCreated)
                return;

            glControl.MakeCurrent();
            GL.Viewport(0, 0, glControl.Width, glControl.Height);
            UpdateProjection();
            glControl.Invalidate();
        }

        private void Gl_MouseDown(object? sender, MouseEventArgs e) {
            _activeButton = e.Button;
            _lastMousePos = e.Location;
            glControl.Focus();
        }

        private void Gl_MouseUp(object? sender, MouseEventArgs e) {
            _activeButton = MouseButtons.None;
        }

        private void Gl_MouseWheel(object? sender, MouseEventArgs e) {
            float factor = 1f - e.Delta * 0.001f;
            _distance = Math.Max(_distance * factor, 0.1);
            glControl.Invalidate();
        }

        private void Gl_MouseMove(object? sender, MouseEventArgs e) {
            if (_activeButton == MouseButtons.None) {
                //Hovering rather than dragging. Picking during a drag would flicker the highlight
                //across every face the cursor swept on its way round the model.
                UpdateHoverPick(e.Location);
                return;
            }

            int dx = e.X - _lastMousePos.X;
            int dy = e.Y - _lastMousePos.Y;

            if (_activeButton == MouseButtons.Left) {
                _yaw += dx * OrbitSpeed;
                _pitch -= dy * OrbitSpeed;
                double limit = MathHelper.DegreesToRadians(89.0);
                _pitch = Math.Clamp(_pitch, -limit, limit);
            }
            else if (_activeButton == MouseButtons.Right) {
                Vector3 camPos = CameraPosition();
                Vector3 forward = Vector3.Normalize(_target - camPos);
                Vector3 right = Vector3.Normalize(Vector3.Cross(forward, _up));
                Vector3 realUp = Vector3.Normalize(Vector3.Cross(right, forward));
                _target += (-right * dx + realUp * dy) * PanSpeed;
            }

            _lastMousePos = e.Location;
            glControl.Invalidate();
        }

        private void DisposeOldResources() {
            //First, and unconditionally. Unbinding joins the map's render thread, which is the one
            //thing still reading through cache.store on a background thread. LoadEditorTab only
            //rebinds the tab that happens to be selected, so a reload started from any other tab
            //would otherwise leave that thread decoding out of a disposed file store.
            MapEditorPanel.Bind(null);

            //Same reason again, and the one that runs on a timer rather than a worker: the viewport's
            //animation and particle sources read frames, skeletons and emitters straight through the
            //cache on every tick, so the timer has to be stopped and both unbound before the store
            //goes. Nothing else here stops that clock.
            BindViewerAnimation(null);

            //Same reason, one step weaker: this cancels the definition list's worker rather than
            //joining it, so a load already inside a group read can still see the store close. Every
            //group read there is guarded, so the worst case is a discarded result - which it was
            //going to be anyway, since unbinding supersedes it.
            InterfacePanel.Bind(null);

            //And the entity page, whose grid sweeps whichever of indexes 19, 18, 16 and 7 is
            //selected. Index 16 is the one that matters - 56,199 records over 224 groups - but the
            //reason is the one every panel below has: a reload started from any other page would
            //otherwise leave a sweep decoding out of a file store that is about to be disposed.
            EntityPanel.Bind(null);

            //Same again: the config tab's record list reads a whole group of index 2 on a worker.
            ConfigPanel.Bind(null);

            //Same again: the animation tab's frame-set sweep reads every group in index 0, so a
            //reload started from another tab would leave it decoding out of a disposed file store.
            AnimationPanel.Bind(null);

            //And the four newest tabs, each of which owns a DefinitionListPanel whose worker walks a
            //whole index. Index 17 and index 22 are the ones that matter - 3,558 and 8,785 files -
            //but all four are unbound for the same reason: a reload started from any other tab would
            //otherwise leave a sweep reading out of a file store that is about to be disposed.
            EnumPanel.Bind(null);
            VarBitPanel.Bind(null);
            DefaultsPanel.Bind(null);
            BillboardPanel.Bind(null);

            //And the five newest, on exactly the same terms. Index 20 is the one that matters here -
            //15,260 records - but every one of them owns a DefinitionListPanel whose worker walks a
            //group or a whole index, so a reload started from any other tab would otherwise leave a
            //sweep reading out of a file store that is about to be disposed.
            AnimationDefinitionPanel.Bind(null);
            SpotAnimPanel.Bind(null);
            QuickChatPanel.Bind(null);
            ParticlePanel.Bind(null);
            LoadingScreenPanel.Bind(null);

            //And index 13, on the same terms: its sweep decodes every group in the index, so a reload
            //started from another tab would otherwise leave it reading out of a disposed file store.
            FontPanel.Bind(null);

            //And index 14, on the same terms again. This one walks 3,657 groups, so a reload started
            //from another tab has a real window in which its worker is still reading.
            Sfx2Panel.Bind(null);

            //And index 12, which is the largest sweep of the three-grid tabs at 4,149 groups: a
            //reload started from another tab would otherwise leave it decoding scripts out of a file
            //store that is about to be disposed.
            ClientScriptPanel.Bind(null);

            if(SpriteListView.Objects != null) {
                foreach(object obj in SpriteListView.Objects) {
                    if(obj is SpriteDefinition sprite)
                        sprite.Dispose();
                }
            }

            //And the tiles drawn from those sets, which are GDI bitmaps this form owns rather than
            //anything the definitions hold: one per sprite set, plus one per frame row ever expanded.
            ReleaseSpriteTiles();
            _spriteRows.Clear();
            _spriteRowStatus.Clear();
            TextureManager.Clear();
            _textureImageList.Images.Clear();

            //The slot map indexes into the ImageList that was just emptied, so leaving it behind
            //would have the next cache's batches write over slots that no longer exist. The
            //placeholders still held here are the ones no render displaced.
            _textureTileSlots.Clear();
            foreach (Bitmap placeholder in _texturePlaceholders.Values)
                placeholder.Dispose();
            _texturePlaceholders.Clear();

            _textureCache?.Dispose();
            _textureCache = null;
            cache?.store?.Dispose();
        }

        protected override void OnFormClosed(FormClosedEventArgs e) {
            _fpsTimer.Stop();
            DisposeOldResources();

            /* The sprite placeholder and its marker font outlive a cache: the grid's columns are
               bound once for the form, so releasing them with the rest of the tiles would leave the
               next cache's rows drawing a disposed bitmap. They belong to the form, so they go here. */
            _spritePendingTile?.Dispose();
            _spritePendingTile = null;
            _spriteMarkerFont?.Dispose();
            _spriteMarkerFont = null;
            _modelRenderer.Dispose();

            //Before the context goes, and on this thread. Every handle it holds is a GL object, and
            //deleting one from anywhere else is undefined - which is why it is IDisposable rather
            //than finalised.
            _viewportOverlay?.Dispose();
            _viewportOverlay = null;
            _indexLabelFont.Dispose();
            if (_testTexture != 0)
            {
                GL.DeleteTexture(_testTexture);
                _testTexture = 0;
            }
            if (_program != 0)
                GL.DeleteProgram(_program);
            base.OnFormClosed(e);
        }

        private void DummyMethod() {
            MessageBox.Show("Dummy action executed.");
        }

        /// <summary>
        /// Builds the 100x100 tile bitmap the texture list shows for one texture.
        /// </summary>
        /// <remarks>
        /// Every path that fills a tile has to come through here. The graph evaluator writes
        /// alpha 0 for a black pixel, so drawing onto a cleared-to-black surface rather than onto
        /// a transparent one is a visible difference, and two paths that disagree about it show
        /// up as a handful of tiles with the wrong background.
        /// </remarks>
        private static Bitmap CreateThumbnail(Image img, int width = 100, int height = 100) {
            var bmp = new Bitmap(width, height, System.Drawing.Imaging.PixelFormat.Format32bppArgb);
            using (var g = Graphics.FromImage(bmp)) {
                g.Clear(Color.Black);
                g.CompositingQuality = CompositingQuality.HighQuality;
                g.InterpolationMode = InterpolationMode.HighQualityBicubic;
                g.SmoothingMode = SmoothingMode.HighQuality;
                g.DrawImage(img, 0, 0, width, height);
            }
            return bmp;
        }

        /// <summary>
        /// The 100x100 tile drawn for a texture that produced no image at all.
        /// </summary>
        /// <remarks>
        /// This replaced a branch that drew the texture id over the colour and then bicubic
        /// resampled the result from 100x100 to 100x100, which cost a font measurement and a
        /// resample to arrive at the same flat colour. It is also all but unreachable, because
        /// <see cref="TextureManager.EnsureRendered"/> already falls back to this colour on every
        /// exit - and it read <c>Control.Font</c>, which the worker thread must not touch.
        /// </remarks>
        private static Bitmap SolidTextureThumbnail(int rgb) {
            var bmp = new Bitmap(100, 100, System.Drawing.Imaging.PixelFormat.Format32bppArgb);
            using (var g = Graphics.FromImage(bmp))
                g.Clear(Color.FromArgb(255, (rgb >> 16) & 0xFF, (rgb >> 8) & 0xFF, rgb & 0xFF));
            return bmp;
        }

        /// <summary>
        ///     Fills the grid with a tile per texture before a single graph has been rendered, then
        ///     runs <paramref name="onSeeded"/>.
        /// </summary>
        /// <remarks>
        ///     <para>
        ///     The tab used to bind its rows only once the whole sweep had finished, so for two
        ///     minutes it showed an empty grid and read as broken. Every id is known the moment the
        ///     cache opens - <c>GLTextureCache</c> runs <c>TextureManager.Load</c> then - and the
        ///     representative colour costs one HSL conversion, so a complete grid is available
        ///     immediately and the renders only sharpen it.
        ///     </para>
        ///     <para>
        ///     Seeded in chunks posted back through <c>BeginInvoke</c> rather than in one loop. The
        ///     ImageList inserts are the expensive half, about 1ms each, and doing all 1408 in one
        ///     turn blocks the message loop for 1.75s; measured, chunking costs the same total and
        ///     cuts the worst stall to 226ms, so the tab stays usable while it fills. The rows are
        ///     bound only after the last chunk, because inserting into an ImageList that a
        ///     populated list view is bound to costs 24ms an image instead of 1ms.
        ///     </para>
        /// </remarks>
        /// <param name="textures">Every row to bind, in the order the grid should show them.</param>
        /// <param name="onSeeded">Runs on the UI thread once every slot exists and the rows are bound.</param>
        private void SeedTextureGrid(List<TextureDefinition> textures, Action onSeeded) {
            const int SeedChunk = 64;

            _textureImageList.Images.Clear();
            _textureTileSlots.Clear();

            //Anything still here is a placeholder from an earlier seed that no render displaced,
            //and the ImageList it was drawn from has just been emptied.
            foreach (Bitmap stale in _texturePlaceholders.Values)
                stale.Dispose();
            _texturePlaceholders.Clear();

            //Opening another cache disposes every definition in the snapshot and empties the
            //ImageList underneath a seed that is still running, so a continuation that outlives its
            //cache has to stop rather than bind rows that no longer decode.
            RSCache seededFor = cache;

            void SeedFrom(int start) {
                if (!ReferenceEquals(cache, seededFor))
                    return;

                int end = Math.Min(start + SeedChunk, textures.Count);
                for (int i = start; i < end; i++) {
                    TextureDefinition def = textures[i];

                    //An id already seeded would claim a second slot and orphan the first, so the
                    //duplicate is dropped rather than added. Textures is keyed by id, so this only
                    //fires if that ever stops being true.
                    if (_textureTileSlots.ContainsKey(def.id))
                        continue;

                    Bitmap placeholder = SolidTextureThumbnail(TextureManager.RepresentativeRgb(def));
                    _textureTileSlots[def.id] = _textureImageList.Images.Count;
                    _textureImageList.Images.Add(def.id.ToString(), placeholder);
                    _texturePlaceholders[def.id] = placeholder;
                }

                if (end < textures.Count) {
                    BeginInvoke(new Action(() => SeedFrom(end)));
                    return;
                }

                TextureListView.SetObjects(textures);
                TextureLoadingLabel.Text = $"Rendering {textures.Count} textures";
                onSeeded();
            }

            SeedFrom(0);
        }

        /// <summary>
        ///     Swaps a batch of finished tiles into the grid.
        /// </summary>
        /// <remarks>
        ///     <para>
        ///     Nothing here rebuilds a row or grows the list, which is what makes populating the
        ///     grid as the renders land affordable at all. Measured on this machine over 1408
        ///     textures: <c>AddObjects</c> in batches costs 41s of UI-thread time and stalls the
        ///     message loop for up to 4.3s at a time, and <c>RefreshObjects</c> alone is 4.3ms per
        ///     row because it finds a row by scanning for its model object. Replacing the ImageList
        ///     entry in place leaves the row's stored image index correct, so a plain
        ///     <c>Invalidate</c> is all that is needed and the whole sweep costs 386ms.
        ///     </para>
        ///     <para>
        ///     <c>Invalidate</c> rather than <c>RedrawItems</c> deliberately: it needs no id-to-row
        ///     map, so it cannot be wrong after a column sort reorders the rows, and it measured
        ///     0.05ms either way.
        ///     </para>
        /// </remarks>
        private void ApplyTextureTiles(TextureTileBatch batch) {
            foreach ((int id, Bitmap tile) in batch.Tiles) {
                if (!_textureTileSlots.TryGetValue(id, out int slot)) {
                    //No slot means this texture was not in the snapshot the grid was seeded from,
                    //so there is nothing to draw it in and the bitmap is ours to release.
                    tile.Dispose();
                    continue;
                }

                _textureImageList.Images[slot] = tile;

                //Released only now, and only for the id actually displaced. Disposing the whole set
                //at the end would free the tile still on screen for any texture whose thumbnail
                //failed to build.
                if (_texturePlaceholders.Remove(id, out Bitmap? placeholder))
                    placeholder.Dispose();
            }

            TextureListView.Invalidate();
        }

        /// <summary>
        /// Renders every texture graph and builds its list thumbnail, entirely off the UI thread,
        /// publishing tiles to the grid in batches as they finish.
        /// </summary>
        /// <remarks>
        /// Nothing here may touch a control or the ImageList: an ImageList realises a native
        /// handle on first use and is not thread safe, and the list view is a control. Creating
        /// the bitmaps here is safe because each one is unattached and owned by this thread until
        /// <see cref="ApplyTextureTiles"/> hands it over.
        /// </remarks>
        /// <param name="bgw">The worker driving this pass, used for progress and cancellation.</param>
        /// <param name="textures">A snapshot taken on the UI thread, so the static dictionary is never enumerated off-thread.</param>
        /// <param name="args">The <see cref="DoWorkEventArgs"/> to flag when cancellation wins the race.</param>
        /// <returns>How many tiles were published.</returns>
        private static int RenderTextureThumbnails(BackgroundWorker bgw, List<TextureDefinition> textures, DoWorkEventArgs args) {
            int total = textures.Count;
            if (total == 0)
                return 0;

            int percentile = Math.Max(1, total / 100);
            int done = 0;
            int failed = 0;
            int published = 0;
            int solidFallbacks = 0;

            //Guards the batch alone. The render itself is what the parallelism is for, so the lock
            //is only ever held for a list append and the occasional handover.
            object gate = new object();
            var pending = new List<(int Id, Bitmap Tile)>(TextureTileBatchSize);
            Stopwatch sinceFlush = Stopwatch.StartNew();

            void Flush(bool force) {
                List<(int Id, Bitmap Tile)> ready;
                lock (gate) {
                    if (pending.Count == 0)
                        return;
                    if (!force && pending.Count < TextureTileBatchSize && sinceFlush.ElapsedMilliseconds < TextureTileBatchIntervalMs)
                        return;

                    ready = new List<(int, Bitmap)>(pending);
                    pending.Clear();
                    sinceFlush.Restart();
                }

                System.Threading.Interlocked.Add(ref published, ready.Count);

                //ReportProgress posts rather than sends, so the render never waits on the UI thread.
                //The percentage is ignored for a batch payload; it is passed so a handler that only
                //looks at the number still sees a sane one.
                bgw.ReportProgress(Math.Clamp(done * 100 / total, 0, 100), new TextureTileBatch(ready));
            }

            Debug($"LoadTextures: rendering {total} texture graphs across {Environment.ProcessorCount} threads", LOG_DETAIL.BASIC);
            Stopwatch sw = Stopwatch.StartNew();

            //Environment.ProcessorCount rather than the previous 20: 20 was picked for a body that
            //blocked half its threads, and once the body no longer blocks, oversubscribing purely
            //CPU-bound work only costs context switches.
            var options = new System.Threading.Tasks.ParallelOptions { MaxDegreeOfParallelism = Environment.ProcessorCount };

            System.Threading.Tasks.Parallel.ForEach(textures, options, (tex, state) => {
                if (bgw.CancellationPending) {
                    state.Stop();
                    return;
                }

                try {
                    //Called directly rather than through Task.Run with a 15 second Wait.
                    //Parallel.ForEach already runs this body on a pool thread, so that shape
                    //needed two pool threads per texture and blocked one of them in Wait. Against
                    //a pool that starts at ProcessorCount and injects further threads slowly, the
                    //renders queued behind the blocked waiters could not start, so "timed out" was
                    //a starvation report rather than a slow texture. Wait also cancelled nothing:
                    //a timed-out render kept its thread and could still assign def.thumb after the
                    //loop had moved on.
                    TextureManager.EnsureRendered(tex);
                } catch (Exception ex) {
                    System.Threading.Interlocked.Increment(ref failed);
                    Debug($"Error rendering texture {tex.id}: {ex.GetType().Name}: {ex.Message}", LOG_DETAIL.BASIC);
                }

                //Built here rather than in a second pass over the whole list. GDI+ drawing into an
                //unattached bitmap with a single owner is safe off the UI thread, and building it
                //next to the render is what lets the tile be published while the sweep runs.
                try {
                    //EnsureRendered leaves a thumb behind on every exit, so a null one means it
                    //could not run at all rather than that the texture has nothing to draw.
                    Bitmap? source = tex.thumb;
                    Bitmap tile;
                    if (source != null) {
                        tile = CreateThumbnail(source);
                    } else {
                        tile = SolidTextureThumbnail(TextureManager.RepresentativeRgb(tex));
                        System.Threading.Interlocked.Increment(ref solidFallbacks);
                    }

                    lock (gate)
                        pending.Add((tex.id, tile));
                } catch (Exception ex) {
                    //Skipped rather than substituted: the slot already holds this texture's
                    //representative colour from the seed, so the grid keeps a sensible tile.
                    Debug($"Error building thumbnail for texture {tex.id}: {ex.GetType().Name}: {ex.Message}", LOG_DETAIL.BASIC);
                }

                //Reported on 1% boundaries only. ReportProgress is safe from several threads, but
                //one post per texture from N threads floods the message pump and makes the UI less
                //responsive, which is the opposite of the point.
                int count = System.Threading.Interlocked.Increment(ref done);
                if (count % percentile == 0 || count == total)
                    bgw.ReportProgress(count * 100 / total, $"Rendering {count}/{total} ({count * 100 / total}%)");

                Flush(false);
            });

            sw.Stop();
            Debug($"LoadTextures: rendered {done}/{total} in {sw.ElapsedMilliseconds}ms, {failed} errors", LOG_DETAIL.BASIC);

            if (bgw.CancellationPending) {
                args.Cancel = true;

                //Whatever the last batch collected is never going to be published, and the UI thread
                //never took ownership of it.
                lock (gate) {
                    foreach ((int Id, Bitmap Tile) entry in pending)
                        entry.Tile.Dispose();
                    pending.Clear();
                }
                return published;
            }

            Flush(true);

            Debug($"LoadTextures: {published} tiles published, {solidFallbacks} from the material colour", LOG_DETAIL.BASIC);
            return published;
        }

        // ===================================================================
        //  The sprite grid: index 8 presented as pictures rather than rows
        // ===================================================================

        /// <summary>
        ///     Wires the sprite grid's columns, its tile size and the tree beneath it.
        /// </summary>
        /// <remarks>
        ///     <para>
        ///     Every column is an <c>AspectGetter</c> rather than an <c>AspectName</c>, because the
        ///     tree holds two row types. A set is a <c>SpriteDefinition</c> and its children are the
        ///     <c>RSBufferedImage</c> frames it rasterises, which derive from it - so a name like
        ///     <c>index</c> reads a frame's position in its set as though it were a sprite id, and
        ///     <c>width</c> reads a field a frame never populates. That relationship is what makes
        ///     the tree legal and is deliberately kept; only the presentation changed.
        ///     </para>
        ///     <para>
        ///     The tile side and every column width are measured from the grid's own font. A list
        ///     view's row height and column widths are pixel counts that the form's DPI scaling does
        ///     not touch, so a literal is right only at the DPI it was picked at - which is the
        ///     defect that left this page's rows 20 pixels tall with an 11.25pt font in them.
        ///     </para>
        /// </remarks>
        private void BindSpriteColumns() {
            if (_spriteColumnsBound)
                return;
            _spriteColumnsBound = true;

            //Four rows of text tall. Big enough that a 16x16 icon is magnified three times and a
            //400x200 banner still reads as a banner, small enough that the grid is a list.
            _spriteTileSide = Math.Clamp(SpriteListView.Font.Height * 4, 48, 96);
            _spriteMarkerFont = new Font(SpriteListView.Font.FontFamily, Math.Max(7f, _spriteTileSide / 6f),
                FontStyle.Regular, GraphicsUnit.Pixel);
            _spritePendingTile = SpritePainter.RenderTile(null, _spriteTileSide, SpriteTileContent.Pending, _spriteMarkerFont);

            SpriteListView.RowHeight = _spriteTileSide + 2;

            //One character's width in the grid's font, so the columns hold what they claim to at any
            //DPI. Measured over ten characters because MeasureText pads a single one.
            int cell = Math.Max(1, TextRenderer.MeasureText(new string('0', 10), SpriteListView.Font).Width / 10);

            //Wide enough for the tile and the cell padding around it. Stated rather than filled: a
            //filling column is the one ObjectListView narrows when the others do not leave it room,
            //and a narrowed picture column crops the picture.
            int[] widths = {
                cell * 9,  //ID, holding a four digit id and the tree glyph drawn beside it
                cell * 9,  //Frames
                cell * 10, //Canvas
                cell * 18, //Frame at
                cell * 15, //Stored, widened to whatever is left over, being the filling column
                cell * 6,  //Tile
                _spriteTileSide + cell
            };

            SpriteIdColumn.Width = widths[0];
            SpriteFrameCountColumn.Width = widths[1];
            SpriteCanvasColumn.Width = widths[2];
            SpritePlacementColumn.Width = widths[3];
            SpriteStoredColumn.Width = widths[4];
            SpriteScaleColumn.Width = widths[5];
            SpriteImageColumn.Width = widths[6];

            /* What the grid needs to show every column in full: its own columns, plus the tree indent
               and the vertical scrollbar, neither of which is a column. The splitter is placed from
               this rather than from a fraction of the page, because a fraction that suited one window
               left the filling column too narrow to hold "column-major" in another.
               Summed from what was assigned rather than read back off the columns: a space-filling
               column answers with the width ObjectListView has currently given it, which before the
               grid has been laid out is zero - so reading them back measured the page 105 pixels
               short and put the splitter exactly one column too far left. */
            _spriteGridWidth = cell * 4 + SystemInformation.VerticalScrollBarWidth;
            foreach (int width in widths)
                _spriteGridWidth += width;

            //Measured from the same character width, so the strip holds its widest button caption at
            //any DPI without either clipping it or reserving a third of the page for it.
            groupBox3.Width = cell * 30;

            /* Every getter answers an empty cell for a null row. ObjectListView evaluates aspects
               for rows being recycled during a scroll and for cells measured before a model is
               attached. A row of the wrong type still throws, because that can only mean the columns
               were wired to something this tab does not produce. */
            SpriteIdColumn.AspectGetter = row => row == null ? null : (object) ((SpriteDefinition) row).index;

            SpriteFrameCountColumn.AspectGetter = row => {
                if (row == null)
                    return string.Empty;
                if (row is RSBufferedImage frame)
                    return "frame " + frame.index;
                SpriteDefinition set = (SpriteDefinition) row;
                return SpriteStatusOf(set).Content == SpriteTileContent.Pending
                    ? string.Empty
                    : set.GetFrameCount().ToString();
            };

            SpriteCanvasColumn.AspectGetter = row => {
                if (row == null)
                    return string.Empty;
                if (row is RSBufferedImage frame)
                    return frame.GetWidth() + "x" + frame.GetHeight();
                SpriteDefinition set = (SpriteDefinition) row;
                return SpriteStatusOf(set).Content == SpriteTileContent.Pending
                    ? string.Empty
                    : set.width + "x" + set.height;
            };

            SpritePlacementColumn.AspectGetter = row => {
                if (row == null)
                    return string.Empty;
                SpriteFrame? frame = SpriteFrameBehind(row);
                return frame == null
                    ? string.Empty
                    : frame.SubWidth + "x" + frame.SubHeight + " at " + frame.OffsetX + "," + frame.OffsetY;
            };

            SpriteStoredColumn.AspectGetter = row => {
                if (row == null)
                    return string.Empty;
                if (row is not RSBufferedImage) {
                    SpriteRowStatus status = SpriteStatusOf((SpriteDefinition) row);
                    if (status.Failure != null)
                        return status.Failure;
                    if (status.Content == SpriteTileContent.Pending)
                        return string.Empty;
                }
                return DescribeSpriteStorage(row);
            };

            SpriteScaleColumn.AspectGetter = row => {
                if (row == null)
                    return string.Empty;
                if (row is not RSBufferedImage)
                    return SpriteStatusOf((SpriteDefinition) row).Scale;

                SpriteFrame? stored = SpriteFrameBehind(row);
                if (stored == null || stored.Area == 0)
                    return "-";
                var frame = (RSBufferedImage) row;
                return SpriteTileFit.Fit(frame.GetWidth(), frame.GetHeight(), _spriteTileSide, _spriteTileSide).ToString();
            };

            SpriteImageColumn.AspectGetter = row => null; //The picture is the cell; there is no text under it
            SpriteImageColumn.ImageGetter = row => {
                if (row == null)
                    return null;
                return row is RSBufferedImage frame
                    ? SpriteFrameTileFor(frame)
                    : SpriteTileFor((SpriteDefinition) row);
            };

            SpriteListView.CanExpandGetter = row =>
                row is SpriteDefinition set && row is not RSBufferedImage && set.GetFrameCount() > 1;
            SpriteListView.ChildrenGetter = row => SpriteFrameRows((SpriteDefinition) row);

            SpritePreview.Zoom = (int) SpriteZoom.Value;
            SpritePreview.OutlineFrame = SpriteFrameOutline.Checked;

            //An AutoSize label docked to an edge grows sideways and is clipped by its container: it
            //wraps only once its MaximumSize states a width. Re-measured on resize, because that
            //width is the page's and the page follows the form.
            SpriteEditorTab.SizeChanged += (_, _) => {
                WrapSpriteNotice();
                PlaceSpriteSplitter();
            };
            WrapSpriteNotice();

            /* Re-placed on every resize rather than once. SplitterMoving is raised only for a drag,
               so the moment the user states a preference the measurement stops overriding it. */
            SpriteSplit.SizeChanged += (_, _) => PlaceSpriteSplitter();
            SpriteSplit.SplitterMoving += (_, _) => _spriteSplitMovedByHand = true;
        }

        /// <summary>Gives the sprite notice a width to wrap against.</summary>
        private void WrapSpriteNotice() {
            int width = SpriteEditorTab.ClientSize.Width;
            //Written only when it changes: assigning a maximum size lays the page out again, and a
            //layout is what raises the event this is called from.
            if (width > 0 && SpriteNoticeLabel.MaximumSize.Width != width)
                SpriteNoticeLabel.MaximumSize = new Size(width, 0);
        }

        /// <summary>
        ///     Divides the sprite page between the list and the preview, once it has a width worth
        ///     dividing.
        /// </summary>
        /// <remarks>
        ///     A <see cref="SplitContainer"/> defaults to a splitter distance of 50 <i>pixels</i>
        ///     rather than half, so the distance has to be set - and setting it in the designer would
        ///     make it one more literal the form's DPI scaling multiplies.
        /// </remarks>
        private void PlaceSpriteSplitter() {
            if (_spriteSplitMovedByHand || SpriteSplit.Width < 400)
                return;

            try {
                //As much as the grid's own columns need, and never more than three quarters of the
                //page - the preview is what an edit is judged against and cannot be squeezed out of
                //existence, and it scrolls, so it does not need the width of the largest sprite.
                SpriteSplit.SplitterDistance = Math.Clamp(_spriteGridWidth,
                    SpriteSplit.Panel1MinSize, Math.Max(SpriteSplit.Panel1MinSize, SpriteSplit.Width * 3 / 4));
            }
            catch (InvalidOperationException ex) {
                //A distance the panels' minimum sizes will not allow at the current width. Left
                //where it is; the next resize tries again.
                Debug("Sprite splitter not placed yet: " + ex.Message, LOG_DETAIL.ADVANCED);
            }
        }

        /// <summary>
        ///     Binds one row per declared sprite group before a single group has been read.
        /// </summary>
        /// <remarks>
        ///     The tab used to bind nothing until the whole index had been decoded, so it showed an
        ///     empty grid for the length of the load and read as broken. Every group id is known from
        ///     the reference table at no cost, so the complete list is available immediately and the
        ///     load only fills it in.
        ///     <para>
        ///     The rows are empty <c>SpriteDefinition</c> instances that the load decodes into, which
        ///     is what lets a finished set replace a placeholder without adding a row: adding
        ///     reorders and rebuilds the tree, replacing does not.
        ///     </para>
        /// </remarks>
        /// <param name="addresses">One (group, file) pair per sprite set, in table order.</param>
        private void SeedSpriteGrid(List<(int Group, int File)> addresses) {
            ReleaseSpriteTiles();
            _spriteRows.Clear();
            _spriteRowStatus.Clear();

            var rows = new List<SpriteDefinition>(addresses.Count);
            foreach ((int group, int _) in addresses) {
                var row = new SpriteDefinition();
                row.SetIndex(group);
                _spriteRows[group] = row;
                _spriteRowStatus[group] = SpriteRowStatus.Pending;
                rows.Add(row);
            }

            SpriteListView.SetObjects(rows);
            ShowSelectedSprite();

            SpriteProgressBar.Value = 0;
            SpriteLoadingLabel.Text = "Reading " + rows.Count + " sprite sets";
        }

        /// <summary>
        ///     Fills in a batch of decoded sets and swaps their tiles into the grid.
        /// </summary>
        /// <remarks>
        ///     <para>
        ///     Each set is decoded twice on purpose, once on the worker and once here. The worker's
        ///     decode is what its tile is rasterised from; this one fills the row the grid is already
        ///     holding. The alternatives are worse: rasterising on the UI thread puts the expensive
        ///     half of the load back on the message loop, and writing into a bound row from the
        ///     worker is a control-visible object being mutated off the UI thread. A sprite decode is
        ///     a walk over the stored bytes with no rasterising in it, and index 8 is under fifteen
        ///     megabytes decompressed, so the second pass is paid in ones of milliseconds per batch.
        ///     </para>
        ///     <para>
        ///     Nothing here rebuilds a row or grows the list. The tile is swapped in the dictionary
        ///     the image getter reads, so a plain <c>Invalidate</c> is all the grid needs.
        ///     </para>
        /// </remarks>
        /// <param name="batch">The sets the worker has finished with.</param>
        private void ApplySpriteSets(SpriteSetBatch batch) {
            bool selectionFilled = false;

            foreach (SpriteSetResult result in batch.Sets) {
                if (!_spriteRows.TryGetValue(result.GroupId, out SpriteDefinition? row)) {
                    //No row means this group was not in the list the grid was seeded from, so there
                    //is nothing to draw it in and the tile is ours to release.
                    result.Tile?.Dispose();
                    continue;
                }

                SpriteRowStatus status = result.Status;

                if (result.Stored != null) {
                    try {
                        row.Decode(new JagStream(result.Stored));
                    }
                    catch (Exception ex) {
                        //The worker decoded these same bytes, so this can only be a defect rather
                        //than a bad file - but it must cost the row and not the load.
                        status = SpriteRowStatus.Failed(ex.Message);
                        Debug("Sprite " + result.GroupId + " decoded on the worker and not on the UI thread: " + ex);
                    }
                }

                if (_spriteTiles.Remove(result.GroupId, out Bitmap? previous))
                    previous.Dispose();
                if (result.Tile != null)
                    _spriteTiles[result.GroupId] = result.Tile;

                _spriteRowStatus[result.GroupId] = status;

                //The pane is bound to a row, not to a batch, so a set that arrives while it is
                //selected has to redraw it - otherwise it keeps saying the row has not been read.
                object? selected = SpriteListView.SelectedObject;
                if (selected != null && ReferenceEquals(SpriteSetBehind(selected), row))
                    selectionFilled = true;
            }

            SpriteListView.Invalidate();

            if (selectionFilled)
                ShowSelectedSprite();
        }

        /// <summary>
        ///     Reads and rasterises every sprite set, entirely off the UI thread, publishing them in
        ///     batches as they finish.
        /// </summary>
        /// <remarks>
        ///     Nothing here touches a control. The bitmaps are unattached and owned by this thread
        ///     until <see cref="ApplySpriteSets"/> takes them, and the definitions decoded here are
        ///     thrown away once their tile exists - the rows the grid holds are decoded on the UI
        ///     thread from the same bytes.
        ///     <para>
        ///     Sequential rather than parallel, unlike the texture sweep. The work here is dominated
        ///     by reading and inflating containers, which every thread would take the cache's own
        ///     lock to do.
        ///     </para>
        /// </remarks>
        /// <param name="bgw">The worker driving this pass, for progress and cancellation.</param>
        /// <param name="open">The cache to read from, passed rather than read off the field so a reload cannot swap it mid-sweep.</param>
        /// <param name="addresses">One (group, file) pair per sprite set.</param>
        /// <param name="tileSide">The tile side in pixels, measured on the UI thread.</param>
        /// <param name="markerFont">The font for the empty and failed markers.</param>
        /// <param name="args">The <see cref="DoWorkEventArgs"/> to flag when cancellation wins the race.</param>
        /// <returns>What the sweep found.</returns>
        private static SpriteLoadOutcome ReadSpriteSets(BackgroundWorker bgw, RSCache open,
            List<(int Group, int File)> addresses, int tileSide, Font markerFont, DoWorkEventArgs args) {
            var outcome = new SpriteLoadOutcome();
            int total = addresses.Count;
            if (total == 0)
                return outcome;

            int percentile = Math.Max(1, total / 100);
            int done = 0;
            var pending = new List<SpriteSetResult>(SpriteBatchSize);
            Stopwatch sinceFlush = Stopwatch.StartNew();

            void Flush(bool force) {
                if (pending.Count == 0)
                    return;
                if (!force && pending.Count < SpriteBatchSize && sinceFlush.ElapsedMilliseconds < SpriteBatchIntervalMs)
                    return;

                var ready = new List<SpriteSetResult>(pending);
                pending.Clear();
                sinceFlush.Restart();

                //ReportProgress posts rather than sends, so the sweep never waits on the UI thread.
                //The percentage is ignored for a batch payload; it is passed so a handler that only
                //looks at the number still sees a sane one.
                bgw.ReportProgress(Math.Clamp(done * 100 / total, 0, 100), new SpriteSetBatch(ready));
            }

            foreach ((int group, int file) in addresses) {
                if (bgw.CancellationPending) {
                    args.Cancel = true;

                    //Whatever the last batch collected is never going to be published, and the UI
                    //thread never took ownership of it.
                    foreach (SpriteSetResult abandoned in pending)
                        abandoned.Tile?.Dispose();
                    pending.Clear();
                    return outcome;
                }

                pending.Add(ReadOneSpriteSet(open, group, file, tileSide, markerFont, outcome));
                done++;

                //Reported on one-percent boundaries only. ReportProgress marshals to the UI thread
                //on every call, so one post per set would be 4,593 of them.
                if (done % percentile == 0 || done == total)
                    bgw.ReportProgress(done * 100 / total,
                        "Read " + done + "/" + total + " sprite sets (" + done * 100 / total + "%)");

                Flush(false);
            }

            Flush(true);
            return outcome;
        }

        /// <summary>Reads one sprite set and builds the tile the grid will show for it.</summary>
        /// <param name="open">The cache to read from.</param>
        /// <param name="group">The group id, which is the sprite id on index 8.</param>
        /// <param name="file">The file id the reference table declares for that group.</param>
        /// <param name="tileSide">The tile side in pixels.</param>
        /// <param name="markerFont">The font for the empty and failed markers.</param>
        /// <param name="outcome">The running tally, which this adds one set to.</param>
        /// <returns>The finished row, tile included.</returns>
        private static SpriteSetResult ReadOneSpriteSet(RSCache open, int group, int file, int tileSide,
            Font markerFont, SpriteLoadOutcome outcome) {
            var probe = new SpriteDefinition();
            Bitmap? picture = null;

            try {
                byte[] stored = open.ReadFileBytes(RSConstants.SPRITES_INDEX, group, file);
                probe.Decode(new JagStream(stored));

                /* The tile shows frame 0, so the tile's state is frame 0's. A frame with a zero-area
                   plane reads as empty rather than as a failed draw - 2,377 of the vanilla capture's
                   11,177 frames store one, and they are legitimate records. Note that such a frame
                   can still have a positive canvas, so this cannot be decided from the canvas. */
                SpriteTileContent content = SpritePainter.ContentOf(probe, 0);
                if (content == SpriteTileContent.Picture) {
                    picture = SpritePainter.ToDisplayBitmap(probe.GetFrame(0));
                    if (picture == null)
                        content = SpriteTileContent.Empty;
                }

                Bitmap tile = SpritePainter.RenderTile(picture, tileSide, content, markerFont);
                string scale = picture == null
                    ? "-"
                    : SpriteTileFit.Fit(picture.Width, picture.Height, tileSide, tileSide).ToString();

                outcome.Loaded++;
                if (content != SpriteTileContent.Picture)
                    outcome.Empty++;

                return new SpriteSetResult(group, stored, tile, new SpriteRowStatus(content, scale, null));
            }
            catch (Exception ex) {
                outcome.Failed++;
                Debug("Sprite group " + group + " could not be read: " + ex.GetType().Name + ": " + ex.Message);
                return new SpriteSetResult(group, null,
                    SpritePainter.RenderTile(null, tileSide, SpriteTileContent.Failed, markerFont),
                    SpriteRowStatus.Failed(ex.Message));
            }
            finally {
                picture?.Dispose();

                //The rendered frames are a pinned pixel buffer and a GDI bitmap each, and the tile
                //has already taken the only copy that is wanted.
                probe.Dispose();
            }
        }

        /// <summary>The frames of one set, recorded so a frame row can name where it came from.</summary>
        /// <param name="set">The set being expanded.</param>
        /// <returns>The rendered frames, or nothing when the set cannot be rasterised.</returns>
        private IEnumerable<object> SpriteFrameRows(SpriteDefinition set) {
            if (!SpritePainter.CanRasterise(set))
                return Array.Empty<object>();

            List<RSBufferedImage>? frames;
            try {
                frames = set.GetFrames();
            }
            catch (Exception ex) {
                //Costs the branch rather than the tab: expanding a row must not take the form down.
                Debug("Sprite " + set.index + " could not be rasterised: " + ex.Message);
                return Array.Empty<object>();
            }

            if (frames == null)
                return Array.Empty<object>();

            var rows = new List<object>(frames.Count);
            for (int id = 0; id < frames.Count; id++) {
                _spriteFrameOwners[frames[id]] = (set, id);
                rows.Add(frames[id]);
            }

            return rows;
        }

        /// <summary>The tile for a sprite set, which is the placeholder until its load lands.</summary>
        /// <param name="set">The set row.</param>
        /// <returns>The tile.</returns>
        private Bitmap? SpriteTileFor(SpriteDefinition set) {
            return _spriteTiles.TryGetValue(set.index, out Bitmap? tile) ? tile : _spritePendingTile;
        }

        /// <summary>
        ///     The tile for one expanded frame row, built the first time the row is drawn.
        /// </summary>
        /// <remarks>
        ///     On demand rather than during the load: only 44 of the vanilla capture's 4,593 sets
        ///     hold more than one frame, so building every frame's tile up front would be 11,177
        ///     tiles rendered to show 4,593 rows. This runs on the UI thread, which is affordable
        ///     only because of that.
        /// </remarks>
        /// <param name="frame">The frame row.</param>
        /// <returns>The tile.</returns>
        private Bitmap? SpriteFrameTileFor(RSBufferedImage frame) {
            if (!_spriteFrameOwners.TryGetValue(frame, out (SpriteDefinition Set, int Frame) owner))
                return _spritePendingTile;

            (int, int) key = (owner.Set.index, owner.Frame);
            if (_spriteFrameTiles.TryGetValue(key, out Bitmap? cached))
                return cached;

            Bitmap? picture = SpritePainter.ContentOf(owner.Set, owner.Frame) == SpriteTileContent.Picture
                ? SpritePainter.ToDisplayBitmap(frame)
                : null;
            Bitmap tile = SpritePainter.RenderTile(picture, _spriteTileSide,
                picture == null ? SpriteTileContent.Empty : SpriteTileContent.Picture, _spriteMarkerFont!);
            picture?.Dispose();

            _spriteFrameTiles[key] = tile;
            return tile;
        }

        /// <summary>The stored frame a row describes, which for a set is its first.</summary>
        /// <param name="row">A set row or a frame row.</param>
        /// <returns>The stored frame, or null when the row has none.</returns>
        private SpriteFrame? SpriteFrameBehind(object row) {
            if (row is RSBufferedImage frame) {
                if (!_spriteFrameOwners.TryGetValue(frame, out (SpriteDefinition Set, int Frame) owner))
                    return null;
                List<SpriteFrame>? stored = owner.Set.Frames;
                return stored != null && owner.Frame < stored.Count ? stored[owner.Frame] : null;
            }

            List<SpriteFrame>? frames = ((SpriteDefinition) row).Frames;
            return frames != null && frames.Count > 0 ? frames[0] : null;
        }

        /// <summary>The set a row belongs to, which for a frame row is the set it was expanded from.</summary>
        /// <param name="row">A set row or a frame row.</param>
        /// <returns>The set, or null when a frame row's owner is no longer known.</returns>
        private SpriteDefinition? SpriteSetBehind(object row) {
            if (row is not RSBufferedImage frame)
                return (SpriteDefinition) row;

            return _spriteFrameOwners.TryGetValue(frame, out (SpriteDefinition Set, int Frame) owner) ? owner.Set : null;
        }

        /// <summary>What a row's stored frame carries beyond its pixels.</summary>
        /// <remarks>
        ///     Every one of these is a stored choice that the drawn picture cannot express: an alpha
        ///     plane that leaves everything opaque draws like no plane at all, the traversal flag is
        ///     unrecoverable on a frame one pixel wide, and a frame reaching outside its canvas is
        ///     something the client would refuse to draw at all.
        /// </remarks>
        /// <param name="row">A set row or a frame row.</param>
        /// <returns>The summary, or a dash when there is nothing to say.</returns>
        private string DescribeSpriteStorage(object row) {
            SpriteFrame? frame = SpriteFrameBehind(row);
            SpriteDefinition? owner = SpriteSetBehind(row);
            return frame == null || owner == null ? "-" : DescribeSpriteStorage(owner, frame);
        }

        /// <summary>What one stored frame carries beyond its pixels.</summary>
        /// <param name="owner">The set the frame belongs to, which owns the palette.</param>
        /// <param name="frame">The stored frame.</param>
        /// <returns>The summary.</returns>
        private static string DescribeSpriteStorage(SpriteDefinition owner, SpriteFrame frame) {
            /* The palette size is deliberately not here. It belongs to the set rather than to the
               frame, the detail pane states it for whatever is selected, and putting it in front of
               the three flags cost the column the width it needed to show them - "pal 28, ..." on
               every row said less than "column-major" on the rows that have it. */
            var parts = new List<string>();
            if (frame.HasAlphaPlane)
                parts.Add(frame.AlphaPlaneIsRedundant ? "alpha (opaque)" : "alpha");
            if (frame.IsColumnMajor)
                parts.Add("column-major");
            if (owner.Overflows(frame))
                parts.Add("overflows canvas");

            return parts.Count == 0 ? "-" : string.Join(", ", parts);
        }

        /// <summary>What the grid knows about one sprite row.</summary>
        /// <param name="set">The set row.</param>
        /// <returns>Its status, which is pending for a row no load has reached.</returns>
        private SpriteRowStatus SpriteStatusOf(SpriteDefinition set) {
            return _spriteRowStatus.TryGetValue(set.index, out SpriteRowStatus? status) ? status : SpriteRowStatus.Pending;
        }

        /// <summary>Shows the selected sprite in the detail pane.</summary>
        /// <param name="sender">The grid.</param>
        /// <param name="e">The event data.</param>
        private void SpriteListView_SelectedIndexChanged(object sender, EventArgs e) {
            ShowSelectedSprite();
        }

        /// <summary>
        ///     Draws the selected row at 1:1 in the detail pane and describes what it is.
        /// </summary>
        /// <remarks>
        ///     The pane shows the frame on its canvas rather than cropped to its own pixels: the
        ///     offset a frame is placed at is a stored field, and cropping to the sub-rectangle hides
        ///     it. The dashed outline is where the frame's own pixels are.
        /// </remarks>
        private void ShowSelectedSprite() {
            //Cleared first, so a failure below leaves an empty pane rather than the previous sprite.
            SpritePreview.ShowFrame(null, Rectangle.Empty);
            _spriteDetailPicture?.Dispose();
            _spriteDetailPicture = null;

            object? row = SpriteListView.SelectedObject;
            if (row == null) {
                SpritePreview.EmptyText = "No sprite selected";
                SpriteDetailLabel.Text = "Select a sprite set to see its frames at full size.";
                return;
            }

            SpriteDefinition set;
            int frameId;
            if (row is RSBufferedImage frameRow) {
                if (!_spriteFrameOwners.TryGetValue(frameRow, out (SpriteDefinition Set, int Frame) owner)) {
                    SpritePreview.EmptyText = "That frame's set is no longer loaded";
                    SpriteDetailLabel.Text = string.Empty;
                    return;
                }
                set = owner.Set;
                frameId = owner.Frame;
            } else {
                set = (SpriteDefinition) row;
                frameId = 0;
            }

            SpriteRowStatus status = SpriteStatusOf(set);
            if (status.Failure != null) {
                SpritePreview.EmptyText = "This set would not decode";
                SpriteDetailLabel.Text = "Sprite " + set.index + " could not be decoded: " + status.Failure;
                return;
            }

            if (status.Content == SpriteTileContent.Pending) {
                SpritePreview.EmptyText = "Not read yet";
                SpriteDetailLabel.Text = "Sprite " + set.index + " has not been read yet.";
                return;
            }

            SpriteFrame? frame = set.Frames != null && frameId < set.Frames.Count ? set.Frames[frameId] : null;
            if (frame == null) {
                SpritePreview.EmptyText = "This set stores no frames";
                SpriteDetailLabel.Text = "Sprite " + set.index + " stores no frames at all.";
                return;
            }

            SpriteDetailLabel.Text = DescribeSelectedSprite(set, frame, frameId);

            if (frame.Area == 0) {
                SpritePreview.EmptyText = "This frame stores no pixels";
                return;
            }

            if (!SpritePainter.CanRasterise(set)) {
                SpritePreview.EmptyText = "This set cannot be drawn: its canvas is empty";
                return;
            }

            try {
                _spriteDetailPicture = SpritePainter.ToDisplayBitmap(set.GetFrame(frameId));
            }
            catch (Exception ex) {
                //Costs the preview rather than the tab
                Debug("Sprite " + set.index + " frame " + frameId + " could not be drawn: " + ex.Message);
                SpritePreview.EmptyText = "This frame could not be drawn";
                return;
            }

            SpritePreview.ShowFrame(_spriteDetailPicture,
                new Rectangle(frame.OffsetX, frame.OffsetY, frame.SubWidth, frame.SubHeight));
        }

        /// <summary>The sentence above the detail pane.</summary>
        /// <remarks>
        ///     It ends by saying what this pane is not. The colour rule is the client's - palette
        ///     entry 0 transparent, an alpha plane blended - but nothing above it is: the client
        ///     draws a sprite through whatever the interface asked for, and a user comparing this
        ///     against the game has no other way to tell a documented difference from a defect.
        /// </remarks>
        /// <param name="set">The selected set.</param>
        /// <param name="frame">The frame on show.</param>
        /// <param name="frameId">Its position in the set.</param>
        /// <returns>The description.</returns>
        private string DescribeSelectedSprite(SpriteDefinition set, SpriteFrame frame, int frameId) {
            string placement = frame.SubWidth + "x" + frame.SubHeight + " at " + frame.OffsetX + "," + frame.OffsetY;
            string canvas = set.width + "x" + set.height;

            return "Sprite " + set.index + ", frame " + frameId + " of " + set.GetFrameCount() +
                   ". Canvas " + canvas + ", frame " + placement + ", " + DescribeSpriteStorage(set, frame) +
                   ", palette " + Math.Max(0, set.PaletteStored.Length - 1) + ", " +
                   set.StoredLength + " bytes stored." + Environment.NewLine +
                   "Drawn at " + SpritePreview.Zoom + ":1 with the client's colour rule - palette entry 0 is " +
                   "transparent and an alpha plane is blended - over a checkerboard, so a fully transparent " +
                   "sprite is checkerboard rather than a blank box. This is not the client's renderer: it " +
                   "applies none of the tinting, team colour or transparency an interface can ask for.";
        }

        /// <summary>
        ///     Rebuilds one row's tile from the definition it now holds.
        /// </summary>
        /// <remarks>
        ///     For after an import: the row is decoded in place, and everything the grid draws for it
        ///     comes from a tile and a status recorded when the set was first read. Both are rebuilt
        ///     here rather than left to a repaint, which would show the previous file's picture over
        ///     the new file's bytes.
        /// </remarks>
        /// <param name="set">The row whose stored form has just changed.</param>
        private void RedrawSpriteRow(SpriteDefinition set) {
            //Any expanded frame rows belong to the bytes that were there before this.
            var stale = new List<(int Set, int Frame)>();
            foreach ((int Set, int Frame) key in _spriteFrameTiles.Keys)
                if (key.Set == set.index)
                    stale.Add(key);
            foreach ((int Set, int Frame) key in stale) {
                _spriteFrameTiles[key].Dispose();
                _spriteFrameTiles.Remove(key);
            }

            SpriteTileContent content = SpritePainter.ContentOf(set, 0);
            Bitmap? picture = null;

            try {
                if (content == SpriteTileContent.Picture) {
                    picture = SpritePainter.ToDisplayBitmap(set.GetFrame(0));
                    if (picture == null)
                        content = SpriteTileContent.Empty;
                }

                if (_spriteTiles.Remove(set.index, out Bitmap? previous))
                    previous.Dispose();

                _spriteTiles[set.index] = SpritePainter.RenderTile(picture, _spriteTileSide, content, _spriteMarkerFont!);
                _spriteRowStatus[set.index] = new SpriteRowStatus(content,
                    picture == null ? "-" : SpriteTileFit.Fit(picture.Width, picture.Height, _spriteTileSide, _spriteTileSide).ToString(),
                    null);
            }
            finally {
                picture?.Dispose();
            }

            ShowSelectedSprite();
        }

        /// <summary>Releases every bitmap the sprite grid and its preview are holding.</summary>
        private void ReleaseSpriteTiles() {
            SpritePreview.ShowFrame(null, Rectangle.Empty);
            _spriteDetailPicture?.Dispose();
            _spriteDetailPicture = null;

            foreach (Bitmap tile in _spriteTiles.Values)
                tile.Dispose();
            _spriteTiles.Clear();

            foreach (Bitmap tile in _spriteFrameTiles.Values)
                tile.Dispose();
            _spriteFrameTiles.Clear();

            _spriteFrameOwners.Clear();
        }

        /// <summary>What the sprite grid knows about one row beyond the definition itself.</summary>
        private sealed class SpriteRowStatus {
            internal SpriteRowStatus(SpriteTileContent content, string scale, string? failure) {
                Content = content;
                Scale = scale;
                Failure = failure;
            }

            /// <summary>The row every seeded set starts as.</summary>
            internal static SpriteRowStatus Pending { get; } =
                new SpriteRowStatus(SpriteTileContent.Pending, string.Empty, null);

            /// <summary>A row whose group would not read or would not decode.</summary>
            /// <param name="reason">What went wrong, shown in the grid rather than only logged.</param>
            /// <returns>The status.</returns>
            internal static SpriteRowStatus Failed(string reason) {
                return new SpriteRowStatus(SpriteTileContent.Failed, "-", reason);
            }

            internal SpriteTileContent Content { get; }

            /// <summary>How the tile is scaled, as the tile itself was drawn.</summary>
            internal string Scale { get; }

            /// <summary>Why the row has no picture, or null when it has one.</summary>
            internal string? Failure { get; }
        }

        /// <summary>One decoded sprite set on its way from the loader to the grid.</summary>
        private sealed class SpriteSetResult {
            internal SpriteSetResult(int groupId, byte[]? stored, Bitmap? tile, SpriteRowStatus status) {
                GroupId = groupId;
                Stored = stored;
                Tile = tile;
                Status = status;
            }

            internal int GroupId { get; }

            /// <summary>The stored bytes, for the UI thread to decode into the bound row.</summary>
            /// <remarks>Null when the group could not be read, so there is nothing to decode.</remarks>
            internal byte[]? Stored { get; }

            /// <summary>The finished tile, owned by the grid from the moment it is applied.</summary>
            internal Bitmap? Tile { get; }

            internal SpriteRowStatus Status { get; }
        }

        /// <summary>
        ///     One instalment of decoded sprite sets, on its way from the loader to the grid.
        /// </summary>
        /// <remarks>
        ///     A type of its own rather than a bare list so the <c>ProgressChanged</c> handler can
        ///     tell a batch from the status string the same worker reports.
        /// </remarks>
        private sealed class SpriteSetBatch {
            internal SpriteSetBatch(List<SpriteSetResult> sets) {
                Sets = sets;
            }

            internal List<SpriteSetResult> Sets { get; }
        }

        /// <summary>What one sweep of index 8 found.</summary>
        /// <remarks>
        ///     Empty sets are counted apart from failures because they are not failures: a set whose
        ///     first frame stores no pixels is a legitimate record and thousands of frames in this
        ///     index are exactly that. Folding the two together would let a decoder regression hide
        ///     inside a number that already had a benign reason to be large.
        /// </remarks>
        private sealed class SpriteLoadOutcome {
            /// <summary>Sets that decoded.</summary>
            internal int Loaded { get; set; }

            /// <summary>Of those, the ones whose first frame stores no pixels.</summary>
            internal int Empty { get; set; }

            /// <summary>Groups that would not read or would not decode.</summary>
            internal int Failed { get; set; }

            /// <summary>The status line.</summary>
            /// <returns>The description.</returns>
            internal string Describe() {
                string text = Loaded.ToString("N0") + " sprite sets";
                if (Empty > 0)
                    text += ", " + Empty.ToString("N0") + " with no pixels in frame 0";
                if (Failed > 0)
                    text += ", " + Failed.ToString("N0") + " failed";
                return text;
            }
        }

        /// <summary>
        /// One instalment of finished tiles, on its way from the render worker to the grid.
        /// </summary>
        /// <remarks>
        /// A type of its own rather than a bare list so the <c>ProgressChanged</c> handler can tell
        /// a batch from the status string the same worker reports, and cannot mistake one for the
        /// other as the payloads change.
        /// </remarks>
        private sealed class TextureTileBatch {
            internal TextureTileBatch(List<(int Id, Bitmap Tile)> tiles) {
                Tiles = tiles;
            }

            /// <summary>Each texture id paired with the bitmap its ImageList slot should now hold.</summary>
            internal List<(int Id, Bitmap Tile)> Tiles { get; }
        }
    }
}