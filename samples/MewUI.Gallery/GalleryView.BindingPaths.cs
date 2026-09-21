using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using Aprillz.MewUI.Controls;

namespace Aprillz.MewUI.Gallery;

partial class GalleryView
{
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
            "Follow the selected object",
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
                        .Text("A plain viewmodel that raises PropertyChanged binds without an ObservableValue wrapper. The getter alone is enough for TwoWay: the generator writes the setter from it. The subscription is weak, so the viewmodel does not keep the view alive.")
                        .TextWrapping(TextWrapping.Wrap),
                    BindingDescription("TwoWay target (edit this):"),
                    new TextBox()
                        .Width(280)
                        .Bind(TextBox.TextProperty, viewModel, value => value.UserName),
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
            "Nested path",
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

    private FrameworkElement CollectionPathCard()
    {
        var root = new InpcDemoLibrary();
        var nextTitle = 1;

        return Card(
            "Collection path",
            new StackPanel()
                .Vertical()
                .Spacing(8)
                .Children(
                    BindingDescription(
                        "Paths: library.Books.Count and library.Books[0].Title; Mode: OneWay"),
                    new TextBlock()
                        .Text("A collection reports its own Count through PropertyChanged, and an indexed segment follows the element at a fixed position as the collection changes.")
                        .TextWrapping(TextWrapping.Wrap),
                    new TextBlock()
                        .Bind(
                            TextBlock.TextProperty,
                            root,
                            value => value.Books.Count,
                            count => $"Books.Count = {count}"),
                    BindingDescription("Books[0].Title (empty when the list is empty):"),
                    new TextBlock()
                        .Bind(TextBlock.TextProperty, root, value => value.Books[0].Title),
                    new StackPanel()
                        .Horizontal()
                        .Spacing(6)
                        .Children(
                            new Button()
                                .Content("Insert at front")
                                .OnClick(() => root.Books.Insert(
                                    0, new InpcDemoBook($"Book {nextTitle++}"))),
                            new Button()
                                .Content("Rename first")
                                .OnClick(() =>
                                {
                                    if (root.Books.Count > 0)
                                    {
                                        root.Books[0] = new InpcDemoBook($"Book {nextTitle++}");
                                    }
                                }),
                            new Button()
                                .Content("Remove first")
                                .OnClick(() =>
                                {
                                    if (root.Books.Count > 0)
                                    {
                                        root.Books.RemoveAt(0);
                                    }
                                })),
                    new TextBlock()
                        .Text("Insert at front shifts the observed element, so the title follows the new first book. Removing the last book leaves the target empty rather than reporting an error.")
                        .FontSize(ThemeFontSize.Small)
                        .TextWrapping(TextWrapping.Wrap)),
            minWidth: 420);
    }

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

internal sealed class InpcDemoLibrary : InpcDemoObject
{
    public ObservableCollection<InpcDemoBook> Books { get; } = [];
}

internal sealed class InpcDemoBook(string title) : InpcDemoObject
{
    private string _title = title;

    public string Title
    {
        get => _title;
        set => SetField(ref _title, value);
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
