using BrightIdeasSoftware;
using FlashEditor.cache;
using FlashEditor.cache.sprites;
using FlashEditor.Definitions;
using FlashEditor.Definitions.Sprites;
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
        ///     kept in the same order as the tab strip - it was read as
        ///     <c>editorTypes[EditorTabControl.SelectedIndex]</c> - so inserting a page anywhere but
        ///     the end silently pointed every tab after it at the wrong index, and the only thing
        ///     standing between the editor and that was a comment. Keyed by the page object, a tab
        ///     can be moved, reordered or hidden and still name its own index.
        /// </remarks>
        private sealed class EditorTabBinding {
            internal EditorTabBinding(int indexId, Action<RSCache>? bind) {
                IndexId = indexId;
                Bind = bind;
            }

            /// <summary>The cache index this tab edits.</summary>
            internal int IndexId { get; }

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

        /// <summary>Every editor tab, keyed by the page itself so its position cannot matter.</summary>
        private readonly Dictionary<TabPage, EditorTabBinding> editorTabs = new Dictionary<TabPage, EditorTabBinding>();

        /// <summary>The tabs already populated for the cache currently open.</summary>
        private readonly HashSet<TabPage> loadedTabs = new HashSet<TabPage>();

        List<BackgroundWorker> workers = new List<BackgroundWorker>();
        public Editor() {
            InitializeComponent();
            RegisterEditorTabs();

            //Added here rather than in the designer so the generated file stays untouched
            ToolStripMenuItem saveAsItem = new ToolStripMenuItem("Save As...");
            saveAsItem.Click += saveAsToolStripMenuItem_Click;
            openToolStripMenuItem.DropDownItems.Insert(1, saveAsItem);

            glControl.Load += Gl_Load;
            glControl.Paint += Gl_Paint;
            glControl.Resize += Editor_Resize;
            glControl.MouseDown += Gl_MouseDown;
            glControl.MouseUp += Gl_MouseUp;
            glControl.MouseMove += Gl_MouseMove;
            glControl.MouseWheel += Gl_MouseWheel;

            _fpsTimer.Interval = 1000 / 30;
            _fpsTimer.Tick += (_, _) => glControl.Invalidate();
            _fpsTimer.Start();

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

            GL.UseProgram(0);
            glControl.SwapBuffers();
            CheckGLError("After SwapBuffers");

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

        public string GetCacheDir() {
            while (!IsCacheDirSet())
                SetCacheDir();
            return Properties.Settings.Default.cacheDir;
        }

        private void Editor_Load(object sender, EventArgs e) {
            //Here rather than in the constructor: the form's font scaling runs during layout, before
            //Load, and it would multiply anything set earlier by the same ratio that shrank the
            //designer's literals in the first place.
            SizeProgressBars();

            if (!string.Equals(Properties.Settings.Default.cacheDir, string.Empty, StringComparison.Ordinal))
                LoadCache(Properties.Settings.Default.cacheDir);
            NPCListView.AlwaysGroupByColumn = npcIdColumn;
            ItemListView.AlwaysGroupByColumn = ItemID;
            SpriteListView.AlwaysGroupByColumn = ID;
            GameObjectListView.AlwaysGroupByColumn = objectIdColumn;
            ModelListView.AlwaysGroupByColumn = ModelID;

            //ObjectListView invokes this on the UI thread while it builds and paints rows, so it
            //is the safety net for any row the texture worker did not supply a bitmap for, not
            //the normal path.
            TextureImage.ImageGetter = rowObject => {
                //The texture id is the ImageList key the worker writes under
                string key = ((TextureDefinition) rowObject).id.ToString();

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

            ItemListView.ClearObjects();
            ItemListView.Refresh();

            SpriteListView.ClearObjects();
            SpriteListView.Refresh();

            NPCListView.ClearObjects();
            NPCListView.Refresh();

            try {
                //Dispose old resources before loading new cache
                DisposeOldResources();

                //Load the cache and the reference tables
                RSFileStore store = new RSFileStore(directory);
                cache = new RSCache(store);
                cache.TryAutoLoadXTEAKeys(directory);
                _textureCache = new GLTextureCache(cache);
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
        ///     Gives the editor-controls progress bars a height their own font can fill.
        /// </summary>
        /// <remarks>
        ///     A <see cref="ProgressBar"/> cannot auto-size, so it is the one control in those strips
        ///     whose height has to be stated at all. Derived from the font rather than written into
        ///     the designer because the form is <c>AutoScaleMode.Font</c> against
        ///     <c>AutoScaleDimensions(9, 20)</c> and its own font measures about two thirds of that:
        ///     every literal size in the designer is multiplied down at runtime while the fonts stated
        ///     on the controls are not, which is what left the buttons shorter than their captions and
        ///     the status labels wider than the box around them. <c>DefinitionListPanel</c> sizes its
        ///     bar the same way and for the same reason.
        /// </remarks>
        private void SizeProgressBars() {
            foreach (ProgressBar bar in new[] { ItemProgressBar, ObjectProgressBar })
                bar.Height = Math.Max(10, bar.Font.Height);
        }

        /// <summary>
        ///     States which cache index each tab edits, and how the self-contained ones are bound.
        /// </summary>
        /// <remarks>
        ///     Called once, from the constructor, after <c>InitializeComponent</c> has created the
        ///     pages. Adding a tab means adding a line here; getting that wrong is caught on the
        ///     next launch rather than by a tab quietly showing another index's contents.
        /// </remarks>
        private void RegisterEditorTabs() {
            //The console describes the cache as a whole rather than one index, so it takes the
            //meta index - which is also what the loader reads to rebuild it on every visit.
            Register(Console, RSConstants.META_INDEX);

            Register(ItemEditorTab, RSConstants.ITEM_DEFINITIONS_INDEX);
            Register(SpriteEditorTab, RSConstants.SPRITES_INDEX);
            Register(NPCEditorTab, RSConstants.NPC_DEFINITIONS_INDEX);
            Register(ObjectEditorTab, RSConstants.OBJECTS_DEFINITIONS_INDEX);
            Register(ModelViewerTab, RSConstants.MODELS_INDEX);
            Register(TextureViewerTab, RSConstants.TEXTURES);

            //The self-contained tabs. Each owns its worker and its layout, so all the form does is
            //hand it the cache.
            /* Index 3 has two real levels - a group is one interface and a file is one component -
               so the tab lists interfaces and loads a single interface's components on selection.
               A flat listing of all 42,256 files hid the level that matters. */
            Register(InterfaceEditorTab, RSConstants.INTERFACE_DEFINITIONS_INDEX,
                openCache => InterfacePanel.Bind(openCache));
            /* Index 2 is thirty-five unrelated config families sharing one index, so the tab is one
               grid with a group selector: the group is the record type, not a page of ids. */
            Register(ConfigEditorTab, RSConstants.CONFIG,
                openCache => ConfigPanel.Bind(openCache));
            Register(MapEditorTab, RSConstants.MAPS_INDEX,
                openCache => MapEditorPanel.Bind(openCache, GetCacheDir()));
            //The tracks tab lists index 11 alongside index 6; 6 is what identifies the tab
            Register(TrackEditorTab, RSConstants.MUSIC_INDEX,
                openCache => TrackEditorPanel.Bind(openCache));
            /* Indexes 0 and 1 share one tab because neither can be read without the other: a frame
               addresses its bones positionally and the bone's transform type is what decides how the
               frame's numbers are read. Index 0 is what identifies the tab, since a row is a frame
               set and index 1 is joined onto it. */
            Register(AnimationEditorTab, RSConstants.FRAMES_INDEX,
                openCache => AnimationPanel.Bind(openCache));
            /* Index 4 is one file per group and the group id is the effect id, so the tab is a list
               of records rather than of files. The panel nests three grids under that list because a
               record does: ten tone slots, each with its envelopes and a filter cascade. */
            Register(SoundEffectEditorTab, RSConstants.SOUND_EFFECTS,
                openCache => SoundEffectPanel.Bind(openCache));

            /* Every page in the strip has to have named its index. An unregistered page is the
               failure the positional array made silent - it used to read whatever index happened to
               sit at its position, or run off the end of the array - so it is refused loudly here,
               at construction, rather than left to surface as a tab showing the wrong contents. */
            foreach (TabPage page in EditorTabControl.TabPages)
                if (!editorTabs.ContainsKey(page))
                    throw new InvalidOperationException(
                        "Editor tab '" + page.Name + "' is in the tab strip but names no cache index." +
                        " Add a Register call for it in RegisterEditorTabs.");
        }

        /// <summary>
        ///     Records what one tab edits.
        /// </summary>
        /// <param name="page">The page, which is the key - so its position in the strip is free to change.</param>
        /// <param name="indexId">The cache index the tab edits.</param>
        /// <param name="bind">
        ///     Hands the tab's own panel the open cache, for a tab that owns its loading. Null for a
        ///     tab still driven by the loader in <see cref="LoadEditorTab"/>.
        /// </param>
        private void Register(TabPage page, int indexId, Action<RSCache>? bind = null) {
            if (page == null)
                throw new ArgumentNullException(nameof(page));

            if (editorTabs.ContainsKey(page))
                throw new InvalidOperationException("Editor tab '" + page.Name + "' is registered twice.");

            editorTabs.Add(page, new EditorTabBinding(indexId, bind));
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

            RSReferenceTable? referenceTable = null; //Only the META_INDEX branch leaves this null, and that branch never reads the local

            //Set the reference table to the one we need for the index
            if (type != RSConstants.META_INDEX)
                referenceTable = cache.GetReferenceTable(type);

            switch (type) {
                case RSConstants.META_INDEX:
                    bgw.DoWork += delegate {
                        List<RSReferenceTable> refTables = new List<RSReferenceTable>();
                        for (int k = 0 ; k < cache.referenceTables.Length ; k++)
                            if (cache.referenceTables[k] != null)
                                refTables.Add(cache.referenceTables[k]);

                        List<RSContainer> containers = new List<RSContainer>();
                        foreach (KeyValuePair<int, SortedDictionary<int, RSContainer>> types in cache.containers) {
                            int containerType = types.Key;
                            foreach (KeyValuePair<int, RSContainer> container in types.Value) {
                                RSContainer c = container.Value;
                                containers.Add(c);
                            }
                        }

                        /*
                        RefTableListView.ChildrenGetter = delegate (object x) {
                            //Reference table object
                            RSReferenceTable tbl = (RSReferenceTable) x;

                            //List of archive IDs to be set as the childs
                            List<int> idList = new List<int>();
                            idList.AddRange(tbl.validArchiveIds);

                            return idList;
                        };*/

                        CompressCol.AspectGetter = (x) => ((RSContainer) x).GetCompressionString();

                        RefTableListView.SetObjects(refTables);
                        ContainerListView.SetObjects(containers);
                    };

                    bgw.Disposed += delegate {
                        workers.Remove(bgw);
                    };

                    bgw.RunWorkerAsync();
                    break;

                case RSConstants.ITEM_DEFINITIONS_INDEX:
                    //When an item is loaded, update the progress bar
                    bgw.ProgressChanged += new ProgressChangedEventHandler((sender, e) => {
                        ItemProgressBar.Value = e.ProgressPercentage;
                        ItemLoadingLabel.Text = e.UserState!.ToString(); //Every ReportProgress call in this worker passes a status string
                    });

                    bgw.DoWork += delegate {
                        int done = 0;
                        int total = referenceTable!.GetArchiveCount() * 256;
                        int percentile = Math.Max(1, total / 100);

                        Debug(@"  _                     _ _               _ _                     ");
                        Debug(@" | |                   | (_)             (_) |                    ");
                        Debug(@" | |     ___   __ _  __| |_ _ __   __ _   _| |_ ___ _ __ ___  ___ ");
                        Debug(@" | |    / _ \ / _` |/ _` | | '_ \ / _` | | | __/ _ \ '_ ` _ \/ __|");
                        Debug(@" | |___| (_) | (_| | (_| | | | | | (_| | | | ||  __/ | | | | \__ \");
                        Debug(@" |______\___/ \__,_|\__,_|_|_| |_|\__, | |_|\__\___|_| |_| |_|___/");
                        Debug(@"                                   __/ |                          ");
                        Debug(@"                                  |___/                           ");
                        Debug(@"Loading Items");

                        foreach (KeyValuePair<int, RSArchiveEntry> archive in referenceTable.GetArchiveEntries()) {
                            int archiveId = archive.Key;

                            Debug("Loading archive " + archive.Key);
                            for (int file = 0 ; file < 256 ; file++) {
                                try {
                                    ItemDefinition item = cache.GetItemDefinition(archiveId, file);
                                    int itemId = archiveId * 256 + file;
                                    item.SetId(itemId); //Set the item ID
                                    cache.items.Add(itemId, item);
                                }
                                catch (Exception ex) {
                                    Debug(ex.Message);
                                }
                                finally {
                                    done++;

                                    //Only update the progress bar for each 1% completed
                                    if (done % percentile == 0 || done == total)
                                        bgw.ReportProgress((done + 1) * 100 / total, "Loaded " + done + "/" + total + " (" + (done + 1) * 100 / total + "%)");
                                }
                            }
                        }

                        Debug("Finished loading " + total + " items");

                        ItemListView.SetObjects(cache.items.Values);
                    };

                    bgw.Disposed += delegate {
                        workers.Remove(bgw);
                    };

                    bgw.RunWorkerAsync();
                    break;
                case RSConstants.SPRITES_INDEX:

                    //When a sprite is loaded, update the progress bar
                    bgw.ProgressChanged += new ProgressChangedEventHandler((sender, e) => {
                        SpriteProgressBar.Value = e.ProgressPercentage;
                        SpriteLoadingLabel.Text = e.UserState!.ToString(); //Every ReportProgress call in this worker passes a status string
                    });

                    bgw.DoWork += delegate {
                        Debug(@" _                     _ _                _____            _ _           ");
                        Debug(@"| |                   | (_)              / ____|          (_| |          ");
                        Debug(@"| |     ___   __ _  __| |_ _ __   __ _  | (___  _ __  _ __ _| |_ ___ ___ ");
                        Debug(@"| |    / _ \ / _` |/ _` | | '_ \ / _` |  \___ \| '_ \| '__| | __/ _ / __|");
                        Debug(@"| |___| (_) | (_| | (_| | | | | | (_| |  ____) | |_) | |  | | ||  __\__ \");
                        Debug(@"|______\___/ \__,_|\__,_|_|_| |_|\__, | |_____/| .__/|_|  |_|\__\___|___/");
                        Debug(@"                                  __/ |        | |                       ");
                        Debug(@"                                 |___/         |_|                       ");
                        Debug(@"Loading Sprites");

                        List<SpriteDefinition> sprites = new List<SpriteDefinition>();

                        int done = 0;
                        int total = referenceTable!.GetArchiveCount();
                        int percentile = Math.Max(1, total / 100);

                        bgw.ReportProgress(0, "Loading " + total + " Sprites");
                        Debug("Loading " + total + " Sprites");
                        foreach (KeyValuePair<int, RSArchiveEntry> entry in referenceTable.GetArchiveEntries()) {
                            try {
                                Debug("Loading sprite: " + entry.Key, LOG_DETAIL.ADVANCED);

                                SpriteDefinition sprite = cache.GetSprite(entry.Key);
                                sprite.SetIndex(entry.Key);
                                sprites.Add(sprite);

                                done++;

                                //Only update the progress bar for each 1% completed
                                if (done % percentile == 0 || done == total)
                                    bgw.ReportProgress((done + 1) * 100 / total, "Loaded " + done + "/" + total + " (" + (done + 1) * 100 / total + "%)");
                            }
                            catch (Exception ex) {
                                Debug(ex.Message);
                            }
                        }

                        //Set the root objects for the tree
                        SpriteListView.SetObjects(sprites);

                        SpriteListView.CanExpandGetter = delegate (object x) {
                            if (x is SpriteDefinition definition)
                                if (definition.GetFrameCount() > 1)
                                    return true;
                            return false;
                        };

                        SpriteListView.ChildrenGetter = delegate (object x) {
                            //Basically this rewraps the RSBufferedImage (frames) as SpriteDefinitions
                            return ((SpriteDefinition) x).GetFrames().ConvertAll(y => ((SpriteDefinition) y));
                        };

                        //SpriteListView.TreeModel.ExpandAll();
                    };
                    bgw.RunWorkerAsync();
                    break;
                case RSConstants.NPC_DEFINITIONS_INDEX:
                    Debug(@" _                     _ _               _   _ _____   _____     ");
                    Debug(@"| |                   | (_)             | \ | |  __ \ / ____|    ");
                    Debug(@"| |     ___   __ _  __| |_ _ __   __ _  |  \| | |__) | |     ___ ");
                    Debug(@"| |    / _ \ / _` |/ _` | | '_ \ / _` | | . ` |  ___/| |    / __|");
                    Debug(@"| |___| (_) | (_| | (_| | | | | | (_| | | |\  | |    | |____\__ \");
                    Debug(@"|______\___/ \__,_|\__,_|_|_| |_|\__, | |_| \_|_|     \_____|___/");
                    Debug(@"                                  __/ |                          ");
                    Debug(@"                                 |___/                           ");
                    Debug(@"Loading NPCs");

                    //When an NPC is loaded, update the progress bar
                    bgw.ProgressChanged += new ProgressChangedEventHandler((sender, e) => {
                        NPCProgressBar.Value = e.ProgressPercentage;
                        NPCLoadingLabel.Text = e.UserState!.ToString(); //Every ReportProgress call in this worker passes a status string
                    });

                    bgw.DoWork += async delegate {
                        List<NPCDefinition> npcs = new List<NPCDefinition>();

                        int done = 0;
                        int total = referenceTable!.GetArchiveCount() * 128;
                        int percentile = Math.Max(1, total / 100);

                        bgw.ReportProgress(0, "Loading NPCs");

                        Debug("Loading NPC data");

                        foreach (KeyValuePair<int, RSArchiveEntry> archive in referenceTable.GetArchiveEntries()) {
                            int archiveId = archive.Key;

                            Debug("Loading archive " + archiveId);
                            for (int file = 0 ; file < 128 ; file++) {
                                try {
                                    NPCDefinition npc = cache.GetNPCDefinition(archiveId, file);
                                    npc.SetId(archiveId * 128 + file); //Set the NPC ID
                                    cache.npcs[npc.id] = npc;
                                    npcs.Add(npc);
                                }
                                catch (Exception ex) {
                                    Debug(ex.Message);
                                }
                                finally {
                                    done++;

                                    //Only update the progress bar for each 1% completed
                                    if (done % percentile == 0 || done == total)
                                        bgw.ReportProgress((done + 1) * 100 / total, "Loaded " + done + "/" + total + " (" + (done + 1) * 100 / total + "%)");
                                }
                            }
                        }

                        NPCListView.SetObjects(npcs);
                    };

                    bgw.Disposed += delegate {
                        workers.Remove(bgw);
                    };

                    bgw.RunWorkerAsync();
                    break;
                case RSConstants.OBJECTS_DEFINITIONS_INDEX:
                    bgw.ProgressChanged += new ProgressChangedEventHandler((sender, e) => {
                        ObjectProgressBar.Value = e.ProgressPercentage;
                        ObjectLoadingLabel.Text = e.UserState!.ToString(); //Every ReportProgress call in this worker passes a status string
                    });

                    bgw.DoWork += delegate {
                        List<ObjectDefinition> objects = new List<ObjectDefinition>();

                        int filesPerArchive = referenceTable!.GetArchiveEntry(referenceTable.GetArchiveEntries().Keys.First()).GetValidFileIds().Length;
                        int total = referenceTable.GetArchiveCount() * filesPerArchive;
                        int done = 0;
                        int percentile = Math.Max(1, total / 100);

                        bgw.ReportProgress(0, "Loading Objects");

                        foreach (KeyValuePair<int, RSArchiveEntry> archive in referenceTable.GetArchiveEntries()) {
                            int archiveId = archive.Key;
                            for (int file = 0 ; file < filesPerArchive ; file++) {
                                try {
                                    ObjectDefinition obj = cache.GetObjectDefinition(archiveId, file);
                                    obj.id = archiveId * filesPerArchive + file;
                                    cache.objects[obj.id] = obj;
                                    objects.Add(obj);
                                }
                                catch (Exception ex) {
                                    Debug(ex.Message);
                                }
                                finally {
                                    done++;
                                    if (done % percentile == 0 || done == total)
                                        bgw.ReportProgress((done + 1) * 100 / total, $"Loaded {done}/{total} {(done + 1) * 100 / total}%");
                                }
                            }
                        }

                        GameObjectListView.SetObjects(objects);
                    };

                    bgw.Disposed += delegate {
                        workers.Remove(bgw);
                    };

                    bgw.RunWorkerAsync();
                    break;

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
                        TextureLoadingLabel.Text = $"Rendering {snapshot.Count} textures";

                        bgw.ProgressChanged += new ProgressChangedEventHandler((sender, e) => {
                            TextureProgressBar.Value = Math.Clamp(e.ProgressPercentage, 0, 100);
                            TextureLoadingLabel.Text = e.UserState!.ToString(); //Every ReportProgress call in this worker passes a status string
                        });

                        bgw.DoWork += (object? s, DoWorkEventArgs args) => {
                            args.Result = RenderTextureThumbnails(bgw, snapshot, args);
                        };

                        bgw.RunWorkerCompleted += (_, e) => {
                            //Reading e.Result throws when the worker cancelled or faulted, so both
                            //are checked first. LoadEditorTab marks the tab loaded before any work
                            //starts, so a fault has to clear that flag or the tab stays empty for
                            //the rest of the session with no way to retry.
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

                            ApplyTextureThumbnails((TextureThumbnailBatch) e.Result!);
                        };

                        bgw.Disposed += delegate {
                            workers.Remove(bgw);
                        };

                        bgw.RunWorkerAsync();
                        break;
                    }

                case RSConstants.MODELS_INDEX: {
                        ProgressBar bar = ModelProgressBar;
                        Label lbl = ModelLoadingLabel;

                        bgw.DoWork += (object? s, DoWorkEventArgs args) => {
                            var list = cache.EnumerateModelReferences().ToList();
                            args.Result = list;
                        };

                        bgw.RunWorkerCompleted += (_, e) => {
                            var list = (List<ModelReference>) e.Result!;
                            ModelListView.SetObjects(list);
                            lbl.Text = $"Models loaded ({list.Count})";
                        };

                        bgw.RunWorkerAsync();
                        break;
                    }

            }
        }

        /// <summary>
        /// When you flick to a different editor page
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private void EditorTabControl_SelectedIndexChanged(object sender, EventArgs e) {
            LoadEditorTab(EditorTabControl.SelectedTab);
        }

        /// <summary>The cache index the selected tab edits, or -1 when it names none.</summary>
        public int GetEditorType() {
            TabPage? page = EditorTabControl.SelectedTab;
            return page != null && editorTabs.TryGetValue(page, out EditorTabBinding? binding)
                ? binding.IndexId
                : -1;
        }

        private void ExportSpriteBmpBtn_Click(object sender, EventArgs e) {
            string dir = GetCacheDir() + "\\sprites";
            Directory.CreateDirectory(dir);

            foreach (SpriteDefinition sprite in SpriteListView.SelectedObjects)
                if (sprite.thumb != null)
                    sprite.thumb.Save(dir + "\\" + sprite.index + ".png");
        }

        private void SetDirectoryToolStripMenuItem_Click(object sender, EventArgs e) {
            SetCacheDir();
            LoadCache();
        }

        private void OpenDirectoryToolStripMenuItem_Click(object sender, EventArgs e) {
            if (IsCacheDirSet())
                Process.Start(GetCacheDir());
        }

        private void ExportSpriteDatBtn_Click(object sender, EventArgs e) {
            //Nothing yet bro
            MessageBox.Show("Sorry doesn't work");
        }

        //Finished editing a definition
        private void ItemListView_CellEditFinished(object sender, CellEditEventArgs e) {
            Debug(@" ______    _ _ _     _____ _                ");
            Debug(@"|  ____|  | (_) |   |_   _| |                ");
            Debug(@"| |__   __| |_| |_    | | | |_ ___ _ __ ___  ");
            Debug(@"|  __| / _` | | __|   | | | __/ _ \ '_ ` _ \ ");
            Debug(@"| |___| (_| | | |_   _| |_| ||  __/ | | | | |");
            Debug(@"|______\__,_|_|\__| |_____|\__\___|_| |_| |_|");
            Debug("Edit Item");

            Debug("itemdef name: " + currentItem.name);

            //Get the object represented by the ListView
            ItemDefinition newDefinition = (ItemDefinition) e.RowObject;

            //Skip write if nothing actually changed
            byte[] newBytes = newDefinition.Encode().ToArray();
            byte[] oldBytes = currentItem.Encode().ToArray();
            if (newBytes.AsSpan().SequenceEqual(oldBytes))
                return;

            //Update the items archive with the new definition
            cache.items[newDefinition.id] = newDefinition;

            //Update the cache definition
            int archiveId = newDefinition.id / 256;
            int entryId = newDefinition.id % 256;

            //Update the entry in the container's archive
            JagStream newItemStream = new JagStream(newBytes);

            cache.WriteFile(RSConstants.ITEM_DEFINITIONS_INDEX, archiveId, entryId, newItemStream);

            PrintDifferences(newDefinition, currentItem);
        }

        private void ObjectListView_CellEditFinished(object sender, CellEditEventArgs e) {
            ObjectDefinition newDef = (ObjectDefinition) e.RowObject;

            //Skip write if nothing actually changed
            byte[] newBytes = newDef.Encode().ToArray();
            byte[] oldBytes = currentObject.Encode().ToArray();
            if (newBytes.AsSpan().SequenceEqual(oldBytes))
                return;

            cache.objects[newDef.id] = newDef;

            var refTable = cache.GetReferenceTable(RSConstants.OBJECTS_DEFINITIONS_INDEX);
            int filesPerArchive = refTable.GetArchiveEntry(refTable.GetArchiveEntries().Keys.First()).GetValidFileIds().Length;
            int archiveId = newDef.id / filesPerArchive;
            int entryId = newDef.id % filesPerArchive;

            JagStream data = new JagStream(newBytes);
            cache.WriteFile(RSConstants.OBJECTS_DEFINITIONS_INDEX, archiveId, entryId, data);

            PrintDifferences(newDef, currentObject);
        }

        private void NPCListView_CellEditFinished(object sender, CellEditEventArgs e) {
            NPCDefinition newDef = (NPCDefinition) e.RowObject;

            //Skip write if nothing actually changed
            byte[] newBytes = newDef.Encode().ToArray();
            byte[] oldBytes = currentNpc.Encode().ToArray();
            if (newBytes.AsSpan().SequenceEqual(oldBytes))
                return;

            cache.npcs[newDef.id] = newDef;

            int archiveId = newDef.id / 128;
            int entryId = newDef.id % 128;

            JagStream data = new JagStream(newBytes);
            cache.WriteFile(RSConstants.NPC_DEFINITIONS_INDEX, archiveId, entryId, data);

            PrintDifferences(newDef, currentNpc);
        }

        private void ExportItemDatBtn_Click(object sender, EventArgs e) {
            ItemLoadingLabel.Text = "Status: Dumping " + ItemListView.SelectedObjects.Count + " Items...";

            //Creates a new background worker
            BackgroundWorker itemDumper = new BackgroundWorker {
                WorkerReportsProgress = true,
                WorkerSupportsCancellation = true
            };
            workers.Add(itemDumper);

            //When an item is loaded, update the progress bar
            itemDumper.ProgressChanged += new ProgressChangedEventHandler((sender2, e2) => {
                ItemProgressBar.Value = e2.ProgressPercentage;
                //DoWork calls the single-arg ReportProgress, so UserState is always null
                ItemLoadingLabel.Text = e2.UserState?.ToString() ?? "Status: Dumping " + e2.ProgressPercentage + "%...";
            });

            ItemDefinition[] items = new ItemDefinition[ItemListView.SelectedObjects.Count];
            ItemListView.SelectedObjects.CopyTo(items, 0);
            Debug(items[0].name);

            itemDumper.DoWork += delegate {
                if (items.Length > 0) {
                    //Ensures that the directory exists
                    Directory.CreateDirectory(RSConstants.CACHE_OUTPUT_DIRECTORY + "/items/");

                    int done = 0;

                    foreach (ItemDefinition def in items) {
                        Debug("Exporting Item " + def.GetId() + " name is " + def.name);
                        JagStream.Save(def.Encode(), RSConstants.CACHE_OUTPUT_DIRECTORY + "/items/" + def.id + ".dat");
                        done++;
                        itemDumper.ReportProgress(done * 100 / items.Length);
                    }
                }
            };

            itemDumper.Disposed += delegate {
                workers.Remove(itemDumper);
            };

            itemDumper.RunWorkerCompleted += (sender2, e2) => {
                if (e2.Error != null)
                    Debug("error: " + e2.Error.ToString());
            };

            itemDumper.RunWorkerAsync();
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

        internal ItemDefinition currentItem;

        internal NPCDefinition currentNpc;

        internal ObjectDefinition currentObject;

        private void ItemListView_CellEditStarting(object sender, CellEditEventArgs e) {
            //cache the item definition prior to editing
            currentItem = (ItemDefinition) ItemListView.SelectedObject;
            currentItem = currentItem.Clone();
        }

        private void ObjectListView_CellEditStarting(object sender, CellEditEventArgs e) {
            currentObject = (ObjectDefinition) GameObjectListView.SelectedObject;
            currentObject = currentObject.Clone();
        }

        private void NPCListView_CellEditStarting(object sender, CellEditEventArgs e) {
            currentNpc = (NPCDefinition) NPCListView.SelectedObject;
            currentNpc = currentNpc.Clone();
        }

        private void button5_Click(object sender, EventArgs e) {
            SetCacheDir(RSConstants.CACHE_ORIGINAL_COPY);
            LoadCache(GetCacheDir());
        }

        private void button6_Click(object sender, EventArgs e) {
            SetCacheDir(RSConstants.CACHE_OUTPUT_DIRECTORY);
            LoadCache(GetCacheDir());
        }

        //Set the alternating row back color
        private void alternateRowsToolStripMenuItem_Click(object sender, EventArgs e) {
            TreeListView[] tlvs = { RefTableListView, ContainerListView, SpriteListView };
            FastObjectListView[] olvs = { ItemListView, NPCListView, GameObjectListView };
            DialogResult result = colorDialog1.ShowDialog();

            foreach (TreeListView tlv in tlvs) {
                tlv.UseAlternatingBackColors = result == DialogResult.OK;
                tlv.AlternateRowBackColor = colorDialog1.Color;
                tlv.Refresh();
            }

            foreach (FastObjectListView olv in olvs) {
                olv.UseAlternatingBackColors = result == DialogResult.OK;
                olv.AlternateRowBackColor = colorDialog1.Color;
                olv.Refresh();
            }

        }

        private void numericUpDown1_ValueChanged_1(object sender, EventArgs e) {
            SpriteListView.RowHeight = (int) numericUpDown1.Value;
        }

        private void ModelListView_SelectedIndexChanged(object sender, EventArgs e) {
            Debug("Entered ModelListView_SelectedIndexChanged", LOG_DETAIL.ADVANCED);

            if (ModelListView.SelectedObject is ModelReference mr) {
                Debug($"SelectedObject is ModelReference (ID={mr.ModelID}, Archive={mr.ArchiveId}, File={mr.FileId})", LOG_DETAIL.ADVANCED);

                int id = mr.ModelID;

                // Check cache
                if (cache.models.TryGetValue(id, out var def)) {
                    Debug($"Cache hit for model {id} – rendering immediately.", LOG_DETAIL.ADVANCED);
                    if (_textureCache != null)
                    {
                        if (_testTexture != 0)
                        {
                            GL.DeleteTexture(_testTexture);
                            _testTexture = 0;
                        }
                        _modelRenderer.Load(def, _textureCache);
                        FrameModel(new[] { def });
                        UpdateModelTooltip($"Model {id} (Archive={mr.ArchiveId}, File={mr.FileId})", new[] { id }, new[] { def });
                    }
                    glControl.Invalidate();
                    return;
                }

                Debug($"Cache miss for model {id}.", LOG_DETAIL.ADVANCED);

                // See if a load is already in progress
                if (!_modelTasks.TryGetValue(id, out var task)) {
                    Debug($"No existing task for model {id}, starting new Task.Run…", LOG_DETAIL.ADVANCED);
                    task = Task.Run(() => {
                        Debug($"[BG] Calling cache.GetModelDefinition({mr.ArchiveId}, {mr.FileId})", LOG_DETAIL.ADVANCED);
                        var result = cache.GetModelDefinition(mr.ArchiveId, mr.FileId);
                        Debug($"[BG] Finished GetModelDefinition for {id}", LOG_DETAIL.ADVANCED);
                        return result;
                    });
                    _modelTasks[id] = task;
                }
                else {
                    Debug($"Found existing task for model {id}, skipping new Task.Run.", LOG_DETAIL.ADVANCED);
                }

                // When the task completes…
                task.ContinueWith(t => {
                    Debug($"[UI] Task completed with status {t.Status} for model {id}", LOG_DETAIL.ADVANCED);

                    if (t.Status == TaskStatus.RanToCompletion) {
                        var loaded = t.Result;
                        Debug($"[UI] Caching loaded model {id}", LOG_DETAIL.ADVANCED);
                        cache.models[id] = loaded;

                        Debug($"[UI] Removing task entry for {id}", LOG_DETAIL.ADVANCED);
                        _modelTasks.Remove(id);

                        Debug($"[UI] Rendering loaded model {id}", LOG_DETAIL.ADVANCED);
                        if (_textureCache != null)
                        {
                            if (_testTexture != 0)
                            {
                                GL.DeleteTexture(_testTexture);
                                _testTexture = 0;
                            }
                            _modelRenderer.Load(loaded, _textureCache);
                            FrameModel(new[] { loaded });
                            UpdateModelTooltip($"Model {id} (Archive={mr.ArchiveId}, File={mr.FileId})", new[] { id }, new[] { loaded });
                        }
                        glControl.Invalidate();
                    }
                    else if (t.IsFaulted) {
                        _modelTasks.Remove(id);
                        Debug($"[UI] Error loading model {id}: {t.Exception?.Flatten().InnerException}", LOG_DETAIL.ADVANCED);
                    }
                }, TaskScheduler.FromCurrentSynchronizationContext());
            }
            else {
                Debug("SelectedObject was NOT a ModelReference – doing nothing.", LOG_DETAIL.ADVANCED);
            }
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

        private void NPCListView_SelectedIndexChanged(object sender, EventArgs e) {
            if (NPCListView.SelectedObject is not NPCDefinition npc) return;
            if (_textureCache == null) return;

            var ids = npc.modelIds?.Where(id => id >= 0).ToArray();
            if (ids == null || ids.Length == 0) return;

            Debug($"NPC {npc.id} '{npc.name}': loading {ids.Length} models [{string.Join(", ", ids)}]");

            Task.Run(() => {
                var defs = new List<ModelDefinition>();
                for (int i = 0; i < ids.Length; i++) {
                    try {
                        var def = cache.GetModelDefinition(ids[i], 0).CloneForRendering();
                        // Find the original index in modelIds for translation lookup
                        int modelIndex = Array.IndexOf(npc.modelIds!, ids[i]); //non-null: ids was built from npc.modelIds and the null case returned at the top
                        ApplyNpcTransforms(def, npc, modelIndex);
                        defs.Add(def);
                        Debug($"  Model {ids[i]}: {def.VertexCount} verts, {def.TriangleCount} tris", LOG_DETAIL.ADVANCED);
                    }
                    catch (Exception ex) {
                        Debug($"  Model {ids[i]}: FAILED - {ex.Message}");
                    }
                }
                Debug($"NPC {npc.id}: loaded {defs.Count}/{ids.Length} models");
                return defs;
            }).ContinueWith(t => {
                if (t.Status != TaskStatus.RanToCompletion || t.Result.Count == 0) return;
                if (!glControl.IsHandleCreated) return;
                glControl.MakeCurrent();
                if (_testTexture != 0) { GL.DeleteTexture(_testTexture); _testTexture = 0; }
                _modelRenderer.LoadMultiple(t.Result, _textureCache);
                FrameModel(t.Result);
                UpdateModelTooltip($"NPC {npc.id} '{npc.name}'", ids, t.Result);
                glControl.Invalidate();
            }, TaskScheduler.FromCurrentSynchronizationContext());
        }

        private void ItemListView_SelectedIndexChanged(object sender, EventArgs e) {
            if (ItemListView.SelectedObject is not ItemDefinition item) return;
            if (_textureCache == null) return;

            int modelId = item.inventoryModelId;
            if (modelId <= 0) return;

            Debug($"Item {item.id} '{item.name}': loading model {modelId}");

            Task.Run(() => {
                var defs = new List<ModelDefinition>();
                try {
                    var def = cache.GetModelDefinition(modelId, 0).CloneForRendering();
                    ApplyItemTransforms(def, item);
                    defs.Add(def);
                    Debug($"  Model {modelId}: {def.VertexCount} verts, {def.TriangleCount} tris", LOG_DETAIL.ADVANCED);
                }
                catch (Exception ex) {
                    Debug($"  Model {modelId}: FAILED - {ex.Message}");
                }
                return defs;
            }).ContinueWith(t => {
                if (t.Status != TaskStatus.RanToCompletion || t.Result.Count == 0) return;
                if (!glControl.IsHandleCreated) return;
                glControl.MakeCurrent();
                if (_testTexture != 0) { GL.DeleteTexture(_testTexture); _testTexture = 0; }
                _modelRenderer.LoadMultiple(t.Result, _textureCache);
                FrameModel(t.Result);
                UpdateModelTooltip($"Item {item.id} '{item.name}' (model {modelId})", new[] { modelId }, t.Result);
                glControl.Invalidate();
            }, TaskScheduler.FromCurrentSynchronizationContext());
        }

        private void GameObjectListView_SelectedIndexChanged(object sender, EventArgs e) {
            if (GameObjectListView.SelectedObject is not ObjectDefinition obj) return;
            if (_textureCache == null) return;

            // Use the first render group (default orientation)
            if (obj.modelIds == null || obj.modelIds.Length == 0 || obj.modelIds[0] == null) return;
            var ids = obj.modelIds[0].Where(id => id > 0).Select(id => (int)id).ToArray();
            if (ids.Length == 0) return;

            Debug($"Object {obj.id} '{obj.name}': loading {ids.Length} models [{string.Join(", ", ids)}]");

            Task.Run(() => {
                var defs = new List<ModelDefinition>();
                foreach (int id in ids) {
                    try {
                        var def = cache.GetModelDefinition(id, 0);
                        defs.Add(def);
                        Debug($"  Model {id}: {def.VertexCount} verts, {def.TriangleCount} tris", LOG_DETAIL.ADVANCED);
                    }
                    catch (Exception ex) {
                        Debug($"  Model {id}: FAILED - {ex.Message}");
                    }
                }
                Debug($"Object {obj.id}: loaded {defs.Count}/{ids.Length} models");
                return defs;
            }).ContinueWith(t => {
                if (t.Status != TaskStatus.RanToCompletion || t.Result.Count == 0) return;
                if (!glControl.IsHandleCreated) return;
                glControl.MakeCurrent();
                if (_testTexture != 0) { GL.DeleteTexture(_testTexture); _testTexture = 0; }
                _modelRenderer.LoadMultiple(t.Result, _textureCache);
                FrameModel(t.Result);
                UpdateModelTooltip($"Object {obj.id} '{obj.name}'", ids, t.Result);
                glControl.Invalidate();
            }, TaskScheduler.FromCurrentSynchronizationContext());
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
            if (_activeButton == MouseButtons.None)
                return;

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

            //Same reason, one step weaker: this cancels the definition list's worker rather than
            //joining it, so a load already inside a group read can still see the store close. Every
            //group read there is guarded, so the worst case is a discarded result - which it was
            //going to be anyway, since unbinding supersedes it.
            InterfacePanel.Bind(null);

            //Same again: the config tab's record list reads a whole group of index 2 on a worker.
            ConfigPanel.Bind(null);

            //Same again: the animation tab's frame-set sweep reads every group in index 0, so a
            //reload started from another tab would leave it decoding out of a disposed file store.
            AnimationPanel.Bind(null);

            if(SpriteListView.Objects != null) {
                foreach(object obj in SpriteListView.Objects) {
                    if(obj is SpriteDefinition sprite)
                        sprite.Dispose();
                }
            }
            TextureManager.Clear();
            _textureImageList.Images.Clear();
            _textureCache?.Dispose();
            _textureCache = null;
            cache?.store?.Dispose();
        }

        protected override void OnFormClosed(FormClosedEventArgs e) {
            _fpsTimer.Stop();
            DisposeOldResources();
            _modelRenderer.Dispose();
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
        /// Renders every texture graph and builds its list thumbnail, entirely off the UI thread.
        /// </summary>
        /// <remarks>
        /// Nothing here may touch a control or the ImageList: an ImageList realises a native
        /// handle on first use and is not thread safe, and the list view is a control. Creating
        /// the bitmaps here is safe because each one is unattached and owned by this thread until
        /// <see cref="ApplyTextureThumbnails"/> hands it over.
        /// </remarks>
        /// <param name="bgw">The worker driving this pass, used for progress and cancellation.</param>
        /// <param name="textures">A snapshot taken on the UI thread, so the static dictionary is never enumerated off-thread.</param>
        /// <param name="args">The <see cref="DoWorkEventArgs"/> to flag when cancellation wins the race.</param>
        private static TextureThumbnailBatch RenderTextureThumbnails(BackgroundWorker bgw, List<TextureDefinition> textures, DoWorkEventArgs args) {
            var batch = new TextureThumbnailBatch(textures);

            int total = textures.Count;
            if (total == 0)
                return batch;

            int percentile = Math.Max(1, total / 100);
            int done = 0;
            int failed = 0;

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

                //Reported on 1% boundaries only. ReportProgress is safe from several threads, but
                //one post per texture from N threads floods the message pump and makes the UI less
                //responsive, which is the opposite of the point.
                int count = System.Threading.Interlocked.Increment(ref done);
                if (count % percentile == 0 || count == total)
                    bgw.ReportProgress(count * 100 / total, $"Rendering {count}/{total} ({count * 100 / total}%)");
            });

            sw.Stop();
            Debug($"LoadTextures: rendered {done}/{total} in {sw.ElapsedMilliseconds}ms, {failed} errors", LOG_DETAIL.BASIC);

            if (bgw.CancellationPending) {
                args.Cancel = true;
                return batch;
            }

            //Thumbnail construction also belongs here. GDI+ bitmap creation and drawing are safe
            //off the UI thread for an unattached bitmap with a single owner, and doing all 1408 of
            //them on the UI thread was the second half of the freeze.
            bgw.ReportProgress(100, $"Building {total} thumbnails");

            int solidFallbacks = 0;
            foreach (var tex in textures) {
                try {
                    //EnsureRendered leaves a thumb behind on every exit, so a null one means it
                    //could not run at all rather than that the texture has nothing to draw.
                    Bitmap? source = tex.thumb;
                    if (source != null) {
                        batch.Thumbnails.Add((tex.id, CreateThumbnail(source)));
                    } else {
                        batch.Thumbnails.Add((tex.id, SolidTextureThumbnail(TextureManager.RepresentativeRgb(tex))));
                        solidFallbacks++;
                    }
                } catch (Exception ex) {
                    //Skipped rather than substituted: the deferred ImageGetter fills any key the
                    //batch does not carry, so a texture is never left without a tile.
                    Debug($"Error building thumbnail for texture {tex.id}: {ex.GetType().Name}: {ex.Message}", LOG_DETAIL.BASIC);
                }
            }

            Debug($"LoadTextures: {batch.Thumbnails.Count} thumbnails built, {solidFallbacks} from the material colour", LOG_DETAIL.BASIC);
            return batch;
        }

        /// <summary>
        /// Publishes what the texture worker produced: the tile bitmaps first, then the rows.
        /// </summary>
        /// <remarks>
        /// Split out from the render pass because both halves of this are UI-thread only. The
        /// other tabs call <c>SetObjects</c> from inside <c>DoWork</c>, which is cross-thread
        /// control access and works by luck; the models tab does it in <c>RunWorkerCompleted</c>
        /// and is the shape copied here.
        /// </remarks>
        private void ApplyTextureThumbnails(TextureThumbnailBatch batch) {
            TextureListView.BeginUpdate();
            try {
                foreach ((int id, Bitmap thumbnail) in batch.Thumbnails) {
                    string key = id.ToString();

                    //Only added to the image list. tex.thumb keeps the full-resolution render so
                    //GLTextureCache can still upload it to the model viewer.
                    if (!_textureImageList.Images.ContainsKey(key))
                        _textureImageList.Images.Add(key, thumbnail);
                    else
                        thumbnail.Dispose(); //Never handed over, so this one is ours to release
                }

                TextureListView.SetObjects(batch.Definitions);
            } finally {
                TextureListView.EndUpdate();
            }

            TextureProgressBar.Value = 100;
            TextureLoadingLabel.Text = $"Textures loaded ({batch.Definitions.Count})";
            TextureManager.PrintDiagnostics();
        }

        /// <summary>
        /// What the texture worker hands back to the UI thread.
        /// </summary>
        /// <remarks>
        /// The rows and their bitmaps travel together so the completion handler cannot bind one
        /// without inserting the other, which would leave rows drawing whatever the ImageList
        /// happened to already hold under those keys.
        /// </remarks>
        private sealed class TextureThumbnailBatch {
            internal TextureThumbnailBatch(List<TextureDefinition> definitions) {
                Definitions = definitions;
            }

            /// <summary>The rows to bind, in the order the list view should show them.</summary>
            internal List<TextureDefinition> Definitions { get; }

            /// <summary>Each texture id paired with the bitmap the ImageList should key under it.</summary>
            internal List<(int Id, Bitmap Thumbnail)> Thumbnails { get; } = new();
        }
    }
}