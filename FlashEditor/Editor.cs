using static FlashEditor.utils.DebugUtil;
using FlashEditor.cache;
using FlashEditor.cache.sprites;
using System;
using System.Collections.Generic;
using System.IO;
using System.Windows.Forms;
using System.ComponentModel;
using System.Diagnostics;
using FlashEditor.utils;
using System.Drawing;
using BrightIdeasSoftware;

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
            } catch(Exception ex) {
                Debug(ex.StackTrace);
            }
        }

        public void LoadEditorTab(int editorIndex) {
            int type = editorTypes[editorIndex];

            //Don't worry about the main menu
            if(type == -1)
                type = RSConstants.META_INDEX;

            if(cache == null)
                throw new FileNotFoundException("Cache failed to load");

            //Already loaded, no need to reload
            if(loaded[editorIndex])
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
            if(type != RSConstants.META_INDEX)
                referenceTable = cache.GetReferenceTable(type);

            switch(type) {
                case RSConstants.META_INDEX:
                    bgw.DoWork += delegate {
                        List<RSReferenceTable> refTables = new List<RSReferenceTable>();
                        for(int k = 0; k < cache.referenceTables.Length; k++)
                            if(cache.referenceTables[k] != null) {
                                refTables.Add(cache.referenceTables[k]);

                            }

                        RefTableListView.CanExpandGetter = delegate (object x) {
                            if(x is RSReferenceTable definition)
                                if(definition.validArchivesCount > 0)
                                    return true;
                            return false;
                        };

                        RefTableListView.ChildrenGetter = delegate (object x) {
                            //Reference table object
                            RSReferenceTable tbl = (RSReferenceTable) x;

                            //List of archive IDs to be set as the childs
                            List<int> idList = new List<int>();
                            idList.AddRange(tbl.validArchiveIds);

                            return idList;
                        };

                        typeCol.AspectGetter = delegate (object x) {
                            if(x is RSReferenceTable refTbl)
                                return refTbl.type;
                            return (int) x;
                        };

                        formatCol.AspectGetter = delegate (object x) {
                            if(x is RSReferenceTable refTbl)
                                return refTbl.format;
                            return "";
                        };

                        flagsCol.AspectGetter = delegate (object x) {
                            if(x is RSReferenceTable refTbl)
                                return refTbl.flags;
                            return "";
                        };

                        namedCol.AspectGetter = delegate (object x) {
                            if(x is RSReferenceTable refTbl)
                                return refTbl.named;
                            return "";
                        };

                        usesWhirlpoolCol.AspectGetter = delegate (object x) {
                            if(x is RSReferenceTable refTbl)
                                return refTbl.usesWhirlpool;
                            return "";
                        };

                        validArchiveCountCol.AspectGetter = delegate (object x) {
                            if(x is RSReferenceTable refTbl)
                                return refTbl.validArchivesCount;
                            return "";
                        };

                        versionCol.AspectGetter = delegate (object x) {
                            if(x is RSReferenceTable refTbl)
                                return refTbl.version;
                            return "";
                        };



                        /*
                        column2.AspectGetter = delegate (object x) {
                            if(x is Contract) {
                                return ((Contract) x).Value;
                            } else {
                                Double d = (Double)x;
                                return d.ToString();
                            }
                        };*/

                        RefTableListView.SetObjects(refTables);
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

                        //Once it's complete, hide the progress bar
                        /*
                        if(e.ProgressPercentage == 100) {
                            ItemLoadingLabel.Text = "Status: IDLE";
                            ItemProgressBar.Value = 0;
                        }*/
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

                        foreach(KeyValuePair<int, RSEntry> archive in referenceTable.GetEntries()) {
                            int archiveId = archive.Key;

                            Debug("Loading archive " + archive.Key);
                            for(int file = 0; file < 256; file++) {
                                try {
                                    ItemDefinition item = cache.GetItemDefinition(archiveId, file);
                                    int itemId = archiveId * 256 + file;
                                    item.SetId(itemId); //Set the item ID
                                    cache.items.Add(itemId, item);
                                } catch(Exception ex) {
                                    Debug(ex.Message);
                                } finally {
                                    done++;

                                    //Only update the progress bar for each 1% completed
                                    if(done % percentile == 0 || done == total)
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

                        //Once it's complete, hide the progress bar
                        /*
                        if(e.ProgressPercentage == 100) {
                            SpriteProgressBar.Value = 0;
                            SpriteLoadingLabel.Text = "Status: IDLE";
                        }*/
                    });

                    bgw.DoWork += async delegate {
                        Debug(@" _                     _ _                _____            _ _           ");
                        Debug(@"| |                   | (_)              / ____|          (_| |          ");
                        Debug(@"| |     ___   __ _  __| |_ _ __   __ _  | (___  _ __  _ __ _| |_ ___ ___ ");
                        Debug(@"| |    / _ \ / _` |/ _` | | '_ \ / _` |  \___ \| '_ \| '__| | __/ _ / __|");
                        Debug(@"| |___| (_) | (_| | (_| | | | | | (_| |  ____) | |_) | |  | | ||  __\__ \");
                        Debug(@"|______\___/ \__,_|\__,_|_|_| |_|\__, | |_____/| .__/|_|  |_|\__\___|___/");
                        Debug(@"                                  __/ |        | |                       ");
                        Debug(@"                                 |___/         |_|                       ");

                        List<SpriteDefinition> sprites = new List<SpriteDefinition>();

                        int done = 0;
                        int total = referenceTable.GetEntryTotal();
                        int percentile = total / 100;

                        bgw.ReportProgress(0, "Loading " + total + " Sprites");
                        System.Console.WriteLine("Loading " + total + " Sprites");
                        foreach(KeyValuePair<int, RSEntry> entry in referenceTable.GetEntries()) {
                            try {
                                Debug("Loading sprite: " + entry.Key, LOG_DETAIL.ADVANCED);

                                SpriteDefinition sprite = cache.GetSprite(entry.Key);
                                sprite.SetIndex(entry.Key);
                                sprites.Add(sprite);

                                done++;

                                //Only update the progress bar for each 1% completed
                                if(done % percentile == 0 || done == total)
                                    bgw.ReportProgress((done + 1) * 100 / total, "Loaded " + done + "/" + total + " (" + (done + 1) * 100 / total + "%)");
                            } catch(Exception ex) {
                                Debug(ex.Message);
                            }
                        }

                        //Set the root objects for the tree
                        SpriteListView.SetObjects(sprites);

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

                        //SpriteListView.TreeModel.ExpandAll();
                    };
                    bgw.RunWorkerAsync();
                    break;
                case RSConstants.NPC_DEFINITIONS_INDEX:
                    //When an NPC is loaded, update the progress bar
                    bgw.ProgressChanged += new ProgressChangedEventHandler((sender, e) => {
                        NPCProgressBar.Value = e.ProgressPercentage;
                        NPCLoadingLabel.Text = e.UserState.ToString();

                        //Once it's complete, hide the progress bar
                        /*
                        if(e.ProgressPercentage == 100) {
                            NPCLoadingLabel.Text = "Status: IDLE";
                            NPCProgressBar.Value = 0;
                        }*/
                    });

                    bgw.DoWork += async delegate {
                        List<NPCDefinition> npcs = new List<NPCDefinition>();

                        int done = 0;
                        int total = referenceTable.GetEntryTotal() * 128;
                        int percentile = total / 100;

                        bgw.ReportProgress(0, "Loading NPCs");

                        Debug("Loading NPC shit xxxx");

                        foreach(KeyValuePair<int, RSEntry> archive in referenceTable.GetEntries()) {
                            int archiveId = archive.Key;

                            Debug("Loading archive " + archiveId);
                            for(int file = 0; file < 128; file++) {
                                try {
                                    Debug("Loading file " + file);
                                    NPCDefinition npc = cache.GetNPCDefinition(archiveId, file);
                                    npc.SetId(archiveId * 128 + file); //Set the NPC ID
                                    npcs.Add(npc);
                                } catch(Exception ex) {
                                    Debug(ex.Message);
                                } finally {
                                    done++;

                                    //Only update the progress bar for each 1% completed
                                    if(done % percentile == 0 || done == total)
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
            Debug(@"  ______    _ _ _     _____ _                 ");
            Debug(@" |  ____|  | (_) |   |_   _| |                ");
            Debug(@" | |__   __| |_| |_    | | | |_ ___ _ __ ___  ");
            Debug(@" |  __| / _` | | __|   | | | __/ _ \ '_ ` _ \ ");
            Debug(@" | |___| (_| | | |_   _| |_| ||  __/ | | | | |");
            Debug(@" |______\__,_|_|\__| |_____|\__\___|_| |_| |_|");
            Debug(@"                                              ");
            Debug(@"                                              ");

            Debug("Edit Item");

            //Get the object represented by the ListView
            ItemDefinition newDefinition = (ItemDefinition) e.RowObject;

            //Update the items archive with the new definition
            cache.items[newDefinition.id] = newDefinition;

            //Update the cache definition
            int archiveId = newDefinition.id / 256;
            int entryId = newDefinition.id % 256;

            //Update the entry in the container's archive
            cache.WriteEntry(RSConstants.ITEM_DEFINITIONS_INDEX, archiveId, entryId, newDefinition.Encode());
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

                //Once it's complete, hide the progress bar
                /*
                if(e2.ProgressPercentage == 100) {
                    ItemLoadingLabel.Text = "Status: IDLE";
                    ItemProgressBar.Value = 0;
                }*/
            });

            ItemDefinition[] items = new ItemDefinition[ItemListView.SelectedObjects.Count];
            ItemListView.SelectedObjects.CopyTo(items, 0);
            Debug(items[0].name);

            itemDumper.DoWork += delegate {
                if(items.Length > 0) {
                    //Ensures that the directory exists
                    Directory.CreateDirectory(RSConstants.CACHE_OUTPUT_DIRECTORY + "/items/");

                    int done = 0;

                    foreach(ItemDefinition def in items) {
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
                if(e2.Error != null)
                    Debug("error: " + e2.Error.ToString());
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

        private void button4_Click(object sender, EventArgs e) {
            string cacheIn = RSConstants.CACHE_DIRECTORY + "/main_file_cache.";
            string cacheOut = RSConstants.CACHE_OUTPUT_DIRECTORY + "/main_file_cache.";

            //Load the cache into memory
            JagStream dat2in = cache.GetStore().LoadStream(cacheIn + "dat2");
            JagStream dat2out = cache.GetStore().LoadStream(cacheOut + "dat2");

            StreamDifference(dat2in, dat2out, "dat2");

            JagStream idx255in = cache.GetStore().LoadStream(cacheIn + "idx255");
            JagStream idx255out = cache.GetStore().LoadStream(cacheOut + "idx255");

            StreamDifference(idx255in, idx255out, "idx255");

            int count = 0;

            //Count how many idx files exist
            for(int k = 0; k < 254; k++) {
                if(!File.Exists(cacheIn + "idx" + k)) {
                    //If we didn't read any, check the directory is set correctly
                    if(count == 0)
                        throw new FileNotFoundException("No index files could be found");
                    break;
                }
                count++;
            }

            //Load the indexes
            JagStream[] metaStreamsIn = new JagStream[count];
            JagStream[] metaStreamsOut = new JagStream[count];

            //And load in the data
            for(int k = 0; k < count; k++) {
                metaStreamsIn[k] = cache.GetStore().LoadStream(cacheIn + "idx" + k);
                metaStreamsOut[k] = cache.GetStore().LoadStream(cacheOut + "idx" + k);

                StreamDifference(metaStreamsIn[k], metaStreamsOut[k], "idx" + k);
            }
        }

        private void StreamDifference(JagStream stream1, JagStream stream2, string v) {
            //Rudimentary check, fast if the streams are different lengths
            if(stream1.Length != stream2.Length) {
                Debug("Difference in " + v + " data, len: " + (stream1.Length - stream2.Length) + " bytes");
                return;
            }

            //Same length? Check the bytes I guess.
            for(int k = 0; k < stream1.Length; k++) {
                if(stream1.ReadByte() != stream2.ReadByte()) {
                    Debug("Difference in " + v + " @ " + stream1.Position);
                    return;
                }
            }

            Debug("No difference found in " + v);
        }
    }
}