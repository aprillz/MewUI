using Aprillz.MewUI;
using Aprillz.MewUI.Controls;

using static Samples;

// A gallery faithful to the LiveCharts2 samples, in the order of the upstream sample index, rendered
// entirely through MewUI's IGraphicsContext backend (no SkiaSharp). Builders live in the Samples.*.cs files.

Startup();

var gallery = new ScrollViewer()
    .NoHorizontalScroll()
    .AutoVerticalScroll()
    .Content(
        new WrapPanel()
            .Spacing(16)
            .Children(
                Section("General / FirstChart", GeneralFirstChart()),
                Section("Design / LinearGradients", DesignLinearGradients()),
                Section("Design / RadialGradients", DesignRadialGradients()),
                Section("Design / StrokeDashArray", DesignStrokeDashArray()),
                Section("Lines / Basic", LinesBasic()),
                Section("Lines / AutoUpdate", LinesAutoUpdate()),
                Section("Lines / Straight", LinesStraight()),
                Section("Lines / Properties", LinesProperties()),
                Section("Lines / Area", LinesArea()),
                Section("Lines / Custom", LinesCustom()),
                Section("Lines / CustomPoints", LinesCustomPoints()),
                Section("Lines / Padding", LinesPadding()),
                Section("Lines / XY", LinesXY()),
                Section("Lines / Zoom", LinesZoom()),
                Section("Bars / Basic", BarsBasic()),
                Section("Bars / AutoUpdate", BarsAutoUpdate()),
                Section("Bars / Custom", BarsCustom()),
                Section("Bars / WithBackground", BarsWithBackground()),
                Section("Bars / Spacing", BarsSpacing()),
                Section("Bars / DelayedAnimation", BarsDelayedAnimation()),
                Section("Bars / Race", BarsRace()),
                Section("Bars / RowsWithLabels", BarsRows()),
                Section("Bars / Layered", BarsLayered()),
                Section("Pies / Basic", PiesBasic()),
                Section("Pies / AutoUpdate", PiesAutoUpdate()),
                Section("Pies / Doughnut", PiesDoughnut()),
                Section("Pies / Pushout", PiesPushout()),
                Section("Pies / Nested", PiesNested()),
                Section("Pies / Custom", PiesCustom()),
                Section("Pies / Icons", PiesIcons()),
                Section("Pies / OutLabels", PiesOutLabels()),
                Section("Pies / NightingaleRose", PiesNightingale()),
                Section("Pies / Gauge", PiesGauge()),
                Section("Pies / Basic gauge", PiesGauge1()),
                Section("Pies / 270 degrees gauge", PiesGauge2()),
                Section("Pies / Multiple values gauge", PiesGauge3()),
                Section("Pies / Slim gauge", PiesGauge4()),
                Section("Pies / Auto updates on gauges", PiesGauge5()),
                Section("Pies / Angular gauge", AngularGaugeInteractive()),
                Section("Scatter / Basic", ScatterBasic()),
                Section("Scatter / Bubbles", ScatterBubbles()),
                Section("Scatter / AutoUpdate", ScatterAutoUpdate()),
                Section("Scatter / Custom", ScatterCustom()),
                Section("StackedArea / Basic", StackedAreaBasic()),
                Section("StackedArea / StepArea", StackedStepArea()),
                Section("StackedBars / Basic", StackedBarsBasic()),
                Section("StackedBars / Groups", StackedBarsGroups()),
                Section("Financial / BasicCandlesticks", FinancialCandlesticks()),
                Section("Error / Basic", ErrorBasic(), WIDE_CELL_WIDTH, 480),
                Section("Box / Basic", BoxBasic()),
                Section("Heat / Basic", HeatBasic()),
                Section("StepLines / Basic", StepLinesBasic()),
                Section("StepLines / AutoUpdate", StepLinesAutoUpdate()),
                Section("StepLines / Properties", StepLinesProperties()),
                Section("StepLines / Area", StepLinesArea()),
                Section("StepLines / Custom", StepLinesCustom()),
                Section("StepLines / Zoom", StepLinesZoom()),
                Section("Polar / Basic", PolarBasic()),
                Section("Polar / RadialArea", PolarRadialArea()),
                Section("Polar / Coordinates", PolarCoordinates()),
                Section("Polar / Test", PolarTest()),
                Section("Axes / LabelsFormat", AxesLabelsFormat()),
                Section("Axes / LabelsFormat2", AxesLabelsFormat2()),
                Section("Axes / NamedLabels", AxesNamedLabels()),
                Section("Axes / LabelsRotation", AxesLabelsRotation()),
                Section("Axes / Multiple", AxesMultiple(), WIDE_CELL_WIDTH, 300),
                Section("Axes / Shared", AxesShared(), WIDE_CELL_WIDTH, 360),
                Section("Axes / ColorsAndPosition", AxesColorsAndPosition()),
                Section("Axes / Crosshairs", AxesCrosshairs()),
                Section("Axes / CustomSeparatorsInterval", AxesCustomSeparators()),
                Section("Axes / DateTimeScaled", AxesDateTimeScaled()),
                Section("Axes / TimeSpanScaled", AxesTimeSpanScaled()),
                Section("Axes / Logarithmic", AxesLogarithmic()),
                Section("Axes / Style", AxesStyle()),
                Section("Axes / MatchScale", AxesMatchScale()),
                Section("Axes / Paging", AxesPaging()),
                Section("Events / Tutorial", EventsTutorialInteractive()),
                Section("Events / AddPointOnClick", EventsAddPointOnClickInteractive()),
                Section("Events / OverrideFind", EventsOverrideFind()),
                Section("General / MapPoints", GeneralMapPoints()),
                Section("General / RealTime", RealTime()),
                Section("General / Scrollable", GeneralScrollableInteractive()),
                Section("General / Sections", GeneralSections()),
                Section("General / Sections2", GeneralSections2()),
                Section("General / ConditionalDraw", GeneralConditionalDraw()),
                Section("General / VisualElements", GeneralVisualElements()),
                Section("General / ChartToImage", GeneralChartToImage()),
                Section("General / Tooltips", GeneralTooltips()),
                Section("General / TooltipHoverArea", GeneralTooltipHoverArea()),
                Section("General / Legends", GeneralLegends(), WIDE_CELL_WIDTH, 380),
                Section("General / Visibility", GeneralVisibilityInteractive()),
                Section("General / TemplatedTooltips", GeneralTemplatedTooltips()),
                Section("General / TemplatedLegends", GeneralTemplatedLegends()),
                Section("General / UserDefinedTypes", GeneralUserDefinedTypes()),
                Section("General / NullPoints", NullPoints()),
                Section("General / MultiThreading", GeneralMultiThreading()),
                Section("General / MultiThreading2", GeneralMultiThreading2()),
                Section("General / DrawOnCanvas", GeneralDrawOnCanvas())
            )
    );

Window window = null!;

var root = new Window()
    .Resizable(1280, 760)
    .StartCenterScreen()
    .Build(x => x
        .Ref(out window)
        .Title("Aprillz.MewUI.MewCharts Sample")
        .OnLoaded(() =>
        {
            window.Content = gallery;
        }));

Application.Run(root);

static void Startup()
{
    var args = Environment.GetCommandLineArgs();

#if MEWUI_GALLERY_WIN
#pragma warning disable CA1416
    Win32Platform.Register();
    Direct2DBackend.Register();
#pragma warning restore CA1416
#elif MEWUI_GALLERY_OSX
    MacOSPlatform.Register();
    MewVGMacOSBackend.Register();
#elif MEWUI_GALLERY_LINUX
    X11Platform.Register();
    MewVGX11Backend.Register();
#else
    if (OperatingSystem.IsWindows())
    {
        Win32Platform.Register();

        if (args.Any(a => a is "--gdi"))
        {
            GdiBackend.Register();
        }
        else if (args.Any(a => a is "--vg"))
        {
            MewVGWin32Backend.Register();
        }
        else
        {
            Direct2DBackend.Register();
        }
    }
    else if (OperatingSystem.IsMacOS())
    {
        MacOSPlatform.Register();
        MewVGMacOSBackend.Register();
    }
    else if (OperatingSystem.IsLinux())
    {
        X11Platform.Register();
        MewVGX11Backend.Register();
    }
#endif

    Application.DispatcherUnhandledException += e =>
    {
        try
        {
            NativeMessageBox.Show(e.Exception.ToString(), "Unhandled UI exception");
        }
        catch
        {
            // ignore
        }
        e.Handled = true;
    };
}
