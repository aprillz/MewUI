using Aprillz.MewUI;
using Aprillz.MewUI.Controls;
using Aprillz.MewUI.Headless;

namespace MewUI.Headless.Test;

/// <summary>
/// The measure-only session the gate tests share, started on first use; a test that needs other
/// options borrows the process for its own session and gives it back.
/// </summary>
internal static class HeadlessTestHost
{
    private static HeadlessSession? _shared;

    public static HeadlessSession Shared => _shared ??= HeadlessSession.Start(new HeadlessOptions());

    /// <summary>Stops the shared session, runs <paramref name="body"/> with a session of <paramref name="options"/>, and disposes it.</summary>
    public static void WithSession(HeadlessOptions options, Action<HeadlessSession> body)
    {
        Shutdown();
        using var session = HeadlessSession.Start(options);
        body(session);
    }

    public static void Shutdown()
    {
        _shared?.Dispose();
        _shared = null;
    }

    /// <summary>Shows a window of the given size with <paramref name="content"/> on the shared session's UI thread.</summary>
    public static Window ShowWindow(Element content, double width = 400, double height = 300)
        => Shared.Run(() =>
        {
            var window = new Window
            {
                WindowSize = WindowSize.Resizable(width, height),
                Content = content,
            };
            window.Show();
            return window;
        });

    /// <summary>Closes <paramref name="window"/> on the UI thread so the next test starts from an empty application.</summary>
    public static void Close(Window window) => Shared.Run(window.Close);
}
