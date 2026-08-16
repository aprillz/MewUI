using Aprillz.MewUI.Controls;
using Aprillz.MewUI.Input;
using Aprillz.MewUI.Text;

namespace Aprillz.MewUI.Diagnostics;

internal sealed class DebugProfilerWindow : Window
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
