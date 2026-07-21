using Aprillz.MewUI.Controls;

namespace Aprillz.MewUI.Gallery;

partial class GalleryView
{
    private FrameworkElement DataBindingPage() =>
        CardGrid(
            ObservableValueBindingCard(),
            ConvertedBindingCard(),
            MewPropertyBindingCard(),
            BindingPathCard(),
            BindingLifetimeCard());

    private FrameworkElement ObservableValueBindingCard()
    {
        var source = new ObservableValue<string>("Alice");
        var nextValue = 1;

        return Card(
            "ObservableValue / TwoWay",
            new StackPanel()
                .Vertical()
                .Spacing(8)
                .Children(
                    BindingDescription(
                        "Source: ObservableValue<string>; Target: TextBox.Text; Mode: TwoWay"),
                    new TextBlock()
                        .Text("Typing updates source.Value. Changing source.Value updates the TextBox.")
                        .TextWrapping(TextWrapping.Wrap),
                    BindingDescription("Target TextBox (edit this):"),
                    new TextBox()
                        .Width(280)
                        .BindText(source),
                    new TextBlock()
                        .BindText(source, static value => $"source.Value = \"{value}\""),
                    new Button()
                        .Content("Change source.Value")
                        .OnClick(() => source.Value = $"Source value {nextValue++}")),
            minWidth: 380);
    }

    private FrameworkElement ConvertedBindingCard()
    {
        var source = new ObservableValue<double>(42);

        return Card(
            "Conversion / mixed modes",
            new StackPanel()
                .Vertical()
                .Spacing(8)
                .Children(
                    BindingDescription(
                        "Source: ObservableValue<double>; Slider: TwoWay; Progress/Text: OneWay"),
                    new TextBlock()
                        .Text("The Slider writes the number back. The other targets only render it, and the TextBlock uses a converter.")
                        .TextWrapping(TextWrapping.Wrap),
                    BindingDescription("TwoWay target (Slider):"),
                    new Slider()
                        .Width(280)
                        .Minimum(0)
                        .Maximum(100)
                        .BindValue(source),
                    BindingDescription("OneWay target (ProgressBar):"),
                    new ProgressBar()
                        .Width(280)
                        .Minimum(0)
                        .Maximum(100)
                        .BindValue(source),
                    new TextBlock()
                        .BindText(source, static value => $"Converted text: {value:0.0}%")),
            minWidth: 380);
    }

    private FrameworkElement MewPropertyBindingCard()
    {
        var source = new Slider()
            .Width(280)
            .Minimum(0)
            .Maximum(100)
            .Value(35);
        var propertyPath = BindingPath
            .From<Slider>()
            .Then(RangeBase.ValueProperty);

        return Card(
            "MewProperty source",
            new StackPanel()
                .Vertical()
                .Spacing(8)
                .Children(
                    BindingDescription(
                        "Source: Slider.ValueProperty; Target: ProgressBar.ValueProperty; Mode: OneWay"),
                    new TextBlock()
                        .Text("This binds framework properties directly, without an ObservableValue wrapper. The readout uses a MewProperty BindingPath segment.")
                        .TextWrapping(TextWrapping.Wrap),
                    BindingDescription("Source Slider:"),
                    source,
                    BindingDescription("Direct MewProperty target:"),
                    new ProgressBar()
                        .Width(280)
                        .Minimum(0)
                        .Maximum(100)
                        .Bind(RangeBase.ValueProperty, source, RangeBase.ValueProperty),
                    new TextBlock()
                        .Bind(
                            TextBlock.TextProperty,
                            source,
                            propertyPath,
                            static value => $"BindingPath value: {value:0.0}",
                            mode: BindingMode.OneWay)),
            minWidth: 380);
    }

    private FrameworkElement BindingPathCard()
    {
        const string fallbackText = "No profile selected";
        var profileA = new BindingPathDemoProfile("Profile A", "Alice");
        var profileB = new BindingPathDemoProfile("Profile B", "Bob");
        var root = new BindingPathDemoRoot(profileA);
        var path = BindingPath
            .From<BindingPathDemoRoot>()
            .Then(static value => value.SelectedProfile)
            .Then(static value => value!.Name);

        return Card(
            "BindingPath / follow the selected object",
            new StackPanel()
                .Vertical()
                .Spacing(8)
                .Children(
                    BindingDescription(
                        "Path: root.SelectedProfile.Value?.Name.Value; Target: TextBox.Text; Mode: TwoWay"),
                    new TextBlock()
                        .Text("Edit both source names, then choose which Profile object the root points to. The target follows only the selected object's Name.")
                        .TextWrapping(TextWrapping.Wrap),
                    new StackPanel()
                        .Horizontal()
                        .Spacing(8)
                        .Children(
                            new TextBlock()
                                .Width(110)
                                .Text("Profile A.Name")
                                .CenterVertical(),
                            new TextBox()
                                .Width(220)
                                .BindText(profileA.Name)),
                    new StackPanel()
                        .Horizontal()
                        .Spacing(8)
                        .Children(
                            new TextBlock()
                                .Width(110)
                                .Text("Profile B.Name")
                                .CenterVertical(),
                            new TextBox()
                                .Width(220)
                                .BindText(profileB.Name)),
                    new TextBlock()
                        .BindText(
                            root.SelectedProfile,
                            static profile =>
                                $"root.SelectedProfile = {profile?.Id ?? "null"}"),
                    new StackPanel()
                        .Horizontal()
                        .Spacing(6)
                        .Children(
                            new Button()
                                .Content("Select A")
                                .OnClick(() => root.SelectedProfile.Value = profileA),
                            new Button()
                                .Content("Select B")
                                .OnClick(() => root.SelectedProfile.Value = profileB),
                            new Button()
                                .Content("Select null")
                                .OnClick(() => root.SelectedProfile.Value = null)),
                    BindingDescription("BindingPath target (edit to write the selected Profile.Name):"),
                    new TextBox()
                        .Width(280)
                        .Bind(
                            TextBox.TextProperty,
                            root,
                            path,
                            BindingMode.TwoWay,
                            fallbackValue: fallbackText),
                    new TextBlock()
                        .Text("Try Select B, then edit Profile A: the target must stay on B. Select null to see the fallback; null-state target edits are not buffered.")
                        .FontSize(11)
                        .TextWrapping(TextWrapping.Wrap)),
            minWidth: 440);
    }

    private FrameworkElement BindingLifetimeCard()
    {
        var source = new ObservableValue<string>("Bound value 1");
        var state = new ObservableValue<string>("Binding is active");
        var target = new TextBlock();
        var version = 1;

        void BindTarget()
        {
            target.SetBinding(TextBlock.TextProperty, source, BindingMode.OneWay);
            state.Value = "Binding is active";
        }

        BindTarget();

        return Card(
            "Binding lifetime / ClearBinding",
            new StackPanel()
                .Vertical()
                .Spacing(8)
                .Children(
                    BindingDescription(
                        "Source: ObservableValue<string>; Target: TextBlock.Text; Mode: OneWay"),
                    new TextBlock()
                        .Text("ClearBinding detaches the source and preserves the target's current value. Bind again to resynchronize it.")
                        .TextWrapping(TextWrapping.Wrap),
                    new TextBlock()
                        .BindText(source, static value => $"Source: {value}"),
                    new StackPanel()
                        .Horizontal()
                        .Spacing(4)
                        .Children(
                            new TextBlock().Text("Target:"),
                            target),
                    new TextBlock()
                        .BindText(state, static value => $"State: {value}"),
                    new StackPanel()
                        .Horizontal()
                        .Spacing(6)
                        .Children(
                            new Button()
                                .Content("Change Source")
                                .OnClick(() => source.Value = $"Bound value {++version}"),
                            new Button()
                                .Content("Clear Binding")
                                .OnClick(() =>
                                {
                                    target.ClearBinding(TextBlock.TextProperty);
                                    state.Value = "Binding cleared; target value preserved";
                                }),
                            new Button()
                                .Content("Bind Again")
                                .OnClick(BindTarget))),
            minWidth: 440);
    }

    private static TextBlock BindingDescription(string text) =>
        new TextBlock()
            .Text(text)
            .FontSize(11)
            .TextWrapping(TextWrapping.Wrap);

    private sealed class BindingPathDemoRoot(BindingPathDemoProfile initialProfile)
    {
        public ObservableValue<BindingPathDemoProfile?> SelectedProfile { get; } =
            new(initialProfile);
    }

    private sealed class BindingPathDemoProfile(string id, string name)
    {
        public string Id { get; } = id;

        public ObservableValue<string> Name { get; } = new(name);
    }
}
