# Property System

## Overview

MewUI's property system plays a role similar to WPF's `DependencyProperty`, but it has been redesigned around these goals:

| WPF | MewUI |
|-----|-------|
| `DependencyProperty` | `MewProperty<T>` |
| `DependencyObject` | `MewObject` |
| `DependencyPropertyKey` | `MewPropertyKey<T>` |
| `PropertyMetadata` | `MewPropertyOptions` (flag enum) + callback parameters |
| Reflection-based binding | Delegate-based, no Reflection |

Design principles:
- **Native AOT compatibility**: works without Reflection or runtime code generation
- **Minimal allocation**: `PropertyValueStore` is created lazily only when a property is actually used
- **Value-comparison optimization**: setting the same value again skips change notification

---

## Declaring Properties

### Basic pattern

Declare properties as `static readonly` fields and wrap them with CLR properties:

```csharp
public static readonly MewProperty<bool> IsVisibleProperty =
    MewProperty<bool>.Register<UIElement>(nameof(IsVisible), true,
        MewPropertyOptions.AffectsRender | MewPropertyOptions.AffectsLayout);

public bool IsVisible
{
    get => GetValue(IsVisibleProperty);
    set => SetValue(IsVisibleProperty, value);
}
```

`Register<TOwner>` signature:

```csharp
public static MewProperty<T> Register<TOwner>(
    string name,                          // Property name, used for diagnostics
    T defaultValue,                       // Default value
    MewPropertyOptions options = MewPropertyOptions.None,
    Action<TOwner, T, T>? changed = null, // Change callback (owner, oldValue, newValue)
    Func<TOwner, T, T>? coerce = null     // Coerce callback (owner, proposed) => stored
)
```

### Properties with change callbacks

Register a `changed` callback for properties that need side effects.
The callback receives the owner type-safely cast to `TOwner`:

```csharp
public static readonly MewProperty<Element?> ContentProperty =
    MewProperty<Element?>.Register<Button>(nameof(Content), null,
        MewPropertyOptions.AffectsLayout,
        static (self, oldValue, newValue) =>
        {
            if (oldValue != null) oldValue.Parent = null;
            if (newValue != null) newValue.Parent = self;
        });
```

### Properties with coerce callbacks

The `coerce` callback runs before a proposed value is stored and can normalize it.
It applies to every value source: local values, styles, and triggers.

```csharp
public static readonly MewProperty<double> ValueProperty =
    MewProperty<double>.Register<Slider>(nameof(Value), 0.0,
        MewPropertyOptions.AffectsRender,
        coerce: static (self, proposed) =>
            Math.Clamp(proposed, self.Minimum, self.Maximum));
```

When external state used by the coercion rule changes, call `CoerceValue` to request reevaluation:

```csharp
// Example: re-coerce CanMaximize when resizability changes
CoerceValue(CanMaximizeProperty);
```

### Read-only properties

State properties that should not be set externally, such as `IsFocused` or `IsMouseOver`, are registered with `RegisterReadOnly`.
Keep the returned `MewPropertyKey<T>` private and expose only `Key.Property` publicly:

```csharp
private static readonly MewPropertyKey<bool> IsFocusedPropertyKey =
    MewProperty<bool>.RegisterReadOnly<UIElement>(nameof(IsFocused), false,
        MewPropertyOptions.AffectsVisualState);

public static MewProperty<bool> IsFocusedProperty => IsFocusedPropertyKey.Property;

public bool IsFocused => GetValue(IsFocusedProperty);

// Owner-internal only:
internal void SetFocused(bool value) => SetValue(IsFocusedPropertyKey, value);
```

Read-only properties:
- throw `InvalidOperationException` from normal `SetValue(MewProperty<T>, T)` calls
- cannot be binding targets and throw the same exception from `SetBinding`
- can only be set through the `SetValue(MewPropertyKey<T>, T)` overload

### Inherited properties

Properties whose values flow from parent to child:

```csharp
public static readonly MewProperty<Color> ForegroundProperty =
    MewProperty<Color>.Register<Control>(nameof(Foreground), Color.Black,
        MewPropertyOptions.AffectsRender | MewPropertyOptions.Inherits);
```

---

## MewPropertyOptions

`MewPropertyOptions` is a flag enum that controls property behavior:

| Flag | Effect |
|------|--------|
| `None` | No special behavior |
| `AffectsRender` | Calls `InvalidateVisual()` when the value changes |
| `AffectsLayout` | Calls `InvalidateMeasure()` when the value changes |
| `Inherits` | Looks up the parent chain when there is no local/style value |
| `BindsTwoWayByDefault` | Makes `SetBinding()` / `Bind()` default to `TwoWay` |
| `AffectsVisualState` | Calls `InvalidateVisualState()` when the value changes. This queues visual-state recomputation at the start of the next layout/render pass. Use it for properties read by `ComputeVisualState`, such as `IsEnabled`, `IsMouseOver`, `IsFocused`, and `IsPressed`. |

Flags can be combined:

```csharp
// Affects rendering + inherits from parent
MewPropertyOptions.AffectsRender | MewPropertyOptions.Inherits
```

### Automatic invalidation

`UIElement.OnMewPropertyChanged` checks the flags and performs invalidation automatically, so control code rarely needs to call `InvalidateMeasure()` or `InvalidateVisual()` manually.

---

## Value Resolution Priority

Property values are resolved in the following priority order. Higher entries win:

```
1. Local      <- SetValue() / CLR property setter (user-explicit value)
2. Animated   <- Animation system (interpolated value updated each frame)
3. Trigger    <- StateTrigger Setter (when a state condition matches)
4. Style      <- Style base Setter
5. Inherited  <- Resolved through the parent chain when the Inherits flag is set
6. Default    <- Default value passed to Register(), including type-specific overrides
```

- When a **Local** value exists, trigger/style setters are ignored for that property, matching WPF behavior.
- **Animated** values sit below Local, so animations do not override user-explicit values.
- **Trigger** values override the same property's **Style** base setter. When a trigger no longer matches, that source value is cleared and the style base value is restored.
- **Inherited** values have higher priority than Default. Inherited values are resolved through the parent chain and cached; when the parent changes, the cache is invalidated and resolved again on the next lookup.

Internally, writes from lower-priority sources are ignored. For example, if a property has a local value, attempts by a style setter to write that property are rejected before storage.

Note: there is currently no public API for clearing a local value and returning to the style/default value.
The value store (`PropertyValueStore`) and its `ClearLocal` operation are internal and are used only by the framework, such as when clearing triggers or restoring animation values.

---

## Default Value Overrides

Derived types can change the default value of an existing property.
This must be done from the **static constructor**:

```csharp
public sealed class ProgressBar : RangeBase
{
    static ProgressBar()
    {
        // Change RangeBase.Maximum's default value (double.MaxValue) to 100
        MaximumProperty.OverrideDefaultValue<ProgressBar>(100.0);

        // Change FrameworkElement.Height's default value to 10
        HeightProperty.OverrideDefaultValue<ProgressBar>(10.0);
    }
}
```

Default lookup starts from the **most-derived type** and walks upward. The first override found is used:

```
ProgressBar -> RangeBase -> Control -> FrameworkElement -> UIElement -> ...
     ^
  Use the overridden value if found here
```

---

## Property Inheritance

Properties with the `MewPropertyOptions.Inherits` flag look up the parent chain when the object has no value of its own.

### How it works

```csharp
// Inside MewObject.GetValue:
if (PropertyStore.HasOwnValue(property.Id) || !property.Inherits)
    return PropertyStore.GetValue(property);

return ResolveInheritedValue(property);
```

`ResolveInheritedValue` is a virtual method that returns the default value in `MewObject`.
`Element`, which participates in the visual tree, overrides it to walk the parent chain.
If it finds the first ancestor with a value, that value is used; otherwise the type-specific default is used.

The resolved inherited value is cached in the store without change notification.
When the parent changes, caches originating from the previous parent chain are invalidated.
When the value of an inherited property changes, the change propagates through the descendant tree.
If a descendant has its own value, such as a local/trigger/style value, propagation stops for that subtree.

### Common inherited properties

The font-related properties on `Control` are inherited:
`Foreground`, `FontFamily`, `FontSize`, and `FontWeight`.

This lets a top-level font setting automatically apply to every child control:

```csharp
new StackPanel()
    .FontSize(14)        // Applies to every control under this panel
    .FontFamily("Pretendard")
    .Children(
        new Label().Text("Inherited font"),
        new Button().Content("Inherited font"),
        new TextBox()    // Also Pretendard 14
    )
```

---

## Change Notification Pipeline

When a property's value actually changes, processing happens in this order:

```
SetValue / SetStyle / SetTrigger
  -> apply coerce callback
  -> stop here if the value is equal (skip notification)
  -> stop any running animation
  -> store the value, then:

1. OnMewPropertyChanged(property)    <- virtual, cross-cutting handling
   |-- AffectsLayout      -> InvalidateMeasure()
   |-- AffectsRender      -> InvalidateVisual()
   |-- AffectsVisualState -> InvalidateVisualState()
   `-- Inherits           -> propagate to descendant tree

2. Property Forwards                 <- property-to-property forwarding (weak-reference targets)
3. Binding Callbacks                 <- ObservableValue synchronization
4. changed(owner, oldValue, newValue) <- callback registered by Register()
```

### Avoiding same-value updates

Local value assignment compares the new value with the existing value using `EqualityComparer<T>.Default`.
If the values are equal, it returns without even boxing.
Other paths also skip notification if the value is equal immediately before storage.
This prevents unnecessary layout/render invalidation and infinite invalidation loops.

---

## Binding Integration

`MewProperty` can bind directly to `ObservableValue<T>` or to another `MewObject` property:

```csharp
var fontSize = new ObservableValue<double>(14);

// MewProperty <-> ObservableValue binding
element.SetBinding(FontSizeProperty, fontSize);

// Binding with type conversion
var count = new ObservableValue<int>(0);
element.SetBinding(FontSizeProperty, count,
    convert: c => 12.0 + c,
    convertBack: fs => (int)(fs - 12));

// Property-to-property binding
child.SetBinding(PaddingProperty, parent, PaddingProperty);
```

Rules:
- The default mode is `property.BindsTwoWayByDefault ? TwoWay : OneWay`.
- Conversion bindings are downgraded to `OneWay` when the requested mode is `TwoWay` but `convertBack` is missing.
- Rebinding the same property disposes the previous binding first.
- `ClearBinding(property)` removes only the binding and preserves the current value.
- Read-only properties cannot be binding targets.
- In property-to-property binding, source changes are written to the target's **Style** tier. If the target has a local value, the local value still wins.

> See the [Binding](Binding.md) document for more details.

---

## Animation Integration

Animated values are not handled as a separate value tier in storage. Instead, they use a **wrapper**.
When an animation starts, the entry's value is wrapped in an `AnimatedEntry`, preserving the existing base value and its source.
Each frame updates the interpolated value:

- Each frame: `SetAnimatedValue(propertyId, value)` (internal API)
- On completion: `ClearAnimatedValue(propertyId)` restores the preserved base value and source
- If `SetValue` arrives from any source while an animation is running, the animation is stopped and the new value is stored

Effectively, Animated resolves below Local and above Trigger/Style.
Style transitions run on top of this mechanism.

---

## Implementation Notes

- `PropertyValueStore` holds a weak reference to its owner and a strong reference only to the owner type for type-specific default-value resolution.
- The store starts as a sparse property-id-based array for up to 8 entries, then promotes to a dense array only for elements that actually use many properties. This keeps `GetValue` / `SetValue` O(1) for heavily styled controls.
- To reduce boxing on the style/trigger hot path, small ints (-1..8) and doubles 0.0 / 1.0 reuse cached boxes.
- Property-to-property forward targets are stored as weak references and are automatically removed from the forwarding list when the target is collected.
