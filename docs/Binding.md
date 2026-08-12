# Data Binding Guide

MewUI data binding is delegate based and free of reflection, so it stays compatible with Native AOT.

---

## 1. Core Concepts

### Binding without reflection

WPF and WinUI write **what to bind to** as a string and resolve it with reflection at run time. MewUI writes the same thing as **code**.

```xml
<!-- WPF -->
<TextBlock Text="{Binding UserName}" />
<TextBlock Text="{Binding Customer.City}" />
<TextBlock Text="{Binding Orders[0].Title}" />
<TextBlock Text="{Binding Total, StringFormat=N0}" />
```

```csharp
// MewUI
new TextBlock().Bind(TextBlock.TextProperty, vm, x => x.UserName);
new TextBlock().Bind(TextBlock.TextProperty, vm, x => x.Customer.City);
new TextBlock().Bind(TextBlock.TextProperty, vm, x => x.Orders[0].Title);
new TextBlock().Bind(TextBlock.TextProperty, vm, x => x.Total, total => $"{total:N0}");
```

A nested path is code as well, not a string. The compiler checks every step, a change notification is attached at every step, and replacing an intermediate object reconnects everything below it (section 4).

As the last line shows, WPF's `StringFormat` and `Converter` collapse into one thing here. Every value transformation is a `convert` delegate (section 3.3).

What follows from strings becoming code:

- **Native AOT compatible**: there is no reflection, so trimming is safe
- **Compile time checking**: a misspelled property or a type mismatch fails the build
- **IntelliSense and refactoring**: completion works, and renaming a property renames the binding

### Binding modes

```csharp
public enum BindingMode
{
    OneWay,   // source to control
    TwoWay,   // both directions
}
```

The target property decides the default. Input properties such as `TextBox.TextProperty` default to TwoWay, display properties such as `Label.TextProperty` default to OneWay. An explicit `mode` argument wins.

What happens when a binding resolves to TwoWay but has no way to write back depends on the API. A converted `ObservableValue` binding without `convertBack` degrades to OneWay. **An INotifyPropertyChanged source and a path binding throw.** Telling you immediately beats leaving an input control silently one-way.

---

## 2. Source Kinds

A binding source is one of three things. All of them use the same API from section 3.

### 2.1 ObservableValue\<T>

A container that holds one value and announces changes, so you write no notification code.

```csharp
var name = new ObservableValue<string>("default");

string current = name.Value;
name.Value = "new value";

name.Changed += () => Console.WriteLine("changed");
```

A `coerce` delegate constrains the value.

```csharp
var percent = new ObservableValue<double>(50, v => Math.Clamp(v, 0, 100));
percent.Value = 150;  // 100
percent.Value = -10;  // 0
```

### 2.2 INotifyPropertyChanged view models

A view model with ordinary properties works as a source once it implements `INotifyPropertyChanged`. The subscription is weak, so a long-lived view model does not keep the view alive.

```csharp
sealed class UserViewModel : INotifyPropertyChanged
{
    private string _name = "";

    public event PropertyChangedEventHandler? PropertyChanged;

    public string Name
    {
        get => _name;
        set
        {
            _name = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Name)));
        }
    }
}
```

A notification whose property name is null or empty means "everything changed" and re-reads the value.

### 2.3 A MewProperty on another MewObject

Connects one control's property straight to another's.

```csharp
new ProgressBar().Bind(RangeBase.ValueProperty, slider, RangeBase.ValueProperty);
```

### 2.4 Choosing between them

`ObservableValue<T>` writes the notification code for you, which suits a **view model that only serves MewUI**. `INotifyPropertyChanged` suits **an existing MVVM view model or one shared with another framework**. You can mix both in one view model, and mix them within a single path (section 4).

A plain property with no notification cannot be a source. It is read once and then stops updating.

---

## 3. Attaching a Binding

### 3.1 Fluent shorthands

Frequently bound properties have a dedicated method that takes an `ObservableValue<T>`.

```csharp
new TextBox().BindText(name)                        // two-way
new Label().BindText(name)                          // one-way
new Label().BindText(count, c => $"count: {c}")     // converted
new CheckBox().BindIsChecked(isChecked)
new Slider().BindValue(volume)
new Button().BindIsVisible(isVisible).BindIsEnabled(isEnabled)
```

### 3.2 Bind and SetBinding

The general API works with any `MewProperty<T>`. `Bind` returns the element for chaining; `SetBinding` is the lower level method offering the same overloads.

```csharp
// ObservableValue
element.Bind(Control.BackgroundProperty, colorSource);

// INotifyPropertyChanged view model
new Label().Bind(Label.TextProperty, vm, x => x.Name);

// A TwoWay target is two-way as written; the write path comes from the getter expression
new TextBox().Bind(TextBox.TextProperty, vm, x => x.Name);

// Pass a setter only to decide how the write happens
new TextBox().Bind(TextBox.TextProperty, vm,
    x => x.Name,
    (owner, value) => owner.Name = value.Trim());

// An ObservableValue reached through an owner. This overload does not require INotifyPropertyChanged.
new Label().Bind(Label.TextProperty, settings, x => x.Caption);

// Another element's property
element.Bind(TextBlock.TextProperty, otherElement, Window.TitleProperty);
```

`setter` is optional. Without one, the write path is built from the getter expression at compile time. Pass one to normalize or validate on the way in, and it takes precedence.

A build where the generator does not run (see the SDK requirement in 4.1) has no such synthesis. There, binding a TwoWay target without a `setter` **throws**. Pass a `setter` or state `mode: BindingMode.OneWay`.

A getter over an `INotifyPropertyChanged` source must read **exactly one member**, because the observed property name comes from that expression. No parameter is offered for passing the name directly, so the name and the value it reads cannot drift apart. To walk more than one member, see section 4.

### 3.3 Conversion

When the source type and the target type differ, pass `convert`. TwoWay also needs `convertBack`.

```csharp
// A number as display text
new Label().Bind(Label.TextProperty, vm,
    x => x.Temperature,
    value => $"{value:0.0} C");

// Inverted boolean: hide the results while loading
results.Bind(UIElement.IsVisibleProperty, vm,
    x => x.IsLoading,
    loading => !loading);

// Presence of a value as visibility
banner.Bind(UIElement.IsVisibleProperty, vm,
    x => x.ErrorMessage,
    message => !string.IsNullOrEmpty(message));

// Two-way needs convertBack
textBox.Bind(TextBase.TextProperty, intSource,
    convert: i => i.ToString(),
    convertBack: s => int.TryParse(s, out var v) ? v : 0);
```

With an `ObservableValue` source and a visibility or enabled target, the `BindIsVisible(source, convert)` and `BindIsEnabled(source, convert)` shorthands do the same job.

Where WPF has you write a `BooleanToVisibilityConverter` or its inverted twin, here it is one lambda. There is no converter class to define and no resource to register.

Keep every computation in `convert`. Computation inside the getter leaves nothing to decide what should be observed.

### 3.4 Putting it together

```csharp
class LoginViewModel
{
    public ObservableValue<string> Username { get; } = new("");
    public ObservableValue<bool> RememberMe { get; } = new(false);
    public ObservableValue<string> ErrorMessage { get; } = new("");
    public ObservableValue<bool> IsLoading { get; } = new(false);

    public void Login()
    {
        if (string.IsNullOrEmpty(Username.Value))
        {
            ErrorMessage.Value = "Enter a user name";
            return;
        }

        IsLoading.Value = true;
    }
}
```

```csharp
new StackPanel()
    .Vertical()
    .Spacing(8)
    .Children(
        new TextBox()
            .Placeholder("User name")
            .BindText(vm.Username),

        new CheckBox()
            .Content("Stay signed in")
            .BindIsChecked(vm.RememberMe),

        new Label()
            .Foreground(Color.FromRgb(200, 60, 60))
            .BindText(vm.ErrorMessage),

        new Button()
            .Content("Sign in")
            .OnCanClick(() => !vm.IsLoading.Value)
            .OnClick(() => vm.Login()))
```

---

## 4. Nested Paths

When the source sits one level in, use a path. A path is a **chain of segments**, and each segment subscribes to its own owner. Replacing an intermediate object reconnects everything downstream.

A path does not care which source kind it walks. `ObservableValue`, `MewProperty` and `INotifyPropertyChanged` members can appear in the same chain.

### 4.1 One dotted line

This is the recommended form. Dotted member access is split into a segment chain at compile time.

```csharp
new Label().Bind(Label.TextProperty, vm, x => x.CurrentUser.Profile.DisplayName);
```

Each step picks how to observe from the member's type: `PropertyChanged` for `INotifyPropertyChanged`, the wrapper's notification for `ObservableValue<T>`, the matching `{Name}Property` for a `MewObject`, and a non-observing segment when none of those apply.

Six syntax forms are accepted.

| Form | Example |
|------|---------|
| Member access | `x.A.B` |
| Null-conditional | `x.A?.B` |
| Cast | `((User)x.Current).Name` |
| `as` cast | `(x.Current as User).Name` |
| Null-forgiving | `x.A!.B` |
| Constant indexer | `x.Items[0].Name` |

Computed expressions, method calls and conditional operators are not paths and produce a compile error. Move computation into `convert`. Indexer arguments must be constants, because the generated path is a static field and cannot hold a local from the call site.

This form requires **.NET 9 SDK or newer** to build. Below Roslyn 4.12 the source generator cannot load, multi-step access becomes a compile error, and you use the explicit chain in 4.2 instead. Only the syntax is lost, not the capability. The target framework you build for does not matter, only the SDK you build with.

### 4.2 Explicit chain

Use this to keep a path in a static field shared by several elements, or to support builds where the generator does not run. A `BindingPath` is immutable and holds no root instance until it is attached.

```csharp
static readonly BindingPath<AppViewModel, string> DisplayNamePath = BindingPath
    .From<AppViewModel>()
    .ThenNotifying(x => x.CurrentUser!)
    .ThenNotifying(x => x.DisplayName);

new Label().Bind(Label.TextProperty, vm, DisplayNamePath, fallbackValue: "-");
```

Add `!` to a nullable intermediate so the next segment's owner type is non-nullable. The operator does not disable the runtime null checks.

### 4.3 Segment kinds

| How it is appended | Owner | Observes changes | TwoWay leaf |
|--------------------|-------|------------------|-------------|
| `Then(getter)` | anything | No | No |
| `Then(selector)` | an owner exposing `ObservableValue<T>` | Yes | Yes |
| `Then(property)` | `MewObject` | Yes | Unless the property is read-only |
| `ThenNotifying(getter, setter?)` | `INotifyPropertyChanged` | Yes | When a setter is supplied |
| `ThenIndexed(getter)` | a notifying collection or indexer | Yes | No |

A non-observing `Then(getter)` is evaluated on the initial attach and whenever an upstream segment rebuilds the chain below it. A change in the getter result alone raises nothing, so the binding does not refresh. That is the right choice when the intermediate value never changes after construction.

### 4.4 Null and fallback

- A null intermediate makes the path unavailable and applies `fallbackValue`.
- When an observed intermediate becomes non-null again, the path reconnects automatically.
- **Null from the last segment is the real source value** and is not replaced by the fallback.
- A selector that returns a null `ObservableValue` is a broken path and throws.

The observer never calls a downstream selector with a null owner. C# cannot express that runtime guarantee in the generic signature, so nullable intermediate parameters use `!` as in the example above.

### 4.5 TwoWay paths

The last segment must be writable; see the last column in 4.3. A converted TwoWay path needs `convertBack`, and **a path binding throws rather than degrading to OneWay in silence.**

While a path is unavailable, target changes are not buffered. When it reconnects, the current source value overwrites the fallback or any temporary value.

### 4.6 Collections

`ObservableCollection<T>` also implements `INotifyPropertyChanged`, so a collection's own properties are observed.

```csharp
new Label().Bind(Label.TextProperty, vm, x => x.Items.Count, count => $"{count} items");
```

Indexers are observed too. When the owner implements `INotifyCollectionChanged` the segment subscribes to collection changes; when it only implements `INotifyPropertyChanged` it subscribes to the conventional `Item[]` indexer notification.

```csharp
// Updates when the element at index 0 is replaced or an item is inserted before it
new Label().Bind(Label.TextProperty, vm, x => x.Items[0].Name);

// Explicit chain
BindingPath.From<AppViewModel>()
    .ThenNotifying(x => x.Items)
    .ThenIndexed(x => x[0]);
```

In the dotted form the **declared static type** decides. A property declared as `IReadOnlyList<T>` does not become an observing segment even when the instance is an `ObservableCollection<T>`. Calling `ThenIndexed` directly checks the instance instead, so it has no such limit.

An index that no longer exists makes the path unavailable and applies `fallbackValue`. When the indexer is the last segment, rule 4.4 applies and null is delivered as the real value.

**For list UI itself, use `ItemsSource`.** List controls observe additions and removals on their own. An indexer in a path is for watching one item at a fixed position.

### 4.7 Diagnostics

| ID | Severity | Meaning |
|----|----------|---------|
| MEW1201 | Warning | A non-observing `Then` whose owner implements `INotifyPropertyChanged`. Offers a fix to `ThenNotifying` |
| MEW1202 | Error | A `ThenNotifying` getter that is not a single member access |
| MEW1203 | Error | A dotted multi-step getter in a build where the generator does not run |
| MEWG001 | Error | A getter that cannot be split into path segments |

---

## 5. Combining Several Sources

A displayed value that depends on more than one source cannot be expressed as a path, because a path is a chain with one subscription per step. **Let the view model combine and notify.**

```csharp
// Good: the view model computes FullName and notifies, so the view watches one thing
new Label().Bind(Label.TextProperty, vm, x => x.FullName);
```

When the view model cannot be changed, wire the subscriptions yourself.

```csharp
new Label()
    .Apply(label =>
    {
        void Update() => label.Text = $"{firstName.Value} {lastName.Value}".Trim();
        firstName.Changed += Update;
        lastName.Changed += Update;
        Update();
    })
```

A computation over a single source does not need this pattern. Use `convert` from 3.3.

---

## 6. Lifetime and Memory

Every observing segment uses a weak subscription, so a long-lived source never keeps a target alive. The reverse does not hold: a target owns its active bindings and therefore keeps the root and the current path objects alive until `ClearBinding`, target disposal, or `TemplateContext.Reset`.

Bindings are released automatically when the control is disposed.

```csharp
var textBox = new TextBox().BindText(vm.Name);  // released when the window closes
```

`ClearBinding` removes the binding **and the value it supplied**, revealing the next lower value source. Note that no value is left behind.

Subscriptions you make directly on an `ObservableValue` are yours to release.

```csharp
counter.Subscribe(OnChanged);
counter.Unsubscribe(OnChanged);
```

`static` on the lambdas you pass to a path is recommended, not required. A path stores its delegates, so a captured object lives as long as the path or any binding using it.

---

## 7. Methods by Control

### Label

| Method | Direction | Description |
|--------|-----------|-------------|
| `BindText(ObservableValue<string>)` | One-way | Text |
| `BindText<T>(ObservableValue<T>, Func<T, string>)` | One-way | Converted |

### TextBox / MultiLineTextBox

| Method | Direction | Description |
|--------|-----------|-------------|
| `BindText(ObservableValue<string>)` | Two-way | Text input |

### Button

| Method | Direction | Description |
|--------|-----------|-------------|
| `BindContent(ObservableValue<string>)` | One-way | Button text |
| `BindContent<T>(ObservableValue<T>, Func<T, string>)` | One-way | Converted |

### CheckBox / RadioButton / ToggleSwitch

| Method | Direction | Description |
|--------|-----------|-------------|
| `BindIsChecked(ObservableValue<bool>)` | Two-way | Checked state |

### ListBox / ComboBox

| Method | Direction | Description |
|--------|-----------|-------------|
| `BindSelectedIndex(ObservableValue<int>)` | Two-way | Selected index |

### Slider / ProgressBar

| Method | Direction | Description |
|--------|-----------|-------------|
| `BindValue(ObservableValue<double>)` | Two-way on Slider, one-way on ProgressBar | Value |

### UIElement (common)

| Method | Direction | Description |
|--------|-----------|-------------|
| `BindIsVisible(ObservableValue<bool>)` | One-way | Visibility |
| `BindIsEnabled(ObservableValue<bool>)` | One-way | Enabled state |

### Any MewProperty

| Method | Direction | Description |
|--------|-----------|-------------|
| `Bind(MewProperty<T>, ObservableValue<T>)` | Default | Direct |
| `Bind(MewProperty<TProp>, ObservableValue<TSource>, convert, convertBack?)` | Default | Converted |
| `Bind(MewProperty<T>, TSource, getter, setter?)` | Target default | INotifyPropertyChanged source (2.2); setter optional |
| `Bind(MewProperty<TProp>, TSource, getter, convert, setter?, convertBack?)` | Two-way with convertBack | Converted INotifyPropertyChanged source |
| `Bind(MewProperty<T>, TSource, Func<TSource, ObservableValue<T>>)` | Default | `ObservableValue` reached through an owner |
| `Bind(MewProperty<T>, MewObject, MewProperty<T>)` | Default | Another element's property (2.3) |
| `Bind(MewProperty<T>, TRoot, BindingPath<TRoot, T>, mode?, fallbackValue?)` | Default | Path (section 4) |

`SetBinding` offers the same set; `Bind` is the fluent wrapper over it.

---

## 8. Best Practices

### Use a source that raises notifications

```csharp
// Good: ObservableValue
class ViewModel { public ObservableValue<string> Name { get; } = new(""); }

// Good: INotifyPropertyChanged
class ViewModel : INotifyPropertyChanged { public string Name { get; set; } }

// Bad: nothing notifies, so the binding never updates
class ViewModel { public string Name { get; set; } }
```

### Keep display logic in the UI layer

```csharp
// Good: convert at the binding
new Label().BindText(vm.Price, p => $"${p:N0}");

// Bad: formatting in the view model
class ViewModel { public ObservableValue<string> FormattedPrice { get; } }
```

### Validate with coerce

```csharp
var age = new ObservableValue<int>(0, v => Math.Clamp(v, 0, 150));
```

### Dotted paths for one use, explicit chains for sharing

```csharp
// Used in one place
label.Bind(Label.TextProperty, vm, x => x.CurrentUser.Profile.DisplayName);

// Shared by several elements
static readonly BindingPath<AppViewModel, string> DisplayName = /* section 4.2 */;
```
