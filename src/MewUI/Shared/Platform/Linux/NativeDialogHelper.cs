using Aprillz.MewUI.Controls;

namespace Aprillz.MewUI.Platform.Linux;

/// <summary>
/// Shared helpers for native Linux dialogs (XDG portal, zenity/kdialog): bridge an async/blocking dialog to
/// the synchronous service contract via a nested event loop (keeps the UI responsive) and make it modal by
/// disabling the owner window for the dialog's duration.
/// </summary>
internal static class NativeDialogHelper
{
    /// <summary>
    /// Disables the owner window (by native handle) until the returned scope is disposed, so the app ignores
    /// input while a native dialog is shown. X11 has no OS-level "disabled window", so this is the app-level
    /// modal guard; focus/z-order remain the window manager's responsibility (via the dialog's parent hint).
    /// </summary>
    public static IDisposable? BeginOwnerModal(nint owner)
    {
        if (owner == 0 || !Application.IsRunning)
        {
            return null;
        }

        var windows = Application.Current.AllWindows;
        for (int i = 0; i < windows.Count; i++)
        {
            if (windows[i].Handle == owner)
            {
                windows[i].AcquireModalDisable();
                return new OwnerModalScope(windows[i]);
            }
        }

        return null;
    }

    /// <summary>
    /// Runs the nested event loop until <paramref name="task"/> completes, then returns its result. Keeps the
    /// app rendering/responsive while a native dialog (which completes off the UI thread) is open.
    /// </summary>
    public static T PumpUntil<T>(Task<T> task)
    {
        if (!Application.IsRunning)
        {
            return task.GetAwaiter().GetResult();
        }

        var app = Application.Current;
        var dispatcher = app.Dispatcher;
        // The dialog task may complete off the UI thread; poke the dispatcher so the nested loop wakes and re-checks.
        if (dispatcher != null)
        {
            _ = task.ContinueWith(_ => dispatcher.BeginInvoke(static () => { }), TaskScheduler.Default);
        }

        app.PlatformHost.RunNestedLoop(() => !task.IsCompleted);
        return task.GetAwaiter().GetResult();
    }

    private sealed class OwnerModalScope(Window owner) : IDisposable
    {
        public void Dispose() => owner.ReleaseModalDisable();
    }
}
