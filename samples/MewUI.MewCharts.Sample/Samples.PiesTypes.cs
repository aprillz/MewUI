using Aprillz.MewUI;
using Aprillz.MewUI.MewCharts.Drawing;
using Aprillz.MewUI.MewCharts.Drawing.Geometries;
using Aprillz.MewUI.Text;

using LiveChartsCore;
using LiveChartsCore.Drawing;
using LiveChartsCore.Kernel;

// Row of the Pies/Nested sales table.
sealed class PieSalesRecord(string country, string zone, string name, double value)
{
    public string Country { get; set; } = country;
    public string Zone { get; set; } = zone;
    public string Name { get; set; } = name;
    public double Value { get; set; } = value;
}

// One series of Pies/Nested: its 3-slot values place it on the person, zone or country ring.
sealed class PieNestedData(string name, double?[] values, string color)
{
    public string Name { get; set; } = name;
    public double?[] Values { get; set; } = values;
    public string Color { get; set; } = color;
    public bool IsTotal => Name is "Brazil" or "Colombia" or "Mexico";
    public Func<ChartPoint, string> Formatter { get; } = point =>
        $"{name}{Environment.NewLine}{point.StackedValue!.Share:P2}";
}

// Pies/Icons SvgLabel: a data label that draws an SVG icon and, above it, the icon's name in bold.
sealed class PieSvgLabel : LabelGeometry
{
    // WhiteSmoke, as the upstream label paints.
    private static readonly Color _nameColor = new(245, 245, 245);

    private readonly SvgGeometry _icon = new();

    public string Name { get; set; } = string.Empty;

    public float Size { get; set; }

    public float NameSize { get; set; }

    public float NameGap { get; set; }

    public string? SvgString
    {
        get => _icon.SVGPath;
        set => _icon.SVGPath = value;
    }

    public override void Draw(MewDrawingContext context)
    {
        _icon.X = X;
        _icon.Y = Y;
        _icon.Width = Size;
        _icon.Height = Size;
        _icon.Draw(context);

        if (string.IsNullOrEmpty(Name)) return;

        var family = string.IsNullOrEmpty(MewChartsText.FontFamily) ? ThemeManager.DefaultMetrics.FontFamily : MewChartsText.FontFamily;
        var layout = context.Factory.TextEngine.GetOrCreateLayout(
            new TextLayoutRequest
            {
                Text = Name.AsMemory(),
                Dpi = 96,
                DefaultStyle = new TextRunStyle(family, Math.Max(1, NameSize * MewChartsText.FontScale), FontWeight.Bold),
            },
            TextLayoutCachePolicy.Content);

        // The upstream text is left aligned at the icon's left edge with its baseline above the icon.
        var baseline = layout.Lines.Count > 0 ? layout.Lines[0].Baseline : layout.MeasuredSize.Height;
        context.G.Text.Draw(layout, new Point(X, Y - NameGap - baseline), new TextDrawOptions(_nameColor));
    }
}

// Pies/Icons CustomPieSeries: a pie series whose data labels are PieSvgLabel icons.
sealed class PieIconSeries : CorePieSeries<double, DoughnutGeometry, PieSvgLabel, CircleGeometry>
{
    public PieIconSeries(string name, string icon, double value, float iconSize, float nameSize)
        : base(new[] { value })
    {
        Name = name;

        PointMeasured += point =>
        {
            var label = point.Label!;
            label.SvgString = icon;
            label.Name = name;
            label.Size = iconSize;
            label.NameSize = nameSize;
            label.NameGap = nameSize * 0.6f;
            label.TranslateTransform = new LvcPoint(-iconSize * 0.5f, -iconSize * 0.5f);
        };
    }
}

// SVG path data of the Pies/Icons browser logos, copied from the upstream sample.
static class PieIcons
{
    public static string Chrome { get; } =
        "M21.25 11.031h-10.063c-0.969 0-1.844 0.313-2.625 0.781-1.344 0.844-2.25 2.344-2.313 " +
        "4.031l-3.875-6.719c2.063-2.625 5.219-4.344 8.813-4.344 4.406 0 8.25 2.563 10.063 " +
        "6.25zM1.875 9.75l5 8.688c0.031 0.031 0.031 0.063 0.063 0.094 0.438 0.781 1.125 " +
        "1.406 1.938 1.844 0.719 0.344 1.469 0.594 2.313 0.594s1.625-0.25 2.344-0.594l-3.906 " +
        "6.719c-5.438-0.781-9.625-5.438-9.625-11.094 0-2.313 0.688-4.469 1.875-6.25zM13.813 " +
        "11.813h7.781c0.5 1.281 0.813 2.719 0.813 4.188 0 6.188-5.031 11.188-11.219 11.188-0.25 " +
        "0-0.469 0-0.719-0.031l5-8.625c0.031-0.031 0.031-0.063 0.063-0.094 0.406-0.75 0.625-1.563 " +
        "0.625-2.438v-0.156c-0.063-1.688-1-3.188-2.344-4.031zM7.469 16c0-2.094 1.625-3.75 3.719-3.75s3.75 " +
        "1.656 3.75 3.75-1.656 3.75-3.75 3.75-3.719-1.656-3.719-3.75z";

    public static string Safari { get; } =
        "M12 3.5C7.30558 3.5 3.5 7.30558 3.5 12C3.5 13.5799 3.93054 15.0576 4.68039 16.324C4.82109 16.5617 " +
        "4.74251 16.8683 4.5049 17.009C4.26729 17.1497 3.96061 17.0712 3.81992 16.8335C2.98125 15.4171 2.5 " +
        "13.764 2.5 12C2.5 6.75329 6.75329 2.5 12 2.5C17.2467 2.5 21.5 6.75329 21.5 12C21.5 17.2467 17.2467 " +
        "21.5 12 21.5C9.41171 21.5 7.06433 20.4642 5.3514 18.7857C5.24102 18.6776 5.18686 18.5244 5.20469 " +
        "18.3709C5.22251 18.2174 5.31033 18.0808 5.44256 18.0008L13.111 13.3621L16.0946 8.3816L10.9439 " +
        "11.1098L7.81033 15.6802C7.65418 15.908 7.34296 15.966 7.11521 15.8098C6.88746 15.6537 6.82941 15.3425 " +
        "6.98557 15.1147L10.1887 10.4428C10.2344 10.3762 10.2956 10.3216 10.367 10.2837L17.2108 6.65874C17.4091 " +
        "6.5537 17.6531 6.59365 17.8076 6.75646C17.962 6.91926 17.9891 7.16502 17.8737 7.35754L13.905 " +
        "13.9825C13.8631 14.0525 13.8047 14.1112 13.7349 14.1534L6.5337 18.5095C8.01177 19.7521 9.91814 " +
        "20.5 12 20.5C16.6944 20.5 20.5 16.6944 20.5 12C20.5 7.30558 16.6944 3.5 12 3.5Z";

    public static string Edge { get; } =
        "M10.1836 13.436H20.098C20.098 9.91613 18.5621 7.51636 14.3743 7.51636C9.42537 7.51634 4.3926 10.7381 " +
        "2 14.7553C2.74111 7.78266 7.74053 2.15332 15.2673 2.15332C21.7186 2.15332 27.8309 7.08585 27.8309 " +
        "15.2853V18.4026H10.2364C10.2364 22.6456 13.9377 24.3267 17.8142 24.3267C22.5317 24.3267 25.4532 22.2068 " +
        "25.4532 22.2068V28.15C25.4532 28.15 22.1552 30.1533 16.8985 30.1533C10.0672 30.1533 5.24517 25.2576 " +
        "5.24517 19.1697C5.24517 14.3911 8.13564 10.5759 12.1393 9.00165C10.1904 11.142 10.1835 13.436 10.1835 " +
        "13.436H10.1836Z";
}
