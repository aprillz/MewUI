# Control Templates (ControlTemplate)

## Overview

- A `ControlTemplate` **separates a control's visual tree into a replaceable definition**.
- The control keeps its public properties (logical state: `Content`, `Header`, ...) while the
  template owns the tree that is actually drawn and laid out (visual).
- **Templates are opt-in.** With `Template` null (the default) a control uses its own rendering
  path unchanged, with the same performance characteristics. Only controls with a template set
  run on the template path.
- However, some composite controls (currently `NumericUpDown`) are **default-template**
  controls whose theme default style supplies a template. They always run on the template path
  even without a local `Template` (see "Default templates" below).

```csharp
// Definition: a blueprint for the visual tree. Reusable across control instances.
var template = new DelegateControlTemplate<ContentControl>((owner, ctx) =>
{
    var presenter = new ContentPresenter();
    var chrome = new Border { Child = presenter };
    ctx.Bind(chrome, Control.BackgroundProperty);
    return chrome;                       // the return value is the visual root
});

host.Template = template;                // apply; the build happens lazily on the next measure
```

---

## Details

### <a id="model"></a>Definition and instance

- A `ControlTemplate` (the definition) is a stateless blueprint. Applying one definition to a
  hundred controls calls `Build` a hundred times, producing **a hundred independent trees**.
- The build output (visual root, part registry) is owned by the control instance. Storing
  elements on the definition object is forbidden - multiple controls would end up sharing one
  element.
- As an exception, a template used by a single window may capture field elements in the
  delegate - the **instance-only template** pattern (see the custom chrome example below). Such
  a definition cannot be reused on another control.

### <a id="lifecycle"></a>Application lifecycle

1. **Parts exist from `OnApplyTemplate` on.** Assigning `Template` only schedules the build
   (lazy, on the next measure); `GetTemplateChild` returns null in the constructor or right
   after the assignment.
2. **`Build` runs after the control is attached to the tree and its style is resolved.** It is
   therefore safe to read theme/DPI values inside `Build`; baking them in is fine because a
   theme change rebuilds the tree.
3. **Replacement and a theme swap are tree-death events.** The previous tree detaches (focus
   inside it is released safely) and a new tree builds on the next measure. Transient part
   state (in-progress text, scroll position, ...) is lost at that point, so **state that must
   survive belongs on control properties (logical), not on parts.**

Once applied, measure, arrange, render, hit testing and tree traversal all flow through **the
single template root**; the control's own visual implementation does not run.

### <a id="parts"></a>Named parts

Name parts during the build with `ctx.Register(name, element)`; the control implementation
caches them in `OnApplyTemplate`.

```csharp
public class Badge : Control
{
    private TextBlock? _label;

    protected override void OnApplyTemplate()
    {
        _label = GetTemplateChild<TextBlock>("Label");   // null when the template omits it
    }
}
```

- `ctx.Get<T>(name)` is for template authors (throws when missing); `GetTemplateChild<T>` is
  for controls (returns null - a template is allowed to omit the part).
- Registering the same name twice throws at build time.

#### Part interaction contracts (PART_*)

Beyond simple lookup, there are contracts where the control **wires its own behavior onto a
part registered under a well-known name**. The first case is `NumericUpDown.PART_TEXT_BOX`:
when a template registers a TextBox under this name, the control hands over the entire edit
pipeline - value <-> text synchronization, Enter/Escape commit/cancel, entering edit mode when
the part gains focus, committing when focus leaves.

```csharp
nud.Template = new DelegateControlTemplate<NumericUpDown>((owner, ctx) =>
{
    var editBox = new TextBox();
    ctx.Register(NumericUpDown.PART_TEXT_BOX, editBox);   // hands over the edit pipeline

    var display = new TextBlock();
    ctx.Bind(display, TextBlock.TextProperty, NumericUpDown.DisplayTextProperty);
    ctx.Bind(editBox, TextBox.IsVisibleProperty, NumericUpDown.IsEditingProperty);
    // ...layout omitted...
});
```

- The non-editing display binds the read-only `DisplayText` property (the formatted value
  string).
- Parts that need a reverse data flow (part -> control) are what this contract is for. For
  one-way visual reflection, `ctx.Bind` is enough and no contract is needed.
- When the part is not registered, the feature is simply disabled - no exception
  (`BeginEdit` becomes a no-op, for example).

### <a id="presenter"></a>ContentPresenter: projecting logical slots

A `ContentPresenter` marks the spot inside a template tree where "the control's content
appears here".

```csharp
new DelegateControlTemplate<ContentControl>((owner, ctx) =>
    new Border { Child = new ContentPresenter() });
```

- The control's `Content` stays **logically owned by the control**; the presenter provides
  the element's visual location. `host.Content` always answers with the real user content
  regardless of the template.
- **Without a presenter in the template, the content does not appear on screen.** The
  framework does not attach content to an arbitrary spot on its own. Logical ownership is
  still kept, so swapping in a template that has a presenter brings the content back.

#### The slot mapping mechanism

Presenters and control properties are connected in this order:

```text
1. The tree returned by the template build is attached to the control.
2. The framework walks that subtree and connects every not-yet-wired ContentPresenter to the
   owning control (based on "presenters created by this build", not name matching).
3. Each presenter reads the owner property its ContentSource points at and attaches the value
   as its visual child (pull).
```

`ContentSource` is not a string name but a **property descriptor reference
(`MewProperty<Element?>`)**. It formally states which slot to project.

```csharp
var headerPresenter  = new ContentPresenter { ContentSource = HeaderedContentControl.HeaderProperty };
var contentPresenter = new ContentPresenter();   // default: ContentControl.ContentProperty
```

Afterwards, when an owner slot property changes (`host.Content = other`, ...), the control
re-projects only the presenters whose `ContentSource` is that property. A Header change does
not touch the Content presenter.

- Multiple presenters with different slots are a normal composition (Header + Content, ...).
  **Two presenters pointing at the same slot are not supported** - an element can only exist
  in one place.
- Alignment of projected content follows MewUI's general rules: the content element's own
  `HorizontalAlignment`/`VerticalAlignment` apply within the presenter slot, and a template
  author directs alignment by setting it on the presenter itself.

### <a id="template-binding"></a>Template binding (ctx.Bind)

Template parts usually need to **visually reflect** properties of the owning control.
`ctx.Bind` is that connection: a one-way binding where the part's property follows the owner's
property - the counterpart of WPF's `TemplateBinding`.

```csharp
ctx.Bind(part, targetProperty, sourceProperty);   // the source is always the template's owner
ctx.Bind(part, property);                         // shorthand when both sides are the same property
```

- Applies the current value immediately at connection time and keeps forwarding changes.
- One-way (owner -> part) with no value conversion. The semantics are "the part reflects state
  the owner exposes"; a part that needs the reverse direction (an edit text box, for example)
  should have the control subscribe to part events in `OnApplyTemplate` instead.
- Released automatically when the template is torn down.
- Style triggers and transitions keep working: triggers write to the owner's properties, and
  the binding forwards every animation tick to the part, so color transitions stay smooth.

The typical use is chrome. A templated control **does not draw its default Background/Border**
(re-templating only works if the template owns the whole visual), so background and border are
drawn by a template part bound to the control's properties. The four-property bundle
(Background, BorderBrush, BorderThickness, CornerRadius) is declared with a single
`ctx.BindChrome` call - meaning "this part takes over the suppressed chrome":

```csharp
_pathHost.Template = new DelegateControlTemplate<ContentControl>((host, ctx) =>
{
    var chrome = new Border { Child = /* ... */ };
    ctx.BindChrome(chrome);
    return chrome;
});
```

### <a id="default-template"></a>Default templates: templates supplied by a style

`Template` is an ordinary property, so **a theme default style's setter can supply it** too.
Composite controls whose parts own real interactions use this. `NumericUpDown` is the first
case: a display TextBlock, a hidden edit TextBox and a RepeatButton spinner column are
supplied as the theme style's template, and the control itself has no drawing code.

```csharp
// inside the theme default style (framework internal)
Setter.Create(Control.TemplateProperty, (ControlTemplate?)NumericUpDownTemplate.Instance)
```

- **Precedence follows the value-store rules as-is**: a local `Template` beats the style
  template. An app can replace the default look wholesale with a single local assignment.
- **An explicit local `Template = null` means "reject the style template".** A
  default-template control has no drawing path of its own, so it becomes an empty control
  (standard lookless-control semantics).
- Styles carrying templates can also be supplied through `StyleName` or subtree `StyleSheet`
  type rules, so app- or screen-level look variations work within the style system.
- The criterion for which controls draw by default and which get default templates: **does the
  interaction only work when the part exists as a real control?** Primitives (TextBox, Button,
  CheckBox, ...) are drawing by nature and get no default template.

### <a id="state-parts"></a>The state-part pattern: switching presentation states

A control that must show different trees per state expresses this **by toggling template part
visibility, not by swapping content**. The file dialog's address bar (breadcrumb <-> path
input) is a real example.

```csharp
// Both states live as template parts; a state flag only toggles visibility.
_pathHost.Template = new DelegateControlTemplate<ContentControl>((host, ctx) =>
{
    var chrome = new Border
    {
        Child = new Grid().Children(_breadcrumb.CenterVertical(), _pathBox),
    };
    // ...chrome bindings omitted...
    return chrome;
});

// Enter: _breadcrumb.IsVisible = false; _pathBox.IsVisible = true; _pathBox.Focus();
// Exit: toggle back + move focus (hiding does not release focus, so move it explicitly)
```

Benefits of this pattern:

- Switching causes no detach, so part state (text, caret, ...) survives round trips.
- Presentation state (machinery) does not occupy the `Content` slot. `Content` is the place
  for user content injected from outside, not for the control's own presentation.

Caution: hiding (`IsVisible = false`) does not release focus. When hiding a focused part,
always move or clear focus explicitly.

### <a id="window"></a>Window templates: custom chrome

`Window` is a ContentControl, so it takes templates the same way. A window drawing its own
title bar is the representative case: the chrome (title bar, border) is owned by the template
and the app content projects into a `ContentPresenter` slot.

```csharp
public class ChromeWindow : Window
{
    public ChromeWindow()
    {
        Template = new DelegateControlTemplate<ChromeWindow>((window, ctx) =>
        {
            var titleText = new TextBlock().CenterVertical().Margin(8, 0);
            titleText.SetBinding(TextBlock.TextProperty, window, TitleProperty);

            var titleBar = new Border { MinHeight = 32, Child = titleText };
            titleBar.OnMouseDown(e =>
            {
                if (e.Button == MouseButton.Left)
                {
                    window.DragMove();
                    e.Handled = true;
                }
            });

            var chrome = new Border
            {
                BorderThickness = 1,
                Child = new DockPanel().Children(
                    titleBar.DockTop(),
                    new ContentPresenter()),
            };
            ctx.Bind(chrome, Control.BorderBrushProperty);
            return chrome;
        });
    }
}

// Callers use it like any window: Content is the app content's place, independent of chrome.
var window = new ChromeWindow { Content = appRoot };
```

- `Window.Content` is **always the app content** under any chrome. There is no need to push a
  chrome tree into Content and detour the real content through another property.
- For parts the window code must touch before layout (minimize/maximize buttons, ...), create
  the parts as constructor fields and let the delegate capture them - the instance-only
  template pattern (see "Definition and instance").
- A window without a template (the default) keeps the native frame. Same picture as WPF:
  standard windows use OS chrome, and only custom-chrome windows have a template own the
  visuals.

### <a id="logical-visual"></a>Logical and visual: what stays where

| Relationship | No template | Template applied |
|---|---|---|
| `content.LogicalParent` | the control | the control (unchanged) |
| `content.Parent` (visual) | the control | the `ContentPresenter` inside the template |
| chrome (background, border) | drawn by the control | owned by template parts |

- The developer tools (`Ctrl/Cmd+Shift+T`) Logical Tree mode shows only user-owned structure;
  the visual mode marks elements without a logical owner (template parts, presenters -
  machinery) as `[TypeName]`.

### <a id="pitfalls"></a>Pitfalls

- **Do not store elements on the definition.** `Build` must produce a fresh tree every time.
  (The instance-only template is the exception pattern and trades away reusability.)
- **Do not put machinery into `Content`.** The control's own presentation (chrome, per-state
  trees) belongs to the template. `Content` is the place for user content injected from
  outside.
- **Remember chrome suppression.** If the background/border disappeared after attaching a
  template, that is the signal to move those visuals into a template part and connect it with
  `ctx.Bind`.
- **Part lookup is only valid from `OnApplyTemplate` on.** The build is lazy, so there are no
  parts at constructor time.
- **There is one visual root.** Do not build implementations that draw or traverse elements
  outside the template through a separate path.
