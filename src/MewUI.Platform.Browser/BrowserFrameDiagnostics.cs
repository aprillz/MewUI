using System.Diagnostics;
using System.Runtime.InteropServices.JavaScript;
using System.Runtime.Versioning;

namespace Aprillz.MewUI.Platform.Browser;

/// <summary>
/// Per-frame timing and scene counts for looking at a device from outside. Off until a page turns it
/// on, and then read by the page once per frame; nothing here is part of the platform's API.
/// </summary>
internal static partial class BrowserFrameDiagnostics
{
    private static readonly double _ticksToMs = 1000.0 / Stopwatch.Frequency;

    internal static bool Enabled { get; private set; }

    // One frame's values, in the order TakeFrame returns them.
    private static double _dispatcherMs;
    private static double _flingMs;
    private static double _pulseMs;
    private static double _layoutMs;
    private static double _renderMs;
    private static double _visited;
    private static double _recorded;
    private static double _replayed;
    private static double _liveFallbacks;
    private static double _wholeFrame;
    private static double _dirtyArea;
    private static double _flingStepDip;
    private static double _gen0;
    private static double _gen1;
    private static double _gen2;
    private static double _allocatedKb;

    private static int _gen0AtStart;
    private static int _gen1AtStart;
    private static int _gen2AtStart;
    private static long _allocatedAtStart;

    /// <summary>Starts collecting; the page calls it once when it wants the numbers.</summary>
    [JSExport]
    [SupportedOSPlatform("browser")]
    internal static void Enable() => Enabled = true;

    /// <summary>
    /// The last frame's values, then clears them: dispatcher, fling, pulse, layout and render time in
    /// milliseconds; visuals visited, recorded and replayed; live fallbacks; whole frame (1 or 0);
    /// repainted area in device pixels; fling step in DIPs; gen 0, 1 and 2 collections; KB allocated.
    /// </summary>
    [JSExport]
    [SupportedOSPlatform("browser")]
    internal static double[] TakeFrame()
    {
        double[] values =
        [
            _dispatcherMs, _flingMs, _pulseMs, _layoutMs, _renderMs,
            _visited, _recorded, _replayed, _liveFallbacks, _wholeFrame, _dirtyArea,
            _flingStepDip, _gen0, _gen1, _gen2, _allocatedKb,
        ];
        _dispatcherMs = _flingMs = _pulseMs = _layoutMs = _renderMs = 0;
        _visited = _recorded = _replayed = _liveFallbacks = _wholeFrame = _dirtyArea = 0;
        _flingStepDip = _gen0 = _gen1 = _gen2 = _allocatedKb = 0;
        return values;
    }

    internal static long Now() => Stopwatch.GetTimestamp();

    internal static double Since(long start) => (Stopwatch.GetTimestamp() - start) * _ticksToMs;

    internal static void BeginFrame()
    {
        _gen0AtStart = GC.CollectionCount(0);
        _gen1AtStart = GC.CollectionCount(1);
        _gen2AtStart = GC.CollectionCount(2);
        _allocatedAtStart = GC.GetAllocatedBytesForCurrentThread();
    }

    internal static void EndFrame()
    {
        _gen0 = GC.CollectionCount(0) - _gen0AtStart;
        _gen1 = GC.CollectionCount(1) - _gen1AtStart;
        _gen2 = GC.CollectionCount(2) - _gen2AtStart;
        _allocatedKb = (GC.GetAllocatedBytesForCurrentThread() - _allocatedAtStart) / 1024.0;
    }

    internal static void NoteDispatcher(double ms) => _dispatcherMs += ms;

    internal static void NoteFling(double ms) => _flingMs += ms;

    internal static void NotePulse(double ms) => _pulseMs += ms;

    internal static void NoteLayout(double ms) => _layoutMs += ms;

    internal static void NoteRender(double ms) => _renderMs += ms;

    internal static void NoteFlingStep(double stepDip) => _flingStepDip += stepDip;

    internal static void NoteScene(int visited, int recorded, int replayed, int liveFallbacks, Rect? dirtyRect, double dpiScale)
    {
        _visited += visited;
        _recorded += recorded;
        _replayed += replayed;
        _liveFallbacks += liveFallbacks;
        if (dirtyRect is Rect rect)
        {
            _dirtyArea += rect.Width * rect.Height * dpiScale * dpiScale;
        }
        else
        {
            _wholeFrame = 1;
        }
    }
}
