﻿using FlashEditor.cache;
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
        internal cache.RSCache cache;

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

        public bool IsCacheDirSet() {
            if(Properties.Settings.Default.cacheDir == "")
                return false;
            return true;
        }

        public void SetCacheDir() {
            if(folderBrowserDialog1.ShowDialog() == DialogResult.OK) {
                string directory = folderBrowserDialog1.SelectedPath;

                Properties.Settings.Default.cacheDir = directory;
                Properties.Settings.Default.Save();
                Properties.Settings.Default.Reload();
            }
        }

        public string GetCacheDir() {
            while(!IsCacheDirSet())
                SetCacheDir();
            return Properties.Settings.Default.cacheDir;
        }

        private void Editor_Load(object sender, EventArgs e) {
            if(Properties.Settings.Default.cacheDir != "")
                LoadCache(Properties.Settings.Default.cacheDir);
        }

        private void LoadCache() {
            LoadCache(GetCacheDir());
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
                RSFileStore store = new RSFileStore(directory);
                cache = new cache.RSCache(store);
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

            RSReferenceTable refTable;

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
                        case RSConstants.ITEM_DEFINITIONS_INDEX:
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

                                foreach(KeyValuePair<int, RSEntry> archive in refTable.GetEntries()) {
                                    int archiveId = archive.Key;

                                    DebugUtil.Debug("Loading archive " + archive);
                                    for(int file = 0; file < 256; file++) {
                                        try {
                                            ItemDefinition item = cache.GetItemDefinition(archiveId, file);
                                            int itemId = archiveId * 256 + file;
                                            item.SetId(itemId); //Set the item ID
                                            cache.items.Add(itemId, item);
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

                                //Once it's complete, hide the progress bar
                                if(e.ProgressPercentage == 100) {
                                    SpriteProgressBar.Value = 0;
                                    SpriteLoadingLabel.Text = "Status: IDLE";
                                }
                            });

                            bgw.DoWork += async delegate {
                                List<SpriteDefinition> sprites = new List<SpriteDefinition>();
                                refTable = cache.GetReferenceTable(RSConstants.SPRITES_INDEX);

                                int done = 0;
                                int total = refTable.GetEntryTotal();
                                int percentile = total / 100;

                                bgw.ReportProgress(0, "Loading Sprites");
                                foreach(KeyValuePair<int, RSEntry> entry in refTable.GetEntries()) {
                                    DebugUtil.Debug("Loading sprite: " + entry.Key);

                                    SpriteDefinition sprite = cache.GetSprite(entry.Key);
                                    sprite.SetIndex(entry.Key);
                                    sprites.Add(sprite);

                                    done++;

                                    //Only update the progress bar for each 1% completed
                                    if(done % percentile == 0 || done == total)
                                        bgw.ReportProgress((done + 1) * 100 / total);
                                }

                                //Set the root objects for the tree
                                SpriteListView.SetObjects(sprites);

                                bgw.ReportProgress(100);

                                SpriteListView.CanExpandGetter = delegate (object x) {
                                    if(x is SpriteDefinition definition)
                                        if(definition.GetFrameCount() > 1)
                                            return true;
                                    return false;
                                };

                                SpriteListView.ChildrenGetter = delegate (object x) {
                                    //Basically this rewraps the RSBufferedImage (frames) as SpriteDefinitions
                                    return ((SpriteDefinition) x).GetFrames().ConvertAll(y => ((SpriteDefinition) y));
                                };
                            };
                            bgw.RunWorkerAsync();
                            break;
                        case RSConstants.NPC_DEFINITIONS_INDEX:
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

                                foreach(KeyValuePair<int, RSEntry> archive in refTable.GetEntries()) {
                                    int archiveId = archive.Key;

                                    DebugUtil.Debug("Loading archive " + archiveId);
                                    for(int file = 0; file < 128; file++) {
                                        try {
                                            DebugUtil.Debug("Loading file " + file);
                                            NPCDefinition npc = cache.GetNPCDefinition(archiveId, file);
                                            npc.SetId(archiveId * 128 + file); //Set the NPC ID
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

        public int GetEditorType() {
            int editorIndex = EditorTabControl.SelectedIndex;
            if(editorIndex > 0 & editorIndex < editorTypes.Length)
                return editorTypes[editorIndex];
            return -1;
        }

        private void NumericUpDown1_ValueChanged(object sender, EventArgs e) {
            SpriteListView.RowHeight = (int) numericUpDown1.Value;
        }

        private void CheckBox1_CheckedChanged(object sender, EventArgs e) {
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
            string dir = GetCacheDir() + "\\sprites";
            Directory.CreateDirectory(dir);

            foreach(SpriteDefinition sprite in SpriteListView.SelectedObjects)
                if(sprite.thumb != null)
                    sprite.thumb.Save(dir + "\\" + sprite.index + ".png");
        }

        private void SetDirectoryToolStripMenuItem_Click(object sender, EventArgs e) {
            SetCacheDir();
            LoadCache();
        }

        private void OpenDirectoryToolStripMenuItem_Click(object sender, EventArgs e) {
            if(IsCacheDirSet())
                Process.Start(GetCacheDir());
        }

        private void ExportSpriteDatBtn_Click(object sender, EventArgs e) {
            //Nothing yet bro
            MessageBox.Show("Sorry doesn't work");
        }

        //Finished editing a definition
        private void ItemListView_CellEditFinished(object sender, BrightIdeasSoftware.CellEditEventArgs e) {
            //Get the object represented by the ListView
            ItemDefinition newDefinition = (ItemDefinition) e.RowObject;

            //Update the cache definition
            DebugUtil.Debug("cache item total: " + cache.items.Count + ", newdef ID : " + newDefinition.id);
            cache.items[newDefinition.id] = newDefinition;

            int archiveId = newDefinition.id / 256;
            int entryId = newDefinition.id % 256;

            DebugUtil.Debug("Updating items archive " + archiveId + " entry " + entryId + "...");

            //Convert Item Definition to Entry
            RSEntry newEntry = new RSEntry(newDefinition.Encode());

            //Update the entry in the archive
            RSArchive archive = cache.archives[RSConstants.ITEM_DEFINITIONS_INDEX][archiveId];
            archive.UpdateEntry(entryId, newEntry);

            RSContainer container = cache.containers[RSConstants.ITEM_DEFINITIONS_INDEX][archiveId];
            container.UpdateStream(archive.Encode());


            //Encode the archive
            JagStream newArchive = archive.Encode();




            /*
            RSContainer newContainer = cache.containers[Constants.ITEM_DEFINITIONS_INDEX][archiveId];
            newContainer.UpdateStream(archive.Encode());
            JagStream containerStream = newContainer.Encode();
            */

            //Update the reference table
            cache.UpdateReferenceTable(RSConstants.ITEM_DEFINITIONS_INDEX);

            //idkk its shit
            cache.UpdateContainer(RSConstants.ITEM_DEFINITIONS_INDEX);
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
                    Directory.CreateDirectory(RSConstants.CACHE_OUTPUT_DIRECTORY + "/items/");

                    int done = 0;

                    foreach(ItemDefinition def in items) {
                        DebugUtil.Debug("Exporting Item " + def.GetId() + " name is " + def.name);
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
                if(e2.Error != null)
                    DebugUtil.Debug("error: " + e2.Error.ToString());
            };

            itemDumper.RunWorkerAsync();
        }

        private void Button1_Click(object sender, EventArgs e) {
            //Set the alternating row back color
            if(colorDialog1.ShowDialog() == DialogResult.OK)
                ItemListView.AlternateRowBackColor = colorDialog1.Color;

            //Refresh the view
            ItemListView.Refresh();
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
    }
}