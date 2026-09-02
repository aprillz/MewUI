# Manage state and binding

Use typed state. Do not create XAML binding strings, `DataContext`, or reflection-based dotted paths.

## Local observable state

```csharp
var name = new ObservableValue<string>(string.Empty);
var count = new ObservableValue<int>(0);

var content = new StackPanel()
    .Vertical()
    .Spacing(8)
    .Children(
        new TextBox().BindText(name),
        new TextBlock().BindText(name, value => $"Hello, {value}"),
        new TextBlock().BindText(count, value => $"Count: {value}"),
        new Button().Content("Increment").OnClick(() => count.Value++));
```

Updating `ObservableValue<T>.Value` updates every bound target. `Changed` is raised only when the effective value changes.

## Generic property binding

```csharp
var enabled = new ObservableValue<bool>(true);

var checkBox = new CheckBox()
    .Content("Enabled")
    .BindIsChecked(enabled);

var button = new Button()
    .Content("Run")
    .Bind(UIElement.IsEnabledProperty, enabled);
```

Use `Bind` with a `MewProperty<T>`, an `ObservableValue<T>`, and optional conversion functions. A conversion binding needs a convert-back function to be two-way.

Bind one control property directly to another when no application state is needed:

```csharp
var slider = new Slider().Minimum(0).Maximum(100).Value(40);
var progress = new ProgressBar()
    .Bind(RangeBase.ValueProperty, slider, RangeBase.ValueProperty);
```

## Conversion and validation

A convert-back function may reject invalid input by throwing. MewUI keeps the source value unchanged and exposes the failure through `Control.ValidationErrorsProperty`.

```csharp
static int ParseWholeNumber(string text) =>
    int.TryParse(text, out var value)
        ? value
        : throw new FormatException("Enter a whole number.");

var quantity = new ObservableValue<int>(1);
var editor = new TextBox()
    .BindText(quantity, value => value.ToString(), ParseWholeNumber);

var validation = new TextBlock().Bind(
    TextBlock.TextProperty,
    editor,
    Control.ValidationErrorsProperty,
    errors => errors.Count == 0
        ? "Valid whole number"
        : $"Invalid: {errors[0].Message}",
    mode: BindingMode.OneWay);

var quantityPreview = new TextBlock()
    .BindText(quantity, value => $"Quantity: {value}");
```

Do not catch the conversion exception in the converter or write invalid text into the source. Display `ValidationErrors` beside the editor and let the user correct the value.

## Text editing behavior

`TextBox.Text` is an external value applied to the control. Actual editing, paste, undo, and `ReplaceSelection` commit document changes through a two-way binding.

```csharp
textBox.SelectAll();
textBox.ReplaceSelection("New text");
```

Use this editing path when testing control-to-source behavior.

## INotifyPropertyChanged models

Bind a model through a typed property lambda. MewUI observes `PropertyChanged`; a writable property lambda is two-way by default for an editing control.

```csharp
var profile = new Profile { Name = "Ada" };

var nameEditor = new TextBox()
    .Bind(TextBox.TextProperty, profile, value => value.Name);

sealed class Profile : INotifyPropertyChanged
{
    private string _name = string.Empty;

    public event PropertyChangedEventHandler? PropertyChanged;

    public string Name
    {
        get => _name;
        set
        {
            if (_name == value)
            {
                return;
            }

            _name = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Name)));
        }
    }
}
```

Keep model types used by generated binding interceptors accessible from generated code. Prefer a top-level `internal` or `public` model; do not hide such a model in a `private` nested type.

## Explicit nested BindingPath

Use `BindingPath` when an intermediate object can change and the target must reconnect. Segment behavior depends on the overload:

- `.Then(value => value.ObservableProperty)` observes an `ObservableValue<T>`.
- `.Then(MewProperty)` observes a MewUI property.
- `.ThenNotifying(getter, setter, expression)` observes `INotifyPropertyChanged`.
- A plain getter segment reads a value but does not make that value observable.

```csharp
var profileA = new EditableProfile("Ada");
var profileB = new EditableProfile("Grace");
var state = new ProfileState(profileA);

var selectedNamePath = BindingPath
    .From<ProfileState>()
    .Then(value => value.SelectedProfile)
    .Then(value => value!.Name);

var selectedName = new TextBox().Bind(
    TextBox.TextProperty,
    state,
    selectedNamePath,
    BindingMode.TwoWay,
    fallbackValue: "No profile selected");

state.SelectedProfile.Value = profileB;

sealed class ProfileState(EditableProfile initialProfile)
{
    public ObservableValue<EditableProfile?> SelectedProfile { get; } = new(initialProfile);
}

sealed class EditableProfile(string name)
{
    public ObservableValue<string> Name { get; } = new(name);
}
```

The target now follows `SelectedProfile.Value` and the selected profile's `Name.Value`. When selection is null, it displays the fallback; target edits in that state are not buffered.

Rebinding replaces the previous binding on that property. `ClearBinding` preserves the current effective value. Disposing a target control disposes its bindings.
