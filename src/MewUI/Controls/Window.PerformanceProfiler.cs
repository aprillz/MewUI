using Aprillz.MewUI.Controls;
using Aprillz.MewUI.Diagnostics;
using Aprillz.MewUI.Rendering;


namespace Aprillz.MewUI;

partial class Window
{
#if DEBUG
    private Adorner? _performanceAdorner;
    private DebugPerformanceOverlay? _performanceOverlay;
    private DebugProfilerWindow? _profilerWindow;

    public bool PerformanceMonitorIsOpen => _performanceAdorner != null;

    public bool ProfilerIsOpen => _profilerWindow != null;

    public event Action<bool>? PerformanceMonitorOpenChanged;

    public event Action<bool>? ProfilerOpenChanged;

    /// <summary>
    /// Registers profiler-related key bindings (Ctrl/Cmd+Shift+P for the perf overlay,
    /// Ctrl/Cmd+Shift+Alt+P for the timeline window). Called from <see cref="Window"/>'s
    /// constructor alongside the inspector/visual-tree DevTools bindings; kept separate
    /// from <c>InitializeDebugDevTools</c> so profiler concerns live wholly in this file.
    /// </summary>
    private void InitializeDebugPerformanceProfiler()
    {
        KeyBindings.Add(new KeyBinding(new KeyGesture(Key.P, ModifierKeys.Primary | ModifierKeys.Shift), TogglePerformanceMonitor));
        KeyBindings.Add(new KeyBinding(new KeyGesture(Key.P, ModifierKeys.Primary | ModifierKeys.Shift | ModifierKeys.Alt), ToggleProfiler));
    }

    private void UpdateProfilerEnabled()
    {
        PerformanceProfiler.Instance.IsEnabled = _performanceAdorner != null || _profilerWindow != null;
    }

    public void TogglePerformanceMonitor()
    {
        if (_performanceAdorner != null)
        {
            AdornerLayer.Remove(_performanceAdorner);
            _performanceAdorner = null;
            _performanceOverlay = null;
            UpdateProfilerEnabled();
            RequestLayout();
            RequestRender();
            PerformanceMonitorOpenChanged?.Invoke(false);
            return;
        }

        _performanceOverlay = new DebugPerformanceOverlay(this)
        {
            IsHitTestVisible = false,
            IsVisible = true,
        };

        _performanceAdorner = new Adorner(this, _performanceOverlay)
        {
            IsHitTestVisible = false,
            IsVisible = true,
        };

        AdornerLayer.Add(_performanceAdorner);
        UpdateProfilerEnabled();
        RequestRender();
        PerformanceMonitorOpenChanged?.Invoke(true);
    }

    public void ToggleProfiler()
    {
        if (_profilerWindow != null)
        {
            try
            {
                _profilerWindow.Close();
            }
            catch { }
            _profilerWindow = null;
            UpdateProfilerEnabled();
            ProfilerOpenChanged?.Invoke(false);
            return;
        }

        var profilerWindow = new DebugProfilerWindow(this);
        _profilerWindow = profilerWindow;
        UpdateProfilerEnabled();

        profilerWindow.Closed += () =>
        {
            if (ReferenceEquals(_profilerWindow, profilerWindow))
            {
                _profilerWindow = null;
                UpdateProfilerEnabled();
                ProfilerOpenChanged?.Invoke(false);
            }
        };

        Closed += CloseProfilerOnOwnerClose;
        void CloseProfilerOnOwnerClose()
        {
            Closed -= CloseProfilerOnOwnerClose;
            try { _profilerWindow?.Close(); } catch { }
            _profilerWindow = null;
            UpdateProfilerEnabled();
            ProfilerOpenChanged?.Invoke(false);
        }

        profilerWindow.Show();
        ProfilerOpenChanged?.Invoke(true);
    }
#endif

    private sealed class DebugPerformanceOverlay : Control
    {
        private readonly Window _window;

        public DebugPerformanceOverlay(Window window)
        {
            _window = window;
            Background = Color.Transparent;
        }

        public override bool Focusable => false;

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
                AppendRenderLoopMode(ref text, loop.Mode);
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

            var font = GetFont();
            const double maxWidth = 380;
            const double pad = 8;
            var size = context.MeasureText(text.WrittenSpan, font, maxWidth);
            var x = Math.Max(Bounds.X + 8, Bounds.Right - size.Width - pad * 2 - 8);
            var panelRect = new Rect(x, Bounds.Y + 8, size.Width + pad * 2, size.Height + pad * 2);
            panelRect = LayoutRounding.SnapBoundsRectToPixels(panelRect, context.DpiScale);

            context.FillRoundedRectangle(panelRect, 6, 6, Color.FromArgb(205, 18, 18, 18));
            context.DrawRoundedRectangle(panelRect, 6, 6, Color.FromArgb(230, 210, 180, 0), 1, strokeInset: true);
            context.DrawText(text.WrittenSpan, panelRect.Deflate(new Thickness(pad)), font, Color.White, TextAlignment.Left, TextAlignment.Top, TextWrapping.Wrap);
        }
    }

    private sealed class DebugProfilerWindow : Window
    {
        private readonly Window _target;
        private readonly ProfilerSurface _surface;

        public DebugProfilerWindow(Window target)
        {
            ExcludeFromProfiler = true;
            _target = target;
            Title = "MewUI Profiler";
            WindowSize = WindowSize.Resizable(980, 680);

            _surface = new ProfilerSurface(_target);

            var liveHint = new TextBlock
            {
                Text = "Space: Live/Pause",
                VerticalTextAlignment = TextAlignment.Center,
            };

            Content = new DockPanel()
                .Children(
                    new StackPanel()
                        .DockTop()
                        .Horizontal()
                        .Spacing(12)
                        .Padding(8, 4)
                        .Children(liveHint),
                    _surface);

            _target.FrameRendered += OnTargetFrameRendered;
            Closed += () => _target.FrameRendered -= OnTargetFrameRendered;

            PreviewKeyDown += e =>
            {
                if (e.Key == Key.Space)
                {
                    _surface.IsLive = !_surface.IsLive;
                    _surface.InvalidateVisual();
                    e.Handled = true;
                }
            };
        }

        private void OnTargetFrameRendered()
        {
            if (_surface.IsLive)
            {
                _surface.InvalidateVisual();
            }
        }
    }

    private sealed class ProfilerSurface : Control
    {
        private readonly Window _target;
        private readonly long _sourceId;
        private readonly FramePerformanceStats[] _frames = new FramePerformanceStats[512];
        private readonly double[] _childTicks = new double[4096];
        private readonly List<AggregateRow> _aggregateRows = new();
        private FrameProfilerData? _selectedFrame;
        private Rect _lastChartRect;
        private Rect _lastTimelineRect;
        private Rect _lastDetailsRect;
        private int _selectedFrameIndex = -1;
        private long _selectedFrameId = -1;
        private int _selectedSampleIndex = -1;
        private int _hoveredSampleIndex = -1;
        private Point _lastMousePoint;
        private double _timelineViewStartMs;
        private double _timelineViewDurationMs = double.NaN;
        private int _detailsScrollRow;

        public bool IsLive { get; set; } = true;

        public ProfilerSurface(Window target)
        {
            FontSize = 11;
            _target = target;
            _sourceId = target.ProfilerSourceId;
            Background = Color.FromArgb(255, 32, 32, 32);
        }

        protected override void OnRender(IGraphicsContext context)
        {
            base.OnRender(context);

            var bounds = Bounds;
            context.FillRectangle(bounds, Color.FromArgb(255, 32, 32, 32));

            var header = new Rect(bounds.X, bounds.Y, bounds.Width, 28);
            var chart = new Rect(bounds.X, header.Bottom, bounds.Width, Math.Max(120, bounds.Height * 0.36));
            var timeline = new Rect(bounds.X, chart.Bottom + 1, bounds.Width, Math.Max(160, bounds.Height * 0.38));
            var details = new Rect(bounds.X, timeline.Bottom + 1, bounds.Width, Math.Max(0, bounds.Bottom - timeline.Bottom - 1));

            DrawHeader(context, header);
            DrawCpuChart(context, chart);
            DrawTimeline(context, timeline);
            DrawDetails(context, details);
            DrawTimelineToolTip(context);
        }

        private void DrawHeader(IGraphicsContext context, Rect rect)
        {
            context.FillRectangle(rect, Color.FromArgb(255, 45, 45, 45));
            var stats = PerformanceProfiler.Instance.GetLatestFrame(_sourceId);
            Span<char> buffer = stackalloc char[128];
            var text = new StackTextFormatter(buffer);
            text.Append(IsLive ? "Live" : "Paused");
            text.Append("   CPU ");
            text.Append(stats.FrameMs, "0.00");
            text.Append("ms   GPU --ms   Frame ");
            text.Append(stats.FrameIndex);
            text.Append("   Space: pause/live");
            context.DrawText(text.WrittenSpan, rect.Deflate(new Thickness(8, 0)), GetFont(), Color.White, TextAlignment.Left, TextAlignment.Center, TextWrapping.NoWrap);
        }

        private void DrawCpuChart(IGraphicsContext context, Rect rect)
        {
            _lastChartRect = rect;
            context.FillRectangle(rect, Color.FromArgb(255, 38, 38, 38));
            int count = PerformanceProfiler.Instance.CopyFrames(_frames, _sourceId);
            if (count == 0)
            {
                DrawEmpty(context, rect, "No profiler frames yet");
                return;
            }

            double maxMs = 50;
            for (int i = 0; i < count; i++)
            {
                maxMs = Math.Max(maxMs, _frames[i].FrameMs);
            }

            DrawGuide(context, rect, 16.67, maxMs, "16ms (60FPS)");
            DrawGuide(context, rect, 33.33, maxMs, "33ms (30FPS)");

            double barW = Math.Max(1, rect.Width / count);
            for (int i = 0; i < count; i++)
            {
                var f = _frames[i];
                double x = rect.X + i * barW;
                double bottom = rect.Bottom;
                DrawStack(context, x, barW, ref bottom, rect, maxMs, f.AnimationMs, ProfilerSampleCategory.Animation);
                DrawStack(context, x, barW, ref bottom, rect, maxMs, f.LayoutMs, ProfilerSampleCategory.Layout);
                DrawStack(context, x, barW, ref bottom, rect, maxMs, f.RenderBodyMs, ProfilerSampleCategory.Render);
                DrawStack(context, x, barW, ref bottom, rect, maxMs, f.BeginFrameMs, ProfilerSampleCategory.Backend);
                DrawStack(context, x, barW, ref bottom, rect, maxMs, f.DevToolsMs, ProfilerSampleCategory.DevTools);
                DrawStack(context, x, barW, ref bottom, rect, maxMs, f.EndFrameMs, ProfilerSampleCategory.Backend);
                DrawStack(context, x, barW, ref bottom, rect, maxMs, f.PresentMs, ProfilerSampleCategory.VSyncWait);
                if (f.Gen0Collections + f.Gen1Collections + f.Gen2Collections > 0)
                {
                    DrawStack(context, x, barW, ref bottom, rect, maxMs, Math.Max(0.2, f.FrameMs * 0.03), ProfilerSampleCategory.GC);
                }
                double other = Math.Max(0, f.FrameMs - f.AnimationMs - f.LayoutMs - f.RenderBodyMs - f.DevToolsMs - f.BeginFrameMs - f.EndFrameMs - f.PresentMs);
                DrawStack(context, x, barW, ref bottom, rect, maxMs, other, ProfilerSampleCategory.Other);
            }

            _selectedFrameIndex = -1;
            if (_selectedFrameId >= 0)
            {
                for (int i = 0; i < count; i++)
                {
                    if (_frames[i].FrameIndex == _selectedFrameId)
                    {
                        _selectedFrameIndex = i;
                        break;
                    }
                }
            }

            if (_selectedFrameIndex >= 0 && _selectedFrameIndex < count)
            {
                double x = rect.X + _selectedFrameIndex * barW + barW * 0.5;
                context.DrawLine(new Point(x, rect.Y), new Point(x, rect.Bottom), Color.White, 2);
            }

            context.DrawRectangle(rect, Color.FromArgb(255, 74, 74, 74), 1, strokeInset: true);
        }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            base.OnMouseDown(e);

            if (e.Button != MouseButton.Left)
            {
                return;
            }

            var point = ToRenderPoint(e.GetPosition(this));
            if (!_lastChartRect.Contains(point))
            {
                if (_lastTimelineRect.Contains(point) && TryFindTimelineSample(point, out int sampleIndex, out var sample))
                {
                    _selectedSampleIndex = sampleIndex;
                    TryHighlightSampleTarget(sample);
                    IsLive = false;
                    e.Handled = true;
                    InvalidateVisual();
                }
                return;
            }

            int count = PerformanceProfiler.Instance.CopyFrames(_frames, _sourceId);
            if (count == 0)
            {
                return;
            }

            double relative = Math.Clamp((point.X - _lastChartRect.X) / Math.Max(1, _lastChartRect.Width), 0, 0.999999);
            int index = Math.Clamp((int)(relative * count), 0, count - 1);
            _selectedFrameIndex = index;
            _selectedFrameId = _frames[index].FrameIndex;
            _selectedFrame = PerformanceProfiler.Instance.GetTimelineFrame(_sourceId, _selectedFrameId);
            _selectedSampleIndex = -1;
            ResetTimelineZoom();
            _detailsScrollRow = 0;
            IsLive = false;
            e.Handled = true;
            InvalidateVisual();
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);

            var point = ToRenderPoint(e.GetPosition(this));
            _lastMousePoint = point;

            int oldHoveredSample = _hoveredSampleIndex;
            _hoveredSampleIndex = -1;
            if (_lastTimelineRect.Contains(point) && TryFindTimelineSample(point, out int sampleIndex, out _))
            {
                _hoveredSampleIndex = sampleIndex;
            }

            if (oldHoveredSample != _hoveredSampleIndex)
            {
                InvalidateVisual();
            }
        }

        protected override void OnMouseWheel(MouseWheelEventArgs e)
        {
            base.OnMouseWheel(e);

            var point = ToRenderPoint(e.GetPosition(this));
            if (_lastTimelineRect.Contains(point))
            {
                ZoomTimeline(point, e.Delta > 0);
                e.Handled = true;
                InvalidateVisual();
                return;
            }

            if (!_lastDetailsRect.Contains(point))
            {
                return;
            }

            int direction = e.Delta > 0 ? -1 : 1;
            _detailsScrollRow = Math.Max(0, _detailsScrollRow + direction * 3);
            e.Handled = true;
            InvalidateVisual();
        }

        private Point ToRenderPoint(Point localPoint)
            => new(localPoint.X + Bounds.X, localPoint.Y + Bounds.Y);

        private static void DrawStack(IGraphicsContext context, double x, double width, ref double bottom, Rect chart, double maxMs, double ms, ProfilerSampleCategory category)
        {
            if (ms <= 0)
            {
                return;
            }

            double h = Math.Max(1, ms / maxMs * chart.Height);
            var color = ProfilerMarkerRegistry.GetCategoryColor(category);
            context.FillRectangle(new Rect(x, Math.Max(chart.Y, bottom - h), Math.Max(1, width - 1), h), color);
            bottom -= h;
        }

        private void DrawTimeline(IGraphicsContext context, Rect rect)
        {
            _lastTimelineRect = rect;
            context.FillRectangle(rect, Color.FromArgb(255, 30, 30, 30));
            var frame = IsLive ? PerformanceProfiler.Instance.GetLatestTimelineFrame(_sourceId) : _selectedFrame ?? PerformanceProfiler.Instance.GetLatestTimelineFrame(_sourceId);
            _selectedFrame = frame;
            if (IsLive && frame != null)
            {
                if (_selectedFrameId != frame.Stats.FrameIndex)
                {
                    _selectedSampleIndex = -1;
                }
                _selectedFrameId = frame.Stats.FrameIndex;
            }

            if (frame == null || frame.SampleCount == 0)
            {
                DrawEmpty(context, rect, "No selected frame samples");
                return;
            }

            var font = GetFont();
            var labelRect = new Rect(rect.X, rect.Y, 140, rect.Height);
            context.FillRectangle(labelRect, Color.FromArgb(255, 50, 50, 50));
            context.DrawText("Main Thread", new Rect(labelRect.X + 8, labelRect.Y, labelRect.Width - 16, 28), font, Color.White, TextAlignment.Left, TextAlignment.Center, TextWrapping.NoWrap);

            var lane = new Rect(labelRect.Right, rect.Y, Math.Max(0, rect.Width - labelRect.Width), rect.Height);
            double frameMs = Math.Max(1, frame.Stats.FrameMs);
            long frameStart = frame.Samples[0].StartTimestamp;
            double rowH = 18;
            EnsureTimelineZoom(frameMs);

            for (int i = 0; i < frame.SampleCount; i++)
            {
                var sample = frame.Samples[i];
                double startMs = FrameTimingBuilder.ToMilliseconds(sample.StartTimestamp - frameStart);
                double durMs = FrameTimingBuilder.ToMilliseconds(sample.EndTimestamp - sample.StartTimestamp);
                if (startMs + durMs < _timelineViewStartMs || startMs > _timelineViewStartMs + _timelineViewDurationMs)
                {
                    continue;
                }

                double x = lane.X + (startMs - _timelineViewStartMs) / _timelineViewDurationMs * lane.Width;
                double w = Math.Max(1, durMs / _timelineViewDurationMs * lane.Width);
                double clippedX = Math.Max(lane.X, x);
                double clippedRight = Math.Min(lane.Right, x + w);
                if (clippedRight <= clippedX)
                {
                    continue;
                }

                double y = lane.Y + 6 + sample.Depth * rowH;
                if (y + rowH > lane.Bottom)
                {
                    continue;
                }

                var info = ProfilerMarkerRegistry.GetInfo(sample.MarkerId);
                var block = new Rect(clippedX, y, clippedRight - clippedX, rowH - 2);
                context.FillRectangle(block, info.Color);
                if (i == _selectedSampleIndex)
                {
                    context.DrawRectangle(block, Color.White, 1, strokeInset: true);
                }
                else if (i == _hoveredSampleIndex)
                {
                    context.DrawRectangle(block, Color.FromArgb(255, 255, 230, 120), 1, strokeInset: true);
                }
                if (w > 44)
                {
                    context.DrawText(info.Name, block.Deflate(new Thickness(3, 0)), font, Color.White, TextAlignment.Left, TextAlignment.Center, TextWrapping.NoWrap);
                }
            }

            context.DrawRectangle(rect, Color.FromArgb(255, 74, 74, 74), 1, strokeInset: true);
        }

        private void DrawTimelineToolTip(IGraphicsContext context)
        {
            if (_hoveredSampleIndex < 0)
            {
                return;
            }

            var frame = IsLive ? PerformanceProfiler.Instance.GetLatestTimelineFrame(_sourceId) : _selectedFrame ?? PerformanceProfiler.Instance.GetLatestTimelineFrame(_sourceId);
            if (frame == null || (uint)_hoveredSampleIndex >= (uint)frame.SampleCount)
            {
                return;
            }

            var sample = frame.Samples[_hoveredSampleIndex];
            var info = ProfilerMarkerRegistry.GetInfo(sample.MarkerId);
            double totalMs = FrameTimingBuilder.ToMilliseconds(sample.EndTimestamp - sample.StartTimestamp);
            double selfMs = totalMs;
            for (int i = 0; i < frame.SampleCount; i++)
            {
                if (frame.Samples[i].ParentIndex == _hoveredSampleIndex)
                {
                    selfMs -= FrameTimingBuilder.ToMilliseconds(frame.Samples[i].EndTimestamp - frame.Samples[i].StartTimestamp);
                }
            }

            selfMs = Math.Max(0, selfMs);
            double startMs = FrameTimingBuilder.ToMilliseconds(sample.StartTimestamp - frame.Samples[0].StartTimestamp);

            Span<char> buffer = stackalloc char[384];
            var text = new StackTextFormatter(buffer);
            text.Append(info.Name);
            text.Append("\nTotal ");
            text.Append(totalMs, "0.000");
            text.Append("ms  Self ");
            text.Append(selfMs, "0.000");
            text.Append("ms\nStart ");
            text.Append(startMs, "0.000");
            text.Append("ms  Category ");
            AppendSampleCategory(ref text, sample.Category);
            text.Append("\nDepth ");
            text.Append(sample.Depth);
            text.Append("  Calls 1");

            if (sample.Target is { } element)
            {
                text.Append("\nElement ");
                text.Append(element.GetType().Name);
                text.Append("  ");
                AppendRect(ref text, element.Bounds);
            }
            else
            {
                text.Append("\nElement (none)");
            }

            var font = GetFont();
            const double pad = 6;
            const double maxWidth = 360;
            var size = context.MeasureText(text.WrittenSpan, font, maxWidth);
            double x = _lastMousePoint.X + 14;
            double y = _lastMousePoint.Y + 16;
            double w = Math.Min(maxWidth, size.Width) + pad * 2;
            double h = size.Height + pad * 2;

            if (x + w > Bounds.Right - 4)
            {
                x = Math.Max(Bounds.X + 4, _lastMousePoint.X - w - 14);
            }
            if (y + h > Bounds.Bottom - 4)
            {
                y = Math.Max(Bounds.Y + 4, _lastMousePoint.Y - h - 14);
            }

            var rect = LayoutRounding.SnapBoundsRectToPixels(new Rect(x, y, w, h), context.DpiScale);
            context.FillRoundedRectangle(rect, 4, 4, Color.FromArgb(235, 18, 18, 18));
            context.DrawRoundedRectangle(rect, 4, 4, Color.FromArgb(255, 220, 220, 220), 1, strokeInset: true);
            context.DrawText(text.WrittenSpan, rect.Deflate(new Thickness(pad)), font, Color.White, TextAlignment.Left, TextAlignment.Top, TextWrapping.Wrap);
        }

        private bool TryFindTimelineSample(Point point, out int sampleIndex, out ProfilerSample sample)
        {
            sampleIndex = -1;
            sample = default;
            var frame = IsLive ? PerformanceProfiler.Instance.GetLatestTimelineFrame(_sourceId) : _selectedFrame ?? PerformanceProfiler.Instance.GetLatestTimelineFrame(_sourceId);
            if (frame == null || frame.SampleCount == 0)
            {
                return false;
            }

            var rect = _lastTimelineRect;
            var labelRect = new Rect(rect.X, rect.Y, 140, rect.Height);
            var lane = new Rect(labelRect.Right, rect.Y, Math.Max(0, rect.Width - labelRect.Width), rect.Height);
            if (!lane.Contains(point))
            {
                return false;
            }

            double frameMs = Math.Max(1, frame.Stats.FrameMs);
            long frameStart = frame.Samples[0].StartTimestamp;
            double rowH = 18;
            EnsureTimelineZoom(frameMs);

            for (int i = frame.SampleCount - 1; i >= 0; i--)
            {
                var candidate = frame.Samples[i];
                double startMs = FrameTimingBuilder.ToMilliseconds(candidate.StartTimestamp - frameStart);
                double durMs = FrameTimingBuilder.ToMilliseconds(candidate.EndTimestamp - candidate.StartTimestamp);
                if (startMs + durMs < _timelineViewStartMs || startMs > _timelineViewStartMs + _timelineViewDurationMs)
                {
                    continue;
                }

                double x = lane.X + (startMs - _timelineViewStartMs) / _timelineViewDurationMs * lane.Width;
                double w = Math.Max(1, durMs / _timelineViewDurationMs * lane.Width);
                double y = lane.Y + 6 + candidate.Depth * rowH;
                if (y + rowH > lane.Bottom)
                {
                    continue;
                }

                var block = new Rect(x, y, w, rowH - 2);
                if (block.Contains(point))
                {
                    sampleIndex = i;
                    sample = candidate;
                    _selectedFrame = frame;
                    _selectedFrameId = frame.Stats.FrameIndex;
                    return true;
                }
            }

            return false;
        }

        private void ZoomTimeline(Point point, bool zoomIn)
        {
            var frame = IsLive ? PerformanceProfiler.Instance.GetLatestTimelineFrame(_sourceId) : _selectedFrame ?? PerformanceProfiler.Instance.GetLatestTimelineFrame(_sourceId);
            if (frame == null || frame.SampleCount == 0)
            {
                return;
            }

            var rect = _lastTimelineRect;
            var labelRect = new Rect(rect.X, rect.Y, 140, rect.Height);
            var lane = new Rect(labelRect.Right, rect.Y, Math.Max(0, rect.Width - labelRect.Width), rect.Height);
            if (!lane.Contains(point))
            {
                return;
            }

            double frameMs = Math.Max(1, frame.Stats.FrameMs);
            EnsureTimelineZoom(frameMs);

            double anchorRatio = Math.Clamp((point.X - lane.X) / Math.Max(1, lane.Width), 0, 1);
            double anchorMs = _timelineViewStartMs + anchorRatio * _timelineViewDurationMs;
            double scale = zoomIn ? 0.8 : 1.25;
            double newDuration = Math.Clamp(_timelineViewDurationMs * scale, 0.05, frameMs);
            double newStart = anchorMs - anchorRatio * newDuration;
            _timelineViewStartMs = Math.Clamp(newStart, 0, Math.Max(0, frameMs - newDuration));
            _timelineViewDurationMs = newDuration;

            if (Math.Abs(_timelineViewDurationMs - frameMs) < 0.001)
            {
                ResetTimelineZoom();
            }
        }

        private void EnsureTimelineZoom(double frameMs)
        {
            if (double.IsNaN(_timelineViewDurationMs) || _timelineViewDurationMs <= 0 || _timelineViewDurationMs > frameMs)
            {
                _timelineViewStartMs = 0;
                _timelineViewDurationMs = frameMs;
                return;
            }

            _timelineViewStartMs = Math.Clamp(_timelineViewStartMs, 0, Math.Max(0, frameMs - _timelineViewDurationMs));
        }

        private void ResetTimelineZoom()
        {
            _timelineViewStartMs = 0;
            _timelineViewDurationMs = double.NaN;
        }

        private bool TryHighlightSampleTarget(ProfilerSample sample)
        {
#if DEBUG
            if (sample.Target is not { } element)
            {
#endif
            return false;
#if DEBUG
            }

            if (_target._debugInspectorOverlay == null)
            {
                _target.ToggleDebugInspector();
            }

            if (_target._debugInspectorOverlay == null)
            {
                return false;
            }

            _target._debugInspectorOverlay.HighlightedElement = element;
            _target.RequestRender();
            return true;
#endif
        }

        private void DrawDetails(IGraphicsContext context, Rect rect)
        {
            _lastDetailsRect = rect;
            context.FillRectangle(rect, Color.FromArgb(255, 36, 36, 36));
            var frame = _selectedFrame;
            if (frame == null)
            {
                return;
            }

            var font = GetFont();
            var prim = frame.Stats.PrimitiveStats;
            Span<char> buffer = stackalloc char[512];
            var text = new StackTextFormatter(buffer);
            text.Append("Frame ");
            text.Append(frame.Stats.FrameIndex);
            text.Append("  Samples ");
            text.Append(frame.SampleCount);
            text.Append("  Overflow ");
            text.Append(frame.SampleOverflowCount);
            text.Append("  Dev ");
            text.Append(frame.Stats.DevToolsMs, "0.00");
            text.Append("ms  Present ");
            text.Append(frame.Stats.PresentMs, "0.00");
            text.Append("ms  Draw ");
            text.Append(frame.Stats.DrawCalls);
            text.Append("  Cull ");
            text.Append(frame.Stats.CullCount);
            text.Append("  Shape ");
            text.Append(prim.ShapeCount);
            text.Append("  Text ");
            text.Append(prim.DrawTextCount);
            text.Append("  Img ");
            text.Append(prim.DrawImageCount);
            text.Append("  Clip ");
            text.Append(prim.ClipCount);
            text.Append("  Alloc ");
            text.AppendBytes(frame.Stats.AllocatedBytes);
            text.Append("  GC ");
            text.Append(frame.Stats.Gen0Collections);
            text.Append('/');
            text.Append(frame.Stats.Gen1Collections);
            text.Append('/');
            text.Append(frame.Stats.Gen2Collections);
            context.DrawText(text.WrittenSpan, new Rect(rect.X + 8, rect.Y + 4, rect.Width - 16, 20), font, Color.White, TextAlignment.Left, TextAlignment.Center, TextWrapping.NoWrap);

            var header = new Rect(rect.X + 8, rect.Y + 28, rect.Width - 16, 18);
            context.DrawText("Name", new Rect(header.X, header.Y, Math.Max(0, header.Width - 220), header.Height), font, Color.FromArgb(255, 210, 210, 210), TextAlignment.Left, TextAlignment.Center, TextWrapping.NoWrap);
            context.DrawText("Total", new Rect(header.Right - 210, header.Y, 70, header.Height), font, Color.FromArgb(255, 210, 210, 210), TextAlignment.Right, TextAlignment.Center, TextWrapping.NoWrap);
            context.DrawText("Self", new Rect(header.Right - 135, header.Y, 70, header.Height), font, Color.FromArgb(255, 210, 210, 210), TextAlignment.Right, TextAlignment.Center, TextWrapping.NoWrap);
            context.DrawText("Calls", new Rect(header.Right - 58, header.Y, 58, header.Height), font, Color.FromArgb(255, 210, 210, 210), TextAlignment.Right, TextAlignment.Center, TextWrapping.NoWrap);

            DrawAggregateRows(context, rect, frame, font);
        }

        private void DrawAggregateRows(IGraphicsContext context, Rect rect, FrameProfilerData frame, IFont font)
        {
            int sampleCount = Math.Min(frame.SampleCount, _childTicks.Length);
            Array.Clear(_childTicks, 0, sampleCount);
            for (int i = 0; i < sampleCount; i++)
            {
                int parent = frame.Samples[i].ParentIndex;
                if ((uint)parent < (uint)sampleCount)
                {
                    _childTicks[parent] += frame.Samples[i].EndTimestamp - frame.Samples[i].StartTimestamp;
                }
            }

            _aggregateRows.Clear();
            for (int i = 0; i < sampleCount; i++)
            {
                var sample = frame.Samples[i];
                double totalTicks = sample.EndTimestamp - sample.StartTimestamp;
                double selfTicks = Math.Max(0, totalTicks - _childTicks[i]);
                int rowIndex = FindAggregateRow(sample.MarkerId);
                if (rowIndex < 0)
                {
                    _aggregateRows.Add(new AggregateRow(sample.MarkerId, sample.Category, totalTicks, selfTicks, 1));
                }
                else
                {
                    var row = _aggregateRows[rowIndex];
                    _aggregateRows[rowIndex] = row with
                    {
                        TotalTicks = row.TotalTicks + totalTicks,
                        SelfTicks = row.SelfTicks + selfTicks,
                        Calls = row.Calls + 1,
                    };
                }
            }

            _aggregateRows.Sort(static (a, b) => b.SelfTicks.CompareTo(a.SelfTicks));

            double y = rect.Y + 50;
            double rowH = 18;
            int maxRows = Math.Max(0, (int)((rect.Bottom - y - 4) / rowH));
            int maxScroll = Math.Max(0, _aggregateRows.Count - maxRows);
            _detailsScrollRow = Math.Min(_detailsScrollRow, maxScroll);
            int rows = Math.Min(_aggregateRows.Count - _detailsScrollRow, maxRows);
            Span<char> totalBuffer = stackalloc char[32];
            Span<char> selfBuffer = stackalloc char[32];
            Span<char> callsBuffer = stackalloc char[16];
            for (int i = 0; i < rows; i++)
            {
                int sourceIndex = i + _detailsScrollRow;
                var aggregate = _aggregateRows[sourceIndex];
                var info = ProfilerMarkerRegistry.GetInfo(aggregate.MarkerId);
                double totalMs = FrameTimingBuilder.ToMilliseconds((long)aggregate.TotalTicks);
                double selfMs = FrameTimingBuilder.ToMilliseconds((long)aggregate.SelfTicks);

                var row = new Rect(rect.X + 8, y + i * rowH, rect.Width - 16, rowH);
                if ((i & 1) == 0)
                {
                    context.FillRectangle(row, Color.FromArgb(255, 42, 42, 42));
                }

                context.FillRectangle(new Rect(row.X, row.Y + 4, 7, row.Height - 8), info.Color);
                context.DrawText(info.Name, new Rect(row.X + 12, row.Y, Math.Max(0, row.Width - 232), row.Height), font, Color.White, TextAlignment.Left, TextAlignment.Center, TextWrapping.NoWrap);
                var totalText = new StackTextFormatter(totalBuffer);
                totalText.Append(totalMs, "0.00");
                context.DrawText(totalText.WrittenSpan, new Rect(row.Right - 210, row.Y, 70, row.Height), font, Color.White, TextAlignment.Right, TextAlignment.Center, TextWrapping.NoWrap);

                var selfText = new StackTextFormatter(selfBuffer);
                selfText.Append(selfMs, "0.00");
                context.DrawText(selfText.WrittenSpan, new Rect(row.Right - 135, row.Y, 70, row.Height), font, Color.White, TextAlignment.Right, TextAlignment.Center, TextWrapping.NoWrap);

                var callsText = new StackTextFormatter(callsBuffer);
                callsText.Append(aggregate.Calls);
                context.DrawText(callsText.WrittenSpan, new Rect(row.Right - 58, row.Y, 58, row.Height), font, Color.FromArgb(255, 210, 210, 210), TextAlignment.Right, TextAlignment.Center, TextWrapping.NoWrap);
            }

            if (_aggregateRows.Count > maxRows && maxRows > 0)
            {
                var track = new Rect(rect.Right - 6, y, 4, Math.Max(1, rect.Bottom - y - 4));
                context.FillRectangle(track, Color.FromArgb(120, 90, 90, 90));
                double thumbH = Math.Max(18, track.Height * maxRows / _aggregateRows.Count);
                double thumbY = track.Y + (track.Height - thumbH) * (_detailsScrollRow / (double)Math.Max(1, maxScroll));
                context.FillRectangle(new Rect(track.X, thumbY, track.Width, thumbH), Color.FromArgb(220, 180, 180, 180));
            }
        }

        private int FindAggregateRow(int markerId)
        {
            for (int i = 0; i < _aggregateRows.Count; i++)
            {
                if (_aggregateRows[i].MarkerId == markerId)
                {
                    return i;
                }
            }

            return -1;
        }

        private readonly record struct AggregateRow(int MarkerId, ProfilerSampleCategory Category, double TotalTicks, double SelfTicks, int Calls);

        private void DrawGuide(IGraphicsContext context, Rect rect, double ms, double maxMs, string label)
        {
            if (ms > maxMs)
            {
                return;
            }

            double y = rect.Bottom - ms / maxMs * rect.Height;
            context.DrawLine(new Point(rect.X, y), new Point(rect.Right, y), Color.FromArgb(180, 210, 210, 210), 1);
            double labelY = Math.Clamp(y - 18, rect.Y + 2, rect.Bottom - 18);
            context.DrawText(label, new Rect(rect.X + 6, labelY, 120, 16), GetFont(), Color.White, TextAlignment.Left, TextAlignment.Center, TextWrapping.NoWrap);
        }

        private void DrawEmpty(IGraphicsContext context, Rect rect, string text)
        {
            context.DrawText(text, rect, GetFont(), Color.FromArgb(255, 180, 180, 180), TextAlignment.Center, TextAlignment.Center, TextWrapping.NoWrap);
        }
    }

    private static void AppendRenderLoopMode(ref StackTextFormatter formatter, RenderLoopMode mode)
    {
        switch (mode)
        {
            case RenderLoopMode.OnRequest:
                formatter.Append("OnRequest");
                break;
            case RenderLoopMode.Continuous:
                formatter.Append("Continuous");
                break;
            default:
                formatter.Append((int)mode);
                break;
        }
    }

    private static void AppendRect(ref StackTextFormatter formatter, Rect rect)
    {
        formatter.Append('[');
        formatter.Append(rect.X, "0.#");
        formatter.Append(',');
        formatter.Append(rect.Y, "0.#");
        formatter.Append(' ');
        formatter.Append(rect.Width, "0.#");
        formatter.Append('x');
        formatter.Append(rect.Height, "0.#");
        formatter.Append(']');
    }

    private static void AppendSampleCategory(ref StackTextFormatter formatter, ProfilerSampleCategory category)
    {
        switch (category)
        {
            case ProfilerSampleCategory.Frame:
                formatter.Append("Frame");
                break;
            case ProfilerSampleCategory.Layout:
                formatter.Append("Layout");
                break;
            case ProfilerSampleCategory.Measure:
                formatter.Append("Measure");
                break;
            case ProfilerSampleCategory.Arrange:
                formatter.Append("Arrange");
                break;
            case ProfilerSampleCategory.Render:
                formatter.Append("Render");
                break;
            case ProfilerSampleCategory.Animation:
                formatter.Append("Animation");
                break;
            case ProfilerSampleCategory.Backend:
                formatter.Append("Backend");
                break;
            case ProfilerSampleCategory.VSyncWait:
                formatter.Append("VSyncWait");
                break;
            case ProfilerSampleCategory.DevTools:
                formatter.Append("DevTools");
                break;
            case ProfilerSampleCategory.GC:
                formatter.Append("GC");
                break;
            default:
                formatter.Append("Other");
                break;
        }
    }

    private ref struct StackTextFormatter
    {
        private Span<char> _buffer;
        private int _length;

        public StackTextFormatter(Span<char> buffer)
        {
            _buffer = buffer;
            _length = 0;
        }

        public readonly ReadOnlySpan<char> WrittenSpan => _buffer[.._length];

        public void Append(char value)
        {
            if (_length < _buffer.Length)
            {
                _buffer[_length++] = value;
            }
        }

        public void Append(ReadOnlySpan<char> value)
        {
            int count = Math.Min(value.Length, _buffer.Length - _length);
            if (count <= 0)
            {
                return;
            }

            value[..count].CopyTo(_buffer[_length..]);
            _length += count;
        }

        public void Append(bool value)
            => Append(value ? "True" : "False");

        public void Append(int value)
            => AppendFormattable(value);

        public void Append(long value)
            => AppendFormattable(value);

        public void Append(double value, ReadOnlySpan<char> format)
        {
            if (value.TryFormat(_buffer[_length..], out int written, format))
            {
                _length += written;
            }
        }

        public void AppendBytes(long bytes)
        {
            if (bytes < 1024)
            {
                Append(bytes);
                Append(" B");
            }
            else if (bytes < 1024 * 1024)
            {
                Append(bytes / 1024.0, "0.0");
                Append(" KB");
            }
            else
            {
                Append(bytes / (1024.0 * 1024.0), "0.0");
                Append(" MB");
            }
        }

        private void AppendFormattable<T>(T value)
            where T : ISpanFormattable
        {
            if (value.TryFormat(_buffer[_length..], out int written, default, null))
            {
                _length += written;
            }
        }
    }
}
