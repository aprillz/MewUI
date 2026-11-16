using Microsoft.Testing.Platform.Builder;

namespace MewUI.WindowAutomationTest;

/// <summary>
/// Entry point of the macOS runner executable, compiled only for osx runtime identifiers in place of the
/// generated one. AppKit runs only on the process main thread, so the test platform runs on a worker thread
/// while the main thread hosts the application loop for <see cref="RealAppSession"/>.
/// </summary>
internal static class MacMainThreadEntryPoint
{
    public static int Main(string[] args)
    {
        RealAppSession.HostsLoopOnMainThread = true;

        var run = Task.Run(async () =>
        {
            var builder = await TestApplication.CreateBuilderAsync(args);
            global::Aprillz.MewUI.WindowAutomationTest.SelfRegisteredExtensions.AddSelfRegisteredExtensions(builder, args);
            using var app = await builder.BuildAsync();
            return await app.RunAsync();
        });

        // The loop ends once the platform has reported, and Run returning hands the platform's exit code back.
        _ = run.ContinueWith(static _ => RealAppSession.EndMainThreadLoop(), TaskScheduler.Default);

        RealAppSession.RunLoop();
        return run.GetAwaiter().GetResult();
    }
}
