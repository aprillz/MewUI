using System.ComponentModel;
using System.Runtime.CompilerServices;

using Aprillz.MewUI.Controls;

namespace Aprillz.MewUI.Gallery;

partial class GalleryView
{
    private FrameworkElement DataBindingPage() =>
        CardGrid(
            ObservableValueBindingCard(),
            ConvertedBindingCard(),
            BindingValidationCard(),
            MewPropertyBindingCard(),
            BindingPathCard(),
            InpcBindingCard(),
            InpcNestedPathCard(),
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

    private FrameworkElement BindingValidationCard()
    {
        static int ParseWholeNumber(string text) =>
            int.TryParse(text, out var value)
                ? value
                : throw new FormatException("Enter a whole number.");

        var source = new ObservableValue<int>(42);
        var nextValidValue = 43;
        var target = new TextBox()
            .Width(280)
            .BindText(
                source,
                static value => value.ToString(),
                ParseWholeNumber);
        var status = new TextBlock()
            .Bind(
                TextBlock.TextProperty,
                target,
                Control.ValidationErrorsProperty,
                static errors => errors.Count == 0
                    ? "Valid: no binding errors"
                    : $"Invalid: {errors[0].Message}",
                mode: BindingMode.OneWay)
            .TextWrapping(TextWrapping.Wrap);

        return Card(
            "Validation / Invalid state",
            new StackPanel()
                .Vertical()
                .Spacing(8)
                .Children(
                    BindingDescription(
                        "Source: ObservableValue<int>; Target: TextBox.Text; Mode: TwoWay"),
                    new TextBlock()
                        .Text("Type a non-numeric value. ConvertBack fails, the source stays unchanged, and the TextBox uses the Error border until the binding recovers.")
                        .TextWrapping(TextWrapping.Wrap),
                    BindingDescription("TwoWay target (try letters):"),
                    target,
                    new TextBlock()
                        .BindText(source, static value => $"source.Value = {value}"),
                    status,
                    new Button()
                        .Content("Restore from source")
                        .HorizontalAlignment(HorizontalAlignment.Left)
                        .OnClick(() => source.Value = nextValidValue++)),
            minWidth: 420);
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
                        .FontSize(ThemeFontSize.Small)
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

    private FrameworkElement InpcBindingCard()
    {
        var viewModel = new InpcDemoViewModel { UserName = "Alice", Temperature = 21.5 };
        var nextName = 1;

        return Card(
            "INotifyPropertyChanged source",
            new StackPanel()
                .Vertical()
                .Spacing(8)
                .Children(
                    BindingDescription(
                        "Source: INotifyPropertyChanged viewmodel; Target: TextBox.Text; Mode: TwoWay"),
                    new TextBlock()
                        .Text("A plain viewmodel that raises PropertyChanged binds without an ObservableValue wrapper. The subscription is weak, so the viewmodel does not keep the view alive.")
                        .TextWrapping(TextWrapping.Wrap),
                    BindingDescription("TwoWay target (edit this):"),
                    new TextBox()
                        .Width(280)
                        .Bind(
                            TextBox.TextProperty,
                            viewModel,
                            static value => value.UserName,
                            static (owner, value) => owner.UserName = value),
                    new TextBlock()
                        .Bind(
                            TextBlock.TextProperty,
                            viewModel,
                            static value => value.Temperature,
                            static value => $"Temperature: {value:0.0} C"),
                    new Button()
                        .Content("Change from the viewmodel")
                        .HorizontalAlignment(HorizontalAlignment.Left)
                        .OnClick(() =>
                        {
                            viewModel.UserName = $"User {nextName++}";
                            viewModel.Temperature += 0.5;
                        })),
            minWidth: 380);
    }

    private FrameworkElement InpcNestedPathCard()
    {
        var firstProfile = new InpcDemoProfile("Profile A", "Alice");
        var secondProfile = new InpcDemoProfile("Profile B", "Bob");
        var root = new InpcDemoRoot(firstProfile);

        return Card(
            "INotifyPropertyChanged nested path",
            new StackPanel()
                .Vertical()
                .Spacing(8)
                .Children(
                    BindingDescription(
                        "Path: root.CurrentProfile.DisplayName; Target: TextBox.Text; Mode: TwoWay"),
                    new TextBlock()
                        .Text("The dotted lambda is split into observed segments at compile time. Replacing CurrentProfile rewires the downstream subscription, and edits follow the selected profile.")
                        .TextWrapping(TextWrapping.Wrap),
                    BindingDescription("Nested path target (edit to write the selected profile):"),
                    new TextBox()
                        .Width(280)
                        .Bind(
                            TextBox.TextProperty,
                            root,
                            static value => value.CurrentProfile.DisplayName),
                    new TextBlock()
                        .Bind(
                            TextBlock.TextProperty,
                            root,
                            static value => value.CurrentProfile,
                            static profile => $"root.CurrentProfile = {profile.Id}"),
                    new StackPanel()
                        .Horizontal()
                        .Spacing(6)
                        .Children(
                            new Button()
                                .Content("Select A")
                                .OnClick(() => root.CurrentProfile = firstProfile),
                            new Button()
                                .Content("Select B")
                                .OnClick(() => root.CurrentProfile = secondProfile)),
                    new TextBlock()
                        .Text("Select B, then edit the text: Profile A must keep its own name.")
                        .FontSize(ThemeFontSize.Small)
                        .TextWrapping(TextWrapping.Wrap)),
            minWidth: 420);
    }

    private static TextBlock BindingDescription(string text) =>
        new TextBlock()
            .Text(text)
            .FontSize(ThemeFontSize.Small)
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

// The generated interceptors name these types, so they cannot be private members of GalleryView.
internal abstract class InpcDemoObject : INotifyPropertyChanged
{
    public event PropertyChangedEventHandler? PropertyChanged;

    protected void SetField<T>(
        ref T field,
        T value,
        [CallerMemberName] string? propertyName = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value))
        {
            return;
        }

        field = value;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}

internal sealed class InpcDemoViewModel : InpcDemoObject
{
    private string _userName = string.Empty;
    private double _temperature;

    public string UserName
    {
        get => _userName;
        set => SetField(ref _userName, value);
    }

    public double Temperature
    {
        get => _temperature;
        set => SetField(ref _temperature, value);
    }
}

internal sealed class InpcDemoRoot(InpcDemoProfile profile) : InpcDemoObject
{
    private InpcDemoProfile _currentProfile = profile;

    public InpcDemoProfile CurrentProfile
    {
        get => _currentProfile;
        set => SetField(ref _currentProfile, value);
    }
}

internal sealed class InpcDemoProfile(string id, string displayName) : InpcDemoObject
{
    private string _displayName = displayName;

    public string Id { get; } = id;

    public string DisplayName
    {
        get => _displayName;
        set => SetField(ref _displayName, value);
    }
}
