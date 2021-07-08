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
        internal cache.Cache cache;

        //Change the order of the indexes when you change the layout of the editor tabs
        static readonly int[] editorTypes = {
            -1,
            Constants.ITEM_DEFINITIONS_INDEX,
            Constants.SPRITES_INDEX,
            Constants.NPC_DEFINITIONS_INDEX,
            Constants.OBJECTS_DEFINITIONS_INDEX,
            Constants.INTERFACE_DEFINITIONS_INDEX,
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

            try {
                //Load the cache and the reference tables
                FileStore store = new FileStore(directory);
                cache = new cache.Cache(store);
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

            ReferenceTable refTable;

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
                    refTable = cache.GetReferenceTable(type);

                    switch(type) {
                        case Constants.ITEM_DEFINITIONS_INDEX:
                            //When an item is loaded, update the progress bar
                            bgw.ProgressChanged += new ProgressChangedEventHandler((sender, e) => {
                                ItemProgressBar.Value = e.ProgressPercentage;

                                //Once it's complete, hide the progress bar
                                if(e.ProgressPercentage == 100) {
                                    ItemLoadingLabel.Text = "Status: IDLE";
                                    ItemProgressBar.Value = 0;
                                }
                            });

                            ItemLoadingLabel.Text = "Status: Loading Items";

                            bgw.DoWork += delegate {
                                int done = 0;
                                int total = refTable.GetEntryTotal() * 256;
                                int percentile = total / 100;

                                foreach(KeyValuePair<int, Entry> archive in refTable.GetEntries()) {
                                    int archiveId = archive.Key;

                                    DebugUtil.Debug("Loading archive " + archive);
                                    for(int file = 0; file < 256; file++) {
                                        try {
                                            ItemDefinition item = cache.GetItemDefinition(archiveId, file);
                                            item.setId(archiveId * 256 + file); //Set the item ID
                                            cache.items.Add(item);
                                        } catch(Exception ex) {
                                            DebugUtil.Debug(ex.Message);
                                        } finally {
                                            done++;

                                            //Only update the progress bar for each 1% completed
                                            if(done % percentile == 0 || done == total)
                                                bgw.ReportProgress(done * 100 / total);

                                            //DebugUtil.Debug("Done: " + done + ", total: " + total);
                                        }
                                    }
                                }

                                DebugUtil.Debug("Finished loading " + total + " entries");

                                ItemListView.SetObjects(cache.items);
                            };

                            bgw.Disposed += delegate {
                                workers.Remove(bgw);
                            };

                            bgw.RunWorkerAsync();
                            break;
                        case Constants.SPRITES_INDEX:
                            //When a sprite is loaded, update the progress bar
                            bgw.ProgressChanged += new ProgressChangedEventHandler((sender, e) => {
                                SpriteProgressBar.Value = e.ProgressPercentage;

                                //Once it's complete, hide the progress bar
                                if(e.ProgressPercentage == 100) {
                                    SpriteProgressBar.Value = 0;
                                    SpriteLoadingLabel.Text = "Status: IDLE";
                                }
                            });

                            bgw.DoWork += async delegate {
                                List<SpriteDefinition> sprites = new List<SpriteDefinition>();
                                refTable = cache.GetReferenceTable(Constants.SPRITES_INDEX);

                                int done = 0;
                                int total = refTable.GetEntryTotal();
                                int percentile = total / 100;

                                bgw.ReportProgress(0, "Loading Sprites");
                                foreach(KeyValuePair<int, Entry> entry in refTable.GetEntries()) {
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
                        case Constants.NPC_DEFINITIONS_INDEX:
                            //When an NPC is loaded, update the progress bar
                            bgw.ProgressChanged += new ProgressChangedEventHandler((sender, e) => {
                                NPCProgressBar.Value = e.ProgressPercentage;

                                //Once it's complete, hide the progress bar
                                if(e.ProgressPercentage == 100) {
                                    NPCLoadingLabel.Text = "Status: IDLE";
                                    NPCProgressBar.Value = 0;
                                }
                            });

                            bgw.DoWork += async delegate {
                                List<NPCDefinition> npcs = new List<NPCDefinition>();

                                int done = 0;
                                int total = refTable.GetEntryTotal() * 128;
                                int percentile = total / 100;

                                bgw.ReportProgress(0, "Loading NPCs");

                                DebugUtil.Debug("Loading NPC shit xxxx");

                                foreach(KeyValuePair<int, Entry> archive in refTable.GetEntries()) {
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
            //cache.Encode(container);
        }

        private void ExportSpriteDatBtn_Click(object sender, EventArgs e) {
            //Nothing yet bro
            MessageBox.Show("Sorry doesn't work");
        }

        /// <summary>
        /// This is where the magic gets done.
        /// And I really mean magic, because if this works then I am a literal god.
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>

        private void saveAllToolStripMenuItem_Click(object sender, EventArgs e) {
            //Saves the data streams
            cache.WriteCache();
        }

        //Finished editing a definition
        private void ItemListView_CellEditFinished(object sender, BrightIdeasSoftware.CellEditEventArgs e) {
            //Get the object represented by the ListView
            ItemDefinition newDefinition = (ItemDefinition) e.RowObject;

            //Update the cache definition
            cache.items[newDefinition.id] = newDefinition;

            int archiveId = newDefinition.id / 256;
            int entryId = newDefinition.id % 256;

            DebugUtil.Debug("Updating items archive " + archiveId + " entry " + entryId + "...");

            //Convert Item Definition to Entry
            Entry newEntry = new Entry(newDefinition.Encode());

            //Update the entry in the archive
            Archive archive = cache.archives[Constants.ITEM_DEFINITIONS_INDEX][archiveId];
            archive.UpdateEntry(entryId, newEntry);

            //Update the reference table
            cache.UpdateReferenceTable(Constants.ITEM_DEFINITIONS_INDEX);


            //idkk its shit
            cache.UpdateContainer(Constants.ITEM_DEFINITIONS_INDEX);
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

                //Once it's complete, hide the progress bar
                if(e2.ProgressPercentage == 100) {
                    ItemLoadingLabel.Text = "Status: IDLE";
                    ItemProgressBar.Value = 0;
                }
            });

            ItemDefinition[] items = new ItemDefinition[ItemListView.SelectedObjects.Count];
            ItemListView.SelectedObjects.CopyTo(items, 0);
            DebugUtil.Debug(items[0].name);

            itemDumper.DoWork += delegate {
                if(items.Length > 0) {
                    //Ensures that the directory exists
                    Directory.CreateDirectory(Constants.CACHE_OUTPUT_DIRECTORY + "/items/");

                    int done = 0;

                    foreach(ItemDefinition def in items) {
                        DebugUtil.Debug("Exporting Item " + def.getId() + " name is " + def.name);
                        JagStream.Save(def.Encode(), Constants.CACHE_OUTPUT_DIRECTORY + "/items/" + def.id + ".dat");
                        done++;
                        itemDumper.ReportProgress(done * 100 / items.Length);
                    }
                }
            };

            itemDumper.Disposed += delegate {
                workers.Remove(itemDumper);
            };

            itemDumper.RunWorkerCompleted += (sender2, e2) => {
                if(e2.Error != null)
                    DebugUtil.Debug("error: " + e2.Error.ToString());
            };

            itemDumper.RunWorkerAsync();
        }

        private void button1_Click(object sender, EventArgs e) {
            //Set the alternating row back color
            if(colorDialog1.ShowDialog() == DialogResult.OK)
                ItemListView.AlternateRowBackColor = colorDialog1.Color;

            //Refresh the view
            ItemListView.Refresh();
        }
    }
}