using Aprillz.MewUI.Animation;
using Aprillz.MewUI.Rendering;

namespace Aprillz.MewUI.Controls;

/// <summary>
/// Represents an active busy indicator. Dispose to dismiss.
/// </summary>
public interface IBusyIndicator : IDisposable
{
    /// <summary>
    /// Gets a cancellation token that is cancelled when the user confirms abort.
    /// Always returns <see cref="CancellationToken.None"/> if the indicator is not cancellable.
    /// </summary>
    CancellationToken CancellationToken { get; }

    /// <summary>
    /// Updates the progress message displayed below the progress ring.
    /// </summary>
    void NotifyProgress(string message);
}

/// <summary>
/// Overlay service for showing busy indicators with optional abort support.
/// </summary>
public sealed class BusyIndicatorService : IOverlayService
{
    private readonly OverlayLayer _layer;

    internal BusyIndicatorService(OverlayLayer layer)
    {
        _layer = layer;
    }

    /// <summary>
    /// Creates and shows a busy indicator.
    /// Dispose the returned <see cref="IBusyIndicator"/> to dismiss it.
    /// </summary>
    /// <param name="message">Initial progress message.</param>
    /// <param name="cancellable">If <c>true</c>, an Abort button is shown.</param>
    public IBusyIndicator Create(string? message = null, bool cancellable = false)
    {
        return new BusyIndicatorSession(_layer, message, cancellable);
    }
}

internal sealed class BusyIndicatorSession : IBusyIndicator
{
    private readonly OverlayLayer _layer;
    private readonly BusyIndicatorPresenter _presenter;
    private readonly CancellationTokenSource? _cts;
    private readonly Window? _window;
    private readonly bool _contentWasEnabled;
    private bool _disposed;

    internal BusyIndicatorSession(OverlayLayer layer, string? message, bool cancellable)
    {
        _layer = layer;
        _cts = cancellable ? new CancellationTokenSource() : null;
        _presenter = new BusyIndicatorPresenter(message, cancellable, _cts);
        _layer.Add(_presenter);
        _presenter.FadeIn();

        // Disable window content and prevent closing
        _window = _presenter.Parent as Window;
        if (_window != null)
        {
            if (_window.Content is UIElement content)
            {
                _contentWasEnabled = content.IsEnabled;
                content.IsEnabled = false;
            }
            _window.Closing += OnWindowClosing;
        }
    }

    public CancellationToken CancellationToken => _cts?.Token ?? CancellationToken.None;

    public void NotifyProgress(string message)
    {
        if (_disposed) return;
        _presenter.UpdateMessage(message);
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        // Restore window state
        if (_window != null)
        {
            _window.Closing -= OnWindowClosing;
            if (_window.Content is UIElement content)
                content.IsEnabled = _contentWasEnabled;
        }

        _presenter.FadeOut(() =>
        {
            _layer.Remove(_presenter);
            _presenter.StopAnimation();
            _cts?.Dispose();
        });
    }

    private void OnWindowClosing(ClosingEventArgs e)
    {
        if (!_disposed)
            e.Cancel = true;
    }
}

/// <summary>
/// Internal presenter control for busy indicator.
/// Grid layout: Row 0 (*) = bottom-aligned ring + abort, Row 1 (Auto) = spacing, Row 2 (*) = top-aligned message.
/// </summary>
internal sealed class BusyIndicatorPresenter : Control, IVisualTreeHost
{
    private const int FadeDurationMs = 250;

    private readonly ProgressRing _ring;
    private readonly Label _messageLabel;
    private readonly CancellationTokenSource? _cts;
    private readonly bool _cancellable;
    private AnimationClock? _fadeClock;
    private double _opacity;

    // Abort UI elements — only created when cancellable
    private readonly Label? _abortLabel;

    private readonly TextBlock? _confirmLabel;
    private readonly Label? _yesLabel;
    private readonly Label? _noLabel;
    private readonly TextBlock? _abortingLabel;
    private readonly Border? _abortArea;
    private readonly StackPanel? _normalPanel;
    private readonly StackPanel? _confirmPanel;
    private readonly StackPanel? _abortPanel;

    private enum AbortState
    { Normal, Confirming, Aborting }

    private AbortState _abortState = AbortState.Normal;

    public static readonly MewProperty<double> RingSizeProperty =
        MewProperty<double>.Register<BusyIndicatorPresenter>(nameof(RingSize), 64.0,
            MewPropertyOptions.AffectsLayout,
            static (self, _, newValue) =>
            {
                var clamped = Math.Max(32, Math.Min(256, newValue));
                self._ring.Width = clamped;
                self._ring.Height = clamped;
            });

    private readonly Grid _child;

    public double RingSize
    {
        get => GetValue(RingSizeProperty);
        set => SetValue(RingSizeProperty, value);
    }

    internal BusyIndicatorPresenter(string? message, bool cancellable, CancellationTokenSource? cts)
    {
        _cancellable = cancellable;
        _cts = cts;

        _ring = new ProgressRing
        {
            IsActive = true,
            Width = RingSizeProperty.DefaultValue,
            Height = RingSizeProperty.DefaultValue,
            HorizontalAlignment = HorizontalAlignment.Center,
        };
        _ring.WithTheme((t, c) => c.Foreground = t.Palette.Accent);

        _messageLabel = new Label
        {
            HorizontalAlignment = HorizontalAlignment.Center,
            Padding = new Thickness(8, 4),
            Margin = new Thickness(0, 12, 0, 0),
            IsVisible = message != null,
        };
        _messageLabel.WithTheme((t, c) =>
        {
            c.Foreground = t.Palette.WindowText;
            c.Background = t.Palette.ControlBackground;
        });

        if (message != null)
            _messageLabel.Text = message;

        // Center stack: ring + abort + message, all vertically stacked
        var centerStack = new StackPanel { HorizontalAlignment = HorizontalAlignment.Center };
        centerStack.Vertical().Spacing(4);
        centerStack.Add(_ring);

        if (cancellable)
        {
            // Normal state: "Abort" label
            _abortLabel = new Label { Text = MewUIStrings.Abort.Value, Cursor = CursorType.Hand };
            SetupHoverAccent(_abortLabel);
            _abortLabel.MouseDown += OnAbortClicked;

            _normalPanel = new StackPanel { HorizontalAlignment = HorizontalAlignment.Center };
            _normalPanel.Horizontal();
            _normalPanel.Add(_abortLabel);

            _confirmLabel = new TextBlock { Text = MewUIStrings.AbortConfirmation.Value };
            _confirmLabel.WithTheme((t, c) => c.Foreground = t.Palette.WindowText);

            _yesLabel = new Label { Text = StripAccessKey(MewUIStrings.Yes.Value), Cursor = CursorType.Hand };
            SetupHoverAccent(_yesLabel);
            _yesLabel.MouseDown += OnYesClicked;

            _noLabel = new Label { Text = StripAccessKey(MewUIStrings.No.Value), Cursor = CursorType.Hand };
            SetupHoverAccent(_noLabel);
            _noLabel.MouseDown += OnNoClicked;

            _confirmPanel = new StackPanel { HorizontalAlignment = HorizontalAlignment.Center, IsVisible = false };
            _confirmPanel.Horizontal().Spacing(8);
            _confirmPanel.Add(_confirmLabel);
            _confirmPanel.Add(_yesLabel);
            _confirmPanel.Add(_noLabel);

            // Aborting state label
            _abortingLabel = new TextBlock { Text = MewUIStrings.Aborting.Value, IsVisible = false };
            _abortingLabel.WithTheme((t, c) => c.Foreground = t.Palette.WindowText);

            // Container that switches between the three states
            _abortPanel = new StackPanel { HorizontalAlignment = HorizontalAlignment.Center };
            _abortPanel.Add(_normalPanel);
            _abortPanel.Add(_confirmPanel);
            _abortPanel.Add(_abortingLabel);

            _abortArea = new Border
            {
                Child = _abortPanel,
                Padding = new(8, 4),
                HorizontalAlignment = HorizontalAlignment.Center
            };
            _abortArea.WithTheme((t, c) =>
            {
                c.Background = t.Palette.ControlBackground;
                c.CornerRadius = t.Metrics.ControlCornerRadius;
            });

            centerStack.Add(_abortArea);
        }

        centerStack.Add(_messageLabel);

        // Build grid: *,Auto,* — centers everything vertically
        var grid = new Grid();
        grid.Rows("2*,Auto,*");

        centerStack.Row(1);
        grid.Add(centerStack);

        _child = grid;
        _child.Parent = this;
        IsHitTestVisible = true; // block input to controls behind the overlay
    }

    internal void FadeIn()
    {
        _fadeClock?.Stop();
        _fadeClock = new AnimationClock(TimeSpan.FromMilliseconds(FadeDurationMs), Easing.EaseOutCubic);
        _fadeClock.TickCallback = progress =>
        {
            _opacity = (float)progress;
            InvalidateVisual();
        };
        _fadeClock.Start();
    }

    internal void FadeOut(Action onCompleted)
    {
        _fadeClock?.Stop();
        _fadeClock = new AnimationClock(TimeSpan.FromMilliseconds(FadeDurationMs), Easing.EaseInCubic);
        _fadeClock.TickCallback = progress =>
        {
            _opacity = 1.0 - progress;
            InvalidateVisual();
        };
        _fadeClock.CompletedCallback = onCompleted;
        _fadeClock.Start();
    }

    protected override void OnRender(IGraphicsContext context)
    {
        if (_opacity <= 0) return;

        context.Save();
        context.GlobalAlpha *= (float)_opacity;

        // Dim the entire window with a semi-transparent background
        var bg = Theme.Palette.ControlBackground;
        context.FillRectangle(Bounds, Color.FromArgb(Theme.IsDark ? (byte)192 : (byte)160, bg.R, bg.G, bg.B)); // ~75% opacity

        base.OnRender(context);
        context.Restore();
    }

    protected override Size MeasureContent(Size availableSize)
    {
        _child.Measure(availableSize);
        return _child.DesiredSize;
    }

    protected override void ArrangeContent(Rect bounds)
    {
        _child.Arrange(bounds);
    }

    protected override void RenderSubtree(IGraphicsContext context)
    {
        if (_opacity <= 0) return;
        base.RenderSubtree(context);
        _child.Render(context);
    }

    protected override UIElement? OnHitTest(Point point)
    {
        if (!IsVisible || !IsHitTestVisible) return null;
        if (_child is UIElement uiChild)
        {
            var result = uiChild.HitTest(point);
            if (result != null) return result;
        }
        return Bounds.Contains(point) ? this : null;
    }

    bool IVisualTreeHost.VisitChildren(Func<Element, bool> visitor) => visitor(_child);

    internal void UpdateMessage(string message)
    {
        _messageLabel.Text = message;
        _messageLabel.IsVisible = true;
    }

    internal void StopAnimation()
    {
        _ring.IsActive = false;
    }

    private void SetAbortState(AbortState state)
    {
        if (!_cancellable) return;
        _abortState = state;

        _normalPanel!.IsVisible = state == AbortState.Normal;
        _confirmPanel!.IsVisible = state == AbortState.Confirming;
        _abortingLabel!.IsVisible = state == AbortState.Aborting;
    }

    private static void SetupHoverAccent(Control label)
    {
        Color accentColor = default;
        Color normalColor = default;
        label.WithTheme((t, c) =>
        {
            accentColor = t.Palette.Accent;
            normalColor = t.Palette.WindowText;
            c.Foreground = normalColor;
        });
        label.MouseEnter += () => label.Foreground = accentColor;
        label.MouseLeave += () => label.Foreground = normalColor;
    }

    private static string StripAccessKey(string text) => text.Replace("_", "");

    private void OnAbortClicked(MouseEventArgs e)
    {
        if (_abortState == AbortState.Normal)
            SetAbortState(AbortState.Confirming);
        e.Handled = true;
    }

    private void OnYesClicked(MouseEventArgs e)
    {
        if (_abortState == AbortState.Confirming)
        {
            SetAbortState(AbortState.Aborting);
            _cts?.Cancel();
        }
        e.Handled = true;
    }

    private void OnNoClicked(MouseEventArgs e)
    {
        if (_abortState == AbortState.Confirming)
            SetAbortState(AbortState.Normal);
        e.Handled = true;
    }
}
