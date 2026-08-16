using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Globalization;
using System.Windows.Forms;
using FlashEditor.Cache;
using FlashEditor.Definitions.Editing;
using FlashEditor.Definitions.Entities;
using FlashEditor.Map;
using FlashEditor.UI;
using static FlashEditor.Utils.DebugUtil;

namespace FlashEditor.Definitions.Config {
    /// <summary>
    ///     Which record a config preview asked to navigate to.
    /// </summary>
    /// <remarks>
    ///     A place in the cache rather than a control action, so the host can hand it to the same
    ///     navigator a grid cell goes through and the back stack covers a jump from here exactly as
    ///     it covers one from a link column.
    /// </remarks>
    public sealed class ConfigNavigationEventArgs : EventArgs {
        /// <summary>Names a record to go to.</summary>
        /// <param name="indexId">The index the id addresses.</param>
        /// <param name="recordId">The record within it.</param>
        /// <param name="groupId">The group within index 2, or -1 for every other index.</param>
        public ConfigNavigationEventArgs(int indexId, int recordId, int groupId = -1) {
            IndexId = indexId;
            RecordId = recordId;
            GroupId = groupId;
        }

        /// <summary>The index the id addresses.</summary>
        public int IndexId { get; }

        /// <summary>The record within it.</summary>
        public int RecordId { get; }

        /// <summary>The group within index 2, or -1.</summary>
        public int GroupId { get; }
    }

    /// <summary>
    ///     What the selected config record <i>looks like</i>, for the eight families where the
    ///     numbers alone say nothing.
    /// </summary>
    /// <remarks>
    ///     <b>The gap this closes.</b> A cursor is a sprite id and two bytes; a damage mark is nine
    ///     numbers naming four sprites laid out left to right; a light curve is four integers
    ///     describing a waveform. None of those is legible as a row of digits, and until now the
    ///     only way to find out what one was was to open another tab, sort to the id, look, and come
    ///     back - for each of the four sprites in turn.
    ///     <para>
    ///     <b>Nothing here draws a family twice.</b> The two floor families are shown through
    ///     <see cref="FloorMaterialPalette"/>, the map tab's own palette, rather than through a
    ///     second swatch renderer that would drift from it; sprites come from
    ///     <see cref="SpriteThumbnailRenderer"/> uncomposited, which is the same decoder the sprite
    ///     tab and every thumbnail column use; the render animation set comes from
    ///     <see cref="NpcAnimationSet"/>, which is the route the entity page already resolves one
    ///     by.
    ///     </para>
    ///     <para>
    ///     <b>What it deliberately is not.</b> This is not the client's renderer. A damage mark is
    ///     composed at the offsets the client's own layout walk produces but with no drift, no fade
    ///     and no font - the number is drawn in the editor's font rather than the cache's, because
    ///     the cache's glyphs belong to the Fonts tab. A map element's polygon is drawn in tile
    ///     space with no map under it. Each of those is stated in the pane's own notice rather than
    ///     left for a user to discover by comparing it against the game.
    ///     </para>
    /// </remarks>
    public sealed class ConfigPreviewPanel : UserControl {
        /* Nothing here is drawn at a scaled size, because nothing in this application scales: the
           process is pinned DPI-unaware at FlashEditorForm.cs:46, so 16 logical pixels is 16
           physical pixels on every machine. */
        private const int SpriteSide = 64;
        private const int Inset = 10;

        private readonly Label caption = new Label {
            AutoSize = true,
            Dock = DockStyle.Top,
            Font = EditorTheme.NoticeFont,
            Text = ""
        };

        private readonly FloorMaterialPalette floors = new FloorMaterialPalette();
        private readonly PreviewCanvas canvas;

        /* One list for both families that have records to point at, rather than two controls that
           would each be hidden most of the time. Which way it docks is what differs: a render
           animation IS its animation list, so there it fills the pane; a quest has an icon to draw
           as well, so there it takes a strip under the canvas. */
        private readonly ListBox related = new ListBox {
            Font = new Font("Consolas", 9F),
            IntegralHeight = false
        };

        /* One renderer, uncomposited, for every sprite this pane draws. Composited would put the
           transparency checkerboard behind each piece, which is right when a sprite is the subject
           of a grid cell and wrong here: a hit splat is four sprites side by side and a cursor is
           one over a background, so the checkerboard would read as part of the picture. */
        private SpriteThumbnailRenderer? sprites;

        /* Decoded frames, kept per sprite group for as long as the cache is bound. A record names
           at most four, and a user walking a grid re-selects the same ones constantly. */
        private readonly Dictionary<int, Bitmap?> frames = new Dictionary<int, Bitmap?>();

        /* Drives the light curve. Stopped whenever the shown family is not group 31, because a
           timer ticking behind a static picture is a repaint per frame for nothing. */
        private readonly System.Windows.Forms.Timer clock =
            new System.Windows.Forms.Timer { Interval = 33 };

        private RSCache? cache;
        private ConfigFamily? family;
        private ConfigListing? listing;
        private int tick;

        /* The item opcode 132 join, inverted. Built the first time a quest is shown rather than on
           bind, because it decodes the whole of index 19 and most visits to this tab never open
           group 35. Dropped with the cache, never reused across one. */
        private QuestItemIndex? quests;

        /// <summary>Creates an empty preview.</summary>
        public ConfigPreviewPanel() {
            Dock = DockStyle.Fill;

            canvas = new PreviewCanvas(this) { Dock = DockStyle.Fill };
            floors.Visible = false;
            related.Visible = false;

            //Docking resolves from the end of the Controls collection backwards, so the filled
            //surfaces go in before the strips that take space off them.
            Controls.Add(canvas);
            Controls.Add(floors);
            Controls.Add(related);
            Controls.Add(caption);

            floors.Picked += OnFloorPicked;
            related.DoubleClick += OnRelatedChosen;
            clock.Tick += (_, _) => {
                tick++;
                canvas.Invalidate();
            };
        }

        /// <summary>Raised when the user asks to go to a record this preview named.</summary>
        /// <remarks>
        ///     The panel deliberately does not act on it, for the reason
        ///     <c>DefinitionListPanel.CellActivated</c> does not: what following a reference means is
        ///     the form's decision, and a panel that decided it would have to know about every tab.
        /// </remarks>
        public event EventHandler<ConfigNavigationEventArgs>? Navigate;

        /// <summary>Raised when the user picks a floor swatch, so the record list can select it.</summary>
        public event EventHandler<int>? FloorPicked;

        /// <summary>Points the preview at a cache, or clears it.</summary>
        /// <param name="newCache">The open cache, or null to unbind.</param>
        public void Bind(RSCache? newCache) {
            if (ReferenceEquals(newCache, cache))
                return;

            cache = newCache;
            quests = null;
            ReleaseFrames();

            sprites?.Dispose();
            sprites = newCache == null ? null : new SpriteThumbnailRenderer(newCache, composited: false);

            floors.Bind(newCache);
            ShowRecord(null, null);
        }

        /// <summary>
        ///     Shows one record of one family, or clears the pane.
        /// </summary>
        /// <remarks>
        ///     Called on every selection change and again after every edit, because a picture cached
        ///     by id somewhere else would otherwise keep showing what the record used to say - and a
        ///     user has no way to tell that from an edit that did not take.
        /// </remarks>
        /// <param name="shown">The family being listed, or null.</param>
        /// <param name="record">The selected record, or null.</param>
        public void ShowRecord(ConfigFamily? shown, ConfigListing? record) {
            family = shown;
            listing = record;
            tick = 0;

            bool isFloor = shown != null &&
                (shown.GroupId == ConfigGroup.FloorUnderlay || shown.GroupId == ConfigGroup.FloorOverlay);
            bool isRenderAnimation = shown != null && shown.GroupId == ConfigGroup.RenderAnimation;
            bool isQuest = shown != null && shown.GroupId == ConfigGroup.Quest;

            floors.Visible = isFloor;
            canvas.Visible = !isFloor && !isRenderAnimation;

            if (isFloor)
                floors.ShowOnly(shown!.GroupId == ConfigGroup.FloorUnderlay
                    ? FloorKind.Underlay
                    : FloorKind.Overlay);

            related.Items.Clear();

            if (isRenderAnimation) {
                related.Dock = DockStyle.Fill;
                FillAnimations(record);
            }
            else if (isQuest) {
                //A strip rather than the whole pane, so the quest's icon is still drawn above it.
                //Sized from the font, never as a pixel count.
                related.Dock = DockStyle.Bottom;
                related.Height = related.Font.Height * 7;
                FillItemsNamingQuest(record);
            }

            related.Visible = isRenderAnimation || (isQuest && related.Items.Count > 0);

            clock.Enabled = shown != null && shown.GroupId == ConfigGroup.LightIntensity &&
                record != null;

            caption.Text = Caption(shown, record);
            canvas.Invalidate();
        }

        /// <inheritdoc/>
        protected override void Dispose(bool disposing) {
            if (disposing) {
                clock.Stop();
                clock.Dispose();
                ReleaseFrames();
                sprites?.Dispose();
                sprites = null;
            }

            base.Dispose(disposing);
        }

        /// <summary>What the pane is showing, and what it deliberately is not.</summary>
        /// <param name="shown">The family, or null.</param>
        /// <param name="record">The record, or null.</param>
        /// <returns>The caption line.</returns>
        private static string Caption(ConfigFamily? shown, ConfigListing? record) {
            if (shown == null)
                return "";

            switch (shown.GroupId) {
                case ConfigGroup.FloorUnderlay:
                case ConfigGroup.FloorOverlay:
                    return "Every floor in this table, in the colour the renderer uses. Click one to select it.";
                case ConfigGroup.Cursor:
                    return "The pointer image, with the hotspot marked. The client draws no hotspot cross.";
                case ConfigGroup.MapSceneIcon:
                    return "The minimap stamp, untinted - the client applies opcode 2's tint at draw time.";
                case ConfigGroup.DamageMark:
                    return "The splat composed left to right as IntegerNode.java:596-624 lays it out." +
                           " No drift, no fade, and the number is in the editor's font, not the cache's.";
                case ConfigGroup.MapElement:
                    return "The marker and its polygon in tile coordinates. No map is drawn under it.";
                case ConfigGroup.Quest:
                    return "The chat icon this quest draws beside a name, and the items that" +
                           " require it. Double click an item to open it.";
                case ConfigGroup.RenderAnimation:
                    return "Every index-20 animation this set names. Double click one to open it.";
                case ConfigGroup.LightIntensity:
                    return "The intensity the client's own formula produces, one point per tick.";
                default:
                    return record == null
                        ? "Nothing in this family has a picture."
                        : "Nothing in this family has a picture. Its fields are below.";
            }
        }

        /// <summary>Selects the picked floor in the record list.</summary>
        /// <param name="sender">The palette.</param>
        /// <param name="pick">Which floor was picked.</param>
        private void OnFloorPicked(object? sender, FloorPick pick) {
            FloorPicked?.Invoke(this, pick.Id);
        }

        /// <summary>Opens whatever record the chosen line names, in the tab that edits its index.</summary>
        /// <param name="sender">The list.</param>
        /// <param name="e">The event data.</param>
        private void OnRelatedChosen(object? sender, EventArgs e) {
            if (related.SelectedItem is not RelatedRecord chosen)
                return;

            Navigate?.Invoke(this, new ConfigNavigationEventArgs(
                chosen.IndexId, chosen.RecordId, chosen.GroupId));
        }

        /// <summary>
        ///     Lists the items whose opcode 132 names this quest.
        /// </summary>
        /// <remarks>
        ///     <b>The join runs one way on disk</b>, so this is the forward relation inverted -
        ///     <see cref="QuestItemIndex"/> does that by decoding index 19, and it is built once per
        ///     cache and only when a quest is first shown. A quest nothing names gets no list at all
        ///     rather than an empty box, because an empty list reads as a lookup that failed.
        /// </remarks>
        /// <param name="record">The selected record, or null.</param>
        private void FillItemsNamingQuest(ConfigListing? record) {
            if (record == null || cache == null)
                return;

            try {
                quests ??= QuestItemIndex.Build(cache);
            }
            catch (Exception ex) {
                Debug("Could not invert the quest join: " + ex.Message);
                return;
            }

            foreach (int item in quests.ItemsNaming(record.FileId))
                related.Items.Add(new RelatedRecord(
                    "item " + item.ToString(CultureInfo.InvariantCulture) + " requires this quest",
                    RSConstants.ITEM_DEFINITIONS_INDEX, item));
        }

        /// <summary>
        ///     Lists the animations a render animation set names, labelled by what plays them.
        /// </summary>
        /// <remarks>
        ///     Through <see cref="NpcAnimationSet"/>, which is the route the entity page already
        ///     resolves an NPC's set by - so the labels here and there cannot disagree, and every one
        ///     of them is settled by what the client does with the field rather than by its opcode
        ///     number.
        /// </remarks>
        /// <param name="record">The selected record, or null.</param>
        private void FillAnimations(ConfigListing? record) {
            if (record?.Record.Definition is not RenderAnimationDefinition set)
                return;

            foreach (NpcAnimation animation in NpcAnimationSet.For(set))
                related.Items.Add(new RelatedRecord(animation.ToString(),
                    RSConstants.ANIMATIONS_INDEX, animation.AnimationId));
        }

        /// <summary>One record this preview points at, and where it lives.</summary>
        /// <remarks>
        ///     A label plus an address rather than the decoded record, because the two families that
        ///     produce these produce different types - an <see cref="NpcAnimation"/> and an item id -
        ///     and the list only ever has to show one and navigate to it.
        /// </remarks>
        private sealed class RelatedRecord {
            internal RelatedRecord(string label, int indexId, int recordId, int groupId = -1) {
                Label = label;
                IndexId = indexId;
                RecordId = recordId;
                GroupId = groupId;
            }

            internal string Label { get; }

            internal int IndexId { get; }

            internal int RecordId { get; }

            internal int GroupId { get; }

            /// <summary>The line the list shows.</summary>
            /// <returns>The label.</returns>
            public override string ToString() {
                return Label;
            }
        }

        /// <summary>
        ///     One sprite group's frame 0, decoded once and kept.
        /// </summary>
        /// <remarks>
        ///     Decoded on the UI thread, deliberately: a preview shows one record at a time and names
        ///     at most four sprite groups, where the grid behind it can be asked for thousands and
        ///     goes through the asynchronous thumbnail cache for exactly that reason.
        ///     <para>
        ///     A null is cached as well as a bitmap. An id with no picture is a permanent answer, and
        ///     re-asking on every repaint would decode a failing group thirty times a second while
        ///     the light curve's timer runs.
        ///     </para>
        /// </remarks>
        /// <param name="spriteGroupId">The index-8 group, or a negative for none.</param>
        /// <returns>The frame, or null.</returns>
        private Bitmap? Frame(int spriteGroupId) {
            if (spriteGroupId < 0 || sprites == null)
                return null;

            if (frames.TryGetValue(spriteGroupId, out Bitmap? known))
                return known;

            Bitmap? decoded = null;
            try {
                decoded = sprites.Render(RSConstants.SPRITES_INDEX, spriteGroupId, SpriteSide);
            }
            catch (Exception ex) {
                Debug("Config preview could not draw sprite " + spriteGroupId + ": " + ex.Message,
                    LOG_DETAIL.ADVANCED);
            }

            frames[spriteGroupId] = decoded;
            return decoded;
        }

        private void ReleaseFrames() {
            foreach (Bitmap? frame in frames.Values)
                frame?.Dispose();

            frames.Clear();
        }

        /// <summary>
        ///     The surface every family bar the floors and the render animations is drawn on.
        /// </summary>
        /// <remarks>
        ///     A nested control rather than painting the panel itself, so the caption above it stays
        ///     an ordinary docked label and the two list-shaped families can take the same space by
        ///     being made visible over it.
        /// </remarks>
        private sealed class PreviewCanvas : Control {
            /* A field rather than a property, and a constructor argument rather than a setter.
               Analyzer WFO1000 fails the build for a public or internal property on a Control
               subclass that does not declare its designer serialisation, and this one is not a
               design-time property at all - it is the back reference the paint needs. */
            private readonly ConfigPreviewPanel owner;

            internal PreviewCanvas(ConfigPreviewPanel owner) {
                this.owner = owner;
                DoubleBuffered = true;
                BackColor = Color.FromArgb(0x20, 0x20, 0x24);
            }

            /// <inheritdoc/>
            protected override void OnPaint(PaintEventArgs e) {
                base.OnPaint(e);
                owner.Draw(e.Graphics, ClientSize);
            }
        }

        /// <summary>Draws the selected record.</summary>
        /// <param name="g">The surface.</param>
        /// <param name="size">The area available.</param>
        private void Draw(Graphics g, Size size) {
            if (family == null || listing?.Record.Definition is not object definition) {
                DrawNote(g, "No record selected");
                return;
            }

            try {
                switch (definition) {
                    case CursorDefinition cursor: DrawCursor(g, cursor); return;
                    case MapSceneIconDefinition icon: DrawMapSceneIcon(g, icon); return;
                    case DamageMarkDefinition mark: DrawDamageMark(g, mark); return;
                    case MapElementDefinition element: DrawMapElement(g, element, size); return;
                    case QuestDefinition quest: DrawQuest(g, quest); return;
                    case LightIntensityDefinition light: DrawLightCurve(g, light, size); return;
                    default:
                        DrawNote(g, family.IsModelled
                            ? "This family stores no picture. Its fields are in the pane below."
                            : "No codec has been written for this group.");
                        return;
                }
            }
            catch (Exception ex) {
                //Reported on the surface rather than thrown: this runs from a paint handler, and an
                //exception out of one takes the form down.
                DrawNote(g, "Preview failed: " + ex.Message);
                Debug("Config preview failed: " + ex, LOG_DETAIL.ADVANCED);
            }
        }

        /// <summary>
        ///     A cursor: the pointer image with its hotspot marked.
        /// </summary>
        /// <remarks>
        ///     The cross is the editor's, not the client's. <c>RSFont.java:82-95</c> hands the sprite
        ///     and <c>new Point(anInt1738, anInt1736)</c> to the platform, which draws the pointer at
        ///     that offset and nothing else - so the mark is a statement about the two bytes rather
        ///     than a rendering of what the player sees, which is why the caption says so.
        /// </remarks>
        /// <param name="g">The surface.</param>
        /// <param name="cursor">The record.</param>
        private void DrawCursor(Graphics g, CursorDefinition cursor) {
            Bitmap? frame = Frame(cursor.SpriteId);
            if (frame == null) {
                DrawNote(g, "Sprite " + cursor.SpriteId + " has no picture in this cache.");
                return;
            }

            //Drawn at native size. A cursor is authored at the size the platform shows it, and
            //scaling it would move the hotspot off the pixel it names.
            var origin = new Point(Inset, Inset);
            g.DrawImageUnscaled(frame, origin);

            using var box = new Pen(Color.FromArgb(0x50, 0xFF, 0xFF, 0xFF));
            g.DrawRectangle(box, origin.X, origin.Y, frame.Width, frame.Height);

            int hotX = origin.X + cursor.HotspotX;
            int hotY = origin.Y + cursor.HotspotY;

            using var mark = new Pen(EditorTheme.Accent(EditorSurface.Canvas), 1f);
            g.DrawLine(mark, hotX - 5, hotY, hotX + 5, hotY);
            g.DrawLine(mark, hotX, hotY - 5, hotX, hotY + 5);

            DrawLabel(g, origin.X + frame.Width + Inset, origin.Y,
                "sprite " + cursor.SpriteId,
                "hotspot " + cursor.HotspotX + ", " + cursor.HotspotY,
                frame.Width + " x " + frame.Height + " pixels");
        }

        /// <summary>A map scene icon: the stamp the minimap draws, or the two forms of "no icon".</summary>
        /// <param name="g">The surface.</param>
        /// <param name="icon">The record.</param>
        private void DrawMapSceneIcon(Graphics g, MapSceneIconDefinition icon) {
            if (icon.SpriteGroupId < 0) {
                DrawNote(g, icon.DescribeAbsentIconEncoding() +
                    ". The client gates the whole draw on this, at Class122.java:93.");
                return;
            }

            Bitmap? frame = Frame(icon.SpriteGroupId);
            if (frame == null) {
                DrawNote(g, "Sprite group " + icon.SpriteGroupId + " has no picture in this cache.");
                return;
            }

            g.DrawImageUnscaled(frame, Inset, Inset);
            DrawLabel(g, Inset + frame.Width + Inset, Inset,
                icon.DescribeAbsentIconEncoding(),
                icon.StretchToFootprint ? "stretched to the tile footprint" : "drawn at native size",
                icon.TintRgb == 0 ? "untinted" : "tinted 0x" + icon.TintRgb.ToString("X6"));
        }

        /// <summary>
        ///     A damage mark, composed as the client lays it out.
        /// </summary>
        /// <remarks>
        ///     One x cursor through opcode 3, opcode 4, opcode 5 repeated to the width of the number,
        ///     the number itself and then opcode 6 - which is <c>IntegerNode.java:596-624</c>, and is
        ///     the reason a hit splat cannot be read as three stacked layers. The repeat count is the
        ///     client's own <c>numberWidth / spriteWidth + 1</c> (:588-590).
        /// </remarks>
        /// <param name="g">The surface.</param>
        /// <param name="mark">The record.</param>
        private void DrawDamageMark(Graphics g, DamageMarkDefinition mark) {
            //A number the strip has to be sized around. Any number would do; a two-digit one is
            //what the great majority of hits are and keeps the strip's repeat visible.
            string number = (mark.NumberTemplate ?? "").Replace("%1", "42");
            if (number.Length == 0)
                number = "42";

            using var font = new Font("Segoe UI", 12F, FontStyle.Bold);
            int numberWidth = (int) Math.Ceiling(g.MeasureString(number, font).Width);

            int x = Inset;
            int y = Inset;
            int tallest = 0;

            x += DrawPiece(g, Frame(mark.SpriteLayer1Id), x, y, ref tallest);
            x += DrawPiece(g, Frame(mark.SpriteLayer2Id), x, y, ref tallest);

            Bitmap? strip = Frame(mark.PreloadedSpriteId);
            if (strip != null && strip.Width > 0) {
                int repeats = numberWidth / strip.Width + 1;
                for (int i = 0; i < repeats; i++)
                    x += DrawPiece(g, strip, x, y, ref tallest);
            }

            using var ink = new SolidBrush(Color.FromArgb(0xFF,
                (mark.TextRgb >> 16) & 0xFF, (mark.TextRgb >> 8) & 0xFF, mark.TextRgb & 0xFF));
            g.DrawString(number, font, ink, x, y);
            x += numberWidth;
            tallest = Math.Max(tallest, font.Height);

            x += DrawPiece(g, Frame(mark.SpriteLayer3Id), x, y, ref tallest);

            DrawLabel(g, Inset, y + Math.Max(tallest, 1) + Inset,
                "sprites " + mark.SpriteLayer1Id + ", " + mark.SpriteLayer2Id + ", " +
                    mark.PreloadedSpriteId + ", " + mark.SpriteLayer3Id + " left to right",
                "font " + mark.FontId + ", template \"" + mark.NumberTemplate + "\"",
                mark.LifetimeMillis + " ms, fading from " + mark.FadeStartMillis +
                    ", drifting " + mark.DriftX + ", " + mark.DriftY);
        }

        /// <summary>Draws one piece of a composed splat and reports how far it advanced the cursor.</summary>
        /// <param name="g">The surface.</param>
        /// <param name="piece">The sprite, or null when the record names none.</param>
        /// <param name="x">Where the piece starts.</param>
        /// <param name="y">The baseline.</param>
        /// <param name="tallest">The tallest piece so far, updated.</param>
        /// <returns>The width consumed.</returns>
        private static int DrawPiece(Graphics g, Bitmap? piece, int x, int y, ref int tallest) {
            if (piece == null)
                return 0;

            g.DrawImageUnscaled(piece, x, y);
            tallest = Math.Max(tallest, piece.Height);
            return piece.Width;
        }

        /// <summary>
        ///     A world map element: its marker sprite, its label, and its polygon.
        /// </summary>
        /// <remarks>
        ///     The polygon is drawn in the tile coordinates the record stores, fitted to the pane,
        ///     with no map under it. <c>Class278.method3314</c> (:787-843) offsets each pair by the
        ///     placement's world position before filling and outlining it, and the placement is not
        ///     part of this record - so an absolute position here would be invented.
        ///     <para>
        ///     Every edge is drawn in <c>anIntArray234[aByteArray229[i]]</c>. Measured over the 69
        ///     records carrying opcode 15: one edge colour each and every one of the 344 indices is
        ///     0, so the table is exercised only in its degenerate form - which the drawing follows
        ///     rather than assumes.
        ///     </para>
        /// </remarks>
        /// <param name="g">The surface.</param>
        /// <param name="element">The record.</param>
        /// <param name="size">The area available.</param>
        private void DrawMapElement(Graphics g, MapElementDefinition element, Size size) {
            int x = Inset;

            Bitmap? marker = Frame(element.SpriteId);
            if (marker != null) {
                g.DrawImageUnscaled(marker, x, Inset);
                x += marker.Width + Inset;
            }

            Bitmap? hovered = Frame(element.HighlightedSpriteId);
            if (hovered != null) {
                g.DrawImageUnscaled(hovered, x, Inset);
                x += hovered.Width + Inset;
            }

            if (!string.IsNullOrEmpty(element.Label)) {
                using var font = new Font("Segoe UI", 10F);
                using var ink = new SolidBrush(Color.FromArgb(0xFF,
                    (element.LabelRgb >> 16) & 0xFF, (element.LabelRgb >> 8) & 0xFF, element.LabelRgb & 0xFF));
                g.DrawString(element.Label, font, ink, x, Inset);
            }

            int[]? vertices = element.PolygonVertices;
            if (vertices == null || vertices.Length < 6) {
                DrawLabel(g, Inset, Inset + SpriteSide,
                    "sprite " + element.SpriteId + ", hovered " + element.HighlightedSpriteId,
                    "label colour 0x" + element.LabelRgb.ToString("X6") + ", font " + element.FontId,
                    "this record stores no polygon");
                return;
            }

            DrawPolygon(g, element, vertices, size);
        }

        /// <summary>Draws the polygon opcode 15 stores, fitted to whatever room the pane has.</summary>
        /// <param name="g">The surface.</param>
        /// <param name="element">The record.</param>
        /// <param name="vertices">The x,y pairs, in tile coordinates.</param>
        /// <param name="size">The area available.</param>
        private void DrawPolygon(Graphics g, MapElementDefinition element, int[] vertices, Size size) {
            int count = vertices.Length / 2;
            int minX = int.MaxValue, maxX = int.MinValue, minY = int.MaxValue, maxY = int.MinValue;

            for (int i = 0; i < count; i++) {
                minX = Math.Min(minX, vertices[i * 2]);
                maxX = Math.Max(maxX, vertices[i * 2]);
                minY = Math.Min(minY, vertices[i * 2 + 1]);
                maxY = Math.Max(maxY, vertices[i * 2 + 1]);
            }

            int top = Inset + SpriteSide + Inset;
            int room = Math.Min(size.Width - Inset * 2, size.Height - top - Inset);
            if (room < 16)
                return;

            //Fitted rather than drawn at one tile per pixel. Measured coordinates run -128..384, so
            //a fixed scale either overflows the pane or renders a four-vertex polygon as a dot.
            float span = Math.Max(1, Math.Max(maxX - minX, maxY - minY));
            float scale = room / span;

            var points = new PointF[count];
            for (int i = 0; i < count; i++)
                points[i] = new PointF(
                    Inset + (vertices[i * 2] - minX) * scale,
                    top + (vertices[i * 2 + 1] - minY) * scale);

            using var fill = new SolidBrush(Argb(element.PolygonFillArgb));
            g.FillPolygon(fill, points);

            int[] edges = element.PolygonEdgeArgb ?? Array.Empty<int>();
            sbyte[] indices = element.PolygonEdgeColourIndices ?? Array.Empty<sbyte>();

            for (int i = 0; i < count; i++) {
                //Wrapping the last vertex to the first, which is what makes it a closed polygon
                //(Class278.java:787-843).
                PointF from = points[i];
                PointF to = points[(i + 1) % count];

                int slot = i < indices.Length ? indices[i] : 0;
                int colour = slot >= 0 && slot < edges.Length ? edges[slot] : element.OutlineArgb;

                using var pen = new Pen(Argb(colour));
                g.DrawLine(pen, from, to);
            }

            DrawLabel(g, Inset, Inset + SpriteSide - EditorTheme.NoticeFont.Height,
                count + " vertices, " + (maxX - minX) + " x " + (maxY - minY) + " tiles",
                edges.Length + " edge colour" + (edges.Length == 1 ? "" : "s"),
                "fill 0x" + element.PolygonFillArgb.ToString("X8"));
        }

        /// <summary>A quest: the chat icon it draws beside a name.</summary>
        /// <remarks>
        ///     <c>Class64_Sub25.method653</c> (:9-38) is the only field of a quest this client reads
        ///     back - it turns opcode 17 into an inline <c>&lt;img=n&gt;</c> tag in a chat string.
        ///     Everything else in the record is decoded and never used, which is why nothing else
        ///     here is drawn.
        /// </remarks>
        /// <param name="g">The surface.</param>
        /// <param name="quest">The record.</param>
        private void DrawQuest(Graphics g, QuestDefinition quest) {
            int x = Inset;

            Bitmap? icon = Frame(quest.IconSpriteId);
            if (icon != null) {
                g.DrawImageUnscaled(icon, x, Inset);
                x += icon.Width + Inset;
            }

            DrawLabel(g, x, Inset,
                string.IsNullOrEmpty(quest.Name) ? "unnamed quest" : "\"" + quest.Name + "\"",
                quest.IconSpriteId < 0
                    ? "no chat icon"
                    : "chat icon sprite " + quest.IconSpriteId +
                      (icon == null ? " - no picture in this cache" : ""),
                quest.Conditions3.Count + " opcode 3 entries, " + quest.Conditions4.Count + " opcode 4");
        }

        /// <summary>
        ///     A light intensity curve, evaluated by the client's own formula.
        /// </summary>
        /// <remarks>
        ///     <c>Class1.method161</c> (:183-253): the phase is
        ///     <c>0x7ff &amp; (base + tick * rate / 50)</c>, the waveform selects one of five shapes,
        ///     and the intensity is <c>(offset + (wave * amplitude &gt;&gt; 11)) / 2048</c>. Four
        ///     integers describe that and none of them looks like a waveform; the animation is the
        ///     point.
        ///     <para>
        ///     <b>Waveform 3 is not reproduced</b>, and the pane says so rather than drawing a
        ///     plausible curve. It indexes a 2,048-entry noise table the client generates at startup
        ///     (Class358.java:17 through <c>Class64_Sub15.method610</c>), from a seeded fractal
        ///     generator; a substitute would be an invented shape presented as the client's. One of
        ///     the four records in both caches uses it.
        ///     </para>
        /// </remarks>
        /// <param name="g">The surface.</param>
        /// <param name="light">The record.</param>
        /// <param name="size">The area available.</param>
        private void DrawLightCurve(Graphics g, LightIntensityDefinition light, Size size) {
            int top = Inset + EditorTheme.NoticeFont.Height + Inset;
            int height = size.Height - top - Inset;
            int width = size.Width - Inset * 2;

            if (width < 32 || height < 32)
                return;

            if (light.Waveform == 3) {
                DrawNote(g, "Waveform 3 reads a 2,048-entry noise table the client generates at" +
                    " startup (Class358.java:17). It is not reproduced here, so no curve is drawn.");
                return;
            }

            //Two full phase cycles across the pane, so a rate that wraps is visibly periodic rather
            //than looking like a straight line.
            const int Samples = 512;
            var points = new PointF[Samples];

            for (int i = 0; i < Samples; i++) {
                int phase = 0x7FF & (i * 4096 / Samples);
                float intensity = Intensity(light, phase);

                points[i] = new PointF(
                    Inset + (float) i * width / (Samples - 1),
                    top + height - Math.Clamp(intensity, 0f, 2f) / 2f * height);
            }

            using var axis = new Pen(Color.FromArgb(0x40, 0xFF, 0xFF, 0xFF));
            g.DrawLine(axis, Inset, top + height, Inset + width, top + height);
            g.DrawLine(axis, Inset, top + height / 2, Inset + width, top + height / 2);

            using var curve = new Pen(EditorTheme.Accent(EditorSurface.Canvas), 1.5f);
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.DrawLines(curve, points);
            g.SmoothingMode = SmoothingMode.Default;

            //The moving dot is what makes the rate legible: two curves of the same shape at
            //different rates are the same picture until something runs along them.
            int live = 0x7FF & (tick * light.Rate / 50);
            float here = Intensity(light, live);
            float dotX = Inset + (float) live * width / 2047f;
            float dotY = top + height - Math.Clamp(here, 0f, 2f) / 2f * height;

            using var dot = new SolidBrush(EditorTheme.Accent(EditorSurface.Canvas));
            g.FillEllipse(dot, dotX - 3, dotY - 3, 6, 6);

            DrawLabel(g, Inset, Inset,
                "waveform " + light.Waveform + ", rate " + light.Rate + ", amplitude " +
                    light.Amplitude + ", offset " + light.Offset,
                "intensity now " + here.ToString("0.000", CultureInfo.InvariantCulture));
        }

        /// <summary>
        ///     The intensity the client writes into a light for one phase.
        /// </summary>
        /// <remarks>
        ///     The waveform arms are <c>Class1.java:206-249</c> in order: 1 a sine table lookup
        ///     biased by 1024, 2 the raw phase, 3 the noise table, 4 the phase squared off, 5 a
        ///     triangle, and anything else a flat 2048. The sine table is 16,384 entries of one cycle
        ///     (Class284_Sub2_Sub2.java:15-23), sampled every eighth entry, which is a full cycle
        ///     over the 2,048 phase steps.
        /// </remarks>
        /// <param name="light">The record.</param>
        /// <param name="phase">The phase, 0..2047.</param>
        /// <returns>The intensity, where 1.0 is the light's nominal brightness.</returns>
        internal static float Intensity(LightIntensityDefinition light, int phase) {
            int wave;

            switch (light.Waveform) {
                case 1:
                    wave = ((int) (16384.0 * Math.Sin((phase << 3) * 3.834951969714103E-4)) >> 4) + 1024;
                    break;
                case 2:
                    wave = phase;
                    break;
                case 4:
                    wave = phase >> 10 << 11;
                    break;
                case 5:
                    wave = (phase < 1024 ? phase : 2048 - phase) << 1;
                    break;
                default:
                    //Waveform 3 lands here too. Its table is not reproduced, and the caller refuses
                    //to draw rather than presenting this flat line as the client's shape.
                    wave = 2048;
                    break;
            }

            return (light.Offset + (wave * light.Amplitude >> 11)) / 2048.0f;
        }

        /// <summary>A colour the record stores as a signed 32-bit ARGB.</summary>
        /// <remarks>
        ///     Opaque where the stored alpha is zero. Every measured fill and outline is negative, so
        ///     the alpha byte is genuinely set on the records that carry one; a record leaving it at
        ///     zero would otherwise draw nothing at all and read as a missing polygon.
        /// </remarks>
        /// <param name="argb">The stored value.</param>
        /// <returns>The colour.</returns>
        private static Color Argb(int argb) {
            int alpha = (argb >> 24) & 0xFF;
            return Color.FromArgb(alpha == 0 ? 0xFF : alpha,
                (argb >> 16) & 0xFF, (argb >> 8) & 0xFF, argb & 0xFF);
        }

        /// <summary>Writes a short note where a picture would have gone.</summary>
        /// <param name="g">The surface.</param>
        /// <param name="text">What to say.</param>
        private static void DrawNote(Graphics g, string text) {
            using var ink = new SolidBrush(EditorTheme.InkMuted(EditorSurface.Canvas));
            g.DrawString(text, EditorTheme.NoticeFont, ink,
                new RectangleF(Inset, Inset, 640, 120));
        }

        /// <summary>Writes a stack of caption lines beside a picture.</summary>
        /// <param name="g">The surface.</param>
        /// <param name="x">Where the lines start.</param>
        /// <param name="y">The first line's top.</param>
        /// <param name="lines">The lines.</param>
        private static void DrawLabel(Graphics g, int x, int y, params string[] lines) {
            using var ink = new SolidBrush(EditorTheme.InkMuted(EditorSurface.Canvas));

            for (int i = 0; i < lines.Length; i++)
                g.DrawString(lines[i], EditorTheme.NoticeFont, ink,
                    x, y + i * EditorTheme.NoticeFont.Height);
        }
    }
}
