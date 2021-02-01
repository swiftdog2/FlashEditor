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
            this.indexToolStripMenuItem = new System.Windows.Forms.ToolStripMenuItem();
            this.reloadToolStripMenuItem = new System.Windows.Forms.ToolStripMenuItem();
            this.saveToolStripMenuItem = new System.Windows.Forms.ToolStripMenuItem();
            this.EditorTabControl = new System.Windows.Forms.TabControl();
            this.Console = new System.Windows.Forms.TabPage();
            this.groupBox1 = new System.Windows.Forms.GroupBox();
            this.button1 = new System.Windows.Forms.Button();
            this.DebugConsole = new System.Windows.Forms.ListBox();
            this.ItemEditorTab = new System.Windows.Forms.TabPage();
            this.ItemLoadingLabel = new System.Windows.Forms.Label();
            this.groupBox4 = new System.Windows.Forms.GroupBox();
            this.ItemProgressBar = new System.Windows.Forms.ProgressBar();
            this.button8 = new System.Windows.Forms.Button();
            this.button9 = new System.Windows.Forms.Button();
            this.groupBox5 = new System.Windows.Forms.GroupBox();
            this.checkBox2 = new System.Windows.Forms.CheckBox();
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
            this.SpriteEditorTab = new System.Windows.Forms.TabPage();
            this.SpriteLoadingLabel = new System.Windows.Forms.Label();
            this.groupBox3 = new System.Windows.Forms.GroupBox();
            this.SpriteProgressBar = new System.Windows.Forms.ProgressBar();
            this.ExportSpriteBmpBtn = new System.Windows.Forms.Button();
            this.ExportSpriteDatBtn = new System.Windows.Forms.Button();
            this.ImportSpriteBtn = new System.Windows.Forms.Button();
            this.groupBox2 = new System.Windows.Forms.GroupBox();
            this.label1 = new System.Windows.Forms.Label();
            this.numericUpDown1 = new System.Windows.Forms.NumericUpDown();
            this.checkBox1 = new System.Windows.Forms.CheckBox();
            this.SpriteListView = new BrightIdeasSoftware.TreeListView();
            this.ID = ((BrightIdeasSoftware.OLVColumn)(new BrightIdeasSoftware.OLVColumn()));
            this.Frames = ((BrightIdeasSoftware.OLVColumn)(new BrightIdeasSoftware.OLVColumn()));
            this.Width = ((BrightIdeasSoftware.OLVColumn)(new BrightIdeasSoftware.OLVColumn()));
            this.Height = ((BrightIdeasSoftware.OLVColumn)(new BrightIdeasSoftware.OLVColumn()));
            this.Image = ((BrightIdeasSoftware.OLVColumn)(new BrightIdeasSoftware.OLVColumn()));
            this.NPCEditorTab = new System.Windows.Forms.TabPage();
            this.NPCLoadingLabel = new System.Windows.Forms.Label();
            this.groupBox6 = new System.Windows.Forms.GroupBox();
            this.NPCProgressBar = new System.Windows.Forms.ProgressBar();
            this.button2 = new System.Windows.Forms.Button();
            this.button3 = new System.Windows.Forms.Button();
            this.groupBox7 = new System.Windows.Forms.GroupBox();
            this.checkBox3 = new System.Windows.Forms.CheckBox();
            this.NPCListView = new BrightIdeasSoftware.FastObjectListView();
            this.idColumn = ((BrightIdeasSoftware.OLVColumn)(new BrightIdeasSoftware.OLVColumn()));
            this.nameColumn = ((BrightIdeasSoftware.OLVColumn)(new BrightIdeasSoftware.OLVColumn()));
            this.sizeColumn = ((BrightIdeasSoftware.OLVColumn)(new BrightIdeasSoftware.OLVColumn()));
            this.levelColumn = ((BrightIdeasSoftware.OLVColumn)(new BrightIdeasSoftware.OLVColumn()));
            this.ObjectEditorTab = new System.Windows.Forms.TabPage();
            this.InterfaceEditorTab = new System.Windows.Forms.TabPage();
            this.eventLog1 = new System.Diagnostics.EventLog();
            this.colorDialog1 = new System.Windows.Forms.ColorDialog();
            this.EditButton = ((BrightIdeasSoftware.OLVColumn)(new BrightIdeasSoftware.OLVColumn()));
            this.menuStrip1.SuspendLayout();
            this.EditorTabControl.SuspendLayout();
            this.Console.SuspendLayout();
            this.groupBox1.SuspendLayout();
            this.ItemEditorTab.SuspendLayout();
            this.groupBox4.SuspendLayout();
            this.groupBox5.SuspendLayout();
            ((System.ComponentModel.ISupportInitialize)(this.ItemListView)).BeginInit();
            this.SpriteEditorTab.SuspendLayout();
            this.groupBox3.SuspendLayout();
            this.groupBox2.SuspendLayout();
            ((System.ComponentModel.ISupportInitialize)(this.numericUpDown1)).BeginInit();
            ((System.ComponentModel.ISupportInitialize)(this.SpriteListView)).BeginInit();
            this.NPCEditorTab.SuspendLayout();
            this.groupBox6.SuspendLayout();
            this.groupBox7.SuspendLayout();
            ((System.ComponentModel.ISupportInitialize)(this.NPCListView)).BeginInit();
            ((System.ComponentModel.ISupportInitialize)(this.eventLog1)).BeginInit();
            this.SuspendLayout();
            // 
            // menuStrip1
            // 
            this.menuStrip1.BackColor = System.Drawing.Color.White;
            this.menuStrip1.Items.AddRange(new System.Windows.Forms.ToolStripItem[] {
            this.openToolStripMenuItem,
            this.indexToolStripMenuItem});
            this.menuStrip1.Location = new System.Drawing.Point(0, 0);
            this.menuStrip1.Name = "menuStrip1";
            this.menuStrip1.Size = new System.Drawing.Size(1261, 33);
            this.menuStrip1.TabIndex = 1;
            this.menuStrip1.Text = "menuStrip1";
            // 
            // openToolStripMenuItem
            // 
            this.openToolStripMenuItem.DropDownItems.AddRange(new System.Windows.Forms.ToolStripItem[] {
            this.setDirectoryToolStripMenuItem,
            this.openDirectoryToolStripMenuItem});
            this.openToolStripMenuItem.Font = new System.Drawing.Font("Segoe UI", 14.25F, System.Drawing.FontStyle.Regular, System.Drawing.GraphicsUnit.Point, ((byte)(0)));
            this.openToolStripMenuItem.Name = "openToolStripMenuItem";
            this.openToolStripMenuItem.Size = new System.Drawing.Size(76, 29);
            this.openToolStripMenuItem.Text = "Cache";
            // 
            // setDirectoryToolStripMenuItem
            // 
            this.setDirectoryToolStripMenuItem.Name = "setDirectoryToolStripMenuItem";
            this.setDirectoryToolStripMenuItem.Size = new System.Drawing.Size(212, 30);
            this.setDirectoryToolStripMenuItem.Text = "Set Directory";
            this.setDirectoryToolStripMenuItem.Click += new System.EventHandler(this.setDirectoryToolStripMenuItem_Click);
            // 
            // openDirectoryToolStripMenuItem
            // 
            this.openDirectoryToolStripMenuItem.Name = "openDirectoryToolStripMenuItem";
            this.openDirectoryToolStripMenuItem.Size = new System.Drawing.Size(212, 30);
            this.openDirectoryToolStripMenuItem.Text = "Open Directory";
            this.openDirectoryToolStripMenuItem.Click += new System.EventHandler(this.openDirectoryToolStripMenuItem_Click);
            // 
            // indexToolStripMenuItem
            // 
            this.indexToolStripMenuItem.DropDownItems.AddRange(new System.Windows.Forms.ToolStripItem[] {
            this.reloadToolStripMenuItem,
            this.saveToolStripMenuItem});
            this.indexToolStripMenuItem.Font = new System.Drawing.Font("Segoe UI", 14.25F);
            this.indexToolStripMenuItem.Name = "indexToolStripMenuItem";
            this.indexToolStripMenuItem.Size = new System.Drawing.Size(107, 29);
            this.indexToolStripMenuItem.Text = "Container";
            // 
            // reloadToolStripMenuItem
            // 
            this.reloadToolStripMenuItem.Name = "reloadToolStripMenuItem";
            this.reloadToolStripMenuItem.Size = new System.Drawing.Size(180, 30);
            this.reloadToolStripMenuItem.Text = "Reload";
            // 
            // saveToolStripMenuItem
            // 
            this.saveToolStripMenuItem.Name = "saveToolStripMenuItem";
            this.saveToolStripMenuItem.Size = new System.Drawing.Size(180, 30);
            this.saveToolStripMenuItem.Text = "Save";
            this.saveToolStripMenuItem.Click += new System.EventHandler(this.saveToolStripMenuItem_Click);
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
            this.EditorTabControl.Font = new System.Drawing.Font("Consolas", 14.25F);
            this.EditorTabControl.Location = new System.Drawing.Point(12, 41);
            this.EditorTabControl.Name = "EditorTabControl";
            this.EditorTabControl.SelectedIndex = 0;
            this.EditorTabControl.Size = new System.Drawing.Size(1237, 545);
            this.EditorTabControl.TabIndex = 3;
            this.EditorTabControl.SelectedIndexChanged += new System.EventHandler(this.EditorTabControl_SelectedIndexChanged);
            // 
            // Console
            // 
            this.Console.BackColor = System.Drawing.Color.White;
            this.Console.Controls.Add(this.groupBox1);
            this.Console.Controls.Add(this.DebugConsole);
            this.Console.Location = new System.Drawing.Point(4, 31);
            this.Console.Name = "Console";
            this.Console.Size = new System.Drawing.Size(1229, 510);
            this.Console.TabIndex = 2;
            this.Console.Text = "Console";
            // 
            // groupBox1
            // 
            this.groupBox1.Anchor = ((System.Windows.Forms.AnchorStyles)(((System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Bottom) 
            | System.Windows.Forms.AnchorStyles.Right)));
            this.groupBox1.BackColor = System.Drawing.Color.White;
            this.groupBox1.Controls.Add(this.button1);
            this.groupBox1.Location = new System.Drawing.Point(890, 3);
            this.groupBox1.Name = "groupBox1";
            this.groupBox1.Size = new System.Drawing.Size(336, 498);
            this.groupBox1.TabIndex = 5;
            this.groupBox1.TabStop = false;
            this.groupBox1.Text = "Cache Tests";
            // 
            // button1
            // 
            this.button1.Location = new System.Drawing.Point(16, 38);
            this.button1.Name = "button1";
            this.button1.Size = new System.Drawing.Size(303, 33);
            this.button1.TabIndex = 0;
            this.button1.Text = "Refresh Reference Tables";
            this.button1.UseVisualStyleBackColor = true;
            // 
            // DebugConsole
            // 
            this.DebugConsole.Anchor = ((System.Windows.Forms.AnchorStyles)((((System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Bottom) 
            | System.Windows.Forms.AnchorStyles.Left) 
            | System.Windows.Forms.AnchorStyles.Right)));
            this.DebugConsole.BackColor = System.Drawing.Color.White;
            this.DebugConsole.Font = new System.Drawing.Font("Consolas", 12F);
            this.DebugConsole.FormattingEnabled = true;
            this.DebugConsole.ItemHeight = 19;
            this.DebugConsole.Location = new System.Drawing.Point(3, 3);
            this.DebugConsole.Name = "DebugConsole";
            this.DebugConsole.Size = new System.Drawing.Size(881, 498);
            this.DebugConsole.TabIndex = 4;
            // 
            // ItemEditorTab
            // 
            this.ItemEditorTab.Controls.Add(this.ItemLoadingLabel);
            this.ItemEditorTab.Controls.Add(this.groupBox4);
            this.ItemEditorTab.Controls.Add(this.groupBox5);
            this.ItemEditorTab.Controls.Add(this.ItemListView);
            this.ItemEditorTab.Location = new System.Drawing.Point(4, 31);
            this.ItemEditorTab.Name = "ItemEditorTab";
            this.ItemEditorTab.Padding = new System.Windows.Forms.Padding(3);
            this.ItemEditorTab.Size = new System.Drawing.Size(1229, 510);
            this.ItemEditorTab.TabIndex = 0;
            this.ItemEditorTab.Text = "Items";
            this.ItemEditorTab.UseVisualStyleBackColor = true;
            // 
            // ItemLoadingLabel
            // 
            this.ItemLoadingLabel.AutoSize = true;
            this.ItemLoadingLabel.BackColor = System.Drawing.Color.Transparent;
            this.ItemLoadingLabel.Font = new System.Drawing.Font("Consolas", 12F, System.Drawing.FontStyle.Regular, System.Drawing.GraphicsUnit.Point, ((byte)(0)));
            this.ItemLoadingLabel.Location = new System.Drawing.Point(300, 220);
            this.ItemLoadingLabel.Name = "ItemLoadingLabel";
            this.ItemLoadingLabel.Size = new System.Drawing.Size(135, 38);
            this.ItemLoadingLabel.TabIndex = 12;
            this.ItemLoadingLabel.Text = "Loading Items,\r\nplease wait...\r\n";
            this.ItemLoadingLabel.TextAlign = System.Drawing.ContentAlignment.MiddleCenter;
            this.ItemLoadingLabel.Visible = false;
            // 
            // groupBox4
            // 
            this.groupBox4.Anchor = ((System.Windows.Forms.AnchorStyles)(((System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Bottom) 
            | System.Windows.Forms.AnchorStyles.Right)));
            this.groupBox4.BackColor = System.Drawing.Color.White;
            this.groupBox4.Controls.Add(this.ItemProgressBar);
            this.groupBox4.Controls.Add(this.button8);
            this.groupBox4.Controls.Add(this.button9);
            this.groupBox4.Font = new System.Drawing.Font("Consolas", 14.25F);
            this.groupBox4.Location = new System.Drawing.Point(973, 183);
            this.groupBox4.Name = "groupBox4";
            this.groupBox4.Size = new System.Drawing.Size(250, 321);
            this.groupBox4.TabIndex = 11;
            this.groupBox4.TabStop = false;
            this.groupBox4.Text = "Editor Controls";
            // 
            // ItemProgressBar
            // 
            this.ItemProgressBar.Anchor = ((System.Windows.Forms.AnchorStyles)((System.Windows.Forms.AnchorStyles.Bottom | System.Windows.Forms.AnchorStyles.Right)));
            this.ItemProgressBar.ForeColor = System.Drawing.Color.DarkRed;
            this.ItemProgressBar.Location = new System.Drawing.Point(0, 259);
            this.ItemProgressBar.Name = "ItemProgressBar";
            this.ItemProgressBar.Size = new System.Drawing.Size(250, 60);
            this.ItemProgressBar.TabIndex = 8;
            this.ItemProgressBar.Visible = false;
            // 
            // button8
            // 
            this.button8.BackColor = System.Drawing.Color.White;
            this.button8.Location = new System.Drawing.Point(6, 70);
            this.button8.Name = "button8";
            this.button8.Size = new System.Drawing.Size(238, 35);
            this.button8.TabIndex = 1;
            this.button8.Text = "Export Selected (.dat)";
            this.button8.UseVisualStyleBackColor = false;
            // 
            // button9
            // 
            this.button9.BackColor = System.Drawing.Color.White;
            this.button9.Location = new System.Drawing.Point(6, 29);
            this.button9.Name = "button9";
            this.button9.Size = new System.Drawing.Size(238, 35);
            this.button9.TabIndex = 0;
            this.button9.Text = "Import Item";
            this.button9.UseVisualStyleBackColor = false;
            // 
            // groupBox5
            // 
            this.groupBox5.Anchor = ((System.Windows.Forms.AnchorStyles)((System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Right)));
            this.groupBox5.BackColor = System.Drawing.Color.White;
            this.groupBox5.Controls.Add(this.checkBox2);
            this.groupBox5.Font = new System.Drawing.Font("Consolas", 14.25F);
            this.groupBox5.Location = new System.Drawing.Point(973, 6);
            this.groupBox5.Name = "groupBox5";
            this.groupBox5.Size = new System.Drawing.Size(250, 171);
            this.groupBox5.TabIndex = 10;
            this.groupBox5.TabStop = false;
            this.groupBox5.Text = "Layout Controls";
            // 
            // checkBox2
            // 
            this.checkBox2.AutoSize = true;
            this.checkBox2.Font = new System.Drawing.Font("Consolas", 12F, System.Drawing.FontStyle.Regular, System.Drawing.GraphicsUnit.Point, ((byte)(0)));
            this.checkBox2.Location = new System.Drawing.Point(24, 29);
            this.checkBox2.Name = "checkBox2";
            this.checkBox2.Size = new System.Drawing.Size(199, 23);
            this.checkBox2.TabIndex = 0;
            this.checkBox2.Text = "Alternate Row Color";
            this.checkBox2.UseVisualStyleBackColor = true;
            this.checkBox2.CheckedChanged += new System.EventHandler(this.checkBox2_CheckedChanged);
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
            this.ItemListView.Anchor = ((System.Windows.Forms.AnchorStyles)((((System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Bottom) 
            | System.Windows.Forms.AnchorStyles.Left) 
            | System.Windows.Forms.AnchorStyles.Right)));
            this.ItemListView.BackColor = System.Drawing.Color.White;
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
            this.Rotate2});
            this.ItemListView.Cursor = System.Windows.Forms.Cursors.Default;
            this.ItemListView.Font = new System.Drawing.Font("Consolas", 11.25F, System.Drawing.FontStyle.Regular, System.Drawing.GraphicsUnit.Point, ((byte)(0)));
            this.ItemListView.FullRowSelect = true;
            this.ItemListView.GridLines = true;
            this.ItemListView.HideSelection = false;
            this.ItemListView.Location = new System.Drawing.Point(6, 3);
            this.ItemListView.Name = "ItemListView";
            this.ItemListView.RowHeight = 10;
            this.ItemListView.ShowGroups = false;
            this.ItemListView.ShowImagesOnSubItems = true;
            this.ItemListView.Size = new System.Drawing.Size(961, 498);
            this.ItemListView.TabIndex = 14;
            this.ItemListView.UseCompatibleStateImageBehavior = false;
            this.ItemListView.View = System.Windows.Forms.View.Details;
            this.ItemListView.VirtualMode = true;
            // 
            // ItemID
            // 
            this.ItemID.AspectName = "id";
            this.ItemID.Groupable = false;
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
            // SpriteEditorTab
            // 
            this.SpriteEditorTab.Controls.Add(this.SpriteLoadingLabel);
            this.SpriteEditorTab.Controls.Add(this.groupBox3);
            this.SpriteEditorTab.Controls.Add(this.groupBox2);
            this.SpriteEditorTab.Controls.Add(this.SpriteListView);
            this.SpriteEditorTab.Location = new System.Drawing.Point(4, 31);
            this.SpriteEditorTab.Name = "SpriteEditorTab";
            this.SpriteEditorTab.Padding = new System.Windows.Forms.Padding(3);
            this.SpriteEditorTab.Size = new System.Drawing.Size(1229, 510);
            this.SpriteEditorTab.TabIndex = 1;
            this.SpriteEditorTab.Text = "Sprites";
            this.SpriteEditorTab.UseVisualStyleBackColor = true;
            // 
            // SpriteLoadingLabel
            // 
            this.SpriteLoadingLabel.AutoSize = true;
            this.SpriteLoadingLabel.BackColor = System.Drawing.Color.White;
            this.SpriteLoadingLabel.Font = new System.Drawing.Font("Consolas", 12F, System.Drawing.FontStyle.Regular, System.Drawing.GraphicsUnit.Point, ((byte)(0)));
            this.SpriteLoadingLabel.Location = new System.Drawing.Point(300, 220);
            this.SpriteLoadingLabel.Name = "SpriteLoadingLabel";
            this.SpriteLoadingLabel.Size = new System.Drawing.Size(153, 38);
            this.SpriteLoadingLabel.TabIndex = 10;
            this.SpriteLoadingLabel.Text = "Loading Sprites,\r\nplease wait...";
            this.SpriteLoadingLabel.TextAlign = System.Drawing.ContentAlignment.MiddleCenter;
            this.SpriteLoadingLabel.Visible = false;
            // 
            // groupBox3
            // 
            this.groupBox3.Anchor = ((System.Windows.Forms.AnchorStyles)((System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Right)));
            this.groupBox3.BackColor = System.Drawing.Color.White;
            this.groupBox3.Controls.Add(this.SpriteProgressBar);
            this.groupBox3.Controls.Add(this.ExportSpriteBmpBtn);
            this.groupBox3.Controls.Add(this.ExportSpriteDatBtn);
            this.groupBox3.Controls.Add(this.ImportSpriteBtn);
            this.groupBox3.Font = new System.Drawing.Font("Consolas", 14.25F);
            this.groupBox3.Location = new System.Drawing.Point(973, 183);
            this.groupBox3.Name = "groupBox3";
            this.groupBox3.Size = new System.Drawing.Size(250, 321);
            this.groupBox3.TabIndex = 9;
            this.groupBox3.TabStop = false;
            this.groupBox3.Text = "Editor Controls";
            // 
            // SpriteProgressBar
            // 
            this.SpriteProgressBar.Anchor = ((System.Windows.Forms.AnchorStyles)(((System.Windows.Forms.AnchorStyles.Bottom | System.Windows.Forms.AnchorStyles.Left) 
            | System.Windows.Forms.AnchorStyles.Right)));
            this.SpriteProgressBar.BackColor = System.Drawing.Color.White;
            this.SpriteProgressBar.ForeColor = System.Drawing.Color.DarkRed;
            this.SpriteProgressBar.Location = new System.Drawing.Point(0, 260);
            this.SpriteProgressBar.Name = "SpriteProgressBar";
            this.SpriteProgressBar.Size = new System.Drawing.Size(250, 60);
            this.SpriteProgressBar.TabIndex = 8;
            this.SpriteProgressBar.Visible = false;
            // 
            // ExportSpriteBmpBtn
            // 
            this.ExportSpriteBmpBtn.BackColor = System.Drawing.Color.White;
            this.ExportSpriteBmpBtn.Location = new System.Drawing.Point(6, 111);
            this.ExportSpriteBmpBtn.Name = "ExportSpriteBmpBtn";
            this.ExportSpriteBmpBtn.Size = new System.Drawing.Size(238, 35);
            this.ExportSpriteBmpBtn.TabIndex = 4;
            this.ExportSpriteBmpBtn.Text = "Export Selected (.png)";
            this.ExportSpriteBmpBtn.UseVisualStyleBackColor = false;
            this.ExportSpriteBmpBtn.Click += new System.EventHandler(this.ExportSpriteBmpBtn_Click);
            // 
            // ExportSpriteDatBtn
            // 
            this.ExportSpriteDatBtn.BackColor = System.Drawing.Color.White;
            this.ExportSpriteDatBtn.Location = new System.Drawing.Point(6, 70);
            this.ExportSpriteDatBtn.Name = "ExportSpriteDatBtn";
            this.ExportSpriteDatBtn.Size = new System.Drawing.Size(238, 35);
            this.ExportSpriteDatBtn.TabIndex = 1;
            this.ExportSpriteDatBtn.Text = "Export Selected (.dat)";
            this.ExportSpriteDatBtn.UseVisualStyleBackColor = false;
            this.ExportSpriteDatBtn.Click += new System.EventHandler(this.ExportSpriteDatBtn_Click);
            // 
            // ImportSpriteBtn
            // 
            this.ImportSpriteBtn.BackColor = System.Drawing.Color.White;
            this.ImportSpriteBtn.Location = new System.Drawing.Point(6, 29);
            this.ImportSpriteBtn.Name = "ImportSpriteBtn";
            this.ImportSpriteBtn.Size = new System.Drawing.Size(238, 35);
            this.ImportSpriteBtn.TabIndex = 0;
            this.ImportSpriteBtn.Text = "Import Sprite";
            this.ImportSpriteBtn.UseVisualStyleBackColor = false;
            // 
            // groupBox2
            // 
            this.groupBox2.Anchor = ((System.Windows.Forms.AnchorStyles)((System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Right)));
            this.groupBox2.BackColor = System.Drawing.Color.White;
            this.groupBox2.Controls.Add(this.label1);
            this.groupBox2.Controls.Add(this.numericUpDown1);
            this.groupBox2.Controls.Add(this.checkBox1);
            this.groupBox2.Font = new System.Drawing.Font("Consolas", 14.25F);
            this.groupBox2.Location = new System.Drawing.Point(973, 6);
            this.groupBox2.Name = "groupBox2";
            this.groupBox2.Size = new System.Drawing.Size(250, 171);
            this.groupBox2.TabIndex = 8;
            this.groupBox2.TabStop = false;
            this.groupBox2.Text = "Layout Controls";
            // 
            // label1
            // 
            this.label1.AutoSize = true;
            this.label1.Font = new System.Drawing.Font("Consolas", 12F, System.Drawing.FontStyle.Regular, System.Drawing.GraphicsUnit.Point, ((byte)(0)));
            this.label1.Location = new System.Drawing.Point(20, 64);
            this.label1.Name = "label1";
            this.label1.Size = new System.Drawing.Size(99, 19);
            this.label1.TabIndex = 2;
            this.label1.Text = "Row Height";
            // 
            // numericUpDown1
            // 
            this.numericUpDown1.BackColor = System.Drawing.Color.White;
            this.numericUpDown1.Increment = new decimal(new int[] {
            2,
            0,
            0,
            0});
            this.numericUpDown1.Location = new System.Drawing.Point(154, 58);
            this.numericUpDown1.Maximum = new decimal(new int[] {
            256,
            0,
            0,
            0});
            this.numericUpDown1.Minimum = new decimal(new int[] {
            10,
            0,
            0,
            0});
            this.numericUpDown1.Name = "numericUpDown1";
            this.numericUpDown1.Size = new System.Drawing.Size(69, 30);
            this.numericUpDown1.TabIndex = 1;
            this.numericUpDown1.Value = new decimal(new int[] {
            20,
            0,
            0,
            0});
            this.numericUpDown1.ValueChanged += new System.EventHandler(this.numericUpDown1_ValueChanged);
            // 
            // checkBox1
            // 
            this.checkBox1.AutoSize = true;
            this.checkBox1.Font = new System.Drawing.Font("Consolas", 12F, System.Drawing.FontStyle.Regular, System.Drawing.GraphicsUnit.Point, ((byte)(0)));
            this.checkBox1.Location = new System.Drawing.Point(24, 29);
            this.checkBox1.Name = "checkBox1";
            this.checkBox1.Size = new System.Drawing.Size(199, 23);
            this.checkBox1.TabIndex = 0;
            this.checkBox1.Text = "Alternate Row Color";
            this.checkBox1.UseVisualStyleBackColor = true;
            this.checkBox1.CheckedChanged += new System.EventHandler(this.checkBox1_CheckedChanged);
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
            this.SpriteListView.Font = new System.Drawing.Font("Consolas", 14.25F, System.Drawing.FontStyle.Regular, System.Drawing.GraphicsUnit.Point, ((byte)(0)));
            this.SpriteListView.FullRowSelect = true;
            this.SpriteListView.GridLines = true;
            this.SpriteListView.HideSelection = false;
            this.SpriteListView.Location = new System.Drawing.Point(6, 6);
            this.SpriteListView.Name = "SpriteListView";
            this.SpriteListView.RowHeight = 20;
            this.SpriteListView.ShowGroups = false;
            this.SpriteListView.Size = new System.Drawing.Size(961, 498);
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
            this.ID.Width = 125;
            // 
            // Frames
            // 
            this.Frames.AspectName = "frameCount";
            this.Frames.Text = "Frames";
            this.Frames.Width = 75;
            // 
            // Width
            // 
            this.Width.AspectName = "width";
            this.Width.Text = "Width";
            this.Width.Width = 75;
            // 
            // Height
            // 
            this.Height.AspectName = "height";
            this.Height.Text = "Height";
            this.Height.Width = 94;
            // 
            // Image
            // 
            this.Image.AspectName = "sprite";
            this.Image.FillsFreeSpace = true;
            this.Image.ImageAspectName = "thumb";
            this.Image.IsEditable = false;
            this.Image.Text = "Sprite";
            this.Image.Width = 350;
            // 
            // NPCEditorTab
            // 
            this.NPCEditorTab.Controls.Add(this.NPCLoadingLabel);
            this.NPCEditorTab.Controls.Add(this.groupBox6);
            this.NPCEditorTab.Controls.Add(this.groupBox7);
            this.NPCEditorTab.Controls.Add(this.NPCListView);
            this.NPCEditorTab.Location = new System.Drawing.Point(4, 31);
            this.NPCEditorTab.Name = "NPCEditorTab";
            this.NPCEditorTab.Size = new System.Drawing.Size(1229, 510);
            this.NPCEditorTab.TabIndex = 3;
            this.NPCEditorTab.Text = "NPCs";
            this.NPCEditorTab.UseVisualStyleBackColor = true;
            // 
            // NPCLoadingLabel
            // 
            this.NPCLoadingLabel.AutoSize = true;
            this.NPCLoadingLabel.BackColor = System.Drawing.Color.Transparent;
            this.NPCLoadingLabel.Font = new System.Drawing.Font("Consolas", 12F, System.Drawing.FontStyle.Regular, System.Drawing.GraphicsUnit.Point, ((byte)(0)));
            this.NPCLoadingLabel.Location = new System.Drawing.Point(300, 222);
            this.NPCLoadingLabel.Name = "NPCLoadingLabel";
            this.NPCLoadingLabel.Size = new System.Drawing.Size(135, 38);
            this.NPCLoadingLabel.TabIndex = 17;
            this.NPCLoadingLabel.Text = "Loading NPCs,\r\nplease wait...\r\n";
            this.NPCLoadingLabel.TextAlign = System.Drawing.ContentAlignment.MiddleCenter;
            this.NPCLoadingLabel.Visible = false;
            // 
            // groupBox6
            // 
            this.groupBox6.Anchor = ((System.Windows.Forms.AnchorStyles)(((System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Bottom) 
            | System.Windows.Forms.AnchorStyles.Right)));
            this.groupBox6.BackColor = System.Drawing.Color.White;
            this.groupBox6.Controls.Add(this.NPCProgressBar);
            this.groupBox6.Controls.Add(this.button2);
            this.groupBox6.Controls.Add(this.button3);
            this.groupBox6.Font = new System.Drawing.Font("Consolas", 14.25F);
            this.groupBox6.Location = new System.Drawing.Point(973, 185);
            this.groupBox6.Name = "groupBox6";
            this.groupBox6.Size = new System.Drawing.Size(250, 321);
            this.groupBox6.TabIndex = 16;
            this.groupBox6.TabStop = false;
            this.groupBox6.Text = "Editor Controls";
            // 
            // NPCProgressBar
            // 
            this.NPCProgressBar.Anchor = ((System.Windows.Forms.AnchorStyles)((System.Windows.Forms.AnchorStyles.Bottom | System.Windows.Forms.AnchorStyles.Right)));
            this.NPCProgressBar.ForeColor = System.Drawing.Color.DarkRed;
            this.NPCProgressBar.Location = new System.Drawing.Point(0, 259);
            this.NPCProgressBar.Name = "NPCProgressBar";
            this.NPCProgressBar.Size = new System.Drawing.Size(250, 60);
            this.NPCProgressBar.TabIndex = 8;
            this.NPCProgressBar.Visible = false;
            // 
            // button2
            // 
            this.button2.BackColor = System.Drawing.Color.White;
            this.button2.Location = new System.Drawing.Point(6, 70);
            this.button2.Name = "button2";
            this.button2.Size = new System.Drawing.Size(238, 35);
            this.button2.TabIndex = 1;
            this.button2.Text = "Export Selected (.dat)";
            this.button2.UseVisualStyleBackColor = false;
            // 
            // button3
            // 
            this.button3.BackColor = System.Drawing.Color.White;
            this.button3.Location = new System.Drawing.Point(6, 29);
            this.button3.Name = "button3";
            this.button3.Size = new System.Drawing.Size(238, 35);
            this.button3.TabIndex = 0;
            this.button3.Text = "Import NPC";
            this.button3.UseVisualStyleBackColor = false;
            // 
            // groupBox7
            // 
            this.groupBox7.Anchor = ((System.Windows.Forms.AnchorStyles)((System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Right)));
            this.groupBox7.BackColor = System.Drawing.Color.White;
            this.groupBox7.Controls.Add(this.checkBox3);
            this.groupBox7.Font = new System.Drawing.Font("Consolas", 14.25F);
            this.groupBox7.Location = new System.Drawing.Point(973, 8);
            this.groupBox7.Name = "groupBox7";
            this.groupBox7.Size = new System.Drawing.Size(250, 171);
            this.groupBox7.TabIndex = 15;
            this.groupBox7.TabStop = false;
            this.groupBox7.Text = "Layout Controls";
            // 
            // checkBox3
            // 
            this.checkBox3.AutoSize = true;
            this.checkBox3.Font = new System.Drawing.Font("Consolas", 12F, System.Drawing.FontStyle.Regular, System.Drawing.GraphicsUnit.Point, ((byte)(0)));
            this.checkBox3.Location = new System.Drawing.Point(24, 29);
            this.checkBox3.Name = "checkBox3";
            this.checkBox3.Size = new System.Drawing.Size(199, 23);
            this.checkBox3.TabIndex = 0;
            this.checkBox3.Text = "Alternate Row Color";
            this.checkBox3.UseVisualStyleBackColor = true;
            // 
            // NPCListView
            // 
            this.NPCListView.AllColumns.Add(this.idColumn);
            this.NPCListView.AllColumns.Add(this.nameColumn);
            this.NPCListView.AllColumns.Add(this.sizeColumn);
            this.NPCListView.AllColumns.Add(this.levelColumn);
            this.NPCListView.Anchor = ((System.Windows.Forms.AnchorStyles)((((System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Bottom) 
            | System.Windows.Forms.AnchorStyles.Left) 
            | System.Windows.Forms.AnchorStyles.Right)));
            this.NPCListView.BackColor = System.Drawing.Color.White;
            this.NPCListView.CellEditUseWholeCell = false;
            this.NPCListView.CheckBoxes = true;
            this.NPCListView.Columns.AddRange(new System.Windows.Forms.ColumnHeader[] {
            this.idColumn,
            this.nameColumn,
            this.sizeColumn,
            this.levelColumn});
            this.NPCListView.Cursor = System.Windows.Forms.Cursors.Default;
            this.NPCListView.Font = new System.Drawing.Font("Consolas", 11.25F, System.Drawing.FontStyle.Regular, System.Drawing.GraphicsUnit.Point, ((byte)(0)));
            this.NPCListView.FullRowSelect = true;
            this.NPCListView.GridLines = true;
            this.NPCListView.HideSelection = false;
            this.NPCListView.Location = new System.Drawing.Point(6, 5);
            this.NPCListView.Name = "NPCListView";
            this.NPCListView.RowHeight = 10;
            this.NPCListView.ShowGroups = false;
            this.NPCListView.ShowImagesOnSubItems = true;
            this.NPCListView.Size = new System.Drawing.Size(961, 498);
            this.NPCListView.TabIndex = 18;
            this.NPCListView.UseCompatibleStateImageBehavior = false;
            this.NPCListView.View = System.Windows.Forms.View.Details;
            this.NPCListView.VirtualMode = true;
            // 
            // idColumn
            // 
            this.idColumn.AspectName = "id";
            this.idColumn.Groupable = false;
            this.idColumn.Searchable = false;
            this.idColumn.Text = "ID";
            this.idColumn.Width = 78;
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
            this.sizeColumn.Width = 87;
            // 
            // levelColumn
            // 
            this.levelColumn.AspectName = "level";
            this.levelColumn.Text = "Level";
            this.levelColumn.Width = 86;
            // 
            // ObjectEditorTab
            // 
            this.ObjectEditorTab.Location = new System.Drawing.Point(4, 31);
            this.ObjectEditorTab.Name = "ObjectEditorTab";
            this.ObjectEditorTab.Size = new System.Drawing.Size(1229, 510);
            this.ObjectEditorTab.TabIndex = 4;
            this.ObjectEditorTab.Text = "Objects";
            this.ObjectEditorTab.UseVisualStyleBackColor = true;
            // 
            // InterfaceEditorTab
            // 
            this.InterfaceEditorTab.Location = new System.Drawing.Point(4, 31);
            this.InterfaceEditorTab.Name = "InterfaceEditorTab";
            this.InterfaceEditorTab.Size = new System.Drawing.Size(1229, 510);
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
            this.ClientSize = new System.Drawing.Size(1261, 598);
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
            this.groupBox1.ResumeLayout(false);
            this.ItemEditorTab.ResumeLayout(false);
            this.ItemEditorTab.PerformLayout();
            this.groupBox4.ResumeLayout(false);
            this.groupBox5.ResumeLayout(false);
            this.groupBox5.PerformLayout();
            ((System.ComponentModel.ISupportInitialize)(this.ItemListView)).EndInit();
            this.SpriteEditorTab.ResumeLayout(false);
            this.SpriteEditorTab.PerformLayout();
            this.groupBox3.ResumeLayout(false);
            this.groupBox2.ResumeLayout(false);
            this.groupBox2.PerformLayout();
            ((System.ComponentModel.ISupportInitialize)(this.numericUpDown1)).EndInit();
            ((System.ComponentModel.ISupportInitialize)(this.SpriteListView)).EndInit();
            this.NPCEditorTab.ResumeLayout(false);
            this.NPCEditorTab.PerformLayout();
            this.groupBox6.ResumeLayout(false);
            this.groupBox7.ResumeLayout(false);
            this.groupBox7.PerformLayout();
            ((System.ComponentModel.ISupportInitialize)(this.NPCListView)).EndInit();
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
        private System.Windows.Forms.ListBox DebugConsole;
        private System.Windows.Forms.GroupBox groupBox1;
        private System.Windows.Forms.Button button1;
        private BrightIdeasSoftware.TreeListView SpriteListView;
        private BrightIdeasSoftware.OLVColumn ID;
        private BrightIdeasSoftware.OLVColumn Frames;
        private BrightIdeasSoftware.OLVColumn Width;
        private BrightIdeasSoftware.OLVColumn Height;
        private BrightIdeasSoftware.OLVColumn Image;
        private System.Windows.Forms.GroupBox groupBox2;
        private System.Windows.Forms.Label label1;
        private System.Windows.Forms.NumericUpDown numericUpDown1;
        private System.Windows.Forms.CheckBox checkBox1;
        private System.Windows.Forms.ColorDialog colorDialog1;
        private System.Windows.Forms.GroupBox groupBox3;
        private System.Windows.Forms.Button ExportSpriteDatBtn;
        private System.Windows.Forms.Button ImportSpriteBtn;
        private System.Windows.Forms.Button ExportSpriteBmpBtn;
        private System.Windows.Forms.Label SpriteLoadingLabel;
        private System.Windows.Forms.ProgressBar SpriteProgressBar;
        private System.Windows.Forms.GroupBox groupBox4;
        private System.Windows.Forms.ProgressBar ItemProgressBar;
        private System.Windows.Forms.Button button8;
        private System.Windows.Forms.Button button9;
        private System.Windows.Forms.GroupBox groupBox5;
        private System.Windows.Forms.CheckBox checkBox2;
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
        private System.Windows.Forms.Label NPCLoadingLabel;
        private System.Windows.Forms.GroupBox groupBox6;
        private System.Windows.Forms.ProgressBar NPCProgressBar;
        private System.Windows.Forms.Button button2;
        private System.Windows.Forms.Button button3;
        private System.Windows.Forms.GroupBox groupBox7;
        private System.Windows.Forms.CheckBox checkBox3;
        private BrightIdeasSoftware.FastObjectListView NPCListView;
        private BrightIdeasSoftware.OLVColumn idColumn;
        private BrightIdeasSoftware.OLVColumn nameColumn;
        private BrightIdeasSoftware.OLVColumn Rotate1;
        private BrightIdeasSoftware.OLVColumn Rotate2;
        private System.Windows.Forms.ToolStripMenuItem setDirectoryToolStripMenuItem;
        private System.Windows.Forms.ToolStripMenuItem openDirectoryToolStripMenuItem;
        private System.Windows.Forms.ToolStripMenuItem indexToolStripMenuItem;
        private System.Windows.Forms.ToolStripMenuItem reloadToolStripMenuItem;
        private System.Windows.Forms.ToolStripMenuItem saveToolStripMenuItem;
        private BrightIdeasSoftware.OLVColumn sizeColumn;
        private BrightIdeasSoftware.OLVColumn levelColumn;
    }
}

