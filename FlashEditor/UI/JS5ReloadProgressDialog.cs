using FlashEditor.cache;
using System;
using System.Drawing;
using System.Runtime.ExceptionServices;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using static FlashEditor.Utils.DebugUtil;

namespace FlashEditor.UI {
    /// <summary>
    ///     Shows what a save is waiting for while the JS5 reload handshake runs, and lets the user
    ///     give up on it.
    /// </summary>
    /// <remarks>
    ///     The handshake blocks for as long as the server takes to shut its handles, up to the
    ///     configured timeout. Run on the UI thread that is a frozen window with no title bar
    ///     response and no explanation, which is worse than the failure it is recovering from: a
    ///     user whose server is not running would see a dead application for thirty seconds and
    ///     then an error. So the work runs on a worker and this dialog is what the user sees.
    ///     <para>
    ///     It is modal on purpose. The cache is mid-write for part of that window and nothing else
    ///     in the editor may touch it, so blocking input is the correct behaviour rather than a
    ///     shortcut - and a modal dialog keeps the message loop pumping, which is what stops the
    ///     window from greying out.
    ///     </para>
    ///     <para>
    ///     The timeout and its message box are still there and unchanged. They are the fallback for
    ///     a server that never answers; this is the foreground behaviour while it might still.
    ///     </para>
    /// </remarks>
    internal sealed class JS5ReloadProgressDialog : Form {
        /// <summary>What the user is waiting for, above the countdown.</summary>
        private readonly Label _message = new Label();

        /// <summary>The countdown, or what the write is doing once the wait is over.</summary>
        private readonly Label _detail = new Label();

        private readonly Button _cancel = new Button();
        private readonly CancellationTokenSource _cancellation = new CancellationTokenSource();
        private readonly Action<CancellationToken, Action<TimeSpan>, Action> _work;

        /// <summary>Whatever the worker threw, rethrown on the caller's thread once the dialog closes.</summary>
        private ExceptionDispatchInfo? _failure;

        /// <summary>The last whole second shown, so a 100 ms poll does not repaint ten times a second.</summary>
        private int _shownSeconds = -1;

        /// <summary>
        ///     Runs a save through the handshake, showing progress and offering cancellation.
        /// </summary>
        /// <remarks>
        ///     With the handshake off this is a plain call on the calling thread and no window
        ///     appears at all, which keeps the default path exactly what it was before the
        ///     handshake existed. Only the user who ticked the menu item pays for any of this.
        /// </remarks>
        /// <param name="owner">The window to centre on and block, or null when the caller has no form yet.</param>
        /// <param name="cacheDirectory">The directory being written.</param>
        /// <param name="save">The write to perform, which takes whatever locks it needs itself.</param>
        /// <exception cref="OperationCanceledException">The user abandoned the wait. Nothing was written.</exception>
        /// <exception cref="TimeoutException">No server released the cache in time. Nothing was written.</exception>
        public static void Save(IWin32Window? owner, string cacheDirectory, Action save) {
            if (!JS5ReloadHandshake.Enabled) {
                save();
                return;
            }

            using var dialog = new JS5ReloadProgressDialog(cacheDirectory,
                (cancellation, waiting, writing) =>
                    JS5ReloadHandshake.AroundSave(cacheDirectory, save, cancellation, waiting, writing));

            dialog.ShowDialog(owner);
            dialog._failure?.Throw();
        }

        /// <summary>Builds the dialog around the work it will run.</summary>
        /// <param name="cacheDirectory">The directory being written, named on screen.</param>
        /// <param name="work">What to run on the worker.</param>
        private JS5ReloadProgressDialog(string cacheDirectory,
                                        Action<CancellationToken, Action<TimeSpan>, Action> work) {
            _work = work;

            //Matching the main form: Dpi against 96, so nothing here is multiplied by a ratio
            //derived from a font this application does not use.
            AutoScaleMode = AutoScaleMode.Dpi;
            AutoScaleDimensions = new SizeF(96F, 96F);

            Text = "JS5 live reload";
            FormBorderStyle = FormBorderStyle.FixedDialog;
            StartPosition = FormStartPosition.CenterParent;
            MinimizeBox = false;
            MaximizeBox = false;
            ShowInTaskbar = false;

            //Sized by what it holds rather than by literals, which is what stops a caption being
            //clipped to "Waiting for the JS5 up" on a machine whose scaling differs from this one.
            AutoSize = true;
            AutoSizeMode = AutoSizeMode.GrowAndShrink;
            Padding = new Padding(12);

            _message.AutoSize = true;
            _message.MaximumSize = new Size(TextWidth, 0);
            _message.Text = "Waiting for the JS5 update server to release the cache before writing:"
                + Environment.NewLine + cacheDirectory
                + Environment.NewLine + Environment.NewLine
                + "The server cannot serve anything until the write finishes.";

            _detail.AutoSize = true;
            _detail.MaximumSize = new Size(TextWidth, 0);

            _cancel.AutoSize = true;
            _cancel.Text = "Cancel";
            _cancel.Anchor = AnchorStyles.Right;
            _cancel.Click += (sender, e) => RequestCancel();

            var layout = new TableLayoutPanel {
                AutoSize = true,
                AutoSizeMode = AutoSizeMode.GrowAndShrink,
                ColumnCount = 1,
                RowCount = 3,
                Dock = DockStyle.Fill
            };

            for (int row = 0; row < 3; row++)
                layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));

            layout.Controls.Add(_message, 0, 0);
            layout.Controls.Add(_detail, 0, 1);
            layout.Controls.Add(_cancel, 0, 2);
            Controls.Add(layout);

            //The Cancel button is the only way out, so Escape has to reach it: there is no control
            //box and closing the window mid-write is not something to offer.
            CancelButton = _cancel;
            ControlBox = false;
        }

        /// <summary>How wide a label may grow before it wraps, in the dialog's own design units.</summary>
        /// <remarks>
        ///     A maximum rather than a size. It bounds the width of a path that could otherwise be
        ///     several hundred characters long and push the dialog off the screen; everything else
        ///     is measured.
        /// </remarks>
        private const int TextWidth = 420;

        /// <inheritdoc/>
        protected override void OnShown(EventArgs e) {
            base.OnShown(e);

            Task.Run(() => {
                try {
                    _work(_cancellation.Token, ReportWaiting, ReportWriting);
                } catch (Exception ex) {
                    //Captured rather than thrown here: this is a worker thread, and an exception
                    //escaping it would take the process down instead of reaching the save path's
                    //error handling.
                    _failure = ExceptionDispatchInfo.Capture(ex);
                }
            }).ContinueWith(_ => Finish());
        }

        /// <inheritdoc/>
        protected override void Dispose(bool disposing) {
            if (disposing)
                _cancellation.Dispose();

            base.Dispose(disposing);
        }

        /// <summary>Stops the wait and says so, rather than closing on a write that is still running.</summary>
        private void RequestCancel() {
            _cancel.Enabled = false;
            _detail.Text = "Cancelling, and withdrawing the request so the server reopens the cache...";
            _cancellation.Cancel();
        }

        /// <summary>Redraws the countdown, at most once a second.</summary>
        /// <param name="remaining">How long is left before the wait gives up.</param>
        private void ReportWaiting(TimeSpan remaining) {
            int seconds = (int) Math.Ceiling(remaining.TotalSeconds);

            if (seconds == _shownSeconds)
                return;

            _shownSeconds = seconds;
            OnUiThread(() => _detail.Text = "Giving up in " + seconds + " second" + (seconds == 1 ? "" : "s") + ".");
        }

        /// <summary>Switches the dialog from waiting to writing, and takes cancellation away.</summary>
        /// <remarks>
        ///     Cancelling a write is not offered because there is nothing to cancel to: the
        ///     promotion replaces the dat2 and every index file together, and abandoning it half
        ///     way is the one outcome the save path exists to prevent.
        /// </remarks>
        private void ReportWriting() {
            OnUiThread(() => {
                _cancel.Enabled = false;
                _detail.Text = "The server has released the cache. Writing, which cannot be cancelled...";
            });
        }

        /// <summary>Closes the dialog once the worker has finished, however it finished.</summary>
        private void Finish() {
            OnUiThread(Close);
        }

        /// <summary>
        ///     Runs an update on the UI thread, dropping it if the window has already gone.
        /// </summary>
        /// <remarks>
        ///     <c>BeginInvoke</c> rather than <c>Invoke</c>: the worker must never block on the UI
        ///     thread, because the UI thread can be inside the same store lock the write is about
        ///     to take, and a worker waiting on it would deadlock the save it is reporting on.
        /// </remarks>
        /// <param name="action">The update to run.</param>
        private void OnUiThread(Action action) {
            try {
                if (IsHandleCreated && !IsDisposed)
                    BeginInvoke(action);
            } catch (Exception ex) {
                //A window torn down between the check and the call is not worth failing a save for.
                Debug("JS5 reload: could not update the progress dialog: " + ex.Message);
            }
        }
    }
}
