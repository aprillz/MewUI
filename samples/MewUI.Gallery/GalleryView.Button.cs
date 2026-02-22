using Aprillz.MewUI.Controls;

namespace Aprillz.MewUI.Gallery;

partial class GalleryView
{
    private FrameworkElement ButtonsPage() =>
        CardGrid(
            Card(
                "Buttons",
                new StackPanel()
                    .Vertical()
                    .Spacing(8)
                    .Children(
                        new Button().Content("Default"),
                        new Button()
                            .Content("Accent")
                            .WithTheme((t, c) => c.Background(t.Palette.Accent).Foreground(t.Palette.AccentText)),
                        new Button().Content("Disabled").Disable(),
                        new Button()
                            .Content("Double Click")
                            .OnDoubleClick(() => MessageBox.Show("Double Click"))
                    )
            ),

            Card(
                "ToggleButton",
                new StackPanel()
                    .Vertical()
                    .Spacing(8)
                    .Children(
                        new ToggleButton().Content("Toggle"),
                        new ToggleButton().Content("Checked").IsChecked(true),
                        new ToggleButton().Content("Disabled").Disable(),
                        new ToggleButton().Content("Disabled (Checked)").IsChecked(true).Disable()
                    )
            ),

            Card(
                "Toggle / Switch",
                new StackPanel()
                    .Vertical()
                    .Spacing(8)
                    .Children(
                        new ToggleSwitch().IsChecked(true),
                        new ToggleSwitch().IsChecked(false),
                        new ToggleSwitch().IsChecked(true).Disable(),
                        new ToggleSwitch().IsChecked(false).Disable()
                    )
            ),

            Card(
                "Progress",
                new StackPanel()
                    .Vertical()
                    .Spacing(8)
                    .Children(
                        new ProgressBar().Value(20),
                        new ProgressBar().Value(65),
                        new ProgressBar().Value(65).Disable(),
                        new Slider().Minimum(0).Maximum(100).Value(25),
                        new Slider().Minimum(0).Maximum(100).Value(25).Disable()
                    )
            )
        );
}

