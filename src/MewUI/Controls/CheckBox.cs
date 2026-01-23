using Aprillz.MewUI.Rendering;

namespace Aprillz.MewUI.Controls;

public class CheckBox : Control
{
    private bool _isPressed;
    private TextMeasureCache _textMeasureCache;
    private bool? _isChecked = false;
    private ValueBinding<bool?>? _checkedBinding;
    private bool _updatingFromSource;

    public CheckBox()
    {
        Background = Color.Transparent;
        BorderThickness = 1;
        Padding = new Thickness(2);
    }

    public override bool Focusable => true;

    protected override Color DefaultBorderBrush => GetTheme().Palette.ControlBorder;

    public string Text
    {
        get;
        set
        {
            field = value ?? string.Empty;
            InvalidateMeasure();
            InvalidateVisual();
        }
    } = string.Empty;

    public bool IsThreeState
    {
        get;
        set;
    }

    public bool? IsChecked
    {
        get => _isChecked;
        set
        {
            if (_isChecked == value)
            {
                return;
            }

            SetIsCheckedCore(value, fromInput: false);
        }
    }
 
    public event Action<bool?>? CheckedChanged;

    public void SetIsCheckedBinding(
        Func<bool?> get,
        Action<bool?> set,
        Action<Action>? subscribe = null,
        Action<Action>? unsubscribe = null)
    {
        ArgumentNullException.ThrowIfNull(get);
        ArgumentNullException.ThrowIfNull(set);

        _checkedBinding?.Dispose();
        _checkedBinding = new ValueBinding<bool?>(
            get,
            set,
            subscribe,
            unsubscribe,
            () =>
            {
                _updatingFromSource = true;
                try { SetIsCheckedFromSource(get()); }
                finally { _updatingFromSource = false; }
            });

        _updatingFromSource = true;
        try { SetIsCheckedFromSource(get()); }
        finally { _updatingFromSource = false; }
    }

    protected override Size MeasureContent(Size availableSize)
    {
        const double boxSize = 14;
        const double spacing = 6;

        double width = boxSize + spacing;
        double height = boxSize;

        if (!string.IsNullOrEmpty(Text))
        {
            var factory = GetGraphicsFactory();
            var font = GetFont(factory);
            var size = _textMeasureCache.Measure(factory, GetDpi(), font, Text, TextWrapping.NoWrap, 0);
            width += size.Width;
            height = Math.Max(height, size.Height);
        }

        return new Size(width, height).Inflate(Padding);
    }

    protected override void OnRender(IGraphicsContext context)
    {
        var theme = GetTheme();
        var bounds = Bounds;
        var contentBounds = bounds.Deflate(Padding);
        var state = GetVisualState(_isPressed, _isPressed);

        const double boxSize = 14;
        const double spacing = 6;

        double boxY = contentBounds.Y + (contentBounds.Height - boxSize) / 2;
        var boxRect = new Rect(contentBounds.X, boxY, boxSize, boxSize);

        var fill = state.IsEnabled ? theme.Palette.ControlBackground : theme.Palette.DisabledControlBackground;
        var radius = Math.Max(0, theme.ControlCornerRadius * 0.5);

        var borderColor = PickAccentBorder(theme, BorderBrush, state, 0.6);
        DrawBackgroundAndBorder(context, boxRect, fill, borderColor, radius);

        var markColor = state.IsEnabled ? theme.Palette.Accent : theme.Palette.DisabledAccent;

        if (_isChecked == true)
        {
            // Check mark
            var p1 = new Point(boxRect.X + 3, boxRect.Y + boxRect.Height * 0.55);
            var p2 = new Point(boxRect.X + boxRect.Width * 0.45, boxRect.Bottom - 3);
            var p3 = new Point(boxRect.Right - 3, boxRect.Y + 3);
            context.DrawLine(p1, p2, markColor, 2);
            context.DrawLine(p2, p3, markColor, 2);
        }
        else if (_isChecked == null)
        {
            // Indeterminate mark (horizontal bar)
            var y = boxRect.Y + boxRect.Height / 2;
            var p1 = new Point(boxRect.X + 3, y);
            var p2 = new Point(boxRect.Right - 3, y);
            context.DrawLine(p1, p2, markColor, 2);
        }

        if (!string.IsNullOrEmpty(Text))
        {
            var textColor = state.IsEnabled ? Foreground : theme.Palette.DisabledText;
            var textBounds = new Rect(contentBounds.X + boxSize + spacing, contentBounds.Y, contentBounds.Width - boxSize - spacing, contentBounds.Height);
            var font = GetFont();
            context.DrawText(Text, textBounds, font, textColor, TextAlignment.Left, TextAlignment.Center, TextWrapping.NoWrap);
        }
    }

    protected override void OnMouseDown(MouseEventArgs e)
    {
        base.OnMouseDown(e);

        if (!IsEnabled || e.Button != MouseButton.Left)
        {
            return;
        }

        _isPressed = true;
        Focus();

        var root = FindVisualRoot();
        if (root is Window window)
        {
            window.CaptureMouse(this);
        }

        InvalidateVisual();
        e.Handled = true;
    }

    protected override void OnMouseUp(MouseEventArgs e)
    {
        base.OnMouseUp(e);

        if (e.Button != MouseButton.Left || !_isPressed)
        {
            return;
        }

        _isPressed = false;

        var root = FindVisualRoot();
        if (root is Window window)
        {
            window.ReleaseMouseCapture();
        }

        if (IsEnabled && Bounds.Contains(e.Position))
        {
            ToggleFromInput();
        }

        InvalidateVisual();
        e.Handled = true;
    }

    protected override void OnKeyUp(KeyEventArgs e)
    {
        base.OnKeyUp(e);

        if (!IsEnabled)
        {
            return;
        }

        if (e.Key == Key.Space)
        {
            ToggleFromInput();
            e.Handled = true;
        }
    }

    private void ToggleFromInput()
    {
        if (IsThreeState)
        {
            var next = _isChecked switch
            {
                false => (bool?)true,
                true => (bool?)null,
                _ => (bool?)false
            };

            SetIsCheckedCore(next, fromInput: true);
            return;
        }

        // If currently indeterminate, a user toggle treats it like unchecked and toggles to checked.
        bool nextBool = _isChecked != true;
        SetIsCheckedCore(nextBool, fromInput: true);
    }

    private void SetIsCheckedFromSource(bool? value) => SetIsCheckedCore(value, fromInput: false);

    private void SetIsCheckedCore(bool? value, bool fromInput)
    {
        _isChecked = value;
        CheckedChanged?.Invoke(value);

        if (fromInput && !_updatingFromSource)
        {
            _checkedBinding?.Set(value);
        }

        InvalidateVisual();
    }

    protected override void OnDispose()
    {
        _checkedBinding?.Dispose();
        _checkedBinding = null;
        base.OnDispose();
    }

    protected override void OnThemeChanged(Theme oldTheme, Theme newTheme)
    {
        base.OnThemeChanged(oldTheme, newTheme);
        _textMeasureCache.Invalidate();
    }
}
