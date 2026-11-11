using System.Globalization;

using Aprillz.MewUI.Rendering;

namespace Aprillz.MewUI.Controls;

/// <summary>
/// Draws one day, month or year of a <see cref="Calendar"/>, so a change of that cell records it alone.
/// The calendar lays the cells out and takes all input; a cell is never hit.
/// </summary>
internal sealed class CalendarCell : Control
{
    // Removes beforefieldinit: nothing reads the field below, so trimming would otherwise drop its
    // initializer and the registration with it.
    static CalendarCell() { }

    // Triggers only: a style without setters keeps out the Control base style and its themed border.
    // Later triggers win, so the selection covers the hover.
    private static readonly bool _defaultStyleRegistered =
        DefaultStyles.Register<CalendarCell>(static () => new Style(typeof(CalendarCell))
        {
            Triggers =
            [
                new StateTrigger
                {
                    Match = VisualStateFlags.Hot,
                    Setters = [Setter.Create(BackgroundProperty, theme => theme.Palette.AccentHoverOverlay)],
                },
                new StateTrigger
                {
                    Match = VisualStateFlags.Selected,
                    Setters =
                    [
                        Setter.Create(BackgroundProperty, theme => theme.Palette.Accent),
                        Setter.Create(ForegroundProperty, theme => theme.Palette.AccentText),
                    ],
                },
            ],
        });

    private const double MARK_STROKE_THICKNESS = 1;

    private string _text = string.Empty;
    private CultureInfo _culture = CultureInfo.InvariantCulture;
    private bool _isHot;
    private bool _isSelected;
    private bool _isMarked;
    private bool _isOutside;
    private double _circleInset;
    private bool _markInset;

    internal CalendarCell()
    {
        IsHitTestVisible = false;
    }

    /// <summary>
    /// Takes what the cell shows: its label, whether it is selected, whether it is the current date
    /// (drawn with a ring), and whether it lies outside the period in view (drawn dimmed).
    /// </summary>
    internal void Update(
        string text,
        CultureInfo culture,
        bool isSelected,
        bool isMarked,
        bool isOutside,
        double circleInset,
        bool markInset)
    {
        if (_isSelected != isSelected)
        {
            _isSelected = isSelected;
            InvalidateVisualState();
            InvalidateVisual();
        }

        if (!string.Equals(_text, text, StringComparison.Ordinal) ||
            !ReferenceEquals(_culture, culture) ||
            _isMarked != isMarked ||
            _isOutside != isOutside ||
            _circleInset != circleInset ||
            _markInset != markInset)
        {
            _text = text;
            _culture = culture;
            _isMarked = isMarked;
            _isOutside = isOutside;
            _circleInset = circleInset;
            _markInset = markInset;
            InvalidateVisual();
        }
    }

    /// <summary>Whether the pointer is over this cell.</summary>
    internal void SetIsHot(bool isHot)
    {
        if (_isHot != isHot)
        {
            _isHot = isHot;
            InvalidateVisualState();
        }
    }

    protected override VisualState ComputeVisualState()
    {
        var flags = VisualStateFlags.None;
        if (IsEffectivelyEnabled)
        {
            flags |= VisualStateFlags.Enabled;
        }

        if (_isHot)
        {
            flags |= VisualStateFlags.Hot;
        }

        if (_isSelected)
        {
            flags |= VisualStateFlags.Selected;
        }

        return new VisualState { Flags = flags };
    }

    protected override Size MeasureContent(Size availableSize) => Size.Empty;

    protected override void OnRender(IGraphicsContext context)
    {
        var cell = Bounds;
        double unit = Math.Max(0, Math.Min(cell.Width, cell.Height) - _circleInset);
        var circle = new Rect(cell.X + (cell.Width - unit) / 2, cell.Y + (cell.Height - unit) / 2, unit, unit);
        double radius = unit / 2.0;

        var background = Background;
        if (background.A > 0)
        {
            context.FillRoundedRectangle(circle, radius, radius, background);
        }

        if (_isMarked && !_isSelected)
        {
            double dpiScale = GetDpi() / 96.0;
            double stroke = LayoutRounding.SnapThicknessToPixels(MARK_STROKE_THICKNESS, dpiScale, 1);
            if (_markInset)
            {
                context.DrawRoundedRectangle(circle, radius, radius, Theme.Palette.Accent, stroke, strokeInset: true);
            }
            else
            {
                context.DrawRoundedRectangle(circle, radius, radius, Theme.Palette.Accent, stroke);
            }
        }

        // An outside cell is data, not a state: it stays clickable, so it cannot read as disabled.
        var color = _isOutside && !_isSelected ? Theme.Palette.DisabledText : Foreground;
        Calendar.DrawCalendarText(context, this, GetGraphicsFactory(), _text, cell, Calendar.CELL_FONT_SIZE, color, _culture);
    }
}
