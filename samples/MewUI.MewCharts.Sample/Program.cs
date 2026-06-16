using Aprillz.MewUI;
using Aprillz.MewUI.Controls;

using static Samples;

// A gallery faithful to the LiveCharts2 samples, rendered entirely through MewUI's IGraphicsContext
// backend (no SkiaSharp). The per-category sample builders live in the Samples.*.cs partial files.

Startup();

var gallery = new ScrollViewer()
    .NoHorizontalScroll()
    .AutoVerticalScroll()
    .Content(
        new WrapPanel()
            .Spacing(16)
            .Children(
                Section("Pies / Solid gauge", GaugeSolid()),
                Section("Pies / Angular gauge", AngularGaugeInteractive()),
                Section("Pies / Basic gauge", PiesGauge1()),
                Section("Pies / 270 degrees gauge", PiesGauge2()),
                Section("Pies / Multiple values gauge", PiesGauge3()),
                Section("Pies / Slim gauge", PiesGauge4()),
                Section("Pies / Auto updates on gauges", PiesGauge5()),
                Section("Pies / Nested", PiesNested()),
                Section("Pies / AutoUpdate", PiesAutoUpdate()),
                Section("Pies / Icons", PiesIcons()),
                Section("Lines / Basic", LinesBasic()),
                Section("Lines / Straight", LinesStraight()),
                Section("Lines / Area", LinesArea()),
                Section("Lines / XY", LinesXY()),
                Section("Lines / Properties", LinesProperties()),
                Section("Lines / Padding", LinesPadding()),
                Section("Lines / AutoUpdate", LinesAutoUpdate()),
                Section("Lines / Custom", LinesCustom()),
                Section("Lines / CustomPoints", LinesCustomPoints()),
                Section("Bars / Basic", BarsBasic()),
                Section("Bars / Spacing", BarsSpacing()),
                Section("Bars / WithBackground", BarsWithBackground()),
                Section("Bars / Layered", BarsLayered()),
                Section("Bars / RowsWithLabels", BarsRows()),
                Section("Bars / AutoUpdate", BarsAutoUpdate()),
                Section("Bars / Race", BarsRace()),
                Section("Bars / Custom", BarsCustom()),
                Section("Bars / DelayedAnimation", BarsDelayedAnimation()),
                Section("StackedBars / Basic", StackedBarsBasic()),
                Section("StackedBars / Groups", StackedBarsGroups()),
                Section("StackedArea / Basic", StackedAreaBasic()),
                Section("StackedArea / StepArea", StackedStepArea()),
                Section("StepLines / Basic", StepLinesBasic()),
                Section("StepLines / Area", StepLinesArea()),
                Section("StepLines / Properties", StepLinesProperties()),
                Section("StepLines / AutoUpdate", StepLinesAutoUpdate()),
                Section("Scatter / Basic", ScatterBasic()),
                Section("Scatter / Bubbles", ScatterBubbles()),
                Section("Scatter / AutoUpdate", ScatterAutoUpdate()),
                Section("Scatter / Custom", ScatterCustom()),
                Section("Pies / Basic", PiesBasic()),
                Section("Pies / Doughnut", PiesDoughnut()),
                Section("Pies / Pushout", PiesPushout()),
                Section("Pies / NightingaleRose", PiesNightingale()),
                Section("Pies / OutLabels", PiesOutLabels()),
                Section("Polar / Basic", PolarBasic()),
                Section("Polar / RadialArea", PolarRadialArea()),
                Section("Polar / Coordinates", PolarCoordinates()),
                Section("Polar / Test", PolarTest()),
                Section("Axes / NamedLabels", AxesNamedLabels()),
                Section("Axes / LabelsRotation", AxesLabelsRotation()),
                Section("Axes / LabelsFormat", AxesLabelsFormat()),
                Section("Axes / LabelsFormat2", AxesLabelsFormat2()),
                Section("Axes / Multiple", AxesMultiple()),
                Section("Axes / DateTimeScaled", AxesDateTimeScaled()),
                Section("Axes / TimeSpanScaled", AxesTimeSpanScaled()),
                Section("Axes / ColorsAndPosition", AxesColorsAndPosition()),
                Section("Axes / CustomSeparatorsInterval", AxesCustomSeparators()),
                Section("Axes / MatchScale", AxesMatchScale()),
                Section("Axes / Crosshairs", AxesCrosshairs()),
                Section("Axes / Paging", AxesPaging()),
                Section("Axes / Shared", AxesShared()),
                Section("Axes / Style", AxesStyle()),
                Section("Axes / Logarithmic", AxesLogarithmic()),
                Section("Lines / Zoom (X)", LinesZoom()),
                Section("Financial / Candlesticks", FinancialCandlesticks()),
                Section("Box / Basic", BoxBasic()),
                Section("Heat / Basic", HeatBasic()),
                Section("Error / Basic", ErrorBasic()),
                Section("General / NullPoints", NullPoints()),
                Section("General / Sections", GeneralSections()),
                Section("General / Legends", GeneralLegends()),
                Section("General / Tooltips", GeneralTooltips()),
                Section("General / Visibility", GeneralVisibilityInteractive()),
                Section("General / VisualElements", GeneralVisualElements()),
                Section("General / UserDefinedTypes", GeneralUserDefinedTypes()),
                Section("General / Sections2", GeneralSections2()),
                Section("General / ConditionalDraw", GeneralConditionalDraw()),
                Section("General / Scrollable", GeneralScrollableInteractive()),
                Section("General / MultiThreading", GeneralMultiThreading()),
                Section("General / MultiThreading2", GeneralMultiThreading2()),
                Section("General / ChartToImage", GeneralChartToImage()),
                Section("General / DrawOnCanvas", GeneralDrawOnCanvas()),
                Section("General / TemplatedTooltips", GeneralTemplatedTooltips()),
                Section("General / TemplatedLegends", GeneralTemplatedLegends()),
                Section("General / TooltipHoverArea", GeneralTooltipHoverArea()),
                Section("Events / Tutorial (click bars)", EventsTutorialInteractive()),
                Section("Events / OverrideFind", EventsOverrideFind()),
                Section("Events / AddPointOnClick (click plot)", EventsAddPointOnClickInteractive()),
                Section("Design / LinearGradients", DesignLinearGradients()),
                Section("Design / RadialGradients", DesignRadialGradients()),
                Section("Design / StrokeDashArray", DesignStrokeDashArray()),
                Section("General / RealTime", RealTime())
            )
    );

Window window = null!;

var root = new Window()
    .Resizable(1280, 760)
    .StartCenterScreen()
    .OnBuild(x => x
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
