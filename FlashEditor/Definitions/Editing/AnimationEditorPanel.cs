using BrightIdeasSoftware;
using FlashEditor.Cache;
using FlashEditor.Definitions.Animation;
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;
using static FlashEditor.Utils.DebugUtil;
using FlashEditor.IO;

namespace FlashEditor.Definitions.Editing {
    /// <summary>
    ///     The Animation tab: indexes 0 and 1 together, from a list of animations down to the
    ///     per-bone numbers one keyframe carries.
    /// </summary>
    /// <remarks>
    ///     One tab for two indexes because neither can be read without the other. A frame stores its
    ///     deltas positionally against the skeleton it names, and the skeleton's transform type is
    ///     what decides whether a missing axis defaults to 0 or 128 and whether a stored value is a
    ///     14-bit angle (<c>Class7.java:61,72-95</c>). An index-0 viewer with no index 1 shows
    ///     unlabelled integers.
    ///     <para>
    ///     <b>Three levels, one of which <see cref="DefinitionListPanel"/> owns.</b> That panel is a
    ///     flat list and cannot express a master/detail view, but it exposes
    ///     <see cref="DefinitionListPanel.SelectedRowChanged"/> for exactly this - so the frame-set
    ///     list is the panel driven by a descriptor, and the two grids beside it are this control's.
    ///     Neither grid enumerates an index, so neither is a descriptor's job.
    ///     </para>
    ///     <para>
    ///     Read only throughout. <c>FrameDefinition</c> and <c>SkeletonDefinition</c> both re-encode
    ///     byte for byte, but a safe editor needs more than a codec here: a slot's position is its
    ///     bone, so inserting or removing one silently re-points every slot after it, and changing a
    ///     skeleton's bone count re-points every frame in every set that names it.
    ///     </para>
    /// </remarks>
    public sealed class AnimationEditorPanel : UserControl {
        /// <summary>
        ///     The descriptor the frame-set list is driven by.
        /// </summary>
        /// <remarks>
        ///     One instance, held rather than built per bind, because <c>DefinitionListPanel.Bind</c>
        ///     treats a different descriptor as a different thing to show and would reload the whole
        ///     index on every visit to the tab. It also carries the skeleton memo, which is worth
        ///     keeping for the life of the cache.
        /// </remarks>
        private static readonly IDefinitionListDescriptor FrameSets = new FrameSetListDescriptor();

        /* Consolas 9 on every child. The form puts Consolas 12 on the tab control and everything
           under it inherits, which is half again what these grids are laid out for - the same
           mismatch that clipped the tracks tab's own strips. */
        private static readonly Font GridFont = new Font("Consolas", 9F);

        private readonly DefinitionListPanel sets = new DefinitionListPanel();

        private readonly FastObjectListView frames = new FastObjectListView {
            Dock = DockStyle.Fill,
            Font = GridFont,
            FullRowSelect = true,
            GridLines = true,
            ShowGroups = false,
            UseFiltering = true,
            View = View.Details
        };

        private readonly FastObjectListView transforms = new FastObjectListView {
            Dock = DockStyle.Fill,
            Font = GridFont,
            FullRowSelect = true,
            GridLines = true,
            ShowGroups = false,
            UseFiltering = true,
            View = View.Details
        };

        //AutoSize rather than a stated height, so the line the skeleton summary needs is the line it
        //gets whatever font the form ends up scaling to.
        private readonly Label header = new Label {
            AutoSize = true,
            Dock = DockStyle.Top,
            Font = GridFont,
            Text = NoCacheText
        };

        /* Neither splitter states a minimum size. Setting one re-checks the current distance against
           it, and the control is still at its 150x100 default when a field initialiser runs, so a
           minimum wide enough to be useful throws before the panel has ever been shown. */
        private readonly SplitContainer listAndDetail = new SplitContainer {
            Dock = DockStyle.Fill,
            Orientation = Orientation.Vertical
        };

        private readonly SplitContainer framesAndTransforms = new SplitContainer {
            Dock = DockStyle.Fill,
            Orientation = Orientation.Horizontal
        };

        private const string NoCacheText = "No cache loaded";
        private const string NoSelectionText = "Select a frame set to see its frames";

        private RSCache? cache;
        private bool splittersPlaced;

        /// <summary>Creates the panel.</summary>
        public AnimationEditorPanel() {
            Dock = DockStyle.Fill;
            BuildLayout();

            sets.SelectedRowChanged += (_, _) => ShowFrameSet(sets.SelectedRow as FrameSetListing);
            frames.SelectedIndexChanged += (_, _) => ShowFrame(frames.SelectedObject as FrameListing);
        }

        /// <summary>
        ///     Points the panel at a cache, or clears it when given none.
        /// </summary>
        /// <remarks>
        ///     <c>Editor.LoadEditorTab</c> calls this on every visit to the tab, so a rebind of the
        ///     cache already on display has to be a no-op - the frame-set sweep reads every group in
        ///     index 0 and doing it again would also throw away the selection. Identity is the right
        ///     test because opening a cache builds a new <see cref="RSCache"/>.
        /// </remarks>
        /// <param name="newCache">The open cache, or null to unbind.</param>
        public void Bind(RSCache? newCache) {
            if (ReferenceEquals(newCache, cache))
                return;

            cache = newCache;
            frames.ClearObjects();
            transforms.ClearObjects();
            header.Text = newCache == null ? NoCacheText : NoSelectionText;

            //The descriptor is passed either way. DefinitionListPanel only requires one alongside a
            //non-null cache, and keeping it constant means the columns survive an unbind.
            sets.Bind(newCache, FrameSets);
        }

        /// <summary>Places the splitters once the layout pass has given the containers a real size.</summary>
        /// <param name="levent">The event data.</param>
        protected override void OnLayout(LayoutEventArgs levent) {
            base.OnLayout(levent);
            PlaceSplitters();
        }

        /// <summary>
        ///     Divides the panel proportionally, once, when it first has a size worth dividing.
        /// </summary>
        /// <remarks>
        ///     A <see cref="SplitContainer"/> defaults to a splitter distance of 50 <i>pixels</i>, not
        ///     half, so the distance has to be stated - and stating it in the designer would make it
        ///     one more literal the form multiplies by its font ratio. A fraction of the measured
        ///     width is the same division at any font or DPI.
        ///     <para>
        ///     Deferred to layout rather than the constructor because assigning a distance the control
        ///     is not yet wide enough for throws, and a field initialiser runs while the container is
        ///     still 150x100. Once only, so a user who drags a splitter keeps where they put it.
        ///     </para>
        /// </remarks>
        private void PlaceSplitters() {
            if (splittersPlaced || listAndDetail.Width < 200 || framesAndTransforms.Height < 120)
                return;

            //Set before the assignments, not after: changing a splitter distance lays the panel out
            //again, and this is called from that layout.
            splittersPlaced = true;

            try {
                listAndDetail.SplitterDistance = Math.Max(listAndDetail.Panel1MinSize, listAndDetail.Width * 2 / 5);
                framesAndTransforms.SplitterDistance =
                    Math.Max(framesAndTransforms.Panel1MinSize, framesAndTransforms.Height / 2);
            } catch (InvalidOperationException ex) {
                //Left for the next layout rather than clamped. A clamped distance sticks, and the
                //user would see a collapsed pane on a window that later has room for both.
                splittersPlaced = false;
                Debug("Animation tab splitters not placed yet: " + ex.Message, LOG_DETAIL.ADVANCED);
            }
        }

        private void BuildLayout() {
            BuildFrameColumns();
            BuildTransformColumns();

            framesAndTransforms.Panel1.Controls.Add(frames);
            framesAndTransforms.Panel2.Controls.Add(transforms);

            //Docking resolves from the end of the Controls collection backwards, so the header has to
            //be added after the filled splitter or the splitter claims the whole panel.
            listAndDetail.Panel1.Controls.Add(sets);
            listAndDetail.Panel2.Controls.Add(framesAndTransforms);
            listAndDetail.Panel2.Controls.Add(header);

            Controls.Add(listAndDetail);
        }

        private void BuildFrameColumns() {
            AddColumn(frames, "File", 70, row => Frame(row).FileId);
            AddColumn(frames, "Frame id", 100, row => Frame(row).Definition.Id);
            AddColumn(frames, "Skeleton", 90, row => Frame(row).Definition.SkeletonId);
            AddColumn(frames, "Slots", 70, row => Frame(row).Definition.TransformCount);
            AddColumn(frames, "Posed", 70, row => Frame(row).PosedCount);
            AddColumn(frames, "Values", 70, row => Frame(row).Definition.StoredValueCount);
            AddColumn(frames, "Model flags", 110, row => Frame(row).ModelBuildFlags);
            AddColumn(frames, "Bytes", 70, row => Frame(row).SizeBytes);
        }

        private void BuildTransformColumns() {
            AddColumn(transforms, "Slot", 60, row => Transform(row).Slot);
            AddColumn(transforms, "Type", 60, row => Transform(row).EffectiveTransformType);
            AddColumn(transforms, "Stored type", 100, row => Transform(row).StoredTransformType);
            AddColumn(transforms, "Bone flag", 90, row => Transform(row).BoneFlag);
            AddColumn(transforms, "Mask", 80, row => Transform(row).Mask);
            AddColumn(transforms, "Labels", 130, row => Transform(row).Labels);
            AddColumn(transforms, "Flag", 60, row => Transform(row).Flag);
            AddColumn(transforms, "Sub", 50, row => Transform(row).SubType);
            AddColumn(transforms, "X", 70, row => Transform(row).X);
            AddColumn(transforms, "Y", 70, row => Transform(row).Y);
            AddColumn(transforms, "Z", 70, row => Transform(row).Z);
            AddColumn(transforms, "Resolved", 130, row => Transform(row).Resolved);
            AddColumn(transforms, "Pivot", 60, row => Transform(row).Pivot);
        }

        /// <summary>
        ///     Adds one column, reading its value through a delegate rather than an aspect name.
        /// </summary>
        /// <remarks>
        ///     Same reasoning as <see cref="DefinitionColumn"/>: a name looked up by reflection blanks
        ///     the column when the property is renamed, where a delegate stops compiling.
        /// </remarks>
        /// <param name="list">The grid to add to.</param>
        /// <param name="heading">The column heading.</param>
        /// <param name="width">The column width, in the grid's own pinned font.</param>
        /// <param name="read">Reads the displayed value off a row.</param>
        private static void AddColumn(FastObjectListView list, string heading, int width, Func<object, object?> read) {
            //Delegated so the null-row guard has one implementation. Ten copies of this method
            //existed and not one of them had it, which is how closing a cache crashed the
            //interfaces list.
            DetailGrid.AddColumn(list, heading, width, read);
        }

        private static FrameListing Frame(object row) {
            return (FrameListing) row;
        }

        private static TransformListing Transform(object row) {
            return (TransformListing) row;
        }

        /// <summary>
        ///     Loads every frame of the selected set and resolves each one against its skeleton.
        /// </summary>
        /// <remarks>
        ///     Read as a group rather than file by file: <c>RSCache.ReadFile</c> releases the container
        ///     as soon as it has handed back one file, so a per-file walk re-inflates the group once
        ///     per frame - up to 2792 times for the largest set in this cache.
        ///     <para>
        ///     Synchronous, unlike the frame-set sweep behind it. One group is one container decode
        ///     and a few thousand four-byte headers; the sweep is 3526 of them, which is why that one
        ///     is on a worker and this one is not.
        ///     </para>
        /// </remarks>
        /// <param name="set">The selected frame set, or null.</param>
        private void ShowFrameSet(FrameSetListing? set) {
            frames.ClearObjects();
            transforms.ClearObjects();

            if (cache == null || set == null) {
                header.Text = cache == null ? NoCacheText : NoSelectionText;
                return;
            }

            header.Text = Describe(set);

            var rows = new List<FrameListing>(set.FrameCount);
            try {
                IReadOnlyDictionary<int, JagStream> files = cache.ReadGroup(RSConstants.FRAMES_INDEX, set.SetId);
                int[]? types = set.EffectiveTransformTypes;

                foreach (KeyValuePair<int, JagStream> file in files.OrderBy(entry => entry.Key)) {
                    try {
                        int sizeBytes = file.Value.Length;
                        var frame = new FrameDefinition { Id = (set.SetId << 16) | file.Key };
                        frame.Decode(file.Value);
                        rows.Add(new FrameListing(file.Key, frame, sizeBytes, Resolve(frame, types)));
                    } catch (Exception ex) {
                        //One frame costs its own row, not the set. A frame that will not decode is
                        //worth seeing the rest of the animation around.
                        Debug("Frame set " + set.SetId + " file " + file.Key + " failed to decode: " + ex.Message);
                    }
                }
            } catch (Exception ex) {
                header.Text = "Frame set " + set.SetId + " could not be read: " + ex.Message;
                Debug("Frame set " + set.SetId + " could not be read: " + ex);
            }

            frames.SetObjects(rows);
        }

        /// <summary>
        ///     Fills the transform grid with one row per declared slot of the selected frame.
        /// </summary>
        /// <remarks>
        ///     Every slot, including the ones the client skips. A skipped slot still occupies a flag
        ///     byte and still names its bone by position, so leaving it out would renumber everything
        ///     below it and misreport which bone each row moves.
        /// </remarks>
        /// <param name="frame">The selected frame, or null.</param>
        private void ShowFrame(FrameListing? frame) {
            transforms.ClearObjects();

            if (frame == null)
                return;

            SkeletonDefinition? skeleton = (sets.SelectedRow as FrameSetListing)?.Skeleton;

            //Keyed by slot because Resolve omits the skipped ones, so a pose's position in the list
            //is not its slot.
            Dictionary<int, FramePose> posesBySlot = frame.Resolved == null
                ? new Dictionary<int, FramePose>()
                : frame.Resolved.Poses.ToDictionary(pose => pose.Slot);

            var rows = new List<TransformListing>(frame.Definition.TransformCount);
            for (int slot = 0; slot < frame.Definition.TransformCount; slot++) {
                SkeletonBone? bone = skeleton != null && slot < skeleton.BoneCount ? skeleton.Bones[slot] : null;
                FramePose? pose = posesBySlot.TryGetValue(slot, out FramePose found) ? (FramePose?) found : null;
                rows.Add(new TransformListing(slot, bone, frame.Definition.Transforms[slot], pose));
            }

            transforms.SetObjects(rows);
        }

        /// <summary>The one-line summary above the detail grids: the set, and the skeleton behind it.</summary>
        /// <param name="set">The selected frame set.</param>
        /// <returns>The summary line.</returns>
        private static string Describe(FrameSetListing set) {
            string skeleton = set.Skeleton == null
                ? "skeleton " + set.SkeletonId + " is not in index 1"
                : "skeleton " + set.SkeletonId + ": " + set.Skeleton.BoneCount + " bones, " +
                  set.Skeleton.TotalLabelCount + " labels, types " + set.TransformTypes;

            return "Set " + set.SetId + " - " + set.FrameCount + " frames - " + skeleton;
        }

        /// <summary>
        ///     Resolves a frame against its skeleton, or reports that it cannot be.
        /// </summary>
        /// <remarks>
        ///     Null rather than an exception for the two cases that are data rather than defects: the
        ///     skeleton is missing, or it holds fewer bones than the frame declares slots. The client
        ///     hits the second by walking off the end of the array and turning the frame into an empty
        ///     one (<c>Class7.java:130-134</c>), which is a silent wrong answer - the grid says so
        ///     instead, by leaving the resolved columns empty.
        /// </remarks>
        /// <param name="frame">The decoded frame.</param>
        /// <param name="types">The skeleton's effective transform types, or null.</param>
        /// <returns>The resolved frame, or null.</returns>
        private static ResolvedFrame? Resolve(FrameDefinition frame, int[]? types) {
            if (types == null)
                return null;

            try {
                return frame.Resolve(types);
            } catch (ArgumentException ex) {
                Debug("Frame " + frame.Id + " will not resolve: " + ex.Message, LOG_DETAIL.ADVANCED);
                return null;
            }
        }

        /// <summary>One frame of the selected set, as a grid row.</summary>
        private sealed class FrameListing {
            internal FrameListing(int fileId, FrameDefinition definition, int sizeBytes, ResolvedFrame? resolved) {
                FileId = fileId;
                Definition = definition;
                SizeBytes = sizeBytes;
                Resolved = resolved;
            }

            /// <summary>The file id, which is the frame's ordinal within the animation.</summary>
            internal int FileId { get; }

            /// <summary>The decoded frame.</summary>
            internal FrameDefinition Definition { get; }

            /// <summary>The stored file length.</summary>
            internal int SizeBytes { get; }

            /// <summary>The frame read against its skeleton, or null when it could not be.</summary>
            internal ResolvedFrame? Resolved { get; }

            /// <summary>How many slots actually produce a pose, or null when nothing resolved.</summary>
            internal object? PosedCount => Resolved?.Poses.Count;

            /// <summary>The model-build bits the frame's transform types imply, in hex.</summary>
            /// <remarks>Hex because the three constants are 0x80, 0x100 and 0x400 and read as bits.</remarks>
            internal object? ModelBuildFlags =>
                Resolved == null ? null : "0x" + Resolved.ModelBuildFlags.ToString("X3");
        }

        /// <summary>
        ///     One slot of the selected frame beside the bone it addresses.
        /// </summary>
        /// <remarks>
        ///     The bone half comes from index 1 and the flag and value half from index 0, and the row
        ///     exists to put them next to each other - the type on the left is what decides how the
        ///     numbers on the right are read.
        /// </remarks>
        private sealed class TransformListing {
            private readonly SkeletonBone? bone;
            private readonly FrameTransform transform;
            private readonly FramePose? pose;

            internal TransformListing(int slot, SkeletonBone? bone, FrameTransform transform, FramePose? pose) {
                Slot = slot;
                this.bone = bone;
                this.transform = transform;
                this.pose = pose;
            }

            /// <summary>The slot, which is the index of the bone in the skeleton's table.</summary>
            internal int Slot { get; }

            /// <summary>The transform type the client acts on, or null when the skeleton has no such bone.</summary>
            internal object? EffectiveTransformType => bone?.EffectiveTransformType;

            /// <summary>
            ///     The transform type as stored, shown only where it differs from the effective one.
            /// </summary>
            /// <remarks>
            ///     Blank in every row of this cache, because the client's only remap is 6 to 2 and no
            ///     bone here stores a 6. It is a column rather than nothing so that a repack which
            ///     introduces one is visible instead of silently reading as a type 2.
            /// </remarks>
            internal object? StoredTransformType =>
                bone == null || bone.TransformType == bone.EffectiveTransformType ? null : (object) bone.TransformType;

            /// <summary>The bone's own flag byte, which gates a separate skeletal path in the client.</summary>
            internal object? BoneFlag => bone?.Flag;

            /// <summary>The bone's 16-bit mask, in hex.</summary>
            internal object? Mask => bone == null ? null : "0x" + bone.Mask.ToString("X4");

            /// <summary>The label groups the bone moves.</summary>
            internal string Labels => bone == null ? string.Empty : string.Join(", ", bone.Labels);

            /// <summary>The frame's flag byte for this slot, exactly as stored.</summary>
            internal int Flag => transform.Flag;

            /// <summary>The two-bit field at bits 3-4 of that flag byte.</summary>
            internal int SubType => transform.SubType;

            /// <summary>The stored x value, or blank when the flag byte does not announce one.</summary>
            internal object? X => transform.HasX ? (object) transform.X.Value : null;

            /// <summary>The stored y value, or blank when the flag byte does not announce one.</summary>
            internal object? Y => transform.HasY ? (object) transform.Y.Value : null;

            /// <summary>The stored z value, or blank when the flag byte does not announce one.</summary>
            internal object? Z => transform.HasZ ? (object) transform.Z.Value : null;

            /// <summary>
            ///     What the client would transform with, once the type has defaulted and rescaled it.
            /// </summary>
            /// <remarks>
            ///     Beside the stored values rather than instead of them, because they differ for four
            ///     of the eleven types and the difference is the whole reason index 0 needs index 1.
            /// </remarks>
            internal string Resolved {
                get {
                    if (transform.IsSkipped)
                        return "skipped";
                    return pose == null ? string.Empty : pose.Value.X + ", " + pose.Value.Y + ", " + pose.Value.Z;
                }
            }

            /// <summary>The slot holding the pivot this one turns about, or blank for none.</summary>
            internal object? Pivot => pose == null || pose.Value.PivotSlot < 0 ? null : (object) pose.Value.PivotSlot;
        }
    }
}
