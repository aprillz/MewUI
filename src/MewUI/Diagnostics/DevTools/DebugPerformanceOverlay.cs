using Aprillz.MewUI.Controls;
using Aprillz.MewUI.Rendering;

namespace Aprillz.MewUI.Diagnostics;

internal sealed class DebugPerformanceOverlay : Control
{
    private readonly Window _window;

    public DebugPerformanceOverlay(Window window)
    {
        _window = window;
        Background = Color.Transparent;
    }

    protected override void OnRender(IGraphicsContext context)
    {
        base.OnRender(context);

        var profiler = PerformanceProfiler.Instance;
        var latest = profiler.GetLatestFrame(_window.ProfilerSourceId);
        var rolling = profiler.GetRollingStats(_window.ProfilerSourceId);
        var loop = Application.IsRunning ? Application.Current.RenderLoopSettings : null;
            
        Span<char> textBuffer = stackalloc char[768];
        var text = new StackTextFormatter(textBuffer);
        text.Append("FPS ");
        text.Append(rolling.Fps, "0.0");
        text.Append("  Frame ");
        text.Append(latest.FrameMs, "0.00");
        text.Append(" ms\nAvg ");
        text.Append(rolling.AverageFrameMs, "0.00");
        text.Append("  Min ");
        text.Append(rolling.MinFrameMs, "0.00");
        text.Append("  Max ");
        text.Append(rolling.MaxFrameMs, "0.00");
        text.Append("\nLayout ");
        text.Append(latest.LayoutMs, "0.00");
        text.Append("  Measure ");
        text.Append(latest.MeasureMs, "0.00");
        text.Append("  Arrange ");
        text.Append(latest.ArrangeMs, "0.00");
        text.Append("\nAnim ");
        text.Append(latest.AnimationMs, "0.00");
        text.Append("  Render ");
        text.Append(latest.RenderBodyMs, "0.00");
        text.Append("  Dev ");
        text.Append(latest.DevToolsMs, "0.00");
        text.Append("  End ");
        text.Append(latest.EndFrameMs, "0.00");
        text.Append("  Present ");
        text.Append(latest.PresentMs, "0.00");
        text.Append("\nDraw ");
        text.Append(latest.DrawCalls);
        text.Append("  Cull ");
        text.Append(latest.CullCount);
        text.Append("  Alloc ");
        text.AppendBytes(latest.AllocatedBytes);
        text.Append("  GC ");
        text.Append(latest.Gen0Collections);
        text.Append('/');
        text.Append(latest.Gen1Collections);
        text.Append('/');
        text.Append(latest.Gen2Collections);
        text.Append("\nPrim Shape ");
        text.Append(latest.PrimitiveStats.ShapeCount);
        text.Append("  Text ");
        text.Append(latest.PrimitiveStats.DrawTextCount);
        text.Append("  Img ");
        text.Append(latest.PrimitiveStats.DrawImageCount);
        text.Append("  Clip ");
        text.Append(latest.PrimitiveStats.ClipCount);
        text.Append('\n');
        if (loop != null)
        {
            text.Append("Loop ");
            if (loop.IsContinuous)
            {
                text.Append("Continuous");
                text.Append(loop.Continuous ? " user" : loop.AnimationActive ? " anim" : " vsync");
            }
            else
            {
                text.Append("OnRequest");
            }
            text.Append("  VSync ");
            text.Append(loop.VSyncEnabled);
            text.Append("  Target ");
            text.Append(loop.TargetFps);
        }
        else
        {
            text.Append("Loop (not running)");
        }
        text.Append("\nCtrl+Shift+Alt+P: Profiler");

        const double maxWidth = 380;
        const double pad = 8;
        // The numbers change every frame, so this text is transient: nothing it draws is cached.
        var size = MeasureEngineText(text.WrittenSpan, maxWidth, TextWrapping.Wrap, transient: true);
        var x = Math.Max(Bounds.X + 8, Bounds.Right - size.Width - pad * 2 - 8);
        var panelRect = new Rect(x, Bounds.Y + 8, size.Width + pad * 2, size.Height + pad * 2);
        panelRect = LayoutRounding.SnapBoundsRectToPixels(panelRect, context.DpiScale);

        context.FillRoundedRectangle(panelRect, 6, 6, Color.FromArgb(205, 18, 18, 18));
        context.DrawRoundedRectangle(panelRect, 6, 6, Color.FromArgb(230, 210, 180, 0), 1, strokeInset: true);
        DrawEngineText(
            context,
            text.WrittenSpan,
            panelRect.Deflate(new Thickness(pad)),
            Color.White,
            wrapping: TextWrapping.Wrap,
            transient: true);

        DrawResourcePanel(context, panelRect, maxWidth, pad);
    }

    // A second box under the frame panel: what the render caches hold and what the process
    // occupies, from RenderMemoryLedger.
    private void DrawResourcePanel(IGraphicsContext context, Rect framePanel, double maxWidth, double pad)
    {
        var memory = RenderMemoryLedger.Snapshot();

        Span<char> buffer = stackalloc char[768];
        var text = new StackTextFormatter(buffer);
        text.Append("Scratch active ");
        text.Append((int)memory.ScratchActiveCount);
        text.Append(" / ");
        text.AppendBytes(memory.ScratchActiveBytes);
        text.Append("  pooled ");
        text.Append((int)memory.ScratchPooledCount);
        text.Append(" / ");
        text.AppendBytes(memory.ScratchPooledBytes);
        text.Append("\nBitmapCache ");
        text.Append((int)memory.BitmapCacheCount);
        text.Append(" / ");
        text.AppendBytes(memory.BitmapCacheBytes);
        text.Append("  Vector ");
        text.Append((int)memory.VectorCacheCount);
        text.Append(" / ");
        text.AppendBytes(memory.VectorCacheBytes);
        text.Append("\nText ");
        text.AppendBytes(memory.TextCacheBytes);
        text.Append("  Geometry ");
        text.AppendBytes(memory.GeometryCacheBytes);
        text.Append("\nImages encoded ");
        text.Append((int)memory.EncodedBackingCount);
        text.Append(" / ");
        text.AppendBytes(memory.EncodedBackingBytes);
        text.Append("  decoded ");
        text.Append((int)memory.DecodedPixelCount);
        text.Append(" / ");
        text.AppendBytes(memory.DecodedPixelBytes);
        text.Append("\nImage decode ");
        text.Append((int)memory.DecodeSucceeded);
        text.Append("/");
        text.Append((int)memory.DecodeAttempts);
        text.Append("  realize ");
        text.Append((int)memory.ImageRealizationSucceeded);
        text.Append("/");
        text.Append((int)memory.ImageRealizationRequests);
        text.Append("\nPersistent ");
        text.Append((int)memory.PersistentResourceCount);
        text.Append(" / ");
        text.AppendBytes(memory.PersistentResourceBytes);
        text.Append("  pending ");
        text.Append((int)memory.PendingReleaseCount);
        text.Append(" / ");
        text.AppendBytes(memory.PendingReleaseBytes);
        text.Append("\nPrivate ");
        text.AppendBytes(memory.PrivateUsage);
        text.Append(" \nWS ");
        text.AppendBytes(memory.WorkingSetSize);
        text.Append(" \nPrivate WS ");
        text.AppendBytes(memory.PrivateWorkingSetSize);
        text.Append(" \nGC ");
        text.AppendBytes(memory.GcHeapBytes);
        if (!memory.IsBalanced)
        {
            text.Append("\nScratch ledger mismatch: created ");
            text.Append((int)memory.ScratchCreated);
            text.Append(" disposed ");
            text.Append((int)memory.ScratchDisposed);
        }

        var size = MeasureEngineText(text.WrittenSpan, maxWidth, TextWrapping.Wrap, transient: true);
        var x = Math.Max(Bounds.X + 8, Bounds.Right - size.Width - pad * 2 - 8);
        var panelRect = new Rect(x, framePanel.Bottom + 8, size.Width + pad * 2, size.Height + pad * 2);
        panelRect = LayoutRounding.SnapBoundsRectToPixels(panelRect, context.DpiScale);

        context.FillRoundedRectangle(panelRect, 6, 6, Color.FromArgb(205, 18, 18, 18));
        context.DrawRoundedRectangle(panelRect, 6, 6, Color.FromArgb(230, 90, 190, 230), 1, strokeInset: true);
        DrawEngineText(
            context,
            text.WrittenSpan,
            panelRect.Deflate(new Thickness(pad)),
            Color.White,
            wrapping: TextWrapping.Wrap,
            transient: true);
    }
}
