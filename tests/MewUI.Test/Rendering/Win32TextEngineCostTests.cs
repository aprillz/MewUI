extern alias MewVGWin32;

using System.Diagnostics;
using System.Globalization;
using Aprillz.MewUI;
using Aprillz.MewUI.Controls;
using Aprillz.MewUI.Rendering;
using Aprillz.MewUI.Rendering.Gdi;
using MewUI.Test.Infrastructure;

using MewVGWin32GraphicsFactory = MewVGWin32::Aprillz.MewUI.Rendering.MewVG.MewVGWin32GraphicsFactory;

namespace MewUI.Test.Rendering;

/// <summary>
/// Measures what text costs on the Win32 backends under the text engine the process runs with: the
/// first frame of a page of text, frames that draw the same text again, frames whose text is new every
/// time, measuring alone, and what the process holds afterwards. It reports numbers and asserts none.
/// The engine is fixed for the life of a process, so the two engines are compared by running it twice:
/// MEWUI_TEXT_ENGINE_COST=1 runs it, and MEWUI_TEXT_ENGINE=DirectWrite selects DirectWrite.
/// Not parallelizable: timing, and the process-wide graphics factory.
/// </summary>
[TestClass]
[DoNotParallelize]
public sealed class Win32TextEngineCostTests
{
    private const string ENGINE_SWITCH = "Aprillz.MewUI.Win32.DirectWriteText.Enabled";
    private const int WIDTH = 1200;
    private const int HEIGHT = 800;
    private const int CHANGING_COUNT = 50;
    private const int WARM_UP_FRAMES = 200;
    private const int FRAMES = 200;
    private const int MEASURED_STRINGS = 2000;

    [TestMethod]
    [DataRow("Gdi", 100)]
    [DataRow("MewVG", 100)]
    [DataRow("Gdi", 300)]
    [DataRow("MewVG", 300)]
    public void MeasureText(string backend, int labelCount)
    {
        if (!OperatingSystem.IsWindows() || Environment.GetEnvironmentVariable("MEWUI_TEXT_ENGINE_COST") != "1")
        {
            Assert.Inconclusive("Set MEWUI_TEXT_ENGINE_COST=1 on Windows to run the text engine cost measurement.");
            return;
        }

        bool directWrite = Environment.GetEnvironmentVariable("MEWUI_TEXT_ENGINE") == "DirectWrite";

        // Before anything reads the gate: it is read once and stays for the life of the process.
        AppContext.SetSwitch(ENGINE_SWITCH, directWrite);

        IGraphicsFactory factory = backend == "Gdi" ? new GdiGraphicsFactory() : new MewVGWin32GraphicsFactory();
        using var disposable = factory as IDisposable;
        Application.DefaultGraphicsFactory = factory;
        using var renderScope = factory is MewVGWin32GraphicsFactory ? factory.AcquireBackgroundRenderScope() : null;

        var labels = new List<TextBlock>();
        var page = new WrapPanel();
        for (int index = 0; index < labelCount; index++)
        {
            var label = new TextBlock
            {
                Text = Sample(index),
                FontSize = 11 + (index % 5) * 2,
                Margin = new Thickness(4, 2, 4, 2),
            };
            labels.Add(label);
            page.Children(label);
        }

        var window = HeadlessWindow.Create(WIDTH, HEIGHT);
        window.Content = new Border { Background = Color.FromArgb(255, 250, 250, 250), Child = page };

        long firstStart = Stopwatch.GetTimestamp();
        window.PerformLayout();
        window.PerformLayout();
        double layoutMs = Stopwatch.GetElapsedTime(firstStart).TotalMilliseconds;

        using var surface = factory.CreateSurface(RenderSurfaceDescriptor.Offscreen(WIDTH, HEIGHT, 1.0, hasAlpha: false));
        long drawStart = Stopwatch.GetTimestamp();
        // One context for every frame, as a window has: what a context learns about a run of text stays with it.
        using var context = factory.CreateContext(surface);
        DrawFrame(context, surface, window);
        double firstFrameMs = Stopwatch.GetElapsedTime(drawStart).TotalMilliseconds;

        for (int warm = 0; warm < WARM_UP_FRAMES; warm++)
        {
            DrawFrame(context, surface, window);
        }

        var same = new double[FRAMES];
        long sameBytes = GC.GetAllocatedBytesForCurrentThread();
        for (int frame = 0; frame < FRAMES; frame++)
        {
            long start = Stopwatch.GetTimestamp();
            DrawFrame(context, surface, window);
            same[frame] = Stopwatch.GetElapsedTime(start).TotalMilliseconds;
        }

        sameBytes = GC.GetAllocatedBytesForCurrentThread() - sameBytes;

        var changing = new double[FRAMES];
        long changingBytes = GC.GetAllocatedBytesForCurrentThread();
        for (int frame = 0; frame < FRAMES; frame++)
        {
            for (int index = 0; index < CHANGING_COUNT; index++)
            {
                labels[index].Text = string.Create(CultureInfo.InvariantCulture, $"{Sample(index)} {frame}");
            }

            long start = Stopwatch.GetTimestamp();
            window.PerformLayout();
            DrawFrame(context, surface, window);
            changing[frame] = Stopwatch.GetElapsedTime(start).TotalMilliseconds;
        }

        changingBytes = GC.GetAllocatedBytesForCurrentThread() - changingBytes;

        // Layout alone: text the engine has not seen is measured and nothing is drawn.
        var measured = new StackPanel { Orientation = Orientation.Vertical };
        for (int index = 0; index < MEASURED_STRINGS; index++)
        {
            measured.Children(new TextBlock { Text = string.Create(CultureInfo.InvariantCulture, $"measured {index} {Sample(index)}") });
        }

        var measureWindow = HeadlessWindow.Create(WIDTH, HEIGHT);
        measureWindow.Content = new ScrollViewer { Content = measured };
        long measureStart = Stopwatch.GetTimestamp();
        measureWindow.PerformLayout();
        double measureMs = Stopwatch.GetElapsedTime(measureStart).TotalMilliseconds;

        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();
        using var process = Process.GetCurrentProcess();
        process.Refresh();
        var resources = RenderResourceMetrics.Snapshot();

        Console.Error.WriteLine(string.Create(CultureInfo.InvariantCulture, $"""

            === text cost ({backend} backend, {(directWrite ? "DirectWrite" : "Gdi")} text, {labelCount} labels) ===
            layout of the page        : {layoutMs,9:0.00} ms
            first frame               : {firstFrameMs,9:0.00} ms
            same text again           : median {Median(same),7:0.000} ms, {sameBytes / FRAMES / 1024.0,7:0.0} KB a frame
            {CHANGING_COUNT} labels new each frame : median {Median(changing),7:0.000} ms, {changingBytes / FRAMES / 1024.0,7:0.0} KB a frame
            measuring {MEASURED_STRINGS} strings    : {measureMs,9:0.00} ms
            managed heap              : {GC.GetTotalMemory(false) / 1048576.0,9:0.0} MB
            private bytes             : {process.PrivateMemorySize64 / 1048576.0,9:0.0} MB
            working set               : {process.WorkingSet64 / 1048576.0,9:0.0} MB
            text cache                : {resources.TextCacheBytes / 1048576.0,9:0.00} MB
            """));
    }

    private static void DrawFrame(IGraphicsContext context, IRenderSurface surface, Window window)
    {
        context.BeginFrame(surface);
        context.Clear(Color.White);
        window.Content!.Render(context);
        context.EndFrame();
    }

    private static double Median(double[] samples)
    {
        var sorted = (double[])samples.Clone();
        Array.Sort(sorted);
        return sorted[sorted.Length / 2];
    }

    private static string Sample(int index) => (index % 6) switch
    {
        0 => "The quick brown fox " + index,
        1 => "안녕하세요 미유아이 " + index,
        2 => "Save As… (Ctrl+Shift+S) " + index,
        3 => "0123456789.,:;-+ " + index,
        4 => "Résumé naïve façade " + index,
        _ => "Lorem ipsum dolor sit amet, consectetur " + index,
    };
}
