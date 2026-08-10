using OpenTK.GLControl;
using OpenTK.Graphics.OpenGL;
using OpenTK.Windowing.Common;
using System;
using System.Windows.Forms;

namespace FlashEditor.Definitions.Particles {
    /// <summary>
    ///     A second OpenGL surface, on the Particles tab, that clears to a fixed colour and draws
    ///     nothing else.
    /// </summary>
    /// <remarks>
    ///     <b>This exists to answer one question before anything is built on it: can this
    ///     application hold a second <c>GLControl</c> at all?</b> It could not, and the reason was
    ///     never the count. Realising a <c>GLControl</c> used to flip the whole process from
    ///     DPI-unaware to per-monitor part way through a session, and Windows then refuses
    ///     <c>SetParent</c> between windows in different awareness contexts, so the tab control
    ///     threw out of its next selection change and <b>no further tab opened for the rest of the
    ///     session</b>. <see cref="FlashEditorForm"/>'s remarks carry the whole failure.
    ///     <para>
    ///     That is fixed process-wide by pinning the awareness before any window exists, so the
    ///     second context should now be viable - but "should" is not evidence, and the failure is
    ///     silent enough that it was once read as a handle leak. So this lands on its own, and the
    ///     check is a walk across the tabs afterwards rather than a screenshot: no capture on this
    ///     machine can see a GL surface at all, which the header of
    ///     <c>tools/Capture-EditorTab.ps1</c> states at length.
    ///     </para>
    /// </remarks>
    internal sealed class ParticlePreviewPanel : UserControl {
        /// <summary>
        ///     The surface. Its own context, not the Entities viewport's.
        /// </summary>
        /// <remarks>
        ///     Built in code rather than by the designer because the designer's instance is the one
        ///     that must never be reparented, and putting a second one in the same file invites
        ///     exactly that edit.
        ///     <para>
        ///     <see cref="GLControl.IsEventDriven"/> is on, matching the Entities viewport: nothing
        ///     here runs a render loop, so the surface redraws when something invalidates it.
        ///     </para>
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

        /// <summary>Whether the context has been made current and its state set at least once.</summary>
        private bool contextReady;

        /// <summary>Creates the panel and wires the surface's paint path.</summary>
        public ParticlePreviewPanel() {
            Dock = DockStyle.Fill;
            Controls.Add(surface);

            surface.Load += (_, _) => PrepareContext();
            surface.Paint += (_, _) => Render();
            surface.Resize += (_, _) => ResizeViewport();
        }

        /// <summary>Sets the clear colour once, on the surface's own context.</summary>
        /// <remarks>
        ///     <c>Load</c> is the first point a context exists. Doing this in the constructor would
        ///     run against whichever context happened to be current - the Entities viewport's, if it
        ///     had been opened first - and set the wrong surface's state.
        /// </remarks>
        private void PrepareContext() {
            surface.MakeCurrent();
            GL.ClearColor(0.72f, 0.10f, 0.60f, 1f);
            contextReady = true;
            ResizeViewport();
        }

        /// <summary>Matches the GL viewport to the control after a layout change.</summary>
        private void ResizeViewport() {
            if (!contextReady || !surface.IsHandleCreated)
                return;

            surface.MakeCurrent();
            GL.Viewport(0, 0, Math.Max(1, surface.Width), Math.Max(1, surface.Height));
        }

        /// <summary>Clears and swaps.</summary>
        /// <remarks>
        ///     <see cref="GLControl.MakeCurrent"/> on every frame rather than once, because the
        ///     Entities viewport makes its own context current whenever it paints and whichever
        ///     painted last owns the thread.
        /// </remarks>
        private void Render() {
            if (!contextReady)
                return;

            surface.MakeCurrent();
            GL.Clear(ClearBufferMask.ColorBufferBit | ClearBufferMask.DepthBufferBit);
            surface.SwapBuffers();
        }
    }
}
