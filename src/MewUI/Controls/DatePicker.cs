using System.Globalization;
using Aprillz.MewUI.Rendering;

namespace Aprillz.MewUI.Controls;

/// <summary>
/// A drop-down control that allows selecting a date via an embedded <see cref="Calendar"/>.
/// </summary>
public sealed class DatePicker : DropDownBase
{
    private Calendar? _calendar;
    private DateOnly? _cachedHeaderDate;
    private string? _cachedHeaderFormat;
    private string? _cachedHeaderText;

    public static readonly MewProperty<DateOnly?> SelectedDateProperty =
        MewProperty<DateOnly?>.Register<DatePicker>(nameof(SelectedDate), null,
            MewPropertyOptions.AffectsRender | MewPropertyOptions.BindsTwoWayByDefault,
            static (self, oldValue, newValue) => self.OnSelectedDatePropertyChanged(oldValue, newValue));

    public static readonly MewProperty<string> PlaceholderProperty =
        MewProperty<string>.Register<DatePicker>(nameof(Placeholder), string.Empty, MewPropertyOptions.AffectsRender);

    public static readonly MewProperty<string> DateFormatProperty =
        MewProperty<string>.Register<DatePicker>(nameof(DateFormat), "yyyy-MM-dd", MewPropertyOptions.AffectsRender);

    public static readonly MewProperty<DayOfWeek> FirstDayOfWeekProperty =
        MewProperty<DayOfWeek>.Register<DatePicker>(nameof(FirstDayOfWeek), DayOfWeek.Sunday);

    public static readonly MewProperty<CultureInfo?> DisplayCultureProperty =
        MewProperty<CultureInfo?>.Register<DatePicker>(nameof(DisplayCulture), null,
            MewPropertyOptions.AffectsRender,
            static (self, _, _) => self._cachedHeaderText = null);

    public static readonly MewProperty<bool> UseGregorianCalendarProperty =
        MewProperty<bool>.Register<DatePicker>(nameof(UseGregorianCalendar), false,
            MewPropertyOptions.AffectsRender,
            static (self, _, _) => self._cachedHeaderText = null);

    /// <summary>Gets or sets the selected date.</summary>
    public DateOnly? SelectedDate
    {
        get => GetValue(SelectedDateProperty);
        set => SetValue(SelectedDateProperty, value);
    }

    /// <summary>Gets or sets the placeholder text shown when no date is selected.</summary>
    public string Placeholder
    {
        get => GetValue(PlaceholderProperty);
        set => SetValue(PlaceholderProperty, value ?? string.Empty);
    }

    /// <summary>Gets or sets the date display format string.</summary>
    public string DateFormat
    {
        get => GetValue(DateFormatProperty);
        set => SetValue(DateFormatProperty, value ?? "yyyy-MM-dd");
    }

    /// <summary>Gets or sets the first day of the week for the calendar popup.</summary>
    public DayOfWeek FirstDayOfWeek
    {
        get => GetValue(FirstDayOfWeekProperty);
        set => SetValue(FirstDayOfWeekProperty, value);
    }

    /// <summary>
    /// Gets or sets the culture used for display (month/day names, number formatting).
    /// If <see langword="null"/>, <see cref="CultureInfo.CurrentCulture"/> is used.
    /// </summary>
    public CultureInfo? DisplayCulture
    {
        get => GetValue(DisplayCultureProperty);
        set => SetValue(DisplayCultureProperty, value);
    }

    /// <summary>
    /// Gets or sets whether the Gregorian calendar is used for date calculations,
    /// regardless of the <see cref="DisplayCulture"/> setting.
    /// When <see langword="false"/> (default), the calendar of <see cref="DisplayCulture"/>
    /// (or <see cref="CultureInfo.CurrentCulture"/>) is used.
    /// </summary>
    public bool UseGregorianCalendar
    {
        get => GetValue(UseGregorianCalendarProperty);
        set => SetValue(UseGregorianCalendarProperty, value);
    }

    /// <summary>Raised when the selected date changes.</summary>
    public event Action<DateOnly?>? SelectedDateChanged;

    private void OnSelectedDatePropertyChanged(DateOnly? oldValue, DateOnly? newValue)
    {
        if (_calendar != null)
            _calendar.SelectedDate = newValue;

        SelectedDateChanged?.Invoke(newValue);
    }

    protected override UIElement CreatePopupContent()
    {
        _calendar = new Calendar
        {
            FirstDayOfWeek = FirstDayOfWeek,
            DisplayCulture = DisplayCulture,
            UseGregorianCalendar = UseGregorianCalendar,
            StyleName = BuiltInStyles.DatePickerPopup,
        };

        if (SelectedDate.HasValue)
        {
            _calendar.SelectedDate = SelectedDate;
            _calendar.DisplayDate = SelectedDate.Value;
        }

        _calendar.SelectedDateChanged += OnCalendarSelectedDateChanged;
        _calendar.DateActivated += OnCalendarDateActivated;
        return _calendar;
    }

    protected override void SyncPopupContent(UIElement popup)
    {
        // Only sync lightweight properties that the owner may change while open.
        // Avoid overwriting DisplayDate/DisplayMode/SelectedDate — the Calendar
        // manages those internally (nav buttons, mode drill-down, etc.) and
        // SyncPopupContent is called every render frame via UpdatePopupBoundsCore.
        if (_calendar == null) return;

        _calendar.FirstDayOfWeek = FirstDayOfWeek;
        _calendar.DisplayCulture = DisplayCulture;
        _calendar.UseGregorianCalendar = UseGregorianCalendar;
    }

    protected override void OnIsDropDownOpenChanged(bool oldValue, bool newValue)
    {
        if (newValue && _calendar != null)
        {
            // Sync full state only when opening
            if (SelectedDate.HasValue)
            {
                _calendar.SelectedDate = SelectedDate;
                _calendar.DisplayDate = SelectedDate.Value;
            }

            _calendar.DisplayMode = CalendarMode.Month;
        }

        base.OnIsDropDownOpenChanged(oldValue, newValue);
    }

    protected override UIElement GetPopupFocusTarget(UIElement popup) => popup;

    private void OnCalendarSelectedDateChanged(DateOnly? date)
    {
        // Sync value during navigation (keyboard arrows) without closing popup.
        if (date.HasValue)
        {
            SelectedDate = date;
        }
    }

    private void OnCalendarDateActivated(DateOnly date)
    {
        // Commit action (mouse click or Enter key) — close popup.
        SelectedDate = date;
        IsDropDownOpen = false;
    }

    protected override Size MeasureHeader(Size availableSize)
    {
        var headerHeight = ResolveHeaderHeight();

        // Measure a representative date string to determine width
        string sample = DateOnly.FromDateTime(DateTime.Today).ToString(DateFormat);
        using var measure = BeginTextMeasurement();
        var textSize = measure.Context.MeasureText(sample, measure.Font);

        double width = textSize.Width + ArrowAreaWidth;
        return new Size(width, headerHeight);
    }

    protected override void RenderHeaderContent(IGraphicsContext context, Rect headerRect, Rect innerHeaderRect)
    {
        var textRect = new Rect(
            innerHeaderRect.X,
            innerHeaderRect.Y,
            innerHeaderRect.Width - ArrowAreaWidth,
            innerHeaderRect.Height).Deflate(Padding);

        var state = CurrentVisualState;
        string? text = null;
        Color textColor = default;

        if (SelectedDate.HasValue)
        {
            var date = SelectedDate.Value;
            var fmt = DateFormat;
            var baseCulture = DisplayCulture ?? CultureInfo.CurrentCulture;
            if (_cachedHeaderText == null || _cachedHeaderDate != date || _cachedHeaderFormat != fmt)
            {
                _cachedHeaderDate = date;
                _cachedHeaderFormat = fmt;
                CultureInfo formatCulture;
                if (UseGregorianCalendar && baseCulture.Calendar is not System.Globalization.GregorianCalendar)
                {
                    var cloned = (CultureInfo)baseCulture.Clone();
                    cloned.DateTimeFormat.Calendar = new System.Globalization.GregorianCalendar();
                    formatCulture = cloned;
                }
                else
                {
                    formatCulture = baseCulture;
                }
                _cachedHeaderText = date.ToDateTime(TimeOnly.MinValue).ToString(fmt, formatCulture);
            }
            text = _cachedHeaderText;
            textColor = state.IsEnabled ? Foreground : Theme.Palette.DisabledText;
        }
        else if (!string.IsNullOrEmpty(Placeholder) && !state.IsFocused)
        {
            text = Placeholder;
            textColor = Theme.Palette.PlaceholderText;
        }

        if (!string.IsNullOrEmpty(text))
        {
            context.DrawText(text, textRect, GetFont(), textColor,
                TextAlignment.Left, TextAlignment.Center, TextWrapping.NoWrap);
        }
    }

    protected override Rect CalculatePopupBounds(Window window, UIElement popup)
    {
        var bounds = Bounds;
        var client = window.ClientSize;

        // Calendar measures itself — use its desired size
        popup.Measure(new Size(client.Width, client.Height));
        double popupW = Math.Max(popup.DesiredSize.Width, bounds.Width);
        double popupH = popup.DesiredSize.Height;

        double x = bounds.X;
        if (x + popupW > client.Width)
            x = Math.Max(0, client.Width - popupW);

        double belowY = bounds.Y + ResolveHeaderHeight();
        double availableBelow = Math.Max(0, client.Height - belowY);
        double availableAbove = Math.Max(0, bounds.Y);

        double y;
        if (availableBelow >= popupH || availableBelow >= availableAbove)
        {
            y = belowY;
            popupH = Math.Min(popupH, availableBelow);
        }
        else
        {
            popupH = Math.Min(popupH, availableAbove);
            y = bounds.Y - popupH;
        }

        return new Rect(x, y, popupW, popupH);
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        if (!IsEffectivelyEnabled || e.Handled)
        {
            base.OnKeyDown(e);
            return;
        }

        // Allow Escape in Calendar to drill up modes before closing the dropdown
        if (e.Key == Key.Escape && IsDropDownOpen && _calendar != null && _calendar.DisplayMode != CalendarMode.Month)
        {
            // Let Calendar handle it (mode drill-up)
            return;
        }

        base.OnKeyDown(e);
    }
}
