namespace Aprillz.MewUI.Platform.Win32;

internal static class StaHelper
{
    /// <summary>
    /// Runs <paramref name="func"/> on an STA thread and returns its result. While the application loop is
    /// running, pumps a nested loop so rendering and input stay live (same pattern as the X11 portal helper).
    /// </summary>
    public static T Run<T>(Func<T> func)
    {
        ArgumentNullException.ThrowIfNull(func);

        if (Thread.CurrentThread.GetApartmentState() == ApartmentState.STA)
        {
            return func();
        }

        // Capture on the calling thread; the app can be quitting by the time the worker finishes.
        var dispatcher = Application.IsRunning ? Application.Current.Dispatcher : null;

        T result = default!;
        Exception? exception = null;
        using var done = new ManualResetEventSlim(false);

        var thread = new Thread(() =>
        {
            try
            {
                result = func();
            }
            catch (Exception ex)
            {
                exception = ex;
            }
            finally
            {
                done.Set();
                // The worker finishes off the UI thread; poke the dispatcher so the nested loop wakes and re-checks.
                dispatcher?.BeginInvoke(static () => { });
            }
        })
        {
            IsBackground = true
        };

#pragma warning disable CA1416 // Validate platform compatibility
        thread.SetApartmentState(ApartmentState.STA);
#pragma warning restore CA1416 // Validate platform compatibility
        thread.Start();

        if (Application.IsRunning)
        {
            Application.Current.PlatformHost.RunNestedLoop(() => !done.IsSet);
        }

        done.Wait();

        if (exception != null)
        {
            throw new InvalidOperationException("STA call failed.", exception);
        }

        return result;
    }
}
