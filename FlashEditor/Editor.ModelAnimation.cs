using FlashEditor.Cache;
using FlashEditor.Definitions;
using FlashEditor.Definitions.Animation;
using FlashEditor.Rendering;
using OpenTK.Mathematics;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Drawing;
using System.Globalization;
using System.Windows.Forms;
using System;
using static FlashEditor.Utils.DebugUtil;
using FlashEditor.IO;
using FlashEditor.Definitions.Models;

namespace FlashEditor {
    /// <summary>
    ///     The model viewport's animation, picking and particle wiring.
    /// </summary>
    /// <remarks>
    ///     Split out of <c>Editor.cs</c> because it is the only part of the form that owns a clock.
    ///     Everything else on the form reacts to an event; this advances two independent simulations
    ///     from wall-clock time and decides when a frame is worth drawing, and mixing that into a
    ///     2,800-line file made it impossible to see at a glance what invalidates the viewport.
    ///     <para>
    ///     <b>Nothing here can be checked by screenshot.</b> No BitBlt capture on this machine sees
    ///     the GL surface, so a defect in the pose upload, the overlay geometry or the billboard
    ///     orientation passes every automated check there is. That is why every number this file can
    ///     produce is put on screen next to the viewport rather than only into the picture: the
    ///     readouts, not the rectangle, are what says whether the animation is running.
    ///     </para>
    /// </remarks>
    public partial class Editor {
        /// <summary>Frames and skeletons for the viewport, or null when no cache is open.</summary>
        /// <remarks>
        ///     Held across model loads rather than rebuilt per selection: it caches whole index-0
        ///     groups, and index 0 is the largest index in the cache. Rebuilding it would re-decode
        ///     the frame set every time the selection moved.
        /// </remarks>
        private CacheAnimationDataSource? _animationSource;

        /// <summary>Index-27 emitters and effectors for the viewport, or null when no cache is open.</summary>
        private CacheParticleDataSource? _particleSource;

        /// <summary>The playhead and the posed meshes, or null before a cache is open.</summary>
        private SkeletalAnimator? _animator;

        /// <summary>The bounded particle simulation, or null before a cache is open.</summary>
        private ParticleSystem? _particles;

        /// <summary>Ray-picking geometry for whatever the viewport is showing.</summary>
        private PickMesh? _pickMesh;

        /// <summary>The wireframe, hover highlight and particle billboards.</summary>
        /// <remarks>
        ///     Created lazily in <see cref="Gl_Paint"/> rather than in the constructor: every method
        ///     on it touches GL, and the form is constructed long before the control is realised.
        /// </remarks>
        private ViewportOverlayRenderer? _viewportOverlay;

        /// <summary>The models currently uploaded, in the order the renderer was given them.</summary>
        private IReadOnlyList<ModelDefinition> _viewerModels = Array.Empty<ModelDefinition>();

        /// <summary>Existing emitter and effector attachments, one entry per loaded model.</summary>
        private IReadOnlyList<ModelAttachments> _viewerAttachments = Array.Empty<ModelAttachments>();

        /// <summary>The face under the cursor, or <see cref="FaceHit.None"/>.</summary>
        private FaceHit _hoverHit = FaceHit.None;

        /// <summary>Whether the GL vertex buffers still hold a pose older than <see cref="_animator"/>'s.</summary>
        /// <remarks>
        ///     A flag rather than an upload at the point the pose changed. The pose is advanced from a
        ///     timer tick, where no GL context is current; uploading there works only by accident of
        ///     which control last called <c>MakeCurrent</c>. Every GL call this feature makes happens
        ///     inside the paint handler.
        /// </remarks>
        private bool _viewerPoseDirty;

        /// <summary>Whether the wireframe buffers need rebuilding from the current pose.</summary>
        private bool _viewerWireframeDirty;

        /// <summary>Measures the gap between viewport ticks, which is what both simulations advance on.</summary>
        /// <remarks>
        ///     Separate from <c>_animStopwatch</c>, which measures time since the GL context was
        ///     created for the texture scroll uniform. This one is reset whenever the timer stops, so a
        ///     tab left for ten minutes does not resume by running ten minutes of cycles.
        /// </remarks>
        private readonly Stopwatch _viewportClock = new Stopwatch();

        /// <summary>The font the index labels are drawn in.</summary>
        /// <remarks>
        ///     Fixed pitch so a face index and a vertex index of the same digit count occupy the same
        ///     width, which is what keeps the four labels on a small face from jittering as the model
        ///     turns.
        /// </remarks>
        private readonly Font _indexLabelFont = new Font("Consolas", 8.25f);

        /// <summary>Set while the animation list is being repopulated, so the load is not re-entered.</summary>
        private bool _animationSelectorPopulating;

        /// <summary>
        ///     Points the viewport's animation and particle layers at a cache, or unbinds them.
        /// </summary>
        /// <remarks>
        ///     Called on the same terms as every <c>Panel.Bind(null)</c> in
        ///     <see cref="DisposeOldResources"/>: the sources read through the file store, so leaving
        ///     one bound across a reload would have the next frame decode out of a disposed store.
        /// </remarks>
        /// <param name="openCache">The open cache, or null to unbind.</param>
        private void BindViewerAnimation(RSCache? openCache) {
            StopViewportTimer();
            SetViewerModels(Array.Empty<ModelDefinition>());

            _animationSource = null;
            _particleSource = null;
            _animator = null;
            _particles = null;

            _animationSelectorPopulating = true;
            AnimationSelector.Items.Clear();
            AnimationSelector.Text = string.Empty;
            _animationSelectorPopulating = false;

            if (openCache == null) {
                UpdateViewerReadouts();
                return;
            }

            _animationSource = new CacheAnimationDataSource(openCache);
            _particleSource = new CacheParticleDataSource(openCache);
            _animator = new SkeletalAnimator(_animationSource);
            _particles = new ParticleSystem(_particleSource);
            _animator.Player.RepeatIndefinitely = AnimationLoopCheck.Checked;

            UpdateViewerReadouts();
        }

        /// <summary>
        ///     Fills the animation selector from index 20's reference table.
        /// </summary>
        /// <remarks>
        ///     Ids only, and no decode: the table already states every declared file, so listing the
        ///     ids costs one table walk where decoding 15,260 records to list them would cost 120 group
        ///     decodes before the tab could be used at all. The one that is picked is decoded then.
        ///     <para>
        ///     Deliberately <b>not</b> a <c>DefinitionListPanel</c>. That convention is for a tab that
        ///     edits an index, and index 20 already has one - the Animations tab. A second grid of the
        ///     same index inside the viewport would be a second editor of it, and the thing the viewer
        ///     needs is a selector, not a list. Nor is the list filtered by the selected model: which
        ///     animations can pose a model is only knowable by reading a frame per animation, which
        ///     means decoding index-0 groups, and the honest answer is cheaper the other way round -
        ///     load one and read <see cref="SkeletalAnimator.Status"/>, which says in words whether any
        ///     transform reached the model.
        ///     </para>
        /// </remarks>
        /// <param name="ids">Every animation id the table declares, ascending.</param>
        private void PopulateAnimationSelector(IReadOnlyList<int> ids) {
            _animationSelectorPopulating = true;

            try {
                AnimationSelector.BeginUpdate();
                AnimationSelector.Items.Clear();

                foreach (int id in ids)
                    AnimationSelector.Items.Add(id);

                AnimationSelector.EndUpdate();
            }
            finally {
                _animationSelectorPopulating = false;
            }

            //The count stays beside the caption rather than replacing it: a bare number leaves nothing
            //on screen saying what the box next to it selects.
            AnimationSelectorLabel.Text = "Animation (" + ids.Count + ")";

            //What picking one costs, which is invisible on screen. The record itself is one file, but
            //posing its first frame decodes a whole index-0 group - index 0 is the largest in the
            //cache and is laid out chunk-major, so a group cannot be part-decoded.
            _modelTooltip.SetToolTip(AnimationSelector,
                "Picking an animation decodes its whole index-0 frame set on the first posed frame.");
        }

        /// <summary>Every animation id index 20 declares, ascending.</summary>
        /// <remarks>
        ///     Table-driven through <see cref="RSCache.EnumerateFiles"/> for the reason every other
        ///     enumeration here is: a 0..255 walk over the groups catches a not-found for each hole,
        ///     and the holes are real.
        /// </remarks>
        /// <param name="openCache">The open cache.</param>
        /// <returns>The ids.</returns>
        private static List<int> EnumerateAnimationIds(RSCache openCache) {
            List<int> ids = new List<int>();

            if (!CacheAddressing.TryGetFor(RSConstants.ANIMATIONS_INDEX, out CacheAddressing addressing))
                return ids;

            foreach ((int group, int file) in openCache.EnumerateFiles(RSConstants.ANIMATIONS_INDEX))
                ids.Add(addressing.DefinitionId(group, file));

            ids.Sort();
            return ids;
        }

        /// <summary>
        ///     Hands the viewport a new set of models and rebuilds everything derived from them.
        /// </summary>
        /// <remarks>
        ///     Called after every <c>ModelRenderer.Load</c> and <c>LoadMultiple</c>, and the order
        ///     matters: the picker, the animator and the particle system all index by the same model
        ///     position the renderer was given, so a set built from a different list would highlight
        ///     one face and pose another. Passing an empty set is how the viewport is cleared.
        /// </remarks>
        /// <param name="definitions">The models now uploaded, in upload order.</param>
        private void SetViewerModels(IReadOnlyList<ModelDefinition> definitions) {
            _viewerModels = definitions ?? Array.Empty<ModelDefinition>();
            _hoverHit = FaceHit.None;

            if (_viewerModels.Count == 0) {
                _pickMesh = null;
                _viewerAttachments = Array.Empty<ModelAttachments>();
            }
            else {
                _pickMesh = new PickMesh(_viewerModels);

                ModelAttachments[] attachments = new ModelAttachments[_viewerModels.Count];
                for (int i = 0; i < _viewerModels.Count; i++)
                    attachments[i] = new ModelAttachments(_viewerModels[i]);
                _viewerAttachments = attachments;
            }

            _animator?.SetModels(_viewerModels);
            _particles?.SetModels(_viewerModels);
            PrewarmParticleMaterials();

            //The pose the animator holds belongs to the models that have just been replaced, so the
            //buffers are stale even though the playhead has not moved.
            _viewerPoseDirty = _animator?.HasPose == true;
            _viewerWireframeDirty = true;

            UpdateViewerReadouts();
            SyncViewportTimer();
        }

        /// <summary>Loads the animation named by the selector and starts it.</summary>
        /// <remarks>
        ///     Decoding is on the UI thread on purpose. One animation record is a single file of a
        ///     120-group index, and the frame set behind it is fetched lazily on the first pose - so
        ///     the expensive part is already deferred, and a worker here would buy a race between the
        ///     selection and the playhead for nothing.
        /// </remarks>
        /// <param name="animationId">The index-20 id.</param>
        private void LoadViewerAnimation(int animationId) {
            if (cache == null || _animator == null)
                return;

            try {
                CacheAddressing addressing = CacheAddressing.For(RSConstants.ANIMATIONS_INDEX);
                JagStream payload = cache.ReadFile(RSConstants.ANIMATIONS_INDEX,
                    addressing.GroupOf(animationId), addressing.FileOf(animationId));

                AnimationDefinition record = new AnimationDefinition { Id = animationId };
                record.Decode(payload);

                _animator.Player.RepeatIndefinitely = AnimationLoopCheck.Checked;
                _animator.Play(record);
                _particles?.Reset();
                _particles?.ApplyPose(_animator.HasPose ? _animator.Poses : null);
            }
            catch (Exception failure) {
                //A missing or damaged record is an ordinary state of an editor pointed at an
                //incomplete cache, and the status line is where it belongs rather than a dialog.
                Debug("Animation " + animationId + " failed to load: " + failure.Message, LOG_DETAIL.BASIC);
                _animator.Play(null);
                ViewerStatusLabel.Text = "Animation " + animationId + " could not be read: " + failure.Message;
            }

            _viewerPoseDirty = true;
            _viewerWireframeDirty = true;
            _viewportClock.Restart();

            UpdateViewerReadouts();
            SyncViewportTimer();
            glControl.Invalidate();
        }

        /// <summary>Reads the id the selector currently names, whether picked or typed.</summary>
        /// <param name="animationId">The id, when this returns <c>true</c>.</param>
        /// <returns><c>false</c> when the box holds nothing usable as an id.</returns>
        private bool TryGetSelectedAnimationId(out int animationId) {
            if (AnimationSelector.SelectedItem is int selected) {
                animationId = selected;
                return true;
            }

            return int.TryParse(AnimationSelector.Text.Trim(), NumberStyles.Integer,
                CultureInfo.InvariantCulture, out animationId);
        }

        /// <summary>Picking an animation loads it immediately, so the viewport shows it without a second click.</summary>
        private void AnimationSelector_SelectionChanged(object? sender, EventArgs e) {
            if (_animationSelectorPopulating)
                return;

            if (TryGetSelectedAnimationId(out int animationId))
                LoadViewerAnimation(animationId);
        }

        /// <summary>Starts, resumes, or loads whatever id is in the box.</summary>
        private void AnimationPlayButton_Click(object? sender, EventArgs e) {
            if (_animator == null)
                return;

            if (!TryGetSelectedAnimationId(out int animationId)) {
                ViewerStatusLabel.Text = "Type or pick an animation id first.";
                return;
            }

            if (_animator.Player.Animation?.Id != animationId)
                LoadViewerAnimation(animationId);

            _animator.Player.Resume();
            _viewportClock.Restart();
            SyncViewportTimer();
            UpdateViewerReadouts();
        }

        /// <summary>Holds the playhead where it is, leaving the pose on screen.</summary>
        private void AnimationPauseButton_Click(object? sender, EventArgs e) {
            _animator?.Player.Pause();
            SyncViewportTimer();
            UpdateViewerReadouts();
            glControl.Invalidate();
        }

        /// <summary>Puts the playhead back to the first step and restarts the particle simulation with it.</summary>
        /// <remarks>
        ///     The particle reset is not decoration. Emitters keep spawning across a rewind, so without
        ///     it a rewound animation is compared against a particle cloud that has been running since
        ///     the model was loaded, and the two never line up twice.
        /// </remarks>
        private void AnimationRewindButton_Click(object? sender, EventArgs e) {
            if (_animator == null)
                return;

            _animator.Player.Rewind();
            _animator.RefreshPose();
            _particles?.Reset();
            _particles?.ApplyPose(_animator.HasPose ? _animator.Poses : null);

            _viewerPoseDirty = true;
            _viewerWireframeDirty = true;
            _viewportClock.Restart();

            UpdateViewerReadouts();
            SyncViewportTimer();
            glControl.Invalidate();
        }

        /// <summary>Switches between the record's own ending and an unconditional wrap.</summary>
        private void AnimationLoopCheck_CheckedChanged(object? sender, EventArgs e) {
            if (_animator != null)
                _animator.Player.RepeatIndefinitely = AnimationLoopCheck.Checked;

            SyncViewportTimer();
        }

        /// <summary>Any of the three overlay toggles.</summary>
        private void ViewerOverlayToggle_CheckedChanged(object? sender, EventArgs e) {
            _viewerWireframeDirty = true;
            SyncViewportTimer();
            glControl.Invalidate();
        }

        /// <summary>
        ///     Whether the viewport is the page on screen and therefore worth drawing.
        /// </summary>
        /// <remarks>
        ///     The timer used to run from the constructor to <c>OnFormClosed</c> and invalidate on
        ///     every tick regardless of which page was showing, so a session spent on any other page
        ///     still repainted a hidden GL surface thirty times a second for its whole length.
        /// </remarks>
        /// <returns><c>true</c> when the entity page is selected and realised.</returns>
        private bool ViewportIsVisible() {
            return glControl.IsHandleCreated
                && EditorTabControl.SelectedTab == EntityEditorTab;
        }

        /// <summary>
        ///     Whether anything on the viewport would move if a frame were drawn.
        /// </summary>
        /// <remarks>
        ///     The second half of the gate. A visible viewport showing a static model needs no ticks
        ///     at all - the camera handlers invalidate directly when they move it - so the timer is
        ///     for the two things that advance on their own: a playing animation, and a particle
        ///     system with either live particles or an emitter that could produce one.
        /// </remarks>
        /// <returns><c>true</c> when a redraw is worth scheduling.</returns>
        private bool ViewportNeedsFrames() {
            if (_animator != null && _animator.Player.IsPlaying)
                return true;

            return ViewerParticleCheck.Checked && _particles != null
                && (_particles.LiveParticleCount > 0 || _particles.EmitterCount > 0);
        }

        /// <summary>Starts or stops the render timer to match what the viewport currently needs.</summary>
        private void SyncViewportTimer() {
            if (ViewportIsVisible() && ViewportNeedsFrames()) {
                if (!_fpsTimer.Enabled) {
                    //Restarted rather than left running, so the first tick after a pause measures the
                    //gap since the resume and not since the pause.
                    _viewportClock.Restart();
                    _fpsTimer.Start();
                }

                return;
            }

            StopViewportTimer();
        }

        /// <summary>Stops the render timer and the clock the simulations advance on.</summary>
        private void StopViewportTimer() {
            _fpsTimer.Stop();
            _viewportClock.Reset();
        }

        /// <summary>
        ///     One render tick: advance both simulations by real elapsed time and redraw only if
        ///     either moved.
        /// </summary>
        /// <remarks>
        ///     <b>The render rate and the animation rate are different things.</b> The timer fires at
        ///     <see cref="AnimationPlayer.RenderFramesPerSecond"/>, and what it hands the player is
        ///     elapsed <i>seconds</i>, which the player converts into the 20 ms client cycles an
        ///     animation's stored durations are counted in. Advancing one step per tick instead would
        ///     play every animation at the redraw rate - faster on a fast machine, and nothing but a
        ///     stopwatch would say so.
        /// </remarks>
        private void ViewportTick() {
            if (!ViewportIsVisible() || !ViewportNeedsFrames()) {
                StopViewportTimer();

                //Read once more on the way out. An animation that has just run to its end stops the
                //clock on the tick after the one that finished it, so without this the readouts keep
                //the state they had a frame before the end for as long as the tab is open.
                UpdateViewerReadouts();
                return;
            }

            double seconds = _viewportClock.Elapsed.TotalSeconds;
            _viewportClock.Restart();

            bool poseMoved = _animator?.Advance(seconds) ?? false;

            if (poseMoved) {
                _viewerPoseDirty = true;
                _viewerWireframeDirty = true;
                _particles?.ApplyPose(_animator!.HasPose ? _animator.Poses : null);
                _pickMesh?.ApplyPose(_animator!.HasPose ? _animator.Poses : null);
            }

            bool particlesMoved = ViewerParticleCheck.Checked && (_particles?.Advance(seconds) ?? false);

            if (!poseMoved && !particlesMoved)
                return;

            UpdateViewerReadouts();
            glControl.Invalidate();
        }

        /// <summary>
        ///     Uploads whatever the last tick changed, then draws the three overlays.
        /// </summary>
        /// <remarks>
        ///     Every GL call this feature makes is reached from here, because this is the one place a
        ///     context is guaranteed current. Called from <see cref="Gl_Paint"/> after the model draw
        ///     and before the buffer swap.
        /// </remarks>
        /// <summary>
        ///     Rasterises the materials the newly attached emitters name, off the UI thread.
        /// </summary>
        /// <remarks>
        ///     A particle's material is a procedural graph in index 9, and evaluating one is the
        ///     kind of work that has its own fifteen-second budget. Doing it inside
        ///     <see cref="DrawViewportOverlays"/> would freeze the window for as long as it took, on
        ///     the frame a model was selected. So the evaluation happens here on a pool thread and
        ///     produces pixels only; the GL upload is a lookup on the paint path.
        ///     <para>
        ///     Nothing waits for it. Until a material is warm its quads draw against the flat white
        ///     texture, which is what every particle looked like before this existed, and the next
        ///     frame after the warm picks up the real one. Fire and forget is safe because the
        ///     rasteriser touches no GL and no control - the texture cache's warm store is
        ///     concurrent for exactly this.
        ///     </para>
        /// </remarks>
        private void PrewarmParticleMaterials() {
            GLTextureCache? textures = _textureCache;
            IReadOnlyList<int>? materials = _particles?.AttachedMaterialIds();

            if (textures == null || materials == null || materials.Count == 0)
                return;

            System.Threading.Tasks.Task.Run(() => {
                foreach (int material in materials) {
                    try {
                        textures.PrewarmParticleMaterial(material);
                    }
                    catch (Exception failure) {
                        //Swallowed deliberately: an unobserved exception on a pool thread takes the
                        //process down, and a material that will not rasterise is a white quad, not
                        //a reason to close the editor.
                        Utils.DebugUtil.Debug("Particle material " + material + " failed to warm: " + failure.Message,
                            Utils.DebugUtil.LOG_DETAIL.BASIC);
                    }
                }

                //The viewport only repaints when the simulation moves, so a warm that lands while
                //the effect is paused would otherwise not be seen until something else invalidated.
                if (!glControl.IsDisposed)
                    glControl.BeginInvoke(new Action(() => glControl.Invalidate()));
            });
        }

        private void DrawViewportOverlays() {
            _viewportOverlay ??= new ViewportOverlayRenderer();
            _viewportOverlay.MaterialTextureResolver ??= ResolveParticleMaterialTexture;

            if (_viewerPoseDirty && _animator != null) {
                if (_animator.HasPose)
                    _modelRenderer.ApplyPose(_animator.Poses);
                else
                    _modelRenderer.ResetPose();

                _viewerPoseDirty = false;
            }

            System.Numerics.Vector3 light = ToNumerics(Vector3.Normalize(CameraPosition() - _target));

            _viewportOverlay.ShowWireframe = ViewerWireframeCheck.Checked;
            _viewportOverlay.ShowParticles = ViewerParticleCheck.Checked;

            if (_viewerWireframeDirty) {
                _viewportOverlay.SetWireframe(ViewerWireframeCheck.Checked ? _pickMesh : null, light);
                _viewerWireframeDirty = false;
            }

            if (ViewerParticleCheck.Checked) {
                CameraBasis(out System.Numerics.Vector3 right, out System.Numerics.Vector3 up);
                _viewportOverlay.SetParticles(_particles, right, up, light);
            }

            if (ViewerHoverIndexCheck.Checked && _hoverHit.Found && _pickMesh != null
                && _pickMesh.TryFaceCorners(_hoverHit.ModelIndex, _hoverHit.FaceIndex,
                    out System.Numerics.Vector3 a, out System.Numerics.Vector3 b, out System.Numerics.Vector3 c)) {
                _viewportOverlay.SetHighlight(a, b, c, light);
            }
            else {
                _viewportOverlay.ClearHighlight();
            }

            _viewportOverlay.Draw();
        }

        /// <summary>Hands the overlay renderer a GL texture for one particle material.</summary>
        /// <remarks>
        ///     On the paint path, so it must not rasterise anything - see
        ///     <see cref="PrewarmParticleMaterials"/>. Zero means "not warm yet" and the caller
        ///     falls back to flat white for the frame.
        /// </remarks>
        /// <param name="materialId">The material a batch of quads names.</param>
        /// <returns>A GL texture handle, or 0.</returns>
        private int ResolveParticleMaterialTexture(int materialId) =>
            _textureCache?.GetParticleTexture(materialId) ?? 0;

        /// <summary>
        ///     Draws the face and vertex index labels with GDI, over the swapped GL surface.
        /// </summary>
        /// <remarks>
        ///     After the buffer swap, on the control's own <see cref="Graphics"/>. Text in the GL
        ///     pipeline would mean a glyph atlas and a second shader for four short strings.
        ///     <para>
        ///     Both index spaces are shown at once and labelled apart, because that is the whole point
        ///     of the overlay: a particle <b>emitter</b> attaches to a face and an <b>effector</b>
        ///     attaches to a vertex, the two numbers look identical, and crossing them puts an effect
        ///     on the wrong part of the model with nothing below to object.
        ///     </para>
        /// </remarks>
        /// <param name="graphics">The control's paint surface.</param>
        private void PaintIndexLabels(Graphics graphics) {
            if (!ViewerHoverIndexCheck.Checked || !_hoverHit.Found || _pickMesh == null)
                return;

            ModelAttachments? attachments = (uint)_hoverHit.ModelIndex < (uint)_viewerAttachments.Count
                ? _viewerAttachments[_hoverHit.ModelIndex]
                : null;

            IReadOnlyList<IndexLabel> labels = FaceLabelLayout.Build(_pickMesh, _hoverHit,
                ViewportMatrix(), glControl.Width, glControl.Height, attachments);

            IndexLabelPainter.Paint(graphics, labels, _indexLabelFont);
        }

        /// <summary>Re-picks the face under the cursor and redraws if it moved.</summary>
        /// <remarks>
        ///     Only while no mouse button is down: a drag is a camera move, and re-picking through one
        ///     would flicker the highlight across every face the cursor swept.
        /// </remarks>
        /// <param name="location">The cursor, in control pixels.</param>
        private void UpdateHoverPick(Point location) {
            if (_pickMesh == null || !ViewerHoverIndexCheck.Checked) {
                if (!_hoverHit.Found)
                    return;

                _hoverHit = FaceHit.None;
                glControl.Invalidate();
                return;
            }

            FaceHit previous = _hoverHit;

            if (ViewportMath.TryBuildRay(ViewportMatrix(), location.X, location.Y,
                    glControl.Width, glControl.Height, out PickRay ray)
                && _pickMesh.TryPick(ray, out FaceHit hit)) {
                _hoverHit = hit;
            }
            else {
                _hoverHit = FaceHit.None;
            }

            if (_hoverHit.Found != previous.Found
                || _hoverHit.ModelIndex != previous.ModelIndex
                || _hoverHit.FaceIndex != previous.FaceIndex) {
                UpdateViewerReadouts();
                glControl.Invalidate();
            }
        }

        /// <summary>
        ///     The composed matrix the viewport last drew with, in the form the picker wants.
        /// </summary>
        /// <remarks>
        ///     Composed in the same order the shader does - model, then view, then projection - and
        ///     handed over row by row. Both OpenTK and <c>System.Numerics</c> transform a row vector
        ///     from the left, so no transpose is involved; inserting one produces a picker that still
        ///     returns faces and returns the wrong ones.
        /// </remarks>
        /// <returns>The model-view-projection matrix.</returns>
        private System.Numerics.Matrix4x4 ViewportMatrix() {
            Matrix4 mvp = _model * _view * _proj;

            Span<float> rows = stackalloc float[16] {
                mvp.M11, mvp.M12, mvp.M13, mvp.M14,
                mvp.M21, mvp.M22, mvp.M23, mvp.M24,
                mvp.M31, mvp.M32, mvp.M33, mvp.M34,
                mvp.M41, mvp.M42, mvp.M43, mvp.M44
            };

            return ViewportMath.FromRowMajor(rows);
        }

        /// <summary>The camera's right and up axes, so a particle quad can be turned to face it.</summary>
        /// <param name="right">The right axis.</param>
        /// <param name="up">The up axis, re-derived rather than taken from the world up.</param>
        private void CameraBasis(out System.Numerics.Vector3 right, out System.Numerics.Vector3 up) {
            Vector3 forward = Vector3.Normalize(_target - CameraPosition());
            Vector3 sideways = Vector3.Cross(forward, _up);

            //A camera looking straight down leaves the cross product at zero, and normalising that
            //produces NaN positions for every billboard rather than a visible defect.
            sideways = sideways.LengthSquared > 1E-12f ? Vector3.Normalize(sideways) : Vector3.UnitX;

            right = ToNumerics(sideways);
            up = ToNumerics(Vector3.Normalize(Vector3.Cross(sideways, forward)));
        }

        /// <summary>Converts an OpenTK vector to the one the rendering layer is written against.</summary>
        /// <remarks>
        ///     The layer uses <c>System.Numerics</c> so its arithmetic can be tested without OpenTK,
        ///     and the form uses OpenTK because that is what the GL bindings take. This is the seam.
        /// </remarks>
        /// <param name="value">The OpenTK vector.</param>
        /// <returns>The same vector.</returns>
        private static System.Numerics.Vector3 ToNumerics(Vector3 value) {
            return new System.Numerics.Vector3(value.X, value.Y, value.Z);
        }

        /// <summary>
        ///     Rewrites the numbers beside the viewport.
        /// </summary>
        /// <remarks>
        ///     Invariant culture throughout: these are ids and counts, and a frame id that picked up a
        ///     thousands separator is neither searchable nor quotable in a bug report.
        ///     <para>
        ///     This is the only evidence anyone has that the viewport is animating. Nothing in the
        ///     suite covers the renderer, and no screen capture on this machine sees the GL surface, so
        ///     a frame counter that climbs while the picture holds still is the difference between a
        ///     dead simulation and a pose that reaches nothing.
        ///     </para>
        /// </remarks>
        private void UpdateViewerReadouts() {
            if (_animator == null) {
                ViewerReadoutLabel.Text = "No cache loaded.";
                ViewerStatusLabel.Text = string.Empty;
                return;
            }

            AnimationPlayer player = _animator.Player;

            string frame = player.FrameIndex < 0
                ? "frame -/-"
                : "frame " + player.FrameIndex.ToString(CultureInfo.InvariantCulture)
                    + "/" + player.FrameCount.ToString(CultureInfo.InvariantCulture);

            string frameId = player.PackedFrameId == -1
                ? "id -"
                : "id " + player.PackedFrameId.ToString(CultureInfo.InvariantCulture)
                    + " (g" + player.FrameGroup.ToString(CultureInfo.InvariantCulture)
                    + " f" + player.FrameFileId.ToString(CultureInfo.InvariantCulture) + ")";

            //Position inside the pass, not total run time. ElapsedSeconds keeps climbing across
            //loops by design, so a looping two second animation read "51.120 s of 2.000 s" - a
            //figure that is not wrong so much as answering a question nobody asked while looking
            //like a broken clock. The loop counter carries what it used to say.
            string elapsed = player.PositionSeconds.ToString("0.000", CultureInfo.InvariantCulture) + " s of "
                + player.TotalSeconds.ToString("0.000", CultureInfo.InvariantCulture) + " s"
                + (player.LoopsCompleted > 0
                    ? ", loop " + (player.LoopsCompleted + 1).ToString(CultureInfo.InvariantCulture)
                    : string.Empty);

            string particles = _particles == null
                ? "particles -"
                : "particles " + _particles.LiveParticleCount.ToString(CultureInfo.InvariantCulture)
                    + "/" + _particles.MaximumParticles.ToString(CultureInfo.InvariantCulture)
                    + ", emitters " + _particles.ActiveEmitterCount.ToString(CultureInfo.InvariantCulture)
                    + "/" + _particles.EmitterCount.ToString(CultureInfo.InvariantCulture);

            string hover = _hoverHit.Found
                ? "  hover face " + _hoverHit.FaceIndex.ToString(CultureInfo.InvariantCulture)
                    + " v" + _hoverHit.VertexA.ToString(CultureInfo.InvariantCulture)
                    + "/" + _hoverHit.VertexB.ToString(CultureInfo.InvariantCulture)
                    + "/" + _hoverHit.VertexC.ToString(CultureInfo.InvariantCulture)
                : string.Empty;

            ViewerReadoutLabel.Text = string.Join("  ", frame, frameId, elapsed, particles) + hover;

            //The animator's own sentence, not a paraphrase. It is the one thing that tells a frame
            //that posed nothing apart from a frame that holds still, which look identical on screen.
            ViewerStatusLabel.Text = _animator.Status;
        }
    }
}
