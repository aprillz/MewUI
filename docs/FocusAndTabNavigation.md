# Focus and Tab Navigation

This document describes MewUI's keyboard focus model and Tab traversal rules.

## Focus Model

Focus is managed per window. Each `Window` has one `FocusManager`, and at most one
element inside the window can have keyboard focus (`FocusManager.FocusedElement`).

```csharp
window.FocusManager.FocusedElement   // Current focused element, or null
element.Focus();                     // Move focus programmatically
window.FocusManager.ClearFocus();    // Clear focus
```

### Focus Eligibility

An element must satisfy all of the following conditions to receive focus:

- `Focusable == true`: determined by the control type (`Button`, `TextBox`, `ListBox`, etc.).
  Non-interactive elements such as Label, TextBlock, and Panel do not receive focus.
- `IsEffectivelyEnabled == true`: the element and all ancestors are enabled.
- `IsVisible == true`

### Focus State and Styling

| Property | Meaning |
|---|---|
| `IsFocused` | This element has keyboard focus (read-only) |
| `IsFocusWithin` | This element or one of its descendants has focus (read-only) |

Both properties can be used in style state triggers, such as focus rings.
Subscribe to focus changes with the `GotFocus` / `LostFocus` events.

```csharp
new TextBox()
    .OnGotFocus(() => Console.WriteLine("focus in"))
    .OnLostFocus(() => Console.WriteLine("focus out"));
```

### Mouse and Focus

- When clicked, the nearest focusable ancestor of the hit element receives focus
  (clicking a Label can focus the control that wraps it).
- Clicking an empty window background clears focus.
- When no element has focus, key input starts bubbling from the window (`Window`), so
  window-level `OnKeyDown` handlers, such as Escape-to-cancel in a dialog, work
  regardless of whether any element currently has focus.

## Tab Navigation

Tab moves focus to the next focusable element, and Shift+Tab moves focus to the previous
one. Traversal wraps back to the beginning when it reaches the end. Ordering rules:

1. The default order is the **visual tree order** (the order in which children were added).
2. Elements with an explicit `TabIndex` are visited **first in ascending order**, followed by
   elements without an explicit `TabIndex` in tree order.
3. Elements with the same `TabIndex` keep their tree order.

### TabIndex (double)

```csharp
new Button().TabIndex(1);      // Explicit order
new Button().TabIndex(1.5);    // Insert between 1 and 2 without renumbering
new Button().TabIndex(0);      // Valid first order, same as WPF/WinForms
```

- The default value is `double.NaN`, meaning "automatic (tree order)".
- Only finite values greater than or equal to 0 are treated as explicit order.
  Negative values, NaN, and infinity behave like automatic order.
- To return to automatic order, assign `double.NaN`, not 0.
- Difference from HTML: in HTML, `tabindex="0"` means document order, but in MewUI
  `TabIndex = 0` means "place first". To exclude an element from Tab traversal, use
  `IsTabStop = false` instead of a negative value.

### IsTabStop (bool)

```csharp
new Button().IsTabStop(false);   // Exclude only from Tab traversal
```

- When `false`, the element is excluded from Tab traversal only; it can still receive focus
  through mouse clicks or `Focus()` calls.
- This is only a filter. Setting `IsTabStop = true` on a non-focusable element, such as a
  Label, does not make it a Tab target. `Focusable` is the gate.
- Responsibility split: control authors decide which internal parts are excluded
  (constructor/style), while apps decide whether individual instances are excluded.

### Traversal Scope

- `TabControl` includes **only the active tab's content** in traversal. Inactive tabs are
  skipped. If the active tab contains no focusable elements, the TabControl itself becomes
  the tab stop.
- `TabIndex` values inside a scope are compared only within that scope. Nested scopes move
  as a single block in the outer order, and their internal order is not mixed with outer
  elements.
- Virtualized lists (`ListBox`, `TreeView`, `GridView`) can move focus to off-screen items
  with Tab, scrolling automatically when needed.

## Summary

| Goal | Method |
|---|---|
| Set tab order | `TabIndex(1)`, `TabIndex(2)` ... |
| Insert between two elements | `TabIndex(1.5)` |
| Exclude only from Tab, while preserving click focus | `IsTabStop(false)` |
| Prevent focus itself | Determined by the control type's `Focusable` value; cannot be set per instance |
| Return to automatic order | `TabIndex = double.NaN` |
