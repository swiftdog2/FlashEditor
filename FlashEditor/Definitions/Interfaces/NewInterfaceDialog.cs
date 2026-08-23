using System;
using System.ComponentModel;
using System.Drawing;
using System.Windows.Forms;
using FlashEditor.UI;

namespace FlashEditor.Definitions.Interfaces {
    /// <summary>
    ///     Collects what a new interface needs before it is written.
    /// </summary>
    /// <remarks>
    ///     <b>The name is not optional here although the format allows an unnamed group.</b> Index 3
    ///     stores a name as a hash and the editor recovers it by re-hashing candidates, so a group
    ///     left unnamed now cannot be given a name later without rewriting it. The dialog refuses to
    ///     close rather than defaulting one, because a generated name would be a name nothing can
    ///     ever resolve.
    ///     <para>
    ///     <b>The root choice is two types, not the six the codec handles.</b> A text root needs a
    ///     font and a message and a sprite or model root needs an asset id, and collecting those
    ///     here would make this a component editor. Rooting on one without them writes a component
    ///     that decodes correctly and draws nothing, which reads as a broken interface.
    ///     </para>
    ///     <para>
    ///     Sizes are stated in pixels and mean pixels. The process is pinned DPI-unaware, so the
    ///     numbers typed here are the numbers the client works in.
    ///     </para>
    /// </remarks>
    public sealed class NewInterfaceDialog : Form {
        /// <summary>
        ///     The default root size.
        /// </summary>
        /// <remarks>
        ///     The client's fixed-layout window, so a root at this size is the whole screen area an
        ///     interface can occupy rather than an arbitrary rectangle.
        /// </remarks>
        private const int DefaultWidth = 512;

        /// <summary>The default root height. See <see cref="DefaultWidth"/>.</summary>
        private const int DefaultHeight = 334;

        /// <summary>Largest value the stored width and height fields can hold.</summary>
        private const int MaxDimension = 0xFFFF;

        private readonly TextBox nameBox = new TextBox { Dock = DockStyle.Fill };

        private readonly ComboBox rootType = new ComboBox {
            Dock = DockStyle.Fill,
            DropDownStyle = ComboBoxStyle.DropDownList
        };

        private readonly NumericUpDown width = Dimension(DefaultWidth);
        private readonly NumericUpDown height = Dimension(DefaultHeight);

        private readonly Button accept = new Button { Text = "Create", DialogResult = DialogResult.None };
        private readonly Button cancel = new Button { Text = "Cancel", DialogResult = DialogResult.Cancel };

        private readonly Label problem = new Label {
            AutoSize = true,
            Dock = DockStyle.Fill,
            ForeColor = Color.Firebrick
        };

        /// <summary>Whether the root component is being chosen, or copied from somewhere.</summary>
        private readonly bool choosingRoot;

        /// <summary>
        ///     Builds the dialog.
        /// </summary>
        /// <remarks>
        ///     A copy is named and nothing else: its components come from the source, so offering a
        ///     root type and a size would be offering to change something this operation does not
        ///     touch.
        /// </remarks>
        /// <param name="groupId">The group id the interface will take, shown so it is not a surprise.</param>
        /// <param name="copiedFrom">
        ///     The interface being copied, or <c>null</c> when a new one is being made.
        /// </param>
        public NewInterfaceDialog(int groupId, string? copiedFrom = null) {
            choosingRoot = copiedFrom == null;
            Text = copiedFrom == null ? "New interface" : "Duplicate " + copiedFrom;
            StartPosition = FormStartPosition.CenterParent;
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MinimizeBox = false;
            MaximizeBox = false;
            ShowInTaskbar = false;
            Font = EditorTheme.UiFont;
            AutoSize = true;
            AutoSizeMode = AutoSizeMode.GrowAndShrink;

            rootType.Items.Add(InterfaceComponentEdits.RootComponentType.Container);
            rootType.Items.Add(InterfaceComponentEdits.RootComponentType.Rectangle);
            rootType.SelectedIndex = 0;

            BuildLayout(groupId);

            accept.Click += (_, _) => {
                if (!Validate(out string? why)) {
                    problem.Text = why;
                    return;
                }

                DialogResult = DialogResult.OK;
                Close();
            };
        }

        /// <summary>The name typed, trimmed.</summary>
        [Browsable(false)]
        [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
        public string InterfaceName => nameBox.Text.Trim();

        /// <summary>What the root component should be.</summary>
        [Browsable(false)]
        [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
        public InterfaceComponentEdits.RootComponentType RootType =>
            (InterfaceComponentEdits.RootComponentType) rootType.SelectedItem!;

        /// <summary>The root's width in pixels.</summary>
        [Browsable(false)]
        [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
        public int RootWidth => (int) width.Value;

        /// <summary>The root's height in pixels.</summary>
        [Browsable(false)]
        [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
        public int RootHeight => (int) height.Value;

        private static NumericUpDown Dimension(int initial) {
            return new NumericUpDown {
                Dock = DockStyle.Fill,
                Minimum = 0,
                Maximum = MaxDimension,
                Value = initial
            };
        }

        /// <summary>
        ///     Whether what has been typed can be written.
        /// </summary>
        /// <remarks>
        ///     Only the name is checked, and only for being present. Whether it is already taken is
        ///     settled by <see cref="InterfaceGroupWriter"/> against the cache, because this dialog
        ///     has none and a check made here would be a second opinion that could disagree.
        /// </remarks>
        /// <param name="why">What is wrong, when something is.</param>
        /// <returns>Whether the dialog can close.</returns>
        private bool Validate(out string? why) {
            if (string.IsNullOrWhiteSpace(nameBox.Text)) {
                why = "An interface has to be named. The name is stored as a hash and recovered by"
                    + " re-hashing candidates, so one left unnamed now cannot be named later.";
                return false;
            }

            why = null;
            return true;
        }

        private void BuildLayout(int groupId) {
            //AutoSize throughout rather than stated sizes, so a row is as tall as the control it
            //holds needs to be rather than as tall as a number written here guessed.
            var root = new TableLayoutPanel {
                AutoSize = true,
                AutoSizeMode = AutoSizeMode.GrowAndShrink,
                ColumnCount = 2,
                Dock = DockStyle.Fill,
                Padding = new Padding(8)
            };
            root.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            root.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));

            AddRow(root, "Group id", new Label {
                AutoSize = true,
                Dock = DockStyle.Fill,
                Text = groupId.ToString(),
                TextAlign = ContentAlignment.MiddleLeft
            });

            AddRow(root, "Name", nameBox);

            if (choosingRoot) {
                AddRow(root, "Root", rootType);
                AddRow(root, "Width", width);
                AddRow(root, "Height", height);
            }

            root.Controls.Add(problem, 1, root.RowCount);
            root.RowCount++;

            var buttons = new FlowLayoutPanel {
                AutoSize = true,
                AutoSizeMode = AutoSizeMode.GrowAndShrink,
                Dock = DockStyle.Fill,
                FlowDirection = FlowDirection.RightToLeft
            };
            buttons.Controls.Add(cancel);
            buttons.Controls.Add(accept);

            root.Controls.Add(buttons, 1, root.RowCount);
            root.RowCount++;

            Controls.Add(root);
            AcceptButton = accept;
            CancelButton = cancel;
        }

        private static void AddRow(TableLayoutPanel table, string caption, Control control) {
            table.Controls.Add(new Label {
                AutoSize = true,
                Dock = DockStyle.Fill,
                Text = caption,
                TextAlign = ContentAlignment.MiddleLeft
            }, 0, table.RowCount);

            table.Controls.Add(control, 1, table.RowCount);
            table.RowCount++;
        }
    }
}
