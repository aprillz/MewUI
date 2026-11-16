# Hot Reload

This document explains **Hot Reload** for MewUI apps written in C# markup. When you edit code,
`dotnet watch` (or your IDE's Hot Reload) applies the change to the running app, and MewUI rebuilds
**only the nodes whose build code actually changed**.

Hot Reload works only in debug (JIT) sessions. It is fully disabled in release/NativeAOT builds.

---

## 1. Enable (no setup required)

MewUI registers the Hot Reload handler in its own assembly, so **your app needs no declaration.**
Just run under `dotnet watch`:

```
dotnet watch run
```

> The old `#if DEBUG [assembly: MetadataUpdateHandler(...)]` manual declaration is no longer needed.
> If you still have it, remove it to avoid a duplicate registration.

### Opting out

Disable Hot Reload from your project with:

```xml
<PropertyGroup>
  <MewUIHotReload>false</MewUIHotReload>
</PropertyGroup>
```

---

## 2. What each edit does, and how it works

What you edit determines **how much is rebuilt** and **which state survives**.

| What you edit | When it shows | Rebuild scope | State preserved |
| --- | --- | --- | --- |
| Window `.Build(...)`/`OnBuild()` / UserControl `OnBuild()` body | on save | that one window/control | everything **outside** that node (siblings, other windows) |
| Code a build runs: helper methods in any type of your app, constructors of controls it creates, overrides it calls | on save | every window/control whose build runs that code | everything outside those nodes |
| Event handler body (`.OnClick(...)`, `+=`, etc.) and the code only handlers run (view-model methods they call, for example) | on next event | **none** | **all** state |
| Rendering, layout, or input override of a custom control (`MeasureOverride`, `OnRender`, ...) | on save | none: the live instances of that control are re-measured and re-rendered | **all** state |
| Item template build function (`ItemTemplate`, etc.) | on save | the window/control whose build sets that template | everything outside that node |
| Any other app code (a method nothing in the UI calls, a class only startup code uses, ...) | on save | **every** registered build | everything outside the rebuilt nodes |
| Default style definition | on next rebuild | (rides along a rebuild) | — |
| Signature / structural change (rude edit) | after app restart | everything | none |

### Why it works this way

**1) Change detection follows the code a build runs.**

The runtime only tells us *which types* changed, and in C# markup build code and event handlers usually live in the same type. So for each registered build function MewUI records the methods that build runs: the methods it calls, the constructors of the objects it creates, the overrides those calls dispatch to, and the lambdas it runs while building, followed through your app's own debug-built assemblies. It records the code the build's event handlers run the same way, and keeps a baseline of every other method in the types either of them touches. On each change it compares the bodies of those methods, ignoring compiler noise such as metadata-token reshuffling, and rebuilds the nodes whose build runs a changed method.

**2) Editing a handler needs no rebuild.**

A lambda or method handed to an event subscription (an `OnXxx(...)` markup method or `+=`) is not run by the build, and neither is the code it calls. .NET replaces the method body in place, the delegate wired up during the build stays, and the next event runs the new body. The window is not rebuilt, and in-progress input, scroll position, and selection are preserved.

**3) A framework callback only needs a repaint.**

An override the framework calls on a live control, such as `MeasureOverride` or `OnRender`, already runs its new body the next time the framework calls it. MewUI invalidates the measure and the rendering of that control's live instances so that call happens now, without rebuilding anything.

**4) What cannot be traced re-runs every build.**

A changed method that no registered build and no handler runs (code reached through reflection or a delegate stored for later, a method nothing calls, a class only startup code uses) could still affect what is on screen, and MewUI cannot tell where. In that case it re-runs every registered build, as Blazor and Flutter do for every change, and the console line says which change caused it. Types the change adds never cause this: nothing ran them before, and the edited code that calls them is detected on its own.

**5) Only the shallowest affected node is rebuilt.**

When several nodes are affected, rebuilding an ancestor re-creates its children, so only the ancestor is rebuilt. Unaffected parts and other windows are left alone.

The same principles imply these limits:

- A delegate handed to anything other than an event subscription counts as build code. Editing it (a binding converter, for example) rebuilds the node that creates it, even if the delegate itself runs later.
- Code in libraries built in Release is not tracked.
- A control that builds its content in its constructor is refreshed by rebuilding the window or control whose build creates it. A window built directly in `Main` (`new Window().Content(...)`) has no build to re-run, so give it a `.Build(...)` callback or an `OnBuild()` override.
- Signature, base-type, or structural changes cannot be applied as a delta by the runtime (a rude edit) and require a restart.

---

## 3. Registering build code

Hot Reload rebuilds nodes whose build function is registered. The following are registered
automatically:

- **A Window's `.Build(...)`**: registers the callback and runs the initial build.
- **A Window's `OnBuild()` override**: runs right before the first show and is registered then.
- **A UserControl's `OnBuild()` override**: registered when the control builds its content.

### Build ownership

Exactly one place owns a window's build: **the `Build(...)` callback when set, otherwise the virtual `OnBuild()` hook.** The hook is not invoked on windows that have a callback, and attaching a callback to a window that overrides `OnBuild()` throws in debug builds. Rebuilds always re-run the current owner only.

Choosing an idiom: named reusable windows (dialogs) override `OnBuild()`; one-off windows at the composition site (the main window in `Main`) use the fluent `Build(...)`.

### Re-runnability rule

Build code (callback or override alike) is **re-run repeatedly** by Hot Reload and the editor preview. It must therefore contain only re-runnable configuration:

- Window specs that cannot change after the window is shown (`StartupLocation`, ...) throw on rebuild.
- Event subscriptions (`OnClosed(...)`, ...) get duplicated on every re-run.

Keep one-shot specs and subscriptions in the constructor or the composition-site chain (outside the build callback).

```csharp
public class AboutDialog : Window
{
    public AboutDialog()
    {
        // One-shot: window specs and event subscriptions
        this.Fixed(400, 340).StartCenterOwner().OnClosed(SaveState);
    }

    protected override void OnBuild()
    {
        // Re-runnable: same result no matter how often it runs
        this.Title("About").Padding(new Thickness(20)).Content(BuildUI());
    }
}
```

```csharp
var window = new Window()
    .Build(w => w
        .Title("Hot Reload Demo")
        .Content(new StackPanel()
            .Spacing(8)
            .Children(
                new TextBlock().Text("Edit code and save."),
                new Button()
                    .Content("Click")
                    .OnClick(() => Console.WriteLine("clicked"))   // editing this body -> no rebuild
            )));
```

Changing the `TextBlock` text rebuilds the window; changing the `OnClick` body applies on the next
click with no rebuild.

---

## 4. Full example

```csharp
using Aprillz.MewUI;
using Aprillz.MewUI.Markup;
using Aprillz.MewUI.Controls;

var window = new Window()
    .Build(w => w
        .Title("Hot Reload Demo")
        .Content(new StackPanel()
            .Spacing(8)
            .Children(
                new TextBlock().Text($"Now: {DateTime.Now}"),
                new Button().Content("Click")
            )));

Application.Create()
    .UseWin32()
    .UseDirect2D()
    .Run(window);
```

Run with `dotnet watch run`, then edit the `TextBlock` text to see the change applied immediately.

---

## 5. Notes

- **State inside a rebuilt node is reset.** Keep state you need to preserve in a view-model/service
  outside the build function. (Handler edits do not rebuild, so their state is preserved.)
- Hot Reload works only in debug (JIT); it does nothing in release/NativeAOT builds.
- Changes that arrive before the app starts running need no rebuild: the windows build with the edited code.
- Each applied change prints a `[MewUI HotReload]` line to the console error stream with the number of changed methods, rebuilt nodes, repainted elements, and handler-only changes, and the reason when every build was re-run. A rebuild that throws is reported there too; the other nodes are still rebuilt, and the failed node keeps its previous content and is retried with the next change.
