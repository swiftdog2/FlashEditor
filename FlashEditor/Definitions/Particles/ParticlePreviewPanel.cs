using FlashEditor.Cache;
using FlashEditor.Definitions.Models;
using FlashEditor.Rendering;
using FlashEditor.UI;
using OpenTK.GLControl;
using OpenTK.Graphics.OpenGL;
using OpenTK.Mathematics;
using OpenTK.Windowing.Common;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Windows.Forms;
using static FlashEditor.Utils.DebugUtil;

//System.Threading.Timer arrives through the implicit usings and collides with the WinForms one.
//Aliased rather than qualified at each use, the same way Region is elsewhere in this project.
using UiTimer = System.Windows.Forms.Timer;

namespace FlashEditor.Definitions.Particles {
    /// <summary>
    ///     Runs the selected index-27 emitter and draws its particles, beside the record grid.
    /// </summary>
    /// <remarks>
    ///     <b>This is the application's second OpenGL context, and that was worth proving before it
    ///     was worth using.</b> Realising a <c>GLControl</c> used to flip the whole process from
    ///     DPI-unaware to per-monitor part way through a session; Windows then refuses
    ///     <c>SetParent</c> between windows in different awareness contexts, so the tab control threw
    ///     out of its next selection change and no further tab opened for the rest of the session.
    ///     <see cref="FlashEditorForm"/>'s remarks carry the whole failure. It is fixed process-wide
    ///     by pinning the awareness before any window exists, and the fix was re-checked by walking
    ///     the tabs with this surface realised - in document order, and in an order that realises it
    ///     before the Entities viewport - because no capture on this machine can see a GL surface and
    ///     a screenshot would have proved nothing.
    ///     <para>
    ///     The context is its own rather than the Entities viewport's. Sharing one would let the two
    ///     share a shader program and the material textures, and would also mean this surface could
    ///     not draw until the Entities page had been opened at least once, since that is where the
    ///     program is compiled. An independent context costs one duplicate program and owes nothing
    ///     to what the user visited first.
    ///     </para>
    ///     <para>
    ///     <b>What this preview is not.</b> It is not the client's renderer, and the view says so
    ///     rather than leaving a user to read a difference as a defect:
    ///     <list type="bullet">
    ///     <item>No scene, so opcodes 12, 13 and 33 - which destroy a particle against terrain and
    ///     roof - do nothing, and an effect that relies on a floor to stop its particles overruns.
    ///     <see cref="ParticleSystem.SimulatesSceneBounds"/> is the same statement in the simulation.</item>
    ///     <item>A material texture arrives a moment after the emitter does, not with it. The
    ///     material is a procedural graph in index 9 and rasterising one is unbounded work, so it
    ///     happens on a pool thread and the first frames sample flat white. This panel holds its
    ///     own <c>GLTextureCache</c> because a GL handle belongs to the context that made it and
    ///     the Entities viewport's cannot be bound here - only the handles are duplicated, since
    ///     the decoded graphs sit in <c>TextureManager</c>'s shared store.</item>
    ///     <item>One emitter on one synthetic face, not an emitter in the place a model puts it. The
    ///     spawn area, and therefore the spread, is this panel's triangle rather than the model's.</item>
    ///     </list>
    ///     </para>
    /// </remarks>
    internal sealed class ParticlePreviewPanel : UserControl {
        /// <summary>Model units per side of the synthetic emitter face.</summary>
        /// <remarks>
        ///     Small on purpose. A particle spawns at a random point inside the triangle, so the face
        ///     is the effect's spread at birth - a large one would widen every effect by an amount
        ///     this panel invented. Sixteen units is an eighth of a world unit, which reads as a point
        ///     source at any distance the camera sits at.
        /// </remarks>
        private const int FaceSideModelUnits = 16;

        /// <summary>
        ///     The surface. Its own context, not the Entities viewport's.
        /// </summary>
        /// <remarks>
        ///     Built in code rather than by the designer, because the designer's <c>glControl</c> is
        ///     the one that must never be reparented and putting a second one in that file invites
        ///     exactly that edit.
        /// </remarks>
        private readonly GLControl surface = new GLControl {
            API = ContextAPI.OpenGL,
            APIVersion = new Version(3, 3, 0, 0),
            Dock = DockStyle.Fill,
            Flags = ContextFlags.Default,
            IsEventDriven = true,
            Name = "particlePreviewSurface",
            Profile = ContextProfile.Core
        };

        /// <summary>What the simulation is currently doing, refreshed every frame that moves.</summary>
        private readonly Label status = new Label {
            AutoSize = true,
            Dock = DockStyle.Bottom,
            Font = EditorTheme.UiFont,
            Text = NoSelectionText
        };

        /// <summary>
        ///     The standing statement of how this view differs from the client.
        /// </summary>
        /// <remarks>
        ///     Permanent rather than shown on demand. A user comparing the preview against the game
        ///     has no way to tell a documented omission from a decoder defect, and the moment to say
        ///     so is while they are looking at it.
        /// </remarks>
        private readonly Label notice = new Label {
            AutoSize = true,
            Dock = DockStyle.Bottom,
            Font = EditorTheme.NoticeFont,
            Text = "Preview, not the client's renderer: no scene to destroy particles against, "
                 + "no material texture, and one synthetic face rather than a model's."
        };

        /// <summary>Drives the simulation forward while the page is on screen.</summary>
        /// <remarks>
        ///     Gated on visibility in <see cref="Tick"/> rather than merely started and stopped, for
        ///     the same reason the Entities viewport's is: an emitter that spawns forever would
        ///     otherwise keep repainting a hidden surface for the whole session.
        /// </remarks>
        private readonly UiTimer clock = new UiTimer {
            Interval = 1000 / AnimationPlayer.RenderFramesPerSecond
        };

        /// <summary>Wall-clock time since the last advance, which is what the simulation consumes.</summary>
        /// <remarks>
        ///     Elapsed seconds, not one step per tick. A step per tick would run every effect at the
        ///     redraw rate, so the same emitter would look different on a faster machine and nothing
        ///     but a stopwatch would say so.
        /// </remarks>
        private readonly Stopwatch elapsed = new Stopwatch();

        /// <summary>Where the emitter and its effectors are read from.</summary>
        private readonly PreviewParticleSource source = new PreviewParticleSource();

        /// <summary>The simulation. Rebuilt only when the cap changes, which it never does here.</summary>
        private readonly ParticleSystem system;

        /// <summary>The one model the emitter is attached to: a single triangle at the origin.</summary>
        private readonly ModelDefinition face = BuildEmitterFace();

        /// <summary>Owns the billboard buffers and the draw. Created on the first paint.</summary>
        /// <remarks>
        ///     Its GL objects belong to this control's context, so it is created and disposed on the
        ///     thread that holds it and never handed to the Entities viewport.
        /// </remarks>
        private ViewportOverlayRenderer? billboards;

        /// <summary>
        ///     This surface's own texture cache, or null until a cache is bound.
        /// </summary>
        /// <remarks>
        ///     <b>Its own, because a GL texture handle belongs to the context that created it.</b>
        ///     The Entities viewport already warms particle materials, and this panel cannot bind
        ///     what that one uploaded. Only the handles are duplicated: the decoded index-26
        ///     metadata and index-9 graphs sit in TextureManager's shared static store, which
        ///     GLTextureCache now reaches through EnsureLoaded rather than reloading and disposing
        ///     out from under the other consumers.
        /// </remarks>
        private GLTextureCache? textures;

        /// <summary>The cache this panel is bound to, for warming materials off the paint path.</summary>
        private RSCache? bound;

        /// <summary>The shader program, or 0 before the context exists.</summary>
        private int program;

        /// <summary>Uniform locations, valid once <see cref="program"/> is non-zero.</summary>
        private int uModel;

        /// <summary>Uniform locations, valid once <see cref="program"/> is non-zero.</summary>
        private int uView;

        /// <summary>Uniform locations, valid once <see cref="program"/> is non-zero.</summary>
        private int uProj;

        /// <summary>Uniform locations, valid once <see cref="program"/> is non-zero.</summary>
        private int uLightDir;

        /// <summary>Whether the context has been made current and its state set at least once.</summary>
        private bool contextReady;

        /// <summary>Orbit angle around the vertical axis, in radians.</summary>
        private float yaw = 0.6f;

        /// <summary>Orbit angle above the horizon, in radians.</summary>
        private float pitch = 0.25f;

        /// <summary>Camera distance from <see cref="target"/>, in world units.</summary>
        private float distance = 4f;

        /// <summary>What the camera looks at, a little above the emitter so a rising effect is centred.</summary>
        private Vector3 target = new Vector3(0f, 0.6f, 0f);

        /// <summary>Where the last drag was, or null when no button is down.</summary>
        private Point? dragFrom;

        /// <summary>Shown while no emitter is selected.</summary>
        private const string NoSelectionText = "Select an emitter to preview it";

        /// <summary>Shown while an effector is selected.</summary>
        /// <remarks>
        ///     An effector has no particles of its own - it is a force an emitter names - so there is
        ///     nothing here to run. Said rather than left blank, because a blank surface beside a
        ///     selected row reads as a preview that failed.
        /// </remarks>
        private const string EffectorText = "An effector emits nothing on its own. It shapes the "
                                          + "particles of any emitter that names it.";

        /// <summary>Creates the panel.</summary>
        public ParticlePreviewPanel() {
            Dock = DockStyle.Fill;
            system = new ParticleSystem(source);

            //Docking resolves from the end of the Controls collection backwards, so the filled
            //surface is added first and the two strips after it, bottom-most last.
            Controls.Add(surface);
            Controls.Add(status);
            Controls.Add(notice);

            surface.Load += (_, _) => PrepareContext();
            surface.Paint += (_, _) => Render();
            surface.Resize += (_, _) => ResizeViewport();
            surface.MouseDown += (_, e) => dragFrom = e.Location;
            surface.MouseUp += (_, _) => dragFrom = null;
            surface.MouseMove += OnDrag;
            surface.MouseWheel += OnWheel;

            clock.Tick += (_, _) => Tick();
        }

        /// <summary>Points the preview at a cache, or clears it.</summary>
        /// <remarks>
        ///     Only the effector lookups need the cache: the emitter itself arrives already decoded
        ///     from the selected row, so an edit that has not been written yet would still preview.
        /// </remarks>
        /// <param name="cache">The open cache, or null to unbind.</param>
        public void Bind(RSCache? cache) {
            source.Effectors = cache == null ? null : new CacheParticleDataSource(cache);

            bound = cache;
            textures = cache == null ? null : new GLTextureCache(cache);

            Restart();
        }

        /// <summary>Runs one emitter, or nothing.</summary>
        /// <param name="emitter">The emitter to simulate, or null to show the empty state.</param>
        public void ShowEmitter(ParticleEmitterDefinition? emitter) {
            source.Emitter = emitter;
            face.Emitters = emitter == null
                ? Array.Empty<ModelParticleEmitter>()
                : new ModelParticleEmitter[1] { new ModelParticleEmitter(emitter.Id, 0) };

            Restart();
            PrewarmMaterials();

            if (emitter == null)
                status.Text = NoSelectionText;
        }

        /// <summary>
        ///     Rasterises the materials this emitter names, off the paint path.
        /// </summary>
        /// <remarks>
        ///     A material is a procedural graph in index 9 and evaluating one is unbounded work, so
        ///     doing it inside the paint would freeze the window on the frame an emitter was picked.
        ///     The same split the Entities viewport uses: pixels on a pool thread, the GL upload as
        ///     a lookup on the paint path.
        ///     <para>
        ///     Nothing waits for it. Until a material is warm its quads sample the flat white
        ///     texture and come out as plain squares, which is precisely what this whole panel
        ///     looked like before it had a texture cache at all.
        ///     </para>
        /// </remarks>
        private void PrewarmMaterials() {
            GLTextureCache? warm = textures;
            IReadOnlyList<int>? materials = system?.AttachedMaterialIds();

            if (warm == null || materials == null || materials.Count == 0)
                return;

            System.Threading.Tasks.Task.Run(() => {
                foreach (int material in materials) {
                    try {
                        warm.PrewarmParticleMaterial(material);
                    }
                    catch (Exception failure) {
                        //Swallowed on purpose: an unobserved exception on a pool thread takes the
                        //process down, and a material that will not rasterise is a white quad.
                        Utils.DebugUtil.Debug(
                            "Particle material " + material + " failed to warm: " + failure.Message,
                            Utils.DebugUtil.LOG_DETAIL.BASIC);
                    }
                }

                //The warm lands after the frame that asked for it, so nothing would show the result
                //until the simulation next moved the surface.
                if (!surface.IsDisposed && surface.IsHandleCreated)
                    surface.BeginInvoke(new Action(() => surface.Invalidate()));
            });
        }

        /// <summary>Shows the empty state with the reason an effector has nothing to run.</summary>
        public void ShowEffector() {
            ShowEmitter(null);
            status.Text = EffectorText;
        }

        /// <summary>Releases the GL objects this panel owns, on the thread that holds the context.</summary>
        /// <remarks>
        ///     A GL handle means nothing outside its context, so this cannot be left to a finaliser
        ///     and the context has to be made current first - the Entities viewport may well have
        ///     made its own current since the last paint here.
        /// </remarks>
        /// <param name="disposing">Whether managed state should be released.</param>
        protected override void Dispose(bool disposing) {
            if (disposing) {
                clock.Stop();

                if (contextReady && surface.IsHandleCreated && !surface.IsDisposed) {
                    surface.MakeCurrent();
                    billboards?.Dispose();

                    if (program != 0)
                        GL.DeleteProgram(program);
                }

                billboards = null;
                program = 0;
                clock.Dispose();
            }

            base.Dispose(disposing);
        }

        /// <summary>Starts and stops the clock as the page comes and goes.</summary>
        /// <param name="e">The event data.</param>
        protected override void OnVisibleChanged(EventArgs e) {
            base.OnVisibleChanged(e);
            SyncClock();
        }

        /// <summary>
        ///     Caps the two captions at the panel's width so they wrap rather than clip.
        /// </summary>
        /// <remarks>
        ///     An <c>AutoSize</c> label does not wrap; it grows sideways and a docked one is then cut
        ///     off at the panel edge, silently losing the end of the sentence. A
        ///     <see cref="Control.MaximumSize"/> with a zero height caps the width alone and lets the
        ///     height follow the text - which is the same "measure, do not state pixels" rule the rest
        ///     of the form follows, applied to a caption whose length is not known here.
        /// </remarks>
        /// <param name="levent">The event data.</param>
        protected override void OnLayout(LayoutEventArgs levent) {
            //Set before the base call, and only when it changes: assigning a maximum size lays the
            //panel out again, and this runs from that layout.
            Size cap = new Size(Math.Max(1, ClientSize.Width), 0);

            if (notice.MaximumSize != cap) {
                notice.MaximumSize = cap;
                status.MaximumSize = cap;
            }

            base.OnLayout(levent);
        }

        /// <summary>Compiles the program and sets the fixed draw state, once, on this context.</summary>
        /// <remarks>
        ///     <c>Load</c> is the first point at which a context exists here. Doing any of this in the
        ///     constructor would run it against whichever context happened to be current, which is the
        ///     Entities viewport's if that page was opened first.
        /// </remarks>
        private void PrepareContext() {
            surface.MakeCurrent();

            program = BuildProgram();
            uModel = GL.GetUniformLocation(program, "uModel");
            uView = GL.GetUniformLocation(program, "uView");
            uProj = GL.GetUniformLocation(program, "uProj");
            uLightDir = GL.GetUniformLocation(program, "uLightDir");

            GL.UseProgram(program);
            GL.Uniform1(GL.GetUniformLocation(program, "uTexture"), 0);
            GL.Uniform2(GL.GetUniformLocation(program, "uTexOffset"), 0f, 0f);
            GL.UseProgram(0);

            //A near-black canvas rather than the page colour: a particle's own colour is the whole
            //content of this view, and a light background washes out every additively bright effect
            //in the cache.
            GL.ClearColor(0.06f, 0.07f, 0.09f, 1f);
            GL.Enable(EnableCap.DepthTest);
            GL.Enable(EnableCap.Blend);
            GL.BlendFunc(BlendingFactor.SrcAlpha, BlendingFactor.OneMinusSrcAlpha);
            GL.Disable(EnableCap.CullFace);

            contextReady = true;
            ResizeViewport();
            SyncClock();
        }

        /// <summary>Compiles and links this context's own copy of the model shader.</summary>
        /// <remarks>
        ///     The same pair the Entities viewport uses, because <see cref="ParticleBillboards"/>
        ///     writes its quads in that shader's twelve-float layout and
        ///     <see cref="OverlayGeometry.Unlit"/> pre-divides the colour by exactly the lighting term
        ///     it applies. A shader of this panel's own would have to reproduce both, and a
        ///     disagreement in either would show as particles of the wrong brightness rather than as
        ///     an error.
        /// </remarks>
        /// <returns>The linked program.</returns>
        private static int BuildProgram() {
            int vertex = Compile(ShaderType.VertexShader, "texture.vert");
            int fragment = Compile(ShaderType.FragmentShader, "texture.frag");

            int linked = GL.CreateProgram();
            GL.AttachShader(linked, vertex);
            GL.AttachShader(linked, fragment);
            GL.LinkProgram(linked);
            GL.GetProgram(linked, GetProgramParameterName.LinkStatus, out int ok);

            if (ok == 0)
                Debug("Particle preview program link error: " + GL.GetProgramInfoLog(linked));

            GL.DeleteShader(vertex);
            GL.DeleteShader(fragment);
            return linked;
        }

        /// <summary>Compiles one shader stage from the deployed shader directory.</summary>
        /// <param name="stage">Which stage.</param>
        /// <param name="file">The file name under <c>Shaders/</c>.</param>
        /// <returns>The compiled shader.</returns>
        private static int Compile(ShaderType stage, string file) {
            string source = File.ReadAllText(
                Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Shaders", file));

            int shader = GL.CreateShader(stage);
            GL.ShaderSource(shader, source);
            GL.CompileShader(shader);
            GL.GetShader(shader, ShaderParameter.CompileStatus, out int ok);

            if (ok == 0)
                Debug("Particle preview " + stage + " compile error: " + GL.GetShaderInfoLog(shader));

            return shader;
        }

        /// <summary>Puts the simulation back to its first frame.</summary>
        /// <remarks>
        ///     <c>SetModels</c> rather than <c>Reset</c>, because the attachment list itself has
        ///     changed - a reset keeps the emitters it already had and would keep running the
        ///     previous selection.
        /// </remarks>
        private void Restart() {
            system.SetModels(new ModelDefinition[1] { face });
            elapsed.Restart();
            SyncClock();
            UpdateStatus();
            surface.Invalidate();
        }

        /// <summary>Starts or stops the clock to match what the preview currently needs.</summary>
        private void SyncClock() {
            bool wanted = contextReady && Visible && surface.IsHandleCreated
                && (system.EmitterCount > 0 || system.LiveParticleCount > 0);

            if (wanted == clock.Enabled)
                return;

            if (wanted) {
                //Restarted rather than left running, so the first tick after a resume measures the
                //gap since the resume and not since the page was last on screen.
                elapsed.Restart();
                clock.Start();
            } else {
                clock.Stop();
                elapsed.Reset();
            }
        }

        /// <summary>Advances the simulation by real elapsed time and repaints only if it moved.</summary>
        private void Tick() {
            if (!Visible || !contextReady) {
                SyncClock();
                return;
            }

            double seconds = elapsed.Elapsed.TotalSeconds;
            elapsed.Restart();

            if (!system.Advance(seconds))
                return;

            UpdateStatus();
            surface.Invalidate();
        }

        /// <summary>Matches the GL viewport to the control after a layout change.</summary>
        private void ResizeViewport() {
            if (!contextReady || !surface.IsHandleCreated)
                return;

            surface.MakeCurrent();
            GL.Viewport(0, 0, Math.Max(1, surface.Width), Math.Max(1, surface.Height));
        }

        /// <summary>Orbits or pans the camera as the mouse is dragged.</summary>
        /// <param name="sender">The surface.</param>
        /// <param name="e">The event data.</param>
        private void OnDrag(object? sender, MouseEventArgs e) {
            if (dragFrom == null)
                return;

            float dx = e.X - dragFrom.Value.X;
            float dy = e.Y - dragFrom.Value.Y;
            dragFrom = e.Location;

            if (e.Button == MouseButtons.Right) {
                //Panned in the camera's own plane, so a drag moves the effect the way it looks like
                //it should whatever angle the camera is at.
                CameraBasis(out System.Numerics.Vector3 right, out System.Numerics.Vector3 up);
                float scale = distance / 400f;
                target -= new Vector3(right.X, right.Y, right.Z) * dx * scale;
                target += new Vector3(up.X, up.Y, up.Z) * dy * scale;
            } else {
                yaw -= dx * 0.01f;

                //Clamped short of the poles. Straight down collapses the cross product the billboard
                //basis is built from, and every quad would land at NaN rather than merely look odd.
                pitch = Math.Clamp(pitch + dy * 0.01f, -1.5f, 1.5f);
            }

            surface.Invalidate();
        }

        /// <summary>Zooms on the wheel.</summary>
        /// <param name="sender">The surface.</param>
        /// <param name="e">The event data.</param>
        private void OnWheel(object? sender, MouseEventArgs e) {
            //Multiplicative, so one notch covers the same proportion of the distance at every scale -
            //a fixed step is imperceptible far out and lands inside the effect close in.
            distance = Math.Clamp(distance * (e.Delta > 0 ? 0.9f : 1f / 0.9f), 0.2f, 200f);
            surface.Invalidate();
        }

        /// <summary>Where the camera is, from the orbit angles.</summary>
        /// <returns>The camera position in world space.</returns>
        private Vector3 CameraPosition() {
            return new Vector3(
                target.X + distance * (float) (Math.Cos(pitch) * Math.Sin(yaw)),
                target.Y + distance * (float) Math.Sin(pitch),
                target.Z + distance * (float) (Math.Cos(pitch) * Math.Cos(yaw)));
        }

        /// <summary>The camera's right and up axes, which is what makes a quad face the camera.</summary>
        /// <remarks>
        ///     The same construction the client uses - it reads the modelview matrix's first two rows
        ///     (<c>Class360.java:110-115</c>) - and the same one the Entities viewport builds, so a
        ///     particle is the same size and orientation in both.
        /// </remarks>
        /// <param name="right">The right axis, unit length.</param>
        /// <param name="up">The up axis, unit length.</param>
        private void CameraBasis(out System.Numerics.Vector3 right, out System.Numerics.Vector3 up) {
            Vector3 forward = Vector3.Normalize(target - CameraPosition());
            Vector3 sideways = Vector3.Cross(forward, Vector3.UnitY);

            //A camera looking straight down leaves the cross product at zero, and normalising that
            //gives every billboard a NaN position rather than a visible defect.
            sideways = sideways.LengthSquared > 1E-12f ? Vector3.Normalize(sideways) : Vector3.UnitX;

            right = new System.Numerics.Vector3(sideways.X, sideways.Y, sideways.Z);

            Vector3 cameraUp = Vector3.Normalize(Vector3.Cross(sideways, forward));
            up = new System.Numerics.Vector3(cameraUp.X, cameraUp.Y, cameraUp.Z);
        }

        /// <summary>Uploads this frame's quads and draws them.</summary>
        private void Render() {
            if (!contextReady)
                return;

            //Every frame rather than once: the Entities viewport makes its own context current
            //whenever it paints, and whichever painted last owns the thread.
            surface.MakeCurrent();
            GL.Viewport(0, 0, Math.Max(1, surface.Width), Math.Max(1, surface.Height));
            GL.Clear(ClearBufferMask.ColorBufferBit | ClearBufferMask.DepthBufferBit);

            billboards ??= new ViewportOverlayRenderer { ShowWireframe = false, ShowParticles = true };

            /* Without this every quad binds the renderer's flat white texture, which is what made
               the preview a field of plain squares. The resolver is re-pointed rather than assigned
               once, because Bind builds a new cache per cache and a stale closure would hand back
               handles belonging to a cache that has been closed. */
            billboards.MaterialTextureResolver = textures == null
                ? null
                : materialId => textures.GetParticleTexture(materialId);

            Vector3 eye = CameraPosition();

            //The light follows the camera, which is what the billboards assume: each quad writes the
            //light direction as its own normal so N dot L is exactly one, and OverlayGeometry.Unlit
            //has already divided the colour by the lighting term that produces. A light pointing
            //anywhere else would darken every particle by an amount nothing states.
            Vector3 light = Vector3.Normalize(eye - target);

            Matrix4 model = Matrix4.Identity;
            Matrix4 view = Matrix4.LookAt(eye, target, Vector3.UnitY);
            Matrix4 projection = Matrix4.CreatePerspectiveFieldOfView(
                MathHelper.DegreesToRadians(45f),
                Math.Max(1, surface.Width) / (float) Math.Max(1, surface.Height),
                0.05f,
                1000f);

            GL.UseProgram(program);
            GL.UniformMatrix4(uModel, transpose: false, ref model);
            GL.UniformMatrix4(uView, transpose: false, ref view);
            GL.UniformMatrix4(uProj, transpose: false, ref projection);
            GL.Uniform3(uLightDir, light.X, light.Y, light.Z);

            CameraBasis(out System.Numerics.Vector3 right, out System.Numerics.Vector3 up);
            billboards.SetParticles(system, right, up, new System.Numerics.Vector3(light.X, light.Y, light.Z));
            billboards.Draw();

            GL.UseProgram(0);
            surface.SwapBuffers();
        }

        /// <summary>Rewrites the status line from the simulation's own counters.</summary>
        /// <remarks>
        ///     The counters rather than a sentence of this panel's own. A truncated effect, a missing
        ///     definition and a working effect are indistinguishable on a surface nothing can capture,
        ///     and a rising refusal or missing count is how a human tells which they are looking at -
        ///     which is why <see cref="ParticleSystem"/> makes every one of them public.
        /// </remarks>
        private void UpdateStatus() {
            if (source.Emitter == null)
                return;

            string line = system.Status;

            if (system.MissingEffectorCount > 0)
                line += ", " + system.MissingEffectorCount + " effector(s) missing";

            if (system.SkippedAttachmentKeyReferences > 0)
                line += ", " + system.SkippedAttachmentKeyReferences + " opcode-25 ref(s) skipped";

            if (system.SpawnsRefusedByCap > 0)
                line += ", " + system.SpawnsRefusedByCap + " refused by the " + system.MaximumParticles + " cap";

            status.Text = line;
        }

        /// <summary>
        ///     Builds the one triangle the emitter is attached to.
        /// </summary>
        /// <remarks>
        ///     A model, because that is the only way to attach an emitter -
        ///     <see cref="ParticleSystem.SetModels"/> reads the attachment list off the mesh, exactly
        ///     as the client does. The winding is chosen so the face normal points along model -y,
        ///     which <see cref="RenderSpace"/> maps to world +y: an emitter with no direction of its
        ///     own then sprays upwards rather than into the floor of the view.
        ///     <para>
        ///     <see cref="ModelDefinition.VertexCount"/> and its siblings are left at zero and nothing
        ///     reads them here. The simulation bounds every index against the array lengths instead,
        ///     which is what lets a synthetic mesh be three arrays rather than a decoded model.
        ///     </para>
        /// </remarks>
        /// <returns>The mesh.</returns>
        private static ModelDefinition BuildEmitterFace() {
            const int Half = FaceSideModelUnits / 2;

            return new ModelDefinition {
                VertX = new int[3] { -Half, Half, 0 },
                VertY = new int[3] { 0, 0, 0 },
                VertZ = new int[3] { -Half, -Half, Half },
                faceIndices1 = new int[1] { 0 },
                faceIndices2 = new int[1] { 1 },
                faceIndices3 = new int[1] { 2 },
                Emitters = Array.Empty<ModelParticleEmitter>(),
                Effectors = Array.Empty<ModelParticleEffector>()
            };
        }

        /// <summary>
        ///     The selected emitter, plus the cache for whatever effectors it names.
        /// </summary>
        /// <remarks>
        ///     Two sources rather than one because they answer different questions. The emitter is the
        ///     record the grid has already decoded, so previewing it needs no cache read and would
        ///     survive an edit that has not been written; its effectors are named by id and have to
        ///     come from index 27 group 1.
        /// </remarks>
        private sealed class PreviewParticleSource : IParticleDataSource {
            /// <summary>The emitter being previewed, or null.</summary>
            internal ParticleEmitterDefinition? Emitter { get; set; }

            /// <summary>Where effectors come from, or null while no cache is bound.</summary>
            internal CacheParticleDataSource? Effectors { get; set; }

            /// <inheritdoc/>
            public ParticleEmitterDefinition? GetEmitter(int emitterId) {
                return Emitter != null && Emitter.Id == emitterId ? Emitter : null;
            }

            /// <inheritdoc/>
            public ParticleEffectorDefinition? GetEffector(int effectorId) {
                return Effectors?.GetEffector(effectorId);
            }
        }
    }
}
