using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace FlashEditor {
    static class FlashEditorForm {
        /// <summary>
        /// The main entry point for the application.
        /// </summary>
        /// <remarks>
        ///     <para>
        ///     <b>The DPI awareness is pinned here, and that is a crash fix rather than a
        ///     preference.</b> A process that never states an awareness runs unaware, but only by
        ///     default, which leaves any library free to change it later. OpenTK's
        ///     <c>GLControl</c> does: initialising GLFW calls
        ///     <c>SetProcessDpiAwarenessContext(PER_MONITOR_AWARE_V2)</c>, so the moment the
        ///     Entities page realises the 3-D viewport the form, the tab control and every page
        ///     already created flip from unaware to per-monitor while WinForms carries on creating
        ///     new pages unaware.
        ///     </para>
        ///     <para>
        ///     Windows refuses <c>SetParent</c> between two windows in different DPI awareness
        ///     contexts, failing with ERROR_INVALID_STATE (5023). Any page whose handle has to be
        ///     re-parented into the tab control after that point therefore throws
        ///     <c>Win32Exception</c> "Failed to set Win32 parent window of the Control" out of
        ///     <c>TabControl.UpdateTabSelection</c>, and because the tab control is left half way
        ///     through a selection change, <b>no further tab opens for the rest of the session</b>.
        ///     Measured by walking the navigation tree: the contexts split at the Entities page and
        ///     the exception lands several pages later, which is why it reads as a slow leak and is
        ///     not one - it happens at about 380 USER handles against a quota of 10,000.
        ///     </para>
        ///     <para>
        ///     Stating the awareness first is what fixes it: <c>SetProcessDpiAwarenessContext</c>
        ///     may only succeed once, so GLFW's later call is refused and every window in the
        ///     process keeps one context. <see cref="HighDpiMode.DpiUnaware"/> rather than
        ///     per-monitor because that is what the process already runs as today, so this changes
        ///     no layout: the form is <c>AutoScaleMode.Dpi</c> against 96 dpi and moving it to
        ///     per-monitor would rescale every view on a scaled display, which is a rendering change
        ///     and not this fix's to make.
        ///     </para>
        /// </remarks>
        [STAThread]
        static void Main() {
            Application.SetHighDpiMode(HighDpiMode.DpiUnaware);
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            Application.Run(new Editor());
        }
    }
}
