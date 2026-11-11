using System.Globalization;

using Aprillz.MewUI.Rendering;
using Aprillz.MewUI.Text;

namespace Aprillz.MewUI.Controls;

/// <summary>
/// A calendar control that displays a month/year/decade view for date selection.
/// Can be used standalone or as popup content for a DatePicker.
/// </summary>
public sealed partial class Calendar : Control, IVisualTreeHost
{
    private static readonly bool _defaultStyleRegistered =
        DefaultStyles.Register<Calendar>(DefaultStyles.CreateCalendarStyle);

    private const int DaysPerWeek = 7;
    private const int MonthRows = 6;
    private const int MonthCells = DaysPerWeek * MonthRows;
    private const int YearDecadeCols = 4;
    private const int YearDecadeRows = 3;
    private const int YearDecadeCells = YearDecadeCols * YearDecadeRows;

    private const double HeaderHeight = 28;
    private const double DayOfWeekHeaderHeight = 18;
    private const double CellHeight = 24;
    private const double CellSpacing = 0;
    private const double NavButtonWidth = 32;

    // Navigation buttons (FlatButton style)
    private readonly Button _prevButton;
    private readonly Button _nextButton;
    private readonly Button _headerButton; // "March 2026" - click to switch mode

    // Cached header text
    private DateTime _cachedHeaderDate;
    private CalendarMode _cachedHeaderMode = (CalendarMode)(-1);

    // Cached cell rects for hit testing
    private Rect[] _cellRects = Array.Empty<Rect>();
    private int _hotCellIndex = -1;
    private readonly List<CalendarCell> _cells = new();

    // Culture-formatted digit strings for day-of-month cells (index 0 = day 1). Native digits
    // (e.g. Arabic-indic) depend on culture, so the table is rebuilt only when culture changes.
    private string[] _cachedDayNumbers = Array.Empty<string>();
    private CultureInfo? _cachedDayNumbersCulture;

    // Abbreviated day-of-week header labels, ordered starting at FirstDayOfWeek and truncated to 2 chars.
    private string[] _cachedDayNames = Array.Empty<string>();
    private CultureInfo? _cachedDayNamesCulture;
    private DayOfWeek _cachedDayNamesFirstDayOfWeek;

    // Abbreviated month names for the year view (index 0 = January).
    private string[] _cachedMonthNames = Array.Empty<string>();
    private CultureInfo? _cachedMonthNamesCulture;

    // Year label strings for the decade view, keyed by the decade's start year.
    private string[] _cachedDecadeYearNumbers = Array.Empty<string>();
    private int _cachedDecadeStart = int.MinValue;
    private CultureInfo? _cachedDecadeYearNumbersCulture;

    // Small font for cell text
    internal const double CELL_FONT_SIZE = 11;
    private static readonly double DowFontSize = Math.Round(CELL_FONT_SIZE * 0.85);
    public static readonly MewProperty<DateTime?> SelectedDateProperty =
        MewProperty<DateTime?>.Register<Calendar>(nameof(SelectedDate), null,
            MewPropertyOptions.AffectsRender,
            static (self, oldValue, newValue) => self.OnSelectedDateChanged(oldValue, newValue));

    public static readonly MewProperty<DateTime> DisplayDateProperty =
        MewProperty<DateTime>.Register<Calendar>(nameof(DisplayDate), DateTime.Today,
            MewPropertyOptions.AffectsRender,
            static (self, _, _) =>
            {
                self.UpdateHeaderText();
                self.InvalidateArrange();
            });

    public static readonly MewProperty<CalendarMode> DisplayModeProperty =
        MewProperty<CalendarMode>.Register<Calendar>(nameof(DisplayMode), CalendarMode.Month,
            MewPropertyOptions.AffectsRender,
            static (self, oldValue, newValue) => self.OnDisplayModeChanged(oldValue, newValue));

    public static readonly MewProperty<DayOfWeek> FirstDayOfWeekProperty =
        MewProperty<DayOfWeek>.Register<Calendar>(nameof(FirstDayOfWeek), DayOfWeek.Sunday,
            MewPropertyOptions.AffectsRender,
            static (self, _, _) => self.InvalidateArrange());

    public static readonly MewProperty<bool> IsTodayHighlightedProperty =
        MewProperty<bool>.Register<Calendar>(nameof(IsTodayHighlighted), true,
            MewPropertyOptions.AffectsRender,
            static (self, _, _) => self.InvalidateArrange());

    static Calendar()
    {
        FocusableProperty.OverrideDefaultValue<Calendar>(true);
    }

    public Calendar()
    {
        _prevButton = new Button
        {
            Content = new GlyphElement { Kind = GlyphKind.ChevronLeft },
            MinWidth = NavButtonWidth,
            MinHeight = HeaderHeight,
            Padding = new Thickness(2),
        };
        _prevButton.StyleName = BuiltInStyles.FlatButton;
        _prevButton.Click += OnPrevClick;
        _prevButton.Parent = this;

        _nextButton = new Button
        {
            Content = new GlyphElement { Kind = GlyphKind.ChevronRight },
            MinWidth = NavButtonWidth,
            MinHeight = HeaderHeight,
            Padding = new Thickness(2),
        };
        _nextButton.StyleName = BuiltInStyles.FlatButton;
        _nextButton.Click += OnNextClick;
        _nextButton.Parent = this;

        _headerButton = new Button
        {
            Content = new TextBlock
            {
                FontWeight = FontWeight.SemiBold,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalTextAlignment = TextAlignment.Center,
            },
            MinHeight = HeaderHeight,
        };
        _headerButton.StyleName = BuiltInStyles.FlatButton;
        _headerButton.Click += OnHeaderClick;
        _headerButton.Parent = this;
        UpdateHeaderText();
    }

    /// <summary>Gets or sets the selected date.</summary>
    public DateTime? SelectedDate
    {
        get => GetValue(SelectedDateProperty);
        set => SetValue(SelectedDateProperty, value);
    }

    /// <summary>Gets or sets the month/year currently displayed.</summary>
    public DateTime DisplayDate
    {
        get => GetValue(DisplayDateProperty);
        set => SetValue(DisplayDateProperty, value);
    }

    /// <summary>Gets or sets the display mode (Month, Year, Decade).</summary>
    public CalendarMode DisplayMode
    {
        get => GetValue(DisplayModeProperty);
        set => SetValue(DisplayModeProperty, value);
    }

    /// <summary>Gets or sets the first day of the week.</summary>
    public DayOfWeek FirstDayOfWeek
    {
        get => GetValue(FirstDayOfWeekProperty);
        set => SetValue(FirstDayOfWeekProperty, value);
    }

    /// <summary>Gets or sets whether today's date is highlighted.</summary>
    public bool IsTodayHighlighted
    {
        get => GetValue(IsTodayHighlightedProperty);
        set => SetValue(IsTodayHighlightedProperty, value);
    }

    /// <summary>Raised when <see cref="SelectedDate"/> changes (keyboard navigation or click).</summary>
    public event Action<DateTime?>? SelectedDateChanged;

    // Internal commit signal for the DatePicker popup ("user picked a date, close"); not a public
    // feature (mirrors ListBox.ItemActivated).
    internal event Action<DateTime>? DateActivated;

    /// <summary>Raised when <see cref="DisplayMode"/> changes.</summary>
    public event Action<CalendarMode>? DisplayModeChanged;

    private void OnSelectedDateChanged(DateTime? oldValue, DateTime? newValue)
    {
        if (newValue.HasValue)
        {
            DisplayDate = newValue.Value;
        }

        InvalidateArrange();
        SelectedDateChanged?.Invoke(newValue);
    }

    private void OnDisplayModeChanged(CalendarMode oldValue, CalendarMode newValue)
    {
        UpdateHeaderText();
        InvalidateArrange();
        DisplayModeChanged?.Invoke(newValue);
    }

    #region Layout

    protected override Size MeasureContent(Size availableSize)
    {
        var border = BorderThickness > 0 ? new Thickness(BorderThickness) : Thickness.Zero;
        var slot = availableSize.Deflate(border).Deflate(Padding);

        double width = CellSpacing + (CellHeight + CellSpacing) * DaysPerWeek;
        double height = HeaderHeight + DayOfWeekHeaderHeight + (CellHeight + CellSpacing) * MonthRows + CellSpacing;

        // Measure nav buttons
        _prevButton.Measure(new Size(NavButtonWidth, HeaderHeight));
        _nextButton.Measure(new Size(NavButtonWidth, HeaderHeight));

        // The header gets the width it is arranged at, not the grid's, and the calendar widens when its text needs more.
        double headerSpace = double.IsFinite(slot.Width) ? Math.Max(0, slot.Width - NavButtonWidth * 2) : double.PositiveInfinity;
        _headerButton.Measure(new Size(headerSpace, HeaderHeight));
        width = Math.Max(width, _headerButton.DesiredSize.Width + NavButtonWidth * 2);
        // Layout: [Header] [Prev] [Next] - both nav buttons on the right

        return new Size(width, height).Inflate(Padding).Inflate(border);
    }

    private Rect GetInnerBounds()
    {
        var snapped = GetSnappedBorderBounds(Bounds);
        var border = BorderThickness > 0 ? new Thickness(BorderThickness) : Thickness.Zero;
        return snapped.Deflate(border).Deflate(Padding);
    }

    protected override void ArrangeContent(Rect bounds)
    {
        var inner = GetInnerBounds();
        double x = inner.X;
        double y = inner.Y;
        double w = inner.Width;

        // Header row: [Header Text] [Prev] [Next]
        double navTotal = NavButtonWidth * 2;
        _headerButton.Arrange(new Rect(x, y, Math.Max(0, w - navTotal), HeaderHeight));
        _prevButton.Arrange(new Rect(x + w - navTotal, y, NavButtonWidth, HeaderHeight));
        _nextButton.Arrange(new Rect(x + w - NavButtonWidth, y, NavButtonWidth, HeaderHeight));

        // Compute cell rects
        ComputeCellRects(inner);
        UpdateCells();
    }

    /// <summary>Gives every cell of the current view its rect, label and state.</summary>
    private void UpdateCells()
    {
        SyncCellCount(_cellRects.Length);

        var culture = CultureInfo.CurrentCulture;
        var display = DisplayDate;
        var selected = SelectedDate;
        var today = DateTime.Today;

        switch (DisplayMode)
        {
            case CalendarMode.Month:
            {
                var dayNumbers = GetDayNumbers(culture);
                var firstOfMonth = new DateTime(display.Year, display.Month, 1);
                int startDow = ((int)firstOfMonth.DayOfWeek - (int)FirstDayOfWeek + DaysPerWeek) % DaysPerWeek;
                var startDate = firstOfMonth.AddDays(-startDow);
                bool highlightToday = IsTodayHighlighted;
                for (int index = 0; index < _cells.Count; index++)
                {
                    var date = startDate.AddDays(index);
                    _cells[index].Update(
                        dayNumbers[date.Day - 1],
                        culture,
                        isSelected: selected.HasValue && date == selected.Value.Date,
                        isMarked: highlightToday && date == today,
                        isOutside: date.Month != display.Month || date.Year != display.Year,
                        circleInset: 0,
                        markInset: true);
                }

                break;
            }
            case CalendarMode.Year:
            {
                var monthNames = GetMonthNames(culture);
                for (int index = 0; index < _cells.Count; index++)
                {
                    int month = index + 1;
                    _cells[index].Update(
                        monthNames[index],
                        culture,
                        isSelected: selected.HasValue && selected.Value.Year == display.Year && selected.Value.Month == month,
                        isMarked: today.Year == display.Year && today.Month == month,
                        isOutside: false,
                        circleInset: 2,
                        markInset: false);
                }

                break;
            }
            case CalendarMode.Decade:
            {
                int decadeStart = display.Year / 10 * 10;
                var yearNumbers = GetDecadeYearNumbers(decadeStart, culture);
                for (int index = 0; index < _cells.Count; index++)
                {
                    // The decade view runs from the year before the decade to the year after it.
                    int year = decadeStart - 1 + index;
                    _cells[index].Update(
                        yearNumbers[index],
                        culture,
                        isSelected: selected.HasValue && selected.Value.Year == year,
                        isMarked: today.Year == year,
                        isOutside: year < decadeStart || year >= decadeStart + 10,
                        circleInset: 2,
                        markInset: false);
                }

                break;
            }
        }

        for (int index = 0; index < _cells.Count; index++)
        {
            var cell = _cells[index];
            var rect = _cellRects[index];
            cell.SetIsHot(index == _hotCellIndex && IsHotEligible(index));
            cell.Measure(rect.Size);
            cell.Arrange(rect);
        }
    }

    private void SyncCellCount(int count)
    {
        while (_cells.Count > count)
        {
            var last = _cells[^1];
            _cells.RemoveAt(_cells.Count - 1);
            last.Parent = null;
        }

        while (_cells.Count < count)
        {
            var cell = new CalendarCell();
            cell.Parent = this;
            _cells.Add(cell);
        }
    }

    /// <summary>Whether a cell takes the hover: the years either side of a decade do not.</summary>
    private bool IsHotEligible(int index)
        => DisplayMode != CalendarMode.Decade || (index > 0 && index < YearDecadeCells - 1);

    private void SetHotCell(int index)
    {
        if (index == _hotCellIndex)
        {
            return;
        }

        if ((uint)_hotCellIndex < (uint)_cells.Count)
        {
            _cells[_hotCellIndex].SetIsHot(false);
        }

        _hotCellIndex = index;
        if ((uint)index < (uint)_cells.Count && IsHotEligible(index))
        {
            _cells[index].SetIsHot(true);
        }
    }

    private void ComputeCellRects(Rect bounds)
    {
        var mode = DisplayMode;
        int cols = mode == CalendarMode.Month ? DaysPerWeek : YearDecadeCols;
        int rows = mode == CalendarMode.Month ? MonthRows : YearDecadeRows;
        int total = cols * rows;

        if (_cellRects.Length != total)
            _cellRects = new Rect[total];

        double dpiScale = GetDpi() / 96.0;
        double gridTop = bounds.Y + HeaderHeight + (mode == CalendarMode.Month ? DayOfWeekHeaderHeight : 0);
        double cellW = (bounds.Width - CellSpacing * (cols + 1)) / cols;
        double cellH = mode == CalendarMode.Month ? CellHeight : CellHeight * 2;

        for (int r = 0; r < rows; r++)
        {
            for (int c = 0; c < cols; c++)
            {
                double cx = bounds.X + CellSpacing + c * (cellW + CellSpacing);
                double cy = gridTop + CellSpacing + r * (cellH + CellSpacing);
                _cellRects[r * cols + c] = LayoutRounding.SnapBoundsRectToPixels(
                    new Rect(cx, cy, cellW, cellH), dpiScale);
            }
        }
    }

    #endregion

    #region Rendering

    protected override void OnRender(IGraphicsContext context)
    {
        DrawBackgroundAndBorder(context, Bounds, Background, BorderBrush, BorderThickness, Math.Max(0, CornerRadius));

        if (DisplayMode == CalendarMode.Month && _cellRects.Length == MonthCells)
        {
            RenderDayOfWeekHeaders(context);
        }
    }

    private void RenderDayOfWeekHeaders(IGraphicsContext context)
    {
        var palette = Theme.Palette;
        var inner = GetInnerBounds();
        double headerY = inner.Y + HeaderHeight;
        double cellW = _cellRects[0].Width;
        var culture = CultureInfo.CurrentCulture;
        var dayNames = GetDayNames(culture, FirstDayOfWeek);
        var factory = GetGraphicsFactory();

        for (int index = 0; index < DaysPerWeek; index++)
        {
            var rect = new Rect(
                inner.X + CellSpacing + index * (cellW + CellSpacing),
                headerY,
                cellW,
                DayOfWeekHeaderHeight);

            DrawCalendarText(context, this, factory, dayNames[index], rect, DowFontSize, palette.PlaceholderText, culture);
        }
    }

    private void UpdateHeaderText()
    {
        var display = DisplayDate;
        var mode = DisplayMode;

        // Skip if inputs haven't changed
        if (_cachedHeaderDate == display && _cachedHeaderMode == mode)
            return;

        _cachedHeaderDate = display;
        _cachedHeaderMode = mode;

        var label = (TextBlock)_headerButton.Content!;
        label.Text = mode switch
        {
            CalendarMode.Month => display.ToString("Y", CultureInfo.CurrentCulture),
            CalendarMode.Year => display.ToString("yyyy", CultureInfo.CurrentCulture),
            CalendarMode.Decade => $"{display.Year / 10 * 10}–{display.Year / 10 * 10 + 9}",
            _ => string.Empty,
        };
    }

    private string[] GetDayNumbers(CultureInfo culture)
    {
        if (_cachedDayNumbersCulture == culture && _cachedDayNumbers.Length == 31)
            return _cachedDayNumbers;

        var numbers = new string[31];
        for (int day = 1; day <= 31; day++)
            numbers[day - 1] = day.ToString(culture);

        _cachedDayNumbers = numbers;
        _cachedDayNumbersCulture = culture;
        return numbers;
    }

    private string[] GetDayNames(CultureInfo culture, DayOfWeek firstDayOfWeek)
    {
        if (_cachedDayNamesCulture == culture && _cachedDayNamesFirstDayOfWeek == firstDayOfWeek && _cachedDayNames.Length == DaysPerWeek)
            return _cachedDayNames;

        var names = new string[DaysPerWeek];
        for (int i = 0; i < DaysPerWeek; i++)
        {
            var dow = (DayOfWeek)(((int)firstDayOfWeek + i) % DaysPerWeek);
            string dayName = culture.DateTimeFormat.GetAbbreviatedDayName(dow);
            names[i] = dayName.Length > 2 ? dayName[..2] : dayName;
        }

        _cachedDayNames = names;
        _cachedDayNamesCulture = culture;
        _cachedDayNamesFirstDayOfWeek = firstDayOfWeek;
        return names;
    }

    private string[] GetMonthNames(CultureInfo culture)
    {
        if (_cachedMonthNamesCulture == culture && _cachedMonthNames.Length == 12)
            return _cachedMonthNames;

        var names = new string[12];
        for (int month = 1; month <= 12; month++)
            names[month - 1] = culture.DateTimeFormat.GetAbbreviatedMonthName(month);

        _cachedMonthNames = names;
        _cachedMonthNamesCulture = culture;
        return names;
    }

    private string[] GetDecadeYearNumbers(int decadeStart, CultureInfo culture)
    {
        if (_cachedDecadeStart == decadeStart && _cachedDecadeYearNumbersCulture == culture && _cachedDecadeYearNumbers.Length == YearDecadeCells)
            return _cachedDecadeYearNumbers;

        var numbers = new string[YearDecadeCells];
        for (int i = 0; i < YearDecadeCells; i++)
        {
            int year = decadeStart - 1 + i; // decade: -1 to +10, matches RenderDecadeView
            numbers[i] = year.ToString(culture);
        }

        _cachedDecadeYearNumbers = numbers;
        _cachedDecadeStart = decadeStart;
        _cachedDecadeYearNumbersCulture = culture;
        return numbers;
    }

    /// <summary>Draws a centered, clipped calendar label in the owner's font.</summary>
    internal static void DrawCalendarText(
        IGraphicsContext context,
        Control owner,
        IGraphicsFactory factory,
        string text,
        Rect bounds,
        double fontSize,
        Color color,
        CultureInfo culture)
    {
        var request = new TextLayoutRequest
        {
            Text = text.AsMemory(),
            Dpi = owner.GetDpi(),
            Paragraph = new TextParagraphStyle
            {
                MaxWidth = bounds.Width,
                Wrapping = TextWrapping.NoWrap,
                Alignment = TextAlignment.Center,
                Culture = culture
            },
            DefaultStyle = new TextRunStyle(
                owner.FontFamily,
                fontSize,
                owner.FontWeight,
                owner.FontStyle == FontStyle.Italic,
                Culture: culture)
        };
        var layout = factory.TextEngine.GetOrCreateLayout(
            request,
            TextLayoutCachePolicy.Content);
        var origin = new Point(bounds.X, bounds.Y + Math.Max(0, (bounds.Height - layout.MeasuredSize.Height) * 0.5));
        var options = new TextDrawOptions(color, Owner: owner);

        context.Save();
        try
        {
            context.IntersectClip(bounds);
            context.Text.Draw(layout, origin, in options);
        }
        finally
        {
            context.Restore();
        }
    }

    #endregion

    #region Navigation

    private void OnPrevClick()
    {
        switch (DisplayMode)
        {
            case CalendarMode.Month:
                DisplayDate = DisplayDate.AddMonths(-1);
                break;
            case CalendarMode.Year:
                DisplayDate = DisplayDate.AddYears(-1);
                break;
            case CalendarMode.Decade:
                DisplayDate = DisplayDate.AddYears(-10);
                break;
        }
    }

    private void OnNextClick()
    {
        switch (DisplayMode)
        {
            case CalendarMode.Month:
                DisplayDate = DisplayDate.AddMonths(1);
                break;
            case CalendarMode.Year:
                DisplayDate = DisplayDate.AddYears(1);
                break;
            case CalendarMode.Decade:
                DisplayDate = DisplayDate.AddYears(10);
                break;
        }
    }

    private void OnHeaderClick()
    {
        // Month → Year → Decade
        if (DisplayMode == CalendarMode.Month)
            DisplayMode = CalendarMode.Year;
        else if (DisplayMode == CalendarMode.Year)
            DisplayMode = CalendarMode.Decade;
    }

    #endregion

    #region Mouse

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        SetHotCell(HitTestCell(TransformFromRoot(e.Position)));
    }

    protected override void OnMouseLeave()
    {
        base.OnMouseLeave();
        SetHotCell(-1);
    }

    protected override void OnMouseDown(MouseEventArgs e)
    {
        base.OnMouseDown(e);
        if (e.Handled || e.Button != MouseButton.Left) return;

        Focus();

        int cellIndex = HitTestCell(TransformFromRoot(e.Position));
        if (cellIndex < 0) return;

        HandleCellActivated(cellIndex);
        e.Handled = true;
    }

    protected override UIElement? OnHitTest(Point point)
    {
        if (!IsVisible || !IsHitTestVisible || !IsEffectivelyEnabled)
            return null;

        // Let buttons handle their own hits
        var hit = _prevButton.HitTest(point) ?? _nextButton.HitTest(point) ?? _headerButton.HitTest(point);
        if (hit != null) return hit;

        if (Bounds.Contains(point)) return this;
        return null;
    }

    private int HitTestCell(Point point)
    {
        for (int i = 0; i < _cellRects.Length; i++)
        {
            if (_cellRects[i].Contains(point))
                return i;
        }
        return -1;
    }

    private void HandleCellActivated(int cellIndex)
    {
        switch (DisplayMode)
        {
            case CalendarMode.Month:
            {
                var firstOfMonth = new DateTime(DisplayDate.Year, DisplayDate.Month, 1);
                int startDow = ((int)firstOfMonth.DayOfWeek - (int)FirstDayOfWeek + DaysPerWeek) % DaysPerWeek;
                var date = firstOfMonth.AddDays(cellIndex - startDow);
                SelectedDate = date.Date;
                // Navigate to the selected date's month if different
                if (date.Month != DisplayDate.Month || date.Year != DisplayDate.Year)
                    DisplayDate = new DateTime(date.Year, date.Month, 1);
                DateActivated?.Invoke(date.Date);
                break;
            }
            case CalendarMode.Year:
            {
                int month = cellIndex + 1;
                if (month >= 1 && month <= 12)
                {
                    DisplayDate = new DateTime(DisplayDate.Year, month, 1);
                    DisplayMode = CalendarMode.Month;
                }
                break;
            }
            case CalendarMode.Decade:
            {
                int decadeStart = DisplayDate.Year / 10 * 10;
                int year = decadeStart - 1 + cellIndex;
                if (year >= 1 && year <= 9999)
                {
                    DisplayDate = new DateTime(year, DisplayDate.Month, 1);
                    DisplayMode = CalendarMode.Year;
                }
                break;
            }
        }
    }

    #endregion

    #region Keyboard

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        if (e.Handled) return;

        switch (DisplayMode)
        {
            case CalendarMode.Month:
                HandleMonthKeyDown(e);
                break;
            case CalendarMode.Year:
            case CalendarMode.Decade:
                HandleGridKeyDown(e);
                break;
        }
    }

    private void HandleMonthKeyDown(KeyEventArgs e)
    {
        var current = SelectedDate ?? DisplayDate;

        switch (e.Key)
        {
            case Key.Left:
                SelectedDate = current.AddDays(-1);
                e.Handled = true;
                break;
            case Key.Right:
                SelectedDate = current.AddDays(1);
                e.Handled = true;
                break;
            case Key.Up:
                SelectedDate = current.AddDays(-7);
                e.Handled = true;
                break;
            case Key.Down:
                SelectedDate = current.AddDays(7);
                e.Handled = true;
                break;
            case Key.PageUp:
                SelectedDate = current.AddMonths(-1);
                e.Handled = true;
                break;
            case Key.PageDown:
                SelectedDate = current.AddMonths(1);
                e.Handled = true;
                break;
            case Key.Home:
                SelectedDate = new DateTime(current.Year, current.Month, 1);
                e.Handled = true;
                break;
            case Key.End:
                SelectedDate = new DateTime(current.Year, current.Month, DateTime.DaysInMonth(current.Year, current.Month));
                e.Handled = true;
                break;
            case Key.Enter:
            case Key.Space:
                if (SelectedDate.HasValue)
                    DateActivated?.Invoke(SelectedDate.Value);
                e.Handled = true;
                break;
        }
    }

    private void HandleGridKeyDown(KeyEventArgs e)
    {
        int cols = YearDecadeCols;

        switch (e.Key)
        {
            case Key.Left:
                NavigateDisplayDate(-1);
                e.Handled = true;
                break;
            case Key.Right:
                NavigateDisplayDate(1);
                e.Handled = true;
                break;
            case Key.Up:
                NavigateDisplayDate(-cols);
                e.Handled = true;
                break;
            case Key.Down:
                NavigateDisplayDate(cols);
                e.Handled = true;
                break;
            case Key.Enter:
            case Key.Space:
                // Drill down
                if (DisplayMode == CalendarMode.Decade)
                    DisplayMode = CalendarMode.Year;
                else if (DisplayMode == CalendarMode.Year)
                    DisplayMode = CalendarMode.Month;
                e.Handled = true;
                break;
            case Key.Escape:
                // Drill up
                if (DisplayMode == CalendarMode.Month)
                    DisplayMode = CalendarMode.Year;
                else if (DisplayMode == CalendarMode.Year)
                    DisplayMode = CalendarMode.Decade;
                e.Handled = true;
                break;
        }
    }

    private void NavigateDisplayDate(int offset)
    {
        if (DisplayMode == CalendarMode.Year)
            DisplayDate = DisplayDate.AddMonths(offset);
        else if (DisplayMode == CalendarMode.Decade)
            DisplayDate = DisplayDate.AddYears(offset);
    }

    #endregion

    bool IVisualTreeHost.VisitChildren(Func<Element, bool> visitor)
    {
        if (!visitor(_prevButton) || !visitor(_headerButton) || !visitor(_nextButton))
        {
            return false;
        }

        foreach (var cell in _cells)
        {
            if (!visitor(cell))
            {
                return false;
            }
        }

        return true;
    }
}

/// <summary>
/// Specifies the display mode of a <see cref="Calendar"/> control.
/// </summary>
public enum CalendarMode
{
    /// <summary>Displays a month view with individual days.</summary>
    Month,

    /// <summary>Displays a year view with months.</summary>
    Year,

    /// <summary>Displays a decade view with years.</summary>
    Decade,
}
