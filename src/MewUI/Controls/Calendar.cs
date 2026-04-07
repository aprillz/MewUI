using System.Globalization;

using Aprillz.MewUI.Rendering;

namespace Aprillz.MewUI.Controls;

/// <summary>
/// A calendar control that displays a month/year/decade view for date selection.
/// Can be used standalone or as popup content for a DatePicker.
/// </summary>
public sealed class Calendar : Control, IVisualTreeHost
{
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
    private const double CellCornerRadius = 4;
    private const double TodayStrokeThickness = 1;

    // Navigation buttons (FlatButton style)
    private readonly Button _prevButton;
    private readonly Button _nextButton;
    private readonly Button _headerButton; // "March 2026" — click to switch mode

    // Cached cell rects for hit testing
    private Rect[] _cellRects = Array.Empty<Rect>();
    private int _hotCellIndex = -1;

    // Small font for cell text
    private const double CellFontSize = 11;
    private static readonly double DowFontSize = Math.Round(CellFontSize * 0.85);
    private IFont? _cellFont;
    private IFont? _dowFont;
    private uint _cellFontDpi;

    public static readonly MewProperty<DateTime?> SelectedDateProperty =
        MewProperty<DateTime?>.Register<Calendar>(nameof(SelectedDate), null,
            MewPropertyOptions.AffectsRender,
            static (self, oldValue, newValue) => self.OnSelectedDateChanged(oldValue, newValue));

    public static readonly MewProperty<DateTime> DisplayDateProperty =
        MewProperty<DateTime>.Register<Calendar>(nameof(DisplayDate), DateTime.Today,
            MewPropertyOptions.AffectsRender);

    public static readonly MewProperty<CalendarMode> DisplayModeProperty =
        MewProperty<CalendarMode>.Register<Calendar>(nameof(DisplayMode), CalendarMode.Month,
            MewPropertyOptions.AffectsRender,
            static (self, oldValue, newValue) => self.OnDisplayModeChanged(oldValue, newValue));

    public static readonly MewProperty<DayOfWeek> FirstDayOfWeekProperty =
        MewProperty<DayOfWeek>.Register<Calendar>(nameof(FirstDayOfWeek), DayOfWeek.Sunday,
            MewPropertyOptions.AffectsRender);

    public static readonly MewProperty<bool> IsTodayHighlightedProperty =
        MewProperty<bool>.Register<Calendar>(nameof(IsTodayHighlighted), true,
            MewPropertyOptions.AffectsRender);

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

    public override bool Focusable => true;

    /// <summary>Raised when <see cref="SelectedDate"/> changes (keyboard navigation or click).</summary>
    public event Action<DateTime?>? SelectedDateChanged;

    /// <summary>Raised when a date is activated by mouse click or Enter key (commit action).</summary>
    public event Action<DateTime>? DateActivated;

    /// <summary>Raised when <see cref="DisplayMode"/> changes.</summary>
    public event Action<CalendarMode>? DisplayModeChanged;

    private void OnSelectedDateChanged(DateTime? oldValue, DateTime? newValue)
    {
        if (newValue.HasValue)
        {
            DisplayDate = newValue.Value;
        }

        SelectedDateChanged?.Invoke(newValue);
    }

    private void OnDisplayModeChanged(CalendarMode oldValue, CalendarMode newValue)
    {
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
        _headerButton.Measure(new Size(Math.Max(0, width - NavButtonWidth * 2), HeaderHeight));
        // Layout: [Header] [Prev] [Next] — both nav buttons on the right

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

    private IFont GetCellFont()
    {
        var dpi = GetDpi();
        if (_cellFont != null && _cellFontDpi == dpi)
            return _cellFont;

        var factory = GetGraphicsFactory();
        _cellFont?.Dispose();
        _cellFont = factory.CreateFont(FontFamily, CellFontSize, dpi, FontWeight);
        _dowFont?.Dispose();
        _dowFont = factory.CreateFont(FontFamily, DowFontSize, dpi, FontWeight);
        _cellFontDpi = dpi;
        return _cellFont;
    }

    private IFont GetDowFont()
    {
        GetCellFont(); // ensure both fonts are created
        return _dowFont!;
    }

    #endregion

    #region Rendering

    protected override void OnRender(IGraphicsContext context)
    {
        DrawBackgroundAndBorder(context, Bounds, Background, BorderBrush, BorderThickness, Math.Max(0, CornerRadius));
    }

    protected override void RenderSubtree(IGraphicsContext context)
    {
        // Ensure cell rects match current display mode (mode may change between Arrange and Render).
        ComputeCellRects(GetInnerBounds());

        // Update header text
        UpdateHeaderText();

        // Render header buttons
        _prevButton.Render(context);
        _headerButton.Render(context);
        _nextButton.Render(context);

        // Render grid
        switch (DisplayMode)
        {
            case CalendarMode.Month:
                RenderMonthView(context);
                break;
            case CalendarMode.Year:
                RenderYearView(context);
                break;
            case CalendarMode.Decade:
                RenderDecadeView(context);
                break;
        }
    }

    private void UpdateHeaderText()
    {
        var display = DisplayDate;
        var label = (TextBlock)_headerButton.Content!;

        label.Text = DisplayMode switch
        {
            CalendarMode.Month => display.ToString("Y", CultureInfo.CurrentCulture),
            CalendarMode.Year => display.ToString("yyyy", CultureInfo.CurrentCulture),
            CalendarMode.Decade => $"{display.Year / 10 * 10}–{display.Year / 10 * 10 + 9}",
            _ => string.Empty,
        };
    }

    private void RenderMonthView(IGraphicsContext context)
    {
        var theme = Theme;
        var palette = theme.Palette;
        var font = GetCellFont();
        var today = DateTime.Today;
        double dpiScale = GetDpi() / 96.0;
        double snappedRadius = LayoutRounding.RoundToPixel(CellCornerRadius, dpiScale);
        double snappedStroke = LayoutRounding.SnapThicknessToPixels(TodayStrokeThickness, dpiScale, 1);
        var display = DisplayDate;
        var selected = SelectedDate;

        // Day of week headers
        var inner = GetInnerBounds();
        double headerY = inner.Y + HeaderHeight;
        double cellW = _cellRects.Length > 0 ? _cellRects[0].Width : 0;
        var fdow = FirstDayOfWeek;

        for (int i = 0; i < DaysPerWeek; i++)
        {
            var dow = (DayOfWeek)(((int)fdow + i) % DaysPerWeek);
            string dayName = CultureInfo.CurrentCulture.DateTimeFormat.GetAbbreviatedDayName(dow);
            if (dayName.Length > 2) dayName = dayName[..2];

            var rect = new Rect(
                inner.X + CellSpacing + i * (cellW + CellSpacing),
                headerY,
                cellW,
                DayOfWeekHeaderHeight);

            context.DrawText(dayName, rect, GetDowFont(), palette.PlaceholderText,
                TextAlignment.Center, TextAlignment.Center);
        }

        // Day cells
        var firstOfMonth = new DateTime(display.Year, display.Month, 1);
        int startDow = ((int)firstOfMonth.DayOfWeek - (int)fdow + DaysPerWeek) % DaysPerWeek;
        var startDate = firstOfMonth.AddDays(-startDow);

        for (int i = 0; i < MonthCells; i++)
        {
            var date = startDate.AddDays(i);
            var cellRect = _cellRects[i];
            bool isCurrentMonth = date.Month == display.Month && date.Year == display.Year;
            bool isToday = IsTodayHighlighted && date == today;
            bool isSelected = selected.HasValue && date == selected.Value.Date;
            bool isHot = i == _hotCellIndex;

            // Background
            if (isSelected)
            {
                context.FillRoundedRectangle(cellRect, snappedRadius, snappedRadius, palette.Accent);
            }
            else if (isHot)
            {
                context.FillRoundedRectangle(cellRect, snappedRadius, snappedRadius, palette.AccentHoverOverlay);
            }

            // Today ring
            if (isToday && !isSelected)
            {
                context.DrawRoundedRectangle(cellRect, snappedRadius, snappedRadius, palette.Accent, snappedStroke);
            }

            // Text
            var textColor = isSelected
                ? palette.AccentText
                : isCurrentMonth
                    ? palette.WindowText
                    : palette.DisabledText;

            context.DrawText(date.Day.ToString(), cellRect, font, textColor,
                TextAlignment.Center, TextAlignment.Center);
        }
    }

    private void RenderYearView(IGraphicsContext context)
    {
        var theme = Theme;
        var palette = theme.Palette;
        var font = GetCellFont();
        var display = DisplayDate;
        var selected = SelectedDate;
        double dpiScale = GetDpi() / 96.0;
        double snappedRadius = LayoutRounding.RoundToPixel(CellCornerRadius, dpiScale);
        double snappedStroke = LayoutRounding.SnapThicknessToPixels(TodayStrokeThickness, dpiScale, 1);

        for (int i = 0; i < YearDecadeCells; i++)
        {
            var cellRect = _cellRects[i];
            int month = i + 1;
            bool isSelected = selected.HasValue &&
                              selected.Value.Year == display.Year &&
                              selected.Value.Month == month;
            bool isHot = i == _hotCellIndex;
            bool isCurrent = DateTime.Today.Year == display.Year && DateTime.Today.Month == month;

            if (isSelected)
            {
                context.FillRoundedRectangle(cellRect, snappedRadius, snappedRadius, palette.Accent);
            }
            else if (isHot)
            {
                context.FillRoundedRectangle(cellRect, snappedRadius, snappedRadius, palette.AccentHoverOverlay);
            }

            if (isCurrent && !isSelected)
            {
                context.DrawRoundedRectangle(cellRect, snappedRadius, snappedRadius, palette.Accent, snappedStroke);
            }

            var textColor = isSelected ? palette.AccentText : palette.WindowText;
            string label = CultureInfo.CurrentCulture.DateTimeFormat.GetAbbreviatedMonthName(month);

            context.DrawText(label, cellRect, font, textColor,
                TextAlignment.Center, TextAlignment.Center);
        }
    }

    private void RenderDecadeView(IGraphicsContext context)
    {
        var theme = Theme;
        var palette = theme.Palette;
        var font = GetCellFont();
        var display = DisplayDate;
        var selected = SelectedDate;
        int decadeStart = display.Year / 10 * 10;
        double dpiScale = GetDpi() / 96.0;
        double snappedRadius = LayoutRounding.RoundToPixel(CellCornerRadius, dpiScale);
        double snappedStroke = LayoutRounding.SnapThicknessToPixels(TodayStrokeThickness, dpiScale, 1);

        for (int i = 0; i < YearDecadeCells; i++)
        {
            var cellRect = _cellRects[i];
            int year = decadeStart - 1 + i; // decade: -1 to +10
            bool isInDecade = year >= decadeStart && year < decadeStart + 10;
            bool isSelected = selected.HasValue && selected.Value.Year == year;
            bool isHot = i == _hotCellIndex;
            bool isCurrent = DateTime.Today.Year == year;

            if (isSelected)
            {
                context.FillRoundedRectangle(cellRect, snappedRadius, snappedRadius, palette.Accent);
            }
            else if (isHot && isInDecade)
            {
                context.FillRoundedRectangle(cellRect, snappedRadius, snappedRadius, palette.AccentHoverOverlay);
            }

            if (isCurrent && !isSelected)
            {
                context.DrawRoundedRectangle(cellRect, snappedRadius, snappedRadius, palette.Accent, snappedStroke);
            }

            var textColor = isSelected
                ? palette.AccentText
                : isInDecade
                    ? palette.WindowText
                    : palette.DisabledText;

            context.DrawText(year.ToString(), cellRect, font, textColor,
                TextAlignment.Center, TextAlignment.Center);
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
        int newHot = HitTestCell(e.Position);
        if (newHot != _hotCellIndex)
        {
            _hotCellIndex = newHot;
            InvalidateVisual();
        }
    }

    protected override void OnMouseLeave()
    {
        base.OnMouseLeave();
        if (_hotCellIndex >= 0)
        {
            _hotCellIndex = -1;
            InvalidateVisual();
        }
    }

    protected override void OnMouseDown(MouseEventArgs e)
    {
        base.OnMouseDown(e);
        if (e.Handled || e.Button != MouseButton.Left) return;

        Focus();

        int cellIndex = HitTestCell(e.Position);
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
        => visitor(_prevButton) && visitor(_nextButton) && visitor(_headerButton);
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
