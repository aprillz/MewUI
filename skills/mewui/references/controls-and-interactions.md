# Build common interactions

## Input form

```csharp
var userName = new ObservableValue<string>(string.Empty);
var accepted = new ObservableValue<bool>(false);
var result = new ObservableValue<string>("Enter a name");

var form = new StackPanel()
    .Vertical()
    .Spacing(10)
    .Children(
        new TextBlock().Text("Name").Bold(),
        new TextBox().BindText(userName),
        new CheckBox().Content("Accept terms").BindIsChecked(accepted),
        new Button()
            .Content("Continue")
            .OnClick(() =>
            {
                result.Value = string.IsNullOrWhiteSpace(userName.Value)
                    ? "Name is required"
                    : $"Welcome, {userName.Value}";
            }),
        new TextBlock().BindText(result));
```

Use `TextBlock` and `Label` for display, `TextBox` and `MultiLineTextBox` for text entry, `PasswordBox` for secrets, and `NumericUpDown` for numeric values. Use typed `Bind*` helpers where available.

## Selection controls

- `CheckBox`: independent boolean or nullable boolean state
- `RadioButton`: one choice in a group
- `ToggleButton` and `ToggleSwitch`: persistent on/off state
- `ButtonGroup` and `SegmentedControl`: compact related choices
- `ComboBox`: one choice from a collection

Do not assume WPF `ICommand` properties. Use `.OnClick(...)` for local actions. For reusable keyboard actions, use the public MewUI command and input-map APIs exposed by the selected package version.

## Tabs

```csharp
var tabs = new TabControl()
    .TabItems(
        new TabItem()
            .Header("Home")
            .Content(new TextBlock().Text("Home page")),
        new TabItem()
            .Header("Settings")
            .Content(new TextBlock().Text("Settings page")));
```

Keep popup, menu, tooltip, and dialog content associated with an owner window. Do not reuse one visual element under multiple parents.
