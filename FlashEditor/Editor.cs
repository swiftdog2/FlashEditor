using static FlashEditor.utils.DebugUtil;
using FlashEditor.cache;
using FlashEditor.cache.sprites;
using System;
using System.Collections.Generic;
using System.Collections.Concurrent;
using System.Threading.Tasks;
using System.Threading;
using System.IO;
using System.Windows.Forms;
using System.ComponentModel;
using System.Diagnostics;
using System.Text;
using BrightIdeasSoftware;
using FlashEditor.Tests;

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
            if(string.Equals(Properties.Settings.Default.cacheDir, string.Empty, StringComparison.Ordinal))
                return false;
            return true;
        }

        public void SetCacheDir() {
            if(folderBrowserDialog1.ShowDialog() == DialogResult.OK)
                SetCacheDir(folderBrowserDialog1.SelectedPath);
        }

        public void SetCacheDir(string directory) {
            Properties.Settings.Default.cacheDir = directory;
            Properties.Settings.Default.Save();
            Properties.Settings.Default.Reload();
        }

        public string GetCacheDir() {
            while(!IsCacheDirSet())
                SetCacheDir();
            return Properties.Settings.Default.cacheDir;
        }

        private void Editor_Load(object sender, EventArgs e) {
            if(!string.Equals(Properties.Settings.Default.cacheDir, string.Empty, StringComparison.Ordinal))
                LoadCache(Properties.Settings.Default.cacheDir);
            NPCListView.AlwaysGroupByColumn = npcIdColumn;
            ItemListView.AlwaysGroupByColumn = ItemID;
            SpriteListView.AlwaysGroupByColumn = ID;
        }

        private void LoadCache() {
            workers.ForEach(w => w.CancelAsync());
            loaded = new bool[editorTypes.Length];
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

            if(cache == null) {
                Debug("Cache failed to load");
                return;
            }

            //Already loaded, no need to reload
            if(loaded[editorIndex] && type != RSConstants.META_INDEX)
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
                            if(cache.referenceTables[k] != null)
                                refTables.Add(cache.referenceTables[k]);

                        List<RSContainer> containers = new List<RSContainer>();
                        foreach(KeyValuePair<int, SortedDictionary<int, RSContainer>> types in cache.containers) {
                            int containerType = types.Key;
                            foreach(KeyValuePair<int, RSContainer> container in types.Value) {
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

                        var items = new System.Collections.Concurrent.ConcurrentDictionary<int, ItemDefinition>();

                        System.Threading.Tasks.Parallel.ForEach(referenceTable.GetEntries(), archive => {
                            int archiveId = archive.Key;

                            Debug("Loading archive " + archive.Key);

                            for(int file = 0; file < 256; file++) {
                                try {
                                    ItemDefinition item = cache.GetItemDefinition(archiveId, file);
                                    int itemId = archiveId * 256 + file;
                                    item.SetId(itemId);
                                    items[itemId] = item;
                                } catch(Exception ex) {
                                    Debug(ex.Message);
                                } finally {
                                    done++;

                                    //Only update the progress bar for each 1% completed
                                    if(done % percentile == 0 || done == total)
                                        bgw.ReportProgress((done + 1) * 100 / total, BuildProgressMessage(done, total));                                }

                                int progress = System.Threading.Interlocked.Increment(ref done);

                                if(progress % percentile == 0 || progress == total)
                                    bgw.ReportProgress(progress * 100 / total, "Loaded " + progress + "/" + total + " (" + progress * 100 / total + "%)");
                            }
                        });

                        cache.items = new System.Collections.Generic.SortedDictionary<int, ItemDefinition>(items);

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

                        var sprites = new System.Collections.Concurrent.ConcurrentBag<SpriteDefinition>();

                        int done = 0;
                        int total = referenceTable.GetEntryTotal();
                        int percentile = total / 100;

                        bgw.ReportProgress(0, "Loading " + total + " Sprites");
                        Debug("Loading " + total + " Sprites");

                        System.Threading.Tasks.Parallel.ForEach(referenceTable.GetEntries(), entry => {
                            try {
                                Debug("Loading sprite: " + entry.Key, LOG_DETAIL.ADVANCED);

                                SpriteDefinition sprite = cache.GetSprite(entry.Key);
                                sprite.SetIndex(entry.Key);
                                sprites.Add(sprite);

                                int progress = System.Threading.Interlocked.Increment(ref done);

                                //Only update the progress bar for each 1% completed
                                if(done % percentile == 0 || done == total)
                                    bgw.ReportProgress((done + 1) * 100 / total, BuildProgressMessage(done, total));
                            } catch(Exception ex) {
                                Debug(ex.Message);
                            }
                        });

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

                    bgw.DoWork += delegate {
                        var npcs = new System.Collections.Concurrent.ConcurrentBag<NPCDefinition>();

                        int done = 0;
                        int total = referenceTable.GetEntryTotal() * 128;
                        int percentile = total / 100;

                        bgw.ReportProgress(0, "Loading NPCs");

                        Debug("Loading NPC shit xxxx");

                        System.Threading.Tasks.Parallel.ForEach(referenceTable.GetEntries(), archive => {
                            int archiveId = archive.Key;

                            Debug("Loading archive " + archiveId);
                            for(int file = 0; file < 128; file++) {
                                try {
                                    NPCDefinition npc = cache.GetNPCDefinition(archiveId, file);
                                    npc.SetId(archiveId * 128 + file);
                                    npcs.Add(npc);
                                } catch(Exception ex) {
                                    Debug(ex.Message);
                                } finally {
                                    done++;

                                    //Only update the progress bar for each 1% completed
                                    if(done % percentile == 0 || done == total)
                                        bgw.ReportProgress((done + 1) * 100 / total, BuildProgressMessage(done, total));
                                }

                                int progress = System.Threading.Interlocked.Increment(ref done);

                                if(progress % percentile == 0 || progress == total)
                                    bgw.ReportProgress(progress * 100 / total, "Loaded " + progress + "/" + total + " (" + progress * 100 / total + "%)");
                            }
                        });

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
            if(editorIndex > 0 && editorIndex < editorTypes.Length)
                return editorTypes[editorIndex];
            return -1;
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
        private void ItemListView_CellEditFinished(object sender, CellEditEventArgs e) {
            Debug(@" ______    _ _ _     _____ _                ");
            Debug(@"|  ____|  | (_) |   |_   _| |                ");
            Debug(@"| |__   __| |_| |_    | | | |_ ___ _ __ ___  ");
            Debug(@"|  __| / _` | | __|   | | | __/ _ \ '_ ` _ \ ");
            Debug(@"| |___| (_| | | |_   _| |_| ||  __/ | | | | |");
            Debug(@"|______\__,_|_|\__| |_____|\__\___|_| |_| |_|");
            Debug("Edit Item");

            Debug("itemdef name: " + (currentItem != null ? currentItem.name : "<none>"));
            if(currentItem == null)
                currentItem = ((ItemDefinition) e.RowObject).Clone();

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
            var sb = new StringBuilder();
            foreach(KeyValuePair<int, RSIndex> index in cache.GetStore().indexChannels) {
                sb.Clear();
                sb.Append("idx");
                sb.Append(index.Key);
                diff += AnalyseCache(sb.ToString());
            }

            Debug("Analysis complete, " + (diff > 0 ? diff + " differences found" : "no differences found"));
        }

        public int AnalyseCache(string file) {
            string cacheIn = RSConstants.CACHE_DIRECTORY + "/main_file_cache.";
            string cacheOut = RSConstants.CACHE_OUTPUT_DIRECTORY + "/main_file_cache.";

            try {
                //Load the two caches into a stream
                JagStream inputCache = JagStream.LoadStream(cacheIn + file);
                JagStream outputCache = JagStream.LoadStream(cacheOut + file);
                if(StreamTests.StreamDifference(inputCache, outputCache, file))
                    return 1;
            } catch(Exception ex) {
                Debug(ex.Message);
            }

            return 0;
        }

        internal ItemDefinition currentItem;

        private void ItemListView_CellEditStarting(object sender, CellEditEventArgs e) {
            //cache the item definition prior to editing
            currentItem = (ItemDefinition) ItemListView.SelectedObject;
            currentItem = currentItem.Clone();
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
            TreeListView[] tlvs = {RefTableListView, ContainerListView, SpriteListView };
            FastObjectListView[] olvs = {ItemListView, NPCListView };
            DialogResult result = colorDialog1.ShowDialog();

            foreach(TreeListView tlv in tlvs) {
                tlv.UseAlternatingBackColors = result == DialogResult.OK;
                tlv.AlternateRowBackColor = colorDialog1.Color;
                tlv.Refresh();
            }

            foreach(FastObjectListView olv in olvs) {
                olv.UseAlternatingBackColors = result == DialogResult.OK;
                olv.AlternateRowBackColor = colorDialog1.Color;
                olv.Refresh();
            }

        }

        private void numericUpDown1_ValueChanged_1(object sender, EventArgs e) {
            SpriteListView.RowHeight = (int) numericUpDown1.Value;
        }

        private static string BuildProgressMessage(int done, int total) {
            var sb = new StringBuilder();
            sb.Append("Loaded ");
            sb.Append(done);
            sb.Append('/');
            sb.Append(total);
            sb.Append(" (");
            sb.Append((done + 1) * 100 / total);
            sb.Append('%');
            sb.Append(')');
            return sb.ToString();
        }
    }
}