using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Windows.Forms;
using FlashEditor.Cache;
using FlashEditor.Definitions.Editing;
using static FlashEditor.Utils.DebugUtil;
using FlashEditor.IO;
using FlashEditor.UI;

namespace FlashEditor.Definitions.LoadingSprites {
    /// <summary>
    ///     The Loading Sprites tab: index 32, both of the formats it holds, side by side.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///     <b>The index is mixed and that is the whole design problem.</b> Twenty-one of its
    ///     twenty-six groups are JPEG images and five are 256-frame Jagex glyph sheets, so a tab that
    ///     presented one format would show a fifth of the index as broken. The list therefore carries
    ///     a shape column, the preview draws whichever picture the row's shape asks for, and the
    ///     header states the discriminator - the payload's own <c>FF D8</c> magic, never the index id
    ///     and never the constant's name, which says "in jpg format" and is wrong about five groups.
    ///     </para>
    ///     <para>
    ///     <b>The JPEG colour is the one thing on this tab that can be wrong and look right.</b> These
    ///     files are four-component with no <c>JFIF APP0</c> and no <c>Adobe APP14</c>, so every
    ///     general-purpose decoder falls back to CMYK and produces a recognisable, plausible, wrong
    ///     picture. The preview goes through <see cref="JpegRaster.ToArgb"/>, whose reading is settled
    ///     by the files' own quantisation tables and sampling factors, and the note above the picture
    ///     says so - because a user comparing this tab against the game has no other way to tell a
    ///     defect from a documented choice.
    ///     </para>
    ///     <para>
    ///     <b>Replacing is not transcoding.</b> <c>LoadingSpriteDefinition.Encode</c> returns a JPEG
    ///     group's stored bytes verbatim, so there is no encoder to push an edited picture through:
    ///     the only edit this index supports is substituting one file for another. The tab offers
    ///     exactly that, gates it on <see cref="LoadingSpriteJpegPolicy"/>, and states both the cost
    ///     and the accepted shape next to the button.
    ///     </para>
    ///     <para>
    ///     <b>Which group is the loading-screen background cannot be settled from the client, and
    ///     the tab says so rather than implying an answer.</b> Index 32 is opened into
    ///     <c>Class1.aJS5Archive_67</c> (<c>InterfaceSettings.java:73-74</c>), or index 34 into the
    ///     same field when the AWT decoder fails the probe. Only three groups are ever asked for by
    ///     name - <c>p11_full</c>, <c>p12_full</c> and <c>b12_full</c>, at
    ///     <c>Class84.java:23,25,26</c> - and all three are glyph sheets. Every picture the client
    ///     draws from this index arrives through <c>Class237_Sub1.method2915</c>
    ///     (<c>Class237_Sub1.java:23</c>) with a group id its caller supplies, and the full-screen
    ///     one is <c>Class5.java:86</c>, which reads <c>Class367.anInt3544</c>. That field is a
    ///     <c>readUnsignedShort</c> off an index-33 loading-screen record
    ///     (<c>Node_Sub46_Sub19.java:35-36</c>, reached for element type byte 8 via
    ///     <c>Class4.java:17</c> and <c>Class362.java:125-127</c>), and which record is used is
    ///     chosen from a client preference against a shuffled table (<c>InterfaceSettings.java:95,
    ///     100</c>, <c>Class282.java:150-163</c>). So the client carries no constant, no name and no
    ///     hash for the background, more than one of the twenty-one images may be one, and the
    ///     question is answerable only by reading index 33 - which nothing in this project does yet.
    ///     An id guessed from ordering or from file size would be exactly the plausible,
    ///     unverifiable claim this cache rewards.
    ///     </para>
    /// </remarks>
    public sealed class LoadingSpriteEditorPanel : UserControl {
        /* Consolas 9 on every child. The form puts Consolas 12 on the tab control and everything
           under it inherits, which is half again what these grids are laid out for. */
        private static readonly Font GridFont = new Font("Consolas", 9F);

        private const string NoCacheText = "No cache loaded";
        private const string NoSelectionText = "Select a group to see what it holds";

        //AutoSize rather than a stated height, so a wrapped line is a line that fits. The form is
        //AutoScaleMode.Dpi and a literal height would be scaled into something that clips text.
        private readonly Label header = new Label {
            AutoSize = true,
            Dock = DockStyle.Top,
            Font = GridFont,
            Text = NoCacheText
        };

        /* Behind an (i). The clause that stops a wrong inference - that this tab cannot tell you
           which picture is the loading screen - stays on screen as the summary, because it is the
           question a user arrives with. */
        private readonly InfoAffordance notice = new InfoAffordance {
            Dock = DockStyle.Top,
            Font = GridFont,
            Kind = InfoKind.Limitation,
            Caption = "Which picture is the backdrop cannot be answered here",
            Summary = "Which picture is the backdrop cannot be answered here",
            Body = "Shape is decided by the payload's own FF D8 magic, not by the index: RSConstants calls " +
                   "index 32 \"loading sprites in jpg format\" and five of its groups are Jagex glyph sheets." +
                   Environment.NewLine +
                   "This tab cannot tell you which image is the loading-screen background. The client names no " +
                   "group here except the three font sheets; every picture it draws is fetched by an id read out " +
                   "of index 33 at runtime, so which one is the backdrop is a property of that data and not of " +
                   "the client. Do not infer it from a group's id or its size."
        };

        private readonly DefinitionListPanel groups = new DefinitionListPanel {
            //Bound with a null cache before a cache arrives so the grid keeps its headings, and the
            //panel's own default would then claim no cache is loaded.
            EmptyMessage = NoCacheText
        };

        private readonly Label previewNote = new Label {
            AutoSize = true,
            Dock = DockStyle.Top,
            Font = GridFont,
            Text = NoSelectionText
        };

        /* A neutral dark grey rather than black or a system colour. The whole point of this preview
           is that its colour can be judged by eye, and a background that is itself a colour biases
           that judgement. */
        private readonly PictureBox preview = new PictureBox {
            BackColor = Color.FromArgb(0xFF, 0x28, 0x28, 0x28),
            Dock = DockStyle.Fill,
            SizeMode = PictureBoxSizeMode.Zoom
        };

        private readonly DetailFieldGrid fields = new DetailFieldGrid();

        /* No splitter states a minimum size. Setting one re-checks the current distance against it,
           and a container is still at its 150x100 default when a field initialiser runs. */
        private readonly SplitContainer listAndPreview = new SplitContainer {
            Dock = DockStyle.Fill,
            Orientation = Orientation.Vertical
        };

        private readonly SplitContainer previewAndFields = new SplitContainer {
            Dock = DockStyle.Fill,
            Orientation = Orientation.Horizontal
        };

        private readonly FlowLayoutPanel actions = new FlowLayoutPanel {
            AutoSize = true,
            Dock = DockStyle.Bottom,
            FlowDirection = FlowDirection.LeftToRight,
            WrapContents = true
        };

        private readonly Button exportImage = new Button {
            AutoSize = true,
            Enabled = false,
            Font = GridFont,
            Text = "Export picture as PNG..."
        };

        private readonly Button exportStored = new Button {
            AutoSize = true,
            Enabled = false,
            Font = GridFont,
            Text = "Export stored bytes..."
        };

        private readonly Button replaceStored = new Button {
            AutoSize = true,
            Enabled = false,
            Font = GridFont,
            Text = "Replace stored bytes..."
        };

        /* The accepted shape is spliced in from the policy rather than retyped, so the limit this
           note states and the limit ReplaceStored enforces cannot drift apart. A refusal the user
           was never warned about reads as the tab being broken.
           It sits on the actions strip beside the button it is about rather than docked under it:
           the whole paragraph is about what pressing Replace does. */
        private readonly InfoAffordance cost = new InfoAffordance {
            Font = GridFont,
            Kind = InfoKind.Cost,
            Caption = "What replacing costs",
            Summary = "What replacing costs",
            Body = "Replace stores the file you pick byte for byte - there is no transcode, so what the client " +
                   "sees is your file and not a re-encoding of it. It rewrites the group's CRC and the " +
                   "reference-table entry of every archive packed beside it, and stages the change; nothing " +
                   "reaches disk until the cache is saved." + Environment.NewLine +
                   "Only the shape every index-32 image and the client's own probe image carry is accepted: " +
                   LoadingSpriteJpegPolicy.AcceptedShapeInWords + ". An ordinary three-component JFIF - what " +
                   "any normal tool saves - is refused, because this editor's colour reading is inferred from " +
                   "that four-component layout and nothing establishes what the client would draw instead."
        };

        private readonly Label status = new Label {
            AutoSize = true,
            Font = GridFont,
            Text = string.Empty
        };

        private RSCache? cache;
        private Bitmap? shown;
        private bool splitterPlaced;

        /// <summary>Creates the panel with its grid headings already in place.</summary>
        public LoadingSpriteEditorPanel() {
            Dock = DockStyle.Fill;

            BuildLayout();

            groups.SelectedRowChanged += (_, _) => ShowGroup(groups.SelectedRow as LoadingSpriteListing);
            exportImage.Click += (_, _) => ExportImage();
            exportStored.Click += (_, _) => ExportStored();
            replaceStored.Click += (_, _) => ReplaceStored();
        }

        /// <summary>
        ///     Points the tab at a cache, or clears it when given none.
        /// </summary>
        /// <remarks>
        ///     <c>Editor.LoadEditorTab</c> calls this on every visit to the tab, so a rebind of the
        ///     cache already on display has to be a no-op or the selection and the decoded pictures
        ///     are thrown away each time. Identity is the right test because opening a cache builds a
        ///     new <see cref="RSCache"/>.
        /// </remarks>
        /// <param name="newCache">The open cache, or null to unbind.</param>
        public void Bind(RSCache? newCache) {
            if (ReferenceEquals(newCache, cache))
                return;

            cache = newCache;
            ShowGroup(null);
            Reload();
        }

        /// <summary>Releases the bitmap the preview is holding.</summary>
        /// <param name="disposing">Whether managed state should be released.</param>
        protected override void Dispose(bool disposing) {
            if (disposing) {
                preview.Image = null;
                shown?.Dispose();
                shown = null;
            }

            base.Dispose(disposing);
        }

        /// <summary>Places the splitters once the layout pass has given them a real size.</summary>
        /// <param name="levent">The event data.</param>
        protected override void OnLayout(LayoutEventArgs levent) {
            base.OnLayout(levent);
            PlaceSplitters();
            WrapNotices();
        }

        /// <summary>
        ///     Lets the explanatory labels wrap instead of running off the right edge.
        /// </summary>
        /// <remarks>
        ///     An <c>AutoSize</c> label docked to an edge grows sideways and is clipped by its
        ///     container; it only wraps once its <see cref="Control.MaximumSize"/> states a width, and
        ///     then <c>AutoSize</c> gives it the height the wrapped text needs. Measured rather than
        ///     stated, because these labels carry the sentences that say what the tab cannot do and a
        ///     sentence cut off half way through is worse than one that was never written. The
        ///     preview note is bound to its own splitter panel rather than to the form, since that is
        ///     the width it actually has.
        ///     <para>
        ///     Assigning a maximum size lays the panel out again, so each is written only when it
        ///     changes; without that this recurses until the layout engine gives up.
        ///     </para>
        /// </remarks>
        private void WrapNotices() {
            Wrap(header, ClientSize.Width);
            Wrap(previewNote, previewAndFields.Panel1.ClientSize.Width);
        }

        private static void Wrap(Label label, int width) {
            if (width > 0 && label.MaximumSize.Width != width)
                label.MaximumSize = new Size(width, 0);
        }

        /// <summary>
        ///     Divides the panel proportionally, once, when it first has a size worth dividing.
        /// </summary>
        /// <remarks>
        ///     A <see cref="SplitContainer"/> defaults to a splitter distance of 50 <i>pixels</i>, not
        ///     half, so the distance has to be stated - and stating it in a designer would make it one
        ///     more literal the form scales by its DPI factor.
        /// </remarks>
        private void PlaceSplitters() {
            if (splitterPlaced || listAndPreview.Width < 400 || previewAndFields.Height < 200)
                return;

            //Set before the assignments, not after: changing a splitter distance lays the panel out
            //again, and this is called from that layout.
            splitterPlaced = true;

            try {
                //Two fifths to the list. The grid holds six narrow columns and the picture is the
                //thing being judged, so the picture gets the larger share.
                listAndPreview.SplitterDistance =
                    Math.Max(listAndPreview.Panel1MinSize, listAndPreview.Width * 2 / 5);
                previewAndFields.SplitterDistance =
                    Math.Max(previewAndFields.Panel1MinSize, previewAndFields.Height * 2 / 3);
            }
            catch (InvalidOperationException ex) {
                splitterPlaced = false;
                Debug("Loading sprites tab splitters not placed yet: " + ex.Message, LOG_DETAIL.ADVANCED);
            }
        }

        private void BuildLayout() {
            actions.Controls.Add(exportImage);
            actions.Controls.Add(exportStored);
            actions.Controls.Add(replaceStored);
            actions.Controls.Add(cost);
            actions.Controls.Add(status);

            previewAndFields.Panel1.Controls.Add(preview);
            previewAndFields.Panel1.Controls.Add(previewNote);
            previewAndFields.Panel2.Controls.Add(fields);

            listAndPreview.Panel1.Controls.Add(groups);
            listAndPreview.Panel2.Controls.Add(previewAndFields);

            //Docking resolves from the end of the Controls collection backwards, so the strips have
            //to be added after the filled splitter and in inside-out order among themselves.
            Controls.Add(listAndPreview);
            Controls.Add(actions);
            Controls.Add(notice);
            Controls.Add(header);

            //Named for a screen reader only. InfoAffordance does not reparent or position itself
            //from this, so each still has to be placed.
            notice.Describes = groups;
            cost.Describes = replaceStored;

            //Bound before any cache arrives so the grid has headings from the start.
            groups.Bind(null, new LoadingSpriteListDescriptor());
        }

        /// <summary>
        ///     Reloads the list against the bound cache.
        /// </summary>
        /// <remarks>
        ///     A fresh descriptor every time, because <c>DefinitionListPanel.Bind</c> treats the same
        ///     cache and descriptor pair as the same thing to show and would leave the previous rows
        ///     on screen. That is what makes this usable after a replace, where the stored length and
        ///     the picture of one row have both changed.
        /// </remarks>
        private void Reload() {
            if (cache == null) {
                header.Text = NoCacheText;
                groups.Bind(null, new LoadingSpriteListDescriptor());
                return;
            }

            int declared = cache.EnumerateFiles(RSConstants.LOADING_SPRITES).Count();
            header.Text = "Index 32 - " + declared + " group(s) declared, one file each. " +
                          "The pre-login art store, and the only index holding two unrelated payload formats " +
                          "with no flag to tell them apart.";

            groups.Bind(cache, new LoadingSpriteListDescriptor());
        }

        /// <summary>Draws the selected row's picture and fills the field grid from it.</summary>
        /// <remarks>
        ///     No cache read: the row already carries its rendered pixels, decoded on the list
        ///     panel's worker. All that happens on the UI thread is the copy into a bitmap.
        /// </remarks>
        /// <param name="listing">The selected row, or null.</param>
        private void ShowGroup(LoadingSpriteListing? listing) {
            fields.ShowFields(listing);

            exportImage.Enabled = listing?.Preview.HasImage == true;
            exportStored.Enabled = listing != null;
            //Only the JPEG half can be replaced. A glyph sheet's bytes are 256 pixel planes read
            //backwards from the end of the file, and there is nothing that turns a picture the user
            //picked into one.
            replaceStored.Enabled = listing?.Shape == LoadingSpriteShape.JpegImage && cache != null;

            preview.Image = null;
            shown?.Dispose();
            shown = null;

            if (listing == null) {
                previewNote.Text = cache == null ? NoCacheText : NoSelectionText;
                return;
            }

            previewNote.Text = listing.Summary + Environment.NewLine + listing.Preview.Note;

            if (!listing.Preview.HasImage)
                return;

            shown = ToBitmap(listing.Preview);
            preview.Image = shown;
        }

        /// <summary>
        ///     Copies rendered pixels into a bitmap.
        /// </summary>
        /// <remarks>
        ///     A block copy rather than <c>SetPixel</c>: the largest image in this index is over
        ///     200,000 pixels and <c>SetPixel</c> locks and unlocks the bitmap once each. The layouts
        ///     line up exactly - a .NET <c>int</c> is little-endian on every platform this builds for,
        ///     so 0xAARRGGBB in memory is the B, G, R, A byte order <c>Format32bppArgb</c> wants.
        /// </remarks>
        /// <param name="picture">The rendered picture.</param>
        /// <returns>The bitmap.</returns>
        private static Bitmap ToBitmap(LoadingSpritePreview picture) {
            var bitmap = new Bitmap(picture.Width, picture.Height, PixelFormat.Format32bppArgb);
            BitmapData data = bitmap.LockBits(new Rectangle(0, 0, picture.Width, picture.Height),
                ImageLockMode.WriteOnly, PixelFormat.Format32bppArgb);

            try {
                //Row by row rather than one copy: LockBits may hand back a stride wider than the row,
                //and a single copy would then shear the image.
                for (int y = 0; y < picture.Height; y++) {
                    Marshal.Copy(picture.Pixels, y * picture.Width,
                        data.Scan0 + y * data.Stride, picture.Width);
                }
            }
            finally {
                bitmap.UnlockBits(data);
            }

            return bitmap;
        }

        private void ExportImage() {
            if (groups.SelectedRow is not LoadingSpriteListing listing || !listing.Preview.HasImage)
                return;

            using var dialog = new SaveFileDialog {
                Filter = "PNG image (*.png)|*.png",
                FileName = "index32_group" + listing.GroupId + ".png"
            };

            if (dialog.ShowDialog(this) != DialogResult.OK)
                return;

            try {
                using Bitmap bitmap = ToBitmap(listing.Preview);
                bitmap.Save(dialog.FileName, ImageFormat.Png);
                status.Text = "Wrote " + listing.Preview.Width + "x" + listing.Preview.Height + " to " +
                              Path.GetFileName(dialog.FileName);
            }
            catch (Exception ex) {
                //Reported rather than thrown: an exception out of a button handler takes the form down.
                status.Text = "Export failed: " + ex.Message;
                Debug("Loading sprite PNG export failed: " + ex);
            }
        }

        private void ExportStored() {
            if (groups.SelectedRow is not LoadingSpriteListing listing)
                return;

            //The extension follows the shape rather than the index. A glyph sheet given a .jpg name
            //is a file nothing can open and a name that says the index is JPEG-only, which is the
            //belief this tab exists to correct.
            bool isJpeg = listing.Shape == LoadingSpriteShape.JpegImage;
            using var dialog = new SaveFileDialog {
                Filter = isJpeg
                    ? "JPEG image (*.jpg)|*.jpg|All files (*.*)|*.*"
                    : "Jagex sprite set (*.dat)|*.dat|All files (*.*)|*.*",
                FileName = "index32_group" + listing.GroupId + (isJpeg ? ".jpg" : ".dat")
            };

            if (dialog.ShowDialog(this) != DialogResult.OK)
                return;

            try {
                File.WriteAllBytes(dialog.FileName, listing.StoredBytes);
                status.Text = "Wrote " + listing.StoredLength.ToString("N0", CultureInfo.InvariantCulture) +
                              " stored bytes to " + Path.GetFileName(dialog.FileName);
            }
            catch (Exception ex) {
                status.Text = "Export failed: " + ex.Message;
                Debug("Loading sprite byte export failed: " + ex);
            }
        }

        /// <summary>
        ///     Substitutes a file for the selected group's stored bytes.
        /// </summary>
        /// <remarks>
        ///     <para>
        ///     The tab's whole claim is that the picture on screen is the picture the client will
        ///     draw. <see cref="LoadingSpriteJpegPolicy"/> is what keeps that claim true across a
        ///     replace, and the dangerous case is not a file that fails to render - it is one that
        ///     renders beautifully and is a different kind of file to everything the index holds. A
        ///     three-component JFIF is exactly that, so "did it produce a picture" is deliberately
        ///     not the test.
        ///     </para>
        ///     <para>
        ///     The bytes are staged exactly as read. Re-encoding them would change the stored bytes of
        ///     an image nobody edited, and a JPEG re-encode is no more reproducible than a GZip one.
        ///     </para>
        /// </remarks>
        private void ReplaceStored() {
            if (cache == null || groups.SelectedRow is not LoadingSpriteListing listing ||
                listing.Shape != LoadingSpriteShape.JpegImage)
                return;

            using var dialog = new OpenFileDialog {
                Filter = "JPEG image (*.jpg;*.jpeg)|*.jpg;*.jpeg|All files (*.*)|*.*"
            };

            if (dialog.ShowDialog(this) != DialogResult.OK)
                return;

            try {
                byte[] bytes = File.ReadAllBytes(dialog.FileName);

                if (bytes.AsSpan().SequenceEqual(listing.StoredBytes)) {
                    //A save that changes nothing must write nothing: re-storing identical bytes still
                    //rewrites the archive CRC and drags in the reference-table entry beside it.
                    status.Text = "No change: that file is byte for byte what group " + listing.GroupId +
                                  " already holds.";
                    return;
                }

                //One gate, and it is the whole safety margin. A three-component JFIF previews here as a
                //perfectly good picture - JpegRaster reads planes 0, 1 and 2 as Y, Cb and Cr whether
                //there are three planes or four - so "it rendered" is no evidence that the client would
                //draw it, and without this check it would be stored with nothing said.
                if (!LoadingSpriteJpegPolicy.TryAccept(bytes, out JagexJpeg? accepted, out string refusal)) {
                    status.Text = "Refused: " + refusal;
                    return;
                }

                cache.WriteFile(RSConstants.LOADING_SPRITES, listing.Address.GroupId, listing.Address.FileId,
                    new JagStream(bytes));

                status.Text = "Staged group " + listing.GroupId + ": " +
                              bytes.Length.ToString("N0", CultureInfo.InvariantCulture) + " bytes stored verbatim, " +
                              accepted!.Width + "x" + accepted.Height + ". Save the cache to write it.";

                //Reloaded rather than patched in place, so the stored length and the picture in the
                //grid cannot disagree with what was just staged.
                Reload();
            }
            catch (Exception ex) {
                status.Text = "Replace failed: " + ex.Message;
                Debug("Loading sprite replace failed: " + ex);
            }
        }
    }
}
