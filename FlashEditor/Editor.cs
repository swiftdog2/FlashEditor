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
using System.Diagnostics;
using System;
using System.IO;
using System.Drawing;
using System.Runtime.InteropServices;
using System.Windows.Forms;
using static FlashEditor.Utils.DebugUtil;
using Timer = System.Windows.Forms.Timer;


namespace FlashEditor {
    public partial class Editor : Form {
        internal RSCache cache;
        private readonly ModelRenderer _modelRenderer = new ModelRenderer();
        private readonly Dictionary<int, System.Threading.Tasks.Task<ModelDefinition>> _modelTasks = new();

        private readonly ImageList _textureImageList = new ImageList();
        private readonly ContextMenuStrip _textureContextMenu = new ContextMenuStrip();

        private readonly Timer _fpsTimer = new();
        private int _program;
        private int _uModel, _uView, _uProj;
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

        private static void SetIconSpacing(ListView lv, int spacing)
        {
            int param = (spacing << 16) | spacing;
            SendMessage(lv.Handle, LVM_SETICONSPACING, 0, param);
        }
        //Change the order of the indexes when you change the layout of the editor tabs
        static readonly int[] editorTypes = {
            -1,
            RSConstants.ITEM_DEFINITIONS_INDEX,
            RSConstants.SPRITES_INDEX,
            RSConstants.NPC_DEFINITIONS_INDEX,
            RSConstants.OBJECTS_DEFINITIONS_INDEX,
            RSConstants.INTERFACE_DEFINITIONS_INDEX,
            RSConstants.MODELS_INDEX
        };

        bool[] loaded = new bool[editorTypes.Length];

        List<BackgroundWorker> workers = new List<BackgroundWorker>();
        public Editor() {
            InitializeComponent();

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

            int vert = CompileShader(ShaderType.VertexShader, LoadShader("basic.vert"));
            int frag = CompileShader(ShaderType.FragmentShader, LoadShader("basic.frag"));

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
            float[] v = { -0.5f, -0.5f, 0f, 0.5f, -0.5f, 0f, 0f, 0.5f, 0f };
            ushort[] i = { 0, 1, 2 };
            _modelRenderer.Load(v, i);

            GL.ClearColor(0.1f, 0.15f, 0.20f, 1.0f);
            GL.Enable(EnableCap.DepthTest);

            UpdateView();
            UpdateProjection();
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

        private void UpdateProjection() {
            _proj = Matrix4.CreatePerspectiveFieldOfView(
                MathHelper.DegreesToRadians((float)_fov),
                glControl.Width / (float)glControl.Height,
                0.1f,
                100f);
        }

        private Vector3 CameraPosition() {
            return new Vector3(
                _target.X + (float)(_distance * Math.Cos(_pitch) * Math.Sin(_yaw)),
                _target.Y + (float)(_distance * Math.Sin(_pitch)),
                _target.Z + (float)(_distance * Math.Cos(_pitch) * Math.Cos(_yaw))
            );
        }

        private void UpdateView() {
            _view = Matrix4.LookAt(CameraPosition(), _target, _up);
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
            _modelRenderer.Draw();

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
            if (!string.Equals(Properties.Settings.Default.cacheDir, string.Empty, StringComparison.Ordinal))
                LoadCache(Properties.Settings.Default.cacheDir);
            NPCListView.AlwaysGroupByColumn = npcIdColumn;
            ItemListView.AlwaysGroupByColumn = ItemID;
            SpriteListView.AlwaysGroupByColumn = ID;
            ObjectListView.AlwaysGroupByColumn = objectIdColumn;
            ModelListView.AlwaysGroupByColumn = ModelID;
        }

        private void LoadCache() {
            workers.ForEach(w => w.CancelAsync());
            loaded = new bool[editorTypes.Length];
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

            //Clear off the previous crap
            workers.Clear();

            ItemListView.ClearObjects();
            ItemListView.Refresh();

            SpriteListView.ClearObjects();
            SpriteListView.Refresh();

            NPCListView.ClearObjects();
            NPCListView.Refresh();

            try {
                //Load the cache and the reference tables
                RSFileStore store = new RSFileStore(directory);
                cache = new RSCache(store);
                sw.Stop();

                Debug("Loaded cache in " + sw.ElapsedMilliseconds + "ms");

                //Refresh the loaded pages
                loaded = new bool[editorTypes.Length];

                //Go back to the main panel
                LoadEditorTab(EditorTabControl.SelectedIndex);
            }
            catch (Exception ex) {
                Debug(ex.StackTrace);
            }
        }

        public void LoadEditorTab(int editorIndex) {
            int type = editorTypes[editorIndex];

            //Don't worry about the main menu
            if (type == -1)
                type = RSConstants.META_INDEX;

            if (cache == null) {
                Debug("Cache failed to load");
                return;
            }

            //Already loaded, no need to reload
            if (loaded[editorIndex] && type != RSConstants.META_INDEX)
                return;

            /*
             * Once we've loaded the tab, there's no need to reload it every time
             * so lock the editor index from being re-loaded (and hence limit access
             * to a single background worker, otherwise you run into race conditions)
             */

            loaded[editorIndex] = true;

            //Creates a new background worker
            BackgroundWorker bgw = new BackgroundWorker {
                WorkerReportsProgress = true,
                WorkerSupportsCancellation = true
            };
            //This enables us to load multiple tabs at once
            workers.Add(bgw);

            RSReferenceTable referenceTable = null;

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
                        ItemLoadingLabel.Text = e.UserState.ToString();
                    });

                    bgw.DoWork += delegate {
                        int done = 0;
                        int total = referenceTable.GetEntryTotal() * 256;
                        int percentile = total / 100;

                        Debug(@"  _                     _ _               _ _                     ");
                        Debug(@" | |                   | (_)             (_) |                    ");
                        Debug(@" | |     ___   __ _  __| |_ _ __   __ _   _| |_ ___ _ __ ___  ___ ");
                        Debug(@" | |    / _ \ / _` |/ _` | | '_ \ / _` | | | __/ _ \ '_ ` _ \/ __|");
                        Debug(@" | |___| (_) | (_| | (_| | | | | | (_| | | | ||  __/ | | | | \__ \");
                        Debug(@" |______\___/ \__,_|\__,_|_|_| |_|\__, | |_|\__\___|_| |_| |_|___/");
                        Debug(@"                                   __/ |                          ");
                        Debug(@"                                  |___/                           ");
                        Debug(@"Loading Items");

                        foreach (KeyValuePair<int, RSEntry> archive in referenceTable.GetEntries()) {
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
                        SpriteLoadingLabel.Text = e.UserState.ToString();
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
                        int total = referenceTable.GetEntryTotal();
                        int percentile = total / 100;

                        bgw.ReportProgress(0, "Loading " + total + " Sprites");
                        Debug("Loading " + total + " Sprites");
                        foreach (KeyValuePair<int, RSEntry> entry in referenceTable.GetEntries()) {
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
                        NPCLoadingLabel.Text = e.UserState.ToString();
                    });

                    bgw.DoWork += async delegate {
                        List<NPCDefinition> npcs = new List<NPCDefinition>();

                        int done = 0;
                        int total = referenceTable.GetEntryTotal() * 128;
                        int percentile = total / 100;

                        bgw.ReportProgress(0, "Loading NPCs");

                        Debug("Loading NPC shit xxxx");

                        foreach (KeyValuePair<int, RSEntry> archive in referenceTable.GetEntries()) {
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
                        ObjectLoadingLabel.Text = e.UserState.ToString();
                    });

                    bgw.DoWork += delegate {
                        List<ObjectDefinition> objects = new List<ObjectDefinition>();

                        int filesPerArchive = referenceTable.GetEntry(referenceTable.GetEntries().Keys.First()).GetValidFileIds().Length;
                        int total = referenceTable.GetEntryTotal() * filesPerArchive;
                        int done = 0;
                        int percentile = total / 100;

                        bgw.ReportProgress(0, "Loading Objects");

                        foreach (KeyValuePair<int, RSEntry> archive in referenceTable.GetEntries()) {
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

                        ObjectListView.SetObjects(objects);
                    };

                    bgw.Disposed += delegate {
                        workers.Remove(bgw);
                    };

                    bgw.RunWorkerAsync();
                    break;

                case RSConstants.TEXTURES:
                    bgw.DoWork += (object? s, DoWorkEventArgs args) => {
                        var manager = new TextureManager(cache);
                        manager.Load();
                        args.Result = manager.Textures;
                    };

                    bgw.RunWorkerCompleted += (_, e) => {
                        if (e.Result is List<TextureDefinition> list)
                            LoadTextures(list);
                    };

                    bgw.RunWorkerAsync();
                    break;

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
            LoadEditorTab(EditorTabControl.SelectedIndex);
        }

        public int GetEditorType() {
            int editorIndex = EditorTabControl.SelectedIndex;
            if (editorIndex > 0 & editorIndex < editorTypes.Length)
                return editorTypes[editorIndex];
            return -1;
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

            //Update the items archive with the new definition
            cache.items[newDefinition.id] = newDefinition;

            //Update the cache definition
            int archiveId = newDefinition.id / 256;
            int entryId = newDefinition.id % 256;

            //Update the entry in the container's archive   
            JagStream newItemStream = newDefinition.Encode();

            cache.WriteEntry(RSConstants.ITEM_DEFINITIONS_INDEX, archiveId, entryId, newItemStream);

            PrintDifferences(newDefinition, currentItem);
        }

        private void ObjectListView_CellEditFinished(object sender, CellEditEventArgs e) {
            ObjectDefinition newDef = (ObjectDefinition) e.RowObject;
            cache.objects[newDef.id] = newDef;

            var refTable = cache.GetReferenceTable(RSConstants.OBJECTS_DEFINITIONS_INDEX);
            int filesPerArchive = refTable.GetEntry(refTable.GetEntries().Keys.First()).GetValidFileIds().Length;
            int archiveId = newDef.id / filesPerArchive;
            int entryId = newDef.id % filesPerArchive;

            JagStream data = newDef.Encode();
            cache.WriteEntry(RSConstants.OBJECTS_DEFINITIONS_INDEX, archiveId, entryId, data);

            PrintDifferences(newDef, currentObject);
        }

        private void NPCListView_CellEditFinished(object sender, CellEditEventArgs e) {
            NPCDefinition newDef = (NPCDefinition) e.RowObject;
            cache.npcs[newDef.id] = newDef;

            int archiveId = newDef.id / 128;
            int entryId = newDef.id % 128;

            JagStream data = newDef.Encode();
            cache.WriteEntry(RSConstants.NPC_DEFINITIONS_INDEX, archiveId, entryId, data);

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
                ItemLoadingLabel.Text = e2.UserState.ToString();
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
            //Saves the data streams
            cache.WriteCache();
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
            currentObject = (ObjectDefinition) ObjectListView.SelectedObject;
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
            FastObjectListView[] olvs = { ItemListView, NPCListView, ObjectListView };
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
                    _modelRenderer.Load(def);
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
                        _modelRenderer.Load(loaded);
                        glControl.Invalidate();
                    }
                    else if (t.IsFaulted) {
                        Debug($"[UI] Error loading model {id}: {t.Exception?.Flatten().InnerException}", LOG_DETAIL.ADVANCED);
                        // Optionally: show a MessageBox or other UI error indicator
                    }
                }, TaskScheduler.FromCurrentSynchronizationContext());
            }
            else {
                Debug("SelectedObject was NOT a ModelReference – doing nothing.", LOG_DETAIL.ADVANCED);
            }
        }

        private void Editor_Resize(object sender, EventArgs e) {
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
            _distance = Math.Clamp(_distance * factor, 1.0, 50.0);
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
            } else if (_activeButton == MouseButtons.Right) {
                Vector3 camPos = CameraPosition();
                Vector3 forward = Vector3.Normalize(_target - camPos);
                Vector3 right = Vector3.Normalize(Vector3.Cross(forward, _up));
                Vector3 realUp = Vector3.Normalize(Vector3.Cross(right, forward));
                _target += (-right * dx + realUp * dy) * PanSpeed;
            }

            _lastMousePos = e.Location;
            glControl.Invalidate();
        }

        protected override void OnFormClosed(FormClosedEventArgs e) {
            _fpsTimer.Stop();
            _modelRenderer.Dispose();
            if (_program != 0)
                GL.DeleteProgram(_program);
            base.OnFormClosed(e);
        }

        private void DummyMethod()
        {
            MessageBox.Show("Dummy action executed.");
        }

        private void LoadTextures(List<TextureDefinition> textures)
        {
            foreach (var tex in textures)
            {
                Bitmap bmp = new Bitmap(100, 100);
                using (var g = Graphics.FromImage(bmp))
                {
                    int colVal = (tex.field1786 != null && tex.field1786.Length > 0) ? tex.field1786[0] : unchecked((int)0xFF777777);
                    Color c = Color.FromArgb(colVal | unchecked((int)0xFF000000));
                    g.Clear(c);
                    using var sf = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center };
                    g.DrawString(tex.id.ToString(), Font, Brushes.White, new RectangleF(0, 0, 100, 100), sf);
                }
                tex.thumb = bmp;
                _textureImageList.Images.Add(tex.id.ToString(), bmp);
            }
            TextureListView.SetObjects(textures);
        }
    }
}