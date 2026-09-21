using Aprillz.MewUI.Controls;

namespace Aprillz.MewUI.Gallery;

partial class GalleryView
{
    private FrameworkElement PickersPage()
    {
        var items = Enumerable
            .Range(1, 5)
            .Select(i => $"Item {i}")
            .Append("Item Long Long Long Long Long Long Long")
            .ToArray();

        Calendar calendar = null!;

        return CardGrid(
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
                "ComboBox (SelectedItemTemplate)",
                ComboBoxSelectedItemTemplateSample(),
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

                        new TextBlock()
                            .Bind(TextBlock.TextProperty, calendar, Calendar.SelectedDateProperty, x => $"Selected: {x:yyyy-MM-dd}")
                    )
            ),

            Card(
                "DatePicker",
                new StackPanel()
                    .Vertical()
                    .Spacing(8)
                    .Children(
                        new DatePicker()
                            .Placeholder("Select a date..."),

                        new DatePicker()
                            .SelectedDate(DateTime.Today),

                        new DatePicker()
                            .Placeholder("Disabled")
                            .Disable()
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
                        new TextBlock()
                            .Text("Both"),

                        new ColorPicker()
                            .SelectedColor(Color.FromRgb(255, 0, 0)),

                        new TextBlock()
                            .Text("Wheel"),

                        new ColorPicker()
                            .SelectedColor(Color.FromRgb(0, 128, 255))
                            .Kind(ColorPickerKind.Wheel),

                        new TextBlock()
                            .Text("Panel"),

                        new ColorPicker()
                            .SelectedColor(Color.FromRgb(0, 200, 100))
                            .Kind(ColorPickerKind.Panel),

                        new TextBlock()
                            .Text("Alpha"),

                        new ColorPicker()
                            .SelectedColor(Color.FromArgb(180, 255, 128, 0))
                            .ShowAlpha(),

                        new TextBlock()
                            .Text("Disabled"),

                        new ColorPicker()
                            .SelectedColor(Color.FromRgb(80, 80, 80))
                            .Disable()
                    ),
                minWidth: 250
            )
        );
    }

    // SelectedItemTemplate presents the selection in the header the same way ItemTemplate presents a
    // row, so the closed ComboBox shows the status dot instead of falling back to the item text.
    private static FrameworkElement ComboBoxSelectedItemTemplateSample()
    {
        var members = new[]
        {
            new DemoUser(1, "Alice", "Admin", IsOnline: true),
            new DemoUser(2, "Bob", "Editor", IsOnline: false),
            new DemoUser(3, "Charlie", "Viewer", IsOnline: true),
        };

        DelegateTemplate<DemoUser> MemberTemplate(bool showRole) => new(
            build: ctx => new StackPanel()
                .Horizontal()
                .Spacing(8)
                .Children(
                    new Ellipse()
                        .Register(ctx, "Dot")
                        .Width(10)
                        .Height(10)
                        .CenterVertical(),
                    new TextBlock()
                        .Register(ctx, "Name")
                        .CenterVertical(),
                    new TextBlock()
                        .Register(ctx, "Role")
                        .CenterVertical()
                        .FontSize(ThemeFontSize.Small)
                        .IsVisible(showRole)),
            bind: (_, member, _, ctx) =>
            {
                ctx.Get<TextBlock>("Name").Text = member.Name;
                ctx.Get<TextBlock>("Role").Text = member.Role;
                ctx.Get<Ellipse>("Dot").WithTheme((theme, dot) =>
                    dot.Fill(member.IsOnline ? theme.Palette.Accent : theme.Palette.ControlBorder));
            });

        return new StackPanel()
            .Vertical()
            .Width(220)
            .Spacing(8)
            .Children(
                new TextBlock()
                    .Text("Header and list share one template")
                    .FontSize(ThemeFontSize.Small),

                new ComboBox()
                    .Items(members, member => member.Name)
                    .ItemHeight(28)
                    .ItemTemplate(MemberTemplate(showRole: true))
                    .SelectedIndex(0),

                new TextBlock()
                    .Text("Header-only template, list stays text")
                    .FontSize(ThemeFontSize.Small),

                new ComboBox()
                    .Items(members, member => member.Name)
                    .SelectedItemTemplate(MemberTemplate(showRole: false))
                    .SelectedIndex(1),

                new TextBlock()
                    .Text("No selection falls back to the placeholder")
                    .FontSize(ThemeFontSize.Small),

                new ComboBox()
                    .Placeholder("Pick a member...")
                    .Items(members, member => member.Name)
                    .SelectedItemTemplate(MemberTemplate(showRole: true))
            );
    }
}
