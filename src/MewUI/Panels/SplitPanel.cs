using Aprillz.MewUI.Rendering;

namespace Aprillz.MewUI.Controls;

/// <summary>
/// A two-pane layout panel with a draggable splitter.
/// </summary>
public sealed class SplitPanel : Panel
{
    private UIElement? _first;
    private UIElement? _second;
    private readonly SplitterThumb _splitter;

    private bool _isDragging;
    private double _dragStartMain;
    private double _dragStartFirst;
    private double _dragStartSecond;

    public SplitPanel()
    {
        _splitter = new SplitterThumb(this)
        {
            IsVisible = false,
            IsHitTestVisible = true,
        };

        RebuildChildren();
    }

    /// <summary>
    /// Gets or sets the split direction.
    /// Horizontal means left/right panes; Vertical means top/bottom panes.
    /// </summary>
    public Orientation Orientation
    {
        get;
        set
        {
            if (Set(ref field, value))
            {
                InvalidateMeasure();
            }
        }
    } = Orientation.Horizontal;

    /// <summary>
    /// Gets or sets the splitter thickness (DIPs).
    /// </summary>
    public double SplitterThickness
    {
        get;
        set
        {
            if (SetDouble(ref field, value))
            {
                InvalidateMeasure();
            }
        }
    } = 6;

    /// <summary>
    /// Gets or sets the first pane length.
    /// </summary>
    public GridLength FirstLength
    {
        get;
        set
        {
            if (!field.Equals(value))
            {
                field = value;
                InvalidateMeasure();
            }
        }
    } = GridLength.Star;

    /// <summary>
    /// Gets or sets the second pane length.
    /// Used when both panes are star-sized.
    /// </summary>
    public GridLength SecondLength
    {
        get;
        set
        {
            if (!field.Equals(value))
            {
                field = value;
                InvalidateMeasure();
            }
        }
    } = GridLength.Star;

    public double MinFirst
    {
        get;
        set
        {
            if (SetDouble(ref field, value))
            {
                InvalidateMeasure();
            }
        }
    }

    public double MinSecond
    {
        get;
        set
        {
            if (SetDouble(ref field, value))
            {
                InvalidateMeasure();
            }
        }
    }

    public double MaxFirst
    {
        get;
        set
        {
            if (SetDouble(ref field, value))
            {
                InvalidateMeasure();
            }
        }
    } = double.PositiveInfinity;

    public double MaxSecond
    {
        get;
        set
        {
            if (SetDouble(ref field, value))
            {
                InvalidateMeasure();
            }
        }
    } = double.PositiveInfinity;

    /// <summary>
    /// Gets or sets the first pane content.
    /// </summary>
    public UIElement? First
    {
        get => _first;
        set
        {
            if (ReferenceEquals(_first, value))
            {
                return;
            }

            _first = value;
            RebuildChildren();
            InvalidateMeasure();
        }
    }

    /// <summary>
    /// Gets or sets the second pane content.
    /// </summary>
    public UIElement? Second
    {
        get => _second;
        set
        {
            if (ReferenceEquals(_second, value))
            {
                return;
            }

            _second = value;
            RebuildChildren();
            InvalidateMeasure();
        }
    }

    private void RebuildChildren()
    {
        Clear();

        if (_first != null)
        {
            Add(_first);
        }

        Add(_splitter);

        if (_second != null)
        {
            Add(_second);
        }
    }

    protected override Size MeasureContent(Size availableSize)
    {
        var paddedSize = availableSize.Deflate(Padding);

        var first = _first;
        var second = _second;

        bool firstVisible = first is { IsVisible: true };
        bool secondVisible = second is { IsVisible: true };

        if (!firstVisible && !secondVisible)
        {
            _splitter.IsVisible = false;
            return Size.Empty.Inflate(Padding);
        }

        if (firstVisible && !secondVisible)
        {
            _splitter.IsVisible = false;
            first!.Measure(paddedSize);
            return first.DesiredSize.Inflate(Padding);
        }

        if (!firstVisible && secondVisible)
        {
            _splitter.IsVisible = false;
            second!.Measure(paddedSize);
            return second.DesiredSize.Inflate(Padding);
        }

        _splitter.IsVisible = true;

        double thickness = Math.Max(0, SplitterThickness);
        bool isHorizontal = Orientation == Orientation.Horizontal;

        if (isHorizontal)
        {
            _splitter.Measure(new Size(thickness, paddedSize.Height));
        }
        else
        {
            _splitter.Measure(new Size(paddedSize.Width, thickness));
        }

        // First pass: measure unconstrained to get desired sizes (for Auto).
        first!.Measure(Size.Infinity);
        second!.Measure(Size.Infinity);

        double availableMain = isHorizontal ? paddedSize.Width : paddedSize.Height;
        double availableCross = isHorizontal ? paddedSize.Height : paddedSize.Width;
        double panesMain = double.IsPositiveInfinity(availableMain) ? double.PositiveInfinity : Math.Max(0, availableMain - thickness);

        if (!double.IsPositiveInfinity(panesMain))
        {
            ResolvePaneSizes(
                panesMain,
                isHorizontal ? first.DesiredSize.Width : first.DesiredSize.Height,
                isHorizontal ? second.DesiredSize.Width : second.DesiredSize.Height,
                out double firstMain,
                out double secondMain);

            if (isHorizontal)
            {
                first.Measure(new Size(firstMain, availableCross));
                second.Measure(new Size(secondMain, availableCross));
            }
            else
            {
                first.Measure(new Size(availableCross, firstMain));
                second.Measure(new Size(availableCross, secondMain));
            }
        }

        double firstMainDesired = isHorizontal ? first.DesiredSize.Width : first.DesiredSize.Height;
        double secondMainDesired = isHorizontal ? second.DesiredSize.Width : second.DesiredSize.Height;
        double main = firstMainDesired + thickness + secondMainDesired;

        double firstCrossDesired = isHorizontal ? first.DesiredSize.Height : first.DesiredSize.Width;
        double secondCrossDesired = isHorizontal ? second.DesiredSize.Height : second.DesiredSize.Width;
        double cross = Math.Max(firstCrossDesired, secondCrossDesired);

        var content = isHorizontal ? new Size(main, cross) : new Size(cross, main);
        return content.Inflate(Padding);
    }

    protected override void ArrangeContent(Rect bounds)
    {
        var content = bounds.Deflate(Padding);

        var first = _first;
        var second = _second;

        bool firstVisible = first is { IsVisible: true };
        bool secondVisible = second is { IsVisible: true };

        if (firstVisible && !secondVisible)
        {
            _splitter.IsVisible = false;
            first!.Arrange(content);
            _splitter.Arrange(new Rect(content.X, content.Y, 0, 0));
            if (second != null)
            {
                second.Arrange(new Rect(content.X, content.Y, 0, 0));
            }
            return;
        }

        if (!firstVisible && secondVisible)
        {
            _splitter.IsVisible = false;
            second!.Arrange(content);
            _splitter.Arrange(new Rect(content.X, content.Y, 0, 0));
            if (first != null)
            {
                first.Arrange(new Rect(content.X, content.Y, 0, 0));
            }
            return;
        }

        if (!firstVisible && !secondVisible)
        {
            _splitter.IsVisible = false;
            _splitter.Arrange(new Rect(content.X, content.Y, 0, 0));
            if (first != null)
            {
                first.Arrange(new Rect(content.X, content.Y, 0, 0));
            }
            if (second != null)
            {
                second.Arrange(new Rect(content.X, content.Y, 0, 0));
            }
            return;
        }

        _splitter.IsVisible = true;

        bool isHorizontal = Orientation == Orientation.Horizontal;
        double thickness = Math.Max(0, SplitterThickness);

        double totalMain = isHorizontal ? content.Width : content.Height;
        double panesMain = Math.Max(0, totalMain - thickness);

        ResolvePaneSizes(
            panesMain,
            isHorizontal ? first!.DesiredSize.Width : first!.DesiredSize.Height,
            isHorizontal ? second!.DesiredSize.Width : second!.DesiredSize.Height,
            out double firstMain,
            out double secondMain);

        if (isHorizontal)
        {
            first.Arrange(new Rect(content.X, content.Y, firstMain, content.Height));
            _splitter.Arrange(new Rect(content.X + firstMain, content.Y, thickness, content.Height));
            second!.Arrange(new Rect(content.X + firstMain + thickness, content.Y, secondMain, content.Height));
        }
        else
        {
            first!.Arrange(new Rect(content.X, content.Y, content.Width, firstMain));
            _splitter.Arrange(new Rect(content.X, content.Y + firstMain, content.Width, thickness));
            second!.Arrange(new Rect(content.X, content.Y + firstMain + thickness, content.Width, secondMain));
        }
    }

    private void ResolvePaneSizes(double panesMain, double firstDesiredMain, double secondDesiredMain, out double firstMain, out double secondMain)
    {
        panesMain = Math.Max(0, panesMain);

        double minFirst = Math.Max(0, MinFirst);
        double minSecond = Math.Max(0, MinSecond);
        double maxFirst = MaxFirst;
        double maxSecond = MaxSecond;

        double fixedFirst = LengthToMain(FirstLength, firstDesiredMain);
        double fixedSecond = LengthToMain(SecondLength, secondDesiredMain);

        bool firstStar = FirstLength.IsStar;
        bool secondStar = SecondLength.IsStar;

        if (double.IsPositiveInfinity(panesMain))
        {
            firstMain = fixedFirst;
            secondMain = fixedSecond;
        }
        else if (firstStar && secondStar)
        {
            double a = Math.Max(0, FirstLength.Value);
            double b = Math.Max(0, SecondLength.Value);
            double denom = a + b;
            if (denom <= 0)
            {
                firstMain = panesMain * 0.5;
                secondMain = panesMain - firstMain;
            }
            else
            {
                firstMain = panesMain * (a / denom);
                secondMain = panesMain - firstMain;
            }
        }
        else if (firstStar && !secondStar)
        {
            secondMain = fixedSecond;
            firstMain = panesMain - secondMain;
        }
        else if (!firstStar && secondStar)
        {
            firstMain = fixedFirst;
            secondMain = panesMain - firstMain;
        }
        else
        {
            firstMain = fixedFirst;
            secondMain = fixedSecond;

            double sum = firstMain + secondMain;
            if (sum < panesMain)
            {
                secondMain += panesMain - sum;
            }
            else if (sum > panesMain)
            {
                secondMain = Math.Max(0, panesMain - firstMain);
                if (firstMain + secondMain > panesMain)
                {
                    firstMain = panesMain;
                    secondMain = 0;
                }
            }
        }

        // Clamp second then first (keep sum constant when possible).
        secondMain = Math.Clamp(secondMain, minSecond, maxSecond);
        firstMain = panesMain - secondMain;
        firstMain = Math.Clamp(firstMain, minFirst, maxFirst);
        secondMain = panesMain - firstMain;
        secondMain = Math.Clamp(secondMain, minSecond, maxSecond);
        firstMain = panesMain - secondMain;

        firstMain = Math.Max(0, firstMain);
        secondMain = Math.Max(0, secondMain);
    }

    private static double LengthToMain(GridLength length, double desired)
    {
        if (length.IsAbsolute)
        {
            return Math.Max(0, length.Value);
        }

        if (length.IsAuto)
        {
            return Math.Max(0, desired);
        }

        // Star: return a placeholder (handled by distribution).
        return desired;
    }

    private void BeginDrag(MouseEventArgs e)
    {
        if (_isDragging)
        {
            return;
        }

        if (_first is not { IsVisible: true } first || _second is not { IsVisible: true } second)
        {
            return;
        }

        var pos = e.GetPosition(this);
        bool isHorizontal = Orientation == Orientation.Horizontal;
        _dragStartMain = isHorizontal ? pos.X : pos.Y;
        _dragStartFirst = isHorizontal ? first.RenderSize.Width : first.RenderSize.Height;
        _dragStartSecond = isHorizontal ? second.RenderSize.Width : second.RenderSize.Height;

        _isDragging = true;

        var root = FindVisualRoot();
        if (root is Window window)
        {
            window.CaptureMouse(_splitter);
        }
    }

    private void Drag(MouseEventArgs e)
    {
        if (!_isDragging)
        {
            return;
        }

        if (_first is not { IsVisible: true } || _second is not { IsVisible: true })
        {
            return;
        }

        var pos = e.GetPosition(this);
        bool isHorizontal = Orientation == Orientation.Horizontal;
        double main = isHorizontal ? pos.X : pos.Y;

        double delta = main - _dragStartMain;
        double panesMain = Math.Max(0, _dragStartFirst + _dragStartSecond);
        double newFirst = _dragStartFirst + delta;

        double minFirst = Math.Max(0, MinFirst);
        double minSecond = Math.Max(0, MinSecond);
        double maxFirst = MaxFirst;
        double maxSecond = MaxSecond;

        // Clamp by second constraints.
        double newSecond = panesMain - newFirst;
        newSecond = Math.Clamp(newSecond, minSecond, maxSecond);
        newFirst = panesMain - newSecond;
        newFirst = Math.Clamp(newFirst, minFirst, maxFirst);
        newSecond = panesMain - newFirst;

        FirstLength = GridLength.Pixels(newFirst);
        SecondLength = GridLength.Star;
        InvalidateMeasure();
        InvalidateVisual();
    }

    private void EndDrag()
    {
        _isDragging = false;

        var root = FindVisualRoot();
        if (root is Window window)
        {
            window.ReleaseMouseCapture();
        }
    }

    private sealed class SplitterThumb : Control
    {
        private readonly SplitPanel _owner;

        public SplitterThumb(SplitPanel owner)
        {
            _owner = owner;
            Background = Color.Transparent;
        }

        public override bool Focusable => false;

        protected override void OnMouseDown(MouseEventArgs e)
        {
            base.OnMouseDown(e);
            if (e.Button == MouseButton.Left && _owner.IsEffectivelyEnabled)
            {
                _owner.BeginDrag(e);
                e.Handled = true;
            }
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);
            if (_owner._isDragging)
            {
                _owner.Drag(e);
                e.Handled = true;
            }
        }

        protected override void OnMouseUp(MouseEventArgs e)
        {
            base.OnMouseUp(e);
            if (e.Button == MouseButton.Left && _owner._isDragging)
            {
                _owner.EndDrag();
                e.Handled = true;
            }
        }

        protected override void OnRender(IGraphicsContext context)
        {
            base.OnRender(context);

            var theme = _owner.Theme;
            var bounds = Bounds;

            // Visual state
            bool pressed = IsMouseCaptured || _owner._isDragging;
            bool hot = pressed || IsMouseOver;

            // Subtle background highlight on hover/press.
            if (hot)
            {
                byte alpha = pressed ? (byte)48 : (byte)26;
                var r = _owner.SplitterThickness / 2.0;
                if (r > 0)
                {
                    context.FillRoundedRectangle(bounds, r, r, theme.Palette.Accent.WithAlpha(alpha));
                }
                else
                {
                    context.FillRectangle(bounds, theme.Palette.Accent.WithAlpha(alpha));
                }
            }

            // Minimal visual: draw a subtle line centered in the thumb.
            double t = pressed ? 0.65 : IsMouseOver ? 0.35 : 0.15;
            var color = theme.Palette.ControlBorder.Lerp(theme.Palette.Accent, t);

            if (_owner.Orientation == Orientation.Horizontal)
            {
                double x = bounds.X + bounds.Width / 2;
                context.DrawLine(new Point(x, bounds.Y + 4), new Point(x, bounds.Bottom - 4), color, thickness: pressed ? 2 : 1);
            }
            else
            {
                double y = bounds.Y + bounds.Height / 2;
                context.DrawLine(new Point(bounds.X + 4, y), new Point(bounds.Right - 4, y), color, thickness: pressed ? 2 : 1);
            }
        }
    }
}
