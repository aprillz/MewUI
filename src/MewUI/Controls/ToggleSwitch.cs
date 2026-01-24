using Aprillz.MewUI.Rendering;

namespace Aprillz.MewUI.Controls;

public sealed class ToggleSwitch : ToggleBase
{
    private bool _isPressed;
    private bool _isDragging;
    private double _dragT;
    private double _dragStartX;
    private TextMeasureCache _textMeasureCache;

    public ToggleSwitch()
    {
        BorderThickness = 1;
        Padding = new Thickness(8, 4, 8, 4);
        MinHeight = GetTheme().BaseControlHeight;

        // ToggleBase sets Background=Transparent. For ToggleSwitch we want a normal control background by default.
        Background = GetTheme().Palette.ButtonFace;
    }

    private const double spacing = 8;

    protected override void OnThemeChanged(Theme oldTheme, Theme newTheme)
    {
        base.OnThemeChanged(oldTheme, newTheme);

        if (MinHeight == oldTheme.BaseControlHeight)
        {
            MinHeight = newTheme.BaseControlHeight;
        }

        if (Background == oldTheme.Palette.ButtonFace)
        {
            Background = newTheme.Palette.ButtonFace;
        }

        _textMeasureCache.Invalidate();
    }

    private (double trackWidth, double trackHeight) GetTrackSize()
    {
        var theme = GetTheme();

        // Match the overall control sizing style (Button/ComboBox) while keeping the switch itself compact.
        // BaseControlHeight is 28 in the default theme -> trackHeight 20.
        double trackHeight = Math.Max(16, theme.BaseControlHeight - 8);
        double trackWidth = Math.Max(trackHeight * 2, trackHeight + 18);
        return (trackWidth, trackHeight);
    }

    protected override Size MeasureContent(Size availableSize)
    {
        var (trackWidth, trackHeight) = GetTrackSize();

        double width = trackWidth;
        double height = trackHeight;

        if (!string.IsNullOrEmpty(Text))
        {
            var factory = GetGraphicsFactory();
            var font = GetFont(factory);
            var textSize = _textMeasureCache.Measure(factory, GetDpi(), font, Text, TextWrapping.NoWrap, 0);
            width += spacing + textSize.Width;
            height = Math.Max(height, textSize.Height);
        }

        return new Size(width, height).Inflate(Padding);
    }

    protected override void OnRender(IGraphicsContext context)
    {
        var theme = GetTheme();
        var bounds = GetSnappedBorderBounds(Bounds);
        var contentBounds = bounds.Deflate(Padding);

        var (trackWidth, trackHeight) = GetTrackSize();

        double y = contentBounds.Y + (contentBounds.Height - trackHeight) / 2;
        var trackRect = new Rect(contentBounds.X, y, trackWidth, trackHeight);
        trackRect = LayoutRounding.SnapBoundsRectToPixels(trackRect, context.DpiScale);

        double radius = trackRect.Height / 2.0;
        double borderInset = GetBorderVisualInset();
        double stroke = Math.Max(1, BorderThickness);

        double t = _isDragging ? _dragT : (IsChecked ? 1.0 : 0.0);

        var state = GetVisualState(_isPressed, _isPressed || _isDragging);
        var isHot = state.IsHot;
        var isFocus = state.IsFocused;
        var isActive = state.IsActive;

        Color trackOff;
        Color trackOn;
        if (state.IsEnabled)
        {
            trackOff = Background;
            trackOn = theme.Palette.Accent;

            if (isHot)
            {
                trackOff = trackOff.Lerp(theme.Palette.Accent, 0.08);
                trackOn = trackOn.Lerp(theme.Palette.ControlBackground, 0.10);
            }
            if (isActive)
            {
                trackOff = trackOff.Lerp(theme.Palette.Accent, 0.12);
                trackOn = trackOn.Lerp(theme.Palette.ControlBackground, 0.06);
            }
        }
        else
        {
            trackOff = theme.Palette.ButtonDisabledBackground;
            trackOn = theme.Palette.DisabledAccent;
        }

        var trackFill = trackOff.Lerp(trackOn, t);


        if (IsChecked)
        {
            context.FillRoundedRectangle(trackRect, radius, radius, trackFill);
        }
        else
        {
            var borderColor = PickAccentBorder(theme, theme.Palette.ControlBorder, state);

            DrawBackgroundAndBorder(context, trackRect, trackFill, borderColor, radius);
        }


        double thumbInset = Math.Max(2, trackRect.Height * 0.20) + borderInset;
        double thumbSize = Math.Max(0, trackRect.Height - thumbInset * 2);
        double thumbXMin = trackRect.X + thumbInset;
        double thumbXMax = trackRect.Right - thumbInset - thumbSize;
        double thumbX = thumbXMin + (thumbXMax - thumbXMin) * t;
        var thumbRect = new Rect(thumbX, trackRect.Y + thumbInset, thumbSize, thumbSize);

        Color thumbFill;

        if (state.IsEnabled)
        {
            if (isFocus)
            {
                thumbFill = (IsChecked ? theme.Palette.AccentText : theme.Palette.WindowText).Lerp(theme.Palette.Focus, 0.3);
            }
            else
            {
                thumbFill = IsChecked ? theme.Palette.AccentText : theme.Palette.WindowText;
            }
        }
        else
        {
            thumbFill = IsChecked ? theme.Palette.DisabledControlBackground : theme.Palette.DisabledText;
        }
        context.FillEllipse(thumbRect, thumbFill);

        //var thumbBorder = state.IsEnabled
        //    ? theme.Palette.ControlBorder.Lerp(borderColor, isActive ? 0.55 : (isHot ? 0.40 : 0.25))
        //    : theme.Palette.ControlBorder.Lerp(theme.Palette.DisabledText, 0.25);
        //context.DrawEllipse(thumbRect, thumbBorder, 1);

        if (!string.IsNullOrEmpty(Text))
        {
            var font = GetFont();
            var textColor = state.IsEnabled ? Foreground : theme.Palette.DisabledText;
            var textBounds = new Rect(trackRect.Right + spacing, contentBounds.Y, contentBounds.Width - trackRect.Width - spacing, contentBounds.Height);
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
        _isDragging = false;
        _dragT = IsChecked ? 1 : 0;
        _dragStartX = e.Position.X;
        Focus();

        var root = FindVisualRoot();
        if (root is Window window)
        {
            window.CaptureMouse(this);
        }

        InvalidateVisual();
        e.Handled = true;
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);

        if (!IsEnabled || !_isPressed || !IsMouseCaptured || !e.LeftButton)
        {
            return;
        }

        const double dragThreshold = 3;
        double dx = e.Position.X - _dragStartX;
        if (!_isDragging && Math.Abs(dx) >= dragThreshold)
        {
            _isDragging = true;
        }

        if (_isDragging)
        {
            double t0 = IsChecked ? 1.0 : 0.0;
            var (trackWidth, trackHeight) = GetTrackSize();
            var dragRange = Math.Max(1, trackWidth - trackHeight);
            _dragT = Math.Clamp(t0 + dx / dragRange, 0, 1);
            InvalidateVisual();
        }

        e.Handled = true;
    }

    protected override void OnMouseUp(MouseEventArgs e)
    {
        base.OnMouseUp(e);

        if (e.Button != MouseButton.Left || !_isPressed)
        {
            return;
        }

        bool wasDragging = _isDragging;
        _isPressed = false;
        _isDragging = false;

        var root = FindVisualRoot();
        if (root is Window window)
        {
            window.ReleaseMouseCapture();
        }

        if (!IsEnabled)
        {
            return;
        }

        if (wasDragging)
        {
            IsChecked = _dragT >= 0.5;
        }
        else if (Bounds.Contains(e.Position))
        {
            IsChecked = !IsChecked;
        }

        InvalidateVisual();
        e.Handled = true;
    }

    // Binding provided by ToggleBase
}
