using Aprillz.MewUI.Controls;

namespace Aprillz.MewUI.Gallery;

partial class GalleryView
{
    DatePicker datePicker = null!;

    private FrameworkElement SelectionPage()
    {
        var items = Enumerable.Range(1, 20).Select(i => $"Item {i}").Append("Item Long Long Long Long Long Long Long").ToArray();
        Calendar calendar = null!;

        return CardGrid(
            Card(
                "CheckBox",
                new Grid()
                    .Columns("Auto,Auto")
                    .Rows("Auto,Auto,Auto")
                    .Spacing(8)
                    .Children(
                        new CheckBox().Content("CheckBox"),
                        new CheckBox().Content("Disabled").Disable(),
                        new CheckBox().Content("Checked").IsChecked(true),
                        new CheckBox().Content("Disabled (Checked)").IsChecked(true).Disable(),
                        new CheckBox().Content("Three-state").IsThreeState(true).IsChecked(null),
                        new CheckBox().Content("Disabled (Indeterminate)").IsThreeState(true).IsChecked(null).Disable()
                    )
            ),

            Card(
                "RadioButton",
                new Grid()
                    .Columns("Auto,Auto")
                    .Rows("Auto,Auto")
                    .Spacing(8)
                    .Children(
                        new RadioButton().Content("A").GroupName("g"),
                        new RadioButton().Content("C (Disabled)").GroupName("g2").Disable(),
                        new RadioButton().Content("B").GroupName("g").IsChecked(true),
                        new RadioButton().Content("Disabled (Checked)").GroupName("g2").IsChecked(true).Disable()
                    )
            ),

            Card(
                "ComboBox",
                new StackPanel()
                    .Vertical()
                    .Width(200)
                    .Spacing(8)
                    .Children(
                        new ComboBox()
                            .Items(["Alpha", "Beta", "Gamma", "Delta", "Epsilon", "Zeta", "Eta", "Theta", "Iota", "Kappa"])
                            .SelectedIndex(1),

                        new ComboBox()
                            .Placeholder("Select an item...")
                            .Items(items),

                        new ComboBox()
                            .Items(items)
                            .SelectedIndex(1)
                            .Disable()
                    ),
                minWidth: 250
            ),

            Card(
                "Calendar",
                new StackPanel()
                    .Vertical()
                    .Spacing(8)
                    .Children(
                        new Calendar()
                            .Ref(out calendar),

                        new StackPanel()
                        .Horizontal()
                        .Spacing(8)
                        .Children(
                            new ComboBox()
                                .Items(["Default", "Persian (fa-IR)", "Arabic (ar-SA)", "Hebrew (he-IL)", "Japanese (ja-JP)", "Korean (ko-KR)", "Taiwan (zh-TW)", "Thai (th-TH)"])
                                .SelectedIndex(0)
                                .OnSelectionChanged(x => {
                                    var culture = x switch {
                                        "Persian (fa-IR)"  => System.Globalization.CultureInfo.GetCultureInfo("fa-IR"),
                                        "Arabic (ar-SA)"   => System.Globalization.CultureInfo.GetCultureInfo("ar-SA"),
                                        "Hebrew (he-IL)"   => System.Globalization.CultureInfo.GetCultureInfo("he-IL"),
                                        "Japanese (ja-JP)" => System.Globalization.CultureInfo.GetCultureInfo("ja-JP"),
                                        "Korean (ko-KR)"   => System.Globalization.CultureInfo.GetCultureInfo("ko-KR"),
                                        "Taiwan (zh-TW)"   => System.Globalization.CultureInfo.GetCultureInfo("zh-TW"),
                                        "Thai (th-TH)"     => System.Globalization.CultureInfo.GetCultureInfo("th-TH"),
                                        _ => (System.Globalization.CultureInfo?)null
                                    };
                                    calendar.DisplayCulture = culture;
                                    datePicker.DisplayCulture = culture;
                                }),

                        new CheckBox()
                                .Content("Gregorian")
                                .OnCheckedChanged(v => {
                                    calendar.UseGregorianCalendar = v == true;
                                    datePicker.UseGregorianCalendar = v == true;
                                }),

                            new TextBlock()
                                .Bind(TextBlock.TextProperty, calendar, Calendar.SelectedDateProperty, x => $"Selected: {x:yyyy-MM-dd}")
                        )
                    )
            ),

            Card(
                "DatePicker",
                new StackPanel()
                    .Vertical()
                    .Spacing(8)
                    .Children(
                        new DatePicker().Placeholder("Select a date..."),
                        new DatePicker().Ref(out datePicker).SelectedDate(DateOnly.FromDateTime(DateTime.Today)),
                        new DatePicker().Placeholder("Disabled").Disable()
                    ),
                minWidth: 250
            ),

            Card(
                "ColorPicker",
                new Grid()
                    .Rows("Auto,Auto,Auto,Auto,Auto")
                    .Columns("Auto,*")
                    .Spacing(8)
                    .Children(
                        new TextBlock().Text("Both"),
                        new ColorPicker().SelectedColor(Color.FromRgb(255, 0, 0)),
                        new TextBlock().Text("Wheel"),
                        new ColorPicker().SelectedColor(Color.FromRgb(0, 128, 255)).Kind(ColorPickerKind.Wheel),
                        new TextBlock().Text("Panel"),
                        new ColorPicker().SelectedColor(Color.FromRgb(0, 200, 100)).Kind(ColorPickerKind.Panel),
                        new TextBlock().Text("Alpha"),
                        new ColorPicker().SelectedColor(Color.FromArgb(180, 255, 128, 0)).ShowAlpha(),
                        new TextBlock().Text("Disabled"),
                        new ColorPicker().SelectedColor(Color.FromRgb(80, 80, 80)).Disable()
                    ),
                minWidth: 250
            ),

            Card(
                "TabControl",
                new UniformGrid()
                    .Columns(2)
                    .Spacing(8)
                    .Children(
                        new TabControl()
                            .Height(120)
                            .TabItems(
                                new TabItem().Header("_Home").Content(new TextBlock().Text("Home tab content")),
                                new TabItem().Header("Se_ttings").Content(new TextBlock().Text("Settings tab content")),
                                new TabItem().Header("A_bout").Content(new TextBlock().Text("About tab content"))
                            ),

                        new TabControl()
                            .Height(120)
                            .Disable()
                            .TabItems(
                                new TabItem().Header("Home").Content(new TextBlock().Text("Home tab content")),
                                new TabItem().Header("Settings").Content(new TextBlock().Text("Settings tab content")),
                                new TabItem().Header("About").Content(new TextBlock().Text("About tab content"))
                            )
                    )
            )
        );
    }
}

