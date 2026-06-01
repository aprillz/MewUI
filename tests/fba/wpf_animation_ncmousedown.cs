#:sdk Microsoft.NET.Sdk

#:property OutputType=WinExe
#:property TargetFramework=net10.0-windows
#:property UseWPF=True
#:property UseWindowsForms=False

// WPF cannot use AOT compilation.
#:property PublishAot=False

// Reproduces the question: does WPF freeze animation rendering while the title
// bar is mouse-down but no movement occurs (DefWindowProc's pre-drag NC tracking
// state).
//
//   * Render FPS — frames per second observed from CompositionTarget.Rendering.
//   * The rotating square is animated via a DoubleAnimation on a RotateTransform.
//
// FPS is a rolling rate over the last second so a freeze decays to 0 quickly and
// resume jumps right back to the steady-state rate.
//
// To test:
//   * Watch Render FPS at steady state (typically ≈ 60).
//   * Press and hold the LMB on the title bar caption (do NOT move the mouse).
//   * Observe whether FPS decays to 0 and the square stops spinning.
//   * Releasing or moving the mouse should snap it back.

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;

Thread.CurrentThread.SetApartmentState(ApartmentState.Unknown);
Thread.CurrentThread.SetApartmentState(ApartmentState.STA);

var app = new Application();
app.Run(BuildWindow());

static Window BuildWindow()
{
    var renderFpsLabel = new TextBlock { FontSize = 22 };
    var instructions = new TextBlock
    {
        Text = "Press & hold LMB on the title bar (no movement).\nWatch Render FPS + rotating square.",
        FontSize = 12,
        Margin = new Thickness(0, 16, 0, 0),
        TextWrapping = TextWrapping.Wrap,
    };

    var rotate = new RotateTransform(0);
    var square = new Border
    {
        Width = 120,
        Height = 120,
        Background = new SolidColorBrush(Color.FromRgb(80, 160, 220)),
        RenderTransform = rotate,
        RenderTransformOrigin = new Point(0.5, 0.5),
        Margin = new Thickness(0, 24, 0, 0),
        HorizontalAlignment = HorizontalAlignment.Center,
    };

    var spin = new DoubleAnimation
    {
        From = 0,
        To = 360,
        Duration = TimeSpan.FromSeconds(2),
        RepeatBehavior = RepeatBehavior.Forever,
    };
    rotate.BeginAnimation(RotateTransform.AngleProperty, spin);

    var renderFps = new RollingFps(windowMs: 1000);

    // Force the FPS label to refresh on a separate cadence — otherwise the displayed
    // value only updates when its own source ticks, which would itself appear frozen.
    var displayTimer = new DispatcherTimer(DispatcherPriority.Render)
    {
        Interval = TimeSpan.FromMilliseconds(500),
    };
    displayTimer.Tick += (_, _) =>
    {
        renderFpsLabel.Text = $"Render FPS: {renderFps.Current}";
    };
    displayTimer.Start();

    CompositionTarget.Rendering += (_, _) => renderFps.Mark();

    var stack = new StackPanel
    {
        Margin = new Thickness(24),
        Children = { renderFpsLabel, square, instructions },
    };

    return new Window
    {
        Title = "WPF Animation vs NC MouseDown Repro",
        Width = 520,
        Height = 440,
        WindowStartupLocation = WindowStartupLocation.CenterScreen,
        Content = stack,
    };
}

// Rolling FPS over a fixed time window. Decays to zero when no marks arrive.
sealed class RollingFps
{
    readonly Queue<long> _ticks = new();
    readonly long _windowTicks;

    public RollingFps(int windowMs)
    {
        _windowTicks = Stopwatch.Frequency * windowMs / 1000;
    }

    public void Mark()
    {
        long now = Stopwatch.GetTimestamp();
        _ticks.Enqueue(now);
        Trim(now);
    }

    public double Current
    {
        get
        {
            long now = Stopwatch.GetTimestamp();
            Trim(now);
            return _ticks.Count * 1000.0 / Math.Max(1, _windowTicks * 1000 / Stopwatch.Frequency);
        }
    }

    void Trim(long now)
    {
        long cutoff = now - _windowTicks;
        while (_ticks.Count > 0 && _ticks.Peek() < cutoff)
        {
            _ticks.Dequeue();
        }
    }
}
