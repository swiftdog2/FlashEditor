using FlashEditor.cache;
using FlashEditor.cache.sprites;
using FlashEditor.cache.util;
using System;
using System.Collections.Generic;
using System.IO;
using System.Windows.Forms;
using System.ComponentModel;
using System.Diagnostics;
using FlashEditor.utils;
using System.Drawing;

namespace FlashEditor {
    public partial class Editor : Form {
        internal RSCache cache;

        //Change the order of the indexes when you change the layout of the editor tabs
        static readonly int[] editorTypes = {
            -1,
            RSConstants.ITEM_DEFINITIONS_INDEX,
            RSConstants.SPRITES_INDEX,
            RSConstants.NPC_DEFINITIONS_INDEX,
            RSConstants.OBJECTS_DEFINITIONS_INDEX,
            RSConstants.INTERFACE_DEFINITIONS_INDEX,
        };
        bool[] loaded = new bool[editorTypes.Length];

        List<BackgroundWorker> workers = new List<BackgroundWorker>();

        public Editor() {
            InitializeComponent();
        }

        public bool isCacheDirSet() {
            if(Properties.Settings.Default.cacheDir == "")
                return false;
            return true;
        }

        public void setCacheDir() {
            if(folderBrowserDialog1.ShowDialog() == DialogResult.OK) {
                string directory = folderBrowserDialog1.SelectedPath;

                Properties.Settings.Default.cacheDir = directory;
                Properties.Settings.Default.Save();
                Properties.Settings.Default.Reload();

            }
        }

        public string getCacheDir() {
            while(!isCacheDirSet())
                setCacheDir();
            return Properties.Settings.Default.cacheDir;
        }

        private void Editor_Load(object sender, EventArgs e) {
            if(Properties.Settings.Default.cacheDir != "")
                LoadCache(Properties.Settings.Default.cacheDir);
        }

        private void LoadCache() {
            LoadCache(getCacheDir());
        }

        private void LoadCache(string directory) {
            Stopwatch sw = new Stopwatch();
            sw.Start();

            foreach(BackgroundWorker worker in workers) {
                if(worker.IsBusy) {
                    DebugConsole.Items.Add("Cannot interrupt background worker at this time.");
                    return;
                }
            }

            //Clear off the previous crap
            workers.Clear();
            DebugConsole.Items.Clear();

            ItemListView.ClearObjects();
            ItemListView.Refresh();

            SpriteListView.ClearObjects();
            SpriteListView.Refresh();

            NPCListView.ClearObjects();
            NPCListView.Refresh();

            //Release the resources of the prior cache if necessary
            if(cache != null)
                cache.GetStore().DisposeAll();

            try {
                //Load the cache and the reference tables
                cache = new RSCache(new RSFileStore(directory));
                sw.Stop();

                DebugConsole.Items.Add("Loaded cache in " + sw.ElapsedMilliseconds + "ms");

                //Refresh the loaded pages
                loaded = new bool[editorTypes.Length];

                //Go back to the main panel
                LoadEditorTab(EditorTabControl.SelectedIndex);
            } catch(Exception ex) {
                DebugConsole.Items.Add(ex.StackTrace);
            }
        }

        public void LoadEditorTab(int editorIndex) {
            int type = editorTypes[editorIndex];

            //Don't worry about the main menu
            if(type == -1)
                return;

            RSReferenceTable table;

            if(cache != null) {
                if(!loaded[editorIndex]) {
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
                    workers.Add(bgw);

                    //Set the reference table to the one we're loading
                    table = cache.GetReferenceTable(type);

                    switch(type) {
                        case RSConstants.ITEM_DEFINITIONS_INDEX:
                            ItemLoadingLabel.Visible = true;
                            ItemProgressBar.Visible = true;

                            //When an item is loaded, update the progress bar
                            bgw.ProgressChanged += new ProgressChangedEventHandler((sender, e) => {
                                ItemProgressBar.Value = e.ProgressPercentage;

                                //Once it's complete, hide the progress bar
                                if(e.ProgressPercentage == 100) {
                                    ItemLoadingLabel.Visible = false;
                                    ItemProgressBar.Visible = false;
                                }
                            });

                            bgw.DoWork += delegate {
                                List<ItemDefinition> items = new List<ItemDefinition>();

                                int done = 0;
                                int total = table.GetEntryTotal() * 256;
                                int percentile = total / 100;

                                bgw.ReportProgress(0, "Loading Items");

                                foreach(KeyValuePair<int, RSEntry> archive in table.GetEntries()) {
                                    int archiveId = archive.Key;

                                    DebugUtil.Debug("Loading archive " + archive);
                                    for(int file = 0; file < 256; file++) {
                                        try {
                                            ItemDefinition item = cache.GetItemDefinition(archiveId, file);
                                            item.setId(archiveId * 256 + file); //Set the item ID
                                            items.Add(item);
                                        } catch(Exception ex) {
                                            DebugUtil.Debug(ex.Message);
                                        } finally {
                                            done++;

                                            //Only update the progress bar for each 1% completed
                                            if(done % percentile == 0 || done == total)
                                                bgw.ReportProgress(done * 100 / total);

                                            DebugUtil.Debug("Done: " + done + ", total: " + total);
                                        }
                                    }
                                }

                                ItemListView.SetObjects(items);
                            };

                            bgw.Disposed += delegate {
                                workers.Remove(bgw);
                            };

                            bgw.RunWorkerAsync();
                            break;
                        case RSConstants.SPRITES_INDEX:
                            SpriteProgressBar.Visible = true;
                            SpriteLoadingLabel.Visible = true;

                            //When a sprite is loaded, update the progress bar
                            bgw.ProgressChanged += new ProgressChangedEventHandler((sender, e) => {
                                SpriteProgressBar.Value = e.ProgressPercentage;

                                //Once it's complete, hide the progress bar
                                if(e.ProgressPercentage == 100) {
                                    SpriteProgressBar.Visible = false;
                                    SpriteLoadingLabel.Visible = false;
                                }
                            });

                            bgw.DoWork += async delegate {
                                List<SpriteDefinition> sprites = new List<SpriteDefinition>();
                                table = cache.GetReferenceTable(RSConstants.SPRITES_INDEX);

                                int done = 0;
                                int total = table.GetEntryTotal();
                                int percentile = total / 100;

                                bgw.ReportProgress(0, "Loading Sprites");
                                foreach(KeyValuePair<int, RSEntry> entry in table.GetEntries()) {
                                    DebugUtil.Debug("Loading sprite: " + entry.Key);

                                    SpriteDefinition sprite = cache.GetSprite(entry.Key);
                                    sprite.setIndex(entry.Key);
                                    sprites.Add(sprite);

                                    done++;

                                    //Only update the progress bar for each 1% completed
                                    if(done % percentile == 0 || done == total)
                                        bgw.ReportProgress((done + 1) * 100 / total);
                                }

                                //Set the root objects for the tree
                                SpriteListView.SetObjects(sprites);

                                bgw.ReportProgress(100);

                                List<Bitmap> frames;

                                /*
                                SpriteListView.CanExpandGetter = delegate (object x) {
                                    if(x is SpriteDefinition)
                                        if(((SpriteDefinition) x).GetFrameCount() > 1)
                                            return true;
                                    return false;
                                };

                                
                                SpriteListView.ChildrenGetter = delegate (object x) {
                                    return frames = ((SpriteDefinition) x).GetFrames().ConvertAll(
                                        y => y.getSprite().Bitmap
                                    );
                                };*/
                            };
                            bgw.RunWorkerAsync();
                            break;
                        case RSConstants.NPC_DEFINITIONS_INDEX:
                            NPCLoadingLabel.Visible = true;
                            NPCProgressBar.Visible = true;

                            //When an NPC is loaded, update the progress bar
                            bgw.ProgressChanged += new ProgressChangedEventHandler((sender, e) => {
                                NPCProgressBar.Value = e.ProgressPercentage;

                                //Once it's complete, hide the progress bar
                                if(e.ProgressPercentage == 100) {
                                    NPCLoadingLabel.Visible = false;
                                    NPCProgressBar.Visible = false;
                                }
                            });

                            bgw.DoWork += async delegate {
                                List<NPCDefinition> npcs = new List<NPCDefinition>();

                                int done = 0;
                                int total = table.GetEntryTotal() * 128;
                                int percentile = total / 100;

                                bgw.ReportProgress(0, "Loading NPCs");

                                DebugUtil.Debug("Loading NPC shit xxxx");

                                foreach(KeyValuePair<int, RSEntry> archive in table.GetEntries()) {
                                    int archiveId = archive.Key;

                                    DebugUtil.Debug("Loading archive " + archiveId);
                                    for(int file = 0; file < 128; file++) {
                                        try {
                                            DebugUtil.Debug("Loading file " + file);
                                            NPCDefinition npc = cache.GetNPCDefinition(archiveId, file);
                                            npc.setId(archiveId * 128 + file); //Set the NPC ID
                                            npcs.Add(npc);
                                        } catch(Exception ex) {
                                            DebugUtil.Debug(ex.Message);
                                        } finally {
                                            done++;

                                            //Only update the progress bar for each 1% completed
                                            if(done % percentile == 0 || done == total)
                                                bgw.ReportProgress(done * 100 / total);

                                            DebugUtil.Debug("Done: " + done + ", total: " + total);
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
                    }
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

        public int getEditorType() {
            int editorIndex = EditorTabControl.SelectedIndex;
            if(editorIndex > 0 & editorIndex < editorTypes.Length)
                return editorTypes[editorIndex];
            return -1;
        }

        private void numericUpDown1_ValueChanged(object sender, EventArgs e) {
            SpriteListView.RowHeight = (int) numericUpDown1.Value;
        }

        private void checkBox1_CheckedChanged(object sender, EventArgs e) {
            if(checkBox1.Checked) {
                //Set the alternating row back color
                if(colorDialog1.ShowDialog() == DialogResult.OK)
                    SpriteListView.AlternateRowBackColor = colorDialog1.Color;
                SpriteListView.UseAlternatingBackColors = true;
            } else {
                //Clear the row back color
                SpriteListView.UseAlternatingBackColors = false;
            }

            //Refresh the view
            SpriteListView.Refresh();
        }

        private void checkBox2_CheckedChanged(object sender, EventArgs e) {
            if(checkBox2.Checked) {
                //Set the alternating row back color
                if(colorDialog1.ShowDialog() == DialogResult.OK)
                    ItemListView.AlternateRowBackColor = colorDialog1.Color;
                ItemListView.UseAlternatingBackColors = true;
            } else {
                //Clear the row back color
                ItemListView.UseAlternatingBackColors = false;
            }

            //Refresh the view
            ItemListView.Refresh();
        }

        private void ExportSpriteBmpBtn_Click(object sender, EventArgs e) {
            string dir = getCacheDir() + "\\sprites";
            Directory.CreateDirectory(dir);

            foreach(SpriteDefinition sprite in SpriteListView.SelectedObjects)
                if(sprite.thumb != null)
                    sprite.thumb.Save(dir + "\\" + sprite.index + ".png");
        }

        private void setDirectoryToolStripMenuItem_Click(object sender, EventArgs e) {
            setCacheDir();
            LoadCache();
        }

        private void openDirectoryToolStripMenuItem_Click(object sender, EventArgs e) {
            if(isCacheDirSet())
                Process.Start(getCacheDir());
        }

        private void saveToolStripMenuItem_Click(object sender, EventArgs e) {
            int selectedTab = EditorTabControl.SelectedIndex;
            int container = editorTypes[selectedTab];
            cache.WriteContainer(container);
        }

        private void ExportSpriteDatBtn_Click(object sender, EventArgs e) {
            //Nothing yet bro
        }
    }
}
