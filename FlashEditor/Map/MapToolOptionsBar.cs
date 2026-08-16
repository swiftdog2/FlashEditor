using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Drawing;
using System.Windows.Forms;
using FlashEditor.UI;

namespace FlashEditor.Map {
    /// <summary>
    ///     Which option groups a tool needs on the bar.
    /// </summary>
    /// <remarks>
    ///     Flags rather than one enum per tool, because the groups genuinely combine: the overlay
    ///     brush wants an id, a brush footprint, a shape and a rotation, and the underlay brush
    ///     wants the first two of those. Spelled per tool it would be a table with the same entry
    ///     copied five times.
    /// </remarks>
    [Flags]
    public enum MapToolOptions {
        /// <summary>The tool takes no options at all.</summary>
        None = 0,

        /// <summary>An underlay id, capped at what one byte can carry.</summary>
        UnderlayId = 1 << 0,

        /// <summary>An overlay id.</summary>
        OverlayId = 1 << 1,

        /// <summary>An object definition id, with the asset picker behind it.</summary>
        ObjectId = 1 << 2,

        /// <summary>How many tiles across the brush is, and what outline it stamps.</summary>
        Brush = 1 << 3,

        /// <summary>The overlay tile shape and rotation a paint writes.</summary>
        OverlayForm = 1 << 4,

        /// <summary>Which field the wand matches on and how far from it still counts.</summary>
        Wand = 1 << 5
    }

    /// <summary>
    ///     The options for whichever tool is armed, labelled with what they mean.
    /// </summary>
    /// <remarks>
    ///     <b>What this replaces, and why it was worth replacing.</b> One unlabelled
    ///     <see cref="NumericUpDown"/> sat under the tool combo and silently meant "underlay id",
    ///     "overlay id" or "object definition id" depending on the combo above it, with nothing on
    ///     screen saying which. A user who armed the overlay brush, typed 40 and painted got floor
    ///     overlay 40 or object 40 depending on a selection they made a minute earlier, and the two
    ///     mistakes look identical until the map redraws.
    ///     <para>
    ///     <b>So the label is the feature.</b> Every id box on this bar carries the noun it holds -
    ///     "Underlay id", "Overlay id", "Object id" - and the boxes are separate controls rather than
    ///     one relabelled box, so switching tools cannot carry an overlay id into an object field.
    ///     </para>
    ///     <para>
    ///     <b>Two things here exist because the tools used to be the only way to reach them.</b> An
    ///     overlay's shape and rotation could previously only be changed by painting the overlay and
    ///     then clicking the tile once per step with two further tools, so laying a shaped overlay
    ///     was a three-tool operation whose intermediate states were written to the square. They are
    ///     brush settings now and the cycle tools stay for adjusting what is already there.
    ///     </para>
    ///     <para>
    ///     <b>The per-tool note is an <see cref="InfoAffordance"/>, not a docked paragraph.</b> It
    ///     changes with the tool, so a paragraph would have to be the union of every tool's caveats
    ///     and would be read by nobody. The height tools' note is the one that discharges
    ///     <c>CLAUDE.md</c>'s "mark what an edit will cost", and it is an
    ///     <see cref="InfoKind.Cost"/> for that reason.
    ///     </para>
    /// </remarks>
    public sealed class MapToolOptionsBar : UserControl {
        private readonly FlowLayoutPanel flow = new FlowLayoutPanel {
            Dock = DockStyle.Fill,
            FlowDirection = FlowDirection.LeftToRight,
            WrapContents = true,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            Margin = Padding.Empty,
            Padding = new Padding(2)
        };

        private readonly NumericUpDown underlayId = Spin(0, MapToolLimits.MaximumUnderlayId, 1);
        private readonly NumericUpDown overlayId = Spin(0, MapToolLimits.MaximumOverlayId, 1);
        private readonly NumericUpDown objectId = Spin(0, MapToolLimits.MaximumLocationId, 0);
        private readonly NumericUpDown brushSize = Spin(1, MapBrush.MaximumSize, 1);
        private readonly NumericUpDown overlayShape = Spin(0, TileShapes.FileShapeCount - 1, 0);
        private readonly NumericUpDown overlayRotation = Spin(0, 3, 0);
        private readonly NumericUpDown wandTolerance = Spin(0, MapWand.MaximumTolerance, 0);

        private readonly ComboBox brushShape = new ComboBox {
            DropDownStyle = ComboBoxStyle.DropDownList, Width = 90
        };

        private readonly ComboBox wandField = new ComboBox {
            DropDownStyle = ComboBoxStyle.DropDownList, Width = 90
        };

        private readonly Button pickObject = new Button {
            Text = "Browse...", AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink
        };

        private readonly InfoAffordance note = new InfoAffordance();

        //Every group, so ShowFor can hide the lot and then reveal only what the tool asked for.
        private readonly List<(MapToolOptions Needs, Control Group)> groups = new();

        /// <summary>Builds the bar with every group present and hidden.</summary>
        public MapToolOptionsBar() {
            Dock = DockStyle.Fill;
            AutoSize = true;
            AutoSizeMode = AutoSizeMode.GrowAndShrink;
            Margin = Padding.Empty;

            brushShape.Items.AddRange(new object[] { "Square", "Round", "Diamond" });
            brushShape.SelectedIndex = 0;

            wandField.Items.AddRange(new object[] { "Underlay", "Overlay" });
            wandField.SelectedIndex = 0;

            Add(MapToolOptions.UnderlayId, "Underlay id", underlayId);
            Add(MapToolOptions.OverlayId, "Overlay id", overlayId);
            Add(MapToolOptions.ObjectId, "Object id", objectId, pickObject);
            Add(MapToolOptions.Brush, "Brush", brushSize, brushShape);
            Add(MapToolOptions.OverlayForm, "Shape", overlayShape, RotationLabel(), overlayRotation);
            Add(MapToolOptions.Wand, "Match", wandField, ToleranceLabel(), wandTolerance);

            note.Margin = new Padding(8, 6, 4, 2);
            flow.Controls.Add(note);

            Controls.Add(flow);

            foreach (NumericUpDown box in new[] {
                         underlayId, overlayId, objectId, brushSize,
                         overlayShape, overlayRotation, wandTolerance
                     })
                box.ValueChanged += (_, _) => Changed?.Invoke(this, EventArgs.Empty);

            brushShape.SelectedIndexChanged += (_, _) => Changed?.Invoke(this, EventArgs.Empty);
            wandField.SelectedIndexChanged += (_, _) => Changed?.Invoke(this, EventArgs.Empty);
            pickObject.Click += (_, _) => PickObjectRequested?.Invoke(this, EventArgs.Empty);
        }

        /// <summary>Raised when any option the armed tool reads has changed.</summary>
        public event EventHandler? Changed;

        /// <summary>Raised when the user asks to choose an object id by looking at one.</summary>
        public event EventHandler? PickObjectRequested;

        /// <summary>The underlay id the brush paints.</summary>
        [Browsable(false)]
        [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
        public int UnderlayId {
            get => (int) underlayId.Value;
            set => underlayId.Value = Clamp(underlayId, value);
        }

        /// <summary>The overlay id the brush paints.</summary>
        [Browsable(false)]
        [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
        public int OverlayId {
            get => (int) overlayId.Value;
            set => overlayId.Value = Clamp(overlayId, value);
        }

        /// <summary>The object definition id the place tool writes.</summary>
        [Browsable(false)]
        [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
        public int ObjectId {
            get => (int) objectId.Value;
            set => objectId.Value = Clamp(objectId, value);
        }

        /// <summary>How many tiles across the brush is.</summary>
        [Browsable(false)]
        [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
        public int BrushSize => (int) brushSize.Value;

        /// <summary>The outline the brush stamps.</summary>
        [Browsable(false)]
        [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
        public MapBrushShape BrushShape => (MapBrushShape) Math.Max(0, brushShape.SelectedIndex);

        /// <summary>The overlay tile shape a paint writes.</summary>
        [Browsable(false)]
        [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
        public byte OverlayShape {
            get => (byte) overlayShape.Value;
            set => overlayShape.Value = Clamp(overlayShape, value);
        }

        /// <summary>The overlay rotation a paint writes.</summary>
        [Browsable(false)]
        [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
        public byte OverlayRotation {
            get => (byte) overlayRotation.Value;
            set => overlayRotation.Value = Clamp(overlayRotation, value);
        }

        /// <summary>Which id the wand matches on.</summary>
        [Browsable(false)]
        [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
        public MapWandField WandField =>
            wandField.SelectedIndex == 1 ? MapWandField.Overlay : MapWandField.Underlay;

        /// <summary>How far from the clicked id the wand still counts as a match.</summary>
        [Browsable(false)]
        [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
        public int WandTolerance => (int) wandTolerance.Value;

        /// <summary>
        ///     Shows only the groups a tool needs, and the note that belongs to it.
        /// </summary>
        /// <remarks>
        ///     The layout is suspended around the whole pass. Toggling six groups one at a time
        ///     re-flows the bar six times, and on a wrapping flow each of those can change the bar's
        ///     height, which the containing table then answers by resizing the canvas.
        /// </remarks>
        /// <param name="needed">The groups the armed tool reads.</param>
        /// <param name="noteKind">Which obligation the tool's note discharges.</param>
        /// <param name="noteCaption">The heading over the note, or empty for the kind's own.</param>
        /// <param name="noteBody">The note, or empty to hide the glyph entirely.</param>
        public void ShowFor(MapToolOptions needed, InfoKind noteKind, string noteCaption, string noteBody) {
            SuspendLayout();
            flow.SuspendLayout();

            try {
                foreach ((MapToolOptions needs, Control group) in groups)
                    group.Visible = (needed & needs) != 0;

                note.Kind = noteKind;
                note.Caption = noteCaption ?? string.Empty;
                note.Body = noteBody ?? string.Empty;
                note.Visible = !string.IsNullOrEmpty(noteBody);
            }
            finally {
                flow.ResumeLayout(true);
                ResumeLayout(true);
            }
        }

        /// <summary>
        ///     Builds one labelled group and remembers which tools want it.
        /// </summary>
        /// <remarks>
        ///     The label is not optional and there is no overload without one. That is the whole
        ///     point of this control: the box it replaced had no label and its meaning came from a
        ///     combo three rows away.
        /// </remarks>
        private void Add(MapToolOptions needs, string caption, params Control[] controls) {
            var group = new FlowLayoutPanel {
                FlowDirection = FlowDirection.LeftToRight,
                WrapContents = false,
                AutoSize = true,
                AutoSizeMode = AutoSizeMode.GrowAndShrink,
                Margin = new Padding(0, 0, 12, 0),
                Padding = Padding.Empty,
                Visible = false
            };

            var label = new Label {
                Text = caption,
                AutoSize = true,
                //No Top and no Bottom is what centres a label against the box beside it, rather than
                //a hand-tuned top padding that a font change would put out.
                Anchor = AnchorStyles.Left,
                Margin = new Padding(0, 6, 4, 0)
            };

            group.Controls.Add(label);
            foreach (Control control in controls) {
                control.Margin = new Padding(0, 2, 4, 2);
                group.Controls.Add(control);
                if (control.AccessibleName == null)
                    control.AccessibleName = caption;
            }

            flow.Controls.Add(group);
            groups.Add((needs, group));
        }

        private static Label RotationLabel() =>
            new Label { Text = "Rotation", AutoSize = true, Anchor = AnchorStyles.Left, Margin = new Padding(4, 6, 4, 0) };

        private static Label ToleranceLabel() =>
            new Label { Text = "Tolerance", AutoSize = true, Anchor = AnchorStyles.Left, Margin = new Padding(4, 6, 4, 0) };

        /// <summary>
        ///     A spin box sized from its own font rather than to a literal width.
        /// </summary>
        /// <remarks>
        ///     Five characters plus the spin buttons, measured live. A literal width is the failure
        ///     this panel has already had once: font scaling cut a 60-pixel combo to 40 and it
        ///     rendered "Pl".
        /// </remarks>
        private static NumericUpDown Spin(int minimum, int maximum, int value) {
            var box = new NumericUpDown { Minimum = minimum, Maximum = maximum, Value = value };
            box.Width = TextRenderer.MeasureText("00000", EditorTheme.UiFont).Width
                        + SystemInformation.VerticalScrollBarWidth + 8;
            return box;
        }

        private static decimal Clamp(NumericUpDown box, int value) =>
            Math.Clamp(value, (int) box.Minimum, (int) box.Maximum);
    }
}
