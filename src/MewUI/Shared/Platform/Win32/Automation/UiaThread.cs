namespace Aprillz.MewUI.Platform.Win32.Automation;

/// <summary>
/// Runs provider work on the UI thread. UI Automation calls a provider from its own threads, while peers and the
/// elements they read belong to the UI thread.
/// </summary>
internal static class UiaThread
{
    // Long enough for a busy UI thread; a hung one fails the reader's call instead of holding it forever.
    private const int TIMEOUT_MILLISECONDS = 10_000;

    /// <summary>Runs <paramref name="body"/> on the UI thread and returns its HRESULT, or the failure an exception maps to.</summary>
    internal static int Run(Func<int> body)
    {
        var dispatcher = Application.IsRunning ? Application.Current.Dispatcher : null;
        if (dispatcher == null)
        {
            return UiaNative.UIA_E_ELEMENTNOTAVAILABLE;
        }

        if (dispatcher.IsOnUIThread)
        {
            return Guard(body);
        }

        var call = new PendingCall(body);
        dispatcher.BeginInvoke(DispatcherPriority.Normal, call.Execute);
        return call.Wait();
    }

    /// <summary>Queues <paramref name="action"/> on the UI thread without waiting for it.</summary>
    internal static void Post(Action action)
    {
        var dispatcher = Application.IsRunning ? Application.Current.Dispatcher : null;
        dispatcher?.BeginInvoke(DispatcherPriority.Normal, () =>
        {
            try
            {
                action();
            }
            catch (InvalidOperationException)
            {
                // The element changed (disabled, removed) after the reader was told the action was accepted.
            }
        });
    }

    private static int Guard(Func<int> body)
    {
        try
        {
            return body();
        }
        catch (InvalidOperationException)
        {
            return UiaNative.UIA_E_INVALIDOPERATION;
        }
        catch (ArgumentException)
        {
            return UiaNative.E_INVALIDARG;
        }
        catch (Exception exception)
        {
            return exception.HResult;
        }
    }

    private sealed class PendingCall
    {
        private readonly Func<int> _body;
        private readonly ManualResetEventSlim _done = new();
        private int _result = UiaNative.UIA_E_TIMEOUT;

        public PendingCall(Func<int> body) => _body = body;

        public void Execute()
        {
            _result = Guard(_body);
            _done.Set();
        }

        public int Wait() => _done.Wait(TIMEOUT_MILLISECONDS) ? _result : UiaNative.UIA_E_TIMEOUT;
    }
}
