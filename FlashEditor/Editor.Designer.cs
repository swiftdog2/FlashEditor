namespace FlashEditor {
    partial class Editor {
        /// <summary>
        /// Required designer variable.
        /// </summary>
        private System.ComponentModel.IContainer components = null;

        /// <summary>
        /// Clean up any resources being used.
        /// </summary>
        /// <param name="disposing">true if managed resources should be disposed; otherwise, false.</param>
        protected override void Dispose(bool disposing) {
            if(disposing && (components != null)) {
                components.Dispose();
            }
            base.Dispose(disposing);
        }

        #region Windows Form Designer generated code

        /// <summary>
        /// Required method for Designer support - do not modify
        /// the contents of this method with the code editor.
        /// </summary>
        private void InitializeComponent() {
            this.components = new System.ComponentModel.Container();
            System.ComponentModel.ComponentResourceManager resources = new System.ComponentModel.ComponentResourceManager(typeof(Editor));
            this.folderBrowserDialog1 = new System.Windows.Forms.FolderBrowserDialog();
            this.menuStrip1 = new System.Windows.Forms.MenuStrip();
            this.openToolStripMenuItem = new System.Windows.Forms.ToolStripMenuItem();
            this.setDirectoryToolStripMenuItem = new System.Windows.Forms.ToolStripMenuItem();
            this.openDirectoryToolStripMenuItem = new System.Windows.Forms.ToolStripMenuItem();
            this.saveAllToolStripMenuItem = new System.Windows.Forms.ToolStripMenuItem();
            this.viewToolStripMenuItem = new System.Windows.Forms.ToolStripMenuItem();
            this.alternateRowsToolStripMenuItem = new System.Windows.Forms.ToolStripMenuItem();
            this.EditorTabControl = new System.Windows.Forms.TabControl();
            this.Console = new System.Windows.Forms.TabPage();
            this.tabControl1 = new System.Windows.Forms.TabControl();
            this.tabPage1 = new System.Windows.Forms.TabPage();
            this.RefTableListView = new BrightIdeasSoftware.TreeListView();
            this.typeCol = ((BrightIdeasSoftware.OLVColumn)(new BrightIdeasSoftware.OLVColumn()));
            this.formatCol = ((BrightIdeasSoftware.OLVColumn)(new BrightIdeasSoftware.OLVColumn()));
            this.validArchiveCountCol = ((BrightIdeasSoftware.OLVColumn)(new BrightIdeasSoftware.OLVColumn()));
            this.versionCol = ((BrightIdeasSoftware.OLVColumn)(new BrightIdeasSoftware.OLVColumn()));
            this.namedCol = ((BrightIdeasSoftware.OLVColumn)(new BrightIdeasSoftware.OLVColumn()));
            this.usesWhirlpoolCol = ((BrightIdeasSoftware.OLVColumn)(new BrightIdeasSoftware.OLVColumn()));
            this.olvColumn4 = ((BrightIdeasSoftware.OLVColumn)(new BrightIdeasSoftware.OLVColumn()));
            this.olvColumn7 = ((BrightIdeasSoftware.OLVColumn)(new BrightIdeasSoftware.OLVColumn()));
            this.tabPage2 = new System.Windows.Forms.TabPage();
            this.ContainerListView = new BrightIdeasSoftware.TreeListView();
            this.olvColumn1 = ((BrightIdeasSoftware.OLVColumn)(new BrightIdeasSoftware.OLVColumn()));
            this.olvColumn2 = ((BrightIdeasSoftware.OLVColumn)(new BrightIdeasSoftware.OLVColumn()));
            this.CompressCol = ((BrightIdeasSoftware.OLVColumn)(new BrightIdeasSoftware.OLVColumn()));
            this.olvColumn5 = ((BrightIdeasSoftware.OLVColumn)(new BrightIdeasSoftware.OLVColumn()));
            this.olvColumn3 = ((BrightIdeasSoftware.OLVColumn)(new BrightIdeasSoftware.OLVColumn()));
            this.olvColumn6 = ((BrightIdeasSoftware.OLVColumn)(new BrightIdeasSoftware.OLVColumn()));
            this.groupBox1 = new System.Windows.Forms.GroupBox();
            this.button6 = new System.Windows.Forms.Button();
            this.button5 = new System.Windows.Forms.Button();
            this.button4 = new System.Windows.Forms.Button();
            this.ItemEditorTab = new System.Windows.Forms.TabPage();
            this.groupBox4 = new System.Windows.Forms.GroupBox();
            this.ItemLoadingLabel = new System.Windows.Forms.Label();
            this.ItemProgressBar = new System.Windows.Forms.ProgressBar();
            this.ExportItemDatBtn = new System.Windows.Forms.Button();
            this.button9 = new System.Windows.Forms.Button();
            this.ItemListView = new BrightIdeasSoftware.FastObjectListView();
            this.ItemID = ((BrightIdeasSoftware.OLVColumn)(new BrightIdeasSoftware.OLVColumn()));
            this.ItemName = ((BrightIdeasSoftware.OLVColumn)(new BrightIdeasSoftware.OLVColumn()));
            this.InvModel = ((BrightIdeasSoftware.OLVColumn)(new BrightIdeasSoftware.OLVColumn()));
            this.Male1 = ((BrightIdeasSoftware.OLVColumn)(new BrightIdeasSoftware.OLVColumn()));
            this.Male2 = ((BrightIdeasSoftware.OLVColumn)(new BrightIdeasSoftware.OLVColumn()));
            this.Female1 = ((BrightIdeasSoftware.OLVColumn)(new BrightIdeasSoftware.OLVColumn()));
            this.Female2 = ((BrightIdeasSoftware.OLVColumn)(new BrightIdeasSoftware.OLVColumn()));
            this.Rotate1 = ((BrightIdeasSoftware.OLVColumn)(new BrightIdeasSoftware.OLVColumn()));
            this.Rotate2 = ((BrightIdeasSoftware.OLVColumn)(new BrightIdeasSoftware.OLVColumn()));
            this.valueColumn = ((BrightIdeasSoftware.OLVColumn)(new BrightIdeasSoftware.OLVColumn()));
            this.stackableColumn = ((BrightIdeasSoftware.OLVColumn)(new BrightIdeasSoftware.OLVColumn()));
            this.equipSlotColumn = ((BrightIdeasSoftware.OLVColumn)(new BrightIdeasSoftware.OLVColumn()));
            this.equipIdColumn = ((BrightIdeasSoftware.OLVColumn)(new BrightIdeasSoftware.OLVColumn()));
            this.membersOnlyColumn = ((BrightIdeasSoftware.OLVColumn)(new BrightIdeasSoftware.OLVColumn()));
            this.SpriteEditorTab = new System.Windows.Forms.TabPage();
            this.groupBox3 = new System.Windows.Forms.GroupBox();
            this.label1 = new System.Windows.Forms.Label();
            this.numericUpDown1 = new System.Windows.Forms.NumericUpDown();
            this.SpriteLoadingLabel = new System.Windows.Forms.Label();
            this.SpriteProgressBar = new System.Windows.Forms.ProgressBar();
            this.ExportSpriteBmpBtn = new System.Windows.Forms.Button();
            this.ExportSpriteDatBtn = new System.Windows.Forms.Button();
            this.ImportSpriteBtn = new System.Windows.Forms.Button();
            this.SpriteListView = new BrightIdeasSoftware.TreeListView();
            this.ID = ((BrightIdeasSoftware.OLVColumn)(new BrightIdeasSoftware.OLVColumn()));
            this.Frames = ((BrightIdeasSoftware.OLVColumn)(new BrightIdeasSoftware.OLVColumn()));
            this.Width = ((BrightIdeasSoftware.OLVColumn)(new BrightIdeasSoftware.OLVColumn()));
            this.Height = ((BrightIdeasSoftware.OLVColumn)(new BrightIdeasSoftware.OLVColumn()));
            this.Image = ((BrightIdeasSoftware.OLVColumn)(new BrightIdeasSoftware.OLVColumn()));
            this.NPCEditorTab = new System.Windows.Forms.TabPage();
            this.groupBox6 = new System.Windows.Forms.GroupBox();
            this.NPCLoadingLabel = new System.Windows.Forms.Label();
            this.NPCProgressBar = new System.Windows.Forms.ProgressBar();
            this.button2 = new System.Windows.Forms.Button();
            this.button3 = new System.Windows.Forms.Button();
            this.NPCListView = new BrightIdeasSoftware.FastObjectListView();
            this.npcIdColumn = ((BrightIdeasSoftware.OLVColumn)(new BrightIdeasSoftware.OLVColumn()));
            this.nameColumn = ((BrightIdeasSoftware.OLVColumn)(new BrightIdeasSoftware.OLVColumn()));
            this.sizeColumn = ((BrightIdeasSoftware.OLVColumn)(new BrightIdeasSoftware.OLVColumn()));
            this.levelColumn = ((BrightIdeasSoftware.OLVColumn)(new BrightIdeasSoftware.OLVColumn()));
            this.olvColumn10 = ((BrightIdeasSoftware.OLVColumn)(new BrightIdeasSoftware.OLVColumn()));
            this.olvColumn9 = ((BrightIdeasSoftware.OLVColumn)(new BrightIdeasSoftware.OLVColumn()));
            this.olvColumn11 = ((BrightIdeasSoftware.OLVColumn)(new BrightIdeasSoftware.OLVColumn()));
            this.rotationColumn = ((BrightIdeasSoftware.OLVColumn)(new BrightIdeasSoftware.OLVColumn()));
            this.ambientColumn = ((BrightIdeasSoftware.OLVColumn)(new BrightIdeasSoftware.OLVColumn()));
            this.contrastColumn = ((BrightIdeasSoftware.OLVColumn)(new BrightIdeasSoftware.OLVColumn()));
            this.attackCursorColumn = ((BrightIdeasSoftware.OLVColumn)(new BrightIdeasSoftware.OLVColumn()));
            this.visiblePriorityColumn = ((BrightIdeasSoftware.OLVColumn)(new BrightIdeasSoftware.OLVColumn()));
            this.ObjectEditorTab = new System.Windows.Forms.TabPage();
            this.groupBox5 = new System.Windows.Forms.GroupBox();
            this.ObjectLoadingLabel = new System.Windows.Forms.Label();
            this.ObjectProgressBar = new System.Windows.Forms.ProgressBar();
            this.button7 = new System.Windows.Forms.Button();
            this.button8 = new System.Windows.Forms.Button();
            this.ObjectListView = new BrightIdeasSoftware.FastObjectListView();
            this.objectIdColumn = ((BrightIdeasSoftware.OLVColumn)(new BrightIdeasSoftware.OLVColumn()));
            this.objectNameColumn = ((BrightIdeasSoftware.OLVColumn)(new BrightIdeasSoftware.OLVColumn()));
            this.sizeXColumn = ((BrightIdeasSoftware.OLVColumn)(new BrightIdeasSoftware.OLVColumn()));
            this.sizeYColumn = ((BrightIdeasSoftware.OLVColumn)(new BrightIdeasSoftware.OLVColumn()));
            this.walkableColumn = ((BrightIdeasSoftware.OLVColumn)(new BrightIdeasSoftware.OLVColumn()));
            this.clippedColumn = ((BrightIdeasSoftware.OLVColumn)(new BrightIdeasSoftware.OLVColumn()));
            this.ambientSoundColumn = ((BrightIdeasSoftware.OLVColumn)(new BrightIdeasSoftware.OLVColumn()));
            this.morphVarbitColumn = ((BrightIdeasSoftware.OLVColumn)(new BrightIdeasSoftware.OLVColumn()));
            this.InterfaceEditorTab = new System.Windows.Forms.TabPage();
            this.eventLog1 = new System.Diagnostics.EventLog();
            this.colorDialog1 = new System.Windows.Forms.ColorDialog();
            this.EditButton = ((BrightIdeasSoftware.OLVColumn)(new BrightIdeasSoftware.OLVColumn()));
            this.menuStrip1.SuspendLayout();
            this.EditorTabControl.SuspendLayout();
            this.Console.SuspendLayout();
            this.tabControl1.SuspendLayout();
            this.tabPage1.SuspendLayout();
            ((System.ComponentModel.ISupportInitialize)(this.RefTableListView)).BeginInit();
            this.tabPage2.SuspendLayout();
            ((System.ComponentModel.ISupportInitialize)(this.ContainerListView)).BeginInit();
            this.groupBox1.SuspendLayout();
            this.ItemEditorTab.SuspendLayout();
            this.groupBox4.SuspendLayout();
            ((System.ComponentModel.ISupportInitialize)(this.ItemListView)).BeginInit();
            this.SpriteEditorTab.SuspendLayout();
            this.groupBox3.SuspendLayout();
            ((System.ComponentModel.ISupportInitialize)(this.numericUpDown1)).BeginInit();
            ((System.ComponentModel.ISupportInitialize)(this.SpriteListView)).BeginInit();
            this.NPCEditorTab.SuspendLayout();
            this.groupBox6.SuspendLayout();
            ((System.ComponentModel.ISupportInitialize)(this.NPCListView)).BeginInit();
            this.ObjectEditorTab.SuspendLayout();
            this.groupBox5.SuspendLayout();
            ((System.ComponentModel.ISupportInitialize)(this.ObjectListView)).BeginInit();
            ((System.ComponentModel.ISupportInitialize)(this.eventLog1)).BeginInit();
            this.SuspendLayout();
            // 
            // menuStrip1
            // 
            this.menuStrip1.BackColor = System.Drawing.Color.White;
            this.menuStrip1.Font = new System.Drawing.Font("Consolas", 11.25F);
            this.menuStrip1.Items.AddRange(new System.Windows.Forms.ToolStripItem[] {
            this.openToolStripMenuItem,
            this.viewToolStripMenuItem});
            this.menuStrip1.Location = new System.Drawing.Point(0, 0);
            this.menuStrip1.Name = "menuStrip1";
            this.menuStrip1.Size = new System.Drawing.Size(1153, 30);
            this.menuStrip1.TabIndex = 1;
            this.menuStrip1.Text = "menuStrip1";
            // 
            // openToolStripMenuItem
            // 
            this.openToolStripMenuItem.DropDownItems.AddRange(new System.Windows.Forms.ToolStripItem[] {
            this.saveAllToolStripMenuItem,
            this.setDirectoryToolStripMenuItem,
            this.openDirectoryToolStripMenuItem});
            this.openToolStripMenuItem.Font = new System.Drawing.Font("Consolas", 14.25F);
            this.openToolStripMenuItem.Name = "openToolStripMenuItem";
            this.openToolStripMenuItem.Size = new System.Drawing.Size(72, 26);
            this.openToolStripMenuItem.Text = "Cache";
            // 
            // setDirectoryToolStripMenuItem
            // 
            this.setDirectoryToolStripMenuItem.Name = "setDirectoryToolStripMenuItem";
            this.setDirectoryToolStripMenuItem.Size = new System.Drawing.Size(220, 26);
            this.setDirectoryToolStripMenuItem.Text = "Set Directory";
            this.setDirectoryToolStripMenuItem.Click += new System.EventHandler(this.SetDirectoryToolStripMenuItem_Click);
            // 
            // openDirectoryToolStripMenuItem
            // 
            this.openDirectoryToolStripMenuItem.Name = "openDirectoryToolStripMenuItem";
            this.openDirectoryToolStripMenuItem.Size = new System.Drawing.Size(220, 26);
            this.openDirectoryToolStripMenuItem.Text = "Open Directory";
            this.openDirectoryToolStripMenuItem.Click += new System.EventHandler(this.OpenDirectoryToolStripMenuItem_Click);
            // 
            // saveAllToolStripMenuItem
            // 
            this.saveAllToolStripMenuItem.Name = "saveAllToolStripMenuItem";
            this.saveAllToolStripMenuItem.Size = new System.Drawing.Size(220, 26);
            this.saveAllToolStripMenuItem.Text = "Save All";
            this.saveAllToolStripMenuItem.Click += new System.EventHandler(this.saveAllToolStripMenuItem_Click_1);
            // 
            // viewToolStripMenuItem
            // 
            this.viewToolStripMenuItem.DropDownItems.AddRange(new System.Windows.Forms.ToolStripItem[] {
            this.alternateRowsToolStripMenuItem});
            this.viewToolStripMenuItem.Font = new System.Drawing.Font("Consolas", 14.25F);
            this.viewToolStripMenuItem.Name = "viewToolStripMenuItem";
            this.viewToolStripMenuItem.Size = new System.Drawing.Size(62, 26);
            this.viewToolStripMenuItem.Text = "View";
            // 
            // alternateRowsToolStripMenuItem
            // 
            this.alternateRowsToolStripMenuItem.Name = "alternateRowsToolStripMenuItem";
            this.alternateRowsToolStripMenuItem.Size = new System.Drawing.Size(290, 26);
            this.alternateRowsToolStripMenuItem.Text = "Alternate Row Colours";
            this.alternateRowsToolStripMenuItem.Click += new System.EventHandler(this.alternateRowsToolStripMenuItem_Click);
            // 
            // EditorTabControl
            // 
            this.EditorTabControl.Anchor = ((System.Windows.Forms.AnchorStyles)((((System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Bottom) 
            | System.Windows.Forms.AnchorStyles.Left) 
            | System.Windows.Forms.AnchorStyles.Right)));
            this.EditorTabControl.Controls.Add(this.Console);
            this.EditorTabControl.Controls.Add(this.ItemEditorTab);
            this.EditorTabControl.Controls.Add(this.SpriteEditorTab);
            this.EditorTabControl.Controls.Add(this.NPCEditorTab);
            this.EditorTabControl.Controls.Add(this.ObjectEditorTab);
            this.EditorTabControl.Controls.Add(this.InterfaceEditorTab);
            this.EditorTabControl.Font = new System.Drawing.Font("Consolas", 12F, System.Drawing.FontStyle.Regular, System.Drawing.GraphicsUnit.Point, ((byte)(0)));
            this.EditorTabControl.Location = new System.Drawing.Point(12, 41);
            this.EditorTabControl.Name = "EditorTabControl";
            this.EditorTabControl.SelectedIndex = 0;
            this.EditorTabControl.Size = new System.Drawing.Size(1129, 570);
            this.EditorTabControl.TabIndex = 3;
            this.EditorTabControl.SelectedIndexChanged += new System.EventHandler(this.EditorTabControl_SelectedIndexChanged);
            // 
            // Console
            // 
            this.Console.BackColor = System.Drawing.Color.White;
            this.Console.Controls.Add(this.tabControl1);
            this.Console.Controls.Add(this.groupBox1);
            this.Console.Location = new System.Drawing.Point(4, 28);
            this.Console.Name = "Console";
            this.Console.Size = new System.Drawing.Size(1121, 538);
            this.Console.TabIndex = 2;
            this.Console.Text = "Meta";
            // 
            // tabControl1
            // 
            this.tabControl1.Anchor = ((System.Windows.Forms.AnchorStyles)((((System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Bottom) 
            | System.Windows.Forms.AnchorStyles.Left) 
            | System.Windows.Forms.AnchorStyles.Right)));
            this.tabControl1.Controls.Add(this.tabPage1);
            this.tabControl1.Controls.Add(this.tabPage2);
            this.tabControl1.Font = new System.Drawing.Font("Consolas", 11.25F);
            this.tabControl1.Location = new System.Drawing.Point(-2, -2);
            this.tabControl1.Name = "tabControl1";
            this.tabControl1.SelectedIndex = 0;
            this.tabControl1.Size = new System.Drawing.Size(871, 544);
            this.tabControl1.TabIndex = 7;
            // 
            // tabPage1
            // 
            this.tabPage1.Controls.Add(this.RefTableListView);
            this.tabPage1.Location = new System.Drawing.Point(4, 27);
            this.tabPage1.Name = "tabPage1";
            this.tabPage1.Padding = new System.Windows.Forms.Padding(3);
            this.tabPage1.Size = new System.Drawing.Size(863, 513);
            this.tabPage1.TabIndex = 0;
            this.tabPage1.Text = "Reference Tables";
            this.tabPage1.UseVisualStyleBackColor = true;
            // 
            // RefTableListView
            // 
            this.RefTableListView.AllColumns.Add(this.typeCol);
            this.RefTableListView.AllColumns.Add(this.formatCol);
            this.RefTableListView.AllColumns.Add(this.validArchiveCountCol);
            this.RefTableListView.AllColumns.Add(this.versionCol);
            this.RefTableListView.AllColumns.Add(this.namedCol);
            this.RefTableListView.AllColumns.Add(this.usesWhirlpoolCol);
            this.RefTableListView.AllColumns.Add(this.olvColumn4);
            this.RefTableListView.AllColumns.Add(this.olvColumn7);
            this.RefTableListView.Anchor = ((System.Windows.Forms.AnchorStyles)((((System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Bottom) 
            | System.Windows.Forms.AnchorStyles.Left) 
            | System.Windows.Forms.AnchorStyles.Right)));
            this.RefTableListView.CellEditUseWholeCell = false;
            this.RefTableListView.Columns.AddRange(new System.Windows.Forms.ColumnHeader[] {
            this.typeCol,
            this.formatCol,
            this.validArchiveCountCol,
            this.versionCol,
            this.namedCol,
            this.usesWhirlpoolCol,
            this.olvColumn4,
            this.olvColumn7});
            this.RefTableListView.Cursor = System.Windows.Forms.Cursors.Default;
            this.RefTableListView.Font = new System.Drawing.Font("Consolas", 11.25F);
            this.RefTableListView.HideSelection = false;
            this.RefTableListView.Location = new System.Drawing.Point(-2, -1);
            this.RefTableListView.Name = "RefTableListView";
            this.RefTableListView.ShowGroups = false;
            this.RefTableListView.Size = new System.Drawing.Size(866, 515);
            this.RefTableListView.TabIndex = 0;
            this.RefTableListView.UseCompatibleStateImageBehavior = false;
            this.RefTableListView.View = System.Windows.Forms.View.Details;
            this.RefTableListView.VirtualMode = true;
            // 
            // typeCol
            // 
            this.typeCol.AspectName = "type";
            this.typeCol.Text = "Type";
            this.typeCol.Width = 80;
            // 
            // formatCol
            // 
            this.formatCol.AspectName = "format";
            this.formatCol.Text = "Format";
            this.formatCol.Width = 82;
            // 
            // validArchiveCountCol
            // 
            this.validArchiveCountCol.AspectName = "validArchivesCount";
            this.validArchiveCountCol.Text = "Archives";
            this.validArchiveCountCol.Width = 80;
            // 
            // versionCol
            // 
            this.versionCol.AspectName = "version";
            this.versionCol.AspectToStringFormat = "";
            this.versionCol.Text = "Version";
            this.versionCol.Width = 80;
            // 
            // namedCol
            // 
            this.namedCol.AspectName = "entryHashes";
            this.namedCol.CheckBoxes = true;
            this.namedCol.Text = "Hashes";
            this.namedCol.Width = 80;
            // 
            // usesWhirlpoolCol
            // 
            this.usesWhirlpoolCol.AspectName = "usesWhirlpool";
            this.usesWhirlpoolCol.CheckBoxes = true;
            this.usesWhirlpoolCol.Text = "Whirlpool";
            this.usesWhirlpoolCol.Width = 85;
            // 
            // olvColumn4
            // 
            this.olvColumn4.AspectName = "sizes";
            this.olvColumn4.CheckBoxes = true;
            this.olvColumn4.Text = "Sizes";
            this.olvColumn4.Width = 85;
            // 
            // olvColumn7
            // 
            this.olvColumn7.AspectName = "hasIdentifiers";
            this.olvColumn7.CheckBoxes = true;
            this.olvColumn7.FillsFreeSpace = true;
            this.olvColumn7.Text = "Identifiers";
            this.olvColumn7.Width = 100;
            // 
            // tabPage2
            // 
            this.tabPage2.Controls.Add(this.ContainerListView);
            this.tabPage2.Location = new System.Drawing.Point(4, 27);
            this.tabPage2.Name = "tabPage2";
            this.tabPage2.Padding = new System.Windows.Forms.Padding(3);
            this.tabPage2.Size = new System.Drawing.Size(863, 513);
            this.tabPage2.TabIndex = 1;
            this.tabPage2.Text = "Containers";
            this.tabPage2.UseVisualStyleBackColor = true;
            // 
            // ContainerListView
            // 
            this.ContainerListView.AllColumns.Add(this.olvColumn1);
            this.ContainerListView.AllColumns.Add(this.olvColumn2);
            this.ContainerListView.AllColumns.Add(this.CompressCol);
            this.ContainerListView.AllColumns.Add(this.olvColumn5);
            this.ContainerListView.AllColumns.Add(this.olvColumn3);
            this.ContainerListView.AllColumns.Add(this.olvColumn6);
            this.ContainerListView.Anchor = ((System.Windows.Forms.AnchorStyles)((((System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Bottom) 
            | System.Windows.Forms.AnchorStyles.Left) 
            | System.Windows.Forms.AnchorStyles.Right)));
            this.ContainerListView.CellEditUseWholeCell = false;
            this.ContainerListView.Columns.AddRange(new System.Windows.Forms.ColumnHeader[] {
            this.olvColumn1,
            this.olvColumn2,
            this.CompressCol,
            this.olvColumn5,
            this.olvColumn3,
            this.olvColumn6});
            this.ContainerListView.Cursor = System.Windows.Forms.Cursors.Default;
            this.ContainerListView.Font = new System.Drawing.Font("Consolas", 11.25F, System.Drawing.FontStyle.Regular, System.Drawing.GraphicsUnit.Point, ((byte)(0)));
            this.ContainerListView.HideSelection = false;
            this.ContainerListView.Location = new System.Drawing.Point(-2, -1);
            this.ContainerListView.Name = "ContainerListView";
            this.ContainerListView.ShowGroups = false;
            this.ContainerListView.Size = new System.Drawing.Size(866, 516);
            this.ContainerListView.TabIndex = 1;
            this.ContainerListView.UseCompatibleStateImageBehavior = false;
            this.ContainerListView.View = System.Windows.Forms.View.Details;
            this.ContainerListView.VirtualMode = true;
            // 
            // olvColumn1
            // 
            this.olvColumn1.AspectName = "type";
            this.olvColumn1.IsEditable = false;
            this.olvColumn1.Text = "Type";
            this.olvColumn1.Width = 67;
            // 
            // olvColumn2
            // 
            this.olvColumn2.AspectName = "id";
            this.olvColumn2.IsEditable = false;
            this.olvColumn2.Text = "ID";
            this.olvColumn2.Width = 74;
            // 
            // CompressCol
            // 
            this.CompressCol.AspectName = "compressionType";
            this.CompressCol.AspectToStringFormat = "";
            this.CompressCol.IsEditable = false;
            this.CompressCol.Text = "Compression";
            this.CompressCol.Width = 112;
            // 
            // olvColumn5
            // 
            this.olvColumn5.AspectName = "version";
            this.olvColumn5.Hideable = false;
            this.olvColumn5.IsEditable = false;
            this.olvColumn5.Text = "Version";
            this.olvColumn5.Width = 90;
            // 
            // olvColumn3
            // 
            this.olvColumn3.AspectName = "length";
            this.olvColumn3.IsEditable = false;
            this.olvColumn3.Text = "Length";
            this.olvColumn3.Width = 79;
            // 
            // olvColumn6
            // 
            this.olvColumn6.AspectName = "decompressedLength";
            this.olvColumn6.FillsFreeSpace = true;
            this.olvColumn6.IsEditable = false;
            this.olvColumn6.Text = "Decompressed Length";
            this.olvColumn6.Width = 183;
            // 
            // groupBox1
            // 
            this.groupBox1.Anchor = ((System.Windows.Forms.AnchorStyles)(((System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Bottom) 
            | System.Windows.Forms.AnchorStyles.Right)));
            this.groupBox1.BackColor = System.Drawing.Color.White;
            this.groupBox1.Controls.Add(this.button6);
            this.groupBox1.Controls.Add(this.button5);
            this.groupBox1.Controls.Add(this.button4);
            this.groupBox1.FlatStyle = System.Windows.Forms.FlatStyle.Popup;
            this.groupBox1.Font = new System.Drawing.Font("Consolas", 12.25F);
            this.groupBox1.Location = new System.Drawing.Point(875, 3);
            this.groupBox1.Name = "groupBox1";
            this.groupBox1.Size = new System.Drawing.Size(243, 532);
            this.groupBox1.TabIndex = 5;
            this.groupBox1.TabStop = false;
            this.groupBox1.Text = "Cache Tools";
            // 
            // button6
            // 
            this.button6.BackColor = System.Drawing.Color.White;
            this.button6.Location = new System.Drawing.Point(6, 111);
            this.button6.Name = "button6";
            this.button6.Size = new System.Drawing.Size(231, 35);
            this.button6.TabIndex = 4;
            this.button6.Text = "Reload Output";
            this.button6.UseVisualStyleBackColor = false;
            this.button6.Click += new System.EventHandler(this.button6_Click);
            // 
            // button5
            // 
            this.button5.BackColor = System.Drawing.Color.White;
            this.button5.Location = new System.Drawing.Point(6, 70);
            this.button5.Name = "button5";
            this.button5.Size = new System.Drawing.Size(231, 35);
            this.button5.TabIndex = 3;
            this.button5.Text = "Reload Original";
            this.button5.UseVisualStyleBackColor = false;
            this.button5.Click += new System.EventHandler(this.button5_Click);
            // 
            // button4
            // 
            this.button4.BackColor = System.Drawing.Color.White;
            this.button4.Location = new System.Drawing.Point(6, 29);
            this.button4.Name = "button4";
            this.button4.Size = new System.Drawing.Size(231, 35);
            this.button4.TabIndex = 2;
            this.button4.Text = "Compare to Output";
            this.button4.UseVisualStyleBackColor = false;
            this.button4.Click += new System.EventHandler(this.button4_Click);
            // 
            // ItemEditorTab
            // 
            this.ItemEditorTab.Controls.Add(this.groupBox4);
            this.ItemEditorTab.Controls.Add(this.ItemListView);
            this.ItemEditorTab.Location = new System.Drawing.Point(4, 28);
            this.ItemEditorTab.Name = "ItemEditorTab";
            this.ItemEditorTab.Padding = new System.Windows.Forms.Padding(3);
            this.ItemEditorTab.Size = new System.Drawing.Size(1121, 538);
            this.ItemEditorTab.TabIndex = 0;
            this.ItemEditorTab.Text = "Items";
            this.ItemEditorTab.UseVisualStyleBackColor = true;
            // 
            // groupBox4
            // 
            this.groupBox4.Anchor = ((System.Windows.Forms.AnchorStyles)(((System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Bottom) 
            | System.Windows.Forms.AnchorStyles.Right)));
            this.groupBox4.BackColor = System.Drawing.Color.White;
            this.groupBox4.Controls.Add(this.ItemLoadingLabel);
            this.groupBox4.Controls.Add(this.ItemProgressBar);
            this.groupBox4.Controls.Add(this.ExportItemDatBtn);
            this.groupBox4.Controls.Add(this.button9);
            this.groupBox4.Font = new System.Drawing.Font("Consolas", 12.25F);
            this.groupBox4.Location = new System.Drawing.Point(857, 3);
            this.groupBox4.Name = "groupBox4";
            this.groupBox4.Size = new System.Drawing.Size(243, 523);
            this.groupBox4.TabIndex = 11;
            this.groupBox4.TabStop = false;
            this.groupBox4.Text = "Editor Controls";
            // 
            // ItemLoadingLabel
            // 
            this.ItemLoadingLabel.Anchor = ((System.Windows.Forms.AnchorStyles)((System.Windows.Forms.AnchorStyles.Bottom | System.Windows.Forms.AnchorStyles.Right)));
            this.ItemLoadingLabel.AutoSize = true;
            this.ItemLoadingLabel.BackColor = System.Drawing.Color.Transparent;
            this.ItemLoadingLabel.Font = new System.Drawing.Font("Consolas", 12F, System.Drawing.FontStyle.Regular, System.Drawing.GraphicsUnit.Point, ((byte)(0)));
            this.ItemLoadingLabel.Location = new System.Drawing.Point(3, 439);
            this.ItemLoadingLabel.Name = "ItemLoadingLabel";
            this.ItemLoadingLabel.Size = new System.Drawing.Size(126, 19);
            this.ItemLoadingLabel.TabIndex = 12;
            this.ItemLoadingLabel.Text = "Loading Items";
            this.ItemLoadingLabel.TextAlign = System.Drawing.ContentAlignment.MiddleCenter;
            // 
            // ItemProgressBar
            // 
            this.ItemProgressBar.Anchor = ((System.Windows.Forms.AnchorStyles)((System.Windows.Forms.AnchorStyles.Bottom | System.Windows.Forms.AnchorStyles.Right)));
            this.ItemProgressBar.ForeColor = System.Drawing.Color.DarkRed;
            this.ItemProgressBar.Location = new System.Drawing.Point(0, 461);
            this.ItemProgressBar.Name = "ItemProgressBar";
            this.ItemProgressBar.Size = new System.Drawing.Size(240, 62);
            this.ItemProgressBar.TabIndex = 8;
            // 
            // ExportItemDatBtn
            // 
            this.ExportItemDatBtn.BackColor = System.Drawing.Color.White;
            this.ExportItemDatBtn.Location = new System.Drawing.Point(6, 70);
            this.ExportItemDatBtn.Name = "ExportItemDatBtn";
            this.ExportItemDatBtn.Size = new System.Drawing.Size(228, 35);
            this.ExportItemDatBtn.TabIndex = 1;
            this.ExportItemDatBtn.Text = "Export Selected (.dat)";
            this.ExportItemDatBtn.UseVisualStyleBackColor = false;
            this.ExportItemDatBtn.Click += new System.EventHandler(this.ExportItemDatBtn_Click);
            // 
            // button9
            // 
            this.button9.BackColor = System.Drawing.Color.White;
            this.button9.Location = new System.Drawing.Point(6, 29);
            this.button9.Name = "button9";
            this.button9.Size = new System.Drawing.Size(228, 35);
            this.button9.TabIndex = 0;
            this.button9.Text = "Import Item";
            this.button9.UseVisualStyleBackColor = false;
            // 
            // ItemListView
            // 
            this.ItemListView.AllColumns.Add(this.ItemID);
            this.ItemListView.AllColumns.Add(this.ItemName);
            this.ItemListView.AllColumns.Add(this.InvModel);
            this.ItemListView.AllColumns.Add(this.Male1);
            this.ItemListView.AllColumns.Add(this.Male2);
            this.ItemListView.AllColumns.Add(this.Female1);
            this.ItemListView.AllColumns.Add(this.Female2);
            this.ItemListView.AllColumns.Add(this.Rotate1);
            this.ItemListView.AllColumns.Add(this.Rotate2);
            this.ItemListView.AllColumns.Add(this.valueColumn);
            this.ItemListView.AllColumns.Add(this.stackableColumn);
            this.ItemListView.AllColumns.Add(this.equipSlotColumn);
            this.ItemListView.AllColumns.Add(this.equipIdColumn);
            this.ItemListView.AllColumns.Add(this.membersOnlyColumn);
            this.ItemListView.AlternateRowBackColor = System.Drawing.Color.FromArgb(((int)(((byte)(192)))), ((int)(((byte)(255)))), ((int)(((byte)(192)))));
            this.ItemListView.Anchor = ((System.Windows.Forms.AnchorStyles)((((System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Bottom) 
            | System.Windows.Forms.AnchorStyles.Left) 
            | System.Windows.Forms.AnchorStyles.Right)));
            this.ItemListView.BackColor = System.Drawing.Color.White;
            this.ItemListView.CellEditActivation = BrightIdeasSoftware.ObjectListView.CellEditActivateMode.DoubleClick;
            this.ItemListView.CellEditUseWholeCell = false;
            this.ItemListView.CheckBoxes = true;
            this.ItemListView.Columns.AddRange(new System.Windows.Forms.ColumnHeader[] {
            this.ItemID,
            this.ItemName,
            this.InvModel,
            this.Male1,
            this.Male2,
            this.Female1,
            this.Female2,
            this.Rotate1,
            this.Rotate2,
            this.valueColumn,
            this.stackableColumn,
            this.equipSlotColumn,
            this.equipIdColumn,
            this.membersOnlyColumn});
            this.ItemListView.Cursor = System.Windows.Forms.Cursors.Default;
            this.ItemListView.Font = new System.Drawing.Font("Consolas", 11.25F, System.Drawing.FontStyle.Regular, System.Drawing.GraphicsUnit.Point, ((byte)(0)));
            this.ItemListView.FullRowSelect = true;
            this.ItemListView.GridLines = true;
            this.ItemListView.HideSelection = false;
            this.ItemListView.Location = new System.Drawing.Point(-2, -2);
            this.ItemListView.Name = "ItemListView";
            this.ItemListView.RowHeight = 10;
            this.ItemListView.ShowGroups = false;
            this.ItemListView.ShowImagesOnSubItems = true;
            this.ItemListView.Size = new System.Drawing.Size(853, 535);
            this.ItemListView.TabIndex = 14;
            this.ItemListView.UseAlternatingBackColors = true;
            this.ItemListView.UseCompatibleStateImageBehavior = false;
            this.ItemListView.UseFilterIndicator = true;
            this.ItemListView.View = System.Windows.Forms.View.Details;
            this.ItemListView.VirtualMode = true;
            this.ItemListView.CellEditFinished += new BrightIdeasSoftware.CellEditEventHandler(this.ItemListView_CellEditFinished);
            this.ItemListView.CellEditStarting += new BrightIdeasSoftware.CellEditEventHandler(this.ItemListView_CellEditStarting);
            // 
            // ItemID
            // 
            this.ItemID.AspectName = "id";
            this.ItemID.Groupable = false;
            this.ItemID.IsEditable = false;
            this.ItemID.Searchable = false;
            this.ItemID.Text = "ID";
            this.ItemID.Width = 78;
            // 
            // ItemName
            // 
            this.ItemName.AspectName = "name";
            this.ItemName.Text = "Name";
            this.ItemName.Width = 191;
            // 
            // InvModel
            // 
            this.InvModel.AspectName = "inventoryModelId";
            this.InvModel.Text = "InvModel";
            this.InvModel.Width = 78;
            // 
            // Male1
            // 
            this.Male1.AspectName = "maleWearModel1";
            this.Male1.Text = "ManModel1";
            this.Male1.Width = 87;
            // 
            // Male2
            // 
            this.Male2.AspectName = "maleWearModel2";
            this.Male2.Text = "ManModel2";
            this.Male2.Width = 88;
            // 
            // Female1
            // 
            this.Female1.AspectName = "femaleWearModel1";
            this.Female1.Text = "Female1";
            this.Female1.Width = 72;
            // 
            // Female2
            // 
            this.Female2.AspectName = "femaleWearModel2";
            this.Female2.Text = "Female2";
            this.Female2.Width = 69;
            // 
            // Rotate1
            // 
            this.Rotate1.AspectName = "modelRotation1";
            this.Rotate1.Text = "Rotate1";
            this.Rotate1.Width = 73;
            // 
            // Rotate2
            // 
            this.Rotate2.AspectName = "modelRotation2";
            this.Rotate2.Text = "Rotate2";
            this.Rotate2.Width = 71;
            //
            // valueColumn
            //
            this.valueColumn.AspectName = "value";
            this.valueColumn.Text = "Value";
            this.valueColumn.Width = 80;
            //
            // stackableColumn
            //
            this.stackableColumn.AspectName = "stackable";
            this.stackableColumn.Text = "Stack";
            this.stackableColumn.Width = 60;
            //
            // equipSlotColumn
            //
            this.equipSlotColumn.AspectName = "equipSlotId";
            this.equipSlotColumn.Text = "Slot";
            this.equipSlotColumn.Width = 60;
            //
            // equipIdColumn
            //
            this.equipIdColumn.AspectName = "equipId";
            this.equipIdColumn.Text = "EquipId";
            this.equipIdColumn.Width = 70;
            //
            // membersOnlyColumn
            //
            this.membersOnlyColumn.AspectName = "membersOnly";
            this.membersOnlyColumn.CheckBoxes = true;
            this.membersOnlyColumn.Text = "Members";
            this.membersOnlyColumn.Width = 80;
            // 
            // SpriteEditorTab
            // 
            this.SpriteEditorTab.Controls.Add(this.groupBox3);
            this.SpriteEditorTab.Controls.Add(this.SpriteListView);
            this.SpriteEditorTab.Location = new System.Drawing.Point(4, 28);
            this.SpriteEditorTab.Name = "SpriteEditorTab";
            this.SpriteEditorTab.Padding = new System.Windows.Forms.Padding(3);
            this.SpriteEditorTab.Size = new System.Drawing.Size(1121, 538);
            this.SpriteEditorTab.TabIndex = 1;
            this.SpriteEditorTab.Text = "Sprites";
            this.SpriteEditorTab.UseVisualStyleBackColor = true;
            // 
            // groupBox3
            // 
            this.groupBox3.Anchor = ((System.Windows.Forms.AnchorStyles)(((System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Bottom) 
            | System.Windows.Forms.AnchorStyles.Right)));
            this.groupBox3.BackColor = System.Drawing.Color.White;
            this.groupBox3.Controls.Add(this.label1);
            this.groupBox3.Controls.Add(this.numericUpDown1);
            this.groupBox3.Controls.Add(this.SpriteLoadingLabel);
            this.groupBox3.Controls.Add(this.SpriteProgressBar);
            this.groupBox3.Controls.Add(this.ExportSpriteBmpBtn);
            this.groupBox3.Controls.Add(this.ExportSpriteDatBtn);
            this.groupBox3.Controls.Add(this.ImportSpriteBtn);
            this.groupBox3.Font = new System.Drawing.Font("Consolas", 12.25F);
            this.groupBox3.Location = new System.Drawing.Point(875, 3);
            this.groupBox3.Name = "groupBox3";
            this.groupBox3.Size = new System.Drawing.Size(243, 532);
            this.groupBox3.TabIndex = 9;
            this.groupBox3.TabStop = false;
            this.groupBox3.Text = "Editor Controls";
            // 
            // label1
            // 
            this.label1.AutoSize = true;
            this.label1.Font = new System.Drawing.Font("Consolas", 12F, System.Drawing.FontStyle.Regular, System.Drawing.GraphicsUnit.Point, ((byte)(0)));
            this.label1.Location = new System.Drawing.Point(9, 36);
            this.label1.Name = "label1";
            this.label1.Size = new System.Drawing.Size(99, 19);
            this.label1.TabIndex = 11;
            this.label1.Text = "Row Height";
            // 
            // numericUpDown1
            // 
            this.numericUpDown1.Location = new System.Drawing.Point(114, 34);
            this.numericUpDown1.Maximum = new decimal(new int[] {
            256,
            0,
            0,
            0});
            this.numericUpDown1.Name = "numericUpDown1";
            this.numericUpDown1.Size = new System.Drawing.Size(120, 27);
            this.numericUpDown1.TabIndex = 12;
            this.numericUpDown1.Value = new decimal(new int[] {
            21,
            0,
            0,
            0});
            this.numericUpDown1.ValueChanged += new System.EventHandler(this.numericUpDown1_ValueChanged_1);
            // 
            // SpriteLoadingLabel
            // 
            this.SpriteLoadingLabel.Anchor = ((System.Windows.Forms.AnchorStyles)((System.Windows.Forms.AnchorStyles.Bottom | System.Windows.Forms.AnchorStyles.Right)));
            this.SpriteLoadingLabel.AutoSize = true;
            this.SpriteLoadingLabel.BackColor = System.Drawing.Color.White;
            this.SpriteLoadingLabel.Font = new System.Drawing.Font("Consolas", 12F, System.Drawing.FontStyle.Regular, System.Drawing.GraphicsUnit.Point, ((byte)(0)));
            this.SpriteLoadingLabel.Location = new System.Drawing.Point(3, 448);
            this.SpriteLoadingLabel.Name = "SpriteLoadingLabel";
            this.SpriteLoadingLabel.Size = new System.Drawing.Size(144, 19);
            this.SpriteLoadingLabel.TabIndex = 10;
            this.SpriteLoadingLabel.Text = "Loading Sprites";
            this.SpriteLoadingLabel.TextAlign = System.Drawing.ContentAlignment.MiddleCenter;
            // 
            // SpriteProgressBar
            // 
            this.SpriteProgressBar.Anchor = ((System.Windows.Forms.AnchorStyles)(((System.Windows.Forms.AnchorStyles.Bottom | System.Windows.Forms.AnchorStyles.Left) 
            | System.Windows.Forms.AnchorStyles.Right)));
            this.SpriteProgressBar.BackColor = System.Drawing.Color.White;
            this.SpriteProgressBar.ForeColor = System.Drawing.Color.DarkRed;
            this.SpriteProgressBar.Location = new System.Drawing.Point(0, 470);
            this.SpriteProgressBar.Name = "SpriteProgressBar";
            this.SpriteProgressBar.Size = new System.Drawing.Size(240, 62);
            this.SpriteProgressBar.TabIndex = 8;
            // 
            // ExportSpriteBmpBtn
            // 
            this.ExportSpriteBmpBtn.BackColor = System.Drawing.Color.White;
            this.ExportSpriteBmpBtn.Location = new System.Drawing.Point(6, 149);
            this.ExportSpriteBmpBtn.Name = "ExportSpriteBmpBtn";
            this.ExportSpriteBmpBtn.Size = new System.Drawing.Size(228, 35);
            this.ExportSpriteBmpBtn.TabIndex = 4;
            this.ExportSpriteBmpBtn.Text = "Export Selected (.png)";
            this.ExportSpriteBmpBtn.UseVisualStyleBackColor = false;
            this.ExportSpriteBmpBtn.Click += new System.EventHandler(this.ExportSpriteBmpBtn_Click);
            // 
            // ExportSpriteDatBtn
            // 
            this.ExportSpriteDatBtn.BackColor = System.Drawing.Color.White;
            this.ExportSpriteDatBtn.Location = new System.Drawing.Point(6, 108);
            this.ExportSpriteDatBtn.Name = "ExportSpriteDatBtn";
            this.ExportSpriteDatBtn.Size = new System.Drawing.Size(228, 35);
            this.ExportSpriteDatBtn.TabIndex = 1;
            this.ExportSpriteDatBtn.Text = "Export Selected (.dat)";
            this.ExportSpriteDatBtn.UseVisualStyleBackColor = false;
            this.ExportSpriteDatBtn.Click += new System.EventHandler(this.ExportSpriteDatBtn_Click);
            // 
            // ImportSpriteBtn
            // 
            this.ImportSpriteBtn.BackColor = System.Drawing.Color.White;
            this.ImportSpriteBtn.Location = new System.Drawing.Point(6, 67);
            this.ImportSpriteBtn.Name = "ImportSpriteBtn";
            this.ImportSpriteBtn.Size = new System.Drawing.Size(228, 35);
            this.ImportSpriteBtn.TabIndex = 0;
            this.ImportSpriteBtn.Text = "Import Sprite";
            this.ImportSpriteBtn.UseVisualStyleBackColor = false;
            // 
            // SpriteListView
            // 
            this.SpriteListView.AllColumns.Add(this.ID);
            this.SpriteListView.AllColumns.Add(this.Frames);
            this.SpriteListView.AllColumns.Add(this.Width);
            this.SpriteListView.AllColumns.Add(this.Height);
            this.SpriteListView.AllColumns.Add(this.Image);
            this.SpriteListView.AlternateRowBackColor = System.Drawing.Color.White;
            this.SpriteListView.Anchor = ((System.Windows.Forms.AnchorStyles)((((System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Bottom) 
            | System.Windows.Forms.AnchorStyles.Left) 
            | System.Windows.Forms.AnchorStyles.Right)));
            this.SpriteListView.CellEditUseWholeCell = false;
            this.SpriteListView.Columns.AddRange(new System.Windows.Forms.ColumnHeader[] {
            this.ID,
            this.Frames,
            this.Width,
            this.Height,
            this.Image});
            this.SpriteListView.Font = new System.Drawing.Font("Consolas", 11.25F);
            this.SpriteListView.FullRowSelect = true;
            this.SpriteListView.GridLines = true;
            this.SpriteListView.HideSelection = false;
            this.SpriteListView.Location = new System.Drawing.Point(-2, -2);
            this.SpriteListView.Name = "SpriteListView";
            this.SpriteListView.RowHeight = 20;
            this.SpriteListView.ShowGroups = false;
            this.SpriteListView.Size = new System.Drawing.Size(871, 544);
            this.SpriteListView.TabIndex = 6;
            this.SpriteListView.TintSortColumn = true;
            this.SpriteListView.UseAlternatingBackColors = true;
            this.SpriteListView.UseCompatibleStateImageBehavior = false;
            this.SpriteListView.UseFiltering = true;
            this.SpriteListView.UseHotControls = false;
            this.SpriteListView.View = System.Windows.Forms.View.Details;
            this.SpriteListView.VirtualMode = true;
            // 
            // ID
            // 
            this.ID.AspectName = "index";
            this.ID.Text = "ID";
            this.ID.Width = 59;
            // 
            // Frames
            // 
            this.Frames.AspectName = "frameCount";
            this.Frames.Text = "Frames";
            this.Frames.Width = 67;
            // 
            // Width
            // 
            this.Width.AspectName = "width";
            this.Width.Text = "Width";
            this.Width.Width = 61;
            // 
            // Height
            // 
            this.Height.AspectName = "height";
            this.Height.Text = "Height";
            this.Height.Width = 67;
            // 
            // Image
            // 
            this.Image.AspectName = "";
            this.Image.FillsFreeSpace = true;
            this.Image.ImageAspectName = "thumb";
            this.Image.IsEditable = false;
            this.Image.Searchable = false;
            this.Image.Sortable = false;
            this.Image.Text = "Sprite";
            this.Image.Width = 350;
            // 
            // NPCEditorTab
            // 
            this.NPCEditorTab.Controls.Add(this.groupBox6);
            this.NPCEditorTab.Controls.Add(this.NPCListView);
            this.NPCEditorTab.Location = new System.Drawing.Point(4, 28);
            this.NPCEditorTab.Name = "NPCEditorTab";
            this.NPCEditorTab.Size = new System.Drawing.Size(1121, 538);
            this.NPCEditorTab.TabIndex = 3;
            this.NPCEditorTab.Text = "NPCs";
            this.NPCEditorTab.UseVisualStyleBackColor = true;
            // 
            // groupBox6
            // 
            this.groupBox6.Anchor = ((System.Windows.Forms.AnchorStyles)(((System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Bottom) 
            | System.Windows.Forms.AnchorStyles.Right)));
            this.groupBox6.BackColor = System.Drawing.Color.White;
            this.groupBox6.Controls.Add(this.NPCLoadingLabel);
            this.groupBox6.Controls.Add(this.NPCProgressBar);
            this.groupBox6.Controls.Add(this.button2);
            this.groupBox6.Controls.Add(this.button3);
            this.groupBox6.Font = new System.Drawing.Font("Consolas", 12.25F);
            this.groupBox6.Location = new System.Drawing.Point(857, 3);
            this.groupBox6.Name = "groupBox6";
            this.groupBox6.Size = new System.Drawing.Size(243, 523);
            this.groupBox6.TabIndex = 16;
            this.groupBox6.TabStop = false;
            this.groupBox6.Text = "Editor Controls";
            // 
            // NPCLoadingLabel
            // 
            this.NPCLoadingLabel.Anchor = ((System.Windows.Forms.AnchorStyles)((System.Windows.Forms.AnchorStyles.Bottom | System.Windows.Forms.AnchorStyles.Right)));
            this.NPCLoadingLabel.AutoSize = true;
            this.NPCLoadingLabel.BackColor = System.Drawing.Color.Transparent;
            this.NPCLoadingLabel.Font = new System.Drawing.Font("Consolas", 12F, System.Drawing.FontStyle.Regular, System.Drawing.GraphicsUnit.Point, ((byte)(0)));
            this.NPCLoadingLabel.Location = new System.Drawing.Point(3, 439);
            this.NPCLoadingLabel.Name = "NPCLoadingLabel";
            this.NPCLoadingLabel.Size = new System.Drawing.Size(117, 19);
            this.NPCLoadingLabel.TabIndex = 17;
            this.NPCLoadingLabel.Text = "Loading NPCs";
            this.NPCLoadingLabel.TextAlign = System.Drawing.ContentAlignment.MiddleCenter;
            // 
            // NPCProgressBar
            // 
            this.NPCProgressBar.Anchor = ((System.Windows.Forms.AnchorStyles)((System.Windows.Forms.AnchorStyles.Bottom | System.Windows.Forms.AnchorStyles.Right)));
            this.NPCProgressBar.ForeColor = System.Drawing.Color.DarkRed;
            this.NPCProgressBar.Location = new System.Drawing.Point(0, 461);
            this.NPCProgressBar.Name = "NPCProgressBar";
            this.NPCProgressBar.Size = new System.Drawing.Size(240, 62);
            this.NPCProgressBar.TabIndex = 8;
            // 
            // button2
            // 
            this.button2.BackColor = System.Drawing.Color.White;
            this.button2.Location = new System.Drawing.Point(6, 70);
            this.button2.Name = "button2";
            this.button2.Size = new System.Drawing.Size(231, 35);
            this.button2.TabIndex = 1;
            this.button2.Text = "Export Selected (.dat)";
            this.button2.UseVisualStyleBackColor = false;
            // 
            // button3
            // 
            this.button3.BackColor = System.Drawing.Color.White;
            this.button3.Location = new System.Drawing.Point(6, 29);
            this.button3.Name = "button3";
            this.button3.Size = new System.Drawing.Size(231, 35);
            this.button3.TabIndex = 0;
            this.button3.Text = "Import NPC";
            this.button3.UseVisualStyleBackColor = false;
            // 
            // NPCListView
            // 
            this.NPCListView.AllColumns.Add(this.npcIdColumn);
            this.NPCListView.AllColumns.Add(this.nameColumn);
            this.NPCListView.AllColumns.Add(this.sizeColumn);
            this.NPCListView.AllColumns.Add(this.levelColumn);
            this.NPCListView.AllColumns.Add(this.olvColumn10);
            this.NPCListView.AllColumns.Add(this.olvColumn9);
            this.NPCListView.AllColumns.Add(this.olvColumn11);
            this.NPCListView.AllColumns.Add(this.rotationColumn);
            this.NPCListView.AllColumns.Add(this.ambientColumn);
            this.NPCListView.AllColumns.Add(this.contrastColumn);
            this.NPCListView.AllColumns.Add(this.attackCursorColumn);
            this.NPCListView.AllColumns.Add(this.visiblePriorityColumn);
            this.NPCListView.Anchor = ((System.Windows.Forms.AnchorStyles)((((System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Bottom) 
            | System.Windows.Forms.AnchorStyles.Left) 
            | System.Windows.Forms.AnchorStyles.Right)));
            this.NPCListView.BackColor = System.Drawing.Color.White;
            this.NPCListView.CellEditUseWholeCell = false;
            this.NPCListView.Columns.AddRange(new System.Windows.Forms.ColumnHeader[] {
            this.npcIdColumn,
            this.nameColumn,
            this.sizeColumn,
            this.levelColumn,
            this.olvColumn10,
            this.olvColumn9,
            this.olvColumn11,
            this.rotationColumn,
            this.ambientColumn,
            this.contrastColumn,
            this.attackCursorColumn,
            this.visiblePriorityColumn});
            this.NPCListView.Cursor = System.Windows.Forms.Cursors.Default;
            this.NPCListView.Font = new System.Drawing.Font("Consolas", 11.25F, System.Drawing.FontStyle.Regular, System.Drawing.GraphicsUnit.Point, ((byte)(0)));
            this.NPCListView.FullRowSelect = true;
            this.NPCListView.GridLines = true;
            this.NPCListView.HideSelection = false;
            this.NPCListView.Location = new System.Drawing.Point(-2, -2);
            this.NPCListView.Name = "NPCListView";
            this.NPCListView.RowHeight = 10;
            this.NPCListView.ShowGroups = false;
            this.NPCListView.ShowImagesOnSubItems = true;
            this.NPCListView.Size = new System.Drawing.Size(853, 535);
            this.NPCListView.TabIndex = 18;
            this.NPCListView.TintSortColumn = true;
            this.NPCListView.UseCompatibleStateImageBehavior = false;
            this.NPCListView.View = System.Windows.Forms.View.Details;
            this.NPCListView.VirtualMode = true;
            // 
            // npcIdColumn
            // 
            this.npcIdColumn.AspectName = "id";
            this.npcIdColumn.Groupable = false;
            this.npcIdColumn.Searchable = false;
            this.npcIdColumn.Text = "ID";
            this.npcIdColumn.Width = 78;
            // 
            // nameColumn
            // 
            this.nameColumn.AspectName = "name";
            this.nameColumn.Text = "Name";
            this.nameColumn.Width = 191;
            // 
            // sizeColumn
            // 
            this.sizeColumn.AspectName = "size";
            this.sizeColumn.Text = "Size";
            this.sizeColumn.Width = 67;
            // 
            // levelColumn
            // 
            this.levelColumn.AspectName = "level";
            this.levelColumn.Text = "Level";
            this.levelColumn.Width = 58;
            // 
            // olvColumn10
            // 
            this.olvColumn10.AspectName = "renderTypeID";
            this.olvColumn10.Text = "Render";
            this.olvColumn10.Width = 73;
            // 
            // olvColumn9
            // 
            this.olvColumn9.AspectName = "clickable";
            this.olvColumn9.CheckBoxes = true;
            this.olvColumn9.Text = "Clickable";
            this.olvColumn9.Width = 93;
            // 
            // olvColumn11
            // 
            this.olvColumn11.AspectName = "drawMinimapDot";
            this.olvColumn11.CheckBoxes = true;
            this.olvColumn11.Text = "Minidot";
            this.olvColumn11.Width = 77;
            //
            // rotationColumn
            //
            this.rotationColumn.AspectName = "rotation";
            this.rotationColumn.Text = "Rotation";
            this.rotationColumn.Width = 80;
            //
            // ambientColumn
            //
            this.ambientColumn.AspectName = "ambient";
            this.ambientColumn.Text = "Ambient";
            this.ambientColumn.Width = 70;
            //
            // contrastColumn
            //
            this.contrastColumn.AspectName = "contrast";
            this.contrastColumn.Text = "Contrast";
            this.contrastColumn.Width = 70;
            //
            // attackCursorColumn
            //
            this.attackCursorColumn.AspectName = "attackOpCursor";
            this.attackCursorColumn.Text = "AtkCursor";
            this.attackCursorColumn.Width = 90;
            //
            // visiblePriorityColumn
            //
            this.visiblePriorityColumn.AspectName = "visiblePriority";
            this.visiblePriorityColumn.CheckBoxes = true;
            this.visiblePriorityColumn.Text = "VisiblePrio";
            this.visiblePriorityColumn.Width = 90;
            // 
            // ObjectEditorTab
            // 
            this.ObjectEditorTab.Controls.Add(this.groupBox5);
            this.ObjectEditorTab.Controls.Add(this.ObjectListView);
            this.ObjectEditorTab.Location = new System.Drawing.Point(4, 28);
            this.ObjectEditorTab.Name = "ObjectEditorTab";
            this.ObjectEditorTab.Size = new System.Drawing.Size(1121, 538);
            this.ObjectEditorTab.TabIndex = 4;
            this.ObjectEditorTab.Text = "Objects";
            this.ObjectEditorTab.UseVisualStyleBackColor = true;
            //
            // groupBox5
            //
            this.groupBox5.Anchor = ((System.Windows.Forms.AnchorStyles)(((System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Bottom)
            | System.Windows.Forms.AnchorStyles.Right)));
            this.groupBox5.BackColor = System.Drawing.Color.White;
            this.groupBox5.Controls.Add(this.ObjectLoadingLabel);
            this.groupBox5.Controls.Add(this.ObjectProgressBar);
            this.groupBox5.Controls.Add(this.button7);
            this.groupBox5.Controls.Add(this.button8);
            this.groupBox5.Font = new System.Drawing.Font("Consolas", 12.25F);
            this.groupBox5.Location = new System.Drawing.Point(857, 3);
            this.groupBox5.Name = "groupBox5";
            this.groupBox5.Size = new System.Drawing.Size(243, 523);
            this.groupBox5.TabIndex = 19;
            this.groupBox5.TabStop = false;
            this.groupBox5.Text = "Editor Controls";
            //
            // ObjectLoadingLabel
            //
            this.ObjectLoadingLabel.Anchor = ((System.Windows.Forms.AnchorStyles)((System.Windows.Forms.AnchorStyles.Bottom | System.Windows.Forms.AnchorStyles.Right)));
            this.ObjectLoadingLabel.AutoSize = true;
            this.ObjectLoadingLabel.BackColor = System.Drawing.Color.Transparent;
            this.ObjectLoadingLabel.Font = new System.Drawing.Font("Consolas", 12F);
            this.ObjectLoadingLabel.Location = new System.Drawing.Point(3, 439);
            this.ObjectLoadingLabel.Name = "ObjectLoadingLabel";
            this.ObjectLoadingLabel.Size = new System.Drawing.Size(144, 19);
            this.ObjectLoadingLabel.TabIndex = 17;
            this.ObjectLoadingLabel.Text = "Loading Objects";
            this.ObjectLoadingLabel.TextAlign = System.Drawing.ContentAlignment.MiddleCenter;
            //
            // ObjectProgressBar
            //
            this.ObjectProgressBar.Anchor = ((System.Windows.Forms.AnchorStyles)((System.Windows.Forms.AnchorStyles.Bottom | System.Windows.Forms.AnchorStyles.Right)));
            this.ObjectProgressBar.ForeColor = System.Drawing.Color.DarkRed;
            this.ObjectProgressBar.Location = new System.Drawing.Point(0, 461);
            this.ObjectProgressBar.Name = "ObjectProgressBar";
            this.ObjectProgressBar.Size = new System.Drawing.Size(240, 62);
            this.ObjectProgressBar.TabIndex = 8;
            //
            // button7
            //
            this.button7.BackColor = System.Drawing.Color.White;
            this.button7.Location = new System.Drawing.Point(6, 70);
            this.button7.Name = "button7";
            this.button7.Size = new System.Drawing.Size(231, 35);
            this.button7.TabIndex = 1;
            this.button7.Text = "Export Selected (.dat)";
            this.button7.UseVisualStyleBackColor = false;
            //
            // button8
            //
            this.button8.BackColor = System.Drawing.Color.White;
            this.button8.Location = new System.Drawing.Point(6, 29);
            this.button8.Name = "button8";
            this.button8.Size = new System.Drawing.Size(231, 35);
            this.button8.TabIndex = 0;
            this.button8.Text = "Import Object";
            this.button8.UseVisualStyleBackColor = false;
            //
            // ObjectListView
            //
            this.ObjectListView.AllColumns.Add(this.objectIdColumn);
            this.ObjectListView.AllColumns.Add(this.objectNameColumn);
            this.ObjectListView.AllColumns.Add(this.sizeXColumn);
            this.ObjectListView.AllColumns.Add(this.sizeYColumn);
            this.ObjectListView.AllColumns.Add(this.walkableColumn);
            this.ObjectListView.AllColumns.Add(this.clippedColumn);
            this.ObjectListView.AllColumns.Add(this.ambientSoundColumn);
            this.ObjectListView.AllColumns.Add(this.morphVarbitColumn);
            this.ObjectListView.Anchor = ((System.Windows.Forms.AnchorStyles)((((System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Bottom)
            | System.Windows.Forms.AnchorStyles.Left)
            | System.Windows.Forms.AnchorStyles.Right)));
            this.ObjectListView.BackColor = System.Drawing.Color.White;
            this.ObjectListView.CellEditUseWholeCell = false;
            this.ObjectListView.Columns.AddRange(new System.Windows.Forms.ColumnHeader[] {
            this.objectIdColumn,
            this.objectNameColumn,
            this.sizeXColumn,
            this.sizeYColumn,
            this.walkableColumn,
            this.clippedColumn,
            this.ambientSoundColumn,
            this.morphVarbitColumn});
            this.ObjectListView.Cursor = System.Windows.Forms.Cursors.Default;
            this.ObjectListView.Font = new System.Drawing.Font("Consolas", 11.25F);
            this.ObjectListView.FullRowSelect = true;
            this.ObjectListView.GridLines = true;
            this.ObjectListView.HideSelection = false;
            this.ObjectListView.Location = new System.Drawing.Point(-2, -2);
            this.ObjectListView.Name = "ObjectListView";
            this.ObjectListView.RowHeight = 10;
            this.ObjectListView.ShowGroups = false;
            this.ObjectListView.ShowImagesOnSubItems = true;
            this.ObjectListView.Size = new System.Drawing.Size(853, 535);
            this.ObjectListView.TabIndex = 18;
            this.ObjectListView.UseCompatibleStateImageBehavior = false;
            this.ObjectListView.View = System.Windows.Forms.View.Details;
            this.ObjectListView.VirtualMode = true;
            this.ObjectListView.CellEditFinished += new BrightIdeasSoftware.CellEditEventHandler(this.ObjectListView_CellEditFinished);
            this.ObjectListView.CellEditStarting += new BrightIdeasSoftware.CellEditEventHandler(this.ObjectListView_CellEditStarting);
            //
            // objectIdColumn
            //
            this.objectIdColumn.AspectName = "id";
            this.objectIdColumn.Groupable = false;
            this.objectIdColumn.Searchable = false;
            this.objectIdColumn.Text = "ID";
            this.objectIdColumn.Width = 78;
            //
            // objectNameColumn
            //
            this.objectNameColumn.AspectName = "name";
            this.objectNameColumn.Text = "Name";
            this.objectNameColumn.Width = 191;
            //
            // sizeXColumn
            //
            this.sizeXColumn.AspectName = "sizeX";
            this.sizeXColumn.Text = "SizeX";
            this.sizeXColumn.Width = 60;
            //
            // sizeYColumn
            //
            this.sizeYColumn.AspectName = "sizeY";
            this.sizeYColumn.Text = "SizeY";
            this.sizeYColumn.Width = 60;
            //
            // walkableColumn
            //
            this.walkableColumn.AspectName = "walkable";
            this.walkableColumn.CheckBoxes = true;
            this.walkableColumn.Text = "Walkable";
            this.walkableColumn.Width = 80;
            //
            // clippedColumn
            //
            this.clippedColumn.AspectName = "isClipped";
            this.clippedColumn.CheckBoxes = true;
            this.clippedColumn.Text = "Clipped";
            this.clippedColumn.Width = 70;
            //
            // ambientSoundColumn
            //
            this.ambientSoundColumn.AspectName = "ambientSoundId";
            this.ambientSoundColumn.Text = "Sound";
            this.ambientSoundColumn.Width = 70;
            //
            // morphVarbitColumn
            //
            this.morphVarbitColumn.AspectName = "morphVarbit";
            this.morphVarbitColumn.Text = "MorphVar";
            this.morphVarbitColumn.Width = 80;
            //
            // InterfaceEditorTab
            //
            this.InterfaceEditorTab.Location = new System.Drawing.Point(4, 28);
            this.InterfaceEditorTab.Name = "InterfaceEditorTab";
            this.InterfaceEditorTab.Size = new System.Drawing.Size(1121, 538);
            this.InterfaceEditorTab.TabIndex = 5;
            this.InterfaceEditorTab.Text = "Interfaces";
            this.InterfaceEditorTab.UseVisualStyleBackColor = true;
            // 
            // eventLog1
            // 
            this.eventLog1.SynchronizingObject = this;
            // 
            // EditButton
            // 
            this.EditButton.DisplayIndex = 2;
            this.EditButton.IsButton = true;
            this.EditButton.Width = 125;
            // 
            // Editor
            // 
            this.AutoScaleDimensions = new System.Drawing.SizeF(6F, 13F);
            this.AutoScaleMode = System.Windows.Forms.AutoScaleMode.Font;
            this.BackColor = System.Drawing.Color.White;
            this.ClientSize = new System.Drawing.Size(1153, 623);
            this.Controls.Add(this.EditorTabControl);
            this.Controls.Add(this.menuStrip1);
            this.DoubleBuffered = true;
            this.Font = new System.Drawing.Font("Consolas", 8.25F, System.Drawing.FontStyle.Regular, System.Drawing.GraphicsUnit.Point, ((byte)(0)));
            this.Icon = ((System.Drawing.Icon)(resources.GetObject("$this.Icon")));
            this.MainMenuStrip = this.menuStrip1;
            this.Name = "Editor";
            this.Text = "Flash\'s 639 Editor";
            this.Load += new System.EventHandler(this.Editor_Load);
            this.menuStrip1.ResumeLayout(false);
            this.menuStrip1.PerformLayout();
            this.EditorTabControl.ResumeLayout(false);
            this.Console.ResumeLayout(false);
            this.tabControl1.ResumeLayout(false);
            this.tabPage1.ResumeLayout(false);
            ((System.ComponentModel.ISupportInitialize)(this.RefTableListView)).EndInit();
            this.tabPage2.ResumeLayout(false);
            ((System.ComponentModel.ISupportInitialize)(this.ContainerListView)).EndInit();
            this.groupBox1.ResumeLayout(false);
            this.ItemEditorTab.ResumeLayout(false);
            this.groupBox4.ResumeLayout(false);
            this.groupBox4.PerformLayout();
            ((System.ComponentModel.ISupportInitialize)(this.ItemListView)).EndInit();
            this.SpriteEditorTab.ResumeLayout(false);
            this.groupBox3.ResumeLayout(false);
            this.groupBox3.PerformLayout();
            ((System.ComponentModel.ISupportInitialize)(this.numericUpDown1)).EndInit();
            ((System.ComponentModel.ISupportInitialize)(this.SpriteListView)).EndInit();
            this.NPCEditorTab.ResumeLayout(false);
            this.groupBox6.ResumeLayout(false);
            this.groupBox6.PerformLayout();
            ((System.ComponentModel.ISupportInitialize)(this.NPCListView)).EndInit();
            this.ObjectEditorTab.ResumeLayout(false);
            this.groupBox5.ResumeLayout(false);
            this.groupBox5.PerformLayout();
            ((System.ComponentModel.ISupportInitialize)(this.ObjectListView)).EndInit();
            ((System.ComponentModel.ISupportInitialize)(this.eventLog1)).EndInit();
            this.ResumeLayout(false);
            this.PerformLayout();

        }

        #endregion
        private System.Windows.Forms.FolderBrowserDialog folderBrowserDialog1;
        private System.Windows.Forms.MenuStrip menuStrip1;
        private System.Windows.Forms.ToolStripMenuItem openToolStripMenuItem;
        private System.Windows.Forms.TabControl EditorTabControl;
        private System.Windows.Forms.TabPage ItemEditorTab;
        private System.Windows.Forms.TabPage SpriteEditorTab;
        private System.Windows.Forms.TabPage Console;
        private System.Diagnostics.EventLog eventLog1;
        private System.Windows.Forms.GroupBox groupBox1;
        private BrightIdeasSoftware.TreeListView SpriteListView;
        private BrightIdeasSoftware.OLVColumn ID;
        private BrightIdeasSoftware.OLVColumn Frames;
        private BrightIdeasSoftware.OLVColumn Width;
        private BrightIdeasSoftware.OLVColumn Height;
        private BrightIdeasSoftware.OLVColumn Image;
        private System.Windows.Forms.ColorDialog colorDialog1;
        private System.Windows.Forms.GroupBox groupBox3;
        private System.Windows.Forms.Button ExportSpriteDatBtn;
        private System.Windows.Forms.Button ImportSpriteBtn;
        private System.Windows.Forms.Button ExportSpriteBmpBtn;
        private System.Windows.Forms.Label SpriteLoadingLabel;
        private System.Windows.Forms.ProgressBar SpriteProgressBar;
        private System.Windows.Forms.GroupBox groupBox4;
        private System.Windows.Forms.ProgressBar ItemProgressBar;
        private System.Windows.Forms.Button ExportItemDatBtn;
        private System.Windows.Forms.Button button9;
        private System.Windows.Forms.Label ItemLoadingLabel;
        private BrightIdeasSoftware.FastObjectListView ItemListView;
        private BrightIdeasSoftware.OLVColumn ItemID;
        private BrightIdeasSoftware.OLVColumn ItemName;
        private System.Windows.Forms.TabPage NPCEditorTab;
        private System.Windows.Forms.TabPage ObjectEditorTab;
        private System.Windows.Forms.TabPage InterfaceEditorTab;
        private BrightIdeasSoftware.OLVColumn EditButton;
        private BrightIdeasSoftware.OLVColumn InvModel;
        private BrightIdeasSoftware.OLVColumn Male1;
        private BrightIdeasSoftware.OLVColumn Male2;
        private BrightIdeasSoftware.OLVColumn Female1;
        private BrightIdeasSoftware.OLVColumn Female2;
        private BrightIdeasSoftware.OLVColumn valueColumn;
        private BrightIdeasSoftware.OLVColumn stackableColumn;
        private BrightIdeasSoftware.OLVColumn equipSlotColumn;
        private BrightIdeasSoftware.OLVColumn equipIdColumn;
        private BrightIdeasSoftware.OLVColumn membersOnlyColumn;
        private System.Windows.Forms.Label NPCLoadingLabel;
        private System.Windows.Forms.GroupBox groupBox6;
        private System.Windows.Forms.ProgressBar NPCProgressBar;
        private System.Windows.Forms.Button button2;
        private System.Windows.Forms.Button button3;
        private BrightIdeasSoftware.FastObjectListView NPCListView;
        private BrightIdeasSoftware.OLVColumn npcIdColumn;
        private BrightIdeasSoftware.OLVColumn nameColumn;
        private BrightIdeasSoftware.OLVColumn Rotate1;
        private BrightIdeasSoftware.OLVColumn Rotate2;
        private System.Windows.Forms.ToolStripMenuItem setDirectoryToolStripMenuItem;
        private System.Windows.Forms.ToolStripMenuItem openDirectoryToolStripMenuItem;
        private BrightIdeasSoftware.OLVColumn sizeColumn;
        private BrightIdeasSoftware.OLVColumn levelColumn;
        private System.Windows.Forms.ToolStripMenuItem saveAllToolStripMenuItem;
        private BrightIdeasSoftware.TreeListView RefTableListView;
        private BrightIdeasSoftware.OLVColumn formatCol;
        private BrightIdeasSoftware.OLVColumn namedCol;
        private BrightIdeasSoftware.OLVColumn usesWhirlpoolCol;
        private BrightIdeasSoftware.OLVColumn validArchiveCountCol;
        private BrightIdeasSoftware.OLVColumn versionCol;
        private BrightIdeasSoftware.OLVColumn typeCol;
        private System.Windows.Forms.TabControl tabControl1;
        private System.Windows.Forms.TabPage tabPage1;
        private System.Windows.Forms.TabPage tabPage2;
        private System.Windows.Forms.Button button4;
        private BrightIdeasSoftware.TreeListView ContainerListView;
        private BrightIdeasSoftware.OLVColumn olvColumn1;
        private BrightIdeasSoftware.OLVColumn olvColumn2;
        private BrightIdeasSoftware.OLVColumn olvColumn3;
        private BrightIdeasSoftware.OLVColumn CompressCol;
        private BrightIdeasSoftware.OLVColumn olvColumn5;
        private BrightIdeasSoftware.OLVColumn olvColumn6;
        private System.Windows.Forms.Button button6;
        private System.Windows.Forms.Button button5;
        private BrightIdeasSoftware.OLVColumn olvColumn4;
        private BrightIdeasSoftware.OLVColumn olvColumn7;
        private BrightIdeasSoftware.OLVColumn olvColumn9;
        private BrightIdeasSoftware.OLVColumn olvColumn10;
        private BrightIdeasSoftware.OLVColumn olvColumn11;
        private BrightIdeasSoftware.OLVColumn rotationColumn;
        private BrightIdeasSoftware.OLVColumn ambientColumn;
        private BrightIdeasSoftware.OLVColumn contrastColumn;
        private BrightIdeasSoftware.OLVColumn attackCursorColumn;
        private BrightIdeasSoftware.OLVColumn visiblePriorityColumn;
        private System.Windows.Forms.ToolStripMenuItem viewToolStripMenuItem;
        private System.Windows.Forms.ToolStripMenuItem alternateRowsToolStripMenuItem;
        private System.Windows.Forms.Label label1;
        private System.Windows.Forms.NumericUpDown numericUpDown1;
        private System.Windows.Forms.GroupBox groupBox5;
        private System.Windows.Forms.Label ObjectLoadingLabel;
        private System.Windows.Forms.ProgressBar ObjectProgressBar;
        private System.Windows.Forms.Button button7;
        private System.Windows.Forms.Button button8;
        private BrightIdeasSoftware.FastObjectListView ObjectListView;
        private BrightIdeasSoftware.OLVColumn objectIdColumn;
        private BrightIdeasSoftware.OLVColumn objectNameColumn;
        private BrightIdeasSoftware.OLVColumn sizeXColumn;
        private BrightIdeasSoftware.OLVColumn sizeYColumn;
        private BrightIdeasSoftware.OLVColumn walkableColumn;
        private BrightIdeasSoftware.OLVColumn clippedColumn;
        private BrightIdeasSoftware.OLVColumn ambientSoundColumn;
        private BrightIdeasSoftware.OLVColumn morphVarbitColumn;
    }
}

